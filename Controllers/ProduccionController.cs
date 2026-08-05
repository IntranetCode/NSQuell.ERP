using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using System.Data;


namespace ERP.NSQuell.Controllers
{
    public sealed partial class ProduccionController : Controller
    {
        private readonly IConfiguration _configuration;

        public ProduccionController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private string ConnectionString =>
            _configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("No se encontró la cadena de conexión DefaultConnection.");
     


        [HttpGet]
        public async Task<IActionResult> Index(  string? busqueda = null,  int? maquinaId = null,   int? estatusId = null, DateTime? fechaDesde = null,  DateTime? fechaHasta = null)
        {
            if (!UsuarioEnSesion())
                return RedirectToAction("Login", "Login");

            var vm = new ProduccionBandejaVm
            {
                Busqueda = busqueda?.Trim(),
                MaquinaID = maquinaId,
                EstatusID = estatusId,
                FechaDesde = fechaDesde,
                FechaHasta = fechaHasta
            };

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            vm.Maquinas = await CargarMaquinasAsync(cn);
            vm.Estatus = await CargarEstatusProduccionAsync(cn);
            ViewBag.OperadoresProduccion = await CargarOperadoresProduccionAsync(cn);

            vm.ProgramasDisponibles = await ObtenerProgramasDisponiblesAsync(
    busqueda,
    maquinaId,
    fechaDesde,
    fechaHasta,
    cn);

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

    e.FechaInicioReal,
    e.FechaFinReal,

    e.CantidadPlaneada,
    e.CantidadOKTotal,
    e.CantidadSospechosaTotal,
    e.CantidadScrapTotal,

    e.EstatusID,
    e.Observaciones,

    e.UsuarioCreacionID,
    e.FechaCreacion,
    e.UsuarioModificacionID,
    e.FechaModificacion,
    e.Activo
FROM dbo.Produccion_Ejecucion e
WHERE e.Activo = 1
  AND (@MaquinaID IS NULL OR e.MaquinaID = @MaquinaID)
  AND (@EstatusID IS NULL OR e.EstatusID = @EstatusID)
  AND (@FechaDesde IS NULL OR CONVERT(DATE, e.FechaCreacion) >= @FechaDesde)
  AND (@FechaHasta IS NULL OR CONVERT(DATE, e.FechaCreacion) <= @FechaHasta)
  AND
  (
        @Busqueda IS NULL
     OR e.MaquinaCodigo LIKE '%' + @Busqueda + '%'
     OR e.MaquinaNombre LIKE '%' + @Busqueda + '%'
     OR e.NumeroParte LIKE '%' + @Busqueda + '%'
     OR e.ReferenciaSAP LIKE '%' + @Busqueda + '%'
     OR e.DescripcionParte LIKE '%' + @Busqueda + '%'
     OR e.MoldeCodigo LIKE '%' + @Busqueda + '%'
     OR e.OperadorNombre LIKE '%' + @Busqueda + '%'
     OR CONVERT(NVARCHAR(30), e.ProgramaProduccionID) LIKE '%' + @Busqueda + '%'
     OR CONVERT(NVARCHAR(30), e.SolicitudProduccionID) LIKE '%' + @Busqueda + '%'
  )
ORDER BY
    CASE e.EstatusID
        WHEN 3 THEN 1
        WHEN 4 THEN 2
        WHEN 2 THEN 3
        WHEN 1 THEN 4
        ELSE 5
    END,
    e.FechaCreacion DESC,
    e.EjecucionProduccionID DESC;";

            await using var cmd = new SqlCommand(sql, cn);

            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value =
                (object?)maquinaId ?? DBNull.Value;

            cmd.Parameters.Add("@EstatusID", SqlDbType.Int).Value =
                (object?)estatusId ?? DBNull.Value;

            cmd.Parameters.Add("@FechaDesde", SqlDbType.Date).Value =
                (object?)fechaDesde?.Date ?? DBNull.Value;

            cmd.Parameters.Add("@FechaHasta", SqlDbType.Date).Value =
                (object?)fechaHasta?.Date ?? DBNull.Value;

            cmd.Parameters.Add("@Busqueda", SqlDbType.NVarChar, 200).Value =
                string.IsNullOrWhiteSpace(busqueda)
                    ? DBNull.Value
                    : busqueda.Trim();

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
                vm.Ejecuciones.Add(MapearEjecucion(rd));

            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> Detalle(int id)
        {
            if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");
            if (id <= 0) return NotFound();

            var usuarioId = ObtenerUsuarioID();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var ejecucion = await ObtenerEjecucionAsync(id, cn);
            if (ejecucion == null) return NotFound();

            ProduccionMonitoreoTurnoAvisoVm? monitoreoTurnoActual = null;

            var ejecucionActivaParaMonitoreo =
                ejecucion.EstatusID == ProduccionEstatus.EnPreparacion ||
                ejecucion.EstatusID == ProduccionEstatus.EnProduccion ||
                ejecucion.EstatusID == ProduccionEstatus.Pausado;

            if (ejecucionActivaParaMonitoreo && ejecucion.SolicitudProduccionID.HasValue && ejecucion.SolicitudProduccionID.Value > 0)
            {
                await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();

                try
                {
                    var checklistPerifericosId = await ObtenerOCrearChecklistPerifericosTurnoAsync(ejecucion, DateTime.Now, usuarioId, cn, tx);
                    await tx.CommitAsync();
                    monitoreoTurnoActual = await ObtenerAvisoMonitoreoTurnoAsync(checklistPerifericosId, cn);
                }
                catch (Exception ex)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "No fue posible preparar el monitoreo de periféricos del turno actual: " + ex.Message;
                }
            }

            var vm = new ProduccionDetalleVm
            {
                Ejecucion = ejecucion,
                RegistrosHora = await ObtenerRegistrosHoraAsync(id, cn),
                Paros = await ObtenerParosAsync(id, cn),
                MotivosParo = await CargarMotivosParoAsync(cn),
                ChecklistResumen = await ObtenerResumenChecklistArranqueAsync(id, cn),
                CalidadResumen = await ObtenerResumenCalidadAsync(id, cn),
                MonitoreoTurnoActual = monitoreoTurnoActual
            };

            vm.RecepcionesOF = await ObtenerEntregasAlmacenOFAsync(ejecucion, cn, null);

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
            const string sqlDatos = @"
SELECT TOP (1)
    c.ChecklistArranqueID,
    c.EjecucionProduccionID,
    c.ProgramaProduccionID,
    c.SolicitudProduccionID,
    c.SolicitudProduccionDetalleID,
    c.ReleaseID,
    c.ReleaseDetalleID,

    c.MaquinaID,
    c.MaquinaCodigo,
    c.MaquinaNombre,

    c.MoldeID,
    c.MoldeCodigo,

    c.ParteID,
    c.NumeroParte,
    c.ReferenciaSAP,
    c.DescripcionParte,

    e.OperadorID,
    e.OperadorNombre,
    e.CantidadPlaneada,

    pp.ClienteID,
    pp.ClienteNombre,
    pp.MaterialID,
    pp.MaterialCodigo,
    pp.MaterialDescripcion,
    pp.FechaInicioProgramada,
    pp.FechaFinProgramada,

    sp.NumeroOFRecibida,
    sp.FolioSolicitud
FROM dbo.Produccion_ChecklistArranque c
INNER JOIN dbo.Produccion_Ejecucion e
    ON e.EjecucionProduccionID = c.EjecucionProduccionID
   AND e.Activo = 1
INNER JOIN dbo.Planeacion_ProgramaProduccion pp
    ON pp.ProgramaProduccionID = c.ProgramaProduccionID
   AND pp.Activo = 1
LEFT JOIN dbo.SolicitudesProduccion sp
    ON sp.SolicitudProduccionID = c.SolicitudProduccionID
WHERE c.ChecklistArranqueID = @ChecklistArranqueID
  AND c.EjecucionProduccionID = @EjecucionProduccionID
  AND c.Activo = 1;";

            int programaProduccionId;
            int? solicitudProduccionId;
            int? solicitudProduccionDetalleId;
            int? releaseId;
            int? releaseDetalleId;
            int? clienteId;
            string? clienteNombre;
            int? parteId;
            int? maquinaId;
            int? moldeId;
            int? materialId;
            string? ordenTrabajo;
            string? numeroParte;
            string? material;
            string? proceso;
            string? maquina;
            string? molde;
            DateTime? fechaInicioProgramada;
            DateTime? fechaFinProgramada;
            int? operadorPrincipalId;
            string? operadorPrincipalNombre;
            int cantidadTotal;

            await using (var cmd = new SqlCommand(sqlDatos, cn, tx))
            {
                cmd.Parameters.Add("@ChecklistArranqueID", SqlDbType.Int).Value =
                    checklistArranqueId;

                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value =
                    ejecucionProduccionId;

                await using var rd = await cmd.ExecuteReaderAsync();

                if (!await rd.ReadAsync())
                {
                    throw new InvalidOperationException(
                        "No se encontró la información del checklist para enviar a Calidad.");
                }

                programaProduccionId = Entero(rd, "ProgramaProduccionID");
                solicitudProduccionId = NullableEntero(rd, "SolicitudProduccionID");
                solicitudProduccionDetalleId = NullableEntero(rd, "SolicitudProduccionDetalleID");
                releaseId = NullableEntero(rd, "ReleaseID");
                releaseDetalleId = NullableEntero(rd, "ReleaseDetalleID");

                clienteId = NullableEntero(rd, "ClienteID");
                clienteNombre = TextoNullable(rd, "ClienteNombre");

                parteId = NullableEntero(rd, "ParteID");
                maquinaId = NullableEntero(rd, "MaquinaID");
                moldeId = NullableEntero(rd, "MoldeID");
                materialId = NullableEntero(rd, "MaterialID");

                ordenTrabajo =
                    TextoNullable(rd, "NumeroOFRecibida") ??
                    TextoNullable(rd, "FolioSolicitud") ??
                    ("PROG-" + programaProduccionId.ToString());

                numeroParte =
                    TextoNullable(rd, "ReferenciaSAP") ??
                    TextoNullable(rd, "NumeroParte");

                var materialCodigo = TextoNullable(rd, "MaterialCodigo");
                var materialDescripcion = TextoNullable(rd, "MaterialDescripcion");

                material = UnirTextoProduccion(materialCodigo, materialDescripcion);

                proceso = "LIBERACIÓN DE PREARRANQUE";

                maquina = UnirTextoProduccion(
                    TextoNullable(rd, "MaquinaCodigo"),
                    TextoNullable(rd, "MaquinaNombre"));

                molde = TextoNullable(rd, "MoldeCodigo");

                fechaInicioProgramada = NullableFecha(rd, "FechaInicioProgramada");
                fechaFinProgramada = NullableFecha(rd, "FechaFinProgramada");

                operadorPrincipalId = NullableEntero(rd, "OperadorID");
                operadorPrincipalNombre = TextoNullable(rd, "OperadorNombre");

                cantidadTotal = Entero(rd, "CantidadPlaneada");
            }

            const string sqlExiste = @"
SELECT TOP (1)
    InspeccionID
FROM dbo.Calidad_Inspecciones
WHERE ProgramaProduccionID = @ProgramaProduccionID
  AND EjecucionProduccionID = @EjecucionProduccionID
  AND ChecklistArranqueID = @ChecklistArranqueID
  AND ISNULL(ConfiguracionInvalidada, 0) = 0
  AND Estado <> 'CERRADA'
ORDER BY InspeccionID DESC;";

            int? inspeccionExistenteId = null;

            await using (var cmd = new SqlCommand(sqlExiste, cn, tx))
            {
                cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value =
                    programaProduccionId;

                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value =
                    ejecucionProduccionId;

                cmd.Parameters.Add("@ChecklistArranqueID", SqlDbType.Int).Value =
                    checklistArranqueId;

                var result = await cmd.ExecuteScalarAsync();

                if (result != null && result != DBNull.Value)
                    inspeccionExistenteId = Convert.ToInt32(result);
            }

            if (inspeccionExistenteId.HasValue)
            {
                const string sqlUpdate = @"
UPDATE dbo.Calidad_Inspecciones
SET
    Estado = 'PENDIENTE_PREARRANQUE',
    ChecklistValidado = 1,
    FechaNotificacionCalidad = GETDATE(),
    UsuarioNotificoID = @UsuarioID,
    Observaciones =
        CASE
            WHEN Observaciones IS NULL OR LTRIM(RTRIM(Observaciones)) = ''
                THEN 'Producción actualizó y reenvió el checklist de prearranque.'
            ELSE Observaciones + CHAR(13) + CHAR(10) + 'Producción actualizó y reenvió el checklist de prearranque.'
        END,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE InspeccionID = @InspeccionID;";

                await using var cmd = new SqlCommand(sqlUpdate, cn, tx);

                cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value =
                    inspeccionExistenteId.Value;

                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                    usuarioId;

                await cmd.ExecuteNonQueryAsync();

                return;
            }

            const string sqlInsert = @"
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

    CantidadTotal,
    CantidadRevisada,
    CantidadPendiente,

    ChecklistValidado,
    HojaInspeccionProducto,
    HojaValidacionCalidad,

    FechaNotificacionCalidad,
    UsuarioNotificoID,

    CincoDisparosSegregados,
    CantidadDisparosConformes,

    Liberado,
    RequiereGP12,
    EnContencion,
    EsScrap,
    ConfiguracionInvalidada,

    Observaciones,
    Estado,

    UsuarioCreacionID,
    FechaCreacion
)
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
    @Proceso,
    @Maquina,
    @Molde,

