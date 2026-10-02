using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Monii.Application;
using Monii.Domain;
using Microsoft.Win32;

namespace Monii.Desktop;

public partial class MainWindow
{
    private readonly ObservableCollection<CartLine> saleCart = [];

    private void Form(string title, StackPanel panel, Action commit, string saveLabel = "Guardar")
    {
        panel.Margin = new Thickness(24);
        panel.Children.Insert(0, Text(title, 24));
        var error = Text("", 13, "#B91C1C"); panel.Children.Add(error);
        var dialog = new Window { Title = "Monii · " + title, Owner = this, Width = 620, Height = Math.Min(740, SystemParameters.WorkArea.Height - 60), MinHeight = 400, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Brush("#F3F6FA") };
        var actions = new WrapPanel();
        actions.Children.Add(Button(saveLabel, () => { try { commit(); dialog.Close(); } catch (Exception exception) { error.Text = FriendlyError(exception); } }));
        actions.Children.Add(Button("Cancelar", () => dialog.Close())); panel.Children.Add(actions); dialog.Content = Scroll(panel); dialog.ShowDialog();
    }
    private void AskReason(string title, Action<string> commit)
    {
        var panel = new StackPanel(); var reason = Field(panel, "Motivo obligatorio", "");
        Form(title, panel, () => commit(reason.Text), "Confirmar anulación");
    }
    private static DataGrid Table(params (string Title, string Property, double Width)[] columns)
    {
        var grid = new DataGrid(); foreach (var c in columns) grid.Columns.Add(Column(c.Title, c.Property, c.Width)); return grid;
    }
    private static UIElement Page(Panel header, UIElement main) { var dock = new DockPanel(); DockPanel.SetDock(header, Dock.Top); dock.Children.Add(header); dock.Children.Add(main); return dock; }

    private UIElement Inventory()
    {
        var header = new StackPanel(); header.Children.Add(Text("Existencias y alertas", 20));
        var search = Field(header, "Buscar producto", "");
        var grid = Table(("Producto", "Name", 3), ("Código", "Code", 1), ("Unidad", "Unit", 1), ("Stock", "Quantity", 1), ("Mínimo", "Minimum", 1), ("Estado", "Alert", 1));
        void Reload() { var balances=storage.StockBalances(); grid.ItemsSource = service.Products(search.Text, true).Select(p => new StockRow(p.Id, p.Name, p.Code, p.Unit, balances.GetValueOrDefault(p.Id), p.MinimumStock, !p.Active ? "Inactivo" : balances.GetValueOrDefault(p.Id) <= p.MinimumStock ? "Stock bajo" : "Disponible")).ToList(); }
        var actions = new WrapPanel();
        actions.Children.Add(Button("Registrar ajuste", () =>
        {
            if (grid.SelectedItem is not StockRow row) { Status.Text = "Selecciona un producto."; return; }
            var panel = new StackPanel(); panel.Children.Add(Text(row.Name));
            var delta = Field(panel, "Cantidad (+ entrada, − salida)", "0"); var reason = Field(panel, "Motivo", "");
            Form("Ajuste de inventario", panel, () => { operations.Adjust(row.Id, ParseNumber(delta.Text, "cantidad"), reason.Text); Reload(); Status.Text = "Ajuste registrado."; });
        }));
        actions.Children.Add(Button("Ver movimientos", () =>
        {
            if (grid.SelectedItem is not StockRow row) { Status.Text = "Selecciona un producto."; return; }
            var panel = new StackPanel(); panel.Children.Add(Text(row.Name, 20));
            foreach (var move in operations.State.Stock.Where(m => m.ProductId == row.Id).OrderByDescending(m => m.At)) panel.Children.Add(Text($"{move.At.ToLocalTime():g} · {move.Quantity:+0.###;-0.###} · {move.Reason}"));
            Form("Movimientos", panel, () => { }, "Cerrar");
        }));
        header.Children.Add(actions); search.TextChanged += (_, _) => Reload(); Reload(); return Page(header, grid);
    }

