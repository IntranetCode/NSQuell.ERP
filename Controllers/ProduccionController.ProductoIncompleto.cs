using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers;

public sealed partial class ProduccionController
{
    [HttpGet]
    public async Task<IActionResult> ProductoIncompleto(string? busqueda = null, string? estado = null, string? ubicacion = null, bool soloConAntiguedad = false)
    {
        if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");
        busqueda = string.IsNullOrWhiteSpace(busqueda) ? null : busqueda.Trim();
        estado = string.IsNullOrWhiteSpace(estado) ? null : estado.Trim().ToUpperInvariant();
        ubicacion = string.IsNullOrWhiteSpace(ubicacion) ? null : ubicacion.Trim();
        if (!string.IsNullOrWhiteSpace(estado) && estado != ProduccionProductoIncompletoEstado.Disponible && estado != ProduccionProductoIncompletoEstado.Reservada && estado != ProduccionProductoIncompletoEstado.EnCompletado && estado != ProduccionProductoIncompletoEstado.Completa && estado != ProduccionProductoIncompletoEstado.Cancelada)
        {
            estado = null;
        }
        var vm = new ProduccionProductoIncompletoIndexVm
        {
            Busqueda = busqueda,
            Estado = estado,
            Ubicacion = ubicacion,
            SoloConAntiguedad = soloConAntiguedad
        };
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();
        vm.Cajas = await CargarProductoIncompletoAsync(busqueda, estado, ubicacion, soloConAntiguedad, cn);
        return View("ProductoIncompleto", vm);
    }

