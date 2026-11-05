# Changelog — DholeDataExtractionService

## [1.2.0] — 2026-11-05

### Added
- Bounded batch processing of email tariff extractions with concurrent worker slots.

### Changed
- DataExtraction distributes up to 100 extraction jobs per cycle.
- Active AI requests are reconciled in one database query to reduce backpressure and duplicate request risk.

### Safeguards
- Individual email-job state, PostgreSQL claim leases and Redis outbox/inbox idempotency are retained.
- No automated production deployment is authorized by this change.
