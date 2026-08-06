using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.NSQuell.Models
{
    [Table("ERP_MaquinasSustitutas")]
    public class ERPMaquinaSustituta
    {
        [Key]
        public int MaquinaSustitutaRelacionID { get; set; }

        public int MaquinaPrincipalID { get; set; }

        public int MaquinaSustitutaID { get; set; }

        public int? Prioridad { get; set; }

        [StringLength(500)]
        public string? Observaciones { get; set; }

        public bool Activo { get; set; } = true;

        public DateTime FechaCreacion { get; set; }

        public int? UsuarioCreacionID { get; set; }

        [ForeignKey(nameof(MaquinaPrincipalID))]
        public ERPMaquina? MaquinaPrincipal { get; set; }

        [ForeignKey(nameof(MaquinaSustitutaID))]
        public ERPMaquina? MaquinaSustituta { get; set; }
    }
}