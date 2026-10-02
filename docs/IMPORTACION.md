# Importación de productos — Monii 0.5

En Productos, pulsa **Importar Excel / CSV**.

1. Guarda la plantilla CSV o prepara un archivo con sus mismos encabezados.
2. Sustituye el producto de ejemplo. Selecciona tu archivo.
3. Revisa todas las filas. Los errores se muestran en la última columna; al pasar el cursor puedes leer el texto completo.
4. Si lo necesitas, activa Actualizar códigos existentes o Crear categorías faltantes.
5. Pulsa Importar productos. Se crea un respaldo previo y se guarda el lote completo.

## Formatos
- Excel .xlsx: se lee la primera hoja visible. Guarda los códigos como texto para conservar ceros iniciales y códigos largos. No se importan fórmulas, aunque tengan un resultado calculado; pega sus resultados como valores.
- CSV UTF-8: separador punto y coma, coma o tabulador, detectado por los encabezados. Admite valores entre comillas, comillas escapadas y campos con saltos de línea.
- Archivos .xls: guardar como .xlsx antes de importar.
- Máximo 5.000 productos y 10 MB por archivo. Excel: hasta 50 MB descomprimidos y 30 columnas.
- Encabezados en la primera fila con contenido; los nombres ignoran espacios, mayúsculas y tildes. No se admiten columnas desconocidas o repetidas.
- Los errores se corrigen en el archivo de origen; vuelve a seleccionarlo para leer los cambios. Volver a validar revisa el catálogo actual y los datos ya cargados.

## Columnas
| Encabezado | Requerido | Contenido |
|---|---|---|
| Codigo | Sí | Código único. Se conservan ceros; máximo 128 caracteres |
| Nombre | Sí | Nombre del producto; máximo 250 caracteres |
| CostoUSD | Sí | Costo en dólares; hasta dos decimales |
| PrecioUSD | Sí | Precio en dólares; hasta dos decimales |
| Categoria | No | Nombre del listado; vacío significa sin categoría |
| Unidad | No | Unidad, Kilogramo, Litro, Metro o Caja |
| Marca | No | Marca |
| Referencia | No | Código alternativo |
| Compatibilidad | No | Vehículos o compatibilidad |
| StockMinimo | No | Umbral de alerta; hasta tres decimales |
| Activo | No | Si o No; también true/false y 1/0 |

Usa punto o coma decimal, sin separadores de miles ni símbolos de moneda. En CSV separado por coma, un valor como 3,50 debe ir entre comillas.
Los nuevos productos usan Unidad, activo y stock mínimo cero cuando esas columnas están ausentes. Los existentes conservan sus campos opcionales cuando la columna está ausente. Una columna presente y vacía representa un valor vacío; Unidad, StockMinimo y Activo requieren un valor válido si se incluyen.

## Actualizaciones e historial
- Por defecto, un código existente, incluso desactivado, bloquea la fila.
- Actualizar códigos existentes permite editar el mismo producto: mantiene su ID, existencias, movimientos y documentos anteriores.
- Importar no cambia la unidad de productos existentes.
- Los códigos duplicados dentro del archivo invalidan todas sus filas, sin distinguir mayúsculas.
- Una categoría desactivada solo puede conservarse en el producto que ya la tenía.
- Crear categorías faltantes añade categorías activas; nombres equivalentes se unifican.
- No se importan cantidades en inventario: registra compras o ajustes.
- Las ventas conservan sus precios y costos históricos.

## Respaldo y seguridad
Solo usuarios con permiso para gestionar productos pueden importar. Se exige una vista previa sin errores. El almacenamiento vuelve a validar el catálogo dentro de una transacción; si cambió después de la vista previa, vuelve a validar. Si algo falla no se guardan productos, categorías ni auditoría parcial.
Antes de guardar se crea una copia monii-before-import-*.db en la carpeta backups junto a la base de datos. Estas copias se conservan y pueden restaurarse desde Configuración → Respaldos.
La auditoría registra quién importó, las filas y los valores anteriores y nuevos.

## Verificación de entrega
214 comprobaciones de integración aprobadas (39 específicas de importación), más 76 comprobaciones WPF, incluida la compilación portable.
Se probaron CSV, Excel, duplicados, categorías, fórmulas, actualización histórica, permisos, respaldo, cancelación y rollback.
La lectura de Excel usa los componentes ZIP/XML de .NET; no requiere Microsoft Excel instalado ni paquetes nuevos.