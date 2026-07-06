using ERP.NSQuell.Areas.AdminUsuarios.DTOs;
using ERP.NSQuell.Models;
using ERP.NSQuell.Models.ModelUsuarios;

namespace ERP.NSQuell.Areas.AdminUsuarios.Interfaces
{
    // Este archivo define TODAS las acciones que nuestro servicio de usuarios DEBE poder hacer.
    // Es como el "contrato" que debe cumplir.
    public interface IUsuarioService
    {

        // Tarea 1: Obtener la lista de todos los usuarios para mostrarla en la tabla.

        // Cambia la firma del método
        Task<IEnumerable<V_InformacionUsuarioCompleta>> ObtenerTodosAsync(bool? activos, string? filtroCampo, string? terminoBusqueda);

        // Tarea 2: Registrar un usuario nuevo en la base de datos.
        Task RegistrarAsync(UsuarioRegistroDTO nuevoUsuario);

        // Tarea 3: Dar de baja (desactivar) a un usuario.
        Task DarDeBajaAsync(int usuarioId);

        // Tarea 4: Obtener los datos de un único usuario para poder cargarlos en el formulario de edición.
        Task<UsuarioEdicionDTO?> ObtenerParaEditarAsync(int usuarioId);

        // Tarea 5: Guardar los cambios de un usuario que fue editado.
        Task<IEnumerable<AuditoriaUsuario>> ObtenerHistorialAsync(int usuarioId);
        Task ActualizarAsync(UsuarioEdicionDTO usuarioEditado);
        Task<bool> TienePermisoAsync(int usuarioId, string nombreAccion);

        Task<List<MenuViewModel>> ObtenerMenusConSubMenusAsync();
        Task<bool> VerificarPermisoAsync(int usuarioId, int subMenuId);

        Task<bool> VerificarPermisoParaMenuAsync(int usuarioId, int menuId);



        //Agregando metodos para los overrides

        Task<List<OverrideItemDto>> ListarOverridesAsync(int usuarioId, int? empresaId);
        Task GuardarOverridesAsync(int usuarioId, int? empresaId, IEnumerable<OverrideItemDto> items);

        Task<bool> VerificarPermisoParaMenuAsync(int usuarioId, int? empresaId, int menuId); // NUEVA

    }
}

