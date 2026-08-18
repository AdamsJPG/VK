# Planning — MSSQL Database Comparison Tool

## 1. Overview

A Windows desktop application that connects to two Microsoft SQL Server databases (**A** = original application, **B** = refactored presentation-layer version) and reports schema and data differences between them, while deliberately excluding identity/surrogate columns (IDs, UUIDs) and timestamps from the comparison (configurable).

Primary use case: verify that a presentation-layer refactor produced byte-for-byte identical business data by replaying the same actions against both systems and diffing the resulting databases.

## 2. Goals

- Compare schema: tables, columns, and data types present in A vs B.
- Compare data: row-level content differences between corresponding tables, ignoring excluded columns.
- Scale to large tables (millions+ rows) without loading full table contents into application memory.
- Let the user configure exclusions per table/column, with sensible type-based defaults.
- Produce three output forms: interactive in-app grid/tree, a shareable HTML report, and a raw export (CSV/Excel/JSON).

## 3. Non-goals (v1)

- Cross-vendor DB support (SQL Server only).
- Automatic schema reconciliation/migration generation.
- Real-time/continuous comparison (this is a point-in-time, on-demand diff).
- Comparing stored procedures, views, triggers, or other DB objects beyond tables/columns.

## 4. Architecture

**Stack:** WPF on .NET 10 (`net10.0-windows`), C#, MVVM (CommunityToolkit.Mvvm), `Microsoft.Data.SqlClient` for connectivity. (Deviated from the originally-planned .NET 8 — .NET 10 is the SDK installed and is itself the current LTS release, since even-numbered .NET releases are LTS.)

```
┌─────────────────────────────────────────────────────────┐
│ WPF UI (MVVM)                                            │
│  - Connection setup (A / B)                              │
│  - Exclusion rule editor                                 │
│  - Comparison run view (progress per table)              │
│  - Results browser (tree: tables → schema diff / row diff)│
└───────────────┬─────────────────────────────────────────┘
                │
┌───────────────▼─────────────────────────────────────────┐
│ Comparison Engine (class library, no UI deps)             │
│  - SchemaReader (per DB)                                  │
│  - SchemaComparer                                          │
│  - ExclusionRuleResolver                                    │
│  - TableHashComparer (per-table data diff)                 │
│  - RowDrillDownFetcher (fetch actual rows for mismatches)   │
│  - ComparisonRunner (orchestrates, parallel per table)      │
└───────────────┬─────────────────────────────────────────┘
                │
┌───────────────▼─────────────────────────────────────────┐
│ Report Generators                                          │
│  - HtmlReportWriter                                        │
│  - CsvExcelJsonExporter                                     │
└─────────────────────────────────────────────────────────┘
```

Connection profiles and exclusion rules persist as JSON under `%AppData%\DataCompare\profiles\*.json`. Credentials are never stored in plaintext — see §8.

## 5. Schema comparison

