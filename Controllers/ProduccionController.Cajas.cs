using ERP.NSQuell.Models;
using ERP.NSQuell.Servicios.Almacen;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using static ERP.NSQuell.Models.ProduccionOperadorCajasVm;

namespace ERP.NSQuell.Controllers
{
    public sealed partial class ProduccionController : Controller
    {
        private IActionResult AccesoDenegadoCajas()
        {
            return StatusCode(StatusCodes.Status403Forbidden, "No tienes permisos para gestionar cajas de Producción.");
        }

        [HttpGet]
        public async Task<IActionResult> Cajas(int id)
        {
            if (!UsuarioEnSesion())
                return RedirectToAction("Login", "Login");

            if (id <= 0) return NotFound();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var usuarioId = ObtenerUsuarioID();
            var puedeGestionarCajas = await UsuarioPuedeGestionarCajasAsync(usuarioId, cn);
            if (!puedeGestionarCajas) return AccesoDenegadoCajas();

            var vm = await ObtenerCajasProduccionVmAsync(id, cn);

            if (vm == null)
                return NotFound();

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> FormarCaja(int ejecucionProduccionId, int cantidadPiezas, string tipoCaja, string? observaciones)
        {
            if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            var usuarioId = ObtenerUsuarioID();
            if (!await UsuarioPuedeGestionarCajasAsync(usuarioId, cn)) return AccesoDenegadoCajas();
            if (ejecucionProduccionId <= 0) { TempData["Error"] = "No se recibió la ejecución de producción."; return RedirectToAction(nameof(Index)); }
            if (cantidadPiezas <= 0) { TempData["Error"] = "La cantidad de piezas debe ser mayor a cero."; return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId }); }
            var tipoNormalizado = NormalizarTipoCajaProduccion(tipoCaja);
            if (string.IsNullOrWhiteSpace(tipoNormalizado)) { TempData["Error"] = "El tipo de caja no es válido."; return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId }); }
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var ejecucion = await ObtenerEjecucionAsync(ejecucionProduccionId, cn, tx);
                if (ejecucion == null) { await tx.RollbackAsync(); return NotFound(); }
                if (ejecucion.EstatusID != ProduccionEstatus.EnProduccion) { await tx.RollbackAsync(); TempData["Error"] = "Solo puedes formar cajas cuando la producción está en serie."; return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId }); }
                if (await TieneParoAbiertoAsync(ejecucionProduccionId, cn, tx)) { await tx.RollbackAsync(); TempData["Error"] = "No puedes formar cajas mientras exista un paro abierto."; return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId }); }
                decimal? piezasPorEmbalaje = null;
                decimal? cantidadEmbalajes = null;
                if (ejecucion.SolicitudProduccionDetalleID.HasValue && ejecucion.SolicitudProduccionDetalleID.Value > 0)
                {
                    const string sqlEmbalaje = @"
SELECT TOP(1) PiezasPorEmbalaje,CantidadEmbalajes
FROM dbo.SolicitudesProduccionDetalle WITH(UPDLOCK,HOLDLOCK)
WHERE SolicitudProduccionDetalleID=@SolicitudProduccionDetalleID AND Activo=1;";
                    await using var cmdEmbalaje = new SqlCommand(sqlEmbalaje, cn, tx);
                    cmdEmbalaje.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value = ejecucion.SolicitudProduccionDetalleID.Value;
                    await using var rd = await cmdEmbalaje.ExecuteReaderAsync();
                    if (await rd.ReadAsync())
                    {
                        piezasPorEmbalaje = rd["PiezasPorEmbalaje"] == DBNull.Value ? null : Convert.ToDecimal(rd["PiezasPorEmbalaje"]);
                        cantidadEmbalajes = rd["CantidadEmbalajes"] == DBNull.Value ? null : Convert.ToDecimal(rd["CantidadEmbalajes"]);
                    }
                }
                var esIncompleta = tipoNormalizado == ProduccionCajaTipo.Incompleta;
                if ((tipoNormalizado == ProduccionCajaTipo.Ok || esIncompleta) && (!piezasPorEmbalaje.HasValue || piezasPorEmbalaje.Value <= 0)) { await tx.RollbackAsync(); TempData["Error"] = "La pieza no tiene configurada la capacidad de piezas por embalaje."; return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId }); }
                var capacidadCaja = piezasPorEmbalaje.HasValue ? Convert.ToInt32(Math.Floor(piezasPorEmbalaje.Value)) : 0;
                if (tipoNormalizado == ProduccionCajaTipo.Ok && capacidadCaja > 0 && cantidadPiezas > capacidadCaja) { await tx.RollbackAsync(); TempData["Error"] = $"La caja excede la capacidad del embalaje. Máximo permitido: {capacidadCaja:N0} pieza(s)."; return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId }); }
                if (esIncompleta)
                {
                    if (cantidadPiezas >= capacidadCaja) { await tx.RollbackAsync(); TempData["Error"] = $"Una caja incompleta debe contener menos de {capacidadCaja:N0} pieza(s). Si alcanza la capacidad debe formarse como caja OK."; return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId }); }
                    if (!ejecucion.CantidadPlaneada.HasValue || ejecucion.CantidadPlaneada.Value <= 0) { await tx.RollbackAsync(); TempData["Error"] = "La ejecución no tiene una cantidad planeada válida."; return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId }); }
                    var consumo = await ObtenerConsumoCajasEjecucionAsync(ejecucionProduccionId, cn, tx);
                    if (consumo.Ok < ejecucion.CantidadPlaneada.Value) { await tx.RollbackAsync(); TempData["Error"] = $"Todavía faltan piezas planeadas por empacar. Planeado: {ejecucion.CantidadPlaneada.Value:N0}; aplicado a cajas: {consumo.Ok:N0}. La etiqueta blanca solo se usa para sobreproducción."; return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId }); }
                    var excedenteProducido = Math.Max(0, ejecucion.CantidadOKTotal - ejecucion.CantidadPlaneada.Value);
                    if (excedenteProducido <= 0) { await tx.RollbackAsync(); TempData["Error"] = "No existe sobreproducción OK disponible para formar una caja incompleta."; return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId }); }
                }
                var tipoDisponibilidad = esIncompleta ? ProduccionCajaTipo.Ok : tipoNormalizado;
                var capturadoDisponible = await ObtenerCantidadDisponibleParaCajaAsync(ejecucionProduccionId, tipoDisponibilidad, cn, tx);
                if (cantidadPiezas > capturadoDisponible) { await tx.RollbackAsync(); TempData["Error"] = "No puedes formar la caja porque la cantidad excede lo capturado disponible. Disponible: " + capturadoDisponible.ToString("N0") + " pieza(s)."; return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId }); }
                if (tipoNormalizado == ProduccionCajaTipo.Ok)
                {
                    const string sqlTotales = @"
SELECT COUNT(1) AS CajasFormadas,ISNULL(SUM(ISNULL(CantidadPiezas,ISNULL(Cantidad,0))),0) AS PiezasEnCajas
FROM dbo.Produccion_Cajas c WITH(UPDLOCK,HOLDLOCK)
WHERE c.EjecucionProduccionID=@EjecucionProduccionID AND c.Activo=1
AND UPPER(LTRIM(RTRIM(ISNULL(c.TipoCaja,N'OK'))))=N'OK'
AND NOT EXISTS(SELECT 1 FROM dbo.Produccion_CajaOrigenDetalle od WHERE od.CajaProduccionID=c.CajaProduccionID AND od.Activo=1);";
                    int cajasFormadas;
                    int piezasEnCajas;
                    await using (var cmdTotales = new SqlCommand(sqlTotales, cn, tx))
                    {
                        cmdTotales.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                        await using var rd = await cmdTotales.ExecuteReaderAsync();
                        await rd.ReadAsync();
                        cajasFormadas = Convert.ToInt32(rd["CajasFormadas"]);
                        piezasEnCajas = Convert.ToInt32(rd["PiezasEnCajas"]);
                    }
                    var detalleAplicado = await ObtenerCantidadDetalleCajaPorEjecucionAsync(ejecucionProduccionId, cn, tx);
                    var totalAplicado = piezasEnCajas + detalleAplicado;
                    if (ejecucion.CantidadPlaneada.HasValue && ejecucion.CantidadPlaneada.Value > 0 && totalAplicado + cantidadPiezas > ejecucion.CantidadPlaneada.Value) { await tx.RollbackAsync(); TempData["Error"] = $"La caja excedería la cantidad planeada. Planeado: {ejecucion.CantidadPlaneada.Value:N0}; actualmente aplicado a cajas: {totalAplicado:N0}."; return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId }); }
                    if (cantidadEmbalajes.HasValue && cantidadEmbalajes.Value > 0)
                    {
                        var cajasEsperadas = Convert.ToInt32(Math.Ceiling(cantidadEmbalajes.Value));
                        if (cajasFormadas >= cajasEsperadas) { await tx.RollbackAsync(); TempData["Error"] = $"Ya se formaron las {cajasEsperadas:N0} caja(s)/embalaje(s) normales esperadas para esta orden."; return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId }); }
                    }
                }
                var siguienteNumero = await ObtenerSiguienteNumeroCajaAsync(ejecucionProduccionId, cn, tx);
                var folioCaja = CrearFolioCajaProduccion(ejecucion, siguienteNumero);
                const string sqlInsert = @"
