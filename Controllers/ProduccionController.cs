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
        public async Task<IActionResult> Index(
            string? busqueda = null,
            int? maquinaId = null,
            int? estatusId = null,
            DateTime? fechaDesde = null,
            DateTime? fechaHasta = null)
        {
            if (!UsuarioEnSesion())
            {
                return RedirectToAction(
                    "Login",
                    "Login");
            }

            var vm =
                new ProduccionBandejaVm
                {
                    Busqueda =
                        busqueda?.Trim(),

                    MaquinaID =
                        maquinaId,

                    EstatusID =
                        estatusId,

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
                await CargarMaquinasAsync(cn);

            vm.Estatus =
                await CargarEstatusProduccionAsync(cn);

            ViewBag.OperadoresProduccion =
                await CargarOperadoresProduccionAsync(
                    cn);



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
                await ObtenerMaquinasOcupadasAsync(
                    cn);

            vm.ProximosAIniciar =
                vm.ProgramasDisponibles
                    .Where(x =>
                        x.PuedeIniciar &&

                        x.MaquinaID.HasValue &&

                        !maquinasOcupadas.Contains(
                            x.MaquinaID.Value) &&

                        x.FechaInicioProgramada.HasValue &&


                        x.FechaInicioProgramada.Value <=
                            limiteProximos)
                    .OrderBy(x =>
                        x.FechaInicioProgramada)
                    .ThenBy(x =>
                        x.MaquinaCodigo)
                    .ThenBy(x =>
                        x.ProgramaProduccionID)
                    .ToList();


            try
            {
                vm.AlertasReprogramacion =
                    await ObtenerAlertasReprogramacionProduccionAsync(
                        maquinaId,
                        cn);
            }
            catch (Exception ex)
            {
             

                vm.AlertasReprogramacion =
                    new List<ProduccionAlertaReprogramacionVm>();

                ViewBag.ErrorAlertasReprogramacion =
                    "No fue posible consultar los movimientos recientes: "
                    + ex.Message;
            }

   

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
    ISNULL(
        e.EsCambioMolde,
        0
    ) AS EsCambioMolde,

    e.OperadorID,
    e.OperadorNombre,

    e.OperadorAuxiliarID,
    e.OperadorAuxiliarNombre,

    ISNULL(
        e.OperadoresModificadosManual,
        0
    ) AS OperadoresModificadosManual,

    e.MotivoCambioOperadores,

    e.FechaInicioReal,
    e.FechaFinReal,

    e.CantidadPlaneada,

    ISNULL(
        e.CantidadOKTotal,
        0
    ) AS CantidadOKTotal,

    ISNULL(
        e.CantidadSospechosaTotal,
        0
    ) AS CantidadSospechosaTotal,

    ISNULL(
        e.CantidadScrapTotal,
        0
    ) AS CantidadScrapTotal,

    e.EstatusID,
    e.Observaciones,

    e.UsuarioCreacionID,
    e.FechaCreacion,

    e.UsuarioModificacionID,
    e.FechaModificacion,

    e.Activo

FROM dbo.Produccion_Ejecucion e

WHERE e.Activo = 1

AND
(
       @MaquinaID IS NULL
    OR e.MaquinaID = @MaquinaID
)

AND
(
       @EstatusID IS NULL
    OR e.EstatusID = @EstatusID
)

AND
(
       @FechaDesde IS NULL
    OR CONVERT(
           DATE,
           e.FechaCreacion
       ) >= @FechaDesde
)

AND
(
       @FechaHasta IS NULL
    OR CONVERT(
           DATE,
           e.FechaCreacion
       ) <= @FechaHasta
)

AND
(
       @Busqueda IS NULL

    OR e.MaquinaCodigo
        LIKE N'%' + @Busqueda + N'%'

    OR e.MaquinaNombre
        LIKE N'%' + @Busqueda + N'%'

    OR e.NumeroParte
        LIKE N'%' + @Busqueda + N'%'

    OR e.ReferenciaSAP
        LIKE N'%' + @Busqueda + N'%'

    OR e.DescripcionParte
        LIKE N'%' + @Busqueda + N'%'

    OR e.MoldeCodigo
        LIKE N'%' + @Busqueda + N'%'

    OR e.OperadorNombre
        LIKE N'%' + @Busqueda + N'%'

    OR e.OperadorAuxiliarNombre
        LIKE N'%' + @Busqueda + N'%'

    OR CONVERT(
           NVARCHAR(30),
           e.ProgramaProduccionID
       )
       LIKE N'%' + @Busqueda + N'%'

    OR CONVERT(
           NVARCHAR(30),
           e.SolicitudProduccionID
       )
       LIKE N'%' + @Busqueda + N'%'
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

            await using var cmd =
                new SqlCommand(
                    sql,
                    cn);

            cmd.Parameters.Add(
                "@MaquinaID",
                SqlDbType.Int).Value =
                maquinaId.HasValue &&
                maquinaId.Value > 0
                    ? maquinaId.Value
                    : DBNull.Value;

            cmd.Parameters.Add(
                "@EstatusID",
                SqlDbType.Int).Value =
                estatusId.HasValue &&
                estatusId.Value > 0
                    ? estatusId.Value
                    : DBNull.Value;

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
                string.IsNullOrWhiteSpace(
                    busqueda)
                    ? DBNull.Value
                    : busqueda.Trim();

            await using var rd =
                await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                vm.Ejecuciones.Add(
                    MapearEjecucion(rd));
            }

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
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "No fue posible preparar el monitoreo de periféricos " +
                        "del turno actual: " + ex.Message;
                }
            }

            var calidadResumen =
                await ObtenerResumenCalidadAsync(id, cn);

            var vm = new ProduccionDetalleVm
            {
                Ejecucion = ejecucion,
                RegistrosHora = await ObtenerRegistrosHoraAsync(id, cn),
                Paros = await ObtenerParosAsync(id, cn),
                MotivosParo = await CargarMotivosParoAsync(cn),
                ChecklistResumen = await ObtenerResumenChecklistArranqueAsync(id, cn),
                CalidadResumen = calidadResumen,
                MonitoreoTurnoActual = monitoreoTurnoActual
            };

            vm.RecepcionesOF =
                await ObtenerEntregasAlmacenOFAsync(
                    ejecucion,
                    cn,
                    null);

           
            ViewBag.EsReinicioSerie =
                ejecucion.EstatusID == ProduccionEstatus.EnPreparacion &&
                await EsReinicioSeriePendienteAsync(id, cn);

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


        private async Task CrearOActualizarSolicitudCalidadAsync(int checklistArranqueId, int ejecucionProduccionId, int usuarioId, SqlConnection cn, SqlTransaction tx)
        {
            var origen = await ObtenerOrigenSolicitudCalidadAsync(checklistArranqueId, ejecucionProduccionId, cn, tx);
            if (origen == null) throw new InvalidOperationException("No se encontró la información de la ejecución y el checklist para enviar a Calidad.");
            const string sqlInspeccionExistente = @"
SELECT TOP (1)
    InspeccionID,
    ChecklistArranqueID,
    Estado,
    ISNULL(ConfiguracionInvalidada,0) AS ConfiguracionInvalidada
FROM dbo.Calidad_Inspecciones WITH (UPDLOCK,HOLDLOCK)
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
                    checklistExistenteId = rd["ChecklistArranqueID"] == DBNull.Value ? null : Convert.ToInt32(rd["ChecklistArranqueID"]);
                    estadoExistente = rd["Estado"] == DBNull.Value ? null : rd["Estado"].ToString();
                    configuracionInvalidada = rd["ConfiguracionInvalidada"] != DBNull.Value && Convert.ToBoolean(rd["ConfiguracionInvalidada"]);
                }
            }
            if (inspeccionId.HasValue)
            {
                if (configuracionInvalidada) throw new InvalidOperationException("La inspección actual fue invalidada y requiere un proceso de reliberación. No debe generarse una inspección nueva.");
                const string sqlActualizar = @"
UPDATE dbo.Calidad_Inspecciones
SET ProgramaProduccionID=@ProgramaProduccionID,
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
    CantidadPendiente=CASE WHEN ISNULL(CantidadRevisada,0)>=@CantidadTotal THEN 0 ELSE @CantidadTotal-ISNULL(CantidadRevisada,0) END,
    ChecklistValidado=1,
    FechaNotificacionCalidad=GETDATE(),
    UsuarioNotificoID=@UsuarioID,
    MotivoDevolucion=NULL,
    Estado=CASE
        WHEN Estado IN(N'PENDIENTE_PREARRANQUE',N'DEVUELTO_PREARRANQUE',N'ARRANQUE_AUTORIZADO') THEN N'PENDIENTE_PREARRANQUE'
        ELSE Estado
    END,
    Observaciones=CASE
        WHEN Observaciones IS NULL OR LTRIM(RTRIM(Observaciones))=N'' THEN N'Producción envió el checklist de prearranque a Calidad.'
        ELSE Observaciones+CHAR(13)+CHAR(10)+N'Producción actualizó y reenvió el checklist de prearranque.'
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
                    if (filasActualizadas <= 0) throw new InvalidOperationException("No fue posible actualizar la inspección existente de Calidad.");
                }
                var estadoNuevo = estadoExistente is "PENDIENTE_PREARRANQUE" or "DEVUELTO_PREARRANQUE" or "ARRANQUE_AUTORIZADO" ? "PENDIENTE_PREARRANQUE" : estadoExistente ?? "PENDIENTE_PREARRANQUE";
                await RegistrarHistorialEnvioCalidadAsync(inspeccionId.Value, estadoExistente, estadoNuevo, checklistExistenteId == checklistArranqueId ? "Producción reenvió el checklist de prearranque a Calidad." : "Producción actualizó el checklist asociado y envió nuevamente la solicitud a Calidad.", usuarioId, cn, tx);
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
                if (resultado == null || resultado == DBNull.Value) throw new InvalidOperationException("No fue posible crear la inspección de Calidad.");
                nuevaInspeccionId = Convert.ToInt32(resultado);
            }
            await RegistrarHistorialEnvioCalidadAsync(nuevaInspeccionId, null, "PENDIENTE_PREARRANQUE", "Producción envió la corrida a Calidad para revisión de prearranque.", usuarioId, cn, tx);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Iniciar(
    int programaProduccionId,
    int? operadorId = null,
    string? operadorNombre = null,
    int? operadorAuxiliarId = null,
    string? operadorAuxiliarNombre = null,
    string? observaciones = null)
        {
            if (!UsuarioEnSesion())
                return RedirectToAction("Login", "Login");

            if (programaProduccionId <= 0)
            {
                TempData["Error"] =
                    "No se recibió correctamente el programa de producción.";

                return RedirectToAction(nameof(Index));
            }

            operadorNombre =
                string.IsNullOrWhiteSpace(operadorNombre)
                    ? null
                    : operadorNombre.Trim();

            operadorAuxiliarNombre =
                string.IsNullOrWhiteSpace(operadorAuxiliarNombre)
                    ? null
                    : operadorAuxiliarNombre.Trim();

            observaciones =
                string.IsNullOrWhiteSpace(observaciones)
                    ? null
                    : observaciones.Trim();

            if (operadorId.HasValue && operadorId.Value <= 0)
                operadorId = null;

            if (operadorAuxiliarId.HasValue &&
                operadorAuxiliarId.Value <= 0)
            {
                operadorAuxiliarId = null;
            }

            /*
             * Si se seleccionó una persona del catálogo,
             * el ID es la fuente de verdad.
             *
             * Cualquier texto residual que haya llegado desde
             * el modal se ignora para evitar falsos positivos.
             */
            if (operadorId.HasValue)
                operadorNombre = null;

            if (operadorAuxiliarId.HasValue)
                operadorAuxiliarNombre = null;

            if (operadorNombre?.Length > 200)
            {
                TempData["Error"] =
                    "El nombre manual del operador principal no puede superar 200 caracteres.";

                return RedirectToAction(nameof(Index));
            }

            if (operadorAuxiliarNombre?.Length > 200)
            {
                TempData["Error"] =
                    "El nombre manual del operador auxiliar no puede superar 200 caracteres.";

                return RedirectToAction(nameof(Index));
            }

            if (observaciones?.Length > 500)
            {
                TempData["Error"] =
                    "Las observaciones no pueden superar 500 caracteres.";

                return RedirectToAction(nameof(Index));
            }

            var usuarioId = ObtenerUsuarioID();

            await using var cn =
                new SqlConnection(ConnectionString);

            await cn.OpenAsync();

            await using var tx =
                (SqlTransaction)await cn.BeginTransactionAsync(
                    IsolationLevel.Serializable);

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
                        "No se encontró el programa de producción o ya no está disponible para iniciar.";

                    return RedirectToAction(nameof(Index));
                }

                if (!programa.MaquinaID.HasValue ||
                    programa.MaquinaID.Value <= 0)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "El programa no tiene máquina asignada. No puede iniciar preparación.";

                    return RedirectToAction(nameof(Index));
                }

                const string sqlMaquinaOcupada = @"
SELECT TOP (1)
    e.EjecucionProduccionID,
    e.ProgramaProduccionID,
    e.MaquinaID,
    e.MaquinaCodigo,
    e.MaquinaNombre,
    e.OperadorID,
    e.OperadorNombre,
    e.EstatusID,
    e.FechaInicioReal
FROM dbo.Produccion_Ejecucion e WITH(UPDLOCK,HOLDLOCK)
WHERE e.Activo = 1
  AND e.MaquinaID = @MaquinaID
  AND e.ProgramaProduccionID <> @ProgramaProduccionID
  AND e.EstatusID IN(@EnPreparacion,@EnProduccion,@Pausado)
