# Entrega Monii 0.4 · categorías, captura y apariencia

Fecha: 1 de octubre de 2026. Alcance solicitado implementado. Sin publicación/despliegue. Tickets y validación/distribución de Windows 10 siguen aplazados.

## Cambios operativos

- **Categorías:** nuevo apartado lateral para crear, buscar, editar, renombrar y activar/desactivar. Nombres duplicados rechazados sin distinguir mayúsculas. Sin borrado físico.
- **Producto:** categoría elegida desde lista no editable; opción Sin categoría. Se conservan categorías inactivas ya asignadas al editar productos, sin permitir nuevas asignaciones a ellas.
- **Migración v4:** convierte categorías de texto existentes en listado, normaliza nombres equivalentes, conserva catálogo/operaciones y copia before-v4. Renombrar actualiza productos en una transacción.
- **Escanear:** botón junto al código abre captura enfocada. Enter/Tab coloca el código automáticamente; Escape/cancelación/30 segundos de espera. Capturar no guarda el producto sin su confirmación.
- **Configuración:** pestañas Negocio, Apariencia, Usuarios, Respaldos y Actualizaciones. Se retiraron esas últimas tres herramientas del menú lateral, conservando sus permisos y funciones.
- **Iconos:** todas las opciones laterales llevan iconos de Segoe MDL2 Assets.
- **Apariencia:** modo claro/oscuro y color principal; cinco sugerencias y hexadecimal personalizado. Persistente al reabrir y en respaldos. Formularios, tablas, listas y navegación adaptados; contraste corregido tras inspección visual.

Usuarios vendedores pueden consultar categorías de sus productos, pero no gestionarlas. Configuración sigue limitada al administrador. Caja/ventas/compras/clientes/créditos/reportes/respaldos continúan operativos.

## Verificación

- Release: cero errores y advertencias.
- **144 comprobaciones de integración:** 128 anteriores + 16 nuevas de categorías, migración, conservación de stock/venta, desactivación/reactivación, duplicados, permisos, apariencia y respaldos.
- **43 comprobaciones WPF:** 32 anteriores + 11 nuevas de navegación/pestañas/iconos, creación/desactivación de categoría, selector, captura de código, modo oscuro/color y vuelta a claro.
- Escaneo simulado con eventos reales de teclado WPF: entrada vacía no confirma; código con Enter se copia al formulario y producto/categoría persisten.
- Imágenes inspeccionadas de apariencia, producto y ventas oscuras, reportes y Configuración a 960×640. Se corrigió el desplegable claro que inicialmente reducía contraste en modo oscuro.
- Portable final con runtime incluido verificado mediante WPF y base nueva aislada.
- Evidencia: artifacts/build-04-final.txt, artifacts/verification-04.txt y artifacts/phase4-portable-review/ui-verification.txt e imágenes.

Las imágenes son renderizadas por WPF. Las verificaciones no modifican la base real del negocio.

## Probar

Cerrar la versión anterior y abrir **artifacts/Monii-0.4.0-win-x64/Monii.exe**, conservando toda su carpeta. Puede usarse --data-dir con carpeta nueva para explorar.

1. Categorías → Nueva categoría.
2. Productos → Nuevo producto → seleccionar categoría.
3. Escanear → usar lector en modo teclado o escribir código y Enter para probar el flujo.
4. Configuración → Apariencia → modo/color → Guardar apariencia.
5. Configuración → Usuarios, Respaldos o Actualizaciones para sus herramientas.

Generador y paquete firmado de actualización disponibles localmente. Una instalación 0.3 puede importar el ZIP 0.4.0 junto a su .zip.json desde Actualizaciones. No hay servidor publicado. Ver ACTUALIZACIONES.md.

## Límite del lector y pendientes

El usuario indicó que todavía no tiene lector. La captura está preparada para USB/Bluetooth en modo teclado con Enter/Tab; el gatillo se pulsa en el dispositivo. El botón no enciende físicamente el lector. Cámara y dispositivos con SDK propio no están integrados. Prueba física pendiente cuando se elija hardware.

