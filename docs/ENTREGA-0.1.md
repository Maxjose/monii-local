# Informe de primera entrega · Monii 0.1

Fecha: 1 de octubre de 2026.

## Entregado

- Solución .NET 10 de cinco proyectos: Domain, Application, Infrastructure, Desktop y Verification.
- Aplicación WPF en español, estilo claro/verde petróleo, navegación lateral, inicio y actividad reciente. Adaptación a ventana pequeña con desplazamiento, F2/F4 y diálogos con scroll.
- Catálogo operativo: crear, editar, filtrar por nombre/código/categoría/marca/referencia, mostrar inactivos, activar/desactivar. Categorías por texto, unidades, marca, referencia, compatibilidad opcional, costo y precio USD.
- Configuración operativa: negocio, identificación fiscal, perfiles general/víveres/repuestos, módulos, dependencias clientes/créditos, campos por perfil. Desactivar conserva datos.
- Configuración de USD base, Bs BCV manual, Bs manual y COP; tasas con seis decimales, switches independientes y fecha de cambio. Conversiones visibles en demo.
- SQLite local separado de instalación, migración transaccional v1 y rechazo de esquema más reciente. Código único, consultas parametrizadas y auditoría de snapshots sin borrado.
- Protección frente a segunda instancia en la misma sesión/carpeta de datos y registro básico de errores.
- Plan completo, decisiones/supuestos, instrucciones, scripts y carpeta ejecutable portable win-x64 con runtime incluido.

## Prototipos claramente identificados

Ventas: productos ficticios, búsqueda, carrito, cantidades acumuladas, eliminación, totales y simulación de cobro. No guarda venta, pago, ticket ni movimiento. Inventario/compras/clientes/créditos/caja/reportes son pantallas informativas de próxima fase. Impresión y lotes solo conservan preferencias.

## Verificación

- Build Release: sin errores ni advertencias.
- Ejecutable portable final ejecutado directamente: salida 0 y las 13 comprobaciones WPF completas, usando su runtime incluido. Resultados adicionales en `artifacts/portable-review`.
- 20 comprobaciones de integración SQLite: creación, reapertura, edición, búsqueda, duplicados, negativos, nombre vacío, precisión, inactivación/reactivación, conservación histórica, configuración/tasas, dependencia de módulos y redondeo.
- 13 comprobaciones de controles WPF: datos iniciales aislados, configuración/perfil, tasas con coma decimal, ocultación de módulos, creación por formulario, filtrado de tabla, cantidades demo, conversiones identificadas, aislamiento del catálogo, vaciado y navegación en ventana mínima.
- Imágenes de Inicio, Productos, Configuración y Ventas renderizadas desde WPF e inspeccionadas visualmente. Corregidos contraste de botones, columnas y acceso a navegación en ventana pequeña. Imágenes en `artifacts/ui-review`.
- Pruebas ejecutadas en el equipo actual (Windows build 26200 x64). No equivale a validar toda la matriz Windows 10/11, distintas escalas DPI o impresoras físicas.
- SDK .NET 10.0.401 preparado solo en `.tools/dotnet`. NuGet necesitó permisos adicionales de red; revisión automática autorizó restauración. Se corrigió dependencia SQLite vulnerable detectada en el primer restore, sin suprimir avisos.

## Cómo revisar

Abrir `artifacts/Monii-win-x64/Monii.exe` manteniendo todos los archivos de esa carpeta. El catálogo normal comienza vacío. Crear un producto, editarlo, buscarlo y desactivarlo; cerrar y reabrir para comprobar persistencia. Configurar nombre/perfil y monedas; explorar ventas demo. El ejecutable portable no necesita SDK ni runtime instalado.

La base normal se guarda en `%LOCALAPPDATA%/Monii/monii.db`; las verificaciones usan carpetas separadas. No se publicaron binarios ni se desplegó un servicio. La carpeta original estaba vacía; no existía repositorio Git ni AGENTS.md y no se realizaron commits.

## Decisiones y límites

Acceso SQLite explícito con migraciones, no EF Core por ahora. Esquema inicial JSON para catálogo/configuración; operaciones futuras tendrán tablas normalizadas. UI de eventos y bindings con reglas fuera de WPF; MVVM completo pendiente antes de ampliar flujos. Reglas de créditos, descuento, impuesto, costos de inventario y devoluciones necesitan especificación antes de ventas reales. La compatibilidad de Windows 10 debe verificarse en un equipo de esa versión; compilación x64 únicamente.

## Bloqueos y próximos pasos

No hay bloqueos para probar esta entrega. Pendientes del alcance posterior: consulta automática de tasas, stock/movimientos, ventas transaccionales y medios de pago, tickets, compras, clientes/créditos, caja/reportes, usuarios/permisos, backups/restauración, instalador/firmas/actualizador y servicio en red. Siguiente paso: revisar interfaz y catálogo, definir reglas de inventario y comenzar movimientos de existencias.
