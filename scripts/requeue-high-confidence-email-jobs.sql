-- Reencola trabajos que ya fueron enviados a AI aunque el correo tenía
-- una clasificación de 75% o superior. Ejecute una sola vez después de
-- desplegar la corrección y antes de reiniciar los Workers.
BEGIN;

UPDATE data_extraction."EmailAiAnalysisRequests" request
SET completed_at_utc = COALESCE(request.completed_at_utc, NOW()),
    updated_at_utc = NOW()
FROM data_extraction."EmailExtractionJobs" job
JOIN data_extraction."EmailMessages" message
  ON message.id = job.email_message_id
WHERE request.id = job.ai_request_id
  AND request.completed_at_utc IS NULL
  AND job.is_deleted = FALSE
  AND message.is_deleted = FALSE
  AND COALESCE(message.classification_confidence, 0) >= 75
  AND job.status IN ('AwaitingAi', 'AiProcessing');

UPDATE data_extraction."EmailExtractionJobs" job
SET status = 'Pending',
    extraction_execution_id = NULL,
    pricing_import_batch_id = NULL,
    ai_request_id = NULL,
    ai_execution_id = NULL,
    ai_request_hash = NULL,
    pricing_request_id = NULL,
    confidence_score = NULL,
    error_message = NULL,
    last_error_code = NULL,
    attempt_count = 0,
    next_attempt_at_utc = NOW(),
    lease_owner = NULL,
    lease_expires_at_utc = NULL,
    last_heartbeat_at_utc = NULL,
    started_at = NULL,
    finished_at = NULL,
    version = version + 1,
    updated_at_utc = NOW()
FROM data_extraction."EmailMessages" message
WHERE message.id = job.email_message_id
  AND job.is_deleted = FALSE
  AND message.is_deleted = FALSE
  AND COALESCE(message.classification_confidence, 0) >= 75
  AND job.status IN ('AwaitingAi', 'AiProcessing');

COMMIT;
