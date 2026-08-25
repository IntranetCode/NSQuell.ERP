using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Globalization;

namespace ERP.NSQuell.Controllers;

public sealed partial class ProduccionPersonalController
{
    private sealed class ProgramaBaseV7
    {
        public int ProgramaID { get; set; }
        public int? SolicitudID { get; set; }
        public string OF { get; set; } = string.Empty;
        public int? ParteID { get; set; }
        public string NumeroParte { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public int MaquinaID { get; set; }
        public string MaquinaCodigo { get; set; } = string.Empty;
        public string MaquinaNombre { get; set; } = string.Empty;
        public DateTime Inicio { get; set; }
        public DateTime Fin { get; set; }
        public int? OperadorBaseID { get; set; }
        public string OperadorBaseNombre { get; set; } = string.Empty;
        public int? EjecucionID { get; set; }
        public int? EstatusProduccionID { get; set; }
    }

    private sealed class AsignacionV7
    {
        public int AsignacionID { get; set; }
        public int ProgramaID { get; set; }
        public DateTime FechaTrabajo { get; set; }
        public int TurnoID { get; set; }
        public DateTime Inicio { get; set; }
        public DateTime Fin { get; set; }
        public int? OperadorID { get; set; }
        public string OperadorNombre { get; set; } = string.Empty;
    }

    private static (string Vista, DateTime Inicio, DateTime Fin, DateTime Referencia, DateTime? RangoInicio, DateTime? RangoFin)
        ResolverPeriodoV7(string? vista, DateTime? fechaDesde, DateTime? fechaHasta)
    {
        var referencia = (fechaDesde ?? DateTime.Today).Date;
        var modo = (vista ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(modo))
            modo = fechaHasta.HasValue ? "rango" : "dia";

        if (modo == "semana")
        {
            var inicio = InicioSemanaV2(referencia);
            return (modo, inicio, inicio.AddDays(7), referencia, null, null);
        }

        if (modo == "mes")
        {
            var inicio = new DateTime(referencia.Year, referencia.Month, 1);
            return (modo, inicio, inicio.AddMonths(1), referencia, null, null);
        }

        if (modo == "rango")
        {
            var ini = referencia;
            var finInc = (fechaHasta ?? referencia).Date;
            if (finInc < ini) (ini, finInc) = (finInc, ini);
            if ((finInc - ini).TotalDays > 92) finInc = ini.AddDays(92);
            return (modo, ini, finInc.AddDays(1), referencia, ini, finInc);
        }

        return ("dia", referencia, referencia.AddDays(1), referencia, null, null);
    }

    private async Task<IActionResult> IndexV7CoreAsync(string? vista, DateTime? fechaDesde, DateTime? fechaHasta)
    {
        if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");

        var p = ResolverPeriodoV7(vista, fechaDesde, fechaHasta);
        var semana = InicioSemanaV2(p.Referencia);
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        var vm = new ProduccionPersonalV7IndexVm
        {
            Vista = p.Vista,
            InicioPeriodo = p.Inicio,
            FinPeriodo = p.Fin,
            FechaReferencia = p.Referencia,
            RangoInicio = p.RangoInicio,
            RangoFin = p.RangoFin,
            SemanaInicio = semana,
            Configurado = await ConfiguradoV7Async(cn, null)
        };

        var escala = await CargarEscalaSemanaV2Async(semana, cn, null);
        if (escala != null)
        {
            vm.EscalaID = escala.EscalaID;
            vm.EscalaFolio = escala.Folio;
            vm.EscalaEstado = escala.Estado;
        }

        await CargarCoberturaSemanalV7Async(vm, escala?.EscalaID, cn);

        // NSQ_V7_4_OPERADORES_COMPLETOS
        // La vista por persona debe incluir a todos los operadores oficiales activos,
        // aunque no tengan ninguna OF asignada en el periodo consultado.
        var operadoresOficialesV74 = await CargarOperadoresOficialesV2Async(cn, null);
        vm.Operadores = operadoresOficialesV74
            .Select(x => new ProduccionPersonalPersonaOpcionVm
            {
                PersonaID = x.PersonaID,
                NumeroControl = x.NumeroControl,
                Nombre = x.Nombre,
                Puesto = "OPERADOR",
                TipoOpcion = "OPERADOR"
            })
            .OrderBy(x => x.Nombre)
            .ToList();

        var programas = await CargarProgramasPeriodoV7Async(p.Inicio, p.Fin, cn, null);
        var asignaciones = await CargarAsignacionesPeriodoV7Async(p.Inicio, p.Fin, cn, null);
        var turnos = (await CargarTurnosV2Async(cn, null))
            .Where(EsTurnoOperadorV7)
            .OrderBy(x => x.Orden)
            .ThenBy(x => x.Inicio)
            .ToList();

        foreach (var programa in programas)
        {
            foreach (var seg in ConstruirSegmentosV7(programa, turnos, p.Inicio, p.Fin))
            {
                var guardada = asignaciones
                    .Where(x => x.ProgramaID == programa.ProgramaID && x.TurnoID == seg.TurnoID && x.FechaTrabajo.Date == seg.FechaTrabajo.Date)
                    .OrderByDescending(x => x.AsignacionID)
                    .FirstOrDefault();

                if (guardada != null)
                {
                    seg.AsignacionPersonalID = guardada.AsignacionID;
                    seg.TieneAsignacionEspecifica = true;
                    seg.OperadorAsignadoID = guardada.OperadorID;
                    seg.OperadorAsignadoNombre = guardada.OperadorNombre;
                }

                vm.Segmentos.Add(seg);
            }
        }

        await CompletarNivelesV7Async(vm.Segmentos, cn, null);
        MarcarConflictosV7(vm.Segmentos);

        vm.Segmentos = vm.Segmentos
            .OrderBy(x => x.Inicio)
            .ThenBy(x => x.MaquinaCodigo)
            .ThenBy(x => x.OF)
            .ToList();

        return View("IndexV7", vm);
    }

