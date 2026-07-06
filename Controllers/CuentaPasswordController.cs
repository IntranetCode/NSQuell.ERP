using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using ERP.NSQuell.Models.ModelUsuarios;
using System;
using Microsoft.Data.SqlClient;
using System.Threading.Tasks;

namespace ERP.NSQuell.Controllers
{
    public class CuentaPasswordController : Controller
    {
        private readonly string _connectionString;

        private const string SessionUsuarioPendienteCambioPasswordID = "UsuarioPendienteCambioPasswordID";
        private const string SessionUsuarioCambioPasswordID = "UsuarioCambioPasswordID";
        private const string SessionUsernameCambioPassword = "UsernameCambioPassword";

        public CuentaPasswordController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        [HttpGet]
        public async Task<IActionResult> CambiarPasswordInicial()
        {
            var usuarioId = ObtenerUsuarioPendienteCambioPasswordId();

            if (!usuarioId.HasValue || usuarioId.Value <= 0)
            {
                return RedirectToAction("Login", "Login");
            }

            NormalizarSesionCambioPassword(usuarioId.Value);

            var debeCambiar = await DebeCambiarPasswordAsync(usuarioId.Value);

            if (!debeCambiar)
            {
                LimpiarSesionCambioPassword();
                return RedirectToAction("Login", "Login");
            }

            return View(new CambioPasswordInicialViewModel
            {
                UsuarioID = usuarioId.Value
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CambiarPasswordInicial(CambioPasswordInicialViewModel viewModel)
        {
            var usuarioIdSesion = ObtenerUsuarioPendienteCambioPasswordId();

            if (!usuarioIdSesion.HasValue || usuarioIdSesion.Value <= 0 || usuarioIdSesion.Value != viewModel.UsuarioID)
            {
                return RedirectToAction("Login", "Login");
            }

            NormalizarSesionCambioPassword(usuarioIdSesion.Value);

            if (!ModelState.IsValid)
            {
                return View(viewModel);
            }

            var passwordActual = await ObtenerPasswordActualAsync(viewModel.UsuarioID);

            if (passwordActual == null)
            {
                HttpContext.Session.Clear();
                return RedirectToAction("Login", "Login");
            }

            if (string.Equals(passwordActual, viewModel.NuevaPassword, StringComparison.Ordinal))
            {
                ModelState.AddModelError(nameof(viewModel.NuevaPassword), "La nueva contraseña debe ser diferente a la contraseña temporal.");
                return View(viewModel);
            }

            await ActualizarPasswordInicialAsync(viewModel.UsuarioID, viewModel.NuevaPassword);

            HttpContext.Session.Clear();

            TempData["SuccessMessage"] = "Contraseña actualizada correctamente. Ingresa nuevamente con tu nueva contraseña.";

            return RedirectToAction("Login", "Login");
        }

        private int? ObtenerUsuarioPendienteCambioPasswordId()
        {
            var usuarioPendiente = HttpContext.Session.GetInt32(SessionUsuarioPendienteCambioPasswordID);

            if (usuarioPendiente.HasValue && usuarioPendiente.Value > 0)
            {
                return usuarioPendiente.Value;
            }

            var usuarioCambio = HttpContext.Session.GetInt32(SessionUsuarioCambioPasswordID);

            if (usuarioCambio.HasValue && usuarioCambio.Value > 0)
            {
                return usuarioCambio.Value;
            }

            return null;
        }

        private void NormalizarSesionCambioPassword(int usuarioId)
        {
            HttpContext.Session.SetInt32(SessionUsuarioPendienteCambioPasswordID, usuarioId);
            HttpContext.Session.SetInt32(SessionUsuarioCambioPasswordID, usuarioId);
        }

        private void LimpiarSesionCambioPassword()
        {
            HttpContext.Session.Remove(SessionUsuarioPendienteCambioPasswordID);
            HttpContext.Session.Remove(SessionUsuarioCambioPasswordID);
            HttpContext.Session.Remove(SessionUsernameCambioPassword);
        }

        private async Task<bool> DebeCambiarPasswordAsync(int usuarioId)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = @"
                SELECT TOP 1
                    CASE
                        WHEN ISNULL(DebeCambiarPassword, 0) = 1 THEN 1
                        WHEN FechaUltimoCambioPassword IS NULL THEN 1
                        WHEN FechaUltimoCambioPassword <= DATEADD(MONTH, -2, GETDATE()) THEN 1
                        ELSE 0
                    END AS DebeCambiar
                FROM Usuarios
                WHERE UsuarioID = @UsuarioID
                  AND Activo = 1;";

            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@UsuarioID", usuarioId);

            var result = await command.ExecuteScalarAsync();

            return result != null && result != DBNull.Value && Convert.ToBoolean(result);
        }

        private async Task<string?> ObtenerPasswordActualAsync(int usuarioId)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = @"
                SELECT TOP 1 Contrasena
                FROM Usuarios
                WHERE UsuarioID = @UsuarioID
                  AND Activo = 1;";

            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@UsuarioID", usuarioId);

            var result = await command.ExecuteScalarAsync();

            return result == null || result == DBNull.Value
                ? null
                : Convert.ToString(result);
        }

        private async Task ActualizarPasswordInicialAsync(int usuarioId, string nuevaPassword)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = @"
                UPDATE Usuarios
                SET Contrasena = @NuevaPassword,
                    DebeCambiarPassword = 0,
                    FechaUltimoCambioPassword = GETDATE()
                WHERE UsuarioID = @UsuarioID
                  AND Activo = 1;";

            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@UsuarioID", usuarioId);
            command.Parameters.AddWithValue("@NuevaPassword", nuevaPassword);

            await command.ExecuteNonQueryAsync();
        }
    }
}