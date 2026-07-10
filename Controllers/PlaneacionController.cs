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

        // ============================================================
        // INDEX
        // ============================================================
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

        // ============================================================
        // CREAR GET
        // ============================================================
        [HttpGet]
        public async Task<IActionResult> Crear()
        {
            var vm = new PlaneacionOFCrearVm
            {
                FolioSolicitud = null,
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

        // ============================================================
        // CREAR POST
        // ============================================================
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

        // ============================================================
        // DETALLE
        // ============================================================
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

        // ============================================================
        // CANCELAR
        // ============================================================
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

        // ============================================================
        // AJAX: OBTENER INFO DE PARTE
        // ============================================================
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
    t.Ciclo,
    t.TipoSecado,
    t.HorasSecado,
    t.PesoBrutoPieza,
    t.MaterialCodigo,
    t.MaterialDescripcion,
    t.EmbalajeCodigo,
    t.EmbalajeDescripcion,
    t.PiezasPorEmbalaje
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

                materialCodigo = rd["MaterialCodigo"] == DBNull.Value ? null : rd["MaterialCodigo"],
                materialDescripcion = rd["MaterialDescripcion"] == DBNull.Value ? null : rd["MaterialDescripcion"],

                embalajeCodigo = rd["EmbalajeCodigo"] == DBNull.Value ? null : rd["EmbalajeCodigo"],
                embalajeDescripcion = rd["EmbalajeDescripcion"] == DBNull.Value ? null : rd["EmbalajeDescripcion"],
                piezasPorEmbalaje = rd["PiezasPorEmbalaje"] == DBNull.Value ? null : rd["PiezasPorEmbalaje"]
            });
        }

        // ============================================================
        // AJAX: CALCULAR DATOS
        // ============================================================
        [HttpGet]
        public IActionResult CalcularDatosOF(
            int? cantidadPiezas,
            decimal? horasPlaneadas,
            int? objetivoHora,
            decimal? piezasPorEmbalaje,
            decimal? pesoBrutoPieza)
        {
            var cantidad = cantidadPiezas ?? 0;

            if (cantidad <= 0 && horasPlaneadas.HasValue && objetivoHora.HasValue)
            {
                cantidad = Convert.ToInt32(Math.Ceiling(horasPlaneadas.Value * objetivoHora.Value));
            }

            decimal? cantidadEmbalajes = null;
            decimal? cantidadMpKg = null;

            if (cantidad > 0 && piezasPorEmbalaje.HasValue && piezasPorEmbalaje.Value > 0)
            {
                cantidadEmbalajes = Math.Ceiling(cantidad / piezasPorEmbalaje.Value);
            }

            if (cantidad > 0 && pesoBrutoPieza.HasValue && pesoBrutoPieza.Value > 0)
            {
                cantidadMpKg = Math.Round(cantidad * pesoBrutoPieza.Value, 4);
            }

            return Json(new
            {
                ok = true,
                cantidadPiezas = cantidad,
                cantidadEmbalajes,
                cantidadMpKg
            });
        }

        // ============================================================
        // INSERTS
        // ============================================================
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
    EmbalajeCodigo,
    EmbalajeDescripcion,
    PiezasPorEmbalaje,
    CantidadEmbalajes,
    CantidadMpKg,
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
    @EmbalajeCodigo,
    @EmbalajeDescripcion,
    @PiezasPorEmbalaje,
    @CantidadEmbalajes,
    @CantidadMpKg,
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
            cmd.Parameters.Add("@EmbalajeCodigo", SqlDbType.NVarChar, 100).Value = (object?)d.EmbalajeCodigo ?? DBNull.Value;
            cmd.Parameters.Add("@EmbalajeDescripcion", SqlDbType.NVarChar, 250).Value = (object?)d.EmbalajeDescripcion ?? DBNull.Value;
            AddDecimal(cmd, "@PiezasPorEmbalaje", d.PiezasPorEmbalaje, 18, 4);
            AddDecimal(cmd, "@CantidadEmbalajes", d.CantidadEmbalajes, 18, 4);
            AddDecimal(cmd, "@CantidadMpKg", d.CantidadMpKg, 18, 4);
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

        // ============================================================
        // CONSULTAS
        // ============================================================
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
    t.Ciclo,
    t.TipoSecado,
    t.HorasSecado,
    t.PesoBrutoPieza,
    t.MaterialCodigo,
    t.MaterialDescripcion,
    t.EmbalajeCodigo,
    t.EmbalajeDescripcion,
    t.PiezasPorEmbalaje
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
            if (d.CantidadPiezas <= 0 && d.HorasPlaneadas.HasValue && d.ObjetivoHora.HasValue)
            {
                d.CantidadPiezas = Convert.ToInt32(Math.Ceiling(d.HorasPlaneadas.Value * d.ObjetivoHora.Value));
            }

            if (d.CantidadPiezas > 0 && d.PiezasPorEmbalaje.HasValue && d.PiezasPorEmbalaje.Value > 0)
            {
                d.CantidadEmbalajes = Math.Ceiling(d.CantidadPiezas / d.PiezasPorEmbalaje.Value);
            }

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

        // ============================================================
        // HELPERS GENERALES
        // ============================================================
        private async Task<string> GenerarFolioOFAsync(SqlConnection cn, SqlTransaction tx)
        {
            const string sql = "SELECT NEXT VALUE FOR dbo.SEQ_SolicitudesProduccion;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            var consecutivo = Convert.ToInt32(await cmd.ExecuteScalarAsync());

            var yy = DateTime.Now.ToString("yy");

            return $"OF-{consecutivo:00000}/{yy}";
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