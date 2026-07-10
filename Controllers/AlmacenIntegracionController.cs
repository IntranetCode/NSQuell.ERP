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
        var sesion = ValidarSesion();
        if (sesion != null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(codigo)) return BadRequest(new { mensaje = "El código de material es obligatorio." });

        await using var connection = await AbrirConexionAsync(cancellationToken);
        const string sql = @"
SELECT TOP 1 MaterialID,Codigo,Nombre,Unidad,Saldo,Semaforo
FROM dbo.vw_AlmacenMPInventario WHERE Codigo=@Codigo;";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Codigo", SqlDbType.NVarChar, 80).Value = codigo.Trim();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return NotFound(new { mensaje = "Material no encontrado." });
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
            semaforo = Texto(reader, "Semaforo")
        });
    }

    [HttpGet]
    public async Task<IActionResult> StockPT(string numeroParte, int cantidad = 0, CancellationToken cancellationToken = default)
    {
        var sesion = ValidarSesion();
        if (sesion != null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(numeroParte)) return BadRequest(new { mensaje = "El número de parte es obligatorio." });

        await using var connection = await AbrirConexionAsync(cancellationToken);
        const string sql = @"
SELECT TOP 1 ParteID,NumeroParte,Descripcion,Disponible,Retenido,Semaforo
FROM dbo.vw_AlmacenPTInventario WHERE NumeroParte=@NumeroParte;";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@NumeroParte", SqlDbType.NVarChar, 120).Value = numeroParte.Trim();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return NotFound(new { mensaje = "Número de parte no encontrado." });
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
            semaforo = Texto(reader, "Semaforo")
        });
    }
}
