using CustomCodeFramework.Postgres.EntityFramework.Configurations;
using Dhole.DataExtraction.Domain.Extraction.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhole.DataExtraction.Persistence.Configurations.Extraction;

internal sealed class ColumnMappingRuleConfiguration
    : EntityTypeConfigurationBase<ColumnMappingRule, Guid>
{
    public override void Configure(EntityTypeBuilder<ColumnMappingRule> builder)
    {
        base.Configure(builder);

        builder.ToTable("ColumnMappingRules");

        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.ColumnMappingProfileId).IsRequired();

        builder.Property(x => x.SourceColumnName).HasMaxLength(250).IsRequired();

        builder.Property(x => x.NormalizedSourceColumnName).HasMaxLength(250).IsRequired();

        builder.Property(x => x.TargetField).HasMaxLength(250).IsRequired();

        builder.Property(x => x.IsRequired).IsRequired().HasDefaultValue(false);

        builder.Property(x => x.Priority).IsRequired().HasDefaultValue(0);

        builder.Property(x => x.DefaultValue).HasMaxLength(500);

        builder.Property(x => x.TransformExpression).HasMaxLength(1000);

        builder.Property(x => x.MetadataJson).HasColumnType("jsonb");

        builder.Property(x => x.IsActive).IsRequired().HasDefaultValue(true);

        builder.HasIndex(x => x.ColumnMappingProfileId);

        builder.HasIndex(x => new { x.ColumnMappingProfileId, x.NormalizedSourceColumnName });

        builder.HasIndex(x => new { x.ColumnMappingProfileId, x.TargetField });

        builder
            .HasOne(x => x.ColumnMappingProfile)
            .WithMany(x => x.Rules)
            .HasForeignKey(x => x.ColumnMappingProfileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
