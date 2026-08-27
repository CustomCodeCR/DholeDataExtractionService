from pathlib import Path


def replace_exact(path: Path, old: str, new: str, expected: int = 1) -> None:
    text = path.read_text(encoding="utf-8")
    count = text.count(old)
    if count != expected:
        raise RuntimeError(
            f"{path}: expected {expected} occurrence(s), found {count}: {old[:100]!r}"
        )
    path.write_text(text.replace(old, new), encoding="utf-8")


service = Path(
    "src/Dhole.DataExtraction.Infrastructure/Pipeline/AutomatedPricingExtractionService.cs"
)
replace_exact(
    service,
    '"AI solo puede normalizar cuerpo de correo, PDF, CSV o XLSX."',
    '"AI solo puede normalizar cuerpo de correo, PDF, CSV o Excel (XLS, XLSX o XLSM)."',
)
replace_exact(
    service,
    'return extension is ".pdf" or ".csv" or ".xlsx";',
    'return extension is ".pdf" or ".csv" or ".xls" or ".xlsx" or ".xlsm";',
)
replace_exact(
    service,
    '"El formato no se procesa. DataExtraction solo admite cuerpo de correo, PDF, CSV o XLSX; las imágenes y demás archivos únicamente se almacenan."',
    '"El formato no se procesa. DataExtraction solo admite cuerpo de correo, PDF, CSV o Excel (XLS, XLSX o XLSM); las imágenes y demás archivos únicamente se almacenan."',
)

excel_clause_old = '&& attachment.FileExtension.ToLower() == ".xlsx")'
excel_clause_new = '''&& (
                            attachment.FileExtension.ToLower() == ".xlsx"
                            || attachment.FileExtension.ToLower() == ".xlsm"
                            || attachment.FileExtension.ToLower() == ".xls"
                        ))'''

legacy_worker = Path(
    "src/Dhole.DataExtraction.Workers/Workers/LegacyEmailExtractionWorker.cs"
)
replace_exact(legacy_worker, excel_clause_old, excel_clause_new, expected=1)

worker_di = Path(
    "src/Dhole.DataExtraction.Workers/DependencyInjection/WorkerServiceCollectionExtensions.cs"
)
replace_exact(
    worker_di,
    '''        if (emailIngestionEnabled)
        {
            services.AddCustomCodePeriodicWorker<EmailPollingWorker>();
''',
    '''        if (emailIngestionEnabled)
        {
            services.AddCustomCodePeriodicWorker<EmailPollingWorker>();
            services.AddCustomCodePeriodicWorker<LegacyExcelAiRecoveryWorker>();
''',
)

