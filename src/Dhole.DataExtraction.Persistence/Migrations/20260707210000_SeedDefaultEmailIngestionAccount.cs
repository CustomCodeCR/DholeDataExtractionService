using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.DataExtraction.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeedDefaultEmailIngestionAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO data_extraction."EmailIngestionAccounts"
                (
                    id,
                    name,
                    email_address,
                    provider_type,
                    host,
                    port,
                    use_ssl,
                    username,
                    secret_reference,
                    folder_name,
                    polling_interval_minutes,
                    auto_process,
                    auto_send_to_pricing,
                    auto_send_min_confidence,
                    process_body_when_no_supported_attachments,
                    process_body_even_with_attachments,
                    allowed_senders,
                    is_active,
                    created_at_utc,
                    is_deleted
                )
                VALUES
                (
                    '4f1bd7f0-dc89-4d21-a4d0-12c2e7f2c311',
                    'Correo de extracción de tarifas',
                    'pricing-imports@example.com',
                    'Gmail',
                    'imap.gmail.com',
                    993,
                    true,
                    'pricing-imports@example.com',
                    'DATA_EXTRACTION_EMAIL_PASSWORD',
                    'INBOX',
                    5,
                    false,
                    false,
                    90,
                    true,
                    false,
                    '*',
                    true,
                    '2026-07-07T00:00:00Z',
                    false
                )
                ON CONFLICT (email_address)
                DO UPDATE SET
                    name = EXCLUDED.name,
                    provider_type = EXCLUDED.provider_type,
                    host = EXCLUDED.host,
                    port = EXCLUDED.port,
                    use_ssl = EXCLUDED.use_ssl,
                    username = EXCLUDED.username,
                    secret_reference = EXCLUDED.secret_reference,
                    folder_name = EXCLUDED.folder_name,
                    polling_interval_minutes = EXCLUDED.polling_interval_minutes,
                    auto_process = EXCLUDED.auto_process,
                    auto_send_to_pricing = EXCLUDED.auto_send_to_pricing,
                    auto_send_min_confidence = EXCLUDED.auto_send_min_confidence,
                    process_body_when_no_supported_attachments = EXCLUDED.process_body_when_no_supported_attachments,
                    process_body_even_with_attachments = EXCLUDED.process_body_even_with_attachments,
                    allowed_senders = EXCLUDED.allowed_senders,
                    is_active = EXCLUDED.is_active,
                    is_deleted = false,
                    updated_at_utc = NOW();
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM data_extraction."EmailIngestionAccounts"
                WHERE email_address = 'pricing-imports@example.com';
            """);
        }
    }
}
