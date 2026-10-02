# Plan completo de desarrollo de Monii

Actualizado: 1 de octubre de 2026 · entrega 0.7.

## Requisitos y límites

Windows 10/11; inicialmente una computadora. C#/.NET 10/WPF, arquitectura modular por capas y SQLite administrado localmente. Negocio general, víveres y repuestos. Español, interfaz moderna y navegación lateral. Catálogo/ventas esenciales; módulos y monedas opcionales configurables sin borrar datos. USD contable; Bs BCV, Bs manual y COP independientes. Respaldos. Futuras actualizaciones y trabajo en varias computadoras.

No publicación ni despliegue. Usuario aplazó impresión de tickets y validación/distribución para Windows 10. Las fases futuras del plan no implican autorización para implementarlas todas en esta entrega.

## Fases y criterios de aceptación

| Fase | Objetivo | Estado y aceptación |
|---|---|---|
| 1. Definición y diseño | Perfiles, flujos, reglas financieras y navegación | Documentado. Decisiones revisables en REGLAS-FINANCIERAS.md; revisión comercial/fiscal posterior |
| 2. Base técnica | Capas, configuración, SQLite/migraciones, errores y operación local | Operativa. Migración 1/2→3 conserva datos, esquema futuro rechazado |
| 3. Catálogo | Crear/editar/buscar, categorías/códigos, costos/precios, unidades, marcas/repuestos y desactivar con historial | Operativo. Categorías administrables, selector y captura por lector en 0.4. Importación CSV/.xlsx operativa desde 0.5 |
| 4. Inventario | Movimientos, ajustes, stock mínimo, cantidades por unidad/peso, trazabilidad | Operativo. Stock negativo y fallos parciales rechazados. Lotes/vencimientos operativos con FEFO y preservación de reintegros |
| 5. Monedas | USD, dos referencias VES y COP; automática al abrir/periódica/manual, caché y snapshots | Operativa en 0.3. Ambas fuentes reales verificadas; sin red conserva tasa/fecha |
| 6. Ventas | Carrito, códigos, descuentos, pagos combinados, crédito, historial/anulación y devoluciones parciales | Operativa en 0.3. Transacción stock/pago; centavos, saldos y devoluciones verificados |
| 7. Compras | Proveedores, recepción, costos, pagos, historial y anulaciones | Operativa con pago completo. Cuentas por pagar y devolución parcial de compras pendientes |
| 8. Clientes/créditos | Contactos, límites/vencimientos, abonos, estado de cuenta y saldos | Operativa; deuda USD y snapshots. Intereses/cobranza avanzada posteriores |
| 9. Caja/usuarios/reportes | Apertura/cierre, diferencias, gastos, roles, auditoría, reportes por fechas/usuario, stock/deuda y CSV | Operativa en 0.3. Permisos backend y conciliación probados. Reporte agregado por producto y exportaciones avanzadas futuros |
| 10. Respaldos/estabilidad | Manual/automático, retención/destino, restauración validada, rollback y pruebas completas | Operativa. 144 comprobaciones de integración y 43 WPF; pruebas de carga/energía pendientes |
| 11. Versiones/actualizaciones | Paquetes firmados, versión/notas, HTTPS/local, respaldo/migraciones y recuperación | Cliente/generador operativos localmente. Hosting/feed comercial sin publicar; procedimiento documentado |
| 12. Tickets/distribución | Impresión opcional 58/80 mm, instalador/firma, matriz Windows 10/11 y pilotos | Aplazados tickets y Windows 10. Instalador comercial/Authenticode y periféricos pendientes |
| 13. Red | Servicio ASP.NET Core/base central, varias cajas, autenticación, migración, concurrencia y desconexión | Implementada en 0.7: HTTPS, autenticación central, cajas independientes/compartida e idempotencia. Piloto físico y alta del servicio pendientes; véase RED.md |

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

## Monii 0.7 — varias computadoras

Implementados gateway local/remoto, servidor ASP.NET Core para servicio Windows, configuración de equipo, migración v5, cajas por terminal o compartida, permisos centrales, comprobantes persistentes de peticiones y reconciliación. Respaldos centrales con restauración exclusiva y revocación de sesiones. Verificaciones: 239 locales, 51 de red, 88 WPF locales y 13 WPF conectadas. Portable local preparado; instalación física del servicio, reinicio del equipo y LAN real pendientes por falta de elevación Windows y segundo equipo. No se desplegó ni se publicaron actualizaciones.

### Ajustes posteriores al servidor central
Completados: desinstalación protegida con recuperación a modo local, preservación de datos previos y cierre de sesión con ventana principal oculta. Verificados recuperación y acceso en bases aisladas. Pendiente: piloto de desinstalación real bajo UAC en Windows.