    private async Task CargarCoberturaSemanalV7Async(ProduccionPersonalV7IndexVm vm, int? escalaId, SqlConnection cn)
    {
        var semana = vm.SemanaInicio;
        var turnos = await CargarTurnosV2Async(cn, null);
        var guardadas = await CargarCoberturasV2Async(semana, cn, null);
        vm.Tecnicos = await CargarPersonasApoyoV2Async("TECNICO", cn, null);
        var smed = await CargarPersonasApoyoV2Async("SMED", cn, null);
        vm.SmedYTecnicos = UnirPersonasV2(smed, vm.Tecnicos, "SMED / TECNICO");
        vm.Auxiliares = await CargarPersonasApoyoV2Async("AUXILIAR", cn, null);

        foreach (var t in turnos)
        {
            var g = guardadas.FirstOrDefault(x => x.TurnoID == t.TurnoID);
            int? tec = g?.TecnicoID, smedId = g?.SmedID, aux = g?.AuxiliarID;
            var fuente = g?.Fuente ?? "SIN_CONFIGURAR";
            if (g == null && escalaId.HasValue)
            {
                tec = await SugerirApoyoEscalaV2Async(escalaId.Value, t.TurnoID, "TECNICO", cn, null);
                smedId = await SugerirApoyoEscalaV2Async(escalaId.Value, t.TurnoID, "SMED", cn, null);
                aux = await SugerirApoyoEscalaV2Async(escalaId.Value, t.TurnoID, "AUXILIAR", cn, null);
                if (tec.HasValue || smedId.HasValue || aux.HasValue) fuente = "ESCALA_RRHH";
            }
            vm.TurnosApoyo.Add(new ProduccionPersonalTurnoApoyoVm
            {
                TurnoID=t.TurnoID, Nombre=t.Nombre, TipoTurno=t.TipoTurno, Color=t.Color,
                HoraInicio=t.Inicio, HoraFin=t.Fin, CruzaDiaSiguiente=t.Cruza, Orden=t.Orden,
                TecnicoProduccionID=tec, SmedID=smedId, AuxiliarID=aux, Fuente=fuente
            });
        }
    }

    private static bool EsTurnoOperadorV7(TurnoV2 t)
    {
        var nombre=(t.Nombre ?? string.Empty).ToUpperInvariant();
        var tipo=(t.TipoTurno ?? string.Empty).ToUpperInvariant();
        if (!t.Inicio.HasValue || !t.Fin.HasValue) return false;
        if (nombre.Contains("12X12") || nombre.Contains("12 X 12") || tipo.Contains("12")) return false;
        if (nombre.Contains("MIXT")) return false;
        return true;
    }

    private static IEnumerable<ProduccionPersonalV7SegmentoVm> ConstruirSegmentosV7(
        ProgramaBaseV7 p, List<TurnoV2> turnos, DateTime inicioPeriodo, DateTime finPeriodo)
    {
        var desde = p.Inicio.Date.AddDays(-1);
        if (desde < inicioPeriodo.Date.AddDays(-1)) desde = inicioPeriodo.Date.AddDays(-1);
        var hasta = p.Fin.Date;
        if (hasta > finPeriodo.Date) hasta = finPeriodo.Date;

        for (var d = desde; d <= hasta; d = d.AddDays(1))
        {
            foreach (var t in turnos)
            {
                var ti = d.Add(t.Inicio!.Value);
                var tf = d.Add(t.Fin!.Value);
                if (t.Cruza || tf <= ti) tf = tf.AddDays(1);
                var ini = new[] { ti, p.Inicio, inicioPeriodo }.Max();
                var fin = new[] { tf, p.Fin, finPeriodo }.Min();
                if (fin <= ini) continue;

                yield return new ProduccionPersonalV7SegmentoVm
                {
                    ProgramaProduccionID=p.ProgramaID, SolicitudProduccionID=p.SolicitudID, OF=p.OF,
                    ParteID=p.ParteID, NumeroParte=p.NumeroParte, DescripcionParte=p.Descripcion,
                    MaquinaID=p.MaquinaID, MaquinaCodigo=p.MaquinaCodigo, MaquinaNombre=p.MaquinaNombre,
                    InicioPrograma=p.Inicio, FinPrograma=p.Fin, FechaTrabajo=d.Date, TurnoID=t.TurnoID,
                    TurnoNombre=t.Nombre, Inicio=ini, Fin=fin, OperadorBaseID=p.OperadorBaseID,
                    OperadorBaseNombre=p.OperadorBaseNombre, EjecucionProduccionID=p.EjecucionID,
                    EstatusProduccionID=p.EstatusProduccionID, ProduccionActiva=p.EstatusProduccionID == 3
                };
            }
        }
    }

