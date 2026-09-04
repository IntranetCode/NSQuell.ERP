using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading;
using System.Linq;
using System.Threading.Tasks;

namespace ERP.NSQuell.ViewComponents;

// NSQ_GLOBAL_NAV_V1_COMPONENT
public sealed class GlobalDepartmentNavigationViewComponent : ViewComponent
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<GlobalDepartmentNavigationViewComponent> _logger;
    private readonly IWebHostEnvironment _environment;
    private readonly IActionDescriptorCollectionProvider _actionProvider;

    public GlobalDepartmentNavigationViewComponent(
        IConfiguration configuration,
        ILogger<GlobalDepartmentNavigationViewComponent> logger,
        IWebHostEnvironment environment,
        IActionDescriptorCollectionProvider actionProvider)
    {
        _configuration = configuration;
        _logger = logger;
        _environment = environment;
        _actionProvider = actionProvider;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var vm = new GlobalDepartmentNavigationVm
        {
            CurrentPath = HttpContext.Request.Path.Value ?? string.Empty,
            CurrentController = ViewContext.RouteData.Values["controller"]?.ToString() ?? string.Empty
        };

        var usuarioId = HttpContext.Session.GetInt32("UsuarioID");
        if (!usuarioId.HasValue || usuarioId.Value <= 0)
            return View(vm);

        try
        {
            var cnn = _configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(cnn))
                return View(vm);

            await using var cn = new SqlConnection(cnn);
            await cn.OpenAsync();

            const string sql = @"
WITH Perms AS
(
    SELECT DISTINCT SubMenuID
    FROM dbo.fn_PermisosEfectivosUsuario(@UsuarioID)
    WHERE TienePermiso = 1
)
SELECT
    g.MenuGrupoID,
    g.Nombre AS GrupoNombre,
    g.Descripcion AS GrupoDescripcion,
    ISNULL(g.IconoCss,N'fa-solid fa-layer-group') AS GrupoIcono,
    ISNULL(g.Orden,0) AS GrupoOrden,
    m.MenuID,
    m.Nombre AS MenuNombre,
    m.Descripcion AS MenuDescripcion,
    ISNULL(m.IconoCss,N'fa-solid fa-folder') AS MenuIcono,
    ISNULL(m.Orden,0) AS MenuOrden,
    sm.SubMenuID,
    sm.Nombre AS SubMenuNombre,
    sm.UrlEnlace
FROM dbo.SubMenus sm
INNER JOIN Perms p
    ON p.SubMenuID=sm.SubMenuID
INNER JOIN dbo.Menus m
    ON m.MenuID=sm.MenuID
INNER JOIN dbo.MenuGrupo g
    ON g.MenuGrupoID=m.MenuGrupoID
WHERE ISNULL(sm.Activo,1)=1
  AND ISNULL(m.Activo,1)=1
  AND ISNULL(g.Activo,1)=1
  AND NULLIF(LTRIM(RTRIM(sm.UrlEnlace)),N'') IS NOT NULL
ORDER BY
    ISNULL(g.Orden,0),
    g.Nombre,
    ISNULL(m.Orden,0),
    m.Nombre,
    sm.SubMenuID;";

            var groups = new Dictionary<int, GlobalDepartmentNavigationGroupVm>();
            var menus = new Dictionary<int, GlobalDepartmentNavigationMenuVm>();

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId.Value;

            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                var groupId = Convert.ToInt32(rd["MenuGrupoID"]);
                var menuId = Convert.ToInt32(rd["MenuID"]);

                if (!groups.TryGetValue(groupId, out var group))
                {
                    group = new GlobalDepartmentNavigationGroupVm
                    {
                        MenuGrupoID = groupId,
                        Nombre = Text(rd, "GrupoNombre") ?? "Departamento",
                        Descripcion = Text(rd, "GrupoDescripcion"),
                        Icono = Text(rd, "GrupoIcono") ?? "fa-solid fa-layer-group",
                        Orden = Int(rd, "GrupoOrden")
                    };
                    groups.Add(groupId, group);
                }

                if (!menus.TryGetValue(menuId, out var menu))
                {
                    menu = new GlobalDepartmentNavigationMenuVm
                    {
                        MenuID = menuId,
                        MenuGrupoID = groupId,
                        Nombre = Text(rd, "MenuNombre") ?? "Menu",
                        Descripcion = Text(rd, "MenuDescripcion"),
                        Icono = Text(rd, "MenuIcono") ?? "fa-solid fa-folder",
                        Orden = Int(rd, "MenuOrden")
                    };
                    menus.Add(menuId, menu);
                    group.Menus.Add(menu);
                }

                var rawUrl = Text(rd, "UrlEnlace");
                var rawName = Text(rd, "SubMenuNombre");
                var remappedUrl = RemapLegacyNavigationUrl(rawUrl);
                var url = NormalizeLocalUrl(remappedUrl);
                if (url == null)
                    continue;

                menu.SubMenus.Add(new GlobalDepartmentNavigationSubMenuVm
                {
                    SubMenuID = Convert.ToInt32(rd["SubMenuID"]),
                    Nombre = RemapLegacyNavigationName(rawUrl, rawName) ?? "Abrir",
                    Url = url
                });
            }

            vm.Groups = groups.Values
                .OrderBy(x => x.Orden)
                .ThenBy(x => x.Nombre)
                .ToList();

            EnsureCompatibilityLinks(vm);
            EnrichPermittedMenuSectionsFromViews(vm);
            ResolveNavigationState(vm);