ORDER BY e.EjecucionProduccionID DESC;";

                int? ejecucionOcupanteId = null;
                int? programaOcupanteId = null;
                int? estatusOcupanteId = null;
                string? maquinaOcupadaCodigo = null;
                string? operadorOcupante = null;
                DateTime? fechaInicioOcupante = null;

                await using (var cmdOcupada =
                    new SqlCommand(sqlMaquinaOcupada, cn, tx))
                {
                    cmdOcupada.Parameters
                        .Add("@MaquinaID", SqlDbType.Int)
                        .Value = programa.MaquinaID.Value;

                    cmdOcupada.Parameters
                        .Add("@ProgramaProduccionID", SqlDbType.Int)
                        .Value = programaProduccionId;

                    cmdOcupada.Parameters
                        .Add("@EnPreparacion", SqlDbType.Int)
                        .Value = ProduccionEstatus.EnPreparacion;

                    cmdOcupada.Parameters
                        .Add("@EnProduccion", SqlDbType.Int)
                        .Value = ProduccionEstatus.EnProduccion;

                    cmdOcupada.Parameters
                        .Add("@Pausado", SqlDbType.Int)
                        .Value = ProduccionEstatus.Pausado;

                    await using var rdOcupada =
                        await cmdOcupada.ExecuteReaderAsync();

                    if (await rdOcupada.ReadAsync())
                    {
                        ejecucionOcupanteId =
                            Convert.ToInt32(
                                rdOcupada["EjecucionProduccionID"]);

                        programaOcupanteId =
                            Convert.ToInt32(
                                rdOcupada["ProgramaProduccionID"]);

                        estatusOcupanteId =
                            Convert.ToInt32(
                                rdOcupada["EstatusID"]);

                        maquinaOcupadaCodigo =
                            rdOcupada["MaquinaCodigo"] == DBNull.Value
                                ? null
                                : rdOcupada["MaquinaCodigo"].ToString();

                        operadorOcupante =
                            rdOcupada["OperadorNombre"] == DBNull.Value
                                ? null
                                : rdOcupada["OperadorNombre"].ToString();

                        fechaInicioOcupante =
                            rdOcupada["FechaInicioReal"] == DBNull.Value
                                ? null
                                : Convert.ToDateTime(
                                    rdOcupada["FechaInicioReal"]);
                    }
                }

                if (ejecucionOcupanteId.HasValue)
                {
                    await tx.RollbackAsync();

                    var maquinaTexto =
                        !string.IsNullOrWhiteSpace(maquinaOcupadaCodigo)
                            ? maquinaOcupadaCodigo.Trim()
                            : programa.MaquinaCodigo ?? "seleccionada";

                    var estatusTexto =
                        estatusOcupanteId switch
                        {
                            ProduccionEstatus.EnPreparacion =>
                                "en preparación",

                            ProduccionEstatus.EnProduccion =>
                                "en producción",

                            ProduccionEstatus.Pausado =>
                                "pausada",

                            _ =>
                                "activa"
                        };

                    var mensaje =
                        $"No puedes iniciar esta OF porque la máquina {maquinaTexto} " +
                        $"está ocupada por el Programa {programaOcupanteId}, " +
                        $"ejecución {ejecucionOcupanteId}, actualmente {estatusTexto}.";

                    if (!string.IsNullOrWhiteSpace(operadorOcupante))
                    {
                        mensaje +=
                            " Operador: " +
                            operadorOcupante.Trim() +
                            ".";
                    }

                    if (fechaInicioOcupante.HasValue)
                    {
                        mensaje +=
                            " Inicio real: " +
                            fechaInicioOcupante.Value
                                .ToString("dd/MM/yyyy HH:mm") +
                            ".";
                    }

                    mensaje +=
                        " Termina o libera la ejecución anterior antes de iniciar la siguiente preparación.";

                    TempData["Error"] = mensaje;

                    return RedirectToAction(nameof(Index));
                }

                int? operadorPrincipalFinalId =
                    operadorId;

                string? operadorPrincipalFinalNombre =
                    operadorNombre;

                /*
                 * Si no hubo selección explícita,
                 * conserva el operador planeado.
                 */
                if (!operadorPrincipalFinalId.HasValue &&
                    string.IsNullOrWhiteSpace(
                        operadorPrincipalFinalNombre) &&
                    programa.OperadorPrincipalPlaneadoID.HasValue)
                {
                    operadorPrincipalFinalId =
                        programa.OperadorPrincipalPlaneadoID;
                }

                /*
                 * Si existe PersonaID, el nombre SIEMPRE
                 * se obtiene del catálogo.
                 */
                if (operadorPrincipalFinalId.HasValue)
                {
                    operadorPrincipalFinalNombre =
                        await ObtenerNombreOperadorProduccionAsync(
                            operadorPrincipalFinalId.Value,
                            cn,
                            tx);

                    if (string.IsNullOrWhiteSpace(
                        operadorPrincipalFinalNombre))
                    {
                        await tx.RollbackAsync();

                        TempData["Error"] =
                            "El operador principal seleccionado no está activo o su puesto no es OPERADOR.";

                        return RedirectToAction(nameof(Index));
                    }
                }

                if (!operadorPrincipalFinalId.HasValue &&
                    string.IsNullOrWhiteSpace(
                        operadorPrincipalFinalNombre))
                {
                    var operadorSugerido =
                        await ObtenerOperadorSugeridoProduccionAsync(
                            programa.MaquinaID.Value,
                            DateTime.Now,
                            cn,
                            tx);

                    if (operadorSugerido != null)
                    {
                        var nombreValidado =
                            await ObtenerNombreOperadorProduccionAsync(
                                operadorSugerido.OperadorID,
                                cn,
                                tx);

                        if (!string.IsNullOrWhiteSpace(
                            nombreValidado))
                        {
                            operadorPrincipalFinalId =
                                operadorSugerido.OperadorID;

                            operadorPrincipalFinalNombre =
                                nombreValidado;
                        }
                    }
                }

                if (!operadorPrincipalFinalId.HasValue &&
                    string.IsNullOrWhiteSpace(
                        operadorPrincipalFinalNombre))
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "Debes indicar un operador principal antes de iniciar la preparación. " +
                        "Selecciona un operador del catálogo o captura su nombre manual.";

                    return RedirectToAction(nameof(Index));
                }

                int? operadorAuxiliarFinalId =
                    operadorAuxiliarId;

                string? operadorAuxiliarFinalNombre =
                    operadorAuxiliarNombre;

                if (!operadorAuxiliarFinalId.HasValue &&
                    string.IsNullOrWhiteSpace(
                        operadorAuxiliarFinalNombre) &&
                    programa.OperadorAuxiliarID.HasValue)
                {
                    operadorAuxiliarFinalId =
                        programa.OperadorAuxiliarID;
                }

                if (operadorAuxiliarFinalId.HasValue)
                {
                    operadorAuxiliarFinalNombre =
                        await ObtenerNombreOperadorProduccionAsync(
                            operadorAuxiliarFinalId.Value,
                            cn,
                            tx);

                    if (string.IsNullOrWhiteSpace(
                        operadorAuxiliarFinalNombre))
                    {
                        await tx.RollbackAsync();

                        TempData["Error"] =
                            "El operador auxiliar seleccionado no está activo o su puesto no es OPERADOR.";

                        return RedirectToAction(nameof(Index));
                    }
                }

                if (operadorPrincipalFinalId.HasValue &&
                    operadorAuxiliarFinalId.HasValue &&
                    operadorPrincipalFinalId.Value ==
                    operadorAuxiliarFinalId.Value)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "El operador principal y el operador auxiliar no pueden ser la misma persona.";

                    return RedirectToAction(nameof(Index));
                }

                if (!string.IsNullOrWhiteSpace(
                        operadorPrincipalFinalNombre) &&
                    !string.IsNullOrWhiteSpace(
                        operadorAuxiliarFinalNombre) &&
                    string.Equals(
                        operadorPrincipalFinalNombre.Trim(),
                        operadorAuxiliarFinalNombre.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "El operador principal y el operador auxiliar no pueden ser la misma persona.";

                    return RedirectToAction(nameof(Index));
                }

                if (programa.ParteID.HasValue && programa.ParteID.Value > 0 &&
                    await ParteTienePolivalenciaProduccionAsync(programa.ParteID.Value, cn, tx))
                {
                    if (!operadorPrincipalFinalId.HasValue)
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] =
                            "Esta pieza tiene matriz de polivalencia. Selecciona un operador evaluado de la lista; no se permite nombre manual para esta pieza.";
                        return RedirectToAction(nameof(Index));
                    }

                    var nivelPolivalencia =
                        await ObtenerNivelPolivalenciaProduccionAsync(
                            programa.ParteID.Value,
                            operadorPrincipalFinalId.Value,
                            cn,
                            tx);

                    if (!nivelPolivalencia.HasValue)
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] =
                            "El operador seleccionado no está evaluado para el número de parte de este programa. Selecciona un operador N1–N4 de la matriz de polivalencia.";
                        return RedirectToAction(nameof(Index));
                    }
                }

                // NSQ_ESCALA_OPERADORES_V5_BEGIN
                if (!operadorPrincipalFinalId.HasValue)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "Selecciona un operador principal de la lista.";
                    return RedirectToAction(nameof(Index));
                }

                var partePolivalenciaValidacion =
                    await ResolverPartePolivalenciaProgramaAsync(
                        programa.ProgramaProduccionID,
                        programa.ParteID,
                        cn,
                        tx);

                var tieneMatrizPolivalencia =
                    partePolivalenciaValidacion.HasValue &&
                    await ParteTienePolivalenciaProduccionAsync(
                        partePolivalenciaValidacion.Value,
                        cn,
                        tx);

                if (tieneMatrizPolivalencia)
                {
                    var nivelSeleccionado =
                        await ObtenerNivelPolivalenciaProduccionAsync(
                            partePolivalenciaValidacion!.Value,
                            operadorPrincipalFinalId.Value,
                            cn,
                            tx);

                    if (!nivelSeleccionado.HasValue ||
                        nivelSeleccionado.Value < 1 ||
                        nivelSeleccionado.Value > 4)
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] =
                            "Para esta pieza solo puedes seleccionar operadores evaluados N1-N4 en la matriz de polivalencia.";
                        return RedirectToAction(nameof(Index));
                    }
                }
                else if (!await PersonaEsOperadorActivoProduccionAsync(
                             operadorPrincipalFinalId.Value,
                             cn,
                             tx))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] =
                        "La pieza no tiene matriz; selecciona una persona activa del catalogo de Operadores.";
                    return RedirectToAction(nameof(Index));
                }

                if (operadorAuxiliarFinalId.HasValue &&
                    !await PersonaEsAuxiliarActivoProduccionAsync(
                        operadorAuxiliarFinalId.Value,
                        cn,
                        tx))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] =
                        "El auxiliar debe seleccionarse del catalogo activo de Auxiliares.";
                    return RedirectToAction(nameof(Index));
                }

                if (!operadorAuxiliarFinalId.HasValue &&
                    !string.IsNullOrWhiteSpace(operadorAuxiliarFinalNombre))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] =
                        "Selecciona el auxiliar desde la lista; no se permite captura manual.";
                    return RedirectToAction(nameof(Index));
                }
                // NSQ_ESCALA_OPERADORES_V5_END
