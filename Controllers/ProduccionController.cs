using ERP.NSQuell.Models;
using ERP.NSQuell.Servicios.Planeacion;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using static ERP.NSQuell.Models.ProduccionEjecucionVm;


namespace ERP.NSQuell.Controllers
{
    public sealed partial class ProduccionController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly IPlaneacionSecuenciaService _planeacionSecuenciaService;

        public ProduccionController(
            IConfiguration configuration,
            IPlaneacionSecuenciaService planeacionSecuenciaService)
        {
            _configuration = configuration;
            _planeacionSecuenciaService = planeacionSecuenciaService;
        }

        private sealed class ProduccionPermisosUsuario
        {
            public int UsuarioID { get; set; }
            public int? PersonaID { get; set; }
            public int? RolID { get; set; }
            public string Nombre { get; set; } = string.Empty;
            public string Puesto { get; set; } = string.Empty;
            public bool EsAdministradorERP { get; set; }
            public bool EsEncargadoProduccion { get; set; }
            public bool EsTecnicoProduccion { get; set; }
            public bool EsSMED { get; set; }
            public bool EsAuxiliarProduccion { get; set; }
            public bool EsOperadorProduccion { get; set; }
            public bool PuedeVerTodo => EsAdministradorERP || EsEncargadoProduccion;
            public bool PuedeGestionarChecklistArranque => PuedeVerTodo || EsTecnicoProduccion || EsSMED;
            public bool PuedeGestionarSMED => PuedeVerTodo || EsTecnicoProduccion || EsSMED;
            public bool PuedeGestionarMonitoreoPerifericos => PuedeVerTodo || EsTecnicoProduccion || EsSMED;
            public bool PuedeGestionarCajas => PuedeVerTodo || EsAuxiliarProduccion;
            public bool PuedeVerCapturasHora => PuedeVerTodo || EsAuxiliarProduccion || EsOperadorProduccion;
            public bool PuedeCapturarHora => PuedeVerTodo || EsOperadorProduccion;
            public bool PuedeGestionarSugerenciaCambioTurno => PuedeVerTodo || EsAuxiliarProduccion;
        }


        private string ConnectionString =>
            _configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("No se encontró la cadena de conexión DefaultConnection.");


        [HttpGet]
        public async Task<IActionResult> Index(string? busqueda = null, int? maquinaId = null, int? estatusId = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null)
        {
            if (!UsuarioEnSesion())
                return RedirectToAction("Login", "Login");

            var vm = new ProduccionBandejaVm
            {
                Busqueda = string.IsNullOrWhiteSpace(busqueda) ? null : busqueda.Trim(),
                MaquinaID = maquinaId,
                EstatusID = estatusId,
                FechaDesde = fechaDesde,
                FechaHasta = fechaHasta
            };

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            // NSQ_PREPARACION_MOLDE_INDEX_V1
            ViewBag.EstadosCambioMolde =
                await ObtenerEstadosCambioMoldeBandejaAsync(cn);

            vm.Maquinas = await CargarMaquinasAsync(cn);
            vm.Estatus = await CargarEstatusProduccionAsync(cn);
            ViewBag.OperadoresProduccion = await CargarOperadoresProduccionAsync(cn);

            vm.ProgramasDisponibles = await ObtenerProgramasDisponiblesAsync(busqueda, maquinaId, fechaDesde, fechaHasta, cn);

            var ahora = DateTime.Now;
            var limiteProximos = ahora.AddMinutes(15);
            var maquinasOcupadas = await ObtenerMaquinasOcupadasAsync(cn);
            ViewBag.MaquinasOcupadas = maquinasOcupadas;

            vm.ProximosAIniciar = vm.ProgramasDisponibles
                .Where(x => x.PuedeIniciar &&
                            x.MaquinaID.HasValue &&
                            x.FechaInicioProgramada.HasValue &&
                            x.FechaInicioProgramada.Value <= limiteProximos)
                .OrderBy(x => x.FechaInicioProgramada)
                .ThenBy(x => x.MaquinaCodigo)
                .ThenBy(x => x.ProgramaProduccionID)
                .ToList();

            try
            {
                vm.AlertasReprogramacion = await ObtenerAlertasReprogramacionProduccionAsync(maquinaId, cn);
            }
            catch (Exception ex)
            {
                vm.AlertasReprogramacion = new List<ProduccionAlertaReprogramacionVm>();
                ViewBag.ErrorAlertasReprogramacion = "No fue posible consultar los movimientos recientes: " + ex.Message;
            }

            vm.Ejecuciones = await ObtenerEjecucionesPanelAsync(estatusId, maquinaId, busqueda, fechaDesde, fechaHasta, cn);

            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> Detalle(int id)
        {
            if (!UsuarioEnSesion())
                return RedirectToAction("Login", "Login");

            if (id <= 0)
                return NotFound();

            var usuarioId = ObtenerUsuarioID();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var ejecucion = await ObtenerEjecucionAsync(id, cn);

            if (ejecucion == null)
                return NotFound();

            var permisos =
                await ObtenerPermisosProduccionUsuarioAsync(
                    usuarioId,
                    cn);

            ViewBag.UsuarioProduccionID = permisos.UsuarioID;
            ViewBag.PersonaProduccionID = permisos.PersonaID;
            ViewBag.NombreProduccionUsuario = permisos.Nombre;
            ViewBag.PuestoProduccionUsuario = permisos.Puesto;
            ViewBag.EsAdministradorERP = permisos.EsAdministradorERP;
            ViewBag.EsEncargadoProduccion = permisos.EsEncargadoProduccion;
            ViewBag.EsTecnicoProduccion = permisos.EsTecnicoProduccion;
            ViewBag.EsSMED = permisos.EsSMED;
            ViewBag.EsAuxiliarProduccion = permisos.EsAuxiliarProduccion;
            ViewBag.EsOperadorProduccion = permisos.EsOperadorProduccion;
            ViewBag.PuedeVerTodoProduccion = permisos.PuedeVerTodo;
            ViewBag.PuedeGestionarChecklistArranque = permisos.PuedeGestionarChecklistArranque;
            ViewBag.PuedeGestionarSMED = permisos.PuedeGestionarSMED;
            ViewBag.PuedeGestionarMonitoreoPerifericos = permisos.PuedeGestionarMonitoreoPerifericos;
            ViewBag.PuedeGestionarCajas = permisos.PuedeGestionarCajas;
            ViewBag.PuedeVerCapturasHora = permisos.PuedeVerCapturasHora;
            ViewBag.PuedeCapturarHora = permisos.PuedeCapturarHora;
            ViewBag.PuedeGestionarSugerenciaCambioTurno = permisos.PuedeGestionarSugerenciaCambioTurno;

            ViewBag.OperadoresProduccion =
                await CargarOperadoresProduccionAsync(cn);

            ProduccionMonitoreoTurnoAvisoVm? monitoreoTurnoActual = null;

            var ejecucionActivaParaMonitoreo =
                ejecucion.EstatusID == ProduccionEstatus.EnPreparacion ||
                ejecucion.EstatusID == ProduccionEstatus.EnProduccion ||
                ejecucion.EstatusID == ProduccionEstatus.Pausado;

            if (ejecucionActivaParaMonitoreo &&
                ejecucion.SolicitudProduccionID.HasValue &&
                ejecucion.SolicitudProduccionID.Value > 0)
            {
                await using var tx =
                    (SqlTransaction)await cn.BeginTransactionAsync();

                try
                {
                    var checklistPerifericosId =
                        await ObtenerOCrearChecklistPerifericosTurnoAsync(
                            ejecucion,
                            DateTime.Now,
                            usuarioId,
                            cn,
                            tx);

                    await tx.CommitAsync();

                    monitoreoTurnoActual =
                        await ObtenerAvisoMonitoreoTurnoAsync(
                            checklistPerifericosId,
                            cn);
                }
                catch (Exception ex)
                {
                    try
                    {
                        await tx.RollbackAsync();
                    }
                    catch
                    {
                    }

                    TempData["Error"] =
                        "No fue posible preparar el monitoreo de periféricos del turno actual: " +
                        ex.Message;
                }
            }

            var calidadResumen =
                await ObtenerResumenCalidadAsync(
                    id,
                    cn);

            var vm = new ProduccionDetalleVm
            {
                Ejecucion = ejecucion,
                ParejaLhRh = await ObtenerParejaLhRhProduccionAsync(ejecucion.ProgramaProduccionID, cn),
                RegistrosHora = await ObtenerRegistrosHoraAsync(id, cn),
                Paros = await ObtenerParosAsync(id, cn),
                MotivosParo = await CargarMotivosParoAsync(cn),
                ChecklistResumen = await ObtenerResumenChecklistArranqueAsync(id, cn),
                CalidadResumen = calidadResumen,
                MonitoreoTurnoActual = monitoreoTurnoActual,
                CambioTurnoTecnico = await ConstruirCambioTurnoTecnicoAsync(ejecucion, cn)
            };

           
            var contextoConfiguracion =
                await ObtenerContextoConfiguracionCorridaAsync(
                    id,
                    cn);

            if (contextoConfiguracion != null)
            {
                vm.ConfiguracionTiempoReal =
                    await ConstruirConfiguracionTecnicoAsync(
                        contextoConfiguracion,
                        cn);

                ViewBag.PuedeGestionarConfiguracionCorrida =
                    PuedeModificarConfiguracionCorrida(
                        permisos,
                        contextoConfiguracion)
                    &&
                    EjecucionPermiteConfiguracionCorrida(
                        contextoConfiguracion);
            }
            else
            {
                vm.ConfiguracionTiempoReal = null;

                ViewBag.PuedeGestionarConfiguracionCorrida = false;
            }

            // Esta bandera nos servirá en la vista para avisar
            // que falta la configuración del técnico.
            ViewBag.FaltaConfiguracionCorrida =
                ejecucionActivaParaMonitoreo &&
                (
                    vm.ConfiguracionTiempoReal == null ||
                    !vm.ConfiguracionTiempoReal.TieneConfiguracionActual
                );

            vm.RecepcionesOF =
                await ObtenerEntregasAlmacenOFAsync(
                    ejecucion,
                    cn,
                    null);

            ViewBag.EsReinicioSerie =
                ejecucion.EstatusID == ProduccionEstatus.EnPreparacion &&
                await EsReinicioSeriePendienteAsync(
                    id,
                    cn);

            return View(vm);
        }

        private async Task<List<ProduccionRecepcionOFVm>>
    ObtenerEntregasAlmacenOFAsync(
        ProduccionEjecucionVm ejecucion,
        SqlConnection cn,
        SqlTransaction? tx)
        {
            var lista = new List<ProduccionRecepcionOFVm>();

            if (!ejecucion.SolicitudProduccionID.HasValue ||
                ejecucion.SolicitudProduccionID.Value <= 0)
            {
                return lista;
            }

            const string sql = @"
DECLARE @FolioSolicitud NVARCHAR(100);
DECLARE @NumeroOFRecibida NVARCHAR(100);

SELECT TOP (1)
    @FolioSolicitud =
        NULLIF(
            LTRIM(RTRIM(ISNULL(FolioSolicitud, N''))),
            N''
        ),

    @NumeroOFRecibida =
        NULLIF(
            LTRIM(RTRIM(ISNULL(NumeroOFRecibida, N''))),
            N''
        )
FROM dbo.SolicitudesProduccion
WHERE SolicitudProduccionID = @SolicitudProduccionID
  AND Activo = 1;

/* ============================================================
   MATERIA PRIMA Y COMPONENTES
   ============================================================ */
SELECT
    CONVERT(BIGINT, movimiento.MovimientoID) AS MovimientoID,
    N'MP' AS AreaAlmacen,

    ISNULL(movimiento.TipoMovimiento, N'')
        AS TipoMovimiento,

    ISNULL(material.Codigo, N'')
        AS Codigo,

    ISNULL(material.Nombre, N'')
        AS Descripcion,

    ISNULL(movimiento.Lote, N'')
        AS Lote,

    ISNULL(movimiento.TipoMP, N'')
        AS NumeroUI,

    CONVERT
    (
        DECIMAL(18,4),
        CASE
            WHEN movimiento.TipoMovimiento = N'Retorno'
                THEN -ABS(ISNULL(movimiento.Cantidad, 0))
            ELSE ABS(ISNULL(movimiento.Cantidad, 0))
        END
    ) AS Cantidad,

    ISNULL
    (
        NULLIF(
            LTRIM(RTRIM(movimiento.Unidad)),
            N''
        ),
        N'KG'
    ) AS Unidad,

    ISNULL(movimiento.EntregadoPorNombre, N'')
        AS EntregadoPor,

    movimiento.FechaMovimiento
        AS FechaRecepcion,

    ISNULL(movimiento.Seguimiento, N'')
        AS Observaciones,

    ISNULL(movimiento.NumeroOF, N'')
        AS NumeroOF,

    ISNULL(movimiento.ReferenciaOperacion, N'')
        AS ReferenciaOperacion,

    movimiento.SolicitudProduccionID

FROM dbo.AlmacenMP_Movimientos movimiento

INNER JOIN dbo.ERP_Materiales material
    ON material.MaterialID =
       movimiento.MaterialID

WHERE movimiento.Activo = 1

  AND movimiento.TipoMovimiento IN
  (
      N'Salida',
      N'Consumo',
      N'Retorno'
  )

  AND
  (
      movimiento.SolicitudProduccionID =
          @SolicitudProduccionID

      OR
      (
          movimiento.SolicitudProduccionID IS NULL

          AND
          (
              (
                  @FolioSolicitud IS NOT NULL

                  AND LTRIM(
                      RTRIM(
                          ISNULL(
                              movimiento.NumeroOF,
                              N''
                          )
                      )
                  ) = @FolioSolicitud
              )

              OR

              (
                  @NumeroOFRecibida IS NOT NULL

                  AND LTRIM(
                      RTRIM(
                          ISNULL(
                              movimiento.NumeroOF,
                              N''
                          )
                      )
                  ) = @NumeroOFRecibida
              )
          )
      )
  )

UNION ALL

/* ============================================================
   EMBALAJES Y ETIQUETAS
   ============================================================ */
SELECT
    CONVERT(BIGINT, movimiento.MovimientoID) AS MovimientoID,
    N'EMBALAJE' AS AreaAlmacen,

    ISNULL(movimiento.TipoMovimiento, N'')
        AS TipoMovimiento,

    ISNULL(embalaje.Codigo, N'')
        AS Codigo,

    ISNULL(embalaje.Nombre, N'')
        AS Descripcion,

    ISNULL(movimiento.Lote, N'')
        AS Lote,

    N'' AS NumeroUI,

    CONVERT
    (
        DECIMAL(18,4),
        CASE
            WHEN movimiento.TipoMovimiento = N'Retorno'
                THEN -ABS(ISNULL(movimiento.Cantidad, 0))
            ELSE ABS(ISNULL(movimiento.Cantidad, 0))
        END
    ) AS Cantidad,

    ISNULL(movimiento.Unidad, N'')
        AS Unidad,

    ISNULL(movimiento.EntregadoPorNombre, N'')
        AS EntregadoPor,

    movimiento.FechaMovimiento
        AS FechaRecepcion,

    ISNULL(movimiento.Seguimiento, N'')
        AS Observaciones,

    ISNULL(movimiento.NumeroOF, N'')
        AS NumeroOF,

    ISNULL(movimiento.ReferenciaOperacion, N'')
        AS ReferenciaOperacion,

    movimiento.SolicitudProduccionID

FROM dbo.AlmacenEmbalajes_Movimientos movimiento

INNER JOIN dbo.ERP_Embalajes embalaje
    ON embalaje.EmbalajeID =
       movimiento.EmbalajeID

WHERE movimiento.Activo = 1

  AND movimiento.TipoMovimiento IN
  (
      N'Salida',
      N'Consumo',
      N'Retorno'
  )

  AND
  (
      movimiento.SolicitudProduccionID =
          @SolicitudProduccionID

      OR
      (
          movimiento.SolicitudProduccionID IS NULL

          AND
          (
              (
                  @FolioSolicitud IS NOT NULL

                  AND LTRIM(
                      RTRIM(
                          ISNULL(
                              movimiento.NumeroOF,
                              N''
                          )
                      )
                  ) = @FolioSolicitud
              )

              OR

              (
                  @NumeroOFRecibida IS NOT NULL

                  AND LTRIM(
                      RTRIM(
                          ISNULL(
                              movimiento.NumeroOF,
                              N''
                          )
                      )
                  ) = @NumeroOFRecibida
              )
          )
      )
  )

ORDER BY
    FechaRecepcion DESC,
    MovimientoID DESC;";

            using var cmd = tx == null
                ? new SqlCommand(sql, cn)
                : new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add(
                "@SolicitudProduccionID",
                SqlDbType.Int).Value =
                ejecucion.SolicitudProduccionID.Value;

            using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                var area =
                    rd["AreaAlmacen"] == DBNull.Value
                        ? string.Empty
                        : rd["AreaAlmacen"].ToString()
                          ?? string.Empty;

                var codigo =
                    rd["Codigo"] == DBNull.Value
                        ? null
                        : rd["Codigo"].ToString();

                var descripcion =
                    rd["Descripcion"] == DBNull.Value
                        ? null
                        : rd["Descripcion"].ToString();

                lista.Add(
                    new ProduccionRecepcionOFVm
                    {
                        RecepcionOFID = 0,

                        MovimientoID =
                            rd["MovimientoID"] == DBNull.Value
                                ? 0L
                                : Convert.ToInt64(
                                    rd["MovimientoID"]),

                        EjecucionProduccionID =
                            ejecucion.EjecucionProduccionID,

                        ProgramaProduccionID =
                            ejecucion.ProgramaProduccionID,

                        SolicitudProduccionID =
                            rd["SolicitudProduccionID"] == DBNull.Value
                                ? ejecucion.SolicitudProduccionID
                                : Convert.ToInt32(
                                    rd["SolicitudProduccionID"]),

                        OrigenRegistro = "ALMACEN",

                        TipoMovimiento =
                            rd["TipoMovimiento"] == DBNull.Value
                                ? string.Empty
                                : rd["TipoMovimiento"].ToString()
                                  ?? string.Empty,

                        TipoRecepcion =
                            ClasificarTipoRecepcionAlmacen(
                                area,
                                codigo,
                                descripcion),

                        Codigo = codigo,

                        Descripcion = descripcion,

                        Lote =
                            rd["Lote"] == DBNull.Value
                                ? null
                                : rd["Lote"].ToString(),

                        NumeroUI =
                            rd["NumeroUI"] == DBNull.Value
                                ? null
                                : rd["NumeroUI"].ToString(),

                        EtiquetaInicio = null,
                        EtiquetaFin = null,

                        Cantidad =
                            rd["Cantidad"] == DBNull.Value
                                ? null
                                : Convert.ToDecimal(
                                    rd["Cantidad"]),

                        Unidad =
                            rd["Unidad"] == DBNull.Value
                                ? null
                                : rd["Unidad"].ToString(),

                        EntregadoPor =
                            rd["EntregadoPor"] == DBNull.Value
                                ? null
                                : rd["EntregadoPor"].ToString(),

                        RecibidoPor =
                            string.IsNullOrWhiteSpace(
                                ejecucion.OperadorNombre)
                                ? "Producción"
                                : ejecucion.OperadorNombre,

                        FechaRecepcion =
                            rd["FechaRecepcion"] == DBNull.Value
                                ? DateTime.MinValue
                                : Convert.ToDateTime(
                                    rd["FechaRecepcion"]),

                        Observaciones =
                            rd["Observaciones"] == DBNull.Value
                                ? null
                                : rd["Observaciones"].ToString(),

                        NumeroOF =
                            rd["NumeroOF"] == DBNull.Value
                                ? null
                                : rd["NumeroOF"].ToString(),

                        ReferenciaOperacion =
                            rd["ReferenciaOperacion"] == DBNull.Value
                                ? null
                                : rd["ReferenciaOperacion"].ToString()
                    });
            }

            return lista;
        }


        private static string ClasificarTipoRecepcionAlmacen(
    string areaAlmacen,
    string? codigo,
    string? descripcion)
        {
            var area =
                areaAlmacen?
                    .Trim()
                    .ToUpperInvariant()
                ?? string.Empty;

            var texto =
                (
                    (codigo ?? string.Empty)
                    + " "
                    + (descripcion ?? string.Empty)
                )
                .Trim()
                .ToUpperInvariant();

            if (area == "EMBALAJE")
            {
                if (texto.Contains("ETIQUETA") ||
                    texto.Contains("LABEL") ||
                    texto.Contains("STICKER"))
                {
                    return "ETIQUETA";
                }

                return "EMBALAJE";
            }

            if (area == "MP")
            {
                if (texto.Contains("COMPONENTE") ||
                    texto.Contains("INSERTO") ||
                    texto.Contains("INSERT") ||
                    texto.Contains("BUJE") ||
                    texto.Contains("TORNILLO") ||
                    texto.Contains("TUERCA") ||
                    texto.Contains("ARANDELA") ||
                    texto.Contains("RESORTE"))
                {
                    return "COMPONENTE";
                }

                return "MP";
            }

            return area;
        }



        [HttpGet]
        public async Task<IActionResult> ChecklistArranque(
      int ejecucionProduccionId)
        {
            if (!UsuarioEnSesion())
                return RedirectToAction("Login", "Login");

            if (ejecucionProduccionId <= 0)
                return NotFound();

            var usuarioId = ObtenerUsuarioID();

            await using var cn =
                new SqlConnection(ConnectionString);

            await cn.OpenAsync();

            var ejecucion =
                await ObtenerEjecucionAsync(
                    ejecucionProduccionId,
                    cn);

            if (ejecucion == null)
                return NotFound();

            int checklistArranqueId;

            await using (
                var tx = (SqlTransaction)
                    await cn.BeginTransactionAsync())
            {
                try
                {
                    /*
                     * Genera todos los formatos que corresponden
                     * al inicio de esta OF.
                     */
                    checklistArranqueId =
                        await ObtenerOCrearChecklistsInicialesAsync(
                            ejecucion,
                            usuarioId,
                            cn,
                            tx);

                    await tx.CommitAsync();
                }
                catch (Exception ex)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "No fue posible preparar los checklist: "
                        + ex.Message;

                    return RedirectToAction(
                        nameof(Detalle),
                        new
                        {
                            id = ejecucionProduccionId
                        });
                }
            }

            var checklist =
                await ObtenerChecklistArranqueAsync(
                    checklistArranqueId,
                    cn);

            if (checklist == null)
            {
                TempData["Error"] =
                    "No fue posible obtener el checklist de arranque.";

                return RedirectToAction(
                    nameof(Detalle),
                    new
                    {
                        id = ejecucionProduccionId
                    });
            }

            await CargarEstadoCalidadChecklistAsync(
                checklist,
                cn);

            return View(checklist);
        }



        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarChecklistArranque(
            ProduccionChecklistGuardarVm vm)
        {
            if (!UsuarioEnSesion())
                return RedirectToAction("Login", "Login");

            var usuarioId = ObtenerUsuarioID();

            if (vm.ChecklistArranqueID <= 0 ||
                vm.EjecucionProduccionID <= 0)
            {
                TempData["Error"] =
                    "No se recibió correctamente el checklist.";

                return RedirectToAction(nameof(Index));
            }

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();

            try
            {
                var estatusActual =
                    await ObtenerEstatusChecklistAsync(
                        vm.ChecklistArranqueID,
                        cn,
                        tx);

                if (!ProduccionChecklistEstatus.PuedeEditarProduccion(estatusActual))
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "Este checklist ya no puede ser editado por Producción.";

                    return RedirectToAction(
                        nameof(ChecklistArranque),
                        new { ejecucionProduccionId = vm.EjecucionProduccionID });
                }

                var respuestasRecibidas = vm.Respuestas ?? new List<ProduccionChecklistRespuestaPostVm>();

                if (!respuestasRecibidas.Any())
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "No se recibieron las respuestas del checklist. Revisa los nombres de los campos en la vista.";
                    return RedirectToAction(nameof(ChecklistFormato), new { id = vm.ChecklistArranqueID });
                }

                foreach (var respuesta in respuestasRecibidas)
                {
                    var resultadoNormalizado = NormalizarResultadoChecklist(respuesta.Resultado);

                    if (resultadoNormalizado == "__INVALIDO__")
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = "Una o más respuestas tienen un valor inválido.";
                        return RedirectToAction(nameof(ChecklistFormato), new { id = vm.ChecklistArranqueID });
                    }

                    if (resultadoNormalizado == null)
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = "Todas las verificaciones deben tener una respuesta OK, NOK o N/A.";
                        return RedirectToAction(nameof(ChecklistFormato), new { id = vm.ChecklistArranqueID });
                    }

                    await ActualizarRespuestaChecklistAsync(
                        respuesta.ChecklistArranqueDetalleID,
                        resultadoNormalizado,
                        respuesta.Observaciones,
                        true,
                        respuesta.ValorCapturado,
                        usuarioId,
                        cn,
                        tx);
                }

                if (vm.EnviarACalidad)
                {
                    var tienePreguntasSinRespuesta =
                        await TienePreguntasProduccionSinRespuestaAsync(
                            vm.ChecklistArranqueID,
                            cn,
                            tx);

                    if (tienePreguntasSinRespuesta)
                    {
                        await tx.RollbackAsync();

                        TempData["Error"] =
                            "Para enviar a Calidad debes responder todas las preguntas de Producción.";

                        return RedirectToAction(
                            nameof(ChecklistArranque),
                            new { ejecucionProduccionId = vm.EjecucionProduccionID });
                    }

                    var tieneNokSinObservacion =
                        await TieneNokSinObservacionAsync(
                            vm.ChecklistArranqueID,
                            cn,
                            tx);

                    if (tieneNokSinObservacion)
                    {
                        await tx.RollbackAsync();

                        TempData["Error"] =
                            "Las respuestas NOK requieren observación antes de enviar a Calidad.";

                        return RedirectToAction(
                            nameof(ChecklistArranque),
                            new { ejecucionProduccionId = vm.EjecucionProduccionID });
                    }

                    var tieneValoresSinCapturar = await TieneValoresRequeridosSinCapturarAsync(vm.ChecklistArranqueID, cn, tx);

                    if (tieneValoresSinCapturar)
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = "Faltan valores requeridos en una o más preguntas del monitoreo.";
                        return RedirectToAction(nameof(ChecklistFormato), new { id = vm.ChecklistArranqueID });
                    }
                }

                var nuevoEstatus =
                    vm.EnviarACalidad
                        ? ProduccionChecklistEstatus.PendienteValidacionCalidad
                        : ProduccionChecklistEstatus.CapturadoPorProduccion;

                await ActualizarEncabezadoChecklistProduccionAsync(
                    vm.ChecklistArranqueID,
                    nuevoEstatus,
                    vm.ObservacionesGenerales,
                    vm.EnviarACalidad,
                    usuarioId,
                    cn,
                    tx);

                if (vm.EnviarACalidad)
                {
                    await CrearOActualizarSolicitudCalidadAsync(
                        vm.ChecklistArranqueID,
                        vm.EjecucionProduccionID,
                        usuarioId,
                        cn,
                        tx);
                }

                await tx.CommitAsync();

                TempData["Success"] =
                    vm.EnviarACalidad
                        ? "Checklist capturado y enviado a validación de Calidad."
                        : "Checklist guardado correctamente.";

                return RedirectToAction(nameof(ChecklistFormato), new
                {
                    id = vm.ChecklistArranqueID
                });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                TempData["Error"] =
                    "No fue posible guardar el checklist: " + ex.Message;

                return RedirectToAction(nameof(ChecklistFormato), new
                {
                    id = vm.ChecklistArranqueID
                });
            }
        }


        private async Task CrearOActualizarSolicitudCalidadAsync(
        int checklistArranqueId,
        int ejecucionProduccionId,
        int usuarioId,
        SqlConnection cn,
        SqlTransaction tx)
        {
            var origen = await ObtenerOrigenSolicitudCalidadAsync(
                checklistArranqueId,
                ejecucionProduccionId,
                cn,
                tx);
            if (origen == null)
                throw new InvalidOperationException(
                    "No se encontró la información de la ejecución y el checklist para enviar a Calidad.");
            if (!origen.SolicitudProduccionID.HasValue ||
               origen.SolicitudProduccionID.Value <= 0)
                throw new InvalidOperationException(
                    "La ejecución no tiene una OF válida relacionada.");
            if (!origen.SolicitudProduccionDetalleID.HasValue ||
               origen.SolicitudProduccionDetalleID.Value <= 0)
                throw new InvalidOperationException(
                    "La ejecución no tiene un detalle de OF válido relacionado.");
            const string sqlInspeccionExistente = @"
SELECT TOP (1)
    InspeccionID,
    ChecklistArranqueID,
    Estado,
    ISNULL(ConfiguracionInvalidada,0) AS ConfiguracionInvalidada
FROM dbo.Calidad_Inspecciones WITH(UPDLOCK,HOLDLOCK)
WHERE EjecucionProduccionID=@EjecucionProduccionID
  AND ISNULL(Estado,N'')<>N'CERRADA'
ORDER BY
    CASE WHEN ISNULL(ConfiguracionInvalidada,0)=0 THEN 0 ELSE 1 END,
    InspeccionID DESC;";
            int? inspeccionId = null;
            int? checklistExistenteId = null;
            string? estadoExistente = null;
            var configuracionInvalidada = false;
            await using (var cmd = new SqlCommand(sqlInspeccionExistente, cn, tx))
            {
                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = origen.EjecucionProduccionID;
                await using var rd = await cmd.ExecuteReaderAsync();
                if (await rd.ReadAsync())
                {
                    inspeccionId = Convert.ToInt32(rd["InspeccionID"]);
                    checklistExistenteId = rd["ChecklistArranqueID"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(rd["ChecklistArranqueID"]);
                    estadoExistente = rd["Estado"] == DBNull.Value
                        ? null
                        : rd["Estado"]?.ToString()?.Trim();
                    configuracionInvalidada =
                        rd["ConfiguracionInvalidada"] != DBNull.Value &&
                        Convert.ToBoolean(rd["ConfiguracionInvalidada"]);
                }
            }
            if (inspeccionId.HasValue)
            {
                if (configuracionInvalidada)
                    throw new InvalidOperationException(
                        "La inspección actual fue invalidada y requiere un proceso de reliberación. No debe generarse una inspección nueva.");
                const string sqlActualizar = @"
UPDATE dbo.Calidad_Inspecciones
SET
    ProgramaProduccionID=@ProgramaProduccionID,
    ChecklistArranqueID=@ChecklistArranqueID,
    SolicitudProduccionID=@SolicitudProduccionID,
    SolicitudProduccionDetalleID=@SolicitudProduccionDetalleID,
    ReleaseID=@ReleaseID,
    ReleaseDetalleID=@ReleaseDetalleID,
    ClienteID=@ClienteID,
    ClienteNombre=@ClienteNombre,
    ParteID=@ParteID,
    MaquinaID=@MaquinaID,
    MoldeID=@MoldeID,
    MaterialID=@MaterialID,
    OrdenTrabajo=@OrdenTrabajo,
    NumeroParte=@NumeroParte,
    Material=@Material,
    Proceso=N'LIBERACIÓN DE PREARRANQUE',
    Maquina=@Maquina,
    Molde=@Molde,
    FechaInicioProgramada=@FechaInicioProgramada,
    FechaFinProgramada=@FechaFinProgramada,
    OperadorPrincipalPersonaID=@OperadorPrincipalPersonaID,
    OperadorPrincipalNombre=@OperadorPrincipalNombre,
    OperadorAuxiliarPersonaID=@OperadorAuxiliarPersonaID,
    OperadorAuxiliarNombre=@OperadorAuxiliarNombre,
    CantidadTotal=@CantidadTotal,
    CantidadPendiente=
        CASE
            WHEN ISNULL(CantidadRevisada,0)>=@CantidadTotal THEN 0
            ELSE @CantidadTotal-ISNULL(CantidadRevisada,0)
        END,
    ChecklistValidado=1,
    FechaNotificacionCalidad=GETDATE(),
    UsuarioNotificoID=@UsuarioID,
    MotivoDevolucion=NULL,
    Estado=
        CASE
            WHEN Estado IN
            (
                N'PENDIENTE_PREARRANQUE',
                N'DEVUELTO_PREARRANQUE',
                N'ARRANQUE_AUTORIZADO'
            )
                THEN N'PENDIENTE_PREARRANQUE'
            ELSE Estado
        END,
    Observaciones=
        CASE
            WHEN Observaciones IS NULL
              OR LTRIM(RTRIM(Observaciones))=N''
                THEN N'Producción envió el checklist de prearranque a Calidad.'
            ELSE Observaciones+CHAR(13)+CHAR(10)+
                 N'Producción actualizó y reenvió el checklist de prearranque.'
        END,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=GETDATE()
WHERE InspeccionID=@InspeccionID
  AND EjecucionProduccionID=@EjecucionProduccionID
  AND ISNULL(Estado,N'')<>N'CERRADA';";
                await using (var cmd = new SqlCommand(sqlActualizar, cn, tx))
                {
                    AgregarParametrosOrigenCalidad(cmd, origen, usuarioId);
                    cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value = inspeccionId.Value;
                    var filasActualizadas = await cmd.ExecuteNonQueryAsync();
                    if (filasActualizadas <= 0)
                        throw new InvalidOperationException(
                            "No fue posible actualizar la inspección existente de Calidad.");
                }
                var estadoNuevo =
                    estadoExistente is
                        "PENDIENTE_PREARRANQUE" or
                        "DEVUELTO_PREARRANQUE" or
                        "ARRANQUE_AUTORIZADO"
                        ? "PENDIENTE_PREARRANQUE"
                        : estadoExistente ?? "PENDIENTE_PREARRANQUE";
                await RegistrarHistorialEnvioCalidadAsync(
                    inspeccionId.Value,
                    estadoExistente,
                    estadoNuevo,
                    checklistExistenteId == checklistArranqueId
                        ? "Producción reenvió el checklist de prearranque a Calidad."
                        : "Producción actualizó el checklist asociado y envió nuevamente la solicitud a Calidad.",
                    usuarioId,
                    cn,
                    tx);
                return;
            }
            const string sqlInsertar = @"
INSERT INTO dbo.Calidad_Inspecciones
(
    ProgramaProduccionID,
    EjecucionProduccionID,
    ChecklistArranqueID,
    SolicitudProduccionID,
    SolicitudProduccionDetalleID,
    ReleaseID,
    ReleaseDetalleID,
    ClienteID,
    ClienteNombre,
    ParteID,
    MaquinaID,
    MoldeID,
    MaterialID,
    OrdenTrabajo,
    NumeroParte,
    Material,
    Proceso,
    Maquina,
    Molde,
    FechaInicioProgramada,
    FechaFinProgramada,
    OperadorPrincipalPersonaID,
    OperadorPrincipalNombre,
    OperadorAuxiliarPersonaID,
    OperadorAuxiliarNombre,
    CantidadTotal,
    CantidadRevisada,
    CantidadPendiente,
    ChecklistValidado,
    HojaInspeccionProducto,
    HojaValidacionCalidad,
    AyudaVisualColocada,
    AlertaCalidadAplica,
    AlertaCalidadColocada,
    HIPColocada,
    HCCColocada,
    MatrizPolivalenciaValidada,
    FechaNotificacionCalidad,
    UsuarioNotificoID,
    CincoDisparosSegregados,
    CantidadDisparosConformes,
    ResultadoCalidad,
    Etiqueta,
    Liberado,
    RequiereGP12,
    EnContencion,
    EsScrap,
    RequiereReliberacion,
    ConfiguracionInvalidada,
    Observaciones,
    Estado,
    UsuarioCreacionID,
    FechaCreacion
)
OUTPUT INSERTED.InspeccionID
VALUES
(
    @ProgramaProduccionID,
    @EjecucionProduccionID,
    @ChecklistArranqueID,
    @SolicitudProduccionID,
    @SolicitudProduccionDetalleID,
    @ReleaseID,
    @ReleaseDetalleID,
    @ClienteID,
    @ClienteNombre,
    @ParteID,
    @MaquinaID,
    @MoldeID,
    @MaterialID,
    @OrdenTrabajo,
    @NumeroParte,
    @Material,
    N'LIBERACIÓN DE PREARRANQUE',
    @Maquina,
    @Molde,
    @FechaInicioProgramada,
    @FechaFinProgramada,
    @OperadorPrincipalPersonaID,
    @OperadorPrincipalNombre,
    @OperadorAuxiliarPersonaID,
    @OperadorAuxiliarNombre,
    @CantidadTotal,
    0,
    @CantidadTotal,
    1,
    0,
    0,
    0,
    NULL,
    NULL,
    0,
    0,
    0,
    GETDATE(),
    @UsuarioID,
    0,
    0,
    NULL,
    NULL,
    0,
    0,
    0,
    0,
    0,
    0,
    N'Producción envió el checklist de prearranque a Calidad.',
    N'PENDIENTE_PREARRANQUE',
    @UsuarioID,
    GETDATE()
);";
            int nuevaInspeccionId;
            await using (var cmd = new SqlCommand(sqlInsertar, cn, tx))
            {
                AgregarParametrosOrigenCalidad(cmd, origen, usuarioId);
                var resultado = await cmd.ExecuteScalarAsync();
                if (resultado == null || resultado == DBNull.Value)
                    throw new InvalidOperationException(
                        "No fue posible crear la inspección de Calidad.");
                nuevaInspeccionId = Convert.ToInt32(resultado);
            }
            await RegistrarHistorialEnvioCalidadAsync(
                nuevaInspeccionId,
                null,
                "PENDIENTE_PREARRANQUE",
                "Producción envió la corrida a Calidad para revisión de prearranque.",
                usuarioId,
                cn,
                tx);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Iniciar(int programaProduccionId, int? operadorId = null, string? operadorNombre = null, int? operadorAuxiliarId = null, string? operadorAuxiliarNombre = null, string? observaciones = null, List<long>? etiquetasBlancasSeleccionadas = null)
        {
            if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");

            if (programaProduccionId <= 0)
            {
                TempData["Error"] = "No se recibió correctamente el programa de producción.";
                return RedirectToAction(nameof(Index));
            }

            operadorNombre = string.IsNullOrWhiteSpace(operadorNombre) ? null : operadorNombre.Trim();
            operadorAuxiliarNombre = string.IsNullOrWhiteSpace(operadorAuxiliarNombre) ? null : operadorAuxiliarNombre.Trim();
            observaciones = string.IsNullOrWhiteSpace(observaciones) ? null : observaciones.Trim();

            if (operadorId.HasValue && operadorId.Value <= 0) operadorId = null;
            if (operadorAuxiliarId.HasValue && operadorAuxiliarId.Value <= 0) operadorAuxiliarId = null;
            if (operadorId.HasValue) operadorNombre = null;
            if (operadorAuxiliarId.HasValue) operadorAuxiliarNombre = null;

            if (operadorNombre?.Length > 200)
            {
                TempData["Error"] = "El nombre manual del operador principal no puede superar 200 caracteres.";
                return RedirectToAction(nameof(Index));
            }

            if (operadorAuxiliarNombre?.Length > 200)
            {
                TempData["Error"] = "El nombre manual del operador auxiliar no puede superar 200 caracteres.";
                return RedirectToAction(nameof(Index));
            }

            if (observaciones?.Length > 500)
            {
                TempData["Error"] = "Las observaciones no pueden superar 500 caracteres.";
                return RedirectToAction(nameof(Index));
            }

            var usuarioId = ObtenerUsuarioID();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);

            try
            {
                var ejecucionExistenteId = await ObtenerEjecucionActivaPorProgramaAsync(programaProduccionId, cn, tx);

                if (ejecucionExistenteId.HasValue)
                {
                    await tx.CommitAsync();
                    TempData["Info"] = "Este programa ya tiene una ejecución de producción activa.";
                    return RedirectToAction(nameof(Detalle), new { id = ejecucionExistenteId.Value });
                }

                var programa = await ObtenerProgramaParaIniciarAsync(programaProduccionId, cn, tx);

                if (programa == null)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "No se encontró el programa de producción o ya no está disponible para iniciar.";
                    return RedirectToAction(nameof(Index));
                }

                if (!programa.MaquinaID.HasValue || programa.MaquinaID.Value <= 0)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "El programa no tiene máquina asignada. No puede iniciar preparación.";
                    return RedirectToAction(nameof(Index));
                }

                var cantidadProgramadaPlaneacion = programa.CantidadPlaneada ?? 0;

                if (cantidadProgramadaPlaneacion <= 0)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "El programa no tiene una cantidad válida para iniciar Producción.";
                    return RedirectToAction(nameof(Index));
                }

                var parejaLhRh = await ObtenerParejaLhRhProduccionAsync(programaProduccionId, cn, tx);
                ValidarParejaLhRhParaInicio(parejaLhRh);

                var estadoCambioMoldeInicio = await ObtenerEstadoCambioMoldeProgramaAsync(programaProduccionId, cn, tx);

                if (estadoCambioMoldeInicio.RequiereCambioMolde && !string.Equals(estadoCambioMoldeInicio.Estado, EstadoMoldeConfirmada, StringComparison.OrdinalIgnoreCase))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = MensajeBloqueoCambioMolde(estadoCambioMoldeInicio.Estado);
                    return RedirectToAction(nameof(Index));
                }

                var personalProgramado = await ObtenerPersonalProgramadoProduccionAsync(programaProduccionId, DateTime.Now, programa.FechaInicioProgramada, cn, tx);

                if (personalProgramado?.OperadorID.HasValue == true)
                {
                    operadorId = personalProgramado.OperadorID;
                    operadorNombre = null;
                }

                if (personalProgramado?.AuxiliarID.HasValue == true)
                {
                    operadorAuxiliarId = personalProgramado.AuxiliarID;
                    operadorAuxiliarNombre = null;
                }

                var bloqueoMaquina = await ObtenerBloqueoMaquinaParaInicioAsync(programaProduccionId, programa.MaquinaID.Value, parejaLhRh?.ProgramaParejaID, cn, tx);

                if (bloqueoMaquina != null)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = ConstruirMensajeBloqueoMaquinaInicio(bloqueoMaquina, programa.MaquinaCodigo);
                    return RedirectToAction(nameof(Index));
                }

                int? operadorPrincipalFinalId = operadorId;
                string? operadorPrincipalFinalNombre = operadorNombre;

                if (!operadorPrincipalFinalId.HasValue && string.IsNullOrWhiteSpace(operadorPrincipalFinalNombre) && programa.OperadorPrincipalPlaneadoID.HasValue)
                    operadorPrincipalFinalId = programa.OperadorPrincipalPlaneadoID;

                if (operadorPrincipalFinalId.HasValue)
                {
                    operadorPrincipalFinalNombre = await ObtenerNombreOperadorProduccionAsync(operadorPrincipalFinalId.Value, cn, tx);

                    if (string.IsNullOrWhiteSpace(operadorPrincipalFinalNombre))
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = "El operador principal seleccionado no está activo o su puesto no es OPERADOR.";
                        return RedirectToAction(nameof(Index));
                    }
                }

                if (!operadorPrincipalFinalId.HasValue && string.IsNullOrWhiteSpace(operadorPrincipalFinalNombre))
                {
                    var operadorSugerido = await ObtenerOperadorSugeridoProduccionAsync(programa.MaquinaID.Value, DateTime.Now, cn, tx);

                    if (operadorSugerido != null)
                    {
                        var nombreValidado = await ObtenerNombreOperadorProduccionAsync(operadorSugerido.OperadorID, cn, tx);

                        if (!string.IsNullOrWhiteSpace(nombreValidado))
                        {
                            operadorPrincipalFinalId = operadorSugerido.OperadorID;
                            operadorPrincipalFinalNombre = nombreValidado;
                        }
                    }
                }

                if (!operadorPrincipalFinalId.HasValue && string.IsNullOrWhiteSpace(operadorPrincipalFinalNombre))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "Debes indicar un operador principal antes de iniciar la preparación. Selecciona un operador del catálogo o captura su nombre manual.";
                    return RedirectToAction(nameof(Index));
                }

                int? operadorAuxiliarFinalId = operadorAuxiliarId;
                string? operadorAuxiliarFinalNombre = operadorAuxiliarNombre;

                if (!operadorAuxiliarFinalId.HasValue && string.IsNullOrWhiteSpace(operadorAuxiliarFinalNombre) && programa.OperadorAuxiliarID.HasValue)
                    operadorAuxiliarFinalId = programa.OperadorAuxiliarID;

                if (operadorAuxiliarFinalId.HasValue)
                {
                    operadorAuxiliarFinalNombre = await ObtenerNombrePersonaActivaProduccionAsync(operadorAuxiliarFinalId.Value, cn, tx);

                    if (string.IsNullOrWhiteSpace(operadorAuxiliarFinalNombre))
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = "El auxiliar seleccionado no esta activo o ya no existe en el catalogo de Personal.";
                        return RedirectToAction(nameof(Index));
                    }
                }

                if (operadorPrincipalFinalId.HasValue && operadorAuxiliarFinalId.HasValue && operadorPrincipalFinalId.Value == operadorAuxiliarFinalId.Value)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "El operador principal y el auxiliar de producción no pueden ser la misma persona.";
                    return RedirectToAction(nameof(Index));
                }

                if (!string.IsNullOrWhiteSpace(operadorPrincipalFinalNombre) && !string.IsNullOrWhiteSpace(operadorAuxiliarFinalNombre) && string.Equals(operadorPrincipalFinalNombre.Trim(), operadorAuxiliarFinalNombre.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "El operador principal y el auxiliar de producción no pueden ser la misma persona.";
                    return RedirectToAction(nameof(Index));
                }

                if (!operadorPrincipalFinalId.HasValue && string.IsNullOrWhiteSpace(operadorPrincipalFinalNombre))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "Indica el operador principal: selecciona uno de la lista o captura el nombre manual.";
                    return RedirectToAction(nameof(Index));
                }

                var partePolivalenciaValidacion = await ResolverPartePolivalenciaProgramaAsync(programa.ProgramaProduccionID, programa.ParteID, cn, tx);
                var tieneMatrizPolivalencia = partePolivalenciaValidacion.HasValue && await ParteTienePolivalenciaProduccionAsync(partePolivalenciaValidacion.Value, cn, tx);

                if (operadorPrincipalFinalId.HasValue)
                {
                    if (tieneMatrizPolivalencia)
                    {
                        var nivelSeleccionado = await ObtenerNivelPolivalenciaProduccionAsync(partePolivalenciaValidacion!.Value, operadorPrincipalFinalId.Value, cn, tx);

                        if (!nivelSeleccionado.HasValue || nivelSeleccionado.Value < 1 || nivelSeleccionado.Value > 4)
                        {
                            await tx.RollbackAsync();
                            TempData["Error"] = "Para esta pieza, el operador seleccionado debe estar evaluado N1-N4 en la matriz de Polivalencia. Si necesitas una excepcion, usa Nombre manual sin seleccionar operador.";
                            return RedirectToAction(nameof(Index));
                        }
                    }
                    else if (!await PersonaEsOperadorActivoProduccionAsync(operadorPrincipalFinalId.Value, cn, tx))
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = "La pieza no tiene matriz; selecciona una persona activa del catalogo de Operadores o captura su nombre manual.";
                        return RedirectToAction(nameof(Index));
                    }
                }

                if (operadorAuxiliarFinalId.HasValue && !await PersonaEsAuxiliarActivoProduccionAsync(operadorAuxiliarFinalId.Value, cn, tx))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "El auxiliar debe seleccionarse del catalogo activo de Auxiliares.";
                    return RedirectToAction(nameof(Index));
                }

                int? tecnicoProduccionFinalId = personalProgramado?.TecnicoID;
                string? tecnicoProduccionFinalNombre = personalProgramado?.TecnicoNombre;
                int? smedFinalId = personalProgramado?.SmedID;
                string? smedFinalNombre = personalProgramado?.SmedNombre;

                var apoyoYaProgramado = tecnicoProduccionFinalId.HasValue || smedFinalId.HasValue;

                if (!apoyoYaProgramado)
                {
                    var responsableApoyo = Request.Form["responsableApoyo"].ToString().Trim();

                    if (!string.IsNullOrWhiteSpace(responsableApoyo))
                    {
                        var partesApoyo = responsableApoyo.Split(':', 2, StringSplitOptions.TrimEntries);

                        if (partesApoyo.Length != 2 || !int.TryParse(partesApoyo[1], out var apoyoId) || apoyoId <= 0)
                        {
                            await tx.RollbackAsync();
                            TempData["Error"] = "El responsable Técnico/SMED seleccionado no es válido.";
                            return RedirectToAction(nameof(Index));
                        }

                        var tipoApoyo = partesApoyo[0].Trim().ToUpperInvariant();
                        var validacionApoyo = await ValidarResponsableApoyoInicioV8Async(apoyoId, tipoApoyo, cn, tx);

                        if (!validacionApoyo.Valido || string.IsNullOrWhiteSpace(validacionApoyo.Nombre))
                        {
                            await tx.RollbackAsync();
                            TempData["Error"] = "El responsable seleccionado ya no pertenece al catálogo activo de Técnico/SMED.";
                            return RedirectToAction(nameof(Index));
                        }

                        if (tipoApoyo == "SMED")
                        {
                            smedFinalId = apoyoId;
                            smedFinalNombre = validacionApoyo.Nombre;
                        }
                        else
                        {
                            tecnicoProduccionFinalId = apoyoId;
                            tecnicoProduccionFinalNombre = validacionApoyo.Nombre;
                        }
                    }
                }

                if (tecnicoProduccionFinalId.HasValue)
                {
                    var validoTec = await ValidarResponsableApoyoInicioV8Async(tecnicoProduccionFinalId.Value, "TECNICO", cn, tx);

                    if (!validoTec.Valido)
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = "El Técnico programado ya no está activo o ya no corresponde al rol.";
                        return RedirectToAction(nameof(Index));
                    }

                    tecnicoProduccionFinalNombre = validoTec.Nombre;
                }

                if (smedFinalId.HasValue)
                {
                    var validoSmed = await ValidarResponsableApoyoInicioV8Async(smedFinalId.Value, "SMED", cn, tx);

                    if (!validoSmed.Valido)
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = "El SMED programado ya no está activo o ya no corresponde al rol.";
                        return RedirectToAction(nameof(Index));
                    }

                    smedFinalNombre = validoSmed.Nombre;
                }

                if (!tecnicoProduccionFinalId.HasValue && !smedFinalId.HasValue)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "Para iniciar la preparación debe existir al menos un Técnico en Producción o un SMED.";
                    return RedirectToAction(nameof(Index));
                }

                bool CoincideConOperadorOAuxiliar(int personaId)
                {
                    return (operadorPrincipalFinalId.HasValue && operadorPrincipalFinalId.Value == personaId) || (operadorAuxiliarFinalId.HasValue && operadorAuxiliarFinalId.Value == personaId);
                }

                if (tecnicoProduccionFinalId.HasValue && CoincideConOperadorOAuxiliar(tecnicoProduccionFinalId.Value))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "El Técnico en Producción debe ser distinto del operador principal y del auxiliar.";
                    return RedirectToAction(nameof(Index));
                }

                if (smedFinalId.HasValue && CoincideConOperadorOAuxiliar(smedFinalId.Value))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "El SMED debe ser distinto del operador principal y del auxiliar.";
                    return RedirectToAction(nameof(Index));
                }

                await SincronizarCoberturaFaltanteInicioV8Async(programaProduccionId, DateTime.Now, programa.FechaInicioProgramada, tecnicoProduccionFinalId, smedFinalId, operadorAuxiliarFinalId, usuarioId, cn, tx);

                var etiquetasBlancasValidadas = await ValidarEtiquetasBlancasInicioAsync(etiquetasBlancasSeleccionadas, programa, cn, tx);
                var cantidadEtiquetaBlanca = etiquetasBlancasValidadas.Sum(x => x.CantidadPiezas);
                var cantidadPlaneadaEjecucion = cantidadProgramadaPlaneacion - cantidadEtiquetaBlanca;

                if (cantidadPlaneadaEjecucion < 0)
                    throw new InvalidOperationException($"Las etiquetas blancas seleccionadas contienen {cantidadEtiquetaBlanca:N0} piezas, pero Planeación programó {cantidadProgramadaPlaneacion:N0} piezas.");

                var observacionesFinales = observaciones;
                var textoOperadores = "Operadores al iniciar preparación. Principal: " + operadorPrincipalFinalNombre!.Trim() + ".";
                textoOperadores += !string.IsNullOrWhiteSpace(operadorAuxiliarFinalNombre) ? " Auxiliar: " + operadorAuxiliarFinalNombre.Trim() + "." : " Auxiliar: sin asignar.";
                textoOperadores += !string.IsNullOrWhiteSpace(tecnicoProduccionFinalNombre) ? " Técnico en Producción: " + tecnicoProduccionFinalNombre.Trim() + "." : " Técnico en Producción: sin asignar.";
                textoOperadores += !string.IsNullOrWhiteSpace(smedFinalNombre) ? " SMED: " + smedFinalNombre.Trim() + "." : " SMED: sin asignar.";

                if (parejaLhRh != null)
                    textoOperadores += $" Producción conjunta LH/RH grupo {parejaLhRh.GrupoLhRh}; contraparte Programa {parejaLhRh.ProgramaParejaID}, {parejaLhRh.OFParejaTexto}.";

                observacionesFinales = string.IsNullOrWhiteSpace(observacionesFinales) ? textoOperadores : observacionesFinales + Environment.NewLine + textoOperadores;

                if (observacionesFinales.Length > 500)
                    observacionesFinales = observacionesFinales[..500];

                var ejecucionId = await InsertarEjecucionAsync(programa, cantidadPlaneadaEjecucion, operadorPrincipalFinalId, operadorPrincipalFinalNombre, operadorAuxiliarFinalId, operadorAuxiliarFinalNombre, tecnicoProduccionFinalId, tecnicoProduccionFinalNombre, observacionesFinales, usuarioId, cn, tx);

                await ReservarEtiquetasBlancasInicioAsync(etiquetasBlancasValidadas, programa, ejecucionId, usuarioId, cn, tx);
                await SincronizarOperadorProgramaAsync(programaProduccionId, operadorPrincipalFinalId, "PRINCIPAL", usuarioId, cn, tx);
                await SincronizarOperadorProgramaAsync(programaProduccionId, operadorAuxiliarFinalId, "AUXILIAR", usuarioId, cn, tx);
                await MarcarProgramaEnPreparacionAsync(programaProduccionId, usuarioId, cn, tx);
                await tx.CommitAsync();

                var mensajeInicio = string.IsNullOrWhiteSpace(operadorAuxiliarFinalNombre)
                    ? "Preparación iniciada correctamente con operador principal confirmado. Continúa con el checklist de arranque."
                    : "Preparación iniciada correctamente con operador principal, auxiliar de producción y responsables confirmados. Continúa con el checklist de arranque.";

                if (cantidadEtiquetaBlanca > 0)
                    mensajeInicio += $" Se aplicaron {cantidadEtiquetaBlanca:N0} pieza(s) de etiqueta blanca. Planeación conserva {cantidadProgramadaPlaneacion:N0} pieza(s) programadas y Producción ejecutará {cantidadPlaneadaEjecucion:N0} pieza(s).";

                if (parejaLhRh != null)
                    mensajeInicio += $" Producción detectó la pareja LH/RH con {parejaLhRh.OFParejaTexto}. Ambas OF deben completar su preparación y liberación de Calidad antes de iniciar serie conjunta.";

                TempData["Success"] = mensajeInicio;
                return RedirectToAction(nameof(Detalle), new { id = ejecucionId });
            }
            catch (Exception ex)
            {
                try
                {
                    await tx.RollbackAsync();
                }
                catch
                {
                }

                TempData["Error"] = "No fue posible iniciar la preparación: " + ex.Message;
                return RedirectToAction(nameof(Index));
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> IniciarSerie(int ejecucionProduccionId, long? contadorMaquinaActual = null)
        {
            if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");

            if (ejecucionProduccionId <= 0)
            {
                TempData["Error"] = "No se recibió la ejecución de producción.";
                return RedirectToAction(nameof(Index));
            }

            var usuarioId = ObtenerUsuarioID();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);

            try
            {
                var ejecucion = await ObtenerEjecucionAsync(ejecucionProduccionId, cn, tx);

                if (ejecucion == null)
                {
                    await tx.RollbackAsync();
                    return NotFound();
                }

                var parejaLhRh = await ObtenerParejaLhRhProduccionAsync(
                    ejecucion.ProgramaProduccionID,
                    cn,
                    tx);

                ProduccionEjecucionVm? ejecucionPareja = null;

                if (parejaLhRh != null)
                {
                    if (!parejaLhRh.EsCompatibleFisicamente)
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = $"La pareja LH/RH grupo {parejaLhRh.GrupoLhRh} ya no conserva la misma máquina, molde y ventana programada. Corrige Planeación antes de iniciar serie.";
                        return RedirectToAction(nameof(Detalle), new { id = ejecucionProduccionId });
                    }

                    if (!parejaLhRh.EjecucionParejaID.HasValue || parejaLhRh.EjecucionParejaID.Value <= 0)
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = $"Esta OF pertenece a una producción conjunta LH/RH. Primero debes iniciar la preparación de {parejaLhRh.OFParejaTexto}. Las dos ejecuciones deben existir antes de iniciar serie.";
                        return RedirectToAction(nameof(Detalle), new { id = ejecucionProduccionId });
                    }

                    ejecucionPareja = await ObtenerEjecucionAsync(
                        parejaLhRh.EjecucionParejaID.Value,
                        cn,
                        tx);

                    if (ejecucionPareja == null)
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = $"No fue posible encontrar la ejecución activa de {parejaLhRh.OFParejaTexto}.";
                        return RedirectToAction(nameof(Detalle), new { id = ejecucionProduccionId });
                    }

                    if (ejecucion.MaquinaID != ejecucionPareja.MaquinaID)
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = "Las ejecuciones LH/RH ya no pertenecen a la misma máquina.";
                        return RedirectToAction(nameof(Detalle), new { id = ejecucionProduccionId });
                    }

                    if (ejecucion.MoldeID.HasValue &&
                       ejecucionPareja.MoldeID.HasValue &&
                       ejecucion.MoldeID.Value != ejecucionPareja.MoldeID.Value)
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = "Las ejecuciones LH/RH ya no tienen el mismo molde.";
                        return RedirectToAction(nameof(Detalle), new { id = ejecucionProduccionId });
                    }
                }

                if (ejecucion.EstatusID == ProduccionEstatus.EnProduccion)
                {
                    if (ejecucionPareja == null)
                    {
                        await tx.CommitAsync();
                        TempData["Info"] = "La producción ya se encuentra en serie.";
                        return RedirectToAction(nameof(Detalle), new { id = ejecucionProduccionId });
                    }

                    if (ejecucionPareja.EstatusID == ProduccionEstatus.EnProduccion)
                    {
                        await tx.CommitAsync();
                        TempData["Info"] = $"La producción conjunta LH/RH grupo {parejaLhRh!.GrupoLhRh} ya se encuentra en serie.";
                        return RedirectToAction(nameof(Detalle), new { id = ejecucionProduccionId });
                    }

                    await tx.RollbackAsync();
                    TempData["Error"] = $"Se detectó una inconsistencia LH/RH: el Programa {ejecucion.ProgramaProduccionID} está produciendo pero el Programa {ejecucionPareja.ProgramaProduccionID} no. No se permitirá incorporar una OF después del arranque.";
                    return RedirectToAction(nameof(Detalle), new { id = ejecucionProduccionId });
                }

                if (ejecucion.EstatusID != ProduccionEstatus.EnPreparacion)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "Solo puedes iniciar o reiniciar serie cuando la ejecución está en preparación.";
                    return RedirectToAction(nameof(Detalle), new { id = ejecucionProduccionId });
                }

                if (ejecucionPareja != null && ejecucionPareja.EstatusID != ProduccionEstatus.EnPreparacion)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = $"{parejaLhRh!.OFParejaTexto} todavía no está lista para el arranque conjunto. Su estado actual es {ProduccionEstatus.Nombre(ejecucionPareja.EstatusID)}. Ambas OF deben estar EN PREPARACIÓN.";
                    return RedirectToAction(nameof(Detalle), new { id = ejecucionProduccionId });
                }

                if (ejecucionPareja != null && !CoincidenOperadoresLhRh(ejecucion, ejecucionPareja))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = $"Las OF LH/RH tienen operadores principales diferentes. Programa {ejecucion.ProgramaProduccionID}: {ejecucion.OperadorNombre ?? "sin operador"}. Programa {ejecucionPareja.ProgramaProduccionID}: {ejecucionPareja.OperadorNombre ?? "sin operador"}.";
                    return RedirectToAction(nameof(Detalle), new { id = ejecucionProduccionId });
                }

                if (await TieneParoAbiertoAsync(ejecucionProduccionId, cn, tx))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "No puedes iniciar o reiniciar serie mientras exista un paro abierto.";
                    return RedirectToAction(nameof(Detalle), new { id = ejecucionProduccionId });
                }

                if (ejecucionPareja != null &&
                   await TieneParoAbiertoAsync(ejecucionPareja.EjecucionProduccionID, cn, tx))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = $"{parejaLhRh!.OFParejaTexto} todavía tiene un paro abierto. No se puede arrancar una producción LH/RH parcialmente.";
                    return RedirectToAction(nameof(Detalle), new { id = ejecucionProduccionId });
                }

                var configuracionCorrida = await ObtenerConfiguracionActualAsync(
                    ejecucionProduccionId,
                    cn,
                    tx);

                if (configuracionCorrida == null)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "Antes de iniciar Producción en serie, el Técnico de Producción debe confirmar las cavidades que realmente se utilizarán, el tiempo de ciclo real y el contador actual de la máquina.";
                    return RedirectToAction(nameof(Detalle), new { id = ejecucionProduccionId });
                }

                if (configuracionCorrida.CavidadesUsadas <= 0)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La configuración real de Producción no tiene un número válido de cavidades.";
                    return RedirectToAction(nameof(Detalle), new { id = ejecucionProduccionId });
                }

                if (configuracionCorrida.TiempoCicloSegundos <= 0)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La configuración real de Producción no tiene un tiempo de ciclo válido.";
                    return RedirectToAction(nameof(Detalle), new { id = ejecucionProduccionId });
                }

                if (!configuracionCorrida.ContadorInicioVigencia.HasValue)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La configuración real de Producción no tiene contador base. El Técnico de Producción debe confirmar el contador actual de la máquina.";
                    return RedirectToAction(nameof(Detalle), new { id = ejecucionProduccionId });
                }

                ProduccionConfiguracionCorridaVm? configuracionPareja = null;

                if (ejecucionPareja != null)
                {
                    configuracionPareja = await ObtenerConfiguracionActualAsync(
                        ejecucionPareja.EjecucionProduccionID,
                        cn,
                        tx);

                    if (configuracionPareja == null)
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = $"{parejaLhRh!.OFParejaTexto} todavía no tiene configuración real de Producción.";
                        return RedirectToAction(nameof(Detalle), new { id = ejecucionProduccionId });
                    }

                    if (configuracionPareja.CavidadesUsadas <= 0)
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = $"La configuración de {parejaLhRh!.OFParejaTexto} no tiene un número válido de cavidades.";
                        return RedirectToAction(nameof(Detalle), new { id = ejecucionProduccionId });
                    }

                    if (configuracionPareja.TiempoCicloSegundos <= 0)
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = $"La configuración de {parejaLhRh!.OFParejaTexto} no tiene un tiempo de ciclo válido.";
                        return RedirectToAction(nameof(Detalle), new { id = ejecucionProduccionId });
                    }

                    if (!configuracionPareja.ContadorInicioVigencia.HasValue)
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = $"La configuración de {parejaLhRh!.OFParejaTexto} no tiene contador base.";
                        return RedirectToAction(nameof(Detalle), new { id = ejecucionProduccionId });
                    }

                    if (Math.Abs(configuracionCorrida.TiempoCicloSegundos - configuracionPareja.TiempoCicloSegundos) > 0.0001m)
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = $"Las OF LH/RH tienen tiempos de ciclo diferentes ({configuracionCorrida.TiempoCicloSegundos:0.####} s y {configuracionPareja.TiempoCicloSegundos:0.####} s). Ambas pertenecen al mismo ciclo físico de máquina.";
                        return RedirectToAction(nameof(Detalle), new { id = ejecucionProduccionId });
                    }

                    if (configuracionCorrida.ContadorInicioVigencia.Value != configuracionPareja.ContadorInicioVigencia.Value)
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = $"Las OF LH/RH tienen contadores base diferentes ({configuracionCorrida.ContadorInicioVigencia.Value:N0} y {configuracionPareja.ContadorInicioVigencia.Value:N0}). Ambas deben partir del mismo contador físico.";
                        return RedirectToAction(nameof(Detalle), new { id = ejecucionProduccionId });
                    }
                }

                var contextoReinicio = await ObtenerContextoReinicioSerieAsync(
                    ejecucionProduccionId,
                    cn,
                    tx);

                ContextoReinicioSerie? contextoReinicioPareja = null;

                if (ejecucionPareja != null)
                {
                    contextoReinicioPareja = await ObtenerContextoReinicioSerieAsync(
                        ejecucionPareja.EjecucionProduccionID,
                        cn,
                        tx);
                }

                var algunoTieneReinicio =
                    contextoReinicio != null ||
                    contextoReinicioPareja != null;

                if (algunoTieneReinicio)
                {
                    if (!contadorMaquinaActual.HasValue)
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = "Para reiniciar serie debes capturar el contador que muestra físicamente la máquina en este momento. Ese valor será la nueva base y evitará sumar los ciclos de la interrupción.";
                        return RedirectToAction(nameof(Detalle), new { id = ejecucionProduccionId });
                    }

                    if (contadorMaquinaActual.Value < 0)
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = "El contador actual de la máquina no puede ser negativo.";
                        return RedirectToAction(nameof(Detalle), new { id = ejecucionProduccionId });
                    }
                }

                var validacionCalidad = await ValidarInicioSerieCalidadAsync(
                    ejecucionProduccionId,
                    cn,
                    tx);

                if (!validacionCalidad.Permitido)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = validacionCalidad.Mensaje;
                    return RedirectToAction(nameof(Detalle), new { id = ejecucionProduccionId });
                }

                ValidacionInicioSerieResultado? validacionCalidadPareja = null;

                if (ejecucionPareja != null)
                {
                    validacionCalidadPareja = await ValidarInicioSerieCalidadAsync(
                        ejecucionPareja.EjecucionProduccionID,
                        cn,
                        tx);

                    if (!validacionCalidadPareja.Permitido)
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = $"{parejaLhRh!.OFParejaTexto} todavía no está liberada para iniciar serie. {validacionCalidadPareja.Mensaje}";
                        return RedirectToAction(nameof(Detalle), new { id = ejecucionProduccionId });
                    }
                }

                var ahora = NormalizarFechaMinuto(DateTime.Now);
                var programasRecorridos = 0;
                var esReinicio = contextoReinicio != null;
                var esReinicioLhRh = false;
                ContextoReinicioLhRhInterno? contextoFisicoLhRh = null;

                if (ejecucionPareja == null)
                {
                    if (contextoReinicio != null)
                    {
                        programasRecorridos = await _planeacionSecuenciaService.ReacomodarPorInterrupcionAsync(
                            ejecucion.ProgramaProduccionID,
                            ejecucionProduccionId,
                            contextoReinicio.FechaInicioParo,
                            ahora,
                            usuarioId,
                            cn,
                            tx,
                            trabajarDomingo: false);

                        await RebasarContadorReinicioSerieAsync(
                            ejecucionProduccionId,
                            ejecucion.MaquinaID,
                            contextoReinicio.ParoID,
                            contadorMaquinaActual!.Value,
                            ahora,
                            usuarioId,
                            cn,
                            tx);
                    }
                }
                else
                {
                    var ambosTienenReinicio =
                        contextoReinicio != null &&
                        contextoReinicioPareja != null;

                    if (algunoTieneReinicio && !ambosTienenReinicio)
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = "La producción LH/RH tiene un reinicio inconsistente: solamente una de las dos OF está pendiente de reinicio.";
                        return RedirectToAction(nameof(Detalle), new { id = ejecucionProduccionId });
                    }

                    if (ambosTienenReinicio)
                    {
                        contextoFisicoLhRh = await ObtenerContextoReinicioLhRhAsync(
                            contextoReinicio!.ParoID,
                            contextoReinicioPareja!.ParoID,
                            ejecucion.EjecucionProduccionID,
                            ejecucionPareja.EjecucionProduccionID,
                            cn,
                            tx);

                        if (contextoFisicoLhRh == null)
                        {
                            await tx.RollbackAsync();
                            TempData["Error"] = "Las dos OF no pertenecen al mismo paro físico LH/RH. No se realizará un reinicio parcial.";
                            return RedirectToAction(nameof(Detalle), new { id = ejecucionProduccionId });
                        }

                        esReinicio = true;
                        esReinicioLhRh = true;

                        var ejecucionRaiz =
                            ejecucion.ProgramaProduccionID <= ejecucionPareja.ProgramaProduccionID
                                ? ejecucion
                                : ejecucionPareja;

                        var ejecucionSecundaria =
                            ejecucionRaiz.EjecucionProduccionID == ejecucion.EjecucionProduccionID
                                ? ejecucionPareja
                                : ejecucion;

                        programasRecorridos = await _planeacionSecuenciaService.ReacomodarPorInterrupcionAsync(
                            ejecucionRaiz.ProgramaProduccionID,
                            ejecucionRaiz.EjecucionProduccionID,
                            contextoFisicoLhRh.FechaInicioFisica,
                            ahora,
                            usuarioId,
                            cn,
                            tx,
                            trabajarDomingo: false);

                        await SincronizarFinParejaLhRhDesdeProgramaAsync(
                            ejecucionRaiz.ProgramaProduccionID,
                            ejecucionSecundaria.ProgramaProduccionID,
                            usuarioId,
                            cn,
                            tx);

                        await RebasarContadorReinicioSerieAsync(
                            ejecucion.EjecucionProduccionID,
                            ejecucion.MaquinaID,
                            contextoReinicio.ParoID,
                            contadorMaquinaActual!.Value,
                            ahora,
                            usuarioId,
                            cn,
                            tx);

                        await RebasarContadorReinicioSerieAsync(
                            ejecucionPareja.EjecucionProduccionID,
                            ejecucionPareja.MaquinaID,
                            contextoReinicioPareja.ParoID,
                            contadorMaquinaActual.Value,
                            ahora,
                            usuarioId,
                            cn,
                            tx);
                    }
                }

                await CambiarEstatusEjecucionAsync(
                    ejecucionProduccionId,
                    ProduccionEstatus.EnProduccion,
                    usuarioId,
                    cn,
                    tx);

                await MarcarProgramaEnProduccionAsync(
                    ejecucion.ProgramaProduccionID,
                    ahora,
                    usuarioId,
                    cn,
                    tx);

                await MarcarCalidadEnMonitoreoAsync(
                    ejecucionProduccionId,
                    usuarioId,
                    cn,
                    tx);

                await RegistrarInicioSerieHistorialProduccionAsync(
                    ejecucionProduccionId,
                    validacionCalidad.InspeccionID,
                    usuarioId,
                    cn,
                    tx);

                if (ejecucionPareja != null)
                {
                    await CambiarEstatusEjecucionAsync(
                        ejecucionPareja.EjecucionProduccionID,
                        ProduccionEstatus.EnProduccion,
                        usuarioId,
                        cn,
                        tx);

                    await MarcarProgramaEnProduccionAsync(
                        ejecucionPareja.ProgramaProduccionID,
                        ahora,
                        usuarioId,
                        cn,
                        tx);

                    await MarcarCalidadEnMonitoreoAsync(
                        ejecucionPareja.EjecucionProduccionID,
                        usuarioId,
                        cn,
                        tx);

                    await RegistrarInicioSerieHistorialProduccionAsync(
                        ejecucionPareja.EjecucionProduccionID,
                        validacionCalidadPareja!.InspeccionID,
                        usuarioId,
                        cn,
                        tx);
                }

                await tx.CommitAsync();

                if (esReinicioLhRh)
                {
                    TempData["Success"] =
                        $"Producción conjunta LH/RH reiniciada correctamente. Los Programas {ejecucion.ProgramaProduccionID} y {ejecucionPareja!.ProgramaProduccionID} regresaron juntos a producción a las {ahora:HH:mm}. " +
                        $"El contador físico quedó rebasado en {contadorMaquinaActual!.Value:N0} para ambas OF. " +
                        $"El impacto se calculó una sola vez desde {contextoFisicoLhRh!.FechaInicioFisica:dd/MM/yyyy HH:mm} hasta el reinicio real. " +
                        $"Se recorrieron {programasRecorridos} programa(s) posteriores.";
                }
                else if (ejecucionPareja != null)
                {
                    TempData["Success"] =
                        $"Producción conjunta LH/RH iniciada correctamente. Los Programas {ejecucion.ProgramaProduccionID} y {ejecucionPareja.ProgramaProduccionID} pasaron juntos a EN PRODUCCIÓN a las {ahora:HH:mm}. " +
                        $"Grupo LH/RH: {parejaLhRh!.GrupoLhRh}. Ciclo físico compartido: {configuracionCorrida.TiempoCicloSegundos:0.####} s. Contador inicial compartido: {configuracionCorrida.ContadorInicioVigencia.Value:N0}.";
                }
                else if (esReinicio)
                {
                    TempData["Success"] =
                        $"Producción reiniciada correctamente a las {ahora:HH:mm}. " +
                        $"El contador físico quedó rebasado en {contadorMaquinaActual!.Value:N0}; los ciclos realizados durante la interrupción no se contabilizarán como producción de esta OF. " +
                        $"Se recorrieron {programasRecorridos} programa(s) posteriores.";
                }
                else
                {
                    TempData["Success"] =
                        "Producción en serie iniciada correctamente. " +
                        $"Configuración confirmada: {configuracionCorrida.CavidadesUsadas:N0} cavidad(es), " +
                        $"{configuracionCorrida.TiempoCicloSegundos:0.####} s de ciclo, " +
                        $"objetivo aproximado {configuracionCorrida.ObjetivoHoraOperativo:N0} pzas/h. El operador ya puede registrar el contador de máquina por hora.";
                }

                return RedirectToAction(nameof(Detalle), new { id = ejecucionProduccionId });
            }
            catch (Exception ex)
            {
                try
                {
                    await tx.RollbackAsync();
                }
                catch
                {
                }

                TempData["Error"] = "No fue posible iniciar o reiniciar producción en serie: " + ex.Message;
                return RedirectToAction(nameof(Detalle), new { id = ejecucionProduccionId });
            }
        }
        private static async Task RegistrarInicioSerieHistorialProduccionAsync(
    int ejecucionProduccionId,
    int? inspeccionId,
    int usuarioId,
    SqlConnection cn,
    SqlTransaction tx)
        {
            if (!inspeccionId.HasValue ||
                inspeccionId.Value <= 0)
            {
                return;
            }

            const string sql = @"
INSERT INTO dbo.Calidad_InspeccionHistorial
(
    InspeccionID,
    Movimiento,
    EstadoAnterior,
    EstadoNuevo,
    ResultadoCalidad,
    Etiqueta,
    Comentario,
    UsuarioID,
    FechaMovimiento
)
SELECT
    ci.InspeccionID,
    N'CONFIRMACION_INICIO_SERIE_PRODUCCION',
    ci.Estado,
    ci.Estado,
    ci.ResultadoCalidad,
    ci.Etiqueta,
    CASE
        WHEN EXISTS
        (
            SELECT 1
            FROM dbo.Calidad_Reliberaciones r
            WHERE r.InspeccionID=ci.InspeccionID
              AND r.EjecucionProduccionID=@EjecucionProduccionID
              AND r.Activo=1
              AND UPPER(LTRIM(RTRIM(ISNULL(r.Resultado,N''))))=N'AUTORIZADA'
        )
            THEN N'Producción confirmó el reinicio de serie después de la reliberación autorizada por Calidad.'
        ELSE N'Producción confirmó el inicio de la producción en serie después de la liberación inicial de Calidad.'
    END,
    @UsuarioID,
    GETDATE()
FROM dbo.Calidad_Inspecciones ci
WHERE ci.InspeccionID=@InspeccionID
  AND ci.EjecucionProduccionID=@EjecucionProduccionID;";

            await using var cmd =
                new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add(
                "@InspeccionID",
                SqlDbType.Int).Value =
                inspeccionId.Value;

            cmd.Parameters.Add(
                "@EjecucionProduccionID",
                SqlDbType.Int).Value =
                ejecucionProduccionId;

            cmd.Parameters.Add(
                "@UsuarioID",
                SqlDbType.Int).Value =
                usuarioId;

            await cmd.ExecuteNonQueryAsync();
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegistrarHora(
            ProduccionRegistroHoraPostVm vm)
        {
            if (!UsuarioEnSesion())
                return RedirectToAction("Login", "Login");

            var usuarioId = ObtenerUsuarioID();

            if (vm.EjecucionProduccionID <= 0)
            {
                TempData["Error"] =
                    "No se recibió la ejecución de producción.";

                return RedirectToAction(nameof(Index));
            }

            if (!TimeSpan.TryParse(vm.HoraInicio, out var horaInicio) ||
                !TimeSpan.TryParse(vm.HoraFin, out var horaFin))
            {
                TempData["Error"] =
                    "El rango de hora no es válido.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = vm.EjecucionProduccionID });
            }

            if (horaFin <= horaInicio)
            {
                TempData["Error"] =
                    "La hora fin debe ser mayor que la hora inicio.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = vm.EjecucionProduccionID });
            }

            if (vm.CantidadOK < 0 ||
                vm.CantidadSospechosa < 0 ||
                vm.CantidadScrap < 0)
            {
                TempData["Error"] =
                    "Las cantidades no pueden ser negativas.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = vm.EjecucionProduccionID });
            }

            if (vm.CantidadOK == 0 &&
                vm.CantidadSospechosa == 0 &&
                vm.CantidadScrap == 0 &&
                string.IsNullOrWhiteSpace(vm.Observaciones))
            {
                TempData["Error"] =
                    "Captura al menos una cantidad u observación.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = vm.EjecucionProduccionID });
            }

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();

            try
            {
                var ejecucion =
                    await ObtenerEjecucionAsync(
                        vm.EjecucionProduccionID,
                        cn,
                        tx);

                if (ejecucion == null)
                {
                    await tx.RollbackAsync();
                    return NotFound();
                }

                if (ejecucion.EstatusID != ProduccionEstatus.EnProduccion)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "Solo puedes registrar piezas cuando la producción está en estatus En producción.";

                    return RedirectToAction(
                        nameof(Detalle),
                        new { id = vm.EjecucionProduccionID });
                }

                var registroHoraId = await InsertarRegistroHoraAsync(
    ejecucion,
    vm,
    horaInicio,
    horaFin,
    usuarioId,
    cn,
    tx);

                await VincularRegistroHoraConMonitoreoAsync(
                    ejecucion,
                    vm,
                    horaInicio,
                    horaFin,
                    registroHoraId,
                    usuarioId,
                    cn,
                    tx);

                await RecalcularTotalesEjecucionAsync(
                    vm.EjecucionProduccionID,
                    usuarioId,
                    cn,
                    tx);

                await tx.CommitAsync();

                TempData["Success"] =
                    "Registro por hora guardado correctamente.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = vm.EjecucionProduccionID });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                TempData["Error"] =
                    "No fue posible registrar la producción por hora: " + ex.Message;

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = vm.EjecucionProduccionID });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> IniciarParo(ProduccionParoPostVm vm)
        {
            if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");
            if (vm.EjecucionProduccionID <= 0) return RedirectToAction(nameof(Index));

            var usuarioId = ObtenerUsuarioID();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);

            try
            {
                var ejecucion = await ObtenerEjecucionAsync(vm.EjecucionProduccionID, cn, tx);

                if (ejecucion == null)
                {
                    await tx.RollbackAsync();
                    return NotFound();
                }

                if (await TieneParoAbiertoAsync(ejecucion.EjecucionProduccionID, cn, tx))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "Esta ejecución ya tiene un paro abierto.";
                    return RedirectToAction(nameof(Detalle), new { id = vm.EjecucionProduccionID });
                }

                var parejaLhRh = await ObtenerParejaLhRhProduccionAsync(ejecucion.ProgramaProduccionID, cn, tx);
                ProduccionEjecucionVm? ejecucionPareja = null;

                if (parejaLhRh != null)
                {
                    if (!parejaLhRh.EsCompatibleFisicamente)
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = $"La pareja LH/RH grupo {parejaLhRh.GrupoLhRh} ya no conserva la misma máquina, molde y ventana programada. Corrige Planeación antes de registrar el paro.";
                        return RedirectToAction(nameof(Detalle), new { id = vm.EjecucionProduccionID });
                    }

                    if (!parejaLhRh.EjecucionParejaID.HasValue || parejaLhRh.EjecucionParejaID.Value <= 0)
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = $"No se encontró la ejecución activa de {parejaLhRh.OFParejaTexto}. Un paro físico LH/RH no puede afectar solamente una de las dos OF.";
                        return RedirectToAction(nameof(Detalle), new { id = vm.EjecucionProduccionID });
                    }

                    ejecucionPareja = await ObtenerEjecucionAsync(parejaLhRh.EjecucionParejaID.Value, cn, tx);

                    if (ejecucionPareja == null)
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = $"No fue posible cargar la ejecución de {parejaLhRh.OFParejaTexto}.";
                        return RedirectToAction(nameof(Detalle), new { id = vm.EjecucionProduccionID });
                    }

                    if (ejecucion.EstatusID != ProduccionEstatus.EnProduccion || ejecucionPareja.EstatusID != ProduccionEstatus.EnProduccion)
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = "Un paro físico LH/RH solo puede iniciarse cuando las dos OF se encuentran EN PRODUCCIÓN.";
                        return RedirectToAction(nameof(Detalle), new { id = vm.EjecucionProduccionID });
                    }

                    if (ejecucion.MaquinaID != ejecucionPareja.MaquinaID)
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = "Las ejecuciones LH/RH ya no pertenecen a la misma máquina. No se puede registrar un paro conjunto.";
                        return RedirectToAction(nameof(Detalle), new { id = vm.EjecucionProduccionID });
                    }

                    if (ejecucion.FechaLiberacionMaquina.HasValue || ejecucionPareja.FechaLiberacionMaquina.HasValue)
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = "Una de las ejecuciones LH/RH ya tiene la máquina liberada. No se puede registrar un paro físico conjunto.";
                        return RedirectToAction(nameof(Detalle), new { id = vm.EjecucionProduccionID });
                    }

                    if (await TieneParoAbiertoAsync(ejecucionPareja.EjecucionProduccionID, cn, tx))
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = $"{parejaLhRh.OFParejaTexto} ya tiene un paro abierto. No se creará un segundo paro sobre la misma producción física.";
                        return RedirectToAction(nameof(Detalle), new { id = vm.EjecucionProduccionID });
                    }
                }

                var motivoTexto = vm.MotivoParoTexto;

                if (vm.MotivoParoID.HasValue)
                    motivoTexto = await ObtenerMotivoParoNombreAsync(vm.MotivoParoID.Value, cn, tx);

                var fechaInicioParo = DateTime.Now;
                Guid? grupoParoLhRh = ejecucionPareja != null ? Guid.NewGuid() : null;

                var paroPrincipalId = await InsertarParoProduccionInternoAsync(
                    ejecucion,
                    vm.MotivoParoID,
                    motivoTexto,
                    vm.Descripcion,
                    fechaInicioParo,
                    grupoParoLhRh,
                    ejecucionPareja != null,
                    usuarioId,
                    cn,
                    tx);

                int? paroParejaId = null;

                if (ejecucionPareja != null)
                {
                    paroParejaId = await InsertarParoProduccionInternoAsync(
                        ejecucionPareja,
                        vm.MotivoParoID,
                        motivoTexto,
                        vm.Descripcion,
                        fechaInicioParo,
                        grupoParoLhRh,
                        true,
                        usuarioId,
                        cn,
                        tx);
                }

                await CambiarEstatusEjecucionAsync(ejecucion.EjecucionProduccionID, ProduccionEstatus.Pausado, usuarioId, cn, tx);
                await CambiarEstatusProgramaAsync(ejecucion.ProgramaProduccionID, ProgramaProduccionEstatus.Pausado, usuarioId, cn, tx);

                if (ejecucionPareja != null)
                {
                    await CambiarEstatusEjecucionAsync(ejecucionPareja.EjecucionProduccionID, ProduccionEstatus.Pausado, usuarioId, cn, tx);
                    await CambiarEstatusProgramaAsync(ejecucionPareja.ProgramaProduccionID, ProgramaProduccionEstatus.Pausado, usuarioId, cn, tx);
                }

                await tx.CommitAsync();

                TempData["Success"] = ejecucionPareja == null
                    ? $"Paro {paroPrincipalId} iniciado correctamente."
                    : $"Paro físico LH/RH iniciado correctamente. Los Programas {ejecucion.ProgramaProduccionID} y {ejecucionPareja.ProgramaProduccionID} quedaron pausados en la misma operación. Paros relacionados: {paroPrincipalId} y {paroParejaId}.";

                return RedirectToAction(nameof(Detalle), new { id = vm.EjecucionProduccionID });
            }
            catch (Exception ex)
            {
                try
                {
                    await tx.RollbackAsync();
                }
                catch
                {
                }

                TempData["Error"] = "No fue posible iniciar el paro: " + ex.Message;
                return RedirectToAction(nameof(Detalle), new { id = vm.EjecucionProduccionID });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CerrarParo(ProduccionCerrarParoPostVm vm)
        {
            if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");

            if (vm.ParoID <= 0)
            {
                TempData["Error"] = "No se recibió correctamente el paro.";
                return RedirectToAction(nameof(Index));
            }

            var usuarioId = ObtenerUsuarioID();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
            var ejecucionProduccionId = 0;

            try
            {
                const string sqlLeer = @"
SELECT TOP(1)
    ParoID,
    EjecucionProduccionID,
    ProgramaProduccionID,
    FechaInicioParo,
    ISNULL(EsInterrupcionUrgente,0) AS EsInterrupcionUrgente,
    ProgramaUrgenteID,
    ISNULL(EsParoLhRh,0) AS EsParoLhRh,
    GrupoParoLhRh
FROM dbo.Produccion_Paros WITH(UPDLOCK,HOLDLOCK)
WHERE ParoID=@ParoID
  AND Activo=1
  AND FechaFinParo IS NULL;";

                int programaProduccionId;
                DateTime fechaInicioParo;
                bool esInterrupcionUrgente;
                int? programaUrgenteId;
                bool esParoLhRh;
                Guid? grupoParoLhRh;

                await using (var cmd = new SqlCommand(sqlLeer, cn, tx))
                {
                    cmd.Parameters.Add("@ParoID", SqlDbType.Int).Value = vm.ParoID;
                    await using var rd = await cmd.ExecuteReaderAsync();

                    if (!await rd.ReadAsync())
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = "No se encontró un paro abierto para cerrar.";
                        return RedirectToAction(nameof(Index));
                    }

                    ejecucionProduccionId = Convert.ToInt32(rd["EjecucionProduccionID"]);
                    programaProduccionId = Convert.ToInt32(rd["ProgramaProduccionID"]);
                    fechaInicioParo = Convert.ToDateTime(rd["FechaInicioParo"]);
                    esInterrupcionUrgente = rd["EsInterrupcionUrgente"] != DBNull.Value && Convert.ToBoolean(rd["EsInterrupcionUrgente"]);
                    programaUrgenteId = rd["ProgramaUrgenteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ProgramaUrgenteID"]);
                    esParoLhRh = rd["EsParoLhRh"] != DBNull.Value && Convert.ToBoolean(rd["EsParoLhRh"]);
                    grupoParoLhRh = rd["GrupoParoLhRh"] == DBNull.Value ? null : (Guid?)rd["GrupoParoLhRh"];
                }

                if (esInterrupcionUrgente)
                {
                    await tx.RollbackAsync();

                    string referenciaUrgente;

                    if (programaUrgenteId.HasValue)
                    {
                        await using var cmdUrgente = new SqlCommand(@"
SELECT TOP(1) SolicitudProduccionID
FROM dbo.Planeacion_ProgramaProduccion
WHERE ProgramaProduccionID=@ProgramaProduccionID
  AND Activo=1;", cn);

                        cmdUrgente.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaUrgenteId.Value;
                        var resultadoUrgente = await cmdUrgente.ExecuteScalarAsync();

                        referenciaUrgente = resultadoUrgente != null && resultadoUrgente != DBNull.Value
                            ? $"OF {Convert.ToInt32(resultadoUrgente)}"
                            : $"programa {programaUrgenteId.Value}";
                    }
                    else
                    {
                        referenciaUrgente = "la OF urgente";
                    }

                    TempData["Error"] = $"Este paro fue generado automáticamente por una interrupción urgente de Planeación y no puede cerrarse manualmente. Se cerrará automáticamente cuando termine {referenciaUrgente}.";
                    return RedirectToAction(nameof(Detalle), new { id = ejecucionProduccionId });
                }

                var ahora = DateTime.Now;
                var observacionesCierre = string.IsNullOrWhiteSpace(vm.ObservacionesCierre) ? null : vm.ObservacionesCierre.Trim();

                if (observacionesCierre?.Length > 500)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "Las observaciones de cierre no pueden superar 500 caracteres.";
                    return RedirectToAction(nameof(Detalle), new { id = ejecucionProduccionId });
                }

                if (esParoLhRh)
                {
                    if (!grupoParoLhRh.HasValue)
                        throw new InvalidOperationException("El paro está marcado como LH/RH pero no tiene identificador de grupo.");

                    var parosGrupo = await ObtenerParosAbiertosGrupoLhRhAsync(grupoParoLhRh.Value, cn, tx);

                    if (parosGrupo.Count != 2)
                        throw new InvalidOperationException($"El grupo de paro LH/RH debería contener exactamente 2 paros abiertos y actualmente contiene {parosGrupo.Count}. No se realizará un cierre parcial.");

                    if (parosGrupo.Any(x => x.EsInterrupcionUrgente))
                        throw new InvalidOperationException("El grupo LH/RH contiene una interrupción urgente y no puede cerrarse manualmente.");

                    var paroA = parosGrupo.OrderBy(x => x.ProgramaProduccionID).First();
                    var paroB = parosGrupo.OrderBy(x => x.ProgramaProduccionID).Last();

                    var relacionLhRh = await ObtenerParejaLhRhProduccionAsync(paroA.ProgramaProduccionID, cn, tx);

                    if (relacionLhRh == null || relacionLhRh.ProgramaParejaID != paroB.ProgramaProduccionID)
                        throw new InvalidOperationException("Los dos paros del grupo ya no corresponden a la misma pareja LH/RH de Planeación.");

                    var ejecucionA = await ObtenerEjecucionAsync(paroA.EjecucionProduccionID, cn, tx);
                    var ejecucionB = await ObtenerEjecucionAsync(paroB.EjecucionProduccionID, cn, tx);

                    if (ejecucionA == null || ejecucionB == null)
                        throw new InvalidOperationException("No fue posible encontrar las dos ejecuciones del paro LH/RH.");

                    if (ejecucionA.EstatusID != ProduccionEstatus.Pausado || ejecucionB.EstatusID != ProduccionEstatus.Pausado)
                        throw new InvalidOperationException("Las dos ejecuciones LH/RH deben permanecer pausadas hasta cerrar conjuntamente el paro.");

                    const string sqlCerrarGrupo = @"
UPDATE dbo.Produccion_Paros
SET FechaFinParo=@FechaFin,
    DuracionMinutos=DATEDIFF(MINUTE,FechaInicioParo,@FechaFin),
    EsMayorA15Minutos=CASE WHEN DATEDIFF(MINUTE,FechaInicioParo,@FechaFin)>15 THEN 1 ELSE 0 END,
    Descripcion=CASE
        WHEN @ObservacionesCierre IS NULL OR LTRIM(RTRIM(@ObservacionesCierre))=N'' THEN Descripcion
        WHEN Descripcion IS NULL OR LTRIM(RTRIM(Descripcion))=N'' THEN @ObservacionesCierre
        ELSE Descripcion+CHAR(13)+CHAR(10)+N'Cierre: '+@ObservacionesCierre
    END,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=GETDATE()
WHERE Activo=1
  AND FechaFinParo IS NULL
  AND ISNULL(EsParoLhRh,0)=1
  AND GrupoParoLhRh=@GrupoParoLhRh
  AND ISNULL(EsInterrupcionUrgente,0)=0;";

                    int filasCerradas;

                    await using (var cmd = new SqlCommand(sqlCerrarGrupo, cn, tx))
                    {
                        cmd.Parameters.Add("@FechaFin", SqlDbType.DateTime).Value = ahora;
                        cmd.Parameters.Add("@ObservacionesCierre", SqlDbType.NVarChar, 500).Value = (object?)observacionesCierre ?? DBNull.Value;
                        cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                        cmd.Parameters.Add("@GrupoParoLhRh", SqlDbType.UniqueIdentifier).Value = grupoParoLhRh.Value;
                        filasCerradas = await cmd.ExecuteNonQueryAsync();
                    }

                    if (filasCerradas != 2)
                        throw new InvalidOperationException("El grupo LH/RH cambió mientras se intentaba cerrar. No se aplicará un cierre parcial.");

                    var fechaInicioFisica = parosGrupo.Min(x => x.FechaInicioParo);
                    var duracionMinutos = (int)Math.Max(0, Math.Floor((ahora - fechaInicioFisica).TotalMinutes));
                    var requiereReliberacion = duracionMinutos > 15;

                    if (requiereReliberacion)
                    {
                        foreach (var paro in parosGrupo)
                        {
                            var ejecucionGrupo = paro.EjecucionProduccionID == ejecucionA.EjecucionProduccionID ? ejecucionA : ejecucionB;
                            var duracionFila = (int)Math.Max(0, Math.Floor((ahora - paro.FechaInicioParo).TotalMinutes));

                            await CambiarEstatusEjecucionAsync(ejecucionGrupo.EjecucionProduccionID, ProduccionEstatus.EnPreparacion, usuarioId, cn, tx);
                            await CambiarEstatusProgramaAsync(ejecucionGrupo.ProgramaProduccionID, ProgramaProduccionEstatus.EnPreparacion, usuarioId, cn, tx);
                            await CrearOActualizarSolicitudReliberacionCalidadPorParoAsync(ejecucionGrupo.EjecucionProduccionID, paro.ParoID, duracionFila, usuarioId, cn, tx);
                        }

                        await tx.CommitAsync();

                        TempData["Success"] = $"Paro LH/RH cerrado después de {duracionMinutos} minuto(s). Las dos OF regresaron a preparación y ambas requieren reliberación de Calidad antes de reiniciar juntas.";
                        return RedirectToAction(nameof(Detalle), new { id = ejecucionProduccionId });
                    }

                    var programasRecorridos = await _planeacionSecuenciaService.ReacomodarPorInterrupcionAsync(
                        ejecucionA.ProgramaProduccionID,
                        ejecucionA.EjecucionProduccionID,
                        fechaInicioFisica,
                        ahora,
                        usuarioId,
                        cn,
                        tx,
                        trabajarDomingo: false);

                    await SincronizarFinParejaLhRhDesdeProgramaAsync(ejecucionA.ProgramaProduccionID, ejecucionB.ProgramaProduccionID, usuarioId, cn, tx);

                    await CambiarEstatusEjecucionAsync(ejecucionA.EjecucionProduccionID, ProduccionEstatus.EnProduccion, usuarioId, cn, tx);
                    await CambiarEstatusProgramaAsync(ejecucionA.ProgramaProduccionID, ProgramaProduccionEstatus.EnProduccion, usuarioId, cn, tx);
                    await CambiarEstatusEjecucionAsync(ejecucionB.EjecucionProduccionID, ProduccionEstatus.EnProduccion, usuarioId, cn, tx);
                    await CambiarEstatusProgramaAsync(ejecucionB.ProgramaProduccionID, ProgramaProduccionEstatus.EnProduccion, usuarioId, cn, tx);

                    await tx.CommitAsync();

                    TempData["Success"] = $"Paro LH/RH cerrado correctamente después de {duracionMinutos} minuto(s). Las dos OF regresaron juntas a producción y se recorrieron {programasRecorridos} programa(s) posteriores.";
                    return RedirectToAction(nameof(Detalle), new { id = ejecucionProduccionId });
                }

                var duracionParo = (int)Math.Max(0, Math.Floor((ahora - fechaInicioParo).TotalMinutes));
                var requiereReliberacionIndividual = duracionParo > 15;

                const string sqlCerrarIndividual = @"
UPDATE dbo.Produccion_Paros
SET FechaFinParo=@FechaFin,
    DuracionMinutos=@DuracionMinutos,
    EsMayorA15Minutos=CASE WHEN @DuracionMinutos>15 THEN 1 ELSE 0 END,
    Descripcion=CASE
        WHEN @ObservacionesCierre IS NULL OR LTRIM(RTRIM(@ObservacionesCierre))=N'' THEN Descripcion
        WHEN Descripcion IS NULL OR LTRIM(RTRIM(Descripcion))=N'' THEN @ObservacionesCierre
        ELSE Descripcion+CHAR(13)+CHAR(10)+N'Cierre: '+@ObservacionesCierre
    END,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=GETDATE()
WHERE ParoID=@ParoID
  AND EjecucionProduccionID=@EjecucionProduccionID
  AND Activo=1
  AND FechaFinParo IS NULL
  AND ISNULL(EsInterrupcionUrgente,0)=0
  AND ISNULL(EsParoLhRh,0)=0;

IF @@ROWCOUNT<>1
    THROW 51400,'El paro cambió mientras se intentaba cerrar o no corresponde a un paro individual cerrable manualmente.',1;";

                await using (var cmd = new SqlCommand(sqlCerrarIndividual, cn, tx))
                {
                    cmd.Parameters.Add("@FechaFin", SqlDbType.DateTime).Value = ahora;
                    cmd.Parameters.Add("@ParoID", SqlDbType.Int).Value = vm.ParoID;
                    cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                    cmd.Parameters.Add("@DuracionMinutos", SqlDbType.Int).Value = duracionParo;
                    cmd.Parameters.Add("@ObservacionesCierre", SqlDbType.NVarChar, 500).Value = (object?)observacionesCierre ?? DBNull.Value;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    await cmd.ExecuteNonQueryAsync();
                }

                var ejecucion = await ObtenerEjecucionAsync(ejecucionProduccionId, cn, tx);

                if (ejecucion == null)
                {
                    await tx.RollbackAsync();
                    return NotFound();
                }

                if (requiereReliberacionIndividual)
                {
                    await CambiarEstatusEjecucionAsync(ejecucionProduccionId, ProduccionEstatus.EnPreparacion, usuarioId, cn, tx);
                    await CambiarEstatusProgramaAsync(ejecucion.ProgramaProduccionID, ProgramaProduccionEstatus.EnPreparacion, usuarioId, cn, tx);
                    await CrearOActualizarSolicitudReliberacionCalidadPorParoAsync(ejecucionProduccionId, vm.ParoID, duracionParo, usuarioId, cn, tx);
                    await tx.CommitAsync();

                    TempData["Success"] = "Paro cerrado. Al superar 15 minutos, Producción regresó a preparación y Calidad debe autorizar la reliberación antes de reiniciar la serie.";
                    return RedirectToAction(nameof(Detalle), new { id = ejecucionProduccionId });
                }

                var programasRecorridosIndividual = await _planeacionSecuenciaService.ReacomodarPorInterrupcionAsync(
                    ejecucion.ProgramaProduccionID,
                    ejecucionProduccionId,
                    fechaInicioParo,
                    ahora,
                    usuarioId,
                    cn,
                    tx,
                    trabajarDomingo: false);

                await CambiarEstatusEjecucionAsync(ejecucionProduccionId, ProduccionEstatus.EnProduccion, usuarioId, cn, tx);
                await CambiarEstatusProgramaAsync(ejecucion.ProgramaProduccionID, ProgramaProduccionEstatus.EnProduccion, usuarioId, cn, tx);
                await tx.CommitAsync();

                TempData["Success"] = $"Paro cerrado correctamente. La producción continúa en serie y se recorrieron {programasRecorridosIndividual} programa(s) posteriores por el tiempo real del paro.";
                return RedirectToAction(nameof(Detalle), new { id = ejecucionProduccionId });
            }
            catch (Exception ex)
            {
                try
                {
                    await tx.RollbackAsync();
                }
                catch
                {
                }

                TempData["Error"] = "No fue posible cerrar el paro: " + ex.Message;

                return ejecucionProduccionId > 0
                    ? RedirectToAction(nameof(Detalle), new { id = ejecucionProduccionId })
                    : RedirectToAction(nameof(Index));
            }
        }

        private static async Task<int?> ObtenerProgramaUrgenteRaizInterrupcionAsync(int programaUrgenteId, int? programaUrgenteParejaId, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
SELECT TOP(1) ProgramaUrgenteID
FROM dbo.Produccion_Paros WITH(UPDLOCK,HOLDLOCK)
WHERE Activo=1
  AND FechaFinParo IS NULL
  AND ISNULL(EsInterrupcionUrgente,0)=1
  AND
  (
      ProgramaUrgenteID=@ProgramaUrgenteID
      OR
      (
          @ProgramaUrgenteParejaID IS NOT NULL
          AND ProgramaUrgenteID=@ProgramaUrgenteParejaID
      )
  )
ORDER BY ParoID DESC;";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ProgramaUrgenteID", SqlDbType.Int).Value = programaUrgenteId;
            cmd.Parameters.Add("@ProgramaUrgenteParejaID", SqlDbType.Int).Value = (object?)programaUrgenteParejaId ?? DBNull.Value;
            var result = await cmd.ExecuteScalarAsync();
            return result == null || result == DBNull.Value ? null : Convert.ToInt32(result);
        }
        private static async Task<bool> TodasEjecucionesUrgentesConcluyeronFisicamenteAsync(int programaUrgenteRaizId, int? programaUrgenteParejaId, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
DECLARE @Esperadas INT=CASE WHEN @ProgramaUrgenteParejaID IS NULL THEN 1 ELSE 2 END;
DECLARE @Concluidas INT;
SELECT @Concluidas=COUNT(DISTINCT e.ProgramaProduccionID)
FROM dbo.Produccion_Ejecucion e WITH(UPDLOCK,HOLDLOCK)
WHERE e.Activo=1
  AND
  (
      e.ProgramaProduccionID=@ProgramaUrgenteRaizID
      OR
      (
          @ProgramaUrgenteParejaID IS NOT NULL
          AND e.ProgramaProduccionID=@ProgramaUrgenteParejaID
      )
  )
  AND
  (
      e.FechaLiberacionMaquina IS NOT NULL
      OR e.FechaFinReal IS NOT NULL
      OR e.EstatusID IN(5,6,9)
  );
SELECT CASE WHEN ISNULL(@Concluidas,0)=@Esperadas THEN 1 ELSE 0 END;";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ProgramaUrgenteRaizID", SqlDbType.Int).Value = programaUrgenteRaizId;
            cmd.Parameters.Add("@ProgramaUrgenteParejaID", SqlDbType.Int).Value = (object?)programaUrgenteParejaId ?? DBNull.Value;
            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1;
        }
        private static async Task<List<InterrupcionUrgenteRetornoContexto>> ObtenerInterrupcionesUrgentesRetornoAsync(int programaUrgenteRaizId, SqlConnection cn, SqlTransaction tx)
        {
            var lista = new List<InterrupcionUrgenteRetornoContexto>();
            const string sql = @"
SELECT
    p.ParoID,
    p.EjecucionProduccionID AS EjecucionInterrumpidaID,
    p.ProgramaProduccionID AS ProgramaInterrumpidoID,
    p.ProgramaUrgenteID,
    p.MaquinaID,
    p.FechaInicioParo,
    ISNULL(p.EsParoLhRh,0) AS EsParoLhRh,
    p.GrupoParoLhRh,
    e.EstatusID AS EstatusEjecucionInterrumpida,
    ppOrigen.MoldeID AS MoldeInterrumpidoID,
    ppUrgente.MoldeID AS MoldeUrgenteID
FROM dbo.Produccion_Paros p WITH(UPDLOCK,HOLDLOCK)
INNER JOIN dbo.Produccion_Ejecucion e WITH(UPDLOCK,HOLDLOCK)
    ON e.EjecucionProduccionID=p.EjecucionProduccionID
   AND e.ProgramaProduccionID=p.ProgramaProduccionID
   AND e.Activo=1
LEFT JOIN dbo.Planeacion_ProgramaProduccion ppOrigen WITH(UPDLOCK,HOLDLOCK)
    ON ppOrigen.ProgramaProduccionID=p.ProgramaProduccionID
   AND ppOrigen.Activo=1
LEFT JOIN dbo.Planeacion_ProgramaProduccion ppUrgente WITH(UPDLOCK,HOLDLOCK)
    ON ppUrgente.ProgramaProduccionID=p.ProgramaUrgenteID
   AND ppUrgente.Activo=1
WHERE p.Activo=1
  AND p.FechaFinParo IS NULL
  AND ISNULL(p.EsInterrupcionUrgente,0)=1
  AND p.ProgramaUrgenteID=@ProgramaUrgenteID
ORDER BY p.ProgramaProduccionID,p.ParoID;";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ProgramaUrgenteID", SqlDbType.Int).Value = programaUrgenteRaizId;
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                lista.Add(new InterrupcionUrgenteRetornoContexto
                {
                    ParoID = Convert.ToInt32(rd["ParoID"]),
                    EjecucionInterrumpidaID = Convert.ToInt32(rd["EjecucionInterrumpidaID"]),
                    ProgramaInterrumpidoID = Convert.ToInt32(rd["ProgramaInterrumpidoID"]),
                    ProgramaUrgenteID = Convert.ToInt32(rd["ProgramaUrgenteID"]),
                    MaquinaID = rd["MaquinaID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaquinaID"]),
                    FechaInicioParo = Convert.ToDateTime(rd["FechaInicioParo"]),
                    EsParoLhRh = rd["EsParoLhRh"] != DBNull.Value && Convert.ToBoolean(rd["EsParoLhRh"]),
                    GrupoParoLhRh = rd["GrupoParoLhRh"] == DBNull.Value ? null : (Guid?)rd["GrupoParoLhRh"],
                    EstatusEjecucionInterrumpida = Convert.ToInt32(rd["EstatusEjecucionInterrumpida"]),
                    MoldeInterrumpidoID = rd["MoldeInterrumpidoID"] == DBNull.Value ? null : Convert.ToInt32(rd["MoldeInterrumpidoID"]),
                    MoldeUrgenteID = rd["MoldeUrgenteID"] == DBNull.Value ? null : Convert.ToInt32(rd["MoldeUrgenteID"])
                });
            }
            return lista;
        }
        private async Task CrearOActualizarSolicitudReliberacionCalidadPorParoAsync(int ejecucionProduccionId, int paroId, int duracionMinutos, int usuarioId, SqlConnection cn, SqlTransaction tx, bool forzarReliberacion = false)
        {
            if (ejecucionProduccionId <= 0) throw new ArgumentException("La ejecución de producción no es válida.", nameof(ejecucionProduccionId));
            if (paroId <= 0) throw new ArgumentException("El paro de producción no es válido.", nameof(paroId));
            if (duracionMinutos <= 15 && !forzarReliberacion) throw new InvalidOperationException("Solo se debe solicitar reliberación cuando el paro sea mayor a 15 minutos o exista una condición especial que obligue a reliberar.");

            const string sqlObtenerInspeccion = @"SELECT TOP(1) ci.InspeccionID,ci.Estado,ci.ChecklistArranqueID,ISNULL(ci.ConfiguracionInvalidada,0) AS ConfiguracionInvalidada FROM dbo.Calidad_Inspecciones ci WITH(UPDLOCK,HOLDLOCK) WHERE ci.EjecucionProduccionID=@EjecucionProduccionID AND ISNULL(ci.Estado,N'')<>N'CERRADA' ORDER BY ci.InspeccionID DESC;";
            int inspeccionId;
            string estadoAnterior;
            int? checklistArranqueId;
            bool configuracionInvalidada;

            await using (var cmd = new SqlCommand(sqlObtenerInspeccion, cn, tx))
            {
                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                await using var rd = await cmd.ExecuteReaderAsync();
                if (!await rd.ReadAsync()) throw new InvalidOperationException("No existe una inspección activa de Calidad para esta ejecución. Primero debe completarse y enviarse el checklist de arranque.");
                inspeccionId = Convert.ToInt32(rd["InspeccionID"]);
                estadoAnterior = rd["Estado"] == DBNull.Value ? string.Empty : rd["Estado"].ToString() ?? string.Empty;
                checklistArranqueId = rd["ChecklistArranqueID"] == DBNull.Value ? null : Convert.ToInt32(rd["ChecklistArranqueID"]);
                configuracionInvalidada = rd["ConfiguracionInvalidada"] != DBNull.Value && Convert.ToBoolean(rd["ConfiguracionInvalidada"]);
            }

            if (!checklistArranqueId.HasValue || checklistArranqueId.Value <= 0) throw new InvalidOperationException("La inspección de Calidad no tiene un checklist de arranque relacionado.");
            if (configuracionInvalidada) throw new InvalidOperationException("La configuración de la inspección ya se encuentra invalidada. Calidad debe resolver esa condición antes de procesar la reliberación.");

            const string sqlValidarParo = @"
SELECT COUNT(1)
FROM dbo.Produccion_Paros WITH(UPDLOCK,HOLDLOCK)
WHERE ParoID=@ParoID
  AND EjecucionProduccionID=@EjecucionProduccionID
  AND Activo=1
  AND FechaFinParo IS NOT NULL
  AND
  (
      ISNULL(EsMayorA15Minutos,0)=1
      OR (@ForzarReliberacion=1 AND ISNULL(EsInterrupcionUrgente,0)=1)
  );";

            await using (var cmd = new SqlCommand(sqlValidarParo, cn, tx))
            {
                cmd.Parameters.Add("@ParoID", SqlDbType.Int).Value = paroId;
                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                cmd.Parameters.Add("@ForzarReliberacion", SqlDbType.Bit).Value = forzarReliberacion;
                if (Convert.ToInt32(await cmd.ExecuteScalarAsync()) <= 0) throw new InvalidOperationException("El paro no está cerrado o no cumple las condiciones necesarias para solicitar reliberación.");
            }

            const string sqlExiste = @"SELECT TOP(1) ReliberacionID FROM dbo.Calidad_Reliberaciones WITH(UPDLOCK,HOLDLOCK) WHERE InspeccionID=@InspeccionID AND EjecucionProduccionID=@EjecucionProduccionID AND ParoID=@ParoID AND Activo=1;";
            int? reliberacionId;

            await using (var cmd = new SqlCommand(sqlExiste, cn, tx))
            {
                cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value = inspeccionId;
                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                cmd.Parameters.Add("@ParoID", SqlDbType.Int).Value = paroId;
                var resultado = await cmd.ExecuteScalarAsync();
                reliberacionId = resultado == null || resultado == DBNull.Value ? null : Convert.ToInt32(resultado);
            }

            var motivo = forzarReliberacion
                ? $"Interrupción urgente con cambio de molde. Duración registrada: {duracionMinutos} minuto(s). Se requieren primeras piezas y nueva autorización de Calidad."
                : $"Paro mayor a 15 minutos. Duración registrada: {duracionMinutos} minuto(s).";

            var observacion = $"Solicitud automática de reliberación. ParoID: {paroId}. {motivo}";

            const string sqlActualizarInspeccion = @"UPDATE dbo.Calidad_Inspecciones SET RequiereReliberacion=1,Liberado=0,Estado=N'PENDIENTE_RELIBERACION',ResultadoCalidad=NULL,Etiqueta=NULL,CincoDisparosSegregados=0,CantidadDisparosConformes=0,ValidacionDimensional=NULL,ValidacionApariencia=NULL,ValidacionGauge=NULL,ValidacionConductividad=NULL,FechaNotificacionCalidad=GETDATE(),UsuarioNotificoID=@UsuarioID,MotivoDevolucion=@Motivo,Observaciones=CASE WHEN Observaciones IS NULL OR LTRIM(RTRIM(Observaciones))=N'' THEN @Observacion ELSE Observaciones+CHAR(13)+CHAR(10)+@Observacion END,UsuarioModificacionID=@UsuarioID,FechaModificacion=GETDATE() WHERE InspeccionID=@InspeccionID AND EjecucionProduccionID=@EjecucionProduccionID AND ISNULL(Estado,N'')<>N'CERRADA'; IF @@ROWCOUNT<>1 THROW 51401,'No fue posible marcar la inspección como pendiente de reliberación.',1;";

            await using (var cmd = new SqlCommand(sqlActualizarInspeccion, cn, tx))
            {
                cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value = inspeccionId;
                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                cmd.Parameters.Add("@Motivo", SqlDbType.NVarChar, 500).Value = motivo.Length > 500 ? motivo[..500] : motivo;
                cmd.Parameters.Add("@Observacion", SqlDbType.NVarChar, 1000).Value = observacion.Length > 1000 ? observacion[..1000] : observacion;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                await cmd.ExecuteNonQueryAsync();
            }

            if (reliberacionId.HasValue)
            {
                const string sqlReactivar = @"UPDATE dbo.Calidad_Reliberaciones SET Resultado=N'PENDIENTE',FechaSolicitud=GETDATE(),FechaValidacion=NULL,UsuarioSolicitudID=@UsuarioID,UsuarioCalidadID=NULL,Motivo=@Motivo,Observaciones=CASE WHEN Observaciones IS NULL OR LTRIM(RTRIM(Observaciones))=N'' THEN @Observacion ELSE Observaciones+CHAR(13)+CHAR(10)+@Observacion END,UsuarioModificacionID=@UsuarioID,FechaModificacion=GETDATE() WHERE ReliberacionID=@ReliberacionID AND Activo=1;";
                await using var cmd = new SqlCommand(sqlReactivar, cn, tx);
                cmd.Parameters.Add("@ReliberacionID", SqlDbType.Int).Value = reliberacionId.Value;
                cmd.Parameters.Add("@Motivo", SqlDbType.NVarChar, 500).Value = motivo.Length > 500 ? motivo[..500] : motivo;
                cmd.Parameters.Add("@Observacion", SqlDbType.NVarChar, 1000).Value = observacion.Length > 1000 ? observacion[..1000] : observacion;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                await cmd.ExecuteNonQueryAsync();
            }
            else
            {
                const string sqlInsertar = @"DECLARE @NumeroReliberacion INT; SELECT @NumeroReliberacion=ISNULL(MAX(NumeroReliberacion),0)+1 FROM dbo.Calidad_Reliberaciones WITH(UPDLOCK,HOLDLOCK) WHERE InspeccionID=@InspeccionID; INSERT INTO dbo.Calidad_Reliberaciones(InspeccionID,EjecucionProduccionID,ParoID,NumeroReliberacion,Motivo,FechaSolicitud,UsuarioSolicitudID,Resultado,Observaciones,UsuarioCreacionID,FechaCreacion,Activo) VALUES(@InspeccionID,@EjecucionProduccionID,@ParoID,@NumeroReliberacion,@Motivo,GETDATE(),@UsuarioID,N'PENDIENTE',@Observacion,@UsuarioID,GETDATE(),1);";
                await using var cmd = new SqlCommand(sqlInsertar, cn, tx);
                cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value = inspeccionId;
                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                cmd.Parameters.Add("@ParoID", SqlDbType.Int).Value = paroId;
                cmd.Parameters.Add("@Motivo", SqlDbType.NVarChar, 500).Value = motivo.Length > 500 ? motivo[..500] : motivo;
                cmd.Parameters.Add("@Observacion", SqlDbType.NVarChar, 1000).Value = observacion.Length > 1000 ? observacion[..1000] : observacion;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                await cmd.ExecuteNonQueryAsync();
            }

            const string sqlHistorial = @"INSERT INTO dbo.Calidad_InspeccionHistorial(InspeccionID,Movimiento,EstadoAnterior,EstadoNuevo,ResultadoCalidad,Etiqueta,Comentario,UsuarioID,FechaMovimiento) VALUES(@InspeccionID,N'SOLICITUD_RELIBERACION',@EstadoAnterior,N'PENDIENTE_RELIBERACION',NULL,NULL,@Comentario,@UsuarioID,GETDATE());";

            await using (var cmd = new SqlCommand(sqlHistorial, cn, tx))
            {
                cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value = inspeccionId;
                cmd.Parameters.Add("@EstadoAnterior", SqlDbType.NVarChar, 50).Value = string.IsNullOrWhiteSpace(estadoAnterior) ? DBNull.Value : estadoAnterior;
                cmd.Parameters.Add("@Comentario", SqlDbType.NVarChar, 1000).Value = observacion.Length > 1000 ? observacion[..1000] : observacion;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                await cmd.ExecuteNonQueryAsync();
            }
        }

        private static async Task ReactivarCambioMoldeRetornoUrgenteAsync(int programaProduccionId, int paroId, DateTime fechaRetorno, int usuarioId, SqlConnection cn, SqlTransaction tx)
        {
            var observacion = $"NSQ_RETORNO_URGENTE:ParoID={paroId}; Cambio de molde obligatorio para regresar a la OF interrumpida después de finalizar la producción urgente.";
            if (observacion.Length > 500) observacion = observacion[..500];
            const string sql = @"
DECLARE @PreparacionAnticipadaID INT;
SELECT TOP(1) @PreparacionAnticipadaID=PreparacionAnticipadaID
FROM dbo.Produccion_PreparacionAnticipada WITH(UPDLOCK,HOLDLOCK)
WHERE ProgramaProduccionID=@ProgramaProduccionID
  AND TipoTarea=N'CAMBIO_MOLDE'
ORDER BY PreparacionAnticipadaID DESC;
IF @PreparacionAnticipadaID IS NULL
BEGIN
    INSERT INTO dbo.Produccion_PreparacionAnticipada
    (
        ProgramaProduccionID,TipoTarea,FechaObjetivo,FechaAviso,Estado,
        UsuarioConfirmacionID,FechaConfirmacion,Observaciones,Activo,
        UsuarioCreacionID,FechaCreacion,UsuarioInicioID,FechaInicioReal,
        FechaFinReal,DuracionRealMinutos,LimiteMinutosAplicado,
        ExcedioLimite,MotivoExceso
    )
    VALUES
    (
        @ProgramaProduccionID,N'CAMBIO_MOLDE',@FechaRetorno,@FechaRetorno,N'PENDIENTE',
        NULL,NULL,@Observaciones,1,@UsuarioID,SYSDATETIME(),NULL,NULL,NULL,NULL,NULL,0,NULL
    );
END
ELSE
BEGIN
    UPDATE dbo.Produccion_PreparacionAnticipada
    SET FechaObjetivo=@FechaRetorno,
        FechaAviso=@FechaRetorno,
        Estado=N'PENDIENTE',
        UsuarioInicioID=NULL,
        FechaInicioReal=NULL,
        FechaFinReal=NULL,
        DuracionRealMinutos=NULL,
        LimiteMinutosAplicado=NULL,
        ExcedioLimite=0,
        MotivoExceso=NULL,
        UsuarioConfirmacionID=NULL,
        FechaConfirmacion=NULL,
        Observaciones=LEFT
        (
            @Observaciones+
            CASE
                WHEN Observaciones IS NULL OR LTRIM(RTRIM(Observaciones))=N'' THEN N''
                ELSE CHAR(13)+CHAR(10)+Observaciones
            END,
            500
        ),
        Activo=1,
        UsuarioModificacionID=@UsuarioID,
        FechaModificacion=SYSDATETIME()
    WHERE PreparacionAnticipadaID=@PreparacionAnticipadaID;
END;";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;
            cmd.Parameters.Add("@FechaRetorno", SqlDbType.DateTime2).Value = fechaRetorno;
            cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 500).Value = observacion;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            await cmd.ExecuteNonQueryAsync();
        }
        private static async Task<string?> ObtenerBloqueoCambioMoldeRetornoUrgenteAsync(int ejecucionProduccionId, SqlConnection cn, SqlTransaction tx)
        {
            if (ejecucionProduccionId <= 0) return null;
            const string sql = @"
SELECT TOP(1)
    p.ParoID,
    p.FechaFinParo,
    tarea.Estado,
    tarea.FechaConfirmacion
FROM dbo.Produccion_Paros p WITH(UPDLOCK,HOLDLOCK)
INNER JOIN dbo.Planeacion_ProgramaProduccion origen WITH(UPDLOCK,HOLDLOCK)
    ON origen.ProgramaProduccionID=p.ProgramaProduccionID
   AND origen.Activo=1
INNER JOIN dbo.Planeacion_ProgramaProduccion urgente WITH(UPDLOCK,HOLDLOCK)
    ON urgente.ProgramaProduccionID=p.ProgramaUrgenteID
   AND urgente.Activo=1
OUTER APPLY
(
    SELECT TOP(1)
        pa.Estado,
        pa.FechaConfirmacion
    FROM dbo.Produccion_PreparacionAnticipada pa WITH(UPDLOCK,HOLDLOCK)
    WHERE pa.ProgramaProduccionID=p.ProgramaProduccionID
      AND pa.TipoTarea=N'CAMBIO_MOLDE'
    ORDER BY pa.PreparacionAnticipadaID DESC
) tarea
WHERE p.EjecucionProduccionID=@EjecucionProduccionID
  AND p.Activo=1
  AND p.FechaFinParo IS NOT NULL
  AND ISNULL(p.EsInterrupcionUrgente,0)=1
  AND ISNULL(origen.MoldeID,-1)<>ISNULL(urgente.MoldeID,-1)
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.Calidad_InspeccionHistorial h
      INNER JOIN dbo.Calidad_Inspecciones ci
          ON ci.InspeccionID=h.InspeccionID
      WHERE ci.EjecucionProduccionID=@EjecucionProduccionID
        AND h.Movimiento=N'CONFIRMACION_INICIO_SERIE_PRODUCCION'
        AND h.FechaMovimiento>=p.FechaFinParo
  )
ORDER BY p.FechaFinParo DESC,p.ParoID DESC;";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync()) return null;
            var paroId = Convert.ToInt32(rd["ParoID"]);
            var fechaFinParo = Convert.ToDateTime(rd["FechaFinParo"]);
            var estado = rd["Estado"] == DBNull.Value ? string.Empty : rd["Estado"]?.ToString()?.Trim().ToUpperInvariant() ?? string.Empty;
            var fechaConfirmacion = rd["FechaConfirmacion"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(rd["FechaConfirmacion"]);
            if (estado == "CONFIRMADA" && fechaConfirmacion.HasValue && fechaConfirmacion.Value >= fechaFinParo) return null;
            if (estado == "EN_PROCESO") return $"El cambio de molde para regresar de la interrupción urgente del paro {paroId} todavía está en proceso. Finalízalo antes de reiniciar la serie.";
            if (estado == "CONFIRMADA") return $"Existe una confirmación de cambio de molde, pero es anterior al cierre de la interrupción urgente del paro {paroId}. Debe confirmarse nuevamente el montaje del molde original.";
            return $"La OF proviene de una interrupción urgente con cambio de molde. Debes realizar y confirmar el cambio de molde de retorno correspondiente al paro {paroId} antes de reiniciar Producción.";
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Terminar(ProduccionTerminarPostVm vm)
        {
            if (!UsuarioEnSesion())
                return RedirectToAction("Login", "Login");

            if (vm.EjecucionProduccionID <= 0)
            {
                TempData["Error"] = "No se recibió una ejecución de Producción válida.";
                return RedirectToAction(nameof(Index));
            }

            var usuarioId = ObtenerUsuarioID();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);

            try
            {
                var ejecucion = await ObtenerEjecucionAsync(
                    vm.EjecucionProduccionID,
                    cn,
                    tx);

                if (ejecucion == null)
                {
                    await tx.RollbackAsync();
                    return NotFound();
                }

                if (!ProduccionEstatus.PuedeTerminar(ejecucion.EstatusID))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La producción no se encuentra en un estado válido para terminar.";
                    return RedirectToAction(nameof(Detalle), new { id = vm.EjecucionProduccionID });
                }

                await RecalcularTotalesEjecucionAsync(
                    vm.EjecucionProduccionID,
                    usuarioId,
                    cn,
                    tx);

                var validacion = await ValidarTerminarProduccionAsync(
                    vm.EjecucionProduccionID,
                    cn,
                    tx);

                if (!validacion.Permitido)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "No se puede terminar la producción porque existen pendientes: " + validacion.Mensaje;
                    return RedirectToAction(nameof(Detalle), new { id = vm.EjecucionProduccionID });
                }

                var estatusProduccion = vm.TerminarParcial
                    ? ProduccionEstatus.TerminadoParcial
                    : ProduccionEstatus.Terminado;

                const string sqlCerrar = @"
UPDATE dbo.Produccion_Ejecucion
SET FechaFinReal=GETDATE(),
    EstatusID=@EstatusID,
    Observaciones=
        CASE
            WHEN @Observaciones IS NULL OR LTRIM(RTRIM(@Observaciones))=N''
                THEN Observaciones
            WHEN Observaciones IS NULL OR LTRIM(RTRIM(Observaciones))=N''
                THEN @Observaciones
            ELSE Observaciones+CHAR(13)+CHAR(10)+@Observaciones
        END,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=GETDATE()
WHERE EjecucionProduccionID=@EjecucionProduccionID
  AND Activo=1
  AND EstatusID IN(@EnProduccion,@Pausado,@TerminadoParcial);

IF @@ROWCOUNT<>1
    THROW 51090,'La ejecución cambió de estado mientras se intentaba terminar.',1;";

                await using (var cmd = new SqlCommand(sqlCerrar, cn, tx))
                {
                    cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = vm.EjecucionProduccionID;
                    cmd.Parameters.Add("@EstatusID", SqlDbType.Int).Value = estatusProduccion;
                    cmd.Parameters.Add("@EnProduccion", SqlDbType.Int).Value = ProduccionEstatus.EnProduccion;
                    cmd.Parameters.Add("@Pausado", SqlDbType.Int).Value = ProduccionEstatus.Pausado;
                    cmd.Parameters.Add("@TerminadoParcial", SqlDbType.Int).Value = ProduccionEstatus.TerminadoParcial;
                    cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 500).Value =
                        string.IsNullOrWhiteSpace(vm.Observaciones)
                            ? DBNull.Value
                            : vm.Observaciones.Trim();
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    await cmd.ExecuteNonQueryAsync();
                }

                await MarcarProgramaTerminadoAsync(
                    ejecucion.ProgramaProduccionID,
                    usuarioId,
                    cn,
                    tx);

                await tx.CommitAsync();

                TempData["Success"] = vm.TerminarParcial
                    ? "Producción terminada parcialmente. No existen pendientes de cajas ni de Calidad."
                    : "Producción terminada correctamente. Todas las piezas fueron asignadas a cajas y no existen pendientes de Calidad.";

                return RedirectToAction(nameof(Detalle), new { id = vm.EjecucionProduccionID });
            }
            catch (Exception ex)
            {
                try
                {
                    await tx.RollbackAsync();
                }
                catch
                {
                }

                TempData["Error"] = "No fue posible terminar producción: " + ex.Message;
                return RedirectToAction(nameof(Detalle), new { id = vm.EjecucionProduccionID });
            }
        }
        private async Task<InterrupcionUrgenteRetornoResultado?> ProcesarRetornoInterrupcionUrgenteAsync(int programaUrgenteId, int usuarioId, SqlConnection cn, SqlTransaction tx)
        {
            var relacionUrgenteInicial = await ObtenerParejaLhRhProduccionAsync(programaUrgenteId, cn, tx);
            var programaUrgenteRaizId = await ObtenerProgramaUrgenteRaizInterrupcionAsync(
                programaUrgenteId,
                relacionUrgenteInicial?.ProgramaParejaID,
                cn,
                tx);

            if (!programaUrgenteRaizId.HasValue) return null;

            var relacionUrgenteRaiz = await ObtenerParejaLhRhProduccionAsync(programaUrgenteRaizId.Value, cn, tx);
            var programaUrgenteParejaId = relacionUrgenteRaiz?.ProgramaParejaID;

            if (!await TodasEjecucionesUrgentesConcluyeronFisicamenteAsync(
                programaUrgenteRaizId.Value,
                programaUrgenteParejaId,
                cn,
                tx))
            {
                return null;
            }

            var contextos = await ObtenerInterrupcionesUrgentesRetornoAsync(
                programaUrgenteRaizId.Value,
                cn,
                tx);

            if (contextos.Count == 0) return null;

            if (contextos.Count > 2)
                throw new InvalidOperationException("La interrupción urgente tiene más de dos ejecuciones originales relacionadas. Revisa los datos antes de procesar el retorno.");

            if (contextos.Any(x => x.EstatusEjecucionInterrumpida != ProduccionEstatus.Pausado))
                throw new InvalidOperationException("Una de las OF interrumpidas ya no se encuentra pausada. No es seguro ejecutar el retorno.");

            var esLhRh = contextos.Any(x => x.EsParoLhRh);

            if (esLhRh)
            {
                if (contextos.Count != 2)
                    throw new InvalidOperationException("La interrupción urgente LH/RH no contiene exactamente dos paros relacionados. No se realizará un retorno parcial.");

                if (contextos.Any(x => !x.EsParoLhRh))
                    throw new InvalidOperationException("La interrupción contiene una mezcla inválida de paro individual y paro LH/RH.");

                var grupo = contextos[0].GrupoParoLhRh;

                if (!grupo.HasValue || contextos.Any(x => x.GrupoParoLhRh != grupo))
                    throw new InvalidOperationException("Los dos paros urgentes LH/RH no pertenecen al mismo grupo físico.");

                var relacionOriginal = await ObtenerParejaLhRhProduccionAsync(
                    contextos[0].ProgramaInterrumpidoID,
                    cn,
                    tx);

                if (relacionOriginal == null ||
                   relacionOriginal.ProgramaParejaID != contextos[1].ProgramaInterrumpidoID)
                {
                    throw new InvalidOperationException("Los dos paros urgentes ya no corresponden a la misma pareja LH/RH.");
                }

                if (contextos.Select(x => x.CambiaMolde).Distinct().Count() > 1)
                    throw new InvalidOperationException("La pareja LH/RH presenta una inconsistencia de molde respecto de la OF urgente.");
            }
            else if (contextos.Count != 1)
            {
                throw new InvalidOperationException("Se encontraron varios paros individuales asociados a la misma interrupción urgente.");
            }

            var ahora = NormalizarFechaMinuto(DateTime.Now);
            var fechaInicioFisica = contextos.Min(x => x.FechaInicioParo);
            var duracionMinutos = (int)Math.Max(0, Math.Floor((ahora - fechaInicioFisica).TotalMinutes));
            var cambiaMolde = contextos.Any(x => x.CambiaMolde);
            var requiereReliberacion = cambiaMolde || duracionMinutos > 15;

            const string sqlCerrar = @"
UPDATE dbo.Produccion_Paros
SET FechaFinParo=@FechaFin,
    DuracionMinutos=DATEDIFF(MINUTE,FechaInicioParo,@FechaFin),
    EsMayorA15Minutos=CASE WHEN DATEDIFF(MINUTE,FechaInicioParo,@FechaFin)>15 THEN 1 ELSE 0 END,
    Descripcion=CASE
        WHEN Descripcion IS NULL OR LTRIM(RTRIM(Descripcion))=N''
            THEN N'Interrupción urgente finalizada. La producción prioritaria liberó la máquina y la OF interrumpida queda pendiente de reinicio real.'
        ELSE Descripcion+CHAR(13)+CHAR(10)+N'Cierre: la producción urgente liberó físicamente la máquina. La OF original queda pendiente de reinicio real y rebase de contador.'
    END,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=GETDATE()
WHERE Activo=1
  AND FechaFinParo IS NULL
  AND ISNULL(EsInterrupcionUrgente,0)=1
  AND ProgramaUrgenteID=@ProgramaUrgenteID;";

            int filasCerradas;

            await using (var cmd = new SqlCommand(sqlCerrar, cn, tx))
            {
                cmd.Parameters.Add("@FechaFin", SqlDbType.DateTime).Value = ahora;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                cmd.Parameters.Add("@ProgramaUrgenteID", SqlDbType.Int).Value = programaUrgenteRaizId.Value;
                filasCerradas = await cmd.ExecuteNonQueryAsync();
            }

            if (filasCerradas != contextos.Count)
                throw new InvalidOperationException("La interrupción urgente cambió mientras se procesaba el retorno. No se aplicará un cierre parcial.");

            foreach (var contexto in contextos)
            {
                var duracionFila = (int)Math.Max(
                    0,
                    Math.Floor((ahora - contexto.FechaInicioParo).TotalMinutes));

                await CambiarEstatusEjecucionAsync(
                    contexto.EjecucionInterrumpidaID,
                    ProduccionEstatus.EnPreparacion,
                    usuarioId,
                    cn,
                    tx);

                await CambiarEstatusProgramaAsync(
                    contexto.ProgramaInterrumpidoID,
                    ProgramaProduccionEstatus.EnPreparacion,
                    usuarioId,
                    cn,
                    tx);

                if (requiereReliberacion)
                {
                    if (contexto.CambiaMolde)
                    {
                        await ReactivarCambioMoldeRetornoUrgenteAsync(
                            contexto.ProgramaInterrumpidoID,
                            contexto.ParoID,
                            ahora,
                            usuarioId,
                            cn,
                            tx);
                    }

                    await CrearOActualizarSolicitudReliberacionCalidadPorParoAsync(
                        contexto.EjecucionInterrumpidaID,
                        contexto.ParoID,
                        duracionFila,
                        usuarioId,
                        cn,
                        tx,
                        forzarReliberacion: contexto.CambiaMolde);
                }
                else
                {
                    await PrepararCalidadReinicioUrgenteSinReliberacionAsync(
                        contexto.EjecucionInterrumpidaID,
                        contexto.ParoID,
                        usuarioId,
                        cn,
                        tx);
                }
            }

            var principal = contextos
                .OrderBy(x => x.ProgramaInterrumpidoID)
                .First();

            return new InterrupcionUrgenteRetornoResultado
            {
                ParoID = principal.ParoID,
                EjecucionInterrumpidaID = principal.EjecucionInterrumpidaID,
                ProgramaInterrumpidoID = principal.ProgramaInterrumpidoID,
                DuracionMinutos = duracionMinutos,
                CambiaMolde = cambiaMolde,
                RequiereReliberacion = requiereReliberacion,
                ProgramasRecorridos = 0
            };
        }
        private static async Task PrepararCalidadReinicioUrgenteSinReliberacionAsync(int ejecucionProduccionId, int paroId, int usuarioId, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
DECLARE @InspeccionID INT;
DECLARE @EstadoAnterior NVARCHAR(50);
DECLARE @ResultadoCalidad NVARCHAR(50);
DECLARE @Etiqueta NVARCHAR(50);
DECLARE @Liberado BIT;
DECLARE @ConfiguracionInvalidada BIT;

SELECT TOP(1)
    @InspeccionID=ci.InspeccionID,
    @EstadoAnterior=ci.Estado,
    @ResultadoCalidad=ci.ResultadoCalidad,
    @Etiqueta=ci.Etiqueta,
    @Liberado=ISNULL(ci.Liberado,0),
    @ConfiguracionInvalidada=ISNULL(ci.ConfiguracionInvalidada,0)
FROM dbo.Calidad_Inspecciones ci WITH(UPDLOCK,HOLDLOCK)
WHERE ci.EjecucionProduccionID=@EjecucionProduccionID
  AND ISNULL(ci.Estado,N'')<>N'CERRADA'
ORDER BY ci.InspeccionID DESC;

IF @InspeccionID IS NULL
    THROW 51640,'No existe una inspección activa de Calidad para regresar a la OF interrumpida.',1;

IF ISNULL(@ConfiguracionInvalidada,0)=1
    THROW 51641,'La configuración de Calidad está invalidada. No es seguro reiniciar automáticamente después de la urgencia.',1;

IF ISNULL(@Liberado,0)=0
   OR UPPER(LTRIM(RTRIM(ISNULL(@ResultadoCalidad,N''))))<>N'VERDE'
   OR UPPER(LTRIM(RTRIM(ISNULL(@Etiqueta,N''))))<>N'VERDE'
    THROW 51642,'La OF interrumpida ya no conserva una liberación verde válida de Calidad.',1;

UPDATE dbo.Calidad_Inspecciones
SET Estado=N'PRODUCCION_LIBERADA',
    RequiereReliberacion=0,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=GETDATE()
WHERE InspeccionID=@InspeccionID;

INSERT INTO dbo.Calidad_InspeccionHistorial
(
    InspeccionID,
    Movimiento,
    EstadoAnterior,
    EstadoNuevo,
    ResultadoCalidad,
    Etiqueta,
    Comentario,
    UsuarioID,
    FechaMovimiento
)
VALUES
(
    @InspeccionID,
    N'RETORNO_URGENTE_SIN_RELIBERACION',
    @EstadoAnterior,
    N'PRODUCCION_LIBERADA',
    @ResultadoCalidad,
    @Etiqueta,
    N'La interrupción urgente terminó sin cambio de molde y sin superar 15 minutos. Se conserva la liberación verde previa; Producción debe confirmar el contador actual y reiniciar serie.',
    @UsuarioID,
    GETDATE()
);";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
            cmd.Parameters.Add("@ParoID", SqlDbType.Int).Value = paroId;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            await cmd.ExecuteNonQueryAsync();
        }
        private async Task RebasarContadorReinicioSerieAsync(int ejecucionProduccionId, int? maquinaId, int paroId, long contadorMaquinaActual, DateTime fechaReinicio, int usuarioId, SqlConnection cn, SqlTransaction tx)
        {
            if (ejecucionProduccionId <= 0)
                throw new InvalidOperationException("La ejecución no es válida para rebasar el contador.");

            if (contadorMaquinaActual < 0)
                throw new InvalidOperationException("El contador actual de la máquina no puede ser negativo.");

            var configuracion = await ObtenerConfiguracionActualAsync(
                ejecucionProduccionId,
                cn,
                tx);

            if (configuracion == null)
                throw new InvalidOperationException("No existe una configuración vigente para establecer la nueva base del contador.");

            var ultimaLectura = await ObtenerUltimaLecturaContadorAsync(
                ejecucionProduccionId,
                cn,
                tx);

            var huboReinicioFisico =
                ultimaLectura != null &&
                contadorMaquinaActual < ultimaLectura.ValorContador;

            var tipoLectura = huboReinicioFisico
                ? ProduccionTipoLecturaContador.ReinicioContador
                : "REBASE_REINICIO_SERIE";

            var motivoReinicio = huboReinicioFisico
                ? $"Al reiniciar serie después del ParoID {paroId}, el contador físico capturado ({contadorMaquinaActual:N0}) fue menor a la última lectura de esta ejecución ({ultimaLectura!.ValorContador:N0})."
                : null;

            var observaciones =
                $"Nueva base lógica del contador al reiniciar serie después del ParoID {paroId}. " +
                $"Contador físico confirmado: {contadorMaquinaActual:N0}. " +
                "Esta lectura evita atribuir a la OF reiniciada los ciclos realizados durante la interrupción, preparación o producción urgente.";

            await RegistrarLecturaContadorConfiguracionAsync(
                ejecucionProduccionId,
                configuracion.ConfiguracionCorridaID,
                maquinaId,
                tipoLectura,
                contadorMaquinaActual,
                fechaReinicio,
                huboReinicioFisico,
                motivoReinicio,
                observaciones,
                usuarioId,
                cn,
                tx);
        }
        private async Task<ProduccionEjecucionVm?> ObtenerEjecucionAsync(int ejecucionProduccionId, SqlConnection cn)
        {
            const string sql = @"
SELECT EjecucionProduccionID,ProgramaProduccionID,SolicitudProduccionID,SolicitudProduccionDetalleID,ReleaseID,ReleaseDetalleID,
       MaquinaID,MaquinaCodigo,MaquinaNombre,ParteID,NumeroParte,ReferenciaSAP,DescripcionParte,MoldeID,MoldeCodigo,
       ISNULL(EsCambioMolde,0) AS EsCambioMolde,OperadorID,OperadorNombre,OperadorAuxiliarID,OperadorAuxiliarNombre,
       ISNULL(OperadoresModificadosManual,0) AS OperadoresModificadosManual,MotivoCambioOperadores,
       FechaInicioReal,FechaFinReal,FechaLiberacionMaquina,UsuarioLiberacionMaquinaID,ObservacionesLiberacionMaquina,
       CantidadPlaneada,CantidadOKTotal,CantidadSospechosaTotal,CantidadScrapTotal,
       EstatusID,Observaciones,UsuarioCreacionID,FechaCreacion,UsuarioModificacionID,FechaModificacion,Activo
FROM dbo.Produccion_Ejecucion
WHERE EjecucionProduccionID=@EjecucionProduccionID AND Activo=1;";
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
            await using var rd = await cmd.ExecuteReaderAsync();
            return await rd.ReadAsync() ? MapearEjecucion(rd) : null;
        }

        private async Task<ProduccionEjecucionVm?> ObtenerEjecucionAsync(int ejecucionProduccionId, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
SELECT EjecucionProduccionID,ProgramaProduccionID,SolicitudProduccionID,SolicitudProduccionDetalleID,ReleaseID,ReleaseDetalleID,
       MaquinaID,MaquinaCodigo,MaquinaNombre,ParteID,NumeroParte,ReferenciaSAP,DescripcionParte,MoldeID,MoldeCodigo,
       ISNULL(EsCambioMolde,0) AS EsCambioMolde,OperadorID,OperadorNombre,OperadorAuxiliarID,OperadorAuxiliarNombre,
       ISNULL(OperadoresModificadosManual,0) AS OperadoresModificadosManual,MotivoCambioOperadores,
       FechaInicioReal,FechaFinReal,FechaLiberacionMaquina,UsuarioLiberacionMaquinaID,ObservacionesLiberacionMaquina,
       CantidadPlaneada,CantidadOKTotal,CantidadSospechosaTotal,CantidadScrapTotal,
       EstatusID,Observaciones,UsuarioCreacionID,FechaCreacion,UsuarioModificacionID,FechaModificacion,Activo
FROM dbo.Produccion_Ejecucion WITH (UPDLOCK,HOLDLOCK)
WHERE EjecucionProduccionID=@EjecucionProduccionID AND Activo=1;";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
            await using var rd = await cmd.ExecuteReaderAsync();
            return await rd.ReadAsync() ? MapearEjecucion(rd) : null;
        }
        private static bool TieneColumna(SqlDataReader rd, string columna)
        {
            for (var i = 0; i < rd.FieldCount; i++)
            {
                if (string.Equals(rd.GetName(i), columna, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private async Task<List<ProduccionRegistroHoraVm>> ObtenerRegistrosHoraAsync(int ejecucionProduccionId, SqlConnection cn)
        {
            var lista = new List<ProduccionRegistroHoraVm>();
            const string sql = @"
SELECT
    RegistroHoraID,
    EjecucionProduccionID,
    ProgramaProduccionID,
    SolicitudProduccionID,
    MaquinaID,
    OperadorID,
    FechaProduccion,
    HoraInicio,
    HoraFin,
    ISNULL(CantidadOK,0) AS CantidadOK,
    ISNULL(CantidadSospechosa,0) AS CantidadSospechosa,
    ISNULL(CantidadScrap,0) AS CantidadScrap,
    ObjetivoHora,
    ObjetivoBloque,
    CumplioObjetivo,
    DiferenciaObjetivo,
    PorcentajeCumplimiento,
    PiezasCalculadasContador,
    MinutosProductivos,
    ISNULL(EsTiempoExtra,0) AS EsTiempoExtra,
    TipoBloque,
    TiempoExtraID,
    NumeroCorteTiempoExtra,
    ISNULL(TieneCambioConfiguracion,0) AS TieneCambioConfiguracion,
    ISNULL(TieneReinicioContador,0) AS TieneReinicioContador,
    Observaciones,
    UsuarioCreacionID,
    FechaCreacion,
    UsuarioModificacionID,
    FechaModificacion,
    Activo
FROM dbo.Produccion_RegistroHora
WHERE EjecucionProduccionID=@EjecucionProduccionID
  AND Activo=1
ORDER BY FechaProduccion DESC,HoraInicio DESC,RegistroHoraID DESC;";

            await using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                await using var rd = await cmd.ExecuteReaderAsync();

                while (await rd.ReadAsync())
                {
                    lista.Add(new ProduccionRegistroHoraVm
                    {
                        RegistroHoraID = Entero(rd, "RegistroHoraID"),
                        EjecucionProduccionID = Entero(rd, "EjecucionProduccionID"),
                        ProgramaProduccionID = Entero(rd, "ProgramaProduccionID"),
                        SolicitudProduccionID = NullableEntero(rd, "SolicitudProduccionID"),
                        MaquinaID = NullableEntero(rd, "MaquinaID"),
                        OperadorID = NullableEntero(rd, "OperadorID"),
                        FechaProduccion = Fecha(rd, "FechaProduccion"),
                        HoraInicio = Tiempo(rd, "HoraInicio"),
                        HoraFin = Tiempo(rd, "HoraFin"),
                        CantidadOK = Entero(rd, "CantidadOK"),
                        CantidadSospechosa = Entero(rd, "CantidadSospechosa"),
                        CantidadScrap = Entero(rd, "CantidadScrap"),
                        ObjetivoHora = NullableEntero(rd, "ObjetivoHora"),
                        ObjetivoBloque = NullableEntero(rd, "ObjetivoBloque"),
                        CumplioObjetivo = rd["CumplioObjetivo"] == DBNull.Value ? null : Convert.ToBoolean(rd["CumplioObjetivo"]),
                        DiferenciaObjetivo = NullableEntero(rd, "DiferenciaObjetivo"),
                        PorcentajeCumplimiento = rd["PorcentajeCumplimiento"] == DBNull.Value ? null : Convert.ToDecimal(rd["PorcentajeCumplimiento"]),
                        PiezasCalculadasContador = rd["PiezasCalculadasContador"] == DBNull.Value ? null : Convert.ToInt32(rd["PiezasCalculadasContador"]),
                        MinutosProductivos = rd["MinutosProductivos"] == DBNull.Value ? null : Convert.ToDecimal(rd["MinutosProductivos"]),
                        EsTiempoExtra = rd["EsTiempoExtra"] != DBNull.Value && Convert.ToBoolean(rd["EsTiempoExtra"]),
                        TipoBloque = TextoNullable(rd, "TipoBloque"),
                        TiempoExtraID = NullableEntero(rd, "TiempoExtraID"),
                        NumeroCorteTiempoExtra = NullableEntero(rd, "NumeroCorteTiempoExtra"),
                        TieneCambioConfiguracion = rd["TieneCambioConfiguracion"] != DBNull.Value && Convert.ToBoolean(rd["TieneCambioConfiguracion"]),
                        TieneReinicioContador = rd["TieneReinicioContador"] != DBNull.Value && Convert.ToBoolean(rd["TieneReinicioContador"]),
                        Observaciones = TextoNullable(rd, "Observaciones"),
                        UsuarioCreacionID = NullableEntero(rd, "UsuarioCreacionID"),
                        FechaCreacion = Fecha(rd, "FechaCreacion"),
                        UsuarioModificacionID = NullableEntero(rd, "UsuarioModificacionID"),
                        FechaModificacion = NullableFecha(rd, "FechaModificacion"),
                        Activo = Booleano(rd, "Activo")
                    });
                }
            }

            foreach (var registro in lista)
                registro.Segmentos = await ObtenerSegmentosRegistroHoraAsync(registro.RegistroHoraID, cn);

            return lista;
        }

        private async Task<List<ProduccionRegistroHoraSegmentoVm>> ObtenerSegmentosRegistroHoraAsync(int registroHoraId, SqlConnection cn)
        {
            var lista = new List<ProduccionRegistroHoraSegmentoVm>();
            if (registroHoraId <= 0) return lista;

            const string sql = @"
SELECT
    RegistroHoraSegmentoID,
    RegistroHoraID,
    EjecucionProduccionID,
    ConfiguracionCorridaID,
    NumeroSegmento,
    FechaHoraInicio,
    FechaHoraFin,
    ISNULL(MinutosProductivos,0) AS MinutosProductivos,
    ContadorInicial,
    ContadorFinal,
    CavidadesUsadas,
    TiempoCicloSegundos,
    ObjetivoHoraCalculado,
    ObjetivoSegmentoCalculado,
    Observaciones,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
FROM dbo.Produccion_RegistroHoraSegmentos
WHERE RegistroHoraID=@RegistroHoraID
  AND Activo=1
ORDER BY NumeroSegmento,RegistroHoraSegmentoID;";

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@RegistroHoraID", SqlDbType.Int).Value = registroHoraId;
            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                var contadorInicial = rd["ContadorInicial"] == DBNull.Value ? 0L : Convert.ToInt64(rd["ContadorInicial"]);
                var contadorFinal = rd["ContadorFinal"] == DBNull.Value ? 0L : Convert.ToInt64(rd["ContadorFinal"]);
                var cavidades = rd["CavidadesUsadas"] == DBNull.Value ? 0 : Convert.ToInt32(rd["CavidadesUsadas"]);
                var ciclos = contadorFinal >= contadorInicial ? contadorFinal - contadorInicial : 0L;
                var piezasCalculadas = checked(ciclos * (long)cavidades);

                lista.Add(new ProduccionRegistroHoraSegmentoVm
                {
                    RegistroHoraSegmentoID = Convert.ToInt64(rd["RegistroHoraSegmentoID"]),
                    RegistroHoraID = Convert.ToInt32(rd["RegistroHoraID"]),
                    EjecucionProduccionID = Convert.ToInt32(rd["EjecucionProduccionID"]),
                    ConfiguracionCorridaID = Convert.ToInt32(rd["ConfiguracionCorridaID"]),
                    NumeroSegmento = Convert.ToInt32(rd["NumeroSegmento"]),
                    FechaHoraInicio = Convert.ToDateTime(rd["FechaHoraInicio"]),
                    FechaHoraFin = Convert.ToDateTime(rd["FechaHoraFin"]),
                    MinutosProductivos = rd["MinutosProductivos"] == DBNull.Value ? 0m : Convert.ToDecimal(rd["MinutosProductivos"]),
                    ContadorInicial = contadorInicial,
                    ContadorFinal = contadorFinal,
                    CiclosPeriodo = ciclos,
                    CavidadesUsadas = cavidades,
                    TiempoCicloSegundos = rd["TiempoCicloSegundos"] == DBNull.Value ? 0m : Convert.ToDecimal(rd["TiempoCicloSegundos"]),
                    PiezasCalculadas = piezasCalculadas,
                    ObjetivoHoraCalculado = rd["ObjetivoHoraCalculado"] == DBNull.Value ? 0m : Convert.ToDecimal(rd["ObjetivoHoraCalculado"]),
                    ObjetivoSegmentoCalculado = rd["ObjetivoSegmentoCalculado"] == DBNull.Value ? null : Convert.ToDecimal(rd["ObjetivoSegmentoCalculado"]),
                    Observaciones = rd["Observaciones"] == DBNull.Value ? null : rd["Observaciones"]?.ToString()?.Trim(),
                    UsuarioCreacionID = rd["UsuarioCreacionID"] == DBNull.Value ? null : Convert.ToInt32(rd["UsuarioCreacionID"]),
                    FechaCreacion = Convert.ToDateTime(rd["FechaCreacion"]),
                    Activo = rd["Activo"] != DBNull.Value && Convert.ToBoolean(rd["Activo"])
                });
            }

            return lista;
        }
        private async Task<List<ProduccionProgramaDisponibleVm>> ObtenerProgramasDisponiblesAsync(
     string? busqueda,
     int? maquinaId,
     DateTime? fechaDesde,
     DateTime? fechaHasta,
     SqlConnection cn)
        {
            var lista = new List<ProduccionProgramaDisponibleVm>();
            const string sql = @"
SELECT
    pp.ProgramaProduccionID,
    pp.SolicitudProduccionID,
    pp.SolicitudProduccionDetalleID,
    pp.ReleaseDetalleID,
    rd.ReleaseID,
    s.FolioSolicitud,
    s.NumeroOFRecibida,
    pp.MaquinaID,
    COALESCE(NULLIF(pp.MaquinaCodigo,N''),maq.Codigo) AS MaquinaCodigo,
    COALESCE(NULLIF(pp.MaquinaNombre,N''),maq.Nombre) AS MaquinaNombre,
    pp.ParteID,
    pp.NumeroParte,
    pp.ReferenciaSAP,
    pp.DesignacionDescripcionSAP AS DescripcionParte,
    pp.MoldeID,
    pp.MoldeCodigo,
    CONVERT(INT,ISNULL(pp.CantidadProgramada,0)) AS CantidadProgramada,
    pp.FechaInicioProgramada,
    pp.FechaFinProgramada,
    ISNULL(pp.SecuenciaMaquina,999999) AS SecuenciaMaquina,
    ISNULL(pp.EstatusID,1) AS EstatusID,
    escala.OperadorSugeridoID,
    escala.OperadorSugeridoNombre,
    escala.TurnoSugeridoNombre,
    escala.TurnoSugeridoColor,
    escala.EscalaAsignacionID
FROM dbo.Planeacion_ProgramaProduccion pp
INNER JOIN dbo.SolicitudesProduccion s
    ON s.SolicitudProduccionID=pp.SolicitudProduccionID
   AND s.Activo=1
LEFT JOIN dbo.Planeacion_ReleaseDetalle rd
    ON rd.ReleaseDetalleID=pp.ReleaseDetalleID
LEFT JOIN dbo.ERP_Maquinas maq
    ON maq.MaquinaID=pp.MaquinaID
OUTER APPLY
(
    SELECT TOP (1)
        a.AsignacionID AS EscalaAsignacionID,
        a.PersonalID AS OperadorSugeridoID,
        LTRIM(RTRIM(
            ISNULL(p.Nombre,N'')+N' '+
            ISNULL(p.ApellidoPaterno,N'')+N' '+
            ISNULL(p.ApellidoMaterno,N'')
        )) AS OperadorSugeridoNombre,
        et.Nombre AS TurnoSugeridoNombre,
        et.Color AS TurnoSugeridoColor
    FROM dbo.RRHH_EscalaAsignaciones a
    INNER JOIN dbo.RRHH_EscalasPersonal esc
        ON esc.EscalaID=a.EscalaID
       AND esc.Activo=1
       AND esc.Estado=N'Publicada'
    INNER JOIN dbo.Persona p
        ON p.PersonaID=a.PersonalID
       AND ISNULL(p.EsColaboradorActivo,1)=1
    INNER JOIN dbo.RRHH_EscalaTurnos et
        ON et.EscalaID=a.EscalaID
       AND et.EscalaTurnoID=a.EscalaTurnoID
    WHERE pp.MaquinaID IS NOT NULL
      AND pp.FechaInicioProgramada IS NOT NULL
      AND a.Activo=1
      AND a.MaquinaID=pp.MaquinaID
      AND CAST(pp.FechaInicioProgramada AS date)>=CAST(a.FechaInicio AS date)
      AND CAST(pp.FechaInicioProgramada AS date)<=CAST(a.FechaFin AS date)
      AND
      (
            ISNULL(et.EsFlexible,0)=1
         OR et.HoraInicio IS NULL
         OR et.HoraFin IS NULL
         OR
         (
                ISNULL(et.CruzaDiaSiguiente,0)=0
            AND CAST(pp.FechaInicioProgramada AS time)>=et.HoraInicio
            AND CAST(pp.FechaInicioProgramada AS time)<et.HoraFin
         )
         OR
         (
                ISNULL(et.CruzaDiaSiguiente,0)=1
            AND
            (
                   CAST(pp.FechaInicioProgramada AS time)>=et.HoraInicio
                OR CAST(pp.FechaInicioProgramada AS time)<et.HoraFin
            )
         )
      )
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.RRHH_NovedadesPersonal n
          WHERE n.EscalaID=a.EscalaID
            AND n.PersonalID=a.PersonalID
            AND n.Activo=1
            AND n.TipoNovedad IN(N'Baja',N'Incapacidad',N'Vacaciones')
            AND CAST(pp.FechaInicioProgramada AS date)>=CAST(n.FechaInicio AS date)
            AND CAST(pp.FechaInicioProgramada AS date)<=CAST(ISNULL(n.FechaFin,n.FechaInicio) AS date)
      )
    ORDER BY et.Orden,a.AsignacionID DESC
) escala
WHERE pp.Activo=1
  AND pp.SolicitudProduccionID IS NOT NULL
  AND pp.SolicitudProduccionID>0
  AND pp.SolicitudProduccionDetalleID IS NOT NULL
  AND pp.SolicitudProduccionDetalleID>0
  AND ISNULL(pp.EstatusID,1) NOT IN(3,4,5,9,99)
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.Produccion_Ejecucion e
      WHERE e.ProgramaProduccionID=pp.ProgramaProduccionID
        AND e.Activo=1
        AND e.EstatusID NOT IN(9,99)
  )
  AND (@MaquinaID IS NULL OR pp.MaquinaID=@MaquinaID)
  AND (@FechaDesde IS NULL OR CONVERT(date,pp.FechaInicioProgramada)>=@FechaDesde)
  AND (@FechaHasta IS NULL OR CONVERT(date,pp.FechaInicioProgramada)<=@FechaHasta)
  AND
  (
        @Busqueda IS NULL
     OR CONVERT(NVARCHAR(30),pp.ProgramaProduccionID) LIKE N'%'+@Busqueda+N'%'
     OR s.FolioSolicitud LIKE N'%'+@Busqueda+N'%'
     OR s.NumeroOFRecibida LIKE N'%'+@Busqueda+N'%'
     OR pp.NumeroParte LIKE N'%'+@Busqueda+N'%'
     OR pp.ReferenciaSAP LIKE N'%'+@Busqueda+N'%'
     OR pp.DesignacionDescripcionSAP LIKE N'%'+@Busqueda+N'%'
     OR pp.MaquinaCodigo LIKE N'%'+@Busqueda+N'%'
     OR pp.MaquinaNombre LIKE N'%'+@Busqueda+N'%'
     OR pp.MoldeCodigo LIKE N'%'+@Busqueda+N'%'
     OR escala.OperadorSugeridoNombre LIKE N'%'+@Busqueda+N'%'
  )
ORDER BY
    COALESCE(NULLIF(pp.MaquinaCodigo,N''),maq.Codigo),
    pp.FechaInicioProgramada,
    ISNULL(pp.SecuenciaMaquina,999999),
    pp.ProgramaProduccionID;";
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = (object?)maquinaId ?? DBNull.Value;
            cmd.Parameters.Add("@FechaDesde", SqlDbType.Date).Value = (object?)fechaDesde?.Date ?? DBNull.Value;
            cmd.Parameters.Add("@FechaHasta", SqlDbType.Date).Value = (object?)fechaHasta?.Date ?? DBNull.Value;
            cmd.Parameters.Add("@Busqueda", SqlDbType.NVarChar, 200).Value = string.IsNullOrWhiteSpace(busqueda) ? DBNull.Value : busqueda.Trim();
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                lista.Add(new ProduccionProgramaDisponibleVm
                {
                    ProgramaProduccionID = Entero(rd, "ProgramaProduccionID"),
                    SolicitudProduccionID = NullableEntero(rd, "SolicitudProduccionID"),
                    SolicitudProduccionDetalleID = NullableEntero(rd, "SolicitudProduccionDetalleID"),
                    ReleaseID = NullableEntero(rd, "ReleaseID"),
                    ReleaseDetalleID = NullableEntero(rd, "ReleaseDetalleID"),
                    FolioSolicitud = TextoNullable(rd, "FolioSolicitud"),
                    NumeroOFRecibida = TextoNullable(rd, "NumeroOFRecibida"),
                    MaquinaID = NullableEntero(rd, "MaquinaID"),
                    MaquinaCodigo = TextoNullable(rd, "MaquinaCodigo"),
                    MaquinaNombre = TextoNullable(rd, "MaquinaNombre"),
                    ParteID = NullableEntero(rd, "ParteID"),
                    NumeroParte = TextoNullable(rd, "NumeroParte"),
                    ReferenciaSAP = TextoNullable(rd, "ReferenciaSAP"),
                    DescripcionParte = TextoNullable(rd, "DescripcionParte"),
                    MoldeID = NullableEntero(rd, "MoldeID"),
                    MoldeCodigo = TextoNullable(rd, "MoldeCodigo"),
                    CantidadProgramada = NullableEntero(rd, "CantidadProgramada"),
                    FechaInicioProgramada = NullableFecha(rd, "FechaInicioProgramada"),
                    FechaFinProgramada = NullableFecha(rd, "FechaFinProgramada"),
                    EstatusID = Entero(rd, "EstatusID"),
                    OperadorSugeridoID = NullableEntero(rd, "OperadorSugeridoID"),
                    OperadorSugeridoNombre = TextoNullable(rd, "OperadorSugeridoNombre"),
                    TurnoSugeridoNombre = TextoNullable(rd, "TurnoSugeridoNombre"),
                    TurnoSugeridoColor = TextoNullable(rd, "TurnoSugeridoColor"),
                    EscalaAsignacionID = NullableEntero(rd, "EscalaAsignacionID")
                });
            }
            return lista;
        }
        private sealed class ProgramaParaProduccion
        {
            public int ProgramaProduccionID { get; set; }
            public int? SolicitudProduccionID { get; set; }
            public int? SolicitudProduccionDetalleID { get; set; }
            public int? ReleaseID { get; set; }
            public int? ReleaseDetalleID { get; set; }

            public int? MaquinaID { get; set; }
            public string? MaquinaCodigo { get; set; }
            public string? MaquinaNombre { get; set; }

            public int? ParteID { get; set; }
            public string? NumeroParte { get; set; }
            public string? ReferenciaSAP { get; set; }
            public string? DescripcionParte { get; set; }

            public int? MoldeID { get; set; }
            public string? MoldeCodigo { get; set; }

            public int? CantidadPlaneada { get; set; }

            public DateTime? FechaInicioProgramada { get; set; }
            public DateTime? FechaFinProgramada { get; set; }

            public TimeSpan? Cambio { get; set; }
            public TimeSpan? Arranque { get; set; }
            public bool EsCambioMolde { get; set; }

            public int? OperadorPrincipalPlaneadoID { get; set; }
            public string? OperadorPrincipalPlaneadoNombre { get; set; }

            public int? OperadorAuxiliarID { get; set; }
            public string? OperadorAuxiliarNombre { get; set; }
        }


        private async Task<bool> ExisteEjecucionProgramaAsync( int ejecucionProduccionId, int programaProduccionId, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
SELECT COUNT(1)
FROM dbo.Produccion_Ejecucion
WHERE EjecucionProduccionID = @EjecucionProduccionID
  AND ProgramaProduccionID = @ProgramaProduccionID
  AND Activo = 1;";

            using (var cmd = new SqlCommand(sql, cn, tx))
            {
                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value =
                    ejecucionProduccionId;

                cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value =
                    programaProduccionId;

                var result = await cmd.ExecuteScalarAsync();

                return Convert.ToInt32(result) > 0;
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelarRecepcionOF(
    int recepcionOFId,
    int ejecucionProduccionId,
    string? motivoCancelacion)
        {
            if (!UsuarioEnSesion())
                return RedirectToAction("Login", "Login");

            if (recepcionOFId <= 0 || ejecucionProduccionId <= 0)
            {
                TempData["Error"] = "No se recibió la recepción a cancelar.";
                return RedirectToAction(nameof(Index));
            }

            if (string.IsNullOrWhiteSpace(motivoCancelacion))
            {
                TempData["Error"] = "Captura el motivo de cancelación de la recepción.";
                return RedirectToAction(nameof(Detalle), new { id = ejecucionProduccionId });
            }

            var usuarioId = ObtenerUsuarioID();

            using (var cn = new SqlConnection(ConnectionString))
            {
                await cn.OpenAsync();

                const string sql = @"
UPDATE dbo.Produccion_RecepcionOF
SET
    Activo = 0,
    UsuarioCancelacionID = @UsuarioCancelacionID,
    FechaCancelacion = GETDATE(),
    MotivoCancelacion = @MotivoCancelacion
WHERE RecepcionOFID = @RecepcionOFID
  AND EjecucionProduccionID = @EjecucionProduccionID
  AND Activo = 1;";

                using (var cmd = new SqlCommand(sql, cn))
                {
                    cmd.Parameters.Add("@UsuarioCancelacionID", SqlDbType.Int).Value =
                        usuarioId > 0 ? usuarioId : DBNull.Value;

                    cmd.Parameters.Add("@MotivoCancelacion", SqlDbType.NVarChar, 500).Value =
                        motivoCancelacion.Trim();

                    cmd.Parameters.Add("@RecepcionOFID", SqlDbType.Int).Value =
                        recepcionOFId;

                    cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value =
                        ejecucionProduccionId;

                    var afectados = await cmd.ExecuteNonQueryAsync();

                    TempData[afectados > 0 ? "Success" : "Error"] =
                        afectados > 0
                            ? "Recepción cancelada correctamente."
                            : "No se encontró la recepción activa para cancelar.";
                }
            }

            return RedirectToAction(nameof(Detalle), new { id = ejecucionProduccionId });
        }

        private async Task<int> ObtenerOCrearChecklistsInicialesAsync(
      ProduccionEjecucionVm ejecucion,
      int usuarioId,
      SqlConnection cn,
      SqlTransaction tx)
        {
            if (ejecucion == null)
                throw new ArgumentNullException(nameof(ejecucion));
            await SincronizarOFEnEjecucionDesdeProgramaAsync(
                ejecucion,
                usuarioId,
                cn,
                tx);
            if (!ejecucion.SolicitudProduccionID.HasValue ||
               ejecucion.SolicitudProduccionID.Value <= 0)
            {
                throw new InvalidOperationException(
                    "La ejecución no tiene una OF válida relacionada.");
            }
            if (!ejecucion.SolicitudProduccionDetalleID.HasValue ||
               ejecucion.SolicitudProduccionDetalleID.Value <= 0)
            {
                throw new InvalidOperationException(
                    "La ejecución no tiene un detalle de OF válido relacionado.");
            }
            var fechaOperacion = DateTime.Today;
            var checklistArranqueId =
                await ObtenerOCrearChecklistFormatoAsync(
                    ejecucion: ejecucion,
                    codigoFormato: "GQ-F-PR01-06",
                    versionFormato: "Ver.10",
                    tipoChecklist: "ARRANQUE_LIBERACION",
                    momentoProceso: "INICIO_PRODUCCION",
                    fechaOperacion: fechaOperacion,
                    turnoId: null,
                    turnoNombre: null,
                    esRecurrente: false,
                    requiereCambioMolde: ejecucion.EsCambioMolde,
                    numeroAplicacion: 1,
                    usuarioId: usuarioId,
                    cn: cn,
                    tx: tx);
            if (ejecucion.EsCambioMolde)
            {
                await ObtenerOCrearChecklistFormatoAsync(
                    ejecucion: ejecucion,
                    codigoFormato: "GQ-F-PR01-03",
                    versionFormato: "Ver.09",
                    tipoChecklist: "CAMBIO_MOLDE",
                    momentoProceso: "INICIO_PRODUCCION",
                    fechaOperacion: fechaOperacion,
                    turnoId: null,
                    turnoNombre: null,
                    esRecurrente: false,
                    requiereCambioMolde: true,
                    numeroAplicacion: 1,
                    usuarioId: usuarioId,
                    cn: cn,
                    tx: tx);
            }
            await ObtenerOCrearChecklistFormatoAsync(
                ejecucion: ejecucion,
                codigoFormato: "GQ-F-PR01-05",
                versionFormato: "Ver.10",
                tipoChecklist: "MONITOREO_PARAMETROS",
                momentoProceso: "INICIO_PRODUCCION",
                fechaOperacion: fechaOperacion,
                turnoId: null,
                turnoNombre: null,
                esRecurrente: false,
                requiereCambioMolde: ejecucion.EsCambioMolde,
                numeroAplicacion: 1,
                usuarioId: usuarioId,
                cn: cn,
                tx: tx);
            await ObtenerOCrearChecklistPerifericosTurnoAsync(
                ejecucion,
                DateTime.Now,
                usuarioId,
                cn,
                tx);
            return checklistArranqueId;
        }
        private async Task<int>   ObtenerOCrearChecklistFormatoAsync( ProduccionEjecucionVm ejecucion,  string codigoFormato,
        string versionFormato,
        string tipoChecklist,
        string momentoProceso,
        DateTime fechaOperacion,
        int? turnoId,
        string? turnoNombre,
        bool esRecurrente,
        bool requiereCambioMolde,
        int numeroAplicacion,
        int usuarioId,
        SqlConnection cn,
        SqlTransaction tx)
        {
            codigoFormato =
                codigoFormato?.Trim()
                ?? string.Empty;

            versionFormato =
                versionFormato?.Trim()
                ?? string.Empty;

            tipoChecklist =
                tipoChecklist?.Trim().ToUpperInvariant()
                ?? string.Empty;

            momentoProceso =
                momentoProceso?.Trim().ToUpperInvariant()
                ?? string.Empty;

            if (string.IsNullOrWhiteSpace(codigoFormato))
            {
                throw new InvalidOperationException(
                    "El código del formato es obligatorio.");
            }

            if (string.IsNullOrWhiteSpace(versionFormato))
            {
                throw new InvalidOperationException(
                    "La versión del formato es obligatoria.");
            }

            if (!ejecucion.SolicitudProduccionID.HasValue ||
                ejecucion.SolicitudProduccionID.Value <= 0)
            {
                throw new InvalidOperationException(
                    "La ejecución no está relacionada con una OF.");
            }

            /*
             * Primero se busca una aplicación existente.
             *
             * Para formatos no recurrentes:
             * ejecución + OF + formato + aplicación.
             *
             * Para periféricos:
             * ejecución + OF + formato + fecha + turno.
             */
            const string sqlExiste = @"
SELECT TOP (1)
    ChecklistArranqueID
FROM dbo.Produccion_ChecklistArranque WITH (UPDLOCK, HOLDLOCK)
WHERE EjecucionProduccionID = @EjecucionProduccionID
  AND SolicitudProduccionID = @SolicitudProduccionID
  AND CodigoFormato = @CodigoFormato
  AND VersionFormato = @VersionFormato
  AND TipoChecklist = @TipoChecklist
  AND Activo = 1
  AND
  (
      (
          @EsRecurrente = 0
          AND ISNULL(EsRecurrente, 0) = 0
          AND ISNULL(NumeroAplicacion, 1) =
              @NumeroAplicacion
      )
      OR
      (
          @EsRecurrente = 1
          AND ISNULL(EsRecurrente, 0) = 1
          AND FechaOperacion = @FechaOperacion
          AND TurnoID = @TurnoID
      )
  )
ORDER BY ChecklistArranqueID DESC;";

            int? checklistExistenteId = null;

            await using (
                var cmd = new SqlCommand(
                    sqlExiste,
                    cn,
                    tx))
            {
                cmd.Parameters.Add(
                    "@EjecucionProduccionID",
                    SqlDbType.Int).Value =
                    ejecucion.EjecucionProduccionID;

                cmd.Parameters.Add(
                    "@SolicitudProduccionID",
                    SqlDbType.Int).Value =
                    ejecucion.SolicitudProduccionID.Value;

                cmd.Parameters.Add(
                    "@CodigoFormato",
                    SqlDbType.NVarChar,
                    30).Value =
                    codigoFormato;

                cmd.Parameters.Add(
                    "@VersionFormato",
                    SqlDbType.NVarChar,
                    20).Value =
                    versionFormato;

                cmd.Parameters.Add(
                    "@TipoChecklist",
                    SqlDbType.NVarChar,
                    50).Value =
                    tipoChecklist;

                cmd.Parameters.Add(
                    "@FechaOperacion",
                    SqlDbType.Date).Value =
                    fechaOperacion.Date;

                cmd.Parameters.Add(
                    "@TurnoID",
                    SqlDbType.Int).Value =
                    (object?)turnoId
                    ?? DBNull.Value;

                cmd.Parameters.Add(
                    "@EsRecurrente",
                    SqlDbType.Bit).Value =
                    esRecurrente;

                cmd.Parameters.Add(
                    "@NumeroAplicacion",
                    SqlDbType.Int).Value =
                    numeroAplicacion;

                var resultado =
                    await cmd.ExecuteScalarAsync();

                if (resultado != null &&
                    resultado != DBNull.Value)
                {
                    checklistExistenteId =
                        Convert.ToInt32(resultado);
                }
            }

            if (checklistExistenteId.HasValue)
            {
                /*
                 * Si se agregaron preguntas nuevas al catálogo,
                 * se incorporan al checklist existente.
                 */
                await CrearDetalleChecklistFormatoAsync(
                    checklistExistenteId.Value,
                    codigoFormato,
                    versionFormato,
                    usuarioId,
                    cn,
                    tx);

                return checklistExistenteId.Value;
            }

            const string sqlInsert = @"
INSERT INTO dbo.Produccion_ChecklistArranque
(
    EjecucionProduccionID,
    ProgramaProduccionID,
    SolicitudProduccionID,
    SolicitudProduccionDetalleID,
    ReleaseID,
    ReleaseDetalleID,

    FechaChecklist,

    MaquinaID,
    MaquinaCodigo,
    MaquinaNombre,

    MoldeID,
    MoldeCodigo,

    ParteID,
    NumeroParte,
    ReferenciaSAP,
    DescripcionParte,

    CodigoFormato,
    VersionFormato,

    TipoChecklist,
    MomentoProceso,
    FechaOperacion,

    TurnoID,
    TurnoNombre,

    NumeroAplicacion,
    EsRecurrente,
    RequiereCambioMolde,

    EstatusID,

    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
OUTPUT INSERTED.ChecklistArranqueID
VALUES
(
    @EjecucionProduccionID,
    @ProgramaProduccionID,
    @SolicitudProduccionID,
    @SolicitudProduccionDetalleID,
    @ReleaseID,
    @ReleaseDetalleID,

    GETDATE(),

    @MaquinaID,
    @MaquinaCodigo,
    @MaquinaNombre,

    @MoldeID,
    @MoldeCodigo,

    @ParteID,
    @NumeroParte,
    @ReferenciaSAP,
    @DescripcionParte,

    @CodigoFormato,
    @VersionFormato,

    @TipoChecklist,
    @MomentoProceso,
    @FechaOperacion,

    @TurnoID,
    @TurnoNombre,

    @NumeroAplicacion,
    @EsRecurrente,
    @RequiereCambioMolde,

    @EstatusID,

    @UsuarioID,
    GETDATE(),
    1
);";

            int checklistArranqueId;

            await using (
                var cmd = new SqlCommand(
                    sqlInsert,
                    cn,
                    tx))
            {
                cmd.Parameters.Add( "@EjecucionProduccionID",  SqlDbType.Int).Value = ejecucion.EjecucionProduccionID;

                cmd.Parameters.Add( "@ProgramaProduccionID", SqlDbType.Int).Value = ejecucion.ProgramaProduccionID;

                cmd.Parameters.Add(  "@SolicitudProduccionID",  SqlDbType.Int).Value =  ejecucion.SolicitudProduccionID.Value;

                cmd.Parameters.Add(  "@SolicitudProduccionDetalleID",  SqlDbType.Int).Value =  (object?)ejecucion.SolicitudProduccionDetalleID ?? DBNull.Value;

                cmd.Parameters.Add(  "@ReleaseID",  SqlDbType.Int).Value =   (object?)ejecucion.ReleaseID   ?? DBNull.Value;

                cmd.Parameters.Add(   "@ReleaseDetalleID",  SqlDbType.Int).Value =    (object?)ejecucion.ReleaseDetalleID   ?? DBNull.Value;

                cmd.Parameters.Add(  "@MaquinaID",  SqlDbType.Int).Value =   (object?)ejecucion.MaquinaID  ?? DBNull.Value;

                cmd.Parameters.Add( "@MaquinaCodigo",  SqlDbType.NVarChar, 100).Value =     string.IsNullOrWhiteSpace(     ejecucion.MaquinaCodigo)  ? DBNull.Value  : ejecucion.MaquinaCodigo.Trim();

                cmd.Parameters.Add( "@MaquinaNombre", SqlDbType.NVarChar,  200).Value = string.IsNullOrWhiteSpace(  ejecucion.MaquinaNombre)  ? DBNull.Value   : ejecucion.MaquinaNombre.Trim();

                cmd.Parameters.Add( "@MoldeID", SqlDbType.Int).Value = (object?)ejecucion.MoldeID ?? DBNull.Value;

                cmd.Parameters.Add(
                    "@MoldeCodigo",
                    SqlDbType.NVarChar,
                    100).Value =
                    string.IsNullOrWhiteSpace(
                        ejecucion.MoldeCodigo)
                        ? DBNull.Value
                        : ejecucion.MoldeCodigo.Trim();

                cmd.Parameters.Add(
                    "@ParteID",
                    SqlDbType.Int).Value =
                    (object?)ejecucion.ParteID
                    ?? DBNull.Value;

                cmd.Parameters.Add(
                    "@NumeroParte",
                    SqlDbType.NVarChar,
                    120).Value =
                    string.IsNullOrWhiteSpace(
                        ejecucion.NumeroParte)
                        ? DBNull.Value
                        : ejecucion.NumeroParte.Trim();

                cmd.Parameters.Add(
                    "@ReferenciaSAP",
                    SqlDbType.NVarChar,
                    150).Value =
                    string.IsNullOrWhiteSpace(
                        ejecucion.ReferenciaSAP)
                        ? DBNull.Value
                        : ejecucion.ReferenciaSAP.Trim();

                cmd.Parameters.Add(
                    "@DescripcionParte",
                    SqlDbType.NVarChar,
                    300).Value =
                    string.IsNullOrWhiteSpace(
                        ejecucion.DescripcionParte)
                        ? DBNull.Value
                        : ejecucion.DescripcionParte.Trim();

                cmd.Parameters.Add(
                    "@CodigoFormato",
                    SqlDbType.NVarChar,
                    30).Value =
                    codigoFormato;

                cmd.Parameters.Add(
                    "@VersionFormato",
                    SqlDbType.NVarChar,
                    20).Value =
                    versionFormato;

                cmd.Parameters.Add(
                    "@TipoChecklist",
                    SqlDbType.NVarChar,
                    50).Value =
                    tipoChecklist;

                cmd.Parameters.Add(
                    "@MomentoProceso",
                    SqlDbType.NVarChar,
                    40).Value =
                    momentoProceso;

                cmd.Parameters.Add(
                    "@FechaOperacion",
                    SqlDbType.Date).Value =
                    fechaOperacion.Date;

                cmd.Parameters.Add(
                    "@TurnoID",
                    SqlDbType.Int).Value =
                    (object?)turnoId
                    ?? DBNull.Value;

                cmd.Parameters.Add(
                    "@TurnoNombre",
                    SqlDbType.NVarChar,
                    50).Value =
                    string.IsNullOrWhiteSpace(turnoNombre)
                        ? DBNull.Value
                        : turnoNombre.Trim();

                cmd.Parameters.Add(
                    "@NumeroAplicacion",
                    SqlDbType.Int).Value =
                    numeroAplicacion;

                cmd.Parameters.Add(
                    "@EsRecurrente",
                    SqlDbType.Bit).Value =
                    esRecurrente;

                cmd.Parameters.Add(
                    "@RequiereCambioMolde",
                    SqlDbType.Bit).Value =
                    requiereCambioMolde;

                cmd.Parameters.Add(
                    "@EstatusID",
                    SqlDbType.Int).Value =
                    ProduccionChecklistEstatus
                        .PendienteProduccion;

                cmd.Parameters.Add(
                    "@UsuarioID",
                    SqlDbType.Int).Value =
                    usuarioId;

                var resultado =
                    await cmd.ExecuteScalarAsync();

                if (resultado == null ||
                    resultado == DBNull.Value)
                {
                    throw new InvalidOperationException(
                        "No fue posible obtener el ID del checklist.");
                }

                checklistArranqueId =
                    Convert.ToInt32(resultado);
            }

            await CrearDetalleChecklistFormatoAsync(
                checklistArranqueId,
                codigoFormato,
                versionFormato,
                usuarioId,
                cn,
                tx);

            return checklistArranqueId;
        }


        private async Task CrearDetalleChecklistFormatoAsync(
       int checklistArranqueId,
       string codigoFormato,
       string versionFormato,
       int usuarioId,
       SqlConnection cn,
       SqlTransaction tx)
        {
            const string sql = @"
INSERT INTO dbo.Produccion_ChecklistArranqueDetalle
(
    ChecklistArranqueID,
    PreguntaID,
    Resultado,
    Confirmado,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
SELECT
    @ChecklistArranqueID,
    p.PreguntaID,

    CASE
        /* =========================================================
           PREGUNTAS DE CALIDAD / AUDITOR
           NUNCA deben nacer respondidas.
           ========================================================= */
        WHEN ISNULL(p.EsPreguntaCalidad,0)=1
          OR UPPER(LTRIM(RTRIM(ISNULL(p.GrupoResponsable,N'')))) IN
             (
                 N'CALIDAD',
                 N'AUDITOR',
                 N'AUDITOR DE CALIDAD'
             )
          OR UPPER(ISNULL(p.Seccion,N'')) LIKE N'%CALIDAD%'
          OR UPPER(ISNULL(p.Seccion,N'')) LIKE N'%AUDITOR%'
          OR UPPER(ISNULL(p.ResponsableSugerido,N'')) LIKE N'%CALIDAD%'
          OR UPPER(ISNULL(p.ResponsableSugerido,N'')) LIKE N'%AUDITOR%'
            THEN NULL

        /* =========================================================
           PREGUNTAS DE PRODUCCION
           Conservan el estado predeterminado para la interfaz,
           pero siguen sin estar confirmadas.
           ========================================================= */
        WHEN NULLIF(
            LTRIM(RTRIM(ISNULL(p.EstadoPredeterminado,N''))),
            N''
        ) IS NULL
            THEN N'OK'

        ELSE UPPER(
            LTRIM(RTRIM(p.EstadoPredeterminado))
        )
    END AS Resultado,

    0 AS Confirmado,

    @UsuarioID,
    GETDATE(),
    1

FROM dbo.ERP_ChecklistArranquePreguntas p

WHERE p.CodigoFormato=@CodigoFormato
  AND ISNULL(p.VersionFormato,N'')=ISNULL(@VersionFormato,N'')
  AND p.Activo=1

  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.Produccion_ChecklistArranqueDetalle d
      WHERE d.ChecklistArranqueID=@ChecklistArranqueID
        AND d.PreguntaID=p.PreguntaID
        AND d.Activo=1
  )

ORDER BY
    ISNULL(p.OrdenSeccion,0),
    ISNULL(p.OrdenPregunta,0),
    p.PreguntaID;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add(
                "@ChecklistArranqueID",
                SqlDbType.Int).Value =
                checklistArranqueId;

            cmd.Parameters.Add(
                "@CodigoFormato",
                SqlDbType.NVarChar,
                30).Value =
                codigoFormato.Trim();

            cmd.Parameters.Add(
                "@VersionFormato",
                SqlDbType.NVarChar,
                20).Value =
                versionFormato.Trim();

            cmd.Parameters.Add(
                "@UsuarioID",
                SqlDbType.Int).Value =
                usuarioId;

            await cmd.ExecuteNonQueryAsync();
        }

        private async Task<int>
    ObtenerOCrearChecklistPerifericosTurnoAsync(
        ProduccionEjecucionVm ejecucion,
        DateTime fechaHora,
        int usuarioId,
        SqlConnection cn,
        SqlTransaction tx)
        {
            var turno =
                ObtenerTurnoProduccion(fechaHora);

            return await ObtenerOCrearChecklistFormatoAsync(
                ejecucion: ejecucion,
                codigoFormato: "GQ-F-PR01-14",
                versionFormato: "Ver.01",
                tipoChecklist: "MONITOREO_PERIFERICOS",
                momentoProceso: "CAMBIO_TURNO",
                fechaOperacion: turno.FechaOperacion,
                turnoId: turno.TurnoID,
                turnoNombre: turno.TurnoNombre,
                esRecurrente: true,
                requiereCambioMolde: ejecucion.EsCambioMolde,
                numeroAplicacion: 1,
                usuarioId: usuarioId,
                cn: cn,
                tx: tx);
        }

        private static ProduccionTurnoActual
    ObtenerTurnoProduccion(
        DateTime fechaHora)
        {
            var hora = fechaHora.TimeOfDay;

            /*
             * Matutino:
             * 07:00:00 a 14:59:59
             */
            if (hora >= new TimeSpan(7, 0, 0) &&
                hora < new TimeSpan(15, 0, 0))
            {
                return new ProduccionTurnoActual
                {
                    TurnoID = 1,
                    TurnoNombre = "Matutino",
                    FechaOperacion = fechaHora.Date,
                    FechaInicio =
                        fechaHora.Date.AddHours(7),
                    FechaFin =
                        fechaHora.Date.AddHours(15)
                };
            }

            /*
             * Vespertino:
             * 15:00:00 a 22:59:59
             */
            if (hora >= new TimeSpan(15, 0, 0) &&
                hora < new TimeSpan(23, 0, 0))
            {
                return new ProduccionTurnoActual
                {
                    TurnoID = 2,
                    TurnoNombre = "Vespertino",
                    FechaOperacion = fechaHora.Date,
                    FechaInicio =
                        fechaHora.Date.AddHours(15),
                    FechaFin =
                        fechaHora.Date.AddHours(23)
                };
            }

            /*
             * Nocturno:
             * 23:00 a 07:00.
             *
             * Entre 00:00 y 06:59 la fecha operativa
             * corresponde al día anterior.
             */
            var fechaOperacion =
                hora < new TimeSpan(7, 0, 0)
                    ? fechaHora.Date.AddDays(-1)
                    : fechaHora.Date;

            return new ProduccionTurnoActual
            {
                TurnoID = 3,
                TurnoNombre = "Nocturno",
                FechaOperacion = fechaOperacion,
                FechaInicio =
                    fechaOperacion.AddHours(23),
                FechaFin =
                    fechaOperacion
                        .AddDays(1)
                        .AddHours(7)
            };
        }

        private async Task<ProduccionCambioTurnoTecnicoVm?>
     ConstruirCambioTurnoTecnicoAsync(
         ProduccionEjecucionVm ejecucion,
         SqlConnection cn,
         SqlTransaction? tx = null)
        {
            if (ejecucion == null)
                return null;

            if (ejecucion.EjecucionProduccionID <= 0)
                return null;

            if (ejecucion.EstatusID != ProduccionEstatus.EnPreparacion &&
                ejecucion.EstatusID != ProduccionEstatus.EnProduccion &&
                ejecucion.EstatusID != ProduccionEstatus.Pausado)
            {
                return null;
            }

            var ahora = DateTime.Now;

            var vm = new ProduccionCambioTurnoTecnicoVm
            {
                EjecucionProduccionID =
                    ejecucion.EjecucionProduccionID,

                ProgramaProduccionID =
                    ejecucion.ProgramaProduccionID,

                MaquinaID =
                    ejecucion.MaquinaID,

                MaquinaCodigo =
                    ejecucion.MaquinaCodigo,

                MaquinaNombre =
                    ejecucion.MaquinaNombre,

                ParteID =
                    ejecucion.ParteID,

                NumeroParte =
                    ejecucion.NumeroParte,

                ReferenciaSAP =
                    ejecucion.ReferenciaSAP,

                OperadorActualID =
                    ejecucion.OperadorID,

                OperadorActualNombre =
                    ejecucion.OperadorNombre
            };

            /*
             * Sugerencia vigente registrada por el técnico.
             */
            vm.SugerenciaActual =
                await ObtenerSugerenciaTecnicoCambioTurnoAsync(
                    ejecucion.EjecucionProduccionID,
                    cn,
                    tx);

            /*
             * Resolver la ParteID que realmente utiliza
             * la matriz de polivalencia.
             */
            var partePolivalencia =
                await ResolverPartePolivalenciaProgramaAsync(
                    ejecucion.ProgramaProduccionID,
                    ejecucion.ParteID,
                    cn,
                    tx);

            /*
             * La fuente de verdad de Polivalencia es:
             *
             * dbo.vw_RRHH_PolivalenciaOperadoresParte
             *
             * NO:
             * dbo.RRHH_MatrizPolivalencia
             */
            vm.TieneMatrizPolivalencia =
                partePolivalencia.HasValue &&
                partePolivalencia.Value > 0 &&
                await ParteTienePolivalenciaProduccionAsync(
                    partePolivalencia.Value,
                    cn,
                    tx);

            /*
             * Buscar la escala publicada correspondiente
             * a la máquina y fecha/hora actuales.
             */
            const string sqlEscala = @"
SELECT TOP (1)
    e.EscalaID,
    e.Folio
FROM dbo.RRHH_EscalasPersonal e
WHERE e.Activo = 1
  AND e.Estado = N'Publicada'
  AND EXISTS
  (
      SELECT 1
      FROM dbo.RRHH_EscalaAsignaciones a
      WHERE a.EscalaID = e.EscalaID
        AND a.Activo = 1
        AND
        (
            @MaquinaID IS NULL
            OR a.MaquinaID = @MaquinaID
        )
        AND CAST(@Ahora AS date)
            BETWEEN CAST(a.FechaInicio AS date)
                AND CAST(a.FechaFin AS date)
  )
ORDER BY
    e.EscalaID DESC;";

            int? escalaId = null;

            await using (
                var cmd =
                    tx == null
                        ? new SqlCommand(
                            sqlEscala,
                            cn)
                        : new SqlCommand(
                            sqlEscala,
                            cn,
                            tx))
            {
                cmd.Parameters.Add(
                    "@MaquinaID",
                    SqlDbType.Int).Value =
                    ejecucion.MaquinaID.HasValue &&
                    ejecucion.MaquinaID.Value > 0
                        ? ejecucion.MaquinaID.Value
                        : DBNull.Value;

                cmd.Parameters.Add(
                    "@Ahora",
                    SqlDbType.DateTime2).Value =
                    ahora;

                await using var rd =
                    await cmd.ExecuteReaderAsync();

                if (await rd.ReadAsync())
                {
                    escalaId =
                        rd["EscalaID"] == DBNull.Value
                            ? null
                            : Convert.ToInt32(
                                rd["EscalaID"]);

                    vm.EscalaFolio =
                        rd["Folio"] == DBNull.Value
                            ? null
                            : rd["Folio"]
                                ?.ToString()
                                ?.Trim();

                    vm.EscalaEncontrada =
                        escalaId.HasValue;
                }
            }

            /*
             * Candidatos para el cambio de turno.
             *
             * IMPORTANTE:
             * La polivalencia se obtiene de la VISTA real:
             *
             * dbo.vw_RRHH_PolivalenciaOperadoresParte
             *
             * La vista utiliza PersonalID.
             */
            const string sqlOperadores = @"
SELECT
    p.PersonaID,

    LTRIM(
        RTRIM(
            CONCAT(
                ISNULL(p.Nombre,N''),
                N' ',
                ISNULL(p.ApellidoPaterno,N''),
                N' ',
                ISNULL(p.ApellidoMaterno,N'')
            )
        )
    ) AS Nombre,

    pol.Nivel,

    escala.TurnoID,
    escala.TurnoNombre,
    escala.HoraInicio,

    CAST(
        CASE
            WHEN escala.PersonaID IS NOT NULL
                THEN 1
            ELSE 0
        END
        AS BIT
    ) AS EnEscala,

    escala.MinutosParaInicio

FROM dbo.Persona p

/* ============================================================
   POLIVALENCIA
   ============================================================ */
OUTER APPLY
(
    SELECT TOP (1)

        CONVERT(
            INT,
            v.Nivel
        ) AS Nivel

    FROM dbo.vw_RRHH_PolivalenciaOperadoresParte v

    WHERE v.PersonalID =
          p.PersonaID

      AND
      (
          @ParteID IS NULL
          OR v.ParteID =
             @ParteID
      )

    ORDER BY
        CONVERT(INT, v.Nivel) DESC
) pol

/* ============================================================
   ESCALA
   ============================================================ */
OUTER APPLY
(
    SELECT TOP (1)

        a.PersonalID
            AS PersonaID,

        et.EscalaTurnoID
            AS TurnoID,

        et.Nombre
            AS TurnoNombre,

        et.HoraInicio,

        CASE
            WHEN et.HoraInicio IS NULL
                THEN NULL

            WHEN DATEDIFF(
                    MINUTE,
                    CONVERT(time,@Ahora),
                    et.HoraInicio
                 ) >= 0
                THEN DATEDIFF(
                    MINUTE,
                    CONVERT(time,@Ahora),
                    et.HoraInicio
                )

            ELSE
                DATEDIFF(
                    MINUTE,
                    CONVERT(time,@Ahora),
                    et.HoraInicio
                ) + 1440
        END AS MinutosParaInicio

    FROM dbo.RRHH_EscalaAsignaciones a

    INNER JOIN dbo.RRHH_EscalaTurnos et
        ON et.EscalaID =
           a.EscalaID
       AND et.EscalaTurnoID =
           a.EscalaTurnoID

    WHERE a.PersonalID =
          p.PersonaID

      AND a.Activo = 1

      AND
      (
          @EscalaID IS NULL
          OR a.EscalaID =
             @EscalaID
      )

      AND
      (
          @MaquinaID IS NULL
          OR a.MaquinaID =
             @MaquinaID
      )

      AND CAST(@Ahora AS date)
          BETWEEN CAST(a.FechaInicio AS date)
              AND CAST(a.FechaFin AS date)

    ORDER BY

        CASE
            WHEN et.HoraInicio IS NULL
                THEN 1
            ELSE 0
        END,

        CASE
            WHEN DATEDIFF(
                    MINUTE,
                    CONVERT(time,@Ahora),
                    et.HoraInicio
                 ) > 0
                THEN DATEDIFF(
                    MINUTE,
                    CONVERT(time,@Ahora),
                    et.HoraInicio
                )

            ELSE
                DATEDIFF(
                    MINUTE,
                    CONVERT(time,@Ahora),
                    et.HoraInicio
                ) + 1440
        END,

        a.AsignacionID DESC
) escala

WHERE ISNULL(
        p.EsColaboradorActivo,
        1
      ) = 1

  /*
   * El operador actual no debe aparecer
   * como candidato para recibir su propio turno.
   */
  AND
  (
      @OperadorActualID IS NULL
      OR p.PersonaID <>
         @OperadorActualID
  )

  /*
   * Debe ser operador por puesto o
   * tener función OPERADOR en alguna escala.
   */
  AND
  (
      UPPER(
          LTRIM(
              RTRIM(
                  ISNULL(
                      p.Puesto,
                      N''
                  )
              )
          )
      ) = N'OPERADOR'

      OR EXISTS
      (
          SELECT 1

          FROM dbo.RRHH_EscalaAsignaciones ah

          INNER JOIN dbo.RRHH_FuncionesPersonal fh
              ON fh.FuncionID =
                 ah.FuncionID
             AND fh.Activo = 1

          WHERE ah.PersonalID =
                p.PersonaID

            AND ah.Activo = 1

            AND UPPER(
                    LTRIM(
                        RTRIM(
                            ISNULL(
                                fh.Nombre,
                                N''
                            )
                        )
                    )
                ) = N'OPERADOR'
      )
  )

  /*
   * Si la pieza tiene matriz,
   * el candidato debe existir en la vista
   * con nivel N1-N4.
   */
  AND
  (
      @TieneMatriz = 0

      OR EXISTS
      (
          SELECT 1

          FROM dbo.vw_RRHH_PolivalenciaOperadoresParte v2

          WHERE v2.PersonalID =
                p.PersonaID

            AND v2.ParteID =
                @ParteID

            AND TRY_CONVERT(
                    INT,
                    v2.Nivel
                ) BETWEEN 1 AND 4
      )
  )

ORDER BY

    EnEscala DESC,

    CASE
        WHEN MinutosParaInicio IS NULL
            THEN 999999
        ELSE MinutosParaInicio
    END,

    Nombre;";

            await using (
                var cmd =
                    tx == null
                        ? new SqlCommand(
                            sqlOperadores,
                            cn)
                        : new SqlCommand(
                            sqlOperadores,
                            cn,
                            tx))
            {
                cmd.Parameters.Add(
                    "@ParteID",
                    SqlDbType.Int).Value =
                    partePolivalencia.HasValue &&
                    partePolivalencia.Value > 0
                        ? partePolivalencia.Value
                        : DBNull.Value;

                cmd.Parameters.Add(
                    "@MaquinaID",
                    SqlDbType.Int).Value =
                    ejecucion.MaquinaID.HasValue &&
                    ejecucion.MaquinaID.Value > 0
                        ? ejecucion.MaquinaID.Value
                        : DBNull.Value;

                cmd.Parameters.Add(
                    "@OperadorActualID",
                    SqlDbType.Int).Value =
                    ejecucion.OperadorID.HasValue &&
                    ejecucion.OperadorID.Value > 0
                        ? ejecucion.OperadorID.Value
                        : DBNull.Value;

                cmd.Parameters.Add(
                    "@EscalaID",
                    SqlDbType.Int).Value =
                    escalaId.HasValue
                        ? escalaId.Value
                        : DBNull.Value;

                cmd.Parameters.Add(
                    "@Ahora",
                    SqlDbType.DateTime2).Value =
                    ahora;

                cmd.Parameters.Add(
                    "@TieneMatriz",
                    SqlDbType.Bit).Value =
                    vm.TieneMatrizPolivalencia;

                await using var rd =
                    await cmd.ExecuteReaderAsync();

                while (await rd.ReadAsync())
                {
                    vm.Operadores.Add(
                        new ProduccionCambioTurnoCandidatoVm
                        {
                            PersonaID =
                                Convert.ToInt32(
                                    rd["PersonaID"]),

                            Nombre =
                                rd["Nombre"]
                                    ?.ToString()
                                    ?.Trim()
                                ?? string.Empty,

                            Nivel =
                                rd["Nivel"] == DBNull.Value
                                    ? null
                                    : Convert.ToInt32(
                                        rd["Nivel"]),

                            TurnoID =
                                rd["TurnoID"] == DBNull.Value
                                    ? null
                                    : Convert.ToInt32(
                                        rd["TurnoID"]),

                            TurnoNombre =
                                rd["TurnoNombre"] == DBNull.Value
                                    ? null
                                    : rd["TurnoNombre"]
                                        ?.ToString()
                                        ?.Trim(),

                            HoraInicioTurno =
                                rd["HoraInicio"] == DBNull.Value
                                    ? null
                                    : (TimeSpan?)rd[
                                        "HoraInicio"],

                            EnEscala =
                                rd["EnEscala"] != DBNull.Value &&
                                Convert.ToBoolean(
                                    rd["EnEscala"]),

                            MinutosParaInicio =
                                rd["MinutosParaInicio"] == DBNull.Value
                                    ? null
                                    : Convert.ToInt32(
                                        rd["MinutosParaInicio"])
                        });
                }
            }

            /*
             * Si ya existe sugerencia del técnico,
             * se marca dentro de la lista.
             */
            if (vm.SugerenciaActual != null)
            {
                var sugerido =
                    vm.Operadores.FirstOrDefault(
                        x =>
                            x.PersonaID ==
                            vm.SugerenciaActual
                                .OperadorSugeridoID);

                if (sugerido != null)
                {
                    sugerido.EsSugerido = true;

                    vm.SugerenciaActual.TurnoNombre =
                        sugerido.TurnoNombre;

                    vm.SugerenciaActual
                        .NivelPolivalencia =
                        sugerido.Nivel;

                    vm.SugerenciaActual.EnEscala =
                        sugerido.EnEscala;
                }
            }

            return vm;
        }
        private async Task<ProduccionCambioTurnoSugerenciaVm?> ObtenerSugerenciaTecnicoCambioTurnoAsync(int ejecucionProduccionId, SqlConnection cn, SqlTransaction? tx = null)
        {
            if (ejecucionProduccionId <= 0) return null;
            const string sql = @"
SELECT TOP(1)
    s.CambioTurnoSugerenciaID,
    s.EjecucionProduccionID,
    s.ProgramaProduccionID,
    s.OperadorSugeridoID,
    LTRIM(RTRIM(CONCAT(ISNULL(op.Nombre,N''),N' ',ISNULL(op.ApellidoPaterno,N''),N' ',ISNULL(op.ApellidoMaterno,N'')))) AS OperadorSugeridoNombre,
    s.UsuarioTecnicoID,
    s.FechaSugerencia,
    s.Observaciones,
    ISNULL(s.Utilizada,0) AS Utilizada,
    ISNULL(s.Activo,1) AS Activo,
    s.UsuarioModificacionID,
    s.FechaModificacion
FROM dbo.Produccion_CambioTurnoSugerencias s
INNER JOIN dbo.Persona op ON op.PersonaID=s.OperadorSugeridoID
WHERE s.EjecucionProduccionID=@EjecucionProduccionID
  AND s.Activo=1
  AND ISNULL(s.Utilizada,0)=0
ORDER BY s.FechaSugerencia DESC,s.CambioTurnoSugerenciaID DESC;";
            ProduccionCambioTurnoSugerenciaVm? vm = null;
            int usuarioTecnicoId = 0;
            await using (var cmd = tx == null ? new SqlCommand(sql, cn) : new SqlCommand(sql, cn, tx))
            {
                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                await using (var rd = await cmd.ExecuteReaderAsync())
                {
                    if (!await rd.ReadAsync()) return null;
                    usuarioTecnicoId = Convert.ToInt32(rd["UsuarioTecnicoID"]);
                    vm = new ProduccionCambioTurnoSugerenciaVm
                    {
                        CambioTurnoSugerenciaID = Convert.ToInt32(rd["CambioTurnoSugerenciaID"]),
                        EjecucionProduccionID = Convert.ToInt32(rd["EjecucionProduccionID"]),
                        ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),
                        OperadorSugeridoID = Convert.ToInt32(rd["OperadorSugeridoID"]),
                        OperadorSugeridoNombre = rd["OperadorSugeridoNombre"]?.ToString()?.Trim() ?? string.Empty,
                        UsuarioTecnicoID = usuarioTecnicoId,
                        FechaSugerencia = Convert.ToDateTime(rd["FechaSugerencia"]),
                        Observaciones = rd["Observaciones"] == DBNull.Value ? null : rd["Observaciones"]?.ToString(),
                        Utilizada = Convert.ToBoolean(rd["Utilizada"]),
                        Activo = Convert.ToBoolean(rd["Activo"]),
                        UsuarioModificacionID = rd["UsuarioModificacionID"] == DBNull.Value ? null : Convert.ToInt32(rd["UsuarioModificacionID"]),
                        FechaModificacion = rd["FechaModificacion"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaModificacion"])
                    };
                }
            }
            vm.TecnicoNombre = await ObtenerPersonaNombreAsync(usuarioTecnicoId, cn, tx) ?? $"Usuario {usuarioTecnicoId}";
            return vm;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarSugerenciaCambioTurno(ProduccionCambioTurnoSugerenciaPostVm vm)
        {
            if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");
            if (vm.EjecucionProduccionID <= 0)
            {
                TempData["Error"] = "No se recibió correctamente la ejecución de producción.";
                return RedirectToAction(nameof(Index));
            }
            if (vm.OperadorSugeridoID <= 0)
            {
                TempData["Error"] = "Selecciona al operador que deseas sugerir para el siguiente turno.";
                return RedirectToAction(nameof(Detalle), new { id = vm.EjecucionProduccionID });
            }
            vm.Observaciones = string.IsNullOrWhiteSpace(vm.Observaciones) ? null : vm.Observaciones.Trim();
            if (vm.Observaciones?.Length > 500)
            {
                TempData["Error"] = "Las observaciones de la sugerencia no pueden superar 500 caracteres.";
                return RedirectToAction(nameof(Detalle), new { id = vm.EjecucionProduccionID });
            }
            var usuarioId = ObtenerUsuarioID();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var ejecucion = await ObtenerEjecucionAsync(vm.EjecucionProduccionID, cn, tx);
                if (ejecucion == null)
                {
                    await tx.RollbackAsync();
                    return NotFound();
                }
                if (ejecucion.EstatusID != ProduccionEstatus.EnPreparacion && ejecucion.EstatusID != ProduccionEstatus.EnProduccion && ejecucion.EstatusID != ProduccionEstatus.Pausado)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "Solo puedes registrar una sugerencia de cambio de turno mientras la ejecución esté activa.";
                    return RedirectToAction(nameof(Detalle), new { id = vm.EjecucionProduccionID });
                }
                if (ejecucion.OperadorID.HasValue && ejecucion.OperadorID.Value == vm.OperadorSugeridoID)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "El operador sugerido ya es el operador principal actual de la ejecución.";
                    return RedirectToAction(nameof(Detalle), new { id = vm.EjecucionProduccionID });
                }
                var tecnicoVm = await ConstruirCambioTurnoTecnicoAsync(ejecucion, cn, tx);
                if (tecnicoVm == null)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "No fue posible construir los candidatos del siguiente turno.";
                    return RedirectToAction(nameof(Detalle), new { id = vm.EjecucionProduccionID });
                }
                var candidato = tecnicoVm.Operadores.FirstOrDefault(x => x.PersonaID == vm.OperadorSugeridoID);
                if (candidato == null)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = tecnicoVm.TieneMatrizPolivalencia
                        ? "El operador seleccionado no tiene un nivel de polivalencia autorizado para esta pieza."
                        : "El operador seleccionado no está activo o no pertenece al catálogo válido de operadores.";
                    return RedirectToAction(nameof(Detalle), new { id = vm.EjecucionProduccionID });
                }
                const string sql = @"
UPDATE dbo.Produccion_CambioTurnoSugerencias
SET Activo=0,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=SYSDATETIME()
WHERE EjecucionProduccionID=@EjecucionProduccionID
  AND Activo=1
  AND ISNULL(Utilizada,0)=0;

INSERT INTO dbo.Produccion_CambioTurnoSugerencias
(
    EjecucionProduccionID,
    ProgramaProduccionID,
    OperadorSugeridoID,
    UsuarioTecnicoID,
    FechaSugerencia,
    Observaciones,
    Utilizada,
    Activo
)
VALUES
(
    @EjecucionProduccionID,
    @ProgramaProduccionID,
    @OperadorSugeridoID,
    @UsuarioID,
    SYSDATETIME(),
    @Observaciones,
    0,
    1
);

SELECT CAST(SCOPE_IDENTITY() AS INT);";
                int sugerenciaId;
                await using (var cmd = new SqlCommand(sql, cn, tx))
                {
                    cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucion.EjecucionProduccionID;
                    cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = ejecucion.ProgramaProduccionID;
                    cmd.Parameters.Add("@OperadorSugeridoID", SqlDbType.Int).Value = candidato.PersonaID;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 500).Value = string.IsNullOrWhiteSpace(vm.Observaciones) ? DBNull.Value : vm.Observaciones;
                    var result = await cmd.ExecuteScalarAsync();
                    if (result == null || result == DBNull.Value) throw new InvalidOperationException("No fue posible recuperar el identificador de la sugerencia.");
                    sugerenciaId = Convert.ToInt32(result);
                }
                await tx.CommitAsync();
                TempData["Success"] = $"Sugerencia guardada. {candidato.Nombre} quedó recomendado para el siguiente cambio de turno. El operador podrá modificar esta selección al realizar la entrega.";
                return RedirectToAction(nameof(Detalle), new { id = vm.EjecucionProduccionID });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] = "No fue posible guardar la sugerencia de cambio de turno: " + ex.Message;
                return RedirectToAction(nameof(Detalle), new { id = vm.EjecucionProduccionID });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelarSugerenciaCambioTurno(int ejecucionProduccionId)
        {
            if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");
            if (ejecucionProduccionId <= 0)
            {
                TempData["Error"] = "No se recibió correctamente la ejecución.";
                return RedirectToAction(nameof(Index));
            }
            var usuarioId = ObtenerUsuarioID();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                const string sql = @"
UPDATE dbo.Produccion_CambioTurnoSugerencias
SET Activo=0,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=SYSDATETIME()
WHERE EjecucionProduccionID=@EjecucionProduccionID
  AND Activo=1
  AND ISNULL(Utilizada,0)=0;";
                int afectados;
                await using (var cmd = new SqlCommand(sql, cn, tx))
                {
                    cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    afectados = await cmd.ExecuteNonQueryAsync();
                }
                await tx.CommitAsync();
                TempData[afectados > 0 ? "Success" : "Info"] = afectados > 0
                    ? "La sugerencia del técnico fue retirada. El operador podrá seleccionar al receptor del turno."
                    : "La ejecución no tenía una sugerencia activa.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] = "No fue posible cancelar la sugerencia: " + ex.Message;
            }
            return RedirectToAction(nameof(Detalle), new { id = ejecucionProduccionId });
        }

        [HttpGet]
        public async Task<IActionResult> Historial(
    string? busqueda,
    DateTime? fechaDesde,
    DateTime? fechaHasta)
        {
            if (!UsuarioEnSesion())
                return RedirectToAction("Login", "Login");

            busqueda = string.IsNullOrWhiteSpace(busqueda)
                ? null
                : busqueda.Trim();

            if (fechaDesde.HasValue &&
                fechaHasta.HasValue &&
                fechaDesde.Value.Date > fechaHasta.Value.Date)
            {
                var temporal = fechaDesde;
                fechaDesde = fechaHasta;
                fechaHasta = temporal;
            }

            await using var cn =
                new SqlConnection(ConnectionString);

            await cn.OpenAsync();

            var vm = new ProduccionHistorialVm
            {
                Busqueda = busqueda,
                FechaDesde = fechaDesde,
                FechaHasta = fechaHasta,
                EsVistaOperador = false
            };

            vm.Producciones =
                await ObtenerHistorialProduccionAsync(
                    busqueda,
                    fechaDesde,
                    fechaHasta,
                    cn);

            return View(vm);
        }

        private async Task<List<ProduccionHistorialEjecucionVm>>
    ObtenerHistorialProduccionAsync(
        string? busqueda,
        DateTime? fechaDesde,
        DateTime? fechaHasta,
        SqlConnection cn,
        SqlTransaction? tx = null)
        {
            var resultado =
                new List<ProduccionHistorialEjecucionVm>();

            const string sql = @"
SELECT
    e.EjecucionProduccionID,
    e.ProgramaProduccionID,
    e.SolicitudProduccionID,

    e.MaquinaID,
    e.MaquinaCodigo,
    e.MaquinaNombre,

    e.ParteID,
    e.NumeroParte,
    e.ReferenciaSAP,
    e.DescripcionParte,

    e.FechaInicioReal,
    e.FechaFinReal,

    ISNULL(e.CantidadPlaneada,0) AS CantidadPlaneada,
    ISNULL(e.CantidadOKTotal,0) AS CantidadOKTotal,
    ISNULL(e.CantidadSospechosaTotal,0) AS CantidadSospechosaTotal,
    ISNULL(e.CantidadScrapTotal,0) AS CantidadScrapTotal,

    e.EstatusID,
    e.OperadorNombre,

    ISNULL(h.HorasCapturadas,0) AS HorasCapturadas,
    ISNULL(h.ObjetivoAcumulado,0) AS ObjetivoAcumulado,

    CAST(
        CASE
            WHEN ISNULL(h.ObjetivoAcumulado,0) <= 0
                THEN 0
            ELSE
                ISNULL(e.CantidadOKTotal,0) * 100.0
                / h.ObjetivoAcumulado
        END
        AS DECIMAL(18,2)
    ) AS PorcentajeCumplimiento,

    ISNULL(ct.TotalCambiosTurno,0) AS TotalCambiosTurno,
    ISNULL(pa.TotalParos,0) AS TotalParos

FROM dbo.Produccion_Ejecucion e

OUTER APPLY
(
    SELECT
        COUNT(1) AS HorasCapturadas,

        SUM(
            ISNULL(
                NULLIF(rh.ObjetivoBloque,0),
                ISNULL(rh.ObjetivoHora,0)
            )
        ) AS ObjetivoAcumulado

    FROM dbo.Produccion_RegistroHora rh
    WHERE rh.EjecucionProduccionID =
          e.EjecucionProduccionID
      AND rh.Activo = 1
) h

OUTER APPLY
(
    SELECT
        COUNT(1) AS TotalCambiosTurno
    FROM dbo.Produccion_CambiosTurno ct
    WHERE ct.EjecucionProduccionID =
          e.EjecucionProduccionID
      AND ct.Activo = 1
) ct

OUTER APPLY
(
    SELECT
        COUNT(1) AS TotalParos
    FROM dbo.Produccion_Paros p
    WHERE p.EjecucionProduccionID =
          e.EjecucionProduccionID
      AND p.Activo = 1
) pa

WHERE e.Activo = 1

  AND e.EstatusID IN
  (
      @TerminadoParcial,
      @Terminado,
      @ListaCierreDocumental,
      @Cerrado
  )

  AND
  (
      @FechaDesde IS NULL
      OR CAST(
            ISNULL(
                e.FechaFinReal,
                e.FechaModificacion
            ) AS DATE
         ) >= @FechaDesde
  )

  AND
  (
      @FechaHasta IS NULL
      OR CAST(
            ISNULL(
                e.FechaFinReal,
                e.FechaModificacion
            ) AS DATE
         ) <= @FechaHasta
  )

  AND
  (
      @Busqueda IS NULL

      OR e.MaquinaCodigo LIKE '%' + @Busqueda + '%'
      OR e.MaquinaNombre LIKE '%' + @Busqueda + '%'
      OR e.NumeroParte LIKE '%' + @Busqueda + '%'
      OR e.ReferenciaSAP LIKE '%' + @Busqueda + '%'
      OR e.DescripcionParte LIKE '%' + @Busqueda + '%'
      OR e.OperadorNombre LIKE '%' + @Busqueda + '%'

      OR CONVERT(
            NVARCHAR(30),
            e.ProgramaProduccionID
         ) LIKE '%' + @Busqueda + '%'

      OR CONVERT(
            NVARCHAR(30),
            e.EjecucionProduccionID
         ) LIKE '%' + @Busqueda + '%'
  )

ORDER BY
    ISNULL(
        e.FechaFinReal,
        e.FechaModificacion
    ) DESC,
    e.EjecucionProduccionID DESC;";

            await using var cmd =
                tx == null
                    ? new SqlCommand(sql, cn)
                    : new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add(
                "@TerminadoParcial",
                SqlDbType.Int).Value =
                ProduccionEstatus.TerminadoParcial;

            cmd.Parameters.Add(
                "@Terminado",
                SqlDbType.Int).Value =
                ProduccionEstatus.Terminado;

            cmd.Parameters.Add(
                "@ListaCierreDocumental",
                SqlDbType.Int).Value =
                ProduccionEstatus.ListaCierreDocumental;

            cmd.Parameters.Add(
                "@Cerrado",
                SqlDbType.Int).Value =
                ProduccionEstatus.Cerrado;

            cmd.Parameters.Add(
                "@FechaDesde",
                SqlDbType.Date).Value =
                fechaDesde.HasValue
                    ? fechaDesde.Value.Date
                    : DBNull.Value;

            cmd.Parameters.Add(
                "@FechaHasta",
                SqlDbType.Date).Value =
                fechaHasta.HasValue
                    ? fechaHasta.Value.Date
                    : DBNull.Value;

            cmd.Parameters.Add(
                "@Busqueda",
                SqlDbType.NVarChar,
                200).Value =
                string.IsNullOrWhiteSpace(busqueda)
                    ? DBNull.Value
                    : busqueda.Trim();

            await using var rd =
                await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                resultado.Add(
                    new ProduccionHistorialEjecucionVm
                    {
                        EjecucionProduccionID =
                            Convert.ToInt32(
                                rd["EjecucionProduccionID"]),

                        ProgramaProduccionID =
                            Convert.ToInt32(
                                rd["ProgramaProduccionID"]),

                        SolicitudProduccionID =
                            rd["SolicitudProduccionID"] ==
                            DBNull.Value
                                ? null
                                : Convert.ToInt32(
                                    rd["SolicitudProduccionID"]),

                        MaquinaID =
                            rd["MaquinaID"] == DBNull.Value
                                ? null
                                : Convert.ToInt32(
                                    rd["MaquinaID"]),

                        MaquinaCodigo =
                            rd["MaquinaCodigo"] ==
                            DBNull.Value
                                ? null
                                : rd["MaquinaCodigo"]
                                    .ToString(),

                        MaquinaNombre =
                            rd["MaquinaNombre"] ==
                            DBNull.Value
                                ? null
                                : rd["MaquinaNombre"]
                                    .ToString(),

                        ParteID =
                            rd["ParteID"] == DBNull.Value
                                ? null
                                : Convert.ToInt32(
                                    rd["ParteID"]),

                        NumeroParte =
                            rd["NumeroParte"] ==
                            DBNull.Value
                                ? null
                                : rd["NumeroParte"]
                                    .ToString(),

                        ReferenciaSAP =
                            rd["ReferenciaSAP"] ==
                            DBNull.Value
                                ? null
                                : rd["ReferenciaSAP"]
                                    .ToString(),

                        DescripcionParte =
                            rd["DescripcionParte"] ==
                            DBNull.Value
                                ? null
                                : rd["DescripcionParte"]
                                    .ToString(),

                        FechaInicioReal =
                            rd["FechaInicioReal"] ==
                            DBNull.Value
                                ? null
                                : Convert.ToDateTime(
                                    rd["FechaInicioReal"]),

                        FechaFinReal =
                            rd["FechaFinReal"] ==
                            DBNull.Value
                                ? null
                                : Convert.ToDateTime(
                                    rd["FechaFinReal"]),

                        CantidadPlaneada =
                            Convert.ToInt32(
                                rd["CantidadPlaneada"]),

                        CantidadOK =
                            Convert.ToInt32(
                                rd["CantidadOKTotal"]),

                        CantidadSospechosa =
                            Convert.ToInt32(
                                rd["CantidadSospechosaTotal"]),

                        CantidadScrap =
                            Convert.ToInt32(
                                rd["CantidadScrapTotal"]),

                        ObjetivoAcumulado =
                            Convert.ToInt32(
                                rd["ObjetivoAcumulado"]),

                        HorasCapturadas =
                            Convert.ToInt32(
                                rd["HorasCapturadas"]),

                        PorcentajeCumplimiento =
                            Convert.ToDecimal(
                                rd["PorcentajeCumplimiento"]),

                        EstatusID =
                            Convert.ToInt32(
                                rd["EstatusID"]),

                        OperadorPrincipalNombre =
                            rd["OperadorNombre"] ==
                            DBNull.Value
                                ? null
                                : rd["OperadorNombre"]
                                    .ToString(),

                        TotalCambiosTurno =
                            Convert.ToInt32(
                                rd["TotalCambiosTurno"]),

                        TotalParos =
                            Convert.ToInt32(
                                rd["TotalParos"])
                    });
            }

            return resultado;
        }

        private sealed class ValidacionTerminarProduccionResultado
        {
            public bool Permitido { get; set; }

            public List<string> Bloqueos { get; set; } =
                new List<string>();

            public string Mensaje =>
                Permitido
                    ? "La producción puede terminarse."
                    : string.Join(" ", Bloqueos);
        }

        private async Task<ValidacionTerminarProduccionResultado> ValidarTerminarProduccionAsync(int ejecucionProduccionId, SqlConnection cn, SqlTransaction tx)
        {
            var resultado = new ValidacionTerminarProduccionResultado();
            if (ejecucionProduccionId <= 0)
            {
                resultado.Bloqueos.Add("La ejecución de Producción no es válida.");
                return resultado;
            }
            const string sql = @"
DECLARE @CantidadOK INT=0;
DECLARE @CantidadSospechosa INT=0;
DECLARE @CantidadScrap INT=0;
DECLARE @OkEnCajas INT=0;
DECLARE @SospechosoEnCajas INT=0;
DECLARE @RetencionEnCajas INT=0;
DECLARE @ScrapEnCajas INT=0;
DECLARE @DetalleOk INT=0;
DECLARE @ParosAbiertos INT=0;
DECLARE @TiempoExtraActivo INT=0;
DECLARE @CajasFormadasPendientes INT=0;
DECLARE @CajasPendientesCalidad INT=0;
DECLARE @InspeccionID INT=NULL;
DECLARE @EstadoCalidad NVARCHAR(50)=NULL;
DECLARE @ConfiguracionInvalidada BIT=0;
DECLARE @RequiereReliberacion BIT=0;
DECLARE @MonitoreosPendientes INT=0;
DECLARE @DisposicionesPendientes INT=0;
DECLARE @ReliberacionesPendientes INT=0;

SELECT
    @CantidadOK=ISNULL(e.CantidadOKTotal,0),
    @CantidadSospechosa=ISNULL(e.CantidadSospechosaTotal,0),
    @CantidadScrap=ISNULL(e.CantidadScrapTotal,0)
FROM dbo.Produccion_Ejecucion e WITH (UPDLOCK,HOLDLOCK)
WHERE e.EjecucionProduccionID=@EjecucionProduccionID
  AND e.Activo=1;

SELECT @ParosAbiertos=COUNT(1)
FROM dbo.Produccion_Paros p WITH (UPDLOCK,HOLDLOCK)
WHERE p.EjecucionProduccionID=@EjecucionProduccionID
  AND p.Activo=1
  AND p.FechaFinParo IS NULL;

SELECT @TiempoExtraActivo=COUNT(1)
FROM dbo.Produccion_TiempoExtra te WITH (UPDLOCK,HOLDLOCK)
WHERE te.EjecucionProduccionID=@EjecucionProduccionID
  AND te.Activo=1
  AND te.FechaHoraFin IS NULL
  AND UPPER(LTRIM(RTRIM(ISNULL(te.Estado,N''))))=N'EN_CURSO';

SELECT
    @OkEnCajas=ISNULL(SUM(CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(c.TipoCaja,N'OK'))))=N'OK' THEN ISNULL(c.CantidadPiezas,ISNULL(c.Cantidad,0)) ELSE 0 END),0),
    @SospechosoEnCajas=ISNULL(SUM(CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(c.TipoCaja,N''))))=N'SOSPECHOSO' THEN ISNULL(c.CantidadPiezas,ISNULL(c.Cantidad,0)) ELSE 0 END),0),
    @RetencionEnCajas=ISNULL(SUM(CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(c.TipoCaja,N''))))=N'RETENCION' THEN ISNULL(c.CantidadPiezas,ISNULL(c.Cantidad,0)) ELSE 0 END),0),
    @ScrapEnCajas=ISNULL(SUM(CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(c.TipoCaja,N''))))=N'SCRAP' THEN ISNULL(c.CantidadPiezas,ISNULL(c.Cantidad,0)) ELSE 0 END),0)
FROM dbo.Produccion_Cajas c WITH (UPDLOCK,HOLDLOCK)
WHERE c.EjecucionProduccionID=@EjecucionProduccionID
  AND c.Activo=1
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.Produccion_CajaOrigenDetalle od
      WHERE od.CajaProduccionID=c.CajaProduccionID
        AND od.Activo=1
  );

SELECT @DetalleOk=ISNULL(SUM(od.CantidadPiezas),0)
FROM dbo.Produccion_CajaOrigenDetalle od WITH (UPDLOCK,HOLDLOCK)
WHERE od.EjecucionProduccionID=@EjecucionProduccionID
  AND od.Activo=1;

SET @OkEnCajas=@OkEnCajas+@DetalleOk;

SELECT
    @CajasFormadasPendientes=SUM(CASE WHEN c.EstadoCajaID=@CajaFormadaProduccion THEN 1 ELSE 0 END),
    @CajasPendientesCalidad=SUM(CASE WHEN c.EstadoCajaID=@CajaPendienteCalidad THEN 1 ELSE 0 END)
FROM dbo.Produccion_Cajas c WITH (UPDLOCK,HOLDLOCK)
WHERE c.EjecucionProduccionID=@EjecucionProduccionID
  AND c.Activo=1;

SET @CajasFormadasPendientes=ISNULL(@CajasFormadasPendientes,0);
SET @CajasPendientesCalidad=ISNULL(@CajasPendientesCalidad,0);

SELECT TOP (1)
    @InspeccionID=ci.InspeccionID,
    @EstadoCalidad=UPPER(LTRIM(RTRIM(ISNULL(ci.Estado,N'')))),
    @ConfiguracionInvalidada=ISNULL(ci.ConfiguracionInvalidada,0),
    @RequiereReliberacion=ISNULL(ci.RequiereReliberacion,0)
FROM dbo.Calidad_Inspecciones ci WITH (UPDLOCK,HOLDLOCK)
WHERE ci.EjecucionProduccionID=@EjecucionProduccionID
ORDER BY ci.InspeccionID DESC;

IF @InspeccionID IS NOT NULL
BEGIN
    SELECT @MonitoreosPendientes=COUNT(1)
    FROM dbo.Calidad_MonitoreosProceso m WITH (UPDLOCK,HOLDLOCK)
    WHERE m.InspeccionID=@InspeccionID
      AND m.Activo=1
      AND UPPER(LTRIM(RTRIM(ISNULL(m.Resultado,N'PENDIENTE'))))=N'PENDIENTE';

    SELECT @DisposicionesPendientes=COUNT(1)
    FROM dbo.Calidad_DisposicionesMaterial d WITH (UPDLOCK,HOLDLOCK)
    WHERE d.InspeccionID=@InspeccionID
      AND d.Activo=1
      AND UPPER(LTRIM(RTRIM(ISNULL(d.ResultadoFinal,N'PENDIENTE'))))=N'PENDIENTE';

    SELECT @ReliberacionesPendientes=COUNT(1)
    FROM dbo.Calidad_Reliberaciones r WITH (UPDLOCK,HOLDLOCK)
    WHERE r.InspeccionID=@InspeccionID
      AND r.Activo=1
      AND UPPER(LTRIM(RTRIM(ISNULL(r.Resultado,N'PENDIENTE'))))<>N'AUTORIZADA';
END;

SELECT
    @CantidadOK AS CantidadOK,
    @CantidadSospechosa AS CantidadSospechosa,
    @CantidadScrap AS CantidadScrap,
    @OkEnCajas AS OkEnCajas,
    @SospechosoEnCajas AS SospechosoEnCajas,
    @RetencionEnCajas AS RetencionEnCajas,
    @ScrapEnCajas AS ScrapEnCajas,
    @ParosAbiertos AS ParosAbiertos,
    @TiempoExtraActivo AS TiempoExtraActivo,
    @CajasFormadasPendientes AS CajasFormadasPendientes,
    @CajasPendientesCalidad AS CajasPendientesCalidad,
    @InspeccionID AS InspeccionID,
    @EstadoCalidad AS EstadoCalidad,
    @ConfiguracionInvalidada AS ConfiguracionInvalidada,
    @RequiereReliberacion AS RequiereReliberacion,
    @MonitoreosPendientes AS MonitoreosPendientes,
    @DisposicionesPendientes AS DisposicionesPendientes,
    @ReliberacionesPendientes AS ReliberacionesPendientes;";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
            cmd.Parameters.Add("@CajaFormadaProduccion", SqlDbType.Int).Value = ProduccionCajaEstatus.FormadaProduccion;
            cmd.Parameters.Add("@CajaPendienteCalidad", SqlDbType.Int).Value = ProduccionCajaEstatus.PendienteCalidad;
            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync())
            {
                resultado.Bloqueos.Add("No fue posible validar los pendientes de la producción.");
                return resultado;
            }
            var cantidadOk = Convert.ToInt32(rd["CantidadOK"]);
            var cantidadSospechosa = Convert.ToInt32(rd["CantidadSospechosa"]);
            var cantidadScrap = Convert.ToInt32(rd["CantidadScrap"]);
            var okEnCajas = Convert.ToInt32(rd["OkEnCajas"]);
            var sospechosoEnCajas = Convert.ToInt32(rd["SospechosoEnCajas"]);
            var retencionEnCajas = Convert.ToInt32(rd["RetencionEnCajas"]);
            var scrapEnCajas = Convert.ToInt32(rd["ScrapEnCajas"]);
            var parosAbiertos = Convert.ToInt32(rd["ParosAbiertos"]);
            var tiempoExtraActivo = Convert.ToInt32(rd["TiempoExtraActivo"]);
            var cajasFormadasPendientes = Convert.ToInt32(rd["CajasFormadasPendientes"]);
            var cajasPendientesCalidad = Convert.ToInt32(rd["CajasPendientesCalidad"]);
            var configuracionInvalidada = Convert.ToBoolean(rd["ConfiguracionInvalidada"]);
            var requiereReliberacion = Convert.ToBoolean(rd["RequiereReliberacion"]);
            var monitoreosPendientes = Convert.ToInt32(rd["MonitoreosPendientes"]);
            var disposicionesPendientes = Convert.ToInt32(rd["DisposicionesPendientes"]);
            var reliberacionesPendientes = Convert.ToInt32(rd["ReliberacionesPendientes"]);
            var pendienteOk = Math.Max(0, cantidadOk - okEnCajas);
            var pendienteSospechoso = Math.Max(0, cantidadSospechosa - sospechosoEnCajas - retencionEnCajas);
            var pendienteScrap = Math.Max(0, cantidadScrap - scrapEnCajas);
            if (parosAbiertos > 0)
                resultado.Bloqueos.Add("Existe un paro abierto.");
            if (tiempoExtraActivo > 0)
                resultado.Bloqueos.Add("Existe una sesión de tiempo extra en curso. Debe finalizarse y registrar su último corte antes de terminar la producción.");
            if (pendienteOk > 0)
                resultado.Bloqueos.Add($"Faltan {pendienteOk:N0} pieza(s) OK por asignar a caja.");
            if (pendienteSospechoso > 0)
                resultado.Bloqueos.Add($"Faltan {pendienteSospechoso:N0} pieza(s) sospechosas/retención por asignar a caja.");
            if (pendienteScrap > 0)
                resultado.Bloqueos.Add($"Faltan {pendienteScrap:N0} pieza(s) scrap por asignar a caja.");
            if (cajasFormadasPendientes > 0)
                resultado.Bloqueos.Add($"Existen {cajasFormadasPendientes:N0} caja(s) todavía sin enviar a Calidad.");
            if (cajasPendientesCalidad > 0)
                resultado.Bloqueos.Add($"Existen {cajasPendientesCalidad:N0} caja(s) pendientes de decisión de Calidad.");
            if (configuracionInvalidada)
                resultado.Bloqueos.Add("La configuración de Calidad se encuentra invalidada.");
            if (requiereReliberacion)
                resultado.Bloqueos.Add("Existe una reliberación de Calidad pendiente.");
            if (monitoreosPendientes > 0)
                resultado.Bloqueos.Add($"Calidad tiene {monitoreosPendientes:N0} monitoreo(s) pendiente(s).");
            if (disposicionesPendientes > 0)
                resultado.Bloqueos.Add($"Calidad tiene {disposicionesPendientes:N0} disposición(es) pendiente(s).");
            if (reliberacionesPendientes > 0)
                resultado.Bloqueos.Add($"Existen {reliberacionesPendientes:N0} reliberación(es) sin concluir.");
            resultado.Permitido = resultado.Bloqueos.Count == 0;
            return resultado;
        }

        private sealed class ProduccionTurnoActual
        {
            public int TurnoID { get; set; }

            public string TurnoNombre { get; set; } =
                string.Empty;

            public DateTime FechaOperacion { get; set; }

            public DateTime FechaInicio { get; set; }

            public DateTime FechaFin { get; set; }
        }

        private async Task<ProduccionChecklistArranqueVm?>
      ObtenerChecklistArranquePorEjecucionAsync(
          int ejecucionProduccionId,
          SqlConnection cn)
        {
            const string sqlId = @"
SELECT TOP (1)
    ChecklistArranqueID
FROM dbo.Produccion_ChecklistArranque
WHERE EjecucionProduccionID =
      @EjecucionProduccionID
  AND CodigoFormato =
      N'GQ-F-PR01-06'
  AND TipoChecklist =
      N'ARRANQUE_LIBERACION'
  AND Activo = 1
ORDER BY
    NumeroAplicacion DESC,
    ChecklistArranqueID DESC;";

            await using var cmd =
                new SqlCommand(
                    sqlId,
                    cn);

            cmd.Parameters.Add(
                "@EjecucionProduccionID",
                SqlDbType.Int).Value =
                ejecucionProduccionId;

            var resultado =
                await cmd.ExecuteScalarAsync();

            if (resultado == null ||
                resultado == DBNull.Value)
            {
                return null;
            }

            return await ObtenerChecklistArranqueAsync(
                Convert.ToInt32(resultado),
                cn);
        }

        [HttpGet]
        public async Task<IActionResult> ChecklistFormato(int id)
        {
            if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");
            if (id <= 0) return NotFound();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var checklist = await ObtenerChecklistArranqueAsync(id, cn);
            if (checklist == null) return NotFound();

            if (string.Equals(checklist.TipoChecklist, "MONITOREO_PERIFERICOS", StringComparison.OrdinalIgnoreCase))
                ViewBag.TecnicosProduccion = await CargarOperadoresProduccionAsync(cn);

            return View("ChecklistArranque", checklist);
        }


        private async Task<ProduccionChecklistArranqueVm?> ObtenerChecklistArranqueAsync(int checklistArranqueId, SqlConnection cn)
        {
            const string sql = @"
SELECT ChecklistArranqueID,EjecucionProduccionID,ProgramaProduccionID,SolicitudProduccionID,SolicitudProduccionDetalleID,ReleaseID,ReleaseDetalleID,
       FechaChecklist,FechaOperacion,MaquinaID,MaquinaCodigo,MaquinaNombre,MoldeID,MoldeCodigo,ParteID,NumeroParte,ReferenciaSAP,DescripcionParte,
       CodigoFormato,VersionFormato,TipoChecklist,MomentoProceso,TurnoID,TurnoNombre,NumeroAplicacion,EsRecurrente,RequiereCambioMolde,EstatusID,
       UsuarioProduccionID,FechaCapturaProduccion,UsuarioCalidadID,FechaValidacionCalidad,ObservacionesGenerales,ObservacionesCalidad,
       TecnicoEntregaPersonaID,TecnicoEntregaNombre,FechaEntregaTurno,TecnicoRecibePersonaID,TecnicoRecibeNombre,FechaRecepcionTurno,
       UsuarioCreacionID,FechaCreacion,UsuarioModificacionID,FechaModificacion,Activo
FROM dbo.Produccion_ChecklistArranque
WHERE ChecklistArranqueID=@ChecklistArranqueID AND Activo=1;";

            ProduccionChecklistArranqueVm? vm = null;

            await using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.Add("@ChecklistArranqueID", SqlDbType.Int).Value = checklistArranqueId;
                await using var rd = await cmd.ExecuteReaderAsync();

                if (await rd.ReadAsync())
                {
                    vm = new ProduccionChecklistArranqueVm
                    {
                        ChecklistArranqueID = Entero(rd, "ChecklistArranqueID"),
                        EjecucionProduccionID = Entero(rd, "EjecucionProduccionID"),
                        ProgramaProduccionID = Entero(rd, "ProgramaProduccionID"),
                        SolicitudProduccionID = NullableEntero(rd, "SolicitudProduccionID"),
                        SolicitudProduccionDetalleID = NullableEntero(rd, "SolicitudProduccionDetalleID"),
                        ReleaseID = NullableEntero(rd, "ReleaseID"),
                        ReleaseDetalleID = NullableEntero(rd, "ReleaseDetalleID"),
                        FechaChecklist = Fecha(rd, "FechaChecklist"),
                        FechaOperacion = NullableFecha(rd, "FechaOperacion"),
                        MaquinaID = NullableEntero(rd, "MaquinaID"),
                        MaquinaCodigo = TextoNullable(rd, "MaquinaCodigo"),
                        MaquinaNombre = TextoNullable(rd, "MaquinaNombre"),
                        MoldeID = NullableEntero(rd, "MoldeID"),
                        MoldeCodigo = TextoNullable(rd, "MoldeCodigo"),
                        ParteID = NullableEntero(rd, "ParteID"),
                        NumeroParte = TextoNullable(rd, "NumeroParte"),
                        ReferenciaSAP = TextoNullable(rd, "ReferenciaSAP"),
                        DescripcionParte = TextoNullable(rd, "DescripcionParte"),
                        CodigoFormato = TextoNullable(rd, "CodigoFormato") ?? string.Empty,
                        VersionFormato = TextoNullable(rd, "VersionFormato"),
                        TipoChecklist = TextoNullable(rd, "TipoChecklist") ?? string.Empty,
                        MomentoProceso = TextoNullable(rd, "MomentoProceso") ?? string.Empty,
                        TurnoID = NullableEntero(rd, "TurnoID"),
                        TurnoNombre = TextoNullable(rd, "TurnoNombre"),
                        NumeroAplicacion = Entero(rd, "NumeroAplicacion"),
                        EsRecurrente = Booleano(rd, "EsRecurrente"),
                        RequiereCambioMolde = Booleano(rd, "RequiereCambioMolde"),
                        EstatusID = Entero(rd, "EstatusID"),
                        UsuarioProduccionID = NullableEntero(rd, "UsuarioProduccionID"),
                        FechaCapturaProduccion = NullableFecha(rd, "FechaCapturaProduccion"),
                        UsuarioCalidadID = NullableEntero(rd, "UsuarioCalidadID"),
                        FechaValidacionCalidad = NullableFecha(rd, "FechaValidacionCalidad"),
                        ObservacionesGenerales = TextoNullable(rd, "ObservacionesGenerales"),
                        ObservacionesCalidad = TextoNullable(rd, "ObservacionesCalidad"),
                        TecnicoEntregaPersonaID = NullableEntero(rd, "TecnicoEntregaPersonaID"),
                        TecnicoEntregaNombre = TextoNullable(rd, "TecnicoEntregaNombre"),
                        FechaEntregaTurno = NullableFecha(rd, "FechaEntregaTurno"),
                        TecnicoRecibePersonaID = NullableEntero(rd, "TecnicoRecibePersonaID"),
                        TecnicoRecibeNombre = TextoNullable(rd, "TecnicoRecibeNombre"),
                        FechaRecepcionTurno = NullableFecha(rd, "FechaRecepcionTurno"),
                        UsuarioCreacionID = NullableEntero(rd, "UsuarioCreacionID"),
                        FechaCreacion = Fecha(rd, "FechaCreacion"),
                        UsuarioModificacionID = NullableEntero(rd, "UsuarioModificacionID"),
                        FechaModificacion = NullableFecha(rd, "FechaModificacion"),
                        Activo = Booleano(rd, "Activo")
                    };
                }
            }

            if (vm == null) return null;

            await CargarPreguntasChecklistArranqueAsync(vm, cn);

            if (string.Equals(vm.TipoChecklist, "MONITOREO_PERIFERICOS", StringComparison.OrdinalIgnoreCase))
                vm.ProblemasPerifericos = await ObtenerProblemasPerifericosAsync(vm.ChecklistArranqueID, cn);

            return vm;
        }

        private async Task<List<ProduccionMonitoreoPerifericoProblemaVm>> ObtenerProblemasPerifericosAsync(int checklistArranqueId, SqlConnection cn)
        {
            var lista = new List<ProduccionMonitoreoPerifericoProblemaVm>();

            const string sql = @"
SELECT MonitoreoPerifericoProblemaID,ChecklistArranqueID,EjecucionProduccionID,FechaOperacion,TurnoID,TurnoNombre,
       MaquinaID,MaquinaCodigo,MaquinaNombre,DescripcionFalla,CausaRaiz,Acciones,Solucionado,FechaSolucion,
       UsuarioSolucionID,UsuarioSolucionNombre,UsuarioCreacionID,FechaCreacion,UsuarioModificacionID,FechaModificacion,Activo
FROM dbo.Produccion_MonitoreoPerifericosProblemas
WHERE ChecklistArranqueID=@ChecklistArranqueID AND Activo=1
ORDER BY Solucionado,FechaCreacion DESC,MonitoreoPerifericoProblemaID DESC;";

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@ChecklistArranqueID", SqlDbType.Int).Value = checklistArranqueId;
            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(new ProduccionMonitoreoPerifericoProblemaVm
                {
                    MonitoreoPerifericoProblemaID = Entero(rd, "MonitoreoPerifericoProblemaID"),
                    ChecklistArranqueID = Entero(rd, "ChecklistArranqueID"),
                    EjecucionProduccionID = Entero(rd, "EjecucionProduccionID"),
                    FechaOperacion = Fecha(rd, "FechaOperacion"),
                    TurnoID = Entero(rd, "TurnoID"),
                    TurnoNombre = TextoNullable(rd, "TurnoNombre"),
                    MaquinaID = NullableEntero(rd, "MaquinaID"),
                    MaquinaCodigo = TextoNullable(rd, "MaquinaCodigo"),
                    MaquinaNombre = TextoNullable(rd, "MaquinaNombre"),
                    DescripcionFalla = TextoNullable(rd, "DescripcionFalla") ?? string.Empty,
                    CausaRaiz = TextoNullable(rd, "CausaRaiz"),
                    Acciones = TextoNullable(rd, "Acciones"),
                    Solucionado = Booleano(rd, "Solucionado"),
                    FechaSolucion = NullableFecha(rd, "FechaSolucion"),
                    UsuarioSolucionID = NullableEntero(rd, "UsuarioSolucionID"),
                    UsuarioSolucionNombre = TextoNullable(rd, "UsuarioSolucionNombre")
                });
            }

            return lista;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarProblemaPerifericos(ProduccionMonitoreoPerifericoProblemaPostVm vm)
        {
            if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");

            if (vm.ChecklistArranqueID <= 0)
            {
                TempData["Error"] = "No se recibió correctamente el checklist.";
                return RedirectToAction(nameof(Index));
            }

            if (string.IsNullOrWhiteSpace(vm.DescripcionFalla))
            {
                TempData["Error"] = "Captura la descripción del problema.";
                return RedirectToAction(nameof(ChecklistFormato), new { id = vm.ChecklistArranqueID });
            }

            var usuarioId = ObtenerUsuarioID();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();

            try
            {
                const string sqlChecklist = @"
SELECT TOP(1) EjecucionProduccionID,FechaOperacion,TurnoID,TurnoNombre,MaquinaID,MaquinaCodigo,MaquinaNombre,TipoChecklist,EstatusID
FROM dbo.Produccion_ChecklistArranque WITH(UPDLOCK,HOLDLOCK)
WHERE ChecklistArranqueID=@ChecklistArranqueID AND Activo=1;";

                int ejecucionProduccionId;
                DateTime fechaOperacion;
                int turnoId;
                string? turnoNombre;
                int? maquinaId;
                string? maquinaCodigo;
                string? maquinaNombre;
                string tipoChecklist;
                int estatusId;

                await using (var cmd = new SqlCommand(sqlChecklist, cn, tx))
                {
                    cmd.Parameters.Add("@ChecklistArranqueID", SqlDbType.Int).Value = vm.ChecklistArranqueID;
                    await using var rd = await cmd.ExecuteReaderAsync();

                    if (!await rd.ReadAsync())
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = "No se encontró el checklist.";
                        return RedirectToAction(nameof(Index));
                    }

                    ejecucionProduccionId = Entero(rd, "EjecucionProduccionID");
                    fechaOperacion = NullableFecha(rd, "FechaOperacion") ?? DateTime.Today;
                    turnoId = NullableEntero(rd, "TurnoID") ?? 0;
                    turnoNombre = TextoNullable(rd, "TurnoNombre");
                    maquinaId = NullableEntero(rd, "MaquinaID");
                    maquinaCodigo = TextoNullable(rd, "MaquinaCodigo");
                    maquinaNombre = TextoNullable(rd, "MaquinaNombre");
                    tipoChecklist = TextoNullable(rd, "TipoChecklist") ?? string.Empty;
                    estatusId = Entero(rd, "EstatusID");
                }

                if (!string.Equals(tipoChecklist, "MONITOREO_PERIFERICOS", StringComparison.OrdinalIgnoreCase))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "El checklist no corresponde al monitoreo de periféricos.";
                    return RedirectToAction(nameof(ChecklistFormato), new { id = vm.ChecklistArranqueID });
                }

                if (turnoId <= 0)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "El checklist no tiene un turno válido.";
                    return RedirectToAction(nameof(ChecklistFormato), new { id = vm.ChecklistArranqueID });
                }

                if (!ProduccionChecklistEstatus.PuedeEditarProduccion(estatusId))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "Este checklist ya no puede ser modificado.";
                    return RedirectToAction(nameof(ChecklistFormato), new { id = vm.ChecklistArranqueID });
                }

                string? usuarioSolucionNombre = null;

                if (vm.Solucionado)
                {
                    usuarioSolucionNombre = await ObtenerPersonaNombreAsync(usuarioId, cn, tx);
                    if (string.IsNullOrWhiteSpace(usuarioSolucionNombre)) usuarioSolucionNombre = User?.Identity?.Name ?? "Usuario de producción";
                }

                if (vm.MonitoreoPerifericoProblemaID > 0)
                {
                    const string sqlActualizar = @"
UPDATE dbo.Produccion_MonitoreoPerifericosProblemas
SET DescripcionFalla=@DescripcionFalla,CausaRaiz=@CausaRaiz,Acciones=@Acciones,Solucionado=@Solucionado,
    FechaSolucion=CASE WHEN @Solucionado=1 THEN ISNULL(FechaSolucion,GETDATE()) ELSE NULL END,
    UsuarioSolucionID=CASE WHEN @Solucionado=1 THEN @UsuarioID ELSE NULL END,
    UsuarioSolucionNombre=CASE WHEN @Solucionado=1 THEN @UsuarioSolucionNombre ELSE NULL END,
    UsuarioModificacionID=@UsuarioID,FechaModificacion=GETDATE()
WHERE MonitoreoPerifericoProblemaID=@ProblemaID AND ChecklistArranqueID=@ChecklistArranqueID AND Activo=1;";

                    await using var cmd = new SqlCommand(sqlActualizar, cn, tx);
                    cmd.Parameters.Add("@ProblemaID", SqlDbType.Int).Value = vm.MonitoreoPerifericoProblemaID;
                    cmd.Parameters.Add("@ChecklistArranqueID", SqlDbType.Int).Value = vm.ChecklistArranqueID;
                    cmd.Parameters.Add("@DescripcionFalla", SqlDbType.NVarChar, 1000).Value = vm.DescripcionFalla.Trim();
                    cmd.Parameters.Add("@CausaRaiz", SqlDbType.NVarChar, 1000).Value = string.IsNullOrWhiteSpace(vm.CausaRaiz) ? DBNull.Value : vm.CausaRaiz.Trim();
                    cmd.Parameters.Add("@Acciones", SqlDbType.NVarChar, 1000).Value = string.IsNullOrWhiteSpace(vm.Acciones) ? DBNull.Value : vm.Acciones.Trim();
                    cmd.Parameters.Add("@Solucionado", SqlDbType.Bit).Value = vm.Solucionado;
                    cmd.Parameters.Add("@UsuarioSolucionNombre", SqlDbType.NVarChar, 200).Value = string.IsNullOrWhiteSpace(usuarioSolucionNombre) ? DBNull.Value : usuarioSolucionNombre.Trim();
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;

                    if (await cmd.ExecuteNonQueryAsync() <= 0) throw new InvalidOperationException("No se encontró el problema que deseas actualizar.");
                }
                else
                {
                    const string sqlInsertar = @"
INSERT INTO dbo.Produccion_MonitoreoPerifericosProblemas
(ChecklistArranqueID,EjecucionProduccionID,FechaOperacion,TurnoID,TurnoNombre,MaquinaID,MaquinaCodigo,MaquinaNombre,
 DescripcionFalla,CausaRaiz,Acciones,Solucionado,FechaSolucion,UsuarioSolucionID,UsuarioSolucionNombre,UsuarioCreacionID,FechaCreacion,Activo)
VALUES
(@ChecklistArranqueID,@EjecucionProduccionID,@FechaOperacion,@TurnoID,@TurnoNombre,@MaquinaID,@MaquinaCodigo,@MaquinaNombre,
 @DescripcionFalla,@CausaRaiz,@Acciones,@Solucionado,
 CASE WHEN @Solucionado=1 THEN GETDATE() ELSE NULL END,
 CASE WHEN @Solucionado=1 THEN @UsuarioID ELSE NULL END,
 CASE WHEN @Solucionado=1 THEN @UsuarioSolucionNombre ELSE NULL END,
 @UsuarioID,GETDATE(),1);";

                    await using var cmd = new SqlCommand(sqlInsertar, cn, tx);
                    cmd.Parameters.Add("@ChecklistArranqueID", SqlDbType.Int).Value = vm.ChecklistArranqueID;
                    cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                    cmd.Parameters.Add("@FechaOperacion", SqlDbType.Date).Value = fechaOperacion.Date;
                    cmd.Parameters.Add("@TurnoID", SqlDbType.Int).Value = turnoId;
                    cmd.Parameters.Add("@TurnoNombre", SqlDbType.NVarChar, 50).Value = string.IsNullOrWhiteSpace(turnoNombre) ? DBNull.Value : turnoNombre.Trim();
                    cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = (object?)maquinaId ?? DBNull.Value;
                    cmd.Parameters.Add("@MaquinaCodigo", SqlDbType.NVarChar, 100).Value = string.IsNullOrWhiteSpace(maquinaCodigo) ? DBNull.Value : maquinaCodigo.Trim();
                    cmd.Parameters.Add("@MaquinaNombre", SqlDbType.NVarChar, 200).Value = string.IsNullOrWhiteSpace(maquinaNombre) ? DBNull.Value : maquinaNombre.Trim();
                    cmd.Parameters.Add("@DescripcionFalla", SqlDbType.NVarChar, 1000).Value = vm.DescripcionFalla.Trim();
                    cmd.Parameters.Add("@CausaRaiz", SqlDbType.NVarChar, 1000).Value = string.IsNullOrWhiteSpace(vm.CausaRaiz) ? DBNull.Value : vm.CausaRaiz.Trim();
                    cmd.Parameters.Add("@Acciones", SqlDbType.NVarChar, 1000).Value = string.IsNullOrWhiteSpace(vm.Acciones) ? DBNull.Value : vm.Acciones.Trim();
                    cmd.Parameters.Add("@Solucionado", SqlDbType.Bit).Value = vm.Solucionado;
                    cmd.Parameters.Add("@UsuarioSolucionNombre", SqlDbType.NVarChar, 200).Value = string.IsNullOrWhiteSpace(usuarioSolucionNombre) ? DBNull.Value : usuarioSolucionNombre.Trim();
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    await cmd.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();
                TempData["Success"] = vm.MonitoreoPerifericoProblemaID > 0 ? "Problema actualizado correctamente." : "Problema registrado correctamente.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] = "No fue posible guardar el problema: " + ex.Message;
            }

            return RedirectToAction(nameof(ChecklistFormato), new { id = vm.ChecklistArranqueID });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EliminarProblemaPerifericos(int monitoreoPerifericoProblemaId, int checklistArranqueId)
        {
            if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");

            if (monitoreoPerifericoProblemaId <= 0 || checklistArranqueId <= 0)
            {
                TempData["Error"] = "No se recibió correctamente el problema.";
                return RedirectToAction(nameof(Index));
            }

            var usuarioId = ObtenerUsuarioID();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            const string sql = @"
UPDATE problema
SET problema.Activo=0,problema.UsuarioModificacionID=@UsuarioID,problema.FechaModificacion=GETDATE()
FROM dbo.Produccion_MonitoreoPerifericosProblemas problema
INNER JOIN dbo.Produccion_ChecklistArranque checklist ON checklist.ChecklistArranqueID=problema.ChecklistArranqueID AND checklist.Activo=1
WHERE problema.MonitoreoPerifericoProblemaID=@ProblemaID
  AND problema.ChecklistArranqueID=@ChecklistArranqueID
  AND problema.Activo=1
  AND checklist.TipoChecklist=N'MONITOREO_PERIFERICOS'
  AND checklist.EstatusID IN(@PendienteProduccion,@CapturadoProduccion);";

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@ProblemaID", SqlDbType.Int).Value = monitoreoPerifericoProblemaId;
            cmd.Parameters.Add("@ChecklistArranqueID", SqlDbType.Int).Value = checklistArranqueId;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            cmd.Parameters.Add("@PendienteProduccion", SqlDbType.Int).Value = ProduccionChecklistEstatus.PendienteProduccion;
            cmd.Parameters.Add("@CapturadoProduccion", SqlDbType.Int).Value = ProduccionChecklistEstatus.CapturadoPorProduccion;

            var afectados = await cmd.ExecuteNonQueryAsync();
            TempData[afectados > 0 ? "Success" : "Error"] = afectados > 0 ? "Problema eliminado correctamente." : "No fue posible eliminar el problema o el checklist ya está bloqueado.";

            return RedirectToAction(nameof(ChecklistFormato), new { id = checklistArranqueId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarEntregaTurnoPerifericos(ProduccionEntregaTurnoPerifericosPostVm vm)
        {
            if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");

            if (vm.ChecklistArranqueID <= 0)
            {
                TempData["Error"] = "No se recibió correctamente el checklist.";
                return RedirectToAction(nameof(Index));
            }

            if (!vm.TecnicoRecibePersonaID.HasValue || vm.TecnicoRecibePersonaID.Value <= 0)
            {
                TempData["Error"] = "Selecciona al técnico que recibe el turno.";
                return RedirectToAction(nameof(ChecklistFormato), new { id = vm.ChecklistArranqueID });
            }

            var usuarioId = ObtenerUsuarioID();

            if (usuarioId == vm.TecnicoRecibePersonaID.Value)
            {
                TempData["Error"] = "El técnico que entrega y el técnico que recibe no pueden ser la misma persona.";
                return RedirectToAction(nameof(ChecklistFormato), new { id = vm.ChecklistArranqueID });
            }

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();

            try
            {
                var tecnicoEntregaNombre = await ObtenerPersonaNombreAsync(usuarioId, cn, tx);
                if (string.IsNullOrWhiteSpace(tecnicoEntregaNombre)) tecnicoEntregaNombre = User?.Identity?.Name ?? "Técnico de producción";

                var tecnicoRecibeNombre = await ObtenerPersonaNombreAsync(vm.TecnicoRecibePersonaID.Value, cn, tx);

                if (string.IsNullOrWhiteSpace(tecnicoRecibeNombre))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "No se encontró al técnico que recibe el turno.";
                    return RedirectToAction(nameof(ChecklistFormato), new { id = vm.ChecklistArranqueID });
                }

                const string sql = @"
UPDATE dbo.Produccion_ChecklistArranque
SET TecnicoEntregaPersonaID=@TecnicoEntregaPersonaID,TecnicoEntregaNombre=@TecnicoEntregaNombre,FechaEntregaTurno=GETDATE(),
    TecnicoRecibePersonaID=@TecnicoRecibePersonaID,TecnicoRecibeNombre=@TecnicoRecibeNombre,FechaRecepcionTurno=GETDATE(),
    UsuarioModificacionID=@UsuarioID,FechaModificacion=GETDATE()
WHERE ChecklistArranqueID=@ChecklistArranqueID
  AND TipoChecklist=N'MONITOREO_PERIFERICOS'
  AND Activo=1
  AND EstatusID IN(@PendienteProduccion,@CapturadoProduccion);";

                await using var cmd = new SqlCommand(sql, cn, tx);
                cmd.Parameters.Add("@ChecklistArranqueID", SqlDbType.Int).Value = vm.ChecklistArranqueID;
                cmd.Parameters.Add("@TecnicoEntregaPersonaID", SqlDbType.Int).Value = usuarioId;
                cmd.Parameters.Add("@TecnicoEntregaNombre", SqlDbType.NVarChar, 200).Value = tecnicoEntregaNombre.Trim();
                cmd.Parameters.Add("@TecnicoRecibePersonaID", SqlDbType.Int).Value = vm.TecnicoRecibePersonaID.Value;
                cmd.Parameters.Add("@TecnicoRecibeNombre", SqlDbType.NVarChar, 200).Value = tecnicoRecibeNombre.Trim();
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                cmd.Parameters.Add("@PendienteProduccion", SqlDbType.Int).Value = ProduccionChecklistEstatus.PendienteProduccion;
                cmd.Parameters.Add("@CapturadoProduccion", SqlDbType.Int).Value = ProduccionChecklistEstatus.CapturadoPorProduccion;

                if (await cmd.ExecuteNonQueryAsync() <= 0)
                    throw new InvalidOperationException("El checklist no existe, no corresponde a periféricos o ya está bloqueado.");

                await tx.CommitAsync();
                TempData["Success"] = "Entrega de turno registrada correctamente.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] = "No fue posible registrar la entrega de turno: " + ex.Message;
            }

            return RedirectToAction(nameof(ChecklistFormato), new { id = vm.ChecklistArranqueID });
        }

        private async Task CargarPreguntasChecklistArranqueAsync(ProduccionChecklistArranqueVm vm, SqlConnection cn)
        {
            vm.Secciones.Clear();

            const string sql = @"
SELECT d.ChecklistArranqueDetalleID,d.ChecklistArranqueID,d.PreguntaID,d.Resultado,d.Observaciones,d.Confirmado,
       d.ValorCapturado,d.Unidad,d.Especificacion,d.Tolerancia,d.UsuarioRespuestaID,d.FechaRespuesta,d.Activo,
       p.CodigoFormato,p.VersionFormato,p.TipoChecklist,p.MomentoProceso,p.TipoRespuesta,p.EstadoPredeterminado,
       p.Seccion,p.OrdenSeccion,p.OrdenPregunta,p.TextoPregunta,p.ResponsableSugerido,p.GrupoResponsable,
       p.RequiereObservacionSiNOK,p.RequiereObservacionSiNA,p.EsPreguntaCalidad,p.EsRecurrente
FROM dbo.Produccion_ChecklistArranqueDetalle d
INNER JOIN dbo.ERP_ChecklistArranquePreguntas p ON p.PreguntaID=d.PreguntaID
WHERE d.ChecklistArranqueID=@ChecklistArranqueID AND d.Activo=1 AND p.Activo=1
ORDER BY p.OrdenSeccion,p.OrdenPregunta,p.PreguntaID;";

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@ChecklistArranqueID", SqlDbType.Int).Value = vm.ChecklistArranqueID;
            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                var pregunta = new ProduccionChecklistPreguntaVm
                {
                    ChecklistArranqueDetalleID = Entero(rd, "ChecklistArranqueDetalleID"),
                    ChecklistArranqueID = Entero(rd, "ChecklistArranqueID"),
                    PreguntaID = Entero(rd, "PreguntaID"),
                    CodigoFormato = TextoNullable(rd, "CodigoFormato") ?? string.Empty,
                    VersionFormato = TextoNullable(rd, "VersionFormato"),
                    TipoChecklist = TextoNullable(rd, "TipoChecklist") ?? string.Empty,
                    MomentoProceso = TextoNullable(rd, "MomentoProceso") ?? string.Empty,
                    TipoRespuesta = TextoNullable(rd, "TipoRespuesta") ?? "ESTADO",
                    EstadoPredeterminado = TextoNullable(rd, "EstadoPredeterminado") ?? "OK",
                    Seccion = TextoNullable(rd, "Seccion") ?? string.Empty,
                    OrdenSeccion = Entero(rd, "OrdenSeccion"),
                    OrdenPregunta = Entero(rd, "OrdenPregunta"),
                    TextoPregunta = TextoNullable(rd, "TextoPregunta") ?? string.Empty,
                    ResponsableSugerido = TextoNullable(rd, "ResponsableSugerido"),
                    GrupoResponsable = TextoNullable(rd, "GrupoResponsable"),
                    RequiereObservacionSiNOK = Booleano(rd, "RequiereObservacionSiNOK"),
                    RequiereObservacionSiNA = Booleano(rd, "RequiereObservacionSiNA"),
                    EsPreguntaCalidad = Booleano(rd, "EsPreguntaCalidad"),
                    EsRecurrente = Booleano(rd, "EsRecurrente"),
                    Resultado = TextoNullable(rd, "Resultado"),
                    Observaciones = TextoNullable(rd, "Observaciones"),
                    Confirmado = Booleano(rd, "Confirmado"),
                    ValorCapturado = TextoNullable(rd, "ValorCapturado"),
                    Unidad = TextoNullable(rd, "Unidad"),
                    Especificacion = TextoNullable(rd, "Especificacion"),
                    Tolerancia = TextoNullable(rd, "Tolerancia"),
                    UsuarioRespuestaID = NullableEntero(rd, "UsuarioRespuestaID"),
                    FechaRespuesta = NullableFecha(rd, "FechaRespuesta"),
                    Activo = Booleano(rd, "Activo")
                };

                var seccion = vm.Secciones.FirstOrDefault(x => x.OrdenSeccion == pregunta.OrdenSeccion && x.Seccion == pregunta.Seccion);
                if (seccion == null)
                {
                    seccion = new ProduccionChecklistSeccionVm
                    {
                        Seccion = pregunta.Seccion,
                        OrdenSeccion = pregunta.OrdenSeccion,
                        ResponsableSugerido = pregunta.ResponsableSugerido
                    };
                    vm.Secciones.Add(seccion);
                }

                seccion.Preguntas.Add(pregunta);
            }
        }

        private async Task<int> ObtenerEstatusChecklistAsync(
            int checklistArranqueId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT TOP (1)
    EstatusID
FROM dbo.Produccion_ChecklistArranque
WHERE ChecklistArranqueID = @ChecklistArranqueID
  AND Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@ChecklistArranqueID", SqlDbType.Int).Value =
                checklistArranqueId;

            var result = await cmd.ExecuteScalarAsync();

            if (result == null || result == DBNull.Value)
                return ProduccionChecklistEstatus.Cancelado;

            return Convert.ToInt32(result);
        }

        private async Task ActualizarRespuestaChecklistAsync(int checklistArranqueDetalleId, string? resultado, string? observaciones, bool confirmado, string? valorCapturado, int usuarioId, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
UPDATE dbo.Produccion_ChecklistArranqueDetalle
SET Resultado=@Resultado,Observaciones=@Observaciones,Confirmado=@Confirmado,ValorCapturado=@ValorCapturado,
    UsuarioRespuestaID=CASE WHEN @Confirmado=1 THEN @UsuarioID ELSE UsuarioRespuestaID END,
    FechaRespuesta=CASE WHEN @Confirmado=1 THEN GETDATE() ELSE FechaRespuesta END,
    UsuarioModificacionID=@UsuarioID,FechaModificacion=GETDATE()
WHERE ChecklistArranqueDetalleID=@ChecklistArranqueDetalleID AND Activo=1;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ChecklistArranqueDetalleID", SqlDbType.Int).Value = checklistArranqueDetalleId;
            cmd.Parameters.Add("@Resultado", SqlDbType.NVarChar, 10).Value = string.IsNullOrWhiteSpace(resultado) ? DBNull.Value : resultado.Trim();
            cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 500).Value = string.IsNullOrWhiteSpace(observaciones) ? DBNull.Value : observaciones.Trim();
            cmd.Parameters.Add("@Confirmado", SqlDbType.Bit).Value = confirmado;
            cmd.Parameters.Add("@ValorCapturado", SqlDbType.NVarChar, 100).Value = string.IsNullOrWhiteSpace(valorCapturado) ? DBNull.Value : valorCapturado.Trim();
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            await cmd.ExecuteNonQueryAsync();
        }

        private async Task ActualizarEncabezadoChecklistProduccionAsync(
            int checklistArranqueId,
            int estatusId,
            string? observacionesGenerales,
            bool enviadoACalidad,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
UPDATE dbo.Produccion_ChecklistArranque
SET
    EstatusID = @EstatusID,
    UsuarioProduccionID = @UsuarioID,
    FechaCapturaProduccion =
        CASE
            WHEN @EnviadoACalidad = 1 THEN GETDATE()
            ELSE FechaCapturaProduccion
        END,
    ObservacionesGenerales = @ObservacionesGenerales,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE ChecklistArranqueID = @ChecklistArranqueID
  AND Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@ChecklistArranqueID", SqlDbType.Int).Value =
                checklistArranqueId;

            cmd.Parameters.Add("@EstatusID", SqlDbType.Int).Value =
                estatusId;

            cmd.Parameters.Add("@EnviadoACalidad", SqlDbType.Bit).Value =
                enviadoACalidad;

            cmd.Parameters.Add("@ObservacionesGenerales", SqlDbType.NVarChar, 1000).Value =
                string.IsNullOrWhiteSpace(observacionesGenerales)
                    ? DBNull.Value
                    : observacionesGenerales.Trim();

            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                usuarioId;

            await cmd.ExecuteNonQueryAsync();
        }

        private async Task<bool> TienePreguntasProduccionSinRespuestaAsync(
    int checklistArranqueId,
    SqlConnection cn,
    SqlTransaction tx)
        {
            const string sql = @"
SELECT COUNT(1)
FROM dbo.Produccion_ChecklistArranqueDetalle d
INNER JOIN dbo.ERP_ChecklistArranquePreguntas p
    ON p.PreguntaID = d.PreguntaID
WHERE d.ChecklistArranqueID = @ChecklistArranqueID
  AND d.Activo = 1
  AND p.Activo = 1
  AND UPPER(ISNULL(p.Seccion,N'')) NOT LIKE N'%PARO%'
  AND ISNULL(p.EsPreguntaCalidad,0) = 0
  AND UPPER(LTRIM(RTRIM(ISNULL(p.GrupoResponsable,N'')))) NOT IN
  (
      N'CALIDAD',
      N'AUDITOR',
      N'AUDITOR DE CALIDAD'
  )
  AND UPPER(ISNULL(p.Seccion,N'')) NOT LIKE N'%CALIDAD%'
  AND UPPER(ISNULL(p.Seccion,N'')) NOT LIKE N'%AUDITOR%'
  AND UPPER(ISNULL(p.ResponsableSugerido,N'')) NOT LIKE N'%CALIDAD%'
  AND UPPER(ISNULL(p.ResponsableSugerido,N'')) NOT LIKE N'%AUDITOR%'
  AND
  (
      ISNULL(d.Confirmado,0) = 0
      OR NULLIF(LTRIM(RTRIM(ISNULL(d.Resultado,N''))),N'') IS NULL
  );";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add(
                "@ChecklistArranqueID",
                SqlDbType.Int).Value =
                checklistArranqueId;

            var resultado = await cmd.ExecuteScalarAsync();

            return resultado != null &&
                   resultado != DBNull.Value &&
                   Convert.ToInt32(resultado) > 0;
        }
        private async Task<bool> TieneNokSinObservacionAsync(
      int checklistArranqueId,
      SqlConnection cn,
      SqlTransaction tx)
        {
            const string sql = @"
SELECT COUNT(1)
FROM dbo.Produccion_ChecklistArranqueDetalle d
INNER JOIN dbo.ERP_ChecklistArranquePreguntas p
    ON p.PreguntaID = d.PreguntaID
WHERE d.ChecklistArranqueID = @ChecklistArranqueID
  AND d.Activo = 1
  AND p.Activo = 1
  AND UPPER(ISNULL(p.Seccion,N'')) NOT LIKE N'%PARO%'
  AND ISNULL(p.EsPreguntaCalidad,0) = 0
  AND UPPER(LTRIM(RTRIM(ISNULL(p.GrupoResponsable,N'')))) NOT IN
  (
      N'CALIDAD',
      N'AUDITOR',
      N'AUDITOR DE CALIDAD'
  )
  AND UPPER(ISNULL(p.Seccion,N'')) NOT LIKE N'%CALIDAD%'
  AND UPPER(ISNULL(p.Seccion,N'')) NOT LIKE N'%AUDITOR%'
  AND UPPER(ISNULL(p.ResponsableSugerido,N'')) NOT LIKE N'%CALIDAD%'
  AND UPPER(ISNULL(p.ResponsableSugerido,N'')) NOT LIKE N'%AUDITOR%'
  AND ISNULL(d.Confirmado,0) = 1
  AND
  (
      (
          UPPER(LTRIM(RTRIM(ISNULL(d.Resultado,N'')))) = N'NOK'
          AND ISNULL(p.RequiereObservacionSiNOK,0) = 1
      )
      OR
      (
          UPPER(LTRIM(RTRIM(ISNULL(d.Resultado,N'')))) IN (N'NA',N'N/A')
          AND ISNULL(p.RequiereObservacionSiNA,0) = 1
      )
  )
  AND NULLIF(
      LTRIM(RTRIM(ISNULL(d.Observaciones,N''))),
      N''
  ) IS NULL;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add(
                "@ChecklistArranqueID",
                SqlDbType.Int).Value =
                checklistArranqueId;

            var resultado = await cmd.ExecuteScalarAsync();

            return resultado != null &&
                   resultado != DBNull.Value &&
                   Convert.ToInt32(resultado) > 0;
        }



        private static string? NormalizarResultadoChecklist(string? resultado)
        {
            if (string.IsNullOrWhiteSpace(resultado))
                return null;

            var valor = resultado.Trim().ToUpperInvariant();

            if (valor == "OK")
                return "OK";

            if (valor == "NOK")
                return "NOK";

            if (valor == "NA" || valor == "N/A")
                return "NA";

            return "__INVALIDO__";
        }


        private async Task<int?> ObtenerEjecucionActivaPorProgramaAsync(
            int programaProduccionId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT TOP (1)
    EjecucionProduccionID
FROM dbo.Produccion_Ejecucion
WHERE ProgramaProduccionID = @ProgramaProduccionID
  AND Activo = 1
  AND EstatusID NOT IN (9, 99)
ORDER BY EjecucionProduccionID DESC;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value =
                programaProduccionId;

            var result = await cmd.ExecuteScalarAsync();

            return result == null || result == DBNull.Value
                ? null
                : Convert.ToInt32(result);
        }

        private async Task<ProgramaParaProduccion?> ObtenerProgramaParaIniciarAsync(
     int programaProduccionId,
     SqlConnection cn,
     SqlTransaction tx)
        {
            const string sql = @"
SELECT TOP (1)
    pp.ProgramaProduccionID,
    pp.SolicitudProduccionID,
    pp.SolicitudProduccionDetalleID,
    pp.ReleaseDetalleID,
    rd.ReleaseID,
    pp.MaquinaID,
    COALESCE(NULLIF(pp.MaquinaCodigo,N''),maq.Codigo) AS MaquinaCodigo,
    COALESCE(NULLIF(pp.MaquinaNombre,N''),maq.Nombre) AS MaquinaNombre,
    pp.ParteID,
    pp.NumeroParte,
    pp.ReferenciaSAP,
    pp.DesignacionDescripcionSAP AS DescripcionParte,
    pp.MoldeID,
    pp.MoldeCodigo,
    CONVERT(INT,ISNULL(pp.CantidadProgramada,0)) AS CantidadPlaneada,
    pp.FechaInicioProgramada,
    pp.FechaFinProgramada,
    pp.Cambio,
    pp.Arranque,
    CASE
        WHEN pp.Cambio IS NOT NULL
         AND pp.Arranque IS NOT NULL
         AND pp.Cambio<pp.Arranque
            THEN 1
        ELSE 0
    END AS EsCambioMolde,
    opPrincipal.PersonaID AS OperadorPrincipalPlaneadoID,
    opPrincipal.NombreCompleto AS OperadorPrincipalPlaneadoNombre,
    opAuxiliar.PersonaID AS OperadorAuxiliarID,
    opAuxiliar.NombreCompleto AS OperadorAuxiliarNombre
FROM dbo.Planeacion_ProgramaProduccion pp WITH(UPDLOCK,HOLDLOCK)
INNER JOIN dbo.SolicitudesProduccion s
    ON s.SolicitudProduccionID=pp.SolicitudProduccionID
   AND s.Activo=1
INNER JOIN dbo.SolicitudesProduccionDetalle sd
    ON sd.SolicitudProduccionDetalleID=pp.SolicitudProduccionDetalleID
   AND sd.SolicitudProduccionID=pp.SolicitudProduccionID
   AND sd.Activo=1
LEFT JOIN dbo.Planeacion_ReleaseDetalle rd
    ON rd.ReleaseDetalleID=pp.ReleaseDetalleID
LEFT JOIN dbo.ERP_Maquinas maq
    ON maq.MaquinaID=pp.MaquinaID
OUTER APPLY
(
    SELECT TOP (1)
        po.PersonaID,
        LTRIM(RTRIM(
            ISNULL(p.Nombre,N'')+N' '+
            ISNULL(p.ApellidoPaterno,N'')+N' '+
            ISNULL(p.ApellidoMaterno,N'')
        )) AS NombreCompleto
    FROM dbo.Planeacion_ProgramaOperadores po
    INNER JOIN dbo.Persona p
        ON p.PersonaID=po.PersonaID
       AND ISNULL(p.EsColaboradorActivo,1)=1
       AND UPPER(LTRIM(RTRIM(ISNULL(p.Puesto,N''))))=N'OPERADOR'
    WHERE po.ProgramaProduccionID=pp.ProgramaProduccionID
      AND po.Activo=1
      AND UPPER(LTRIM(RTRIM(ISNULL(po.RolOperador,N''))))=N'PRINCIPAL'
    ORDER BY po.ProgramaOperadorID
) opPrincipal
OUTER APPLY
(
    SELECT TOP (1)
        po.PersonaID,
        LTRIM(RTRIM(
            ISNULL(p.Nombre,N'')+N' '+
            ISNULL(p.ApellidoPaterno,N'')+N' '+
            ISNULL(p.ApellidoMaterno,N'')
        )) AS NombreCompleto
    FROM dbo.Planeacion_ProgramaOperadores po
    INNER JOIN dbo.Persona p
        ON p.PersonaID=po.PersonaID
       AND ISNULL(p.EsColaboradorActivo,1)=1
       AND UPPER(LTRIM(RTRIM(ISNULL(p.Puesto,N''))))=N'OPERADOR'
    WHERE po.ProgramaProduccionID=pp.ProgramaProduccionID
      AND po.Activo=1
      AND UPPER(LTRIM(RTRIM(ISNULL(po.RolOperador,N''))))=N'AUXILIAR'
    ORDER BY po.ProgramaOperadorID
) opAuxiliar
WHERE pp.ProgramaProduccionID=@ProgramaProduccionID
  AND pp.Activo=1
  AND pp.SolicitudProduccionID IS NOT NULL
  AND pp.SolicitudProduccionID>0
  AND pp.SolicitudProduccionDetalleID IS NOT NULL
  AND pp.SolicitudProduccionDetalleID>0
  AND ISNULL(pp.EstatusID,1) NOT IN(3,4,5,8,9,99);";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;
            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync())
                return null;
            return new ProgramaParaProduccion
            {
                ProgramaProduccionID = Entero(rd, "ProgramaProduccionID"),
                SolicitudProduccionID = NullableEntero(rd, "SolicitudProduccionID"),
                SolicitudProduccionDetalleID = NullableEntero(rd, "SolicitudProduccionDetalleID"),
                ReleaseID = NullableEntero(rd, "ReleaseID"),
                ReleaseDetalleID = NullableEntero(rd, "ReleaseDetalleID"),
                MaquinaID = NullableEntero(rd, "MaquinaID"),
                MaquinaCodigo = TextoNullable(rd, "MaquinaCodigo"),
                MaquinaNombre = TextoNullable(rd, "MaquinaNombre"),
                ParteID = NullableEntero(rd, "ParteID"),
                NumeroParte = TextoNullable(rd, "NumeroParte"),
                ReferenciaSAP = TextoNullable(rd, "ReferenciaSAP"),
                DescripcionParte = TextoNullable(rd, "DescripcionParte"),
                MoldeID = NullableEntero(rd, "MoldeID"),
                MoldeCodigo = TextoNullable(rd, "MoldeCodigo"),
                CantidadPlaneada = NullableEntero(rd, "CantidadPlaneada"),
                FechaInicioProgramada = NullableFecha(rd, "FechaInicioProgramada"),
                FechaFinProgramada = NullableFecha(rd, "FechaFinProgramada"),
                Cambio = NullableTiempo(rd, "Cambio"),
                Arranque = NullableTiempo(rd, "Arranque"),
                EsCambioMolde = Booleano(rd, "EsCambioMolde"),
                OperadorPrincipalPlaneadoID = NullableEntero(rd, "OperadorPrincipalPlaneadoID"),
                OperadorPrincipalPlaneadoNombre = TextoNullable(rd, "OperadorPrincipalPlaneadoNombre"),
                OperadorAuxiliarID = NullableEntero(rd, "OperadorAuxiliarID"),
                OperadorAuxiliarNombre = TextoNullable(rd, "OperadorAuxiliarNombre")
            };
        }

        private async Task SincronizarOFEnEjecucionDesdeProgramaAsync(
    ProduccionEjecucionVm ejecucion,
    int usuarioId,
    SqlConnection cn,
    SqlTransaction tx)
        {
            if (ejecucion == null)
                throw new ArgumentNullException(nameof(ejecucion));
            if (ejecucion.EjecucionProduccionID <= 0)
                throw new InvalidOperationException("La ejecución de producción no es válida.");
            if (ejecucion.ProgramaProduccionID <= 0)
                throw new InvalidOperationException("La ejecución no tiene un programa de producción válido.");
            const string sql = @"
DECLARE @SolicitudProduccionID INT;
DECLARE @SolicitudProduccionDetalleID INT;
DECLARE @ReleaseID INT;
DECLARE @ReleaseDetalleID INT;

SELECT TOP (1)
    @SolicitudProduccionID=pp.SolicitudProduccionID,
    @SolicitudProduccionDetalleID=pp.SolicitudProduccionDetalleID,
    @ReleaseID=COALESCE(pp.ReleaseID,rd.ReleaseID),
    @ReleaseDetalleID=pp.ReleaseDetalleID
FROM dbo.Planeacion_ProgramaProduccion pp WITH(UPDLOCK,HOLDLOCK)
LEFT JOIN dbo.Planeacion_ReleaseDetalle rd
    ON rd.ReleaseDetalleID=pp.ReleaseDetalleID
WHERE pp.ProgramaProduccionID=@ProgramaProduccionID
  AND pp.Activo=1;

IF @SolicitudProduccionID IS NULL OR @SolicitudProduccionID<=0
BEGIN
    THROW 51501,
        'El programa todavía no tiene una OF generada. Genera la OF desde Planeación antes de continuar en Producción.',
        1;
END;

IF @SolicitudProduccionDetalleID IS NULL OR @SolicitudProduccionDetalleID<=0
BEGIN
    THROW 51502,
        'El programa tiene OF, pero no tiene un detalle de OF relacionado. Revisa la generación de la OF desde Planeación.',
        1;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.SolicitudesProduccion s
    WHERE s.SolicitudProduccionID=@SolicitudProduccionID
      AND s.Activo=1
)
BEGIN
    THROW 51503,
        'La OF relacionada con el programa no existe o ya no está activa.',
        1;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.SolicitudesProduccionDetalle d
    WHERE d.SolicitudProduccionDetalleID=@SolicitudProduccionDetalleID
      AND d.SolicitudProduccionID=@SolicitudProduccionID
      AND d.Activo=1
)
BEGIN
    THROW 51504,
        'El detalle relacionado con la OF no existe, no pertenece a la OF o ya no está activo.',
        1;
END;

UPDATE dbo.Produccion_Ejecucion
SET
    SolicitudProduccionID=@SolicitudProduccionID,
    SolicitudProduccionDetalleID=@SolicitudProduccionDetalleID,
    ReleaseID=COALESCE(ReleaseID,@ReleaseID),
    ReleaseDetalleID=COALESCE(ReleaseDetalleID,@ReleaseDetalleID),
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=GETDATE()
WHERE EjecucionProduccionID=@EjecucionProduccionID
  AND ProgramaProduccionID=@ProgramaProduccionID
  AND Activo=1
  AND
  (
       SolicitudProduccionID IS NULL
    OR SolicitudProduccionID<>@SolicitudProduccionID
    OR SolicitudProduccionDetalleID IS NULL
    OR SolicitudProduccionDetalleID<>@SolicitudProduccionDetalleID
  );

SELECT
    @SolicitudProduccionID AS SolicitudProduccionID,
    @SolicitudProduccionDetalleID AS SolicitudProduccionDetalleID,
    @ReleaseID AS ReleaseID,
    @ReleaseDetalleID AS ReleaseDetalleID;";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucion.EjecucionProduccionID;
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = ejecucion.ProgramaProduccionID;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync())
                throw new InvalidOperationException("No fue posible sincronizar la OF de la ejecución.");
            ejecucion.SolicitudProduccionID =
                rd["SolicitudProduccionID"] == DBNull.Value
                    ? null
                    : Convert.ToInt32(rd["SolicitudProduccionID"]);
            ejecucion.SolicitudProduccionDetalleID =
                rd["SolicitudProduccionDetalleID"] == DBNull.Value
                    ? null
                    : Convert.ToInt32(rd["SolicitudProduccionDetalleID"]);
            ejecucion.ReleaseID =
                rd["ReleaseID"] == DBNull.Value
                    ? ejecucion.ReleaseID
                    : Convert.ToInt32(rd["ReleaseID"]);
            ejecucion.ReleaseDetalleID =
                rd["ReleaseDetalleID"] == DBNull.Value
                    ? ejecucion.ReleaseDetalleID
                    : Convert.ToInt32(rd["ReleaseDetalleID"]);
        }

        private async Task<int> InsertarEjecucionAsync(
            ProgramaParaProduccion programa,
            int cantidadPlaneadaEjecucion,
            int? operadorId,
            string? operadorNombre,
            int? operadorAuxiliarId,
            string? operadorAuxiliarNombre,
            int? tecnicoProduccionId,
            string? tecnicoProduccionNombre,
            string? observaciones,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            if (cantidadPlaneadaEjecucion < 0)
                throw new InvalidOperationException("La cantidad planeada de la ejecución no puede ser negativa.");

            const string sql = @"
DECLARE @Ids TABLE
(
    EjecucionProduccionID INT NOT NULL
);

INSERT INTO dbo.Produccion_Ejecucion
(
    ProgramaProduccionID,
    SolicitudProduccionID,
    SolicitudProduccionDetalleID,
    ReleaseID,
    ReleaseDetalleID,
    MaquinaID,
    MaquinaCodigo,
    MaquinaNombre,
    ParteID,
    NumeroParte,
    ReferenciaSAP,
    DescripcionParte,
    MoldeID,
    MoldeCodigo,
    OperadorID,
    OperadorNombre,
    OperadorAuxiliarID,
    OperadorAuxiliarNombre,
    TecnicoProduccionID,
    TecnicoProduccionNombre,
    EsCambioMolde,
    FechaCambioMoldeProgramada,
    FechaArranqueProgramada,
    FechaInicioReal,
    CantidadPlaneada,
    CantidadOKTotal,
    CantidadSospechosaTotal,
    CantidadScrapTotal,
    EstatusID,
    Observaciones,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
OUTPUT INSERTED.EjecucionProduccionID
INTO @Ids(EjecucionProduccionID)
VALUES
(
    @ProgramaProduccionID,
    @SolicitudProduccionID,
    @SolicitudProduccionDetalleID,
    @ReleaseID,
    @ReleaseDetalleID,
    @MaquinaID,
    @MaquinaCodigo,
    @MaquinaNombre,
    @ParteID,
    @NumeroParte,
    @ReferenciaSAP,
    @DescripcionParte,
    @MoldeID,
    @MoldeCodigo,
    @OperadorID,
    @OperadorNombre,
    @OperadorAuxiliarID,
    @OperadorAuxiliarNombre,
    @TecnicoProduccionID,
    @TecnicoProduccionNombre,
    @EsCambioMolde,
    @FechaCambioMoldeProgramada,
    @FechaArranqueProgramada,
    GETDATE(),
    @CantidadPlaneada,
    0,
    0,
    0,
    @EstatusID,
    @Observaciones,
    @UsuarioID,
    GETDATE(),
    1
);

SELECT TOP(1)
    EjecucionProduccionID
FROM @Ids;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value =
                programa.ProgramaProduccionID;

            cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value =
                (object?)programa.SolicitudProduccionID ?? DBNull.Value;

            cmd.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value =
                (object?)programa.SolicitudProduccionDetalleID ?? DBNull.Value;

            cmd.Parameters.Add("@ReleaseID", SqlDbType.Int).Value =
                (object?)programa.ReleaseID ?? DBNull.Value;

            cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value =
                (object?)programa.ReleaseDetalleID ?? DBNull.Value;

            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value =
                (object?)programa.MaquinaID ?? DBNull.Value;

            cmd.Parameters.Add("@MaquinaCodigo", SqlDbType.NVarChar, 100).Value =
                (object?)programa.MaquinaCodigo ?? DBNull.Value;

            cmd.Parameters.Add("@MaquinaNombre", SqlDbType.NVarChar, 200).Value =
                (object?)programa.MaquinaNombre ?? DBNull.Value;

            cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value =
                (object?)programa.ParteID ?? DBNull.Value;

            cmd.Parameters.Add("@NumeroParte", SqlDbType.NVarChar, 120).Value =
                (object?)programa.NumeroParte ?? DBNull.Value;

            cmd.Parameters.Add("@ReferenciaSAP", SqlDbType.NVarChar, 150).Value =
                (object?)programa.ReferenciaSAP ?? DBNull.Value;

            cmd.Parameters.Add("@DescripcionParte", SqlDbType.NVarChar, 300).Value =
                (object?)programa.DescripcionParte ?? DBNull.Value;

            cmd.Parameters.Add("@MoldeID", SqlDbType.Int).Value =
                (object?)programa.MoldeID ?? DBNull.Value;

            cmd.Parameters.Add("@MoldeCodigo", SqlDbType.NVarChar, 100).Value =
                (object?)programa.MoldeCodigo ?? DBNull.Value;

            cmd.Parameters.Add("@OperadorID", SqlDbType.Int).Value =
                (object?)operadorId ?? DBNull.Value;

            cmd.Parameters.Add("@OperadorNombre", SqlDbType.NVarChar, 200).Value =
                string.IsNullOrWhiteSpace(operadorNombre)
                    ? DBNull.Value
                    : operadorNombre.Trim();

            cmd.Parameters.Add("@OperadorAuxiliarID", SqlDbType.Int).Value =
                (object?)operadorAuxiliarId ?? DBNull.Value;

            cmd.Parameters.Add("@OperadorAuxiliarNombre", SqlDbType.NVarChar, 200).Value =
                string.IsNullOrWhiteSpace(operadorAuxiliarNombre)
                    ? DBNull.Value
                    : operadorAuxiliarNombre.Trim();

            cmd.Parameters.Add("@TecnicoProduccionID", SqlDbType.Int).Value =
                (object?)tecnicoProduccionId ?? DBNull.Value;

            cmd.Parameters.Add("@TecnicoProduccionNombre", SqlDbType.NVarChar, 200).Value =
                string.IsNullOrWhiteSpace(tecnicoProduccionNombre)
                    ? DBNull.Value
                    : tecnicoProduccionNombre.Trim();

            cmd.Parameters.Add("@EsCambioMolde", SqlDbType.Bit).Value =
                programa.EsCambioMolde;

            DateTime? fechaCambioMoldeProgramada = null;
            DateTime? fechaArranqueProgramada = null;

            var fechaBase =
                programa.FechaInicioProgramada?.Date ??
                DateTime.Today;

            if (programa.Cambio.HasValue)
                fechaCambioMoldeProgramada =
                    fechaBase.Add(programa.Cambio.Value);

            if (programa.Arranque.HasValue)
                fechaArranqueProgramada =
                    fechaBase.Add(programa.Arranque.Value);
            else if (programa.FechaInicioProgramada.HasValue)
                fechaArranqueProgramada =
                    programa.FechaInicioProgramada.Value;

            cmd.Parameters.Add(
                "@FechaCambioMoldeProgramada",
                SqlDbType.DateTime).Value =
                fechaCambioMoldeProgramada.HasValue
                    ? fechaCambioMoldeProgramada.Value
                    : DBNull.Value;

            cmd.Parameters.Add(
                "@FechaArranqueProgramada",
                SqlDbType.DateTime).Value =
                fechaArranqueProgramada.HasValue
                    ? fechaArranqueProgramada.Value
                    : DBNull.Value;

            cmd.Parameters.Add("@CantidadPlaneada", SqlDbType.Int).Value =
                cantidadPlaneadaEjecucion;

            cmd.Parameters.Add("@EstatusID", SqlDbType.Int).Value =
                ProduccionEstatus.EnPreparacion;

            cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 500).Value =
                (object?)observaciones ?? DBNull.Value;

            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                usuarioId;

            var resultado = await cmd.ExecuteScalarAsync();

            if (resultado == null || resultado == DBNull.Value)
                throw new InvalidOperationException(
                    "La ejecución fue creada, pero no fue posible recuperar EjecucionProduccionID.");

            return Convert.ToInt32(resultado);
        }
        private static async Task<string?> ObtenerNombreOperadorProduccionAsync(int personaId, SqlConnection cn, SqlTransaction tx)
        {
            if (personaId <= 0) return null;
            const string sql = @"
SELECT TOP (1)
    LTRIM(RTRIM(
        ISNULL(Nombre,N'')+N' '+
        ISNULL(ApellidoPaterno,N'')+N' '+
        ISNULL(ApellidoMaterno,N'')
    )) AS NombreCompleto
FROM dbo.Persona
WHERE PersonaID=@PersonaID
  AND ISNULL(EsColaboradorActivo,1)=1
  AND UPPER(LTRIM(RTRIM(ISNULL(Puesto,N''))))=N'OPERADOR';";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@PersonaID", SqlDbType.Int).Value = personaId;
            var resultado = await cmd.ExecuteScalarAsync();
            if (resultado == null || resultado == DBNull.Value) return null;
            var nombre = resultado.ToString()?.Trim();
            return string.IsNullOrWhiteSpace(nombre) ? null : nombre;
        }

        private async Task<int> InsertarRegistroHoraAsync(
    ProduccionEjecucionVm ejecucion,
    ProduccionRegistroHoraPostVm vm,
    TimeSpan horaInicio,
    TimeSpan horaFin,
    int usuarioId,
    SqlConnection cn,
    SqlTransaction tx)
        {
            const string sql = @"
INSERT INTO dbo.Produccion_RegistroHora
(
    EjecucionProduccionID,
    ProgramaProduccionID,
    SolicitudProduccionID,
    MaquinaID,
    OperadorID,

    FechaProduccion,
    HoraInicio,
    HoraFin,

    CantidadOK,
    CantidadSospechosa,
    CantidadScrap,

    Observaciones,

    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
OUTPUT INSERTED.RegistroHoraID
VALUES
(
    @EjecucionProduccionID,
    @ProgramaProduccionID,
    @SolicitudProduccionID,
    @MaquinaID,
    @OperadorID,

    @FechaProduccion,
    @HoraInicio,
    @HoraFin,

    @CantidadOK,
    @CantidadSospechosa,
    @CantidadScrap,

    @Observaciones,

    @UsuarioID,
    GETDATE(),
    1
);";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add(
                "@EjecucionProduccionID",
                SqlDbType.Int).Value =
                ejecucion.EjecucionProduccionID;

            cmd.Parameters.Add(
                "@ProgramaProduccionID",
                SqlDbType.Int).Value =
                ejecucion.ProgramaProduccionID;

            cmd.Parameters.Add(
                "@SolicitudProduccionID",
                SqlDbType.Int).Value =
                (object?)ejecucion.SolicitudProduccionID ?? DBNull.Value;

            cmd.Parameters.Add(
                "@MaquinaID",
                SqlDbType.Int).Value =
                (object?)ejecucion.MaquinaID ?? DBNull.Value;

            cmd.Parameters.Add(
                "@OperadorID",
                SqlDbType.Int).Value =
                (object?)ejecucion.OperadorID ?? DBNull.Value;

            cmd.Parameters.Add(
                "@FechaProduccion",
                SqlDbType.Date).Value =
                vm.FechaProduccion.Date;

            cmd.Parameters.Add(
                "@HoraInicio",
                SqlDbType.Time).Value =
                horaInicio;

            cmd.Parameters.Add(
                "@HoraFin",
                SqlDbType.Time).Value =
                horaFin;

            cmd.Parameters.Add(
                "@CantidadOK",
                SqlDbType.Int).Value =
                vm.CantidadOK;

            cmd.Parameters.Add(
                "@CantidadSospechosa",
                SqlDbType.Int).Value =
                vm.CantidadSospechosa;

            cmd.Parameters.Add(
                "@CantidadScrap",
                SqlDbType.Int).Value =
                vm.CantidadScrap;

            cmd.Parameters.Add(
                "@Observaciones",
                SqlDbType.NVarChar,
                500).Value =
                string.IsNullOrWhiteSpace(vm.Observaciones)
                    ? DBNull.Value
                    : vm.Observaciones.Trim();

            cmd.Parameters.Add(
                "@UsuarioID",
                SqlDbType.Int).Value =
                usuarioId;

            var resultado = await cmd.ExecuteScalarAsync();

            if (resultado == null || resultado == DBNull.Value)
            {
                throw new InvalidOperationException(
                    "No fue posible obtener el identificador del registro por hora.");
            }

            return Convert.ToInt32(resultado);
        }

        private async Task<ProduccionMonitoreoTurnoAvisoVm?> ObtenerAvisoMonitoreoTurnoAsync(int checklistArranqueId, SqlConnection cn)
        {
            const string sql = @"
SELECT
    c.ChecklistArranqueID,
    c.EjecucionProduccionID,
    ISNULL(c.TurnoID,0) AS TurnoID,
    ISNULL(c.TurnoNombre,N'') AS TurnoNombre,
    ISNULL(c.FechaOperacion,CONVERT(DATE,c.FechaChecklist)) AS FechaOperacion,
    c.MaquinaCodigo,
    c.MaquinaNombre,
    c.EstatusID,
    COUNT(CASE WHEN p.PreguntaID IS NOT NULL THEN 1 END) AS TotalPreguntas,
    SUM(CASE WHEN p.PreguntaID IS NOT NULL AND ISNULL(d.Confirmado,0)=1 THEN 1 ELSE 0 END) AS TotalConfirmadas,
    CASE
        WHEN c.TecnicoEntregaPersonaID IS NOT NULL
         AND c.TecnicoRecibePersonaID IS NOT NULL
         AND c.FechaEntregaTurno IS NOT NULL
         AND c.FechaRecepcionTurno IS NOT NULL
        THEN CONVERT(BIT,1)
        ELSE CONVERT(BIT,0)
    END AS EntregaTurnoRegistrada
FROM dbo.Produccion_ChecklistArranque c
LEFT JOIN dbo.Produccion_ChecklistArranqueDetalle d
    ON d.ChecklistArranqueID=c.ChecklistArranqueID
   AND d.Activo=1
LEFT JOIN dbo.ERP_ChecklistArranquePreguntas p
    ON p.PreguntaID=d.PreguntaID
   AND p.Activo=1
   AND ISNULL(p.EsPreguntaCalidad,0)=0
WHERE c.ChecklistArranqueID=@ChecklistArranqueID
  AND c.CodigoFormato=N'GQ-F-PR01-14'
  AND c.TipoChecklist=N'MONITOREO_PERIFERICOS'
  AND c.Activo=1
GROUP BY
    c.ChecklistArranqueID,
    c.EjecucionProduccionID,
    c.TurnoID,
    c.TurnoNombre,
    c.FechaOperacion,
    c.FechaChecklist,
    c.MaquinaCodigo,
    c.MaquinaNombre,
    c.EstatusID,
    c.TecnicoEntregaPersonaID,
    c.TecnicoRecibePersonaID,
    c.FechaEntregaTurno,
    c.FechaRecepcionTurno;";

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@ChecklistArranqueID", SqlDbType.Int).Value = checklistArranqueId;

            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync()) return null;

            return new ProduccionMonitoreoTurnoAvisoVm
            {
                ChecklistArranqueID = Entero(rd, "ChecklistArranqueID"),
                EjecucionProduccionID = Entero(rd, "EjecucionProduccionID"),
                TurnoID = Entero(rd, "TurnoID"),
                TurnoNombre = TextoNullable(rd, "TurnoNombre") ?? string.Empty,
                FechaOperacion = Fecha(rd, "FechaOperacion"),
                MaquinaCodigo = TextoNullable(rd, "MaquinaCodigo"),
                MaquinaNombre = TextoNullable(rd, "MaquinaNombre"),
                EstatusID = Entero(rd, "EstatusID"),
                TotalPreguntas = Entero(rd, "TotalPreguntas"),
                TotalConfirmadas = Entero(rd, "TotalConfirmadas"),
                EntregaTurnoRegistrada = Booleano(rd, "EntregaTurnoRegistrada")
            };
        }

        private static async Task<List<ProduccionAlertaReprogramacionVm>>
    ObtenerAlertasReprogramacionProduccionAsync(
        int? maquinaId,
        SqlConnection cn)
        {
            var lista =
                new List<ProduccionAlertaReprogramacionVm>();

            const string sql = @"
SELECT TOP (30)
    h.ReprogramacionHistorialID,
    h.ProgramaProduccionID,
    h.ProgramaOrigenMovimientoID,

    h.MaquinaAnteriorID,
    ma.Codigo AS MaquinaAnteriorCodigo,
    ma.Nombre AS MaquinaAnteriorNombre,

    h.MaquinaNuevaID,
    mn.Codigo AS MaquinaNuevaCodigo,
    mn.Nombre AS MaquinaNuevaNombre,

    h.InicioAnterior,
    h.InicioNuevo,
    h.FinAnterior,
    h.FinNuevo,

    h.CambioAnterior,
    h.CambioNuevo,
    h.ArranqueAnterior,
    h.ArranqueNuevo,

    h.TipoMovimiento,
    ISNULL(h.EsMovimientoAutomatico, 0)
        AS EsMovimientoAutomatico,

    h.Motivo,
    h.UsuarioID,
    h.FechaCambio,

    pp.NumeroParte,
    pp.ReferenciaSAP,
    pp.DesignacionDescripcionSAP AS DescripcionParte,
    pp.MoldeCodigo

FROM dbo.Planeacion_ProgramaReprogramacionHistorial h

LEFT JOIN dbo.Planeacion_ProgramaProduccion pp
    ON pp.ProgramaProduccionID =
       h.ProgramaProduccionID

LEFT JOIN dbo.ERP_Maquinas ma
    ON ma.MaquinaID =
       h.MaquinaAnteriorID

LEFT JOIN dbo.ERP_Maquinas mn
    ON mn.MaquinaID =
       h.MaquinaNuevaID

WHERE h.FechaCambio >= DATEADD(HOUR, -24, GETDATE())

  AND
  (
      @MaquinaID IS NULL
      OR h.MaquinaAnteriorID = @MaquinaID
      OR h.MaquinaNuevaID = @MaquinaID
  )

ORDER BY
    CASE
        WHEN h.FechaCambio >= DATEADD(HOUR, -2, GETDATE())
            THEN 0
        ELSE 1
    END,
    h.FechaCambio DESC,
    h.ReprogramacionHistorialID DESC;";

            await using var cmd =
                new SqlCommand(sql, cn);

            cmd.Parameters.Add(
                "@MaquinaID",
                SqlDbType.Int).Value =
                maquinaId.HasValue && maquinaId.Value > 0
                    ? maquinaId.Value
                    : DBNull.Value;

            await using var rd =
                await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(
                    new ProduccionAlertaReprogramacionVm
                    {
                        ReprogramacionHistorialID =
                            Convert.ToInt32(
                                rd["ReprogramacionHistorialID"]),

                        ProgramaProduccionID =
                            Convert.ToInt32(
                                rd["ProgramaProduccionID"]),

                        ProgramaOrigenMovimientoID =
                            ObtenerEnteroNullable(
                                rd,
                                "ProgramaOrigenMovimientoID"),

                        MaquinaAnteriorID =
                            ObtenerEnteroNullable(
                                rd,
                                "MaquinaAnteriorID"),

                        MaquinaAnteriorCodigo =
                            ObtenerTextoNullable(
                                rd,
                                "MaquinaAnteriorCodigo"),

                        MaquinaAnteriorNombre =
                            ObtenerTextoNullable(
                                rd,
                                "MaquinaAnteriorNombre"),

                        MaquinaNuevaID =
                            ObtenerEnteroNullable(
                                rd,
                                "MaquinaNuevaID"),

                        MaquinaNuevaCodigo =
                            ObtenerTextoNullable(
                                rd,
                                "MaquinaNuevaCodigo"),

                        MaquinaNuevaNombre =
                            ObtenerTextoNullable(
                                rd,
                                "MaquinaNuevaNombre"),

                        InicioAnterior =
                            ObtenerFechaNullable(
                                rd,
                                "InicioAnterior"),

                        InicioNuevo =
                            ObtenerFechaNullable(
                                rd,
                                "InicioNuevo"),

                        FinAnterior =
                            ObtenerFechaNullable(
                                rd,
                                "FinAnterior"),

                        FinNuevo =
                            ObtenerFechaNullable(
                                rd,
                                "FinNuevo"),

                        CambioAnterior =
                            ObtenerTiempoNullable(
                                rd,
                                "CambioAnterior"),

                        CambioNuevo =
                            ObtenerTiempoNullable(
                                rd,
                                "CambioNuevo"),

                        ArranqueAnterior =
                            ObtenerTiempoNullable(
                                rd,
                                "ArranqueAnterior"),

                        ArranqueNuevo =
                            ObtenerTiempoNullable(
                                rd,
                                "ArranqueNuevo"),

                        TipoMovimiento =
                            ObtenerTextoNullable(
                                rd,
                                "TipoMovimiento"),

                        EsMovimientoAutomatico =
                            ObtenerBooleano(
                                rd,
                                "EsMovimientoAutomatico"),

                        Motivo =
                            ObtenerTextoNullable(
                                rd,
                                "Motivo"),

                        UsuarioID =
                            ObtenerEnteroNullable(
                                rd,
                                "UsuarioID"),

                        FechaCambio =
                            Convert.ToDateTime(
                                rd["FechaCambio"]),

                        NumeroParte =
                            ObtenerTextoNullable(
                                rd,
                                "NumeroParte"),

                        ReferenciaSAP =
                            ObtenerTextoNullable(
                                rd,
                                "ReferenciaSAP"),

                        DescripcionParte =
                            ObtenerTextoNullable(
                                rd,
                                "DescripcionParte"),

                        MoldeCodigo =
                            ObtenerTextoNullable(
                                rd,
                                "MoldeCodigo")
                    });
            }

            return lista;
        }
        private async Task<ProduccionChecklistResumenVm?> ObtenerResumenChecklistArranqueAsync(
            int ejecucionProduccionId,
            SqlConnection cn)
        {
            const string sql = @"
        SELECT TOP (1)
            c.ChecklistArranqueID,
            c.EjecucionProduccionID,
            c.ProgramaProduccionID,

            c.MaquinaCodigo,
            c.MaquinaNombre,

            c.ReferenciaSAP,
            c.NumeroParte,
            c.DescripcionParte,

            c.CodigoFormato,
            c.VersionFormato,
            c.EstatusID,
            c.FechaChecklist,

            COUNT(d.ChecklistArranqueDetalleID) AS TotalPreguntas,

            SUM(
                CASE
                    WHEN d.Resultado IS NOT NULL
                    AND LTRIM(RTRIM(d.Resultado)) <> ''
                        THEN 1
                    ELSE 0
                END
            ) AS TotalRespondidas,

            SUM(
                CASE
                    WHEN d.Resultado = 'NOK'
                        THEN 1
                    ELSE 0
                END
            ) AS TotalNOK

        FROM dbo.Produccion_ChecklistArranque c

        LEFT JOIN dbo.Produccion_ChecklistArranqueDetalle d
            ON d.ChecklistArranqueID = c.ChecklistArranqueID
        AND d.Activo = 1

        WHERE c.EjecucionProduccionID = @EjecucionProduccionID
        AND c.CodigoFormato = N'GQ-F-PR01-06'
        AND c.TipoChecklist = N'ARRANQUE_LIBERACION'
        AND c.Activo = 1

        GROUP BY
            c.ChecklistArranqueID,
            c.EjecucionProduccionID,
            c.ProgramaProduccionID,

            c.MaquinaCodigo,
            c.MaquinaNombre,

            c.ReferenciaSAP,
            c.NumeroParte,
            c.DescripcionParte,

            c.CodigoFormato,
            c.VersionFormato,
            c.EstatusID,
            c.FechaChecklist

        ORDER BY
            c.ChecklistArranqueID DESC;";

            await using var cmd = new SqlCommand(sql, cn);

            cmd.Parameters.Add(
                "@EjecucionProduccionID",
                SqlDbType.Int
            ).Value = ejecucionProduccionId;

            await using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
                return null;

            return new ProduccionChecklistResumenVm
            {
                ChecklistArranqueID =
                    Entero(rd, "ChecklistArranqueID"),

                EjecucionProduccionID =
                    Entero(rd, "EjecucionProduccionID"),

                ProgramaProduccionID =
                    Entero(rd, "ProgramaProduccionID"),

                MaquinaCodigo =
                    TextoNullable(rd, "MaquinaCodigo"),

                MaquinaNombre =
                    TextoNullable(rd, "MaquinaNombre"),

                ReferenciaSAP =
                    TextoNullable(rd, "ReferenciaSAP"),

                NumeroParte =
                    TextoNullable(rd, "NumeroParte"),

                DescripcionParte =
                    TextoNullable(rd, "DescripcionParte"),

                CodigoFormato =
                    TextoNullable(rd, "CodigoFormato")
                    ?? "GQ-F-PR01-06",

                VersionFormato =
                    TextoNullable(rd, "VersionFormato"),

                EstatusID =
                    Entero(rd, "EstatusID"),

                FechaChecklist =
                    Fecha(rd, "FechaChecklist"),

                TotalPreguntas =
                    Entero(rd, "TotalPreguntas"),

                TotalRespondidas =
                    Entero(rd, "TotalRespondidas"),

                TotalNOK =
                    Entero(rd, "TotalNOK")
            };
        }
        private async Task RecalcularTotalesEjecucionAsync(
            int ejecucionProduccionId,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
;WITH Totales AS
(
    SELECT
        EjecucionProduccionID,
        SUM(ISNULL(CantidadOK, 0)) AS OKTotal,
        SUM(ISNULL(CantidadSospechosa, 0)) AS SospechosaTotal,
        SUM(ISNULL(CantidadScrap, 0)) AS ScrapTotal
    FROM dbo.Produccion_RegistroHora
    WHERE EjecucionProduccionID = @EjecucionProduccionID
      AND Activo = 1
    GROUP BY EjecucionProduccionID
)
UPDATE e
SET
    e.CantidadOKTotal = ISNULL(t.OKTotal, 0),
    e.CantidadSospechosaTotal = ISNULL(t.SospechosaTotal, 0),
    e.CantidadScrapTotal = ISNULL(t.ScrapTotal, 0),
    e.UsuarioModificacionID = @UsuarioID,
    e.FechaModificacion = GETDATE()
FROM dbo.Produccion_Ejecucion e
LEFT JOIN Totales t
    ON t.EjecucionProduccionID = e.EjecucionProduccionID
WHERE e.EjecucionProduccionID = @EjecucionProduccionID;

UPDATE pp
SET
    pp.CantidadProducida = ISNULL(e.CantidadOKTotal, 0),
    pp.FechaInicioReal = ISNULL(pp.FechaInicioReal, e.FechaInicioReal),
    pp.HorasReales =
        CASE
            WHEN e.FechaInicioReal IS NOT NULL
                THEN CONVERT(DECIMAL(18,2), DATEDIFF(MINUTE, e.FechaInicioReal, GETDATE()) / 60.0)
            ELSE pp.HorasReales
        END,
    pp.UsuarioModificacionID = @UsuarioID,
    pp.FechaModificacion = GETDATE()
FROM dbo.Planeacion_ProgramaProduccion pp
INNER JOIN dbo.Produccion_Ejecucion e
    ON e.ProgramaProduccionID = pp.ProgramaProduccionID
WHERE e.EjecucionProduccionID = @EjecucionProduccionID
  AND pp.Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value =
                ejecucionProduccionId;

            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                usuarioId;

            await cmd.ExecuteNonQueryAsync();
        }

        private async Task MarcarProgramaEnProduccionAsync(
            int programaProduccionId,
            DateTime fechaInicioReal,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
UPDATE dbo.Planeacion_ProgramaProduccion
SET
    EstatusID = @EstatusID,
    FechaInicioReal = ISNULL(FechaInicioReal, @FechaInicioReal),
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE ProgramaProduccionID = @ProgramaProduccionID
  AND Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value =
                programaProduccionId;

            cmd.Parameters.Add("@EstatusID", SqlDbType.Int).Value =
                ProgramaProduccionEstatus.EnProduccion;

            cmd.Parameters.Add("@FechaInicioReal", SqlDbType.DateTime).Value =
                fechaInicioReal;

            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                usuarioId;

            await cmd.ExecuteNonQueryAsync();
        }
        private async Task<bool> EsReinicioSeriePendienteAsync(int ejecucionProduccionId, SqlConnection cn)
        {
            if (ejecucionProduccionId <= 0) return false;

            const string sql = @"
SELECT CASE
    WHEN EXISTS
    (
        SELECT 1
        FROM dbo.Produccion_Paros p
        OUTER APPLY
        (
            SELECT TOP(1)
                r.ReliberacionID,
                r.Resultado,
                r.FechaValidacion
            FROM dbo.Calidad_Reliberaciones r
            WHERE r.ParoID=p.ParoID
              AND r.EjecucionProduccionID=p.EjecucionProduccionID
              AND r.Activo=1
            ORDER BY r.NumeroReliberacion DESC,r.ReliberacionID DESC
        ) r
        WHERE p.EjecucionProduccionID=@EjecucionProduccionID
          AND p.Activo=1
          AND p.FechaFinParo IS NOT NULL
          AND
          (
              ISNULL(p.EsMayorA15Minutos,0)=1
              OR ISNULL(p.EsInterrupcionUrgente,0)=1
          )
          AND
          (
              (
                  ISNULL(p.EsInterrupcionUrgente,0)=1
                  AND r.ReliberacionID IS NULL
              )
              OR UPPER(LTRIM(RTRIM(ISNULL(r.Resultado,N''))))=N'AUTORIZADA'
          )
          AND NOT EXISTS
          (
              SELECT 1
              FROM dbo.Calidad_InspeccionHistorial h
              INNER JOIN dbo.Calidad_Inspecciones ci
                  ON ci.InspeccionID=h.InspeccionID
              WHERE ci.EjecucionProduccionID=@EjecucionProduccionID
                AND h.Movimiento=N'CONFIRMACION_INICIO_SERIE_PRODUCCION'
                AND h.FechaMovimiento>=COALESCE(r.FechaValidacion,p.FechaFinParo)
          )
    )
    THEN CAST(1 AS BIT)
    ELSE CAST(0 AS BIT)
END;";

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
            var resultado = await cmd.ExecuteScalarAsync();

            return resultado != null && resultado != DBNull.Value && Convert.ToBoolean(resultado);
        }
        private sealed class ContextoReinicioSerie
        {
            public int ParoID { get; set; }
            public int? ReliberacionID { get; set; }
            public DateTime FechaInicioParo { get; set; }
            public DateTime FechaFinParo { get; set; }
            public DateTime? FechaValidacionCalidad { get; set; }
            public bool EsInterrupcionUrgente { get; set; }
            public int? ProgramaUrgenteID { get; set; }
            public bool EsMayorA15Minutos { get; set; }
            public bool RequiereReliberacion => ReliberacionID.HasValue;
        }

     
        private async Task<ContextoReinicioSerie?> ObtenerContextoReinicioSerieAsync(int ejecucionProduccionId, SqlConnection cn, SqlTransaction tx)
        {
            if (ejecucionProduccionId <= 0) return null;

            const string sql = @"
SELECT TOP(1)
    p.ParoID,
    p.FechaInicioParo,
    p.FechaFinParo,
    ISNULL(p.EsMayorA15Minutos,0) AS EsMayorA15Minutos,
    ISNULL(p.EsInterrupcionUrgente,0) AS EsInterrupcionUrgente,
    p.ProgramaUrgenteID,
    r.ReliberacionID,
    r.FechaValidacion
FROM dbo.Produccion_Paros p WITH(UPDLOCK,HOLDLOCK)
OUTER APPLY
(
    SELECT TOP(1)
        cr.ReliberacionID,
        cr.Resultado,
        cr.FechaValidacion
    FROM dbo.Calidad_Reliberaciones cr WITH(UPDLOCK,HOLDLOCK)
    WHERE cr.ParoID=p.ParoID
      AND cr.EjecucionProduccionID=p.EjecucionProduccionID
      AND cr.Activo=1
    ORDER BY cr.NumeroReliberacion DESC,cr.ReliberacionID DESC
) r
WHERE p.EjecucionProduccionID=@EjecucionProduccionID
  AND p.Activo=1
  AND p.FechaFinParo IS NOT NULL
  AND
  (
      ISNULL(p.EsMayorA15Minutos,0)=1
      OR ISNULL(p.EsInterrupcionUrgente,0)=1
  )
  AND
  (
      (
          ISNULL(p.EsInterrupcionUrgente,0)=1
          AND r.ReliberacionID IS NULL
      )
      OR UPPER(LTRIM(RTRIM(ISNULL(r.Resultado,N''))))=N'AUTORIZADA'
  )
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.Calidad_InspeccionHistorial h
      INNER JOIN dbo.Calidad_Inspecciones ci
          ON ci.InspeccionID=h.InspeccionID
      WHERE ci.EjecucionProduccionID=@EjecucionProduccionID
        AND h.Movimiento=N'CONFIRMACION_INICIO_SERIE_PRODUCCION'
        AND h.FechaMovimiento>=COALESCE(r.FechaValidacion,p.FechaFinParo)
  )
ORDER BY p.FechaInicioParo DESC,p.ParoID DESC;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;

            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync()) return null;

            return new ContextoReinicioSerie
            {
                ParoID = Convert.ToInt32(rd["ParoID"]),
                ReliberacionID = rd["ReliberacionID"] == DBNull.Value ? null : Convert.ToInt32(rd["ReliberacionID"]),
                FechaInicioParo = Convert.ToDateTime(rd["FechaInicioParo"]),
                FechaFinParo = Convert.ToDateTime(rd["FechaFinParo"]),
                FechaValidacionCalidad = rd["FechaValidacion"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaValidacion"]),
                EsInterrupcionUrgente = rd["EsInterrupcionUrgente"] != DBNull.Value && Convert.ToBoolean(rd["EsInterrupcionUrgente"]),
                ProgramaUrgenteID = rd["ProgramaUrgenteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ProgramaUrgenteID"]),
                EsMayorA15Minutos = rd["EsMayorA15Minutos"] != DBNull.Value && Convert.ToBoolean(rd["EsMayorA15Minutos"])
            };
        }

        [HttpGet]
        public async Task<IActionResult> PanelCola(
    int? maquinaId = null,
    string? busqueda = null,
    DateTime? fechaDesde = null,
    DateTime? fechaHasta = null)
        {
            if (!UsuarioEnSesion())
            {
                return Unauthorized();
            }

     

            var vm =
                new ProduccionBandejaVm
                {
                    Busqueda =
                        string.IsNullOrWhiteSpace(busqueda)
                            ? null
                            : busqueda.Trim(),

                    MaquinaID =
                        maquinaId,

                    FechaDesde =
                        fechaDesde,

                    FechaHasta =
                        fechaHasta
                };

            await using var cn =
                new SqlConnection(
                    ConnectionString);

            await cn.OpenAsync();

        
            vm.Maquinas =
                await CargarMaquinasAsync(
                    cn);

     
            vm.ProgramasDisponibles =
                await ObtenerProgramasDisponiblesAsync(
                    busqueda,
                    null,
                    fechaDesde,
                    fechaHasta,
                    cn);

           
            vm.MaquinaID =
                maquinaId;

            ViewBag.OperadoresProduccion =
                await CargarOperadoresProduccionAsync(
                    cn);

            return PartialView(
                "_Cola",
                vm);
        }

        [HttpGet]
        public async Task<IActionResult> PanelEjecuciones(int? estatusId = null, int? maquinaId = null, string? busqueda = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null)
        {
            if (!UsuarioEnSesion())
                return Unauthorized();

            var vm = new ProduccionBandejaVm
            {
                Busqueda = string.IsNullOrWhiteSpace(busqueda) ? null : busqueda.Trim(),
                MaquinaID = maquinaId,
                EstatusID = estatusId,
                FechaDesde = fechaDesde,
                FechaHasta = fechaHasta
            };

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            vm.Maquinas = await CargarMaquinasAsync(cn);
            vm.Estatus = await CargarEstatusProduccionAsync(cn);
            vm.Ejecuciones = await ObtenerEjecucionesPanelAsync(estatusId, maquinaId, busqueda, fechaDesde, fechaHasta, cn);

            return PartialView("_Ejecuciones", vm);
        }

        private async Task<List<ProduccionEjecucionVm>> ObtenerEjecucionesPanelAsync(int? estatusId, int? maquinaId, string? busqueda, DateTime? fechaDesde, DateTime? fechaHasta, SqlConnection cn)
        {
            var lista = new List<ProduccionEjecucionVm>();

            const string sql = @"
SELECT
    e.EjecucionProduccionID,
    e.ProgramaProduccionID,
    e.SolicitudProduccionID,
    e.SolicitudProduccionDetalleID,
    e.ReleaseID,
    e.ReleaseDetalleID,
    e.MaquinaID,
    e.MaquinaCodigo,
    e.MaquinaNombre,
    e.ParteID,
    e.NumeroParte,
    e.ReferenciaSAP,
    e.DescripcionParte,
    e.MoldeID,
    e.MoldeCodigo,
    ISNULL(e.EsCambioMolde,0) AS EsCambioMolde,
    e.OperadorID,
    e.OperadorNombre,
    e.OperadorAuxiliarID,
    e.OperadorAuxiliarNombre,
    ISNULL(e.OperadoresModificadosManual,0) AS OperadoresModificadosManual,
    e.MotivoCambioOperadores,
    e.FechaInicioReal,
    e.FechaFinReal,
    e.FechaLiberacionMaquina,
    e.UsuarioLiberacionMaquinaID,
    e.ObservacionesLiberacionMaquina,
    e.CantidadPlaneada,
    ISNULL(e.CantidadOKTotal,0) AS CantidadOKTotal,
    ISNULL(e.CantidadSospechosaTotal,0) AS CantidadSospechosaTotal,
    ISNULL(e.CantidadScrapTotal,0) AS CantidadScrapTotal,
    e.EstatusID,
    e.Observaciones,
    e.UsuarioCreacionID,
    e.FechaCreacion,
    e.UsuarioModificacionID,
    e.FechaModificacion,
    e.Activo,

    ci.InspeccionID AS InspeccionCalidadID,
    ci.Estado AS EstadoCalidad,
    ci.ResultadoCalidad,
    ci.Etiqueta AS EtiquetaCalidad,
    CAST(ISNULL(ci.Liberado,0) AS BIT) AS CalidadLiberado,
    CAST(ISNULL(ci.RequiereReliberacion,0) AS BIT) AS RequiereReliberacion,
    CAST(ISNULL(ci.ConfiguracionInvalidada,0) AS BIT) AS ConfiguracionCalidadInvalidada,
    ci.FechaNotificacionCalidad,
    histArranque.FechaMovimiento AS FechaAutorizacionPrearranque,
    histLiberacion.FechaMovimiento AS FechaLiberacionProduccion,

    CASE
        WHEN UPPER(LTRIM(RTRIM(ISNULL(rel.Resultado,N''))))=N'AUTORIZADA'
             AND confirmacionReinicio.FechaMovimiento IS NOT NULL
            THEN NULL
        ELSE rel.ReliberacionID
    END AS ReliberacionID,

    CASE
        WHEN UPPER(LTRIM(RTRIM(ISNULL(rel.Resultado,N''))))=N'AUTORIZADA'
             AND confirmacionReinicio.FechaMovimiento IS NOT NULL
            THEN NULL
        ELSE rel.NumeroReliberacion
    END AS NumeroReliberacion,

    CASE
        WHEN UPPER(LTRIM(RTRIM(ISNULL(rel.Resultado,N''))))=N'AUTORIZADA'
             AND confirmacionReinicio.FechaMovimiento IS NOT NULL
            THEN NULL
        ELSE rel.Resultado
    END AS ResultadoReliberacion,

    CASE
        WHEN UPPER(LTRIM(RTRIM(ISNULL(rel.Resultado,N''))))=N'AUTORIZADA'
             AND confirmacionReinicio.FechaMovimiento IS NOT NULL
            THEN NULL
        ELSE rel.FechaSolicitud
    END AS FechaSolicitudReliberacion,

    CASE
        WHEN UPPER(LTRIM(RTRIM(ISNULL(rel.Resultado,N''))))=N'AUTORIZADA'
             AND confirmacionReinicio.FechaMovimiento IS NOT NULL
            THEN NULL
        ELSE rel.FechaValidacion
    END AS FechaValidacionReliberacion,

    CAST(CASE WHEN paro.ParoID IS NULL THEN 0 ELSE 1 END AS BIT) AS TieneParoAbierto,
    paro.ParoID AS ParoAbiertoID,
    paro.FechaInicioParo AS FechaInicioParoAbierto,
    CAST(
        CASE
            WHEN paro.ParoID IS NULL THEN 0
            WHEN ISNULL(paro.EsMayorA15Minutos,0)=1 THEN 1
            WHEN DATEDIFF(MINUTE,paro.FechaInicioParo,GETDATE())>15 THEN 1
            ELSE 0
        END
    AS BIT) AS ParoAbiertoMayorA15Minutos

FROM dbo.Produccion_Ejecucion e

OUTER APPLY
(
    SELECT TOP (1)
        ci0.InspeccionID,
        ci0.Estado,
        ci0.ResultadoCalidad,
        ci0.Etiqueta,
        ci0.Liberado,
        ci0.RequiereReliberacion,
        ci0.ConfiguracionInvalidada,
        ci0.FechaNotificacionCalidad
    FROM dbo.Calidad_Inspecciones ci0
    WHERE ci0.EjecucionProduccionID=e.EjecucionProduccionID
      AND ISNULL(ci0.Estado,N'')<>N'CERRADA'
    ORDER BY ci0.InspeccionID DESC
) ci

OUTER APPLY
(
    SELECT TOP (1)
        r.ReliberacionID,
        r.NumeroReliberacion,
        r.Resultado,
        r.FechaSolicitud,
        r.FechaValidacion
    FROM dbo.Calidad_Reliberaciones r
    WHERE r.EjecucionProduccionID=e.EjecucionProduccionID
      AND r.InspeccionID=ci.InspeccionID
      AND r.Activo=1
    ORDER BY r.NumeroReliberacion DESC,r.ReliberacionID DESC
) rel

OUTER APPLY
(
    SELECT TOP (1)
        h.FechaMovimiento
    FROM dbo.Calidad_InspeccionHistorial h
    WHERE h.InspeccionID=ci.InspeccionID
      AND h.Movimiento=N'CONFIRMACION_INICIO_SERIE_PRODUCCION'
      AND rel.ReliberacionID IS NOT NULL
      AND h.FechaMovimiento>=COALESCE(rel.FechaValidacion,rel.FechaSolicitud)
    ORDER BY h.FechaMovimiento DESC
) confirmacionReinicio

OUTER APPLY
(
    SELECT TOP (1)
        h.FechaMovimiento
    FROM dbo.Calidad_InspeccionHistorial h
    WHERE h.InspeccionID=ci.InspeccionID
      AND h.EstadoNuevo=N'ARRANQUE_AUTORIZADO'
    ORDER BY h.FechaMovimiento DESC
) histArranque

OUTER APPLY
(
    SELECT TOP (1)
        h.FechaMovimiento
    FROM dbo.Calidad_InspeccionHistorial h
    WHERE h.InspeccionID=ci.InspeccionID
      AND h.EstadoNuevo=N'PRODUCCION_LIBERADA'
    ORDER BY h.FechaMovimiento DESC
) histLiberacion

OUTER APPLY
(
    SELECT TOP (1)
        p.ParoID,
        p.FechaInicioParo,
        p.EsMayorA15Minutos
    FROM dbo.Produccion_Paros p
    WHERE p.EjecucionProduccionID=e.EjecucionProduccionID
      AND p.Activo=1
      AND p.FechaFinParo IS NULL
    ORDER BY p.FechaInicioParo DESC,p.ParoID DESC
) paro

WHERE e.Activo=1
  AND e.EstatusID IN(@Pendiente,@EnPreparacion,@EnProduccion,@Pausado)
  AND (@MaquinaID IS NULL OR e.MaquinaID=@MaquinaID)
  AND (@EstatusID IS NULL OR e.EstatusID=@EstatusID)
  AND (@FechaDesde IS NULL OR CONVERT(DATE,e.FechaCreacion)>=@FechaDesde)
  AND (@FechaHasta IS NULL OR CONVERT(DATE,e.FechaCreacion)<=@FechaHasta)
  AND
  (
      @Busqueda IS NULL
      OR e.MaquinaCodigo LIKE N'%'+@Busqueda+N'%'
      OR e.MaquinaNombre LIKE N'%'+@Busqueda+N'%'
      OR e.NumeroParte LIKE N'%'+@Busqueda+N'%'
      OR e.ReferenciaSAP LIKE N'%'+@Busqueda+N'%'
      OR e.DescripcionParte LIKE N'%'+@Busqueda+N'%'
      OR e.MoldeCodigo LIKE N'%'+@Busqueda+N'%'
      OR e.OperadorNombre LIKE N'%'+@Busqueda+N'%'
      OR e.OperadorAuxiliarNombre LIKE N'%'+@Busqueda+N'%'
      OR ci.Estado LIKE N'%'+@Busqueda+N'%'
      OR rel.Resultado LIKE N'%'+@Busqueda+N'%'
      OR CONVERT(NVARCHAR(30),e.ProgramaProduccionID) LIKE N'%'+@Busqueda+N'%'
      OR CONVERT(NVARCHAR(30),e.SolicitudProduccionID) LIKE N'%'+@Busqueda+N'%'
  )
ORDER BY e.FechaCreacion DESC,e.EjecucionProduccionID DESC;";

            await using var cmd = new SqlCommand(sql, cn);

            cmd.Parameters.Add("@Pendiente", SqlDbType.Int).Value = ProduccionEstatus.Pendiente;
            cmd.Parameters.Add("@EnPreparacion", SqlDbType.Int).Value = ProduccionEstatus.EnPreparacion;
            cmd.Parameters.Add("@EnProduccion", SqlDbType.Int).Value = ProduccionEstatus.EnProduccion;
            cmd.Parameters.Add("@Pausado", SqlDbType.Int).Value = ProduccionEstatus.Pausado;
            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = maquinaId.HasValue && maquinaId.Value > 0 ? maquinaId.Value : DBNull.Value;
            cmd.Parameters.Add("@EstatusID", SqlDbType.Int).Value = estatusId.HasValue && estatusId.Value > 0 ? estatusId.Value : DBNull.Value;
            cmd.Parameters.Add("@FechaDesde", SqlDbType.Date).Value = fechaDesde.HasValue ? fechaDesde.Value.Date : DBNull.Value;
            cmd.Parameters.Add("@FechaHasta", SqlDbType.Date).Value = fechaHasta.HasValue ? fechaHasta.Value.Date : DBNull.Value;
            cmd.Parameters.Add("@Busqueda", SqlDbType.NVarChar, 200).Value = string.IsNullOrWhiteSpace(busqueda) ? DBNull.Value : busqueda.Trim();

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
                lista.Add(MapearEjecucion(rd));

            return lista
                .OrderBy(x => x.EstadoOperativoPrioridad)
                .ThenBy(x => x.MaquinaCodigo)
                .ThenByDescending(x => x.FechaCreacion)
                .ThenByDescending(x => x.EjecucionProduccionID)
                .ToList();
        }

        [HttpGet]
        public async Task<IActionResult> PanelProximos(
       int? maquinaId = null,
       string? busqueda = null,
       DateTime? fechaDesde = null,
       DateTime? fechaHasta = null)
        {
            if (!UsuarioEnSesion())
            {
                return Unauthorized();
            }

            var vm =
                new ProduccionBandejaVm
                {
                    Busqueda =
                        string.IsNullOrWhiteSpace(busqueda)
                            ? null
                            : busqueda.Trim(),

                    MaquinaID = maquinaId,
                    FechaDesde = fechaDesde,
                    FechaHasta = fechaHasta
                };

            await using var cn =
                new SqlConnection(ConnectionString);

            await cn.OpenAsync();

            vm.Maquinas =
                await CargarMaquinasAsync(cn);

            ViewBag.OperadoresProduccion =
                await CargarOperadoresProduccionAsync(cn);

            vm.ProgramasDisponibles =
                await ObtenerProgramasDisponiblesAsync(
                    busqueda,
                    maquinaId,
                    fechaDesde,
                    fechaHasta,
                    cn);

            var ahora =
                DateTime.Now;

            var limiteProximos =
                ahora.AddMinutes(15);

            var maquinasOcupadas =
                await ObtenerMaquinasOcupadasAsync(cn);

            ViewBag.MaquinasOcupadas =
                maquinasOcupadas;

            vm.ProximosAIniciar =
                vm.ProgramasDisponibles
                    .Where(x =>
                        x.PuedeIniciar &&
                        x.MaquinaID.HasValue &&
                        x.FechaInicioProgramada.HasValue &&
                        x.FechaInicioProgramada.Value <= limiteProximos)
                    .OrderBy(x =>
                        x.FechaInicioProgramada)
                    .ThenBy(x =>
                        x.MaquinaCodigo)
                    .ThenBy(x =>
                        x.ProgramaProduccionID)
                    .ToList();

            // NSQ_PREPARACION_MOLDE_PANEL_V1
            ViewBag.EstadosCambioMolde =
                await ObtenerEstadosCambioMoldeBandejaAsync(cn);

            return PartialView(
                "_Proximos",
                vm);
        }

        private static DateTime NormalizarFechaMinuto(
    DateTime value)
        {
            return new DateTime(
                value.Year,
                value.Month,
                value.Day,
                value.Hour,
                value.Minute,
                0,
                value.Kind);
        }

        private static string? UnirTextoProduccion(
    string? codigo,
    string? descripcion)
        {
            codigo = string.IsNullOrWhiteSpace(codigo)
                ? null
                : codigo.Trim();

            descripcion = string.IsNullOrWhiteSpace(descripcion)
                ? null
                : descripcion.Trim();

            if (codigo == null)
                return descripcion;

            if (descripcion == null)
                return codigo;

            if (string.Equals(codigo, descripcion, StringComparison.OrdinalIgnoreCase))
                return codigo;

            return codigo + " | " + descripcion;
        }

      
        private async Task MarcarProgramaEnPreparacionAsync(
    int programaProduccionId,
    int usuarioId,
    SqlConnection cn,
    SqlTransaction tx)
        {
            const string sql = @"
UPDATE dbo.Planeacion_ProgramaProduccion
SET
    EstatusID = @EstatusID,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE ProgramaProduccionID = @ProgramaProduccionID
  AND Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value =
                programaProduccionId;

            cmd.Parameters.Add("@EstatusID", SqlDbType.Int).Value =
                ProgramaProduccionEstatus.EnPreparacion;

            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                usuarioId;

            await cmd.ExecuteNonQueryAsync();
        }

        private async Task<ValidacionInicioSerieResultado> ValidarInicioSerieCalidadAsync(int ejecucionProduccionId, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
DECLARE @InspeccionID INT;
DECLARE @ChecklistArranqueID INT;
DECLARE @Estado NVARCHAR(50);
DECLARE @ResultadoCalidad NVARCHAR(50);
DECLARE @Etiqueta NVARCHAR(50);
DECLARE @Liberado BIT;
DECLARE @ConfiguracionInvalidada BIT;
DECLARE @RequiereReliberacion BIT;
DECLARE @EstatusChecklistID INT;
DECLARE @ReliberacionID INT;
DECLARE @ResultadoReliberacion NVARCHAR(50);

SELECT TOP (1)
    @InspeccionID=ci.InspeccionID,
    @ChecklistArranqueID=ci.ChecklistArranqueID,
    @Estado=ci.Estado,
    @ResultadoCalidad=ci.ResultadoCalidad,
    @Etiqueta=ci.Etiqueta,
    @Liberado=ISNULL(ci.Liberado,0),
    @ConfiguracionInvalidada=ISNULL(ci.ConfiguracionInvalidada,0),
    @RequiereReliberacion=ISNULL(ci.RequiereReliberacion,0)
FROM dbo.Calidad_Inspecciones ci WITH (UPDLOCK,HOLDLOCK)
WHERE ci.EjecucionProduccionID=@EjecucionProduccionID
  AND ISNULL(ci.Estado,N'')<>N'CERRADA'
ORDER BY ci.InspeccionID DESC;

IF @InspeccionID IS NULL
BEGIN
    SELECT CAST(0 AS BIT) AS Permitido,CAST(NULL AS INT) AS InspeccionID,N'No existe una inspección activa de Calidad para esta ejecución.' AS Mensaje;
    RETURN;
END;

SELECT @EstatusChecklistID=c.EstatusID
FROM dbo.Produccion_ChecklistArranque c WITH (UPDLOCK,HOLDLOCK)
WHERE c.ChecklistArranqueID=@ChecklistArranqueID
  AND c.EjecucionProduccionID=@EjecucionProduccionID
  AND c.Activo=1;

IF @EstatusChecklistID IS NULL
BEGIN
    SELECT CAST(0 AS BIT) AS Permitido,@InspeccionID AS InspeccionID,N'La inspección de Calidad no tiene un checklist activo relacionado.' AS Mensaje;
    RETURN;
END;

IF @EstatusChecklistID<>@ChecklistValidado
BEGIN
    SELECT CAST(0 AS BIT) AS Permitido,@InspeccionID AS InspeccionID,N'El checklist todavía no ha sido validado por Calidad.' AS Mensaje;
    RETURN;
END;

IF @ConfiguracionInvalidada=1
BEGIN
    SELECT CAST(0 AS BIT) AS Permitido,@InspeccionID AS InspeccionID,N'La configuración autorizada fue invalidada. Calidad debe revisar nuevamente máquina, molde, material y operadores.' AS Mensaje;
    RETURN;
END;

SELECT TOP (1)
    @ReliberacionID=r.ReliberacionID,
    @ResultadoReliberacion=UPPER(LTRIM(RTRIM(ISNULL(r.Resultado,N''))))
FROM dbo.Calidad_Reliberaciones r WITH (UPDLOCK,HOLDLOCK)
WHERE r.InspeccionID=@InspeccionID
  AND r.EjecucionProduccionID=@EjecucionProduccionID
  AND r.Activo=1
ORDER BY r.NumeroReliberacion DESC,r.ReliberacionID DESC;

IF @RequiereReliberacion=1
BEGIN
    IF @ReliberacionID IS NULL
    BEGIN
        SELECT CAST(0 AS BIT) AS Permitido,@InspeccionID AS InspeccionID,N'La inspección requiere reliberación, pero no existe una solicitud asociada al paro.' AS Mensaje;
        RETURN;
    END;
    IF @ResultadoReliberacion=N'PENDIENTE'
    BEGIN
        SELECT CAST(0 AS BIT) AS Permitido,@InspeccionID AS InspeccionID,N'La reliberación de Calidad continúa pendiente.' AS Mensaje;
        RETURN;
    END;
    IF @ResultadoReliberacion=N'RECHAZADA'
    BEGIN
        SELECT CAST(0 AS BIT) AS Permitido,@InspeccionID AS InspeccionID,N'La última reliberación fue rechazada. Producción debe realizar ajustes y presentar nuevamente las primeras piezas.' AS Mensaje;
        RETURN;
    END;
    SELECT CAST(0 AS BIT) AS Permitido,@InspeccionID AS InspeccionID,N'Calidad todavía no ha concluido la reliberación.' AS Mensaje;
    RETURN;
END;

IF @ReliberacionID IS NOT NULL AND @ResultadoReliberacion<>N'AUTORIZADA'
BEGIN
    SELECT
        CAST(0 AS BIT) AS Permitido,
        @InspeccionID AS InspeccionID,
        CASE
            WHEN @ResultadoReliberacion=N'PENDIENTE' THEN N'La reliberación de Calidad continúa pendiente.'
            WHEN @ResultadoReliberacion=N'RECHAZADA' THEN N'La última reliberación fue rechazada. Deben corregirse las primeras piezas.'
            ELSE N'La última reliberación no se encuentra autorizada.'
        END AS Mensaje;
    RETURN;
END;

IF UPPER(LTRIM(RTRIM(ISNULL(@Estado,N''))))<>N'PRODUCCION_LIBERADA'
BEGIN
    SELECT CAST(0 AS BIT) AS Permitido,@InspeccionID AS InspeccionID,N'Calidad todavía no ha liberado la producción.' AS Mensaje;
    RETURN;
END;

IF ISNULL(@Liberado,0)=0
BEGIN
    SELECT CAST(0 AS BIT) AS Permitido,@InspeccionID AS InspeccionID,N'La inspección todavía no está marcada como liberada por Calidad.' AS Mensaje;
    RETURN;
END;

IF UPPER(LTRIM(RTRIM(ISNULL(@ResultadoCalidad,N''))))<>N'VERDE'
BEGIN
    SELECT CAST(0 AS BIT) AS Permitido,@InspeccionID AS InspeccionID,N'El resultado de Calidad no es verde.' AS Mensaje;
    RETURN;
END;

IF UPPER(LTRIM(RTRIM(ISNULL(@Etiqueta,N''))))<>N'VERDE'
BEGIN
    SELECT CAST(0 AS BIT) AS Permitido,@InspeccionID AS InspeccionID,N'Calidad todavía no ha asignado etiqueta verde.' AS Mensaje;
    RETURN;
END;

SELECT CAST(1 AS BIT) AS Permitido,@InspeccionID AS InspeccionID,N'Calidad autorizó el inicio o reinicio de la producción en serie.' AS Mensaje;";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
            cmd.Parameters.Add("@ChecklistValidado", SqlDbType.Int).Value = ProduccionChecklistEstatus.ValidadoPorCalidad;
            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync())
            {
                return new ValidacionInicioSerieResultado
                {
                    Permitido = false,
                    Mensaje = "No fue posible validar la liberación de Calidad."
                };
            }
            return new ValidacionInicioSerieResultado
            {
                Permitido = rd["Permitido"] != DBNull.Value && Convert.ToBoolean(rd["Permitido"]),
                InspeccionID = rd["InspeccionID"] == DBNull.Value ? null : Convert.ToInt32(rd["InspeccionID"]),
                Mensaje = rd["Mensaje"] == DBNull.Value ? "Calidad no ha autorizado el inicio de serie." : rd["Mensaje"].ToString() ?? "Calidad no ha autorizado el inicio de serie."
            };
        }
        private async Task MarcarProgramaTerminadoAsync(
            int programaProduccionId,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
UPDATE pp
SET
    pp.EstatusID = @EstatusID,
    pp.FechaFinReal = GETDATE(),
    pp.HorasReales =
        CASE
            WHEN pp.FechaInicioReal IS NOT NULL
                THEN CONVERT(DECIMAL(18,2), DATEDIFF(MINUTE, pp.FechaInicioReal, GETDATE()) / 60.0)
            ELSE pp.HorasReales
        END,
    pp.UsuarioModificacionID = @UsuarioID,
    pp.FechaModificacion = GETDATE()
FROM dbo.Planeacion_ProgramaProduccion pp
WHERE pp.ProgramaProduccionID = @ProgramaProduccionID
  AND pp.Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value =
                programaProduccionId;

            cmd.Parameters.Add("@EstatusID", SqlDbType.Int).Value =
                ProgramaProduccionEstatus.Terminado;

            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                usuarioId;

            await cmd.ExecuteNonQueryAsync();
        }

        private async Task CambiarEstatusEjecucionAsync(
            int ejecucionProduccionId,
            int estatusId,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
UPDATE dbo.Produccion_Ejecucion
SET
    EstatusID = @EstatusID,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE EjecucionProduccionID = @EjecucionProduccionID
  AND Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value =
                ejecucionProduccionId;

            cmd.Parameters.Add("@EstatusID", SqlDbType.Int).Value =
                estatusId;

            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                usuarioId;

            await cmd.ExecuteNonQueryAsync();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SolicitarReprogramacion(int programaProduccionId, string? motivo, string? observaciones)
        {
            if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");
            if (programaProduccionId <= 0)
            {
                TempData["Error"] = "No se recibió correctamente el programa de producción.";
                return RedirectToAction(nameof(Index));
            }
            motivo = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim();
            observaciones = string.IsNullOrWhiteSpace(observaciones) ? null : observaciones.Trim();
            if (string.IsNullOrWhiteSpace(motivo))
            {
                TempData["Error"] = "Selecciona el motivo de la solicitud de reprogramación.";
                return RedirectToAction(nameof(Index));
            }
            if (string.IsNullOrWhiteSpace(observaciones))
            {
                TempData["Error"] = "Explica brevemente por qué no puede iniciarse la producción en el horario programado.";
                return RedirectToAction(nameof(Index));
            }
            if (motivo.Length > 100)
            {
                TempData["Error"] = "El motivo de reprogramación no puede superar 100 caracteres.";
                return RedirectToAction(nameof(Index));
            }
            if (observaciones.Length > 500)
            {
                TempData["Error"] = "Las observaciones no pueden superar 500 caracteres.";
                return RedirectToAction(nameof(Index));
            }
            var usuarioId = ObtenerUsuarioID();
            if (usuarioId <= 0) return Unauthorized();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                const string sqlPrograma = @"
SELECT TOP(1)
    pp.ProgramaProduccionID,
    pp.SolicitudProduccionID,
    pp.MaquinaID,
    pp.FechaInicioProgramada,
    pp.FechaFinProgramada,
    ISNULL(pp.EstatusID,1) AS EstatusID,
    pp.FechaInicioReal
FROM dbo.Planeacion_ProgramaProduccion pp WITH(UPDLOCK,HOLDLOCK)
WHERE pp.ProgramaProduccionID=@ProgramaProduccionID
  AND pp.Activo=1;";
                int? solicitudProduccionId;
                int? maquinaId;
                DateTime fechaInicioProgramada;
                DateTime? fechaFinProgramada;
                int estatusId;
                DateTime? fechaInicioReal;
                await using (var cmd = new SqlCommand(sqlPrograma, cn, tx))
                {
                    cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;
                    await using var rd = await cmd.ExecuteReaderAsync();
                    if (!await rd.ReadAsync())
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = "El programa de producción ya no existe o dejó de estar activo.";
                        return RedirectToAction(nameof(Index));
                    }
                    solicitudProduccionId = rd["SolicitudProduccionID"] == DBNull.Value ? null : Convert.ToInt32(rd["SolicitudProduccionID"]);
                    maquinaId = rd["MaquinaID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaquinaID"]);
                    if (rd["FechaInicioProgramada"] == DBNull.Value)
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = "El programa no tiene fecha de inicio programada.";
                        return RedirectToAction(nameof(Index));
                    }
                    fechaInicioProgramada = Convert.ToDateTime(rd["FechaInicioProgramada"]);
                    fechaFinProgramada = rd["FechaFinProgramada"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaFinProgramada"]);
                    estatusId = Convert.ToInt32(rd["EstatusID"]);
                    fechaInicioReal = rd["FechaInicioReal"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaInicioReal"]);
                }
                if (fechaInicioProgramada >= DateTime.Now)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La reprogramación por atraso solo puede solicitarse cuando la hora programada de inicio ya fue superada.";
                    return RedirectToAction(nameof(Index));
                }
                if (fechaInicioReal.HasValue)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La producción ya inició. Esta opción solo aplica a programas atrasados que todavía no han comenzado.";
                    return RedirectToAction(nameof(Index));
                }
                if (estatusId != ProgramaProduccionEstatus.Pendiente)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "El programa ya cambió de estatus y no puede solicitar reprogramación desde esta bandeja.";
                    return RedirectToAction(nameof(Index));
                }
                const string sqlExiste = @"
SELECT TOP(1) SolicitudReprogramacionID
FROM dbo.Planeacion_SolicitudesReprogramacion WITH(UPDLOCK,HOLDLOCK)
WHERE ProgramaProduccionID=@ProgramaProduccionID
  AND Activo=1
  AND Estatus=N'PENDIENTE';";
                await using (var cmd = new SqlCommand(sqlExiste, cn, tx))
                {
                    cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;
                    var existente = await cmd.ExecuteScalarAsync();
                    if (existente != null && existente != DBNull.Value)
                    {
                        await tx.CommitAsync();
                        TempData["Info"] = "Este programa ya tiene una solicitud de reprogramación pendiente de revisión por Planeación.";
                        return RedirectToAction(nameof(Index));
                    }
                }
                const string sqlInsert = @"
INSERT INTO dbo.Planeacion_SolicitudesReprogramacion
(
    ProgramaProduccionID,SolicitudProduccionID,MaquinaID,
    FechaInicioProgramadaActual,FechaFinProgramadaActual,
    Motivo,Observaciones,Estatus,
    UsuarioSolicitanteID,FechaSolicitud,Activo,
    UsuarioCreacionID,FechaCreacion
)
VALUES
(
    @ProgramaProduccionID,@SolicitudProduccionID,@MaquinaID,
    @FechaInicioProgramada,@FechaFinProgramada,
    @Motivo,@Observaciones,N'PENDIENTE',
    @UsuarioID,SYSDATETIME(),1,
    @UsuarioID,SYSDATETIME()
);";
                await using (var cmd = new SqlCommand(sqlInsert, cn, tx))
                {
                    cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;
                    cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = (object?)solicitudProduccionId ?? DBNull.Value;
                    cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = (object?)maquinaId ?? DBNull.Value;
                    cmd.Parameters.Add("@FechaInicioProgramada", SqlDbType.DateTime2).Value = fechaInicioProgramada;
                    cmd.Parameters.Add("@FechaFinProgramada", SqlDbType.DateTime2).Value = (object?)fechaFinProgramada ?? DBNull.Value;
                    cmd.Parameters.Add("@Motivo", SqlDbType.NVarChar, 100).Value = motivo;
                    cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 500).Value = observaciones;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    await cmd.ExecuteNonQueryAsync();
                }
                await tx.CommitAsync();
                TempData["Success"] = "Solicitud de reprogramación enviada a Planeación. El programa permanece disponible hasta que Planeación determine la nueva programación.";
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627)
            {
                try { await tx.RollbackAsync(); } catch { }
                TempData["Info"] = "Este programa ya tiene una solicitud de reprogramación pendiente.";
            }
            catch (Exception ex)
            {
                try { await tx.RollbackAsync(); } catch { }
                TempData["Error"] = "No fue posible solicitar la reprogramación: " + ex.Message;
            }
            return RedirectToAction(nameof(Index));
        }

        private async Task<ProduccionPermisosUsuario> ObtenerPermisosProduccionUsuarioAsync(int usuarioId, SqlConnection cn, SqlTransaction? tx = null)
        {
            var permisos = new ProduccionPermisosUsuario { UsuarioID = usuarioId };
            if (usuarioId <= 0) return permisos;

            const string sql = @"
SELECT TOP(1)
    u.UsuarioID,
    u.PersonaID,
    u.RolID,
    LTRIM(RTRIM(CONCAT(ISNULL(p.Nombre,N''),N' ',ISNULL(p.ApellidoPaterno,N''),N' ',ISNULL(p.ApellidoMaterno,N'')))) AS NombreCompleto,
    LTRIM(RTRIM(ISNULL(p.Puesto,N''))) AS Puesto,
    CAST(CASE WHEN u.RolID=1 THEN 1 ELSE 0 END AS BIT) AS EsAdministradorERP,
    CAST(CASE WHEN ISNULL(p.EsColaboradorActivo,0)=1
                   AND UPPER(LTRIM(RTRIM(ISNULL(p.Puesto,N'')))) COLLATE Latin1_General_CI_AI LIKE N'%ENCARGAD%'
                   AND UPPER(LTRIM(RTRIM(ISNULL(p.Puesto,N'')))) COLLATE Latin1_General_CI_AI LIKE N'%PRODUC%'
              THEN 1 ELSE 0 END AS BIT) AS EsEncargadoProduccion,
    CAST(CASE WHEN ISNULL(p.EsColaboradorActivo,0)=1
                   AND UPPER(LTRIM(RTRIM(ISNULL(p.Puesto,N'')))) COLLATE Latin1_General_CI_AI LIKE N'%TECN%'
                   AND UPPER(LTRIM(RTRIM(ISNULL(p.Puesto,N'')))) COLLATE Latin1_General_CI_AI LIKE N'%PRODUC%'
              THEN 1 ELSE 0 END AS BIT) AS EsTecnicoProduccion,
    CAST(CASE WHEN ISNULL(p.EsColaboradorActivo,0)=1
                   AND UPPER(LTRIM(RTRIM(ISNULL(p.Puesto,N'')))) COLLATE Latin1_General_CI_AI LIKE N'%SMED%'
              THEN 1 ELSE 0 END AS BIT) AS EsSMED,
    CAST(CASE WHEN ISNULL(p.EsColaboradorActivo,0)=1
                   AND UPPER(LTRIM(RTRIM(ISNULL(p.Puesto,N'')))) COLLATE Latin1_General_CI_AI LIKE N'%AUXILIAR%'
                   AND UPPER(LTRIM(RTRIM(ISNULL(p.Puesto,N'')))) COLLATE Latin1_General_CI_AI LIKE N'%PRODUC%'
              THEN 1 ELSE 0 END AS BIT) AS EsAuxiliarProduccion,
    CAST(CASE WHEN ISNULL(p.EsColaboradorActivo,0)=1
                   AND (
                        UPPER(LTRIM(RTRIM(ISNULL(p.Puesto,N'')))) COLLATE Latin1_General_CI_AI=N'OPERADOR'
                        OR
                        (
                            UPPER(LTRIM(RTRIM(ISNULL(p.Puesto,N'')))) COLLATE Latin1_General_CI_AI LIKE N'%OPERADOR%'
                            AND UPPER(LTRIM(RTRIM(ISNULL(p.Puesto,N'')))) COLLATE Latin1_General_CI_AI LIKE N'%PRODUC%'
                        )
                   )
              THEN 1 ELSE 0 END AS BIT) AS EsOperadorProduccion
FROM dbo.Usuarios u
LEFT JOIN dbo.Persona p ON p.PersonaID=u.PersonaID
WHERE u.UsuarioID=@UsuarioID
  AND u.Activo=1;";

            await using var cmd = tx == null ? new SqlCommand(sql, cn) : new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;

            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync()) return permisos;

            permisos.PersonaID = rd["PersonaID"] == DBNull.Value ? null : Convert.ToInt32(rd["PersonaID"]);
            permisos.RolID = rd["RolID"] == DBNull.Value ? null : Convert.ToInt32(rd["RolID"]);
            permisos.Nombre = rd["NombreCompleto"]?.ToString()?.Trim() ?? string.Empty;
            permisos.Puesto = rd["Puesto"]?.ToString()?.Trim() ?? string.Empty;
            permisos.EsAdministradorERP = rd["EsAdministradorERP"] != DBNull.Value && Convert.ToBoolean(rd["EsAdministradorERP"]);
            permisos.EsEncargadoProduccion = rd["EsEncargadoProduccion"] != DBNull.Value && Convert.ToBoolean(rd["EsEncargadoProduccion"]);
            permisos.EsTecnicoProduccion = rd["EsTecnicoProduccion"] != DBNull.Value && Convert.ToBoolean(rd["EsTecnicoProduccion"]);
            permisos.EsSMED = rd["EsSMED"] != DBNull.Value && Convert.ToBoolean(rd["EsSMED"]);
            permisos.EsAuxiliarProduccion = rd["EsAuxiliarProduccion"] != DBNull.Value && Convert.ToBoolean(rd["EsAuxiliarProduccion"]);
            permisos.EsOperadorProduccion = rd["EsOperadorProduccion"] != DBNull.Value && Convert.ToBoolean(rd["EsOperadorProduccion"]);

            return permisos;
        }

        private async Task<bool> UsuarioPuedeGestionarCajasAsync(int usuarioId, SqlConnection cn, SqlTransaction? tx = null)
        {
            var permisos = await ObtenerPermisosProduccionUsuarioAsync(usuarioId, cn, tx);
            return permisos.PuedeGestionarCajas;
        }

        private async Task<bool> UsuarioPuedeCapturarHoraAsync(int usuarioId, SqlConnection cn, SqlTransaction? tx = null)
        {
            var permisos = await ObtenerPermisosProduccionUsuarioAsync(usuarioId, cn, tx);
            return permisos.PuedeCapturarHora;
        }

        private async Task<bool> UsuarioPuedeVerCapturasHoraAsync(int usuarioId, SqlConnection cn, SqlTransaction? tx = null)
        {
            var permisos = await ObtenerPermisosProduccionUsuarioAsync(usuarioId, cn, tx);
            return permisos.PuedeVerCapturasHora;
        }

        private async Task<bool> UsuarioPuedeGestionarChecklistArranqueAsync(int usuarioId, SqlConnection cn, SqlTransaction? tx = null)
        {
            var permisos = await ObtenerPermisosProduccionUsuarioAsync(usuarioId, cn, tx);
            return permisos.PuedeGestionarChecklistArranque;
        }

        private async Task<bool> UsuarioPuedeGestionarSMEDAsync(int usuarioId, SqlConnection cn, SqlTransaction? tx = null)
        {
            var permisos = await ObtenerPermisosProduccionUsuarioAsync(usuarioId, cn, tx);
            return permisos.PuedeGestionarSMED;
        }

        private async Task<bool> UsuarioPuedeGestionarMonitoreoPerifericosAsync(int usuarioId, SqlConnection cn, SqlTransaction? tx = null)
        {
            var permisos = await ObtenerPermisosProduccionUsuarioAsync(usuarioId, cn, tx);
            return permisos.PuedeGestionarMonitoreoPerifericos;
        }

        private async Task<bool> UsuarioPuedeGestionarSugerenciaCambioTurnoAsync(int usuarioId, SqlConnection cn, SqlTransaction? tx = null)
        {
            var permisos = await ObtenerPermisosProduccionUsuarioAsync(usuarioId, cn, tx);
            return permisos.PuedeGestionarSugerenciaCambioTurno;
        }

        private async Task<bool> UsuarioPuedeVerTodoProduccionAsync(int usuarioId, SqlConnection cn, SqlTransaction? tx = null)
        {
            var permisos = await ObtenerPermisosProduccionUsuarioAsync(usuarioId, cn, tx);
            return permisos.PuedeVerTodo;
        }

        private async Task CambiarEstatusProgramaAsync(
            int programaProduccionId,
            int estatusId,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
UPDATE dbo.Planeacion_ProgramaProduccion
SET
    EstatusID = @EstatusID,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE ProgramaProduccionID = @ProgramaProduccionID
  AND Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value =
                programaProduccionId;

            cmd.Parameters.Add("@EstatusID", SqlDbType.Int).Value =
                estatusId;

            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                usuarioId;

            await cmd.ExecuteNonQueryAsync();
        }


        private async Task<List<SelectListItem>> CargarMaquinasAsync(
            SqlConnection cn)
        {
            var lista = new List<SelectListItem>
            {
                new() { Value = "", Text = "Todas las máquinas" }
            };

            const string sql = @"
SELECT
    MaquinaID AS Id,
    Codigo + ' - ' + ISNULL(Nombre, '') AS Texto
FROM dbo.ERP_Maquinas
WHERE Activo = 1
ORDER BY Codigo;";

            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(new SelectListItem
                {
                    Value = rd["Id"].ToString(),
                    Text = rd["Texto"].ToString()
                });
            }

            return lista;
        }

        private async Task<List<SelectListItem>> CargarEstatusProduccionAsync(
     SqlConnection cn)
        {
            var lista = new List<SelectListItem>
    {
        new SelectListItem
        {
            Value = "",
            Text = "Todos los estatus"
        }
    };

            const string sql = @"
SELECT
    EstatusID,
    Nombre
FROM dbo.ERP_ProduccionEstatus
WHERE TipoEstatus = @TipoEstatus
  AND Activo = 1
ORDER BY Orden, EstatusID;";

            await using var cmd = new SqlCommand(sql, cn);

            cmd.Parameters.Add("@TipoEstatus", SqlDbType.NVarChar, 30).Value =
                ProduccionTipoEstatus.Ejecucion;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(new SelectListItem
                {
                    Value = rd["EstatusID"].ToString(),
                    Text = rd["Nombre"].ToString()
                });
            }

            return lista;
        }

        private async Task<List<SelectListItem>> CargarMotivosParoAsync(
            SqlConnection cn)
        {
            var lista = new List<SelectListItem>
            {
                new() { Value = "", Text = "-- Selecciona motivo --" }
            };

            const string sql = @"
SELECT
    MotivoParoID,
    Nombre
FROM dbo.ERP_MotivosParoProduccion
WHERE Activo = 1
ORDER BY Nombre;";

            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(new SelectListItem
                {
                    Value = rd["MotivoParoID"].ToString(),
                    Text = rd["Nombre"].ToString()
                });
            }

            return lista;
        }

        private async Task<string?> ObtenerMotivoParoNombreAsync(
            int motivoParoId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT TOP (1)
    Nombre
FROM dbo.ERP_MotivosParoProduccion
WHERE MotivoParoID = @MotivoParoID
  AND Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@MotivoParoID", SqlDbType.Int).Value =
                motivoParoId;

            var result = await cmd.ExecuteScalarAsync();

            return result == null || result == DBNull.Value
                ? null
                : result.ToString();
        }

        private async Task<bool> TieneValoresRequeridosSinCapturarAsync(
     int checklistArranqueId,
     SqlConnection cn,
     SqlTransaction tx)
        {
            const string sql = @"
SELECT COUNT(1)
FROM dbo.Produccion_ChecklistArranqueDetalle d
INNER JOIN dbo.ERP_ChecklistArranquePreguntas p
    ON p.PreguntaID = d.PreguntaID
WHERE d.ChecklistArranqueID = @ChecklistArranqueID
  AND d.Activo = 1
  AND p.Activo = 1
  AND UPPER(ISNULL(p.Seccion,N'')) NOT LIKE N'%PARO%'
  AND ISNULL(p.EsPreguntaCalidad,0) = 0
  AND UPPER(LTRIM(RTRIM(ISNULL(p.GrupoResponsable,N'')))) NOT IN
  (
      N'CALIDAD',
      N'AUDITOR',
      N'AUDITOR DE CALIDAD'
  )
  AND UPPER(ISNULL(p.Seccion,N'')) NOT LIKE N'%CALIDAD%'
  AND UPPER(ISNULL(p.Seccion,N'')) NOT LIKE N'%AUDITOR%'
  AND UPPER(ISNULL(p.ResponsableSugerido,N'')) NOT LIKE N'%CALIDAD%'
  AND UPPER(ISNULL(p.ResponsableSugerido,N'')) NOT LIKE N'%AUDITOR%'
  AND ISNULL(d.Confirmado,0) = 1
  AND UPPER(ISNULL(p.TipoRespuesta,N'')) IN
  (
      N'NUMERICO',
      N'ESTADO_Y_VALOR'
  )
  AND NULLIF(
      LTRIM(RTRIM(ISNULL(d.ValorCapturado,N''))),
      N''
  ) IS NULL;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add(
                "@ChecklistArranqueID",
                SqlDbType.Int).Value =
                checklistArranqueId;

            var resultado = await cmd.ExecuteScalarAsync();

            return resultado != null &&
                   resultado != DBNull.Value &&
                   Convert.ToInt32(resultado) > 0;
        }

        private async Task<bool> TieneParoAbiertoAsync(
            int ejecucionProduccionId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT COUNT(1)
FROM dbo.Produccion_Paros
WHERE EjecucionProduccionID = @EjecucionProduccionID
  AND Activo = 1
  AND FechaFinParo IS NULL;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value =
                ejecucionProduccionId;

            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }

        private async Task<List<ProduccionParoVm>> ObtenerParosAsync(int ejecucionProduccionId, SqlConnection cn)
        {
            var lista = new List<ProduccionParoVm>();
            if (ejecucionProduccionId <= 0) return lista;

            const string sql = @"
SELECT
    p.ParoID,
    p.EjecucionProduccionID,
    p.ProgramaProduccionID,
    p.SolicitudProduccionID,
    p.MaquinaID,
    p.OperadorID,
    p.FechaInicioParo,
    p.FechaFinParo,
    CASE
        WHEN p.FechaFinParo IS NOT NULL THEN ISNULL(p.DuracionMinutos,DATEDIFF(MINUTE,p.FechaInicioParo,p.FechaFinParo))
        ELSE DATEDIFF(MINUTE,p.FechaInicioParo,GETDATE())
    END AS DuracionMinutos,
    p.MotivoParoID,
    p.MotivoParoTexto,
    p.Descripcion,
    CAST(CASE
        WHEN ISNULL(p.EsMayorA15Minutos,0)=1 THEN 1
        WHEN DATEDIFF(MINUTE,p.FechaInicioParo,ISNULL(p.FechaFinParo,GETDATE()))>15 THEN 1
        ELSE 0
    END AS BIT) AS EsMayorA15Minutos,
    CAST(ISNULL(p.EsInterrupcionUrgente,0) AS BIT) AS EsInterrupcionUrgente,
    p.ProgramaUrgenteID,
    pu.SolicitudProduccionID AS SolicitudUrgenteID,
    p.UsuarioCreacionID,
    p.FechaCreacion,
    p.UsuarioModificacionID,
    p.FechaModificacion,
    p.Activo
FROM dbo.Produccion_Paros p
LEFT JOIN dbo.Planeacion_ProgramaProduccion pu
    ON pu.ProgramaProduccionID=p.ProgramaUrgenteID
   AND pu.Activo=1
WHERE p.EjecucionProduccionID=@EjecucionProduccionID
  AND p.Activo=1
ORDER BY p.FechaInicioParo DESC,p.ParoID DESC;";

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(new ProduccionParoVm
                {
                    ParoID = Convert.ToInt32(rd["ParoID"]),
                    EjecucionProduccionID = Convert.ToInt32(rd["EjecucionProduccionID"]),
                    ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),
                    SolicitudProduccionID = rd["SolicitudProduccionID"] == DBNull.Value ? null : Convert.ToInt32(rd["SolicitudProduccionID"]),
                    MaquinaID = rd["MaquinaID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaquinaID"]),
                    OperadorID = rd["OperadorID"] == DBNull.Value ? null : Convert.ToInt32(rd["OperadorID"]),
                    FechaInicioParo = Convert.ToDateTime(rd["FechaInicioParo"]),
                    FechaFinParo = rd["FechaFinParo"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaFinParo"]),
                    DuracionMinutos = rd["DuracionMinutos"] == DBNull.Value ? null : Convert.ToInt32(rd["DuracionMinutos"]),
                    MotivoParoID = rd["MotivoParoID"] == DBNull.Value ? null : Convert.ToInt32(rd["MotivoParoID"]),
                    MotivoParoTexto = rd["MotivoParoTexto"] == DBNull.Value ? null : rd["MotivoParoTexto"]?.ToString()?.Trim(),
                    Descripcion = rd["Descripcion"] == DBNull.Value ? null : rd["Descripcion"]?.ToString()?.Trim(),
                    EsMayorA15Minutos = rd["EsMayorA15Minutos"] != DBNull.Value && Convert.ToBoolean(rd["EsMayorA15Minutos"]),
                    EsInterrupcionUrgente = rd["EsInterrupcionUrgente"] != DBNull.Value && Convert.ToBoolean(rd["EsInterrupcionUrgente"]),
                    ProgramaUrgenteID = rd["ProgramaUrgenteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ProgramaUrgenteID"]),
                    SolicitudUrgenteID = rd["SolicitudUrgenteID"] == DBNull.Value ? null : Convert.ToInt32(rd["SolicitudUrgenteID"]),
                    UsuarioCreacionID = rd["UsuarioCreacionID"] == DBNull.Value ? null : Convert.ToInt32(rd["UsuarioCreacionID"]),
                    FechaCreacion = rd["FechaCreacion"] == DBNull.Value ? DateTime.MinValue : Convert.ToDateTime(rd["FechaCreacion"]),
                    UsuarioModificacionID = rd["UsuarioModificacionID"] == DBNull.Value ? null : Convert.ToInt32(rd["UsuarioModificacionID"]),
                    FechaModificacion = rd["FechaModificacion"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaModificacion"]),
                    Activo = rd["Activo"] != DBNull.Value && Convert.ToBoolean(rd["Activo"])
                });
            }

            return lista;
        }
        private async Task<ValidacionLiberacionMaquinaResultado> ValidarLiberacionMaquinaAsync(int ejecucionProduccionId, SqlConnection cn, SqlTransaction tx)
        {
            var resultado = new ValidacionLiberacionMaquinaResultado();
            const string sql = @"
SELECT TOP(1)
    e.EstatusID,
    e.MaquinaID,
    e.FechaLiberacionMaquina,
    ISNULL(e.CantidadPlaneada,0) AS CantidadPlaneada,
    TRY_CONVERT(DECIMAL(18,4),dt.ObjetivoHora) AS ObjetivoHora,
    ISNULL(reg.RegistrosNormales,0) AS RegistrosNormales,
    ISNULL(reg.MinutosNormalesCapturados,0) AS MinutosNormalesCapturados,
    CASE WHEN EXISTS
    (
        SELECT 1
        FROM dbo.Produccion_Paros p WITH(UPDLOCK,HOLDLOCK)
        WHERE p.EjecucionProduccionID=e.EjecucionProduccionID
          AND p.Activo=1
          AND p.FechaFinParo IS NULL
    ) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS TieneParoAbierto,
    CASE WHEN EXISTS
    (
        SELECT 1
        FROM dbo.Produccion_TiempoExtra te WITH(UPDLOCK,HOLDLOCK)
        WHERE te.EjecucionProduccionID=e.EjecucionProduccionID
          AND te.Activo=1
          AND te.FechaHoraFin IS NULL
          AND UPPER(LTRIM(RTRIM(ISNULL(te.Estado,N'')))) IN(N'EN_CURSO',N'PAUSADO')
    ) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS TieneTiempoExtraActivo
FROM dbo.Produccion_Ejecucion e WITH(UPDLOCK,HOLDLOCK)
OUTER APPLY
(
    SELECT TOP(1) dt0.ObjetivoHora
    FROM dbo.ERP_ParteDatosTecnicos dt0
    WHERE dt0.ParteID=e.ParteID
      AND dt0.Activo=1
    ORDER BY dt0.ParteDatoTecnicoID DESC
) dt
OUTER APPLY
(
    SELECT
        COUNT(1) AS RegistrosNormales,
        ISNULL
        (
            SUM
            (
                CASE
                    WHEN r.MinutosProductivos IS NOT NULL AND r.MinutosProductivos>0
                        THEN r.MinutosProductivos
                    WHEN r.HoraFin>=r.HoraInicio
                        THEN CONVERT(DECIMAL(18,2),DATEDIFF(MINUTE,r.HoraInicio,r.HoraFin))
                    ELSE CONVERT(DECIMAL(18,2),1440+DATEDIFF(MINUTE,r.HoraInicio,r.HoraFin))
                END
            ),
            0
        ) AS MinutosNormalesCapturados
    FROM dbo.Produccion_RegistroHora r WITH(UPDLOCK,HOLDLOCK)
    WHERE r.EjecucionProduccionID=e.EjecucionProduccionID
      AND r.Activo=1
      AND ISNULL(r.EsTiempoExtra,0)=0
) reg
WHERE e.EjecucionProduccionID=@EjecucionProduccionID
  AND e.Activo=1;";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync())
            {
                resultado.Mensaje = "No se encontró la ejecución de Producción.";
                return resultado;
            }

            var estatusId = Convert.ToInt32(rd["EstatusID"]);
            var maquinaId = rd["MaquinaID"] == DBNull.Value ? (int?)null : Convert.ToInt32(rd["MaquinaID"]);
            var fechaLiberacion = rd["FechaLiberacionMaquina"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(rd["FechaLiberacionMaquina"]);
            var cantidadPlaneada = rd["CantidadPlaneada"] == DBNull.Value ? 0 : Convert.ToInt32(rd["CantidadPlaneada"]);
            var objetivoHora = rd["ObjetivoHora"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(rd["ObjetivoHora"]);
            var registrosNormales = rd["RegistrosNormales"] == DBNull.Value ? 0 : Convert.ToInt32(rd["RegistrosNormales"]);
            var minutosNormalesCapturados = rd["MinutosNormalesCapturados"] == DBNull.Value ? 0m : Convert.ToDecimal(rd["MinutosNormalesCapturados"]);
            var tieneParoAbierto = rd["TieneParoAbierto"] != DBNull.Value && Convert.ToBoolean(rd["TieneParoAbierto"]);
            var tieneTiempoExtraActivo = rd["TieneTiempoExtraActivo"] != DBNull.Value && Convert.ToBoolean(rd["TieneTiempoExtraActivo"]);

            if (fechaLiberacion.HasValue)
            {
                resultado.Mensaje = $"La máquina ya fue liberada el {fechaLiberacion.Value:dd/MM/yyyy HH:mm}.";
                return resultado;
            }

            if (estatusId != ProduccionEstatus.EnProduccion)
            {
                resultado.Mensaje = "La máquina únicamente puede liberarse cuando la ejecución se encuentra en producción.";
                return resultado;
            }

            if (!maquinaId.HasValue || maquinaId.Value <= 0)
            {
                resultado.Mensaje = "La ejecución no tiene una máquina válida relacionada.";
                return resultado;
            }

            if (tieneParoAbierto)
            {
                resultado.Mensaje = "No puedes liberar la máquina mientras exista un paro abierto.";
                return resultado;
            }

            if (tieneTiempoExtraActivo)
            {
                resultado.Mensaje = "No puedes liberar la máquina mientras exista una sesión de tiempo extra abierta. Finaliza el tiempo extra y registra su último corte.";
                return resultado;
            }

            if (registrosNormales <= 0)
            {
                resultado.Mensaje = "No puedes liberar la máquina porque todavía no existe ninguna captura normal de producción.";
                return resultado;
            }

            if (cantidadPlaneada > 0)
            {
                if (!objetivoHora.HasValue || objetivoHora.Value <= 0)
                {
                    resultado.Mensaje = "No se puede validar la liberación porque la pieza no tiene un objetivo por hora válido.";
                    return resultado;
                }

                var minutosNormalesRequeridos = Math.Ceiling((decimal)cantidadPlaneada * 60m / objetivoHora.Value);
                if (minutosNormalesCapturados + 0.01m < minutosNormalesRequeridos)
                {
                    var minutosPendientes = Math.Max(0m, minutosNormalesRequeridos - minutosNormalesCapturados);
                    resultado.Mensaje = $"No puedes liberar la máquina. Se han capturado {minutosNormalesCapturados:0.##} de {minutosNormalesRequeridos:0.##} minuto(s) productivos normales requeridos. Faltan aproximadamente {minutosPendientes:0.##} minuto(s).";
                    return resultado;
                }
            }

            resultado.Permitido = true;
            resultado.Mensaje = "La máquina puede liberarse.";
            return resultado;
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LiberarMaquina(ProduccionLiberarMaquinaPostVm vm)
        {
            if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");

            if (vm.EjecucionProduccionID <= 0)
            {
                TempData["Error"] = "No se recibió una ejecución de Producción válida.";
                return RedirectToAction(nameof(Index));
            }

            var observaciones = string.IsNullOrWhiteSpace(vm.Observaciones)
                ? null
                : vm.Observaciones.Trim();

            if (observaciones?.Length > 500)
            {
                TempData["Error"] = "Las observaciones de liberación no pueden superar 500 caracteres.";
                return RedirectToAction(nameof(Detalle), new { id = vm.EjecucionProduccionID });
            }

            var usuarioId = ObtenerUsuarioID();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);

            try
            {
                var ejecucion = await ObtenerEjecucionAsync(
                    vm.EjecucionProduccionID,
                    cn,
                    tx);

                if (ejecucion == null)
                {
                    await tx.RollbackAsync();
                    return NotFound();
                }

                var parejaLhRh = await ObtenerParejaLhRhProduccionAsync(
                    ejecucion.ProgramaProduccionID,
                    cn,
                    tx);

                ProduccionEjecucionVm? ejecucionPareja = null;

                if (parejaLhRh != null)
                {
                    if (!parejaLhRh.EsCompatibleFisicamente)
                        throw new InvalidOperationException($"La producción LH/RH grupo {parejaLhRh.GrupoLhRh} ya no conserva la misma máquina, molde y ventana.");

                    if (!parejaLhRh.EjecucionParejaID.HasValue ||
                       parejaLhRh.EjecucionParejaID.Value <= 0)
                    {
                        throw new InvalidOperationException($"No se encontró la ejecución de {parejaLhRh.OFParejaTexto}. Una producción LH/RH no puede liberar la máquina parcialmente.");
                    }

                    ejecucionPareja = await ObtenerEjecucionAsync(
                        parejaLhRh.EjecucionParejaID.Value,
                        cn,
                        tx);

                    if (ejecucionPareja == null)
                        throw new InvalidOperationException($"No fue posible recuperar la ejecución de {parejaLhRh.OFParejaTexto}.");

                    if (ejecucionPareja.MaquinaID != ejecucion.MaquinaID)
                        throw new InvalidOperationException("Las dos ejecuciones LH/RH ya no pertenecen a la misma máquina.");
                }

                var validacion = await ValidarLiberacionMaquinaAsync(
                    ejecucion.EjecucionProduccionID,
                    cn,
                    tx);

                if (!validacion.Permitido)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = validacion.Mensaje;
                    return RedirectToAction(nameof(Detalle), new { id = vm.EjecucionProduccionID });
                }

                if (ejecucionPareja != null)
                {
                    var validacionPareja = await ValidarLiberacionMaquinaAsync(
                        ejecucionPareja.EjecucionProduccionID,
                        cn,
                        tx);

                    if (!validacionPareja.Permitido)
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = $"{parejaLhRh!.OFParejaTexto}: {validacionPareja.Mensaje}";
                        return RedirectToAction(nameof(Detalle), new { id = vm.EjecucionProduccionID });
                    }
                }

                const string sql = @"
UPDATE dbo.Produccion_Ejecucion
SET FechaLiberacionMaquina=GETDATE(),
    UsuarioLiberacionMaquinaID=@UsuarioID,
    ObservacionesLiberacionMaquina=@Observaciones,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=GETDATE()
WHERE Activo=1
  AND EstatusID=@EnProduccion
  AND FechaLiberacionMaquina IS NULL
  AND
  (
      EjecucionProduccionID=@EjecucionProduccionID
      OR
      (
          @EjecucionParejaID IS NOT NULL
          AND EjecucionProduccionID=@EjecucionParejaID
      )
  );

SELECT @@ROWCOUNT;";

                int filasLiberadas;

                await using (var cmd = new SqlCommand(sql, cn, tx))
                {
                    cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucion.EjecucionProduccionID;
                    cmd.Parameters.Add("@EjecucionParejaID", SqlDbType.Int).Value = (object?)ejecucionPareja?.EjecucionProduccionID ?? DBNull.Value;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 500).Value = (object?)observaciones ?? DBNull.Value;
                    cmd.Parameters.Add("@EnProduccion", SqlDbType.Int).Value = ProduccionEstatus.EnProduccion;
                    filasLiberadas = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                }

                var esperadas = ejecucionPareja == null ? 1 : 2;

                if (filasLiberadas != esperadas)
                {
                    throw new InvalidOperationException(
                        ejecucionPareja == null
                            ? "La ejecución cambió de estado o la máquina ya fue liberada."
                            : "Las ejecuciones LH/RH cambiaron de estado o una de ellas ya había liberado la máquina. No se permitirá una liberación parcial.");
                }

                var retornoInterrupcionUrgente = await ProcesarRetornoInterrupcionUrgenteAsync(
                    ejecucion.ProgramaProduccionID,
                    usuarioId,
                    cn,
                    tx);

                await tx.CommitAsync();

                var mensaje = ejecucionPareja == null
                    ? "Máquina liberada correctamente. La ejecución continúa abierta para cajas, Calidad, GP12 y cierre posterior."
                    : $"Máquina liberada correctamente para la producción conjunta LH/RH. Los Programas {ejecucion.ProgramaProduccionID} y {ejecucionPareja.ProgramaProduccionID} liberaron la máquina en la misma operación.";

                if (retornoInterrupcionUrgente != null)
                {
                    mensaje += $" Se cerró la interrupción urgente de la producción original iniciada por el Programa {retornoInterrupcionUrgente.ProgramaInterrumpidoID}. La OF interrumpida quedó EN PREPARACIÓN y todavía no está produciendo.";

                    if (retornoInterrupcionUrgente.RequiereReliberacion)
                    {
                        if (retornoInterrupcionUrgente.CambiaMolde)
                        {
                            mensaje += " Como la urgencia utilizó un molde diferente, debe realizarse el cambio de molde de retorno, presentar nuevamente primeras piezas y obtener autorización de Calidad.";
                        }
                        else
                        {
                            mensaje += " Como la interrupción superó 15 minutos, deben presentarse nuevamente primeras piezas y Calidad debe autorizar la reliberación.";
                        }

                        mensaje += " Después se deberá capturar el contador físico actual y pulsar Reiniciar serie.";
                    }
                    else
                    {
                        mensaje += " No requiere una nueva reliberación de Calidad porque conservó el mismo molde y no superó 15 minutos. Aun así, debe capturarse el contador físico actual y pulsar Reiniciar serie para excluir los ciclos fabricados durante la urgencia.";
                    }
                }

                TempData["Success"] = mensaje;
            }
            catch (Exception ex)
            {
                try
                {
                    await tx.RollbackAsync();
                }
                catch
                {
                }

                TempData["Error"] = "No fue posible liberar la máquina: " + ex.Message;
            }

            return RedirectToAction(nameof(Detalle), new { id = vm.EjecucionProduccionID });
        }
        private static ProduccionEjecucionVm MapearEjecucion(SqlDataReader rd)
        {
            return new ProduccionEjecucionVm
            {
                EjecucionProduccionID = Entero(rd, "EjecucionProduccionID"),
                ProgramaProduccionID = Entero(rd, "ProgramaProduccionID"),
                SolicitudProduccionID = NullableEntero(rd, "SolicitudProduccionID"),
                SolicitudProduccionDetalleID = NullableEntero(rd, "SolicitudProduccionDetalleID"),
                ReleaseID = NullableEntero(rd, "ReleaseID"),
                ReleaseDetalleID = NullableEntero(rd, "ReleaseDetalleID"),
                MaquinaID = NullableEntero(rd, "MaquinaID"),
                MaquinaCodigo = TextoNullable(rd, "MaquinaCodigo"),
                MaquinaNombre = TextoNullable(rd, "MaquinaNombre"),
                ParteID = NullableEntero(rd, "ParteID"),
                NumeroParte = TextoNullable(rd, "NumeroParte"),
                ReferenciaSAP = TextoNullable(rd, "ReferenciaSAP"),
                DescripcionParte = TextoNullable(rd, "DescripcionParte"),
                MoldeID = NullableEntero(rd, "MoldeID"),
                MoldeCodigo = TextoNullable(rd, "MoldeCodigo"),
                EsCambioMolde = Booleano(rd, "EsCambioMolde"),
                OperadorID = NullableEntero(rd, "OperadorID"),
                OperadorNombre = TextoNullable(rd, "OperadorNombre"),
                OperadorAuxiliarID = NullableEntero(rd, "OperadorAuxiliarID"),
                OperadorAuxiliarNombre = TextoNullable(rd, "OperadorAuxiliarNombre"),
                OperadoresModificadosManual = Booleano(rd, "OperadoresModificadosManual"),
                MotivoCambioOperadores = TextoNullable(rd, "MotivoCambioOperadores"),
                FechaInicioReal = NullableFecha(rd, "FechaInicioReal"),
                FechaFinReal = NullableFecha(rd, "FechaFinReal"),
                FechaLiberacionMaquina = TieneColumna(rd, "FechaLiberacionMaquina") ? NullableFecha(rd, "FechaLiberacionMaquina") : null,
                UsuarioLiberacionMaquinaID = TieneColumna(rd, "UsuarioLiberacionMaquinaID") ? NullableEntero(rd, "UsuarioLiberacionMaquinaID") : null,
                ObservacionesLiberacionMaquina = TieneColumna(rd, "ObservacionesLiberacionMaquina") ? TextoNullable(rd, "ObservacionesLiberacionMaquina") : null,
                CantidadPlaneada = NullableEntero(rd, "CantidadPlaneada"),
                CantidadOKTotal = Entero(rd, "CantidadOKTotal"),
                CantidadSospechosaTotal = Entero(rd, "CantidadSospechosaTotal"),
                CantidadScrapTotal = Entero(rd, "CantidadScrapTotal"),
                EstatusID = Entero(rd, "EstatusID"),
                Observaciones = TextoNullable(rd, "Observaciones"),
                UsuarioCreacionID = NullableEntero(rd, "UsuarioCreacionID"),
                FechaCreacion = Fecha(rd, "FechaCreacion"),
                UsuarioModificacionID = NullableEntero(rd, "UsuarioModificacionID"),
                FechaModificacion = NullableFecha(rd, "FechaModificacion"),
                Activo = Booleano(rd, "Activo"),

                InspeccionCalidadID = TieneColumna(rd, "InspeccionCalidadID") ? NullableEntero(rd, "InspeccionCalidadID") : null,
                EstadoCalidad = TieneColumna(rd, "EstadoCalidad") ? TextoNullable(rd, "EstadoCalidad") : null,
                ResultadoCalidad = TieneColumna(rd, "ResultadoCalidad") ? TextoNullable(rd, "ResultadoCalidad") : null,
                EtiquetaCalidad = TieneColumna(rd, "EtiquetaCalidad") ? TextoNullable(rd, "EtiquetaCalidad") : null,
                CalidadLiberado = TieneColumna(rd, "CalidadLiberado") && Booleano(rd, "CalidadLiberado"),
                RequiereReliberacion = TieneColumna(rd, "RequiereReliberacion") && Booleano(rd, "RequiereReliberacion"),
                ConfiguracionCalidadInvalidada = TieneColumna(rd, "ConfiguracionCalidadInvalidada") && Booleano(rd, "ConfiguracionCalidadInvalidada"),
                FechaNotificacionCalidad = TieneColumna(rd, "FechaNotificacionCalidad") ? NullableFecha(rd, "FechaNotificacionCalidad") : null,
                FechaAutorizacionPrearranque = TieneColumna(rd, "FechaAutorizacionPrearranque") ? NullableFecha(rd, "FechaAutorizacionPrearranque") : null,
                FechaLiberacionProduccion = TieneColumna(rd, "FechaLiberacionProduccion") ? NullableFecha(rd, "FechaLiberacionProduccion") : null,

                ReliberacionID = TieneColumna(rd, "ReliberacionID") ? NullableEntero(rd, "ReliberacionID") : null,
                NumeroReliberacion = TieneColumna(rd, "NumeroReliberacion") ? NullableEntero(rd, "NumeroReliberacion") : null,
                ResultadoReliberacion = TieneColumna(rd, "ResultadoReliberacion") ? TextoNullable(rd, "ResultadoReliberacion") : null,
                FechaSolicitudReliberacion = TieneColumna(rd, "FechaSolicitudReliberacion") ? NullableFecha(rd, "FechaSolicitudReliberacion") : null,
                FechaValidacionReliberacion = TieneColumna(rd, "FechaValidacionReliberacion") ? NullableFecha(rd, "FechaValidacionReliberacion") : null,

                TieneParoAbierto = TieneColumna(rd, "TieneParoAbierto") && Booleano(rd, "TieneParoAbierto"),
                ParoAbiertoID = TieneColumna(rd, "ParoAbiertoID") ? NullableEntero(rd, "ParoAbiertoID") : null,
                FechaInicioParoAbierto = TieneColumna(rd, "FechaInicioParoAbierto") ? NullableFecha(rd, "FechaInicioParoAbierto") : null,
                ParoAbiertoMayorA15Minutos = TieneColumna(rd, "ParoAbiertoMayorA15Minutos") && Booleano(rd, "ParoAbiertoMayorA15Minutos")
            };
        }
        private bool UsuarioEnSesion()
        {
            return HttpContext.Session.GetInt32("UsuarioID").HasValue;
        }

        private int ObtenerUsuarioID()
        {
            return HttpContext.Session.GetInt32("UsuarioID") ?? 0;
        }

        private static int Entero(SqlDataReader rd, string columna)
        {
            var ordinal = rd.GetOrdinal(columna);

            return rd.IsDBNull(ordinal)
                ? 0
                : Convert.ToInt32(rd.GetValue(ordinal));
        }

        private static int? NullableEntero(SqlDataReader rd, string columna)
        {
            var ordinal = rd.GetOrdinal(columna);

            return rd.IsDBNull(ordinal)
                ? null
                : Convert.ToInt32(rd.GetValue(ordinal));
        }

        private static DateTime Fecha(SqlDataReader rd, string columna)
        {
            var ordinal = rd.GetOrdinal(columna);

            return rd.IsDBNull(ordinal)
                ? DateTime.MinValue
                : Convert.ToDateTime(rd.GetValue(ordinal));
        }

        private static DateTime? NullableFecha(SqlDataReader rd, string columna)
        {
            var ordinal = rd.GetOrdinal(columna);

            return rd.IsDBNull(ordinal)
                ? null
                : Convert.ToDateTime(rd.GetValue(ordinal));
        }

        private static TimeSpan Tiempo(SqlDataReader rd, string columna)
        {
            var ordinal = rd.GetOrdinal(columna);

            if (rd.IsDBNull(ordinal))
                return TimeSpan.Zero;

            var value = rd.GetValue(ordinal);

            return value switch
            {
                TimeSpan time => time,
                DateTime date => date.TimeOfDay,
                _ => TimeSpan.Parse(value.ToString() ?? "00:00")
            };
        }
        private sealed class ValidacionLiberacionMaquinaResultado
        {
            public bool Permitido { get; set; }
            public string Mensaje { get; set; } = string.Empty;
        }
        private sealed class OperadorSugeridoProduccion
        {
            public int OperadorID { get; set; }
            public string OperadorNombre { get; set; } = string.Empty;
            public string? TurnoNombre { get; set; }
            public string? TurnoColor { get; set; }
            public int? EscalaAsignacionID { get; set; }
        }

        private async Task<OperadorSugeridoProduccion?> ObtenerOperadorSugeridoProduccionAsync(
            int maquinaId,
            DateTime fechaHora,
            SqlConnection cn,
            SqlTransaction? tx)
        {
            const string sql = @"
SELECT TOP (1)
    a.AsignacionID AS EscalaAsignacionID,
    a.PersonalID AS OperadorID,
    LTRIM(RTRIM(
        ISNULL(p.Nombre, '') + ' ' +
        ISNULL(p.ApellidoPaterno, '') + ' ' +
        ISNULL(p.ApellidoMaterno, '')
    )) AS OperadorNombre,
    et.Nombre AS TurnoNombre,
    et.Color AS TurnoColor
FROM dbo.RRHH_EscalaAsignaciones a
INNER JOIN dbo.RRHH_EscalasPersonal esc
    ON esc.EscalaID = a.EscalaID
   AND esc.Activo = 1
   AND esc.Estado = N'Publicada'
INNER JOIN dbo.Persona p
    ON p.PersonaID = a.PersonalID
INNER JOIN dbo.RRHH_EscalaTurnos et
    ON et.EscalaID = a.EscalaID
   AND et.EscalaTurnoID = a.EscalaTurnoID
WHERE a.Activo = 1
  AND a.MaquinaID = @MaquinaID
  AND CAST(@FechaHora AS date) >= CAST(a.FechaInicio AS date)
  AND CAST(@FechaHora AS date) <= CAST(a.FechaFin AS date)
  AND
  (
        ISNULL(et.EsFlexible, 0) = 1
     OR et.HoraInicio IS NULL
     OR et.HoraFin IS NULL
     OR
     (
            ISNULL(et.CruzaDiaSiguiente, 0) = 0
        AND CAST(@FechaHora AS time) >= et.HoraInicio
        AND CAST(@FechaHora AS time) < et.HoraFin
     )
     OR
     (
            ISNULL(et.CruzaDiaSiguiente, 0) = 1
        AND
        (
               CAST(@FechaHora AS time) >= et.HoraInicio
            OR CAST(@FechaHora AS time) < et.HoraFin
        )
     )
  )
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.RRHH_NovedadesPersonal n
      WHERE n.EscalaID = a.EscalaID
        AND n.PersonalID = a.PersonalID
        AND n.Activo = 1
        AND n.TipoNovedad IN (N'Baja', N'Incapacidad', N'Vacaciones')
        AND CAST(@FechaHora AS date) >= CAST(n.FechaInicio AS date)
        AND CAST(@FechaHora AS date) <= CAST(ISNULL(n.FechaFin, n.FechaInicio) AS date)
  )
ORDER BY
    et.Orden,
    a.AsignacionID DESC;";

            await using var cmd = tx == null
                ? new SqlCommand(sql, cn)
                : new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = maquinaId;
            cmd.Parameters.Add("@FechaHora", SqlDbType.DateTime).Value = fechaHora;

            await using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
                return null;

            return new OperadorSugeridoProduccion
            {
                OperadorID = Entero(rd, "OperadorID"),
                OperadorNombre = TextoNullable(rd, "OperadorNombre") ?? string.Empty,
                TurnoNombre = TextoNullable(rd, "TurnoNombre"),
                TurnoColor = TextoNullable(rd, "TurnoColor"),
                EscalaAsignacionID = NullableEntero(rd, "EscalaAsignacionID")
            };
        }

        [HttpGet]
        public async Task<IActionResult> OperadoresPorMaquinaFecha(
    int maquinaId,
    DateTime fechaHora)
        {
            if (!UsuarioEnSesion())
            {
                return Json(new
                {
                    ok = false,
                    mensaje = "La sesión terminó. Vuelve a iniciar sesión."
                });
            }

            if (maquinaId <= 0)
            {
                return Json(new
                {
                    ok = false,
                    mensaje = "No se recibió la máquina."
                });
            }

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var operador = await ObtenerOperadorSugeridoProduccionAsync(
                maquinaId,
                fechaHora,
                cn,
                null);

            if (operador == null)
            {
                return Json(new
                {
                    ok = true,
                    operadorID = (int?)null,
                    operadorNombre = "",
                    turnoNombre = "",
                    turnoColor = "",
                    mensaje = "No hay operador asignado en la escala RRHH para esta máquina y horario. Puedes seleccionar uno manualmente."
                });
            }

            return Json(new
            {
                ok = true,
                operadorID = operador.OperadorID,
                operadorNombre = operador.OperadorNombre,
                turnoNombre = operador.TurnoNombre,
                turnoColor = operador.TurnoColor,
                escalaAsignacionID = operador.EscalaAsignacionID,
                mensaje = "Operador sugerido desde escala RRHH."
            });
        }

        private async Task<List<SelectListItem>> CargarOperadoresProduccionAsync(SqlConnection cn)
        {
            var lista = new List<SelectListItem>
    {
        new SelectListItem
        {
            Value="",
            Text="-- Seleccionar operador --"
        }
    };
            const string sql = @"
SELECT
    PersonaID,
    LTRIM(RTRIM(
        ISNULL(Nombre,N'')+N' '+
        ISNULL(ApellidoPaterno,N'')+N' '+
        ISNULL(ApellidoMaterno,N'')
    )) AS NombreCompleto
FROM dbo.Persona
WHERE ISNULL(EsColaboradorActivo,1)=1
  AND UPPER(LTRIM(RTRIM(ISNULL(Puesto,N''))))=N'OPERADOR'
ORDER BY
    Nombre,
    ApellidoPaterno,
    ApellidoMaterno;";
            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                var personaId = Entero(rd, "PersonaID");
                var nombre = TextoNullable(rd, "NombreCompleto") ?? personaId.ToString();
                lista.Add(new SelectListItem
                {
                    Value = personaId.ToString(),
                    Text = nombre
                });
            }
            return lista;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CambiarOperadores(
       int ejecucionProduccionId,
       string? tipoCambio,
       int? personaNuevaId,
       string? motivoCambio)
        {
            if (!UsuarioEnSesion())
                return RedirectToAction("Login", "Login");

            if (ejecucionProduccionId <= 0)
            {
                TempData["Error"] = "No se recibió una ejecución válida.";
                return RedirectToAction(nameof(Index));
            }

            tipoCambio = tipoCambio?.Trim().ToUpperInvariant();

            if (tipoCambio != "PRINCIPAL" && tipoCambio != "AUXILIAR")
            {
                TempData["Error"] = "Selecciona si deseas cambiar al operador principal o al auxiliar.";
                return RedirectToAction(nameof(Detalle), new { id = ejecucionProduccionId });
            }

            if (!personaNuevaId.HasValue || personaNuevaId.Value <= 0)
            {
                TempData["Error"] =
                    tipoCambio == "PRINCIPAL"
                        ? "Selecciona el nuevo operador principal."
                        : "Selecciona el nuevo auxiliar.";

                return RedirectToAction(nameof(Detalle), new { id = ejecucionProduccionId });
            }

            motivoCambio = motivoCambio?.Trim();

            if (string.IsNullOrWhiteSpace(motivoCambio))
            {
                TempData["Error"] = "El motivo del cambio es obligatorio.";
                return RedirectToAction(nameof(Detalle), new { id = ejecucionProduccionId });
            }

            if (motivoCambio.Length > 1000)
            {
                TempData["Error"] = "El motivo del cambio no puede superar 1000 caracteres.";
                return RedirectToAction(nameof(Detalle), new { id = ejecucionProduccionId });
            }

            var usuarioId = ObtenerUsuarioID();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var tx =
                (SqlTransaction)await cn.BeginTransactionAsync(
                    IsolationLevel.Serializable);

            try
            {
                const string sqlEjecucion = @"
SELECT TOP (1)
    e.ProgramaProduccionID,
    e.EstatusID,
    e.OperadorID,
    e.OperadorNombre,
    e.OperadorAuxiliarID,
    e.OperadorAuxiliarNombre
FROM dbo.Produccion_Ejecucion e WITH(UPDLOCK,HOLDLOCK)
WHERE e.EjecucionProduccionID=@EjecucionProduccionID
  AND e.Activo=1;";

                int programaProduccionId;
                int estatusId;

                int? operadorPrincipalAnteriorId;
                int? operadorAuxiliarAnteriorId;

                string operadorPrincipalAnteriorNombre;
                string operadorAuxiliarAnteriorNombre;

                await using (var cmd = new SqlCommand(sqlEjecucion, cn, tx))
                {
                    cmd.Parameters.Add(
                        "@EjecucionProduccionID",
                        SqlDbType.Int).Value =
                        ejecucionProduccionId;

                    await using var rd = await cmd.ExecuteReaderAsync();

                    if (!await rd.ReadAsync())
                    {
                        await tx.RollbackAsync();

                        TempData["Error"] = "No se encontró la ejecución activa.";

                        return RedirectToAction(
                            nameof(Detalle),
                            new { id = ejecucionProduccionId });
                    }

                    programaProduccionId =
                        Convert.ToInt32(
                            rd["ProgramaProduccionID"]);

                    estatusId =
                        Convert.ToInt32(
                            rd["EstatusID"]);

                    operadorPrincipalAnteriorId =
                        rd["OperadorID"] == DBNull.Value
                            ? null
                            : Convert.ToInt32(
                                rd["OperadorID"]);

                    operadorAuxiliarAnteriorId =
                        rd["OperadorAuxiliarID"] == DBNull.Value
                            ? null
                            : Convert.ToInt32(
                                rd["OperadorAuxiliarID"]);

                    operadorPrincipalAnteriorNombre =
                        rd["OperadorNombre"] == DBNull.Value
                            ? "Sin asignar"
                            : rd["OperadorNombre"]?.ToString()?.Trim()
                              ?? "Sin asignar";

                    operadorAuxiliarAnteriorNombre =
                        rd["OperadorAuxiliarNombre"] == DBNull.Value
                            ? "Sin asignar"
                            : rd["OperadorAuxiliarNombre"]?.ToString()?.Trim()
                              ?? "Sin asignar";
                }

                if (estatusId != ProduccionEstatus.EnPreparacion &&
                   estatusId != ProduccionEstatus.EnProduccion &&
                   estatusId != ProduccionEstatus.Pausado)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "Solo puedes cambiar operadores mientras la ejecución esté en preparación, producción o pausa.";

                    return RedirectToAction(
                        nameof(Detalle),
                        new { id = ejecucionProduccionId });
                }

                var personaNuevaNombre =
                    await ObtenerPersonaNombreAsync(
                        personaNuevaId.Value,
                        cn,
                        tx);

                if (string.IsNullOrWhiteSpace(personaNuevaNombre))
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "La persona seleccionada no existe o ya no está activa.";

                    return RedirectToAction(
                        nameof(Detalle),
                        new { id = ejecucionProduccionId });
                }

                int? operadorPrincipalNuevoId =
                    operadorPrincipalAnteriorId;

                string operadorPrincipalNuevoNombre =
                    operadorPrincipalAnteriorNombre;

                int? operadorAuxiliarNuevoId =
                    operadorAuxiliarAnteriorId;

                string? operadorAuxiliarNuevoNombre =
                    operadorAuxiliarAnteriorId.HasValue
                        ? operadorAuxiliarAnteriorNombre
                        : null;

                if (tipoCambio == "PRINCIPAL")
                {
                    if (operadorPrincipalAnteriorId ==
                       personaNuevaId.Value)
                    {
                        await tx.CommitAsync();

                        TempData["Info"] =
                            "La persona seleccionada ya es el operador principal.";

                        return RedirectToAction(
                            nameof(Detalle),
                            new { id = ejecucionProduccionId });
                    }

                    if (operadorAuxiliarAnteriorId.HasValue &&
                       operadorAuxiliarAnteriorId.Value ==
                       personaNuevaId.Value)
                    {
                        await tx.RollbackAsync();

                        TempData["Error"] =
                            "La persona seleccionada actualmente está asignada como auxiliar. Principal y auxiliar no pueden ser la misma persona.";

                        return RedirectToAction(
                            nameof(Detalle),
                            new { id = ejecucionProduccionId });
                    }

                    var ejecucion =
                        await ObtenerEjecucionAsync(
                            ejecucionProduccionId,
                            cn,
                            tx);

                    if (ejecucion == null)
                    {
                        await tx.RollbackAsync();

                        TempData["Error"] =
                            "No fue posible recuperar la ejecución.";

                        return RedirectToAction(
                            nameof(Detalle),
                            new { id = ejecucionProduccionId });
                    }

                    var partePolivalencia =
                        await ResolverPartePolivalenciaProgramaAsync(
                            programaProduccionId,
                            ejecucion.ParteID,
                            cn,
                            tx);

                    var tieneMatriz =
                        partePolivalencia.HasValue &&
                        await ParteTienePolivalenciaProduccionAsync(
                            partePolivalencia.Value,
                            cn,
                            tx);

                    if (tieneMatriz)
                    {
                        var nivel =
                            await ObtenerNivelPolivalenciaProduccionAsync(
                                partePolivalencia!.Value,
                                personaNuevaId.Value,
                                cn,
                                tx);

                        if (!nivel.HasValue ||
                           nivel.Value < 1 ||
                           nivel.Value > 4)
                        {
                            await tx.RollbackAsync();

                            TempData["Error"] =
                                "El nuevo operador principal debe estar evaluado N1-N4 para esta pieza.";

                            return RedirectToAction(
                                nameof(Detalle),
                                new { id = ejecucionProduccionId });
                        }
                    }
                    else if (!await PersonaEsOperadorActivoProduccionAsync(
                                personaNuevaId.Value,
                                cn,
                                tx))
                    {
                        await tx.RollbackAsync();

                        TempData["Error"] =
                            "Selecciona una persona activa del catálogo de operadores.";

                        return RedirectToAction(
                            nameof(Detalle),
                            new { id = ejecucionProduccionId });
                    }

                    operadorPrincipalNuevoId =
                        personaNuevaId.Value;

                    operadorPrincipalNuevoNombre =
                        personaNuevaNombre.Trim();
                }
                else
                {
                    if (operadorAuxiliarAnteriorId ==
                       personaNuevaId.Value)
                    {
                        await tx.CommitAsync();

                        TempData["Info"] =
                            "La persona seleccionada ya está asignada como auxiliar.";

                        return RedirectToAction(
                            nameof(Detalle),
                            new { id = ejecucionProduccionId });
                    }

                    if (operadorPrincipalAnteriorId.HasValue &&
                       operadorPrincipalAnteriorId.Value ==
                       personaNuevaId.Value)
                    {
                        await tx.RollbackAsync();

                        TempData["Error"] =
                            "El operador principal y el auxiliar no pueden ser la misma persona.";

                        return RedirectToAction(
                            nameof(Detalle),
                            new { id = ejecucionProduccionId });
                    }

                    if (!await PersonaEsAuxiliarActivoProduccionAsync(
                            personaNuevaId.Value,
                            cn,
                            tx))
                    {
                        await tx.RollbackAsync();

                        TempData["Error"] =
                            "Selecciona una persona activa del catálogo de auxiliares.";

                        return RedirectToAction(
                            nameof(Detalle),
                            new { id = ejecucionProduccionId });
                    }

                    operadorAuxiliarNuevoId =
                        personaNuevaId.Value;

                    operadorAuxiliarNuevoNombre =
                        personaNuevaNombre.Trim();
                }

                var observacionCambio =
                    tipoCambio == "PRINCIPAL"
                        ? $"Cambio manual de operador principal. {operadorPrincipalAnteriorNombre} → {operadorPrincipalNuevoNombre}. Motivo: {motivoCambio}"
                        : $"Cambio manual de auxiliar. {operadorAuxiliarAnteriorNombre} → {operadorAuxiliarNuevoNombre}. Motivo: {motivoCambio}";

                if (observacionCambio.Length > 1000)
                    observacionCambio = observacionCambio[..1000];

                const string sqlActualizarEjecucion = @"
UPDATE dbo.Produccion_Ejecucion
SET
    OperadorID=@OperadorPrincipalPersonaID,
    OperadorNombre=@OperadorPrincipalNombre,
    OperadorAuxiliarID=@OperadorAuxiliarPersonaID,
    OperadorAuxiliarNombre=@OperadorAuxiliarNombre,
    OperadoresModificadosManual=1,
    MotivoCambioOperadores=@MotivoCambio,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=GETDATE(),
    Observaciones=
        CASE
            WHEN Observaciones IS NULL
              OR LTRIM(RTRIM(Observaciones))=N''
                THEN @ObservacionCambio
            ELSE
                Observaciones+
                CHAR(13)+CHAR(10)+
                @ObservacionCambio
        END
WHERE EjecucionProduccionID=@EjecucionProduccionID
  AND ProgramaProduccionID=@ProgramaProduccionID
  AND Activo=1
  AND EstatusID=@EstatusID;

IF @@ROWCOUNT<>1
BEGIN
    THROW 51300,
          'La ejecución cambió mientras se actualizaba el operador.',
          1;
END;";

                await using (var cmd =
                    new SqlCommand(
                        sqlActualizarEjecucion,
                        cn,
                        tx))
                {
                    cmd.Parameters.Add(
                        "@EjecucionProduccionID",
                        SqlDbType.Int).Value =
                        ejecucionProduccionId;

                    cmd.Parameters.Add(
                        "@ProgramaProduccionID",
                        SqlDbType.Int).Value =
                        programaProduccionId;

                    cmd.Parameters.Add(
                        "@EstatusID",
                        SqlDbType.Int).Value =
                        estatusId;

                    cmd.Parameters.Add(
                        "@OperadorPrincipalPersonaID",
                        SqlDbType.Int).Value =
                        (object?)operadorPrincipalNuevoId
                        ?? DBNull.Value;

                    cmd.Parameters.Add(
                        "@OperadorPrincipalNombre",
                        SqlDbType.NVarChar,
                        200).Value =
                        string.IsNullOrWhiteSpace(
                            operadorPrincipalNuevoNombre)
                            ? DBNull.Value
                            : operadorPrincipalNuevoNombre.Trim();

                    cmd.Parameters.Add(
                        "@OperadorAuxiliarPersonaID",
                        SqlDbType.Int).Value =
                        (object?)operadorAuxiliarNuevoId
                        ?? DBNull.Value;

                    cmd.Parameters.Add(
                        "@OperadorAuxiliarNombre",
                        SqlDbType.NVarChar,
                        200).Value =
                        string.IsNullOrWhiteSpace(
                            operadorAuxiliarNuevoNombre)
                            ? DBNull.Value
                            : operadorAuxiliarNuevoNombre.Trim();

                    cmd.Parameters.Add(
                        "@MotivoCambio",
                        SqlDbType.NVarChar,
                        1000).Value =
                        motivoCambio;

                    cmd.Parameters.Add(
                        "@ObservacionCambio",
                        SqlDbType.NVarChar,
                        1000).Value =
                        observacionCambio;

                    cmd.Parameters.Add(
                        "@UsuarioID",
                        SqlDbType.Int).Value =
                        usuarioId;

                    await cmd.ExecuteNonQueryAsync();
                }

                if (tipoCambio == "PRINCIPAL")
                {
                    await SincronizarOperadorProgramaAsync(
                        programaProduccionId,
                        operadorPrincipalNuevoId,
                        "PRINCIPAL",
                        usuarioId,
                        cn,
                        tx);
                }
                else
                {
                    await SincronizarOperadorProgramaAsync(
                        programaProduccionId,
                        operadorAuxiliarNuevoId,
                        "AUXILIAR",
                        usuarioId,
                        cn,
                        tx);
                }

                const string sqlActualizarCalidad = @"
DECLARE @InspeccionID INT;
DECLARE @EstadoActual NVARCHAR(50);

SELECT TOP (1)
    @InspeccionID=i.InspeccionID,
    @EstadoActual=
        UPPER(
            LTRIM(
                RTRIM(
                    ISNULL(i.Estado,N'')
                )
            )
        )
FROM dbo.Calidad_Inspecciones i
WITH(UPDLOCK,HOLDLOCK)
WHERE i.EjecucionProduccionID=@EjecucionProduccionID
  AND ISNULL(i.Estado,N'')<>N'CERRADA'
ORDER BY i.InspeccionID DESC;

IF @InspeccionID IS NOT NULL
BEGIN
    UPDATE dbo.Calidad_Inspecciones
    SET
        OperadorPrincipalPersonaID=
            @OperadorPrincipalPersonaID,
        OperadorPrincipalNombre=
            @OperadorPrincipalNombre,
        OperadorAuxiliarPersonaID=
            @OperadorAuxiliarPersonaID,
        OperadorAuxiliarNombre=
            @OperadorAuxiliarNombre,
        UsuarioModificacionID=
            @UsuarioID,
        FechaModificacion=
            GETDATE()
    WHERE InspeccionID=
          @InspeccionID;

    INSERT INTO dbo.Calidad_InspeccionHistorial
    (
        InspeccionID,
        Movimiento,
        EstadoAnterior,
        EstadoNuevo,
        ResultadoCalidad,
        Etiqueta,
        Comentario,
        UsuarioID,
        FechaMovimiento
    )
    VALUES
    (
        @InspeccionID,
        N'OPERADORES_ACTUALIZADOS_PRODUCCION',
        @EstadoActual,
        @EstadoActual,
        N'ACTUALIZADO',
        NULL,
        @Comentario,
        @UsuarioID,
        GETDATE()
    );
END;";

                await using (var cmd =
                    new SqlCommand(
                        sqlActualizarCalidad,
                        cn,
                        tx))
                {
                    cmd.Parameters.Add(
                        "@EjecucionProduccionID",
                        SqlDbType.Int).Value =
                        ejecucionProduccionId;

                    cmd.Parameters.Add(
                        "@OperadorPrincipalPersonaID",
                        SqlDbType.Int).Value =
                        (object?)operadorPrincipalNuevoId
                        ?? DBNull.Value;

                    cmd.Parameters.Add(
                        "@OperadorPrincipalNombre",
                        SqlDbType.NVarChar,
                        200).Value =
                        string.IsNullOrWhiteSpace(
                            operadorPrincipalNuevoNombre)
                            ? DBNull.Value
                            : operadorPrincipalNuevoNombre.Trim();

                    cmd.Parameters.Add(
                        "@OperadorAuxiliarPersonaID",
                        SqlDbType.Int).Value =
                        (object?)operadorAuxiliarNuevoId
                        ?? DBNull.Value;

                    cmd.Parameters.Add(
                        "@OperadorAuxiliarNombre",
                        SqlDbType.NVarChar,
                        200).Value =
                        string.IsNullOrWhiteSpace(
                            operadorAuxiliarNuevoNombre)
                            ? DBNull.Value
                            : operadorAuxiliarNuevoNombre.Trim();

                    cmd.Parameters.Add(
                        "@Comentario",
                        SqlDbType.NVarChar,
                        1000).Value =
                        observacionCambio;

                    cmd.Parameters.Add(
                        "@UsuarioID",
                        SqlDbType.Int).Value =
                        usuarioId;

                    await cmd.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();

                TempData["Success"] =
                    tipoCambio == "PRINCIPAL"
                        ? "Operador principal actualizado correctamente."
                        : "Auxiliar actualizado correctamente.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                TempData["Error"] =
                    "No fue posible cambiar el operador: " +
                    ex.Message;
            }

            return RedirectToAction(
                nameof(Detalle),
                new { id = ejecucionProduccionId });
        }

        private static async Task SincronizarOperadorProgramaAsync(int programaProduccionId, int? personaId, string rolOperador, int usuarioId, SqlConnection cn, SqlTransaction tx)
        {
            if (programaProduccionId <= 0) throw new ArgumentException("El programa de Producción no es válido.", nameof(programaProduccionId));
            rolOperador = rolOperador?.Trim().ToUpperInvariant() ?? string.Empty;
            if (rolOperador != "PRINCIPAL" && rolOperador != "AUXILIAR") throw new ArgumentException("El rol del operador no es válido.", nameof(rolOperador));

            const string sql = @"
UPDATE dbo.Planeacion_ProgramaOperadores
SET Activo=0,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=GETDATE()
WHERE ProgramaProduccionID=@ProgramaProduccionID
  AND UPPER(LTRIM(RTRIM(ISNULL(RolOperador,N''))))=@RolOperador
  AND Activo=1
  AND
  (
      @PersonaID IS NULL
      OR PersonaID<>@PersonaID
  );

IF @PersonaID IS NOT NULL
BEGIN
    IF EXISTS
    (
        SELECT 1
        FROM dbo.Planeacion_ProgramaOperadores WITH (UPDLOCK,HOLDLOCK)
        WHERE ProgramaProduccionID=@ProgramaProduccionID
          AND PersonaID=@PersonaID
          AND UPPER(LTRIM(RTRIM(ISNULL(RolOperador,N''))))=@RolOperador
    )
    BEGIN
        UPDATE dbo.Planeacion_ProgramaOperadores
        SET Activo=1,
            UsuarioModificacionID=@UsuarioID,
            FechaModificacion=GETDATE()
        WHERE ProgramaProduccionID=@ProgramaProduccionID
          AND PersonaID=@PersonaID
          AND UPPER(LTRIM(RTRIM(ISNULL(RolOperador,N''))))=@RolOperador;
    END
    ELSE
    BEGIN
        INSERT INTO dbo.Planeacion_ProgramaOperadores
        (
            ProgramaProduccionID,
            PersonaID,
            RolOperador,
            UsuarioCreacionID,
            FechaCreacion,
            Activo
        )
        VALUES
        (
            @ProgramaProduccionID,
            @PersonaID,
            @RolOperador,
            @UsuarioID,
            GETDATE(),
            1
        );
    END;
END;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;
            cmd.Parameters.Add("@PersonaID", SqlDbType.Int).Value = personaId.HasValue ? personaId.Value : DBNull.Value;
            cmd.Parameters.Add("@RolOperador", SqlDbType.NVarChar, 30).Value = rolOperador;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            await cmd.ExecuteNonQueryAsync();
        }

        // NSQ_TECNICO_PRODUCCION_PREP_V2
        [HttpGet]
        public async Task<IActionResult> TecnicosProduccionActivos()
        {
            if (!UsuarioEnSesion())
                return Unauthorized();

            var tecnicos = new List<object>();

            await using var cn =
                new SqlConnection(ConnectionString);

            await cn.OpenAsync();

            const string sql = @"
SELECT
    p.PersonaID,
    p.NumeroControl,
    NULLIF(
        LTRIM(RTRIM(
            ISNULL(p.Nombre,N'') + N' ' +
            ISNULL(p.ApellidoPaterno,N'') + N' ' +
            ISNULL(p.ApellidoMaterno,N''))),
        N''
    ) AS NombreCompleto,
    LTRIM(RTRIM(ISNULL(p.Puesto,N''))) AS Puesto
FROM dbo.Persona p
WHERE ISNULL(p.EsColaboradorActivo,0)=1
  AND UPPER(LTRIM(RTRIM(ISNULL(p.Puesto,N''))))
        COLLATE Latin1_General_CI_AI LIKE N'%TECN%'
  AND UPPER(LTRIM(RTRIM(ISNULL(p.Puesto,N''))))
        COLLATE Latin1_General_CI_AI LIKE N'%PRODUC%'
ORDER BY
    NombreCompleto,
    p.PersonaID;";

            await using var cmd =
                new SqlCommand(sql, cn);

            await using var rd =
                await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                tecnicos.Add(new
                {
                    personaID =
                        Convert.ToInt32(
                            rd["PersonaID"]),

                    numeroControl =
                        rd["NumeroControl"] == DBNull.Value
                            ? string.Empty
                            : rd["NumeroControl"]
                                .ToString()?
                                .Trim()
                              ?? string.Empty,

                    nombre =
                        rd["NombreCompleto"] == DBNull.Value
                            ? string.Empty
                            : rd["NombreCompleto"]
                                .ToString()?
                                .Trim()
                              ?? string.Empty,

                    puesto =
                        rd["Puesto"] == DBNull.Value
                            ? string.Empty
                            : rd["Puesto"]
                                .ToString()?
                                .Trim()
                              ?? string.Empty
                });
            }

            return Json(new
            {
                ok = true,
                tecnicos
            });
        }

       
        private static async Task<bool>
            PersonaEsTecnicoProduccionActivoAsync(
                int personaId,
                SqlConnection cn,
                SqlTransaction tx)
        {
            if (personaId <= 0)
                return false;

            const string sql = @"
SELECT CASE WHEN EXISTS
(
    SELECT 1
    FROM dbo.Persona p WITH(UPDLOCK,HOLDLOCK)
    WHERE p.PersonaID=@PersonaID
      AND ISNULL(p.EsColaboradorActivo,0)=1
      AND UPPER(LTRIM(RTRIM(ISNULL(p.Puesto,N''))))
            COLLATE Latin1_General_CI_AI LIKE N'%TECN%'
      AND UPPER(LTRIM(RTRIM(ISNULL(p.Puesto,N''))))
            COLLATE Latin1_General_CI_AI LIKE N'%PRODUC%'
)
THEN 1 ELSE 0 END;";

            await using var cmd =
                new SqlCommand(sql, cn, tx);

            cmd.Parameters
                .Add("@PersonaID", SqlDbType.Int)
                .Value = personaId;

            return Convert.ToInt32(
                await cmd.ExecuteScalarAsync()) == 1;
        }

        private static async Task<string?>
            ObtenerNombreTecnicoProduccionAsync(
                int personaId,
                SqlConnection cn,
                SqlTransaction tx)
        {
            if (personaId <= 0)
                return null;

            const string sql = @"
SELECT TOP (1)
    NULLIF(
        LTRIM(RTRIM(
            ISNULL(p.Nombre,N'') + N' ' +
            ISNULL(p.ApellidoPaterno,N'') + N' ' +
            ISNULL(p.ApellidoMaterno,N''))),
        N''
    )
FROM dbo.Persona p WITH(UPDLOCK,HOLDLOCK)
WHERE p.PersonaID=@PersonaID
  AND ISNULL(p.EsColaboradorActivo,0)=1
  AND UPPER(LTRIM(RTRIM(ISNULL(p.Puesto,N''))))
        COLLATE Latin1_General_CI_AI LIKE N'%TECN%'
  AND UPPER(LTRIM(RTRIM(ISNULL(p.Puesto,N''))))
        COLLATE Latin1_General_CI_AI LIKE N'%PRODUC%';";

            await using var cmd =
                new SqlCommand(sql, cn, tx);

            cmd.Parameters
                .Add("@PersonaID", SqlDbType.Int)
                .Value = personaId;

            var resultado =
                await cmd.ExecuteScalarAsync();

            if (resultado == null ||
                resultado == DBNull.Value)
            {
                return null;
            }

            var nombre =
                resultado.ToString()?.Trim();

            return string.IsNullOrWhiteSpace(nombre)
                ? null
                : nombre;
        }
        private static async Task<string?> ObtenerNombrePersonaActivaProduccionAsync(int personaId, SqlConnection cn, SqlTransaction tx)
        {
            if (personaId <= 0) return null;
            const string sql = @"
SELECT TOP (1)
    NULLIF(LTRIM(RTRIM(ISNULL(Nombre,N'')+N' '+ISNULL(ApellidoPaterno,N'')+N' '+ISNULL(ApellidoMaterno,N''))),N'') AS NombreCompleto
FROM dbo.Persona WITH(UPDLOCK,HOLDLOCK)
WHERE PersonaID=@PersonaID
  AND ISNULL(EsColaboradorActivo,1)=1;";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@PersonaID", SqlDbType.Int).Value = personaId;
            var resultado = await cmd.ExecuteScalarAsync();
            if (resultado == null || resultado == DBNull.Value) return null;
            var nombre = resultado.ToString()?.Trim();
            return string.IsNullOrWhiteSpace(nombre) ? null : nombre;
        }

        private static int? ObtenerEnteroNullable(
    SqlDataReader rd,
    string columna)
        {
            var ordinal = rd.GetOrdinal(columna);

            return rd.IsDBNull(ordinal)
                ? null
                : Convert.ToInt32(rd.GetValue(ordinal));
        }

        private static TimeSpan? NullableTiempo(
    SqlDataReader rd,
    string columna)
        {
            var ordinal = rd.GetOrdinal(columna);

            if (rd.IsDBNull(ordinal))
                return null;

            var valor = rd.GetValue(ordinal);

            if (valor is TimeSpan tiempo)
                return tiempo;

            if (valor is DateTime fecha)
                return fecha.TimeOfDay;

            if (TimeSpan.TryParse(
                valor.ToString(),
                out var resultado))
            {
                return resultado;
            }

            return null;
        }
        private static string? ObtenerTextoNullable(
            SqlDataReader rd,
            string columna)
        {
            var ordinal = rd.GetOrdinal(columna);

            if (rd.IsDBNull(ordinal))
                return null;

            var valor =
                rd.GetValue(ordinal)?.ToString()?.Trim();

            return string.IsNullOrWhiteSpace(valor)
                ? null
                : valor;
        }

        private static DateTime? ObtenerFechaNullable(
            SqlDataReader rd,
            string columna)
        {
            var ordinal = rd.GetOrdinal(columna);

            return rd.IsDBNull(ordinal)
                ? null
                : Convert.ToDateTime(rd.GetValue(ordinal));
        }

        private static TimeSpan? ObtenerTiempoNullable(
            SqlDataReader rd,
            string columna)
        {
            var ordinal = rd.GetOrdinal(columna);

            if (rd.IsDBNull(ordinal))
                return null;

            var valor = rd.GetValue(ordinal);

            if (valor is TimeSpan tiempo)
                return tiempo;

            return TimeSpan.TryParse(
                valor.ToString(),
                out var resultado)
                    ? resultado
                    : null;
        }

        private static bool ObtenerBooleano(
            SqlDataReader rd,
            string columna)
        {
            var ordinal = rd.GetOrdinal(columna);

            return !rd.IsDBNull(ordinal) &&
                   Convert.ToBoolean(rd.GetValue(ordinal));
        }

        private async Task<HashSet<int>> ObtenerMaquinasOcupadasAsync(SqlConnection cn)
        {
            var maquinas = new HashSet<int>();
            const string sql = @"
SELECT DISTINCT e.MaquinaID
FROM dbo.Produccion_Ejecucion e
WHERE e.Activo=1
  AND e.MaquinaID IS NOT NULL
  AND e.FechaLiberacionMaquina IS NULL
  AND e.EstatusID IN(@EnPreparacion,@EnProduccion,@Pausado);";
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@EnPreparacion", SqlDbType.Int).Value = ProduccionEstatus.EnPreparacion;
            cmd.Parameters.Add("@EnProduccion", SqlDbType.Int).Value = ProduccionEstatus.EnProduccion;
            cmd.Parameters.Add("@Pausado", SqlDbType.Int).Value = ProduccionEstatus.Pausado;
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                if (rd["MaquinaID"] != DBNull.Value)
                    maquinas.Add(Convert.ToInt32(rd["MaquinaID"]));
            }
            return maquinas;
        }

        private sealed class InterrupcionUrgenteRetornoContexto
        {
            public int ParoID { get; set; }
            public int EjecucionInterrumpidaID { get; set; }
            public int ProgramaInterrumpidoID { get; set; }
            public int ProgramaUrgenteID { get; set; }
            public int? MaquinaID { get; set; }
            public DateTime FechaInicioParo { get; set; }
            public bool EsParoLhRh { get; set; }
            public Guid? GrupoParoLhRh { get; set; }
            public int EstatusEjecucionInterrumpida { get; set; }
            public int? MoldeInterrumpidoID { get; set; }
            public int? MoldeUrgenteID { get; set; }
            public bool CambiaMolde => MoldeInterrumpidoID != MoldeUrgenteID;
        }
        private sealed class InterrupcionUrgenteRetornoResultado
        {
            public int ParoID { get; set; }
            public int EjecucionInterrumpidaID { get; set; }
            public int ProgramaInterrumpidoID { get; set; }
            public int DuracionMinutos { get; set; }
            public bool CambiaMolde { get; set; }
            public bool RequiereReliberacion { get; set; }
            public int ProgramasRecorridos { get; set; }
        }
        private sealed class ValidacionInicioSerieResultado
        {
            public bool Permitido { get; set; }
            public int? InspeccionID { get; set; }
            public string Mensaje { get; set; } = string.Empty;
        }

        private async Task<string?> ObtenerPersonaNombreAsync(
    int personaId,
    SqlConnection cn,
    SqlTransaction? tx)
        {
            const string sql = @"
SELECT TOP (1)
    LTRIM(RTRIM(
        ISNULL(Nombre, '') + ' ' +
        ISNULL(ApellidoPaterno, '') + ' ' +
        ISNULL(ApellidoMaterno, '')
    )) AS NombreCompleto
FROM dbo.Persona
WHERE PersonaID = @PersonaID;";

            await using var cmd = tx == null
                ? new SqlCommand(sql, cn)
                : new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@PersonaID", SqlDbType.Int).Value = personaId;

            var result = await cmd.ExecuteScalarAsync();
            
            return result == null || result == DBNull.Value
                ? null
                : result.ToString()?.Trim();
        }

        private static bool Booleano(SqlDataReader rd, string columna)
        {
            var ordinal = rd.GetOrdinal(columna);

            return !rd.IsDBNull(ordinal) &&
                   Convert.ToBoolean(rd.GetValue(ordinal));
        }

        private static string? TextoNullable(SqlDataReader rd, string columna)
        {
            var ordinal = rd.GetOrdinal(columna);

            return rd.IsDBNull(ordinal)
                ? null
                : rd.GetValue(ordinal)?.ToString()?.Trim();
        }
    }
}