### Lotes y vencimientos
Completados recepción, clasificación, ajustes, alertas, listado, asignación FEFO, bloqueo de vencidos y reintegros originales; migración v6, respaldos y pruebas de interfaz/local/red. Reglas y decisiones documentadas en LOTES.md y REGLAS-FINANCIERAS.md. Inicio compacto y mínimo de contraseña de 8 caracteres completados.

### Perfil básico
Completados perfil Básico, navegación reducida, inicio sencillo, formulario de producto sin campos avanzados y listado de precios con búsqueda/categoría/monedas activas. Configuración, respaldo y acceso conservados. Operaciones financieras bloqueadas en aplicación/servidor; histórico y preferencias preservados al cambiar perfil. Guía: BASICO.md.

### Mantenimiento y detección local del servidor
Implementados actualización manual del servicio instalado con respaldo/copia previa, mensaje específico para Básico no reconocido por runtime anterior, búsqueda UDP IPv4 y confirmación inicial mediante código corto. Verificados servidor UDP local, login HTTPS y formulario de guardado. Piloto físico/firewall/UAC pendientes. Guía en RED.md.


## Ajustes de servidor — 2 de octubre de 2026

- Conexión y Servidor se presentan como subsecciones. Controles del servidor en tres columnas, con margen y texto adaptable.
- Configurar firewall solicita elevación Windows y repara únicamente las dos reglas de Monii: TCP de servicio y UDP de descubrimiento, con los puertos configurados, para red privada y subred local. No cambia la categoría de red ni desactiva el firewall. No ejecutado contra el firewall real durante las pruebas.
- Contraseñas: mínimo 8 caracteres, probado localmente y mediante creación y autenticación por HTTPS. Si otra copia muestra 12, usar el portable actualizado completo en ese equipo. El servidor instalado inspeccionado coincide con la biblioteca actual de seguridad.


## Corrección de sesión al cambiar contraseña — 2 de octubre de 2026

El cambio se guardaba, pero la siguiente recarga fallaba porque el sello de sesión incluía la contraseña anterior. El servidor renueva exclusivamente la sesión que realiza una edición propia si la cuenta continúa activa y conserva su rol. Las demás sesiones de la cuenta se revocan; una desactivación o cambio de rol no renueva la sesión. Se verifica cambio propio, recarga de usuarios, rechazo de contraseña anterior y acceso con la nueva. Requiere actualizar el servidor instalado desde el portable corregido, con todas las cajas cerradas.


## Seguimiento: contraseña guardada y error de recarga — 2 de octubre de 2026

- Los hashes del servidor instalado y del portable mostraron binarios distintos. Se reprodujo el error exacto usando ese ejecutable anterior con una base y certificado aislados, sin tocar el servicio ni los datos reales.
- RemoteStore renueva el acceso con la contraseña nueva tras confirmar un cambio propio, antes de que el formulario recargue usuarios. Compatible con el servidor anterior y el corregido. Si falla la renovación por conexión, informa que el cambio ya se guardó y que se debe iniciar sesión con la nueva contraseña.
- Cinco verificaciones contra el runtime anterior; se añadió regresión WPF que cambia dos veces la contraseña propia desde Editar usuario y comprueba cierre y recarga del formulario. Se mantienen las pruebas de revocación de otras sesiones.
- El mantenimiento valida hashes de los cinco archivos principales copiados antes de arrancar el servidor. No se actualizó el servicio instalado desde las pruebas.


## Ventas: selección de productos y cantidades — 2 de octubre de 2026

- Se quitaron el desplegable y la cantidad exterior al carrito. Junto al buscador, un icono de listado abre una ventana con catálogo activo y filtro instantáneo por nombre, código, categoría, marca y referencia. Ctrl/Mayús permite elegir varios; Agregar seleccionados añade una unidad de cada producto sin cerrar la ventana; doble clic o Enter también agregan. Cerrar conserva el carrito.
- El buscador principal agrega con Enter o Agregar al carrito si hay un código exacto o una coincidencia única. Con varias coincidencias abre el catálogo filtrado; no elige arbitrariamente el primero.
- Cada fila dispone de menos, cantidad editable y más. Enter o salir del campo confirma. Unidad/caja admite enteros; otras unidades hasta tres decimales. Menos retira la fila al llegar a cero. Los cambios actualizan línea y subtotal; al cobrar se mantienen las validaciones centrales de inventario, lotes, permisos y caja. No modifica la base hasta confirmar la venta.
- Verificación WPF: filtro, exclusión de inactivos, selección múltiple, ventana abierta tras agregar, acumulación en una línea, más/menos, entrada escrita y decimal, rechazo de fracción en unidades, subtotal y eliminación. Flujo de venta/cobro local y conectado incluido.
