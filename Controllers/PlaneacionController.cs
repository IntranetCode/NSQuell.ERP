using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers
{
    public class PlaneacionController : Controller
    {
        private readonly IConfiguration _configuration;

        public PlaneacionController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private string ConnectionString =>
            _configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("No se encontró la cadena de conexión DefaultConnection.");

        // Index
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var lista = new List<PlaneacionOFIndexVm>();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            const string sqlOF = @"
SELECT
    s.SolicitudProduccionID,
    s.FolioSolicitud,
    s.NumeroOFRecibida,
    s.FechaSolicitud,
    s.FechaRequerida,
    s.FechaInicioPlaneada,
    s.FechaFinPlaneada,
    ISNULL(c.Nombre, s.ClienteNombre) AS Cliente,
    s.Prioridad,
    ISNULL(NULLIF(s.TipoOF, ''), 'RELEASE') AS TipoOF,
    s.MotivoTipoOF,
    s.EstatusID,
    s.ResponsablePlaneacionNombre,

    CASE
        WHEN ISNULL(maquinaResumen.TotalMaquinas, 0) = 1
            THEN maquinaResumen.MaquinaID
        ELSE NULL
    END AS MaquinaID,

    CASE
        WHEN ISNULL(maquinaResumen.TotalMaquinas, 0) = 0
            THEN 'SIN MAQUINA'
        WHEN maquinaResumen.TotalMaquinas = 1
            THEN ISNULL(m.Codigo, 'MAQUINA SIN CODIGO')
        ELSE 'VARIAS MAQUINAS'
    END AS MaquinaCodigo,

    CASE
        WHEN ISNULL(maquinaResumen.TotalMaquinas, 0) = 0
            THEN 'Sin asignacion de maquina'
        WHEN maquinaResumen.TotalMaquinas = 1
            THEN ISNULL(m.Nombre, ISNULL(m.Codigo, 'Maquina'))
        ELSE 'La OF tiene asignaciones en mas de una maquina'
    END AS MaquinaNombre,

    COUNT(DISTINCT d.SolicitudProduccionDetalleID) AS TotalRenglones,
    ISNULL(SUM(d.CantidadPiezas), 0) AS TotalPiezas
FROM dbo.SolicitudesProduccion s
LEFT JOIN dbo.ERP_Clientes c
    ON c.ClienteID = s.ClienteID
LEFT JOIN dbo.SolicitudesProduccionDetalle d
    ON d.SolicitudProduccionID = s.SolicitudProduccionID
   AND d.Activo = 1
OUTER APPLY
(
    SELECT
        COUNT(DISTINCT a.MaquinaID) AS TotalMaquinas,
        MIN(a.MaquinaID) AS MaquinaID
    FROM dbo.SolicitudesProduccionDetalle detalleMaquina
    INNER JOIN dbo.SolicitudesProduccionAsignacionMaquina a
        ON a.SolicitudProduccionDetalleID = detalleMaquina.SolicitudProduccionDetalleID
       AND a.Activo = 1
    WHERE detalleMaquina.SolicitudProduccionID = s.SolicitudProduccionID
      AND detalleMaquina.Activo = 1
      AND a.MaquinaID IS NOT NULL
) maquinaResumen
LEFT JOIN dbo.ERP_Maquinas m
    ON m.MaquinaID = maquinaResumen.MaquinaID
WHERE s.Activo = 1
GROUP BY
    s.SolicitudProduccionID,
    s.FolioSolicitud,
    s.NumeroOFRecibida,
    s.FechaSolicitud,
    s.FechaRequerida,
    s.FechaInicioPlaneada,
    s.FechaFinPlaneada,
    ISNULL(c.Nombre, s.ClienteNombre),
    s.Prioridad,
    s.TipoOF,
s.MotivoTipoOF,
    s.EstatusID,
    s.ResponsablePlaneacionNombre,
    s.FechaCreacion,
    maquinaResumen.TotalMaquinas,
    maquinaResumen.MaquinaID,
    m.Codigo,
    m.Nombre
ORDER BY s.FechaCreacion DESC;";

            await using (var cmd = new SqlCommand(sqlOF, cn))
            await using (var rd = await cmd.ExecuteReaderAsync())
            {
                while (await rd.ReadAsync())
                {
                    var estatusId = Convert.ToInt32(rd["EstatusID"]);

                    lista.Add(new PlaneacionOFIndexVm
                    {
                        SolicitudProduccionID = Convert.ToInt32(rd["SolicitudProduccionID"]),
                        FolioSolicitud = rd["FolioSolicitud"] as string,
                        NumeroOFRecibida = rd["NumeroOFRecibida"] as string,
                        FechaSolicitud = Convert.ToDateTime(rd["FechaSolicitud"]),
                        FechaRequerida = rd["FechaRequerida"] == DBNull.Value
                            ? null
                            : Convert.ToDateTime(rd["FechaRequerida"]),
                        FechaInicioPlaneada = rd["FechaInicioPlaneada"] == DBNull.Value
                            ? null
                            : Convert.ToDateTime(rd["FechaInicioPlaneada"]),
                        FechaFinPlaneada = rd["FechaFinPlaneada"] == DBNull.Value
                            ? null
                            : Convert.ToDateTime(rd["FechaFinPlaneada"]),
                        Cliente = rd["Cliente"] as string,
                        Prioridad = rd["Prioridad"] as string ?? "Normal",
                        TipoOF = NormalizarTipoOF(rd["TipoOF"] as string),
                        MotivoTipoOF = rd["MotivoTipoOF"] as string,
                        EstatusID = estatusId,
                        EstatusNombre = PlaneacionOFEstatus.Nombre(estatusId),
                        ResponsablePlaneacionNombre = rd["ResponsablePlaneacionNombre"] as string,
                        MaquinaID = rd["MaquinaID"] == DBNull.Value
                            ? null
                            : Convert.ToInt32(rd["MaquinaID"]),
                        MaquinaCodigo = rd["MaquinaCodigo"] as string,
                        MaquinaNombre = rd["MaquinaNombre"] as string,
                        TotalRenglones = Convert.ToInt32(rd["TotalRenglones"]),
                        TotalPiezas = Convert.ToInt32(rd["TotalPiezas"])
                    });
                }
            }

            const string sqlProgramadosSinOF = @"
SELECT
    pp.ProgramaProduccionID,
    pp.ClienteID,
    ISNULL(c.Nombre, pp.ClienteNombre) AS ClienteNombre,
    pp.ReferenciaSAP,
    pp.NumeroParte,
    pp.DesignacionDescripcionSAP,
    pp.CantidadProgramada,
    pp.FechaInicioProgramada,
    pp.FechaFinProgramada,
    pp.FechaCreacion,
    pp.MaquinaID,
    pp.MaquinaCodigo,
    pp.MaquinaNombre,
    pp.MoldeCodigo,
    pp.CondicionProduccion,
    ISNULL(NULLIF(pp.TipoOF, ''), 'RELEASE') AS TipoOF,
    pp.MotivoTipoOF,
    pp.EstatusID
FROM dbo.Planeacion_ProgramaProduccion pp
LEFT JOIN dbo.ERP_Clientes c
    ON c.ClienteID = pp.ClienteID
WHERE pp.Activo = 1
  AND pp.SolicitudProduccionID IS NULL
  AND ISNULL(pp.EstatusID, 1) NOT IN (5, 9, 99)
ORDER BY
    pp.FechaInicioProgramada,
    pp.MaquinaCodigo,
    pp.SecuenciaMaquina,
    pp.ProgramaProduccionID;";

            await using (var cmd = new SqlCommand(sqlProgramadosSinOF, cn))
            await using (var rd = await cmd.ExecuteReaderAsync())
            {
                while (await rd.ReadAsync())
                {
                    var programaProduccionId = Convert.ToInt32(rd["ProgramaProduccionID"]);
                    var maquinaCodigo = rd["MaquinaCodigo"] as string;
                    var maquinaNombre = rd["MaquinaNombre"] as string;
                    var moldeCodigo = rd["MoldeCodigo"] as string;
                    var condicion = rd["CondicionProduccion"] as string;
                    var tipoOF = NormalizarTipoOF(rd["TipoOF"] as string);

                    var folioPendiente = $"PROG-{programaProduccionId:0000}";

                    lista.Add(new PlaneacionOFIndexVm
                    {
                        // ID negativo para identificar en la vista que todavia no existe OF.
                        SolicitudProduccionID = programaProduccionId * -1,

                        FolioSolicitud = folioPendiente,
                        NumeroOFRecibida = "Pendiente generar OF",

                        FechaSolicitud = rd["FechaCreacion"] == DBNull.Value
                            ? DateTime.Today
                            : Convert.ToDateTime(rd["FechaCreacion"]),

                        FechaRequerida = rd["FechaFinProgramada"] == DBNull.Value
                            ? null
                            : Convert.ToDateTime(rd["FechaFinProgramada"]).Date,

                        FechaInicioPlaneada = rd["FechaInicioProgramada"] == DBNull.Value
                            ? null
                            : Convert.ToDateTime(rd["FechaInicioProgramada"]),

                        FechaFinPlaneada = rd["FechaFinProgramada"] == DBNull.Value
                            ? null
                            : Convert.ToDateTime(rd["FechaFinProgramada"]),

                        Cliente = rd["ClienteNombre"] as string,

                        Prioridad = "Programado",
                        TipoOF = tipoOF,
                        MotivoTipoOF = rd["MotivoTipoOF"] as string,
                        EstatusID = 0,
                        EstatusNombre = "Pendiente generar OF",
                        ResponsablePlaneacionNombre =
                            $"Tipo OF: {tipoOF} · Molde: {moldeCodigo ?? "-"} · {condicion ?? "-"}",

                        MaquinaID = rd["MaquinaID"] == DBNull.Value
                            ? null
                            : Convert.ToInt32(rd["MaquinaID"]),
                        MaquinaCodigo = string.IsNullOrWhiteSpace(maquinaCodigo)
                            ? "SIN MAQUINA"
                            : maquinaCodigo,
                        MaquinaNombre = string.IsNullOrWhiteSpace(maquinaNombre)
                            ? "Sin asignacion de maquina"
                            : maquinaNombre,

                        TotalRenglones = 1,
                        TotalPiezas = rd["CantidadProgramada"] == DBNull.Value
                            ? 0
                            : Convert.ToInt32(rd["CantidadProgramada"])
                    });
                }
            }

            lista = lista
                .OrderBy(x => x.MaquinaCodigo == "SIN MAQUINA" ? 1 : 0)
                .ThenBy(x => x.MaquinaCodigo)
                .ThenBy(x => x.SolicitudProduccionID > 0 ? 1 : 0)
                .ThenBy(x => x.FechaInicioPlaneada ?? x.FechaSolicitud)
                .ThenByDescending(x => x.FechaSolicitud)
                .ToList();

            return View(lista);
        }


        // crearr get
        [HttpGet]
        public async Task<IActionResult> Crear()
        {
            var vm = new PlaneacionOFCrearVm
            {
                FolioSolicitud = null,
                NumeroOFRecibida = await ObtenerNumeroOFSugeridoAsync(),
                FechaSolicitud = DateTime.Today,
                OrigenSolicitud = "Dirección",
                Prioridad = "Normal",
                TipoOF = "RELEASE",
                MotivoTipoOF = null
            };

            vm.Detalles.Add(new PlaneacionOFDetalleCrearVm
            {
                Renglon = 1,
                AsignacionesMaquina = new List<PlaneacionOFAsignacionMaquinaCrearVm>
        {
            new PlaneacionOFAsignacionMaquinaCrearVm
            {
                Secuencia = 1
            }
        }
            });

            await CargarCatalogosAsync(vm);

            return View(vm);
        }

        //crear post
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Crear(PlaneacionOFCrearVm vm)
        {
            var usuarioId = ObtenerUsuarioID();
            var usuarioNombre = ObtenerUsuarioNombre();

            if (usuarioId <= 0)
            {
                ModelState.AddModelError("", "No se pudo identificar el usuario de sesión.");
            }

            vm.TipoOF = NormalizarTipoOF(vm.TipoOF);
            vm.MotivoTipoOF = vm.MotivoTipoOF?.Trim();

            ValidarTipoOF(vm.TipoOF, vm.MotivoTipoOF, nameof(vm.MotivoTipoOF));

            vm.Detalles = vm.Detalles
                .Where(d =>
                    d.CantidadPiezas > 0 &&
                    (
                        d.ParteID.HasValue ||
                        !string.IsNullOrWhiteSpace(d.ReferenciaSAP) ||
                        !string.IsNullOrWhiteSpace(d.DesignacionDescripcionSAP)
                    ))
                .ToList();

            if (!vm.Detalles.Any())
            {
                ModelState.AddModelError("", "Debes capturar al menos un renglón de producción.");
            }

            if (!vm.ClienteID.HasValue && string.IsNullOrWhiteSpace(vm.ClienteNombre))
            {
                ModelState.AddModelError("", "Selecciona o captura el cliente.");
            }

            foreach (var detalle in vm.Detalles)
            {
                detalle.AsignacionesMaquina = detalle.AsignacionesMaquina
                    .Where(a => a.MaquinaID > 0 && a.CantidadAsignada > 0)
                    .ToList();

                var totalAsignado = detalle.AsignacionesMaquina.Sum(a => a.CantidadAsignada);

                if (totalAsignado > 0 && totalAsignado != detalle.CantidadPiezas)
                {
                    ModelState.AddModelError(
                        "",
                        $"En el renglón {detalle.Renglon}, la cantidad asignada a máquinas ({totalAsignado}) debe coincidir con la cantidad de piezas ({detalle.CantidadPiezas})."
                    );
                }
            }

            if (!ModelState.IsValid)
            {
                await CargarCatalogosAsync(vm);
                return View(vm);
            }

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var tx = await cn.BeginTransactionAsync();

            try
            {
                if (string.IsNullOrWhiteSpace(vm.FolioSolicitud))
                {
                    vm.FolioSolicitud = await GenerarFolioOFAsync(cn, (SqlTransaction)tx);
                }

                var clienteNombre = vm.ClienteNombre;

                if (vm.ClienteID.HasValue)
                {
                    clienteNombre = await ObtenerClienteNombreAsync(vm.ClienteID.Value, cn, (SqlTransaction)tx);
                }

                var solicitudId = await InsertarEncabezadoAsync(
                    vm,
                    clienteNombre,
                    usuarioId,
                    usuarioNombre,
                    cn,
                    (SqlTransaction)tx
                );

                var renglon = 1;

                foreach (var d in vm.Detalles)
                {
                    await CompletarDetalleDesdeParteAsync(d, cn, (SqlTransaction)tx);
                    CalcularDatosTecnicos(d);

                    await CalcularCostosDetalleAsync(d, cn, (SqlTransaction)tx);

                    var detalleId = await InsertarDetalleAsync(
                        solicitudId,
                        renglon,
                        d,
                        cn,
                        (SqlTransaction)tx
                    );

                    foreach (var a in d.AsignacionesMaquina)
                    {
                        await InsertarAsignacionMaquinaAsync(
                            detalleId,
                            a,
                            d.MoldeID,
                            usuarioId,
                            cn,
                            (SqlTransaction)tx
                        );
                    }

                    renglon++;
                }

                await ActualizarTotalesCostosOFAsync(
    solicitudId,
    vm.Detalles,
    cn,
    (SqlTransaction)tx
);

                await InsertarHistorialAsync(
                    solicitudId,
                    null,
                    PlaneacionOFEstatus.Capturada,
                    "Creación de OF desde Planeación",
                    $"OF capturada por Planeación. Tipo OF: {vm.TipoOF}.",
                    usuarioId,
                    cn,
                    (SqlTransaction)tx
                );

                await tx.CommitAsync();

                TempData["Success"] = "OF capturada correctamente.";
                return RedirectToAction(nameof(Detalle), new { id = solicitudId });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                ModelState.AddModelError("", "Ocurrió un error al guardar la OF: " + ex.Message);
                await CargarCatalogosAsync(vm);
                return View(vm);
            }
        }

        // detalle
        [HttpGet]
        public async Task<IActionResult> Detalle(int id)
        {
            PlaneacionOFDetalleVm? vm = null;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            const string sql = @"
                SELECT
                    s.SolicitudProduccionID,
                    s.FolioSolicitud,
                    s.NumeroOFRecibida,
                    s.FechaSolicitud,
                    s.FechaRequerida,
                    s.FechaInicioPlaneada,
                    s.FechaFinPlaneada,
                    ISNULL(c.Nombre, s.ClienteNombre) AS Cliente,
                    s.Prioridad,
                    ISNULL(NULLIF(s.TipoOF, ''), 'RELEASE') AS TipoOF,
                    s.MotivoTipoOF,
                    s.EstatusID,
                    s.NotasGenerales,
                    s.ResponsablePlaneacionNombre
                FROM dbo.SolicitudesProduccion s
                LEFT JOIN dbo.ERP_Clientes c
                    ON c.ClienteID = s.ClienteID
                WHERE s.SolicitudProduccionID = @SolicitudProduccionID
                AND s.Activo = 1;";

            await using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = id;

                await using var rd = await cmd.ExecuteReaderAsync();

                if (await rd.ReadAsync())
                {
                    var estatusId = Convert.ToInt32(rd["EstatusID"]);

                    vm = new PlaneacionOFDetalleVm
                    {
                        SolicitudProduccionID = Convert.ToInt32(rd["SolicitudProduccionID"]),
                        FolioSolicitud = rd["FolioSolicitud"] as string,
                        NumeroOFRecibida = rd["NumeroOFRecibida"] as string,
                        FechaSolicitud = Convert.ToDateTime(rd["FechaSolicitud"]),
                        FechaRequerida = rd["FechaRequerida"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaRequerida"]),
                        FechaInicioPlaneada = rd["FechaInicioPlaneada"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaInicioPlaneada"]),
                        FechaFinPlaneada = rd["FechaFinPlaneada"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaFinPlaneada"]),
                        Cliente = rd["Cliente"] as string,
                        Prioridad = rd["Prioridad"] as string ?? "Normal",
                        TipoOF = NormalizarTipoOF(rd["TipoOF"] as string),
                        MotivoTipoOF = rd["MotivoTipoOF"] as string,
                        EstatusID = estatusId,
                        EstatusNombre = PlaneacionOFEstatus.Nombre(estatusId),
                        NotasGenerales = rd["NotasGenerales"] as string,
                        ResponsablePlaneacionNombre = rd["ResponsablePlaneacionNombre"] as string
                    };
                }
            }

            if (vm == null)
            {
                return NotFound();
            }

            vm.Detalles = await ObtenerDetallesAsync(id, cn);

            foreach (var detalle in vm.Detalles)
            {
                detalle.AsignacionesMaquina = await ObtenerAsignacionesAsync(detalle.SolicitudProduccionDetalleID, cn);
            }

            vm.Historial = await ObtenerHistorialAsync(id, cn);

            var permisoEdicion = await ObtenerPermisoEdicionOFAsync(
    vm.SolicitudProduccionID,
    vm.FolioSolicitud,
    vm.NumeroOFRecibida,
    vm.EstatusID,
    cn
);

            vm.PuedeEditar = permisoEdicion.PuedeEditar;
            vm.MotivoNoEditable = permisoEdicion.Motivo;


            return View(vm);
        }

        // cancelar
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancelar(int id, string? comentario)
        {
            var usuarioId = ObtenerUsuarioID();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var tx = await cn.BeginTransactionAsync();

            try
            {
                var estatusActual = await ObtenerEstatusActualAsync(id, cn, (SqlTransaction)tx);

                if (!estatusActual.HasValue)
                {
                    await tx.RollbackAsync();
                    return NotFound();
                }

                if (estatusActual.Value >= PlaneacionOFEstatus.EnProduccion)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "No se puede cancelar una OF que ya está en producción o cerrada.";
                    return RedirectToAction(nameof(Detalle), new { id });
                }

                const string sql = @"
UPDATE dbo.SolicitudesProduccion
SET
    EstatusID = @EstatusID,
    UsuarioModificacionID = @UsuarioModificacionID,
    FechaModificacion = GETDATE()
WHERE SolicitudProduccionID = @SolicitudProduccionID;";

                await using (var cmd = new SqlCommand(sql, cn, (SqlTransaction)tx))
                {
                    cmd.Parameters.Add("@EstatusID", SqlDbType.Int).Value = PlaneacionOFEstatus.Cancelada;
                    cmd.Parameters.Add("@UsuarioModificacionID", SqlDbType.Int).Value = usuarioId;
                    cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = id;

                    await cmd.ExecuteNonQueryAsync();
                }

                await InsertarHistorialAsync(
                    id,
                    estatusActual.Value,
                    PlaneacionOFEstatus.Cancelada,
                    "Cancelación de OF",
                    comentario,
                    usuarioId,
                    cn,
                    (SqlTransaction)tx
                );

                await tx.CommitAsync();

                TempData["Success"] = "OF cancelada correctamente.";
                return RedirectToAction(nameof(Detalle), new { id });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] = "Error al cancelar la OF: " + ex.Message;
                return RedirectToAction(nameof(Detalle), new { id });
            }
        }

        // Obtener info de la parte
        [HttpGet]
        public async Task<IActionResult> ObtenerParteInfo(int parteId)
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            const string sql = @"
SELECT
    p.ParteID,
    p.ClienteID,
    c.Nombre AS ClienteNombre,
    p.NumeroParte,
    p.ReferenciaSAP,
    p.Descripcion,
    p.Designacion,

    t.Color,
    t.Cavidades,
    t.ObjetivoHora,
    t.PiezasPorCaja,
    t.MoldePrincipalID,
    mol.CodigoMolde AS MoldeCodigo,
    t.MaquinaPrincipalID,
    t.MaquinaSustitutaID,

    t.MaterialID,
    t.MaterialCodigo,
    t.MaterialDescripcion,

    t.Ciclo,
    t.TipoSecado,
    t.HorasSecado,
    t.PesoBrutoPieza,
    t.EmbalajeCodigo,
    t.EmbalajeDescripcion,
    t.PiezasPorEmbalaje
FROM dbo.ERP_Partes p
LEFT JOIN dbo.ERP_Clientes c
    ON c.ClienteID = p.ClienteID
LEFT JOIN dbo.ERP_ParteDatosTecnicos t
    ON t.ParteID = p.ParteID
   AND t.Activo = 1
LEFT JOIN dbo.ERP_Moldes mol
    ON mol.MoldeID = t.MoldePrincipalID
WHERE p.ParteID = @ParteID
  AND p.Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = parteId;

            await using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
            {
                return Json(new { ok = false, mensaje = "No se encontró la parte." });
            }

            var numeroParte = rd["NumeroParte"] as string ?? "";
            var referencia = rd["ReferenciaSAP"] as string;
            var descripcion = rd["Descripcion"] as string ?? "";
            var designacion = rd["Designacion"] as string;

            return Json(new
            {
                ok = true,

                parteID = Convert.ToInt32(rd["ParteID"]),
                clienteID = rd["ClienteID"] == DBNull.Value ? null : rd["ClienteID"],
                clienteNombre = rd["ClienteNombre"] == DBNull.Value ? null : rd["ClienteNombre"],

                numeroParte,
                referenciaSAP = string.IsNullOrWhiteSpace(referencia) ? numeroParte : referencia,
                designacionDescripcionSAP = !string.IsNullOrWhiteSpace(designacion) ? designacion : descripcion,

                color = rd["Color"] == DBNull.Value ? null : rd["Color"],
                cavidades = rd["Cavidades"] == DBNull.Value ? null : rd["Cavidades"],
                objetivoHora = rd["ObjetivoHora"] == DBNull.Value ? null : rd["ObjetivoHora"],
                piezasPorCaja = rd["PiezasPorCaja"] == DBNull.Value ? null : rd["PiezasPorCaja"],

                moldeID = rd["MoldePrincipalID"] == DBNull.Value ? null : rd["MoldePrincipalID"],
                moldeCodigo = rd["MoldeCodigo"] == DBNull.Value ? null : rd["MoldeCodigo"],

                maquinaPrincipalID = rd["MaquinaPrincipalID"] == DBNull.Value ? null : rd["MaquinaPrincipalID"],
                maquinaSustitutaID = rd["MaquinaSustitutaID"] == DBNull.Value ? null : rd["MaquinaSustitutaID"],

                ciclo = rd["Ciclo"] == DBNull.Value ? null : rd["Ciclo"],
                tipoSecado = rd["TipoSecado"] == DBNull.Value ? null : rd["TipoSecado"],
                horasSecado = rd["HorasSecado"] == DBNull.Value ? null : rd["HorasSecado"],
                pesoBrutoPieza = rd["PesoBrutoPieza"] == DBNull.Value ? null : rd["PesoBrutoPieza"],

                materialID = rd["MaterialID"] == DBNull.Value ? null : rd["MaterialID"],
                materialCodigo = rd["MaterialCodigo"] == DBNull.Value ? null : rd["MaterialCodigo"],
                materialDescripcion = rd["MaterialDescripcion"] == DBNull.Value ? null : rd["MaterialDescripcion"],

                embalajeCodigo = rd["EmbalajeCodigo"] == DBNull.Value ? null : rd["EmbalajeCodigo"],
                embalajeDescripcion = rd["EmbalajeDescripcion"] == DBNull.Value ? null : rd["EmbalajeDescripcion"],
                piezasPorEmbalaje = rd["PiezasPorEmbalaje"] == DBNull.Value ? null : rd["PiezasPorEmbalaje"]
            });
        }

        // calcular datos
        [HttpGet]
        public IActionResult CalcularDatosOF(
    int? cantidadPiezas,
    decimal? horasPlaneadas,
    int? objetivoHora,
    decimal? piezasPorEmbalaje,
    decimal? pesoBrutoPieza)
        {
            var cantidad = cantidadPiezas ?? 0;

            decimal? horasCalculadas = null;
            decimal? cantidadEmbalajes = null;
            decimal? cantidadMpKg = null;

            // Horas planeadas = Cantidad de piezas / Objetivo por hora
            if (cantidad > 0 && objetivoHora.HasValue && objetivoHora.Value > 0)
            {
                horasCalculadas = Math.Round(cantidad / (decimal)objetivoHora.Value, 2);
            }

            // Cantidad de embalajes = Cantidad de piezas / Piezas por embalaje
            if (cantidad > 0 && piezasPorEmbalaje.HasValue && piezasPorEmbalaje.Value > 0)
            {
                cantidadEmbalajes = Math.Ceiling(cantidad / piezasPorEmbalaje.Value);
            }

            // Cantidad MP kg = Peso bruto pieza * Cantidad de piezas
            if (cantidad > 0 && pesoBrutoPieza.HasValue && pesoBrutoPieza.Value > 0)
            {
                cantidadMpKg = Math.Round(cantidad * pesoBrutoPieza.Value, 4);
            }

            return Json(new
            {
                ok = true,
                cantidadPiezas = cantidad,
                horasPlaneadas = horasCalculadas,
                cantidadEmbalajes,
                cantidadMpKg
            });
        }


        [HttpGet]
        public async Task<IActionResult> ValidarDisponibilidadAlmacen(
            int parteId,
            int cantidadPiezas,
            CancellationToken cancellationToken = default)
        {
            if (parteId <= 0)
            {
                return BadRequest(new { ok = false, mensaje = "La parte es obligatoria." });
            }

            if (cantidadPiezas <= 0)
            {
                return BadRequest(new { ok = false, mensaje = "La cantidad de piezas debe ser mayor a cero." });
            }

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(cancellationToken);
            const string sqlParte = @"
SELECT
    p.ParteID,
    p.NumeroParte,
    COALESCE(NULLIF(p.Designacion, ''), p.Descripcion) AS Descripcion,

    t.MaterialID,
    t.MaterialCodigo,
    t.MaterialDescripcion,
    t.PesoBrutoPieza,

    t.EmbalajeCodigo,
    t.EmbalajeDescripcion,
    t.PiezasPorEmbalaje
FROM dbo.ERP_Partes p
LEFT JOIN dbo.ERP_ParteDatosTecnicos t
    ON t.ParteID = p.ParteID
   AND t.Activo = 1
WHERE p.ParteID = @ParteID
  AND p.Activo = 1;";

            int? materialId = null;
            string numeroParte = "";
            string descripcionParte = "";
            string materialCodigo = "";
            string materialDescripcion = "";
            decimal pesoBrutoPieza = 0m;

            string embalajeCodigo = "";
            string embalajeDescripcion = "";
            decimal piezasPorEmbalaje = 0m;

            await using (var cmd = new SqlCommand(sqlParte, cn))
            {
                cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = parteId;

                await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);

                if (!await rd.ReadAsync(cancellationToken))
                {
                    return NotFound(new { ok = false, mensaje = "No se encontró la parte seleccionada." });
                }

                numeroParte = rd["NumeroParte"] as string ?? "";
                descripcionParte = rd["Descripcion"] as string ?? "";

                if (rd["MaterialID"] != DBNull.Value)
                    materialId = Convert.ToInt32(rd["MaterialID"]);

                materialCodigo = rd["MaterialCodigo"] as string ?? "";
                materialDescripcion = rd["MaterialDescripcion"] as string ?? "";

                if (rd["PesoBrutoPieza"] != DBNull.Value)
                    pesoBrutoPieza = Convert.ToDecimal(rd["PesoBrutoPieza"]);

                embalajeCodigo = rd["EmbalajeCodigo"] as string ?? "";
                embalajeDescripcion = rd["EmbalajeDescripcion"] as string ?? "";

                if (rd["PiezasPorEmbalaje"] != DBNull.Value)
                    piezasPorEmbalaje = Convert.ToDecimal(rd["PiezasPorEmbalaje"]);
            }

            var embalaje = await ValidarEmbalajeAsync(
                cn,
                embalajeCodigo,
                embalajeDescripcion,
                piezasPorEmbalaje,
                cantidadPiezas,
                cancellationToken
            );

            var ptDisponible = 0;
            var ptRetenido = 0;
            var ptSemaforo = "";

            const string sqlPT = @"
