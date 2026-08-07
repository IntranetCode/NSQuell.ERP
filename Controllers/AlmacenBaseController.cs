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

    protected static async Task<bool> ExisteColumnaAsync(
        SqlConnection connection,
        string objeto,
        string columna,
        CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT CASE WHEN EXISTS
(
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(@Objeto)
      AND name = @Columna
) THEN 1 ELSE 0 END;";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Objeto", SqlDbType.NVarChar, 256).Value = objeto;
        command.Parameters.Add("@Columna", SqlDbType.NVarChar, 128).Value = columna;
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

    protected static string Csv(string? valor)
    {
        var texto = valor ?? string.Empty;
        if (texto.Length > 0 && texto[0] is '=' or '+' or '-' or '@')
            texto = "'" + texto;

        return $"\"{texto.Replace("\"", "\"\"")}\"";
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
    protected static async Task<List<ERP.NSQuell.Models.ViewModels.Almacen.AlmacenSelectVm>>
        CargarOrdenesFabricacionAsync(
            SqlConnection connection,
            CancellationToken cancellationToken)
    {
        var rows = new List<ERP.NSQuell.Models.ViewModels.Almacen.AlmacenSelectVm>();

        if (!await ExisteObjetoAsync(
                connection,
                "dbo.SolicitudesProduccion",
                "U",
                cancellationToken))
        {
            return rows;
        }

        const string sql = @"
SELECT TOP (500)
    s.SolicitudProduccionID,
    COALESCE
    (
        NULLIF(LTRIM(RTRIM(s.NumeroOFRecibida)), N''),
        NULLIF(LTRIM(RTRIM(s.FolioSolicitud)), N''),
        CONCAT(N'OF-ID-', s.SolicitudProduccionID)
    ) AS NumeroOF,
    ISNULL(NULLIF(LTRIM(RTRIM(c.Nombre)), N''),
           NULLIF(LTRIM(RTRIM(s.ClienteNombre)), N'')) AS Cliente
FROM dbo.SolicitudesProduccion s
LEFT JOIN dbo.ERP_Clientes c
    ON c.ClienteID = s.ClienteID
WHERE s.Activo = 1
ORDER BY s.FechaCreacion DESC, s.SolicitudProduccionID DESC;";

        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var numeroOF = Texto(reader, "NumeroOF");
            var cliente = Texto(reader, "Cliente");

            rows.Add(new ERP.NSQuell.Models.ViewModels.Almacen.AlmacenSelectVm
            {
                Id = Entero(reader, "SolicitudProduccionID"),
                Texto = string.IsNullOrWhiteSpace(cliente)
                    ? numeroOF
                    : $"{numeroOF} · {cliente}",
                Extra = numeroOF
            });
        }

        return rows;
    }

    protected static async Task<string?> ResolverNumeroOFAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        int solicitudProduccionID,
        CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT TOP (1)
    COALESCE
    (
        NULLIF(LTRIM(RTRIM(NumeroOFRecibida)), N''),
        NULLIF(LTRIM(RTRIM(FolioSolicitud)), N''),
        CONCAT(N'OF-ID-', SolicitudProduccionID)
    )
FROM dbo.SolicitudesProduccion WITH (UPDLOCK, HOLDLOCK)
WHERE SolicitudProduccionID = @SolicitudProduccionID
  AND Activo = 1;";

        await using var command = new SqlCommand(sql, connection);
        if (transaction != null)
            command.Transaction = transaction;

        command.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value =
            solicitudProduccionID;

        var value = await command.ExecuteScalarAsync(cancellationToken);
        var numeroOF = value == null || value == DBNull.Value
            ? null
            : value.ToString()?.Trim();

        return string.IsNullOrWhiteSpace(numeroOF) ? null : numeroOF;
    }

}