    private UIElement Sales()
    {
        var tabs = new TabControl(); var panel = new DockPanel(); var header = new StackPanel();
        header.Children.Add(Text("Venta real · Se guarda al confirmar el cobro", 15, "#0F766E"));
        var currentRates=service.Settings;
        header.Children.Add(Text($"Tasas: BCV {currentRates.BcvSource} ({currentRates.BcvEffectiveDate?.ToString()??"sin fecha oficial"}) · COP {currentRates.CopSource} ({currentRates.CopEffectiveDate?.ToString()??"sin fecha oficial"}). Los cobros guardan la tasa vigente al confirmar.",12,"#64748B"));
        var search = Field(header, "Buscar producto / leer código de barras", "");
        var selector = new ComboBox { DisplayMemberPath = "Name" }; header.Children.Add(selector);
        void Options() { selector.ItemsSource = service.Products(search.Text); selector.SelectedIndex = 0; }
        search.TextChanged += (_, _) => Options(); Options();
        var quantity = Field(header, "Cantidad", "1");
        var total = Text("", 24); var conversions = Text("", 13, "#64748B"); var grid = Table(("Producto", "Name", 3), ("Cantidad", "Quantity", 1), ("Precio USD", "Price", 1), ("Total USD", "Total", 1)); grid.ItemsSource = saleCart;
        void Total()
        {
            var usd = saleCart.Sum(l => l.Total); var settings = service.Settings; var values = new List<string>();
            total.Text = $"Subtotal USD {usd:N2}";
            if (settings.ShowBcv) values.Add($"Bs BCV {BusinessService.ConvertPrice(usd, settings.BcvRate):N2}");
            if (settings.ShowManualVes) values.Add($"Bs manual {BusinessService.ConvertPrice(usd, settings.ManualVesRate):N2}");
            if (settings.ShowCop) values.Add($"COP {BusinessService.ConvertPrice(usd, settings.CopRate):N2}");
            conversions.Text = string.Join(" · ", values);
        }
        void Add()
        {
            if (selector.SelectedItem is not Product p) throw new ArgumentException("Selecciona un producto activo.");
            var qty = ParseNumber(quantity.Text, "cantidad"); OperationsService.Quantity(p, qty);
            var old = saleCart.FirstOrDefault(l => l.ProductId == p.Id);
            if (old is null) saleCart.Add(new(p.Id, p.Name, qty, p.PriceUsd));
            else { var sum = old.Quantity + qty; OperationsService.Quantity(p, sum); saleCart[saleCart.IndexOf(old)] = old with { Quantity = sum }; }
            search.Text = ""; quantity.Text = "1"; Total(); search.Focus();
        }
        search.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { Safe(Add); e.Handled = true; } };
        var actions = new WrapPanel(); actions.Children.Add(Button("Agregar al carrito", () => Safe(Add)));
        actions.Children.Add(Button("Quitar seleccionado", () => { if (grid.SelectedItem is CartLine row) saleCart.Remove(row); Total(); }));
        actions.Children.Add(Button("Vaciar", () => { saleCart.Clear(); Total(); }));
        actions.Children.Add(Button("Abrir demostración", () => Navigate("Venta demo"))); header.Children.Add(actions);
        DockPanel.SetDock(header, Dock.Top); panel.Children.Add(header);
        var footer = new StackPanel(); footer.Children.Add(total); footer.Children.Add(conversions); footer.Children.Add(Button("Cobrar / registrar crédito", () => Checkout(Total)));
        DockPanel.SetDock(footer, Dock.Bottom); panel.Children.Add(footer); panel.Children.Add(grid); Total();
        tabs.Items.Add(new TabItem { Header = "Nueva venta", Content = panel }); tabs.Items.Add(new TabItem { Header = "Historial y anulaciones", Content = SaleHistory() }); return tabs;
    }

    private PaymentInput PaymentFields(Panel panel, string title, decimal amount = 0)
    {
        panel.Children.Add(Text(title, 18));
        var currency = Choice(panel, "Moneda", Enum.GetValues<Currency>().Where(c => c == Currency.USD || c == Currency.VES_BCV && service.Settings.ShowBcv || c == Currency.VES_Manual && service.Settings.ShowManualVes || c == Currency.COP && service.Settings.ShowCop).Select(CurrencyLabel), "USD");
        var method = Choice(panel, "Medio", Enum.GetNames<PaymentMethod>(), "Efectivo");
        var value = Field(panel, "Importe recibido / pagado en esa moneda", amount.ToString("0.00", UiCulture));
        return new(currency, method, value);
    }
    private static string CurrencyLabel(Currency currency) => currency switch { Currency.USD => "USD", Currency.VES_BCV => "Bolívares · BCV", Currency.VES_Manual => "Bolívares · tasa manual", _ => "Pesos colombianos" };
    private static Payment ReadPayment(PaymentInput input) => new(Enum.GetValues<Currency>().Single(c => CurrencyLabel(c) == input.Currency.SelectedItem?.ToString()), Enum.Parse<PaymentMethod>(input.Method.SelectedItem?.ToString() ?? "Efectivo"), ParseNumber(input.Amount.Text, "pago"));

    private void Checkout(Action refresh)
    {
        if (saleCart.Count == 0) { Status.Text = "Agrega productos a la venta."; return; }
        var panel = new StackPanel(); var subtotal = saleCart.Sum(l => l.Total); panel.Children.Add(Text($"Subtotal USD {subtotal:N2}", 22));
        var discount = Field(panel, "Descuento total USD", "0");
        var payment1 = PaymentFields(panel, "Pago 1", subtotal); var payment2 = PaymentFields(panel, "Pago 2 opcional"); var payment3 = PaymentFields(panel, "Pago 3 opcional");
        var preview = Text("", 18, "#0F766E"); panel.Children.Add(preview);
        void Preview()
        {
            try
            {
                var total = subtotal - ParseNumber(discount.Text, "descuento");
                var paid = new[] { ReadPayment(payment1), ReadPayment(payment2), ReadPayment(payment3) }.Where(p => p.Amount != 0).Sum(p => OperationsService.Usd(p, service.Settings));
                preview.Text = $"Total USD {total:N2} · Pagado USD {paid:N2}\nCambio USD {Math.Max(0, paid-total):N2} · Pendiente USD {Math.Max(0,total-paid):N2}";
            }
            catch (ArgumentException) { preview.Text = "Revisa los importes y tasas del cobro."; }
        }
        discount.TextChanged += (_, _) => Preview();
        foreach (var payment in new[] { payment1, payment2, payment3 }) { payment.Amount.TextChanged += (_, _) => Preview(); payment.Currency.SelectionChanged += (_, _) => Preview(); }
        Preview();
        var credit = Check(panel, "Dejar saldo a crédito (requiere cliente y límite)", false); credit.IsEnabled=storage.Can(Permission.Credit)&&service.Settings.Credit;
        panel.Children.Add(Text("Cliente (opcional al contado)")); var customer = new ComboBox { ItemsSource = service.Settings.Customers ? operations.State.Customers.Where(c => c.Active).ToList() : [], DisplayMemberPath = "Name" }; panel.Children.Add(customer);
        panel.Children.Add(Text("Vencimiento del crédito")); var due = new DatePicker { SelectedDate = DateTime.Today.AddDays(30), Margin = new Thickness(0, 8, 0, 8) }; panel.Children.Add(due);
        panel.Children.Add(Text("El cambio se entrega en efectivo USD. Para crédito puedes poner los pagos en cero. Los importes en monedas alternativas se convierten con las tasas actuales.", 13, "#64748B"));
        Form("Confirmar venta", panel, () =>
        {
            var payments = new[] { ReadPayment(payment1), ReadPayment(payment2), ReadPayment(payment3) }.Where(p => p.Amount != 0).ToList();
            var sale = operations.Sell(saleCart.Select(l => (l.ProductId, l.Quantity)).ToList(), ParseNumber(discount.Text, "descuento"), payments, (customer.SelectedItem as Contact)?.Id, credit.IsChecked == true && due.SelectedDate is { } date ? DateOnly.FromDateTime(date) : null);
            saleCart.Clear(); refresh(); Navigate("Ventas"); Status.Text = $"Venta #{sale.Number} guardada · Total USD {sale.Total:N2} · Cambio USD {sale.ChangeUsd:N2} · Deuda USD {sale.InitialDebt:N2}";
        }, "Confirmar venta");
    }

    private UIElement SaleHistory()
    {
        var header = new StackPanel(); var search = Field(header, "Buscar número o cliente", "");
        var grid = Table(("Número", "Number", 1), ("Fecha", "At", 2), ("Cliente", "CustomerName", 2), ("Total USD", "Total", 1), ("Anulada", "Voided", 1));
        var page=0; var pageLabel=Text("",12,"#64748B");
        void Reload() { grid.ItemsSource=storage.SalesPage(page,search.Text); pageLabel.Text=$"Página {page+1} · hasta 50 ventas"; }
        var actions = new WrapPanel();
        actions.Children.Add(Button("Detalle / comprobante", () => { if (grid.SelectedItem is Sale sale) ShowSale(sale); else Status.Text = "Selecciona una venta."; }));
        actions.Children.Add(Button("Anular venta", () => { if (grid.SelectedItem is Sale sale) AskReason("Anular venta #" + sale.Number, reason => { operations.VoidSale(sale.Id, reason); Reload(); }); else Status.Text = "Selecciona una venta."; }));
        actions.Children.Add(Button("Devolver productos",()=> { if(grid.SelectedItem is Sale sale) ReturnForm(sale,Reload); else Status.Text="Selecciona una venta."; }));
        actions.Children.Add(Button("Anterior",()=> { if(page>0) page--; Reload(); })); actions.Children.Add(Button("Siguiente",()=> { if(grid.Items.Count==50) page++; Reload(); }));
        header.Children.Add(actions); header.Children.Add(pageLabel); search.TextChanged += (_, _) => { page=0; Reload(); }; Reload(); return Page(header, grid);
    }
    private void ShowSale(Sale sale)
    {
        var panel = new StackPanel(); var text = Receipt(sale); panel.Children.Add(Text(text, 15));
        panel.Children.Add(Button("Guardar comprobante TXT", () => ExportText($"venta-{sale.Number}.txt", text)));
        Form("Venta #" + sale.Number, panel, () => { }, "Cerrar");
    }
    private string Receipt(Sale sale) => $"{service.Settings.Name}\nCOMPROBANTE INTERNO · No es factura fiscal\nVenta #{sale.Number} · {sale.At.ToLocalTime():g}\nCliente: {sale.CustomerName}\n" + string.Join("\n", sale.Lines.Select(l => $"{l.Code} · {l.Name} · {l.Quantity} {l.Unit} × USD {l.Price:N2} = {l.Total:N2}")) + $"\nDescuento USD {sale.Discount:N2}\nTotal USD {sale.Total:N2}\nCambio USD {sale.ChangeUsd:N2}\nDeuda inicial USD {sale.InitialDebt:N2}\nSaldo pendiente USD {OperationsService.Debt(operations.State, sale):N2}\n" + string.Join("\n", sale.Payments.Select(p => $"{p.Method} · {CurrencyLabel(p.Currency)} {p.Amount:N2}")) + $"\nTasas por USD: BCV {sale.Rates.Bcv}, Bs manual {sale.Rates.Manual}, COP {sale.Rates.Cop}\nDevoluciones USD {operations.State.Returns.Where(r=>r.SaleId==sale.Id).Sum(r=>r.Total):N2} · Neto USD {OperationsService.NetTotal(operations.State,sale):N2}\nUsuario: {sale.SellerName}\nEstado: {(sale.Voided ? "ANULADA · " + sale.VoidReason : "Vigente")}";

    private UIElement CashPage()
    {
        var state = operations.State; var session = operations.CurrentCash; var header = new StackPanel();
        header.Children.Add(Text("Caja: "+storage.CashName,14));
        header.Children.Add(Text(session is null ? "Caja cerrada" : $"Caja abierta desde {session.OpenedAt.ToLocalTime():g}", 22));
        if (session is not null) { var balances=new List<string> { $"USD {OperationsService.Expected(state,session,"USD"):N2}" }; if(service.Settings.ShowBcv||service.Settings.ShowManualVes||OperationsService.Expected(state,session,"VES")!=0) balances.Add($"Bs {OperationsService.Expected(state,session,"VES"):N2}"); if(service.Settings.ShowCop||OperationsService.Expected(state,session,"COP")!=0) balances.Add($"COP {OperationsService.Expected(state,session,"COP"):N2}"); header.Children.Add(Text("Efectivo esperado · "+string.Join(" · ",balances),18)); }
        if (session is not null) header.Children.Add(Text("Movimiento neto equivalente USD por medio: " + string.Join(" · ", Enum.GetValues<PaymentMethod>().Select(m => $"{m} {state.Cash.Where(e => e.SessionId == session.Id && e.Payment.Method == m).Sum(e => e.Usd):N2}")), 13, "#64748B"));
        var actions = new WrapPanel();
        actions.Children.Add(Button(session is null ? "Abrir caja" : "Cerrar caja", () =>
        {
            var panel = new StackPanel(); var usd = Field(panel, session is null ? "Fondo USD" : "Contado USD", "0"); TextBox? ves = service.Settings.ShowBcv || service.Settings.ShowManualVes || session is not null && OperationsService.Expected(state,session,"VES")!=0 ? Field(panel, session is null ? "Fondo Bs" : "Contado Bs (saldo existente)", "0") : null; TextBox? cop = service.Settings.ShowCop || session is not null && OperationsService.Expected(state,session,"COP")!=0 ? Field(panel, session is null ? "Fondo COP" : "Contado COP (saldo existente)", "0") : null;
            Form(session is null ? "Apertura" : "Cierre y conciliación", panel, () => { if (session is null) operations.OpenCash(ParseNumber(usd.Text, "USD"), ParseNumber(ves?.Text??"0", "Bs"), ParseNumber(cop?.Text??"0", "COP")); else operations.CloseCash(ParseNumber(usd.Text, "USD"), ParseNumber(ves?.Text??"0", "Bs"), ParseNumber(cop?.Text??"0", "COP")); Navigate("Caja"); });
        }));
        actions.Children.Add(Button("Ingreso / gasto", () =>
        {
            var panel = new StackPanel(); var payment = PaymentFields(panel, "Movimiento"); var expense = Check(panel, "Es un gasto / retiro", false); var reason = Field(panel, "Motivo", "");
            Form("Movimiento de caja", panel, () => { operations.CashMovement(ReadPayment(payment), expense.IsChecked == true, reason.Text); Navigate("Caja"); });
        })); header.Children.Add(actions);
        var tabs = new TabControl();
        var entries = Table(("Caja", "CashName", 2),("Fecha", "At", 2), ("Motivo", "Reason", 2), ("Moneda", "Currency", 1), ("Medio", "Method", 1), ("Importe", "Amount", 1), ("Equiv. USD", "Usd", 1));
        entries.ItemsSource = state.Cash.Where(e=>storage.Can(Permission.Reports)||state.Sessions.Any(s=>s.Id==e.SessionId&&s.CashScope==storage.CashScope)).OrderByDescending(e => e.At).Select(e => new { CashName=state.Sessions.Single(s=>s.Id==e.SessionId).Name,e.At, e.Reason, Currency = CurrencyLabel(e.Payment.Currency), e.Payment.Method, e.Payment.Amount, e.Usd }).ToList();
        tabs.Items.Add(new TabItem { Header = "Movimientos", Content = entries });
        var closes = Table(("Caja", "Name", 2),("Apertura", "OpenedAt", 2), ("Cierre", "ClosedAt", 2), ("Dif. USD", "Usd", 1), ("Dif. Bs", "Ves", 1), ("Dif. COP", "Cop", 1));
        closes.ItemsSource = state.Sessions.Where(s => s.ClosedAt is not null && (storage.Can(Permission.Reports)||s.CashScope==storage.CashScope)).OrderByDescending(s => s.ClosedAt).Select(s => new { s.Name,s.OpenedAt, s.ClosedAt, Usd = s.CountedUsd - s.ExpectedUsd, Ves = s.CountedVes - s.ExpectedVes, Cop = s.CountedCop - s.ExpectedCop }).ToList();
        tabs.Items.Add(new TabItem { Header = "Cierres", Content = closes }); return Page(header, tabs);
    }

    private UIElement Contacts(bool supplier)
    {
        var header = new StackPanel(); var search = Field(header, supplier ? "Buscar proveedor" : "Buscar cliente", "");
        var grid = Table(("Nombre", "Name", 3), ("Identificación", "Identification", 2), ("Teléfono", "Phone", 2), ("Límite USD", "CreditLimit", 1), ("Activo", "Active", 1));
        void Reload() { var state = operations.State; grid.ItemsSource = (supplier ? state.Suppliers : state.Customers).Where(c => $"{c.Name} {c.Identification} {c.Phone}".Contains(search.Text, StringComparison.OrdinalIgnoreCase)).ToList(); }
        void Edit(Contact? existing)
        {
            var c = existing ?? new Contact(); var panel = new StackPanel(); var name = Field(panel, "Nombre", c.Name); var id = Field(panel, "Identificación", c.Identification); var phone = Field(panel, "Teléfono", c.Phone);
            var limit = supplier ? null : Field(panel, "Límite crédito USD", c.CreditLimit.ToString("0.00", UiCulture)); var active = Check(panel, "Activo", c.Active);
            if(limit is not null) limit.IsEnabled=storage.Can(Permission.Credit);
            Form(supplier ? "Proveedor" : "Cliente", panel, () => { operations.SaveContact(c with { Name = name.Text, Identification = id.Text.Trim(), Phone = phone.Text.Trim(), CreditLimit = limit is null ? 0 : ParseNumber(limit.Text, "límite"), Active = active.IsChecked == true }, supplier); Reload(); });
        }
        var actions = new WrapPanel(); actions.Children.Add(Button(supplier ? "Nuevo proveedor" : "Nuevo cliente", () => Edit(null)));
        actions.Children.Add(Button("Editar contacto", () => { if (grid.SelectedItem is Contact c) Edit(c); else Status.Text = "Selecciona un contacto."; }));
        if (!supplier) actions.Children.Add(Button("Estado de cuenta", () =>
        {
            if (grid.SelectedItem is not Contact c) { Status.Text = "Selecciona un cliente."; return; }
            var state = operations.State; var sales = state.Sales.Where(s => s.CustomerId == c.Id).ToList(); var panel = new StackPanel();
            var text = $"{c.Name}\nSaldo USD {sales.Sum(s => OperationsService.Debt(state, s)):N2}\n" + string.Join("\n", sales.Select(s => $"Venta #{s.Number} · {s.At.ToLocalTime():d} · USD {s.Total:N2} · saldo {OperationsService.Debt(state, s):N2} · {(s.Voided ? "Anulada" : "Vigente")}"));
            text += "\n\nABONOS\n" + string.Join("\n", state.Abonos.Where(a => sales.Any(s => s.Id == a.SaleId)).Select(a => $"{a.At.ToLocalTime():g} · Venta #{sales.Single(s => s.Id == a.SaleId).Number} · USD {a.Usd:N2} · {CurrencyLabel(a.Payment.Currency)} {a.Payment.Amount:N2} · {(a.Voided ? "Anulado: " + a.VoidReason : "Vigente")}"));
            panel.Children.Add(Text(text)); panel.Children.Add(Button("Exportar estado TXT", () => ExportText("estado-cliente.txt", text))); Form("Estado de cuenta", panel, () => { }, "Cerrar");
        }));
        header.Children.Add(actions); search.TextChanged += (_, _) => Reload(); Reload(); return Page(header, grid);
    }

    private UIElement Purchases()
    {
        var tabs = new TabControl(); var header = new StackPanel(); var grid = Table(("Fecha", "At", 2), ("Proveedor", "SupplierName", 2), ("Referencia", "Reference", 2), ("Total USD", "Total", 1), ("Anulada", "Voided", 1));
        void Reload() { grid.ItemsSource = operations.State.Purchases.OrderByDescending(p => p.At).ToList(); }
        var actions = new WrapPanel(); actions.Children.Add(Button("Nueva compra", () => PurchaseForm(Reload)));
        actions.Children.Add(Button("Detalle compra", () => { if (grid.SelectedItem is Purchase p) { var panel = new StackPanel(); panel.Children.Add(Text($"{p.Reference} · {p.SupplierName}\nTotal USD {p.Total:N2}\n" + string.Join("\n", p.Lines.Select(l => $"{l.Name} · {l.Quantity} × {l.Cost:N2} = {l.Total:N2}")))); Form("Compra", panel, () => { }, "Cerrar"); } }));
        actions.Children.Add(Button("Anular compra", () => { if (grid.SelectedItem is Purchase p) AskReason("Anular compra", reason => { operations.VoidPurchase(p.Id, reason); Reload(); }); else Status.Text = "Selecciona una compra."; }));
        header.Children.Add(actions); Reload(); tabs.Items.Add(new TabItem { Header = "Compras", Content = Page(header, grid) }); tabs.Items.Add(new TabItem { Header = "Proveedores", Content = Contacts(true) }); return tabs;
    }
    private void PurchaseForm(Action refresh)
    {
        var panel = new StackPanel(); panel.Children.Add(Text("Proveedor")); var supplier = new ComboBox { ItemsSource = operations.State.Suppliers.Where(c => c.Active).ToList(), DisplayMemberPath = "Name" }; panel.Children.Add(supplier);
        var reference = Field(panel, "Referencia de compra", ""); panel.Children.Add(Text("Producto")); var product = new ComboBox { ItemsSource = service.Products(), DisplayMemberPath = "Name" }; panel.Children.Add(product);
        var qty = Field(panel, "Cantidad", "1"); var cost = Field(panel, "Costo unitario USD", "0");
        product.SelectionChanged += (_, _) => { if (product.SelectedItem is Product p) cost.Text = p.CostUsd.ToString("0.00", UiCulture); };
        var lines = new ObservableCollection<PurchaseDraft>(); var grid = Table(("Producto", "Name", 3), ("Cantidad", "Quantity", 1), ("Costo", "Cost", 1)); grid.Height = 160; grid.ItemsSource = lines;
        var total = Text("Total USD 0,00", 20); var payment = PaymentFields(panel, "Pago de compra");
        var actions = new WrapPanel(); actions.Children.Add(Button("Agregar línea", () => Safe(() =>
        {
            if (product.SelectedItem is not Product p) throw new ArgumentException("Selecciona un producto.");
            if (lines.Any(l => l.ProductId == p.Id)) throw new ArgumentException("El producto ya está en la compra. Quita su línea para reemplazarla.");
            var q = ParseNumber(qty.Text, "cantidad"); var c = ParseNumber(cost.Text, "costo"); OperationsService.Quantity(p, q); OperationsService.Amount(c, true); lines.Add(new(p.Id, p.Name, q, c));
            var sum = lines.Sum(l => OperationsService.Money(l.Quantity * l.Cost)); total.Text = $"Total USD {sum:N2}"; if (payment.Currency.SelectedItem?.ToString() == "USD") payment.Amount.Text = sum.ToString("0.00", UiCulture);
        })));
        actions.Children.Add(Button("Quitar línea", () => { if (grid.SelectedItem is PurchaseDraft line) lines.Remove(line); var sum = lines.Sum(l => OperationsService.Money(l.Quantity * l.Cost)); total.Text = $"Total USD {sum:N2}"; if (payment.Currency.SelectedItem?.ToString() == "USD") payment.Amount.Text = sum.ToString("0.00", UiCulture); }));
        panel.Children.Add(actions); panel.Children.Add(grid); panel.Children.Add(total);
        Form("Recibir compra", panel, () => { if (supplier.SelectedItem is not Contact c) throw new ArgumentException("Selecciona proveedor."); operations.Buy(c.Id, reference.Text, lines.Select(l => (l.ProductId, l.Quantity, l.Cost)).ToList(), ReadPayment(payment)); refresh(); }, "Registrar compra");
    }

    private UIElement Credits()
    {
        var tabs = new TabControl(); var state = operations.State; var header = new StackPanel(); header.Children.Add(Text($"Pendiente USD {state.Sales.Sum(s => OperationsService.Debt(state, s)):N2}", 22));
        var grid = Table(("Venta", "Number", 1), ("Cliente", "Customer", 3), ("Vence", "Due", 2), ("Saldo USD", "Balance", 1), ("Estado", "Status", 1));
        grid.ItemsSource = state.Sales.Where(s => OperationsService.Debt(state, s) > 0).Select(s => new DebtRow(s.Id, s.Number, s.CustomerName, s.Due, OperationsService.Debt(state, s), s.Due < DateOnly.FromDateTime(DateTime.Today) ? "Vencido" : "Pendiente")).ToList();
        header.Children.Add(Button("Registrar abono", () =>
        {
            if (grid.SelectedItem is not DebtRow row) { Status.Text = "Selecciona una deuda."; return; }
            var panel = new StackPanel(); panel.Children.Add(Text($"{row.Customer} · Venta #{row.Number} · Saldo USD {row.Balance:N2}")); var payment = PaymentFields(panel, "Abono", row.Balance);
            Form("Abono de crédito", panel, () => { operations.PayDebt(row.Id, ReadPayment(payment)); Navigate("Créditos"); });
        })); tabs.Items.Add(new TabItem { Header = "Deudas", Content = Page(header, grid) });
        var abonos = Table(("Fecha", "At", 2), ("Venta", "Number", 1), ("Cliente", "Customer", 2), ("USD", "Usd", 1), ("Anulado", "Voided", 1)); abonos.ItemsSource = state.Abonos.OrderByDescending(a => a.At).Select(a => new AbonoRow(a.Id, a.At, state.Sales.Single(s => s.Id == a.SaleId).Number, state.Sales.Single(s => s.Id == a.SaleId).CustomerName, a.Usd, a.Voided)).ToList(); var abonoHeader = new StackPanel();
        abonoHeader.Children.Add(Button("Anular abono", () => { if (abonos.SelectedItem is AbonoRow a) AskReason("Anular abono", reason => { operations.VoidDebtPayment(a.Id, reason); Navigate("Créditos"); }); else Status.Text = "Selecciona un abono."; }));
        abonoHeader.Children.Add(Button("Comprobante de abono", () =>
        {
            if (abonos.SelectedItem is not AbonoRow row) { Status.Text = "Selecciona un abono."; return; }
            var abono = state.Abonos.Single(a => a.Id == row.Id); var panel = new StackPanel();
            var text = $"{service.Settings.Name}\nCOMPROBANTE DE ABONO INTERNO\n{row.Customer} · Venta #{row.Number}\n{abono.At.ToLocalTime():g}\n{CurrencyLabel(abono.Payment.Currency)} {abono.Payment.Amount:N2} · {abono.Payment.Method}\nEquivalente USD {abono.Usd:N2}\nTasa guardada {abono.Rates.For(abono.Payment.Currency)}\nEstado {(abono.Voided ? "Anulado: " + abono.VoidReason : "Vigente")}";
            panel.Children.Add(Text(text)); panel.Children.Add(Button("Guardar abono TXT", () => ExportText("abono.txt", text))); Form("Comprobante de abono", panel, () => { }, "Cerrar");
        }));
        tabs.Items.Add(new TabItem { Header = "Historial de abonos", Content = Page(abonoHeader, abonos) }); return tabs;
    }

    private UIElement Reports()
    {
        var header = new StackPanel(); var range = new WrapPanel(); range.Children.Add(Text("Desde")); var from = new DatePicker { SelectedDate = DateTime.Today.AddDays(-30), Width = 150, Margin = new Thickness(8) }; range.Children.Add(from); range.Children.Add(Text("Hasta")); var to = new DatePicker { SelectedDate = DateTime.Today, Width = 150, Margin = new Thickness(8) }; range.Children.Add(to); header.Children.Add(range);
        var summary = Text("", 18); header.Children.Add(summary); var tabs = new TabControl(); var sales = Table(("Venta", "Number", 1), ("Fecha", "At", 2), ("Cliente", "CustomerName", 2), ("Usuario","SellerName",2), ("Neto USD", "Total", 1)); var stock = Table(("Producto", "Name", 3), ("Stock actual", "Quantity", 1)); var debt = Table(("Cliente", "Name", 3), ("Saldo actual USD", "Balance", 1));
        header.Children.Add(Text("Ventas agrupadas por su fecha original, netas de todas sus devoluciones hasta hoy. La pestaña Devoluciones usa la fecha del reintegro.",13,"#64748B"));
        var sellers=Table(("Usuario","Name",3),("Ventas","Count",1),("Neto USD","Total",1),("Margen USD","Margin",1));
        var returns=Table(("Fecha","At",2),("Venta","Number",1),("Motivo","Reason",3),("Total USD","Total",1),("Deuda reducida","DebtReduction",1),("Reintegro USD","RefundUsd",1));
        tabs.Items.Add(new TabItem { Header="Por usuario",Content=sellers }); tabs.Items.Add(new TabItem { Header="Devoluciones",Content=returns });
        tabs.Items.Add(new TabItem { Header = "Ventas", Content = sales }); tabs.Items.Add(new TabItem { Header = "Inventario actual", Content = stock }); tabs.Items.Add(new TabItem { Header = "Cuentas por cobrar", Content = debt });
        List<Sale> rows = []; var reportState=operations.State;
        void Refresh()
        {
            if (from.SelectedDate is not { } start || to.SelectedDate is not { } end || end < start) throw new ArgumentException("Selecciona un rango válido.");
            var state = operations.State; rows = state.Sales.Where(s => !s.Voided && s.At.LocalDateTime.Date >= start.Date && s.At.LocalDateTime.Date <= end.Date).OrderByDescending(s => s.At).ToList(); sales.ItemsSource = rows.Select(s=>new { s.Number,s.At,s.CustomerName,s.SellerName,Total=OperationsService.NetTotal(state,s) }).ToList();
            reportState=state;
            sellers.ItemsSource=rows.GroupBy(s=>s.SellerId).Select(g=>new { Name=g.First().SellerName,Count=g.Count(),Total=g.Sum(s=>OperationsService.NetTotal(state,s)),Margin=g.Sum(s=>OperationsService.NetMargin(state,s)) }).ToList();
            returns.ItemsSource=state.Returns.Where(r=>r.At.LocalDateTime.Date>=start.Date&&r.At.LocalDateTime.Date<=end.Date).OrderByDescending(r=>r.At).Select(r=>new { r.At,Number=state.Sales.Single(s=>s.Id==r.SaleId).Number,r.Reason,r.Total,r.DebtReduction,r.RefundUsd }).ToList();
            var profit = rows.Sum(s=>OperationsService.NetMargin(state,s));
            var buys = state.Purchases.Where(p => !p.Voided && p.At.LocalDateTime.Date >= start.Date && p.At.LocalDateTime.Date <= end.Date).Sum(p => p.Total);
            summary.Text = $"Ventas: {rows.Count} · USD {rows.Sum(s => OperationsService.NetTotal(state,s)):N2} · Margen estimado USD {profit:N2}\nCompras del período USD {buys:N2} · Deuda actual USD {state.Sales.Sum(s => OperationsService.Debt(state, s)):N2}";
            stock.ItemsSource = service.Products(includeInactive: true).Select(p => new { p.Name, Quantity = OperationsService.Stock(state, p.Id) }).ToList();
            debt.ItemsSource = state.Customers.Select(c => new { c.Name, Balance = state.Sales.Where(s => s.CustomerId == c.Id).Sum(s => OperationsService.Debt(state, s)) }).ToList();
        }
        var actions = new WrapPanel(); actions.Children.Add(Button("Consultar", () => Safe(Refresh)));
        actions.Children.Add(Button("Exportar ventas CSV", () => ExportText("ventas.csv", "Venta;Fecha;Cliente;Total_USD;Descuento_USD;Costo_USD;Margen_USD\n" + string.Join("\n", rows.Select(s => $"{s.Number};{Csv(s.At.ToLocalTime().ToString("O"))};{Csv(s.CustomerName)};{OperationsService.NetTotal(reportState,s).ToString(System.Globalization.CultureInfo.InvariantCulture)};{s.Discount.ToString(System.Globalization.CultureInfo.InvariantCulture)};{(s.Lines.Sum(l => OperationsService.Money(l.Quantity * l.Cost))-reportState.Returns.Where(r=>r.SaleId==s.Id).Sum(r=>r.CostReduction)).ToString(System.Globalization.CultureInfo.InvariantCulture)};{OperationsService.NetMargin(reportState,s).ToString(System.Globalization.CultureInfo.InvariantCulture)}")))));
        actions.Children.Add(Button("Exportar inventario CSV", () => { var state = operations.State; ExportText("inventario.csv", "Codigo;Producto;Unidad;Stock;Costo_USD;Precio_USD\n" + string.Join("\n", service.Products(includeInactive: true).Select(p => $"{Csv(p.Code)};{Csv(p.Name)};{Csv(p.Unit)};{OperationsService.Stock(state, p.Id).ToString(System.Globalization.CultureInfo.InvariantCulture)};{p.CostUsd.ToString(System.Globalization.CultureInfo.InvariantCulture)};{p.PriceUsd.ToString(System.Globalization.CultureInfo.InvariantCulture)}"))); }));
        header.Children.Add(actions); Refresh(); return Page(header, tabs);
    }
    private static string Csv(string value) => "\"" + (value.StartsWith('=') || value.StartsWith('+') || value.StartsWith('-') || value.StartsWith('@') ? "'" : "") + value.Replace("\"", "\"\"") + "\"";
    private void ExportText(string fileName, string content)
    {
        var dialog = new SaveFileDialog { FileName = fileName, Filter = fileName.EndsWith(".csv") ? "CSV|*.csv" : "Texto|*.txt" };
        if (dialog.ShowDialog(this) == true) Safe(() => { File.WriteAllText(dialog.FileName, content, new UTF8Encoding(true)); Status.Text = "Archivo exportado."; });
    }

    private UIElement Backups()
    {
        var panel = new StackPanel(); panel.Children.Add(Text("Protege y recupera los datos de tu negocio", 22));
        if (storage.BackupWarning is { } warning) panel.Children.Add(Text(warning, 15, "#B91C1C"));
        panel.Children.Add(Text("Los respaldos incluyen catálogo, configuración, inventario y operaciones. La restauración reemplaza los datos actuales y conserva una copia previa.", 14, "#64748B"));
        var settings = service.Settings; var auto = Check(panel, storage is Monii.Infrastructure.RemoteStore ? "Respaldo automático diario en el principal (conserva 14 copias)" : "Respaldo automático diario al abrir Monii (conserva 14 copias)", settings.AutoBackups);
        var directory = storage is Monii.Infrastructure.RemoteStore ? new TextBox { Text=settings.BackupDirectory } : Field(panel, "Carpeta de respaldos (vacío: carpeta de datos / backups)", settings.BackupDirectory);
        if(storage is Monii.Infrastructure.RemoteStore)panel.Children.Add(Text("Los respaldos automáticos se guardan en el equipo principal. Puedes descargar una copia con Crear respaldo ahora."));
        panel.Children.Add(Button("Guardar preferencias de respaldo", () => Safe(() => { service.SaveSettings(service.Settings with { AutoBackups = auto.IsChecked == true, BackupDirectory = directory.Text.Trim() }); storage.AutomaticBackup(); Status.Text = "Preferencias guardadas."; })));
        panel.Children.Add(Button("Crear respaldo ahora", () =>
        {
            var save = new SaveFileDialog { FileName = "monii-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".db", Filter = "Respaldo Monii|*.db" };
            if (save.ShowDialog(this) == true) Safe(() => { storage.Backup(save.FileName); Status.Text = "Respaldo validado: " + save.FileName; });
        }));
        panel.Children.Add(Button("Restaurar respaldo", () =>
        {
            var open = new OpenFileDialog { Filter = "Respaldo Monii|*.db" }; if (open.ShowDialog(this) != true) return;
            Safe(() =>
            {
                storage.ValidateBackup(open.FileName);
                if (MessageBox.Show("Se reemplazarán TODOS los datos actuales por este respaldo. Se conservará una copia previa. ¿Continuar?\n\n" + open.FileName, "Restaurar Monii", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                var recovery = storage.Restore(open.FileName); saleCart.Clear(); demoCart.Clear(); Status.Text = "Restauración completada. Copia previa: " + recovery;
                storage.SignOut(); if(!App.SignIn(storage)) Close(); else Navigate("Inicio");
            });
        }));
        panel.Children.Add(Text("Base activa: " + databasePath, 12, "#64748B")); return Scroll(panel);
    }
}

public sealed record CartLine(Guid ProductId, string Name, decimal Quantity, decimal Price) { public decimal Total => OperationsService.Money(Quantity * Price); }
public sealed record PurchaseDraft(Guid ProductId, string Name, decimal Quantity, decimal Cost);
public sealed record StockRow(Guid Id, string Name, string Code, string Unit, decimal Quantity, decimal Minimum, string Alert);
public sealed record DebtRow(Guid Id, long Number, string Customer, DateOnly? Due, decimal Balance, string Status);
public sealed record AbonoRow(Guid Id, DateTimeOffset At, long Number, string Customer, decimal Usd, bool Voided);
internal sealed record PaymentInput(ComboBox Currency, ComboBox Method, TextBox Amount);
