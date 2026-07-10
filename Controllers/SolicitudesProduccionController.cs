using ERP.NSQuell.Models.ERP;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers
{
    public class SolicitudesProduccionController : Controller
    {
        private readonly IConfiguration _configuration;

        public SolicitudesProduccionController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private string ConnectionString =>
            _configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("No se encontró la cadena de conexión DefaultConnection.");

        // index
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var lista = new List<SolicitudProduccionIndexVm>();

            const string sql = @"
SELECT
    s.SolicitudProduccionID,
    s.FolioSolicitud,
    s.NumeroOFRecibida,
    s.FechaSolicitud,
    s.FechaRequerida,
    ISNULL(c.Nombre, s.ClienteNombre) AS Cliente,
    s.Prioridad,
    s.EstatusID,
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
    ISNULL(c.Nombre, s.ClienteNombre),
    s.Prioridad,
    s.EstatusID,
    s.FechaCreacion
ORDER BY s.FechaCreacion DESC;";

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                var estatusID = rd.GetInt32(rd.GetOrdinal("EstatusID"));

                lista.Add(new SolicitudProduccionIndexVm
                {
                    SolicitudProduccionID = rd.GetInt32(rd.GetOrdinal("SolicitudProduccionID")),
                    FolioSolicitud = rd["FolioSolicitud"] as string,
                    NumeroOFRecibida = rd["NumeroOFRecibida"] as string,
                    FechaSolicitud = rd.GetDateTime(rd.GetOrdinal("FechaSolicitud")),
                    FechaRequerida = rd["FechaRequerida"] == DBNull.Value ? null : rd.GetDateTime(rd.GetOrdinal("FechaRequerida")),
                    Cliente = rd["Cliente"] as string,
                    Prioridad = rd["Prioridad"] as string ?? "Normal",
                    EstatusID = estatusID,
                    EstatusNombre = SolicitudProduccionEstatus.Nombre(estatusID),
                    TotalRenglones = Convert.ToInt32(rd["TotalRenglones"]),
                    TotalPiezas = Convert.ToInt32(rd["TotalPiezas"])
                });
            }

            return View(lista);
        }

        // get crear
        [HttpGet]
        public async Task<IActionResult> Crear()
        {
            var vm = new SolicitudProduccionCrearVm
            {
                FolioSolicitud = null,
                FechaSolicitud = DateTime.Today,
                Prioridad = "Normal",
                OrigenSolicitud = "Manual"
            };

            vm.Detalles.Add(new SolicitudProduccionDetalleCrearVm
            {
                Renglon = 1
            });

            await CargarCatalogosAsync(vm);

            return View(vm);
        }

        // post crear
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Crear(SolicitudProduccionCrearVm vm)
        {
            var usuarioId = ObtenerUsuarioID();

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
                var asignacionesValidas = detalle.AsignacionesMaquina
                    .Where(a => a.MaquinaID > 0 && a.CantidadAsignada > 0)
                    .ToList();

                var totalAsignado = asignacionesValidas.Sum(a => a.CantidadAsignada);

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
                    vm.FolioSolicitud = await GenerarFolioAsync(cn, (SqlTransaction)tx);
                }

                var clienteNombre = vm.ClienteNombre;

                if (vm.ClienteID.HasValue)
                {
                    clienteNombre = await ObtenerClienteNombreAsync(vm.ClienteID.Value, cn, (SqlTransaction)tx);
                }

                var solicitudId = await InsertarSolicitudAsync(vm, clienteNombre, usuarioId, cn, (SqlTransaction)tx);

                var renglon = 1;

                foreach (var d in vm.Detalles)
                {
                    await CompletarDetalleDesdeParteAsync(d, cn, (SqlTransaction)tx);

                    var detalleId = await InsertarDetalleAsync(
                        solicitudId,
                        renglon,
                        d,
                        cn,
                        (SqlTransaction)tx
                    );

                    var asignacionesValidas = d.AsignacionesMaquina
                        .Where(a => a.MaquinaID > 0 && a.CantidadAsignada > 0)
                        .ToList();

                    foreach (var a in asignacionesValidas)
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
                    SolicitudProduccionEstatus.Capturada,
                    "Creación de solicitud de producción",
                    "Solicitud capturada por Gestión Comercial.",
                    usuarioId,
                    cn,
                    (SqlTransaction)tx
                );

                await tx.CommitAsync();

                TempData["Success"] = "Solicitud de producción creada correctamente.";
                return RedirectToAction(nameof(Detalle), new { id = solicitudId });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                ModelState.AddModelError("", "Ocurrió un error al guardar la solicitud: " + ex.Message);
                await CargarCatalogosAsync(vm);
                return View(vm);
            }
        }

        // detalle
        [HttpGet]
        public async Task<IActionResult> Detalle(int id)
        {
            SolicitudProduccionDetalleVistaVm? vm = null;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            const string sqlSolicitud = @"
SELECT
    s.SolicitudProduccionID,
    s.FolioSolicitud,
    s.NumeroOFRecibida,
    s.FechaSolicitud,
    s.FechaRequerida,
    ISNULL(c.Nombre, s.ClienteNombre) AS Cliente,
    s.Prioridad,
    s.EstatusID,
    s.NotasGenerales
FROM dbo.SolicitudesProduccion s
LEFT JOIN dbo.ERP_Clientes c
    ON c.ClienteID = s.ClienteID
WHERE s.SolicitudProduccionID = @SolicitudProduccionID
  AND s.Activo = 1;";

            await using (var cmd = new SqlCommand(sqlSolicitud, cn))
            {
                cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = id;

                await using var rd = await cmd.ExecuteReaderAsync();

                if (await rd.ReadAsync())
                {
                    var estatusID = rd.GetInt32(rd.GetOrdinal("EstatusID"));

                    vm = new SolicitudProduccionDetalleVistaVm
                    {
                        SolicitudProduccionID = rd.GetInt32(rd.GetOrdinal("SolicitudProduccionID")),
                        FolioSolicitud = rd["FolioSolicitud"] as string,
                        NumeroOFRecibida = rd["NumeroOFRecibida"] as string,
                        FechaSolicitud = rd.GetDateTime(rd.GetOrdinal("FechaSolicitud")),
                        FechaRequerida = rd["FechaRequerida"] == DBNull.Value ? null : rd.GetDateTime(rd.GetOrdinal("FechaRequerida")),
                        Cliente = rd["Cliente"] as string,
                        Prioridad = rd["Prioridad"] as string ?? "Normal",
                        EstatusID = estatusID,
                        EstatusNombre = SolicitudProduccionEstatus.Nombre(estatusID),
                        NotasGenerales = rd["NotasGenerales"] as string
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
                int? estatusActual = null;

                const string sqlGet = @"
SELECT EstatusID
FROM dbo.SolicitudesProduccion
WHERE SolicitudProduccionID = @SolicitudProduccionID
  AND Activo = 1;";

                await using (var cmd = new SqlCommand(sqlGet, cn, (SqlTransaction)tx))
                {
                    cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = id;
                    var result = await cmd.ExecuteScalarAsync();

                    if (result == null || result == DBNull.Value)
                    {
                        await tx.RollbackAsync();
                        return NotFound();
                    }

                    estatusActual = Convert.ToInt32(result);
                }

                if (estatusActual >= SolicitudProduccionEstatus.EnProduccion)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "No se puede cancelar una solicitud que ya está en producción o cerrada.";
                    return RedirectToAction(nameof(Detalle), new { id });
                }

                const string sqlUpdate = @"
UPDATE dbo.SolicitudesProduccion
SET
    EstatusID = @EstatusID,
    UsuarioModificacionID = @UsuarioModificacionID,
    FechaModificacion = GETDATE()
WHERE SolicitudProduccionID = @SolicitudProduccionID;";

                await using (var cmd = new SqlCommand(sqlUpdate, cn, (SqlTransaction)tx))
                {
                    cmd.Parameters.Add("@EstatusID", SqlDbType.Int).Value = SolicitudProduccionEstatus.Cancelada;
                    cmd.Parameters.Add("@UsuarioModificacionID", SqlDbType.Int).Value = usuarioId;
                    cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = id;

                    await cmd.ExecuteNonQueryAsync();
                }

                await InsertarHistorialAsync(
                    id,
                    estatusActual,
                    SolicitudProduccionEstatus.Cancelada,
                    "Cancelación de solicitud",
                    comentario,
                    usuarioId,
                    cn,
                    (SqlTransaction)tx
                );

                await tx.CommitAsync();

                TempData["Success"] = "Solicitud cancelada correctamente.";
                return RedirectToAction(nameof(Detalle), new { id });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] = "Error al cancelar: " + ex.Message;
                return RedirectToAction(nameof(Detalle), new { id });
            }
        }

        // ajax para oobtener la informacion de una parte
        [HttpGet]
        public async Task<IActionResult> ObtenerParteInfo(int parteId)
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            const string sql = @"
SELECT
    p.ParteID,
    p.ClienteID,
    p.NumeroParte,
    p.ReferenciaSAP,
    p.Descripcion,
    p.Designacion,
    p.Color,
    p.Cavidades,
    p.ObjetivoHora,
    p.PiezasPorCaja,
    p.MaquinaPrincipalID,
    p.MaquinaSustitutaID,
    p.MoldePrincipalID,
    m.CodigoMolde AS MoldeCodigo
