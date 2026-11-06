# Changelog

## [1.3.2] - 2026-11-06

- Aplica backpressure a solicitudes AI de correos cuando existen 12 análisis activos, sin consumir intentos y sin perder los originales.
- Limita a tres extracciones automáticas reales por correo; evita reencolados indefinidos y amplía el backoff.
- Prioriza Microsoft MarkItDown en archivos Office/MIME y en cuerpos HTML; mantiene lectores nativos para PDF/Excel y fallback seguro.
- Reduce el lote por ciclo a 24 sin bloquear la extracción determinística y mantiene integración con Pricing.
- Ecosistema Dhole 1.3.4, release DH-20261106-EMAIL-QUEUE-001.


## [1.3.1] - 2026-11-05

- Elimina condiciones que omitían el deploy por push master y el salto de commits de producción.
- Conserva staging automático y amplía el límite de ejecución de producción.
- Incrementa versión SemVer, assembly y metadatos del ecosistema Dhole.

## [1.3.0] - 2026-11-05

- Extracción de hasta 100 trabajos por lote con concurrencia acotada.
- Recuperación de entregas AI huérfanas sin reiniciar solicitudes activas.
- Reconciliación de solicitudes AI en una consulta por lote para evitar N+1.
- Métricas de antigüedad de cola y de envíos horarios a Pricing.
- Pruebas de presupuesto y reparto de lotes.

## [1.2.0] - 2026-11-05

- Incorporates universal Dhole SemVer policy with GitHub Actions checks.
- Synchronizes service assembly metadata with the ecosystem version.
- Adds permanent Codex instructions and coordinated release requirements.