INSERT INTO dbo.Produccion_Cajas
(
    EjecucionProduccionID,ProgramaProduccionID,SolicitudProduccionID,SolicitudProduccionDetalleID,ReleaseID,ReleaseDetalleID,
    NumeroCaja,FolioCaja,CantidadPiezas,TipoCaja,LoteMaterial,EtiquetaFolio,EstadoCajaID,EstadoCajaNombre,EtiquetaVerde,
    FechaFormacion,UsuarioFormacionID,Observaciones,Activo,UsuarioCreacionID,FechaCreacion,Etiqueta,Cantidad,EstatusCalidad,
    OperadorUsuarioID,EsProductoIncompleto,EstadoProductoIncompleto,CapacidadObjetivoCaja,CantidadPendienteCompletar,EtiquetaBlanca
)
OUTPUT INSERTED.CajaProduccionID
VALUES
(
    @EjecucionProduccionID,@ProgramaProduccionID,@SolicitudProduccionID,@SolicitudProduccionDetalleID,@ReleaseID,@ReleaseDetalleID,
    @NumeroCaja,@FolioCaja,@CantidadPiezas,@TipoCaja,NULL,NULL,@EstadoCajaID,@EstadoCajaNombre,0,
    GETDATE(),@UsuarioID,@Observaciones,1,@UsuarioID,GETDATE(),@EtiquetaCompatibilidad,@CantidadPiezas,@EstatusCalidad,
    @UsuarioID,@EsProductoIncompleto,@EstadoProductoIncompleto,@CapacidadObjetivoCaja,@CantidadPendienteCompletar,NULL
);";
                long cajaProduccionId;
                await using (var cmd = new SqlCommand(sqlInsert, cn, tx))
                {
                    cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucion.EjecucionProduccionID;
                    cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = ejecucion.ProgramaProduccionID;
                    cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = (object?)ejecucion.SolicitudProduccionID ?? DBNull.Value;
                    cmd.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value = (object?)ejecucion.SolicitudProduccionDetalleID ?? DBNull.Value;
                    cmd.Parameters.Add("@ReleaseID", SqlDbType.Int).Value = (object?)ejecucion.ReleaseID ?? DBNull.Value;
                    cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = (object?)ejecucion.ReleaseDetalleID ?? DBNull.Value;
                    cmd.Parameters.Add("@NumeroCaja", SqlDbType.Int).Value = siguienteNumero;
                    cmd.Parameters.Add("@FolioCaja", SqlDbType.NVarChar, 100).Value = folioCaja;
                    cmd.Parameters.Add("@CantidadPiezas", SqlDbType.Int).Value = cantidadPiezas;
                    cmd.Parameters.Add("@TipoCaja", SqlDbType.NVarChar, 30).Value = tipoNormalizado;
                    cmd.Parameters.Add("@EstadoCajaID", SqlDbType.Int).Value = ProduccionCajaEstatus.FormadaProduccion;
                    cmd.Parameters.Add("@EstadoCajaNombre", SqlDbType.NVarChar, 100).Value = ProduccionCajaEstatus.Nombre(ProduccionCajaEstatus.FormadaProduccion);
                    cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 500).Value = string.IsNullOrWhiteSpace(observaciones) ? DBNull.Value : observaciones.Trim();
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    cmd.Parameters.Add("@EtiquetaCompatibilidad", SqlDbType.NVarChar, 100).Value = folioCaja;
                    cmd.Parameters.Add(
     "@EstatusCalidad",
     SqlDbType.NVarChar,
     50).Value =
     "FORMADA";
                    cmd.Parameters.Add("@EsProductoIncompleto", SqlDbType.Bit).Value = esIncompleta;
                    cmd.Parameters.Add("@EstadoProductoIncompleto", SqlDbType.NVarChar, 30).Value = esIncompleta ? ProduccionProductoIncompletoEstado.Disponible : DBNull.Value;
                    cmd.Parameters.Add("@CapacidadObjetivoCaja", SqlDbType.Int).Value = esIncompleta ? capacidadCaja : DBNull.Value;
                    cmd.Parameters.Add("@CantidadPendienteCompletar", SqlDbType.Int).Value = esIncompleta ? capacidadCaja - cantidadPiezas : DBNull.Value;
                    cajaProduccionId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
                }
                if (esIncompleta)
                {
                    var etiquetaBlanca = $"BLA-{cajaProduccionId:000000}";
                    const string sqlBlanca = @"
UPDATE dbo.Produccion_Cajas
SET EtiquetaBlanca=@EtiquetaBlanca,EtiquetaFolio=@EtiquetaBlanca,Etiqueta=@EtiquetaBlanca,UsuarioModificacionID=@UsuarioID,FechaModificacion=GETDATE()
WHERE CajaProduccionID=@CajaProduccionID AND Activo=1;
INSERT INTO dbo.Produccion_CajaOrigenDetalle
(CajaProduccionID,EjecucionProduccionID,ProgramaProduccionID,SolicitudProduccionID,SolicitudProduccionDetalleID,ReleaseID,ReleaseDetalleID,CantidadPiezas,TipoMovimiento,Observaciones,UsuarioCreacionID,FechaCreacion,Activo)
VALUES
(@CajaProduccionID,@EjecucionProduccionID,@ProgramaProduccionID,@SolicitudProduccionID,@SolicitudProduccionDetalleID,@ReleaseID,@ReleaseDetalleID,@CantidadPiezas,N'ORIGEN',N'Sobreproducción resguardada como producto incompleto con etiqueta blanca.',@UsuarioID,SYSDATETIME(),1);";
                    await using var cmdBlanca = new SqlCommand(sqlBlanca, cn, tx);
                    cmdBlanca.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value = cajaProduccionId;
                    cmdBlanca.Parameters.Add("@EtiquetaBlanca", SqlDbType.NVarChar, 100).Value = etiquetaBlanca;
                    cmdBlanca.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucion.EjecucionProduccionID;
                    cmdBlanca.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = ejecucion.ProgramaProduccionID;
                    cmdBlanca.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = (object?)ejecucion.SolicitudProduccionID ?? DBNull.Value;
                    cmdBlanca.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value = (object?)ejecucion.SolicitudProduccionDetalleID ?? DBNull.Value;
                    cmdBlanca.Parameters.Add("@ReleaseID", SqlDbType.Int).Value = (object?)ejecucion.ReleaseID ?? DBNull.Value;
                    cmdBlanca.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = (object?)ejecucion.ReleaseDetalleID ?? DBNull.Value;
                    cmdBlanca.Parameters.Add("@CantidadPiezas", SqlDbType.Int).Value = cantidadPiezas;
                    cmdBlanca.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    await cmdBlanca.ExecuteNonQueryAsync();
                    await tx.CommitAsync();
                    TempData["Success"] = $"Producto incompleto {etiquetaBlanca} registrado con {cantidadPiezas:N0} pieza(s). Faltan {capacidadCaja - cantidadPiezas:N0} para completar la caja.";
                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }
                await tx.CommitAsync();
                TempData["Success"] = $"Caja {siguienteNumero:N0} formada correctamente con {cantidadPiezas:N0} pieza(s).";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] = "No fue posible formar la caja: " + ex.Message;
            }
            return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EscanearCaja(ProduccionEscanearCajaPostVm vm)
        {
            if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");
            if (vm.EjecucionProduccionID <= 0)
            {
                TempData["Error"] = "No se recibió una ejecución de Producción válida.";
                return RedirectToAction(nameof(Index));
            }
            vm.CodigoBarras = vm.CodigoBarras?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(vm.CodigoBarras))
            {
                TempData["Error"] = "Escanea una etiqueta física.";
                return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
            }
            if (!AlmacenPTCodigoBarrasService.TryParse(vm.CodigoBarras, out var parseado, out var error) || parseado == null)
            {
                TempData["Error"] = string.IsNullOrWhiteSpace(error) ? "No fue posible interpretar la etiqueta escaneada." : error;
                return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
            }
            if (parseado.Cantidad <= 0)
            {
                TempData["Error"] = "La etiqueta no contiene una cantidad válida.";
                return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
            }
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            var usuarioId = ObtenerUsuarioID();
            if (!await UsuarioPuedeGestionarCajasAsync(usuarioId, cn)) return AccesoDenegadoCajas();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var contexto = await ObtenerContextoEscaneoCajaAsync(vm.EjecucionProduccionID, cn, tx);
                if (contexto == null)
                {
                    await tx.RollbackAsync();
                    return NotFound();
                }
                if (contexto.EstatusID != ProduccionEstatus.EnProduccion)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "Solo puedes registrar cajas cuando la corrida se encuentra en Producción.";
                    return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
                }
                if (await TieneParoAbiertoAsync(vm.EjecucionProduccionID, cn, tx))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "No puedes registrar cajas mientras exista un paro abierto.";
                    return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
                }
                var codigoFisico = parseado.CodigoOriginal?.Trim();
                if (string.IsNullOrWhiteSpace(codigoFisico)) codigoFisico = vm.CodigoBarras;
                if (codigoFisico.Length > 500)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "El código de barras excede la longitud permitida.";
                    return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
                }
                if (await ExisteCodigoBarrasCajaAsync(codigoFisico, cn, tx))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "Esta etiqueta ya fue escaneada anteriormente. No se generó una caja duplicada.";
                    return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
                }
                var ofEsperada = NormalizarValorEscaneo(contexto.NumeroOF);
                var ofEscaneada = NormalizarValorEscaneo(parseado.NumeroOF);
                if (string.IsNullOrWhiteSpace(ofEsperada))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La corrida no tiene una Orden de Fabricación válida para comparar contra la etiqueta.";
                    return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
                }
                if (string.IsNullOrWhiteSpace(ofEscaneada) || !string.Equals(ofEsperada, ofEscaneada, StringComparison.OrdinalIgnoreCase))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = $"La etiqueta no corresponde a la OF actual. Esperada: {contexto.NumeroOF}. Escaneada: {parseado.NumeroOF}.";
                    return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
                }
                var parteEscaneada = NormalizarValorEscaneo(parseado.NumeroParte);
                var numeroParteEsperado = NormalizarValorEscaneo(contexto.NumeroParte);
                var referenciaEsperada = NormalizarValorEscaneo(contexto.ReferenciaSAP);
                var parteCoincide = !string.IsNullOrWhiteSpace(parteEscaneada) && ((!string.IsNullOrWhiteSpace(numeroParteEsperado) && parteEscaneada == numeroParteEsperado) || (!string.IsNullOrWhiteSpace(referenciaEsperada) && parteEscaneada == referenciaEsperada));
                if (!parteCoincide)
                {
                    await tx.RollbackAsync();
                    var parteMostrar = !string.IsNullOrWhiteSpace(contexto.ReferenciaSAP) ? contexto.ReferenciaSAP : contexto.NumeroParte;
                    TempData["Error"] = $"La etiqueta pertenece a otro número de parte. Esperado: {parteMostrar}. Escaneado: {parseado.NumeroParte}.";
                    return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
                }
                if (!contexto.PiezasPorEmbalaje.HasValue || contexto.PiezasPorEmbalaje.Value <= 0)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La pieza no tiene configurada la capacidad de piezas por embalaje.";
                    return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
                }
                var capacidadCaja = Convert.ToInt32(Math.Floor(contexto.PiezasPorEmbalaje.Value));
                if (capacidadCaja <= 0)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La capacidad configurada del embalaje no es válida.";
                    return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
                }
                if (parseado.Cantidad > capacidadCaja)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = $"La etiqueta indica {parseado.Cantidad:N0} pieza(s), pero el embalaje permite como máximo {capacidadCaja:N0}.";
                    return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
                }
                if (contexto.CantidadPlaneada <= 0)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La ejecución no tiene una cantidad planeada válida.";
                    return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
                }
                var consumo = await ObtenerConsumoCajasEjecucionAsync(vm.EjecucionProduccionID, cn, tx);
                var planeadoPendiente = Math.Max(0, contexto.CantidadPlaneada - consumo.Ok);
                if (planeadoPendiente <= 0)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La cantidad planeada ya se encuentra completamente aplicada a cajas. La sobreproducción debe manejarse mediante el flujo de producto incompleto/etiqueta blanca.";
                    return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
                }
                var cantidadEsperadaCaja = Math.Min(capacidadCaja, planeadoPendiente);
                if (parseado.Cantidad != cantidadEsperadaCaja)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = $"La cantidad de la etiqueta no corresponde a la caja que debe formarse. Esperada: {cantidadEsperadaCaja:N0} pieza(s). Etiqueta: {parseado.Cantidad:N0} pieza(s).";
                    return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
                }
                var capturadoDisponible = await ObtenerCantidadDisponibleParaCajaAsync(vm.EjecucionProduccionID, ProduccionCajaTipo.Ok, cn, tx);
                if (parseado.Cantidad > capturadoDisponible)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = $"Todavía no existen suficientes piezas OK capturadas para esta caja. Etiqueta: {parseado.Cantidad:N0}; disponible: {capturadoDisponible:N0}.";
                    return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
                }
                if (contexto.CantidadEmbalajes.HasValue && contexto.CantidadEmbalajes.Value > 0)
                {
                    var cajasEsperadas = Convert.ToInt32(Math.Ceiling(contexto.CantidadEmbalajes.Value));
                    var cajasActuales = await ObtenerCantidadCajasNormalesAsync(vm.EjecucionProduccionID, cn, tx);
                    if (cajasActuales >= cajasEsperadas)
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = $"Ya se registraron las {cajasEsperadas:N0} caja(s) normales esperadas para esta orden.";
                        return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
                    }
                }
                var validacionCalidad = await ValidarEnvioCajaCalidadAsync(vm.EjecucionProduccionID, cn, tx);
                if (!validacionCalidad.Permitido || !validacionCalidad.InspeccionID.HasValue)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = validacionCalidad.Mensaje;
                    return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
                }
                var siguienteNumero = await ObtenerSiguienteNumeroCajaAsync(vm.EjecucionProduccionID, cn, tx);
                var ejecucion = await ObtenerEjecucionAsync(vm.EjecucionProduccionID, cn, tx);
                if (ejecucion == null)
                {
                    await tx.RollbackAsync();
                    return NotFound();
                }
                var folioCaja = CrearFolioCajaProduccion(ejecucion, siguienteNumero);
                const string sqlInsert = @"
INSERT INTO dbo.Produccion_Cajas
(
    EjecucionProduccionID,ProgramaProduccionID,SolicitudProduccionID,SolicitudProduccionDetalleID,ReleaseID,ReleaseDetalleID,
    NumeroCaja,FolioCaja,CantidadPiezas,TipoCaja,LoteMaterial,EtiquetaFolio,EstadoCajaID,EstadoCajaNombre,EtiquetaVerde,
    FechaFormacion,UsuarioFormacionID,FechaSolicitudCalidad,UsuarioSolicitudCalidadID,Observaciones,Activo,UsuarioCreacionID,FechaCreacion,
    Etiqueta,Cantidad,EstatusCalidad,OperadorUsuarioID,EsProductoIncompleto,
    CodigoBarrasOrigen,NumeroOFEtiqueta,NumeroParteEtiqueta,DesignacionEtiqueta,CantidadEtiqueta,LoteEtiqueta,
    FechaEscaneoProduccion,UsuarioEscaneoProduccionID,FechaEscaneoCalidad,UsuarioEscaneoCalidadID
)
OUTPUT INSERTED.CajaProduccionID
VALUES
(
    @EjecucionProduccionID,@ProgramaProduccionID,@SolicitudProduccionID,@SolicitudProduccionDetalleID,@ReleaseID,@ReleaseDetalleID,
    @NumeroCaja,@FolioCaja,@CantidadPiezas,N'OK',@LoteMaterial,NULL,@EstadoCajaID,@EstadoCajaNombre,0,
    @Ahora,@UsuarioID,@Ahora,@UsuarioID,@Observaciones,1,@UsuarioID,@Ahora,
    @FolioCaja,@CantidadPiezas,N'PENDIENTE',@UsuarioID,0,
    @CodigoBarrasOrigen,@NumeroOFEtiqueta,@NumeroParteEtiqueta,@DesignacionEtiqueta,@CantidadEtiqueta,@LoteEtiqueta,
    @Ahora,@UsuarioID,NULL,NULL
);";
                long cajaProduccionId;
                var ahora = DateTime.Now;
                await using (var cmd = new SqlCommand(sqlInsert, cn, tx))
                {
                    cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = contexto.EjecucionProduccionID;
                    cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = contexto.ProgramaProduccionID;
                    cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = (object?)contexto.SolicitudProduccionID ?? DBNull.Value;
                    cmd.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value = (object?)contexto.SolicitudProduccionDetalleID ?? DBNull.Value;
                    cmd.Parameters.Add("@ReleaseID", SqlDbType.Int).Value = (object?)contexto.ReleaseID ?? DBNull.Value;
                    cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = (object?)contexto.ReleaseDetalleID ?? DBNull.Value;
                    cmd.Parameters.Add("@NumeroCaja", SqlDbType.Int).Value = siguienteNumero;
                    cmd.Parameters.Add("@FolioCaja", SqlDbType.NVarChar, 100).Value = folioCaja;
                    cmd.Parameters.Add("@CantidadPiezas", SqlDbType.Int).Value = parseado.Cantidad;
                    cmd.Parameters.Add("@LoteMaterial", SqlDbType.NVarChar, 150).Value = string.IsNullOrWhiteSpace(parseado.Lote) ? DBNull.Value : parseado.Lote.Trim();
                    cmd.Parameters.Add("@EstadoCajaID", SqlDbType.Int).Value = ProduccionCajaEstatus.PendienteCalidad;
                    cmd.Parameters.Add("@EstadoCajaNombre", SqlDbType.NVarChar, 100).Value = ProduccionCajaEstatus.Nombre(ProduccionCajaEstatus.PendienteCalidad);
                    cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 500).Value = "Caja formada mediante escaneo de etiqueta física y enviada a Calidad pendiente de recepción física.";
                    cmd.Parameters.Add("@CodigoBarrasOrigen", SqlDbType.NVarChar, 500).Value = codigoFisico;
                    cmd.Parameters.Add("@NumeroOFEtiqueta", SqlDbType.NVarChar, 120).Value = string.IsNullOrWhiteSpace(parseado.NumeroOF) ? DBNull.Value : parseado.NumeroOF.Trim();
                    cmd.Parameters.Add("@NumeroParteEtiqueta", SqlDbType.NVarChar, 150).Value = string.IsNullOrWhiteSpace(parseado.NumeroParte) ? DBNull.Value : parseado.NumeroParte.Trim();
                    cmd.Parameters.Add("@DesignacionEtiqueta", SqlDbType.NVarChar, 300).Value = string.IsNullOrWhiteSpace(parseado.Designacion) ? DBNull.Value : parseado.Designacion.Trim();
                    cmd.Parameters.Add("@CantidadEtiqueta", SqlDbType.Int).Value = parseado.Cantidad;
                    cmd.Parameters.Add("@LoteEtiqueta", SqlDbType.NVarChar, 150).Value = string.IsNullOrWhiteSpace(parseado.Lote) ? DBNull.Value : parseado.Lote.Trim();
                    cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahora;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    cajaProduccionId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
                }
                var comentario = $"Producción escaneó y envió la caja {folioCaja} a Calidad. Etiqueta física validada contra OF {contexto.NumeroOF}, parte {(string.IsNullOrWhiteSpace(contexto.ReferenciaSAP) ? contexto.NumeroParte : contexto.ReferenciaSAP)} y cantidad {parseado.Cantidad:N0}. Pendiente de recepción física por Calidad.";
                if (comentario.Length > 1000) comentario = comentario[..1000];
                const string sqlHistorial = @"
