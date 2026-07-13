using ERP.NSQuell.Models.ViewModels.Almacen;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AlmacenIntegracionController : AlmacenBaseController
{
    public AlmacenIntegracionController(IConfiguration configuration) : base(configuration) { }

    [HttpGet]
    public async Task<IActionResult> StockMP(string codigo, decimal cantidad = 0, CancellationToken cancellationToken = default)
    {
        if (ValidarSesion() != null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(codigo))
            return BadRequest(new { mensaje = "El código de material es obligatorio." });

        await using var connection = await AbrirConexionAsync(cancellationToken);
        const string sql = @"
SELECT TOP 1 MaterialID, Codigo, Nombre, Unidad, Saldo,
       StockMinimo, StockAviso, StockConfigurado, Semaforo
FROM dbo.vw_AlmacenMPInventario
WHERE Codigo=@Codigo;";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Codigo", SqlDbType.NVarChar, 80).Value = codigo.Trim();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return NotFound(new { mensaje = "Material no encontrado." });

        var saldo = DecimalValor(reader, "Saldo");
        return Json(new
        {
            materialId = Entero(reader, "MaterialID"),
            codigo = Texto(reader, "Codigo"),
            material = Texto(reader, "Nombre"),
            unidad = Texto(reader, "Unidad"),
            disponible = saldo,
            requerido = cantidad,
            suficiente = saldo >= cantidad,
            stockMinimo = DecimalValor(reader, "StockMinimo"),
            stockAviso = DecimalValor(reader, "StockAviso"),
            stockConfigurado = Convert.ToBoolean(reader["StockConfigurado"]),
            semaforo = Texto(reader, "Semaforo")
        });
    }

    [HttpGet]
    public async Task<IActionResult> StockPT(string numeroParte, int cantidad = 0, CancellationToken cancellationToken = default)
    {
        if (ValidarSesion() != null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(numeroParte))
            return BadRequest(new { mensaje = "El número de parte es obligatorio." });

        await using var connection = await AbrirConexionAsync(cancellationToken);
        const string sql = @"
SELECT TOP 1 ParteID, NumeroParte, Descripcion, Disponible, Retenido,
       StockMinimo, StockAviso, StockConfigurado, Semaforo
FROM dbo.vw_AlmacenPTInventario
WHERE NumeroParte=@NumeroParte;";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@NumeroParte", SqlDbType.NVarChar, 120).Value = numeroParte.Trim();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return NotFound(new { mensaje = "Número de parte no encontrado." });

        var disponible = Entero(reader, "Disponible");
        return Json(new
        {
            parteId = Entero(reader, "ParteID"),
            numeroParte = Texto(reader, "NumeroParte"),
            descripcion = Texto(reader, "Descripcion"),
            disponible,
            retenido = Entero(reader, "Retenido"),
            requerido = cantidad,
            suficiente = disponible >= cantidad,
            stockMinimo = Entero(reader, "StockMinimo"),
            stockAviso = Entero(reader, "StockAviso"),
            stockConfigurado = Convert.ToBoolean(reader["StockConfigurado"]),
            semaforo = Texto(reader, "Semaforo")
        });
    }

    [HttpGet]
    public async Task<IActionResult> CajasPT(string numeroParte, CancellationToken cancellationToken = default)
    {
        if (ValidarSesion() != null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(numeroParte))
            return BadRequest(new { mensaje = "El número de parte es obligatorio." });

        await using var connection = await AbrirConexionAsync(cancellationToken);
        const string sql = @"
SELECT c.CajaID, c.Etiqueta, c.NumeroCaja, c.LoteEtiqueta,
       c.EstadoCalidad, v.Disponible,
       CONCAT(u.Almacen, ' / ', u.Rack,
           CASE WHEN u.Nivel IS NULL THEN '' ELSE ' / ' + u.Nivel END,
           CASE WHEN u.Posicion IS NULL THEN '' ELSE ' / ' + u.Posicion END) AS Ubicacion
FROM dbo.AlmacenPT_Cajas c
INNER JOIN dbo.ERP_Partes p ON p.ParteID=c.ParteID
INNER JOIN dbo.vw_AlmacenPTInventarioCaja v ON v.CajaID=c.CajaID
LEFT JOIN dbo.ERP_Ubicaciones u ON u.UbicacionID=c.UbicacionID
WHERE p.NumeroParte=@NumeroParte
  AND p.Activo=1
  AND c.Activo=1
  AND v.Disponible>0
ORDER BY c.FechaEntrada, c.CajaID;";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@NumeroParte", SqlDbType.NVarChar, 120).Value = numeroParte.Trim();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var cajas = new List<object>();
        while (await reader.ReadAsync(cancellationToken))
        {
            cajas.Add(new
            {
                cajaId = Entero(reader, "CajaID"),
                etiqueta = Texto(reader, "Etiqueta"),
                numeroCaja = Entero(reader, "NumeroCaja"),
                lote = Texto(reader, "LoteEtiqueta"),
                estadoCalidad = Texto(reader, "EstadoCalidad"),
                disponible = Entero(reader, "Disponible"),
                ubicacion = Texto(reader, "Ubicacion")
            });
        }

        return Json(new { numeroParte = numeroParte.Trim(), cajas });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DescontarMP(
        AlmacenDescuentoMPRequestVm model,
        CancellationToken cancellationToken = default)
    {
        if (ValidarSesion() != null) return Unauthorized();

        model.Codigo = model.Codigo?.Trim() ?? string.Empty;
        model.NumeroOF = model.NumeroOF?.Trim() ?? string.Empty;
        model.ReferenciaOperacion = model.ReferenciaOperacion?.Trim() ?? string.Empty;
        model.Lote = string.IsNullOrWhiteSpace(model.Lote) ? "S/L" : model.Lote.Trim();
        model.Unidad = model.Unidad?.Trim().ToUpperInvariant();

        if (!ModelState.IsValid)
            return BadRequest(new { mensaje = "La solicitud contiene datos inválidos.", errores = ErroresModelo() });

        await using var connection = await AbrirConexionAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            const string duplicadoSql = @"
SELECT TOP 1 MovimientoID
FROM dbo.AlmacenMP_Movimientos WITH (UPDLOCK,HOLDLOCK)
WHERE ReferenciaOperacion=@Referencia AND Activo=1;";
            await using (var duplicado = new SqlCommand(duplicadoSql, connection, transaction))
            {
                duplicado.Parameters.Add("@Referencia", SqlDbType.NVarChar, 120).Value = model.ReferenciaOperacion;
                var movimientoExistente = await duplicado.ExecuteScalarAsync(cancellationToken);
                if (movimientoExistente != null)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return Json(new
                    {
                        aplicado = true,
                        repetido = true,
                        movimientoId = Convert.ToInt64(movimientoExistente),
                        referenciaOperacion = model.ReferenciaOperacion,
                        mensaje = "La operación ya había sido aplicada; no se realizó un segundo descuento."
                    });
                }
            }

            const string materialSql = @"
