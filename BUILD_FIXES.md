# Correcciones de compilación

- Actualizado el override de `System.Security.Cryptography.Xml` a `10.0.10`.
- Las librerías compartidas no generan `.deps.json`.
- Incluido `run-dev.sh` para compilar de forma serial y ejecutar API/Workers con `--no-build`.

Ejecutar:

```bash
chmod +x run-dev.sh
./run-dev.sh
```
