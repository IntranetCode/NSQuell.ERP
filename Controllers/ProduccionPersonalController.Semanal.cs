using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Globalization;

namespace ERP.NSQuell.Controllers;

public sealed partial class ProduccionPersonalController
{
    private sealed class EscalaSemanaV2
    {
        public int EscalaID { get; set; }
        public string Folio { get; set; } = string.Empty;
        public string Estado { get; set; } = string.Empty;
        public DateTime FechaInicio { get; set; }
        public DateTime FechaFin { get; set; }
    }

    private sealed class TurnoV2
    {
        public int TurnoID { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string TipoTurno { get; set; } = string.Empty;
        public string Color { get; set; } = "#64748B";
        public TimeSpan? Inicio { get; set; }
        public TimeSpan? Fin { get; set; }
        public bool Cruza { get; set; }
        public int Orden { get; set; }
    }

    private sealed class CoberturaV2
    {
        public int TurnoID { get; set; }
        public int? TecnicoID { get; set; }
        public int? SmedID { get; set; }
        public int? AuxiliarID { get; set; }
        public string Fuente { get; set; } = "MANUAL";
    }

    private sealed class ProgramaV2
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
        public int? OperadorID { get; set; }
        public string OperadorNombre { get; set; } = string.Empty;
        public bool ExcepcionActiva { get; set; }
    }

    private sealed class OperadorOficialV2
    {
        public int PersonaID { get; set; }
        public string NumeroControl { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
    }

    private sealed class EscalaOperadorV2
    {
        public int PersonaID { get; set; }
        public int? MaquinaID { get; set; }
        public DateTime FechaInicio { get; set; }
        public DateTime FechaFin { get; set; }
        public string TurnoNombre { get; set; } = string.Empty;
        public TimeSpan? HoraInicio { get; set; }
        public TimeSpan? HoraFin { get; set; }
        public bool Cruza { get; set; }
        public bool Flexible { get; set; }
    }

    private async Task<IActionResult> IndexSemanalCoreAsync(DateTime? referencia)
    {
        if (!UsuarioEnSesion())
            return RedirectToAction("Login", "Login");

        var semana = InicioSemanaV2(referencia ?? DateTime.Today);
        var sugerir = string.Equals(
            Request.Query["sugerir"].ToString(),
            "1",
            StringComparison.OrdinalIgnoreCase);

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        var vm = new ProduccionPersonalSemanalIndexVm
        {
            SemanaInicio = semana,
            Configurado = await ConfiguradoV2Async(cn, null),
            SugerenciasAplicadas = sugerir
        };

        var escala = await CargarEscalaSemanaV2Async(semana, cn, null);
        if (escala != null)
        {
            vm.EscalaID = escala.EscalaID;
            vm.EscalaFolio = escala.Folio;
            vm.EscalaEstado = escala.Estado;
            vm.EscalaFechaInicio = escala.FechaInicio;
            vm.EscalaFechaFin = escala.FechaFin;
        }

        var turnos = await CargarTurnosV2Async(cn, null);
        var guardadas = vm.Configurado
            ? await CargarCoberturasV2Async(semana, cn, null)
            : new List<CoberturaV2>();

        vm.Tecnicos = await CargarPersonasApoyoV2Async("TECNICO", cn, null);
        var smed = await CargarPersonasApoyoV2Async("SMED", cn, null);
        vm.SmedYTecnicos = UnirPersonasV2(smed, vm.Tecnicos, "SMED / TECNICO");
        vm.Auxiliares = await CargarPersonasApoyoV2Async("AUXILIAR", cn, null);

        foreach (var turno in turnos)
        {
            var guardada = guardadas.FirstOrDefault(x => x.TurnoID == turno.TurnoID);

            int? tecnico = guardada?.TecnicoID;
            int? smedId = guardada?.SmedID;
            int? auxiliar = guardada?.AuxiliarID;
            var fuente = guardada?.Fuente ?? "SIN_CONFIGURAR";

            if (guardada == null && escala != null)
            {
                tecnico = await SugerirApoyoEscalaV2Async(
                    escala.EscalaID, turno.TurnoID, "TECNICO", cn, null);
                smedId = await SugerirApoyoEscalaV2Async(
                    escala.EscalaID, turno.TurnoID, "SMED", cn, null);
                auxiliar = await SugerirApoyoEscalaV2Async(
                    escala.EscalaID, turno.TurnoID, "AUXILIAR", cn, null);

                if (tecnico.HasValue || smedId.HasValue || auxiliar.HasValue)
                    fuente = "ESCALA_RRHH";
            }

            vm.TurnosApoyo.Add(new ProduccionPersonalTurnoApoyoVm
            {
                TurnoID = turno.TurnoID,
                Nombre = turno.Nombre,
                TipoTurno = turno.TipoTurno,
                Color = turno.Color,
                HoraInicio = turno.Inicio,
                HoraFin = turno.Fin,
                CruzaDiaSiguiente = turno.Cruza,
                Orden = turno.Orden,
                TecnicoProduccionID = tecnico,
                SmedID = smedId,
                AuxiliarID = auxiliar,
                Fuente = fuente
            });
        }

        var programas = await CargarProgramasV2Async(semana, cn, null);
        var oficiales = await CargarOperadoresOficialesV2Async(cn, null);
        var niveles = await CargarNivelesV2Async(cn, null);
        var escalaOps = escala == null
            ? new List<EscalaOperadorV2>()
            : await CargarEscalaOperadoresV2Async(escala.EscalaID, cn, null);

        var reservas = new Dictionary<int, List<ProgramaV2>>();
        var ultimoPorMaquina = new Dictionary<int, int>();

        foreach (var programa in programas.OrderBy(x => x.Inicio).ThenBy(x => x.MaquinaCodigo))
        {
            var tieneMatriz = programa.ParteID.HasValue &&
                niveles.Keys.Any(x => x.ParteID == programa.ParteID.Value);

            var candidatos = new List<ProduccionPersonalOperadorCandidatoVm>();

            foreach (var op in oficiales)
            {
                int? nivel = null;
                if (programa.ParteID.HasValue &&
                    niveles.TryGetValue((programa.ParteID.Value, op.PersonaID), out var n))
                {
                    nivel = n;
                }

                if (tieneMatriz && !nivel.HasValue)
                    continue;

                var asignacionesMomento = escalaOps
                    .Where(x => x.PersonaID == op.PersonaID &&
                                EscalaCubreMomentoV2(x, programa.Inicio))
                    .ToList();

                var enEscala = escala == null || asignacionesMomento.Count > 0;
                var mismaMaquina = asignacionesMomento.Any(x =>
                    x.MaquinaID.HasValue && x.MaquinaID.Value == programa.MaquinaID);

                candidatos.Add(new ProduccionPersonalOperadorCandidatoVm
                {
                    PersonaID = op.PersonaID,
                    NumeroControl = op.NumeroControl,
                    Nombre = op.Nombre,
                    Nivel = nivel,
                    EnEscala = enEscala,
                    MismaMaquinaEscala = mismaMaquina,
                    TurnoEscala = asignacionesMomento.FirstOrDefault()?.TurnoNombre ?? string.Empty
                });
            }

            candidatos = candidatos
                .OrderByDescending(x => x.Nivel ?? 0)
                .ThenByDescending(x => x.EnEscala)
                .ThenByDescending(x => x.MismaMaquinaEscala)
                .ThenBy(x => x.Nombre)
                .ToList();

            var seleccionado = programa.OperadorID;
            var fueSugerido = false;

            if (!seleccionado.HasValue && sugerir && !programa.ExcepcionActiva && tieneMatriz)
            {
                IEnumerable<ProduccionPersonalOperadorCandidatoVm> pool = candidatos;

                if (escala != null)
                    pool = pool.Where(x => x.EnEscala);

                pool = pool
                    .OrderByDescending(x => x.Nivel ?? 0)
                    .ThenByDescending(x =>
                        ultimoPorMaquina.TryGetValue(programa.MaquinaID, out var anterior) &&
                        anterior == x.PersonaID)
                    .ThenByDescending(x => x.MismaMaquinaEscala)
                    .ThenBy(x => x.Nombre);

                var elegido = pool.FirstOrDefault(x =>
                    !TieneCruceReservaV2(x.PersonaID, programa, reservas));

                if (elegido != null)
                {
                    seleccionado = elegido.PersonaID;
                    fueSugerido = true;
                }
            }

            if (seleccionado.HasValue)
            {
                AgregarReservaV2(
                    seleccionado.Value,
                    programa,
                    reservas);
                ultimoPorMaquina[programa.MaquinaID] = seleccionado.Value;
            }

            var candidatoActual = seleccionado.HasValue
                ? candidatos.FirstOrDefault(x => x.PersonaID == seleccionado.Value)
                : null;

            vm.Programas.Add(new ProduccionPersonalProgramaOperadorVm
            {
                ProgramaProduccionID = programa.ProgramaID,
                SolicitudProduccionID = programa.SolicitudID,
                OF = programa.OF,
                ParteID = programa.ParteID,
                NumeroParte = programa.NumeroParte,
                DescripcionParte = programa.Descripcion,
                MaquinaID = programa.MaquinaID,
                MaquinaCodigo = programa.MaquinaCodigo,
                MaquinaNombre = programa.MaquinaNombre,
                Inicio = programa.Inicio,
                Fin = programa.Fin,
                OperadorID = seleccionado,
                OperadorNombre = candidatoActual?.Nombre ?? programa.OperadorNombre,
                NivelOperador = candidatoActual?.Nivel,
                OperadorEnEscala = candidatoActual?.EnEscala ?? false,
                OperadorMismaMaquinaEscala = candidatoActual?.MismaMaquinaEscala ?? false,
                FueSugerido = fueSugerido,
                ExcepcionActiva = programa.ExcepcionActiva,
                TieneMatriz = tieneMatriz,
                TieneConflictoHorario = seleccionado.HasValue &&
                    TieneCruceActualV2(seleccionado.Value, programa, programas),
                Candidatos = candidatos
            });
        }

        return View("Index", vm);
    }

