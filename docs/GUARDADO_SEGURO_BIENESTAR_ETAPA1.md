# Guardado seguro de precargas IMSS Bienestar — etapa 1

**Estado:** implementación aislada en desarrollo. No habilita el registro contable de aportaciones.

## Alcance
- Nuevo servicio `AhorroBienestarGuardadoService.GuardarPrecargaAsync` (solo `Development`): lee la primera hoja con encabezados IMSS Bienestar; guarda cada línea con RFC, nombre, CURP, plaza, concepto, quincena, fecha de pago, importe, estatus y motivo de incidencia.
- Transacción SQL única para encabezado y líneas. El archivo idéntico se bloquea por SHA-256 único.
- Los importes por ahorro (solo AAFA y AAFB) se suman **exclusivamente en la precarga**. AA39 y AAFM se conservan como otros conceptos.
- No elimina filas duplicadas de RFC: una persona puede tener dos contratos.
- Filas con estatus distinto de LIQUIDADO quedan como incidencia. El total impreso en la hoja no se utiliza.
- No se escribe en `Ahorros`, `Socios`, `Prestamos`, `AhorroEncabezados` ni en tablas contables. Tampoco habilita botones de aplicación o rollback.
- Esta etapa NO modifica la precarga UI existente: el servicio todavía no está invocado desde la pantalla.

## Preparación controlada de desarrollo
1. Crear respaldo verificable de la BD **de desarrollo** y comprobar que se puede restaurar.
2. Revisar antes de ejecutar `db/scripts/20261105_01_precarga_bienestar.sql`. Es aditivo; crea dos tablas sin alterar las existentes. Ejecutar manualmente solo en desarrollo, con usuario SQL autorizado.
3. Compilar y ejecutar la aplicación en entorno `Development` apuntando **únicamente** a la BD de desarrollo. El servicio rechazará llamadas si el entorno no es `Development`.
4. No ejecutar el script en producción. `Program.cs` continúa utilizando `Database.Migrate()` del código previo; esta etapa no incluye una migración EF automática. Antes de cualquier despliegue futuro debemos eliminar/restringir ese comportamiento y reconciliar este script versionado con el modelo/migraciones EF.
5. No eliminar las tablas como mecanismo automático de rollback. Para desactivar la fase basta con retirar o deshabilitar la función y conservar el historial; cualquier operación de reversión de esquema requiere revisión de dependencias, respaldo y autorización expresa.

## Pruebas obligatorias pendientes
- Archivo Bienestar real: contrastar cada línea guardada con el Excel, incluyendo filas con estatus CHEQUE/CANCELADO, importes, plazas y quincenas.
- Reintento con archivo de igual SHA-256: segundo guardado debe fallar sin movimientos parciales.
- Archivo inválido: no crea precarga.
- Falla SQL en mitad de operación: no deja líneas huérfanas.
- Dos contratos del mismo RFC: mantiene dos líneas y no duplica ni sobrescribe ninguna.
- Comprobar que sumas de `Ahorros`, finiquitos y todos los saldos contables **no cambian**.
- No se han ejecutado estas pruebas contra SQL Server al crear el PR.

## Etapas posteriores (no incluidas)
- Habilitar acción de guardar precarga en UI solamente para administradores, en desarrollo, con confirmación explícita y auditoría de usuario.
- Diseñar referencia de institución y contrato, snapshot por movimiento y migración de `Ahorros` que preserve las aportaciones SSZ y Bienestar sin sobrescritura.
- Integrar histórico por archivo, detalle de rechazados, aplicación contable transaccional y reversión por lote con bloqueos de dependencias.
- Probar en desarrollo y obtener autorización antes de publicar en producción.
