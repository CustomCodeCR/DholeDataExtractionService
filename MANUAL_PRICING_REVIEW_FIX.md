# Revisión manual de extracción en Pricing

Cuando un trabajo termina en `NeedsReview` pero contiene una `ExtractionExecutionId` y filas persistidas, el API permite enviarlo manualmente a Pricing mediante:

`POST /api/data-extraction/email/extraction-jobs/{jobId}/send-to-pricing`

La operación no vuelve a extraer el correo y no invoca AI. Reconstruye el resultado persistido, publica `pricing.import-from-extraction.requested` mediante Outbox y cambia el trabajo a `AwaitingPricing`. Cuando Pricing confirma la importación, el trabajo obtiene `PricingImportBatchId` y el Web abre la bandeja filtrada por ese lote.
