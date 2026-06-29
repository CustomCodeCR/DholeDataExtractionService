namespace Dhole.DataExtraction.Infrastructure.Cache;

public static class DataExtractionCacheKeys
{
    public static string SourceDocumentByHash(string fileHash) =>
        $"data-extraction:source-document:hash:{fileHash}";

    public static string ExtractedRows(Guid extractionExecutionId) =>
        $"data-extraction:rows:{extractionExecutionId}";
}
