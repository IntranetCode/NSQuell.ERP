using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ERP.NSQuell.Controllers;

public partial class EscalaPersonalController
{
    private sealed class PoliPersonaV7 { public int PersonaID; public string Control=""; public string Nombre=""; public string Puesto=""; public int Row; }
    private sealed class PoliParteV7 { public int ParteID; public string Numero=""; public string Sap=""; public string Designacion=""; public string Descripcion=""; }
    private sealed class PoliCompetenciaV7 { public int PersonaID; public string Control=""; public string Puesto=""; public int ParteID; public string Clave=""; public string Encabezado=""; public int Nivel; }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(60_000_000)]
    public async Task<IActionResult> ImportarPolivalenciaV7(IFormFile archivo)
    {
        if(archivo==null||archivo.Length<=0){TempData["Error"]="Selecciona el archivo XLSX de la matriz.";return RedirectToAction(nameof(Polivalencia));}
        if(!Path.GetExtension(archivo.FileName).Equals(".xlsx",StringComparison.OrdinalIgnoreCase)){TempData["Error"]="La matriz debe ser un archivo .xlsx.";return RedirectToAction(nameof(Polivalencia));}
        if(archivo.Length>55_000_000){TempData["Error"]="El archivo excede 55 MB.";return RedirectToAction(nameof(Polivalencia));}

        await using var ms=new MemoryStream();await archivo.CopyToAsync(ms);var bytes=ms.ToArray();var hash=Convert.ToHexString(SHA256.HashData(bytes));
        try
        {
            using var wb=new XLWorkbook(new MemoryStream(bytes));
            var ws=wb.Worksheets
                .Where(x=>!x.Name.Contains("OBSOLETA",StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(x=>string.Equals((x.Cell("D2").GetString()??"").Trim(),"GQ-F-RH02-05",StringComparison.OrdinalIgnoreCase))
                ?? wb.Worksheets
                    .Where(x=>!x.Name.Contains("OBSOLETA",StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault(x=>x.CellsUsed().Take(30).Any(c=>c.GetString().Contains("GQ-F-RH02-05",StringComparison.OrdinalIgnoreCase)))
                ?? throw new InvalidOperationException("No se encontró la hoja vigente GQ-F-RH02-05.");
            var fuente=(ws.Cell("D2").GetString()??"").Trim();if(!fuente.Equals("GQ-F-RH02-05",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("El documento no corresponde a GQ-F-RH02-05.");
            var mes=BuscarValorJuntoEtiqueta(ws,"MES")??throw new InvalidOperationException("No se pudo identificar el mes de la matriz.");
            var anioTxt=BuscarValorJuntoEtiqueta(ws,"AÑO")??BuscarValorJuntoEtiqueta(ws,"ANO")??throw new InvalidOperationException("No se pudo identificar el año.");
            if(!int.TryParse(Regex.Match(anioTxt,@"\d{4}").Value,out var anio)||anio<2020||anio>2100)throw new InvalidOperationException("El año de la matriz no es válido.");
            var vm=Regex.Match(ws.Name,@"(\d+)");var version=vm.Success?vm.Groups[1].Value:"12";
            var headerRow=EncontrarFilaCabecera(ws);if(headerRow<=0)throw new InvalidOperationException("No se encontró la cabecera Nombre / No. de control / Puesto.");
            var pieceRow=headerRow-1;

            await using var cn=new SqlConnection(_connectionString);await cn.OpenAsync();

            const string estructuraSql = @"
SELECT
    @@SERVERNAME AS Servidor,
    DB_NAME() AS BaseDatos,
    CONVERT(bit,CASE WHEN OBJECT_ID(N'dbo.RRHH_PolivalenciaImportaciones',N'U') IS NULL THEN 0 ELSE 1 END) AS TieneAuditoria,
    CONVERT(bit,CASE WHEN OBJECT_ID(N'dbo.RRHH_PolivalenciaCompetencias',N'U') IS NULL THEN 0 ELSE 1 END) AS TieneMatriz;";

            string servidorActual;
            string baseActual;
            bool tieneAuditoria;
            bool tieneMatriz;

            await using (var estructuraCmd = new SqlCommand(estructuraSql,cn))
            await using (var estructuraRd = await estructuraCmd.ExecuteReaderAsync())
            {
                await estructuraRd.ReadAsync();
                servidorActual = estructuraRd["Servidor"]?.ToString() ?? "(desconocido)";
                baseActual = estructuraRd["BaseDatos"]?.ToString() ?? "(desconocida)";
                tieneAuditoria = Convert.ToBoolean(estructuraRd["TieneAuditoria"]);
                tieneMatriz = Convert.ToBoolean(estructuraRd["TieneMatriz"]);
            }

            if (!tieneAuditoria || !tieneMatriz)
            {
                throw new InvalidOperationException(
                    $"Falta estructura V7 en la BD conectada por el ERP: {servidorActual} / {baseActual}. " +
                    "Ejecuta el SQL V7.3 de reparación en ESA misma instancia.");
            }
            const string duplicado=@"SELECT COUNT(1) FROM dbo.RRHH_PolivalenciaImportaciones WHERE HashSHA256=@Hash AND Resultado IN(N'EXITO',N'SIN_CAMBIOS');";await using(var cmd=new SqlCommand(duplicado,cn)){cmd.Parameters.Add("@Hash",SqlDbType.VarChar,64).Value=hash;if(Convert.ToInt32(await cmd.ExecuteScalarAsync())>0){TempData["Exito"]="Este mismo archivo ya fue procesado correctamente. No se duplicaron registros.";return RedirectToAction(nameof(Polivalencia));}}
            var personasDb=await CargarPersonasPoliV7Async(cn);var partes=await CargarPartesPoliV7Async(cn);
            var personas=ExtraerPersonasV7(ws,headerRow,personasDb);
            if(personas.Count==0)throw new InvalidOperationException("No se encontraron operadores/auxiliares válidos en la matriz.");
            if(personas.GroupBy(x=>x.Control,StringComparer.OrdinalIgnoreCase).Any(g=>g.Count()>1))throw new InvalidOperationException("La matriz contiene números de control duplicados.");
            var comps=ExtraerCompetenciasV7(ws,pieceRow,personas,partes);
            if(comps.Count==0)throw new InvalidOperationException("No se encontraron competencias N1-N4 para importar.");

            if (await MatrizActivaCoincideV7Async(comps,cn))
            {
                const string sinCambios = @"
INSERT dbo.RRHH_PolivalenciaImportaciones
(
    NombreArchivo,HashSHA256,FuenteDocumento,VersionDocumento,Mes,Anio,
    TotalPersonas,TotalPartes,TotalCompetencias,PersonasDesactivadas,
    UsuarioID,Resultado,Observaciones
)
VALUES
(
    @Archivo,@Hash,@Fuente,@Version,@Mes,@Anio,
    @Personas,@Partes,@Competencias,0,
    @Usuario,N'SIN_CAMBIOS',N'La matriz activa ya coincidía exactamente con el XLSX.'
);";

                await using var sinCambiosCmd = new SqlCommand(sinCambios,cn);
                sinCambiosCmd.Parameters.Add("@Archivo",SqlDbType.NVarChar,260).Value=Path.GetFileName(archivo.FileName);
                sinCambiosCmd.Parameters.Add("@Hash",SqlDbType.VarChar,64).Value=hash;
                sinCambiosCmd.Parameters.Add("@Fuente",SqlDbType.NVarChar,100).Value=fuente;
                sinCambiosCmd.Parameters.Add("@Version",SqlDbType.NVarChar,50).Value=version;
                sinCambiosCmd.Parameters.Add("@Mes",SqlDbType.NVarChar,30).Value=mes;
                sinCambiosCmd.Parameters.Add("@Anio",SqlDbType.Int).Value=anio;
                sinCambiosCmd.Parameters.Add("@Personas",SqlDbType.Int).Value=personas.Count;
                sinCambiosCmd.Parameters.Add("@Partes",SqlDbType.Int).Value=comps.Select(x=>x.ParteID).Distinct().Count();
                sinCambiosCmd.Parameters.Add("@Competencias",SqlDbType.Int).Value=comps.Count;
                sinCambiosCmd.Parameters.Add("@Usuario",SqlDbType.Int).Value=(object?)ObtenerUsuarioID()??DBNull.Value;
                await sinCambiosCmd.ExecuteNonQueryAsync();

                TempData["Exito"] =
                    $"La matriz ya estaba actualizada: {personas.Count} personas, " +
                    $"{comps.Select(x=>x.ParteID).Distinct().Count()} partes y {comps.Count} competencias. " +
                    "No se duplicaron registros.";
                return RedirectToAction(nameof(Polivalencia));
            }

            var usuario=ObtenerUsuarioID();
            await using var tx=(SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var anteriores=new HashSet<int>();const string ant=@"SELECT DISTINCT PersonalID FROM dbo.RRHH_PolivalenciaCompetencias WITH(UPDLOCK,HOLDLOCK) WHERE Activo=1;";await using(var cmd=new SqlCommand(ant,cn,tx))await using(var rd=await cmd.ExecuteReaderAsync()){while(await rd.ReadAsync())anteriores.Add(Convert.ToInt32(rd[0]));}
                const string off=@"UPDATE dbo.RRHH_PolivalenciaCompetencias SET Activo=0,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Por WHERE Activo=1;";await using(var cmd=new SqlCommand(off,cn,tx)){cmd.Parameters.Add("@Por",SqlDbType.NVarChar,120).Value=$"WEB_POLIVALENCIA_U{usuario}";await cmd.ExecuteNonQueryAsync();}
                const string ins=@"INSERT dbo.RRHH_PolivalenciaCompetencias(PersonalID,ParteID,ClaveMatriz,EncabezadoMatriz,NumeroControl,PuestoMatriz,Nivel,FuenteDocumento,VersionDocumento,Mes,Anio,FechaVigencia,Activo,FechaRegistro,RegistradoPor) VALUES(@Personal,@Parte,@Clave,@Encabezado,@Control,@Puesto,@Nivel,@Fuente,@Version,@Mes,@Anio,@Vigencia,1,SYSDATETIME(),@Por);";
                foreach(var c in comps){await using var cmd=new SqlCommand(ins,cn,tx);cmd.Parameters.Add("@Personal",SqlDbType.Int).Value=c.PersonaID;cmd.Parameters.Add("@Parte",SqlDbType.Int).Value=c.ParteID;cmd.Parameters.Add("@Clave",SqlDbType.NVarChar,10).Value=c.Clave;cmd.Parameters.Add("@Encabezado",SqlDbType.NVarChar,500).Value=c.Encabezado[..Math.Min(500,c.Encabezado.Length)];cmd.Parameters.Add("@Control",SqlDbType.NVarChar,30).Value=c.Control;cmd.Parameters.Add("@Puesto",SqlDbType.NVarChar,100).Value=c.Puesto[..Math.Min(100,c.Puesto.Length)];cmd.Parameters.Add("@Nivel",SqlDbType.TinyInt).Value=c.Nivel;cmd.Parameters.Add("@Fuente",SqlDbType.NVarChar,50).Value=fuente;cmd.Parameters.Add("@Version",SqlDbType.NVarChar,20).Value=version;cmd.Parameters.Add("@Mes",SqlDbType.NVarChar,20).Value=mes[..Math.Min(20,mes.Length)];cmd.Parameters.Add("@Anio",SqlDbType.SmallInt).Value=anio;cmd.Parameters.Add("@Vigencia",SqlDbType.Date).Value=new DateTime(anio,MesNumero(mes),1);cmd.Parameters.Add("@Por",SqlDbType.NVarChar,120).Value=$"WEB_POLIVALENCIA_U{usuario}";await cmd.ExecuteNonQueryAsync();}
                var actuales=personas.Select(x=>x.PersonaID).ToHashSet();var bajas=anteriores.Except(actuales).ToList();var desactivadas=0;
                if(bajas.Count>0){foreach(var id in bajas){const string baja=@"UPDATE dbo.Persona SET EsColaboradorActivo=0,FechaBaja=COALESCE(FechaBaja,CONVERT(date,GETDATE())) WHERE PersonaID=@ID AND ISNULL(EsColaboradorActivo,1)=1 AND (UPPER(ISNULL(Puesto,N'')) COLLATE Modern_Spanish_CI_AI LIKE N'%OPERADOR%' OR UPPER(ISNULL(Puesto,N'')) COLLATE Modern_Spanish_CI_AI LIKE N'%AUXILIAR%PRODU%');";await using var cmd=new SqlCommand(baja,cn,tx);cmd.Parameters.Add("@ID",SqlDbType.Int).Value=id;desactivadas+=await cmd.ExecuteNonQueryAsync();}}
                foreach(var id in actuales){const string on=@"UPDATE dbo.Persona SET EsColaboradorActivo=1,FechaBaja=NULL WHERE PersonaID=@ID;";await using var cmd=new SqlCommand(on,cn,tx);cmd.Parameters.Add("@ID",SqlDbType.Int).Value=id;await cmd.ExecuteNonQueryAsync();}
                const string audit=@"INSERT dbo.RRHH_PolivalenciaImportaciones(NombreArchivo,HashSHA256,FuenteDocumento,VersionDocumento,Mes,Anio,TotalPersonas,TotalPartes,TotalCompetencias,PersonasDesactivadas,UsuarioID,Resultado,Observaciones) VALUES(@Archivo,@Hash,@Fuente,@Version,@Mes,@Anio,@Personas,@Partes,@Competencias,@Bajas,@Usuario,N'EXITO',@Obs);";await using(var cmd=new SqlCommand(audit,cn,tx)){cmd.Parameters.Add("@Archivo",SqlDbType.NVarChar,260).Value=Path.GetFileName(archivo.FileName);cmd.Parameters.Add("@Hash",SqlDbType.VarChar,64).Value=hash;cmd.Parameters.Add("@Fuente",SqlDbType.NVarChar,100).Value=fuente;cmd.Parameters.Add("@Version",SqlDbType.NVarChar,50).Value=version;cmd.Parameters.Add("@Mes",SqlDbType.NVarChar,30).Value=mes;cmd.Parameters.Add("@Anio",SqlDbType.Int).Value=anio;cmd.Parameters.Add("@Personas",SqlDbType.Int).Value=personas.Count;cmd.Parameters.Add("@Partes",SqlDbType.Int).Value=comps.Select(x=>x.ParteID).Distinct().Count();cmd.Parameters.Add("@Competencias",SqlDbType.Int).Value=comps.Count;cmd.Parameters.Add("@Bajas",SqlDbType.Int).Value=desactivadas;cmd.Parameters.Add("@Usuario",SqlDbType.Int).Value=(object?)usuario??DBNull.Value;cmd.Parameters.Add("@Obs",SqlDbType.NVarChar,1000).Value=$"Hoja {ws.Name}. Importación transaccional desde Polivalencia.";await cmd.ExecuteNonQueryAsync();}
                await tx.CommitAsync();TempData["Exito"]=$"Matriz actualizada: {personas.Count} personas, {comps.Select(x=>x.ParteID).Distinct().Count()} partes ERP y {comps.Count} competencias. Personal operativo desactivado: {desactivadas}.";
            }catch{try{await tx.RollbackAsync();}catch{}throw;}
        }
        catch(Exception ex){TempData["Error"]="No se importó la matriz. "+ex.Message;}
        return RedirectToAction(nameof(Polivalencia));
    }

    private static async Task<bool> MatrizActivaCoincideV7Async(
        List<PoliCompetenciaV7> comps,
        SqlConnection cn)
    {
        const string sql = @"
SELECT PersonalID,ParteID,MAX(CONVERT(int,Nivel)) AS Nivel
FROM dbo.RRHH_PolivalenciaCompetencias
WHERE Activo=1
GROUP BY PersonalID,ParteID;";

        var db = new Dictionary<(int PersonalID,int ParteID),int>();

        await using var cmd = new SqlCommand(sql,cn);
        await using var rd = await cmd.ExecuteReaderAsync();

        while(await rd.ReadAsync())
        {
            db[(Convert.ToInt32(rd["PersonalID"]),Convert.ToInt32(rd["ParteID"]))] =
                Convert.ToInt32(rd["Nivel"]);
        }

        var archivo = comps
            .GroupBy(x => (x.PersonaID,x.ParteID))
            .ToDictionary(
                g => g.Key,
                g => g.Max(x => x.Nivel));

        if (db.Count != archivo.Count)
            return false;

        foreach (var item in archivo)
        {
            if (!db.TryGetValue(item.Key,out var nivel) || nivel != item.Value)
                return false;
        }

        return true;
    }
    private static string? BuscarValorJuntoEtiqueta(IXLWorksheet ws,string etiqueta){for(int r=1;r<=Math.Min(15,ws.LastRowUsed()?.RowNumber()??15);r++)for(int c=1;c<=10;c++){var t=(ws.Cell(r,c).GetString()??"").Trim();if(t.Equals(etiqueta,StringComparison.OrdinalIgnoreCase)||t.TrimEnd(':').Equals(etiqueta,StringComparison.OrdinalIgnoreCase)){for(int k=1;k<=3;k++){var v=(ws.Cell(r,c+k).GetString()??"").Trim();if(!string.IsNullOrWhiteSpace(v))return v;}}}return null;}
    private static int EncontrarFilaCabecera(IXLWorksheet ws){var last=Math.Min(30,ws.LastRowUsed()?.RowNumber()??30);for(int r=1;r<=last;r++){var vals=ws.Row(r).Cells(1,8).Select(x=>(x.GetString()??"").Trim().ToUpperInvariant()).ToList();if(vals.Any(x=>x=="NOMBRE")&&vals.Any(x=>x.Contains("CONTROL"))&&vals.Any(x=>x=="PUESTO"))return r;}return 0;}
    private static string Control(IXLCell c){var s=(c.GetString()??"").Trim();if(string.IsNullOrWhiteSpace(s)&&c.TryGetValue<double>(out var d))s=Math.Round(d).ToString(System.Globalization.CultureInfo.InvariantCulture);return Regex.Replace(s,@"\.0+$","");}
    private static async Task<Dictionary<string,(int Id,string Nombre,string Puesto)>> CargarPersonasPoliV7Async(SqlConnection cn){const string sql=@"SELECT PersonaID,ISNULL(NumeroControl,N'') NumeroControl,LTRIM(RTRIM(CONCAT(ISNULL(Nombre,N''),N' ',ISNULL(ApellidoPaterno,N''),N' ',ISNULL(ApellidoMaterno,N'')))) Nombre,ISNULL(Puesto,N'') Puesto FROM dbo.Persona WHERE NULLIF(LTRIM(RTRIM(NumeroControl)),N'') IS NOT NULL ORDER BY NumeroControl,PersonaID;";var d=new Dictionary<string,(int,string,string)>(StringComparer.OrdinalIgnoreCase);await using var cmd=new SqlCommand(sql,cn);await using var rd=await cmd.ExecuteReaderAsync();while(await rd.ReadAsync()){var k=rd["NumeroControl"]?.ToString()?.Trim();if(string.IsNullOrWhiteSpace(k))continue;if(d.ContainsKey(k))throw new InvalidOperationException($"El número de control {k} está duplicado en Persona. Corrige el catálogo antes de importar la matriz.");d[k]=(Convert.ToInt32(rd["PersonaID"]),rd["Nombre"]?.ToString()?.Trim()??"",rd["Puesto"]?.ToString()?.Trim()??"");}return d;}
    private static async Task<List<PoliParteV7>> CargarPartesPoliV7Async(SqlConnection cn){const string sql=@"SELECT ParteID,ISNULL(NumeroParte,N'') NumeroParte,ISNULL(ReferenciaSAP,N'') ReferenciaSAP,ISNULL(Designacion,N'') Designacion,ISNULL(Descripcion,N'') Descripcion FROM dbo.ERP_Partes WHERE Activo=1;";var l=new List<PoliParteV7>();await using var cmd=new SqlCommand(sql,cn);await using var rd=await cmd.ExecuteReaderAsync();while(await rd.ReadAsync())l.Add(new PoliParteV7{ParteID=Convert.ToInt32(rd["ParteID"]),Numero=rd["NumeroParte"]?.ToString()??"",Sap=rd["ReferenciaSAP"]?.ToString()??"",Designacion=rd["Designacion"]?.ToString()??"",Descripcion=rd["Descripcion"]?.ToString()??""});return l;}
    private static List<PoliPersonaV7> ExtraerPersonasV7(IXLWorksheet ws,int headerRow,Dictionary<string,(int Id,string Nombre,string Puesto)> db){var l=new List<PoliPersonaV7>();var last=Math.Min(ws.LastRowUsed()?.RowNumber()??headerRow,500);for(int r=headerRow+1;r<=last;r++){var nombre=(ws.Cell(r,2).GetString()??"").Trim();var control=Control(ws.Cell(r,3));var puesto=(ws.Cell(r,4).GetString()??"").Trim();var up=puesto.ToUpperInvariant();if(string.IsNullOrWhiteSpace(nombre)||string.IsNullOrWhiteSpace(control)||!(up.Contains("OPERADOR")||up.Contains("AUXILIAR DE PRODU")))continue;if(!db.TryGetValue(control,out var p))throw new InvalidOperationException($"El control {control} ({nombre}) no existe en Persona.");l.Add(new PoliPersonaV7{PersonaID=p.Id,Control=control,Nombre=nombre,Puesto=puesto,Row=r});}return l;}
    private static List<PoliCompetenciaV7> ExtraerCompetenciasV7(IXLWorksheet ws,int pieceRow,List<PoliPersonaV7> personas,List<PoliParteV7> partes){var lastCol=Math.Min(ws.LastColumnUsed()?.ColumnNumber()??4,500);var headers=new List<(int Col,string Texto)>();for(int c=5;c<=lastCol;c++){var h=(ws.Cell(pieceRow,c).GetString()??"").Trim();if(!string.IsNullOrWhiteSpace(h))headers.Add((c,h));}var result=new List<PoliCompetenciaV7>();foreach(var h in headers){var niveles=new Dictionary<PoliPersonaV7,int>();foreach(var p in personas){var next=personas.Where(x=>x.Row>p.Row).Select(x=>x.Row).DefaultIfEmpty(p.Row+3).Min();var max=0;for(int r=p.Row;r<Math.Min(next,p.Row+3);r++)for(int c=h.Col;c<=Math.Min(h.Col+1,lastCol);c++){var s=(ws.Cell(r,c).GetString()??"").Trim();if(int.TryParse(s,out var n)&&n>=1&&n<=4)max=Math.Max(max,n);}if(max>0)niveles[p]=max;}if(niveles.Count==0)continue;var m=MapearPartesHeaderV7(h.Texto,partes);if(m.Count==0){if(ParteSinActivoConocidaV76(h.Texto))continue;throw new InvalidOperationException($"No se pudo relacionar de forma segura la columna {XLHelper.GetColumnLetterFromNumber(h.Col)} '{h.Texto.Replace("\n"," ")}' con ERP_Partes."+SugerenciasParteHeaderV75(h.Texto,partes));}foreach(var kv in niveles)foreach(var parte in m)result.Add(new PoliCompetenciaV7{PersonaID=kv.Key.PersonaID,Control=kv.Key.Control,Puesto=kv.Key.Puesto,ParteID=parte.ParteID,Clave=XLHelper.GetColumnLetterFromNumber(h.Col),Encabezado=h.Texto,Nivel=kv.Value});}return result.GroupBy(x=>new{x.PersonaID,x.ParteID}).Select(g=>g.OrderByDescending(x=>x.Nivel).First()).ToList();}
    private static string N(string? s)=>Regex.Replace((s??"").Normalize(NormalizationForm.FormD).ToUpperInvariant(),@"[^A-Z0-9]","");
    private static List<PoliParteV7> MapearPartesHeaderV7(string header,List<PoliParteV7> partes)
    {
        var h=N(header);

        // V7.5 - aliases oficiales conocidos de la matriz GQ-F-RH02-05.
        // Se resuelven por NumeroParte/ReferenciaSAP, NO por ParteID, para que
        // Test y Produccion puedan tener identities distintos.
        var aliasNumero=new Dictionary<string,string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [N("S/C Bushing +S/C rewPF-KI")]=new[]{"66101496"},
            [N("Lock Cylinder Tool 2")]=new[]{"34.947.651"},
            [N("Axle Pin 14.788.216")]=new[]{"14.788.215"},
            [N("Ram Box Bezel 51.954.606")]=new[]{"51.964.606","51964606-003-A1"},
            [N("Key Insert Top 57.900.412")]=new[]{"07.900.012"},
            [N("Halter Clip/Holder F295123C.b")]=new[]{"F29512C-b"},
            [N("W3 Shipping Plug 34.316.308")]=new[]{"34.315.308"},
            [N("Cover Door Lock 36.338.209")]=new[]{"35.338.209"},
            [N("STUD F30508A-c")]=new[]{"F30608A-c"}
        };

        if(aliasNumero.TryGetValue(h,out var numerosAlias))
        {
            var alias=partes
                .Where(p=>numerosAlias.Any(n=>
                    N(p.Numero)==N(n) ||
                    N(p.Sap)==N(n)))
                .GroupBy(x=>x.ParteID)
                .Select(x=>x.First())
                .ToList();

            if(alias.Count>0) return alias;
        }

        // 1) Numero de parte / SAP contenido en el encabezado: prioridad maxima.
        var fuertes=partes
            .Where(p=>(N(p.Numero).Length>=4&&h.Contains(N(p.Numero))) ||
                      (N(p.Sap).Length>=4&&h.Contains(N(p.Sap))))
            .GroupBy(x=>x.ParteID)
            .Select(x=>x.First())
            .ToList();
        if(fuertes.Count>0) return fuertes;

        // 2) Designacion/descripcion equivalente por inclusion.
        var suaves=partes
            .Where(p=>(N(p.Designacion).Length>=8 &&
                       (h.Contains(N(p.Designacion))||N(p.Designacion).Contains(h))) ||
                      (N(p.Descripcion).Length>=10 &&
                       (h.Contains(N(p.Descripcion))||N(p.Descripcion).Contains(h))))
            .GroupBy(x=>x.ParteID)
            .Select(x=>x.First())
            .ToList();
        if(suaves.Count==1) return suaves;

        // 3) Fallback por nombre cercano. Solo se acepta cuando la coincidencia
        // es alta Y existe separacion suficiente contra el segundo candidato.
        // Asi evitamos relacionar automaticamente encabezados genericos como
        // "Bushing" con una pieza equivocada.
        var ranking=partes
            .Select(p=>new
            {
                Parte=p,
                Score=Math.Max(
                    SimilitudNombrePoliV75(header,p.Designacion),
                    SimilitudNombrePoliV75(header,p.Descripcion))
            })
            .Where(x=>x.Score>=0.45)
            .OrderByDescending(x=>x.Score)
            .ThenBy(x=>x.Parte.Numero)
            .ToList();

        if(ranking.Count==0) return new List<PoliParteV7>();

        var primero=ranking[0];
        var segundo=ranking.Count>1?ranking[1].Score:0d;
        if(primero.Score>=0.78 && primero.Score-segundo>=0.08)
            return new List<PoliParteV7>{primero.Parte};

        return new List<PoliParteV7>();
    }

    private static string NombreFuzzyPoliV75(string? value)
    {
        var descomp=(value??string.Empty).Normalize(NormalizationForm.FormD).ToUpperInvariant();
        var limpio=new string(descomp.Where(c=>System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)!=System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
        return Regex.Replace(limpio,@"[^A-Z0-9]+"," ").Trim();
    }

    private static double SimilitudNombrePoliV75(string? a,string? b)
    {
        var x=NombreFuzzyPoliV75(a);
        var y=NombreFuzzyPoliV75(b);
        if(string.IsNullOrWhiteSpace(x)||string.IsNullOrWhiteSpace(y)) return 0d;
        if(x==y) return 1d;

        var xc=Regex.Replace(x,@"\s+",string.Empty);
        var yc=Regex.Replace(y,@"\s+",string.Empty);
        if(Math.Min(xc.Length,yc.Length)>=8 && (xc.Contains(yc)||yc.Contains(xc)))
            return 0.94d;

        var distancia=DistanciaLevenshteinPoliV75(xc,yc);
        var ratio=1d-(double)distancia/Math.Max(xc.Length,yc.Length);

        var stop=new HashSet<string>(new[]{"ASSY","ASSEMBLY","PART","TOOL","THE","AND","OR"},StringComparer.OrdinalIgnoreCase);
        var tx=x.Split(' ',StringSplitOptions.RemoveEmptyEntries).Where(t=>t.Length>1&&!stop.Contains(t)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ty=y.Split(' ',StringSplitOptions.RemoveEmptyEntries).Where(t=>t.Length>1&&!stop.Contains(t)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var union=tx.Union(ty,StringComparer.OrdinalIgnoreCase).Count();
        var inter=tx.Intersect(ty,StringComparer.OrdinalIgnoreCase).Count();
        var token=union==0?0d:(double)inter/union;

        return Math.Max(ratio,(ratio*0.45d)+(token*0.55d));
    }

    private static int DistanciaLevenshteinPoliV75(string a,string b)
    {
        if(a.Length==0) return b.Length;
        if(b.Length==0) return a.Length;

        var anterior=new int[b.Length+1];
        var actual=new int[b.Length+1];
        for(int j=0;j<=b.Length;j++) anterior[j]=j;

        for(int i=1;i<=a.Length;i++)
        {
            actual[0]=i;
            for(int j=1;j<=b.Length;j++)
            {
                var costo=a[i-1]==b[j-1]?0:1;
                actual[j]=Math.Min(
                    Math.Min(actual[j-1]+1,anterior[j]+1),
                    anterior[j-1]+costo);
            }
            (anterior,actual)=(actual,anterior);
        }
        return anterior[b.Length];
    }

    private static bool ParteSinActivoConocidaV76(string header)
    {
        var h=N(header);
        var conocidas=new[]
        {
            N("Housing F15 EKT CA box 14.262.501"),
            N("Housing F15 EKT CA box 14.262.701"),
            N("Cover 36.751.613"),
            N("Lock Cylinder Lordstown"),
            N("Dummy Housing R1S"),
            N("Dummy Housing R1T"),
            N("Led Housing 13.966.262")
        };
        return conocidas.Any(x=>h==x || h.Contains(x) || x.Contains(h));
    }
    private static string SugerenciasParteHeaderV75(string header,List<PoliParteV7> partes)
    {
        var opciones=partes
            .Select(p=>new
            {
                Parte=p,
                Score=Math.Max(
                    SimilitudNombrePoliV75(header,p.Designacion),
                    SimilitudNombrePoliV75(header,p.Descripcion))
            })
            .Where(x=>x.Score>=0.40)
            .OrderByDescending(x=>x.Score)
            .Take(3)
            .Select(x=>$"{x.Parte.Numero} - {(!string.IsNullOrWhiteSpace(x.Parte.Designacion)?x.Parte.Designacion:x.Parte.Descripcion)} ({x.Score:P0})")
            .ToList();
        return opciones.Count==0?string.Empty:" Candidatos cercanos: "+string.Join(" | ",opciones);
    }
    private static int MesNumero(string mes){var m=N(mes);return m.StartsWith("ENERO")?1:m.StartsWith("FEBRERO")?2:m.StartsWith("MARZO")?3:m.StartsWith("ABRIL")?4:m.StartsWith("MAYO")?5:m.StartsWith("JUNIO")?6:m.StartsWith("JULIO")?7:m.StartsWith("AGOSTO")?8:m.StartsWith("SEPTIEMBRE")?9:m.StartsWith("OCTUBRE")?10:m.StartsWith("NOVIEMBRE")?11:m.StartsWith("DICIEMBRE")?12:throw new InvalidOperationException("Mes no reconocido: "+mes);}
}
