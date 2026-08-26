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
            if (ejecucionProduccionId <= 0)
            {
                TempData["Error"] = "No se recibió la ejecución de producción.";
                return RedirectToAction(nameof(Index));
            }
            if (cantidadPiezas <= 0)
            {
                TempData["Error"] = "La cantidad de piezas debe ser mayor a cero.";
                return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
            }
            var tipoNormalizado = NormalizarTipoCajaProduccion(tipoCaja);
            if (tipoNormalizado != ProduccionCajaTipo.Incompleta)
            {
                TempData["Error"] = "Producción ya no registra la formación de cajas normales en ERP. Las cajas normales se registran automáticamente cuando Calidad realiza el primer escaneo físico. Este proceso solo se conserva para producto incompleto con etiqueta blanca.";
                return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
            }
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            var usuarioId = ObtenerUsuarioID();
            if (!await UsuarioPuedeGestionarCajasAsync(usuarioId, cn)) return AccesoDenegadoCajas();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var ejecucion = await ObtenerEjecucionAsync(ejecucionProduccionId, cn, tx);
                if (ejecucion == null)
                {
                    await tx.RollbackAsync();
                    return NotFound();
                }
                if (ejecucion.EstatusID != ProduccionEstatus.EnProduccion)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "Solo puedes registrar producto incompleto cuando la producción está en serie.";
                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }
                if (await TieneParoAbiertoAsync(ejecucionProduccionId, cn, tx))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "No puedes registrar producto incompleto mientras exista un paro abierto.";
                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }
                decimal? piezasPorEmbalaje = null;
                if (ejecucion.SolicitudProduccionDetalleID.HasValue && ejecucion.SolicitudProduccionDetalleID.Value > 0)
                {
                    const string sqlEmbalaje = @"SELECT TOP(1) PiezasPorEmbalaje FROM dbo.SolicitudesProduccionDetalle WITH(UPDLOCK,HOLDLOCK) WHERE SolicitudProduccionDetalleID=@SolicitudProduccionDetalleID AND Activo=1;";
                    await using var cmdEmbalaje = new SqlCommand(sqlEmbalaje, cn, tx);
                    cmdEmbalaje.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value = ejecucion.SolicitudProduccionDetalleID.Value;
                    var valor = await cmdEmbalaje.ExecuteScalarAsync();
                    piezasPorEmbalaje = valor == null || valor == DBNull.Value ? null : Convert.ToDecimal(valor);
                }
                if (!piezasPorEmbalaje.HasValue || piezasPorEmbalaje.Value <= 0)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La pieza no tiene configurada la capacidad de piezas por embalaje.";
                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }
                var capacidadCaja = Convert.ToInt32(Math.Floor(piezasPorEmbalaje.Value));
                if (capacidadCaja <= 0)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La capacidad del embalaje no es válida.";
                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }
                if (cantidadPiezas >= capacidadCaja)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = $"El producto incompleto debe contener menos de {capacidadCaja:N0} pieza(s). Una caja completa continúa por el flujo normal y será registrada por Calidad al escanearla.";
                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }
                if (!ejecucion.CantidadPlaneada.HasValue || ejecucion.CantidadPlaneada.Value <= 0)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La ejecución no tiene una cantidad planeada válida.";
                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }
                var consumo = await ObtenerConsumoCajasEjecucionAsync(ejecucionProduccionId, cn, tx);
                if (consumo.Ok < ejecucion.CantidadPlaneada.Value)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = $"Todavía faltan piezas planeadas por aplicar a cajas reales. Planeado: {ejecucion.CantidadPlaneada.Value:N0}; aplicado: {consumo.Ok:N0}. La etiqueta blanca solamente se usa para sobreproducción.";
                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }
                var excedenteProducido = Math.Max(0, ejecucion.CantidadOKTotal - ejecucion.CantidadPlaneada.Value);
                if (excedenteProducido <= 0)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "No existe sobreproducción OK disponible para registrar como producto incompleto.";
                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }
                var disponible = await ObtenerCantidadDisponibleParaCajaAsync(ejecucionProduccionId, ProduccionCajaTipo.Ok, cn, tx);
                if (cantidadPiezas > disponible)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = $"La cantidad supera la sobreproducción OK disponible. Disponible: {disponible:N0} pieza(s).";
                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }
                var siguienteNumero = await ObtenerSiguienteNumeroCajaAsync(ejecucionProduccionId, cn, tx);
                var folioCaja = CrearFolioCajaProduccion(ejecucion, siguienteNumero);
                var ahora = DateTime.Now;
                const string sqlInsert = @"
