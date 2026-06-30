// Formats field values into a properly escaped CSV output row.
static class CsvEscaper
{
    /// Joins a sequence of values into a single comma-separated, RFC-4180-escaped CSV line.
    public static string JoinRow(IEnumerable<string> fields) =>
        string.Join(",", fields.Select(Escape));

    /// Wraps a value in quotes and escapes internal quotes when the value contains
    /// commas, quotes, tabs, or newlines.
    static string Escape(string? v)
    {
        v ??= "";
        return v.IndexOfAny([',', '"', '\n', '\r', '\t']) >= 0
            ? $"\"{v.Replace("\"", "\"\"")}\""
            : v;
    }
}
