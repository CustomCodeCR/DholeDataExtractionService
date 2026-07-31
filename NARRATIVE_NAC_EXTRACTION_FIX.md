# Extracción determinística de correos NAC narrativos

Se agregó soporte para correos que expresan la tarifa en una oración, por ejemplo:

- `USD6300/6400`
- `Carrier MSC/ONE NAC`
- `valid 8-14/Aug`
- `21 days free at dest`
- bloques `POL`, `POD` y `COMM`

El extractor usa únicamente la oferta más reciente, asigna los importes por orden a las navieras, interpreta el POD marítimo como POE, conserva restricciones de mercancía y recargos, elimina POL excluidos y no importa las ofertas históricas citadas en el hilo.

Cuando el cuerpo produce filas determinísticas sin bloqueos estructurales, el worker no llama a AI aunque queden catálogos pendientes de asignación. Esos valores pasan a revisión manual.
