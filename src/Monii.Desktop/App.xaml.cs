using System.IO;
using System.Windows;
using Monii.Application;
using Monii.Infrastructure;

namespace Monii.Desktop;

public partial class App : System.Windows.Application
{
    private string? dataDirectory;
    private Mutex? instanceMutex;
    private bool ownsMutex;
    protected override void OnStartup(StartupEventArgs e)
    {
        var culture = System.Globalization.CultureInfo.GetCultureInfo("es-VE");
        System.Globalization.CultureInfo.CurrentCulture = culture;
        System.Globalization.CultureInfo.CurrentUICulture = culture;
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = culture;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = culture;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(System.Windows.Markup.XmlLanguage.GetLanguage("es-VE")));
        base.OnStartup(e);
        try
        {
            var args = e.Args;
            var index = Array.IndexOf(args, "--data-dir");
            var directory = index >= 0 && args.Length > index + 1 ? args[index + 1] : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Monii");
            dataDirectory = Path.GetFullPath(directory);
            string? updateWarning=null;
            if(!args.Contains("--ui-verify")&&!args.Contains("--ignore-update"))
            {
                try
                {
                var updated=UpdatePackages.LatestExecutable(Path.Combine(dataDirectory,"updates","versions"),typeof(App).Assembly.GetName().Version!);
                if(updated is not null)
                {
                    var launch=new System.Diagnostics.ProcessStartInfo(updated) { UseShellExecute=false };
                    foreach(var argument in args) launch.ArgumentList.Add(argument);
                    System.Diagnostics.Process.Start(launch); Shutdown(); return;
                }
                }
                catch(Exception error) when(error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or FormatException or System.Security.Cryptography.CryptographicException or InvalidOperationException)
                { updateWarning="No se pudo abrir la actualización instalada: "+error.Message+" Se continúa con esta versión."; }
            }
            var identity = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(dataDirectory.ToUpperInvariant())));
            instanceMutex = new Mutex(false, "Local\\Monii-" + identity);
            try { ownsMutex = instanceMutex.WaitOne(0); } catch (AbandonedMutexException) { ownsMutex = true; }
            if (!ownsMutex)
            {
                MessageBox.Show("Monii ya está abierto con esta base de datos. Utiliza la ventana existente.", "Monii", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown(); return;
            }
            DispatcherUnhandledException += (_, eventArgs) =>
            {
                try
                {
                    Directory.CreateDirectory(dataDirectory);
                    File.AppendAllText(Path.Combine(dataDirectory, "errors.log"), $"{DateTimeOffset.UtcNow:O}\n{eventArgs.Exception}\n\n");
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                MessageBox.Show("No se pudo completar la acción. Revisa la operación e inténtalo nuevamente.\n\n" + eventArgs.Exception.Message, "Monii", MessageBoxButton.OK, MessageBoxImage.Error);
                eventArgs.Handled = true;
            };
            var store = new SqliteStore(Path.Combine(directory, "monii.db"));
            Theme.Apply(store.GetSettings());
            store.BackupWarning=updateWarning;
            var verificationIndex = Array.IndexOf(args, "--ui-verify");
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            if(verificationIndex>=0)
            {
                if(store.HasUsers||store.GetProducts().Count!=0||store.ReadOperations().Sales.Count!=0) throw new InvalidOperationException("La verificación UI solo admite una base nueva y aislada.");
                var testPassword=Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
                store.SaveUser(null,"verificacion","Administrador de verificación",Monii.Domain.UserRole.Administrador,true,testPassword);
                store.Authenticate("verificacion",testPassword);
            }
            else if(!SignIn(store)) { Shutdown(); return; }
            try { store.AutomaticBackup(); }
            catch (Exception backupError) { store.BackupWarning = "No se pudo crear el respaldo automático. Revisa la carpeta en Respaldos."; File.AppendAllText(Path.Combine(directory, "errors.log"), $"{DateTimeOffset.UtcNow:O} Respaldo automático: {backupError}\n"); }
            MainWindow = new MainWindow(new BusinessService(store), store);
            if(verificationIndex<0) { ((MainWindow)MainWindow).StartExchangeRefresh(); ((MainWindow)MainWindow).StartUpdateChecks(); }
            MainWindow.Show();
            ShutdownMode=ShutdownMode.OnMainWindowClose;
            if (verificationIndex >= 0)
            {
                if (index < 0 || verificationIndex + 1 >= args.Length) throw new ArgumentException("La verificación requiere --data-dir y un directorio de resultados explícitos.");
                var output = Path.GetFullPath(args[verificationIndex + 1]);
                Directory.CreateDirectory(output);
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() =>
                {
                    try
                    {
                        ((MainWindow)MainWindow).VerifyUi(output);
                        Shutdown(0);
                    }
                    catch (Exception error)
                    {
                        File.WriteAllText(Path.Combine(output, "ui-error.txt"), error.ToString());
                        Shutdown(1);
                    }
                }));
            }
        }
        catch (Exception error)
        {
            var verification=Array.IndexOf(e.Args,"--ui-verify");
            if(verification>=0&&verification+1<e.Args.Length)
            {
                var output=Path.GetFullPath(e.Args[verification+1]); Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output,"ui-error.txt"),error.ToString()); Shutdown(1); return;
            }
            MessageBox.Show("No se pudo iniciar Monii. Los datos se conservarán.\n\n" + error.Message, "Monii", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (ownsMutex) instanceMutex?.ReleaseMutex();
        instanceMutex?.Dispose();
        base.OnExit(e);
    }
    internal static bool SignIn(SqliteStore store) => new LoginWindow(store).ShowDialog()==true;
}
