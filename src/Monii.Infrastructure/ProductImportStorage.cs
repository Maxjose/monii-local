using System.Text.Json;
using Monii.Application;
using Monii.Domain;

namespace Monii.Infrastructure;
public sealed partial class SqliteStore
{
    public int ImportProducts(ProductImportPreview preview)
    {
        Require(Permission.Products);
        if(!preview.Valid||preview.Rows.Count>ProductImport.MaxRows) throw new ArgumentException("Corrige los errores de la vista previa antes de importar.");
        using var connection=Open(); using var tx=connection.BeginTransaction(deferred:false); using var cmd=connection.CreateCommand(); cmd.Transaction=tx;
        cmd.CommandText="SELECT payload FROM products"; var products=new List<Product>(); using(var reader=cmd.ExecuteReader()) while(reader.Read()) products.Add(JsonSerializer.Deserialize<Product>(reader.GetString(0))!);
        cmd.CommandText="SELECT id,name,active FROM categories"; var categories=new List<Category>(); using(var reader=cmd.ExecuteReader()) while(reader.Read()) categories.Add(new(Guid.Parse(reader.GetString(0)),reader.GetString(1),reader.GetBoolean(2)));
        var codes=new HashSet<string>(StringComparer.OrdinalIgnoreCase); var ids=new HashSet<Guid>();
        foreach(var row in preview.Rows)
        {
            var product=row.Product!; BusinessService.ValidateProduct(product);
            if(product.Id==Guid.Empty||product.Code!=product.Code.Trim()||!codes.Add(product.Code)||!ids.Add(product.Id)) throw new ArgumentException("Identificador o código duplicado en la importación.");
            var old=products.FirstOrDefault(p=>p.Id==product.Id);
            if(old is not null&&old.Unit!=product.Unit) throw new ArgumentException("La importación no cambia unidades existentes.");
            if(old!=row.Original||products.Any(p=>p.Id!=product.Id&&p.Code.Equals(product.Code,StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("El catálogo cambió desde la vista previa. Vuelve a validar el archivo.");
        }
        foreach(var category in preview.NewCategories)
        {
            if(category.Id==Guid.Empty||category.Name.Length is <1 or >120||category.Name!=category.Name.Trim()||!category.Active||categories.Any(c=>c.Id==category.Id||c.Name.Equals(category.Name,StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Las categorías cambiaron o no son válidas. Vuelve a validar.");
            cmd.Parameters.Clear(); cmd.CommandText="INSERT INTO categories VALUES($id,$name,1)"; cmd.Parameters.AddWithValue("$id",category.Id.ToString()); cmd.Parameters.AddWithValue("$name",category.Name); cmd.ExecuteNonQuery(); categories.Add(category);
        }
        foreach(var row in preview.Rows)
        {
            var product=row.Product!; ValidateProductCategory(connection,tx,product);
            cmd.Parameters.Clear(); cmd.CommandText="INSERT INTO products(id,code,payload) VALUES($id,$code,$payload) ON CONFLICT(id) DO UPDATE SET code=$code,payload=$payload";
            cmd.Parameters.AddWithValue("$id",product.Id.ToString()); cmd.Parameters.AddWithValue("$code",product.Code); cmd.Parameters.AddWithValue("$payload",JsonSerializer.Serialize(product)); cmd.ExecuteNonQuery();
        }
        cmd.Parameters.Clear(); cmd.CommandText="INSERT INTO audit(at,action,details,actor) VALUES($at,'Productos importados',$details,$actor)"; cmd.Parameters.AddWithValue("$at",DateTimeOffset.UtcNow.ToString("O")); cmd.Parameters.AddWithValue("$actor",CurrentUser?.Name??"Histórico sin usuario"); cmd.Parameters.AddWithValue("$details",JsonSerializer.Serialize(new { Rows=preview.Rows.Select(r=>new { r.Number,Before=r.Original,After=r.Product }),Categories=preview.NewCategories })); cmd.ExecuteNonQuery(); tx.Commit(); return preview.Rows.Count;
    }
}
