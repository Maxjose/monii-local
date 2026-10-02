# Lotes y vencimientos

## Activar
En **Configuración > Negocio**, activa **Inventario** y **Lotes y vencimientos**. El perfil víveres propone lotes activados; también se pueden usar en los otros perfiles.

## Recibir y clasificar
- En **Compras > Nueva compra**, cada línea admite código de lote y vencimiento opcional. Para recibir el mismo producto en dos lotes, agrégalo una vez por lote. La fecha exige código; un lote sin fecha sirve para trazabilidad sin caducidad.
- En **Inventario > Registrar ajuste**, indica lote y fecha para entradas o salidas específicas. Para retirar vencidos, registra cantidad negativa, lote, fecha registrada y motivo.
- En **Inventario > Clasificar existencias sin lote**, asigna parte del saldo anterior a un lote sin cambiar las existencias totales. No es una entrada de mercancía ni una compra.
- En **Inventario > Lotes y vencimientos**, busca producto/código/lote y consulta cantidad, fecha y estado. Puedes incluir lotes sin saldo para consultar el historial. Los movimientos y detalles de compra conservan los datos del lote.

## Ventas y reversión
Monii elige automáticamente los lotes vigentes por vencimiento más próximo (FEFO); después usa lotes sin fecha y existencias sin clasificar. Un lote es vendible durante su fecha de vencimiento; al día siguiente queda bloqueado. Las unidades vencidas siguen formando parte del stock físico, pero no del saldo vendible. En red, la validación la hace el principal.

El detalle de venta identifica los lotes entregados. Anular o devolver repone los lotes originales, incluso si ahora están vencidos; volver a venderlos seguirá bloqueado. Anular una compra exige cantidades disponibles en los mismos lotes recibidos. El código se identifica por producto sin distinguir mayúsculas. La fecha de un lote registrado es inmutable; para otra fecha utiliza otro código.

## Datos existentes y respaldos
La migración a esquema 6 guarda una copia previa y conserva las existencias anteriores como «Sin lote». No inventa fechas para mercancía anterior. Clasifica esa mercancía antes de depender del control de vencidos. La migración conserva las devoluciones parciales antiguas y sus cantidades pendientes.

Los respaldos incluyen lotes, asignaciones y movimientos. Restaurar valida que no haya saldos negativos por lote ni reintegros duplicados. Desactivar lotes oculta las herramientas y conserva sus datos y bloqueo de vencidos. No se puede desactivar inventario mientras queden saldos en lotes identificados.

El costo mantiene la regla anterior de último costo de compra por producto; no se calcula un costo contable distinto por lote. El aviso «Vence pronto» contempla los próximos 30 días. No incluye notificaciones externas ni selección manual de lote en ventas. Actualiza el cliente y el servidor con la misma compilación antes de usar esta función en red.
