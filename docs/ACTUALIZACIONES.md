# Actualizaciones de Monii — detección en GitHub

## Estado de Monii 0.6
Operativo: consulta de manifiesto firmado, detección de una versión superior, notas de versión, estado de consulta y aviso dentro de Monii.
Por pedido del usuario, esta entrega no descarga, importa ni instala paquetes desde la interfaz. El botón Actualizar programa solo aparece si se detecta una versión superior y permanece deshabilitado hasta implementar la instalación. El motor anterior de paquetes se conserva para una fase posterior.
No se creó ninguna Release ni un paquete de actualización 0.6. El manifiesto firmado vacío forma parte del estado del código preparado para GitHub.

## Uso
Configuración → Actualizaciones muestra:
- Versión instalada.
- Comprobar actualizaciones al abrir Monii (se puede desactivar; la elección se guarda automáticamente).
- Buscar actualizaciones, disponible aunque la consulta automática esté desactivada.
- Última comprobación y resultado durante la sesión.

La consulta automática solo se realiza al abrir Monii; no hay comprobaciones periódicas. Se realiza en segundo plano y tiene un límite de 20 segundos. No bloquea el inicio de ventas ni exige internet para usar Monii. Se cancela al cerrar la aplicación. Solo consulta un archivo JSON; no transmite datos del negocio ni descarga ejecutables.
El acceso a preferencias y consulta manual conserva el permiso de administrador. Las consultas automáticas pueden informar de disponibilidad en cualquier sesión.

La pantalla no muestra URL, manifiesto, firmas, repositorio ni detalles técnicos de errores. El origen se administra internamente.

## Repositorio y dirección preparada
Repositorio: https://github.com/Maxjose/monii-local
Rama comprobada: main. El repositorio es público.
Dirección predeterminada:
https://raw.githubusercontent.com/Maxjose/monii-local/main/updates/stable.json

La comprobación anterior al primer push devolvió 404. Cuando updates/stable.json esté en main, el detector podrá verificar que el canal todavía no tiene versiones publicadas.
Para habilitar la respuesta real de GitHub sin publicar una actualización, basta versionar updates/stable.json en la rama main. Está firmado y declara que no hay versiones publicadas.
No hace falta subir ningún ZIP ni ejecutar la instalación para esta preparación.

## Estados
| Estado | Qué significa |
|---|---|
| No hay versiones publicadas | Manifiesto y firma válidos; Latest es null |
| Al día | La versión firmada es igual o inferior a la instalada |
| Disponible | La versión firmada es superior; se muestran versión y notas |
| No publicado/no accesible | HTTP 404; no se puede confirmar disponibilidad |
| Error | Sin conexión, timeout, HTTP fallido, formato o firma inválidos; no se anuncia una actualización |

La última consulta se conserva durante la sesión; al volver a abrir se comprueba de nuevo si la opción automática está activa. No se presenta un resultado antiguo como una comprobación nueva.

## Archivos
- updates/catalog.source.json: contenido legible que se firma.
- updates/stable.json: sobre firmado preparado para GitHub.
- scripts/sign-update-catalog.ps1: firma local usando la clave privada original.
- src/Monii.Infrastructure/update-public.pem: clave pública que Monii incorpora para verificar.
- .tools/signing/update-private.pem: clave privada local, excluida por .gitignore. Nunca se sube ni se incluye en las compilaciones.

Contenido inicial del catálogo:
    { "Schema": 1, "Application": "Monii", "Channel": "stable", "Platform": "win-x64", "Latest": null }

El archivo publicado tiene dos propiedades: Manifest (catálogo codificado en Base64) y Signature (firma RSA SHA-256). Base64 no es cifrado; la firma asegura integridad y origen.
Se valida esquema, aplicación, canal estable, Windows x64, versión, SHA-256, tamaño y URL HTTPS de un eventual paquete. Los metadatos se validan aunque no se descargue nada.
No se aceptan URLs HTTP o credenciales dentro de la dirección del manifiesto. Se rechazan redirecciones en el cliente de producción. Máximo de manifiesto: 100.000 bytes.

## Preparar futuros anuncios (sin ejecutarlo ahora)
Un anuncio futuro reemplaza Latest con Version, Sha256, Size, PackageUrl y Notes. Estos datos deben corresponder al paquete real firmado, alojado posteriormente como archivo de una Release. La comparación admite versiones numéricas estables de .NET; 0.6.0 equivale a 0.6.0.0.
Para firmar, primero compilar; ejecutar scripts/sign-update-catalog.ps1 con Source y Output. El script no reemplaza un destino existente. Para un nuevo anuncio usar, por ejemplo, -Output artifacts/stable-next.json, revisarlo y después publicar ese archivo como updates/stable.json.
Conservar la clave privada original: cambiarla impediría verificar anuncios en clientes distribuidos.
Publicar anuncios y activar descarga/instalación siguen para una solicitud posterior.

## Pruebas
239 comprobaciones de integración aprobadas, incluidas 25 específicas de detección; 88 comprobaciones WPF en la versión portable.
Se verificaron manifiesto real firmado sin versiones, versión superior, equivalencia numérica, no downgrade, firma ajena/alterada, campos nulos, esquema/canal/plataforma incorrectos, 404, 503, HTML, límite de tamaño, dirección insegura, cancelación y red caída.
La interfaz se probó con respuestas controladas; los anuncios 9.0.0 son datos de demostración de pruebas y no existen en GitHub.
Comprobación real de la dirección actual: artifacts/live-update-check.txt.
Logs: artifacts/build-update-check.txt, artifacts/verification-update-check.txt y artifacts/update-check-portable-review/ui-verification.txt.
Portable: artifacts/Monii-0.6.0-win-x64/Monii.exe.
No se requieren migraciones SQL nuevas; la preferencia automática se añade a la configuración JSON sin borrar preferencias ni operaciones.