    [HttpGet]
    public async Task<IActionResult> DetalleProductoIncompleto(long id)
    {
        if (!UsuarioEnSesion()) return Unauthorized();
        if (id <= 0) return BadRequest(new { ok = false, mensaje = "No se recibió una etiqueta blanca válida." });
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();
        var caja = await ObtenerProductoIncompletoAsync(id, cn);
        if (caja == null) return NotFound(new { ok = false, mensaje = "La etiqueta blanca ya no existe o no corresponde a producto incompleto." });
        var movimientos = await CargarTrazabilidadProductoIncompletoAsync(id, cn);
        return Json(new
        {
            ok = true,
            caja = new
            {
                caja.CajaProduccionID,
                caja.EtiquetaBlanca,
                caja.FolioCaja,
                caja.TextoParte,
                caja.NumeroParte,
                caja.ReferenciaSAP,
                caja.DescripcionParte,
                caja.CantidadPiezas,
                caja.CapacidadObjetivoCaja,
                caja.CantidadPendienteCompletar,
                caja.PorcentajeLlenado,
                caja.EstadoProductoIncompleto,
                caja.TextoEstado,
                caja.UbicacionProductoIncompleto,
                caja.TextoUbicacion,
                caja.NumeroOFOrigen,
                caja.TextoOFOrigen,
                caja.NumeroOFDestino,
                caja.TextoOFDestino,
                caja.FechaFormacion,
                caja.FechaIngresoProductoIncompleto,
                caja.FechaReservaIncompleto,
                caja.FechaCompletadoIncompleto,
                caja.DiasEnAlmacen,
                caja.TextoAntiguedad,
                caja.MaquinaOrigenCodigo,
                caja.MaquinaOrigenNombre,
                caja.OperadorOrigenNombre,
                caja.MaterialCodigo,
                caja.MaterialDescripcion
            },
            movimientos = movimientos.Select(x => new
            {
                x.CajaOrigenDetalleID,
                x.TipoMovimiento,
                x.TextoTipoMovimiento,
                x.CantidadPiezas,
                x.EjecucionProduccionID,
                x.ProgramaProduccionID,
                x.SolicitudProduccionID,
                x.NumeroOF,
                x.NumeroParte,
                x.ReferenciaSAP,
                x.TextoParte,
                x.MaquinaCodigo,
                x.MaquinaNombre,
                x.TextoMaquina,
                x.OperadorNombre,
                x.FechaMovimiento,
                x.UsuarioID,
                x.Observaciones
            })
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ActualizarUbicacionProductoIncompleto(ProduccionProductoIncompletoUbicacionPostVm vm)
    {
        if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");
        if (vm.CajaProduccionID <= 0)
        {
            TempData["Error"] = "No se recibió correctamente la etiqueta blanca.";
            return RedirectToAction(nameof(ProductoIncompleto));
        }
        var ubicacion = string.IsNullOrWhiteSpace(vm.UbicacionProductoIncompleto) ? null : vm.UbicacionProductoIncompleto.Trim();
        if (string.IsNullOrWhiteSpace(ubicacion))
        {
            TempData["Error"] = "Captura la ubicación física del producto incompleto.";
            return RedirectToAction(nameof(ProductoIncompleto));
        }
        if (ubicacion.Length > 100)
        {
            TempData["Error"] = "La ubicación no puede superar 100 caracteres.";
            return RedirectToAction(nameof(ProductoIncompleto));
        }
        var usuarioId = ObtenerUsuarioID();
        if (usuarioId <= 0) return Unauthorized();
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            const string sqlLeer = @"
SELECT TOP(1)
    CajaProduccionID,
    EtiquetaBlanca,
    EstadoProductoIncompleto,
    UbicacionProductoIncompleto,
    FechaIngresoProductoIncompleto
FROM dbo.Produccion_Cajas WITH(UPDLOCK,HOLDLOCK)
WHERE CajaProduccionID=@CajaProduccionID
  AND Activo=1
  AND ISNULL(EsProductoIncompleto,0)=1;";
            string etiquetaBlanca;
            string estadoActual;
            string? ubicacionAnterior;
            DateTime? fechaIngreso;
            await using (var cmd = new SqlCommand(sqlLeer, cn, tx))
            {
                cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value = vm.CajaProduccionID;
                await using var rd = await cmd.ExecuteReaderAsync();
                if (!await rd.ReadAsync())
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "No se encontró la etiqueta blanca indicada.";
                    return RedirectToAction(nameof(ProductoIncompleto));
                }
                etiquetaBlanca = rd["EtiquetaBlanca"] == DBNull.Value ? $"Caja {vm.CajaProduccionID}" : rd["EtiquetaBlanca"]?.ToString()?.Trim() ?? $"Caja {vm.CajaProduccionID}";
                estadoActual = rd["EstadoProductoIncompleto"] == DBNull.Value ? string.Empty : rd["EstadoProductoIncompleto"]?.ToString()?.Trim().ToUpperInvariant() ?? string.Empty;
                ubicacionAnterior = rd["UbicacionProductoIncompleto"] == DBNull.Value ? null : rd["UbicacionProductoIncompleto"]?.ToString()?.Trim();
                fechaIngreso = rd["FechaIngresoProductoIncompleto"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaIngresoProductoIncompleto"]);
            }
            if (estadoActual == ProduccionProductoIncompletoEstado.Completa || estadoActual == ProduccionProductoIncompletoEstado.Cancelada)
            {
                await tx.RollbackAsync();
                TempData["Error"] = $"La etiqueta blanca {etiquetaBlanca} ya se encuentra {ProduccionProductoIncompletoEstado.Nombre(estadoActual).ToLowerInvariant()} y su ubicación de resguardo ya no puede modificarse.";
                return RedirectToAction(nameof(ProductoIncompleto));
            }
            if (string.Equals(ubicacionAnterior, ubicacion, StringComparison.OrdinalIgnoreCase))
            {
                await tx.CommitAsync();
                TempData["Info"] = $"La etiqueta blanca {etiquetaBlanca} ya se encuentra registrada en {ubicacion}.";
                return RedirectToAction(nameof(ProductoIncompleto));
            }
            const string sqlActualizar = @"
UPDATE dbo.Produccion_Cajas
SET UbicacionProductoIncompleto=@Ubicacion,
    FechaIngresoProductoIncompleto=COALESCE(FechaIngresoProductoIncompleto,SYSDATETIME()),
    UsuarioIngresoProductoIncompletoID=CASE WHEN FechaIngresoProductoIncompleto IS NULL THEN @UsuarioID ELSE UsuarioIngresoProductoIncompletoID END,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=SYSDATETIME()
WHERE CajaProduccionID=@CajaProduccionID
  AND Activo=1
  AND ISNULL(EsProductoIncompleto,0)=1
  AND UPPER(LTRIM(RTRIM(ISNULL(EstadoProductoIncompleto,N'')))) NOT IN(N'COMPLETA',N'CANCELADA');
IF @@ROWCOUNT<>1
    THROW 51601,'La etiqueta blanca cambió de estado y no fue posible actualizar su ubicación.',1;";
            await using (var cmd = new SqlCommand(sqlActualizar, cn, tx))
            {
                cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value = vm.CajaProduccionID;
                cmd.Parameters.Add("@Ubicacion", SqlDbType.NVarChar, 100).Value = ubicacion;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                await cmd.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
            TempData["Success"] = string.IsNullOrWhiteSpace(ubicacionAnterior)
                ? $"La etiqueta blanca {etiquetaBlanca} ingresó al almacén de producto incompleto en la ubicación {ubicacion}."
                : $"La etiqueta blanca {etiquetaBlanca} cambió de ubicación: {ubicacionAnterior} → {ubicacion}.";
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(); } catch { }
            TempData["Error"] = "No fue posible actualizar la ubicación: " + ex.Message;
        }
        return RedirectToAction(nameof(ProductoIncompleto));
    }

    private static async Task<List<ProduccionCajaIncompletaDisponibleVm>> CargarProductoIncompletoAsync(string? busqueda, string? estado, string? ubicacion, bool soloConAntiguedad, SqlConnection cn)
    {
        var lista = new List<ProduccionCajaIncompletaDisponibleVm>();
        const string sql = @"
SELECT
    c.CajaProduccionID,
    c.EjecucionProduccionID,
    c.ProgramaProduccionID,
    c.SolicitudProduccionID,
    c.SolicitudProduccionDetalleID,
    e.ParteID,
    e.NumeroParte,
    e.ReferenciaSAP,
    COALESCE(NULLIF(e.DescripcionParte,N''),NULLIF(p.Designacion,N''),NULLIF(p.Descripcion,N'')) AS DescripcionParte,
    c.FolioCaja,
    c.EtiquetaBlanca,
    ISNULL(c.CantidadPiezas,ISNULL(c.Cantidad,0)) AS CantidadPiezas,
    ISNULL(c.CapacidadObjetivoCaja,0) AS CapacidadObjetivoCaja,
    ISNULL(c.CantidadPendienteCompletar,0) AS CantidadPendienteCompletar,
    UPPER(LTRIM(RTRIM(ISNULL(c.EstadoProductoIncompleto,N'DISPONIBLE')))) AS EstadoProductoIncompleto,
    c.EjecucionReservaID,
    c.ProgramaReservaID,
    c.SolicitudReservaID,
    c.SolicitudDetalleReservaID,
    ISNULL(c.FechaFormacion,c.FechaCreacion) AS FechaFormacion,
    c.FechaReservaIncompleto,
    c.FechaCompletadoIncompleto,
    c.UbicacionProductoIncompleto,
    c.FechaIngresoProductoIncompleto,
    c.UsuarioIngresoProductoIncompletoID,
    COALESCE(NULLIF(LTRIM(RTRIM(sOrigen.NumeroOFRecibida)),N''),NULLIF(LTRIM(RTRIM(sOrigen.FolioSolicitud)),N''),CONCAT(N'Programa ',c.ProgramaProduccionID)) AS NumeroOFOrigen,
    COALESCE(NULLIF(LTRIM(RTRIM(sDestino.NumeroOFRecibida)),N''),NULLIF(LTRIM(RTRIM(sDestino.FolioSolicitud)),N''),CASE WHEN c.ProgramaReservaID IS NOT NULL THEN CONCAT(N'Programa ',c.ProgramaReservaID) END) AS NumeroOFDestino,
    e.MaquinaCodigo AS MaquinaOrigenCodigo,
    e.MaquinaNombre AS MaquinaOrigenNombre,
    e.OperadorNombre AS OperadorOrigenNombre,
    dt.MaterialCodigo,
    dt.MaterialDescripcion
FROM dbo.Produccion_Cajas c
INNER JOIN dbo.Produccion_Ejecucion e
    ON e.EjecucionProduccionID=c.EjecucionProduccionID
   AND e.Activo=1
LEFT JOIN dbo.ERP_Partes p
    ON p.ParteID=e.ParteID
LEFT JOIN dbo.ERP_ParteDatosTecnicos dt
    ON dt.ParteID=e.ParteID
   AND dt.Activo=1
LEFT JOIN dbo.SolicitudesProduccion sOrigen
    ON sOrigen.SolicitudProduccionID=c.SolicitudProduccionID
   AND sOrigen.Activo=1
LEFT JOIN dbo.SolicitudesProduccion sDestino
    ON sDestino.SolicitudProduccionID=c.SolicitudReservaID
   AND sDestino.Activo=1
WHERE c.Activo=1
  AND ISNULL(c.EsProductoIncompleto,0)=1
  AND (@Estado IS NULL OR UPPER(LTRIM(RTRIM(ISNULL(c.EstadoProductoIncompleto,N'DISPONIBLE'))))=@Estado)
  AND (@Ubicacion IS NULL OR c.UbicacionProductoIncompleto LIKE N'%'+@Ubicacion+N'%')
  AND
  (
      @Busqueda IS NULL
      OR c.EtiquetaBlanca LIKE N'%'+@Busqueda+N'%'
      OR c.FolioCaja LIKE N'%'+@Busqueda+N'%'
      OR e.NumeroParte LIKE N'%'+@Busqueda+N'%'
      OR e.ReferenciaSAP LIKE N'%'+@Busqueda+N'%'
      OR e.DescripcionParte LIKE N'%'+@Busqueda+N'%'
      OR sOrigen.NumeroOFRecibida LIKE N'%'+@Busqueda+N'%'
      OR sOrigen.FolioSolicitud LIKE N'%'+@Busqueda+N'%'
      OR sDestino.NumeroOFRecibida LIKE N'%'+@Busqueda+N'%'
      OR sDestino.FolioSolicitud LIKE N'%'+@Busqueda+N'%'
      OR c.UbicacionProductoIncompleto LIKE N'%'+@Busqueda+N'%'
  )
  AND
  (
      @SoloConAntiguedad=0
      OR DATEDIFF(DAY,CONVERT(date,COALESCE(c.FechaIngresoProductoIncompleto,c.FechaFormacion,c.FechaCreacion)),CONVERT(date,GETDATE()))>=7
  )
ORDER BY
    CASE UPPER(LTRIM(RTRIM(ISNULL(c.EstadoProductoIncompleto,N'DISPONIBLE'))))
        WHEN N'DISPONIBLE' THEN 1
        WHEN N'RESERVADA' THEN 2
        WHEN N'EN_COMPLETADO' THEN 3
        WHEN N'COMPLETA' THEN 4
        WHEN N'CANCELADA' THEN 5
        ELSE 6
    END,
    CASE WHEN c.UbicacionProductoIncompleto IS NULL OR LTRIM(RTRIM(c.UbicacionProductoIncompleto))=N'' THEN 0 ELSE 1 END,
    COALESCE(c.FechaIngresoProductoIncompleto,c.FechaFormacion,c.FechaCreacion),
    c.CajaProduccionID;";
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@Busqueda", SqlDbType.NVarChar, 200).Value = string.IsNullOrWhiteSpace(busqueda) ? DBNull.Value : busqueda;
        cmd.Parameters.Add("@Estado", SqlDbType.NVarChar, 30).Value = string.IsNullOrWhiteSpace(estado) ? DBNull.Value : estado;
        cmd.Parameters.Add("@Ubicacion", SqlDbType.NVarChar, 100).Value = string.IsNullOrWhiteSpace(ubicacion) ? DBNull.Value : ubicacion;
        cmd.Parameters.Add("@SoloConAntiguedad", SqlDbType.Bit).Value = soloConAntiguedad;
        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync()) lista.Add(MapearProductoIncompleto(rd));
        return lista;
    }

    private static async Task<ProduccionCajaIncompletaDisponibleVm?> ObtenerProductoIncompletoAsync(long cajaProduccionId, SqlConnection cn)
    {
        const string sql = @"
SELECT TOP(1)
    c.CajaProduccionID,
    c.EjecucionProduccionID,
    c.ProgramaProduccionID,
    c.SolicitudProduccionID,
    c.SolicitudProduccionDetalleID,
    e.ParteID,
    e.NumeroParte,
    e.ReferenciaSAP,
    COALESCE(NULLIF(e.DescripcionParte,N''),NULLIF(p.Designacion,N''),NULLIF(p.Descripcion,N'')) AS DescripcionParte,
    c.FolioCaja,
    c.EtiquetaBlanca,
    ISNULL(c.CantidadPiezas,ISNULL(c.Cantidad,0)) AS CantidadPiezas,
    ISNULL(c.CapacidadObjetivoCaja,0) AS CapacidadObjetivoCaja,
    ISNULL(c.CantidadPendienteCompletar,0) AS CantidadPendienteCompletar,
    UPPER(LTRIM(RTRIM(ISNULL(c.EstadoProductoIncompleto,N'DISPONIBLE')))) AS EstadoProductoIncompleto,
    c.EjecucionReservaID,
    c.ProgramaReservaID,
    c.SolicitudReservaID,
    c.SolicitudDetalleReservaID,
    ISNULL(c.FechaFormacion,c.FechaCreacion) AS FechaFormacion,
    c.FechaReservaIncompleto,
    c.FechaCompletadoIncompleto,
    c.UbicacionProductoIncompleto,
    c.FechaIngresoProductoIncompleto,
    c.UsuarioIngresoProductoIncompletoID,
    COALESCE(NULLIF(LTRIM(RTRIM(sOrigen.NumeroOFRecibida)),N''),NULLIF(LTRIM(RTRIM(sOrigen.FolioSolicitud)),N''),CONCAT(N'Programa ',c.ProgramaProduccionID)) AS NumeroOFOrigen,
    COALESCE(NULLIF(LTRIM(RTRIM(sDestino.NumeroOFRecibida)),N''),NULLIF(LTRIM(RTRIM(sDestino.FolioSolicitud)),N''),CASE WHEN c.ProgramaReservaID IS NOT NULL THEN CONCAT(N'Programa ',c.ProgramaReservaID) END) AS NumeroOFDestino,
    e.MaquinaCodigo AS MaquinaOrigenCodigo,
    e.MaquinaNombre AS MaquinaOrigenNombre,
    e.OperadorNombre AS OperadorOrigenNombre,
    dt.MaterialCodigo,
    dt.MaterialDescripcion
FROM dbo.Produccion_Cajas c
INNER JOIN dbo.Produccion_Ejecucion e
    ON e.EjecucionProduccionID=c.EjecucionProduccionID
   AND e.Activo=1
LEFT JOIN dbo.ERP_Partes p
    ON p.ParteID=e.ParteID
LEFT JOIN dbo.ERP_ParteDatosTecnicos dt
    ON dt.ParteID=e.ParteID
   AND dt.Activo=1
LEFT JOIN dbo.SolicitudesProduccion sOrigen
    ON sOrigen.SolicitudProduccionID=c.SolicitudProduccionID
   AND sOrigen.Activo=1
LEFT JOIN dbo.SolicitudesProduccion sDestino
    ON sDestino.SolicitudProduccionID=c.SolicitudReservaID
   AND sDestino.Activo=1
WHERE c.CajaProduccionID=@CajaProduccionID
  AND c.Activo=1
  AND ISNULL(c.EsProductoIncompleto,0)=1;";
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value = cajaProduccionId;
        await using var rd = await cmd.ExecuteReaderAsync();
        return await rd.ReadAsync() ? MapearProductoIncompleto(rd) : null;
    }

    private static ProduccionCajaIncompletaDisponibleVm MapearProductoIncompleto(SqlDataReader rd)
    {
        return new ProduccionCajaIncompletaDisponibleVm
        {
            CajaProduccionID = Convert.ToInt64(rd["CajaProduccionID"]),
            EjecucionProduccionID = Convert.ToInt32(rd["EjecucionProduccionID"]),
            ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),
            SolicitudProduccionID = rd["SolicitudProduccionID"] == DBNull.Value ? null : Convert.ToInt32(rd["SolicitudProduccionID"]),
            SolicitudProduccionDetalleID = rd["SolicitudProduccionDetalleID"] == DBNull.Value ? null : Convert.ToInt32(rd["SolicitudProduccionDetalleID"]),
            ParteID = rd["ParteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ParteID"]),
            NumeroParte = rd["NumeroParte"] == DBNull.Value ? null : rd["NumeroParte"]?.ToString()?.Trim(),
            ReferenciaSAP = rd["ReferenciaSAP"] == DBNull.Value ? null : rd["ReferenciaSAP"]?.ToString()?.Trim(),
            DescripcionParte = rd["DescripcionParte"] == DBNull.Value ? null : rd["DescripcionParte"]?.ToString()?.Trim(),
            FolioCaja = rd["FolioCaja"] == DBNull.Value ? null : rd["FolioCaja"]?.ToString()?.Trim(),
            EtiquetaBlanca = rd["EtiquetaBlanca"] == DBNull.Value ? null : rd["EtiquetaBlanca"]?.ToString()?.Trim(),
            CantidadPiezas = Convert.ToInt32(rd["CantidadPiezas"]),
            CapacidadObjetivoCaja = Convert.ToInt32(rd["CapacidadObjetivoCaja"]),
            CantidadPendienteCompletar = Convert.ToInt32(rd["CantidadPendienteCompletar"]),
            EstadoProductoIncompleto = rd["EstadoProductoIncompleto"]?.ToString()?.Trim() ?? ProduccionProductoIncompletoEstado.Disponible,
            EjecucionReservaID = rd["EjecucionReservaID"] == DBNull.Value ? null : Convert.ToInt32(rd["EjecucionReservaID"]),
            ProgramaReservaID = rd["ProgramaReservaID"] == DBNull.Value ? null : Convert.ToInt32(rd["ProgramaReservaID"]),
            SolicitudReservaID = rd["SolicitudReservaID"] == DBNull.Value ? null : Convert.ToInt32(rd["SolicitudReservaID"]),
            SolicitudDetalleReservaID = rd["SolicitudDetalleReservaID"] == DBNull.Value ? null : Convert.ToInt32(rd["SolicitudDetalleReservaID"]),
            FechaFormacion = Convert.ToDateTime(rd["FechaFormacion"]),
            FechaReservaIncompleto = rd["FechaReservaIncompleto"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaReservaIncompleto"]),
            FechaCompletadoIncompleto = rd["FechaCompletadoIncompleto"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaCompletadoIncompleto"]),
            UbicacionProductoIncompleto = rd["UbicacionProductoIncompleto"] == DBNull.Value ? null : rd["UbicacionProductoIncompleto"]?.ToString()?.Trim(),
            FechaIngresoProductoIncompleto = rd["FechaIngresoProductoIncompleto"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaIngresoProductoIncompleto"]),
            UsuarioIngresoProductoIncompletoID = rd["UsuarioIngresoProductoIncompletoID"] == DBNull.Value ? null : Convert.ToInt32(rd["UsuarioIngresoProductoIncompletoID"]),
            NumeroOFOrigen = rd["NumeroOFOrigen"] == DBNull.Value ? null : rd["NumeroOFOrigen"]?.ToString()?.Trim(),
            NumeroOFDestino = rd["NumeroOFDestino"] == DBNull.Value ? null : rd["NumeroOFDestino"]?.ToString()?.Trim(),
            MaquinaOrigenCodigo = rd["MaquinaOrigenCodigo"] == DBNull.Value ? null : rd["MaquinaOrigenCodigo"]?.ToString()?.Trim(),
            MaquinaOrigenNombre = rd["MaquinaOrigenNombre"] == DBNull.Value ? null : rd["MaquinaOrigenNombre"]?.ToString()?.Trim(),
            OperadorOrigenNombre = rd["OperadorOrigenNombre"] == DBNull.Value ? null : rd["OperadorOrigenNombre"]?.ToString()?.Trim(),
            MaterialCodigo = rd["MaterialCodigo"] == DBNull.Value ? null : rd["MaterialCodigo"]?.ToString()?.Trim(),
            MaterialDescripcion = rd["MaterialDescripcion"] == DBNull.Value ? null : rd["MaterialDescripcion"]?.ToString()?.Trim()
        };
    }

    private static async Task<List<ProduccionProductoIncompletoMovimientoVm>> CargarTrazabilidadProductoIncompletoAsync(long cajaProduccionId, SqlConnection cn)
    {
        var lista = new List<ProduccionProductoIncompletoMovimientoVm>();
        const string sql = @"
SELECT
    d.CajaOrigenDetalleID,
    d.CajaProduccionID,
    d.TipoMovimiento,
    d.EjecucionProduccionID,
    d.ProgramaProduccionID,
    d.SolicitudProduccionID,
    d.SolicitudProduccionDetalleID,
    d.ReleaseID,
    d.ReleaseDetalleID,
    ISNULL(d.CantidadPiezas,0) AS CantidadPiezas,
    COALESCE(NULLIF(LTRIM(RTRIM(s.NumeroOFRecibida)),N''),NULLIF(LTRIM(RTRIM(s.FolioSolicitud)),N''),CONCAT(N'Programa ',d.ProgramaProduccionID)) AS NumeroOF,
    e.NumeroParte,
    e.ReferenciaSAP,
    e.MaquinaCodigo,
    e.MaquinaNombre,
    e.OperadorNombre,
    d.FechaCreacion AS FechaMovimiento,
    d.UsuarioCreacionID AS UsuarioID,
    d.Observaciones
FROM dbo.Produccion_CajaOrigenDetalle d
LEFT JOIN dbo.Produccion_Ejecucion e
    ON e.EjecucionProduccionID=d.EjecucionProduccionID
LEFT JOIN dbo.SolicitudesProduccion s
    ON s.SolicitudProduccionID=d.SolicitudProduccionID
   AND s.Activo=1
WHERE d.CajaProduccionID=@CajaProduccionID
  AND d.Activo=1
ORDER BY d.FechaCreacion,d.CajaOrigenDetalleID;";
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value = cajaProduccionId;
        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            lista.Add(new ProduccionProductoIncompletoMovimientoVm
            {
                CajaOrigenDetalleID = Convert.ToInt64(rd["CajaOrigenDetalleID"]),
                CajaProduccionID = Convert.ToInt64(rd["CajaProduccionID"]),
                TipoMovimiento = rd["TipoMovimiento"]?.ToString()?.Trim() ?? string.Empty,
                EjecucionProduccionID = Convert.ToInt32(rd["EjecucionProduccionID"]),
                ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),
                SolicitudProduccionID = rd["SolicitudProduccionID"] == DBNull.Value ? null : Convert.ToInt32(rd["SolicitudProduccionID"]),
                SolicitudProduccionDetalleID = rd["SolicitudProduccionDetalleID"] == DBNull.Value ? null : Convert.ToInt32(rd["SolicitudProduccionDetalleID"]),
                ReleaseID = rd["ReleaseID"] == DBNull.Value ? null : Convert.ToInt32(rd["ReleaseID"]),
                ReleaseDetalleID = rd["ReleaseDetalleID"] == DBNull.Value ? null : Convert.ToInt32(rd["ReleaseDetalleID"]),
                CantidadPiezas = Convert.ToInt32(rd["CantidadPiezas"]),
                NumeroOF = rd["NumeroOF"] == DBNull.Value ? null : rd["NumeroOF"]?.ToString()?.Trim(),
                NumeroParte = rd["NumeroParte"] == DBNull.Value ? null : rd["NumeroParte"]?.ToString()?.Trim(),
                ReferenciaSAP = rd["ReferenciaSAP"] == DBNull.Value ? null : rd["ReferenciaSAP"]?.ToString()?.Trim(),
                MaquinaCodigo = rd["MaquinaCodigo"] == DBNull.Value ? null : rd["MaquinaCodigo"]?.ToString()?.Trim(),
                MaquinaNombre = rd["MaquinaNombre"] == DBNull.Value ? null : rd["MaquinaNombre"]?.ToString()?.Trim(),
                OperadorNombre = rd["OperadorNombre"] == DBNull.Value ? null : rd["OperadorNombre"]?.ToString()?.Trim(),
                FechaMovimiento = Convert.ToDateTime(rd["FechaMovimiento"]),
                UsuarioID = rd["UsuarioID"] == DBNull.Value ? null : Convert.ToInt32(rd["UsuarioID"]),
                Observaciones = rd["Observaciones"] == DBNull.Value ? null : rd["Observaciones"]?.ToString()?.Trim()
            });
        }
        return lista;
    }

