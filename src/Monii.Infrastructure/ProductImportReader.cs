using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Monii.Application;

namespace Monii.Infrastructure;

public static class ProductImportReader
{
    public static ProductImportFile Read(string path)
    {
        if(new FileInfo(path).Length>10*1024*1024) throw new ArgumentException("El archivo supera 10 MB.");
        return Path.GetExtension(path).ToLowerInvariant() switch { ".csv"=>ReadCsv(path),".xlsx"=>ReadExcel(path),_=>throw new ArgumentException("Usa un archivo .xlsx o .csv UTF-8. Los archivos .xls deben guardarse como .xlsx.") };
    }
    private static ProductImportFile File(string sheet,List<ImportSourceRow> rows)
    {
        rows=rows.Where(r=>r.Cells.Any(c=>!string.IsNullOrWhiteSpace(c.Value)||c.Formula)).ToList();
        if(rows.Count<2) throw new ArgumentException("El archivo debe incluir encabezados y al menos un producto.");
        if(rows[0].Cells.Any(c=>c.Formula)) throw new ArgumentException("Los encabezados no deben contener fórmulas.");
        if(rows.Count>ProductImport.MaxRows+1) throw new ArgumentException($"Máximo {ProductImport.MaxRows} productos por archivo.");
        return new(sheet,rows[0].Cells.Select(c=>c.Value.Trim().TrimStart('\uFEFF')).ToList(),rows.Skip(1).ToList());
    }
    private static ProductImportFile ReadCsv(string path)
    {
        string text;
        try { text=System.IO.File.ReadAllText(path,new UTF8Encoding(false,true)); }
        catch(DecoderFallbackException) { throw new ArgumentException("Guarda el CSV con codificación UTF-8."); }
        var first=text.Split(['\r','\n'],2)[0];
        int Count(char delimiter) { var quoted=false; var count=0; foreach(var c in first) { if(c=='"') quoted=!quoted; else if(c==delimiter&&!quoted) count++; } return count; }
        var delimiter=new[]{';',',','\t'}.OrderByDescending(Count).First();
        var rows=new List<ImportSourceRow>(); var cells=new List<ImportCell>(); var field=new StringBuilder();
        bool quoted=false,closed=false; var line=1; var start=1;
        void Cell() { if(field.Length>1000) throw new ArgumentException($"Fila {start}: campo demasiado largo."); cells.Add(new(field.ToString())); field.Clear(); closed=false; if(cells.Count>30) throw new ArgumentException("Demasiadas columnas."); }
        void Row() { Cell(); rows.Add(new(start,cells.ToArray())); cells.Clear(); if(rows.Count>ProductImport.MaxRows+100) throw new ArgumentException("Demasiadas filas."); }
        for(var i=0;i<text.Length;i++)
        {
            var c=text[i];
            if(quoted)
            {
                if(c=='"') { if(i+1<text.Length&&text[i+1]=='"') { field.Append('"'); i++; } else { quoted=false; closed=true; } }
                else { field.Append(c); if(c=='\n') line++; }
            }
            else if(c=='"') { if(field.Length>0||closed) throw new ArgumentException($"Fila {start}: comillas inválidas."); quoted=true; }
            else if(c==delimiter) Cell();
            else if(c is '\r' or '\n') { Row(); if(c=='\r'&&i+1<text.Length&&text[i+1]=='\n') i++; line++; start=line; }
            else { if(closed) throw new ArgumentException($"Fila {start}: texto después de comillas."); field.Append(c); }
        }
        if(quoted) throw new ArgumentException("CSV con comillas sin cerrar.");
        if(field.Length>0||cells.Count>0||closed) Row();
        return File("CSV",rows);
    }
    private static ProductImportFile ReadExcel(string path)
    {
        using var zip=ZipFile.OpenRead(path);
        if(zip.Entries.Count>2000||zip.Entries.Sum(e=>e.Length)>50*1024*1024) throw new ArgumentException("Excel demasiado grande al descomprimir.");
        XDocument Load(string name)
        {
            var entry=zip.GetEntry(name)??throw new ArgumentException("Excel incompleto: "+name);
            using var stream=entry.Open(); using var reader=XmlReader.Create(stream,new XmlReaderSettings { DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=50*1024*1024 }); return XDocument.Load(reader);
        }
        var workbook=Load("xl/workbook.xml");
        var sheet=workbook.Descendants().FirstOrDefault(e=>e.Name.LocalName=="sheet"&&(string?)e.Attribute("state") is null or "visible")??throw new ArgumentException("No hay una hoja visible.");
        var relId=sheet.Attributes().FirstOrDefault(a=>a.Name.LocalName=="id")?.Value;
        var relation=Load("xl/_rels/workbook.xml.rels").Descendants().SingleOrDefault(e=>e.Name.LocalName=="Relationship"&&(string?)e.Attribute("Id")==relId)??throw new ArgumentException("Hoja de Excel sin relación.");
        if((string?)relation.Attribute("TargetMode")=="External") throw new ArgumentException("No se admiten hojas externas.");
        var target=(string?)relation.Attribute("Target")??"";
        var sheetPath=target.StartsWith('/')?target.TrimStart('/'):"xl/"+target;
        if(sheetPath.Contains("..")||sheetPath.Contains('\\')) throw new ArgumentException("Ruta de hoja inválida.");
        var strings=zip.GetEntry("xl/sharedStrings.xml") is not null?Load("xl/sharedStrings.xml").Descendants().Where(e=>e.Name.LocalName=="si").Select(e=>string.Concat(e.Descendants().Where(t=>t.Name.LocalName=="t"&&!t.Ancestors().Any(a=>a.Name.LocalName=="rPh")).Select(t=>t.Value))).ToArray():[];
        var rows=new List<ImportSourceRow>();
        foreach(var row in Load(sheetPath).Descendants().Where(e=>e.Name.LocalName=="row"))
        {
            var number=int.TryParse((string?)row.Attribute("r"),out var parsed)?parsed:rows.Count+1;
            var cells=new List<ImportCell>();
            foreach(var cell in row.Elements().Where(e=>e.Name.LocalName=="c"))
            {
                var reference=(string?)cell.Attribute("r")??throw new ArgumentException("Celda sin referencia.");
                var index=0; foreach(var c in reference.TakeWhile(char.IsLetter)) { index=index*26+char.ToUpperInvariant(c)-'A'+1; if(index>30) throw new ArgumentException("Excel admite hasta 30 columnas."); }
                if(index<1||index<=cells.Count) throw new ArgumentException("Referencias de celdas inválidas.");
                while(cells.Count<index-1) cells.Add(new(""));
                var type=(string?)cell.Attribute("t"); var value=cell.Elements().FirstOrDefault(e=>e.Name.LocalName=="v")?.Value??"";
                if(type=="s") { if(!int.TryParse(value,out var id)||id<0||id>=strings.Length) throw new ArgumentException("Texto compartido inválido."); value=strings[id]; }
                else if(type=="inlineStr") value=string.Concat(cell.Descendants().Where(e=>e.Name.LocalName=="t").Select(e=>e.Value));
                else if(type=="b") value=value=="1"?"Si":"No";
                var formula=cell.Elements().Any(e=>e.Name.LocalName=="f")||type=="e";
                cells.Add(new(value,formula,(type is null or "n")&&value.Length>0));
            }
            rows.Add(new(number,cells)); if(rows.Count>ProductImport.MaxRows+100) throw new ArgumentException("Demasiadas filas en Excel.");
        }
        return File((string?)sheet.Attribute("name")??"Excel",rows);
    }
}
