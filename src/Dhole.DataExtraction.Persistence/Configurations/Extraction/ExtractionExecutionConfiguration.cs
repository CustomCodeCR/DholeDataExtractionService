using CustomCodeFramework.Postgres.EntityFramework.Configurations;
using Dhole.DataExtraction.Domain.Extraction.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhole.DataExtraction.Persistence.Configurations.Extraction;

internal sealed class ExtractionExecutionConfiguration
    : EntityTypeConfigurationBase<ExtractionExecution, Guid>
{
    public override void Configure(EntityTypeBuilder<ExtractionExecution> builder)
    {
        base.Configure(builder);

        builder.ToTable("ExtractionExecutions");

        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.PricingImportId).IsRequired();

        builder.Property(x => x.CorrelationId).HasMaxLength(100).IsRequired();

        builder.Property(x => x.OriginalFileName).HasMaxLength(500).IsRequired();

        builder.Property(x => x.ContentType).HasMaxLength(250);

        builder.Property(x => x.FileExtension).HasMaxLength(20);

        builder.Property(x => x.FileSizeBytes).IsRequired();

        builder.Property(x => x.FileHash).HasMaxLength(128).IsRequired();

        builder
            .Property(x => x.SourceFileType)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.ProfileCode).HasMaxLength(100);
        builder.Property(x => x.SourceOriginType).HasMaxLength(100);
        builder.Property(x => x.SourceOriginId);
        builder.Property(x => x.SourceEmailMessageId);
        builder.Property(x => x.SourceEmailAttachmentId);

        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(50).IsRequired();

        builder.Property(x => x.TotalRows).IsRequired().HasDefaultValue(0);

        builder.Property(x => x.ValidRows).IsRequired().HasDefaultValue(0);

        builder.Property(x => x.WarningRows).IsRequired().HasDefaultValue(0);

        builder.Property(x => x.InvalidRows).IsRequired().HasDefaultValue(0);

        builder.Property(x => x.ErrorMessage).HasMaxLength(4000);

        builder.Property(x => x.RequestedBy);

        builder.Property(x => x.RequestedByName).HasMaxLength(250);

        builder.HasIndex(x => x.PricingImportId);

        builder.HasIndex(x => x.CorrelationId);

        builder.HasIndex(x => x.FileHash);

        builder.HasIndex(x => x.SourceOriginId);

        builder.HasIndex(x => x.SourceEmailMessageId);

        builder.HasIndex(x => x.Status);
    }
}