    [HttpPost("GuardarSemana")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarSemana(ProduccionPersonalSemanaGuardarVm vm)
    {
        if (!UsuarioEnSesion())
            return RedirectToAction("Login", "Login");

        var semana = InicioSemanaV2(vm.SemanaInicio);

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        if (!await ConfiguradoV2Async(cn, null))
        {
            TempData["Error"] =
                "Falta ejecutar el SQL V2 de Programación de Personal.";
            return VolverSemanaV2(semana);
        }

        await using var tx =
            (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);

        try
        {
            var turnos = await CargarTurnosV2Async(cn, tx);
            var programas = await CargarProgramasV2Async(semana, cn, tx);
            var programasMap = programas.ToDictionary(x => x.ProgramaID);
            var oficiales = await CargarOperadoresOficialesV2Async(cn, tx);
            var oficialesSet = oficiales.Select(x => x.PersonaID).ToHashSet();
            var niveles = await CargarNivelesV2Async(cn, tx);

            ValidarCoberturasTraslapadasV2(vm.Coberturas ?? new(), turnos);

            foreach (var cobertura in vm.Coberturas ?? new())
            {
                if (!turnos.Any(x => x.TurnoID == cobertura.TurnoID))
                    continue;

                await ValidarPersonaApoyoV2Async(
                    cobertura.TecnicoProduccionID, "TECNICO", cn, tx);
                await ValidarPersonaApoyoV2Async(
                    cobertura.SmedID, "SMED_O_TECNICO", cn, tx);
                await ValidarPersonaApoyoV2Async(
                    cobertura.AuxiliarID, "AUXILIAR", cn, tx);

                await UpsertCoberturaV2Async(
                    semana, cobertura, UsuarioID(), cn, tx);
            }

            var asignacionesPost = (vm.Operadores ?? new())
                .Where(x => programasMap.ContainsKey(x.ProgramaProduccionID))
                .GroupBy(x => x.ProgramaProduccionID)
                .Select(x => x.Last())
                .ToList();

            ValidarCrucesPostV2(asignacionesPost, programasMap);

            var omitidasExcepcion = 0;

            foreach (var item in asignacionesPost)
            {
                var programa = programasMap[item.ProgramaProduccionID];

                if (programa.ExcepcionActiva)
                {
                    omitidasExcepcion++;
                    continue;
                }

                if (item.OperadorID.HasValue)
                {
                    if (!oficialesSet.Contains(item.OperadorID.Value))
                    {
                        throw new InvalidOperationException(
                            $"El operador seleccionado para {programa.OF} ya no pertenece a la matriz operativa activa.");
                    }

                    var parteTieneMatriz = programa.ParteID.HasValue &&
                        niveles.Keys.Any(x => x.ParteID == programa.ParteID.Value);

                    if (parteTieneMatriz &&
                        !niveles.ContainsKey((programa.ParteID!.Value, item.OperadorID.Value)))
                    {
                        throw new InvalidOperationException(
                            $"El operador seleccionado para {programa.OF} no tiene nivel N1-N4 para {programa.NumeroParte}.");
                    }
                }

                await GuardarOperadorProgramaV2Async(
                    programa.ProgramaID,
                    item.OperadorID,
                    UsuarioID(),
                    cn,
                    tx);
            }

            await tx.CommitAsync();

            TempData["Success"] =
                "Programación guardada por OF/pieza." +
                (omitidasExcepcion > 0
                    ? $" {omitidasExcepcion} OF con excepción de Calendario se conservaron sin cambios."
                    : string.Empty);

            return VolverSemanaV2(semana);
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(); } catch { }

            TempData["Error"] =
                "No fue posible guardar la programación: " + ex.Message;
            return VolverSemanaV2(semana);
        }
    }

