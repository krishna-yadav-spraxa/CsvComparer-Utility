# CsvComparer

A command-line utility to compare two CSV files and report exactly what changed, was added, or was removed — row by row, column by column.

---

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download) installed on your machine

---

## Setup (One Time)

```bash
cd E:\Spraxa\CoolR\CsvComparer
dotnet build --configuration Release
```

---

## How to Use

### Step 1 — Export your data to CSV

Export both views/queries from SSMS:
- Run your query → right-click results → **Save Results As** → save as `.csv`
- Name them clearly, e.g. `OldView-Data.csv` and `NewView-Data.csv`

### Step 2 — Place both CSV files in the project folder

```
E:\Spraxa\CoolR\CsvComparer\
    OldView-Data.csv
    NewView-Data.csv
```

### Step 3 — Run the comparison

Open a terminal in `E:\Spraxa\CoolR\CsvComparer` and run:

```bash
dotnet run -- OldView-Data.csv NewView-Data.csv --key "AssetId,ClientId" --trim --compact --output diff.csv
```

The diff result is saved to `diff.csv` in the same folder.

---

## Common Commands

### Basic comparison (positional — row 1 vs row 1)
```bash
dotnet run -- OldView-Data.csv NewView-Data.csv
```

### Match rows by a unique ID column
```bash
dotnet run -- OldView-Data.csv NewView-Data.csv --key AssetId
```

### Match rows by a composite key (multiple columns)
```bash
dotnet run -- OldView-Data.csv NewView-Data.csv --key "AssetId,ClientId"
```

### Ignore whitespace differences
```bash
dotnet run -- OldView-Data.csv NewView-Data.csv --key AssetId --trim
```

### Only show columns that actually changed (compact output — easier to read in Excel)
```bash
dotnet run -- OldView-Data.csv NewView-Data.csv --key AssetId --trim --compact
```

### Skip audit/timestamp columns you don't want to compare
```bash
dotnet run -- OldView-Data.csv NewView-Data.csv --key AssetId --exclude "CreatedAt,UpdatedAt,ModifiedBy"
```

### Save diff + a per-column summary
```bash
dotnet run -- OldView-Data.csv NewView-Data.csv --key AssetId --output diff.csv --summary summary.csv
```

### TSV files (tab-separated)
```bash
dotnet run -- OldView-Data.tsv NewView-Data.tsv --delimiter tab
```

### Case-insensitive comparison
```bash
dotnet run -- OldView-Data.csv NewView-Data.csv --key AssetId --ignore-case
```

---

## All Options

| Option | Description |
|---|---|
| `--key <col1,col2,...>` | Match rows by column value instead of row position. Use when rows may be in a different order. |
| `--trim` | Ignore leading/trailing whitespace when comparing values. |
| `--ignore-case` | Case-insensitive value comparison. |
| `--compact` | Only show columns that differ in the output. Reduces width for wide tables. |
| `--exclude <col1,col2,...>` | Skip these columns entirely (not compared, not in output). |
| `--delimiter <value>` | Force delimiter: `comma`, `tab`, `semicolon`, `pipe`. Auto-detected if not set. |
| `--output <path>` | Output diff CSV filename. Default: `diff_<timestamp>.csv` |
| `--summary <path>` | Write a second CSV with per-column diff counts. |

---

## Understanding the Output

### Console output

```
Skipped (only in Old) : Displacement
Skipped (only in New) : MdmLatestLatitude, LatestLatitude

Columns (active) : 66  (5 skipped)
Old data rows    : 5,73,392
New data rows    : 5,73,392

─── Results ──────────────────────────
  Identical  :   66,975
  Changed    : 5,06,417
  Added      :        0
  Removed    :        0
  Total diff : 5,06,417 / 5,73,392 rows  (88.3% differ)

─── Column diff breakdown ─────────────
  KeyAccountId       5,03,649 row(s) differ
  ClassificationId   4,31,904 row(s) differ
  ...
```

- **Skipped** — columns that exist in only one file are automatically skipped (no error).
- **Identical** — rows where every column value matches exactly.
- **Changed** — rows where at least one column value differs.
- **Added** — rows present in the New file but not in the Old file (only relevant when using `--key`).
- **Removed** — rows present in the Old file but not in the New file (only relevant when using `--key`).

### Diff CSV columns

| Column | Description |
|---|---|
| `KeyValue` | The key column value(s) used to match the row (only present when `--key` is used) |
| `RowNumber` | Row number in the source file |
| `Status` | `Changed`, `Added`, or `Removed` |
| `ChangedColumns` | Semicolon-separated list of column names that differ for this row |
| `<Column>_Old` | Value from the Old CSV |
| `<Column>_New` | Value from the New CSV |

---

## Exit Codes

| Code | Meaning |
|---|---|
| `0` | Files are identical — no differences found |
| `1` | Differences found |
| `2` | Error (file not found, bad arguments, etc.) |

Useful for scripting:
```bash
dotnet run -- old.csv new.csv --key AssetId
if ($LASTEXITCODE -eq 0) { Write-Host "Views match!" }
if ($LASTEXITCODE -eq 1) { Write-Host "Differences found — check diff.csv" }
```

---

## Tips

- **Rows look the same but are flagged as changed?**
  Add `--trim` — one view likely has trailing spaces on values.

- **100% of rows flagged as changed without `--key`?**
  The two files are probably sorted differently. Add `--key YourIdColumn`.

- **Columns exist in one view but not the other?**
  The tool automatically skips them and tells you which ones were skipped.

- **Diff file is too wide to read in Excel?**
  Add `--compact` — only columns that actually differ are included in the output.

- **Only care about specific columns?**
  Use `--exclude` to drop columns you don't want to compare.
