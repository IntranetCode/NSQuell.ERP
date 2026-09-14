namespace ERP.NSQuell.Models
{
    // NSQ_ADMIN_MENUS_CRUD_V1
    public sealed class AdministracionMenusVm
    {
        public List<AdministracionMenuGrupoVm> Grupos { get; set; } = new();
        public List<AdministracionMenuVm> Menus { get; set; } = new();
        public List<AdministracionSubMenuVm> SubMenus { get; set; } = new();
    }

    public sealed class AdministracionMenuGrupoVm
    {
        public int MenuGrupoID { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public string IconoCss { get; set; } = string.Empty;
        public int Orden { get; set; }
        public bool Activo { get; set; }
        public int TotalMenus { get; set; }
    }

    public sealed class AdministracionMenuVm
    {
        public int MenuID { get; set; }
        public int? MenuGrupoID { get; set; }
        public string GrupoNombre { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public string IconoCss { get; set; } = string.Empty;
        public int Orden { get; set; }
        public bool Activo { get; set; }
        public int TotalSubMenus { get; set; }
    }

    public sealed class AdministracionSubMenuVm
    {
        public int SubMenuID { get; set; }
        public int? MenuID { get; set; }
        public string GrupoNombre { get; set; } = string.Empty;
        public string MenuNombre { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string UrlEnlace { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public string IconoCss { get; set; } = string.Empty;
        public bool Activo { get; set; }
        public DateTime? FechaCreacion { get; set; }
        public DateTime? FechaModificacion { get; set; }
        public int AccionesActivas { get; set; }
    }
}