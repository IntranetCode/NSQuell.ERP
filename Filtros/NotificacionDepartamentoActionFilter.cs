// NSQ_NOTIFICACIONES_V3F_EVENTOS_EXPLICITOS
// El filtro global YA NO convierte cada POST/PUT/PATCH/DELETE en una notificacion.
// Solo conserva la deteccion segura de flujos que CREAN una OF real.
// Los demas avisos deben publicarse como eventos de negocio explicitos desde el flujo que hace COMMIT.
using ERP.NSQuell.Servicios;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ERP.NSQuell.Filtros;

public sealed class NotificacionDepartamentoActionFilter : IAsyncActionFilter
{
    private readonly NotificacionDepartamentalService _resolver;
    private readonly NotificacionEventoService _eventoService;
    private readonly ILogger<NotificacionDepartamentoActionFilter> _logger;

    public NotificacionDepartamentoActionFilter(
        NotificacionDepartamentalService resolver,
        NotificacionEventoService eventoService,
        ILogger<NotificacionDepartamentoActionFilter> logger)
    {
        _resolver = resolver;
        _eventoService = eventoService;
        _logger = logger;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var method = context.HttpContext.Request.Method;
        var mutacion = HttpMethods.IsPost(method)
            || HttpMethods.IsPut(method)
            || HttpMethods.IsPatch(method)
            || HttpMethods.IsDelete(method);

        if (!mutacion)
        {
            await next();
            return;
        }

        var controller = context.RouteData.Values["controller"]?.ToString()?.Trim() ?? string.Empty;
        var action = context.RouteData.Values["action"]?.ToString()?.Trim() ?? string.Empty;

        if (controller.Equals("Notificaciones", StringComparison.OrdinalIgnoreCase)
            || controller.Equals("Login", StringComparison.OrdinalIgnoreCase))
        {
            await next();
            return;
        }

        // SolicitudesProduccion/Crear ya publica OF_CREADA explicitamente DESPUES de su COMMIT.
        if (controller.Equals("SolicitudesProduccion", StringComparison.OrdinalIgnoreCase)
            && action.Equals("Crear", StringComparison.OrdinalIgnoreCase))
        {
            await next();
            return;
        }

        // V3F: no existe fallback generico. Guardar/Editar/Eliminar por si solos NO son eventos de negocio.
        if (!EsCreacionOf(controller, action))
        {
            await next();
            return;
        }

        var executed = await next();
        if (executed.Exception != null && !executed.ExceptionHandled) return;
        if (context.HttpContext.Response.StatusCode >= 400) return;
        if (executed.Controller is Controller mvc && mvc.TempData.ContainsKey("Error")) return;

        try
        {
            var solicitudProduccionId = await ResolverSolicitudProduccionIdAsync(
                context,
                executed.Result,
                controller,
                action);

            if (!solicitudProduccionId.HasValue || solicitudProduccionId.Value <= 0)
            {
                _logger.LogWarning(
                    "Flujo {Controller}/{Action} identificado como creacion de OF, pero no se resolvio SolicitudProduccionID. No se publica una notificacion ambigua.",
                    controller,
                    action);
                return;
            }

            var actorId = context.HttpContext.Session.GetInt32("UsuarioID") ?? 0;
            await _eventoService.PublicarOfCreadaAsync(solicitudProduccionId.Value, actorId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error posterior publicando evento explicito para {Controller}/{Action}.",
                controller,
                action);
        }
    }

    private async Task<int?> ResolverSolicitudProduccionIdAsync(
        ActionExecutingContext context,
        IActionResult? result,
        string controller,
        string action)
    {
        foreach (var key in new[] { "solicitudProduccionId", "SolicitudProduccionID", "solicitudId" })
        {
            if (context.ActionArguments.TryGetValue(key, out var raw) && TryInt(raw, out var id))
                return id;
        }

        // El flujo real GenerarOF termina en un Detalle con el ID de la OF creada.
        if (result is RedirectToActionResult redirect
            && redirect.ActionName != null
            && redirect.ActionName.Equals("Detalle", StringComparison.OrdinalIgnoreCase)
            && redirect.RouteValues != null
            && redirect.RouteValues.TryGetValue("id", out var redirectId)
            && TryInt(redirectId, out var ofId))
        {
            return ofId;
        }

        // Respaldo seguro para PlaneacionPrograma/GenerarOF cuando el argumento recibido es ProgramaProduccionID.
        if (controller.Equals("PlaneacionPrograma", StringComparison.OrdinalIgnoreCase)
            && action.Replace("_", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Equals("GenerarOF", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var key in new[] { "programaProduccionId", "ProgramaProduccionID", "id" })
            {
                if (context.ActionArguments.TryGetValue(key, out var raw) && TryInt(raw, out var programaId))
                    return await _resolver.ResolverSolicitudProduccionIdAsync("PROGRAMA", programaId);
            }
        }

        return null;
    }

    private static bool EsCreacionOf(string controller, string action)
    {
        var compacta = action.Replace("_", string.Empty, StringComparison.OrdinalIgnoreCase);

        if (controller.Equals("PlaneacionPrograma", StringComparison.OrdinalIgnoreCase))
            return compacta.Equals("GenerarOF", StringComparison.OrdinalIgnoreCase);

        if (controller.Equals("Planeacion", StringComparison.OrdinalIgnoreCase))
            return compacta.Equals("Crear", StringComparison.OrdinalIgnoreCase)
                || compacta.Equals("CrearOF", StringComparison.OrdinalIgnoreCase)
                || compacta.Equals("GenerarOF", StringComparison.OrdinalIgnoreCase);

        return false;
    }

    private static bool TryInt(object? value, out int id)
    {
        id = 0;
        if (value == null) return false;
        try
        {
            var raw = Convert.ToInt64(value);
            if (raw <= 0 || raw > int.MaxValue) return false;
            id = (int)raw;
            return true;
        }
        catch
        {
            return false;
        }
    }
}