SELECT TOP 1
    ISNULL(Disponible, 0) AS Disponible,
    ISNULL(Retenido, 0) AS Retenido,
    ISNULL(Semaforo, '') AS Semaforo
FROM dbo.vw_AlmacenPTInventario
WHERE ParteID = @ParteID;";

            await using (var cmd = new SqlCommand(sqlPT, cn))
            {
                cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = parteId;

                await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);

                if (await rd.ReadAsync(cancellationToken))
                {
                    ptDisponible = rd["Disponible"] == DBNull.Value ? 0 : Convert.ToInt32(rd["Disponible"]);
                    ptRetenido = rd["Retenido"] == DBNull.Value ? 0 : Convert.ToInt32(rd["Retenido"]);
                    ptSemaforo = rd["Semaforo"] as string ?? "";
                }
            }

            var ptSuficiente = ptDisponible >= cantidadPiezas;

            var mpRequeridaKg = 0m;
            var mpDisponibleKg = 0m;
            var mpUnidad = "";
            var mpSemaforo = "";
            var mpSuficiente = false;

            if (!ptSuficiente)
            {
                mpRequeridaKg = Math.Round(cantidadPiezas * pesoBrutoPieza, 4);

                if (pesoBrutoPieza <= 0)
                {
                    return Json(new
                    {
                        ok = true,
                        parteId,
                        numeroParte,
                        descripcionParte,
                        cantidadPiezas,

                        pt = new
                        {
                            disponible = ptDisponible,
                            retenido = ptRetenido,
                            requerido = cantidadPiezas,
                            suficiente = false,
                            semaforo = ptSemaforo
                        },

                        mp = new
                        {
                            materialId,
                            codigo = materialCodigo,
                            material = materialDescripcion,
                            requeridoKg = 0,
                            disponibleKg = 0,
                            unidad = "",
                            suficiente = false,
                            semaforo = ""
                        },

                        embalaje,

                        decision = "SIN_PESO_BRUTO",
                        bloquear = true,
                        mensaje = "No hay PT suficiente y la parte no tiene PesoBrutoPieza configurado. No se puede calcular la MP requerida."
                    });
                }

                if (!materialId.HasValue)
                {
                    return Json(new
                    {
                        ok = true,
                        parteId,
                        numeroParte,
                        descripcionParte,
                        cantidadPiezas,

                        pt = new
                        {
                            disponible = ptDisponible,
                            retenido = ptRetenido,
                            requerido = cantidadPiezas,
                            suficiente = false,
                            semaforo = ptSemaforo
                        },

                        mp = new
                        {
                            materialId = (int?)null,
                            codigo = materialCodigo,
                            material = materialDescripcion,
                            requeridoKg = mpRequeridaKg,
                            disponibleKg = 0,
                            unidad = "",
                            suficiente = false,
                            semaforo = ""
                        },

                        embalaje,

                        decision = "SIN_MATERIAL",
                        bloquear = true,
                        mensaje = "No hay PT suficiente y la parte no tiene MaterialID relacionado. Revisa el catálogo de partes/materiales."
                    });
                }

                const string sqlMP = @"
SELECT TOP 1
    MaterialID,
    Codigo,
    Nombre,
    Unidad,
    ISNULL(Saldo, 0) AS Saldo,
    ISNULL(Semaforo, '') AS Semaforo
FROM dbo.vw_AlmacenMPInventario
WHERE MaterialID = @MaterialID;";

                await using var cmd = new SqlCommand(sqlMP, cn);
                cmd.Parameters.Add("@MaterialID", SqlDbType.Int).Value = materialId.Value;

                await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);

                if (await rd.ReadAsync(cancellationToken))
                {
                    materialCodigo = rd["Codigo"] as string ?? materialCodigo;
                    materialDescripcion = rd["Nombre"] as string ?? materialDescripcion;
                    mpUnidad = rd["Unidad"] as string ?? "";
                    mpDisponibleKg = rd["Saldo"] == DBNull.Value ? 0m : Convert.ToDecimal(rd["Saldo"]);
                    mpSemaforo = rd["Semaforo"] as string ?? "";
                }

                mpSuficiente = mpDisponibleKg >= mpRequeridaKg;
            }

            string decision;
            bool bloquear;
            string mensaje;

            if (ptSuficiente)
            {
                decision = "PT";
                bloquear = false;
                mensaje = "Hay producto terminado suficiente. Puedes surtir desde PT o continuar con planeación.";
            }
            else if (mpSuficiente)
            {
                decision = "MP";
                bloquear = false;
                mensaje = "No hay PT suficiente, pero sí hay MP suficiente para producir.";
            }
            else
            {
                decision = "SIN_EXISTENCIA";
                bloquear = true;
                mensaje = "No hay PT suficiente y tampoco hay MP suficiente. Revisar con Almacén o Compras.";
            }

            return Json(new
            {
                ok = true,
                parteId,
                numeroParte,
                descripcionParte,
                cantidadPiezas,

                pt = new
                {
                    disponible = ptDisponible,
                    retenido = ptRetenido,
                    requerido = cantidadPiezas,
                    suficiente = ptSuficiente,
                    semaforo = ptSemaforo
                },

                mp = new
                {
                    materialId,
                    codigo = materialCodigo,
                    material = materialDescripcion,
                    requeridoKg = mpRequeridaKg,
                    disponibleKg = mpDisponibleKg,
                    unidad = mpUnidad,
                    suficiente = mpSuficiente,
                    semaforo = mpSemaforo
                },

                embalaje,

                decision,
                bloquear,
                mensaje
            });
        }


        private async Task<object> ValidarEmbalajeAsync(
    SqlConnection cn,
    string? embalajeCodigo,
    string? embalajeDescripcion,
    decimal piezasPorEmbalaje,
    int cantidadPiezas,
    CancellationToken cancellationToken)
        {
            decimal requerido = 0m;

            if (cantidadPiezas > 0 && piezasPorEmbalaje > 0)
            {
                requerido = Math.Ceiling(cantidadPiezas / piezasPorEmbalaje);
            }

            if (string.IsNullOrWhiteSpace(embalajeCodigo))
            {
                return new
                {
                    embalajeId = (int?)null,
                    codigo = "",
                    nombre = embalajeDescripcion ?? "",
                    requerido,
                    disponible = 0m,
                    unidad = "",
                    suficiente = false,
                    semaforo = "SIN_CODIGO",
                    bloquear = false,
                    mensaje = "La parte no tiene código de embalaje configurado. Revisar catálogo maestro."
                };
            }

            if (requerido <= 0)
            {
                return new
                {
                    embalajeId = (int?)null,
                    codigo = embalajeCodigo,
                    nombre = embalajeDescripcion ?? "",
                    requerido,
                    disponible = 0m,
                    unidad = "",
                    suficiente = false,
                    semaforo = "SIN_CALCULO",
                    bloquear = false,
                    mensaje = "La parte no tiene piezas por embalaje configurado. No se puede calcular embalaje requerido."
                };
            }

            const string sql = @"
SELECT TOP 1
    EmbalajeID,
    Codigo,
    Nombre,
    Unidad,
    ISNULL(Saldo, 0) AS Saldo,
    ISNULL(Semaforo, '') AS Semaforo
FROM dbo.vw_AlmacenEmbalajesInventario
WHERE Codigo = @Codigo;";

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@Codigo", SqlDbType.NVarChar, 100).Value = embalajeCodigo;

            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);

            if (!await rd.ReadAsync(cancellationToken))
            {
                return new
                {
                    embalajeId = (int?)null,
                    codigo = embalajeCodigo,
                    nombre = embalajeDescripcion ?? "",
                    requerido,
                    disponible = 0m,
                    unidad = "",
                    suficiente = false,
                    semaforo = "NO_EXISTE",
                    bloquear = false,
                    mensaje = "El embalaje no existe en ERP_Embalajes. Revisar catálogo de embalajes."
                };
            }

            var embalajeId = Convert.ToInt32(rd["EmbalajeID"]);
            var codigo = rd["Codigo"] as string ?? embalajeCodigo;
            var nombre = rd["Nombre"] as string ?? embalajeDescripcion ?? "";
            var unidad = rd["Unidad"] as string ?? "";
            var disponible = rd["Saldo"] == DBNull.Value ? 0m : Convert.ToDecimal(rd["Saldo"]);
            var semaforo = rd["Semaforo"] as string ?? "";

            var suficiente = disponible >= requerido;

            string mensaje;

            if (suficiente)
            {
                mensaje = "Embalaje suficiente para esta OF.";
            }
            else if (semaforo == "SIN_CONFIGURAR")
            {
                mensaje = "El embalaje existe, pero no tiene stock configurado. Almacén debe revisar la configuración.";
            }
            else
            {
                mensaje = "Embalaje insuficiente. Almacén debe revisar existencias antes del surtido.";
            }

            return new
            {
                embalajeId,
                codigo,
                nombre,
                requerido,
                disponible,
                unidad,
                suficiente,
                semaforo,
                bloquear = false,
                mensaje
            };
        }

        private async Task CalcularCostosDetalleAsync(
    PlaneacionOFDetalleCrearVm d,
    SqlConnection cn,
    SqlTransaction tx)
        {
            d.CostoMPUnitario = null;
            d.CostoMPTotal = null;
            d.MonedaCostoMP = null;
            d.UnidadCostoMP = null;

            d.CostoEmbalajeUnitario = null;
            d.CostoEmbalajeTotal = null;
            d.MonedaCostoEmbalaje = null;
            d.UnidadCostoEmbalaje = null;

            d.CostoTotalRenglon = null;
            d.PrecioVentaUnitario = null;
            d.VentaTotalRenglon = null;
            d.UtilidadEstimadaRenglon = null;

            if (d.MaterialID.HasValue)
            {
                const string sqlMaterial = @"
SELECT TOP 1
    CostoUnitario,
    MonedaCosto,
    UnidadCosto
FROM dbo.ERP_Materiales
WHERE MaterialID = @MaterialID
  AND Activo = 1;";

                await using var cmd = new SqlCommand(sqlMaterial, cn, tx);
                cmd.Parameters.Add("@MaterialID", SqlDbType.Int).Value = d.MaterialID.Value;

                await using var rd = await cmd.ExecuteReaderAsync();

                if (await rd.ReadAsync())
                {
                    if (rd["CostoUnitario"] != DBNull.Value)
                        d.CostoMPUnitario = Convert.ToDecimal(rd["CostoUnitario"]);

                    d.MonedaCostoMP = rd["MonedaCosto"] as string;
                    d.UnidadCostoMP = rd["UnidadCosto"] as string;
                }
            }

            if (d.CostoMPUnitario.HasValue && d.CantidadMpKg.HasValue)
            {
                d.CostoMPTotal = Math.Round(d.CantidadMpKg.Value * d.CostoMPUnitario.Value, 4);
            }

            if (!string.IsNullOrWhiteSpace(d.EmbalajeCodigo))
            {
                const string sqlEmbalaje = @"
SELECT TOP 1
    CostoUnitario,
    MonedaCosto,
    UnidadCosto
FROM dbo.ERP_Embalajes
WHERE Codigo = @Codigo
  AND Activo = 1;";

                await using var cmd = new SqlCommand(sqlEmbalaje, cn, tx);
                cmd.Parameters.Add("@Codigo", SqlDbType.NVarChar, 100).Value = d.EmbalajeCodigo;

                await using var rd = await cmd.ExecuteReaderAsync();

                if (await rd.ReadAsync())
                {
                    if (rd["CostoUnitario"] != DBNull.Value)
                        d.CostoEmbalajeUnitario = Convert.ToDecimal(rd["CostoUnitario"]);

                    d.MonedaCostoEmbalaje = rd["MonedaCosto"] as string;
                    d.UnidadCostoEmbalaje = rd["UnidadCosto"] as string;
                }
            }

            if (d.CostoEmbalajeUnitario.HasValue && d.CantidadEmbalajes.HasValue)
            {
                d.CostoEmbalajeTotal = Math.Round(d.CantidadEmbalajes.Value * d.CostoEmbalajeUnitario.Value, 4);
            }

            if (d.CostoMPTotal.HasValue || d.CostoEmbalajeTotal.HasValue)
            {
                d.CostoTotalRenglon = Math.Round(
                    (d.CostoMPTotal ?? 0m) + (d.CostoEmbalajeTotal ?? 0m),
                    4
                );
            }

            if (d.ParteID.HasValue)
            {
                const string sqlPrecioVenta = @"
SELECT TOP 1
    PrecioVentaUnitario
FROM dbo.ERP_Partes
WHERE ParteID = @ParteID
  AND Activo = 1;";

                await using var cmd = new SqlCommand(sqlPrecioVenta, cn, tx);
                cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = d.ParteID.Value;

                var result = await cmd.ExecuteScalarAsync();

                if (result != null && result != DBNull.Value)
                {
                    d.PrecioVentaUnitario = Convert.ToDecimal(result);
                }
            }

            if (d.PrecioVentaUnitario.HasValue && d.CantidadPiezas > 0)
            {
                d.VentaTotalRenglon = Math.Round(d.PrecioVentaUnitario.Value * d.CantidadPiezas, 4);
            }

            if (d.VentaTotalRenglon.HasValue && d.CostoTotalRenglon.HasValue)
            {
                d.UtilidadEstimadaRenglon = Math.Round(
                    d.VentaTotalRenglon.Value - d.CostoTotalRenglon.Value,
                    4
                );
            }
        }


        private async Task ActualizarTotalesCostosOFAsync(
    int solicitudId,
    List<PlaneacionOFDetalleCrearVm> detalles,
    SqlConnection cn,
    SqlTransaction tx)
        {
            decimal? Sumar(Func<PlaneacionOFDetalleCrearVm, decimal?> selector)
            {
                var valores = detalles
                    .Select(selector)
                    .Where(x => x.HasValue)
                    .Select(x => x!.Value)
                    .ToList();

                if (!valores.Any())
                    return null;

                return Math.Round(valores.Sum(), 4);
            }

            var costoMpTotal = Sumar(x => x.CostoMPTotal);
            var costoEmbalajeTotal = Sumar(x => x.CostoEmbalajeTotal);
            var costoTotalOF = Sumar(x => x.CostoTotalRenglon);
            var ventaTotalOF = Sumar(x => x.VentaTotalRenglon);
            var utilidadEstimadaOF = Sumar(x => x.UtilidadEstimadaRenglon);

            var monedaCosto =
                detalles.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.MonedaCostoMP))?.MonedaCostoMP
                ?? detalles.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.MonedaCostoEmbalaje))?.MonedaCostoEmbalaje;

            const string sql = @"
