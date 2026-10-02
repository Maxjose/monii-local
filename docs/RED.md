# Monii en varios equipos — decisiones y plan 0.7

## Arquitectura
La misma interfaz admite modo local, equipo principal y caja conectada. Un servicio Windows ASP.NET Core mantiene SQLite en el principal; ningún cliente abre la base a través de una carpeta compartida. El servicio continúa al cerrar la ventana. API HTTPS con certificado propio y huella SHA-256 fijada al configurar cada caja. Las contraseñas viajan cifradas; los tokens caducan y se revocan al cambiar la cuenta o restaurar.

## Reglas financieras
Se mantienen costos de última compra, descuentos USD, deudas USD y conversiones por las tasas del servidor registradas con cada documento. Las cajas no envían totales calculados ni saldos; el servidor calcula y valida. Caja independiente por terminal es la modalidad inicial. Caja compartida es configurable por administrador únicamente con todas las cajas cerradas. Historial anterior conserva su modalidad. La anulación o devolución sale del efectivo de la caja que realiza la acción, con permiso de administrador y validación de disponibilidad; no altera cierres anteriores.

Inventario y límites de crédito se validan dentro de la transacción central, incluida la última unidad. Venta, stock, efectivo, auditoría y comprobante de idempotencia se confirman juntos. Una petición financiera conserva su ID hasta obtener respuesta definitiva; un reintento devuelve el mismo resultado. Se bloquean nuevas mutaciones del terminal cuando hay una respuesta incierta. No hay ventas sin conexión ni sincronización posterior en esta entrega.

## Migración y operación
Respaldo previo y migración v4 a v5: índice de caja abierta por ámbito y comprobantes de peticiones. Los documentos antiguos permanecen; caja local abierta debe cerrarse antes de pasar a red. Instalación de servicio y regla de firewall requieren elevación Windows. La importación inicial usa un respaldo consistente y nunca sobrescribe una base de servidor existente. Restauración central requiere todas las cajas cerradas, mantenimiento exclusivo y revoca sesiones. Respaldos se descargan a la caja administradora y la restauración sube el archivo; no se aceptan rutas arbitrarias del cliente.

## Verificación prevista
Compilación; pruebas existentes; HTTP real con dos terminales; autenticación/permisos; caja compartida e independiente; ventas concurrentes; reintentos; crédito y anulaciones; migración y restauración. Portable local con servicio y herramientas de instalación. Validación física en dos equipos y Windows 10 pendiente; sin tickets, releases ni despliegue.


## Uso del portable
1. Conserva completa la carpeta `Monii-0.7.0-win-x64`. En el principal abre `Monii.exe` en modo de una computadora y crea el administrador si aún no existe.
2. Cierra las cajas abiertas. En **Configuración > Conexión**, pulsa **Preparar este equipo como principal**. Confirma la instalación y la solicitud de administrador de Windows. Se conserva la base local y se importa su respaldo al servidor nuevo; un servidor existente conserva sus propios datos.
3. Abre Monii de nuevo. Ya trabaja conectado al servicio. En Configuración > Conexión consulta el estado, selecciona cajas independientes o compartida y guarda las instrucciones de conexión.
4. Copia el portable completo a cada caja. En su primera apertura elige **Conectar al equipo principal**. Escribe su nombre, la dirección y la huella entregadas por el administrador. Usa `https://IP-del-principal:58443` si el nombre de equipo no se resuelve. No copies `connection.json` entre cajas: cada equipo conserva un identificador propio.
5. Inicia sesión con una cuenta creada por el administrador. En aperturas posteriores se muestra directamente el acceso. El administrador también puede vender desde su equipo con su propia caja.

Todos los equipos deben estar en la misma red privada de Windows. La regla de instalación permite solo la subred local y el puerto 58443 en perfil privado. Reserva la IP del principal en el router si usas una dirección IP. Cerrar Monii no apaga el servicio; apagar el principal sí interrumpe las cajas. Inicio/reinicio/detención están en Configuración > Conexión del principal y necesitan permisos de Windows; la detención solicita confirmación. Si el servicio está detenido, la pantalla de acceso del principal permite iniciarlo.