    private IActionResult VolverSemanaV2(DateTime semana) =>
        RedirectToAction(nameof(Index), new
        {
            fechaDesde = semana.ToString("yyyy-MM-dd")
        });

    private static DateTime InicioSemanaV2(DateTime fecha)
    {
        var isoYear = ISOWeek.GetYear(fecha);
        var isoWeek = ISOWeek.GetWeekOfYear(fecha);
        return ISOWeek.ToDateTime(isoYear, isoWeek, DayOfWeek.Monday).Date;
    }

    private static async Task<bool> ConfiguradoV2Async(
        SqlConnection cn,
        SqlTransaction? tx)
    {
        const string sql = @"
SELECT CONVERT(bit,CASE WHEN
       OBJECT_ID(N'dbo.Produccion_PersonalTurnoCobertura',N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.Produccion_OperadorExcepciones',N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.RRHH_PolivalenciaCompetencias',N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.vw_RRHH_PolivalenciaOperadoresParte',N'V') IS NOT NULL
   AND OBJECT_ID(N'dbo.Planeacion_ProgramaOperadores',N'U') IS NOT NULL
THEN 1 ELSE 0 END);";

        await using var cmd = tx == null
            ? new SqlCommand(sql, cn)
            : new SqlCommand(sql, cn, tx);

        return Convert.ToBoolean(await cmd.ExecuteScalarAsync() ?? false);
    }

    private static async Task<EscalaSemanaV2?> CargarEscalaSemanaV2Async(
        DateTime semana,
        SqlConnection cn,
        SqlTransaction? tx)
    {
        const string sql = @"
SELECT TOP (1)
    EscalaID,Folio,Estado,FechaInicio,FechaFin
FROM dbo.RRHH_EscalasPersonal
WHERE Activo=1
  AND Estado IN(N'Publicada',N'Borrador')
  AND FechaInicio<@Hasta
  AND DATEADD(DAY,1,FechaFin)>@Desde
ORDER BY
    CASE WHEN Estado=N'Publicada' THEN 0 ELSE 1 END,
    ISNULL(FechaPublicacion,FechaRegistro) DESC,
    EscalaID DESC;";

        await using var cmd = tx == null
            ? new SqlCommand(sql, cn)
            : new SqlCommand(sql, cn, tx);

        cmd.Parameters.Add("@Desde", SqlDbType.Date).Value = semana;
        cmd.Parameters.Add("@Hasta", SqlDbType.Date).Value = semana.AddDays(7);

        await using var rd = await cmd.ExecuteReaderAsync();
        if (!await rd.ReadAsync())
            return null;

        return new EscalaSemanaV2
        {
            EscalaID = Convert.ToInt32(rd["EscalaID"]),
            Folio = rd["Folio"]?.ToString()?.Trim() ?? string.Empty,
            Estado = rd["Estado"]?.ToString()?.Trim() ?? string.Empty,
            FechaInicio = Convert.ToDateTime(rd["FechaInicio"]),
            FechaFin = Convert.ToDateTime(rd["FechaFin"])
        };
    }

    private static async Task<List<TurnoV2>> CargarTurnosV2Async(
        SqlConnection cn,
        SqlTransaction? tx)
    {
        const string sql = @"
SELECT
    TurnoID,Nombre,TipoTurno,Color,HoraInicio,HoraFin,
    CruzaDiaSiguiente,Orden
FROM dbo.RRHH_Turnos
WHERE Activo=1
  AND EsFlexible=0
  AND HoraInicio IS NOT NULL
  AND HoraFin IS NOT NULL
ORDER BY Orden,HoraInicio,TurnoID;";

        var lista = new List<TurnoV2>();

        await using var cmd = tx == null
            ? new SqlCommand(sql, cn)
            : new SqlCommand(sql, cn, tx);

        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            lista.Add(new TurnoV2
            {
                TurnoID = Convert.ToInt32(rd["TurnoID"]),
                Nombre = rd["Nombre"]?.ToString()?.Trim() ?? string.Empty,
                TipoTurno = rd["TipoTurno"]?.ToString()?.Trim() ?? string.Empty,
                Color = rd["Color"]?.ToString()?.Trim() ?? "#64748B",
                Inicio = rd["HoraInicio"] == DBNull.Value
                    ? null : (TimeSpan)rd["HoraInicio"],
                Fin = rd["HoraFin"] == DBNull.Value
                    ? null : (TimeSpan)rd["HoraFin"],
                Cruza = Convert.ToBoolean(rd["CruzaDiaSiguiente"]),
                Orden = Convert.ToInt32(rd["Orden"])
            });
        }

        return lista;
    }

