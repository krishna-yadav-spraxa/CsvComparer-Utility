using System.Text;

// ─── Parse Options ────────────────────────────────────────────────────────────

var opts = AppOptions.Parse(args);
if (opts is null) Environment.Exit(1);

// ─── Parse & Validate CSVs ───────────────────────────────────────────────────

Console.WriteLine($"Old CSV  : {opts.OldPath}");
Console.WriteLine($"New CSV  : {opts.NewPath}");
Console.WriteLine();

var oldParsed = CsvParser.Parse(opts.OldPath);
var newParsed = CsvParser.Parse(opts.NewPath);

if (oldParsed.Count == 0) Abort($"File is empty: {opts.OldPath}");
if (newParsed.Count == 0) Abort($"File is empty: {opts.NewPath}");

string[] headers = oldParsed[0];

if (!headers.SequenceEqual(newParsed[0], StringComparer.OrdinalIgnoreCase))
    Abort($"Column headers do not match.\n  Old: {string.Join(", ", headers)}\n  New: {string.Join(", ", newParsed[0])}");

int colCount = headers.Length;
int keyColIdx = -1;

if (opts.KeyColumn is not null)
{
    keyColIdx = Array.FindIndex(headers, h => h.Equals(opts.KeyColumn, StringComparison.OrdinalIgnoreCase));
    if (keyColIdx < 0)
        Abort($"Key column '{opts.KeyColumn}' not found. Available: {string.Join(", ", headers)}");
}

var oldRows = oldParsed.Skip(1).ToList();
var newRows = newParsed.Skip(1).ToList();

Console.WriteLine($"Columns       : {colCount}");
Console.WriteLine($"Old data rows : {oldRows.Count:N0}");
Console.WriteLine($"New data rows : {newRows.Count:N0}");
if (opts.KeyColumn is not null) Console.WriteLine($"Key column    : {opts.KeyColumn}");
if (opts.Trim)                  Console.WriteLine("Trim          : on");
if (opts.IgnoreCase)            Console.WriteLine("Ignore case   : on");
Console.WriteLine();

// ─── Compare ─────────────────────────────────────────────────────────────────

var diffs = keyColIdx >= 0
    ? Comparer.ByKey(oldRows, newRows, colCount, keyColIdx, opts)
    : Comparer.ByPosition(oldRows, newRows, colCount, opts);

// ─── Write Diff CSV ──────────────────────────────────────────────────────────

DiffWriter.Write(diffs, headers, opts.OutputPath, keyColIdx >= 0);

// ─── Console Summary ─────────────────────────────────────────────────────────

int cntChanged  = diffs.Count(d => d.Status == "Changed");
int cntAdded    = diffs.Count(d => d.Status == "Added");
int cntRemoved  = diffs.Count(d => d.Status == "Removed");
int totalRows   = Math.Max(oldRows.Count, newRows.Count);
int cntIdentical = totalRows - cntChanged - cntAdded - cntRemoved;
double pctDiff  = totalRows > 0 ? (double)diffs.Count / totalRows : 0;

Console.WriteLine("─── Results ─────────────────────────────────────────────────");
Console.WriteLine($"  Identical  : {cntIdentical,8:N0}");
Console.WriteLine($"  Changed    : {cntChanged,8:N0}");
Console.WriteLine($"  Added      : {cntAdded,8:N0}");
Console.WriteLine($"  Removed    : {cntRemoved,8:N0}");
Console.WriteLine($"  Total diff : {diffs.Count,8:N0} / {totalRows:N0} rows  ({pctDiff:P1} differ)");
Console.WriteLine();

if (cntChanged > 0)
{
    var colChangeCounts = new int[colCount];
    foreach (var d in diffs.Where(d => d.Status == "Changed"))
        foreach (int i in d.ChangedIndices)
            colChangeCounts[i]++;

    Console.WriteLine("─── Column breakdown (Changed rows only) ─────────────────────");
    var ranked = colChangeCounts
        .Select((cnt, i) => (cnt, i))
        .Where(x => x.cnt > 0)
        .OrderByDescending(x => x.cnt);

    foreach (var (cnt, i) in ranked)
        Console.WriteLine($"  {headers[i],-40} {cnt,6:N0} row(s) differ");

    Console.WriteLine();
}

