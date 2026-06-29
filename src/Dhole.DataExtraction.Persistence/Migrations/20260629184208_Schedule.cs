using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.DataExtraction.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Schedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "data_extraction");

            migrationBuilder.CreateTable(
                name: "ColumnMappingProfiles",
                schema: "data_extraction",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    is_system = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_column_mapping_profiles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ExtractionExecutions",
                schema: "data_extraction",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pricing_import_id = table.Column<Guid>(type: "uuid", nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    original_file_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    content_type = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    file_extension = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    file_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    file_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    source_file_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    profile_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    total_rows = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    valid_rows = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    warning_rows = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    invalid_rows = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    failed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    error_message = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: true),
                    requested_by_name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_extraction_executions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                schema: "data_extraction",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    event_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    source_service = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    consumer_service = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    processed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_inbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "data_extraction",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    event_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    source_service = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    headers_json = table.Column<string>(type: "jsonb", nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    retry_count = table.Column<int>(type: "integer", nullable: false),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ColumnMappingRules",
                schema: "data_extraction",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    column_mapping_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_column_name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    normalized_source_column_name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    target_field = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    priority = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    default_value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    transform_expression = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    metadata_json = table.Column<string>(type: "jsonb", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_column_mapping_rules", x => x.id);
                    table.ForeignKey(
                        name: "f_k_column_mapping_rules_column_mapping_profiles_column_mapping~",
                        column: x => x.column_mapping_profile_id,
                        principalSchema: "data_extraction",
                        principalTable: "ColumnMappingProfiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SourceDocuments",
                schema: "data_extraction",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    extraction_execution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_file_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    content_type = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    file_extension = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    file_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    file_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    source_file_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    storage_path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_source_documents", x => x.id);
                    table.ForeignKey(
                        name: "FK_SourceDocuments_ExtractionExecutions_extraction_execution_id",
                        column: x => x.extraction_execution_id,
                        principalSchema: "data_extraction",
                        principalTable: "ExtractionExecutions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PricingExtractionRecords",
                schema: "data_extraction",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    extraction_execution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_sheet_name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    source_row_number = table.Column<int>(type: "integer", nullable: true),
                    origin_port = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    port_of_exit = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    destination_port = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    container_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    carrier = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    agent = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    commodity = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    valid_from = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    valid_to = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ocean_freight = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    origin_charges = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    destination_charges = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    surcharges = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    total_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    total_sale = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    profit = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    margin = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    space_comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    remarks = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    raw_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_pricing_extraction_records", x => x.id);
                    table.ForeignKey(
                        name: "FK_PricingExtractionRecords_ExtractionExecutions_extraction_ex~",
                        column: x => x.extraction_execution_id,
                        principalSchema: "data_extraction",
                        principalTable: "ExtractionExecutions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PricingExtractionRecords_SourceDocuments_source_document_id",
                        column: x => x.source_document_id,
                        principalSchema: "data_extraction",
                        principalTable: "SourceDocuments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExtractionIssues",
                schema: "data_extraction",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    extraction_execution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pricing_extraction_record_id = table.Column<Guid>(type: "uuid", nullable: true),
                    code = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    is_blocking = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    source_sheet_name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    source_row_number = table.Column<int>(type: "integer", nullable: true),
                    column_name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    raw_value = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_extraction_issues", x => x.id);
                    table.ForeignKey(
                        name: "FK_ExtractionIssues_ExtractionExecutions_extraction_execution_~",
                        column: x => x.extraction_execution_id,
                        principalSchema: "data_extraction",
                        principalTable: "ExtractionExecutions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExtractionIssues_PricingExtractionRecords_pricing_extractio~",
                        column: x => x.pricing_extraction_record_id,
                        principalSchema: "data_extraction",
                        principalTable: "PricingExtractionRecords",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ColumnMappingProfiles_code",
                schema: "data_extraction",
                table: "ColumnMappingProfiles",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_column_mapping_rules_column_mapping_profile_id",
                schema: "data_extraction",
                table: "ColumnMappingRules",
                column: "column_mapping_profile_id");

            migrationBuilder.CreateIndex(
                name: "IX_ColumnMappingRules_column_mapping_profile_id_normalized_sou~",
                schema: "data_extraction",
                table: "ColumnMappingRules",
                columns: new[] { "column_mapping_profile_id", "normalized_source_column_name" });

            migrationBuilder.CreateIndex(
                name: "IX_ColumnMappingRules_column_mapping_profile_id_target_field",
                schema: "data_extraction",
                table: "ColumnMappingRules",
                columns: new[] { "column_mapping_profile_id", "target_field" });

            migrationBuilder.CreateIndex(
                name: "IX_ExtractionExecutions_correlation_id",
                schema: "data_extraction",
                table: "ExtractionExecutions",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "IX_ExtractionExecutions_file_hash",
                schema: "data_extraction",
                table: "ExtractionExecutions",
                column: "file_hash");

            migrationBuilder.CreateIndex(
                name: "IX_ExtractionExecutions_pricing_import_id",
                schema: "data_extraction",
                table: "ExtractionExecutions",
                column: "pricing_import_id");

            migrationBuilder.CreateIndex(
                name: "IX_ExtractionExecutions_status",
                schema: "data_extraction",
                table: "ExtractionExecutions",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_ExtractionIssues_code",
                schema: "data_extraction",
                table: "ExtractionIssues",
                column: "code");

            migrationBuilder.CreateIndex(
                name: "IX_ExtractionIssues_extraction_execution_id",
                schema: "data_extraction",
                table: "ExtractionIssues",
                column: "extraction_execution_id");

            migrationBuilder.CreateIndex(
                name: "IX_ExtractionIssues_is_blocking",
                schema: "data_extraction",
                table: "ExtractionIssues",
                column: "is_blocking");

            migrationBuilder.CreateIndex(
                name: "IX_ExtractionIssues_pricing_extraction_record_id",
                schema: "data_extraction",
                table: "ExtractionIssues",
                column: "pricing_extraction_record_id");

            migrationBuilder.CreateIndex(
                name: "IX_inbox_messages_event_id_consumer_service",
                schema: "data_extraction",
                table: "inbox_messages",
                columns: new[] { "event_id", "consumer_service" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inbox_messages_status_created_at",
                schema: "data_extraction",
                table: "inbox_messages",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_event_id",
                schema: "data_extraction",
                table: "outbox_messages",
                column: "event_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_status_created_at",
                schema: "data_extraction",
                table: "outbox_messages",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_PricingExtractionRecords_extraction_execution_id",
                schema: "data_extraction",
                table: "PricingExtractionRecords",
                column: "extraction_execution_id");

            migrationBuilder.CreateIndex(
                name: "IX_PricingExtractionRecords_origin_port_destination_port_conta~",
                schema: "data_extraction",
                table: "PricingExtractionRecords",
                columns: new[] { "origin_port", "destination_port", "container_type" });

            migrationBuilder.CreateIndex(
                name: "IX_PricingExtractionRecords_source_document_id",
                schema: "data_extraction",
                table: "PricingExtractionRecords",
                column: "source_document_id");

            migrationBuilder.CreateIndex(
                name: "IX_PricingExtractionRecords_status",
                schema: "data_extraction",
                table: "PricingExtractionRecords",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_SourceDocuments_extraction_execution_id",
                schema: "data_extraction",
                table: "SourceDocuments",
                column: "extraction_execution_id");

            migrationBuilder.CreateIndex(
                name: "IX_SourceDocuments_file_hash",
                schema: "data_extraction",
                table: "SourceDocuments",
                column: "file_hash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ColumnMappingRules",
                schema: "data_extraction");

            migrationBuilder.DropTable(
                name: "ExtractionIssues",
                schema: "data_extraction");

            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "data_extraction");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "data_extraction");

            migrationBuilder.DropTable(
                name: "ColumnMappingProfiles",
                schema: "data_extraction");

            migrationBuilder.DropTable(
                name: "PricingExtractionRecords",
                schema: "data_extraction");

            migrationBuilder.DropTable(
                name: "SourceDocuments",
                schema: "data_extraction");

            migrationBuilder.DropTable(
                name: "ExtractionExecutions",
                schema: "data_extraction");
        }
    }
}