    @FechaInicioProgramada,
    @FechaFinProgramada,

    @OperadorPrincipalPersonaID,
    @OperadorPrincipalNombre,

    @CantidadTotal,
    0,
    @CantidadTotal,

    1,
    0,
    0,

    GETDATE(),
    @UsuarioID,

    0,
    0,

    0,
    0,
    0,
    0,
    0,

    'Solicitud recibida desde Producción para revisión de prearranque.',
    'PENDIENTE_PREARRANQUE',

    @UsuarioID,
    GETDATE()
);";

            await using (var cmd = new SqlCommand(sqlInsert, cn, tx))
            {
                cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value =
                    programaProduccionId;

                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value =
                    ejecucionProduccionId;

                cmd.Parameters.Add("@ChecklistArranqueID", SqlDbType.Int).Value =
                    checklistArranqueId;

                cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value =
                    (object?)solicitudProduccionId ?? DBNull.Value;

                cmd.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value =
                    (object?)solicitudProduccionDetalleId ?? DBNull.Value;

                cmd.Parameters.Add("@ReleaseID", SqlDbType.Int).Value =
                    (object?)releaseId ?? DBNull.Value;

                cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value =
                    (object?)releaseDetalleId ?? DBNull.Value;

                cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value =
                    (object?)clienteId ?? DBNull.Value;

                cmd.Parameters.Add("@ClienteNombre", SqlDbType.NVarChar, 200).Value =
                    (object?)clienteNombre ?? DBNull.Value;

                cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value =
                    (object?)parteId ?? DBNull.Value;

                cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value =
                    (object?)maquinaId ?? DBNull.Value;

                cmd.Parameters.Add("@MoldeID", SqlDbType.Int).Value =
                    (object?)moldeId ?? DBNull.Value;

                cmd.Parameters.Add("@MaterialID", SqlDbType.Int).Value =
                    (object?)materialId ?? DBNull.Value;

                cmd.Parameters.Add("@OrdenTrabajo", SqlDbType.NVarChar, 100).Value =
                    (object?)ordenTrabajo ?? DBNull.Value;

                cmd.Parameters.Add("@NumeroParte", SqlDbType.NVarChar, 150).Value =
                    (object?)numeroParte ?? DBNull.Value;

                cmd.Parameters.Add("@Material", SqlDbType.NVarChar, 300).Value =
                    (object?)material ?? DBNull.Value;

                cmd.Parameters.Add("@Proceso", SqlDbType.NVarChar, 150).Value =
                    proceso;

                cmd.Parameters.Add("@Maquina", SqlDbType.NVarChar, 300).Value =
                    (object?)maquina ?? DBNull.Value;

                cmd.Parameters.Add("@Molde", SqlDbType.NVarChar, 150).Value =
                    (object?)molde ?? DBNull.Value;

                cmd.Parameters.Add("@FechaInicioProgramada", SqlDbType.DateTime).Value =
                    (object?)fechaInicioProgramada ?? DBNull.Value;

                cmd.Parameters.Add("@FechaFinProgramada", SqlDbType.DateTime).Value =
                    (object?)fechaFinProgramada ?? DBNull.Value;

                cmd.Parameters.Add("@OperadorPrincipalPersonaID", SqlDbType.Int).Value =
                    (object?)operadorPrincipalId ?? DBNull.Value;

                cmd.Parameters.Add("@OperadorPrincipalNombre", SqlDbType.NVarChar, 200).Value =
                    (object?)operadorPrincipalNombre ?? DBNull.Value;

                cmd.Parameters.Add("@CantidadTotal", SqlDbType.Decimal).Value =
                    cantidadTotal;

                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                    usuarioId;

                await cmd.ExecuteNonQueryAsync();
            }
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Iniciar(int programaProduccionId,int? operadorId = null, string? operadorNombre = null, string? observaciones = null)
        {
            if (!UsuarioEnSesion())
                return RedirectToAction("Login", "Login");

            var usuarioId = ObtenerUsuarioID();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();

            try
            {
                var ejecucionExistenteId =
                    await ObtenerEjecucionActivaPorProgramaAsync(
                        programaProduccionId,
                        cn,
                        tx);

                if (ejecucionExistenteId.HasValue)
                {
                    await tx.CommitAsync();

                    TempData["Info"] =
                        "Este programa ya tiene una ejecución de producción activa.";

                    return RedirectToAction(
                        nameof(Detalle),
                        new { id = ejecucionExistenteId.Value });
                }

                var programa =
                    await ObtenerProgramaParaIniciarAsync(
                        programaProduccionId,
                        cn,
                        tx);

                if (programa == null)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "No se encontró el programa de producción o ya no está activo.";

                    return RedirectToAction(nameof(Index));
                }

                if (!programa.MaquinaID.HasValue)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "El programa no tiene máquina asignada. No puede iniciar producción.";

                    return RedirectToAction(nameof(Index));
                }

