using CustomCodeFramework.Postgres.EntityFramework.Configurations;
using Dhole.DataExtraction.Domain.Emails.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhole.DataExtraction.Persistence.Configurations.Emails;

internal sealed class EmailMessageConfiguration : EntityTypeConfigurationBase<EmailMessage, Guid>
{
    public override void Configure(EntityTypeBuilder<EmailMessage> builder)
    {
        base.Configure(builder);

        builder.ToTable("EmailMessages");
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.EmailIngestionAccountId).IsRequired();
        builder.Property(x => x.ExternalMessageId).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Uid);
        builder.Property(x => x.MessageIdHeader).HasMaxLength(500);
        builder.Property(x => x.FromName).HasMaxLength(250);
        builder.Property(x => x.FromAddress).HasMaxLength(320).IsRequired();
        builder.Property(x => x.ToAddresses).HasMaxLength(2000);
        builder.Property(x => x.CcAddresses).HasMaxLength(2000);
        builder.Property(x => x.Subject).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.BodyText);
        builder.Property(x => x.BodyHtml);
        builder.Property(x => x.ReceivedAt).IsRequired();
        builder.Property(x => x.HasAttachments).IsRequired();
        builder.Property(x => x.RawEmailStoragePath).HasMaxLength(1000);
        builder.Property(x => x.RawMetadataJson);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(x => x.ErrorMessage).HasMaxLength(4000);
        builder.Property(x => x.ClassificationConfidence).HasPrecision(5, 2);
        builder.Property(x => x.ClassificationReason).HasMaxLength(1000);
        builder.Property(x => x.ProcessedAt);

        builder.HasIndex(x => new { x.EmailIngestionAccountId, x.ExternalMessageId }).IsUnique();
        builder.HasIndex(x => x.EmailIngestionAccountId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.ReceivedAt);
        builder.HasIndex(x => x.FromAddress);
    }
}
