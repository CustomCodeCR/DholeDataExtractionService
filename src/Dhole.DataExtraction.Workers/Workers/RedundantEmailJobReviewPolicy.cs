using Dhole.DataExtraction.Domain.Emails.Entities;

namespace Dhole.DataExtraction.Workers.Workers;

internal static class RedundantEmailJobReviewPolicy
{
    private static readonly string[] StructuralIdentityIssueCodes =
    [
        "missing_origin_port",
        "missing_container_type",
        "missing_carrier",
        "missing_valid_from",
        "missing_valid_to",
    ];

    public static bool IsRedundantAfterPricingSuccess(EmailExtractionJob job)
    {
        if (
            string.Equals(
                job.LastErrorCode,
                "AI.NoPricingRows",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return true;
        }

        var message = job.ErrorMessage ?? string.Empty;
        if (
            message.Contains(
                "AI no encontró filas de tarifas utilizables",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return true;
        }

        var isDeterministicFallbackReview =
            string.Equals(
                job.LastErrorCode,
                "DataExtraction.DeterministicFallbackRequiresReview",
                StringComparison.OrdinalIgnoreCase
            )
            || message.Contains(
                "AI no produjo filas utilizables, pero DataExtraction sí conservó filas del adjunto",
                StringComparison.OrdinalIgnoreCase
            );

        var isAiBlockingReview =
            string.Equals(
                job.LastErrorCode,
                "DataExtraction.AiResultHasBlockingIssues",
                StringComparison.OrdinalIgnoreCase
            )
            || message.Contains(
                "validaciones estructurales bloqueantes",
                StringComparison.OrdinalIgnoreCase
            );

        return (isDeterministicFallbackReview || isAiBlockingReview)
            && LooksLikeNonPricingContent(message);
    }

    private static bool LooksLikeNonPricingContent(string message)
    {
        if (
            !message.Contains(
                "missing_rate_amount",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return false;
        }

        var structuralMisses = StructuralIdentityIssueCodes.Count(code =>
            message.Contains(code, StringComparison.OrdinalIgnoreCase)
        );

        // Un contenido que no tiene monto y además carece de varias piezas básicas
        // de identidad tarifaria (POL, equipo, naviera o vigencias) no representa
        // una segunda tarifa pendiente: normalmente es un adjunto complementario,
        // como cargos locales. Solo se considera redundante cuando otro trabajo del
        // mismo correo ya llegó correctamente a Pricing.
        return structuralMisses >= 3;
    }
}
