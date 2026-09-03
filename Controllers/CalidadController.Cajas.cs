using ERP.NSQuell.Models;
using ERP.NSQuell.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using ERP.NSQuell.Servicios.Almacen;

namespace ERP.NSQuell.Controllers
{
    public partial class CalidadController
    {
        private const string DecisionCajaLiberar = CalidadDecisionCaja.Liberar;
        private const string DecisionCajaGP12 = CalidadDecisionCaja.GP12;
        private const string DecisionCajaDevolver = CalidadDecisionCaja.Devolver;

        private sealed class CajaProduccionCalidadOrigen
        {
            public long CajaProduccionID { get; set; }
            public int InspeccionID { get; set; }
            public int EjecucionProduccionID { get; set; }
            public int ProgramaProduccionID { get; set; }
            public int? SolicitudProduccionID { get; set; }
            public int? SolicitudProduccionDetalleID { get; set; }
            public int? ReleaseID { get; set; }
            public int? ReleaseDetalleID { get; set; }
            public int? ClienteID { get; set; }
            public string? ClienteNombre { get; set; }
            public int? ParteID { get; set; }
            public string? NumeroParte { get; set; }
            public string? DescripcionParte { get; set; }
            public int? MaterialID { get; set; }
            public string? MaterialCodigo { get; set; }
            public string? MaterialDescripcion { get; set; }
            public string? OrdenFabricacion { get; set; }
            public int NumeroCaja { get; set; }
            public string FolioCaja { get; set; } = string.Empty;
            public int CantidadPiezas { get; set; }
            public string TipoCaja { get; set; } = "OK";
            public string? LoteMaterial { get; set; }
            public string? EtiquetaFolio { get; set; }
            public int EstadoCajaID { get; set; }
            public string EstadoCajaNombre { get; set; } = string.Empty;
            public string? EstatusCalidad { get; set; }
            public DateTime FechaFormacion { get; set; }
            public int? UsuarioFormacionID { get; set; }
            public string EstadoInspeccion { get; set; } = string.Empty;
            public bool ConfiguracionInvalidada { get; set; }
            public int DisposicionesPendientes { get; set; }
            public string? CodigoBarrasOrigen { get; set; }
            public string? NumeroOFEtiqueta { get; set; }
            public string? NumeroParteEtiqueta { get; set; }
            public string? DesignacionEtiqueta { get; set; }
            public int? CantidadEtiqueta { get; set; }
            public string? LoteEtiqueta { get; set; }
            public DateTime? FechaEscaneoProduccion { get; set; }
            public int? UsuarioEscaneoProduccionID { get; set; }
            public DateTime? FechaEscaneoCalidad { get; set; }
            public int? UsuarioEscaneoCalidadID { get; set; }
        }

        private async Task<List<CalidadCajaProduccionItemViewModel>> CargarCajasPendientesCalidadAsync(string? busqueda)
        {
            var lista = new List<CalidadCajaProduccionItemViewModel>();
            const string sql = @"
SELECT
    pc.CajaProduccionID,
    ci.InspeccionID,
    pc.EjecucionProduccionID,
    ISNULL(pc.ProgramaProduccionID,ISNULL(ci.ProgramaProduccionID,0)) AS ProgramaProduccionID,
    ISNULL(pc.NumeroCaja,0) AS NumeroCaja,
    COALESCE(NULLIF(pc.FolioCaja,N''),NULLIF(pc.Etiqueta,N''),CONVERT(NVARCHAR(100),pc.CajaProduccionID)) AS FolioCaja,
    ISNULL(pc.CantidadPiezas,ISNULL(pc.Cantidad,0)) AS CantidadPiezas,
    ISNULL(pc.TipoCaja,N'OK') AS TipoCaja,
    pc.LoteMaterial,
    COALESCE(NULLIF(pc.EtiquetaFolio,N''),NULLIF(pc.Etiqueta,N'')) AS EtiquetaFolio,
    ISNULL(pc.EtiquetaVerde,0) AS EtiquetaVerde,
    ISNULL(pc.EstadoCajaID,1) AS EstadoCajaID,
    ISNULL(pc.EstadoCajaNombre,N'Formada en Producción') AS EstadoCajaNombre,
    ISNULL(pc.FechaFormacion,pc.FechaCreacion) AS FechaFormacion,
    pc.FechaSolicitudCalidad,
    pc.FechaLiberacionCalidad,
    pc.ResultadoCalidad,
    pc.MotivoCalidad,
    ci.OrdenTrabajo,
    ci.ClienteNombre,
    ci.NumeroParte,
    ci.Maquina,
    ci.Molde,
    pc.CodigoBarrasOrigen,
    pc.NumeroOFEtiqueta,
    pc.NumeroParteEtiqueta,
    pc.DesignacionEtiqueta,
    pc.CantidadEtiqueta,
    pc.LoteEtiqueta,
    pc.FechaEscaneoProduccion,
    pc.UsuarioEscaneoProduccionID,
    pc.FechaEscaneoCalidad,
    pc.UsuarioEscaneoCalidadID
FROM dbo.Produccion_Cajas pc
CROSS APPLY
(
    SELECT TOP(1)
        i.InspeccionID,i.ProgramaProduccionID,i.OrdenTrabajo,i.ClienteNombre,i.NumeroParte,i.Maquina,i.Molde
    FROM dbo.Calidad_Inspecciones i
    WHERE i.EjecucionProduccionID=pc.EjecucionProduccionID
      AND ISNULL(i.ConfiguracionInvalidada,0)=0
      AND i.Estado<>N'CERRADA'
    ORDER BY i.InspeccionID DESC
) ci
WHERE pc.Activo=1
  AND ISNULL(pc.EstadoCajaID,1)=@PendienteCalidad
  AND
  (
        @Busqueda IS NULL
     OR pc.FolioCaja LIKE N'%'+@Busqueda+N'%'
     OR pc.Etiqueta LIKE N'%'+@Busqueda+N'%'
     OR pc.EtiquetaFolio LIKE N'%'+@Busqueda+N'%'
     OR pc.CodigoBarrasOrigen LIKE N'%'+@Busqueda+N'%'
     OR pc.NumeroOFEtiqueta LIKE N'%'+@Busqueda+N'%'
     OR pc.NumeroParteEtiqueta LIKE N'%'+@Busqueda+N'%'
     OR pc.LoteMaterial LIKE N'%'+@Busqueda+N'%'
     OR ci.OrdenTrabajo LIKE N'%'+@Busqueda+N'%'
     OR ci.ClienteNombre LIKE N'%'+@Busqueda+N'%'
     OR ci.NumeroParte LIKE N'%'+@Busqueda+N'%'
     OR ci.Maquina LIKE N'%'+@Busqueda+N'%'
     OR ci.Molde LIKE N'%'+@Busqueda+N'%'
  )
ORDER BY
    CASE WHEN pc.FechaEscaneoCalidad IS NULL THEN 0 ELSE 1 END,
    ISNULL(pc.FechaSolicitudCalidad,pc.FechaCreacion),
    pc.CajaProduccionID;";
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@PendienteCalidad", SqlDbType.Int).Value = ProduccionCajaEstatus.PendienteCalidad;
            cmd.Parameters.Add("@Busqueda", SqlDbType.NVarChar, 500).Value = string.IsNullOrWhiteSpace(busqueda) ? DBNull.Value : busqueda.Trim();
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync()) lista.Add(MapearCajaProduccionCalidad(rd));
            return lista;
        }

        private async Task<List<CalidadCajaProduccionItemViewModel>> CargarCajasProduccionInspeccionAsync(int inspeccionId, int? ejecucionProduccionId)
        {
            var lista = new List<CalidadCajaProduccionItemViewModel>();
            if (!ejecucionProduccionId.HasValue || ejecucionProduccionId.Value <= 0) return lista;
            const string sql = @"
SELECT
    pc.CajaProduccionID,
    ci.InspeccionID,
    pc.EjecucionProduccionID,
    ISNULL(pc.ProgramaProduccionID,ISNULL(ci.ProgramaProduccionID,0)) AS ProgramaProduccionID,
    ISNULL(pc.NumeroCaja,0) AS NumeroCaja,
    COALESCE(NULLIF(pc.FolioCaja,N''),NULLIF(pc.Etiqueta,N''),CONVERT(NVARCHAR(100),pc.CajaProduccionID)) AS FolioCaja,
    ISNULL(pc.CantidadPiezas,ISNULL(pc.Cantidad,0)) AS CantidadPiezas,
    ISNULL(pc.TipoCaja,N'OK') AS TipoCaja,
    pc.LoteMaterial,
    COALESCE(NULLIF(pc.EtiquetaFolio,N''),NULLIF(pc.Etiqueta,N'')) AS EtiquetaFolio,
    ISNULL(pc.EtiquetaVerde,0) AS EtiquetaVerde,
    ISNULL(pc.EstadoCajaID,1) AS EstadoCajaID,
    ISNULL(pc.EstadoCajaNombre,N'Formada en Producción') AS EstadoCajaNombre,
    ISNULL(pc.FechaFormacion,pc.FechaCreacion) AS FechaFormacion,
    pc.FechaSolicitudCalidad,
    pc.FechaLiberacionCalidad,
    pc.ResultadoCalidad,
    pc.MotivoCalidad,
    ci.OrdenTrabajo,
    ci.ClienteNombre,
    ci.NumeroParte,
    ci.Maquina,
    ci.Molde,
    pc.CodigoBarrasOrigen,
    pc.NumeroOFEtiqueta,
    pc.NumeroParteEtiqueta,
    pc.DesignacionEtiqueta,
    pc.CantidadEtiqueta,
    pc.LoteEtiqueta,
    pc.FechaEscaneoProduccion,
    pc.UsuarioEscaneoProduccionID,
    pc.FechaEscaneoCalidad,
    pc.UsuarioEscaneoCalidadID
FROM dbo.Produccion_Cajas pc
INNER JOIN dbo.Calidad_Inspecciones ci
    ON ci.InspeccionID=@InspeccionID
   AND ci.EjecucionProduccionID=pc.EjecucionProduccionID
WHERE pc.EjecucionProduccionID=@EjecucionProduccionID
  AND pc.Activo=1
ORDER BY ISNULL(pc.NumeroCaja,0),pc.CajaProduccionID;";
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value = inspeccionId;
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId.Value;
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync()) lista.Add(MapearCajaProduccionCalidad(rd));
            return lista;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResolverCajaProduccion(CalidadCajaDecisionViewModel model)
        {
            var decision = model.Decision?.Trim().ToUpperInvariant();

            if (!ModelState.IsValid || (decision != DecisionCajaLiberar && decision != DecisionCajaGP12 && decision != DecisionCajaDevolver))
            {
                TempData["Error"] = "No se recibió una decisión válida para la caja.";
                return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
            }

            model.Decision = decision ?? string.Empty;
            model.NumeroOperadorEtiqueta = model.NumeroOperadorEtiqueta?.Trim();
            model.Tarima = model.Tarima?.Trim();
            model.MotivoGP12 = model.MotivoGP12?.Trim();
            model.Observaciones = model.Observaciones?.Trim();

            if (decision == DecisionCajaLiberar)
            {
                if (!model.EstandarPackCumple || !model.EtiquetaProductoCorrecta || !model.TecnicoConfirmoInformacion)
                {
                    TempData["Error"] = "Para liberar la caja debes confirmar empaque, etiqueta y validación del técnico.";
                    return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
                }

                if (string.IsNullOrWhiteSpace(model.NumeroOperadorEtiqueta))
                {
                    TempData["Error"] = "Captura el número de operador indicado en la etiqueta.";
                    return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
                }
            }

            if (decision == DecisionCajaGP12 && string.IsNullOrWhiteSpace(model.MotivoGP12))
            {
                TempData["Error"] = "Captura el motivo para enviar la caja a GP12.";
                return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
            }

            if (decision == DecisionCajaDevolver && string.IsNullOrWhiteSpace(model.Observaciones))
            {
                TempData["Error"] = "Captura el motivo para devolver la caja a Producción.";
                return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
            }

            var usuarioId = ObtenerUsuarioIdActual();
            if (!usuarioId.HasValue || usuarioId.Value <= 0) return Unauthorized();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);

            try
            {
                var caja = await ObtenerCajaParaDecisionAsync(model.CajaProduccionID, model.InspeccionID, cn, tx);

                if (caja == null)
                {
                    await tx.RollbackAsync();
                    return NotFound();
                }

                if (caja.EstadoCajaID != ProduccionCajaEstatus.PendienteCalidad)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La caja ya no se encuentra pendiente de revisión de Calidad.";
                    return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
                }