SELECT TOP 1 m.MaterialID, m.Codigo, m.Nombre, m.UnidadDefault, m.RequiereLote,
       ISNULL(v.Saldo,0) AS Disponible
FROM dbo.ERP_Materiales m WITH (UPDLOCK,HOLDLOCK)
LEFT JOIN dbo.vw_AlmacenMPInventario v ON v.MaterialID=m.MaterialID
WHERE m.Codigo=@Codigo AND m.Activo=1;";

            int materialId;
            string unidad;
            decimal disponible;
            bool requiereLote;
            await using (var material = new SqlCommand(materialSql, connection, transaction))
            {
                material.Parameters.Add("@Codigo", SqlDbType.NVarChar, 80).Value = model.Codigo;
                await using var reader = await material.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return NotFound(new { mensaje = "El material no existe o está inactivo." });
                }

                materialId = Entero(reader, "MaterialID");
                unidad = string.IsNullOrWhiteSpace(model.Unidad) ? Texto(reader, "UnidadDefault") : model.Unidad;
                disponible = DecimalValor(reader, "Disponible");
                requiereLote = Convert.ToBoolean(reader["RequiereLote"]);
            }

            if (requiereLote && model.Lote == "S/L")
            {
                await transaction.RollbackAsync(cancellationToken);
                return BadRequest(new { mensaje = "El material requiere un lote válido para realizar el descuento." });
            }

            if (disponible < model.Cantidad)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Conflict(new
                {
                    mensaje = "Stock MP insuficiente.",
                    disponible,
                    requerido = model.Cantidad,
                    codigo = model.Codigo
                });
            }

            const string insertSql = @"
INSERT dbo.AlmacenMP_Movimientos
(FechaMovimiento, MaterialID, NumeroOF, TipoMovimiento, Lote, Cantidad, Unidad,
 UbicacionID, ResponsableUsuarioID, EntregadoPorNombre, Seguimiento,
 ReferenciaOperacion, FechaCreacion, CreadoPor, Activo,
 RequiereValidacionProduccion, ValidadoProduccion)
