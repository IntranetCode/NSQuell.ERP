using System.Data;
using System.Data.SqlClient;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace ERP.NSQuell.Servicios
{
    public class ServicioAcceso : IServicioAcceso
    {
        private readonly string _connStr;
        private readonly IHttpContextAccessor _http;

        public ServicioAcceso(
            IConfiguration configuration,
            IHttpContextAccessor http)
        {
            _connStr = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException(
                    "No está configurada la conexión DefaultConnection.");

            _http = http;
        }

        public Task<bool> TienePermisoAsync(
            int usuarioId,
            string subMenu,
            string? accion = null)
        {
            int? empresaId =
                _http.HttpContext?.Session.GetInt32("EmpresaID");

            if (empresaId is null)
            {
                var claim =
                    _http.HttpContext?.User?
                        .FindFirst("EmpresaID")?
                        .Value;

                if (int.TryParse(claim, out var valorEmpresa))
                    empresaId = valorEmpresa;
            }

            return TienePermisoAsync(
                usuarioId,
                empresaId,
                subMenu,
                accion);
        }

        public async Task<bool> TienePermisoAsync(
            int usuarioId,
            int? empresaId,
            string subMenu,
            string? accion = null)
        {
            if (usuarioId <= 0 || string.IsNullOrWhiteSpace(subMenu))
                return false;

            using var connection = new SqlConnection(_connStr);
            await connection.OpenAsync();

            var cantidadParametros =
                await ObtenerCantidadParametrosFuncionAsync(connection);

            var funcionPermisos = cantidadParametros switch
            {
                1 => "dbo.fn_PermisosEfectivosUsuario(@u)",
                2 => "dbo.fn_PermisosEfectivosUsuario(@u,@e)",
                _ => throw new InvalidOperationException(
                    $"dbo.fn_PermisosEfectivosUsuario tiene {cantidadParametros} parámetros; se esperaban 1 o 2.")
            };

            var sql = $@"
DECLARE @SubId INT =
(
    SELECT TOP (1) SubMenuID
    FROM dbo.SubMenus
    WHERE UPPER(LTRIM(RTRIM(Nombre))) =
          UPPER(LTRIM(RTRIM(@s)))
      AND Activo = 1
    ORDER BY SubMenuID
);

IF @SubId IS NULL
BEGIN
    SELECT CAST(0 AS bit);
    RETURN;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM {funcionPermisos} permisos
    WHERE permisos.SubMenuID = @SubId
      AND permisos.TienePermiso = 1
)
BEGIN
    SELECT CAST(0 AS bit);
    RETURN;
END;

IF @accion IS NULL OR LTRIM(RTRIM(@accion)) = ''
BEGIN
    SELECT CAST(1 AS bit);
    RETURN;
END;

IF UPPER(LTRIM(RTRIM(@accion))) = 'VER'
BEGIN
    SELECT CAST(1 AS bit);
    RETURN;
END;

IF EXISTS
(
    SELECT 1
    FROM dbo.PermisosUsuarioOverride overrideUsuario
    INNER JOIN dbo.SubMenuAcciones subMenuAccion
        ON subMenuAccion.SubMenuID = overrideUsuario.SubMenuID
       AND subMenuAccion.SubMenuID = @SubId
    INNER JOIN dbo.Acciones accionCatalogo
        ON accionCatalogo.AccionID = subMenuAccion.AccionID
    WHERE overrideUsuario.UsuarioID = @u
      AND UPPER(LTRIM(RTRIM(accionCatalogo.Nombre))) =
          UPPER(LTRIM(RTRIM(@accion)))
      AND overrideUsuario.Decision = 0
)
BEGIN
    SELECT CAST(0 AS bit);
    RETURN;
END;

IF EXISTS
(
    SELECT 1
    FROM dbo.PermisosUsuarioOverride overrideUsuario
    INNER JOIN dbo.SubMenuAcciones subMenuAccion
        ON subMenuAccion.SubMenuID = overrideUsuario.SubMenuID
       AND subMenuAccion.SubMenuID = @SubId
    INNER JOIN dbo.Acciones accionCatalogo
        ON accionCatalogo.AccionID = subMenuAccion.AccionID
    WHERE overrideUsuario.UsuarioID = @u
      AND UPPER(LTRIM(RTRIM(accionCatalogo.Nombre))) =
          UPPER(LTRIM(RTRIM(@accion)))
      AND overrideUsuario.Decision = 1
)
BEGIN
    SELECT CAST(1 AS bit);
    RETURN;
END;

SELECT CAST
(
    CASE
        WHEN EXISTS
        (
            SELECT 1
            FROM dbo.Usuarios usuario
            INNER JOIN dbo.PermisosPorRol permisoRol
                ON permisoRol.RolID = usuario.RolID
               AND permisoRol.Activo = 1
            INNER JOIN dbo.SubMenuAcciones subMenuAccion
                ON subMenuAccion.SubMenuAccionID =
                   permisoRol.SubMenuAccionID
            INNER JOIN dbo.Acciones accionCatalogo
                ON accionCatalogo.AccionID =
                   subMenuAccion.AccionID
            WHERE usuario.UsuarioID = @u
              AND subMenuAccion.SubMenuID = @SubId
              AND UPPER(LTRIM(RTRIM(accionCatalogo.Nombre))) =
                  UPPER(LTRIM(RTRIM(@accion)))
        )
        THEN 1
        ELSE 0
    END
    AS bit
);";

            using var command = new SqlCommand(sql, connection);

            command.Parameters.Add("@u", SqlDbType.Int).Value =
                usuarioId;

            command.Parameters.Add("@e", SqlDbType.Int).Value =
                empresaId.HasValue
                    ? empresaId.Value
                    : DBNull.Value;

            command.Parameters.Add("@s", SqlDbType.NVarChar, 200).Value =
                subMenu.Trim();

            command.Parameters.Add("@accion", SqlDbType.NVarChar, 100).Value =
                string.IsNullOrWhiteSpace(accion)
                    ? DBNull.Value
                    : accion.Trim();

            var resultado = await command.ExecuteScalarAsync();

            return resultado is bool permitido && permitido;
        }

        private static async Task<int>
            ObtenerCantidadParametrosFuncionAsync(
                SqlConnection connection)
        {
            const string sql = @"
SELECT COUNT(*)
FROM sys.parameters
WHERE object_id =
      OBJECT_ID(N'dbo.fn_PermisosEfectivosUsuario')
  AND parameter_id > 0;";

            using var command = new SqlCommand(sql, connection);
            var resultado = await command.ExecuteScalarAsync();

            return Convert.ToInt32(resultado ?? 0);
        }
    }
}

