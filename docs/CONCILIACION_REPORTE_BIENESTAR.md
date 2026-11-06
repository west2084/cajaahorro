# Conciliación y reportes de precarga IMSS Bienestar

## Alcance
- **Solo lectura:** se consulta cada línea de `AhorroBienestarCargaLineas` y se contrasta su RFC con `Socios` y la existencia de ahorro en `Ahorros` por socio/quincena/año/ciclo.
- Una coincidencia de ahorro **NO se modifica ni se considera duplicado automáticamente** porque `Ahorros` todavía no distingue institución ni contrato.
- Clasificación: `Rechazado en precarga` (incidencia original), `Otro concepto` (AA39/AAFM), `Socio no encontrado`, `Ahorro existente` y `Sin coincidencia contable`.
- Los renglones que pasan validación son **movimientos de precarga**, no aportaciones aplicadas.
- Exportación xlsx desde **Reportes → IMSS Bienestar — precargas**, tres hojas: `Resumen`, `Detalle completo` (incluye observaciones de conciliación) y `Rechazados` (fila Excel, RFC, concepto, importe y motivo).
- **Control de acceso:** pantalla solo para rol `Administrador` en `Development`; endpoint `api/reportes/bienestar-precarga/{id}` requiere autenticación y rol `Administrador`, con `Cache-Control: no-store`. Exportación incluye información personal: restringir distribución y conservar conforme a las políticas institucionales.
- No requiere nuevas migraciones ni scripts SQL. No modifica carga tradicional SSZ. No aplica importes ni actualiza saldos.

## Comprobación en ambiente de desarrollo
1. Abrir `/reportes` como administrador y seleccionar la precarga #1.
2. Descargar Excel y confirmar conteos **1438 válidos de ahorro, 152 otros y 3 incidencias**, importe de ahorro **858860.00**. Verificar conciliación con el catálogo real (resultado dependerá de los socios y ahorros existentes en la base).
3. Confirmar que `Rechazados` contiene exactamente los 3 renglones con número de fila/motivo. El reporte contiene toda la información de `Detalle completo`.
4. Confirmar que un usuario no administrador recibe 403 en el endpoint. Confirmar que `Reportes` normal de SSZ y ahorradores sigue funcionando.
5. Confirmar que el reporte no modifica `Ahorros` ni `Socios`, y que `AhorroBienestarCargas.Estado` sigue en `Precarga`.
6. Validar archivos nuevos con dos contratos/plazas del mismo RFC; una coincidencia contable debe señalar revisión, nunca sustituir o sumar automáticamente.

## Fase futura (sin implementar)
Cuando exista aplicación contable transaccional con fuente/contrato y reversión, registrar por línea el resultado **Aplicado / Rechazado** en una bitácora permanente. Hoy el reporte no debe llamar «cargado» a algo que solo es precarga, ni confundir `AhorroValidado` con saldo aplicado.
