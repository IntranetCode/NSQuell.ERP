namespace ERP.NSQuell.Models
{
    public class Notificacion
    {

        public int Id { get; set; }

        public string Tipo { get; set; } = string.Empty;
        public string Titulo { get; set; }= string.Empty;
        public string? Mensaje { get; set; }

        public int IdOrigen { get; set; }
         public string TablaOrigen { get; set; } = string.Empty;
        public int? UsuarioId{ get; set; }
        public int? EmpresaId { get; set; }

        public DateTime FechaCreacion { get; set; } 
        public DateTime FechaExpiracion { get; set; }
        public bool EsLeida { get; set; }

        public DateTime? FechaEliminacion { get; set; }

        public bool EsArchivada { get; set; }


    }

    public class NotificacionLectura
    {
             public int Id { get; set; }
        public int NotificacionId { get; set; }
        public int UsuarioId { get; set; }
        public DateTime FechaLeida { get; set; }

        public Notificacion Notificacion { get; set; } = null!;
    }

    public class  NotificacionEmpresas
    {
        public int Id { get; set; }
        public int NotificacionId { get; set; }
        public int EmpresaId { get; set; }


        public Notificacion Notificacion { get; set; } = null!;


    }

}
