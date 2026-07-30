using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ERP.NSQuell.Models.ViewModels
{
    public class MaquinariaFormViewModel
    {
        public int? MaquinaID { get; set; }

        [Required(ErrorMessage = "El código de la máquina es obligatorio.")]
        [StringLength(50, ErrorMessage = "El código no puede exceder 50 caracteres.")]
        public string Codigo { get; set; } = string.Empty;

        [Required(ErrorMessage = "El nombre de la máquina es obligatorio.")]
        [StringLength(100, ErrorMessage = "El nombre no puede exceder 100 caracteres.")]
        public string Nombre { get; set; } = string.Empty;

        [Required(ErrorMessage = "El área es obligatoria.")]
        [StringLength(100, ErrorMessage = "El área no puede exceder 100 caracteres.")]
        public string Area { get; set; } = "Inyección";

        [Required(ErrorMessage = "El estado operativo es obligatorio.")]
        [StringLength(50, ErrorMessage = "El estado operativo no puede exceder 50 caracteres.")]
        public string EstadoOperativo { get; set; } = "Operativa";

        [StringLength(300, ErrorMessage = "La descripción no puede exceder 300 caracteres.")]
        public string? Descripcion { get; set; }

        public bool Activo { get; set; } = true;

        public DateTime? FechaCreacion { get; set; }

        public DateTime? FechaModificacion { get; set; }

        // NUEVO:
        // Aquí se guardan los IDs de las máquinas seleccionadas como sustitutas.
        public List<int> MaquinasSustitutasSeleccionadas { get; set; } = new();

        // NUEVO:
        // Aquí se carga la lista de máquinas disponibles para mostrar en la vista.
        public List<MaquinaSustitutaOpcionViewModel> MaquinasSustitutasDisponibles { get; set; } = new();
    }

    public class MaquinaSustitutaOpcionViewModel
    {
        public int MaquinaID { get; set; }

        public string Codigo { get; set; } = string.Empty;

        public string Nombre { get; set; } = string.Empty;

        public string? Area { get; set; }

        public string? EstadoOperativo { get; set; }

        public bool Seleccionada { get; set; }
    }
}