    private static async Task<List<CoberturaV2>> CargarCoberturasV2Async(
        DateTime semana,
        SqlConnection cn,
        SqlTransaction? tx)
    {
        const string sql = @"
SELECT TurnoID,TecnicoProduccionID,SmedID,AuxiliarID,
       ISNULL(Fuente,N'MANUAL') AS Fuente
FROM dbo.Produccion_PersonalTurnoCobertura
WHERE SemanaInicio=@Semana
  AND Activo=1;";

        var lista = new List<CoberturaV2>();

        await using var cmd = tx == null
            ? new SqlCommand(sql, cn)
            : new SqlCommand(sql, cn, tx);

        cmd.Parameters.Add("@Semana", SqlDbType.Date).Value = semana;

        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            lista.Add(new CoberturaV2
            {
                TurnoID = Convert.ToInt32(rd["TurnoID"]),
                TecnicoID = rd["TecnicoProduccionID"] == DBNull.Value
                    ? null : Convert.ToInt32(rd["TecnicoProduccionID"]),
                SmedID = rd["SmedID"] == DBNull.Value
                    ? null : Convert.ToInt32(rd["SmedID"]),
                AuxiliarID = rd["AuxiliarID"] == DBNull.Value
                    ? null : Convert.ToInt32(rd["AuxiliarID"]),
                Fuente = rd["Fuente"]?.ToString()?.Trim() ?? "MANUAL"
            });
        }

        return lista;
    }

    private static string FiltroApoyoV2(string tipo, string personaAlias, string funcionAlias)
    {
        return tipo switch
        {
            "TECNICO" => $@"(
                UPPER(ISNULL({personaAlias}.Puesto,N'')) COLLATE Modern_Spanish_CI_AI LIKE N'%TECNIC%'
                OR UPPER(ISNULL({funcionAlias}.Nombre,N'')) COLLATE Modern_Spanish_CI_AI LIKE N'%TECNIC%'
            )",

            "SMED" => $@"(
                UPPER(ISNULL({personaAlias}.Puesto,N'')) COLLATE Modern_Spanish_CI_AI LIKE N'%SMED%'
                OR UPPER(ISNULL({funcionAlias}.Nombre,N'')) COLLATE Modern_Spanish_CI_AI LIKE N'%SMED%'
            )",

            "AUXILIAR" => $@"(
                UPPER(ISNULL({personaAlias}.Puesto,N'')) COLLATE Modern_Spanish_CI_AI LIKE N'%AUXILIAR%PRODU%'
                OR UPPER(ISNULL({funcionAlias}.Nombre,N'')) COLLATE Modern_Spanish_CI_AI LIKE N'%AUXILIAR%'
            )",

            _ => "1=0"
        };
    }

    private static async Task<List<ProduccionPersonalPersonaOpcionVm>>
        CargarPersonasApoyoV2Async(
            string tipo,
            SqlConnection cn,
            SqlTransaction? tx)
    {
        var filtro = FiltroApoyoV2(tipo, "p", "f");

        var sql = $@"
SELECT DISTINCT
    p.PersonaID,
    ISNULL(p.NumeroControl,N'') AS NumeroControl,
    LTRIM(RTRIM(CONCAT(
        ISNULL(p.Nombre,N''),N' ',
        ISNULL(p.ApellidoPaterno,N''),N' ',
        ISNULL(p.ApellidoMaterno,N'')))) AS Nombre,
    ISNULL(p.Puesto,N'') AS Puesto
FROM dbo.Persona p
LEFT JOIN dbo.RRHH_EscalaAsignaciones a
  ON a.PersonalID=p.PersonaID AND a.Activo=1
LEFT JOIN dbo.RRHH_FuncionesPersonal f
  ON f.FuncionID=a.FuncionID AND f.Activo=1
WHERE ISNULL(p.EsColaboradorActivo,1)=1
  AND {filtro}
ORDER BY Nombre;";

        var lista = new List<ProduccionPersonalPersonaOpcionVm>();

        await using var cmd = tx == null
            ? new SqlCommand(sql, cn)
            : new SqlCommand(sql, cn, tx);

        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            lista.Add(new ProduccionPersonalPersonaOpcionVm
            {
                PersonaID = Convert.ToInt32(rd["PersonaID"]),
                NumeroControl = rd["NumeroControl"]?.ToString()?.Trim() ?? string.Empty,
                Nombre = rd["Nombre"]?.ToString()?.Trim() ?? string.Empty,
                Puesto = rd["Puesto"]?.ToString()?.Trim() ?? string.Empty,
                TipoOpcion = tipo
            });
        }

        return lista;
    }

    private static List<ProduccionPersonalPersonaOpcionVm> UnirPersonasV2(
        IEnumerable<ProduccionPersonalPersonaOpcionVm> primera,
        IEnumerable<ProduccionPersonalPersonaOpcionVm> segunda,
        string tipo)
    {
        return primera.Concat(segunda)
            .GroupBy(x => x.PersonaID)
            .Select(g =>
            {
                var x = g.First();
                return new ProduccionPersonalPersonaOpcionVm
                {
                    PersonaID = x.PersonaID,
                    NumeroControl = x.NumeroControl,
                    Nombre = x.Nombre,
                    Puesto = x.Puesto,
                    TipoOpcion = tipo
                };
            })
            .OrderBy(x => x.Nombre)
            .ToList();
    }

    private static async Task<int?> SugerirApoyoEscalaV2Async(
        int escalaId,
        int turnoId,
        string tipo,
        SqlConnection cn,
        SqlTransaction? tx)
    {
        var condicion = tipo switch
        {
            "TECNICO" => @"UPPER(ISNULL(f.Nombre,N'')) COLLATE Modern_Spanish_CI_AI LIKE N'%TECNIC%'",
            "AUXILIAR" => @"UPPER(ISNULL(f.Nombre,N'')) COLLATE Modern_Spanish_CI_AI LIKE N'%AUXILIAR%'",
            "SMED" => @"(
                UPPER(ISNULL(f.Nombre,N'')) COLLATE Modern_Spanish_CI_AI LIKE N'%SMED%'
                OR UPPER(ISNULL(f.Nombre,N'')) COLLATE Modern_Spanish_CI_AI LIKE N'%TECNIC%'
            )",
            _ => "1=0"
        };

        var orden = tipo == "SMED"
            ? @"CASE WHEN UPPER(ISNULL(f.Nombre,N'')) COLLATE Modern_Spanish_CI_AI LIKE N'%SMED%' THEN 0 ELSE 1 END,"
            : string.Empty;

        var sql = $@"
