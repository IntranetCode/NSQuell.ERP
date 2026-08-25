using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Globalization;

namespace ERP.NSQuell.Controllers;

// NSQ_PRODUCCION_PERSONAL_INTEGRACION_V2_POR_PIEZA
public sealed partial class ProduccionController
{
    private sealed class PersonalProgramadoProduccionInterno
    {
        public int AsignacionPersonalID { get; set; }
        public int? OperadorID { get; set; }
        public string? OperadorNombre { get; set; }
        public int? AuxiliarID { get; set; }
        public string? AuxiliarNombre { get; set; }
        public int? TecnicoID { get; set; }
        public string? TecnicoNombre { get; set; }
        public int? SmedID { get; set; }
        public string? SmedNombre { get; set; }
        public int? TurnoID { get; set; }
        public string? TurnoNombre { get; set; }
        public string Fuente { get; set; } = "LEGADO";
        public DateTime Inicio { get; set; }
        public DateTime Fin { get; set; }
    }

    private sealed class TurnoResolverV2
    {
        public int TurnoID { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string TipoTurno { get; set; } = string.Empty;
        public TimeSpan Inicio { get; set; }
        public TimeSpan Fin { get; set; }
        public bool Cruza { get; set; }
        public int Orden { get; set; }
    }

    private sealed class CoberturaResolverV2
    {
        public int CoberturaID { get; set; }
        public DateTime SemanaInicio { get; set; }
        public int TurnoID { get; set; }
        public int? TecnicoID { get; set; }
        public int? SmedID { get; set; }
        public int? AuxiliarID { get; set; }
    }

    private static DateTime InicioSemanaResolverV2(DateTime fecha)
    {
        var y = ISOWeek.GetYear(fecha);
        var w = ISOWeek.GetWeekOfYear(fecha);
        return ISOWeek.ToDateTime(y,w,DayOfWeek.Monday).Date;
    }

    private static bool TurnoCubreResolverV2(TurnoResolverV2 turno, DateTime momento)
    {
        var h = momento.TimeOfDay;
        return turno.Cruza
            ? h >= turno.Inicio || h < turno.Fin
            : h >= turno.Inicio && h < turno.Fin;
    }

    private static DateTime FechaTrabajoResolverV2(TurnoResolverV2 turno, DateTime momento) =>
        turno.Cruza && momento.TimeOfDay < turno.Fin
            ? momento.Date.AddDays(-1)
            : momento.Date;

    private static (DateTime Inicio,DateTime Fin) VentanaTurnoResolverV2(
        TurnoResolverV2 turno,
        DateTime fechaTrabajo)
    {
        var inicio = fechaTrabajo.Date.Add(turno.Inicio);
        var fin = fechaTrabajo.Date.Add(turno.Fin);
        if (turno.Cruza || fin <= inicio) fin=fin.AddDays(1);
        return (inicio,fin);
    }

