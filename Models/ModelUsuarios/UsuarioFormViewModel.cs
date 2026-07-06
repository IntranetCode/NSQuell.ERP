using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ERP.NSQuell.Models.ModelUsuarios
{
    // AÑADIDO: Clases para la jerarquía de menús
    public class SubMenuViewModel
    {
        public int SubMenuID { get; set; }
        public string Nombre { get; set; }
        public string UrlEnlace { get; set; }
    }

    public class MenuViewModel
    {
        public int MenuID { get; set; }
        public string Nombre { get; set; }
        public List<SubMenuViewModel> SubMenus { get; set; } = new();
    }
    // FIN AÑADIDO

    public class UsuarioFormViewModel : IValidatableObject
    {
        [ValidateNever]

        public IEnumerable<AuditoriaUsuario> HistorialDeCambios { get; set; } = new List<AuditoriaUsuario>();
        public int? UsuarioID { get; set; }

        [Required(ErrorMessage = "El nombre es obligatorio.")]
        public string Nombre { get; set; }

        [Required(ErrorMessage = "El apellido paterno es obligatorio.")]
        public string ApellidoPaterno { get; set; }

        public string? ApellidoMaterno { get; set; }

        [EmailAddress(ErrorMessage = "El formato del correo no es válido.")]
        [Remote(action: "VerificarCorreo", controller: "Usuarios", AdditionalFields = nameof(UsuarioID), ErrorMessage = "Este correo electrónico ya está en uso.")]
        public string? Correo { get; set; }

        public string? Telefono { get; set; }

        [Remote(action: "VerificarUsername", controller: "Usuarios", AdditionalFields = nameof(UsuarioID), ErrorMessage = "Este nombre de usuario ya está en uso.")]
        public string? Username { get; set; }

        public string? Password { get; set; }

        [Required(ErrorMessage = "El rol es obligatorio.")]
        public int RolID { get; set; }

        public bool Activo { get; set; }

        [Required(ErrorMessage = "Debe seleccionar al menos una empresa.")]
        public List<int> EmpresasIDs { get; set; } = new List<int>();

        public bool EsModoCrear { get; set; }

        [ValidateNever]
        public List<int> SubMenuIDs { get; set; } = new List<int>();
        [ValidateNever]
        public List<MenuViewModel> MenusDisponibles { get; set; } = new List<MenuViewModel>();

        // Campos nuevos para solicitar en el form
        public string? NumeroEmpleado { get; set; }
        public string? ClaveEmpleadoNomina { get; set; }
        public DateTime? FechaIngreso { get; set; }
        public string? Puesto { get; set; }
        public DateTime? FechaNacimiento { get; set; }
        // En UsuarioFormViewModel.cs
        public int? JefeInmediatoPersonaID { get; set; }

        [ValidateNever]
        public string? JefeInmediatoNombreCompleto { get; set; }

       
        public int? DepartamentoID { get; set; }

        [ValidateNever]
        public string? NombreDepartamento { get; set; }


        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (EsModoCrear)
            {
                if (string.IsNullOrWhiteSpace(Username))
                {
                    yield return new ValidationResult("El nombre de usuario es obligatorio.", new[] { nameof(Username) });
                }
                if (string.IsNullOrWhiteSpace(Password))
                {
                    yield return new ValidationResult("La contraseña es obligatoria.", new[] { nameof(Password) });
                }
            }
        }

        public bool PermiteAdelantadas { get; set; }

    } 
}