INSERT INTO dbo.Produccion_Cajas
(
    EjecucionProduccionID,ProgramaProduccionID,SolicitudProduccionID,SolicitudProduccionDetalleID,ReleaseID,ReleaseDetalleID,
    NumeroCaja,FolioCaja,CantidadPiezas,TipoCaja,LoteMaterial,EtiquetaFolio,EstadoCajaID,EstadoCajaNombre,EtiquetaVerde,
    FechaFormacion,UsuarioFormacionID,FechaSolicitudCalidad,UsuarioSolicitudCalidadID,Observaciones,Activo,UsuarioCreacionID,
    FechaCreacion,Etiqueta,Cantidad,EstatusCalidad,OperadorUsuarioID,EsProductoIncompleto,EstadoProductoIncompleto,
    CapacidadObjetivoCaja,CantidadPendienteCompletar,EtiquetaBlanca
)
OUTPUT INSERTED.CajaProduccionID
VALUES
(
    @EjecucionProduccionID,@ProgramaProduccionID,@SolicitudProduccionID,@SolicitudProduccionDetalleID,@ReleaseID,@ReleaseDetalleID,
    @NumeroCaja,@FolioCaja,@CantidadPiezas,N'INCOMPLETA',NULL,NULL,@EstadoCajaID,@EstadoCajaNombre,0,
    @Ahora,@UsuarioID,NULL,NULL,@Observaciones,1,@UsuarioID,@Ahora,
    @FolioCaja,@CantidadPiezas,N'FORMADA',@UsuarioID,1,@EstadoProductoIncompleto,
    @CapacidadObjetivoCaja,@CantidadPendienteCompletar,NULL
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
                    cmd.Parameters.Add("@EstadoCajaID", SqlDbType.Int).Value = ProduccionCajaEstatus.FormadaProduccion;
                    cmd.Parameters.Add("@EstadoCajaNombre", SqlDbType.NVarChar, 100).Value = "Producto incompleto - etiqueta blanca";
                    cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahora;
                    cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 500).Value = string.IsNullOrWhiteSpace(observaciones) ? DBNull.Value : observaciones.Trim();
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    cmd.Parameters.Add("@EstadoProductoIncompleto", SqlDbType.NVarChar, 30).Value = ProduccionProductoIncompletoEstado.Disponible;
                    cmd.Parameters.Add("@CapacidadObjetivoCaja", SqlDbType.Int).Value = capacidadCaja;
                    cmd.Parameters.Add("@CantidadPendienteCompletar", SqlDbType.Int).Value = capacidadCaja - cantidadPiezas;
                    cajaProduccionId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
                }
                var etiquetaBlanca = $"BLA-{cajaProduccionId:000000}";
                const string sqlBlanca = @"
UPDATE dbo.Produccion_Cajas
SET EtiquetaBlanca=@EtiquetaBlanca,
    EtiquetaFolio=@EtiquetaBlanca,
    Etiqueta=@EtiquetaBlanca,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=SYSDATETIME()
WHERE CajaProduccionID=@CajaProduccionID
  AND Activo=1;

