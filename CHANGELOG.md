# Changelog

## [1.4.5] - 2026-11-08 (F9 review candidate)

- PATCH SemVer global 1.13.14 para excepción F3 en revisión de F9. Mantiene gates formales, flags OFF y prohibición de despliegue hasta verificar staging y durabilidad.


## [1.4.4] - 2026-11-08 (candidato F3)

- Sincronización PATCH de la versión global 1.13.13 correspondiente al ensayo WSS/Traefik de DholeApiGateway; sin nuevas modificaciones funcionales ni despliegue.


## [1.4.3] - 2026-11-08 (F6/F9 staging-audit candidate)

- Sincroniza Dhole Realtime `rt-20261107-01` a versión global `1.13.12` tras incorporar auditoría real de persistencia en staging. PATCH 1.4.2 → 1.4.3; no modifica rutas, eventos, datos, contratos REST ni flags. No se fusiona hasta completar gates formales.


## [1.4.2] - 2026-11-08 (candidato F9, sin publicar)

- Sincroniza release global `rt-20261107-01` a `1.13.11` tras resolver los hallazgos de CI de Config, Reports y Gateway. Bump PATCH de DholeDataExtractionService `1.4.1` a `1.4.2`, sin modificar REST, eventos, precios ni datos; flags realtime OFF y PR F9 aún DRAFT.


## [1.4.1] - 2026-11-08 (candidato F9, sin publicación)

- Sincroniza metadatos de versión global del batch Dhole Realtime `rt-20261107-01` a `1.13.10`; actualización PATCH independiente de DholeDataExtractionService `1.4.0` → `1.4.1`. No cambia contratos REST, datos, lógica de negocio ni flags; requiere gates F0–F8 y CI de SHA final antes de merge.


- F6.08 (candidato): GET autorizados realtime de execution, emailjob y airequest ahora devuelven snapshots mínimos para reconstrucción REST tras reconexión, sin contenido de correos, documentos ni prompts.

## [1.4.0] - 2026-11-08 (candidato F6.08, sin publicar)

- F6.08: productores realtime transaccionales para extracciones de tarifas y trabajos de correo/IA, reutilizando Outbox existente del servicio y un Redis Stream independiente.
- Los mensajes incluyen únicamente identificador del recurso, tipo de cambio, versión-hint y trazabilidad mínima, nunca correos, documentos, prompts, respuestas, URLs privadas ni credenciales.
- El consumidor debe reconciliar mediante REST autorizado, especialmente al comparar eventos originados por réplicas distintas. Cuatro flags de batch OFF por defecto.
- Componente 1.3.5 → 1.4.0; ecosistema propuesto 1.13.0. No se ha promovido develop/master ni desplegado.

## [1.3.5] - 2026-11-06

- Expone sourceReceivedAtUtc desde EmailMessages.ReceivedAt en la conciliación masiva de IA, incluso si el correo está marcado como eliminado.
- Permite cancelar correctamente trabajos de correos de octubre creados en noviembre, preservando evidencia y trazabilidad.
- Ecosistema Dhole 1.3.12, release DH-20261106-OCTOBER-EMAIL-AI-QUEUE-014.

## [1.3.4] - 2026-11-06

- API de conciliación masiva que identifica solicitudes de correos eliminados, archivados o finalizados sin transferir contenido sensible a AI.
- Unifica el cierre histórico entre worker y API incluso si la opción no está declarada en la configuración de la API; mantiene la posibilidad de desactivación explícita.
- Impide entregar a Ollama contenido de mensajes cuya fuente fue eliminada.
- Ecosistema Dhole 1.3.10, release DH-20261106-AI-SOURCE-RECONCILE-012.


## [1.3.3] - 2026-11-06

- Archiva correos y extracciones anteriores al mes actual en horario Costa Rica, usando ReceivedAt y no la fecha del reintento.
- Mantenimiento gradual por lotes de 250; previene nuevas importaciones, reprocesos y respuestas AI de esos correos.
- Conserva adjuntos, historia, resultados ya enviados y solicitudes activas de Pricing.
- La API interna devuelve HTTP 410 para solicitudes de correos antiguos; AI las finaliza sin consumir Ollama.
- Ecosistema Dhole 1.3.9, release DH-20261106-HISTORICAL-CLOSE-011.


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
