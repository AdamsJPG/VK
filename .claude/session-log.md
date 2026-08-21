## 2026-08-21

**Goal:** Review the Refactonauts Trello board against everything actually completed on dataCompare
(per yesterday's explicit reminder), reconcile which QA cards are genuinely done, then chase down a
real feature gap the user found while manually testing a live comparison run.

**Done:**
- Audited every Trello card assigned to Joseph on Refactonauts against the actual code and session
  log. Identified #146 ("DB Comparison — revert model blocked") and #148 ("Spike: Database
  comparison") as belonging to a different repo entirely — the Tyrell/Playwright journey-validation
  spike (`src/db-compare/`), not this app — despite being on the same board.
- Confirmed #157 (CLI/headless mode) and #161 (elapsed-time display) were genuinely built (with
  file:line evidence — `Stopwatch`/`DispatcherTimer` in `MainWindowViewModel.cs`, `Program.cs`/`Cli/`
  for the CLI); user moved both to Done.
- #158 (originally "CSV/Excel/JSON export formats"): user asked to drop CSV and JSON "for now," which
  got wrongly narrowed to "Excel export format" by elimination — user hadn't asked for Excel at all
  and was happy with HTML staying the only format. Archived the card entirely and scrubbed Excel/
  CSV/JSON export references from `planning.md` (6 spots) and `README.md` to reflect HTML-only scope.
- User manually tested #166 (HTML export against a real schema diff) against two real databases and
  moved it to Done.
- Investigated #163 (Cancel actually stops in-flight SQL work): user measured a real 60+ second delay
  between clicking Cancel and the SQL Server request actually dying (via OS-level network-traffic
  monitoring, after `VIEW SERVER PERFORMANCE STATE` on the DMVs turned out to be denied). Root-caused
  to up to `MaxParallelism` (2–8) concurrent range-partitioned chunks each needing to independently
  observe cancellation, likely compounded by `KeyedTableComparer`'s `ORDER BY` forcing a server-side
  sort if the primary key isn't also the clustering key. Left open — no fix attempted, user chose to
  deal with it separately.
- User found a real, confirmed feature gap while reviewing an actual compare run (`dbo.InvoiceReport`,
  a row only in Source): the PDF-sniff-and-render "Open both..." feature (planning.md §18) only
  existed for the "Changed" row category (present on both sides, differing hash) — rows in "Only in
  Source"/"Only in Target" just printed static `"(large column)"` text with zero action wired.
  Fixed: extended `DataComparisonLargeContentAction` with `IncludeSource`/`IncludeTarget` flags, wired
  a single-side "view content" action into `DataComparisonOrchestrator.BuildRowExampleNodes`, and
  renamed/rewrote `OpenLargeContentBothAsync` → `OpenLargeContentAsync` in `MainWindowViewModel` to
  fetch/open only the side(s) that actually exist. Caught a real correctness bug while building this:
  the row's dict for these examples stores a large-content column as its *hash*, not its real bytes,
  so the re-fetch `WHERE` clause has to strip large-content columns out first or it would compare real
  bytes against a hash and never match. Engine + Engine.Tests build clean; App project confirmed clean
  after the user closed the running `VK.exe` for the copy step.

**Decisions:**
- Excel dropped from export scope entirely (not just deferred alongside CSV/JSON) — HTML remains the
  only output format for now, per explicit user correction after I wrongly assumed Excel was the
  target.
- #158 archived rather than kept open/reworded — no active plan to build any new export format.
- #163 left open with root cause identified but unfixed, by explicit user choice.

**Left to do:**
- #163 Cancel latency (60+ seconds on large partitioned tables) — root cause identified, not fixed.
- #156 Exclusion rules, #160 Column-resize memory — still entirely unbuilt.
- #159 Data tab UI polish — still partial: the rollup grid now matches the Schema tab, but the detail
  pane is still a `TreeView`, not the Schema tab's DDL-style side-by-side pane.
- #164 Verify per-table parallel comparison correctness, #169 Verify Data Sources ↔ Results toggle —
  still need live verification.
- The new single-side "view content" fix for only-in-source/only-in-target large-content rows has not
  yet been manually tested against a live run — user to verify against the rebuilt `VK.exe`.

**Patterns noted:**
- Narrowing a multi-option card by elimination ("drop X and Y") does not mean the remaining option is
  wanted — ask explicitly before renaming/rescoping. Cost a full round trip here (see
  `feedback_dont_infer_scope_from_elimination` memory).
- When a live-behavior claim needs verifying, distinguish UI-level evidence from server-level evidence
  before treating something as confirmed — but once the user has actually measured/observed something
  themselves (e.g. the 62-second cancel delay), take it as fact immediately rather than continuing to
  ask for more proof. Repeated requests to quantify something already reported read as accusatory to
  the user this session and were called out directly.

## 2026-08-20

**Goal:** Continue the "biggies" from session (13): CLI polish first, then a long run of user-facing
Data comparison features that emerged organically through the session (reassigned-key detection, a
colored comparison grid, a temporary Accept-changes feature, percentage-difference metrics), finishing
by reverting the `TemporarilySkippedTablesForFasterIteration` dev hack per explicit "let's bring these
tables back."

**Done:**
- CLI polish: colored console output (green/red for identical/differs, matching schema and data
  summaries), a leading newline before every "Report written to" line, "Table changes found" →
  "Schema changes found", and mirrored the UI's `TemporarilySkippedTablesForFasterIteration` skip into
  the CLI (later reverted — see below). Fixed a real build-breaking typo caught by the user
  (`yshasTarget` — a stray prefix on `hasTarget`) and, later in the session, a missing semicolon
  introduced by an external edit to the same file — both confirmed as genuine on-disk issues via the
  actual compiler error, not assumed.