INSERT INTO dbo.Produccion_CajaOrigenDetalle
(
    CajaProduccionID,EjecucionProduccionID,ProgramaProduccionID,SolicitudProduccionID,SolicitudProduccionDetalleID,
    ReleaseID,ReleaseDetalleID,CantidadPiezas,TipoMovimiento,Observaciones,UsuarioCreacionID,FechaCreacion,Activo
)
VALUES
(
    @CajaProduccionID,@EjecucionProduccionID,@ProgramaProduccionID,@SolicitudProduccionID,@SolicitudProduccionDetalleID,
    @ReleaseID,@ReleaseDetalleID,@CantidadPiezas,N'ORIGEN',N'Sobreproducción resguardada como producto incompleto con etiqueta blanca.',
    @UsuarioID,SYSDATETIME(),1
);";
                await using (var cmd = new SqlCommand(sqlBlanca, cn, tx))
                {
                    cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value = cajaProduccionId;
                    cmd.Parameters.Add("@EtiquetaBlanca", SqlDbType.NVarChar, 100).Value = etiquetaBlanca;
                    cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucion.EjecucionProduccionID;
                    cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = ejecucion.ProgramaProduccionID;
                    cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = (object?)ejecucion.SolicitudProduccionID ?? DBNull.Value;
                    cmd.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value = (object?)ejecucion.SolicitudProduccionDetalleID ?? DBNull.Value;
                    cmd.Parameters.Add("@ReleaseID", SqlDbType.Int).Value = (object?)ejecucion.ReleaseID ?? DBNull.Value;
                    cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = (object?)ejecucion.ReleaseDetalleID ?? DBNull.Value;
                    cmd.Parameters.Add("@CantidadPiezas", SqlDbType.Int).Value = cantidadPiezas;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    await cmd.ExecuteNonQueryAsync();
                }
                await tx.CommitAsync();
                TempData["Success"] = $"Producto incompleto {etiquetaBlanca} registrado con {cantidadPiezas:N0} pieza(s). Faltan {capacidadCaja - cantidadPiezas:N0} para completar el embalaje.";
            }
            catch (Exception ex)
            {
                try { await tx.RollbackAsync(); } catch { }
                TempData["Error"] = "No fue posible registrar el producto incompleto: " + ex.Message;
            }
            return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
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
                TempData["Error"] = "Captura la corrección realizada antes de devolver la caja al flujo de Calidad.";
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
SELECT TOP(1)
    c.CajaProduccionID,
    c.EjecucionProduccionID,
    COALESCE(NULLIF(c.FolioCaja,N''),NULLIF(c.EtiquetaFolio,N''),NULLIF(c.Etiqueta,N''),CONVERT(NVARCHAR(100),c.CajaProduccionID)) AS FolioCaja,
    ISNULL(c.EstadoCajaID,1) AS EstadoCajaID,
    UPPER(LTRIM(RTRIM(ISNULL(c.EstatusCalidad,N'')))) AS EstatusCalidad,
    ISNULL(c.MotivoCalidad,N'') AS MotivoCalidad,
    ci.InspeccionID,
    UPPER(LTRIM(RTRIM(ISNULL(ci.Estado,N'')))) AS EstadoInspeccion,
    ISNULL(ci.ConfiguracionInvalidada,0) AS ConfiguracionInvalidada
FROM dbo.Produccion_Cajas c WITH(UPDLOCK,HOLDLOCK)
OUTER APPLY
(
    SELECT TOP(1) i.InspeccionID,i.Estado,i.ConfiguracionInvalidada
    FROM dbo.Calidad_Inspecciones i WITH(UPDLOCK,HOLDLOCK)
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
                    TempData["Error"] = "La ejecución tiene una reliberación pendiente. La caja no puede regresar a revisión hasta que Calidad autorice el reinicio.";
                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }

                var comentarioCorreccion = $"Producción corrigió la caja {folioCaja}. Motivo de devolución: {(string.IsNullOrWhiteSpace(motivoDevolucion) ? "No especificado" : motivoDevolucion)}. Corrección realizada: {correccionRealizada}";
                if (comentarioCorreccion.Length > 1000) comentarioCorreccion = comentarioCorreccion[..1000];

                const string sqlActualizar = @"
UPDATE dbo.Produccion_Cajas
SET EstatusCalidad=N'PENDIENTE',
    EstadoCajaID=@EstadoNuevo,
    EstadoCajaNombre=N'Pendiente escaneo Calidad',
    EtiquetaVerde=0,
    FechaSolicitudCalidad=GETDATE(),
    UsuarioSolicitudCalidadID=@UsuarioID,
    FechaLiberacionCalidad=NULL,
    AuditorCalidadUsuarioID=NULL,
    UsuarioCalidadID=NULL,
    ResultadoCalidad=NULL,
    MotivoCalidad=NULL,
    FechaEscaneoCalidad=NULL,
    UsuarioEscaneoCalidadID=NULL,
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
  AND UPPER(LTRIM(RTRIM(ISNULL(EstatusCalidad,N''))))=N'DEVUELTA';

IF @@ROWCOUNT<>1
    THROW 51070,'La caja cambió de estado mientras se registraba la corrección.',1;

