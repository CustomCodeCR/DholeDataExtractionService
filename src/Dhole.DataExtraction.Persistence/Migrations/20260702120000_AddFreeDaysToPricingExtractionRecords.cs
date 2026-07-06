using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.DataExtraction.Persistence.Migrations;

public partial class AddFreeDaysToPricingExtractionRecords : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "free_days",
            table: "PricingExtractionRecords",
            schema: "data_extraction",
            type: "integer",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "free_days",
            table: "PricingExtractionRecords",
            schema: "data_extraction");
    }
}
