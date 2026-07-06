using System;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using ERP.NSQuell.Models;
using ERP.NSQuell.Servicios;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Security.Claims;
using System.Text;
using System.Security.Cryptography;
using System.Threading.Tasks;

public class LoginController : Controller
{
    private readonly string _connectionString;
    private readonly BitacoraService _bitacoraService;
    private readonly ServicioNotificaciones _servicioNotificaciones;

    public LoginController(
        IConfiguration configuration,
        BitacoraService bitacora,
        ServicioNotificaciones servicioNotificaciones)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection");
        _bitacoraService = bitacora;
        _servicioNotificaciones = servicioNotificaciones;
    }

    // ---------- LOGIN GET ----------
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login()
    {
        if (HttpContext.Session.GetInt32("UsuarioCambioPasswordID") != null)
        {
            return RedirectToAction("CambiarPasswordInicial", "CuentaPassword");
        }

        if (HttpContext.Session.GetInt32("UsuarioID") != null)
        {
            return RedirectToAction("Index", "Menu");
        }

        return View();
    }

    // ---------- LOGIN POST ----------
    [HttpPost]
    [AllowAnonymous]
    [AuditarAccion(Modulo = "SEGURIDAD", Entidad = "Login", Operacion = "LOGIN", OmitirListas = false)]
    public async Task<IActionResult> Login(UsuarioModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var usuario = await ObtenerUsuarioActivoAsync(model.Username, _connectionString);

        var solicitudId = HttpContext.Items["SolicitudId"]?.ToString();
        var direccionIp = HttpContext.Items["DireccionIp"]?.ToString();
        var agenteUsuario = HttpContext.Items["AgenteUsuario"]?.ToString();

        if (usuario == null || usuario.Password != model.Password)
        {
            try
            {
                await _bitacoraService.RegistrarAsync(
                    idUsuario: null,
                    accion: "LOGIN_FALLIDO",
                    mensaje: $"Intento fallido con usuario {model.Username}",
                    modulo: "SEGURIDAD",
                    entidad: "Login",
                    entidadId: null,
                    resultado: "ERROR",
                    severidad: 3,
                    solicitudId: solicitudId,
                    ip: direccionIp,
                    AgenteUsuario: agenteUsuario
                );
            }
            catch
            {
                // No romper login por error de bitácora.
            }

            ModelState.AddModelError("", "Usuario o contraseña incorrectos");
            return View(model);
        }

        return await CompletarLogin(usuario);
    }

    // ---------- VALIDAR POLÍTICA DE CONTRASEÑA ----------
    private async Task<bool> DebeRedirigirCambioPasswordAsync(int usuarioId)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        const string query = @"
            SELECT TOP 1
                ISNULL(DebeCambiarPassword, 0) AS DebeCambiarPassword,
                FechaUltimoCambioPassword
            FROM dbo.Usuarios
            WHERE UsuarioID = @UsuarioID
              AND Activo = 1;";

        using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@UsuarioID", usuarioId);

        using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return false;
        }

        var debeCambiar = false;
        var ordDebeCambiar = reader.GetOrdinal("DebeCambiarPassword");

        if (!reader.IsDBNull(ordDebeCambiar))
        {
            var valor = reader.GetValue(ordDebeCambiar);

            if (valor is bool booleano)
            {
                debeCambiar = booleano;
            }
            else
            {
                debeCambiar = Convert.ToInt32(valor) == 1;
            }
        }

        DateTime? fechaUltimoCambio = null;
        var ordFechaUltimoCambio = reader.GetOrdinal("FechaUltimoCambioPassword");

        if (!reader.IsDBNull(ordFechaUltimoCambio))
        {
            fechaUltimoCambio = reader.GetDateTime(ordFechaUltimoCambio);
        }

        if (debeCambiar)
        {
            return true;
        }

        // Si la fecha viene NULL, se obliga el cambio para que el usuario entre a la política.
        if (!fechaUltimoCambio.HasValue)
        {
            return true;
        }

        var fechaLimite = DateTime.Now.AddMonths(-2);
        return fechaUltimoCambio.Value <= fechaLimite;
    }

    // ---------- COMPLETAR LOGIN ----------
    private async Task<IActionResult> CompletarLogin(UsuarioModel usuario)
    {
        if (await DebeRedirigirCambioPasswordAsync(usuario.UsuarioID))
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            HttpContext.Session.Remove("UsuarioID");
            HttpContext.Session.Remove("Username");
            HttpContext.Session.Remove("MenuUsuario");

            HttpContext.Session.SetInt32("UsuarioCambioPasswordID", usuario.UsuarioID);
            HttpContext.Session.SetString("UsernameCambioPassword", usuario.Username ?? "");

            TempData["InfoMessage"] = "Por seguridad, debes actualizar tu contraseña antes de ingresar al ERP.";

            return RedirectToAction("CambiarPasswordInicial", "CuentaPassword");
        }

        int rolId = await ObtenerRolIdPorUsuarioAsync(usuario.UsuarioID);

        var perfilSesion = await ObtenerPerfilSesionUsuarioAsync(usuario.UsuarioID);

        var nombreMostrar = !string.IsNullOrWhiteSpace(perfilSesion?.NombreCompleto)
            ? perfilSesion.NombreCompleto
            : await ObtenerNombreMostrarPorUsuarioAsync(usuario.UsuarioID);

        if (string.IsNullOrWhiteSpace(nombreMostrar))
        {
            nombreMostrar = usuario.Username;
        }

        var rolSesion = !string.IsNullOrWhiteSpace(perfilSesion?.Rol)
            ? perfilSesion.Rol
            : usuario.Rol;

        var menuUsuario = await ObtenerMenuEfectivoPorUsuarioAsync(usuario.UsuarioID);

        // Sesión
        HttpContext.Session.SetInt32("UsuarioID", usuario.UsuarioID);
        HttpContext.Session.SetString("Username", usuario.Username ?? "");

        HttpContext.Session.SetString("Rol", rolSesion ?? "");
        HttpContext.Session.SetString("NombreRol", rolSesion ?? "");
        HttpContext.Session.SetInt32("RolID", rolId);

        HttpContext.Session.SetString("NombreMostrar", nombreMostrar ?? usuario.Username ?? "");
        HttpContext.Session.SetString("NombreCompleto", nombreMostrar ?? usuario.Username ?? "");

        HttpContext.Session.SetString("Correo", perfilSesion?.Correo ?? "");
        HttpContext.Session.SetString("Email", perfilSesion?.Correo ?? "");
        HttpContext.Session.SetString("CorreoUsuario", perfilSesion?.Correo ?? "");

        HttpContext.Session.SetString("Telefono", perfilSesion?.Telefono ?? "");
        HttpContext.Session.SetString("TelefonoUsuario", perfilSesion?.Telefono ?? "");

        HttpContext.Session.SetString("DescripcionRol", perfilSesion?.DescripcionRol ?? "");
        HttpContext.Session.SetString("DescripcionDelRol", perfilSesion?.DescripcionRol ?? "");
        HttpContext.Session.SetString("RolDescripcion", perfilSesion?.DescripcionRol ?? "");

        HttpContext.Session.SetString("MenuUsuario", JsonConvert.SerializeObject(menuUsuario));

        // Autenticación por cookies
        var claims = new List<Claim>
        {
            // Identidad humana
            new Claim(ClaimTypes.Name, nombreMostrar ?? usuario.Username ?? $"user:{usuario.UsuarioID}"),

            // Identificador de usuario
            new Claim("UsuarioID", usuario.UsuarioID.ToString()),
            new Claim(ClaimTypes.NameIdentifier, usuario.UsuarioID.ToString()),

            // Datos visibles
            new Claim("Username", usuario.Username ?? ""),
            new Claim("NombreMostrar", nombreMostrar ?? usuario.Username ?? ""),
            new Claim("Correo", perfilSesion?.Correo ?? ""),
            new Claim("Telefono", perfilSesion?.Telefono ?? ""),

            // Rol
            new Claim(ClaimTypes.Role, rolSesion ?? "Usuario"),
            new Claim("Rol", rolSesion ?? "Usuario"),
            new Claim("RolID", rolId.ToString()),
            new Claim("DescripcionRol", perfilSesion?.DescripcionRol ?? "")
        };

        TempData["MostrarBienvenida"] = "true";

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

        if (rolId == 7)
        {
            return RedirectToAction("Index", "Universidad");
        }

        TempData["Bienvenida"] = $"Bienvenido, {nombreMostrar ?? usuario.Username}";
        return RedirectToAction("Index", "Menu");
    }

    // ---------- OBTENER USUARIO POR USERNAME ----------
    private async Task<UsuarioModel?> ObtenerUsuarioActivoAsync(string username, string connectionString)
    {
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        string query = @"
            SELECT TOP 1 
                u.UsuarioID, 
                u.Username, 
                u.Contrasena AS PasswordHash, 
                r.NombreRol AS Rol
            FROM dbo.Usuarios u
            INNER JOIN dbo.Roles r 
                ON u.RolID = r.RolID
            WHERE u.Username = @Username
              AND u.Activo = 1;";

        using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@Username", username);

        using var reader = await command.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            return new UsuarioModel
            {
                UsuarioID = reader.GetInt32(reader.GetOrdinal("UsuarioID")),
                Username = reader.GetString(reader.GetOrdinal("Username")),
                Password = reader.GetString(reader.GetOrdinal("PasswordHash")),
                Rol = reader.GetString(reader.GetOrdinal("Rol"))
            };
        }

        return null;
    }

    // ---------- OBTENER USUARIO POR ID ----------
    private async Task<UsuarioModel?> ObtenerUsuarioActivoPorIdAsync(int usuarioId)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        string query = @"
            SELECT TOP 1 
                u.UsuarioID, 
                u.Username, 
                u.Contrasena AS PasswordHash, 
                r.NombreRol AS Rol
            FROM dbo.Usuarios u
            INNER JOIN dbo.Roles r 
                ON u.RolID = r.RolID
            WHERE u.UsuarioID = @UsuarioID
              AND u.Activo = 1;";

        using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@UsuarioID", usuarioId);

        using var reader = await command.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            return new UsuarioModel
            {
                UsuarioID = reader.GetInt32(reader.GetOrdinal("UsuarioID")),
                Username = reader.GetString(reader.GetOrdinal("Username")),
                Password = reader.GetString(reader.GetOrdinal("PasswordHash")),
                Rol = reader.GetString(reader.GetOrdinal("Rol"))
            };
        }

        return null;
    }

    // ---------- OBTENER MENÚ POR USUARIO ----------
    private async Task<List<MenuModel>> ObtenerMenuEfectivoPorUsuarioAsync(int usuarioId)
    {
        var lista = new List<MenuModel>();

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        const string sql = @"
    WITH Perms AS (
        SELECT SubMenuID
        FROM dbo.fn_PermisosEfectivosUsuario(@UsuarioID)
        WHERE TienePermiso = 1
    )
    SELECT DISTINCT 
        m.MenuID, 
        m.Nombre AS NombreMenu,
        ISNULL(m.IconoCss, 'fa-solid fa-folder') AS Icono,
        ISNULL(m.Descripcion, '') AS Descripcion,
        ISNULL(m.Orden, 0) AS Orden,
        ISNULL(sm.UrlEnlace, '') AS UrlEnlace
    FROM dbo.Menus m
    INNER JOIN dbo.SubMenus sm 
        ON sm.MenuID = m.MenuID
    INNER JOIN Perms p  
        ON p.SubMenuID = sm.SubMenuID
    WHERE ISNULL(m.Activo, 1) = 1
      AND ISNULL(sm.Activo, 1) = 1
    ORDER BY ISNULL(m.Orden, 0), m.Nombre;";

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);

        using var rd = await cmd.ExecuteReaderAsync();

        while (await rd.ReadAsync())
        {
            lista.Add(new MenuModel
{
    MenuID = Convert.ToInt32(rd["MenuID"]),
    Nombre = rd["NombreMenu"]?.ToString() ?? "",
    Icono = rd["Icono"]?.ToString() ?? "fa-solid fa-folder",
    Descripcion = rd["Descripcion"]?.ToString() ?? "",
    Url = rd["UrlEnlace"]?.ToString() ?? "#",
    Orden = Convert.ToInt32(rd["Orden"])
});
        }

        return lista;
    }

    // ---------- LOGOUT ----------
    [HttpGet]
    [AuditarAccion(Modulo = "SEGURIDAD", Entidad = "Login", Operacion = "LOGOUT", OmitirListas = false)]
    public async Task<IActionResult> Logout()
    {
        var solicitudId = HttpContext.Items["SolicitudId"]?.ToString();
        var direccionIp = HttpContext.Items["DireccionIp"]?.ToString();
        var agenteUsuario = HttpContext.Items["AgenteUsuario"]?.ToString();

        int? idUsuario = null;

        if (int.TryParse(User.FindFirst("UsuarioID")?.Value, out var uid))
            idUsuario = uid;
        else if (int.TryParse(User.FindFirst("UserID")?.Value, out uid))
            idUsuario = uid;
        else if (int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out uid))
            idUsuario = uid;
        else
            idUsuario = HttpContext.Session.GetInt32("UsuarioID");

     

        try
        {
            await _bitacoraService.RegistrarAsync(
                idUsuario: idUsuario,
             
                accion: "LOGOUT",
                mensaje: $"Usuario {idUsuario} cerró sesión",
                modulo: "SEGURIDAD",
                entidad: "Login",
                entidadId: idUsuario?.ToString(),
                resultado: "OK",
                severidad: 4,
                solicitudId: solicitudId,
                ip: direccionIp,
                AgenteUsuario: agenteUsuario
            );
        }
        catch
        {
            // Nunca romper el flujo de logout por bitácora.
        }

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        HttpContext.Session.Clear();

        return RedirectToAction("Login");
    }

    private sealed class PerfilSesionUsuario
    {
        public string NombreCompleto { get; set; } = "";
        public string Correo { get; set; } = "";
        public string Telefono { get; set; } = "";
        public string Rol { get; set; } = "";
        public string DescripcionRol { get; set; } = "";
    }

    private async Task<PerfilSesionUsuario?> ObtenerPerfilSesionUsuarioAsync(int usuarioId)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var columnaDescripcionRol = await ObtenerColumnaDescripcionRolAsync(connection);

        var expresionDescripcionRol = string.IsNullOrWhiteSpace(columnaDescripcionRol)
            ? "CAST('' AS nvarchar(500))"
            : $"ISNULL(r.[{columnaDescripcionRol}], '')";

        string query = $@"
            SELECT TOP 1
                LTRIM(RTRIM(
                    ISNULL(p.Nombre, '') + ' ' +
                    ISNULL(p.ApellidoPaterno, '') + ' ' +
                    ISNULL(p.ApellidoMaterno, '')
                )) AS NombreCompleto,
                ISNULL(p.Correo, '') AS Correo,
                ISNULL(p.Telefono, '') AS Telefono,
                ISNULL(r.NombreRol, '') AS Rol,
                {expresionDescripcionRol} AS DescripcionRol
            FROM dbo.Usuarios u
            INNER JOIN dbo.Persona p 
                ON p.PersonaID = u.PersonaID
            INNER JOIN dbo.Roles r 
                ON r.RolID = u.RolID
            WHERE u.UsuarioID = @UsuarioID
              AND u.Activo = 1;";

        using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@UsuarioID", usuarioId);

        using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return null;
        }

        var rol = reader["Rol"]?.ToString() ?? "";
        var descripcionRol = reader["DescripcionRol"]?.ToString() ?? "";

        if (string.IsNullOrWhiteSpace(descripcionRol))
        {
            descripcionRol = ObtenerDescripcionRolDefault(rol);
        }

        return new PerfilSesionUsuario
        {
            NombreCompleto = reader["NombreCompleto"]?.ToString() ?? "",
            Correo = reader["Correo"]?.ToString() ?? "",
            Telefono = reader["Telefono"]?.ToString() ?? "",
            Rol = rol,
            DescripcionRol = descripcionRol
        };
    }

    private static async Task<string?> ObtenerColumnaDescripcionRolAsync(SqlConnection connection)
    {
        const string query = @"
            SELECT TOP 1 c.name
            FROM sys.columns c
            INNER JOIN sys.objects o 
                ON o.object_id = c.object_id
            WHERE o.object_id = OBJECT_ID('dbo.Roles')
              AND c.name IN ('DescripcionRol', 'Descripcion', 'DescripcionDelRol', 'RolDescripcion')
            ORDER BY CASE c.name
                WHEN 'DescripcionRol' THEN 1
                WHEN 'Descripcion' THEN 2
                WHEN 'DescripcionDelRol' THEN 3
                WHEN 'RolDescripcion' THEN 4
                ELSE 5
            END;";

        using var command = new SqlCommand(query, connection);
        var result = await command.ExecuteScalarAsync();

        return result?.ToString();
    }

    private static string ObtenerDescripcionRolDefault(string? rol)
    {
        if (string.IsNullOrWhiteSpace(rol))
            return "";

        return rol.Trim().ToUpperInvariant() switch
        {
            "USUARIO FINAL" => "Colaborador que accede y consulta contenidos del portal",
            "COLABORADOR" => "Colaborador que accede y consulta contenidos del portal",
            "ADMIN" => "Administrador del sistema con acceso a funciones de gestión",
            "ADMINISTRADOR" => "Administrador del sistema con acceso a funciones de gestión",
            _ => ""
        };
    }

    // =========================================================
    // RECUPERACIÓN DE CONTRASEÑA POR CÓDIGO EN CORREO
    // =========================================================

    private sealed class UsuarioRecuperacionCorreo
    {
        public int UsuarioID { get; set; }
        public int PersonaID { get; set; }
        public string Username { get; set; } = "";
        public string NombreCompleto { get; set; } = "";
        public string Correo { get; set; } = "";
    }

    private sealed class CodigoRecuperacionActivo
    {
        public int PasswordRecoveryCodeID { get; set; }
        public int UsuarioID { get; set; }
        public string CodigoHash { get; set; } = "";
        public int Intentos { get; set; }
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult RecuperarPassword()
    {
        return View();
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SolicitarCodigoRecuperacion(string usuarioOCorreo)
    {
        usuarioOCorreo = (usuarioOCorreo ?? "").Trim();

        if (string.IsNullOrWhiteSpace(usuarioOCorreo))
        {
            return Json(new
            {
                ok = false,
                mensaje = "Ingresa tu usuario o correo registrado."
            });
        }

        var usuario = await BuscarUsuarioParaRecuperacionCorreoAsync(usuarioOCorreo);

        if (usuario == null || usuario.UsuarioID <= 0 || usuario.PersonaID <= 0 || string.IsNullOrWhiteSpace(usuario.Correo))
        {
            return Json(new
            {
                ok = false,
                mensaje = "No fue posible completar la recuperación automáticamente. Verifica que tu usuario o correo sea correcto y que tengas un correo registrado."
            });
        }

        var codigo = GenerarCodigoRecuperacion();

        await GuardarCodigoRecuperacionAsync(usuario.UsuarioID, usuario.PersonaID, codigo);

        var asunto = "Código de recuperación de contraseña - ERP NSQuell";
        var nombreSeguro = System.Net.WebUtility.HtmlEncode(
            string.IsNullOrWhiteSpace(usuario.NombreCompleto)
                ? usuario.Username
                : usuario.NombreCompleto
        );

        var codigoSeguro = System.Net.WebUtility.HtmlEncode(codigo);

        var html = $@"
            <div style='font-family:Segoe UI,Arial,sans-serif;color:#1f2937;line-height:1.5;'>
                <h2 style='color:#0b1744;margin-bottom:8px;'>Recuperación de contraseña</h2>
                <p>Hola <strong>{nombreSeguro}</strong>.</p>
                <p>Recibimos una solicitud para cambiar la contraseña de tu cuenta en el ERP.</p>
                <p>Tu código de verificación es:</p>
                <div style='font-size:30px;font-weight:800;letter-spacing:8px;color:#0b1744;background:#f1f5f9;border-radius:12px;padding:16px 20px;display:inline-block;margin:12px 0;'>
                    {codigoSeguro}
                </div>
                <p style='font-size:13px;color:#64748b;'>Este código vence en 10 minutos y solo puede usarse una vez.</p>
                <p style='font-size:13px;color:#64748b;'>Si tú no solicitaste este cambio, ignora este correo.</p>
            </div>";

        try
        {
            var resultadoAviso = await _servicioNotificaciones.EnviarABccPersonasAsync(
                new List<int> { usuario.PersonaID },
                asunto,
                html
            );

            if (resultadoAviso == null || resultadoAviso.Enviados <= 0 || resultadoAviso.Errores > 0)
            {
                return Json(new
                {
                    ok = false,
                    mensaje = "No fue posible enviar el código en este momento. Intenta nuevamente más tarde."
                });
            }

            return Json(new
            {
                ok = true,
                mensaje = "Si la cuenta existe y tiene correo registrado, enviaremos un código de verificación."
            });
        }
        catch
        {
            return Json(new
            {
                ok = false,
                mensaje = "No fue posible enviar el código en este momento. Intenta nuevamente más tarde."
            });
        }
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ValidarCodigoRecuperacion(string usuarioOCorreo, string codigo)
    {
        usuarioOCorreo = (usuarioOCorreo ?? "").Trim();
        codigo = (codigo ?? "").Trim();

        if (string.IsNullOrWhiteSpace(usuarioOCorreo) || string.IsNullOrWhiteSpace(codigo))
        {
            return Json(new
            {
                ok = false,
                mensaje = "Ingresa tu usuario/correo y el código recibido."
            });
        }

        var resultado = await ValidarCodigoRecuperacionAsync(
            usuarioOCorreo,
            codigo,
            registrarIntentoFallido: true
        );

        if (!resultado)
        {
            return Json(new
            {
                ok = false,
                mensaje = "El código no es válido o ya no puede usarse. Solicita un nuevo código para continuar."
            });
        }

        return Json(new
        {
            ok = true,
            mensaje = "Código validado correctamente. Ahora define tu nueva contraseña."
        });
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CambiarPasswordRecuperacion(
        string usuarioOCorreo,
        string codigo,
        string nuevaPassword,
        string confirmarPassword)
    {
        usuarioOCorreo = (usuarioOCorreo ?? "").Trim();
        codigo = (codigo ?? "").Trim();
        nuevaPassword = nuevaPassword ?? "";
        confirmarPassword = confirmarPassword ?? "";

        if (string.IsNullOrWhiteSpace(usuarioOCorreo) || string.IsNullOrWhiteSpace(codigo))
        {
            return Json(new
            {
                ok = false,
                mensaje = "La solicitud no es válida. Vuelve a solicitar un código."
            });
        }

        if (string.IsNullOrWhiteSpace(nuevaPassword) || nuevaPassword.Length < 6)
        {
            return Json(new
            {
                ok = false,
                mensaje = "La contraseña debe tener al menos 6 caracteres."
            });
        }

        if (nuevaPassword != confirmarPassword)
        {
            return Json(new
            {
                ok = false,
                mensaje = "Las contraseñas no coinciden."
            });
        }

        var usuario = await BuscarUsuarioParaRecuperacionCorreoAsync(usuarioOCorreo);

        if (usuario == null)
        {
            return Json(new
            {
                ok = false,
                mensaje = "No fue posible completar la recuperación. Solicita un nuevo código."
            });
        }

        var codigoActivo = await ObtenerCodigoRecuperacionActivoAsync(usuario.UsuarioID);

        if (codigoActivo == null || codigoActivo.CodigoHash != CalcularHashCodigo(codigo))
        {
            await RegistrarIntentoFallidoCodigoAsync(codigoActivo);

            return Json(new
            {
                ok = false,
                mensaje = "El código no es válido o ya no puede usarse. Solicita un nuevo código para continuar."
            });
        }

        var actualizado = await ActualizarPasswordRecuperacionAsync(
            codigoActivo.PasswordRecoveryCodeID,
            usuario.UsuarioID,
            nuevaPassword
        );

        if (!actualizado)
        {
            return Json(new
            {
                ok = false,
                mensaje = "No se pudo actualizar la contraseña. Solicita un nuevo código."
            });
        }

        return Json(new
        {
            ok = true,
            mensaje = "Tu contraseña fue actualizada correctamente. Ya puedes iniciar sesión."
        });
    }

    private async Task<UsuarioRecuperacionCorreo?> BuscarUsuarioParaRecuperacionCorreoAsync(string usuarioOCorreo)
    {
        usuarioOCorreo = (usuarioOCorreo ?? "").Trim();

        if (string.IsNullOrWhiteSpace(usuarioOCorreo))
            return null;

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        const string query = @"
            SELECT TOP 1
                u.UsuarioID,
                u.PersonaID,
                ISNULL(u.Username, '') AS Username,
                LTRIM(RTRIM(
                    ISNULL(p.Nombre, '') + ' ' +
                    ISNULL(p.ApellidoPaterno, '') + ' ' +
                    ISNULL(p.ApellidoMaterno, '')
                )) AS NombreCompleto,
                ISNULL(p.Correo, '') AS Correo
            FROM dbo.Usuarios u
            INNER JOIN dbo.Persona p 
                ON p.PersonaID = u.PersonaID
            WHERE u.Activo = 1
              AND (
                    UPPER(LTRIM(RTRIM(u.Username))) = UPPER(LTRIM(RTRIM(@Valor)))
                    OR UPPER(LTRIM(RTRIM(p.Correo))) = UPPER(LTRIM(RTRIM(@Valor)))
              );";

        using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@Valor", usuarioOCorreo);

        using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return null;

        return new UsuarioRecuperacionCorreo
        {
            UsuarioID = Convert.ToInt32(reader["UsuarioID"]),
            PersonaID = Convert.ToInt32(reader["PersonaID"]),
            Username = reader["Username"]?.ToString() ?? "",
            NombreCompleto = reader["NombreCompleto"]?.ToString() ?? "",
            Correo = reader["Correo"]?.ToString() ?? ""
        };
    }

    private static string GenerarCodigoRecuperacion()
    {
        using var rng = RandomNumberGenerator.Create();

        var bytes = new byte[4];
        rng.GetBytes(bytes);

        var valor = BitConverter.ToUInt32(bytes, 0);
        var codigo = (valor % 900000) + 100000;

        return codigo.ToString();
    }

    private static string CalcularHashCodigo(string codigo)
    {
        codigo = (codigo ?? "").Trim();

        using var sha = SHA256.Create();

        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(codigo));

        return BitConverter.ToString(bytes).Replace("-", "");
    }

    private async Task GuardarCodigoRecuperacionAsync(int usuarioId, int personaId, string codigo)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        using var transaction = connection.BeginTransaction();

        try
        {
            const string invalidarAnteriores = @"
                UPDATE dbo.PasswordRecoveryCodes
                SET Usado = 1,
                    FechaUso = SYSDATETIME()
                WHERE UsuarioID = @UsuarioID
                  AND Usado = 0;";

            using (var command = new SqlCommand(invalidarAnteriores, connection, transaction))
            {
                command.Parameters.AddWithValue("@UsuarioID", usuarioId);
                await command.ExecuteNonQueryAsync();
            }

            const string insertar = @"
                INSERT INTO dbo.PasswordRecoveryCodes
                (
                    UsuarioID,
                    PersonaID,
                    CodigoHash,
                    FechaExpiracion,
                    IPRegistro,
                    UserAgent
                )
                VALUES
                (
                    @UsuarioID,
                    @PersonaID,
                    @CodigoHash,
                    DATEADD(MINUTE, 10, SYSDATETIME()),
                    @IPRegistro,
                    @UserAgent
                );";

            using (var command = new SqlCommand(insertar, connection, transaction))
            {
                command.Parameters.AddWithValue("@UsuarioID", usuarioId);
                command.Parameters.AddWithValue("@PersonaID", personaId);
                command.Parameters.AddWithValue("@CodigoHash", CalcularHashCodigo(codigo));
                command.Parameters.AddWithValue("@IPRegistro", HttpContext.Connection.RemoteIpAddress?.ToString() ?? "");
                command.Parameters.AddWithValue("@UserAgent", Request.Headers.UserAgent.ToString());

                await command.ExecuteNonQueryAsync();
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private async Task<bool> ValidarCodigoRecuperacionAsync(
        string usuarioOCorreo,
        string codigo,
        bool registrarIntentoFallido)
    {
        var usuario = await BuscarUsuarioParaRecuperacionCorreoAsync(usuarioOCorreo);

        if (usuario == null)
            return false;

        var codigoActivo = await ObtenerCodigoRecuperacionActivoAsync(usuario.UsuarioID);

        if (codigoActivo == null)
            return false;

        var coincide = codigoActivo.CodigoHash == CalcularHashCodigo(codigo);

        if (!coincide && registrarIntentoFallido)
        {
            await RegistrarIntentoFallidoCodigoAsync(codigoActivo);
        }

        return coincide;
    }

    private async Task<CodigoRecuperacionActivo?> ObtenerCodigoRecuperacionActivoAsync(int usuarioId)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        const string query = @"
            SELECT TOP 1
                PasswordRecoveryCodeID,
                UsuarioID,
                CodigoHash,
                Intentos
            FROM dbo.PasswordRecoveryCodes
            WHERE UsuarioID = @UsuarioID
              AND Usado = 0
              AND Bloqueado = 0
              AND FechaExpiracion > SYSDATETIME()
            ORDER BY PasswordRecoveryCodeID DESC;";

        using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@UsuarioID", usuarioId);

        using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return null;

        return new CodigoRecuperacionActivo
        {
            PasswordRecoveryCodeID = Convert.ToInt32(reader["PasswordRecoveryCodeID"]),
            UsuarioID = Convert.ToInt32(reader["UsuarioID"]),
            CodigoHash = reader["CodigoHash"]?.ToString() ?? "",
            Intentos = Convert.ToInt32(reader["Intentos"])
        };
    }

    private async Task RegistrarIntentoFallidoCodigoAsync(CodigoRecuperacionActivo? codigoActivo)
    {
        if (codigoActivo == null)
            return;

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        const string query = @"
            UPDATE dbo.PasswordRecoveryCodes
            SET Intentos = Intentos + 1,
                Bloqueado = CASE WHEN Intentos + 1 >= 5 THEN 1 ELSE Bloqueado END
            WHERE PasswordRecoveryCodeID = @PasswordRecoveryCodeID
              AND Usado = 0;";

        using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@PasswordRecoveryCodeID", codigoActivo.PasswordRecoveryCodeID);

        await command.ExecuteNonQueryAsync();
    }

    private async Task<bool> ActualizarPasswordRecuperacionAsync(
        int passwordRecoveryCodeId,
        int usuarioId,
        string nuevaPassword)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        using var transaction = connection.BeginTransaction();

        try
        {
            const string actualizarUsuario = @"
                UPDATE dbo.Usuarios
                SET Contrasena = @NuevaPassword,
                    DebeCambiarPassword = 0,
                    FechaUltimoCambioPassword = GETDATE()
                WHERE UsuarioID = @UsuarioID
                  AND Activo = 1;";

            int filasUsuario;

            using (var command = new SqlCommand(actualizarUsuario, connection, transaction))
            {
                command.Parameters.AddWithValue("@NuevaPassword", nuevaPassword);
                command.Parameters.AddWithValue("@UsuarioID", usuarioId);

                filasUsuario = await command.ExecuteNonQueryAsync();
            }

            if (filasUsuario <= 0)
            {
                transaction.Rollback();
                return false;
            }

            const string marcarCodigoUsado = @"
                UPDATE dbo.PasswordRecoveryCodes
                SET Usado = 1,
                    FechaUso = SYSDATETIME()
                WHERE PasswordRecoveryCodeID = @PasswordRecoveryCodeID
                  AND UsuarioID = @UsuarioID
                  AND Usado = 0;";

            int filasCodigo;

            using (var command = new SqlCommand(marcarCodigoUsado, connection, transaction))
            {
                command.Parameters.AddWithValue("@PasswordRecoveryCodeID", passwordRecoveryCodeId);
                command.Parameters.AddWithValue("@UsuarioID", usuarioId);

                filasCodigo = await command.ExecuteNonQueryAsync();
            }

            if (filasCodigo <= 0)
            {
                transaction.Rollback();
                return false;
            }

            transaction.Commit();
            return true;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    // ---------- OBTENER ROL ID POR USUARIO ----------
    private async Task<int> ObtenerRolIdPorUsuarioAsync(int usuarioId)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        string query = @"
            SELECT TOP 1 u.RolID 
            FROM dbo.Usuarios u 
            WHERE u.UsuarioID = @UsuarioID
              AND u.Activo = 1;";

        using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@UsuarioID", usuarioId);

        var result = await command.ExecuteScalarAsync();

        return result != null && result != DBNull.Value
            ? Convert.ToInt32(result)
            : 0;
    }

    // ---------- OBTENER NOMBRE COMPLETO ----------
    private async Task<string?> ObtenerNombreMostrarPorUsuarioAsync(int usuarioId)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        string query = @"
            SELECT TOP 1 
                p.Nombre,
                p.ApellidoPaterno,
                p.ApellidoMaterno
            FROM dbo.Usuarios u
            INNER JOIN dbo.Persona p 
                ON u.PersonaID = p.PersonaID
            WHERE u.UsuarioID = @UsuarioID
              AND u.Activo = 1;";

        using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@UsuarioID", usuarioId);

        using var reader = await command.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            var nombre = reader["Nombre"] as string ?? "";
            var apePat = reader["ApellidoPaterno"] as string ?? "";
            var apeMat = reader["ApellidoMaterno"] as string ?? "";

            var nombreMostrar = $"{nombre} {apePat} {apeMat}".Trim();

            if (!string.IsNullOrWhiteSpace(nombreMostrar))
                return nombreMostrar;
        }

        return null;
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult VerificarSesion()
    {
        int? usuarioID = HttpContext.Session.GetInt32("UsuarioID");

        if (usuarioID == null)
            return Json(new { sesionActiva = false });

        return Json(new { sesionActiva = true });
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult AccesoDenegado(string? returnUrl = null)
    {
        ViewBag.ReturnUrl = returnUrl;
        return View();
    }

    [AllowAnonymous]
    public IActionResult Index()
    {
        if (HttpContext.Session.GetInt32("UsuarioID") != null)
        {
            return RedirectToAction("Index", "Menu");
        }

        return RedirectToAction("Login");
    }
}