INSERT INTO dbo.Calidad_InspeccionHistorial
(
    InspeccionID,Movimiento,EstadoAnterior,EstadoNuevo,ResultadoCalidad,Etiqueta,Comentario,UsuarioID,FechaMovimiento
)
VALUES
(
    @InspeccionID,N'CAJA_CORREGIDA_PRODUCCION',@EstadoInspeccion,@EstadoInspeccion,N'PENDIENTE',NULL,@Comentario,@UsuarioID,GETDATE()
);";

                await using (var cmd = new SqlCommand(sqlActualizar, cn, tx))
                {
                    cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value = cajaProduccionId;
                    cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                    cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value = inspeccionId;
                    cmd.Parameters.Add("@EstadoNuevo", SqlDbType.Int).Value = ProduccionCajaEstatus.PendienteCalidad;
                    cmd.Parameters.Add("@EstadoActual", SqlDbType.Int).Value = ProduccionCajaEstatus.FormadaProduccion;
                    cmd.Parameters.Add("@EstadoInspeccion", SqlDbType.NVarChar, 50).Value = estadoInspeccion;
                    cmd.Parameters.Add("@Comentario", SqlDbType.NVarChar, 1000).Value = comentarioCorreccion;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    await cmd.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();
                TempData["Success"] = $"Corrección de la caja {folioCaja} registrada. La caja quedó nuevamente pendiente de escaneo por Calidad.";
            }
            catch (Exception ex)
            {
                try { await tx.RollbackAsync(); } catch { }
                TempData["Error"] = "No fue posible registrar la corrección de la caja: " + ex.Message;
            }

            return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
        }

     

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EscanearSalidaCaja(int? cajaProduccionId, int? ejecucionProduccionId, string? etiquetaEscaneada)
        {
            if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");

            etiquetaEscaneada = etiquetaEscaneada?.Trim();

            if (string.IsNullOrWhiteSpace(etiquetaEscaneada))
            {
                TempData["Error"] = "Escanea la etiqueta física de la caja que vas a entregar a Almacén PT.";
                return ejecucionProduccionId.HasValue && ejecucionProduccionId.Value > 0
                    ? RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId.Value })
                    : RedirectToAction(nameof(Index));
            }

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var usuarioId = ObtenerUsuarioID();
            if (!await UsuarioPuedeGestionarCajasAsync(usuarioId, cn)) return AccesoDenegadoCajas();

            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
            var ejecucionRealId = ejecucionProduccionId ?? 0;

            try
            {
                const string sqlCaja = @"
SELECT TOP(1)
    CajaProduccionID,
    EjecucionProduccionID,
    COALESCE(NULLIF(FolioCaja,N''),CONVERT(NVARCHAR(100),CajaProduccionID)) AS FolioCaja,
    ISNULL(EstadoCajaID,1) AS EstadoCajaID,
    ISNULL(EstadoCajaNombre,N'') AS EstadoCajaNombre,
    ISNULL(EtiquetaVerde,0) AS EtiquetaVerde,
    CodigoBarrasOrigen,
    FechaEscaneoCalidad,
    FechaLiberacionCalidad,
    ResultadoCalidad,
    FechaSalidaProduccion,
    FechaRecepcionAlmacen
FROM dbo.Produccion_Cajas WITH(UPDLOCK,HOLDLOCK)
WHERE Activo=1
  AND
  (
      (@CajaProduccionID IS NOT NULL AND CajaProduccionID=@CajaProduccionID)
      OR
      (@CajaProduccionID IS NULL AND CodigoBarrasOrigen=@CodigoBarras)
  )
  AND (@EjecucionProduccionID IS NULL OR EjecucionProduccionID=@EjecucionProduccionID)
ORDER BY CajaProduccionID DESC;";

                long cajaId;
                int estadoCajaId;
                string folioCaja;
                bool etiquetaVerde;
                string? codigoRegistrado;
                DateTime? fechaEscaneoCalidad;
                DateTime? fechaLiberacionCalidad;
                DateTime? fechaSalidaProduccion;
                DateTime? fechaRecepcionAlmacen;
                string? resultadoCalidad;

                await using (var cmd = new SqlCommand(sqlCaja, cn, tx))
                {
                    cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value = cajaProduccionId.HasValue && cajaProduccionId.Value > 0 ? cajaProduccionId.Value : DBNull.Value;
                    cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId.HasValue && ejecucionProduccionId.Value > 0 ? ejecucionProduccionId.Value : DBNull.Value;
                    cmd.Parameters.Add("@CodigoBarras", SqlDbType.NVarChar, 500).Value = etiquetaEscaneada;

                    await using var rd = await cmd.ExecuteReaderAsync();

                    if (!await rd.ReadAsync())
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = "La etiqueta escaneada no corresponde a una caja de esta producción.";
                        return ejecucionProduccionId.HasValue && ejecucionProduccionId.Value > 0
                            ? RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId.Value })
                            : RedirectToAction(nameof(Index));
                    }

                    cajaId = Convert.ToInt64(rd["CajaProduccionID"]);
                    ejecucionRealId = Convert.ToInt32(rd["EjecucionProduccionID"]);
                    estadoCajaId = Convert.ToInt32(rd["EstadoCajaID"]);
                    folioCaja = rd["FolioCaja"]?.ToString()?.Trim() ?? cajaId.ToString();
                    etiquetaVerde = Convert.ToBoolean(rd["EtiquetaVerde"]);
                    codigoRegistrado = rd["CodigoBarrasOrigen"] == DBNull.Value ? null : rd["CodigoBarrasOrigen"]?.ToString()?.Trim();
                    fechaEscaneoCalidad = rd["FechaEscaneoCalidad"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaEscaneoCalidad"]);
                    fechaLiberacionCalidad = rd["FechaLiberacionCalidad"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaLiberacionCalidad"]);
                    resultadoCalidad = rd["ResultadoCalidad"] == DBNull.Value ? null : rd["ResultadoCalidad"]?.ToString()?.Trim();
                    fechaSalidaProduccion = rd["FechaSalidaProduccion"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaSalidaProduccion"]);
                    fechaRecepcionAlmacen = rd["FechaRecepcionAlmacen"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaRecepcionAlmacen"]);
                }

                if (fechaRecepcionAlmacen.HasValue)
                {
                    await tx.RollbackAsync();
                    TempData["Info"] = $"La caja {folioCaja} ya fue recibida por Almacén PT.";
                    return RedirectToAction(nameof(Cajas), new { id = ejecucionRealId });
                }

                if (fechaSalidaProduccion.HasValue || estadoCajaId == ProduccionCajaEstatus.SalidaProduccion)
                {
                    await tx.RollbackAsync();
                    TempData["Info"] = $"La caja {folioCaja} ya fue escaneada como entrega de Producción.";
                    return RedirectToAction(nameof(Cajas), new { id = ejecucionRealId });
                }

                if (string.IsNullOrWhiteSpace(codigoRegistrado))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = $"La caja {folioCaja} todavía no tiene asociada la etiqueta física de Planeación. Calidad debe escanearla primero.";
                    return RedirectToAction(nameof(Cajas), new { id = ejecucionRealId });
                }

                if (!string.Equals(codigoRegistrado, etiquetaEscaneada, StringComparison.OrdinalIgnoreCase))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = $"La etiqueta escaneada no corresponde a la caja {folioCaja}.";
                    return RedirectToAction(nameof(Cajas), new { id = ejecucionRealId });
                }

                if (!fechaEscaneoCalidad.HasValue)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = $"La caja {folioCaja} todavía está pendiente de escaneo por Calidad.";
                    return RedirectToAction(nameof(Cajas), new { id = ejecucionRealId });
                }

                if (!fechaLiberacionCalidad.HasValue || !etiquetaVerde || !string.Equals(resultadoCalidad, "LIBERADA", StringComparison.OrdinalIgnoreCase))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = $"La caja {folioCaja} fue escaneada por Calidad, pero todavía no está validada con etiqueta verde.";
                    return RedirectToAction(nameof(Cajas), new { id = ejecucionRealId });
                }

                if (estadoCajaId != ProduccionCajaEstatus.LiberadaCalidad && estadoCajaId != ProduccionCajaEstatus.ZonaVerde)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = $"La caja {folioCaja} no se encuentra pendiente de entrega a Almacén.";
                    return RedirectToAction(nameof(Cajas), new { id = ejecucionRealId });
                }

                var ahora = DateTime.Now;

                const string sqlActualizar = @"
