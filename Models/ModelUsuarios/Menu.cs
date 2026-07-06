using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.NSQuell.Models.ModelUsuarios 
{
    [Table("Menus")]
    public class Menu
    {
        [Key]
        public int MenuID { get; set; }

        public string Nombre { get; set; }

        public virtual ICollection<SubMenu> SubMenus { get; set; }
    }
}