// Compares two sets of CSV rows and returns the list of rows that differ.
// Supports positional matching (row N vs row N) and key-based matching (match by column value).
static class Comparer
{
    /// Compares rows by position: row 1 of old vs row 1 of new, etc.
    /// Extra rows at the end of either file are flagged as Added or Removed.
    public static List<DiffRow> ByPosition(
        List<string[]> oldRows, List<string[]> newRows, int colCount, AppOptions opts)
    {
        var result = new List<DiffRow>();
        int max    = Math.Max(oldRows.Count, newRows.Count);

        for (int i = 0; i < max; i++)
        {
            bool hasOld = i < oldRows.Count;
            bool hasNew = i < newRows.Count;
            string[] ov = hasOld ? oldRows[i] : EmptyRow(colCount);
            string[] nv = hasNew ? newRows[i] : EmptyRow(colCount);

            string    status;
            List<int> changes;

            if      (!hasOld) { status = "Added";   changes = AllColIndices(colCount); }
            else if (!hasNew) { status = "Removed";  changes = AllColIndices(colCount); }
            else
            {
                changes = FindChanges(ov, nv, colCount, opts);
                if (changes.Count == 0) continue; // identical row — skip
                status = "Changed";
            }

            result.Add(new DiffRow(i + 2, null, status, ov, nv, changes));
        }

        return result;
    }

    /// Compares rows by matching them on one or more key column values.
    /// Rows present in only one file are flagged as Added or Removed.
    public static List<DiffRow> ByKey(
        List<string[]> oldRows, List<string[]> newRows, int colCount, int[] keyIdx, AppOptions opts)
    {
        var result  = new List<DiffRow>();
        var oldDict = BuildIndex(oldRows, keyIdx);
        var newDict = BuildIndex(newRows, keyIdx);

        // Iterate all keys from both files in sorted order
        var allKeys = new SortedSet<string>(
            oldDict.Keys.Concat(newDict.Keys),
            StringComparer.OrdinalIgnoreCase);

        foreach (string key in allKeys)
        {
            bool hasOld = oldDict.TryGetValue(key, out var oe);
            bool hasNew = newDict.TryGetValue(key, out var ne);

            string[] ov    = hasOld ? oe!.Row : EmptyRow(colCount);
            string[] nv    = hasNew ? ne!.Row  : EmptyRow(colCount);
            int      rowNum = hasOld ? oe!.Line  : (hasNew ? ne!.Line : -1);

            string    status;
            List<int> changes;

            if      (!hasOld) { status = "Added";   changes = AllColIndices(colCount); }
            else if (!hasNew) { status = "Removed";  changes = AllColIndices(colCount); }
            else
            {
                changes = FindChanges(ov, nv, colCount, opts);
                if (changes.Count == 0) continue;
                status = "Changed";
            }

            // Replace the internal U+001F separator with | for human-readable display
            result.Add(new DiffRow(rowNum, key.Replace('\x1F', '|'), status, ov, nv, changes));
        }

        return result;
    }

    /// Returns the indices of columns whose values differ between two rows.
    static List<int> FindChanges(string[] a, string[] b, int colCount, AppOptions opts)
    {
        var comp    = opts.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var changed = new List<int>();
        for (int c = 0; c < colCount; c++)
        {
            string va = opts.Trim ? a[c].Trim() : a[c];
            string vb = opts.Trim ? b[c].Trim() : b[c];
            if (!string.Equals(va, vb, comp)) changed.Add(c);
        }
        return changed;
    }

    static List<int> AllColIndices(int n) => Enumerable.Range(0, n).ToList();

    static string[] EmptyRow(int n) { var a = new string[n]; Array.Fill(a, ""); return a; }

    /// Builds a dictionary keyed by composite key value (parts joined with U+001F to prevent collisions).
    static Dictionary<string, (string[] Row, int Line)> BuildIndex(List<string[]> rows, int[] keyIdx)
    {
        var d = new Dictionary<string, (string[], int)>(StringComparer.Ordinal);
        for (int i = 0; i < rows.Count; i++)
        {
            // U+001F (unit separator) between parts so ("A","BC") != ("AB","C")
            string key     = string.Join('\x1F', keyIdx.Select(ki => ki < rows[i].Length ? rows[i][ki] : ""));
            string display = key.Replace('\x1F', '|');
            if (!d.TryAdd(key, (rows[i], i + 2)))
                Console.Error.WriteLine($"  Warning: duplicate key '{display}' at data row {i + 1} — first occurrence used.");
        }
        return d;
    }
}