    private static async Task<List<ProgramaBaseV7>> CargarProgramasPeriodoV7Async(
        DateTime desde, DateTime hasta, SqlConnection cn, SqlTransaction? tx)
    {
        const string sql=@"
SELECT pp.ProgramaProduccionID,pp.SolicitudProduccionID,
 COALESCE(NULLIF(LTRIM(RTRIM(s.NumeroOFRecibida)),N''),NULLIF(LTRIM(RTRIM(s.FolioSolicitud)),N''),CONCAT(N'PROG-',pp.ProgramaProduccionID)) AS [OF],
 pp.ParteID,ISNULL(pp.NumeroParte,N'') NumeroParte,ISNULL(pp.DesignacionDescripcionSAP,N'') Descripcion,
 pp.MaquinaID,ISNULL(pp.MaquinaCodigo,N'') MaquinaCodigo,ISNULL(pp.MaquinaNombre,N'') MaquinaNombre,
 pp.FechaInicioProgramada,
 ISNULL(pp.FechaFinProgramada,DATEADD(MINUTE,CONVERT(INT,CEILING(ISNULL(pp.HorasProgramadas,1)*60)),pp.FechaInicioProgramada)) FechaFinProgramada,
 op.PersonaID OperadorBaseID,
 LTRIM(RTRIM(CONCAT(ISNULL(per.Nombre,N''),N' ',ISNULL(per.ApellidoPaterno,N''),N' ',ISNULL(per.ApellidoMaterno,N'')))) OperadorBaseNombre,
 ej.EjecucionProduccionID,ej.EstatusID EstatusProduccionID
FROM dbo.Planeacion_ProgramaProduccion pp
LEFT JOIN dbo.SolicitudesProduccion s ON s.SolicitudProduccionID=pp.SolicitudProduccionID
OUTER APPLY(SELECT TOP(1) x.PersonaID FROM dbo.Planeacion_ProgramaOperadores x WHERE x.ProgramaProduccionID=pp.ProgramaProduccionID AND x.Activo=1 AND UPPER(LTRIM(RTRIM(ISNULL(x.RolOperador,N''))))=N'PRINCIPAL' ORDER BY x.ProgramaOperadorID DESC) op
LEFT JOIN dbo.Persona per ON per.PersonaID=op.PersonaID
OUTER APPLY(SELECT TOP(1) e.EjecucionProduccionID,e.EstatusID FROM dbo.Produccion_Ejecucion e WHERE e.ProgramaProduccionID=pp.ProgramaProduccionID AND e.Activo=1 ORDER BY e.EjecucionProduccionID DESC) ej
WHERE pp.Activo=1 AND pp.MaquinaID IS NOT NULL AND pp.FechaInicioProgramada IS NOT NULL
 AND ISNULL(pp.EstatusID,1)<>99
 AND pp.FechaInicioProgramada<@Hasta
 AND ISNULL(pp.FechaFinProgramada,DATEADD(MINUTE,CONVERT(INT,CEILING(ISNULL(pp.HorasProgramadas,1)*60)),pp.FechaInicioProgramada))>@Desde
ORDER BY pp.FechaInicioProgramada,pp.MaquinaCodigo,pp.ProgramaProduccionID;";
        var lista=new List<ProgramaBaseV7>();
        await using var cmd=tx==null?new SqlCommand(sql,cn):new SqlCommand(sql,cn,tx);
        cmd.Parameters.Add("@Desde",SqlDbType.DateTime2).Value=desde;
        cmd.Parameters.Add("@Hasta",SqlDbType.DateTime2).Value=hasta;
        await using var rd=await cmd.ExecuteReaderAsync();
        while(await rd.ReadAsync()) lista.Add(new ProgramaBaseV7
        {
            ProgramaID=Convert.ToInt32(rd["ProgramaProduccionID"]),
            SolicitudID=rd["SolicitudProduccionID"]==DBNull.Value?null:Convert.ToInt32(rd["SolicitudProduccionID"]),
            OF=rd["OF"]?.ToString()?.Trim()??string.Empty,
            ParteID=rd["ParteID"]==DBNull.Value?null:Convert.ToInt32(rd["ParteID"]),
            NumeroParte=rd["NumeroParte"]?.ToString()?.Trim()??string.Empty,
            Descripcion=rd["Descripcion"]?.ToString()?.Trim()??string.Empty,
            MaquinaID=Convert.ToInt32(rd["MaquinaID"]), MaquinaCodigo=rd["MaquinaCodigo"]?.ToString()?.Trim()??string.Empty,
            MaquinaNombre=rd["MaquinaNombre"]?.ToString()?.Trim()??string.Empty,
            Inicio=Convert.ToDateTime(rd["FechaInicioProgramada"]), Fin=Convert.ToDateTime(rd["FechaFinProgramada"]),
            OperadorBaseID=rd["OperadorBaseID"]==DBNull.Value?null:Convert.ToInt32(rd["OperadorBaseID"]),
            OperadorBaseNombre=rd["OperadorBaseNombre"]?.ToString()?.Trim()??string.Empty,
            EjecucionID=rd["EjecucionProduccionID"]==DBNull.Value?null:Convert.ToInt32(rd["EjecucionProduccionID"]),
            EstatusProduccionID=rd["EstatusProduccionID"]==DBNull.Value?null:Convert.ToInt32(rd["EstatusProduccionID"])
        });
        return lista;
    }

    private static async Task<List<AsignacionV7>> CargarAsignacionesPeriodoV7Async(
        DateTime desde, DateTime hasta, SqlConnection cn, SqlTransaction? tx)
    {
        const string sql=@"
SELECT a.AsignacionPersonalID,a.ProgramaProduccionID,a.FechaTrabajo,a.TurnoID,a.Inicio,a.Fin,a.OperadorID,
 LTRIM(RTRIM(CONCAT(ISNULL(p.Nombre,N''),N' ',ISNULL(p.ApellidoPaterno,N''),N' ',ISNULL(p.ApellidoMaterno,N'')))) OperadorNombre
FROM dbo.Produccion_ProgramaPersonalAsignaciones a
LEFT JOIN dbo.Persona p ON p.PersonaID=a.OperadorID
WHERE a.Activo=1 AND a.Inicio<@Hasta AND a.Fin>@Desde;";
        var lista=new List<AsignacionV7>();
        await using var cmd=tx==null?new SqlCommand(sql,cn):new SqlCommand(sql,cn,tx);
        cmd.Parameters.Add("@Desde",SqlDbType.DateTime2).Value=desde; cmd.Parameters.Add("@Hasta",SqlDbType.DateTime2).Value=hasta;
        await using var rd=await cmd.ExecuteReaderAsync();
        while(await rd.ReadAsync()) lista.Add(new AsignacionV7
        {
            AsignacionID=Convert.ToInt32(rd["AsignacionPersonalID"]), ProgramaID=Convert.ToInt32(rd["ProgramaProduccionID"]),
            FechaTrabajo=Convert.ToDateTime(rd["FechaTrabajo"]), TurnoID=Convert.ToInt32(rd["TurnoID"]),
            Inicio=Convert.ToDateTime(rd["Inicio"]), Fin=Convert.ToDateTime(rd["Fin"]),
            OperadorID=rd["OperadorID"]==DBNull.Value?null:Convert.ToInt32(rd["OperadorID"]), OperadorNombre=rd["OperadorNombre"]?.ToString()?.Trim()??string.Empty
        });
        return lista;
    }

    private static async Task CompletarNivelesV7Async(List<ProduccionPersonalV7SegmentoVm> segmentos, SqlConnection cn, SqlTransaction? tx)
    {
        var niveles=await CargarNivelesV2Async(cn,tx);
        foreach(var s in segmentos)
            if(s.ParteID.HasValue && s.OperadorEfectivoID.HasValue && niveles.TryGetValue((s.ParteID.Value,s.OperadorEfectivoID.Value),out var n)) s.NivelOperador=n;
    }

    private static void MarcarConflictosV7(List<ProduccionPersonalV7SegmentoVm> segmentos)
    {
        foreach(var grupo in segmentos.Where(x=>x.OperadorEfectivoID.HasValue).GroupBy(x=>x.OperadorEfectivoID!.Value))
        {
            var arr=grupo.OrderBy(x=>x.Inicio).ToList();
            for(int i=0;i<arr.Count;i++) for(int j=i+1;j<arr.Count;j++)
            {
                if(arr[j].Inicio>=arr[i].Fin) break;
                if(arr[i].ProgramaProduccionID==arr[j].ProgramaProduccionID) continue;
                if(arr[i].Inicio<arr[j].Fin && arr[i].Fin>arr[j].Inicio){arr[i].TieneConflicto=true;arr[j].TieneConflicto=true;}
            }
        }
    }

