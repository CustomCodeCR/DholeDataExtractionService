# Changelog — DholeDataExtractionService

## [1.2.0] — 2026-11-05

### Added
- Bounded batch processing of email tariff extractions with concurrent worker slots.

### Changed
- DataExtraction distributes up to 100 extraction jobs per cycle.
- Active AI requests are reconciled in one database query to reduce backpressure and duplicate request risk.

### Monitoring and recovery
- Recuperación de handoffs AI obsoletos sin solicitud AI activa; no se invalida trabajo legítimo en ejecución.
- Reconciliación de hasta 250 solicitudes AI mediante consulta de lote en vez de N+1 consultas.
- Métricas de trabajos antiguos, antigüedad en cola y envíos a Pricing durante la última hora.
- Pruebas de distribución exacta de trabajos por batch y límite de concurrencia.

### Safeguards
- Individual email-job state, PostgreSQL claim leases and Redis outbox/inbox idempotency are retained.
- No automated production deployment is authorized by this change.
