namespace Dhole.DataExtraction.Domain.Shared;

public static class DataExtractionConstants
{
    public const string ServiceName = "DataExtraction";

    public static class Scopes
    {
        // Estos scopes son internos. DataExtraction no expone endpoints funcionales públicos.
        public const string ExtractionExecute = "data-extraction.extraction.execute";
        public const string ExtractionPreview = "data-extraction.extraction.preview";
        public const string ExtractionValidate = "data-extraction.extraction.validate";

        // Solo para endpoints de prueba en Development.
        public const string DevExtractionTest = "data-extraction.dev.extract";
    }

    public static class Audit
    {
        public static class EntityTypes
        {
            public const string ExtractionExecution = "ExtractionExecution";
            public const string SourceDocument = "SourceDocument";
            public const string PricingExtractionRecord = "PricingExtractionRecord";
            public const string ExtractionIssue = "ExtractionIssue";
            public const string ColumnMappingProfile = "ColumnMappingProfile";
            public const string ColumnMappingRule = "ColumnMappingRule";
        }

        public static class Actions
        {
            public const string Started = "started";
            public const string Completed = "completed";
            public const string Failed = "failed";
            public const string LowConfidenceDetected = "low_confidence_detected";

            public const string Created = "created";
            public const string Updated = "updated";
            public const string Deleted = "deleted";
            public const string Activated = "activated";
            public const string Inactivated = "inactivated";

            public const string DocumentReceived = "document_received";
            public const string RowsExtracted = "rows_extracted";
            public const string IssuesDetected = "issues_detected";
        }

        public static class EventTypes
        {
            // Extraction execution
            public const string ExtractionExecutionStarted = "data-extraction.execution.started";

            public const string ExtractionExecutionCompleted =
                "data-extraction.execution.completed";

            public const string ExtractionExecutionFailed = "data-extraction.execution.failed";

            public const string LowConfidenceExtractionDetected =
                "data-extraction.execution.low-confidence-detected";

            // Source document
            public const string SourceDocumentReceived = "data-extraction.source-document.received";

            // Pricing records
            public const string PricingRecordsExtracted =
                "data-extraction.pricing-records.extracted";

            // Issues
            public const string ExtractionIssuesDetected = "data-extraction.issues.detected";

            // Column mapping profile
            public const string ColumnMappingProfileCreated =
                "data-extraction.column-mapping-profile.created";

            public const string ColumnMappingProfileUpdated =
                "data-extraction.column-mapping-profile.updated";

            public const string ColumnMappingProfileDeleted =
                "data-extraction.column-mapping-profile.deleted";

            public const string ColumnMappingProfileActivated =
                "data-extraction.column-mapping-profile.activated";

            public const string ColumnMappingProfileInactivated =
                "data-extraction.column-mapping-profile.inactivated";
        }
    }
}