                int? operadorFinalId = operadorId;
                string? operadorFinalNombre = operadorNombre;

                if (!operadorFinalId.HasValue &&
    string.IsNullOrWhiteSpace(operadorFinalNombre) &&
    programa.OperadorPrincipalPlaneadoID.HasValue)
                {
                    operadorFinalId = programa.OperadorPrincipalPlaneadoID;
                    operadorFinalNombre = programa.OperadorPrincipalPlaneadoNombre;
                }

                if (operadorFinalId.HasValue)
                {
                    var operadorDb = await ObtenerPersonaNombreAsync(
                        operadorFinalId.Value,
                        cn,
                        tx);

                    if (!string.IsNullOrWhiteSpace(operadorDb))
                        operadorFinalNombre = operadorDb;
                }

                if (!operadorFinalId.HasValue &&
                    string.IsNullOrWhiteSpace(operadorFinalNombre))
                {
                    var operadorSugerido = await ObtenerOperadorSugeridoProduccionAsync(
                        programa.MaquinaID.Value,
                        DateTime.Now,
                        cn,
                        tx);

                    if (operadorSugerido != null)
                    {
                        operadorFinalId = operadorSugerido.OperadorID;
                        operadorFinalNombre = operadorSugerido.OperadorNombre;
                    }
                }

                var observacionesFinales = observaciones;

                if (!string.IsNullOrWhiteSpace(operadorFinalNombre))
                {
                    var textoOperador =
                        "Operador al iniciar preparación: " + operadorFinalNombre.Trim() + ".";

                    observacionesFinales = string.IsNullOrWhiteSpace(observacionesFinales)
                        ? textoOperador
                        : observacionesFinales.Trim() + Environment.NewLine + textoOperador;
                }
                else
                {
                    var textoSinOperador =
                        "Preparación iniciada sin operador asignado. Producción podrá asignarlo posteriormente.";

                    observacionesFinales = string.IsNullOrWhiteSpace(observacionesFinales)
                        ? textoSinOperador
                        : observacionesFinales.Trim() + Environment.NewLine + textoSinOperador;
                }

                var ejecucionId =
                    await InsertarEjecucionAsync(
                        programa,
                        operadorFinalId,
                        operadorFinalNombre,
                        observacionesFinales,
                        usuarioId,
                        cn,
                        tx);

                await MarcarProgramaEnPreparacionAsync(
                    programaProduccionId,
                    usuarioId,
                    cn,
                    tx);

                await tx.CommitAsync();

                TempData["Success"] =
                    "Preparación iniciada correctamente. Continúa con el checklist de arranque.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = ejecucionId });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                TempData["Error"] =
                    "No fue posible iniciar producción: " + ex.Message;