## Desconexión
Si se pierde la respuesta de una operación financiera, se guarda una petición pendiente en el equipo. Ve a **Inicio > Reconciliar operación pendiente** con el mismo usuario que la realizó; también está en Configuración > Conexión para administradores. Está disponible para cajeros sin darles permisos de configuración. Recuperar una operación confirmada devuelve el mismo documento; recuperar una que no llegó la ejecuta una vez. Revisa el historial antes de repetir una venta. No borres `pending-operation.json` ni cambies de servidor mientras exista una petición pendiente.

## Datos y mantenimiento
- Local/cajas: `%LOCALAPPDATA%\Monii\connection.json`; local además `monii.db`.
- Servidor: `%PROGRAMDATA%\MoniiServer\data\monii.db`, certificado `server.pfx`, configuración `server.json` y respaldos automáticos. El directorio tiene permisos para Administradores, SYSTEM y LocalService.
- Servicio: `MoniiServer`, inicio automático, identidad LocalService y reinicio por Windows si falla.
- Diagnóstico: `%PROGRAMDATA%\MoniiServer\logs` y `installation.log`.
- Claves privadas, bases, configuraciones de equipo y binarios se excluyen de Git.
- Restauración central: se sube y valida el respaldo (máximo 100 MB), exige todas las cajas actuales cerradas y un administrador activo en el respaldo, crea copia previa y cierra todas las sesiones. Las peticiones del historial anterior se rechazan. Un fallo incierto de restauración exige volver a ingresar y revisar la base antes de repetirla.
- Cambiar a modo local conserva la base local previa; no copia ni fusiona automáticamente los datos centrales. Respaldos y recuperación del servidor se administran antes de retirar el equipo principal.
- Las preferencias del negocio, módulos, monedas y apariencia son compartidas. La dirección, huella y nombre/identificador de caja son propios de cada equipo.

## Verificaciones y límites
Se verificó HTTPS real en el mismo Windows con varios clientes, incluida una instancia WPF separada; apertura/venta/cierre, compras, abonos, devoluciones, anulaciones, tasa COP, permisos, última unidad, importación, respaldo/restauración, cambio de modalidad, reinicio y desconexión. El servidor permaneció disponible después de cerrar la interfaz.

La instalación y los controles de SCM/firewall están implementados y los scripts analizados. No se instaló un servicio permanente ni se modificó el firewall de este equipo. La sesión no tiene elevación de administrador de Windows: falta probar el alta real del servicio, reinicio de Windows y conexión en dos equipos físicos. La prueba HTTPS se ejecutó con un servidor de consola aislado. Windows 10 y tickets siguen aplazados.

Esta entrega está pensada para 2–3 equipos en una LAN; no incluye modo sin conexión, acceso por internet ni pruebas de carga de negocios grandes. La API utiliza lecturas completas de operaciones para algunas pantallas; paginación de todos los reportes y optimización de grandes historiales quedan para una entrega posterior.

