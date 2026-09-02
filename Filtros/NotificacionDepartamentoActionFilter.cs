// NSQ_NOTIFICACIONES_DEPARTAMENTALES_V1_1
using ERP.NSQuell.Servicios;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ERP.NSQuell.Filtros;

public sealed class NotificacionDepartamentoActionFilter
    : IAsyncActionFilter
{
    private readonly NotificacionDepartamentalService _service;
    private readonly ILogger<NotificacionDepartamentoActionFilter> _logger;

    public NotificacionDepartamentoActionFilter(
        NotificacionDepartamentalService service,
        ILogger<NotificacionDepartamentoActionFilter> logger)
    {
        _service = service;
        _logger = logger;
    }

    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        var method =
            context.HttpContext.Request.Method;

        var mutacion =
            HttpMethods.IsPost(method)
            || HttpMethods.IsPut(method)
            || HttpMethods.IsPatch(method)
            || HttpMethods.IsDelete(method);

        if (!mutacion)
        {
            await next();
            return;
        }

        var controller =
            context.RouteData.Values["controller"]
                ?.ToString()
                ?.Trim()
            ?? string.Empty;

        // Evita una recursión al marcar/crear notificaciones.
        if (
            controller.Equals(
                "Notificaciones",
                StringComparison.OrdinalIgnoreCase)
            || controller.Equals(
                "Login",
                StringComparison.OrdinalIgnoreCase)
        )
        {
            await next();
            return;
        }

        var executed = await next();

        if (
            executed.Exception != null
            && !executed.ExceptionHandled
        )
        {
            return;
        }

        if (context.HttpContext.Response.StatusCode >= 400)
            return;

        // Varios controladores del ERP regresan RedirectToAction con
        // TempData["Error"] cuando la operación no se aplicó.
        if (
            executed.Controller is Controller mvc
            && mvc.TempData.ContainsKey("Error")
        )
        {
            return;
        }

        try
        {
            var area =
                await _service.ResolverAreaAsync(controller);

            if (string.IsNullOrWhiteSpace(area))
                return;

            var action =
                context.RouteData.Values["action"]
                    ?.ToString()
                    ?.Trim()
                ?? "Actualizacion";

            var actor =
                context.HttpContext.User.Identity?.Name;

            if (string.IsNullOrWhiteSpace(actor))
            {
                actor =
                    context.HttpContext.Session.GetString("Username")
                    ?? context.HttpContext.Session.GetString("NombreUsuario")
                    ?? "Usuario ERP";
            }

            var idOrigen =
                ObtenerIdOrigen(context.ActionArguments);

            await _service.NotificarAsync(
                area,
                controller,
                action,
                idOrigen,
                actor);
        }
        catch (Exception ex)
        {
            // La notificación jamás debe revertir una operación de negocio
            // que ya fue completada correctamente.
            _logger.LogError(
                ex,
                "Error posterior creando la notificación departamental " +
                "para {Controller}.",
                controller);
        }
    }

    private static int ObtenerIdOrigen(
        IDictionary<string,object?> argumentos)
    {
        foreach (var key in new[]
        {
            "id",
            "Id",
            "ID",
            "inspeccionId",
            "InspeccionID",
            "solicitudId",
            "SolicitudID",
            "embarqueId",
            "EmbarqueID"
        })
        {
            if (
                argumentos.TryGetValue(key,out var value)
                && TryInt(value,out var id)
            )
            {
                return id;
            }
        }

        foreach (var value in argumentos.Values)
        {
            if (value == null)
                continue;

            var properties =
                value.GetType().GetProperties()
                    .Where(p =>
                        p.CanRead
                        && p.Name.EndsWith(
                            "ID",
                            StringComparison.OrdinalIgnoreCase))
                    .Take(8);

            foreach (var property in properties)
            {
                try
                {
                    if (
                        TryInt(
                            property.GetValue(value),
                            out var id)
                    )
                    {
                        return id;
                    }
                }
                catch
                {
                }
            }
        }

        return 0;
    }

    private static bool TryInt(
        object? value,
        out int id)
    {
        id=0;

        if (value==null)
            return false;

        try
        {
            var raw=Convert.ToInt64(value);

            if (
                raw<=0
                || raw>int.MaxValue
            )
            {
                return false;
            }

            id=(int)raw;
            return true;
        }
        catch
        {
            return false;
        }
    }
}