Color/tema se comparten por negocio; solo administrador configura. Desactivar conserva datos. Renombrar modifica la categoría actual del catálogo, sin alterar ventas, costos o cantidades históricas.

Persisten límites previos: lectura completa del historial en algunos casos de uso, pruebas de carga, red, lotes/vencimientos, cuentas por pagar e importación. Tickets y Windows 10 aplazados. No hay bloqueos para probar esta entrega local.

[Guía](../README.md) · [Plan](PLAN.md) · [Arquitectura](ARQUITECTURA.md) · [Entrega anterior](ENTREGA-0.3.md)
## Actualización 0.4.1
Disponible en artifacts/Monii-0.4.1-win-x64. Detalle y verificaciones en AJUSTES-0.4.1.md.

## Corrección 0.4.2
Secciones de Configuración restauradas arriba, una junto a otra con márgenes. Plantilla común de submenús corregida para preservar las cuatro esquinas y evitar cortes derechos en Ventas, Créditos, Caja, Compras y Reportes. Verificada visualmente a 960 × 640; 55 comprobaciones WPF aprobadas. Portable: artifacts/Monii-0.4.2-win-x64.

## Cambio 0.4.3
Botón Cerrar sesión con confirmación previa. Cancelar, Escape o cerrar el diálogo conserva la sesión y el carrito. Confirmar descarta el carrito pendiente y vuelve al acceso; datos guardados y caja abierta se conservan. Cancelar es la opción predeterminada. 60 comprobaciones WPF superadas en la compilación portable.

## Revisión integral
175 comprobaciones de integración y 66 WPF aprobadas. Informe y limitación de BCV en PRUEBAS-2026-10-01.md.

## Entrega 0.5.0 — Importación
Operativo: Productos → Importar Excel / CSV, plantilla CSV, primera hoja visible .xlsx, vista previa por fila, errores, categorías opcionales, actualización por código preservando historial, permisos, respaldo previo y lote atómico. 214 comprobaciones de integración y 76 WPF. Portable en artifacts/Monii-0.5.0-win-x64. Manual en docs/IMPORTACION.md. No modifica existencias ni requiere migración. Excel antiguo .xls debe guardarse como .xlsx.

## Entrega 0.6.0 — detección
239 comprobaciones de integración y 88 WPF. Manifest firmado vacío local, GitHub main configurado como origen, detección automática/manual y aviso de novedades. El endpoint actual retorna no publicado porque no se subieron archivos. No se descargan ni instalan actualizaciones, ni se generó paquete 0.6. Portable en artifacts/Monii-0.6.0-win-x64. Manual: docs/ACTUALIZACIONES.md.
Ajuste final de interfaz: sin URL ni controles técnicos. Casilla al abrir con guardado automático, botón Buscar actualizaciones y botón Actualizar programa preparado pero deshabilitado mientras no haya instalación. Se quitaron las comprobaciones cada 6 horas.

## Entrega 0.7 — servidor y cajas

### Operativo y probado
Modo local conservado y modo conectado con principal central; login, permisos de servidor, catálogo/categorías/importación, inventario, ventas, clientes, créditos/abonos, compras, devoluciones y anulaciones. Cajas identificadas por equipo con efectivo independiente o compartido. Estado de conexión, reconciliación accesible al cajero desde Inicio, backup descargado/restauración central validada y revocación de sesiones. Preferencias de negocio/monedas compartidas. Migración v4 a v5 con copia previa. Servicio y controles Windows preparados para instalación desde el portable.

### Verificación
Compilación .NET 10 sin errores ni advertencias; 239 comprobaciones locales, 51 de red mediante HTTPS real con varios clientes, 88 WPF locales y 13 WPF conectadas. Se probaron concurrencia de última unidad, pérdida de conexión antes y después de confirmar, reinicio con replay persistente, cierres por caja, cambio de modalidad, crédito/anulación/devolución, conversión COP con cambio posterior de tasa, importación y restauración. Los tests usan bases aisladas y no alteran los datos del negocio. Scripts de servicio y firewall con sintaxis validada. Portable WPF: las 88 comprobaciones también pasaron ejecutando Monii.exe sin SDK. Servidor portable: inicio HTTPS confirmado con base aislada.

