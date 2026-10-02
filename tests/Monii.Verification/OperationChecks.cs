using Microsoft.Data.Sqlite;
using Monii.Application;
using Monii.Domain;
using Monii.Infrastructure;

internal static class OperationChecks
{
    public static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "MoniiOperations", Guid.NewGuid().ToString("N"));
        var store = new SqliteStore(Path.Combine(root, "business.db"));
        var business = new BusinessService(store); var ops = new OperationsService(store); var passed = 0;
        void Check(bool condition, string label) { if (!condition) throw new Exception("FALLÓ: " + label); Console.WriteLine("OK OPERACIONES: " + label); passed++; }
        void Reject(Action action, string label) { try { action(); } catch (ArgumentException) { Check(true, label); return; } throw new Exception("FALLÓ: " + label); }
        var product = new Product { Name = "Producto operativo", Code = "OP-1", CostUsd = 3, PriceUsd = 10, MinimumStock = 5 };
        business.Save(product);
        // Recreate a real version-1 layout; the constructor must preserve its catalogue on upgrade.
        LegacyFixture.Downgrade(store, 1);
        store = new SqliteStore(store.DatabasePath); business = new(store); ops = new(store);
        Check(business.Products().Single() == product, "Migración v1→v2 conserva catálogo");
        Check(Directory.GetFiles(root, "*.before-v2-*.db").Length == 1, "Migración conserva copia previa del esquema v1");
        business.SaveSettings(business.Settings with { Cash = false, ShowBcv = true, BcvRate = 50, ShowManualVes = true, ManualVesRate = 60, ShowCop = true, CopRate = 4000 });
        ops.Adjust(product.Id, 50, "Carga inicial");
        Check(OperationsService.Stock(ops.State, product.Id) == 50, "Entrada inicial");
        Reject(() => ops.Adjust(product.Id, -51, "Falla"), "Rechazo de stock negativo");
        Reject(() => ops.Adjust(product.Id, 0.5m, "Falla"), "Unidades enteras");
        Reject(() => ops.Adjust(product.Id, 1, ""), "Motivo obligatorio");
        Check(ops.State.Stock.Count == 1, "Ajustes rechazados no dejan movimientos");
        var weight = product with { Id = Guid.NewGuid(), Code = "PESO", Unit = "Kilogramo" }; business.Save(weight); ops.Adjust(weight.Id, 1.125m, "Carga peso");
        Check(OperationsService.Stock(ops.State, weight.Id) == 1.125m, "Cantidad fraccionaria exacta");
        Console.WriteLine("GATE INVENTARIO: aprobado");

        var sale = ops.Sell([(product.Id, 2)], 2, [new(Currency.USD, PaymentMethod.Transferencia, 18)]);
        Check(sale.Total == 18 && sale.InitialDebt == 0 && OperationsService.Stock(ops.State, product.Id) == 48, "Venta descuenta stock y aplica descuento");
        Reject(() => ops.Sell([(product.Id, 49)], 0, [new(Currency.USD, PaymentMethod.Efectivo, 490)]), "Venta sin existencias se rechaza");
        Reject(() => ops.Sell([(product.Id, 1)], 11, []), "Descuento mayor al subtotal se rechaza");
        Check(ops.State.Sales.Count == 1 && OperationsService.Stock(ops.State, product.Id) == 48, "Venta rechazada no deja documento ni stock parcial");
        Reject(() => ops.Sell([(weight.Id, 0.125m), (product.Id, 49)], 0, [new(Currency.USD, PaymentMethod.Transferencia, 500)]), "Fallo en segunda línea revierte la primera");
        Check(OperationsService.Stock(ops.State, weight.Id) == 1.125m, "Rollback de múltiples líneas conserva todos los saldos");
        var mixed = ops.Sell([(product.Id, 1)], 0, [new(Currency.USD, PaymentMethod.Efectivo, 5), new(Currency.VES_BCV, PaymentMethod.Transferencia, 250)]);
        business.SaveSettings(business.Settings with { BcvRate = 100 });
        Check(ops.State.Sales.Single(s => s.Id == mixed.Id).Rates.Bcv == 50 && mixed.Total == 10, "Venta conserva tasa histórica");
        ops.VoidSale(sale.Id, "Corrección");
        Check(OperationsService.Stock(ops.State, product.Id) == 49 && ops.State.Sales.Single(s => s.Id == sale.Id).Voided, "Anulación restaura stock y conserva venta");
        Reject(() => ops.VoidSale(sale.Id, "Duplicada"), "No se anula dos veces");
        ops = new(new SqliteStore(store.DatabasePath)); Check(ops.State.Sales.Count == 2, "Reapertura conserva operaciones");
        Console.WriteLine("GATE VENTAS: aprobado");

        business.SaveSettings(business.Settings with { Cash = true });
        Reject(() => ops.Sell([(product.Id, 1)], 0, [new(Currency.USD, PaymentMethod.Efectivo, 10)]), "Venta requiere apertura de caja");
        Check(OperationsService.Stock(ops.State, product.Id) == 49, "Fallo de caja revierte descuento de stock");
        ops.OpenCash(1000, 10000, 100000);
        Reject(() => ops.OpenCash(0, 0, 0), "No hay dos sesiones abiertas");
        var changeSale = ops.Sell([(product.Id, 1)], 0, [new(Currency.USD, PaymentMethod.Efectivo, 20)]);
        Check(changeSale.ChangeUsd == 10 && OperationsService.Expected(ops.State, OperationsService.OpenSession(ops.State)!, "USD") == 1010, "Cambio USD y caja conciliados");
        ops.VoidSale(changeSale.Id, "Devolución completa");
        Check(OperationsService.Expected(ops.State, OperationsService.OpenSession(ops.State)!, "USD") == 1000, "Anulación devuelve pago neto");
        Reject(() => business.SaveSettings(business.Settings with { Cash = false }), "No desactivar caja abierta");
        Reject(() => ops.CashMovement(new(Currency.USD, PaymentMethod.Efectivo, 1001), true, "Gasto excesivo"), "No permitir egreso superior al efectivo");
        ops.CashMovement(new(Currency.USD, PaymentMethod.Efectivo, 10), true, "Gasto");
        ops.CloseCash(989, 10000, 100000);
        var closed = ops.State.Sessions.Single(); Check(closed.ExpectedUsd == 990 && closed.CountedUsd - closed.ExpectedUsd == -1, "Cierre conserva contado y diferencia");
        ops.OpenCash(1000, 10000, 100000);
        Console.WriteLine("GATE CAJA: aprobado");

        var supplier = new Contact { Name = "Proveedor prueba" }; ops.SaveContact(supplier, true);
        var purchase = ops.Buy(supplier.Id, "COMPRA-1", [(product.Id, 3, 4)], new(Currency.USD, PaymentMethod.Efectivo, 12));
        Check(OperationsService.Stock(ops.State, product.Id) == 52 && business.Products().Single(p => p.Id == product.Id).CostUsd == 4, "Compra actualiza stock y costo junto al pago");
        Reject(() => ops.Buy(supplier.Id, "FALLA", [(product.Id, 3, 9)], new(Currency.USD, PaymentMethod.Efectivo, 1)), "Compra con pago inconsistente se rechaza");
        Check(business.Products().Single(p => p.Id == product.Id).CostUsd == 4 && ops.State.Purchases.Count == 1, "Compra rechazada no cambia costos");
        Check(ops.State.Sales.Single(s => s.Id == mixed.Id).Lines[0].Cost == 3 && OperationsService.Margin(mixed) == 7, "Cambio de costo no reescribe margen histórico");
        ops.VoidPurchase(purchase.Id, "Recepción incorrecta");
        Check(OperationsService.Stock(ops.State, product.Id) == 49 && OperationsService.Expected(ops.State, OperationsService.OpenSession(ops.State)!, "USD") == 1000, "Anular compra revierte stock y gasto");
        Console.WriteLine("GATE COMPRAS: aprobado");

        var customer = new Contact { Name = "Cliente prueba", CreditLimit = 100 }; ops.SaveContact(customer, false);
        var credit = ops.Sell([(product.Id, 2)], 0, [new(Currency.USD, PaymentMethod.Efectivo, 5)], customer.Id, DateOnly.FromDateTime(DateTime.Today.AddDays(30)));
        Check(credit.InitialDebt == 15 && OperationsService.Debt(ops.State, credit) == 15, "Venta parcial genera deuda USD");
        Reject(() => ops.Sell([(product.Id, 10)], 0, [], customer.Id, DateOnly.FromDateTime(DateTime.Today.AddDays(30))), "Límite de crédito efectivo");
        Reject(() => business.SaveSettings(business.Settings with { Credit = false }), "No ocultar créditos pendientes");
        Reject(() => ops.SaveContact(customer with { Active = false }, false), "No desactivar cliente con deuda");
        var abono = ops.PayDebt(credit.Id, new(Currency.VES_BCV, PaymentMethod.Efectivo, 500));
        Check(abono.Usd == 5 && OperationsService.Debt(ops.State, credit) == 10, "Abono con tasa vigente reduce deuda USD");
        Reject(() => ops.PayDebt(credit.Id, new(Currency.USD, PaymentMethod.Efectivo, 11)), "No permitir sobreabono");
        Reject(() => ops.VoidSale(credit.Id, "Falla"), "Venta con abonos exige reversarlos");
        ops.VoidDebtPayment(abono.Id, "Corrección"); Check(OperationsService.Debt(ops.State, credit) == 15, "Anular abono restaura deuda");
        ops.VoidSale(credit.Id, "Corrección"); Check(OperationsService.Debt(ops.State, ops.State.Sales.Single(s => s.Id == credit.Id)) == 0, "Anular crédito elimina saldo sin borrar historial");
        Console.WriteLine("GATE CRÉDITOS: aprobado");

        var backup = store.Backup(Path.Combine(root, "snapshot.db")); var before = OperationsService.Stock(ops.State, product.Id);
        ops.Adjust(product.Id, 7, "Después de respaldo");
        var recovery = store.Restore(backup);
        Check(OperationsService.Stock(ops.State, product.Id) == before && File.Exists(recovery), "Restauración recupera saldo y crea copia previa");
        using (var recovered = new SqliteConnection($"Data Source={recovery}")) { recovered.Open(); }
        var recoveryOps = new OperationsService(new SqliteStore(recovery)); Check(OperationsService.Stock(recoveryOps.State, product.Id) == before + 7, "Copia previa permite recuperar cambios reemplazados");
        File.WriteAllText(Path.Combine(root, "invalid.db"), "No es SQLite");
        try { store.Restore(Path.Combine(root, "invalid.db")); throw new Exception("Restauró un archivo inválido"); } catch (SqliteException) { Check(true, "Respaldo corrupto rechazado"); }
        Check(OperationsService.Stock(ops.State, product.Id) == before, "Restauración fallida no modifica base activa");
        store.AutomaticBackup(); store.AutomaticBackup(); Check(Directory.GetFiles(Path.Combine(root, "backups"), "monii-auto-*.db").Length == 1, "Respaldo automático diario sin duplicados");
        var future = Path.Combine(root, "future.db"); File.Copy(backup, future);
        using (var connection = new SqliteConnection($"Data Source={future}")) { connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "PRAGMA user_version=99"; command.ExecuteNonQuery(); }
        Reject(() => store.Restore(future), "Restauración rechaza esquema futuro");
        Check(OperationsService.Stock(ops.State, product.Id) == before, "Rechazo de versión futura conserva base actual");
        var recoveredV1 = Directory.GetFiles(root, "*.before-v2-*.db").Single(); var targetV1 = new SqliteStore(Path.Combine(root, "v1-restored.db")); targetV1.Restore(recoveredV1);
        Check(targetV1.GetProducts().Single() == product && targetV1.ReadOperations().Sales.Count == 0, "Restaurar respaldo v1 migra sin perder catálogo");
        var limited = product with { Id = Guid.NewGuid(), Code = "ULTIMO", PriceUsd = 1 }; business.Save(limited); ops.Adjust(limited.Id, 1, "Último stock");
        var attempts = Enumerable.Range(0, 2).Select(_ => Task.Run(() => { try { ops.Sell([(limited.Id, 1)], 0, [new(Currency.USD, PaymentMethod.Transferencia, 1)]); return true; } catch (ArgumentException) { return false; } })).ToArray();
        Task.WaitAll(attempts);
        Check(attempts.Count(t => t.Result) == 1 && OperationsService.Stock(ops.State, limited.Id) == 0, "Dos ventas concurrentes no consumen la misma última unidad");
        var free = ops.Buy(supplier.Id, "Muestra sin costo", [(limited.Id, 1, 0)], new(Currency.USD, PaymentMethod.Efectivo, 0));
        Check(free.Total == 0 && OperationsService.Stock(ops.State, limited.Id) == 1, "Recepción sin costo no requiere pago ficticio");
        ops.Sell([(limited.Id, 1)], 0, [new(Currency.USD, PaymentMethod.Transferencia, 1)]);
        Reject(() => ops.VoidPurchase(free.Id, "Ya vendida"), "No anular compra si su reversión deja stock negativo");
        Check(!ops.State.Purchases.Single(p => p.Id == free.Id).Voided && OperationsService.Stock(ops.State, limited.Id) == 0, "Anulación rechazada conserva documento y saldo");
        var vesSale = ops.Sell([(product.Id, 2)], 0, [new(Currency.VES_BCV, PaymentMethod.Efectivo, 1000), new(Currency.VES_Manual, PaymentMethod.Efectivo, 600)]);
        Check(OperationsService.Expected(ops.State, OperationsService.OpenSession(ops.State)!, "VES") == 11600, "Referencias BCV/manual comparten el efectivo físico en Bs");
        ops.VoidSale(vesSale.Id, "Reversión Bs");
        Check(OperationsService.Expected(ops.State, OperationsService.OpenSession(ops.State)!, "VES") == 10000, "Anulación respeta importes originales de ambas tasas");
        var badStatePath = Path.Combine(root, "bad-state.db"); File.Copy(backup, badStatePath);
        using (var connection = new SqliteConnection($"Data Source={badStatePath}"))
        {
            connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "SELECT payload FROM stock_moves LIMIT 1";
            var move = System.Text.Json.JsonSerializer.Deserialize<StockMove>((string)command.ExecuteScalar()!)!;
            command.CommandText = "UPDATE stock_moves SET payload=$payload,quantity_milli=-1000000 WHERE id=$id";
            command.Parameters.AddWithValue("$payload", System.Text.Json.JsonSerializer.Serialize(move with { Quantity = -1000 })); command.Parameters.AddWithValue("$id", move.Id.ToString()); command.ExecuteNonQuery();
        }
        Reject(() => store.Restore(badStatePath), "Respaldo íntegro en SQLite pero con saldos inválidos se rechaza");
        Console.WriteLine($"GATE RESPALDOS: aprobado. OPERACIONES: {passed} comprobaciones.");
    }
}