var observacionesFinales =
                    observaciones;

                var textoOperadores =
                    "Operadores al iniciar preparación. Principal: " +
                    operadorPrincipalFinalNombre!.Trim() +
                    ".";

                if (!string.IsNullOrWhiteSpace(
                    operadorAuxiliarFinalNombre))
                {
                    textoOperadores +=
                        " Auxiliar: " +
                        operadorAuxiliarFinalNombre.Trim() +
                        ".";
                }
                else
                {
                    textoOperadores +=
                        " Auxiliar: sin asignar.";
                }

                observacionesFinales =
                    string.IsNullOrWhiteSpace(
                        observacionesFinales)
                        ? textoOperadores
                        : observacionesFinales +
                          Environment.NewLine +
                          textoOperadores;

                if (observacionesFinales.Length > 500)
                {
                    observacionesFinales =
                        observacionesFinales[..500];
                }

                var ejecucionId =
                    await InsertarEjecucionAsync(
                        programa,
                        operadorPrincipalFinalId,
                        operadorPrincipalFinalNombre,
                        operadorAuxiliarFinalId,
                        operadorAuxiliarFinalNombre,
                        observacionesFinales,
                        usuarioId,
                        cn,
                        tx);

                await SincronizarOperadorProgramaAsync(
                    programaProduccionId,
                    operadorPrincipalFinalId,
                    "PRINCIPAL",
                    usuarioId,
                    cn,
                    tx);

                await SincronizarOperadorProgramaAsync(
                    programaProduccionId,
                    operadorAuxiliarFinalId,
                    "AUXILIAR",
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
                    string.IsNullOrWhiteSpace(
                        operadorAuxiliarFinalNombre)
                        ? "Preparación iniciada correctamente con operador principal confirmado. Continúa con el checklist de arranque."
                        : "Preparación iniciada correctamente con operador principal y auxiliar confirmados. Continúa con el checklist de arranque.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = ejecucionId });
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
                    "No fue posible iniciar la preparación: " +
                    ex.Message;

                return RedirectToAction(nameof(Index));
            }
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> IniciarSerie(
    int ejecucionProduccionId)
        {
            if (!UsuarioEnSesion())
                return RedirectToAction("Login", "Login");

            if (ejecucionProduccionId <= 0)
            {
                TempData["Error"] =
                    "No se recibió la ejecución de producción.";

                return RedirectToAction(nameof(Index));
            }

            var usuarioId = ObtenerUsuarioID();

            await using var cn =
                new SqlConnection(ConnectionString);

            await cn.OpenAsync();

            await using var tx =
                (SqlTransaction)await cn.BeginTransactionAsync(
                    IsolationLevel.Serializable);

            try
            {
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

                if (ejecucion.EstatusID ==
                    ProduccionEstatus.EnProduccion)
                {
                    await tx.CommitAsync();

                    TempData["Info"] =
                        "La producción ya se encuentra en serie.";

                    return RedirectToAction(
                        nameof(Detalle),
                        new { id = ejecucionProduccionId });
                }

                if (ejecucion.EstatusID !=
                    ProduccionEstatus.EnPreparacion)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "Solo puedes iniciar o reiniciar serie cuando " +
                        "la ejecución está en preparación.";

                    return RedirectToAction(
                        nameof(Detalle),
                        new { id = ejecucionProduccionId });
                }

                var tieneParoAbierto =
                    await TieneParoAbiertoAsync(
                        ejecucionProduccionId,
                        cn,
                        tx);

                if (tieneParoAbierto)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "No puedes iniciar o reiniciar serie mientras " +
                        "exista un paro abierto.";

                    return RedirectToAction(
                        nameof(Detalle),
                        new { id = ejecucionProduccionId });
                }

                var validacionCalidad =
                    await ValidarInicioSerieCalidadAsync(
                        ejecucionProduccionId,
                        cn,
                        tx);

                if (!validacionCalidad.Permitido)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        validacionCalidad.Mensaje;

                    return RedirectToAction(
                        nameof(Detalle),
                        new { id = ejecucionProduccionId });
                }

                var contextoReinicio =
                    await ObtenerContextoReinicioSerieAsync(
                        ejecucionProduccionId,
                        cn,
                        tx);

                var ahora =
                    NormalizarFechaMinuto(DateTime.Now);

                var esReinicio =
                    contextoReinicio != null;

                var programasRecorridos = 0;

                if (esReinicio)
                {
                    /*
                     * El reinicio de Producción es la fuente de verdad.
                     * No usamos únicamente FechaFinParo porque después del paro
                     * puede existir espera de primeras piezas / Calidad.
                     */
                    programasRecorridos =
                        await DesplazarYReacomodarCalendarioPorInterrupcionAsync(
                            ejecucion.ProgramaProduccionID,
                            ejecucionProduccionId,
                            contextoReinicio!.FechaInicioParo,
                            ahora,
                            usuarioId,
                            cn,
                            tx,
                            trabajarDomingo: false);
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

                await tx.CommitAsync();

                TempData["Success"] =
                    esReinicio
                        ? "Producción reiniciada correctamente. " +
                          "Se recalculó la OF afectada y se recorrieron " +
                          programasRecorridos +
                          " programa(s) adicionales por cola de máquina y/o " +
                          "ocupación de molde. Las capturas horarias continuarán " +
                          "desde la hora real de reinicio."
                        : "Producción en serie iniciada correctamente. " +
                          "Ya puedes registrar piezas por hora.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = ejecucionProduccionId });
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
                    "No fue posible iniciar o reiniciar producción en serie: " +
                    ex.Message;

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = ejecucionProduccionId });
            }
        }

        private async Task<int> DesplazarYReacomodarCalendarioPorInterrupcionAsync(
    int programaProduccionId,
    int ejecucionProduccionId,
    DateTime fechaInicioInterrupcion,
    DateTime fechaReinicioReal,
    int usuarioId,
    SqlConnection cn,
    SqlTransaction tx,
    bool trabajarDomingo)
        {
            if (programaProduccionId <= 0)
                return 0;

            fechaInicioInterrupcion =
                NormalizarFechaMinuto(fechaInicioInterrupcion);

            fechaReinicioReal =
                NormalizarFechaMinuto(fechaReinicioReal);

            if (fechaReinicioReal <= fechaInicioInterrupcion)
                return 0;

            /*
             * Mismo candado que utiliza el calendario editable.
             * Así evitamos que Planeación arrastre una barra mientras Producción
             * está propagando un paro.
             */
            await TomarCandadoCalendarioDesdeProduccionAsync(cn, tx);
            await ActivarReacomodoPlaneacionDesdeProduccionAsync(cn, tx);

            try
            {
                var programaRaiz =
                    await ObtenerProgramaReacomodoAsync(
                        programaProduccionId,
                        cn,
                        tx,
                        bloquear: true);

                if (programaRaiz == null)
                {
                    throw new InvalidOperationException(
                        "No se encontró el programa de Planeación relacionado con la ejecución.");
                }

                if (!programaRaiz.MaquinaID.HasValue)
                {
                    throw new InvalidOperationException(
                        "El programa que se desea reanudar no tiene máquina asignada.");
                }

                var finAnteriorRaiz =
                    programaRaiz.Fin;

                /*
                 * Solo contamos minutos que realmente pertenecen a ventanas
                 * operativas. Ejemplo: si cruza domingo no autorizado, esas horas
                 * no se consideran producción perdida.
                 */
                var minutosProductivosPerdidos =
                    CalcularMinutosOperativosEntreReacomodo(
                        fechaInicioInterrupcion,
                        fechaReinicioReal,
                        trabajarDomingo);

                if (minutosProductivosPerdidos <= 0)
                    return 0;

                var nuevoFinRaiz =
                    SumarHorasOperativasReacomodo(
                        finAnteriorRaiz,
                        minutosProductivosPerdidos / 60m,
                        trabajarDomingo);

                nuevoFinRaiz =
                    NormalizarFechaMinuto(nuevoFinRaiz);

                await ActualizarFinProgramaRaizPorParoAsync(
                    programaRaiz,
                    ejecucionProduccionId,
                    nuevoFinRaiz,
                    usuarioId,
                    cn,
                    tx);

                await InsertarHistorialReacomodoAutomaticoAsync(
                    programaRaiz,
                    programaRaiz.Inicio,
                    nuevoFinRaiz,
                    programaRaiz.Cambio,
                    programaRaiz.Arranque,
                    usuarioId,
                    programaProduccionId,
                    "RECORRIDO_POR_PARO",
                    "La OF se extendió por una interrupción de Producción. " +
                    "Inicio de interrupción: " +
                    fechaInicioInterrupcion.ToString("dd/MM/yyyy HH:mm") +
                    ". Reinicio real: " +
                    fechaReinicioReal.ToString("dd/MM/yyyy HH:mm") +
                    ". Minutos productivos recuperados: " +
                    minutosProductivosPerdidos + ".",
                    cn,
                    tx);

                /*
                 * Recargamos desde la BD DESPUÉS de extender la OF raíz.
                 * La OF raíz ya queda como un bloque fijo porque tiene ejecución.
                 *
                 * Se cargan todos los bloques cuyo fin toca o supera el punto
                 * donde comenzó el impacto. De esta manera puede propagarse a:
                 * - la misma máquina,
                 * - otra máquina por molde,
                 * - la cola de esa segunda máquina,
                 * - otro molde usado por una OF desplazada,
                 * y así sucesivamente.
                 */
                var programas =
                    await CargarProgramasReacomodoGlobalAsync(
                        finAnteriorRaiz,
                        programaProduccionId,
                        cn,
                        tx);

                var raizEnMemoria =
                    programas.FirstOrDefault(
                        x => x.ProgramaProduccionID == programaProduccionId);

                if (raizEnMemoria != null)
                {
                    raizEnMemoria.Inicio = programaRaiz.Inicio;
                    raizEnMemoria.Fin = nuevoFinRaiz;
                    raizEnMemoria.EsMovible = false;
                }

                /*
                 * Reservados = bloques que NO se pueden mover:
                 * - OF raíz que está trabajando,
                 * - ejecuciones ya iniciadas,
                 * - programas que ya entraron a Calidad,
                 * - estados no programados.
                 *
                 * Conforme reacomodamos un programa movible, se agrega también
                 * a reservados. Por eso las OF posteriores respetan su nueva hora.
                 */
                var reservados =
                    programas
                        .Where(x => !x.EsMovible)
                        .OrderBy(x => x.Inicio)
                        .ThenBy(x => x.ProgramaProduccionID)
                        .ToList();

                var movibles =
                    programas
                        .Where(x => x.EsMovible)
                        .OrderBy(x => x.InicioOriginal)
                        .ThenBy(x => x.SecuenciaMaquina)
                        .ThenBy(x => x.ProgramaProduccionID)
                        .ToList();

                var reacomodados = 0;

                foreach (var programa in movibles)
                {
                    var posicion =
                        CalcularPosicionGlobalSinCruces(
                            programa,
                            reservados,
                            trabajarDomingo);

                    var cambioDiferente =
                        programa.Inicio != posicion.Cambio;

                    var arranqueDiferente =
                        programa.ArranqueFecha != posicion.Arranque;

                    var finDiferente =
                        programa.Fin != posicion.Fin;

                    if (cambioDiferente ||
                        arranqueDiferente ||
                        finDiferente)
                    {
                        var inicioAnterior = programa.Inicio;
                        var finAnterior = programa.Fin;

                        await ActualizarProgramaReacomodoGlobalAsync(
                            programa,
                            posicion.Cambio,
                            posicion.Arranque,
                            posicion.Fin,
                            usuarioId,
                            cn,
                            tx);

                        await InsertarHistorialReacomodoAutomaticoAsync(
                            programa,
                            posicion.Cambio,
                            posicion.Fin,
                            posicion.Cambio.TimeOfDay,
                            posicion.Arranque.TimeOfDay,
                            usuarioId,
                            programaProduccionId,
                            posicion.MovidoPorMolde
                                ? "RECORRIDO_POR_MOLDE"
                                : "RECORRIDO_POR_COLA",
                            posicion.MovidoPorMolde
                                ? "Programa recorrido automáticamente porque el molde " +
                                  (programa.MoldeCodigo ?? programa.MoldeID?.ToString() ?? "-") +
                                  " quedó ocupado por una reprogramación previa. " +
                                  "Anterior: " + inicioAnterior.ToString("dd/MM/yyyy HH:mm") +
                                  " - " + finAnterior.ToString("dd/MM/yyyy HH:mm") +
                                  ". Nuevo: " + posicion.Cambio.ToString("dd/MM/yyyy HH:mm") +
                                  " - " + posicion.Fin.ToString("dd/MM/yyyy HH:mm") + "."
                                : "Programa recorrido automáticamente por la cola de su máquina. " +
                                  "Anterior: " + inicioAnterior.ToString("dd/MM/yyyy HH:mm") +
                                  " - " + finAnterior.ToString("dd/MM/yyyy HH:mm") +
                                  ". Nuevo: " + posicion.Cambio.ToString("dd/MM/yyyy HH:mm") +
                                  " - " + posicion.Fin.ToString("dd/MM/yyyy HH:mm") + ".",
                            cn,
                            tx);

                        programa.Inicio = posicion.Cambio;
                        programa.Fin = posicion.Fin;
                        programa.Cambio = posicion.Cambio.TimeOfDay;
                        programa.Arranque = posicion.Arranque.TimeOfDay;
                        programa.ArranqueFecha = posicion.Arranque;

                        reacomodados++;
                    }

                    /*
                     * Aunque no se haya movido, desde este momento forma parte de
                     * la secuencia ya resuelta y debe bloquear a los siguientes.
                     */
                    reservados.Add(programa);
                }

                await ReordenarSecuenciasReacomodoGlobalAsync(
                    programas
                        .Where(x => x.MaquinaID.HasValue)
                        .Select(x => x.MaquinaID!.Value)
                        .Distinct()
                        .ToList(),
                    cn,
                    tx);

                return reacomodados;
            }
            finally
            {
                await DesactivarReacomodoPlaneacionDesdeProduccionAsync(cn, tx);
            }
        }


        private async Task<ProgramaReacomodoGlobal?> ObtenerProgramaReacomodoAsync(
    int programaProduccionId,
    SqlConnection cn,
    SqlTransaction tx,
    bool bloquear)
        {
            var hint = bloquear
                ? " WITH (UPDLOCK, HOLDLOCK)"
                : string.Empty;

            var sql = $@"
SELECT TOP (1)
    pp.ProgramaProduccionID,
    pp.MaquinaID,
    pp.ParteID,
    pp.MoldeID,
    pp.MoldeCodigo,
    pp.ReleaseDetalleID,
    pp.SolicitudProduccionID,
    pp.SolicitudProduccionDetalleID,
    pp.FechaInicioProgramada,
    ISNULL
    (
        pp.FechaFinProgramada,
        DATEADD
        (
            MINUTE,
            CAST(CEILING(ISNULL(pp.HorasProgramadas, 1) * 60) AS INT),
            pp.FechaInicioProgramada
        )
    ) AS FechaFinProgramada,
    ISNULL(pp.HorasProgramadas, 0) AS HorasProgramadas,
    pp.Cambio,
    pp.Arranque,
    ISNULL(pp.SecuenciaMaquina, 999999) AS SecuenciaMaquina,
    ISNULL(pp.EstatusID, 1) AS EstatusID
FROM dbo.Planeacion_ProgramaProduccion pp{hint}
WHERE pp.ProgramaProduccionID = @ProgramaProduccionID
  AND pp.Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add(
                "@ProgramaProduccionID",
                SqlDbType.Int).Value = programaProduccionId;

            await using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
                return null;

            var inicio = Convert.ToDateTime(rd["FechaInicioProgramada"]);
            var fin = Convert.ToDateTime(rd["FechaFinProgramada"]);

            var cambio =
                rd["Cambio"] == DBNull.Value
                    ? (TimeSpan?)null
                    : (TimeSpan)rd["Cambio"];

            var arranque =
                rd["Arranque"] == DBNull.Value
                    ? (TimeSpan?)null
                    : (TimeSpan)rd["Arranque"];

            return new ProgramaReacomodoGlobal
            {
                ProgramaProduccionID =
                    Convert.ToInt32(rd["ProgramaProduccionID"]),

                MaquinaID =
                    rd["MaquinaID"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(rd["MaquinaID"]),

                ParteID =
                    rd["ParteID"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(rd["ParteID"]),

                MoldeID =
                    rd["MoldeID"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(rd["MoldeID"]),

                MoldeCodigo =
                    rd["MoldeCodigo"] == DBNull.Value
                        ? null
                        : rd["MoldeCodigo"].ToString(),

                ReleaseDetalleID =
                    rd["ReleaseDetalleID"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(rd["ReleaseDetalleID"]),

                SolicitudProduccionID =
                    rd["SolicitudProduccionID"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(rd["SolicitudProduccionID"]),

                SolicitudProduccionDetalleID =
                    rd["SolicitudProduccionDetalleID"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(rd["SolicitudProduccionDetalleID"]),

                InicioOriginal = inicio,
                FinOriginal = fin,
                Inicio = inicio,
                Fin = fin,

                HorasProgramadas =
                    Convert.ToDecimal(rd["HorasProgramadas"]),

                Cambio = cambio,
                Arranque = arranque,

                ArranqueFecha =
                    ConstruirFechaHoraDesdeTimeSpan(inicio, arranque),

                SecuenciaMaquina =
                    Convert.ToInt32(rd["SecuenciaMaquina"]),

                EstatusID =
                    Convert.ToInt32(rd["EstatusID"])
            };
        }


        private async Task<List<ProgramaReacomodoGlobal>>
            CargarProgramasReacomodoGlobalAsync(
                DateTime desdeImpacto,
                int programaRaizId,
                SqlConnection cn,
                SqlTransaction tx)
        {
            var lista = new List<ProgramaReacomodoGlobal>();

            const string sql = @"
SELECT
    pp.ProgramaProduccionID,
    pp.MaquinaID,
    pp.ParteID,
    pp.MoldeID,
    pp.MoldeCodigo,
    pp.ReleaseDetalleID,
    pp.SolicitudProduccionID,
    pp.SolicitudProduccionDetalleID,
    pp.FechaInicioProgramada,
    ISNULL
    (
        pp.FechaFinProgramada,
        DATEADD
        (
            MINUTE,
            CAST(CEILING(ISNULL(pp.HorasProgramadas, 1) * 60) AS INT),
            pp.FechaInicioProgramada
        )
    ) AS FechaFinProgramada,
    ISNULL(pp.HorasProgramadas, 0) AS HorasProgramadas,
    pp.Cambio,
    pp.Arranque,
    ISNULL(pp.SecuenciaMaquina, 999999) AS SecuenciaMaquina,
    ISNULL(pp.EstatusID, 1) AS EstatusID,

    CASE
        WHEN pp.ProgramaProduccionID = @ProgramaRaizID
            THEN CAST(0 AS BIT)

        WHEN ISNULL(pp.EstatusID, 1) <> 1
            THEN CAST(0 AS BIT)

        WHEN EXISTS
        (
            SELECT 1
            FROM dbo.Produccion_Ejecucion e
            WHERE e.ProgramaProduccionID = pp.ProgramaProduccionID
              AND e.Activo = 1
        )
            THEN CAST(0 AS BIT)

        WHEN EXISTS
        (
            SELECT 1
            FROM dbo.Calidad_Inspecciones ci
            WHERE ci.ProgramaProduccionID = pp.ProgramaProduccionID
        )
            THEN CAST(0 AS BIT)

        ELSE CAST(1 AS BIT)
    END AS EsMovible

FROM dbo.Planeacion_ProgramaProduccion pp WITH (UPDLOCK, HOLDLOCK)
WHERE pp.Activo = 1
  AND pp.MaquinaID IS NOT NULL
  AND pp.FechaInicioProgramada IS NOT NULL
  AND ISNULL(pp.EstatusID, 1) NOT IN (5, 6, 9, 99)
  AND ISNULL
      (
          pp.FechaFinProgramada,
          DATEADD
          (
              MINUTE,
              CAST(CEILING(ISNULL(pp.HorasProgramadas, 1) * 60) AS INT),
              pp.FechaInicioProgramada
          )
      ) >= @DesdeImpacto
ORDER BY
    pp.FechaInicioProgramada,
    ISNULL(pp.SecuenciaMaquina, 999999),
    pp.ProgramaProduccionID;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add(
                "@ProgramaRaizID",
                SqlDbType.Int).Value = programaRaizId;

            cmd.Parameters.Add(
                "@DesdeImpacto",
                SqlDbType.DateTime).Value = desdeImpacto;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                var inicio =
                    Convert.ToDateTime(rd["FechaInicioProgramada"]);

                var fin =
                    Convert.ToDateTime(rd["FechaFinProgramada"]);

                var cambio =
                    rd["Cambio"] == DBNull.Value
                        ? (TimeSpan?)null
                        : (TimeSpan)rd["Cambio"];

                var arranque =
                    rd["Arranque"] == DBNull.Value
                        ? (TimeSpan?)null
                        : (TimeSpan)rd["Arranque"];

                lista.Add(new ProgramaReacomodoGlobal
                {
                    ProgramaProduccionID =
                        Convert.ToInt32(rd["ProgramaProduccionID"]),

                    MaquinaID =
                        rd["MaquinaID"] == DBNull.Value
                            ? null
                            : Convert.ToInt32(rd["MaquinaID"]),

                    ParteID =
                        rd["ParteID"] == DBNull.Value
                            ? null
                            : Convert.ToInt32(rd["ParteID"]),

                    MoldeID =
                        rd["MoldeID"] == DBNull.Value
                            ? null
                            : Convert.ToInt32(rd["MoldeID"]),

                    MoldeCodigo =
                        rd["MoldeCodigo"] == DBNull.Value
                            ? null
                            : rd["MoldeCodigo"].ToString(),

                    ReleaseDetalleID =
                        rd["ReleaseDetalleID"] == DBNull.Value
                            ? null
                            : Convert.ToInt32(rd["ReleaseDetalleID"]),

                    SolicitudProduccionID =
                        rd["SolicitudProduccionID"] == DBNull.Value
                            ? null
                            : Convert.ToInt32(rd["SolicitudProduccionID"]),

                    SolicitudProduccionDetalleID =
                        rd["SolicitudProduccionDetalleID"] == DBNull.Value
                            ? null
                            : Convert.ToInt32(rd["SolicitudProduccionDetalleID"]),

                    InicioOriginal = inicio,
                    FinOriginal = fin,
                    Inicio = inicio,
                    Fin = fin,

                    HorasProgramadas =
                        Convert.ToDecimal(rd["HorasProgramadas"]),

                    Cambio = cambio,
                    Arranque = arranque,

                    ArranqueFecha =
                        ConstruirFechaHoraDesdeTimeSpan(inicio, arranque),

                    SecuenciaMaquina =
                        Convert.ToInt32(rd["SecuenciaMaquina"]),

                    EstatusID =
                        Convert.ToInt32(rd["EstatusID"]),

                    EsMovible =
                        Convert.ToBoolean(rd["EsMovible"])
                });
            }

            return lista;
        }


        private static PosicionReacomodoGlobal CalcularPosicionGlobalSinCruces(
    ProgramaReacomodoGlobal programa,
    List<ProgramaReacomodoGlobal> reservados,
    bool trabajarDomingo)
        {
            var cursor =
                programa.InicioOriginal;

            var movidoPorMolde = false;

            for (var intento = 0; intento < 2000; intento++)
            {
                cursor =
                    SiguienteAperturaOperativaReacomodo(
                        cursor,
                        trabajarDomingo);

                cursor =
                    RedondearSiguienteBloqueReacomodo(
                        cursor,
                        15);

                /*
                 * Programa inmediatamente anterior de la MISMA máquina.
                 * Se usa para decidir si requiere una hora de cambio.
                 */
                var anteriorMaquina =
                    reservados
                        .Where(x =>
                            x.ProgramaProduccionID != programa.ProgramaProduccionID &&
                            x.MaquinaID.HasValue &&
                            programa.MaquinaID.HasValue &&
                            x.MaquinaID.Value == programa.MaquinaID.Value &&
                            x.Fin <= cursor)
                        .OrderByDescending(x => x.Fin)
                        .ThenByDescending(x => x.ProgramaProduccionID)
                        .FirstOrDefault();

                var mismaParte =
                    programa.ParteID.HasValue &&
                    anteriorMaquina?.ParteID.HasValue == true &&
                    programa.ParteID.Value == anteriorMaquina.ParteID.Value;

                var mismoMolde =
                    programa.MoldeID.HasValue &&
                    anteriorMaquina?.MoldeID.HasValue == true &&
                    programa.MoldeID.Value == anteriorMaquina.MoldeID.Value;

                var horasCambio =
                    anteriorMaquina != null &&
                    !mismaParte &&
                    !mismoMolde
                        ? 1m
                        : 0m;

                var cambio = cursor;

                var arranque =
                    SumarHorasOperativasReacomodo(
                        cambio,
                        horasCambio,
                        trabajarDomingo);

                var horasProduccion =
                    programa.HorasProgramadas > 0
                        ? programa.HorasProgramadas
                        : 1m;

                var fin =
                    SumarHorasOperativasReacomodo(
                        arranque,
                        horasProduccion,
                        trabajarDomingo);

                /*
                 * Conflictos ya reservados:
                 * 1) misma máquina;
                 * 2) mismo molde, incluso aunque esté en OTRA máquina.
                 */
                var conflictos =
                    reservados
                        .Where(x =>
                            x.ProgramaProduccionID != programa.ProgramaProduccionID &&
                            IntervalosSeCruzan(
                                cambio,
                                fin,
                                x.Inicio,
                                x.Fin) &&
                            (
                                (
                                    x.MaquinaID.HasValue &&
                                    programa.MaquinaID.HasValue &&
                                    x.MaquinaID.Value == programa.MaquinaID.Value
                                )
                                ||
                                (
                                    x.MoldeID.HasValue &&
                                    programa.MoldeID.HasValue &&
                                    x.MoldeID.Value == programa.MoldeID.Value
                                )
                            ))
                        .ToList();

                if (!conflictos.Any())
                {
                    return new PosicionReacomodoGlobal
                    {
                        Cambio = cambio,
                        Arranque = arranque,
                        Fin = fin,
                        HorasCambio = horasCambio,
                        MovidoPorMolde = movidoPorMolde
                    };
                }

                if (conflictos.Any(x =>
                    x.MoldeID.HasValue &&
                    programa.MoldeID.HasValue &&
                    x.MoldeID.Value == programa.MoldeID.Value &&
                    (!x.MaquinaID.HasValue ||
                     !programa.MaquinaID.HasValue ||
                     x.MaquinaID.Value != programa.MaquinaID.Value)))
                {
                    movidoPorMolde = true;
                }

                /*
                 * Saltamos después del conflicto que termina más tarde.
                 * Luego se vuelve a calcular cambio, arranque, fin y conflictos.
                 */
                cursor =
                    conflictos.Max(x => x.Fin);
            }

            throw new InvalidOperationException(
                "No fue posible reacomodar automáticamente la programación sin cruces de máquina o molde.");
        }

        private async Task ActualizarFinProgramaRaizPorParoAsync(
    ProgramaReacomodoGlobal programa,
    int ejecucionProduccionId,
    DateTime nuevoFin,
    int usuarioId,
    SqlConnection cn,
    SqlTransaction tx)
        {
            const string sql = @"
UPDATE dbo.Planeacion_ProgramaProduccion
SET
    FechaFinProgramada = @FechaFin,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE ProgramaProduccionID = @ProgramaProduccionID
  AND Activo = 1;

IF @@ROWCOUNT <> 1
BEGIN
    THROW 51021,
        'No fue posible extender el programa que sufrió el paro.',
        1;
END;

UPDATE dbo.Calidad_Inspecciones
SET
    FechaFinProgramada = @FechaFin,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE EjecucionProduccionID = @EjecucionProduccionID
  AND ISNULL(Estado, N'') <> N'CERRADA';

UPDATE s
SET
    s.FechaFinPlaneada = @FechaFin
FROM dbo.SolicitudesProduccion s
INNER JOIN dbo.Planeacion_ProgramaProduccion pp
    ON pp.SolicitudProduccionID = s.SolicitudProduccionID
WHERE pp.ProgramaProduccionID = @ProgramaProduccionID;

UPDATE am
SET
    am.FechaProgramadaTentativa = CAST(pp.FechaInicioProgramada AS date),
    am.HoraInicioTentativa = CAST(pp.FechaInicioProgramada AS time),
    am.HoraFinTentativa = CAST(@FechaFin AS time),
    am.HorasEstimadas = pp.HorasProgramadas
FROM dbo.SolicitudesProduccionAsignacionMaquina am
INNER JOIN dbo.Planeacion_ProgramaProduccion pp
    ON pp.SolicitudProduccionDetalleID = am.SolicitudProduccionDetalleID
WHERE pp.ProgramaProduccionID = @ProgramaProduccionID
  AND am.Activo = 1;

UPDATE rd
SET
    rd.FechaFinEstimada = @FechaFin,
    rd.DaTiempo =
        CASE
            WHEN rd.FechaRequerida IS NULL THEN NULL
            WHEN CONVERT(date, @FechaFin) <= CONVERT(date, rd.FechaRequerida)
                THEN 1
            ELSE 0
        END,
    rd.MensajeCapacidad =
        CASE
            WHEN rd.FechaRequerida IS NULL
                THEN N'Programa extendido por paro. Sin fecha requerida del cliente.'
            WHEN CONVERT(date, @FechaFin) <= CONVERT(date, rd.FechaRequerida)
                THEN N'Programa extendido por paro dentro de la fecha requerida.'
            ELSE N'Programa extendido por paro posterior a la fecha requerida.'
        END,
    rd.FechaModificacion = GETDATE()
FROM dbo.Planeacion_ReleaseDetalle rd
INNER JOIN dbo.Planeacion_ProgramaProduccion pp
    ON pp.ReleaseDetalleID = rd.ReleaseDetalleID
WHERE pp.ProgramaProduccionID = @ProgramaProduccionID
  AND rd.Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add(
                "@ProgramaProduccionID",
                SqlDbType.Int).Value = programa.ProgramaProduccionID;

            cmd.Parameters.Add(
                "@EjecucionProduccionID",
                SqlDbType.Int).Value = ejecucionProduccionId;

            cmd.Parameters.Add(
                "@FechaFin",
                SqlDbType.DateTime).Value = nuevoFin;

            cmd.Parameters.Add(
                "@UsuarioID",
                SqlDbType.Int).Value = usuarioId;

            await cmd.ExecuteNonQueryAsync();
        }


        private async Task ActualizarProgramaReacomodoGlobalAsync(
            ProgramaReacomodoGlobal programa,
            DateTime cambio,
            DateTime arranque,
            DateTime fin,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
UPDATE dbo.Planeacion_ProgramaProduccion
SET
    FechaInicioProgramada = @FechaInicio,
    FechaFinProgramada = @FechaFin,
    Cambio = @Cambio,
    Arranque = @Arranque,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE ProgramaProduccionID = @ProgramaProduccionID
  AND Activo = 1
  AND ISNULL(EstatusID, 1) = 1
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.Produccion_Ejecucion e
      WHERE e.ProgramaProduccionID = @ProgramaProduccionID
        AND e.Activo = 1
  )
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.Calidad_Inspecciones ci
      WHERE ci.ProgramaProduccionID = @ProgramaProduccionID
  );

IF @@ROWCOUNT <> 1
BEGIN
    THROW 51022,
        'Uno de los programas que debía recorrerse ya inició Producción o Calidad.',
        1;
END;

UPDATE d
SET
    d.HorasPlaneadas = pp.HorasProgramadas,
    d.Cambio = @Cambio,
    d.Arranque = @Arranque
FROM dbo.SolicitudesProduccionDetalle d
INNER JOIN dbo.Planeacion_ProgramaProduccion pp
    ON pp.SolicitudProduccionDetalleID = d.SolicitudProduccionDetalleID
WHERE pp.ProgramaProduccionID = @ProgramaProduccionID;

UPDATE am
SET
    am.FechaProgramadaTentativa = CAST(@FechaInicio AS date),
    am.HoraInicioTentativa = CAST(@FechaInicio AS time),
    am.HoraFinTentativa = CAST(@FechaFin AS time),
    am.HorasEstimadas = pp.HorasProgramadas
FROM dbo.SolicitudesProduccionAsignacionMaquina am
INNER JOIN dbo.Planeacion_ProgramaProduccion pp
    ON pp.SolicitudProduccionDetalleID = am.SolicitudProduccionDetalleID
WHERE pp.ProgramaProduccionID = @ProgramaProduccionID
  AND am.Activo = 1;

UPDATE s
SET
    s.FechaInicioPlaneada = @FechaInicio,
    s.FechaFinPlaneada = @FechaFin
FROM dbo.SolicitudesProduccion s
INNER JOIN dbo.Planeacion_ProgramaProduccion pp
    ON pp.SolicitudProduccionID = s.SolicitudProduccionID
WHERE pp.ProgramaProduccionID = @ProgramaProduccionID;

UPDATE rd
SET
    rd.FechaInicioSugerida = @FechaInicio,
    rd.FechaFinEstimada = @FechaFin,
    rd.DaTiempo =
        CASE
            WHEN rd.FechaRequerida IS NULL THEN NULL
            WHEN CONVERT(date, @FechaFin) <= CONVERT(date, rd.FechaRequerida)
                THEN 1
            ELSE 0
        END,
    rd.MensajeCapacidad =
        CASE
            WHEN rd.FechaRequerida IS NULL
                THEN N'Programa recorrido automáticamente. Sin fecha requerida del cliente.'
            WHEN CONVERT(date, @FechaFin) <= CONVERT(date, rd.FechaRequerida)
                THEN N'Programa recorrido automáticamente dentro de la fecha requerida.'
            ELSE N'Programa recorrido automáticamente posterior a la fecha requerida.'
        END,
    rd.FechaModificacion = GETDATE()
FROM dbo.Planeacion_ReleaseDetalle rd
INNER JOIN dbo.Planeacion_ProgramaProduccion pp
    ON pp.ReleaseDetalleID = rd.ReleaseDetalleID
WHERE pp.ProgramaProduccionID = @ProgramaProduccionID
  AND rd.Activo = 1;";

            await using (var cmd = new SqlCommand(sql, cn, tx))
            {
                cmd.Parameters.Add(
                    "@FechaInicio",
                    SqlDbType.DateTime).Value = cambio;

                cmd.Parameters.Add(
                    "@FechaFin",
                    SqlDbType.DateTime).Value = fin;

                cmd.Parameters.Add(
                    "@Cambio",
                    SqlDbType.Time).Value = cambio.TimeOfDay;

                cmd.Parameters.Add(
                    "@Arranque",
                    SqlDbType.Time).Value = arranque.TimeOfDay;

                cmd.Parameters.Add(
                    "@UsuarioID",
                    SqlDbType.Int).Value = usuarioId;

                cmd.Parameters.Add(
                    "@ProgramaProduccionID",
                    SqlDbType.Int).Value = programa.ProgramaProduccionID;

                await cmd.ExecuteNonQueryAsync();
            }

            await RecalcularOperadoresProgramaPorReacomodoAsync(
                programa.ProgramaProduccionID,
                programa.MaquinaID!.Value,
                cambio,
                usuarioId,
                cn,
                tx);
        }


        private async Task InsertarHistorialReacomodoAutomaticoAsync(
            ProgramaReacomodoGlobal programa,
            DateTime inicioNuevo,
            DateTime finNuevo,
            TimeSpan? cambioNuevo,
            TimeSpan? arranqueNuevo,
            int usuarioId,
            int programaOrigenMovimientoId,
            string tipoMovimiento,
            string motivo,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
IF OBJECT_ID
(
    N'dbo.Planeacion_ProgramaReprogramacionHistorial',
    N'U'
) IS NOT NULL
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
        TipoMovimiento,
        EsMovimientoAutomatico,
        ProgramaOrigenMovimientoID,
        UsuarioID,
        FechaCambio,
        Motivo
    )
    VALUES
    (
        @ProgramaProduccionID,
        @MaquinaID,
        @MaquinaID,
        @InicioAnterior,
        @InicioNuevo,
        @FinAnterior,
        @FinNuevo,
        @Horas,
        @Horas,
        @CambioAnterior,
        @CambioNuevo,
        @ArranqueAnterior,
        @ArranqueNuevo,
        @ReleaseDetalleID,
        @SolicitudProduccionID,
        @SolicitudProduccionDetalleID,
        @TipoMovimiento,
        1,
        @ProgramaOrigenMovimientoID,
        @UsuarioID,
        GETDATE(),
        @Motivo
    );
END;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add(
                "@ProgramaProduccionID",
                SqlDbType.Int).Value = programa.ProgramaProduccionID;

            cmd.Parameters.Add(
                "@MaquinaID",
                SqlDbType.Int).Value =
                (object?)programa.MaquinaID ?? DBNull.Value;

            cmd.Parameters.Add(
                "@InicioAnterior",
                SqlDbType.DateTime).Value = programa.InicioOriginal;

            cmd.Parameters.Add(
                "@InicioNuevo",
                SqlDbType.DateTime).Value = inicioNuevo;

            cmd.Parameters.Add(
                "@FinAnterior",
                SqlDbType.DateTime).Value = programa.FinOriginal;

            cmd.Parameters.Add(
                "@FinNuevo",
                SqlDbType.DateTime).Value = finNuevo;

            var horas =
                cmd.Parameters.Add(
                    "@Horas",
                    SqlDbType.Decimal);

            horas.Precision = 18;
            horas.Scale = 4;
            horas.Value = programa.HorasProgramadas;

            cmd.Parameters.Add(
                "@CambioAnterior",
                SqlDbType.Time).Value =
                (object?)programa.Cambio ?? DBNull.Value;

            cmd.Parameters.Add(
                "@CambioNuevo",
                SqlDbType.Time).Value =
                (object?)cambioNuevo ?? DBNull.Value;

            cmd.Parameters.Add(
                "@ArranqueAnterior",
                SqlDbType.Time).Value =
                (object?)programa.Arranque ?? DBNull.Value;

            cmd.Parameters.Add(
                "@ArranqueNuevo",
                SqlDbType.Time).Value =
                (object?)arranqueNuevo ?? DBNull.Value;

            cmd.Parameters.Add(
                "@ReleaseDetalleID",
                SqlDbType.Int).Value =
                (object?)programa.ReleaseDetalleID ?? DBNull.Value;

            cmd.Parameters.Add(
                "@SolicitudProduccionID",
                SqlDbType.Int).Value =
                (object?)programa.SolicitudProduccionID ?? DBNull.Value;

            cmd.Parameters.Add(
                "@SolicitudProduccionDetalleID",
                SqlDbType.Int).Value =
                (object?)programa.SolicitudProduccionDetalleID ?? DBNull.Value;

            cmd.Parameters.Add(
                "@TipoMovimiento",
                SqlDbType.NVarChar,
                60).Value = tipoMovimiento;

            cmd.Parameters.Add(
                "@ProgramaOrigenMovimientoID",
                SqlDbType.Int).Value = programaOrigenMovimientoId;

            cmd.Parameters.Add(
                "@UsuarioID",
                SqlDbType.Int).Value = usuarioId;

            cmd.Parameters.Add(
                "@Motivo",
                SqlDbType.NVarChar,
                500).Value =
                motivo.Length > 500
                    ? motivo[..500]
                    : motivo;

            await cmd.ExecuteNonQueryAsync();
        }


        private async Task RecalcularOperadoresProgramaPorReacomodoAsync(
    int programaProduccionId,
    int maquinaId,
    DateTime fechaHora,
    int usuarioId,
    SqlConnection cn,
    SqlTransaction tx)
        {
            var operadores =
                new List<(int PersonaID, int EscalaAsignacionID)>();

            const string sqlOperadores = @"
SELECT TOP (2)
    a.AsignacionID AS EscalaAsignacionID,
    a.PersonalID AS PersonaID
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
  AND ISNULL(p.EsColaboradorActivo, 1) = 1
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

            await using (var cmd = new SqlCommand(sqlOperadores, cn, tx))
            {
                cmd.Parameters.Add(
                    "@MaquinaID",
                    SqlDbType.Int).Value = maquinaId;

                cmd.Parameters.Add(
                    "@FechaHora",
                    SqlDbType.DateTime).Value = fechaHora;

                await using var rd = await cmd.ExecuteReaderAsync();

                while (await rd.ReadAsync())
                {
                    operadores.Add((
                        Convert.ToInt32(rd["PersonaID"]),
                        Convert.ToInt32(rd["EscalaAsignacionID"])));
                }
            }

            const string sqlDesactivar = @"
UPDATE dbo.Planeacion_ProgramaOperadores
SET
    Activo = 0,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE ProgramaProduccionID = @ProgramaProduccionID
  AND Activo = 1;";

            await using (var cmd = new SqlCommand(sqlDesactivar, cn, tx))
            {
                cmd.Parameters.Add(
                    "@UsuarioID",
                    SqlDbType.Int).Value = usuarioId;

                cmd.Parameters.Add(
                    "@ProgramaProduccionID",
                    SqlDbType.Int).Value = programaProduccionId;

                await cmd.ExecuteNonQueryAsync();
            }

            const string sqlInsertar = @"
INSERT INTO dbo.Planeacion_ProgramaOperadores
(
    ProgramaProduccionID,
    PersonaID,
    RolOperador,
    Activo,
    UsuarioCreacionID,
    FechaCreacion
)
VALUES
(
    @ProgramaProduccionID,
    @PersonaID,
    @RolOperador,
    1,
    @UsuarioID,
    GETDATE()
);";

            for (var i = 0; i < operadores.Count; i++)
            {
                await using var cmd =
                    new SqlCommand(sqlInsertar, cn, tx);

                cmd.Parameters.Add(
                    "@ProgramaProduccionID",
                    SqlDbType.Int).Value = programaProduccionId;

                cmd.Parameters.Add(
                    "@PersonaID",
                    SqlDbType.Int).Value = operadores[i].PersonaID;

                cmd.Parameters.Add(
                    "@RolOperador",
                    SqlDbType.NVarChar,
                    30).Value =
                    i == 0
                        ? "PRINCIPAL"
                        : "AUXILIAR";

                cmd.Parameters.Add(
                    "@UsuarioID",
                    SqlDbType.Int).Value = usuarioId;

                await cmd.ExecuteNonQueryAsync();
            }
        }


        private static async Task ReordenarSecuenciasReacomodoGlobalAsync(
            List<int> maquinas,
            SqlConnection cn,
            SqlTransaction tx)
        {
            if (maquinas == null || maquinas.Count == 0)
                return;

            foreach (var maquinaId in maquinas.Distinct())
            {
                const string sql = @"
;WITH Orden AS
(
    SELECT
        ProgramaProduccionID,
        ROW_NUMBER() OVER
        (
            ORDER BY
                FechaInicioProgramada,
                ProgramaProduccionID
        ) AS NuevaSecuencia
    FROM dbo.Planeacion_ProgramaProduccion
    WHERE Activo = 1
      AND MaquinaID = @MaquinaID
      AND ISNULL(EstatusID, 1) NOT IN (5, 6, 9, 99)
)
UPDATE pp
SET
    SecuenciaMaquina = o.NuevaSecuencia
FROM dbo.Planeacion_ProgramaProduccion pp
INNER JOIN Orden o
    ON o.ProgramaProduccionID = pp.ProgramaProduccionID;";

                await using var cmd = new SqlCommand(sql, cn, tx);

                cmd.Parameters.Add(
                    "@MaquinaID",
                    SqlDbType.Int).Value = maquinaId;

                await cmd.ExecuteNonQueryAsync();
            }
        }


        private static async Task TomarCandadoCalendarioDesdeProduccionAsync(
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
DECLARE @Resultado INT;

EXEC @Resultado = sys.sp_getapplock
    @Resource = N'ERP_PLANEACION_CALENDARIO_MAQUINAS',
    @LockMode = N'Exclusive',
    @LockOwner = N'Transaction',
    @LockTimeout = 15000;

IF @Resultado < 0
BEGIN
    THROW 51010,
        'El calendario está siendo actualizado. Intenta nuevamente en unos segundos.',
        1;
END;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            await cmd.ExecuteNonQueryAsync();
        }


        private static async Task ActivarReacomodoPlaneacionDesdeProduccionAsync(
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
EXEC sys.sp_set_session_context
    @key = N'PlaneacionPermitirReacomodo',
    @value = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task DesactivarReacomodoPlaneacionDesdeProduccionAsync(
    SqlConnection cn,
    SqlTransaction tx)
        {
            const string sql = @"
EXEC sys.sp_set_session_context
    @key = N'PlaneacionPermitirReacomodo',
    @value = NULL;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            await cmd.ExecuteNonQueryAsync();
        }


        private static bool IntervalosSeCruzan(
            DateTime inicioA,
            DateTime finA,
            DateTime inicioB,
            DateTime finB)
        {
            return inicioA < finB &&
                   finA > inicioB;
        }


        private static DateTime ConstruirFechaHoraDesdeTimeSpan(
            DateTime fechaBase,
            TimeSpan? hora)
        {
            if (!hora.HasValue)
                return fechaBase;

            var result =
                fechaBase.Date.Add(hora.Value);

            /*
             * Si el TimeSpan queda antes del inicio y se trata de un arranque que
             * cruzó medianoche, lo llevamos al día siguiente.
             */
            if (result < fechaBase)
                result = result.AddDays(1);

            return result;
        }


        private static int CalcularMinutosOperativosEntreReacomodo(
            DateTime inicio,
            DateTime fin,
            bool trabajarDomingo)
        {
            if (fin <= inicio)
                return 0;

            var cursor = inicio;
            var totalMinutos = 0d;
            var guard = 0;

            while (cursor < fin)
            {
                guard++;

                if (guard > 5000)
                {
                    throw new InvalidOperationException(
                        "No fue posible calcular los minutos operativos de la interrupción.");
                }

                cursor =
                    SiguienteAperturaOperativaReacomodo(
                        cursor,
                        trabajarDomingo);

                if (cursor >= fin)
                    break;

                var cierre =
                    FinVentanaOperativaReacomodo(
                        cursor,
                        trabajarDomingo);

                var hasta =
                    cierre < fin
                        ? cierre
                        : fin;

                if (hasta > cursor)
                {
                    totalMinutos +=
                        (hasta - cursor).TotalMinutes;
                }

                if (hasta >= fin)
                    break;

                cursor =
                    SiguienteAperturaOperativaReacomodo(
                        cierre.AddMinutes(1),
                        trabajarDomingo);
            }

            return (int)Math.Round(totalMinutos);
        }


        private static DateTime SiguienteAperturaOperativaReacomodo(
            DateTime fecha,
            bool trabajarDomingo)
        {
            var value = fecha;

            while (true)
            {
                if (value.DayOfWeek == DayOfWeek.Sunday)
                {
                    if (trabajarDomingo)
                        return value;

                    value =
                        value.Date
                             .AddDays(1)
                             .AddHours(7);

                    continue;
                }

                if (value.DayOfWeek == DayOfWeek.Monday &&
                    value.TimeOfDay < TimeSpan.FromHours(7))
                {
                    return value.Date.AddHours(7);
                }

                if (value.DayOfWeek == DayOfWeek.Saturday &&
                    value.TimeOfDay >= new TimeSpan(22, 30, 0))
                {
                    value =
                        value.Date
                             .AddDays(2)
                             .AddHours(7);

                    continue;
                }

                return value;
            }
        }

        private static DateTime FinVentanaOperativaReacomodo(
            DateTime fecha,
            bool trabajarDomingo)
        {
            if (fecha.DayOfWeek == DayOfWeek.Saturday)
                return fecha.Date.AddHours(22).AddMinutes(30);

            if (fecha.DayOfWeek == DayOfWeek.Sunday)
            {
                return trabajarDomingo
                    ? fecha.Date.AddDays(1)
                    : fecha.Date;
            }

            return fecha.Date.AddDays(1);
        }


        private static DateTime SumarHorasOperativasReacomodo(
            DateTime inicio,
            decimal horas,
            bool trabajarDomingo)
        {
            if (horas <= 0)
            {
                return SiguienteAperturaOperativaReacomodo(
                    inicio,
                    trabajarDomingo);
            }

            var cursor =
                SiguienteAperturaOperativaReacomodo(
                    inicio,
                    trabajarDomingo);

            var restante = horas;
            var guard = 0;

            while (restante > 0.0001m)
            {
                guard++;

                if (guard > 5000)
                {
                    throw new InvalidOperationException(
                        "No fue posible calcular el horario operativo del reacomodo.");
                }

                cursor =
                    SiguienteAperturaOperativaReacomodo(
                        cursor,
                        trabajarDomingo);

                var finVentana =
                    FinVentanaOperativaReacomodo(
                        cursor,
                        trabajarDomingo);

                var disponible =
                    (decimal)(finVentana - cursor).TotalHours;

                if (disponible <= 0)
                {
                    cursor =
                        SiguienteAperturaOperativaReacomodo(
                            finVentana.AddMinutes(1),
                            trabajarDomingo);

                    continue;
                }

                if (restante <= disponible)
                    return cursor.AddHours((double)restante);

                restante -= disponible;

                cursor =
                    SiguienteAperturaOperativaReacomodo(
                        finVentana.AddMinutes(1),
                        trabajarDomingo);
            }

            return cursor;
        }


        private static DateTime RedondearSiguienteBloqueReacomodo(
            DateTime fecha,
            int minutos)
        {
            if (minutos <= 0)
                minutos = 15;

            var bloqueTicks =
                TimeSpan.FromMinutes(minutos).Ticks;

            var resto =
                fecha.Ticks % bloqueTicks;

            var ticks =
                resto == 0
                    ? fecha.Ticks
                    : fecha.Ticks + (bloqueTicks - resto);

            var redondeada =
                new DateTime(ticks, DateTimeKind.Unspecified);

            return new DateTime(
                redondeada.Year,
                redondeada.Month,
                redondeada.Day,
                redondeada.Hour,
                redondeada.Minute,
                0,
                DateTimeKind.Unspecified);
        }


        private sealed class ProgramaReacomodoGlobal
        {
            public int ProgramaProduccionID { get; set; }
            public int? MaquinaID { get; set; }
            public int? ParteID { get; set; }
            public int? MoldeID { get; set; }
            public string? MoldeCodigo { get; set; }

            public int? ReleaseDetalleID { get; set; }
            public int? SolicitudProduccionID { get; set; }
            public int? SolicitudProduccionDetalleID { get; set; }

            public DateTime InicioOriginal { get; set; }
            public DateTime FinOriginal { get; set; }

            public DateTime Inicio { get; set; }
            public DateTime Fin { get; set; }

            public decimal HorasProgramadas { get; set; }

            public TimeSpan? Cambio { get; set; }
            public TimeSpan? Arranque { get; set; }
            public DateTime ArranqueFecha { get; set; }

            public int SecuenciaMaquina { get; set; }
            public int EstatusID { get; set; }
            public bool EsMovible { get; set; }
        }


        private sealed class PosicionReacomodoGlobal
        {
            public DateTime Cambio { get; set; }
            public DateTime Arranque { get; set; }
            public DateTime Fin { get; set; }
            public decimal HorasCambio { get; set; }
            public bool MovidoPorMolde { get; set; }
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
                const string sqlLeer = @"SELECT TOP (1) ParoID,EjecucionProduccionID,FechaInicioParo FROM dbo.Produccion_Paros WITH (UPDLOCK,HOLDLOCK) WHERE ParoID=@ParoID AND Activo=1 AND FechaFinParo IS NULL;";
                DateTime fechaInicioParo;
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
                    fechaInicioParo = Convert.ToDateTime(rd["FechaInicioParo"]);
                }
                var duracionMinutos = (int)Math.Max(0, Math.Floor((DateTime.Now - fechaInicioParo).TotalMinutes));
                var requiereReliberacion = duracionMinutos > 15;
                const string sqlCerrar = @"UPDATE dbo.Produccion_Paros SET FechaFinParo=GETDATE(),DuracionMinutos=@DuracionMinutos,EsMayorA15Minutos=CASE WHEN @DuracionMinutos>15 THEN 1 ELSE 0 END,Descripcion=CASE WHEN @ObservacionesCierre IS NULL OR LTRIM(RTRIM(@ObservacionesCierre))=N'' THEN Descripcion WHEN Descripcion IS NULL OR LTRIM(RTRIM(Descripcion))=N'' THEN @ObservacionesCierre ELSE Descripcion+CHAR(13)+CHAR(10)+N'Cierre: '+@ObservacionesCierre END,UsuarioModificacionID=@UsuarioID,FechaModificacion=GETDATE() WHERE ParoID=@ParoID AND EjecucionProduccionID=@EjecucionProduccionID AND Activo=1 AND FechaFinParo IS NULL; IF @@ROWCOUNT<>1 THROW 51400,'El paro cambió mientras se intentaba cerrar.',1;";
                await using (var cmd = new SqlCommand(sqlCerrar, cn, tx))
                {
                    cmd.Parameters.Add("@ParoID", SqlDbType.Int).Value = vm.ParoID;
                    cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                    cmd.Parameters.Add("@DuracionMinutos", SqlDbType.Int).Value = duracionMinutos;
                    cmd.Parameters.Add("@ObservacionesCierre", SqlDbType.NVarChar, 500).Value = string.IsNullOrWhiteSpace(vm.ObservacionesCierre) ? DBNull.Value : vm.ObservacionesCierre.Trim();
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    await cmd.ExecuteNonQueryAsync();
                }
                var ejecucion = await ObtenerEjecucionAsync(ejecucionProduccionId, cn, tx);
                if (ejecucion == null)
                {
                    await tx.RollbackAsync();
                    return NotFound();
                }
                if (requiereReliberacion)
                {
                    await CambiarEstatusEjecucionAsync(ejecucionProduccionId, ProduccionEstatus.EnPreparacion, usuarioId, cn, tx);
                    await CambiarEstatusProgramaAsync(ejecucion.ProgramaProduccionID, ProgramaProduccionEstatus.EnPreparacion, usuarioId, cn, tx);
                    await CrearOActualizarSolicitudReliberacionCalidadPorParoAsync(ejecucionProduccionId, vm.ParoID, duracionMinutos, usuarioId, cn, tx);
                    await tx.CommitAsync();
                    TempData["Success"] = "Paro cerrado. Al superar 15 minutos, Producción regresó a preparación y Calidad debe autorizar la reliberación antes de reiniciar la serie.";
                    return RedirectToAction(nameof(Detalle), new { id = ejecucionProduccionId });
                }
                await CambiarEstatusEjecucionAsync(ejecucionProduccionId, ProduccionEstatus.EnProduccion, usuarioId, cn, tx);
                await CambiarEstatusProgramaAsync(ejecucion.ProgramaProduccionID, ProgramaProduccionEstatus.EnProduccion, usuarioId, cn, tx);
                await tx.CommitAsync();
                TempData["Success"] = "Paro cerrado correctamente. La producción continúa en serie.";
                return RedirectToAction(nameof(Detalle), new { id = ejecucionProduccionId });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] = "No fue posible cerrar el paro: " + ex.Message;
                return ejecucionProduccionId > 0 ? RedirectToAction(nameof(Detalle), new { id = ejecucionProduccionId }) : RedirectToAction(nameof(Index));
            }
        }
        private async Task CrearOActualizarSolicitudReliberacionCalidadPorParoAsync(int ejecucionProduccionId, int paroId, int duracionMinutos, int usuarioId, SqlConnection cn, SqlTransaction tx)
        {
            if (ejecucionProduccionId <= 0) throw new ArgumentException("La ejecución de producción no es válida.", nameof(ejecucionProduccionId));
            if (paroId <= 0) throw new ArgumentException("El paro de producción no es válido.", nameof(paroId));
            if (duracionMinutos <= 15) throw new InvalidOperationException("Solo se debe solicitar reliberación cuando el paro sea mayor a 15 minutos.");
            const string sqlObtenerInspeccion = @"SELECT TOP (1) ci.InspeccionID,ci.Estado,ci.ChecklistArranqueID,ISNULL(ci.ConfiguracionInvalidada,0) AS ConfiguracionInvalidada FROM dbo.Calidad_Inspecciones ci WITH (UPDLOCK,HOLDLOCK) WHERE ci.EjecucionProduccionID=@EjecucionProduccionID AND ISNULL(ci.Estado,N'')<>N'CERRADA' ORDER BY ci.InspeccionID DESC;";
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
            if (configuracionInvalidada) throw new InvalidOperationException("La configuración de la inspección ya se encuentra invalidada. Calidad debe resolver esa condición antes de procesar la reliberación por paro.");
            const string sqlValidarParo = @"SELECT COUNT(1) FROM dbo.Produccion_Paros WITH (UPDLOCK,HOLDLOCK) WHERE ParoID=@ParoID AND EjecucionProduccionID=@EjecucionProduccionID AND Activo=1 AND FechaFinParo IS NOT NULL AND ISNULL(EsMayorA15Minutos,0)=1;";
            await using (var cmd = new SqlCommand(sqlValidarParo, cn, tx))
            {
                cmd.Parameters.Add("@ParoID", SqlDbType.Int).Value = paroId;
                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                if (Convert.ToInt32(await cmd.ExecuteScalarAsync()) <= 0) throw new InvalidOperationException("El paro no está cerrado, no pertenece a la ejecución o no fue marcado como mayor a 15 minutos.");
            }
            const string sqlExiste = @"SELECT TOP (1) ReliberacionID FROM dbo.Calidad_Reliberaciones WITH (UPDLOCK,HOLDLOCK) WHERE InspeccionID=@InspeccionID AND EjecucionProduccionID=@EjecucionProduccionID AND ParoID=@ParoID AND Activo=1;";
            int? reliberacionId;
            await using (var cmd = new SqlCommand(sqlExiste, cn, tx))
            {
                cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value = inspeccionId;
                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                cmd.Parameters.Add("@ParoID", SqlDbType.Int).Value = paroId;
                var resultado = await cmd.ExecuteScalarAsync();
                reliberacionId = resultado == null || resultado == DBNull.Value ? null : Convert.ToInt32(resultado);
            }
            var observacion = $"Solicitud automática de reliberación por paro mayor a 15 minutos. ParoID: {paroId}. Duración registrada: {duracionMinutos} minuto(s).";
            const string sqlActualizarInspeccion = @"UPDATE dbo.Calidad_Inspecciones SET RequiereReliberacion=1,Liberado=0,Estado=N'PENDIENTE_RELIBERACION',ResultadoCalidad=NULL,Etiqueta=NULL,CincoDisparosSegregados=0,CantidadDisparosConformes=0,ValidacionDimensional=NULL,ValidacionApariencia=NULL,ValidacionGauge=NULL,ValidacionConductividad=NULL,FechaNotificacionCalidad=GETDATE(),UsuarioNotificoID=@UsuarioID,MotivoDevolucion=N'Paro mayor a 15 minutos. Se requieren cinco disparos y reliberación de Calidad.',Observaciones=CASE WHEN Observaciones IS NULL OR LTRIM(RTRIM(Observaciones))=N'' THEN @Observacion ELSE Observaciones+CHAR(13)+CHAR(10)+@Observacion END,UsuarioModificacionID=@UsuarioID,FechaModificacion=GETDATE() WHERE InspeccionID=@InspeccionID AND EjecucionProduccionID=@EjecucionProduccionID AND ISNULL(Estado,N'')<>N'CERRADA'; IF @@ROWCOUNT<>1 THROW 51401,'No fue posible marcar la inspección como pendiente de reliberación.',1;";
            await using (var cmd = new SqlCommand(sqlActualizarInspeccion, cn, tx))
            {
                cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value = inspeccionId;
                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                cmd.Parameters.Add("@Observacion", SqlDbType.NVarChar, 1000).Value = observacion;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                await cmd.ExecuteNonQueryAsync();
            }
            if (reliberacionId.HasValue)
            {
                const string sqlReactivar = @"UPDATE dbo.Calidad_Reliberaciones SET Resultado=N'PENDIENTE',FechaSolicitud=GETDATE(),FechaValidacion=NULL,UsuarioSolicitudID=@UsuarioID,UsuarioCalidadID=NULL,Observaciones=CASE WHEN Observaciones IS NULL OR LTRIM(RTRIM(Observaciones))=N'' THEN @Observacion ELSE Observaciones+CHAR(13)+CHAR(10)+@Observacion END,UsuarioModificacionID=@UsuarioID,FechaModificacion=GETDATE() WHERE ReliberacionID=@ReliberacionID AND Activo=1;";
                await using var cmd = new SqlCommand(sqlReactivar, cn, tx);
                cmd.Parameters.Add("@ReliberacionID", SqlDbType.Int).Value = reliberacionId.Value;
                cmd.Parameters.Add("@Observacion", SqlDbType.NVarChar, 1000).Value = "La solicitud de reliberación fue sincronizada nuevamente desde Producción.";
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                await cmd.ExecuteNonQueryAsync();
            }
            else
            {
                const string sqlInsertar = @"DECLARE @NumeroReliberacion INT; SELECT @NumeroReliberacion=ISNULL(MAX(NumeroReliberacion),0)+1 FROM dbo.Calidad_Reliberaciones WITH (UPDLOCK,HOLDLOCK) WHERE InspeccionID=@InspeccionID; INSERT INTO dbo.Calidad_Reliberaciones(InspeccionID,EjecucionProduccionID,ParoID,NumeroReliberacion,Motivo,FechaSolicitud,UsuarioSolicitudID,Resultado,Observaciones,UsuarioCreacionID,FechaCreacion,Activo) VALUES(@InspeccionID,@EjecucionProduccionID,@ParoID,@NumeroReliberacion,@Motivo,GETDATE(),@UsuarioID,N'PENDIENTE',@Observaciones,@UsuarioID,GETDATE(),1);";
                await using var cmd = new SqlCommand(sqlInsertar, cn, tx);
                cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value = inspeccionId;
                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                cmd.Parameters.Add("@ParoID", SqlDbType.Int).Value = paroId;
                cmd.Parameters.Add("@Motivo", SqlDbType.NVarChar, 500).Value = $"Paro mayor a 15 minutos. Duración registrada: {duracionMinutos} minuto(s).";
                cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1000).Value = "Producción debe ejecutar nuevamente cinco disparos de prueba y Calidad debe autorizar la reliberación antes de reiniciar la serie.";
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                await cmd.ExecuteNonQueryAsync();
            }
            const string sqlHistorial = @"INSERT INTO dbo.Calidad_InspeccionHistorial(InspeccionID,Movimiento,EstadoAnterior,EstadoNuevo,ResultadoCalidad,Etiqueta,Comentario,UsuarioID,FechaMovimiento) VALUES(@InspeccionID,N'SOLICITUD_RELIBERACION',@EstadoAnterior,N'PENDIENTE_RELIBERACION',NULL,NULL,@Comentario,@UsuarioID,GETDATE());";
            await using (var cmd = new SqlCommand(sqlHistorial, cn, tx))
            {
                cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value = inspeccionId;
                cmd.Parameters.Add("@EstadoAnterior", SqlDbType.NVarChar, 50).Value = string.IsNullOrWhiteSpace(estadoAnterior) ? DBNull.Value : estadoAnterior;
                cmd.Parameters.Add("@Comentario", SqlDbType.NVarChar, 1000).Value = observacion + " Producción regresó a preparación y queda bloqueada hasta la autorización de Calidad.";
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                await cmd.ExecuteNonQueryAsync();
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
       ISNULL(EsCambioMolde,0) AS EsCambioMolde,OperadorID,OperadorNombre,OperadorAuxiliarID,OperadorAuxiliarNombre,
       ISNULL(OperadoresModificadosManual,0) AS OperadoresModificadosManual,MotivoCambioOperadores,
       FechaInicioReal,FechaFinReal,CantidadPlaneada,CantidadOKTotal,CantidadSospechosaTotal,CantidadScrapTotal,
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
       FechaInicioReal,FechaFinReal,CantidadPlaneada,CantidadOKTotal,CantidadSospechosaTotal,CantidadScrapTotal,
       EstatusID,Observaciones,UsuarioCreacionID,FechaCreacion,UsuarioModificacionID,FechaModificacion,Activo
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

        private async Task<ProgramaParaProduccion?> ObtenerProgramaParaIniciarAsync(int programaProduccionId, SqlConnection cn, SqlTransaction tx)
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
FROM dbo.Planeacion_ProgramaProduccion pp
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
  AND ISNULL(pp.EstatusID,1) NOT IN(3,4,5,8,9,99);";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;
            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync()) return null;
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

        private async Task<int> InsertarEjecucionAsync(ProgramaParaProduccion programa, int? operadorId, string? operadorNombre, int? operadorAuxiliarId, string? operadorAuxiliarNombre, string? observaciones, int usuarioId, SqlConnection cn, SqlTransaction tx)
        {
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

SELECT TOP (1) EjecucionProduccionID
FROM @Ids;";
            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programa.ProgramaProduccionID;
            cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = (object?)programa.SolicitudProduccionID ?? DBNull.Value;
            cmd.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value = (object?)programa.SolicitudProduccionDetalleID ?? DBNull.Value;
            cmd.Parameters.Add("@ReleaseID", SqlDbType.Int).Value = (object?)programa.ReleaseID ?? DBNull.Value;
            cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = (object?)programa.ReleaseDetalleID ?? DBNull.Value;
            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = (object?)programa.MaquinaID ?? DBNull.Value;
            cmd.Parameters.Add("@MaquinaCodigo", SqlDbType.NVarChar, 100).Value = (object?)programa.MaquinaCodigo ?? DBNull.Value;
            cmd.Parameters.Add("@MaquinaNombre", SqlDbType.NVarChar, 200).Value = (object?)programa.MaquinaNombre ?? DBNull.Value;
            cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = (object?)programa.ParteID ?? DBNull.Value;
            cmd.Parameters.Add("@NumeroParte", SqlDbType.NVarChar, 120).Value = (object?)programa.NumeroParte ?? DBNull.Value;
            cmd.Parameters.Add("@ReferenciaSAP", SqlDbType.NVarChar, 150).Value = (object?)programa.ReferenciaSAP ?? DBNull.Value;
            cmd.Parameters.Add("@DescripcionParte", SqlDbType.NVarChar, 300).Value = (object?)programa.DescripcionParte ?? DBNull.Value;
            cmd.Parameters.Add("@MoldeID", SqlDbType.Int).Value = (object?)programa.MoldeID ?? DBNull.Value;
            cmd.Parameters.Add("@MoldeCodigo", SqlDbType.NVarChar, 100).Value = (object?)programa.MoldeCodigo ?? DBNull.Value;
            cmd.Parameters.Add("@OperadorID", SqlDbType.Int).Value = (object?)operadorId ?? DBNull.Value;
            cmd.Parameters.Add("@OperadorNombre", SqlDbType.NVarChar, 200).Value = string.IsNullOrWhiteSpace(operadorNombre) ? DBNull.Value : operadorNombre.Trim();
            cmd.Parameters.Add("@OperadorAuxiliarID", SqlDbType.Int).Value = (object?)operadorAuxiliarId ?? DBNull.Value;
            cmd.Parameters.Add("@OperadorAuxiliarNombre", SqlDbType.NVarChar, 200).Value = string.IsNullOrWhiteSpace(operadorAuxiliarNombre) ? DBNull.Value : operadorAuxiliarNombre.Trim();
            cmd.Parameters.Add("@EsCambioMolde", SqlDbType.Bit).Value = programa.EsCambioMolde;

            DateTime? fechaCambioMoldeProgramada = null;
            DateTime? fechaArranqueProgramada = null;
            var fechaBase = programa.FechaInicioProgramada?.Date ?? DateTime.Today;

            if (programa.Cambio.HasValue)
                fechaCambioMoldeProgramada = fechaBase.Add(programa.Cambio.Value);

            if (programa.Arranque.HasValue)
                fechaArranqueProgramada = fechaBase.Add(programa.Arranque.Value);
            else if (programa.FechaInicioProgramada.HasValue)
                fechaArranqueProgramada = programa.FechaInicioProgramada.Value;

            cmd.Parameters.Add("@FechaCambioMoldeProgramada", SqlDbType.DateTime).Value = fechaCambioMoldeProgramada.HasValue ? fechaCambioMoldeProgramada.Value : DBNull.Value;
            cmd.Parameters.Add("@FechaArranqueProgramada", SqlDbType.DateTime).Value = fechaArranqueProgramada.HasValue ? fechaArranqueProgramada.Value : DBNull.Value;
            cmd.Parameters.Add("@CantidadPlaneada", SqlDbType.Int).Value = (object?)programa.CantidadPlaneada ?? DBNull.Value;
            cmd.Parameters.Add("@EstatusID", SqlDbType.Int).Value = ProduccionEstatus.EnPreparacion;
            cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 500).Value = (object?)observaciones ?? DBNull.Value;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;

            var resultado = await cmd.ExecuteScalarAsync();

            if (resultado == null || resultado == DBNull.Value)
                throw new InvalidOperationException("La ejecución fue creada, pero no fue posible recuperar EjecucionProduccionID.");

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


        private async Task<bool> EsReinicioSeriePendienteAsync(
    int ejecucionProduccionId,
    SqlConnection cn)
        {
            const string sql = @"
SELECT
    CASE
        WHEN EXISTS
        (
            SELECT 1
            FROM dbo.Produccion_Paros p
            INNER JOIN dbo.Calidad_Reliberaciones r
                ON r.ParoID = p.ParoID
               AND r.EjecucionProduccionID = p.EjecucionProduccionID
               AND r.Activo = 1
            WHERE p.EjecucionProduccionID = @EjecucionProduccionID
              AND p.Activo = 1
              AND p.FechaFinParo IS NOT NULL
              AND ISNULL(p.EsMayorA15Minutos, 0) = 1
              AND UPPER(LTRIM(RTRIM(ISNULL(r.Resultado, N'')))) = N'AUTORIZADA'
              AND NOT EXISTS
              (
                  SELECT 1
                  FROM dbo.Calidad_InspeccionHistorial h
                  INNER JOIN dbo.Calidad_Inspecciones ci
                      ON ci.InspeccionID = h.InspeccionID
                  WHERE ci.EjecucionProduccionID = @EjecucionProduccionID
                    AND h.Movimiento = N'CONFIRMACION_INICIO_SERIE_PRODUCCION'
                    AND h.FechaMovimiento >=
                        COALESCE(r.FechaValidacion, p.FechaFinParo)
              )
        )
        THEN CAST(1 AS BIT)
        ELSE CAST(0 AS BIT)
    END;";

            await using var cmd = new SqlCommand(sql, cn);

            cmd.Parameters.Add(
                "@EjecucionProduccionID",
                SqlDbType.Int).Value =
                ejecucionProduccionId;

            var resultado = await cmd.ExecuteScalarAsync();

            return resultado != null &&
                   resultado != DBNull.Value &&
                   Convert.ToBoolean(resultado);
        }

        private sealed class ContextoReinicioSerie
        {
            public int ParoID { get; set; }
            public int ReliberacionID { get; set; }
            public DateTime FechaInicioParo { get; set; }
            public DateTime FechaFinParo { get; set; }
            public DateTime? FechaValidacionCalidad { get; set; }
        }

        private async Task<ContextoReinicioSerie?>
    ObtenerContextoReinicioSerieAsync(
        int ejecucionProduccionId,
        SqlConnection cn,
        SqlTransaction tx)
        {
            const string sql = @"
SELECT TOP (1)
    p.ParoID,
    p.FechaInicioParo,
    p.FechaFinParo,
    r.ReliberacionID,
    r.FechaValidacion
FROM dbo.Produccion_Paros p WITH (UPDLOCK, HOLDLOCK)
INNER JOIN dbo.Calidad_Reliberaciones r WITH (UPDLOCK, HOLDLOCK)
    ON r.ParoID = p.ParoID
   AND r.EjecucionProduccionID = p.EjecucionProduccionID
   AND r.Activo = 1
WHERE p.EjecucionProduccionID = @EjecucionProduccionID
  AND p.Activo = 1
  AND p.FechaFinParo IS NOT NULL
  AND ISNULL(p.EsMayorA15Minutos, 0) = 1
  AND UPPER(LTRIM(RTRIM(ISNULL(r.Resultado, N'')))) = N'AUTORIZADA'
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.Calidad_InspeccionHistorial h
      INNER JOIN dbo.Calidad_Inspecciones ci
          ON ci.InspeccionID = h.InspeccionID
      WHERE ci.EjecucionProduccionID = @EjecucionProduccionID
        AND h.Movimiento = N'CONFIRMACION_INICIO_SERIE_PRODUCCION'
        AND h.FechaMovimiento >=
            COALESCE(r.FechaValidacion, p.FechaFinParo)
  )
