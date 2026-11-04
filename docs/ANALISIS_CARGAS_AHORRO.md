# Propuesta técnica — auditoría de cargas de ahorro

**Estado:** análisis y diseño, sin cambios a datos, tablas ni producción.  
**Rama:** `feature/auditoria-cargas-ahorro` (desde `develop`).

## Necesidades del negocio
1. Seleccionar una carga por archivo y revertir únicamente sus efectos, con vista previa, autorización y constancia.
2. Consultar por fecha/hora de carga: importes, recuentos, aplicados, ignorados/rechazados y motivos por fila.
3. Separar la información importada de **IMSS Bienestar (Federal)** y **Secretaría de Salud de Zacatecas (SSZ, Estatal)**.

## Hallazgos verificados
- `AhorroEncabezado` tiene clave única `(Quincena, Anio, Ciclo)`, **sin institución**.
- `Ahorro` tiene clave única `(SocioId, Quincena, Anio, Ciclo)`, **sin institución**.
- `AhorroImportService.ImportarAsync` hace upsert (sobrescribe montos y el encabezado) en esa combinación, crea socios provisionales y completa CURP faltantes.
- La importación ejecuta varios `SaveChangesAsync` sin transacción explícita que abarque todo; una falla posterior puede dejar cambios parciales.
- `ResultadoImportacion` mantiene `Log` y `Advertencias` en memoria; no hay persistencia de filas rechazadas en el historial de cargas.
- `SocioInstitucion` representa un historial de adscripción del socio; **no equivale** a la institución de origen de una línea o archivo y puede cambiar después.
- `Ahorro.PagoQuincenalId` y `TotalAhorradoAsync` implican dependencias financieras que deben validarse antes de revertir.
- El arranque ejecuta `Database.Migrate()`: cualquier migración destinada al desarrollo necesita un plan para impedir aplicación no aprobada en producción.

## Diseño propuesto (requiere revisión del esquema y datos reales)
### Nuevas entidades (aditivas, con migración SQL versionada)
- `AhorroCarga`: Id de operación, nombre original del archivo, SHA-256, fecha/hora UTC, usuario, institución de origen (SSZ/IMSS Bienestar), formato, ciclo, quincena, año, totales contabilizados, número de aplicados, nuevos, actualizados, rechazados, estado (Aplicada/Revertida/ConIncidencia), motivo de reversión, usuario y fecha de reversión.
- `AhorroCargaLinea`: carga Id, fila original, RFC normalizado, nombre, CURP cuando exista, importe original, estatus (Aplicado/Rechazado/Ignorado), motivo exacto; identificador del ahorro afectado y valores anteriores necesarios para revertir un *update* de forma determinista.
- `AhorroCambio` (o bitácora equivalente): snapshot de antes/después de cada cambio: registro existente/nuevo, monto, status, encabezado, y ajustes a socio (CURP/provisional) con referencia a la carga; verificación de concurrencia.
- Mantener los encabezados históricos sin borrar ni reinterpretar su atribución, hasta tener evidencia para migrarlos.

### Política de unicidad y coexistencia
Antes de modificar índices de `Ahorro` o `AhorroEncabezado`, acordar la semántica financiera de un RFC que aparece en ambos orígenes dentro de la misma quincena y ciclo:
- **Opción A:** una sola aportación consolidada por socio/quincena/ciclo, con múltiples líneas de origen y una regla documentada de agregación.
- **Opción B:** aportaciones independientes por institución, con revisión de todas las consultas que suman ahorros, finiquitos y tableros.
**No elegir sin contrastar datos históricos y reglas del negocio**. Nunca eliminar índices únicos existentes hasta completar ese análisis.

### Flujo de importación
1. Preanalizar archivo: hash, formato detectado (por encabezados, no por adscripción actual), ciclo y quincena; vista previa y posibles conflictos.
2. Validar formato, importes, RFC y duplicados con motivos por fila; distinguir total leído, válido, aplicado y rechazado.
3. Comprobar que no hay conflicto de cargas simultáneas y resolver política de reimportación por institución.
4. Iniciar una transacción SQL; registrar operación y detalles; aplicar movimientos y completar datos de socio de manera controlada.
5. Si ocurre un error, rollback completo de la transacción; dejar evidencia fuera de la operación si es necesario registrar fallos.
6. Mostrar resumen por carga y conciliación `importe_archivo = aplicado + rechazado + ignorado`, con reglas explícitas para filas de totales.

### Reversión (restricción fuerte)
- Solo administradores autenticados; se requiere motivo obligatorio y confirmación de ID/archivo.
- Primero una simulación de impactos: número de líneas, importe, socios tocados, registros relacionados y cambios posteriores.
- **Bloquear** si un ahorro tiene `PagoQuincenalId`, cambios posteriores, dependencias financieras, conflictos de concurrencia o si no existe snapshot auditable de esa carga.
- Revertir con transacción y control de concurrencia: para inserciones propias, quitar solo los movimientos que no tengan dependencias; para actualizaciones, restaurar el valor anterior verificado; no borrar un socio provisional si ya tiene referencias o modificaciones independientes.
- Conservar la bitácora y marcar la operación como **Revertida**, sin borrar su historial.
- Cargas históricas previas a esta funcionalidad: **solo consulta**, no reversión automática porque carecen de snapshots.

### Reporte e interfaz
- Menú `Ahorro > Historial de cargas`, filtro por fecha (intervalo), institución, quincena, año, ciclo y estado.
- Tarjetas: líneas leídas, aplicadas, rechazadas, importe aplicado, importe rechazado, totales SSZ/Bienestar.
- Detalle descargable CSV/Excel con fila, RFC, motivo de no aplicación, fuente y monto.
- Acción `Vista previa de reversión` y, cuando sea seguro y autorizado, `Revertir carga`; nunca eliminar sin rastro.
- No asumir que el dato de institución histórica puede inferirse de `Socio.InstitucionActual`.

## Plan de migración seguro
1. Respaldo completo y prueba de restauración de una copia **de desarrollo**.
2. Migración aditiva versionada; aplicar solo en desarrollo, verificar integridad e índices.
3. Mantener importaciones antiguas consultables; no recalcular ni reinterpretar montos sin conciliación.
4. Probar A/B en importaciones de ambas fuentes, reimportaciones, RFC repetidos, fallos SQL y reversión bloqueada.
5. Revisar consultas de saldos/finiquitos y cotejar totales antes/después.
6. Documento de instalación/rollback; ningún despliegue a producción sin autorización expresa.

## Criterios de aceptación
- Dos archivos de origen distinto para una misma quincena/ciclo no se sobrescriben ni duplican importes inadvertidamente.
- Fecha/hora y usuario identifican inequívocamente cada carga; el reporte permite saber qué filas no se aplicaron y por qué.
- Los rechazos quedan persistidos y disponibles después de reiniciar la aplicación.
- Una reversión aprobada preserva saldos ajenos, deja evidencia y puede abortarse sin cambios parciales.
- Una reversión peligrosa queda bloqueada con explicación clara.
- El saldo total y las aportaciones del socio se concilian antes y después de cada operación.
- Los procesos de compilación, pruebas y migraciones se realizan únicamente en desarrollo.

## Decisión pendiente
Validar con datos sintéticos y revisión del modelo qué ocurre si un mismo RFC aporta en el mismo período/ciclo desde SSZ e IMSS Bienestar. La semántica debe quedar fijada antes de desarrollar los cambios en claves únicas y suma de saldos.
