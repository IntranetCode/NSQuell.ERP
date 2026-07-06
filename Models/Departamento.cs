using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.NSQuell.Models
{
    [Table("Departamentos")]
    public class Departamento
    {
        [Key]
        public int DepartamentoID { get; set; }

        public string NombreDepartamento { get; set; } = string.Empty;

        public bool Activo { get; set; }
    }
}
