using Dhole.DataExtraction.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.DataExtraction.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260826211000_ExpandPricingExtractionContainerType")]
public sealed class ExpandPricingExtractionContainerType : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DO $$
            BEGIN
                IF to_regclass('data_extraction."PricingExtractionRecords"') IS NOT NULL THEN
                    ALTER TABLE data_extraction."PricingExtractionRecords"
                        ALTER COLUMN container_type TYPE character varying(250);
                END IF;
            END $$;
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DO $$
            BEGIN
                IF to_regclass('data_extraction."PricingExtractionRecords"') IS NOT NULL THEN
                    ALTER TABLE data_extraction."PricingExtractionRecords"
                        ALTER COLUMN container_type TYPE character varying(50)
                        USING LEFT(container_type, 50);
                END IF;
            END $$;
            """
        );
    }
}