INSERT INTO dbo.Calidad_InspeccionHistorial
(
    InspeccionID,Movimiento,EstadoAnterior,EstadoNuevo,ResultadoCalidad,Etiqueta,Comentario,UsuarioID,FechaMovimiento
)
VALUES
(
    @InspeccionID,N'CAJA_ENVIADA_DESDE_PRODUCCION',N'MONITOREO_ACTIVO',N'MONITOREO_ACTIVO',NULL,NULL,@Comentario,@UsuarioID,@Ahora
);";
                await using (var cmd = new SqlCommand(sqlHistorial, cn, tx))
                {
                    cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value = validacionCalidad.InspeccionID.Value;
                    cmd.Parameters.Add("@Comentario", SqlDbType.NVarChar, 1000).Value = comentario;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahora;
                    await cmd.ExecuteNonQueryAsync();
                }
                await tx.CommitAsync();
                TempData["Success"] = $"Caja {siguienteNumero:N0} registrada por escáner con {parseado.Cantidad:N0} pieza(s). Calidad ya fue notificada; la caja aún está pendiente de recepción física.";
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627)
            {
                await tx.RollbackAsync();
                TempData["Error"] = "La etiqueta ya fue registrada. No se generó una caja duplicada.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] = "No fue posible registrar la caja escaneada: " + ex.Message;
            }
            return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CorregirCajaDevuelta(int cajaProduccionId, string? correccionRealizada)
        {
            if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");
            if (cajaProduccionId <= 0)
            {
                TempData["Error"] = "No se recibió una caja válida.";
                return RedirectToAction(nameof(Index));
            }

            correccionRealizada = correccionRealizada?.Trim();
            if (string.IsNullOrWhiteSpace(correccionRealizada))
            {
                TempData["Error"] = "Captura la corrección realizada antes de reenviar la caja a Calidad.";
                return RedirectToAction(nameof(Index));
            }

            if (correccionRealizada.Length > 1000)
            {
                TempData["Error"] = "La descripción de la corrección no puede superar 1000 caracteres.";
                return RedirectToAction(nameof(Index));
            }

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var usuarioId = ObtenerUsuarioID();
            if (!await UsuarioPuedeGestionarCajasAsync(usuarioId, cn)) return AccesoDenegadoCajas();

            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
            var ejecucionProduccionId = 0;

            try
            {
                const string sqlObtenerCaja = @"
SELECT TOP (1)
    c.CajaProduccionID,
    c.EjecucionProduccionID,
    ISNULL(c.NumeroCaja,0) AS NumeroCaja,
    COALESCE(NULLIF(c.FolioCaja,N''),NULLIF(c.EtiquetaFolio,N''),NULLIF(c.Etiqueta,N''),CONVERT(NVARCHAR(100),c.CajaProduccionID)) AS FolioCaja,
    ISNULL(c.EstadoCajaID,1) AS EstadoCajaID,
    UPPER(LTRIM(RTRIM(ISNULL(c.EstatusCalidad,N'')))) AS EstatusCalidad,
    ISNULL(c.MotivoCalidad,N'') AS MotivoCalidad,
    ci.InspeccionID,
    UPPER(LTRIM(RTRIM(ISNULL(ci.Estado,N'')))) AS EstadoInspeccion,
    ISNULL(ci.ConfiguracionInvalidada,0) AS ConfiguracionInvalidada
FROM dbo.Produccion_Cajas c WITH (UPDLOCK,HOLDLOCK)
OUTER APPLY
(
    SELECT TOP (1)
        i.InspeccionID,
        i.Estado,
        i.ConfiguracionInvalidada
    FROM dbo.Calidad_Inspecciones i WITH (UPDLOCK,HOLDLOCK)
    WHERE i.EjecucionProduccionID=c.EjecucionProduccionID
      AND ISNULL(i.Estado,N'')<>N'CERRADA'
    ORDER BY i.InspeccionID DESC
) ci
WHERE c.CajaProduccionID=@CajaProduccionID
  AND c.Activo=1;";

                int inspeccionId;
                int estadoCajaId;
                string estatusCalidad;
                string folioCaja;
                string motivoDevolucion;
                string estadoInspeccion;
                bool configuracionInvalidada;

                await using (var cmd = new SqlCommand(sqlObtenerCaja, cn, tx))
                {
                    cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value = cajaProduccionId;
                    await using var rd = await cmd.ExecuteReaderAsync();

                    if (!await rd.ReadAsync())
                    {
                        await tx.RollbackAsync();
                        return NotFound();
                    }

                    ejecucionProduccionId = Convert.ToInt32(rd["EjecucionProduccionID"]);
                    estadoCajaId = Convert.ToInt32(rd["EstadoCajaID"]);
                    estatusCalidad = rd["EstatusCalidad"]?.ToString()?.Trim() ?? string.Empty;
                    folioCaja = rd["FolioCaja"]?.ToString()?.Trim() ?? cajaProduccionId.ToString();
                    motivoDevolucion = rd["MotivoCalidad"]?.ToString()?.Trim() ?? string.Empty;
                    estadoInspeccion = rd["EstadoInspeccion"]?.ToString()?.Trim() ?? string.Empty;
                    configuracionInvalidada = Convert.ToBoolean(rd["ConfiguracionInvalidada"]);

                    if (rd["InspeccionID"] == DBNull.Value)
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = "No existe una inspección activa de Calidad relacionada con esta caja.";
                        return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                    }

                    inspeccionId = Convert.ToInt32(rd["InspeccionID"]);
                }

                if (estadoCajaId != ProduccionCajaEstatus.FormadaProduccion || estatusCalidad != "DEVUELTA")
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "Solamente pueden corregirse cajas devueltas por Calidad.";
                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }

                if (configuracionInvalidada)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La configuración de Calidad está invalidada. Primero debe corregirse la configuración de la ejecución.";
                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }

                if (estadoInspeccion == "PENDIENTE_RELIBERACION")
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La ejecución tiene una reliberación pendiente. La caja no puede corregirse para reenvío hasta que Calidad autorice el reinicio.";
                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }

                var comentarioCorreccion = $"Producción corrigió la caja {folioCaja}. Motivo de devolución: {(string.IsNullOrWhiteSpace(motivoDevolucion) ? "No especificado" : motivoDevolucion)}. Corrección realizada: {correccionRealizada}";
                if (comentarioCorreccion.Length > 1000) comentarioCorreccion = comentarioCorreccion[..1000];

                const string sqlActualizar = @"
UPDATE dbo.Produccion_Cajas
SET EstatusCalidad=N'CORREGIDA',
    EstadoCajaID=@EstadoCajaID,
    EstadoCajaNombre=@EstadoCajaNombre,
    EtiquetaVerde=0,
    FechaLiberacionCalidad=NULL,
    AuditorCalidadUsuarioID=NULL,
    UsuarioCalidadID=NULL,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=GETDATE()
WHERE CajaProduccionID=@CajaProduccionID
  AND EjecucionProduccionID=@EjecucionProduccionID
  AND Activo=1
  AND EstadoCajaID=@EstadoCajaID
  AND UPPER(LTRIM(RTRIM(ISNULL(EstatusCalidad,N''))))=N'DEVUELTA';

IF @@ROWCOUNT<>1
    THROW 51070,'La caja cambió de estado mientras se registraba la corrección.',1;

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
    @InspeccionID,
    N'CAJA_CORREGIDA_PRODUCCION',
    @EstadoInspeccion,
    @EstadoInspeccion,
    N'CORREGIDA',
    NULL,
    @Comentario,
    @UsuarioID,
    GETDATE()
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.Calidad_InspeccionHistorial h
    WHERE h.InspeccionID=@InspeccionID
      AND h.Movimiento=N'CAJA_CORREGIDA_PRODUCCION'
      AND h.Comentario LIKE N'%caja '+@FolioCaja+N'%'
      AND h.Comentario LIKE N'%'+@CorreccionRealizada+N'%'
);";

                await using (var cmd = new SqlCommand(sqlActualizar, cn, tx))
                {
                    cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value = cajaProduccionId;
                    cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                    cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value = inspeccionId;
                    cmd.Parameters.Add("@EstadoCajaID", SqlDbType.Int).Value = ProduccionCajaEstatus.FormadaProduccion;
                    cmd.Parameters.Add("@EstadoCajaNombre", SqlDbType.NVarChar, 100).Value = ProduccionCajaEstatus.Nombre(ProduccionCajaEstatus.FormadaProduccion);
                    cmd.Parameters.Add("@EstadoInspeccion", SqlDbType.NVarChar, 50).Value = estadoInspeccion;
                    cmd.Parameters.Add("@Comentario", SqlDbType.NVarChar, 1000).Value = comentarioCorreccion;
                    cmd.Parameters.Add("@FolioCaja", SqlDbType.NVarChar, 100).Value = folioCaja;
                    cmd.Parameters.Add("@CorreccionRealizada", SqlDbType.NVarChar, 1000).Value = correccionRealizada;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    await cmd.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();
                TempData["Success"] = $"Corrección de la caja {folioCaja} registrada. Ya puede reenviarse a Calidad.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] = "No fue posible registrar la corrección de la caja: " + ex.Message;
            }

            return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SolicitarLiberacionCaja(int cajaProduccionId)
        {
            if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");
            if (cajaProduccionId <= 0)
            {
                TempData["Error"] = "No se recibió una caja válida.";
                return RedirectToAction(nameof(Index));
            }

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var usuarioId = ObtenerUsuarioID();
            if (!await UsuarioPuedeGestionarCajasAsync(usuarioId, cn)) return AccesoDenegadoCajas();

            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
            var ejecucionProduccionId = 0;

            try
            {
                var caja = await ObtenerCajaProduccionAsync(cajaProduccionId, cn, tx);
                if (caja == null)
                {
                    await tx.RollbackAsync();
                    return NotFound();
                }

                ejecucionProduccionId = caja.EjecucionProduccionID;

                if (caja.EstadoCajaID == ProduccionCajaEstatus.PendienteCalidad)
                {
                    await tx.CommitAsync();
                    TempData["Info"] = "La caja ya se encuentra pendiente de revisión de Calidad.";
                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }

                if (caja.EstadoCajaID != ProduccionCajaEstatus.FormadaProduccion)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "Solo puedes solicitar liberación de una caja formada en Producción.";
                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }

                const string sqlEstadoCaja = @"
SELECT TOP (1)
    UPPER(LTRIM(RTRIM(ISNULL(EstatusCalidad,N'')))) AS EstatusCalidad,
    ISNULL(MotivoCalidad,N'') AS MotivoCalidad
FROM dbo.Produccion_Cajas WITH (UPDLOCK,HOLDLOCK)
WHERE CajaProduccionID=@CajaProduccionID
  AND EjecucionProduccionID=@EjecucionProduccionID
  AND Activo=1;";

                string estatusCalidad;
                string motivoCalidad;

                await using (var cmd = new SqlCommand(sqlEstadoCaja, cn, tx))
                {
                    cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value = cajaProduccionId;
                    cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                    await using var rd = await cmd.ExecuteReaderAsync();

                    if (!await rd.ReadAsync())
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = "No fue posible consultar el estado de Calidad de la caja.";
                        return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                    }

                    estatusCalidad = rd["EstatusCalidad"]?.ToString()?.Trim() ?? string.Empty;
                    motivoCalidad = rd["MotivoCalidad"]?.ToString()?.Trim() ?? string.Empty;
                }

                if (estatusCalidad == "DEVUELTA")
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = string.IsNullOrWhiteSpace(motivoCalidad)
                        ? "La caja fue devuelta por Calidad. Registra la corrección realizada antes de reenviarla."
                        : $"La caja fue devuelta por Calidad: {motivoCalidad}. Registra la corrección realizada antes de reenviarla.";
                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }

                if (!string.IsNullOrWhiteSpace(estatusCalidad) && estatusCalidad != "CORREGIDA" && estatusCalidad != "FORMADA")
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = $"La caja no puede enviarse a Calidad porque actualmente tiene el estatus {estatusCalidad}.";
                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }

                var validacion = await ValidarEnvioCajaCalidadAsync(ejecucionProduccionId, cn, tx);
                if (!validacion.Permitido || !validacion.InspeccionID.HasValue)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = validacion.Mensaje;
                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }

                var esReenvio = estatusCalidad == "CORREGIDA";
                var movimiento = esReenvio ? "CAJA_REENVIADA_DESDE_PRODUCCION" : "CAJA_RECIBIDA_DESDE_PRODUCCION";
                var comentario = esReenvio
                    ? $"Producción reenvió la caja {caja.FolioCaja} después de registrar su corrección. Cantidad: {caja.CantidadPiezas} pieza(s). Tipo: {caja.TipoCaja}."
                    : $"Producción envió la caja {caja.FolioCaja} a Calidad. Cantidad: {caja.CantidadPiezas} pieza(s). Tipo: {caja.TipoCaja}.";

                const string sql = @"
