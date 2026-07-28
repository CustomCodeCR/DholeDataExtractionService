namespace Dhole.DataExtraction.Infrastructure.Mapping;

/// <summary>
/// Commercial China base ports. These names are extraction candidates only:
/// every expanded value still has to resolve against the active Config "pol"
/// catalog before an imported rate can be approved.
/// </summary>
public static class PricingBasePorts
{
    public static readonly IReadOnlyList<string> China =
    [
        "Shanghai",
        "Ningbo-Zhoushan",
        "Shenzhen",
        "Qingdao",
        "Guangzhou (Nansha)",
        "Tianjin (Xingang)",
        "Xiamen",
        "Dalian",
        "Lianyungang",
        "Yantian (Shenzhen)",
    ];

    public static bool IsChinaOrAsiaBasePorts(string normalizedValue)
    {
        return normalizedValue
            is "chinabaseports"
                or "chinabaseport"
                or "baseportschina"
                or "baseportchina"
                or "asiabaseports"
                or "asiabaseport"
                or "baseportsasia"
                or "baseportasia";
    }
}
