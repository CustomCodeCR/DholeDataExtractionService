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
        builder.Property(x => x.AiRequestId);
        builder.Property(x => x.AiExecutionId);
        builder.Property(x => x.AiRequestHash).HasMaxLength(128);
        builder.Property(x => x.PricingRequestId);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(x => x.ConfidenceScore).HasPrecision(5, 2);
        builder.Property(x => x.ErrorMessage).HasMaxLength(4000);
        builder.Property(x => x.LastErrorCode).HasMaxLength(250);
        builder.Property(x => x.AttemptCount).HasDefaultValue(0).IsRequired();
        builder.Property(x => x.NextAttemptAtUtc);
        builder.Property(x => x.LeaseOwner).HasMaxLength(250);
        builder.Property(x => x.LeaseExpiresAtUtc);
        builder.Property(x => x.LastHeartbeatAtUtc);
        builder.Property(x => x.Version).HasDefaultValue(1).IsConcurrencyToken();
        builder.Property(x => x.StartedAt);
        builder.Property(x => x.FinishedAt);

        builder
            .HasOne<EmailMessage>()
            .WithMany()
            .HasForeignKey(x => x.EmailMessageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne<EmailAttachment>()
            .WithMany()
            .HasForeignKey(x => x.EmailAttachmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.EmailMessageId);
        builder.HasIndex(x => x.EmailAttachmentId);
        builder.HasIndex(x => x.ExtractionExecutionId);
        builder.HasIndex(x => x.AiRequestId).IsUnique();
        builder.HasIndex(x => x.PricingRequestId).IsUnique();
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.ProvisionalPricingImportId);
        builder
            .HasIndex(x => new
            {
                x.Status,
                x.NextAttemptAtUtc,
                x.CreatedAtUtc,
            })
            .HasDatabaseName("i_x_email_extraction_jobs_status_next_attempt_created");
        builder
            .HasIndex(x => new
            {
                x.Status,
                x.LeaseExpiresAtUtc,
            })
            .HasDatabaseName("i_x_email_extraction_jobs_status_lease");
    }
}
