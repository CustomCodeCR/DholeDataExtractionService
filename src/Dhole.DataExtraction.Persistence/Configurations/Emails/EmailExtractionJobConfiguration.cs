using CustomCodeFramework.Postgres.EntityFramework.Configurations;
using Dhole.DataExtraction.Domain.Emails.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhole.DataExtraction.Persistence.Configurations.Emails;

internal sealed class EmailExtractionJobConfiguration : EntityTypeConfigurationBase<EmailExtractionJob, Guid>
{
    public override void Configure(EntityTypeBuilder<EmailExtractionJob> builder)
    {
        base.Configure(builder);

        builder.ToTable("EmailExtractionJobs");
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.EmailMessageId).IsRequired();
        builder.Property(x => x.EmailAttachmentId);
        builder.Property(x => x.SourceType).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(x => x.ProvisionalPricingImportId).IsRequired();
        builder.Property(x => x.ExtractionExecutionId);
        builder.Property(x => x.PricingImportBatchId);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(x => x.ConfidenceScore).HasPrecision(5, 2);
        builder.Property(x => x.ErrorMessage).HasMaxLength(4000);
        builder.Property(x => x.StartedAt);
        builder.Property(x => x.FinishedAt);

        builder.HasIndex(x => x.EmailMessageId);
        builder.HasIndex(x => x.EmailAttachmentId);
        builder.HasIndex(x => x.ExtractionExecutionId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.ProvisionalPricingImportId);
    }
}
