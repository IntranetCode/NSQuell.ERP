using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers
{
    // NSQ_ADMIN_MENUS_CRUD_V1
    public sealed class AdministracionMenusController : Controller
    {
        private const int RolAdministradorErp = 1;
        private readonly string _cnn;

        public AdministracionMenusController(IConfiguration configuration)
        {
            _cnn = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException(
                    "No existe la cadena de conexión DefaultConnection.");
        }

        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> Index()
        {
            var usuarioId = HttpContext.Session.GetInt32("UsuarioID");

            if (!usuarioId.HasValue || usuarioId.Value <= 0)
                return RedirectToAction("Login", "Login");

            if (!await EsAdministradorErpAsync(usuarioId.Value))
                return Forbid();

            var vm = new AdministracionMenusVm();

            await using var cn = new SqlConnection(_cnn);
            await cn.OpenAsync();

            const string sql = @"
SELECT
    g.MenuGrupoID,
    g.Nombre,
    g.Descripcion,
    g.IconoCss,
    g.Orden,
    g.Activo,
    (
        SELECT COUNT(*)
        FROM dbo.Menus m
        WHERE m.MenuGrupoID=g.MenuGrupoID
    ) AS TotalMenus
FROM dbo.MenuGrupo g
ORDER BY g.Orden, g.Nombre, g.MenuGrupoID;

SELECT
    m.MenuID,
    m.MenuGrupoID,
    g.Nombre AS GrupoNombre,
    m.Nombre,
    m.Descripcion,
    m.IconoCss,
    m.Orden,
    m.Activo,
    (
        SELECT COUNT(*)
        FROM dbo.SubMenus sm
        WHERE sm.MenuID=m.MenuID
    ) AS TotalSubMenus
FROM dbo.Menus m
LEFT JOIN dbo.MenuGrupo g
    ON g.MenuGrupoID=m.MenuGrupoID
ORDER BY
    ISNULL(g.Orden,2147483647),
    ISNULL(m.Orden,0),
    m.Nombre,
    m.MenuID;

SELECT
    sm.SubMenuID,
    sm.MenuID,
    m.Nombre AS MenuNombre,
    g.Nombre AS GrupoNombre,
    sm.Nombre,
    sm.UrlEnlace,
    sm.Descripcion,
    sm.IconoCSS,
    sm.Activo,
    sm.FechaCreacion,
    sm.FechaModificacion,
    (
        SELECT COUNT(*)
        FROM dbo.SubMenuAcciones sma
        WHERE sma.SubMenuID=sm.SubMenuID
          AND sma.Activo=1
    ) AS AccionesActivas
FROM dbo.SubMenus sm
LEFT JOIN dbo.Menus m
    ON m.MenuID=sm.MenuID
LEFT JOIN dbo.MenuGrupo g
    ON g.MenuGrupoID=m.MenuGrupoID
ORDER BY
    ISNULL(g.Orden,2147483647),
    ISNULL(m.Orden,2147483647),
    sm.Nombre,
    sm.SubMenuID;";

            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                vm.Grupos.Add(new AdministracionMenuGrupoVm
                {
                    MenuGrupoID = rd.GetInt32(rd.GetOrdinal("MenuGrupoID")),
                    Nombre = rd["Nombre"]?.ToString() ?? string.Empty,
                    Descripcion = rd["Descripcion"] == DBNull.Value
                        ? string.Empty
                        : rd["Descripcion"]?.ToString() ?? string.Empty,
                    IconoCss = rd["IconoCss"] == DBNull.Value
                        ? string.Empty
                        : rd["IconoCss"]?.ToString() ?? string.Empty,
                    Orden = Convert.ToInt32(rd["Orden"]),
                    Activo = Convert.ToBoolean(rd["Activo"]),
                    TotalMenus = Convert.ToInt32(rd["TotalMenus"])
                });
            }

            await rd.NextResultAsync();

            while (await rd.ReadAsync())
            {
                vm.Menus.Add(new AdministracionMenuVm
                {
                    MenuID = rd.GetInt32(rd.GetOrdinal("MenuID")),
                    MenuGrupoID = rd["MenuGrupoID"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(rd["MenuGrupoID"]),
                    GrupoNombre = rd["GrupoNombre"] == DBNull.Value
                        ? "(Sin grupo)"
                        : rd["GrupoNombre"]?.ToString() ?? "(Sin grupo)",
                    Nombre = rd["Nombre"]?.ToString() ?? string.Empty,
                    Descripcion = rd["Descripcion"] == DBNull.Value
                        ? string.Empty
                        : rd["Descripcion"]?.ToString() ?? string.Empty,
                    IconoCss = rd["IconoCss"] == DBNull.Value
                        ? string.Empty
                        : rd["IconoCss"]?.ToString() ?? string.Empty,
                    Orden = Convert.ToInt32(rd["Orden"]),
                    Activo = Convert.ToBoolean(rd["Activo"]),
                    TotalSubMenus = Convert.ToInt32(rd["TotalSubMenus"])
                });
            }

            await rd.NextResultAsync();

            while (await rd.ReadAsync())
            {
                vm.SubMenus.Add(new AdministracionSubMenuVm
                {
                    SubMenuID = rd.GetInt32(rd.GetOrdinal("SubMenuID")),
                    MenuID = rd["MenuID"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(rd["MenuID"]),
                    MenuNombre = rd["MenuNombre"] == DBNull.Value
                        ? "(Sin menú)"
                        : rd["MenuNombre"]?.ToString() ?? "(Sin menú)",
                    GrupoNombre = rd["GrupoNombre"] == DBNull.Value
                        ? "(Sin grupo)"
                        : rd["GrupoNombre"]?.ToString() ?? "(Sin grupo)",
                    Nombre = rd["Nombre"]?.ToString() ?? string.Empty,
                    UrlEnlace = rd["UrlEnlace"] == DBNull.Value
                        ? string.Empty
                        : rd["UrlEnlace"]?.ToString() ?? string.Empty,
                    Descripcion = rd["Descripcion"] == DBNull.Value
                        ? string.Empty
                        : rd["Descripcion"]?.ToString() ?? string.Empty,
                    IconoCss = rd["IconoCSS"] == DBNull.Value
                        ? string.Empty
                        : rd["IconoCSS"]?.ToString() ?? string.Empty,
                    Activo = Convert.ToBoolean(rd["Activo"]),
                    FechaCreacion = rd["FechaCreacion"] == DBNull.Value
                        ? null
                        : Convert.ToDateTime(rd["FechaCreacion"]),
                    FechaModificacion = rd["FechaModificacion"] == DBNull.Value
                        ? null
                        : Convert.ToDateTime(rd["FechaModificacion"]),
                    AccionesActivas = Convert.ToInt32(rd["AccionesActivas"])
                });
            }

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CrearGrupo(
            string nombre,
            string? descripcion,
            string? iconoCss,
            int orden = 0)
        {
            var auth = await ValidarAdministradorAsync();
            if (auth != null)
                return auth;

            nombre = Limpiar(nombre, 100);
            descripcion = LimpiarNullable(descripcion, 250);
            iconoCss = LimpiarNullable(iconoCss, 100);

            if (string.IsNullOrWhiteSpace(nombre))
                return Error("El nombre del grupo es obligatorio.");

            await using var cn = new SqlConnection(_cnn);
            await cn.OpenAsync();

            const string sql = @"
IF EXISTS
(
    SELECT 1
    FROM dbo.MenuGrupo
    WHERE UPPER(LTRIM(RTRIM(Nombre))) =
          UPPER(LTRIM(RTRIM(@Nombre)))
)
    THROW 51001,'Ya existe un grupo con ese nombre.',1;

IF @Orden<=0
    SELECT @Orden=ISNULL(MAX(Orden),0)+1
    FROM dbo.MenuGrupo;

INSERT INTO dbo.MenuGrupo
(
    Nombre,
    Descripcion,
    IconoCss,
    Orden,
    Activo
)
VALUES
(
    @Nombre,
    @Descripcion,
    @IconoCss,
    @Orden,
    1
);

SELECT CAST(SCOPE_IDENTITY() AS int);";

            await using var cmd = new SqlCommand(sql, cn);

            var pOrden = cmd.Parameters.Add("@Orden", SqlDbType.Int);
            pOrden.Direction = ParameterDirection.InputOutput;
            pOrden.Value = orden;

            cmd.Parameters.Add("@Nombre", SqlDbType.VarChar, 100).Value = nombre;
            cmd.Parameters.Add("@Descripcion", SqlDbType.VarChar, 250).Value =
                DbValor(descripcion);
            cmd.Parameters.Add("@IconoCss", SqlDbType.NVarChar, 100).Value =
                DbValor(iconoCss);

            try
            {
                var id = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                TempData["Success"] =
                    $"Grupo creado correctamente (ID {id}).";
            }
            catch (SqlException ex)
            {
                return Error(ex.Message);
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditarGrupo(
            int menuGrupoID,
            string nombre,
            string? descripcion,
            string? iconoCss,
            int orden)
        {
            var auth = await ValidarAdministradorAsync();
            if (auth != null)
                return auth;

            nombre = Limpiar(nombre, 100);
            descripcion = LimpiarNullable(descripcion, 250);
            iconoCss = LimpiarNullable(iconoCss, 100);

            if (menuGrupoID <= 0 || string.IsNullOrWhiteSpace(nombre))
                return Error("Datos de grupo inválidos.");

            if (orden <= 0)
                orden = 1;

            await using var cn = new SqlConnection(_cnn);
            await cn.OpenAsync();

            const string sql = @"
IF NOT EXISTS
(
    SELECT 1
    FROM dbo.MenuGrupo
    WHERE MenuGrupoID=@MenuGrupoID
)
    THROW 51002,'El grupo ya no existe.',1;

IF EXISTS
(
    SELECT 1
    FROM dbo.MenuGrupo
    WHERE MenuGrupoID<>@MenuGrupoID
      AND UPPER(LTRIM(RTRIM(Nombre))) =
          UPPER(LTRIM(RTRIM(@Nombre)))
)
    THROW 51003,'Ya existe otro grupo con ese nombre.',1;

UPDATE dbo.MenuGrupo
SET
    Nombre=@Nombre,
    Descripcion=@Descripcion,
    IconoCss=@IconoCss,
    Orden=@Orden
WHERE MenuGrupoID=@MenuGrupoID;";

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@MenuGrupoID", SqlDbType.Int).Value = menuGrupoID;
            cmd.Parameters.Add("@Nombre", SqlDbType.VarChar, 100).Value = nombre;
            cmd.Parameters.Add("@Descripcion", SqlDbType.VarChar, 250).Value =
                DbValor(descripcion);
            cmd.Parameters.Add("@IconoCss", SqlDbType.NVarChar, 100).Value =
                DbValor(iconoCss);
            cmd.Parameters.Add("@Orden", SqlDbType.Int).Value = orden;

            try
            {
                await cmd.ExecuteNonQueryAsync();
                TempData["Success"] = "Grupo actualizado.";
            }
            catch (SqlException ex)
            {
                return Error(ex.Message);
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AlternarGrupo(int menuGrupoID)
        {
            var auth = await ValidarAdministradorAsync();
            if (auth != null)
                return auth;

            await using var cn = new SqlConnection(_cnn);
            await cn.OpenAsync();

            const string sql = @"
UPDATE dbo.MenuGrupo
SET Activo=CASE WHEN Activo=1 THEN 0 ELSE 1 END
WHERE MenuGrupoID=@MenuGrupoID;

IF @@ROWCOUNT=0
    THROW 51004,'El grupo ya no existe.',1;";

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@MenuGrupoID", SqlDbType.Int).Value = menuGrupoID;

            try
            {
                await cmd.ExecuteNonQueryAsync();
                TempData["Success"] = "Estado del grupo actualizado.";
            }
            catch (SqlException ex)
            {
                return Error(ex.Message);
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CrearMenu(
            int menuGrupoID,
            string nombre,
            string? descripcion,
            string? iconoCss,
            int orden = 0)
        {
            var auth = await ValidarAdministradorAsync();
            if (auth != null)
                return auth;

            nombre = Limpiar(nombre, 100);
            descripcion = LimpiarNullable(descripcion, 300);
            iconoCss = LimpiarNullable(iconoCss, 100);

            if (menuGrupoID <= 0 || string.IsNullOrWhiteSpace(nombre))
                return Error("Selecciona un grupo e indica el nombre del menú.");

            await using var cn = new SqlConnection(_cnn);
            await cn.OpenAsync();

            const string sql = @"
IF NOT EXISTS
(
    SELECT 1
    FROM dbo.MenuGrupo
    WHERE MenuGrupoID=@MenuGrupoID
)
    THROW 51010,'El grupo seleccionado no existe.',1;

IF EXISTS
(
    SELECT 1
    FROM dbo.Menus
    WHERE MenuGrupoID=@MenuGrupoID
      AND UPPER(LTRIM(RTRIM(Nombre))) =
          UPPER(LTRIM(RTRIM(@Nombre)))
)
    THROW 51011,'Ya existe un menú con ese nombre dentro del grupo.',1;

IF @Orden<=0
    SELECT @Orden=ISNULL(MAX(Orden),0)+1
    FROM dbo.Menus
    WHERE MenuGrupoID=@MenuGrupoID;

INSERT INTO dbo.Menus
(
    Nombre,
    MenuGrupoID,
    Activo,
    IconoCss,
    Orden,
    Descripcion
)
VALUES
(
    @Nombre,
    @MenuGrupoID,
    1,
    @IconoCss,
    @Orden,
    @Descripcion
);

SELECT CAST(SCOPE_IDENTITY() AS int);";

            await using var cmd = new SqlCommand(sql, cn);

            var pOrden = cmd.Parameters.Add("@Orden", SqlDbType.Int);
            pOrden.Direction = ParameterDirection.InputOutput;
            pOrden.Value = orden;

            cmd.Parameters.Add("@MenuGrupoID", SqlDbType.Int).Value = menuGrupoID;
            cmd.Parameters.Add("@Nombre", SqlDbType.NVarChar, 100).Value = nombre;
            cmd.Parameters.Add("@Descripcion", SqlDbType.NVarChar, 300).Value =
                DbValor(descripcion);
            cmd.Parameters.Add("@IconoCss", SqlDbType.NVarChar, 100).Value =
                DbValor(iconoCss);

            try
            {
                var id = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                TempData["Success"] =
                    $"Menú creado correctamente (ID {id}).";
            }
            catch (SqlException ex)
            {
                return Error(ex.Message);
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditarMenu(
            int menuID,
            int menuGrupoID,
            string nombre,
            string? descripcion,
            string? iconoCss,
            int orden)
        {
            var auth = await ValidarAdministradorAsync();
            if (auth != null)
                return auth;

            nombre = Limpiar(nombre, 100);
            descripcion = LimpiarNullable(descripcion, 300);
            iconoCss = LimpiarNullable(iconoCss, 100);

            if (menuID <= 0 ||
                menuGrupoID <= 0 ||
                string.IsNullOrWhiteSpace(nombre))
            {
                return Error("Datos de menú inválidos.");
            }

            if (orden <= 0)
                orden = 1;

            await using var cn = new SqlConnection(_cnn);
            await cn.OpenAsync();

            const string sql = @"
IF NOT EXISTS
(
    SELECT 1
    FROM dbo.MenuGrupo
    WHERE MenuGrupoID=@MenuGrupoID
)
    THROW 51012,'El grupo seleccionado no existe.',1;

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.Menus
    WHERE MenuID=@MenuID
)
    THROW 51013,'El menú ya no existe.',1;

IF EXISTS
(
    SELECT 1
    FROM dbo.Menus
    WHERE MenuID<>@MenuID
      AND MenuGrupoID=@MenuGrupoID
      AND UPPER(LTRIM(RTRIM(Nombre))) =
          UPPER(LTRIM(RTRIM(@Nombre)))
)
    THROW 51014,'Ya existe otro menú con ese nombre dentro del grupo.',1;

UPDATE dbo.Menus
SET
    MenuGrupoID=@MenuGrupoID,
    Nombre=@Nombre,
    Descripcion=@Descripcion,
    IconoCss=@IconoCss,
    Orden=@Orden
WHERE MenuID=@MenuID;";

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@MenuID", SqlDbType.Int).Value = menuID;
            cmd.Parameters.Add("@MenuGrupoID", SqlDbType.Int).Value = menuGrupoID;
            cmd.Parameters.Add("@Nombre", SqlDbType.NVarChar, 100).Value = nombre;
            cmd.Parameters.Add("@Descripcion", SqlDbType.NVarChar, 300).Value =
                DbValor(descripcion);
            cmd.Parameters.Add("@IconoCss", SqlDbType.NVarChar, 100).Value =
                DbValor(iconoCss);
            cmd.Parameters.Add("@Orden", SqlDbType.Int).Value = orden;

            try
            {
                await cmd.ExecuteNonQueryAsync();
                TempData["Success"] = "Menú actualizado.";
            }
            catch (SqlException ex)
            {
                return Error(ex.Message);
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AlternarMenu(int menuID)
        {
            var auth = await ValidarAdministradorAsync();
            if (auth != null)
                return auth;

            await using var cn = new SqlConnection(_cnn);
            await cn.OpenAsync();

            const string sql = @"
UPDATE dbo.Menus
SET Activo=CASE WHEN Activo=1 THEN 0 ELSE 1 END
WHERE MenuID=@MenuID;

IF @@ROWCOUNT=0
    THROW 51015,'El menú ya no existe.',1;";

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@MenuID", SqlDbType.Int).Value = menuID;

            try
            {
                await cmd.ExecuteNonQueryAsync();
                TempData["Success"] = "Estado del menú actualizado.";
            }
            catch (SqlException ex)
            {
                return Error(ex.Message);
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CrearSubMenu(
            int menuID,
            string nombre,
            string urlEnlace,
            string? descripcion,
            string? iconoCss)
        {
            var auth = await ValidarAdministradorAsync();
            if (auth != null)
                return auth;

            nombre = Limpiar(nombre, 100);
            urlEnlace = NormalizarUrl(urlEnlace);
            descripcion = LimpiarNullable(descripcion, 255);
            iconoCss = LimpiarNullable(iconoCss, 100);

            if (menuID <= 0 || string.IsNullOrWhiteSpace(nombre))
                return Error("Selecciona un menú e indica el nombre del submenú.");

            if (!UrlInternaValida(urlEnlace))
            {
                return Error(
                    "La URL debe ser una ruta interna que comience con '/', por ejemplo /Produccion/Index.");
            }

            await using var cn = new SqlConnection(_cnn);
            await cn.OpenAsync();

            await using var tx =
                (SqlTransaction)await cn.BeginTransactionAsync();

            try
            {
                const string sql = @"
IF NOT EXISTS
(
    SELECT 1
    FROM dbo.Menus
    WHERE MenuID=@MenuID
)
    THROW 51020,'El menú seleccionado no existe.',1;

IF EXISTS
(
    SELECT 1
    FROM dbo.SubMenus
    WHERE MenuID=@MenuID
      AND UPPER(LTRIM(RTRIM(Nombre))) =
          UPPER(LTRIM(RTRIM(@Nombre)))
)
    THROW 51021,'Ya existe un submenú con ese nombre dentro del menú.',1;

INSERT INTO dbo.SubMenus
(
    MenuID,
    Nombre,
    UrlEnlace,
    Descripcion,
    Activo,
    FechaCreacion,
    FechaModificacion,
    IconoCSS
)
VALUES
(
    @MenuID,
    @Nombre,
    @UrlEnlace,
    @Descripcion,
    1,
    GETDATE(),
    NULL,
    @IconoCss
);

SELECT CAST(SCOPE_IDENTITY() AS int);";

                await using var cmd =
                    new SqlCommand(sql, cn, tx);

                cmd.Parameters.Add("@MenuID", SqlDbType.Int).Value = menuID;
                cmd.Parameters.Add("@Nombre", SqlDbType.NVarChar, 100).Value = nombre;
                cmd.Parameters.Add("@UrlEnlace", SqlDbType.VarChar, 500).Value = urlEnlace;
                cmd.Parameters.Add("@Descripcion", SqlDbType.VarChar, 255).Value =
                    DbValor(descripcion);
                cmd.Parameters.Add("@IconoCss", SqlDbType.NVarChar, 100).Value =
                    DbValor(iconoCss);

                var subMenuID =
                    Convert.ToInt32(await cmd.ExecuteScalarAsync());

                await GarantizarAccionesYAdminAsync(
                    subMenuID,
                    cn,
                    tx);

                await tx.CommitAsync();

                TempData["Success"] =
                    $"Submenú creado correctamente (ID {subMenuID}). " +
                    "Se asociaron las acciones disponibles y permiso al Administrador ERP.";
            }
            catch (Exception ex)
            {
                try
                {
                    await tx.RollbackAsync();
                }
                catch
                {
                }

                return Error(ex.Message);
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditarSubMenu(
            int subMenuID,
            int menuID,
            string nombre,
            string urlEnlace,
            string? descripcion,
            string? iconoCss)
        {
            var auth = await ValidarAdministradorAsync();
            if (auth != null)
                return auth;

            nombre = Limpiar(nombre, 100);
            urlEnlace = NormalizarUrl(urlEnlace);
            descripcion = LimpiarNullable(descripcion, 255);
            iconoCss = LimpiarNullable(iconoCss, 100);

            if (subMenuID <= 0 ||
                menuID <= 0 ||
                string.IsNullOrWhiteSpace(nombre))
            {
                return Error("Datos de submenú inválidos.");
            }

            if (!UrlInternaValida(urlEnlace))
            {
                return Error(
                    "La URL debe ser una ruta interna que comience con '/', por ejemplo /Produccion/Index.");
            }

            await using var cn = new SqlConnection(_cnn);
            await cn.OpenAsync();

            await using var tx =
                (SqlTransaction)await cn.BeginTransactionAsync();

            try
            {
                const string sql = @"
IF NOT EXISTS
(
    SELECT 1
    FROM dbo.Menus
    WHERE MenuID=@MenuID
)
    THROW 51022,'El menú seleccionado no existe.',1;

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.SubMenus
    WHERE SubMenuID=@SubMenuID
)
    THROW 51023,'El submenú ya no existe.',1;

IF EXISTS
(
    SELECT 1
    FROM dbo.SubMenus
    WHERE SubMenuID<>@SubMenuID
      AND MenuID=@MenuID
      AND UPPER(LTRIM(RTRIM(Nombre))) =
          UPPER(LTRIM(RTRIM(@Nombre)))
)
    THROW 51024,'Ya existe otro submenú con ese nombre dentro del menú.',1;

UPDATE dbo.SubMenus
SET
    MenuID=@MenuID,
    Nombre=@Nombre,
    UrlEnlace=@UrlEnlace,
    Descripcion=@Descripcion,
    IconoCSS=@IconoCss,
    FechaModificacion=GETDATE()
WHERE SubMenuID=@SubMenuID;";

                await using var cmd =
                    new SqlCommand(sql, cn, tx);

                cmd.Parameters.Add("@SubMenuID", SqlDbType.Int).Value = subMenuID;
                cmd.Parameters.Add("@MenuID", SqlDbType.Int).Value = menuID;
                cmd.Parameters.Add("@Nombre", SqlDbType.NVarChar, 100).Value = nombre;
                cmd.Parameters.Add("@UrlEnlace", SqlDbType.VarChar, 500).Value = urlEnlace;
                cmd.Parameters.Add("@Descripcion", SqlDbType.VarChar, 255).Value =
                    DbValor(descripcion);
                cmd.Parameters.Add("@IconoCss", SqlDbType.NVarChar, 100).Value =
                    DbValor(iconoCss);

                await cmd.ExecuteNonQueryAsync();

                await GarantizarAccionesYAdminAsync(
                    subMenuID,
                    cn,
                    tx);

                await tx.CommitAsync();

                TempData["Success"] = "Submenú actualizado.";
            }
            catch (Exception ex)
            {
                try
                {
                    await tx.RollbackAsync();
                }
                catch
                {
                }

                return Error(ex.Message);
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AlternarSubMenu(int subMenuID)
        {
            var auth = await ValidarAdministradorAsync();
            if (auth != null)
                return auth;

            await using var cn = new SqlConnection(_cnn);
            await cn.OpenAsync();

            await using var tx =
                (SqlTransaction)await cn.BeginTransactionAsync();

            try
            {
                const string sql = @"
UPDATE dbo.SubMenus
SET
    Activo=CASE WHEN Activo=1 THEN 0 ELSE 1 END,
    FechaModificacion=GETDATE()
WHERE SubMenuID=@SubMenuID;

IF @@ROWCOUNT=0
    THROW 51025,'El submenú ya no existe.',1;";

                await using var cmd =
                    new SqlCommand(sql, cn, tx);

                cmd.Parameters.Add("@SubMenuID", SqlDbType.Int).Value = subMenuID;
                await cmd.ExecuteNonQueryAsync();

                await GarantizarAccionesYAdminAsync(
                    subMenuID,
                    cn,
                    tx);

                await tx.CommitAsync();

                TempData["Success"] =
                    "Estado del submenú actualizado.";
            }
            catch (Exception ex)
            {
                try
                {
                    await tx.RollbackAsync();
                }
                catch
                {
                }

                return Error(ex.Message);
            }

            return RedirectToAction(nameof(Index));
        }

        private async Task GarantizarAccionesYAdminAsync(
            int subMenuID,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
UPDATE sma
SET
    Activo=1,
    FechaModificacion=GETDATE()
FROM dbo.SubMenuAcciones sma
WHERE sma.SubMenuID=@SubMenuID
  AND sma.Activo=0;

INSERT INTO dbo.SubMenuAcciones
(
    SubMenuID,
    AccionID,
    Activo,
    FechaCreacion,
    FechaModificacion
)
SELECT
    @SubMenuID,
    a.AccionID,
    1,
    GETDATE(),
    NULL
FROM dbo.Acciones a
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.SubMenuAcciones sma
    WHERE sma.SubMenuID=@SubMenuID
      AND sma.AccionID=a.AccionID
);

UPDATE pr
SET
    Activo=1,
    FechaModificacion=GETDATE()
FROM dbo.PermisosPorRol pr
INNER JOIN dbo.SubMenuAcciones sma
    ON sma.SubMenuAccionID=pr.SubMenuAccionID
WHERE pr.RolID=@RolID
  AND sma.SubMenuID=@SubMenuID
  AND pr.Activo=0;

INSERT INTO dbo.PermisosPorRol
(
    RolID,
    SubMenuAccionID,
    Activo,
    FechaCreacion,
    FechaModificacion
)
SELECT
    @RolID,
    sma.SubMenuAccionID,
    1,
    GETDATE(),
    NULL
FROM dbo.SubMenuAcciones sma
WHERE sma.SubMenuID=@SubMenuID
  AND sma.Activo=1
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.PermisosPorRol pr
      WHERE pr.RolID=@RolID
        AND pr.SubMenuAccionID=sma.SubMenuAccionID
  );";

            await using var cmd =
                new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@SubMenuID", SqlDbType.Int).Value = subMenuID;
            cmd.Parameters.Add("@RolID", SqlDbType.Int).Value = RolAdministradorErp;

            await cmd.ExecuteNonQueryAsync();
        }

        private async Task<IActionResult?> ValidarAdministradorAsync()
        {
            var usuarioId =
                HttpContext.Session.GetInt32("UsuarioID");

            if (!usuarioId.HasValue ||
                usuarioId.Value <= 0)
            {
                return RedirectToAction(
                    "Login",
                    "Login");
            }

            if (!await EsAdministradorErpAsync(usuarioId.Value))
                return Forbid();

            return null;
        }

        private async Task<bool> EsAdministradorErpAsync(int usuarioId)
        {
            await using var cn = new SqlConnection(_cnn);
            await cn.OpenAsync();

            const string sql = @"
SELECT CAST(
    CASE
        WHEN EXISTS
        (
            SELECT 1
            FROM dbo.Usuarios u
            WHERE u.UsuarioID=@UsuarioID
              AND u.Activo=1
              AND u.RolID=@RolID
        )
        THEN 1
        ELSE 0
    END
AS bit);";

            await using var cmd =
                new SqlCommand(sql, cn);

            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            cmd.Parameters.Add("@RolID", SqlDbType.Int).Value = RolAdministradorErp;

            var value = await cmd.ExecuteScalarAsync();

            return value != null &&
                   value != DBNull.Value &&
                   Convert.ToBoolean(value);
        }

        private IActionResult Error(string mensaje)
        {
            TempData["Error"] = mensaje;
            return RedirectToAction(nameof(Index));
        }

        private static string Limpiar(string? valor, int max)
        {
            var s = (valor ?? string.Empty).Trim();

            if (s.Length > max)
                s = s[..max];

            return s;
        }

        private static string? LimpiarNullable(
            string? valor,
            int max)
        {
            var s = (valor ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(s))
                return null;

            if (s.Length > max)
                s = s[..max];

            return s;
        }

        private static string NormalizarUrl(string? valor)
        {
            var s = Limpiar(valor, 500);

            if (string.IsNullOrWhiteSpace(s))
                return string.Empty;

            if (!s.StartsWith('/'))
                s = "/" + s;

            return s;
        }

        private static bool UrlInternaValida(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            if (!url.StartsWith('/'))
                return false;

            if (url.StartsWith("//"))
                return false;

            return !url.Contains(
                "://",
                StringComparison.OrdinalIgnoreCase);
        }

        private static object DbValor(string? valor)
        {
            return string.IsNullOrWhiteSpace(valor)
                ? DBNull.Value
                : valor;
        }
    }
}