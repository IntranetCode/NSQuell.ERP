using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ERP.NSQuell.Models.ViewModels
{
    /*
     * ============================================================
     * BANDEJA PRINCIPAL DE CALIDAD
     * ============================================================
     */
    public class CalidadIndexViewModel
    {
        public string? Busqueda { get; set; }

        public string? EstadoFiltro { get; set; }

        public int TotalMostrados { get; set; }

        /*
         * Indicadores del nuevo flujo.
         */
        public int TotalPendientePrearranque { get; set; }

        public int TotalDevueltoPrearranque { get; set; }

        public int TotalArranqueAutorizado { get; set; }

        public int TotalPendientePrimerasPiezas { get; set; }

        public int TotalAjustesSolicitados { get; set; }

        public int TotalProduccionLiberada { get; set; }

        public int TotalPendienteGP12 { get; set; }

        public int TotalEnGP12 { get; set; }

        public int TotalMaterialNoConforme { get; set; }

        public int TotalCerradas { get; set; }

        /*
         * Indicadores anteriores.
         *
         * Se conservan temporalmente porque el Index actual
         * todavía los utiliza. Se retirarán cuando actualicemos
         * la vista.
         */
        public int TotalAbiertas { get; set; }

        public int TotalLiberadas { get; set; }

        public int TotalGPI2 { get; set; }

        public int TotalContencion { get; set; }

        public int TotalScrap { get; set; }

        public List<CalidadListadoItemViewModel> Inspecciones { get; set; } =
            new();
    }

    /*
     * ============================================================
     * RENGLÓN DE LA BANDEJA DE CALIDAD
     * ============================================================
     */
    public class CalidadListadoItemViewModel
    {
        public int InspeccionID { get; set; }

        /*
         * Relaciones con Planeación.
         */
        public int? ProgramaProduccionID { get; set; }

        public int? SolicitudProduccionID { get; set; }

        public int? SolicitudProduccionDetalleID { get; set; }

        public int? ReleaseID { get; set; }

        public int? ReleaseDetalleID { get; set; }

        /*
         * Información de la corrida.
         */
        public string? CodigoBarras { get; set; }

        public string? OrdenTrabajo { get; set; }

        public string? ClienteNombre { get; set; }

        public string? NumeroParte { get; set; }

        public string? Material { get; set; }

        public string? Proceso { get; set; }

        public string? Maquina { get; set; }

        public string? Molde { get; set; }

        public string? OperadorPrincipalNombre { get; set; }

        public string? OperadorAuxiliarNombre { get; set; }

        /*
         * Cantidades.
         */
        public decimal CantidadTotal { get; set; }

        public decimal CantidadRevisada { get; set; }

        public decimal CantidadPendiente { get; set; }

        /*
         * Fechas.
         */
        public DateTime? FechaInicioProgramada { get; set; }

        public DateTime? FechaFinProgramada { get; set; }

        public DateTime? FechaNotificacionCalidad { get; set; }

        /*
         * Resultado.
         */
        public string? ResultadoCalidad { get; set; }

        public string? Etiqueta { get; set; }

        public string Estado { get; set; } =
            CalidadEstados.PendientePrearranque;

        public bool ConfiguracionInvalidada { get; set; }

        public string? MotivoInvalidacion { get; set; }

        public DateTime FechaCreacion { get; set; }
    }

    /*
     * ============================================================
     * FORMULARIO LEGADO
     *
     * Se conserva porque la vista Crear actual todavía depende
     * de este ViewModel. Posteriormente la creación manual se
     * retirará y se sustituirá por el envío desde Producción.
     * ============================================================
     */
    public class CalidadFormViewModel
    {
        [StringLength(
            150,
            ErrorMessage =
                "El código de barras no puede superar los 150 caracteres."
        )]
        public string? CodigoBarras { get; set; }

        [StringLength(
            120,
            ErrorMessage =
                "La orden de trabajo no puede superar los 120 caracteres."
        )]
        public string? OrdenTrabajo { get; set; }

        [StringLength(
            120,
            ErrorMessage =
                "El número de parte no puede superar los 120 caracteres."
        )]
        public string? NumeroParte { get; set; }

        [StringLength(
            250,
            ErrorMessage =
                "El material no puede superar los 250 caracteres."
        )]
        public string? Material { get; set; }

        [StringLength(
            200,
            ErrorMessage =
                "El proceso no puede superar los 200 caracteres."
        )]
        public string? Proceso { get; set; }

        [StringLength(
            150,
            ErrorMessage =
                "La máquina no puede superar los 150 caracteres."
        )]
        public string? Maquina { get; set; }

        [Range(
            0,
            999999999,
            ErrorMessage =
                "La cantidad total no puede ser negativa."
        )]
        public decimal CantidadTotal { get; set; }

        [Range(
            0,
            999999999,
            ErrorMessage =
                "La cantidad revisada no puede ser negativa."
        )]
        public decimal CantidadRevisada { get; set; }

        public bool ChecklistValidado { get; set; }

        public bool HojaInspeccionProducto { get; set; }

        public bool HojaValidacionCalidad { get; set; }

        [StringLength(30)]
        public string? ResultadoCalidad { get; set; }

        [StringLength(
            1000,
            ErrorMessage =
                "Las observaciones no pueden superar los 1000 caracteres."
        )]
        public string? Observaciones { get; set; }
    }

    /*
     * ============================================================
     * DETALLE COMPLETO DE UNA LIBERACIÓN
     * ============================================================
     */
    public class CalidadDetalleViewModel
    {
        public int InspeccionID { get; set; }

        // Relaciones con Planeación
        public int? ProgramaProduccionID { get; set; }
        public int? SolicitudProduccionID { get; set; }
        public int? SolicitudProduccionDetalleID { get; set; }
        public int? ReleaseID { get; set; }
        public int? ReleaseDetalleID { get; set; }

        // Identificadores
        public int? ClienteID { get; set; }
        public int? ParteID { get; set; }
        public int? MaquinaID { get; set; }
        public int? MoldeID { get; set; }
        public int? MaterialID { get; set; }

        // Información de la corrida
        public string? CodigoBarras { get; set; }
        public string? OrdenTrabajo { get; set; }
        public string? ClienteNombre { get; set; }
        public string? NumeroParte { get; set; }
        public string? Material { get; set; }
        public string? Proceso { get; set; }
        public string? Maquina { get; set; }
        public string? Molde { get; set; }

        // Operadores
        public int? OperadorPrincipalPersonaID { get; set; }
        public string? OperadorPrincipalNombre { get; set; }

        public int? OperadorAuxiliarPersonaID { get; set; }
        public string? OperadorAuxiliarNombre { get; set; }

        // Programación
        public DateTime? FechaInicioProgramada { get; set; }
        public DateTime? FechaFinProgramada { get; set; }

        // Cantidades
        public decimal CantidadTotal { get; set; }
        public decimal CantidadRevisada { get; set; }
        public decimal CantidadPendiente { get; set; }

        // Documentación
        public bool ChecklistValidado { get; set; }
        public bool HojaInspeccionProducto { get; set; }
        public bool HojaValidacionCalidad { get; set; }

        // Notificación desde Producción
        public DateTime? FechaNotificacionCalidad { get; set; }
        public int? UsuarioNotificoID { get; set; }

        // Prearranque
        public DateTime? FechaAutorizacionPrearranque { get; set; }
        public int? UsuarioAutorizacionPrearranqueID { get; set; }
        public string? MotivoDevolucion { get; set; }

        // Primeras piezas
        public bool CincoDisparosSegregados { get; set; }
        public int CantidadDisparosConformes { get; set; }

        public bool? ValidacionDimensional { get; set; }
        public bool? ValidacionApariencia { get; set; }
        public bool? ValidacionGauge { get; set; }
        public bool? ValidacionConductividad { get; set; }

        public DateTime? FechaValidacionPrimerasPiezas { get; set; }
        public int? UsuarioValidacionPrimerasPiezasID { get; set; }

        // Resultado
        public string? ResultadoCalidad { get; set; }
        public string? Etiqueta { get; set; }

        public bool Liberado { get; set; }
        public bool RequiereGP12 { get; set; }

        // Compatibilidad temporal con las vistas anteriores
        public bool RequiereGPI2
        {
            get => RequiereGP12;
            set => RequiereGP12 = value;
        }

        public bool EnContencion { get; set; }
        public bool EsScrap { get; set; }

        public DateTime? FechaLiberacionProduccion { get; set; }
        public int? UsuarioLiberacionProduccionID { get; set; }

        // Invalidación por cambios en Planeación
        public bool ConfiguracionInvalidada { get; set; }
        public DateTime? FechaInvalidacion { get; set; }
        public int? UsuarioInvalidacionID { get; set; }
        public string? MotivoInvalidacion { get; set; }

        // Control general
        public string? Observaciones { get; set; }

        public string Estado { get; set; } =
            "PENDIENTE_PREARRANQUE";

        public int? UsuarioCreacionID { get; set; }
        public DateTime FechaCreacion { get; set; }

        public int? UsuarioModificacionID { get; set; }
        public DateTime? FechaModificacion { get; set; }

        public List<CalidadHistorialItemViewModel> Historial { get; set; } =
            new();
    }

    /*
     * ============================================================
     * DATOS OBTENIDOS DESDE PLANEACIÓN
     *
     * Este objeto no representa una tabla. Se llenará mediante
     * SqlConnection desde Planeacion_ProgramaProduccion.
     * ============================================================
     */
    public class CalidadCorridaOrigenViewModel
    {
        public int ProgramaProduccionID { get; set; }

        public int? ReleaseID { get; set; }

        public int? ReleaseDetalleID { get; set; }

        public int? SolicitudProduccionID { get; set; }

        public int? SolicitudProduccionDetalleID { get; set; }

        public string? NumeroOF { get; set; }

        public int? ClienteID { get; set; }

        public string? ClienteNombre { get; set; }

        public int? ParteID { get; set; }

        public string? NumeroParte { get; set; }

        public string? ReferenciaSAP { get; set; }

        public string? DescripcionParte { get; set; }

        public int? MaquinaID { get; set; }

        public string? MaquinaCodigo { get; set; }

        public string? MaquinaNombre { get; set; }

        public int? MoldeID { get; set; }

        public string? MoldeCodigo { get; set; }

        public int? MaterialID { get; set; }

        public string? MaterialCodigo { get; set; }

        public string? MaterialDescripcion { get; set; }

        public int CantidadProgramada { get; set; }

        public int CantidadProducida { get; set; }

        public DateTime? FechaInicioProgramada { get; set; }

        public DateTime? FechaFinProgramada { get; set; }

        public int EstatusProgramaID { get; set; }

        public int? OperadorPrincipalPersonaID { get; set; }

        public string? OperadorPrincipalNombre { get; set; }

        public int? OperadorAuxiliarPersonaID { get; set; }

        public string? OperadorAuxiliarNombre { get; set; }

        public bool TieneOF =>
            SolicitudProduccionID.HasValue &&
            SolicitudProduccionID.Value > 0;

        public bool TieneConfiguracionMinima =>
            TieneOF &&
            ParteID.HasValue &&
            MaquinaID.HasValue &&
            MoldeID.HasValue &&
            MaterialID.HasValue &&
            OperadorPrincipalPersonaID.HasValue;
    }

    /*
     * ============================================================
     * DECISIÓN DE PREARRANQUE
     * ============================================================
     */
    public class CalidadPrearranqueViewModel
    {
        [Range(
            1,
            int.MaxValue,
            ErrorMessage = "No se recibió una inspección válida."
        )]
        public int InspeccionID { get; set; }

        [StringLength(
            1000,
            ErrorMessage =
                "El motivo no puede superar los 1000 caracteres."
        )]
        public string? Motivo { get; set; }
    }

    /*
     * ============================================================
     * VALIDACIÓN DE PRIMERAS PIEZAS
     * ============================================================
     */
    public class CalidadPrimerasPiezasViewModel
    {
        [Range(
            1,
            int.MaxValue,
            ErrorMessage = "No se recibió una inspección válida."
        )]
        public int InspeccionID { get; set; }

        [Required(
            ErrorMessage =
                "Confirma que los primeros cinco disparos fueron segregados."
        )]
        public bool CincoDisparosSegregados { get; set; }

        [Range(
            0,
            5,
            ErrorMessage =
                "La cantidad de disparos conformes debe estar entre 0 y 5."
        )]
        public int CantidadDisparosConformes { get; set; }

        /*
         * true  = cumple
         * false = no cumple
         * null  = no aplica
         */
        public bool? ValidacionDimensional { get; set; }

        public bool? ValidacionApariencia { get; set; }

        public bool? ValidacionGauge { get; set; }

        public bool? ValidacionConductividad { get; set; }

        [StringLength(
            1000,
            ErrorMessage =
                "Las observaciones no pueden superar los 1000 caracteres."
        )]
        public string? Observaciones { get; set; }
    }

    /*
     * ============================================================
     * HISTORIAL
     * ============================================================
     */
    public class CalidadHistorialItemViewModel
    {
        public int HistorialID { get; set; }

        public string Movimiento { get; set; } =
            string.Empty;

        public string? EstadoAnterior { get; set; }

        public string? EstadoNuevo { get; set; }

        public string? ResultadoCalidad { get; set; }

        public string? Etiqueta { get; set; }

        public string? Comentario { get; set; }

        public int? UsuarioID { get; set; }

        public DateTime FechaMovimiento { get; set; }
    }
}