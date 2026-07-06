using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ERP.NSQuell.Servicios;
using System.Security.Claims;

namespace ERP.NSQuell.Seguridad
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public class AutorizarAccionAttribute : Attribute, IAsyncAuthorizationFilter
    {
        private readonly string _subMenu;
        private readonly string? _accion;

        public AutorizarAccionAttribute(string subMenu, string? accion = null)
        {
            _subMenu = subMenu;
            _accion = accion;
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var http = context.HttpContext;
            var user = http.User;

            if (!(user?.Identity?.IsAuthenticated ?? false))
            {
                context.Result = new ChallengeResult();
                return;
            }

            var usuarioId = ObtenerUsuarioId(http);

            if (usuarioId <= 0)
            {
                context.Result = new ChallengeResult();
                return;
            }

            var acceso = http.RequestServices.GetRequiredService<IServicioAcceso>();
            var tienePermiso = await acceso.TienePermisoAsync(usuarioId, _subMenu, _accion);

            if (tienePermiso)
            {
                return;
            }

            // Usuario autenticado, pero sin permiso.
            // Esto activa options.AccessDeniedPath = "/Login/AccesoDenegado".
            context.Result = new ForbidResult();
        }

        private static int ObtenerUsuarioId(HttpContext http)
        {
            var user = http.User;

            var usuarioIdClaim =
                user.FindFirstValue("UsuarioID")
                ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? user.FindFirstValue("NameIdentifier");

            if (int.TryParse(usuarioIdClaim, out var usuarioId) && usuarioId > 0)
            {
                return usuarioId;
            }

            var usuarioIdSession = http.Session.GetInt32("UsuarioID");

            if (usuarioIdSession.HasValue && usuarioIdSession.Value > 0)
            {
                return usuarioIdSession.Value;
            }

            return 0;
        }
    }
}
