using CustomCodeFramework.Postgres.EntityFramework.Configurations;
using Dhole.DataExtraction.Domain.Extraction.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhole.DataExtraction.Persistence.Configurations.Extraction;

internal sealed class SourceDocumentConfiguration
    : EntityTypeConfigurationBase<SourceDocument, Guid>
{
    public override void Configure(EntityTypeBuilder<SourceDocument> builder)
    {
        base.Configure(builder);

        builder.ToTable("SourceDocuments");

        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.ExtractionExecutionId).IsRequired();

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

        builder.Property(x => x.StoragePath).HasMaxLength(1000);

        builder.HasIndex(x => x.ExtractionExecutionId);

        builder.HasIndex(x => x.FileHash);

        builder
            .HasOne<ExtractionExecution>()
            .WithMany()
            .HasForeignKey(x => x.ExtractionExecutionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