UPDATE dbo.Produccion_Cajas
SET EstadoCajaID=@EstadoCajaID,
    EstadoCajaNombre=@EstadoCajaNombre,
    FechaSolicitudCalidad=GETDATE(),
    UsuarioSolicitudCalidadID=@UsuarioID,
    EstatusCalidad=N'PENDIENTE',
    EtiquetaVerde=0,
    FechaLiberacionCalidad=NULL,
    AuditorCalidadUsuarioID=NULL,
    UsuarioCalidadID=NULL,
    ResultadoCalidad=NULL,
    MotivoCalidad=NULL,
    FechaZonaVerde=NULL,
    UsuarioZonaVerdeID=NULL,
    FechaSalidaProduccion=NULL,
    UsuarioSalidaProduccionID=NULL,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=GETDATE()
WHERE CajaProduccionID=@CajaProduccionID
  AND EjecucionProduccionID=@EjecucionProduccionID
  AND Activo=1
  AND EstadoCajaID=@EstadoActual
  AND
  (
      UPPER(LTRIM(RTRIM(ISNULL(EstatusCalidad,N'')))) IN (N'',N'FORMADA',N'CORREGIDA')
  );

IF @@ROWCOUNT<>1
    THROW 51060,'La caja cambió de estado mientras se enviaba a Calidad.',1;

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
    @InspeccionID,
    @Movimiento,
    N'MONITOREO_ACTIVO',
    N'MONITOREO_ACTIVO',
    NULL,
    NULL,
    @Comentario,
    @UsuarioID,
    GETDATE()
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.Calidad_InspeccionHistorial h
    WHERE h.InspeccionID=@InspeccionID
      AND h.Movimiento=@Movimiento
      AND h.Comentario LIKE N'%'+@FolioCaja+N'%'
      AND h.FechaMovimiento>=DATEADD(SECOND,-5,GETDATE())
);";

                await using (var cmd = new SqlCommand(sql, cn, tx))
                {
                    cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value = cajaProduccionId;
                    cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                    cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value = validacion.InspeccionID.Value;
                    cmd.Parameters.Add("@EstadoCajaID", SqlDbType.Int).Value = ProduccionCajaEstatus.PendienteCalidad;
                    cmd.Parameters.Add("@EstadoCajaNombre", SqlDbType.NVarChar, 100).Value = ProduccionCajaEstatus.Nombre(ProduccionCajaEstatus.PendienteCalidad);
                    cmd.Parameters.Add("@EstadoActual", SqlDbType.Int).Value = ProduccionCajaEstatus.FormadaProduccion;
                    cmd.Parameters.Add("@Movimiento", SqlDbType.NVarChar, 100).Value = movimiento;
                    cmd.Parameters.Add("@Comentario", SqlDbType.NVarChar, 1000).Value = comentario.Length > 1000 ? comentario[..1000] : comentario;
                    cmd.Parameters.Add("@FolioCaja", SqlDbType.NVarChar, 100).Value = string.IsNullOrWhiteSpace(caja.FolioCaja) ? cajaProduccionId.ToString() : caja.FolioCaja;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    await cmd.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();
                TempData["Success"] = esReenvio
                    ? "Caja corregida y reenviada a Calidad para una nueva revisión."
                    : "Caja enviada a Calidad para revisión.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] = "No fue posible solicitar liberación de la caja: " + ex.Message;
            }

            return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MoverCajaZonaVerde(
     int cajaProduccionId)
        {
            if (!UsuarioEnSesion())
                return RedirectToAction("Login", "Login");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var usuarioId = ObtenerUsuarioID();

            var puedeGestionarCajas = await UsuarioPuedeGestionarCajasAsync(usuarioId, cn);

            if (!puedeGestionarCajas)
                return AccesoDenegadoCajas();

            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();

            int ejecucionProduccionId = 0;

            try
            {
                var caja = await ObtenerCajaProduccionAsync(cajaProduccionId, cn, tx);

                if (caja == null)
                {
                    await tx.RollbackAsync();
                    return NotFound();
                }

                ejecucionProduccionId = caja.EjecucionProduccionID;

                if (caja.EstadoCajaID != ProduccionCajaEstatus.LiberadaCalidad || !caja.EtiquetaVerde)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "No puedes mover esta caja a zona verde. Primero debe estar liberada por Calidad con etiqueta verde.";

                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }

                const string sql = @"
UPDATE dbo.Produccion_Cajas
SET
    EstadoCajaID = @EstadoCajaID,
    EstadoCajaNombre = @EstadoCajaNombre,
    FechaZonaVerde = GETDATE(),
    UsuarioZonaVerdeID = @UsuarioID,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE CajaProduccionID = @CajaProduccionID
  AND Activo = 1;";

                await using var cmd = new SqlCommand(sql, cn, tx);

                cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value =
                    cajaProduccionId;

                cmd.Parameters.Add("@EstadoCajaID", SqlDbType.Int).Value =
                    ProduccionCajaEstatus.ZonaVerde;

                cmd.Parameters.Add("@EstadoCajaNombre", SqlDbType.NVarChar, 100).Value =
                    ProduccionCajaEstatus.Nombre(ProduccionCajaEstatus.ZonaVerde);

                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                    usuarioId;

                await cmd.ExecuteNonQueryAsync();

                await tx.CommitAsync();

                TempData["Success"] =
                    "Caja movida a zona verde.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                TempData["Error"] =
                    "No fue posible mover la caja a zona verde: " + ex.Message;
            }

            return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EscanearSalidaCaja(
     int cajaProduccionId,
     string? etiquetaEscaneada)
        {
            if (!UsuarioEnSesion())
                return RedirectToAction("Login", "Login");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var usuarioId = ObtenerUsuarioID();

            var puedeGestionarCajas = await UsuarioPuedeGestionarCajasAsync(usuarioId, cn);

            if (!puedeGestionarCajas)
                return AccesoDenegadoCajas();

            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();

            int ejecucionProduccionId = 0;

            try
            {
                var caja = await ObtenerCajaProduccionAsync(cajaProduccionId, cn, tx);

                if (caja == null)
                {
                    await tx.RollbackAsync();
                    return NotFound();
                }

                ejecucionProduccionId = caja.EjecucionProduccionID;

                if (caja.EstadoCajaID != ProduccionCajaEstatus.ZonaVerde)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "Solo puedes escanear salida de Producción cuando la caja ya está en zona verde.";

                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }

                if (!string.IsNullOrWhiteSpace(caja.EtiquetaFolio) &&
                    !string.IsNullOrWhiteSpace(etiquetaEscaneada) &&
                    !string.Equals(caja.EtiquetaFolio.Trim(), etiquetaEscaneada.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "La etiqueta escaneada no coincide con la etiqueta registrada en la caja.";

                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }

                const string sql = @"
UPDATE dbo.Produccion_Cajas
SET
    EstadoCajaID = @EstadoCajaID,
    EstadoCajaNombre = @EstadoCajaNombre,
    FechaSalidaProduccion = GETDATE(),
    UsuarioSalidaProduccionID = @UsuarioID,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE CajaProduccionID = @CajaProduccionID
  AND Activo = 1;";

                await using var cmd = new SqlCommand(sql, cn, tx);

                cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value =
                    cajaProduccionId;

                cmd.Parameters.Add("@EstadoCajaID", SqlDbType.Int).Value =
                    ProduccionCajaEstatus.SalidaProduccion;

                cmd.Parameters.Add("@EstadoCajaNombre", SqlDbType.NVarChar, 100).Value =
                    ProduccionCajaEstatus.Nombre(ProduccionCajaEstatus.SalidaProduccion);

                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                    usuarioId;

                await cmd.ExecuteNonQueryAsync();

                await tx.CommitAsync();

                TempData["Success"] =
                    "Salida de Producción escaneada correctamente. Pendiente recepción de Almacén PT.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                TempData["Error"] =
                    "No fue posible escanear la salida de Producción: " + ex.Message;
            }

            return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
        }


        private async Task<ProduccionOperadorCajasVm?> ObtenerCajasProduccionVmAsync(int ejecucionProduccionId, SqlConnection cn)
        {
            const string sql = @"
SELECT TOP(1)
    e.EjecucionProduccionID,e.ProgramaProduccionID,e.SolicitudProduccionID,e.SolicitudProduccionDetalleID,e.ParteID,
    s.FolioSolicitud,s.NumeroOFRecibida,pp.ClienteNombre,e.MaquinaCodigo,e.MaquinaNombre,e.NumeroParte,e.ReferenciaSAP,
    e.DescripcionParte,e.MoldeCodigo,
    COALESCE(NULLIF(d.MaterialCodigo,N''),NULLIF(pp.MaterialCodigo,N'')) AS MaterialCodigo,
    COALESCE(NULLIF(d.MaterialDescripcion,N''),NULLIF(pp.MaterialDescripcion,N'')) AS MaterialDescripcion,
    COALESCE(NULLIF(d.EmbalajeCodigo,N''),NULLIF(pp.EmbalajeCodigo,N'')) AS EmbalajeCodigo,
    COALESCE(NULLIF(d.EmbalajeDescripcion,N''),NULLIF(pp.EmbalajeDescripcion,N'')) AS EmbalajeDescripcion,
    d.PiezasPorEmbalaje,d.CantidadEmbalajes,
    ISNULL(e.CantidadPlaneada,0) AS CantidadPlaneada,
    ISNULL(e.CantidadOKTotal,0) AS CantidadOKTotal,
    ISNULL(e.CantidadSospechosaTotal,0) AS CantidadSospechosaTotal,
    ISNULL(e.CantidadScrapTotal,0) AS CantidadScrapTotal,
    e.EstatusID,
    CASE WHEN EXISTS(SELECT 1 FROM dbo.Produccion_Paros p WHERE p.EjecucionProduccionID=e.EjecucionProduccionID AND p.Activo=1 AND p.FechaFinParo IS NULL) THEN 1 ELSE 0 END AS TieneParoAbierto
FROM dbo.Produccion_Ejecucion e
LEFT JOIN dbo.SolicitudesProduccion s ON s.SolicitudProduccionID=e.SolicitudProduccionID AND s.Activo=1
LEFT JOIN dbo.SolicitudesProduccionDetalle d ON d.SolicitudProduccionDetalleID=e.SolicitudProduccionDetalleID AND d.Activo=1
LEFT JOIN dbo.Planeacion_ProgramaProduccion pp ON pp.ProgramaProduccionID=e.ProgramaProduccionID AND pp.Activo=1
WHERE e.EjecucionProduccionID=@EjecucionProduccionID AND e.Activo=1;";
            ProduccionOperadorCajasVm? vm = null;
            await using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                await using var rd = await cmd.ExecuteReaderAsync();
                if (!await rd.ReadAsync()) return null;
                vm = new ProduccionOperadorCajasVm
                {
                    EjecucionProduccionID = Entero(rd, "EjecucionProduccionID"),
                    ProgramaProduccionID = Entero(rd, "ProgramaProduccionID"),
                    SolicitudProduccionID = NullableEntero(rd, "SolicitudProduccionID"),
                    SolicitudProduccionDetalleID = NullableEntero(rd, "SolicitudProduccionDetalleID"),
                    ParteID = NullableEntero(rd, "ParteID"),
                    FolioSolicitud = TextoNullable(rd, "FolioSolicitud"),
                    NumeroOFRecibida = TextoNullable(rd, "NumeroOFRecibida"),
                    ClienteNombre = TextoNullable(rd, "ClienteNombre"),
                    MaquinaCodigo = TextoNullable(rd, "MaquinaCodigo"),
                    MaquinaNombre = TextoNullable(rd, "MaquinaNombre"),
                    NumeroParte = TextoNullable(rd, "NumeroParte"),
                    ReferenciaSAP = TextoNullable(rd, "ReferenciaSAP"),
                    DescripcionParte = TextoNullable(rd, "DescripcionParte"),
                    MoldeCodigo = TextoNullable(rd, "MoldeCodigo"),
                    MaterialCodigo = TextoNullable(rd, "MaterialCodigo"),
                    MaterialDescripcion = TextoNullable(rd, "MaterialDescripcion"),
                    EmbalajeCodigo = TextoNullable(rd, "EmbalajeCodigo"),
                    EmbalajeDescripcion = TextoNullable(rd, "EmbalajeDescripcion"),
                    PiezasPorEmbalaje = rd["PiezasPorEmbalaje"] == DBNull.Value ? null : Convert.ToDecimal(rd["PiezasPorEmbalaje"]),
                    CantidadEmbalajes = rd["CantidadEmbalajes"] == DBNull.Value ? null : Convert.ToDecimal(rd["CantidadEmbalajes"]),
                    CantidadPlaneada = Entero(rd, "CantidadPlaneada"),
                    CantidadOKTotal = Entero(rd, "CantidadOKTotal"),
                    CantidadSospechosaTotal = Entero(rd, "CantidadSospechosaTotal"),
                    CantidadScrapTotal = Entero(rd, "CantidadScrapTotal"),
                    EstatusID = Entero(rd, "EstatusID"),
                    TieneParoAbierto = Booleano(rd, "TieneParoAbierto")
                };
            }
            vm.Cajas = await ObtenerCajasPorEjecucionAsync(ejecucionProduccionId, cn);
            var consumo = await ObtenerConsumoCajasEjecucionAsync(ejecucionProduccionId, cn, null);
            vm.CantidadOKEnCajas = consumo.Ok;
            vm.CantidadSospechosaEnCajas = consumo.Sospechoso;
            vm.CantidadScrapEnCajas = consumo.Scrap;
            vm.CantidadRetencionEnCajas = consumo.Retencion;
            vm.SiguienteNumeroCaja = vm.Cajas.Any() ? vm.Cajas.Max(x => x.NumeroCaja) + 1 : 1;
            vm.PuedeFormarCaja = vm.EstatusID == ProduccionEstatus.EnProduccion && !vm.TieneParoAbierto;
            vm.CajasIncompletasDisponibles = vm.ParteID.HasValue ? await ObtenerCajasIncompletasCompatiblesAsync(ejecucionProduccionId, vm.ParteID.Value, vm.PiezasPorCajaSugeridas, cn) : new List<ProduccionCajaIncompletaDisponibleVm>();
            return vm;
        }


        private async Task<List<ProduccionOperadorCajaVm>> ObtenerCajasPorEjecucionAsync(int ejecucionProduccionId, SqlConnection cn)
        {
            var lista = new List<ProduccionOperadorCajaVm>();
            const string sql = @"
SELECT
    CajaProduccionID,EjecucionProduccionID,ISNULL(ProgramaProduccionID,0) AS ProgramaProduccionID,
    SolicitudProduccionID,SolicitudProduccionDetalleID,ISNULL(NumeroCaja,0) AS NumeroCaja,FolioCaja,
    ISNULL(CantidadPiezas,ISNULL(Cantidad,0)) AS CantidadPiezas,ISNULL(TipoCaja,N'OK') AS TipoCaja,
    LoteMaterial,ISNULL(EtiquetaFolio,Etiqueta) AS EtiquetaFolio,ISNULL(EtiquetaVerde,0) AS EtiquetaVerde,
    ISNULL(EstadoCajaID,1) AS EstadoCajaID,ISNULL(EstadoCajaNombre,N'Formada en Producción') AS EstadoCajaNombre,
    ISNULL(FechaFormacion,FechaCreacion) AS FechaFormacion,UsuarioFormacionID,FechaSolicitudCalidad,UsuarioSolicitudCalidadID,
    FechaLiberacionCalidad,UsuarioCalidadID,ResultadoCalidad,MotivoCalidad,FechaZonaVerde,UsuarioZonaVerdeID,
    FechaSalidaProduccion,UsuarioSalidaProduccionID,FechaRecepcionAlmacen,UsuarioAlmacenID,Observaciones,
    ISNULL(EsProductoIncompleto,0) AS EsProductoIncompleto,EstadoProductoIncompleto,EjecucionReservaID,ProgramaReservaID,
    SolicitudReservaID,SolicitudDetalleReservaID,FechaReservaIncompleto,UsuarioReservaIncompletoID,FechaCompletadoIncompleto,
    CapacidadObjetivoCaja,CantidadPendienteCompletar,EtiquetaBlanca,
    CodigoBarrasOrigen,NumeroOFEtiqueta,NumeroParteEtiqueta,DesignacionEtiqueta,CantidadEtiqueta,LoteEtiqueta,
    FechaEscaneoProduccion,UsuarioEscaneoProduccionID,FechaEscaneoCalidad,UsuarioEscaneoCalidadID
FROM dbo.Produccion_Cajas
WHERE EjecucionProduccionID=@EjecucionProduccionID
  AND Activo=1
ORDER BY NumeroCaja,CajaProduccionID;";
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync()) lista.Add(MapearCajaProduccion(rd));
            return lista;
        }
        private async Task<ProduccionOperadorCajaVm?> ObtenerCajaProduccionAsync(long cajaProduccionId, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
SELECT TOP(1)
    CajaProduccionID,EjecucionProduccionID,ISNULL(ProgramaProduccionID,0) AS ProgramaProduccionID,
    SolicitudProduccionID,SolicitudProduccionDetalleID,ISNULL(NumeroCaja,0) AS NumeroCaja,FolioCaja,
    ISNULL(CantidadPiezas,ISNULL(Cantidad,0)) AS CantidadPiezas,ISNULL(TipoCaja,N'OK') AS TipoCaja,
    LoteMaterial,ISNULL(EtiquetaFolio,Etiqueta) AS EtiquetaFolio,ISNULL(EtiquetaVerde,0) AS EtiquetaVerde,
    ISNULL(EstadoCajaID,1) AS EstadoCajaID,ISNULL(EstadoCajaNombre,N'Formada en Producción') AS EstadoCajaNombre,
    ISNULL(FechaFormacion,FechaCreacion) AS FechaFormacion,UsuarioFormacionID,FechaSolicitudCalidad,UsuarioSolicitudCalidadID,
    FechaLiberacionCalidad,UsuarioCalidadID,ResultadoCalidad,MotivoCalidad,FechaZonaVerde,UsuarioZonaVerdeID,
    FechaSalidaProduccion,UsuarioSalidaProduccionID,FechaRecepcionAlmacen,UsuarioAlmacenID,Observaciones,
    ISNULL(EsProductoIncompleto,0) AS EsProductoIncompleto,EstadoProductoIncompleto,EjecucionReservaID,ProgramaReservaID,
    SolicitudReservaID,SolicitudDetalleReservaID,FechaReservaIncompleto,UsuarioReservaIncompletoID,FechaCompletadoIncompleto,
    CapacidadObjetivoCaja,CantidadPendienteCompletar,EtiquetaBlanca,
    CodigoBarrasOrigen,NumeroOFEtiqueta,NumeroParteEtiqueta,DesignacionEtiqueta,CantidadEtiqueta,LoteEtiqueta,
    FechaEscaneoProduccion,UsuarioEscaneoProduccionID,FechaEscaneoCalidad,UsuarioEscaneoCalidadID
FROM dbo.Produccion_Cajas WITH(UPDLOCK,HOLDLOCK)
WHERE CajaProduccionID=@CajaProduccionID
  AND Activo=1;";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value = cajaProduccionId;
            await using var rd = await cmd.ExecuteReaderAsync();
            return await rd.ReadAsync() ? MapearCajaProduccion(rd) : null;
        }

        private static ProduccionOperadorCajaVm MapearCajaProduccion(SqlDataReader rd)
        {
            return new ProduccionOperadorCajaVm
            {
                CajaProduccionID = EnteroLargoCaja(rd, "CajaProduccionID"),
                EjecucionProduccionID = Entero(rd, "EjecucionProduccionID"),
                ProgramaProduccionID = Entero(rd, "ProgramaProduccionID"),
                SolicitudProduccionID = NullableEntero(rd, "SolicitudProduccionID"),
                SolicitudProduccionDetalleID = NullableEntero(rd, "SolicitudProduccionDetalleID"),
                NumeroCaja = Entero(rd, "NumeroCaja"),
                FolioCaja = TextoNullable(rd, "FolioCaja"),
                CantidadPiezas = Entero(rd, "CantidadPiezas"),
                TipoCaja = TextoNullable(rd, "TipoCaja") ?? ProduccionCajaTipo.Ok,
                LoteMaterial = TextoNullable(rd, "LoteMaterial"),
                EtiquetaFolio = TextoNullable(rd, "EtiquetaFolio"),
                EtiquetaVerde = Booleano(rd, "EtiquetaVerde"),
                EstadoCajaID = Entero(rd, "EstadoCajaID"),
                EstadoCajaNombre = TextoNullable(rd, "EstadoCajaNombre") ?? "Formada en Producción",
                FechaFormacion = Fecha(rd, "FechaFormacion"),
                UsuarioFormacionID = NullableEntero(rd, "UsuarioFormacionID"),
                FechaSolicitudCalidad = NullableFecha(rd, "FechaSolicitudCalidad"),
                UsuarioSolicitudCalidadID = NullableEntero(rd, "UsuarioSolicitudCalidadID"),
                FechaLiberacionCalidad = NullableFecha(rd, "FechaLiberacionCalidad"),
                UsuarioCalidadID = NullableEntero(rd, "UsuarioCalidadID"),
                ResultadoCalidad = TextoNullable(rd, "ResultadoCalidad"),
                MotivoCalidad = TextoNullable(rd, "MotivoCalidad"),
                FechaZonaVerde = NullableFecha(rd, "FechaZonaVerde"),
                UsuarioZonaVerdeID = NullableEntero(rd, "UsuarioZonaVerdeID"),
                FechaSalidaProduccion = NullableFecha(rd, "FechaSalidaProduccion"),
                UsuarioSalidaProduccionID = NullableEntero(rd, "UsuarioSalidaProduccionID"),
                FechaRecepcionAlmacen = NullableFecha(rd, "FechaRecepcionAlmacen"),
                UsuarioAlmacenID = NullableEntero(rd, "UsuarioAlmacenID"),
                Observaciones = TextoNullable(rd, "Observaciones"),
                EsProductoIncompleto = Booleano(rd, "EsProductoIncompleto"),
                EstadoProductoIncompleto = TextoNullable(rd, "EstadoProductoIncompleto"),
                EjecucionReservaID = NullableEntero(rd, "EjecucionReservaID"),
                ProgramaReservaID = NullableEntero(rd, "ProgramaReservaID"),
                SolicitudReservaID = NullableEntero(rd, "SolicitudReservaID"),
                SolicitudDetalleReservaID = NullableEntero(rd, "SolicitudDetalleReservaID"),
                FechaReservaIncompleto = NullableFecha(rd, "FechaReservaIncompleto"),
                UsuarioReservaIncompletoID = NullableEntero(rd, "UsuarioReservaIncompletoID"),
                FechaCompletadoIncompleto = NullableFecha(rd, "FechaCompletadoIncompleto"),
                CapacidadObjetivoCaja = NullableEntero(rd, "CapacidadObjetivoCaja"),
                CantidadPendienteCompletar = NullableEntero(rd, "CantidadPendienteCompletar"),
                EtiquetaBlanca = TextoNullable(rd, "EtiquetaBlanca"),
                CodigoBarrasOrigen = TextoNullable(rd, "CodigoBarrasOrigen"),
                NumeroOFEtiqueta = TextoNullable(rd, "NumeroOFEtiqueta"),
                NumeroParteEtiqueta = TextoNullable(rd, "NumeroParteEtiqueta"),
                DesignacionEtiqueta = TextoNullable(rd, "DesignacionEtiqueta"),
                CantidadEtiqueta = NullableEntero(rd, "CantidadEtiqueta"),
                LoteEtiqueta = TextoNullable(rd, "LoteEtiqueta"),
                FechaEscaneoProduccion = NullableFecha(rd, "FechaEscaneoProduccion"),
                UsuarioEscaneoProduccionID = NullableEntero(rd, "UsuarioEscaneoProduccionID"),
                FechaEscaneoCalidad = NullableFecha(rd, "FechaEscaneoCalidad"),
                UsuarioEscaneoCalidadID = NullableEntero(rd, "UsuarioEscaneoCalidadID"),
                ActivoParaCalculo = true
            };
        }
        private async Task<int> ObtenerSiguienteNumeroCajaAsync(
    int ejecucionProduccionId,
    SqlConnection cn,
    SqlTransaction tx)
        {
            const string sql = @"
SELECT ISNULL(MAX(NumeroCaja), 0) + 1
FROM dbo.Produccion_Cajas
WHERE EjecucionProduccionID = @EjecucionProduccionID
  AND Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value =
                ejecucionProduccionId;

            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        private async Task<int> ObtenerCantidadDisponibleParaCajaAsync(int ejecucionProduccionId, string tipoCaja, SqlConnection cn, SqlTransaction tx)
        {
            tipoCaja = NormalizarTipoCajaProduccion(tipoCaja);
            const string sql = @"
SELECT ISNULL(CantidadOKTotal,0) AS OKTotal,ISNULL(CantidadSospechosaTotal,0) AS SospechosaTotal,ISNULL(CantidadScrapTotal,0) AS ScrapTotal
FROM dbo.Produccion_Ejecucion WITH(UPDLOCK,HOLDLOCK)
WHERE EjecucionProduccionID=@EjecucionProduccionID AND Activo=1;";
            int okTotal;
            int sospechosaTotal;
            int scrapTotal;
            await using (var cmd = new SqlCommand(sql, cn, tx))
            {
                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                await using var rd = await cmd.ExecuteReaderAsync();
                if (!await rd.ReadAsync()) return 0;
                okTotal = Entero(rd, "OKTotal");
                sospechosaTotal = Entero(rd, "SospechosaTotal");
                scrapTotal = Entero(rd, "ScrapTotal");
            }
            var consumo = await ObtenerConsumoCajasEjecucionAsync(ejecucionProduccionId, cn, tx);
            return tipoCaja switch
            {
                ProduccionCajaTipo.Ok => Math.Max(0, okTotal - consumo.Ok),
                ProduccionCajaTipo.Incompleta => Math.Max(0, okTotal - consumo.Ok),
                ProduccionCajaTipo.Sospechoso => Math.Max(0, sospechosaTotal - consumo.Sospechoso - consumo.Retencion),
                ProduccionCajaTipo.Retencion => Math.Max(0, sospechosaTotal - consumo.Sospechoso - consumo.Retencion),
                ProduccionCajaTipo.Scrap => Math.Max(0, scrapTotal - consumo.Scrap),
                _ => 0
            };
        }

        private async Task<(int Ok, int Sospechoso, int Scrap, int Retencion)> ObtenerConsumoCajasEjecucionAsync(int ejecucionProduccionId, SqlConnection cn, SqlTransaction? tx)
        {
            const string sql = @"
SELECT
ISNULL(SUM(CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(c.TipoCaja,N'OK'))))=N'OK' THEN ISNULL(c.CantidadPiezas,ISNULL(c.Cantidad,0)) ELSE 0 END),0) AS OkNormal,
ISNULL(SUM(CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(c.TipoCaja,N''))))=N'SOSPECHOSO' THEN ISNULL(c.CantidadPiezas,ISNULL(c.Cantidad,0)) ELSE 0 END),0) AS Sospechoso,
ISNULL(SUM(CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(c.TipoCaja,N''))))=N'SCRAP' THEN ISNULL(c.CantidadPiezas,ISNULL(c.Cantidad,0)) ELSE 0 END),0) AS Scrap,
ISNULL(SUM(CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(c.TipoCaja,N''))))=N'RETENCION' THEN ISNULL(c.CantidadPiezas,ISNULL(c.Cantidad,0)) ELSE 0 END),0) AS Retencion
FROM dbo.Produccion_Cajas c
WHERE c.EjecucionProduccionID=@EjecucionProduccionID AND c.Activo=1
AND NOT EXISTS(SELECT 1 FROM dbo.Produccion_CajaOrigenDetalle od WHERE od.CajaProduccionID=c.CajaProduccionID AND od.Activo=1);
SELECT ISNULL(SUM(od.CantidadPiezas),0)
FROM dbo.Produccion_CajaOrigenDetalle od
INNER JOIN dbo.Produccion_Cajas c ON c.CajaProduccionID=od.CajaProduccionID AND c.Activo=1
WHERE od.EjecucionProduccionID=@EjecucionProduccionID AND od.Activo=1;";
            await using var cmd = tx == null ? new SqlCommand(sql, cn) : new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
            int okNormal;
            int sospechoso;
            int scrap;
            int retencion;
            int detalleOk;
            await using (var rd = await cmd.ExecuteReaderAsync())
            {
                if (!await rd.ReadAsync()) return (0, 0, 0, 0);
                okNormal = Entero(rd, "OkNormal");
                sospechoso = Entero(rd, "Sospechoso");
                scrap = Entero(rd, "Scrap");
                retencion = Entero(rd, "Retencion");
                if (!await rd.NextResultAsync() || !await rd.ReadAsync()) detalleOk = 0;
                else detalleOk = rd[0] == DBNull.Value ? 0 : Convert.ToInt32(rd[0]);
            }
            return (okNormal + detalleOk, sospechoso, scrap, retencion);
        }

        private async Task<int> ObtenerCantidadDetalleCajaPorEjecucionAsync(int ejecucionProduccionId, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
SELECT ISNULL(SUM(CantidadPiezas),0)
FROM dbo.Produccion_CajaOrigenDetalle WITH(UPDLOCK,HOLDLOCK)
WHERE EjecucionProduccionID=@EjecucionProduccionID AND Activo=1;";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        private async Task<List<ProduccionCajaIncompletaDisponibleVm>> ObtenerCajasIncompletasCompatiblesAsync(int ejecucionActualId, int parteId, int capacidadCajaActual, SqlConnection cn)
        {
            var lista = new List<ProduccionCajaIncompletaDisponibleVm>();
            const string sql = @"
SELECT c.CajaProduccionID,c.EjecucionProduccionID,ISNULL(c.ProgramaProduccionID,0) AS ProgramaProduccionID,
c.SolicitudProduccionID,c.SolicitudProduccionDetalleID,eOrigen.ParteID,eOrigen.NumeroParte,eOrigen.ReferenciaSAP,
c.FolioCaja,c.EtiquetaBlanca,ISNULL(c.CantidadPiezas,ISNULL(c.Cantidad,0)) AS CantidadPiezas,
ISNULL(c.CapacidadObjetivoCaja,0) AS CapacidadObjetivoCaja,
ISNULL(c.CantidadPendienteCompletar,CASE WHEN ISNULL(c.CapacidadObjetivoCaja,0)>ISNULL(c.CantidadPiezas,ISNULL(c.Cantidad,0)) THEN c.CapacidadObjetivoCaja-ISNULL(c.CantidadPiezas,ISNULL(c.Cantidad,0)) ELSE 0 END) AS CantidadPendienteCompletar,
ISNULL(c.EstadoProductoIncompleto,N'RESERVADA') AS EstadoProductoIncompleto,
c.EjecucionReservaID,c.ProgramaReservaID,c.SolicitudReservaID,c.SolicitudDetalleReservaID,
ISNULL(c.FechaFormacion,c.FechaCreacion) AS FechaFormacion,c.FechaReservaIncompleto
FROM dbo.Planeacion_ProductoIncompletoApartado a
INNER JOIN dbo.Produccion_Cajas c ON c.CajaProduccionID=a.CajaProduccionID
INNER JOIN dbo.Produccion_Ejecucion eOrigen ON eOrigen.EjecucionProduccionID=c.EjecucionProduccionID
WHERE a.EjecucionProduccionID=@EjecucionActualID
  AND a.Activo=1
  AND a.EstatusID=4
  AND c.Activo=1
  AND ISNULL(c.EsProductoIncompleto,0)=1
  AND eOrigen.ParteID=@ParteID
  AND c.EjecucionProduccionID<>@EjecucionActualID
  AND c.EjecucionReservaID=@EjecucionActualID
  AND UPPER(LTRIM(RTRIM(ISNULL(c.EstadoProductoIncompleto,N'')))) IN(N'RESERVADA',N'EN_COMPLETADO')
  AND(@CapacidadCaja<=0 OR ISNULL(c.CapacidadObjetivoCaja,0)=@CapacidadCaja)
ORDER BY ISNULL(c.FechaReservaIncompleto,c.FechaFormacion),c.CajaProduccionID;";
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@EjecucionActualID", SqlDbType.Int).Value = ejecucionActualId;
            cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = parteId;
            cmd.Parameters.Add("@CapacidadCaja", SqlDbType.Int).Value = capacidadCajaActual;
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                lista.Add(new ProduccionCajaIncompletaDisponibleVm
                {
                    CajaProduccionID = EnteroLargoCaja(rd, "CajaProduccionID"),
                    EjecucionProduccionID = Entero(rd, "EjecucionProduccionID"),
                    ProgramaProduccionID = Entero(rd, "ProgramaProduccionID"),
                    SolicitudProduccionID = NullableEntero(rd, "SolicitudProduccionID"),
                    SolicitudProduccionDetalleID = NullableEntero(rd, "SolicitudProduccionDetalleID"),
                    ParteID = NullableEntero(rd, "ParteID"),
                    NumeroParte = TextoNullable(rd, "NumeroParte"),
                    ReferenciaSAP = TextoNullable(rd, "ReferenciaSAP"),
                    FolioCaja = TextoNullable(rd, "FolioCaja"),
                    EtiquetaBlanca = TextoNullable(rd, "EtiquetaBlanca"),
                    CantidadPiezas = Entero(rd, "CantidadPiezas"),
                    CapacidadObjetivoCaja = Entero(rd, "CapacidadObjetivoCaja"),
                    CantidadPendienteCompletar = Entero(rd, "CantidadPendienteCompletar"),
                    EstadoProductoIncompleto = TextoNullable(rd, "EstadoProductoIncompleto") ?? ProduccionProductoIncompletoEstado.Reservada,
                    EjecucionReservaID = NullableEntero(rd, "EjecucionReservaID"),
                    ProgramaReservaID = NullableEntero(rd, "ProgramaReservaID"),
                    SolicitudReservaID = NullableEntero(rd, "SolicitudReservaID"),
                    SolicitudDetalleReservaID = NullableEntero(rd, "SolicitudDetalleReservaID"),
                    FechaFormacion = Fecha(rd, "FechaFormacion"),
                    FechaReservaIncompleto = NullableFecha(rd, "FechaReservaIncompleto")
                });
            }
            return lista;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReservarCajaIncompleta(ProduccionReservarCajaIncompletaPostVm vm)
        {
            if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");
            if (vm.CajaProduccionID <= 0 || vm.EjecucionProduccionID <= 0) { TempData["Error"] = "No se recibió correctamente la etiqueta blanca."; return RedirectToAction(nameof(Index)); }
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            var usuarioId = ObtenerUsuarioID();
            if (!await UsuarioPuedeGestionarCajasAsync(usuarioId, cn)) return AccesoDenegadoCajas();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var ejecucion = await ObtenerEjecucionAsync(vm.EjecucionProduccionID, cn, tx);
                if (ejecucion == null) { await tx.RollbackAsync(); return NotFound(); }
                if (!ejecucion.ParteID.HasValue || ejecucion.ParteID.Value <= 0) { await tx.RollbackAsync(); TempData["Error"] = "La ejecución no tiene una pieza válida relacionada."; return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID }); }
                const string sql = @"
