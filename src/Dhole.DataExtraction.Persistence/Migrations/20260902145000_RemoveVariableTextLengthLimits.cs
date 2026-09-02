using Dhole.DataExtraction.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.DataExtraction.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260902145000_RemoveVariableTextLengthLimits")]
public sealed class RemoveVariableTextLengthLimits : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Imported email/document content is not bounded by our application. Keeping those
        // values in varchar(N) makes a valid tariff fail completely when a provider sends a
        // longer description, note, MIME value, sheet/header value or diagnostic message.
        // PostgreSQL text has no application-sized cap and is the correct persistence type
        // for these variable/external values.
        migrationBuilder.Sql(
            """
            DO $$
            BEGIN
                IF to_regclass('data_extraction."PricingExtractionRecords"') IS NOT NULL THEN
                    ALTER TABLE data_extraction."PricingExtractionRecords"
                        ALTER COLUMN source_sheet_name TYPE text,
                        ALTER COLUMN origin_port TYPE text,
                        ALTER COLUMN port_of_exit TYPE text,
                        ALTER COLUMN destination_port TYPE text,
                        ALTER COLUMN container_type TYPE text,
                        ALTER COLUMN carrier TYPE text,
                        ALTER COLUMN agent TYPE text,
                        ALTER COLUMN commodity TYPE text,
                        ALTER COLUMN space_comment TYPE text,
                        ALTER COLUMN remarks TYPE text,
                        ALTER COLUMN origin_port_catalog_name TYPE text,
                        ALTER COLUMN origin_port_raw_value TYPE text,
                        ALTER COLUMN port_of_exit_catalog_name TYPE text,
                        ALTER COLUMN port_of_exit_raw_value TYPE text,
                        ALTER COLUMN destination_port_catalog_name TYPE text,
                        ALTER COLUMN destination_port_raw_value TYPE text,
                        ALTER COLUMN container_type_catalog_name TYPE text,
                        ALTER COLUMN container_type_raw_value TYPE text,
                        ALTER COLUMN carrier_catalog_name TYPE text,
                        ALTER COLUMN carrier_raw_value TYPE text,
                        ALTER COLUMN agent_catalog_name TYPE text,
                        ALTER COLUMN agent_raw_value TYPE text,
                        ALTER COLUMN currency_catalog_name TYPE text,
                        ALTER COLUMN currency_raw_value TYPE text;
                END IF;

                IF to_regclass('data_extraction."ExtractionIssues"') IS NOT NULL THEN
                    ALTER TABLE data_extraction."ExtractionIssues"
                        ALTER COLUMN message TYPE text,
                        ALTER COLUMN source_sheet_name TYPE text,
                        ALTER COLUMN column_name TYPE text,
                        ALTER COLUMN raw_value TYPE text;
                END IF;

                IF to_regclass('data_extraction."ExtractionExecutions"') IS NOT NULL THEN
                    ALTER TABLE data_extraction."ExtractionExecutions"
                        ALTER COLUMN original_file_name TYPE text,
                        ALTER COLUMN content_type TYPE text,
                        ALTER COLUMN error_message TYPE text,
                        ALTER COLUMN requested_by_name TYPE text;
                END IF;

                IF to_regclass('data_extraction."SourceDocuments"') IS NOT NULL THEN
                    ALTER TABLE data_extraction."SourceDocuments"
                        ALTER COLUMN original_file_name TYPE text,
                        ALTER COLUMN content_type TYPE text,
                        ALTER COLUMN storage_path TYPE text;
                END IF;

                IF to_regclass('data_extraction."EmailMessages"') IS NOT NULL THEN
                    ALTER TABLE data_extraction."EmailMessages"
                        ALTER COLUMN message_id_header TYPE text,
                        ALTER COLUMN from_name TYPE text,
                        ALTER COLUMN to_addresses TYPE text,
                        ALTER COLUMN cc_addresses TYPE text,
                        ALTER COLUMN subject TYPE text,
                        ALTER COLUMN raw_email_storage_path TYPE text,
                        ALTER COLUMN error_message TYPE text,
                        ALTER COLUMN classification_reason TYPE text;
                END IF;

                IF to_regclass('data_extraction."EmailAttachments"') IS NOT NULL THEN
                    ALTER TABLE data_extraction."EmailAttachments"
                        ALTER COLUMN file_name TYPE text,
                        ALTER COLUMN content_type TYPE text,
                        ALTER COLUMN storage_path TYPE text,
                        ALTER COLUMN error_message TYPE text;
                END IF;

                IF to_regclass('data_extraction."EmailAiAnalysisRequests"') IS NOT NULL THEN
                    ALTER TABLE data_extraction."EmailAiAnalysisRequests"
                        ALTER COLUMN image_storage_path TYPE text,
                        ALTER COLUMN image_content_type TYPE text;
                END IF;

                IF to_regclass('data_extraction."EmailExtractionJobs"') IS NOT NULL THEN
                    ALTER TABLE data_extraction."EmailExtractionJobs"
                        ALTER COLUMN error_message TYPE text,
                        ALTER COLUMN last_error_code TYPE text;
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
                        ALTER COLUMN source_sheet_name TYPE character varying(250) USING LEFT(source_sheet_name, 250),
                        ALTER COLUMN origin_port TYPE character varying(250) USING LEFT(origin_port, 250),
                        ALTER COLUMN port_of_exit TYPE character varying(250) USING LEFT(port_of_exit, 250),
                        ALTER COLUMN destination_port TYPE character varying(250) USING LEFT(destination_port, 250),
                        ALTER COLUMN container_type TYPE character varying(250) USING LEFT(container_type, 250),
                        ALTER COLUMN carrier TYPE character varying(250) USING LEFT(carrier, 250),
                        ALTER COLUMN agent TYPE character varying(250) USING LEFT(agent, 250),
                        ALTER COLUMN commodity TYPE character varying(250) USING LEFT(commodity, 250),
                        ALTER COLUMN space_comment TYPE character varying(2000) USING LEFT(space_comment, 2000),
                        ALTER COLUMN remarks TYPE character varying(2000) USING LEFT(remarks, 2000),
                        ALTER COLUMN origin_port_catalog_name TYPE character varying(250) USING LEFT(origin_port_catalog_name, 250),
                        ALTER COLUMN origin_port_raw_value TYPE character varying(500) USING LEFT(origin_port_raw_value, 500),
                        ALTER COLUMN port_of_exit_catalog_name TYPE character varying(250) USING LEFT(port_of_exit_catalog_name, 250),
                        ALTER COLUMN port_of_exit_raw_value TYPE character varying(500) USING LEFT(port_of_exit_raw_value, 500),
                        ALTER COLUMN destination_port_catalog_name TYPE character varying(250) USING LEFT(destination_port_catalog_name, 250),
                        ALTER COLUMN destination_port_raw_value TYPE character varying(500) USING LEFT(destination_port_raw_value, 500),
                        ALTER COLUMN container_type_catalog_name TYPE character varying(250) USING LEFT(container_type_catalog_name, 250),
                        ALTER COLUMN container_type_raw_value TYPE character varying(500) USING LEFT(container_type_raw_value, 500),
                        ALTER COLUMN carrier_catalog_name TYPE character varying(250) USING LEFT(carrier_catalog_name, 250),
                        ALTER COLUMN carrier_raw_value TYPE character varying(500) USING LEFT(carrier_raw_value, 500),
                        ALTER COLUMN agent_catalog_name TYPE character varying(250) USING LEFT(agent_catalog_name, 250),
                        ALTER COLUMN agent_raw_value TYPE character varying(500) USING LEFT(agent_raw_value, 500),
                        ALTER COLUMN currency_catalog_name TYPE character varying(250) USING LEFT(currency_catalog_name, 250),
                        ALTER COLUMN currency_raw_value TYPE character varying(500) USING LEFT(currency_raw_value, 500);
                END IF;

                IF to_regclass('data_extraction."ExtractionIssues"') IS NOT NULL THEN
                    ALTER TABLE data_extraction."ExtractionIssues"
                        ALTER COLUMN message TYPE character varying(2000) USING LEFT(message, 2000),
                        ALTER COLUMN source_sheet_name TYPE character varying(250) USING LEFT(source_sheet_name, 250),
                        ALTER COLUMN column_name TYPE character varying(250) USING LEFT(column_name, 250),
                        ALTER COLUMN raw_value TYPE character varying(2000) USING LEFT(raw_value, 2000);
                END IF;

                IF to_regclass('data_extraction."ExtractionExecutions"') IS NOT NULL THEN
                    ALTER TABLE data_extraction."ExtractionExecutions"
                        ALTER COLUMN original_file_name TYPE character varying(500) USING LEFT(original_file_name, 500),
                        ALTER COLUMN content_type TYPE character varying(250) USING LEFT(content_type, 250),
                        ALTER COLUMN error_message TYPE character varying(4000) USING LEFT(error_message, 4000),
                        ALTER COLUMN requested_by_name TYPE character varying(250) USING LEFT(requested_by_name, 250);
                END IF;

                IF to_regclass('data_extraction."SourceDocuments"') IS NOT NULL THEN
                    ALTER TABLE data_extraction."SourceDocuments"
                        ALTER COLUMN original_file_name TYPE character varying(500) USING LEFT(original_file_name, 500),
                        ALTER COLUMN content_type TYPE character varying(250) USING LEFT(content_type, 250),
                        ALTER COLUMN storage_path TYPE character varying(1000) USING LEFT(storage_path, 1000);
                END IF;

                IF to_regclass('data_extraction."EmailMessages"') IS NOT NULL THEN
                    ALTER TABLE data_extraction."EmailMessages"
                        ALTER COLUMN message_id_header TYPE character varying(500) USING LEFT(message_id_header, 500),
                        ALTER COLUMN from_name TYPE character varying(250) USING LEFT(from_name, 250),
                        ALTER COLUMN to_addresses TYPE character varying(2000) USING LEFT(to_addresses, 2000),
                        ALTER COLUMN cc_addresses TYPE character varying(2000) USING LEFT(cc_addresses, 2000),
                        ALTER COLUMN subject TYPE character varying(1000) USING LEFT(subject, 1000),
                        ALTER COLUMN raw_email_storage_path TYPE character varying(1000) USING LEFT(raw_email_storage_path, 1000),
                        ALTER COLUMN error_message TYPE character varying(4000) USING LEFT(error_message, 4000),
                        ALTER COLUMN classification_reason TYPE character varying(1000) USING LEFT(classification_reason, 1000);
                END IF;

                IF to_regclass('data_extraction."EmailAttachments"') IS NOT NULL THEN
                    ALTER TABLE data_extraction."EmailAttachments"
                        ALTER COLUMN file_name TYPE character varying(500) USING LEFT(file_name, 500),
                        ALTER COLUMN content_type TYPE character varying(250) USING LEFT(content_type, 250),
                        ALTER COLUMN storage_path TYPE character varying(1000) USING LEFT(storage_path, 1000),
                        ALTER COLUMN error_message TYPE character varying(4000) USING LEFT(error_message, 4000);
                END IF;

                IF to_regclass('data_extraction."EmailAiAnalysisRequests"') IS NOT NULL THEN
                    ALTER TABLE data_extraction."EmailAiAnalysisRequests"
                        ALTER COLUMN image_storage_path TYPE character varying(1000) USING LEFT(image_storage_path, 1000),
                        ALTER COLUMN image_content_type TYPE character varying(250) USING LEFT(image_content_type, 250);
                END IF;

                IF to_regclass('data_extraction."EmailExtractionJobs"') IS NOT NULL THEN
                    ALTER TABLE data_extraction."EmailExtractionJobs"
                        ALTER COLUMN error_message TYPE character varying(4000) USING LEFT(error_message, 4000),
                        ALTER COLUMN last_error_code TYPE character varying(250) USING LEFT(last_error_code, 250);
                END IF;
            END $$;
            """
        );
    }
}
