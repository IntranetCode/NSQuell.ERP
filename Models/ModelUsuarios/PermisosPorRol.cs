using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.NSQuell.Models.ModelUsuarios
{
    [Table("PermisosPorRol")]
    public class PermisosPorRol
    {
        [Key]
        public int PermisoRolID { get; set; }

        public int RolID { get; set; }

        public int SubMenuAccionID { get; set; }

        public bool Activo { get; set; }
    }
}