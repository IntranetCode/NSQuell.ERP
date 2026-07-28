using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.NSQuell.Models
{
    [Table("Calidad_Inspecciones")]
    public class CalidadInspeccion
    {
        [Key]
        public int InspeccionID { get; set; }

        [StringLength(150)]
        public string? CodigoBarras { get; set; }

        [StringLength(120)]
        public string? OrdenTrabajo { get; set; }

        [StringLength(120)]
        public string? NumeroParte { get; set; }

        [StringLength(250)]
        public string? Material { get; set; }

        [StringLength(200)]
        public string? Proceso { get; set; }

        [StringLength(150)]
        public string? Maquina { get; set; }

        [Column(TypeName = "decimal(18,3)")]
        public decimal CantidadTotal { get; set; }

        [Column(TypeName = "decimal(18,3)")]
        public decimal CantidadRevisada { get; set; }

        [Column(TypeName = "decimal(18,3)")]
        public decimal CantidadPendiente { get; set; }

        public bool ChecklistValidado { get; set; }

        public bool HojaInspeccionProducto { get; set; }

        public bool HojaValidacionCalidad { get; set; }

        [StringLength(30)]
        public string? ResultadoCalidad { get; set; }

        [StringLength(30)]
        public string? Etiqueta { get; set; }

        public bool Liberado { get; set; }

        public bool RequiereGPI2 { get; set; }

        public bool EnContencion { get; set; }

        public bool EsScrap { get; set; }

        [StringLength(1000)]
        public string? Observaciones { get; set; }

        [Required]
        [StringLength(50)]
        public string Estado { get; set; } = "ABIERTA";

        public int? UsuarioCreacionID { get; set; }

        public DateTime FechaCreacion { get; set; }

        public int? UsuarioModificacionID { get; set; }

        public DateTime? FechaModificacion { get; set; }

        public ICollection<CalidadInspeccionHistorial> Historial { get; set; } = new List<CalidadInspeccionHistorial>();
    }
}