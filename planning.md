# Planning — MSSQL Database Comparison Tool

## 1. Overview

A Windows desktop application that connects to two Microsoft SQL Server databases (**A** = original application, **B** = refactored presentation-layer version) and reports schema and data differences between them, while deliberately excluding identity/surrogate columns (IDs, UUIDs) and timestamps from the comparison. Exclusions are configurable, per table, via right-click in the results grid — see §7 for the actual (simpler than originally planned) implementation.

Primary use case: verify that a presentation-layer refactor produced byte-for-byte identical business data by replaying the same actions against both systems and diffing the resulting databases.

## 2. Goals

- Compare schema: tables, columns, and data types present in A vs B.
- Compare data: row-level content differences between corresponding tables, ignoring excluded columns.
- Scale to large tables (millions+ rows) without loading full table contents into application memory.
- Let the user configure exclusions per table/column, with sensible type-based defaults. **Built** — see §7.
- Produce two output forms: interactive in-app grid/tree and shareable HTML/Excel reports, both generated in the same database pass "Compare now" already runs (no second comparison pass to export) — see §23.

## 3. Non-goals (v1)

- Cross-vendor DB support (SQL Server only).
- Automatic schema reconciliation/migration generation.
- Real-time/continuous comparison (this is a point-in-time, on-demand diff).
- Triggers, and any DB object beyond tables, views, functions, and stored procedures (the latter three were originally out of scope entirely — added later; see git/session history).

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

## 7. Exclusion rules — built (2026-09-30), simpler than originally planned; see §23

