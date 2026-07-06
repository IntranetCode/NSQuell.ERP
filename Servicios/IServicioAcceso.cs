// SE CREO ESTE SERVICIO PARA CONSULTAR QUE ACCIONES TIENE ASIGNADO UN USUARIO
namespace ERP.NSQuell.Servicios
{
    public interface IServicioAcceso
    {
        // Conveniencia: resuelve EmpresaID desde sesión/claims
        Task<bool> TienePermisoAsync(int usuarioId, string subMenu, string? accion = null);

        // Núcleo: si quieres pasar EmpresaID explícito
        Task<bool> TienePermisoAsync(int usuarioId, int? empresaId, string subMenu, string? accion = null);
    }
}
