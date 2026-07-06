namespace ERP.NSQuell.Areas.AdminUsuarios.DTOs
{
    public class OverridesVm
    {
        public int UsuarioID { get; set; }
        public int? EmpresaID { get; set; }   // null = override global
        public List<OverrideItemDto> Items { get; set; } = new();

        public int MenuID { get; set; }
        public string MenuNombre { get; set; } = "";

    }
}
