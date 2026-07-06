
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.NSQuell.Models
{
    public class Usuario
    {
        [Key]
        public int UsuarioID { get; set; }
      
        public string? Username { get; set; }
      
        public string? Contrasena { get; set; } 

        public int RolID { get; set; }

        public int PersonaID { get; set; }

        public bool Activo { get; set; }
        public DateTime FechaCreacion { get; set; }

        public bool DebeCambiarPassword { get; set; }

        public DateTime? FechaUltimoCambioPassword { get; set; }

        // Propiedades de navegación
        [ForeignKey("PersonaID")]
        public virtual Persona Persona { get; set; }
    }
}