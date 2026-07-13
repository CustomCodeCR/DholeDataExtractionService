using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.DataExtraction.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailIngestion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE SCHEMA IF NOT EXISTS data_extraction;

                ALTER TABLE data_extraction."ExtractionExecutions"
                    ADD COLUMN IF NOT EXISTS source_origin_type character varying(100);

                ALTER TABLE data_extraction."ExtractionExecutions"
                    ADD COLUMN IF NOT EXISTS source_origin_id uuid;

                ALTER TABLE data_extraction."ExtractionExecutions"
                    ADD COLUMN IF NOT EXISTS source_email_message_id uuid;

                ALTER TABLE data_extraction."ExtractionExecutions"
                    ADD COLUMN IF NOT EXISTS source_email_attachment_id uuid;

                CREATE TABLE IF NOT EXISTS data_extraction."EmailIngestionAccounts" (
                    id uuid NOT NULL,
                    name character varying(200) NOT NULL,
                    email_address character varying(320) NOT NULL,
                    provider_type character varying(50) NOT NULL,
                    host character varying(250) NOT NULL,
                    port integer NOT NULL,
                    use_ssl boolean NOT NULL,
                    username character varying(320) NOT NULL,
                    secret_reference character varying(500) NOT NULL,
                    folder_name character varying(200) NOT NULL,
                    polling_interval_minutes integer NOT NULL DEFAULT 5,
                    auto_process boolean NOT NULL DEFAULT true,
                    auto_send_to_pricing boolean NOT NULL DEFAULT false,
                    auto_send_min_confidence numeric(5,2) NOT NULL DEFAULT 90,
                    process_body_when_no_supported_attachments boolean NOT NULL DEFAULT true,
                    process_body_even_with_attachments boolean NOT NULL DEFAULT false,
                    allowed_senders character varying(4000),
                    is_active boolean NOT NULL DEFAULT true,
                    last_processed_uid bigint,
                    last_sync_at timestamp with time zone,
                    last_sync_error character varying(4000),
                    created_at_utc timestamp with time zone NOT NULL,
                    created_by text,
                    updated_at_utc timestamp with time zone,
                    updated_by text,
                    is_deleted boolean NOT NULL,
                    deleted_at_utc timestamp with time zone,
                    deleted_by text,
                    CONSTRAINT p_k_email_ingestion_accounts PRIMARY KEY (id)
                );

                CREATE TABLE IF NOT EXISTS data_extraction."EmailMessages" (
                    id uuid NOT NULL,
                    email_ingestion_account_id uuid NOT NULL,
                    external_message_id character varying(500) NOT NULL,
                    uid bigint,
                    message_id_header character varying(500),
                    from_name character varying(250),
                    from_address character varying(320) NOT NULL,
                    to_addresses character varying(2000),
                    cc_addresses character varying(2000),
                    subject character varying(1000) NOT NULL,
                    body_text text,
                    body_html text,
                    received_at timestamp with time zone NOT NULL,
                    has_attachments boolean NOT NULL,
                    raw_email_storage_path character varying(1000),
                    raw_metadata_json text,
                    status character varying(50) NOT NULL,
                    error_message character varying(4000),
                    classification_confidence numeric(5,2),
                    classification_reason character varying(1000),
                    processed_at timestamp with time zone,
                    created_at_utc timestamp with time zone NOT NULL,
                    created_by text,
                    updated_at_utc timestamp with time zone,
                    updated_by text,
                    is_deleted boolean NOT NULL,
                    deleted_at_utc timestamp with time zone,
                    deleted_by text,
                    CONSTRAINT p_k_email_messages PRIMARY KEY (id)
                );

                CREATE TABLE IF NOT EXISTS data_extraction."EmailAttachments" (
                    id uuid NOT NULL,
                    email_message_id uuid NOT NULL,
                    file_name character varying(500) NOT NULL,
                    content_type character varying(250),
                    file_extension character varying(20),
                    size_bytes bigint NOT NULL,
                    file_hash character varying(128) NOT NULL,
                    storage_path character varying(1000) NOT NULL,
                    source_file_type character varying(50) NOT NULL,
                    status character varying(50) NOT NULL,
                    error_message character varying(4000),
                    processed_at timestamp with time zone,
                    created_at_utc timestamp with time zone NOT NULL,
                    created_by text,
                    updated_at_utc timestamp with time zone,
                    updated_by text,
                    is_deleted boolean NOT NULL,
                    deleted_at_utc timestamp with time zone,
                    deleted_by text,
                    CONSTRAINT p_k_email_attachments PRIMARY KEY (id)
                );

                CREATE TABLE IF NOT EXISTS data_extraction."EmailExtractionJobs" (
                    id uuid NOT NULL,
                    email_message_id uuid NOT NULL,
                    email_attachment_id uuid,
                    source_type character varying(50) NOT NULL,
                    provisional_pricing_import_id uuid NOT NULL,
                    extraction_execution_id uuid,
                    pricing_import_batch_id uuid,
                    status character varying(50) NOT NULL,
                    confidence_score numeric(5,2),
                    error_message character varying(4000),
                    started_at timestamp with time zone,
                    finished_at timestamp with time zone,
                    created_at_utc timestamp with time zone NOT NULL,
                    created_by text,
                    updated_at_utc timestamp with time zone,
                    updated_by text,
                    is_deleted boolean NOT NULL,
                    deleted_at_utc timestamp with time zone,
                    deleted_by text,
                    CONSTRAINT p_k_email_extraction_jobs PRIMARY KEY (id)
                );

                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_constraint WHERE conname = 'f_k_email_messages_email_ingestion_accounts_email_ingestion_account_id'
                    ) THEN
                        ALTER TABLE data_extraction."EmailMessages"
                            ADD CONSTRAINT f_k_email_messages_email_ingestion_accounts_email_ingestion_account_id
                            FOREIGN KEY (email_ingestion_account_id)
                            REFERENCES data_extraction."EmailIngestionAccounts" (id)
                            ON DELETE RESTRICT;
                    END IF;
                END $$;

                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_constraint WHERE conname = 'f_k_email_attachments_email_messages_email_message_id'
                    ) THEN
                        ALTER TABLE data_extraction."EmailAttachments"
                            ADD CONSTRAINT f_k_email_attachments_email_messages_email_message_id
                            FOREIGN KEY (email_message_id)
                            REFERENCES data_extraction."EmailMessages" (id)
                            ON DELETE RESTRICT;
                    END IF;
                END $$;

                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_constraint WHERE conname = 'f_k_email_extraction_jobs_email_messages_email_message_id'
                    ) THEN
                        ALTER TABLE data_extraction."EmailExtractionJobs"
                            ADD CONSTRAINT f_k_email_extraction_jobs_email_messages_email_message_id
                            FOREIGN KEY (email_message_id)
                            REFERENCES data_extraction."EmailMessages" (id)
                            ON DELETE RESTRICT;
                    END IF;
                END $$;

                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_constraint WHERE conname = 'f_k_email_extraction_jobs_email_attachments_email_attachment_id'
                    ) THEN
                        ALTER TABLE data_extraction."EmailExtractionJobs"
                            ADD CONSTRAINT f_k_email_extraction_jobs_email_attachments_email_attachment_id
                            FOREIGN KEY (email_attachment_id)
                            REFERENCES data_extraction."EmailAttachments" (id)
                            ON DELETE RESTRICT;
                    END IF;
                END $$;

                CREATE INDEX IF NOT EXISTS i_x_extraction_executions_source_origin_id
                    ON data_extraction."ExtractionExecutions" (source_origin_id);

                CREATE INDEX IF NOT EXISTS i_x_extraction_executions_source_email_message_id
                    ON data_extraction."ExtractionExecutions" (source_email_message_id);

                CREATE UNIQUE INDEX IF NOT EXISTS i_x_email_ingestion_accounts_email_address
                    ON data_extraction."EmailIngestionAccounts" (email_address);

                CREATE INDEX IF NOT EXISTS i_x_email_ingestion_accounts_is_active
                    ON data_extraction."EmailIngestionAccounts" (is_active);

                CREATE INDEX IF NOT EXISTS i_x_email_ingestion_accounts_provider_type
                    ON data_extraction."EmailIngestionAccounts" (provider_type);

                CREATE UNIQUE INDEX IF NOT EXISTS i_x_email_messages_account_external
                    ON data_extraction."EmailMessages" (email_ingestion_account_id, external_message_id);

                CREATE INDEX IF NOT EXISTS i_x_email_messages_email_ingestion_account_id
                    ON data_extraction."EmailMessages" (email_ingestion_account_id);

                CREATE INDEX IF NOT EXISTS i_x_email_messages_status
                    ON data_extraction."EmailMessages" (status);

                CREATE INDEX IF NOT EXISTS i_x_email_messages_received_at
                    ON data_extraction."EmailMessages" (received_at);

                CREATE INDEX IF NOT EXISTS i_x_email_messages_from_address
                    ON data_extraction."EmailMessages" (from_address);

                CREATE INDEX IF NOT EXISTS i_x_email_attachments_email_message_id
                    ON data_extraction."EmailAttachments" (email_message_id);

                CREATE INDEX IF NOT EXISTS i_x_email_attachments_file_hash
                    ON data_extraction."EmailAttachments" (file_hash);

                CREATE INDEX IF NOT EXISTS i_x_email_attachments_status
                    ON data_extraction."EmailAttachments" (status);

                CREATE UNIQUE INDEX IF NOT EXISTS i_x_email_attachments_message_hash
                    ON data_extraction."EmailAttachments" (email_message_id, file_hash);

                CREATE INDEX IF NOT EXISTS i_x_email_extraction_jobs_email_message_id
                    ON data_extraction."EmailExtractionJobs" (email_message_id);

                CREATE INDEX IF NOT EXISTS i_x_email_extraction_jobs_email_attachment_id
                    ON data_extraction."EmailExtractionJobs" (email_attachment_id);

                CREATE INDEX IF NOT EXISTS i_x_email_extraction_jobs_extraction_execution_id
                    ON data_extraction."EmailExtractionJobs" (extraction_execution_id);

                CREATE INDEX IF NOT EXISTS i_x_email_extraction_jobs_status
                    ON data_extraction."EmailExtractionJobs" (status);

                CREATE INDEX IF NOT EXISTS i_x_email_extraction_jobs_provisional_pricing_import_id
                    ON data_extraction."EmailExtractionJobs" (provisional_pricing_import_id);
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TABLE IF EXISTS data_extraction."EmailExtractionJobs" CASCADE;
                DROP TABLE IF EXISTS data_extraction."EmailAttachments" CASCADE;
                DROP TABLE IF EXISTS data_extraction."EmailMessages" CASCADE;
                DROP TABLE IF EXISTS data_extraction."EmailIngestionAccounts" CASCADE;

                DROP INDEX IF EXISTS data_extraction.i_x_extraction_executions_source_origin_id;
                DROP INDEX IF EXISTS data_extraction.i_x_extraction_executions_source_email_message_id;

                ALTER TABLE IF EXISTS data_extraction."ExtractionExecutions"
                    DROP COLUMN IF EXISTS source_email_attachment_id;

                ALTER TABLE IF EXISTS data_extraction."ExtractionExecutions"
                    DROP COLUMN IF EXISTS source_email_message_id;

                ALTER TABLE IF EXISTS data_extraction."ExtractionExecutions"
                    DROP COLUMN IF EXISTS source_origin_id;

                ALTER TABLE IF EXISTS data_extraction."ExtractionExecutions"
                    DROP COLUMN IF EXISTS source_origin_type;
            """);
        }
    }
}
