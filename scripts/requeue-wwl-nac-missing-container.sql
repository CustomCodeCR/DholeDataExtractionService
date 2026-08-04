-- Reprocesa desde DataExtraction los correos WWL NAC que fueron rechazados por
-- missing_container_type. Se reprocesan desde el correo original para recuperar
-- toda la matriz MSC + ONE; reencolar únicamente Pricing conservaría solo las
-- filas que alcanzó a producir la respuesta AI anterior.

BEGIN;

-- Cierra cualquier solicitud AI anterior asociada para permitir una nueva ejecución.
UPDATE data_extraction."EmailAiAnalysisRequests" AS request
SET completed_at_utc = COALESCE(request.completed_at_utc, NOW()),
    updated_at_utc = NOW()
FROM data_extraction."EmailExtractionJobs" AS job
JOIN data_extraction."EmailMessages" AS message
  ON message.id = job.email_message_id
 AND message.is_deleted = FALSE
WHERE request.id = job.ai_request_id
  AND job.is_deleted = FALSE
  AND message.subject ILIKE '%WWL CONTRACT ONE-MSC%'
  AND (
        COALESCE(job.error_message, '') ILIKE '%missing_container_type%'
        OR COALESCE(job.error_message, '') ILIKE '%Ninguna fila pudo guardarse%'
        OR job.last_error_code = 'Pricing.NoUsableExtractionRows'
      );

-- Devuelve el trabajo al inicio del flujo. La nueva extracción determinística
-- reconocerá POD como POE y usará 40HC para este contrato NAC narrativo.
UPDATE data_extraction."EmailExtractionJobs" AS job
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
    version = job.version + 1,
    updated_at_utc = NOW()
FROM data_extraction."EmailMessages" AS message
WHERE message.id = job.email_message_id
  AND message.is_deleted = FALSE
  AND job.is_deleted = FALSE
  AND message.subject ILIKE '%WWL CONTRACT ONE-MSC%'
  AND (
        COALESCE(job.error_message, '') ILIKE '%missing_container_type%'
        OR COALESCE(job.error_message, '') ILIKE '%Ninguna fila pudo guardarse%'
        OR job.last_error_code = 'Pricing.NoUsableExtractionRows'
      );

COMMIT;
