using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using System.Data;
using System.IO;
using System.Security.Claims;

namespace ERP.NSQuell.Controllers
{
    public class ComprasController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;

        public ComprasController(IConfiguration configuration, IWebHostEnvironment environment)
        {
            _configuration = configuration;
            _environment = environment;
        }

        private string GetConnectionString()
        {
            return _configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("No se encontró la cadena de conexión 'DefaultConnection'.");
        }

        // =========================================================
        // INDEX / DASHBOARD
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var model = new Compras.DashboardViewModel();

            using var connection = new SqlConnection(GetConnectionString());
            await connection.OpenAsync();

            var query = @"
                SELECT
                    SolicitudesPendientes = SUM(CASE WHEN EstatusID = 1 THEN 1 ELSE 0 END),
                    SolicitudesEnRevision = SUM(CASE WHEN EstatusID IN (2, 3, 4, 5) THEN 1 ELSE 0 END),
                    SolicitudesAprobadas = SUM(CASE WHEN EstatusID IN (2, 3, 4, 5, 6, 7, 8, 9, 10) THEN 1 ELSE 0 END),
                    SolicitudesRechazadas = SUM(CASE WHEN EstatusID = 11 THEN 1 ELSE 0 END),
                    RecepcionesPendientes = SUM(CASE WHEN EstatusID = 8 THEN 1 ELSE 0 END)
                FROM dbo.vw_ComprasSolicitudes_Flujo
                WHERE Activo = 1;

                SELECT
                    OrdenesPendientes = SUM(CASE WHEN Estatus = 'Pendiente' AND Activo = 1 THEN 1 ELSE 0 END),
                    OrdenesEnEsperaPago = SUM(CASE WHEN Estatus = 'En espera de pago' AND Activo = 1 THEN 1 ELSE 0 END),
                    OrdenesRecibidasParcial = SUM(CASE WHEN Estatus = 'Recibida parcial' AND Activo = 1 THEN 1 ELSE 0 END),
                    OrdenesCerradas = SUM(CASE WHEN Estatus = 'Cerrada' AND Activo = 1 THEN 1 ELSE 0 END)
                FROM dbo.ComprasOrdenes;

                SELECT
                    ProveedoresActivos = COUNT(*)
                FROM dbo.ERP_Proveedores
                WHERE Activo = 1;
            ";

