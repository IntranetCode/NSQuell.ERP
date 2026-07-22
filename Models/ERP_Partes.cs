using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.NSQuell.Models
{
    [Table("ERP_Partes")]
    public class ERPParte
    {
        [Key]
        public int ParteID { get; set; }

        public int ClienteID { get; set; }

        [Required]
        [StringLength(120)]
        public string NumeroParte { get; set; } = string.Empty;

        [StringLength(120)]
        public string? ReferenciaSAP { get; set; }

        [Required]
        [StringLength(300)]
        public string Descripcion { get; set; } = string.Empty;

        [StringLength(200)]
        public string? Designacion { get; set; }

        public bool RequiereGP12 { get; set; }

        public bool RequiereCertificado { get; set; }

        [StringLength(500)]
        public string? Notas { get; set; }

        public bool Activo { get; set; } = true;

        public int? UsuarioCreacionID { get; set; }

        public DateTime FechaCreacion { get; set; }

        public int? UsuarioModificacionID { get; set; }

        public DateTime? FechaModificacion { get; set; }

        public int StockMinimo { get; set; }

        public int StockAviso { get; set; }

        public bool StockConfigurado { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal? PrecioVentaUnitario { get; set; }

        [StringLength(10)]
        public string? MonedaPrecioVenta { get; set; }

        [StringLength(50)]
        public string? UnidadPrecioVenta { get; set; }

        [StringLength(50)]
        public string? FuentePrecioVenta { get; set; }

        [StringLength(100)]
        public string? ClavePrecioVentaOrigen { get; set; }

        [StringLength(250)]
        public string? DescripcionPrecioVentaOrigen { get; set; }

        public DateTime? FechaPrecioVenta { get; set; }
    }
}