using ERP.NSQuell.Models;
using ERP.NSQuell.Models.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace ERP.NSQuell.Controllers
{
    public partial class CalidadController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _configuration;

        private static readonly string[] EstadosProcesoActivo =
        {
            CalidadEstados.PendientePrearranque,
            CalidadEstados.DevueltoPrearranque,
            CalidadEstados.ArranqueAutorizado,
            CalidadEstados.PendientePrimerasPiezas,
            CalidadEstados.AjustesSolicitados,
            CalidadEstados.ProduccionLiberada,
            CalidadEstados.MonitoreoActivo,
            CalidadEstados.PendienteLiberacionCaja,
            CalidadEstados.PendienteReliberacion,
            CalidadEstados.PendienteGP12,
            CalidadEstados.EnGP12,
            CalidadEstados.LegacyAbierta,
            CalidadEstados.LegacyDetenida,
            CalidadEstados.LegacyGPI2
        };

        public CalidadController(
            ApplicationDbContext context,
            IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        private string ConnectionString =>
            _configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "No se encontró la cadena de conexión DefaultConnection."
            );

        // =========================================================
        // BANDEJA PRINCIPAL
        // =========================================================

        [HttpGet]
        public async Task<IActionResult> Index(
            string? busqueda,
            string? estadoFiltro)
        {
            busqueda = busqueda?.Trim();
            estadoFiltro = estadoFiltro?
                .Trim()
                .ToUpperInvariant();

            var baseQuery = _context.CalidadInspecciones
                .AsNoTracking();

            var query = baseQuery;

            if (!string.IsNullOrWhiteSpace(busqueda))
            {
                query = query.Where(x =>
                    (x.CodigoBarras != null &&
                     x.CodigoBarras.Contains(busqueda)) ||

                    (x.OrdenTrabajo != null &&
                     x.OrdenTrabajo.Contains(busqueda)) ||

                    (x.ClienteNombre != null &&
                     x.ClienteNombre.Contains(busqueda)) ||

                    (x.NumeroParte != null &&
                     x.NumeroParte.Contains(busqueda)) ||

                    (x.Material != null &&
                     x.Material.Contains(busqueda)) ||

                    (x.Proceso != null &&
                     x.Proceso.Contains(busqueda)) ||

                    (x.Maquina != null &&
                     x.Maquina.Contains(busqueda)) ||

                    (x.Molde != null &&
                     x.Molde.Contains(busqueda)) ||

                    (x.OperadorPrincipalNombre != null &&
                     x.OperadorPrincipalNombre.Contains(busqueda))
                );
            }

            if (!string.IsNullOrWhiteSpace(estadoFiltro) &&
                estadoFiltro != CalidadEstados.PendienteLiberacionCaja)
            {
                query = query.Where(x =>
                    x.Estado == estadoFiltro);
            }

            var model = new CalidadIndexViewModel
            {
                Busqueda = busqueda,
                EstadoFiltro = estadoFiltro,

                TotalPendientePrearranque =
                    await baseQuery.CountAsync(x =>
                        x.Estado ==
                        CalidadEstados.PendientePrearranque),

                TotalDevueltoPrearranque =
                    await baseQuery.CountAsync(x =>
                        x.Estado ==
                        CalidadEstados.DevueltoPrearranque),

                TotalArranqueAutorizado =
                    await baseQuery.CountAsync(x =>
                        x.Estado ==
                        CalidadEstados.ArranqueAutorizado),

                TotalPendientePrimerasPiezas =
                    await baseQuery.CountAsync(x =>
                        x.Estado ==
                        CalidadEstados.PendientePrimerasPiezas),

                TotalAjustesSolicitados =
                    await baseQuery.CountAsync(x =>
                        x.Estado ==
                        CalidadEstados.AjustesSolicitados),

                TotalProduccionLiberada =
                    await baseQuery.CountAsync(x =>
                        x.Estado ==
                        CalidadEstados.ProduccionLiberada),

                TotalMonitoreoActivo =
                    await baseQuery.CountAsync(x =>
                        x.Estado ==
                        CalidadEstados.MonitoreoActivo),

                // Este total se carga desde Produccion_Cajas, no desde
                // el estado general de Calidad_Inspecciones.
                TotalPendienteLiberacionCaja = 0,

                TotalPendienteReliberacion =
                    await baseQuery.CountAsync(x =>
                        x.Estado ==
                        CalidadEstados.PendienteReliberacion),

                TotalPendienteGP12 =
                    await baseQuery.CountAsync(x =>
                        x.Estado ==
                        CalidadEstados.PendienteGP12),

                TotalEnGP12 =
                    await baseQuery.CountAsync(x =>
                        x.Estado ==
                        CalidadEstados.EnGP12),

                TotalMaterialNoConforme =
                    await baseQuery.CountAsync(x =>
                        x.Estado ==
                        CalidadEstados.MaterialNoConforme),

                TotalCerradas =
                    await baseQuery.CountAsync(x =>
                        x.Estado ==
                        CalidadEstados.Cerrada),

                /*
                 * Totales de compatibilidad para el Index anterior.
                 */
                TotalAbiertas =
                    await baseQuery.CountAsync(x =>
                        x.Estado ==
                            CalidadEstados.LegacyAbierta ||
                        x.Estado ==
                            CalidadEstados.PendientePrearranque ||
                        x.Estado ==
                            CalidadEstados.DevueltoPrearranque ||
                        x.Estado ==
                            CalidadEstados.ArranqueAutorizado ||
                        x.Estado ==
                            CalidadEstados.PendientePrimerasPiezas ||
                        x.Estado ==
                            CalidadEstados.AjustesSolicitados ||
                        x.Estado ==
                            CalidadEstados.ProduccionLiberada ||
                        x.Estado ==
                            CalidadEstados.MonitoreoActivo ||
                        x.Estado ==
                            CalidadEstados.PendienteLiberacionCaja ||
                        x.Estado ==
                            CalidadEstados.PendienteReliberacion),

                TotalLiberadas =
                    await baseQuery.CountAsync(x =>
                        x.Estado ==
                            CalidadEstados.LegacyLiberada ||
                        x.Estado ==
                            CalidadEstados.ProduccionLiberada ||
                        x.Estado ==
                            CalidadEstados.MaterialLiberado),

                TotalGPI2 =
                    await baseQuery.CountAsync(x =>
                        x.Estado ==
                            CalidadEstados.LegacyGPI2 ||
                        x.Estado ==
                            CalidadEstados.PendienteGP12 ||
                        x.Estado ==
                            CalidadEstados.EnGP12),

                TotalContencion =
                    await baseQuery.CountAsync(x =>
                        x.Estado ==
                            CalidadEstados.LegacyContencion ||
                        x.Estado ==
                            CalidadEstados.MaterialNoConforme ||
                        x.EnContencion),

                TotalScrap =
                    await baseQuery.CountAsync(x =>
                        x.Estado ==
                            CalidadEstados.LegacyScrap ||
                        x.EsScrap)
            };

            model.Inspecciones = await query
                .OrderByDescending(x =>
                    x.FechaNotificacionCalidad ??
                    x.FechaCreacion)
                .Select(x =>
                    new CalidadListadoItemViewModel
                    {
                        InspeccionID =
                            x.InspeccionID,

                        ProgramaProduccionID =
                            x.ProgramaProduccionID,

                        EjecucionProduccionID =
                            x.EjecucionProduccionID,

                        ChecklistArranqueID =
                            x.ChecklistArranqueID,

                        SolicitudProduccionID =
                            x.SolicitudProduccionID,

                        SolicitudProduccionDetalleID =
                            x.SolicitudProduccionDetalleID,

                        ReleaseID =
                            x.ReleaseID,

                        ReleaseDetalleID =
                            x.ReleaseDetalleID,

                        CodigoBarras =
                            x.CodigoBarras,

                        OrdenTrabajo =
                            x.OrdenTrabajo,

                        ClienteNombre =
                            x.ClienteNombre,

                        NumeroParte =
                            x.NumeroParte,

                        Material =
                            x.Material,

                        Proceso =
                            x.Proceso,

                        Maquina =
                            x.Maquina,

                        Molde =
                            x.Molde,

                        OperadorPrincipalNombre =
                            x.OperadorPrincipalNombre,

                        OperadorAuxiliarNombre =
                            x.OperadorAuxiliarNombre,

                        TecnicoInyeccionNombre =
                            x.TecnicoInyeccionNombre,

                        CantidadTotal =
                            x.CantidadTotal,

                        CantidadRevisada =
                            x.CantidadRevisada,

                        CantidadPendiente =
                            x.CantidadPendiente,

                        FechaInicioProgramada =
                            x.FechaInicioProgramada,

                        FechaFinProgramada =
                            x.FechaFinProgramada,

                        FechaNotificacionCalidad =
                            x.FechaNotificacionCalidad,

                        FechaLiberacionProduccion =
                            x.FechaLiberacionProduccion,

                        ResultadoCalidad =
                            x.ResultadoCalidad,

                        Etiqueta =
                            x.Etiqueta,

                        Estado =
                            x.Estado,

                        RequiereReliberacion =
                            x.RequiereReliberacion,

                        ConfiguracionInvalidada =
                            x.ConfiguracionInvalidada,

                        MotivoInvalidacion =
                            x.MotivoInvalidacion,

                        FechaCreacion =
                            x.FechaCreacion
                    }
                )
                .ToListAsync();

            model.TotalMostrados =
                model.Inspecciones.Count;

            model.CajasPendientes =
                await CargarCajasPendientesCalidadAsync(busqueda);

            model.TotalCajasPendientes =
                model.CajasPendientes.Count;

            model.TotalPendienteLiberacionCaja =
                model.TotalCajasPendientes;

            if (estadoFiltro == CalidadEstados.PendienteLiberacionCaja)
            {
                model.TotalMostrados =
                    model.TotalCajasPendientes;
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RecibirDesdeProduccion(int programaProduccionId)
        {
            var usuarioId = ObtenerUsuarioIdActual();

            if (!usuarioId.HasValue || usuarioId.Value <= 0)
            {
                TempData["Error"] = "No se pudo identificar el usuario de la sesión.";
                return RedirectToAction(nameof(Index));
            }

            if (programaProduccionId <= 0)
            {
                TempData["Error"] = "No se recibió un programa de producción válido.";
                return RedirectToAction(nameof(Index));
            }

            var inspeccion = await _context.CalidadInspecciones
                .AsNoTracking()
                .Where(x =>
                    x.ProgramaProduccionID == programaProduccionId &&
                    x.EjecucionProduccionID.HasValue &&
                    x.EjecucionProduccionID.Value > 0 &&
                    x.ChecklistArranqueID.HasValue &&
                    x.ChecklistArranqueID.Value > 0 &&
                    !x.ConfiguracionInvalidada &&
                    x.Estado != CalidadEstados.Cerrada)
                .OrderByDescending(x => x.InspeccionID)
                .Select(x => new
                {
                    x.InspeccionID,
                    x.EjecucionProduccionID,
                    x.ChecklistArranqueID
                })
                .FirstOrDefaultAsync();

            if (inspeccion == null)
            {
                TempData["Error"] =
                    "Producción todavía no ha enviado el checklist a Calidad. " +
                    "La inspección debe generarse desde el checklist de arranque.";

                return RedirectToAction(nameof(Index));
            }

            TempData["Mensaje"] =
                "La solicitud de Calidad ya fue recibida desde Producción.";

            return RedirectToAction(
                nameof(Detalle),
                new { id = inspeccion.InspeccionID });
        }
        // =========================================================
        // DETALLE
        // =========================================================

        [HttpGet]
        public async Task<IActionResult> Detalle(int id)
        {
            if (id <= 0)
                return NotFound();

            var usuarioId = ObtenerUsuarioIdActual();

            var inspeccionBase = await _context.CalidadInspecciones
                .AsNoTracking()
                .Where(x => x.InspeccionID == id)
                .Select(x => new
                {
                    x.InspeccionID,
                    x.ChecklistArranqueID,
                    x.Estado,
                    x.ConfiguracionInvalidada
                })
                .FirstOrDefaultAsync();

            if (inspeccionBase == null)
                return NotFound();

            var incidenciasCarga = new List<string>();

            /*
             * Producción crea únicamente sus preguntas. Al abrir la
             * inspección, Calidad completa de forma idempotente las
             * preguntas asignadas a CALIDAD o AUDITOR.
             */
            if (inspeccionBase.ChecklistArranqueID.HasValue)
            {
                try
                {
                    await AsegurarPreguntasChecklistAuditorAsync(
                        inspeccionBase.ChecklistArranqueID.Value,
                        usuarioId);
                }
                catch (Exception ex)
                {
                    incidenciasCarga.Add(
                        "No fue posible preparar las preguntas del auditor: " +
                        ex.Message);
                }
            }

            try
            {
                await RegistrarContextoReliberacionAsync(id, usuarioId);
            }
            catch (Exception ex)
            {
                incidenciasCarga.Add(
                    "No fue posible sincronizar el contexto de reliberación: " +
                    ex.Message);
            }

            if (!inspeccionBase.ConfiguracionInvalidada &&
                CalidadEstados.PuedeAutorizarPrearranque(inspeccionBase.Estado))
            {
                try
                {
                    await RegistrarInicioValidacionPrearranqueAsync(id, usuarioId);
                }
                catch (Exception ex)
                {
                    incidenciasCarga.Add(
                        "No fue posible registrar el inicio de la revisión: " +
                        ex.Message);
                }
            }

            if (string.Equals(
                    inspeccionBase.Estado,
                    CalidadEstados.MonitoreoActivo,
                    StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    await ReconciliarMonitoreosConProduccionAsync(id, usuarioId);
                }
                catch (Exception ex)
                {
                    incidenciasCarga.Add(
                        "No fue posible vincular las capturas horarias de Producción: " +
                        ex.Message);
                }
            }

            if (incidenciasCarga.Count > 0)
            {
                TempData["Error"] = string.Join(" ", incidenciasCarga);
            }

            var model = await ConstruirDetalleFlujoAsync(id);

            if (model == null)
                return NotFound();

            await CargarChecklistArranqueParaDetalleAsync(model);

            return View(model);
        }

        // =========================================================
        // CHECKLIST DE PREARRANQUE: INTEGRACIÓN PRODUCCIÓN -> CALIDAD
        // =========================================================

        private async Task AsegurarPreguntasChecklistAuditorAsync(
            int checklistArranqueId,
            int? usuarioId)
        {
            const string sql = @"
INSERT INTO dbo.Produccion_ChecklistArranqueDetalle
(
    ChecklistArranqueID,
    PreguntaID,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
SELECT
    c.ChecklistArranqueID,
    p.PreguntaID,
    COALESCE(@UsuarioID, c.UsuarioCreacionID),
    GETDATE(),
    1
FROM dbo.Produccion_ChecklistArranque c
INNER JOIN dbo.ERP_ChecklistArranquePreguntas p
    ON p.CodigoFormato = c.CodigoFormato
   AND ISNULL(p.VersionFormato, N'') = ISNULL(c.VersionFormato, N'')
WHERE c.ChecklistArranqueID = @ChecklistArranqueID
  AND c.Activo = 1
  AND p.Activo = 1
  AND
  (
        UPPER(ISNULL(p.Seccion, N'')) LIKE N'%CALIDAD%'
     OR UPPER(ISNULL(p.Seccion, N'')) LIKE N'%AUDITOR%'
     OR UPPER(ISNULL(p.ResponsableSugerido, N'')) LIKE N'%CALIDAD%'
     OR UPPER(ISNULL(p.ResponsableSugerido, N'')) LIKE N'%AUDITOR%'
  )
  AND UPPER(ISNULL(p.Seccion, N'')) NOT LIKE N'%PARO%'
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.Produccion_ChecklistArranqueDetalle d WITH (UPDLOCK, HOLDLOCK)
      WHERE d.ChecklistArranqueID = c.ChecklistArranqueID
        AND d.PreguntaID = p.PreguntaID
        AND d.Activo = 1
  );";

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();

            try
            {
                await using var cmd = new SqlCommand(sql, cn, tx);

                cmd.Parameters.Add("@ChecklistArranqueID", SqlDbType.Int).Value =
                    checklistArranqueId;

                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                    (object?)usuarioId ?? DBNull.Value;

                await cmd.ExecuteNonQueryAsync();
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        private async Task RegistrarInicioValidacionPrearranqueAsync(
            int inspeccionId,
            int? usuarioId)
        {
            var inspeccion = await _context.CalidadInspecciones
                .FirstOrDefaultAsync(x => x.InspeccionID == inspeccionId);

            if (inspeccion == null ||
                inspeccion.ConfiguracionInvalidada ||
                !CalidadEstados.PuedeAutorizarPrearranque(inspeccion.Estado) ||
                inspeccion.FechaInicioValidacionPrearranque.HasValue)
            {
                return;
            }

            inspeccion.FechaInicioValidacionPrearranque = DateTime.Now;
            MarcarModificacion(inspeccion, usuarioId);

            await _context.SaveChangesAsync();
        }

        private async Task RegistrarContextoReliberacionAsync(
            int inspeccionId,
            int? usuarioId)
        {
            var inspeccion = await _context.CalidadInspecciones
                .FirstOrDefaultAsync(x => x.InspeccionID == inspeccionId);

            if (inspeccion == null ||
                !CalidadTipoProceso.EsReliberacion(inspeccion.Proceso) ||
                !inspeccion.EjecucionProduccionID.HasValue)
            {
                return;
            }

            var huboCambios = false;

            if (!inspeccion.RequiereReliberacion)
            {
                inspeccion.RequiereReliberacion = true;
                huboCambios = true;
            }

            const string sqlParo = @"
SELECT TOP (1)
    ParoID,
    ISNULL(DuracionMinutos, 0) AS DuracionMinutos
FROM dbo.Produccion_Paros
WHERE EjecucionProduccionID = @EjecucionProduccionID
  AND Activo = 1
  AND FechaFinParo IS NOT NULL
  AND ISNULL(EsMayorA15Minutos, 0) = 1
ORDER BY FechaFinParo DESC, ParoID DESC;";

            int? paroId = null;
            int duracionMinutos = 0;

            await using (var cn = new SqlConnection(ConnectionString))
            {
                await cn.OpenAsync();

                await using var cmd = new SqlCommand(sqlParo, cn);
                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value =
                    inspeccion.EjecucionProduccionID.Value;

                await using var rd = await cmd.ExecuteReaderAsync();

                if (await rd.ReadAsync())
                {
                    paroId = Convert.ToInt32(rd["ParoID"]);
                    duracionMinutos = Convert.ToInt32(rd["DuracionMinutos"]);
                }
            }

            if (paroId.HasValue)
            {
                var existe = await _context.CalidadReliberaciones
                    .AnyAsync(x =>
                        x.InspeccionID == inspeccionId &&
                        x.ParoID == paroId.Value &&
                        x.Activo);

                if (!existe)
                {
                    var numero =
                        (await _context.CalidadReliberaciones
                            .Where(x =>
                                x.EjecucionProduccionID ==
                                    inspeccion.EjecucionProduccionID.Value)
                            .MaxAsync(x => (int?)x.NumeroReliberacion) ?? 0) + 1;

                    _context.CalidadReliberaciones.Add(
                        new CalidadReliberacion
                        {
                            InspeccionID = inspeccionId,
                            EjecucionProduccionID =
                                inspeccion.EjecucionProduccionID.Value,
                            ParoID = paroId.Value,
                            NumeroReliberacion = numero,
                            Motivo =
                                "Paro mayor a 15 minutos. Duración registrada: " +
                                duracionMinutos + " minuto(s).",
                            FechaSolicitud =
                                inspeccion.FechaNotificacionCalidad ?? DateTime.Now,
                            UsuarioSolicitudID = inspeccion.UsuarioNotificoID,
                            Resultado = CalidadResultadoReliberacion.Pendiente,
                            UsuarioCreacionID = usuarioId,
                            FechaCreacion = DateTime.Now,
                            Activo = true
                        });

                    huboCambios = true;
                }
            }

            if (!huboCambios)
                return;

            MarcarModificacion(inspeccion, usuarioId);
            await _context.SaveChangesAsync();
        }

        private async Task CargarChecklistArranqueParaDetalleAsync(
            CalidadDetalleViewModel model)
        {
            model.PreguntasChecklistProduccion = new();
            model.PreguntasChecklistCalidad = new();

            if (!model.ChecklistArranqueID.HasValue)
                return;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            const string sqlEncabezado = @"
SELECT TOP (1)
    EstatusID,
    ObservacionesGenerales,
    ObservacionesCalidad
FROM dbo.Produccion_ChecklistArranque
WHERE ChecklistArranqueID = @ChecklistArranqueID
  AND Activo = 1;";

            await using (var cmd = new SqlCommand(sqlEncabezado, cn))
            {
                cmd.Parameters.Add("@ChecklistArranqueID", SqlDbType.Int).Value =
                    model.ChecklistArranqueID.Value;

                await using var rd = await cmd.ExecuteReaderAsync();

                if (await rd.ReadAsync())
                {
                    model.EstatusChecklistArranqueID =
                        rd["EstatusID"] == DBNull.Value
                            ? null
                            : Convert.ToInt32(rd["EstatusID"]);

                    model.ObservacionesChecklistProduccion =
                        rd["ObservacionesGenerales"] as string;

                    model.ObservacionesChecklistCalidad =
                        rd["ObservacionesCalidad"] as string;
                }
            }

            const string sqlPreguntas = @"
SELECT
    d.ChecklistArranqueDetalleID,
    d.PreguntaID,
    ISNULL(p.Seccion, N'') AS Seccion,
    ISNULL(p.OrdenSeccion, 0) AS OrdenSeccion,
    ISNULL(p.OrdenPregunta, 0) AS OrdenPregunta,
    ISNULL(p.TextoPregunta, N'') AS TextoPregunta,
    p.ResponsableSugerido,
    ISNULL(p.RequiereObservacionSiNOK, 0) AS RequiereObservacionSiNOK,
    d.Resultado,
    d.Observaciones,
    CASE
        WHEN
        (
              UPPER(ISNULL(p.Seccion, N'')) LIKE N'%CALIDAD%'
           OR UPPER(ISNULL(p.Seccion, N'')) LIKE N'%AUDITOR%'
           OR UPPER(ISNULL(p.ResponsableSugerido, N'')) LIKE N'%CALIDAD%'
           OR UPPER(ISNULL(p.ResponsableSugerido, N'')) LIKE N'%AUDITOR%'
        )
        THEN 1 ELSE 0
    END AS EsPreguntaCalidad
FROM dbo.Produccion_ChecklistArranqueDetalle d
INNER JOIN dbo.ERP_ChecklistArranquePreguntas p
    ON p.PreguntaID = d.PreguntaID
WHERE d.ChecklistArranqueID = @ChecklistArranqueID
  AND d.Activo = 1
  AND p.Activo = 1
  AND UPPER(ISNULL(p.Seccion, N'')) NOT LIKE N'%PARO%'
ORDER BY
    ISNULL(p.OrdenSeccion, 0),
    ISNULL(p.OrdenPregunta, 0),
    p.PreguntaID;";

            await using (var cmd = new SqlCommand(sqlPreguntas, cn))
            {
                cmd.Parameters.Add("@ChecklistArranqueID", SqlDbType.Int).Value =
                    model.ChecklistArranqueID.Value;

                await using var rd = await cmd.ExecuteReaderAsync();

                while (await rd.ReadAsync())
                {
                    var pregunta = new CalidadChecklistPreguntaViewModel
                    {
                        ChecklistArranqueDetalleID =
                            Convert.ToInt32(rd["ChecklistArranqueDetalleID"]),
                        PreguntaID = Convert.ToInt32(rd["PreguntaID"]),
                        Seccion = rd["Seccion"] as string ?? string.Empty,
                        OrdenSeccion = Convert.ToInt32(rd["OrdenSeccion"]),
                        OrdenPregunta = Convert.ToInt32(rd["OrdenPregunta"]),
                        TextoPregunta =
                            rd["TextoPregunta"] as string ?? string.Empty,
                        ResponsableSugerido =
                            rd["ResponsableSugerido"] as string,
                        RequiereObservacionSiNOK =
                            Convert.ToBoolean(rd["RequiereObservacionSiNOK"]),
                        Resultado = rd["Resultado"] as string,
                        Observaciones = rd["Observaciones"] as string
                    };

                    if (Convert.ToBoolean(rd["EsPreguntaCalidad"]))
                        model.PreguntasChecklistCalidad.Add(pregunta);
                    else
                        model.PreguntasChecklistProduccion.Add(pregunta);
                }
            }
        }

        private async Task ReconciliarMonitoreosConProduccionAsync(
            int inspeccionId,
            int? usuarioId)
        {
            const string sql = @"
DECLARE @EjecucionProduccionID INT;

SELECT @EjecucionProduccionID = EjecucionProduccionID
FROM dbo.Calidad_Inspecciones
WHERE InspeccionID = @InspeccionID
  AND Estado = N'MONITOREO_ACTIVO';

IF @EjecucionProduccionID IS NULL
    RETURN;

;WITH MonitoresPendientes AS
(
    SELECT
        m.MonitoreoID,
        ROW_NUMBER() OVER
        (
            ORDER BY m.FechaHoraProgramada, m.MonitoreoID
        ) AS OrdenVinculo
    FROM dbo.Calidad_MonitoreosProceso m
    WHERE m.InspeccionID = @InspeccionID
      AND m.Activo = 1
      AND m.Resultado = N'PENDIENTE'
      AND m.RegistroHoraID IS NULL
),
RegistrosDisponibles AS
(
    SELECT
        rh.RegistroHoraID,
        ISNULL(rh.CantidadOK, 0) +
        ISNULL(rh.CantidadSospechosa, 0) +
        ISNULL(rh.CantidadScrap, 0) AS CantidadPeriodo,
        ROW_NUMBER() OVER
        (
            ORDER BY
                rh.FechaProduccion,
                rh.HoraInicio,
                rh.RegistroHoraID
        ) AS OrdenVinculo
    FROM dbo.Produccion_RegistroHora rh
    WHERE rh.EjecucionProduccionID = @EjecucionProduccionID
      AND rh.Activo = 1
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.Calidad_MonitoreosProceso usado
          WHERE usado.RegistroHoraID = rh.RegistroHoraID
            AND usado.Activo = 1
      )
)
UPDATE monitor
SET
    monitor.RegistroHoraID = registro.RegistroHoraID,
    monitor.CantidadProducidaPeriodo = registro.CantidadPeriodo,
    monitor.UsuarioModificacionID =
        COALESCE(@UsuarioID, monitor.UsuarioModificacionID),
    monitor.FechaModificacion = GETDATE()
FROM dbo.Calidad_MonitoreosProceso monitor
INNER JOIN MonitoresPendientes pendiente
    ON pendiente.MonitoreoID = monitor.MonitoreoID
INNER JOIN RegistrosDisponibles registro
    ON registro.OrdenVinculo = pendiente.OrdenVinculo;";

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value = inspeccionId;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                (object?)usuarioId ?? DBNull.Value;

            await cmd.ExecuteNonQueryAsync();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarChecklistAuditor(
            CalidadChecklistGuardarViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] =
                    "No se recibió correctamente el checklist del auditor.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = model.InspeccionID });
            }

            var usuarioId = ObtenerUsuarioIdActual();

            if (!usuarioId.HasValue || usuarioId.Value <= 0)
                return Unauthorized();

            var inspeccion = await _context.CalidadInspecciones
                .FirstOrDefaultAsync(x =>
                    x.InspeccionID == model.InspeccionID &&
                    x.ChecklistArranqueID == model.ChecklistArranqueID);

            if (inspeccion == null)
                return NotFound();

            if (!CalidadEstados.PuedeAutorizarPrearranque(inspeccion.Estado))
            {
                TempData["Error"] =
                    "El checklist ya no está disponible para revisión de prearranque.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = model.InspeccionID });
            }

            await AsegurarPreguntasChecklistAuditorAsync(
                model.ChecklistArranqueID,
                usuarioId);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();

            try
            {
                foreach (var respuesta in
                    model.Respuestas ??
                    new List<CalidadChecklistRespuestaViewModel>())
                {
                    var resultado =
                        NormalizarResultadoChecklistAuditor(respuesta.Resultado);

                    if (resultado == "__INVALIDO__")
                    {
                        throw new InvalidOperationException(
                            "Se recibió una respuesta inválida en el checklist del auditor.");
                    }

                    if (resultado == CalidadChecklistResultado.Nok &&
                        string.IsNullOrWhiteSpace(respuesta.Observaciones))
                    {
                        throw new InvalidOperationException(
                            "Toda respuesta NOK del auditor requiere una observación.");
                    }

                    const string sqlUpdate = @"
UPDATE d
SET
    d.Resultado = @Resultado,
    d.Observaciones = @Observaciones,
    d.UsuarioRespuestaID = @UsuarioID,
    d.FechaRespuesta =
        CASE WHEN @Resultado IS NULL THEN d.FechaRespuesta ELSE GETDATE() END,
    d.UsuarioModificacionID = @UsuarioID,
    d.FechaModificacion = GETDATE()
FROM dbo.Produccion_ChecklistArranqueDetalle d
INNER JOIN dbo.ERP_ChecklistArranquePreguntas p
    ON p.PreguntaID = d.PreguntaID
WHERE d.ChecklistArranqueDetalleID = @DetalleID
  AND d.ChecklistArranqueID = @ChecklistArranqueID
  AND d.Activo = 1
  AND p.Activo = 1
  AND
  (
        UPPER(ISNULL(p.Seccion, N'')) LIKE N'%CALIDAD%'
     OR UPPER(ISNULL(p.Seccion, N'')) LIKE N'%AUDITOR%'
     OR UPPER(ISNULL(p.ResponsableSugerido, N'')) LIKE N'%CALIDAD%'
     OR UPPER(ISNULL(p.ResponsableSugerido, N'')) LIKE N'%AUDITOR%'
  );";

                    await using var cmd = new SqlCommand(sqlUpdate, cn, tx);

                    cmd.Parameters.Add("@Resultado", SqlDbType.NVarChar, 10).Value =
                        (object?)resultado ?? DBNull.Value;

                    cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 500).Value =
                        string.IsNullOrWhiteSpace(respuesta.Observaciones)
                            ? DBNull.Value
                            : respuesta.Observaciones.Trim();

                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                        usuarioId.Value;

                    cmd.Parameters.Add("@DetalleID", SqlDbType.Int).Value =
                        respuesta.ChecklistArranqueDetalleID;

                    cmd.Parameters.Add("@ChecklistArranqueID", SqlDbType.Int).Value =
                        model.ChecklistArranqueID;

                    await cmd.ExecuteNonQueryAsync();
                }

                const string sqlHeader = @"
UPDATE dbo.Produccion_ChecklistArranque
SET
    UsuarioCalidadID = @UsuarioID,
    ObservacionesCalidad = @ObservacionesCalidad,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE ChecklistArranqueID = @ChecklistArranqueID
  AND Activo = 1;";

                await using (var cmd = new SqlCommand(sqlHeader, cn, tx))
                {
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                        usuarioId.Value;

                    cmd.Parameters.Add("@ObservacionesCalidad", SqlDbType.NVarChar, 1000).Value =
                        string.IsNullOrWhiteSpace(model.ObservacionesCalidad)
                            ? DBNull.Value
                            : model.ObservacionesCalidad.Trim();

                    cmd.Parameters.Add("@ChecklistArranqueID", SqlDbType.Int).Value =
                        model.ChecklistArranqueID;

                    await cmd.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();

                inspeccion.FechaInicioValidacionPrearranque ??= DateTime.Now;
                MarcarModificacion(inspeccion, usuarioId);

                AgregarHistorial(
                    inspeccion,
                    CalidadMovimientos.ChecklistCalidadCapturado,
                    inspeccion.Estado,
                    inspeccion.Estado,
                    inspeccion.ResultadoCalidad,
                    inspeccion.Etiqueta,
                    "El auditor guardó su sección del checklist de arranque.",
                    usuarioId);

                await _context.SaveChangesAsync();

                TempData["Mensaje"] =
                    "Sección del auditor de Calidad guardada correctamente.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                TempData["Error"] =
                    "No fue posible guardar el checklist del auditor: " + ex.Message;
            }

            return RedirectToAction(
                nameof(Detalle),
                new { id = model.InspeccionID });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AutorizarPrearranqueAuditor(
            CalidadPrearranqueViewModel model)
        {
            if (!ModelState.IsValid || model.InspeccionID <= 0)
            {
                TempData["Error"] = "No se recibió una inspección válida.";
                return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
            }

            if (!model.AyudaVisualColocada ||
                !model.HIPColocada ||
                !model.HCCColocada ||
                !model.MatrizPolivalenciaValidada)
            {
                TempData["Error"] =
                    "Confirma ayuda visual, HIP, HCC y matriz de polivalencia antes de autorizar.";

                return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
            }

            if (!model.AlertaCalidadAplica.HasValue)
            {
                TempData["Error"] =
                    "Indica expresamente si aplica una alerta de Calidad.";

                return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
            }

            if (model.AlertaCalidadAplica == true &&
                model.AlertaCalidadColocada != true)
            {
                TempData["Error"] =
                    "La alerta de Calidad aplica y debe confirmarse como colocada.";

                return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
            }

            var inspeccion = await _context.CalidadInspecciones
                .FirstOrDefaultAsync(x => x.InspeccionID == model.InspeccionID);

            if (inspeccion == null)
                return NotFound();

            if (!CalidadEstados.PuedeAutorizarPrearranque(inspeccion.Estado))
            {
                TempData["Error"] =
                    "La inspección ya no está pendiente de prearranque.";

                return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
            }

            if (inspeccion.ConfiguracionInvalidada)
            {
                TempData["Error"] =
                    "La configuración fue invalidada y debe generarse una nueva revisión.";

                return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
            }

            if (!inspeccion.ChecklistArranqueID.HasValue)
            {
                TempData["Error"] =
                    "La inspección no tiene un checklist de Producción relacionado.";

                return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
            }

            var validacionConfiguracion =
                await ValidarConfiguracionActualAsync(inspeccion);

            if (!validacionConfiguracion.Valida)
            {
                await InvalidarConfiguracionAsync(
                    inspeccion,
                    validacionConfiguracion.Motivo);

                TempData["Error"] = validacionConfiguracion.Motivo;
                return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
            }

            await AsegurarPreguntasChecklistAuditorAsync(
                inspeccion.ChecklistArranqueID.Value,
                ObtenerUsuarioIdActual());

            var validacionChecklist =
                await ValidarChecklistCompletoParaAutorizarAsync(
                    inspeccion.ChecklistArranqueID.Value);

            if (!validacionChecklist.Valido)
            {
                TempData["Error"] = validacionChecklist.Mensaje;
                return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
            }

            var usuarioId = ObtenerUsuarioIdActual();

            if (!usuarioId.HasValue || usuarioId.Value <= 0)
                return Unauthorized();

            await using var tx = await _context.Database.BeginTransactionAsync();

            try
            {
                var ahora = DateTime.Now;
                var estadoAnterior = inspeccion.Estado;

                inspeccion.AyudaVisualColocada = model.AyudaVisualColocada;
                inspeccion.AlertaCalidadAplica = model.AlertaCalidadAplica;
                inspeccion.AlertaCalidadColocada =
                    model.AlertaCalidadAplica == true
                        ? model.AlertaCalidadColocada
                        : null;
                inspeccion.HIPColocada = model.HIPColocada;
                inspeccion.HCCColocada = model.HCCColocada;
                inspeccion.MatrizPolivalenciaValidada =
                    model.MatrizPolivalenciaValidada;
                inspeccion.ChecklistValidado = true;
                inspeccion.HojaInspeccionProducto = true;
                inspeccion.HojaValidacionCalidad = true;
                inspeccion.FechaInicioValidacionPrearranque ??= ahora;
                inspeccion.FechaFinValidacionPrearranque = ahora;
                inspeccion.FechaAutorizacionPrearranque = ahora;
                inspeccion.UsuarioAutorizacionPrearranqueID = usuarioId;
                inspeccion.MotivoDevolucion = null;
                inspeccion.Estado = CalidadEstados.ArranqueAutorizado;

                MarcarModificacion(inspeccion, usuarioId);

                AgregarHistorial(
                    inspeccion,
                    CalidadMovimientos.PrearranqueAutorizado,
                    estadoAnterior,
                    inspeccion.Estado,
                    inspeccion.ResultadoCalidad,
                    inspeccion.Etiqueta,
                    string.IsNullOrWhiteSpace(model.Motivo)
                        ? "Calidad autorizó el arranque controlado."
                        : model.Motivo.Trim(),
                    usuarioId);

                await _context.SaveChangesAsync();

                await _context.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE dbo.Produccion_ChecklistArranque
SET
    EstatusID = {ProduccionChecklistEstatus.ValidadoPorCalidad},
    UsuarioCalidadID = {usuarioId.Value},
    FechaValidacionCalidad = {ahora},
    ObservacionesCalidad = {model.Motivo},
    UsuarioModificacionID = {usuarioId.Value},
    FechaModificacion = {ahora}
WHERE ChecklistArranqueID = {inspeccion.ChecklistArranqueID.Value}
  AND Activo = 1;");

                await tx.CommitAsync();

                TempData["Mensaje"] =
                    "Prearranque autorizado. Producción puede generar las primeras piezas, pero todavía no iniciar la serie.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] =
                    "No fue posible autorizar el prearranque: " + ex.Message;
            }

            return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DevolverPrearranqueAuditor(
            CalidadPrearranqueViewModel model)
        {
            model.Motivo = model.Motivo?.Trim();

            if (model.InspeccionID <= 0 || string.IsNullOrWhiteSpace(model.Motivo))
            {
                TempData["Error"] =
                    "Captura el motivo obligatorio de la devolución.";

                return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
            }

            var inspeccion = await _context.CalidadInspecciones
                .FirstOrDefaultAsync(x => x.InspeccionID == model.InspeccionID);

            if (inspeccion == null)
                return NotFound();

            if (!CalidadEstados.PuedeAutorizarPrearranque(inspeccion.Estado))
            {
                TempData["Error"] =
                    "La inspección ya no está pendiente de prearranque.";

                return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
            }

            var usuarioId = ObtenerUsuarioIdActual();

            if (!usuarioId.HasValue || usuarioId.Value <= 0)
                return Unauthorized();

            await using var tx = await _context.Database.BeginTransactionAsync();

            try
            {
                var ahora = DateTime.Now;
                var estadoAnterior = inspeccion.Estado;

                inspeccion.Estado = CalidadEstados.DevueltoPrearranque;
                inspeccion.MotivoDevolucion = model.Motivo;
                inspeccion.ChecklistValidado = false;
                inspeccion.Liberado = false;
                inspeccion.FechaFinValidacionPrearranque = ahora;

                MarcarModificacion(inspeccion, usuarioId);

                AgregarHistorial(
                    inspeccion,
                    CalidadMovimientos.PrearranqueDevuelto,
                    estadoAnterior,
                    inspeccion.Estado,
                    inspeccion.ResultadoCalidad,
                    inspeccion.Etiqueta,
                    model.Motivo,
                    usuarioId);

                await _context.SaveChangesAsync();

                if (inspeccion.ChecklistArranqueID.HasValue)
                {
                    await _context.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE dbo.Produccion_ChecklistArranque
SET
    EstatusID = {ProduccionChecklistEstatus.RechazadoRequiereAjuste},
    UsuarioCalidadID = {usuarioId.Value},
    FechaValidacionCalidad = {ahora},
    ObservacionesCalidad = {model.Motivo},
    UsuarioModificacionID = {usuarioId.Value},
    FechaModificacion = {ahora}
WHERE ChecklistArranqueID = {inspeccion.ChecklistArranqueID.Value}
  AND Activo = 1;");
                }

                await tx.CommitAsync();

                TempData["Mensaje"] =
                    "La revisión fue devuelta a Producción para corrección.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] =
                    "No fue posible devolver la revisión: " + ex.Message;
            }

            return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LiberarProduccionAuditor(int id)
        {
            if (id <= 0) return NotFound();

            var usuarioId = ObtenerUsuarioIdActual();
            if (!usuarioId.HasValue || usuarioId.Value <= 0) return Unauthorized();

            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                var inspeccion = await _context.CalidadInspecciones.FirstOrDefaultAsync(x => x.InspeccionID == id);
                if (inspeccion == null)
                {
                    await tx.RollbackAsync();
                    return NotFound();
                }

                if (inspeccion.ConfiguracionInvalidada)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La configuración fue invalidada y no puede liberarse.";
                    return RedirectToAction(nameof(Detalle), new { id });
                }

                if (inspeccion.Estado != CalidadEstados.PendientePrimerasPiezas &&
                    inspeccion.Estado != CalidadEstados.AjustesSolicitados &&
                    inspeccion.Estado != CalidadEstados.LegacyAbierta &&
                    inspeccion.Estado != CalidadEstados.PendienteReliberacion)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La inspección no se encuentra lista para liberar Producción.";
                    return RedirectToAction(nameof(Detalle), new { id });
                }

                var validacionConfiguracion = await ValidarConfiguracionActualAsync(inspeccion);
                if (!validacionConfiguracion.Valida)
                {
                    await InvalidarConfiguracionAsync(inspeccion, validacionConfiguracion.Motivo);
                    await tx.CommitAsync();
                    TempData["Error"] = validacionConfiguracion.Motivo;
                    return RedirectToAction(nameof(Detalle), new { id });
                }

                var intento = await _context.CalidadPrimerasPiezasIntentos
                    .Where(x => x.InspeccionID == id && x.Activo)
                    .OrderByDescending(x => x.NumeroIntento)
                    .FirstOrDefaultAsync();

                if (intento == null)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "Primero registra la validación de las primeras piezas.";
                    return RedirectToAction(nameof(Detalle), new { id });
                }

                if (!intento.CincoDisparosSegregados ||
                    intento.CantidadDisparosPresentados < 3 ||
                    intento.ValidacionDimensional != true ||
                    intento.ValidacionApariencia != true ||
                    intento.ValidacionGauge == false ||
                    intento.ValidacionConductividad == false)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "El último intento no cumple los requisitos para liberar la producción.";
                    return RedirectToAction(nameof(Detalle), new { id });
                }

                var eraReliberacion = inspeccion.RequiereReliberacion ||
                                      inspeccion.Estado == CalidadEstados.PendienteReliberacion ||
                                      CalidadTipoProceso.EsReliberacion(inspeccion.Proceso);

                CalidadReliberacion? reliberacionPendiente = null;
                if (eraReliberacion)
                {
                    reliberacionPendiente = await _context.CalidadReliberaciones
                        .Where(x => x.InspeccionID == id &&
                                    x.Activo &&
                                    x.Resultado == CalidadResultadoReliberacion.Pendiente)
                        .OrderByDescending(x => x.NumeroReliberacion)
                        .FirstOrDefaultAsync();

                    if (reliberacionPendiente == null)
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = "No existe una reliberación pendiente relacionada con esta inspección.";
                        return RedirectToAction(nameof(Detalle), new { id });
                    }
                }

                var ahora = DateTime.Now;
                var estadoAnterior = inspeccion.Estado;

                intento.Resultado = CalidadResultadoIntento.Ok;
                intento.AjusteSolicitado = false;
                intento.FechaFin = ahora;
                intento.UsuarioModificacionID = usuarioId.Value;
                intento.FechaModificacion = ahora;

                inspeccion.CincoDisparosSegregados = intento.CincoDisparosSegregados;
                inspeccion.CantidadDisparosConformes = intento.CantidadDisparosPresentados;
                inspeccion.ValidacionDimensional = intento.ValidacionDimensional;
                inspeccion.ValidacionApariencia = intento.ValidacionApariencia;
                inspeccion.ValidacionGauge = intento.ValidacionGauge;
                inspeccion.ValidacionConductividad = intento.ValidacionConductividad;
                inspeccion.ResultadoCalidad = "VERDE";
                inspeccion.Etiqueta = "VERDE";
                inspeccion.Liberado = true;
                inspeccion.RequiereGP12 = false;
                inspeccion.EnContencion = false;
                inspeccion.EsScrap = false;
                inspeccion.RequiereReliberacion = false;
                inspeccion.Estado = CalidadEstados.ProduccionLiberada;
                inspeccion.FechaLiberacionProduccion = ahora;
                inspeccion.UsuarioLiberacionProduccionID = usuarioId.Value;
                inspeccion.FechaValidacionPrimerasPiezas = ahora;
                inspeccion.UsuarioValidacionPrimerasPiezasID = usuarioId.Value;
                inspeccion.MotivoDevolucion = null;

                if (inspeccion.FechaNotificacionCalidad.HasValue)
                {
                    var minutos = (int)Math.Max(0, Math.Round((ahora - inspeccion.FechaNotificacionCalidad.Value).TotalMinutes));
                    inspeccion.MinutosLiberacionInicial = minutos;
                    inspeccion.CumplioTiempoObjetivoInicial = minutos >= 10 && minutos <= 20;
                }

                if (reliberacionPendiente != null)
                {
                    reliberacionPendiente.Resultado = CalidadResultadoReliberacion.Autorizada;
                    reliberacionPendiente.FechaValidacion = ahora;
                    reliberacionPendiente.UsuarioCalidadID = usuarioId.Value;
                    reliberacionPendiente.Observaciones = UnirObservaciones(
                        reliberacionPendiente.Observaciones,
                        $"Reliberación {reliberacionPendiente.NumeroReliberacion} autorizada después de validar primeras piezas conformes.");
                    reliberacionPendiente.UsuarioModificacionID = usuarioId.Value;
                    reliberacionPendiente.FechaModificacion = ahora;
                }

                MarcarModificacion(inspeccion, usuarioId.Value);

                AgregarHistorial(
                    inspeccion,
                    eraReliberacion ? CalidadMovimientos.ReliberacionAutorizada : CalidadMovimientos.ProduccionLiberada,
                    estadoAnterior,
                    inspeccion.Estado,
                    inspeccion.ResultadoCalidad,
                    inspeccion.Etiqueta,
                    eraReliberacion
                        ? $"Reliberación {reliberacionPendiente?.NumeroReliberacion} autorizada con etiqueta verde. Producción debe confirmar el reinicio de la serie."
                        : "Calidad liberó la producción con etiqueta verde. Producción debe confirmar el inicio de la serie.",
                    usuarioId.Value);

                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                TempData["Mensaje"] = eraReliberacion
                    ? "Reliberación autorizada con etiqueta verde. Producción ya puede reiniciar la serie."
                    : "Producción liberada con etiqueta verde. Producción ya puede iniciar la serie.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] = "No fue posible liberar la producción: " + ex.Message;
            }

            return RedirectToAction(nameof(Detalle), new { id });
        }


        private async Task<(bool Valido, string Mensaje)>
            ValidarChecklistCompletoParaAutorizarAsync(
                int checklistArranqueId)
        {
            const string sql = @"
;WITH Preguntas AS
(
    SELECT
        d.Resultado,
        d.Observaciones,
        ISNULL(p.RequiereObservacionSiNOK, 0) AS RequiereObservacionSiNOK,
        CASE
            WHEN
            (
                  UPPER(ISNULL(p.Seccion, N'')) LIKE N'%CALIDAD%'
               OR UPPER(ISNULL(p.Seccion, N'')) LIKE N'%AUDITOR%'
               OR UPPER(ISNULL(p.ResponsableSugerido, N'')) LIKE N'%CALIDAD%'
               OR UPPER(ISNULL(p.ResponsableSugerido, N'')) LIKE N'%AUDITOR%'
            )
            THEN 1 ELSE 0
        END AS EsCalidad
    FROM dbo.Produccion_ChecklistArranqueDetalle d
    INNER JOIN dbo.ERP_ChecklistArranquePreguntas p
        ON p.PreguntaID = d.PreguntaID
    WHERE d.ChecklistArranqueID = @ChecklistArranqueID
      AND d.Activo = 1
      AND p.Activo = 1
      AND UPPER(ISNULL(p.Seccion, N'')) NOT LIKE N'%PARO%'
)
SELECT
    ISNULL(SUM(CASE WHEN EsCalidad = 0 THEN 1 ELSE 0 END), 0) AS TotalProduccion,
    ISNULL(SUM(CASE WHEN EsCalidad = 0 AND (Resultado IS NULL OR LTRIM(RTRIM(Resultado)) = N'') THEN 1 ELSE 0 END), 0) AS PendientesProduccion,
    ISNULL(SUM(CASE WHEN EsCalidad = 0 AND Resultado = N'NOK' THEN 1 ELSE 0 END), 0) AS NokProduccion,
    ISNULL(SUM(CASE WHEN EsCalidad = 1 THEN 1 ELSE 0 END), 0) AS TotalCalidad,
    ISNULL(SUM(CASE WHEN EsCalidad = 1 AND (Resultado IS NULL OR LTRIM(RTRIM(Resultado)) = N'') THEN 1 ELSE 0 END), 0) AS PendientesCalidad,
    ISNULL(SUM(CASE WHEN EsCalidad = 1 AND Resultado = N'NOK' THEN 1 ELSE 0 END), 0) AS NokCalidad,
    ISNULL(SUM(CASE WHEN EsCalidad = 1 AND Resultado = N'NOK' AND ISNULL(RequiereObservacionSiNOK, 0) = 1 AND (Observaciones IS NULL OR LTRIM(RTRIM(Observaciones)) = N'') THEN 1 ELSE 0 END), 0) AS NokSinObservacion
FROM Preguntas;";

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@ChecklistArranqueID", SqlDbType.Int).Value =
                checklistArranqueId;

            await using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
                return (false, "No fue posible leer el checklist de arranque.");

            var totalProduccion = Convert.ToInt32(rd["TotalProduccion"]);
            var pendientesProduccion = Convert.ToInt32(rd["PendientesProduccion"]);
            var nokProduccion = Convert.ToInt32(rd["NokProduccion"]);
            var totalCalidad = Convert.ToInt32(rd["TotalCalidad"]);
            var pendientesCalidad = Convert.ToInt32(rd["PendientesCalidad"]);
            var nokCalidad = Convert.ToInt32(rd["NokCalidad"]);
            var nokSinObservacion = Convert.ToInt32(rd["NokSinObservacion"]);

            if (totalProduccion <= 0)
                return (false, "No se encontraron preguntas de preparación respondidas por Producción.");

            if (pendientesProduccion > 0)
                return (false, "El checklist de Producción todavía tiene preguntas pendientes.");

            if (nokProduccion > 0)
                return (false, "El checklist de Producción contiene resultados NOK y debe devolverse para corrección.");

            if (totalCalidad <= 0)
                return (false, "No se encontraron preguntas asignadas a Calidad o al auditor.");

            if (pendientesCalidad > 0)
                return (false, "Responde todas las preguntas del auditor antes de autorizar el prearranque.");

            if (nokSinObservacion > 0)
                return (false, "Existen respuestas NOK del auditor sin observación.");

            if (nokCalidad > 0)
                return (false, "El checklist del auditor contiene resultados NOK. Debe devolverse a Producción.");

            return (true, string.Empty);
        }

        private static string? NormalizarResultadoChecklistAuditor(
            string? resultado)
        {
            if (string.IsNullOrWhiteSpace(resultado))
                return null;

            var valor = resultado.Trim().ToUpperInvariant();

            return valor switch
            {
                CalidadChecklistResultado.Ok => CalidadChecklistResultado.Ok,
                CalidadChecklistResultado.Nok => CalidadChecklistResultado.Nok,
                CalidadChecklistResultado.NoAplica => CalidadChecklistResultado.NoAplica,
                "N/A" => CalidadChecklistResultado.NoAplica,
                _ => "__INVALIDO__"
            };
        }

        // =========================================================
        // PREARRANQUE
        // =========================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AutorizarPrearranque(
            CalidadPrearranqueViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] =
                    "No se recibió una inspección válida.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = model.InspeccionID }
                );
            }

            var inspeccion =
                await _context.CalidadInspecciones
                    .FirstOrDefaultAsync(x =>
                        x.InspeccionID ==
                            model.InspeccionID);

            if (inspeccion == null)
                return NotFound();

            if (!CalidadEstados
                .PuedeAutorizarPrearranque(
                    inspeccion.Estado))
            {
                TempData["Error"] =
                    "La inspección ya no se encuentra pendiente de prearranque.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = model.InspeccionID }
                );
            }

            if (inspeccion.ConfiguracionInvalidada)
            {
                TempData["Error"] =
                    "La configuración de Planeación fue invalidada. Debe generarse una nueva revisión.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = model.InspeccionID }
                );
            }

            var validacion =
                await ValidarConfiguracionActualAsync(
                    inspeccion
                );

            if (!validacion.Valida)
            {
                await InvalidarConfiguracionAsync(
                    inspeccion,
                    validacion.Motivo
                );

                TempData["Error"] =
                    validacion.Motivo;

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = model.InspeccionID }
                );
            }

            var usuarioId =
                ObtenerUsuarioIdActual();

            var estadoAnterior =
                inspeccion.Estado;

            inspeccion.Estado =
                CalidadEstados.ArranqueAutorizado;

            inspeccion.FechaAutorizacionPrearranque =
                DateTime.Now;

            inspeccion.UsuarioAutorizacionPrearranqueID =
                usuarioId;

            inspeccion.MotivoDevolucion =
                null;

            MarcarModificacion(
                inspeccion,
                usuarioId
            );

            AgregarHistorial(
                inspeccion,
                CalidadMovimientos
                    .PrearranqueAutorizado,
                estadoAnterior,
                inspeccion.Estado,
                inspeccion.ResultadoCalidad,
                inspeccion.Etiqueta,
                string.IsNullOrWhiteSpace(model.Motivo)
                    ? "Calidad autorizó el arranque controlado."
                    : model.Motivo.Trim(),
                usuarioId
            );

            await _context.SaveChangesAsync();

            TempData["Mensaje"] =
                "Prearranque autorizado correctamente.";

            return RedirectToAction(
                nameof(Detalle),
                new { id = model.InspeccionID }
            );
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DevolverPrearranque(
            CalidadPrearranqueViewModel model)
        {
            model.Motivo =
                model.Motivo?.Trim();

            if (string.IsNullOrWhiteSpace(model.Motivo))
            {
                TempData["Error"] =
                    "Debes capturar el motivo de la devolución.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = model.InspeccionID }
                );
            }

            var inspeccion =
                await _context.CalidadInspecciones
                    .FirstOrDefaultAsync(x =>
                        x.InspeccionID ==
                            model.InspeccionID);

            if (inspeccion == null)
                return NotFound();

            if (!CalidadEstados
                .PuedeAutorizarPrearranque(
                    inspeccion.Estado))
            {
                TempData["Error"] =
                    "La inspección ya no se encuentra pendiente de prearranque.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = model.InspeccionID }
                );
            }

            var usuarioId =
                ObtenerUsuarioIdActual();

            var estadoAnterior =
                inspeccion.Estado;

            inspeccion.Estado =
                CalidadEstados.DevueltoPrearranque;

            inspeccion.MotivoDevolucion =
                model.Motivo;

            inspeccion.Liberado =
                false;

            MarcarModificacion(
                inspeccion,
                usuarioId
            );

            AgregarHistorial(
                inspeccion,
                CalidadMovimientos
                    .PrearranqueDevuelto,
                estadoAnterior,
                inspeccion.Estado,
                inspeccion.ResultadoCalidad,
                inspeccion.Etiqueta,
                model.Motivo,
                usuarioId
            );

            await _context.SaveChangesAsync();

            TempData["Mensaje"] =
                "La revisión fue devuelta a Producción.";

            return RedirectToAction(
                nameof(Detalle),
                new { id = model.InspeccionID }
            );
        }

        // =========================================================
        // PRIMERAS PIEZAS
        // =========================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegistrarPrimerasPiezas(
            CalidadPrimerasPiezasViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] =
                    "Revisa la información de las primeras piezas.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = model.InspeccionID }
                );
            }

            if (!model.CincoDisparosSegregados)
            {
                TempData["Error"] =
                    "Debes confirmar la segregación de los primeros cinco disparos.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = model.InspeccionID }
                );
            }

            var inspeccion =
                await _context.CalidadInspecciones
                    .FirstOrDefaultAsync(x =>
                        x.InspeccionID ==
                            model.InspeccionID);

            if (inspeccion == null)
                return NotFound();

            if (!CalidadEstados
                .PuedeValidarPrimerasPiezas(
                    inspeccion.Estado))
            {
                TempData["Error"] =
                    "La inspección no está disponible para registrar primeras piezas.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = model.InspeccionID }
                );
            }

            var usuarioId =
                ObtenerUsuarioIdActual();

            var estadoAnterior =
                inspeccion.Estado;

            AplicarPrimerasPiezas(
                inspeccion,
                model,
                usuarioId
            );

            inspeccion.Estado =
                CalidadEstados
                    .PendientePrimerasPiezas;

            MarcarModificacion(
                inspeccion,
                usuarioId
            );

            AgregarHistorial(
                inspeccion,
                CalidadMovimientos
                    .PrimerasPiezasRecibidas,
                estadoAnterior,
                inspeccion.Estado,
                inspeccion.ResultadoCalidad,
                inspeccion.Etiqueta,
                string.IsNullOrWhiteSpace(
                    model.Observaciones)
                    ? "Calidad registró la validación de primeras piezas."
                    : model.Observaciones.Trim(),
                usuarioId
            );

            await _context.SaveChangesAsync();

            TempData["Mensaje"] =
                "La validación de primeras piezas fue registrada.";

            return RedirectToAction(
                nameof(Detalle),
                new { id = model.InspeccionID }
            );
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SolicitarAjustes(
            CalidadPrimerasPiezasViewModel model)
        {
            model.Observaciones =
                model.Observaciones?.Trim();

            if (string.IsNullOrWhiteSpace(
                model.Observaciones))
            {
                TempData["Error"] =
                    "Captura las observaciones o ajustes requeridos.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = model.InspeccionID }
                );
            }

            var inspeccion =
                await _context.CalidadInspecciones
                    .FirstOrDefaultAsync(x =>
                        x.InspeccionID ==
                            model.InspeccionID);

            if (inspeccion == null)
                return NotFound();

            if (!CalidadEstados
                .PuedeValidarPrimerasPiezas(
                    inspeccion.Estado))
            {
                TempData["Error"] =
                    "La inspección no permite solicitar ajustes en su estado actual.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = model.InspeccionID }
                );
            }

            var usuarioId =
                ObtenerUsuarioIdActual();

            var estadoAnterior =
                inspeccion.Estado;

            AplicarPrimerasPiezas(
                inspeccion,
                model,
                usuarioId
            );

            inspeccion.ResultadoCalidad =
                "NOK";

            inspeccion.Etiqueta =
                null;

            inspeccion.Liberado =
                false;

            inspeccion.Estado =
                CalidadEstados
                    .AjustesSolicitados;

            inspeccion.Observaciones =
                model.Observaciones;

            MarcarModificacion(
                inspeccion,
                usuarioId
            );

            AgregarHistorial(
                inspeccion,
                CalidadMovimientos
                    .AjustesSolicitados,
                estadoAnterior,
                inspeccion.Estado,
                inspeccion.ResultadoCalidad,
                inspeccion.Etiqueta,
                model.Observaciones,
                usuarioId
            );

            await _context.SaveChangesAsync();

            TempData["Mensaje"] =
                "Los ajustes fueron solicitados a Producción.";

            return RedirectToAction(
                nameof(Detalle),
                new { id = model.InspeccionID }
            );
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LiberarProduccion(
            int id)
        {
            var inspeccion =
                await _context.CalidadInspecciones
                    .FirstOrDefaultAsync(x =>
                        x.InspeccionID == id);

            if (inspeccion == null)
                return NotFound();

            if (inspeccion.Estado !=
                    CalidadEstados
                        .PendientePrimerasPiezas &&
                inspeccion.Estado !=
                    CalidadEstados
                        .AjustesSolicitados &&
                inspeccion.Estado !=
                    CalidadEstados
                        .LegacyAbierta)
            {
                TempData["Error"] =
                    "La inspección no se encuentra lista para liberar producción.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id }
                );
            }

            if (inspeccion.ConfiguracionInvalidada)
            {
                TempData["Error"] =
                    "La configuración fue invalidada y no puede liberarse.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id }
                );
            }

            var validacionConfiguracion =
                await ValidarConfiguracionActualAsync(
                    inspeccion
                );

            if (!validacionConfiguracion.Valida)
            {
                await InvalidarConfiguracionAsync(
                    inspeccion,
                    validacionConfiguracion.Motivo
                );

                TempData["Error"] =
                    validacionConfiguracion.Motivo;

                return RedirectToAction(
                    nameof(Detalle),
                    new { id }
                );
            }

            if (!inspeccion
                    .CincoDisparosSegregados)
            {
                TempData["Error"] =
                    "No se confirmó la segregación de los primeros cinco disparos.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id }
                );
            }

            if (inspeccion
                .CantidadDisparosConformes < 3)
            {
                TempData["Error"] =
                    "Se requieren al menos tres disparos conformes para liberar producción.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id }
                );
            }

            if (inspeccion
                    .ValidacionDimensional != true ||
                inspeccion
                    .ValidacionApariencia != true)
            {
                TempData["Error"] =
                    "Las validaciones dimensional y de apariencia deben ser conformes.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id }
                );
            }

            /*
             * Gauge y conductividad pueden ser:
             * true  = cumplen
             * null  = no aplican
             * false = no cumplen
             */
            if (inspeccion.ValidacionGauge == false ||
                inspeccion.ValidacionConductividad == false)
            {
                TempData["Error"] =
                    "Gauge o conductividad presentan un resultado no conforme.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id }
                );
            }

            var usuarioId =
                ObtenerUsuarioIdActual();

            var estadoAnterior =
                inspeccion.Estado;

            inspeccion.ResultadoCalidad =
                "VERDE";

            inspeccion.Etiqueta =
                "VERDE";

            inspeccion.Liberado =
                true;

            inspeccion.RequiereGP12 =
                false;

            inspeccion.EnContencion =
                false;

            inspeccion.EsScrap =
                false;

            inspeccion.Estado =
                CalidadEstados
                    .ProduccionLiberada;

            inspeccion.FechaLiberacionProduccion =
                DateTime.Now;

            inspeccion.UsuarioLiberacionProduccionID =
                usuarioId;

            MarcarModificacion(
                inspeccion,
                usuarioId
            );

            AgregarHistorial(
                inspeccion,
                CalidadMovimientos
                    .ProduccionLiberada,
                estadoAnterior,
                inspeccion.Estado,
                inspeccion.ResultadoCalidad,
                inspeccion.Etiqueta,
                "Calidad liberó la producción y asignó etiqueta verde.",
                usuarioId
            );

            await _context.SaveChangesAsync();

            TempData["Mensaje"] =
                "Producción liberada correctamente.";

            return RedirectToAction(
                nameof(Detalle),
                new { id }
            );
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EnviarContencion(
            int id,
            string? comentario)
        {
            var inspeccion =
                await _context.CalidadInspecciones
                    .FirstOrDefaultAsync(x =>
                        x.InspeccionID == id);

            if (inspeccion == null)
                return NotFound();

            var usuarioId =
                ObtenerUsuarioIdActual();

            var estadoAnterior =
                inspeccion.Estado;

            inspeccion.ResultadoCalidad =
                "NOK";

            inspeccion.Etiqueta =
                "ROJA";

            inspeccion.Liberado =
                false;

            inspeccion.RequiereGP12 =
                false;

            inspeccion.EnContencion =
                true;

            /*
             * Importante:
             * material no conforme no equivale automáticamente
             * a scrap.
             */
            inspeccion.EsScrap =
                false;

            inspeccion.Estado =
                CalidadEstados
                    .MaterialNoConforme;

            inspeccion.Observaciones =
                string.IsNullOrWhiteSpace(comentario)
                    ? inspeccion.Observaciones
                    : comentario.Trim();

            MarcarModificacion(
                inspeccion,
                usuarioId
            );

            AgregarHistorial(
                inspeccion,
                "MATERIAL_NO_CONFORME",
                estadoAnterior,
                inspeccion.Estado,
                inspeccion.ResultadoCalidad,
                inspeccion.Etiqueta,
                string.IsNullOrWhiteSpace(comentario)
                    ? "Material identificado como no conforme y bloqueado."
                    : comentario.Trim(),
                usuarioId
            );

            await _context.SaveChangesAsync();

            TempData["Mensaje"] =
                "Material identificado como no conforme.";

            return RedirectToAction(
                nameof(Detalle),
                new { id }
            );
        }

        // =========================================================
        // CIERRE
        // =========================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Cerrar(
            int id,
            string? observaciones)
        {
            return CerrarInspeccionCalidadAsync(
                id,
                observaciones);
        }

        // =========================================================
        // ACCIONES ANTERIORES DE COMPATIBILIDAD
        // =========================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Liberar(
            int id)
        {
            return LiberarProduccion(id);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Detener(
            int id)
        {
            var inspeccion =
                await _context.CalidadInspecciones
                    .FirstOrDefaultAsync(x =>
                        x.InspeccionID == id);

            if (inspeccion == null)
                return NotFound();

            var usuarioId =
                ObtenerUsuarioIdActual();

            var estadoAnterior =
                inspeccion.Estado;

            inspeccion.Liberado =
                false;

            inspeccion.ResultadoCalidad =
                "NOK";

            inspeccion.Estado =
                CalidadEstados
                    .AjustesSolicitados;

            MarcarModificacion(
                inspeccion,
                usuarioId
            );

            AgregarHistorial(
                inspeccion,
                CalidadMovimientos
                    .AjustesSolicitados,
                estadoAnterior,
                inspeccion.Estado,
                inspeccion.ResultadoCalidad,
                inspeccion.Etiqueta,
                "Proceso detenido y devuelto para corrección.",
                usuarioId
            );

            await _context.SaveChangesAsync();

            TempData["Mensaje"] =
                "Proceso detenido y enviado a corrección.";

            return RedirectToAction(
                nameof(Detalle),
                new { id }
            );
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult MarcarScrap(
            int id)
        {
            /*
             * El flujo actual no confirma que Calidad pueda
             * convertir automáticamente material NOK en scrap.
             */
            TempData["Error"] =
                "El material no puede marcarse automáticamente como scrap. Debe permanecer bloqueado hasta definir su disposición.";

            return RedirectToAction(
                nameof(Detalle),
                new { id }
            );
        }

        // =========================================================
        // CREACIÓN MANUAL ANTERIOR
        // Se conserva hasta actualizar completamente las vistas.
        // =========================================================

        [HttpGet]
        public IActionResult Crear()
        {
            var model =
                new CalidadFormViewModel
                {
                    CantidadTotal = 0,
                    CantidadRevisada = 0
                };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Crear(
            CalidadFormViewModel model)
        {
            NormalizarModelo(model);

            if (model.CantidadRevisada >
                model.CantidadTotal)
            {
                ModelState.AddModelError(
                    nameof(model.CantidadRevisada),
                    "La cantidad revisada no puede ser mayor a la cantidad total."
                );
            }

            if (!string.IsNullOrWhiteSpace(
                    model.ResultadoCalidad))
            {
                var documentosCompletos =
                    model.ChecklistValidado &&
                    model.HojaInspeccionProducto &&
                    model.HojaValidacionCalidad;

                if (!documentosCompletos)
                {
                    ModelState.AddModelError(
                        "",
                        "Para registrar un resultado primero debes validar la documentación."
                    );
                }
            }

            if (!ModelState.IsValid)
                return View(model);

            var usuarioId =
                ObtenerUsuarioIdActual();

            var inspeccion =
                new CalidadInspeccion
                {
                    CodigoBarras =
                        model.CodigoBarras,

                    OrdenTrabajo =
                        model.OrdenTrabajo,

                    NumeroParte =
                        model.NumeroParte,

                    Material =
                        model.Material,

                    Proceso =
                        model.Proceso,

                    Maquina =
                        model.Maquina,

                    CantidadTotal =
                        model.CantidadTotal,

                    CantidadRevisada =
                        model.CantidadRevisada,

                    CantidadPendiente =
                        CalcularPendiente(
                            model.CantidadTotal,
                            model.CantidadRevisada
                        ),

                    ChecklistValidado =
                        model.ChecklistValidado,

                    HojaInspeccionProducto =
                        model.HojaInspeccionProducto,

                    HojaValidacionCalidad =
                        model.HojaValidacionCalidad,

                    ResultadoCalidad =
                        model.ResultadoCalidad,

                    Observaciones =
                        model.Observaciones,

                    Estado =
                        CalidadEstados
                            .LegacyAbierta,

                    UsuarioCreacionID =
                        usuarioId,

                    FechaCreacion =
                        DateTime.Now
                };

            AplicarResultadoLegado(
                inspeccion
            );

            _context.CalidadInspecciones
                .Add(inspeccion);

            await _context.SaveChangesAsync();

            AgregarHistorial(
                inspeccion,
                "CREACION_MANUAL_LEGADA",
                null,
                inspeccion.Estado,
                inspeccion.ResultadoCalidad,
                inspeccion.Etiqueta,
                "Registro manual creado desde la vista anterior.",
                usuarioId
            );

            await _context.SaveChangesAsync();

            TempData["Mensaje"] =
                "Inspección registrada correctamente.";

            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // LECTURA DE PLANEACIÓN
        // =========================================================

        private async Task<CalidadCorridaOrigenViewModel?>
            ObtenerCorridaOrigenAsync(
                int programaProduccionId)
        {
            const string sql = @"
SELECT
    pp.ProgramaProduccionID,

    pp.ReleaseID,
    pp.ReleaseDetalleID,

    pp.SolicitudProduccionID,
    pp.SolicitudProduccionDetalleID,

    COALESCE(
        NULLIF(sp.NumeroOFRecibida, ''),
        NULLIF(sp.FolioSolicitud, '')
    ) AS NumeroOF,

    pp.ClienteID,
    pp.ClienteNombre,

    pp.ParteID,
    pp.NumeroParte,
    pp.ReferenciaSAP,
    pp.DesignacionDescripcionSAP,

    pp.MaquinaID,
    pp.MaquinaCodigo,
    pp.MaquinaNombre,

    pp.MoldeID,
    pp.MoldeCodigo,

    COALESCE(
        pp.MaterialID,
        t.MaterialID
    ) AS MaterialID,

    COALESCE(
        NULLIF(pp.MaterialCodigo, ''),
        t.MaterialCodigo
    ) AS MaterialCodigo,

    COALESCE(
        NULLIF(pp.MaterialDescripcion, ''),
        t.MaterialDescripcion
    ) AS MaterialDescripcion,

    ISNULL(pp.CantidadProgramada, 0)
        AS CantidadProgramada,

    ISNULL(pp.CantidadProducida, 0)
        AS CantidadProducida,

    pp.FechaInicioProgramada,
    pp.FechaFinProgramada,

    ISNULL(pp.EstatusID, 1)
        AS EstatusProgramaID

FROM dbo.Planeacion_ProgramaProduccion pp

LEFT JOIN dbo.SolicitudesProduccion sp
    ON sp.SolicitudProduccionID =
       pp.SolicitudProduccionID

LEFT JOIN dbo.ERP_ParteDatosTecnicos t
    ON t.ParteID = pp.ParteID
   AND t.Activo = 1

WHERE pp.ProgramaProduccionID =
      @ProgramaProduccionID

  AND pp.Activo = 1;";

            await using var cn =
                new SqlConnection(
                    ConnectionString
                );

            await cn.OpenAsync();

            CalidadCorridaOrigenViewModel?
                corrida = null;

            await using (
                var cmd =
                    new SqlCommand(sql, cn))
            {
                cmd.Parameters.Add(
                    "@ProgramaProduccionID",
                    SqlDbType.Int
                ).Value =
                    programaProduccionId;

                await using var rd =
                    await cmd.ExecuteReaderAsync();

                if (!await rd.ReadAsync())
                    return null;

                corrida =
                    new CalidadCorridaOrigenViewModel
                    {
                        ProgramaProduccionID =
                            Convert.ToInt32(
                                rd["ProgramaProduccionID"]
                            ),

                        ReleaseID =
                            LeerIntNullable(
                                rd["ReleaseID"]
                            ),

                        ReleaseDetalleID =
                            LeerIntNullable(
                                rd["ReleaseDetalleID"]
                            ),

                        SolicitudProduccionID =
                            LeerIntNullable(
                                rd["SolicitudProduccionID"]
                            ),

                        SolicitudProduccionDetalleID =
                            LeerIntNullable(
                                rd["SolicitudProduccionDetalleID"]
                            ),

                        NumeroOF =
                            rd["NumeroOF"] as string,

                        ClienteID =
                            LeerIntNullable(
                                rd["ClienteID"]
                            ),

                        ClienteNombre =
                            rd["ClienteNombre"] as string,

                        ParteID =
                            LeerIntNullable(
                                rd["ParteID"]
                            ),

                        NumeroParte =
                            rd["NumeroParte"] as string,

                        ReferenciaSAP =
                            rd["ReferenciaSAP"] as string,

                        DescripcionParte =
                            rd["DesignacionDescripcionSAP"]
                                as string,

                        MaquinaID =
                            LeerIntNullable(
                                rd["MaquinaID"]
                            ),

                        MaquinaCodigo =
                            rd["MaquinaCodigo"] as string,

                        MaquinaNombre =
                            rd["MaquinaNombre"] as string,

                        MoldeID =
                            LeerIntNullable(
                                rd["MoldeID"]
                            ),

                        MoldeCodigo =
                            rd["MoldeCodigo"] as string,

                        MaterialID =
                            LeerIntNullable(
                                rd["MaterialID"]
                            ),

                        MaterialCodigo =
                            rd["MaterialCodigo"] as string,

                        MaterialDescripcion =
                            rd["MaterialDescripcion"]
                                as string,

                        CantidadProgramada =
                            Convert.ToInt32(
                                rd["CantidadProgramada"]
                            ),

                        CantidadProducida =
                            Convert.ToInt32(
                                rd["CantidadProducida"]
                            ),

                        FechaInicioProgramada =
                            LeerFechaNullable(
                                rd["FechaInicioProgramada"]
                            ),

                        FechaFinProgramada =
                            LeerFechaNullable(
                                rd["FechaFinProgramada"]
                            ),

                        EstatusProgramaID =
                            Convert.ToInt32(
                                rd["EstatusProgramaID"]
                            )
                    };
            }

            const string sqlOperadores = @"
SELECT
    po.PersonaID,
    po.RolOperador,

    LTRIM(RTRIM(
        ISNULL(p.Nombre, '') + ' ' +
        ISNULL(p.ApellidoPaterno, '') + ' ' +
        ISNULL(p.ApellidoMaterno, '')
    )) AS NombreCompleto

FROM dbo.Planeacion_ProgramaOperadores po

LEFT JOIN dbo.Persona p
    ON p.PersonaID = po.PersonaID

WHERE po.ProgramaProduccionID =
      @ProgramaProduccionID

  AND po.Activo = 1

ORDER BY
    CASE
        WHEN po.RolOperador = 'PRINCIPAL'
            THEN 1
        WHEN po.RolOperador = 'AUXILIAR'
            THEN 2
        ELSE 3
    END,
    po.ProgramaOperadorID;";

            await using (
                var cmd =
                    new SqlCommand(
                        sqlOperadores,
                        cn
                    ))
            {
                cmd.Parameters.Add(
                    "@ProgramaProduccionID",
                    SqlDbType.Int
                ).Value =
                    programaProduccionId;

                await using var rd =
                    await cmd.ExecuteReaderAsync();

                while (await rd.ReadAsync())
                {
                    var personaId =
                        Convert.ToInt32(
                            rd["PersonaID"]
                        );

                    var rol =
                        rd["RolOperador"]
                            ?.ToString()
                            ?.Trim()
                            .ToUpperInvariant();

                    var nombre =
                        rd["NombreCompleto"]
                            as string;

                    if (rol == "PRINCIPAL" &&
                        !corrida
                            .OperadorPrincipalPersonaID
                            .HasValue)
                    {
                        corrida
                            .OperadorPrincipalPersonaID =
                            personaId;

                        corrida
                            .OperadorPrincipalNombre =
                            nombre;
                    }
                    else if (
                        rol == "AUXILIAR" &&
                        !corrida
                            .OperadorAuxiliarPersonaID
                            .HasValue)
                    {
                        corrida
                            .OperadorAuxiliarPersonaID =
                            personaId;

                        corrida
                            .OperadorAuxiliarNombre =
                            nombre;
                    }
                }
            }

            return corrida;
        }

        private async Task<(bool Valida, string Motivo)>
    ValidarConfiguracionActualAsync(
        CalidadInspeccion inspeccion)
        {
            /*
             * ============================================================
             * REGISTROS MANUALES ANTERIORES
             * ============================================================
             *
             * Una inspección manual puede no tener programa de Planeación.
             * En ese caso no existe una corrida contra la cual comparar.
             */
            if (!inspeccion.ProgramaProduccionID.HasValue ||
                inspeccion.ProgramaProduccionID.Value <= 0)
            {
                return (
                    true,
                    string.Empty
                );
            }

            /*
             * ============================================================
             * CONSULTAR CONFIGURACIÓN ACTUAL DE PLANEACIÓN
             * ============================================================
             */
            var actual =
                await ObtenerCorridaOrigenAsync(
                    inspeccion.ProgramaProduccionID.Value
                );

            if (actual == null)
            {
                return (
                    false,
                    "La corrida de Planeación ya no existe o fue desactivada."
                );
            }

            var cambiosCriticos =
                new List<string>();

            /*
             * ============================================================
             * DATOS QUE SÍ INVALIDAN LA CONFIGURACIÓN DE CALIDAD
             * ============================================================
             */

            if (actual.SolicitudProduccionID !=
                inspeccion.SolicitudProduccionID)
            {
                cambiosCriticos.Add(
                    "la Orden de Fabricación"
                );
            }

            if (actual.SolicitudProduccionDetalleID !=
                inspeccion.SolicitudProduccionDetalleID)
            {
                cambiosCriticos.Add(
                    "el renglón de la OF"
                );
            }

            if (actual.ParteID !=
                inspeccion.ParteID)
            {
                cambiosCriticos.Add(
                    "la parte"
                );
            }

            if (actual.MaquinaID !=
                inspeccion.MaquinaID)
            {
                cambiosCriticos.Add(
                    "la máquina"
                );
            }

            if (actual.MoldeID !=
                inspeccion.MoldeID)
            {
                cambiosCriticos.Add(
                    "el molde"
                );
            }

            if (actual.MaterialID !=
                inspeccion.MaterialID)
            {
                cambiosCriticos.Add(
                    "el material"
                );
            }

            if (FechasDiferentes(
                actual.FechaInicioProgramada,
                inspeccion.FechaInicioProgramada))
            {
                cambiosCriticos.Add(
                    "la fecha u hora de inicio"
                );
            }

            if (FechasDiferentes(
                actual.FechaFinProgramada,
                inspeccion.FechaFinProgramada))
            {
                cambiosCriticos.Add(
                    "la fecha u hora de término"
                );
            }

            if (actual.EstatusProgramaID == 99)
            {
                cambiosCriticos.Add(
                    "el programa fue cancelado"
                );
            }

           

            if (cambiosCriticos.Count == 0)
            {
                return (
                    true,
                    string.Empty
                );
            }

            return (
                false,
                "La configuración autorizada ya no coincide con Planeación. " +
                "Cambió: " +
                string.Join(", ", cambiosCriticos) +
                ". Debe generarse una nueva revisión de Calidad."
            );
        }

        private async Task InvalidarConfiguracionAsync(
            CalidadInspeccion inspeccion,
            string motivo)
        {
            var usuarioId =
                ObtenerUsuarioIdActual();

            var estadoAnterior =
                inspeccion.Estado;

            inspeccion.ConfiguracionInvalidada =
                true;

            inspeccion.FechaInvalidacion =
                DateTime.Now;

            inspeccion.UsuarioInvalidacionID =
                usuarioId;

            inspeccion.MotivoInvalidacion =
                motivo;

            inspeccion.MotivoDevolucion =
                motivo;

            inspeccion.Liberado =
                false;

            inspeccion.Estado =
                CalidadEstados
                    .DevueltoPrearranque;

            MarcarModificacion(
                inspeccion,
                usuarioId
            );

            AgregarHistorial(
                inspeccion,
                CalidadMovimientos
                    .ConfiguracionInvalidada,
                estadoAnterior,
                inspeccion.Estado,
                inspeccion.ResultadoCalidad,
                inspeccion.Etiqueta,
                motivo,
                usuarioId
            );

            await _context.SaveChangesAsync();
        }

        // =========================================================
        // HELPERS DE HISTORIAL Y MODELOS
        // =========================================================

        private void AgregarHistorial(
            CalidadInspeccion inspeccion,
            string movimiento,
            string? estadoAnterior,
            string? estadoNuevo,
            string? resultadoCalidad,
            string? etiqueta,
            string? comentario,
            int? usuarioId)
        {
            var historial =
                new CalidadInspeccionHistorial
                {
                    InspeccionID =
                        inspeccion.InspeccionID,

                    Movimiento =
                        movimiento,

                    EstadoAnterior =
                        estadoAnterior,

                    EstadoNuevo =
                        estadoNuevo,

                    ResultadoCalidad =
                        resultadoCalidad,

                    Etiqueta =
                        etiqueta,

                    Comentario =
                        comentario,

                    UsuarioID =
                        usuarioId,

                    FechaMovimiento =
                        DateTime.Now
                };

            _context
                .CalidadInspeccionHistorial
                .Add(historial);
        }

        private static void AplicarPrimerasPiezas(
            CalidadInspeccion inspeccion,
            CalidadPrimerasPiezasViewModel model,
            int? usuarioId)
        {
            inspeccion.CincoDisparosSegregados =
                model.CincoDisparosSegregados;

            inspeccion.CantidadDisparosConformes =
                model.CantidadDisparosConformes;

            inspeccion.ValidacionDimensional =
                model.ValidacionDimensional;

            inspeccion.ValidacionApariencia =
                model.ValidacionApariencia;

            inspeccion.ValidacionGauge =
                model.ValidacionGauge;

            inspeccion.ValidacionConductividad =
                model.ValidacionConductividad;

            inspeccion.FechaValidacionPrimerasPiezas =
                DateTime.Now;

            inspeccion.UsuarioValidacionPrimerasPiezasID =
                usuarioId;

            if (!string.IsNullOrWhiteSpace(
                model.Observaciones))
            {
                inspeccion.Observaciones =
                    model.Observaciones.Trim();
            }
        }

        private static decimal CalcularPendiente(
            decimal total,
            decimal revisada)
        {
            var pendiente =
                total - revisada;

            return pendiente < 0
                ? 0
                : pendiente;
        }

        private static void NormalizarModelo(
            CalidadFormViewModel model)
        {
            model.CodigoBarras =
                model.CodigoBarras?.Trim();

            model.OrdenTrabajo =
                model.OrdenTrabajo?.Trim();

            model.NumeroParte =
                model.NumeroParte?.Trim();

            model.Material =
                model.Material?.Trim();

            model.Proceso =
                model.Proceso?.Trim();

            model.Maquina =
                model.Maquina?.Trim();

            model.ResultadoCalidad =
                model.ResultadoCalidad?
                    .Trim()
                    .ToUpperInvariant();

            model.Observaciones =
                model.Observaciones?.Trim();

            if (model.CantidadTotal < 0)
                model.CantidadTotal = 0;

            if (model.CantidadRevisada < 0)
                model.CantidadRevisada = 0;
        }

        private static void AplicarResultadoLegado(
            CalidadInspeccion inspeccion)
        {
            inspeccion.Liberado =
                false;

            inspeccion.RequiereGP12 =
                false;

            inspeccion.EnContencion =
                false;

            inspeccion.EsScrap =
                false;

            if (string.IsNullOrWhiteSpace(
                inspeccion.ResultadoCalidad))
            {
                inspeccion.Etiqueta =
                    null;

                inspeccion.Estado =
                    CalidadEstados
                        .LegacyAbierta;

                return;
            }

            switch (
                inspeccion
                    .ResultadoCalidad
                    .ToUpperInvariant())
            {
                case "VERDE":
                    inspeccion.Etiqueta =
                        "VERDE";

                    inspeccion.Liberado =
                        true;

                    inspeccion.Estado =
                        CalidadEstados
                            .LegacyLiberada;

                    break;

                case "AMARILLO":
                    inspeccion.Etiqueta =
                        "AMARILLA";

                    inspeccion.RequiereGP12 =
                        true;

                    inspeccion.Estado =
                        CalidadEstados
                            .PendienteGP12;

                    break;

                case "ROJO":
                    inspeccion.Etiqueta =
                        "ROJA";

                    inspeccion.EnContencion =
                        true;

                    inspeccion.EsScrap =
                        false;

                    inspeccion.Estado =
                        CalidadEstados
                            .MaterialNoConforme;

                    break;

                default:
                    inspeccion.ResultadoCalidad =
                        null;

                    inspeccion.Etiqueta =
                        null;

                    inspeccion.Estado =
                        CalidadEstados
                            .LegacyAbierta;

                    break;
            }
        }

        private static void MarcarModificacion(
            CalidadInspeccion inspeccion,
            int? usuarioId)
        {
            inspeccion.UsuarioModificacionID =
                usuarioId;

            inspeccion.FechaModificacion =
                DateTime.Now;
        }

        // =========================================================
        // HELPERS GENERALES
        // =========================================================

        private static List<string>
            ObtenerFaltantesCorrida(
                CalidadCorridaOrigenViewModel corrida)
        {
            var faltantes =
                new List<string>();

            if (!corrida
                .SolicitudProduccionID
                .HasValue)
            {
                faltantes.Add(
                    "Orden de Fabricación"
                );
            }

            if (!corrida
                .SolicitudProduccionDetalleID
                .HasValue)
            {
                faltantes.Add(
                    "renglón de la OF"
                );
            }

            if (string.IsNullOrWhiteSpace(
                corrida.NumeroOF))
            {
                faltantes.Add(
                    "número o folio de OF"
                );
            }

            if (!corrida.ParteID.HasValue)
            {
                faltantes.Add(
                    "parte"
                );
            }

            if (!corrida.MaquinaID.HasValue)
            {
                faltantes.Add(
                    "máquina"
                );
            }

            if (!corrida.MoldeID.HasValue)
            {
                faltantes.Add(
                    "molde"
                );
            }

            if (!corrida.MaterialID.HasValue)
            {
                faltantes.Add(
                    "material"
                );
            }

            if (!corrida
                .OperadorPrincipalPersonaID
                .HasValue)
            {
                faltantes.Add(
                    "operador principal"
                );
            }

            if (corrida.CantidadProgramada <= 0)
            {
                faltantes.Add(
                    "cantidad programada"
                );
            }

            return faltantes;
        }

        private static string?
            PrimerTextoDisponible(
                params string?[] valores)
        {
            return valores
                .FirstOrDefault(x =>
                    !string.IsNullOrWhiteSpace(x))
                ?.Trim();
        }

        private static string?
            UnirCodigoDescripcion(
                string? codigo,
                string? descripcion)
        {
            codigo =
                codigo?.Trim();

            descripcion =
                descripcion?.Trim();

            if (string.IsNullOrWhiteSpace(codigo))
                return descripcion;

            if (string.IsNullOrWhiteSpace(descripcion))
                return codigo;

            if (string.Equals(
                codigo,
                descripcion,
                StringComparison.OrdinalIgnoreCase))
            {
                return codigo;
            }

            return codigo +
                   " | " +
                   descripcion;
        }

        private static int?
            LeerIntNullable(
                object valor)
        {
            return valor == DBNull.Value
                ? null
                : Convert.ToInt32(valor);
        }

        private static DateTime?
            LeerFechaNullable(
                object valor)
        {
            return valor == DBNull.Value
                ? null
                : Convert.ToDateTime(valor);
        }

        private static bool FechasDiferentes(
            DateTime? fechaA,
            DateTime? fechaB)
        {
            if (!fechaA.HasValue &&
                !fechaB.HasValue)
            {
                return false;
            }

            if (fechaA.HasValue !=
                fechaB.HasValue)
            {
                return true;
            }

            return Math.Abs(
                (
                    fechaA!.Value -
                    fechaB!.Value
                ).TotalMinutes
            ) > 1;
        }

        private int?
            ObtenerUsuarioIdActual()
        {
            var claimValue =
                User.FindFirst("UsuarioID")
                    ?.Value ??

                User.FindFirst("UserId")
                    ?.Value ??

                User.FindFirst(
                    ClaimTypes.NameIdentifier
                )?.Value;

            if (int.TryParse(
                    claimValue,
                    out var usuarioId) &&
                usuarioId > 0)
            {
                return usuarioId;
            }

            try
            {
                var sessionId =
                    HttpContext.Session
                        .GetInt32("UsuarioID");

                if (sessionId.HasValue &&
                    sessionId.Value > 0)
                {
                    return sessionId.Value;
                }
            }
            catch
            {
                // Session no disponible.
            }

            return null;
        }
    }
}