                return RedirectToAction(nameof(Index));
            }
        }



        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> IniciarSerie(int ejecucionProduccionId)
        {
            if (!UsuarioEnSesion())
                return RedirectToAction("Login", "Login");

            var usuarioId = ObtenerUsuarioID();

            if (ejecucionProduccionId <= 0)
            {
                TempData["Error"] =
                    "No se recibió la ejecución de producción.";

                return RedirectToAction(nameof(Index));
            }

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();

            try
            {
                var ejecucion = await ObtenerEjecucionAsync(
                    ejecucionProduccionId,
                    cn,
                    tx);

                if (ejecucion == null)
                {
                    await tx.RollbackAsync();
                    return NotFound();
                }

                if (ejecucion.EstatusID != ProduccionEstatus.EnPreparacion)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "Solo puedes iniciar serie cuando la ejecución está en preparación.";

                    return RedirectToAction(
                        nameof(Detalle),
                        new { id = ejecucionProduccionId });
                }

                var tieneParoAbierto = await TieneParoAbiertoAsync(
                    ejecucionProduccionId,
                    cn,
                    tx);

                if (tieneParoAbierto)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "No puedes iniciar serie mientras exista un paro abierto.";

                    return RedirectToAction(
                        nameof(Detalle),
                        new { id = ejecucionProduccionId });
                }

                var checklistValido = await ChecklistPermiteIniciarSerieAsync(
                    ejecucionProduccionId,
                    cn,
                    tx);

                if (!checklistValido)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "No se puede iniciar la producción en serie hasta que Calidad libere las primeras piezas con etiqueta verde.";

                    return RedirectToAction(
                        nameof(Detalle),
                        new { id = ejecucionProduccionId });
                }

                await CambiarEstatusEjecucionAsync(
                    ejecucionProduccionId,
                    ProduccionEstatus.EnProduccion,
                    usuarioId,
                    cn,
                    tx);

                await MarcarProgramaEnProduccionAsync(
                    ejecucion.ProgramaProduccionID,
                    DateTime.Now,
                    usuarioId,
                    cn,
                    tx);

                await MarcarCalidadEnMonitoreoAsync(
                    ejecucionProduccionId,
                    usuarioId,
                    cn,
                    tx);

                await tx.CommitAsync();

                TempData["Success"] =
                    "Producción en serie iniciada correctamente. Ya puedes registrar piezas por hora.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = ejecucionProduccionId });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                TempData["Error"] =
                    "No fue posible iniciar producción en serie: " + ex.Message;

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = ejecucionProduccionId });
            }
        }

        // ============================================================
        // REGISTRO POR HORA
        // ============================================================

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

        // ============================================================
        // PAROS
        // ============================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> IniciarParo(
            ProduccionParoPostVm vm)
        {
            if (!UsuarioEnSesion())
                return RedirectToAction("Login", "Login");

            var usuarioId = ObtenerUsuarioID();

            if (vm.EjecucionProduccionID <= 0)
                return RedirectToAction(nameof(Index));

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

                var tieneParoAbierto =
                    await TieneParoAbiertoAsync(
                        vm.EjecucionProduccionID,
                        cn,
                        tx);

                if (tieneParoAbierto)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "Esta ejecución ya tiene un paro abierto.";

                    return RedirectToAction(
                        nameof(Detalle),
                        new { id = vm.EjecucionProduccionID });
                }

                var motivoTexto = vm.MotivoParoTexto;

                if (vm.MotivoParoID.HasValue)
                {
                    motivoTexto =
                        await ObtenerMotivoParoNombreAsync(
                            vm.MotivoParoID.Value,
                            cn,
                            tx);
                }

                const string sqlInsert = @"
INSERT INTO dbo.Produccion_Paros
(
    EjecucionProduccionID,
    ProgramaProduccionID,
    SolicitudProduccionID,
    MaquinaID,
    OperadorID,
    FechaInicioParo,
    MotivoParoID,
    MotivoParoTexto,
    Descripcion,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
VALUES
(
    @EjecucionProduccionID,
    @ProgramaProduccionID,
    @SolicitudProduccionID,
    @MaquinaID,
    @OperadorID,
    GETDATE(),
    @MotivoParoID,
    @MotivoParoTexto,
    @Descripcion,
    @UsuarioID,
    GETDATE(),
    1
);";

                await using (var cmd = new SqlCommand(sqlInsert, cn, tx))
                {
                    cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value =
                        ejecucion.EjecucionProduccionID;

                    cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value =
                        ejecucion.ProgramaProduccionID;

                    cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value =
                        (object?)ejecucion.SolicitudProduccionID ?? DBNull.Value;

                    cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value =
                        (object?)ejecucion.MaquinaID ?? DBNull.Value;

                    cmd.Parameters.Add("@OperadorID", SqlDbType.Int).Value =
                        (object?)ejecucion.OperadorID ?? DBNull.Value;

                    cmd.Parameters.Add("@MotivoParoID", SqlDbType.Int).Value =
                        (object?)vm.MotivoParoID ?? DBNull.Value;

                    cmd.Parameters.Add("@MotivoParoTexto", SqlDbType.NVarChar, 200).Value =
                        (object?)motivoTexto ?? DBNull.Value;

                    cmd.Parameters.Add("@Descripcion", SqlDbType.NVarChar, 500).Value =
                        (object?)vm.Descripcion ?? DBNull.Value;

                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                        usuarioId;

                    await cmd.ExecuteNonQueryAsync();
                }

                await CambiarEstatusEjecucionAsync(
                    ejecucion.EjecucionProduccionID,
                    ProduccionEstatus.Pausado,
                    usuarioId,
                    cn,
                    tx);

                await CambiarEstatusProgramaAsync(
                    ejecucion.ProgramaProduccionID,
                    ProgramaProduccionEstatus.Pausado,
                    usuarioId,
                    cn,
                    tx);

                await tx.CommitAsync();

                TempData["Success"] =
                    "Paro iniciado correctamente.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = vm.EjecucionProduccionID });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                TempData["Error"] =
                    "No fue posible iniciar el paro: " + ex.Message;

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = vm.EjecucionProduccionID });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CerrarParo(
            ProduccionCerrarParoPostVm vm)
        {
            if (!UsuarioEnSesion())
                return RedirectToAction("Login", "Login");

            var usuarioId = ObtenerUsuarioID();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();

            int ejecucionProduccionId;

            try
            {
                const string sqlLeer = @"
SELECT TOP (1)
    ParoID,
    EjecucionProduccionID,
    FechaInicioParo
FROM dbo.Produccion_Paros
WHERE ParoID = @ParoID
  AND Activo = 1
  AND FechaFinParo IS NULL;";

                DateTime fechaInicioParo;

                await using (var cmd = new SqlCommand(sqlLeer, cn, tx))
                {
                    cmd.Parameters.Add("@ParoID", SqlDbType.Int).Value =
                        vm.ParoID;

                    await using var rd = await cmd.ExecuteReaderAsync();

                    if (!await rd.ReadAsync())
                    {
                        await tx.RollbackAsync();

                        TempData["Error"] =
                            "No se encontró un paro abierto para cerrar.";

                        return RedirectToAction(nameof(Index));
                    }

                    ejecucionProduccionId =
                        Convert.ToInt32(rd["EjecucionProduccionID"]);

                    fechaInicioParo =
                        Convert.ToDateTime(rd["FechaInicioParo"]);
                }

                var duracion =
                    (int)Math.Max(
                        0,
                        (DateTime.Now - fechaInicioParo).TotalMinutes);

                const string sqlCerrar = @"
UPDATE dbo.Produccion_Paros
SET
    FechaFinParo = GETDATE(),
    DuracionMinutos = @DuracionMinutos,
    EsMayorA15Minutos = CASE WHEN @DuracionMinutos > 15 THEN 1 ELSE 0 END,
    Descripcion =
        CASE
            WHEN @ObservacionesCierre IS NULL OR LTRIM(RTRIM(@ObservacionesCierre)) = ''
                THEN Descripcion
            WHEN Descripcion IS NULL OR LTRIM(RTRIM(Descripcion)) = ''
                THEN @ObservacionesCierre
            ELSE Descripcion + CHAR(13) + CHAR(10) + 'Cierre: ' + @ObservacionesCierre
        END,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE ParoID = @ParoID
  AND Activo = 1;";

                await using (var cmd = new SqlCommand(sqlCerrar, cn, tx))
                {
                    cmd.Parameters.Add("@ParoID", SqlDbType.Int).Value =
                        vm.ParoID;

                    cmd.Parameters.Add("@DuracionMinutos", SqlDbType.Int).Value =
                        duracion;

                    cmd.Parameters.Add("@ObservacionesCierre", SqlDbType.NVarChar, 500).Value =
                        (object?)vm.ObservacionesCierre ?? DBNull.Value;

                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                        usuarioId;

                    await cmd.ExecuteNonQueryAsync();
                }

                var ejecucion =
                    await ObtenerEjecucionAsync(
                        ejecucionProduccionId,
                        cn,
                        tx);

                if (ejecucion == null)
                {
                    await tx.RollbackAsync();
                    return NotFound();
                }

                await CambiarEstatusEjecucionAsync(
                    ejecucionProduccionId,
                    ProduccionEstatus.EnProduccion,
                    usuarioId,
                    cn,
                    tx);

                await CambiarEstatusProgramaAsync(
                    ejecucion.ProgramaProduccionID,
                    ProgramaProduccionEstatus.EnProduccion,
                    usuarioId,
                    cn,
                    tx);

                await tx.CommitAsync();

                TempData["Success"] =
                    duracion > 15
                        ? "Paro cerrado. Duró más de 15 minutos; queda registrado para seguimiento."
                        : "Paro cerrado correctamente.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = ejecucionProduccionId });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                TempData["Error"] =
                    "No fue posible cerrar el paro: " + ex.Message;

                return RedirectToAction(nameof(Index));
            }
        }

        

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Terminar(
            ProduccionTerminarPostVm vm)
        {
            if (!UsuarioEnSesion())
                return RedirectToAction("Login", "Login");

            var usuarioId = ObtenerUsuarioID();

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

                var tieneParoAbierto =
                    await TieneParoAbiertoAsync(
                        vm.EjecucionProduccionID,
                        cn,
                        tx);

                if (tieneParoAbierto)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "No puedes terminar producción mientras exista un paro abierto.";

                    return RedirectToAction(
                        nameof(Detalle),
                        new { id = vm.EjecucionProduccionID });
                }

                await RecalcularTotalesEjecucionAsync(
                    vm.EjecucionProduccionID,
                    usuarioId,
                    cn,
                    tx);

                var estatusProduccion =
                    vm.TerminarParcial
                        ? ProduccionEstatus.TerminadoParcial
                        : ProduccionEstatus.Terminado;

                const string sqlCerrar = @"