SELECT TOP(1)c.CajaProduccionID,c.EtiquetaBlanca,ISNULL(c.CapacidadObjetivoCaja,0) AS CapacidadObjetivoCaja,
ISNULL(c.CantidadPendienteCompletar,0) AS CantidadPendienteCompletar,
ISNULL(c.EstadoProductoIncompleto,N'') AS EstadoProductoIncompleto,c.EjecucionReservaID,eOrigen.ParteID,
a.ProductoIncompletoApartadoID,a.EstatusID AS EstatusApartado
FROM dbo.Planeacion_ProductoIncompletoApartado a WITH(UPDLOCK,HOLDLOCK)
INNER JOIN dbo.Produccion_Cajas c WITH(UPDLOCK,HOLDLOCK) ON c.CajaProduccionID=a.CajaProduccionID
INNER JOIN dbo.Produccion_Ejecucion eOrigen ON eOrigen.EjecucionProduccionID=c.EjecucionProduccionID
WHERE a.CajaProduccionID=@CajaProduccionID
  AND a.EjecucionProduccionID=@EjecucionProduccionID
  AND a.ProgramaProduccionID=@ProgramaProduccionID
  AND a.Activo=1
  AND a.EstatusID=4
  AND c.Activo=1
  AND ISNULL(c.EsProductoIncompleto,0)=1;";
                long apartadoId;
                int parteOrigen;
                int capacidadCaja;
                int pendiente;
                int? reservaActual;
                string etiquetaBlanca;
                string estado;
                await using (var cmd = new SqlCommand(sql, cn, tx))
                {
                    cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value = vm.CajaProduccionID;
                    cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = vm.EjecucionProduccionID;
                    cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = ejecucion.ProgramaProduccionID;
                    await using var rd = await cmd.ExecuteReaderAsync();
                    if (!await rd.ReadAsync())
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = "Esta etiqueta blanca no fue asignada por Planeación a esta OF. Producción no puede reservar producto incompleto libre.";
                        return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
                    }
                    apartadoId = Convert.ToInt64(rd["ProductoIncompletoApartadoID"]);
                    parteOrigen = rd["ParteID"] == DBNull.Value ? 0 : Convert.ToInt32(rd["ParteID"]);
                    capacidadCaja = Convert.ToInt32(rd["CapacidadObjetivoCaja"]);
                    pendiente = Convert.ToInt32(rd["CantidadPendienteCompletar"]);
                    reservaActual = rd["EjecucionReservaID"] == DBNull.Value ? null : Convert.ToInt32(rd["EjecucionReservaID"]);
                    etiquetaBlanca = rd["EtiquetaBlanca"]?.ToString() ?? vm.CajaProduccionID.ToString();
                    estado = rd["EstadoProductoIncompleto"]?.ToString()?.Trim().ToUpperInvariant() ?? string.Empty;
                }
                if (parteOrigen != ejecucion.ParteID.Value) { await tx.RollbackAsync(); TempData["Error"] = $"La etiqueta blanca {etiquetaBlanca} corresponde a una pieza diferente."; return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID }); }
                if (pendiente <= 0) { await tx.RollbackAsync(); TempData["Error"] = $"La etiqueta blanca {etiquetaBlanca} ya no tiene piezas pendientes por completar."; return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID }); }
                if (reservaActual.HasValue && reservaActual.Value != vm.EjecucionProduccionID) { await tx.RollbackAsync(); TempData["Error"] = $"La etiqueta blanca {etiquetaBlanca} quedó relacionada con otra ejecución. Solicita revisión de Planeación."; return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID }); }
                if (estado != ProduccionProductoIncompletoEstado.Reservada && estado != ProduccionProductoIncompletoEstado.EnCompletado) { await tx.RollbackAsync(); TempData["Error"] = $"La etiqueta blanca {etiquetaBlanca} no se encuentra en estado válido para esta OF."; return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID }); }
                if (ejecucion.SolicitudProduccionDetalleID.HasValue)
                {
                    const string sqlCapacidad = @"SELECT TOP(1)PiezasPorEmbalaje FROM dbo.SolicitudesProduccionDetalle WITH(UPDLOCK,HOLDLOCK) WHERE SolicitudProduccionDetalleID=@SolicitudProduccionDetalleID AND Activo=1;";
                    await using var cmdCapacidad = new SqlCommand(sqlCapacidad, cn, tx);
                    cmdCapacidad.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value = ejecucion.SolicitudProduccionDetalleID.Value;
                    var valor = await cmdCapacidad.ExecuteScalarAsync();
                    var capacidadActual = valor == null || valor == DBNull.Value ? 0 : Convert.ToInt32(Math.Floor(Convert.ToDecimal(valor)));
                    if (capacidadActual <= 0 || capacidadActual != capacidadCaja) { await tx.RollbackAsync(); TempData["Error"] = $"La capacidad del embalaje de esta OF no coincide con la etiqueta blanca {etiquetaBlanca}."; return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID }); }
                }
                const string sqlSincronizar = @"
