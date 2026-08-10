using ERP.NSQuell.Models;
using ERP.NSQuell.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

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
        }
        private async Task<List<CalidadCajaProduccionItemViewModel>>
            CargarCajasPendientesCalidadAsync(string? busqueda)
        {
            var lista = new List<CalidadCajaProduccionItemViewModel>();

            const string sql = @"
SELECT
    pc.CajaProduccionID,
    ci.InspeccionID,
    pc.EjecucionProduccionID,
    ISNULL(pc.ProgramaProduccionID, ISNULL(ci.ProgramaProduccionID, 0)) AS ProgramaProduccionID,
    ISNULL(pc.NumeroCaja, 0) AS NumeroCaja,
    COALESCE(NULLIF(pc.FolioCaja, ''), NULLIF(pc.Etiqueta, ''), CONVERT(NVARCHAR(100), pc.CajaProduccionID)) AS FolioCaja,
    ISNULL(pc.CantidadPiezas, ISNULL(pc.Cantidad, 0)) AS CantidadPiezas,
    ISNULL(pc.TipoCaja, N'OK') AS TipoCaja,
    pc.LoteMaterial,
    COALESCE(NULLIF(pc.EtiquetaFolio, ''), NULLIF(pc.Etiqueta, '')) AS EtiquetaFolio,
    ISNULL(pc.EtiquetaVerde, 0) AS EtiquetaVerde,
    ISNULL(pc.EstadoCajaID, 1) AS EstadoCajaID,
    ISNULL(pc.EstadoCajaNombre, N'Formada en Producción') AS EstadoCajaNombre,
    ISNULL(pc.FechaFormacion, pc.FechaCreacion) AS FechaFormacion,
    pc.FechaSolicitudCalidad,
    pc.FechaLiberacionCalidad,
    pc.ResultadoCalidad,
    pc.MotivoCalidad,
    ci.OrdenTrabajo,
    ci.ClienteNombre,
    ci.NumeroParte,
    ci.Maquina,
    ci.Molde
FROM dbo.Produccion_Cajas pc
CROSS APPLY
(
    SELECT TOP (1)
        i.InspeccionID,
        i.ProgramaProduccionID,
        i.OrdenTrabajo,
        i.ClienteNombre,
        i.NumeroParte,
        i.Maquina,
        i.Molde
    FROM dbo.Calidad_Inspecciones i
    WHERE i.EjecucionProduccionID = pc.EjecucionProduccionID
      AND ISNULL(i.ConfiguracionInvalidada, 0) = 0
    ORDER BY i.InspeccionID DESC
) ci
WHERE pc.Activo = 1
  AND ISNULL(pc.EstadoCajaID, 1) = 2
  AND
  (
        @Busqueda IS NULL
     OR pc.FolioCaja LIKE '%' + @Busqueda + '%'
     OR pc.Etiqueta LIKE '%' + @Busqueda + '%'
     OR pc.EtiquetaFolio LIKE '%' + @Busqueda + '%'
     OR pc.LoteMaterial LIKE '%' + @Busqueda + '%'
     OR ci.OrdenTrabajo LIKE '%' + @Busqueda + '%'
     OR ci.ClienteNombre LIKE '%' + @Busqueda + '%'
     OR ci.NumeroParte LIKE '%' + @Busqueda + '%'
     OR ci.Maquina LIKE '%' + @Busqueda + '%'
     OR ci.Molde LIKE '%' + @Busqueda + '%'
  )
ORDER BY
    ISNULL(pc.FechaSolicitudCalidad, pc.FechaCreacion),
    pc.CajaProduccionID;";

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@Busqueda", SqlDbType.NVarChar, 200).Value =
                string.IsNullOrWhiteSpace(busqueda)
                    ? DBNull.Value
                    : busqueda.Trim();

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
                lista.Add(MapearCajaProduccionCalidad(rd));

            return lista;
        }

        private async Task<List<CalidadCajaProduccionItemViewModel>>
            CargarCajasProduccionInspeccionAsync(
                int inspeccionId,
                int? ejecucionProduccionId)
        {
            var lista = new List<CalidadCajaProduccionItemViewModel>();

            if (!ejecucionProduccionId.HasValue ||
                ejecucionProduccionId.Value <= 0)
            {
                return lista;
            }

            const string sql = @"
SELECT
    pc.CajaProduccionID,
    ci.InspeccionID,
    pc.EjecucionProduccionID,
    ISNULL(pc.ProgramaProduccionID, ISNULL(ci.ProgramaProduccionID, 0)) AS ProgramaProduccionID,
    ISNULL(pc.NumeroCaja, 0) AS NumeroCaja,
    COALESCE(NULLIF(pc.FolioCaja, ''), NULLIF(pc.Etiqueta, ''), CONVERT(NVARCHAR(100), pc.CajaProduccionID)) AS FolioCaja,
    ISNULL(pc.CantidadPiezas, ISNULL(pc.Cantidad, 0)) AS CantidadPiezas,
    ISNULL(pc.TipoCaja, N'OK') AS TipoCaja,
    pc.LoteMaterial,
    COALESCE(NULLIF(pc.EtiquetaFolio, ''), NULLIF(pc.Etiqueta, '')) AS EtiquetaFolio,
    ISNULL(pc.EtiquetaVerde, 0) AS EtiquetaVerde,
    ISNULL(pc.EstadoCajaID, 1) AS EstadoCajaID,
    ISNULL(pc.EstadoCajaNombre, N'Formada en Producción') AS EstadoCajaNombre,
    ISNULL(pc.FechaFormacion, pc.FechaCreacion) AS FechaFormacion,
    pc.FechaSolicitudCalidad,
    pc.FechaLiberacionCalidad,
    pc.ResultadoCalidad,
    pc.MotivoCalidad,
    ci.OrdenTrabajo,
    ci.ClienteNombre,
    ci.NumeroParte,
    ci.Maquina,
    ci.Molde
FROM dbo.Produccion_Cajas pc
INNER JOIN dbo.Calidad_Inspecciones ci
    ON ci.InspeccionID = @InspeccionID
   AND ci.EjecucionProduccionID = pc.EjecucionProduccionID
WHERE pc.EjecucionProduccionID = @EjecucionProduccionID
  AND pc.Activo = 1
ORDER BY
    ISNULL(pc.NumeroCaja, 0),
    pc.CajaProduccionID;";

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value = inspeccionId;
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value =
                ejecucionProduccionId.Value;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
                lista.Add(MapearCajaProduccionCalidad(rd));

            return lista;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResolverCajaProduccion(
            CalidadCajaDecisionViewModel model)
        {
            var decision = model.Decision?
                .Trim()
                .ToUpperInvariant();

            if (!ModelState.IsValid ||
                (decision != DecisionCajaLiberar &&
                 decision != DecisionCajaGP12 &&
                 decision != DecisionCajaDevolver))
            {
                TempData["Error"] =
                    "No se recibió una decisión válida para la caja.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = model.InspeccionID });
            }

            model.Decision = decision ?? string.Empty;

            model.NumeroOperadorEtiqueta =
                model.NumeroOperadorEtiqueta?.Trim();

            model.Tarima =
                model.Tarima?.Trim();

            model.MotivoGP12 =
                model.MotivoGP12?.Trim();

            model.Observaciones =
                model.Observaciones?.Trim();

            if (decision == DecisionCajaLiberar)
            {
                if (!model.EstandarPackCumple ||
                    !model.EtiquetaProductoCorrecta ||
                    !model.TecnicoConfirmoInformacion)
                {
                    TempData["Error"] =
                        "Para liberar la caja debes confirmar empaque, etiqueta y validación del técnico.";

                    return RedirectToAction(
                        nameof(Detalle),
                        new { id = model.InspeccionID });
                }

                if (string.IsNullOrWhiteSpace(
                    model.NumeroOperadorEtiqueta))
                {
                    TempData["Error"] =
                        "Captura el número de operador indicado en la etiqueta.";

                    return RedirectToAction(
                        nameof(Detalle),
                        new { id = model.InspeccionID });
                }
            }

            if (decision == DecisionCajaGP12 &&
                string.IsNullOrWhiteSpace(model.MotivoGP12))
            {
                TempData["Error"] =
                    "Captura el motivo para enviar la caja a GP12.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = model.InspeccionID });
            }

            if (decision == DecisionCajaDevolver &&
                string.IsNullOrWhiteSpace(model.Observaciones))
            {
                TempData["Error"] =
                    "Captura el motivo para devolver la caja a Producción.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = model.InspeccionID });
            }

            var usuarioId = ObtenerUsuarioIdActual();

            if (!usuarioId.HasValue || usuarioId.Value <= 0)
                return Unauthorized();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();

            try
            {
                var caja = await ObtenerCajaParaDecisionAsync(
                    model.CajaProduccionID,
                    model.InspeccionID,
                    cn,
                    tx);

                if (caja == null)
                {
                    await tx.RollbackAsync();
                    return NotFound();
                }

                if (caja.EstadoCajaID != 2)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "La caja ya no se encuentra pendiente de revisión de Calidad.";

                    return RedirectToAction(
                        nameof(Detalle),
                        new { id = model.InspeccionID });
                }

                if (caja.ConfiguracionInvalidada)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "La configuración de la corrida fue invalidada. No se puede resolver la caja hasta completar la reliberación correspondiente.";

                    return RedirectToAction(
                        nameof(Detalle),
                        new { id = model.InspeccionID });
                }

                if (EstadoBloqueaRevisionCaja(caja.EstadoInspeccion))
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "El proceso de Calidad se encuentra cerrado o bloqueado y ya no permite decisiones sobre cajas.";

                    return RedirectToAction(
                        nameof(Detalle),
                        new { id = model.InspeccionID });
                }

                if (caja.CantidadPiezas <= 0)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "La caja no tiene una cantidad válida de piezas para revisión.";

                    return RedirectToAction(
                        nameof(Detalle),
                        new { id = model.InspeccionID });
                }

                if (decision == DecisionCajaLiberar &&
                    caja.DisposicionesPendientes > 0)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        $"No se puede liberar la caja a Almacén mientras existan {caja.DisposicionesPendientes} disposición(es) de material pendientes. Resuélvelas o envía la caja a GP12.";

                    return RedirectToAction(
                        nameof(Detalle),
                        new { id = model.InspeccionID });
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
                    estadoCajaId = 3;
                    estadoCajaNombre = "Liberada por Calidad";
                    estatusCalidad = "LIBERADA";
                    comentarioHistorial =
                        $"Caja {caja.FolioCaja} liberada con etiqueta verde para Almacén.";
                }
                else if (decision == DecisionCajaGP12)
                {
                    resultadoCalidad = "GP12";
                    etiquetaLiberacion = "AMARILLA";
                    destino = CalidadDestinoCaja.GP12;
                    estadoRegistroCalidad = CalidadEstadoCaja.EnGP12;
                    estadoCajaId = 4;
                    estadoCajaNombre = "En GP12";
                    estatusCalidad = "GP12";
                    comentarioHistorial =
                        $"Caja {caja.FolioCaja} enviada a GP12. Motivo: {model.MotivoGP12}";
                }
                else
                {
                    resultadoCalidad = "DEVUELTA";
                    etiquetaLiberacion = null;
                    destino = null;
                    estadoRegistroCalidad = CalidadEstadoCaja.Devuelta;
                    estadoCajaId = 1;
                    estadoCajaNombre = "Devuelta por Calidad";
                    estatusCalidad = "DEVUELTA";
                    comentarioHistorial =
                        $"Caja {caja.FolioCaja} devuelta a Producción. Motivo: {model.Observaciones}";
                }

                var motivoCalidad = decision == DecisionCajaGP12
                    ? model.MotivoGP12
                    : model.Observaciones;

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

                var cajaLiberadaId =
                    await RegistrarOActualizarCajaCalidadAsync(
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
                    DecisionCajaLiberar =>
                        "Caja liberada con etiqueta verde.",

                    DecisionCajaGP12 =>
                        "Caja enviada a GP12.",

                    _ =>
                        "Caja devuelta a Producción para corrección."
                };
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                TempData["Error"] =
                    "No fue posible resolver la caja: " + ex.Message;
            }

            return RedirectToAction(
                nameof(Detalle),
                new { id = model.InspeccionID });
        }

        private async Task<CajaProduccionCalidadOrigen?> ObtenerCajaParaDecisionAsync(long cajaProduccionId, int inspeccionId, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
SELECT TOP (1)
    pc.CajaProduccionID,
    ci.InspeccionID,
    pc.EjecucionProduccionID,
    ISNULL(pc.ProgramaProduccionID, ISNULL(ci.ProgramaProduccionID, 0)) AS ProgramaProduccionID,
    ci.SolicitudProduccionID,
    ci.SolicitudProduccionDetalleID,
    ci.ReleaseID,
    ci.ReleaseDetalleID,
    ci.ClienteID,
    ci.ClienteNombre,
    ci.ParteID,
    ci.NumeroParte,
    COALESCE(NULLIF(d.DesignacionDescripcionSAP,N''),NULLIF(p.Designacion,N''),NULLIF(p.Descripcion,N''),NULLIF(ci.NumeroParte,N''),N'Sin descripción') AS DescripcionParte,
    ci.MaterialID,
    COALESCE(NULLIF(d.MaterialCodigo,N''),NULLIF(ci.Material,N'')) AS MaterialCodigo,
    COALESCE(NULLIF(d.MaterialDescripcion,N''),NULLIF(ci.Material,N'')) AS MaterialDescripcion,
    ci.OrdenTrabajo AS OrdenFabricacion,
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
    (SELECT COUNT(1) FROM dbo.Calidad_DisposicionesMaterial dpm WHERE dpm.InspeccionID=ci.InspeccionID AND dpm.Activo=1 AND UPPER(LTRIM(RTRIM(ISNULL(dpm.ResultadoFinal,N''))))=N'PENDIENTE') AS DisposicionesPendientes
FROM dbo.Produccion_Cajas pc WITH (UPDLOCK,HOLDLOCK)
INNER JOIN dbo.Calidad_Inspecciones ci ON ci.InspeccionID=@InspeccionID AND ci.EjecucionProduccionID=pc.EjecucionProduccionID
LEFT JOIN dbo.SolicitudesProduccionDetalle d ON d.SolicitudProduccionDetalleID=ci.SolicitudProduccionDetalleID AND d.Activo=1
LEFT JOIN dbo.ERP_Partes p ON p.ParteID=ci.ParteID
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
                DisposicionesPendientes = Convert.ToInt32(rd["DisposicionesPendientes"])
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

      

        private async Task<List<CalidadGP12ItemViewModel>> CargarRegistrosGP12Async(int inspeccionId)
        {
            var lista = new List<CalidadGP12ItemViewModel>();
            const string sql = @"
SELECT
    s.SolicitudGP12ID AS GP12ID,
    s.CalidadInspeccionID AS InspeccionID,
    s.CajaLiberadaID,
    s.CajaProduccionID,
    ISNULL(c.FolioCaja,N'') AS FolioCaja,
    s.FechaSolicitud AS FechaEntrada,
    CAST(ISNULL(s.CantidadSolicitada,0) AS INT) AS CantidadEntrada,
    s.Motivo,
    CASE
        WHEN s.EstatusID=1 THEN N'RECIBIDO'
        WHEN s.EstatusID=2 THEN N'PENDIENTE_PROGRAMAR'
        WHEN s.EstatusID=3 THEN N'PROGRAMADO'
        WHEN s.EstatusID=4 THEN N'ASIGNADO'
        WHEN s.EstatusID=5 THEN N'EN_INSPECCION'
        WHEN s.EstatusID=6 THEN N'INSPECCION_PAUSADA'
        WHEN s.EstatusID=7 THEN N'INSPECCION_TERMINADA'
        WHEN s.EstatusID=8 THEN N'EN_TARIMA'
        WHEN s.EstatusID=9 THEN N'SALIDA_REGISTRADA'
        WHEN s.EstatusID=10 THEN N'CERRADO'
        ELSE N'DESCONOCIDO'
    END AS Estado,
    s.FechaFin AS FechaSalida,
    CASE
        WHEN s.FechaFin IS NULL THEN NULL
        ELSE CAST(ISNULL(s.CantidadProcesada,0) AS INT)
    END AS CantidadSalida,
    s.Observaciones
FROM dbo.GP12_Solicitudes s
LEFT JOIN dbo.Calidad_CajasLiberadas c
    ON c.CajaLiberadaID=s.CajaLiberadaID
   AND c.Activo=1
WHERE s.CalidadInspeccionID=@InspeccionID
  AND UPPER(LTRIM(RTRIM(ISNULL(s.Origen,N''))))=N'CALIDAD'
  AND s.Activo=1
ORDER BY s.FechaSolicitud DESC,s.SolicitudGP12ID DESC;";
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value = inspeccionId;
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                lista.Add(new CalidadGP12ItemViewModel
                {
                    GP12ID = Convert.ToInt32(rd["GP12ID"]),
                    InspeccionID = Convert.ToInt32(rd["InspeccionID"]),
                    CajaLiberadaID = rd["CajaLiberadaID"] == DBNull.Value ? 0 : Convert.ToInt32(rd["CajaLiberadaID"]),
                    CajaProduccionID = rd["CajaProduccionID"] == DBNull.Value ? null : Convert.ToInt64(rd["CajaProduccionID"]),
                    FolioCaja = rd["FolioCaja"]?.ToString()?.Trim() ?? string.Empty,
                    FechaEntrada = Convert.ToDateTime(rd["FechaEntrada"]),
                    CantidadEntrada = Convert.ToInt32(rd["CantidadEntrada"]),
                    Motivo = rd["Motivo"] == DBNull.Value ? null : rd["Motivo"].ToString()?.Trim(),
                    Estado = rd["Estado"]?.ToString()?.Trim() ?? string.Empty,
                    FechaSalida = rd["FechaSalida"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaSalida"]),
                    CantidadSalida = rd["CantidadSalida"] == DBNull.Value ? null : Convert.ToInt32(rd["CantidadSalida"]),
                    Observaciones = rd["Observaciones"] == DBNull.Value ? null : rd["Observaciones"].ToString()?.Trim(),
                    Revisiones = new List<CalidadGP12RevisionItemViewModel>()
                });
            }
            foreach (var gp12 in lista)
                gp12.Revisiones = await CargarRevisionesGP12Async(gp12.GP12ID, cn);
            return lista;
        }

        private static async Task<List<CalidadGP12RevisionItemViewModel>> CargarRevisionesGP12Async(int solicitudGP12Id, SqlConnection cn)
        {
            var lista = new List<CalidadGP12RevisionItemViewModel>();
            const string sql = @"
SELECT
    i.InspeccionGP12ID AS RevisionGP12ID,
    ROW_NUMBER() OVER(ORDER BY ISNULL(i.FechaInicio,i.FechaCreacion),i.InspeccionGP12ID) AS NumeroRevision,
    ISNULL(i.FechaFin,ISNULL(i.FechaInicio,i.FechaCreacion)) AS FechaRevision,
    CAST(ISNULL(i.CantidadRevisada,0) AS INT) AS CantidadRevisada,
    CAST(ISNULL(i.CantidadOK,0) AS INT) AS CantidadOK,
    CAST(ISNULL(i.CantidadNOK,0) AS INT) AS CantidadNOK,
    CASE
        WHEN i.FechaFin IS NULL THEN N'PENDIENTE'
        WHEN ISNULL(i.CantidadNOK,0)=0 AND ISNULL(i.CantidadScrap,0)=0 THEN N'OK'
        ELSE N'NOK'
    END AS Resultado,
    i.Observaciones
FROM dbo.GP12_Inspecciones i
WHERE i.SolicitudGP12ID=@SolicitudGP12ID
  AND i.Activo=1
ORDER BY ISNULL(i.FechaInicio,i.FechaCreacion) DESC,i.InspeccionGP12ID DESC;";
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@SolicitudGP12ID", SqlDbType.Int).Value = solicitudGP12Id;
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                lista.Add(new CalidadGP12RevisionItemViewModel
                {
                    RevisionGP12ID = Convert.ToInt32(rd["RevisionGP12ID"]),
                    NumeroRevision = Convert.ToInt32(rd["NumeroRevision"]),
                    FechaRevision = Convert.ToDateTime(rd["FechaRevision"]),
                    CantidadRevisada = Convert.ToInt32(rd["CantidadRevisada"]),
                    CantidadOK = Convert.ToInt32(rd["CantidadOK"]),
                    CantidadNOK = Convert.ToInt32(rd["CantidadNOK"]),
                    Resultado = rd["Resultado"]?.ToString()?.Trim() ?? string.Empty,
                    Observaciones = rd["Observaciones"] == DBNull.Value ? null : rd["Observaciones"].ToString()?.Trim(),
                    Defectos = new List<CalidadGP12DefectoItemViewModel>()
                });
            }
            foreach (var revision in lista)
                revision.Defectos = await CargarDefectosRevisionGP12Async(revision.RevisionGP12ID, cn);
            return lista;
        }

        private static async Task<List<CalidadGP12DefectoItemViewModel>> CargarDefectosRevisionGP12Async(int inspeccionGP12Id, SqlConnection cn)
        {
            var lista = new List<CalidadGP12DefectoItemViewModel>();
            const string sql = @"
SELECT
    d.InspeccionDefectoID AS DefectoGP12ID,
    d.DefectoID AS CatalogoDefectoID,
    ISNULL(c.Codigo,N'') AS Codigo,
    ISNULL(c.Nombre,N'') AS Nombre,
    CAST(ISNULL(d.Cantidad,0) AS INT) AS Cantidad,
    d.Observaciones
FROM dbo.GP12_InspeccionDefectos d
INNER JOIN dbo.GP12_CatalogoDefectos c
    ON c.DefectoID=d.DefectoID
WHERE d.InspeccionGP12ID=@InspeccionGP12ID
  AND d.Activo=1
ORDER BY c.Orden,c.Codigo,d.InspeccionDefectoID;";
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@InspeccionGP12ID", SqlDbType.Int).Value = inspeccionGP12Id;
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                lista.Add(new CalidadGP12DefectoItemViewModel
                {
                    DefectoGP12ID = Convert.ToInt32(rd["DefectoGP12ID"]),
                    CatalogoDefectoID = Convert.ToInt32(rd["CatalogoDefectoID"]),
                    Codigo = rd["Codigo"]?.ToString()?.Trim() ?? string.Empty,
                    Nombre = rd["Nombre"]?.ToString()?.Trim() ?? string.Empty,
                    Cantidad = Convert.ToInt32(rd["Cantidad"]),
                    Observaciones = rd["Observaciones"] == DBNull.Value ? null : rd["Observaciones"].ToString()?.Trim()
                });
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

        private static CalidadCajaProduccionItemViewModel
            MapearCajaProduccionCalidad(SqlDataReader rd)
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
                Molde = LeerTextoCaja(rd, "Molde")
            };
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
