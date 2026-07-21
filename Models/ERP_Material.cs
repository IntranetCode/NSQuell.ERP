using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.NSQuell.Models
{
    [Table("ERP_Materiales")]
    public class ERPMaterial
    {
        [Key]
        public int MaterialID { get; set; }

        [Required]
        [StringLength(80)]
        public string Codigo { get; set; } = string.Empty;

        [Required]
        [StringLength(250)]
        public string Nombre { get; set; } = string.Empty;

        [StringLength(80)]
        public string? TipoMaterial { get; set; }

        [Required]
        [StringLength(20)]
        public string UnidadDefault { get; set; } = string.Empty;

        [StringLength(200)]
        public string? Proveedor { get; set; }

        public bool RequiereLote { get; set; }

        public DateTime FechaCreacion { get; set; }

        [StringLength(120)]
        public string? CreadoPor { get; set; }

        public DateTime? FechaModificacion { get; set; }

        [StringLength(120)]
        public string? ActualizadoPor { get; set; }

        public bool Activo { get; set; }

        [Column(TypeName = "decimal(18,3)")]
        public decimal StockMinimo { get; set; }

        [Column(TypeName = "decimal(18,3)")]
        public decimal StockAviso { get; set; }

        public bool StockConfigurado { get; set; }

        [Column(TypeName = "decimal(18,6)")]
        public decimal? CostoUnitario { get; set; }

        [StringLength(30)]
        public string? MonedaCosto { get; set; }

        [StringLength(30)]
        public string? UnidadCosto { get; set; }

        [StringLength(120)]
        public string? FuenteCosto { get; set; }

        [StringLength(120)]
        public string? ClaveCostoOrigen { get; set; }

        [StringLength(500)]
        public string? DescripcionCostoOrigen { get; set; }

        public DateTime? FechaCosto { get; set; }
    }
}