                if (!caja.FechaEscaneoCalidad.HasValue)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "Primero escanea físicamente la etiqueta de la caja antes de tomar una decisión.";
                    return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
                }

                if (caja.ConfiguracionInvalidada)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La configuración de la corrida fue invalidada. No se puede resolver la caja hasta completar la reliberación correspondiente.";
                    return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
                }

                if (EstadoBloqueaRevisionCaja(caja.EstadoInspeccion))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "El proceso de Calidad se encuentra cerrado o bloqueado y ya no permite decisiones sobre cajas.";
                    return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
                }

                if (caja.CantidadPiezas <= 0)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La caja no tiene una cantidad válida de piezas para revisión.";
                    return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
                }

                if (decision == DecisionCajaLiberar && caja.DisposicionesPendientes > 0)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = $"No se puede validar la caja para Almacén mientras existan {caja.DisposicionesPendientes} disposición(es) de material pendientes. Resuélvelas o envía la caja a GP12.";
                    return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
                }

                var ahora = DateTime.Now;

                string resultadoCalidad;
                string? etiquetaLiberacion;
                string? destino;
                string estadoRegistroCalidad;
                int estadoCajaId;
                string estadoCajaNombre;
                string estatusCalidad;
                string comentarioHistorial;

                if (decision == DecisionCajaLiberar)
                {
                    resultadoCalidad = "LIBERADA";
                    etiquetaLiberacion = "VERDE";
                    destino = CalidadDestinoCaja.Almacen;
                    estadoRegistroCalidad = CalidadEstadoCaja.Liberada;
                    estadoCajaId = ProduccionCajaEstatus.LiberadaCalidad;
                    estadoCajaNombre = "Pendiente entrega a Almacén";
                    estatusCalidad = "LIBERADA";
                    comentarioHistorial = $"Caja {caja.FolioCaja} escaneada y validada por Calidad con etiqueta verde. Queda pendiente de entrega física de Producción a Almacén PT.";
                }
                else if (decision == DecisionCajaGP12)
                {
                    resultadoCalidad = "GP12";
                    etiquetaLiberacion = "AMARILLA";
                    destino = CalidadDestinoCaja.GP12;
                    estadoRegistroCalidad = CalidadEstadoCaja.EnGP12;
                    estadoCajaId = ProduccionCajaEstatus.RetenidaGp12Scrap;
                    estadoCajaNombre = "En GP12";
                    estatusCalidad = "GP12";
                    comentarioHistorial = $"Caja {caja.FolioCaja} enviada a GP12. Motivo: {model.MotivoGP12}";
                }
                else
                {
                    resultadoCalidad = "DEVUELTA";
                    etiquetaLiberacion = null;
                    destino = null;
                    estadoRegistroCalidad = CalidadEstadoCaja.Devuelta;
                    estadoCajaId = ProduccionCajaEstatus.FormadaProduccion;
                    estadoCajaNombre = "Devuelta por Calidad";
                    estatusCalidad = "DEVUELTA";
                    comentarioHistorial = $"Caja {caja.FolioCaja} devuelta a Producción. Motivo: {model.Observaciones}";
                }

                var motivoCalidad = decision == DecisionCajaGP12 ? model.MotivoGP12 : model.Observaciones;

                await ActualizarCajaProduccionDecisionAsync(
                    caja.CajaProduccionID,
                    estadoCajaId,
                    estadoCajaNombre,
                    estatusCalidad,
                    resultadoCalidad,
                    motivoCalidad,
                    decision == DecisionCajaLiberar,
                    ahora,
                    usuarioId.Value,
                    cn,
                    tx);

                var cajaLiberadaId = await RegistrarOActualizarCajaCalidadAsync(
                    caja,
                    model,
                    estadoRegistroCalidad,
                    destino,
                    etiquetaLiberacion,
                    ahora,
                    usuarioId.Value,
                    cn,
                    tx);

                if (decision == DecisionCajaGP12)
                {
                    await RegistrarEntradaGP12Async(
                        caja,
                        cajaLiberadaId,
                        model.MotivoGP12,
                        ahora,
                        usuarioId.Value,
                        cn,
                        tx);
                }

                await RegistrarHistorialDecisionCajaAsync(
                    caja,
                    decision,
                    resultadoCalidad,
                    etiquetaLiberacion,
                    comentarioHistorial,
                    usuarioId.Value,
                    ahora,
                    cn,
                    tx);

                await tx.CommitAsync();



                TempData["Mensaje"] = decision switch
                {
                    DecisionCajaLiberar => $"Caja {caja.FolioCaja} validada con etiqueta verde. Producción ya la verá como PENDIENTE ENTREGA A ALMACÉN.",
                    DecisionCajaGP12 => "Caja enviada a GP12.",
                    _ => "Caja devuelta a Producción para corrección."
                };
            }
            catch (Exception ex)
            {
                try { await tx.RollbackAsync(); } catch { }
                TempData["Error"] = "No fue posible resolver la caja: " + ex.Message;
            }

            return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
        }

        private async Task<CajaProduccionCalidadOrigen?> ObtenerCajaParaDecisionAsync(long cajaProduccionId, int inspeccionId, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
SELECT TOP(1)
    pc.CajaProduccionID,
    ci.InspeccionID,
    pc.EjecucionProduccionID,
    ISNULL(pc.ProgramaProduccionID,ISNULL(ci.ProgramaProduccionID,0)) AS ProgramaProduccionID,
    COALESCE(pc.SolicitudProduccionID,ci.SolicitudProduccionID) AS SolicitudProduccionID,
    COALESCE(pc.SolicitudProduccionDetalleID,ci.SolicitudProduccionDetalleID) AS SolicitudProduccionDetalleID,
    COALESCE(pc.ReleaseID,ci.ReleaseID) AS ReleaseID,
    COALESCE(pc.ReleaseDetalleID,ci.ReleaseDetalleID) AS ReleaseDetalleID,
    ci.ClienteID,
    ci.ClienteNombre,
    COALESCE(e.ParteID,ci.ParteID) AS ParteID,
    COALESCE(NULLIF(e.NumeroParte,N''),NULLIF(ci.NumeroParte,N'')) AS NumeroParte,
    COALESCE(NULLIF(d.DesignacionDescripcionSAP,N''),NULLIF(p.Designacion,N''),NULLIF(p.Descripcion,N''),NULLIF(e.DescripcionParte,N''),NULLIF(ci.NumeroParte,N''),N'Sin descripción') AS DescripcionParte,
    COALESCE(d.MaterialID,ci.MaterialID) AS MaterialID,
    COALESCE(NULLIF(d.MaterialCodigo,N''),NULLIF(ci.Material,N'')) AS MaterialCodigo,
    COALESCE(NULLIF(d.MaterialDescripcion,N''),NULLIF(ci.Material,N'')) AS MaterialDescripcion,
    COALESCE(NULLIF(LTRIM(RTRIM(sp.NumeroOFRecibida)),N''),NULLIF(LTRIM(RTRIM(sp.FolioSolicitud)),N''),NULLIF(LTRIM(RTRIM(ci.OrdenTrabajo)),N''),CASE WHEN sp.SolicitudProduccionID IS NOT NULL THEN CONCAT(N'OF-ID-',sp.SolicitudProduccionID) ELSE NULL END) AS OrdenFabricacion,
    ISNULL(pc.NumeroCaja,0) AS NumeroCaja,
    COALESCE(NULLIF(pc.FolioCaja,N''),NULLIF(pc.Etiqueta,N''),CONVERT(NVARCHAR(100),pc.CajaProduccionID)) AS FolioCaja,
    ISNULL(pc.CantidadPiezas,ISNULL(pc.Cantidad,0)) AS CantidadPiezas,
    ISNULL(pc.TipoCaja,N'OK') AS TipoCaja,
    pc.LoteMaterial,
    COALESCE(NULLIF(pc.EtiquetaFolio,N''),NULLIF(pc.Etiqueta,N'')) AS EtiquetaFolio,
    ISNULL(pc.EstadoCajaID,1) AS EstadoCajaID,
    ISNULL(pc.EstadoCajaNombre,N'Formada en Producción') AS EstadoCajaNombre,
    pc.EstatusCalidad,
    ISNULL(pc.FechaFormacion,pc.FechaCreacion) AS FechaFormacion,
    pc.UsuarioFormacionID,
    ci.Estado AS EstadoInspeccion,
    ISNULL(ci.ConfiguracionInvalidada,0) AS ConfiguracionInvalidada,
    (SELECT COUNT(1) FROM dbo.Calidad_DisposicionesMaterial dpm WHERE dpm.InspeccionID=ci.InspeccionID AND dpm.Activo=1 AND UPPER(LTRIM(RTRIM(ISNULL(dpm.ResultadoFinal,N''))))=N'PENDIENTE') AS DisposicionesPendientes,
    pc.CodigoBarrasOrigen,
    pc.NumeroOFEtiqueta,
    pc.NumeroParteEtiqueta,
    pc.DesignacionEtiqueta,
    pc.CantidadEtiqueta,
    pc.LoteEtiqueta,
    pc.FechaEscaneoProduccion,
    pc.UsuarioEscaneoProduccionID,
    pc.FechaEscaneoCalidad,
    pc.UsuarioEscaneoCalidadID
FROM dbo.Produccion_Cajas pc WITH(UPDLOCK,HOLDLOCK)
INNER JOIN dbo.Calidad_Inspecciones ci ON ci.InspeccionID=@InspeccionID AND ci.EjecucionProduccionID=pc.EjecucionProduccionID
LEFT JOIN dbo.Produccion_Ejecucion e ON e.EjecucionProduccionID=pc.EjecucionProduccionID AND e.Activo=1
LEFT JOIN dbo.SolicitudesProduccionDetalle d ON d.SolicitudProduccionDetalleID=COALESCE(pc.SolicitudProduccionDetalleID,ci.SolicitudProduccionDetalleID) AND d.Activo=1
LEFT JOIN dbo.SolicitudesProduccion sp ON sp.SolicitudProduccionID=COALESCE(pc.SolicitudProduccionID,ci.SolicitudProduccionID) AND sp.Activo=1
LEFT JOIN dbo.ERP_Partes p ON p.ParteID=COALESCE(e.ParteID,ci.ParteID)
WHERE pc.CajaProduccionID=@CajaProduccionID AND pc.Activo=1;";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value = cajaProduccionId;
            cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value = inspeccionId;
            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync()) return null;
            return new CajaProduccionCalidadOrigen
            {
                CajaProduccionID = Convert.ToInt64(rd["CajaProduccionID"]),
                InspeccionID = Convert.ToInt32(rd["InspeccionID"]),
                EjecucionProduccionID = Convert.ToInt32(rd["EjecucionProduccionID"]),
                ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),
                SolicitudProduccionID = rd["SolicitudProduccionID"] == DBNull.Value ? null : Convert.ToInt32(rd["SolicitudProduccionID"]),
                SolicitudProduccionDetalleID = rd["SolicitudProduccionDetalleID"] == DBNull.Value ? null : Convert.ToInt32(rd["SolicitudProduccionDetalleID"]),
                ReleaseID = rd["ReleaseID"] == DBNull.Value ? null : Convert.ToInt32(rd["ReleaseID"]),
                ReleaseDetalleID = rd["ReleaseDetalleID"] == DBNull.Value ? null : Convert.ToInt32(rd["ReleaseDetalleID"]),
                ClienteID = rd["ClienteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ClienteID"]),
                ClienteNombre = rd["ClienteNombre"] == DBNull.Value ? null : rd["ClienteNombre"].ToString()?.Trim(),
                ParteID = rd["ParteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ParteID"]),
                NumeroParte = rd["NumeroParte"] == DBNull.Value ? null : rd["NumeroParte"].ToString()?.Trim(),
                DescripcionParte = rd["DescripcionParte"] == DBNull.Value ? null : rd["DescripcionParte"].ToString()?.Trim(),
                MaterialID = rd["MaterialID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaterialID"]),
                MaterialCodigo = rd["MaterialCodigo"] == DBNull.Value ? null : rd["MaterialCodigo"].ToString()?.Trim(),
                MaterialDescripcion = rd["MaterialDescripcion"] == DBNull.Value ? null : rd["MaterialDescripcion"].ToString()?.Trim(),
                OrdenFabricacion = rd["OrdenFabricacion"] == DBNull.Value ? null : rd["OrdenFabricacion"].ToString()?.Trim(),
                NumeroCaja = Convert.ToInt32(rd["NumeroCaja"]),
                FolioCaja = rd["FolioCaja"]?.ToString()?.Trim() ?? string.Empty,
                CantidadPiezas = Convert.ToInt32(rd["CantidadPiezas"]),
                TipoCaja = rd["TipoCaja"]?.ToString()?.Trim() ?? "OK",
                LoteMaterial = rd["LoteMaterial"] == DBNull.Value ? null : rd["LoteMaterial"].ToString()?.Trim(),
                EtiquetaFolio = rd["EtiquetaFolio"] == DBNull.Value ? null : rd["EtiquetaFolio"].ToString()?.Trim(),
                EstadoCajaID = Convert.ToInt32(rd["EstadoCajaID"]),
                EstadoCajaNombre = rd["EstadoCajaNombre"]?.ToString()?.Trim() ?? string.Empty,
                EstatusCalidad = rd["EstatusCalidad"] == DBNull.Value ? null : rd["EstatusCalidad"].ToString()?.Trim(),
                FechaFormacion = Convert.ToDateTime(rd["FechaFormacion"]),
                UsuarioFormacionID = rd["UsuarioFormacionID"] == DBNull.Value ? null : Convert.ToInt32(rd["UsuarioFormacionID"]),
                EstadoInspeccion = rd["EstadoInspeccion"]?.ToString()?.Trim() ?? string.Empty,
                ConfiguracionInvalidada = Convert.ToBoolean(rd["ConfiguracionInvalidada"]),
                DisposicionesPendientes = Convert.ToInt32(rd["DisposicionesPendientes"]),
                CodigoBarrasOrigen = rd["CodigoBarrasOrigen"] == DBNull.Value ? null : rd["CodigoBarrasOrigen"].ToString()?.Trim(),
                NumeroOFEtiqueta = rd["NumeroOFEtiqueta"] == DBNull.Value ? null : rd["NumeroOFEtiqueta"].ToString()?.Trim(),
                NumeroParteEtiqueta = rd["NumeroParteEtiqueta"] == DBNull.Value ? null : rd["NumeroParteEtiqueta"].ToString()?.Trim(),
                DesignacionEtiqueta = rd["DesignacionEtiqueta"] == DBNull.Value ? null : rd["DesignacionEtiqueta"].ToString()?.Trim(),
                CantidadEtiqueta = rd["CantidadEtiqueta"] == DBNull.Value ? null : Convert.ToInt32(rd["CantidadEtiqueta"]),
                LoteEtiqueta = rd["LoteEtiqueta"] == DBNull.Value ? null : rd["LoteEtiqueta"].ToString()?.Trim(),
                FechaEscaneoProduccion = rd["FechaEscaneoProduccion"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaEscaneoProduccion"]),
                UsuarioEscaneoProduccionID = rd["UsuarioEscaneoProduccionID"] == DBNull.Value ? null : Convert.ToInt32(rd["UsuarioEscaneoProduccionID"]),
                FechaEscaneoCalidad = rd["FechaEscaneoCalidad"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaEscaneoCalidad"]),
                UsuarioEscaneoCalidadID = rd["UsuarioEscaneoCalidadID"] == DBNull.Value ? null : Convert.ToInt32(rd["UsuarioEscaneoCalidadID"])
            };
        }

        private static async Task ActualizarCajaProduccionDecisionAsync(
            long cajaProduccionId,
            int estadoCajaId,
            string estadoCajaNombre,
            string estatusCalidad,
            string resultadoCalidad,
            string? motivoCalidad,
            bool etiquetaVerde,
            DateTime ahora,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
UPDATE dbo.Produccion_Cajas
SET
    EstadoCajaID = @EstadoCajaID,
    EstadoCajaNombre = @EstadoCajaNombre,
    EstatusCalidad = @EstatusCalidad,
    EtiquetaVerde = @EtiquetaVerde,
    FechaLiberacionCalidad =
        CASE
            WHEN @EstadoCajaID = 1 THEN NULL
            ELSE @Ahora
        END,
    AuditorCalidadUsuarioID = @UsuarioID,
    UsuarioCalidadID = @UsuarioID,
    ResultadoCalidad = @ResultadoCalidad,
    MotivoCalidad = @MotivoCalidad,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = @Ahora
WHERE CajaProduccionID = @CajaProduccionID
  AND Activo = 1
  AND EstadoCajaID = 2;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value =
                cajaProduccionId;
            cmd.Parameters.Add("@EstadoCajaID", SqlDbType.Int).Value =
                estadoCajaId;
            cmd.Parameters.Add("@EstadoCajaNombre", SqlDbType.NVarChar, 100).Value =
                estadoCajaNombre;
            cmd.Parameters.Add("@EstatusCalidad", SqlDbType.NVarChar, 20).Value =
                estatusCalidad;
            cmd.Parameters.Add("@EtiquetaVerde", SqlDbType.Bit).Value =
                etiquetaVerde;
            cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value =
                ahora;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                usuarioId;
            cmd.Parameters.Add("@ResultadoCalidad", SqlDbType.NVarChar, 30).Value =
                resultadoCalidad;
            cmd.Parameters.Add("@MotivoCalidad", SqlDbType.NVarChar, 500).Value =
                string.IsNullOrWhiteSpace(motivoCalidad)
                    ? DBNull.Value
                    : motivoCalidad.Trim();

            var afectados = await cmd.ExecuteNonQueryAsync();

            if (afectados != 1)
            {
                throw new InvalidOperationException(
                    "La caja cambió de estado mientras se realizaba la revisión.");
            }
        }

        private static async Task<int> RegistrarOActualizarCajaCalidadAsync(
            CajaProduccionCalidadOrigen caja,
            CalidadCajaDecisionViewModel model,
            string estado,
            string? destino,
            string? etiquetaLiberacion,
            DateTime ahora,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sqlExiste = @"
SELECT TOP (1) CajaLiberadaID
FROM dbo.Calidad_CajasLiberadas WITH (UPDLOCK, HOLDLOCK)
WHERE CajaProduccionID = @CajaProduccionID;";

            int? cajaLiberadaId = null;

            await using (var cmd = new SqlCommand(sqlExiste, cn, tx))
            {
                cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value =
                    caja.CajaProduccionID;

                var result = await cmd.ExecuteScalarAsync();

                if (result != null && result != DBNull.Value)
                    cajaLiberadaId = Convert.ToInt32(result);
            }

            var observaciones = model.Decision == DecisionCajaGP12
                ? model.MotivoGP12
                : model.Observaciones;

            if (cajaLiberadaId.HasValue)
            {
                const string sqlUpdate = @"
UPDATE dbo.Calidad_CajasLiberadas
SET
    InspeccionID = @InspeccionID,
    EjecucionProduccionID = @EjecucionProduccionID,
    FolioCaja = @FolioCaja,
    CantidadPiezas = @CantidadPiezas,
    EstandarPackCumple = @EstandarPackCumple,
    EtiquetaProductoCorrecta = @EtiquetaProductoCorrecta,
    NumeroOperadorEtiqueta = @NumeroOperadorEtiqueta,
    TecnicoConfirmoInformacion = @TecnicoConfirmoInformacion,
    FechaCierreProduccion = @FechaCierreProduccion,
    UsuarioCierreProduccionID = @UsuarioCierreProduccionID,
    FechaValidacionCalidad = @Ahora,
    UsuarioValidacionCalidadID = @UsuarioID,
    EtiquetaLiberacion = @EtiquetaLiberacion,
    Tarima = @Tarima,
    Destino = @Destino,
    Estado = @Estado,
    Observaciones = @Observaciones,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = @Ahora,
    Activo = 1
WHERE CajaLiberadaID = @CajaLiberadaID;";

                await using var cmd = new SqlCommand(sqlUpdate, cn, tx);
                AgregarParametrosCajaCalidad(
                    cmd,
                    caja,
                    model,
                    estado,
                    destino,
                    etiquetaLiberacion,
                    observaciones,
                    ahora,
                    usuarioId);
                cmd.Parameters.Add("@CajaLiberadaID", SqlDbType.Int).Value =
                    cajaLiberadaId.Value;

                await cmd.ExecuteNonQueryAsync();
                return cajaLiberadaId.Value;
            }

            const string sqlInsert = @"
INSERT INTO dbo.Calidad_CajasLiberadas
(
    CajaProduccionID,
    InspeccionID,
    EjecucionProduccionID,
    FolioCaja,
    CantidadPiezas,
    EstandarPackCumple,
    EtiquetaProductoCorrecta,
    NumeroOperadorEtiqueta,
    TecnicoConfirmoInformacion,
    FechaCierreProduccion,
    UsuarioCierreProduccionID,
    FechaValidacionCalidad,
    UsuarioValidacionCalidadID,
    EtiquetaLiberacion,
    Tarima,
    Destino,
    Estado,
    Observaciones,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
OUTPUT INSERTED.CajaLiberadaID
VALUES
(
    @CajaProduccionID,
    @InspeccionID,
    @EjecucionProduccionID,
    @FolioCaja,
    @CantidadPiezas,
    @EstandarPackCumple,
    @EtiquetaProductoCorrecta,
    @NumeroOperadorEtiqueta,
    @TecnicoConfirmoInformacion,
    @FechaCierreProduccion,
    @UsuarioCierreProduccionID,
    @Ahora,
    @UsuarioID,
    @EtiquetaLiberacion,
    @Tarima,
    @Destino,
    @Estado,
    @Observaciones,
    @UsuarioID,
    @Ahora,
    1
);";

            await using (var cmd = new SqlCommand(sqlInsert, cn, tx))
            {
                AgregarParametrosCajaCalidad(
                    cmd,
                    caja,
                    model,
                    estado,
                    destino,
                    etiquetaLiberacion,
                    observaciones,
                    ahora,
                    usuarioId);

                var result = await cmd.ExecuteScalarAsync();

                if (result == null || result == DBNull.Value)
                {
                    throw new InvalidOperationException(
                        "No fue posible registrar la revisión de la caja.");
                }

                return Convert.ToInt32(result);
            }
        }

        private static void AgregarParametrosCajaCalidad(
            SqlCommand cmd,
            CajaProduccionCalidadOrigen caja,
            CalidadCajaDecisionViewModel model,
            string estado,
            string? destino,
            string? etiquetaLiberacion,
            string? observaciones,
            DateTime ahora,
            int usuarioId)
        {
            cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value =
                caja.CajaProduccionID;
            cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value =
                caja.InspeccionID;
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value =
                caja.EjecucionProduccionID;
            cmd.Parameters.Add("@FolioCaja", SqlDbType.NVarChar, 100).Value =
                caja.FolioCaja;
            cmd.Parameters.Add("@CantidadPiezas", SqlDbType.Int).Value =
                caja.CantidadPiezas;
            cmd.Parameters.Add("@EstandarPackCumple", SqlDbType.Bit).Value =
                model.EstandarPackCumple;
            cmd.Parameters.Add("@EtiquetaProductoCorrecta", SqlDbType.Bit).Value =
                model.EtiquetaProductoCorrecta;
            cmd.Parameters.Add("@NumeroOperadorEtiqueta", SqlDbType.NVarChar, 50).Value =
                string.IsNullOrWhiteSpace(model.NumeroOperadorEtiqueta)
                    ? DBNull.Value
                    : model.NumeroOperadorEtiqueta.Trim();
            cmd.Parameters.Add("@TecnicoConfirmoInformacion", SqlDbType.Bit).Value =
                model.TecnicoConfirmoInformacion;
            cmd.Parameters.Add("@FechaCierreProduccion", SqlDbType.DateTime2).Value =
                caja.FechaFormacion;
            cmd.Parameters.Add("@UsuarioCierreProduccionID", SqlDbType.Int).Value =
                (object?)caja.UsuarioFormacionID ?? DBNull.Value;
            cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value =
                ahora;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                usuarioId;
            cmd.Parameters.Add("@EtiquetaLiberacion", SqlDbType.NVarChar, 100).Value =
                (object?)etiquetaLiberacion ?? DBNull.Value;
            cmd.Parameters.Add("@Tarima", SqlDbType.NVarChar, 100).Value =
                string.IsNullOrWhiteSpace(model.Tarima)
                    ? DBNull.Value
                    : model.Tarima.Trim();
            cmd.Parameters.Add("@Destino", SqlDbType.VarChar, 20).Value =
                (object?)destino ?? DBNull.Value;
            cmd.Parameters.Add("@Estado", SqlDbType.VarChar, 20).Value =
                estado;
            cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1000).Value =
                string.IsNullOrWhiteSpace(observaciones)
                    ? DBNull.Value
                    : observaciones.Trim();
        }

        private static async Task RegistrarEntradaGP12Async(CajaProduccionCalidadOrigen caja, int cajaLiberadaId, string? motivo, DateTime ahora, int usuarioId, SqlConnection cn, SqlTransaction tx)
        {
            if (caja == null) throw new ArgumentNullException(nameof(caja));
            if (caja.CajaProduccionID <= 0) throw new InvalidOperationException("La caja de Producción no es válida.");
            if (cajaLiberadaId <= 0) throw new InvalidOperationException("La caja registrada por Calidad no es válida.");
            if (caja.CantidadPiezas <= 0) throw new InvalidOperationException("La cantidad enviada a GP12 debe ser mayor que cero.");
            if (string.IsNullOrWhiteSpace(motivo)) throw new InvalidOperationException("Debe indicarse el motivo de envío a GP12.");
            const string sqlExiste = @"
SELECT TOP (1) SolicitudGP12ID
FROM dbo.GP12_Solicitudes WITH (UPDLOCK,HOLDLOCK)
WHERE CajaProduccionID=@CajaProduccionID
  AND Activo=1
  AND EstatusID NOT IN (@Cerrado,@Cancelado)
ORDER BY SolicitudGP12ID DESC;";
            await using (var cmdExiste = new SqlCommand(sqlExiste, cn, tx))
            {
                cmdExiste.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value = caja.CajaProduccionID;
                cmdExiste.Parameters.Add("@Cerrado", SqlDbType.Int).Value = GP12Estatus.Cerrado;
                cmdExiste.Parameters.Add("@Cancelado", SqlDbType.Int).Value = GP12Estatus.Cancelado;
                var existente = await cmdExiste.ExecuteScalarAsync();
                if (existente != null && existente != DBNull.Value) return;
            }
            const string sqlInsert = @"
INSERT INTO dbo.GP12_Solicitudes
(
    Origen,
    ProgramaProduccionID,
    EjecucionProduccionID,
    CalidadInspeccionID,
    CajaProduccionID,
    CajaLiberadaID,
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
    @ProgramaProduccionID,
    @EjecucionProduccionID,
    @CalidadInspeccionID,
    @CajaProduccionID,
    @CajaLiberadaID,
    @SolicitudProduccionID,
    @SolicitudProduccionDetalleID,
    @OrdenFabricacion,
    @ClienteID,
    @ClienteNombre,
    @ParteID,
    @NumeroParte,
    @DescripcionParte,
    @MaterialID,
    @MaterialCodigo,
    @MaterialDescripcion,
    @CantidadSolicitada,
    0,
    0,
    0,
    @Motivo,
    NULL,
    NULL,
    NULL,
    @Observaciones,
    @EstatusID,
    @Ahora,
    @UsuarioID,
    @UsuarioID,
    @Ahora,
    1
);";
            int solicitudGP12Id;
            await using (var cmd = new SqlCommand(sqlInsert, cn, tx))
            {
                cmd.Parameters.Add("@Origen", SqlDbType.NVarChar, 20).Value = GP12Origen.Calidad;
                cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = caja.ProgramaProduccionID;
                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = caja.EjecucionProduccionID;
                cmd.Parameters.Add("@CalidadInspeccionID", SqlDbType.Int).Value = caja.InspeccionID;
                cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value = caja.CajaProduccionID;
                cmd.Parameters.Add("@CajaLiberadaID", SqlDbType.Int).Value = cajaLiberadaId;
                cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = (object?)caja.SolicitudProduccionID ?? DBNull.Value;
                cmd.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value = (object?)caja.SolicitudProduccionDetalleID ?? DBNull.Value;
                cmd.Parameters.Add("@OrdenFabricacion", SqlDbType.NVarChar, 100).Value = string.IsNullOrWhiteSpace(caja.OrdenFabricacion) ? DBNull.Value : caja.OrdenFabricacion.Trim();
                cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = (object?)caja.ClienteID ?? DBNull.Value;
                cmd.Parameters.Add("@ClienteNombre", SqlDbType.NVarChar, 250).Value = string.IsNullOrWhiteSpace(caja.ClienteNombre) ? DBNull.Value : caja.ClienteNombre.Trim();
                cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = (object?)caja.ParteID ?? DBNull.Value;
                cmd.Parameters.Add("@NumeroParte", SqlDbType.NVarChar, 150).Value = string.IsNullOrWhiteSpace(caja.NumeroParte) ? DBNull.Value : caja.NumeroParte.Trim();
                cmd.Parameters.Add("@DescripcionParte", SqlDbType.NVarChar, 500).Value = string.IsNullOrWhiteSpace(caja.DescripcionParte) ? DBNull.Value : caja.DescripcionParte.Trim();
                cmd.Parameters.Add("@MaterialID", SqlDbType.Int).Value = (object?)caja.MaterialID ?? DBNull.Value;
                cmd.Parameters.Add("@MaterialCodigo", SqlDbType.NVarChar, 150).Value = string.IsNullOrWhiteSpace(caja.MaterialCodigo) ? DBNull.Value : caja.MaterialCodigo.Trim();
                cmd.Parameters.Add("@MaterialDescripcion", SqlDbType.NVarChar, 500).Value = string.IsNullOrWhiteSpace(caja.MaterialDescripcion) ? DBNull.Value : caja.MaterialDescripcion.Trim();
                cmd.Parameters.Add("@CantidadSolicitada", SqlDbType.Decimal).Value = Convert.ToDecimal(caja.CantidadPiezas);
                cmd.Parameters["@CantidadSolicitada"].Precision = 18;
                cmd.Parameters["@CantidadSolicitada"].Scale = 4;
                cmd.Parameters.Add("@Motivo", SqlDbType.NVarChar, 1000).Value = motivo.Trim();
                cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 2000).Value = $"Caja {caja.FolioCaja} enviada automáticamente desde Calidad a GP12.";
                cmd.Parameters.Add("@EstatusID", SqlDbType.Int).Value = GP12Estatus.Recibido;
                cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahora;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                var result = await cmd.ExecuteScalarAsync();
                if (result == null || result == DBNull.Value) throw new InvalidOperationException("No fue posible crear la solicitud GP12.");
                solicitudGP12Id = Convert.ToInt32(result);
            }
            const string sqlEtiqueta = @"
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
    @CantidadSolicitada,
    0,
    0,
    @UsuarioID,
    @Ahora,
    1
);";
            await using (var cmd = new SqlCommand(sqlEtiqueta, cn, tx))
            {
                cmd.Parameters.Add("@SolicitudGP12ID", SqlDbType.Int).Value = solicitudGP12Id;
                cmd.Parameters.Add("@TipoEtiqueta", SqlDbType.VarChar, 20).Value = GP12TipoEtiqueta.Amarilla;
                cmd.Parameters.Add("@CantidadSolicitada", SqlDbType.Decimal).Value = Convert.ToDecimal(caja.CantidadPiezas);
                cmd.Parameters["@CantidadSolicitada"].Precision = 18;
                cmd.Parameters["@CantidadSolicitada"].Scale = 4;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahora;
                await cmd.ExecuteNonQueryAsync();
            }
            await RegistrarHistorialSolicitudGP12DesdeCalidadAsync(solicitudGP12Id, caja, motivo, usuarioId, ahora, cn, tx);
        }

        private static async Task RegistrarHistorialSolicitudGP12DesdeCalidadAsync(int solicitudGP12Id, CajaProduccionCalidadOrigen caja, string? motivo, int usuarioId, DateTime ahora, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
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
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@SolicitudGP12ID", SqlDbType.Int).Value = solicitudGP12Id;
            cmd.Parameters.Add("@Movimiento", SqlDbType.NVarChar, 100).Value = GP12Movimientos.SolicitudCreada;
            cmd.Parameters.Add("@EstatusNuevoID", SqlDbType.Int).Value = GP12Estatus.Recibido;
            cmd.Parameters.Add("@Entidad", SqlDbType.NVarChar, 30).Value = GP12EntidadHistorial.Solicitud;
            cmd.Parameters.Add("@EntidadID", SqlDbType.Int).Value = solicitudGP12Id;
            cmd.Parameters.Add("@Comentario", SqlDbType.NVarChar, 2000).Value = $"Solicitud GP12 creada automáticamente desde Calidad. Caja: {caja.FolioCaja}. Cantidad: {caja.CantidadPiezas:N0}. OF: {caja.OrdenFabricacion ?? "Sin OF"}. Motivo: {motivo?.Trim()}";
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahora;
            await cmd.ExecuteNonQueryAsync();
        }
        private static async Task RegistrarHistorialDecisionCajaAsync(
            CajaProduccionCalidadOrigen caja,
            string decision,
            string resultadoCalidad,
            string? etiqueta,
            string comentario,
            int usuarioId,
            DateTime ahora,
            SqlConnection cn,
            SqlTransaction tx)
        {
            var movimiento = decision switch
            {
                DecisionCajaLiberar => "CAJA_LIBERADA",
                DecisionCajaGP12 => "CAJA_ENVIADA_GP12",
                _ => "CAJA_DEVUELTA_PRODUCCION"
            };

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
VALUES
(
    @InspeccionID,
    @Movimiento,
    @EstadoInspeccion,
    @EstadoInspeccion,
    @ResultadoCalidad,
    @Etiqueta,
    @Comentario,
    @UsuarioID,
    @Ahora
);";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value =
                caja.InspeccionID;
            cmd.Parameters.Add("@Movimiento", SqlDbType.NVarChar, 100).Value =
                movimiento;
            cmd.Parameters.Add("@EstadoInspeccion", SqlDbType.NVarChar, 50).Value =
                caja.EstadoInspeccion;
            cmd.Parameters.Add("@ResultadoCalidad", SqlDbType.NVarChar, 30).Value =
                resultadoCalidad;
            cmd.Parameters.Add("@Etiqueta", SqlDbType.NVarChar, 30).Value =
                (object?)etiqueta ?? DBNull.Value;
            cmd.Parameters.Add("@Comentario", SqlDbType.NVarChar, 1000).Value =
                comentario;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                usuarioId;
            cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value =
                ahora;

            await cmd.ExecuteNonQueryAsync();
        }

        private async Task<List<CalidadGP12ItemViewModel>>
    CargarRegistrosGP12Async(
        int inspeccionId)
        {
            var lista =
                new List<CalidadGP12ItemViewModel>();

            if (inspeccionId <= 0)
                return lista;

            const string sql = @"
SELECT
    s.SolicitudGP12ID AS GP12ID,
    s.CalidadInspeccionID AS InspeccionID,
    s.CajaLiberadaID,
    s.CajaProduccionID,

    ISNULL(
        c.FolioCaja,
        N''
    ) AS FolioCaja,

    s.FechaSolicitud AS FechaEntrada,

    CAST(
        ISNULL(
            s.CantidadSolicitada,
            0
        )
        AS INT
    ) AS CantidadEntrada,

    s.Motivo,

    CASE
        WHEN s.EstatusID = 1
            THEN N'RECIBIDO'

        WHEN s.EstatusID = 2
            THEN N'PENDIENTE_PROGRAMAR'

        WHEN s.EstatusID = 3
            THEN N'PROGRAMADO'

        WHEN s.EstatusID = 4
            THEN N'ASIGNADO'

        WHEN s.EstatusID = 5
            THEN N'EN_INSPECCION'

        WHEN s.EstatusID = 6
            THEN N'INSPECCION_PAUSADA'

        WHEN s.EstatusID = 7
            THEN N'INSPECCION_TERMINADA'

        WHEN s.EstatusID = 8
            THEN N'EN_TARIMA'

        WHEN s.EstatusID = 9
            THEN N'SALIDA_REGISTRADA'

        WHEN s.EstatusID = 10
            THEN N'CERRADO'

        ELSE N'DESCONOCIDO'
    END AS Estado,

    s.FechaFin AS FechaSalida,

    CASE
        WHEN s.FechaFin IS NULL
            THEN NULL

        ELSE
            CAST(
                ISNULL(
                    s.CantidadProcesada,
                    0
                )
                AS INT
            )
    END AS CantidadSalida,

    s.Observaciones

FROM dbo.GP12_Solicitudes s

LEFT JOIN dbo.Calidad_CajasLiberadas c
    ON c.CajaLiberadaID =
       s.CajaLiberadaID

   AND c.Activo = 1

WHERE s.CalidadInspeccionID =
      @InspeccionID

  AND UPPER(
        LTRIM(
            RTRIM(
                ISNULL(
                    s.Origen,
                    N''
                )
            )
        )
      ) = N'CALIDAD'

  AND s.Activo = 1

ORDER BY
    s.FechaSolicitud DESC,
    s.SolicitudGP12ID DESC;";


            /*
             * PRIMERA CONEXIÓN:
             * solo obtiene las solicitudes GP12.
             */
            await using (
                var cn =
                    new SqlConnection(
                        ConnectionString))
            {
                await cn.OpenAsync();

                await using (
                    var cmd =
                        new SqlCommand(
                            sql,
                            cn))
                {
                    cmd.Parameters.Add(
                        "@InspeccionID",
                        SqlDbType.Int).Value =
                        inspeccionId;

                    await using (
                        var rd =
                            await cmd.ExecuteReaderAsync())
                    {
                        while (await rd.ReadAsync())
                        {
                            lista.Add(
                                new CalidadGP12ItemViewModel
                                {
                                    GP12ID =
                                        Convert.ToInt32(
                                            rd["GP12ID"]),

                                    InspeccionID =
                                        Convert.ToInt32(
                                            rd["InspeccionID"]),

                                    CajaLiberadaID =
                                        rd["CajaLiberadaID"]
                                            == DBNull.Value
                                            ? 0
                                            : Convert.ToInt32(
                                                rd[
                                                    "CajaLiberadaID"]),

                                    CajaProduccionID =
                                        rd["CajaProduccionID"]
                                            == DBNull.Value
                                            ? null
                                            : Convert.ToInt64(
                                                rd[
                                                    "CajaProduccionID"]),

                                    FolioCaja =
                                        rd["FolioCaja"]
                                            ?.ToString()
                                            ?.Trim()
                                        ?? string.Empty,

                                    FechaEntrada =
                                        Convert.ToDateTime(
                                            rd[
                                                "FechaEntrada"]),

                                    CantidadEntrada =
                                        Convert.ToInt32(
                                            rd[
                                                "CantidadEntrada"]),

                                    Motivo =
                                        rd["Motivo"]
                                            == DBNull.Value
                                            ? null
                                            : rd["Motivo"]
                                                ?.ToString()
                                                ?.Trim(),

                                    Estado =
                                        rd["Estado"]
                                            ?.ToString()
                                            ?.Trim()
                                        ?? string.Empty,

                                    FechaSalida =
                                        rd["FechaSalida"]
                                            == DBNull.Value
                                            ? null
                                            : Convert.ToDateTime(
                                                rd[
                                                    "FechaSalida"]),

                                    CantidadSalida =
                                        rd["CantidadSalida"]
                                            == DBNull.Value
                                            ? null
                                            : Convert.ToInt32(
                                                rd[
                                                    "CantidadSalida"]),

                                    Observaciones =
                                        rd["Observaciones"]
                                            == DBNull.Value
                                            ? null
                                            : rd[
                                                "Observaciones"]
                                                ?.ToString()
                                                ?.Trim(),

                                    Revisiones =
                                        new List<
                                            CalidadGP12RevisionItemViewModel>()
                                });
                        }
                    }
                }
            }


            /*
             * Aquí la conexión anterior YA FUE CERRADA.
             *
             * Cada carga de revisiones utilizará
             * una conexión independiente.
             */
            foreach (var gp12 in lista)
            {
                gp12.Revisiones =
                    await CargarRevisionesGP12Async(
                        gp12.GP12ID);
            }


            return lista;
        }


        private async Task<
       List<CalidadGP12RevisionItemViewModel>>
       CargarRevisionesGP12Async(
           int solicitudGP12Id)
        {
            var lista =
                new List<
                    CalidadGP12RevisionItemViewModel>();

            if (solicitudGP12Id <= 0)
                return lista;

            const string sql = @"
SELECT
    i.InspeccionGP12ID
        AS RevisionGP12ID,

    ROW_NUMBER() OVER
    (
        ORDER BY
            ISNULL(
                i.FechaInicio,
                i.FechaCreacion
            ),
            i.InspeccionGP12ID
    ) AS NumeroRevision,

    ISNULL(
        i.FechaFin,
        ISNULL(
            i.FechaInicio,
            i.FechaCreacion
        )
    ) AS FechaRevision,

    CAST(
        ISNULL(
            i.CantidadRevisada,
            0
        )
        AS INT
    ) AS CantidadRevisada,

    CAST(
        ISNULL(
            i.CantidadOK,
            0
        )
        AS INT
    ) AS CantidadOK,

    CAST(
        ISNULL(
            i.CantidadNOK,
            0
        )
        AS INT
    ) AS CantidadNOK,

    CASE
        WHEN i.FechaFin IS NULL
            THEN N'PENDIENTE'

        WHEN ISNULL(
                i.CantidadNOK,
                0
             ) = 0

         AND ISNULL(
                i.CantidadScrap,
                0
             ) = 0

            THEN N'OK'

        ELSE N'NOK'
    END AS Resultado,

    i.Observaciones

FROM dbo.GP12_Inspecciones i

WHERE i.SolicitudGP12ID =
      @SolicitudGP12ID

  AND i.Activo = 1

ORDER BY
    ISNULL(
        i.FechaInicio,
        i.FechaCreacion
    ) DESC,

    i.InspeccionGP12ID DESC;";


            /*
             * CONEXIÓN PROPIA DE REVISIONES.
             */
            await using (
                var cn =
                    new SqlConnection(
                        ConnectionString))
            {
                await cn.OpenAsync();

                await using (
                    var cmd =
                        new SqlCommand(
                            sql,
                            cn))
                {
                    cmd.Parameters.Add(
                        "@SolicitudGP12ID",
                        SqlDbType.Int).Value =
                        solicitudGP12Id;

                    await using (
                        var rd =
                            await cmd.ExecuteReaderAsync())
                    {
                        while (await rd.ReadAsync())
                        {
                            lista.Add(
                                new CalidadGP12RevisionItemViewModel
                                {
                                    RevisionGP12ID =
                                        Convert.ToInt32(
                                            rd[
                                                "RevisionGP12ID"]),

                                    NumeroRevision =
                                        Convert.ToInt32(
                                            rd[
                                                "NumeroRevision"]),

                                    FechaRevision =
                                        Convert.ToDateTime(
                                            rd[
                                                "FechaRevision"]),

                                    CantidadRevisada =
                                        Convert.ToInt32(
                                            rd[
                                                "CantidadRevisada"]),

                                    CantidadOK =
                                        Convert.ToInt32(
                                            rd[
                                                "CantidadOK"]),

                                    CantidadNOK =
                                        Convert.ToInt32(
                                            rd[
                                                "CantidadNOK"]),

                                    Resultado =
                                        rd["Resultado"]
                                            ?.ToString()
                                            ?.Trim()
                                        ?? string.Empty,

                                    Observaciones =
                                        rd["Observaciones"]
                                            == DBNull.Value
                                            ? null
                                            : rd[
                                                "Observaciones"]
                                                ?.ToString()
                                                ?.Trim(),

                                    Defectos =
                                        new List<
                                            CalidadGP12DefectoItemViewModel>()
                                });
                        }
                    }
                }
            }


            /*
             * El reader y conexión anteriores
             * ya están completamente cerrados.
             */
            foreach (var revision in lista)
            {
                revision.Defectos =
                    await CargarDefectosRevisionGP12Async(
                        revision.RevisionGP12ID);
            }


            return lista;
        }

        private async Task<
     List<CalidadGP12DefectoItemViewModel>>
     CargarDefectosRevisionGP12Async(
         int inspeccionGP12Id)
        {
            var lista =
                new List<
                    CalidadGP12DefectoItemViewModel>();

            if (inspeccionGP12Id <= 0)
                return lista;

            const string sql = @"
SELECT
    d.InspeccionDefectoID
        AS DefectoGP12ID,

    d.DefectoID
        AS CatalogoDefectoID,

    ISNULL(
        c.Codigo,
        N''
    ) AS Codigo,

    ISNULL(
        c.Nombre,
        N''
    ) AS Nombre,

    CAST(
        ISNULL(
            d.Cantidad,
            0
        )
        AS INT
    ) AS Cantidad,

    d.Observaciones

FROM dbo.GP12_InspeccionDefectos d

INNER JOIN dbo.GP12_CatalogoDefectos c
    ON c.DefectoID =
       d.DefectoID

WHERE d.InspeccionGP12ID =
      @InspeccionGP12ID

  AND d.Activo = 1

ORDER BY
    c.Orden,
    c.Codigo,
    d.InspeccionDefectoID;";


            await using (
                var cn =
                    new SqlConnection(
                        ConnectionString))
            {
                await cn.OpenAsync();

                await using (
                    var cmd =
                        new SqlCommand(
                            sql,
                            cn))
                {
                    cmd.Parameters.Add(
                        "@InspeccionGP12ID",
                        SqlDbType.Int).Value =
                        inspeccionGP12Id;

                    await using (
                        var rd =
                            await cmd.ExecuteReaderAsync())
                    {
                        while (await rd.ReadAsync())
                        {
                            lista.Add(
                                new CalidadGP12DefectoItemViewModel
                                {
                                    DefectoGP12ID =
                                        Convert.ToInt32(
                                            rd[
                                                "DefectoGP12ID"]),

                                    CatalogoDefectoID =
                                        Convert.ToInt32(
                                            rd[
                                                "CatalogoDefectoID"]),

                                    Codigo =
                                        rd["Codigo"]
                                            ?.ToString()
                                            ?.Trim()
                                        ?? string.Empty,

                                    Nombre =
                                        rd["Nombre"]
                                            ?.ToString()
                                            ?.Trim()
                                        ?? string.Empty,

                                    Cantidad =
                                        Convert.ToInt32(
                                            rd[
                                                "Cantidad"]),

                                    Observaciones =
                                        rd["Observaciones"]
                                            == DBNull.Value
                                            ? null
                                            : rd[
                                                "Observaciones"]
                                                ?.ToString()
                                                ?.Trim()
                                });
                        }
                    }
                }
            }

            return lista;
        }
        private static bool EstadoBloqueaRevisionCaja(string? estado)
        {
            if (string.IsNullOrWhiteSpace(estado))
                return false;

            var valor = estado.Trim().ToUpperInvariant();

            return valor == CalidadEstados.Cerrada ||
                   valor == CalidadEstados.LegacyDetenida ||
                   valor == CalidadEstados.LegacyScrap;
        }

        private static CalidadCajaProduccionItemViewModel MapearCajaProduccionCalidad(SqlDataReader rd)
        {
            return new CalidadCajaProduccionItemViewModel
            {
                CajaProduccionID = Convert.ToInt64(rd["CajaProduccionID"]),
                InspeccionID = Convert.ToInt32(rd["InspeccionID"]),
                EjecucionProduccionID = Convert.ToInt32(rd["EjecucionProduccionID"]),
                ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),
                NumeroCaja = Convert.ToInt32(rd["NumeroCaja"]),
                FolioCaja = rd["FolioCaja"]?.ToString()?.Trim() ?? string.Empty,
                CantidadPiezas = Convert.ToInt32(rd["CantidadPiezas"]),
                TipoCaja = rd["TipoCaja"]?.ToString()?.Trim() ?? "OK",
                LoteMaterial = LeerTextoCaja(rd, "LoteMaterial"),
                EtiquetaFolio = LeerTextoCaja(rd, "EtiquetaFolio"),
                EtiquetaVerde = Convert.ToBoolean(rd["EtiquetaVerde"]),
                EstadoCajaID = Convert.ToInt32(rd["EstadoCajaID"]),
                EstadoCajaNombre = rd["EstadoCajaNombre"]?.ToString()?.Trim() ?? string.Empty,
                FechaFormacion = Convert.ToDateTime(rd["FechaFormacion"]),
                FechaSolicitudCalidad = LeerFechaNullableCaja(rd, "FechaSolicitudCalidad"),
                FechaLiberacionCalidad = LeerFechaNullableCaja(rd, "FechaLiberacionCalidad"),
                ResultadoCalidad = LeerTextoCaja(rd, "ResultadoCalidad"),
                MotivoCalidad = LeerTextoCaja(rd, "MotivoCalidad"),
                OrdenTrabajo = LeerTextoCaja(rd, "OrdenTrabajo"),
                ClienteNombre = LeerTextoCaja(rd, "ClienteNombre"),
                NumeroParte = LeerTextoCaja(rd, "NumeroParte"),
                Maquina = LeerTextoCaja(rd, "Maquina"),
                Molde = LeerTextoCaja(rd, "Molde"),
                CodigoBarrasOrigen = LeerTextoCaja(rd, "CodigoBarrasOrigen"),
                NumeroOFEtiqueta = LeerTextoCaja(rd, "NumeroOFEtiqueta"),
                NumeroParteEtiqueta = LeerTextoCaja(rd, "NumeroParteEtiqueta"),
                CantidadEtiqueta = LeerEnteroNullableCaja(rd, "CantidadEtiqueta"),
                FechaEscaneoProduccion = LeerFechaNullableCaja(rd, "FechaEscaneoProduccion"),
                FechaEscaneoCalidad = LeerFechaNullableCaja(rd, "FechaEscaneoCalidad"),
                UsuarioEscaneoCalidadID = LeerEnteroNullableCaja(rd, "UsuarioEscaneoCalidadID")
            };
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EscanearCajaCalidad(CalidadCajaEscaneoViewModel model)
        {
            var codigo = model.CodigoBarras?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(codigo))
            {
                TempData["Error"] = "Escanea la etiqueta física colocada por Planeación.";
                return model.InspeccionID.HasValue
                    ? RedirectToAction(nameof(Detalle), new { id = model.InspeccionID.Value })
                    : RedirectToAction(nameof(Index), new { grupo = "CAJAS" });
            }

            if (codigo.Length > 500)
            {
                TempData["Error"] = "El código escaneado excede la longitud permitida.";
                return model.InspeccionID.HasValue
                    ? RedirectToAction(nameof(Detalle), new { id = model.InspeccionID.Value })
                    : RedirectToAction(nameof(Index), new { grupo = "CAJAS" });
            }

            if (!AlmacenPTCodigoBarrasService.TryParse(codigo, out var parseado, out var error) || parseado == null)
            {
                TempData["Error"] = string.IsNullOrWhiteSpace(error)
                    ? "No fue posible interpretar la etiqueta colocada por Planeación."
                    : error;

                return model.InspeccionID.HasValue
                    ? RedirectToAction(nameof(Detalle), new { id = model.InspeccionID.Value })
                    : RedirectToAction(nameof(Index), new { grupo = "CAJAS" });
            }

            if (parseado.Cantidad <= 0)
            {
                TempData["Error"] = "La etiqueta no contiene una cantidad válida.";
                return model.InspeccionID.HasValue
                    ? RedirectToAction(nameof(Detalle), new { id = model.InspeccionID.Value })
                    : RedirectToAction(nameof(Index), new { grupo = "CAJAS" });
            }

            var numeroOF = parseado.NumeroOF?.Trim();
            var numeroParte = parseado.NumeroParte?.Trim();

            if (string.IsNullOrWhiteSpace(numeroOF))
            {
                TempData["Error"] = "La etiqueta no contiene una Orden de Fabricación válida.";
                return model.InspeccionID.HasValue
                    ? RedirectToAction(nameof(Detalle), new { id = model.InspeccionID.Value })
                    : RedirectToAction(nameof(Index), new { grupo = "CAJAS" });
            }

            if (string.IsNullOrWhiteSpace(numeroParte))
            {
                TempData["Error"] = "La etiqueta no contiene un número de parte válido.";
                return model.InspeccionID.HasValue
                    ? RedirectToAction(nameof(Detalle), new { id = model.InspeccionID.Value })
                    : RedirectToAction(nameof(Index), new { grupo = "CAJAS" });
            }

                        // NSQ_CALIDAD_CANONICAL_KEYS_V1_3
            // Una misma OF/parte puede venir con formato diferente en etiqueta y ERP.
            // Para relacionar la corrida se usa una clave alfanumerica estable.
            static string ClaveAlfanumerica(string? valor) =>
                new string(
                    (valor ?? string.Empty)
                        .Where(char.IsLetterOrDigit)
                        .Select(char.ToUpperInvariant)
                        .ToArray());

            // NSQ_CALIDAD_OF_CANONICA_V1
            var numeroOFClave =
                AlmacenPTCodigoBarrasService.ObtenerClaveNumeroOF(
                    numeroOF);

            var numeroParteClave =
                ClaveAlfanumerica(numeroParte);

            if (string.IsNullOrWhiteSpace(numeroOFClave) ||
                string.IsNullOrWhiteSpace(numeroParteClave))
            {
                TempData["Error"] =
                    "La etiqueta no contiene una OF y número de parte comparables con Producción.";

                return model.InspeccionID.HasValue
                    ? RedirectToAction(nameof(Detalle), new { id = model.InspeccionID.Value })
                    : RedirectToAction(nameof(Index), new { grupo = "CAJAS" });
            }

            var codigoFisico = parseado.CodigoOriginal?.Trim();

            if (string.IsNullOrWhiteSpace(codigoFisico))
                codigoFisico = codigo;

            if (codigoFisico.Length > 500)
            {
                TempData["Error"] = "El código físico de la etiqueta excede la longitud permitida.";
                return model.InspeccionID.HasValue
                    ? RedirectToAction(nameof(Detalle), new { id = model.InspeccionID.Value })
                    : RedirectToAction(nameof(Index), new { grupo = "CAJAS" });
            }

            var usuarioId = ObtenerUsuarioIdActual();

            if (!usuarioId.HasValue || usuarioId.Value <= 0)
                return Unauthorized();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var tx =
                (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);

            try
            {
               
                 
                const string sqlExistente = @"
SELECT TOP(1)
    pc.CajaProduccionID,
    pc.EjecucionProduccionID,
    ISNULL(pc.EstadoCajaID,1) AS EstadoCajaID,
    ISNULL(pc.EstadoCajaNombre,N'') AS EstadoCajaNombre,
    pc.FechaEscaneoCalidad,
    ci.InspeccionID,
    ci.Estado AS EstadoInspeccion,
    ISNULL(ci.ConfiguracionInvalidada,0) AS ConfiguracionInvalidada
FROM dbo.Produccion_Cajas pc WITH(UPDLOCK,HOLDLOCK)
OUTER APPLY
(
    SELECT TOP(1)
        i.InspeccionID,
        i.Estado,
        i.ConfiguracionInvalidada
    FROM dbo.Calidad_Inspecciones i WITH(UPDLOCK,HOLDLOCK)
    WHERE i.EjecucionProduccionID=pc.EjecucionProduccionID
      AND i.Estado<>N'CERRADA'
    ORDER BY
        ISNULL(i.ConfiguracionInvalidada,0),
        i.InspeccionID DESC
) ci
WHERE pc.Activo=1
  AND pc.CodigoBarrasOrigen=@CodigoBarras
ORDER BY pc.CajaProduccionID DESC;";

                long? cajaExistenteId = null;
                int? inspeccionExistenteId = null;
                int estadoCajaExistente = 0;
                string estadoCajaNombreExistente = string.Empty;
                DateTime? fechaEscaneoCalidadExistente = null;
                string estadoInspeccionExistente = string.Empty;
                bool configuracionInvalidadaExistente = false;

                await using (var cmd = new SqlCommand(sqlExistente, cn, tx))
                {
                    cmd.Parameters.Add("@CodigoBarras", SqlDbType.NVarChar, 500).Value =
                        codigoFisico;

                    await using var rd = await cmd.ExecuteReaderAsync();

                    if (await rd.ReadAsync())
                    {
                        cajaExistenteId =
                            Convert.ToInt64(rd["CajaProduccionID"]);

                        estadoCajaExistente =
                            Convert.ToInt32(rd["EstadoCajaID"]);

                        estadoCajaNombreExistente =
                            rd["EstadoCajaNombre"]?.ToString()?.Trim()
                            ?? string.Empty;

                        fechaEscaneoCalidadExistente =
                            rd["FechaEscaneoCalidad"] == DBNull.Value
                                ? null
                                : Convert.ToDateTime(rd["FechaEscaneoCalidad"]);

                        inspeccionExistenteId =
                            rd["InspeccionID"] == DBNull.Value
                                ? null
                                : Convert.ToInt32(rd["InspeccionID"]);

                        estadoInspeccionExistente =
                            rd["EstadoInspeccion"] == DBNull.Value
                                ? string.Empty
                                : rd["EstadoInspeccion"]?.ToString()?.Trim()
                                  ?? string.Empty;

                        configuracionInvalidadaExistente =
                            rd["ConfiguracionInvalidada"] != DBNull.Value &&
                            Convert.ToBoolean(rd["ConfiguracionInvalidada"]);
                    }
                }

                if (cajaExistenteId.HasValue)
                {
                    if (!inspeccionExistenteId.HasValue)
                    {
                        await tx.RollbackAsync();

                        TempData["Error"] =
                            "La etiqueta ya pertenece a una caja, pero la corrida no tiene una inspección activa de Calidad.";

                        return model.InspeccionID.HasValue
                            ? RedirectToAction(nameof(Detalle), new { id = model.InspeccionID.Value })
                            : RedirectToAction(nameof(Index), new { grupo = "CAJAS" });
                    }

                    if (model.InspeccionID.HasValue &&
                        model.InspeccionID.Value > 0 &&
                        model.InspeccionID.Value != inspeccionExistenteId.Value)
                    {
                        await tx.RollbackAsync();

                        TempData["Error"] =
                            "La etiqueta ya pertenece a una caja de otra inspección.";

                        TempData["InspeccionCajaEscaneadaID"] =
                            inspeccionExistenteId.Value;

                        return RedirectToAction(
                            nameof(Detalle),
                            new { id = model.InspeccionID.Value });
                    }

                    if (estadoCajaExistente != ProduccionCajaEstatus.PendienteCalidad)
                    {
                        await tx.RollbackAsync();

                        TempData["Mensaje"] =
                            $"La etiqueta ya fue registrada y la caja se encuentra en estado: " +
                            $"{(string.IsNullOrWhiteSpace(estadoCajaNombreExistente) ? ProduccionCajaEstatus.Nombre(estadoCajaExistente) : estadoCajaNombreExistente)}.";

                        return RedirectToAction(
                            nameof(Detalle),
                            new { id = inspeccionExistenteId.Value });
                    }

                    if (fechaEscaneoCalidadExistente.HasValue)
                    {
                        await tx.RollbackAsync();

                        TempData["Mensaje"] =
                            $"Esta caja ya fue escaneada por Calidad el " +
                            $"{fechaEscaneoCalidadExistente.Value:dd/MM/yyyy HH:mm}. " +
                            $"Está pendiente de decisión.";

                        return RedirectToAction(
                            nameof(Detalle),
                            new { id = inspeccionExistenteId.Value });
                    }

                    if (configuracionInvalidadaExistente)
                    {
                        await tx.RollbackAsync();

                        TempData["Error"] =
                            "La configuración de la corrida fue invalidada. La caja no puede revisarse hasta completar la reliberación correspondiente.";

                        return RedirectToAction(
                            nameof(Detalle),
                            new { id = inspeccionExistenteId.Value });
                    }

                    if (EstadoBloqueaRevisionCaja(estadoInspeccionExistente))
                    {
                        await tx.RollbackAsync();

                        TempData["Error"] =
                            "La inspección de Calidad está cerrada o bloqueada y no permite recibir cajas.";

                        return RedirectToAction(
                            nameof(Detalle),
                            new { id = inspeccionExistenteId.Value });
                    }

                    var ahoraExistente = DateTime.Now;

                    const string sqlActualizarExistente = @"
UPDATE dbo.Produccion_Cajas
SET
    NumeroOFEtiqueta=COALESCE(NULLIF(NumeroOFEtiqueta,N''),@NumeroOF),
    NumeroParteEtiqueta=COALESCE(NULLIF(NumeroParteEtiqueta,N''),@NumeroParte),
    DesignacionEtiqueta=COALESCE(NULLIF(DesignacionEtiqueta,N''),@Designacion),
    CantidadEtiqueta=COALESCE(CantidadEtiqueta,@Cantidad),
    LoteEtiqueta=COALESCE(NULLIF(LoteEtiqueta,N''),@Lote),
    LoteMaterial=COALESCE(NULLIF(LoteMaterial,N''),@Lote),
    FechaEscaneoCalidad=@Ahora,
    UsuarioEscaneoCalidadID=@UsuarioID,
    EstadoCajaNombre=N'Escaneada por Calidad - pendiente validación',
    EstatusCalidad=N'PENDIENTE' /* NSQ_CALIDAD_ESTATUS_V1_4_1_SQL */,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=@Ahora
WHERE CajaProduccionID=@CajaProduccionID
  AND Activo=1
  AND EstadoCajaID=@PendienteCalidad
  AND FechaEscaneoCalidad IS NULL;

IF @@ROWCOUNT<>1
    THROW 51401,'La caja cambió de estado mientras Calidad realizaba el escaneo.',1;

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
    N'CAJA_ESCANEADA_CALIDAD',
    @Estado,
    @Estado,
    NULL,
    NULL,
    @Comentario,
    @UsuarioID,
    @Ahora
);";

                    await using (var cmd =
                        new SqlCommand(sqlActualizarExistente, cn, tx))
                    {
                        cmd.Parameters.Add(
                            "@CajaProduccionID",
                            SqlDbType.BigInt).Value =
                            cajaExistenteId.Value;

                        cmd.Parameters.Add(
                            "@PendienteCalidad",
                            SqlDbType.Int).Value =
                            ProduccionCajaEstatus.PendienteCalidad;

                        cmd.Parameters.Add(
                            "@NumeroOF",
                            SqlDbType.NVarChar,
                            120).Value =
                            numeroOF;

                        cmd.Parameters.Add(
                            "@NumeroParte",
                            SqlDbType.NVarChar,
                            150).Value =
                            numeroParte;

                        cmd.Parameters.Add(
                            "@Designacion",
                            SqlDbType.NVarChar,
                            300).Value =
                            string.IsNullOrWhiteSpace(parseado.Designacion)
                                ? DBNull.Value
                                : parseado.Designacion.Trim();

                        cmd.Parameters.Add(
                            "@Cantidad",
                            SqlDbType.Int).Value =
                            parseado.Cantidad;

                        cmd.Parameters.Add(
                            "@Lote",
                            SqlDbType.NVarChar,
                            150).Value =
                            string.IsNullOrWhiteSpace(parseado.Lote)
                                ? DBNull.Value
                                : parseado.Lote.Trim();

                        cmd.Parameters.Add(
                            "@Ahora",
                            SqlDbType.DateTime2).Value =
                            ahoraExistente;

                        cmd.Parameters.Add(
                            "@UsuarioID",
                            SqlDbType.Int).Value =
                            usuarioId.Value;

                        cmd.Parameters.Add(
                            "@InspeccionID",
                            SqlDbType.Int).Value =
                            inspeccionExistenteId.Value;

                        cmd.Parameters.Add(
                            "@Estado",
                            SqlDbType.NVarChar,
                            50).Value =
                            estadoInspeccionExistente;

                        cmd.Parameters.Add(
                            "@Comentario",
                            SqlDbType.NVarChar,
                            1000).Value =
                            $"Calidad realizó el primer escaneo físico de una caja existente. " +
                            $"OF: {numeroOF}. Parte: {numeroParte}. Cantidad: {parseado.Cantidad:N0}.";
                        await cmd.ExecuteNonQueryAsync();
                    }

                    await AsegurarTrazabilidadHorariaCajaAsync(cajaExistenteId.Value, usuarioId.Value, cn, tx);
                    await tx.CommitAsync();

                    TempData["Mensaje"] =
                        $"Caja escaneada correctamente. OF: {numeroOF}. " +
                        $"Parte: {numeroParte}. Cantidad: {parseado.Cantidad:N0}. " +
                        $"Ahora Calidad puede validarla.";

                    return RedirectToAction(
                        nameof(Detalle),
                        new { id = inspeccionExistenteId.Value });
                }

                const string sqlContexto = @"