UPDATE dbo.Produccion_Cajas
SET EstadoCajaID=@EstadoCajaID,
    EstadoCajaNombre=N'Pendiente recepción Almacén PT',
    FechaEscaneoProduccion=COALESCE(FechaEscaneoProduccion,@Ahora),
    UsuarioEscaneoProduccionID=COALESCE(UsuarioEscaneoProduccionID,@UsuarioID),
    FechaSalidaProduccion=@Ahora,
    UsuarioSalidaProduccionID=@UsuarioID,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=@Ahora
WHERE CajaProduccionID=@CajaProduccionID
  AND Activo=1
  AND FechaSalidaProduccion IS NULL
  AND EtiquetaVerde=1
  AND FechaEscaneoCalidad IS NOT NULL
  AND EstadoCajaID IN(@LiberadaCalidad,@ZonaVerde);

IF @@ROWCOUNT<>1
    THROW 51500,'La caja cambió de estado mientras Producción registraba la entrega.',1;";

                await using (var cmd = new SqlCommand(sqlActualizar, cn, tx))
                {
                    cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value = cajaId;
                    cmd.Parameters.Add("@EstadoCajaID", SqlDbType.Int).Value = ProduccionCajaEstatus.SalidaProduccion;
                    cmd.Parameters.Add("@LiberadaCalidad", SqlDbType.Int).Value = ProduccionCajaEstatus.LiberadaCalidad;
                    cmd.Parameters.Add("@ZonaVerde", SqlDbType.Int).Value = ProduccionCajaEstatus.ZonaVerde;
                    cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahora;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    await cmd.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();
                TempData["Success"] = $"Caja {folioCaja} escaneada y entregada por Producción. Estado: Pendiente recepción Almacén PT.";
            }
            catch (Exception ex)
            {
                try { await tx.RollbackAsync(); } catch { }
                TempData["Error"] = "No fue posible registrar la entrega de Producción: " + ex.Message;
            }

            return ejecucionRealId > 0
                ? RedirectToAction(nameof(Cajas), new { id = ejecucionRealId })
                : RedirectToAction(nameof(Index));
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
            CalcularCajasEstimadas(vm);
            return vm;
        }

        private static void CalcularCajasEstimadas(ProduccionOperadorCajasVm vm)
        {
            vm.CajasEstimadas = new List<ProduccionCajaEstimadaVm>();
            vm.PiezasAcumuladasSiguienteCaja = 0;
            vm.PiezasFaltantesSiguienteCaja = 0;
            var capacidadCaja = vm.PiezasPorCajaSugeridas;
            if (capacidadCaja <= 0) return;
            var okDisponible = Math.Max(0, vm.CantidadOKTotal - vm.CantidadOKEnCajas);
            if (okDisponible <= 0) return;
            var tieneCantidadPlaneada = vm.CantidadPlaneada > 0;
            var planeadoYaEnCajas = tieneCantidadPlaneada ? Math.Min(vm.CantidadPlaneada, vm.CantidadOKEnCajas) : 0;
            var planeadoPendiente = tieneCantidadPlaneada ? Math.Max(0, vm.CantidadPlaneada - planeadoYaEnCajas) : 0;
            var disponibleParaCajasNormales = tieneCantidadPlaneada ? Math.Min(okDisponible, planeadoPendiente) : okDisponible;
            if (disponibleParaCajasNormales <= 0) return;
            var numeroEstimado = vm.SiguienteNumeroCaja > 0 ? vm.SiguienteNumeroCaja : 1;
            var piezasDisponibles = disponibleParaCajasNormales;
            var piezasPlaneadasPendientes = tieneCantidadPlaneada ? planeadoPendiente : int.MaxValue;
            while (piezasDisponibles >= capacidadCaja && (!tieneCantidadPlaneada || piezasPlaneadasPendientes >= capacidadCaja))
            {
                var esUltimaPlaneada = tieneCantidadPlaneada && piezasPlaneadasPendientes == capacidadCaja;
                vm.CajasEstimadas.Add(new ProduccionCajaEstimadaVm
                {
                    NumeroEstimado = numeroEstimado++,
                    EjecucionProduccionID = vm.EjecucionProduccionID,
                    CantidadPiezas = capacidadCaja,
                    CapacidadCaja = capacidadCaja,
                    EsUltimaPlaneada = esUltimaPlaneada
                });
                piezasDisponibles -= capacidadCaja;
                if (tieneCantidadPlaneada) piezasPlaneadasPendientes -= capacidadCaja;
            }
            if (tieneCantidadPlaneada && piezasPlaneadasPendientes > 0 && piezasPlaneadasPendientes < capacidadCaja && piezasDisponibles >= piezasPlaneadasPendientes)
            {
                vm.CajasEstimadas.Add(new ProduccionCajaEstimadaVm
                {
                    NumeroEstimado = numeroEstimado,
                    EjecucionProduccionID = vm.EjecucionProduccionID,
                    CantidadPiezas = piezasPlaneadasPendientes,
                    CapacidadCaja = capacidadCaja,
                    EsUltimaPlaneada = true
                });
                piezasDisponibles -= piezasPlaneadasPendientes;
                piezasPlaneadasPendientes = 0;
            }
            if (piezasDisponibles <= 0) return;
            vm.PiezasAcumuladasSiguienteCaja = piezasDisponibles;
            var objetivoSiguienteCaja = capacidadCaja;
            if (tieneCantidadPlaneada && piezasPlaneadasPendientes > 0) objetivoSiguienteCaja = Math.Min(capacidadCaja, piezasPlaneadasPendientes);
            vm.PiezasFaltantesSiguienteCaja = Math.Max(0, objetivoSiguienteCaja - piezasDisponibles);
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
            if (vm.CajaProduccionID <= 0 || vm.EjecucionProduccionID <= 0 || vm.CantidadPiezas <= 0)
            {
                TempData["Error"] = "Los datos para completar la caja no son válidos.";
                return RedirectToAction(nameof(Index));
            }
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            var usuarioId = ObtenerUsuarioID();
            if (!await UsuarioPuedeGestionarCajasAsync(usuarioId, cn)) return AccesoDenegadoCajas();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var ejecucion = await ObtenerEjecucionAsync(vm.EjecucionProduccionID, cn, tx);
                if (ejecucion == null)
                {
                    await tx.RollbackAsync();
                    return NotFound();
                }
                if (ejecucion.EstatusID != ProduccionEstatus.EnProduccion)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "Solo puedes completar producto incompleto cuando la OF está en producción.";
                    return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
                }
                if (await TieneParoAbiertoAsync(vm.EjecucionProduccionID, cn, tx))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "No puedes completar producto incompleto mientras exista un paro abierto.";
                    return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
                }
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
                    if (!await rd.ReadAsync())
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = "La etiqueta blanca no fue asignada por Planeación a esta ejecución o ya fue aplicada.";
                        return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
                    }
                    cantidadActual = Convert.ToInt32(rd["CantidadPiezas"]);
                    capacidad = Convert.ToInt32(rd["CapacidadObjetivoCaja"]);
                    pendiente = Convert.ToInt32(rd["CantidadPendienteCompletar"]);
                    parteOrigen = rd["ParteID"] == DBNull.Value ? 0 : Convert.ToInt32(rd["ParteID"]);
                    reservaId = rd["EjecucionReservaID"] == DBNull.Value ? null : Convert.ToInt32(rd["EjecucionReservaID"]);
                    estado = rd["EstadoProductoIncompleto"]?.ToString()?.Trim().ToUpperInvariant() ?? string.Empty;
                    etiquetaBlanca = rd["EtiquetaBlanca"]?.ToString()?.Trim() ?? vm.CajaProduccionID.ToString();
                }
                if (!reservaId.HasValue || reservaId.Value != vm.EjecucionProduccionID)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = $"La etiqueta blanca {etiquetaBlanca} no está relacionada con esta ejecución.";
                    return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
                }
                if (estado != ProduccionProductoIncompletoEstado.Reservada && estado != ProduccionProductoIncompletoEstado.EnCompletado)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La etiqueta blanca no se encuentra en estado válido para completarse.";
                    return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
                }
                if (!ejecucion.ParteID.HasValue || ejecucion.ParteID.Value != parteOrigen)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La pieza de la OF actual no coincide con la etiqueta blanca asignada.";
                    return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
                }
                if (capacidad <= 0 || pendiente <= 0)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La etiqueta blanca no tiene una capacidad pendiente válida.";
                    return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
                }
                if (vm.CantidadPiezas > pendiente)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = $"Solo faltan {pendiente:N0} pieza(s) para completar {etiquetaBlanca}.";
                    return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
                }
                var disponible = await ObtenerCantidadDisponibleParaCajaAsync(vm.EjecucionProduccionID, ProduccionCajaTipo.Ok, cn, tx);
                if (vm.CantidadPiezas > disponible)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = $"La OF solamente tiene {disponible:N0} pieza(s) OK disponibles para agregar a la caja.";
                    return RedirectToAction(nameof(Cajas), new { id = vm.EjecucionProduccionID });
                }
                var nuevaCantidad = cantidadActual + vm.CantidadPiezas;
                var nuevoPendiente = Math.Max(0, capacidad - nuevaCantidad);
                const string sqlDetalle = @"