    private static async Task<bool> ConfiguradoV7Async(SqlConnection cn, SqlTransaction? tx)
    {
        const string sql=@"SELECT CONVERT(bit,CASE WHEN OBJECT_ID(N'dbo.Produccion_ProgramaPersonalAsignaciones',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.Produccion_PersonalAsignacionHistorial',N'U') IS NOT NULL THEN 1 ELSE 0 END);";
        await using var cmd=tx==null?new SqlCommand(sql,cn):new SqlCommand(sql,cn,tx);
        return Convert.ToBoolean(await cmd.ExecuteScalarAsync()??false);
    }

    [HttpGet("CandidatosV7")]
    public async Task<IActionResult> CandidatosV7(int programaProduccionId,int turnoId,DateTime fechaTrabajo)
    {
        if(!UsuarioEnSesion()) return Unauthorized();
        await using var cn=new SqlConnection(ConnectionString); await cn.OpenAsync();
        var programas=await CargarProgramasPeriodoV7Async(fechaTrabajo.Date.AddDays(-1),fechaTrabajo.Date.AddDays(2),cn,null);
        var p=programas.FirstOrDefault(x=>x.ProgramaID==programaProduccionId);
        if(p==null) return Json(new{ok=false,mensaje="No se encontró la OF."});
        var turno=(await CargarTurnosV2Async(cn,null)).FirstOrDefault(x=>x.TurnoID==turnoId);
        if(turno==null || !turno.Inicio.HasValue || !turno.Fin.HasValue) return Json(new{ok=false,mensaje="Turno no válido."});
        var ti=fechaTrabajo.Date.Add(turno.Inicio.Value); var tf=fechaTrabajo.Date.Add(turno.Fin.Value); if(turno.Cruza||tf<=ti)tf=tf.AddDays(1);
        var ini=ti>p.Inicio?ti:p.Inicio; var fin=tf<p.Fin?tf:p.Fin;
        if(fin<=ini) return Json(new{ok=false,mensaje="El turno no cruza con la OF."});

        var oficiales=await CargarOperadoresOficialesV2Async(cn,null); var niveles=await CargarNivelesV2Async(cn,null);
        var tieneMatriz=p.ParteID.HasValue && niveles.Keys.Any(x=>x.ParteID==p.ParteID.Value);
        var semana=InicioSemanaV2(fechaTrabajo); var escala=await CargarEscalaSemanaV2Async(semana,cn,null);
        var escalaOps=escala==null?new List<EscalaOperadorV2>():await CargarEscalaOperadoresV2Async(escala.EscalaID,cn,null);
        var cargas=await CargarCargaOperadoresV7Async(p.ProgramaID,ini,fin,cn,null);
        var datos=oficiales.Select(o=>
        {
            int? nivel=null; if(p.ParteID.HasValue && niveles.TryGetValue((p.ParteID.Value,o.PersonaID),out var n))nivel=n;
            var en=escala==null || escalaOps.Any(x=>x.PersonaID==o.PersonaID && EscalaCubreMomentoV2(x,ini));
            var misma=escalaOps.Any(x=>x.PersonaID==o.PersonaID && x.MaquinaID==p.MaquinaID && EscalaCubreMomentoV2(x,ini));
            cargas.TryGetValue(o.PersonaID,out var c);
            return new{personaID=o.PersonaID,numeroControl=o.NumeroControl,nombre=o.Nombre,nivel,enEscala=en,mismaMaquina=misma,horasSemana=c.Horas,conflicto=c.Conflicto};
        }).Where(x=>!tieneMatriz || x.nivel.HasValue)
          .OrderBy(x=>x.conflicto).ThenByDescending(x=>x.enEscala).ThenByDescending(x=>x.nivel??0).ThenBy(x=>x.horasSemana).ThenBy(x=>x.nombre).ToList();
        return Json(new{ok=true,tieneMatriz,programaProduccionID=p.ProgramaID,of=p.OF,turno=turno.Nombre,inicio=ini,fin,candidatos=datos});
    }

    private static async Task<Dictionary<int,(decimal Horas,bool Conflicto)>> CargarCargaOperadoresV7Async(
        int programaExcluir, DateTime ini, DateTime fin, SqlConnection cn, SqlTransaction? tx)
    {
        var semana=InicioSemanaV2(ini);
        var hasta=semana.AddDays(7);
        var programas=await CargarProgramasPeriodoV7Async(semana,hasta,cn,tx);
        var asignaciones=await CargarAsignacionesPeriodoV7Async(semana,hasta,cn,tx);
        var turnos=(await CargarTurnosV2Async(cn,tx))
            .Where(EsTurnoOperadorV7)
            .OrderBy(x=>x.Orden)
            .ThenBy(x=>x.Inicio)
            .ToList();
        var segmentos=new List<ProduccionPersonalV7SegmentoVm>();
        foreach(var programa in programas)
        {
            foreach(var seg in ConstruirSegmentosV7(programa,turnos,semana,hasta))
            {
                var guardada=asignaciones
                    .Where(x=>x.ProgramaID==programa.ProgramaID && x.TurnoID==seg.TurnoID && x.FechaTrabajo.Date==seg.FechaTrabajo.Date)
                    .OrderByDescending(x=>x.AsignacionID)
                    .FirstOrDefault();
                if(guardada!=null)
                {
                    seg.TieneAsignacionEspecifica=true;
                    seg.OperadorAsignadoID=guardada.OperadorID;
                    seg.OperadorAsignadoNombre=guardada.OperadorNombre;
                }
                segmentos.Add(seg);
            }
        }

        var resultado=new Dictionary<int,(decimal Horas,bool Conflicto)>();
        foreach(var grupo in segmentos.Where(x=>x.OperadorEfectivoID.HasValue).GroupBy(x=>x.OperadorEfectivoID!.Value))
        {
            var horas=Convert.ToDecimal(grupo.Sum(x=>Math.Max(0,(x.Fin-x.Inicio).TotalHours)));
            var conflicto=grupo.Any(x=>x.ProgramaProduccionID!=programaExcluir && x.Inicio<fin && x.Fin>ini);
            resultado[grupo.Key]=(Math.Round(horas,2),conflicto);
        }
        return resultado;
    }

