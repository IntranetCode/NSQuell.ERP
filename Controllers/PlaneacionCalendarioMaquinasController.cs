using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Security.Claims;

namespace ERP.NSQuell.Controllers;

[Route("PlaneacionCalendarioMaquinas")]
public sealed class PlaneacionCalendarioMaquinasController : Controller
{
    private readonly IConfiguration _configuration;

    public PlaneacionCalendarioMaquinasController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    private string ConnectionString =>
        _configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("No se encontro la cadena DefaultConnection.");

    [HttpGet("")]
    [HttpGet("Index")]
    public async Task<IActionResult> Index(
    string vista = "semana",
    DateTime? fecha = null,
    DateTime? rangoInicio = null,
    DateTime? rangoFin = null,
    DateTime? semana = null)
    {
        var vistaNormalizada = string.IsNullOrWhiteSpace(vista)
            ? "semana"
            : vista.Trim().ToLowerInvariant();

        if (vistaNormalizada != "dia" &&
            vistaNormalizada != "semana" &&
            vistaNormalizada != "mes" &&
            vistaNormalizada != "rango")
        {
            vistaNormalizada = "semana";
        }

        var fechaBase = (fecha ?? semana ?? DateTime.Today).Date;

        DateTime inicioPeriodo;
        DateTime finPeriodo;
        DateTime? rangoInicioVm = null;
        DateTime? rangoFinVm = null;

        if (vistaNormalizada == "dia")
        {
            inicioPeriodo = fechaBase;
            finPeriodo = inicioPeriodo.AddDays(1);
        }
        else if (vistaNormalizada == "mes")
        {
            inicioPeriodo = new DateTime(fechaBase.Year, fechaBase.Month, 1);
            finPeriodo = inicioPeriodo.AddMonths(1);
        }
        else if (vistaNormalizada == "rango")
        {
            if (!rangoInicio.HasValue || !rangoFin.HasValue)
            {
                vistaNormalizada = "semana";

                var diasDesdeLunesDefault = ((int)fechaBase.DayOfWeek + 6) % 7;
                inicioPeriodo = fechaBase.AddDays(-diasDesdeLunesDefault);
                finPeriodo = inicioPeriodo.AddDays(7);
            }
            else
            {
                inicioPeriodo = rangoInicio.Value.Date;
                var finInclusivo = rangoFin.Value.Date;

                if (finInclusivo < inicioPeriodo)
                {
                    var temporal = inicioPeriodo;
                    inicioPeriodo = finInclusivo;
                    finInclusivo = temporal;
                }

                finPeriodo = finInclusivo.AddDays(1);

                if ((finPeriodo - inicioPeriodo).TotalDays > 31)
                {
                    TempData["CalendarioError"] =
                        "El rango no puede ser mayor a un mes. Se limitó a 31 días.";

                    finPeriodo = inicioPeriodo.AddDays(31);
                }

                if (finPeriodo <= inicioPeriodo)
                    finPeriodo = inicioPeriodo.AddDays(1);

                rangoInicioVm = inicioPeriodo;
                rangoFinVm = finPeriodo.AddDays(-1);
            }
        }
        else
        {
            vistaNormalizada = "semana";

            var diasDesdeLunes = ((int)fechaBase.DayOfWeek + 6) % 7;
            inicioPeriodo = fechaBase.AddDays(-diasDesdeLunes);
            finPeriodo = inicioPeriodo.AddDays(7);
        }

        var vm = new PlaneacionCalendarioMaquinasVm
        {
            Vista = vistaNormalizada,
            InicioPeriodo = inicioPeriodo,
            FinPeriodo = finPeriodo,
            FechaReferencia = fechaBase,
            RangoInicio = rangoInicioVm,
            RangoFin = rangoFinVm,
            Ahora = DateTime.Now
        };

        const string sql = @"
SELECT
    m.MaquinaID,
    m.Codigo AS MaquinaCodigo,
    m.Nombre AS MaquinaNombre,

    pp.ProgramaProduccionID,
    pp.SolicitudProduccionID,
    pp.SolicitudProduccionDetalleID,
    pp.ReleaseDetalleID,

    pp.ClienteNombre,
    pp.NumeroParte,
    pp.ReferenciaSAP,
    pp.DesignacionDescripcionSAP,
    pp.MoldeCodigo,
    ISNULL(pp.CantidadProgramada, 0) AS CantidadProgramada,
    ISNULL(pp.CantidadProducida, 0) AS CantidadProducida,
    pp.FechaInicioProgramada,
    ISNULL(
        pp.FechaFinProgramada,
        DATEADD(
            MINUTE,
            CONVERT(INT, CEILING(ISNULL(pp.HorasProgramadas, 1) * 60)),
            pp.FechaInicioProgramada
        )
    ) AS FechaFinProgramada,
    ISNULL(pp.HorasProgramadas, 0) AS HorasProgramadas,
    pp.Cambio,
    pp.Arranque,
    ISNULL(pp.EstatusID, 1) AS EstatusID,

    CAST(
        CASE
            WHEN pp.ProgramaProduccionID IS NULL THEN 0
            WHEN ISNULL(pp.EstatusID, 1) IN (5, 9, 99) THEN 0
            WHEN ISNULL(pp.CantidadProgramada, 0) > 0
                 AND ISNULL(pp.CantidadProducida, 0) >= ISNULL(pp.CantidadProgramada, 0) THEN 0
            WHEN GETDATE() >= pp.FechaInicioProgramada
                 AND GETDATE() < ISNULL(
                        pp.FechaFinProgramada,
                        DATEADD(
                            MINUTE,
                            CONVERT(INT, CEILING(ISNULL(pp.HorasProgramadas, 1) * 60)),
                            pp.FechaInicioProgramada
                        )
                     ) THEN 1
            ELSE 0
        END AS bit
    ) AS EstaEnLinea,

    t.MaquinaPrincipalID,
    maqPrincipal.Codigo AS MaquinaPrincipalCodigo,
    maqPrincipal.Nombre AS MaquinaPrincipalNombre,

    t.MaquinaSustitutaID,
    maqSustituta.Codigo AS MaquinaSustitutaCodigo,
    maqSustituta.Nombre AS MaquinaSustitutaNombre

FROM dbo.ERP_Maquinas m
LEFT JOIN dbo.Planeacion_ProgramaProduccion pp
    ON pp.MaquinaID = m.MaquinaID
   AND pp.Activo = 1
   AND ISNULL(pp.EstatusID, 1) <> 99
   AND pp.FechaInicioProgramada IS NOT NULL
   AND pp.FechaInicioProgramada < @FinPeriodo
   AND ISNULL(
        pp.FechaFinProgramada,
        DATEADD(
            MINUTE,
            CONVERT(INT, CEILING(ISNULL(pp.HorasProgramadas, 1) * 60)),
            pp.FechaInicioProgramada
        )
   ) > @InicioPeriodo

LEFT JOIN dbo.ERP_ParteDatosTecnicos t
    ON t.ParteID = pp.ParteID
   AND t.Activo = 1

LEFT JOIN dbo.ERP_Maquinas maqPrincipal
    ON maqPrincipal.MaquinaID = t.MaquinaPrincipalID

LEFT JOIN dbo.ERP_Maquinas maqSustituta
    ON maqSustituta.MaquinaID = t.MaquinaSustitutaID

WHERE m.Activo = 1
ORDER BY
    m.Codigo,
    pp.FechaInicioProgramada,
    pp.ProgramaProduccionID;";

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@InicioPeriodo", SqlDbType.DateTime).Value = inicioPeriodo;
        cmd.Parameters.Add("@FinPeriodo", SqlDbType.DateTime).Value = finPeriodo;

        await using var rd = await cmd.ExecuteReaderAsync();

        var maquinas = new Dictionary<int, PlaneacionCalendarioMaquinaVm>();

        while (await rd.ReadAsync())
        {
            var maquinaId = Convert.ToInt32(rd["MaquinaID"]);

            if (!maquinas.TryGetValue(maquinaId, out var maquina))
            {
                maquina = new PlaneacionCalendarioMaquinaVm
                {
                    MaquinaID = maquinaId,
                    Codigo = rd["MaquinaCodigo"] as string ?? maquinaId.ToString(),
                    Nombre = rd["MaquinaNombre"] as string ?? string.Empty
                };

                maquinas.Add(maquinaId, maquina);
            }

            if (rd["ProgramaProduccionID"] == DBNull.Value)
                continue;

            var inicioBloque = Convert.ToDateTime(rd["FechaInicioProgramada"]);
            var finBloque = Convert.ToDateTime(rd["FechaFinProgramada"]);

            if (finBloque <= inicioBloque)
                finBloque = inicioBloque.AddHours(1);

            maquina.Bloques.Add(new PlaneacionCalendarioBloqueVm
            {
                ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),

                SolicitudProduccionID =
                    rd["SolicitudProduccionID"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(rd["SolicitudProduccionID"]),

                MaquinaID = maquinaId,
                MaquinaCodigo = maquina.Codigo,

                ClienteNombre = rd["ClienteNombre"] as string,
                NumeroParte = rd["NumeroParte"] as string,
                ReferenciaSAP = rd["ReferenciaSAP"] as string,
                Descripcion = rd["DesignacionDescripcionSAP"] as string,
                MoldeCodigo = rd["MoldeCodigo"] as string,

                CantidadProgramada = Convert.ToInt32(rd["CantidadProgramada"]),
                CantidadProducida = Convert.ToInt32(rd["CantidadProducida"]),

                Inicio = inicioBloque,
                Fin = finBloque,

                HorasProgramadas = Convert.ToDecimal(rd["HorasProgramadas"]),

                Cambio =
                    rd["Cambio"] == DBNull.Value
                        ? null
                        : (TimeSpan)rd["Cambio"],

                Arranque =
                    rd["Arranque"] == DBNull.Value
                        ? null
                        : (TimeSpan)rd["Arranque"],

                EstatusID = Convert.ToInt32(rd["EstatusID"]),

                EstaEnLinea =
                    rd["EstaEnLinea"] != DBNull.Value &&
                    Convert.ToBoolean(rd["EstaEnLinea"]),

                MaquinaPrincipalID =
                    rd["MaquinaPrincipalID"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(rd["MaquinaPrincipalID"]),

                MaquinaPrincipalCodigo = rd["MaquinaPrincipalCodigo"] as string,
                MaquinaPrincipalNombre = rd["MaquinaPrincipalNombre"] as string,

                MaquinaSustitutaID =
                    rd["MaquinaSustitutaID"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(rd["MaquinaSustitutaID"]),

                MaquinaSustitutaCodigo = rd["MaquinaSustitutaCodigo"] as string,
                MaquinaSustitutaNombre = rd["MaquinaSustitutaNombre"] as string
            });
        }

        foreach (var maquina in maquinas.Values)
        {
            AsignarCarriles(maquina);
        }

        vm.Maquinas = maquinas.Values
            .OrderBy(x => x.Codigo)
            .ToList();

        return View(vm);
    }

    [HttpPost("ReprogramarCalendario")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReprogramarCalendario(
        [FromBody] PlaneacionCalendarioMoverRequest request)
    {
        if (request == null ||
            request.ProgramaProduccionID <= 0 ||
            request.MaquinaID <= 0)
        {
            return Json(new
            {
                ok = false,
                mensaje = "Los datos recibidos para reprogramar son incompletos."
            });
        }

        var usuarioId = ObtenerUsuarioID();

        if (usuarioId <= 0)
        {
            return Json(new
            {
                ok = false,
                mensaje = "No se pudo identificar el usuario de la sesión."
            });
        }

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();

        try
        {
            int? maquinaAnteriorId = null;
            string? maquinaAnteriorCodigo = null;
            int? parteId = null;
            int? moldeId = null;
            int? releaseDetalleId = null;
            int? solicitudProduccionId = null;
            int? solicitudProduccionDetalleId = null;
            int estatusId = 0;
            DateTime inicioAnterior = DateTime.MinValue;
            DateTime finAnterior = DateTime.MinValue;
            decimal horasProduccionAnteriores = 0;
            int cantidadProgramada = 0;
            int cantidadProducida = 0;
            TimeSpan? cambioAnterior = null;
            TimeSpan? arranqueAnterior = null;
            int? maquinaPrincipalId = null;
            int? maquinaSustitutaId = null;
            string? maquinaPrincipalCodigo = null;
            string? maquinaSustitutaCodigo = null;

            const string sqlPrograma = @"
SELECT
    pp.MaquinaID,
    pp.MaquinaCodigo,
    pp.ParteID,
    pp.MoldeID,
    pp.ReleaseDetalleID,
    pp.SolicitudProduccionID,
    pp.SolicitudProduccionDetalleID,
    pp.EstatusID,
    pp.FechaInicioProgramada,
    ISNULL(
        pp.FechaFinProgramada,
        DATEADD(
            MINUTE,
            CONVERT(INT, CEILING(ISNULL(pp.HorasProgramadas, 1) * 60)),
            pp.FechaInicioProgramada
        )
    ) AS FechaFinProgramada,
    ISNULL(pp.HorasProgramadas, 0) AS HorasProgramadas,
    ISNULL(pp.CantidadProgramada, 0) AS CantidadProgramada,
    ISNULL(pp.CantidadProducida, 0) AS CantidadProducida,
    pp.Cambio,
    pp.Arranque,

    t.MaquinaPrincipalID,
    maqPrincipal.Codigo AS MaquinaPrincipalCodigo,

    t.MaquinaSustitutaID,
    maqSustituta.Codigo AS MaquinaSustitutaCodigo

FROM dbo.Planeacion_ProgramaProduccion pp
LEFT JOIN dbo.ERP_ParteDatosTecnicos t
    ON t.ParteID = pp.ParteID
   AND t.Activo = 1
LEFT JOIN dbo.ERP_Maquinas maqPrincipal
    ON maqPrincipal.MaquinaID = t.MaquinaPrincipalID
LEFT JOIN dbo.ERP_Maquinas maqSustituta
    ON maqSustituta.MaquinaID = t.MaquinaSustitutaID
WHERE pp.ProgramaProduccionID = @ProgramaProduccionID
  AND pp.Activo = 1;";

            var programaEncontrado = false;

            await using (var cmd = new SqlCommand(sqlPrograma, cn, tx))
            {
                cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value =
                    request.ProgramaProduccionID;

                await using var rd = await cmd.ExecuteReaderAsync();

                if (await rd.ReadAsync())
                {
                    programaEncontrado = true;

                    maquinaAnteriorId =
                        rd["MaquinaID"] == DBNull.Value
                            ? null
                            : Convert.ToInt32(rd["MaquinaID"]);

                    maquinaAnteriorCodigo = rd["MaquinaCodigo"] as string;

                    parteId =
                        rd["ParteID"] == DBNull.Value
                            ? null
                            : Convert.ToInt32(rd["ParteID"]);

                    moldeId =
                        rd["MoldeID"] == DBNull.Value
                            ? null
                            : Convert.ToInt32(rd["MoldeID"]);

                    releaseDetalleId =
                        rd["ReleaseDetalleID"] == DBNull.Value
                            ? null
                            : Convert.ToInt32(rd["ReleaseDetalleID"]);

                    solicitudProduccionId =
                        rd["SolicitudProduccionID"] == DBNull.Value
                            ? null
                            : Convert.ToInt32(rd["SolicitudProduccionID"]);

                    solicitudProduccionDetalleId =
                        rd["SolicitudProduccionDetalleID"] == DBNull.Value
                            ? null
                            : Convert.ToInt32(rd["SolicitudProduccionDetalleID"]);

                    estatusId = Convert.ToInt32(rd["EstatusID"]);
                    inicioAnterior = Convert.ToDateTime(rd["FechaInicioProgramada"]);
                    finAnterior = Convert.ToDateTime(rd["FechaFinProgramada"]);
                    horasProduccionAnteriores = Convert.ToDecimal(rd["HorasProgramadas"]);
                    cantidadProgramada = Convert.ToInt32(rd["CantidadProgramada"]);
                    cantidadProducida = Convert.ToInt32(rd["CantidadProducida"]);

                    cambioAnterior =
                        rd["Cambio"] == DBNull.Value
                            ? null
                            : (TimeSpan)rd["Cambio"];

                    arranqueAnterior =
                        rd["Arranque"] == DBNull.Value
                            ? null
                            : (TimeSpan)rd["Arranque"];

                    maquinaPrincipalId =
                        rd["MaquinaPrincipalID"] == DBNull.Value
                            ? null
                            : Convert.ToInt32(rd["MaquinaPrincipalID"]);

                    maquinaPrincipalCodigo = rd["MaquinaPrincipalCodigo"] as string;

                    maquinaSustitutaId =
                        rd["MaquinaSustitutaID"] == DBNull.Value
                            ? null
                            : Convert.ToInt32(rd["MaquinaSustitutaID"]);

                    maquinaSustitutaCodigo = rd["MaquinaSustitutaCodigo"] as string;
                }
            }

            if (!programaEncontrado)
            {
                await tx.RollbackAsync();

                return Json(new
                {
                    ok = false,
                    mensaje = "No se encontró el programa de producción."
                });
            }

            var estaEnLinea =
                DateTime.Now >= inicioAnterior &&
                DateTime.Now < finAnterior;

            var yaProducido =
                cantidadProgramada > 0 &&
                cantidadProducida >= cantidadProgramada;

            if (estaEnLinea ||
                yaProducido ||
                estatusId == PlaneacionProgramaEstatus.EnProduccion ||
                estatusId == PlaneacionProgramaEstatus.Terminado ||
                estatusId == PlaneacionProgramaEstatus.Cerrado ||
                estatusId == 99)
            {
                await tx.RollbackAsync();

                return Json(new
                {
                    ok = false,
                    mensaje =
                        "Este programa ya está en línea, producción, producido, terminado o cerrado. " +
                        "Por ahora no puede moverse desde el calendario hasta que quede listo el módulo de Producción."
                });
            }

            string maquinaNuevaCodigo = string.Empty;
            string maquinaNuevaNombre = string.Empty;

            const string sqlMaquina = @"
SELECT TOP (1)
    Codigo,
    Nombre
FROM dbo.ERP_Maquinas
WHERE MaquinaID = @MaquinaID
  AND Activo = 1;";

            var maquinaEncontrada = false;

            await using (var cmd = new SqlCommand(sqlMaquina, cn, tx))
            {
                cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value =
                    request.MaquinaID;

                await using var rd = await cmd.ExecuteReaderAsync();

                if (await rd.ReadAsync())
                {
                    maquinaEncontrada = true;
                    maquinaNuevaCodigo = rd["Codigo"] as string ?? request.MaquinaID.ToString();
                    maquinaNuevaNombre = rd["Nombre"] as string ?? maquinaNuevaCodigo;
                }
            }

            if (!maquinaEncontrada)
            {
                await tx.RollbackAsync();

                return Json(new
                {
                    ok = false,
                    mensaje = "La máquina seleccionada no existe o está inactiva."
                });
            }

            var maquinaCompatible =
                !parteId.HasValue ||
                maquinaPrincipalId == request.MaquinaID ||
                maquinaSustitutaId == request.MaquinaID;

            if (!maquinaCompatible)
            {
                await tx.RollbackAsync();

                var principalTexto = string.IsNullOrWhiteSpace(maquinaPrincipalCodigo)
                    ? "Sin máquina principal configurada"
                    : maquinaPrincipalCodigo;

                var sustitutaTexto = string.IsNullOrWhiteSpace(maquinaSustitutaCodigo)
                    ? "Sin máquina sustituta configurada"
                    : maquinaSustitutaCodigo;

                return Json(new
                {
                    ok = false,
                    requiereConfirmacion = false,
                    maquinaPrincipal = principalTexto,
                    maquinaSustituta = sustitutaTexto,
                    mensaje =
                        $"No puedes mover este programa a la máquina {maquinaNuevaCodigo}, porque no está configurada como máquina principal ni sustituta para esta parte. " +
                        $"Sí puedes moverlo a: Principal: {principalTexto}. Sustituta: {sustitutaTexto}."
                });
            }

            var duracionAnterior = CalcularHorasOperativasCalendario(
                inicioAnterior,
                finAnterior
            );

            var horasProduccionNuevas = request.Redimensionado
                ? Math.Max(0.25m, request.DuracionBloqueHoras)
                : horasProduccionAnteriores;

            if (horasProduccionNuevas <= 0)
                horasProduccionNuevas = duracionAnterior;

            if (horasProduccionNuevas <= 0 || horasProduccionNuevas > 744)
            {
                await tx.RollbackAsync();

                return Json(new
                {
                    ok = false,
                    mensaje = "La duración seleccionada no es válida."
                });
            }

            var lineTime = ObtenerLineTimeCalendario();

            var sugerenciaCola = await ObtenerSiguienteCambioDisponibleCalendarioAsync(
                request.MaquinaID,
                request.ProgramaProduccionID,
                parteId,
                moldeId,
                lineTime,
                horasProduccionNuevas,
                cn,
                tx
            );

            var inicioNuevo = sugerenciaCola.Cambio;
            var arranqueNuevo = sugerenciaCola.Arranque;

            var finNuevo = SumarHorasOperativasCalendario(
                arranqueNuevo,
                horasProduccionNuevas
            );

            if (!request.ConfirmarMovimiento)
            {
                await tx.RollbackAsync();

                return Json(new
                {
                    ok = true,
                    requiereConfirmacion = true,
                    mensaje = "Confirma si deseas aplicar este movimiento.",
                    programaProduccionID = request.ProgramaProduccionID,
                    maquinaID = request.MaquinaID,
                    maquinaCodigo = maquinaNuevaCodigo,
                    maquinaNombre = maquinaNuevaNombre,
                    cambio = inicioNuevo.ToString("yyyy-MM-ddTHH:mm:ss"),
                    arranque = arranqueNuevo.ToString("yyyy-MM-ddTHH:mm:ss"),
                    fin = finNuevo.ToString("yyyy-MM-ddTHH:mm:ss"),
                    cambioTexto = inicioNuevo.ToString("dd/MM/yyyy HH:mm"),
                    arranqueTexto = arranqueNuevo.ToString("dd/MM/yyyy HH:mm"),
                    finTexto = finNuevo.ToString("dd/MM/yyyy HH:mm"),
                    horasProgramadas = Math.Round(horasProduccionNuevas, 2),
                    motivo = sugerenciaCola.Motivo,
                    resumen =
                        $"El programa se moverá a la máquina {maquinaNuevaCodigo}. " +
                        $"Cambio: {inicioNuevo:dd/MM/yyyy HH:mm}. " +
                        $"Arranque: {arranqueNuevo:dd/MM/yyyy HH:mm}. " +
                        $"Fin: {finNuevo:dd/MM/yyyy HH:mm}. " +
                        sugerenciaCola.Motivo
                });
            }

            const string sqlUpdatePrograma = @"
UPDATE dbo.Planeacion_ProgramaProduccion
SET
    MaquinaID = @MaquinaID,
    MaquinaCodigo = @MaquinaCodigo,
    MaquinaNombre = @MaquinaNombre,
    FechaInicioProgramada = @FechaInicio,
    FechaFinProgramada = @FechaFin,
    HorasProgramadas = @HorasProgramadas,
    Cambio = @Cambio,
    Arranque = @Arranque,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE ProgramaProduccionID = @ProgramaProduccionID
  AND Activo = 1;";

            await using (var cmd = new SqlCommand(sqlUpdatePrograma, cn, tx))
            {
                cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value =
                    request.MaquinaID;

                cmd.Parameters.Add("@MaquinaCodigo", SqlDbType.NVarChar, 100).Value =
                    maquinaNuevaCodigo;

                cmd.Parameters.Add("@MaquinaNombre", SqlDbType.NVarChar, 200).Value =
                    maquinaNuevaNombre;

                cmd.Parameters.Add("@FechaInicio", SqlDbType.DateTime).Value =
                    inicioNuevo;

                cmd.Parameters.Add("@FechaFin", SqlDbType.DateTime).Value =
                    finNuevo;

                var horasParam = cmd.Parameters.Add("@HorasProgramadas", SqlDbType.Decimal);
                horasParam.Precision = 18;
                horasParam.Scale = 2;
                horasParam.Value = Math.Round(horasProduccionNuevas, 2);

                cmd.Parameters.Add("@Cambio", SqlDbType.Time).Value =
                    inicioNuevo.TimeOfDay;

                cmd.Parameters.Add("@Arranque", SqlDbType.Time).Value =
                    arranqueNuevo.TimeOfDay;

                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                    usuarioId;

                cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value =
                    request.ProgramaProduccionID;

                await cmd.ExecuteNonQueryAsync();
            }

            if (solicitudProduccionDetalleId.HasValue)
            {
                const string sqlSincronizarDetalle = @"
UPDATE dbo.SolicitudesProduccionDetalle
SET
    MaquinaSugeridaID = @MaquinaID,
    HorasPlaneadas = @HorasProgramadas,
    Cambio = @Cambio,
    Arranque = @Arranque
WHERE SolicitudProduccionDetalleID = @SolicitudProduccionDetalleID;

UPDATE dbo.SolicitudesProduccionAsignacionMaquina
SET
    MaquinaID = @MaquinaID,
    FechaProgramadaTentativa = @FechaProgramada,
    HoraInicioTentativa = @HoraInicio,
    HoraFinTentativa = @HoraFin,
    HorasEstimadas = @HorasProgramadas
WHERE SolicitudProduccionDetalleID = @SolicitudProduccionDetalleID
  AND Activo = 1;";

                await using var cmd = new SqlCommand(sqlSincronizarDetalle, cn, tx);

                cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value =
                    request.MaquinaID;

                var horasParam = cmd.Parameters.Add("@HorasProgramadas", SqlDbType.Decimal);
                horasParam.Precision = 18;
                horasParam.Scale = 2;
                horasParam.Value = Math.Round(horasProduccionNuevas, 2);

                cmd.Parameters.Add("@Cambio", SqlDbType.Time).Value =
                    inicioNuevo.TimeOfDay;

                cmd.Parameters.Add("@Arranque", SqlDbType.Time).Value =
                    arranqueNuevo.TimeOfDay;

                cmd.Parameters.Add("@FechaProgramada", SqlDbType.Date).Value =
                    inicioNuevo.Date;

                cmd.Parameters.Add("@HoraInicio", SqlDbType.Time).Value =
                    inicioNuevo.TimeOfDay;

                cmd.Parameters.Add("@HoraFin", SqlDbType.Time).Value =
                    finNuevo.TimeOfDay;

                cmd.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value =
                    solicitudProduccionDetalleId.Value;

                await cmd.ExecuteNonQueryAsync();
            }

            if (solicitudProduccionId.HasValue)
            {
                const string sqlSincronizarOF = @"
UPDATE dbo.SolicitudesProduccion
SET
    FechaInicioPlaneada = @FechaInicio,
    FechaFinPlaneada = @FechaFin
WHERE SolicitudProduccionID = @SolicitudProduccionID;";

                await using var cmd = new SqlCommand(sqlSincronizarOF, cn, tx);

                cmd.Parameters.Add("@FechaInicio", SqlDbType.DateTime).Value =
                    inicioNuevo;

                cmd.Parameters.Add("@FechaFin", SqlDbType.DateTime).Value =
                    finNuevo;

                cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value =
                    solicitudProduccionId.Value;

                await cmd.ExecuteNonQueryAsync();
            }

            await SincronizarReleaseDesdeReprogramacionAsync(
                releaseDetalleId,
                inicioNuevo,
                finNuevo,
                usuarioId,
                cn,
                tx
            );

            await InsertarHistorialReprogramacionProgramaAsync(
                request.ProgramaProduccionID,
                maquinaAnteriorId,
                request.MaquinaID,
                inicioAnterior,
                inicioNuevo,
                finAnterior,
                finNuevo,
                horasProduccionAnteriores,
                horasProduccionNuevas,
                cambioAnterior,
                inicioNuevo.TimeOfDay,
                arranqueAnterior,
                arranqueNuevo.TimeOfDay,
                releaseDetalleId,
                solicitudProduccionId,
                solicitudProduccionDetalleId,
                usuarioId,
                $"Reprogramación confirmada desde calendario de máquinas. Máquina anterior: {maquinaAnteriorCodigo ?? "sin máquina"}, nueva máquina: {maquinaNuevaCodigo}. {sugerenciaCola.Motivo}",
                cn,
                tx
            );

            await ReordenarSecuenciaMaquinaAsync(
                request.MaquinaID,
                maquinaAnteriorId,
                cn,
                tx
            );

            await tx.CommitAsync();

            return Json(new
            {
                ok = true,
                requiereConfirmacion = false,
                mensaje =
                    $"Programa movido correctamente a la máquina {maquinaNuevaCodigo}. " +
                    $"Cambio: {inicioNuevo:dd/MM/yyyy HH:mm}. " +
                    $"Arranque: {arranqueNuevo:dd/MM/yyyy HH:mm}. " +
                    $"Fin: {finNuevo:dd/MM/yyyy HH:mm}. " +
                    sugerenciaCola.Motivo,
                programaProduccionID = request.ProgramaProduccionID,
                maquinaID = request.MaquinaID,
                maquinaCodigo = maquinaNuevaCodigo,
                maquinaNombre = maquinaNuevaNombre,
                inicio = inicioNuevo.ToString("yyyy-MM-ddTHH:mm:ss"),
                arranque = arranqueNuevo.ToString("yyyy-MM-ddTHH:mm:ss"),
                fin = finNuevo.ToString("yyyy-MM-ddTHH:mm:ss"),
                horasProgramadas = Math.Round(horasProduccionNuevas, 2)
            });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();

            return Json(new
            {
                ok = false,
                mensaje = "No fue posible reprogramar: " + ex.Message
            });
        }
    }

    private async Task SincronizarReleaseDesdeReprogramacionAsync(
        int? releaseDetalleId,
        DateTime inicioNuevo,
        DateTime finNuevo,
        int usuarioId,
        SqlConnection cn,
        SqlTransaction tx)
    {
        if (!releaseDetalleId.HasValue || releaseDetalleId.Value <= 0)
            return;

        DateTime? fechaRequeridaCliente = null;

        const string sqlObtenerReleaseDetalle = @"
SELECT TOP 1
    FechaRequerida
FROM dbo.Planeacion_ReleaseDetalle
WHERE ReleaseDetalleID = @ReleaseDetalleID
  AND Activo = 1;";

        await using (var cmd = new SqlCommand(sqlObtenerReleaseDetalle, cn, tx))
        {
            cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value =
                releaseDetalleId.Value;

            var result = await cmd.ExecuteScalarAsync();

            if (result != null && result != DBNull.Value)
                fechaRequeridaCliente = Convert.ToDateTime(result).Date;
        }

        bool? daTiempo = null;
        string mensajeCapacidad;

        if (fechaRequeridaCliente.HasValue)
        {
            daTiempo = finNuevo.Date <= fechaRequeridaCliente.Value.Date;

            mensajeCapacidad = daTiempo.Value
                ? $"Programación actualizada: termina el {finNuevo:dd/MM/yyyy HH:mm}, dentro de la fecha requerida del cliente ({fechaRequeridaCliente:dd/MM/yyyy})."
                : $"Programación actualizada: termina el {finNuevo:dd/MM/yyyy HH:mm}, posterior a la fecha requerida del cliente ({fechaRequeridaCliente:dd/MM/yyyy}).";
        }
        else
        {
            mensajeCapacidad =
                $"Programación actualizada: inicio {inicioNuevo:dd/MM/yyyy HH:mm}, fin {finNuevo:dd/MM/yyyy HH:mm}.";
        }

        const string sqlActualizarReleaseDetalle = @"
UPDATE dbo.Planeacion_ReleaseDetalle
SET
    DaTiempo = @DaTiempo,
    FechaInicioSugerida = @FechaInicioProgramada,
    FechaFinEstimada = @FechaFinProgramada,
    MensajeCapacidad = @MensajeCapacidad,
    FechaModificacion = GETDATE(),
    UsuarioModificacionID = @UsuarioModificacionID
WHERE ReleaseDetalleID = @ReleaseDetalleID
  AND Activo = 1;";

        await using (var cmd = new SqlCommand(sqlActualizarReleaseDetalle, cn, tx))
        {
            cmd.Parameters.Add("@DaTiempo", SqlDbType.Bit).Value =
                daTiempo.HasValue ? daTiempo.Value : DBNull.Value;

            cmd.Parameters.Add("@FechaInicioProgramada", SqlDbType.DateTime).Value =
                inicioNuevo;

            cmd.Parameters.Add("@FechaFinProgramada", SqlDbType.DateTime).Value =
                finNuevo;

            cmd.Parameters.Add("@MensajeCapacidad", SqlDbType.NVarChar, 500).Value =
                mensajeCapacidad;

            cmd.Parameters.Add("@UsuarioModificacionID", SqlDbType.Int).Value =
                usuarioId;

            cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value =
                releaseDetalleId.Value;

            await cmd.ExecuteNonQueryAsync();
        }

        const string sqlActualizarReleasePadre = @"
UPDATE r
SET
    r.FechaModificacion = GETDATE(),
    r.UsuarioModificacionID = @UsuarioModificacionID
FROM dbo.Planeacion_Releases r
INNER JOIN dbo.Planeacion_ReleaseDetalle d
    ON d.ReleaseID = r.ReleaseID
WHERE d.ReleaseDetalleID = @ReleaseDetalleID
  AND r.Activo = 1;";

        await using (var cmd = new SqlCommand(sqlActualizarReleasePadre, cn, tx))
        {
            cmd.Parameters.Add("@UsuarioModificacionID", SqlDbType.Int).Value =
                usuarioId;

            cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value =
                releaseDetalleId.Value;

            await cmd.ExecuteNonQueryAsync();
        }
    }

    private async Task InsertarHistorialReprogramacionProgramaAsync(
        int programaProduccionId,
        int? maquinaAnteriorId,
        int? maquinaNuevaId,
        DateTime inicioAnterior,
        DateTime inicioNuevo,
        DateTime finAnterior,
        DateTime finNuevo,
        decimal horasAnteriores,
        decimal horasNuevas,
        TimeSpan? cambioAnterior,
        TimeSpan? cambioNuevo,
        TimeSpan? arranqueAnterior,
        TimeSpan? arranqueNuevo,
        int? releaseDetalleId,
        int? solicitudProduccionId,
        int? solicitudProduccionDetalleId,
        int usuarioId,
        string? motivo,
        SqlConnection cn,
        SqlTransaction tx)
    {
        const string sql = @"
IF OBJECT_ID('dbo.Planeacion_ProgramaReprogramacionHistorial', 'U') IS NOT NULL
BEGIN
    INSERT INTO dbo.Planeacion_ProgramaReprogramacionHistorial
    (
        ProgramaProduccionID,
        MaquinaAnteriorID,
        MaquinaNuevaID,
        InicioAnterior,
        InicioNuevo,
        FinAnterior,
        FinNuevo,
        HorasAnteriores,
        HorasNuevas,
        CambioAnterior,
        CambioNuevo,
        ArranqueAnterior,
        ArranqueNuevo,
        ReleaseDetalleID,
        SolicitudProduccionID,
        SolicitudProduccionDetalleID,
        DaTiempoDespues,
        FechaRequeridaCliente,
        UsuarioID,
        FechaCambio,
        Motivo
    )
    SELECT
        @ProgramaProduccionID,
        @MaquinaAnteriorID,
        @MaquinaNuevaID,
        @InicioAnterior,
        @InicioNuevo,
        @FinAnterior,
        @FinNuevo,
        @HorasAnteriores,
        @HorasNuevas,
        @CambioAnterior,
        @CambioNuevo,
        @ArranqueAnterior,
        @ArranqueNuevo,
        @ReleaseDetalleID,
        @SolicitudProduccionID,
        @SolicitudProduccionDetalleID,
        CASE
            WHEN d.FechaRequerida IS NULL THEN NULL
            WHEN CAST(@FinNuevo AS DATE) <= CAST(d.FechaRequerida AS DATE) THEN 1
            ELSE 0
        END,
        CAST(d.FechaRequerida AS DATE),
        @UsuarioID,
        GETDATE(),
        @Motivo
    FROM (SELECT 1 AS X) base
    LEFT JOIN dbo.Planeacion_ReleaseDetalle d
        ON d.ReleaseDetalleID = @ReleaseDetalleID;
END;";

        await using var cmd = new SqlCommand(sql, cn, tx);

        cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value =
            programaProduccionId;

        cmd.Parameters.Add("@MaquinaAnteriorID", SqlDbType.Int).Value =
            (object?)maquinaAnteriorId ?? DBNull.Value;

        cmd.Parameters.Add("@MaquinaNuevaID", SqlDbType.Int).Value =
            (object?)maquinaNuevaId ?? DBNull.Value;

        cmd.Parameters.Add("@InicioAnterior", SqlDbType.DateTime).Value =
            inicioAnterior;

        cmd.Parameters.Add("@InicioNuevo", SqlDbType.DateTime).Value =
            inicioNuevo;

        cmd.Parameters.Add("@FinAnterior", SqlDbType.DateTime).Value =
            finAnterior;

        cmd.Parameters.Add("@FinNuevo", SqlDbType.DateTime).Value =
            finNuevo;

        var horasAnterioresParam = cmd.Parameters.Add("@HorasAnteriores", SqlDbType.Decimal);
        horasAnterioresParam.Precision = 18;
        horasAnterioresParam.Scale = 2;
        horasAnterioresParam.Value = Math.Round(horasAnteriores, 2);

        var horasNuevasParam = cmd.Parameters.Add("@HorasNuevas", SqlDbType.Decimal);
        horasNuevasParam.Precision = 18;
        horasNuevasParam.Scale = 2;
        horasNuevasParam.Value = Math.Round(horasNuevas, 2);

        cmd.Parameters.Add("@CambioAnterior", SqlDbType.Time).Value =
            (object?)cambioAnterior ?? DBNull.Value;

        cmd.Parameters.Add("@CambioNuevo", SqlDbType.Time).Value =
            (object?)cambioNuevo ?? DBNull.Value;

        cmd.Parameters.Add("@ArranqueAnterior", SqlDbType.Time).Value =
            (object?)arranqueAnterior ?? DBNull.Value;

        cmd.Parameters.Add("@ArranqueNuevo", SqlDbType.Time).Value =
            (object?)arranqueNuevo ?? DBNull.Value;

        cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value =
            (object?)releaseDetalleId ?? DBNull.Value;

        cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value =
            (object?)solicitudProduccionId ?? DBNull.Value;

        cmd.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value =
            (object?)solicitudProduccionDetalleId ?? DBNull.Value;

        cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
            usuarioId;

        cmd.Parameters.Add("@Motivo", SqlDbType.NVarChar, 500).Value =
            string.IsNullOrWhiteSpace(motivo)
                ? DBNull.Value
                : motivo.Trim();

        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task ReordenarSecuenciaMaquinaAsync(
        int maquinaNuevaId,
        int? maquinaAnteriorId,
        SqlConnection cn,
        SqlTransaction tx)
    {
        const string sql = @"
IF COL_LENGTH('dbo.Planeacion_ProgramaProduccion', 'SecuenciaMaquina') IS NOT NULL
BEGIN
    ;WITH OrdenMaquinas AS
    (
        SELECT
            ProgramaProduccionID,
            ROW_NUMBER() OVER
            (
                PARTITION BY MaquinaID
                ORDER BY FechaInicioProgramada, ProgramaProduccionID
            ) AS NuevaSecuencia
        FROM dbo.Planeacion_ProgramaProduccion
        WHERE Activo = 1
          AND ISNULL(EstatusID, 1) NOT IN (5, 9, 99)
          AND
          (
                MaquinaID = @MaquinaNuevaID
             OR (@MaquinaAnteriorID IS NOT NULL AND MaquinaID = @MaquinaAnteriorID)
          )
    )
    UPDATE pp
    SET SecuenciaMaquina = om.NuevaSecuencia
    FROM dbo.Planeacion_ProgramaProduccion pp
    INNER JOIN OrdenMaquinas om
        ON om.ProgramaProduccionID = pp.ProgramaProduccionID;
END;";

        await using var cmd = new SqlCommand(sql, cn, tx);

        cmd.Parameters.Add("@MaquinaNuevaID", SqlDbType.Int).Value =
            maquinaNuevaId;

        cmd.Parameters.Add("@MaquinaAnteriorID", SqlDbType.Int).Value =
            (object?)maquinaAnteriorId ?? DBNull.Value;

        await cmd.ExecuteNonQueryAsync();
    }

    private static void AsignarCarriles(PlaneacionCalendarioMaquinaVm maquina)
    {
        var finales = new List<DateTime>();

        foreach (var bloque in maquina.Bloques
            .OrderBy(x => x.Inicio)
            .ThenBy(x => x.Fin)
            .ThenBy(x => x.ProgramaProduccionID))
        {
            var carril = -1;

            for (var i = 0; i < finales.Count; i++)
            {
                if (finales[i] <= bloque.Inicio)
                {
                    carril = i;
                    break;
                }
            }

            if (carril < 0)
            {
                carril = finales.Count;
                finales.Add(bloque.Fin);
            }
            else
            {
                finales[carril] = bloque.Fin;
            }

            bloque.Carril = carril;
        }

        maquina.Carriles = Math.Max(1, finales.Count);
    }

    private int ObtenerUsuarioID()
    {
        var claimValue =
            User.FindFirst("UsuarioID")?.Value ??
            User.FindFirst("UserId")?.Value ??
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (int.TryParse(claimValue, out var usuarioId) && usuarioId > 0)
            return usuarioId;

        try
        {
            var sessionId = HttpContext.Session.GetInt32("UsuarioID");

            if (sessionId.HasValue && sessionId.Value > 0)
                return sessionId.Value;
        }
        catch
        {
            // Si Session no está disponible, se deja en 0.
        }

        return 0;
    }

    private sealed class CambioMoldeSugerenciaCalendario
    {
        public DateTime Cambio { get; set; }
        public DateTime Arranque { get; set; }
        public bool OmiteHoraCambio { get; set; }
        public string Motivo { get; set; } = string.Empty;
    }

    private sealed class UltimoProgramaMaquinaCalendario
    {
        public DateTime Fin { get; set; }
        public int? ParteID { get; set; }
        public int? MoldeID { get; set; }
        public string ParteTexto { get; set; } = "la pieza anterior";
        public string MoldeTexto { get; set; } = "el molde anterior";
    }

    private sealed class UltimoUsoMoldeCalendario
    {
        public DateTime Fin { get; set; }
        public int MaquinaID { get; set; }
        public string MaquinaCodigo { get; set; } = string.Empty;
        public int? ParteID { get; set; }
        public string ParteTexto { get; set; } = "la pieza anterior";
        public string MoldeTexto { get; set; } = "el molde";
    }

    private static async Task<CambioMoldeSugerenciaCalendario> ObtenerSiguienteCambioDisponibleCalendarioAsync(
        int maquinaId,
        int programaProduccionId,
        int? parteId,
        int? moldeId,
        DateTime fechaBase,
        decimal horasProduccion,
        SqlConnection cn,
        SqlTransaction tx)
    {
        var baseTrabajo = fechaBase;

        if (!EsInstanteOperativoCalendario(baseTrabajo))
            baseTrabajo = SiguienteAperturaOperativa(baseTrabajo);

        if (horasProduccion <= 0)
            horasProduccion = 0.25m;

        for (var intento = 0; intento < 300; intento++)
        {
            var ultimo = await ObtenerUltimoProgramaMaquinaCalendarioAsync(
                maquinaId,
                programaProduccionId,
                cn,
                tx
            );

            var ultimoMolde = moldeId.HasValue
                ? await ObtenerUltimoUsoMoldeCalendarioAsync(
                    moldeId.Value,
                    programaProduccionId,
                    cn,
                    tx
                )
                : null;

            var cambio = baseTrabajo;

            if (ultimo != null && ultimo.Fin > cambio)
                cambio = ultimo.Fin;

            if (ultimoMolde != null && ultimoMolde.Fin > cambio)
                cambio = ultimoMolde.Fin;

            cambio = RedondearSiguienteBloque(cambio, 15);

            if (!EsInstanteOperativoCalendario(cambio))
                cambio = SiguienteAperturaOperativa(cambio);

            var mismaParte =
                ultimo != null &&
                parteId.HasValue &&
                ultimo.ParteID.HasValue &&
                parteId.Value == ultimo.ParteID.Value;

            var mismoMolde =
                ultimo != null &&
                moldeId.HasValue &&
                ultimo.MoldeID.HasValue &&
                moldeId.Value == ultimo.MoldeID.Value;

            var omiteCambio = mismaParte || mismoMolde;

            var arranque = omiteCambio
                ? cambio
                : SumarHorasOperativasCalendario(cambio, 1);

            if (!omiteCambio)
            {
                var cambioOcupado = await CambioMoldeTieneCruceCalendarioAsync(
                    programaProduccionId,
                    cambio,
                    cn,
                    tx
                );

                if (cambioOcupado)
                {
                    baseTrabajo = cambio.AddHours(1);

                    if (!EsInstanteOperativoCalendario(baseTrabajo))
                        baseTrabajo = SiguienteAperturaOperativa(baseTrabajo);

                    continue;
                }
            }

            var finProduccion = SumarHorasOperativasCalendario(
                arranque,
                horasProduccion
            );

            var finCruceMaquina = await ObtenerFinCruceMaquinaCalendarioAsync(
                maquinaId,
                programaProduccionId,
                cambio,
                finProduccion,
                cn,
                tx
            );

            if (finCruceMaquina.HasValue)
            {
                baseTrabajo = finCruceMaquina.Value;

                if (!EsInstanteOperativoCalendario(baseTrabajo))
                    baseTrabajo = SiguienteAperturaOperativa(baseTrabajo);

                continue;
            }

            if (moldeId.HasValue)
            {
                var finCruceMolde = await ObtenerFinCruceMoldeCalendarioAsync(
                    moldeId.Value,
                    programaProduccionId,
                    cambio,
                    finProduccion,
                    cn,
                    tx
                );

                if (finCruceMolde.HasValue)
                {
                    baseTrabajo = finCruceMolde.Value;

                    if (!EsInstanteOperativoCalendario(baseTrabajo))
                        baseTrabajo = SiguienteAperturaOperativa(baseTrabajo);

                    continue;
                }
            }

            if (mismaParte)
            {
                return new CambioMoldeSugerenciaCalendario
                {
                    Cambio = cambio,
                    Arranque = arranque,
                    OmiteHoraCambio = true,
                    Motivo = $"La máquina continúa con la misma pieza ({ultimo!.ParteTexto}); se omite la hora de cambio."
                };
            }

            if (mismoMolde)
            {
                return new CambioMoldeSugerenciaCalendario
                {
                    Cambio = cambio,
                    Arranque = arranque,
                    OmiteHoraCambio = true,
                    Motivo = $"La máquina conserva el mismo molde ({ultimo!.MoldeTexto}); se omite la hora de cambio."
                };
            }

            return new CambioMoldeSugerenciaCalendario
            {
                Cambio = cambio,
                Arranque = arranque,
                OmiteHoraCambio = false,
                Motivo =
                    ultimoMolde != null && ultimo == null
                        ? $"La máquina destino no tenía cola activa, pero el molde {ultimoMolde.MoldeTexto} estaba ocupado previamente en {ultimoMolde.MaquinaCodigo}. Se colocó después del último uso del molde y se considera 1 hora de preparación."
                        : ultimoMolde != null
                            ? $"Se colocó al final de la cola válida considerando también el último uso del molde {ultimoMolde.MoldeTexto}. Se considera 1 hora de preparación entre cambio y arranque."
                            : ultimo == null
                                ? "La máquina destino no tenía cola activa. Se colocó en el line time actual y se considera 1 hora de preparación."
                                : "Se colocó al final de la cola válida. Se considera 1 hora de preparación entre cambio y arranque."
            };
        }

        throw new InvalidOperationException(
            "No fue posible encontrar un espacio válido en la cola de la máquina."
        );
    }

    private static async Task<UltimoProgramaMaquinaCalendario?> ObtenerUltimoProgramaMaquinaCalendarioAsync(
        int maquinaId,
        int programaProduccionId,
        SqlConnection cn,
        SqlTransaction tx)
    {
        const string sql = @"
SELECT TOP (1)
    pp.ParteID,
    pp.NumeroParte,
    pp.ReferenciaSAP,
    pp.MoldeID,
    pp.MoldeCodigo,
    ISNULL(
        pp.FechaFinProgramada,
        DATEADD(
            MINUTE,
            CONVERT(INT, CEILING(ISNULL(pp.HorasProgramadas, 1) * 60)),
            pp.FechaInicioProgramada
        )
    ) AS FechaFinProgramada
FROM dbo.Planeacion_ProgramaProduccion pp
WHERE pp.Activo = 1
  AND pp.MaquinaID = @MaquinaID
  AND pp.ProgramaProduccionID <> @ProgramaProduccionID
  AND ISNULL(pp.EstatusID, 1) NOT IN (5, 9, 99)
  AND pp.FechaInicioProgramada IS NOT NULL
ORDER BY
    ISNULL(
        pp.FechaFinProgramada,
        DATEADD(
            MINUTE,
            CONVERT(INT, CEILING(ISNULL(pp.HorasProgramadas, 1) * 60)),
            pp.FechaInicioProgramada
        )
    ) DESC,
    pp.ProgramaProduccionID DESC;";

        await using var cmd = new SqlCommand(sql, cn, tx);

        cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = maquinaId;
        cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;

        await using var rd = await cmd.ExecuteReaderAsync();

        if (!await rd.ReadAsync())
            return null;

        return new UltimoProgramaMaquinaCalendario
        {
            Fin = Convert.ToDateTime(rd["FechaFinProgramada"]),

            ParteID =
                rd["ParteID"] == DBNull.Value
                    ? null
                    : Convert.ToInt32(rd["ParteID"]),

            MoldeID =
                rd["MoldeID"] == DBNull.Value
                    ? null
                    : Convert.ToInt32(rd["MoldeID"]),

            ParteTexto =
                (rd["ReferenciaSAP"] as string) ??
                (rd["NumeroParte"] as string) ??
                "la pieza anterior",

            MoldeTexto =
                (rd["MoldeCodigo"] as string) ??
                "el molde anterior"
        };
    }

    private static async Task<UltimoUsoMoldeCalendario?> ObtenerUltimoUsoMoldeCalendarioAsync(
        int moldeId,
        int programaProduccionId,
        SqlConnection cn,
        SqlTransaction tx)
    {
        const string sql = @"
SELECT TOP (1)
    pp.MaquinaID,
    pp.MaquinaCodigo,
    pp.ParteID,
    pp.NumeroParte,
    pp.ReferenciaSAP,
    pp.MoldeCodigo,
    ISNULL(
        pp.FechaFinProgramada,
        DATEADD(
            MINUTE,
            CONVERT(INT, CEILING(ISNULL(pp.HorasProgramadas, 1) * 60)),
            pp.FechaInicioProgramada
        )
    ) AS FechaFinProgramada
FROM dbo.Planeacion_ProgramaProduccion pp
WHERE pp.Activo = 1
  AND pp.MoldeID = @MoldeID
  AND pp.ProgramaProduccionID <> @ProgramaProduccionID
  AND ISNULL(pp.EstatusID, 1) NOT IN (5, 9, 99)
  AND pp.FechaInicioProgramada IS NOT NULL
ORDER BY
    ISNULL(
        pp.FechaFinProgramada,
        DATEADD(
            MINUTE,
            CONVERT(INT, CEILING(ISNULL(pp.HorasProgramadas, 1) * 60)),
            pp.FechaInicioProgramada
        )
    ) DESC,
    pp.ProgramaProduccionID DESC;";

        await using var cmd = new SqlCommand(sql, cn, tx);

        cmd.Parameters.Add("@MoldeID", SqlDbType.Int).Value = moldeId;
        cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value =
            programaProduccionId;

        await using var rd = await cmd.ExecuteReaderAsync();

        if (!await rd.ReadAsync())
            return null;

        return new UltimoUsoMoldeCalendario
        {
            Fin = Convert.ToDateTime(rd["FechaFinProgramada"]),

            MaquinaID =
                rd["MaquinaID"] == DBNull.Value
                    ? 0
                    : Convert.ToInt32(rd["MaquinaID"]),

            MaquinaCodigo =
                rd["MaquinaCodigo"] as string ?? string.Empty,

            ParteID =
                rd["ParteID"] == DBNull.Value
                    ? null
                    : Convert.ToInt32(rd["ParteID"]),

            ParteTexto =
                (rd["ReferenciaSAP"] as string) ??
                (rd["NumeroParte"] as string) ??
                "la pieza anterior",

            MoldeTexto =
                (rd["MoldeCodigo"] as string) ??
                "el molde"
        };
    }

    private static async Task<bool> CambioMoldeTieneCruceCalendarioAsync(
        int programaProduccionId,
        DateTime fechaCambio,
        SqlConnection cn,
        SqlTransaction tx)
    {
        const string sql = @"
SELECT TOP (1) 1
FROM dbo.Planeacion_ProgramaProduccion pp
WHERE pp.Activo = 1
  AND pp.ProgramaProduccionID <> @ProgramaProduccionID
  AND ISNULL(pp.EstatusID, 1) NOT IN (5, 9, 99)
  AND pp.Cambio IS NOT NULL
  AND pp.Arranque IS NOT NULL
  AND pp.Cambio <> pp.Arranque
  AND CAST(pp.FechaInicioProgramada AS DATE) = CAST(@FechaCambio AS DATE)
  AND DATEPART(HOUR, pp.FechaInicioProgramada) = DATEPART(HOUR, @FechaCambio);";

        await using var cmd = new SqlCommand(sql, cn, tx);

        cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value =
            programaProduccionId;

        cmd.Parameters.Add("@FechaCambio", SqlDbType.DateTime).Value =
            fechaCambio;

        var result = await cmd.ExecuteScalarAsync();

        return result != null && result != DBNull.Value;
    }

    private static async Task<DateTime?> ObtenerFinCruceMaquinaCalendarioAsync(
        int maquinaId,
        int programaProduccionId,
        DateTime inicio,
        DateTime fin,
        SqlConnection cn,
        SqlTransaction tx)
    {
        const string sql = @"
SELECT TOP (1)
    ISNULL(
        pp.FechaFinProgramada,
        DATEADD(
            MINUTE,
            CONVERT(INT, CEILING(ISNULL(pp.HorasProgramadas, 1) * 60)),
            pp.FechaInicioProgramada
        )
    ) AS FechaFinProgramada
FROM dbo.Planeacion_ProgramaProduccion pp
WHERE pp.Activo = 1
  AND pp.MaquinaID = @MaquinaID
  AND pp.ProgramaProduccionID <> @ProgramaProduccionID
  AND ISNULL(pp.EstatusID, 1) NOT IN (5, 9, 99)
  AND pp.FechaInicioProgramada < @Fin
  AND ISNULL(
        pp.FechaFinProgramada,
        DATEADD(
            MINUTE,
            CONVERT(INT, CEILING(ISNULL(pp.HorasProgramadas, 1) * 60)),
            pp.FechaInicioProgramada
        )
      ) > @Inicio
ORDER BY pp.FechaInicioProgramada;";

        await using var cmd = new SqlCommand(sql, cn, tx);

        cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value =
            maquinaId;

        cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value =
            programaProduccionId;

        cmd.Parameters.Add("@Inicio", SqlDbType.DateTime).Value =
            inicio;

        cmd.Parameters.Add("@Fin", SqlDbType.DateTime).Value =
            fin;

        var result = await cmd.ExecuteScalarAsync();

        return result == null || result == DBNull.Value
            ? null
            : Convert.ToDateTime(result);
    }

    private static async Task<DateTime?> ObtenerFinCruceMoldeCalendarioAsync(
        int moldeId,
        int programaProduccionId,
        DateTime inicio,
        DateTime fin,
        SqlConnection cn,
        SqlTransaction tx)
    {
        const string sql = @"
SELECT TOP (1)
    ISNULL(
        pp.FechaFinProgramada,
        DATEADD(
            MINUTE,
            CONVERT(INT, CEILING(ISNULL(pp.HorasProgramadas, 1) * 60)),
            pp.FechaInicioProgramada
        )
    ) AS FechaFinProgramada
FROM dbo.Planeacion_ProgramaProduccion pp
WHERE pp.Activo = 1
  AND pp.MoldeID = @MoldeID
  AND pp.ProgramaProduccionID <> @ProgramaProduccionID
  AND ISNULL(pp.EstatusID, 1) NOT IN (5, 9, 99)
  AND pp.FechaInicioProgramada < @Fin
  AND ISNULL(
        pp.FechaFinProgramada,
        DATEADD(
            MINUTE,
            CONVERT(INT, CEILING(ISNULL(pp.HorasProgramadas, 1) * 60)),
            pp.FechaInicioProgramada
        )
      ) > @Inicio
ORDER BY pp.FechaInicioProgramada;";

        await using var cmd = new SqlCommand(sql, cn, tx);

        cmd.Parameters.Add("@MoldeID", SqlDbType.Int).Value =
            moldeId;

        cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value =
            programaProduccionId;

        cmd.Parameters.Add("@Inicio", SqlDbType.DateTime).Value =
            inicio;

        cmd.Parameters.Add("@Fin", SqlDbType.DateTime).Value =
            fin;

        var result = await cmd.ExecuteScalarAsync();

        return result == null || result == DBNull.Value
            ? null
            : Convert.ToDateTime(result);
    }

    private static DateTime ObtenerLineTimeCalendario()
    {
        var lineTime = RedondearSiguienteBloque(DateTime.Now, 15);

        if (!EsInstanteOperativoCalendario(lineTime))
            lineTime = SiguienteAperturaOperativa(lineTime);

        return lineTime;
    }

    private static DateTime RedondearSiguienteBloque(DateTime fecha, int minutos)
    {
        if (minutos <= 0)
            minutos = 15;

        var bloqueTicks = TimeSpan.FromMinutes(minutos).Ticks;

        var ticks = fecha.Ticks % bloqueTicks == 0
            ? fecha.Ticks
            : fecha.Ticks + (bloqueTicks - fecha.Ticks % bloqueTicks);

        var redondeada = new DateTime(ticks);

        return new DateTime(
            redondeada.Year,
            redondeada.Month,
            redondeada.Day,
            redondeada.Hour,
            redondeada.Minute,
            0
        );
    }

    private static bool EsInstanteOperativoCalendario(DateTime fecha)
    {
        return ObtenerIntervaloOperativoDia(fecha, out var apertura, out var cierre)
            && fecha >= apertura
            && fecha < cierre;
    }

    private static decimal CalcularHorasOperativasCalendario(
        DateTime inicio,
        DateTime fin)
    {
        if (fin <= inicio)
            return 0;

        decimal total = 0;
        var dia = inicio.Date;

        while (dia <= fin.Date)
        {
            if (ObtenerIntervaloOperativoDia(dia, out var apertura, out var cierre))
            {
                var desde = inicio > apertura ? inicio : apertura;
                var hasta = fin < cierre ? fin : cierre;

                if (hasta > desde)
                    total += (decimal)(hasta - desde).TotalHours;
            }

            dia = dia.AddDays(1);
        }

        return Math.Round(total, 4);
    }

    private static DateTime SumarHorasOperativasCalendario(
        DateTime inicio,
        decimal horas)
    {
        if (horas <= 0)
            return inicio;

        var actual = inicio;
        var restante = horas;

        while (restante > 0)
        {
            if (!ObtenerIntervaloOperativoDia(actual, out var apertura, out var cierre))
            {
                actual = SiguienteAperturaOperativa(actual);
                continue;
            }

            if (actual < apertura)
                actual = apertura;

            if (actual >= cierre)
            {
                actual = SiguienteAperturaOperativa(cierre.AddMinutes(1));
                continue;
            }

            var disponibles = (decimal)(cierre - actual).TotalHours;

            if (disponibles >= restante)
                return actual.AddHours((double)restante);

            restante -= disponibles;
            actual = SiguienteAperturaOperativa(cierre.AddMinutes(1));
        }

        return actual;
    }

    private static DateTime SiguienteAperturaOperativa(DateTime fecha)
    {
        var actual = fecha;

        for (var i = 0; i < 14; i++)
        {
            if (ObtenerIntervaloOperativoDia(actual, out var apertura, out var cierre))
            {
                if (actual < apertura)
                    return apertura;

                if (actual >= apertura && actual < cierre)
                    return actual;
            }

            actual = actual.Date.AddDays(1);
        }

        return fecha;
    }

    private static bool ObtenerIntervaloOperativoDia(
        DateTime fecha,
        out DateTime apertura,
        out DateTime cierre)
    {
        var dia = fecha.Date;

        apertura = dia;
        cierre = dia;

        if (fecha.DayOfWeek == DayOfWeek.Sunday)
            return false;

        if (fecha.DayOfWeek == DayOfWeek.Monday)
        {
            apertura = dia.AddHours(7);
            cierre = dia.AddDays(1);
            return true;
        }

        if (fecha.DayOfWeek == DayOfWeek.Tuesday ||
            fecha.DayOfWeek == DayOfWeek.Wednesday ||
            fecha.DayOfWeek == DayOfWeek.Thursday ||
            fecha.DayOfWeek == DayOfWeek.Friday)
        {
            apertura = dia;
            cierre = dia.AddDays(1);
            return true;
        }

        if (fecha.DayOfWeek == DayOfWeek.Saturday)
        {
            apertura = dia;
            cierre = dia.AddHours(15);
            return true;
        }

        return false;
    }
}

public sealed class PlaneacionCalendarioMoverRequest
{
    public int ProgramaProduccionID { get; set; }

    public int MaquinaID { get; set; }

    public DateTime Inicio { get; set; }

    public decimal DuracionBloqueHoras { get; set; }

    public bool Redimensionado { get; set; }

    public bool ForzarMaquina { get; set; }

    public bool ConfirmarMovimiento { get; set; }
}