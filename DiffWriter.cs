using System.Text;

// Writes comparison results to CSV files.
static class DiffWriter
{
    // UTF-8 with BOM so Excel opens the files without a character-encoding dialog
    static readonly Encoding Utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    /// Writes the main diff CSV — one output row per source row that differs.
    ///
    /// Output columns:
    ///   [KeyValue]  RowNumber  Status  ChangedColumns  <Col>_Old  <Col>_New ...
    ///
    /// compactCols: when non-null, only _Old/_New columns in this set are emitted.
    public static void Write(
        List<DiffRow> diffs,
        string[]      headers,
        string        path,
        bool          keyMode,
        HashSet<int>? compactCols)
    {
        int  colCount = headers.Length;
        bool IsShown(int c) => compactCols is null || compactCols.Contains(c);

        using var w = new StreamWriter(path, false, Utf8Bom);

        // Header row
        var hdr = new List<string>();
        if (keyMode) hdr.Add("KeyValue");
        hdr.Add("RowNumber");
        hdr.Add("Status");
        hdr.Add("ChangedColumns");
        for (int c = 0; c < colCount; c++)
            if (IsShown(c)) { hdr.Add($"{headers[c]}_Old"); hdr.Add($"{headers[c]}_New"); }
        w.WriteLine(CsvEscaper.JoinRow(hdr));

        // Data rows
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
                if (!IsShown(c)) continue;
                row.Add(c < d.OldValues.Length ? d.OldValues[c] : "");
                row.Add(c < d.NewValues.Length ? d.NewValues[c] : "");
            }

            w.WriteLine(CsvEscaper.JoinRow(row));
        }
    }

    /// Writes a per-column summary CSV showing how many rows differ per column.
    ///
    /// Output columns: Column  Changed  Added  Removed  Total
    public static void WriteSummary(List<DiffRow> diffs, string[] headers, string path)
    {
        int colCount = headers.Length;
        var changed  = new int[colCount];
        var added    = new int[colCount];
        var removed  = new int[colCount];

        foreach (var d in diffs)
        {
            if      (d.Status == "Changed") foreach (int i in d.ChangedIndices) changed[i]++;
            else if (d.Status == "Added")   for (int i = 0; i < colCount; i++) added[i]++;
            else if (d.Status == "Removed") for (int i = 0; i < colCount; i++) removed[i]++;
        }

        using var w = new StreamWriter(path, false, Utf8Bom);
        w.WriteLine(CsvEscaper.JoinRow(["Column", "Changed", "Added", "Removed", "Total"]));
        for (int i = 0; i < colCount; i++)
        {
            int total = changed[i] + added[i] + removed[i];
            w.WriteLine(CsvEscaper.JoinRow([
                headers[i],
                changed[i].ToString(),
                added[i].ToString(),
                removed[i].ToString(),
                total.ToString()
            ]));
        }
    }
}
