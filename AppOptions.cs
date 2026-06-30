// Parses command-line arguments and exposes all options to the rest of the program.
class AppOptions
{
    public string   OldPath        { get; private init; } = "";
    public string   NewPath        { get; private init; } = "";
    public string   OutputPath     { get; private init; } = "";
    public string?  SummaryPath    { get; private init; }
    public string[] KeyColumns     { get; private init; } = [];
    public string[] ExcludeColumns { get; private init; } = [];
    public char?    Delimiter      { get; private init; } // null = auto-detect
    public bool     Trim           { get; private init; }
    public bool     IgnoreCase     { get; private init; }
    public bool     Compact        { get; private init; }

    public static AppOptions? Parse(string[] a)
    {
        if (a.Length < 2) { PrintUsage(); return null; }
        if (!File.Exists(a[0])) { Console.Error.WriteLine($"ERROR: File not found: {a[0]}"); return null; }
        if (!File.Exists(a[1])) { Console.Error.WriteLine($"ERROR: File not found: {a[1]}"); return null; }

        string? output = null, summary = null, key = null, exclude = null, delimArg = null;
        bool trim = false, ignoreCase = false, compact = false;

        for (int i = 2; i < a.Length; i++)
        {
            switch (a[i].ToLower())
            {
                case "--output"      when i + 1 < a.Length: output   = a[++i]; break;
                case "--summary"     when i + 1 < a.Length: summary  = a[++i]; break;
                case "--key"         when i + 1 < a.Length: key      = a[++i]; break;
                case "--exclude"     when i + 1 < a.Length: exclude  = a[++i]; break;
                case "--delimiter"   when i + 1 < a.Length: delimArg = a[++i]; break;
                case "--trim":         trim       = true; break;
                case "--ignore-case":  ignoreCase = true; break;
                case "--compact":      compact    = true; break;
                default:
                    Console.Error.WriteLine($"Unknown option: {a[i]}");
                    PrintUsage();
                    return null;
            }
        }

        char? delimiter = null;
        if (delimArg is not null)
        {
            delimiter = delimArg.ToLower() switch
            {
                "tab" or "\\t"     => '\t',
                "semicolon" or ";" => ';',
                "pipe" or "|"      => '|',
                "comma" or ","     => ',',
                _ when delimArg.Length == 1 => delimArg[0],
                _ => null
            };
            if (delimiter is null)
            {
                Console.Error.WriteLine($"ERROR: Invalid --delimiter '{delimArg}'. Use: comma  tab  semicolon  pipe  or a single character.");
                return null;
            }
        }

        static string[] Split(string? s) =>
            s is null ? [] : s.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        return new AppOptions
        {
            OldPath        = a[0],
            NewPath        = a[1],
            OutputPath     = output ?? $"diff_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            SummaryPath    = summary,
            KeyColumns     = Split(key),
            ExcludeColumns = Split(exclude),
            Delimiter      = delimiter,
            Trim           = trim,
            IgnoreCase     = ignoreCase,
            Compact        = compact,
        };
    }

    static void PrintUsage() => Console.WriteLine("""
        Usage: CsvComparer <old.csv> <new.csv> [options]

        Arguments:
          old.csv                      Baseline CSV file
          new.csv                      Updated CSV file

        Options:
          --delimiter <value>          Field delimiter (default: auto-detected from file content)
                                         comma      ->  ,  (standard CSV)
                                         tab        ->  \t (TSV files)
                                         semicolon  ->  ;  (European CSV)
                                         pipe       ->  |
                                         or any single character
          --key <col1,col2,...>        Match rows by key column(s) instead of row position.
                                       Use when rows may be in a different order between files.
                                       Composite keys: --key "OrderID,LineNo"
          --exclude <col1,col2,...>    Exclude columns from comparison and output.
                                       Useful for audit columns: --exclude "CreatedAt,UpdatedBy"
          --trim                       Trim leading/trailing whitespace before comparing values.
          --ignore-case                Case-insensitive value comparison.
          --compact                    Only include columns that differ in at least one row
                                       in the output. Reduces width for wide tables.
          --output <path>              Output diff CSV (default: diff_<timestamp>.csv)
          --summary <path>             Per-column diff count CSV (Changed/Added/Removed/Total)

        Output columns (diff CSV):
          [KeyValue]                   Pipe-joined composite key — only present when --key is used
          RowNumber                    Source file row number (1-based, counting header row)
          Status                       Changed | Added | Removed
          ChangedColumns               Semicolon-separated list of columns that differ (Changed rows)
          <Column>_Old / <Column>_New  Side-by-side values for each active column

        Exit codes:
          0  Files are identical (no differences found)
          1  Differences found
          2  Error (missing file, mismatched headers, bad arguments)

        Notes:
          - UTF-8 BOM is written to output files so Excel opens them correctly.
          - Multi-line quoted fields are supported in input files.
          - Identical rows are omitted from the diff output.

        Examples:
          CsvComparer old.csv new.csv
          CsvComparer old.csv new.csv --output result.csv --summary by-column.csv
          CsvComparer old.csv new.csv --key ProductID --trim --ignore-case --compact
          CsvComparer old.csv new.csv --key "OrderID,LineNo" --exclude "CreatedAt,UpdatedBy"
          CsvComparer data.tsv new.tsv --delimiter tab
          CsvComparer eu.csv  new.csv  --delimiter semicolon
        """);
}
