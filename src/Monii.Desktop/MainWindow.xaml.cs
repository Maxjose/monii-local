using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Monii.Application;
using Monii.Domain;
using Monii.Infrastructure;

namespace Monii.Desktop;

public partial class MainWindow : Window
{
    private readonly BusinessService service;
    private readonly string databasePath;
    private readonly OperationsService operations;
    private readonly IMoniiStore storage;
    private string currentPage = "Inicio";
    private readonly ObservableCollection<DemoLine> demoCart = [];
    private static readonly CultureInfo UiCulture = CultureInfo.GetCultureInfo("es-VE");

    public MainWindow(BusinessService service, IMoniiStore storage)
    {
        InitializeComponent();
        if(storage is RemoteStore remote)
        {
            VersionLabel.Text="VERSIÓN 0.7 · RED";DataModeLabel.Text="Datos en el equipo principal";ConnectionLabel.Text="●  Conectado al principal";
            remote.ConnectionChanged+=connected=>Dispatcher.BeginInvoke(new Action(()=> { ConnectionLabel.Text=connected?"●  Conectado al principal":"●  Sin conexión al principal";ConnectionLabel.Foreground=connected?Brush("#166534"):Theme.Resource("ThemeError"); }));
        }

        this.service = service;
        this.databasePath = storage.DatabasePath;
        this.storage = storage;
        operations = new OperationsService(storage);
        Width = Math.Min(1280, SystemParameters.WorkArea.Width - 40);
        Height = Math.Min(820, SystemParameters.WorkArea.Height - 40);
        BuildNavigation();
        Navigate("Inicio");
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F2) { Navigate("Productos"); e.Handled = true; }
            if (e.Key == Key.F4) { Navigate(service.Settings.Profile==BusinessProfile.Basic?"Listado de precios":"Ventas"); e.Handled = true; }
        };
    }

    private void BuildNavigation()
    {
        Theme.Apply(service.Settings);
        BusinessName.Text = service.Settings.Name + (storage.CurrentUser is { } user ? $" · {user.Name} ({user.Role})" : "");
        Navigation.Children.Clear();
        AddNav("Inicio"); AddNav("Productos");
        var basic=service.Settings.Profile==BusinessProfile.Basic;
        if(basic)AddNav("Listado de precios");else AddNav("Ventas");
        if(storage.Can(Permission.Products)) AddNav("Categorías");
        var settings = service.Settings;
        if (!basic&&settings.Inventory&&storage.Can(Permission.Inventory)) AddNav("Inventario");
        if (!basic&&settings.Purchases&&storage.Can(Permission.Purchases)) AddNav("Compras");
        if (!basic&&settings.Customers&&storage.Can(Permission.Customers)) AddNav("Clientes");
        if (!basic&&settings.Credit&&storage.Can(Permission.Credit)) AddNav("Créditos");
        if (!basic&&settings.Cash&&storage.Can(Permission.Cash)) AddNav("Caja");
        if (!basic&&settings.Reports&&storage.Can(Permission.Reports)) AddNav("Reportes");
        if(storage.Can(Permission.Settings)) AddNav("Configuración");
        AddNav("Cerrar sesión");
    }

    private void AddNav(string label)
    {
        var button = Button(label, () => Navigate(label));
        button.HorizontalContentAlignment = HorizontalAlignment.Left;
        button.Style=(Style)FindResource("NavigationButton");
        button.Tag=label switch { "Inicio"=>"\uE80F","Productos"=>"\uE719","Listado de precios"=>"\uE8D4","Categorías"=>"\uE8EC","Ventas"=>"\uE7BF","Inventario"=>"\uE7B8","Compras"=>"\uE8CC","Clientes"=>"\uE77B","Créditos"=>"\uE8C7","Caja"=>"\uE8D4","Reportes"=>"\uE9D9","Configuración"=>"\uE713",_=>"\uE8D7" };
        button.SetResourceReference(Control.BackgroundProperty,label==currentPage?"Accent":"ThemeSidebar");
        button.Foreground=label==currentPage?Theme.Resource("AccentText"):Brushes.White;
        button.Margin = new Thickness(0, 2, 0, 2);
        Navigation.Children.Add(button);
    }

    private bool ConfirmSignOut()
    {
        var panel=new StackPanel { Margin=new Thickness(24) };
        panel.Children.Add(Text("¿Cerrar sesión?",24));
        panel.Children.Add(Text("Volverás a la pantalla de acceso. Los datos guardados y la caja abierta se conservarán.",14));
        if(saleCart.Count>0||demoCart.Count>0)
            panel.Children.Add(Text("El carrito que aún no has confirmado se descartará.",14,"#92400E"));
        var dialog=new Window { Title="Monii · Cerrar sesión",Owner=this,Width=460,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Brush("#F3F6FA") };
        var actions=new WrapPanel { Margin=new Thickness(0,16,0,0) };
        var cancel=Button("Cancelar",()=>dialog.DialogResult=false); cancel.IsCancel=true; cancel.IsDefault=true;
        actions.Children.Add(cancel);
        actions.Children.Add(Button("Cerrar sesión",()=>dialog.DialogResult=true));
        panel.Children.Add(actions); dialog.Content=panel;
        return dialog.ShowDialog()==true;
    }

    private void Navigate(string page)
    {
        try
        {
            if(page=="Cerrar sesión")
            {
                if(!ConfirmSignOut()) return;
                saleCart.Clear(); demoCart.Clear();
                var resumeExchange=exchangeTimer.IsEnabled;exchangeTimer.Stop();
                Hide();PageContent.Content=null;Navigation.Children.Clear();BusinessName.Text="";PageTitle.Text="";Status.Text="";
                storage.SignOut();
                try { if(!App.SignIn(storage)) { Close(); return; } }
                catch { Close();throw; }
                Show();if(resumeExchange)exchangeTimer.Start();
                page="Inicio";
            }
            if(service.Settings.Profile==BusinessProfile.Basic&&(page is "Ventas" or "Venta demo" or "Inventario" or "Compras" or "Clientes" or "Créditos" or "Caja" or "Reportes"))page="Listado de precios";
            var permission=page switch { "Configuración" or "Apariencia"=>Permission.Settings,"Categorías"=>Permission.Products,"Inventario"=>Permission.Inventory,"Compras"=>Permission.Purchases,"Clientes"=>Permission.Customers,"Créditos"=>Permission.Credit,"Caja"=>Permission.Cash,"Reportes"=>Permission.Reports,"Respaldos"=>Permission.Backup,"Usuarios"=>Permission.Users,"Actualizaciones"=>Permission.Updates,_=>(Permission?)null };
            if(permission is { } needed) storage.Require(needed);
            var section=page is "Respaldos" or "Usuarios" or "Actualizaciones" or "Apariencia"?page:"";
            if(section.Length>0) page="Configuración";
            currentPage = page;
            BuildNavigation();
            PageTitle.Text = page;
            PageHeader.Margin=new Thickness(0,0,0,page=="Inicio"?8:24);
            Status.Text = service.Settings.Profile==BusinessProfile.Basic?"F2: productos · F4: listado de precios":"F2: productos · F4: ventas · USD es la moneda base";
            PageContent.Content = page switch
            {
                "Inicio" => Home(), "Listado de precios"=>PriceList(),"Productos" => Products(), "Categorías"=>CategoriesPage(),"Configuración" => Settings(section), "Ventas" => Sales(),
                "Venta demo" => DemoSales(), "Inventario" => Inventory(), "Caja" => CashPage(), "Compras" => Purchases(),
                "Clientes" => Contacts(false), "Créditos" => Credits(), "Reportes" => Reports(), "Respaldos" => Backups(),
                "Usuarios"=>UsersPage(), "Actualizaciones"=>UpdatesPage(),
                _ => Planned(page)
            };
        }
        catch (Exception error) { ShowError(error); }
    }

    private UIElement Home()
    {
        if(service.Settings.Profile==BusinessProfile.Basic)return BasicHome();
        var panel = new StackPanel();
        if (storage.BackupWarning is { } warning) panel.Children.Add(Text(warning, 15, "#B91C1C"));
        if(storage is RemoteStore pending && pending.HasPending)
        {
            panel.Children.Add(Text("Hay una operación pendiente de confirmar. Accede con el usuario que la realizó y recupera su respuesta antes de registrar otra.",15,"#B91C1C"));
            panel.Children.Add(Button("Reconciliar operación pendiente",()=>Safe(ResolvePendingOperation)));
        }
        var introduction=Text("Consulta existencias, registra ventas y revisa tu caja.",14,"#64748B");
        introduction.Margin=new Thickness(0,0,0,8);panel.Children.Add(introduction);
        var cards = new UniformGrid { Columns = 3, Margin = new Thickness(0, 24, 0, 24) };
        var products = service.Products(includeInactive: true);
        cards.Children.Add(Card("PRODUCTOS ACTIVOS", products.Count(p => p.Active).ToString(), storage is RemoteStore ? "Catálogo del equipo principal" : "Catálogo guardado en este equipo"));
        cards.Children.Add(Card("PERFIL DEL NEGOCIO", ProfileLabel(service.Settings.Profile), "Opciones adaptadas a tu actividad"));
        cards.Children.Add(Card("MONEDA BASE", "USD", "Conversiones opcionales"));
        panel.Children.Add(cards);
        var actions = new WrapPanel();
        if(storage.Can(Permission.Products)) actions.Children.Add(Button("Registrar un producto", () => EditProduct(null, () => Navigate("Productos"))));
        if(storage.Can(Permission.Settings)) actions.Children.Add(Button("Configurar negocio", () => Navigate("Configuración")));
        actions.Children.Add(Button("Registrar venta", () => Navigate("Ventas")));
        panel.Children.Add(actions);
        if(storage.Can(Permission.Reports)) panel.Children.Add(Text("Actividad reciente", 20));
        var audit = storage.Can(Permission.Reports) ? service.Audit.Take(6).ToList() : [];
        if (audit.Count == 0&&storage.Can(Permission.Reports)) panel.Children.Add(Text("Aún no hay cambios registrados. Tu catálogo está listo para comenzar.", 14, "#64748B"));
        foreach (var entry in audit)
            panel.Children.Add(Text($"{entry.At.ToLocalTime():dd/MM HH:mm}   ·   {entry.Action} · {entry.ActorName}", 14, "#64748B"));
        var state = operations.State;
        if(storage.Can(Permission.Reports)) panel.Children.Add(Text($"Ventas vigentes: {state.Sales.Count(s => !s.Voided)} · Total USD {state.Sales.Sum(s => OperationsService.NetTotal(state,s)):N2} · Créditos pendientes USD {state.Sales.Sum(s => OperationsService.Debt(state, s)):N2}", 15));
        panel.Children.Add(Text(operations.CurrentCash is null ? "Caja cerrada" : "Caja abierta", 14, "#0F766E"));
        return Scroll(panel);
    }

    private UIElement Products()
    {
        var layout = new DockPanel();
        var toolbar = new StackPanel();
        toolbar.Children.Add(Text("Crea y consulta tu catálogo. Desactivar conserva el producto y su historial.", 14, "#64748B"));
        toolbar.Children.Add(Text("Buscar por nombre, código, categoría, marca o referencia", 12, "#64748B"));
        var controls = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) };
        var search = new TextBox { Width = 290, ToolTip = "Buscar por nombre, código, categoría, marca o referencia", Margin = new Thickness(0, 4, 16, 4) };
        var inactive = new CheckBox { Content = "Incluir inactivos", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        controls.Children.Add(search); controls.Children.Add(inactive);
        var grid = new DataGrid();
        grid.Columns.Add(Column("Producto", "Name", 2));
        grid.Columns.Add(Column("Código", "Code"));
        grid.Columns.Add(Column("Categoría", "Category"));
        if(service.Settings.Profile!=BusinessProfile.Basic&&storage.Can(Permission.Products)) grid.Columns.Add(Column("Costo USD", "CostUsd", 1, "{0:N2}"));
        grid.Columns.Add(Column("Precio USD", "PriceUsd", 1, "{0:N2}"));
        var currencies = service.Settings;
        if (currencies.ShowBcv) grid.Columns.Add(ConvertedColumn("Bs BCV", currencies.BcvRate));
        if (currencies.ShowManualVes) grid.Columns.Add(ConvertedColumn("Bs manual", currencies.ManualVesRate));
        if (currencies.ShowCop) grid.Columns.Add(ConvertedColumn("COP", currencies.CopRate));
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Activo", Binding = new Binding("Active"), Width = 65 });
        var count = Text("", 12, "#64748B");
        void Reload()
        {
            var rows = service.Products(search.Text, inactive.IsChecked == true);
            grid.ItemsSource = rows;
            count.Text = $"{rows.Count} productos · Doble clic para editar · Precios en USD";
        }
        controls.Children.Add(Button("+ Nuevo producto", () => EditProduct(null, Reload)));
        if(service.Settings.Profile!=BusinessProfile.Basic)controls.Children.Add(Button("Importar Excel / CSV",()=>StartProductImport()));
        controls.Children.Add(Button("Editar", () =>
        {
            if (grid.SelectedItem is Product product) EditProduct(product, Reload);
            else Status.Text = "Selecciona un producto para editarlo.";
        }));
        controls.Children.Add(Button("Activar / desactivar", () =>
        {
            if (grid.SelectedItem is not Product product) { Status.Text = "Selecciona un producto."; return; }
            if (MessageBox.Show($"¿{(product.Active ? "Desactivar" : "Activar")} «{product.Name}»? Sus datos se conservarán.", "Monii", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            Safe(() => { service.Save(product with { Active = !product.Active }); Reload(); Status.Text = "Estado actualizado. Historial conservado."; });
        }));
        foreach(var button in controls.Children.OfType<Button>()) button.IsEnabled=storage.Can(Permission.Products); toolbar.Children.Add(controls); toolbar.Children.Add(count);
        DockPanel.SetDock(toolbar, Dock.Top); layout.Children.Add(toolbar); layout.Children.Add(grid);
        search.TextChanged += (_, _) => Reload();
        inactive.Checked += (_, _) => Reload(); inactive.Unchecked += (_, _) => Reload();
        grid.MouseDoubleClick += (_, _) => { if (grid.SelectedItem is Product product) EditProduct(product, Reload); };
        Reload();
        return layout;
    }

    private void EditProduct(Product? existing, Action saved)
    {
        if(!storage.Can(Permission.Products)) { Status.Text="Tu usuario solo puede consultar el catálogo."; return; }
        if(service.Settings.Profile==BusinessProfile.Basic) { EditBasicProduct(existing,saved);return; }
        var product = existing ?? new Product();
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(Text(existing is null ? "Nuevo producto" : "Editar producto", 24));
        var name = Field(panel, "Nombre *", product.Name);
        panel.Children.Add(Text("Código / código de barras *"));
        var codeRow=new Grid(); codeRow.ColumnDefinitions.Add(new ColumnDefinition()); codeRow.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        var code=new TextBox { Text=product.Code,Name="ProductCode",Margin=new Thickness(0,4,8,12) }; codeRow.Children.Add(code);
        var scan=Button("Escanear",()=>ScanProductCode(code)); scan.ToolTip="Esperar código de un lector USB/Bluetooth que escribe como teclado"; Grid.SetColumn(scan,1); codeRow.Children.Add(scan); panel.Children.Add(codeRow);
        var categories=storage.GetCategories().Where(c=>c.Active||c.Name==product.Category).Prepend(new Category(Guid.Empty,"Sin categoría")).ToList();
        panel.Children.Add(Text("Categoría")); var category=new ComboBox { ItemsSource=categories,DisplayMemberPath="Name",SelectedItem=categories.FirstOrDefault(c=>product.Category.Length==0?c.Id==Guid.Empty:c.Name==product.Category) }; panel.Children.Add(category);
        panel.Children.Add(Text("Gestiona el listado desde Categorías en el menú.",12,"#64748B"));
        var unit = Choice(panel, "Unidad de venta", new[] { "Unidad", "Kilogramo", "Litro", "Metro", "Caja" }, product.Unit);
        var cost = Field(panel, "Costo USD (hasta 2 decimales)", product.CostUsd.ToString("0.00", UiCulture));
        var price = Field(panel, "Precio USD (hasta 2 decimales)", product.PriceUsd.ToString("0.00", UiCulture));
        var brand = Field(panel, "Marca", product.Brand);
        var reference = Field(panel, "Referencia / código alternativo", product.Reference);
        TextBox? compatibility = null;
        if (service.Settings.VehicleCompatibility) compatibility = Field(panel, "Compatibilidad con vehículos", product.Compatibility);
        var active = Check(panel, "Producto activo", product.Active);
        var minimum = Field(panel, "Alerta de stock mínimo", product.MinimumStock.ToString("0.###", UiCulture));
        var error = Text("", 13, "#B91C1C"); panel.Children.Add(error);
        var dialog = new Window { Title = "Monii · Producto", Width = 520, Height = Math.Min(740, SystemParameters.WorkArea.Height - 60), MinHeight = 500, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Brush("#F3F6FA"), Content = Scroll(panel) };
        var actions = new WrapPanel();
        actions.Children.Add(Button("Guardar producto", () =>
        {
            try
            {
                service.Save(product with
                {
                    Name = name.Text, Code = code.Text, Category = category.SelectedItem is Category selectedCategory&&selectedCategory.Id!=Guid.Empty?selectedCategory.Name:"", Unit = unit.SelectedItem?.ToString() ?? "Unidad",
                    CostUsd = ParseNumber(cost.Text, "costo"), PriceUsd = ParseNumber(price.Text, "precio"), Brand = brand.Text.Trim(),
                    Reference = reference.Text.Trim(), Compatibility = compatibility?.Text.Trim() ?? product.Compatibility, Active = active.IsChecked == true, MinimumStock = ParseNumber(minimum.Text, "stock mínimo")
                });
                dialog.Close(); saved(); Status.Text = "Producto guardado en la base de datos local.";
            }
            catch (Exception exception) { error.Text = FriendlyError(exception); }
        }));
        actions.Children.Add(Button("Cancelar", () => dialog.Close())); panel.Children.Add(actions);
        dialog.Loaded += (_, _) => name.Focus();
        dialog.ShowDialog();
    }

    private UIElement BusinessSettingsPage()
    {
        var settings = service.Settings;
        var panel = new StackPanel { MaxWidth = 820 };
        panel.Children.Add(Text("Datos del negocio", 20));
        var name = Field(panel, "Nombre del negocio *", settings.Name);
        var taxId = Field(panel, "Identificación fiscal", settings.TaxId);
        var profile = Choice(panel, "Perfil", new[] { "Negocio general", "Víveres", "Repuestos", "Básico" }, ProfileLabel(settings.Profile));
        var basicDescription=Text("Perfil básico: registra productos, organiza categorías y consulta precios. Las funciones de ventas, caja e inventario quedan ocultas. Puedes volver a otro perfil conservando tus datos.",14,"#64748B");panel.Children.Add(basicDescription);
        var modules=new StackPanel();panel.Children.Add(modules);
        modules.Children.Add(Text("Módulos opcionales", 20));
        modules.Children.Add(Text("Oculta las funciones que no utilices. Sus datos se conservarán.", 13, "#64748B"));
        var inventory = Check(modules, "Inventario", settings.Inventory);
        var purchases = Check(modules, "Compras", settings.Purchases);
        var customers = Check(modules, "Clientes", settings.Customers);
        var credit = Check(modules, "Créditos (requiere clientes)", settings.Credit);
        var cash = Check(modules, "Caja", settings.Cash);
        var reports = Check(modules, "Reportes", settings.Reports);
        var lots = Check(modules, "Lotes y vencimientos", settings.Lots);
        var compatibility = Check(modules, "Campos de compatibilidad con vehículos", settings.VehicleCompatibility);
        var tickets = Check(modules, "Impresión de tickets — preferencia para futura fase", settings.Tickets);
        customers.Unchecked += (_, _) => credit.IsChecked = false;
        credit.Checked += (_, _) => customers.IsChecked = true;
        void ProfileChanged()
        {
            modules.Visibility=profile.SelectedIndex==3?Visibility.Collapsed:Visibility.Visible;
            basicDescription.Visibility=profile.SelectedIndex==3?Visibility.Visible:Visibility.Collapsed;
            if(profile.SelectedIndex==3)return;
            lots.IsChecked = profile.SelectedIndex == 1;
            compatibility.IsChecked = profile.SelectedIndex == 2;
        }
        profile.SelectionChanged+=(_,_)=>ProfileChanged();
        modules.Children.Add(Text("La impresión de tickets se implementará en una entrega posterior.", 13, "#92400E"));
        modules.Visibility=profile.SelectedIndex==3?Visibility.Collapsed:Visibility.Visible;basicDescription.Visibility=profile.SelectedIndex==3?Visibility.Visible:Visibility.Collapsed;
        var error = Text("", 13, "#B91C1C"); panel.Children.Add(error);
        panel.Children.Add(Button("Guardar configuración", () =>
        {
            try
            {
                service.SaveSettings(service.Settings with
                {
                    Name = name.Text, TaxId = taxId.Text.Trim(), Profile = (BusinessProfile)profile.SelectedIndex,
                    Inventory = inventory.IsChecked == true, Purchases = purchases.IsChecked == true, Customers = customers.IsChecked == true,
                    Credit = credit.IsChecked == true, Cash = cash.IsChecked == true, Reports = reports.IsChecked == true,
                    Lots = lots.IsChecked == true, VehicleCompatibility = compatibility.IsChecked == true, Tickets = tickets.IsChecked == true
                });
                BuildNavigation(); error.Text = ""; Status.Text = "Configuración guardada. Los datos de los módulos desactivados se conservan.";
            }
            catch (Exception exception) { error.Text = FriendlyError(exception); }
        }));
        panel.Children.Add(Text("Almacenamiento local", 20)); panel.Children.Add(Text(databasePath, 12, "#64748B"));
        return Scroll(panel);
    }

    private UIElement DemoSales()
    {
        var panel = new DockPanel();
        var header = new StackPanel();
        header.Children.Add(Text("DEMOSTRACIÓN · No registra ventas, cobros ni movimientos de inventario", 15, "#92400E"));
        header.Children.Add(Text("Productos ficticios. El carrito se conserva solo durante esta sesión.", 13, "#64748B"));
        header.Children.Add(Text("Buscar en los productos de demostración", 12, "#64748B"));
        var search = new TextBox { ToolTip = "Buscar producto de demostración" }; header.Children.Add(search);
        var choices = new WrapPanel(); header.Children.Add(choices);
        (string Name, decimal Price)[] examples = [("Arroz 1 kg · DEMO", 1.25m), ("Aceite 1 L · DEMO", 3.50m), ("Filtro de aceite · DEMO", 8.75m)];
        var total = Text("", 26);
        var conversions = Text("", 14, "#64748B");
        void Totals()
        {
            var amount = demoCart.Sum(line => line.Total);
            total.Text = $"Total USD {amount:N2}";
            var settings = service.Settings;
            var labels = new List<string>();
            if (settings.ShowBcv) labels.Add($"Bs BCV (manual): {BusinessService.ConvertPrice(amount, settings.BcvRate):N2}");
            if (settings.ShowManualVes) labels.Add($"Bs manual: {BusinessService.ConvertPrice(amount, settings.ManualVesRate):N2}");
            if (settings.ShowCop) labels.Add($"COP: {BusinessService.ConvertPrice(amount, settings.CopRate):N2}");
            conversions.Text = string.Join("   ·   ", labels);
        }
        void Options()
        {
            choices.Children.Clear();
            foreach (var example in examples.Where(p => p.Name.Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase)))
                choices.Children.Add(Button($"{example.Name}\nUSD {example.Price:N2}", () =>
                {
                    var line = demoCart.FirstOrDefault(l => l.Name == example.Name);
                    if (line is not null) { var i = demoCart.IndexOf(line); demoCart[i] = line with { Quantity = line.Quantity + 1 }; }
                    else demoCart.Add(new(example.Name, 1, example.Price));
                    Totals();
                }));
        }
        search.TextChanged += (_, _) => Options(); Options();
        DockPanel.SetDock(header, Dock.Top); panel.Children.Add(header);
        var footer = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
        footer.Children.Add(total); footer.Children.Add(conversions);
        var actions = new WrapPanel();
        var grid = new DataGrid { ItemsSource = demoCart };
        actions.Children.Add(Button("Quitar seleccionado", () => { if (grid.SelectedItem is DemoLine line) demoCart.Remove(line); Totals(); }));
        actions.Children.Add(Button("Vaciar carrito", () => { demoCart.Clear(); Totals(); }));
        actions.Children.Add(Button("Simular cobro", () =>
        {
            if (demoCart.Count == 0) { Status.Text = "Agrega un producto de demostración."; return; }
            MessageBox.Show($"Demostración de cobro por USD {demoCart.Sum(l => l.Total):N2}.\n\nNo se ha guardado ninguna venta ni se ha modificado el inventario.", "Monii · Demostración", MessageBoxButton.OK, MessageBoxImage.Information);
        }));
        footer.Children.Add(actions); DockPanel.SetDock(footer, Dock.Bottom); panel.Children.Add(footer);
        grid.Columns.Add(Column("Producto demo", "Name", 3)); grid.Columns.Add(Column("Cantidad", "Quantity"));
        grid.Columns.Add(Column("Precio USD", "Price", 1, "{0:N2}")); grid.Columns.Add(Column("Total USD", "Total", 1, "{0:N2}"));
        panel.Children.Add(grid); Totals(); return panel;
    }

    private UIElement Planned(string module)
    {
        var panel = new StackPanel();
        panel.Children.Add(Text($"{module} está previsto para una próxima fase", 22));
        panel.Children.Add(Text("La preferencia de este módulo ya se guarda en configuración. Esta pantalla no realiza operaciones.", 15, "#64748B"));
        panel.Children.Add(Button("Ir a configuración", () => Navigate("Configuración")));
        return panel;
    }

    private static decimal ParseNumber(string text, string label)
    {
        if (!decimal.TryParse(text.Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
            throw new ArgumentException($"Escribe un valor válido para {label}; usa coma o punto decimal, sin separadores de miles.");
        return value;
    }
    private void Safe(Action action) { try { action(); } catch (Exception error) { ShowError(error); } }
    private void ShowError(Exception error) => MessageBox.Show(FriendlyError(error), "Monii", MessageBoxButton.OK, MessageBoxImage.Warning);
    private static string FriendlyError(Exception error) => error is ArgumentException ? error.Message : "No se pudo completar la operación. Revisa el acceso a los datos.\n" + error.Message;
    private static Brush Brush(string color) => Theme.Resolve(color);
    private static string ProfileLabel(BusinessProfile profile) => profile switch { BusinessProfile.Groceries => "Víveres", BusinessProfile.Parts => "Repuestos",BusinessProfile.Basic=>"Básico", _ => "Negocio general" };
    private static TextBlock Text(string value, double size = 14, string color = "#172B3A") => new() { Text = value, FontSize = size, Foreground = Brush(color), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
    private static Button Button(string label, Action action) { var button = new Button { Content = label }; button.Click += (_, _) => action(); return button; }
    private static TextBox Field(Panel panel, string label, string value) { panel.Children.Add(Text(label)); var field = new TextBox { Text = value }; panel.Children.Add(field); return field; }
    private static CheckBox Check(Panel panel, string label, bool value) { var check = new CheckBox { Content = label, IsChecked = value }; panel.Children.Add(check); return check; }
    private static ComboBox Choice(Panel panel, string label, IEnumerable<string> items, string selected) { panel.Children.Add(Text(label)); var choice = new ComboBox { ItemsSource = items, SelectedItem = selected }; panel.Children.Add(choice); return choice; }
    private static ScrollViewer Scroll(UIElement content) => new() { Content = content, Padding=new Thickness(0,0,18,12), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private static Border Card(string label, string value, string caption)
    {
        var panel = new StackPanel(); panel.Children.Add(Text(label, 11, "#64748B")); panel.Children.Add(Text(value, 28)); panel.Children.Add(Text(caption, 12, "#64748B"));
        return new Border { Background = Theme.Resource("ThemeSurface"), CornerRadius = new CornerRadius(12), Padding = new Thickness(20), Margin = new Thickness(0, 0, 12, 0), Child = panel };
    }
    private static DataGridTextColumn Column(string title, string property, double width = 1, string? format = null) => new()
    {
        Header = title,
        Binding = new Binding(property) { Converter = new DisplayValueConverter(), StringFormat = format ?? (property is "Total" or "Price" or "Cost" or "CostUsd" or "PriceUsd" or "Usd" or "Balance" or "CreditLimit" ? "{0:N2}" : null) },
        MinWidth = 85, Width = new DataGridLength(width, DataGridLengthUnitType.Star)
    };
    private static DataGridTextColumn ConvertedColumn(string title, decimal rate) => new() { Header = title, Binding = new Binding("PriceUsd") { Converter = new PriceConversionConverter(rate), StringFormat = "{0:N2}" }, MinWidth = 110, Width = 110 };
}

public sealed record DemoLine(string Name, int Quantity, decimal Price) { public decimal Total => Quantity * Price; }

public sealed class DisplayValueConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        DateTimeOffset at => at.ToLocalTime().ToString("g", culture),
        DateOnly date => date.ToString("d", culture),
        bool flag => flag ? "Sí" : "No",
        _ => value
    };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class PriceConversionConverter(decimal rate) : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is decimal usd ? BusinessService.ConvertPrice(usd, rate) : 0m;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
