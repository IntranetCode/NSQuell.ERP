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

        private async Task<CajaProduccionCalidadOrigen?>
            ObtenerCajaParaDecisionAsync(
                long cajaProduccionId,
                int inspeccionId,
                SqlConnection cn,
                SqlTransaction tx)
        {
            const string sql = @"
SELECT TOP (1)
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
    ISNULL(pc.EstadoCajaID, 1) AS EstadoCajaID,
    ISNULL(pc.EstadoCajaNombre, N'Formada en Producción') AS EstadoCajaNombre,
    pc.EstatusCalidad,
    ISNULL(pc.FechaFormacion, pc.FechaCreacion) AS FechaFormacion,
    pc.UsuarioFormacionID,
    ci.Estado AS EstadoInspeccion,
    ISNULL(ci.ConfiguracionInvalidada, 0) AS ConfiguracionInvalidada,
    (
        SELECT COUNT(1)
        FROM dbo.Calidad_DisposicionesMaterial d
        WHERE d.InspeccionID = ci.InspeccionID
          AND d.Activo = 1
          AND d.ResultadoFinal = 'PENDIENTE'
    ) AS DisposicionesPendientes
FROM dbo.Produccion_Cajas pc WITH (UPDLOCK, HOLDLOCK)
INNER JOIN dbo.Calidad_Inspecciones ci
    ON ci.InspeccionID = @InspeccionID
   AND ci.EjecucionProduccionID = pc.EjecucionProduccionID
WHERE pc.CajaProduccionID = @CajaProduccionID
  AND pc.Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value =
                cajaProduccionId;
            cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value =
                inspeccionId;

            await using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
                return null;

            return new CajaProduccionCalidadOrigen
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
                EstadoCajaID = Convert.ToInt32(rd["EstadoCajaID"]),
                EstadoCajaNombre = rd["EstadoCajaNombre"]?.ToString()?.Trim() ?? string.Empty,
                EstatusCalidad = LeerTextoCaja(rd, "EstatusCalidad"),
                FechaFormacion = Convert.ToDateTime(rd["FechaFormacion"]),
                UsuarioFormacionID = LeerEnteroNullableCaja(rd, "UsuarioFormacionID"),
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

        private static async Task RegistrarEntradaGP12Async(
            CajaProduccionCalidadOrigen caja,
            int cajaLiberadaId,
            string? motivo,
            DateTime ahora,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
IF NOT EXISTS
(
    SELECT 1
    FROM dbo.Calidad_GP12 WITH (UPDLOCK, HOLDLOCK)
    WHERE CajaLiberadaID = @CajaLiberadaID
      AND Activo = 1
      AND Estado NOT IN ('CERRADO', 'CANCELADO')
)
BEGIN
    INSERT INTO dbo.Calidad_GP12
    (
        InspeccionID,
        CajaLiberadaID,
        FechaEntrada,
        CantidadEntrada,
        Motivo,
        Estado,
        UsuarioEntradaID,
        Observaciones,
        UsuarioCreacionID,
        FechaCreacion,
        Activo
    )
    VALUES
    (
        @InspeccionID,
        @CajaLiberadaID,
        @Ahora,
        @CantidadEntrada,
        @Motivo,
        'EN_INSPECCION',
        @UsuarioID,
        @Motivo,
        @UsuarioID,
        @Ahora,
        1
    );
END;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value =
                caja.InspeccionID;
            cmd.Parameters.Add("@CajaLiberadaID", SqlDbType.Int).Value =
                cajaLiberadaId;
            cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value =
                ahora;
            cmd.Parameters.Add("@CantidadEntrada", SqlDbType.Int).Value =
                caja.CantidadPiezas;
            cmd.Parameters.Add("@Motivo", SqlDbType.NVarChar, 500).Value =
                string.IsNullOrWhiteSpace(motivo)
                    ? DBNull.Value
                    : motivo.Trim();
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                usuarioId;

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

        private sealed class GP12RevisionOrigen
        {
            public int GP12ID { get; set; }
            public int InspeccionID { get; set; }
            public int CajaLiberadaID { get; set; }
            public long? CajaProduccionID { get; set; }
            public string FolioCaja { get; set; } = string.Empty;
            public int CantidadEntrada { get; set; }
            public string Estado { get; set; } = string.Empty;
            public string EstadoInspeccion { get; set; } = string.Empty;
        }

        private async Task<List<CalidadGP12ItemViewModel>>
            CargarRegistrosGP12Async(int inspeccionId)
        {
            var lista = new List<CalidadGP12ItemViewModel>();

            const string sql = @"
SELECT
    g.GP12ID,
    g.InspeccionID,
    g.CajaLiberadaID,
    c.CajaProduccionID,
    c.FolioCaja,
    g.FechaEntrada,
    g.CantidadEntrada,
    g.Motivo,
    g.Estado,
    g.FechaSalida,
    g.CantidadSalida,
    g.Observaciones
FROM dbo.Calidad_GP12 g
INNER JOIN dbo.Calidad_CajasLiberadas c
    ON c.CajaLiberadaID = g.CajaLiberadaID
WHERE g.InspeccionID = @InspeccionID
  AND g.Activo = 1
ORDER BY g.FechaEntrada DESC, g.GP12ID DESC;";

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value =
                    inspeccionId;

                await using var rd = await cmd.ExecuteReaderAsync();

                while (await rd.ReadAsync())
                {
                    lista.Add(new CalidadGP12ItemViewModel
                    {
                        GP12ID = Convert.ToInt32(rd["GP12ID"]),
                        InspeccionID = Convert.ToInt32(rd["InspeccionID"]),
                        CajaLiberadaID = Convert.ToInt32(rd["CajaLiberadaID"]),
                        CajaProduccionID = rd["CajaProduccionID"] == DBNull.Value
                            ? null
                            : Convert.ToInt64(rd["CajaProduccionID"]),
                        FolioCaja = rd["FolioCaja"]?.ToString()?.Trim() ?? string.Empty,
                        FechaEntrada = Convert.ToDateTime(rd["FechaEntrada"]),
                        CantidadEntrada = Convert.ToInt32(rd["CantidadEntrada"]),
                        Motivo = LeerTextoCaja(rd, "Motivo"),
                        Estado = rd["Estado"]?.ToString()?.Trim() ?? CalidadEstadoGP12.EnInspeccion,
                        FechaSalida = LeerFechaNullableCaja(rd, "FechaSalida"),
                        CantidadSalida = LeerEnteroNullableCaja(rd, "CantidadSalida"),
                        Observaciones = LeerTextoCaja(rd, "Observaciones")
                    });
                }
            }

            foreach (var gp12 in lista)
            {
                gp12.Revisiones = await CargarRevisionesGP12Async(
                    gp12.GP12ID,
                    cn);
            }

            return lista;
        }

        private static async Task<List<CalidadGP12RevisionItemViewModel>>
            CargarRevisionesGP12Async(
                int gp12Id,
                SqlConnection cn)
        {
            var lista = new List<CalidadGP12RevisionItemViewModel>();

            const string sql = @"
SELECT
    RevisionGP12ID,
    NumeroRevision,
    FechaRevision,
    CantidadRevisada,
    CantidadOK,
    CantidadNOK,
    Resultado,
    Observaciones
FROM dbo.Calidad_GP12_Revisiones
WHERE GP12ID = @GP12ID
  AND Activo = 1
ORDER BY NumeroRevision DESC, RevisionGP12ID DESC;";

            await using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.Add("@GP12ID", SqlDbType.Int).Value = gp12Id;
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
                        Resultado = rd["Resultado"]?.ToString()?.Trim() ?? CalidadResultadoGP12.Nok,
                        Observaciones = LeerTextoCaja(rd, "Observaciones")
                    });
                }
            }

            foreach (var revision in lista)
            {
                revision.Defectos = await CargarDefectosRevisionGP12Async(
                    revision.RevisionGP12ID,
                    cn);
            }

            return lista;
        }

        private static async Task<List<CalidadGP12DefectoItemViewModel>>
            CargarDefectosRevisionGP12Async(
                int revisionGp12Id,
                SqlConnection cn)
        {
            var lista = new List<CalidadGP12DefectoItemViewModel>();

            const string sql = @"
SELECT
    d.DefectoGP12ID,
    d.CatalogoDefectoID,
    c.Codigo,
    c.Nombre,
    d.Cantidad,
    d.Observaciones
FROM dbo.Calidad_GP12_Defectos d
INNER JOIN dbo.Calidad_CatalogoDefectos c
    ON c.CatalogoDefectoID = d.CatalogoDefectoID
WHERE d.RevisionGP12ID = @RevisionGP12ID
  AND d.Activo = 1
ORDER BY c.Codigo, d.DefectoGP12ID;";

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@RevisionGP12ID", SqlDbType.Int).Value =
                revisionGp12Id;

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
                    Observaciones = LeerTextoCaja(rd, "Observaciones")
                });
            }

            return lista;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegistrarRevisionGP12(
            CalidadGP12RevisionGuardarViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] =
                    "Revisa las cantidades capturadas para GP12.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = model.InspeccionID });
            }

            model.Observaciones = model.Observaciones?.Trim();

            var usuarioId = ObtenerUsuarioIdActual();

            if (!usuarioId.HasValue || usuarioId.Value <= 0)
                return Unauthorized();

            if (model.CantidadRevisada <= 0 ||
                model.CantidadOK < 0 ||
                model.CantidadNOK < 0 ||
                model.CantidadOK + model.CantidadNOK != model.CantidadRevisada)
            {
                TempData["Error"] =
                    "La suma de piezas OK y NOK debe ser igual a la cantidad revisada.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = model.InspeccionID });
            }

            var defectos = (model.Defectos ?? new List<CalidadGP12DefectoGuardarViewModel>())
                .Where(x => x.CatalogoDefectoID > 0 && x.Cantidad > 0)
                .ToList();

            if (model.CantidadNOK == 0 && defectos.Count > 0)
            {
                TempData["Error"] =
                    "No agregues defectos cuando la cantidad NOK es cero.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = model.InspeccionID });
            }

            if (model.CantidadNOK > 0 &&
                defectos.Sum(x => x.Cantidad) != model.CantidadNOK)
            {
                TempData["Error"] =
                    "La suma de defectos debe ser igual a la cantidad NOK.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = model.InspeccionID });
            }

            if (model.CantidadNOK > 0 &&
                string.IsNullOrWhiteSpace(model.Observaciones))
            {
                TempData["Error"] =
                    "Describe la condición detectada y las acciones requeridas cuando GP12 tenga piezas NOK.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = model.InspeccionID });
            }

            if (defectos
                .GroupBy(x => x.CatalogoDefectoID)
                .Any(x => x.Count() > 1))
            {
                TempData["Error"] =
                    "Cada defecto debe registrarse una sola vez. Agrupa la cantidad del mismo defecto en un único renglón.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = model.InspeccionID });
            }

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();

            try
            {
                var origen = await ObtenerGP12ParaRevisionAsync(
                    model.GP12ID,
                    model.InspeccionID,
                    cn,
                    tx);

                if (origen == null)
                {
                    await tx.RollbackAsync();
                    return NotFound();
                }

                if (origen.Estado != CalidadEstadoGP12.EnInspeccion &&
                    origen.Estado != CalidadEstadoGP12.NokReinspeccion)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "El registro GP12 ya no permite nuevas revisiones.";

                    return RedirectToAction(
                        nameof(Detalle),
                        new { id = model.InspeccionID });
                }

                if (model.CantidadRevisada != origen.CantidadEntrada)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        $"GP12 requiere revisar el total de la caja: {origen.CantidadEntrada:N0} pieza(s).";

                    return RedirectToAction(
                        nameof(Detalle),
                        new { id = model.InspeccionID });
                }

                foreach (var defecto in defectos)
                {
                    if (!await ExisteDefectoCatalogoAsync(
                        defecto.CatalogoDefectoID,
                        cn,
                        tx))
                    {
                        await tx.RollbackAsync();

                        TempData["Error"] =
                            "Se recibió un defecto que ya no está activo en el catálogo.";

                        return RedirectToAction(
                            nameof(Detalle),
                            new { id = model.InspeccionID });
                    }
                }

                var numeroRevision = await ObtenerSiguienteRevisionGP12Async(
                    origen.GP12ID,
                    cn,
                    tx);

                var ahora = DateTime.Now;
                var resultado = model.CantidadNOK == 0
                    ? CalidadResultadoGP12.Ok
                    : CalidadResultadoGP12.Nok;

                var revisionId = await InsertarRevisionGP12Async(
                    origen,
                    numeroRevision,
                    model,
                    resultado,
                    ahora,
                    usuarioId.Value,
                    cn,
                    tx);

                foreach (var defecto in defectos)
                {
                    await InsertarDefectoRevisionGP12Async(
                        revisionId,
                        defecto,
                        ahora,
                        usuarioId.Value,
                        cn,
                        tx);
                }

                if (resultado == CalidadResultadoGP12.Ok)
                {
                    await LiberarCajaDesdeGP12Async(
                        origen,
                        model,
                        ahora,
                        usuarioId.Value,
                        cn,
                        tx);
                }
                else
                {
                    await MarcarGP12ParaReinspeccionAsync(
                        origen,
                        model,
                        ahora,
                        usuarioId.Value,
                        cn,
                        tx);
                }

                await RegistrarHistorialRevisionGP12Async(
                    origen,
                    numeroRevision,
                    resultado,
                    model,
                    usuarioId.Value,
                    ahora,
                    cn,
                    tx);

                await tx.CommitAsync();

                TempData["Mensaje"] = resultado == CalidadResultadoGP12.Ok
                    ? "GP12 concluido. La caja fue liberada con etiqueta verde."
                    : "Revisión GP12 registrada como NOK. La caja permanece en inspección reforzada.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                TempData["Error"] =
                    "No fue posible registrar la revisión GP12: " + ex.Message;
            }

            return RedirectToAction(
                nameof(Detalle),
                new { id = model.InspeccionID });
        }

        private static async Task<GP12RevisionOrigen?> ObtenerGP12ParaRevisionAsync(
            int gp12Id,
            int inspeccionId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT TOP (1)
    g.GP12ID,
    g.InspeccionID,
    g.CajaLiberadaID,
    c.CajaProduccionID,
    c.FolioCaja,
    g.CantidadEntrada,
    g.Estado,
    i.Estado AS EstadoInspeccion
FROM dbo.Calidad_GP12 g WITH (UPDLOCK, HOLDLOCK)
INNER JOIN dbo.Calidad_CajasLiberadas c
    ON c.CajaLiberadaID = g.CajaLiberadaID
INNER JOIN dbo.Calidad_Inspecciones i
    ON i.InspeccionID = g.InspeccionID
WHERE g.GP12ID = @GP12ID
  AND g.InspeccionID = @InspeccionID
  AND g.Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@GP12ID", SqlDbType.Int).Value = gp12Id;
            cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value = inspeccionId;

            await using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
                return null;

            return new GP12RevisionOrigen
            {
                GP12ID = Convert.ToInt32(rd["GP12ID"]),
                InspeccionID = Convert.ToInt32(rd["InspeccionID"]),
                CajaLiberadaID = Convert.ToInt32(rd["CajaLiberadaID"]),
                CajaProduccionID = rd["CajaProduccionID"] == DBNull.Value
                    ? null
                    : Convert.ToInt64(rd["CajaProduccionID"]),
                FolioCaja = rd["FolioCaja"]?.ToString()?.Trim() ?? string.Empty,
                CantidadEntrada = Convert.ToInt32(rd["CantidadEntrada"]),
                Estado = rd["Estado"]?.ToString()?.Trim() ?? string.Empty,
                EstadoInspeccion = rd["EstadoInspeccion"]?.ToString()?.Trim() ?? string.Empty
            };
        }

        private static async Task<bool> ExisteDefectoCatalogoAsync(
            int catalogoDefectoId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT COUNT(1)
FROM dbo.Calidad_CatalogoDefectos
WHERE CatalogoDefectoID = @CatalogoDefectoID
  AND Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@CatalogoDefectoID", SqlDbType.Int).Value =
                catalogoDefectoId;

            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }

        private static async Task<int> ObtenerSiguienteRevisionGP12Async(
            int gp12Id,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT ISNULL(MAX(NumeroRevision), 0) + 1
FROM dbo.Calidad_GP12_Revisiones WITH (UPDLOCK, HOLDLOCK)
WHERE GP12ID = @GP12ID
  AND Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@GP12ID", SqlDbType.Int).Value = gp12Id;

            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        private static async Task<int> InsertarRevisionGP12Async(
            GP12RevisionOrigen origen,
            int numeroRevision,
            CalidadGP12RevisionGuardarViewModel model,
            string resultado,
            DateTime ahora,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
INSERT INTO dbo.Calidad_GP12_Revisiones
(
    GP12ID,
    NumeroRevision,
    FechaRevision,
    CantidadRevisada,
    CantidadOK,
    CantidadNOK,
    Resultado,
    Observaciones,
    UsuarioCalidadID,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
OUTPUT INSERTED.RevisionGP12ID
VALUES
(
    @GP12ID,
    @NumeroRevision,
    @Ahora,
    @CantidadRevisada,
    @CantidadOK,
    @CantidadNOK,
    @Resultado,
    @Observaciones,
    @UsuarioID,
    @UsuarioID,
    @Ahora,
    1
);";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@GP12ID", SqlDbType.Int).Value = origen.GP12ID;
            cmd.Parameters.Add("@NumeroRevision", SqlDbType.Int).Value = numeroRevision;
            cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahora;
            cmd.Parameters.Add("@CantidadRevisada", SqlDbType.Int).Value = model.CantidadRevisada;
            cmd.Parameters.Add("@CantidadOK", SqlDbType.Int).Value = model.CantidadOK;
            cmd.Parameters.Add("@CantidadNOK", SqlDbType.Int).Value = model.CantidadNOK;
            cmd.Parameters.Add("@Resultado", SqlDbType.VarChar, 10).Value = resultado;
            cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1000).Value =
                string.IsNullOrWhiteSpace(model.Observaciones)
                    ? DBNull.Value
                    : model.Observaciones.Trim();
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;

            var result = await cmd.ExecuteScalarAsync();

            if (result == null || result == DBNull.Value)
                throw new InvalidOperationException("No fue posible crear la revisión GP12.");

            return Convert.ToInt32(result);
        }

        private static async Task InsertarDefectoRevisionGP12Async(
            int revisionId,
            CalidadGP12DefectoGuardarViewModel defecto,
            DateTime ahora,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
INSERT INTO dbo.Calidad_GP12_Defectos
(
    RevisionGP12ID,
    CatalogoDefectoID,
    Cantidad,
    Observaciones,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
VALUES
(
    @RevisionGP12ID,
    @CatalogoDefectoID,
    @Cantidad,
    @Observaciones,
    @UsuarioID,
    @Ahora,
    1
);";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@RevisionGP12ID", SqlDbType.Int).Value = revisionId;
            cmd.Parameters.Add("@CatalogoDefectoID", SqlDbType.Int).Value = defecto.CatalogoDefectoID;
            cmd.Parameters.Add("@Cantidad", SqlDbType.Int).Value = defecto.Cantidad;
            cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 500).Value =
                string.IsNullOrWhiteSpace(defecto.Observaciones)
                    ? DBNull.Value
                    : defecto.Observaciones.Trim();
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahora;

            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task LiberarCajaDesdeGP12Async(
    GP12RevisionOrigen origen,
    CalidadGP12RevisionGuardarViewModel model,
    DateTime ahora,
    int usuarioId,
    SqlConnection cn,
    SqlTransaction tx)
        {
            if (origen == null) throw new ArgumentNullException(nameof(origen));
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (origen.GP12ID <= 0) throw new InvalidOperationException("El registro GP12 no es válido.");
            if (origen.CajaLiberadaID <= 0) throw new InvalidOperationException("La caja de Calidad relacionada con GP12 no es válida.");
            if (model.CantidadRevisada <= 0) throw new InvalidOperationException("La cantidad revisada en GP12 debe ser mayor que cero.");
            if (model.CantidadNOK > 0) throw new InvalidOperationException("No puede liberarse la caja desde GP12 mientras existan piezas NOK.");
            if (model.CantidadOK <= 0) throw new InvalidOperationException("La cantidad OK de GP12 debe ser mayor que cero.");

            const string sql = @"
DECLARE @EstadoGP12Actual NVARCHAR(50);
DECLARE @CajaProduccionActual BIGINT;
DECLARE @EstadoCajaActual INT;

SELECT
    @EstadoGP12Actual=UPPER(LTRIM(RTRIM(ISNULL(g.Estado,N''))))
FROM dbo.Calidad_GP12 g WITH (UPDLOCK,HOLDLOCK)
WHERE g.GP12ID=@GP12ID
  AND g.CajaLiberadaID=@CajaLiberadaID
  AND g.Activo=1;

IF @EstadoGP12Actual IS NULL
    THROW 51100,'No se encontró el registro GP12 activo relacionado con la caja.',1;

IF @EstadoGP12Actual=N'LIBERADO'
BEGIN
    IF @CajaProduccionID IS NULL
        RETURN;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.Produccion_Cajas c
        WHERE c.CajaProduccionID=@CajaProduccionID
          AND c.Activo=1
          AND c.EstadoCajaID IN (@EstadoZonaVerde,@EstadoSalidaProduccion,@EstadoRecibidaAlmacen)
          AND ISNULL(c.EtiquetaVerde,0)=1
          AND UPPER(LTRIM(RTRIM(ISNULL(c.ResultadoCalidad,N''))))=N'LIBERADA_GP12'
    )
        RETURN;