SELECT TOP (1) a.PersonalID
FROM dbo.RRHH_EscalaAsignaciones a
INNER JOIN dbo.RRHH_EscalaTurnos et
  ON et.EscalaTurnoID=a.EscalaTurnoID
 AND et.EscalaID=a.EscalaID
 AND et.Activo=1
INNER JOIN dbo.RRHH_FuncionesPersonal f
  ON f.FuncionID=a.FuncionID
 AND f.Activo=1
INNER JOIN dbo.Persona p
  ON p.PersonaID=a.PersonalID
 AND ISNULL(p.EsColaboradorActivo,1)=1
WHERE a.Activo=1
  AND a.EscalaID=@EscalaID
  AND et.TurnoOrigenID=@TurnoID
  AND {condicion}
ORDER BY {orden} a.AsignacionID DESC;";

        await using var cmd = tx == null
            ? new SqlCommand(sql, cn)
            : new SqlCommand(sql, cn, tx);

        cmd.Parameters.Add("@EscalaID", SqlDbType.Int).Value = escalaId;
        cmd.Parameters.Add("@TurnoID", SqlDbType.Int).Value = turnoId;

        var value = await cmd.ExecuteScalarAsync();
        return value == null || value == DBNull.Value
            ? null
            : Convert.ToInt32(value);
    }

    private static async Task<List<ProgramaV2>> CargarProgramasV2Async(
        DateTime semana,
        SqlConnection cn,
        SqlTransaction? tx)
    {
        const string sql = @"
SELECT
    pp.ProgramaProduccionID,
    pp.SolicitudProduccionID,
    COALESCE(NULLIF(LTRIM(RTRIM(s.NumeroOFRecibida)),N''),
             NULLIF(LTRIM(RTRIM(s.FolioSolicitud)),N''),
             CONCAT(N'PROG-',pp.ProgramaProduccionID)) AS [OF],
    pp.ParteID,
    ISNULL(pp.NumeroParte,N'') AS NumeroParte,
    ISNULL(pp.DesignacionDescripcionSAP,N'') AS Descripcion,
    pp.MaquinaID,
    ISNULL(pp.MaquinaCodigo,N'') AS MaquinaCodigo,
    ISNULL(pp.MaquinaNombre,N'') AS MaquinaNombre,
    pp.FechaInicioProgramada,
    ISNULL(
        pp.FechaFinProgramada,
        DATEADD(MINUTE,
            CONVERT(INT,CEILING(ISNULL(pp.HorasProgramadas,1)*60)),
            pp.FechaInicioProgramada)
    ) AS FechaFinProgramada,
    po.PersonaID AS OperadorID,
    LTRIM(RTRIM(CONCAT(
        ISNULL(p.Nombre,N''),N' ',
        ISNULL(p.ApellidoPaterno,N''),N' ',
        ISNULL(p.ApellidoMaterno,N'')))) AS OperadorNombre,
    CONVERT(bit,CASE WHEN ex.ExcepcionOperadorID IS NULL THEN 0 ELSE 1 END)
        AS ExcepcionActiva
FROM dbo.Planeacion_ProgramaProduccion pp
LEFT JOIN dbo.SolicitudesProduccion s
  ON s.SolicitudProduccionID=pp.SolicitudProduccionID
OUTER APPLY
(
    SELECT TOP (1) x.PersonaID
    FROM dbo.Planeacion_ProgramaOperadores x
    WHERE x.ProgramaProduccionID=pp.ProgramaProduccionID
      AND x.Activo=1
      AND UPPER(ISNULL(x.RolOperador,N''))=N'PRINCIPAL'
    ORDER BY x.ProgramaOperadorID DESC
) po
LEFT JOIN dbo.Persona p ON p.PersonaID=po.PersonaID
OUTER APPLY
(
    SELECT TOP (1) x.ExcepcionOperadorID
    FROM dbo.Produccion_OperadorExcepciones x
    WHERE x.Activo=1
      AND
      (
          x.ProgramaProduccionID=pp.ProgramaProduccionID
          OR
          (
              x.Alcance=N'RESTO_TURNO'
              AND x.MaquinaID=pp.MaquinaID
              AND pp.FechaInicioProgramada>=x.InicioVigencia
              AND pp.FechaInicioProgramada<x.FinVigencia
          )
      )
    ORDER BY
        CASE WHEN x.ProgramaProduccionID=pp.ProgramaProduccionID THEN 0 ELSE 1 END,
        x.ExcepcionOperadorID DESC
) ex
WHERE pp.Activo=1
  AND pp.MaquinaID IS NOT NULL
  AND pp.FechaInicioProgramada IS NOT NULL
  AND pp.FechaInicioProgramada>=@Desde
  AND pp.FechaInicioProgramada<@Hasta
  AND ISNULL(pp.EstatusID,1) NOT IN(6,9,99)
ORDER BY pp.FechaInicioProgramada,pp.MaquinaCodigo,pp.ProgramaProduccionID;";

        var lista = new List<ProgramaV2>();

        await using var cmd = tx == null
            ? new SqlCommand(sql, cn)
            : new SqlCommand(sql, cn, tx);

        cmd.Parameters.Add("@Desde", SqlDbType.DateTime2).Value = semana;
        cmd.Parameters.Add("@Hasta", SqlDbType.DateTime2).Value = semana.AddDays(7);

        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            lista.Add(new ProgramaV2
            {
                ProgramaID = Convert.ToInt32(rd["ProgramaProduccionID"]),
                SolicitudID = rd["SolicitudProduccionID"] == DBNull.Value
                    ? null : Convert.ToInt32(rd["SolicitudProduccionID"]),
                OF = rd["OF"]?.ToString()?.Trim() ?? string.Empty,
                ParteID = rd["ParteID"] == DBNull.Value
                    ? null : Convert.ToInt32(rd["ParteID"]),
                NumeroParte = rd["NumeroParte"]?.ToString()?.Trim() ?? string.Empty,
                Descripcion = rd["Descripcion"]?.ToString()?.Trim() ?? string.Empty,
                MaquinaID = Convert.ToInt32(rd["MaquinaID"]),
                MaquinaCodigo = rd["MaquinaCodigo"]?.ToString()?.Trim() ?? string.Empty,
                MaquinaNombre = rd["MaquinaNombre"]?.ToString()?.Trim() ?? string.Empty,
                Inicio = Convert.ToDateTime(rd["FechaInicioProgramada"]),
                Fin = Convert.ToDateTime(rd["FechaFinProgramada"]),
                OperadorID = rd["OperadorID"] == DBNull.Value
                    ? null : Convert.ToInt32(rd["OperadorID"]),
                OperadorNombre = rd["OperadorNombre"]?.ToString()?.Trim() ?? string.Empty,
                ExcepcionActiva = Convert.ToBoolean(rd["ExcepcionActiva"])
            });
        }

        return lista;
    }

    private static async Task<List<OperadorOficialV2>> CargarOperadoresOficialesV2Async(
        SqlConnection cn,
        SqlTransaction? tx)
    {
        const string sql = @"
SELECT DISTINCT
    p.PersonaID,
    ISNULL(p.NumeroControl,N'') AS NumeroControl,
    LTRIM(RTRIM(CONCAT(
        ISNULL(p.Nombre,N''),N' ',
        ISNULL(p.ApellidoPaterno,N''),N' ',
        ISNULL(p.ApellidoMaterno,N'')))) AS Nombre
FROM dbo.RRHH_PolivalenciaCompetencias c
INNER JOIN dbo.Persona p
  ON p.PersonaID=c.PersonalID
 AND ISNULL(p.EsColaboradorActivo,1)=1
WHERE c.Activo=1
  AND UPPER(LTRIM(RTRIM(ISNULL(c.PuestoMatriz,N''))))=N'OPERADOR'
ORDER BY Nombre;";

        var lista = new List<OperadorOficialV2>();

        await using var cmd = tx == null
            ? new SqlCommand(sql, cn)
            : new SqlCommand(sql, cn, tx);

        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            lista.Add(new OperadorOficialV2
            {
                PersonaID = Convert.ToInt32(rd["PersonaID"]),
                NumeroControl = rd["NumeroControl"]?.ToString()?.Trim() ?? string.Empty,
                Nombre = rd["Nombre"]?.ToString()?.Trim() ?? string.Empty
            });
        }

        return lista;
    }

    private static async Task<Dictionary<(int ParteID,int PersonaID),int>>
        CargarNivelesV2Async(
            SqlConnection cn,
            SqlTransaction? tx)
    {
        const string sql = @"
SELECT v.ParteID,v.PersonalID,MAX(CONVERT(INT,v.Nivel)) AS Nivel
FROM dbo.vw_RRHH_PolivalenciaOperadoresParte v
INNER JOIN dbo.Persona p
  ON p.PersonaID=v.PersonalID
 AND ISNULL(p.EsColaboradorActivo,1)=1
WHERE v.Nivel BETWEEN 1 AND 4
  AND UPPER(LTRIM(RTRIM(ISNULL(v.PuestoMatriz,N''))))=N'OPERADOR'
GROUP BY v.ParteID,v.PersonalID;";

        var d = new Dictionary<(int,int),int>();

        await using var cmd = tx == null
            ? new SqlCommand(sql, cn)
            : new SqlCommand(sql, cn, tx);

        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            d[(Convert.ToInt32(rd["ParteID"]),Convert.ToInt32(rd["PersonalID"]))] =
                Convert.ToInt32(rd["Nivel"]);
        }

        return d;
    }

    private static async Task<List<EscalaOperadorV2>> CargarEscalaOperadoresV2Async(
        int escalaId,
        SqlConnection cn,
        SqlTransaction? tx)
    {
        const string sql = @"
SELECT
    a.PersonalID,
    a.MaquinaID,
    a.FechaInicio,
    a.FechaFin,
    et.Nombre AS TurnoNombre,
    et.HoraInicio,
    et.HoraFin,
    et.CruzaDiaSiguiente,
    et.EsFlexible
FROM dbo.RRHH_EscalaAsignaciones a
INNER JOIN dbo.RRHH_FuncionesPersonal f
  ON f.FuncionID=a.FuncionID
 AND f.Activo=1
INNER JOIN dbo.RRHH_EscalaTurnos et
  ON et.EscalaTurnoID=a.EscalaTurnoID
 AND et.EscalaID=a.EscalaID
 AND et.Activo=1
INNER JOIN dbo.Persona p
  ON p.PersonaID=a.PersonalID
 AND ISNULL(p.EsColaboradorActivo,1)=1
WHERE a.Activo=1
  AND a.EscalaID=@EscalaID
  AND UPPER(LTRIM(RTRIM(f.Nombre)))=N'OPERADOR';";

        var lista = new List<EscalaOperadorV2>();

        await using var cmd = tx == null
            ? new SqlCommand(sql, cn)
            : new SqlCommand(sql, cn, tx);

        cmd.Parameters.Add("@EscalaID", SqlDbType.Int).Value = escalaId;

        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            lista.Add(new EscalaOperadorV2
            {
                PersonaID = Convert.ToInt32(rd["PersonalID"]),
                MaquinaID = rd["MaquinaID"] == DBNull.Value
                    ? null : Convert.ToInt32(rd["MaquinaID"]),
                FechaInicio = Convert.ToDateTime(rd["FechaInicio"]),
                FechaFin = Convert.ToDateTime(rd["FechaFin"]),
                TurnoNombre = rd["TurnoNombre"]?.ToString()?.Trim() ?? string.Empty,
                HoraInicio = rd["HoraInicio"] == DBNull.Value
                    ? null : (TimeSpan)rd["HoraInicio"],
                HoraFin = rd["HoraFin"] == DBNull.Value
                    ? null : (TimeSpan)rd["HoraFin"],
                Cruza = Convert.ToBoolean(rd["CruzaDiaSiguiente"]),
                Flexible = Convert.ToBoolean(rd["EsFlexible"])
            });
        }

        return lista;
    }

    private static bool EscalaCubreMomentoV2(EscalaOperadorV2 x, DateTime momento)
    {
        var fechaTrabajo = momento.Date;

        if (x.Cruza && x.HoraFin.HasValue && momento.TimeOfDay < x.HoraFin.Value)
            fechaTrabajo = fechaTrabajo.AddDays(-1);

        if (fechaTrabajo < x.FechaInicio.Date || fechaTrabajo > x.FechaFin.Date)
            return false;

        if (x.Flexible || !x.HoraInicio.HasValue || !x.HoraFin.HasValue)
            return true;

        var hora = momento.TimeOfDay;
        return x.Cruza
            ? hora >= x.HoraInicio.Value || hora < x.HoraFin.Value
            : hora >= x.HoraInicio.Value && hora < x.HoraFin.Value;
    }

    private static bool SeTraslapanV2(DateTime aInicio, DateTime aFin, DateTime bInicio, DateTime bFin) =>
        aInicio < bFin && bInicio < aFin;

    private static bool MismoBloqueMaquinaV2(ProgramaV2 a, ProgramaV2 b) =>
        a.MaquinaID == b.MaquinaID &&
        a.Inicio == b.Inicio &&
        a.Fin == b.Fin;

    private static bool TieneCruceReservaV2(
        int personaId,
        ProgramaV2 programa,
        Dictionary<int,List<ProgramaV2>> reservas)
    {
        if (!reservas.TryGetValue(personaId, out var ocupados))
            return false;

        return ocupados.Any(x =>
            SeTraslapanV2(x.Inicio,x.Fin,programa.Inicio,programa.Fin) &&
            !MismoBloqueMaquinaV2(x,programa));
    }

    private static void AgregarReservaV2(
        int personaId,
        ProgramaV2 programa,
        Dictionary<int,List<ProgramaV2>> reservas)
    {
        if (!reservas.TryGetValue(personaId, out var lista))
        {
            lista = new List<ProgramaV2>();
            reservas[personaId] = lista;
        }
        lista.Add(programa);
    }

    private static bool TieneCruceActualV2(
        int personaId,
        ProgramaV2 programa,
        IReadOnlyCollection<ProgramaV2> programas)
    {
        return programas.Any(x =>
            x.ProgramaID != programa.ProgramaID &&
            x.OperadorID == personaId &&
            SeTraslapanV2(x.Inicio,x.Fin,programa.Inicio,programa.Fin) &&
            !MismoBloqueMaquinaV2(x,programa));
    }

    private static bool TurnosTraslapanV2(TurnoV2 a, TurnoV2 b)
    {
        if (!a.Inicio.HasValue || !a.Fin.HasValue ||
            !b.Inicio.HasValue || !b.Fin.HasValue)
            return false;

        static List<(int Inicio,int Fin)> Intervalos(TurnoV2 t)
        {
            var ini = (int)t.Inicio!.Value.TotalMinutes;
            var fin = (int)t.Fin!.Value.TotalMinutes;

            if (!t.Cruza && fin > ini)
                return new() { (ini,fin) };

            return new() { (ini,1440),(0,fin) };
        }

        var ia = Intervalos(a);
        var ib = Intervalos(b);

        return ia.Any(x => ib.Any(y => x.Inicio < y.Fin && y.Inicio < x.Fin));
    }

    private static void ValidarCoberturasTraslapadasV2(
        IReadOnlyCollection<ProduccionPersonalTurnoGuardarVm> coberturas,
        IReadOnlyCollection<TurnoV2> turnos)
    {
        var turnosMap = turnos.ToDictionary(x => x.TurnoID);
        var lista = coberturas
            .Where(x => turnosMap.ContainsKey(x.TurnoID))
            .ToList();

        void Revisar(string rol, Func<ProduccionPersonalTurnoGuardarVm,int?> selector)
        {
            var activos = lista.Where(x => selector(x).HasValue).ToList();

            for (var i=0;i<activos.Count;i++)
            for (var j=i+1;j<activos.Count;j++)
            {
                var a = turnosMap[activos[i].TurnoID];
                var b = turnosMap[activos[j].TurnoID];

                if (TurnosTraslapanV2(a,b))
                {
                    throw new InvalidOperationException(
                        $"{rol}: los turnos '{a.Nombre}' y '{b.Nombre}' se traslapan. " +
                        "Usa el esquema regular o 12x12 correspondiente, no ambos al mismo tiempo.");
                }
            }
        }

        Revisar("Técnico",x => x.TecnicoProduccionID);
        Revisar("SMED",x => x.SmedID);
        Revisar("Auxiliar",x => x.AuxiliarID);
    }

    private static void ValidarCrucesPostV2(
        IReadOnlyCollection<ProduccionPersonalOperadorProgramaGuardarVm> items,
        IReadOnlyDictionary<int,ProgramaV2> programas)
    {
        var seleccionados = items
            .Where(x => x.OperadorID.HasValue && programas.ContainsKey(x.ProgramaProduccionID))
            .Select(x => (Item:x,Programa:programas[x.ProgramaProduccionID]))
            .ToList();

        for (var i=0;i<seleccionados.Count;i++)
        for (var j=i+1;j<seleccionados.Count;j++)
        {
            if (seleccionados[i].Item.OperadorID != seleccionados[j].Item.OperadorID)
                continue;

            var a = seleccionados[i].Programa;
            var b = seleccionados[j].Programa;

            if (SeTraslapanV2(a.Inicio,a.Fin,b.Inicio,b.Fin) && !MismoBloqueMaquinaV2(a,b))
            {
                throw new InvalidOperationException(
                    $"El mismo operador quedó traslapado entre {a.OF} y {b.OF}. " +
                    "Corrige una de las dos asignaciones.");
            }
        }
    }

    private static async Task ValidarPersonaApoyoV2Async(
        int? personaId,
        string tipo,
        SqlConnection cn,
        SqlTransaction tx)
    {
        if (!personaId.HasValue || personaId.Value <= 0)
            return;

        string filtro;

        if (tipo == "SMED_O_TECNICO")
        {
            filtro = @"(
                UPPER(ISNULL(p.Puesto,N'')) COLLATE Modern_Spanish_CI_AI LIKE N'%SMED%'
                OR UPPER(ISNULL(p.Puesto,N'')) COLLATE Modern_Spanish_CI_AI LIKE N'%TECNIC%'
                OR UPPER(ISNULL(f.Nombre,N'')) COLLATE Modern_Spanish_CI_AI LIKE N'%SMED%'
                OR UPPER(ISNULL(f.Nombre,N'')) COLLATE Modern_Spanish_CI_AI LIKE N'%TECNIC%'
            )";
        }
        else
        {
            filtro = FiltroApoyoV2(tipo, "p", "f");
        }

        var sql = $@"
