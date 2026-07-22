using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ERP.NSQuell.Models.ViewModels
{
    public class MaterialIndexViewModel
    {
        public string? Busqueda { get; set; }

        public string? EstadoFiltro { get; set; }

        public int TotalMostrados { get; set; }

        public int TotalActivos { get; set; }

        public int TotalInactivos { get; set; }

        public int TotalStockConfigurado { get; set; }

        public int TotalConCosto { get; set; }

        public List<MaterialListadoItemViewModel> Materiales { get; set; } = new();
    }

    public class MaterialListadoItemViewModel
    {
        public int MaterialID { get; set; }

        public string Codigo { get; set; } = string.Empty;

        public string Nombre { get; set; } = string.Empty;

        public string? TipoMaterial { get; set; }

        public string UnidadDefault { get; set; } = string.Empty;

        public string? Proveedor { get; set; }

        public bool RequiereLote { get; set; }

        public bool Activo { get; set; }

        public decimal StockMinimo { get; set; }

        public decimal StockAviso { get; set; }

        public bool StockConfigurado { get; set; }

        public decimal? CostoUnitario { get; set; }

        public string? MonedaCosto { get; set; }

        public string? UnidadCosto { get; set; }

        public string? FuenteCosto { get; set; }

        public DateTime? FechaCosto { get; set; }
    }

    public class MaterialFormViewModel
    {
        public int? MaterialID { get; set; }

        [Required(ErrorMessage = "El código del material es obligatorio.")]
        [StringLength(80, ErrorMessage = "El código no puede superar los 80 caracteres.")]
        public string Codigo { get; set; } = string.Empty;

        [Required(ErrorMessage = "El nombre del material es obligatorio.")]
        [StringLength(250, ErrorMessage = "El nombre no puede superar los 250 caracteres.")]
        public string Nombre { get; set; } = string.Empty;

        [StringLength(80, ErrorMessage = "El tipo de material no puede superar los 80 caracteres.")]
        public string? TipoMaterial { get; set; }

        [Required(ErrorMessage = "La unidad default es obligatoria.")]
        [StringLength(20, ErrorMessage = "La unidad default no puede superar los 20 caracteres.")]
        public string UnidadDefault { get; set; } = string.Empty;

        [StringLength(200, ErrorMessage = "El proveedor no puede superar los 200 caracteres.")]
        public string? Proveedor { get; set; }

        public bool RequiereLote { get; set; }

        public bool Activo { get; set; } = true;

        public decimal StockMinimo { get; set; }

        public decimal StockAviso { get; set; }

        public bool StockConfigurado { get; set; }

        public decimal? CostoUnitario { get; set; }

        [StringLength(30, ErrorMessage = "La moneda no puede superar los 30 caracteres.")]
        public string? MonedaCosto { get; set; }

        [StringLength(30, ErrorMessage = "La unidad de costo no puede superar los 30 caracteres.")]
        public string? UnidadCosto { get; set; }

        [StringLength(120, ErrorMessage = "La fuente de costo no puede superar los 120 caracteres.")]
        public string? FuenteCosto { get; set; }

        [StringLength(120, ErrorMessage = "La clave de origen no puede superar los 120 caracteres.")]
        public string? ClaveCostoOrigen { get; set; }

        [StringLength(500, ErrorMessage = "La descripción del origen no puede superar los 500 caracteres.")]
        public string? DescripcionCostoOrigen { get; set; }

        public DateTime? FechaCosto { get; set; }

        public bool EsModoCrear => !MaterialID.HasValue || MaterialID.Value <= 0;
    }
}