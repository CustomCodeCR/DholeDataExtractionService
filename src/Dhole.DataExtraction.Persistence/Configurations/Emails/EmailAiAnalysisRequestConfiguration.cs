using CustomCodeFramework.Postgres.EntityFramework.Configurations;
using Dhole.DataExtraction.Domain.Emails.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhole.DataExtraction.Persistence.Configurations.Emails;

internal sealed class EmailAiAnalysisRequestConfiguration
    : EntityTypeConfigurationBase<EmailAiAnalysisRequest, Guid>
{
    public override void Configure(EntityTypeBuilder<EmailAiAnalysisRequest> builder)
    {
        base.Configure(builder);

        builder.ToTable("EmailAiAnalysisRequests");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.EmailExtractionJobId).IsRequired();
        builder.Property(x => x.EmailMessageId).IsRequired();
        builder.Property(x => x.EmailAttachmentId);
        builder.Property(x => x.ExtractionExecutionId);
        builder.Property(x => x.ProvisionalPricingImportId).IsRequired();
        builder.Property(x => x.CorrelationId).HasMaxLength(150).IsRequired();
        builder.Property(x => x.RequestHash).HasMaxLength(128).IsRequired();
        builder.Property(x => x.PayloadJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.ImageStoragePath).HasMaxLength(1000);
        builder.Property(x => x.ImageContentType).HasMaxLength(250);
        builder.Property(x => x.CompletedAtUtc);

        builder.HasIndex(x => x.EmailExtractionJobId);
        builder.HasIndex(x => x.EmailMessageId);
        builder.HasIndex(x => x.EmailAttachmentId);
        builder.HasIndex(x => x.ExtractionExecutionId);
        builder.HasIndex(x => x.RequestHash);
        builder.HasIndex(x => x.CompletedAtUtc);
        builder
            .HasIndex(x => new { x.EmailExtractionJobId, x.RequestHash })
            .HasDatabaseName("ix_email_ai_requests_job_hash");
    }
}
