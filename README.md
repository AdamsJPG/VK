# VK

*Part of the **Tyrell** project.*

The overall project is codenamed **Tyrell** (Blade Runner) — a fitting name for a system built around "replicating" an application. This tool is **VK**, after the **Voight-Kampff** test: the in-universe test for detecting whether something is genuinely human or a replicant impersonating one. That's a near-literal match for a tool whose job is verifying whether a migrated database is a faithful copy of the original, or a subtly-off replicant of it.

A Windows desktop tool for comparing two Microsoft SQL Server databases — schema and data — while deliberately excluding noise columns (IDs, UUIDs, timestamps) that don't reflect real business-data differences.

Primary use case: verify that a presentation-layer refactor produced byte-for-byte identical business data by replaying the same actions against an original ("Source") and refactored ("Target") system, then diffing the resulting databases.

## What it does

- **Schema comparison** — diffs tables, columns, types, lengths, precision, and nullability between Source and Target, grouped as Only-in-Source / Only-in-Target / Different / Identical, with a side-by-side DDL diff view. Views are diffed the same way (columns plus definition text); functions and stored procedures are diffed by definition text.
- **Data comparison** — for each table present on both sides, streams rows from both databases ordered by primary key and merge-joins them client-side (the same approach SQL Data Compare uses), reporting rows missing from either side and rows whose values differ. Tables without a usable primary key fall back to a server-side content-hash / multiset diff.
- **HTML export** — generates a self-contained HTML report (schema or data comparison, whichever tab is active) that can be shared without the app installed, including the same row-level drill-down detail shown on screen.
- **Command-line mode** — run a comparison headlessly from a JSON request file; see [Command-line mode](#command-line-mode) below.
- Runs table comparisons in parallel, with real cancellation and a live progress popup showing each table ticking off as it completes.

## Not yet built

- **Exclusion rules** — the mechanism to actually configure which columns (IDs, timestamps, etc.) are excluded from a comparison. Data comparison currently compares every common column.
- Structured export beyond HTML (CSV/JSON) — not currently planned; HTML is the only export format for now. Excel is explicitly out of scope.
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

On launch, fill in Server, User name, and Password for both **Source** and **Target** (Database is optional — leave blank to use the login's default). Check **Remember credentials** to have them restored automatically next time. Use the **Compare:** checkboxes (Tables, Views, Functions, Stored procedures) to choose which object kinds to include — Tables is selected by default, matching the tool's original behavior, and applies symmetrically to both sides. Click **Compare now** once both sides have Server + User filled in.

## Command-line mode

The built executable is `VK.exe`. Passing it any argument switches into headless mode instead of opening the GUI:

```
VK.exe /?                  Show usage help.
VK.exe /stub [path]        Write a template request JSON to [path]
                            (default: vk-compare-stub.json in the current directory).
                            Fails if the file already exists.
VK.exe <path-to-json>      Run the comparison(s) described by the JSON file and write
                            the HTML report(s) next to it.
```

Request JSON shape:

```json
{
  "mode": "schema" | "data" | "both",
  "source": {
    "server": "...", "database": "...", "username": "...", "password": "...",
    "encrypt": true, "trustServerCertificate": false
  },
  "target": { "...": "same fields as source" },
  "objectTypes": "tables, views, functions, storedProcedures"
}
```

`mode` is required — a schema-only check shouldn't silently also trigger a long data comparison on a large database. Passwords are stored in **plaintext** in this file by design, since CLI mode needs a fully non-interactive input; treat the file accordingly. This is separate from the GUI, which never writes a password to disk (Windows Credential Manager only).

`objectTypes` is optional — omit it (as in the `/stub` template) to compare **tables only**, matching this tool's original behavior and keeping older request files working unchanged. When present, it's any comma-separated subset of `tables`, `views`, `functions`, `storedProcedures`. `functions`/`storedProcedures` only apply when `mode` is `schema` or `both` — they have no data to compare, so they're ignored in a data-only run. `views` applies to both: in schema mode a view is diffed like a table (columns) plus its underlying definition text; in data mode its rows are compared exactly like a table's.

**Known quirk:** after a CLI run finishes, the terminal prompt doesn't visibly reappear until you press Enter. `VK.exe` is a GUI-subsystem (`WinExe`) app that attaches to the parent console for CLI output (see `Program.cs`); `cmd.exe` doesn't wait for GUI-subsystem processes to exit the way it does console-subsystem ones, so it hands the prompt back the instant VK.exe launches, and VK.exe's own output races onto the same console afterward. The prompt was there the whole time — it just scrolled past under the CLI's own output. Pressing Enter re-displays it. This is a known, accepted tradeoff of the current WinExe + AttachConsole approach, not a bug in the CLI output itself.
