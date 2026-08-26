from pathlib import Path

path = Path("src/Dhole.DataExtraction.Workers/Streams/AiPricingEmailAnalysisCompletedStreamHandler.cs")
text = path.read_text()

account_block = '''        var account = await dbContext.EmailIngestionAccounts.FirstOrDefaultAsync(
            item =>
                item.Id == message.EmailIngestionAccountId && !item.IsDeleted,
            cancellationToken
        ) ?? throw new InvalidOperationException(
            "No se encontró la cuenta asociada al resultado de AI."
        );
'''
if account_block not in text:
    raise SystemExit("Email account lookup block not found")
text = text.replace(account_block, "", 1)

confidence_gate = '''                attachment?.MarkExtracted();
                var shouldSendToPricing =
                    account.AutoSendToPricing
                    && confidence >= account.AutoSendMinConfidence;
                if (!shouldSendToPricing)
                {
                    var reason =
                        $"Extracción validada por AI con confianza {confidence:0.##}%. "
                        + "Requiere revisión antes de crear la tarifa en Pricing.";
                    job.MarkNeedsReview(
                        response.ExtractionExecutionId,
                        confidence,
                        reason,
                        "DataExtraction.MinimumConfidenceNotMet"
                    );
                    aiRequest.MarkCompleted();
                    await EmailJobStateCoordinator.RecalculateAsync(
                        dbContext,
                        job.EmailMessageId,
                        cancellationToken
                    );
                    await dbContext.SaveChangesAsync(cancellationToken);
                    return null;
                }

'''
replacement = '''                // Toda extracción AI que produjo filas utilizables y pasó las
                // validaciones estructurales entra directamente a la bandeja de
                // revisión de Pricing. La confianza se conserva como metadato, pero
                // no crea una segunda aprobación intermedia en DataExtraction.
                attachment?.MarkExtracted();

'''
if confidence_gate not in text:
    raise SystemExit("AI confidence review gate not found")
text = text.replace(confidence_gate, replacement, 1)

path.write_text(text)