- **Defaults (type-based), built as originally planned:** any column of SQL type `uniqueidentifier`, `datetime`/`datetime2`/`smalldatetime`/`date`/`time`/`datetimeoffset` is excluded from value comparison by default, everywhere, with no configuration (`DataComparisonOrchestrator.AutoExcludedColumnDataTypes`). Primary-key/identity columns don't need a separate exclusion rule — they're never part of value comparison in the first place, since they're the merge-join key.
- **Overrides, built differently than planned:** no rule-model/JSON-config editor with global name-pattern rules was built. Instead: right-click any column in the Data comparison results grid → "Ignore this column" toggles that one column, for that one table, in `ComparisonProfile.ExcludedColumnsByTable`. No global-by-name-pattern override, no separate editor UI — simpler, and sufficient for the actual need (a handful of noisy audit columns per table, not a rule language).
- **No resolved-column-list preview UI was built.** The right-click menu label itself ("Ignore this column" vs. "Stop ignoring this column") is the only feedback on current state; there's no single screen listing every table's exclusions at a glance (see README's "Not yet built").
- **Custom match key (not in the original plan at all, added 2026-09-30):** for a table whose real primary key doesn't align rows meaningfully between Source and Target (e.g. a business-sequence number that drifted between environments after a data migration), right-click columns to build an alternate composite key (`ComparisonProfile.CustomKeyColumnsByTable`), then right-click the table's summary row to enable it (`CustomKeyEnabledTables`) — kept separate from the column list so re-enabling doesn't require re-picking columns. See §23 for the incident that motivated this.

## 8. Connections & security

- SQL Server Authentication only (per decision). Username/password entered per connection profile.
- Credentials stored via Windows Credential Manager (`CredentialManagement` / native Win32 credential APIs), never written into the profile JSON in plaintext — the profile JSON stores a reference/target name, the actual secret lives in Credential Manager.
- Connections use encrypted transport (`Encrypt=True`) by default; certificate trust configurable per connection (needed for on-prem SQL Server with self-signed certs).

## 9. Output artifacts

1. **In-app grid/tree view:** results browser — table list with status icons (identical / schema diff / data diff / error), drill into a table to see schema diff details and the mismatched-hash sample rows.
2. **HTML and Excel reports, built (2026-09-30):** both are streamed directly to scratch files during "Compare now" itself — the same database pass that builds the on-screen view also writes every discrepancy row straight to disk and discards it, so both reports always contain every row regardless of table size, with no second comparison pass needed to export. See §23 for why this replaced an earlier, simpler "export re-runs the comparison" design (it doubled the wait on a slow database) and the design before that (unbounded in-memory retention, which crashed on a table with widespread differences). Export buttons just copy the already-finished file.

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
4. **Exclusion rules:** ✅ (2026-09-30) — type-based defaults plus per-table/column right-click overrides; no separate editor UI or resolved-column-list preview was built (see §7, §23). Informed by exactly what phase 3 predicted: real production data revealed a table (`Invoice`) where the declared primary key itself was noise, which led to the custom-match-key feature (§7, §23) alongside plain column exclusion.
5. **Comparison orchestration:** per-table parallel run, progress reporting, cancellation. ✅
6. **Results UI polish:** richer in-app grid/tree browser (beyond the phase-3 MVP tree).
7. **Report generation:** HTML and Excel report writers. ✅ (2026-09-30 — Excel was originally dropped from scope, then built; see §9, §23)
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
| Exclusion configuration | Configurable per table/column, with type-based defaults — **built** 2026-09-30, right-click in the grid rather than a rule editor (§7) |
| Output artifacts | In-app grid/tree + HTML **and Excel** reports, both streamed during "Compare now" itself — **built** 2026-09-30 (§9, §23). Excel was previously explicitly dropped from scope; that decision was reversed. |
| Tech stack | WPF, .NET 10 (`net10.0-windows`), C# — deviated from originally-planned .NET 8, see §4 |
| Data scale | Large (millions+ rows per table) — drives server-side hashing design |
| DB authentication | SQL Server Authentication |
| Schema diff scope | Yes — schema (tables/columns/types) + data |

## 14. Planned: CLI / headless mode (requested 2026-08-18) — implemented 2026-08-19, see §22

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
- Structured export (CSV/JSON) — HTML is the only export format for now; not currently planned. Excel is explicitly dropped from scope (2026-08-21).
- Data tab UI polish (still the original tree, not the Schema tab's grid + DDL pane).

**Verified:** the progress popup's spinner (rotating dashed `Ellipse` via
`RotateTransform`/`Storyboard`, replacing a static "○" glyph) and the "Remember credentials"
round-trip (Windows Credential Manager persistence + auto-restore of server/database/user/password
on next launch) have both been click-tested and confirmed working — corresponding Trello
verification cards moved to Done on 2026-08-19.

## 18. Large-content columns: hash comparison + on-demand "Open both" drill-down (2026-08-19)

**Trigger:** a live diagnostic run against the actual `Invoicing`/`InvoicingFO` databases (RDS dev
server) was taking a very long time on two tables. Connected directly and checked, rather than
guessing: both `InvoiceLine` and `InvoiceReport` have matching primary keys on both sides (so both
already use the fast `KeyedTableComparer` path, not the `TableHashComparer` fallback — a missing/
mismatched PK was ruled out as the cause). The real causes turned out to be two different things:
- `InvoiceLine` — 53.6M rows, 16 scalar columns. Just genuinely large; the keyed merge-join is
  correctly O(n), there's simply a lot of n. No code change for this one — it's a volume problem,
  not a bug.
- `InvoiceReport` — only ~302K rows, but has a `Bytes VARBINARY(MAX)` column holding a per-row
  rendered document (confirmed by magic-number sniffing: `IsFrontPage=1` rows are real PDFs,
  `IsFrontPage=0` rows are `.xlsx` — a known, project-specific fact, not inferred generically).
  `KeyedTableComparer` was pulling and byte-comparing that column in full for every row on both
  sides — tens of GB transferred for a table whose row count looks trivial.

**Decision — hash comparison for MAX-length binary/text columns:** `LargeContentColumn.Is()`
(`src/DataCompare.Engine/DataComparison/LargeContentColumn.cs`) flags `varbinary(max)`/
`nvarchar(max)`/`varchar(max)` columns (`ColumnSchema.MaxLength == -1`). `KeyedTableComparer`'s
`BuildOrderedSelect` wraps only those columns in `HASHBYTES('SHA2_256', ...)` server-side — never
key columns, since a hash can't be used to align rows. Every other column, and every other table
(including `InvoiceLine`), is unaffected. Only a 32-byte hash crosses the wire per side per row for
these columns now, instead of the full value.

**Follow-up (2026-08-19, found while re-testing §19 live):** `dbo.CardTransactionXml` — only 4,652
rows, but still showed up as a straggler alongside `InvoiceLine`/`InvoiceReport`. Its `SourceXml`
column is SQL Server's native `xml` type (also `MaxLength == -1`, confirmed live), which
`LargeContentColumn.Is()` didn't recognize — only `varbinary`/`nvarchar`/`varchar` were covered, so
this column's full XML content was being pulled and compared in full per row, the exact same class of
bug as `InvoiceReport`'s blob, just a type the original fix didn't anticipate. Added `xml` to
`LargeContentColumn.Is()`; since `HASHBYTES` can't take `xml` directly, `BuildValueSelectExpression`
now wraps it in `CONVERT(nvarchar(max), ...)` first (same pattern `ColumnHashExpressionBuilder`
already used for other non-hashable types). Verified against real SQL Server
(`CompareAsync_XmlColumn_ComparesByHashAndDetectsChange`). **Lesson:** the original fix was scoped to
the one column it was built to fix rather than "any MAX-length column, whatever its type" — worth
double-checking a schema for other MAX-length types (`ntext`, `image`, `geography`, `geometry` are
plausible next surprises) rather than assuming the two known ones are exhaustive.

**Decision — "Open both" on-demand drill-down, scoped to varbinary columns only:** when a changed
row's differing column is a large-content column, the Data tab tree node shows what changed (not
the raw hash — that's meaningless to a user) and, for `varbinary(max)` columns specifically, an
"Open both..." action. Clicking it opens two fresh connections (the per-table connections used
during the compare pass are already closed by the time results are browsed — see
`_lastDataSourceProfile`/`_lastDataTargetProfile`/`_lastDataSourcePassword`/`_lastDataTargetPassword`
in `MainWindowViewModel`), re-fetches the real bytes for just that one row via
`LargeContentValueFetcher` (keyed by the row's primary key), writes each side to a temp file with an
extension guessed from the content's actual magic bytes (`FileSignatureSniffer` — `%PDF` → `.pdf`,
ZIP header → `.xlsx`, the only two formats known to appear in this project's data today; anything
else → `.bin`), and opens each via the OS default application (`Process.Start`,
`UseShellExecute = true`) for the user to eyeball side by side. `nvarchar(max)`/`varchar(max)`
columns are still hash-compared for the same performance reason, but get no "Open" action yet —
"open in the OS default app" only makes sense for binary file content.

**Decision — magic-byte sniffing, not the `IsFrontPage` column, decides the extension.** Initially
proposed keying the extension purely off `IsFrontPage` (1→pdf, 0→xlsx), since that's the known
business rule. Deliberately changed to sniffing the actual bytes instead: at the point the bytes are
already in hand to write the temp file, checking the real magic number costs the same as trusting an
out-of-band column, but can't silently drift out of sync with the content it's describing.

**Decision — anomaly check, drill-down-only for now.** If `IsFrontPage=1` but the sniffed bytes
don't look like a PDF, the status bar surfaces a warning after opening both files
(`DescribeFrontPageAnomaly` in `MainWindowViewModel`) — a cheap cross-check between a business-
meaning column and the actual content, since the two could drift apart (bad data, a future process
change) without either check alone catching it. **Explicitly scoped to drill-down time only**, not a
proactive scan — see the "possible future expansion" below for why.

**Possible future expansion (not built):** proactively pulling `SUBSTRING(Bytes, 1, 4)` alongside
the hash for every row of a table like `InvoiceReport` (cheap — 4 bytes, not the whole blob) would
let this anomaly check catch rows that are consistently mislabeled on **both** sides (identical hash
on both sides, so drill-down would never trigger — nothing looks "changed"). Deferred because the
immediate priority was reaching the diff/comparison mechanism itself, not exhaustive data-quality
scanning. Revisit once the core diff flow is validated end to end.

## 19. Large tables: deferred phase + range-partitioned parallelism (2026-08-19)

**Trigger:** after the §18 fix, re-testing against the live databases showed three tables still
taking a long time — `InvoiceLine` (53.6M rows), `EventLog` (3.9M rows), `InvoiceReport` (302K rows,
now hash-compared per §18). Checked `EventLog` directly the same way as §18: PK (`ID`) matches on
both sides, no MAX-length column — nothing structurally wrong, it's simply a multi-million-row table
going through a single-threaded merge-join. Both `InvoiceLine` and `EventLog` are the same category
of problem: real, large tables with nothing to *fix*, just something to parallelize better.

**Problem with the existing parallelism:** `RunDataComparisonAsync` already runs
`Parallel.ForEachAsync` across all common tables (`DataComparisonMaxParallelism`, `Clamp
(ProcessorCount, 2, 8)`), but that parallelism is *across* tables only — each table's own comparison
is single-threaded. A handful of huge tables each occupy one worker slot for their entire run, and
once the (usually much more numerous) small tables finish, the slots those huge tables aren't using
sit idle for the rest of the run.

**Decision — one flat job list, not nested parallelism, and not sequential phases either.**
Considered (and rejected) splitting each large table into N concurrently-running key-range chunks
*while still mixed in* with the existing per-table parallelism: that needs a second concurrency dial
(chunks-per-table) multiplying against the existing one (tables-in-flight), risking a connection-count
explosion.

**First attempt (shipped, then found broken by re-testing against live data):** classify tables by
approximate row count up front (`TableRowCountReader` — `sys.partitions`, instant, no scan), then run
comparison in two *sequential* phases sharing one concurrency cap: small tables first via
`Parallel.ForEachAsync`, then — only once that call fully returned — a second phase splitting each
large table into chunks. This looked like it resolved the "two dials multiplying" problem, but
re-testing against the real 33-table schema showed the progress counter plateau at "29 of 33" for a
long time. Checked the actual row-count distribution: only 2 of the 33 tables exceed the threshold
(`InvoiceLine` 53.6M, `EventLog` 3.9M); everything else is under 500K, including a few (`AuditLog`
484K, `InvoiceLineSummary` 326K) slow enough on the single-threaded merge-join to take real time. The
bug: because phase 2 only starts after phase 1's `Parallel.ForEachAsync` *fully* returns, the large
tables' chunking couldn't begin until literally every one of the other 31 tables finished — including
any slow-but-under-threshold straggler. That's worse than the original problem: previously
`InvoiceLine` ran immediately, concurrently with everything else; the phased version made it wait
behind the slowest small table instead.

**Fix (current design):** no phases at all — **one flat job list, one `Parallel.ForEachAsync` call**.
Before dispatch, small tables become one whole-table job each; each large table with a usable key has
its boundaries computed up front (a few quick indexed seeks — see below) and becomes
`LargeTablePartitionCount` = 5 key-range-chunk jobs; a large table with no usable key becomes one
whole-table job via the existing hash-fallback path. All of these — 31 small jobs plus up to 10
large-table chunk jobs in the current schema — go into a single list processed by one
`Parallel.ForEachAsync` with the existing cap. `Parallel.ForEachAsync` pulls the next queued item into
a freed worker slot the instant one finishes — it does not wait for the whole list to drain in
batches — so `InvoiceLine`'s and `EventLog`'s chunks start immediately alongside the small tables, no
barrier, no second concurrency dial, and no risk of one slow small table gating the large-table split.

**How a table gets split (`TableRangePartitioner`):** partitions by the *leading* primary-key column
only (works for both single-column keys like `InvoiceLine.ID` and composite keys like
`InvoiceReport`'s `(InvoiceNumber, InvoiceVersion, IsFrontPage)` — partitioning by `InvoiceNumber`
alone is still correctness-safe regardless of how the other key columns are distributed within a
range). Boundary values are sampled via indexed `OFFSET x ROWS FETCH NEXT 1 ROWS ONLY` seeks at
roughly-evenly-spaced row positions — cheap because the leading key is normally the clustered index —
rather than dividing the key's numeric range evenly, which skews badly whenever keys have gaps
(deleted rows, non-uniform inserts). `KeyedTableComparer.CompareAsync` takes an optional `KeyRange`
(one `(exclusive-lower, inclusive-upper]` slice) and adds it as a `WHERE` clause on both sides;
correctness never depends on the chunks being evenly sized, only on both sides using identical
boundaries. `KeyedTableDiffResult.Combine` merges the per-chunk results (sums counts, concatenates
capped example lists) into one result equivalent to comparing the whole table in a single pass —
verified directly against a real LocalDB table split into 4 chunks vs. compared whole (integration
test `TableRangePartitionerIntegrationTests`).

**Progress UI — chunk-level visibility (built 2026-08-19):** after the flat-dispatch fix, a live
re-test still plateaued at "31 of 33" for ~17 minutes with no way to tell whether `InvoiceLine`'s
chunks were legitimately grinding through ~10.7M rows each or something was actually stuck (it turned
out to be the former — the run did eventually finish). That ambiguity is exactly what per-chunk
visibility fixes, so it was built: `TableProgressItem` now carries an `ObservableCollection<
ChunkProgressItem>` (empty for ordinary tables — WPF's `TreeView` hides the expand arrow when
`ItemsSource` is empty, so a normal table's row renders identically to before). A partitioned table
gets one `ChunkProgressItem` per key range, labeled with its actual bounds (e.g. "Chunk 2 of 5 (ID
10,720,068 – 21,440,134)"), and the table's own summary line updates live to "Comparing 5 chunks
(3/5 complete)..." as chunks finish — reported via `IProgress<T>` the same way the rest of the
progress window already marshals background-thread updates back to the UI thread.
`DataComparisonProgressWindow.xaml`'s flat `ListBox` became a `TreeView` with a `HierarchicalDataTemplate`
for the table row and a smaller nested template for chunk rows.

**Elapsed-time display (built 2026-08-19, closes the separately-tracked backlog item):** the
17-minute plateau above could only be timed by checking the built DLL's file timestamp against wall-
clock time outside the app — there was no way to see it from inside VK itself. `MainWindowViewModel`
now runs a `Stopwatch` plus a 1-second `DispatcherTimer` for the whole `CompareAsync` span (schema
comparison through data comparison), exposed as `ElapsedTimeText` and shown in
`DataComparisonProgressWindow` under the status line ("Elapsed: 4m 12s"). The timer stops on
completion, failure, or cancellation but leaves the final value showing, so the total run time is
visible without needing to check anything outside the app.

**"Differences found" made unmissable (built 2026-08-19):** live-tested against real data, a
completed run's per-table summary correctly said "Differences found" for tables that actually
differed — but it was easy to read past (small, gray text identical in style to every other status
message). Given the whole point of this exercise is catching real differences, `TableProgressItem`
now carries a `HasDifferences` flag (set alongside `Summary` in the same `tableProgress` callback),
and a table with differences gets a red warning triangle instead of a green checkmark, bold red text,
a light red row background, and a continuous flash (`DataTrigger.EnterActions`/`ExitActions` with a
`BeginStoryboard`/`StopStoryboard` pulsing opacity) — deliberately loud rather than styled like every
other row, on explicit instruction that nothing here can be easy to miss.

**Root cause found for "VK shows everything as identical" (2026-08-19):** live-tested with a known,
independently-confirmed difference (`Client.ClientIdentifier='10351'` exists in `InvoicingFO`, not in
`Invoicing`) to check whether the comparison engine itself was wrong. It wasn't: running
`KeyedTableComparer.CompareAsync` directly against the real database (bypassing the app entirely, via
a throwaway console probe referencing `DataCompare.Engine`) correctly returned
`RowsOnlyInTarget.TotalCount: 1` with the exact row. The progress popup also correctly flashed
"Differences found" for that table while running. **The actual gap: `MainWindowViewModel.DataDiffNodes`
and the `DiffTreeNodeTemplate` resource in `MainWindow.xaml` (including the §18 "Open both" drill-down
action) were never bound to any `TreeView` or other control anywhere in the XAML** — confirmed by
grepping the whole App project. The Results screen only ever had one sub-view (the schema/DDL
comparison, under "Tables & views"), which correctly reports "Schemas are identical" for tables like
`Client` whose *structure* matches — a completely different claim from *data* being identical, but
easy to conflate when it's the only results screen that exists. Row-level data differences were being
computed correctly the entire time; there was simply nowhere in the app to see them once the progress
popup closed.

**Fix — a real Data comparison results screen (built 2026-08-19):** `MainWindow.xaml`'s Results screen
now has two switchable sub-views, matching how "Tables & views" already implied a second one should
exist: the existing schema/DDL comparison, and a new "Data comparison" sub-view showing `DataDiffNodes`
in a `TreeView` (using the `DiffTreeNodeTemplate` that already existed but was orphaned — the §18
"Open both" button now has somewhere to actually appear). Toggled via two new top-nav buttons
("Tables & views" is now enabled/functional rather than a disabled placeholder); Schema is the default
sub-view on landing, matching prior behavior.

**Follow-up bug found immediately after shipping the above (2026-08-19):** the new "Tables & views"/
"Data comparison" nav buttons only toggled which sub-view was visible *within* the Results screen —
they never actually brought the Results screen back into view if the user had navigated to Data
Sources first. Clicking "Data Sources" after a completed run was a one-way trip: nothing in the nav
could get back to the results (the data was still alive in `MainWindowViewModel`, just unreachable
through the UI), and the only way to see them again was re-running the entire comparison — costing a
real 20+ minute run. Fixed in `MainWindow.xaml.cs`: `ShowSchemaResultsSubView`/`ShowDataResultsSubView`
now each call `EnsureResultsBodyVisible()` first, so both nav buttons are genuine, unconditional
navigation destinations regardless of which screen was showing beforehand — clicking "Data Sources"
can never strand you away from a completed run's results again.

## 20. Data comparison grid, chevron-banner layout, and its own HTML export (2026-08-19)

**Trigger:** live-tested the new Data comparison tab and the two remaining rough edges were: (1) the
raw text-tree rows (`dbo.Client: source=4338, target=4339, matched=4338, changed=0, ...`) took real
parsing effort to scan, and (2) `Export to HTML...` — shared between both Results sub-views — always
generated the schema-only report, regardless of which tab was active, reproducing the exact
schema-vs-data confusion the Data tab itself was built to fix, this time in the export feature. User
also asked to carry the Data Sources screen's visual language (chevron banner, database-cylinder
icons, Server/Database labels) onto the results screens, plus grouped "rollup" sections matching the
Schema tab's existing "Identical (33)" grouping.

**Decision — a real per-table row exists for every table, not just differing ones.** Previously,
`CompareOneTableAsync` (and the large-table chunk-combine path) returned `DiffTreeNode? ` — `null` for
an identical table, meaning identical tables were silently dropped rather than represented anywhere.
That's fine for a tree that only needs to show what's different, but a grid with an "Identical tables
(N)" rollup section needs every table's row, whether it differs or not. Introduced
`DataComparisonRow` (`ViewModels/DataComparisonRow.cs`) and `TableComparisonOutcome` (`(DiffTreeNode?
Node, DataComparisonRow Summary)`) — both `CompareOneTableAsync` and the chunk-combine branch now
return the outcome pair; the tree node stays `null` for identical tables as before (nothing to drill
into), but the summary row is always populated. Collected into `DataComparisonRowsView`, an
`ICollectionView` grouped by `DataComparisonRow.GroupLabel` ("Tables with differences" / "Identical
tables"), sorted so the differences group shows first.

**Grid replaces the text-tree** in the Data comparison sub-view: `ListView`+`GridView` (same pattern
as the Schema tab), columns Table / Source rows / Target rows / Matched / Changed / Missing→Target /
Missing→Source. A row with any non-zero difference count gets the same unmissable treatment as the
progress popup (red background, bold red text) via `ItemContainerStyle` — deliberately whole-row
rather than per-cell, simpler and consistent with the popup's existing pattern. Selecting a row shows
its full `DiffTreeNode` detail (row examples, changed columns) in a pane below, mirroring the Schema
tab's DDL-diff-below-grid layout; an identical table's row has no detail to show.

**Chevron banner echoed onto the Results screens:** the plain "server / database" text boxes at the
top of both Results sub-views are replaced with the same diagonal-cut, grey-Source/orange-Target,
database-cylinder-icon banner used on the Data Sources screen — same geometry resources
(`DatabaseCylinderGeometry`/`DatabaseCylinderSeams`), same proportional-cut math, generalized into a
shared `UpdateChevronBannerCut(FrameworkElement, Polygon)` helper in `MainWindow.xaml.cs` so the two
banner instances (Data Sources' original, Results' new one) don't duplicate the resize logic.

**New, separate Data comparison HTML export:** `DataComparisonHtmlReportWriter` (Engine —
`Reporting/DataComparisonHtmlReportWriter.cs`, taking a new Engine-layer DTO
`DataComparisonTableSummary` rather than the App's `DataComparisonRow`, keeping Engine independent of
the UI) generates a sibling report to the schema one: tag-based Source/Target header, then the same
two rollup sections, non-zero counts in bold red. `Export to HTML...` in `MainWindow.xaml.cs` now
checks which sub-view is actually visible (`DataResultsSubView.Visibility`) and calls the matching
generator — schema and data reports are separate files now, never silently substituting one for the
other. Covered by `DataComparisonHtmlReportWriterTests` (grouping, non-zero styling, HTML encoding,
summary-line counts). Full suite: 84/84 passing; both projects build clean.

**Follow-up (2026-08-19): the HTML export still didn't match what was asked.** The first version of
`DataComparisonHtmlReportWriter` kept the old schema-report-style stacked `<div>` header (tag badges,
no left/right layout) and plain `<h2>` section headers with no actual collapse behavior — neither the
chevron-banner layout nor the rollup grouping had actually been carried into the HTML, only onto the
on-screen grid. Confirmed by reading the actual generated file the user attached rather than assuming
the earlier work covered it. Fixed: `BuildHeader` now renders a flexbox two-column banner (grey
Source panel left, orange Target panel right, a simple inline SVG twin-cylinder icon, Server/Database
text) mirroring the on-screen banner; `Generate`'s signature changed to take source/target
server+database separately rather than a pre-joined label so the writer can lay them out itself.
Sections now use native `<details>`/`<summary>` — "Tables with differences" open by default,
"Identical tables" collapsed by default — no JavaScript needed. Added
`Generate_Header_ShowsSourceAndTargetServerAndDatabase` and
`Generate_DifferencesSection_IsOpenByDefault_IdenticalSectionIsCollapsed` tests. Full suite: 86/86
passing.

## 20a. HTML export drill-down detail (2026-08-19 addendum)

The Data comparison HTML export only ever showed summary counts — no way to see *which* rows were
missing or changed, unlike the on-screen grid where selecting a table shows its full `DiffTreeNode`
detail below. Fixed: a new Engine-layer `DataComparisonDetailNode(Text, Children)` mirrors the shape
of the App's `DiffTreeNode` (plain text + children, no `ActionCommand`/`ActionLabel` — those need a
live DB connection and don't make sense in a static file) so the Engine project doesn't need to
depend on the App's view model type. `DataComparisonTableSummary` gained an 8th optional `Detail`
parameter (defaults to null, so existing calls/tests are unaffected). The ViewModel converts each
row's `DiffTreeNode` recursively via `ToDetailNode`. The HTML writer renders each differing table's
detail as a full-width row directly below its summary row, wrapped in its own `<details><summary>Show
row-level detail</summary>...</details>` — nested `<details>` for sub-groups with children (e.g.
"Rows with changed values (2)"), plain `<li>` for leaf examples — all native HTML, no JavaScript.
Identical tables never carry a `Detail` (nothing to drill into) so get no extra row. Covered by two
new tests (drill-down renders correctly; no detail row when `Detail` is null). Full suite: 88/88
passing, both projects build clean.

## 21. Coding standards retrofit (2026-08-19)

The CLAUDE.md coding standard (block-scoped namespaces, full XML docs on every member, Allman
bracing) was added mid-project, after most of the codebase already existed (see
`feedback_coding_standards_retrofit` memory — user deliberately deferred the retrofit until ready to
stop iterating on functionality). That point arrived this session. Full retrofit completed across all
75 `.cs` files in `src`/`tests`: bracing was already 100% compliant; every file-scoped namespace
converted to block-scoped; every previously-undocumented member given a full `/// <summary>` (+
`<param>`/`<returns>` as applicable). Regions explicitly **not** applied anywhere — this codebase is
records, static helpers, MVVM ViewModels, and WPF code-behind, none of which the standard's
CRUD-manager region taxonomy (Member Variables/Constructors/Create/Read/Update/Delete) fits; user
confirmed skipping regions entirely for these shapes rather than inventing a parallel taxonomy or
forcing a mismatched one. Full session-log entry for how this was executed (parallel-agent batch,
partial failures, manual completion of the remainder) — this section just records the standing
decision: **regions are not required in this codebase**, full stop, going forward.

## 22. CLI / headless mode implemented, `DataComparisonOrchestrator` extracted (2026-08-19)

Fulfils §14. Built as part of the "biggies" list (coding standards retrofit → CLI mode → CSV/JSON
export (low priority) → revert the temp table-skip hack, in that explicit order).

**Shared orchestrator, not a duplicate CLI implementation.** The CLI needed the same large-table
range-partitioning and keyed/hash comparison logic already living in `MainWindowViewModel
.RunDataComparisonAsync` — duplicating ~250 lines of concurrent partitioning logic for a second
entry point was judged a worse maintenance hazard than the one-time cost of extracting it, so (user
confirmed via explicit choice over "duplicate a simpler CLI-only version") the whole data-comparison
orchestration moved into a new `DataCompare.Engine.DataComparison.DataComparisonOrchestrator`:
schema/row-count discovery, the keyed-vs-hash-fallback decision per table, and range-partitioned
dispatch for tables ≥1M rows, all behind one `RunAsync(...)` call. It reports progress via four
plain-data callbacks (`IProgress<T>`, no WPF types) — an upfront table plan, per-large-table chunk
plans as boundaries are computed, per-chunk completion, and per-table completion — so the GUI's
progress popup keeps its existing live behavior (rows appearing immediately, chunks filling in
per-table, live "x/N complete" text) while the CLI can just ignore the callbacks it doesn't need
console output for.

**Layering consequence:** the orchestrator returns `DataComparisonTableSummary`/
`DataComparisonDetailNode` (the plain Engine-layer types §20a introduced for the HTML export)
instead of the App-layer `DiffTreeNode`. The on-screen "Open both..." drill-down action for a
changed large-content column — previously an `AsyncRelayCommand` baked directly into the tree node
— couldn't travel through a UI-independent Engine type, so a new `DataComparisonLargeContentAction`
record (table schemas + column name + row key) rides along on the detail node instead; the
ViewModel converts the Engine tree to its own `DiffTreeNode` tree once, wiring the real
`AsyncRelayCommand` back on wherever an action descriptor is present. Net effect: `MainWindowViewModel`
lost `RunDataComparisonAsync`'s ~250-line body and five now-unused fields/constants, gained two
short converter methods (`ToAppRow`, `ToAppDiffTreeNode`). Verified with a full rebuild (both
projects, 0 warnings/errors) and the full Engine test suite (88/88 passing) before and after.

**CLI shape** (`DataCompare.App/Program.cs`, `DataCompare.App/Cli/*`):
- `VK.exe /?` — prints usage help.
- `VK.exe /stub [path]` — writes a template request JSON (camelCase `mode`/`source`/`target`,
  placeholder values in every required field); refuses to overwrite an existing file at that path.
- `VK.exe <path-to-json>` — deserializes the request, runs schema and/or data comparison per
  `"mode": "schema" | "data" | "both"` (required, no default — a quick schema-only check on a huge
  database shouldn't silently also trigger a long data compare), writes `VK-Schema-Compare-*.html`
  and/or `VK-Data-Compare-*.html` next to the JSON file, using the exact same report writers the GUI
  uses.
- Passwords are **plaintext fields** in the request JSON — user's explicit choice, accepting the
  tradeoff, since CLI mode needs a fully non-interactive input with no "remember credentials" step.
  The GUI path is unaffected — it still never writes a password to disk.
- The process entry point moved from the WPF SDK's auto-generated `Main` (from `App.xaml`'s
  `ApplicationDefinition`) to a hand-written `Program.Main`, gated on `<StartupObject>` in the
  csproj and `App.xaml`'s build action changed from `ApplicationDefinition` to `Page` — the
  auto-generated one launches straight into the GUI with no chance to inspect `argv` first. No
  arguments → identical three lines (`new App(); app.InitializeComponent(); app.Run();`) the
  auto-generated `Main` used, so the GUI launch path is unchanged.
- The app stays `OutputType=WinExe` (no console-window flash on a normal double-click) rather than
  switching to console subsystem — CLI output uses `AttachConsole(ATTACH_PARENT_PROCESS)` so it's
  visible when launched from an existing terminal. Known tradeoff: cmd.exe/PowerShell don't wait for
  a WinExe process the way they wait for a console one, so scripting against it may need
  `Start-Process -Wait` rather than a bare call — accepted rather than giving every normal GUI launch
  a flashing console window.
- **Naming fix, same session:** the built executable was `DataCompare.App.exe` (the project's folder
  name) despite `AssemblyTitle`/`Product` already being "VK" everywhere else (window title, splash
  screen, HTML report filenames) — user caught the CLI help text referencing `VK.exe` while the
  actual binary was named differently. Fixed by adding `<AssemblyName>VK</AssemblyName>` to
  `DataCompare.App.csproj`; the build output is now `VK.exe`, matching branding everywhere.

**Left deliberately undone (per explicit instruction, biggies list order):** CSV/JSON export (§9
item 3, low priority) and reverting `TemporarilySkippedTablesForFasterIteration` (§17/entry (12) —
still hard-codes skipping `dbo.InvoiceLine`/`dbo.EventLog`/`dbo.InvoiceReport`) are both still
outstanding, the latter explicitly last so local iteration doesn't cost a 20+ minute full run per
change. User plans to verify the CLI mode end-to-end tomorrow before either of those.

## 23. Exclusion rules, custom match key, Excel export, and the memory/double-wait saga (2026-09-30)

A single long session, against the real `Invoicing`/`InvoicingFO` RDS databases, that built §7's
exclusion rules and Excel export (both previously deferred/dropped), then hit and fixed two serious
self-inflicted regressions along the way. Recorded in full because the false starts are as load-bearing
as the final design — the same mistake (unbounded in-memory retention) was made twice, once for
display rows and once for export, before the actual fix (stream, never buffer the whole result) stuck.

**Trigger 1 — exports were silently truncated.** The interactive detail tree capped example rows at
`MaxDiscrepanciesShownPerTable = 10` per category per table (a leftover from before exclusion rules
existed, meant to keep the drill-down list readable). Since the HTML export rendered from that same
capped tree, a table with more than 10 differences in a category silently hid the rest in the export
too — never acceptable for an artifact meant to be shared/archived. Fixed by raising the cap to
`int.MaxValue` (`TotalCount` was always tracked exactly regardless of the cap, so summary counts were
never wrong — only the drill-down example list was short).

**Trigger 2 — real production data exposed the actual noise: timestamp columns, and a table whose
primary key isn't a stable identity.** Live-tested against `Invoicing`, a `dbo.Invoice` row picked by
hand (`InvoiceNumber=255403`) appeared to match a *wrong* row in the target (same `InvoiceNumber`, but
`ClientIdentifier`/amounts completely different) — with the *actual* matching row (same client, same
amounts) sitting under a different key, `InvoiceNumber=255405`, in the target. Confirmed via the
table's DDL: `PK_Invoice PRIMARY KEY CLUSTERED (InvoiceNumber)` — a plain `int`, not an identity column,
i.e. an application-assigned business sequence number, not a stable surrogate key. The two environments'
`InvoiceNumber` sequences had drifted apart, so the keyed merge-join (§6) — which trusts the declared PK
completely — was correctly matching by key, but the key itself no longer identified the same real
invoice on both sides. This is not a comparison bug: a key-based diff tool has no way to know a key has
drifted without being told. Led directly to two built features:
- **Column exclusion** (§7): datetime-family columns excluded by default fixed the display-side of this
  same discovery (both `InvoiceDate` and `BatchCreated` differed between the two rows purely because of
  the environment drift, adding noise on top of the real problem).
- **Custom match key** (§7): lets a table be re-keyed by e.g. `(ClientId, PeriodFrom, PeriodTo)` instead
  of its real PK, for exactly this drifted-business-key case. Verified against real LocalDB
  (`CustomMatchKeyIntegrationTests`): confirmed the real PK produces the wrong pairing described above,
  and a custom key on the non-drifted columns produces the correct one.
- **Recompare this table** (right-click a table's row): re-runs just one table, not the whole database,
  for iterating on an exclusion/match-key change without paying for a full re-run each time.

**Excel export, built same session:** `ClosedXML` (MIT-licensed — the earlier "Excel explicitly dropped"
decision was about avoiding a paid dependency/scope creep, not a fundamental objection). One workbook,
a "Summary" sheet listing every table, and one detail sheet per differing table.

**Regression 1 — the uncapped export (Trigger 1's fix) crashed the app with `OutOfMemoryException`.**
Root cause: `DataComparisonRow.DetailNode` was built *eagerly*, for *every* differing table, immediately
after every "Compare now" — converting the engine's result into WPF objects (`DiffTreeNode`/
`GridColumnCell`, each carrying brushes/commands) even for tables the user never selects. With the cap
removed and `Invoice`'s key drift making most of its 152,462 rows look "changed," this eagerly built
millions of WPF objects for one table alone, for every compare, regardless of what was on screen.
First fix (real, but insufficient alone): make conversion lazy — `DataComparisonRow` now holds the raw,
unconverted `DataComparisonDetailNode`, converted to WPF objects only once a table is actually selected,
cached per table. Also deleted `MainWindowViewModel.DataDiffNodes`, which built the same expensive tree
for every table and turned out to be dead code — not bound to anything in the XAML at all.

**Regression 1, continued — lazy conversion didn't stop the crash, because the crash wasn't in the UI
layer.** `KeyedTableComparer`/`DataComparisonOrchestrator` themselves retained every discrepancy row as
a full .NET object (three dictionaries per changed row) in memory for the *entire* comparison pass,
before returning anything to the UI or an export — unbounded retention at the engine level, independent
of how the UI later converts it. Actual fix: reinstated a bounded cap for the interactive result
(`MaxDiscrepanciesShownPerTable = 2_000` — generous, not the original 10), and built true row-level
streaming: `KeyedTableComparer.CompareAsync` gained optional per-row callbacks
(`onRowOnlyInSource`/`onRowOnlyInTarget`/`onChangedRow`), invoked for *every* row found regardless of the
cap, reusing the object already constructed for the capped list rather than allocating twice. A caller
wanting "every row, never held in memory" passes `maxExamplesPerCategory: 0` plus the callbacks.
Verified directly against real SQL Server
(`CompareAsync_WithZeroCapAndStreamingCallbacks_RetainsNothingButStreamsEveryRow`): confirms the returned
result retains nothing while every row still reaches the callback.

**Regression 2 — export doubled the wait, on real remote (AWS RDS) databases.** The first streaming
design (`DataComparisonOrchestrator.StreamReportAsync`, since deleted) ran as a *second*, separate,
sequential (non-parallel) comparison pass specifically for export — meaning clicking Export after a
~20-minute "Compare now" triggered another full database re-scan, sequentially, with no progress
feedback, indistinguishable from a hang. (A real stuck-export report during this window was
independently confirmed *not* caused by this — no SQL blocking session was found — but the design was
already understood to be wrong regardless of that particular incident.) Real fix, and the one that
stuck: **one comparison pass produces everything.** `DataComparisonOrchestrator.RunAsync` (the same
method "Compare now" already called, with its existing parallelism and large-table range-partitioning
untouched) gained an optional `IDataComparisonRowSink? sink` parameter. Each table's rows are buffered
locally (as lightweight formatted `DataComparisonGridColumn` values, not the heavy per-row dictionaries)
while *that table* is being compared — bounding memory to however many tables are concurrently in
flight, not the whole database — then flushed to the sink under a shared lock the instant that table
finishes, and discarded. `StreamReportAsync` was deleted entirely once `RunAsync` could do both jobs
itself. `MainWindowViewModel.RunDataComparisonAsync` ("Compare now") now always streams complete
HTML/Excel reports to scratch temp files during the same pass; the Export buttons just `File.Copy` the
already-finished scratch file to wherever the user chooses — instant, no reconnect, no second pass.
CLI mode (§22) calls `RunAsync` with a sink directly — one pass produces both console output and both
report files, same as it always conceptually should have.

**Known, deliberate scope limit carried through both streaming designs:** a table with no usable primary
key (or disabled custom key) still uses the existing capped hash-fallback sample (§6's fallback path),
not true per-row streaming — reassigned-key reconciliation and hash-bucket grouping both need every
orphan row visible together to pair/bucket them, which is fundamentally in tension with discarding each
row immediately after streaming it. Not implicated in any of the above — `Invoice` and the other
problem tables all have a usable key and go through the fully-streamed path.

**Also fixed, same session:** the Functions & Stored Procedures and Tables & Views status
messages/reports could read as "0 objects exist" when a kind simply wasn't selected for that comparison
(Views/Functions/StoredProcedures all default **off** — only Tables defaults on). `DatabaseSchema`
gained an `ObjectTypes` field recording what was actually requested, so "not included in this
comparison" is now stated explicitly wherever a summary line or report would otherwise show a bare
"0 differences" for a kind that was never compared.
