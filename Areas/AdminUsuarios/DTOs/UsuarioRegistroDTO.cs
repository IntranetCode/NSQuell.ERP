using System.ComponentModel.DataAnnotations;

namespace ERP.NSQuell.Areas.AdminUsuarios.DTOs
{
    public class UsuarioRegistroDTO
    {
        [Required(ErrorMessage = "El nombre es obligatorio.")]
        public string Nombre { get; set; }

        [Required(ErrorMessage = "El apellido paterno es obligatorio.")]
        public string ApellidoPaterno { get; set; }

        public string? ApellidoMaterno { get; set; }

        [Required(ErrorMessage = "El correo electrónico es obligatorio.")]
        [EmailAddress(ErrorMessage = "El formato del correo no es válido.")]
        public string Correo { get; set; }

        public string? Telefono { get; set; }

        public string? Direccion { get; set; }

        public DateTime? FechaNacimiento { get; set; }

        [Required(ErrorMessage = "El nombre de usuario es obligatorio.")]
        public string Username { get; set; }

        [Required(ErrorMessage = "La contraseña es obligatoria.")]
        public string Password { get; set; }

        [Required(ErrorMessage = "El rol es obligatorio.")]
        public int RolID { get; set; }

        [Required(ErrorMessage = "Debe seleccionar al menos una empresa.")]
        public List<int> EmpresasIDs { get; set; } = new List<int>();

        // AÑADIDO
        public List<int> SubMenuIDs { get; set; } = new List<int>();

        public string? NumeroEmpleado { get; set; }
        public string? ClaveEmpleadoNomina { get; set; }
        public DateTime? FechaIngreso { get; set; }
        public string? Puesto { get; set; }

        public int? JefeInmediatoPersonaID { get; set; }

        public int? DepartamentoID { get; set; }
        public string? NombreDepartamento { get; set; }
        public string? JefeInmediatoNombreCompleto { get; set; }

    }
}