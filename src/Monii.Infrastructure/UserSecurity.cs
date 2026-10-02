using Microsoft.Data.Sqlite;
using Monii.Domain;
using System.Security.Cryptography;

namespace Monii.Infrastructure;

public sealed partial class SqliteStore
{
    public SessionUser? CurrentUser { get; private set; }
    public bool HasUsers
    {
        get { using var c=Open(); using var cmd=c.CreateCommand(); cmd.CommandText="SELECT COUNT(*) FROM users"; return Convert.ToInt32(cmd.ExecuteScalar())>0; }
    }
    public void SignOut() => CurrentUser=null;
    public static bool Allowed(UserRole role, Permission permission) => role == UserRole.Administrador || role switch
    {
        UserRole.Cajero => permission is Permission.Sell or Permission.Customers or Permission.Credit or Permission.Cash,
        UserRole.Vendedor => permission is Permission.Sell or Permission.Customers,
        _ => false
    };
    public bool Can(Permission permission)
    {
        if (!HasUsers) return true; // Legacy/test bootstrap only. Desktop requires account setup before opening the shell.
        if (CurrentUser is not { } session) return false;
        using var c=Open(); using var cmd=c.CreateCommand(); cmd.CommandText="SELECT role FROM users WHERE id=$id AND active=1"; cmd.Parameters.AddWithValue("$id",session.Id.ToString());
        return cmd.ExecuteScalar() is long role && Allowed((UserRole)role,permission);
    }
    public void Require(Permission permission) { if(!Can(permission)) throw new UnauthorizedAccessException("Tu usuario no tiene permiso para esta operación. Inicia sesión con una cuenta autorizada."); }
    private static Permission ActionPermission(string action) => action switch
    {
        "Venta registrada"=>Permission.Sell, "Cliente guardado"=>Permission.Customers, "Proveedor guardado" or "Compra registrada"=>Permission.Purchases,
        "Caja abierta" or "Caja cerrada"=>Permission.Cash, "Movimiento de caja"=>Permission.Expenses, "Abono registrado"=>Permission.Credit,
        "Ajuste de inventario"=>Permission.Inventory, "Venta anulada" or "Compra anulada" or "Abono anulado" or "Devolución de venta"=>Permission.Void,
        _=>throw new UnauthorizedAccessException("Operación no autorizada: "+action)
    };
    public IReadOnlyList<UserAccount> Users()
    {
        Require(Permission.Users); using var c=Open(); using var cmd=c.CreateCommand(); cmd.CommandText="SELECT id,username,name,role,active FROM users ORDER BY name";
        using var r=cmd.ExecuteReader(); var users=new List<UserAccount>(); while(r.Read()) users.Add(new(Guid.Parse(r.GetString(0)),r.GetString(1),r.GetString(2),(UserRole)r.GetInt32(3),r.GetBoolean(4))); return users;
    }
    public void SaveUser(Guid? id,string username,string name,UserRole role,bool active,string? password)
    {
        var initial=!HasUsers; if(!initial) Require(Permission.Users);
        if(string.IsNullOrWhiteSpace(username)||string.IsNullOrWhiteSpace(name)||!Enum.IsDefined(role)) throw new ArgumentException("Completa nombre, usuario y rol válido.");
        if(initial && (role!=UserRole.Administrador||!active)) throw new ArgumentException("La primera cuenta debe ser un administrador activo.");
        if(password is not null && password.Length<12) throw new ArgumentException("La contraseña debe tener al menos 12 caracteres.");
        using var c=Open(); using var tx=c.BeginTransaction(deferred:false); using var cmd=c.CreateCommand(); cmd.Transaction=tx;
        cmd.CommandText="SELECT COUNT(*) FROM users WHERE active=1 AND role=0 AND id<>$id"; cmd.Parameters.AddWithValue("$id",id?.ToString()??"");
        if(!initial && (!active||role!=UserRole.Administrador) && Convert.ToInt32(cmd.ExecuteScalar())==0)
        {
            cmd.CommandText="SELECT role FROM users WHERE id=$id";
            if(cmd.ExecuteScalar() is long existingRole && existingRole==0) throw new ArgumentException("Conserva al menos un administrador activo.");
        }
        var key=id??Guid.NewGuid(); string? salt=null,hash=null;
        if(password is not null) { var random=RandomNumberGenerator.GetBytes(32); salt=Convert.ToBase64String(random); hash=Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(password,random,600000,HashAlgorithmName.SHA256,32)); }
        if(id is null && hash is null) throw new ArgumentException("Define una contraseña para el nuevo usuario.");
        if(id is not null&&hash is null)
        {
            cmd.Parameters.Clear(); cmd.CommandText="SELECT salt,password_hash FROM users WHERE id=$id"; cmd.Parameters.AddWithValue("$id",id.ToString());
            using var reader=cmd.ExecuteReader(); if(!reader.Read()) throw new ArgumentException("La cuenta no existe."); salt=reader.GetString(0); hash=reader.GetString(1);
        }
        cmd.Parameters.Clear(); cmd.CommandText="INSERT INTO users(id,username,name,role,active,salt,password_hash) VALUES($id,$username,$name,$role,$active,$salt,$hash) ON CONFLICT(id) DO UPDATE SET username=$username,name=$name,role=$role,active=$active,salt=COALESCE($salt,users.salt),password_hash=COALESCE($hash,users.password_hash),failures=CASE WHEN $reset=0 THEN users.failures ELSE 0 END,locked_until=CASE WHEN $reset=0 THEN users.locked_until ELSE NULL END";
        cmd.Parameters.AddWithValue("$id",key.ToString()); cmd.Parameters.AddWithValue("$username",username.Trim()); cmd.Parameters.AddWithValue("$name",name.Trim()); cmd.Parameters.AddWithValue("$role",(int)role); cmd.Parameters.AddWithValue("$active",active?1:0); cmd.Parameters.AddWithValue("$salt",(object?)salt??DBNull.Value); cmd.Parameters.AddWithValue("$hash",(object?)hash??DBNull.Value); cmd.Parameters.AddWithValue("$reset",password is null?0:1); cmd.ExecuteNonQuery();
        cmd.Parameters.Clear(); cmd.CommandText="INSERT INTO audit(at,action,details,actor) VALUES($at,'Usuario guardado',$details,$actor)"; cmd.Parameters.AddWithValue("$at",DateTimeOffset.UtcNow.ToString("O")); cmd.Parameters.AddWithValue("$details",$"{username.Trim()} · {role} · {(active?"Activo":"Inactivo")}"); cmd.Parameters.AddWithValue("$actor",CurrentUser?.Name??"Configuración inicial"); cmd.ExecuteNonQuery(); tx.Commit();
    }
    public SessionUser Authenticate(string username,string password)
    {
        CurrentUser=null; using var c=Open(); using var tx=c.BeginTransaction(deferred:false); using var cmd=c.CreateCommand(); cmd.Transaction=tx;
        cmd.CommandText="SELECT id,name,role,active,salt,password_hash,failures,locked_until FROM users WHERE username=$username COLLATE NOCASE"; cmd.Parameters.AddWithValue("$username",username.Trim());
        Guid id; string name,salt,hash; UserRole role; int failures; bool active; DateTimeOffset? locked;
        using(var r=cmd.ExecuteReader())
        {
            if(!r.Read()) { _=Rfc2898DeriveBytes.Pbkdf2(password,new byte[32],600000,HashAlgorithmName.SHA256,32); throw new UnauthorizedAccessException("Usuario o contraseña incorrectos."); }
            id=Guid.Parse(r.GetString(0)); name=r.GetString(1); role=(UserRole)r.GetInt32(2); active=r.GetBoolean(3); salt=r.GetString(4); hash=r.GetString(5); failures=r.GetInt32(6); locked=r.IsDBNull(7)?null:DateTimeOffset.Parse(r.GetString(7));
        }
        if(locked>DateTimeOffset.UtcNow) throw new UnauthorizedAccessException("Cuenta temporalmente bloqueada. Intenta en cinco minutos.");
        var valid=CryptographicOperations.FixedTimeEquals(Rfc2898DeriveBytes.Pbkdf2(password,Convert.FromBase64String(salt),600000,HashAlgorithmName.SHA256,32),Convert.FromBase64String(hash))&&active;
        cmd.Parameters.Clear(); cmd.Parameters.AddWithValue("$id",id.ToString());
        cmd.CommandText="UPDATE users SET failures=$failures,locked_until=$locked WHERE id=$id"; cmd.Parameters.AddWithValue("$failures",valid?0:failures+1); cmd.Parameters.AddWithValue("$locked",!valid&&failures+1>=5?(object)DateTimeOffset.UtcNow.AddMinutes(5).ToString("O"):DBNull.Value); cmd.ExecuteNonQuery();
        tx.Commit(); if(!valid) throw new UnauthorizedAccessException("Usuario o contraseña incorrectos.");
        CurrentUser=new(id,username.Trim(),name,role); return CurrentUser;
    }
}
