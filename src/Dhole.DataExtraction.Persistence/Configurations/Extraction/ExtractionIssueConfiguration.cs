using CustomCodeFramework.Postgres.EntityFramework.Configurations;
using Dhole.DataExtraction.Domain.Extraction.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhole.DataExtraction.Persistence.Configurations.Extraction;

internal sealed class ExtractionIssueConfiguration : EntityTypeConfigurationBase<ExtractionIssue, Guid>
{
    public override void Configure(EntityTypeBuilder<ExtractionIssue> builder)
    {
        base.Configure(builder);

        builder.ToTable("ExtractionIssues");

        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ExtractionExecutionId).IsRequired();
        builder.Property(x => x.PricingExtractionRecordId);
        builder.Property(x => x.Code).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Message).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.IsBlocking).IsRequired().HasDefaultValue(false);
        builder.Property(x => x.SourceSheetName).HasMaxLength(250);
        builder.Property(x => x.SourceRowNumber);
        builder.Property(x => x.ColumnName).HasMaxLength(250);
        builder.Property(x => x.RawValue).HasMaxLength(2000);

        builder.HasIndex(x => x.ExtractionExecutionId);
        builder.HasIndex(x => x.PricingExtractionRecordId);
        builder.HasIndex(x => x.Code);
        builder.HasIndex(x => x.IsBlocking);

        builder
            .HasOne<ExtractionExecution>()
            .WithMany()
            .HasForeignKey(x => x.ExtractionExecutionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne<PricingExtractionRecord>()
            .WithMany()
            .HasForeignKey(x => x.PricingExtractionRecordId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