INSERT INTO dbo.Produccion_CajaOrigenDetalle
(
    CajaProduccionID,EjecucionProduccionID,ProgramaProduccionID,SolicitudProduccionID,SolicitudProduccionDetalleID,
    ReleaseID,ReleaseDetalleID,CantidadPiezas,TipoMovimiento,Observaciones,UsuarioCreacionID,FechaCreacion,Activo
)
VALUES
(
    @CajaProduccionID,@EjecucionProduccionID,@ProgramaProduccionID,@SolicitudProduccionID,@SolicitudProduccionDetalleID,
    @ReleaseID,@ReleaseDetalleID,@CantidadPiezas,N'COMPLETADO',@Observaciones,@UsuarioID,SYSDATETIME(),1
);";
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
SET CantidadPiezas=@Cantidad,
    Cantidad=@Cantidad,
    CantidadPendienteCompletar=@Pendiente,
    EstadoProductoIncompleto=N'EN_COMPLETADO',
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=SYSDATETIME()
WHERE CajaProduccionID=@CajaProduccionID
  AND Activo=1
  AND EsProductoIncompleto=1
  AND EjecucionReservaID=@EjecucionProduccionID;
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
SET EjecucionProduccionID=@EjecucionProduccionID,
    ProgramaProduccionID=@ProgramaProduccionID,
    SolicitudProduccionID=@SolicitudProduccionID,
    SolicitudProduccionDetalleID=@SolicitudProduccionDetalleID,
    ReleaseID=@ReleaseID,
    ReleaseDetalleID=@ReleaseDetalleID,
    NumeroCaja=@NumeroCaja,
    FolioCaja=@FolioCaja,
    CantidadPiezas=@Cantidad,
    Cantidad=@Cantidad,
    TipoCaja=N'OK',
    EsProductoIncompleto=0,
    EstadoProductoIncompleto=N'COMPLETA',
    CantidadPendienteCompletar=0,
    FechaCompletadoIncompleto=SYSDATETIME(),
    EstadoCajaID=@EstadoCajaID,
    EstadoCajaNombre=N'Pendiente escaneo Calidad',
    EstatusCalidad=N'PENDIENTE',
    EtiquetaFolio=@FolioCaja,
    Etiqueta=@FolioCaja,
    EtiquetaVerde=0,
    FechaSolicitudCalidad=SYSDATETIME(),
    UsuarioSolicitudCalidadID=@UsuarioID,
    FechaLiberacionCalidad=NULL,
    AuditorCalidadUsuarioID=NULL,
    UsuarioCalidadID=NULL,
    ResultadoCalidad=NULL,
    MotivoCalidad=NULL,
    CodigoBarrasOrigen=NULL,
    NumeroOFEtiqueta=NULL,
    NumeroParteEtiqueta=NULL,
    DesignacionEtiqueta=NULL,
    CantidadEtiqueta=NULL,
    LoteEtiqueta=NULL,
    FechaEscaneoCalidad=NULL,
    UsuarioEscaneoCalidadID=NULL,
    FechaZonaVerde=NULL,
    UsuarioZonaVerdeID=NULL,
    FechaSalidaProduccion=NULL,
    UsuarioSalidaProduccionID=NULL,
    FechaRecepcionAlmacen=NULL,
    UsuarioAlmacenID=NULL,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=SYSDATETIME()