UPDATE dbo.Produccion_Ejecucion
SET
    FechaFinReal = GETDATE(),
    EstatusID = @EstatusID,
    Observaciones =
        CASE
            WHEN @Observaciones IS NULL OR LTRIM(RTRIM(@Observaciones)) = ''
                THEN Observaciones
            WHEN Observaciones IS NULL OR LTRIM(RTRIM(Observaciones)) = ''
                THEN @Observaciones
            ELSE Observaciones + CHAR(13) + CHAR(10) + @Observaciones
        END,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE EjecucionProduccionID = @EjecucionProduccionID
  AND Activo = 1;";

                await using (var cmd = new SqlCommand(sqlCerrar, cn, tx))
                {
                    cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value =
                        vm.EjecucionProduccionID;

                    cmd.Parameters.Add("@EstatusID", SqlDbType.Int).Value =
                        estatusProduccion;

                    cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 500).Value =
                        (object?)vm.Observaciones ?? DBNull.Value;

                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                        usuarioId;

                    await cmd.ExecuteNonQueryAsync();
                }

                await MarcarProgramaTerminadoAsync(
                    ejecucion.ProgramaProduccionID,
                    usuarioId,
                    cn,
                    tx);

                await tx.CommitAsync();

                TempData["Success"] =
                    vm.TerminarParcial
                        ? "Producción terminada parcialmente."
                        : "Producción terminada correctamente.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = vm.EjecucionProduccionID });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                TempData["Error"] =
                    "No fue posible terminar producción: " + ex.Message;

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = vm.EjecucionProduccionID });
            }
        }

        private async Task<ProduccionEjecucionVm?> ObtenerEjecucionAsync(int ejecucionProduccionId, SqlConnection cn)
        {
            const string sql = @"
SELECT EjecucionProduccionID,ProgramaProduccionID,SolicitudProduccionID,SolicitudProduccionDetalleID,ReleaseID,ReleaseDetalleID,
       MaquinaID,MaquinaCodigo,MaquinaNombre,ParteID,NumeroParte,ReferenciaSAP,DescripcionParte,MoldeID,MoldeCodigo,
       ISNULL(EsCambioMolde,0) AS EsCambioMolde,OperadorID,OperadorNombre,FechaInicioReal,FechaFinReal,CantidadPlaneada,
       CantidadOKTotal,CantidadSospechosaTotal,CantidadScrapTotal,EstatusID,Observaciones,UsuarioCreacionID,FechaCreacion,
       UsuarioModificacionID,FechaModificacion,Activo
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
       ISNULL(EsCambioMolde,0) AS EsCambioMolde,OperadorID,OperadorNombre,FechaInicioReal,FechaFinReal,CantidadPlaneada,
       CantidadOKTotal,CantidadSospechosaTotal,CantidadScrapTotal,EstatusID,Observaciones,UsuarioCreacionID,FechaCreacion,
       UsuarioModificacionID,FechaModificacion,Activo
FROM dbo.Produccion_Ejecucion
WHERE EjecucionProduccionID=@EjecucionProduccionID AND Activo=1;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
            await using var rd = await cmd.ExecuteReaderAsync();
            return await rd.ReadAsync() ? MapearEjecucion(rd) : null;
        }

        private async Task<List<ProduccionRegistroHoraVm>> ObtenerRegistrosHoraAsync(
            int ejecucionProduccionId,
            SqlConnection cn)
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
    CantidadOK,
    CantidadSospechosa,
    CantidadScrap,
    Observaciones,
    UsuarioCreacionID,
    FechaCreacion,
    UsuarioModificacionID,
    FechaModificacion,
    Activo
FROM dbo.Produccion_RegistroHora
WHERE EjecucionProduccionID = @EjecucionProduccionID
  AND Activo = 1
