namespace ERP.NSQuell.Models.Opciones
{
    public class CorreoOpciones
    {

        public string SmtpHost { get; set; } = "";
        public int SmtpPort { get; set; } = 587;

        public string Security { get; set; } = "StartTls";

        public string Remitente { get; set; } = "";
        public string NombreRemitente { get; set; } = "";

        // Estos NO van en appsettings.json en dev/prod; se leen de secretos/variables
        public string Usuario { get; set; } = "";
        public string Contrasena { get; set; } = "";


        public bool Habilitado { get; set; } = true;
        public bool SoloPruebas { get; set; } = true;
        public int MaxDestinatariosEnPrueba { get; set; } = 3;

        // Lista blanca opcional (separados por coma)
        public string? ListaBlanca { get; set; } // "yo@mi.com,otro@mi.com"

    }
}
