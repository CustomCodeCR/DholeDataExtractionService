# Flujo híbrido y asíncrono de correos

El worker ejecuta primero la extracción y normalización determinística contra
los catálogos de Config. La confianza final es el menor valor entre la calidad
estructural y el porcentaje de referencias normalizadas de POL, POE, tipo de
contenedor, naviera, moneda y, cuando existan, POD y agente.

- Confianza igual o superior a 75%: AI se omite.
- Confianza entre 75% y el `AutoSendMinConfidence` de la cuenta: se conserva el
  resultado determinístico y se envía a revisión.
- Confianza igual o superior al `AutoSendMinConfidence`: se publica directamente
  `pricing.import-from-extraction.requested` mediante Outbox.
- Confianza menor a 75% o resultado no utilizable: se publica
  `ai.pricing-email-analysis.requested` mediante Outbox.

AI procesa fragmentos pequeños, publica el resultado de vuelta y DataExtraction
lo vuelve a validar contra Config. Si AI falla pero ya existían filas
determinísticas, esas filas se conservan para revisión en lugar de perder la
extracción completa.

Configuración de despliegue:

- `AI__AsyncEmail__Enabled=true`
- `AI__AutomaticExtraction__Mode=ConfidenceFallback`
- `AI__AutomaticExtraction__AnalyzeEverySource=false`
- `AI__AutomaticExtraction__ForceAiForEmail=false`
- `AI__AutomaticExtraction__RequireAiResult=false`
- `AI__AutomaticExtraction__MinimumDeterministicConfidence=75`
- `AI__EmailFallback__MaximumContentCharacters=6000`
- `AI__EmailFallback__MaximumEmailContextCharacters=1000`
- `AI__AsyncEmail__PayloadBaseUrl`: URL interna de la API DataExtraction.
- `EmailIngestion__Enabled=true`
- `Pricing__ImportFromExtractionUrl`: URL interna de Pricing.

Las comunicaciones AI ↔ DataExtraction y DataExtraction → Pricing no requieren
API key, bearer token ni encabezados de autenticación. La migración
`AddAsyncEmailAiWorkflow` debe aplicarse antes de iniciar API y Workers.