### Corregido durante la verificación
Carga de certificado compatible con TLS Windows; distribución de las siete pestañas de configuración en una fila a 960 píxeles sin cortar esquinas; acceso a reconciliación para cajeros sin privilegios de configuración; separación de saldos por caja y bloqueo de peticiones anteriores a una restauración. Históricos de caja muestran su nombre.

### Entrega y límites
Portable: `artifacts/Monii-0.7.0-win-x64/Monii.exe`, incluyendo `server/runtime`, scripts de instalación/control y guía RED.md. No se instaló el servicio permanente ni se abrió el firewall; no se publicó una Release ni se desplegó. Servicio real bajo LocalService, reinicio Windows, LAN con dos equipos físicos y Windows 10 pendientes: esta sesión no tiene elevación Windows ni un segundo equipo disponible. El servidor probado se ejecutó como proceso de consola independiente de WPF. La demostración de ventas sigue marcada como demo; los flujos financieros de red prueban persistencia real.

### Decisiones para revisar en el piloto
Cajas independientes por defecto o caja compartida; IP reservada del principal; nombres de cajas y permisos de usuarios. Reintegros/anulaciones salen de la caja del operador, con validación de efectivo; deudas siguen en USD. Preferencias y apariencia compartidas entre equipos. Sin operación desconectada; respaldo remoto limitado a 100 MB. Guía y pasos completos en RED.md. BCV con tasa futura, tickets y distribución Windows 10 conservan su estado pendiente anterior.

## Ajustes: desinstalación y cierre de sesión
- Operativo: opción de desinstalar en el equipo principal; exige permisos, confirmación, cajas cerradas y ausencia de operaciones pendientes. Recupera datos centrales para modo local y conserva copia previa, datos originales y respaldos.
- Operativo: cierre de sesión muestra únicamente el acceso; nuevo ingreso vuelve a abrir la vista principal.
- Verificado: compilación sin errores/advertencias; 56 comprobaciones HTTPS, incluyendo recuperación de ventas y catálogo y preservación de base anterior; 90 comprobaciones WPF, incluyendo ventana oculta, sesión cerrada y nuevo ingreso. Script de desinstalación con sintaxis válida.
- Pendiente de piloto: eliminación real del servicio y regla de firewall con elevación Windows. No se desinstaló ni instaló un servicio en este equipo. Los datos usados para verificar son aislados.

### Indicador de inicio de sesión
El botón Ingresar muestra un icono animado y «Iniciando sesión…» durante autenticación, sin bloquear la interfaz. Impide solicitudes repetidas y cambios de conexión durante la espera. Ante error vuelve a habilitar el formulario para reintentar. Compilación correcta y 91 comprobaciones WPF aprobadas, incluida carga y nuevo acceso. Portable actualizado.

### Ajuste visual del acceso
Indicador sustituido por aro vectorial animado, centrado y sin texto. Conserva la altura y anchura del botón. Verificado visualmente y mediante 91 comprobaciones WPF; portable actualizado.

## Entrega: lotes, inicio y contraseñas
Operativo: recepción de varios lotes por producto, clasificación del stock anterior, ajustes específicos, listado buscable, alertas a 30 días, FEFO automático y bloqueo de vencidos. Ventas, devoluciones y anulaciones conservan el lote original. La anulación de compra no sustituye un lote consumido por otro. Local y red comparten reglas. Migración SQLite 6 conserva datos y devoluciones previas con copia de seguridad. Guía: LOTES.md.

Inicio: retirados el título redundante y botón de explorar demo; contenido acercado al encabezado. El lema del menú se conserva. Contraseñas nuevas y cambios: mínimo 8 caracteres; las existentes continúan válidas.

Verificaciones: compilación sin errores ni advertencias; 260 comprobaciones locales (21 de lotes/contraseñas/migración/restauración), 60 de red HTTPS, 95 de interfaz WPF y 13 de interfaz conectada incluidas en la prueba de red. Capturas revisadas del inicio, listado y compra. Datos de prueba aislados. Portable actualizado, con servidor y guía.

