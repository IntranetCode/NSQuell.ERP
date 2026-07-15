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
    s.EstatusID,
    s.ResponsablePlaneacionNombre,
    COUNT(DISTINCT d.SolicitudProduccionDetalleID) AS TotalRenglones,
    ISNULL(SUM(d.CantidadPiezas), 0) AS TotalPiezas
FROM dbo.SolicitudesProduccion s
LEFT JOIN dbo.ERP_Clientes c
    ON c.ClienteID = s.ClienteID
LEFT JOIN dbo.SolicitudesProduccionDetalle d
    ON d.SolicitudProduccionID = s.SolicitudProduccionID
   AND d.Activo = 1
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
    s.EstatusID,
    s.ResponsablePlaneacionNombre,
    s.FechaCreacion
ORDER BY s.FechaCreacion DESC;";

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                var estatusId = Convert.ToInt32(rd["EstatusID"]);

                lista.Add(new PlaneacionOFIndexVm
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
                    EstatusID = estatusId,
                    EstatusNombre = PlaneacionOFEstatus.Nombre(estatusId),
                    ResponsablePlaneacionNombre = rd["ResponsablePlaneacionNombre"] as string,
                    TotalRenglones = Convert.ToInt32(rd["TotalRenglones"]),
                    TotalPiezas = Convert.ToInt32(rd["TotalPiezas"])
                });
            }

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
                Prioridad = "Normal"
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
                    "OF capturada por Planeación con datos recibidos de Dirección.",
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
    p.Color,
    p.Cavidades,
    p.ObjetivoHora,
    p.PiezasPorCaja,
    p.MoldePrincipalID,
    mol.CodigoMolde AS MoldeCodigo,
    p.MaquinaPrincipalID,
    p.MaquinaSustitutaID,

    COALESCE(p.MaterialID, t.MaterialID) AS MaterialID,
    COALESCE(NULLIF(p.MaterialCodigo, ''), t.MaterialCodigo) AS MaterialCodigo,
    COALESCE(NULLIF(p.MaterialDescripcion, ''), t.MaterialDescripcion) AS MaterialDescripcion,

    COALESCE(t.Ciclo, NULL) AS Ciclo,
    COALESCE(t.TipoSecado, p.TipoSecado) AS TipoSecado,
    COALESCE(t.HorasSecado, p.HorasSecado) AS HorasSecado,
    COALESCE(t.PesoBrutoPieza, p.PesoBrutoPieza) AS PesoBrutoPieza,
    COALESCE(NULLIF(t.EmbalajeCodigo, ''), p.EmbalajeCodigo) AS EmbalajeCodigo,
    COALESCE(NULLIF(t.EmbalajeDescripcion, ''), p.EmbalajeDescripcion) AS EmbalajeDescripcion,
    COALESCE(t.PiezasPorEmbalaje, p.PiezasPorEmbalaje) AS PiezasPorEmbalaje
FROM dbo.ERP_Partes p
LEFT JOIN dbo.ERP_Clientes c
    ON c.ClienteID = p.ClienteID
LEFT JOIN dbo.ERP_Moldes mol
    ON mol.MoldeID = p.MoldePrincipalID
