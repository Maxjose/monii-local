# Monii 0.6 · gestión local de negocios

Aplicación Windows en español desarrollada con C#, .NET 10, WPF y SQLite. Actualmente funciona con una computadora y usuarios locales. El modo de servidor con varias cajas es la siguiente fase; todavía no está implementado.

## Funciones operativas
- Catálogo y categorías: creación, edición, búsqueda, códigos, costos/precios USD y desactivación con historial.
- Inventario, compras/proveedores, ventas, caja, clientes, créditos y abonos.
- Anulaciones, devoluciones parciales de ventas, reportes y auditoría.
- Monedas opcionales: bolívares BCV, bolívares manuales y pesos colombianos.
- Importación Excel .xlsx y CSV con vista previa, validaciones, actualización explícita y respaldo previo.
- Usuarios con permisos, cierre de sesión con confirmación y respaldos/restauración.
- Apariencia clara/oscura y paleta personalizable.
- Detección de actualizaciones firmadas al abrir (opcional) y manualmente. No descarga ni instala desde la interfaz actual.

## Ejecutar y compilar
Instalar el SDK .NET 10 en Windows. Los scripts también detectan un SDK local en .tools/dotnet; esa carpeta no se sube al repositorio.

Desde la raíz:
    .\scripts\build.ps1
    .\scripts\run.ps1
    .\scripts\verify-ui.ps1
    .\scripts\build.ps1 -Publish

La compilación portable se genera en artifacts/Monii-0.6.0-win-x64/Monii.exe. Conservar toda la carpeta. Incluye el runtime .NET; no está incluida en Git.
En la primera apertura se crea el administrador con contraseña de al menos 12 caracteres; no hay credenciales predeterminadas.
Para probar por separado:
    .\scripts\run.ps1 -DataDirectory 'D:\Ruta\PruebasMonii'

## Datos
Base predeterminada: %LOCALAPPDATA%\Monii\monii.db. Esquema SQLite 4 con migraciones y copia previa al actualizar desde versiones anteriores.
Las bases de datos, respaldos, binarios, archivos de prueba y herramientas locales se excluyen de Git.
Desactivar productos o módulos conserva el historial. Caja abierta y deudas pendientes impiden desactivar funciones necesarias.
Los datos locales no están cifrados: se requiere controlar el acceso al equipo y a sus respaldos.

## Monedas y tasas
USD es la moneda base. Monedas y tasas se gestionan en Configuración. Las operaciones conservan sus conversiones históricas y solo ofrecen monedas activas.
La consulta automática de tasas opera al abrir y cada 60 minutos cuando está habilitada; también hay consulta manual.
Limitación detectada: si BCV publica una tasa con vigencia del día siguiente, se conserva la tasa previa y no se aplica la futura. La mejora de ese caso sigue pendiente.
La tasa manual de bolívares no se sustituye por la consulta oficial.

## Actualizaciones
Configuración → Actualizaciones muestra versión, estado, comprobación opcional al abrir y botón Buscar actualizaciones.
El origen se administra internamente; no se muestran URLs ni controles técnicos.
updates/stable.json es un manifiesto firmado sin versiones publicadas. Subirlo a main habilita la consulta del canal sin publicar paquetes.
La clave pública está incorporada en el código. La clave privada original permanece fuera de Git, en .tools/signing; debe conservarse para anuncios futuros.
No se publica ninguna Release ni ZIP de actualización en esta etapa.

## Verificación y documentación
Última entrega verificada: 239 comprobaciones de integración y 88 WPF en bases separadas. Windows 10, impresora y lector físicos siguen pendientes.

- [Plan](docs/PLAN.md)
- [Arquitectura](docs/ARQUITECTURA.md)
- [Informe de entrega](docs/ENTREGA.md)
- [Reglas financieras](docs/REGLAS-FINANCIERAS.md)
- [Importación](docs/IMPORTACION.md)
- [Actualizaciones](docs/ACTUALIZACIONES.md)
- [Informe de pruebas](docs/PRUEBAS-2026-10-01.md)

## Próxima fase acordada
Mismo Monii en todos los equipos, con servicio de servidor en el equipo principal y clientes en la red local.
Caja compartida o cajas independientes, configurable por el administrador; por defecto independientes. Cambiar de modalidad exige cerrar todas las cajas.
Esta fase comienza después de versionar el estado actual.