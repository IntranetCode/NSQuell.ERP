using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers;

public abstract class AlmacenBaseController : Controller
{
    private readonly IConfiguration _configuration;

    protected AlmacenBaseController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    protected string ConnectionString =>
        _configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("No se encontró ConnectionStrings:DefaultConnection.");

    protected IActionResult? ValidarSesion()
    {
        if (HttpContext.Session.GetInt32("UsuarioID") == null)
            return RedirectToAction("Login", "Login");

        return null;
    }

    protected int? UsuarioID => HttpContext.Session.GetInt32("UsuarioID");

    protected string UsuarioNombre =>
        HttpContext.Session.GetString("NombreMostrar")
        ?? HttpContext.Session.GetString("Username")
        ?? User?.Identity?.Name
        ?? "Usuario";

    protected async Task<SqlConnection> AbrirConexionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    protected static async Task<bool> ExisteObjetoAsync(
        SqlConnection connection,
        string nombre,
        string tipo,
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT CASE WHEN OBJECT_ID(@Nombre, @Tipo) IS NULL THEN 0 ELSE 1 END;";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Nombre", SqlDbType.NVarChar, 256).Value = nombre;
        command.Parameters.Add("@Tipo", SqlDbType.NVarChar, 10).Value = tipo;
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    protected static string Texto(SqlDataReader reader, string columna)
    {
        var ordinal = reader.GetOrdinal(columna);
        return reader.IsDBNull(ordinal) ? string.Empty : reader.GetValue(ordinal)?.ToString() ?? string.Empty;
    }

    protected static int Entero(SqlDataReader reader, string columna)
    {
        var ordinal = reader.GetOrdinal(columna);
        return reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal));
    }

    protected static long EnteroLargo(SqlDataReader reader, string columna)
    {
        var ordinal = reader.GetOrdinal(columna);
        return reader.IsDBNull(ordinal) ? 0 : Convert.ToInt64(reader.GetValue(ordinal));
    }

    protected static decimal DecimalValor(SqlDataReader reader, string columna)
    {
        var ordinal = reader.GetOrdinal(columna);
        return reader.IsDBNull(ordinal) ? 0m : Convert.ToDecimal(reader.GetValue(ordinal));
    }

    protected static DateTime? Fecha(SqlDataReader reader, string columna)
    {
        var ordinal = reader.GetOrdinal(columna);
        return reader.IsDBNull(ordinal) ? null : Convert.ToDateTime(reader.GetValue(ordinal));
    }

    protected void Mensaje(string tipo, string texto)
    {
        TempData["AlmacenMensajeTipo"] = tipo;
        TempData["AlmacenMensaje"] = texto;
    }

    protected static bool EsEntradaMP(string tipo) =>
        tipo is "Entrada" or "Retorno" or "AjustePositivo" or "Ajuste";

    protected static bool EsSalidaMP(string tipo) =>
        tipo is "Salida" or "Consumo" or "Scrap" or "AjusteNegativo";

    protected static bool EsEntradaPT(string tipo) =>
        tipo is "Entrada" or "Retorno" or "AjustePositivo";

    protected static bool EsSalidaPT(string tipo) =>
        tipo is "Salida" or "Embarque" or "Scrap" or "AjusteNegativo";
}
