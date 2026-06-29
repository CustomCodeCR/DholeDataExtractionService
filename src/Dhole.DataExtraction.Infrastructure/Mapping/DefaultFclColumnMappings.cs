namespace Dhole.DataExtraction.Infrastructure.Mapping;

public static class DefaultFclColumnMappings
{
    public static readonly IReadOnlyDictionary<string, string> Mappings = new Dictionary<
        string,
        string
    >
    {
        ["actualizado"] = "UpdatedAt",

        ["pol"] = "OriginPort",
        ["origin"] = "OriginPort",
        ["origen"] = "OriginPort",
        ["puertoorigen"] = "OriginPort",

        ["poe"] = "PortOfExit",
        ["portofexit"] = "PortOfExit",
        ["puertosalida"] = "PortOfExit",

        ["pod"] = "DestinationPort",
        ["destination"] = "DestinationPort",
        ["destino"] = "DestinationPort",
        ["puertodestino"] = "DestinationPort",

        ["carrier"] = "Carrier",
        ["naviera"] = "Carrier",
        ["linea"] = "Carrier",
        ["shippingline"] = "Carrier",

        ["agent"] = "Agent",
        ["agente"] = "Agent",

        ["container"] = "ContainerType",
        ["equipment"] = "ContainerType",
        ["equipo"] = "ContainerType",
        ["tipocontenedor"] = "ContainerType",
        ["cntr"] = "ContainerType",

        ["commodity"] = "Commodity",
        ["mercancia"] = "Commodity",
        ["producto"] = "Commodity",

        ["currency"] = "Currency",
        ["moneda"] = "Currency",

        ["diastransito"] = "TransitDays",
        ["transitdays"] = "TransitDays",
        ["tiempotransito"] = "TransitDays",

        ["diaslibres"] = "FreeDays",
        ["freedays"] = "FreeDays",
        ["demurragefreedays"] = "FreeDays",

        ["validfrom"] = "ValidFrom",
        ["vigenciadesde"] = "ValidFrom",
        ["desde"] = "ValidFrom",

        ["validto"] = "ValidTo",
        ["validuntil"] = "ValidTo",
        ["vigenciahasta"] = "ValidTo",
        ["hasta"] = "ValidTo",

        ["oceanfreight"] = "OceanFreight",
        ["freight"] = "OceanFreight",
        ["flete"] = "OceanFreight",
        ["fleteoceanico"] = "OceanFreight",
        ["fletemaritimocosto"] = "OceanFreight",
        ["fleteoceanicocosto"] = "OceanFreight",

        ["profitagentecosto"] = "AgentProfitCost",
        ["agentprofitcost"] = "AgentProfitCost",

        ["releaseagentecosto"] = "AgentReleaseCost",
        ["agentreleasecost"] = "AgentReleaseCost",
        ["releasecosto"] = "AgentReleaseCost",

        ["thcdcosto"] = "DestinationThcCost",
        ["thddcosto"] = "DestinationThcCost",
        ["thcd"] = "DestinationThcCost",
        ["thcdestinationcosto"] = "DestinationThcCost",

        ["documentacioncosto"] = "DocumentationCost",
        ["documentationcost"] = "DocumentationCost",

        ["containerprotectcosto"] = "ContainerProtectCost",
        ["containerprotectioncosto"] = "ContainerProtectCost",

        ["muellajecosto"] = "WharfageCost",
        ["wharfagecost"] = "WharfageCost",

        ["merchantcosto"] = "MerchantCost",
        ["merchantcost"] = "MerchantCost",

        ["fleteinternocosto"] = "InternalFreightCost",
        ["internalfreightcost"] = "InternalFreightCost",

        ["carruselcosto"] = "CarouselCost",
        ["carouselcost"] = "CarouselCost",

        ["manejosptycosto"] = "PanamaHandlingCost",
        ["manejospty"] = "PanamaHandlingCost",
        ["panamahandlingcost"] = "PanamaHandlingCost",

        ["fleteinternacionalterrestrecosto"] = "InternationalLandFreightCost",
        ["landfreightcost"] = "InternationalLandFreightCost",

        ["bunkercosto"] = "BunkerCost",
        ["bunker"] = "BunkerCost",

        ["origincharges"] = "OriginCharges",
        ["gastosorigen"] = "OriginCharges",
        ["origencharges"] = "OriginCharges",

        ["destinationcharges"] = "DestinationCharges",
        ["gastosdestino"] = "DestinationCharges",
        ["destinocharges"] = "DestinationCharges",

        ["surcharges"] = "Surcharges",
        ["recargos"] = "Surcharges",

        ["totalcost"] = "TotalCost",
        ["totalcostos"] = "TotalCost",
        ["costototal"] = "TotalCost",
        ["costostotales"] = "TotalCost",

        ["fleteinternacionalventa"] = "InternationalFreightSale",
        ["internationalfreightsale"] = "InternationalFreightSale",

        ["allin"] = "AllInSale",
        ["allinventa"] = "AllInSale",

        ["cargosendestinoventa"] = "DestinationChargesSale",
        ["destinationchargessale"] = "DestinationChargesSale",

        ["carruselventa"] = "CarouselSale",
        ["carouselsale"] = "CarouselSale",

        ["fleteinternoventa"] = "InternalFreightSale",
        ["internalfreightsale"] = "InternalFreightSale",

        ["manejosventa"] = "HandlingSale",
        ["handlingsale"] = "HandlingSale",

        ["totalsale"] = "TotalSale",
        ["venta"] = "TotalSale",
        ["totalventa"] = "TotalSale",

        ["profit"] = "Profit",
        ["utilidad"] = "Profit",

        ["margin"] = "Margin",
        ["margen"] = "Margin",

        ["space"] = "SpaceComment",
        ["spaces"] = "SpaceComment",
        ["espacios"] = "SpaceComment",
        ["comentarioespacios"] = "SpaceComment",
        ["comentariosdeespacios"] = "SpaceComment",
        ["comentariosespacios"] = "SpaceComment",

        ["remarks"] = "Remarks",
        ["observaciones"] = "Remarks",
        ["comentarios"] = "Remarks",
        ["via"] = "RouteMode",
        ["route"] = "RouteMode",
        ["routemode"] = "RouteMode",
    };
}
