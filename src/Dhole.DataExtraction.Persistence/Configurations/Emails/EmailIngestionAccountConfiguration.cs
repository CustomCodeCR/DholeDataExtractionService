using CustomCodeFramework.Postgres.EntityFramework.Configurations;
using Dhole.DataExtraction.Domain.Emails.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhole.DataExtraction.Persistence.Configurations.Emails;

internal sealed class EmailIngestionAccountConfiguration
    : EntityTypeConfigurationBase<EmailIngestionAccount, Guid>
{
    public override void Configure(EntityTypeBuilder<EmailIngestionAccount> builder)
    {
        base.Configure(builder);

        builder.ToTable("EmailIngestionAccounts");
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.EmailAddress).HasMaxLength(320).IsRequired();
        builder.Property(x => x.ProviderType).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(x => x.Host).HasMaxLength(250).IsRequired();
        builder.Property(x => x.Port).IsRequired();
        builder.Property(x => x.UseSsl).IsRequired();
        builder.Property(x => x.Username).HasMaxLength(320).IsRequired();
        builder.Property(x => x.SecretReference).HasMaxLength(500).IsRequired();
        builder.Property(x => x.FolderName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.PollingIntervalMinutes).IsRequired().HasDefaultValue(5);
        builder.Property(x => x.AutoProcess).IsRequired().HasDefaultValue(true);
        builder.Property(x => x.AutoSendToPricing).IsRequired().HasDefaultValue(false);
        builder.Property(x => x.AutoSendMinConfidence).HasPrecision(5, 2).IsRequired().HasDefaultValue(90m);
        builder.Property(x => x.ProcessBodyWhenNoSupportedAttachments).IsRequired().HasDefaultValue(true);
        builder.Property(x => x.ProcessBodyEvenWithAttachments).IsRequired().HasDefaultValue(false);
        builder.Property(x => x.AllowedSenders).HasMaxLength(4000);
        builder.Property(x => x.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(x => x.LastProcessedUid);
        builder.Property(x => x.LastSyncAt);
        builder.Property(x => x.LastSyncError).HasMaxLength(4000);

        builder.HasIndex(x => x.EmailAddress).IsUnique();
        builder.HasIndex(x => x.IsActive);
        builder.HasIndex(x => x.ProviderType);
    }
}
