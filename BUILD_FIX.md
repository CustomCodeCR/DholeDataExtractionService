# Corrección de build

- La prueba `PrepareAiRequest_RejectsImageAttachments` ya no depende de `Assert.ThrowsExceptionAsync` ni `Assert.ThrowsExactlyAsync`; usa `try/catch` compatible con MSTest 4.
- Todos los paquetes `Microsoft.EntityFrameworkCore*` usados directamente quedaron en `10.0.8`, la versión que ya utiliza `CustomCodeFramework 0.2.0`.
- `verify-build-fix.sh` comprueba que el parche realmente quedó aplicado antes de compilar.
