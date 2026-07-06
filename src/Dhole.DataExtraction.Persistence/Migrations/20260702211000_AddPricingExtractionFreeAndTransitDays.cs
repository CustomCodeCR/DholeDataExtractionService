using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.DataExtraction.Persistence.Migrations;

public partial class AddPricingExtractionFreeAndTransitDays : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE data_extraction.\"PricingExtractionRecords\"
            ADD COLUMN IF NOT EXISTS free_days integer;

            ALTER TABLE data_extraction.\"PricingExtractionRecords\"
            ADD COLUMN IF NOT EXISTS transit_days integer;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE data_extraction.\"PricingExtractionRecords\"
            DROP COLUMN IF EXISTS free_days;

            ALTER TABLE data_extraction.\"PricingExtractionRecords\"
            DROP COLUMN IF EXISTS transit_days;
            """);
    }
}
