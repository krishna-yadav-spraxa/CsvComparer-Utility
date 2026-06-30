// Holds the comparison result for a single row that differs between the two files.
record DiffRow(
    int      RowNumber,      // 1-based source file row number (includes header row)
    string?  KeyValue,       // Pipe-joined key value — only set when --key is used
    string   Status,         // "Changed" | "Added" | "Removed"
    string[] OldValues,      // Field values from the old CSV (active columns only)
    string[] NewValues,      // Field values from the new CSV (active columns only)
    List<int> ChangedIndices // Indices (into active columns) that differ — empty for Added/Removed
);
