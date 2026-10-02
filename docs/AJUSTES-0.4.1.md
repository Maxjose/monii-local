# Monii 0.4.1 — Ajustes de configuración
- Monedas y tasas tiene una sección independiente dentro de Configuración.
- Negocio y Monedas guardan solo sus propios campos sobre las preferencias actuales, evitando sobrescribir cambios de otras secciones.
- Cobros y catálogo solo muestran monedas activas; el producto conserva costo y precio base USD.
- Apertura de caja oculta fondos en monedas desactivadas. Cierre y efectivo esperado conservan una moneda desactivada si tiene saldo por conciliar. Historial y documentos existentes conservan las monedas originales.
- La paleta deriva fondos, superficies, bordes, encabezados, selecciones y menú lateral del color principal, con modos claro y oscuro y contraste del acento.
- Configuración tiene secciones verticales y margen para la barra de desplazamiento, con contenido adaptable al ancho.
- No requiere migración nueva ni altera datos financieros existentes.
## Verificación
144 comprobaciones de integración; 48 comprobaciones WPF (incluye apertura/cierre solo USD, opciones de pago activas, paleta armonizada y sección de monedas).
Capturas en artifacts/settings-portable-review; compilación portable en artifacts/Monii-0.4.1-win-x64.
La interfaz se revisó mediante capturas WPF a 960 × 640. La prueba física de lector y Windows 10 siguen pendientes.