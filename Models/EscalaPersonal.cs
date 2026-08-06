using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ERP.NSQuell.Models
{
    /// <summary>
    /// Modelos y ViewModels del módulo Escala de Personal.
    /// El módulo trabaja con SQL directo; estas clases no requieren DbContext.
    /// </summary>
    public class EscalaPersonal
    {
        public static class Estados
        {
            public const string Borrador = "Borrador";
            public const string Publicada = "Publicada";
            public const string Cancelada = "Cancelada";
        }

        public static class TiposTurno
        {
            public const string Regular = "Regular";
            public const string Mixto = "Mixto";
            public const string DocePorDoce = "12x12";
            public const string Especial = "Especial";
        }

        public static class TiposNovedad
        {
            public const string Ingreso = "Ingreso";
            public const string Baja = "Baja";
            public const string Incapacidad = "Incapacidad";
            public const string Vacaciones = "Vacaciones";
            public const string Otra = "Otra";
        }

        // ============================================================
        // MODELOS QUE REPRESENTAN LAS TABLAS DEL MÓDULO
        // ============================================================

        public class Turno
        {
            public int TurnoID { get; set; }

            [Required(ErrorMessage = "El nombre del turno es obligatorio.")]
            [StringLength(100)]
            [Display(Name = "Nombre del turno")]
            public string Nombre { get; set; } = string.Empty;

            [Display(Name = "Hora de inicio")]
            [DataType(DataType.Time)]
            public TimeSpan? HoraInicio { get; set; }

            [Display(Name = "Hora de término")]
            [DataType(DataType.Time)]
            public TimeSpan? HoraFin { get; set; }

            [Display(Name = "Termina al día siguiente")]
            public bool CruzaDiaSiguiente { get; set; }

            [Display(Name = "Horario flexible")]
            public bool EsFlexible { get; set; }

            [Required(ErrorMessage = "El tipo de turno es obligatorio.")]
            [StringLength(30)]
            [Display(Name = "Tipo de turno")]
            public string TipoTurno { get; set; } = TiposTurno.Regular;

            [Required(ErrorMessage = "El color es obligatorio.")]
            [StringLength(7)]
            [RegularExpression(
                "^#[0-9A-Fa-f]{6}$",
                ErrorMessage = "El color debe tener el formato #RRGGBB.")]
            public string Color { get; set; } = "#6C757D";

            [Range(0, int.MaxValue, ErrorMessage = "El orden no puede ser negativo.")]
            public int Orden { get; set; }

            public bool Activo { get; set; } = true;
            public DateTime FechaRegistro { get; set; }
            public int? CreadoPor { get; set; }
            public DateTime? FechaModificacion { get; set; }
            public int? ActualizadoPor { get; set; }

            [ValidateNever]
            public string HorarioTexto
            {
                get
                {
                    if (EsFlexible || !HoraInicio.HasValue || !HoraFin.HasValue)
                    {
                        return Nombre;
                    }

                    return $"{HoraInicio.Value:hh\\:mm} - {HoraFin.Value:hh\\:mm}";
                }
            }
        }

        public class Funcion
        {
            public int FuncionID { get; set; }

            [Required(ErrorMessage = "El nombre de la función es obligatorio.")]
            [StringLength(120)]
            [Display(Name = "Función o rol")]
            public string Nombre { get; set; } = string.Empty;

            [StringLength(500)]
            public string? Descripcion { get; set; }

            [Display(Name = "Departamento")]
            public int? DepartamentoID { get; set; }

            [Display(Name = "Requiere máquina")]
            public bool RequiereMaquina { get; set; }

            [Range(0, int.MaxValue, ErrorMessage = "El orden no puede ser negativo.")]
            public int Orden { get; set; }

            public bool Activo { get; set; } = true;
            public DateTime FechaRegistro { get; set; }
            public int? CreadoPor { get; set; }
            public DateTime? FechaModificacion { get; set; }
            public int? ActualizadoPor { get; set; }

            [ValidateNever]
            public string NombreDepartamento { get; set; } = "Todos los departamentos";
        }

        public class Escala
        {
            public int EscalaID { get; set; }

            [Required(ErrorMessage = "El folio es obligatorio.")]
            [StringLength(30)]
            public string Folio { get; set; } = string.Empty;

            [Required(ErrorMessage = "El código del documento es obligatorio.")]
            [StringLength(30)]
            [Display(Name = "Código del documento")]
            public string CodigoDocumento { get; set; } = "BQ-F-PR01-10";

            [Required(ErrorMessage = "La versión es obligatoria.")]
            [StringLength(10)]
            [Display(Name = "Versión")]
            public string VersionDocumento { get; set; } = "01";

            [Required(ErrorMessage = "La fecha de elaboración es obligatoria.")]
            [DataType(DataType.Date)]
            [Display(Name = "Fecha de elaboración")]
            public DateTime FechaElaboracion { get; set; } = DateTime.Today;

            [Range(2000, 2100, ErrorMessage = "El año debe estar entre 2000 y 2100.")]
            [Display(Name = "Año")]
            public int Anio { get; set; } = DateTime.Today.Year;

            [Range(1, 53, ErrorMessage = "La semana debe estar entre 1 y 53.")]
            [Display(Name = "Semana")]
            public int NumeroSemana { get; set; }

            [Required(ErrorMessage = "La fecha inicial es obligatoria.")]
            [DataType(DataType.Date)]
            [Display(Name = "Fecha inicial")]
            public DateTime FechaInicio { get; set; }

            [Required(ErrorMessage = "La fecha final es obligatoria.")]
            [DataType(DataType.Date)]
            [Display(Name = "Fecha final")]
            public DateTime FechaFin { get; set; }

            [Required(ErrorMessage = "El periodo de trabajo es obligatorio.")]
            [StringLength(50)]
            [Display(Name = "Periodo de trabajo")]
            public string PeriodoTrabajo { get; set; } = "Semanal";

            [Required]
            [StringLength(20)]
            public string Estado { get; set; } = Estados.Borrador;

            [StringLength(1000)]
            public string? Observaciones { get; set; }

            public int ElaboradoPor { get; set; }
            public DateTime FechaRegistro { get; set; }
            public DateTime? FechaModificacion { get; set; }
            public int? ActualizadoPor { get; set; }
            public DateTime? FechaPublicacion { get; set; }
            public int? PublicadoPor { get; set; }
            public bool Activo { get; set; } = true;

            [ValidateNever]
            public string ElaboradoPorNombre { get; set; } = string.Empty;

            [ValidateNever]
            public string PublicadoPorNombre { get; set; } = string.Empty;

            [ValidateNever]
            public int TotalAsignaciones { get; set; }

            [ValidateNever]
            public int TotalPersonas { get; set; }

            [ValidateNever]
            public int TotalNovedades { get; set; }
        }

        public class EscalaTurno
        {
            public int EscalaTurnoID { get; set; }
            public int EscalaID { get; set; }
            public int? TurnoOrigenID { get; set; }

            [Required]
            [StringLength(100)]
            public string Nombre { get; set; } = string.Empty;

            [DataType(DataType.Time)]
            public TimeSpan? HoraInicio { get; set; }

            [DataType(DataType.Time)]
            public TimeSpan? HoraFin { get; set; }

            public bool CruzaDiaSiguiente { get; set; }
            public bool EsFlexible { get; set; }

            [Required]
            [StringLength(30)]
            public string TipoTurno { get; set; } = TiposTurno.Regular;

            [Required]
            [StringLength(7)]
            public string Color { get; set; } = "#6C757D";

            public int Orden { get; set; }
            public bool Activo { get; set; } = true;

            [ValidateNever]
            public string HorarioTexto
            {
                get
                {
                    if (EsFlexible || !HoraInicio.HasValue || !HoraFin.HasValue)
                    {
                        return Nombre;
                    }

                    return $"{HoraInicio.Value:hh\\:mm} - {HoraFin.Value:hh\\:mm}";
                }
            }
        }

        public class Asignacion
        {
            public int AsignacionID { get; set; }

            [Range(1, int.MaxValue, ErrorMessage = "La escala es obligatoria.")]
            public int EscalaID { get; set; }

            [Range(1, int.MaxValue, ErrorMessage = "Selecciona una persona.")]
            [Display(Name = "Persona")]
            public int PersonalID { get; set; }

            [Display(Name = "Departamento")]
            public int? DepartamentoID { get; set; }

            [Range(1, int.MaxValue, ErrorMessage = "Selecciona una función.")]
            [Display(Name = "Función")]
            public int FuncionID { get; set; }

            [Display(Name = "Máquina")]
            public int? MaquinaID { get; set; }

            [Range(1, int.MaxValue, ErrorMessage = "Selecciona un turno.")]
            [Display(Name = "Turno")]
            public int EscalaTurnoID { get; set; }

            [Required(ErrorMessage = "La fecha inicial es obligatoria.")]
            [DataType(DataType.Date)]
            [Display(Name = "Desde")]
            public DateTime FechaInicio { get; set; }

            [Required(ErrorMessage = "La fecha final es obligatoria.")]
            [DataType(DataType.Date)]
            [Display(Name = "Hasta")]
            public DateTime FechaFin { get; set; }

            [StringLength(500)]
            public string? Observaciones { get; set; }

            public bool Activo { get; set; } = true;
            public DateTime FechaRegistro { get; set; }
            public int CreadoPor { get; set; }
            public DateTime? FechaModificacion { get; set; }
            public int? ActualizadoPor { get; set; }

            [ValidateNever]
            public string NumeroEmpleado { get; set; } = string.Empty;

            [ValidateNever]
            public string NombrePersona { get; set; } = string.Empty;

            [ValidateNever]
            public string NombreDepartamento { get; set; } = string.Empty;

            [ValidateNever]
            public string NombreFuncion { get; set; } = string.Empty;

            [ValidateNever]
            public string NombreMaquina { get; set; } = string.Empty;

            [ValidateNever]
            public string NombreTurno { get; set; } = string.Empty;

            [ValidateNever]
            public string ColorTurno { get; set; } = "#6C757D";
        }

        public class Novedad
        {
            public int NovedadID { get; set; }

            [Range(1, int.MaxValue)]
            public int EscalaID { get; set; }

            [Range(1, int.MaxValue, ErrorMessage = "Selecciona una persona.")]
            [Display(Name = "Persona")]
            public int PersonalID { get; set; }

            [Required(ErrorMessage = "Selecciona el tipo de novedad.")]
            [StringLength(30)]
            [Display(Name = "Tipo de novedad")]
            public string TipoNovedad { get; set; } = string.Empty;

            [Required(ErrorMessage = "La fecha inicial es obligatoria.")]
            [DataType(DataType.Date)]
            [Display(Name = "Fecha inicial")]
            public DateTime FechaInicio { get; set; }

            [DataType(DataType.Date)]
            [Display(Name = "Fecha final")]
            public DateTime? FechaFin { get; set; }

            [StringLength(300)]
            public string? Motivo { get; set; }

            [StringLength(500)]
            public string? Observaciones { get; set; }

            public bool Activo { get; set; } = true;
            public DateTime FechaRegistro { get; set; }
            public int CreadoPor { get; set; }
            public DateTime? FechaModificacion { get; set; }
            public int? ActualizadoPor { get; set; }

            [ValidateNever]
            public string NumeroEmpleado { get; set; } = string.Empty;

            [ValidateNever]
            public string NombrePersona { get; set; } = string.Empty;
        }

        public class Historial
        {
            public int HistorialID { get; set; }
            public int EscalaID { get; set; }
            public string? EstadoAnterior { get; set; }

            [Required]
            [StringLength(20)]
            public string EstadoNuevo { get; set; } = string.Empty;

            [StringLength(500)]
            public string? Comentario { get; set; }

            public DateTime FechaMovimiento { get; set; }
            public int UsuarioID { get; set; }

            [ValidateNever]
            public string NombreUsuario { get; set; } = string.Empty;
        }

        // ============================================================
        // VIEWMODELS DE CATÁLOGOS AUXILIARES
        // ============================================================

        public class PersonaOpcion
        {
            public int PersonalID { get; set; }
            public int? DepartamentoID { get; set; }
            public string NombreDepartamento { get; set; } = string.Empty;
            public string NumeroEmpleado { get; set; } = string.Empty;
            public string NombreCompleto { get; set; } = string.Empty;
            public string Puesto { get; set; } = string.Empty;
            public DateTime? FechaIngreso { get; set; }
            public DateTime? FechaBaja { get; set; }
            public bool Activo { get; set; }

            public string Texto
            {
                get
                {
                    return string.IsNullOrWhiteSpace(NumeroEmpleado)
                        ? NombreCompleto
                        : $"{NumeroEmpleado} - {NombreCompleto}";
                }
            }
        }

        public class DepartamentoOpcion
        {
            public int DepartamentoID { get; set; }
            public string NombreDepartamento { get; set; } = string.Empty;
        }

        public class MaquinaOpcion
        {
            public int MaquinaID { get; set; }
            public string Codigo { get; set; } = string.Empty;
            public string Nombre { get; set; } = string.Empty;
            public string Area { get; set; } = string.Empty;
            public bool Activo { get; set; }

            public string Texto
            {
                get
                {
                    if (string.IsNullOrWhiteSpace(Codigo))
                    {
                        return Nombre;
                    }

                    return Codigo == Nombre ? Codigo : $"{Codigo} - {Nombre}";
                }
            }
        }

        // ============================================================
        // VIEWMODELS DE LAS VISTAS PRINCIPALES
        // ============================================================

        public class IndexVM
        {
            public List<Escala> Escalas { get; set; } = new List<Escala>();
            public Escala? EscalaSemanaActual { get; set; }
            public int TotalBorradores { get; set; }
            public int TotalPublicadas { get; set; }
            public int TotalCanceladas { get; set; }
            public bool PuedeAdministrar { get; set; }
        }

        public class CrearVM : IValidatableObject
        {
            public Escala Escala { get; set; } = new Escala();

            [Display(Name = "Horarios de la escala")]
            public List<HorarioCrearVM> Horarios { get; set; } =
                new List<HorarioCrearVM>();

            public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
            {
                if (Escala.FechaFin < Escala.FechaInicio)
                {
                    yield return new ValidationResult(
                        "La fecha final no puede ser anterior a la fecha inicial.",
                        new[] { "Escala.FechaFin" });
                }

                if (Horarios == null || Horarios.Count == 0)
                {
                    yield return new ValidationResult(
                        "Crea por lo menos un horario para la escala.",
                        new[] { nameof(Horarios) });
                    yield break;
                }

                var nombresDuplicados = Horarios
                    .Where(x => !string.IsNullOrWhiteSpace(x.Nombre))
                    .GroupBy(
                        x => x.Nombre.Trim(),
                        StringComparer.OrdinalIgnoreCase)
                    .Where(x => x.Count() > 1)
                    .Select(x => x.Key)
                    .ToList();

                if (nombresDuplicados.Count > 0)
                {
                    yield return new ValidationResult(
                        $"No repitas nombres de horario: {string.Join(", ", nombresDuplicados)}.",
                        new[] { nameof(Horarios) });
                }
            }
        }

        public class HorarioCrearVM : IValidatableObject
        {
            public int? TurnoOrigenID { get; set; }

            [Required(ErrorMessage = "Escribe el nombre del horario.")]
            [StringLength(100)]
            [Display(Name = "Nombre")]
            public string Nombre { get; set; } = string.Empty;

            [DataType(DataType.Time)]
            [Display(Name = "Hora de entrada")]
            public TimeSpan? HoraInicio { get; set; }

            [DataType(DataType.Time)]
            [Display(Name = "Hora de salida")]
            public TimeSpan? HoraFin { get; set; }

            public bool CruzaDiaSiguiente { get; set; }

            [Display(Name = "Horario flexible")]
            public bool EsFlexible { get; set; }

            [Required(ErrorMessage = "Selecciona el tipo de horario.")]
            [RegularExpression(
                "^(Regular|Mixto|12x12|Especial)$",
                ErrorMessage = "El tipo de horario no es válido.")]
            [Display(Name = "Tipo")]
            public string TipoTurno { get; set; } = TiposTurno.Regular;

            [Required(ErrorMessage = "Selecciona un color.")]
            [StringLength(7)]
            [RegularExpression(
                "^#[0-9A-Fa-f]{6}$",
                ErrorMessage = "El color debe tener el formato #RRGGBB.")]
            public string Color { get; set; } = "#6C757D";

            public int Orden { get; set; }

            public IEnumerable<ValidationResult> Validate(
                ValidationContext validationContext)
            {
                if (EsFlexible)
                {
                    yield break;
                }

                if (!HoraInicio.HasValue)
                {
                    yield return new ValidationResult(
                        "Indica la hora de entrada.",
                        new[] { nameof(HoraInicio) });
                }

                if (!HoraFin.HasValue)
                {
                    yield return new ValidationResult(
                        "Indica la hora de salida.",
                        new[] { nameof(HoraFin) });
                }

                if (HoraInicio.HasValue
                    && HoraFin.HasValue
                    && HoraInicio.Value == HoraFin.Value)
                {
                    yield return new ValidationResult(
                        "La hora de entrada y salida no pueden ser iguales.",
                        new[] { nameof(HoraFin) });
                }
            }
        }

        public class EditorVM
        {
            public Escala Escala { get; set; } = new Escala();
            public List<EscalaTurno> Turnos { get; set; } = new List<EscalaTurno>();
            public List<GrupoAsignacionVM> Grupos { get; set; } = new List<GrupoAsignacionVM>();
            public List<Novedad> Novedades { get; set; } = new List<Novedad>();

            [ValidateNever]
            public List<PersonaOpcion> Personas { get; set; } = new List<PersonaOpcion>();

            [ValidateNever]
            public List<DepartamentoOpcion> Departamentos { get; set; } =
                new List<DepartamentoOpcion>();

            [ValidateNever]
            public List<Funcion> Funciones { get; set; } = new List<Funcion>();

            [ValidateNever]
            public List<MaquinaOpcion> Maquinas { get; set; } = new List<MaquinaOpcion>();

            public bool PuedeEditar { get; set; }
            public bool PuedePublicar { get; set; }
        }

        public class GrupoAsignacionVM
        {
            public int? DepartamentoID { get; set; }
            public string NombreDepartamento { get; set; } = string.Empty;
            public int FuncionID { get; set; }
            public string NombreFuncion { get; set; } = string.Empty;
            public int? MaquinaID { get; set; }
            public string NombreMaquina { get; set; } = string.Empty;
            public int OrdenDepartamento { get; set; }
            public int OrdenFuncion { get; set; }
            public List<CeldaTurnoVM> Celdas { get; set; } = new List<CeldaTurnoVM>();
        }

        public class CeldaTurnoVM
        {
            public int EscalaTurnoID { get; set; }
            public string NombreTurno { get; set; } = string.Empty;
            public string Color { get; set; } = "#6C757D";
            public List<Asignacion> Asignaciones { get; set; } = new List<Asignacion>();
            public int TotalPersonas => Asignaciones.Count;
        }

        public class GuardarAsignacionVM : IValidatableObject
        {
            public int? AsignacionID { get; set; }

            [Range(1, int.MaxValue)]
            public int EscalaID { get; set; }

            [Range(1, int.MaxValue, ErrorMessage = "Selecciona una persona.")]
            public int PersonalID { get; set; }

            // Se conserva únicamente para compatibilidad con registros y vistas
            // anteriores. El departamento no es necesario para asignar personal.
            public int? DepartamentoID { get; set; }

            [Range(1, int.MaxValue, ErrorMessage = "Selecciona una función.")]
            public int FuncionID { get; set; }

            public int? MaquinaID { get; set; }

            [Range(1, int.MaxValue, ErrorMessage = "Selecciona un turno.")]
            public int EscalaTurnoID { get; set; }

            [Required]
            [DataType(DataType.Date)]
            public DateTime FechaInicio { get; set; }

            [Required]
            [DataType(DataType.Date)]
            public DateTime FechaFin { get; set; }

            [StringLength(500)]
            public string? Observaciones { get; set; }

            public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
            {
                if (FechaFin < FechaInicio)
                {
                    yield return new ValidationResult(
                        "La fecha final no puede ser anterior a la fecha inicial.",
                        new[] { nameof(FechaFin) });
                }
            }
        }

        public class NovedadesVM
        {
            public Escala Escala { get; set; } = new Escala();
            public List<Novedad> Novedades { get; set; } = new List<Novedad>();

            [ValidateNever]
            public List<PersonaOpcion> Personas { get; set; } = new List<PersonaOpcion>();

            public bool PuedeEditar { get; set; }
        }

        public class GuardarNovedadVM : IValidatableObject
        {
            public int? NovedadID { get; set; }

            [Range(1, int.MaxValue)]
            public int EscalaID { get; set; }

            [Range(1, int.MaxValue, ErrorMessage = "Selecciona una persona.")]
            public int PersonalID { get; set; }

            [Required(ErrorMessage = "Selecciona el tipo de novedad.")]
            public string TipoNovedad { get; set; } = string.Empty;

            [Required]
            [DataType(DataType.Date)]
            public DateTime FechaInicio { get; set; }

            [DataType(DataType.Date)]
            public DateTime? FechaFin { get; set; }

            [StringLength(300)]
            public string? Motivo { get; set; }

            [StringLength(500)]
            public string? Observaciones { get; set; }

            public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
            {
                if (FechaFin.HasValue && FechaFin.Value < FechaInicio)
                {
                    yield return new ValidationResult(
                        "La fecha final no puede ser anterior a la fecha inicial.",
                        new[] { nameof(FechaFin) });
                }

                if (TipoNovedad == TiposNovedad.Baja && FechaFin.HasValue)
                {
                    yield return new ValidationResult(
                        "Una baja no requiere fecha final.",
                        new[] { nameof(FechaFin) });
                }
            }
        }

        public class DetalleVM
        {
            public Escala Escala { get; set; } = new Escala();
            public List<EscalaTurno> Turnos { get; set; } = new List<EscalaTurno>();
            public List<GrupoAsignacionVM> Grupos { get; set; } = new List<GrupoAsignacionVM>();
            public List<Novedad> Novedades { get; set; } = new List<Novedad>();
            public List<Historial> Historial { get; set; } = new List<Historial>();
        }

        public class CatalogoTurnosVM
        {
            public List<Turno> Turnos { get; set; } = new List<Turno>();
            public Turno Formulario { get; set; } = new Turno();

            [ValidateNever]
            public List<SelectListItem> TiposTurno { get; set; } =
                new List<SelectListItem>();
        }

        public class CatalogoFuncionesVM
        {
            public List<Funcion> Funciones { get; set; } = new List<Funcion>();
            public Funcion Formulario { get; set; } = new Funcion();

            [ValidateNever]
            public List<SelectListItem> Departamentos { get; set; } =
                new List<SelectListItem>();
        }

        public class CopiarEscalaVM
        {
            [Range(1, int.MaxValue)]
            public int EscalaOrigenID { get; set; }

            [Range(2000, 2100)]
            public int AnioDestino { get; set; }

            [Range(1, 53)]
            public int NumeroSemanaDestino { get; set; }

            [Required]
            [DataType(DataType.Date)]
            public DateTime FechaInicioDestino { get; set; }

            [Required]
            [DataType(DataType.Date)]
            public DateTime FechaFinDestino { get; set; }

            public bool CopiarNovedades { get; set; }
        }

        public class CambiarEstadoVM
        {
            [Range(1, int.MaxValue)]
            public int EscalaID { get; set; }

            [Required]
            public string EstadoNuevo { get; set; } = string.Empty;

            [StringLength(500)]
            public string? Comentario { get; set; }
        }
    }
}
