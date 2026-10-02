using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Monii.Domain;

namespace Monii.Application;

public sealed record ImportCell(string Value, bool Formula=false, bool Numeric=false);
public sealed record ImportSourceRow(int Number,IReadOnlyList<ImportCell> Cells);
public sealed record ProductImportFile(string Sheet,IReadOnlyList<string> Headers,IReadOnlyList<ImportSourceRow> Rows);
public sealed record ProductImportRow(int Number,string Code,string Name,string Category,decimal? Cost,decimal? Price,string Action,string Error,Product? Product,Product? Original);
public sealed record ProductImportPreview(IReadOnlyList<ProductImportRow> Rows,IReadOnlyList<Category> NewCategories)
{
    public bool Valid=>Rows.Count>0&&Rows.All(r=>r.Error.Length==0&&r.Product is not null);
}

public static class ProductImport
{
    public const int MaxRows=5000;
    public const string Template="Codigo;Nombre;Categoria;CostoUSD;PrecioUSD;Unidad;Marca;Referencia;Compatibilidad;StockMinimo;Activo\r\n001234;Producto de ejemplo;;3.50;5.00;Unidad;;;;0;Si\r\n";
    private static string Key(string value)=>new string(value.Normalize(NormalizationForm.FormD).Where(c=>CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark&&char.IsLetterOrDigit(c)).ToArray()).ToLowerInvariant();
    public static ProductImportPreview Preview(ProductImportFile file,IReadOnlyList<Product> products,IReadOnlyList<Category> categories,bool updateExisting,bool createCategories)
    {
        if(file.Rows.Count>MaxRows) throw new ArgumentException($"Máximo {MaxRows} productos por archivo.");
        var allowed=new HashSet<string>(["codigo","nombre","categoria","costousd","preciousd","unidad","marca","referencia","compatibilidad","stockminimo","activo"]);
        var keys=file.Headers.Select(Key).ToArray();
        if(keys.Distinct().Count()!=keys.Length||keys.Any(k=>!allowed.Contains(k))) throw new ArgumentException("Encabezados duplicados o desconocidos. Usa las columnas de la plantilla.");
        foreach(var key in new[]{"codigo","nombre","costousd","preciousd"}) if(!keys.Contains(key)) throw new ArgumentException("Falta la columna "+key+". Usa la plantilla.");
        var existing=products.ToDictionary(p=>p.Code,StringComparer.OrdinalIgnoreCase);
        var catalog=categories.ToDictionary(c=>c.Name,StringComparer.OrdinalIgnoreCase);
        var pending=new Dictionary<string,Category>(StringComparer.OrdinalIgnoreCase);
        var duplicateCodes=file.Rows.Select(r=>r.Cells.ElementAtOrDefault(Array.IndexOf(keys,"codigo"))?.Value.Trim()??"").GroupBy(c=>c,StringComparer.OrdinalIgnoreCase).Where(g=>g.Count()>1).Select(g=>g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result=new List<ProductImportRow>();
        foreach(var row in file.Rows)
        {
            string Read(string key,string fallback="") { var index=Array.IndexOf(keys,key); return index<0?fallback:row.Cells.ElementAtOrDefault(index)?.Value.Trim()??""; }
            var code=Read("codigo"); var name=Read("nombre"); existing.TryGetValue(code,out var original);
            var category=Read("categoria",original?.Category??""); decimal? cost=null,price=null; Product? product=null; var errors=new List<string>();
            try
            {
                if(row.Cells.Count>keys.Length&&row.Cells.Skip(keys.Length).Any(c=>c.Value.Length>0)) throw new ArgumentException("Hay más valores que columnas.");
                if(row.Cells.Any(c=>c.Formula)) throw new ArgumentException("Las fórmulas no se importan; pega sus resultados como valores.");
                if(row.Cells.ElementAtOrDefault(Array.IndexOf(keys,"codigo"))?.Numeric==true) throw new ArgumentException("El código de Excel debe ser texto para conservar ceros y dígitos.");
                if(code.Length>128||name.Length>250||category.Length>120||row.Cells.Any(c=>c.Value.Length>1000)) throw new ArgumentException("Un campo supera la longitud permitida.");
                if(duplicateCodes.Contains(code)) throw new ArgumentException("Código repetido dentro del archivo.");
                if(original is not null&&!updateExisting) throw new ArgumentException("El código ya existe. Activa actualizar existentes o retira esta fila.");
                static decimal Number(string value,string label)
                {
                    if(!Regex.IsMatch(value,@"^\d+(?:[.,]\d+)?$",RegexOptions.None,TimeSpan.FromSeconds(1))||!decimal.TryParse(value.Replace(',','.'),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var parsed)) throw new ArgumentException(label+": usa números sin separadores de miles.");
                    return parsed;
                }
                cost=Number(Read("costousd"),"Costo USD"); price=Number(Read("preciousd"),"Precio USD");
                var unit=Read("unidad",original?.Unit??"Unidad");
                var canonicalUnit=new[]{"Unidad","Kilogramo","Litro","Metro","Caja"}.FirstOrDefault(u=>Key(u)==Key(unit))??throw new ArgumentException("Unidad inválida: Unidad, Kilogramo, Litro, Metro o Caja.");
                if(original is not null&&original.Unit!=canonicalUnit) throw new ArgumentException("La importación no cambia la unidad de productos existentes.");
                var activeText=Read("activo",original?.Active==false?"No":"Si");
                var active=Key(activeText) switch { "si" or "true" or "1"=>true,"no" or "false" or "0"=>false,_=>throw new ArgumentException("Activo debe ser Si o No.") };
                product=(original??new Product()) with { Code=code,Name=name,Category=category,CostUsd=cost.Value,PriceUsd=price.Value,Unit=canonicalUnit,Brand=Read("marca",original?.Brand??""),Reference=Read("referencia",original?.Reference??""),Compatibility=Read("compatibilidad",original?.Compatibility??""),MinimumStock=Number(Read("stockminimo",(original?.MinimumStock??0).ToString(CultureInfo.InvariantCulture)),"Stock mínimo"),Active=active };
                BusinessService.ValidateProduct(product);
                if(category.Length>0)
                {
                    if(catalog.TryGetValue(category,out var found))
                    {
                        if(!found.Active&&(original is null||!original.Category.Equals(found.Name,StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("La categoría está desactivada.");
                        product=product with { Category=found.Name }; category=found.Name;
                    }
                    else if(!createCategories) throw new ArgumentException("Categoría inexistente; créala o activa crear categorías faltantes.");
                    else
                    {
                        if(!pending.TryGetValue(category,out var planned)) { planned=new(Guid.NewGuid(),category); pending.Add(category,planned); }
                        product=product with { Category=planned.Name }; category=planned.Name;
                    }
                }
            }
            catch(ArgumentException error) { errors.Add(error.Message); product=null; }
            result.Add(new(row.Number,code,name,category,cost,price,original is null?"Crear":"Actualizar",string.Join(" ",errors),product,original));
        }
        return new(result,pending.Values.ToList());
    }
}
