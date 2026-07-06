using System;

namespace ERP.NSQuell.Models
{
    // Este modelo representa los datos que vienen de tu Vista SQL.
    public class V_InformacionUsuarioCompleta
    {
        public int UsuarioID { get; set; }
        public bool Activo { get; set; }
        public DateTime FechaCreacion { get; set; }
        public int RolID { get; set; }

        public string? Username { get; set; }
        public string? Nombre { get; set; }
        public string? ApellidoPaterno { get; set; }
        public string? ApellidoMaterno { get; set; }
        public string? Correo { get; set; }
        public string? Telefono { get; set; }
        public string? Rol { get; set; }
    }
}

