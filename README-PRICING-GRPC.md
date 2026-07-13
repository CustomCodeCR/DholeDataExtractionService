# Estandarización de Pricing por gRPC

## Cambios incluidos

- Cliente gRPC real para `DholeConfigService`.
- Resolución de `pricing-imports-profiles` antes del mapeo de columnas.
- Precarga de `carriers`, `pol`, `poe`, `pod`, `currencies`, `agents` y `container-types`.
- Soporte de aliases mediante `MetadataJson`, por ejemplo `{"aliases":["40HQ"]}`.
- Referencias estandarizadas con ID, código, slug y nombre en cada fila.
- Incidencias bloqueantes cuando Config no reconoce un valor.
- Persistencia del archivo fuente en la ruta configurada.
- Campos monetarios opcionales en Protobuf para diferenciar `0` de un dato ausente.

## Configuración

Desarrollo:

```json
{
  "Grpc": {
    "Clients": {
      "Config": {
        "Address": "http://localhost:5102"
      }
    }
  }
}
```

Docker debe sobrescribir la dirección, por ejemplo:

```text
Grpc__Clients__Config__Address=http://config-api:8081
```

## Migración requerida

Las referencias de catálogo son owned types de `PricingExtractionRecord`. Genere la migración antes de ejecutar el servicio:

```bash
dotnet ef migrations add AddPricingCatalogReferences \
  --project src/Dhole.DataExtraction.Persistence \
  --startup-project src/Dhole.DataExtraction.Api \
  --context ServiceDbContext \
  --output-dir Migrations

dotnet ef database update \
  --project src/Dhole.DataExtraction.Persistence \
  --startup-project src/Dhole.DataExtraction.Api \
  --context ServiceDbContext
```

## Datos requeridos en Config

Config crea automáticamente los ocho grupos. También crea monedas, contenedores y el perfil `default`. Antes de importar debe cargar las navieras, agentes y puertos reales.

## Ingesta automática desde correo

El worker sincroniza la cuenta definida en `EmailIngestion:SeedAccounts`, procesa los
adjuntos/cuerpos compatibles y envía las filas normalizadas a Pricing cuando la
confianza alcanza el umbral configurado.

Variables requeridas para desarrollo local:

```bash
dotnet user-secrets --project src/Dhole.DataExtraction.Workers \
  set DATA_EXTRACTION_EMAIL_PASSWORD "app-password-de-gmail"

export Pricing__ImportFromExtractionUrl="http://localhost:5206/api/pricing/rate-import-batches/from-extraction"
```

`EmailIngestion:SeedAccounts:0:secretReference` debe conservar el valor
`DATA_EXTRACTION_EMAIL_PASSWORD`. Ese campo guarda únicamente el nombre de la
clave; nunca coloque allí la contraseña de aplicación. API y Worker comparten el
mismo `UserSecretsId`, por lo que basta configurarla una vez en desarrollo. En
contenedores o servicios configure `DATA_EXTRACTION_EMAIL_PASSWORD` como variable
de entorno del proceso que ejecuta `Dhole.DataExtraction.Workers`.

En desarrollo local el receptor acepta llamadas identificadas de Data Extraction. En
otros ambientes configure la misma llave interna en ambos procesos:

```bash
# Dhole.DataExtraction.Workers
export Pricing__ApiKey="api-key-interna"

# Dhole.Pricing.Api
export Pricing__DataExtractionApiKey="api-key-interna"
```

En Docker use el DNS del contenedor del API de Pricing en lugar de `localhost`.
El receptor incluido usa `POST /api/pricing/rate-import-batches/from-extraction` y
devuelve `pricingImportBatchId` dentro de `data`.
