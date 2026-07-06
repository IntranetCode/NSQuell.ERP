using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.NSQuell.Models.ModelUsuarios // <-- Namespace corregido
{
    [Table("SubMenus")]
    public class SubMenu
    {
        [Key]
        public int SubMenuID { get; set; }

        public int MenuID { get; set; }

        public string Nombre { get; set; }

        public string UrlEnlace { get; set; }

        public string Descripcion { get; set; }

        public bool Activo { get; set; }

        [ForeignKey("MenuID")]
        public virtual Menu Menu { get; set; }
    }
}