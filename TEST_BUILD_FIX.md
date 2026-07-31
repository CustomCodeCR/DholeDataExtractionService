# Corrección del build de pruebas

- Se reemplazó `Assert.ThrowsExceptionAsync` por `Assert.ThrowsExactlyAsync`, compatible con MSTest 4.0.2 y con la semántica exacta de la prueba original.
- Se actualizaron tres aserciones obsoletas sugeridas por los analizadores de MSTest.
- Se fijaron `Microsoft.EntityFrameworkCore` y `Microsoft.EntityFrameworkCore.Relational` 10.0.10 como dependencias primarias de IntegrationTests para evitar la resolución mixta 10.0.8/10.0.10.