UPDATE dbo.SolicitudesProduccion
SET
    CostoMPTotal = @CostoMPTotal,
    CostoEmbalajeTotal = @CostoEmbalajeTotal,
    CostoTotalOF = @CostoTotalOF,
    VentaTotalOF = @VentaTotalOF,
    UtilidadEstimadaOF = @UtilidadEstimadaOF,
    MonedaCosto = @MonedaCosto,
    FechaModificacion = GETDATE()
WHERE SolicitudProduccionID = @SolicitudProduccionID;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            AddDecimal(cmd, "@CostoMPTotal", costoMpTotal, 18, 4);
            AddDecimal(cmd, "@CostoEmbalajeTotal", costoEmbalajeTotal, 18, 4);
            AddDecimal(cmd, "@CostoTotalOF", costoTotalOF, 18, 4);
            AddDecimal(cmd, "@VentaTotalOF", ventaTotalOF, 18, 4);
            AddDecimal(cmd, "@UtilidadEstimadaOF", utilidadEstimadaOF, 18, 4);

            cmd.Parameters.Add("@MonedaCosto", SqlDbType.NVarChar, 20).Value =
                (object?)monedaCosto ?? DBNull.Value;

            cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = solicitudId;

            await cmd.ExecuteNonQueryAsync();
        }

        private async Task<int> InsertarEncabezadoAsync(
    PlaneacionOFCrearVm vm,
    string? clienteNombre,
    int usuarioId,
    string usuarioNombre,
    SqlConnection cn,
    SqlTransaction tx)
        {
            const string sql = @"
DECLARE @Ids TABLE
(
    SolicitudProduccionID INT NOT NULL
);

INSERT INTO dbo.SolicitudesProduccion
(
    FolioSolicitud,
    NumeroOFRecibida,
    FechaSolicitud,
    FechaRequerida,
    FechaInicioPlaneada,
    FechaFinPlaneada,
    ClienteID,
    ClienteNombre,
    OrigenSolicitud,
    Prioridad,
    TipoOF,
    MotivoTipoOF,
    EstatusID,
    NotasGenerales,
    ResponsablePlaneacionUsuarioID,
    ResponsablePlaneacionNombre,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
OUTPUT INSERTED.SolicitudProduccionID
INTO @Ids(SolicitudProduccionID)
VALUES
(
    @FolioSolicitud,
    @NumeroOFRecibida,
    @FechaSolicitud,
    @FechaRequerida,
    @FechaInicioPlaneada,
    @FechaFinPlaneada,
    @ClienteID,
    @ClienteNombre,
    @OrigenSolicitud,
    @Prioridad,
    @TipoOF,
    @MotivoTipoOF,
    @EstatusID,
    @NotasGenerales,
    @ResponsablePlaneacionUsuarioID,
    @ResponsablePlaneacionNombre,
    @UsuarioCreacionID,
    GETDATE(),
    1
);

SELECT TOP (1)
    SolicitudProduccionID
FROM @Ids;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@FolioSolicitud", SqlDbType.NVarChar, 30).Value =
                (object?)vm.FolioSolicitud ?? DBNull.Value;

            cmd.Parameters.Add("@NumeroOFRecibida", SqlDbType.NVarChar, 80).Value =
                (object?)vm.NumeroOFRecibida ?? DBNull.Value;

            cmd.Parameters.Add("@FechaSolicitud", SqlDbType.Date).Value =
                vm.FechaSolicitud.Date;

            cmd.Parameters.Add("@FechaRequerida", SqlDbType.Date).Value =
                (object?)vm.FechaRequerida?.Date ?? DBNull.Value;

            cmd.Parameters.Add("@FechaInicioPlaneada", SqlDbType.DateTime).Value =
                (object?)vm.FechaInicioPlaneada ?? DBNull.Value;

            cmd.Parameters.Add("@FechaFinPlaneada", SqlDbType.DateTime).Value =
                (object?)vm.FechaFinPlaneada ?? DBNull.Value;

            cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value =
                (object?)vm.ClienteID ?? DBNull.Value;

            cmd.Parameters.Add("@ClienteNombre", SqlDbType.NVarChar, 200).Value =
                (object?)clienteNombre ?? DBNull.Value;

            cmd.Parameters.Add("@OrigenSolicitud", SqlDbType.NVarChar, 50).Value =
                (object?)vm.OrigenSolicitud ?? "Dirección";

            cmd.Parameters.Add("@Prioridad", SqlDbType.NVarChar, 30).Value =
                string.IsNullOrWhiteSpace(vm.Prioridad)
                    ? "Normal"
                    : vm.Prioridad.Trim();

            cmd.Parameters.Add("@TipoOF", SqlDbType.NVarChar, 30).Value =
                NormalizarTipoOF(vm.TipoOF);

            cmd.Parameters.Add("@MotivoTipoOF", SqlDbType.NVarChar, 500).Value =
                string.IsNullOrWhiteSpace(vm.MotivoTipoOF)
                    ? DBNull.Value
                    : vm.MotivoTipoOF.Trim();

            cmd.Parameters.Add("@EstatusID", SqlDbType.Int).Value =
                PlaneacionOFEstatus.Capturada;

            cmd.Parameters.Add("@NotasGenerales", SqlDbType.NVarChar).Value =
                (object?)vm.NotasGenerales ?? DBNull.Value;

            cmd.Parameters.Add("@ResponsablePlaneacionUsuarioID", SqlDbType.Int).Value =
                usuarioId;

            cmd.Parameters.Add("@ResponsablePlaneacionNombre", SqlDbType.NVarChar, 200).Value =
                usuarioNombre;

            cmd.Parameters.Add("@UsuarioCreacionID", SqlDbType.Int).Value =
                usuarioId;

            var result = await cmd.ExecuteScalarAsync();

            if (result == null || result == DBNull.Value)
                throw new InvalidOperationException(
                    "No fue posible obtener el ID de la solicitud de producción creada.");

            return Convert.ToInt32(result);
        }

        private async Task<int> InsertarDetalleAsync(
     int solicitudId,
     int renglon,
     PlaneacionOFDetalleCrearVm d,
     SqlConnection cn,
     SqlTransaction tx)
        {
            const string sql = @"
DECLARE @Ids TABLE
(
    SolicitudProduccionDetalleID INT NOT NULL
);

INSERT INTO dbo.SolicitudesProduccionDetalle
(
    SolicitudProduccionID,
    Renglon,
    ParteID,
    MoldeID,
    DesignacionDescripcionSAP,
    ReferenciaSAP,
    CantidadPiezas,
    HorasPlaneadas,
    NumeroMoldeTexto,
    Color,
    Cavidades,
    ObjetivoHora,
    PiezasPorCaja,
    Ciclo,
    TipoSecado,
    HorasSecado,
    PesoBrutoPieza,
    MaterialCodigo,
    MaterialDescripcion,
    MaterialID,
    OrigenSurtido,
    PTDisponibleAlCrear,
    MPDisponibleKgAlCrear,
    AlmacenValidado,
    MensajeAlmacen,
    EmbalajeCodigo,
    EmbalajeDescripcion,
    PiezasPorEmbalaje,
    CantidadEmbalajes,
    CantidadMpKg,
    CostoMPUnitario,
    CostoMPTotal,
    MonedaCostoMP,
    UnidadCostoMP,
    CostoEmbalajeUnitario,
    CostoEmbalajeTotal,
    MonedaCostoEmbalaje,
    UnidadCostoEmbalaje,
    CostoTotalRenglon,
    PrecioVentaUnitario,
    VentaTotalRenglon,
    UtilidadEstimadaRenglon,
    Cambio,
    Arranque,
    Notas,
    EstatusID,
    Activo,
    FechaCreacion
)
OUTPUT INSERTED.SolicitudProduccionDetalleID
INTO @Ids(SolicitudProduccionDetalleID)
VALUES
(
    @SolicitudProduccionID,
    @Renglon,
    @ParteID,
    @MoldeID,
    @DesignacionDescripcionSAP,
    @ReferenciaSAP,
    @CantidadPiezas,
    @HorasPlaneadas,
    @NumeroMoldeTexto,
    @Color,
    @Cavidades,
    @ObjetivoHora,
    @PiezasPorCaja,
    @Ciclo,
    @TipoSecado,
    @HorasSecado,
    @PesoBrutoPieza,
    @MaterialCodigo,
    @MaterialDescripcion,
    @MaterialID,
    @OrigenSurtido,
    @PTDisponibleAlCrear,
    @MPDisponibleKgAlCrear,
    @AlmacenValidado,
    @MensajeAlmacen,
    @EmbalajeCodigo,
    @EmbalajeDescripcion,
    @PiezasPorEmbalaje,
    @CantidadEmbalajes,
    @CantidadMpKg,
    @CostoMPUnitario,
    @CostoMPTotal,
    @MonedaCostoMP,
    @UnidadCostoMP,
    @CostoEmbalajeUnitario,
    @CostoEmbalajeTotal,
    @MonedaCostoEmbalaje,
    @UnidadCostoEmbalaje,
    @CostoTotalRenglon,
    @PrecioVentaUnitario,
    @VentaTotalRenglon,
    @UtilidadEstimadaRenglon,
    @Cambio,
    @Arranque,
    @Notas,
    1,
    1,
    GETDATE()
);

SELECT TOP (1)
    SolicitudProduccionDetalleID
FROM @Ids;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value =
                solicitudId;

            cmd.Parameters.Add("@Renglon", SqlDbType.Int).Value =
                renglon;

            cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value =
                (object?)d.ParteID ?? DBNull.Value;

            cmd.Parameters.Add("@MoldeID", SqlDbType.Int).Value =
                (object?)d.MoldeID ?? DBNull.Value;

            cmd.Parameters.Add("@DesignacionDescripcionSAP", SqlDbType.NVarChar, 300).Value =
                (object?)d.DesignacionDescripcionSAP ?? DBNull.Value;

            cmd.Parameters.Add("@ReferenciaSAP", SqlDbType.NVarChar, 150).Value =
                (object?)d.ReferenciaSAP ?? DBNull.Value;

            cmd.Parameters.Add("@CantidadPiezas", SqlDbType.Int).Value =
                d.CantidadPiezas;

            AddDecimal(cmd, "@HorasPlaneadas", d.HorasPlaneadas, 10, 2);

            cmd.Parameters.Add("@NumeroMoldeTexto", SqlDbType.NVarChar, 100).Value =
                (object?)d.NumeroMoldeTexto ?? DBNull.Value;

            cmd.Parameters.Add("@Color", SqlDbType.NVarChar, 80).Value =
                (object?)d.Color ?? DBNull.Value;

            cmd.Parameters.Add("@Cavidades", SqlDbType.Int).Value =
                (object?)d.Cavidades ?? DBNull.Value;

            cmd.Parameters.Add("@ObjetivoHora", SqlDbType.Int).Value =
                (object?)d.ObjetivoHora ?? DBNull.Value;

            cmd.Parameters.Add("@PiezasPorCaja", SqlDbType.Int).Value =
                (object?)d.PiezasPorCaja ?? DBNull.Value;

            cmd.Parameters.Add("@Ciclo", SqlDbType.NVarChar, 80).Value =
                (object?)d.Ciclo ?? DBNull.Value;

            cmd.Parameters.Add("@TipoSecado", SqlDbType.NVarChar, 100).Value =
                (object?)d.TipoSecado ?? DBNull.Value;

            AddDecimal(cmd, "@HorasSecado", d.HorasSecado, 10, 2);
            AddDecimal(cmd, "@PesoBrutoPieza", d.PesoBrutoPieza, 18, 6);

            cmd.Parameters.Add("@MaterialCodigo", SqlDbType.NVarChar, 100).Value =
                (object?)d.MaterialCodigo ?? DBNull.Value;

            cmd.Parameters.Add("@MaterialDescripcion", SqlDbType.NVarChar, 250).Value =
                (object?)d.MaterialDescripcion ?? DBNull.Value;

            cmd.Parameters.Add("@MaterialID", SqlDbType.Int).Value =
                (object?)d.MaterialID ?? DBNull.Value;

            cmd.Parameters.Add("@OrigenSurtido", SqlDbType.NVarChar, 30).Value =
                (object?)d.OrigenSurtido ?? DBNull.Value;

            cmd.Parameters.Add("@PTDisponibleAlCrear", SqlDbType.Int).Value =
                (object?)d.PTDisponibleAlCrear ?? DBNull.Value;

            AddDecimal(cmd, "@MPDisponibleKgAlCrear", d.MPDisponibleKgAlCrear, 18, 4);

            cmd.Parameters.Add("@AlmacenValidado", SqlDbType.Bit).Value =
                d.AlmacenValidado;

            cmd.Parameters.Add("@MensajeAlmacen", SqlDbType.NVarChar, 500).Value =
                (object?)d.MensajeAlmacen ?? DBNull.Value;

            cmd.Parameters.Add("@EmbalajeCodigo", SqlDbType.NVarChar, 100).Value =
                (object?)d.EmbalajeCodigo ?? DBNull.Value;

            cmd.Parameters.Add("@EmbalajeDescripcion", SqlDbType.NVarChar, 250).Value =
                (object?)d.EmbalajeDescripcion ?? DBNull.Value;

            AddDecimal(cmd, "@PiezasPorEmbalaje", d.PiezasPorEmbalaje, 18, 4);
            AddDecimal(cmd, "@CantidadEmbalajes", d.CantidadEmbalajes, 18, 4);
            AddDecimal(cmd, "@CantidadMpKg", d.CantidadMpKg, 18, 4);

            AddDecimal(cmd, "@CostoMPUnitario", d.CostoMPUnitario, 18, 6);
            AddDecimal(cmd, "@CostoMPTotal", d.CostoMPTotal, 18, 4);

            cmd.Parameters.Add("@MonedaCostoMP", SqlDbType.NVarChar, 20).Value =
                (object?)d.MonedaCostoMP ?? DBNull.Value;

            cmd.Parameters.Add("@UnidadCostoMP", SqlDbType.NVarChar, 30).Value =
                (object?)d.UnidadCostoMP ?? DBNull.Value;

            AddDecimal(
                cmd,
                "@CostoEmbalajeUnitario",
                d.CostoEmbalajeUnitario,
                18,
                6);

            AddDecimal(
                cmd,
                "@CostoEmbalajeTotal",
                d.CostoEmbalajeTotal,
                18,
                4);

            cmd.Parameters.Add("@MonedaCostoEmbalaje", SqlDbType.NVarChar, 20).Value =
                (object?)d.MonedaCostoEmbalaje ?? DBNull.Value;

            cmd.Parameters.Add("@UnidadCostoEmbalaje", SqlDbType.NVarChar, 30).Value =
                (object?)d.UnidadCostoEmbalaje ?? DBNull.Value;

            AddDecimal(cmd, "@CostoTotalRenglon", d.CostoTotalRenglon, 18, 4);
            AddDecimal(cmd, "@PrecioVentaUnitario", d.PrecioVentaUnitario, 18, 6);
            AddDecimal(cmd, "@VentaTotalRenglon", d.VentaTotalRenglon, 18, 4);
            AddDecimal(cmd, "@UtilidadEstimadaRenglon", d.UtilidadEstimadaRenglon, 18, 4);

            cmd.Parameters.Add("@Cambio", SqlDbType.Time).Value =
                (object?)d.Cambio ?? DBNull.Value;

            cmd.Parameters.Add("@Arranque", SqlDbType.Time).Value =
                (object?)d.Arranque ?? DBNull.Value;

            cmd.Parameters.Add("@Notas", SqlDbType.NVarChar, 500).Value =
                (object?)d.Notas ?? DBNull.Value;

            var result = await cmd.ExecuteScalarAsync();

            if (result == null || result == DBNull.Value)
                throw new InvalidOperationException(
                    "No fue posible obtener el ID del detalle de la solicitud de producción.");

            return Convert.ToInt32(result);
        }

        private async Task InsertarAsignacionMaquinaAsync(
            int detalleId,
            PlaneacionOFAsignacionMaquinaCrearVm a,
            int? moldeDetalleId,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
INSERT INTO dbo.SolicitudesProduccionAsignacionMaquina
(
    SolicitudProduccionDetalleID,
    MaquinaID,
    MoldeID,
    CantidadAsignada,
    HorasEstimadas,
    Secuencia,
    CondicionProduccion,
    FechaProgramadaTentativa,
    HoraInicioTentativa,
    HoraFinTentativa,
    EstatusID,
    Observaciones,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
VALUES
(
    @SolicitudProduccionDetalleID,
    @MaquinaID,
    @MoldeID,
    @CantidadAsignada,
    @HorasEstimadas,
    @Secuencia,
    @CondicionProduccion,
    @FechaProgramadaTentativa,
    @HoraInicioTentativa,
    @HoraFinTentativa,
    1,
    @Observaciones,
    @UsuarioCreacionID,
    GETDATE(),
    1
);";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value = detalleId;
            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = a.MaquinaID;
            cmd.Parameters.Add("@MoldeID", SqlDbType.Int).Value = (object?)a.MoldeID ?? (object?)moldeDetalleId ?? DBNull.Value;
            cmd.Parameters.Add("@CantidadAsignada", SqlDbType.Int).Value = a.CantidadAsignada;
            AddDecimal(cmd, "@HorasEstimadas", a.HorasEstimadas, 10, 2);
            cmd.Parameters.Add("@Secuencia", SqlDbType.Int).Value = a.Secuencia <= 0 ? 1 : a.Secuencia;
            cmd.Parameters.Add("@CondicionProduccion", SqlDbType.NVarChar, 10).Value = (object?)a.CondicionProduccion ?? DBNull.Value;
            cmd.Parameters.Add("@FechaProgramadaTentativa", SqlDbType.Date).Value = (object?)a.FechaProgramadaTentativa?.Date ?? DBNull.Value;
            cmd.Parameters.Add("@HoraInicioTentativa", SqlDbType.Time).Value = (object?)a.HoraInicioTentativa ?? DBNull.Value;
            cmd.Parameters.Add("@HoraFinTentativa", SqlDbType.Time).Value = (object?)a.HoraFinTentativa ?? DBNull.Value;
            cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 500).Value = (object?)a.Observaciones ?? DBNull.Value;
            cmd.Parameters.Add("@UsuarioCreacionID", SqlDbType.Int).Value = usuarioId;

            await cmd.ExecuteNonQueryAsync();
        }

        private async Task InsertarHistorialAsync(
            int solicitudId,
            int? estatusAnterior,
            int estatusNuevo,
            string movimiento,
            string? comentario,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
INSERT INTO dbo.SolicitudProduccionHistorial
(
    SolicitudProduccionID,
    EstatusAnteriorID,
    EstatusNuevoID,
    Movimiento,
    Comentario,
    UsuarioID,
    FechaMovimiento
)
VALUES
(
    @SolicitudProduccionID,
    @EstatusAnteriorID,
    @EstatusNuevoID,
    @Movimiento,
    @Comentario,
    @UsuarioID,
    GETDATE()
);";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = solicitudId;
            cmd.Parameters.Add("@EstatusAnteriorID", SqlDbType.Int).Value = (object?)estatusAnterior ?? DBNull.Value;
            cmd.Parameters.Add("@EstatusNuevoID", SqlDbType.Int).Value = estatusNuevo;
            cmd.Parameters.Add("@Movimiento", SqlDbType.NVarChar, 100).Value = movimiento;
            cmd.Parameters.Add("@Comentario", SqlDbType.NVarChar, 500).Value = (object?)comentario ?? DBNull.Value;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;

            await cmd.ExecuteNonQueryAsync();
        }

        // consultas
        private async Task CargarCatalogosAsync(PlaneacionOFCrearVm vm)
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            vm.Clientes = await CargarSelectAsync(
                cn,
                "SELECT ClienteID AS Id, Nombre AS Texto FROM dbo.ERP_Clientes WHERE Activo = 1 ORDER BY Nombre;"
            );

            vm.Partes = await CargarSelectAsync(
                cn,
                "SELECT ParteID AS Id, NumeroParte + ' | ' + ISNULL(NULLIF(ReferenciaSAP, ''), NumeroParte) + ' | ' + ISNULL(NULLIF(Designacion, ''), Descripcion) AS Texto FROM dbo.ERP_Partes WHERE Activo = 1 ORDER BY NumeroParte;"
            );

            vm.Moldes = await CargarSelectAsync(
                cn,
                "SELECT MoldeID AS Id, CodigoMolde + ' - ' + ISNULL(NombreMolde, '') AS Texto FROM dbo.ERP_Moldes WHERE Activo = 1 ORDER BY CodigoMolde;"
            );

            vm.Maquinas = await CargarSelectAsync(
                cn,
                "SELECT MaquinaID AS Id, Codigo + ' - ' + Nombre AS Texto FROM dbo.ERP_Maquinas WHERE Activo = 1 ORDER BY Codigo;"
            );
        }

        private static async Task<List<SelectListItem>> CargarSelectAsync(SqlConnection cn, string sql)
        {
            var lista = new List<SelectListItem>();

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

        private async Task<string?> ObtenerClienteNombreAsync(int clienteId, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = "SELECT Nombre FROM dbo.ERP_Clientes WHERE ClienteID = @ClienteID;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = clienteId;

            var result = await cmd.ExecuteScalarAsync();
            return result == DBNull.Value ? null : result as string;
        }

        private async Task<int?> ObtenerEstatusActualAsync(int solicitudId, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
SELECT EstatusID
FROM dbo.SolicitudesProduccion
WHERE SolicitudProduccionID = @SolicitudProduccionID
  AND Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = solicitudId;

            var result = await cmd.ExecuteScalarAsync();

            if (result == null || result == DBNull.Value)
                return null;

            return Convert.ToInt32(result);
        }

        private async Task CompletarDetalleDesdeParteAsync(
     PlaneacionOFDetalleCrearVm d,
     SqlConnection cn,
     SqlTransaction tx)
        {
            if (!d.ParteID.HasValue)
                return;

            const string sql = @"
SELECT
    p.NumeroParte,
    p.ReferenciaSAP,
    p.Descripcion,
    p.Designacion,

    t.Color,
    t.Cavidades,
    t.ObjetivoHora,
    t.PiezasPorCaja,
    t.MoldePrincipalID,

    t.MaterialID,
    t.MaterialCodigo,
    t.MaterialDescripcion,

    t.Ciclo,
    t.TipoSecado,
    t.HorasSecado,
    t.PesoBrutoPieza,

    t.EmbalajeCodigo,
    t.EmbalajeDescripcion,
    t.PiezasPorEmbalaje
FROM dbo.ERP_Partes p
LEFT JOIN dbo.ERP_ParteDatosTecnicos t
    ON t.ParteID = p.ParteID
   AND t.Activo = 1
WHERE p.ParteID = @ParteID
  AND p.Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = d.ParteID.Value;

            await using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
                return;

            var numeroParte = rd["NumeroParte"] as string ?? "";
            var referencia = rd["ReferenciaSAP"] as string;
            var descripcion = rd["Descripcion"] as string ?? "";
            var designacion = rd["Designacion"] as string;

            if (string.IsNullOrWhiteSpace(d.ReferenciaSAP))
                d.ReferenciaSAP = string.IsNullOrWhiteSpace(referencia) ? numeroParte : referencia;

            if (string.IsNullOrWhiteSpace(d.DesignacionDescripcionSAP))
                d.DesignacionDescripcionSAP = !string.IsNullOrWhiteSpace(designacion) ? designacion : descripcion;

            if (string.IsNullOrWhiteSpace(d.Color) && rd["Color"] != DBNull.Value)
                d.Color = rd["Color"].ToString();

            if (!d.Cavidades.HasValue && rd["Cavidades"] != DBNull.Value)
                d.Cavidades = Convert.ToInt32(rd["Cavidades"]);

            if (!d.ObjetivoHora.HasValue && rd["ObjetivoHora"] != DBNull.Value)
                d.ObjetivoHora = Convert.ToInt32(rd["ObjetivoHora"]);

            if (!d.PiezasPorCaja.HasValue && rd["PiezasPorCaja"] != DBNull.Value)
                d.PiezasPorCaja = Convert.ToInt32(rd["PiezasPorCaja"]);

            if (!d.MoldeID.HasValue && rd["MoldePrincipalID"] != DBNull.Value)
                d.MoldeID = Convert.ToInt32(rd["MoldePrincipalID"]);

            if (string.IsNullOrWhiteSpace(d.Ciclo) && rd["Ciclo"] != DBNull.Value)
                d.Ciclo = rd["Ciclo"].ToString();

            if (string.IsNullOrWhiteSpace(d.TipoSecado) && rd["TipoSecado"] != DBNull.Value)
                d.TipoSecado = rd["TipoSecado"].ToString();

            if (!d.HorasSecado.HasValue && rd["HorasSecado"] != DBNull.Value)
                d.HorasSecado = Convert.ToDecimal(rd["HorasSecado"]);

            if (!d.PesoBrutoPieza.HasValue && rd["PesoBrutoPieza"] != DBNull.Value)
                d.PesoBrutoPieza = Convert.ToDecimal(rd["PesoBrutoPieza"]);

            if (!d.MaterialID.HasValue && rd["MaterialID"] != DBNull.Value)
                d.MaterialID = Convert.ToInt32(rd["MaterialID"]);

            if (string.IsNullOrWhiteSpace(d.MaterialCodigo) && rd["MaterialCodigo"] != DBNull.Value)
                d.MaterialCodigo = rd["MaterialCodigo"].ToString();

            if (string.IsNullOrWhiteSpace(d.MaterialDescripcion) && rd["MaterialDescripcion"] != DBNull.Value)
                d.MaterialDescripcion = rd["MaterialDescripcion"].ToString();

            if (string.IsNullOrWhiteSpace(d.EmbalajeCodigo) && rd["EmbalajeCodigo"] != DBNull.Value)
                d.EmbalajeCodigo = rd["EmbalajeCodigo"].ToString();

            if (string.IsNullOrWhiteSpace(d.EmbalajeDescripcion) && rd["EmbalajeDescripcion"] != DBNull.Value)
                d.EmbalajeDescripcion = rd["EmbalajeDescripcion"].ToString();

            if (!d.PiezasPorEmbalaje.HasValue && rd["PiezasPorEmbalaje"] != DBNull.Value)
                d.PiezasPorEmbalaje = Convert.ToDecimal(rd["PiezasPorEmbalaje"]);
        }

        private static void CalcularDatosTecnicos(PlaneacionOFDetalleCrearVm d)
        {
            // Horas planeadas = Cantidad de piezas / Objetivo por hora
            if (d.CantidadPiezas > 0 && d.ObjetivoHora.HasValue && d.ObjetivoHora.Value > 0)
            {
                d.HorasPlaneadas = Math.Round(d.CantidadPiezas / (decimal)d.ObjetivoHora.Value, 2);
            }

            // Cantidad de embalajes = Cantidad de piezas / Piezas por embalaje
            if (d.CantidadPiezas > 0 && d.PiezasPorEmbalaje.HasValue && d.PiezasPorEmbalaje.Value > 0)
            {
                d.CantidadEmbalajes = Math.Ceiling(d.CantidadPiezas / d.PiezasPorEmbalaje.Value);
            }

            // Cantidad MP kg = Peso bruto pieza * Cantidad de piezas
            if (d.CantidadPiezas > 0 && d.PesoBrutoPieza.HasValue && d.PesoBrutoPieza.Value > 0)
            {
                d.CantidadMpKg = Math.Round(d.CantidadPiezas * d.PesoBrutoPieza.Value, 4);
            }
        }

        private async Task<List<PlaneacionOFDetalleRenglonVm>> ObtenerDetallesAsync(int solicitudId, SqlConnection cn)
        {
            var lista = new List<PlaneacionOFDetalleRenglonVm>();

            const string sql = @"
SELECT
    d.SolicitudProduccionDetalleID,
    d.Renglon,
    d.ReferenciaSAP,
    d.DesignacionDescripcionSAP,
    d.CantidadPiezas,
    d.HorasPlaneadas,
    ISNULL(m.CodigoMolde, d.NumeroMoldeTexto) AS Molde,
    d.Color,
    d.Cavidades,
    d.ObjetivoHora,
    d.PiezasPorCaja,
    d.Ciclo,
    d.TipoSecado,
    d.HorasSecado,
    d.PesoBrutoPieza,
    d.MaterialCodigo,
    d.MaterialDescripcion,
    d.EmbalajeCodigo,
    d.EmbalajeDescripcion,
    d.PiezasPorEmbalaje,
    d.CantidadEmbalajes,
    d.CantidadMpKg,
    d.Cambio,
    d.Arranque,
    d.Notas
FROM dbo.SolicitudesProduccionDetalle d
LEFT JOIN dbo.ERP_Moldes m
    ON m.MoldeID = d.MoldeID
WHERE d.SolicitudProduccionID = @SolicitudProduccionID
  AND d.Activo = 1
ORDER BY d.Renglon;";

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = solicitudId;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(new PlaneacionOFDetalleRenglonVm
                {
                    SolicitudProduccionDetalleID = Convert.ToInt32(rd["SolicitudProduccionDetalleID"]),
                    Renglon = Convert.ToInt32(rd["Renglon"]),
                    ReferenciaSAP = rd["ReferenciaSAP"] as string ?? "",
                    DesignacionDescripcionSAP = rd["DesignacionDescripcionSAP"] as string ?? "",
                    CantidadPiezas = Convert.ToInt32(rd["CantidadPiezas"]),
                    HorasPlaneadas = rd["HorasPlaneadas"] == DBNull.Value ? null : Convert.ToDecimal(rd["HorasPlaneadas"]),
                    Molde = rd["Molde"] as string,
                    Color = rd["Color"] as string,
                    Cavidades = rd["Cavidades"] == DBNull.Value ? null : Convert.ToInt32(rd["Cavidades"]),
                    ObjetivoHora = rd["ObjetivoHora"] == DBNull.Value ? null : Convert.ToInt32(rd["ObjetivoHora"]),
                    PiezasPorCaja = rd["PiezasPorCaja"] == DBNull.Value ? null : Convert.ToInt32(rd["PiezasPorCaja"]),
                    Ciclo = rd["Ciclo"] as string,
                    TipoSecado = rd["TipoSecado"] as string,
                    HorasSecado = rd["HorasSecado"] == DBNull.Value ? null : Convert.ToDecimal(rd["HorasSecado"]),
                    PesoBrutoPieza = rd["PesoBrutoPieza"] == DBNull.Value ? null : Convert.ToDecimal(rd["PesoBrutoPieza"]),
                    MaterialCodigo = rd["MaterialCodigo"] as string,
                    MaterialDescripcion = rd["MaterialDescripcion"] as string,
                    EmbalajeCodigo = rd["EmbalajeCodigo"] as string,
                    EmbalajeDescripcion = rd["EmbalajeDescripcion"] as string,
                    PiezasPorEmbalaje = rd["PiezasPorEmbalaje"] == DBNull.Value ? null : Convert.ToDecimal(rd["PiezasPorEmbalaje"]),
                    CantidadEmbalajes = rd["CantidadEmbalajes"] == DBNull.Value ? null : Convert.ToDecimal(rd["CantidadEmbalajes"]),
                    CantidadMpKg = rd["CantidadMpKg"] == DBNull.Value ? null : Convert.ToDecimal(rd["CantidadMpKg"]),
                    Cambio = rd["Cambio"] == DBNull.Value ? null : (TimeSpan)rd["Cambio"],
                    Arranque = rd["Arranque"] == DBNull.Value ? null : (TimeSpan)rd["Arranque"],
                    Notas = rd["Notas"] as string
                });
            }

            return lista;
        }

        private async Task<List<PlaneacionOFAsignacionMaquinaVm>> ObtenerAsignacionesAsync(int detalleId, SqlConnection cn)
        {
            var lista = new List<PlaneacionOFAsignacionMaquinaVm>();

            const string sql = @"
SELECT
    a.AsignacionMaquinaID,
    maq.Codigo + ' - ' + maq.Nombre AS Maquina,
    mol.CodigoMolde AS Molde,
    a.CantidadAsignada,
    a.HorasEstimadas,
    a.Secuencia,
    a.CondicionProduccion,
    a.FechaProgramadaTentativa,
    a.HoraInicioTentativa,
    a.HoraFinTentativa,
    a.Observaciones
FROM dbo.SolicitudesProduccionAsignacionMaquina a
INNER JOIN dbo.ERP_Maquinas maq
    ON maq.MaquinaID = a.MaquinaID
LEFT JOIN dbo.ERP_Moldes mol
    ON mol.MoldeID = a.MoldeID
WHERE a.SolicitudProduccionDetalleID = @SolicitudProduccionDetalleID
  AND a.Activo = 1
ORDER BY a.Secuencia;";

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value = detalleId;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(new PlaneacionOFAsignacionMaquinaVm
                {
                    AsignacionMaquinaID = Convert.ToInt32(rd["AsignacionMaquinaID"]),
                    Maquina = rd["Maquina"] as string ?? "",
                    Molde = rd["Molde"] as string,
                    CantidadAsignada = Convert.ToInt32(rd["CantidadAsignada"]),
                    HorasEstimadas = rd["HorasEstimadas"] == DBNull.Value ? null : Convert.ToDecimal(rd["HorasEstimadas"]),
                    Secuencia = Convert.ToInt32(rd["Secuencia"]),
                    CondicionProduccion = rd["CondicionProduccion"] as string,
                    FechaProgramadaTentativa = rd["FechaProgramadaTentativa"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaProgramadaTentativa"]),
                    HoraInicioTentativa = rd["HoraInicioTentativa"] == DBNull.Value ? null : (TimeSpan)rd["HoraInicioTentativa"],
                    HoraFinTentativa = rd["HoraFinTentativa"] == DBNull.Value ? null : (TimeSpan)rd["HoraFinTentativa"],
                    Observaciones = rd["Observaciones"] as string
                });
            }

            return lista;
        }

        private async Task<List<PlaneacionOFHistorialVm>> ObtenerHistorialAsync(int solicitudId, SqlConnection cn)
        {
            var lista = new List<PlaneacionOFHistorialVm>();

            const string sql = @"
SELECT
    h.FechaMovimiento,
    h.Movimiento,
    h.Comentario,
    h.EstatusAnteriorID,
    h.EstatusNuevoID,
    CAST(h.UsuarioID AS NVARCHAR(50)) AS Usuario
FROM dbo.SolicitudProduccionHistorial h
WHERE h.SolicitudProduccionID = @SolicitudProduccionID
ORDER BY h.FechaMovimiento DESC;";

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = solicitudId;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(new PlaneacionOFHistorialVm
                {
                    FechaMovimiento = Convert.ToDateTime(rd["FechaMovimiento"]),
                    Movimiento = rd["Movimiento"] as string ?? "",
                    Comentario = rd["Comentario"] as string,
                    EstatusAnteriorID = rd["EstatusAnteriorID"] == DBNull.Value ? null : Convert.ToInt32(rd["EstatusAnteriorID"]),
                    EstatusNuevoID = Convert.ToInt32(rd["EstatusNuevoID"]),
                    Usuario = rd["Usuario"] as string ?? ""
                });
            }

            return lista;
        }

        [HttpGet]
        public async Task<IActionResult> ObtenerPartesPorCliente(int clienteId)
        {
            if (clienteId <= 0)
            {
                return Json(new { ok = false, mensaje = "Cliente inválido." });
            }

            var partes = new List<object>();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            const string sql = @"
SELECT
    ParteID,
    NumeroParte,
    ISNULL(NULLIF(ReferenciaSAP, ''), NumeroParte) AS ReferenciaSAP,
    ISNULL(NULLIF(Designacion, ''), Descripcion) AS Descripcion
FROM dbo.ERP_Partes
WHERE Activo = 1
  AND ClienteID = @ClienteID
ORDER BY NumeroParte;";

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = clienteId;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                partes.Add(new
                {
                    value = rd["ParteID"].ToString(),
                    text = $"{rd["NumeroParte"]} | {rd["ReferenciaSAP"]} | {rd["Descripcion"]}"
                });
            }

            return Json(new
            {
                ok = true,
                partes
            });
        }

        //METODO GET PARA EDITAR

        [HttpGet]
        public async Task<IActionResult> Editar(int id)
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var vm = await ObtenerOFParaEditarAsync(id, cn);

            if (vm == null)
            {
                return NotFound();
            }

            var permisoEdicion = await ObtenerPermisoEdicionOFAsync(
                id,
                vm.FolioSolicitud,
                vm.NumeroOFRecibida,
                vm.EstatusID,
                cn
            );

            if (!permisoEdicion.PuedeEditar)
            {
                TempData["Error"] = permisoEdicion.Motivo;
                return RedirectToAction(nameof(Detalle), new { id });
            }

            vm.SolicitudProduccionID = id;
            vm.EsEdicion = true;

            await CargarCatalogosAsync(vm);

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Editar(PlaneacionOFCrearVm vm)
        {
            var usuarioId = ObtenerUsuarioID();
            var usuarioNombre = ObtenerUsuarioNombre();

            if (!vm.SolicitudProduccionID.HasValue || vm.SolicitudProduccionID.Value <= 0)
            {
                return BadRequest("La OF no es válida.");
            }

            if (usuarioId <= 0)
            {
                ModelState.AddModelError("", "No se pudo identificar el usuario de sesión.");
            }

            vm.EsEdicion = true;

            vm.TipoOF = NormalizarTipoOF(vm.TipoOF);
            vm.MotivoTipoOF = vm.MotivoTipoOF?.Trim();

            ValidarTipoOF(vm.TipoOF, vm.MotivoTipoOF, nameof(vm.MotivoTipoOF));

            vm.Detalles = vm.Detalles
                .Where(d =>
                    d.CantidadPiezas > 0 &&
                    (
                        d.ParteID.HasValue ||
                        !string.IsNullOrWhiteSpace(d.ReferenciaSAP) ||
                        !string.IsNullOrWhiteSpace(d.DesignacionDescripcionSAP)
                    ))
                .ToList();

            if (!vm.Detalles.Any())
            {
                ModelState.AddModelError("", "Debes capturar al menos un renglón de producción.");
            }

            if (!vm.ClienteID.HasValue && string.IsNullOrWhiteSpace(vm.ClienteNombre))
            {
                ModelState.AddModelError("", "Selecciona o captura el cliente.");
            }

            foreach (var detalle in vm.Detalles)
            {
                detalle.AsignacionesMaquina = detalle.AsignacionesMaquina
                    .Where(a => a.MaquinaID > 0 && a.CantidadAsignada > 0)
                    .ToList();

                var totalAsignado = detalle.AsignacionesMaquina.Sum(a => a.CantidadAsignada);

                if (totalAsignado > 0 && totalAsignado != detalle.CantidadPiezas)
                {
                    ModelState.AddModelError(
                        "",
                        $"En el renglón {detalle.Renglon}, la cantidad asignada a máquinas ({totalAsignado}) debe coincidir con la cantidad de piezas ({detalle.CantidadPiezas})."
                    );
                }
            }

            if (!ModelState.IsValid)
            {
                await CargarCatalogosAsync(vm);
                return View(vm);
            }

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var tx = await cn.BeginTransactionAsync();

            try
            {
                var datosActuales = await ObtenerDatosBasicosOFAsync(
                    vm.SolicitudProduccionID.Value,
                    cn,
                    (SqlTransaction)tx
                );

                if (datosActuales == null)
                {
                    await tx.RollbackAsync();
                    return NotFound();
                }

                var permisoEdicion = await ObtenerPermisoEdicionOFAsync(
                    vm.SolicitudProduccionID.Value,
                    datosActuales.FolioSolicitud,
                    datosActuales.NumeroOFRecibida,
                    datosActuales.EstatusID,
                    cn,
                    (SqlTransaction)tx
                );

                if (!permisoEdicion.PuedeEditar)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] = permisoEdicion.Motivo;
                    return RedirectToAction(nameof(Detalle), new { id = vm.SolicitudProduccionID.Value });
                }

                var clienteNombre = vm.ClienteNombre;

                if (vm.ClienteID.HasValue)
                {
                    clienteNombre = await ObtenerClienteNombreAsync(vm.ClienteID.Value, cn, (SqlTransaction)tx);
                }

                await ActualizarEncabezadoOFAsync(
                    vm,
                    clienteNombre,
                    usuarioId,
                    usuarioNombre,
                    cn,
                    (SqlTransaction)tx
                );

                await DesactivarDetalleAnteriorOFAsync(
                    vm.SolicitudProduccionID.Value,
                    usuarioId,
                    cn,
                    (SqlTransaction)tx
                );

                var renglon = 1;

                foreach (var d in vm.Detalles)
                {
                    await CompletarDetalleDesdeParteAsync(d, cn, (SqlTransaction)tx);
                    CalcularDatosTecnicos(d);

                    await CalcularCostosDetalleAsync(d, cn, (SqlTransaction)tx);

                    var detalleId = await InsertarDetalleAsync(
                        vm.SolicitudProduccionID.Value,
                        renglon,
                        d,
                        cn,
                        (SqlTransaction)tx
                    );

                    foreach (var a in d.AsignacionesMaquina)
                    {
                        await InsertarAsignacionMaquinaAsync(
                            detalleId,
                            a,
                            d.MoldeID,
                            usuarioId,
                            cn,
                            (SqlTransaction)tx
                        );
                    }

                    renglon++;
                }

                await ActualizarTotalesCostosOFAsync(
                    vm.SolicitudProduccionID.Value,
                    vm.Detalles,
                    cn,
                    (SqlTransaction)tx
                );

                await InsertarHistorialAsync(
                    vm.SolicitudProduccionID.Value,
                    datosActuales.EstatusID,
                    datosActuales.EstatusID,
                    "Edición de OF",
                    $"OF editada desde Planeación antes de movimientos de almacén. Tipo OF: {vm.TipoOF}.",
                    usuarioId,
                    cn,
                    (SqlTransaction)tx
                );

                await tx.CommitAsync();

                TempData["Success"] = "OF actualizada correctamente.";
                return RedirectToAction(nameof(Detalle), new { id = vm.SolicitudProduccionID.Value });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                ModelState.AddModelError("", "Ocurrió un error al actualizar la OF: " + ex.Message);
                await CargarCatalogosAsync(vm);
                return View(vm);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelarOF(int id, string motivoCancelacion)
        {
            if (id <= 0)
            {
                TempData["Error"] = "No se recibió la OF a cancelar.";
                return RedirectToAction(nameof(Index));
            }

            motivoCancelacion = motivoCancelacion?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(motivoCancelacion) || motivoCancelacion.Length < 5)
            {
                TempData["Error"] = "Captura un motivo de cancelación válido.";
                return RedirectToAction(nameof(Detalle), new { id });
            }

            if (motivoCancelacion.Length > 500)
            {
                TempData["Error"] = "El motivo de cancelación no puede exceder 500 caracteres.";
                return RedirectToAction(nameof(Detalle), new { id });
            }

            var usuarioId = ObtenerUsuarioID();

            if (usuarioId <= 0)
            {
                TempData["Error"] = "No se pudo identificar el usuario de la sesión.";
                return RedirectToAction(nameof(Detalle), new { id });
            }

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();

            try
            {
                int estatusActual;
                int? programaProduccionId;
                int? releaseDetalleId;
                string? origenOF;

                const string sqlObtener = @"
SELECT TOP 1
    SolicitudProduccionID,
    EstatusID,
    ProgramaProduccionID,
    ReleaseDetalleID,
    OrigenOF
FROM dbo.SolicitudesProduccion
WHERE SolicitudProduccionID = @SolicitudProduccionID
  AND Activo = 1;";

                await using (var cmd = new SqlCommand(sqlObtener, cn, tx))
                {
                    cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = id;

                    await using var rd = await cmd.ExecuteReaderAsync();

                    if (!await rd.ReadAsync())
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = "No se encontró la OF.";
                        return RedirectToAction(nameof(Index));
                    }

                    estatusActual = rd["EstatusID"] == DBNull.Value ? 0 : Convert.ToInt32(rd["EstatusID"]);

                    programaProduccionId = rd["ProgramaProduccionID"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(rd["ProgramaProduccionID"]);

                    releaseDetalleId = rd["ReleaseDetalleID"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(rd["ReleaseDetalleID"]);

                    origenOF = rd["OrigenOF"] == DBNull.Value
                        ? null
                        : rd["OrigenOF"].ToString();
                }

                if (estatusActual == PlaneacionOFEstatus.Cancelada || estatusActual == 99)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La OF ya se encuentra cancelada.";
                    return RedirectToAction(nameof(Detalle), new { id });
                }

                /*
                 * Regla base:
                 * La cancelación solo cancela la OF.
                 * Si la OF viene de Programa/Release, también se libera la liga para poder regenerarla después.
                 */

                const string sqlCancelarOF = @"
UPDATE dbo.SolicitudesProduccion
SET
    EstatusID = @EstatusCancelada,
    MotivoCancelacion = @MotivoCancelacion,
    FechaCancelacion = GETDATE(),
    UsuarioCancelacionID = @UsuarioCancelacionID
WHERE SolicitudProduccionID = @SolicitudProduccionID
  AND Activo = 1;";

                await using (var cmd = new SqlCommand(sqlCancelarOF, cn, tx))
                {
                    cmd.Parameters.Add("@EstatusCancelada", SqlDbType.Int).Value = PlaneacionOFEstatus.Cancelada;
                    cmd.Parameters.Add("@MotivoCancelacion", SqlDbType.NVarChar, 500).Value = motivoCancelacion;
                    cmd.Parameters.Add("@UsuarioCancelacionID", SqlDbType.Int).Value = usuarioId;
                    cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = id;

                    await cmd.ExecuteNonQueryAsync();
                }

                if (programaProduccionId.HasValue)
                {
                    // CANCELAR_OF_RETIRAR_CALENDARIO_V1_0
                    // Se conserva el programa para auditoria, pero queda cancelado y
                    // ya no puede ocupar una posicion en el calendario de maquinas.
                    const string sqlLiberarPrograma = @"
UPDATE dbo.Planeacion_ProgramaProduccion
SET
    EstatusID = @EstatusProgramaCancelado,
    SolicitudProduccionID = NULL,
    SolicitudProduccionDetalleID = NULL,
    FechaGeneracionOF = NULL,
    UsuarioGeneroOFID = NULL,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE ProgramaProduccionID = @ProgramaProduccionID
  AND Activo = 1;";

                    await using var cmd = new SqlCommand(sqlLiberarPrograma, cn, tx);
                    cmd.Parameters.Add("@EstatusProgramaCancelado", SqlDbType.Int).Value =
                        PlaneacionProgramaEstatus.Cancelado;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId.Value;

                    await cmd.ExecuteNonQueryAsync();
                }

                if (releaseDetalleId.HasValue)
                {
                    // La entrega vuelve a Calculado para que pueda programarse de nuevo.
                    // El programa cancelado se conserva en historial, pero ya no bloquea
                    // el Release ni vuelve a mostrarse en el calendario.
                    const string sqlLiberarReleaseDetalle = @"
UPDATE dbo.Planeacion_ReleaseDetalle
SET
    ProgramaProduccionID = NULL,
    SolicitudProduccionID = NULL,
    EstatusID = @EstatusReleaseCalculado,
    FechaProgramado = NULL,
    UsuarioProgramoID = NULL,
    FechaModificacion = GETDATE()
WHERE ReleaseDetalleID = @ReleaseDetalleID
  AND Activo = 1;";

                    await using var cmd = new SqlCommand(sqlLiberarReleaseDetalle, cn, tx);
                    cmd.Parameters.Add("@EstatusReleaseCalculado", SqlDbType.Int).Value =
                        PlaneacionReleaseEstatus.Calculado;
                    cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = releaseDetalleId.Value;

                    await cmd.ExecuteNonQueryAsync();
                }

                await InsertarHistorialAsync(
                    id,
                    estatusActual,
                    PlaneacionOFEstatus.Cancelada,
                    "Cancelacion de OF y retiro de calendario",
                    motivoCancelacion,
                    usuarioId,
                    cn,
                    tx
                );

                await tx.CommitAsync();

                TempData["Success"] = "OF cancelada correctamente.";
                return RedirectToAction(nameof(Detalle), new { id });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                TempData["Error"] = "No fue posible cancelar la OF: " + ex.Message;
                return RedirectToAction(nameof(Detalle), new { id });
            }
        }



        // helpesr generales

        private static string NormalizarTipoOF(string? tipoOF)
        {
            if (string.IsNullOrWhiteSpace(tipoOF))
                return "RELEASE";

            var valor = tipoOF.Trim().ToUpperInvariant();

            valor = valor
                .Replace("Á", "A")
                .Replace("É", "E")
                .Replace("Í", "I")
                .Replace("Ó", "O")
                .Replace("Ú", "U");

            return valor switch
            {
                "RELEASE" => "RELEASE",
                "ENSAMBLE" => "ENSAMBLE",
                "PRUEBA" => "PRUEBA",
                "MP EXTRA" => "MP EXTRA",
                "MPEXTRA" => "MP EXTRA",
                "MP_EXTRA" => "MP EXTRA",
                _ => "RELEASE"
            };
        }

        private static bool TipoOFRequiereMotivo(string? tipoOF)
        {
            var valor = NormalizarTipoOF(tipoOF);

            return valor == "PRUEBA" ||
                   valor == "MP EXTRA";
        }

        private void ValidarTipoOF(string? tipoOF, string? motivoTipoOF, string campoMotivo)
        {
            var valor = NormalizarTipoOF(tipoOF);

            if (valor != "RELEASE" &&
                valor != "ENSAMBLE" &&
                valor != "PRUEBA" &&
                valor != "MP EXTRA")
            {
                ModelState.AddModelError(nameof(PlaneacionOFCrearVm.TipoOF), "Selecciona un tipo de OF válido.");
            }

            if (TipoOFRequiereMotivo(valor) &&
                string.IsNullOrWhiteSpace(motivoTipoOF))
            {
                ModelState.AddModelError(campoMotivo, "Captura el motivo para este tipo de OF.");
            }
        }


        private async Task<string> GenerarFolioOFAsync(SqlConnection cn, SqlTransaction tx)
        {
            var yy = DateTime.Now.ToString("yy");
            var prefijo = $"OF-";
            var sufijo = $"/{yy}";

            const string sql = @"
SELECT
    ISNULL(MAX(TRY_CONVERT(INT, SUBSTRING(FolioSolicitud, 4, 4))), 0) + 1
FROM dbo.SolicitudesProduccion WITH (UPDLOCK, HOLDLOCK)
WHERE Activo = 1
  AND FolioSolicitud LIKE @Patron;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@Patron", SqlDbType.NVarChar, 30).Value = $"{prefijo}____{sufijo}";

            var siguiente = Convert.ToInt32(await cmd.ExecuteScalarAsync());

            return $"OF-{siguiente:0000}/{yy}";
        }


        private async Task<string> ObtenerNumeroOFSugeridoAsync()
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var yy = DateTime.Now.ToString("yy");

            const string sql = @"
