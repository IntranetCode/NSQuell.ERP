using ERP.NSQuell.Models.ViewModels.Almacen;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AlmacenMPController : AlmacenBaseController
{
    private static readonly string[] TiposPermitidos =
    {
        "Entrada", "Salida", "Retorno", "Consumo", "Scrap", "AjustePositivo", "AjusteNegativo"
    };

    public AlmacenMPController(IConfiguration configuration) : base(configuration) { }

    [HttpGet]
    public async Task<IActionResult> Index(string? q, string? estado, string? tipo, CancellationToken cancellationToken)
    {
        var sesion = ValidarSesion();
        if (sesion != null) return sesion;

        var vm = new AlmacenMPIndexVm
        {
            Busqueda = q?.Trim(),
            Estado = estado?.Trim().ToUpperInvariant(),
            Tipo = tipo?.Trim()
        };

        await using var connection = await AbrirConexionAsync(cancellationToken);
        if (!await ExisteObjetoAsync(connection, "dbo.vw_AlmacenMPInventario", "V", cancellationToken))
        {
            vm.Configurado = false;
            vm.MensajeConfiguracion = "Falta ejecutar Scripts/SQL/Almacen/01_Estructura_Almacen_MP_PT.sql.";
            return View(vm);
        }

        const string sql = @"
SELECT TOP (500)
    MaterialID, Codigo, Nombre, TipoMaterial, Unidad,
    Entradas, Salidas, Saldo, StockMinimo, StockAviso,
    Semaforo, UltimoMovimiento
FROM dbo.vw_AlmacenMPInventario
WHERE (@Q IS NULL OR Codigo LIKE '%' + @Q + '%' OR Nombre LIKE '%' + @Q + '%')
  AND (@Estado IS NULL OR Semaforo = @Estado)
  AND (@Tipo IS NULL OR TipoMaterial = @Tipo)
ORDER BY CASE Semaforo WHEN 'ROJO' THEN 1 WHEN 'AMARILLO' THEN 2 ELSE 3 END,
         Nombre;";

        await using (var command = new SqlCommand(sql, connection))
        {
            command.Parameters.Add("@Q", SqlDbType.NVarChar, 250).Value = string.IsNullOrWhiteSpace(vm.Busqueda) ? DBNull.Value : vm.Busqueda;
            command.Parameters.Add("@Estado", SqlDbType.NVarChar, 20).Value = string.IsNullOrWhiteSpace(vm.Estado) ? DBNull.Value : vm.Estado;
            command.Parameters.Add("@Tipo", SqlDbType.NVarChar, 80).Value = string.IsNullOrWhiteSpace(vm.Tipo) ? DBNull.Value : vm.Tipo;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                vm.Existencias.Add(new AlmacenMPExistenciaVm
                {
                    MaterialID = Entero(reader, "MaterialID"),
                    Codigo = Texto(reader, "Codigo"),
                    Nombre = Texto(reader, "Nombre"),
                    TipoMaterial = Texto(reader, "TipoMaterial"),
                    Unidad = Texto(reader, "Unidad"),
                    Entradas = DecimalValor(reader, "Entradas"),
                    Salidas = DecimalValor(reader, "Salidas"),
                    Saldo = DecimalValor(reader, "Saldo"),
                    StockMinimo = DecimalValor(reader, "StockMinimo"),
                    StockAviso = DecimalValor(reader, "StockAviso"),
                    Semaforo = Texto(reader, "Semaforo"),
                    UltimoMovimiento = Fecha(reader, "UltimoMovimiento")
                });
            }
        }

        const string movimientosSql = @"
SELECT TOP (60)
    mm.MovimientoID, mm.FechaMovimiento, mm.MaterialID,
    m.Codigo, m.Nombre AS Material, mm.TipoMovimiento,
    mm.Cantidad, mm.Unidad, mm.Lote,
    CONCAT(u.Almacen, ' / ', u.Rack,
        CASE WHEN u.Nivel IS NULL THEN '' ELSE ' / ' + u.Nivel END,
        CASE WHEN u.Posicion IS NULL THEN '' ELSE ' / ' + u.Posicion END) AS Ubicacion,
    ISNULL(mm.NumeroOF, '') AS NumeroOF,
    COALESCE(mm.EntregadoPorNombre, p.Nombre + ' ' + ISNULL(p.ApellidoPaterno, ''), mm.CreadoPor, '') AS Responsable,
    ISNULL(mm.Seguimiento, '') AS Observaciones
FROM dbo.AlmacenMP_Movimientos mm
INNER JOIN dbo.ERP_Materiales m ON m.MaterialID = mm.MaterialID
LEFT JOIN dbo.ERP_Ubicaciones u ON u.UbicacionID = mm.UbicacionID
LEFT JOIN dbo.Usuarios us ON us.UsuarioID = mm.ResponsableUsuarioID
LEFT JOIN dbo.Persona p ON p.PersonaID = us.PersonaID
WHERE mm.Activo = 1
ORDER BY mm.FechaMovimiento DESC, mm.MovimientoID DESC;";

        await using (var command = new SqlCommand(movimientosSql, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                vm.Movimientos.Add(new AlmacenMPMovimientoListaVm
                {
                    MovimientoID = EnteroLargo(reader, "MovimientoID"),
                    FechaMovimiento = Fecha(reader, "FechaMovimiento") ?? DateTime.MinValue,
                    MaterialID = Entero(reader, "MaterialID"),
                    Codigo = Texto(reader, "Codigo"),
                    Material = Texto(reader, "Material"),
                    TipoMovimiento = Texto(reader, "TipoMovimiento"),
                    Cantidad = DecimalValor(reader, "Cantidad"),
                    Unidad = Texto(reader, "Unidad"),
                    Lote = Texto(reader, "Lote"),
                    Ubicacion = Texto(reader, "Ubicacion"),
                    NumeroOF = Texto(reader, "NumeroOF"),
                    Responsable = Texto(reader, "Responsable"),
                    Observaciones = Texto(reader, "Observaciones")
                });
            }
        }

        vm.TotalMateriales = vm.Existencias.Count;
        vm.Criticos = vm.Existencias.Count(x => x.Semaforo == "ROJO");
        vm.Advertencias = vm.Existencias.Count(x => x.Semaforo == "AMARILLO");
        vm.Disponibles = vm.Existencias.Count(x => x.Semaforo == "VERDE");
        vm.SaldoTotal = vm.Existencias.Sum(x => x.Saldo);
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Materiales(CancellationToken cancellationToken)
    {
        var sesion = ValidarSesion();
        if (sesion != null) return sesion;

        var rows = new List<AlmacenMaterialFormVm>();
        await using var connection = await AbrirConexionAsync(cancellationToken);
        if (!await ExisteObjetoAsync(connection, "dbo.ERP_Materiales", "U", cancellationToken))
        {
            Mensaje("warning", "Primero ejecuta el script de estructura de Almacén.");
            return View(rows);
        }

        const string sql = @"
SELECT MaterialID, Codigo, Nombre, TipoMaterial, UnidadDefault, Proveedor,
       RequiereLote, StockMinimo, StockAviso, Activo
FROM dbo.ERP_Materiales
ORDER BY Activo DESC, TipoMaterial, Codigo;";
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new AlmacenMaterialFormVm
            {
                MaterialID = Entero(reader, "MaterialID"),
                Codigo = Texto(reader, "Codigo"),
                Nombre = Texto(reader, "Nombre"),
                TipoMaterial = Texto(reader, "TipoMaterial"),
                UnidadDefault = Texto(reader, "UnidadDefault"),
                Proveedor = Texto(reader, "Proveedor"),
                RequiereLote = Convert.ToBoolean(reader["RequiereLote"]),
                StockMinimo = DecimalValor(reader, "StockMinimo"),
                StockAviso = DecimalValor(reader, "StockAviso"),
                Activo = Convert.ToBoolean(reader["Activo"])
            });
        }
        return View(rows);
    }

    [HttpGet]
    public async Task<IActionResult> Material(int? id, CancellationToken cancellationToken)
    {
        var sesion = ValidarSesion();
        if (sesion != null) return sesion;
        if (!id.HasValue) return View(new AlmacenMaterialFormVm());

        await using var connection = await AbrirConexionAsync(cancellationToken);
        const string sql = @"
SELECT MaterialID, Codigo, Nombre, TipoMaterial, UnidadDefault, Proveedor,
       RequiereLote, StockMinimo, StockAviso, Activo
FROM dbo.ERP_Materiales WHERE MaterialID = @Id;";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Id", SqlDbType.Int).Value = id.Value;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return NotFound();

        return View(new AlmacenMaterialFormVm
        {
            MaterialID = Entero(reader, "MaterialID"),
            Codigo = Texto(reader, "Codigo"),
            Nombre = Texto(reader, "Nombre"),
            TipoMaterial = Texto(reader, "TipoMaterial"),
            UnidadDefault = Texto(reader, "UnidadDefault"),
            Proveedor = Texto(reader, "Proveedor"),
            RequiereLote = Convert.ToBoolean(reader["RequiereLote"]),
            StockMinimo = DecimalValor(reader, "StockMinimo"),
            StockAviso = DecimalValor(reader, "StockAviso"),
            Activo = Convert.ToBoolean(reader["Activo"])
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Material(AlmacenMaterialFormVm model, CancellationToken cancellationToken)
    {
        var sesion = ValidarSesion();
        if (sesion != null) return sesion;

        model.Codigo = model.Codigo?.Trim() ?? string.Empty;
        model.Nombre = model.Nombre?.Trim() ?? string.Empty;
        model.UnidadDefault = model.UnidadDefault?.Trim().ToUpperInvariant() ?? string.Empty;
        model.TipoMaterial = model.TipoMaterial?.Trim().ToUpperInvariant();
        if (model.StockAviso < model.StockMinimo)
            ModelState.AddModelError(nameof(model.StockAviso), "El nivel de aviso debe ser igual o mayor al stock mínimo.");

        if (!ModelState.IsValid) return View(model);

        await using var connection = await AbrirConexionAsync(cancellationToken);
        const string duplicateSql = @"
SELECT COUNT(*) FROM dbo.ERP_Materiales
WHERE UPPER(Codigo) = UPPER(@Codigo) AND (@Id IS NULL OR MaterialID <> @Id);";
        await using (var duplicate = new SqlCommand(duplicateSql, connection))
        {
            duplicate.Parameters.Add("@Codigo", SqlDbType.NVarChar, 80).Value = model.Codigo;
            duplicate.Parameters.Add("@Id", SqlDbType.Int).Value = model.MaterialID.HasValue ? model.MaterialID.Value : DBNull.Value;
            if (Convert.ToInt32(await duplicate.ExecuteScalarAsync(cancellationToken)) > 0)
            {
                ModelState.AddModelError(nameof(model.Codigo), "Ya existe un material con ese código.");
                return View(model);
            }
        }

        var sql = model.MaterialID.HasValue
            ? @"UPDATE dbo.ERP_Materiales
SET Codigo=@Codigo, Nombre=@Nombre, TipoMaterial=@Tipo, UnidadDefault=@Unidad,
    Proveedor=@Proveedor, RequiereLote=@RequiereLote, StockMinimo=@Minimo,
    StockAviso=@Aviso, Activo=@Activo, FechaModificacion=SYSUTCDATETIME(),
    ActualizadoPor=@Usuario
WHERE MaterialID=@Id;"
            : @"INSERT dbo.ERP_Materiales
(Codigo, Nombre, TipoMaterial, UnidadDefault, Proveedor, RequiereLote,
 StockMinimo, StockAviso, FechaCreacion, CreadoPor, Activo)
VALUES (@Codigo,@Nombre,@Tipo,@Unidad,@Proveedor,@RequiereLote,
        @Minimo,@Aviso,SYSUTCDATETIME(),@Usuario,@Activo);";

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Codigo", SqlDbType.NVarChar, 80).Value = model.Codigo;
        command.Parameters.Add("@Nombre", SqlDbType.NVarChar, 250).Value = model.Nombre;
        command.Parameters.Add("@Tipo", SqlDbType.NVarChar, 80).Value = string.IsNullOrWhiteSpace(model.TipoMaterial) ? DBNull.Value : model.TipoMaterial;
        command.Parameters.Add("@Unidad", SqlDbType.NVarChar, 20).Value = model.UnidadDefault;
        command.Parameters.Add("@Proveedor", SqlDbType.NVarChar, 200).Value = string.IsNullOrWhiteSpace(model.Proveedor) ? DBNull.Value : model.Proveedor.Trim();
        command.Parameters.Add("@RequiereLote", SqlDbType.Bit).Value = model.RequiereLote;
        command.Parameters.Add("@Minimo", SqlDbType.Decimal).Value = model.StockMinimo;
        command.Parameters.Add("@Aviso", SqlDbType.Decimal).Value = model.StockAviso;
        command.Parameters.Add("@Activo", SqlDbType.Bit).Value = model.Activo;
        command.Parameters.Add("@Usuario", SqlDbType.NVarChar, 120).Value = UsuarioNombre;
        command.Parameters.Add("@Id", SqlDbType.Int).Value = model.MaterialID.HasValue ? model.MaterialID.Value : DBNull.Value;
        await command.ExecuteNonQueryAsync(cancellationToken);
        Mensaje("success", model.MaterialID.HasValue ? "Material actualizado." : "Material registrado.");
        return RedirectToAction(nameof(Materiales));
    }

    [HttpGet]
    public async Task<IActionResult> Movimiento(int? materialId, string? tipo, CancellationToken cancellationToken)
    {
        var sesion = ValidarSesion();
        if (sesion != null) return sesion;
        var vm = new AlmacenMPMovimientoFormVm
        {
            MaterialID = materialId.GetValueOrDefault(),
            TipoMovimiento = TiposPermitidos.Contains(tipo ?? string.Empty) ? tipo! : "Entrada"
        };
        await CargarMovimientoAsync(vm, cancellationToken);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Movimiento(AlmacenMPMovimientoFormVm model, CancellationToken cancellationToken)
    {
        var sesion = ValidarSesion();
        if (sesion != null) return sesion;
        model.Lote = string.IsNullOrWhiteSpace(model.Lote) ? "S/L" : model.Lote.Trim();
        model.Unidad = model.Unidad?.Trim().ToUpperInvariant() ?? string.Empty;
        model.NumeroOF = model.NumeroOF?.Trim();

        if (!TiposPermitidos.Contains(model.TipoMovimiento))
            ModelState.AddModelError(nameof(model.TipoMovimiento), "Tipo de movimiento inválido.");
        if (!ModelState.IsValid)
        {
            await CargarMovimientoAsync(model, cancellationToken);
            return View(model);
        }

        await using var connection = await AbrirConexionAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            const string materialSql = @"
SELECT Codigo, Nombre, UnidadDefault, RequiereLote
FROM dbo.ERP_Materiales WITH (UPDLOCK, HOLDLOCK)
WHERE MaterialID=@Id AND Activo=1;";
            string codigo;
            bool requiereLote;
            await using (var materialCommand = new SqlCommand(materialSql, connection, transaction))
            {
                materialCommand.Parameters.Add("@Id", SqlDbType.Int).Value = model.MaterialID;
                await using var reader = await materialCommand.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                {
                    ModelState.AddModelError(nameof(model.MaterialID), "El material no existe o está inactivo.");
                    await transaction.RollbackAsync(cancellationToken);
                    await CargarMovimientoAsync(model, cancellationToken);
                    return View(model);
                }
                codigo = Texto(reader, "Codigo");
                requiereLote = Convert.ToBoolean(reader["RequiereLote"]);
                if (string.IsNullOrWhiteSpace(model.Unidad)) model.Unidad = Texto(reader, "UnidadDefault");
            }

            if (requiereLote && model.Lote == "S/L")
            {
                ModelState.AddModelError(nameof(model.Lote), "Este material requiere lote.");
                await transaction.RollbackAsync(cancellationToken);
                await CargarMovimientoAsync(model, cancellationToken);
                return View(model);
            }

            if (EsSalidaMP(model.TipoMovimiento))
            {
                const string saldoSql = @"
SELECT ISNULL(Saldo,0)
FROM dbo.vw_AlmacenMPInventario
WHERE MaterialID=@MaterialID;";
                await using var saldoCommand = new SqlCommand(saldoSql, connection, transaction);
                saldoCommand.Parameters.Add("@MaterialID", SqlDbType.Int).Value = model.MaterialID;
                var saldo = Convert.ToDecimal(await saldoCommand.ExecuteScalarAsync(cancellationToken) ?? 0m);
                if (saldo < model.Cantidad)
                {
                    ModelState.AddModelError(nameof(model.Cantidad), $"Stock insuficiente para {codigo}. Disponible: {saldo:0.###} {model.Unidad}.");
                    await transaction.RollbackAsync(cancellationToken);
                    await CargarMovimientoAsync(model, cancellationToken);
                    return View(model);
                }
            }

            const string insertSql = @"
INSERT dbo.AlmacenMP_Movimientos
(FechaMovimiento, MaterialID, TipoMovimiento, Lote, Cantidad, Unidad,
 UbicacionID, NumeroOF, ResponsableUsuarioID, EntregadoPorNombre,
 Seguimiento, FechaCreacion, CreadoPor, Activo,
 RequiereValidacionProduccion, ValidadoProduccion)
VALUES
(SYSDATETIME(), @MaterialID, @Tipo, @Lote, @Cantidad, @Unidad,
 @UbicacionID, @NumeroOF, @UsuarioID, @Responsable,
 @Observaciones, SYSUTCDATETIME(), @Responsable, 1, 0, 1);";
            await using var insert = new SqlCommand(insertSql, connection, transaction);
            insert.Parameters.Add("@MaterialID", SqlDbType.Int).Value = model.MaterialID;
            insert.Parameters.Add("@Tipo", SqlDbType.NVarChar, 30).Value = model.TipoMovimiento;
            insert.Parameters.Add("@Lote", SqlDbType.NVarChar, 120).Value = model.Lote;
            insert.Parameters.Add("@Cantidad", SqlDbType.Decimal).Value = model.Cantidad;
            insert.Parameters.Add("@Unidad", SqlDbType.NVarChar, 20).Value = model.Unidad;
            insert.Parameters.Add("@UbicacionID", SqlDbType.Int).Value = model.UbicacionID.HasValue ? model.UbicacionID.Value : DBNull.Value;
            insert.Parameters.Add("@NumeroOF", SqlDbType.NVarChar, 80).Value = string.IsNullOrWhiteSpace(model.NumeroOF) ? DBNull.Value : model.NumeroOF;
            insert.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = UsuarioID.HasValue ? UsuarioID.Value : DBNull.Value;
            insert.Parameters.Add("@Responsable", SqlDbType.NVarChar, 180).Value = UsuarioNombre;
            insert.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 800).Value = string.IsNullOrWhiteSpace(model.Observaciones) ? DBNull.Value : model.Observaciones.Trim();
            await insert.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        Mensaje("success", "Movimiento de Almacén MP registrado correctamente.");
        return RedirectToAction(nameof(Index));
    }

    private async Task CargarMovimientoAsync(AlmacenMPMovimientoFormVm vm, CancellationToken cancellationToken)
    {
        await using var connection = await AbrirConexionAsync(cancellationToken);
        const string materialesSql = @"
SELECT MaterialID, Codigo, Nombre, UnidadDefault
FROM dbo.ERP_Materiales WHERE Activo=1
ORDER BY TipoMaterial, Codigo;";
        await using (var command = new SqlCommand(materialesSql, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                vm.Materiales.Add(new AlmacenSelectVm
                {
                    Id = Entero(reader, "MaterialID"),
                    Texto = $"{Texto(reader, "Codigo")} · {Texto(reader, "Nombre")}",
                    Extra = Texto(reader, "UnidadDefault")
                });
            }
        }
        vm.Ubicaciones = await CargarUbicacionesAsync(connection, "MP", cancellationToken);
        vm.TiposMovimiento = TiposPermitidos.Select((x, i) => new AlmacenSelectVm { Id = i + 1, Texto = x, Extra = x }).ToList();
        if (vm.MaterialID > 0 && string.IsNullOrWhiteSpace(vm.Unidad))
            vm.Unidad = vm.Materiales.FirstOrDefault(x => x.Id == vm.MaterialID)?.Extra ?? "KG";
    }

    private static async Task<List<AlmacenSelectVm>> CargarUbicacionesAsync(SqlConnection connection, string almacen, CancellationToken cancellationToken)
    {
        var rows = new List<AlmacenSelectVm>();
        const string sql = @"
SELECT UbicacionID, Almacen, Rack, Nivel, Posicion
FROM dbo.ERP_Ubicaciones
WHERE Activo=1 AND (Almacen=@Almacen OR Almacen='GENERAL')
ORDER BY Almacen, Rack, Nivel, Posicion;";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Almacen", SqlDbType.NVarChar, 60).Value = almacen;
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