    // =========================================================
    // PRODUCTO INCOMPLETO AL INICIAR PRODUCCION
    // =========================================================

    private sealed class EtiquetaBlancaInicioValidada
    {
        public long CajaProduccionID { get; set; }
        public string Etiqueta { get; set; } = string.Empty;
        public int CantidadPiezas { get; set; }
        public int CapacidadObjetivoCaja { get; set; }
    }

    [HttpGet]
    public async Task<IActionResult> EtiquetasBlancasDisponiblesInicio(int programaProduccionId)
    {
        if (!UsuarioEnSesion())
            return Unauthorized(new { ok = false, mensaje = "La sesión ya no está activa." });

        if (programaProduccionId <= 0)
            return BadRequest(new { ok = false, mensaje = "No se recibió correctamente el programa de producción." });

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        const string sqlPrograma = @"
SELECT TOP(1)
    pp.ProgramaProduccionID,
    pp.ParteID,
    pp.NumeroParte,
    pp.ReferenciaSAP,
    CONVERT(INT,ISNULL(pp.CantidadProgramada,0)) AS CantidadProgramada
FROM dbo.Planeacion_ProgramaProduccion pp
WHERE pp.ProgramaProduccionID=@ProgramaProduccionID
  AND pp.Activo=1
  AND ISNULL(pp.EstatusID,1) NOT IN(3,4,5,8,9,99);";

        int parteId;
        int cantidadProgramada;
        string? numeroParte;
        string? referenciaSap;

        await using (var cmd = new SqlCommand(sqlPrograma, cn))
        {
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;

            await using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
                return NotFound(new
                {
                    ok = false,
                    mensaje = "El programa ya no está disponible para iniciar Producción."
                });

            parteId = rd["ParteID"] == DBNull.Value
                ? 0
                : Convert.ToInt32(rd["ParteID"]);

            cantidadProgramada = rd["CantidadProgramada"] == DBNull.Value
                ? 0
                : Convert.ToInt32(rd["CantidadProgramada"]);

            numeroParte = rd["NumeroParte"] == DBNull.Value
                ? null
                : rd["NumeroParte"].ToString();

            referenciaSap = rd["ReferenciaSAP"] == DBNull.Value
                ? null
                : rd["ReferenciaSAP"].ToString();
        }

        if (parteId <= 0)
            return BadRequest(new
            {
                ok = false,
                mensaje = "El programa no tiene una parte válida relacionada."
            });

        if (cantidadProgramada <= 0)
            return BadRequest(new
            {
                ok = false,
                mensaje = "El programa no tiene una cantidad válida para iniciar."
            });

        var cajas = await ObtenerEtiquetasBlancasDisponiblesInicioAsync(
            parteId,
            cn,
            null);

        var totalDisponible = cajas.Sum(x => x.CantidadPiezas);

        return Json(new
        {
            ok = true,
            programaProduccionId,
            parteId,
            numeroParte,
            referenciaSAP = referenciaSap,
            cantidadProgramada,
            totalDisponible,
            totalEtiquetas = cajas.Count,
            cajas = cajas.Select(x => new
            {
                cajaProduccionId = x.CajaProduccionID,
                etiquetaBlanca = string.IsNullOrWhiteSpace(x.EtiquetaBlanca)
                    ? $"Caja {x.CajaProduccionID}"
                    : x.EtiquetaBlanca,
                folioCaja = x.FolioCaja,
                cantidadPiezas = x.CantidadPiezas,
                capacidadObjetivoCaja = x.CapacidadObjetivoCaja,
                cantidadPendienteCompletar = x.CantidadPendienteCompletar,
                fechaFormacion = x.FechaFormacion,
                fechaIngreso = x.FechaIngresoProductoIncompleto,
                ubicacion = x.TextoUbicacion,
                numeroOFOrigen = x.TextoOFOrigen,
                maquinaOrigen = x.MaquinaOrigenCodigo,
                operadorOrigen = x.OperadorOrigenNombre,
                antiguedad = x.TextoAntiguedad
            })
        });
    }