UPDATE dbo.Produccion_Cajas
SET EstadoProductoIncompleto=CASE WHEN EstadoProductoIncompleto=N'EN_COMPLETADO' THEN N'EN_COMPLETADO' ELSE N'RESERVADA' END,
EjecucionReservaID=@EjecucionProduccionID,ProgramaReservaID=@ProgramaProduccionID,SolicitudReservaID=@SolicitudProduccionID,SolicitudDetalleReservaID=@SolicitudDetalleReservaID,
FechaReservaIncompleto=COALESCE(FechaReservaIncompleto,SYSDATETIME()),UsuarioReservaIncompletoID=COALESCE(UsuarioReservaIncompletoID,@UsuarioID),
UsuarioModificacionID=@UsuarioID,FechaModificacion=SYSDATETIME()
WHERE CajaProduccionID=@CajaProduccionID AND Activo=1 AND ISNULL(EsProductoIncompleto,0)=1
AND(EjecucionReservaID IS NULL OR EjecucionReservaID=@EjecucionProduccionID);
IF @@ROWCOUNT<>1 THROW 51110,'La etiqueta blanca cambió de asignación mientras se validaba.',1;";
                await using (var cmd = new SqlCommand(sqlSincronizar, cn, tx))
                {
                    cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value = vm.CajaProduccionID;
                    cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucion.EjecucionProduccionID;
                    cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = ejecucion.ProgramaProduccionID;
                    cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = (object?)ejecucion.SolicitudProduccionID ?? DBNull.Value;
                    cmd.Parameters.Add("@SolicitudDetalleReservaID", SqlDbType.Int).Value = (object?)ejecucion.SolicitudProduccionDetalleID ?? DBNull.Value;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    await cmd.ExecuteNonQueryAsync();
                }
                await tx.CommitAsync();
                TempData["Info"] = $"Etiqueta blanca {etiquetaBlanca} confirmada para esta OF. Contiene producto previo y faltan {pendiente:N0} pieza(s) para completar la caja.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] = "No fue posible validar la etiqueta blanca asignada: " + ex.Message;
            }
            return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CompletarCajaIncompleta(ProduccionCompletarCajaIncompletaPostVm vm)
        {
            if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");
            if (vm.CajaProduccionID <= 0 || vm.EjecucionProduccionID <= 0 || vm.CantidadPiezas <= 0) { TempData["Error"] = "Los datos para completar la caja no son válidos."; return RedirectToAction(nameof(Index)); }
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            var usuarioId = ObtenerUsuarioID();
            if (!await UsuarioPuedeGestionarCajasAsync(usuarioId, cn)) return AccesoDenegadoCajas();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var ejecucion = await ObtenerEjecucionAsync(vm.EjecucionProduccionID, cn, tx);
                if (ejecucion == null) { await tx.RollbackAsync(); return NotFound(); }
                if (ejecucion.EstatusID != ProduccionEstatus.EnProduccion) { await tx.RollbackAsync(); TempData["Error"] = "Solo puedes completar producto incompleto cuando la OF está en producción."; return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID }); }
                if (await TieneParoAbiertoAsync(vm.EjecucionProduccionID, cn, tx)) { await tx.RollbackAsync(); TempData["Error"] = "No puedes completar cajas mientras exista un paro abierto."; return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID }); }
                const string sqlCaja = @"