            using var command = new SqlCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                model.SolicitudesPendientes = GetInt(reader, "SolicitudesPendientes");
                model.SolicitudesEnRevision = GetInt(reader, "SolicitudesEnRevision");
                model.SolicitudesAprobadas = GetInt(reader, "SolicitudesAprobadas");
                model.SolicitudesRechazadas = GetInt(reader, "SolicitudesRechazadas");
                model.RecepcionesPendientes = GetInt(reader, "RecepcionesPendientes");
            }

            if (await reader.NextResultAsync() && await reader.ReadAsync())
            {
                model.OrdenesPendientes = GetInt(reader, "OrdenesPendientes");
                model.OrdenesEnEsperaPago = GetInt(reader, "OrdenesEnEsperaPago");
                model.OrdenesRecibidasParcial = GetInt(reader, "OrdenesRecibidasParcial");
                model.OrdenesCerradas = GetInt(reader, "OrdenesCerradas");
            }

            if (await reader.NextResultAsync() && await reader.ReadAsync())
            {
                model.ProveedoresActivos = GetInt(reader, "ProveedoresActivos");
            }

            reader.Close();

            model.UltimasSolicitudes = await ObtenerSolicitudesAsync(5);

            return View(model);
        }

        // =========================================================
        // LISTADO GENERAL DE SOLICITUDES
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Solicitudes()
        {
            var model = await ObtenerSolicitudesAsync();
            return View(model);
        }

        // =========================================================
        // BANDEJA DIRECCION
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> BandejaDireccion()
        {
            var solicitudes = await ObtenerSolicitudesBandejaAsync();

            var model = new Compras.BandejaDireccionViewModel
            {
                PendientesAprobacion = solicitudes
                    .Where(x => x.EstatusID == 1)
                    .OrderByDescending(x => x.FechaSolicitud)
                    .ToList(),

                PendientesCotizacion = solicitudes
                    .Where(x => x.EstatusID == 4)
                    .OrderByDescending(x => x.FechaSolicitud)
                    .ToList(),

                Historico = solicitudes
                    .Where(x => x.EstatusID == 5 || x.EstatusID == 10 || x.EstatusID == 11 || x.EstatusID == 12)
                    .OrderByDescending(x => x.FechaSolicitud)
                    .ToList()
            };

            return View(model);
        }

        // =========================================================
        // BANDEJA COMPRAS
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> BandejaCompras()
        {
            var solicitudes = await ObtenerSolicitudesBandejaAsync();

            var model = new Compras.BandejaComprasViewModel
            {
                AprobadasParaCotizar = solicitudes
                    .Where(x => x.EstatusID == 2)
                    .OrderByDescending(x => x.FechaSolicitud)
                    .ToList(),

                Cotizando = solicitudes
                    .Where(x => x.EstatusID == 3 || x.EstatusID == 4)
                    .OrderByDescending(x => x.FechaSolicitud)
                    .ToList(),

                ParaOrdenCompra = solicitudes
                    .Where(x => x.EstatusID == 5)
                    .OrderByDescending(x => x.FechaSolicitud)
                    .ToList(),

                OrdenesGeneradas = solicitudes
                    .Where(x => x.EstatusID == 6 || x.EstatusID == 7)
                    .OrderByDescending(x => x.FechaSolicitud)
                    .ToList(),

                Historico = solicitudes
                    .Where(x => x.EstatusID == 8 || x.EstatusID == 9 || x.EstatusID == 10 || x.EstatusID == 11 || x.EstatusID == 12)
                    .OrderByDescending(x => x.FechaSolicitud)
                    .ToList()
            };

            return View(model);
        }

        // =========================================================
        // BANDEJA ALMACEN
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> BandejaAlmacen()
        {
            var solicitudes = await ObtenerSolicitudesBandejaAsync();

            var model = new Compras.BandejaAlmacenViewModel
            {
                PendientesRecepcion = solicitudes
                    .Where(x => x.EstatusID == 8)
                    .OrderByDescending(x => x.FechaSolicitud)
                    .ToList(),

                Historico = solicitudes
                    .Where(x => x.EstatusID == 9 || x.EstatusID == 10)
                    .OrderByDescending(x => x.FechaSolicitud)
                    .ToList()
            };

            return View(model);
        }

        // =========================================================
        // CREAR SOLICITUD - GET
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> CrearSolicitud()
        {
            var model = new Compras.CrearSolicitudViewModel
            {
                OrigenSolicitud = "Almacen",
                Prioridad = "Normal",
                TipoCompra = "Materia prima",
                PedidoClienteReferencia = null,
                Materiales = new List<Compras.SolicitudDetalleItemViewModel>
                {
                    new Compras.SolicitudDetalleItemViewModel()
                }
            };

            await CargarCatalogosSolicitudAsync(model);

            return View(model);
        }

        // =========================================================
        // CREAR SOLICITUD - POST
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CrearSolicitud(Compras.CrearSolicitudViewModel model)
        {
            model.OrigenSolicitud = "Almacen";
            model.AlmacenID = null;
            model.PedidoClienteReferencia = null;

            model.Materiales ??= new List<Compras.SolicitudDetalleItemViewModel>();

            var keysToRemove = ModelState.Keys
                .Where(k =>
                    k == "OrigenSolicitud" ||
                    k == "AlmacenID" ||
                    k == "PedidoClienteReferencia" ||
                    k.EndsWith(".DescripcionMaterial") ||
                    k.EndsWith(".UnidadMedida") ||
                    k.EndsWith(".StockActual") ||
                    k.EndsWith(".StockMinimo") ||
                    k.EndsWith(".CodigoMaterial") ||
                    k.EndsWith(".NombreMaterial"))
                .ToList();

            foreach (var key in keysToRemove)
            {
                ModelState.Remove(key);
            }

            model.Materiales = model.Materiales
                .Where(x =>
                    x.ProductoID.HasValue ||
                    x.CantidadSolicitada > 0 ||
                    !string.IsNullOrWhiteSpace(x.Observaciones))
                .ToList();

            if (!model.Materiales.Any())
            {
                ModelState.AddModelError("", "Debe agregar al menos un material o concepto a la solicitud.");
            }

            if (string.IsNullOrWhiteSpace(model.TipoCompra))
            {
                ModelState.AddModelError(nameof(model.TipoCompra), "Seleccione el tipo de compra.");
            }

            for (int i = 0; i < model.Materiales.Count; i++)
            {
                if (!model.Materiales[i].ProductoID.HasValue || model.Materiales[i].ProductoID.Value <= 0)
                {
                    ModelState.AddModelError($"Materiales[{i}].ProductoID", "Seleccione un material o concepto.");
                }

                if (model.Materiales[i].CantidadSolicitada <= 0)
                {
                    ModelState.AddModelError($"Materiales[{i}].CantidadSolicitada", "La cantidad solicitada debe ser mayor a 0.");
                }
            }

            if (!ModelState.IsValid)
            {
                await CargarCatalogosSolicitudAsync(model);

                if (!model.Materiales.Any())
                {
                    model.Materiales.Add(new Compras.SolicitudDetalleItemViewModel());
                }

                return View(model);
            }

            var usuarioId = ObtenerUsuarioId();

            using var connection = new SqlConnection(GetConnectionString());
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();

            try
            {
                var insertSolicitud = @"
                    INSERT INTO dbo.ComprasSolicitudes
                    (
                        Folio,
                        DepartamentoID,
                        SolicitadoPorUsuarioID,
                        FechaSolicitud,
                        OrigenSolicitud,
                        PedidoClienteReferencia,
                        Prioridad,
                        TipoCompra,
                        Motivo,
                        EstatusID,
                        Estatus,
                        Observaciones,
                        Activo
                    )
                    OUTPUT INSERTED.SolicitudCompraID
                    VALUES
                    (
                        NULL,
                        @DepartamentoID,
                        @SolicitadoPorUsuarioID,
                        GETDATE(),
                        @OrigenSolicitud,
                        @PedidoClienteReferencia,
                        @Prioridad,
                        @TipoCompra,
                        @Motivo,
                        1,
                        'Pendiente de aprobación',
                        @Observaciones,
                        1
                    );
                ";

                int solicitudId;

                using (var command = new SqlCommand(insertSolicitud, connection, transaction))
                {
                    command.Parameters.AddWithValue("@DepartamentoID", ToDbValue(model.DepartamentoID));
                    command.Parameters.AddWithValue("@SolicitadoPorUsuarioID", ToDbValue(usuarioId));
                    command.Parameters.AddWithValue("@OrigenSolicitud", ToDbValue(model.OrigenSolicitud));
                    command.Parameters.AddWithValue("@PedidoClienteReferencia", ToDbValue(model.PedidoClienteReferencia));
                    command.Parameters.AddWithValue("@Prioridad", ToDbValue(model.Prioridad));
                    command.Parameters.AddWithValue("@TipoCompra", ToDbValue(model.TipoCompra));
                    command.Parameters.AddWithValue("@Motivo", ToDbValue(model.Motivo));
                    command.Parameters.AddWithValue("@Observaciones", ToDbValue(model.Observaciones));

                    solicitudId = Convert.ToInt32(await command.ExecuteScalarAsync());
                }

                var folio = $"SC-{DateTime.Now:yyyy}-{solicitudId.ToString().PadLeft(5, '0')}";

                var updateFolio = @"
                    UPDATE dbo.ComprasSolicitudes
                    SET Folio = @Folio
                    WHERE SolicitudCompraID = @SolicitudCompraID;
                ";

                using (var command = new SqlCommand(updateFolio, connection, transaction))
                {
                    command.Parameters.AddWithValue("@Folio", folio);
                    command.Parameters.AddWithValue("@SolicitudCompraID", solicitudId);

                    await command.ExecuteNonQueryAsync();
                }

                var insertDetalle = @"
                    INSERT INTO dbo.ComprasSolicitudDetalle
                    (
                        SolicitudCompraID,
                        ProductoID,
                        DescripcionMaterial,
                        UnidadMedida,
                        CantidadSolicitada,
                        StockActual,
                        StockMinimo,
                        FechaRequerida,
                        AceptaSustituto,
                        Observaciones,
                        Activo
                    )
                    VALUES
                    (
                        @SolicitudCompraID,
                        @ProductoID,
                        @DescripcionMaterial,
                        @UnidadMedida,
                        @CantidadSolicitada,
                        @StockActual,
                        @StockMinimo,
                        @FechaRequerida,
                        @AceptaSustituto,
                        @Observaciones,
                        1
                    );
                ";

                foreach (var item in model.Materiales)
                {
                    if (!item.ProductoID.HasValue || item.ProductoID.Value <= 0)
                    {
                        throw new InvalidOperationException("Uno de los materiales o conceptos no tiene MaterialID.");
                    }

                    var material = await ObtenerMaterialCompraAsync(connection, item.ProductoID.Value, transaction);

                    if (material == null)
                    {
                        throw new InvalidOperationException("No se encontró información del material o concepto seleccionado.");
                    }

                    using var command = new SqlCommand(insertDetalle, connection, transaction);

                    command.Parameters.AddWithValue("@SolicitudCompraID", solicitudId);
                    command.Parameters.AddWithValue("@ProductoID", material.MaterialID);
                    command.Parameters.AddWithValue("@DescripcionMaterial", ToDbValue(material.Material));
                    command.Parameters.AddWithValue("@UnidadMedida", ToDbValue(material.Unidad));
                    command.Parameters.AddWithValue("@CantidadSolicitada", item.CantidadSolicitada);
                    command.Parameters.AddWithValue("@StockActual", material.StockActual);
                    command.Parameters.AddWithValue("@StockMinimo", material.StockMinimo);
                    command.Parameters.AddWithValue("@FechaRequerida", ToDbValue(item.FechaRequerida));
                    command.Parameters.AddWithValue("@AceptaSustituto", item.AceptaSustituto);
                    command.Parameters.AddWithValue("@Observaciones", ToDbValue(item.Observaciones));

                    await command.ExecuteNonQueryAsync();
                }

                await RegistrarHistorialAsync(
                    connection,
                    transaction,
                    solicitudId,
                    1,
                    "Solicitud creada y enviada a Dirección para aprobación.",
                    usuarioId
                );

                transaction.Commit();

                TempData["Success"] = $"Solicitud {folio} creada correctamente.";

                return RedirectToAction(nameof(DetalleSolicitud), new { id = solicitudId });
            }
            catch (Exception ex)
            {
                transaction.Rollback();

                ModelState.AddModelError("", "Ocurrió un error al guardar la solicitud: " + ex.Message);

                await CargarCatalogosSolicitudAsync(model);

                if (!model.Materiales.Any())
                {
                    model.Materiales.Add(new Compras.SolicitudDetalleItemViewModel());
                }

                return View(model);
            }
        }

        // =========================================================
        // DETALLE SOLICITUD
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> DetalleSolicitud(int id)
        {
            using var connection = new SqlConnection(GetConnectionString());
            await connection.OpenAsync();

            var querySolicitud = @"
                SELECT
                    SolicitudCompraID,
                    Folio,
                    DepartamentoID,
                    Departamento,
                    SolicitadoPorUsuarioID,
                    FechaSolicitud,
                    OrigenSolicitud,
                    Prioridad,
                    TipoCompra,
                    Motivo,
                    Observaciones,
                    EstatusID,
                    EstatusNombre,
                    ResponsableActual,
                    Activo,
                    FechaUltimoMovimiento,
                    DiasEnEstatus,
                    AprobadoPorUsuarioID,
                    FechaAprobacion,
                    RechazadoPorUsuarioID,
                    FechaRechazo,
                    MotivoRechazo,
                    CompradorAsignadoUsuarioID,
                    FechaAsignacionComprador,
                    CotizacionSeleccionadaID,
                    FechaSeleccionCotizacion,
                    UsuarioSeleccionCotizacionID,
                    ComentariosSeleccionCotizacion,
                    FechaCierre,
                    CerradoPorUsuarioID
                FROM dbo.vw_ComprasSolicitudes_Flujo
                WHERE SolicitudCompraID = @SolicitudCompraID
                  AND Activo = 1;
            ";

            Compras.DetalleSolicitudViewModel? model = null;

            using (var command = new SqlCommand(querySolicitud, connection))
            {
                command.Parameters.AddWithValue("@SolicitudCompraID", id);

                using var reader = await command.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    model = new Compras.DetalleSolicitudViewModel
                    {
                        SolicitudCompraID = GetInt(reader, "SolicitudCompraID"),
                        Folio = GetString(reader, "Folio"),

                        DepartamentoID = GetNullableInt(reader, "DepartamentoID"),
                        Departamento = GetString(reader, "Departamento"),

                        AlmacenID = null,
                        Almacen = "Almacén principal",

                        SolicitadoPorUsuarioID = GetNullableInt(reader, "SolicitadoPorUsuarioID"),
                        Solicitante = GetNullableInt(reader, "SolicitadoPorUsuarioID").HasValue
                            ? $"Usuario ID {GetNullableInt(reader, "SolicitadoPorUsuarioID")}"
                            : null,

                        FechaSolicitud = GetDateTime(reader, "FechaSolicitud"),

                        OrigenSolicitud = GetString(reader, "OrigenSolicitud") ?? "Almacen",
                        PedidoClienteReferencia = null,

                        Prioridad = GetString(reader, "Prioridad"),
                        TipoCompra = GetString(reader, "TipoCompra"),

                        Motivo = GetString(reader, "Motivo"),

                        EstatusID = GetInt(reader, "EstatusID"),
                        Estatus = GetString(reader, "EstatusNombre"),
                        EstatusNombre = GetString(reader, "EstatusNombre"),
                        ResponsableActual = GetString(reader, "ResponsableActual"),

                        Observaciones = GetString(reader, "Observaciones"),

                        Activo = GetBool(reader, "Activo"),

                        FechaUltimoMovimiento = GetNullableDateTime(reader, "FechaUltimoMovimiento"),
                        DiasEnEstatus = GetInt(reader, "DiasEnEstatus"),

                        AprobadoPorUsuarioID = GetNullableInt(reader, "AprobadoPorUsuarioID"),
                        FechaAprobacion = GetNullableDateTime(reader, "FechaAprobacion"),

                        RechazadoPorUsuarioID = GetNullableInt(reader, "RechazadoPorUsuarioID"),
                        FechaRechazo = GetNullableDateTime(reader, "FechaRechazo"),
                        MotivoRechazo = GetString(reader, "MotivoRechazo"),

                        CompradorAsignadoUsuarioID = GetNullableInt(reader, "CompradorAsignadoUsuarioID"),
                        FechaAsignacionComprador = GetNullableDateTime(reader, "FechaAsignacionComprador"),

                        CotizacionSeleccionadaID = GetNullableInt(reader, "CotizacionSeleccionadaID"),
                        FechaSeleccionCotizacion = GetNullableDateTime(reader, "FechaSeleccionCotizacion"),
                        UsuarioSeleccionCotizacionID = GetNullableInt(reader, "UsuarioSeleccionCotizacionID"),
                        ComentariosSeleccionCotizacion = GetString(reader, "ComentariosSeleccionCotizacion"),

                        FechaCierre = GetNullableDateTime(reader, "FechaCierre"),
                        CerradoPorUsuarioID = GetNullableInt(reader, "CerradoPorUsuarioID"),

                        Materiales = new List<Compras.SolicitudDetalleItemViewModel>(),
                        Cotizaciones = new List<Compras.CotizacionCompraViewModel>(),
                        Historial = new List<Compras.HistorialCompraViewModel>()
                    };
                }
            }

            if (model == null)
            {
                TempData["Error"] = "No se encontró la solicitud seleccionada.";
                return RedirectToAction(nameof(Solicitudes));
            }

            model.Materiales = await ObtenerDetalleMaterialesAsync(connection, id);
            model.Cotizaciones = await ObtenerCotizacionesSolicitudAsync(connection, id);
            model.OrdenCompra = await ObtenerOrdenCompraSolicitudAsync(connection, id);
            model.Recepcion = await ObtenerRecepcionSolicitudAsync(connection, id);
            model.Historial = await ObtenerHistorialSolicitudAsync(connection, id);
            model.Proveedores = await ObtenerProveedoresAsync(connection);

            return View(model);
        }

        // =========================================================
        // OBTENER MATERIAL AJAX
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> ObtenerMaterial(int materialId)
        {
            using var connection = new SqlConnection(GetConnectionString());
            await connection.OpenAsync();

            var material = await ObtenerMaterialCompraAsync(connection, materialId);

            if (material == null)
            {
                return Json(new
                {
                    ok = false,
                    mensaje = "No se encontró el material o concepto seleccionado."
                });
            }

            return Json(new
            {
                ok = true,
                materialID = material.MaterialID,
                codigoMaterial = material.CodigoMaterial,
                material = material.Material,
                tipoMaterial = material.TipoMaterial,
                unidad = material.Unidad,
                stockActual = material.StockActual,
                stockMinimo = material.StockMinimo
            });
        }

        // =========================================================
        // APROBAR SOLICITUD DIRECCION
        // Estatus 1 -> 2
        // Estatus 4 -> 5
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AprobarSolicitud(Compras.AprobarSolicitudViewModel model)
        {
            var usuarioId = ObtenerUsuarioId();

            using var connection = new SqlConnection(GetConnectionString());
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();

            try
            {
                var estatusActual = await ObtenerEstatusActualAsync(connection, transaction, model.SolicitudCompraID);

                if (estatusActual != 1 && estatusActual != 4)
                {
                    TempData["Error"] = "La solicitud no se encuentra en un estatus que pueda aprobar Dirección.";
                    transaction.Rollback();
                    return RedirectToAction(nameof(DetalleSolicitud), new { id = model.SolicitudCompraID });
                }

                var nuevoEstatusID = estatusActual == 1 ? 2 : 5;
                var nuevoEstatusNombre = await ObtenerNombreEstatusAsync(connection, transaction, nuevoEstatusID);

                if (estatusActual == 1)
                {
                    var query = @"
                        UPDATE dbo.ComprasSolicitudes
                        SET
                            EstatusID = @EstatusID,
                            Estatus = @Estatus,
                            AprobadoPorUsuarioID = @UsuarioID,
                            FechaAprobacion = GETDATE(),
                            CompradorAsignadoUsuarioID = @CompradorAsignadoUsuarioID,
                            FechaAsignacionComprador =
                                CASE
                                    WHEN @CompradorAsignadoUsuarioID IS NULL THEN FechaAsignacionComprador
                                    ELSE GETDATE()
                                END
                        WHERE SolicitudCompraID = @SolicitudCompraID
                          AND Activo = 1;
                    ";

                    using var command = new SqlCommand(query, connection, transaction);
                    command.Parameters.AddWithValue("@EstatusID", nuevoEstatusID);
                    command.Parameters.AddWithValue("@Estatus", nuevoEstatusNombre);
                    command.Parameters.AddWithValue("@UsuarioID", ToDbValue(usuarioId));
                    command.Parameters.Add("@CompradorAsignadoUsuarioID", SqlDbType.Int).Value =
                        model.CompradorAsignadoUsuarioID.HasValue
                            ? model.CompradorAsignadoUsuarioID.Value
                            : DBNull.Value;
                    command.Parameters.AddWithValue("@SolicitudCompraID", model.SolicitudCompraID);

                    await command.ExecuteNonQueryAsync();
                }
                else
                {
                    var query = @"
                        UPDATE dbo.ComprasSolicitudes
                        SET
                            EstatusID = @EstatusID,
                            Estatus = @Estatus,
                            AprobadoPorUsuarioID = @UsuarioID,
                            FechaAprobacion = GETDATE()
                        WHERE SolicitudCompraID = @SolicitudCompraID
                          AND Activo = 1;
                    ";

                    using var command = new SqlCommand(query, connection, transaction);
                    command.Parameters.AddWithValue("@EstatusID", nuevoEstatusID);
                    command.Parameters.AddWithValue("@Estatus", nuevoEstatusNombre);
                    command.Parameters.AddWithValue("@UsuarioID", ToDbValue(usuarioId));
                    command.Parameters.AddWithValue("@SolicitudCompraID", model.SolicitudCompraID);

                    await command.ExecuteNonQueryAsync();
                }

                var comentario = string.IsNullOrWhiteSpace(model.Comentario)
                    ? $"Solicitud aprobada. Nuevo estatus: {nuevoEstatusNombre}."
                    : model.Comentario;

                await RegistrarHistorialAsync(
                    connection,
                    transaction,
                    model.SolicitudCompraID,
                    nuevoEstatusID,
                    comentario,
                    usuarioId
                );

                transaction.Commit();

                TempData["Success"] = "Solicitud aprobada correctamente.";
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                TempData["Error"] = "Ocurrió un error al aprobar la solicitud: " + ex.Message;
            }

            return RedirectToAction(nameof(DetalleSolicitud), new { id = model.SolicitudCompraID });
        }

        // =========================================================
        // RECHAZAR SOLICITUD DIRECCION
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RechazarSolicitud(Compras.RechazarSolicitudViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Debes capturar el motivo del rechazo.";
                return RedirectToAction(nameof(DetalleSolicitud), new { id = model.SolicitudCompraID });
            }

            var usuarioId = ObtenerUsuarioId();

            using var connection = new SqlConnection(GetConnectionString());
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();

            try
            {
                var estatusActual = await ObtenerEstatusActualAsync(connection, transaction, model.SolicitudCompraID);

                if (estatusActual != 1 && estatusActual != 4)
                {
                    TempData["Error"] = "La solicitud no se encuentra en un estatus que pueda rechazar Dirección.";
                    transaction.Rollback();
                    return RedirectToAction(nameof(DetalleSolicitud), new { id = model.SolicitudCompraID });
                }

                var estatusNombre = await ObtenerNombreEstatusAsync(connection, transaction, 11);

                var query = @"
                    UPDATE dbo.ComprasSolicitudes
                    SET
                        EstatusID = 11,
                        Estatus = @Estatus,
                        RechazadoPorUsuarioID = @UsuarioID,
                        FechaRechazo = GETDATE(),
                        MotivoRechazo = @MotivoRechazo
                    WHERE SolicitudCompraID = @SolicitudCompraID
                      AND Activo = 1;
                ";

                using (var command = new SqlCommand(query, connection, transaction))
                {
                    command.Parameters.AddWithValue("@Estatus", estatusNombre);
                    command.Parameters.AddWithValue("@UsuarioID", ToDbValue(usuarioId));
                    command.Parameters.AddWithValue("@MotivoRechazo", ToDbValue(model.MotivoRechazo));
                    command.Parameters.AddWithValue("@SolicitudCompraID", model.SolicitudCompraID);

                    await command.ExecuteNonQueryAsync();
                }

                await RegistrarHistorialAsync(
                    connection,
                    transaction,
                    model.SolicitudCompraID,
                    11,
                    "Solicitud rechazada. Motivo: " + model.MotivoRechazo,
                    usuarioId
                );

                transaction.Commit();

                TempData["Success"] = "Solicitud rechazada correctamente.";
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                TempData["Error"] = "Ocurrió un error al rechazar la solicitud: " + ex.Message;
            }

            return RedirectToAction(nameof(DetalleSolicitud), new { id = model.SolicitudCompraID });
        }

        // =========================================================
        // INICIAR COTIZACION
        // Estatus 2 -> 3
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> IniciarCotizacion(int id)
        {
            using var connection = new SqlConnection(GetConnectionString());
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();

            try
            {
                var estatusActual = await ObtenerEstatusActualAsync(connection, transaction, id);

                if (estatusActual != 2)
                {
                    TempData["Error"] = "La solicitud no está aprobada para cotizar.";
                    transaction.Rollback();
                    return RedirectToAction(nameof(DetalleSolicitud), new { id });
                }

                await CambiarEstatusSolicitudAsync(
                    connection,
                    transaction,
                    id,
                    3,
                    "Compras inició el proceso de cotización.",
                    ObtenerUsuarioId()
                );

                transaction.Commit();

                TempData["Success"] = "La solicitud ahora está en proceso de cotización.";
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                TempData["Error"] = "Ocurrió un error al iniciar cotización: " + ex.Message;
            }

            return RedirectToAction(nameof(DetalleSolicitud), new { id });
        }

        // =========================================================
        // REGISTRAR COTIZACION
        // Estatus 3 -> 4
        // =========================================================

        private async Task<string?> ObtenerNombreProveedorAsync(
    SqlConnection connection,
    SqlTransaction transaction,
    int proveedorId)
        {
            var query = @"
        SELECT Nombre
        FROM dbo.ERP_Proveedores
        WHERE ProveedorID = @ProveedorID
          AND Activo = 1;
    ";

            using var command = new SqlCommand(query, connection, transaction);
            command.Parameters.AddWithValue("@ProveedorID", proveedorId);

            var result = await command.ExecuteScalarAsync();

            return result == null || result == DBNull.Value
                ? null
                : result.ToString();
        }


        // =========================================================
        // SELECCIONAR COTIZACION
        // Estatus 4 -> 5
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SeleccionarCotizacion(Compras.SeleccionarCotizacionViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Selecciona una cotización.";
                return RedirectToAction(nameof(DetalleSolicitud), new { id = model.SolicitudCompraID });
            }

            var usuarioId = ObtenerUsuarioId();

            using var connection = new SqlConnection(GetConnectionString());
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();

            try
            {
                var estatusActual = await ObtenerEstatusActualAsync(connection, transaction, model.SolicitudCompraID);

                if (estatusActual != 4)
                {
                    TempData["Error"] = "La solicitud no se encuentra pendiente de autorización de cotización.";
                    transaction.Rollback();
                    return RedirectToAction(nameof(DetalleSolicitud), new { id = model.SolicitudCompraID });
                }

                var existeCotizacion = await ExisteCotizacionSolicitudAsync(
                    connection,
                    transaction,
                    model.SolicitudCompraID,
                    model.CotizacionID
                );

                if (!existeCotizacion)
                {
                    TempData["Error"] = "La cotización seleccionada no pertenece a esta solicitud.";
                    transaction.Rollback();
                    return RedirectToAction(nameof(DetalleSolicitud), new { id = model.SolicitudCompraID });
                }

                var estatusNombre = await ObtenerNombreEstatusAsync(connection, transaction, 5);

                var query = @"
                    UPDATE dbo.ComprasCotizaciones
                    SET EsSeleccionada = 0,
                        Estatus = 'Registrada'
                    WHERE SolicitudCompraID = @SolicitudCompraID
                      AND Activo = 1;

                    UPDATE dbo.ComprasCotizaciones
                    SET EsSeleccionada = 1,
                        Estatus = 'Seleccionada'
                    WHERE CotizacionID = @CotizacionID
                      AND SolicitudCompraID = @SolicitudCompraID
                      AND Activo = 1;

                    UPDATE dbo.ComprasSolicitudes
                    SET
                        EstatusID = 5,
                        Estatus = @Estatus,
                        CotizacionSeleccionadaID = @CotizacionID,
                        FechaSeleccionCotizacion = GETDATE(),
                        UsuarioSeleccionCotizacionID = @UsuarioID,
                        ComentariosSeleccionCotizacion = @ComentariosSeleccion
                    WHERE SolicitudCompraID = @SolicitudCompraID
                      AND Activo = 1;
                ";

                using (var command = new SqlCommand(query, connection, transaction))
                {
                    command.Parameters.AddWithValue("@SolicitudCompraID", model.SolicitudCompraID);
                    command.Parameters.AddWithValue("@CotizacionID", model.CotizacionID);
                    command.Parameters.AddWithValue("@Estatus", estatusNombre);
                    command.Parameters.AddWithValue("@UsuarioID", ToDbValue(usuarioId));
                    command.Parameters.AddWithValue("@ComentariosSeleccion", ToDbValue(model.ComentariosSeleccion));

                    await command.ExecuteNonQueryAsync();
                }

                await RegistrarHistorialAsync(
                    connection,
                    transaction,
                    model.SolicitudCompraID,
                    5,
                    "Dirección autorizó la cotización seleccionada. " + (model.ComentariosSeleccion ?? ""),
                    usuarioId
                );

                transaction.Commit();

                TempData["Success"] = "Cotización seleccionada y autorizada correctamente.";
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                TempData["Error"] = "Ocurrió un error al seleccionar la cotización: " + ex.Message;
            }

            return RedirectToAction(nameof(DetalleSolicitud), new { id = model.SolicitudCompraID });
        }

        // =========================================================
        // REGISTRAR ORDEN DE COMPRA
        // Estatus 5 -> 6
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegistrarOrdenCompra(Compras.OrdenCompraViewModel model)
        {
            if (model.SolicitudCompraID <= 0)
            {
                TempData["Error"] = "No se recibió la solicitud de compra.";
                return RedirectToAction(nameof(Solicitudes));
            }

            if (string.IsNullOrWhiteSpace(model.NumeroOC))
            {
                TempData["Error"] = "Captura el número de orden de compra.";
                return RedirectToAction(nameof(DetalleSolicitud), new { id = model.SolicitudCompraID });
            }

            var usuarioId = ObtenerUsuarioId();

            using var connection = new SqlConnection(GetConnectionString());
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();

            try
            {
                var estatusActual = await ObtenerEstatusActualAsync(connection, transaction, model.SolicitudCompraID);

                if (estatusActual != 5)
                {
                    TempData["Error"] = "La solicitud no está aprobada para generar orden de compra.";
                    transaction.Rollback();
                    return RedirectToAction(nameof(DetalleSolicitud), new { id = model.SolicitudCompraID });
                }

                var tieneOrden = await TieneOrdenCompraActivaAsync(connection, transaction, model.SolicitudCompraID);

                if (tieneOrden)
                {
                    TempData["Error"] = "Esta solicitud ya tiene una orden de compra activa.";
                    transaction.Rollback();
                    return RedirectToAction(nameof(DetalleSolicitud), new { id = model.SolicitudCompraID });
                }

                var cotizacion = await ObtenerCotizacionSeleccionadaAsync(connection, transaction, model.SolicitudCompraID);

                if (cotizacion == null)
                {
                    TempData["Error"] = "No se encontró una cotización seleccionada para generar la orden.";
                    transaction.Rollback();
                    return RedirectToAction(nameof(DetalleSolicitud), new { id = model.SolicitudCompraID });
                }

                var folioOc = $"OC-{DateTime.Now:yyyy}-{model.SolicitudCompraID.ToString().PadLeft(5, '0')}";

                var query = @"
                    INSERT INTO dbo.ComprasOrdenes
                    (
                        SolicitudCompraID,
                        CotizacionID,
                        ProveedorID,
                        ProveedorNombre,
                        NumeroOC,
                        Folio,
                        CreadoPorUsuarioID,
                        FechaOrden,
                        FechaEntregaEstimada,
                        Subtotal,
                        IVA,
                        Total,
                        Estatus,
                        Observaciones,
                        Activo
                    )
                    VALUES
                    (
                        @SolicitudCompraID,
                        @CotizacionID,
                        @ProveedorID,
                        @ProveedorNombre,
                        @NumeroOC,
                        @Folio,
                        @CreadoPorUsuarioID,
                        GETDATE(),
                        @FechaEntregaEstimada,
                        @Subtotal,
                        @IVA,
                        @Total,
                        'Pendiente',
                        @Observaciones,
                        1
                    );
                ";

                using (var command = new SqlCommand(query, connection, transaction))
                {
                    command.Parameters.AddWithValue("@SolicitudCompraID", model.SolicitudCompraID);
                    command.Parameters.AddWithValue("@CotizacionID", cotizacion.CotizacionID);
                    command.Parameters.AddWithValue("@ProveedorID", ToDbValue(cotizacion.ProveedorID));
                    command.Parameters.AddWithValue("@ProveedorNombre", ToDbValue(cotizacion.ProveedorNombre));
                    command.Parameters.AddWithValue("@NumeroOC", ToDbValue(model.NumeroOC));
                    command.Parameters.AddWithValue("@Folio", folioOc);
                    command.Parameters.AddWithValue("@CreadoPorUsuarioID", ToDbValue(usuarioId));
                    command.Parameters.AddWithValue("@FechaEntregaEstimada", ToDbValue(model.FechaEntregaEstimada));
                    command.Parameters.AddWithValue("@Subtotal", ToDbValue(model.Subtotal ?? cotizacion.Subtotal));
                    command.Parameters.AddWithValue("@IVA", ToDbValue(model.IVA ?? cotizacion.IVA));
                    command.Parameters.AddWithValue("@Total", ToDbValue(model.Total ?? cotizacion.Total));
                    command.Parameters.AddWithValue("@Observaciones", ToDbValue(model.Observaciones));

                    await command.ExecuteNonQueryAsync();
                }

                await CambiarEstatusSolicitudAsync(
                    connection,
                    transaction,
                    model.SolicitudCompraID,
                    6,
                    "Compras registró la orden de compra.",
                    usuarioId
                );

                transaction.Commit();

                TempData["Success"] = "Orden de compra registrada correctamente.";
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                TempData["Error"] = "Ocurrió un error al registrar la orden de compra: " + ex.Message;
            }

            return RedirectToAction(nameof(DetalleSolicitud), new { id = model.SolicitudCompraID });
        }

        // =========================================================
        // REGISTRAR COTIZACIONES - PANTALLA
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> RegistrarCotizaciones(int id)
        {
            if (id <= 0)
            {
                TempData["Error"] = "No se recibió la solicitud de compra.";
                return RedirectToAction(nameof(Solicitudes));
            }

            using var connection = new SqlConnection(GetConnectionString());
            await connection.OpenAsync();

            var query = @"
        SELECT
            SolicitudCompraID,
            Folio,
            EstatusID,
            EstatusNombre
        FROM dbo.vw_ComprasSolicitudes_Flujo
        WHERE SolicitudCompraID = @SolicitudCompraID
          AND Activo = 1;
    ";

            Compras.RegistrarCotizacionesViewModel? model = null;

            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@SolicitudCompraID", id);

                using var reader = await command.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    model = new Compras.RegistrarCotizacionesViewModel
                    {
                        SolicitudCompraID = GetInt(reader, "SolicitudCompraID"),
                        Folio = GetString(reader, "Folio"),
                        EstatusID = GetInt(reader, "EstatusID"),
                        EstatusNombre = GetString(reader, "EstatusNombre")
                    };
                }
            }

            if (model == null)
            {
                TempData["Error"] = "No se encontró la solicitud seleccionada.";
                return RedirectToAction(nameof(Solicitudes));
            }

            if (model.EstatusID != 3 && model.EstatusID != 4)
            {
                TempData["Error"] = "La solicitud no se encuentra en estatus de cotización.";
                return RedirectToAction(nameof(DetalleSolicitud), new { id });
            }

            model.Proveedores = await ObtenerProveedoresAsync(connection);
            model.CotizacionesExistentes = await ObtenerCotizacionesSolicitudAsync(connection, id);
            model.Materiales = await ObtenerDetalleMaterialesAsync(connection, id);

            model.Cotizaciones.Add(new Compras.CotizacionCompraViewModel());

            return View(model);
        }

        // =========================================================
        // REGISTRAR COTIZACIONES - GUARDAR
        // POST: /Compras/RegistrarCotizaciones
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegistrarCotizaciones(Compras.RegistrarCotizacionesViewModel model, int? id)
        {
            if (model.SolicitudCompraID <= 0 && id.HasValue)
            {
                model.SolicitudCompraID = id.Value;
            }

            if (model.SolicitudCompraID <= 0)
            {
                TempData["Error"] = "No se recibió la solicitud de compra.";
                return RedirectToAction(nameof(Solicitudes));
            }

            model.Cotizaciones ??= new List<Compras.CotizacionCompraViewModel>();

            var cotizacionesValidas = model.Cotizaciones
                .Where(x =>
                    x.ProveedorID.HasValue ||
                    x.Total.HasValue ||
                    x.ArchivoCotizacion != null ||
                    !string.IsNullOrWhiteSpace(x.Observaciones))
                .ToList();

            if (!cotizacionesValidas.Any())
            {
                TempData["Error"] = "Agrega al menos una cotización.";
                return RedirectToAction(nameof(RegistrarCotizaciones), new { id = model.SolicitudCompraID });
            }

            for (int i = 0; i < cotizacionesValidas.Count; i++)
            {
                var item = cotizacionesValidas[i];

                if (!item.ProveedorID.HasValue || item.ProveedorID.Value <= 0)
                {
                    TempData["Error"] = $"Selecciona el proveedor de la cotización #{i + 1}.";
                    return RedirectToAction(nameof(RegistrarCotizaciones), new { id = model.SolicitudCompraID });
                }

                if (!item.Total.HasValue || item.Total.Value <= 0)
                {
                    TempData["Error"] = $"Captura el total de la cotización #{i + 1}.";
                    return RedirectToAction(nameof(RegistrarCotizaciones), new { id = model.SolicitudCompraID });
                }

                if (item.ArchivoCotizacion == null || item.ArchivoCotizacion.Length <= 0)
                {
                    TempData["Error"] = $"Adjunta el archivo de la cotización #{i + 1}.";
                    return RedirectToAction(nameof(RegistrarCotizaciones), new { id = model.SolicitudCompraID });
                }
            }

            var usuarioId = ObtenerUsuarioId();

            using var connection = new SqlConnection(GetConnectionString());
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();

            try
            {
                var estatusActual = await ObtenerEstatusActualAsync(
                    connection,
                    transaction,
                    model.SolicitudCompraID
                );

                if (estatusActual != 3 && estatusActual != 4)
                {
                    TempData["Error"] = "La solicitud no se encuentra en estatus de cotización.";
                    transaction.Rollback();

                    return RedirectToAction(nameof(DetalleSolicitud), new { id = model.SolicitudCompraID });
                }

                int totalGuardadas = 0;

                foreach (var item in cotizacionesValidas)
                {
                    var proveedorNombre = await ObtenerNombreProveedorAsync(
                        connection,
                        transaction,
                        item.ProveedorID!.Value
                    );

                    if (string.IsNullOrWhiteSpace(proveedorNombre))
                    {
                        TempData["Error"] = "Uno de los proveedores seleccionados no existe o está inactivo.";
                        transaction.Rollback();

                        return RedirectToAction(nameof(RegistrarCotizaciones), new { id = model.SolicitudCompraID });
                    }

                    var archivo = await GuardarArchivoAsync(
                        item.ArchivoCotizacion!,
                        $"uploads/compras/cotizaciones/{model.SolicitudCompraID}",
                        new[] { ".pdf", ".jpg", ".jpeg", ".png", ".webp" }
                    );

                    var query = @"
                INSERT INTO dbo.ComprasCotizaciones
                (
                    SolicitudCompraID,
                    ProveedorID,
                    ProveedorNombre,
                    FechaCotizacion,
                    Subtotal,
                    IVA,
                    Total,
                    TiempoEntrega,
                    CondicionesPago,
                    ArchivoCotizacion,
                    NombreArchivoOriginal,
                    ExtensionArchivo,
                    ContentType,
                    TamanoBytes,
                    EsSeleccionada,
                    EsRecomendada,
                    Estatus,
                    Observaciones,
                    SubidaPorUsuarioID,
                    Activo
                )
                VALUES
                (
                    @SolicitudCompraID,
                    @ProveedorID,
                    @ProveedorNombre,
                    GETDATE(),
                    @Subtotal,
                    @IVA,
                    @Total,
                    @TiempoEntrega,
                    @CondicionesPago,
                    @ArchivoCotizacion,
                    @NombreArchivoOriginal,
                    @ExtensionArchivo,
                    @ContentType,
                    @TamanoBytes,
                    0,
                    @EsRecomendada,
                    'Registrada',
                    @Observaciones,
                    @SubidaPorUsuarioID,
                    1
                );
            ";

                    using var command = new SqlCommand(query, connection, transaction);

                    command.Parameters.AddWithValue("@SolicitudCompraID", model.SolicitudCompraID);
                    command.Parameters.AddWithValue("@ProveedorID", item.ProveedorID.Value);
                    command.Parameters.AddWithValue("@ProveedorNombre", proveedorNombre);
                    command.Parameters.AddWithValue("@Subtotal", ToDbValue(item.Subtotal));
                    command.Parameters.AddWithValue("@IVA", ToDbValue(item.IVA));
                    command.Parameters.AddWithValue("@Total", ToDbValue(item.Total));
                    command.Parameters.AddWithValue("@TiempoEntrega", ToDbValue(item.TiempoEntrega));
                    command.Parameters.AddWithValue("@CondicionesPago", ToDbValue(item.CondicionesPago));
                    command.Parameters.AddWithValue("@ArchivoCotizacion", ToDbValue(archivo.RutaRelativa));
                    command.Parameters.AddWithValue("@NombreArchivoOriginal", ToDbValue(archivo.NombreOriginal));
                    command.Parameters.AddWithValue("@ExtensionArchivo", ToDbValue(archivo.Extension));
                    command.Parameters.AddWithValue("@ContentType", ToDbValue(archivo.ContentType));
                    command.Parameters.AddWithValue("@TamanoBytes", archivo.TamanoBytes);
                    command.Parameters.AddWithValue("@EsRecomendada", item.EsRecomendada);
                    command.Parameters.AddWithValue("@Observaciones", ToDbValue(item.Observaciones));
                    command.Parameters.AddWithValue("@SubidaPorUsuarioID", ToDbValue(usuarioId));

                    await command.ExecuteNonQueryAsync();

                    totalGuardadas++;
                }

                if (estatusActual == 3)
                {
                    await CambiarEstatusSolicitudAsync(
                        connection,
                        transaction,
                        model.SolicitudCompraID,
                        4,
                        $"Compras registró {totalGuardadas} cotización(es) y las envió a Dirección para autorización.",
                        usuarioId
                    );
                }
                else
                {
                    await RegistrarHistorialAsync(
                        connection,
                        transaction,
                        model.SolicitudCompraID,
                        4,
                        $"Compras agregó {totalGuardadas} cotización(es) adicional(es).",
                        usuarioId
                    );
                }

                transaction.Commit();

                TempData["Success"] = $"{totalGuardadas} cotización(es) registrada(s) correctamente.";

                return RedirectToAction(nameof(DetalleSolicitud), new { id = model.SolicitudCompraID });
            }
            catch (Exception ex)
            {
                transaction.Rollback();

                TempData["Error"] = "Ocurrió un error al registrar las cotizaciones: " + ex.Message;

                return RedirectToAction(nameof(RegistrarCotizaciones), new { id = model.SolicitudCompraID });
            }
        }



        // =========================================================
        // ENVIAR ORDEN AL PROVEEDOR
        // Estatus 6 -> 7 -> 8
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EnviarOrdenProveedor(Compras.EnviarOrdenProveedorViewModel model)
        {
            if (model.SolicitudCompraID <= 0)
            {
                TempData["Error"] = "No se recibió la solicitud de compra.";
                return RedirectToAction(nameof(Solicitudes));
            }

            if (!model.FechaEntregaEstimada.HasValue)
            {
                TempData["Error"] = "Captura la fecha estimada de entrega.";
                return RedirectToAction(nameof(DetalleSolicitud), new { id = model.SolicitudCompraID });
            }

            var usuarioId = ObtenerUsuarioId();

            using var connection = new SqlConnection(GetConnectionString());
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();

            try
            {
                var estatusActual = await ObtenerEstatusActualAsync(connection, transaction, model.SolicitudCompraID);

                if (estatusActual != 6)
                {
                    TempData["Error"] = "La solicitud no tiene una orden lista para envío al proveedor.";
                    transaction.Rollback();
                    return RedirectToAction(nameof(DetalleSolicitud), new { id = model.SolicitudCompraID });
                }

                var ordenCompraId = model.OrdenCompraID > 0
                    ? model.OrdenCompraID
                    : await ObtenerOrdenCompraIdActivaAsync(connection, transaction, model.SolicitudCompraID);

                if (ordenCompraId <= 0)
                {
                    TempData["Error"] = "No se encontró una orden de compra activa para esta solicitud.";
                    transaction.Rollback();
                    return RedirectToAction(nameof(DetalleSolicitud), new { id = model.SolicitudCompraID });
                }

                var query = @"
                    UPDATE dbo.ComprasOrdenes
                    SET
                        FechaEnvioProveedor = GETDATE(),
                        EnviadoProveedorPorUsuarioID = @UsuarioID,
                        FechaEntregaEstimada = @FechaEntregaEstimada,
                        Estatus = 'Enviada al proveedor',
                        Observaciones = @Observaciones
                    WHERE OrdenCompraID = @OrdenCompraID
                      AND SolicitudCompraID = @SolicitudCompraID
                      AND Activo = 1;
                ";

                using (var command = new SqlCommand(query, connection, transaction))
                {
                    command.Parameters.AddWithValue("@UsuarioID", ToDbValue(usuarioId));
                    command.Parameters.AddWithValue("@FechaEntregaEstimada", model.FechaEntregaEstimada.Value);
                    command.Parameters.AddWithValue("@Observaciones", ToDbValue(model.Observaciones));
                    command.Parameters.AddWithValue("@OrdenCompraID", ordenCompraId);
                    command.Parameters.AddWithValue("@SolicitudCompraID", model.SolicitudCompraID);

                    await command.ExecuteNonQueryAsync();
                }

                await CambiarEstatusSolicitudAsync(
                    connection,
                    transaction,
                    model.SolicitudCompraID,
                    7,
                    "Orden de compra enviada al proveedor.",
                    usuarioId
                );

                await CambiarEstatusSolicitudAsync(
                    connection,
                    transaction,
                    model.SolicitudCompraID,
                    8,
                    "Solicitud pendiente de recepción en almacén.",
                    usuarioId
                );

                transaction.Commit();

                TempData["Success"] = "Orden enviada al proveedor. La solicitud quedó pendiente de recepción en almacén.";
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                TempData["Error"] = "Ocurrió un error al enviar la orden al proveedor: " + ex.Message;
            }

            return RedirectToAction(nameof(DetalleSolicitud), new { id = model.SolicitudCompraID });
        }

        // =========================================================
        // REGISTRAR RECEPCION ALMACEN
        // Estatus 8 -> 9 -> 10
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegistrarRecepcionAlmacen(Compras.RecepcionCompraViewModel model)
        {
            if (model.SolicitudCompraID <= 0)
            {
                TempData["Error"] = "No se recibió la solicitud de compra.";
                return RedirectToAction(nameof(Solicitudes));
            }

            var usuarioId = ObtenerUsuarioId();

            ArchivoGuardadoDto? evidencia = null;

            if (model.EvidenciaRecepcion != null && model.EvidenciaRecepcion.Length > 0)
            {
                try
                {
                    evidencia = await GuardarArchivoAsync(
                        model.EvidenciaRecepcion,
                        $"uploads/compras/recepciones/{model.SolicitudCompraID}",
                        new[] { ".pdf", ".jpg", ".jpeg", ".png", ".webp" }
                    );
                }
                catch (Exception ex)
                {
                    TempData["Error"] = "No se pudo guardar la evidencia de recepción: " + ex.Message;
                    return RedirectToAction(nameof(DetalleSolicitud), new { id = model.SolicitudCompraID });
                }
            }

            using var connection = new SqlConnection(GetConnectionString());
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();

            try
            {
                var estatusActual = await ObtenerEstatusActualAsync(connection, transaction, model.SolicitudCompraID);

                if (estatusActual != 8)
                {
                    TempData["Error"] = "La solicitud no está pendiente de recepción en almacén.";
                    transaction.Rollback();
                    return RedirectToAction(nameof(DetalleSolicitud), new { id = model.SolicitudCompraID });
                }

                var ordenCompraId = model.OrdenCompraID.HasValue && model.OrdenCompraID.Value > 0
                    ? model.OrdenCompraID.Value
                    : await ObtenerOrdenCompraIdActivaAsync(connection, transaction, model.SolicitudCompraID);

                var query = @"
                    INSERT INTO dbo.ComprasRecepciones
                    (
                        SolicitudCompraID,
                        OrdenCompraID,
                        RecibidoPorUsuarioID,
                        FechaRecepcion,
                        EstatusRecepcion,
                        DocumentoRemision,
                        EvidenciaRecepcionPath,
                        NombreArchivoEvidencia,
                        ExtensionArchivoEvidencia,
                        ContentTypeEvidencia,
                        TamanoBytesEvidencia,
                        Observaciones,
                        Activo
                    )
                    VALUES
                    (
                        @SolicitudCompraID,
                        @OrdenCompraID,
                        @RecibidoPorUsuarioID,
                        GETDATE(),
                        @EstatusRecepcion,
                        @DocumentoRemision,
                        @EvidenciaRecepcionPath,
                        @NombreArchivoEvidencia,
                        @ExtensionArchivoEvidencia,
                        @ContentTypeEvidencia,
                        @TamanoBytesEvidencia,
                        @Observaciones,
                        1
                    );
                ";

                using (var command = new SqlCommand(query, connection, transaction))
                {
                    command.Parameters.AddWithValue("@SolicitudCompraID", model.SolicitudCompraID);
                    command.Parameters.AddWithValue("@OrdenCompraID", ordenCompraId > 0 ? ordenCompraId : DBNull.Value);
                    command.Parameters.AddWithValue("@RecibidoPorUsuarioID", ToDbValue(usuarioId));
                    command.Parameters.AddWithValue("@EstatusRecepcion", ToDbValue(model.EstatusRecepcion ?? "Recibida"));
                    command.Parameters.AddWithValue("@DocumentoRemision", ToDbValue(model.DocumentoRemision));
                    command.Parameters.AddWithValue("@EvidenciaRecepcionPath", ToDbValue(evidencia?.RutaRelativa));
                    command.Parameters.AddWithValue("@NombreArchivoEvidencia", ToDbValue(evidencia?.NombreOriginal));
                    command.Parameters.AddWithValue("@ExtensionArchivoEvidencia", ToDbValue(evidencia?.Extension));
                    command.Parameters.AddWithValue("@ContentTypeEvidencia", ToDbValue(evidencia?.ContentType));
                    command.Parameters.AddWithValue("@TamanoBytesEvidencia", ToDbValue(evidencia?.TamanoBytes));
                    command.Parameters.AddWithValue("@Observaciones", ToDbValue(model.Observaciones));

                    await command.ExecuteNonQueryAsync();
                }

                await CambiarEstatusSolicitudAsync(
                    connection,
                    transaction,
                    model.SolicitudCompraID,
                    9,
                    "Almacén registró la recepción del material.",
                    usuarioId
                );

                await CambiarEstatusSolicitudAsync(
                    connection,
                    transaction,
                    model.SolicitudCompraID,
                    10,
                    "Solicitud cerrada después de la recepción de almacén.",
                    usuarioId
                );

                transaction.Commit();

                TempData["Success"] = "Recepción registrada correctamente. La solicitud fue cerrada.";
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                TempData["Error"] = "Ocurrió un error al registrar la recepción: " + ex.Message;
            }

            return RedirectToAction(nameof(DetalleSolicitud), new { id = model.SolicitudCompraID });
        }

        // =========================================================
        // VER ARCHIVO GUARDADO
        // =========================================================
        [HttpGet]
        public IActionResult VerArchivoCompra(string ruta)
        {
            if (string.IsNullOrWhiteSpace(ruta) || ruta.Contains(".."))
            {
                return BadRequest("Ruta inválida.");
            }

            var webRoot = ObtenerWebRootPath();
            var rutaNormalizada = ruta.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var rutaFisica = Path.Combine(webRoot, rutaNormalizada);

            if (!System.IO.File.Exists(rutaFisica))
            {
                return NotFound("No se encontró el archivo.");
            }

            var contentType = ObtenerContentType(Path.GetExtension(rutaFisica));
            return PhysicalFile(rutaFisica, contentType);
        }

        // =========================================================
        // CANCELAR SOLICITUD
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelarSolicitud(int id)
        {
            using var connection = new SqlConnection(GetConnectionString());
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();

            try
            {
                var estatusActual = await ObtenerEstatusActualAsync(connection, transaction, id);

                if (estatusActual == 10 || estatusActual == 11 || estatusActual == 12)
                {
                    TempData["Error"] = "La solicitud ya se encuentra cerrada, rechazada o cancelada.";
                    transaction.Rollback();
                    return RedirectToAction(nameof(DetalleSolicitud), new { id });
                }

                await CambiarEstatusSolicitudAsync(
                    connection,
                    transaction,
                    id,
                    12,
                    "Solicitud cancelada.",
                    ObtenerUsuarioId()
                );

                transaction.Commit();

                TempData["Success"] = "Solicitud cancelada correctamente.";
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                TempData["Error"] = "Ocurrió un error al cancelar la solicitud: " + ex.Message;
            }

            return RedirectToAction(nameof(Solicitudes));
        }

        // =========================================================
        // METODOS PRIVADOS - CONSULTAS BASE
        // =========================================================
        private async Task<List<Compras.SolicitudListadoViewModel>> ObtenerSolicitudesAsync(int? top = null)
        {
            var lista = new List<Compras.SolicitudListadoViewModel>();

            using var connection = new SqlConnection(GetConnectionString());
            await connection.OpenAsync();

            var topSql = top.HasValue ? $"TOP ({top.Value})" : "";

            var query = $@"
                SELECT {topSql}
                    SolicitudCompraID,
                    Folio,
                    FechaSolicitud,
                    OrigenSolicitud,
                    Departamento,
                    SolicitadoPorUsuarioID,
                    Prioridad,
                    TipoCompra,
                    EstatusID,
                    EstatusNombre,
                    ResponsableActual,
                    FechaUltimoMovimiento,
                    DiasEnEstatus,
                    Activo
                FROM dbo.vw_ComprasSolicitudes_Flujo
                WHERE Activo = 1
                ORDER BY FechaSolicitud DESC;
            ";

            using var command = new SqlCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                lista.Add(new Compras.SolicitudListadoViewModel
                {
                    SolicitudCompraID = GetInt(reader, "SolicitudCompraID"),
                    Folio = GetString(reader, "Folio"),
                    FechaSolicitud = GetDateTime(reader, "FechaSolicitud"),
                    OrigenSolicitud = GetString(reader, "OrigenSolicitud"),
                    Departamento = GetString(reader, "Departamento"),
                    Solicitante = GetNullableInt(reader, "SolicitadoPorUsuarioID").HasValue
                        ? $"Usuario ID {GetNullableInt(reader, "SolicitadoPorUsuarioID")}"
                        : null,
                    Prioridad = GetString(reader, "Prioridad"),
                    TipoCompra = GetString(reader, "TipoCompra"),
                    EstatusID = GetInt(reader, "EstatusID"),
                    Estatus = GetString(reader, "EstatusNombre"),
                    EstatusNombre = GetString(reader, "EstatusNombre"),
                    ResponsableActual = GetString(reader, "ResponsableActual"),
                    FechaUltimoMovimiento = GetNullableDateTime(reader, "FechaUltimoMovimiento"),
                    DiasEnEstatus = GetInt(reader, "DiasEnEstatus"),
                    Activo = GetBool(reader, "Activo")
                });
            }

            return lista;
        }

        private async Task<List<Compras.SolicitudBandejaItemViewModel>> ObtenerSolicitudesBandejaAsync()
        {
            var lista = new List<Compras.SolicitudBandejaItemViewModel>();

            using var connection = new SqlConnection(GetConnectionString());
            await connection.OpenAsync();

            var query = @"
                SELECT
                    s.SolicitudCompraID,
                    s.Folio,
                    s.DepartamentoID,
                    s.Departamento,
                    s.SolicitadoPorUsuarioID,
                    s.FechaSolicitud,
                    s.OrigenSolicitud,
                    s.Prioridad,
                    s.TipoCompra,
                    s.Motivo,
                    s.Observaciones,
                    s.EstatusID,
                    s.EstatusNombre,
                    s.ResponsableActual,
                    s.FechaUltimoMovimiento,
                    s.DiasEnEstatus,
                    s.CompradorAsignadoUsuarioID,
                    TotalMateriales =
                    (
                        SELECT COUNT(*)
                        FROM dbo.ComprasSolicitudDetalle d
                        WHERE d.SolicitudCompraID = s.SolicitudCompraID
                          AND d.Activo = 1
                    ),
                    TotalCotizaciones =
                    (
                        SELECT COUNT(*)
                        FROM dbo.ComprasCotizaciones c
                        WHERE c.SolicitudCompraID = s.SolicitudCompraID
                          AND c.Activo = 1
                    )
                FROM dbo.vw_ComprasSolicitudes_Flujo s
                WHERE s.Activo = 1
                ORDER BY s.FechaSolicitud DESC;
            ";

            using var command = new SqlCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var solicitadoPorUsuarioID = GetNullableInt(reader, "SolicitadoPorUsuarioID");
                var compradorAsignadoUsuarioID = GetNullableInt(reader, "CompradorAsignadoUsuarioID");

                lista.Add(new Compras.SolicitudBandejaItemViewModel
                {
                    SolicitudCompraID = GetInt(reader, "SolicitudCompraID"),
                    Folio = GetString(reader, "Folio"),

                    DepartamentoID = GetNullableInt(reader, "DepartamentoID"),
                    Departamento = GetString(reader, "Departamento"),

                    SolicitadoPorUsuarioID = solicitadoPorUsuarioID,
                    Solicitante = solicitadoPorUsuarioID.HasValue ? $"Usuario ID {solicitadoPorUsuarioID}" : null,

                    FechaSolicitud = GetDateTime(reader, "FechaSolicitud"),

                    OrigenSolicitud = GetString(reader, "OrigenSolicitud"),
                    Prioridad = GetString(reader, "Prioridad"),
                    TipoCompra = GetString(reader, "TipoCompra"),

                    Motivo = GetString(reader, "Motivo"),
                    Observaciones = GetString(reader, "Observaciones"),

                    EstatusID = GetInt(reader, "EstatusID"),
                    EstatusNombre = GetString(reader, "EstatusNombre"),
                    ResponsableActual = GetString(reader, "ResponsableActual"),

                    FechaUltimoMovimiento = GetNullableDateTime(reader, "FechaUltimoMovimiento"),
                    DiasEnEstatus = GetInt(reader, "DiasEnEstatus"),

                    CompradorAsignadoUsuarioID = compradorAsignadoUsuarioID,
                    CompradorAsignado = compradorAsignadoUsuarioID.HasValue ? $"Usuario ID {compradorAsignadoUsuarioID}" : null,

                    TotalMateriales = GetInt(reader, "TotalMateriales"),
                    TotalCotizaciones = GetInt(reader, "TotalCotizaciones")
                });
            }

            return lista;
        }

        private async Task<List<Compras.SolicitudDetalleItemViewModel>> ObtenerDetalleMaterialesAsync(SqlConnection connection, int solicitudCompraId)
        {
            var lista = new List<Compras.SolicitudDetalleItemViewModel>();

            var query = @"
                SELECT
                    SolicitudDetalleID,
                    ProductoID,
                    DescripcionMaterial,
                    UnidadMedida,
                    CantidadSolicitada,
                    StockActual,
                    StockMinimo,
                    FechaRequerida,
                    AceptaSustituto,
                    Observaciones,
                    Activo
                FROM dbo.ComprasSolicitudDetalle
                WHERE SolicitudCompraID = @SolicitudCompraID
                  AND Activo = 1
                ORDER BY SolicitudDetalleID;
            ";

            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@SolicitudCompraID", solicitudCompraId);

            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                lista.Add(new Compras.SolicitudDetalleItemViewModel
                {
                    SolicitudDetalleID = GetInt(reader, "SolicitudDetalleID"),
                    ProductoID = GetNullableInt(reader, "ProductoID"),
                    DescripcionMaterial = GetString(reader, "DescripcionMaterial"),
                    UnidadMedida = GetString(reader, "UnidadMedida"),
                    CantidadSolicitada = GetDecimal(reader, "CantidadSolicitada"),
                    StockActual = GetNullableDecimal(reader, "StockActual"),
                    StockMinimo = GetNullableDecimal(reader, "StockMinimo"),
                    FechaRequerida = GetNullableDateTime(reader, "FechaRequerida"),
                    AceptaSustituto = GetBool(reader, "AceptaSustituto"),
                    Observaciones = GetString(reader, "Observaciones"),
                    Activo = GetBool(reader, "Activo")
                });
            }

            return lista;
        }

        private async Task<List<Compras.CotizacionCompraViewModel>> ObtenerCotizacionesSolicitudAsync(SqlConnection connection, int solicitudCompraId)
        {
            var lista = new List<Compras.CotizacionCompraViewModel>();

            var query = @"
                SELECT
                    CotizacionID,
                    SolicitudCompraID,
                    ProveedorID,
                    ProveedorNombre,
                    FechaCotizacion,
                    Subtotal,
                    IVA,
                    Total,
                    TiempoEntrega,
                    CondicionesPago,
                    ArchivoCotizacion,
                    NombreArchivoOriginal,
                    ExtensionArchivo,
                    ContentType,
                    TamanoBytes,
                    EsSeleccionada,
                    EsRecomendada,
                    Estatus,
                    Observaciones,
                    SubidaPorUsuarioID,
                    Activo
                FROM dbo.ComprasCotizaciones
                WHERE SolicitudCompraID = @SolicitudCompraID
                  AND Activo = 1
                ORDER BY FechaCotizacion DESC;
            ";

            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@SolicitudCompraID", solicitudCompraId);

            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                lista.Add(new Compras.CotizacionCompraViewModel
                {
                    CotizacionID = GetInt(reader, "CotizacionID"),
                    SolicitudCompraID = GetInt(reader, "SolicitudCompraID"),
                    ProveedorID = GetNullableInt(reader, "ProveedorID"),
                    ProveedorNombre = GetString(reader, "ProveedorNombre"),
                    FechaCotizacion = GetDateTime(reader, "FechaCotizacion"),
                    Subtotal = GetNullableDecimal(reader, "Subtotal"),
                    IVA = GetNullableDecimal(reader, "IVA"),
                    Total = GetNullableDecimal(reader, "Total"),
                    TiempoEntrega = GetString(reader, "TiempoEntrega"),
                    CondicionesPago = GetString(reader, "CondicionesPago"),
                    ArchivoCotizacionPath = GetString(reader, "ArchivoCotizacion"),
                    NombreArchivoOriginal = GetString(reader, "NombreArchivoOriginal"),
                    ExtensionArchivo = GetString(reader, "ExtensionArchivo"),
                    ContentType = GetString(reader, "ContentType"),
                    TamanoBytes = GetNullableLong(reader, "TamanoBytes"),
                    EsSeleccionada = GetBool(reader, "EsSeleccionada"),
                    EsRecomendada = GetBool(reader, "EsRecomendada"),
                    Estatus = GetString(reader, "Estatus"),
                    Observaciones = GetString(reader, "Observaciones"),
                    SubidaPorUsuarioID = GetNullableInt(reader, "SubidaPorUsuarioID"),
                    Activo = GetBool(reader, "Activo")
                });
            }

            return lista;
        }

        private async Task<Compras.OrdenCompraViewModel?> ObtenerOrdenCompraSolicitudAsync(SqlConnection connection, int solicitudCompraId)
        {
            var query = @"
                SELECT TOP 1
                    OrdenCompraID,
                    SolicitudCompraID,
                    CotizacionID,
                    ProveedorID,
                    ProveedorNombre,
                    NumeroOC,
                    Folio,
                    CreadoPorUsuarioID,
                    FechaOrden,
                    FechaEnvioProveedor,
                    EnviadoProveedorPorUsuarioID,
                    FechaEntregaEstimada,
                    Subtotal,
                    IVA,
                    Total,
                    Estatus,
                    Observaciones,
                    Activo
                FROM dbo.ComprasOrdenes
                WHERE SolicitudCompraID = @SolicitudCompraID
                  AND Activo = 1
                ORDER BY FechaOrden DESC;
            ";

            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@SolicitudCompraID", solicitudCompraId);

            using var reader = await command.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
            {
                return null;
            }

            return new Compras.OrdenCompraViewModel
            {
                OrdenCompraID = GetInt(reader, "OrdenCompraID"),
                SolicitudCompraID = GetInt(reader, "SolicitudCompraID"),
                CotizacionID = GetNullableInt(reader, "CotizacionID"),
                ProveedorID = GetNullableInt(reader, "ProveedorID"),
                ProveedorNombre = GetString(reader, "ProveedorNombre"),
                NumeroOC = GetString(reader, "NumeroOC"),
                Folio = GetString(reader, "Folio"),
                CreadoPorUsuarioID = GetNullableInt(reader, "CreadoPorUsuarioID"),
                FechaOrden = GetDateTime(reader, "FechaOrden"),
                FechaEnvioProveedor = GetNullableDateTime(reader, "FechaEnvioProveedor"),
                EnviadoProveedorPorUsuarioID = GetNullableInt(reader, "EnviadoProveedorPorUsuarioID"),
                FechaEntregaEstimada = GetNullableDateTime(reader, "FechaEntregaEstimada"),
                Subtotal = GetNullableDecimal(reader, "Subtotal"),
                IVA = GetNullableDecimal(reader, "IVA"),
                Total = GetNullableDecimal(reader, "Total"),
                Estatus = GetString(reader, "Estatus"),
                Observaciones = GetString(reader, "Observaciones"),
                Activo = GetBool(reader, "Activo")
            };
        }

        private async Task<Compras.RecepcionCompraViewModel?> ObtenerRecepcionSolicitudAsync(SqlConnection connection, int solicitudCompraId)
        {
            var query = @"
                SELECT TOP 1
                    RecepcionID,
                    SolicitudCompraID,
                    OrdenCompraID,
                    RecibidoPorUsuarioID,
                    FechaRecepcion,
                    EstatusRecepcion,
                    DocumentoRemision,
                    EvidenciaRecepcionPath,
                    NombreArchivoEvidencia,
                    ExtensionArchivoEvidencia,
                    ContentTypeEvidencia,
                    TamanoBytesEvidencia,
                    Observaciones,
                    Activo
                FROM dbo.ComprasRecepciones
                WHERE SolicitudCompraID = @SolicitudCompraID
                  AND Activo = 1
                ORDER BY FechaRecepcion DESC;
            ";

            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@SolicitudCompraID", solicitudCompraId);

            using var reader = await command.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
            {
                return null;
            }

            return new Compras.RecepcionCompraViewModel
            {
                RecepcionID = GetInt(reader, "RecepcionID"),
                SolicitudCompraID = GetInt(reader, "SolicitudCompraID"),
                OrdenCompraID = GetNullableInt(reader, "OrdenCompraID"),
                RecibidoPorUsuarioID = GetNullableInt(reader, "RecibidoPorUsuarioID"),
                FechaRecepcion = GetDateTime(reader, "FechaRecepcion"),
                EstatusRecepcion = GetString(reader, "EstatusRecepcion"),
                DocumentoRemision = GetString(reader, "DocumentoRemision"),
                EvidenciaRecepcionPath = GetString(reader, "EvidenciaRecepcionPath"),
                NombreArchivoEvidencia = GetString(reader, "NombreArchivoEvidencia"),
                ExtensionArchivoEvidencia = GetString(reader, "ExtensionArchivoEvidencia"),
                ContentTypeEvidencia = GetString(reader, "ContentTypeEvidencia"),
                TamanoBytesEvidencia = GetNullableLong(reader, "TamanoBytesEvidencia"),
                Observaciones = GetString(reader, "Observaciones"),
                Activo = GetBool(reader, "Activo")
            };
        }

        private async Task<List<Compras.HistorialCompraViewModel>> ObtenerHistorialSolicitudAsync(SqlConnection connection, int solicitudCompraId)
        {
            var lista = new List<Compras.HistorialCompraViewModel>();

            var query = @"
                SELECT
                    HistorialID,
                    SolicitudCompraID,
                    EstatusID,
                    EstatusNombre,
                    Comentario,
                    UsuarioID,
                    UsuarioNombre,
                    FechaMovimiento
                FROM dbo.ComprasHistorial
                WHERE SolicitudCompraID = @SolicitudCompraID
                ORDER BY FechaMovimiento DESC;
            ";

            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@SolicitudCompraID", solicitudCompraId);

            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                lista.Add(new Compras.HistorialCompraViewModel
                {
                    HistorialID = GetInt(reader, "HistorialID"),
                    SolicitudCompraID = GetInt(reader, "SolicitudCompraID"),
                    EstatusID = GetInt(reader, "EstatusID"),
                    EstatusNombre = GetString(reader, "EstatusNombre"),
                    Comentario = GetString(reader, "Comentario"),
                    UsuarioID = GetNullableInt(reader, "UsuarioID"),
                    UsuarioNombre = GetString(reader, "UsuarioNombre"),
                    FechaMovimiento = GetDateTime(reader, "FechaMovimiento")
                });
            }

            return lista;
        }

        // =========================================================
        // CATALOGOS
        // =========================================================
        private async Task CargarCatalogosSolicitudAsync(Compras.CrearSolicitudViewModel model)
        {
            model.OrigenSolicitud = "Almacen";
            model.AlmacenID = null;
            model.PedidoClienteReferencia = null;

            if (string.IsNullOrWhiteSpace(model.Prioridad))
            {
                model.Prioridad = "Normal";
            }

            model.Prioridades = new List<SelectListItem>
            {
                new SelectListItem { Value = "Baja", Text = "Baja" },
                new SelectListItem { Value = "Normal", Text = "Normal" },
                new SelectListItem { Value = "Alta", Text = "Alta" },
                new SelectListItem { Value = "Urgente", Text = "Urgente" }
            };

            model.TiposCompra = new List<SelectListItem>
            {
                new SelectListItem { Value = "Materia prima", Text = "Materia prima" },
                new SelectListItem { Value = "Refacciones", Text = "Refacciones" },
                new SelectListItem { Value = "Herramientas", Text = "Herramientas" },
                new SelectListItem { Value = "Consumibles", Text = "Consumibles" },
                new SelectListItem { Value = "Servicios", Text = "Servicios" },
                new SelectListItem { Value = "Otro", Text = "Otro" }
            };

            model.Departamentos = await ObtenerDepartamentosAsync();

            model.Almacenes = new List<SelectListItem>();

            model.OrigenesSolicitud = new List<SelectListItem>
            {
                new SelectListItem { Value = "Almacen", Text = "Almacén" }
            };

            model.MaterialesCatalogo = await ObtenerMaterialesCompraAsync();
        }

        private async Task<List<SelectListItem>> ObtenerMaterialesCompraAsync()
        {
            var lista = new List<SelectListItem>
            {
                new SelectListItem { Value = "", Text = "Seleccione un material" }
            };

            using var connection = new SqlConnection(GetConnectionString());
            await connection.OpenAsync();

            var query = @"
                SELECT
                    MaterialID,
                    CodigoMaterial,
                    Material,
                    TipoMaterial,
                    Unidad,
                    StockActual,
                    StockMinimo
                FROM dbo.vw_Compras_Materiales_Stock
                ORDER BY TipoMaterial, Material;
            ";

            using var command = new SqlCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var materialId = GetInt(reader, "MaterialID");
                var codigo = GetString(reader, "CodigoMaterial") ?? "";
                var material = GetString(reader, "Material") ?? "";
                var tipoMaterial = GetString(reader, "TipoMaterial") ?? "";
                var unidad = GetString(reader, "Unidad") ?? "";
                var stockActual = GetDecimal(reader, "StockActual");

                lista.Add(new SelectListItem
                {
                    Value = materialId.ToString(),
                    Text = $"{codigo} - {material} | Tipo: {tipoMaterial} | Disp: {stockActual:0.####} {unidad}"
                });
            }

            return lista;
        }

        private async Task<List<SelectListItem>> ObtenerDepartamentosAsync()
        {
            var lista = new List<SelectListItem>
            {
                new SelectListItem { Value = "", Text = "Seleccione un departamento" }
            };

            using var connection = new SqlConnection(GetConnectionString());
            await connection.OpenAsync();

            var query = @"
                SELECT DepartamentoID, NombreDepartamento
                FROM dbo.Departamentos
                WHERE Activo = 1
                ORDER BY NombreDepartamento;
            ";

            using var command = new SqlCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                lista.Add(new SelectListItem
                {
                    Value = GetInt(reader, "DepartamentoID").ToString(),
                    Text = GetString(reader, "NombreDepartamento") ?? ""
                });
            }

            return lista;
        }

        private async Task<MaterialCompraDto?> ObtenerMaterialCompraAsync(
            SqlConnection connection,
            int materialId,
            SqlTransaction? transaction = null)
        {
            var query = @"
                SELECT TOP 1
                    MaterialID,
                    CodigoMaterial,
                    Material,
                    TipoMaterial,
                    Unidad,
                    StockActual,
                    StockMinimo
                FROM dbo.vw_Compras_Materiales_Stock
                WHERE MaterialID = @MaterialID;
            ";

            using var command = new SqlCommand(query, connection, transaction);
            command.Parameters.AddWithValue("@MaterialID", materialId);

            using var reader = await command.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
            {
                return null;
            }

            return new MaterialCompraDto
            {
                MaterialID = GetInt(reader, "MaterialID"),
                CodigoMaterial = GetString(reader, "CodigoMaterial"),
                Material = GetString(reader, "Material"),
                TipoMaterial = HasColumn(reader, "TipoMaterial") ? GetString(reader, "TipoMaterial") : null,
                Unidad = GetString(reader, "Unidad"),
                StockActual = GetDecimal(reader, "StockActual"),
                StockMinimo = GetDecimal(reader, "StockMinimo")
            };
        }

        // =========================================================
        // FLUJO / HISTORIAL
        // =========================================================
        private async Task<int> ObtenerEstatusActualAsync(SqlConnection connection, SqlTransaction transaction, int solicitudCompraId)
        {
            var query = @"
                SELECT EstatusID
                FROM dbo.ComprasSolicitudes
                WHERE SolicitudCompraID = @SolicitudCompraID
                  AND Activo = 1;
            ";

            using var command = new SqlCommand(query, connection, transaction);
            command.Parameters.AddWithValue("@SolicitudCompraID", solicitudCompraId);

            var result = await command.ExecuteScalarAsync();

            return result == null || result == DBNull.Value
                ? 0
                : Convert.ToInt32(result);
        }

        private async Task<string> ObtenerNombreEstatusAsync(SqlConnection connection, SqlTransaction transaction, int estatusId)
        {
            var query = @"
                SELECT Nombre
                FROM dbo.ComprasEstatus
                WHERE EstatusID = @EstatusID;
            ";

            using var command = new SqlCommand(query, connection, transaction);
            command.Parameters.AddWithValue("@EstatusID", estatusId);

            var result = await command.ExecuteScalarAsync();

            return result == null || result == DBNull.Value
                ? "Sin estatus"
                : result.ToString()!;
        }

        private async Task CambiarEstatusSolicitudAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            int solicitudCompraId,
            int nuevoEstatusId,
            string comentario,
            int? usuarioId)
        {
            var estatusNombre = await ObtenerNombreEstatusAsync(connection, transaction, nuevoEstatusId);

            var query = @"
                UPDATE dbo.ComprasSolicitudes
                SET
                    EstatusID = @EstatusID,
                    Estatus = @Estatus,
                    FechaCierre =
                        CASE
                            WHEN @EstatusID IN (10, 11, 12) THEN GETDATE()
                            ELSE FechaCierre
                        END,
                    CerradoPorUsuarioID =
                        CASE
                            WHEN @EstatusID IN (10, 11, 12) THEN @UsuarioID
                            ELSE CerradoPorUsuarioID
                        END
                WHERE SolicitudCompraID = @SolicitudCompraID
                  AND Activo = 1;
            ";

            using (var command = new SqlCommand(query, connection, transaction))
            {
                command.Parameters.AddWithValue("@EstatusID", nuevoEstatusId);
                command.Parameters.AddWithValue("@Estatus", estatusNombre);
                command.Parameters.AddWithValue("@UsuarioID", ToDbValue(usuarioId));
                command.Parameters.AddWithValue("@SolicitudCompraID", solicitudCompraId);

                await command.ExecuteNonQueryAsync();
            }

            await RegistrarHistorialAsync(
                connection,
                transaction,
                solicitudCompraId,
                nuevoEstatusId,
                comentario,
                usuarioId
            );
        }

        private async Task RegistrarHistorialAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            int solicitudCompraId,
            int estatusId,
            string? comentario,
            int? usuarioId)
        {
            var estatusNombre = await ObtenerNombreEstatusAsync(connection, transaction, estatusId);

            var query = @"
                INSERT INTO dbo.ComprasHistorial
                (
                    SolicitudCompraID,
                    EstatusID,
                    EstatusNombre,
                    Comentario,
                    UsuarioID,
                    UsuarioNombre,
                    FechaMovimiento
                )
                VALUES
                (
                    @SolicitudCompraID,
                    @EstatusID,
                    @EstatusNombre,
                    @Comentario,
                    @UsuarioID,
                    @UsuarioNombre,
                    GETDATE()
                );
            ";

            using var command = new SqlCommand(query, connection, transaction);
            command.Parameters.AddWithValue("@SolicitudCompraID", solicitudCompraId);
            command.Parameters.AddWithValue("@EstatusID", estatusId);
            command.Parameters.AddWithValue("@EstatusNombre", estatusNombre);
            command.Parameters.AddWithValue("@Comentario", ToDbValue(comentario));
            command.Parameters.AddWithValue("@UsuarioID", ToDbValue(usuarioId));
            command.Parameters.AddWithValue("@UsuarioNombre", ToDbValue(ObtenerNombreUsuario()));

            await command.ExecuteNonQueryAsync();
        }

        private async Task<bool> ExisteCotizacionSolicitudAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            int solicitudCompraId,
            int cotizacionId)
        {
            var query = @"
                SELECT COUNT(1)
                FROM dbo.ComprasCotizaciones
                WHERE SolicitudCompraID = @SolicitudCompraID
                  AND CotizacionID = @CotizacionID
                  AND Activo = 1;
            ";

            using var command = new SqlCommand(query, connection, transaction);
            command.Parameters.AddWithValue("@SolicitudCompraID", solicitudCompraId);
            command.Parameters.AddWithValue("@CotizacionID", cotizacionId);

            var result = await command.ExecuteScalarAsync();

            return Convert.ToInt32(result) > 0;
        }

        private async Task<bool> TieneOrdenCompraActivaAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            int solicitudCompraId)
        {
            var query = @"
                SELECT COUNT(1)
                FROM dbo.ComprasOrdenes
                WHERE SolicitudCompraID = @SolicitudCompraID
                  AND Activo = 1;
            ";

            using var command = new SqlCommand(query, connection, transaction);
            command.Parameters.AddWithValue("@SolicitudCompraID", solicitudCompraId);

            var result = await command.ExecuteScalarAsync();

            return Convert.ToInt32(result) > 0;
        }

        private async Task<CotizacionSeleccionadaDto?> ObtenerCotizacionSeleccionadaAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            int solicitudCompraId)
        {
            var query = @"
                SELECT TOP 1
                    CotizacionID,
                    ProveedorID,
                    ProveedorNombre,
                    Subtotal,
                    IVA,
                    Total
                FROM dbo.ComprasCotizaciones
                WHERE SolicitudCompraID = @SolicitudCompraID
                  AND Activo = 1
                  AND EsSeleccionada = 1
                ORDER BY FechaCotizacion DESC;
            ";

            using var command = new SqlCommand(query, connection, transaction);
            command.Parameters.AddWithValue("@SolicitudCompraID", solicitudCompraId);

            using var reader = await command.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
            {
                return null;
            }

            return new CotizacionSeleccionadaDto
            {
                CotizacionID = GetInt(reader, "CotizacionID"),
                ProveedorID = GetNullableInt(reader, "ProveedorID"),
                ProveedorNombre = GetString(reader, "ProveedorNombre"),
                Subtotal = GetNullableDecimal(reader, "Subtotal"),
                IVA = GetNullableDecimal(reader, "IVA"),
                Total = GetNullableDecimal(reader, "Total")
            };
        }

        private async Task<int> ObtenerOrdenCompraIdActivaAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            int solicitudCompraId)
        {
            var query = @"
                SELECT TOP 1 OrdenCompraID
                FROM dbo.ComprasOrdenes
                WHERE SolicitudCompraID = @SolicitudCompraID
                  AND Activo = 1
                ORDER BY FechaOrden DESC;
            ";

            using var command = new SqlCommand(query, connection, transaction);
            command.Parameters.AddWithValue("@SolicitudCompraID", solicitudCompraId);

            var result = await command.ExecuteScalarAsync();

            return result == null || result == DBNull.Value
                ? 0
                : Convert.ToInt32(result);
        }

        // =========================================================
        // ARCHIVOS
        // =========================================================
        private async Task<ArchivoGuardadoDto> GuardarArchivoAsync(
            IFormFile archivo,
            string carpetaRelativa,
            string[] extensionesPermitidas)
        {
            if (archivo == null || archivo.Length <= 0)
            {
                throw new InvalidOperationException("El archivo está vacío.");
            }

            var extension = Path.GetExtension(archivo.FileName).ToLowerInvariant();

            if (!extensionesPermitidas.Contains(extension))
            {
                throw new InvalidOperationException("Tipo de archivo no permitido.");
            }

            var webRoot = ObtenerWebRootPath();

            var carpetaLimpia = carpetaRelativa
                .Trim('/')
                .Replace("/", Path.DirectorySeparatorChar.ToString());

            var carpetaFisica = Path.Combine(webRoot, carpetaLimpia);

            if (!Directory.Exists(carpetaFisica))
            {
                Directory.CreateDirectory(carpetaFisica);
            }

            var nombreArchivo = $"{DateTime.Now:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}{extension}";
            var rutaFisica = Path.Combine(carpetaFisica, nombreArchivo);

            using (var stream = new FileStream(rutaFisica, FileMode.Create))
            {
                await archivo.CopyToAsync(stream);
            }

            var rutaRelativa = "/" + carpetaRelativa.Trim('/').Replace("\\", "/") + "/" + nombreArchivo;

            return new ArchivoGuardadoDto
            {
                RutaRelativa = rutaRelativa,
                NombreOriginal = archivo.FileName,
                Extension = extension,
                ContentType = archivo.ContentType,
                TamanoBytes = archivo.Length
            };
        }

        private string ObtenerWebRootPath()
        {
            if (!string.IsNullOrWhiteSpace(_environment.WebRootPath))
            {
                return _environment.WebRootPath;
            }

            return Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        }

        private string ObtenerContentType(string extension)
        {
            return extension.ToLowerInvariant() switch
            {
                ".pdf" => "application/pdf",
                ".jpg" => "image/jpeg",
                ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".webp" => "image/webp",
                _ => "application/octet-stream"
            };
        }

        // =========================================================
        // UTILIDADES
        // =========================================================

        private async Task<List<SelectListItem>> ObtenerProveedoresAsync(SqlConnection connection)
        {
            var lista = new List<SelectListItem>
    {
        new SelectListItem
        {
            Value = "",
            Text = "Seleccione un proveedor"
        }
    };

            var query = @"
        SELECT
            ProveedorID,
            Nombre
        FROM dbo.ERP_Proveedores
        WHERE Activo = 1
        ORDER BY Nombre;
    ";

            using var command = new SqlCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                lista.Add(new SelectListItem
                {
                    Value = GetInt(reader, "ProveedorID").ToString(),
                    Text = GetString(reader, "Nombre") ?? ""
                });
            }

            return lista;
        }


        private int? ObtenerUsuarioId()
        {
            var usuarioIdSession = HttpContext.Session.GetInt32("UsuarioID");

            if (usuarioIdSession.HasValue)
            {
                return usuarioIdSession.Value;
            }

            var claimValue = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (int.TryParse(claimValue, out int usuarioIdClaim))
            {
                return usuarioIdClaim;
            }

            return null;
        }

        private string? ObtenerNombreUsuario()
        {
            var nombreSession = HttpContext.Session.GetString("NombreUsuario");

            if (!string.IsNullOrWhiteSpace(nombreSession))
            {
                return nombreSession;
            }

            if (!string.IsNullOrWhiteSpace(User.Identity?.Name))
            {
                return User.Identity.Name;
            }

            return null;
        }

        private static object ToDbValue(object? value)
        {
            return value ?? DBNull.Value;
        }

        private class MaterialCompraDto
        {
            public int MaterialID { get; set; }
            public string? CodigoMaterial { get; set; }
            public string? Material { get; set; }
            public string? TipoMaterial { get; set; }
            public string? Unidad { get; set; }
            public decimal StockActual { get; set; }
            public decimal StockMinimo { get; set; }
        }

        private class ArchivoGuardadoDto
        {
            public string RutaRelativa { get; set; } = "";
            public string NombreOriginal { get; set; } = "";
            public string Extension { get; set; } = "";
            public string? ContentType { get; set; }
            public long TamanoBytes { get; set; }
        }

        private class CotizacionSeleccionadaDto
        {
            public int CotizacionID { get; set; }
            public int? ProveedorID { get; set; }
            public string? ProveedorNombre { get; set; }
            public decimal? Subtotal { get; set; }
            public decimal? IVA { get; set; }
            public decimal? Total { get; set; }
        }

        private static bool HasColumn(SqlDataReader reader, string column)
        {
            for (int i = 0; i < reader.FieldCount; i++)
            {
                if (string.Equals(reader.GetName(i), column, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static int GetInt(SqlDataReader reader, string column)
        {
            var value = reader[column];
            return value == DBNull.Value ? 0 : Convert.ToInt32(value);
        }

        private static int? GetNullableInt(SqlDataReader reader, string column)
        {
            var value = reader[column];
            return value == DBNull.Value ? null : Convert.ToInt32(value);
        }

        private static long? GetNullableLong(SqlDataReader reader, string column)
        {
            var value = reader[column];
            return value == DBNull.Value ? null : Convert.ToInt64(value);
        }

        private static string? GetString(SqlDataReader reader, string column)
        {
            var value = reader[column];
            return value == DBNull.Value ? null : value.ToString();
        }

        private static bool GetBool(SqlDataReader reader, string column)
        {
            var value = reader[column];
            return value != DBNull.Value && Convert.ToBoolean(value);
        }

        private static DateTime GetDateTime(SqlDataReader reader, string column)
        {
            var value = reader[column];
            return value == DBNull.Value ? DateTime.MinValue : Convert.ToDateTime(value);
        }

        private static DateTime? GetNullableDateTime(SqlDataReader reader, string column)
        {
            var value = reader[column];
            return value == DBNull.Value ? null : Convert.ToDateTime(value);
        }

        private static decimal GetDecimal(SqlDataReader reader, string column)
        {
            var value = reader[column];
            return value == DBNull.Value ? 0 : Convert.ToDecimal(value);
        }

        private static decimal? GetNullableDecimal(SqlDataReader reader, string column)
        {
            var value = reader[column];
            return value == DBNull.Value ? null : Convert.ToDecimal(value);
        }
    }
}