Console.WriteLine($"Diff CSV : {Path.GetFullPath(opts.OutputPath)}");

if (opts.SummaryPath is not null)
{
    DiffWriter.WriteSummary(diffs, headers, opts.SummaryPath);
    Console.WriteLine($"Summary  : {Path.GetFullPath(opts.SummaryPath)}");
}

// ─── Utility ─────────────────────────────────────────────────────────────────

static void Abort(string message)
{
    Console.Error.WriteLine($"ERROR: {message}");
    Environment.Exit(1);
}

// ─── Models ──────────────────────────────────────────────────────────────────

record DiffRow(
    int RowNumber,
    string? KeyValue,
    string Status,
    string[] OldValues,
    string[] NewValues,
    List<int> ChangedIndices
);

// ─── Comparer ─────────────────────────────────────────────────────────────────

static class Comparer
{
    /// Compare row-by-row using position (row 1 vs row 1, etc.)
    public static List<DiffRow> ByPosition(
        List<string[]> oldRows, List<string[]> newRows, int colCount, AppOptions opts)
    {
        var result = new List<DiffRow>();
        int max = Math.Max(oldRows.Count, newRows.Count);

        for (int i = 0; i < max; i++)
        {
            bool hasOld = i < oldRows.Count;
            bool hasNew = i < newRows.Count;
            string[] ov = hasOld ? Pad(oldRows[i], colCount) : EmptyRow(colCount);
            string[] nv = hasNew ? Pad(newRows[i], colCount) : EmptyRow(colCount);

            string status;
            List<int> changes;

            if (!hasOld)      { status = "Added";   changes = AllCols(colCount); }
            else if (!hasNew) { status = "Removed";  changes = AllCols(colCount); }
            else
            {
                changes = FindChanges(ov, nv, colCount, opts);
                if (changes.Count == 0) continue;
                status = "Changed";
            }

            result.Add(new DiffRow(i + 2, null, status, ov, nv, changes));
        }

        return result;
    }

    /// Compare rows matched by the value of a key column.
    public static List<DiffRow> ByKey(
        List<string[]> oldRows, List<string[]> newRows, int colCount, int keyIdx, AppOptions opts)
    {
        var result = new List<DiffRow>();
        var oldDict = BuildKeyDict(oldRows, keyIdx);
        var newDict = BuildKeyDict(newRows, keyIdx);
        var allKeys = new SortedSet<string>(
            oldDict.Keys.Concat(newDict.Keys),
            StringComparer.OrdinalIgnoreCase);

        foreach (string key in allKeys)
        {
            bool hasOld = oldDict.TryGetValue(key, out var oldEntry);
            bool hasNew = newDict.TryGetValue(key, out var newEntry);

            string[] ov = hasOld ? Pad(oldEntry!.Row, colCount) : EmptyRow(colCount);
            string[] nv = hasNew ? Pad(newEntry!.Row, colCount) : EmptyRow(colCount);
            int rowNum  = hasOld ? oldEntry!.LineNum : (hasNew ? newEntry!.LineNum : -1);

            string status;
            List<int> changes;

            if (!hasOld)      { status = "Added";   changes = AllCols(colCount); }
            else if (!hasNew) { status = "Removed";  changes = AllCols(colCount); }
            else
            {
                changes = FindChanges(ov, nv, colCount, opts);
                if (changes.Count == 0) continue;
                status = "Changed";
            }

            result.Add(new DiffRow(rowNum, key, status, ov, nv, changes));
        }

        return result;
    }