END;

IF @EstadoGP12Actual NOT IN (N'EN_GP12',N'NOK_REINSPECCION',N'PENDIENTE',N'ABIERTO')
    THROW 51101,'El estado actual de GP12 no permite liberar la caja.',1;

IF @CajaProduccionID IS NOT NULL
BEGIN
    SELECT
        @CajaProduccionActual=c.CajaProduccionID,
        @EstadoCajaActual=c.EstadoCajaID
    FROM dbo.Produccion_Cajas c WITH (UPDLOCK,HOLDLOCK)
    WHERE c.CajaProduccionID=@CajaProduccionID
      AND c.Activo=1;

    IF @CajaProduccionActual IS NULL
        THROW 51102,'No se encontró la caja activa de Producción relacionada con GP12.',1;

    IF @EstadoCajaActual IN (@EstadoSalidaProduccion,@EstadoRecibidaAlmacen)
        THROW 51103,'La caja ya registró salida de Producción o recepción de Almacén y no puede liberarse nuevamente desde GP12.',1;
END;

UPDATE dbo.Calidad_GP12
SET Estado=N'LIBERADO',
    FechaSalida=@Ahora,
    CantidadSalida=@CantidadSalida,
    UsuarioSalidaID=@UsuarioID,
    Observaciones=
        CASE
            WHEN @ObservacionesGP12 IS NULL THEN
                CASE
                    WHEN Observaciones IS NULL OR LTRIM(RTRIM(Observaciones))=N''
                        THEN N'GP12 aprobado. Caja entregada a Producción para escaneo de salida con destino final Almacén PT.'
                    ELSE Observaciones+CHAR(13)+CHAR(10)+N'GP12 aprobado. Caja entregada a Producción para escaneo de salida con destino final Almacén PT.'
                END
            WHEN Observaciones IS NULL OR LTRIM(RTRIM(Observaciones))=N''
                THEN @ObservacionesGP12
            ELSE Observaciones+CHAR(13)+CHAR(10)+@ObservacionesGP12
        END,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=@Ahora
