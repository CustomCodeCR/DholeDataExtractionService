# OCR + análisis visual compartido en Dhole DataExtraction

## Alcance

Este flujo se usa en dos entradas: importaciones manuales realizadas desde Pricing/Average
por gRPC y trabajos asíncronos de adjuntos en la ingesta de correo. No se crea un parser
distinto para Average.

1. DataExtraction intenta primero el extractor determinístico existente.
2. En PDF con texto seleccionable escaso, `AiEmailContentReader` solicita OCR por página.
3. En adjuntos PNG, JPG, WEBP, BMP y TIFF, Tesseract extrae texto.
4. Para imágenes pequeñas y la primera página escaneada de un PDF, el sistema
   adjunta una imagen validada a la solicitud de DholeAI (Qwen); las demás páginas
   se presentan como texto OCR.
5. La IA transforma la evidencia en filas estructuradas, que vuelven a pasar por
   normalización de monedas, rutas, equipos y validaciones de DataExtraction/Pricing.
6. Las filas incompletas de competidores permanecen revisables y no deben entrar
   en Average sin validación.

## Binarios de ejecución

Los contenedores `dataextraction-api` y `dataextraction-workers` instalan
`poppler-utils`, `tesseract-ocr`, `tesseract-ocr-spa` y `tesseract-ocr-eng`.
DholeAI no necesita estos binarios; recibe imágenes a través del contrato visual
existente de su proveedor.

## Configuración opcional

Los valores por defecto están codificados para que el despliegue no requiera
cambiar archivos de configuración que contienen secretos.

| Configuración | Predeterminado | Objetivo |
|---|---:|---|
| `AI:DocumentOcr:Enabled` | true | Desactivar OCR local |
| `AI:DocumentOcr:Languages` | spa+eng | Español e inglés |
| `AI:DocumentOcr:MaximumPages` | 24 | Muestreo de páginas iniciales y finales |
| `AI:DocumentOcr:MaximumTotalSeconds` | 150 | Presupuesto total OCR por PDF |
| `AI:DocumentOcr:ProcessTimeoutSeconds` | 40 | Tiempo máximo por proceso |
| `AI:DocumentOcr:MaximumFileBytes` | 26214400 | Tamaño máximo que procesa OCR |
| `AI:DocumentOcr:MaximumVisionImageBytes` | 500000 | Vista JPEG/base64 segura |
| `AI:EmailJobs:MaximumVisionImageBytes` | 500000 | Límite correspondiente del worker AI |

Se pueden configurar usando las variables de entorno estándar de .NET con doble
guion bajo (por ejemplo `AI__DocumentOcr__Enabled=false`).

## Restricciones y seguridad

- No se invoca shell. Los archivos se rasterizan en directorios temporales
  aleatorios y se eliminan al terminar.
- Una imagen adjunta se verifica por firma de bytes antes de enviarla al modelo.
- El worker AI no incluye la imagen base64 en eventos de auditoría.
- Un PDF cifrado o corrupto puede seguir requiriendo intervención manual.
- El reconocimiento visual es aproximado: un OCR que interpreta mal un número,
  ruta o moneda debe identificarse durante la revisión. No se inventan valores.
- Los documentos extensos se limitan por tiempo, páginas y longitud del contexto.
- Un error en una fila no debe invalidar las otras filas correctamente extraídas.

## Validación funcional

Conservar un corpus autorizado de documentos reales de varias navieras y agentes:
PDF digitales, PDF completamente escaneados, híbridos, XLS/XLSX de varias hojas,
imágenes de tarifarios, correos HTML/tablas y adjuntos en diferentes idiomas.

Para cada documento almacenar: número esperado de rutas/equipos/tarifas, origen,
destino, moneda, importe por rubro y vigencia. Medir cobertura de filas,
precisión por campo, diferencias de montos, falsos positivos, tasas de revisión,
duplicados y tiempos de procesamiento. No marcar como listo para publicación
automática un documento sin importes verificables.

Pruebas específicas:

```bash
dotnet test tests/Dhole.DataExtraction.UnitTests/Dhole.DataExtraction.UnitTests.csproj \
  --filter "FullyQualifiedName~DocumentOcrIntegrationTests|FullyQualifiedName~ExcelMultiSectionTariffTests"
```

En DholeAI:

```bash
dotnet test tests/Dhole.AI.UnitTests/Dhole.AI.UnitTests.csproj \
  --filter "FullyQualifiedName~PricingImageExtractionTests"
```

Los pipelines de GitHub Actions ejecutan las pruebas dirigidas antes de construir
y desplegar las nuevas imágenes. Confirmar ambos entornos y revisar logs de
DataExtraction y AI después del despliegue.