SELECT c.CajaProduccionID,c.EjecucionProduccionID,ISNULL(c.CantidadPiezas,ISNULL(c.Cantidad,0)) AS CantidadPiezas,
ISNULL(c.CapacidadObjetivoCaja,0) AS CapacidadObjetivoCaja,ISNULL(c.CantidadPendienteCompletar,0) AS CantidadPendienteCompletar,
ISNULL(c.EstadoProductoIncompleto,N'') AS EstadoProductoIncompleto,c.EjecucionReservaID,c.EtiquetaBlanca,eOrigen.ParteID,
a.ProductoIncompletoApartadoID
FROM dbo.Planeacion_ProductoIncompletoApartado a WITH(UPDLOCK,HOLDLOCK)
INNER JOIN dbo.Produccion_Cajas c WITH(UPDLOCK,HOLDLOCK) ON c.CajaProduccionID=a.CajaProduccionID
INNER JOIN dbo.Produccion_Ejecucion eOrigen ON eOrigen.EjecucionProduccionID=c.EjecucionProduccionID
WHERE a.CajaProduccionID=@CajaProduccionID
  AND a.EjecucionProduccionID=@EjecucionProduccionID
  AND a.ProgramaProduccionID=@ProgramaProduccionID
  AND a.Activo=1
  AND a.EstatusID=4
  AND c.Activo=1
  AND ISNULL(c.EsProductoIncompleto,0)=1;";
                int cantidadActual, capacidad, pendiente, parteOrigen;
                int? reservaId;
                string estado, etiquetaBlanca;
                await using (var cmd = new SqlCommand(sqlCaja, cn, tx))
                {
                    cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value = vm.CajaProduccionID;
                    cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = vm.EjecucionProduccionID;
                    cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = ejecucion.ProgramaProduccionID;
                    await using var rd = await cmd.ExecuteReaderAsync();
                    if (!await rd.ReadAsync()) { await tx.RollbackAsync(); TempData["Error"] = "La etiqueta blanca no fue asignada por Planeación a esta ejecución o ya fue aplicada."; return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID }); }
                    cantidadActual = Convert.ToInt32(rd["CantidadPiezas"]);
                    capacidad = Convert.ToInt32(rd["CapacidadObjetivoCaja"]);
                    pendiente = Convert.ToInt32(rd["CantidadPendienteCompletar"]);
                    parteOrigen = rd["ParteID"] == DBNull.Value ? 0 : Convert.ToInt32(rd["ParteID"]);
                    reservaId = rd["EjecucionReservaID"] == DBNull.Value ? null : Convert.ToInt32(rd["EjecucionReservaID"]);
                    estado = rd["EstadoProductoIncompleto"]?.ToString()?.Trim().ToUpperInvariant() ?? string.Empty;
                    etiquetaBlanca = rd["EtiquetaBlanca"]?.ToString() ?? vm.CajaProduccionID.ToString();
                }
                if (!reservaId.HasValue || reservaId.Value != vm.EjecucionProduccionID) { await tx.RollbackAsync(); TempData["Error"] = $"La etiqueta blanca {etiquetaBlanca} no está relacionada con esta ejecución."; return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID }); }
                if (estado != ProduccionProductoIncompletoEstado.Reservada && estado != ProduccionProductoIncompletoEstado.EnCompletado) { await tx.RollbackAsync(); TempData["Error"] = "La etiqueta blanca no se encuentra en estado válido para completarse."; return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID }); }
                if (!ejecucion.ParteID.HasValue || ejecucion.ParteID.Value != parteOrigen) { await tx.RollbackAsync(); TempData["Error"] = "La pieza de la OF actual no coincide con la etiqueta blanca asignada."; return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID }); }
                if (capacidad <= 0 || pendiente <= 0) { await tx.RollbackAsync(); TempData["Error"] = "La etiqueta blanca no tiene una capacidad pendiente válida."; return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID }); }
                if (vm.CantidadPiezas > pendiente) { await tx.RollbackAsync(); TempData["Error"] = $"Solo faltan {pendiente:N0} pieza(s) para completar {etiquetaBlanca}."; return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID }); }
                var disponible = await ObtenerCantidadDisponibleParaCajaAsync(vm.EjecucionProduccionID, ProduccionCajaTipo.Ok, cn, tx);
                if (vm.CantidadPiezas > disponible) { await tx.RollbackAsync(); TempData["Error"] = $"La OF solamente tiene {disponible:N0} pieza(s) OK disponibles para agregar a la caja."; return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID }); }
                var nuevaCantidad = cantidadActual + vm.CantidadPiezas;
                var nuevoPendiente = Math.Max(0, capacidad - nuevaCantidad);
                const string sqlDetalle = @"
INSERT INTO dbo.Produccion_CajaOrigenDetalle
(CajaProduccionID,EjecucionProduccionID,ProgramaProduccionID,SolicitudProduccionID,SolicitudProduccionDetalleID,ReleaseID,ReleaseDetalleID,CantidadPiezas,TipoMovimiento,Observaciones,UsuarioCreacionID,FechaCreacion,Activo)
VALUES
(@CajaProduccionID,@EjecucionProduccionID,@ProgramaProduccionID,@SolicitudProduccionID,@SolicitudProduccionDetalleID,@ReleaseID,@ReleaseDetalleID,@CantidadPiezas,N'COMPLETADO',@Observaciones,@UsuarioID,SYSDATETIME(),1);";
                await using (var cmd = new SqlCommand(sqlDetalle, cn, tx))
                {
                    cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value = vm.CajaProduccionID;
                    cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucion.EjecucionProduccionID;
                    cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = ejecucion.ProgramaProduccionID;
                    cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = (object?)ejecucion.SolicitudProduccionID ?? DBNull.Value;
                    cmd.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value = (object?)ejecucion.SolicitudProduccionDetalleID ?? DBNull.Value;
                    cmd.Parameters.Add("@ReleaseID", SqlDbType.Int).Value = (object?)ejecucion.ReleaseID ?? DBNull.Value;
                    cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = (object?)ejecucion.ReleaseDetalleID ?? DBNull.Value;
                    cmd.Parameters.Add("@CantidadPiezas", SqlDbType.Int).Value = vm.CantidadPiezas;
                    cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 500).Value = string.IsNullOrWhiteSpace(vm.Observaciones) ? $"Piezas agregadas por la OF actual para completar {etiquetaBlanca}." : vm.Observaciones.Trim();
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    await cmd.ExecuteNonQueryAsync();
                }
                if (nuevoPendiente > 0)
                {
                    const string sqlParcial = @"
UPDATE dbo.Produccion_Cajas
SET CantidadPiezas=@Cantidad,Cantidad=@Cantidad,CantidadPendienteCompletar=@Pendiente,EstadoProductoIncompleto=N'EN_COMPLETADO',
UsuarioModificacionID=@UsuarioID,FechaModificacion=SYSDATETIME()
WHERE CajaProduccionID=@CajaProduccionID AND Activo=1 AND EsProductoIncompleto=1 AND EjecucionReservaID=@EjecucionProduccionID;
IF @@ROWCOUNT<>1 THROW 51121,'La etiqueta blanca cambió de estado mientras se agregaban las piezas.',1;";
                    await using var cmd = new SqlCommand(sqlParcial, cn, tx);
                    cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value = vm.CajaProduccionID;
                    cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = vm.EjecucionProduccionID;
                    cmd.Parameters.Add("@Cantidad", SqlDbType.Int).Value = nuevaCantidad;
                    cmd.Parameters.Add("@Pendiente", SqlDbType.Int).Value = nuevoPendiente;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    await cmd.ExecuteNonQueryAsync();
                    await tx.CommitAsync();
                    TempData["Success"] = $"Se agregaron {vm.CantidadPiezas:N0} pieza(s) a {etiquetaBlanca}. Ahora contiene {nuevaCantidad:N0}/{capacidad:N0}; faltan {nuevoPendiente:N0}.";
                    return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
                }
                var siguienteNumero = await ObtenerSiguienteNumeroCajaAsync(vm.EjecucionProduccionID, cn, tx);
                var nuevoFolio = CrearFolioCajaProduccion(ejecucion, siguienteNumero);
                const string sqlCompleta = @"
UPDATE dbo.Produccion_Cajas
SET EjecucionProduccionID=@EjecucionProduccionID,ProgramaProduccionID=@ProgramaProduccionID,
SolicitudProduccionID=@SolicitudProduccionID,SolicitudProduccionDetalleID=@SolicitudProduccionDetalleID,
ReleaseID=@ReleaseID,ReleaseDetalleID=@ReleaseDetalleID,NumeroCaja=@NumeroCaja,FolioCaja=@FolioCaja,
CantidadPiezas=@Cantidad,Cantidad=@Cantidad,TipoCaja=N'OK',EsProductoIncompleto=0,
EstadoProductoIncompleto=N'COMPLETA',CantidadPendienteCompletar=0,FechaCompletadoIncompleto=SYSDATETIME(),
EstadoCajaID=@EstadoCajaID,EstadoCajaNombre=@EstadoCajaNombre,EstatusCalidad=N'FORMADA',
EtiquetaFolio=@FolioCaja,Etiqueta=@FolioCaja,EtiquetaVerde=0,
UsuarioModificacionID=@UsuarioID,FechaModificacion=SYSDATETIME()
WHERE CajaProduccionID=@CajaProduccionID AND Activo=1 AND EsProductoIncompleto=1 AND EjecucionReservaID=@EjecucionProduccionID;
IF @@ROWCOUNT<>1 THROW 51120,'La etiqueta blanca cambió de estado mientras se completaba.',1;

UPDATE dbo.Planeacion_ProductoIncompletoApartado
SET EstatusID=5,UsuarioAplicacionID=@UsuarioID,FechaAplicacion=SYSDATETIME(),Activo=0,
Observaciones=LEFT(COALESCE(NULLIF(Observaciones,N'')+N' | ',N'')+N'Producto incompleto aplicado y caja completada por ejecución '+CONVERT(NVARCHAR(20),@EjecucionProduccionID)+N'.',500)
WHERE CajaProduccionID=@CajaProduccionID
  AND EjecucionProduccionID=@EjecucionProduccionID
  AND ProgramaProduccionID=@ProgramaProduccionID
  AND Activo=1
  AND EstatusID=4;