;WITH Candidatos AS
(
    SELECT
        i.InspeccionID,
        i.EjecucionProduccionID,
        ISNULL(e.ProgramaProduccionID,ISNULL(i.ProgramaProduccionID,0)) AS ProgramaProduccionID,
        COALESCE(e.SolicitudProduccionID,i.SolicitudProduccionID) AS SolicitudProduccionID,
        COALESCE(e.SolicitudProduccionDetalleID,i.SolicitudProduccionDetalleID) AS SolicitudProduccionDetalleID,
        COALESCE(e.ReleaseID,i.ReleaseID) AS ReleaseID,
        COALESCE(e.ReleaseDetalleID,i.ReleaseDetalleID) AS ReleaseDetalleID,
        ISNULL(e.CantidadPlaneada,0) AS CantidadPlaneada,
        ISNULL(e.CantidadOKTotal,0) AS CantidadOKTotal,
        d.PiezasPorEmbalaje,
        d.CantidadEmbalajes,
        i.Estado AS EstadoInspeccion,
        ISNULL(i.ConfiguracionInvalidada,0) AS ConfiguracionInvalidada,
        COALESCE
        (
            NULLIF(LTRIM(RTRIM(sp.NumeroOFRecibida)),N''),
            NULLIF(LTRIM(RTRIM(sp.FolioSolicitud)),N''),
            NULLIF(LTRIM(RTRIM(i.OrdenTrabajo)),N'')
        ) AS OrdenFabricacion,
        COALESCE
        (
            NULLIF(LTRIM(RTRIM(e.ReferenciaSAP)),N''),
            NULLIF(LTRIM(RTRIM(e.NumeroParte)),N''),
            NULLIF(LTRIM(RTRIM(i.NumeroParte)),N'')
        ) AS NumeroParteEsperado,
        ROW_NUMBER() OVER
        (
            PARTITION BY i.EjecucionProduccionID
            ORDER BY
                ISNULL(i.ConfiguracionInvalidada,0),
                i.InspeccionID DESC
        ) AS rn
    FROM dbo.Calidad_Inspecciones i WITH(UPDLOCK,HOLDLOCK)
    INNER JOIN dbo.Produccion_Ejecucion e WITH(UPDLOCK,HOLDLOCK)
        ON e.EjecucionProduccionID=i.EjecucionProduccionID
       -- NSQ_CALIDAD_CAJA_INSPECCION_ACTIVA_V1_1
       -- Calidad mantiene la corrida vigente aunque la ejecucion
       -- historica de Produccion haya quedado Activo=0.
    LEFT JOIN dbo.SolicitudesProduccion sp
        ON sp.SolicitudProduccionID=
           COALESCE(e.SolicitudProduccionID,i.SolicitudProduccionID)
       AND sp.Activo=1
    LEFT JOIN dbo.SolicitudesProduccionDetalle d
        ON d.SolicitudProduccionDetalleID=
           COALESCE(e.SolicitudProduccionDetalleID,i.SolicitudProduccionDetalleID)
       AND d.Activo=1
        WHERE i.Estado<>N'CERRADA'
      -- NSQ_CALIDAD_CANONICAL_MATCH_V1_3
      AND
      (
          REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
              UPPER(LTRIM(RTRIM(ISNULL(sp.NumeroOFRecibida,N'')))),
              N'OF',N''),N'-',N''),N'/',N''),N'''',N''),N' ',N''),N'.',N'')=@NumeroOF
          OR
          REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
              UPPER(LTRIM(RTRIM(ISNULL(sp.FolioSolicitud,N'')))),
              N'OF',N''),N'-',N''),N'/',N''),N'''',N''),N' ',N''),N'.',N'')=@NumeroOF
          OR
          REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
              UPPER(LTRIM(RTRIM(ISNULL(i.OrdenTrabajo,N'')))),
              N'OF',N''),N'-',N''),N'/',N''),N'''',N''),N' ',N''),N'.',N'')=@NumeroOF
      )
      AND
      (
          REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
              UPPER(LTRIM(RTRIM(ISNULL(e.NumeroParte,N'')))),
              N'.',N''),N'_',N''),N'?',N''),N':',N''),N' ',N''),N'-',N''),N'/',N'')=@NumeroParte
          OR
          REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
              UPPER(LTRIM(RTRIM(ISNULL(e.ReferenciaSAP,N'')))),
              N'.',N''),N'_',N''),N'?',N''),N':',N''),N' ',N''),N'-',N''),N'/',N'')=@NumeroParte
          OR
          REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
              UPPER(LTRIM(RTRIM(ISNULL(i.NumeroParte,N'')))),
              N'.',N''),N'_',N''),N'?',N''),N':',N''),N' ',N''),N'-',N''),N'/',N'')=@NumeroParte
      )
)
SELECT TOP(1)
    InspeccionID,
    EjecucionProduccionID,
    ProgramaProduccionID,
    SolicitudProduccionID,
    SolicitudProduccionDetalleID,
    ReleaseID,
    ReleaseDetalleID,
    CantidadPlaneada,
    CantidadOKTotal,
    PiezasPorEmbalaje,
    CantidadEmbalajes,
    EstadoInspeccion,
    ConfiguracionInvalidada,
    OrdenFabricacion,
    NumeroParteEsperado,
    COUNT(1) OVER() AS TotalCoincidencias
FROM Candidatos
WHERE rn=1
ORDER BY
    CASE
        WHEN @InspeccionSolicitada IS NOT NULL
         AND InspeccionID=@InspeccionSolicitada
            THEN 0
        ELSE 1
    END,
    InspeccionID DESC;";

                int inspeccionIdReal;
                int ejecucionProduccionId;
                int programaProduccionId;
                int? solicitudProduccionId;
                int? solicitudProduccionDetalleId;
                int? releaseId;
                int? releaseDetalleId;
                int cantidadPlaneada;
                int cantidadOKTotal;
                decimal? piezasPorEmbalaje;
                decimal? cantidadEmbalajes;
                string estadoInspeccion;
                bool configuracionInvalidada;
                string ordenFabricacion;
                string parteEsperada;
                int totalCoincidencias;

                await using (var cmd = new SqlCommand(sqlContexto, cn, tx))
                {
                                        // NSQ_CALIDAD_CANONICAL_PARAMS_V1_3
                    cmd.Parameters.Add(
                        "@NumeroOF",
                        SqlDbType.NVarChar,
                        120).Value =
                        numeroOFClave;

                    cmd.Parameters.Add(
                        "@NumeroParte",
                        SqlDbType.NVarChar,
                        150).Value =
                        numeroParteClave;

                    cmd.Parameters.Add(
                        "@InspeccionSolicitada",
                        SqlDbType.Int).Value =
                        model.InspeccionID.HasValue &&
                        model.InspeccionID.Value > 0
                            ? model.InspeccionID.Value
                            : DBNull.Value;

                    await using var rd = await cmd.ExecuteReaderAsync();

                    if (!await rd.ReadAsync())
                    {
                                                // NSQ_CALIDAD_CLOSE_READER_V1_2
                        await rd.DisposeAsync();
                        await tx.RollbackAsync();

                        TempData["Error"] =
                            $"No se encontró una corrida activa de Calidad que corresponda a la etiqueta. " +
                            $"OF: {numeroOF}. Parte: {numeroParte}.";

                        return model.InspeccionID.HasValue
                            ? RedirectToAction(nameof(Detalle), new { id = model.InspeccionID.Value })
                            : RedirectToAction(nameof(Index), new { grupo = "CAJAS" });
                    }

                    inspeccionIdReal =
                        Convert.ToInt32(rd["InspeccionID"]);

                    ejecucionProduccionId =
                        Convert.ToInt32(rd["EjecucionProduccionID"]);

                    programaProduccionId =
                        Convert.ToInt32(rd["ProgramaProduccionID"]);

                    solicitudProduccionId =
                        rd["SolicitudProduccionID"] == DBNull.Value
                            ? null
                            : Convert.ToInt32(rd["SolicitudProduccionID"]);

                    solicitudProduccionDetalleId =
                        rd["SolicitudProduccionDetalleID"] == DBNull.Value
                            ? null
                            : Convert.ToInt32(rd["SolicitudProduccionDetalleID"]);

                    releaseId =
                        rd["ReleaseID"] == DBNull.Value
                            ? null
                            : Convert.ToInt32(rd["ReleaseID"]);

                    releaseDetalleId =
                        rd["ReleaseDetalleID"] == DBNull.Value
                            ? null
                            : Convert.ToInt32(rd["ReleaseDetalleID"]);

                    cantidadPlaneada =
                        Convert.ToInt32(rd["CantidadPlaneada"]);

                    cantidadOKTotal =
                        Convert.ToInt32(rd["CantidadOKTotal"]);

                    piezasPorEmbalaje =
                        rd["PiezasPorEmbalaje"] == DBNull.Value
                            ? null
                            : Convert.ToDecimal(rd["PiezasPorEmbalaje"]);

                    cantidadEmbalajes =
                        rd["CantidadEmbalajes"] == DBNull.Value
                            ? null
                            : Convert.ToDecimal(rd["CantidadEmbalajes"]);

                    estadoInspeccion =
                        rd["EstadoInspeccion"]?.ToString()?.Trim()
                        ?? string.Empty;

                    configuracionInvalidada =
                        Convert.ToBoolean(rd["ConfiguracionInvalidada"]);

                    ordenFabricacion =
                        rd["OrdenFabricacion"]?.ToString()?.Trim()
                        ?? numeroOF;

                    parteEsperada =
                        rd["NumeroParteEsperado"]?.ToString()?.Trim()
                        ?? numeroParte;

                    totalCoincidencias =
                        Convert.ToInt32(rd["TotalCoincidencias"]);
                }

                if (model.InspeccionID.HasValue &&
                    model.InspeccionID.Value > 0 &&
                    model.InspeccionID.Value != inspeccionIdReal)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        $"La etiqueta corresponde a otra inspección. " +
                        $"OF: {ordenFabricacion}. Parte: {parteEsperada}.";

                    TempData["InspeccionCajaEscaneadaID"] =
                        inspeccionIdReal;

                    return RedirectToAction(
                        nameof(Detalle),
                        new { id = model.InspeccionID.Value });
                }

                if (!model.InspeccionID.HasValue &&
                    totalCoincidencias > 1)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "La etiqueta coincide con más de una corrida activa. Abre la inspección correspondiente y vuelve a escanear la caja.";

                    return RedirectToAction(
                        nameof(Index),
                        new { grupo = "CAJAS" });
                }

                if (configuracionInvalidada)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "La configuración de la corrida fue invalidada. La caja no puede registrarse hasta completar la reliberación correspondiente.";

                    return RedirectToAction(
                        nameof(Detalle),
                        new { id = inspeccionIdReal });
                }

                if (EstadoBloqueaRevisionCaja(estadoInspeccion))
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "La inspección de Calidad está cerrada o bloqueada y no permite recibir cajas.";

                    return RedirectToAction(
                        nameof(Detalle),
                        new { id = inspeccionIdReal });
                }

                if (!piezasPorEmbalaje.HasValue ||
                    piezasPorEmbalaje.Value <= 0)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "La pieza no tiene configurada la capacidad de piezas por embalaje.";

                    return RedirectToAction(
                        nameof(Detalle),
                        new { id = inspeccionIdReal });
                }

                var capacidadCaja =
                    Convert.ToInt32(
                        Math.Floor(piezasPorEmbalaje.Value));

                if (capacidadCaja <= 0)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "La capacidad configurada del embalaje no es válida.";

                    return RedirectToAction(
                        nameof(Detalle),
                        new { id = inspeccionIdReal });
                }

                if (parseado.Cantidad > capacidadCaja)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        $"La etiqueta indica {parseado.Cantidad:N0} pieza(s), " +
                        $"pero el embalaje permite como máximo {capacidadCaja:N0}.";

                    return RedirectToAction(
                        nameof(Detalle),
                        new { id = inspeccionIdReal });
                }

                if (cantidadPlaneada <= 0)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "La ejecución no tiene una cantidad planeada válida.";

                    return RedirectToAction(
                        nameof(Detalle),
                        new { id = inspeccionIdReal });
                }

                var ahora = DateTime.Now;

                var cajaBlancaCompletada =
                    await VincularCajaCompletadaEtiquetaBlancaAsync(
                        inspeccionIdReal,
                        ejecucionProduccionId,
                        estadoInspeccion,
                        codigoFisico,
                        numeroOF,
                        numeroParte,
                        parseado.Designacion,
                        parseado.Cantidad,
                        parseado.Lote,
                        usuarioId.Value,
                        ahora,
                        cn,
                        tx);
                if (cajaBlancaCompletada.Vinculada)
                {
                    await AsegurarTrazabilidadHorariaCajaAsync(cajaBlancaCompletada.CajaProduccionID, usuarioId.Value, cn, tx);
                    await tx.CommitAsync();

                    TempData["Mensaje"] =
                        $"Caja {cajaBlancaCompletada.FolioCaja} vinculada correctamente a la etiqueta física de Planeación. " +
                        $"Proviene de la etiqueta blanca {cajaBlancaCompletada.EtiquetaBlanca}. " +
                        $"Cantidad: {parseado.Cantidad:N0}. Ahora Calidad puede validarla.";

                    return RedirectToAction(nameof(Detalle), new { id = inspeccionIdReal });
                }

               
                const string sqlConsumo = @"
SELECT
    ISNULL
    (
        (
            SELECT SUM
            (
                ISNULL(c.CantidadPiezas,ISNULL(c.Cantidad,0))
            )
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
              )
        ),
        0
    ) AS OkNormal,

    ISNULL
    (
        (
            SELECT SUM(od.CantidadPiezas)
            FROM dbo.Produccion_CajaOrigenDetalle od WITH(UPDLOCK,HOLDLOCK)
            INNER JOIN dbo.Produccion_Cajas c
                ON c.CajaProduccionID=od.CajaProduccionID
               AND c.Activo=1
            WHERE od.EjecucionProduccionID=@EjecucionProduccionID
              AND od.Activo=1
        ),
        0
    ) AS OkDetalle,

    ISNULL
    (
        (
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
              )
        ),
        0
    ) AS CajasNormales,

    ISNULL
    (
        (
            SELECT MAX(ISNULL(c.NumeroCaja,0))
            FROM dbo.Produccion_Cajas c WITH(UPDLOCK,HOLDLOCK)
            WHERE c.EjecucionProduccionID=@EjecucionProduccionID
              -- NSQ_CALIDAD_CAJAS_FOLIO_UNICO_V2_CORREGIDO
              -- La numeracion debe considerar cajas historicas porque las restricciones UNIQUE no filtran Activo.
        ),
        0
    )+1 AS SiguienteNumero;";

                int consumoOk;
                int cajasNormales;
                int siguienteNumero;

                await using (var cmd =
                    new SqlCommand(sqlConsumo, cn, tx))
                {
                    cmd.Parameters.Add(
                        "@EjecucionProduccionID",
                        SqlDbType.Int).Value =
                        ejecucionProduccionId;

                    await using var rd =
                        await cmd.ExecuteReaderAsync();

                    await rd.ReadAsync();

                    consumoOk =
                        Convert.ToInt32(rd["OkNormal"]) +
                        Convert.ToInt32(rd["OkDetalle"]);

                    cajasNormales =
                        Convert.ToInt32(rd["CajasNormales"]);

                    siguienteNumero =
                        Convert.ToInt32(rd["SiguienteNumero"]);
                }

                var planeadoPendiente =
                    Math.Max(
                        0,
                        cantidadPlaneada - consumoOk);

                if (planeadoPendiente <= 0)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "La cantidad planeada ya se encuentra completamente aplicada a cajas. " +
                        "La sobreproducción debe manejarse mediante producto incompleto/etiqueta blanca.";

                    return RedirectToAction(
                        nameof(Detalle),
                        new { id = inspeccionIdReal });
                }

                var cantidadEsperadaCaja =
                    Math.Min(
                        capacidadCaja,
                        planeadoPendiente);

                if (parseado.Cantidad != cantidadEsperadaCaja)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        $"La cantidad de la etiqueta no corresponde a la siguiente caja esperada. " +
                        $"Esperada: {cantidadEsperadaCaja:N0} pieza(s). " +
                        $"Etiqueta: {parseado.Cantidad:N0} pieza(s).";

                    return RedirectToAction(
                        nameof(Detalle),
                        new { id = inspeccionIdReal });
                }

                var cantidadOKDisponible =
                    Math.Max(
                        0,
                        cantidadOKTotal - consumoOk);

                if (parseado.Cantidad > cantidadOKDisponible)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        $"Todavía no existen suficientes piezas OK producidas para registrar esta caja. " +
                        $"Etiqueta: {parseado.Cantidad:N0}; disponibles: {cantidadOKDisponible:N0}.";

                    return RedirectToAction(
                        nameof(Detalle),
                        new { id = inspeccionIdReal });
                }

                if (cantidadEmbalajes.HasValue &&
                    cantidadEmbalajes.Value > 0)
                {
                    var cajasEsperadas =
                        Convert.ToInt32(
                            Math.Ceiling(cantidadEmbalajes.Value));

                    if (cajasNormales >= cajasEsperadas)
                    {
                        await tx.RollbackAsync();

                        TempData["Error"] =
                            $"Ya se registraron las {cajasEsperadas:N0} caja(s) normales esperadas para esta orden.";

                        return RedirectToAction(
                            nameof(Detalle),
                            new { id = inspeccionIdReal });
                    }
                }

                var folioCaja =
                    $"PROD-{ejecucionProduccionId}-C{siguienteNumero:000}";

                const string sqlInsert = @"
INSERT INTO dbo.Produccion_Cajas
(
    EjecucionProduccionID,
    ProgramaProduccionID,
    SolicitudProduccionID,
    SolicitudProduccionDetalleID,
    ReleaseID,
    ReleaseDetalleID,
    NumeroCaja,
    FolioCaja,
    CantidadPiezas,
    TipoCaja,
    LoteMaterial,
    EtiquetaFolio,
    EstadoCajaID,
    EstadoCajaNombre,
    EtiquetaVerde,
    FechaFormacion,
    UsuarioFormacionID,
    FechaSolicitudCalidad,
    UsuarioSolicitudCalidadID,
    Observaciones,
    Activo,
    UsuarioCreacionID,
    FechaCreacion,
    Etiqueta,
    Cantidad,
    EstatusCalidad,
    OperadorUsuarioID,
    EsProductoIncompleto,
    CodigoBarrasOrigen,
    NumeroOFEtiqueta,
    NumeroParteEtiqueta,
    DesignacionEtiqueta,
    CantidadEtiqueta,
    LoteEtiqueta,
    FechaEscaneoProduccion,
    UsuarioEscaneoProduccionID,
    FechaEscaneoCalidad,
    UsuarioEscaneoCalidadID,
    UsuarioModificacionID,
    FechaModificacion
)
OUTPUT INSERTED.CajaProduccionID
VALUES
(
    @EjecucionProduccionID,
    @ProgramaProduccionID,
    @SolicitudProduccionID,
    @SolicitudProduccionDetalleID,
    @ReleaseID,
    @ReleaseDetalleID,
    @NumeroCaja,
    @FolioCaja,
    @CantidadPiezas,
    N'OK',
    @Lote,
    NULL,
    @EstadoCajaID,
    N'Escaneada por Calidad - pendiente validación',
    0,
    @Ahora,
    NULL,
    @Ahora,
    NULL,
    @Observaciones,
    1,
    @UsuarioID,
    @Ahora,
    @FolioCaja,
    @CantidadPiezas,
    N'PENDIENTE' /* NSQ_CALIDAD_ESTATUS_V1_4_1_SQL */,
    NULL,
    0,
    @CodigoBarras,
    @NumeroOF,
    @NumeroParte,
    @Designacion,
    @CantidadPiezas,
    @Lote,
    NULL,
    NULL,
    @Ahora,
    @UsuarioID,
    @UsuarioID,
    @Ahora
);";

                long cajaProduccionId;

                await using (var cmd =
                    new SqlCommand(sqlInsert, cn, tx))
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
                        "@SolicitudProduccionID",
                        SqlDbType.Int).Value =
                        (object?)solicitudProduccionId
                        ?? DBNull.Value;

                    cmd.Parameters.Add(
                        "@SolicitudProduccionDetalleID",
                        SqlDbType.Int).Value =
                        (object?)solicitudProduccionDetalleId
                        ?? DBNull.Value;

                    cmd.Parameters.Add(
                        "@ReleaseID",
                        SqlDbType.Int).Value =
                        (object?)releaseId
                        ?? DBNull.Value;

                    cmd.Parameters.Add(
                        "@ReleaseDetalleID",
                        SqlDbType.Int).Value =
                        (object?)releaseDetalleId
                        ?? DBNull.Value;

                    cmd.Parameters.Add(
                        "@NumeroCaja",
                        SqlDbType.Int).Value =
                        siguienteNumero;

                    cmd.Parameters.Add(
                        "@FolioCaja",
                        SqlDbType.NVarChar,
                        100).Value =
                        folioCaja;

                    cmd.Parameters.Add(
                        "@CantidadPiezas",
                        SqlDbType.Int).Value =
                        parseado.Cantidad;

                    cmd.Parameters.Add(
                        "@Lote",
                        SqlDbType.NVarChar,
                        150).Value =
                        string.IsNullOrWhiteSpace(parseado.Lote)
                            ? DBNull.Value
                            : parseado.Lote.Trim();

                    cmd.Parameters.Add(
                        "@EstadoCajaID",
                        SqlDbType.Int).Value =
                        ProduccionCajaEstatus.PendienteCalidad;

                    cmd.Parameters.Add(
                        "@Observaciones",
                        SqlDbType.NVarChar,
                        500).Value =
                        "Caja registrada automáticamente al primer escaneo físico realizado por Calidad. " +
                        "La formación de la caja se realiza físicamente en Producción.";

                    cmd.Parameters.Add(
                        "@UsuarioID",
                        SqlDbType.Int).Value =
                        usuarioId.Value;

                    cmd.Parameters.Add(
                        "@Ahora",
                        SqlDbType.DateTime2).Value =
                        ahora;

                    cmd.Parameters.Add(
                        "@CodigoBarras",
                        SqlDbType.NVarChar,
                        500).Value =
                        codigoFisico;

                    cmd.Parameters.Add(
                        "@NumeroOF",
                        SqlDbType.NVarChar,
                        120).Value =
                        numeroOF;

                    cmd.Parameters.Add(
                        "@NumeroParte",
                        SqlDbType.NVarChar,
                        150).Value =
                        numeroParte;

                    cmd.Parameters.Add(
                        "@Designacion",
                        SqlDbType.NVarChar,
                        300).Value =
                        string.IsNullOrWhiteSpace(parseado.Designacion)
                            ? DBNull.Value
                            : parseado.Designacion.Trim();
                    cajaProduccionId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
                }

                await AsegurarTrazabilidadHorariaCajaAsync(cajaProduccionId, usuarioId.Value, cn, tx);

                const string sqlHistorial = @"
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
    N'CAJA_ESCANEADA_CALIDAD',
    @Estado,
    @Estado,
    NULL,
    NULL,
    @Comentario,
    @UsuarioID,
    @Ahora
);";

                await using (var cmd =
                    new SqlCommand(sqlHistorial, cn, tx))
                {
                    var comentario =
                        $"Calidad realizó el primer escaneo físico y registró la caja {folioCaja} en ERP. " +
                        $"OF: {numeroOF}. Parte: {numeroParte}. Cantidad: {parseado.Cantidad:N0}.";

                    if (comentario.Length > 1000)
                        comentario = comentario[..1000];

                    cmd.Parameters.Add(
                        "@InspeccionID",
                        SqlDbType.Int).Value =
                        inspeccionIdReal;

                    cmd.Parameters.Add(
                        "@Estado",
                        SqlDbType.NVarChar,
                        50).Value =
                        estadoInspeccion;

                    cmd.Parameters.Add(
                        "@Comentario",
                        SqlDbType.NVarChar,
                        1000).Value =
                        comentario;

                    cmd.Parameters.Add(
                        "@UsuarioID",
                        SqlDbType.Int).Value =
                        usuarioId.Value;

                    cmd.Parameters.Add(
                        "@Ahora",
                        SqlDbType.DateTime2).Value =
                        ahora;

                    await cmd.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();

                TempData["Mensaje"] =
                    $"Caja {folioCaja} registrada y escaneada correctamente por Calidad " +
                    $"con {parseado.Cantidad:N0} pieza(s). " +
                    $"Ahora puede validarse como verde, GP12 o devolverse a Producción.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = inspeccionIdReal });
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627)
            {
                try
                {
                    await tx.RollbackAsync();
                }
                catch
                {
                }

                TempData["Error"] =
                    "La etiqueta ya fue registrada. No se generó una caja duplicada.";

                return model.InspeccionID.HasValue
                    ? RedirectToAction(nameof(Detalle), new { id = model.InspeccionID.Value })
                    : RedirectToAction(nameof(Index), new { grupo = "CAJAS" });
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
                    "No fue posible escanear la caja en Calidad: " +
                    ex.Message;

                return model.InspeccionID.HasValue
                    ? RedirectToAction(nameof(Detalle), new { id = model.InspeccionID.Value })
                    : RedirectToAction(nameof(Index), new { grupo = "CAJAS" });
            }
        }

        private static async Task<(
    bool Vinculada,
    long CajaProduccionID,
    string FolioCaja,
    string EtiquetaBlanca)>
    VincularCajaCompletadaEtiquetaBlancaAsync(
        int inspeccionId,
        int ejecucionProduccionId,
        string estadoInspeccion,
        string codigoBarras,
        string numeroOF,
        string numeroParte,
        string? designacion,
        int cantidad,
        string? lote,
        int usuarioId,
        DateTime ahora,
        SqlConnection cn,
        SqlTransaction tx)
        {
            const string sqlBuscar = @"
SELECT TOP(1)
    pc.CajaProduccionID,
    COALESCE
    (
        NULLIF(pc.FolioCaja,N''),
        NULLIF(pc.Etiqueta,N''),
        CONVERT(NVARCHAR(100),pc.CajaProduccionID)
    ) AS FolioCaja,
    pc.EtiquetaBlanca
FROM dbo.Produccion_Cajas pc WITH(UPDLOCK,HOLDLOCK)
WHERE pc.EjecucionProduccionID=@EjecucionProduccionID
  AND pc.Activo=1
  AND pc.EstadoCajaID=@PendienteCalidad
  AND UPPER(LTRIM(RTRIM(ISNULL(pc.TipoCaja,N'OK'))))=N'OK'
  AND ISNULL(pc.EsProductoIncompleto,0)=0
  AND UPPER(LTRIM(RTRIM(ISNULL(pc.EstadoProductoIncompleto,N''))))=N'COMPLETA'
  AND NULLIF(LTRIM(RTRIM(ISNULL(pc.EtiquetaBlanca,N''))),N'') IS NOT NULL
  AND pc.FechaCompletadoIncompleto IS NOT NULL
  AND NULLIF(LTRIM(RTRIM(ISNULL(pc.CodigoBarrasOrigen,N''))),N'') IS NULL
  AND pc.FechaEscaneoCalidad IS NULL
  AND ISNULL(pc.CantidadPiezas,ISNULL(pc.Cantidad,0))=@Cantidad
ORDER BY
    pc.FechaCompletadoIncompleto,
    pc.CajaProduccionID;";

            long? cajaProduccionId = null;
            string folioCaja = string.Empty;
            string etiquetaBlanca = string.Empty;

            await using (var cmd =
                new SqlCommand(sqlBuscar, cn, tx))
            {
                cmd.Parameters.Add(
                    "@EjecucionProduccionID",
                    SqlDbType.Int).Value =
                    ejecucionProduccionId;

                cmd.Parameters.Add(
                    "@PendienteCalidad",
                    SqlDbType.Int).Value =
                    ProduccionCajaEstatus.PendienteCalidad;

                cmd.Parameters.Add(
                    "@Cantidad",
                    SqlDbType.Int).Value =
                    cantidad;

                await using var rd =
                    await cmd.ExecuteReaderAsync();

                if (await rd.ReadAsync())
                {
                    cajaProduccionId =
                        Convert.ToInt64(
                            rd["CajaProduccionID"]);

                    folioCaja =
                        rd["FolioCaja"]?.ToString()?.Trim()
                        ?? cajaProduccionId.Value.ToString();

                    etiquetaBlanca =
                        rd["EtiquetaBlanca"]?.ToString()?.Trim()
                        ?? string.Empty;
                }
            }

            if (!cajaProduccionId.HasValue)
            {
                return (
                    false,
                    0,
                    string.Empty,
                    string.Empty);
            }

            var observacion =
                $"Caja proveniente de etiqueta blanca " +
                $"{(string.IsNullOrWhiteSpace(etiquetaBlanca) ? cajaProduccionId.Value.ToString() : etiquetaBlanca)} " +
                $"vinculada a etiqueta física de Planeación mediante escaneo de Calidad.";

            if (observacion.Length > 500)
                observacion = observacion[..500];

            const string sqlActualizar = @"
UPDATE dbo.Produccion_Cajas
SET
    CodigoBarrasOrigen=@CodigoBarras,
    NumeroOFEtiqueta=@NumeroOF,
    NumeroParteEtiqueta=@NumeroParte,
    DesignacionEtiqueta=@Designacion,
    CantidadEtiqueta=@Cantidad,
    LoteEtiqueta=@Lote,
    LoteMaterial=
        COALESCE
        (
            NULLIF(LoteMaterial,N''),
            @Lote
        ),
    FechaEscaneoCalidad=@Ahora,
    UsuarioEscaneoCalidadID=@UsuarioID,
    FechaSolicitudCalidad=
        COALESCE
        (
            FechaSolicitudCalidad,
            @Ahora
        ),
    UsuarioSolicitudCalidadID=
        COALESCE
        (
            UsuarioSolicitudCalidadID,
            @UsuarioID
        ),
    EstadoCajaNombre=N'Escaneada por Calidad - pendiente validación',
    EstatusCalidad=N'ESCANEADA',
    Observaciones=
        LEFT
        (
            COALESCE
            (
                NULLIF(Observaciones,N'')+N' | ',
                N''
            )+@Observaciones,
            500
        ),
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=@Ahora
WHERE CajaProduccionID=@CajaProduccionID
  AND EjecucionProduccionID=@EjecucionProduccionID
  AND Activo=1
  AND EstadoCajaID=@PendienteCalidad
  AND UPPER(LTRIM(RTRIM(ISNULL(TipoCaja,N'OK'))))=N'OK'
  AND ISNULL(EsProductoIncompleto,0)=0
  AND UPPER(LTRIM(RTRIM(ISNULL(EstadoProductoIncompleto,N''))))=N'COMPLETA'
  AND FechaCompletadoIncompleto IS NOT NULL
  AND NULLIF(LTRIM(RTRIM(ISNULL(CodigoBarrasOrigen,N''))),N'') IS NULL
  AND FechaEscaneoCalidad IS NULL
  AND ISNULL(CantidadPiezas,ISNULL(Cantidad,0))=@Cantidad;

IF @@ROWCOUNT<>1
    THROW 51405,'La caja completada con etiqueta blanca cambió de estado mientras Calidad realizaba el escaneo.',1;";

            await using (var cmd =
                new SqlCommand(sqlActualizar, cn, tx))
            {
                cmd.Parameters.Add(
                    "@CajaProduccionID",
                    SqlDbType.BigInt).Value =
                    cajaProduccionId.Value;

                cmd.Parameters.Add(
                    "@EjecucionProduccionID",
                    SqlDbType.Int).Value =
                    ejecucionProduccionId;

                cmd.Parameters.Add(
                    "@PendienteCalidad",
                    SqlDbType.Int).Value =
                    ProduccionCajaEstatus.PendienteCalidad;

                cmd.Parameters.Add(
                    "@CodigoBarras",
                    SqlDbType.NVarChar,
                    500).Value =
                    codigoBarras;

                cmd.Parameters.Add(
                    "@NumeroOF",
                    SqlDbType.NVarChar,
                    120).Value =
                    numeroOF;

                cmd.Parameters.Add(
                    "@NumeroParte",
                    SqlDbType.NVarChar,
                    150).Value =
                    numeroParte;

                cmd.Parameters.Add(
                    "@Designacion",
                    SqlDbType.NVarChar,
                    300).Value =
                    string.IsNullOrWhiteSpace(designacion)
                        ? DBNull.Value
                        : designacion.Trim();

                cmd.Parameters.Add(
                    "@Cantidad",
                    SqlDbType.Int).Value =
                    cantidad;

                cmd.Parameters.Add(
                    "@Lote",
                    SqlDbType.NVarChar,
                    150).Value =
                    string.IsNullOrWhiteSpace(lote)
                        ? DBNull.Value
                        : lote.Trim();

                cmd.Parameters.Add(
                    "@Observaciones",
                    SqlDbType.NVarChar,
                    500).Value =
                    observacion;

                cmd.Parameters.Add(
                    "@Ahora",
                    SqlDbType.DateTime2).Value =
                    ahora;

                cmd.Parameters.Add(
                    "@UsuarioID",
                    SqlDbType.Int).Value =
                    usuarioId;

                await cmd.ExecuteNonQueryAsync();
            }

            var comentarioHistorial =
                $"Calidad vinculó la etiqueta física de Planeación a la caja {folioCaja}, " +
                $"proveniente de la etiqueta blanca " +
                $"{(string.IsNullOrWhiteSpace(etiquetaBlanca) ? cajaProduccionId.Value.ToString() : etiquetaBlanca)}. " +
                $"OF: {numeroOF}. Parte: {numeroParte}. Cantidad: {cantidad:N0}.";

            if (comentarioHistorial.Length > 1000)
                comentarioHistorial =
                    comentarioHistorial[..1000];

            const string sqlHistorial = @"
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
    N'CAJA_ESCANEADA_CALIDAD',
    @Estado,
    @Estado,
    NULL,
    @EtiquetaBlanca,
    @Comentario,
    @UsuarioID,
    @Ahora
);";

            await using (var cmd =
                new SqlCommand(sqlHistorial, cn, tx))
            {
                cmd.Parameters.Add(
                    "@InspeccionID",
                    SqlDbType.Int).Value =
                    inspeccionId;

                cmd.Parameters.Add(
                    "@Estado",
                    SqlDbType.NVarChar,
                    50).Value =
                    estadoInspeccion;

                cmd.Parameters.Add(
                    "@EtiquetaBlanca",
                    SqlDbType.NVarChar,
                    100).Value =
                    string.IsNullOrWhiteSpace(etiquetaBlanca)
                        ? DBNull.Value
                        : etiquetaBlanca;

                cmd.Parameters.Add(
                    "@Comentario",
                    SqlDbType.NVarChar,
                    1000).Value =
                    comentarioHistorial;

                cmd.Parameters.Add(
                    "@UsuarioID",
                    SqlDbType.Int).Value =
                    usuarioId;

                cmd.Parameters.Add(
                    "@Ahora",
                    SqlDbType.DateTime2).Value =
                    ahora;

                await cmd.ExecuteNonQueryAsync();
            }

            return (
                true,
                cajaProduccionId.Value,
                folioCaja,
                etiquetaBlanca);
        }

        private static string? LeerTextoCaja(
            SqlDataReader rd,
            string columna)
        {
            var ordinal = rd.GetOrdinal(columna);

            return rd.IsDBNull(ordinal)
                ? null
                : rd.GetValue(ordinal)?.ToString()?.Trim();
        }

        private static int? LeerEnteroNullableCaja(
            SqlDataReader rd,
            string columna)
        {
            var ordinal = rd.GetOrdinal(columna);

            return rd.IsDBNull(ordinal)
                ? null
                : Convert.ToInt32(rd.GetValue(ordinal));
        }

        private static DateTime? LeerFechaNullableCaja(
            SqlDataReader rd,
            string columna)
        {
            var ordinal = rd.GetOrdinal(columna);

            return rd.IsDBNull(ordinal)
                ? null
                : Convert.ToDateTime(rd.GetValue(ordinal));
        }
    }
}