WHERE CajaProduccionID=@CajaProduccionID
  AND Activo=1
  AND EsProductoIncompleto=1
  AND EjecucionReservaID=@EjecucionProduccionID;
IF @@ROWCOUNT<>1 THROW 51120,'La etiqueta blanca cambió de estado mientras se completaba.',1;

UPDATE dbo.Planeacion_ProductoIncompletoApartado
SET EstatusID=5,
    UsuarioAplicacionID=@UsuarioID,
    FechaAplicacion=SYSDATETIME(),
    Activo=0,
    Observaciones=LEFT(COALESCE(NULLIF(Observaciones,N'')+N' | ',N'')+N'Producto incompleto aplicado y caja completada por ejecución '+CONVERT(NVARCHAR(20),@EjecucionProduccionID)+N'. Pendiente de primer escaneo físico de Calidad.',500)
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
                    cmd.Parameters.Add("@EstadoCajaID", SqlDbType.Int).Value = ProduccionCajaEstatus.PendienteCalidad;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    await cmd.ExecuteNonQueryAsync();
                }
                await tx.CommitAsync();
                TempData["Success"] = $"Etiqueta blanca {etiquetaBlanca} completada con {capacidad:N0} pieza(s). Ahora es la caja {nuevoFolio} y quedó pendiente del primer escaneo físico de Calidad.";
            }
            catch (Exception ex)
            {
                try { await tx.RollbackAsync(); } catch { }
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