ORDER BY
    p.FechaInicioParo DESC,
    p.ParoID DESC;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add(
                "@EjecucionProduccionID",
                SqlDbType.Int).Value =
                ejecucionProduccionId;

            await using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
                return null;

            return new ContextoReinicioSerie
            {
                ParoID = Convert.ToInt32(rd["ParoID"]),
                ReliberacionID = Convert.ToInt32(rd["ReliberacionID"]),
                FechaInicioParo = Convert.ToDateTime(rd["FechaInicioParo"]),
                FechaFinParo = Convert.ToDateTime(rd["FechaFinParo"]),
                FechaValidacionCalidad =
                    rd["FechaValidacion"] == DBNull.Value
                        ? null
                        : Convert.ToDateTime(rd["FechaValidacion"])
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
        public async Task<IActionResult> PanelEjecuciones(
    int? estatusId = null,
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

                    EstatusID =
                        estatusId,

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
                await CargarMaquinasAsync(cn);

            vm.Estatus =
                await CargarEstatusProduccionAsync(cn);

    
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

    ISNULL(
        e.EsCambioMolde,
        0
    ) AS EsCambioMolde,

    e.OperadorID,
    e.OperadorNombre,

    e.OperadorAuxiliarID,
    e.OperadorAuxiliarNombre,

    ISNULL(
        e.OperadoresModificadosManual,
        0
    ) AS OperadoresModificadosManual,

    e.MotivoCambioOperadores,

    e.FechaInicioReal,
    e.FechaFinReal,

    e.CantidadPlaneada,

    ISNULL(
        e.CantidadOKTotal,
        0
    ) AS CantidadOKTotal,

    ISNULL(
        e.CantidadSospechosaTotal,
        0
    ) AS CantidadSospechosaTotal,

    ISNULL(
        e.CantidadScrapTotal,
        0
    ) AS CantidadScrapTotal,

    e.EstatusID,
    e.Observaciones,

    e.UsuarioCreacionID,
    e.FechaCreacion,

    e.UsuarioModificacionID,
    e.FechaModificacion,

    e.Activo

FROM dbo.Produccion_Ejecucion e

WHERE e.Activo = 1

AND
(
       @MaquinaID IS NULL
    OR e.MaquinaID = @MaquinaID
)

AND
(
       @FechaDesde IS NULL
    OR CONVERT(
           DATE,
           e.FechaCreacion
       ) >= @FechaDesde
)

AND
(
       @FechaHasta IS NULL
    OR CONVERT(
           DATE,
           e.FechaCreacion
       ) <= @FechaHasta
)

AND
(
       @Busqueda IS NULL

    OR e.MaquinaCodigo
        LIKE N'%' + @Busqueda + N'%'

    OR e.MaquinaNombre
        LIKE N'%' + @Busqueda + N'%'

    OR e.NumeroParte
        LIKE N'%' + @Busqueda + N'%'

    OR e.ReferenciaSAP
        LIKE N'%' + @Busqueda + N'%'

    OR e.DescripcionParte
        LIKE N'%' + @Busqueda + N'%'

    OR e.MoldeCodigo
        LIKE N'%' + @Busqueda + N'%'

    OR e.OperadorNombre
        LIKE N'%' + @Busqueda + N'%'

    OR e.OperadorAuxiliarNombre
        LIKE N'%' + @Busqueda + N'%'

    OR CONVERT(
           NVARCHAR(30),
           e.ProgramaProduccionID
       )
       LIKE N'%' + @Busqueda + N'%'

    OR CONVERT(
           NVARCHAR(30),
           e.SolicitudProduccionID
       )
       LIKE N'%' + @Busqueda + N'%'
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

            await using var cmd =
                new SqlCommand(
                    sql,
                    cn);

            cmd.Parameters.Add(
                "@MaquinaID",
                SqlDbType.Int).Value =
                maquinaId.HasValue &&
                maquinaId.Value > 0
                    ? maquinaId.Value
                    : DBNull.Value;

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
                string.IsNullOrWhiteSpace(
                    busqueda)
                    ? DBNull.Value
                    : busqueda.Trim();

            await using var rd =
                await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                vm.Ejecuciones.Add(
                    MapearEjecucion(rd));
            }

            return PartialView(
                "_Ejecuciones",
                vm);
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
     int operadorPrincipalPersonaId,
     int? operadorAuxiliarPersonaId,
     string? motivoCambio)
        {
            if (!UsuarioEnSesion())
                return RedirectToAction("Login", "Login");

            if (ejecucionProduccionId <= 0)
            {
                TempData["Error"] =
                    "No se recibió una ejecución válida.";

                return RedirectToAction(nameof(Index));
            }

            if (operadorPrincipalPersonaId <= 0)
            {
                TempData["Error"] =
                    "Selecciona al operador principal.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = ejecucionProduccionId });
            }

            if (operadorAuxiliarPersonaId.HasValue &&
                operadorAuxiliarPersonaId.Value <= 0)
            {
                operadorAuxiliarPersonaId = null;
            }

            if (operadorAuxiliarPersonaId.HasValue &&
                operadorAuxiliarPersonaId.Value ==
                operadorPrincipalPersonaId)
            {
                TempData["Error"] =
                    "El operador principal y el operador auxiliar " +
                    "no pueden ser la misma persona.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = ejecucionProduccionId });
            }

            motivoCambio = motivoCambio?.Trim();

            if (string.IsNullOrWhiteSpace(motivoCambio))
            {
                TempData["Error"] =
                    "Captura el motivo del cambio de operadores.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = ejecucionProduccionId });
            }

            if (motivoCambio.Length > 1000)
            {
                TempData["Error"] =
                    "El motivo del cambio no puede superar " +
                    "1000 caracteres.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = ejecucionProduccionId });
            }

            var usuarioId = ObtenerUsuarioID();

            await using var cn =
                new SqlConnection(ConnectionString);

            await cn.OpenAsync();

            await using var tx =
                (SqlTransaction)await cn.BeginTransactionAsync(
                    IsolationLevel.Serializable);

            try
            {
                
                const string sqlEjecucion = @"
SELECT TOP (1)
    e.ProgramaProduccionID,
    ISNULL(e.EstatusID, 0) AS EstatusID,

    e.OperadorID,
    e.OperadorNombre,

    e.OperadorAuxiliarID,
    e.OperadorAuxiliarNombre

FROM dbo.Produccion_Ejecucion e WITH (UPDLOCK, HOLDLOCK)

WHERE e.EjecucionProduccionID = @EjecucionProduccionID
  AND e.Activo = 1;";

                int programaProduccionId;
                int estatusId;

                int? operadorPrincipalAnteriorId;
                int? operadorAuxiliarAnteriorId;

                string operadorPrincipalAnteriorNombre;
                string operadorAuxiliarAnteriorNombre;

                await using (
                    var cmd = new SqlCommand(
                        sqlEjecucion,
                        cn,
                        tx))
                {
                    cmd.Parameters.Add(
                        "@EjecucionProduccionID",
                        SqlDbType.Int).Value =
                        ejecucionProduccionId;

                    await using var rd =
                        await cmd.ExecuteReaderAsync();

                    if (!await rd.ReadAsync())
                    {
                        await tx.RollbackAsync();

                        TempData["Error"] =
                            "No se encontró la ejecución activa.";

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
                            : rd["OperadorNombre"]
                                .ToString()?
                                .Trim()
                              ?? "Sin asignar";

                    operadorAuxiliarAnteriorNombre =
                        rd["OperadorAuxiliarNombre"] == DBNull.Value
                            ? "Sin asignar"
                            : rd["OperadorAuxiliarNombre"]
                                .ToString()?
                                .Trim()
                              ?? "Sin asignar";
                }

                /*
                 * No se bloquea por estatus.
                 *
                 * El operador puede actualizarse mientras la ejecución
                 * continúe activa, sin importar si está en preparación,
                 * producción, pausa o ya fue terminada.
                 */

                /*
                 * ============================================================
                 * VALIDAR OPERADOR PRINCIPAL
                 * ============================================================
                 */
                var operadorPrincipalNombre =
                    await ObtenerPersonaNombreAsync(
                        operadorPrincipalPersonaId,
                        cn,
                        tx);

                if (string.IsNullOrWhiteSpace(
                    operadorPrincipalNombre))
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "No se encontró al operador principal seleccionado.";

                    return RedirectToAction(
                        nameof(Detalle),
                        new { id = ejecucionProduccionId });
                }

                /*
                 * ============================================================
                 * VALIDAR OPERADOR AUXILIAR
                 * ============================================================
                 */
                string? operadorAuxiliarNombre = null;

                if (operadorAuxiliarPersonaId.HasValue)
                {
                    operadorAuxiliarNombre =
                        await ObtenerPersonaNombreAsync(
                            operadorAuxiliarPersonaId.Value,
                            cn,
                            tx);

                    if (string.IsNullOrWhiteSpace(
                        operadorAuxiliarNombre))
                    {
                        await tx.RollbackAsync();

                        TempData["Error"] =
                            "No se encontró al operador auxiliar seleccionado.";

                        return RedirectToAction(
                            nameof(Detalle),
                            new { id = ejecucionProduccionId });
                    }
                }

                /*
                 * ============================================================
                 * EVITAR ACTUALIZACIÓN INNECESARIA
                 * ============================================================
                 */
                var principalSinCambio =
                    operadorPrincipalAnteriorId ==
                    operadorPrincipalPersonaId;

                var auxiliarSinCambio =
                    operadorAuxiliarAnteriorId ==
                    operadorAuxiliarPersonaId;

                if (principalSinCambio &&
                    auxiliarSinCambio)
                {
                    await tx.CommitAsync();

                    TempData["Info"] =
                        "Los operadores seleccionados ya se encuentran " +
                        "asignados a la ejecución.";

                    return RedirectToAction(
                        nameof(Detalle),
                        new { id = ejecucionProduccionId });
                }

                /*
                 * ============================================================
                 * TEXTO PARA BITÁCORA
                 * ============================================================
                 */
                var auxiliarNuevoTexto =
                    string.IsNullOrWhiteSpace(
                        operadorAuxiliarNombre)
                        ? "Sin asignar"
                        : operadorAuxiliarNombre;

                var observacionCambio =
                    "Cambio manual de operadores. " +
                    $"Principal: {operadorPrincipalAnteriorNombre} " +
                    $"→ {operadorPrincipalNombre}. " +
                    $"Auxiliar: {operadorAuxiliarAnteriorNombre} " +
                    $"→ {auxiliarNuevoTexto}. " +
                    $"Motivo: {motivoCambio}";

                if (observacionCambio.Length > 1000)
                {
                    observacionCambio =
                        observacionCambio[..1000];
                }

                /*
                 * ============================================================
                 * ACTUALIZAR EJECUCIÓN
                 * ============================================================
                 */
                const string sqlActualizarEjecucion = @"
UPDATE dbo.Produccion_Ejecucion
SET
    OperadorID =
        @OperadorPrincipalPersonaID,

    OperadorNombre =
        @OperadorPrincipalNombre,

    OperadorAuxiliarID =
        @OperadorAuxiliarPersonaID,

    OperadorAuxiliarNombre =
        @OperadorAuxiliarNombre,

    OperadoresModificadosManual = 1,

    MotivoCambioOperadores =
        @MotivoCambio,

    UsuarioModificacionID =
        @UsuarioID,

    FechaModificacion =
        GETDATE(),

    Observaciones =
        CASE
            WHEN Observaciones IS NULL
              OR LTRIM(RTRIM(Observaciones)) = N''
                THEN @ObservacionCambio

            ELSE
                Observaciones
                + CHAR(13)
                + CHAR(10)
                + @ObservacionCambio
        END

WHERE EjecucionProduccionID =
      @EjecucionProduccionID

  AND ProgramaProduccionID =
      @ProgramaProduccionID

  AND Activo = 1;

IF @@ROWCOUNT <> 1
BEGIN
    THROW 51300,
          'La ejecución cambió mientras se actualizaban los operadores.',
          1;
END;";

                await using (
                    var cmd = new SqlCommand(
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
                        "@OperadorPrincipalPersonaID",
                        SqlDbType.Int).Value =
                        operadorPrincipalPersonaId;

                    cmd.Parameters.Add(
                        "@OperadorPrincipalNombre",
                        SqlDbType.NVarChar,
                        200).Value =
                        operadorPrincipalNombre.Trim();

                    cmd.Parameters.Add(
                        "@OperadorAuxiliarPersonaID",
                        SqlDbType.Int).Value =
                        operadorAuxiliarPersonaId.HasValue
                            ? operadorAuxiliarPersonaId.Value
                            : DBNull.Value;

                    cmd.Parameters.Add(
                        "@OperadorAuxiliarNombre",
                        SqlDbType.NVarChar,
                        200).Value =
                        string.IsNullOrWhiteSpace(
                            operadorAuxiliarNombre)
                            ? DBNull.Value
                            : operadorAuxiliarNombre.Trim();

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

                /*
                 * ============================================================
                 * SINCRONIZAR PLANEACIÓN
                 * ============================================================
                 */
                await SincronizarOperadorProgramaAsync(
                    programaProduccionId,
                    operadorPrincipalPersonaId,
                    "PRINCIPAL",
                    usuarioId,
                    cn,
                    tx);

                await SincronizarOperadorProgramaAsync(
                    programaProduccionId,
                    operadorAuxiliarPersonaId,
                    "AUXILIAR",
                    usuarioId,
                    cn,
                    tx);

                /*
                 * ============================================================
                 * ACTUALIZAR INFORMACIÓN DE CALIDAD SIN INVALIDAR
                 * ============================================================
                 *
                 * Solo se actualizan los nombres y los IDs.
                 *
                 * No se modifica:
                 * - ConfiguracionInvalidada
                 * - FechaInvalidacion
                 * - UsuarioInvalidacionID
                 * - MotivoInvalidacion
                 * - Liberado
                 * - ResultadoCalidad
                 * - Etiqueta
                 * - Estado
                 */
                const string sqlActualizarCalidad = @"
DECLARE @InspeccionID INT;
DECLARE @EstadoActual NVARCHAR(50);

SELECT TOP (1)
    @InspeccionID =
        i.InspeccionID,

    @EstadoActual =
        UPPER(
            LTRIM(
                RTRIM(
                    ISNULL(i.Estado, N'')
                )
            )
        )

FROM dbo.Calidad_Inspecciones i
    WITH (UPDLOCK, HOLDLOCK)

WHERE i.EjecucionProduccionID =
      @EjecucionProduccionID

  AND ISNULL(i.Estado, N'') <>
      N'CERRADA'

ORDER BY
    i.InspeccionID DESC;

IF @InspeccionID IS NOT NULL
BEGIN
    UPDATE dbo.Calidad_Inspecciones
    SET
        OperadorPrincipalPersonaID =
            @OperadorPrincipalPersonaID,

        OperadorPrincipalNombre =
            @OperadorPrincipalNombre,

        OperadorAuxiliarPersonaID =
            @OperadorAuxiliarPersonaID,

        OperadorAuxiliarNombre =
            @OperadorAuxiliarNombre,

        UsuarioModificacionID =
            @UsuarioID,

        FechaModificacion =
            GETDATE()

    WHERE InspeccionID =
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

                await using (
                    var cmd = new SqlCommand(
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
                        operadorPrincipalPersonaId;

                    cmd.Parameters.Add(
                        "@OperadorPrincipalNombre",
                        SqlDbType.NVarChar,
                        200).Value =
                        operadorPrincipalNombre.Trim();

                    cmd.Parameters.Add(
                        "@OperadorAuxiliarPersonaID",
                        SqlDbType.Int).Value =
                        operadorAuxiliarPersonaId.HasValue
                            ? operadorAuxiliarPersonaId.Value
                            : DBNull.Value;

                    cmd.Parameters.Add(
                        "@OperadorAuxiliarNombre",
                        SqlDbType.NVarChar,
                        200).Value =
                        string.IsNullOrWhiteSpace(
                            operadorAuxiliarNombre)
                            ? DBNull.Value
                            : operadorAuxiliarNombre.Trim();

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
                    "Operadores actualizados correctamente. " +
                    "El cambio no afecta la liberación ni el estado de Calidad.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                TempData["Error"] =
                    "No fue posible cambiar los operadores: " +
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

        private async Task<HashSet<int>> ObtenerMaquinasOcupadasAsync(
    SqlConnection cn)
        {
            var maquinas = new HashSet<int>();

            const string sql = @"
SELECT DISTINCT
    e.MaquinaID
FROM dbo.Produccion_Ejecucion e
WHERE e.Activo = 1
  AND e.MaquinaID IS NOT NULL
  AND e.EstatusID IN
  (
      @EnPreparacion,
      @EnProduccion,
      @Pausado
  );";

            await using var cmd =
                new SqlCommand(sql, cn);

            cmd.Parameters.Add(
                "@EnPreparacion",
                SqlDbType.Int).Value =
                ProduccionEstatus.EnPreparacion;

            cmd.Parameters.Add(
                "@EnProduccion",
                SqlDbType.Int).Value =
                ProduccionEstatus.EnProduccion;

            cmd.Parameters.Add(
                "@Pausado",
                SqlDbType.Int).Value =
                ProduccionEstatus.Pausado;

            await using var rd =
                await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                if (rd["MaquinaID"] != DBNull.Value)
                {
                    maquinas.Add(
                        Convert.ToInt32(
                            rd["MaquinaID"]));
                }
            }

            return maquinas;
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