using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.DataExtraction.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Fix130726 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "agent_catalog_code",
                schema: "data_extraction",
                table: "PricingExtractionRecords",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "agent_catalog_group_slug",
                schema: "data_extraction",
                table: "PricingExtractionRecords",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "agent_catalog_item_id",
                schema: "data_extraction",
                table: "PricingExtractionRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "agent_catalog_name",
                schema: "data_extraction",
                table: "PricingExtractionRecords",
                type: "character varying(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "agent_catalog_slug",
                schema: "data_extraction",
                table: "PricingExtractionRecords",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "agent_raw_value",
                schema: "data_extraction",
                table: "PricingExtractionRecords",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "carrier_catalog_code",
                schema: "data_extraction",
                table: "PricingExtractionRecords",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "carrier_catalog_group_slug",
                schema: "data_extraction",
                table: "PricingExtractionRecords",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "carrier_catalog_item_id",
                schema: "data_extraction",
                table: "PricingExtractionRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "carrier_catalog_name",
                schema: "data_extraction",
                table: "PricingExtractionRecords",
                type: "character varying(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "carrier_catalog_slug",
                schema: "data_extraction",
                table: "PricingExtractionRecords",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "carrier_raw_value",
                schema: "data_extraction",
                table: "PricingExtractionRecords",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "container_type_catalog_code",
                schema: "data_extraction",
                table: "PricingExtractionRecords",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "container_type_catalog_group_slug",
                schema: "data_extraction",
                table: "PricingExtractionRecords",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "container_type_catalog_item_id",
                schema: "data_extraction",
                table: "PricingExtractionRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "container_type_catalog_name",
                schema: "data_extraction",
                table: "PricingExtractionRecords",
                type: "character varying(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "container_type_catalog_slug",
                schema: "data_extraction",
                table: "PricingExtractionRecords",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "container_type_raw_value",
                schema: "data_extraction",
                table: "PricingExtractionRecords",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "currency_catalog_code",
                schema: "data_extraction",
                table: "PricingExtractionRecords",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "currency_catalog_group_slug",
                schema: "data_extraction",
                table: "PricingExtractionRecords",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "currency_catalog_item_id",
                schema: "data_extraction",
                table: "PricingExtractionRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "currency_catalog_name",
                schema: "data_extraction",
                table: "PricingExtractionRecords",
                type: "character varying(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "currency_catalog_slug",
                schema: "data_extraction",
                table: "PricingExtractionRecords",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "currency_raw_value",
                schema: "data_extraction",
                table: "PricingExtractionRecords",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "destination_port_catalog_code",
                schema: "data_extraction",
                table: "PricingExtractionRecords",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "destination_port_catalog_group_slug",
                schema: "data_extraction",
                table: "PricingExtractionRecords",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "destination_port_catalog_item_id",
                schema: "data_extraction",
                table: "PricingExtractionRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "destination_port_catalog_name",
                schema: "data_extraction",
                table: "PricingExtractionRecords",
                type: "character varying(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "destination_port_catalog_slug",
                schema: "data_extraction",
                table: "PricingExtractionRecords",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "destination_port_raw_value",
                schema: "data_extraction",
                table: "PricingExtractionRecords",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "port_of_exit_catalog_code",
                schema: "data_extraction",
                table: "PricingExtractionRecords",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "port_of_exit_catalog_group_slug",
                schema: "data_extraction",
                table: "PricingExtractionRecords",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "port_of_exit_catalog_item_id",
                schema: "data_extraction",
                table: "PricingExtractionRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "port_of_exit_catalog_name",
                schema: "data_extraction",
                table: "PricingExtractionRecords",
                type: "character varying(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "port_of_exit_catalog_slug",
                schema: "data_extraction",
                table: "PricingExtractionRecords",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "port_of_exit_raw_value",
                schema: "data_extraction",
                table: "PricingExtractionRecords",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "catalog_item_reference",
                schema: "data_extraction",
                columns: table => new
                {
                    PricingExtractionRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    origin_port_catalog_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origin_port_catalog_group_slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    origin_port_catalog_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    origin_port_catalog_slug = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    origin_port_catalog_name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    origin_port_raw_value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_catalog_item_reference", x => x.PricingExtractionRecordId);
                    table.ForeignKey(
                        name: "FK_catalog_item_reference_PricingExtractionRecords_PricingExtr~",
                        column: x => x.PricingExtractionRecordId,
                        principalSchema: "data_extraction",
                        principalTable: "PricingExtractionRecords",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PricingExtractionRecords_agent_catalog_item_id",
                schema: "data_extraction",
                table: "PricingExtractionRecords",
                column: "agent_catalog_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_PricingExtractionRecords_carrier_catalog_item_id",
                schema: "data_extraction",
                table: "PricingExtractionRecords",
                column: "carrier_catalog_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_PricingExtractionRecords_container_type_catalog_item_id",
                schema: "data_extraction",
                table: "PricingExtractionRecords",
                column: "container_type_catalog_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_PricingExtractionRecords_currency_catalog_item_id",
                schema: "data_extraction",
                table: "PricingExtractionRecords",
                column: "currency_catalog_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_PricingExtractionRecords_destination_port_catalog_item_id",
                schema: "data_extraction",
                table: "PricingExtractionRecords",
                column: "destination_port_catalog_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_PricingExtractionRecords_port_of_exit_catalog_item_id",
                schema: "data_extraction",
                table: "PricingExtractionRecords",
                column: "port_of_exit_catalog_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_catalog_item_reference_origin_port_catalog_item_id",
                schema: "data_extraction",
                table: "catalog_item_reference",
                column: "origin_port_catalog_item_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "catalog_item_reference",
                schema: "data_extraction");

            migrationBuilder.DropIndex(
                name: "IX_PricingExtractionRecords_agent_catalog_item_id",
                schema: "data_extraction",
                table: "PricingExtractionRecords");

            migrationBuilder.DropIndex(
                name: "IX_PricingExtractionRecords_carrier_catalog_item_id",
                schema: "data_extraction",
                table: "PricingExtractionRecords");

            migrationBuilder.DropIndex(
                name: "IX_PricingExtractionRecords_container_type_catalog_item_id",
                schema: "data_extraction",
                table: "PricingExtractionRecords");

            migrationBuilder.DropIndex(
                name: "IX_PricingExtractionRecords_currency_catalog_item_id",
                schema: "data_extraction",
                table: "PricingExtractionRecords");

            migrationBuilder.DropIndex(
                name: "IX_PricingExtractionRecords_destination_port_catalog_item_id",
                schema: "data_extraction",
                table: "PricingExtractionRecords");

            migrationBuilder.DropIndex(
                name: "IX_PricingExtractionRecords_port_of_exit_catalog_item_id",
                schema: "data_extraction",
                table: "PricingExtractionRecords");

            migrationBuilder.DropColumn(
                name: "agent_catalog_code",
                schema: "data_extraction",
                table: "PricingExtractionRecords");

            migrationBuilder.DropColumn(
                name: "agent_catalog_group_slug",
                schema: "data_extraction",
                table: "PricingExtractionRecords");

            migrationBuilder.DropColumn(
                name: "agent_catalog_item_id",
                schema: "data_extraction",
                table: "PricingExtractionRecords");

            migrationBuilder.DropColumn(
                name: "agent_catalog_name",
                schema: "data_extraction",
                table: "PricingExtractionRecords");

            migrationBuilder.DropColumn(
                name: "agent_catalog_slug",
                schema: "data_extraction",
                table: "PricingExtractionRecords");

            migrationBuilder.DropColumn(
                name: "agent_raw_value",
                schema: "data_extraction",
                table: "PricingExtractionRecords");

            migrationBuilder.DropColumn(
                name: "carrier_catalog_code",
                schema: "data_extraction",
                table: "PricingExtractionRecords");

            migrationBuilder.DropColumn(
                name: "carrier_catalog_group_slug",
                schema: "data_extraction",
                table: "PricingExtractionRecords");

            migrationBuilder.DropColumn(
                name: "carrier_catalog_item_id",
                schema: "data_extraction",
                table: "PricingExtractionRecords");

            migrationBuilder.DropColumn(
                name: "carrier_catalog_name",
                schema: "data_extraction",
                table: "PricingExtractionRecords");

            migrationBuilder.DropColumn(
                name: "carrier_catalog_slug",
                schema: "data_extraction",
                table: "PricingExtractionRecords");

            migrationBuilder.DropColumn(
                name: "carrier_raw_value",
                schema: "data_extraction",
                table: "PricingExtractionRecords");

            migrationBuilder.DropColumn(
                name: "container_type_catalog_code",
                schema: "data_extraction",
                table: "PricingExtractionRecords");

            migrationBuilder.DropColumn(
                name: "container_type_catalog_group_slug",
                schema: "data_extraction",
                table: "PricingExtractionRecords");

            migrationBuilder.DropColumn(
                name: "container_type_catalog_item_id",
                schema: "data_extraction",
                table: "PricingExtractionRecords");

            migrationBuilder.DropColumn(
                name: "container_type_catalog_name",
                schema: "data_extraction",
                table: "PricingExtractionRecords");

            migrationBuilder.DropColumn(
                name: "container_type_catalog_slug",
                schema: "data_extraction",
                table: "PricingExtractionRecords");

            migrationBuilder.DropColumn(
                name: "container_type_raw_value",
                schema: "data_extraction",
                table: "PricingExtractionRecords");

            migrationBuilder.DropColumn(
                name: "currency_catalog_code",
                schema: "data_extraction",
                table: "PricingExtractionRecords");

            migrationBuilder.DropColumn(
                name: "currency_catalog_group_slug",
                schema: "data_extraction",
                table: "PricingExtractionRecords");

            migrationBuilder.DropColumn(
                name: "currency_catalog_item_id",
                schema: "data_extraction",
                table: "PricingExtractionRecords");

            migrationBuilder.DropColumn(
                name: "currency_catalog_name",
                schema: "data_extraction",
                table: "PricingExtractionRecords");

            migrationBuilder.DropColumn(
                name: "currency_catalog_slug",
                schema: "data_extraction",
                table: "PricingExtractionRecords");

            migrationBuilder.DropColumn(
                name: "currency_raw_value",
                schema: "data_extraction",
                table: "PricingExtractionRecords");

            migrationBuilder.DropColumn(
                name: "destination_port_catalog_code",
                schema: "data_extraction",
                table: "PricingExtractionRecords");

            migrationBuilder.DropColumn(
                name: "destination_port_catalog_group_slug",
                schema: "data_extraction",
                table: "PricingExtractionRecords");

            migrationBuilder.DropColumn(
                name: "destination_port_catalog_item_id",
                schema: "data_extraction",
                table: "PricingExtractionRecords");

            migrationBuilder.DropColumn(
                name: "destination_port_catalog_name",
                schema: "data_extraction",
                table: "PricingExtractionRecords");

            migrationBuilder.DropColumn(
                name: "destination_port_catalog_slug",
                schema: "data_extraction",
                table: "PricingExtractionRecords");

            migrationBuilder.DropColumn(
                name: "destination_port_raw_value",
                schema: "data_extraction",
                table: "PricingExtractionRecords");

            migrationBuilder.DropColumn(
                name: "port_of_exit_catalog_code",
                schema: "data_extraction",
                table: "PricingExtractionRecords");

            migrationBuilder.DropColumn(
                name: "port_of_exit_catalog_group_slug",
                schema: "data_extraction",
                table: "PricingExtractionRecords");

            migrationBuilder.DropColumn(
                name: "port_of_exit_catalog_item_id",
                schema: "data_extraction",
                table: "PricingExtractionRecords");

            migrationBuilder.DropColumn(
                name: "port_of_exit_catalog_name",
                schema: "data_extraction",
                table: "PricingExtractionRecords");

            migrationBuilder.DropColumn(
                name: "port_of_exit_catalog_slug",
                schema: "data_extraction",
                table: "PricingExtractionRecords");

            migrationBuilder.DropColumn(
                name: "port_of_exit_raw_value",
                schema: "data_extraction",
                table: "PricingExtractionRecords");
        }
    }
}
