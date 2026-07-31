# Corrección de importación XLSX con matriz de tarifas

## Caso cubierto

Archivos como `MSC DT CALDERA - Validez 08 al 14 de AGOSTO.xlsx` que contienen:

- Carrier y destino en el nombre/títulos del libro.
- Vigencia en texto (`08 al 14 de AGOSTO`).
- Matriz principal `POL | 20 DV | 40 DV/HC`.
- Bloques laterales de adicionales que no representan tarifas completas.
- Filas calculadas mediante fórmulas para puertos adicionales.

## Comportamiento

- Extrae únicamente el primer bloque de tarifas completas.
- Ignora el bloque lateral `POL Additional TAO` como tarifa independiente.
- Recupera `MSC`, `Puerto Caldera`, moneda USD y vigencia del encabezado.
- Crea una fila fuente por POL.
- `40 DV/HC` se separa posteriormente en `40DV` y `40HC` mediante el pipeline existente.
- Mantiene los registros sin POD/agente para revisión en Pricing, sin descartarlos.
- No modifica las validaciones estructurales de Pricing.

Para el archivo de prueba se detectan 65 filas fuente y 195 registros por combinación de contenedor.