    private async Task<List<ProduccionCajaIncompletaDisponibleVm>> ObtenerEtiquetasBlancasDisponiblesInicioAsync(
        int parteId,
        SqlConnection cn,
        SqlTransaction? tx)
    {
        var lista = new List<ProduccionCajaIncompletaDisponibleVm>();

        if (parteId <= 0)
            return lista;

        var capacidadEsperada = await ObtenerCapacidadEsperadaEtiquetaBlancaAsync(
            parteId,
            cn,
            tx);

        const string sql = @"
SELECT
    c.CajaProduccionID,
    c.EjecucionProduccionID,
    e.ProgramaProduccionID,
    e.SolicitudProduccionID,
    e.SolicitudProduccionDetalleID,
    e.ParteID,
    e.NumeroParte,
    e.ReferenciaSAP,
    e.DescripcionParte,
    c.FolioCaja,
    c.EtiquetaBlanca,
    ISNULL(c.CantidadPiezas,ISNULL(c.Cantidad,0)) AS CantidadPiezas,
    ISNULL(c.CapacidadObjetivoCaja,0) AS CapacidadObjetivoCaja,
    ISNULL(
        c.CantidadPendienteCompletar,
        CASE
            WHEN ISNULL(c.CapacidadObjetivoCaja,0)>ISNULL(c.CantidadPiezas,ISNULL(c.Cantidad,0))
                THEN ISNULL(c.CapacidadObjetivoCaja,0)-ISNULL(c.CantidadPiezas,ISNULL(c.Cantidad,0))
            ELSE 0
        END
    ) AS CantidadPendienteCompletar,
    ISNULL(c.EstadoProductoIncompleto,N'DISPONIBLE') AS EstadoProductoIncompleto,
    c.EjecucionReservaID,
    c.ProgramaReservaID,
    c.SolicitudReservaID,
    c.SolicitudDetalleReservaID,
    ISNULL(c.FechaFormacion,c.FechaCreacion) AS FechaFormacion,
    c.FechaReservaIncompleto,
    c.FechaCompletadoIncompleto,
    c.UbicacionProductoIncompleto,
    c.FechaIngresoProductoIncompleto,
    c.UsuarioIngresoProductoIncompletoID,
    COALESCE(
        NULLIF(LTRIM(RTRIM(s.NumeroOFRecibida)),N''),
        NULLIF(LTRIM(RTRIM(s.FolioSolicitud)),N''),
        N'Programa '+CONVERT(NVARCHAR(20),e.ProgramaProduccionID)
    ) AS NumeroOFOrigen,
    e.MaquinaCodigo AS MaquinaOrigenCodigo,
    e.MaquinaNombre AS MaquinaOrigenNombre,
    e.OperadorNombre AS OperadorOrigenNombre
FROM dbo.Produccion_Cajas c
INNER JOIN dbo.Produccion_Ejecucion e
    ON e.EjecucionProduccionID=c.EjecucionProduccionID
   AND e.Activo=1
LEFT JOIN dbo.SolicitudesProduccion s
    ON s.SolicitudProduccionID=e.SolicitudProduccionID
   AND s.Activo=1
WHERE c.Activo=1
  AND ISNULL(c.EsProductoIncompleto,0)=1
  AND e.ParteID=@ParteID
  AND ISNULL(c.CantidadPiezas,ISNULL(c.Cantidad,0))>0
  AND UPPER(LTRIM(RTRIM(ISNULL(c.EstadoProductoIncompleto,N''))))=N'DISPONIBLE'
  AND c.EjecucionReservaID IS NULL
  AND c.ProgramaReservaID IS NULL
  AND c.SolicitudReservaID IS NULL
  AND c.SolicitudDetalleReservaID IS NULL
  AND
  (
      @CapacidadEsperada IS NULL
      OR @CapacidadEsperada<=0
      OR ISNULL(c.CapacidadObjetivoCaja,0)=@CapacidadEsperada
  )
ORDER BY
    ISNULL(c.FechaIngresoProductoIncompleto,ISNULL(c.FechaFormacion,c.FechaCreacion)),
    c.CajaProduccionID;";

        await using var cmd = tx == null
            ? new SqlCommand(sql, cn)
            : new SqlCommand(sql, cn, tx);

        cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = parteId;
        cmd.Parameters.Add("@CapacidadEsperada", SqlDbType.Int).Value =
            capacidadEsperada.HasValue && capacidadEsperada.Value > 0
                ? capacidadEsperada.Value
                : DBNull.Value;

        await using var rd = await cmd.ExecuteReaderAsync();

        while (await rd.ReadAsync())
        {
            lista.Add(new ProduccionCajaIncompletaDisponibleVm
            {
                CajaProduccionID = Convert.ToInt64(rd["CajaProduccionID"]),
                EjecucionProduccionID = Convert.ToInt32(rd["EjecucionProduccionID"]),
                ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),
                SolicitudProduccionID = rd["SolicitudProduccionID"] == DBNull.Value
                    ? null
                    : Convert.ToInt32(rd["SolicitudProduccionID"]),
                SolicitudProduccionDetalleID = rd["SolicitudProduccionDetalleID"] == DBNull.Value
                    ? null
                    : Convert.ToInt32(rd["SolicitudProduccionDetalleID"]),
                ParteID = rd["ParteID"] == DBNull.Value
                    ? null
                    : Convert.ToInt32(rd["ParteID"]),
                NumeroParte = rd["NumeroParte"] == DBNull.Value
                    ? null
                    : rd["NumeroParte"].ToString(),
                ReferenciaSAP = rd["ReferenciaSAP"] == DBNull.Value
                    ? null
                    : rd["ReferenciaSAP"].ToString(),
                DescripcionParte = rd["DescripcionParte"] == DBNull.Value
                    ? null
                    : rd["DescripcionParte"].ToString(),
                FolioCaja = rd["FolioCaja"] == DBNull.Value
                    ? null
                    : rd["FolioCaja"].ToString(),
                EtiquetaBlanca = rd["EtiquetaBlanca"] == DBNull.Value
                    ? null
                    : rd["EtiquetaBlanca"].ToString(),
                CantidadPiezas = rd["CantidadPiezas"] == DBNull.Value
                    ? 0
                    : Convert.ToInt32(rd["CantidadPiezas"]),
                CapacidadObjetivoCaja = rd["CapacidadObjetivoCaja"] == DBNull.Value
                    ? 0
                    : Convert.ToInt32(rd["CapacidadObjetivoCaja"]),
                CantidadPendienteCompletar = rd["CantidadPendienteCompletar"] == DBNull.Value
                    ? 0
                    : Convert.ToInt32(rd["CantidadPendienteCompletar"]),
                EstadoProductoIncompleto = rd["EstadoProductoIncompleto"] == DBNull.Value
                    ? ProduccionProductoIncompletoEstado.Disponible
                    : rd["EstadoProductoIncompleto"].ToString() ?? ProduccionProductoIncompletoEstado.Disponible,
                EjecucionReservaID = rd["EjecucionReservaID"] == DBNull.Value
                    ? null
                    : Convert.ToInt32(rd["EjecucionReservaID"]),
                ProgramaReservaID = rd["ProgramaReservaID"] == DBNull.Value
                    ? null
                    : Convert.ToInt32(rd["ProgramaReservaID"]),
                SolicitudReservaID = rd["SolicitudReservaID"] == DBNull.Value
                    ? null
                    : Convert.ToInt32(rd["SolicitudReservaID"]),
                SolicitudDetalleReservaID = rd["SolicitudDetalleReservaID"] == DBNull.Value
                    ? null
                    : Convert.ToInt32(rd["SolicitudDetalleReservaID"]),
                FechaFormacion = Convert.ToDateTime(rd["FechaFormacion"]),
                FechaReservaIncompleto = rd["FechaReservaIncompleto"] == DBNull.Value
                    ? null
                    : Convert.ToDateTime(rd["FechaReservaIncompleto"]),
                FechaCompletadoIncompleto = rd["FechaCompletadoIncompleto"] == DBNull.Value
                    ? null
                    : Convert.ToDateTime(rd["FechaCompletadoIncompleto"]),
                UbicacionProductoIncompleto = rd["UbicacionProductoIncompleto"] == DBNull.Value
                    ? null
                    : rd["UbicacionProductoIncompleto"].ToString(),
                FechaIngresoProductoIncompleto = rd["FechaIngresoProductoIncompleto"] == DBNull.Value
                    ? null
                    : Convert.ToDateTime(rd["FechaIngresoProductoIncompleto"]),
                UsuarioIngresoProductoIncompletoID = rd["UsuarioIngresoProductoIncompletoID"] == DBNull.Value
                    ? null
                    : Convert.ToInt32(rd["UsuarioIngresoProductoIncompletoID"]),
                NumeroOFOrigen = rd["NumeroOFOrigen"] == DBNull.Value
                    ? null
                    : rd["NumeroOFOrigen"].ToString(),
                MaquinaOrigenCodigo = rd["MaquinaOrigenCodigo"] == DBNull.Value
                    ? null
                    : rd["MaquinaOrigenCodigo"].ToString(),
                MaquinaOrigenNombre = rd["MaquinaOrigenNombre"] == DBNull.Value
                    ? null
                    : rd["MaquinaOrigenNombre"].ToString(),
                OperadorOrigenNombre = rd["OperadorOrigenNombre"] == DBNull.Value
                    ? null
                    : rd["OperadorOrigenNombre"].ToString()
            });
        }

