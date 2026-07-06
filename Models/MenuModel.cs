namespace ERP.NSQuell.Models
{
    public class MenuModel
    {
        public int MenuID { get; set; }
        public string Nombre { get; set; } = "";
        public string Url { get; set; } = "#";
        public string Icono { get; set; } = "fa-solid fa-folder";
        public string? Descripcion { get; set; }
        public int Orden { get; set; }
    }
}