LEFT JOIN dbo.ERP_ParteDatosTecnicos t
    ON t.ParteID = p.ParteID
   AND t.Activo = 1
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

    COALESCE(p.MaterialID, t.MaterialID) AS MaterialID,
    COALESCE(NULLIF(p.MaterialCodigo, ''), t.MaterialCodigo) AS MaterialCodigo,
    COALESCE(NULLIF(p.MaterialDescripcion, ''), t.MaterialDescripcion) AS MaterialDescripcion,
    COALESCE(t.PesoBrutoPieza, p.PesoBrutoPieza) AS PesoBrutoPieza,

    COALESCE(NULLIF(t.EmbalajeCodigo, ''), p.EmbalajeCodigo) AS EmbalajeCodigo,
    COALESCE(NULLIF(t.EmbalajeDescripcion, ''), p.EmbalajeDescripcion) AS EmbalajeDescripcion,
    COALESCE(t.PiezasPorEmbalaje, p.PiezasPorEmbalaje) AS PiezasPorEmbalaje
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


        //Inserts
        private async Task<int> InsertarEncabezadoAsync(
            PlaneacionOFCrearVm vm,
            string? clienteNombre,
            int usuarioId,
            string usuarioNombre,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
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
    EstatusID,
    NotasGenerales,
    ResponsablePlaneacionUsuarioID,
    ResponsablePlaneacionNombre,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
OUTPUT INSERTED.SolicitudProduccionID
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
    @EstatusID,
    @NotasGenerales,
    @ResponsablePlaneacionUsuarioID,
    @ResponsablePlaneacionNombre,
    @UsuarioCreacionID,
    GETDATE(),
    1
);";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@FolioSolicitud", SqlDbType.NVarChar, 30).Value = (object?)vm.FolioSolicitud ?? DBNull.Value;
            cmd.Parameters.Add("@NumeroOFRecibida", SqlDbType.NVarChar, 80).Value = (object?)vm.NumeroOFRecibida ?? DBNull.Value;
            cmd.Parameters.Add("@FechaSolicitud", SqlDbType.Date).Value = vm.FechaSolicitud.Date;
            cmd.Parameters.Add("@FechaRequerida", SqlDbType.Date).Value = (object?)vm.FechaRequerida?.Date ?? DBNull.Value;
            cmd.Parameters.Add("@FechaInicioPlaneada", SqlDbType.DateTime).Value = (object?)vm.FechaInicioPlaneada ?? DBNull.Value;
            cmd.Parameters.Add("@FechaFinPlaneada", SqlDbType.DateTime).Value = (object?)vm.FechaFinPlaneada ?? DBNull.Value;
            cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = (object?)vm.ClienteID ?? DBNull.Value;
            cmd.Parameters.Add("@ClienteNombre", SqlDbType.NVarChar, 200).Value = (object?)clienteNombre ?? DBNull.Value;
            cmd.Parameters.Add("@OrigenSolicitud", SqlDbType.NVarChar, 50).Value = (object?)vm.OrigenSolicitud ?? "Dirección";
            cmd.Parameters.Add("@Prioridad", SqlDbType.NVarChar, 30).Value = string.IsNullOrWhiteSpace(vm.Prioridad) ? "Normal" : vm.Prioridad;
            cmd.Parameters.Add("@EstatusID", SqlDbType.Int).Value = PlaneacionOFEstatus.Capturada;
            cmd.Parameters.Add("@NotasGenerales", SqlDbType.NVarChar).Value = (object?)vm.NotasGenerales ?? DBNull.Value;
            cmd.Parameters.Add("@ResponsablePlaneacionUsuarioID", SqlDbType.Int).Value = usuarioId;
            cmd.Parameters.Add("@ResponsablePlaneacionNombre", SqlDbType.NVarChar, 200).Value = usuarioNombre;
            cmd.Parameters.Add("@UsuarioCreacionID", SqlDbType.Int).Value = usuarioId;

            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        private async Task<int> InsertarDetalleAsync(
            int solicitudId,
            int renglon,
            PlaneacionOFDetalleCrearVm d,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
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
);";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = solicitudId;
            cmd.Parameters.Add("@Renglon", SqlDbType.Int).Value = renglon;
            cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = (object?)d.ParteID ?? DBNull.Value;
            cmd.Parameters.Add("@MoldeID", SqlDbType.Int).Value = (object?)d.MoldeID ?? DBNull.Value;
            cmd.Parameters.Add("@DesignacionDescripcionSAP", SqlDbType.NVarChar, 300).Value = d.DesignacionDescripcionSAP;
            cmd.Parameters.Add("@ReferenciaSAP", SqlDbType.NVarChar, 150).Value = d.ReferenciaSAP;
            cmd.Parameters.Add("@CantidadPiezas", SqlDbType.Int).Value = d.CantidadPiezas;

            AddDecimal(cmd, "@HorasPlaneadas", d.HorasPlaneadas, 10, 2);
            cmd.Parameters.Add("@NumeroMoldeTexto", SqlDbType.NVarChar, 100).Value = (object?)d.NumeroMoldeTexto ?? DBNull.Value;
            cmd.Parameters.Add("@Color", SqlDbType.NVarChar, 80).Value = (object?)d.Color ?? DBNull.Value;
            cmd.Parameters.Add("@Cavidades", SqlDbType.Int).Value = (object?)d.Cavidades ?? DBNull.Value;
            cmd.Parameters.Add("@ObjetivoHora", SqlDbType.Int).Value = (object?)d.ObjetivoHora ?? DBNull.Value;
            cmd.Parameters.Add("@PiezasPorCaja", SqlDbType.Int).Value = (object?)d.PiezasPorCaja ?? DBNull.Value;
            cmd.Parameters.Add("@Ciclo", SqlDbType.NVarChar, 80).Value = (object?)d.Ciclo ?? DBNull.Value;
            cmd.Parameters.Add("@TipoSecado", SqlDbType.NVarChar, 100).Value = (object?)d.TipoSecado ?? DBNull.Value;
            AddDecimal(cmd, "@HorasSecado", d.HorasSecado, 10, 2);
            AddDecimal(cmd, "@PesoBrutoPieza", d.PesoBrutoPieza, 18, 6);
            cmd.Parameters.Add("@MaterialCodigo", SqlDbType.NVarChar, 100).Value = (object?)d.MaterialCodigo ?? DBNull.Value;
            cmd.Parameters.Add("@MaterialDescripcion", SqlDbType.NVarChar, 250).Value = (object?)d.MaterialDescripcion ?? DBNull.Value;
            cmd.Parameters.Add("@MaterialID", SqlDbType.Int).Value = (object?)d.MaterialID ?? DBNull.Value;
            cmd.Parameters.Add("@OrigenSurtido", SqlDbType.NVarChar, 30).Value = (object?)d.OrigenSurtido ?? DBNull.Value;
            cmd.Parameters.Add("@PTDisponibleAlCrear", SqlDbType.Int).Value = (object?)d.PTDisponibleAlCrear ?? DBNull.Value;
            AddDecimal(cmd, "@MPDisponibleKgAlCrear", d.MPDisponibleKgAlCrear, 18, 4);
            cmd.Parameters.Add("@AlmacenValidado", SqlDbType.Bit).Value = d.AlmacenValidado;
            cmd.Parameters.Add("@MensajeAlmacen", SqlDbType.NVarChar, 500).Value = (object?)d.MensajeAlmacen ?? DBNull.Value;
            cmd.Parameters.Add("@EmbalajeCodigo", SqlDbType.NVarChar, 100).Value = (object?)d.EmbalajeCodigo ?? DBNull.Value;
            cmd.Parameters.Add("@EmbalajeDescripcion", SqlDbType.NVarChar, 250).Value = (object?)d.EmbalajeDescripcion ?? DBNull.Value;
            AddDecimal(cmd, "@PiezasPorEmbalaje", d.PiezasPorEmbalaje, 18, 4);
            AddDecimal(cmd, "@CantidadEmbalajes", d.CantidadEmbalajes, 18, 4);
            AddDecimal(cmd, "@CantidadMpKg", d.CantidadMpKg, 18, 4);
            AddDecimal(cmd, "@CostoMPUnitario", d.CostoMPUnitario, 18, 6);
            AddDecimal(cmd, "@CostoMPTotal", d.CostoMPTotal, 18, 4);

            cmd.Parameters.Add("@MonedaCostoMP", SqlDbType.NVarChar, 20).Value =
                (object?)d.MonedaCostoMP ?? DBNull.Value;

            cmd.Parameters.Add("@UnidadCostoMP", SqlDbType.NVarChar, 30).Value =
                (object?)d.UnidadCostoMP ?? DBNull.Value;

            AddDecimal(cmd, "@CostoEmbalajeUnitario", d.CostoEmbalajeUnitario, 18, 6);
            AddDecimal(cmd, "@CostoEmbalajeTotal", d.CostoEmbalajeTotal, 18, 4);

            cmd.Parameters.Add("@MonedaCostoEmbalaje", SqlDbType.NVarChar, 20).Value =
                (object?)d.MonedaCostoEmbalaje ?? DBNull.Value;

            cmd.Parameters.Add("@UnidadCostoEmbalaje", SqlDbType.NVarChar, 30).Value =
                (object?)d.UnidadCostoEmbalaje ?? DBNull.Value;

            AddDecimal(cmd, "@CostoTotalRenglon", d.CostoTotalRenglon, 18, 4);
            AddDecimal(cmd, "@PrecioVentaUnitario", d.PrecioVentaUnitario, 18, 6);
            AddDecimal(cmd, "@VentaTotalRenglon", d.VentaTotalRenglon, 18, 4);
            AddDecimal(cmd, "@UtilidadEstimadaRenglon", d.UtilidadEstimadaRenglon, 18, 4);
            cmd.Parameters.Add("@Cambio", SqlDbType.Time).Value = (object?)d.Cambio ?? DBNull.Value;
            cmd.Parameters.Add("@Arranque", SqlDbType.Time).Value = (object?)d.Arranque ?? DBNull.Value;
            cmd.Parameters.Add("@Notas", SqlDbType.NVarChar, 500).Value = (object?)d.Notas ?? DBNull.Value;

            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
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
    p.Color,
    p.Cavidades,
    p.ObjetivoHora,
    p.PiezasPorCaja,
    p.MoldePrincipalID,

    COALESCE(p.MaterialID, t.MaterialID) AS MaterialID,
    COALESCE(NULLIF(p.MaterialCodigo, ''), t.MaterialCodigo) AS MaterialCodigo,
    COALESCE(NULLIF(p.MaterialDescripcion, ''), t.MaterialDescripcion) AS MaterialDescripcion,

    t.Ciclo,
    COALESCE(t.TipoSecado, p.TipoSecado) AS TipoSecado,
    COALESCE(t.HorasSecado, p.HorasSecado) AS HorasSecado,
    COALESCE(t.PesoBrutoPieza, p.PesoBrutoPieza) AS PesoBrutoPieza,

    COALESCE(NULLIF(t.EmbalajeCodigo, ''), p.EmbalajeCodigo) AS EmbalajeCodigo,
    COALESCE(NULLIF(t.EmbalajeDescripcion, ''), p.EmbalajeDescripcion) AS EmbalajeDescripcion,
    COALESCE(t.PiezasPorEmbalaje, p.PiezasPorEmbalaje) AS PiezasPorEmbalaje
FROM dbo.ERP_Partes p
LEFT JOIN dbo.ERP_ParteDatosTecnicos t
    ON t.ParteID = p.ParteID
   AND t.Activo = 1
WHERE p.ParteID = @ParteID;";

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


        // helpesr generales
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
    }
}