WHERE GP12ID=@GP12ID
  AND CajaLiberadaID=@CajaLiberadaID
  AND Activo=1;

IF @@ROWCOUNT<>1
    THROW 51104,'No fue posible registrar la liberación de GP12.',1;

UPDATE dbo.Calidad_CajasLiberadas
SET EtiquetaLiberacion=N'VERDE',
    Destino=N'ALMACEN',
    Estado=N'LIBERADA',
    FechaValidacionCalidad=@Ahora,
    UsuarioValidacionCalidadID=@UsuarioID,
    Observaciones=
        CASE
            WHEN @ObservacionesCaja IS NULL THEN
                CASE
                    WHEN Observaciones IS NULL OR LTRIM(RTRIM(Observaciones))=N''
                        THEN N'Caja aprobada por GP12. Destino final Almacén PT, pendiente escaneo de salida en Producción.'
                    ELSE Observaciones+CHAR(13)+CHAR(10)+N'Caja aprobada por GP12. Destino final Almacén PT, pendiente escaneo de salida en Producción.'
                END
            WHEN Observaciones IS NULL OR LTRIM(RTRIM(Observaciones))=N''
                THEN @ObservacionesCaja
            ELSE Observaciones+CHAR(13)+CHAR(10)+@ObservacionesCaja
        END,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=@Ahora
