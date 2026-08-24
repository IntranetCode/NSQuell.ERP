using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers;

// NSQ_PRODUCCION_PERSONAL_V31
[Route("ProduccionPersonal")]
public sealed class ProduccionPersonalController : Controller
{
    private readonly IConfiguration _configuration;

    public ProduccionPersonalController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    private string ConnectionString =>
        _configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException(
            "No se encontró la cadena de conexión DefaultConnection.");

    private bool UsuarioEnSesion() =>
        HttpContext.Session.GetInt32("UsuarioID").HasValue;

    private int UsuarioID() =>
        HttpContext.Session.GetInt32("UsuarioID") ?? 0;

    [HttpGet("")]
    [HttpGet("Index")]
    public async Task<IActionResult> Index(
        DateTime? fechaDesde,
        DateTime? fechaHasta)
    {
        if (!UsuarioEnSesion())
            return RedirectToAction("Login", "Login");

        var desde = (fechaDesde ?? DateTime.Today).Date;
        var hasta = (fechaHasta ?? desde.AddDays(7)).Date;

        if (hasta < desde)
            (desde, hasta) = (hasta, desde);

        if ((hasta - desde).TotalDays > 31)
            hasta = desde.AddDays(31);

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        var vm = new ProduccionPersonalIndexVm
        {
            FechaDesde = desde,
            FechaHasta = hasta,
            TablaConfigurada = await TablaPersonalConfiguradaAsync(cn, null),
            Turnos = await CargarTurnosAsync(cn),
            Programas = await CargarProgramasAsync(desde, hasta, cn)
        };

        if (vm.TablaConfigurada && vm.Programas.Count > 0)
        {
            var asignaciones =
                await CargarAsignacionesAsync(
                    desde,
                    hasta.AddDays(1),
                    cn);

            var porPrograma =
                asignaciones
                    .GroupBy(x => x.ProgramaProduccionID)
                    .ToDictionary(x => x.Key, x => x.OrderBy(a => a.Inicio).ToList());

            foreach (var programa in vm.Programas)
            {
                if (porPrograma.TryGetValue(
                        programa.ProgramaProduccionID,
                        out var lista))
                {
                    programa.Asignaciones = lista;
                }
            }
        }

        return View(vm);
    }

    [HttpGet("Candidatos")]
    public async Task<IActionResult> Candidatos(
        int programaProduccionId,
        DateTime fechaTrabajo,
        int turnoId)
    {
        if (!UsuarioEnSesion())
            return Unauthorized();

        if (programaProduccionId <= 0 || turnoId <= 0)
        {
            return Json(new
            {
                ok = false,
                mensaje = "Programa o turno no válido."
            });
        }

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        var programa =
            await CargarProgramaBaseAsync(
                programaProduccionId,
                cn,
                null,
                false);

        if (programa == null)
            return Json(new { ok = false, mensaje = "No se encontró el programa de Producción." });

        var turno = await CargarTurnoAsync(turnoId, cn, null);
        if (turno == null)
            return Json(new { ok = false, mensaje = "No se encontró el turno." });

        var ventana = ConstruirVentana(programa, turno, fechaTrabajo.Date);
        if (ventana == null)
        {
            return Json(new
            {
                ok = false,
                mensaje = "Ese turno no cruza con el horario programado de la OF. Selecciona otro turno o fecha."
            });
        }

        var candidatos =
            await CargarCandidatosAsync(
                programa,
                turno,
                fechaTrabajo.Date,
                cn);

        return Json(new
        {
            ok = true,
            ventanaInicio = ventana.Value.Inicio,
            ventanaFin = ventana.Value.Fin,
            ventanaTexto =
                ventana.Value.Inicio.ToString("dd/MM/yyyy HH:mm") +
                " → " +
                ventana.Value.Fin.ToString("dd/MM/yyyy HH:mm"),
            turno = new
            {
                turnoID = turno.TurnoID,
                nombre = turno.Nombre,
                horario = turno.HorarioTexto
            },
            programa = new
            {
                programaProduccionID = programa.ProgramaProduccionID,
                maquina = programa.MaquinaCodigo,
                parte = programa.ParteVisible,
                inicio = programa.Inicio,
                fin = programa.Fin
            },
            tieneMatrizPolivalencia = candidatos.TieneMatriz,
            escalaPublicadaEncontrada = candidatos.EscalaPublicadaEncontrada,
            operadores = candidatos.Operadores,
            auxiliares = candidatos.Auxiliares,
            tecnicos = candidatos.Tecnicos
        });
    }

