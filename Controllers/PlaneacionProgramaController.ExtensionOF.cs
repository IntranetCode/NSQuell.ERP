using ERP.NSQuell.Models;
using ERP.NSQuell.Servicios.Planeacion;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace ERP.NSQuell.Controllers;

// NSQ_PLANEACION_EXTENSION_OF_V2
public partial class PlaneacionProgramaController
{
    private const string ExtensionMotivoContinuidad = "CONTINUIDAD_MAQUINA";
    private const string ExtensionMotivoRelease = "ADELANTO_RELEASE";

    public sealed class PlaneacionExtensionOFRequest
    {
        public int SolicitudProduccionID { get; set; }
        public string Motivo { get; set; } = string.Empty;
        public int CantidadAdicional { get; set; }
        public decimal? HorasAdicionales { get; set; }
        public int? ReleaseDetalleOrigenID { get; set; }
        // NSQ_PLANEACION_AUMENTO_EXTENSION_V4
        public List<PlaneacionProgramaAumentoLineaVm> AumentosRelease { get; set; } = new();
    }

    private sealed class PlaneacionExtensionContexto
    {
        public int SolicitudProduccionID { get; set; }
        public int SolicitudProduccionDetalleID { get; set; }
        public int ProgramaProduccionID { get; set; }
        public int EjecucionProduccionID { get; set; }
        public int? ReleaseID { get; set; }
        public int? ReleaseDetalleID { get; set; }
        public int? ClienteID { get; set; }
        public int? MaquinaID { get; set; }
        public string Folio { get; set; } = string.Empty;
        public string Cliente { get; set; } = string.Empty;
        public string Parte { get; set; } = string.Empty;
        public string Maquina { get; set; } = string.Empty;
        public int CantidadActual { get; set; }
        public decimal HorasActuales { get; set; }
        public decimal? ObjetivoHora { get; set; }
        public decimal? PesoBrutoPieza { get; set; }
        public decimal? PiezasPorEmbalaje { get; set; }
        public DateTime FechaInicioProgramada { get; set; }
        public DateTime FechaFinProgramada { get; set; }
        public bool TrabajarDomingo { get; set; }
    }

