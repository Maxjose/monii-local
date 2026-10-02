# Entrega Monii 0.3

Fecha: 1 de octubre de 2026. Sin publicación ni despliegue externos. Tickets y validación/distribución Windows 10 aplazados por el usuario.

## Implementado y operativo

| Bloque | Resultado |
|---|---|
| Usuarios | Primera cuenta de administrador, inicio/cambio de sesión, gestión de cuentas/roles/contraseñas, desactivación, protección del último administrador |
| Permisos | Administrador/cajero/vendedor; autorización al ejecutar operación, límites/deuda protegidos, costos ocultos al vendedor, auditoría/ventas con usuario |
| Persistencia | Migración v1/v2→v3 con respaldo; documentos/líneas/movimientos/contactos/caja/créditos/devoluciones en tablas con índices y relaciones; escrituras solo de registros cambiados |
| Consultas | Historial de ventas paginado SQL, búsqueda literal y stock agrupado SQL |
| Devoluciones | Parciales, cantidades pendientes, motivo, vista previa, descuento proporcional, reposición, reducción de deuda y reintegro USD; rollback sin efectivo |
| Reportes | Total/margen netos, usuario responsable, agregado por usuario, devoluciones por fecha, CSV y comprobante con saldo/neto |
| Tasas | BCV y TRM Colombia oficiales por HTTPS; inicio/cada 60 minutos/botón, fecha/origen, fuentes independientes y última tasa sin red; Bs manual preservado |
| Actualizaciones | Paquetes RSA/SHA-256 firmados, importación local y cliente HTTPS, notas/tamaño/versión, backup, helper separado, nueva carpeta y conservación de versión anterior |

Continúan operativos catálogo/perfiles/módulos, inventario, ventas, caja, compras/proveedores, clientes/créditos y respaldos de 0.2. Restauración valida relaciones/saldos/devoluciones/coherencia de índices y exige autenticarse de nuevo. Los respaldos automáticos funcionan con cuentas de cajero/vendedor sin permitirles exportación manual.

## Verificaciones

- Release final: cero errores y cero advertencias.
- **128 comprobaciones de integración**: 20 catálogo/configuración, 55 operativas anteriores y 53 nuevas.
- Devoluciones con descuento, deuda/reintegro, centavos extremos, cantidades fraccionarias, costo acumulativo, stock y caja conciliados. Reintegro fallido revierte documento/stock.
- Migración de historial real v2→v3, lectura de respaldos antiguos, paginación y stock exacto.
- Permisos comprobados llamando directamente al servicio; vendedor no crea deuda, amplía límites, ajusta, anula ni exporta base. Último administrador, inactivos, bloqueo tras errores y edición sin contraseña probados.
- Respaldos con índices o devoluciones alteradas rechazados; restore fallido conserva base/sesión y restore correcto invalida sesión.
- Respuestas BCV/TRM válidas, vencidas/futuras, cambio de formato y sin conexión. Una fuente fallida no bloquea la otra.
- **Consulta real exitosa de ambas fuentes** en datos temporales: fecha de vigencia 01/10/2026. Log artifacts/live-rates-03.txt.
- Firmas válidas/ajenas, paquete alterado, rutas de extracción fuera del destino y limpieza tras error.
- **32 comprobaciones WPF**: 24 anteriores y 8 nuevas. Primera creación de administrador, inicio de sesión, rol/navegación/costos, devolución con vista previa, historial y actualización condicionada a verificación.
- Generación y firma del ZIP real, instalación con Monii.UpdateTool real en carpeta aislada, y verificación WPF del ejecutable instalado con runtime incluido: 32 comprobaciones aprobadas.
- Imágenes de usuarios, devolución y actualización inspeccionadas. Renderizadas por WPF; no capturas de escritorio. Diálogos nativos de archivos, confirmación de MessageBox y reinicio desde botón quedan para prueba manual.
- No se modificó la base real del negocio durante las verificaciones.

## Abrir y revisar

Portable final: **artifacts/Monii-0.3.0-win-x64/Monii.exe**, conservar su carpeta completa. La carpeta antigua Monii-0.3-win-x64 tenía una instancia abierta que bloqueaba archivos; se generó una carpeta nueva sin interrumpirla. Usa la carpeta final indicada.

Crear administrador en primera apertura; no hay contraseña predeterminada. Una base existente se migra conservando copia previa. Para explorar sin datos reales utiliza --data-dir con carpeta nueva.

Evidencia:
- artifacts/build-03-final.txt
- artifacts/verification-03.txt
- artifacts/live-rates-03.txt
- artifacts/phase3-portable-review/ui-verification.txt e imágenes
- artifacts/Monii-0.3.0-update.zip y .zip.json
- artifacts/update-install-bfc948e24a1448c18735d584b4354237/resultado-actualizacion.txt

El ZIP 0.3.0 no actualiza otra instalación ya en 0.3.0: el cliente exige una versión posterior. Sirve como artefacto firmado de esta entrega y para verificar el helper; versiones futuras se generan siguiendo ACTUALIZACIONES.md.

## Decisiones que necesita revisar el propietario

1. **Roles:** cajero puede abrir/cerrar caja, modificar límites y cobrar deuda; compras/gastos/ajustes/anulaciones/devoluciones requieren administrador.
2. **Devolución:** reduce deuda primero y reintegra excedente en USD; no hay cambio/reintegro físico en VES/COP en este flujo.
3. **Costos:** último costo de compra, margen histórico por snapshot; anular compra no revierte costo del catálogo.
4. **Créditos:** moneda USD, sin intereses; pagos alternativos convertidos al recibirlos.
5. **Reportes:** rango por fecha original de venta, neto de sus devoluciones hasta hoy. Devoluciones se consultan también por fecha propia; no es cierre fiscal congelado.
6. **Recuperación:** venta con devoluciones y abonos anteriores no admite anulación; reversar una devolución registrada todavía requiere nueva operación comercial.
7. **Distribución:** hosting/feed, resguardo de clave privada y certificado Authenticode por decidir antes de entregar actualizaciones a clientes.

## Límites y próximos pasos

- Demo sigue siendo prototipo sin persistencia. Lotes/vencimientos y tickets solo conservan preferencias; no están operativos.
- Tickets y Windows 10 aplazados expresamente. Instalador comercial, firma Authenticode, periféricos/matriz DPI y pilotos pendientes.
- Actualizador local operativo; no servidor publicado ni feed de producción, por lo que no se probó distribución HTTPS a clientes extremo a extremo. No hay downgrade automático de esquema; recuperación documentada.
- Persistencia normalizada operativa, pero los casos de uso aún cargan el historial completo para validar dentro de la transacción. Mejorar consultas específicas y pruebas de carga antes de grandes volúmenes/red.
- Base no cifrada; roles protegen acciones dentro de Monii, no modificaciones externas del archivo SQLite.
- Importación, lotes, cuentas por pagar/devoluciones de compra, reportes por producto y varias computadoras siguen en el plan futuro.

No hay bloqueo que impida probar el portable local. Próximo paso: revisar estas reglas con un negocio de prueba y cerrar decisiones comerciales; después optimizar consultas/carga antes de ampliar módulos.

[Guía](../README.md) · [Plan](PLAN.md) · [Arquitectura](ARQUITECTURA.md) · [Reglas](REGLAS-FINANCIERAS.md) · [Actualizaciones](ACTUALIZACIONES.md)