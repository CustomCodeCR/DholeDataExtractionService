using CustomCodeFramework.Postgres.EntityFramework.Configurations;
using Dhole.DataExtraction.Domain.Emails.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhole.DataExtraction.Persistence.Configurations.Emails;

internal sealed class EmailAttachmentConfiguration : EntityTypeConfigurationBase<EmailAttachment, Guid>
{
    public override void Configure(EntityTypeBuilder<EmailAttachment> builder)
    {
        base.Configure(builder);

        builder.ToTable("EmailAttachments");
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.EmailMessageId).IsRequired();
        builder.Property(x => x.FileName).HasMaxLength(500).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(250);
        builder.Property(x => x.FileExtension).HasMaxLength(20);
        builder.Property(x => x.SizeBytes).IsRequired();
        builder.Property(x => x.FileHash).HasMaxLength(128).IsRequired();
        builder.Property(x => x.StoragePath).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.SourceFileType).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(x => x.ErrorMessage).HasMaxLength(4000);
        builder.Property(x => x.ProcessedAt);

        builder.HasIndex(x => x.EmailMessageId);
        builder.HasIndex(x => x.FileHash);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => new { x.EmailMessageId, x.FileHash }).IsUnique();
    }
}