    [HttpGet]
    public async Task<IActionResult> ObtenerExtensionOF(int solicitudProduccionId)
    {
        if (solicitudProduccionId <= 0)
            return BadRequest(new { ok = false, mensaje = "La OF no es valida." });

        var usuarioId = ObtenerUsuarioID();
        if (usuarioId <= 0)
            return Unauthorized(new { ok = false, mensaje = "No fue posible identificar al usuario." });

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        var contexto = await CargarContextoExtensionAsync(solicitudProduccionId, cn, null, bloquear: false);
        if (contexto == null)
            return NotFound(new { ok = false, mensaje = "No se encontro una OF activa vinculada a un programa de Planeacion." });

        var bloqueo = await ObtenerBloqueoLogisticaAsync(solicitudProduccionId, cn, null);
        if (!string.IsNullOrWhiteSpace(bloqueo))
            return BadRequest(new { ok = false, mensaje = bloqueo });

        List<PlaneacionProgramaAumentoOrigenVm> origenes = new();
        if (contexto.ReleaseDetalleID.HasValue && contexto.ReleaseID.HasValue)
        {
            origenes = await CargarOrigenesAumentoAsync(
                contexto.ReleaseDetalleID.Value,
                contexto.ReleaseID,
                contexto.ClienteID,
                cn);
        }

        return Json(new
        {
            ok = true,
            solicitudProduccionId = contexto.SolicitudProduccionID,
            programaProduccionId = contexto.ProgramaProduccionID,
            ejecucionProduccionId = contexto.EjecucionProduccionID,
            folio = contexto.Folio,
            cliente = contexto.Cliente,
            parte = contexto.Parte,
            maquina = contexto.Maquina,
            cantidadActual = contexto.CantidadActual,
            horasActuales = contexto.HorasActuales,
            objetivoHora = contexto.ObjetivoHora,
            fechaInicioProgramada = contexto.FechaInicioProgramada,
            fechaFinProgramada = contexto.FechaFinProgramada,
            tieneRelease = contexto.ReleaseDetalleID.HasValue && contexto.ReleaseID.HasValue,
            origenes
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AplicarExtensionOF([FromForm] PlaneacionExtensionOFRequest request)
    {
        var usuarioId = ObtenerUsuarioID();
        if (usuarioId <= 0)
            return Unauthorized(new { ok = false, mensaje = "No fue posible identificar al usuario." });

        if (request.SolicitudProduccionID <= 0)
            return BadRequest(new { ok = false, mensaje = "La OF no es valida." });

        request.Motivo = (request.Motivo ?? string.Empty).Trim().ToUpperInvariant();
        if (request.Motivo != ExtensionMotivoContinuidad && request.Motivo != ExtensionMotivoRelease)
            return BadRequest(new { ok = false, mensaje = "Selecciona un motivo de extension valido." });

        request.AumentosRelease ??= new List<PlaneacionProgramaAumentoLineaVm>();

        if (request.Motivo == ExtensionMotivoContinuidad)
        {
            if (request.CantidadAdicional <= 0)
                return BadRequest(new { ok = false, mensaje = "Las piezas adicionales deben ser mayores a cero." });

            if (!request.HorasAdicionales.HasValue || request.HorasAdicionales.Value <= 0)
                return BadRequest(new { ok = false, mensaje = "Para continuar la maquina debes capturar horas adicionales mayores a cero." });

            request.AumentosRelease.Clear();
        }
        else
        {
            if (request.HorasAdicionales.HasValue && request.HorasAdicionales.Value < 0)
                return BadRequest(new { ok = false, mensaje = "Las horas adicionales no pueden ser negativas. Para el motivo 2 se permite 0." });

            request.HorasAdicionales ??= 0m;
            request.AumentosRelease = request.AumentosRelease
                .Where(x => x != null && ((x.ReleaseDetalleOrigenAumentoID ?? 0) > 0 || x.CantidadPiezas != 0))
                .ToList();

            if (request.AumentosRelease.Count == 0 &&
                request.ReleaseDetalleOrigenID.HasValue &&
                request.ReleaseDetalleOrigenID.Value > 0 &&
                request.CantidadAdicional > 0)
            {
                request.AumentosRelease.Add(new PlaneacionProgramaAumentoLineaVm
                {
                    ReleaseDetalleOrigenAumentoID = request.ReleaseDetalleOrigenID,
                    CantidadPiezas = request.CantidadAdicional
                });
            }

            if (request.AumentosRelease.Count == 0)
                return BadRequest(new { ok = false, mensaje = "Agrega al menos una fila Release con piezas." });

            var duplicados = request.AumentosRelease
                .Where(x => x.ReleaseDetalleOrigenAumentoID.HasValue)
                .GroupBy(x => x.ReleaseDetalleOrigenAumentoID!.Value)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicados.Count > 0)
                return BadRequest(new { ok = false, mensaje = "Una misma entrega Release no puede utilizarse mas de una vez." });

            long total = 0;
            for (var i = 0; i < request.AumentosRelease.Count; i++)
            {
                var linea = request.AumentosRelease[i];
                if (!linea.ReleaseDetalleOrigenAumentoID.HasValue || linea.ReleaseDetalleOrigenAumentoID.Value <= 0)
                    return BadRequest(new { ok = false, mensaje = $"Selecciona la entrega Release de la fila {i + 1}." });
                if (linea.CantidadPiezas <= 0)
                    return BadRequest(new { ok = false, mensaje = $"Las piezas de la fila {i + 1} deben ser mayores a cero." });
                total += linea.CantidadPiezas;
                if (total > int.MaxValue)
                    return BadRequest(new { ok = false, mensaje = "La suma de piezas supera el limite permitido." });
            }

            request.CantidadAdicional = Convert.ToInt32(total);
            request.ReleaseDetalleOrigenID = request.AumentosRelease[0].ReleaseDetalleOrigenAumentoID;
        }
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();
        await using var txBase = await cn.BeginTransactionAsync(IsolationLevel.Serializable);
        var tx = (SqlTransaction)txBase;

        try
        {
            var contexto = await CargarContextoExtensionAsync(request.SolicitudProduccionID, cn, tx, bloquear: true);
            if (contexto == null)
                throw new InvalidOperationException("No se encontro una OF activa vinculada a un programa de Planeacion.");

            var bloqueoLogistica = await ObtenerBloqueoLogisticaAsync(request.SolicitudProduccionID, cn, tx);
            if (!string.IsNullOrWhiteSpace(bloqueoLogistica))
                throw new InvalidOperationException(bloqueoLogistica);

            // La extension V2 esta pensada para un extra imprevisto durante una corrida ya iniciada.
            if (contexto.EjecucionProduccionID <= 0)
                throw new InvalidOperationException("La extension solo se puede aplicar cuando Produccion ya inicio la ejecucion y aun no la cierra.");

            int nuevaCantidad;
            checked
            {
                nuevaCantidad = contexto.CantidadActual + request.CantidadAdicional;
            }

            decimal horasAdicionales;
            int? transferenciaId = null;
            var transferenciasIds = new List<int>();

            if (request.Motivo == ExtensionMotivoContinuidad)
            {
                horasAdicionales = Math.Round(request.HorasAdicionales!.Value, 4, MidpointRounding.AwayFromZero);
            }
            else
            {
                if (!contexto.ReleaseDetalleID.HasValue || !contexto.ReleaseID.HasValue)
                    throw new InvalidOperationException("Esta OF no tiene un ReleaseDetalle relacionado.");

                foreach (var linea in request.AumentosRelease)
                {
                    var aumentoVm = new PlaneacionProgramaCrearDesdeNecesidadVm
                    {
                        ReleaseDetalleID = contexto.ReleaseDetalleID.Value,
                        ReleaseDetalleOrigenAumentoID = linea.ReleaseDetalleOrigenAumentoID,
                        CantidadAumentoPiezas = linea.CantidadPiezas
                    };

                    var id = await AplicarAumentoReleaseAsync(
                        aumentoVm,
                        usuarioId,
                        cn,
                        tx,
                        "Extension de OF: adelanto/faltante de piezas solicitado desde Planeacion.");

                    if (id.HasValue)
                    {
                        transferenciasIds.Add(id.Value);
                        transferenciaId ??= id.Value;
                        await VincularTransferenciaAProgramaAsync(
                            id.Value,
                            contexto.ProgramaProduccionID,
                            contexto.ReleaseDetalleID.Value,
                            cn,
                            tx);
                    }
                }

                horasAdicionales = Math.Round(
                    request.HorasAdicionales ?? 0m,
                    4,
                    MidpointRounding.AwayFromZero);
            }
            var nuevasHoras = Math.Round(
                contexto.HorasActuales + horasAdicionales,
                4,
                MidpointRounding.AwayFromZero);

            var secuencia = new PlaneacionSecuenciaService();
            var nuevoFin = horasAdicionales > 0
                ? secuencia.AjustarFechaFinOperativa(
                    contexto.FechaFinProgramada,
                    horasAdicionales,
                    contexto.TrabajarDomingo)
                : contexto.FechaFinProgramada;

            var motivoTexto = request.Motivo == ExtensionMotivoContinuidad
                ? "No detener maquina, continuar con la produccion"
                : "Adelanto de piezas para el cliente / la OF se programo con menos piezas de las requeridas";

            // 1) Programa: cantidad y consumos. El servicio de secuencia actualizara horas/fin y recorrera la cola.
            const string sqlPrograma = @"
UPDATE dbo.Planeacion_ProgramaProduccion
SET CantidadProgramada = @NuevaCantidad,
    CantidadMpKg = CASE
        WHEN ISNULL(PesoBrutoPieza,0) > 0
            THEN ROUND(CONVERT(decimal(18,6),@NuevaCantidad) * PesoBrutoPieza,4)
        ELSE 0
    END,
    CantidadEmbalajes = CASE
        WHEN ISNULL(PiezasPorEmbalaje,0) > 0
            THEN CEILING(CONVERT(decimal(18,4),@NuevaCantidad) / PiezasPorEmbalaje)
        ELSE 0
    END,
    CantidadRequerida = CASE
        WHEN @EsRelease = 1 AND ReleaseDetalleID IS NOT NULL
            THEN ISNULL((SELECT TOP(1) rd.CantidadRequerida
                         FROM dbo.Planeacion_ReleaseDetalle rd
                         WHERE rd.ReleaseDetalleID = dbo.Planeacion_ProgramaProduccion.ReleaseDetalleID), CantidadRequerida)
        ELSE CantidadRequerida
    END,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE ProgramaProduccionID = @ProgramaProduccionID
  AND Activo = 1;";

            await using (var cmd = new SqlCommand(sqlPrograma, cn, tx))
            {
                cmd.Parameters.Add("@NuevaCantidad", SqlDbType.Int).Value = nuevaCantidad;
                cmd.Parameters.Add("@EsRelease", SqlDbType.Bit).Value = request.Motivo == ExtensionMotivoRelease;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = contexto.ProgramaProduccionID;
                if (await cmd.ExecuteNonQueryAsync() != 1)
                    throw new InvalidOperationException("No fue posible actualizar la cantidad del programa.");
            }

            // 2) OF: cantidad, horas, MP, embalaje y costos derivados del renglon.
            const string sqlDetalle = @"
UPDATE dbo.SolicitudesProduccionDetalle
SET CantidadPiezas = @NuevaCantidad,
    HorasPlaneadas = @NuevasHoras,
    CantidadMpKg = CASE
        WHEN ISNULL(PesoBrutoPieza,0) > 0
            THEN ROUND(CONVERT(decimal(18,6),@NuevaCantidad) * PesoBrutoPieza,4)
        ELSE 0
    END,
    CantidadEmbalajes = CASE
        WHEN ISNULL(PiezasPorEmbalaje,0) > 0
            THEN CEILING(CONVERT(decimal(18,4),@NuevaCantidad) / PiezasPorEmbalaje)
        ELSE 0
    END
WHERE SolicitudProduccionDetalleID = @SolicitudProduccionDetalleID
  AND SolicitudProduccionID = @SolicitudProduccionID
  AND Activo = 1;

UPDATE dbo.SolicitudesProduccionDetalle
SET CostoMPTotal = ROUND(ISNULL(CostoMPUnitario,0) * ISNULL(CantidadMpKg,0),4),
    CostoEmbalajeTotal = ROUND(ISNULL(CostoEmbalajeUnitario,0) * ISNULL(CantidadEmbalajes,0),4),
    CostoTotalRenglon = ROUND(
        (ISNULL(CostoMPUnitario,0) * ISNULL(CantidadMpKg,0)) +
        (ISNULL(CostoEmbalajeUnitario,0) * ISNULL(CantidadEmbalajes,0)),4),
    VentaTotalRenglon = ROUND(ISNULL(PrecioVentaUnitario,0) * ISNULL(CantidadPiezas,0),4),
    UtilidadEstimadaRenglon = ROUND(
        (ISNULL(PrecioVentaUnitario,0) * ISNULL(CantidadPiezas,0)) -
        ((ISNULL(CostoMPUnitario,0) * ISNULL(CantidadMpKg,0)) +
         (ISNULL(CostoEmbalajeUnitario,0) * ISNULL(CantidadEmbalajes,0))),4)
WHERE SolicitudProduccionDetalleID = @SolicitudProduccionDetalleID
  AND Activo = 1;

UPDATE s
SET CostoMPTotal = x.CostoMP,
    CostoEmbalajeTotal = x.CostoEmb,
    CostoTotalOF = x.CostoTotal,
    VentaTotalOF = x.Venta,
    UtilidadEstimadaOF = x.Utilidad,
    FechaFinPlaneada = @NuevoFin
FROM dbo.SolicitudesProduccion s
CROSS APPLY
(
    SELECT
        ISNULL(SUM(ISNULL(d.CostoMPTotal,0)),0) AS CostoMP,
        ISNULL(SUM(ISNULL(d.CostoEmbalajeTotal,0)),0) AS CostoEmb,
        ISNULL(SUM(ISNULL(d.CostoTotalRenglon,0)),0) AS CostoTotal,
        ISNULL(SUM(ISNULL(d.VentaTotalRenglon,0)),0) AS Venta,
        ISNULL(SUM(ISNULL(d.UtilidadEstimadaRenglon,0)),0) AS Utilidad
    FROM dbo.SolicitudesProduccionDetalle d
    WHERE d.SolicitudProduccionID = s.SolicitudProduccionID
      AND d.Activo = 1
) x
WHERE s.SolicitudProduccionID = @SolicitudProduccionID
  AND s.Activo = 1;";

            await using (var cmd = new SqlCommand(sqlDetalle, cn, tx))
            {
                cmd.Parameters.Add("@NuevaCantidad", SqlDbType.Int).Value = nuevaCantidad;
                var pHoras = cmd.Parameters.Add("@NuevasHoras", SqlDbType.Decimal); pHoras.Precision = 18; pHoras.Scale = 4; pHoras.Value = nuevasHoras;
                cmd.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value = contexto.SolicitudProduccionDetalleID;
                cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = contexto.SolicitudProduccionID;
                cmd.Parameters.Add("@NuevoFin", SqlDbType.DateTime2).Value = nuevoFin;
                await cmd.ExecuteNonQueryAsync();
            }

            // 3) Asignacion de maquina de la OF.
            const string sqlAsignacion = @"
UPDATE dbo.SolicitudesProduccionAsignacionMaquina
SET CantidadAsignada = @NuevaCantidad,
    HorasEstimadas = @NuevasHoras,
    FechaProgramadaTentativa = CAST(@FechaInicio AS date),
    HoraInicioTentativa = CAST(@FechaInicio AS time),
    HoraFinTentativa = CAST(@NuevoFin AS time)
WHERE SolicitudProduccionDetalleID = @SolicitudProduccionDetalleID
  AND Activo = 1
  AND (@MaquinaID IS NULL OR MaquinaID = @MaquinaID);";

            await using (var cmd = new SqlCommand(sqlAsignacion, cn, tx))
            {
                cmd.Parameters.Add("@NuevaCantidad", SqlDbType.Int).Value = nuevaCantidad;
                var pHoras = cmd.Parameters.Add("@NuevasHoras", SqlDbType.Decimal); pHoras.Precision = 18; pHoras.Scale = 4; pHoras.Value = nuevasHoras;
                cmd.Parameters.Add("@FechaInicio", SqlDbType.DateTime2).Value = contexto.FechaInicioProgramada;
                cmd.Parameters.Add("@NuevoFin", SqlDbType.DateTime2).Value = nuevoFin;
                cmd.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value = contexto.SolicitudProduccionDetalleID;
                cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = (object?)contexto.MaquinaID ?? DBNull.Value;
                await cmd.ExecuteNonQueryAsync();
            }

            // 4) Produccion: la ejecucion activa debe cerrar contra la meta nueva, no contra el snapshot viejo.
            const string sqlProduccion = @"
UPDATE dbo.Produccion_Ejecucion
SET CantidadPlaneada = @NuevaCantidad
WHERE EjecucionProduccionID = @EjecucionProduccionID
  AND SolicitudProduccionID = @SolicitudProduccionID
  AND ProgramaProduccionID = @ProgramaProduccionID
  AND Activo = 1
  AND FechaInicioReal IS NOT NULL
  AND FechaFinReal IS NULL;";

            await using (var cmd = new SqlCommand(sqlProduccion, cn, tx))
            {
                cmd.Parameters.Add("@NuevaCantidad", SqlDbType.Int).Value = nuevaCantidad;
                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = contexto.EjecucionProduccionID;
                cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = contexto.SolicitudProduccionID;
                cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = contexto.ProgramaProduccionID;
                if (await cmd.ExecuteNonQueryAsync() != 1)
                    throw new InvalidOperationException("La ejecucion de Produccion dejo de estar activa durante la extension.");
            }

            // 5) Calidad: solo inspecciones no invalidadas y no cerradas/finales.
            const string sqlCalidad = @"
IF OBJECT_ID(N'dbo.Calidad_Inspecciones',N'U') IS NOT NULL
BEGIN
    UPDATE dbo.Calidad_Inspecciones
    SET CantidadTotal = @NuevaCantidad,
        CantidadPendiente = CASE
            WHEN CONVERT(decimal(18,3),@NuevaCantidad) > ISNULL(CantidadRevisada,0)
                THEN CONVERT(decimal(18,3),@NuevaCantidad) - ISNULL(CantidadRevisada,0)
            ELSE 0
        END,
        FechaFinProgramada = @NuevoFin,
        UsuarioModificacionID = @UsuarioID,
        FechaModificacion = GETDATE()
    WHERE SolicitudProduccionID = @SolicitudProduccionID
      AND (ProgramaProduccionID = @ProgramaProduccionID OR ProgramaProduccionID IS NULL)
      AND ISNULL(ConfiguracionInvalidada,0) = 0
      AND ISNULL(Estado,N'') NOT IN (N'MATERIAL_LIBERADO',N'MATERIAL_NO_CONFORME',N'CERRADA');
END;";

            await using (var cmd = new SqlCommand(sqlCalidad, cn, tx))
            {
                cmd.Parameters.Add("@NuevaCantidad", SqlDbType.Int).Value = nuevaCantidad;
                cmd.Parameters.Add("@NuevoFin", SqlDbType.DateTime2).Value = nuevoFin;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = contexto.SolicitudProduccionID;
                cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = contexto.ProgramaProduccionID;
                await cmd.ExecuteNonQueryAsync();
            }

            // 6) Calendario: reutiliza el servicio canonico que recorre cola de maquina/molde.
            var recorridos = 0;
            if (horasAdicionales > 0)
            {
                recorridos = await secuencia.ReacomodarPorCambioDuracionAsync(
                    contexto.ProgramaProduccionID,
                    contexto.EjecucionProduccionID,
                    nuevoFin,
                    nuevasHoras,
                    usuarioId,
                    $"Extension OF {contexto.Folio}. Motivo: {motivoTexto}. Cantidad anterior: {contexto.CantidadActual:N0}. Extension: +{request.CantidadAdicional:N0}. Cantidad nueva: {nuevaCantidad:N0}. Horas adicionales: {horasAdicionales:0.####}.",
                    cn,
                    tx,
                    contexto.TrabajarDomingo);
            }

            // 7) Almacen: vuelve a calcular el surtimiento despues de aumentar MP/embalaje.
            await RecalcularAlmacenExtensionAsync(contexto.SolicitudProduccionID, cn, tx);

            // 8) Auditoria visible desde el detalle de Planeacion.
            const string sqlHistorial = @"
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
SELECT
    s.SolicitudProduccionID,
    s.EstatusID,
    s.EstatusID,
    N'EXTENSION_OF',
    @Comentario,
    @UsuarioID,
    GETDATE()
FROM dbo.SolicitudesProduccion s
WHERE s.SolicitudProduccionID = @SolicitudProduccionID
  AND s.Activo = 1;";

            var comentario =
                $"Extension de OF. Motivo: {motivoTexto}. " +
                $"Cantidad anterior: {contexto.CantidadActual:N0}. Extension: +{request.CantidadAdicional:N0}. " +
                $"Cantidad nueva: {nuevaCantidad:N0}. Horas anteriores: {contexto.HorasActuales:0.####}. " +
                $"Horas adicionales: {horasAdicionales:0.####}. Horas nuevas: {nuevasHoras:0.####}. " +
                $"Fin anterior: {contexto.FechaFinProgramada:dd/MM/yyyy HH:mm}. Nuevo fin: {nuevoFin:dd/MM/yyyy HH:mm}. " +
                $"Programas recorridos: {recorridos}. " +
                (transferenciasIds.Count > 0 ? $"Transferencias Release ID: {string.Join(", ", transferenciasIds)}." : string.Empty);

            await using (var cmd = new SqlCommand(sqlHistorial, cn, tx))
            {
                cmd.Parameters.Add("@Comentario", SqlDbType.NVarChar, -1).Value = comentario;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = contexto.SolicitudProduccionID;
                await cmd.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();

            return Json(new
            {
                ok = true,
                mensaje = "La extension se aplico correctamente.",
                cantidadAnterior = contexto.CantidadActual,
                cantidadAdicional = request.CantidadAdicional,
                cantidadNueva = nuevaCantidad,
                horasAdicionales,
                horasNuevas = nuevasHoras,
                nuevoFin,
                programasRecorridos = recorridos,
                transferenciaReleaseId = transferenciaId,
                transferenciasReleaseIds = transferenciasIds
            });
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(); } catch { }
            return BadRequest(new { ok = false, mensaje = ex.Message });
        }
    }

    private static async Task<PlaneacionExtensionContexto?> CargarContextoExtensionAsync(
        int solicitudProduccionId,
        SqlConnection cn,
        SqlTransaction? tx,
        bool bloquear)
    {
        var lockHint = bloquear ? " WITH(UPDLOCK,HOLDLOCK)" : string.Empty;

        var sql = $@"
SELECT TOP(2)
    s.SolicitudProduccionID,
    s.FolioSolicitud,
    s.NumeroOFRecibida,
    ISNULL(NULLIF(c.Nombre,N''),ISNULL(NULLIF(s.ClienteNombre,N''),N'SIN CLIENTE')) AS Cliente,
    pp.ProgramaProduccionID,
    pp.SolicitudProduccionDetalleID,
    pp.ReleaseDetalleID,
    rd.ReleaseID,
    pp.ClienteID,
    pp.MaquinaID,
    ISNULL(NULLIF(pp.MaquinaCodigo,N''),N'SIN MAQUINA') AS MaquinaCodigo,
    ISNULL(NULLIF(pp.MaquinaNombre,N''),N'') AS MaquinaNombre,
    COALESCE(NULLIF(pp.NumeroParte,N''),NULLIF(pp.ReferenciaSAP,N''),NULLIF(pp.DesignacionDescripcionSAP,N''),N'SIN PARTE') AS Parte,
    CONVERT(int,ISNULL(pp.CantidadProgramada,0)) AS CantidadActual,
    CONVERT(decimal(18,4),ISNULL(pp.HorasProgramadas,0)) AS HorasActuales,
    CONVERT(decimal(18,4),pp.ObjetivoHora) AS ObjetivoHora,
    CONVERT(decimal(18,6),pp.PesoBrutoPieza) AS PesoBrutoPieza,
    CONVERT(decimal(18,4),pp.PiezasPorEmbalaje) AS PiezasPorEmbalaje,
    pp.FechaInicioProgramada,
    pp.FechaFinProgramada,
    CONVERT(bit,ISNULL(pp.TrabajarDomingo,0)) AS TrabajarDomingo,
    ISNULL(ej.EjecucionProduccionID,0) AS EjecucionProduccionID
FROM dbo.Planeacion_ProgramaProduccion pp{lockHint}
INNER JOIN dbo.SolicitudesProduccion s{lockHint}
    ON s.SolicitudProduccionID = pp.SolicitudProduccionID
   AND s.Activo = 1
LEFT JOIN dbo.ERP_Clientes c
    ON c.ClienteID = s.ClienteID
LEFT JOIN dbo.Planeacion_ReleaseDetalle rd
    ON rd.ReleaseDetalleID = pp.ReleaseDetalleID
OUTER APPLY
(
    SELECT TOP(1) e.EjecucionProduccionID
    FROM dbo.Produccion_Ejecucion e{lockHint}
    WHERE e.SolicitudProduccionID = s.SolicitudProduccionID
      AND e.ProgramaProduccionID = pp.ProgramaProduccionID
      AND e.Activo = 1
      AND e.FechaInicioReal IS NOT NULL
      AND e.FechaFinReal IS NULL
    ORDER BY e.EjecucionProduccionID DESC
) ej
WHERE s.SolicitudProduccionID = @SolicitudProduccionID
  AND pp.Activo = 1
  AND pp.FechaInicioProgramada IS NOT NULL
  AND pp.FechaFinProgramada IS NOT NULL
ORDER BY pp.ProgramaProduccionID;";

        var encontrados = new List<PlaneacionExtensionContexto>();
        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = solicitudProduccionId;

        await using var rdSql = await cmd.ExecuteReaderAsync();
        while (await rdSql.ReadAsync())
        {
            encontrados.Add(new PlaneacionExtensionContexto
            {
                SolicitudProduccionID = Convert.ToInt32(rdSql["SolicitudProduccionID"]),
                SolicitudProduccionDetalleID = Convert.ToInt32(rdSql["SolicitudProduccionDetalleID"]),
                ProgramaProduccionID = Convert.ToInt32(rdSql["ProgramaProduccionID"]),
                EjecucionProduccionID = Convert.ToInt32(rdSql["EjecucionProduccionID"]),
                ReleaseID = rdSql["ReleaseID"] == DBNull.Value ? null : Convert.ToInt32(rdSql["ReleaseID"]),
                ReleaseDetalleID = rdSql["ReleaseDetalleID"] == DBNull.Value ? null : Convert.ToInt32(rdSql["ReleaseDetalleID"]),
                ClienteID = rdSql["ClienteID"] == DBNull.Value ? null : Convert.ToInt32(rdSql["ClienteID"]),
                MaquinaID = rdSql["MaquinaID"] == DBNull.Value ? null : Convert.ToInt32(rdSql["MaquinaID"]),
                Folio = (rdSql["NumeroOFRecibida"] as string)?.Trim()
                        ?? (rdSql["FolioSolicitud"] as string)?.Trim()
                        ?? $"OF-{solicitudProduccionId}",
                Cliente = rdSql["Cliente"] as string ?? "SIN CLIENTE",
                Parte = rdSql["Parte"] as string ?? "SIN PARTE",
                Maquina = string.Join(" - ", new[]
                {
                    rdSql["MaquinaCodigo"] as string,
                    rdSql["MaquinaNombre"] as string
                }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct()),
                CantidadActual = Convert.ToInt32(rdSql["CantidadActual"]),
                HorasActuales = Convert.ToDecimal(rdSql["HorasActuales"]),
                ObjetivoHora = rdSql["ObjetivoHora"] == DBNull.Value ? null : Convert.ToDecimal(rdSql["ObjetivoHora"]),
                PesoBrutoPieza = rdSql["PesoBrutoPieza"] == DBNull.Value ? null : Convert.ToDecimal(rdSql["PesoBrutoPieza"]),
                PiezasPorEmbalaje = rdSql["PiezasPorEmbalaje"] == DBNull.Value ? null : Convert.ToDecimal(rdSql["PiezasPorEmbalaje"]),
                FechaInicioProgramada = Convert.ToDateTime(rdSql["FechaInicioProgramada"]),
                FechaFinProgramada = Convert.ToDateTime(rdSql["FechaFinProgramada"]),
                TrabajarDomingo = Convert.ToBoolean(rdSql["TrabajarDomingo"])
            });
        }

        if (encontrados.Count == 0)
            return null;

        if (encontrados.Count > 1)
            throw new InvalidOperationException("La OF tiene mas de un programa activo relacionado. La extension debe aplicarse desde el renglon/programa especifico para no mezclar cantidades.");

        return encontrados[0];
    }

    private static async Task<string?> ObtenerBloqueoLogisticaAsync(
        int solicitudProduccionId,
        SqlConnection cn,
        SqlTransaction? tx)
    {
        const string sql = @"
SELECT TOP(1)
    e.EmbarqueID,
    e.Estatus
FROM dbo.Logistica_EmbarqueDetalle d
INNER JOIN dbo.Logistica_Embarques e
    ON e.EmbarqueID = d.EmbarqueID
   AND e.Activo = 1
WHERE d.SolicitudProduccionID = @SolicitudProduccionID
  AND d.Activo = 1
  AND ISNULL(e.Estatus,N'') <> N'Cancelado'
ORDER BY e.EmbarqueID DESC;";

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = solicitudProduccionId;
        await using var rd = await cmd.ExecuteReaderAsync();

        if (!await rd.ReadAsync())
            return null;

        return $"La OF ya esta relacionada con el embarque {rd["EmbarqueID"]} ({rd["Estatus"]}). Para proteger cajas y despachos de Logistica, ya no se permite extenderla.";
    }

    private static async Task RecalcularAlmacenExtensionAsync(
        int solicitudProduccionId,
        SqlConnection cn,
        SqlTransaction tx)
    {
        const string existeSql = @"
SELECT CASE WHEN OBJECT_ID(N'dbo.sp_AlmacenOF_RecalcularEstatus',N'P') IS NULL THEN 0 ELSE 1 END;";

        await using (var existeCmd = new SqlCommand(existeSql, cn, tx))
        {
            var existe = Convert.ToInt32(await existeCmd.ExecuteScalarAsync());
            if (existe == 0)
            {
                // Las pantallas de Almacen calculan requerimiento desde la OF actualizada.
                // Si el SP no existe en esta instancia, no inventamos un estatus manual.
                return;
            }
        }

        await using var cmd = new SqlCommand("dbo.sp_AlmacenOF_RecalcularEstatus", cn, tx)
        {
            CommandType = CommandType.StoredProcedure
        };

        SqlCommandBuilder.DeriveParameters(cmd);
        var parameter = cmd.Parameters
            .Cast<SqlParameter>()
            .FirstOrDefault(p => p.Direction == ParameterDirection.Input &&
                (string.Equals(p.ParameterName, "@SolicitudProduccionID", StringComparison.OrdinalIgnoreCase) ||
                 p.ParameterName.IndexOf("SolicitudProduccion", StringComparison.OrdinalIgnoreCase) >= 0));

        if (parameter == null)
            throw new InvalidOperationException("Existe sp_AlmacenOF_RecalcularEstatus, pero no se encontro su parametro de SolicitudProduccionID.");

        parameter.Value = solicitudProduccionId;
        await cmd.ExecuteNonQueryAsync();
    }
}