SELECT
    ISNULL(MAX(TRY_CONVERT(INT, SUBSTRING(NumeroOFRecibida, 4, 4))), 0) + 1
FROM dbo.SolicitudesProduccion
WHERE Activo = 1
  AND NumeroOFRecibida LIKE @Patron;";

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@Patron", SqlDbType.NVarChar, 30).Value = $"OF-____/{yy}";

            var siguiente = Convert.ToInt32(await cmd.ExecuteScalarAsync());

            return $"OF-{siguiente:0000}/{yy}";
        }


        private int ObtenerUsuarioID()
        {
            return HttpContext.Session.GetInt32("UsuarioID") ?? 0;
        }

        private string ObtenerUsuarioNombre()
        {
            return HttpContext.Session.GetString("NombreUsuario")
                ?? User.Identity?.Name
                ?? "Usuario de Planeación";
        }

        private static void AddDecimal(SqlCommand cmd, string name, decimal? value, byte precision, byte scale)
        {
            var p = cmd.Parameters.Add(name, SqlDbType.Decimal);
            p.Precision = precision;
            p.Scale = scale;
            p.Value = (object?)value ?? DBNull.Value;
        }

        private async Task<(bool PuedeEditar, string? Motivo)> ObtenerPermisoEdicionOFAsync(
     int solicitudProduccionId,
     string? folioSolicitud,
     string? numeroOFRecibida,
     int estatusId,
     SqlConnection cn,
     SqlTransaction? tx = null)
        {
            if (solicitudProduccionId <= 0)
            {
                return (false, "La OF no es válida.");
            }

            if (estatusId == PlaneacionOFEstatus.Cancelada || estatusId == 99)
            {
                return (false, "La OF está cancelada.");
            }

            if (estatusId >= PlaneacionOFEstatus.EnProduccion)
            {
                return (false, "La OF ya está en producción o en una etapa posterior.");
            }

            /*
             * Regla nueva:
             * Solo se puede editar desde OF cuando fue creada manualmente.
             * Si viene de Release / Programa, se debe editar desde Release y recalcular.
             */
            const string sqlOrigen = @"
SELECT TOP 1
    ReleaseID,
    ReleaseDetalleID,
    ProgramaProduccionID,
    OrigenOF
FROM dbo.SolicitudesProduccion
WHERE SolicitudProduccionID = @SolicitudProduccionID
  AND Activo = 1;";

            int? releaseId = null;
            int? releaseDetalleId = null;
            int? programaProduccionId = null;
            string? origenOF = null;

            await using (var cmd = tx == null
                ? new SqlCommand(sqlOrigen, cn)
                : new SqlCommand(sqlOrigen, cn, tx))
            {
                cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = solicitudProduccionId;

                await using var rd = await cmd.ExecuteReaderAsync();

                if (!await rd.ReadAsync())
                {
                    return (false, "No se encontró la OF.");
                }

                releaseId = rd["ReleaseID"] == DBNull.Value
                    ? null
                    : Convert.ToInt32(rd["ReleaseID"]);

                releaseDetalleId = rd["ReleaseDetalleID"] == DBNull.Value
                    ? null
                    : Convert.ToInt32(rd["ReleaseDetalleID"]);

                programaProduccionId = rd["ProgramaProduccionID"] == DBNull.Value
                    ? null
                    : Convert.ToInt32(rd["ProgramaProduccionID"]);

                origenOF = rd["OrigenOF"] == DBNull.Value
                    ? null
                    : rd["OrigenOF"].ToString();
            }

            var origen = (origenOF ?? "").Trim().ToUpperInvariant();

            var vieneDeReleaseOPrograma =
                releaseId.HasValue ||
                releaseDetalleId.HasValue ||
                programaProduccionId.HasValue ||
                origen == "RELEASE" ||
                origen == "PROGRAMA";

            if (vieneDeReleaseOPrograma)
            {
                return (
                    false,
                    "Esta OF nació desde un Release/Programa. Para modificarla, edita el Release y recalcula la planeación."
                );
            }

            /*
             * Regla existente:
             * Si ya tiene movimientos de almacén, no se puede editar.
             */
            var folio = folioSolicitud?.Trim();
            var numero = numeroOFRecibida?.Trim();

            const string sqlMovimientos = @"
DECLARE @TotalMovimientos BIGINT = 0;

IF OBJECT_ID('dbo.AlmacenMP_Movimientos', 'U') IS NOT NULL
BEGIN
    SELECT @TotalMovimientos = @TotalMovimientos + COUNT_BIG(1)
    FROM dbo.AlmacenMP_Movimientos
    WHERE Activo = 1
      AND NULLIF(LTRIM(RTRIM(NumeroOF)), '') IS NOT NULL
      AND
      (
            (@FolioSolicitud IS NOT NULL AND LTRIM(RTRIM(NumeroOF)) = @FolioSolicitud)
         OR (@NumeroOFRecibida IS NOT NULL AND LTRIM(RTRIM(NumeroOF)) = @NumeroOFRecibida)
      );
END;

IF OBJECT_ID('dbo.AlmacenEmbalajes_Movimientos', 'U') IS NOT NULL
BEGIN
    SELECT @TotalMovimientos = @TotalMovimientos + COUNT_BIG(1)
    FROM dbo.AlmacenEmbalajes_Movimientos
    WHERE Activo = 1
      AND NULLIF(LTRIM(RTRIM(NumeroOF)), '') IS NOT NULL
      AND
      (
            (@FolioSolicitud IS NOT NULL AND LTRIM(RTRIM(NumeroOF)) = @FolioSolicitud)
         OR (@NumeroOFRecibida IS NOT NULL AND LTRIM(RTRIM(NumeroOF)) = @NumeroOFRecibida)
      );
END;

IF OBJECT_ID('dbo.AlmacenPT_Movimientos', 'U') IS NOT NULL
BEGIN
    SELECT @TotalMovimientos = @TotalMovimientos + COUNT_BIG(1)
    FROM dbo.AlmacenPT_Movimientos
    WHERE Activo = 1
      AND NULLIF(LTRIM(RTRIM(NumeroOF)), '') IS NOT NULL
      AND
      (
            (@FolioSolicitud IS NOT NULL AND LTRIM(RTRIM(NumeroOF)) = @FolioSolicitud)
         OR (@NumeroOFRecibida IS NOT NULL AND LTRIM(RTRIM(NumeroOF)) = @NumeroOFRecibida)
      );
END;

SELECT @TotalMovimientos;";

            await using (var cmd = tx == null
                ? new SqlCommand(sqlMovimientos, cn)
                : new SqlCommand(sqlMovimientos, cn, tx))
            {
                cmd.Parameters.Add("@FolioSolicitud", SqlDbType.NVarChar, 80).Value =
                    string.IsNullOrWhiteSpace(folio)
                        ? (object)DBNull.Value
                        : folio;

                cmd.Parameters.Add("@NumeroOFRecibida", SqlDbType.NVarChar, 80).Value =
                    string.IsNullOrWhiteSpace(numero)
                        ? (object)DBNull.Value
                        : numero;

                var total = Convert.ToInt64(await cmd.ExecuteScalarAsync());

                if (total > 0)
                {
                    return (false, "La OF ya tiene movimientos de almacén relacionados. No se puede editar.");
                }
            }

            return (true, null);
        }



        private async Task<PlaneacionOFCrearVm?> ObtenerOFParaEditarAsync(
    int solicitudProduccionId,
    SqlConnection cn)
        {
            PlaneacionOFCrearVm? vm = null;

            const string sqlEncabezado = @"
SELECT
    SolicitudProduccionID,
    FolioSolicitud,
    NumeroOFRecibida,
    FechaSolicitud,
    FechaRequerida,
    FechaInicioPlaneada,
    FechaFinPlaneada,
    ClienteID,
    ClienteNombre,
    OrigenSolicitud,
    Prioridad,
    TipoOF,
    MotivoTipoOF,
    EstatusID,
    NotasGenerales
FROM dbo.SolicitudesProduccion
WHERE SolicitudProduccionID = @SolicitudProduccionID
  AND Activo = 1;";

            await using (var cmd = new SqlCommand(sqlEncabezado, cn))
            {
                cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = solicitudProduccionId;

                await using var rd = await cmd.ExecuteReaderAsync();

                if (!await rd.ReadAsync())
                    return null;

                vm = new PlaneacionOFCrearVm
                {
                    SolicitudProduccionID = Convert.ToInt32(rd["SolicitudProduccionID"]),
                    FolioSolicitud = rd["FolioSolicitud"] as string,
                    NumeroOFRecibida = rd["NumeroOFRecibida"] as string,
                    FechaSolicitud = Convert.ToDateTime(rd["FechaSolicitud"]),
                    FechaRequerida = rd["FechaRequerida"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaRequerida"]),
                    FechaInicioPlaneada = rd["FechaInicioPlaneada"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaInicioPlaneada"]),
                    FechaFinPlaneada = rd["FechaFinPlaneada"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaFinPlaneada"]),
                    ClienteID = rd["ClienteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ClienteID"]),
                    ClienteNombre = rd["ClienteNombre"] as string,
                    OrigenSolicitud = rd["OrigenSolicitud"] as string ?? "Dirección",
                    Prioridad = rd["Prioridad"] as string ?? "Normal",
                    TipoOF = NormalizarTipoOF(rd["TipoOF"] as string),
                    MotivoTipoOF = rd["MotivoTipoOF"] as string,
                    EstatusID = Convert.ToInt32(rd["EstatusID"]),
                    NotasGenerales = rd["NotasGenerales"] as string,
                    EsEdicion = true
                };
            }

            const string sqlDetalles = @"
SELECT
    SolicitudProduccionDetalleID,
    Renglon,
    ParteID,
    MoldeID,
    DesignacionDescripcionSAP,
    ReferenciaSAP,
    CantidadPiezas,
    HorasPlaneadas,
    NumeroMoldeTexto,
    Color,
    Cavidades,
    ObjetivoHora,
    PiezasPorCaja,
    Ciclo,
    TipoSecado,
    HorasSecado,
    PesoBrutoPieza,
    MaterialID,
    MaterialCodigo,
    MaterialDescripcion,
    OrigenSurtido,
    PTDisponibleAlCrear,
    MPDisponibleKgAlCrear,
    AlmacenValidado,
    MensajeAlmacen,
    EmbalajeCodigo,
    EmbalajeDescripcion,
    PiezasPorEmbalaje,
    CantidadEmbalajes,
    CantidadMpKg,
    Cambio,
    Arranque,
    Notas
FROM dbo.SolicitudesProduccionDetalle
WHERE SolicitudProduccionID = @SolicitudProduccionID
  AND Activo = 1
ORDER BY Renglon;";

            await using (var cmd = new SqlCommand(sqlDetalles, cn))
            {
                cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = solicitudProduccionId;

                await using var rd = await cmd.ExecuteReaderAsync();

                while (await rd.ReadAsync())
                {
                    vm.Detalles.Add(new PlaneacionOFDetalleCrearVm
                    {
                        Renglon = Convert.ToInt32(rd["Renglon"]),
                        ParteID = rd["ParteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ParteID"]),
                        MoldeID = rd["MoldeID"] == DBNull.Value ? null : Convert.ToInt32(rd["MoldeID"]),
                        DesignacionDescripcionSAP = rd["DesignacionDescripcionSAP"] as string,
                        ReferenciaSAP = rd["ReferenciaSAP"] as string,
                        CantidadPiezas = rd["CantidadPiezas"] == DBNull.Value ? 0 : Convert.ToInt32(rd["CantidadPiezas"]),
                        HorasPlaneadas = rd["HorasPlaneadas"] == DBNull.Value ? null : Convert.ToDecimal(rd["HorasPlaneadas"]),
                        NumeroMoldeTexto = rd["NumeroMoldeTexto"] as string,
                        Color = rd["Color"] as string,
                        Cavidades = rd["Cavidades"] == DBNull.Value ? null : Convert.ToInt32(rd["Cavidades"]),
                        ObjetivoHora = rd["ObjetivoHora"] == DBNull.Value ? null : Convert.ToInt32(rd["ObjetivoHora"]),
                        PiezasPorCaja = rd["PiezasPorCaja"] == DBNull.Value ? null : Convert.ToInt32(rd["PiezasPorCaja"]),
                        Ciclo = rd["Ciclo"] as string,
                        TipoSecado = rd["TipoSecado"] as string,
                        HorasSecado = rd["HorasSecado"] == DBNull.Value ? null : Convert.ToDecimal(rd["HorasSecado"]),
                        PesoBrutoPieza = rd["PesoBrutoPieza"] == DBNull.Value ? null : Convert.ToDecimal(rd["PesoBrutoPieza"]),
                        MaterialID = rd["MaterialID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaterialID"]),
                        MaterialCodigo = rd["MaterialCodigo"] as string,
                        MaterialDescripcion = rd["MaterialDescripcion"] as string,
                        OrigenSurtido = rd["OrigenSurtido"] as string,
                        PTDisponibleAlCrear = rd["PTDisponibleAlCrear"] == DBNull.Value ? null : Convert.ToInt32(rd["PTDisponibleAlCrear"]),
                        MPDisponibleKgAlCrear = rd["MPDisponibleKgAlCrear"] == DBNull.Value ? null : Convert.ToDecimal(rd["MPDisponibleKgAlCrear"]),
                        AlmacenValidado = rd["AlmacenValidado"] != DBNull.Value && Convert.ToBoolean(rd["AlmacenValidado"]),
                        MensajeAlmacen = rd["MensajeAlmacen"] as string,
                        EmbalajeCodigo = rd["EmbalajeCodigo"] as string,
                        EmbalajeDescripcion = rd["EmbalajeDescripcion"] as string,
                        PiezasPorEmbalaje = rd["PiezasPorEmbalaje"] == DBNull.Value ? null : Convert.ToDecimal(rd["PiezasPorEmbalaje"]),
                        CantidadEmbalajes = rd["CantidadEmbalajes"] == DBNull.Value ? null : Convert.ToDecimal(rd["CantidadEmbalajes"]),
                        CantidadMpKg = rd["CantidadMpKg"] == DBNull.Value ? null : Convert.ToDecimal(rd["CantidadMpKg"]),
                        Cambio = rd["Cambio"] == DBNull.Value ? null : (TimeSpan)rd["Cambio"],
                        Arranque = rd["Arranque"] == DBNull.Value ? null : (TimeSpan)rd["Arranque"],
                        Notas = rd["Notas"] as string,
                        AsignacionesMaquina = new List<PlaneacionOFAsignacionMaquinaCrearVm>()
                    });
                }
            }

            const string sqlAsignaciones = @"
SELECT
    a.SolicitudProduccionDetalleID,
    d.Renglon,
    a.MaquinaID,
    a.MoldeID,
    a.CantidadAsignada,
    a.HorasEstimadas,
    a.Secuencia,
    a.CondicionProduccion,
    a.FechaProgramadaTentativa,
    a.HoraInicioTentativa,
    a.HoraFinTentativa,
    a.Observaciones
FROM dbo.SolicitudesProduccionAsignacionMaquina a
INNER JOIN dbo.SolicitudesProduccionDetalle d
    ON d.SolicitudProduccionDetalleID = a.SolicitudProduccionDetalleID
WHERE d.SolicitudProduccionID = @SolicitudProduccionID
  AND d.Activo = 1
  AND a.Activo = 1
ORDER BY d.Renglon, a.Secuencia;";

            await using (var cmd = new SqlCommand(sqlAsignaciones, cn))
            {
                cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = solicitudProduccionId;

                await using var rd = await cmd.ExecuteReaderAsync();

                while (await rd.ReadAsync())
                {
                    var renglon = Convert.ToInt32(rd["Renglon"]);
                    var detalle = vm.Detalles.FirstOrDefault(x => x.Renglon == renglon);

                    if (detalle == null)
                        continue;

                    detalle.AsignacionesMaquina.Add(new PlaneacionOFAsignacionMaquinaCrearVm
                    {
                        MaquinaID = rd["MaquinaID"] == DBNull.Value ? 0 : Convert.ToInt32(rd["MaquinaID"]),
                        MoldeID = rd["MoldeID"] == DBNull.Value ? null : Convert.ToInt32(rd["MoldeID"]),
                        CantidadAsignada = rd["CantidadAsignada"] == DBNull.Value ? 0 : Convert.ToInt32(rd["CantidadAsignada"]),
                        HorasEstimadas = rd["HorasEstimadas"] == DBNull.Value ? null : Convert.ToDecimal(rd["HorasEstimadas"]),
                        Secuencia = rd["Secuencia"] == DBNull.Value ? 1 : Convert.ToInt32(rd["Secuencia"]),
                        CondicionProduccion = rd["CondicionProduccion"] as string,
                        FechaProgramadaTentativa = rd["FechaProgramadaTentativa"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaProgramadaTentativa"]),
                        HoraInicioTentativa = rd["HoraInicioTentativa"] == DBNull.Value ? null : (TimeSpan)rd["HoraInicioTentativa"],
                        HoraFinTentativa = rd["HoraFinTentativa"] == DBNull.Value ? null : (TimeSpan)rd["HoraFinTentativa"],
                        Observaciones = rd["Observaciones"] as string
                    });
                }
            }

            foreach (var detalle in vm.Detalles)
            {
                if (detalle.AsignacionesMaquina == null || !detalle.AsignacionesMaquina.Any())
                {
                    detalle.AsignacionesMaquina = new List<PlaneacionOFAsignacionMaquinaCrearVm>
            {
                new PlaneacionOFAsignacionMaquinaCrearVm
                {
                    Secuencia = 1
                }
            };
                }
            }

            if (!vm.Detalles.Any())
            {
                vm.Detalles.Add(new PlaneacionOFDetalleCrearVm
                {
                    Renglon = 1,
                    AsignacionesMaquina = new List<PlaneacionOFAsignacionMaquinaCrearVm>
            {
                new PlaneacionOFAsignacionMaquinaCrearVm
                {
                    Secuencia = 1
                }
            }
                });
            }

            return vm;
        }

        private sealed class DatosBasicosOF
        {
            public string? FolioSolicitud { get; set; }
            public string? NumeroOFRecibida { get; set; }
            public int EstatusID { get; set; }
        }

        private async Task<DatosBasicosOF?> ObtenerDatosBasicosOFAsync(
            int solicitudProduccionId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT
    FolioSolicitud,
    NumeroOFRecibida,
    EstatusID
FROM dbo.SolicitudesProduccion
WHERE SolicitudProduccionID = @SolicitudProduccionID
  AND Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = solicitudProduccionId;

            await using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
                return null;

            return new DatosBasicosOF
            {
                FolioSolicitud = rd["FolioSolicitud"] as string,
                NumeroOFRecibida = rd["NumeroOFRecibida"] as string,
                EstatusID = Convert.ToInt32(rd["EstatusID"])
            };
        }


        private async Task ActualizarEncabezadoOFAsync(
    PlaneacionOFCrearVm vm,
    string? clienteNombre,
    int usuarioId,
    string usuarioNombre,
    SqlConnection cn,
    SqlTransaction tx)
        {
            const string sql = @"
UPDATE dbo.SolicitudesProduccion
SET
    NumeroOFRecibida = @NumeroOFRecibida,
    FechaSolicitud = @FechaSolicitud,
    FechaRequerida = @FechaRequerida,
    FechaInicioPlaneada = @FechaInicioPlaneada,
    FechaFinPlaneada = @FechaFinPlaneada,
    ClienteID = @ClienteID,
    ClienteNombre = @ClienteNombre,
    OrigenSolicitud = @OrigenSolicitud,
    Prioridad = @Prioridad,
    TipoOF = @TipoOF,
    MotivoTipoOF = @MotivoTipoOF,
    NotasGenerales = @NotasGenerales,
    ResponsablePlaneacionUsuarioID = @ResponsablePlaneacionUsuarioID,
    ResponsablePlaneacionNombre = @ResponsablePlaneacionNombre,
    UsuarioModificacionID = @UsuarioModificacionID,
    FechaModificacion = GETDATE()
WHERE SolicitudProduccionID = @SolicitudProduccionID
  AND Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@NumeroOFRecibida", SqlDbType.NVarChar, 80).Value =
                (object?)vm.NumeroOFRecibida ?? DBNull.Value;

            cmd.Parameters.Add("@FechaSolicitud", SqlDbType.Date).Value = vm.FechaSolicitud.Date;

            cmd.Parameters.Add("@FechaRequerida", SqlDbType.Date).Value =
                (object?)vm.FechaRequerida?.Date ?? DBNull.Value;

            cmd.Parameters.Add("@FechaInicioPlaneada", SqlDbType.DateTime).Value =
                (object?)vm.FechaInicioPlaneada ?? DBNull.Value;

            cmd.Parameters.Add("@FechaFinPlaneada", SqlDbType.DateTime).Value =
                (object?)vm.FechaFinPlaneada ?? DBNull.Value;

            cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value =
                (object?)vm.ClienteID ?? DBNull.Value;

            cmd.Parameters.Add("@ClienteNombre", SqlDbType.NVarChar, 200).Value =
                (object?)clienteNombre ?? DBNull.Value;

            cmd.Parameters.Add("@OrigenSolicitud", SqlDbType.NVarChar, 50).Value =
                string.IsNullOrWhiteSpace(vm.OrigenSolicitud) ? "Dirección" : vm.OrigenSolicitud;

            cmd.Parameters.Add("@Prioridad", SqlDbType.NVarChar, 30).Value =
                string.IsNullOrWhiteSpace(vm.Prioridad) ? "Normal" : vm.Prioridad.Trim();

            cmd.Parameters.Add("@TipoOF", SqlDbType.NVarChar, 30).Value =
                NormalizarTipoOF(vm.TipoOF);

            cmd.Parameters.Add("@MotivoTipoOF", SqlDbType.NVarChar, 500).Value =
                string.IsNullOrWhiteSpace(vm.MotivoTipoOF) ? DBNull.Value : vm.MotivoTipoOF.Trim();

            cmd.Parameters.Add("@NotasGenerales", SqlDbType.NVarChar).Value =
                (object?)vm.NotasGenerales ?? DBNull.Value;

            cmd.Parameters.Add("@ResponsablePlaneacionUsuarioID", SqlDbType.Int).Value = usuarioId;
            cmd.Parameters.Add("@ResponsablePlaneacionNombre", SqlDbType.NVarChar, 200).Value = usuarioNombre;
            cmd.Parameters.Add("@UsuarioModificacionID", SqlDbType.Int).Value = usuarioId;
            cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = vm.SolicitudProduccionID!.Value;

            await cmd.ExecuteNonQueryAsync();
        }

        private async Task DesactivarDetalleAnteriorOFAsync(
    int solicitudProduccionId,
    int usuarioId,
    SqlConnection cn,
    SqlTransaction tx)
        {
            const string sqlAsignaciones = @"
                UPDATE a
                SET
                    a.Activo = 0,
                    a.UsuarioModificacionID = @UsuarioModificacionID,
                    a.FechaModificacion = GETDATE()
                FROM dbo.SolicitudesProduccionAsignacionMaquina a
                INNER JOIN dbo.SolicitudesProduccionDetalle d
                    ON d.SolicitudProduccionDetalleID = a.SolicitudProduccionDetalleID
                WHERE d.SolicitudProduccionID = @SolicitudProduccionID
                AND a.Activo = 1;";

            await using (var cmd = new SqlCommand(sqlAsignaciones, cn, tx))
            {
                cmd.Parameters.Add("@UsuarioModificacionID", SqlDbType.Int).Value = usuarioId;
                cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = solicitudProduccionId;

                await cmd.ExecuteNonQueryAsync();
            }

            const string sqlDetalles = @"
                UPDATE dbo.SolicitudesProduccionDetalle
                SET
                    Activo = 0,
                    UsuarioModificacionID = @UsuarioModificacionID,
                    FechaModificacion = GETDATE()
                WHERE SolicitudProduccionID = @SolicitudProduccionID
                AND Activo = 1;";

            await using (var cmd = new SqlCommand(sqlDetalles, cn, tx))
            {
                cmd.Parameters.Add("@UsuarioModificacionID", SqlDbType.Int).Value = usuarioId;
                cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = solicitudProduccionId;

                await cmd.ExecuteNonQueryAsync();
            }
        }
    }
}