Referencia técnica del certificado en Windows: [Microsoft — diagnóstico de SslStream](https://learn.microsoft.com/en-us/dotnet/core/extensions/sslstream-troubleshooting). Se usa almacenamiento de clave de máquina durante la ejecución TLS; la huella del certificado se comprueba en cada conexión.

## Desinstalar el servidor
En el equipo principal, un administrador de Monii puede usar **Configuración > Conexión > Desinstalar servidor**. Primero debe cerrar todas las cajas y reconciliar operaciones pendientes. La acción solicita confirmación y elevación de Windows.

Se detiene y elimina el servicio y su regla de firewall, y se retiran los binarios instalados. Antes de eliminarlo, Monii valida y copia la base central al equipo principal para continuar en modo local con sus datos actuales. La base local anterior se conserva como `monii-before-server-uninstall-*.db`. Los datos originales, certificados, respaldos y registros del servidor se conservan en `%PROGRAMDATA%\MoniiServer`. Las otras cajas dejarán de conectarse. Monii se cierra al finalizar: ábrelo nuevamente e ingresa con los usuarios recuperados del servidor.

Si la copia o validación falla, no se elimina el servicio; se intenta reanudarlo si estaba funcionando. Si falla la limpieza después de recuperar los datos, consulta `uninstall.log`: puede quedar material instalado pendiente de retirar. No borres los respaldos hasta verificar la recuperación.

Cerrar sesión oculta y limpia la ventana principal mientras aparece el acceso. Al ingresar de nuevo, la ventana principal vuelve a mostrarse; cerrar la ventana de acceso termina Monii.

## Corrección: servidor instalado anterior al perfil básico (2 de octubre de 2026)
Se detectó una instalación activa cuyos archivos difieren del portable nuevo. La instalación anterior conserva su copia de los binarios; reiniciar por sí solo no la actualiza. Esto puede producir «Perfil de negocio inválido» cuando el cliente nuevo envía Básico al servidor anterior.

En el principal, abre el portable nuevo y usa Configuración > Conexión > Actualizar servidor instalado. Exige administrador de Monii/Windows, confirmación y todas las cajas cerradas. Crea respaldo de base sin migrar el origen y copia de los binarios; reemplaza runtime, prepara migraciones y reinicia. Si falla antes de iniciar, restaura base/binarios previos; después de intentar iniciar no reemplaza automáticamente la base para evitar perder operaciones nuevas. Registros en maintenance.log y copias protegidas en ProgramData/MoniiServer/maintenance. La instalación sobre servicio existente también usa este mantenimiento.

Reabre Monii e ingresa antes de seleccionar Básico. La interfaz reconoce el rechazo del servidor antiguo y muestra una indicación concreta. Compilación y prueba HTTPS de respaldo/recuperación verificadas con datos aislados; sintaxis de scripts comprobada. No se actualizó el servicio real desde esta sesión: la acción requiere elevación Windows y debe hacerse con cajas cerradas.

Portable de corrección: artifacts/Monii-0.7.0-corregido-win-x64/Monii.exe. Se generó por separado porque la aplicación abierta bloqueaba archivos del portable anterior. Prueba HTTPS: 68 comprobaciones aprobadas. El servicio instalado permanece pendiente de mantenimiento autorizado con elevación Windows.

## Búsqueda automática del principal
En otra computadora, elige «Conectar al equipo principal». Monii busca durante tres segundos, presenta los servidores encontrados y completa la dirección y huella al seleccionar uno. La primera vez compara el código corto con el mostrado en Configuración > Conexión del principal; confirma solo si coincide. No se escribe la huella. Con la conexión guardada, las siguientes aperturas usan el acceso habitual sin volver a emparejar. Si cambia el certificado, requiere nueva confirmación.

El principal debe ejecutar el runtime nuevo: desde el portable actualizado usa «Actualizar servidor instalado». El mantenimiento añade la regla de descubrimiento UDP 58444 exclusivamente para red privada y subred local. Instalaciones nuevas la incluyen; desinstalar retira esa regla. HTTPS y la huella fijada siguen protegiendo el acceso a los datos.

La búsqueda usa broadcast IPv4 en la misma red/subred. Redes de invitados, aislamiento Wi-Fi, VLAN, redes públicas o firewall pueden impedirla; hay un apartado manual cerrado como alternativa. No hay búsqueda por internet. El protocolo usa un identificador aleatorio por búsqueda, valida tamaño/puerto/nombre/huella y acepta direcciones locales. Los anuncios no sustituyen la confirmación inicial del código; no contienen contraseñas ni tokens.

Verificado: responder UDP real en loopback, selección de dirección/huella, login HTTPS fijando certificado, rechazo de respuesta ajena a la búsqueda o dirección no local, y formulario de conexión que guarda sin escritura manual. Pendiente: broadcast y firewall entre dos equipos físicos. La regla y servicio reales requieren mantenimiento elevado por el administrador.
