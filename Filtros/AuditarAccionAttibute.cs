using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Claims;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class AuditarAccionAttribute : Attribute, IAsyncActionFilter
{
    public string Modulo { get; init; } = "";
    public string Entidad { get; init; } = "";

    /// <summary>Si es true, NO registra Index/Lista/Gestionar (reduce ruido)</summary>
    public bool OmitirListas { get; init; } = true;

    /// <summary>Forzar operación: "CREAR" | "EDITAR" | "ELIMINAR" | "VER" | "VER_LISTA" | "VER_DETALLE"</summary>
    public string? Operacion { get; init; }

    /// <summary>No registrar si el usuario NO está autenticado</summary>
    public bool OmitirSiAnonimo { get; init; } = true;

    private static readonly HashSet<string> AccionesDeLista = new(StringComparer.OrdinalIgnoreCase)
    { "Index", "Lista", "Gestionar" };

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;

        // Evitar duplicado si otro filtro del mismo request ya registró
        if (http.Items.ContainsKey("Bitacora_AccionRegistrada"))
        {
            await next();
            return;
        }

        // Si está configurado, no registres anónimos (por ejemplo, páginas públicas)
        if (OmitirSiAnonimo && (http.User?.Identity?.IsAuthenticated != true))
        {
            await next();
            return;
        }

        var metodo = http.Request.Method.ToUpperInvariant();
        var nombreAccion = context.ActionDescriptor.RouteValues.TryGetValue("action", out var act) ? act ?? "" : "";
        var nombre = nombreAccion.ToLowerInvariant();

        // 1) Si te pasaron Operacion explícita, úsala tal cual
        string accion = !string.IsNullOrWhiteSpace(Operacion)
            ? Operacion!.ToUpperInvariant()
            : metodo switch
            {
                "DELETE" => "ELIMINAR",
                "PUT" or "PATCH" => "EDITAR",
                "POST" => nombre.Contains("elimin") || nombre.Contains("borr") || nombre.Contains("remov")
                                ? "ELIMINAR"
                                : nombre.Contains("edit") || nombre.Contains("actualiz")
                                    ? "EDITAR"
                                    : "CREAR",
                _ => "VER"
            };

        // Detectar si es lista o detalle (solo en GET/VER)
        bool esLista = accion == "VER" && AccionesDeLista.Contains(nombreAccion);

        // IdEntidad si viene en parámetros
        string? idEntidad = context.ActionArguments
            .Where(kv => kv.Key.Equals("id", StringComparison.OrdinalIgnoreCase) || kv.Key.EndsWith("Id", StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Value?.ToString())
            .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));

        // Si es lista y se decidió omitir, ejecutar y salir
        if (esLista && OmitirListas && string.IsNullOrWhiteSpace(Operacion))
        {
            await next();
            return;
        }

        // Ajustar nombres cuando es VER si no venía Operacion forzada
        if (accion == "VER" && string.IsNullOrWhiteSpace(Operacion))
            accion = esLista ? "VER_LISTA" : (idEntidad is null ? "VER" : "VER_DETALLE");

        var ejecutado = await next();
        var resultado = (ejecutado.Exception != null && !ejecutado.ExceptionHandled) ? "ERROR" : "OK";

        // Severidad sugerida
        byte severidad = resultado == "ERROR"
            ? (byte)3
            : accion.StartsWith("VER", StringComparison.OrdinalIgnoreCase) ? (byte)1 : (byte)4;

        string? mensaje = resultado == "ERROR" ? ejecutado.Exception?.Message : null;

        // === Datos de contexto para auditoría ===
        var solicitudId = http.Items["SolicitudId"]?.ToString();
        var ip = (http.Items["DireccionIp"]?.ToString()) ?? http.Connection.RemoteIpAddress?.ToString();
        var agenteUsuario = http.Items["AgenteUsuario"]?.ToString();

        // Ids de usuario/empresa (claims primero, sesión como respaldo)
        int? idUsuario = null;
        if (int.TryParse(http.User.FindFirstValue("UsuarioID"), out var uid)) idUsuario = uid;
        else idUsuario = http.Session?.GetInt32("UsuarioID");

      

        try
        {
            var bitacora = http.RequestServices.GetRequiredService<BitacoraService>();

            await bitacora.RegistrarAsync(
                idUsuario: idUsuario,
              
                accion: accion,
                mensaje: mensaje,
                modulo: string.IsNullOrWhiteSpace(Modulo)
                        ? (context.RouteData.Values["controller"]?.ToString()?.ToUpperInvariant() ?? "")
                        : Modulo,
               entidad: string.IsNullOrWhiteSpace(Entidad)
           ? (context.RouteData.Values["controller"]?.ToString()?.TrimEnd('s') ?? "")
           : Entidad,

                entidadId: esLista ? null : idEntidad,
                resultado: resultado,
                severidad: severidad,
                solicitudId: solicitudId,
                ip: ip,
                AgenteUsuario: agenteUsuario
            );

            // Marca para evitar doble log en el mismo request
            http.Items["Bitacora_AccionRegistrada"] = true;
        }
        catch
        {
            // Nunca romper el flujo por fallas de bitácora
        }
    }
}