WHERE CajaLiberadaID=@CajaLiberadaID
  AND Activo=1;

IF @@ROWCOUNT<>1
    THROW 51105,'No fue posible actualizar la caja liberada de Calidad.',1;

IF @CajaProduccionID IS NOT NULL
BEGIN
    UPDATE dbo.Produccion_Cajas
    SET EstadoCajaID=@EstadoZonaVerde,
        EstadoCajaNombre=@NombreEstadoZonaVerde,
        EstatusCalidad=N'LIBERADA',
        EtiquetaVerde=1,
        FechaLiberacionCalidad=@Ahora,
        AuditorCalidadUsuarioID=@UsuarioID,
        UsuarioCalidadID=@UsuarioID,
        ResultadoCalidad=N'LIBERADA_GP12',
        MotivoCalidad=@ObservacionesGP12,
        FechaSalidaProduccion=NULL,
        UsuarioSalidaProduccionID=NULL,
        FechaRecepcionAlmacen=NULL,
        UsuarioAlmacenID=NULL,
        UsuarioModificacionID=@UsuarioID,
        FechaModificacion=@Ahora
    WHERE CajaProduccionID=@CajaProduccionID
      AND Activo=1
      AND EstadoCajaID NOT IN (@EstadoSalidaProduccion,@EstadoRecibidaAlmacen);

    IF @@ROWCOUNT<>1
        THROW 51106,'No fue posible colocar la caja liberada por GP12 en la zona de escaneo de Producción.',1;