IF @@ROWCOUNT<>1 THROW 51122,'No fue posible cerrar el apartado de producto incompleto como aplicado.',1;";
                await using (var cmd = new SqlCommand(sqlCompleta, cn, tx))
                {
                    cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value = vm.CajaProduccionID;
                    cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucion.EjecucionProduccionID;
                    cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = ejecucion.ProgramaProduccionID;
                    cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = (object?)ejecucion.SolicitudProduccionID ?? DBNull.Value;
                    cmd.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value = (object?)ejecucion.SolicitudProduccionDetalleID ?? DBNull.Value;
                    cmd.Parameters.Add("@ReleaseID", SqlDbType.Int).Value = (object?)ejecucion.ReleaseID ?? DBNull.Value;
                    cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = (object?)ejecucion.ReleaseDetalleID ?? DBNull.Value;
                    cmd.Parameters.Add("@NumeroCaja", SqlDbType.Int).Value = siguienteNumero;
                    cmd.Parameters.Add("@FolioCaja", SqlDbType.NVarChar, 100).Value = nuevoFolio;
                    cmd.Parameters.Add("@Cantidad", SqlDbType.Int).Value = capacidad;
                    cmd.Parameters.Add("@EstadoCajaID", SqlDbType.Int).Value = ProduccionCajaEstatus.FormadaProduccion;
                    cmd.Parameters.Add("@EstadoCajaNombre", SqlDbType.NVarChar, 100).Value = ProduccionCajaEstatus.Nombre(ProduccionCajaEstatus.FormadaProduccion);
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    await cmd.ExecuteNonQueryAsync();
                }
                await tx.CommitAsync();
                TempData["Success"] = $"Etiqueta blanca {etiquetaBlanca} completada con {capacidad:N0} pieza(s). Ahora es la caja {nuevoFolio} y puede continuar al flujo de Calidad.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] = "No fue posible completar la etiqueta blanca: " + ex.Message;
            }
            return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
        }

        private static string NormalizarTipoCajaProduccion(string? tipoCaja)
        {
            var valor = string.IsNullOrWhiteSpace(tipoCaja) ? string.Empty : tipoCaja.Trim().ToUpperInvariant();
            if (valor == "OK") return ProduccionCajaTipo.Ok;
            if (valor == "SOSPECHOSA" || valor == "SOSPECHOSO") return ProduccionCajaTipo.Sospechoso;
            if (valor == "SCRAP") return ProduccionCajaTipo.Scrap;
            if (valor == "RETENCION" || valor == "RETENCIÓN") return ProduccionCajaTipo.Retencion;
            if (valor == "INCOMPLETA" || valor == "INCOMPLETO") return ProduccionCajaTipo.Incompleta;
            return string.Empty;
        }

        private static string CrearFolioCajaProduccion(ProduccionEjecucionVm ejecucion, int numeroCaja)
        {
            if (ejecucion == null) throw new ArgumentNullException(nameof(ejecucion));
            if (ejecucion.EjecucionProduccionID <= 0) throw new InvalidOperationException("La ejecución de Producción no es válida.");
            if (numeroCaja <= 0) throw new ArgumentOutOfRangeException(nameof(numeroCaja));
            return $"PROD-{ejecucion.EjecucionProduccionID}-C{numeroCaja:000}";
        }


        private static async Task<ContextoEscaneoCaja?> ObtenerContextoEscaneoCajaAsync(int ejecucionProduccionId, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
SELECT TOP(1)
    e.EjecucionProduccionID,
    e.ProgramaProduccionID,
    e.SolicitudProduccionID,
    e.SolicitudProduccionDetalleID,
    e.ReleaseID,
    e.ReleaseDetalleID,
    e.ParteID,
    COALESCE(
        NULLIF(LTRIM(RTRIM(s.NumeroOFRecibida)),N''),
        NULLIF(LTRIM(RTRIM(s.FolioSolicitud)),N''),
        CASE
            WHEN e.SolicitudProduccionID IS NOT NULL
                THEN CONCAT(N'OF-ID-',e.SolicitudProduccionID)
            ELSE NULL
        END
    ) AS NumeroOF,
    e.NumeroParte,
    e.ReferenciaSAP,
    ISNULL(e.CantidadPlaneada,0) AS CantidadPlaneada,
    d.PiezasPorEmbalaje,
    d.CantidadEmbalajes,
    e.EstatusID
FROM dbo.Produccion_Ejecucion e WITH(UPDLOCK,HOLDLOCK)
LEFT JOIN dbo.SolicitudesProduccion s
    ON s.SolicitudProduccionID=e.SolicitudProduccionID
   AND s.Activo=1
LEFT JOIN dbo.SolicitudesProduccionDetalle d
    ON d.SolicitudProduccionDetalleID=e.SolicitudProduccionDetalleID
   AND d.Activo=1
WHERE e.EjecucionProduccionID=@EjecucionProduccionID
  AND e.Activo=1;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add(
                "@EjecucionProduccionID",
                SqlDbType.Int).Value =
                ejecucionProduccionId;

            await using var rd =
                await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
                return null;

            return new ContextoEscaneoCaja
            {
                EjecucionProduccionID =
                    Convert.ToInt32(
                        rd["EjecucionProduccionID"]),

                ProgramaProduccionID =
                    Convert.ToInt32(
                        rd["ProgramaProduccionID"]),

                SolicitudProduccionID =
                    rd["SolicitudProduccionID"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(
                            rd["SolicitudProduccionID"]),

                SolicitudProduccionDetalleID =
                    rd["SolicitudProduccionDetalleID"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(
                            rd["SolicitudProduccionDetalleID"]),

                ReleaseID =
                    rd["ReleaseID"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(
                            rd["ReleaseID"]),

                ReleaseDetalleID =
                    rd["ReleaseDetalleID"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(
                            rd["ReleaseDetalleID"]),

                ParteID =
                    rd["ParteID"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(
                            rd["ParteID"]),

                NumeroOF =
                    rd["NumeroOF"] == DBNull.Value
                        ? null
                        : rd["NumeroOF"]
                            ?.ToString()
                            ?.Trim(),

                NumeroParte =
                    rd["NumeroParte"] == DBNull.Value
                        ? null
                        : rd["NumeroParte"]
                            ?.ToString()
                            ?.Trim(),

                ReferenciaSAP =
                    rd["ReferenciaSAP"] == DBNull.Value
                        ? null
                        : rd["ReferenciaSAP"]
                            ?.ToString()
                            ?.Trim(),

                CantidadPlaneada =
                    Convert.ToInt32(
                        rd["CantidadPlaneada"]),

                PiezasPorEmbalaje =
                    rd["PiezasPorEmbalaje"] == DBNull.Value
                        ? null
                        : Convert.ToDecimal(
                            rd["PiezasPorEmbalaje"]),

                CantidadEmbalajes =
                    rd["CantidadEmbalajes"] == DBNull.Value
                        ? null
                        : Convert.ToDecimal(
                            rd["CantidadEmbalajes"]),

                EstatusID =
                    Convert.ToInt32(
                        rd["EstatusID"])
            };
        }

        private sealed class ValidacionEnvioCajaCalidad
        {
            public bool Permitido { get; set; }
            public int? InspeccionID { get; set; }
            public string Mensaje { get; set; } = string.Empty;
        }
        private static async Task<ValidacionEnvioCajaCalidad> ValidarEnvioCajaCalidadAsync(int ejecucionProduccionId, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
DECLARE @InspeccionID INT;
DECLARE @Estado NVARCHAR(50);
DECLARE @ConfiguracionInvalidada BIT;
DECLARE @RequiereReliberacion BIT;
DECLARE @Liberado BIT;
DECLARE @DisposicionesPendientes INT;
SELECT TOP (1)
    @InspeccionID=ci.InspeccionID,
    @Estado=UPPER(LTRIM(RTRIM(ISNULL(ci.Estado,N'')))),
    @ConfiguracionInvalidada=ISNULL(ci.ConfiguracionInvalidada,0),
    @RequiereReliberacion=ISNULL(ci.RequiereReliberacion,0),
    @Liberado=ISNULL(ci.Liberado,0)
FROM dbo.Calidad_Inspecciones ci WITH (UPDLOCK,HOLDLOCK)
WHERE ci.EjecucionProduccionID=@EjecucionProduccionID
  AND ci.Estado<>N'CERRADA'
ORDER BY ci.InspeccionID DESC;
IF @InspeccionID IS NULL
BEGIN
    SELECT CAST(0 AS BIT) Permitido,CAST(NULL AS INT) InspeccionID,N'No existe una inspección activa de Calidad relacionada con la ejecución.' Mensaje;
    RETURN;
END;
IF @ConfiguracionInvalidada=1
BEGIN
    SELECT CAST(0 AS BIT) Permitido,@InspeccionID InspeccionID,N'La configuración de la corrida fue invalidada. Debe completarse la revisión de Calidad antes de enviar cajas.' Mensaje;
    RETURN;
END;
IF @RequiereReliberacion=1 OR @Estado=N'PENDIENTE_RELIBERACION'
BEGIN
    SELECT CAST(0 AS BIT) Permitido,@InspeccionID InspeccionID,N'La corrida requiere reliberación de Calidad después de un paro. No se pueden enviar cajas mientras esté pendiente.' Mensaje;
    RETURN;
END;
IF @Liberado=0
BEGIN
    SELECT CAST(0 AS BIT) Permitido,@InspeccionID InspeccionID,N'Calidad no tiene liberada actualmente la producción.' Mensaje;
    RETURN;
END;
IF @Estado<>N'MONITOREO_ACTIVO'
BEGIN
    SELECT CAST(0 AS BIT) Permitido,@InspeccionID InspeccionID,N'La inspección debe encontrarse en monitoreo activo para recibir cajas de Producción.' Mensaje;
    RETURN;
END;
SELECT @DisposicionesPendientes=COUNT(1)
FROM dbo.Calidad_DisposicionesMaterial d WITH (UPDLOCK,HOLDLOCK)
WHERE d.InspeccionID=@InspeccionID
  AND d.Activo=1
  AND UPPER(LTRIM(RTRIM(ISNULL(d.ResultadoFinal,N''))))=N'PENDIENTE';
IF ISNULL(@DisposicionesPendientes,0)>0
BEGIN
    SELECT CAST(0 AS BIT) Permitido,@InspeccionID InspeccionID,CONCAT(N'Existen ',@DisposicionesPendientes,N' disposición(es) de material pendientes. Calidad debe resolverlas antes de recibir o liberar nuevas cajas.') Mensaje;
    RETURN;
END;
IF EXISTS
(
    SELECT 1
    FROM dbo.Calidad_MonitoreosProceso m
    WHERE m.InspeccionID=@InspeccionID
      AND m.Activo=1
      AND m.RegistroHoraID IS NOT NULL
      AND UPPER(LTRIM(RTRIM(ISNULL(m.Resultado,N''))))=N'PENDIENTE'
      AND (ISNULL(m.CantidadSospechosa,0)>0 OR ISNULL(m.CantidadNoRecuperable,0)>0)
)
BEGIN
    SELECT CAST(0 AS BIT) Permitido,@InspeccionID InspeccionID,N'Existen capturas con material sospechoso o scrap reportado que todavía no han sido evaluadas por Calidad.' Mensaje;
    RETURN;
END;
SELECT CAST(1 AS BIT) Permitido,@InspeccionID InspeccionID,N'La caja puede enviarse a Calidad.' Mensaje;";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync()) return new ValidacionEnvioCajaCalidad { Permitido = false, Mensaje = "No fue posible validar el estado de Calidad." };
            return new ValidacionEnvioCajaCalidad
            {
                Permitido = rd["Permitido"] != DBNull.Value && Convert.ToBoolean(rd["Permitido"]),
                InspeccionID = rd["InspeccionID"] == DBNull.Value ? null : Convert.ToInt32(rd["InspeccionID"]),
                Mensaje = rd["Mensaje"]?.ToString() ?? "La caja no puede enviarse a Calidad."
            };
        }


        private static async Task<int> ObtenerCantidadCajasNormalesAsync(int ejecucionProduccionId, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
SELECT COUNT(1)
FROM dbo.Produccion_Cajas c WITH(UPDLOCK,HOLDLOCK)
WHERE c.EjecucionProduccionID=@EjecucionProduccionID
  AND c.Activo=1
  AND UPPER(LTRIM(RTRIM(ISNULL(c.TipoCaja,N'OK'))))=N'OK'
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.Produccion_CajaOrigenDetalle od
      WHERE od.CajaProduccionID=c.CajaProduccionID
        AND od.Activo=1
  );";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }


        private static async Task<bool> ExisteCodigoBarrasCajaAsync(string codigoBarras, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
SELECT CAST(CASE WHEN EXISTS
(
    SELECT 1
    FROM dbo.Produccion_Cajas WITH(UPDLOCK,HOLDLOCK)
    WHERE Activo=1
      AND CodigoBarrasOrigen=@CodigoBarrasOrigen
) THEN 1 ELSE 0 END AS BIT);";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@CodigoBarrasOrigen", SqlDbType.NVarChar, 500).Value = codigoBarras.Trim();
            return Convert.ToBoolean(await cmd.ExecuteScalarAsync());
        }

        private static string NormalizarValorEscaneo(string? valor)
        {
            if (string.IsNullOrWhiteSpace(valor)) return string.Empty;
            return new string(valor.Trim().ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        }

        private static long EnteroLargoCaja(SqlDataReader rd, string columna)
        {
            var ordinal = rd.GetOrdinal(columna);
            return rd.IsDBNull(ordinal) ? 0L : Convert.ToInt64(rd.GetValue(ordinal));
        }

        private sealed class ContextoEscaneoCaja
        {
            public int EjecucionProduccionID { get; set; }
            public int ProgramaProduccionID { get; set; }
            public int? SolicitudProduccionID { get; set; }
            public int? SolicitudProduccionDetalleID { get; set; }
            public int? ReleaseID { get; set; }
            public int? ReleaseDetalleID { get; set; }
            public int? ParteID { get; set; }
            public string? NumeroOF { get; set; }
            public string? NumeroParte { get; set; }
            public string? ReferenciaSAP { get; set; }
            public int CantidadPlaneada { get; set; }
            public decimal? PiezasPorEmbalaje { get; set; }
            public decimal? CantidadEmbalajes { get; set; }
            public int EstatusID { get; set; }
        }
    }
}
