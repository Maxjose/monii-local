using Microsoft.Data.Sqlite;
using Monii.Domain;
using System.Text.Json;

namespace Monii.Infrastructure;
public sealed partial class SqliteStore
{
    private static void MigrateCategories(SqliteConnection c,SqliteTransaction tx)
    {
        using var cmd=c.CreateCommand(); cmd.Transaction=tx;
        cmd.CommandText="CREATE TABLE categories(id TEXT PRIMARY KEY,name TEXT NOT NULL COLLATE NOCASE UNIQUE,active INTEGER NOT NULL CHECK(active IN (0,1)))"; cmd.ExecuteNonQuery();
        cmd.CommandText="SELECT payload FROM products";
        var products=new List<Product>(); using(var r=cmd.ExecuteReader()) while(r.Read()) products.Add(JsonSerializer.Deserialize<Product>(r.GetString(0))!);
        var names=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var product in products)
        {
            var name=product.Category.Trim();
            if(name.Length==0)
            {
                if(product.Category.Length>0)
                {
                    cmd.Parameters.Clear(); cmd.CommandText="UPDATE products SET payload=$payload WHERE id=$id"; cmd.Parameters.AddWithValue("$id",product.Id.ToString()); cmd.Parameters.AddWithValue("$payload",JsonSerializer.Serialize(product with { Category="" })); cmd.ExecuteNonQuery();
                }
                continue;
            }
            if(!names.TryGetValue(name,out var canonical))
            {
                canonical=name; names.Add(name,name); cmd.Parameters.Clear(); cmd.CommandText="INSERT INTO categories VALUES($id,$name,1)"; cmd.Parameters.AddWithValue("$id",Guid.NewGuid().ToString()); cmd.Parameters.AddWithValue("$name",name); cmd.ExecuteNonQuery();
            }
            if(product.Category!=canonical)
            {
                cmd.Parameters.Clear(); cmd.CommandText="UPDATE products SET payload=$payload WHERE id=$id"; cmd.Parameters.AddWithValue("$id",product.Id.ToString()); cmd.Parameters.AddWithValue("$payload",JsonSerializer.Serialize(product with { Category=canonical })); cmd.ExecuteNonQuery();
            }
        }
        cmd.Parameters.Clear(); cmd.CommandText="PRAGMA user_version=4"; cmd.ExecuteNonQuery();
    }
    public IReadOnlyList<Category> GetCategories()
    {
        using var c=Open(); using var cmd=c.CreateCommand(); cmd.CommandText="SELECT id,name,active FROM categories ORDER BY name COLLATE NOCASE";
        using var r=cmd.ExecuteReader(); var categories=new List<Category>(); while(r.Read()) categories.Add(new(Guid.Parse(r.GetString(0)),r.GetString(1),r.GetBoolean(2))); return categories;
    }
    public void SaveCategory(Category category)
    {
        Require(Permission.Products); var name=category.Name.Trim();
        if(category.Id==Guid.Empty) throw new ArgumentException("Identificador de categoría inválido.");
        if(name.Length is <1 or >120) throw new ArgumentException("Escribe una categoría de 1 a 120 caracteres.");
        using var c=Open(); using var tx=c.BeginTransaction(deferred:false); using var cmd=c.CreateCommand(); cmd.Transaction=tx;
        cmd.CommandText="SELECT id,name FROM categories"; string? oldName=null;
        using(var r=cmd.ExecuteReader()) while(r.Read())
        {
            if(r.GetString(0)==category.Id.ToString()) oldName=r.GetString(1);
            else if(r.GetString(1).Equals(name,StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Ya existe una categoría con ese nombre.");
        }
        cmd.CommandText="INSERT INTO categories VALUES($id,$name,$active) ON CONFLICT(id) DO UPDATE SET name=$name,active=$active"; cmd.Parameters.AddWithValue("$id",category.Id.ToString()); cmd.Parameters.AddWithValue("$name",name); cmd.Parameters.AddWithValue("$active",category.Active?1:0); cmd.ExecuteNonQuery();
        if(oldName is not null&&oldName!=name)
        {
            cmd.Parameters.Clear(); cmd.CommandText="SELECT payload FROM products"; var products=new List<Product>(); using(var r=cmd.ExecuteReader()) while(r.Read()) products.Add(JsonSerializer.Deserialize<Product>(r.GetString(0))!);
            foreach(var p in products.Where(p=>p.Category.Equals(oldName,StringComparison.OrdinalIgnoreCase)))
            {
                cmd.Parameters.Clear(); cmd.CommandText="UPDATE products SET payload=$payload WHERE id=$id"; cmd.Parameters.AddWithValue("$id",p.Id.ToString()); cmd.Parameters.AddWithValue("$payload",JsonSerializer.Serialize(p with { Category=name })); cmd.ExecuteNonQuery();
            }
        }
        cmd.Parameters.Clear(); cmd.CommandText="INSERT INTO audit(at,action,details,actor) VALUES($at,'Categoría guardada',$details,$actor)"; cmd.Parameters.AddWithValue("$at",DateTimeOffset.UtcNow.ToString("O")); cmd.Parameters.AddWithValue("$details",JsonSerializer.Serialize(new { Before=oldName,After=category with { Name=name } })); cmd.Parameters.AddWithValue("$actor",CurrentUser?.Name??"Histórico sin usuario"); cmd.ExecuteNonQuery(); tx.Commit();
    }
    private static void ValidateProductCategory(SqliteConnection c,SqliteTransaction tx,Product product)
    {
        if(product.Category.Length==0) return;
        using var cmd=c.CreateCommand(); cmd.Transaction=tx; cmd.CommandText="SELECT name,active FROM categories";
        bool found=false,active=false; using(var r=cmd.ExecuteReader()) while(r.Read()) if(r.GetString(0).Equals(product.Category,StringComparison.OrdinalIgnoreCase)) { found=true; active=r.GetBoolean(1); }
        if(!found) throw new ArgumentException("Selecciona una categoría del listado. Puedes crearla en Categorías.");
        if(active) return;
        cmd.CommandText="SELECT payload FROM products WHERE id=$id"; cmd.Parameters.AddWithValue("$id",product.Id.ToString());
        var old=cmd.ExecuteScalar() is string json?JsonSerializer.Deserialize<Product>(json):null;
        if(old is null||!old.Category.Equals(product.Category,StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("No puedes asignar una categoría desactivada a otro producto.");
    }
}
