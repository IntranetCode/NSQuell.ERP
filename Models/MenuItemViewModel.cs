namespace ERP.NSQuell.Models
{

    public class MenuItemViewModel
    {
        /*
        public int MenuID { get; set; }
        public string NombreMenu { get; set; }
        public string Icono { get; set; }
        public string Url { get; set; }
        public List<SubmenuViewModel> Submenus { get; set; } = new();
        */
        public int MenuID { get; set; }
        public string Nombre { get; set; }
        public string? Icono { get; set; } // Nullable porque no lo devuelves en la consulta
        public string? Url { get; set; }
        public int Orden { get; set; }
        public int? MenuPadreID { get; set; }
        public List<MenuModel> SubMenus { get; set; } = new List<MenuModel>();
    }

    


}