OUTPUT INSERTED.MovimientoID
VALUES
(SYSDATETIME(), @MaterialID, @NumeroOF, N'Consumo', @Lote, @Cantidad, @Unidad,
 @UbicacionID, @UsuarioID, @Responsable, @Observaciones,
 @Referencia, SYSUTCDATETIME(), @Responsable, 1, 0, 1);";
            long movimientoId;
            await using (var insert = new SqlCommand(insertSql, connection, transaction))
            {
                insert.Parameters.Add("@MaterialID", SqlDbType.Int).Value = materialId;
                insert.Parameters.Add("@NumeroOF", SqlDbType.NVarChar, 80).Value = model.NumeroOF;
                insert.Parameters.Add("@Lote", SqlDbType.NVarChar, 120).Value = model.Lote;
                var cantidad = insert.Parameters.Add("@Cantidad", SqlDbType.Decimal);
                cantidad.Precision = 18;
                cantidad.Scale = 3;
                cantidad.Value = model.Cantidad;
                insert.Parameters.Add("@Unidad", SqlDbType.NVarChar, 20).Value = unidad;
                insert.Parameters.Add("@UbicacionID", SqlDbType.Int).Value = model.UbicacionID.HasValue ? model.UbicacionID.Value : DBNull.Value;
                insert.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = UsuarioID.HasValue ? UsuarioID.Value : DBNull.Value;
                insert.Parameters.Add("@Responsable", SqlDbType.NVarChar, 180).Value = UsuarioNombre;
                insert.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 800).Value = string.IsNullOrWhiteSpace(model.Observaciones) ? "Consumo solicitado por Planeación." : model.Observaciones.Trim();
                insert.Parameters.Add("@Referencia", SqlDbType.NVarChar, 120).Value = model.ReferenciaOperacion;
                movimientoId = Convert.ToInt64(await insert.ExecuteScalarAsync(cancellationToken));
            }

            await transaction.CommitAsync(cancellationToken);
            return Json(new
            {
                aplicado = true,
                repetido = false,
                movimientoId,
                materialId,
                codigo = model.Codigo,
                cantidad = model.Cantidad,
                unidad,
                disponibleAnterior = disponible,
                disponibleActual = disponible - model.Cantidad,
                numeroOF = model.NumeroOF,
                referenciaOperacion = model.ReferenciaOperacion
            });
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Conflict(new { mensaje = "La referencia de operación ya fue registrada. No se duplicó el descuento." });
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DescontarPT(
        AlmacenDescuentoPTRequestVm model,
        CancellationToken cancellationToken = default)
    {
        if (ValidarSesion() != null) return Unauthorized();

        model.NumeroParte = model.NumeroParte?.Trim() ?? string.Empty;
        model.NumeroOF = model.NumeroOF?.Trim() ?? string.Empty;
        model.ReferenciaOperacion = model.ReferenciaOperacion?.Trim() ?? string.Empty;

        if (!ModelState.IsValid)
            return BadRequest(new { mensaje = "La solicitud contiene datos inválidos.", errores = ErroresModelo() });

        await using var connection = await AbrirConexionAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            const string duplicadoSql = @"
SELECT TOP 1 MovimientoID
FROM dbo.AlmacenPT_Movimientos WITH (UPDLOCK,HOLDLOCK)
WHERE ReferenciaOperacion=@Referencia AND Activo=1;";
            await using (var duplicado = new SqlCommand(duplicadoSql, connection, transaction))
            {
                duplicado.Parameters.Add("@Referencia", SqlDbType.NVarChar, 120).Value = model.ReferenciaOperacion;
                var movimientoExistente = await duplicado.ExecuteScalarAsync(cancellationToken);
                if (movimientoExistente != null)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return Json(new
                    {
                        aplicado = true,
                        repetido = true,
                        movimientoId = Convert.ToInt64(movimientoExistente),
                        referenciaOperacion = model.ReferenciaOperacion,
                        mensaje = "La operación ya había sido aplicada; no se realizó un segundo descuento."
                    });
                }
            }

            const string parteSql = @"
