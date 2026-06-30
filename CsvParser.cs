using System.Text;

// Reads CSV / TSV files into a list of string arrays.
// Handles: BOM-aware encoding, configurable delimiter, multi-line quoted fields.
static class CsvParser
{
    /// Sniffs the most frequent field delimiter from outside-quoted characters on the first line.
    /// Falls back to comma when nothing else stands out.
    public static char DetectDelimiter(string path)
    {
        using var r = new StreamReader(path, detectEncodingFromByteOrderMarks: true);
        string? line = r.ReadLine();
        if (string.IsNullOrEmpty(line)) return ',';

        int tabs = 0, semis = 0, pipes = 0, commas = 0;
        bool inQ = false;
        foreach (char c in line)
        {
            if (c == '"') { inQ = !inQ; continue; }
            if (inQ) continue;
            if      (c == '\t') tabs++;
            else if (c == ';')  semis++;
            else if (c == '|')  pipes++;
            else if (c == ',')  commas++;
        }

        if (tabs  > commas && tabs  > semis && tabs  > pipes) return '\t';
        if (semis > commas && semis > tabs  && semis > pipes) return ';';
        if (pipes > commas && pipes > tabs  && pipes > semis) return '|';
        return ',';
    }

    /// Parses a file into rows of fields.
    /// Row 0 is the header. Uses BOM-aware encoding and supports multi-line quoted fields.
    public static List<string[]> Parse(string path, char delimiter)
    {
        var rows       = new List<string[]>();
        var lineBuffer = new StringBuilder();

        using var reader = new StreamReader(path, detectEncodingFromByteOrderMarks: true);
        string? line;

        while ((line = reader.ReadLine()) != null)
        {
            if (lineBuffer.Length > 0) lineBuffer.Append('\n');
            lineBuffer.Append(line);

            // Only finalize the row once all quotes are closed (handles newlines inside quoted fields)
            if (QuotesBalanced(lineBuffer))
            {
                string full = lineBuffer.ToString();
                if (!string.IsNullOrWhiteSpace(full))
                    rows.Add(SplitLine(full, delimiter));
                lineBuffer.Clear();
            }
        }

        // Flush any remaining content (malformed CSV with unclosed quote)
        if (lineBuffer.Length > 0)
        {
            string full = lineBuffer.ToString();
            if (!string.IsNullOrWhiteSpace(full))
                rows.Add(SplitLine(full, delimiter));
        }

        return rows;
    }

    /// Returns true when the buffer contains an even number of unescaped quotes (all fields closed).
    static bool QuotesBalanced(StringBuilder sb)
    {
        bool inQ = false;
        for (int i = 0; i < sb.Length; i++)
        {
            if (sb[i] != '"') continue;
            if (inQ && i + 1 < sb.Length && sb[i + 1] == '"') { i++; continue; } // escaped ""
            inQ = !inQ;
        }
        return !inQ;
    }

    /// Splits one logical CSV line into fields, respecting quoted strings.
    static string[] SplitLine(string line, char delimiter)
    {
        var  fields = new List<string>();
        var  sb     = new StringBuilder();
        bool inQ    = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQ)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } // escaped ""
                    else inQ = false;
                }
                else sb.Append(c);
            }
            else
            {
                if      (c == '"')       inQ = true;
                else if (c == delimiter) { fields.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
        }

        fields.Add(sb.ToString());
        return [.. fields];
    }
}
