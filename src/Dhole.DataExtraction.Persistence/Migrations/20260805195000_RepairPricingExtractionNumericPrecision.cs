using Dhole.DataExtraction.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.DataExtraction.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260805195000_RepairPricingExtractionNumericPrecision")]
public sealed class RepairPricingExtractionNumericPrecision : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Some development databases were created from an older snapshot with narrower
        // numeric columns. Reassert the precision expected by the current model. The CASE
        // expressions also prevent a legacy malformed value from blocking the repair.
        migrationBuilder.Sql(
            """
            DO $$
            BEGIN
                IF to_regclass('data_extraction."PricingExtractionRecords"') IS NOT NULL THEN
                    ALTER TABLE data_extraction."PricingExtractionRecords"
                        ALTER COLUMN ocean_freight TYPE numeric(18,4)
                            USING CASE WHEN ocean_freight IS NULL OR abs(ocean_freight) <= 99999999999999.9999
                                THEN ocean_freight::numeric(18,4) ELSE NULL END,
                        ALTER COLUMN origin_charges TYPE numeric(18,4)
                            USING CASE WHEN origin_charges IS NULL OR abs(origin_charges) <= 99999999999999.9999
                                THEN origin_charges::numeric(18,4) ELSE NULL END,
                        ALTER COLUMN destination_charges TYPE numeric(18,4)
                            USING CASE WHEN destination_charges IS NULL OR abs(destination_charges) <= 99999999999999.9999
                                THEN destination_charges::numeric(18,4) ELSE NULL END,
                        ALTER COLUMN surcharges TYPE numeric(18,4)
                            USING CASE WHEN surcharges IS NULL OR abs(surcharges) <= 99999999999999.9999
                                THEN surcharges::numeric(18,4) ELSE NULL END,
                        ALTER COLUMN total_cost TYPE numeric(18,4)
                            USING CASE WHEN total_cost IS NULL OR abs(total_cost) <= 99999999999999.9999
                                THEN total_cost::numeric(18,4) ELSE NULL END,
                        ALTER COLUMN total_sale TYPE numeric(18,4)
                            USING CASE WHEN total_sale IS NULL OR abs(total_sale) <= 99999999999999.9999
                                THEN total_sale::numeric(18,4) ELSE NULL END,
                        ALTER COLUMN profit TYPE numeric(18,4)
                            USING CASE WHEN profit IS NULL OR abs(profit) <= 99999999999999.9999
                                THEN profit::numeric(18,4) ELSE NULL END,
                        ALTER COLUMN margin TYPE numeric(18,4)
                            USING CASE WHEN margin IS NULL OR abs(margin) <= 99999999999999.9999
                                THEN margin::numeric(18,4) ELSE NULL END;
                END IF;
            END $$;
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The model already defines numeric(18,4); reverting to an unknown historical
        // precision would be unsafe and environment-specific.
    }
}
