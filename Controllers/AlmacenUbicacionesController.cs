using ERP.NSQuell.Models.ViewModels.Almacen;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AlmacenUbicacionesController : AlmacenBaseController
{
    public AlmacenUbicacionesController(IConfiguration configuration) : base(configuration) { }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var sesion = ValidarSesion();
        if (sesion != null) return sesion;
        var vm = new AlmacenUbicacionesIndexVm();
        await using var connection = await AbrirConexionAsync(cancellationToken);
        if (!await ExisteObjetoAsync(connection, "dbo.ERP_Ubicaciones", "U", cancellationToken))
        {
            Mensaje("warning", "Primero ejecuta el script de estructura de Almacén.");
            return View(vm);
        }

        const string sql = @"
SELECT UbicacionID, Almacen, Rack, Nivel, Posicion, Activo
FROM dbo.ERP_Ubicaciones
ORDER BY Activo DESC, Almacen, Rack, Nivel, Posicion;";
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            vm.Ubicaciones.Add(new AlmacenUbicacionVm
            {
                UbicacionID = Entero(reader, "UbicacionID"),
                Almacen = Texto(reader, "Almacen"),
                Rack = Texto(reader, "Rack"),
                Nivel = Texto(reader, "Nivel"),
                Posicion = Texto(reader, "Posicion"),
                Activo = Convert.ToBoolean(reader["Activo"])
            });
        }
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Editar(int? id, CancellationToken cancellationToken)
    {
        var sesion = ValidarSesion();
        if (sesion != null) return sesion;
        if (!id.HasValue) return View(new AlmacenUbicacionVm());

        await using var connection = await AbrirConexionAsync(cancellationToken);
        const string sql = "SELECT UbicacionID,Almacen,Rack,Nivel,Posicion,Activo FROM dbo.ERP_Ubicaciones WHERE UbicacionID=@Id;";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Id", SqlDbType.Int).Value = id.Value;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return NotFound();
        return View(new AlmacenUbicacionVm
        {
            UbicacionID = Entero(reader, "UbicacionID"),
            Almacen = Texto(reader, "Almacen"),
            Rack = Texto(reader, "Rack"),
            Nivel = Texto(reader, "Nivel"),
            Posicion = Texto(reader, "Posicion"),
            Activo = Convert.ToBoolean(reader["Activo"])
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Editar(AlmacenUbicacionVm model, CancellationToken cancellationToken)
    {
        var sesion = ValidarSesion();
        if (sesion != null) return sesion;
        model.Almacen = model.Almacen?.Trim().ToUpperInvariant() ?? string.Empty;
        model.Rack = model.Rack?.Trim().ToUpperInvariant() ?? string.Empty;
        model.Nivel = model.Nivel?.Trim().ToUpperInvariant();
        model.Posicion = model.Posicion?.Trim().ToUpperInvariant();
        if (!new[] { "MP", "EMBALAJES", "PT", "GENERAL", "CUARENTENA", "SCRAP" }.Contains(model.Almacen))
            ModelState.AddModelError(nameof(model.Almacen), "Almacén inválido.");
        if (!ModelState.IsValid) return View(model);

        await using var connection = await AbrirConexionAsync(cancellationToken);
        const string duplicateSql = @"
SELECT COUNT(*) FROM dbo.ERP_Ubicaciones
WHERE Almacen=@Almacen AND Rack=@Rack
  AND ISNULL(Nivel,'')=ISNULL(@Nivel,'')
  AND ISNULL(Posicion,'')=ISNULL(@Posicion,'')
  AND (@Id IS NULL OR UbicacionID<>@Id);";
        await using (var duplicate = new SqlCommand(duplicateSql, connection))
        {
            duplicate.Parameters.Add("@Almacen", SqlDbType.NVarChar, 60).Value = model.Almacen;
            duplicate.Parameters.Add("@Rack", SqlDbType.NVarChar, 120).Value = model.Rack;
            duplicate.Parameters.Add("@Nivel", SqlDbType.NVarChar, 40).Value = string.IsNullOrWhiteSpace(model.Nivel) ? DBNull.Value : model.Nivel;
            duplicate.Parameters.Add("@Posicion", SqlDbType.NVarChar, 40).Value = string.IsNullOrWhiteSpace(model.Posicion) ? DBNull.Value : model.Posicion;
            duplicate.Parameters.Add("@Id", SqlDbType.Int).Value = model.UbicacionID.HasValue ? model.UbicacionID.Value : DBNull.Value;
            if (Convert.ToInt32(await duplicate.ExecuteScalarAsync(cancellationToken)) > 0)
            {
                ModelState.AddModelError(string.Empty, "Ya existe una ubicación con la misma combinación.");
                return View(model);
            }
        }

        var sql = model.UbicacionID.HasValue
            ? @"UPDATE dbo.ERP_Ubicaciones
SET Almacen=@Almacen,Rack=@Rack,Nivel=@Nivel,Posicion=@Posicion,Activo=@Activo,
    FechaModificacion=SYSUTCDATETIME(),ActualizadoPor=@Usuario
WHERE UbicacionID=@Id;"
            : @"INSERT dbo.ERP_Ubicaciones
(Almacen,Rack,Nivel,Posicion,FechaCreacion,CreadoPor,Activo)
VALUES(@Almacen,@Rack,@Nivel,@Posicion,SYSUTCDATETIME(),@Usuario,@Activo);";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Almacen", SqlDbType.NVarChar, 60).Value = model.Almacen;
        command.Parameters.Add("@Rack", SqlDbType.NVarChar, 120).Value = model.Rack;
        command.Parameters.Add("@Nivel", SqlDbType.NVarChar, 40).Value = string.IsNullOrWhiteSpace(model.Nivel) ? DBNull.Value : model.Nivel;
        command.Parameters.Add("@Posicion", SqlDbType.NVarChar, 40).Value = string.IsNullOrWhiteSpace(model.Posicion) ? DBNull.Value : model.Posicion;
        command.Parameters.Add("@Activo", SqlDbType.Bit).Value = model.Activo;
        command.Parameters.Add("@Usuario", SqlDbType.NVarChar, 120).Value = UsuarioNombre;
        command.Parameters.Add("@Id", SqlDbType.Int).Value = model.UbicacionID.HasValue ? model.UbicacionID.Value : DBNull.Value;
        await command.ExecuteNonQueryAsync(cancellationToken);
        Mensaje("success", model.UbicacionID.HasValue ? "Ubicación actualizada." : "Ubicación registrada.");
        return RedirectToAction(nameof(Index));
    }
}
