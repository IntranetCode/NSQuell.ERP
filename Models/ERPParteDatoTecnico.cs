using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.NSQuell.Models
{
    [Table("ERP_ParteDatosTecnicos")]
    public class ERPParteDatoTecnico
    {
        [Key]
        public int ParteDatoTecnicoID { get; set; }

        public int ParteID { get; set; }

        [StringLength(80)]
        public string? Ciclo { get; set; }

        [StringLength(100)]
        public string? TipoSecado { get; set; }

        [Column(TypeName = "decimal(10,2)")]
        public decimal? HorasSecado { get; set; }

        [Column(TypeName = "decimal(18,6)")]
        public decimal? PesoBrutoPieza { get; set; }

        [Column(TypeName = "decimal(18,6)")]
        public decimal? PesoNetoPieza { get; set; }

        [StringLength(100)]
        public string? EmbalajeCodigo { get; set; }

        [StringLength(250)]
        public string? EmbalajeDescripcion { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal? PiezasPorEmbalaje { get; set; }

        [StringLength(100)]
        public string? MaterialCodigo { get; set; }

        [StringLength(250)]
        public string? MaterialDescripcion { get; set; }

        public int? MaterialID { get; set; }

        [StringLength(80)]
        public string? Color { get; set; }

        public int? Cavidades { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal? ObjetivoHora { get; set; }

        public int? PiezasPorCaja { get; set; }

        public int? MaquinaPrincipalID { get; set; }

        public int? MaquinaSustitutaID { get; set; }

        public int? MoldePrincipalID { get; set; }

        public bool Activo { get; set; } = true;

        public DateTime FechaCreacion { get; set; }

        public DateTime? FechaModificacion { get; set; }
<<<<<<< HEAD
=======

        public int? MaterialID { get; set; }

        [StringLength(80)]
        public string? Color { get; set; }

        public int? Cavidades { get; set; }

        public int? ObjetivoHora { get; set; }

        public int? PiezasPorCaja { get; set; }

        public int? MaquinaPrincipalID { get; set; }

        public int? MaquinaSustitutaID { get; set; }

        public int? MoldePrincipalID { get; set; }
>>>>>>> origin/Alex_modulo-altas
    }
}