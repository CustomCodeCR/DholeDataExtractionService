using CustomCodeFramework.Postgres.EntityFramework.Configurations;
using Dhole.DataExtraction.Domain.Extraction.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhole.DataExtraction.Persistence.Configurations.Extraction;

internal sealed class ColumnMappingProfileConfiguration
    : EntityTypeConfigurationBase<ColumnMappingProfile, Guid>
{
    public override void Configure(EntityTypeBuilder<ColumnMappingProfile> builder)
    {
        base.Configure(builder);

        builder.ToTable("ColumnMappingProfiles");

        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Code).HasMaxLength(100).IsRequired();

        builder.HasIndex(x => x.Code).IsUnique();

        builder.Property(x => x.Name).HasMaxLength(250).IsRequired();

        builder.Property(x => x.Description).HasMaxLength(1000);

        builder.Property(x => x.IsSystem).IsRequired().HasDefaultValue(false);

        builder.Property(x => x.IsActive).IsRequired().HasDefaultValue(true);

        builder
            .HasMany(x => x.Rules)
            .WithOne(x => x.ColumnMappingProfile)
            .HasForeignKey(x => x.ColumnMappingProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Rules).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