Decisiones documentadas: fecha válida hasta finalizar el día indicado; recepción física de vencidos permitida, venta bloqueada; fecha de lote inmutable, código por producto; sin lote para stock previo; costo sigue siendo último costo de compra; reintegros automáticos al lote original; aviso a 30 días. No incluye selección manual de lote al vender, costo contable por lote ni notificaciones externas. Servicio Windows real, LAN física y Windows 10 mantienen sus pruebas pendientes anteriores.

## Perfil básico
Operativo: selección en Configuración > Negocio; inicio reducido, productos/categorías y listado de precios. Formulario de producto con nombre, código/escáner, categoría, precio y estado. Listado de productos activos con búsqueda, filtro por categoría y solo monedas activas. F4 abre precios. Mantiene configuración, respaldo, usuarios y actualización.

Oculta ventas/inventario/compras/clientes/créditos/caja/reportes y bloquea sus mutaciones en aplicación y servidor. Conserva preferencias, productos, campos avanzados, existencias e históricos. Exige cerrar todas las cajas y saldar créditos para activar básico. Perfil global del negocio, administrado por usuario autorizado; funciona local y en red. Sin nueva migración SQL: enum añadido conservando valores anteriores.

Verificado: compilación sin errores/advertencias; 273 comprobaciones locales (13 nuevas de básico), 66 HTTPS de red, 103 WPF locales y 13 WPF conectadas incluidas en la prueba de red. Se probó creación/edición en formulario básico, consulta con COP y BCV desactivado, persistencia, respaldo/restauración, bloqueo financiero, cambio entre perfiles y conservación del histórico. Capturas revisadas del inicio, formulario y listado. Bases de prueba aisladas. Guía: BASICO.md.

## Corrección: servidor instalado anterior al perfil básico (2 de octubre de 2026)
Se detectó una instalación activa cuyos archivos difieren del portable nuevo. La instalación anterior conserva su copia de los binarios; reiniciar por sí solo no la actualiza. Esto puede producir «Perfil de negocio inválido» cuando el cliente nuevo envía Básico al servidor anterior.

En el principal, abre el portable nuevo y usa Configuración > Conexión > Actualizar servidor instalado. Exige administrador de Monii/Windows, confirmación y todas las cajas cerradas. Crea respaldo de base sin migrar el origen y copia de los binarios; reemplaza runtime, prepara migraciones y reinicia. Si falla antes de iniciar, restaura base/binarios previos; después de intentar iniciar no reemplaza automáticamente la base para evitar perder operaciones nuevas. Registros en maintenance.log y copias protegidas en ProgramData/MoniiServer/maintenance. La instalación sobre servicio existente también usa este mantenimiento.

Reabre Monii e ingresa antes de seleccionar Básico. La interfaz reconoce el rechazo del servidor antiguo y muestra una indicación concreta. Compilación y prueba HTTPS de respaldo/recuperación verificadas con datos aislados; sintaxis de scripts comprobada. No se actualizó el servicio real desde esta sesión: la acción requiere elevación Windows y debe hacerse con cajas cerradas.

Portable de corrección: artifacts/Monii-0.7.0-corregido-win-x64/Monii.exe. Se generó por separado porque la aplicación abierta bloqueaba archivos del portable anterior. Prueba HTTPS: 68 comprobaciones aprobadas. El servicio instalado permanece pendiente de mantenimiento autorizado con elevación Windows.

## Descubrimiento del servidor (2 de octubre de 2026)
Operativo: búsqueda automática local, selección de servidor, código de confirmación inicial y guardado de huella sin teclearla. Emisor UDP en el servicio y reglas privadas de instalación/mantenimiento/desinstalación. Alternativa manual conservada en sección cerrada. Compilación correcta, 73 comprobaciones HTTPS/UDP y 106 WPF aprobadas; la prueba UDP usa loopback, no una LAN física. Pendientes mantenimiento real con UAC y piloto de dos PCs. El portable habitual actualizado permite realizar el mantenimiento y corregir el rechazo de Básico causado por el servidor anterior.
