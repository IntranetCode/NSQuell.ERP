using System.ComponentModel.DataAnnotations;

namespace ERP.NSQuell.Models.ModelUsuarios
{
    public class CambioPasswordInicialViewModel
    {
        public int UsuarioID { get; set; }

        [Required(ErrorMessage = "La nueva contraseña es obligatoria.")]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "La contraseña debe tener al menos 8 caracteres.")]
        [DataType(DataType.Password)]
        [Display(Name = "Nueva contraseña")]
        public string NuevaPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Confirma la nueva contraseña.")]
        [DataType(DataType.Password)]
        [Compare(nameof(NuevaPassword), ErrorMessage = "Las contraseñas no coinciden.")]
        [Display(Name = "Confirmar contraseña")]
        public string ConfirmarPassword { get; set; } = string.Empty;
    }
}