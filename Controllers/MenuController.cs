using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using ERP.NSQuell.Models;
using ERP.NSQuell.Servicios;

namespace ERP.NSQuell.Controllers
{
    public class MenuController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly IServicioAcceso _acceso;

        public MenuController(IConfiguration configuration, IServicioAcceso acceso)
        {
            _configuration = configuration;
            _acceso = acceso;
        }

        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Login", "Login");
        }

        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> Index()
        {
            ViewBag.MostrarBienvenida = (TempData["MostrarBienvenida"] as string) == "true";

            int? usuarioID = HttpContext.Session.GetInt32("UsuarioID");
            if (usuarioID == null)
                return RedirectToAction("Login", "Login");

            var grupos = new List<MenuGrupoModel>();

            string cnn = _configuration.GetConnectionString("DefaultConnection");
            await using var conn = new SqlConnection(cnn);
            await conn.OpenAsync();

            const string sql = @"
WITH Perms AS (
    SELECT SubMenuID
    FROM dbo.fn_PermisosEfectivosUsuario(@UsuarioID)
    WHERE TienePermiso = 1
)
SELECT DISTINCT
    g.MenuGrupoID,
    g.Nombre,
    g.Descripcion,
    g.IconoCss,
    g.Orden
FROM SubMenus sm
INNER JOIN Perms p 
    ON p.SubMenuID = sm.SubMenuID
INNER JOIN Menus m 
    ON m.MenuID = sm.MenuID
INNER JOIN MenuGrupo g 
    ON g.MenuGrupoID = m.MenuGrupoID
WHERE sm.Activo = 1
  AND g.Activo = 1
ORDER BY g.Orden, g.Nombre;";

            await using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@UsuarioID", usuarioID.Value);

                await using var rd = await cmd.ExecuteReaderAsync();

                while (await rd.ReadAsync())
                {
                    grupos.Add(new MenuGrupoModel
                    {
                        MenuGrupoID = rd.GetInt32(rd.GetOrdinal("MenuGrupoID")),
                        Nombre = rd.GetString(rd.GetOrdinal("Nombre")),
                        Descripcion = rd.IsDBNull(rd.GetOrdinal("Descripcion"))
                            ? null
                            : rd.GetString(rd.GetOrdinal("Descripcion")),
                        IconoCss = rd.IsDBNull(rd.GetOrdinal("IconoCss"))
                            ? null
                            : rd.GetString(rd.GetOrdinal("IconoCss")),
                        Orden = rd.GetInt32(rd.GetOrdinal("Orden"))
                    });
                }
            }

            return View("Grupos", grupos);
        }

        [HttpGet]
