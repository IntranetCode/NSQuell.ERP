using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ERP.NSQuell.Models.ViewModels
{
    public class ClienteIndexViewModel
    {
        public string? Busqueda { get; set; }

        public string? EstadoFiltro { get; set; }

        public int TotalMostrados { get; set; }

        public int TotalActivos { get; set; }

        public int TotalInactivos { get; set; }

        public List<ClienteListadoItemViewModel> Clientes { get; set; } = new();
    }

    public class ClienteListadoItemViewModel
    {
        public int ClienteID { get; set; }

        public string Codigo { get; set; } = string.Empty;

        public string Nombre { get; set; } = string.Empty;

        public string? RFC { get; set; }

        public string? Contacto { get; set; }

        public bool Activo { get; set; }
    }

    public class ClienteFormViewModel
    {
        public int? ClienteID { get; set; }

        [Required(ErrorMessage = "El código del cliente es obligatorio.")]
        [StringLength(100, ErrorMessage = "El código no puede superar los 100 caracteres.")]
        public string Codigo { get; set; } = string.Empty;

        [Required(ErrorMessage = "El nombre del cliente es obligatorio.")]
        [StringLength(200, ErrorMessage = "El nombre no puede superar los 200 caracteres.")]
        public string Nombre { get; set; } = string.Empty;

        [StringLength(50, ErrorMessage = "El RFC no puede superar los 50 caracteres.")]
        public string? RFC { get; set; }

        [StringLength(150, ErrorMessage = "El contacto no puede superar los 150 caracteres.")]
        public string? Contacto { get; set; }

        public bool Activo { get; set; } = true;

        public bool EsModoCrear => !ClienteID.HasValue || ClienteID.Value <= 0;
    }
}