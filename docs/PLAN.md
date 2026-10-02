# Plan completo de desarrollo de Monii

Actualizado: 1 de octubre de 2026 · entrega 0.4.

## Requisitos y límites

Windows 10/11; inicialmente una computadora. C#/.NET 10/WPF, arquitectura modular por capas y SQLite administrado localmente. Negocio general, víveres y repuestos. Español, interfaz moderna y navegación lateral. Catálogo/ventas esenciales; módulos y monedas opcionales configurables sin borrar datos. USD contable; Bs BCV, Bs manual y COP independientes. Respaldos. Futuras actualizaciones y trabajo en varias computadoras.

No publicación ni despliegue. Usuario aplazó impresión de tickets y validación/distribución para Windows 10. Las fases futuras del plan no implican autorización para implementarlas todas en esta entrega.

## Fases y criterios de aceptación

| Fase | Objetivo | Estado y aceptación |
|---|---|---|
| 1. Definición y diseño | Perfiles, flujos, reglas financieras y navegación | Documentado. Decisiones revisables en REGLAS-FINANCIERAS.md; revisión comercial/fiscal posterior |
| 2. Base técnica | Capas, configuración, SQLite/migraciones, errores y operación local | Operativa. Migración 1/2→3 conserva datos, esquema futuro rechazado |
| 3. Catálogo | Crear/editar/buscar, categorías/códigos, costos/precios, unidades, marcas/repuestos y desactivar con historial | Operativo. Categorías administrables, selector y captura por lector en 0.4. Importación masiva pendiente |
| 4. Inventario | Movimientos, ajustes, stock mínimo, cantidades por unidad/peso, trazabilidad | Operativo. Stock negativo y fallos parciales rechazados. Lotes/vencimientos pendientes |
| 5. Monedas | USD, dos referencias VES y COP; automática al abrir/periódica/manual, caché y snapshots | Operativa en 0.3. Ambas fuentes reales verificadas; sin red conserva tasa/fecha |
| 6. Ventas | Carrito, códigos, descuentos, pagos combinados, crédito, historial/anulación y devoluciones parciales | Operativa en 0.3. Transacción stock/pago; centavos, saldos y devoluciones verificados |
| 7. Compras | Proveedores, recepción, costos, pagos, historial y anulaciones | Operativa con pago completo. Cuentas por pagar y devolución parcial de compras pendientes |
| 8. Clientes/créditos | Contactos, límites/vencimientos, abonos, estado de cuenta y saldos | Operativa; deuda USD y snapshots. Intereses/cobranza avanzada posteriores |
| 9. Caja/usuarios/reportes | Apertura/cierre, diferencias, gastos, roles, auditoría, reportes por fechas/usuario, stock/deuda y CSV | Operativa en 0.3. Permisos backend y conciliación probados. Reporte agregado por producto y exportaciones avanzadas futuros |
| 10. Respaldos/estabilidad | Manual/automático, retención/destino, restauración validada, rollback y pruebas completas | Operativa. 144 comprobaciones de integración y 43 WPF; pruebas de carga/energía pendientes |
| 11. Versiones/actualizaciones | Paquetes firmados, versión/notas, HTTPS/local, respaldo/migraciones y recuperación | Cliente/generador operativos localmente. Hosting/feed comercial sin publicar; procedimiento documentado |
| 12. Tickets/distribución | Impresión opcional 58/80 mm, instalador/firma, matriz Windows 10/11 y pilotos | Aplazados tickets y Windows 10. Instalador comercial/Authenticode y periféricos pendientes |
| 13. Red | Servicio ASP.NET Core/base central, varias cajas, autenticación, migración, concurrencia y desconexión | Futura actualización. No compartir archivo SQLite por red |

## Entregas

- **0.1:** estructura, catálogo/configuración, tasas manuales y venta demo.
- **0.2:** inventario → ventas → caja → compras/proveedores → clientes/créditos → reportes/respaldos, con gates antes de avanzar.
- **0.3:** usuarios/permisos, tablas/indexación y migración, devolución parcial, tasas automáticas y actualizador firmado. Se preservaron datos, se mantuvo demo separada y se documentaron límites.

## Siguiente trabajo recomendado

Revisión del propietario de roles, último costo, deuda/reintegro USD y reportes netos. Después, mejorar consultas por operación y probar carga antes de crecer a red. Las siguientes ampliaciones funcionales serían importación, lotes/vencimientos y cuentas por pagar, con reglas acordadas y pruebas por flujo. Tickets y Windows 10 se retomarán cuando el usuario lo solicite.

No avanzar a nuevas funciones para ocultar fallos importantes. Mantener plan/informe, migraciones preservando datos, portable local y pruebas independientes de la base real.
- **0.4:** categorías persistentes/migración, selector en producto, escaneo por teclado, Configuración con pestañas, iconos, color y modo claro/oscuro. Prueba física del lector pendiente; no existe hardware disponible.
## Ajustes 0.4.1 completados
Sección independiente de monedas, caja con monedas activas, paleta armonizada y configuración adaptable. Véase AJUSTES-0.4.1.md.

Corrección 0.4.2 completada: submenús superiores y márgenes completos en todas las secciones.

Completado 0.4.3: cierre de sesión con confirmación previa.

Revisión integral 2026-10-01: 175 comprobaciones de integración y 66 WPF aprobadas. Pendiente BCV con vigencia futura. Véase PRUEBAS-2026-10-01.md.

## Monii 0.5 completado
Importación Excel/CSV de catálogo con plantilla CSV, vista previa, validación, creación opcional de categorías, actualización explícita, respaldo y transacción completa. No importa existencias. Ver docs/IMPORTACION.md.

## Monii 0.6 — detección de actualizaciones
Completado: manifiesto firmado preparado para Maxjose/monii-local, consulta automática solo al abrir (opcional), consulta manual, versiones, notas y errores controlados. Descarga/instalación fuera del alcance actual. No hay archivos subidos ni Releases publicadas. Ver ACTUALIZACIONES.md.
