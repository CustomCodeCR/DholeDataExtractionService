namespace Dhole.DataExtraction.Infrastructure.Mapping;

public static class DefaultFclColumnMappings
{
    public static readonly IReadOnlyDictionary<string, string> Mappings = new Dictionary<
        string,
        string
    >
    {
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

        ["origincharges"] = "OriginCharges",
        ["gastosorigen"] = "OriginCharges",
        ["origencharges"] = "OriginCharges",

        ["destinationcharges"] = "DestinationCharges",
        ["gastosdestino"] = "DestinationCharges",
        ["destinocharges"] = "DestinationCharges",

        ["surcharges"] = "Surcharges",
        ["recargos"] = "Surcharges",

        ["totalcost"] = "TotalCost",
        ["costototal"] = "TotalCost",

        ["totalsale"] = "TotalSale",
        ["allin"] = "TotalSale",
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

        ["remarks"] = "Remarks",
        ["observaciones"] = "Remarks",
        ["comentarios"] = "Remarks",
    };
}
