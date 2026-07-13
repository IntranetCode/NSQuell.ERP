using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ERP.NSQuell.Areas.AdminUsuarios.DTOs
{
    public class UsuarioRegistroDTO
    {
        [Required(ErrorMessage = "El nombre es obligatorio.")]
        public string Nombre { get; set; } = string.Empty;

        [Required(ErrorMessage = "El apellido paterno es obligatorio.")]
        public string ApellidoPaterno { get; set; } = string.Empty;

        public string? ApellidoMaterno { get; set; }

        [EmailAddress(ErrorMessage = "El formato del correo no es válido.")]
        public string? Correo { get; set; }

        public string? Telefono { get; set; }

        public DateTime? FechaIngreso { get; set; }

        public string? Puesto { get; set; }

        [Required(ErrorMessage = "El nombre de usuario es obligatorio.")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "La contraseña es obligatoria.")]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "El rol es obligatorio.")]
        public int RolID { get; set; }

        public int? DepartamentoID { get; set; }

        public string? NombreDepartamento { get; set; }

        public List<int> SubMenuIDs { get; set; } = new List<int>();

    }
}