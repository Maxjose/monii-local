using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Monii.Application;
using Monii.Domain;
using Monii.Infrastructure;

internal static class ImportChecks
{
    public static void Run()
    {
        var root=Path.Combine(Path.GetTempPath(),"MoniiImport",Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var store=new SqliteStore(Path.Combine(root,"monii.db")); var business=new BusinessService(store); var ops=new OperationsService(store); var count=0;
        void Check(bool okay,string label) { if(!okay) throw new Exception("FALLÓ IMPORTACIÓN: "+label); count++; Console.WriteLine("OK IMPORTACIÓN: "+label); }
        void Reject(Action action,string label) { try { action(); } catch(Exception e) when(e is ArgumentException or UnauthorizedAccessException or InvalidDataException or System.Xml.XmlException) { Check(true,label); return; } throw new Exception("FALLÓ IMPORTACIÓN: "+label); }
        ProductImportFile Csv(string text) { var path=Path.Combine(root,Guid.NewGuid()+".csv"); File.WriteAllText(path,text,new UTF8Encoding(true)); return ProductImportReader.Read(path); }
        ProductImportPreview Preview(ProductImportFile file,bool update=false,bool categories=false)=>ProductImport.Preview(file,store.GetProducts(),store.GetCategories(),update,categories);
        string Snapshot()=>JsonSerializer.Serialize(new { Products=store.GetProducts(),Categories=store.GetCategories(),State=ops.State,Audit=store.GetAudit() });
        const string headers="Codigo;Nombre;Categoria;CostoUSD;PrecioUSD\n";
        var file=Csv(headers+"00123;Filtro;Motor;3,25;9.50\n00042;Tornillo;motor;0;1\n");
        var preview=Preview(file);
        Check(!preview.Valid&&preview.Rows.All(r=>r.Error.Contains("Categoría inexistente")),"Categorías faltantes requieren opción explícita");
        preview=Preview(file,categories:true);
        Check(preview.Valid&&preview.NewCategories.Count==1&&preview.Rows[0].Product!.Code=="00123","CSV con coma decimal y códigos con ceros");
        Check(preview.Rows.All(r=>r.Product!.Category=="Motor"),"Categorías equivalentes usan nombre canónico");
        Check(store.GetProducts().Count==0&&store.GetCategories().Count==0,"Vista previa no modifica SQLite");
        Check(store.ImportProducts(preview)==2&&store.GetProducts().Count==2&&store.GetCategories().Count==1,"Importar productos y categorías juntos");
        Check(store.GetAudit().First().Action=="Productos importados","Importación registra auditoría con documentos");
        Check(new SqliteStore(store.DatabasePath).GetProducts().Any(p=>p.Code=="00042"),"Importación persiste al reabrir");
        Reject(()=>store.ImportProducts(preview),"Repetir una vista previa no duplica productos");
        Check(!Preview(file).Valid,"Códigos existentes bloqueados por defecto");
        var unit=store.GetProducts().Single(p=>p.Code=="00123"); ops.Adjust(unit.Id,10,"Existencias previas"); ops.OpenCash(100,0,0);
        var sale=ops.Sell([(unit.Id,1)],0,[new(Currency.USD,PaymentMethod.Efectivo,9.50m)]);
        var changed=Csv("Codigo;Nombre;CostoUSD;PrecioUSD\n00123;Filtro actualizado;4;12\n");
        var update=Preview(changed,true); store.ImportProducts(update);
        Check(store.GetProducts().Single(p=>p.Code=="00123").Id==unit.Id&&store.StockBalance(unit.Id)==9,"Actualizar conserva ID y existencias");
        Check(ops.State.Sales.Single().Lines.Single().Name=="Filtro"&&ops.State.Sales.Single().Lines.Single().Price==9.50m&&ops.State.Sales.Single().Lines.Single().Cost==3.25m,"Actualizar no cambia venta ni costo histórico");
        Check(store.GetProducts().Single(p=>p.Code=="00123").Category=="Motor","Columna opcional ausente conserva categoría");
        var stale=Preview(changed,true); business.Save(store.GetProducts().Single(p=>p.Code=="00123") with { PriceUsd=13 }); var before=Snapshot();
        Reject(()=>store.ImportProducts(stale),"Cambio posterior invalida vista previa"); Check(Snapshot()==before,"Vista previa obsoleta no escribe parcialmente");
        Check(!Preview(Csv(headers+"DUP;A;;1;2\ndup;B;;1;2")).Valid,"Duplicados internos ignoran mayúsculas");
        Check(!Preview(Csv(headers+"BAD;A;;-1;2")).Valid,"Costos negativos rechazados");
        Check(!Preview(Csv(headers+"BAD;A;;1.001;2")).Valid,"Más de dos decimales rechazado");
        Check(!Preview(Csv(headers+"BAD;A;;1.000,50;2")).Valid,"Separadores de miles ambiguos rechazados");
        Check(!Preview(Csv(headers+"BAD;; ;1;2")).Valid,"Nombre vacío rechazado");
        Check(!Preview(Csv(headers+"BAD;A;;1;2;extra")).Valid,"Valores extra rechazados");
        Reject(()=>Preview(Csv("Codigo;Nombre;CostoUSD;Stock\nA;B;1;3")),"Columnas desconocidas y falta de precio rechazadas");
        var quoted=Preview(Csv("Codigo,Nombre,CostoUSD,PrecioUSD\nQ,\"Nombre, con \"\"comillas\"\"\",\"1,25\",2.50"));
        Check(quoted.Valid&&quoted.Rows.Single().Name=="Nombre, con \"comillas\""&&quoted.Rows.Single().Cost==1.25m,"CSV separado por coma con escapes correctos");
        Check(Preview(Csv("Codigo\tNombre\tCostoUSD\tPrecioUSD\nTAB\tProducto\t1\t2")).Valid,"CSV con tabuladores");
        Reject(()=>Csv(headers+"A;\"sin cierre;1;2"),"Comillas sin cerrar rechazadas");
        var inactiveCategory=store.GetCategories().Single(); store.SaveCategory(inactiveCategory with { Active=false });
        Check(!Preview(Csv(headers+"NEW;Nuevo;Motor;1;2"),categories:true).Valid,"No asignar categoría inactiva a nuevos");
        Check(Preview(changed,true).Valid,"Actualizar mantiene categoría inactiva ya asignada");
        var newBatch=Preview(Csv(headers+"NEW;Nuevo;Nueva;1;2"),categories:true);
        var forged=newBatch with { Rows=newBatch.Rows.Concat([new ProductImportRow(3,"INVALID","Inválido","Motor",1,2,"Crear","",new Product { Code="INVALID",Name="Inválido",Category="Motor",CostUsd=1,PriceUsd=2 },null)]).ToList() };
        before=Snapshot(); Reject(()=>store.ImportProducts(forged),"Fallo tardío de categoría revierte todo el lote"); Check(Snapshot()==before,"Rollback incluye categorías nuevas, productos y auditoría");
        var unitChange=Csv("Codigo;Nombre;CostoUSD;PrecioUSD;Unidad\n00123;Filtro;1;2;Kilogramo"); Check(!Preview(unitChange,true).Valid,"Importación no cambia unidades existentes");
        var template=Preview(Csv(ProductImport.Template)); Check(template.Valid,"Plantilla incluida se puede importar");
        Reject(()=>Preview(new ProductImportFile("Límite",file.Headers,Enumerable.Repeat(file.Rows[0],ProductImport.MaxRows+1).ToList())),"Límite de 5.000 productos validado");
        var oversized=Path.Combine(root,"oversized.csv"); using(var stream=File.Create(oversized)) stream.SetLength(10*1024*1024+1);
        Reject(()=>ProductImportReader.Read(oversized),"Límite de 10 MB validado antes de leer");
        // Minimal real Open XML packages exercise shared strings, inline strings and sparse cells.
        string Excel(string codeCell,string priceCell="<c r=\"D2\"><v>4.50</v></c>",string headerExtra="")
        {
            var path=Path.Combine(root,Guid.NewGuid()+".xlsx"); using var zip=ZipFile.Open(path,ZipArchiveMode.Create);
            void Put(string name,string value) { using var writer=new StreamWriter(zip.CreateEntry(name).Open()); writer.Write(value); }
            Put("xl/workbook.xml","<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Productos\" sheetId=\"1\" r:id=\"rId1\" /></sheets></workbook>");
            Put("xl/_rels/workbook.xml.rels","<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Target=\"worksheets/sheet1.xml\" /></Relationships>");
            Put("xl/sharedStrings.xml","<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><si><t>000001</t></si></sst>");
            Put("xl/worksheets/sheet1.xml","<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>Codigo</t></is>"+headerExtra+"</c><c r=\"B1\" t=\"inlineStr\"><is><t>Nombre</t></is></c><c r=\"C1\" t=\"inlineStr\"><is><t>CostoUSD</t></is></c><c r=\"D1\" t=\"inlineStr\"><is><t>PrecioUSD</t></is></c></row><row r=\"2\">"+codeCell+"<c r=\"B2\" t=\"inlineStr\"><is><t>Producto Excel</t></is></c><c r=\"C2\"><v>2.25</v></c>"+priceCell+"</row></sheetData></worksheet>"); return path;
        }
        var excel=ProductImportReader.Read(Excel("<c r=\"A2\" t=\"s\"><v>0</v></c>"));
        Check(Preview(excel).Valid&&excel.Sheet=="Productos"&&Preview(excel).Rows.Single().Code=="000001","Excel compartido conserva ceros y decimales");
        Check(!Preview(ProductImportReader.Read(Excel("<c r=\"A2\"><v>123</v></c>"))).Valid,"Código numérico de Excel bloqueado por pérdida de ceros");
        Check(!Preview(ProductImportReader.Read(Excel("<c r=\"A2\" t=\"inlineStr\"><is><t>FORMULA</t></is></c>","<c r=\"D2\"><f>2+2</f><v>4</v></c>"))).Valid,"Fórmulas Excel rechazadas aunque tengan resultado almacenado");
        Reject(()=>ProductImportReader.Read(Excel("<c r=\"A2\" t=\"s\"><v>0</v></c>",headerExtra:"<f>1</f>")),"Fórmulas en encabezados rechazadas");
        Check(!Preview(ProductImportReader.Read(Excel("<c r=\"A2\" t=\"s\"><v>0</v></c>",""))).Valid,"Celda vacía de precio detectada");
        var backup=store.Backup(Path.Combine(root,"import-backup.db")); business.Save(store.GetProducts().Single(p=>p.Code=="00123") with { PriceUsd=99 }); store.Restore(backup);
        Check(store.GetProducts().Single(p=>p.Code=="00123").PriceUsd==13&&ops.State.Sales.Single().Id==sale.Id,"Respaldo de catálogo importado conserva historia");
        const string password="Import-test-password-strong"; store.SaveUser(null,"admin","Admin",UserRole.Administrador,true,password); store.Authenticate("admin",password); store.SaveUser(null,"seller","Seller",UserRole.Vendedor,true,password); store.Authenticate("seller",password);
        Reject(()=>store.ImportProducts(template),"Vendedor no puede importar llamando al almacenamiento");
        Console.WriteLine($"IMPORTACIÓN COMPLETA: {count} comprobaciones. Datos: {root}");
    }
}