    static List<int> FindChanges(string[] a, string[] b, int colCount, AppOptions opts)
    {
        var comp = opts.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var changed = new List<int>();
        for (int c = 0; c < colCount; c++)
        {
            string va = opts.Trim ? a[c].Trim() : a[c];
            string vb = opts.Trim ? b[c].Trim() : b[c];
            if (!string.Equals(va, vb, comp)) changed.Add(c);
        }
        return changed;
    }

    static List<int> AllCols(int n) => Enumerable.Range(0, n).ToList();

    static string[] EmptyRow(int n) { var a = new string[n]; Array.Fill(a, ""); return a; }

    static string[] Pad(string[] row, int len)
    {
        if (row.Length >= len) return row;
        var r = EmptyRow(len);
        Array.Copy(row, r, row.Length);
        return r;
    }

    static Dictionary<string, (string[] Row, int LineNum)> BuildKeyDict(List<string[]> rows, int keyIdx)
    {
        var d = new Dictionary<string, (string[], int)>(StringComparer.Ordinal);
        for (int i = 0; i < rows.Count; i++)
        {
            string key = keyIdx < rows[i].Length ? rows[i][keyIdx] : "";
            if (!d.TryAdd(key, (rows[i], i + 2)))
                Console.Error.WriteLine($"  Warning: duplicate key '{key}' at row {i + 2} — first occurrence used.");
        }
        return d;
    }
}

// ─── DiffWriter ───────────────────────────────────────────────────────────────

static class DiffWriter
{
    /// Write the main diff CSV. One output row per differing source row.
    /// Columns: [KeyValue], RowNumber, Status, ChangedColumns, Col_Old, Col_New ...
    public static void Write(List<DiffRow> diffs, string[] headers, string path, bool keyMode)
    {
        int colCount = headers.Length;
        using var w = new StreamWriter(path, false, Encoding.UTF8);

        var headerRow = new List<string>();
        if (keyMode) headerRow.Add("KeyValue");
        headerRow.Add("RowNumber");
        headerRow.Add("Status");
        headerRow.Add("ChangedColumns");
        foreach (string h in headers)
        {
            headerRow.Add($"{h}_Old");
            headerRow.Add($"{h}_New");
        }
        w.WriteLine(Csv.Join(headerRow));

        foreach (var d in diffs)
        {
            string changedCols = d.Status == "Changed"
                ? string.Join("; ", d.ChangedIndices.Select(i => headers[i]))
                : string.Empty;

            var row = new List<string>();
            if (keyMode) row.Add(d.KeyValue ?? "");
            row.Add(d.RowNumber > 0 ? d.RowNumber.ToString() : "");
            row.Add(d.Status);
            row.Add(changedCols);

            for (int c = 0; c < colCount; c++)
            {
                row.Add(c < d.OldValues.Length ? d.OldValues[c] : "");
                row.Add(c < d.NewValues.Length ? d.NewValues[c] : "");
            }

            w.WriteLine(Csv.Join(row));
        }
    }

    /// Write a per-column summary CSV showing how many rows differ per column.
    public static void WriteSummary(List<DiffRow> diffs, string[] headers, string path)
    {
        int colCount = headers.Length;
        var changed = new int[colCount];
        var added   = new int[colCount];
        var removed = new int[colCount];

        foreach (var d in diffs)
        {
            if (d.Status == "Changed")
                foreach (int i in d.ChangedIndices) changed[i]++;
            else if (d.Status == "Added")
                for (int i = 0; i < colCount; i++) added[i]++;
            else if (d.Status == "Removed")
                for (int i = 0; i < colCount; i++) removed[i]++;
        }

        using var w = new StreamWriter(path, false, Encoding.UTF8);
        w.WriteLine(Csv.Join(["Column", "Changed", "Added", "Removed", "Total"]));
        for (int i = 0; i < colCount; i++)
        {
            int total = changed[i] + added[i] + removed[i];
            w.WriteLine(Csv.Join([headers[i], changed[i].ToString(), added[i].ToString(), removed[i].ToString(), total.ToString()]));
        }
    }
}

// ─── CSV Helpers ──────────────────────────────────────────────────────────────

