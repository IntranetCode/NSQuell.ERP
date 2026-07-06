using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

// Asegúrate de que este namespace coincida con la ubicación del archivo
namespace ERP.NSQuell.Models
{
    // Esta clase es el "plano" que representa tu tabla Roles.
    public class Rol
    {
        [Key] // Le dice a C# que RolID es la llave primaria.
        public int RolID { get; set; }

        // ¡Perfecto! Aquí le decimos a C# que la propiedad "NombreDelRol"
        // corresponde a la columna "URol" en tu base de datos.
        [Column("URol")]
        public string NombreDelRol { get; set; }
    }
}