// NSQ_PRODUCCION_NAVBAR_SOLO_VISTA_OPERATIVA_V1
// Regla EXCLUSIVA de presentacion del navbar para Produccion.
// /Menu/Grupo/2 sigue mostrando todos los menus permitidos por BD.
            var produccionNavbar = vm.Groups.FirstOrDefault(x =>
                x.MenuGrupoID == 2 ||
                NormalizeNavigationToken(x.Nombre).Equals(
                    "PRODUCCION",
                    StringComparison.OrdinalIgnoreCase));

            if (produccionNavbar != null)
            {
                var vistaOperativa = produccionNavbar.Menus
                    .Where(menu =>
                        NormalizeNavigationToken(menu.Nombre).Equals(
                            "VISTAOPERATIVA",
                            StringComparison.OrdinalIgnoreCase)
                        ||
                        menu.SubMenus.Any(sub =>
                            PathOnly(sub.Url).Equals(
                                "/Produccion/CalendarioOperativo",
                                StringComparison.OrdinalIgnoreCase)))
                    .OrderBy(menu => menu.Orden)
                    .ThenBy(menu => menu.Nombre)
                    .FirstOrDefault();

                if (vistaOperativa != null)
                {
                    produccionNavbar.Menus =
                        new List<GlobalDepartmentNavigationMenuVm>
                        {
                            vistaOperativa
                        };
                }
            }
        }
        catch (Exception ex)
        {
            vm.LoadFailed = true;
            _logger.LogWarning(ex, "No fue posible cargar la navegacion global dinamica del ERP.");
        }

        return View(vm);
    }

    private static string? Text(SqlDataReader rd, string column)
    {
        var ordinal = rd.GetOrdinal(column);
        if (rd.IsDBNull(ordinal)) return null;
        var value = rd.GetValue(ordinal)?.ToString()?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static int Int(SqlDataReader rd, string column)
    {
        var ordinal = rd.GetOrdinal(column);
        return rd.IsDBNull(ordinal) ? 0 : Convert.ToInt32(rd.GetValue(ordinal));
    }

    // NSQ_GLOBAL_NAV_V4_COMPONENT
    private static string? RemapLegacyNavigationUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return raw;

        var value = raw.Trim();
        var path = value.Split('?', '#')[0].TrimEnd('/');

        if (path.Equals("/AlmacenMaterialOF/Entregados", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("AlmacenMaterialOF/Entregados", StringComparison.OrdinalIgnoreCase))
        {
            return "/AlmacenOF/Historico";
        }

        return raw;
    }

    private static string? RemapLegacyNavigationName(string? rawUrl, string? rawName)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
            return rawName;

        var path = rawUrl.Trim().Split('?', '#')[0].TrimEnd('/');

        if (path.Equals("/AlmacenMaterialOF/Entregados", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("AlmacenMaterialOF/Entregados", StringComparison.OrdinalIgnoreCase))
        {
            return "Histórico de OF entregadas";
        }

        return rawName;
    }

    private void EnrichPermittedMenuSectionsFromViews(GlobalDepartmentNavigationVm vm)
    {
        foreach (var group in vm.Groups)
        {
            foreach (var menu in group.Menus)
            {
                var controllers = menu.SubMenus
                    .Select(x => ControllerFromUrl(x.Url))
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (controllers.Count == 0)
                    continue;

                var nextSyntheticId = -100000 - Math.Abs(menu.MenuID * 100);

                foreach (var controller in controllers)
                {
                    // NSQ_NAV_EXACT_OWNER_V5_ENRICH
                    // Si dos menus del mismo departamento comparten controlador
                    // (p.ej. Calidad y Hojas de Control), no mezclar sus acciones.
                    var explicitOwners = group.Menus.Count(candidate =>
                        candidate.SubMenus.Any(s =>
                            string.Equals(
                                ControllerFromUrl(s.Url),
                                controller,
                                StringComparison.OrdinalIgnoreCase)));

                    if (explicitOwners > 1)
                        continue;

                    var viewDirectory = Path.Combine(
                        _environment.ContentRootPath,
                        "Views",
                        controller);

                    if (!Directory.Exists(viewDirectory))
                        continue;

                    var actions = _actionProvider.ActionDescriptors.Items
                        .OfType<ControllerActionDescriptor>()
                        .Where(x =>
                            x.ControllerName.Equals(
                                controller,
                                StringComparison.OrdinalIgnoreCase))
                        .Where(IsReadOnlyNavigableAction)
                        .Where(x => IsUsefulNavigationActionName(x.ActionName))
                        .GroupBy(
                            x => x.ActionName,
                            StringComparer.OrdinalIgnoreCase)
                        .Select(x => x.First())
                        .OrderBy(x => NavigationActionOrder(x.ActionName))
                        .ThenBy(x => FriendlyActionName(x.ActionName))
                        .Take(10)
                        .ToList();

                    foreach (var action in actions)
                    {
                        var viewPath = Path.Combine(
                            viewDirectory,
                            action.ActionName + ".cshtml");

                        if (!File.Exists(viewPath))
                            continue;

                        var url = "/" + controller + "/" + action.ActionName;

                        if (menu.SubMenus.Any(x =>
                            string.Equals(
                                PathOnly(x.Url),
                                PathOnly(url),
                                StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }

                        menu.SubMenus.Add(new GlobalDepartmentNavigationSubMenuVm
                        {
                            SubMenuID = nextSyntheticId--,
                            Nombre = FriendlyActionName(action.ActionName),
                            Url = url
                        });
                    }
                }
            }
        }
    }

    private static bool IsReadOnlyNavigableAction(ControllerActionDescriptor action)
    {
        var httpProviders = action.MethodInfo
            .GetCustomAttributes(inherit: true)
            .OfType<IActionHttpMethodProvider>()
            .ToList();

        if (httpProviders.Count > 0)
        {
            var methods = httpProviders
                .SelectMany(x => x.HttpMethods ?? Array.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            if (methods.Count > 0 &&
                !methods.Any(x =>
                    x.Equals("GET", StringComparison.OrdinalIgnoreCase) ||
                    x.Equals("HEAD", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
        }

        foreach (var parameter in action.MethodInfo.GetParameters())
        {
            if (parameter.ParameterType == typeof(CancellationToken))
                continue;

            if (parameter.HasDefaultValue)
                continue;

            if (!parameter.ParameterType.IsValueType)
                continue;

            if (Nullable.GetUnderlyingType(parameter.ParameterType) != null)
                continue;

            return false;
        }

        return true;
    }

    private static bool IsUsefulNavigationActionName(string actionName)
    {
        if (string.IsNullOrWhiteSpace(actionName) ||
            actionName.StartsWith("_", StringComparison.Ordinal))
        {
            return false;
        }

        var blockedPrefixes = new[]
        {
            "Detalle",
            "Editar",
            "Eliminar",
            "Borrar",
            "Crear",
            "Nuevo",
            "Confirmar",
            "Guardar",
            "Actualizar",
            "Procesar",
            "Validar",
            "Liberar",
            "Rechazar",
            "Aprobar",
            "Cancelar",
            "Descargar",
            "Exportar",
            "Importar",
            "Imprimir",
            "Obtener",
            "Get",
            "Buscar",
            "Cargar",
            "Contar",
            "Json",
            "Api",
            "Ajax",
            "Partial",
            "Modal",
            "Upload",
            "Registrar",
            "Capturar",
            "Asignar",
            "Cambiar",
            "Password",
            "Acceso",
            "Error"
        };

        return !blockedPrefixes.Any(prefix =>
            actionName.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase));
    }

    private static int NavigationActionOrder(string actionName)
    {
        if (actionName.Equals("Index", StringComparison.OrdinalIgnoreCase))
            return 0;

        if (actionName.Equals("Dashboard", StringComparison.OrdinalIgnoreCase) ||
            actionName.Equals("Panel", StringComparison.OrdinalIgnoreCase))
            return 10;

        if (actionName.StartsWith("Pend", StringComparison.OrdinalIgnoreCase) ||
            actionName.StartsWith("EnProceso", StringComparison.OrdinalIgnoreCase))
            return 20;

        if (actionName.StartsWith("Histor", StringComparison.OrdinalIgnoreCase))
            return 80;

        if (actionName.StartsWith("Config", StringComparison.OrdinalIgnoreCase))
            return 90;

        return 50;
    }

    private static string FriendlyActionName(string actionName)
    {
        var known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Index"] = "Inicio",
            ["Historico"] = "Histórico",
            ["Historial"] = "Historial",
            ["NivelesStock"] = "Niveles de stock",
            ["CalendarioMaquinas"] = "Calendario de máquinas",
            ["ProgramacionPersonal"] = "Programación de personal",
            ["EnProceso"] = "En proceso",
            ["ListaCarga"] = "Lista de carga",
            ["Materiales"] = "Materiales",
            ["Almacenes"] = "Almacenes",
            ["Ubicaciones"] = "Ubicaciones",
            ["Embalajes"] = "Embalajes",
            ["Choferes"] = "Choferes",
            ["Viajes"] = "Viajes",
            ["Cajas"] = "Cajas",
            ["GP12"] = "GP12",
            ["Scrap"] = "Scrap"
        };

        if (known.TryGetValue(actionName, out var mapped))
            return mapped;

        var chars = new List<char>(actionName.Length + 8);

        for (var i = 0; i < actionName.Length; i++)
        {
            var current = actionName[i];

            if (i > 0 &&
                char.IsUpper(current) &&
                !char.IsUpper(actionName[i - 1]))
            {
                chars.Add(' ');
            }

            chars.Add(current);
        }

        var result = new string(chars.ToArray()).Trim();

        if (result.Length == 0)
            return actionName;

        return char.ToUpperInvariant(result[0]) + result.Substring(1);
    }

    private static string? NormalizeLocalUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var value = raw.Trim();

        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("//", StringComparison.Ordinal))
            return null;

        if (!value.StartsWith("/", StringComparison.Ordinal))
            value = "/" + value.TrimStart('/');

        return value;
    }

    private static string ControllerFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;
        var path = url.Split('?', '#')[0].Trim('/');
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        return path.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
    }

    private static string PathOnly(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;
        var value = url.Split('?', '#')[0];
        if (value.Length > 1) value = value.TrimEnd('/');
        return value;
    }

    // NSQ_GLOBAL_NAV_V4_3_HOMEURL
    // NSQ_NAV_EXACT_OWNER_V5
    private static void ResolveNavigationState(GlobalDepartmentNavigationVm vm)
    {
        var currentPath = PathOnly(vm.CurrentPath);
        var currentController = vm.CurrentController.Trim();

        var esCalendarioProduccion = currentPath.Equals(
            "/Produccion/Calendario",
            StringComparison.OrdinalIgnoreCase);

        if (esCalendarioProduccion)
            currentController = "Produccion";

        var esRutaHcc =
            currentPath.Equals("/Calidad/HojasControl", StringComparison.OrdinalIgnoreCase) ||
            currentPath.Equals("/Calidad/HCCPlantillas", StringComparison.OrdinalIgnoreCase) ||
            currentPath.StartsWith("/Calidad/HojaControl", StringComparison.OrdinalIgnoreCase) ||
            currentPath.StartsWith("/Calidad/CapturarHCC", StringComparison.OrdinalIgnoreCase) ||
            currentPath.StartsWith("/Calidad/RegistroHCC", StringComparison.OrdinalIgnoreCase) ||
            // NSQ_HCC_CREAR_HISTORIAL_V1_2
            currentPath.StartsWith("/Calidad/CrearControlCalidad", StringComparison.OrdinalIgnoreCase) ||
            currentPath.StartsWith("/Calidad/HistorialControlCalidad", StringComparison.OrdinalIgnoreCase);

        int? currentGroupId = null;
        const string groupPrefix = "/Menu/Grupo/";
        if (currentPath.StartsWith(groupPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var rawGroup = currentPath.Substring(groupPrefix.Length).Trim('/');
            if (int.TryParse(rawGroup, out var parsedGroupId))
                currentGroupId = parsedGroupId;
        }

        foreach (var group in vm.Groups)
        {
            foreach (var menu in group.Menus)
            {
                menu.SubMenus = menu.SubMenus
                    .OrderBy(x => x.SubMenuID)
                    .ToList();

                foreach (var sub in menu.SubMenus)
                {
                    var targetPath = PathOnly(sub.Url);
                    sub.IsActive = !string.IsNullOrWhiteSpace(targetPath) &&
                                   string.Equals(targetPath, currentPath, StringComparison.OrdinalIgnoreCase);
                }
            }

            var exactMenus = group.Menus
                .Where(m => m.SubMenus.Any(s => s.IsActive))
                .ToList();

            var groupToken = NormalizeNavigationToken(group.Nombre);

            foreach (var menu in group.Menus)
            {
                var menuToken = NormalizeNavigationToken(menu.Nombre);

                if (esRutaHcc && groupToken.Equals("CALIDAD", StringComparison.OrdinalIgnoreCase))
                {
                                        // NSQ_CONTROL_CALIDAD_BREADCRUMB_HCC_V3
                    // El nombre visible del menu ahora es "Control de Calidad".
                    // Conservamos tambien el token historico para instalaciones
                    // que aun no hayan ejecutado el cambio de nombre en BD.
                    menu.IsActive =
                        menuToken.Equals(
                            "CONTROLDECALIDAD",
                            StringComparison.OrdinalIgnoreCase)
                        ||
                        menuToken.Equals(
                            "HOJASDECONTROL",
                            StringComparison.OrdinalIgnoreCase);
                }
                else if (esCalendarioProduccion && groupToken.Equals("PRODUCCION", StringComparison.OrdinalIgnoreCase))
                {
                    menu.IsActive = menu.SubMenus.Any(s =>
                        PathOnly(s.Url).Equals("/Produccion/Calendario", StringComparison.OrdinalIgnoreCase));
                }
                else if (exactMenus.Count > 0)
                {
                    menu.IsActive = exactMenus.Contains(menu);
                }
                else
                {
                    menu.IsActive = menu.SubMenus.Any(x =>
                        string.Equals(
                            ControllerFromUrl(x.Url),
                            currentController,
                            StringComparison.OrdinalIgnoreCase));
                }

                menu.HomeUrl = ResolveLogicalMenuHomeUrl(
                    menu,
                    group,
                    currentController,
                    menu.IsActive);
            }

            group.Menus = group.Menus
                .OrderBy(x => x.Orden)
                .ThenBy(x => x.Nombre)
                .ToList();

            group.IsActive =
                group.Menus.Any(x => x.IsActive) ||
                currentGroupId == group.MenuGrupoID;
        }
    }
    private static string ResolveLogicalMenuHomeUrl(
        GlobalDepartmentNavigationMenuVm menu,
        GlobalDepartmentNavigationGroupVm group,
        string currentController,
        bool menuIsActive)
    {
        var fallback =
            menu.SubMenus.FirstOrDefault()?.Url
            ?? $"/Menu/Grupo/{group.MenuGrupoID}";

        var indexCandidates = menu.SubMenus
            .Where(x => IsIndexNavigationUrl(x.Url))
            .ToList();

        if (indexCandidates.Count == 0)
            return fallback;

        // Si estamos dentro de este menú, la raíz del mismo controlador
        // tiene prioridad absoluta. Evita, por ejemplo, que Producción
        // regrese a ProduccionOperador/Historial.
        if (menuIsActive &&
            !string.IsNullOrWhiteSpace(currentController))
        {
            var currentControllerIndex =
                indexCandidates.FirstOrDefault(x =>
                    string.Equals(
                        ControllerFromUrl(x.Url),
                        currentController,
                        StringComparison.OrdinalIgnoreCase));

            if (currentControllerIndex != null)
                return currentControllerIndex.Url;
        }

        var menuToken = NormalizeNavigationToken(menu.Nombre);
        var groupToken = NormalizeNavigationToken(group.Nombre);

        var scored = indexCandidates
            .Select(x =>
            {
                var controller =
                    ControllerFromUrl(x.Url);

                var controllerToken =
                    NormalizeNavigationToken(controller);

                var score = 10;

                if (!string.IsNullOrWhiteSpace(menuToken))
                {
                    if (controllerToken.Equals(
                        menuToken,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        score += 100;
                    }
                    else if (controllerToken.EndsWith(
                        menuToken,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        score += 90;
                    }
                    else if (controllerToken.Contains(
                        menuToken,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        score += 70;
                    }
                }

                if (!string.IsNullOrWhiteSpace(groupToken) &&
                    controllerToken.StartsWith(
                        groupToken,
                        StringComparison.OrdinalIgnoreCase))
                {
                    score += 20;
                }

                // Las pantallas de operador son portales especializados,
                // no la raíz del menú general de Producción.
                if (controller.Contains(
                    "Operador",
                    StringComparison.OrdinalIgnoreCase))
                {
                    score -= 35;
                }

                return new
                {
                    SubMenu = x,
                    Score = score
                };
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.SubMenu.SubMenuID)
            .FirstOrDefault();

        return scored?.SubMenu.Url ?? fallback;
    }

    private static bool IsIndexNavigationUrl(string? url)
    {
        var path = PathOnly(url);

        if (string.IsNullOrWhiteSpace(path))
            return false;

        var parts = path
            .Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries);

        return parts.Length == 2 &&
               parts[1].Equals(
                   "Index",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeNavigationToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value
            .Normalize(
                System.Text.NormalizationForm.FormD);

        var chars = normalized
            .Where(x =>
                System.Globalization.CharUnicodeInfo
                    .GetUnicodeCategory(x)
                !=
                System.Globalization.UnicodeCategory
                    .NonSpacingMark)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray();

        return new string(chars);
    }

    private static void EnsureCompatibilityLinks(GlobalDepartmentNavigationVm vm)
    {
        var almacen = vm.Groups.FirstOrDefault(x => x.MenuGrupoID == 1);
        if (almacen != null)
        {
            var tieneMp = almacen.Menus.Any(x =>
                x.SubMenus.Any(s => s.Url.StartsWith("/AlmacenMP", StringComparison.OrdinalIgnoreCase)));

            if (tieneMp)
            {
                EnsureSyntheticMenu(
                    almacen,
                    "Embalajes",
                    "fa-solid fa-box-open",
                    "/AlmacenEmbalajes/Index",
                    "Gestion de materiales de empaque.");

                EnsureSyntheticMenu(
                    almacen,
                    "OF",
                    "fa-solid fa-clipboard-list",
                    "/AlmacenOF/Index",
                    "Entrega controlada de materiales por orden de fabricacion.");
            }
        }

        // NSQ_PRODUCCION_CALENDARIO_MENU_GLOBAL_V1_START
        var produccion = vm.Groups.FirstOrDefault(x =>
            NormalizeNavigationToken(x.Nombre)
                .Equals("PRODUCCION", StringComparison.OrdinalIgnoreCase));

        if (produccion != null)
        {
            EnsureSyntheticMenu(
                produccion,
                "Calendario",
                "fa-solid fa-calendar-days",
                "/Produccion/Calendario",
                "Consulta de solo lectura del calendario para Produccion.",
                "Calendario");
        }
        // NSQ_PRODUCCION_CALENDARIO_MENU_GLOBAL_V1_END
        var planeacion = vm.Groups.FirstOrDefault(x => x.MenuGrupoID == 3);
        if (planeacion != null)
        {
            EnsureSyntheticMenu(
                planeacion,
                "Calendario Maquinas",
                "fa-solid fa-calendar-days",
                "/PlaneacionCalendarioMaquinas/Index",
                "Consulta semanal de maquinas y OF.");
        }
    }

    private static void EnsureSyntheticMenu(
        GlobalDepartmentNavigationGroupVm group,
        string name,
        string icon,
        string url,
        string description,
        string subMenuName = "Abrir")
    {
        if (group.Menus.Any(x =>
            x.SubMenus.Any(s => s.Url.StartsWith(url.Split('?')[0], StringComparison.OrdinalIgnoreCase))))
            return;

        var nextMenuId = group.Menus.Count == 0 ? -1 : group.Menus.Min(x => x.MenuID) - 1;
        var menu = new GlobalDepartmentNavigationMenuVm
        {
            MenuID = nextMenuId,
            MenuGrupoID = group.MenuGrupoID,
            Nombre = name,
            Icono = icon,
            Descripcion = description,
            Orden = group.Menus.Count == 0 ? 1 : group.Menus.Max(x => x.Orden) + 1,
            HomeUrl = url
        };

        menu.SubMenus.Add(new GlobalDepartmentNavigationSubMenuVm
        {
            SubMenuID = -Math.Abs(nextMenuId),
            Nombre = subMenuName,
            Url = url
        });

        group.Menus.Add(menu);
    }
}

public sealed class GlobalDepartmentNavigationVm
{
    public string CurrentPath { get; set; } = string.Empty;
    public string CurrentController { get; set; } = string.Empty;
    public bool LoadFailed { get; set; }
    public List<GlobalDepartmentNavigationGroupVm> Groups { get; set; } = new();
}

public sealed class GlobalDepartmentNavigationGroupVm
{
    public int MenuGrupoID { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public string Icono { get; set; } = "fa-solid fa-layer-group";
    public int Orden { get; set; }
    public bool IsActive { get; set; }
    public List<GlobalDepartmentNavigationMenuVm> Menus { get; set; } = new();
}

public sealed class GlobalDepartmentNavigationMenuVm
{
    public int MenuID { get; set; }
    public int MenuGrupoID { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public string Icono { get; set; } = "fa-solid fa-folder";
    public int Orden { get; set; }
    public string HomeUrl { get; set; } = "#";
    public bool IsActive { get; set; }
    public List<GlobalDepartmentNavigationSubMenuVm> SubMenus { get; set; } = new();
}

public sealed class GlobalDepartmentNavigationSubMenuVm
{
    public int SubMenuID { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Url { get; set; } = "#";
    public bool IsActive { get; set; }
}