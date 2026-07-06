using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ERP.NSQuell.Models;
using ERP.NSQuell.Models.ViewModels;

namespace ERP.NSQuell.Controllers
{
    public class PerfilController : Controller
    {
        private readonly ApplicationDbContext _context;

        public PerfilController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> ObtenerMiPerfil()
        {
            try
            {
                // Obtener el UsuarioID de la sesión
                int? usuarioID = HttpContext.Session.GetInt32("UsuarioID");

                if (usuarioID == null)
                {
                    return Json(new { success = false, message = "Usuario no autenticado" });
                }

                // Ejecutar consulta SQL para SQL Server
                var perfil = await _context.Database
                    .SqlQueryRaw<Models.UsuarioPerfilViewModel>(@"
                        SELECT TOP 1
                          CONCAT(p.Nombre, ' ', p.ApellidoPaterno, ' ', p.ApellidoMaterno) AS NombreUsuario,
                          u.Username, 
                          p.Correo, 
                          p.Telefono, 
                          r.NombreRol, 
                          r.Descripcion AS DescripcionRol
                        FROM Persona p
                        LEFT JOIN Usuarios u ON p.PersonaID = u.PersonaID
                        LEFT JOIN Roles r ON r.RolID = u.RolID
                        WHERE u.UsuarioID = {0}
                    ", usuarioID)
                    .ToListAsync();

                if (perfil == null || !perfil.Any())
                {
                    return Json(new { success = false, message = "Perfil no encontrado" });
                }

                return Json(new { success = true, data = perfil.First() });
            }
            catch (Exception ex)
            {
                // Log del error para debugging
                Console.WriteLine($"Error en ObtenerMiPerfil: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");

                return Json(new
                {
                    success = false,
                    message = $"Error al obtener el perfil: {ex.Message}"
                });
            }
        }
    }
}