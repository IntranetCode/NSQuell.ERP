namespace ERP.NSQuell.Models.ViewModels
{
    public class UsuarioPerfilViewModel
    {
        public string NombreUsuario { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Correo { get; set; } = string.Empty;
        public string? Telefono { get; set; }
        public string NombreRol { get; set; } = string.Empty;
        public string? DescripcionRol { get; set; }
    }
}