        return lista;
    }

    private async Task<int?> ObtenerCapacidadEsperadaEtiquetaBlancaAsync(
        int parteId,
        SqlConnection cn,
        SqlTransaction? tx)
    {
        const string sql = @"
SELECT TOP(1)
    TRY_CONVERT(INT,COALESCE(PiezasPorEmbalaje,PiezasPorCaja))
FROM dbo.ERP_ParteDatosTecnicos
WHERE ParteID=@ParteID
  AND Activo=1;";

        await using var cmd = tx == null
            ? new SqlCommand(sql, cn)
            : new SqlCommand(sql, cn, tx);

        cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = parteId;

        var resultado = await cmd.ExecuteScalarAsync();

        if (resultado == null || resultado == DBNull.Value)
            return null;

        var capacidad = Convert.ToInt32(resultado);

        return capacidad > 0
            ? capacidad
            : null;
    }

    private async Task<List<EtiquetaBlancaInicioValidada>> ValidarEtiquetasBlancasInicioAsync(
        IEnumerable<long>? cajasSeleccionadas,
        ProgramaParaProduccion programa,
        SqlConnection cn,
        SqlTransaction tx)
    {
        var ids = (cajasSeleccionadas ?? Enumerable.Empty<long>())
            .Where(x => x > 0)
            .Distinct()
            .ToList();

        var resultado = new List<EtiquetaBlancaInicioValidada>();

        if (ids.Count == 0)
            return resultado;

        if (!programa.ParteID.HasValue || programa.ParteID.Value <= 0)
            throw new InvalidOperationException(
                "El programa no tiene una parte válida para aplicar producto incompleto.");

        var cantidadProgramada = programa.CantidadPlaneada ?? 0;

        if (cantidadProgramada <= 0)
            throw new InvalidOperationException(
                "El programa no tiene una cantidad válida para aplicar producto incompleto.");

        var capacidadEsperada =
            await ObtenerCapacidadEsperadaEtiquetaBlancaAsync(
                programa.ParteID.Value,
                cn,
                tx);

        foreach (var cajaId in ids)
        {
            const string sql = @"
SELECT TOP(1)
    c.CajaProduccionID,
    c.EtiquetaBlanca,
    c.FolioCaja,
    ISNULL(c.CantidadPiezas,ISNULL(c.Cantidad,0)) AS CantidadPiezas,
    ISNULL(c.CapacidadObjetivoCaja,0) AS CapacidadObjetivoCaja,
    ISNULL(c.EsProductoIncompleto,0) AS EsProductoIncompleto,
    ISNULL(c.EstadoProductoIncompleto,N'') AS EstadoProductoIncompleto,
    c.EjecucionReservaID,
    c.ProgramaReservaID,
    c.SolicitudReservaID,
    c.SolicitudDetalleReservaID,
    e.ParteID
FROM dbo.Produccion_Cajas c WITH(UPDLOCK,HOLDLOCK)
INNER JOIN dbo.Produccion_Ejecucion e
    ON e.EjecucionProduccionID=c.EjecucionProduccionID
WHERE c.CajaProduccionID=@CajaProduccionID
  AND c.Activo=1;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value = cajaId;

            await using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
                throw new InvalidOperationException(
                    $"La etiqueta blanca correspondiente a la caja {cajaId} ya no existe o fue desactivada.");

            var etiqueta =
                rd["EtiquetaBlanca"] == DBNull.Value
                    ? rd["FolioCaja"] == DBNull.Value
                        ? $"Caja {cajaId}"
                        : rd["FolioCaja"].ToString() ?? $"Caja {cajaId}"
                    : rd["EtiquetaBlanca"].ToString() ?? $"Caja {cajaId}";

            var parteCaja = rd["ParteID"] == DBNull.Value
                ? 0
                : Convert.ToInt32(rd["ParteID"]);

            var cantidadPiezas = rd["CantidadPiezas"] == DBNull.Value
                ? 0
                : Convert.ToInt32(rd["CantidadPiezas"]);

            var capacidadCaja = rd["CapacidadObjetivoCaja"] == DBNull.Value
                ? 0
                : Convert.ToInt32(rd["CapacidadObjetivoCaja"]);

            var esProductoIncompleto =
                rd["EsProductoIncompleto"] != DBNull.Value &&
                Convert.ToBoolean(rd["EsProductoIncompleto"]);

            var estado =
                rd["EstadoProductoIncompleto"] == DBNull.Value
                    ? string.Empty
                    : rd["EstadoProductoIncompleto"].ToString()?.Trim().ToUpperInvariant()
                      ?? string.Empty;

            var tieneReserva =
                rd["EjecucionReservaID"] != DBNull.Value ||
                rd["ProgramaReservaID"] != DBNull.Value ||
                rd["SolicitudReservaID"] != DBNull.Value ||
                rd["SolicitudDetalleReservaID"] != DBNull.Value;

            if (!esProductoIncompleto)
                throw new InvalidOperationException(
                    $"{etiqueta} ya no está marcada como producto incompleto.");

            if (parteCaja != programa.ParteID.Value)
                throw new InvalidOperationException(
                    $"{etiqueta} corresponde a una parte diferente de la OF que se intenta iniciar.");

            if (cantidadPiezas <= 0)
                throw new InvalidOperationException(
                    $"{etiqueta} ya no contiene piezas disponibles.");

            if (!string.Equals(
                    estado,
                    ProduccionProductoIncompletoEstado.Disponible,
                    StringComparison.OrdinalIgnoreCase) ||
                tieneReserva)
            {
                throw new InvalidOperationException(
                    $"{etiqueta} ya fue reservada o dejó de estar disponible.");
            }

            if (capacidadEsperada.HasValue &&
                capacidadEsperada.Value > 0 &&
                capacidadCaja != capacidadEsperada.Value)
            {
                throw new InvalidOperationException(
                    $"{etiqueta} tiene capacidad de {capacidadCaja:N0} piezas y esta parte utiliza embalaje de {capacidadEsperada.Value:N0} piezas.");
            }

            resultado.Add(new EtiquetaBlancaInicioValidada
            {
                CajaProduccionID = cajaId,
                Etiqueta = etiqueta,
                CantidadPiezas = cantidadPiezas,
                CapacidadObjetivoCaja = capacidadCaja
            });
        }

        var totalSeleccionado =
            resultado.Sum(x => x.CantidadPiezas);

        if (totalSeleccionado > cantidadProgramada)
        {
            throw new InvalidOperationException(
                $"Las etiquetas blancas seleccionadas contienen {totalSeleccionado:N0} piezas, pero el programa requiere {cantidadProgramada:N0}. No se permite dividir una etiqueta blanca.");
        }

        return resultado;
    }

    private async Task ReservarEtiquetasBlancasInicioAsync(
        IReadOnlyCollection<EtiquetaBlancaInicioValidada> etiquetas,
        ProgramaParaProduccion programa,
        int ejecucionProduccionId,
        int usuarioId,
        SqlConnection cn,
        SqlTransaction tx)
    {
        if (etiquetas.Count == 0)
            return;

        foreach (var etiqueta in etiquetas)
        {
            const string sql = @"
UPDATE dbo.Produccion_Cajas
SET EstadoProductoIncompleto=N'RESERVADA',
    ProgramaReservaID=@ProgramaProduccionID,
    SolicitudReservaID=@SolicitudProduccionID,
    SolicitudDetalleReservaID=@SolicitudProduccionDetalleID,
    EjecucionReservaID=@EjecucionProduccionID,
    FechaReservaIncompleto=SYSDATETIME(),
    UsuarioReservaIncompletoID=@UsuarioID,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=SYSDATETIME()
WHERE CajaProduccionID=@CajaProduccionID
  AND Activo=1
  AND ISNULL(EsProductoIncompleto,0)=1
  AND UPPER(LTRIM(RTRIM(ISNULL(EstadoProductoIncompleto,N''))))=N'DISPONIBLE'
  AND EjecucionReservaID IS NULL
  AND ProgramaReservaID IS NULL
  AND SolicitudReservaID IS NULL
  AND SolicitudDetalleReservaID IS NULL;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value =
                etiqueta.CajaProduccionID;

            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value =
                programa.ProgramaProduccionID;

            cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value =
                (object?)programa.SolicitudProduccionID ?? DBNull.Value;

            cmd.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value =
                (object?)programa.SolicitudProduccionDetalleID ?? DBNull.Value;

            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value =
                ejecucionProduccionId;

            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                usuarioId;

            var filas = await cmd.ExecuteNonQueryAsync();

            if (filas != 1)
            {
                throw new InvalidOperationException(
                    $"La etiqueta {etiqueta.Etiqueta} cambió de estado mientras se iniciaba Producción. No se realizó el inicio.");
            }
        }
    }

}
