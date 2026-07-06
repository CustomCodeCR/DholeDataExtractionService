using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.DataExtraction.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Fix2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "free_days",
                schema: "data_extraction",
                table: "PricingExtractionRecords",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "transit_days",
                schema: "data_extraction",
                table: "PricingExtractionRecords",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "free_days",
                schema: "data_extraction",
                table: "PricingExtractionRecords");

            migrationBuilder.DropColumn(
                name: "transit_days",
                schema: "data_extraction",
                table: "PricingExtractionRecords");
        }
    }
}