FROM dbo.ERP_Partes p
LEFT JOIN dbo.ERP_Moldes m
    ON m.MoldeID = p.MoldePrincipalID
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
            var designacion = rd["Designacion"] as string;
            var descripcion = rd["Descripcion"] as string ?? "";

            return Json(new
            {
                ok = true,
                parteID = Convert.ToInt32(rd["ParteID"]),
                clienteID = Convert.ToInt32(rd["ClienteID"]),
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
                maquinaSustitutaID = rd["MaquinaSustitutaID"] == DBNull.Value ? null : rd["MaquinaSustitutaID"]
            });
        }

        // helpers
        private async Task<int> InsertarSolicitudAsync(
            SolicitudProduccionCrearVm vm,
            string? clienteNombre,
            int usuarioId,
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
    ClienteID,
    ClienteNombre,
    OrigenSolicitud,
    Prioridad,
    EstatusID,
    NotasGenerales,
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
    @ClienteID,
    @ClienteNombre,
    @OrigenSolicitud,
    @Prioridad,
    @EstatusID,
    @NotasGenerales,
    @UsuarioCreacionID,
    GETDATE(),
    1
);";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@FolioSolicitud", SqlDbType.NVarChar, 30).Value = (object?)vm.FolioSolicitud ?? DBNull.Value;
            cmd.Parameters.Add("@NumeroOFRecibida", SqlDbType.NVarChar, 80).Value = (object?)vm.NumeroOFRecibida ?? DBNull.Value;
            cmd.Parameters.Add("@FechaSolicitud", SqlDbType.Date).Value = vm.FechaSolicitud.Date;
            cmd.Parameters.Add("@FechaRequerida", SqlDbType.Date).Value = (object?)vm.FechaRequerida?.Date ?? DBNull.Value;
            cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = (object?)vm.ClienteID ?? DBNull.Value;
            cmd.Parameters.Add("@ClienteNombre", SqlDbType.NVarChar, 200).Value = (object?)clienteNombre ?? DBNull.Value;
            cmd.Parameters.Add("@OrigenSolicitud", SqlDbType.NVarChar, 50).Value = (object?)vm.OrigenSolicitud ?? DBNull.Value;
            cmd.Parameters.Add("@Prioridad", SqlDbType.NVarChar, 30).Value = string.IsNullOrWhiteSpace(vm.Prioridad) ? "Normal" : vm.Prioridad;
            cmd.Parameters.Add("@EstatusID", SqlDbType.Int).Value = SolicitudProduccionEstatus.Capturada;
            cmd.Parameters.Add("@NotasGenerales", SqlDbType.NVarChar).Value = (object?)vm.NotasGenerales ?? DBNull.Value;
            cmd.Parameters.Add("@UsuarioCreacionID", SqlDbType.Int).Value = usuarioId;

            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        private async Task<int> InsertarDetalleAsync(
            int solicitudId,
            int renglon,
            SolicitudProduccionDetalleCrearVm d,
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
            cmd.Parameters.Add("@HorasPlaneadas", SqlDbType.Decimal).Value = (object?)d.HorasPlaneadas ?? DBNull.Value;
            cmd.Parameters["@HorasPlaneadas"].Precision = 10;
            cmd.Parameters["@HorasPlaneadas"].Scale = 2;
            cmd.Parameters.Add("@NumeroMoldeTexto", SqlDbType.NVarChar, 100).Value = (object?)d.NumeroMoldeTexto ?? DBNull.Value;
            cmd.Parameters.Add("@Color", SqlDbType.NVarChar, 80).Value = (object?)d.Color ?? DBNull.Value;
            cmd.Parameters.Add("@Cavidades", SqlDbType.Int).Value = (object?)d.Cavidades ?? DBNull.Value;
            cmd.Parameters.Add("@ObjetivoHora", SqlDbType.Int).Value = (object?)d.ObjetivoHora ?? DBNull.Value;
            cmd.Parameters.Add("@PiezasPorCaja", SqlDbType.Int).Value = (object?)d.PiezasPorCaja ?? DBNull.Value;
            cmd.Parameters.Add("@Notas", SqlDbType.NVarChar, 500).Value = (object?)d.Notas ?? DBNull.Value;

            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        private async Task InsertarAsignacionMaquinaAsync(
            int detalleId,
            SolicitudProduccionAsignacionMaquinaCrearVm a,
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
            cmd.Parameters.Add("@HorasEstimadas", SqlDbType.Decimal).Value = (object?)a.HorasEstimadas ?? DBNull.Value;
            cmd.Parameters["@HorasEstimadas"].Precision = 10;
            cmd.Parameters["@HorasEstimadas"].Scale = 2;
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

   
        private async Task CargarCatalogosAsync(SolicitudProduccionCrearVm vm)
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            vm.Clientes = await CargarSelectAsync(
                cn,
                "SELECT ClienteID AS Id, Nombre AS Texto FROM dbo.ERP_Clientes WHERE Activo = 1 ORDER BY Nombre;"
            );

            vm.Partes = await CargarSelectAsync(
                cn,
                "SELECT ParteID AS Id, NumeroParte + ' - ' + Descripcion AS Texto FROM dbo.ERP_Partes WHERE Activo = 1 ORDER BY NumeroParte;"
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
            const string sql = @"
SELECT Nombre
FROM dbo.ERP_Clientes
WHERE ClienteID = @ClienteID;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = clienteId;

            return await cmd.ExecuteScalarAsync() as string;
        }

        private async Task CompletarDetalleDesdeParteAsync(
            SolicitudProduccionDetalleCrearVm d,
            SqlConnection cn,
            SqlTransaction tx)
        {
            if (!d.ParteID.HasValue)
            {
                return;
            }

            const string sql = @"
SELECT
    NumeroParte,
    ReferenciaSAP,
    Descripcion,
    Designacion,
    Color,
    Cavidades,
    ObjetivoHora,
    PiezasPorCaja,
    MoldePrincipalID
FROM dbo.ERP_Partes
WHERE ParteID = @ParteID;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = d.ParteID.Value;

            await using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
            {
                return;
            }

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
        }

        private async Task<List<SolicitudProduccionDetalleVistaRenglonVm>> ObtenerDetallesAsync(int solicitudId, SqlConnection cn)
        {
            var lista = new List<SolicitudProduccionDetalleVistaRenglonVm>();

            const string sql = @"
SELECT
    d.SolicitudProduccionDetalleID,
    d.Renglon,
    d.ReferenciaSAP,
    d.DesignacionDescripcionSAP,
    d.CantidadPiezas,
    ISNULL(m.CodigoMolde, d.NumeroMoldeTexto) AS Molde,
    d.Color,
    d.Cavidades,
    d.ObjetivoHora,
    d.PiezasPorCaja,
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
                lista.Add(new SolicitudProduccionDetalleVistaRenglonVm
                {
                    SolicitudProduccionDetalleID = Convert.ToInt32(rd["SolicitudProduccionDetalleID"]),
                    Renglon = Convert.ToInt32(rd["Renglon"]),
                    ReferenciaSAP = rd["ReferenciaSAP"] as string ?? "",
                    DesignacionDescripcionSAP = rd["DesignacionDescripcionSAP"] as string ?? "",
                    CantidadPiezas = Convert.ToInt32(rd["CantidadPiezas"]),
                    Molde = rd["Molde"] as string,
                    Color = rd["Color"] as string,
                    Cavidades = rd["Cavidades"] == DBNull.Value ? null : Convert.ToInt32(rd["Cavidades"]),
                    ObjetivoHora = rd["ObjetivoHora"] == DBNull.Value ? null : Convert.ToInt32(rd["ObjetivoHora"]),
                    PiezasPorCaja = rd["PiezasPorCaja"] == DBNull.Value ? null : Convert.ToInt32(rd["PiezasPorCaja"]),
                    Notas = rd["Notas"] as string
                });
            }

            return lista;
        }

        private async Task<List<SolicitudProduccionAsignacionMaquinaVistaVm>> ObtenerAsignacionesAsync(int detalleId, SqlConnection cn)
        {
            var lista = new List<SolicitudProduccionAsignacionMaquinaVistaVm>();

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
                lista.Add(new SolicitudProduccionAsignacionMaquinaVistaVm
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

        private async Task<List<SolicitudProduccionHistorialVistaVm>> ObtenerHistorialAsync(int solicitudId, SqlConnection cn)
        {
            var lista = new List<SolicitudProduccionHistorialVistaVm>();

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
                lista.Add(new SolicitudProduccionHistorialVistaVm
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

        private async Task<string> GenerarFolioAsync()
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            return await GenerarFolioAsync(cn, null);
        }

        private async Task<string> GenerarFolioAsync(SqlConnection cn, SqlTransaction? tx)
        {
            const string sql = "SELECT NEXT VALUE FOR dbo.SEQ_SolicitudesProduccion;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            var consecutivo = Convert.ToInt32(await cmd.ExecuteScalarAsync());

            return $"SP-{consecutivo:000000}";
        }

        private int ObtenerUsuarioID()
        {
            return HttpContext.Session.GetInt32("UsuarioID") ?? 0;
        }
    }
}