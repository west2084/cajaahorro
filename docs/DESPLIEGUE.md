# Despliegue controlado — Caja de Ahorro

## Situación actual
- Producción está en otra red y se administra mediante RustDesk.
- Hay conectividad saliente a Internet y GitHub.
- Este repositorio **NO** realiza despliegues automáticos ni accede a producción.
- El workflow `.github/workflows/ci-develop.yml` compila un paquete **de desarrollo**, no autorizado para instalarse en producción.

## Procedimiento propuesto (pendiente de implementación y aprobación)
1. Crear `feature/*` desde `develop`, realizar revisión y pruebas contra base de desarrollo.
2. Integrar en `develop`; comprobar CI y pruebas funcionales.
3. Aprobar Pull Request hacia `main`, generar etiqueta de versión y paquete de lanzamiento identificado.
4. Revisar migraciones SQL: script idempotente/versionado cuando proceda, estimación de impacto, respaldo y reversión documentada.
5. Verificar respaldo recuperable, acceso y ventana de mantenimiento. Obtener autorización expresa para producción.
6. Con RustDesk, descargar el paquete verificado (SHA-256). Conservar las configuraciones y secretos propios del servidor.
7. Poner el sitio fuera de servicio de forma controlada, actualizar binarios y aplicar **solo** SQL aprobado.
8. Reiniciar y verificar autenticación, consultas, socios, préstamos, ahorro, retenciones y conciliaciones de saldos.
9. Registrar versión, hora, operador, resultados y eventuales incidentes.
10. En caso de problemas, detener la actualización y seguir el plan de recuperación específico: el rollback de binarios no implica restaurar una base de datos antigua.

## Bloqueadores antes de automatizar producción
- `Program.cs` ejecuta `db.Database.Migrate()` y `SeedAdminAsync()` al arrancar; requieren auditoría previa.
- Revisar configuración de IIS, ruta de publicación, AppPool, permisos, certificados y mecanismo de respaldo.
- No existe todavía comprobación automatizada suficiente de reglas financieras.
- El `.gitignore` no deja de versionar archivos `bin/`, `obj/` o `*.user` ya comprometidos en la historia. Limpiar seguimiento solo después de revisión, en una rama separada.
- Implementar aprobación manual y protecciones de ramas antes de introducir despliegue automático.
