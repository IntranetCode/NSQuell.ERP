using ERP.NSQuell.Models.ViewModels.Almacen;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers;

public sealed partial class AlmacenMPController
{
    [HttpGet]
    public async Task<IActionResult> Detalle(
        int id,
        CancellationToken cancellationToken = default)
    {
        var sesion = ValidarSesion();
        if (sesion != null)
            return sesion;

        if (id <= 0)
        {
            Mensaje("warning", "Selecciona una resina valida.");
            return RedirectToAction(nameof(Index));
        }

        await using var connection =
            await AbrirConexionAsync(cancellationToken);

        if (!await ExisteObjetoAsync(
                connection,
                "dbo.vw_AlmacenMPInventario",
                "V",
                cancellationToken)
            || !await ExisteColumnaAsync(
                connection,
                "dbo.vw_AlmacenMPInventario",
                "Disponible",
                cancellationToken)
            || !await ExisteColumnaAsync(
                connection,
                "dbo.vw_AlmacenMPInventario",
                "Fisico",
                cancellationToken)
            || !await ExisteColumnaAsync(
                connection,
                "dbo.vw_AlmacenMPInventario",
                "Reservado",
                cancellationToken))
        {
            Mensaje("warning", "La vista de inventario MP no contiene el desglose requerido.");
            return RedirectToAction(nameof(Index));
        }

        const string inventarioSql = @"
SELECT
    MaterialID,
    Codigo,
    Nombre,
    Unidad,
    TipoMP,
    Fisico,
    Reservado,
    Disponible,
    UltimoMovimiento
FROM dbo.vw_AlmacenMPInventario
WHERE MaterialID = @MaterialID
ORDER BY OrdenTipo, TipoMP;";

        AlmacenMPDetalleStockVm? vm = null;

        // El reader de inventario debe cerrarse antes de ejecutar la consulta
        // de movimientos sobre la misma conexion.
        await using (var command =
            new SqlCommand(inventarioSql, connection))
        {
            command.Parameters.Add("@MaterialID", SqlDbType.Int).Value = id;

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                vm ??= new AlmacenMPDetalleStockVm
                {
                    MaterialID = Entero(reader, "MaterialID"),
                    Codigo = Texto(reader, "Codigo"),
                    Nombre = Texto(reader, "Nombre"),
                    Unidad = string.IsNullOrWhiteSpace(Texto(reader, "Unidad"))
                        ? "KG"
                        : Texto(reader, "Unidad")
                };

                var tipo = Texto(reader, "TipoMP").Trim().ToUpperInvariant();
                var fisico = Math.Max(0m, DecimalValor(reader, "Fisico"));
                var solicitado = Math.Max(0m, DecimalValor(reader, "Reservado"));
                var stock = Math.Max(0m, DecimalValor(reader, "Disponible"));
                var ultimoMovimiento = Fecha(reader, "UltimoMovimiento");

                if (tipo == "M")
                {
                    vm.FisicoM = fisico;
                    vm.SolicitadoM = solicitado;
                    vm.StockM = stock;
                    vm.UltimoMovimientoM = ultimoMovimiento;
                }
                else
                {
                    vm.FisicoV = fisico;
                    vm.SolicitadoV = solicitado;
                    vm.StockV = stock;
                    vm.UltimoMovimientoV = ultimoMovimiento;
                }
            }
        }

        if (vm == null)
        {
            Mensaje("warning", "La resina no existe o esta inactiva.");
            return RedirectToAction(nameof(Index));
        }

        const string movimientosSql = @"
SELECT TOP (100)
    m.MovimientoID,
    m.FechaMovimiento,
    CASE
        WHEN UPPER(LTRIM(RTRIM(ISNULL(m.TipoMP, N'')))) IN (N'M', N'MOLIDO')
            THEN N'M'
        ELSE N'V'
    END AS TipoExistencia,
    m.TipoMovimiento,
    m.Cantidad,
    ISNULL(NULLIF(LTRIM(RTRIM(m.Unidad)), N''), N'KG') AS Unidad,
    ISNULL(m.NumeroOF, N'') AS NumeroOF,
    CONCAT
    (
        ISNULL(u.Almacen, N''),
        CASE WHEN NULLIF(LTRIM(RTRIM(ISNULL(u.Rack, N''))), N'') IS NULL THEN N'' ELSE N' / ' + u.Rack END,
        CASE WHEN NULLIF(LTRIM(RTRIM(ISNULL(u.Nivel, N''))), N'') IS NULL THEN N'' ELSE N' / ' + u.Nivel END,
        CASE WHEN NULLIF(LTRIM(RTRIM(ISNULL(u.Posicion, N''))), N'') IS NULL THEN N'' ELSE N' / ' + u.Posicion END
    ) AS Ubicacion,
    COALESCE
    (
        NULLIF(LTRIM(RTRIM(m.EntregadoPorNombre)), N''),
        NULLIF(LTRIM(RTRIM(CONCAT(p.Nombre, N' ', p.ApellidoPaterno))), N''),
        m.CreadoPor,
        N''
    ) AS Responsable,
    ISNULL(m.Seguimiento, N'') AS Observaciones
FROM dbo.AlmacenMP_Movimientos m
LEFT JOIN dbo.ERP_Ubicaciones u
    ON u.UbicacionID = m.UbicacionID
LEFT JOIN dbo.Usuarios us
    ON us.UsuarioID = m.ResponsableUsuarioID
LEFT JOIN dbo.Persona p
    ON p.PersonaID = us.PersonaID
WHERE m.Activo = 1
  AND m.MaterialID = @MaterialID
ORDER BY m.FechaMovimiento DESC, m.MovimientoID DESC;";

        await using (var movimientosCommand =
            new SqlCommand(movimientosSql, connection))
        {
            movimientosCommand.Parameters.Add("@MaterialID", SqlDbType.Int).Value = id;

            await using var movimientosReader =
                await movimientosCommand.ExecuteReaderAsync(cancellationToken);

            while (await movimientosReader.ReadAsync(cancellationToken))
            {
                vm.Movimientos.Add(new AlmacenMPDetalleMovimientoVm
                {
                    MovimientoID = Convert.ToInt64(movimientosReader["MovimientoID"]),
                    FechaMovimiento = Fecha(movimientosReader, "FechaMovimiento") ?? DateTime.MinValue,
                    TipoExistencia = Texto(movimientosReader, "TipoExistencia"),
                    TipoMovimiento = Texto(movimientosReader, "TipoMovimiento"),
                    Cantidad = DecimalValor(movimientosReader, "Cantidad"),
                    Unidad = Texto(movimientosReader, "Unidad"),
                    NumeroOF = Texto(movimientosReader, "NumeroOF"),
                    Ubicacion = Texto(movimientosReader, "Ubicacion"),
                    Responsable = Texto(movimientosReader, "Responsable"),
                    Observaciones = Texto(movimientosReader, "Observaciones")
                });
            }
        }

        return View(vm);
    }
}