SELECT TOP 1 p.ParteID, p.NumeroParte, p.Descripcion, ISNULL(v.Disponible,0) AS Disponible
FROM dbo.ERP_Partes p WITH (UPDLOCK,HOLDLOCK)
LEFT JOIN dbo.vw_AlmacenPTInventario v ON v.ParteID=p.ParteID
WHERE p.NumeroParte=@NumeroParte AND p.Activo=1;";
            int parteId;
            await using (var parte = new SqlCommand(parteSql, connection, transaction))
            {
                parte.Parameters.Add("@NumeroParte", SqlDbType.NVarChar, 120).Value = model.NumeroParte;
                await using var reader = await parte.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return NotFound(new { mensaje = "El número de parte no existe o está inactivo." });
                }
                parteId = Entero(reader, "ParteID");
            }

            const string cajaSql = @"
SELECT TOP 1 c.ParteID, ISNULL(v.Disponible,0) AS Disponible
FROM dbo.AlmacenPT_Cajas c WITH (UPDLOCK,HOLDLOCK)
LEFT JOIN dbo.vw_AlmacenPTInventarioCaja v ON v.CajaID=c.CajaID
WHERE c.CajaID=@CajaID AND c.Activo=1;";
            int disponible;
            await using (var caja = new SqlCommand(cajaSql, connection, transaction))
            {
                caja.Parameters.Add("@CajaID", SqlDbType.Int).Value = model.CajaID!.Value;
                await using var reader = await caja.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken) || Entero(reader, "ParteID") != parteId)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return BadRequest(new { mensaje = "La caja no existe o no corresponde al número de parte." });
                }
                disponible = Entero(reader, "Disponible");
            }

            if (disponible < model.Cantidad)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Conflict(new
                {
                    mensaje = "Stock PT insuficiente.",
                    disponible,
                    requerido = model.Cantidad,
                    numeroParte = model.NumeroParte,
                    cajaId = model.CajaID
                });
            }

            const string insertSql = @"
INSERT dbo.AlmacenPT_Movimientos
(CajaID, ParteID, NumeroOF, TipoMovimiento, Cantidad, UbicacionID,
 ResponsableUsuarioID, Observaciones, ReferenciaOperacion,
 FechaMovimiento, FechaCreacion, CreadoPor, Activo)
OUTPUT INSERTED.MovimientoID
VALUES
(@CajaID, @ParteID, @NumeroOF, N'Salida', @Cantidad, @UbicacionID,
 @UsuarioID, @Observaciones, @Referencia,
 SYSDATETIME(), SYSUTCDATETIME(), @Usuario, 1);";
            long movimientoId;
            await using (var insert = new SqlCommand(insertSql, connection, transaction))
            {
                insert.Parameters.Add("@CajaID", SqlDbType.Int).Value = model.CajaID.HasValue ? model.CajaID.Value : DBNull.Value;
                insert.Parameters.Add("@ParteID", SqlDbType.Int).Value = parteId;
                insert.Parameters.Add("@NumeroOF", SqlDbType.NVarChar, 80).Value = model.NumeroOF;
                insert.Parameters.Add("@Cantidad", SqlDbType.Int).Value = model.Cantidad;
                insert.Parameters.Add("@UbicacionID", SqlDbType.Int).Value = model.UbicacionID.HasValue ? model.UbicacionID.Value : DBNull.Value;
                insert.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = UsuarioID.HasValue ? UsuarioID.Value : DBNull.Value;
                insert.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 800).Value = string.IsNullOrWhiteSpace(model.Observaciones) ? "Salida solicitada por Planeación." : model.Observaciones.Trim();
                insert.Parameters.Add("@Referencia", SqlDbType.NVarChar, 120).Value = model.ReferenciaOperacion;
                insert.Parameters.Add("@Usuario", SqlDbType.NVarChar, 120).Value = UsuarioNombre;
                movimientoId = Convert.ToInt64(await insert.ExecuteScalarAsync(cancellationToken));
            }

            await transaction.CommitAsync(cancellationToken);
            return Json(new
            {
                aplicado = true,
                repetido = false,
                movimientoId,
                parteId,
                numeroParte = model.NumeroParte,
                cajaId = model.CajaID,
                cantidad = model.Cantidad,
                disponibleAnterior = disponible,
                disponibleActual = disponible - model.Cantidad,
                numeroOF = model.NumeroOF,
                referenciaOperacion = model.ReferenciaOperacion
            });
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Conflict(new { mensaje = "La referencia de operación ya fue registrada. No se duplicó el descuento." });
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private Dictionary<string, string[]> ErroresModelo() =>
        ModelState
            .Where(x => x.Value?.Errors.Count > 0)
            .ToDictionary(
                x => x.Key,
                x => x.Value!.Errors.Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage) ? "Valor inválido." : e.ErrorMessage).ToArray());
}