recovery_worker = Path(
    "src/Dhole.DataExtraction.Workers/Workers/LegacyExcelAiRecoveryWorker.cs"
)
recovery_worker.write_text(
    '''using CustomCodeFramework.Workers.Abstractions;
using Dhole.DataExtraction.Domain.Emails.Enums;
using Dhole.DataExtraction.Domain.Extraction.Enums;
using Dhole.DataExtraction.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.DataExtraction.Workers.Workers;

/// <summary>
/// Requeues legacy Excel attachments that were incorrectly rejected by older
/// deployments whose AI allow-list only contained XLSX. This worker is intentionally
/// narrow so unrelated failed jobs are never retried automatically.
/// </summary>
internal sealed class LegacyExcelAiRecoveryWorker(
    ServiceDbContext dbContext,
    IConfiguration configuration,
    ILogger<LegacyExcelAiRecoveryWorker> logger
) : IBackgroundWorker
{
    private const string OldAiMessage =
        "AI solo puede normalizar cuerpo de correo, PDF, CSV o XLSX";
    private const string OldDataExtractionMessage =
        "DataExtraction solo admite cuerpo de correo, PDF, CSV o XLSX";

    public string Name => "data-extraction.legacy-excel-ai-recovery";

    public async Task ExecuteAsync(
        IWorkerExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        if (!ReadBoolean(configuration["EmailIngestion:Enabled"], false))
        {
            return;
        }

        var candidates = await (
            from job in dbContext.EmailExtractionJobs
            join attachment in dbContext.EmailAttachments
                on job.EmailAttachmentId equals (Guid?)attachment.Id
            where !job.IsDeleted
                && !attachment.IsDeleted
                && job.SourceType == EmailContentSourceType.Attachment
                && (job.Status == EmailExtractionJobStatus.Failed
                    || job.Status == EmailExtractionJobStatus.NeedsReview)
                && attachment.SourceFileType == SourceFileType.Excel
                && attachment.FileExtension != null
                && (attachment.FileExtension.ToLower() == ".xls"
                    || attachment.FileExtension.ToLower() == ".xlsm")
                && job.ErrorMessage != null
                && (job.ErrorMessage.Contains(OldAiMessage)
                    || job.ErrorMessage.Contains(OldDataExtractionMessage))
            orderby job.CreatedAtUtc
            select job
        )
            .Take(250)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return;
        }

        var messageIds = new HashSet<Guid>();
        foreach (var job in candidates)
        {
            job.Retry();
            messageIds.Add(job.EmailMessageId);
        }

        foreach (var messageId in messageIds)
        {
            await EmailJobStateCoordinator.RecalculateAsync(
                dbContext,
                messageId,
                cancellationToken
            );
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogWarning(
            "Se reencolaron {JobCount} adjuntos XLS/XLSM rechazados por la allow-list antigua de AI.",
            candidates.Count
        );
    }

    private static bool ReadBoolean(string? value, bool fallback)
    {
        return bool.TryParse(value, out var parsed) ? parsed : fallback;
    }
}
''',
    encoding="utf-8",
)

unit_tests = Path(
    "tests/Dhole.DataExtraction.UnitTests/AutomatedPricingExtractionServiceTests.cs"
)
replace_exact(
    unit_tests,
    'StringAssert.Contains(exception.Message, "PDF, CSV o XLSX");',
    'StringAssert.Contains(exception.Message, "XLS, XLSX o XLSM");',
)
legacy_test = '''    [TestMethod]
    public async Task PrepareAiRequest_AcceptsLegacyExcelAttachment()
    {
        var pricingImportId = Guid.NewGuid();
        var content = Encoding.UTF8.GetBytes("legacy-excel-rate");
        var service = new AutomatedPricingExtractionService(
            new RecordingPipeline(Success(pricingImportId)),
            new ExplodingAiClient(),
            new FakeContentReader(),
            new EmptyConfigCatalogClient(),
            new ConfigurationBuilder().Build(),
            NullLogger<AutomatedPricingExtractionService>.Instance
        );
        var request = new ExtractionDataRequest(
            pricingImportId,
            "legacy-xls-test",
            "YML ASCA FAK TARIFF.xls",
            "application/vnd.ms-excel",
            ".xls",
            content.LongLength,
            "xls-hash",
            null,
            null,
            "unit-test",
            content
        )
        {
            SourceOriginType = "EmailAttachment",
            SourceEmailMessageId = Guid.NewGuid(),
            SourceEmailAttachmentId = Guid.NewGuid(),
            StoragePath = "emails/YML ASCA FAK TARIFF.xls",
        };

        var prepared = await service.PrepareAiRequestAsync(
            request,
            Success(pricingImportId),
            new AutomatedPricingExtractionContext(
                request.SourceEmailMessageId,
                request.SourceEmailAttachmentId,
                "sender@example.com",
                "YML FAK tariff",
                "Tarifa adjunta",
                null,
                "EmailAttachment",
                ForceAiAnalysis: true
            ),
            request.StoragePath
        );

        Assert.IsNotNull(prepared);
    }

'''
replace_exact(
    unit_tests,
    '    private static ExtractPricingDataResponse Failure(Guid pricingImportId)\n',
    legacy_test + '    private static ExtractPricingDataResponse Failure(Guid pricingImportId)\n',
)

print("Legacy Excel attachment support patch applied.")
