# Corrección de sincronización IMAP

Se corrigió el error `Resource temporarily unavailable` del polling de correo.

Cambios principales:

- El worker ahora respeta `EmailIngestionAccount.PollingIntervalMinutes`.
- Una cuenta no puede sincronizarse de forma solapada dentro del mismo proceso.
- Los errores temporales de socket, red y timeout se reintentan con backoff exponencial y jitter.
- Después de un fallo, la cuenta se vuelve a intentar según `FailureRetryIntervalMinutes`.
- Un cierre de conexión durante `LOGOUT` o `Dispose` ya no convierte una descarga correcta en fallo.
- Los errores de socket se muestran con un mensaje entendible en lugar del texto nativo `Resource temporarily unavailable`.

Configuración predeterminada:

```json
"EmailIngestion": {
  "Imap": {
    "TransientRetryCount": 3,
    "TransientRetryBaseDelaySeconds": 5,
    "TransientRetryMaxDelaySeconds": 30,
    "FailureRetryIntervalMinutes": 2
  }
}
```

No requiere migración de base de datos.
