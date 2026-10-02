using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Monii.Domain;

namespace Monii.Desktop;
public partial class MainWindow
{
    private UIElement Settings(string section="")
    {
        var tabs=new TabControl { TabStripPlacement=Dock.Top };
        void Add(string name,UIElement content) { var tab=new TabItem { Header=name,Content=content,Padding=new Thickness(6,10,6,10) }; tabs.Items.Add(tab); if(name==section || section.Length==0&&name=="Negocio") tabs.SelectedItem=tab; }
        Add("Negocio",BusinessSettingsPage()); Add("Monedas y tasas",CurrencySettingsPage()); Add("Apariencia",AppearancePage());
        if(storage.Can(Permission.Users)) Add("Usuarios",UsersPage());
        if(storage.Can(Permission.Backup)) Add("Respaldos",Backups());
        if(storage.Can(Permission.Updates)) Add("Actualizaciones",UpdatesPage());
        Add("Conexión",NetworkPage());
        if(tabs.SelectedIndex<0) tabs.SelectedIndex=0; return tabs;
    }
    private UIElement CurrencySettingsPage()
    {
        var settings=service.Settings;
        var panel=new StackPanel { MaxWidth=820 };
        panel.Children.Add(Text("Monedas y tasas", 20));
        panel.Children.Add(Text("USD siempre activo. Todas las tasas indican unidades de la moneda por 1 USD. Usa coma o punto decimal, sin separadores de miles.", 13, "#64748B"));
        var bcv = Check(panel, "Mostrar bolívares a referencia BCV", settings.ShowBcv);
        var bcvRate = Field(panel, "Tasa BCV · Bs por USD", settings.BcvRate.ToString("0.########", UiCulture));
        var manual = Check(panel, "Mostrar bolívares a tasa manual", settings.ShowManualVes);
        var manualRate = Field(panel, "Tasa manual · Bs por USD", settings.ManualVesRate.ToString("0.########", UiCulture));
        var cop = Check(panel, "Mostrar pesos colombianos", settings.ShowCop);
        var copRate = Field(panel, "Tasa COP · pesos por USD", settings.CopRate.ToString("0.########", UiCulture));
        var autoBcv=Check(panel,"Actualizar BCV al abrir y cada 60 minutos",settings.AutoBcv);
        var autoCop=Check(panel,"Actualizar TRM Colombia al abrir y cada 60 minutos",settings.AutoCop);
        panel.Children.Add(Text($"BCV: {settings.BcvSource} · vigencia {settings.BcvEffectiveDate?.ToString()??"sin fecha oficial"}\nCOP: {settings.CopSource} · vigencia {settings.CopEffectiveDate?.ToString()??"sin fecha oficial"}",13,"#64748B"));
        panel.Children.Add(Text("Guarda las monedas elegidas antes de consultar. Sin una tasa válida se bloquean los pagos en esa moneda. Si falla internet, se conserva la última tasa y su fecha.",13,"#64748B"));
        var updateRates=new Button { Content="Actualizar tasas ahora" }; updateRates.Click+=async(_,_)=> { updateRates.IsEnabled=false; await RefreshRates(true); var latest=service.Settings; bcvRate.Text=latest.BcvRate.ToString("0.########",UiCulture); copRate.Text=latest.CopRate.ToString("0.########",UiCulture); updateRates.IsEnabled=true; }; panel.Children.Add(updateRates);
        panel.Children.Add(Text(settings.RatesUpdatedAt is { } at ? $"Última modificación de tasas: {at.ToLocalTime():dd/MM/yyyy HH:mm}" : "Todavía no se han registrado tasas.", 13, "#64748B"));

        var error=Text("",13,"#B91C1C"); panel.Children.Add(error);
        panel.Children.Add(Button("Guardar monedas y tasas",()=>
        {
            try {
                service.SaveSettings(service.Settings with {
                    ShowBcv=bcv.IsChecked==true,ShowManualVes=manual.IsChecked==true,ShowCop=cop.IsChecked==true,
                    BcvRate=ParseNumber(bcvRate.Text,"tasa BCV")==settings.BcvRate?service.Settings.BcvRate:ParseNumber(bcvRate.Text,"tasa BCV"),
                    ManualVesRate=ParseNumber(manualRate.Text,"tasa manual"),
                    CopRate=ParseNumber(copRate.Text,"tasa COP")==settings.CopRate?service.Settings.CopRate:ParseNumber(copRate.Text,"tasa COP"),
                    AutoBcv=autoBcv.IsChecked==true,AutoCop=autoCop.IsChecked==true
                }); error.Text=""; Status.Text="Monedas y tasas guardadas.";
            } catch(Exception e) { error.Text=FriendlyError(e); }
        }));
        return Scroll(panel);
    }
    private UIElement AppearancePage()
    {
        var panel=new StackPanel { MaxWidth=800 };
        panel.Children.Add(Text("Apariencia",24)); panel.Children.Add(Text("Personaliza el color principal y el modo de la aplicación. Se aplica al guardar y se conserva al volver a abrir Monii.",14,"#64748B"));
        var dark=Check(panel,"Modo oscuro",service.Settings.DarkMode);
        var color=Field(panel,"Color principal (#RRGGBB)",service.Settings.AccentColor);
        var presets=new WrapPanel();
        foreach(var (name,value) in new[]{("Verde","#0F766E"),("Azul","#2563EB"),("Violeta","#7C3AED"),("Naranja","#EA580C"),("Rosa","#BE185D")})
        {
            var button=Button(name,()=>color.Text=value); var accent=(Color)ColorConverter.ConvertFromString(value); button.Background=new SolidColorBrush(accent); button.Foreground=new SolidColorBrush(Theme.Contrast(accent)); presets.Children.Add(button);
        }
        panel.Children.Add(presets); var error=Text("",13,"#B91C1C"); panel.Children.Add(error);
        panel.Children.Add(Button("Guardar apariencia",()=>
        {
            try { service.SaveSettings(service.Settings with { DarkMode=dark.IsChecked==true,AccentColor=color.Text.Trim().ToUpperInvariant() }); Theme.Apply(service.Settings); Navigate("Apariencia"); Status.Text="Apariencia guardada."; }
            catch(Exception e) { error.Text=FriendlyError(e); }
        }));
        panel.Children.Add(Button("Restablecer apariencia",()=> { dark.IsChecked=false; color.Text="#0F766E"; })); return Scroll(panel);
    }
    private UIElement CategoriesPage()
    {
        var header=new StackPanel(); header.Children.Add(Text("Categorías de productos",22)); header.Children.Add(Text("Crea, renombra o desactiva categorías. Al renombrar se actualiza el catálogo; desactivar conserva los productos y su historial.",14,"#64748B"));
        var search=Field(header,"Buscar categoría",""); var grid=Table(("Categoría","Name",3),("Activa","Active",1));
        void Reload()=>grid.ItemsSource=storage.GetCategories().Where(c=>c.Name.Contains(search.Text,StringComparison.OrdinalIgnoreCase)).ToList();
        void Edit(Category? existing)
        {
            var category=existing??new Category(Guid.NewGuid(),""); var panel=new StackPanel(); var name=Field(panel,"Nombre de categoría *",category.Name); var active=Check(panel,"Categoría activa",category.Active);
            Form(existing is null?"Nueva categoría":"Editar categoría",panel,()=> { storage.SaveCategory(category with { Name=name.Text,Active=active.IsChecked==true }); Reload(); });
        }
        var actions=new WrapPanel(); actions.Children.Add(Button("Nueva categoría",()=>Edit(null))); actions.Children.Add(Button("Editar categoría",()=> { if(grid.SelectedItem is Category category) Edit(category); else Status.Text="Selecciona una categoría."; }));
        actions.Children.Add(Button("Activar / desactivar categoría",()=>Safe(()=> { if(grid.SelectedItem is not Category category) { Status.Text="Selecciona una categoría."; return; } storage.SaveCategory(category with { Active=!category.Active }); Reload(); })));
        header.Children.Add(actions); search.TextChanged+=(_,_)=>Reload(); Reload(); return Page(header,grid);
    }
}