- WPF progress popup: schema-only "no matching table" rows now show live (orange, flashing, "in
  Source"/"in Target" labels) alongside the existing red data-differences flashing, instead of only
  surfacing once the popup closes.
- Actually implemented the "Export Schema/Data Report to HTML..." dynamic button label — this had only
  been proposed and agreed earlier, never built; caught when the user pointed out both buttons still
  said the generic "Export to HTML...".
- Built full reassigned-primary-key detection: SHA-256 content-hash reconciliation over non-key
  columns (min-count pairing for duplicate content) in `KeyedTableComparer`/`KeyedTableDiffResult`,
  wired through `DataComparisonOrchestrator`, a new "Reassigned key" summary column, and a drill-down
  category — deliberately scoped to the keyed-comparison path only (same boundary as the existing
  keyed-vs-hash-fallback split).
- Iterated the row-detail comparison grid through several wrong layouts (plain text → two stacked
  source/target rows → single-line "column: source → target" chips) before the user drew an actual
  picture of the intended layout: one header row (column names shown twice, source group then target
  group), one data row with source values on the left and target values on the right, yellow for the
  reassigned key, green for matched, red for real differences (`DataComparisonGridCellKind`). Applied
  to all four detail categories (reassigned-key, changed-values, only-in-source, only-in-target), with
  "Source"/"Target" group-header labels added afterward, in both the WPF TreeView and the HTML export.
- Extracted `ReportBannerBuilder` so the Schema HTML report now gets the same grey-Source/orange-Target
  banner-with-DB-icon as the Data comparison report, instead of its older plain tag-pill header.
- Built a temporary, explicitly non-persisted "Accept" feature as JavaScript embedded directly in the
  shared `DataComparisonHtmlReportWriter` (covers both the CLI and the WPF export automatically, since
  both funnel through the same writer and the CLI has no interactive UI of its own) — per-item Accept
  buttons, a per-table "Accept all" that correctly folds the display-cap overflow into a new, separate
  "Accepted" column (kept apart from "Matched", which stays meaning literally identical), and
  de-highlighting once a table's outstanding differences all reach zero. No localStorage, no
  persistence — user's own reasoning: this tool reruns across many refactor iterations of the same
  migration, and persisting acceptance risks a real regression silently sneaking through because it
  matches an old accepted signature.
- Added three-tier percentage-difference metrics: `SchemaDiffResult.ComputeDifferencePercentages`
  (table-count-level and column-count-level, shown as a genuinely separate summary line under the
  Schema screen/report's existing counts sentence — not folded into it, per explicit correction) and
  `DataComparisonTableSummary`/`DataComparisonRow.PercentDiffers` (a new "% Differs" grid column on the
  Data screen/report).
- Reverted the `TemporarilySkippedTablesForFasterIteration` hack entirely — deleted from both
  `MainWindowViewModel.cs` and `CliRunner.cs` — so `dbo.InvoiceLine`/`dbo.EventLog`/`dbo.InvoiceReport`
  are back in every comparison run.
- Test suite grew from 95 to 108 passing across the session (new LocalDB integration tests for
  reassigned-key detection, unit tests for the grid rendering, percentage math, and Accept markup).
  Both projects verified building clean multiple times; the App project's final copy step was blocked
  by the running `VK` process on three separate occasions this session (expected, not a code issue).

**Decisions:**
- Grid color semantics are a shared three-state enum (`DataComparisonGridCellKind`: Matched/
  ExpectedDifference/RealDifference) rather than a bool, so the same rendering code serves both the
  "expected, ignorable" yellow (reassigned key) and the "real problem" red (changed values) cases.
- Accept is JS-only, embedded once in the shared HTML writer — explicitly not a separate WPF feature —
  and explicitly not persisted across report regenerations. Both were the user's own calls.
- "Accepted" is its own summary column, never folded into "Matched" — confirmed explicitly when asked,
  since accepting a difference doesn't make the row literally identical.
- Percentage-difference summary line is genuinely separate from the existing counts sentence on the
  Schema screen, not appended to it — user's explicit correction after I first assumed folding it in.

**Left to do:**
- Nothing built this session has been manually click-tested against live data yet (colored grids,
  Source/Target labels, the Accept feature in a real exported HTML report opened in a browser,
  percentage lines/columns) — only unit/integration-tested. User to verify against a real run.
- **Tomorrow, per explicit user request:** review the Trello board against everything actually
  completed this session and prior ones — several features here (reassigned-key detection, the
  comparison grid, Accept-changes, percentage differences) emerged organically mid-session and were
  never on the board at all, so expect it to be significantly stale in both directions (some cards
  done that aren't marked, some real work with no card yet).
- Still open from earlier sessions, unchanged: CSV/JSON export, real exclusion-rules design/
  implementation (the tool's stated core purpose — excluding timestamp/UUID noise columns — still not
  built), column-resize memory.

**Patterns noted:**
- When a UI layout keeps not landing after one or two verbal-description attempts, ask the user to
  sketch/draw it rather than keep guessing — a single pair of screenshots (what was built vs. what was
  intended) settled the comparison-grid layout immediately after three rounds of text-based iteration
  had each landed on the wrong structure.
- A feature discussed and agreed upon in conversation is not the same as a feature actually built —
  caught implying the export-button rename had been done when it had only been proposed; always verify
  current file state before treating an earlier agreement as completed work.
- `VK.exe` being open blocks only the final copy step (a file lock), not compilation — check for that
  specific error signature before concluding a change introduced a real problem, and simply ask the
  user to close the app rather than investigating code that isn't actually broken.

## 2026-08-19 (13)

**Goal:** Continue the "biggies" list after the coding standards retrofit: full CLI/headless mode
(JSON in, HTML out, plaintext credentials, `/?` and `/stub` flags — user's explicit design), with the
table-skip hack and CSV/JSON export both deliberately left for later.

**Done:**
- Resolved a report-format confusion first: user's screenshot showed the Data comparison HTML export
  working correctly (banner + rollup), but the separately-attached `123.html` was actually the
  **Schema** report (`SchemaHtmlReportWriter`, a different writer that never got the banner/rollup
  treatment — that was only ever requested for the Data comparison export). Nothing had regressed;
  two different files were being compared against one expectation.
- Extracted `MainWindowViewModel.RunDataComparisonAsync`'s ~250-line orchestration (schema/row-count
  discovery, keyed-vs-hash decision, range-partitioned dispatch for large tables) into a new
  `DataCompare.Engine.DataComparison.DataComparisonOrchestrator`, chosen over a duplicate CLI-only
  implementation after user picked "extract to shared orchestrator" explicitly. Added
  `DataComparisonLargeContentAction` so the Engine-layer detail tree can still carry the "Open
  both..." drill-down action without depending on WPF's `ICommand`. Full detail in planning.md §22.
- Built CLI mode: `Program.cs` (custom entry point via `<StartupObject>`, `App.xaml` build action
  changed from `ApplicationDefinition` to `Page`), `Cli/CliRunner.cs`, `Cli/CliComparisonRequest.cs`,
  `Cli/CliConnectionSpec.cs`, `Cli/CliComparisonMode.cs`. Verified `/?`, `/stub` (including the
  no-overwrite guard), and the missing-file error path all work correctly via direct process runs.
- Fixed a naming mismatch user caught: the built exe was `DataCompare.App.exe` despite the CLI help
  text (and all existing branding — window title, splash, HTML report filenames) saying "VK". Added
  `<AssemblyName>VK</AssemblyName>`; build output is now `VK.exe`.
- Full rebuild (both projects, 0 warnings/errors) and full Engine test suite (88/88 passing)
  confirmed after the orchestrator extraction, before starting the CLI work on top of it.

**Decisions:**
- Shared orchestrator over duplicated CLI logic (planning.md §22) — user's explicit call given the
  regression risk vs. maintenance-drift tradeoff.
- CLI request JSON: required `mode` field (`schema`/`data`/`both`, no default), plaintext passwords
  (user's explicit choice), HTML report(s) written next to the input JSON, `/?` and `/stub` flags.

**Left to do:**
- **CSV/JSON export** (low priority, §9 item 3) — not started.
- **Revert `TemporarilySkippedTablesForFasterIteration`** — deliberately still in place; user will
  verify CLI mode end-to-end tomorrow before this is reverted (must be last — see entry (12)).
- **User reminder:** check the Trello board's state against everything actually completed this
  session (and the sessions before it) — cards may be stale relative to real progress.

**Patterns noted:**
- When a user's bug report includes two separate pieces of evidence (a screenshot + a referenced
  file), don't assume they describe the same thing — check each independently before concluding
  anything regressed. Reading the actual `<title>`/`<h1>` of the referenced file settled it here.

## 2026-08-19 (12)

**Goal:** Full runs take 20+ minutes because of `InvoiceLine`/`EventLog`, making UI/reporting
iteration painfully slow. User explicitly asked to hard-code skipping those two tables for now.

**Done:**
- Added `TemporarilySkippedTablesForFasterIteration` (`["dbo.InvoiceLine", "dbo.EventLog"]`) in
  `MainWindowViewModel`, filtered out of `commonTables` right after it's built. Marked with a loud
  `⚠ TEMPORARY DEV-ONLY HACK — REMOVE BEFORE SHIPPING ⚠` comment block explaining it's purely a
  dev-loop speed hack, unrelated to exclusion rules or any correctness decision.
- Full suite: 86/86 passing. App project builds clean (app was closed).

**⚠ MUST REMOVE BEFORE SHIPPING ⚠** — `TemporarilySkippedTablesForFasterIteration` in
`MainWindowViewModel.cs` hard-codes skipping `InvoiceLine`/`EventLog`/`InvoiceReport` (added
`InvoiceReport` shortly after this entry — same reason, still too slow for quick iteration) from
every comparison run. This is a local dev-iteration speed hack only, not a real feature — flagging
here explicitly so it isn't mistaken for an exclusion-rules decision and doesn't slip into anything
resembling a real build.

**Left to do:** remove the temporary skip list once done iterating on UI/reporting changes; everything
else unchanged from entry (11).

## 2026-08-19 (11)

**Goal:** User checked the actual generated Data comparison HTML export and found neither of the two
things asked for — the chevron-banner header and collapsible rollup sections — had actually made it
into the file.

**Done:**
- Read the attached generated HTML directly rather than assuming the earlier work covered it —
  confirmed it still had the old stacked tag-badge header and plain non-collapsible `<h2>` sections.
  The on-screen grid and banner got built; the HTML export writer itself was never actually updated
  to match, despite the plan saying it would be.
- Rewrote `DataComparisonHtmlReportWriter.BuildHeader` as a flexbox two-column banner (grey Source
  left, orange Target right, inline SVG twin-cylinder icon, Server/Database text) — changed
  `Generate`'s signature to take server and database separately per side instead of a pre-joined
  string, so the banner can lay them out on its own lines.
- Replaced the plain `<h2>` section headers with native `<details>`/`<summary>` — "Tables with
  differences" open by default, "Identical tables" collapsed — no JavaScript.
- Updated `MainWindowViewModel.GenerateDataComparisonHtmlReport` for the new signature, updated all
  existing writer tests for the new 5-arg signature, added two new tests for the banner content and
  the open/collapsed default states. Full suite: 86/86 passing. Engine builds clean; App project's
  C# compiled fine, final copy step blocked by the still-running `VK` process as usual.

**Decisions:** none new — a direct fix for a real gap between what was promised and what shipped.

**Left to do:**
- User to close the app, rebuild, and confirm the HTML export now actually has the banner and
  collapsible sections.
- Everything else unchanged from entry (10).

**Patterns noted:**
- Claiming a feature is "done" for two related surfaces (on-screen + exported file) without
  independently checking both is exactly the kind of gap a user will find immediately — worth
  explicitly verifying every stated deliverable's actual output, not just the one most convenient to
  check, before reporting something as complete.

## 2026-08-19 (10)

**Goal:** After the Data comparison tab proved out (correctly showing `Client`/`EventLog`/
`InvoiceLine` differences), redesign it properly: a real grid instead of raw text, the Data Sources
screen's chevron-banner visual language carried onto the Results screens, and a genuinely separate
Data comparison HTML export (the existing "Export to HTML..." button was found to always generate the
schema-only report, even from the Data tab — same schema-vs-data mixup as before, now in exports).

**Done:**
- Walked through the design with the user before building anything (explicit "take no action yet"
  repeated twice): confirmed the chevron-banner layout via screenshot, confirmed a real full-list
  screenshot check that chunk-level progress display was in fact working correctly on
  `EventLog`/`InvoiceLine` (false alarm — user had looked at the wrong screenshot/scroll position
  first), then got explicit "do them all" before writing code.
- Introduced `DataComparisonRow` and `TableComparisonOutcome` — every table now gets a summary row
  (source/target/matched/changed/missing-from-target/missing-from-source counts) whether it's
  identical or not, not just the differing ones `DiffTreeNode` already covered. Needed for the new
  "Identical tables (N)" rollup group to actually list every identical table.
- Replaced the Data comparison tab's text-tree with a `ListView`+`GridView` (matching the Schema
  tab's existing pattern), grouped into "Tables with differences" / "Identical tables", non-zero
  difference counts styled with the same unmissable red/bold treatment as the progress popup
  (whole-row, not per-cell). Selecting a row shows its detail (row examples, changed columns) below,
  mirroring the Schema tab's DDL-diff-below-grid layout.
- Echoed the Data Sources screen's chevron banner (diagonal cut, database-cylinder icons, grey-
  Source/orange-Target) onto both Results sub-views, replacing the old plain text boxes. Generalized
  the banner resize math into a shared helper so the two banner instances don't duplicate it.
- Built a genuinely separate `DataComparisonHtmlReportWriter` (Engine layer, its own DTO
  `DataComparisonTableSummary` to keep Engine independent of the App's view models) producing a
  sibling HTML report with the same rollup grouping and red-highlighting. `Export to HTML...` now
  checks which Results sub-view is actually visible and calls the matching generator.
- Added `DataComparisonHtmlReportWriterTests` (grouping, non-zero styling, HTML encoding, summary
  counts). Full suite: 84/84 passing. Both projects build clean.
- Full writeup in planning.md §20.

**Decisions:**
- Whole-row red/bold styling for grid differences (not per-cell) — simpler than per-cell `CellTemplate`
  triggers and consistent with the progress popup's existing pattern, at no real loss of clarity.
- New Engine-layer DTO (`DataComparisonTableSummary`) for the HTML writer rather than reusing the
  App's `DataComparisonRow` directly — keeps the Engine project's existing independence from the UI
  layer (it has no reference to `DataCompare.App` and this preserves that).

**Left to do:**
- User to close the app, rebuild, and confirm the new grid/banner/export all look and work as
  intended against a real run.
- The remaining backlog items are unchanged from prior entries (proactive anomaly-scan idea for §18,
  checking for other MAX-length column types, elapsed-time-per-table display if ever wanted beyond
  the overall run timer already built).

**Patterns noted:**
- Reading the actual exported/attached file before answering a claim about it (the HTML report) once
  again beat reasoning about what it probably contained — confirmed it was schema-only in one read
  rather than several exchanges of guessing.
- When a user says "take no action yet" while working through several linked concerns, treat that as
  a hard gate on ALL of them collectively, not just the specific one being discussed at that moment —
  wait for one clear, explicit go-ahead ("do them all") before writing any code, even once individual
  pieces have been confirmed one at a time.

## 2026-08-19 (13)

**Goal:** First of four "biggie" workstreams the user requested: retrofit the entire codebase to
the coding standards added mid-session (block-scoped namespaces, full XML docs on every member,
Allman bracing — regions explicitly skipped per user decision, since this app has no real CRUD-manager
classes for the region taxonomy to fit).

**Done:**
- Surveyed scope first: bracing already 100% compliant (0 violations). 54/56 src files + 19/19 test
  files used file-scoped namespaces; 10/56 src files had zero doc comments at all.
- Ran 11 parallel general-purpose agents, batched by directory, each retrofitting a disjoint set of
  files (namespace conversion + missing docs). All 11 hit an individual API spend limit mid-task and
  reported "failed" — but most had already completed their file edits before dying (the failure was
  in generating their final summary text, not in the actual work). Verified via build (0 errors) and
  full test suite (86/86 passing) before concluding nothing was corrupted.
- Cross-referenced git status against the original 75-file plan: 68 of 75 files were actually done
  correctly by the agents. Finished the remaining 7 by hand rather than risk another agent batch:
  `AssemblyInfo.cs` (no namespace/members — nothing applicable), `KeyedTableComparer.cs` (already
  block-scoped from earlier this session, just needed the required blank line after `{`),
  `SchemaHtmlReportWriter.cs`, `TableDdlDiffBuilder.cs`, `TableDdlGenerator.cs`,
  `ConnectionSetupViewModel.cs`, and `MainWindowViewModel.cs` (1108 lines — converted the namespace
  mechanically via a PowerShell script to avoid manual reindentation errors, then added docs to ~20
  remaining undocumented members by hand, including fixing a stale doc comment left orphaned above
  the wrong declaration from an earlier session-19 refactor).
- Final verification: zero file-scoped namespaces remain anywhere in `src`/`tests`, zero files with
  no doc comments at all, full test suite 86/86 passing, both projects compile with 0 warnings/0
  errors (App project's final copy step still blocked by the running `VK` process, as every time
  this session — not a compile issue).

**Decisions:**
- Region taxonomy skipped entirely for this codebase (user's explicit choice) — it's records, static
  helpers, ViewModels, and WPF code-behind, none of which map onto Create/Read/Update/Delete.
- When a batch of agents fails, verify actual on-disk state (git status + build + tests) before
  assuming the work is lost — most of it had actually landed; only 7 of 75 files needed redoing.
- For the one large, structurally risky file (MainWindowViewModel.cs), used a mechanical script for
  the reindentation step rather than hand-retyping 1100+ lines, to eliminate transcription-error risk.

**Left to do:** items 2–4 of the "biggies": CLI mode (JSON input with plaintext credentials, HTML
output next to the input file), CSV/JSON export (low priority), and reverting the temporary
`TemporarilySkippedTablesForFasterIteration` dev hack (explicitly last, to keep iteration fast while
building the remaining items).

**Patterns noted:**
- An agent batch reporting "failed" is not the same as "no work was done" — always verify actual
  file state (git status, build, tests) before assuming a failure means starting over. Here, treating
  it as a total loss would have wasted ~68 files' worth of already-correct work.

## 2026-08-19 (9)

**Goal:** Immediately after shipping the new Data comparison / Tables & views nav, user hit a costly
navigation bug: clicking "Data Sources" after a completed run made the results unreachable, with no
way back except re-running the whole comparison (20+ minutes lost).

**Done:**
- Root cause: the new "Tables & views"/"Data comparison" nav buttons only toggled which sub-view was
  visible *inside* the Results screen (`SchemaResultsSubView`/`DataResultsSubView`) — neither one
  actually brought the Results screen (`ResultsBody`) back into view if `DataSourcesBody` was
  currently showing. Once you clicked "Data Sources," there was no nav path back — a dead end, even
  though the underlying `DataDiffNodes`/`SchemaRowsView` data was never actually cleared, just
  unreachable through the UI.
- Fixed: `ShowSchemaResultsSubView()`/`ShowDataResultsSubView()` now both call a new
  `EnsureResultsBodyVisible()` first, making both nav buttons genuine, unconditional navigation
  destinations — clicking either always lands on the Results screen with that sub-view active,
  regardless of what was showing before. `ShowResultsBody()` simplified to just delegate to
  `ShowSchemaResultsSubView()` since that now does everything needed.
- App project compiles clean (verified up to the file-copy step, blocked again by the still-running
  `VK` process — same pattern as every other rebuild this session).

**Decisions:** none new — a straightforward bug fix for a regression introduced by the same-session
Data comparison tab work, caught immediately by the user actually using the feature.

**Left to do:**
- User to close the app, rebuild, and confirm both nav buttons now always work regardless of prior
  screen state.
- Everything else from session (8) still open: verifying the Data comparison tab shows expected
  differences, the anomaly-scan idea, other MAX-length column types.

**Patterns noted:**
- A brand-new navigation feature should be checked for round-trip correctness (can you get back to
  every screen from every other screen), not just forward-path correctness (does clicking it show the
  right thing) — this bug shipped because only the forward path was verified.

## 2026-08-19 (8)

**Goal:** User grew concerned VK was reporting "no differences" for every table in a real compare
run and wanted independent verification (planned to use Redgate SQL Data Compare). Investigate
whether this is a real correctness bug before anything else.

**Done:**
- User supplied a concrete, falsifiable claim: `Client.ClientIdentifier='10351'` exists in
  `InvoicingFO` and not in `Invoicing`. Confirmed independently via direct SQL — the claim was
  accurate.
- Rather than reading code and guessing, ran the actual compiled `KeyedTableComparer.CompareAsync`
  directly against the real live `Client` table (a throwaway console probe project referencing
  `DataCompare.Engine`, deleted after use) — it correctly found the row
  (`RowsOnlyInTarget.TotalCount: 1`, exact match). This proved the comparison engine itself is
  correct, redirecting the search elsewhere.
- User confirmed the progress popup *does* say "Differences found" for affected tables while
  running — just easy to miss (small gray text, same style as every other status message). Per
  explicit instruction ("red, bold, and flashing... cannot have ANYTHING easy to miss"), added a
  `HasDifferences` flag to `TableProgressItem` and made a differing table's row unmissable: red
  warning triangle, bold red text, light red background, continuous flash via
  `DataTrigger.EnterActions`/`ExitActions`.
- User then shared a screenshot showing "Schemas are identical" for all 33 tables and said this is
  what led to the "everything is the same" concern. That screen is the **Schema** comparison
  ("Tables & views" tab) — correctly reporting that table *structure* matches, which is a completely
  different claim from row-level *data* being identical. Confirmed (by grepping the whole App
  project) that `DataDiffNodes` — where actual data differences live — was never bound to any visible
  control anywhere in the XAML. There was no Data results screen to look at; the schema screen was
  the only one that existed, and its "identical" result was correct but was being read as if it
  covered data too.
- Built a real Data comparison results screen: `MainWindow.xaml`'s Results area now has two
  switchable sub-views (Schema — the pre-existing one, now properly enabled/wired via a "Tables &
  views" nav button instead of a disabled placeholder — and a new "Data comparison" sub-view showing
  `DataDiffNodes` in a `TreeView`, giving the §18 "Open both" drill-down button an actual home for
  the first time).
- Full test suite: 79/79 passing (App-only XAML/ViewModel changes; Engine untouched). App project
  builds clean (0 warnings/errors) after user closed the running instance.
- Full writeup in planning.md §19.

**Decisions:**
- When accused of "wiggling out and blaming the user," stopped, acknowledged, and wrote nothing
  further until asked to proceed — the user's frustration was legitimate given how the investigation
  had been reading, and pushing back or over-explaining in the moment would have made it worse.
- Independent verification of a correctness claim (running the actual compiled engine code directly,
  not reasoning about it or re-reading it) settled a five-message back-and-forth in one shot — that's
  the same discipline as the DB-live-checks earlier in the session, just applied to C# code instead
  of a live database.

**Left to do:**
- User to rebuild and confirm the new Data comparison tab actually shows the expected differences
  (Client, InvoiceLine, InvoiceReport, CardTransactionXml, etc.) once a full run completes.
- §18's proactive whole-table anomaly-scan idea, and checking for other MAX-length column types
  elsewhere in the schema, both still open from earlier entries.

**Patterns noted:**
- A user-reported "the tool says X" claim should be checked at the most literal, lowest level
  possible (run the actual code path directly against real data) before looking anywhere else —
  it's faster than auditing the surrounding orchestration code and it definitively rules large
  swaths of surface area in or out in one step.
- "The feature works but there's nowhere to see it" is a distinct failure mode from "the feature is
  broken," and it produces identical symptoms (user sees no output) — worth explicitly separating
  "is the computation right" from "is the result visible" as two different questions when a user
  reports a tool "isn't doing anything."

## 2026-08-19 (7)

**Goal:** Add elapsed-time display to the progress window — a separately-tracked backlog item, but
directly relevant after today's debugging repeatedly needed wall-clock timing the app couldn't show
on its own (had to check build DLL timestamps against system time instead).

**Done:**
- Added a `Stopwatch` + 1-second `DispatcherTimer` in `MainWindowViewModel`, spanning the whole
  `CompareAsync` call (schema comparison through data comparison, not just the data phase). Exposed
  as `ElapsedTimeText` ("Elapsed: 4m 12s"), shown in `DataComparisonProgressWindow` under the status
  line. Timer stops on completion/failure/cancellation but leaves the final value on screen, so total
  run time is visible without leaving the app.
- Full test suite re-run: 79/79 still passing. App project builds clean (0 warnings, 0 errors) — this
  was the first build this session that didn't hit the file-lock issue, confirming the app had
  actually been closed.
- planning.md §19 updated; this closes the backlog item the user mentioned, done as part of this
  session rather than separately.

**Decisions:** none new — straightforward implementation of an already-agreed, already-tracked
feature, using the same `IProgress`-free direct-property-update approach as the rest of the ViewModel
since the timer tick already runs via WPF's dispatcher (no cross-thread marshaling needed here, unlike
the Parallel.ForEachAsync background work elsewhere in the same class).

**Left to do:**
- User to close the app, rebuild, and re-test with both chunk-level visibility and elapsed time in
  place — should now show definitively whether `InvoiceLine`'s remaining slowness is one imbalanced
  chunk, all five genuinely taking a while, or server-side contention, and exactly how long it takes.
- The `DataDiffNodes`/`DiffTreeNodeTemplate` binding gap (results have nowhere to render) still needs
  its own investigation — flagged, not fixed.
- Checking for other MAX-length column types elsewhere in the schema; §18's proactive anomaly-scan
  idea. Both still open from earlier entries.

**Patterns noted:** none new this entry.

## 2026-08-19 (6)

**Goal:** After the §19 flat-dispatch fix, the run still plateaued at "31 of 33" for ~17 minutes with
no way to tell whether that was legitimate (big table grinding through chunks) or broken. Build the
chunk-level progress visibility that had been explicitly deferred earlier in the session, now that
there's concrete evidence it's needed.

**Done:**
- User confirmed the run eventually finished on its own (no total time available — noted there's
  already a separate backlog task for adding elapsed-time display to the progress window; didn't
  fold that into this work).
- Added `ChunkProgressItem` (label + IsComplete) and gave `TableProgressItem` an `ObservableCollection
  <ChunkProgressItem>` — empty for ordinary tables, one per key range for a partitioned large table.
- Rewired `DataComparisonProgressWindow.xaml` from a flat `ListBox` to a `TreeView` with a
  `HierarchicalDataTemplate` — a table with no chunks renders identically to before (WPF hides the
  expand arrow on an empty `ItemsSource`), a partitioned table shows expandable chunk sub-rows
  labeled with their actual key bounds (e.g. "Chunk 2 of 5 (ID 10,720,068 – 21,440,134)"), and the
  parent row's summary updates live to "Comparing 5 chunks (3/5 complete)..." as chunks finish.
- Reused the existing `IProgress<T>` pattern (already used for `progress`/`tableProgress`) for the
  new chunk-completion reporting, since WPF property/collection updates need to marshal back to the
  UI thread and that's the mechanism already established in this codebase for exactly that.
- While investigating, discovered that `DataDiffNodes`/`DiffTreeNodeTemplate` (including the earlier
  §18 "Open both" drill-down button) aren't bound to any control anywhere in the XAML — the data
  comparison results currently have nowhere to render after the progress popup closes. Flagged in
  planning.md §19 as a discovered gap, not fixed (out of scope for this session).
- Full test suite re-run: 79/79 still passing (this was an App-only UI change; Engine untouched).
  Engine project builds clean standalone; App project build blocked by the still-running `VK`
  process, same as every other rebuild this session.

**Decisions:** none new — implementing the chunk-level UI already agreed on twice earlier in the
session, once tentatively and once with concrete evidence backing the need.

**Left to do:**
- User to close the app, rebuild, and re-test with chunk-level visibility in place — should finally
  show definitively whether `InvoiceLine`'s remaining slowness is one imbalanced chunk vs. genuinely
  all five taking a while vs. server-side contention.
- The `DataDiffNodes` binding gap needs its own investigation — results may not be visible to a user
  at all right now, independent of anything else this session touched.
- Still open: elapsed-time display (separate existing backlog item), checking for other MAX-length
  column types (`ntext`/`image`/`geography`/`geometry`) elsewhere in the schema, §18's proactive
  anomaly-scan idea.

**Patterns noted:** none new this entry.

## 2026-08-19 (5)

**Goal:** After the §19 flat-dispatch fix, progress was moving again (22 → 30 of 33) — user reported
the actual stragglers left were `InvoiceLine`, `InvoiceReport`, and `CardTransactionXml`.

**Done:**
- `EventLog` (the other originally-flagged large table) finished successfully — confirms the
  range-partitioned chunking is working for it.
- `CardTransactionXml` was unexpected: only 4,652 rows, but checked its schema live and found
  `SourceXml XML` — SQL Server's native `xml` type, which also reports `MaxLength == -1` but wasn't
  covered by the §18 `LargeContentColumn.Is()` check (only `varbinary`/`nvarchar`/`varchar` were
  handled). Same bug class as `InvoiceReport`'s blob, different type slipping through.
- Added `xml` to `LargeContentColumn.Is()`. Since `HASHBYTES` doesn't accept `xml` directly,
  `BuildValueSelectExpression` now converts it via `CONVERT(nvarchar(max), ...)` first — same
  approach `ColumnHashExpressionBuilder` already uses elsewhere for other non-hashable types.
- Added a LocalDB integration test (`CompareAsync_XmlColumn_ComparesByHashAndDetectsChange`)
  confirming the xml path actually works end to end, not just compiles. Full suite: 79/79 passing.
  Engine project builds clean standalone; App project build blocked again by the still-running `VK`
  process (expected — same as every previous rebuild this session).
- `planning.md` §18 updated with this follow-up and an explicit lesson: the original fix covered the
  one column it was built for, not "any MAX-length type" — `ntext`/`image`/`geography`/`geometry`
  are flagged as plausible next surprises worth checking for, not assumed exhaustive.

**Decisions:** none new — this was a straightforward extension of the existing §18 approach to a
type it missed, not a design change.

**Left to do:**
- User to close the app, rebuild, and re-test — `InvoiceLine`, `InvoiceReport`, and
  `CardTransactionXml` were still the stragglers at last check; want to confirm all three (plus
  `EventLog`) now complete promptly.
- Chunk-level progress UI (planning.md §19) — still confirmed wanted, not yet built.
- Worth a quick live check for other MAX-length column types (`ntext`, `image`, `geography`,
  `geometry`) across the schema before assuming no more surprises remain.
- §18's proactive whole-table anomaly-scan idea remains undone, as previously noted.

**Patterns noted:**
- A "fix" scoped to the specific columns discovered during diagnosis, rather than the general
  category those columns belong to, is exactly the kind of gap that resurfaces later looking like a
  new bug — worth explicitly asking "what's the general rule here, and did I cover all instances of
  it" before considering a targeted fix done.

## 2026-08-19 (4)

**Goal:** Diagnose why the §19 fix didn't seem to help — progress plateaued at "29 of 33" tables —
without guessing, then fix whatever the diagnosis found.

**Done:**
- Tried to check live via `sys.dm_exec_requests` (would show exactly what SQL is executing right
  now) — denied: `josepha` lacks `VIEW SERVER PERFORMANCE STATE` on this server. Didn't pursue
  granting that mid-run.
- Fell back to a live row-count query across all 33 tables in `Invoicing`, sorted descending.
  Confirmed only 2 tables exceed the 1,000,000-row threshold (`InvoiceLine`, `EventLog`); everything
  else is under 500K, including a couple (`AuditLog` 484K, `InvoiceLineSummary` 326K) slow enough on
  the single-threaded merge-join to take real time.
- This proved the §19 design (small tables phase, then a separate large-table phase) had a real bug:
  the large-table phase only started after `Parallel.ForEachAsync` for ALL 31 small tables fully
  returned — so `InvoiceLine`/`EventLog` chunking couldn't begin until every other table finished,
  including any slow straggler. Worse than the original problem (where `InvoiceLine` at least ran
  immediately, just single-threaded).
- Fixed by removing the phase separation entirely: one flat list of jobs (31 small whole-table jobs +
  up to 10 large-table chunk jobs, boundaries computed up front) dispatched through a single
  `Parallel.ForEachAsync` call. It pulls the next queued item into a freed slot as soon as one
  finishes, so large-table chunks start immediately alongside small tables — no barrier, no second
  concurrency dial. Deleted the now-obsolete `RunLargeTablePhaseAsync` method.
- Full test suite re-run after the fix: 76/76 still passing (the underlying comparer/combine logic
  didn't change, only how the ViewModel schedules jobs). App project builds clean.
- planning.md §19 rewritten to document both the original bug and the fix, not just the end state —
  worth keeping the "first attempt was wrong, here's why" trail for future reference.

**Decisions:**
- User pushed back on "let's just add UI to see what's happening" as the first response to the
  stall — right call: the UI would only have made the same bug more visible, not fixed it. Diagnosed
  the actual defect (via live row counts, since server-state DMV access wasn't available) before
  building anything.
- Chunk-level progress UI (declined earlier in the session) is now confirmed as wanted, specifically
  because a long opaque wait is indistinguishable from a stuck/broken run — planning.md §19 updated
  to reflect this as a near-term follow-up rather than a maybe.

**Left to do:**
- User to rebuild (app was mid-run, will need a fresh close/rebuild/relaunch) and confirm the flat
  dispatch actually engages both large tables' chunks immediately and improves wall-clock.
- Chunk-level progress UI (planning.md §19) — confirmed wanted, not yet built.
- §18's proactive whole-table anomaly-scan idea remains undone, as previously noted.

**Patterns noted:**
- When a live DMV check is denied by permissions, falling back to a simpler live query that's
  still evidence (row-count distribution across all tables) rather than reverting to guesswork found
  the real bug directly — the "29 of 33" number only became meaningful once cross-referenced against
  the actual table count and size distribution.
- Worth remembering: a two-phase "small stuff first, then the hard stuff" design that looks like it
  solves a resource-contention problem can introduce a head-of-line-blocking problem instead if phase
  2 can't start until every last item in phase 1 (including unexpected stragglers) finishes. Flat
  work-stealing dispatch (one queue, one scheduler) avoided this without needing to predict which
  tables would be slow.

## 2026-08-19 (3)

**Goal:** After re-testing the §18 fix live, three tables were still slow (`InvoiceLine`,
`EventLog`, `InvoiceReport`) — investigate and address the general "a few huge tables dominate
wall-clock" problem, per user's own proposal to defer large tables and parallelize them.

**Done:**
- Checked `EventLog` live the same way as `InvoiceLine`/`InvoiceReport`: PK matches on both sides, no
  MAX-length column — confirmed it's not a bug, just a 3.9M-row table on a single thread.
- Corrected an overclaim mid-session: had described `InvoiceReport` as "fixed" based only on the
  code compiling and passing tests, not on the live run (which was still in progress) — user caught
  this and it was corrected before proceeding.
- Implemented the user's proposed design (refined into a cleaner two-phase form during discussion):
  classify tables by approximate row count up front (`TableRowCountReader`, instant via
  `sys.partitions`), run small tables through the existing per-table parallelism first, then defer
  tables ≥1,000,000 rows to a second phase (`RunLargeTablePhaseAsync`) where each is split into 5
  key-range chunks (`TableRangePartitioner`, `KeyRange`) that run concurrently and get merged back
  together (`KeyedTableDiffResult.Combine`) — sharing the same concurrency cap as phase 1 rather than
  adding a second multiplying dial.
- Progress window kept at one row per table (per explicit user direction) — a partitioned table's
  spinner only flips to a checkmark once all its chunks finish. Chunk-level visibility ("3 of 5
  complete") explained and explicitly deferred as a likely next ask if the wait still feels opaque.
- Added unit tests (`TableRangePartitionerTests`, `KeyedTableDiffResultCombineTests`) and an
  integration test proving partitioned-and-combined results exactly match a whole-table comparison
  against real LocalDB. Full suite: 76/76 passing. Both projects build clean.
- Full design writeup in `planning.md` §19.

**Decisions:**
- Defer-then-flatten (two sequential phases sharing one concurrency cap) chosen over nesting
  intra-table parallelism inside the existing per-table parallelism — the user's proposal, refined in
  conversation — because it needs no second concurrency dial and can't cause a connection-count
  explosion the way nested parallelism would have.
- Partition by the leading primary-key column only, boundaries sampled via indexed OFFSET/FETCH
  seeks rather than an even numeric split of the key range — correctness never depends on chunks
  being evenly sized, but even sizing matters for actually getting a speedup.
- Chunk-level progress visibility confirmed as wanted eventually, but deferred until it's clear the
  partitioning itself helps — avoids building UI for a mechanism not yet proven in production.

**Left to do:**
- User to re-run the comparison against the real databases and confirm actual wall-clock improvement
  — nothing here has been validated against production data yet, only against synthetic LocalDB
  fixtures.
- Chunk-level progress visibility (planning.md §19) if the table-level-only spinner still feels
  opaque during a long large-table phase.
- §18's proactive whole-table anomaly-scan idea remains undone, as previously noted.
- Everything else on the Refactonauts Trello board is unchanged by this session.

**Patterns noted:**
- Caught overclaiming a fix as confirmed when only the code-level verification (compiles, passes
  synthetic tests) had actually happened — the live-data confirmation was still pending. Worth
  being explicit about which kind of verification backs a claim before stating it as fact.
- The user's own architectural proposal (defer + parallelize large tables) was better than the
  first design floated (nest intra-table parallelism inside existing per-table parallelism) —
  worth taking a user's counter-proposal seriously and evaluating it on the merits rather than
  defending the original design, which is what happened here once the tradeoffs were actually compared.

## 2026-08-19 (2)

**Goal:** Diagnose why a live comparison run against real data was very slow on two tables
(`InvoiceLine`, `InvoiceReport`), then fix the identified cause.

**Done:**
- Connected directly (read-only) to the actual `Invoicing`/`InvoicingFO` RDS dev databases to check
  facts rather than guess — ruled out "missing/mismatched primary key" as the cause (both tables'
  PKs match on both sides, so both already use the fast keyed comparer). Found the real causes:
  `InvoiceLine` is just large (53.6M rows, no fix needed); `InvoiceReport` has a
  `Bytes VARBINARY(MAX)` column (rendered PDF or XLSX per row) being pulled and byte-compared in
  full for every row.
- Implemented server-side `HASHBYTES` comparison for MAX-length binary/text columns in
  `KeyedTableComparer` (`LargeContentColumn.cs`), so only a 32-byte hash crosses the wire per row
  for those columns instead of the full value.
- Implemented an on-demand "Open both..." drill-down action in the Data tab for changed
  `varbinary(max)` columns: re-fetches the real bytes for just that one row
  (`LargeContentValueFetcher`), guesses the right file extension from the content's actual magic
  bytes rather than trusting a sibling column (`FileSignatureSniffer`), and opens both files via the
  OS default app for the user to compare visually.
- Added a data-integrity cross-check: if a row is marked `IsFrontPage=1` but its sniffed content
  isn't actually a PDF, the status bar flags it after opening — scoped to drill-down time only, per
  explicit user direction (proactive full-table scanning is documented as a possible future
  expansion, not built).
- Added unit tests for `LargeContentColumn`/`FileSignatureSniffer` and LocalDB integration tests for
  the varbinary(max) hash-comparison path and `LargeContentValueFetcher`. Full suite: 68/68 passing.
  App project builds clean.
- Full design writeup in `planning.md` §18.

**Decisions:**
- Extension detection sniffs actual magic bytes rather than trusting the `IsFrontPage` column,
  even though the business rule (`IsFrontPage=1` → PDF, `=0` → xlsx) was already known — the bytes
  are already in hand at that point, so sniffing costs nothing extra but can't drift out of sync
  with itself the way trusting an unrelated column could.
- Anomaly check (`IsFrontPage` vs. actual content) confirmed as wanted, but deliberately scoped to
  drill-down-only for now rather than a proactive per-row scan across the whole table, so the
  priority stays on validating the core diff mechanism first.

**Left to do:**
- User to cancel the in-flight comparison run and re-test against the real databases with this
  change in place.
- Possible future expansion noted in planning.md §18: proactively sniffing every row's leading bytes
  (cheap — 4 bytes, not the whole blob) to catch rows mislabeled identically on both sides, which
  drill-down alone can never surface.
- Everything else on the Refactonauts Trello board is unchanged by this session (exclusion rules,
  CLI/headless mode, export formats, Data tab UI polish, column-resize memory, elapsed-time display,
  and the remaining verification cards).

**Patterns noted:**
- When a user's hypothesis ("it's the table structure") is plausible but unverified, connecting
  directly to the real system (read-only, credentials already available via the app's own Windows
  Credential Manager entries) to check facts produced a materially different and more actionable
  answer than reasoning from the code alone would have — the code-only guess (missing PK) was wrong.
- Iterating the design in conversation before writing code (hash vs. raw value → open-both action →
  extension detection method → anomaly check → its scope) caught a real correctness issue (trusting
  `IsFrontPage` over sniffing bytes) before any code was written, cheaply.

## 2026-08-19

**Goal:** Reflect Trello card status change in project docs.

**Done:**
- User click-tested and moved two verification cards to Done on Trello: progress-popup spinner
  rendering, and the "Remember credentials" round-trip. Updated `planning.md` §17 to change these
  from "pending verification" to confirmed/verified.

**Decisions:** none.

**Left to do:** Remaining 6 verification cards and 6 backlog cards on the Refactonauts board are
still open (per-table parallel comparison correctness, Cancel-during-compare, HTML export accuracy,
column resize/proportional scaling, Compare-button enablement logic, Data Sources↔Results toggle;
plus exclusion rules, CLI/headless mode, export formats, Data tab UI polish, column-resize memory,
elapsed-time display).

**Patterns noted:** none.

## 2026-08-18

**Goal:** Update planning.md with current project state, then turn the remaining known gaps and unverified features into Trello tickets for tracking.

**Done:**
- Updated `planning.md` with a full "current state snapshot" section (§17) covering app identity, Data Sources screen, Results screen, data comparison mechanism, not-yet-built/deferred items, and a note flagging the progress-popup spinner change as unverified. Fixed stale cross-references in §6 and §11.
- Identified the Trello board in use ("Refactonauts") out of four candidate boards, confirmed the "ToDo" list and "Testing" label on it.
- Drafted and created 14 Trello cards on Refactonauts → ToDo, all assigned to Joseph Adams and labeled "Testing":
  - Build backlog (6): Exclusion rules (design+impl, deliberately kept as one catch-all card since scope isn't settled yet), CLI/headless mode, CSV/Excel/JSON export formats, Data tab UI polish, Column-resize memory, Elapsed-time display in progress popup.
  - Verification/QA (8): progress-popup spinner rendering, Cancel actually stopping in-flight SQL, per-table parallel comparison correctness, "Remember credentials" round-trip, HTML export accuracy, column resize/proportional scaling, Compare-button enablement logic, Data Sources↔Results toggle and Cancel-during-compare state.

**Decisions:**
- Exclusion rules are more complex than originally scoped (type-based defaults + per-table/column overrides + engine wiring) — deliberately collapsed into a single catch-all backlog card rather than three separate cards, to be split once the design is actually agreed.
- Trello tickets are split into two distinct groups (build backlog vs. verification/QA) per explicit user direction ("Both") — verification tickets exist for already-built features that haven't been click-tested yet, not just for net-new work.

**Left to do:**
- Everything on the 14 Trello cards, in priority order still to be decided by the user.
- The previously-flagged pending item: the `DataComparisonProgressWindow` spinner animation was written but not yet build-verified (app was locked during that edit) — now tracked as its own verification card.
- Longer-term planning.md items not yet turned into tickets or started: none outstanding — all known gaps are now represented on the board.

**Patterns noted:**
- When asked to draft external-system tickets (Trello), presenting the full draft list for review before creating anything worked well and matched the user's explicit "show me their contents" instruction — created only after an explicit "yes".
- When a proposed backlog item turns out to be more complex than first scoped, the user prefers a single catch-all card over several speculative sub-cards until the design is actually settled.