    [HttpPost("Guardar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Guardar(ProduccionPersonalGuardarVm vm)
    {
        if (!UsuarioEnSesion())
            return RedirectToAction("Login", "Login");

        if (vm.ProgramaProduccionID <= 0 || vm.TurnoID <= 0)
        {
            TempData["Error"] = "Programa o turno no válido.";
            return RedirectToAction(nameof(Index));
        }

        var tieneAlgunRol =
            (vm.OperadorID.HasValue && vm.OperadorID.Value > 0) ||
            (vm.AuxiliarID.HasValue && vm.AuxiliarID.Value > 0) ||
            (vm.TecnicoProduccionID.HasValue && vm.TecnicoProduccionID.Value > 0);

        if (!tieneAlgunRol)
        {
            TempData["Error"] =
                "Selecciona al menos una persona: operador, auxiliar o técnico de Producción.";
            return RedirectToAction(nameof(Index));
        }

        vm.Observaciones =
            string.IsNullOrWhiteSpace(vm.Observaciones)
                ? null
                : vm.Observaciones.Trim();

        if (vm.Observaciones?.Length > 500)
        {
            TempData["Error"] = "Las observaciones no pueden superar 500 caracteres.";
            return RedirectToAction(nameof(Index));
        }

        var ids = new[]
        {
            vm.OperadorID,
            vm.AuxiliarID,
            vm.TecnicoProduccionID
        }
        .Where(x => x.HasValue && x.Value > 0)
        .Select(x => x!.Value)
        .ToList();

        if (ids.Count != ids.Distinct().Count())
        {
            TempData["Error"] =
                "Operador, auxiliar y técnico deben ser personas diferentes.";
            return RedirectToAction(nameof(Index));
        }

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        if (!await TablaPersonalConfiguradaAsync(cn, null))
        {
            TempData["Error"] =
                "Falta crear dbo.Produccion_ProgramaPersonalAsignaciones. Ejecuta primero el SQL incluido en la tanda.";
            return RedirectToAction(nameof(Index));
        }

        await using var tx =
            (SqlTransaction)await cn.BeginTransactionAsync(
                IsolationLevel.Serializable);

        try
        {
            var programa =
                await CargarProgramaBaseAsync(
                    vm.ProgramaProduccionID,
                    cn,
                    tx,
                    true);

            if (programa == null)
                throw new InvalidOperationException("El programa ya no está disponible.");

            var turno = await CargarTurnoAsync(vm.TurnoID, cn, tx);
            if (turno == null)
                throw new InvalidOperationException("El turno ya no está disponible.");

            var ventana = ConstruirVentana(programa, turno, vm.FechaTrabajo.Date);
            if (ventana == null)
            {
                throw new InvalidOperationException(
                    "El turno seleccionado no cruza con el horario programado de la OF.");
            }

            string? operadorNombre = null;
            if (vm.OperadorID.HasValue && vm.OperadorID.Value > 0)
            {
                operadorNombre =
                    await ValidarPersonaRolAsync(
                        vm.OperadorID.Value,
                        "OPERADOR",
                        programa.ParteID,
                        cn,
                        tx);

                if (string.IsNullOrWhiteSpace(operadorNombre))
                {
                    throw new InvalidOperationException(
                        "El operador seleccionado no está activo o no cumple la polivalencia N1-N4 requerida para esta parte.");
                }
            }

            string? auxiliarNombre = null;
            if (vm.AuxiliarID.HasValue && vm.AuxiliarID.Value > 0)
            {
                auxiliarNombre =
                    await ValidarPersonaRolAsync(
                        vm.AuxiliarID.Value,
                        "AUXILIAR",
                        programa.ParteID,
                        cn,
                        tx);

                if (string.IsNullOrWhiteSpace(auxiliarNombre))
                    throw new InvalidOperationException("El auxiliar seleccionado no pertenece al catálogo activo de auxiliares.");
            }

            string? tecnicoNombre = null;
            if (vm.TecnicoProduccionID.HasValue && vm.TecnicoProduccionID.Value > 0)
            {
                tecnicoNombre =
                    await ValidarPersonaRolAsync(
                        vm.TecnicoProduccionID.Value,
                        "TECNICO",
                        programa.ParteID,
                        cn,
                        tx);

                if (string.IsNullOrWhiteSpace(tecnicoNombre))
                    throw new InvalidOperationException("El técnico seleccionado no pertenece al catálogo activo de Técnicos de Producción.");
            }

            foreach (var personaId in ids)
            {
                var conflicto =
                    await BuscarConflictoPersonaAsync(
                        personaId,
                        ventana.Value.Inicio,
                        ventana.Value.Fin,
                        vm.AsignacionPersonalID,
                        cn,
                        tx);

                if (conflicto != null)
                {
                    throw new InvalidOperationException(
                        "La persona " +
                        conflicto.PersonaNombre +
                        " ya está asignada al Programa " +
                        conflicto.ProgramaProduccionID +
                        " de " +
                        conflicto.Inicio.ToString("dd/MM HH:mm") +
                        " a " +
                        conflicto.Fin.ToString("dd/MM HH:mm") + ".");
                }
            }

            var existente =
                await ResolverAsignacionExistenteAsync(
                    vm,
                    cn,
                    tx);

            var usuarioId = UsuarioID();

            if (existente.HasValue)
            {
                const string actualizar = @"
UPDATE dbo.Produccion_ProgramaPersonalAsignaciones
SET
    FechaTrabajo=@FechaTrabajo,
    TurnoID=@TurnoID,
    TurnoNombre=@TurnoNombre,
    Inicio=@Inicio,
    Fin=@Fin,
    OperadorID=@OperadorID,
    AuxiliarID=@AuxiliarID,
    TecnicoProduccionID=@TecnicoProduccionID,
    Observaciones=@Observaciones,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=SYSDATETIME(),
    Activo=1
WHERE AsignacionPersonalID=@AsignacionPersonalID;";

                await using var cmd = new SqlCommand(actualizar, cn, tx);
                AgregarParametrosGuardar(
                    cmd,
                    existente.Value,
                    vm,
                    turno,
                    ventana.Value,
                    usuarioId);
                await cmd.ExecuteNonQueryAsync();
            }
            else
            {
                const string insertar = @"
INSERT INTO dbo.Produccion_ProgramaPersonalAsignaciones
(
    ProgramaProduccionID,
    FechaTrabajo,
    TurnoID,
    TurnoNombre,
    Inicio,
    Fin,
    OperadorID,
    AuxiliarID,
    TecnicoProduccionID,
    Observaciones,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
VALUES
(
    @ProgramaProduccionID,
    @FechaTrabajo,
    @TurnoID,
    @TurnoNombre,
    @Inicio,
    @Fin,
    @OperadorID,
    @AuxiliarID,
    @TecnicoProduccionID,
    @Observaciones,
    @UsuarioID,
    SYSDATETIME(),
    1
);";

                await using var cmd = new SqlCommand(insertar, cn, tx);
                AgregarParametrosGuardar(
                    cmd,
                    null,
                    vm,
                    turno,
                    ventana.Value,
                    usuarioId);
                await cmd.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();

            TempData["Success"] =
                "Programación de personal guardada para " +
                turno.Nombre +
                " · " +
                ventana.Value.Inicio.ToString("dd/MM HH:mm") +
                " → " +
                ventana.Value.Fin.ToString("dd/MM HH:mm") + ".";

            return RedirectToAction(nameof(Index), new
            {
                fechaDesde = programa.Inicio.Date.ToString("yyyy-MM-dd"),
                fechaHasta = programa.Inicio.Date.AddDays(7).ToString("yyyy-MM-dd")
            });
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(); } catch { }
            TempData["Error"] = "No fue posible guardar la programación de personal: " + ex.Message;
            return RedirectToAction(nameof(Index), new
            {
                fechaDesde = vm.FechaTrabajo.Date.ToString("yyyy-MM-dd"),
                fechaHasta = vm.FechaTrabajo.Date.AddDays(7).ToString("yyyy-MM-dd")
            });
        }
    }

    [HttpPost("Eliminar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Eliminar(
        int asignacionPersonalId,
        DateTime? volverAFecha = null)
    {
        if (!UsuarioEnSesion())
            return RedirectToAction("Login", "Login");

        if (asignacionPersonalId <= 0)
            return RedirectToAction(nameof(Index));

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        if (!await TablaPersonalConfiguradaAsync(cn, null))
            return RedirectToAction(nameof(Index));

        const string sql = @"
UPDATE dbo.Produccion_ProgramaPersonalAsignaciones
SET
    Activo=0,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=SYSDATETIME()
WHERE AsignacionPersonalID=@AsignacionPersonalID
  AND Activo=1;";

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@AsignacionPersonalID", SqlDbType.Int).Value = asignacionPersonalId;
        cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = UsuarioID();
        await cmd.ExecuteNonQueryAsync();

        TempData["Success"] = "Asignación retirada correctamente.";

        var fecha = (volverAFecha ?? DateTime.Today).Date;
        return RedirectToAction(nameof(Index), new
        {
            fechaDesde = fecha.ToString("yyyy-MM-dd"),
            fechaHasta = fecha.AddDays(7).ToString("yyyy-MM-dd")
        });
    }

    private static async Task<bool> TablaPersonalConfiguradaAsync(
        SqlConnection cn,
        SqlTransaction? tx)
    {
        const string sql = @"
SELECT CONVERT(bit,CASE WHEN OBJECT_ID(N'dbo.Produccion_ProgramaPersonalAsignaciones',N'U') IS NULL THEN 0 ELSE 1 END);";

        await using var cmd =
            tx == null ? new SqlCommand(sql, cn) : new SqlCommand(sql, cn, tx);
        return Convert.ToBoolean(await cmd.ExecuteScalarAsync() ?? false);
    }

    private static async Task<List<ProduccionPersonalTurnoVm>> CargarTurnosAsync(
        SqlConnection cn)
    {
        const string sql = @"
SELECT
    TurnoID,
    Nombre,
    HoraInicio,
    HoraFin,
    ISNULL(CruzaDiaSiguiente,0) AS CruzaDiaSiguiente,
    ISNULL(EsFlexible,0) AS EsFlexible,
    ISNULL(Orden,999) AS Orden
FROM dbo.RRHH_Turnos
WHERE Activo=1
ORDER BY ISNULL(Orden,999),HoraInicio,Nombre;";

        var result = new List<ProduccionPersonalTurnoVm>();
        await using var cmd = new SqlCommand(sql, cn);
        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            result.Add(MapearTurno(rd));
        }
        return result;
    }

    private static async Task<ProduccionPersonalTurnoVm?> CargarTurnoAsync(
        int turnoId,
        SqlConnection cn,
        SqlTransaction? tx)
    {
        const string sql = @"
SELECT TOP (1)
    TurnoID,
    Nombre,
    HoraInicio,
    HoraFin,
    ISNULL(CruzaDiaSiguiente,0) AS CruzaDiaSiguiente,
    ISNULL(EsFlexible,0) AS EsFlexible,
    ISNULL(Orden,999) AS Orden
FROM dbo.RRHH_Turnos
WHERE TurnoID=@TurnoID
  AND Activo=1;";

        await using var cmd =
            tx == null ? new SqlCommand(sql, cn) : new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@TurnoID", SqlDbType.Int).Value = turnoId;
        await using var rd = await cmd.ExecuteReaderAsync();
        return await rd.ReadAsync() ? MapearTurno(rd) : null;
    }

    private static ProduccionPersonalTurnoVm MapearTurno(SqlDataReader rd)
    {
        return new ProduccionPersonalTurnoVm
        {
            TurnoID = Convert.ToInt32(rd["TurnoID"]),
            Nombre = rd["Nombre"]?.ToString()?.Trim() ?? "Turno",
            HoraInicio = rd["HoraInicio"] == DBNull.Value ? null : (TimeSpan)rd["HoraInicio"],
            HoraFin = rd["HoraFin"] == DBNull.Value ? null : (TimeSpan)rd["HoraFin"],
            CruzaDiaSiguiente = Convert.ToBoolean(rd["CruzaDiaSiguiente"]),
            EsFlexible = Convert.ToBoolean(rd["EsFlexible"]),
            Orden = Convert.ToInt32(rd["Orden"])
        };
    }

    private static async Task<List<ProduccionPersonalProgramaVm>> CargarProgramasAsync(
        DateTime desde,
        DateTime hasta,
        SqlConnection cn)
    {
        var hastaExclusivo = hasta.Date.AddDays(1);

        const string sql = @"
SELECT
    pp.ProgramaProduccionID,
    pp.SolicitudProduccionID,
    COALESCE(NULLIF(s.NumeroOFRecibida,N''),NULLIF(s.FolioSolicitud,N''),N'') AS FolioOF,
    pp.MaquinaID,
    ISNULL(pp.MaquinaCodigo,N'') AS MaquinaCodigo,
    ISNULL(pp.MaquinaNombre,N'') AS MaquinaNombre,
    pp.ParteID,
    ISNULL(pp.NumeroParte,N'') AS NumeroParte,
    ISNULL(pp.ReferenciaSAP,N'') AS ReferenciaSAP,
    ISNULL(pp.DesignacionDescripcionSAP,N'') AS DescripcionParte,
    ISNULL(pp.MoldeCodigo,N'') AS MoldeCodigo,
    pp.FechaInicioProgramada,
    ISNULL
    (
        pp.FechaFinProgramada,
        DATEADD(MINUTE,CAST(CEILING(ISNULL(pp.HorasProgramadas,1)*60) AS INT),pp.FechaInicioProgramada)
    ) AS FechaFinProgramada,
    CONVERT(INT,ISNULL(pp.CantidadProgramada,0)) AS CantidadProgramada
FROM dbo.Planeacion_ProgramaProduccion pp
LEFT JOIN dbo.SolicitudesProduccion s
    ON s.SolicitudProduccionID=pp.SolicitudProduccionID
WHERE pp.Activo=1
  AND pp.MaquinaID IS NOT NULL
  AND pp.FechaInicioProgramada IS NOT NULL
  AND pp.FechaInicioProgramada<@Hasta
  AND ISNULL
      (
          pp.FechaFinProgramada,
          DATEADD(MINUTE,CAST(CEILING(ISNULL(pp.HorasProgramadas,1)*60) AS INT),pp.FechaInicioProgramada)
      )>=@Desde
  AND ISNULL(pp.EstatusID,1) NOT IN(6,9,99)
ORDER BY pp.FechaInicioProgramada,pp.MaquinaCodigo,pp.ProgramaProduccionID;";

        var result = new List<ProduccionPersonalProgramaVm>();
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@Desde", SqlDbType.DateTime).Value = desde.Date;
        cmd.Parameters.Add("@Hasta", SqlDbType.DateTime).Value = hastaExclusivo;
        await using var rd = await cmd.ExecuteReaderAsync();

        while (await rd.ReadAsync())
        {
            var codigo = rd["MaquinaCodigo"]?.ToString()?.Trim() ?? string.Empty;
            var nombre = rd["MaquinaNombre"]?.ToString()?.Trim() ?? string.Empty;
            if (Es1200T(codigo, nombre))
                continue;

            result.Add(new ProduccionPersonalProgramaVm
            {
                ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),
                SolicitudProduccionID = rd["SolicitudProduccionID"] == DBNull.Value ? null : Convert.ToInt32(rd["SolicitudProduccionID"]),
                FolioOF = rd["FolioOF"]?.ToString()?.Trim() ?? string.Empty,
                MaquinaID = rd["MaquinaID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaquinaID"]),
                MaquinaCodigo = codigo,
                MaquinaNombre = nombre,
                ParteID = rd["ParteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ParteID"]),
                NumeroParte = rd["NumeroParte"]?.ToString()?.Trim() ?? string.Empty,
                ReferenciaSAP = rd["ReferenciaSAP"]?.ToString()?.Trim() ?? string.Empty,
                DescripcionParte = rd["DescripcionParte"]?.ToString()?.Trim() ?? string.Empty,
                MoldeCodigo = rd["MoldeCodigo"]?.ToString()?.Trim() ?? string.Empty,
                Inicio = Convert.ToDateTime(rd["FechaInicioProgramada"]),
                Fin = Convert.ToDateTime(rd["FechaFinProgramada"]),
                CantidadProgramada = Convert.ToInt32(rd["CantidadProgramada"])
            });
        }

        return result;
    }