- Read schema via `INFORMATION_SCHEMA.COLUMNS` / `sys.tables`, `sys.columns`, `sys.types` (sys catalog views give more reliable type/precision/identity metadata than INFORMATION_SCHEMA).
- Diff dimensions per table: existence (added/removed table), column existence (added/removed), column type/length/precision/nullability changes.
- Output: a schema diff tree, independent of the data diff, always run first (data diff for a table is skipped/flagged if the table doesn't exist on both sides, or a warning is shown if columns differ, since column sets used in hashing must line up).

## 6. Data comparison algorithm (revised 2026-08-18 — see §6a for why)

**Primary path: primary-key streaming merge-join** (mirrors SQL Data Compare). For each table
present on both sides with a matching primary key (by column name):

1. **Column set resolution.** Common columns = intersection by name of source/target columns (per
   §7, exclusions will later remove noise columns from this set — not yet applied, see §6a).
   Key columns = the table's primary key, in key-ordinal order. Value columns = common columns
   minus key columns.

2. **Ordered SELECT on both sides.** `SELECT <key columns>, <value columns> FROM table ORDER BY <key columns>` run against source and target. No server-side hashing, no aggregation — when the
   primary key is also the clustering key (the common case), this ORDER BY costs nothing extra
   since the table is already physically sorted that way.

3. **Client-side merge-join.** Two forward-only `SqlDataReader`s are stepped through in lockstep,
   comparing key tuples: key present only in source → "missing from target"; only in target →
   "missing from source"; present in both → compare value columns in .NET, record as identical or
   changed. This is O(rows) time, O(1) memory (aside from the current row and a capped example
   list), and needs only one streaming pass per side — no round trips per row.

4. **Capped examples, exact totals.** Every category (missing-from-source/target, changed) tracks
   an exact running count plus a capped list of example rows for display, so a huge table never
   blows out memory but the summary counts are never silently wrong.

**Fallback path: full-row content hashing** (the original design, kept for tables with no usable
primary key — a key is required to align rows without transferring full content). Server-side
`HASHBYTES` per row, grouped into `(hash, count)` pairs, diffed as a multiset in the app; drill-down
queries fetch sample rows for reporting. See `TableHashComparer` / `ColumnHashExpressionBuilder` /
`RowDrillDownFetcher`. Known limitation: can't pair up *which* added row corresponds to *which*
removed row when content changes; only reports "N rows with this content are missing" / "M rows
with this other content are new."

**Parallelism** (done 2026-08-18, see §16): tables are compared concurrently, bounded degree of
parallelism, one connection pair per in-flight table.

### 6a. Why this changed

The original design (full-row fingerprint hashing for every table, chosen because IDs/UUIDs are
excluded and can't be trusted to align rows) hit a real timeout in testing. Root causes, in order
of impact:

1. **No exclusion rules exist yet** (deliberately — phase 4 comes after data comparison, see §11),
   so every column including identity/PK columns was being hashed. Since those are unique per row,
   `GROUP BY hash` collapsed almost nothing — the aggregation that was supposed to shrink the
   result set didn't, so the app ended up pulling back something close to one row per original row
   anyway, just as a hash instead of the real content.
2. Full-row hashing costs real SQL Server CPU per row (`HASHBYTES` + `CONCAT_WS` over every
   column) that a plain key-ordered scan doesn't.
3. Tables ran strictly sequentially with source/target reads also sequential per table.

The user confirmed the databases are seeded by replaying the same action sequence against A and B
(see the top of this document), which means primary keys — even surrogate/identity ones — *do*
correspond across the two databases in practice, even though their raw values aren't meaningful
business data. That makes PK-based matching valid here, unlike the general case this tool was
originally hedging against. The hash-based path is retained as a fallback for the genuinely keyless
case, not deleted.

**Caveat carried forward, not yet resolved:** the merge-join assumes both sides sort key columns
identically (matters mainly for string/collated keys — numeric/GUID identity keys, the common
case, aren't affected). Revisit if a table with a string or composite non-numeric key shows
spurious mismatches.

## 7. Exclusion rules

- **Defaults (type-based):** any column of SQL type `uniqueidentifier`, `datetime`/`datetime2`/`smalldatetime`/`date`/`time`/`datetimeoffset`, and any column that is an identity column or the table's primary key, is excluded by default.
- **Overrides:** stored per comparison profile as a JSON rule set — can add exclusions (e.g. a `LastModifiedBy` audit column) or remove a default exclusion (e.g. include a `datetime` column that's actually meaningful business data) per table or globally by column name pattern.
- Rule resolution order: global type rule → global name-pattern override → per-table override (most specific wins).
- The exclusion editor in the UI shows, per table, the resolved comparable column list before running a comparison, so the user can verify before committing to a run.

## 8. Connections & security

- SQL Server Authentication only (per decision). Username/password entered per connection profile.
- Credentials stored via Windows Credential Manager (`CredentialManagement` / native Win32 credential APIs), never written into the profile JSON in plaintext — the profile JSON stores a reference/target name, the actual secret lives in Credential Manager.
- Connections use encrypted transport (`Encrypt=True`) by default; certificate trust configurable per connection (needed for on-prem SQL Server with self-signed certs).

## 9. Output artifacts

1. **In-app grid/tree view:** results browser — table list with status icons (identical / schema diff / data diff / error), drill into a table to see schema diff details and the mismatched-hash sample rows.
2. **HTML report:** self-contained static HTML summarizing schema diffs and data diffs per table, generated after a run, suitable for sharing/archiving without the app installed.
3. **Export:** CSV/Excel/JSON export of the raw diff results (mismatched hash buckets + drill-down sample rows) for further processing.

## 10. Packaging

- MSIX or WiX-based MSI installer (decide during implementation based on target deployment — MSIX if Windows 10/11 only and app-store-style install is acceptable; WiX MSI if broader enterprise deployment/GPO install is needed). Default assumption: WiX MSI, since this sounds like an internal engineering tool likely deployed outside the Microsoft Store.

## 11. Implementation phases

**Reordered 2026-08-18:** data comparison moved ahead of exclusion rules, so exclusions can be
chosen from observed real diffs (noise columns visible in drill-down samples) rather than guessed
upfront. Data comparison v1 therefore hashes the full intersection of common columns — no
exclusions applied yet; that's the point of running it first.

1. **Foundation:** solution structure (WPF app + Comparison Engine class library + unit test project), connection profile model, Credential Manager integration, basic connect/test-connection UI. ✅
2. **Schema comparison:** SchemaReader, SchemaComparer, schema diff UI tree. ✅
3. **Data comparison engine (moved up):** server-side hash query builder over all common columns (no exclusions yet), group/count fetch, in-memory multiset diff, drill-down sample fetch, Data Comparison UI tab. Used diagnostically to decide what belongs in the exclusion list.
4. **Exclusion rules:** rule model, default type-based rules, per-table/column override editor UI, resolved-column-list preview — informed by what phase 3 showed as noise (IDs, timestamps, audit columns, etc). Data comparison is then re-run with exclusions applied.
5. **Comparison orchestration:** per-table parallel run, progress reporting, cancellation. ✅
6. **Results UI polish:** richer in-app grid/tree browser (beyond the phase-3 MVP tree).
7. **Report generation:** HTML report writer, CSV/Excel/JSON exporter.
8. **Packaging & installer:** MSI/MSIX packaging, versioning.
9. **Hardening:** large-scale performance validation against a millions-row test table, error handling for connection failures/permission issues/type-mapping edge cases.
10. **CLI / headless mode (requested 2026-08-18, not started):** see §14 — deferred until the GUI flow is solid.

## 12. Risks & open questions

- **Collation/locale differences** between A and B could make identical data hash differently (e.g. case sensitivity, trailing whitespace). Need a normalization step (e.g. explicit collation in the hash query) — to be validated against real schemas.
- **Floating point / decimal precision** differences could cause false mismatches; string casting in the hash expression needs a fixed, explicit format (e.g. `CONVERT(nvarchar, val, 2)` style with fixed precision) rather than default `CAST`.
- **NULL handling** in `CONCAT_WS` silently drops NULLs, which could make `('a', NULL, 'b')` and `('a', 'b', NULL)` hash identically — needs an explicit NULL sentinel per column, not `CONCAT_WS`.
- **Very wide rows / large text/blob columns** (nvarchar(max), varbinary(max)) may be expensive to hash server-side at scale — may need a size cap or separate large-object handling strategy.
- **Permission requirements** on both SQL Server instances need confirming (read access to `sys.*` catalog views, `HASHBYTES` requires no special permission but confirm on locked-down instances).
- **Table set mismatch** (table exists in A but not B, or vice versa) handling — reported as schema diff, data diff for that table skipped.
- Actual table/row-count numbers for the "millions+" databases aren't known yet — hash-group approach should scale, but should be load-tested against a representative table before committing to the final query design.

## 13. Confirmed decisions (from planning discussion, 2026-08-18; row matching revised same day — see §6a)

| Decision | Choice |
|---|---|
| Row matching strategy | **Primary-key streaming merge-join** (like SQL Data Compare) when a matching PK exists; full-row fingerprint hash/multiset diff as fallback for keyless tables |
| Exclusion configuration | Configurable per table/column, with type-based defaults |
| Output artifacts | In-app grid/tree + HTML report + CSV/Excel/JSON export |
| Tech stack | WPF, .NET 10 (`net10.0-windows`), C# — deviated from originally-planned .NET 8, see §4 |
| Data scale | Large (millions+ rows per table) — drives server-side hashing design |
| DB authentication | SQL Server Authentication |
| Schema diff scope | Yes — schema (tables/columns/types) + data |

## 14. Planned: CLI / headless mode (requested 2026-08-18, not started)

Explicit user requirement, deliberately deferred: "let's get things working first" — the GUI flow
needs to be solid before building a second entry point around the same engine.

**Intended shape:** invoke the app from the command line, passing connection parameters for both
source and target — server, database, user, password — for each side, and have it run schema
comparison and automatically write the HTML report (no window shown), rather than opening the GUI.
Data comparison is wired into the interactive flow now (re-enabled 2026-08-18 with real per-table
parallelism and cancellation, see below) but still has no HTML export of its own — headless mode
should track whatever the GUI supports at the time it's built, not get ahead of it.

**Why this should be straightforward when it's time:** the GUI already funnels everything through
`MainWindowViewModel` methods (`RunSchemaComparisonAsync`, `GenerateSchemaHtmlReport`) that don't
depend on WPF controls except for reading the two passwords out of PasswordBoxes — a CLI entry
point mainly needs to supply those two passwords from arguments instead, then call the same
methods and write the returned HTML to a path instead of opening a save dialog. Passwords over the
command line are visible in shell history/process listings, unlike the GUI's PasswordBox — worth
a design pass on secure input (e.g. reading from stdin or an env var) rather than a plain `--password` flag, when this gets built.

## 15. Deferred UI polish (noted, not scheduled)

- **Column widths don't persist manual resizes.** The schema results grid (`ListView`/`GridView`)
  recomputes all column widths proportionally on every window resize (see MainWindow.xaml.cs
  `SchemaListView_SizeChanged`), so a column the user manually drag-resized snaps back to the
  proportional layout the next time the window resizes. Flagged 2026-08-18; no fix scheduled yet —
  would need to track "user has customized this column" per column and stop auto-scaling it.
- **No elapsed-time display during long operations** (requested 2026-08-18, explicitly deferred —
  "don't do that now"). Would show how long Compare/data comparison has been running, likely on the
  `DataComparisonProgressWindow` popup (see §16) next to the per-table progress list.

## 16. Data comparison: parallelism, cancellation, progress popup (2026-08-18)

- **Per-table parallelism:** `RunDataComparisonAsync` runs up to `Clamp(ProcessorCount, 2, 8)`
  tables concurrently via `Parallel.ForEachAsync`, each on its own pair of `SqlConnection`s (a
  connection can't run more than one command at a time, so concurrency requires one connection pair
  per in-flight table, not a shared one). Results are collected with their original index and
  re-sorted before display, since completion order isn't the same as table order once parallel.
- **Real cancellation:** `CompareAsync` creates a `CancellationTokenSource` threaded through every
  SQL call in both schema and data comparison. The Engine methods (`KeyedTableComparer`,
  `TableHashComparer`, `RowDrillDownFetcher`, `SchemaReader`) already forwarded any token they were
  given into `ExecuteReaderAsync`/`ReadAsync` — the fix needed was actually supplying a real token
  from the UI instead of `default`. Cancellation is cooperative (sends a SQL Server attention
  signal), not instant.
- **Progress popup:** `DataComparisonProgressWindow` — a genuine separate window (unlike the
  Results screen, which was deliberately merged into the main window after user pushback) — opens
  when Compare now is clicked, lists every common table up front, and ticks each one off
  (`TableProgressItem.IsComplete`) as it's scanned, so what's outstanding is visible at a glance.
  Has its own Cancel button wired to the same cancellation mechanism. Each pending row shows an
  animated spinner (rotating dashed `Ellipse`, since a solid circle looks identical at every
  rotation angle) rather than a static glyph, swapping to a checkmark on completion.

## 17. Current state snapshot (end of session, 2026-08-18)

A from-scratch read of where the project actually stands right now, cutting across the
phase-by-phase history above.

**App identity:** named **VK** (renamed from a placeholder "Doppel" used during a same-day
naming/branding exercise — see git history/session log for the full rename scope). Custom icon: a
twin database-cylinder silhouette with disk-seam lines (`VkGeometry` / `VkIconFactory`), rendered
live for the window/taskbar icon and baked into `Assets/vk.ico` for the exe file icon. A splash
screen shows for ~1.4s on startup. One accent color (currently orange `#E8792A`) is defined once in
`AppTheme.cs` and used everywhere (banner, icon, buttons) — change it in one place to re-theme.

**Data Sources screen** (the former "Connections" tab, now the sole landing screen):
- Diagonal chevron banner — grey Source / accent-colored Target — each side showing its icon plus
  the live server name as it's typed.
- Fields: Server (text), Authentication (hardcoded to SQL Server auth via a disabled combo — ready
  to add Windows auth later without restructuring), User name, Password, **Remember credentials**
  (persists the password to Windows Credential Manager *and* auto-restores server/database/user +
  password on next launch via an implicit "last used" profile — no visible Save/Load UI exists),
  Encrypt / Trust server certificate, **Database** (editable combo + refresh button that queries
  `sys.databases` using the current credentials), **Test Connection**.
- →/⇄/← toolbar buttons copy or swap Source↔Target field values.
- **Compare now** is enabled once Server + User are filled on *both* sides — Database is
  deliberately not required (a blank one just uses the login's default database, since there's no
  way yet to discover valid names before connecting). Disabled while a comparison is running.

**Results screen:** replaces the Data Sources screen in the *same* window when Compare now
finishes (a separate popup was tried first and reverted after feedback — see §16 for the contrast
with the progress popup, which *is* deliberately separate).
- **Schema tab:** grouped `ListView`/`GridView` (Only in Source / Only in Target / Different /
  Identical), columns are user-resizable (drag the header border) and auto-scale proportionally on
  window resize (manual resizes don't persist through a subsequent resize — see §15). Selecting a
  row shows a side-by-side DDL diff (`TableDdlDiffBuilder`, Engine-level, shared with the HTML
  export) with changed lines highlighted green.
- **Export to HTML...** button generates a self-contained HTML report (`SchemaHtmlReportWriter`)
  with the same grouping and DDL diff, including full DDL for identical tables, not just names.
- **Data tab:** still the original plain `TreeView` from before the Schema tab was redesigned —
  deliberately not rebuilt yet ("mechanism first, UI later" was explicit user direction).

**Data comparison mechanism:** re-enabled in the Compare now flow (it was cut out for a while
because it looked hung with no feedback, not because it was broken — see §6a).
- Per-table parallelism and real cancellation, per §16.
- `DataComparisonProgressWindow` shows every common table with a spinner→checkmark per row.
- **No exclusion rules exist yet** (deliberate — see §6a) — expect most/all tables to show as
  "changed" until phase 4 is built from what this diagnostic run reveals.

**Not yet built / explicitly deferred:**
- **Exclusion rules** (phase 4, §7) — the next logical phase now that the data comparison
  mechanism itself works end to end.
- CLI / headless mode (§14).
- Column-resize memory, elapsed-time display (§15).
- CSV/Excel/JSON export — only HTML exists (§9 overstates this as already available; it isn't yet).
- Data tab UI polish (still the original tree, not the Schema tab's grid + DDL pane).

**Pending verification:** the progress popup's spinner change (rotating dashed `Ellipse` via
`RotateTransform`/`Storyboard`, replacing a static "○" glyph) is written but was not yet
build-verified at end of session — the app was running (locking the build output) when the change
was made. Rebuild and reopen the progress popup to confirm the spin actually renders before relying
on it.
