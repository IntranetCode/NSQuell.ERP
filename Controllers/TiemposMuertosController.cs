using ERP.NSQuell.Models;
using ERP.NSQuell.Servicios;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers;

[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class TiemposMuertosController : Controller
{
    private readonly IConfiguration _config;
    private readonly IServicioAcceso _acceso;
    public TiemposMuertosController(IConfiguration config,IServicioAcceso acceso) { _config=config;_acceso=acceso; }
    [HttpGet]
    public async Task<IActionResult> Index(DateTime? desde=null,DateTime? hasta=null,string? departamento=null,string? categoria=null,string? of=null,bool soloActivos=false,bool descargar=false,CancellationToken cancellationToken=default)
    {
        var usuario=HttpContext.Session.GetInt32("UsuarioID");
        if(!usuario.HasValue)return RedirectToAction("Login","Login");
        if(!await _acceso.TienePermisoAsync(usuario.Value,"Seguimiento de tiempos muertos","VER"))return StatusCode(403);
        var inicio=(desde??DateTime.Today.AddDays(-6)).Date;
        var ultimo=(hasta??DateTime.Today).Date;
        if (ultimo>=DateTime.MaxValue.Date) return BadRequest("La fecha final no es válida.");
        var fin=ultimo.AddDays(1);
        categoria=string.IsNullOrWhiteSpace(categoria)?"DEMORA":categoria.Trim().ToUpperInvariant();
        if (categoria is not ("DEMORA" or "TODOS" or "ESPERA" or "PARO" or "TRABAJO")) return BadRequest("Tipo de intervalo no válido.");
        if(fin<=inicio || (fin-inicio).TotalDays>93)
            return BadRequest("Selecciona un intervalo de 1 a 93 días.");
        var vm=new TiemposMuertosVm { Desde=inicio,Hasta=fin.AddDays(-1),Departamento=departamento,Categoria=categoria,OF=of,SoloActivos=soloActivos };
        await using var cn=new SqlConnection(_config.GetConnectionString("DefaultConnection"));await cn.OpenAsync(cancellationToken);
        await using(var clock=new SqlCommand("SELECT SYSDATETIME();",cn))vm.Actualizado=Convert.ToDateTime(await clock.ExecuteScalarAsync(cancellationToken));
        await using(var cmd=new SqlCommand("""
SELECT DISTINCT Departamento FROM dbo.vw_NSQ_TiemposDepartamentosV1 ORDER BY Departamento;
""",cn))
        {
            await using var rd=await cmd.ExecuteReaderAsync(cancellationToken);
            while(await rd.ReadAsync(cancellationToken))vm.Departamentos.Add(Convert.ToString(rd[0])??"");
        }
        await using(var cmd=new SqlCommand("""
SELECT Origen,RegistroID,Fase,ProgramaProduccionID,MaquinaID,MaquinaCodigo,NumeroOF,NumeroParte,Designacion,Departamento,Categoria,Tarea,Inicio,Fin,Motivo
FROM dbo.vw_NSQ_TiemposDepartamentosV1
WHERE Inicio<@Hasta AND COALESCE(Fin,@Ahora)>@Desde AND Inicio<=@Ahora
 AND (@Departamento IS NULL OR Departamento=@Departamento) AND (@Categoria=N'TODOS' OR (@Categoria=N'DEMORA' AND Categoria IN(N'ESPERA',N'PARO')) OR Categoria=@Categoria)
 AND (@OF IS NULL OR NumeroOF LIKE N'%'+@OF+N'%' OR NumeroParte LIKE N'%'+@OF+N'%' OR Designacion LIKE N'%'+@OF+N'%')
 AND (@SoloActivos=0 OR Fin IS NULL)
ORDER BY CASE WHEN Fin IS NULL THEN 0 ELSE 1 END,Inicio;
""",cn))
        {
            cmd.Parameters.Add("@Desde",SqlDbType.DateTime2).Value=inicio;cmd.Parameters.Add("@Hasta",SqlDbType.DateTime2).Value=fin;
            cmd.Parameters.Add("@Ahora",SqlDbType.DateTime2).Value=vm.Actualizado;
            cmd.Parameters.Add("@Departamento",SqlDbType.NVarChar,150).Value=string.IsNullOrWhiteSpace(departamento)?DBNull.Value:departamento.Trim();
            cmd.Parameters.Add("@Categoria",SqlDbType.NVarChar,20).Value=string.IsNullOrWhiteSpace(categoria)?DBNull.Value:categoria.Trim();
            cmd.Parameters.Add("@OF",SqlDbType.NVarChar,100).Value=string.IsNullOrWhiteSpace(of)?DBNull.Value:of.Trim();
            cmd.Parameters.Add("@SoloActivos",SqlDbType.Bit).Value=soloActivos;
            await using var rd=await cmd.ExecuteReaderAsync(cancellationToken);
            while(await rd.ReadAsync(cancellationToken))
            {
                var fila=new TiempoDepartamentoFila {
                    Origen=Convert.ToString(rd["Origen"])??"",RegistroID=Convert.ToInt64(rd["RegistroID"]),Fase=Convert.ToString(rd["Fase"])??"",
                    ProgramaID=Convert.ToInt32(rd["ProgramaProduccionID"]),MaquinaID=rd["MaquinaID"]==DBNull.Value?null:Convert.ToInt32(rd["MaquinaID"]),
                    Maquina=Convert.ToString(rd["MaquinaCodigo"])??"Sin máquina",NumeroOF=Convert.ToString(rd["NumeroOF"])??"",
                    NumeroParte=Convert.ToString(rd["NumeroParte"])??"",Designacion=Convert.ToString(rd["Designacion"])??"",
                    Departamento=Convert.ToString(rd["Departamento"])??"",Categoria=Convert.ToString(rd["Categoria"])??"",
                    Tarea=Convert.ToString(rd["Tarea"])??"",Motivo=Convert.ToString(rd["Motivo"])??"",
                    Inicio=Convert.ToDateTime(rd["Inicio"]),Fin=rd["Fin"]==DBNull.Value?null:Convert.ToDateTime(rd["Fin"])
                };
                // Cortar cada intervalo al periodo; un histórico fuera de rango no aumenta sus horas.
                fila.InicioPeriodo=fila.Inicio>inicio?fila.Inicio:inicio;
                var corte=fila.Fin.HasValue && fila.Fin.Value<vm.Actualizado?fila.Fin.Value:vm.Actualizado;
                fila.FinPeriodo=corte<fin?corte:fin;
                if(fila.FinPeriodo>fila.InicioPeriodo)vm.Filas.Add(fila);
            }
        }
        await using(var calidadDatos=new SqlCommand("""
SELECT COUNT(*) FROM dbo.Produccion_PreparacionAnticipada p
INNER JOIN dbo.Planeacion_ProgramaProduccion pp ON pp.ProgramaProduccionID=p.ProgramaProduccionID AND pp.Activo=1 AND pp.EstatusID<>99
LEFT JOIN dbo.SolicitudesProduccion s ON s.SolicitudProduccionID=pp.SolicitudProduccionID
WHERE p.Activo=1 AND p.Estado=N'CONFIRMADA' AND p.FechaFinReal IS NULL AND p.FechaConfirmacion IS NULL
 AND p.FechaAviso>=@Desde AND p.FechaAviso<@Hasta AND p.FechaAviso<=@Ahora
 AND (@Departamento IS NULL OR @Departamento=CASE WHEN p.TipoTarea LIKE N'%MOLDE%' THEN N'SMED' ELSE N'Producción' END)
 AND (@OF IS NULL OR pp.NumeroParte LIKE N'%'+@OF+N'%' OR pp.DesignacionDescripcionSAP LIKE N'%'+@OF+N'%' OR s.NumeroOFRecibida LIKE N'%'+@OF+N'%' OR s.FolioSolicitud LIKE N'%'+@OF+N'%');
""",cn))
        {
            calidadDatos.Parameters.Add("@Desde",SqlDbType.DateTime2).Value=inicio;
            calidadDatos.Parameters.Add("@Departamento",SqlDbType.NVarChar,150).Value=string.IsNullOrWhiteSpace(departamento)?DBNull.Value:departamento.Trim();
            calidadDatos.Parameters.Add("@Hasta",SqlDbType.DateTime2).Value=fin;
            calidadDatos.Parameters.Add("@Ahora",SqlDbType.DateTime2).Value=vm.Actualizado;
            calidadDatos.Parameters.Add("@OF",SqlDbType.NVarChar,100).Value=string.IsNullOrWhiteSpace(of)?DBNull.Value:of.Trim();
            vm.DatosSinCierre=Convert.ToInt32(await calidadDatos.ExecuteScalarAsync(cancellationToken));
        }
        vm.PrepararAnalisis();
        if (descargar)
        {
            var csv=new System.Text.StringBuilder("Máquina;OF;Espera neta h;Paro h;Total neto h;Episodios;Abiertos\r\n");
            foreach(var frente in vm.Frentes)
                csv.AppendLine(string.Join(";",Csv(frente.Maquina),Csv(string.Join(" / ",frente.Ordenes)),frente.HorasEspera.ToString("0.000",System.Globalization.CultureInfo.InvariantCulture),frente.HorasParo.ToString("0.000",System.Globalization.CultureInfo.InvariantCulture),frente.HorasNetas.ToString("0.000",System.Globalization.CultureInfo.InvariantCulture),frente.Episodios.Count,frente.Episodios.Count(x=>x.Abierto)));
            var bytes=new System.Text.UTF8Encoding(true).GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
            return File(bytes,"text/csv; charset=utf-8",$"Tiempos_{inicio:yyyyMMdd}_{ultimo:yyyyMMdd}.csv");
        }
        return View(vm);
    }
    private static string Csv(string texto)
    {
        if(texto.Length>0 && "=+-@".Contains(texto.TrimStart().FirstOrDefault()))texto="'"+texto;
        return "\""+texto.Replace("\"","\"\"")+"\"";
    }
}
