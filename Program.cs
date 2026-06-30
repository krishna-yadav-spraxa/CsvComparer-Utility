// Entry point — orchestrates the full comparison pipeline.

var opts = AppOptions.Parse(args);
if (opts is null) Environment.Exit(2);

Console.WriteLine($"Old CSV     : {opts.OldPath}");
Console.WriteLine($"New CSV     : {opts.NewPath}");

char   delim      = opts.Delimiter ?? CsvParser.DetectDelimiter(opts.OldPath);
string delimLabel = delim switch { '\t' => "TAB", ';' => "SEMICOLON", '|' => "PIPE", _ => delim.ToString() };
Console.WriteLine($"Delimiter   : {delimLabel}{(opts.Delimiter is null ? " (auto-detected)" : "")}");
Console.WriteLine();

var oldParsed = CsvParser.Parse(opts.OldPath, delim);
var newParsed = CsvParser.Parse(opts.NewPath, delim);

if (oldParsed.Count == 0) Abort($"File is empty: {opts.OldPath}");
if (newParsed.Count == 0) Abort($"File is empty: {opts.NewPath}");

// Trim header names — prevents false mismatches from trailing spaces or BOM remnants
string[] oldHeaders = oldParsed[0].Select(h => h.Trim()).ToArray();
string[] newHeaders = newParsed[0].Select(h => h.Trim()).ToArray();

if (!oldHeaders.SequenceEqual(newHeaders, StringComparer.OrdinalIgnoreCase))
    Abort($"Column headers do not match.\n  Old: {string.Join(", ", oldHeaders)}\n  New: {string.Join(", ", newHeaders)}");

// Build active column list — drop any columns the user excluded
var excludeSet = new HashSet<string>(opts.ExcludeColumns, StringComparer.OrdinalIgnoreCase);
var activeCols = oldHeaders
    .Select((name, srcIdx) => (name, srcIdx))
    .Where(x => !excludeSet.Contains(x.name))
    .ToList();

if (activeCols.Count == 0) Abort("No columns remain after applying --exclude.");

string[] headers  = activeCols.Select(x => x.name).ToArray();
int[]    srcIndex = activeCols.Select(x => x.srcIdx).ToArray(); // maps active-column index → original index
int      colCount = headers.Length;

int[] keyIndices = ResolveKeyIndices(headers, opts.KeyColumns);

var oldRows = oldParsed.Skip(1).ToList();
var newRows = newParsed.Skip(1).ToList();

Console.WriteLine($"Columns (active) : {colCount}{(excludeSet.Count > 0 ? $"  ({excludeSet.Count} excluded: {string.Join(", ", opts.ExcludeColumns)})" : "")}");
Console.WriteLine($"Old data rows    : {oldRows.Count:N0}");
Console.WriteLine($"New data rows    : {newRows.Count:N0}");
if (keyIndices.Length > 0) Console.WriteLine($"Key columns      : {string.Join(", ", opts.KeyColumns)}");
if (opts.Trim)             Console.WriteLine("Trim             : on");
if (opts.IgnoreCase)       Console.WriteLine("Ignore case      : on");
if (opts.Compact)          Console.WriteLine("Compact output   : on");
Console.WriteLine();

// Remap every row so it only contains the active columns (in active-column order)
var oldActive = oldRows.Select(r => Remap(r, srcIndex, colCount)).ToList();
var newActive = newRows.Select(r => Remap(r, srcIndex, colCount)).ToList();

var diffs = keyIndices.Length > 0
    ? Comparer.ByKey(oldActive, newActive, colCount, keyIndices, opts)
    : Comparer.ByPosition(oldActive, newActive, colCount, opts);

// Compact mode: pre-compute which column indices have any difference at all
HashSet<int>? compactCols = opts.Compact
    ? new HashSet<int>(diffs.Where(d => d.Status == "Changed").SelectMany(d => d.ChangedIndices))
    : null;

DiffWriter.Write(diffs, headers, opts.OutputPath, keyIndices.Length > 0, compactCols);

int    cntChanged   = diffs.Count(d => d.Status == "Changed");
int    cntAdded     = diffs.Count(d => d.Status == "Added");
int    cntRemoved   = diffs.Count(d => d.Status == "Removed");
int    totalRows    = Math.Max(oldRows.Count, newRows.Count);
int    cntIdentical = totalRows - cntChanged - cntAdded - cntRemoved;
double pctDiff      = totalRows > 0 ? (double)diffs.Count / totalRows : 0;

Console.WriteLine("─── Results ─────────────────────────────────────────────────");
Console.WriteLine($"  Identical  : {cntIdentical,8:N0}");
Console.WriteLine($"  Changed    : {cntChanged,8:N0}");
Console.WriteLine($"  Added      : {cntAdded,8:N0}");
Console.WriteLine($"  Removed    : {cntRemoved,8:N0}");
Console.WriteLine($"  Total diff : {diffs.Count,8:N0} / {totalRows:N0} rows  ({pctDiff:P1} differ)");
Console.WriteLine();

if (cntChanged > 0)
{
    var colHits = new int[colCount];
    foreach (var d in diffs.Where(d => d.Status == "Changed"))
        foreach (int i in d.ChangedIndices)
            colHits[i]++;

    Console.WriteLine("─── Column diff breakdown ───────────────────────────────────");
    foreach (var (cnt, i) in colHits.Select((c, i) => (c, i)).Where(x => x.c > 0).OrderByDescending(x => x.c))
        Console.WriteLine($"  {headers[i],-40} {cnt,6:N0} row(s) differ");
    Console.WriteLine();
}

Console.WriteLine($"Diff CSV : {Path.GetFullPath(opts.OutputPath)}");
if (opts.Compact && compactCols is not null)
    Console.WriteLine($"           (compact mode: {compactCols.Count} of {colCount} column(s) shown in output)");

if (opts.SummaryPath is not null)
{
    DiffWriter.WriteSummary(diffs, headers, opts.SummaryPath);
    Console.WriteLine($"Summary  : {Path.GetFullPath(opts.SummaryPath)}");
}

// 0 = identical, 1 = differences found, 2 = error (set via Abort)
Environment.Exit(diffs.Count > 0 ? 1 : 0);

// ─── Local helpers ────────────────────────────────────────────────────────────

static void Abort(string message)
{
    Console.Error.WriteLine($"ERROR: {message}");
    Environment.Exit(2);
}

static int[] ResolveKeyIndices(string[] headers, string[] keyColumns)
{
    if (keyColumns.Length == 0) return [];
    var result = new int[keyColumns.Length];
    for (int k = 0; k < keyColumns.Length; k++)
    {
        int idx = Array.FindIndex(headers, h => h.Equals(keyColumns[k], StringComparison.OrdinalIgnoreCase));
        if (idx < 0) Abort($"Key column '{keyColumns[k]}' not found. Available: {string.Join(", ", headers)}");
        result[k] = idx;
    }
    return result;
}

static string[] Remap(string[] row, int[] srcIndex, int colCount)
{
    var result = new string[colCount];
    for (int i = 0; i < colCount; i++)
    {
        int s = srcIndex[i];
        result[i] = s < row.Length ? (row[s] ?? "") : "";
    }
    return result;
}
