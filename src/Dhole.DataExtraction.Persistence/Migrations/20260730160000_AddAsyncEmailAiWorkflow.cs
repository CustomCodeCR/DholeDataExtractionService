using Dhole.DataExtraction.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.DataExtraction.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260730160000_AddAsyncEmailAiWorkflow")]
public sealed class AddAsyncEmailAiWorkflow : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE data_extraction."EmailExtractionJobs"
            SET status = 'Extracting'
            WHERE status = 'Processing';
            """
        );

        migrationBuilder.AddColumn<Guid>(
            name: "ai_request_id",
            schema: "data_extraction",
            table: "EmailExtractionJobs",
            type: "uuid",
            nullable: true
        );
        migrationBuilder.AddColumn<Guid>(
            name: "ai_execution_id",
            schema: "data_extraction",
            table: "EmailExtractionJobs",
            type: "uuid",
            nullable: true
        );
        migrationBuilder.AddColumn<string>(
            name: "ai_request_hash",
            schema: "data_extraction",
            table: "EmailExtractionJobs",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true
        );
        migrationBuilder.AddColumn<Guid>(
            name: "pricing_request_id",
            schema: "data_extraction",
            table: "EmailExtractionJobs",
            type: "uuid",
            nullable: true
        );
        migrationBuilder.AddColumn<int>(
            name: "attempt_count",
            schema: "data_extraction",
            table: "EmailExtractionJobs",
            type: "integer",
            nullable: false,
            defaultValue: 0
        );
        migrationBuilder.AddColumn<DateTime>(
            name: "next_attempt_at_utc",
            schema: "data_extraction",
            table: "EmailExtractionJobs",
            type: "timestamp with time zone",
            nullable: true
        );
        migrationBuilder.AddColumn<string>(
            name: "lease_owner",
            schema: "data_extraction",
            table: "EmailExtractionJobs",
            type: "character varying(250)",
            maxLength: 250,
            nullable: true
        );
        migrationBuilder.AddColumn<DateTime>(
            name: "lease_expires_at_utc",
            schema: "data_extraction",
            table: "EmailExtractionJobs",
            type: "timestamp with time zone",
            nullable: true
        );
        migrationBuilder.AddColumn<DateTime>(
            name: "last_heartbeat_at_utc",
            schema: "data_extraction",
            table: "EmailExtractionJobs",
            type: "timestamp with time zone",
            nullable: true
        );
        migrationBuilder.AddColumn<string>(
            name: "last_error_code",
            schema: "data_extraction",
            table: "EmailExtractionJobs",
            type: "character varying(250)",
            maxLength: 250,
            nullable: true
        );
        migrationBuilder.AddColumn<int>(
            name: "version",
            schema: "data_extraction",
            table: "EmailExtractionJobs",
            type: "integer",
            nullable: false,
            defaultValue: 1
        );

        migrationBuilder.CreateTable(
            name: "EmailAiAnalysisRequests",
            schema: "data_extraction",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                email_extraction_job_id = table.Column<Guid>(
                    type: "uuid",
                    nullable: false
                ),
                email_message_id = table.Column<Guid>(
                    type: "uuid",
                    nullable: false
                ),
                email_attachment_id = table.Column<Guid>(
                    type: "uuid",
                    nullable: true
                ),
                extraction_execution_id = table.Column<Guid>(
                    type: "uuid",
                    nullable: true
                ),
                provisional_pricing_import_id = table.Column<Guid>(
                    type: "uuid",
                    nullable: false
                ),
                correlation_id = table.Column<string>(
                    type: "character varying(150)",
                    maxLength: 150,
                    nullable: false
                ),
                request_hash = table.Column<string>(
                    type: "character varying(128)",
                    maxLength: 128,
                    nullable: false
                ),
                payload_json = table.Column<string>(
                    type: "jsonb",
                    nullable: false
                ),
                image_storage_path = table.Column<string>(
                    type: "character varying(1000)",
                    maxLength: 1000,
                    nullable: true
                ),
                image_content_type = table.Column<string>(
                    type: "character varying(250)",
                    maxLength: 250,
                    nullable: true
                ),
                completed_at_utc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
                created_at_utc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
                created_by = table.Column<string>(
                    type: "text",
                    nullable: true
                ),
                updated_at_utc = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
                updated_by = table.Column<string>(
                    type: "text",
                    nullable: true
                ),
            },
            constraints: table =>
            {
                table.PrimaryKey("p_k_email_ai_analysis_requests", x => x.id);
            }
        );

        migrationBuilder.CreateIndex(
            name: "i_x_email_extraction_jobs_ai_request_id",
            schema: "data_extraction",
            table: "EmailExtractionJobs",
            column: "ai_request_id",
            unique: true
        );
        migrationBuilder.CreateIndex(
            name: "i_x_email_extraction_jobs_pricing_request_id",
            schema: "data_extraction",
            table: "EmailExtractionJobs",
            column: "pricing_request_id",
            unique: true
        );
        migrationBuilder.CreateIndex(
            name: "i_x_email_extraction_jobs_status_next_attempt_created",
            schema: "data_extraction",
            table: "EmailExtractionJobs",
            columns:
            [
                "status",
                "next_attempt_at_utc",
                "created_at_utc",
            ]
        );
        migrationBuilder.CreateIndex(
            name: "i_x_email_extraction_jobs_status_lease",
            schema: "data_extraction",
            table: "EmailExtractionJobs",
            columns: ["status", "lease_expires_at_utc"]
        );
        migrationBuilder.CreateIndex(
            name: "i_x_email_ai_analysis_requests_email_extraction_job_id",
            schema: "data_extraction",
            table: "EmailAiAnalysisRequests",
            column: "email_extraction_job_id"
        );
        migrationBuilder.CreateIndex(
            name: "i_x_email_ai_analysis_requests_email_message_id",
            schema: "data_extraction",
            table: "EmailAiAnalysisRequests",
            column: "email_message_id"
        );
        migrationBuilder.CreateIndex(
            name: "i_x_email_ai_analysis_requests_email_attachment_id",
            schema: "data_extraction",
            table: "EmailAiAnalysisRequests",
            column: "email_attachment_id"
        );
        migrationBuilder.CreateIndex(
            name: "i_x_email_ai_analysis_requests_extraction_execution_id",
            schema: "data_extraction",
            table: "EmailAiAnalysisRequests",
            column: "extraction_execution_id"
        );
        migrationBuilder.CreateIndex(
            name: "i_x_email_ai_analysis_requests_request_hash",
            schema: "data_extraction",
            table: "EmailAiAnalysisRequests",
            column: "request_hash"
        );
        migrationBuilder.CreateIndex(
            name: "i_x_email_ai_analysis_requests_completed_at_utc",
            schema: "data_extraction",
            table: "EmailAiAnalysisRequests",
            column: "completed_at_utc"
        );
        migrationBuilder.CreateIndex(
            name: "ix_email_ai_requests_job_hash",
            schema: "data_extraction",
            table: "EmailAiAnalysisRequests",
            columns: ["email_extraction_job_id", "request_hash"]
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "EmailAiAnalysisRequests",
            schema: "data_extraction"
        );
        migrationBuilder.DropIndex(
            name: "i_x_email_extraction_jobs_ai_request_id",
            schema: "data_extraction",
            table: "EmailExtractionJobs"
        );
        migrationBuilder.DropIndex(
            name: "i_x_email_extraction_jobs_pricing_request_id",
            schema: "data_extraction",
            table: "EmailExtractionJobs"
        );
        migrationBuilder.DropIndex(
            name: "i_x_email_extraction_jobs_status_next_attempt_created",
            schema: "data_extraction",
            table: "EmailExtractionJobs"
        );
        migrationBuilder.DropIndex(
            name: "i_x_email_extraction_jobs_status_lease",
            schema: "data_extraction",
            table: "EmailExtractionJobs"
        );

        foreach (
            var column in new[]
            {
                "ai_request_id",
                "ai_execution_id",
                "ai_request_hash",
                "pricing_request_id",
                "attempt_count",
                "next_attempt_at_utc",
                "lease_owner",
                "lease_expires_at_utc",
                "last_heartbeat_at_utc",
                "last_error_code",
                "version",
            }
        )
        {
            migrationBuilder.DropColumn(
                name: column,
                schema: "data_extraction",
                table: "EmailExtractionJobs"
            );
        }

        migrationBuilder.Sql(
            """
            UPDATE data_extraction."EmailExtractionJobs"
            SET status = 'Processing'
            WHERE status = 'Extracting';

            UPDATE data_extraction."EmailExtractionJobs"
            SET status = 'NeedsReview',
                error_message = COALESCE(
                    error_message,
                    'El flujo asíncrono fue revertido durante el procesamiento.'
                )
            WHERE status IN (
                'AwaitingAi',
                'AiProcessing',
                'ValidatingAiResult',
                'AwaitingPricing'
            );
            """
        );
    }
}