static class Csv
{
    public static string Join(IEnumerable<string> fields) =>
        string.Join(",", fields.Select(Escape));

    static string Escape(string? v)
    {
        v ??= "";
        return v.Contains(',') || v.Contains('"') || v.Contains('\n') || v.Contains('\r')
            ? $"\"{v.Replace("\"", "\"\"")}\""
            : v;
    }
}

static class CsvParser
{
    public static List<string[]> Parse(string path)
    {
        var rows = new List<string[]>();
        using var r = new StreamReader(path, Encoding.UTF8);
        string? line;
        while ((line = r.ReadLine()) != null)
            if (!string.IsNullOrWhiteSpace(line))
                rows.Add(ParseLine(line));
        return rows;
    }

    static string[] ParseLine(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool inQ = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQ)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQ = false;
                }
                else sb.Append(c);
            }
            else
            {
                if      (c == '"') inQ = true;
                else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
        }

        fields.Add(sb.ToString());
        return [.. fields];
    }
}

// ─── AppOptions ───────────────────────────────────────────────────────────────

class AppOptions
{
    public string  OldPath     { get; private init; } = "";
    public string  NewPath     { get; private init; } = "";
    public string  OutputPath  { get; private init; } = "";
    public string? SummaryPath { get; private init; }
    public string? KeyColumn   { get; private init; }
    public bool    Trim        { get; private init; }
    public bool    IgnoreCase  { get; private init; }

    public static AppOptions? Parse(string[] a)
    {
        if (a.Length < 2) { PrintUsage(); return null; }

        if (!File.Exists(a[0])) { Console.Error.WriteLine($"ERROR: File not found: {a[0]}"); return null; }
        if (!File.Exists(a[1])) { Console.Error.WriteLine($"ERROR: File not found: {a[1]}"); return null; }

        string? key = null, output = null, summary = null;
        bool trim = false, ignoreCase = false;

        for (int i = 2; i < a.Length; i++)
        {
            switch (a[i].ToLower())
            {
                case "--key"          when i + 1 < a.Length: key     = a[++i]; break;
                case "--output"       when i + 1 < a.Length: output  = a[++i]; break;
                case "--summary"      when i + 1 < a.Length: summary = a[++i]; break;
                case "--trim":         trim       = true; break;
                case "--ignore-case":  ignoreCase = true; break;
                default:
                    Console.Error.WriteLine($"Unknown option: {a[i]}");
                    PrintUsage();
                    return null;
            }
        }

        return new AppOptions
        {
            OldPath     = a[0],
            NewPath     = a[1],
            OutputPath  = output ?? $"diff_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            SummaryPath = summary,
            KeyColumn   = key,
            Trim        = trim,
            IgnoreCase  = ignoreCase,
        };
    }

    static void PrintUsage() => Console.WriteLine("""
        Usage: CsvComparer <old.csv> <new.csv> [options]

        Arguments:
          old.csv              Original (baseline) CSV file
          new.csv              New (updated) CSV file

        Options:
          --output  <path>     Output diff CSV path  (default: diff_<timestamp>.csv)
          --key     <column>   Match rows by this column's value instead of row position
                               Use this when rows may be in different order between files
          --trim               Trim leading/trailing whitespace before comparing values
          --ignore-case        Case-insensitive value comparison
          --summary <path>     Write per-column diff counts to a separate summary CSV

        Output columns in diff CSV:
          [KeyValue]           Only present when --key is used
          RowNumber            Source file row number (1-based, includes header)
          Status               Changed | Added | Removed
          ChangedColumns       Semicolon-separated list of columns that differ (Changed rows)
          <Column>_Old         Value from old CSV for every original column
          <Column>_New         Value from new CSV for every original column

        Identical rows are omitted from the diff output.

        Examples:
          CsvComparer old.csv new.csv
          CsvComparer old.csv new.csv --output result.csv --summary summary.csv
          CsvComparer old.csv new.csv --key ProductID --trim --ignore-case
        """);
}