END;

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
    N'GP12_ENTREGADO_PARA_SALIDA',
    ci.Estado,
    ci.Estado,
    N'LIBERADA_GP12',
    N'VERDE',
    CONCAT(
        N'GP12 aprobó la caja ',
        ISNULL(NULLIF(LTRIM(RTRIM(@FolioCaja)),N''),CONVERT(NVARCHAR(30),@CajaProduccionID)),
        N'. Cantidad liberada: ',
        CONVERT(NVARCHAR(20),@CantidadSalida),
        N' pieza(s). La caja quedó disponible en Producción para escaneo de salida con destino final Almacén PT.'
    ),
    @UsuarioID,
    @Ahora
FROM dbo.Calidad_Inspecciones ci
WHERE ci.InspeccionID=@InspeccionID
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.Calidad_InspeccionHistorial h
      WHERE h.InspeccionID=@InspeccionID
        AND h.Movimiento=N'GP12_ENTREGADO_PARA_SALIDA'
        AND h.Comentario LIKE N'%'+ISNULL(NULLIF(LTRIM(RTRIM(@FolioCaja)),N''),CONVERT(NVARCHAR(30),@CajaProduccionID))+N'%'
  );";

            var observacionBase = "GP12 aprobado. Caja entregada a Producción para escaneo de salida con destino final Almacén PT.";
            var observacionesGp12 = string.IsNullOrWhiteSpace(model.Observaciones)
                ? observacionBase
                : observacionBase + " " + model.Observaciones.Trim();

            if (observacionesGp12.Length > 1000) observacionesGp12 = observacionesGp12[..1000];

            var observacionesCaja = $"Caja liberada por GP12 con {model.CantidadOK} pieza(s) OK. Pendiente escaneo de salida en Producción. Destino final: Almacén PT.";
            if (!string.IsNullOrWhiteSpace(model.Observaciones)) observacionesCaja += " " + model.Observaciones.Trim();
            if (observacionesCaja.Length > 1000) observacionesCaja = observacionesCaja[..1000];

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@GP12ID", SqlDbType.Int).Value = origen.GP12ID;
            cmd.Parameters.Add("@CajaLiberadaID", SqlDbType.Int).Value = origen.CajaLiberadaID;
            cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value = (object?)origen.CajaProduccionID ?? DBNull.Value;
            cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value = origen.InspeccionID;
            cmd.Parameters.Add("@FolioCaja", SqlDbType.NVarChar, 100).Value = string.IsNullOrWhiteSpace(origen.FolioCaja) ? DBNull.Value : origen.FolioCaja.Trim();
            cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahora;
            cmd.Parameters.Add("@CantidadSalida", SqlDbType.Int).Value = model.CantidadOK;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            cmd.Parameters.Add("@ObservacionesGP12", SqlDbType.NVarChar, 1000).Value = observacionesGp12;
            cmd.Parameters.Add("@ObservacionesCaja", SqlDbType.NVarChar, 1000).Value = observacionesCaja;
            cmd.Parameters.Add("@EstadoZonaVerde", SqlDbType.Int).Value = ProduccionCajaEstatus.ZonaVerde;
            cmd.Parameters.Add("@NombreEstadoZonaVerde", SqlDbType.NVarChar, 100).Value = ProduccionCajaEstatus.Nombre(ProduccionCajaEstatus.ZonaVerde);
            cmd.Parameters.Add("@EstadoSalidaProduccion", SqlDbType.Int).Value = ProduccionCajaEstatus.SalidaProduccion;
            cmd.Parameters.Add("@EstadoRecibidaAlmacen", SqlDbType.Int).Value = ProduccionCajaEstatus.RecibidaAlmacenPt;
            await cmd.ExecuteNonQueryAsync();
        }
        private static async Task MarcarGP12ParaReinspeccionAsync(
            GP12RevisionOrigen origen,
            CalidadGP12RevisionGuardarViewModel model,
            DateTime ahora,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
UPDATE dbo.Calidad_GP12
SET
    Estado = 'NOK_REINSPECCION',
    Observaciones =
        CASE
            WHEN @Observaciones IS NULL THEN Observaciones
            WHEN Observaciones IS NULL OR LTRIM(RTRIM(Observaciones)) = '' THEN @Observaciones
            ELSE Observaciones + CHAR(13) + CHAR(10) + @Observaciones
        END,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = @Ahora
WHERE GP12ID = @GP12ID
  AND Activo = 1;

IF @CajaProduccionID IS NOT NULL
BEGIN
    UPDATE dbo.Produccion_Cajas
    SET
        EstadoCajaID = 4,
        EstadoCajaNombre = N'GP12 - requiere reinspección',
        EstatusCalidad = N'GP12',
        EtiquetaVerde = 0,
        ResultadoCalidad = N'GP12_NOK',
        MotivoCalidad = @Observaciones,
        UsuarioModificacionID = @UsuarioID,
        FechaModificacion = @Ahora
    WHERE CajaProduccionID = @CajaProduccionID
      AND Activo = 1;
END;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@GP12ID", SqlDbType.Int).Value = origen.GP12ID;
            cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value =
                (object?)origen.CajaProduccionID ?? DBNull.Value;
            cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahora;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1000).Value =
                string.IsNullOrWhiteSpace(model.Observaciones)
                    ? "GP12 con piezas NOK; requiere corrección y nueva inspección."
                    : model.Observaciones.Trim();

            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task RegistrarHistorialRevisionGP12Async(
            GP12RevisionOrigen origen,
            int numeroRevision,
            string resultado,
            CalidadGP12RevisionGuardarViewModel model,
            int usuarioId,
            DateTime ahora,
            SqlConnection cn,
            SqlTransaction tx)
        {
            var movimiento = resultado == CalidadResultadoGP12.Ok
                ? "GP12_LIBERADO"
                : "GP12_REVISION_NOK";

            var comentario =
                $"GP12 caja {origen.FolioCaja}, revisión {numeroRevision}. " +
                $"Revisadas: {model.CantidadRevisada}; OK: {model.CantidadOK}; NOK: {model.CantidadNOK}. " +
                model.Observaciones;

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
    @Resultado,
    @Etiqueta,
    @Comentario,
    @UsuarioID,
    @Ahora
);";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value = origen.InspeccionID;
            cmd.Parameters.Add("@Movimiento", SqlDbType.NVarChar, 100).Value = movimiento;
            cmd.Parameters.Add("@EstadoInspeccion", SqlDbType.NVarChar, 50).Value = origen.EstadoInspeccion;
            cmd.Parameters.Add("@Resultado", SqlDbType.NVarChar, 30).Value = resultado;
            cmd.Parameters.Add("@Etiqueta", SqlDbType.NVarChar, 30).Value =
                resultado == CalidadResultadoGP12.Ok ? "VERDE" : "AMARILLA";
            cmd.Parameters.Add("@Comentario", SqlDbType.NVarChar, 1000).Value = comentario;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahora;

            await cmd.ExecuteNonQueryAsync();
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
