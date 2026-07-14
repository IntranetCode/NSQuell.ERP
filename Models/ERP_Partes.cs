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

        [Required]
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

        [StringLength(80)]
        public string? Color { get; set; }

        public int? Cavidades { get; set; }

        public int? ObjetivoHora { get; set; }

        public int? PiezasPorCaja { get; set; }

        public bool RequiereGP12 { get; set; }

        public bool RequiereCertificado { get; set; }

        public int? MaquinaPrincipalID { get; set; }

        public int? MaquinaSustitutaID { get; set; }

        public int? MoldePrincipalID { get; set; }

        [StringLength(100)]
        public string? MaterialCodigo { get; set; }

        [StringLength(250)]
        public string? MaterialDescripcion { get; set; }

        [StringLength(100)]
        public string? TipoSecado { get; set; }

        [Column(TypeName = "decimal(10,2)")]
        public decimal? HorasSecado { get; set; }

        [Column(TypeName = "decimal(18,6)")]
        public decimal? PesoBrutoPieza { get; set; }

        [StringLength(100)]
        public string? EmbalajeCodigo { get; set; }

        [StringLength(250)]
        public string? EmbalajeDescripcion { get; set; }

        public int? PiezasPorEmbalaje { get; set; }

        [StringLength(500)]
        public string? Notas { get; set; }

        public bool Activo { get; set; } = true;

        public int? UsuarioCreacionID { get; set; }

        public DateTime FechaCreacion { get; set; }

        public int? UsuarioModificacionID { get; set; }

        public DateTime? FechaModificacion { get; set; }

        public int StockMinimo { get; set; }

        public int StockAviso { get; set; }

        public int? MaterialID { get; set; }

        public bool StockConfigurado { get; set; }
    }
}