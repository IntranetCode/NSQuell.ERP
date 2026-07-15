using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.NSQuell.Models
{
    [Table("ERP_Clientes")]
    public class ERPCliente
    {
        [Key]
        public int ClienteID { get; set; }

        [Required]
        [StringLength(100)]
        public string Codigo { get; set; } = string.Empty;

        [Required]
        [StringLength(200)]
        public string Nombre { get; set; } = string.Empty;

        [StringLength(50)]
        public string? RFC { get; set; }

        [StringLength(150)]
        public string? Contacto { get; set; }

        public bool Activo { get; set; }

        public int? UsuarioCreacionID { get; set; }

        public DateTime FechaCreacion { get; set; }

        public int? UsuarioModificacionID { get; set; }

        public DateTime? FechaModificacion { get; set; }
    }
}