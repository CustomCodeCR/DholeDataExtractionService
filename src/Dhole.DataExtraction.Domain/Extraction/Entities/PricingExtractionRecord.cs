using CustomCodeFramework.Core.Domain.Entities;
using Dhole.DataExtraction.Domain.Extraction.Enums;

namespace Dhole.DataExtraction.Domain.Extraction.Entities;

public sealed class PricingExtractionRecord : SoftDeletableAggregateRoot<Guid>
{
    private PricingExtractionRecord() { }

    private PricingExtractionRecord(
        Guid id,
        Guid extractionExecutionId,
        Guid sourceDocumentId,
        string? sourceSheetName,
        int? sourceRowNumber,
        string? originPort,
        string? portOfExit,
        string? destinationPort,
        string? containerType,
        string? carrier,
        string? agent,
        string? commodity,
        string? currency,
        DateTime? validFrom,
        DateTime? validTo,
        decimal? oceanFreight,
        decimal? originCharges,
        decimal? destinationCharges,
        decimal? surcharges,
        decimal? totalCost,
        decimal? totalSale,
        decimal? profit,
        decimal? margin,
        string? spaceComment,
        string? remarks,
        string? rawJson,
        Guid? createdBy
    )
        : base(id)
    {
        ExtractionExecutionId = extractionExecutionId;
        SourceDocumentId = sourceDocumentId;

        SourceSheetName = string.IsNullOrWhiteSpace(sourceSheetName)
            ? null
            : sourceSheetName.Trim();
        SourceRowNumber = sourceRowNumber;

        OriginPort = Normalize(originPort);
        PortOfExit = Normalize(portOfExit);
        DestinationPort = Normalize(destinationPort);
        ContainerType = Normalize(containerType);
        Carrier = Normalize(carrier);
        Agent = Normalize(agent);
        Commodity = Normalize(commodity);
        Currency = Normalize(currency);

        ValidFrom = validFrom;
        ValidTo = validTo;

        OceanFreight = oceanFreight;
        OriginCharges = originCharges;
        DestinationCharges = destinationCharges;
        Surcharges = surcharges;
        TotalCost = totalCost;
        TotalSale = totalSale;
        Profit = profit;
        Margin = margin;

        SpaceComment = string.IsNullOrWhiteSpace(spaceComment) ? null : spaceComment.Trim();
        Remarks = string.IsNullOrWhiteSpace(remarks) ? null : remarks.Trim();

        RawJson = string.IsNullOrWhiteSpace(rawJson) ? null : rawJson;

        Status = PricingExtractionRecordStatus.Extracted;

        MarkAsCreated(DateTime.UtcNow, createdBy?.ToString());
    }

    public Guid ExtractionExecutionId { get; private set; }
    public Guid SourceDocumentId { get; private set; }

    public string? SourceSheetName { get; private set; }
    public int? SourceRowNumber { get; private set; }

    public string? OriginPort { get; private set; }
    public string? PortOfExit { get; private set; }
    public string? DestinationPort { get; private set; }
    public string? ContainerType { get; private set; }
    public string? Carrier { get; private set; }
    public string? Agent { get; private set; }
    public string? Commodity { get; private set; }
    public string? Currency { get; private set; }

    public DateTime? ValidFrom { get; private set; }
    public DateTime? ValidTo { get; private set; }

    public decimal? OceanFreight { get; private set; }
    public decimal? OriginCharges { get; private set; }
    public decimal? DestinationCharges { get; private set; }
    public decimal? Surcharges { get; private set; }
    public decimal? TotalCost { get; private set; }
    public decimal? TotalSale { get; private set; }
    public decimal? Profit { get; private set; }
    public decimal? Margin { get; private set; }

    public string? SpaceComment { get; private set; }
    public string? Remarks { get; private set; }

    public PricingExtractionRecordStatus Status { get; private set; }

    public string? RawJson { get; private set; }

    public static PricingExtractionRecord Create(
        Guid extractionExecutionId,
        Guid sourceDocumentId,
        string? sourceSheetName,
        int? sourceRowNumber,
        string? originPort,
        string? portOfExit,
        string? destinationPort,
        string? containerType,
        string? carrier,
        string? agent,
        string? commodity,
        string? currency,
        DateTime? validFrom,
        DateTime? validTo,
        decimal? oceanFreight,
        decimal? originCharges,
        decimal? destinationCharges,
        decimal? surcharges,
        decimal? totalCost,
        decimal? totalSale,
        decimal? profit,
        decimal? margin,
        string? spaceComment,
        string? remarks,
        string? rawJson,
        Guid? createdBy
    )
    {
        return new PricingExtractionRecord(
            Guid.NewGuid(),
            extractionExecutionId,
            sourceDocumentId,
            sourceSheetName,
            sourceRowNumber,
            originPort,
            portOfExit,
            destinationPort,
            containerType,
            carrier,
            agent,
            commodity,
            currency,
            validFrom,
            validTo,
            oceanFreight,
            originCharges,
            destinationCharges,
            surcharges,
            totalCost,
            totalSale,
            profit,
            margin,
            spaceComment,
            remarks,
            rawJson,
            createdBy
        );
    }

    public void MarkAsValid(Guid? updatedBy = null)
    {
        Status = PricingExtractionRecordStatus.Valid;
        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());
    }

    public void MarkAsRequiresReview(Guid? updatedBy = null)
    {
        Status = PricingExtractionRecordStatus.RequiresReview;
        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());
    }

    public void MarkAsInvalid(Guid? updatedBy = null)
    {
        Status = PricingExtractionRecordStatus.Invalid;
        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
