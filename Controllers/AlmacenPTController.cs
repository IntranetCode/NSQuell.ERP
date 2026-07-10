using ERP.NSQuell.Models.ViewModels.Almacen;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AlmacenPTController : AlmacenBaseController
{
    private static readonly string[] TiposPermitidos =
    {
        "Salida", "Embarque", "Retencion", "Liberacion", "Retorno", "Scrap", "AjustePositivo", "AjusteNegativo"
    };

    private static readonly string[] EstadosCalidad =
    {
        "Liberado", "Retenido", "GP12", "Cuarentena", "Rechazado"
    };

    public AlmacenPTController(IConfiguration configuration) : base(configuration) { }

    [HttpGet]
    public async Task<IActionResult> Index(string? q, string? estado, CancellationToken cancellationToken)
    {
        var sesion = ValidarSesion();
        if (sesion != null) return sesion;

        var vm = new AlmacenPTIndexVm
        {
            Busqueda = q?.Trim(),
            Estado = estado?.Trim().ToUpperInvariant()
        };

        await using var connection = await AbrirConexionAsync(cancellationToken);
        if (!await ExisteObjetoAsync(connection, "dbo.vw_AlmacenPTInventario", "V", cancellationToken))
        {
            vm.Configurado = false;
            vm.MensajeConfiguracion = "Falta ejecutar Scripts/SQL/Almacen/01_Estructura_Almacen_MP_PT.sql.";
            return View(vm);
        }

        const string sql = @"
SELECT TOP (500)
    ParteID, NumeroParte, Descripcion, Cliente, Cajas,
    Entradas, Salidas, SaldoFisico, Retenido, Disponible,
    StockMinimo, StockAviso, Semaforo, UltimoMovimiento
FROM dbo.vw_AlmacenPTInventario
WHERE (@Q IS NULL OR NumeroParte LIKE '%' + @Q + '%' OR Descripcion LIKE '%' + @Q + '%' OR Cliente LIKE '%' + @Q + '%')
  AND (@Estado IS NULL OR Semaforo = @Estado)
ORDER BY CASE Semaforo WHEN 'ROJO' THEN 1 WHEN 'AMARILLO' THEN 2 ELSE 3 END,
         NumeroParte;";

        await using (var command = new SqlCommand(sql, connection))
        {
            command.Parameters.Add("@Q", SqlDbType.NVarChar, 250).Value = string.IsNullOrWhiteSpace(vm.Busqueda) ? DBNull.Value : vm.Busqueda;
            command.Parameters.Add("@Estado", SqlDbType.NVarChar, 20).Value = string.IsNullOrWhiteSpace(vm.Estado) ? DBNull.Value : vm.Estado;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                vm.Existencias.Add(new AlmacenPTExistenciaVm
                {
                    ParteID = Entero(reader, "ParteID"),
                    NumeroParte = Texto(reader, "NumeroParte"),
                    Descripcion = Texto(reader, "Descripcion"),
                    Cliente = Texto(reader, "Cliente"),
                    Cajas = Entero(reader, "Cajas"),
                    Entradas = Entero(reader, "Entradas"),
                    Salidas = Entero(reader, "Salidas"),
                    SaldoFisico = Entero(reader, "SaldoFisico"),
                    Retenido = Entero(reader, "Retenido"),
                    Disponible = Entero(reader, "Disponible"),
                    StockMinimo = Entero(reader, "StockMinimo"),
                    StockAviso = Entero(reader, "StockAviso"),
                    Semaforo = Texto(reader, "Semaforo"),
                    UltimoMovimiento = Fecha(reader, "UltimoMovimiento")
                });
            }
        }

        const string movimientosSql = @"
SELECT TOP (60)
    m.MovimientoID, m.FechaMovimiento, m.ParteID,
    p.NumeroParte, p.Descripcion, ISNULL(c.Etiqueta,'') AS Etiqueta,
    m.TipoMovimiento, m.Cantidad,
    CONCAT(u.Almacen, ' / ', u.Rack,
        CASE WHEN u.Nivel IS NULL THEN '' ELSE ' / ' + u.Nivel END,
        CASE WHEN u.Posicion IS NULL THEN '' ELSE ' / ' + u.Posicion END) AS Ubicacion,
    ISNULL(m.EstadoCalidad,'') AS EstadoCalidad,
    ISNULL(m.NumeroOF,'') AS NumeroOF,
    COALESCE(pers.Nombre + ' ' + ISNULL(pers.ApellidoPaterno,''), m.CreadoPor, '') AS Responsable
FROM dbo.AlmacenPT_Movimientos m
INNER JOIN dbo.ERP_Partes p ON p.ParteID = m.ParteID
LEFT JOIN dbo.AlmacenPT_Cajas c ON c.CajaID = m.CajaID
LEFT JOIN dbo.ERP_Ubicaciones u ON u.UbicacionID = m.UbicacionID
LEFT JOIN dbo.Usuarios us ON us.UsuarioID = m.ResponsableUsuarioID
LEFT JOIN dbo.Persona pers ON pers.PersonaID = us.PersonaID
WHERE m.Activo=1
ORDER BY m.FechaMovimiento DESC, m.MovimientoID DESC;";

        await using (var command = new SqlCommand(movimientosSql, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                vm.Movimientos.Add(new AlmacenPTMovimientoListaVm
                {
                    MovimientoID = EnteroLargo(reader, "MovimientoID"),
                    FechaMovimiento = Fecha(reader, "FechaMovimiento") ?? DateTime.MinValue,
                    ParteID = Entero(reader, "ParteID"),
                    NumeroParte = Texto(reader, "NumeroParte"),
                    Descripcion = Texto(reader, "Descripcion"),
                    Etiqueta = Texto(reader, "Etiqueta"),
                    TipoMovimiento = Texto(reader, "TipoMovimiento"),
                    Cantidad = Entero(reader, "Cantidad"),
                    Ubicacion = Texto(reader, "Ubicacion"),
                    EstadoCalidad = Texto(reader, "EstadoCalidad"),
                    NumeroOF = Texto(reader, "NumeroOF"),
                    Responsable = Texto(reader, "Responsable")
                });
            }
        }

        vm.TotalPartes = vm.Existencias.Count;
        vm.Criticos = vm.Existencias.Count(x => x.Semaforo == "ROJO");
        vm.Advertencias = vm.Existencias.Count(x => x.Semaforo == "AMARILLO");
        vm.Disponibles = vm.Existencias.Count(x => x.Semaforo == "VERDE");
        vm.PiezasFisicas = vm.Existencias.Sum(x => x.SaldoFisico);
        vm.PiezasRetenidas = vm.Existencias.Sum(x => x.Retenido);
        vm.PiezasDisponibles = vm.Existencias.Sum(x => x.Disponible);
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Entrada(int? parteId, CancellationToken cancellationToken)
    {
        var sesion = ValidarSesion();
        if (sesion != null) return sesion;
        var vm = new AlmacenPTEntradaFormVm { ParteID = parteId.GetValueOrDefault() };
        await CargarEntradaAsync(vm, cancellationToken);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Entrada(AlmacenPTEntradaFormVm model, CancellationToken cancellationToken)
    {
        var sesion = ValidarSesion();
        if (sesion != null) return sesion;
        model.Etiqueta = model.Etiqueta?.Trim() ?? string.Empty;
        model.LoteEtiqueta = model.LoteEtiqueta?.Trim();
        model.NumeroOF = model.NumeroOF?.Trim();

        if (!EstadosCalidad.Contains(model.EstadoCalidad))
            ModelState.AddModelError(nameof(model.EstadoCalidad), "Estado de calidad inválido.");
        if (!ModelState.IsValid)
        {
            await CargarEntradaAsync(model, cancellationToken);
            return View(model);
        }

        await using var connection = await AbrirConexionAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            const string parteSql = "SELECT COUNT(*) FROM dbo.ERP_Partes WITH (UPDLOCK,HOLDLOCK) WHERE ParteID=@Id AND Activo=1;";
            await using (var parteCommand = new SqlCommand(parteSql, connection, transaction))
            {
                parteCommand.Parameters.Add("@Id", SqlDbType.Int).Value = model.ParteID;
                if (Convert.ToInt32(await parteCommand.ExecuteScalarAsync(cancellationToken)) == 0)
                {
                    ModelState.AddModelError(nameof(model.ParteID), "El número de parte no existe o está inactivo.");
                    await transaction.RollbackAsync(cancellationToken);
                    await CargarEntradaAsync(model, cancellationToken);
                    return View(model);
                }
            }

            const string duplicateSql = "SELECT COUNT(*) FROM dbo.AlmacenPT_Cajas WITH (UPDLOCK,HOLDLOCK) WHERE Etiqueta=@Etiqueta;";
            await using (var duplicate = new SqlCommand(duplicateSql, connection, transaction))
            {
                duplicate.Parameters.Add("@Etiqueta", SqlDbType.NVarChar, 120).Value = model.Etiqueta;
                if (Convert.ToInt32(await duplicate.ExecuteScalarAsync(cancellationToken)) > 0)
                {
                    ModelState.AddModelError(nameof(model.Etiqueta), "La etiqueta ya está registrada.");
                    await transaction.RollbackAsync(cancellationToken);
                    await CargarEntradaAsync(model, cancellationToken);
                    return View(model);
                }
            }

            const string cajaSql = @"
INSERT dbo.AlmacenPT_Cajas
(ParteID, NumeroOF, Etiqueta, NumeroCaja, CantidadInicial, LoteEtiqueta,
 EstadoCalidad, UbicacionID, FechaEntrada, FechaCreacion, CreadoPor, Activo)
OUTPUT INSERTED.CajaID
VALUES
(@ParteID,@NumeroOF,@Etiqueta,@NumeroCaja,@Cantidad,@Lote,@Estado,
 @UbicacionID,SYSDATETIME(),SYSUTCDATETIME(),@Usuario,1);";
            int cajaId;
            await using (var caja = new SqlCommand(cajaSql, connection, transaction))
            {
                caja.Parameters.Add("@ParteID", SqlDbType.Int).Value = model.ParteID;
                caja.Parameters.Add("@NumeroOF", SqlDbType.NVarChar, 80).Value = string.IsNullOrWhiteSpace(model.NumeroOF) ? DBNull.Value : model.NumeroOF;
                caja.Parameters.Add("@Etiqueta", SqlDbType.NVarChar, 120).Value = model.Etiqueta;
                caja.Parameters.Add("@NumeroCaja", SqlDbType.Int).Value = model.NumeroCaja;
                caja.Parameters.Add("@Cantidad", SqlDbType.Int).Value = model.Cantidad;
                caja.Parameters.Add("@Lote", SqlDbType.NVarChar, 120).Value = string.IsNullOrWhiteSpace(model.LoteEtiqueta) ? DBNull.Value : model.LoteEtiqueta;
                caja.Parameters.Add("@Estado", SqlDbType.NVarChar, 30).Value = model.EstadoCalidad;
                caja.Parameters.Add("@UbicacionID", SqlDbType.Int).Value = model.UbicacionID.HasValue ? model.UbicacionID.Value : DBNull.Value;
                caja.Parameters.Add("@Usuario", SqlDbType.NVarChar, 120).Value = UsuarioNombre;
                cajaId = Convert.ToInt32(await caja.ExecuteScalarAsync(cancellationToken));
            }

            const string movimientoSql = @"
INSERT dbo.AlmacenPT_Movimientos
(CajaID, ParteID, NumeroOF, TipoMovimiento, Cantidad, UbicacionID,
 EstadoCalidad, ResponsableUsuarioID, Observaciones, FechaMovimiento,
 FechaCreacion, CreadoPor, Activo)
VALUES
(@CajaID,@ParteID,@NumeroOF,'Entrada',@Cantidad,@UbicacionID,
 @Estado,@UsuarioID,@Observaciones,SYSDATETIME(),SYSUTCDATETIME(),@Usuario,1);";
            await using var movimiento = new SqlCommand(movimientoSql, connection, transaction);
            movimiento.Parameters.Add("@CajaID", SqlDbType.Int).Value = cajaId;
            movimiento.Parameters.Add("@ParteID", SqlDbType.Int).Value = model.ParteID;
            movimiento.Parameters.Add("@NumeroOF", SqlDbType.NVarChar, 80).Value = string.IsNullOrWhiteSpace(model.NumeroOF) ? DBNull.Value : model.NumeroOF;
            movimiento.Parameters.Add("@Cantidad", SqlDbType.Int).Value = model.Cantidad;
            movimiento.Parameters.Add("@UbicacionID", SqlDbType.Int).Value = model.UbicacionID.HasValue ? model.UbicacionID.Value : DBNull.Value;
            movimiento.Parameters.Add("@Estado", SqlDbType.NVarChar, 30).Value = model.EstadoCalidad;
            movimiento.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = UsuarioID.HasValue ? UsuarioID.Value : DBNull.Value;
            movimiento.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 800).Value = string.IsNullOrWhiteSpace(model.Observaciones) ? DBNull.Value : model.Observaciones.Trim();
            movimiento.Parameters.Add("@Usuario", SqlDbType.NVarChar, 120).Value = UsuarioNombre;
            await movimiento.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        Mensaje("success", "Entrada de producto terminado registrada.");
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Movimiento(int? parteId, int? cajaId, string? tipo, CancellationToken cancellationToken)
    {
        var sesion = ValidarSesion();
        if (sesion != null) return sesion;
        var vm = new AlmacenPTMovimientoFormVm
        {
            ParteID = parteId.GetValueOrDefault(),
            CajaID = cajaId,
            TipoMovimiento = TiposPermitidos.Contains(tipo ?? string.Empty) ? tipo! : "Salida"
        };
        await CargarMovimientoAsync(vm, cancellationToken);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Movimiento(AlmacenPTMovimientoFormVm model, CancellationToken cancellationToken)
    {
        var sesion = ValidarSesion();
        if (sesion != null) return sesion;
        model.NumeroOF = model.NumeroOF?.Trim();
        if (!TiposPermitidos.Contains(model.TipoMovimiento))
            ModelState.AddModelError(nameof(model.TipoMovimiento), "Tipo de movimiento inválido.");
        if (!string.IsNullOrWhiteSpace(model.EstadoCalidad) && !EstadosCalidad.Contains(model.EstadoCalidad))
            ModelState.AddModelError(nameof(model.EstadoCalidad), "Estado de calidad inválido.");
        if (!ModelState.IsValid)
        {
            await CargarMovimientoAsync(model, cancellationToken);
            return View(model);
        }

        await using var connection = await AbrirConexionAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            if (model.CajaID.HasValue)
            {
                const string cajaSql = "SELECT ParteID FROM dbo.AlmacenPT_Cajas WITH (UPDLOCK,HOLDLOCK) WHERE CajaID=@CajaID AND Activo=1;";
                await using var caja = new SqlCommand(cajaSql, connection, transaction);
                caja.Parameters.Add("@CajaID", SqlDbType.Int).Value = model.CajaID.Value;
                var parteCaja = await caja.ExecuteScalarAsync(cancellationToken);
                if (parteCaja == null || Convert.ToInt32(parteCaja) != model.ParteID)
                {
                    ModelState.AddModelError(nameof(model.CajaID), "La caja no corresponde al número de parte seleccionado.");
                    await transaction.RollbackAsync(cancellationToken);
                    await CargarMovimientoAsync(model, cancellationToken);
                    return View(model);
                }
            }

            if (EsSalidaPT(model.TipoMovimiento) || model.TipoMovimiento == "Retencion")
            {
                var saldoSql = model.CajaID.HasValue
                    ? @"SELECT ISNULL(Disponible,0) FROM dbo.vw_AlmacenPTInventarioCaja WHERE CajaID=@CajaID;"
                    : @"SELECT ISNULL(Disponible,0) FROM dbo.vw_AlmacenPTInventario WHERE ParteID=@ParteID;";
                await using var saldo = new SqlCommand(saldoSql, connection, transaction);
                saldo.Parameters.Add("@CajaID", SqlDbType.Int).Value = model.CajaID.HasValue ? model.CajaID.Value : DBNull.Value;
                saldo.Parameters.Add("@ParteID", SqlDbType.Int).Value = model.ParteID;
                var disponible = Convert.ToInt32(await saldo.ExecuteScalarAsync(cancellationToken) ?? 0);
                if (disponible < model.Cantidad)
                {
                    ModelState.AddModelError(nameof(model.Cantidad), $"Existencia insuficiente. Disponible: {disponible} piezas.");
                    await transaction.RollbackAsync(cancellationToken);
                    await CargarMovimientoAsync(model, cancellationToken);
                    return View(model);
                }
            }

            if (model.TipoMovimiento == "Liberacion")
            {
                var retenidoSql = model.CajaID.HasValue
                    ? @"SELECT ISNULL(Retenido,0) FROM dbo.vw_AlmacenPTInventarioCaja WHERE CajaID=@CajaID;"
                    : @"SELECT ISNULL(Retenido,0) FROM dbo.vw_AlmacenPTInventario WHERE ParteID=@ParteID;";
                await using var retenidoCommand = new SqlCommand(retenidoSql, connection, transaction);
                retenidoCommand.Parameters.Add("@CajaID", SqlDbType.Int).Value = model.CajaID.HasValue ? model.CajaID.Value : DBNull.Value;
                retenidoCommand.Parameters.Add("@ParteID", SqlDbType.Int).Value = model.ParteID;
                var retenido = Convert.ToInt32(await retenidoCommand.ExecuteScalarAsync(cancellationToken) ?? 0);
                if (retenido < model.Cantidad)
                {
                    ModelState.AddModelError(nameof(model.Cantidad), $"No se pueden liberar {model.Cantidad} piezas. Retenido actual: {retenido}.");
                    await transaction.RollbackAsync(cancellationToken);
                    await CargarMovimientoAsync(model, cancellationToken);
                    return View(model);
                }
            }

            const string insertSql = @"
INSERT dbo.AlmacenPT_Movimientos
(CajaID, ParteID, NumeroOF, TipoMovimiento, Cantidad, UbicacionID,
 EstadoCalidad, ResponsableUsuarioID, Observaciones, FechaMovimiento,
 FechaCreacion, CreadoPor, Activo)
VALUES
(@CajaID,@ParteID,@NumeroOF,@Tipo,@Cantidad,@UbicacionID,
 @Estado,@UsuarioID,@Observaciones,SYSDATETIME(),SYSUTCDATETIME(),@Usuario,1);";
            await using var insert = new SqlCommand(insertSql, connection, transaction);
            insert.Parameters.Add("@CajaID", SqlDbType.Int).Value = model.CajaID.HasValue ? model.CajaID.Value : DBNull.Value;
            insert.Parameters.Add("@ParteID", SqlDbType.Int).Value = model.ParteID;
            insert.Parameters.Add("@NumeroOF", SqlDbType.NVarChar, 80).Value = string.IsNullOrWhiteSpace(model.NumeroOF) ? DBNull.Value : model.NumeroOF;
            insert.Parameters.Add("@Tipo", SqlDbType.NVarChar, 30).Value = model.TipoMovimiento;
            insert.Parameters.Add("@Cantidad", SqlDbType.Int).Value = model.Cantidad;
            insert.Parameters.Add("@UbicacionID", SqlDbType.Int).Value = model.UbicacionID.HasValue ? model.UbicacionID.Value : DBNull.Value;
            insert.Parameters.Add("@Estado", SqlDbType.NVarChar, 30).Value = string.IsNullOrWhiteSpace(model.EstadoCalidad) ? DBNull.Value : model.EstadoCalidad;
            insert.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = UsuarioID.HasValue ? UsuarioID.Value : DBNull.Value;
            insert.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 800).Value = string.IsNullOrWhiteSpace(model.Observaciones) ? DBNull.Value : model.Observaciones.Trim();
            insert.Parameters.Add("@Usuario", SqlDbType.NVarChar, 120).Value = UsuarioNombre;
            await insert.ExecuteNonQueryAsync(cancellationToken);

            if (model.CajaID.HasValue && !string.IsNullOrWhiteSpace(model.EstadoCalidad))
            {
                const string updateCaja = @"
UPDATE dbo.AlmacenPT_Cajas
SET EstadoCalidad=@Estado, UbicacionID=COALESCE(@UbicacionID,UbicacionID),
    FechaModificacion=SYSUTCDATETIME(), ActualizadoPor=@Usuario
WHERE CajaID=@CajaID;";
                await using var update = new SqlCommand(updateCaja, connection, transaction);
                update.Parameters.Add("@Estado", SqlDbType.NVarChar, 30).Value = model.EstadoCalidad;
                update.Parameters.Add("@UbicacionID", SqlDbType.Int).Value = model.UbicacionID.HasValue ? model.UbicacionID.Value : DBNull.Value;
                update.Parameters.Add("@Usuario", SqlDbType.NVarChar, 120).Value = UsuarioNombre;
                update.Parameters.Add("@CajaID", SqlDbType.Int).Value = model.CajaID.Value;
                await update.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        Mensaje("success", "Movimiento de Almacén PT registrado correctamente.");
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Configurar(int id, CancellationToken cancellationToken)
    {
        var sesion = ValidarSesion();
        if (sesion != null) return sesion;
        await using var connection = await AbrirConexionAsync(cancellationToken);
        const string sql = "SELECT ParteID,NumeroParte,Descripcion,StockMinimo,StockAviso FROM dbo.ERP_Partes WHERE ParteID=@Id;";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Id", SqlDbType.Int).Value = id;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return NotFound();
        return View(new AlmacenPTStockFormVm
        {
            ParteID = Entero(reader, "ParteID"),
            NumeroParte = Texto(reader, "NumeroParte"),
            Descripcion = Texto(reader, "Descripcion"),
            StockMinimo = Entero(reader, "StockMinimo"),
            StockAviso = Entero(reader, "StockAviso")
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Configurar(AlmacenPTStockFormVm model, CancellationToken cancellationToken)
    {
        var sesion = ValidarSesion();
        if (sesion != null) return sesion;
        if (model.StockAviso < model.StockMinimo)
            ModelState.AddModelError(nameof(model.StockAviso), "El nivel de aviso debe ser igual o mayor al stock mínimo.");
        if (!ModelState.IsValid) return View(model);

        await using var connection = await AbrirConexionAsync(cancellationToken);
        const string sql = @"
UPDATE dbo.ERP_Partes
SET StockMinimo=@Minimo, StockAviso=@Aviso,
    FechaModificacion=GETDATE(), UsuarioModificacionID=@UsuarioID
WHERE ParteID=@Id;";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Minimo", SqlDbType.Int).Value = model.StockMinimo;
        command.Parameters.Add("@Aviso", SqlDbType.Int).Value = model.StockAviso;
        command.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = UsuarioID.HasValue ? UsuarioID.Value : DBNull.Value;
        command.Parameters.Add("@Id", SqlDbType.Int).Value = model.ParteID;
        await command.ExecuteNonQueryAsync(cancellationToken);
        Mensaje("success", "Niveles de stock PT actualizados.");
        return RedirectToAction(nameof(Index));
    }

    private async Task CargarEntradaAsync(AlmacenPTEntradaFormVm vm, CancellationToken cancellationToken)
    {
        await using var connection = await AbrirConexionAsync(cancellationToken);
        vm.Partes = await CargarPartesAsync(connection, cancellationToken);
        vm.Ubicaciones = await CargarUbicacionesAsync(connection, cancellationToken);
        vm.EstadosCalidad = EstadosCalidad.Select((x, i) => new AlmacenSelectVm { Id = i + 1, Texto = x, Extra = x }).ToList();
    }

    private async Task CargarMovimientoAsync(AlmacenPTMovimientoFormVm vm, CancellationToken cancellationToken)
    {
        await using var connection = await AbrirConexionAsync(cancellationToken);
        vm.Partes = await CargarPartesAsync(connection, cancellationToken);
        vm.Ubicaciones = await CargarUbicacionesAsync(connection, cancellationToken);
        vm.TiposMovimiento = TiposPermitidos.Select((x, i) => new AlmacenSelectVm { Id = i + 1, Texto = x, Extra = x }).ToList();
        vm.EstadosCalidad = EstadosCalidad.Select((x, i) => new AlmacenSelectVm { Id = i + 1, Texto = x, Extra = x }).ToList();

        const string cajasSql = @"
SELECT c.CajaID, c.ParteID, c.Etiqueta, c.NumeroCaja,
       ISNULL(v.Disponible,0) AS Disponible
FROM dbo.AlmacenPT_Cajas c
LEFT JOIN dbo.vw_AlmacenPTInventarioCaja v ON v.CajaID=c.CajaID
WHERE c.Activo=1 AND (@ParteID=0 OR c.ParteID=@ParteID)
ORDER BY c.FechaEntrada DESC, c.CajaID DESC;";
        await using var command = new SqlCommand(cajasSql, connection);
        command.Parameters.Add("@ParteID", SqlDbType.Int).Value = vm.ParteID;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            vm.Cajas.Add(new AlmacenSelectVm
            {
                Id = Entero(reader, "CajaID"),
                Texto = $"{Texto(reader, "Etiqueta")} · Caja {Entero(reader, "NumeroCaja")} · Disponible {Entero(reader, "Disponible")}",
                Extra = Entero(reader, "ParteID").ToString()
            });
        }
    }

    private static async Task<List<AlmacenSelectVm>> CargarPartesAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var rows = new List<AlmacenSelectVm>();
        const string sql = @"
SELECT TOP (1500) ParteID, NumeroParte, COALESCE(NULLIF(Designacion,''),Descripcion) AS Texto
FROM dbo.ERP_Partes WHERE Activo=1 ORDER BY NumeroParte;";
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new AlmacenSelectVm
            {
                Id = Entero(reader, "ParteID"),
                Texto = $"{Texto(reader, "NumeroParte")} · {Texto(reader, "Texto")}"
            });
        }
        return rows;
    }

    private static async Task<List<AlmacenSelectVm>> CargarUbicacionesAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var rows = new List<AlmacenSelectVm>();
        const string sql = @"
SELECT UbicacionID, Almacen, Rack, Nivel, Posicion
FROM dbo.ERP_Ubicaciones
WHERE Activo=1 AND (Almacen='PT' OR Almacen='GENERAL')
ORDER BY Almacen,Rack,Nivel,Posicion;";
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new AlmacenSelectVm
            {
                Id = Entero(reader, "UbicacionID"),
                Texto = string.Join(" · ", new[] { Texto(reader, "Almacen"), Texto(reader, "Rack"), Texto(reader, "Nivel"), Texto(reader, "Posicion") }.Where(x => !string.IsNullOrWhiteSpace(x)))
            });
        }
        return rows;
    }
}
