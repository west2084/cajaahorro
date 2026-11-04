# Caja de Ahorro — instrucciones para agentes y colaboradores

Sistema ASP.NET Core / Blazor (.NET 8) con SQL Server, actualmente operativo en producción.

## Seguridad y flujo
- `main` corresponde a versiones aprobadas para producción; jamás desarrollar directamente ahí.
- `develop` integra y prueba cambios; cada modificación va primero en `feature/*` o `fix/*`, con revisión por Pull Request.
- Flujo: análisis → desarrollo → revisión → pruebas → aprobación → producción → verificación.
- Antes de editar, revisar archivos relacionados, dependencias y riesgos sobre socios, préstamos, amortizaciones, ahorros, retenciones, saldos y usuarios.
- Cambios mínimos. No eliminar código, tablas, columnas, índices o funcionalidad sin estudiar dependencias y recibir autorización.
- Migraciones y cambios SQL deben estar versionados y revisados; exigir respaldo verificado y plan de reversión. No aplicar migraciones automáticamente a producción.
- No conectar CI a la base de producción ni ejecutar operaciones destructivas sobre datos reales.
- No incluir contraseñas, credenciales, cadenas de conexión sensibles ni respaldos en GitHub, logs o artefactos.
- Compilar no equivale a ejecutar pruebas; no afirmar que una versión funciona sin evidencia.
- Nunca desplegar en producción ni ejecutar scripts remotos sin aprobación explícita del responsable.
- Antes de publicar: versión, resumen, archivos modificados, pruebas, riesgos, evidencia del respaldo y procedimiento de recuperación.
- Producción se administra desde otra red con RustDesk; cualquier automatización futura debe exigir aprobación humana.

## Precauciones de implementación
- Actualmente `Program.cs` llama a `db.Database.Migrate()` durante el arranque: es un riesgo conocido. No ejecutar el sitio publicado contra producción hasta reemplazar ese comportamiento por un procedimiento de migración autorizado y probado.
- Mantener la configuración sensible fuera del paquete; la publicación no debe sobreescribir `appsettings*.json` en producción.
- La reversión de binarios no revierte automáticamente SQL ni los movimientos financieros posteriores.
