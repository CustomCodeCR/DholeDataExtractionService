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
        ["portloading"] = "OriginPort",
        ["loading"] = "OriginPort",

        ["poe"] = "PortOfExit",
        ["portofexit"] = "PortOfExit",
        ["puertosalida"] = "PortOfExit",

        ["pod"] = "DestinationPort",
        ["destination"] = "DestinationPort",
        ["destino"] = "DestinationPort",
        ["puertodestino"] = "DestinationPort",
        ["portdischarge"] = "DestinationPort",
        ["discharge"] = "DestinationPort",

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
        ["freetime"] = "FreeDays",
        ["free"] = "FreeDays",
        ["demurragefreedays"] = "FreeDays",

        ["validfrom"] = "ValidFrom",
        ["vigenciadesde"] = "ValidFrom",
        ["desde"] = "ValidFrom",
        ["effective"] = "ValidFrom",

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

        ["originport"] = "OriginPort",
        ["portofloading"] = "OriginPort",
        ["loadingport"] = "OriginPort",
        ["loadport"] = "OriginPort",
        ["from"] = "OriginPort",
        ["por"] = "OriginPort",
        ["placeofreceipt"] = "OriginPort",

        ["exitport"] = "PortOfExit",
        ["transshipmentport"] = "PortOfExit",
        ["transbordo"] = "PortOfExit",

        ["destinationport"] = "DestinationPort",
        ["portofdischarge"] = "DestinationPort",
        ["dischargeport"] = "DestinationPort",
        ["finaldestination"] = "DestinationPort",
        ["placeofdelivery"] = "DestinationPort",
        ["delivery"] = "DestinationPort",
        ["to"] = "DestinationPort",

        ["steamshipline"] = "Carrier",
        ["shippingcompany"] = "Carrier",
        ["lineamaritima"] = "Carrier",
        ["ssl"] = "Carrier",

        ["eq"] = "ContainerType",
        ["equip"] = "ContainerType",
        ["eqtype"] = "ContainerType",
        ["equipmenttype"] = "ContainerType",
        ["containertype"] = "ContainerType",
        ["type"] = "ContainerType",
        ["size"] = "ContainerType",
        ["sizetype"] = "ContainerType",

        ["curr"] = "Currency",
        ["ccy"] = "Currency",
        ["divisa"] = "Currency",

        ["tt"] = "TransitDays",
        ["transittime"] = "TransitDays",
        ["tiempoentransito"] = "TransitDays",

        ["fd"] = "FreeDays",
        ["free time"] = "FreeDays",
        ["detentionfreedays"] = "FreeDays",
        ["demdetfreedays"] = "FreeDays",

        ["validity"] = "ValidTo",
        ["expiration"] = "ValidTo",
        ["expiry"] = "ValidTo",
        ["expiredate"] = "ValidTo",
        ["effectivedate"] = "ValidFrom",
        ["effectivefrom"] = "ValidFrom",
        ["effectiveto"] = "ValidTo",

        ["of"] = "OceanFreight",
        ["ocf"] = "OceanFreight",
        ["seafreight"] = "OceanFreight",
        ["basicfreight"] = "OceanFreight",
        ["tarifa"] = "OceanFreight",
        ["rate"] = "OceanFreight",
        ["amount"] = "OceanFreight",
        ["precio"] = "OceanFreight",
        ["price"] = "OceanFreight",
        ["freightrate"] = "OceanFreight",
        ["rateusd"] = "OceanFreight",

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