ORDER BY FechaProduccion DESC, HoraInicio DESC;";

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value =
                ejecucionProduccionId;

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
                    Observaciones = TextoNullable(rd, "Observaciones"),
                    UsuarioCreacionID = NullableEntero(rd, "UsuarioCreacionID"),
                    FechaCreacion = Fecha(rd, "FechaCreacion"),
                    UsuarioModificacionID = NullableEntero(rd, "UsuarioModificacionID"),
                    FechaModificacion = NullableFecha(rd, "FechaModificacion"),
                    Activo = Booleano(rd, "Activo")
                });
            }

            return lista;
        }

        private async Task<List<ProduccionParoVm>> ObtenerParosAsync(
            int ejecucionProduccionId,
            SqlConnection cn)
        {
            var lista = new List<ProduccionParoVm>();

            const string sql = @"
SELECT
    ParoID,
    EjecucionProduccionID,
    ProgramaProduccionID,
    SolicitudProduccionID,
    MaquinaID,
    OperadorID,
    FechaInicioParo,
    FechaFinParo,
    DuracionMinutos,
    MotivoParoID,
    MotivoParoTexto,
    Descripcion,
    EsMayorA15Minutos,
    UsuarioCreacionID,
    FechaCreacion,
    UsuarioModificacionID,
    FechaModificacion,
    Activo
FROM dbo.Produccion_Paros
WHERE EjecucionProduccionID = @EjecucionProduccionID
  AND Activo = 1
ORDER BY FechaInicioParo DESC;";

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value =
                ejecucionProduccionId;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(new ProduccionParoVm
                {
                    ParoID = Entero(rd, "ParoID"),
                    EjecucionProduccionID = Entero(rd, "EjecucionProduccionID"),
                    ProgramaProduccionID = Entero(rd, "ProgramaProduccionID"),
                    SolicitudProduccionID = NullableEntero(rd, "SolicitudProduccionID"),
                    MaquinaID = NullableEntero(rd, "MaquinaID"),
                    OperadorID = NullableEntero(rd, "OperadorID"),
                    FechaInicioParo = Fecha(rd, "FechaInicioParo"),
                    FechaFinParo = NullableFecha(rd, "FechaFinParo"),
                    DuracionMinutos = NullableEntero(rd, "DuracionMinutos"),
                    MotivoParoID = NullableEntero(rd, "MotivoParoID"),
                    MotivoParoTexto = TextoNullable(rd, "MotivoParoTexto"),
                    Descripcion = TextoNullable(rd, "Descripcion"),
                    EsMayorA15Minutos = Booleano(rd, "EsMayorA15Minutos"),
                    UsuarioCreacionID = NullableEntero(rd, "UsuarioCreacionID"),
                    FechaCreacion = Fecha(rd, "FechaCreacion"),
                    UsuarioModificacionID = NullableEntero(rd, "UsuarioModificacionID"),
                    FechaModificacion = NullableFecha(rd, "FechaModificacion"),
                    Activo = Booleano(rd, "Activo")
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
    COALESCE(NULLIF(pp.MaquinaCodigo, ''), maq.Codigo) AS MaquinaCodigo,
    COALESCE(NULLIF(pp.MaquinaNombre, ''), maq.Nombre) AS MaquinaNombre,

    pp.ParteID,
    pp.NumeroParte,
    pp.ReferenciaSAP,
    pp.DesignacionDescripcionSAP AS DescripcionParte,

    pp.MoldeID,
    pp.MoldeCodigo,

    CONVERT(INT, ISNULL(pp.CantidadProgramada, 0)) AS CantidadProgramada,

pp.FechaInicioProgramada,
pp.FechaFinProgramada,
ISNULL(pp.SecuenciaMaquina, 999999) AS SecuenciaMaquina,
ISNULL(pp.EstatusID, 1) AS EstatusID,

    escala.OperadorSugeridoID,
    escala.OperadorSugeridoNombre,
    escala.TurnoSugeridoNombre,
    escala.TurnoSugeridoColor,
    escala.EscalaAsignacionID

FROM dbo.Planeacion_ProgramaProduccion pp

LEFT JOIN dbo.SolicitudesProduccion s
    ON s.SolicitudProduccionID = pp.SolicitudProduccionID

LEFT JOIN dbo.Planeacion_ReleaseDetalle rd
    ON rd.ReleaseDetalleID = pp.ReleaseDetalleID

LEFT JOIN dbo.ERP_Maquinas maq
    ON maq.MaquinaID = pp.MaquinaID

OUTER APPLY
(
    SELECT TOP (1)
        a.AsignacionID AS EscalaAsignacionID,
        a.PersonalID AS OperadorSugeridoID,
        LTRIM(RTRIM(
            ISNULL(p.Nombre, '') + ' ' +
            ISNULL(p.ApellidoPaterno, '') + ' ' +
            ISNULL(p.ApellidoMaterno, '')
        )) AS OperadorSugeridoNombre,
        et.Nombre AS TurnoSugeridoNombre,
        et.Color AS TurnoSugeridoColor
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
    WHERE pp.MaquinaID IS NOT NULL
      AND pp.FechaInicioProgramada IS NOT NULL
      AND a.Activo = 1
      AND a.MaquinaID = pp.MaquinaID
      AND CAST(pp.FechaInicioProgramada AS date) >= CAST(a.FechaInicio AS date)
      AND CAST(pp.FechaInicioProgramada AS date) <= CAST(a.FechaFin AS date)
      AND
      (
            ISNULL(et.EsFlexible, 0) = 1
         OR et.HoraInicio IS NULL
         OR et.HoraFin IS NULL
         OR
         (
                ISNULL(et.CruzaDiaSiguiente, 0) = 0
            AND CAST(pp.FechaInicioProgramada AS time) >= et.HoraInicio
            AND CAST(pp.FechaInicioProgramada AS time) < et.HoraFin
         )
         OR
         (
                ISNULL(et.CruzaDiaSiguiente, 0) = 1
            AND
            (
                   CAST(pp.FechaInicioProgramada AS time) >= et.HoraInicio
                OR CAST(pp.FechaInicioProgramada AS time) < et.HoraFin
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
            AND CAST(pp.FechaInicioProgramada AS date) >= CAST(n.FechaInicio AS date)
            AND CAST(pp.FechaInicioProgramada AS date) <= CAST(ISNULL(n.FechaFin, n.FechaInicio) AS date)
      )
    ORDER BY
        et.Orden,
        a.AsignacionID DESC
) escala

WHERE pp.Activo = 1
  AND ISNULL(pp.EstatusID, 1) NOT IN (3, 4, 5, 9, 99)
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.Produccion_Ejecucion e
      WHERE e.ProgramaProduccionID = pp.ProgramaProduccionID
        AND e.Activo = 1
        AND e.EstatusID NOT IN (9, 99)
  )
  AND (@MaquinaID IS NULL OR pp.MaquinaID = @MaquinaID)
  AND (@FechaDesde IS NULL OR CONVERT(DATE, pp.FechaInicioProgramada) >= @FechaDesde)
  AND (@FechaHasta IS NULL OR CONVERT(DATE, pp.FechaInicioProgramada) <= @FechaHasta)
  AND
  (
        @Busqueda IS NULL
     OR CONVERT(NVARCHAR(30), pp.ProgramaProduccionID) LIKE '%' + @Busqueda + '%'
     OR s.FolioSolicitud LIKE '%' + @Busqueda + '%'
     OR s.NumeroOFRecibida LIKE '%' + @Busqueda + '%'
     OR pp.NumeroParte LIKE '%' + @Busqueda + '%'
     OR pp.ReferenciaSAP LIKE '%' + @Busqueda + '%'
     OR pp.DesignacionDescripcionSAP LIKE '%' + @Busqueda + '%'
     OR pp.MaquinaCodigo LIKE '%' + @Busqueda + '%'
     OR pp.MaquinaNombre LIKE '%' + @Busqueda + '%'
     OR pp.MoldeCodigo LIKE '%' + @Busqueda + '%'
     OR escala.OperadorSugeridoNombre LIKE '%' + @Busqueda + '%'
  )
ORDER BY
    COALESCE(NULLIF(pp.MaquinaCodigo, ''), maq.Codigo),
    pp.FechaInicioProgramada,
    ISNULL(pp.SecuenciaMaquina, 999999),
    pp.ProgramaProduccionID;";

            await using var cmd = new SqlCommand(sql, cn);

            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value =
                (object?)maquinaId ?? DBNull.Value;

            cmd.Parameters.Add("@FechaDesde", SqlDbType.Date).Value =
                (object?)fechaDesde?.Date ?? DBNull.Value;

            cmd.Parameters.Add("@FechaHasta", SqlDbType.Date).Value =
                (object?)fechaHasta?.Date ?? DBNull.Value;

            cmd.Parameters.Add("@Busqueda", SqlDbType.NVarChar, 200).Value =
                string.IsNullOrWhiteSpace(busqueda)
                    ? DBNull.Value
                    : busqueda.Trim();

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

            public DateTime? Cambio { get; set; }
            public DateTime? Arranque { get; set; }

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

        private async Task<int>
    ObtenerOCrearChecklistsInicialesAsync(
        ProduccionEjecucionVm ejecucion,
        int usuarioId,
        SqlConnection cn,
        SqlTransaction tx)
        {
            if (!ejecucion.SolicitudProduccionID.HasValue ||
                ejecucion.SolicitudProduccionID.Value <= 0)
            {
                throw new InvalidOperationException(
                    "La ejecución no está relacionada con una OF.");
            }

            var fechaOperacion = DateTime.Today;

            /*
             * 1. Siempre se crea el checklist de arranque
             *    y liberación de máquina.
             */
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

            /*
             * 2. Solo se crea cuando Planeación indicó
             *    que existe cambio de molde.
             */
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

            /*
             * 3. El monitoreo de parámetros se realiza
             *    una vez al inicio de la OF.
             */
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

            /*
             * 4. El monitoreo de periféricos se genera
             *    para el turno vigente.
             */
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
        WHEN NULLIF(
            LTRIM(RTRIM(p.EstadoPredeterminado)),
            N''
        ) IS NULL
            THEN N'OK'
        ELSE UPPER(
            LTRIM(RTRIM(p.EstadoPredeterminado))
        )
    END,

    0,

    @UsuarioID,
    GETDATE(),
    1

FROM dbo.ERP_ChecklistArranquePreguntas p

WHERE p.CodigoFormato = @CodigoFormato
  AND p.VersionFormato = @VersionFormato
  AND p.Activo = 1

  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.Produccion_ChecklistArranqueDetalle d
      WHERE d.ChecklistArranqueID =
            @ChecklistArranqueID
        AND d.PreguntaID = p.PreguntaID
        AND d.Activo = 1
  )

ORDER BY
    p.OrdenSeccion,
    p.OrdenPregunta,
    p.PreguntaID;";

            await using var cmd =
                new SqlCommand(
                    sql,
                    cn,
                    tx);

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

        private async Task<bool> TienePreguntasProduccionSinRespuestaAsync(int checklistArranqueId, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
SELECT COUNT(1)
FROM dbo.Produccion_ChecklistArranqueDetalle d
INNER JOIN dbo.ERP_ChecklistArranquePreguntas p ON p.PreguntaID=d.PreguntaID
WHERE d.ChecklistArranqueID=@ChecklistArranqueID AND d.Activo=1 AND p.Activo=1
  AND ISNULL(p.EsPreguntaCalidad,0)=0
  AND ISNULL(p.GrupoResponsable,N'')<>N'CALIDAD'
  AND (ISNULL(d.Confirmado,0)=0 OR NULLIF(LTRIM(RTRIM(ISNULL(d.Resultado,N''))),N'') IS NULL);";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ChecklistArranqueID", SqlDbType.Int).Value = checklistArranqueId;
            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }
        private async Task<bool> TieneNokSinObservacionAsync(int checklistArranqueId, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
SELECT COUNT(1)
FROM dbo.Produccion_ChecklistArranqueDetalle d
INNER JOIN dbo.ERP_ChecklistArranquePreguntas p ON p.PreguntaID=d.PreguntaID
WHERE d.ChecklistArranqueID=@ChecklistArranqueID AND d.Activo=1 AND p.Activo=1
  AND ISNULL(p.EsPreguntaCalidad,0)=0
  AND ISNULL(p.GrupoResponsable,N'')<>N'CALIDAD'
  AND ISNULL(d.Confirmado,0)=1
  AND ((d.Resultado=N'NOK' AND ISNULL(p.RequiereObservacionSiNOK,0)=1)
    OR (d.Resultado=N'NA' AND ISNULL(p.RequiereObservacionSiNA,0)=1))
  AND NULLIF(LTRIM(RTRIM(ISNULL(d.Observaciones,N''))),N'') IS NULL;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ChecklistArranqueID", SqlDbType.Int).Value = checklistArranqueId;
            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
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

        private async Task<ProgramaParaProduccion?> ObtenerProgramaParaIniciarAsync(  int programaProduccionId,  SqlConnection cn,  SqlTransaction tx)
        {
            const string sql = @"
SELECT TOP (1)
    pp.ProgramaProduccionID,
    pp.SolicitudProduccionID,
    pp.SolicitudProduccionDetalleID,
    pp.ReleaseDetalleID,
    rd.ReleaseID,

    pp.MaquinaID,
    COALESCE(NULLIF(pp.MaquinaCodigo, ''), maq.Codigo) AS MaquinaCodigo,
    COALESCE(NULLIF(pp.MaquinaNombre, ''), maq.Nombre) AS MaquinaNombre,

    pp.ParteID,
    pp.NumeroParte,
    pp.ReferenciaSAP,
    pp.DesignacionDescripcionSAP AS DescripcionParte,

    pp.MoldeID,
    pp.MoldeCodigo,

    CONVERT(INT, ISNULL(pp.CantidadProgramada, 0)) AS CantidadPlaneada,

    pp.FechaInicioProgramada,
    pp.FechaFinProgramada,
    pp.Cambio,
    pp.Arranque,

    CASE
        WHEN pp.Cambio IS NOT NULL
         AND pp.Arranque IS NOT NULL
         AND pp.Cambio < pp.Arranque
            THEN 1
        ELSE 0
    END AS EsCambioMolde,

    opPrincipal.PersonaID AS OperadorPrincipalPlaneadoID,
    opPrincipal.NombreCompleto AS OperadorPrincipalPlaneadoNombre,

    opAuxiliar.PersonaID AS OperadorAuxiliarID,
    opAuxiliar.NombreCompleto AS OperadorAuxiliarNombre

FROM dbo.Planeacion_ProgramaProduccion pp

LEFT JOIN dbo.Planeacion_ReleaseDetalle rd
    ON rd.ReleaseDetalleID = pp.ReleaseDetalleID

LEFT JOIN dbo.ERP_Maquinas maq
    ON maq.MaquinaID = pp.MaquinaID

OUTER APPLY
(
    SELECT TOP (1)
        po.PersonaID,
        LTRIM(RTRIM(
            ISNULL(p.Nombre, '') + ' ' +
            ISNULL(p.ApellidoPaterno, '') + ' ' +
            ISNULL(p.ApellidoMaterno, '')
        )) AS NombreCompleto
    FROM dbo.Planeacion_ProgramaOperadores po
    LEFT JOIN dbo.Persona p
        ON p.PersonaID = po.PersonaID
    WHERE po.ProgramaProduccionID = pp.ProgramaProduccionID
      AND po.Activo = 1
      AND UPPER(ISNULL(po.RolOperador, '')) = 'PRINCIPAL'
    ORDER BY po.ProgramaOperadorID
) opPrincipal

OUTER APPLY
(
    SELECT TOP (1)
        po.PersonaID,
        LTRIM(RTRIM(
            ISNULL(p.Nombre, '') + ' ' +
            ISNULL(p.ApellidoPaterno, '') + ' ' +
            ISNULL(p.ApellidoMaterno, '')
        )) AS NombreCompleto
    FROM dbo.Planeacion_ProgramaOperadores po
    LEFT JOIN dbo.Persona p
        ON p.PersonaID = po.PersonaID
    WHERE po.ProgramaProduccionID = pp.ProgramaProduccionID
      AND po.Activo = 1
      AND UPPER(ISNULL(po.RolOperador, '')) = 'AUXILIAR'
    ORDER BY po.ProgramaOperadorID
) opAuxiliar

WHERE pp.ProgramaProduccionID = @ProgramaProduccionID
  AND pp.Activo = 1
  AND ISNULL(pp.EstatusID, 1) NOT IN (3, 4, 5, 8, 9, 99);";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value =
                programaProduccionId;

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

                Cambio = NullableFecha(rd, "Cambio"),
                Arranque = NullableFecha(rd, "Arranque"),

                EsCambioMolde = Booleano(rd, "EsCambioMolde"),

                OperadorPrincipalPlaneadoID = NullableEntero(rd, "OperadorPrincipalPlaneadoID"),
                OperadorPrincipalPlaneadoNombre = TextoNullable(rd, "OperadorPrincipalPlaneadoNombre"),

                OperadorAuxiliarID = NullableEntero(rd, "OperadorAuxiliarID"),
                OperadorAuxiliarNombre = TextoNullable(rd, "OperadorAuxiliarNombre")
            };
        }


        private async Task<int> InsertarEjecucionAsync(   ProgramaParaProduccion programa, int? operadorId,   string? operadorNombre,string? observaciones,  int usuarioId, SqlConnection cn,  SqlTransaction tx)
        {
            const string sql = @"
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
);";

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
                (object?)operadorNombre ?? DBNull.Value;

            cmd.Parameters.Add("@OperadorAuxiliarID", SqlDbType.Int).Value =
                (object?)programa.OperadorAuxiliarID ?? DBNull.Value;

            cmd.Parameters.Add("@OperadorAuxiliarNombre", SqlDbType.NVarChar, 200).Value =
                (object?)programa.OperadorAuxiliarNombre ?? DBNull.Value;

            cmd.Parameters.Add("@EsCambioMolde", SqlDbType.Bit).Value =
                programa.EsCambioMolde;

            cmd.Parameters.Add("@FechaCambioMoldeProgramada", SqlDbType.DateTime).Value =
                (object?)programa.Cambio ?? DBNull.Value;

            cmd.Parameters.Add("@FechaArranqueProgramada", SqlDbType.DateTime).Value =
                (object?)programa.Arranque ??
                (object?)programa.FechaInicioProgramada ??
                DBNull.Value;

            cmd.Parameters.Add("@CantidadPlaneada", SqlDbType.Int).Value =
                (object?)programa.CantidadPlaneada ?? DBNull.Value;

            cmd.Parameters.Add("@EstatusID", SqlDbType.Int).Value =
                ProduccionEstatus.EnPreparacion;

            cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 500).Value =
                (object?)observaciones ?? DBNull.Value;

            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                usuarioId;

            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
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

            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value =
                ejecucionProduccionId;

            await using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
                return null;

            return new ProduccionChecklistResumenVm
            {
                ChecklistArranqueID = Entero(rd, "ChecklistArranqueID"),
                EjecucionProduccionID = Entero(rd, "EjecucionProduccionID"),
                ProgramaProduccionID = Entero(rd, "ProgramaProduccionID"),

                MaquinaCodigo = TextoNullable(rd, "MaquinaCodigo"),
                MaquinaNombre = TextoNullable(rd, "MaquinaNombre"),

                ReferenciaSAP = TextoNullable(rd, "ReferenciaSAP"),
                NumeroParte = TextoNullable(rd, "NumeroParte"),
                DescripcionParte = TextoNullable(rd, "DescripcionParte"),

                CodigoFormato = TextoNullable(rd, "CodigoFormato") ?? "GQ-F-PR01-06",
                VersionFormato = TextoNullable(rd, "VersionFormato"),

                EstatusID = Entero(rd, "EstatusID"),
                FechaChecklist = Fecha(rd, "FechaChecklist"),

                TotalPreguntas = Entero(rd, "TotalPreguntas"),
                TotalRespondidas = Entero(rd, "TotalRespondidas"),
                TotalNOK = Entero(rd, "TotalNOK")
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

        private async Task<bool> ChecklistPermiteIniciarSerieAsync(
            int ejecucionProduccionId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT CAST(
    CASE WHEN EXISTS
    (
        SELECT 1
        FROM dbo.Calidad_Inspecciones ci
        INNER JOIN dbo.Produccion_ChecklistArranque c
            ON c.ChecklistArranqueID = ci.ChecklistArranqueID
           AND c.Activo = 1
        WHERE ci.EjecucionProduccionID = @EjecucionProduccionID
          AND c.EjecucionProduccionID = @EjecucionProduccionID
          AND c.EstatusID = @ChecklistValidado
          AND ci.Estado = 'PRODUCCION_LIBERADA'
          AND ISNULL(ci.Liberado, 0) = 1
          AND ISNULL(ci.ConfiguracionInvalidada, 0) = 0
          AND ISNULL(ci.RequiereReliberacion, 0) = 0
    ) THEN 1 ELSE 0 END
AS BIT);";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value =
                ejecucionProduccionId;
            cmd.Parameters.Add("@ChecklistValidado", SqlDbType.Int).Value =
                ProduccionChecklistEstatus.ValidadoPorCalidad;

            var result = await cmd.ExecuteScalarAsync();
            return result != null && result != DBNull.Value && Convert.ToBoolean(result);
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

        // ============================================================
        // CATÁLOGOS
        // ============================================================

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

        private async Task<bool> TieneValoresRequeridosSinCapturarAsync(int checklistArranqueId, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
SELECT COUNT(1)
FROM dbo.Produccion_ChecklistArranqueDetalle d
INNER JOIN dbo.ERP_ChecklistArranquePreguntas p ON p.PreguntaID=d.PreguntaID
WHERE d.ChecklistArranqueID=@ChecklistArranqueID AND d.Activo=1 AND p.Activo=1 AND ISNULL(d.Confirmado,0)=1
  AND p.TipoRespuesta IN (N'NUMERICO',N'ESTADO_Y_VALOR')
  AND NULLIF(LTRIM(RTRIM(ISNULL(d.ValorCapturado,N''))),N'') IS NULL;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ChecklistArranqueID", SqlDbType.Int).Value = checklistArranqueId;
            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
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

                FechaInicioReal = NullableFecha(rd, "FechaInicioReal"),
                FechaFinReal = NullableFecha(rd, "FechaFinReal"),

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
                Activo = Booleano(rd, "Activo")
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

        private async Task<List<SelectListItem>> CargarOperadoresProduccionAsync(
    SqlConnection cn)
        {
            var lista = new List<SelectListItem>
    {
        new SelectListItem
        {
            Value = "",
            Text = "-- Sin operador / seleccionar manualmente --"
        }
    };

            const string sql = @"
SELECT
    PersonaID,
    LTRIM(RTRIM(
        ISNULL(Nombre, '') + ' ' +
        ISNULL(ApellidoPaterno, '') + ' ' +
        ISNULL(ApellidoMaterno, '')
    )) AS NombreCompleto,
    Puesto
FROM dbo.Persona
WHERE EsColaboradorActivo = 1
  AND
  (
        UPPER(LTRIM(RTRIM(ISNULL(Puesto, '')))) LIKE '%OPERADOR%'
     OR UPPER(LTRIM(RTRIM(ISNULL(Puesto, '')))) LIKE '%PRODUCCION%'
     OR UPPER(LTRIM(RTRIM(ISNULL(Puesto, '')))) LIKE '%PRODUCCIÓN%'
  )
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
                var puesto = TextoNullable(rd, "Puesto");

                lista.Add(new SelectListItem
                {
                    Value = personaId.ToString(),
                    Text = string.IsNullOrWhiteSpace(puesto)
                        ? nombre
                        : nombre + " - " + puesto
                });
            }

            return lista;
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