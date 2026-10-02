using Microsoft.Data.Sqlite;
using Monii.Infrastructure;
namespace Monii.Server;
public static partial class ServerHost
{
    public static void ExportLocal(string serverDirectory,string localDirectory)
    {
        if(string.IsNullOrWhiteSpace(localDirectory))throw new ArgumentException("Indica la carpeta local de Monii.");
        var local=Path.GetFullPath(localDirectory);var server=Path.GetFullPath(serverDirectory);
        if(local.Equals(server,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("La carpeta local debe ser distinta a los datos del servidor.");
        Directory.CreateDirectory(local);var target=Path.Combine(local,"monii.db");
        if(File.Exists(target+"-wal")||File.Exists(target+"-shm"))throw new ArgumentException("La base local tiene archivos de sesión. Cierra cualquier aplicación que esté usando esa base antes de continuar.");
        var database=Path.Combine(server,"monii.db");var sourceStore=new SqliteStore(database);
        if(sourceStore.ReadOperations().Sessions.Any(s=>s.ClosedAt is null))throw new ArgumentException("Cierra todas las cajas antes de desinstalar el servidor.");
        var temporary=Path.Combine(local,"uninstall-"+Guid.NewGuid().ToString("N")+".db");
        try
        {
            using(var source=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=database,Mode=SqliteOpenMode.ReadOnly,Pooling=false }.ToString()))
            using(var destination=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=temporary,Pooling=false }.ToString()))
            { source.Open();destination.Open();source.BackupDatabase(destination); }
            sourceStore.ValidateBackup(temporary);EnsureAdministratorBackup(temporary);
            if(File.Exists(target))
            {
                var previous=Path.Combine(local,"monii-before-server-uninstall-"+Guid.NewGuid().ToString("N")+".db");
                using var original=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=target,Mode=SqliteOpenMode.ReadOnly,Pooling=false }.ToString());original.Open();
                using var copy=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=previous,Pooling=false }.ToString());copy.Open();original.BackupDatabase(copy);
            }
            File.Move(temporary,target,overwrite:true);
        }
        finally { if(File.Exists(temporary))File.Delete(temporary); }
    }
    public static void MaintenanceCopy(string sourcePath,string targetPath,bool backup)
    {
        sourcePath=Path.GetFullPath(sourcePath);targetPath=Path.GetFullPath(targetPath);
        if(sourcePath.Equals(targetPath,StringComparison.OrdinalIgnoreCase)||!File.Exists(sourcePath)||backup&&File.Exists(targetPath))throw new ArgumentException("Rutas inválidas para el respaldo de mantenimiento.");
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        using var source=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=sourcePath,Mode=SqliteOpenMode.ReadOnly,Pooling=false }.ToString());source.Open();
        using var check=source.CreateCommand();check.CommandText="PRAGMA integrity_check";
        if(check.ExecuteScalar()?.ToString()!="ok")throw new ArgumentException("La base no supera la comprobación de integridad.");
        if(backup) { check.CommandText="SELECT count(*) FROM cash_sessions WHERE closed IS NULL";if(Convert.ToInt32(check.ExecuteScalar())>0)throw new ArgumentException("Cierra todas las cajas antes de actualizar el servidor."); }
        using var target=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=targetPath,Pooling=false }.ToString());target.Open();source.BackupDatabase(target);
    }
    public static void ActivateLocal(string localDirectory)
    {
        if(string.IsNullOrWhiteSpace(localDirectory)||!File.Exists(Path.Combine(localDirectory,"monii.db")))throw new ArgumentException("No se preparó la base local.");
        (ConnectionSettings.Load(localDirectory) with { Mode="Local" }).Save(localDirectory);
    }
}
