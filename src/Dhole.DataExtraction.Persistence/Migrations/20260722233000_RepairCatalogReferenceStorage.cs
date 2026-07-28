using Dhole.DataExtraction.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.DataExtraction.Persistence.Migrations;

/// <summary>
/// Repara instalaciones donde la migración Fix130726 fue aplicada antes de que
/// se agregaran las referencias canónicas de Config, o donde EF creó la primera
/// navegación owned en la tabla secundaria catalog_item_reference.
/// </summary>
[DbContext(typeof(ServiceDbContext))]
[Migration("20260722233000_RepairCatalogReferenceStorage")]
public partial class RepairCatalogReferenceStorage : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE data_extraction."PricingExtractionRecords"
                ADD COLUMN IF NOT EXISTS origin_port_catalog_item_id uuid NULL,
                ADD COLUMN IF NOT EXISTS origin_port_catalog_group_slug character varying(100) NULL,
                ADD COLUMN IF NOT EXISTS origin_port_catalog_code character varying(100) NULL,
                ADD COLUMN IF NOT EXISTS origin_port_catalog_slug character varying(150) NULL,
                ADD COLUMN IF NOT EXISTS origin_port_catalog_name character varying(250) NULL,
                ADD COLUMN IF NOT EXISTS origin_port_raw_value character varying(500) NULL,
                ADD COLUMN IF NOT EXISTS port_of_exit_catalog_item_id uuid NULL,
                ADD COLUMN IF NOT EXISTS port_of_exit_catalog_group_slug character varying(100) NULL,
                ADD COLUMN IF NOT EXISTS port_of_exit_catalog_code character varying(100) NULL,
                ADD COLUMN IF NOT EXISTS port_of_exit_catalog_slug character varying(150) NULL,
                ADD COLUMN IF NOT EXISTS port_of_exit_catalog_name character varying(250) NULL,
                ADD COLUMN IF NOT EXISTS port_of_exit_raw_value character varying(500) NULL,
                ADD COLUMN IF NOT EXISTS destination_port_catalog_item_id uuid NULL,
                ADD COLUMN IF NOT EXISTS destination_port_catalog_group_slug character varying(100) NULL,
                ADD COLUMN IF NOT EXISTS destination_port_catalog_code character varying(100) NULL,
                ADD COLUMN IF NOT EXISTS destination_port_catalog_slug character varying(150) NULL,
                ADD COLUMN IF NOT EXISTS destination_port_catalog_name character varying(250) NULL,
                ADD COLUMN IF NOT EXISTS destination_port_raw_value character varying(500) NULL,
                ADD COLUMN IF NOT EXISTS container_type_catalog_item_id uuid NULL,
                ADD COLUMN IF NOT EXISTS container_type_catalog_group_slug character varying(100) NULL,
                ADD COLUMN IF NOT EXISTS container_type_catalog_code character varying(100) NULL,
                ADD COLUMN IF NOT EXISTS container_type_catalog_slug character varying(150) NULL,
                ADD COLUMN IF NOT EXISTS container_type_catalog_name character varying(250) NULL,
                ADD COLUMN IF NOT EXISTS container_type_raw_value character varying(500) NULL,
                ADD COLUMN IF NOT EXISTS carrier_catalog_item_id uuid NULL,
                ADD COLUMN IF NOT EXISTS carrier_catalog_group_slug character varying(100) NULL,
                ADD COLUMN IF NOT EXISTS carrier_catalog_code character varying(100) NULL,
                ADD COLUMN IF NOT EXISTS carrier_catalog_slug character varying(150) NULL,
                ADD COLUMN IF NOT EXISTS carrier_catalog_name character varying(250) NULL,
                ADD COLUMN IF NOT EXISTS carrier_raw_value character varying(500) NULL,
                ADD COLUMN IF NOT EXISTS agent_catalog_item_id uuid NULL,
                ADD COLUMN IF NOT EXISTS agent_catalog_group_slug character varying(100) NULL,
                ADD COLUMN IF NOT EXISTS agent_catalog_code character varying(100) NULL,
                ADD COLUMN IF NOT EXISTS agent_catalog_slug character varying(150) NULL,
                ADD COLUMN IF NOT EXISTS agent_catalog_name character varying(250) NULL,
                ADD COLUMN IF NOT EXISTS agent_raw_value character varying(500) NULL,
                ADD COLUMN IF NOT EXISTS currency_catalog_item_id uuid NULL,
                ADD COLUMN IF NOT EXISTS currency_catalog_group_slug character varying(100) NULL,
                ADD COLUMN IF NOT EXISTS currency_catalog_code character varying(100) NULL,
                ADD COLUMN IF NOT EXISTS currency_catalog_slug character varying(150) NULL,
                ADD COLUMN IF NOT EXISTS currency_catalog_name character varying(250) NULL,
                ADD COLUMN IF NOT EXISTS currency_raw_value character varying(500) NULL;
            """
        );

        migrationBuilder.Sql(
            """
            DO $repair$
            BEGIN
                IF to_regclass('data_extraction.catalog_item_reference') IS NOT NULL THEN
                    UPDATE data_extraction."PricingExtractionRecords" AS record
                    SET
                        origin_port_catalog_item_id = COALESCE(record.origin_port_catalog_item_id, reference.origin_port_catalog_item_id),
                        origin_port_catalog_group_slug = COALESCE(record.origin_port_catalog_group_slug, reference.origin_port_catalog_group_slug),
                        origin_port_catalog_code = COALESCE(record.origin_port_catalog_code, reference.origin_port_catalog_code),
                        origin_port_catalog_slug = COALESCE(record.origin_port_catalog_slug, reference.origin_port_catalog_slug),
                        origin_port_catalog_name = COALESCE(record.origin_port_catalog_name, reference.origin_port_catalog_name),
                        origin_port_raw_value = COALESCE(record.origin_port_raw_value, reference.origin_port_raw_value)
                    FROM data_extraction.catalog_item_reference AS reference
                    WHERE record.id = reference."PricingExtractionRecordId";

                    DROP TABLE data_extraction.catalog_item_reference;
                END IF;
            END
            $repair$;
            """
        );

        migrationBuilder.Sql(
            """
            CREATE INDEX IF NOT EXISTS "IX_PricingExtractionRecords_origin_port_catalog_item_id"
                ON data_extraction."PricingExtractionRecords" (origin_port_catalog_item_id);
            CREATE INDEX IF NOT EXISTS "IX_PricingExtractionRecords_port_of_exit_catalog_item_id"
                ON data_extraction."PricingExtractionRecords" (port_of_exit_catalog_item_id);
            CREATE INDEX IF NOT EXISTS "IX_PricingExtractionRecords_destination_port_catalog_item_id"
                ON data_extraction."PricingExtractionRecords" (destination_port_catalog_item_id);
            CREATE INDEX IF NOT EXISTS "IX_PricingExtractionRecords_container_type_catalog_item_id"
                ON data_extraction."PricingExtractionRecords" (container_type_catalog_item_id);
            CREATE INDEX IF NOT EXISTS "IX_PricingExtractionRecords_carrier_catalog_item_id"
                ON data_extraction."PricingExtractionRecords" (carrier_catalog_item_id);
            CREATE INDEX IF NOT EXISTS "IX_PricingExtractionRecords_agent_catalog_item_id"
                ON data_extraction."PricingExtractionRecords" (agent_catalog_item_id);
            CREATE INDEX IF NOT EXISTS "IX_PricingExtractionRecords_currency_catalog_item_id"
                ON data_extraction."PricingExtractionRecords" (currency_catalog_item_id);
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Reparación deliberadamente no destructiva: no se eliminan columnas ni
        // referencias canónicas al revertir para evitar pérdida de datos extraídos.
    }
}
