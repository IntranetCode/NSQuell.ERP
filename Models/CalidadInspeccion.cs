using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.NSQuell.Models
{
    [Table("Calidad_Inspecciones")]
    public class CalidadInspeccion
    {
        [Key]
        public int InspeccionID { get; set; }

        // =========================================================
        // RELACIONES CON PLANEACIÓN
        // =========================================================

        public int? ProgramaProduccionID { get; set; }

        public int? SolicitudProduccionID { get; set; }

        public int? SolicitudProduccionDetalleID { get; set; }

        public int? ReleaseID { get; set; }

        public int? ReleaseDetalleID { get; set; }

        // =========================================================
        // IDENTIFICADORES DE LA CORRIDA
        // =========================================================

        public int? ClienteID { get; set; }

        [StringLength(200)]
        public string? ClienteNombre { get; set; }

        public int? ParteID { get; set; }

        public int? MaquinaID { get; set; }

        public int? MoldeID { get; set; }

        public int? MaterialID { get; set; }

        // =========================================================
        // FOTOGRAFÍA HISTÓRICA
        // Estos campos conservan la información exacta que Calidad
        // recibió, aunque Planeación cambie posteriormente.
        // =========================================================

        [StringLength(150)]
        public string? CodigoBarras { get; set; }

        [StringLength(120)]
        public string? OrdenTrabajo { get; set; }

        [StringLength(120)]
        public string? NumeroParte { get; set; }

        [StringLength(250)]
        public string? Material { get; set; }

        [StringLength(200)]
        public string? Proceso { get; set; }

        [StringLength(150)]
        public string? Maquina { get; set; }

        [StringLength(150)]
        public string? Molde { get; set; }

        public DateTime? FechaInicioProgramada { get; set; }

        public DateTime? FechaFinProgramada { get; set; }

        // =========================================================
        // OPERADORES ASIGNADOS
        // =========================================================

        public int? OperadorPrincipalPersonaID { get; set; }

        [StringLength(250)]
        public string? OperadorPrincipalNombre { get; set; }

        public int? OperadorAuxiliarPersonaID { get; set; }

        [StringLength(250)]
        public string? OperadorAuxiliarNombre { get; set; }

        // =========================================================
        // CANTIDADES
        // =========================================================

        [Column(TypeName = "decimal(18,3)")]
        public decimal CantidadTotal { get; set; }

        [Column(TypeName = "decimal(18,3)")]
        public decimal CantidadRevisada { get; set; }

        [Column(TypeName = "decimal(18,3)")]
        public decimal CantidadPendiente { get; set; }

        // =========================================================
        // DOCUMENTACIÓN
        // =========================================================

        public bool ChecklistValidado { get; set; }

        public bool HojaInspeccionProducto { get; set; }

        public bool HojaValidacionCalidad { get; set; }

        // =========================================================
        // ENVÍO DESDE PRODUCCIÓN
        // =========================================================

        public DateTime? FechaNotificacionCalidad { get; set; }

        public int? UsuarioNotificoID { get; set; }

        // =========================================================
        // AUTORIZACIÓN DE PREARRANQUE
        // =========================================================

        public DateTime? FechaAutorizacionPrearranque { get; set; }

        public int? UsuarioAutorizacionPrearranqueID { get; set; }

        [StringLength(1000)]
        public string? MotivoDevolucion { get; set; }

        // =========================================================
        // VALIDACIÓN DE PRIMERAS PIEZAS
        // =========================================================

        public bool CincoDisparosSegregados { get; set; }

        [Range(
            0,
            5,
            ErrorMessage = "La cantidad de disparos conformes debe estar entre 0 y 5."
        )]
        public int CantidadDisparosConformes { get; set; }

        /*
         * true  = conforme
         * false = no conforme
         * null  = pendiente o no aplica
         */
        public bool? ValidacionDimensional { get; set; }

        public bool? ValidacionApariencia { get; set; }

        public bool? ValidacionGauge { get; set; }

        public bool? ValidacionConductividad { get; set; }

        public DateTime? FechaValidacionPrimerasPiezas { get; set; }

        public int? UsuarioValidacionPrimerasPiezasID { get; set; }

        // =========================================================
        // RESULTADO Y LIBERACIÓN
        // =========================================================

        [StringLength(30)]
        public string? ResultadoCalidad { get; set; }

        [StringLength(30)]
        public string? Etiqueta { get; set; }

        public bool Liberado { get; set; }

        /*
         * Esta es la propiedad real almacenada en SQL Server.
         */
        public bool RequiereGP12 { get; set; }

        /*
         * Propiedad temporal de compatibilidad.
         *
         * El controlador y las vistas antiguas todavía utilizan
         * RequiereGPI2. Al tener [NotMapped], Entity Framework no
         * buscará una columna llamada RequiereGPI2.
         *
         * Todo valor se redirige hacia RequiereGP12.
         */
        [NotMapped]
        public bool RequiereGPI2
        {
            get => RequiereGP12;
            set => RequiereGP12 = value;
        }

        public bool EnContencion { get; set; }

        public bool EsScrap { get; set; }

        public DateTime? FechaLiberacionProduccion { get; set; }

        public int? UsuarioLiberacionProduccionID { get; set; }

        // =========================================================
        // INVALIDACIÓN POR CAMBIOS DE PLANEACIÓN
        // =========================================================

        public bool ConfiguracionInvalidada { get; set; }

        public DateTime? FechaInvalidacion { get; set; }

        public int? UsuarioInvalidacionID { get; set; }

        [StringLength(1000)]
        public string? MotivoInvalidacion { get; set; }

        // =========================================================
        // CONTROL GENERAL
        // =========================================================

        [StringLength(1000)]
        public string? Observaciones { get; set; }

        [Required]
        [StringLength(50)]
        public string Estado { get; set; } =
            CalidadEstados.PendientePrearranque;

        public int? UsuarioCreacionID { get; set; }

        public DateTime FechaCreacion { get; set; }

        public int? UsuarioModificacionID { get; set; }

        public DateTime? FechaModificacion { get; set; }

        public ICollection<CalidadInspeccionHistorial> Historial { get; set; } =
            new List<CalidadInspeccionHistorial>();
    }

    /// <summary>
    /// Estados propios del flujo de Calidad.
    /// No sustituyen los estados de Planeación ni Producción.
    /// </summary>
    public static class CalidadEstados
    {
        public const string PendientePrearranque =
            "PENDIENTE_PREARRANQUE";

        public const string DevueltoPrearranque =
            "DEVUELTO_PREARRANQUE";

        public const string ArranqueAutorizado =
            "ARRANQUE_AUTORIZADO";

        public const string PendientePrimerasPiezas =
            "PENDIENTE_PRIMERAS_PIEZAS";

        public const string AjustesSolicitados =
            "AJUSTES_SOLICITADOS";

        public const string ProduccionLiberada =
            "PRODUCCION_LIBERADA";

        public const string PendienteGP12 =
            "PENDIENTE_GP12";

        public const string EnGP12 =
            "EN_GP12";

        public const string MaterialLiberado =
            "MATERIAL_LIBERADO";

        public const string MaterialNoConforme =
            "MATERIAL_NO_CONFORME";

        public const string Cerrada =
            "CERRADA";

        /*
         * Estados anteriores.
         * Se conservan temporalmente para leer registros existentes.
         */
        public const string LegacyAbierta =
            "ABIERTA";

        public const string LegacyLiberada =
            "LIBERADA";

        public const string LegacyGPI2 =
            "GPI2";

        public const string LegacyContencion =
            "CONTENCION";

        public const string LegacyScrap =
            "SCRAP";

        public const string LegacyDetenida =
            "DETENIDA";

        public static bool EsProcesoActivo(string? estado)
        {
            return estado == PendientePrearranque ||
                   estado == DevueltoPrearranque ||
                   estado == ArranqueAutorizado ||
                   estado == PendientePrimerasPiezas ||
                   estado == AjustesSolicitados ||
                   estado == PendienteGP12 ||
                   estado == EnGP12;
        }

        public static bool PuedeAutorizarPrearranque(string? estado)
        {
            return estado == PendientePrearranque ||
                   estado == DevueltoPrearranque;
        }

        public static bool PuedeValidarPrimerasPiezas(string? estado)
        {
            return estado == ArranqueAutorizado ||
                   estado == PendientePrimerasPiezas ||
                   estado == AjustesSolicitados;
        }

        public static bool EsEstadoFinal(string? estado)
        {
            return estado == ProduccionLiberada ||
                   estado == MaterialLiberado ||
                   estado == MaterialNoConforme ||
                   estado == Cerrada;
        }
    }

    /// <summary>
    /// Nombres normalizados para el historial de Calidad.
    /// </summary>
    public static class CalidadMovimientos
    {
        public const string RecibidoDesdeProduccion =
            "RECIBIDO_DESDE_PRODUCCION";

        public const string PrearranqueAutorizado =
            "PREARRANQUE_AUTORIZADO";

        public const string PrearranqueDevuelto =
            "PREARRANQUE_DEVUELTO";

        public const string PrimerasPiezasRecibidas =
            "PRIMERAS_PIEZAS_RECIBIDAS";

        public const string AjustesSolicitados =
            "AJUSTES_SOLICITADOS";

        public const string ProduccionLiberada =
            "PRODUCCION_LIBERADA";

        public const string EnviadoGP12 =
            "ENVIADO_GP12";

        public const string ConfiguracionInvalidada =
            "CONFIGURACION_INVALIDADA";

        public const string Cierre =
            "CIERRE";
    }
}