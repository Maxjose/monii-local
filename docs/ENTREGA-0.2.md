# Entrega Monii 0.2 · operaciones locales

Fecha: 1 de octubre de 2026. Alcance autorizado completado: inventario, ventas, caja, compras/proveedores, clientes/créditos, reportes y respaldos. Sin publicación ni despliegue.

## Operativo

| Bloque | Funciones |
|---|---|
| Inventario | Stock, ajustes con motivo, movimientos, cantidades por unidad/peso y stock mínimo |
| Ventas | Catálogo real, carrito, tres pagos combinados, descuento USD, cambio USD, crédito, persistencia, historial, comprobante TXT y anulación completa |
| Caja | Fondos USD/VES/COP, apertura, ingresos/gastos, medios de pago, efectivo esperado, cierres con diferencias |
| Compras | Proveedores, varias líneas, recepción, último costo, pago completo, historial y anulación |
| Clientes/créditos | Contactos, límites, deuda USD, vencimiento, abonos multimoneda/anulación, comprobantes y estado de cuenta |
| Reportes | Ventas por rango, margen histórico estimado, total de compras, stock/deudas actuales y CSV |
| Respaldos | Manual, automático diario al abrir (14 copias), destino, validación/restauración y copia previa |

Catálogo anterior conservado mediante migración v1→v2 con copia previa. Productos desactivados conservan historial. No se permite desactivar caja abierta o créditos con deuda.

## Verificación

- Release sin errores ni advertencias.
- **75 comprobaciones de integración:** 20 anteriores y 55 operativas, gates en orden inventario→ventas→caja→compras→créditos→respaldos.
- Rollback de varias líneas, stock insuficiente, falta de apertura, descuentos inválidos, snapshots de tasas/costos, reversión de pagos, límites/sobreabonos, compras inconsistentes/ya consumidas, restauración v1 y backups corruptos/incompatibles/con saldos inválidos.
- Dos ventas concurrentes sobre última unidad: solo una confirma. Bs BCV/manual comparten efectivo físico y se revierten con importes originales.
- **24 comprobaciones WPF:** 13 anteriores y 11 operativas. Formularios reales de entrada, apertura, venta, compra, cliente, crédito, abono y cierre; efectivo conciliado. Reportes y navegación de respaldo.
- Imágenes renderizadas por WPF inspeccionadas en `artifacts/phase2-review`; corregidos contraste, columnas, fechas locales, importes, booleanos y pestañas.
- Verificación del portable final con runtime incluido y base aislada. Diálogos nativos de selección de archivos/periféricos no automatizados; backup/restauración probado por integración SQLite.
- Equipo actual Windows build 26200 x64. Windows 10 y distintas escalas/impresoras requieren prueba.

## Revisar

Cierra versión anterior y abre `artifacts/Monii-0.2-win-x64/Monii.exe`, con su carpeta completa. No necesita instalar .NET. Datos normales sin ejemplos; `--data-dir` permite pruebas aisladas. Ver README para flujo paso a paso.

Revisar [reglas financieras](REGLAS-FINANCIERAS.md): deuda USD, último costo, descuentos absolutos, cambio USD y precios finales sin impuestos automáticos. Reglas ajustables en Application. Ventas normal registra operaciones; demo separada no persiste.

## Pendientes y límites

- Impresión, lotes/vencimientos, tasas automáticas, usuarios/permisos, factura fiscal, instalador, actualizador y varias computadoras pendientes.
- Anulación completa; devolución parcial y cuentas por pagar pendientes. Anular compra conserva costo actual para no revertir costos posteriores.
- Operaciones guardadas como agregado JSON en transacción SQLite con bloqueo de escritura antes de validar. Decimal exacto y atomicidad, pero historial leído completo. Antes de grandes volúmenes o red: tablas indexadas, paginación y pruebas de rendimiento. No se probó con años de datos.
- UI por eventos/bindings con reglas fuera de WPF; MVVM completo pendiente. Restore consistente por SQLite Backup y recovery; cortes eléctricos/disco defectuoso requieren validación adicional.
- Backup diario al abrir, no continuo. Copias en mismo disco no protegen de su pérdida.

No hay bloqueos abiertos para evaluar esta versión. Próximo: revisar reglas/interfaz, piloto, Windows 10/periféricos, devoluciones/lotes/permisos y almacenamiento escalable antes de distribución comercial.
