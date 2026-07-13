using CustomCodeFramework.Postgres.EntityFramework.Configurations;
using Dhole.DataExtraction.Domain.Extraction.Entities;
using Dhole.DataExtraction.Domain.Extraction.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhole.DataExtraction.Persistence.Configurations.Extraction;

internal sealed class PricingExtractionRecordConfiguration
    : EntityTypeConfigurationBase<PricingExtractionRecord, Guid>
{
    public override void Configure(EntityTypeBuilder<PricingExtractionRecord> builder)
    {
        base.Configure(builder);

        builder.ToTable("PricingExtractionRecords");

        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.ExtractionExecutionId).IsRequired();

        builder.Property(x => x.SourceDocumentId).IsRequired();

        builder.Property(x => x.SourceSheetName).HasMaxLength(250);

        builder.Property(x => x.SourceRowNumber);

        builder.Property(x => x.OriginPort).HasMaxLength(250);

        builder.Property(x => x.PortOfExit).HasMaxLength(250);

        builder.Property(x => x.DestinationPort).HasMaxLength(250);

        builder.Property(x => x.ContainerType).HasMaxLength(50);

        builder.Property(x => x.Carrier).HasMaxLength(250);

        builder.Property(x => x.Agent).HasMaxLength(250);

        builder.Property(x => x.Commodity).HasMaxLength(250);

        builder.Property(x => x.Currency).HasMaxLength(10);

        builder.Property(x => x.FreeDays);

        builder.Property(x => x.TransitDays);

        ConfigureCatalogReference(
            builder.OwnsOne(x => x.OriginPortReference),
            "origin_port"
        );
        ConfigureCatalogReference(
            builder.OwnsOne(x => x.PortOfExitReference),
            "port_of_exit"
        );
        ConfigureCatalogReference(
            builder.OwnsOne(x => x.DestinationPortReference),
            "destination_port"
        );
        ConfigureCatalogReference(
            builder.OwnsOne(x => x.ContainerTypeReference),
            "container_type"
        );
        ConfigureCatalogReference(builder.OwnsOne(x => x.CarrierReference), "carrier");
        ConfigureCatalogReference(builder.OwnsOne(x => x.AgentReference), "agent");
        ConfigureCatalogReference(builder.OwnsOne(x => x.CurrencyReference), "currency");

        builder.Property(x => x.OceanFreight).HasPrecision(18, 4);

        builder.Property(x => x.OriginCharges).HasPrecision(18, 4);

        builder.Property(x => x.DestinationCharges).HasPrecision(18, 4);

        builder.Property(x => x.Surcharges).HasPrecision(18, 4);

        builder.Property(x => x.TotalCost).HasPrecision(18, 4);

        builder.Property(x => x.TotalSale).HasPrecision(18, 4);

        builder.Property(x => x.Profit).HasPrecision(18, 4);

        builder.Property(x => x.Margin).HasPrecision(18, 4);

        builder.Property(x => x.SpaceComment).HasMaxLength(2000);

        builder.Property(x => x.Remarks).HasMaxLength(2000);

        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(50).IsRequired();

        builder.Property(x => x.RawJson).HasColumnType("jsonb");

        builder.HasIndex(x => x.ExtractionExecutionId);

        builder.HasIndex(x => x.SourceDocumentId);

        builder.HasIndex(x => x.Status);

        builder.HasIndex(x => new
        {
            x.OriginPort,
            x.DestinationPort,
            x.ContainerType,
        });

        builder
            .HasOne<ExtractionExecution>()
            .WithMany()
            .HasForeignKey(x => x.ExtractionExecutionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne<SourceDocument>()
            .WithMany()
            .HasForeignKey(x => x.SourceDocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureCatalogReference(
        OwnedNavigationBuilder<PricingExtractionRecord, CatalogItemReference> owned,
        string prefix
    )
    {
        owned.Property(x => x.CatalogItemId)
            .HasColumnName($"{prefix}_catalog_item_id")
            .IsRequired();
        owned.Property(x => x.CatalogGroupSlug)
            .HasColumnName($"{prefix}_catalog_group_slug")
            .HasMaxLength(100)
            .IsRequired();
        owned.Property(x => x.Code)
            .HasColumnName($"{prefix}_catalog_code")
            .HasMaxLength(100)
            .IsRequired();
        owned.Property(x => x.Slug)
            .HasColumnName($"{prefix}_catalog_slug")
            .HasMaxLength(150)
            .IsRequired();
        owned.Property(x => x.Name)
            .HasColumnName($"{prefix}_catalog_name")
            .HasMaxLength(250)
            .IsRequired();
        owned.Property(x => x.RawValue)
            .HasColumnName($"{prefix}_raw_value")
            .HasMaxLength(500);

        owned.HasIndex(x => x.CatalogItemId);
    }
}
