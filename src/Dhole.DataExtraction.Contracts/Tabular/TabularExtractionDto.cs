namespace Dhole.DataExtraction.Contracts.Tabular;

public sealed record TabularExtractionDto(
    string FileName,
    string FileType,
    IReadOnlyCollection<TabularSheetDto> Sheets,
    int TotalRows,
    int IncludedRows,
    bool IsTruncated
);

public sealed record TabularSheetDto(
    string? Name,
    IReadOnlyCollection<string> Headers,
    IReadOnlyCollection<TabularRowDto> Rows
);

public sealed record TabularRowDto(
    int RowNumber,
    IReadOnlyDictionary<string, string?> Values
);
