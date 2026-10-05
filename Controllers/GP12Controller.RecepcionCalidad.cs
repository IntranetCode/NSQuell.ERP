using ERP.NSQuell.Models;
using ERP.NSQuell.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace ERP.NSQuell.Controllers
{
    // NSQ_GP12_RECEPCION_CALIDAD_V1_3
    public partial class GP12Controller
    {
        private async Task<GP12RecepcionCalidadVm>
            CargarRecepcionCalidadRapidaAsync()
        {
            var vm =
                new GP12RecepcionCalidadVm();

            await using var cn =
                new SqlConnection(ConnectionString);

            await cn.OpenAsync();

            const string sql = @"
SELECT TOP (150)
    c.CalidadCajaInventarioID,
    c.CodigoBarras,
    c.NumeroOF,
    COALESCE(
        NULLIF(LTRIM(RTRIM(p.NumeroParte)),N''),
        c.NumeroParteEtiqueta
    ) AS NumeroParte,
    COALESCE(
        NULLIF(LTRIM(RTRIM(c.DesignacionEtiqueta)),N''),
        NULLIF(LTRIM(RTRIM(p.Designacion)),N''),
        NULLIF(LTRIM(RTRIM(p.Descripcion)),N''),
        N''
    ) AS Designacion,
    c.Cantidad,
    c.LoteEtiqueta,
    c.Estado,
    c.FechaClasificacion,
    c.FechaRecepcionGP12,
    s.SolicitudGP12ID,
    ISNULL(s.CantidadRecibida,0) AS CantidadRecibida,
    ISNULL(s.CantidadProcesada,0) AS CantidadProcesada,
    CASE
        WHEN EXISTS
        (
            SELECT 1
            FROM dbo.GP12_Programacion pr
            WHERE pr.SolicitudGP12ID=s.SolicitudGP12ID
              AND pr.Activo=1
        )
        OR EXISTS
        (
            SELECT 1
            FROM dbo.GP12_Asignaciones a
            WHERE a.SolicitudGP12ID=s.SolicitudGP12ID
              AND a.Activo=1
        )
        OR EXISTS
        (
            SELECT 1
            FROM dbo.GP12_Inspecciones i
            WHERE i.SolicitudGP12ID=s.SolicitudGP12ID
              AND i.Activo=1
        )
        THEN 1 ELSE 0
    END AS TieneActividad
FROM dbo.Calidad_CajasInventarioLibre c
INNER JOIN dbo.GP12_Solicitudes s
    ON s.CalidadCajaInventarioID=c.CalidadCajaInventarioID
   AND s.SolicitudGP12ID=c.GP12SolicitudID
   AND s.Activo=1
INNER JOIN dbo.ERP_Partes p
    ON p.ParteID=c.ParteID
WHERE c.Activo=1
  AND c.TipoEtiqueta=N'AMARILLA'
  AND c.RequiereGP12=1
  AND c.Estado IN (N'PENDIENTE_GP12',N'EN_GP12')
ORDER BY
    CASE WHEN c.Estado=N'PENDIENTE_GP12' THEN 0 ELSE 1 END,
    c.FechaClasificacion,
    c.CalidadCajaInventarioID;";

            await using var cmd =
                new SqlCommand(sql, cn);

            await using var rd =
                await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                vm.Items.Add(
                    new GP12RecepcionCalidadItemVm
                    {
                        CalidadCajaInventarioID =
                            Convert.ToInt64(
                                rd["CalidadCajaInventarioID"]),

                        SolicitudGP12ID =
                            Convert.ToInt32(
                                rd["SolicitudGP12ID"]),

                        CodigoBarras =
                            rd["CodigoBarras"]?.ToString()?.Trim()
                            ?? string.Empty,

                        NumeroOF =
                            rd["NumeroOF"] == DBNull.Value
                                ? null
                                : rd["NumeroOF"]?.ToString()?.Trim(),

                        NumeroParte =
                            rd["NumeroParte"]?.ToString()?.Trim()
                            ?? string.Empty,

                        Designacion =
                            rd["Designacion"]?.ToString()?.Trim()
                            ?? string.Empty,

                        Cantidad =
                            Convert.ToInt32(
                                rd["Cantidad"]),

                        Lote =
                            rd["LoteEtiqueta"] == DBNull.Value
                                ? null
                                : rd["LoteEtiqueta"]?.ToString()?.Trim(),

                        Estado =
                            rd["Estado"]?.ToString()?.Trim()
                            ?? string.Empty,

                        FechaClasificacion =
                            rd["FechaClasificacion"] == DBNull.Value
                                ? null
                                : Convert.ToDateTime(
                                    rd["FechaClasificacion"]),

                        FechaRecepcionGP12 =
                            rd["FechaRecepcionGP12"] == DBNull.Value
                                ? null
                                : Convert.ToDateTime(
                                    rd["FechaRecepcionGP12"]),

                        CantidadRecibida =
                            Convert.ToDecimal(
                                rd["CantidadRecibida"]),

                        CantidadProcesada =
                            Convert.ToDecimal(
                                rd["CantidadProcesada"]),

                        TieneActividad =
                            Convert.ToBoolean(
                                rd["TieneActividad"])
                    });
            }

            return vm;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult>
            RecibirCajaCalidadEscaneada(
                string? codigoBarras)
        {
            var codigo =
                codigoBarras?.Trim()
                ?? string.Empty;

            if (string.IsNullOrWhiteSpace(codigo))
            {
                TempData["Error"] =
                    "Escanea la etiqueta fisica de la caja.";

                return RedirectToAction(nameof(Index));
            }

            if (codigo.Length > 120)
            {
                TempData["Error"] =
                    "La etiqueta excede la longitud permitida.";

                return RedirectToAction(nameof(Index));
            }

            var usuarioID =
                ObtenerUsuarioIdActual();

            if (!usuarioID.HasValue
                || usuarioID.Value <= 0)
            {
                return Unauthorized();
            }

            await using var cn =
                new SqlConnection(ConnectionString);

            await cn.OpenAsync();

            await using var tx =
                (SqlTransaction)
                await cn.BeginTransactionAsync(
                    IsolationLevel.Serializable);

            try
            {
                const string sqlCaja = @"
SELECT TOP (1)
    c.CalidadCajaInventarioID,
    c.Estado,
    c.TipoEtiqueta,
    ISNULL(c.RequiereGP12,0) AS RequiereGP12,
    c.Cantidad,
    c.GP12SolicitudID,
    c.FechaRecepcionGP12,
    s.SolicitudGP12ID,
    s.EstatusID,
    ISNULL(s.CantidadSolicitada,0) AS CantidadSolicitada,
    ISNULL(s.CantidadRecibida,0) AS CantidadRecibida,
    ISNULL(s.CantidadProcesada,0) AS CantidadProcesada,
    s.FechaRecepcion,
    e.SolicitudEtiquetaID,
    e.TipoEtiqueta AS TipoEtiquetaGP12,
    ISNULL(e.CantidadSolicitada,0) AS EtiquetaSolicitada,
    ISNULL(e.CantidadRecibida,0) AS EtiquetaRecibida
FROM dbo.Calidad_CajasInventarioLibre c WITH(UPDLOCK,HOLDLOCK)
INNER JOIN dbo.GP12_Solicitudes s WITH(UPDLOCK,HOLDLOCK)
    ON s.CalidadCajaInventarioID=c.CalidadCajaInventarioID
   AND s.SolicitudGP12ID=c.GP12SolicitudID
   AND s.Activo=1
INNER JOIN dbo.GP12_SolicitudEtiquetas e WITH(UPDLOCK,HOLDLOCK)
    ON e.SolicitudGP12ID=s.SolicitudGP12ID
   AND e.Activo=1
WHERE c.Activo=1
  AND c.CodigoBarras=@Codigo
ORDER BY
    CASE WHEN e.TipoEtiqueta=@Amarilla THEN 0 ELSE 1 END,
    e.SolicitudEtiquetaID;";

                long calidadCajaID;
                string estadoCaja;
                string tipoEtiquetaCalidad;
                bool requiereGP12;
                int cantidadCaja;
                int solicitudGP12ID;
                int estatusAnterior;
                decimal cantidadSolicitada;
                decimal cantidadRecibida;
                decimal cantidadProcesada;
                DateTime? fechaRecepcionSolicitud;
                DateTime? fechaRecepcionCaja;
                int solicitudEtiquetaID;
                string tipoEtiquetaGP12;
                decimal etiquetaSolicitada;
                decimal etiquetaRecibida;

                await using (var cmd =
                    new SqlCommand(
                        sqlCaja,
                        cn,
                        tx))
                {
                    cmd.Parameters.Add(
                        "@Codigo",
                        SqlDbType.NVarChar,
                        120).Value =
                        codigo;

                    cmd.Parameters.Add(
                        "@Amarilla",
                        SqlDbType.VarChar,
                        20).Value =
                        GP12TipoEtiqueta.Amarilla;

                    await using var rd =
                        await cmd.ExecuteReaderAsync();

                    if (!await rd.ReadAsync())
                    {
                        await tx.RollbackAsync();

                        TempData["Error"] =
                            "La etiqueta no corresponde a una caja AMARILLA enviada por Calidad a GP12.";

                        return RedirectToAction(nameof(Index));
                    }

                    calidadCajaID =
                        Convert.ToInt64(
                            rd["CalidadCajaInventarioID"]);

                    estadoCaja =
                        rd["Estado"]?.ToString()?.Trim()
                        ?? string.Empty;

                    tipoEtiquetaCalidad =
                        rd["TipoEtiqueta"]?.ToString()?.Trim()
                        ?? string.Empty;

                    requiereGP12 =
                        Convert.ToBoolean(
                            rd["RequiereGP12"]);

                    cantidadCaja =
                        Convert.ToInt32(
                            rd["Cantidad"]);

                    solicitudGP12ID =
                        Convert.ToInt32(
                            rd["SolicitudGP12ID"]);

                    estatusAnterior =
                        Convert.ToInt32(
                            rd["EstatusID"]);

                    cantidadSolicitada =
                        Convert.ToDecimal(
                            rd["CantidadSolicitada"]);

                    cantidadRecibida =
                        Convert.ToDecimal(
                            rd["CantidadRecibida"]);

                    cantidadProcesada =
                        Convert.ToDecimal(
                            rd["CantidadProcesada"]);

                    fechaRecepcionSolicitud =
                        rd["FechaRecepcion"] == DBNull.Value
                            ? null
                            : Convert.ToDateTime(
                                rd["FechaRecepcion"]);

                    fechaRecepcionCaja =
                        rd["FechaRecepcionGP12"] == DBNull.Value
                            ? null
                            : Convert.ToDateTime(
                                rd["FechaRecepcionGP12"]);

                    solicitudEtiquetaID =
                        Convert.ToInt32(
                            rd["SolicitudEtiquetaID"]);

                    tipoEtiquetaGP12 =
                        rd["TipoEtiquetaGP12"]?.ToString()?.Trim()
                        ?? string.Empty;

                    etiquetaSolicitada =
                        Convert.ToDecimal(
                            rd["EtiquetaSolicitada"]);

                    etiquetaRecibida =
                        Convert.ToDecimal(
                            rd["EtiquetaRecibida"]);
                }

                if (string.Equals(
                        estadoCaja,
                        "EN_GP12",
                        StringComparison.OrdinalIgnoreCase)
                    || fechaRecepcionCaja.HasValue
                    || fechaRecepcionSolicitud.HasValue
                    || cantidadRecibida > 0
                    || etiquetaRecibida > 0)
                {
                    await tx.RollbackAsync();

                    TempData["Mensaje"] =
                        $"La caja ya fue recibida fisicamente en GP12. Solicitud #{solicitudGP12ID}.";

                    return RedirectToAction(nameof(Index));
                }

                if (!string.Equals(
                        estadoCaja,
                        "PENDIENTE_GP12",
                        StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(
                        tipoEtiquetaCalidad,
                        "AMARILLA",
                        StringComparison.OrdinalIgnoreCase)
                    || !requiereGP12)
                {
                    throw new InvalidOperationException(
                        "La caja ya no esta pendiente de recepcion GP12.");
                }

                if (GP12Estatus.EsFinal(estatusAnterior))
                {
                    throw new InvalidOperationException(
                        "La solicitud GP12 esta cerrada o cancelada.");
                }

                if (!string.Equals(
                        tipoEtiquetaGP12,
                        GP12TipoEtiqueta.Amarilla,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "La solicitud GP12 no esta configurada como etiqueta amarilla.");
                }

                if (cantidadCaja <= 0
                    || cantidadSolicitada != cantidadCaja
                    || etiquetaSolicitada != cantidadCaja)
                {
                    throw new InvalidOperationException(
                        $"La cantidad fisica de la caja ({cantidadCaja:N0}) no coincide con la solicitud GP12.");
                }

                var ahora = DateTime.Now;

                const string sqlEtiqueta = @"
UPDATE dbo.GP12_SolicitudEtiquetas
SET
    CantidadRecibida=@Cantidad,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=@Ahora
WHERE SolicitudEtiquetaID=@SolicitudEtiquetaID
  AND SolicitudGP12ID=@SolicitudGP12ID
  AND Activo=1
  AND ISNULL(CantidadRecibida,0)=0;

IF @@ROWCOUNT<>1
    THROW 51901,'La clasificacion GP12 ya fue recibida o cambio durante la operacion.',1;";

                await using (var cmd =
                    new SqlCommand(
                        sqlEtiqueta,
                        cn,
                        tx))
                {
                    var cantidad =
                        cmd.Parameters.Add(
                            "@Cantidad",
                            SqlDbType.Decimal);

                    cantidad.Precision = 18;
                    cantidad.Scale = 4;
                    cantidad.Value = cantidadCaja;

                    cmd.Parameters.Add(
                        "@UsuarioID",
                        SqlDbType.Int).Value =
                        usuarioID.Value;

                    cmd.Parameters.Add(
                        "@Ahora",
                        SqlDbType.DateTime2).Value =
                        ahora;

                    cmd.Parameters.Add(
                        "@SolicitudEtiquetaID",
                        SqlDbType.Int).Value =
                        solicitudEtiquetaID;

                    cmd.Parameters.Add(
                        "@SolicitudGP12ID",
                        SqlDbType.Int).Value =
                        solicitudGP12ID;

                    await cmd.ExecuteNonQueryAsync();
                }

                const string sqlMovimiento = @"
INSERT INTO dbo.GP12_InventarioMovimientos
(
    SolicitudGP12ID,
    SolicitudEtiquetaID,
    TipoMovimiento,
    Cantidad,
    CajaID,
    TarimaID,
    Referencia,
    Observaciones,
    FechaMovimiento,
    UsuarioID,
    Activo
)
VALUES
(
    @SolicitudGP12ID,
    @SolicitudEtiquetaID,
    N'ENTRADA',
    @Cantidad,
    NULL,
    NULL,
    @Referencia,
    @Observaciones,
    @Ahora,
    @UsuarioID,
    1
);";

                await using (var cmd =
                    new SqlCommand(
                        sqlMovimiento,
                        cn,
                        tx))
                {
                    cmd.Parameters.Add(
                        "@SolicitudGP12ID",
                        SqlDbType.Int).Value =
                        solicitudGP12ID;

                    cmd.Parameters.Add(
                        "@SolicitudEtiquetaID",
                        SqlDbType.Int).Value =
                        solicitudEtiquetaID;

                    var cantidad =
                        cmd.Parameters.Add(
                            "@Cantidad",
                            SqlDbType.Decimal);

                    cantidad.Precision = 18;
                    cantidad.Scale = 4;
                    cantidad.Value = cantidadCaja;

                    cmd.Parameters.Add(
                        "@Referencia",
                        SqlDbType.NVarChar,
                        250).Value =
                        codigo;

                    cmd.Parameters.Add(
                        "@Observaciones",
                        SqlDbType.NVarChar,
                        1000).Value =
                        $"Caja de Calidad #{calidadCajaID} recibida fisicamente mediante scanner.";

                    cmd.Parameters.Add(
                        "@Ahora",
                        SqlDbType.DateTime2).Value =
                        ahora;

                    cmd.Parameters.Add(
                        "@UsuarioID",
                        SqlDbType.Int).Value =
                        usuarioID.Value;

                    await cmd.ExecuteNonQueryAsync();
                }

                var pendiente =
                    Math.Max(
                        0m,
                        Convert.ToDecimal(cantidadCaja)
                        - cantidadProcesada);

                const string sqlSolicitud = @"
UPDATE dbo.GP12_Solicitudes
SET
    CantidadRecibida=@Cantidad,
    CantidadPendiente=@Pendiente,
    FechaRecepcion=@Ahora,
    UsuarioRecepcionID=@UsuarioID,
    EstatusID=@EstatusID,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=@Ahora
WHERE SolicitudGP12ID=@SolicitudGP12ID
  AND Activo=1
  AND CalidadCajaInventarioID=@CalidadCajaInventarioID
  AND FechaRecepcion IS NULL
  AND ISNULL(CantidadRecibida,0)=0;

IF @@ROWCOUNT<>1
    THROW 51902,'La solicitud GP12 ya fue recibida o cambio durante la operacion.',1;";

                await using (var cmd =
                    new SqlCommand(
                        sqlSolicitud,
                        cn,
                        tx))
                {
                    var cantidad =
                        cmd.Parameters.Add(
                            "@Cantidad",
                            SqlDbType.Decimal);

                    cantidad.Precision = 18;
                    cantidad.Scale = 4;
                    cantidad.Value = cantidadCaja;

                    var pendienteParam =
                        cmd.Parameters.Add(
                            "@Pendiente",
                            SqlDbType.Decimal);

                    pendienteParam.Precision = 18;
                    pendienteParam.Scale = 4;
                    pendienteParam.Value = pendiente;

                    cmd.Parameters.Add(
                        "@Ahora",
                        SqlDbType.DateTime2).Value =
                        ahora;

                    cmd.Parameters.Add(
                        "@UsuarioID",
                        SqlDbType.Int).Value =
                        usuarioID.Value;

                    cmd.Parameters.Add(
                        "@EstatusID",
                        SqlDbType.Int).Value =
                        GP12Estatus.PendienteProgramar;

                    cmd.Parameters.Add(
                        "@SolicitudGP12ID",
                        SqlDbType.Int).Value =
                        solicitudGP12ID;

                    cmd.Parameters.Add(
                        "@CalidadCajaInventarioID",
                        SqlDbType.BigInt).Value =
                        calidadCajaID;

                    await cmd.ExecuteNonQueryAsync();
                }

                const string sqlCalidad = @"
UPDATE dbo.Calidad_CajasInventarioLibre
SET
    Estado=N'EN_GP12',
    FechaRecepcionGP12=@Ahora,
    UsuarioRecepcionGP12ID=@UsuarioID,
    FechaModificacion=@Ahora
WHERE CalidadCajaInventarioID=@CalidadCajaInventarioID
  AND Activo=1
  AND Estado=N'PENDIENTE_GP12'
  AND GP12SolicitudID=@SolicitudGP12ID;

IF @@ROWCOUNT<>1
    THROW 51903,'La caja de Calidad cambio de estado durante la recepcion GP12.',1;";

                await using (var cmd =
                    new SqlCommand(
                        sqlCalidad,
                        cn,
                        tx))
                {
                    cmd.Parameters.Add(
                        "@Ahora",
                        SqlDbType.DateTime2).Value =
                        ahora;

                    cmd.Parameters.Add(
                        "@UsuarioID",
                        SqlDbType.Int).Value =
                        usuarioID.Value;

                    cmd.Parameters.Add(
                        "@CalidadCajaInventarioID",
                        SqlDbType.BigInt).Value =
                        calidadCajaID;

                    cmd.Parameters.Add(
                        "@SolicitudGP12ID",
                        SqlDbType.Int).Value =
                        solicitudGP12ID;

                    await cmd.ExecuteNonQueryAsync();
                }

                await AgregarHistorialAsync(
                    cn,
                    tx,
                    solicitudGP12ID,
                    GP12Movimientos.MaterialRecibido,
                    estatusAnterior,
                    GP12Estatus.PendienteProgramar,
                    GP12EntidadHistorial.Solicitud,
                    solicitudGP12ID,
                    $"Caja de Calidad #{calidadCajaID} recibida fisicamente en GP12 mediante scanner. Cantidad: {cantidadCaja:N0}.",
                    usuarioID.Value);

                await tx.CommitAsync();

                TempData["Mensaje"] =
                    $"Caja recibida en GP12. Solicitud #{solicitudGP12ID}; {cantidadCaja:N0} pieza(s).";
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
                    "No fue posible recibir la caja en GP12: "
                    + ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult>
            IniciarRevisionCalidad(
                int solicitudGP12ID)
        {
            if (solicitudGP12ID <= 0)
                return NotFound();

            var usuarioID =
                ObtenerUsuarioIdActual();

            if (!usuarioID.HasValue
                || usuarioID.Value <= 0)
            {
                return Unauthorized();
            }

            await using var cn =
                new SqlConnection(ConnectionString);

            await cn.OpenAsync();

            await using var tx =
                (SqlTransaction)
                await cn.BeginTransactionAsync(
                    IsolationLevel.Serializable);

            try
            {
                const string sqlPersona = @"
SELECT TOP (1) PersonaID
FROM dbo.Usuarios
WHERE UsuarioID=@UsuarioID
  AND ISNULL(Activo,1)=1;";

                int personaID;

                await using (var cmd =
                    new SqlCommand(
                        sqlPersona,
                        cn,
                        tx))
                {
                    cmd.Parameters.Add(
                        "@UsuarioID",
                        SqlDbType.Int).Value =
                        usuarioID.Value;

                    var result =
                        await cmd.ExecuteScalarAsync();

                    if (result == null
                        || result == DBNull.Value)
                    {
                        throw new InvalidOperationException(
                            "El usuario actual no tiene PersonaID asociado y no puede iniciar la revision GP12.");
                    }

                    personaID =
                        Convert.ToInt32(result);
                }

                const string sqlSolicitud = @"
SELECT TOP (1)
    s.EstatusID,
    ISNULL(s.CantidadRecibida,0) AS CantidadRecibida,
    ISNULL(s.CantidadProcesada,0) AS CantidadProcesada,
    e.SolicitudEtiquetaID,
    e.TipoEtiqueta
FROM dbo.GP12_Solicitudes s WITH(UPDLOCK,HOLDLOCK)
INNER JOIN dbo.Calidad_CajasInventarioLibre c WITH(UPDLOCK,HOLDLOCK)
    ON c.CalidadCajaInventarioID=s.CalidadCajaInventarioID
   AND c.GP12SolicitudID=s.SolicitudGP12ID
   AND c.Activo=1
INNER JOIN dbo.GP12_SolicitudEtiquetas e WITH(UPDLOCK,HOLDLOCK)
    ON e.SolicitudGP12ID=s.SolicitudGP12ID
   AND e.Activo=1
WHERE s.SolicitudGP12ID=@SolicitudGP12ID
  AND s.Activo=1
  AND c.Estado=N'EN_GP12'
  AND c.TipoEtiqueta=N'AMARILLA'
  AND c.RequiereGP12=1
ORDER BY
    CASE WHEN e.TipoEtiqueta=@Amarilla THEN 0 ELSE 1 END,
    e.SolicitudEtiquetaID;";

                int estatusAnterior;
                decimal cantidadRecibida;
                decimal cantidadProcesada;
                int solicitudEtiquetaID;
                string tipoEtiqueta;

                await using (var cmd =
                    new SqlCommand(
                        sqlSolicitud,
                        cn,
                        tx))
                {
                    cmd.Parameters.Add(
                        "@SolicitudGP12ID",
                        SqlDbType.Int).Value =
                        solicitudGP12ID;

                    cmd.Parameters.Add(
                        "@Amarilla",
                        SqlDbType.VarChar,
                        20).Value =
                        GP12TipoEtiqueta.Amarilla;

                    await using var rd =
                        await cmd.ExecuteReaderAsync();

                    if (!await rd.ReadAsync())
                    {
                        throw new InvalidOperationException(
                            "La solicitud no corresponde a una caja recibida fisicamente desde Calidad.");
                    }

                    estatusAnterior =
                        Convert.ToInt32(
                            rd["EstatusID"]);

                    cantidadRecibida =
                        Convert.ToDecimal(
                            rd["CantidadRecibida"]);

                    cantidadProcesada =
                        Convert.ToDecimal(
                            rd["CantidadProcesada"]);

                    solicitudEtiquetaID =
                        Convert.ToInt32(
                            rd["SolicitudEtiquetaID"]);

                    tipoEtiqueta =
                        rd["TipoEtiqueta"]?.ToString()?.Trim()
                        ?? string.Empty;
                }

                if (GP12Estatus.EsFinal(estatusAnterior))
                {
                    throw new InvalidOperationException(
                        "La solicitud GP12 ya esta cerrada o cancelada.");
                }

                if (!string.Equals(
                        tipoEtiqueta,
                        GP12TipoEtiqueta.Amarilla,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "La caja no tiene clasificacion AMARILLA en GP12.");
                }

                var pendiente =
                    cantidadRecibida
                    - cantidadProcesada;

                if (pendiente <= 0)
                {
                    throw new InvalidOperationException(
                        "La caja ya no tiene piezas pendientes de revisar.");
                }

                const string sqlActividad = @"
SELECT
    CASE WHEN
        EXISTS
        (
            SELECT 1
            FROM dbo.GP12_Programacion
            WHERE SolicitudGP12ID=@SolicitudGP12ID
              AND Activo=1
        )
        OR EXISTS
        (
            SELECT 1
            FROM dbo.GP12_Asignaciones
            WHERE SolicitudGP12ID=@SolicitudGP12ID
              AND Activo=1
        )
        OR EXISTS
        (
            SELECT 1
            FROM dbo.GP12_Inspecciones
            WHERE SolicitudGP12ID=@SolicitudGP12ID
              AND Activo=1
        )
    THEN 1 ELSE 0 END;";

                await using (var cmd =
                    new SqlCommand(
                        sqlActividad,
                        cn,
                        tx))
                {
                    cmd.Parameters.Add(
                        "@SolicitudGP12ID",
                        SqlDbType.Int).Value =
                        solicitudGP12ID;

                    if (Convert.ToInt32(
                            await cmd.ExecuteScalarAsync()
                            ?? 0) == 1)
                    {
                        await tx.RollbackAsync();

                        TempData["Mensaje"] =
                            "La solicitud ya tiene actividad GP12. Se abrio su detalle para continuar.";

                        return RedirectToAction(
                            nameof(Detalle),
                            new { id = solicitudGP12ID });
                    }
                }

                var ahora = DateTime.Now;

                const string sqlPrograma = @"
INSERT INTO dbo.GP12_Programacion
(
    SolicitudGP12ID,
    SolicitudEtiquetaID,
    FechaProgramada,
    HoraInicioProgramada,
    HoraFinProgramada,
    Prioridad,
    CantidadProgramada,
    Observaciones,
    UsuarioProgramacionID,
    FechaCreacion,
    Activo
)
VALUES
(
    @SolicitudGP12ID,
    @SolicitudEtiquetaID,
    CAST(@Ahora AS date),
    NULL,
    NULL,
    1,
    @Cantidad,
    N'Programacion automatica del flujo rapido de caja fisica recibida desde Calidad.',
    @UsuarioID,
    @Ahora,
    1
);
SELECT CAST(SCOPE_IDENTITY() AS INT);";

                int programacionID;

                await using (var cmd =
                    new SqlCommand(
                        sqlPrograma,
                        cn,
                        tx))
                {
                    cmd.Parameters.Add(
                        "@SolicitudGP12ID",
                        SqlDbType.Int).Value =
                        solicitudGP12ID;

                    cmd.Parameters.Add(
                        "@SolicitudEtiquetaID",
                        SqlDbType.Int).Value =
                        solicitudEtiquetaID;

                    var cantidad =
                        cmd.Parameters.Add(
                            "@Cantidad",
                            SqlDbType.Decimal);

                    cantidad.Precision = 18;
                    cantidad.Scale = 4;
                    cantidad.Value = pendiente;

                    cmd.Parameters.Add(
                        "@UsuarioID",
                        SqlDbType.Int).Value =
                        usuarioID.Value;

                    cmd.Parameters.Add(
                        "@Ahora",
                        SqlDbType.DateTime2).Value =
                        ahora;

                    programacionID =
                        Convert.ToInt32(
                            await cmd.ExecuteScalarAsync());
                }

                const string sqlAsignacion = @"
INSERT INTO dbo.GP12_Asignaciones
(
    ProgramacionGP12ID,
    SolicitudGP12ID,
    PersonaID,
    CantidadAsignada,
    FechaAsignacion,
    FechaInicio,
    FechaFin,
    Cumplida,
    Observaciones,
    UsuarioAsignacionID,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
VALUES
(
    @ProgramacionGP12ID,
    @SolicitudGP12ID,
    @PersonaID,
    @Cantidad,
    @Ahora,
    @Ahora,
    NULL,
    0,
    N'Asignacion automatica al usuario que inicia la revision rapida.',
    @UsuarioID,
    @UsuarioID,
    @Ahora,
    1
);
SELECT CAST(SCOPE_IDENTITY() AS INT);";

                int asignacionID;

                await using (var cmd =
                    new SqlCommand(
                        sqlAsignacion,
                        cn,
                        tx))
                {
                    cmd.Parameters.Add(
                        "@ProgramacionGP12ID",
                        SqlDbType.Int).Value =
                        programacionID;

                    cmd.Parameters.Add(
                        "@SolicitudGP12ID",
                        SqlDbType.Int).Value =
                        solicitudGP12ID;

                    cmd.Parameters.Add(
                        "@PersonaID",
                        SqlDbType.Int).Value =
                        personaID;

                    var cantidad =
                        cmd.Parameters.Add(
                            "@Cantidad",
                            SqlDbType.Decimal);

                    cantidad.Precision = 18;
                    cantidad.Scale = 4;
                    cantidad.Value = pendiente;

                    cmd.Parameters.Add(
                        "@UsuarioID",
                        SqlDbType.Int).Value =
                        usuarioID.Value;

                    cmd.Parameters.Add(
                        "@Ahora",
                        SqlDbType.DateTime2).Value =
                        ahora;

                    asignacionID =
                        Convert.ToInt32(
                            await cmd.ExecuteScalarAsync());
                }

                const string sqlInspeccion = @"
INSERT INTO dbo.GP12_Inspecciones
(
    SolicitudGP12ID,
    AsignacionGP12ID,
    PersonaInspectorID,
    FechaInicio,
    CantidadRevisada,
    CantidadOK,
    CantidadNOK,
    CantidadRetrabajada,
    CantidadScrap,
    ValidacionEtiqueta,
    DocumentacionColocada,
    RutaInspeccionValidada,
    CantidadBasculaValidada,
    EtiquetaInspeccionColocada,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
VALUES
(
    @SolicitudGP12ID,
    @AsignacionGP12ID,
    @PersonaID,
    @Ahora,
    0,0,0,0,0,
    0,0,0,0,0,
    @UsuarioID,
    @Ahora,
    1
);
SELECT CAST(SCOPE_IDENTITY() AS INT);";

                int inspeccionID;

                await using (var cmd =
                    new SqlCommand(
                        sqlInspeccion,
                        cn,
                        tx))
                {
                    cmd.Parameters.Add(
                        "@SolicitudGP12ID",
                        SqlDbType.Int).Value =
                        solicitudGP12ID;

                    cmd.Parameters.Add(
                        "@AsignacionGP12ID",
                        SqlDbType.Int).Value =
                        asignacionID;

                    cmd.Parameters.Add(
                        "@PersonaID",
                        SqlDbType.Int).Value =
                        personaID;

                    cmd.Parameters.Add(
                        "@UsuarioID",
                        SqlDbType.Int).Value =
                        usuarioID.Value;

                    cmd.Parameters.Add(
                        "@Ahora",
                        SqlDbType.DateTime2).Value =
                        ahora;

                    inspeccionID =
                        Convert.ToInt32(
                            await cmd.ExecuteScalarAsync());
                }

                const string sqlUpdateSolicitud = @"
UPDATE dbo.GP12_Solicitudes
SET
    EstatusID=@EstatusID,
    FechaInicio=COALESCE(FechaInicio,@Ahora),
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=@Ahora
WHERE SolicitudGP12ID=@SolicitudGP12ID
  AND Activo=1;";

                await using (var cmd =
                    new SqlCommand(
                        sqlUpdateSolicitud,
                        cn,
                        tx))
                {
                    cmd.Parameters.Add(
                        "@EstatusID",
                        SqlDbType.Int).Value =
                        GP12Estatus.EnInspeccion;

                    cmd.Parameters.Add(
                        "@Ahora",
                        SqlDbType.DateTime2).Value =
                        ahora;

                    cmd.Parameters.Add(
                        "@UsuarioID",
                        SqlDbType.Int).Value =
                        usuarioID.Value;

                    cmd.Parameters.Add(
                        "@SolicitudGP12ID",
                        SqlDbType.Int).Value =
                        solicitudGP12ID;

                    await cmd.ExecuteNonQueryAsync();
                }

                await AgregarHistorialAsync(
                    cn,
                    tx,
                    solicitudGP12ID,
                    GP12Movimientos.ProgramacionCreada,
                    estatusAnterior,
                    GP12Estatus.Programado,
                    GP12EntidadHistorial.Programacion,
                    programacionID,
                    $"Flujo rapido: se programaron {pendiente:N4} pieza(s) para revision inmediata.",
                    usuarioID.Value);

                await AgregarHistorialAsync(
                    cn,
                    tx,
                    solicitudGP12ID,
                    GP12Movimientos.TrabajoAsignado,
                    GP12Estatus.Programado,
                    GP12Estatus.Asignado,
                    GP12EntidadHistorial.Asignacion,
                    asignacionID,
                    $"Flujo rapido: revision asignada automaticamente a PersonaID {personaID}.",
                    usuarioID.Value);

                await AgregarHistorialAsync(
                    cn,
                    tx,
                    solicitudGP12ID,
                    GP12Movimientos.InspeccionIniciada,
                    GP12Estatus.Asignado,
                    GP12Estatus.EnInspeccion,
                    GP12EntidadHistorial.Inspeccion,
                    inspeccionID,
                    $"Flujo rapido: inspeccion iniciada para {pendiente:N4} pieza(s).",
                    usuarioID.Value);

                await tx.CommitAsync();

                TempData["Mensaje"] =
                    "Revision GP12 iniciada. Se omitieron los pasos manuales de programacion y asignacion; la trazabilidad interna se conservo.";
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
                    "No fue posible iniciar la revision GP12: "
                    + ex.Message;

                return RedirectToAction(nameof(Index));
            }

            return RedirectToAction(
                nameof(Detalle),
                new { id = solicitudGP12ID });
        }
    }
}