public async Task<IActionResult> Grupo(int id)
{
    int? usuarioID = HttpContext.Session.GetInt32("UsuarioID");
    if (usuarioID == null)
        return RedirectToAction("Login", "Login");

    var menus = new List<MenuModel>();

    string cnn = _configuration.GetConnectionString("DefaultConnection");
    await using var conn = new SqlConnection(cnn);
    await conn.OpenAsync();

    const string sql = @"
WITH Perms AS (
    SELECT SubMenuID
    FROM dbo.fn_PermisosEfectivosUsuario(@UsuarioID)
    WHERE TienePermiso = 1
),
MenusConPermiso AS (
    SELECT DISTINCT
        m.MenuID,
        m.Nombre,
        ISNULL(m.IconoCss, 'fa-solid fa-folder') AS IconoCss,
        ISNULL(m.Descripcion, '') AS Descripcion,
        ISNULL(m.Orden, 0) AS Orden
    FROM dbo.Menus m
    INNER JOIN dbo.SubMenus sm
        ON sm.MenuID = m.MenuID
    INNER JOIN Perms p
        ON p.SubMenuID = sm.SubMenuID
    WHERE m.MenuGrupoID = @MenuGrupoID
      AND ISNULL(m.Activo, 1) = 1
      AND ISNULL(sm.Activo, 1) = 1
)
SELECT
    m.MenuID,
    m.Nombre AS NombreMenu,
    m.IconoCss,
    m.Descripcion,
    m.Orden,
    ca.HomeUrl
FROM MenusConPermiso m
CROSS APPLY (
    SELECT TOP 1
        sm.UrlEnlace AS HomeUrl
    FROM dbo.SubMenus sm
    INNER JOIN Perms p
        ON p.SubMenuID = sm.SubMenuID
    WHERE sm.MenuID = m.MenuID
      AND ISNULL(sm.Activo, 1) = 1
      AND NULLIF(LTRIM(RTRIM(sm.UrlEnlace)), '') IS NOT NULL
    ORDER BY
        CASE WHEN sm.Nombre LIKE N'Ver %' THEN 1 ELSE 2 END,
        CASE
            WHEN sm.UrlEnlace LIKE '%/Index'
              OR sm.UrlEnlace LIKE '%/Entrada'
            THEN 1
            ELSE 2
        END,
        sm.SubMenuID
) ca
ORDER BY m.Orden, m.Nombre;";

    await using (var cmd = new SqlCommand(sql, conn))
    {
        cmd.Parameters.AddWithValue("@UsuarioID", usuarioID.Value);
        cmd.Parameters.AddWithValue("@MenuGrupoID", id);

        await using var rd = await cmd.ExecuteReaderAsync();

        while (await rd.ReadAsync())
        {
            menus.Add(new MenuModel
            {
                MenuID = Convert.ToInt32(rd["MenuID"]),
                Nombre = rd["NombreMenu"]?.ToString() ?? "",
                Url = rd["HomeUrl"]?.ToString() ?? "#",
                Icono = rd["IconoCss"]?.ToString() ?? "fa-solid fa-folder",
                Descripcion = rd["Descripcion"]?.ToString() ?? "",
                Orden = Convert.ToInt32(rd["Orden"])
            });
        }
    }

    /*
        /Menu/Grupo/1 es el menú real de Almacén.
        Mientras los permisos nuevos terminan de sincronizarse, Embalajes
        hereda la visibilidad de MP. No se duplica si ya viene de la base.
    */
    if (id == 1)
    {
        var tieneMp = menus.Any(m =>
            string.Equals(m.Nombre?.Trim(), "MP", StringComparison.OrdinalIgnoreCase)
            || (m.Url?.StartsWith("/AlmacenMP", StringComparison.OrdinalIgnoreCase) ?? false));

        var tieneEmbalajes = menus.Any(m =>
            (m.Nombre?.Contains("EMBALAJE", StringComparison.OrdinalIgnoreCase) ?? false)
            || (m.Url?.StartsWith("/AlmacenEmbalajes", StringComparison.OrdinalIgnoreCase) ?? false));

        if (tieneMp && !tieneEmbalajes)
        {
            menus.Add(new MenuModel
            {
                MenuID = menus.Count == 0 ? 3 : menus.Max(m => m.MenuID) + 1,
                Nombre = "EMBALAJES",
                Url = "/AlmacenEmbalajes/Index",
                Icono = "fa-solid fa-box-open",
                Descripcion = "Gestión de materiales de empaque.",
                Orden = menus.Count == 0 ? 3 : menus.Max(m => m.Orden) + 1
            });
        }
        var tieneOrdenesFabricacion = menus.Any(m =>
            (m.Nombre?.Contains("ORDENES DE FABRICACION", StringComparison.OrdinalIgnoreCase) ?? false)
            || (m.Nombre?.Contains("ÓRDENES DE FABRICACIÓN", StringComparison.OrdinalIgnoreCase) ?? false)
            || (m.Url?.StartsWith("/AlmacenOF", StringComparison.OrdinalIgnoreCase) ?? false));

        if (tieneMp && !tieneOrdenesFabricacion)
        {
            menus.Add(new MenuModel
            {
                MenuID = menus.Count == 0 ? 4 : menus.Max(m => m.MenuID) + 1,
                Nombre = "OF",
                Url = "/AlmacenOF/Index",
                Icono = "fa-solid fa-clipboard-list",
                Descripcion = "Órdenes de fabricación: consulta rápida de MP, embalajes y PT entregado al almacén.",
                Orden = menus.Count == 0 ? 4 : menus.Max(m => m.Orden) + 1
            });
        }

        menus = menus
            .OrderBy(m => m.Orden)
            .ThenBy(m => m.Nombre)
            .ToList();
    }

    ViewBag.MenuGrupoID = id;
    ViewBag.NombreGrupo = await ObtenerNombreGrupo(conn, id);

    return View("MenusPorGrupo", menus);
}

        private static async Task<string> ObtenerNombreGrupo(SqlConnection conn, int menuGrupoId)
        {
            const string sql = @"
SELECT Nombre 
FROM MenuGrupo 
WHERE MenuGrupoID = @Id;";

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", menuGrupoId);

            var r = await cmd.ExecuteScalarAsync();

            return r?.ToString() ?? "Grupo";
        }

        
    }
}

