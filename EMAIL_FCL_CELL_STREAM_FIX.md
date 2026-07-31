# Corrección de tablas FCL aplanadas en correos

## Problema

Algunos correos de Outlook convierten una tabla HTML en una secuencia de una celda por línea:

- POL
- POD
- CARRIER
- Free Time
- Validity (ETD)
- 20'GP
- 40'GP
- 40'HQ

Después aparecen ocho líneas por cada tarifa. El extractor anterior solo reconocía tablas delimitadas por tabulaciones, barras o espacios alineados, por lo que no producía filas.

## Comportamiento nuevo

- Detecta la primera matriz FCL válida aplanada en el cuerpo.
- Interpreta `POD` de esta matriz como `Port of Discharge`, almacenado en Dhole como `POE`.
- Se detiene al encontrar el pie de la tabla y no importa las tablas antiguas de la cadena reenviada.
- Divide la vigencia, por ejemplo `8 Aug-14 Aug`, en `ValidFrom=8 Aug` y `ValidTo=14 Aug`.
- Reconoce columnas 20GP, 40GP, 40HQ y otras variantes conocidas.
- Normaliza abreviaturas de puertos: SHA, NGB, SZN, XMN, TAO, TSN y DLN.
- Corrige `Acajulta` a `Acajutla`.
- Normaliza `MSC FAK`, `MSC Basket` y `ONE FAK` contra la naviera real, conservando el producto comercial en observaciones y en el JSON original.
- No llama a AI cuando la clasificación es igual o superior al umbral y la extracción produce filas utilizables.

Para el correo del 31 de julio, el extractor obtiene cuatro filas fuente. Después de expandir POL, POE y los tres tipos de contenedor, produce 180 registros granulares, sin incluir las tarifas históricas citadas.
