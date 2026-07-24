using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.NSQuell.Models
{
    [Table("Calidad_InspeccionHistorial")]
    public class CalidadInspeccionHistorial
    {
        [Key]
        public int HistorialID { get; set; }

        public int InspeccionID { get; set; }

        [Required]
        [StringLength(80)]
        public string Movimiento { get; set; } = string.Empty;

        [StringLength(50)]
        public string? EstadoAnterior { get; set; }

        [StringLength(50)]
        public string? EstadoNuevo { get; set; }

        [StringLength(30)]
        public string? ResultadoCalidad { get; set; }

        [StringLength(30)]
        public string? Etiqueta { get; set; }

        [StringLength(1000)]
        public string? Comentario { get; set; }

        public int? UsuarioID { get; set; }

        public DateTime FechaMovimiento { get; set; }

        [ForeignKey(nameof(InspeccionID))]
        public CalidadInspeccion? Inspeccion { get; set; }
    }
}