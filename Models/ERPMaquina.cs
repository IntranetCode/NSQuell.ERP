using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.NSQuell.Models
{
    [Table("ERP_Maquinas")]
    public class ERPMaquina
    {
        [Key]
        public int MaquinaID { get; set; }

        [StringLength(50)]
        public string? Codigo { get; set; }

        [StringLength(100)]
        public string? Nombre { get; set; }

        [StringLength(100)]
        public string? Area { get; set; }

        [StringLength(50)]
        public string? EstadoOperativo { get; set; }

        [StringLength(300)]
        public string? Descripcion { get; set; }

        public bool Activo { get; set; } = true;

        public DateTime? FechaCreacion { get; set; }

        public DateTime? FechaModificacion { get; set; }
    }
}