    private static async Task<ProduccionPersonalProgramaVm?> CargarProgramaBaseAsync(
        int programaProduccionId,
        SqlConnection cn,
        SqlTransaction? tx,
        bool bloquear)
    {
        var hint = bloquear ? " WITH (UPDLOCK,HOLDLOCK)" : string.Empty;
        var sql = $@"
SELECT TOP (1)
    pp.ProgramaProduccionID,
    pp.SolicitudProduccionID,
    COALESCE(NULLIF(s.NumeroOFRecibida,N''),NULLIF(s.FolioSolicitud,N''),N'') AS FolioOF,
    pp.MaquinaID,
    ISNULL(pp.MaquinaCodigo,N'') AS MaquinaCodigo,
    ISNULL(pp.MaquinaNombre,N'') AS MaquinaNombre,
    pp.ParteID,
    ISNULL(pp.NumeroParte,N'') AS NumeroParte,
    ISNULL(pp.ReferenciaSAP,N'') AS ReferenciaSAP,
    ISNULL(pp.DesignacionDescripcionSAP,N'') AS DescripcionParte,
    ISNULL(pp.MoldeCodigo,N'') AS MoldeCodigo,
    pp.FechaInicioProgramada,
    ISNULL
    (
        pp.FechaFinProgramada,
        DATEADD(MINUTE,CAST(CEILING(ISNULL(pp.HorasProgramadas,1)*60) AS INT),pp.FechaInicioProgramada)
    ) AS FechaFinProgramada,
    CONVERT(INT,ISNULL(pp.CantidadProgramada,0)) AS CantidadProgramada
FROM dbo.Planeacion_ProgramaProduccion pp{hint}
LEFT JOIN dbo.SolicitudesProduccion s
    ON s.SolicitudProduccionID=pp.SolicitudProduccionID
WHERE pp.ProgramaProduccionID=@ProgramaProduccionID
  AND pp.Activo=1
  AND ISNULL(pp.EstatusID,1) NOT IN(6,9,99);";

        await using var cmd =
            tx == null ? new SqlCommand(sql, cn) : new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;
        await using var rd = await cmd.ExecuteReaderAsync();
        if (!await rd.ReadAsync())
            return null;

        var codigo = rd["MaquinaCodigo"]?.ToString()?.Trim() ?? string.Empty;
        var nombre = rd["MaquinaNombre"]?.ToString()?.Trim() ?? string.Empty;
        if (Es1200T(codigo, nombre))
            return null;

        return new ProduccionPersonalProgramaVm
        {
            ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),
            SolicitudProduccionID = rd["SolicitudProduccionID"] == DBNull.Value ? null : Convert.ToInt32(rd["SolicitudProduccionID"]),
            FolioOF = rd["FolioOF"]?.ToString()?.Trim() ?? string.Empty,
            MaquinaID = rd["MaquinaID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaquinaID"]),
            MaquinaCodigo = codigo,
            MaquinaNombre = nombre,
            ParteID = rd["ParteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ParteID"]),
            NumeroParte = rd["NumeroParte"]?.ToString()?.Trim() ?? string.Empty,
            ReferenciaSAP = rd["ReferenciaSAP"]?.ToString()?.Trim() ?? string.Empty,
            DescripcionParte = rd["DescripcionParte"]?.ToString()?.Trim() ?? string.Empty,
            MoldeCodigo = rd["MoldeCodigo"]?.ToString()?.Trim() ?? string.Empty,
            Inicio = Convert.ToDateTime(rd["FechaInicioProgramada"]),
            Fin = Convert.ToDateTime(rd["FechaFinProgramada"]),
            CantidadProgramada = Convert.ToInt32(rd["CantidadProgramada"])
        };
    }

    private static (DateTime Inicio, DateTime Fin)? ConstruirVentana(
        ProduccionPersonalProgramaVm programa,
        ProduccionPersonalTurnoVm turno,
        DateTime fechaTrabajo)
    {
        var horaInicio = turno.HoraInicioEfectiva;
        var horaFin = turno.HoraFinEfectiva;

        if (!horaInicio.HasValue || !horaFin.HasValue)
            return null;

        var inicioTurno = fechaTrabajo.Date.Add(horaInicio.Value);
        var finTurno = fechaTrabajo.Date.Add(horaFin.Value);

        if (turno.CruzaDiaSiguienteEfectivo || finTurno <= inicioTurno)
            finTurno = finTurno.AddDays(1);

        var inicio = programa.Inicio > inicioTurno ? programa.Inicio : inicioTurno;
        var fin = programa.Fin < finTurno ? programa.Fin : finTurno;

        return fin > inicio ? (inicio, fin) : null;
    }

    private sealed class CandidatosResultado
    {
        public bool TieneMatriz { get; set; }
        public bool EscalaPublicadaEncontrada { get; set; }
        public List<object> Operadores { get; set; } = new();
        public List<object> Auxiliares { get; set; } = new();
        public List<object> Tecnicos { get; set; } = new();
    }

    private static async Task<CandidatosResultado> CargarCandidatosAsync(
        ProduccionPersonalProgramaVm programa,
        ProduccionPersonalTurnoVm turno,
        DateTime fechaTrabajo,
        SqlConnection cn)
    {
        var result = new CandidatosResultado();

        result.TieneMatriz =
            programa.ParteID.HasValue &&
            await ParteTieneMatrizAsync(programa.ParteID.Value, cn, null);

        const string escalaSql = @"
SELECT CONVERT(bit,CASE WHEN EXISTS
(
    SELECT 1
    FROM dbo.RRHH_EscalasPersonal e
    INNER JOIN dbo.RRHH_EscalaTurnos et
        ON et.EscalaID=e.EscalaID
       AND et.Activo=1
    WHERE e.Activo=1
      AND e.Estado=N'Publicada'
      AND @Fecha BETWEEN e.FechaInicio AND e.FechaFin
      AND
      (
          et.TurnoOrigenID=@TurnoID
          OR UPPER(LTRIM(RTRIM(et.Nombre)))=UPPER(LTRIM(RTRIM(@TurnoNombre)))
      )
) THEN 1 ELSE 0 END);";

        await using (var cmd = new SqlCommand(escalaSql, cn))
        {
            cmd.Parameters.Add("@Fecha", SqlDbType.Date).Value = fechaTrabajo.Date;
            cmd.Parameters.Add("@TurnoID", SqlDbType.Int).Value = turno.TurnoID;
            cmd.Parameters.Add("@TurnoNombre", SqlDbType.NVarChar, 100).Value = turno.Nombre;
            result.EscalaPublicadaEncontrada = Convert.ToBoolean(await cmd.ExecuteScalarAsync() ?? false);
        }

        var roleBase = @"
SELECT
    p.PersonaID,
    LTRIM(RTRIM(CONCAT(ISNULL(p.Nombre,N''),N' ',ISNULL(p.ApellidoPaterno,N''),N' ',ISNULL(p.ApellidoMaterno,N'')))) AS Nombre,
    ISNULL(p.Puesto,N'') AS Puesto,
    escala.EnEscala,
    escala.MaquinaCodigo,
    escala.FuncionNombre,
    escala.TurnoNombre,
    pol.Nivel
FROM dbo.Persona p
OUTER APPLY
(
    SELECT TOP (1)
        CAST(1 AS bit) AS EnEscala,
        ISNULL(m.Codigo,N'') AS MaquinaCodigo,
        ISNULL(f.Nombre,N'') AS FuncionNombre,
        ISNULL(et.Nombre,N'') AS TurnoNombre
    FROM dbo.RRHH_EscalaAsignaciones a
    INNER JOIN dbo.RRHH_EscalasPersonal e
        ON e.EscalaID=a.EscalaID
       AND e.Activo=1
       AND e.Estado=N'Publicada'
    INNER JOIN dbo.RRHH_EscalaTurnos et
        ON et.EscalaID=a.EscalaID
       AND et.EscalaTurnoID=a.EscalaTurnoID
       AND et.Activo=1
    LEFT JOIN dbo.RRHH_FuncionesPersonal f
        ON f.FuncionID=a.FuncionID
       AND f.Activo=1
    LEFT JOIN dbo.ERP_Maquinas m
        ON m.MaquinaID=a.MaquinaID
    WHERE a.Activo=1
      AND a.PersonalID=p.PersonaID
      AND @Fecha BETWEEN CONVERT(date,a.FechaInicio) AND CONVERT(date,a.FechaFin)
      AND (@MaquinaID IS NULL OR a.MaquinaID=@MaquinaID)
      AND
      (
          et.TurnoOrigenID=@TurnoID
          OR UPPER(LTRIM(RTRIM(et.Nombre)))=UPPER(LTRIM(RTRIM(@TurnoNombre)))
      )
    ORDER BY a.AsignacionID DESC
) escala
OUTER APPLY
(
    SELECT TOP (1) CONVERT(INT,v.Nivel) AS Nivel
    FROM dbo.vw_RRHH_PolivalenciaOperadoresParte v
    WHERE @ParteID IS NOT NULL
      AND v.ParteID=@ParteID
      AND v.PersonalID=p.PersonaID
    ORDER BY CONVERT(INT,v.Nivel) DESC
) pol
WHERE ISNULL(p.EsColaboradorActivo,1)=1
  AND {ROLE_FILTER}
  AND {POL_FILTER}
ORDER BY
    CASE WHEN escala.EnEscala=1 THEN 0 ELSE 1 END,
    CASE WHEN pol.Nivel IS NULL THEN 99 ELSE 4-pol.Nivel END,
    Nombre;";

        result.Operadores =
            await EjecutarCandidatosAsync(
                roleBase
                    .Replace("{ROLE_FILTER}", @"(
                        UPPER(LTRIM(RTRIM(ISNULL(p.Puesto,N''))))=N'OPERADOR'
                        OR EXISTS
                        (
                            SELECT 1
                            FROM dbo.RRHH_EscalaAsignaciones ax
                            INNER JOIN dbo.RRHH_FuncionesPersonal fx
                                ON fx.FuncionID=ax.FuncionID
                               AND fx.Activo=1
                            WHERE ax.Activo=1
                              AND ax.PersonalID=p.PersonaID
                              AND UPPER(LTRIM(RTRIM(fx.Nombre)))=N'OPERADOR'
                        )
                    )")
                    .Replace(
                        "{POL_FILTER}",
                        result.TieneMatriz
                            ? "pol.Nivel BETWEEN 1 AND 4"
                            : "1=1"),
                programa.ParteID,
                turno,
                fechaTrabajo,
                programa.MaquinaID,
                cn);

        result.Auxiliares =
            await EjecutarCandidatosAsync(
                roleBase
                    .Replace("{ROLE_FILTER}", @"(
                        UPPER(LTRIM(RTRIM(ISNULL(p.Puesto,N'')))) LIKE N'%AUXILIAR%'
                        OR EXISTS
                        (
                            SELECT 1
                            FROM dbo.RRHH_EscalaAsignaciones ax
                            INNER JOIN dbo.RRHH_FuncionesPersonal fx
                                ON fx.FuncionID=ax.FuncionID
                               AND fx.Activo=1
                            WHERE ax.Activo=1
                              AND ax.PersonalID=p.PersonaID
                              AND UPPER(LTRIM(RTRIM(fx.Nombre))) LIKE N'%AUXILIAR%'
                        )
                    )")
                    .Replace("{POL_FILTER}", "1=1"),
                programa.ParteID,
                turno,
                fechaTrabajo,
                programa.MaquinaID,
                cn);

        result.Tecnicos =
            await EjecutarCandidatosAsync(
                roleBase
                    .Replace("{ROLE_FILTER}", @"(
                        (
                            UPPER(LTRIM(RTRIM(ISNULL(p.Puesto,N'')))) LIKE N'%TECNIC%'
                            AND UPPER(LTRIM(RTRIM(ISNULL(p.Puesto,N'')))) LIKE N'%PRODU%'
                        )
                        OR EXISTS
                        (
                            SELECT 1
                            FROM dbo.RRHH_EscalaAsignaciones ax
                            INNER JOIN dbo.RRHH_FuncionesPersonal fx
                                ON fx.FuncionID=ax.FuncionID
                               AND fx.Activo=1
                            WHERE ax.Activo=1
                              AND ax.PersonalID=p.PersonaID
                              AND UPPER(LTRIM(RTRIM(fx.Nombre))) LIKE N'%TECNIC%'
                              AND UPPER(LTRIM(RTRIM(fx.Nombre))) LIKE N'%PRODU%'
                        )
                    )")
                    .Replace("{POL_FILTER}", "1=1"),
                programa.ParteID,
                turno,
                fechaTrabajo,
                programa.MaquinaID,
                cn);

        return result;
    }

    private static async Task<List<object>> EjecutarCandidatosAsync(
        string sql,
        int? parteId,
        ProduccionPersonalTurnoVm turno,
        DateTime fecha,
        int? maquinaId,
        SqlConnection cn)
    {
        var result = new List<object>();
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = parteId.HasValue ? parteId.Value : DBNull.Value;
        cmd.Parameters.Add("@Fecha", SqlDbType.Date).Value = fecha.Date;
        cmd.Parameters.Add("@TurnoID", SqlDbType.Int).Value = turno.TurnoID;
        cmd.Parameters.Add("@TurnoNombre", SqlDbType.NVarChar, 100).Value = turno.Nombre;
        cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = maquinaId.HasValue ? maquinaId.Value : DBNull.Value;
        await using var rd = await cmd.ExecuteReaderAsync();

        while (await rd.ReadAsync())
        {
            result.Add(new
            {
                personaID = Convert.ToInt32(rd["PersonaID"]),
                nombre = rd["Nombre"]?.ToString()?.Trim() ?? string.Empty,
                puesto = rd["Puesto"]?.ToString()?.Trim() ?? string.Empty,
                enEscala = rd["EnEscala"] != DBNull.Value && Convert.ToBoolean(rd["EnEscala"]),
                maquinaEscala = rd["MaquinaCodigo"]?.ToString()?.Trim() ?? string.Empty,
                funcionEscala = rd["FuncionNombre"]?.ToString()?.Trim() ?? string.Empty,
                turnoEscala = rd["TurnoNombre"]?.ToString()?.Trim() ?? string.Empty,
                nivel = rd["Nivel"] == DBNull.Value ? (int?)null : Convert.ToInt32(rd["Nivel"])
            });
        }

        return result;
    }

    private static async Task<bool> ParteTieneMatrizAsync(
        int parteId,
        SqlConnection cn,
        SqlTransaction? tx)
    {
        const string sql = @"
SELECT CONVERT(bit,CASE WHEN
       OBJECT_ID(N'dbo.vw_RRHH_PolivalenciaOperadoresParte',N'V') IS NOT NULL
   AND EXISTS
       (
           SELECT 1
           FROM dbo.vw_RRHH_PolivalenciaOperadoresParte
           WHERE ParteID=@ParteID
       )
THEN 1 ELSE 0 END);";

        await using var cmd =
            tx == null ? new SqlCommand(sql, cn) : new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = parteId;
        return Convert.ToBoolean(await cmd.ExecuteScalarAsync() ?? false);
    }

    private static async Task<string?> ValidarPersonaRolAsync(
        int personaId,
        string rol,
        int? parteId,
        SqlConnection cn,
        SqlTransaction tx)
    {
        if (personaId <= 0)
            return null;

        var tieneMatriz =
            rol == "OPERADOR" &&
            parteId.HasValue &&
            await ParteTieneMatrizAsync(parteId.Value, cn, tx);

        var condicionRol = rol switch
        {
            "OPERADOR" => @"(
                UPPER(LTRIM(RTRIM(ISNULL(p.Puesto,N''))))=N'OPERADOR'
                OR EXISTS
                (
                    SELECT 1
                    FROM dbo.RRHH_EscalaAsignaciones a
                    INNER JOIN dbo.RRHH_FuncionesPersonal f
                        ON f.FuncionID=a.FuncionID
                       AND f.Activo=1
                    WHERE a.Activo=1
                      AND a.PersonalID=p.PersonaID
                      AND UPPER(LTRIM(RTRIM(f.Nombre)))=N'OPERADOR'
                )
            )",
            "AUXILIAR" => @"(
                UPPER(LTRIM(RTRIM(ISNULL(p.Puesto,N'')))) LIKE N'%AUXILIAR%'
                OR EXISTS
                (
                    SELECT 1
                    FROM dbo.RRHH_EscalaAsignaciones a
                    INNER JOIN dbo.RRHH_FuncionesPersonal f
                        ON f.FuncionID=a.FuncionID
                       AND f.Activo=1
                    WHERE a.Activo=1
                      AND a.PersonalID=p.PersonaID
                      AND UPPER(LTRIM(RTRIM(f.Nombre))) LIKE N'%AUXILIAR%'
                )
            )",
            "TECNICO" => @"(
                (
                    UPPER(LTRIM(RTRIM(ISNULL(p.Puesto,N'')))) LIKE N'%TECNIC%'
                    AND UPPER(LTRIM(RTRIM(ISNULL(p.Puesto,N'')))) LIKE N'%PRODU%'
                )
                OR EXISTS
                (
                    SELECT 1
                    FROM dbo.RRHH_EscalaAsignaciones a
                    INNER JOIN dbo.RRHH_FuncionesPersonal f
                        ON f.FuncionID=a.FuncionID
                       AND f.Activo=1
                    WHERE a.Activo=1
                      AND a.PersonalID=p.PersonaID
                      AND UPPER(LTRIM(RTRIM(f.Nombre))) LIKE N'%TECNIC%'
                      AND UPPER(LTRIM(RTRIM(f.Nombre))) LIKE N'%PRODU%'
                )
            )",
            _ => "1=0"
        };

        var sql = $@"