    [HttpPost("GuardarTurnoV7")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarTurnoV7(ProduccionPersonalV7GuardarRequest vm)
    {
        if(!UsuarioEnSesion()) return RedirectToAction("Login","Login");
        await using var cn=new SqlConnection(ConnectionString); await cn.OpenAsync();
        await using var tx=(SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var programas=await CargarProgramasPeriodoV7Async(vm.FechaTrabajo.Date.AddDays(-1),vm.FechaTrabajo.Date.AddDays(2),cn,tx);
            var p=programas.FirstOrDefault(x=>x.ProgramaID==vm.ProgramaProduccionID)??throw new InvalidOperationException("La OF ya no está disponible.");
            var t=(await CargarTurnosV2Async(cn,tx)).FirstOrDefault(x=>x.TurnoID==vm.TurnoID)??throw new InvalidOperationException("El turno ya no está disponible.");
            if(!t.Inicio.HasValue||!t.Fin.HasValue) throw new InvalidOperationException("El turno no tiene horario definido.");
            var ti=vm.FechaTrabajo.Date.Add(t.Inicio.Value); var tf=vm.FechaTrabajo.Date.Add(t.Fin.Value); if(t.Cruza||tf<=ti)tf=tf.AddDays(1);
            var ini=ti>p.Inicio?ti:p.Inicio; var fin=tf<p.Fin?tf:p.Fin; if(fin<=ini)throw new InvalidOperationException("El turno no cruza con la OF.");
            var produciendo=p.EstatusProduccionID==3;
            var motivo=(vm.Motivo??string.Empty).Trim(); var just=(vm.Justificacion??string.Empty).Trim();
            if(produciendo && (!vm.OperadorID.HasValue || vm.OperadorID.Value<=0)) throw new InvalidOperationException("No se puede dejar una OF en producción sin operador.");
            if(produciendo && (motivo.Length<3 || just.Length<5)) throw new InvalidOperationException("Al modificar una OF en producción son obligatorios motivo y justificación.");
            var tieneCruceAdvertido=false;
            if(vm.OperadorID.HasValue && vm.OperadorID.Value>0)
            {
                tieneCruceAdvertido=await ValidarOperadorTurnoV7Async(vm.OperadorID.Value,p.ParteID,ini,fin,p.ProgramaID,cn,tx);
            }

            const string sel=@"SELECT TOP(1) AsignacionPersonalID,OperadorID FROM dbo.Produccion_ProgramaPersonalAsignaciones WITH(UPDLOCK,HOLDLOCK) WHERE ProgramaProduccionID=@ProgramaID AND TurnoID=@TurnoID AND FechaTrabajo=@Fecha AND Activo=1 ORDER BY AsignacionPersonalID DESC;";
            int? asignId=null, anterior=null; await using(var cmd=new SqlCommand(sel,cn,tx))
            {cmd.Parameters.Add("@ProgramaID",SqlDbType.Int).Value=p.ProgramaID;cmd.Parameters.Add("@TurnoID",SqlDbType.Int).Value=t.TurnoID;cmd.Parameters.Add("@Fecha",SqlDbType.Date).Value=vm.FechaTrabajo.Date;await using var rd=await cmd.ExecuteReaderAsync();if(await rd.ReadAsync()){asignId=Convert.ToInt32(rd["AsignacionPersonalID"]);anterior=rd["OperadorID"]==DBNull.Value?null:Convert.ToInt32(rd["OperadorID"]);}}
            if(!asignId.HasValue) anterior=p.OperadorBaseID;
            var nuevo=vm.OperadorID.HasValue&&vm.OperadorID.Value>0?vm.OperadorID:null;
            var uid=UsuarioID();
            if(asignId.HasValue)
            {
                const string up=@"UPDATE dbo.Produccion_ProgramaPersonalAsignaciones SET FechaTrabajo=@Fecha,TurnoNombre=@Turno,Inicio=@Inicio,Fin=@Fin,OperadorID=@Operador,Observaciones=CASE WHEN @Operador IS NULL THEN N'SIN_OPERADOR' ELSE NULL END,UsuarioModificacionID=@Usuario,FechaModificacion=SYSDATETIME(),Activo=1 WHERE AsignacionPersonalID=@ID;";
                await using var cmd=new SqlCommand(up,cn,tx);AgregarParametrosV7(cmd,p,t,vm.FechaTrabajo,ini,fin,nuevo,uid);cmd.Parameters.Add("@ID",SqlDbType.Int).Value=asignId.Value;await cmd.ExecuteNonQueryAsync();
            }
            else
            {
                const string ins=@"INSERT dbo.Produccion_ProgramaPersonalAsignaciones(ProgramaProduccionID,FechaTrabajo,TurnoID,TurnoNombre,Inicio,Fin,OperadorID,AuxiliarID,TecnicoProduccionID,Observaciones,UsuarioCreacionID,FechaCreacion,Activo) OUTPUT INSERTED.AsignacionPersonalID VALUES(@ProgramaID,@Fecha,@TurnoID,@Turno,@Inicio,@Fin,@Operador,NULL,NULL,CASE WHEN @Operador IS NULL THEN N'SIN_OPERADOR' ELSE NULL END,@Usuario,SYSDATETIME(),1);";
                await using var cmd=new SqlCommand(ins,cn,tx);AgregarParametrosV7(cmd,p,t,vm.FechaTrabajo,ini,fin,nuevo,uid);asignId=Convert.ToInt32(await cmd.ExecuteScalarAsync());
            }
            if(anterior!=nuevo)
            {
                const string hist=@"INSERT dbo.Produccion_PersonalAsignacionHistorial(AsignacionPersonalID,ProgramaProduccionID,FechaTrabajo,TurnoID,TurnoNombre,Inicio,Fin,Rol,PersonaAnteriorID,PersonaNuevaID,Motivo,Justificacion,Origen,ProduccionActiva,UsuarioID,FechaMovimiento) VALUES(@Asign,@Programa,@Fecha,@TurnoID,@Turno,@Inicio,@Fin,N'OPERADOR',@Anterior,@Nuevo,@Motivo,@Justificacion,N'PRODUCCION_PERSONAL_V7',@Produccion,@Usuario,SYSDATETIME());";
                await using var cmd=new SqlCommand(hist,cn,tx);cmd.Parameters.Add("@Asign",SqlDbType.Int).Value=(object?)asignId??DBNull.Value;cmd.Parameters.Add("@Programa",SqlDbType.Int).Value=p.ProgramaID;cmd.Parameters.Add("@Fecha",SqlDbType.Date).Value=vm.FechaTrabajo.Date;cmd.Parameters.Add("@TurnoID",SqlDbType.Int).Value=t.TurnoID;cmd.Parameters.Add("@Turno",SqlDbType.NVarChar,100).Value=t.Nombre;cmd.Parameters.Add("@Inicio",SqlDbType.DateTime2).Value=ini;cmd.Parameters.Add("@Fin",SqlDbType.DateTime2).Value=fin;cmd.Parameters.Add("@Anterior",SqlDbType.Int).Value=(object?)anterior??DBNull.Value;cmd.Parameters.Add("@Nuevo",SqlDbType.Int).Value=(object?)nuevo??DBNull.Value;cmd.Parameters.Add("@Motivo",SqlDbType.NVarChar,150).Value=string.IsNullOrWhiteSpace(motivo)?(tieneCruceAdvertido?"PROGRAMACION_CON_CRUCE":"PROGRAMACION"):motivo[..Math.Min(150,motivo.Length)];cmd.Parameters.Add("@Justificacion",SqlDbType.NVarChar,500).Value=string.IsNullOrWhiteSpace(just)?DBNull.Value:just[..Math.Min(500,just.Length)];cmd.Parameters.Add("@Produccion",SqlDbType.Bit).Value=produciendo;cmd.Parameters.Add("@Usuario",SqlDbType.Int).Value=uid;await cmd.ExecuteNonQueryAsync();
            }
            if(produciendo && DateTime.Now>=ini && DateTime.Now<fin && nuevo.HasValue)
            {
                const string ex=@"UPDATE e SET OperadorID=@Operador,OperadorNombre=LTRIM(RTRIM(CONCAT(ISNULL(p.Nombre,N''),N' ',ISNULL(p.ApellidoPaterno,N''),N' ',ISNULL(p.ApellidoMaterno,N'')))),UsuarioModificacionID=@Usuario,FechaModificacion=SYSDATETIME() FROM dbo.Produccion_Ejecucion e INNER JOIN dbo.Persona p ON p.PersonaID=@Operador WHERE e.ProgramaProduccionID=@Programa AND e.Activo=1 AND e.EstatusID IN(3,4);";
                await using var cmd=new SqlCommand(ex,cn,tx);cmd.Parameters.Add("@Operador",SqlDbType.Int).Value=nuevo.Value;cmd.Parameters.Add("@Usuario",SqlDbType.Int).Value=uid;cmd.Parameters.Add("@Programa",SqlDbType.Int).Value=p.ProgramaID;await cmd.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
            TempData["Success"]=tieneCruceAdvertido
                ? "Asignación guardada con advertencia: el operador tiene otra OF que cruza con este horario. Revisa Conflictos."
                : "Asignación por turno guardada correctamente.";
        }
        catch(Exception ex){try{await tx.RollbackAsync();}catch{}TempData["Error"]="No fue posible guardar: "+ex.Message;}
        var panelPost=Request.Form["Panel"].ToString();
        return RedirectToAction(nameof(Index),new{vista=vm.Vista,fechaDesde=(vm.FechaDesde??vm.FechaTrabajo).ToString("yyyy-MM-dd"),fechaHasta=vm.FechaHasta?.ToString("yyyy-MM-dd"),panel=string.IsNullOrWhiteSpace(panelPost)?"planner":panelPost});
    }

    private static void AgregarParametrosV7(SqlCommand cmd,ProgramaBaseV7 p,TurnoV2 t,DateTime fecha,DateTime ini,DateTime fin,int? op,int uid)
    {cmd.Parameters.Add("@ProgramaID",SqlDbType.Int).Value=p.ProgramaID;cmd.Parameters.Add("@Fecha",SqlDbType.Date).Value=fecha.Date;cmd.Parameters.Add("@TurnoID",SqlDbType.Int).Value=t.TurnoID;cmd.Parameters.Add("@Turno",SqlDbType.NVarChar,100).Value=t.Nombre;cmd.Parameters.Add("@Inicio",SqlDbType.DateTime2).Value=ini;cmd.Parameters.Add("@Fin",SqlDbType.DateTime2).Value=fin;cmd.Parameters.Add("@Operador",SqlDbType.Int).Value=(object?)op??DBNull.Value;cmd.Parameters.Add("@Usuario",SqlDbType.Int).Value=uid;}

    private static async Task<bool> ValidarOperadorTurnoV7Async(int personaId,int? parteId,DateTime ini,DateTime fin,int programaId,SqlConnection cn,SqlTransaction tx)
    {
        const string activo=@"SELECT COUNT(1) FROM dbo.Persona WHERE PersonaID=@ID AND ISNULL(EsColaboradorActivo,1)=1;";
        await using(var cmd=new SqlCommand(activo,cn,tx))
        {
            cmd.Parameters.Add("@ID",SqlDbType.Int).Value=personaId;
            if(Convert.ToInt32(await cmd.ExecuteScalarAsync())<=0)
                throw new InvalidOperationException("El operador ya no está activo.");
        }

        if(parteId.HasValue)
        {
            const string pol=@"SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.vw_RRHH_PolivalenciaOperadoresParte WHERE ParteID=@Parte) THEN CASE WHEN EXISTS(SELECT 1 FROM dbo.vw_RRHH_PolivalenciaOperadoresParte WHERE ParteID=@Parte AND PersonalID=@Persona AND Nivel BETWEEN 1 AND 4) THEN 1 ELSE 0 END ELSE 1 END;";
            await using var cmd=new SqlCommand(pol,cn,tx);
            cmd.Parameters.Add("@Parte",SqlDbType.Int).Value=parteId.Value;
            cmd.Parameters.Add("@Persona",SqlDbType.Int).Value=personaId;
            if(Convert.ToInt32(await cmd.ExecuteScalarAsync())!=1)
                throw new InvalidOperationException("El operador no tiene polivalencia N1-N4 para esta pieza.");
        }

        // V7.5: un cruce NO bloquea la selección. Producción puede requerir que un
        // operador atienda dos máquinas simultáneamente. Se devuelve la advertencia
        // para mostrarla y registrarla, pero la decisión permanece con el usuario.
        var cargas=await CargarCargaOperadoresV7Async(programaId,ini,fin,cn,tx);
        return cargas.TryGetValue(personaId,out var carga) && carga.Conflicto;
    }

    [HttpPost("SugerirDiaV7")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SugerirDiaV7(DateTime fecha,string? panel)
    {
        if(!UsuarioEnSesion()) return RedirectToAction("Login","Login");
        var dia=fecha.Date;
        await using var cn=new SqlConnection(ConnectionString); await cn.OpenAsync();
        await using var tx=(SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            if(!await ConfiguradoV7Async(cn,tx))
                throw new InvalidOperationException("Falta completar la estructura V7 en esta base.");

            var programas=await CargarProgramasPeriodoV7Async(dia,dia.AddDays(1),cn,tx);
            var asignaciones=await CargarAsignacionesPeriodoV7Async(dia.AddDays(-1),dia.AddDays(2),cn,tx);
            var turnos=(await CargarTurnosV2Async(cn,tx))
                .Where(EsTurnoOperadorV7)
                .OrderBy(x=>x.Orden)
                .ThenBy(x=>x.Inicio)
                .ToList();
            var oficiales=await CargarOperadoresOficialesV2Async(cn,tx);
            var niveles=await CargarNivelesV2Async(cn,tx);
            var semana=InicioSemanaV2(dia);
            var escala=await CargarEscalaSemanaV2Async(semana,cn,tx);
            var escalaOps=escala==null
                ? new List<EscalaOperadorV2>()
                : await CargarEscalaOperadoresV2Async(escala.EscalaID,cn,tx);

            var sugeridos=0;
            var sinCandidato=0;
            var omitidosProduccion=0;
            var uid=UsuarioID();

            foreach(var programa in programas.OrderBy(x=>x.Inicio).ThenBy(x=>x.MaquinaCodigo))
            {
                foreach(var seg in ConstruirSegmentosV7(programa,turnos,dia,dia.AddDays(1)).OrderBy(x=>x.Inicio))
                {
                    var guardada=asignaciones
                        .Where(x=>x.ProgramaID==programa.ProgramaID && x.TurnoID==seg.TurnoID && x.FechaTrabajo.Date==seg.FechaTrabajo.Date)
                        .OrderByDescending(x=>x.AsignacionID)
                        .FirstOrDefault();

                    if(guardada!=null)
                    {
                        seg.AsignacionPersonalID=guardada.AsignacionID;
                        seg.TieneAsignacionEspecifica=true;
                        seg.OperadorAsignadoID=guardada.OperadorID;
                        seg.OperadorAsignadoNombre=guardada.OperadorNombre;
                    }

                    if(seg.OperadorEfectivoID.HasValue) continue;
                    if(seg.ProduccionActiva){omitidosProduccion++;continue;}

                    var tieneMatriz=programa.ParteID.HasValue && niveles.Keys.Any(x=>x.ParteID==programa.ParteID.Value);
                    var cargas=await CargarCargaOperadoresV7Async(programa.ProgramaID,seg.Inicio,seg.Fin,cn,tx);

                    var candidatos=new List<(OperadorOficialV2 Op,int? Nivel,bool EnEscala,bool MismaMaquina,decimal Horas)>();
                    foreach(var op in oficiales)
                    {
                        int? nivel=null;
                        if(programa.ParteID.HasValue && niveles.TryGetValue((programa.ParteID.Value,op.PersonaID),out var n)) nivel=n;
                        if(tieneMatriz && !nivel.HasValue) continue;

                        var enEscala=escala==null || escalaOps.Any(x=>x.PersonaID==op.PersonaID && EscalaCubreMomentoV2(x,seg.Inicio));
                        var misma=escalaOps.Any(x=>x.PersonaID==op.PersonaID && x.MaquinaID==programa.MaquinaID && EscalaCubreMomentoV2(x,seg.Inicio));
                        var hayCarga=cargas.TryGetValue(op.PersonaID,out var carga);
                        if(hayCarga && carga.Conflicto) continue; // La sugerencia automática nunca crea cruces.
                        candidatos.Add((op,nivel,enEscala,misma,hayCarga?carga.Horas:0m));
                    }

                    var elegido=candidatos
                        .OrderByDescending(x=>x.EnEscala)
                        .ThenByDescending(x=>x.Nivel??0)
                        .ThenByDescending(x=>x.MismaMaquina)
                        .ThenBy(x=>x.Horas)
                        .ThenBy(x=>x.Op.Nombre)
                        .FirstOrDefault();

                    if(elegido.Op==null){sinCandidato++;continue;}

                    var turno=turnos.First(x=>x.TurnoID==seg.TurnoID);
                    int asignacionId;
                    if(guardada!=null)
                    {
                        const string up=@"UPDATE dbo.Produccion_ProgramaPersonalAsignaciones SET FechaTrabajo=@Fecha,TurnoNombre=@Turno,Inicio=@Inicio,Fin=@Fin,OperadorID=@Operador,Observaciones=CASE WHEN @Operador IS NULL THEN N'SIN_OPERADOR' ELSE NULL END,UsuarioModificacionID=@Usuario,FechaModificacion=SYSDATETIME(),Activo=1 WHERE AsignacionPersonalID=@ID;";
                        await using var cmd=new SqlCommand(up,cn,tx);
                        AgregarParametrosV7(cmd,programa,turno,seg.FechaTrabajo,seg.Inicio,seg.Fin,elegido.Op.PersonaID,uid);
                        cmd.Parameters.Add("@ID",SqlDbType.Int).Value=guardada.AsignacionID;
                        await cmd.ExecuteNonQueryAsync();
                        asignacionId=guardada.AsignacionID;
                    }
                    else
                    {
                        const string ins=@"INSERT dbo.Produccion_ProgramaPersonalAsignaciones(ProgramaProduccionID,FechaTrabajo,TurnoID,TurnoNombre,Inicio,Fin,OperadorID,AuxiliarID,TecnicoProduccionID,Observaciones,UsuarioCreacionID,FechaCreacion,Activo) OUTPUT INSERTED.AsignacionPersonalID VALUES(@ProgramaID,@Fecha,@TurnoID,@Turno,@Inicio,@Fin,@Operador,NULL,NULL,N'SUGERENCIA AUTOMATICA DEL DIA',@Usuario,SYSDATETIME(),1);";
                        await using var cmd=new SqlCommand(ins,cn,tx);
                        AgregarParametrosV7(cmd,programa,turno,seg.FechaTrabajo,seg.Inicio,seg.Fin,elegido.Op.PersonaID,uid);
                        asignacionId=Convert.ToInt32(await cmd.ExecuteScalarAsync());
                    }

                    const string hist=@"INSERT dbo.Produccion_PersonalAsignacionHistorial(AsignacionPersonalID,ProgramaProduccionID,FechaTrabajo,TurnoID,TurnoNombre,Inicio,Fin,Rol,PersonaAnteriorID,PersonaNuevaID,Motivo,Justificacion,Origen,ProduccionActiva,UsuarioID,FechaMovimiento) VALUES(@Asign,@Programa,@Fecha,@TurnoID,@Turno,@Inicio,@Fin,N'OPERADOR',NULL,@Nuevo,N'SUGERENCIA_AUTOMATICA_DIA',N'Asignación sugerida por polivalencia, escala, disponibilidad y carga.',N'PRODUCCION_PERSONAL_V7_SUGERIR',0,@Usuario,SYSDATETIME());";
                    await using(var cmd=new SqlCommand(hist,cn,tx))
                    {
                        cmd.Parameters.Add("@Asign",SqlDbType.Int).Value=asignacionId;
                        cmd.Parameters.Add("@Programa",SqlDbType.Int).Value=programa.ProgramaID;
                        cmd.Parameters.Add("@Fecha",SqlDbType.Date).Value=seg.FechaTrabajo;
                        cmd.Parameters.Add("@TurnoID",SqlDbType.Int).Value=seg.TurnoID;
                        cmd.Parameters.Add("@Turno",SqlDbType.NVarChar,100).Value=seg.TurnoNombre;
                        cmd.Parameters.Add("@Inicio",SqlDbType.DateTime2).Value=seg.Inicio;
                        cmd.Parameters.Add("@Fin",SqlDbType.DateTime2).Value=seg.Fin;
                        cmd.Parameters.Add("@Nuevo",SqlDbType.Int).Value=elegido.Op.PersonaID;
                        cmd.Parameters.Add("@Usuario",SqlDbType.Int).Value=uid;
                        await cmd.ExecuteNonQueryAsync();
                    }
                    sugeridos++;
                }
            }

            await tx.CommitAsync();
            TempData["Success"]=$"Sugerencias del {dia:dd/MM/yyyy}: {sugeridos} horario(s) completados. Sin candidato libre: {sinCandidato}. En producción omitidos: {omitidosProduccion}.";
        }
        catch(Exception ex)
        {
            try{await tx.RollbackAsync();}catch{}
            TempData["Error"]="No fue posible sugerir operadores: "+ex.Message;
        }
        return RedirectToAction(nameof(Index),new{vista="dia",fechaDesde=dia.ToString("yyyy-MM-dd"),panel=string.IsNullOrWhiteSpace(panel)?"planner":panel});
    }

    [HttpPost("GuardarCoberturaV7")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarCoberturaV7(ProduccionPersonalV7CoberturaPostVm vm)
    {
        if(!UsuarioEnSesion())return RedirectToAction("Login","Login"); var semana=InicioSemanaV2(vm.SemanaInicio);
        await using var cn=new SqlConnection(ConnectionString);await cn.OpenAsync();await using var tx=(SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
        try{var turnos=await CargarTurnosV2Async(cn,tx);foreach(var c in vm.Coberturas??new()){if(!turnos.Any(x=>x.TurnoID==c.TurnoID))continue;await ValidarPersonaApoyoV2Async(c.TecnicoProduccionID,"TECNICO",cn,tx);await ValidarPersonaApoyoV2Async(c.SmedID,"SMED_O_TECNICO",cn,tx);await ValidarPersonaApoyoV2Async(c.AuxiliarID,"AUXILIAR",cn,tx);await UpsertCoberturaV2Async(semana,c,UsuarioID(),cn,tx);}await tx.CommitAsync();TempData["Success"]="Cobertura semanal por turno guardada.";}catch(Exception ex){try{await tx.RollbackAsync();}catch{}TempData["Error"]="No fue posible guardar cobertura: "+ex.Message;}
        var panelCobertura=Request.Form["Panel"].ToString();
        return RedirectToAction(nameof(Index),new{vista=vm.Vista,fechaDesde=(vm.FechaDesde??semana).ToString("yyyy-MM-dd"),fechaHasta=vm.FechaHasta?.ToString("yyyy-MM-dd"),panel=string.IsNullOrWhiteSpace(panelCobertura)?"support":panelCobertura});
    }

    [HttpGet("HistorialV7")]
    public async Task<IActionResult> HistorialV7(int programaProduccionId)
    {
        if(!UsuarioEnSesion())return Unauthorized();await using var cn=new SqlConnection(ConnectionString);await cn.OpenAsync();const string sql=@"SELECT TOP(100) h.PersonalHistorialID,h.FechaTrabajo,h.TurnoNombre,h.Inicio,h.Fin,h.Rol,h.PersonaAnteriorID,LTRIM(RTRIM(CONCAT(ISNULL(pa.Nombre,N''),N' ',ISNULL(pa.ApellidoPaterno,N''),N' ',ISNULL(pa.ApellidoMaterno,N'')))) Anterior,h.PersonaNuevaID,LTRIM(RTRIM(CONCAT(ISNULL(pn.Nombre,N''),N' ',ISNULL(pn.ApellidoPaterno,N''),N' ',ISNULL(pn.ApellidoMaterno,N'')))) Nuevo,h.Motivo,h.Justificacion,h.Origen,h.ProduccionActiva,h.UsuarioID,h.FechaMovimiento FROM dbo.Produccion_PersonalAsignacionHistorial h LEFT JOIN dbo.Persona pa ON pa.PersonaID=h.PersonaAnteriorID LEFT JOIN dbo.Persona pn ON pn.PersonaID=h.PersonaNuevaID WHERE h.ProgramaProduccionID=@Programa ORDER BY h.FechaMovimiento DESC,h.PersonalHistorialID DESC;";await using var cmd=new SqlCommand(sql,cn);cmd.Parameters.Add("@Programa",SqlDbType.Int).Value=programaProduccionId;var l=new List<object>();await using var rd=await cmd.ExecuteReaderAsync();while(await rd.ReadAsync())l.Add(new{historialID=Convert.ToInt64(rd["PersonalHistorialID"]),fechaMovimiento=Convert.ToDateTime(rd["FechaMovimiento"]),turno=rd["TurnoNombre"]?.ToString(),inicio=Convert.ToDateTime(rd["Inicio"]),fin=Convert.ToDateTime(rd["Fin"]),anterior=rd["Anterior"]?.ToString()?.Trim(),nuevo=rd["Nuevo"]?.ToString()?.Trim(),motivo=rd["Motivo"]?.ToString(),justificacion=rd["Justificacion"]?.ToString(),produccionActiva=Convert.ToBoolean(rd["ProduccionActiva"]),usuarioID=Convert.ToInt32(rd["UsuarioID"])});return Json(new{ok=true,historial=l});
    }
}
