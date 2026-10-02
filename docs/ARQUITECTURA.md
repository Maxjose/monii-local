# Arquitectura de Monii 0.4

## Capas

- Domain: documentos, productos, configuración, monedas, usuarios y devoluciones.
- Application: casos de uso y reglas de costos, créditos, ventas, anulaciones y devoluciones; IStore/IOperationsStore.
- Infrastructure: SQLite, migraciones, autenticación, proveedores de tasas y verificación/instalación de paquetes.
- Desktop: WPF/XAML en español; navegación, formularios, bindings de lectura y eventos.
- UpdateTool: generador de claves/paquetes firmados y proceso independiente para instalar después del cierre.
- Verification: integración con SQLite real; WPF se verifica desde Desktop con --ui-verify en datos nuevos aislados.

Referencias: Desktop/UpdateTool → Infrastructure → Application → Domain. Reglas financieras aisladas en Application; proveedores y transporte HTTP encapsulados en Infrastructure. UI utiliza code-behind, no se presenta como MVVM completo. Para flujos más complejos se podrá introducir ViewModels sin mover las reglas a la interfaz.

## Persistencia y migraciones

Microsoft.Data.Sqlite 10.0.9 y SQLitePCLRaw.bundle_e_sqlite3 3.0.5. Auditoría NuGet activa y advertencias como errores.

- v1: catálogo/configuración/auditoría.
- v2: agregado operativo JSON. Se conserva para leer respaldos antiguos.
- v3: tablas customers, suppliers, sales/sale_lines, purchases/purchase_lines, stock_moves, cash_sessions/cash_entries, credit_payments, sale_returns y users; auditoría con actor.
- Relaciones por claves foráneas; índices por producto, fecha, documento y cliente; número de venta único y solo una caja abierta.
- Dinero sigue siendo decimal en snapshots JSON por documento/línea: sin REAL monetario. Existencias también se indexan como milésimas enteras.
- BEGIN IMMEDIATE antes de leer/validar stock y saldos; documento, inventario, caja, costo y auditoría se confirman juntos. Los fallos revierten todo.
- Se comparan snapshots antes de persistir: solo registros nuevos/cambiados emiten escrituras SQL. No se reemplaza un JSON de todo el negocio.
- Historial de ventas: consulta SQL paginada de 50; inventario: saldos agrupados SQL.
- Limitación conservada: los casos de uso aún leen el historial completo dentro de la transacción. La normalización y escrituras incrementales son operativas, pero no se certifica rendimiento con años de datos. Consultas específicas por operación y pruebas de carga serán el siguiente trabajo antes de red/gran volumen.

Migraciones con user_version, copia SQLite previa y transacción. Esquemas futuros se rechazan sin modificar. Datos separados de ejecutables en LocalApplicationData/Monii, o carpeta explícita --data-dir. No compartir SQLite por red. Mutex por carpeta/sesión Windows y transacciones para escrituras concurrentes.

## Seguridad

Roles administrador/cajero/vendedor. Permisos comprobados en persistencia antes de cada mutación; el permiso adicional de crédito se verifica antes de confirmar deuda o ampliar límite. Una cuenta desactivada pierde permiso aunque ya estuviera autenticada. Último administrador protegido.

PBKDF2-SHA256, 600.000 iteraciones, sal aleatoria de 32 bytes y comparación constante. No se guardan contraseñas en auditoría. Bloqueo de cinco minutos tras cinco fallos. Primera apertura exige crear administrador, sin cuenta por defecto. Después de restaurar se invalida la sesión.

No hay cifrado de base, servicio de autenticación remoto ni auditoría resistente a modificación externa. Las cuentas son una barrera dentro de la aplicación; permisos del archivo dependen de Windows.

## Tasas y actualizaciones

BCV por HTTPS y extracción estricta de dólar/fecha de su página; COP por API pública Socrata. Fechas validadas en zona America/Caracas. Tasa BCV consultada de máximo siete días y TRM vigente hoy. Si una fuente falla se conserva su valor y se permite actualizar la otra. Los snapshots de operaciones no cambian al actualizar tasas.

Paquetes ZIP y manifiesto firmado RSA-SHA256 con clave pública embebida. Tamaño/hash, versión y rutas verificados. Clave privada fuera del portable, en .tools/signing y excluida del repositorio. Instala en nueva carpeta, conserva anterior, respalda primero. Puntero local permite abrir la versión nueva desde el ejecutable original. No hay hosting/despliegue. Firma del paquete independiente de Authenticode.

## Respaldos y límites

SQLite Online Backup; integrity_check, esquema, foreign_key_check, referencias, stock/deuda, devoluciones y coherencia entre índices/JSON/líneas. Restore valida y migra staging, crea recovery y reemplaza mediante SQLite Backup. Diario al iniciar, 14 copias; corre como tarea del sistema sin conceder al cajero exportación manual.

Verificado en Windows 11 x64 del entorno. Windows 10, ARM64/32 bits, periféricos, fallo real de energía y matriz DPI pendientes. Un futuro servidor ASP.NET Core y base central reutilizará Domain/Application; requerirá cliente remoto, autorización, concurrencia y estrategia de desconexión. La arquitectura no convierte SQLite local en un sistema de red automáticamente.
## Extensión 0.4

Esquema 4 añade categories(id,name,active); importa nombres antiguos, normaliza espacios/mayúsculas equivalentes y conserva copia before-v4. Productos mantienen el nombre de categoría en su snapshot; renombrar categoría actualiza todas las asignaciones en una transacción. Escritura de producto valida categoría activa o conservación de una categoría inactiva ya asignada. Sin borrado físico de categorías.

BusinessSettings añade DarkMode/AccentColor con valores predeterminados para documentos antiguos. Colores #RRGGBB validados, recursos WPF dinámicos y controles adaptados a ambos modos. Recursos de color se reemplazan porque WPF puede congelar brushes; texto/botones ajustan contraste. Configuración agrupa negocio, apariencia, usuarios, respaldos y actualizaciones. Permisos previos conservados.

Escáner: ventana WPF modal enfoca entrada de teclado; Enter/Tab confirma código, Escape/cancelación/timeout evita alterar el campo. Admite lectores HID o Bluetooth que escriben como teclado. No usa SDK de dispositivo ni cámara y no activa físicamente el lector. No se dispone de lector para prueba real.
## Importación de catálogo
ProductImport (Application) genera una vista previa tipada y aplica validaciones de producto compartidas. ProductImportReader (Infrastructure) lee CSV y Open XML sin Excel instalado, con límites de archivo y XML sin DTD. ImportProducts (SQLite) exige permisos y guarda categorías, productos y auditoría en una transacción inmediata, comprobando cambios desde la vista previa. La interfaz crea respaldo previo y exige opciones explícitas para actualizar o crear categorías. No hay cambio de esquema: se conserva SQLite v4.

## Detección 0.6
UpdateChecker consulta solo metadatos HTTPS y valida UpdateCatalog RSA con clave pública incorporada; separa estado no publicado/error de estado verificado sin novedades. UI en segundo plano con cancelación, timer y consulta manual. No usa el instalador desde la interfaz actual. Catálogo sin Latest permite preparar el canal sin paquetes. Datos de negocio y versión SQL no cambian.

## Extensión de red 0.7

Véase [RED.md](RED.md) para el gateway, servicio HTTPS, identidad de caja, idempotencia transaccional, migración v5, reversión en la caja operadora y restauración central. Las deudas y descuentos siguen en USD; el servidor calcula costos, tasas y saldos. Cajas independientes predeterminadas; cambio de modalidad solo con todas las cajas cerradas.
