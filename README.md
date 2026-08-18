# VK

*Part of the **Tyrell** project.*

The overall project is codenamed **Tyrell** (Blade Runner) — a fitting name for a system built around "replicating" an application. This tool is **VK**, after the **Voight-Kampff** test: the in-universe test for detecting whether something is genuinely human or a replicant impersonating one. That's a near-literal match for a tool whose job is verifying whether a migrated database is a faithful copy of the original, or a subtly-off replicant of it.

A Windows desktop tool for comparing two Microsoft SQL Server databases — schema and data — while deliberately excluding noise columns (IDs, UUIDs, timestamps) that don't reflect real business-data differences.

Primary use case: verify that a presentation-layer refactor produced byte-for-byte identical business data by replaying the same actions against an original ("Source") and refactored ("Target") system, then diffing the resulting databases.

## What it does

- **Schema comparison** — diffs tables, columns, types, lengths, precision, and nullability between Source and Target, grouped as Only-in-Source / Only-in-Target / Different / Identical, with a side-by-side DDL diff view.
- **Data comparison** — for each table present on both sides, streams rows from both databases ordered by primary key and merge-joins them client-side (the same approach SQL Data Compare uses), reporting rows missing from either side and rows whose values differ. Tables without a usable primary key fall back to a server-side content-hash / multiset diff.
- **HTML export** — generates a self-contained HTML report of the schema comparison (including full DDL) that can be shared without the app installed.
- Runs table comparisons in parallel, with real cancellation and a live progress popup showing each table ticking off as it completes.

## Not yet built

- **Exclusion rules** — the mechanism to actually configure which columns (IDs, timestamps, etc.) are excluded from a comparison. Data comparison currently compares every common column.
- CLI / headless mode.
- CSV / Excel / JSON export (HTML only, for now).
- Data tab UI polish (still a plain tree view; the Schema tab's grid + DDL pane hasn't been ported over yet).
- Column-resize memory (manually resized grid columns snap back on window resize).
- Elapsed-time display during long comparisons.

See `planning.md` for full design rationale, decisions, and current state.

## Tech stack

- **WPF on .NET 10** (`net10.0-windows`), C#, MVVM via [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet).
- **Microsoft.Data.SqlClient** for SQL Server connectivity; schema metadata read via `sys.*` catalog views.
- Credentials stored via **Windows Credential Manager** — never written to disk in plaintext.

## Project structure

```
src/
  DataCompare.App/       WPF UI (MVVM)
  DataCompare.Engine/    Comparison engine — schema reading/diffing, data comparison, reporting, credential storage (no UI dependencies)
tests/
  DataCompare.Engine.Tests/   Unit + LocalDB integration tests
```

## Getting started

Requirements: .NET 10 SDK, Windows (WPF), and SQL Server Express LocalDB for running the integration tests.

```
dotnet build
dotnet test
```

Run the app from Visual Studio (`DataCompare.slnx`), or:

```
dotnet run --project src/DataCompare.App
```

On launch, fill in Server, User name, and Password for both **Source** and **Target** (Database is optional — leave blank to use the login's default). Check **Remember credentials** to have them restored automatically next time. Click **Compare now** once both sides have Server + User filled in.
