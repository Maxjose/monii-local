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
