namespace ERP.NSQuell.Models
{
    public class AuditoriaUsuario
    {
        public int AuditoriaID { get; set; }
        public string DescripcionDelCambio { get; set; }
        public DateTime FechaModificacion { get; set; }
        public string ModificadoPor { get; set; }
    }
}