SELECT CONVERT(bit,CASE WHEN EXISTS
(
    SELECT 1
    FROM dbo.Persona p
    LEFT JOIN dbo.RRHH_EscalaAsignaciones a
      ON a.PersonalID=p.PersonaID AND a.Activo=1
    LEFT JOIN dbo.RRHH_FuncionesPersonal f
      ON f.FuncionID=a.FuncionID AND f.Activo=1
    WHERE p.PersonaID=@PersonaID
      AND ISNULL(p.EsColaboradorActivo,1)=1
      AND {filtro}
) THEN 1 ELSE 0 END);";

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@PersonaID", SqlDbType.Int).Value = personaId.Value;

        if (!Convert.ToBoolean(await cmd.ExecuteScalarAsync() ?? false))
        {
            throw new InvalidOperationException(
                $"La persona {personaId.Value} no corresponde al rol {tipo}.");
        }
    }

    private static async Task UpsertCoberturaV2Async(
        DateTime semana,
        ProduccionPersonalTurnoGuardarVm item,
        int usuarioId,
        SqlConnection cn,
        SqlTransaction tx)
    {
        const string sql = @"
DECLARE @ID INT=
(
    SELECT TOP (1) CoberturaTurnoID
    FROM dbo.Produccion_PersonalTurnoCobertura WITH (UPDLOCK,HOLDLOCK)
    WHERE SemanaInicio=@Semana
      AND TurnoID=@TurnoID
      AND Activo=1
    ORDER BY CoberturaTurnoID DESC
);

IF @ID IS NULL
BEGIN
    INSERT INTO dbo.Produccion_PersonalTurnoCobertura
    (
        SemanaInicio,TurnoID,TecnicoProduccionID,SmedID,AuxiliarID,
        Fuente,UsuarioCreacionID,FechaCreacion,Activo
    )
    VALUES
    (
        @Semana,@TurnoID,@Tecnico,@Smed,@Auxiliar,
        N'MANUAL',@UsuarioID,SYSDATETIME(),1
    );
END
ELSE
BEGIN
    UPDATE dbo.Produccion_PersonalTurnoCobertura
    SET TecnicoProduccionID=@Tecnico,
        SmedID=@Smed,
        AuxiliarID=@Auxiliar,
        Fuente=N'MANUAL',
        UsuarioModificacionID=@UsuarioID,
        FechaModificacion=SYSDATETIME()
    WHERE CoberturaTurnoID=@ID;
END;";

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@Semana", SqlDbType.Date).Value = semana;
        cmd.Parameters.Add("@TurnoID", SqlDbType.Int).Value = item.TurnoID;
        cmd.Parameters.Add("@Tecnico", SqlDbType.Int).Value =
            item.TecnicoProduccionID.HasValue
                ? item.TecnicoProduccionID.Value : DBNull.Value;
        cmd.Parameters.Add("@Smed", SqlDbType.Int).Value =
            item.SmedID.HasValue ? item.SmedID.Value : DBNull.Value;
        cmd.Parameters.Add("@Auxiliar", SqlDbType.Int).Value =
            item.AuxiliarID.HasValue ? item.AuxiliarID.Value : DBNull.Value;
        cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;

        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task GuardarOperadorProgramaV2Async(
        int programaId,
        int? operadorId,
        int usuarioId,
        SqlConnection cn,
        SqlTransaction tx)
    {
        const string actualSql = @"
SELECT TOP (1) ProgramaOperadorID,PersonaID
FROM dbo.Planeacion_ProgramaOperadores WITH (UPDLOCK,HOLDLOCK)
WHERE ProgramaProduccionID=@ProgramaID
  AND Activo=1
  AND UPPER(ISNULL(RolOperador,N''))=N'PRINCIPAL'
ORDER BY ProgramaOperadorID DESC;";

        int? registroId = null;
        int? personaActual = null;

        await using (var cmd = new SqlCommand(actualSql, cn, tx))
        {
            cmd.Parameters.Add("@ProgramaID", SqlDbType.Int).Value = programaId;
            await using var rd = await cmd.ExecuteReaderAsync();
            if (await rd.ReadAsync())
            {
                registroId = Convert.ToInt32(rd["ProgramaOperadorID"]);
                personaActual = Convert.ToInt32(rd["PersonaID"]);
            }
        }

        if (operadorId == personaActual)
            return;

        if (registroId.HasValue)
        {
            const string desactivar = @"
UPDATE dbo.Planeacion_ProgramaOperadores
SET Activo=0,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=GETDATE()
WHERE ProgramaOperadorID=@ID;";

            await using var cmd = new SqlCommand(desactivar, cn, tx);
            cmd.Parameters.Add("@ID", SqlDbType.Int).Value = registroId.Value;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            await cmd.ExecuteNonQueryAsync();
        }

        if (!operadorId.HasValue)
            return;

        const string insertar = @"
INSERT INTO dbo.Planeacion_ProgramaOperadores
(
    ProgramaProduccionID,PersonaID,RolOperador,Activo,
    UsuarioCreacionID,FechaCreacion
)
VALUES
(
    @ProgramaID,@PersonaID,N'PRINCIPAL',1,@UsuarioID,GETDATE()
);";

        await using (var cmd = new SqlCommand(insertar, cn, tx))
        {
            cmd.Parameters.Add("@ProgramaID", SqlDbType.Int).Value = programaId;
            cmd.Parameters.Add("@PersonaID", SqlDbType.Int).Value = operadorId.Value;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
