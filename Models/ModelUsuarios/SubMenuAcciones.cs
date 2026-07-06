using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.NSQuell.Models.ModelUsuarios
{
    [Table("SubMenuAcciones")]
    public class SubMenuAcciones
    {
        [Key]
        public int SubMenuAccionID { get; set; }

        public int SubMenuID { get; set; }

        public int AccionID { get; set; }

        public bool Activo { get; set; }
    }
}