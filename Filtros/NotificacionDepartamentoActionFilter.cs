// NSQ_NOTIFICACIONES_INTERNAS_V6
// Motor interno:
// - Solo notifica despues de una mutacion realmente exitosa.
// - Evita avisos de previsualizaciones JSON (requiereConfirmacion=true).
// - Usa eventos detallados cuando existe una entidad de negocio identificable.
// - El boton del navbar solo se muestra cuando existe una URL exacta.
// - OF_CREADA sigue siendo un evento explicito e independiente.
using ERP.NSQuell.Servicios;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Globalization;
using System.Reflection;

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

    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        var method = context.HttpContext.Request.Method;

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
            context.RouteData.Values["controller"]?.ToString()?.Trim()
            ?? string.Empty;

        var action =
            context.RouteData.Values["action"]?.ToString()?.Trim()
            ?? "Actualizacion";

        if (controller.Equals("Notificaciones", StringComparison.OrdinalIgnoreCase)
            || controller.Equals("Login", StringComparison.OrdinalIgnoreCase))
        {
            await next();
            return;
        }

        // Este flujo publica OF_CREADA despues de su propio COMMIT.
        if (controller.Equals("SolicitudesProduccion", StringComparison.OrdinalIgnoreCase)
            && action.Equals("Crear", StringComparison.OrdinalIgnoreCase))
        {
            await next();
            return;
        }

        var esCreacionOf = EsCreacionOf(controller, action);
        var datosAccion = ExtraerDatosAccion(context.ActionArguments);

        var executed = await next();

        if (!ResultadoFueExitoso(executed))
            return;

        try
        {
            if (esCreacionOf)
            {
                var solicitudProduccionId =
                    await ResolverSolicitudProduccionIdAsync(
                        context,
                        executed.Result,
                        controller,
                        action);

                if (!solicitudProduccionId.HasValue
                    || solicitudProduccionId.Value <= 0)
                {
                    _logger.LogWarning(
                        "Flujo {Controller}/{Action} identificado como creacion de OF, " +
                        "pero no se resolvio SolicitudProduccionID.",
                        controller,
                        action);

                    return;
                }

                var actorOfId =
                    context.HttpContext.Session.GetInt32("UsuarioID")
                    ?? 0;

                await _eventoService.PublicarOfCreadaAsync(
                    solicitudProduccionId.Value,
                    actorOfId);

                return;
            }

            var area = await _resolver.ResolverAreaAsync(controller);

            if (string.IsNullOrWhiteSpace(area))
                return;

            var actor =
                context.HttpContext.Session.GetString("NombreMostrar")
                ?? context.HttpContext.User.Identity?.Name
                ?? context.HttpContext.Session.GetString("Username")
                ?? context.HttpContext.Session.GetString("NombreUsuario")
                ?? "Usuario ERP";

            var actorId =
                context.HttpContext.Session.GetInt32("UsuarioID")
                ?? 0;

            var idOrigen =
                ObtenerIdOrigen(context.ActionArguments);

            var urlExacta =
                ResolverUrlDestinoExacta(
                    executed,
                    controller,
                    idOrigen);

            /*
             * Los flujos conocidos consultan la BD DESPUES del COMMIT para
             * construir titulo, detalle e URL desde el registro real creado.
             */
            var manejada =
                await _resolver.NotificarOperacionDetalladaAsync(
                    area,
                    controller,
                    action,
                    actorId,
                    actor,
                    datosAccion);

            if (manejada)
                return;

            // Fallback: conserva cobertura departamental, pero nunca inventa
            // una ruta general de modulo como si fuera un acceso directo.
            await _resolver.NotificarAsync(
                area,
                controller,
                action,
                idOrigen,
                actor,
                solicitudProduccionId: null,
                urlDestino: urlExacta,
                datos: datosAccion);
        }
        catch (Exception ex)
        {
            // Una falla de notificaciones nunca revierte la operacion de negocio.
            _logger.LogError(
                ex,
                "Error posterior creando notificacion interna para {Controller}/{Action}.",
                controller,
                action);
        }
    }

    private static bool ResultadoFueExitoso(
        ActionExecutedContext executed)
    {
        if (executed.Exception != null
            && !executed.ExceptionHandled)
        {
            return false;
        }

        if (executed.HttpContext.Response.StatusCode >= 400)
            return false;

        if (executed.Result is StatusCodeResult status
            && status.StatusCode >= 400)
        {
            return false;
        }

        if (executed.Result is ObjectResult objeto
            && objeto.StatusCode.HasValue
            && objeto.StatusCode.Value >= 400)
        {
            return false;
        }

        if (executed.Controller is Controller mvc
            && mvc.TempData.ContainsKey("Error"))
        {
            return false;
        }

        object? payload =
            executed.Result switch
            {
                JsonResult json => json.Value,
                ObjectResult obj => obj.Value,
                _ => null
            };

        if (payload == null)
            return true;

        if (TryBoolProperty(payload, "ok", out var ok)
            && !ok)
        {
            return false;
        }

        if (TryBoolProperty(payload, "success", out var success)
            && !success)
        {
            return false;
        }

        /*
         * Planeacion/ReprogramarCalendario hace un POST de previsualizacion
         * con ok=true + requiereConfirmacion=true. No es una mutacion confirmada.
         */
        if (TryBoolProperty(
                payload,
                "requiereConfirmacion",
                out var requiereConfirmacion)
            && requiereConfirmacion)
        {
            return false;
        }

        return true;
    }

    private static bool TryBoolProperty(
        object payload,
        string nombre,
        out bool value)
    {
        value = false;

        var property =
            payload.GetType()
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(
                    p => p.Name.Equals(
                        nombre,
                        StringComparison.OrdinalIgnoreCase));

        if (property == null || !property.CanRead)
            return false;

        try
        {
            var raw = property.GetValue(payload);

            if (raw is bool boolean)
            {
                value = boolean;
                return true;
            }

            if (raw != null
                && bool.TryParse(raw.ToString(), out var parsed))
            {
                value = parsed;
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static string? ResolverUrlDestinoExacta(
        ActionExecutedContext executed,
        string controller,
        int idOrigen)
    {
        if (executed.Result is LocalRedirectResult local
            && EsRutaLocal(local.Url)
            && !EsRutaGeneralDeModulo(local.Url))
        {
            return local.Url;
        }

        if (executed.Result is RedirectResult redirect
            && EsRutaLocal(redirect.Url)
            && !EsRutaGeneralDeModulo(redirect.Url))
        {
            return redirect.Url;
        }

        if (executed.Controller is not Controller mvc)
            return null;

        if (executed.Result is RedirectToActionResult redirectAction)
        {
            var accionDestino =
                redirectAction.ActionName?.Trim()
                ?? string.Empty;

            /*
             * Un Index no identifica el objeto afectado. No se usa como
             * "acceso directo" porque obliga al usuario a volver a buscar.
             */
            if (!accionDestino.Equals(
                    "Index",
                    StringComparison.OrdinalIgnoreCase))
            {
                var controllerDestino =
                    string.IsNullOrWhiteSpace(
                        redirectAction.ControllerName)
                        ? controller
                        : redirectAction.ControllerName;

                var url =
                    mvc.Url.Action(
                        redirectAction.ActionName,
                        controllerDestino,
                        redirectAction.RouteValues);

                if (EsRutaLocal(url)
                    && !EsRutaGeneralDeModulo(url))
                {
                    return url;
                }
            }
        }

        if (executed.Result is RedirectToRouteResult redirectRoute)
        {
            var url =
                mvc.Url.RouteUrl(
                    redirectRoute.RouteName,
                    redirectRoute.RouteValues);

            if (EsRutaLocal(url)
                && !EsRutaGeneralDeModulo(url))
            {
                return url;
            }
        }

        if (idOrigen > 0
            && TieneAccion(
                executed.Controller.GetType(),
                "Detalle"))
        {
            var url =
                mvc.Url.Action(
                    "Detalle",
                    controller,
                    new { id = idOrigen });

            if (EsRutaLocal(url))
                return url;
        }

        return null;
    }

    private static bool TieneAccion(
        Type controllerType,
        string action)
    {
        return controllerType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Any(
                m => !m.IsSpecialName
                     && m.Name.Equals(
                         action,
                         StringComparison.OrdinalIgnoreCase));
    }

    private static bool EsRutaLocal(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        var value = url.Trim();

        return value.StartsWith("/", StringComparison.Ordinal)
            && !value.StartsWith("//", StringComparison.Ordinal);
    }

    private static bool EsRutaGeneralDeModulo(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return true;

        var path =
            url.Split('?', '#')[0]
                .TrimEnd('/');

        if (path.EndsWith(
                "/Index",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var segmentos =
            path.Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries);

        return segmentos.Length <= 1;
    }

    private async Task<int?> ResolverSolicitudProduccionIdAsync(
        ActionExecutingContext context,
        IActionResult? result,
        string controller,
        string action)
    {
        foreach (var key in new[]
        {
            "solicitudProduccionId",
            "SolicitudProduccionID",
            "solicitudId"
        })
        {
            if (context.ActionArguments.TryGetValue(
                    key,
                    out var raw)
                && TryInt(raw, out var id))
            {
                return id;
            }
        }

        if (result is RedirectToActionResult redirect
            && redirect.ActionName != null
            && redirect.ActionName.Equals(
                "Detalle",
                StringComparison.OrdinalIgnoreCase)
            && redirect.RouteValues != null
            && redirect.RouteValues.TryGetValue(
                "id",
                out var redirectId)
            && TryInt(
                redirectId,
                out var ofId))
        {
            return ofId;
        }

        if (controller.Equals(
                "PlaneacionPrograma",
                StringComparison.OrdinalIgnoreCase)
            && action.Replace(
                    "_",
                    string.Empty,
                    StringComparison.OrdinalIgnoreCase)
                .Equals(
                    "GenerarOF",
                    StringComparison.OrdinalIgnoreCase))
        {
            foreach (var key in new[]
            {
                "programaProduccionId",
                "ProgramaProduccionID",
                "id"
            })
            {
                if (context.ActionArguments.TryGetValue(
                        key,
                        out var raw)
                    && TryInt(raw, out var programaId))
                {
                    return await _resolver
                        .ResolverSolicitudProduccionIdAsync(
                            "PROGRAMA",
                            programaId);
                }
            }
        }

        return null;
    }

    private static bool EsCreacionOf(
        string controller,
        string action)
    {
        var compacta =
            action.Replace(
                "_",
                string.Empty,
                StringComparison.OrdinalIgnoreCase);

        if (controller.Equals(
                "PlaneacionPrograma",
                StringComparison.OrdinalIgnoreCase))
        {
            return compacta.Equals(
                "GenerarOF",
                StringComparison.OrdinalIgnoreCase);
        }

        if (controller.Equals(
                "Planeacion",
                StringComparison.OrdinalIgnoreCase))
        {
            return compacta.Equals(
                    "Crear",
                    StringComparison.OrdinalIgnoreCase)
                || compacta.Equals(
                    "CrearOF",
                    StringComparison.OrdinalIgnoreCase)
                || compacta.Equals(
                    "GenerarOF",
                    StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static readonly HashSet<string> DatosPermitidos =
        new(
            new[]
            {
                "OperacionToken",
                "ConfirmarMovimiento",
                "ProgramaProduccionID",
                "MaquinaID",
                "Inicio",
                "SolicitudProduccionID",
                "SolicitudProduccionDetalleID",
                "MaterialID",
                "MaterialSolicitadoID",
                "EmbalajeID",
                "EmbalajeSolicitadoID",
                "NumeroOF",
                "TipoMovimiento",
                "TipoMP",
                "Cantidad",
                "CantidadVirgen",
                "CantidadMolido",
                "Observaciones",
                "Motivo",
                "FolioCompra",
                "Lote"
            },
            StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, string?>
        ExtraerDatosAccion(
            IDictionary<string, object?> argumentos)
    {
        var salida =
            new Dictionary<string, string?>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var par in argumentos)
        {
            if (DatosPermitidos.Contains(par.Key))
            {
                salida[par.Key] =
                    ConvertirDato(par.Value);
            }

            var value = par.Value;

            if (value == null
                || EsValorSimple(value.GetType()))
            {
                continue;
            }

            var properties =
                value.GetType()
                    .GetProperties(
                        BindingFlags.Instance
                        | BindingFlags.Public);

            foreach (var property in properties)
            {
                if (!property.CanRead
                    || !DatosPermitidos.Contains(
                        property.Name))
                {
                    continue;
                }

                try
                {
                    salida[property.Name] =
                        ConvertirDato(
                            property.GetValue(value));
                }
                catch
                {
                }
            }
        }

        return salida;
    }

    private static bool EsValorSimple(Type type)
    {
        type =
            Nullable.GetUnderlyingType(type)
            ?? type;

        return type.IsPrimitive
            || type.IsEnum
            || type == typeof(string)
            || type == typeof(decimal)
            || type == typeof(DateTime)
            || type == typeof(DateTimeOffset)
            || type == typeof(Guid);
    }

    private static string? ConvertirDato(object? value)
    {
        if (value == null)
            return null;

        return value switch
        {
            DateTime fecha =>
                fecha.ToString(
                    "O",
                    CultureInfo.InvariantCulture),

            DateTimeOffset fecha =>
                fecha.ToString(
                    "O",
                    CultureInfo.InvariantCulture),

            bool boolean =>
                boolean ? "true" : "false",

            IFormattable formattable =>
                formattable.ToString(
                    null,
                    CultureInfo.InvariantCulture),

            _ => value.ToString()
        };
    }

    private static int ObtenerIdOrigen(
        IDictionary<string, object?> argumentos)
    {
        foreach (var key in new[]
        {
            "id",
            "Id",
            "ID",
            "movimientoId",
            "MovimientoID",
            "ejecucionProduccionId",
            "EjecucionProduccionID",
            "programaProduccionId",
            "ProgramaProduccionID",
            "inspeccionId",
            "InspeccionID",
            "monitoreoId",
            "MonitoreoID",
            "solicitudId",
            "SolicitudID",
            "solicitudProduccionId",
            "SolicitudProduccionID",
            "embarqueId",
            "EmbarqueID"
        })
        {
            if (argumentos.TryGetValue(
                    key,
                    out var value)
                && TryInt(
                    value,
                    out var id))
            {
                return id;
            }
        }

        foreach (var value in argumentos.Values)
        {
            if (value == null)
                continue;

            var properties =
                value.GetType()
                    .GetProperties()
                    .Where(
                        p => p.CanRead
                             && p.Name.EndsWith(
                                 "ID",
                                 StringComparison.OrdinalIgnoreCase))
                    .Take(16);

            foreach (var property in properties)
            {
                try
                {
                    if (TryInt(
                            property.GetValue(value),
                            out var id))
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
        id = 0;

        if (value == null)
            return false;

        try
        {
            var raw = Convert.ToInt64(value);

            if (raw <= 0
                || raw > int.MaxValue)
            {
                return false;
            }

            id = (int)raw;
            return true;
        }
        catch
        {
            return false;
        }
    }
}