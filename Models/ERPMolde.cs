using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.NSQuell.Models
{
    [Table("ERP_Moldes")]
    public class ERPMolde
    {
        [Key]
        public int MoldeID { get; set; }

        [Required]
        [StringLength(100)]
        public string CodigoMolde { get; set; } = string.Empty;

        [StringLength(200)]
        public string? NombreMolde { get; set; }

        public int? Cavidades { get; set; }

        [Required]
        [StringLength(50)]
        public string EstadoOperativo { get; set; } = string.Empty;

        [StringLength(150)]
        public string? Ubicacion { get; set; }

        [StringLength(500)]
        public string? Notas { get; set; }

        public bool Activo { get; set; }

        public int? UsuarioCreacionID { get; set; }

        public DateTime FechaCreacion { get; set; }

        public int? UsuarioModificacionID { get; set; }

        public DateTime? FechaModificacion { get; set; }
    }
}