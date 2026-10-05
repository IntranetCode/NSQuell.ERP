// NSQ_CALIDAD_CAJAS_CLASIFICACION_V1_1
using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ERP.NSQuell.Controllers
{
    public partial class CalidadController
    {
        private const string CajaLibreEstadoPendienteAlmacen = "PENDIENTE_ALMACEN";
        private const string CajaLibreEstadoPendienteGP12 = "PENDIENTE_GP12";
        private const string CajaLibreEstadoScrap = "SCRAP";

        private const string CajaLibreEtiquetaVerde = "VERDE";
        private const string CajaLibreEtiquetaAmarilla = "AMARILLA";
        private const string CajaLibreEtiquetaRoja = "ROJA";
        private const string CajaLibreEtiquetaBlanca = "BLANCA";

        private sealed class CajaLibreClasificacionData
        {
            public long CalidadCajaInventarioID { get; set; }
            public string CodigoBarras { get; set; } = string.Empty;
            public int ParteID { get; set; }
            public string? NumeroOF { get; set; }
            public string NumeroParte { get; set; } = string.Empty;
            public string Designacion { get; set; } = string.Empty;
            public int Cantidad { get; set; }
            public string Lote { get; set; } = string.Empty;
            public string Estado { get; set; } = string.Empty;
            public string? TipoEtiqueta { get; set; }
            public bool RequiereGP12 { get; set; }
            public int? GP12SolicitudID { get; set; }
            public long? ScrapRegistroID { get; set; }
            public string? Observaciones { get; set; }
            public int? ClienteID { get; set; }
            public string? ClienteNombre { get; set; }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ClasificarCajasInventarioLibre(
            long[]? cajaInventarioIds,
            string? tipoEtiqueta,
            bool llevarGP12,
            string? observaciones,
            CancellationToken cancellationToken)
        {
            var usuarioId = ObtenerUsuarioIdActual();

            if (!usuarioId.HasValue || usuarioId.Value <= 0)
            {
                TempData["Error"] = "No se pudo identificar el usuario.";
                return RedirectToAction(nameof(Index), new { grupo = "CAJAS" });
            }

            var ids = (cajaInventarioIds ?? Array.Empty<long>())
                .Where(x => x > 0)
                .Distinct()
                .ToArray();

            if (ids.Length == 0)
            {
                TempData["Error"] = "Selecciona al menos una caja para clasificar.";
                return RedirectToAction(nameof(Index), new { grupo = "CAJAS" });
            }

            if (ids.Length > 250)
            {
                TempData["Error"] = "Clasifica como máximo 250 cajas por operación.";
                return RedirectToAction(nameof(Index), new { grupo = "CAJAS" });
            }

            var tipo = (tipoEtiqueta ?? string.Empty)
                .Trim()
                .ToUpperInvariant();

            if (tipo is not CajaLibreEtiquetaVerde
                and not CajaLibreEtiquetaAmarilla
                and not CajaLibreEtiquetaRoja
                and not CajaLibreEtiquetaBlanca)
            {
                TempData["Error"] = "Selecciona una etiqueta válida: verde, amarilla, roja o blanca.";
                return RedirectToAction(nameof(Index), new { grupo = "CAJAS" });
            }

            if (llevarGP12 && tipo != CajaLibreEtiquetaAmarilla)
            {
                TempData["Error"] = "El envío a GP12 solamente aplica para cajas con etiqueta amarilla.";
                return RedirectToAction(nameof(Index), new { grupo = "CAJAS" });
            }

            observaciones = observaciones?.Trim();

            if (llevarGP12 && string.IsNullOrWhiteSpace(observaciones))
            {
                TempData["Error"] = "Indica el motivo para llevar las cajas amarillas a GP12.";
                return RedirectToAction(nameof(Index), new { grupo = "CAJAS" });
            }

            if (!string.IsNullOrWhiteSpace(observaciones) && observaciones.Length > 500)
                observaciones = observaciones[..500];

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(cancellationToken);

            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            try
            {
                var ahora = DateTime.Now;
                var usuarioTexto = User.Identity?.Name?.Trim();

                if (string.IsNullOrWhiteSpace(usuarioTexto))
                    usuarioTexto = $"UsuarioID:{usuarioId.Value}";

                long piezas = 0;
                var procesadas = 0;

                foreach (var id in ids)
                {
                    var caja = await CargarCajaLibreParaClasificarAsync(
                        id,
                        cn,
                        tx,
                        cancellationToken);

                    if (caja == null)
                        throw new InvalidOperationException($"No existe la caja de Calidad {id}.");

                    if (!string.Equals(
                            caja.Estado,
                            EstadoCajaLibreCalidad,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            $"La caja {id} ya no está disponible para clasificación. Estado actual: {caja.Estado}.");
                    }

                    if (caja.GP12SolicitudID.HasValue || caja.ScrapRegistroID.HasValue)
                    {
                        throw new InvalidOperationException(
                            $"La caja {id} ya tiene una salida externa registrada y no puede reclasificarse desde esta bandeja.");
                    }

                    string nuevoEstado;
                    int? solicitudGp12Id = null;
                    long? scrapRegistroId = null;
                    var enviarGp12 = llevarGP12;

                    switch (tipo)
                    {
                        case CajaLibreEtiquetaVerde:
                            nuevoEstado = CajaLibreEstadoPendienteAlmacen;
                            enviarGp12 = false;
                            break;

                        case CajaLibreEtiquetaAmarilla when enviarGp12:
                            solicitudGp12Id = await CrearSolicitudGP12CajaLibreAsync(
                                caja,
                                observaciones,
                                usuarioId.Value,
                                ahora,
                                cn,
                                tx,
                                cancellationToken);

                            nuevoEstado = CajaLibreEstadoPendienteGP12;
                            break;

                        case CajaLibreEtiquetaRoja:
                            enviarGp12 = false;
                            scrapRegistroId = await RegistrarScrapCajaLibreAsync(
                                caja,
                                observaciones,
                                usuarioId.Value,
                                usuarioTexto,
                                cn,
                                tx,
                                cancellationToken);

                            nuevoEstado = CajaLibreEstadoScrap;
                            break;

                        case CajaLibreEtiquetaBlanca:
                            enviarGp12 = false;
                            nuevoEstado = EstadoCajaLibreCalidad;
                            break;

                        default:
                            enviarGp12 = false;
                            nuevoEstado = EstadoCajaLibreCalidad;
                            break;
                    }

                    var detalle = ConstruirDetalleClasificacionCajaLibre(
                        tipo,
                        enviarGp12,
                        solicitudGp12Id,
                        scrapRegistroId,
                        observaciones);

                    var observacionFinal = CombinarObservacionCajaLibre(
                        caja.Observaciones,
                        detalle,
                        800);

                    const string updateSql = @"
UPDATE dbo.Calidad_CajasInventarioLibre
SET
    TipoEtiqueta=@TipoEtiqueta,
    RequiereGP12=@RequiereGP12,
    GP12SolicitudID=@GP12SolicitudID,
    ScrapRegistroID=@ScrapRegistroID,
    Estado=@Estado,
    FechaClasificacion=@Ahora,
    UsuarioClasificacionID=@UsuarioID,
    Observaciones=@Observaciones,
    FechaModificacion=@Ahora
WHERE CalidadCajaInventarioID=@ID
  AND Activo=1
  AND Estado=N'EN_CALIDAD'
  AND GP12SolicitudID IS NULL
  AND ScrapRegistroID IS NULL;";

                    await using var update = new SqlCommand(updateSql, cn, tx);
                    update.Parameters.Add("@TipoEtiqueta", SqlDbType.NVarChar, 20).Value = tipo;
                    update.Parameters.Add("@RequiereGP12", SqlDbType.Bit).Value = enviarGp12;
                    update.Parameters.Add("@GP12SolicitudID", SqlDbType.Int).Value = (object?)solicitudGp12Id ?? DBNull.Value;
                    update.Parameters.Add("@ScrapRegistroID", SqlDbType.BigInt).Value = (object?)scrapRegistroId ?? DBNull.Value;
                    update.Parameters.Add("@Estado", SqlDbType.NVarChar, 30).Value = nuevoEstado;
                    update.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahora;
                    update.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId.Value;
                    update.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 800).Value = (object?)observacionFinal ?? DBNull.Value;
                    update.Parameters.Add("@ID", SqlDbType.BigInt).Value = id;

                    if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
                    {
                        throw new InvalidOperationException(
                            $"La caja {id} cambió mientras se clasificaba. No se guardó el lote.");
                    }

                    procesadas++;
                    piezas += caja.Cantidad;
                }

                await tx.CommitAsync(cancellationToken);

                var destino = tipo switch
                {
                    CajaLibreEtiquetaVerde => "pendientes de recepción en Almacén PT",
                    CajaLibreEtiquetaAmarilla when llevarGP12 => "enviadas a la bandeja de GP12",
                    CajaLibreEtiquetaAmarilla => "retenidas en inventario de Calidad",
                    CajaLibreEtiquetaRoja => "enviadas al flujo de Scrap",
                    CajaLibreEtiquetaBlanca => "marcadas como producto incompleto en Calidad",
                    _ => "clasificadas"
                };

                TempData["Mensaje"] =
                    $"Se clasificaron {procesadas} caja(s) / {piezas:N0} pieza(s) con etiqueta {tipo}. Quedaron {destino}.";
            }
            catch (Exception ex)
            {
                try { await tx.RollbackAsync(cancellationToken); } catch { }

                TempData["Error"] =
                    "No fue posible clasificar las cajas: " + ex.Message;
            }

            return RedirectToAction(nameof(Index), new { grupo = "CAJAS" });
        }

        private static async Task<CajaLibreClasificacionData?> CargarCajaLibreParaClasificarAsync(
            long id,
            SqlConnection cn,
            SqlTransaction tx,
            CancellationToken cancellationToken)
        {
            const string sql = @"
SELECT TOP(1)
    c.CalidadCajaInventarioID,
    c.CodigoBarras,
    c.ParteID,
    c.NumeroOF,
    COALESCE(NULLIF(LTRIM(RTRIM(p.NumeroParte)),N''),c.NumeroParteEtiqueta) AS NumeroParte,
    COALESCE(NULLIF(LTRIM(RTRIM(c.DesignacionEtiqueta)),N''),NULLIF(LTRIM(RTRIM(p.Designacion)),N''),NULLIF(LTRIM(RTRIM(p.Descripcion)),N''),N'Sin descripción') AS Designacion,
    c.Cantidad,
    COALESCE(NULLIF(LTRIM(RTRIM(c.LoteEtiqueta)),N''),CONCAT(N'CAL-',c.CalidadCajaInventarioID)) AS Lote,
    c.Estado,
    c.TipoEtiqueta,
    ISNULL(c.RequiereGP12,0) AS RequiereGP12,
    c.GP12SolicitudID,
    c.ScrapRegistroID,
    c.Observaciones,
    p.ClienteID,
    cli.Nombre AS ClienteNombre
FROM dbo.Calidad_CajasInventarioLibre c WITH(UPDLOCK,HOLDLOCK)
INNER JOIN dbo.ERP_Partes p
    ON p.ParteID=c.ParteID
LEFT JOIN dbo.ERP_Clientes cli
    ON cli.ClienteID=p.ClienteID
WHERE c.CalidadCajaInventarioID=@ID
  AND c.Activo=1;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ID", SqlDbType.BigInt).Value = id;

            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);

            if (!await rd.ReadAsync(cancellationToken))
                return null;

            return new CajaLibreClasificacionData
            {
                CalidadCajaInventarioID = Convert.ToInt64(rd["CalidadCajaInventarioID"]),
                CodigoBarras = rd["CodigoBarras"]?.ToString()?.Trim() ?? string.Empty,
                ParteID = Convert.ToInt32(rd["ParteID"]),
                NumeroOF = rd["NumeroOF"] == DBNull.Value ? null : rd["NumeroOF"]?.ToString()?.Trim(),
                NumeroParte = rd["NumeroParte"]?.ToString()?.Trim() ?? string.Empty,
                Designacion = rd["Designacion"]?.ToString()?.Trim() ?? string.Empty,
                Cantidad = Convert.ToInt32(rd["Cantidad"]),
                Lote = rd["Lote"]?.ToString()?.Trim() ?? string.Empty,
                Estado = rd["Estado"]?.ToString()?.Trim() ?? string.Empty,
                TipoEtiqueta = rd["TipoEtiqueta"] == DBNull.Value ? null : rd["TipoEtiqueta"]?.ToString()?.Trim(),
                RequiereGP12 = Convert.ToBoolean(rd["RequiereGP12"]),
                GP12SolicitudID = rd["GP12SolicitudID"] == DBNull.Value ? null : Convert.ToInt32(rd["GP12SolicitudID"]),
                ScrapRegistroID = rd["ScrapRegistroID"] == DBNull.Value ? null : Convert.ToInt64(rd["ScrapRegistroID"]),
                Observaciones = rd["Observaciones"] == DBNull.Value ? null : rd["Observaciones"]?.ToString()?.Trim(),
                ClienteID = rd["ClienteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ClienteID"]),
                ClienteNombre = rd["ClienteNombre"] == DBNull.Value ? null : rd["ClienteNombre"]?.ToString()?.Trim()
            };
        }

        private static async Task<int> CrearSolicitudGP12CajaLibreAsync(
            CajaLibreClasificacionData caja,
            string? motivo,
            int usuarioId,
            DateTime ahora,
            SqlConnection cn,
            SqlTransaction tx,
            CancellationToken cancellationToken)
        {
            if (caja.GP12SolicitudID.HasValue)
                return caja.GP12SolicitudID.Value;

            var motivoFinal = string.IsNullOrWhiteSpace(motivo)
                ? "Caja amarilla enviada desde Calidad a GP12."
                : motivo.Trim();

            const string sql = @"
INSERT INTO dbo.GP12_Solicitudes
(
    Origen,
    ProgramaProduccionID,
    EjecucionProduccionID,
    CalidadInspeccionID,
        CajaProduccionID,
    CajaLiberadaID,
    CalidadCajaInventarioID,
    SolicitudProduccionID,
    SolicitudProduccionDetalleID,
    OrdenFabricacion,
    ClienteID,
    ClienteNombre,
    ParteID,
    NumeroParte,
    DescripcionParte,
    MaterialID,
    MaterialCodigo,
    MaterialDescripcion,
    CantidadSolicitada,
    CantidadRecibida,
    CantidadProcesada,
    CantidadPendiente,
    Motivo,
    InstruccionTrabajo,
    CodigoHIP,
    CodigoHOE,
    Observaciones,
    EstatusID,
    FechaSolicitud,
    UsuarioSolicitudID,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
OUTPUT INSERTED.SolicitudGP12ID
VALUES
(
        @Origen,
    NULL,NULL,NULL,NULL,NULL,@CalidadCajaInventarioID,NULL,NULL,
    @OrdenFabricacion,
    @ClienteID,
    @ClienteNombre,
    @ParteID,
    @NumeroParte,
    @DescripcionParte,
    NULL,NULL,NULL,
    @Cantidad,
    0,0,0,
    @Motivo,
    NULL,NULL,NULL,
    @Observaciones,
    @EstatusID,
    @Ahora,
    @UsuarioID,
    @UsuarioID,
    @Ahora,
    1
);";

            int solicitudId;

            await using (var cmd = new SqlCommand(sql, cn, tx))
            {
                cmd.Parameters.Add("@Origen", SqlDbType.NVarChar, 20).Value = GP12Origen.Calidad;
                cmd.Parameters.Add("@CalidadCajaInventarioID", SqlDbType.BigInt).Value = caja.CalidadCajaInventarioID;
                cmd.Parameters.Add("@OrdenFabricacion", SqlDbType.NVarChar, 100).Value = string.IsNullOrWhiteSpace(caja.NumeroOF) ? DBNull.Value : caja.NumeroOF;
                cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = (object?)caja.ClienteID ?? DBNull.Value;
                cmd.Parameters.Add("@ClienteNombre", SqlDbType.NVarChar, 250).Value = string.IsNullOrWhiteSpace(caja.ClienteNombre) ? DBNull.Value : caja.ClienteNombre;
                cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = caja.ParteID;
                cmd.Parameters.Add("@NumeroParte", SqlDbType.NVarChar, 150).Value = caja.NumeroParte;
                cmd.Parameters.Add("@DescripcionParte", SqlDbType.NVarChar, 500).Value = caja.Designacion;

                var cantidad = cmd.Parameters.Add("@Cantidad", SqlDbType.Decimal);
                cantidad.Precision = 18;
                cantidad.Scale = 4;
                cantidad.Value = Convert.ToDecimal(caja.Cantidad);

                cmd.Parameters.Add("@Motivo", SqlDbType.NVarChar, 1000).Value = motivoFinal;
                cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 2000).Value =
                    $"Caja de inventario libre de Calidad #{caja.CalidadCajaInventarioID}. Etiqueta: {caja.CodigoBarras}.";
                cmd.Parameters.Add("@EstatusID", SqlDbType.Int).Value = GP12Estatus.Recibido;
                cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahora;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;

                var result = await cmd.ExecuteScalarAsync(cancellationToken);

                if (result == null || result == DBNull.Value)
                    throw new InvalidOperationException("No fue posible crear la solicitud GP12.");

                solicitudId = Convert.ToInt32(result);
            }

            const string etiquetaSql = @"
INSERT INTO dbo.GP12_SolicitudEtiquetas
(
    SolicitudGP12ID,
    TipoEtiqueta,
    CantidadSolicitada,
    CantidadRecibida,
    CantidadProcesada,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
VALUES
(
    @SolicitudGP12ID,
    @TipoEtiqueta,
    @Cantidad,
    0,
    0,
    @UsuarioID,
    @Ahora,
    1
);";

            await using (var cmd = new SqlCommand(etiquetaSql, cn, tx))
            {
                cmd.Parameters.Add("@SolicitudGP12ID", SqlDbType.Int).Value = solicitudId;
                cmd.Parameters.Add("@TipoEtiqueta", SqlDbType.VarChar, 20).Value = GP12TipoEtiqueta.Amarilla;

                var cantidad = cmd.Parameters.Add("@Cantidad", SqlDbType.Decimal);
                cantidad.Precision = 18;
                cantidad.Scale = 4;
                cantidad.Value = Convert.ToDecimal(caja.Cantidad);

                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahora;
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            const string historialSql = @"
INSERT INTO dbo.GP12_Historial
(
    SolicitudGP12ID,
    Movimiento,
    EstatusAnteriorID,
    EstatusNuevoID,
    Entidad,
    EntidadID,
    Comentario,
    UsuarioID,
    FechaMovimiento
)
VALUES
(
    @SolicitudGP12ID,
    @Movimiento,
    NULL,
    @EstatusNuevoID,
    @Entidad,
    @EntidadID,
    @Comentario,
    @UsuarioID,
    @Ahora
);";

            await using (var cmd = new SqlCommand(historialSql, cn, tx))
            {
                cmd.Parameters.Add("@SolicitudGP12ID", SqlDbType.Int).Value = solicitudId;
                cmd.Parameters.Add("@Movimiento", SqlDbType.NVarChar, 100).Value = GP12Movimientos.SolicitudCreada;
                cmd.Parameters.Add("@EstatusNuevoID", SqlDbType.Int).Value = GP12Estatus.Recibido;
                cmd.Parameters.Add("@Entidad", SqlDbType.NVarChar, 30).Value = GP12EntidadHistorial.Solicitud;
                cmd.Parameters.Add("@EntidadID", SqlDbType.Int).Value = solicitudId;
                cmd.Parameters.Add("@Comentario", SqlDbType.NVarChar, 2000).Value =
                    $"Solicitud GP12 creada desde Calidad/CAJAS. Caja libre #{caja.CalidadCajaInventarioID}; etiqueta {caja.CodigoBarras}; cantidad {caja.Cantidad:N0}. Motivo: {motivoFinal}";
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahora;
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            return solicitudId;
        }

        private static async Task<long> RegistrarScrapCajaLibreAsync(
            CajaLibreClasificacionData caja,
            string? observaciones,
            int usuarioId,
            string usuarioTexto,
            SqlConnection cn,
            SqlTransaction tx,
            CancellationToken cancellationToken)
        {
            if (caja.ScrapRegistroID.HasValue)
                return caja.ScrapRegistroID.Value;

            await using var cmd = new SqlCommand(
                "dbo.usp_AlmacenScrap_RegistrarOrigen",
                cn,
                tx)
            {
                CommandType = CommandType.StoredProcedure
            };

            cmd.Parameters.Add("@Origen", SqlDbType.NVarChar, 30).Value = "CALIDAD";
            cmd.Parameters.Add("@OrigenReferenciaID", SqlDbType.BigInt).Value = caja.CalidadCajaInventarioID;
            cmd.Parameters.Add("@OrigenReferencia", SqlDbType.NVarChar, 120).Value = $"CALIDAD_CAJA_LIBRE:{caja.CalidadCajaInventarioID}";
            cmd.Parameters.Add("@CodigoBarras", SqlDbType.NVarChar, 500).Value = caja.CodigoBarras;
            cmd.Parameters.Add("@NumeroOF", SqlDbType.NVarChar, 80).Value =
                string.IsNullOrWhiteSpace(caja.NumeroOF)
                    ? $"CALIDAD-{caja.CalidadCajaInventarioID}"
                    : caja.NumeroOF;
            cmd.Parameters.Add("@NumeroParte", SqlDbType.NVarChar, 120).Value = caja.NumeroParte;
            cmd.Parameters.Add("@Designacion", SqlDbType.NVarChar, 300).Value = caja.Designacion;
            cmd.Parameters.Add("@CantidadPiezas", SqlDbType.Int).Value = caja.Cantidad;
            cmd.Parameters.Add("@Lote", SqlDbType.NVarChar, 120).Value =
                string.IsNullOrWhiteSpace(caja.Lote)
                    ? $"CAL-{caja.CalidadCajaInventarioID}"
                    : caja.Lote;
            cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = caja.ParteID;
            cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 800).Value =
                string.IsNullOrWhiteSpace(observaciones)
                    ? $"Etiqueta ROJA asignada desde Calidad/CAJAS. Caja libre #{caja.CalidadCajaInventarioID}."
                    : $"Etiqueta ROJA asignada desde Calidad/CAJAS. Caja libre #{caja.CalidadCajaInventarioID}. {observaciones}";
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            cmd.Parameters.Add("@UsuarioNombre", SqlDbType.NVarChar, 180).Value = usuarioTexto;

            var result = await cmd.ExecuteScalarAsync(cancellationToken);

            if (result == null || result == DBNull.Value)
                throw new InvalidOperationException("No fue posible registrar la caja en el flujo de Scrap.");

            return Convert.ToInt64(result);
        }

        private static string ConstruirDetalleClasificacionCajaLibre(
            string tipo,
            bool llevarGp12,
            int? solicitudGp12Id,
            long? scrapRegistroId,
            string? observaciones)
        {
            var texto = tipo switch
            {
                CajaLibreEtiquetaVerde => "Clasificación VERDE. Caja liberada por Calidad y pendiente de recepción física en Almacén PT.",
                CajaLibreEtiquetaAmarilla when llevarGp12 => $"Clasificación AMARILLA. Se generó solicitud GP12 #{solicitudGp12Id}.",
                CajaLibreEtiquetaAmarilla => "Clasificación AMARILLA. Caja retenida e inventariada en Calidad; no se envió a GP12.",
                CajaLibreEtiquetaRoja => $"Clasificación ROJA. Caja enviada al flujo de Scrap #{scrapRegistroId}.",
                CajaLibreEtiquetaBlanca => "Clasificación BLANCA. Producto incompleto; permanece inventariado en Calidad.",
                _ => $"Clasificación {tipo}."
            };

            if (!string.IsNullOrWhiteSpace(observaciones))
                texto += " " + observaciones.Trim();

            return texto;
        }

        private static string? CombinarObservacionCajaLibre(
            string? anterior,
            string? nueva,
            int maximo)
        {
            var partes = new List<string>();

            if (!string.IsNullOrWhiteSpace(anterior))
                partes.Add(anterior.Trim());

            if (!string.IsNullOrWhiteSpace(nueva))
                partes.Add(nueva.Trim());

            if (partes.Count == 0)
                return null;

            var texto = string.Join(" | ", partes);
            return texto.Length <= maximo ? texto : texto[..maximo];
        }
    }
}