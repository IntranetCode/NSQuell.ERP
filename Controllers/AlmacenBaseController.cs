using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.Filters;
using ERP.NSQuell.Servicios;
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

    // REVISION_ALMACEN_V9_PERMISOS
    public override async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        var usuarioID = HttpContext.Session.GetInt32("UsuarioID");
        if (!usuarioID.HasValue)
        {
            context.Result = RedirectToAction("Login", "Login");
            return;
        }

        var controllerName = context.RouteData.Values["controller"]?.ToString() ?? string.Empty;
        var urlPrincipal = controllerName switch
        {
            "AlmacenMP" => "/AlmacenMP/Index",
            "AlmacenEmbalajes" => "/AlmacenEmbalajes/Index",
            "AlmacenOF" => "/AlmacenOF/Index",
            "AlmacenPT" => "/AlmacenPT/Index",
            "AlmacenUbicaciones" => "/AlmacenUbicaciones/Index",
            _ => null
        };

        if (urlPrincipal == null)
        {
            await next();
            return;
        }

        await using var connection = await AbrirConexionAsync(context.HttpContext.RequestAborted);
        const string sql = @"
SELECT TOP (1) sm.Nombre
FROM dbo.SubMenus sm
WHERE sm.Activo = 1
  AND
  (
      sm.UrlEnlace = @UrlPrincipal
      OR sm.UrlEnlace = REPLACE(@UrlPrincipal, N'/Index', N'')
      OR
      (
          @PermitirHerenciaMP = 1
          AND sm.UrlEnlace IN (N'/AlmacenMP/Index', N'/AlmacenMP')
      )
  )
ORDER BY
    CASE WHEN sm.UrlEnlace = @UrlPrincipal THEN 0
         WHEN sm.UrlEnlace = REPLACE(@UrlPrincipal, N'/Index', N'') THEN 1
         ELSE 2 END,
    sm.SubMenuID;";

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@UrlPrincipal", SqlDbType.NVarChar, 500).Value = urlPrincipal;
        command.Parameters.Add("@PermitirHerenciaMP", SqlDbType.Bit).Value =
            controllerName is "AlmacenEmbalajes" or "AlmacenOF";

        var subMenuNombre = (await command.ExecuteScalarAsync(context.HttpContext.RequestAborted))?.ToString();
        if (string.IsNullOrWhiteSpace(subMenuNombre))
        {
            TempData["AlmacenMensajeTipo"] = "warning";
            TempData["AlmacenMensaje"] = "El módulo no tiene un submenú de permisos configurado.";
            context.Result = RedirectToAction("Grupo", "Menu", new { id = 1 });
            return;
        }

        var acceso = HttpContext.RequestServices.GetRequiredService<IServicioAcceso>();
        if (!await acceso.TienePermisoAsync(usuarioID.Value, subMenuNombre))
        {
            TempData["AlmacenMensajeTipo"] = "warning";
            TempData["AlmacenMensaje"] = "No tienes permiso para acceder a este módulo de Almacén.";
            context.Result = RedirectToAction("Grupo", "Menu", new { id = 1 });
            return;
        }

        await next();
    }
}