SELECT TOP (1)
    LTRIM(RTRIM(CONCAT(ISNULL(p.Nombre,N''),N' ',ISNULL(p.ApellidoPaterno,N''),N' ',ISNULL(p.ApellidoMaterno,N'')))) AS Nombre
FROM dbo.Persona p
WHERE p.PersonaID=@PersonaID
  AND ISNULL(p.EsColaboradorActivo,1)=1
  AND {condicionRol}
  AND
  (
      @RequierePolivalencia=0
      OR EXISTS
         (
             SELECT 1
             FROM dbo.vw_RRHH_PolivalenciaOperadoresParte v
             WHERE v.PersonalID=p.PersonaID
               AND v.ParteID=@ParteID
               AND CONVERT(INT,v.Nivel) BETWEEN 1 AND 4
         )
  );";

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@PersonaID", SqlDbType.Int).Value = personaId;
        cmd.Parameters.Add("@RequierePolivalencia", SqlDbType.Bit).Value = tieneMatriz;
        cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = parteId.HasValue ? parteId.Value : DBNull.Value;
        var value = await cmd.ExecuteScalarAsync();
        return value == null || value == DBNull.Value ? null : value.ToString()?.Trim();
    }

    private sealed class ConflictoPersona
    {
        public int ProgramaProduccionID { get; set; }
        public string PersonaNombre { get; set; } = "La persona";
        public DateTime Inicio { get; set; }
        public DateTime Fin { get; set; }
    }

    private static async Task<ConflictoPersona?> BuscarConflictoPersonaAsync(
        int personaId,
        DateTime inicio,
        DateTime fin,
        int? asignacionExcluirId,
        SqlConnection cn,
        SqlTransaction tx)
    {
        const string sql = @"
SELECT TOP (1)
    a.ProgramaProduccionID,
    a.Inicio,
    a.Fin,
    LTRIM(RTRIM(CONCAT(ISNULL(p.Nombre,N''),N' ',ISNULL(p.ApellidoPaterno,N''),N' ',ISNULL(p.ApellidoMaterno,N'')))) AS PersonaNombre
FROM dbo.Produccion_ProgramaPersonalAsignaciones a WITH (UPDLOCK,HOLDLOCK)
INNER JOIN dbo.Persona p ON p.PersonaID=@PersonaID
WHERE a.Activo=1
  AND (@AsignacionExcluirID IS NULL OR a.AsignacionPersonalID<>@AsignacionExcluirID)
  AND @Inicio<a.Fin
  AND @Fin>a.Inicio
  AND
  (
      a.OperadorID=@PersonaID
      OR a.AuxiliarID=@PersonaID
      OR a.TecnicoProduccionID=@PersonaID
  )
ORDER BY a.Inicio;";

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@PersonaID", SqlDbType.Int).Value = personaId;
        cmd.Parameters.Add("@AsignacionExcluirID", SqlDbType.Int).Value =
            asignacionExcluirId.HasValue ? asignacionExcluirId.Value : DBNull.Value;
        cmd.Parameters.Add("@Inicio", SqlDbType.DateTime2).Value = inicio;
        cmd.Parameters.Add("@Fin", SqlDbType.DateTime2).Value = fin;
        await using var rd = await cmd.ExecuteReaderAsync();
        if (!await rd.ReadAsync())
            return null;

        return new ConflictoPersona
        {
            ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),
            PersonaNombre = rd["PersonaNombre"]?.ToString()?.Trim() ?? "La persona",
            Inicio = Convert.ToDateTime(rd["Inicio"]),
            Fin = Convert.ToDateTime(rd["Fin"])
        };
    }

    private static async Task<int?> ResolverAsignacionExistenteAsync(
        ProduccionPersonalGuardarVm vm,
        SqlConnection cn,
        SqlTransaction tx)
    {
        if (vm.AsignacionPersonalID.HasValue && vm.AsignacionPersonalID.Value > 0)
            return vm.AsignacionPersonalID.Value;

        const string sql = @"
SELECT TOP (1) AsignacionPersonalID
FROM dbo.Produccion_ProgramaPersonalAsignaciones WITH (UPDLOCK,HOLDLOCK)
WHERE Activo=1
  AND ProgramaProduccionID=@ProgramaProduccionID
  AND FechaTrabajo=@FechaTrabajo
  AND TurnoID=@TurnoID
ORDER BY AsignacionPersonalID DESC;";

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = vm.ProgramaProduccionID;
        cmd.Parameters.Add("@FechaTrabajo", SqlDbType.Date).Value = vm.FechaTrabajo.Date;
        cmd.Parameters.Add("@TurnoID", SqlDbType.Int).Value = vm.TurnoID;
        var value = await cmd.ExecuteScalarAsync();
        return value == null || value == DBNull.Value ? null : Convert.ToInt32(value);
    }

    private static void AgregarParametrosGuardar(
        SqlCommand cmd,
        int? asignacionPersonalId,
        ProduccionPersonalGuardarVm vm,
        ProduccionPersonalTurnoVm turno,
        (DateTime Inicio, DateTime Fin) ventana,
        int usuarioId)
    {
        if (asignacionPersonalId.HasValue)
            cmd.Parameters.Add("@AsignacionPersonalID", SqlDbType.Int).Value = asignacionPersonalId.Value;

        cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = vm.ProgramaProduccionID;
        cmd.Parameters.Add("@FechaTrabajo", SqlDbType.Date).Value = vm.FechaTrabajo.Date;
        cmd.Parameters.Add("@TurnoID", SqlDbType.Int).Value = vm.TurnoID;
        cmd.Parameters.Add("@TurnoNombre", SqlDbType.NVarChar, 100).Value = turno.Nombre;
        cmd.Parameters.Add("@Inicio", SqlDbType.DateTime2).Value = ventana.Inicio;
        cmd.Parameters.Add("@Fin", SqlDbType.DateTime2).Value = ventana.Fin;
        cmd.Parameters.Add("@OperadorID", SqlDbType.Int).Value = vm.OperadorID.HasValue ? vm.OperadorID.Value : DBNull.Value;
        cmd.Parameters.Add("@AuxiliarID", SqlDbType.Int).Value = vm.AuxiliarID.HasValue ? vm.AuxiliarID.Value : DBNull.Value;
        cmd.Parameters.Add("@TecnicoProduccionID", SqlDbType.Int).Value = vm.TecnicoProduccionID.HasValue ? vm.TecnicoProduccionID.Value : DBNull.Value;
        cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 500).Value = string.IsNullOrWhiteSpace(vm.Observaciones) ? DBNull.Value : vm.Observaciones.Trim();
        cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
    }

    private static async Task<List<ProduccionPersonalAsignacionVm>> CargarAsignacionesAsync(
        DateTime desde,
        DateTime hasta,
        SqlConnection cn)
    {
        const string sql = @"
SELECT
    a.AsignacionPersonalID,
    a.ProgramaProduccionID,
    a.FechaTrabajo,
    a.TurnoID,
    a.TurnoNombre,
    a.Inicio,
    a.Fin,
    a.OperadorID,
    LTRIM(RTRIM(CONCAT(ISNULL(op.Nombre,N''),N' ',ISNULL(op.ApellidoPaterno,N''),N' ',ISNULL(op.ApellidoMaterno,N'')))) AS OperadorNombre,
    a.AuxiliarID,
    LTRIM(RTRIM(CONCAT(ISNULL(aux.Nombre,N''),N' ',ISNULL(aux.ApellidoPaterno,N''),N' ',ISNULL(aux.ApellidoMaterno,N'')))) AS AuxiliarNombre,
    a.TecnicoProduccionID,
    LTRIM(RTRIM(CONCAT(ISNULL(tec.Nombre,N''),N' ',ISNULL(tec.ApellidoPaterno,N''),N' ',ISNULL(tec.ApellidoMaterno,N'')))) AS TecnicoProduccionNombre,
    ISNULL(a.Observaciones,N'') AS Observaciones
FROM dbo.Produccion_ProgramaPersonalAsignaciones a
LEFT JOIN dbo.Persona op ON op.PersonaID=a.OperadorID
LEFT JOIN dbo.Persona aux ON aux.PersonaID=a.AuxiliarID
LEFT JOIN dbo.Persona tec ON tec.PersonaID=a.TecnicoProduccionID
WHERE a.Activo=1
  AND a.Inicio<@Hasta
  AND a.Fin>@Desde
ORDER BY a.Inicio,a.AsignacionPersonalID;";

        var result = new List<ProduccionPersonalAsignacionVm>();
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@Desde", SqlDbType.DateTime2).Value = desde;
        cmd.Parameters.Add("@Hasta", SqlDbType.DateTime2).Value = hasta;
        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            result.Add(new ProduccionPersonalAsignacionVm
            {
                AsignacionPersonalID = Convert.ToInt32(rd["AsignacionPersonalID"]),
                ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),
                FechaTrabajo = Convert.ToDateTime(rd["FechaTrabajo"]),
                TurnoID = rd["TurnoID"] == DBNull.Value ? null : Convert.ToInt32(rd["TurnoID"]),
                TurnoNombre = rd["TurnoNombre"]?.ToString()?.Trim() ?? string.Empty,
                Inicio = Convert.ToDateTime(rd["Inicio"]),
                Fin = Convert.ToDateTime(rd["Fin"]),
                OperadorID = rd["OperadorID"] == DBNull.Value ? null : Convert.ToInt32(rd["OperadorID"]),
                OperadorNombre = rd["OperadorNombre"]?.ToString()?.Trim() ?? string.Empty,
                AuxiliarID = rd["AuxiliarID"] == DBNull.Value ? null : Convert.ToInt32(rd["AuxiliarID"]),
                AuxiliarNombre = rd["AuxiliarNombre"]?.ToString()?.Trim() ?? string.Empty,
                TecnicoProduccionID = rd["TecnicoProduccionID"] == DBNull.Value ? null : Convert.ToInt32(rd["TecnicoProduccionID"]),
                TecnicoProduccionNombre = rd["TecnicoProduccionNombre"]?.ToString()?.Trim() ?? string.Empty,
                Observaciones = rd["Observaciones"]?.ToString()?.Trim() ?? string.Empty
            });
        }
        return result;
    }

    private static bool Es1200T(string? codigo, string? nombre)
    {
        static string N(string? value) =>
            (value ?? string.Empty)
                .Replace("-", string.Empty, StringComparison.Ordinal)
                .Replace(" ", string.Empty, StringComparison.Ordinal)
                .Trim()
                .ToUpperInvariant();

        return N(codigo) == "1200T" || N(nombre).Contains("1200T", StringComparison.Ordinal);
    }
}
