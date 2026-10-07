using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ERP.NSQuell.Models
{
    // NSQ_RRHH_ESCALA_OPERADORES_INTEGRACION_V1_0
    public sealed class EscalaPersonalOperadoresRhVm
    {
        public List<EscalaPersonalOperadorRhVm> Operadores { get; set; } = new();
        public bool PuedeAdministrar { get; set; }
        public int Total => Operadores.Count;
        public int Activos => Operadores.FindAll(x => x.PersonaActiva).Count;
        public int Inactivos => Operadores.FindAll(x => !x.PersonaActiva).Count;
        public int ConCuentaActiva => Operadores.FindAll(x => x.UsuarioActivo).Count;
    }

    public sealed class EscalaPersonalOperadorRhVm
    {
        public int PersonaID { get; set; }
        public string NumeroControl { get; set; } = string.Empty;
        public string NombreCompleto { get; set; } = string.Empty;
        public string Puesto { get; set; } = string.Empty;
        public DateTime? FechaIngreso { get; set; }
        public DateTime? FechaBaja { get; set; }
        public bool PersonaActiva { get; set; }

        public int? UsuarioID { get; set; }
        public string Username { get; set; } = string.Empty;
        public bool UsuarioActivo { get; set; }
        public bool DebeCambiarPassword { get; set; }

        public int PartesPolivalencia { get; set; }
        public bool TieneCuenta => UsuarioID.HasValue;
    }

    public sealed class EscalaPersonalAltaOperadorRhVm
    {
        [Required(ErrorMessage = "El número de control es obligatorio.")]
        [StringLength(50)]
        public string NumeroControl { get; set; } = string.Empty;

        [Required(ErrorMessage = "El nombre es obligatorio.")]
        [StringLength(150)]
        public string Nombre { get; set; } = string.Empty;

        [Required(ErrorMessage = "El apellido paterno es obligatorio.")]
        [StringLength(150)]
        public string ApellidoPaterno { get; set; } = string.Empty;

        [StringLength(150)]
        public string? ApellidoMaterno { get; set; }

        [EmailAddress(ErrorMessage = "El correo no tiene un formato válido.")]
        [StringLength(150)]
        public string? Correo { get; set; }

        [DataType(DataType.Date)]
        public DateTime? FechaIngreso { get; set; }

        [StringLength(250)]
        public string? PasswordTemporal { get; set; }
    }
}