    private static async Task<bool> V2DisponibleAsync(
        SqlConnection cn,
        SqlTransaction? tx)
    {
        const string sql = @"
SELECT CONVERT(bit,CASE WHEN
       OBJECT_ID(N'dbo.Produccion_PersonalTurnoCobertura',N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.Planeacion_ProgramaOperadores',N'U') IS NOT NULL
THEN 1 ELSE 0 END);";

        await using var cmd = tx == null
            ? new SqlCommand(sql,cn)
            : new SqlCommand(sql,cn,tx);

        return Convert.ToBoolean(await cmd.ExecuteScalarAsync() ?? false);
    }

    private static async Task<List<TurnoResolverV2>> CargarTurnosResolverV2Async(
        SqlConnection cn,
        SqlTransaction? tx)
    {
        const string sql = @"
SELECT TurnoID,Nombre,TipoTurno,HoraInicio,HoraFin,CruzaDiaSiguiente,Orden
FROM dbo.RRHH_Turnos
WHERE Activo=1
  AND EsFlexible=0
  AND HoraInicio IS NOT NULL
  AND HoraFin IS NOT NULL
ORDER BY Orden,TurnoID;";

        var lista = new List<TurnoResolverV2>();
        await using var cmd = tx == null
            ? new SqlCommand(sql,cn)
            : new SqlCommand(sql,cn,tx);
        await using var rd = await cmd.ExecuteReaderAsync();
        while(await rd.ReadAsync())
        {
            lista.Add(new TurnoResolverV2
            {
                TurnoID=Convert.ToInt32(rd["TurnoID"]),
                Nombre=rd["Nombre"]?.ToString()?.Trim() ?? string.Empty,
                TipoTurno=rd["TipoTurno"]?.ToString()?.Trim() ?? string.Empty,
                Inicio=(TimeSpan)rd["HoraInicio"],
                Fin=(TimeSpan)rd["HoraFin"],
                Cruza=Convert.ToBoolean(rd["CruzaDiaSiguiente"]),
                Orden=Convert.ToInt32(rd["Orden"])
            });
        }
        return lista;
    }

    private static async Task<List<CoberturaResolverV2>> CargarCoberturasResolverV2Async(
        DateTime semanaA,
        DateTime semanaB,
        SqlConnection cn,
        SqlTransaction? tx)
    {
        const string sql = @"
SELECT CoberturaTurnoID,SemanaInicio,TurnoID,
       TecnicoProduccionID,SmedID,AuxiliarID
FROM dbo.Produccion_PersonalTurnoCobertura
WHERE Activo=1
  AND SemanaInicio IN(@SemanaA,@SemanaB);";

        var lista = new List<CoberturaResolverV2>();
        await using var cmd = tx == null
            ? new SqlCommand(sql,cn)
            : new SqlCommand(sql,cn,tx);
        cmd.Parameters.Add("@SemanaA",SqlDbType.Date).Value=semanaA;
        cmd.Parameters.Add("@SemanaB",SqlDbType.Date).Value=semanaB;
        await using var rd = await cmd.ExecuteReaderAsync();
        while(await rd.ReadAsync())
        {
            lista.Add(new CoberturaResolverV2
            {
                CoberturaID=Convert.ToInt32(rd["CoberturaTurnoID"]),
                SemanaInicio=Convert.ToDateTime(rd["SemanaInicio"]),
                TurnoID=Convert.ToInt32(rd["TurnoID"]),
                TecnicoID=rd["TecnicoProduccionID"]==DBNull.Value?null:Convert.ToInt32(rd["TecnicoProduccionID"]),
                SmedID=rd["SmedID"]==DBNull.Value?null:Convert.ToInt32(rd["SmedID"]),
                AuxiliarID=rd["AuxiliarID"]==DBNull.Value?null:Convert.ToInt32(rd["AuxiliarID"])
            });
        }
        return lista;
    }

    private static async Task<(int? Id,string? Nombre)> NombrePersonaResolverV2Async(
        int? personaId,
        SqlConnection cn,
        SqlTransaction? tx)
    {
        if(!personaId.HasValue) return (null,null);
        const string sql=@"
SELECT TOP(1)
 LTRIM(RTRIM(CONCAT(ISNULL(Nombre,N''),N' ',ISNULL(ApellidoPaterno,N''),N' ',ISNULL(ApellidoMaterno,N''))))
FROM dbo.Persona
WHERE PersonaID=@ID;";
        await using var cmd=tx==null?new SqlCommand(sql,cn):new SqlCommand(sql,cn,tx);
        cmd.Parameters.Add("@ID",SqlDbType.Int).Value=personaId.Value;
        var value=await cmd.ExecuteScalarAsync();
        return (personaId,value==null||value==DBNull.Value?null:value.ToString()?.Trim());
    }

    private static async Task<PersonalProgramadoProduccionInterno?>
        ObtenerPersonalProgramadoProduccionAsync(
            int programaProduccionId,
            DateTime momento,
            DateTime? momentoAlterno,
            SqlConnection cn,
            SqlTransaction? tx)
    {
        if(programaProduccionId<=0) return null;

        if(await V2DisponibleAsync(cn,tx))
        {
            var v2=await ObtenerPersonalProgramadoV2Async(
                programaProduccionId,momento,momentoAlterno,cn,tx);
            if(v2!=null) return v2;
        }

        return await ObtenerPersonalProgramadoLegadoV2Async(
            programaProduccionId,momento,momentoAlterno,cn,tx);
    }

    private static async Task<PersonalProgramadoProduccionInterno?>
        ObtenerPersonalProgramadoV2Async(
            int programaId,
            DateTime momento,
            DateTime? alterno,
            SqlConnection cn,
            SqlTransaction? tx)
    {
        const string programaSql=@"
SELECT TOP(1)
 FechaInicioProgramada,
 ISNULL(FechaFinProgramada,DATEADD(MINUTE,CONVERT(INT,CEILING(ISNULL(HorasProgramadas,1)*60)),FechaInicioProgramada)) AS FechaFinProgramada
FROM dbo.Planeacion_ProgramaProduccion
WHERE ProgramaProduccionID=@ProgramaID AND Activo=1;";

        DateTime? inicioPrograma=null,finPrograma=null;
        await using(var cmd=tx==null?new SqlCommand(programaSql,cn):new SqlCommand(programaSql,cn,tx))
        {
            cmd.Parameters.Add("@ProgramaID",SqlDbType.Int).Value=programaId;
            await using var rd=await cmd.ExecuteReaderAsync();
            if(!await rd.ReadAsync()) return null;
            inicioPrograma=rd["FechaInicioProgramada"]==DBNull.Value?null:Convert.ToDateTime(rd["FechaInicioProgramada"]);
            finPrograma=rd["FechaFinProgramada"]==DBNull.Value?null:Convert.ToDateTime(rd["FechaFinProgramada"]);
        }
        if(!inicioPrograma.HasValue) return null;

        var objetivo=momento;
        if(objetivo<inicioPrograma.Value || (finPrograma.HasValue && objetivo>=finPrograma.Value))
            objetivo=alterno ?? inicioPrograma.Value;

        const string operadorSql=@"
SELECT TOP(1)
 COALESCE(ex.OperadorSustitutoID,po.PersonaID) AS OperadorID,
 LTRIM(RTRIM(CONCAT(ISNULL(p.Nombre,N''),N' ',ISNULL(p.ApellidoPaterno,N''),N' ',ISNULL(p.ApellidoMaterno,N'')))) AS OperadorNombre,
 CASE WHEN ex.ExcepcionOperadorID IS NULL THEN N'PROGRAMA_OF' ELSE N'EXCEPCION' END AS Fuente
FROM (SELECT 1 AS X) base
OUTER APPLY
(
 SELECT TOP(1) x.PersonaID
 FROM dbo.Planeacion_ProgramaOperadores x
 WHERE x.ProgramaProduccionID=@ProgramaID AND x.Activo=1
   AND UPPER(ISNULL(x.RolOperador,N''))=N'PRINCIPAL'
 ORDER BY x.ProgramaOperadorID DESC
) po
OUTER APPLY
(
 SELECT TOP(1) e.ExcepcionOperadorID,e.OperadorSustitutoID
 FROM dbo.Produccion_OperadorExcepciones e
 WHERE OBJECT_ID(N'dbo.Produccion_OperadorExcepciones',N'U') IS NOT NULL
   AND e.Activo=1
   AND e.ProgramaProduccionID=@ProgramaID
 ORDER BY e.ExcepcionOperadorID DESC
) ex
LEFT JOIN dbo.Persona p ON p.PersonaID=COALESCE(ex.OperadorSustitutoID,po.PersonaID)
WHERE po.PersonaID IS NOT NULL OR ex.OperadorSustitutoID IS NOT NULL;";

        int? operadorId=null; string? operadorNombre=null; var fuente="V2";
        // OBJECT_ID inside a query does not prevent name resolution. Check table first.
        var tieneExcepciones=false;
        const string exExiste="SELECT CONVERT(bit,CASE WHEN OBJECT_ID(N'dbo.Produccion_OperadorExcepciones',N'U') IS NULL THEN 0 ELSE 1 END);";
        await using(var exCmd=tx==null?new SqlCommand(exExiste,cn):new SqlCommand(exExiste,cn,tx))
            tieneExcepciones=Convert.ToBoolean(await exCmd.ExecuteScalarAsync() ?? false);

        var opSql=tieneExcepciones?operadorSql:@"
SELECT TOP(1) po.PersonaID AS OperadorID,
 LTRIM(RTRIM(CONCAT(ISNULL(p.Nombre,N''),N' ',ISNULL(p.ApellidoPaterno,N''),N' ',ISNULL(p.ApellidoMaterno,N'')))) AS OperadorNombre,
 N'PROGRAMA_OF' AS Fuente
FROM dbo.Planeacion_ProgramaOperadores po
JOIN dbo.Persona p ON p.PersonaID=po.PersonaID
WHERE po.ProgramaProduccionID=@ProgramaID AND po.Activo=1
 AND UPPER(ISNULL(po.RolOperador,N''))=N'PRINCIPAL'
ORDER BY po.ProgramaOperadorID DESC;";

        await using(var cmd=tx==null?new SqlCommand(opSql,cn):new SqlCommand(opSql,cn,tx))
        {
            cmd.Parameters.Add("@ProgramaID",SqlDbType.Int).Value=programaId;
            await using var rd=await cmd.ExecuteReaderAsync();
            if(await rd.ReadAsync())
            {
                operadorId=rd["OperadorID"]==DBNull.Value?null:Convert.ToInt32(rd["OperadorID"]);
                operadorNombre=rd["OperadorNombre"]?.ToString()?.Trim();
                fuente=rd["Fuente"]?.ToString()?.Trim() ?? "V2";
            }
        }

        var turnos=await CargarTurnosResolverV2Async(cn,tx);
        var semanaHoy=InicioSemanaResolverV2(objetivo.Date);
        var semanaAyer=InicioSemanaResolverV2(objetivo.Date.AddDays(-1));
        var coberturas=await CargarCoberturasResolverV2Async(semanaHoy,semanaAyer,cn,tx);

        var aplicables=turnos
            .Where(t=>TurnoCubreResolverV2(t,objetivo))
            .Select(t=>new
            {
                Turno=t,
                FechaTrabajo=FechaTrabajoResolverV2(t,objetivo),
                Semana=InicioSemanaResolverV2(FechaTrabajoResolverV2(t,objetivo))
            })
            .ToList();

        CoberturaResolverV2? Elegir(Func<CoberturaResolverV2,int?> selector)
        {
            return aplicables
                .SelectMany(a=>coberturas
                    .Where(c=>c.TurnoID==a.Turno.TurnoID && c.SemanaInicio.Date==a.Semana.Date)
                    .Select(c=>new { c,a.Turno }))
                .Where(x=>selector(x.c).HasValue)
                .OrderBy(x=>x.Turno.Orden)
                .Select(x=>x.c)
                .FirstOrDefault();
        }

        var cTec=Elegir(x=>x.TecnicoID);
        var cSmed=Elegir(x=>x.SmedID);
        var cAux=Elegir(x=>x.AuxiliarID);

        var tec=await NombrePersonaResolverV2Async(cTec?.TecnicoID,cn,tx);
        var smed=await NombrePersonaResolverV2Async(cSmed?.SmedID,cn,tx);
        var aux=await NombrePersonaResolverV2Async(cAux?.AuxiliarID,cn,tx);

        var turnoProd=aplicables
            .Where(x=>string.Equals(x.Turno.TipoTurno,"Regular",StringComparison.OrdinalIgnoreCase))
            .OrderBy(x=>x.Turno.Orden)
            .FirstOrDefault();

        var ventana=turnoProd==null
            ? (inicioPrograma.Value,finPrograma ?? inicioPrograma.Value.AddHours(1))
            : VentanaTurnoResolverV2(turnoProd.Turno,turnoProd.FechaTrabajo);

        var coberturaId=cTec?.CoberturaID ?? cSmed?.CoberturaID ?? cAux?.CoberturaID ?? 0;
        var tieneAlgo=operadorId.HasValue || tec.Id.HasValue || smed.Id.HasValue || aux.Id.HasValue;
        if(!tieneAlgo) return null;

        return new PersonalProgramadoProduccionInterno
        {
            AsignacionPersonalID=coberturaId,
            OperadorID=operadorId,
            OperadorNombre=operadorNombre,
            TecnicoID=tec.Id,
            TecnicoNombre=tec.Nombre,
            SmedID=smed.Id,
            SmedNombre=smed.Nombre,
            AuxiliarID=aux.Id,
            AuxiliarNombre=aux.Nombre,
            TurnoID=turnoProd?.Turno.TurnoID,
            TurnoNombre=turnoProd?.Turno.Nombre,
            Fuente=fuente,
            Inicio=ventana.Item1,
            Fin=ventana.Item2
        };
    }

    private static async Task<PersonalProgramadoProduccionInterno?>
        ObtenerPersonalProgramadoLegadoV2Async(
            int programaProduccionId,
            DateTime momento,
            DateTime? momentoAlterno,
            SqlConnection cn,
            SqlTransaction? tx)
    {
        const string existeSql=@"
SELECT CONVERT(bit,CASE WHEN OBJECT_ID(N'dbo.Produccion_ProgramaPersonalAsignaciones',N'U') IS NULL THEN 0 ELSE 1 END);";
        await using(var existe=tx==null?new SqlCommand(existeSql,cn):new SqlCommand(existeSql,cn,tx))
        {
            if(!Convert.ToBoolean(await existe.ExecuteScalarAsync() ?? false)) return null;
        }

        const string sql=@"
SELECT TOP(1)
 a.AsignacionPersonalID,a.OperadorID,a.AuxiliarID,a.TecnicoProduccionID,
 a.TurnoID,a.TurnoNombre,a.Inicio,a.Fin,
 op.NombreCompleto AS OperadorNombre,
 aux.NombreCompleto AS AuxiliarNombre,
 tec.NombreCompleto AS TecnicoNombre
FROM dbo.Produccion_ProgramaPersonalAsignaciones a
OUTER APPLY(SELECT LTRIM(RTRIM(CONCAT(ISNULL(Nombre,N''),N' ',ISNULL(ApellidoPaterno,N''),N' ',ISNULL(ApellidoMaterno,N'')))) NombreCompleto FROM dbo.Persona WHERE PersonaID=a.OperadorID) op
OUTER APPLY(SELECT LTRIM(RTRIM(CONCAT(ISNULL(Nombre,N''),N' ',ISNULL(ApellidoPaterno,N''),N' ',ISNULL(ApellidoMaterno,N'')))) NombreCompleto FROM dbo.Persona WHERE PersonaID=a.AuxiliarID) aux
OUTER APPLY(SELECT LTRIM(RTRIM(CONCAT(ISNULL(Nombre,N''),N' ',ISNULL(ApellidoPaterno,N''),N' ',ISNULL(ApellidoMaterno,N'')))) NombreCompleto FROM dbo.Persona WHERE PersonaID=a.TecnicoProduccionID) tec
WHERE a.Activo=1
  AND a.ProgramaProduccionID=@ProgramaID
  AND ((@Momento>=a.Inicio AND @Momento<a.Fin)
       OR (@Alterno IS NOT NULL AND @Alterno>=a.Inicio AND @Alterno<a.Fin))
ORDER BY CASE WHEN @Momento>=a.Inicio AND @Momento<a.Fin THEN 0 ELSE 1 END,
         a.AsignacionPersonalID DESC;";

        await using var cmd=tx==null?new SqlCommand(sql,cn):new SqlCommand(sql,cn,tx);
        cmd.Parameters.Add("@ProgramaID",SqlDbType.Int).Value=programaProduccionId;
        cmd.Parameters.Add("@Momento",SqlDbType.DateTime2).Value=momento;
        cmd.Parameters.Add("@Alterno",SqlDbType.DateTime2).Value=momentoAlterno.HasValue?momentoAlterno.Value:DBNull.Value;
        await using var rd=await cmd.ExecuteReaderAsync();
        if(!await rd.ReadAsync()) return null;

        return new PersonalProgramadoProduccionInterno
        {
            AsignacionPersonalID=Convert.ToInt32(rd["AsignacionPersonalID"]),
            OperadorID=rd["OperadorID"]==DBNull.Value?null:Convert.ToInt32(rd["OperadorID"]),
            OperadorNombre=rd["OperadorNombre"]?.ToString()?.Trim(),
            AuxiliarID=rd["AuxiliarID"]==DBNull.Value?null:Convert.ToInt32(rd["AuxiliarID"]),
            AuxiliarNombre=rd["AuxiliarNombre"]?.ToString()?.Trim(),
            TecnicoID=rd["TecnicoProduccionID"]==DBNull.Value?null:Convert.ToInt32(rd["TecnicoProduccionID"]),
            TecnicoNombre=rd["TecnicoNombre"]?.ToString()?.Trim(),
            TurnoID=rd["TurnoID"]==DBNull.Value?null:Convert.ToInt32(rd["TurnoID"]),
            TurnoNombre=rd["TurnoNombre"]?.ToString()?.Trim(),
            Fuente="LEGADO_OF",
            Inicio=Convert.ToDateTime(rd["Inicio"]),
            Fin=Convert.ToDateTime(rd["Fin"])
        };
    }

    [HttpGet]
    public async Task<IActionResult> PersonalProgramadoPrograma(int programaProduccionId)
    {
        if(!UsuarioEnSesion()) return Unauthorized();
        if(programaProduccionId<=0) return Json(new { ok=false,mensaje="Programa no válido." });

        await using var cn=new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        DateTime? inicio=null;
        const string sql="SELECT TOP(1) FechaInicioProgramada FROM dbo.Planeacion_ProgramaProduccion WHERE ProgramaProduccionID=@ID AND Activo=1;";
        await using(var cmd=new SqlCommand(sql,cn))
        {
            cmd.Parameters.Add("@ID",SqlDbType.Int).Value=programaProduccionId;
            var value=await cmd.ExecuteScalarAsync();
            if(value!=null && value!=DBNull.Value) inicio=Convert.ToDateTime(value);
        }

        var p=await ObtenerPersonalProgramadoProduccionAsync(
            programaProduccionId,DateTime.Now,inicio,cn,null);

        if(p==null) return Json(new { ok=true,asignado=false });

        return Json(new
        {
            ok=true,
            asignado=true,
            asignacionPersonalID=p.AsignacionPersonalID,
            operadorID=p.OperadorID,
            operadorNombre=p.OperadorNombre,
            auxiliarID=p.AuxiliarID,
            auxiliarNombre=p.AuxiliarNombre,
            tecnicoProduccionID=p.TecnicoID,
            tecnicoProduccionNombre=p.TecnicoNombre,
            smedID=p.SmedID,
            smedNombre=p.SmedNombre,
            turnoID=p.TurnoID,
            turno=p.TurnoNombre,
            fuente=p.Fuente,
            inicio=p.Inicio,
            fin=p.Fin
        });
    }
}
