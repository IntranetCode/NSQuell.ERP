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

      
        public int? ProgramaProduccionID { get; set; }
        public int? EjecucionProduccionID { get; set; }
        public int? ChecklistArranqueID { get; set; }

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
        // FOTOGRAFIA HISTORICA
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
        // PERSONAL ASIGNADO
        // =========================================================

        public int? OperadorPrincipalPersonaID { get; set; }

        [StringLength(250)]
        public string? OperadorPrincipalNombre { get; set; }

        public int? OperadorAuxiliarPersonaID { get; set; }

        [StringLength(250)]
        public string? OperadorAuxiliarNombre { get; set; }

        public int? TecnicoInyeccionPersonaID { get; set; }

        [StringLength(250)]
        public string? TecnicoInyeccionNombre { get; set; }

        // =========================================================
        // CANTIDADES DE LA OF
        // =========================================================

        [Column(TypeName = "decimal(18,3)")]
        public decimal CantidadTotal { get; set; }

        [Column(TypeName = "decimal(18,3)")]
        public decimal CantidadRevisada { get; set; }

        [Column(TypeName = "decimal(18,3)")]
        public decimal CantidadPendiente { get; set; }

        // =========================================================
        // DOCUMENTACION Y PREARRANQUE
        // =========================================================

        public bool ChecklistValidado { get; set; }
        public bool HojaInspeccionProducto { get; set; }
        public bool HojaValidacionCalidad { get; set; }

        public bool AyudaVisualColocada { get; set; }
        public bool? AlertaCalidadAplica { get; set; }
        public bool? AlertaCalidadColocada { get; set; }
        public bool HIPColocada { get; set; }
        public bool HCCColocada { get; set; }
        public bool MatrizPolivalenciaValidada { get; set; }

        public DateTime? FechaNotificacionCalidad { get; set; }
        public int? UsuarioNotificoID { get; set; }

        public DateTime? FechaInicioValidacionPrearranque { get; set; }
        public DateTime? FechaFinValidacionPrearranque { get; set; }
        public int? MinutosLiberacionInicial { get; set; }
        public bool? CumplioTiempoObjetivoInicial { get; set; }

        public DateTime? FechaAutorizacionPrearranque { get; set; }
        public int? UsuarioAutorizacionPrearranqueID { get; set; }

        [StringLength(1000)]
        public string? MotivoDevolucion { get; set; }

        // =========================================================
        // RESUMEN DE LA ULTIMA VALIDACION DE PRIMERAS PIEZAS
        // El detalle completo se conserva en Calidad_PrimerasPiezasIntentos.
        // =========================================================

        public bool CincoDisparosSegregados { get; set; }

        [Range(0, 5, ErrorMessage = "La cantidad de disparos conformes debe estar entre 0 y 5.")]
        public int CantidadDisparosConformes { get; set; }

        public bool? ValidacionDimensional { get; set; }
        public bool? ValidacionApariencia { get; set; }
        public bool? ValidacionGauge { get; set; }
        public bool? ValidacionConductividad { get; set; }

        public DateTime? FechaValidacionPrimerasPiezas { get; set; }
        public int? UsuarioValidacionPrimerasPiezasID { get; set; }

        // =========================================================
        // RESULTADO Y LIBERACION
        // =========================================================

        [StringLength(30)]
        public string? ResultadoCalidad { get; set; }

        [StringLength(30)]
        public string? Etiqueta { get; set; }

        public bool Liberado { get; set; }
        public bool RequiereGP12 { get; set; }

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
        // RELIBERACION DESPUES DE PARO MAYOR A 15 MINUTOS
        // =========================================================

        public bool RequiereReliberacion { get; set; }

        // =========================================================
        // INVALIDACION POR CAMBIOS DE PLANEACION
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
        public string Estado { get; set; } = CalidadEstados.PendientePrearranque;

        public int? UsuarioCreacionID { get; set; }
        public DateTime FechaCreacion { get; set; }
        public int? UsuarioModificacionID { get; set; }
        public DateTime? FechaModificacion { get; set; }

        // =========================================================
        // NAVEGACIONES DEL MODULO DE CALIDAD
        // =========================================================

        public ICollection<CalidadInspeccionHistorial> Historial { get; set; } =
            new List<CalidadInspeccionHistorial>();

        public ICollection<CalidadPrimeraPiezaIntento> PrimerasPiezasIntentos { get; set; } =
            new List<CalidadPrimeraPiezaIntento>();

        public ICollection<CalidadMonitoreoProceso> MonitoreosProceso { get; set; } =
            new List<CalidadMonitoreoProceso>();

        public ICollection<CalidadDisposicionMaterial> DisposicionesMaterial { get; set; } =
            new List<CalidadDisposicionMaterial>();

        public ICollection<CalidadCajaLiberada> CajasLiberadas { get; set; } =
            new List<CalidadCajaLiberada>();

        public ICollection<CalidadMuestraResguardo> MuestrasResguardo { get; set; } =
            new List<CalidadMuestraResguardo>();

        public ICollection<CalidadReliberacion> Reliberaciones { get; set; } =
            new List<CalidadReliberacion>();

        public ICollection<CalidadGP12> RegistrosGP12 { get; set; } =
            new List<CalidadGP12>();
    }

    public static class CalidadEstados
    {
        public const string PendientePrearranque = "PENDIENTE_PREARRANQUE";
        public const string DevueltoPrearranque = "DEVUELTO_PREARRANQUE";
        public const string ArranqueAutorizado = "ARRANQUE_AUTORIZADO";
        public const string PendientePrimerasPiezas = "PENDIENTE_PRIMERAS_PIEZAS";
        public const string AjustesSolicitados = "AJUSTES_SOLICITADOS";
        public const string ProduccionLiberada = "PRODUCCION_LIBERADA";
        public const string MonitoreoActivo = "MONITOREO_ACTIVO";
        public const string PendienteLiberacionCaja = "PENDIENTE_LIBERACION_CAJA";
        public const string CajaLiberada = "CAJA_LIBERADA";
        public const string PendienteReliberacion = "PENDIENTE_RELIBERACION";
        public const string PendienteGP12 = "PENDIENTE_GP12";
        public const string EnGP12 = "EN_GP12";
        public const string MaterialLiberado = "MATERIAL_LIBERADO";
        public const string MaterialNoConforme = "MATERIAL_NO_CONFORME";
        public const string Cerrada = "CERRADA";

        public const string LegacyAbierta = "ABIERTA";
        public const string LegacyLiberada = "LIBERADA";
        public const string LegacyGPI2 = "GPI2";
        public const string LegacyContencion = "CONTENCION";
        public const string LegacyScrap = "SCRAP";
        public const string LegacyDetenida = "DETENIDA";

        public static bool EsProcesoActivo(string? estado)
        {
            return estado == PendientePrearranque ||
                   estado == DevueltoPrearranque ||
                   estado == ArranqueAutorizado ||
                   estado == PendientePrimerasPiezas ||
                   estado == AjustesSolicitados ||
                   estado == ProduccionLiberada ||
                   estado == MonitoreoActivo ||
                   estado == PendienteLiberacionCaja ||
                   estado == CajaLiberada ||
                   estado == PendienteReliberacion ||
                   estado == PendienteGP12 ||
                   estado == EnGP12 ||
                   estado == LegacyAbierta ||
                   estado == LegacyDetenida ||
                   estado == LegacyGPI2;
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

        public static bool PuedeMonitorear(string? estado)
        {
            return estado == ProduccionLiberada ||
                   estado == MonitoreoActivo;
        }

        public static bool EsEstadoFinal(string? estado)
        {
            return estado == MaterialLiberado ||
                   estado == MaterialNoConforme ||
                   estado == Cerrada;
        }
    }

    public static class CalidadMovimientos
    {
        public const string RecibidoDesdeProduccion = "RECIBIDO_DESDE_PRODUCCION";
        public const string ChecklistCalidadCapturado = "CHECKLIST_CALIDAD_CAPTURADO";
        public const string PrearranqueAutorizado = "PREARRANQUE_AUTORIZADO";
        public const string PrearranqueDevuelto = "PREARRANQUE_DEVUELTO";
        public const string PrimerasPiezasRecibidas = "PRIMERAS_PIEZAS_RECIBIDAS";
        public const string AjustesSolicitados = "AJUSTES_SOLICITADOS";
        public const string ProduccionLiberada = "PRODUCCION_LIBERADA";
        public const string MonitoreoRegistrado = "MONITOREO_REGISTRADO";
        public const string MaterialSospechoso = "MATERIAL_SOSPECHOSO";
        public const string CajaLiberada = "CAJA_LIBERADA";
        public const string EnviadoGP12 = "ENVIADO_GP12";
        public const string ReliberacionSolicitada = "RELIBERACION_SOLICITADA";
        public const string ReliberacionAutorizada = "RELIBERACION_AUTORIZADA";
        public const string ReliberacionRechazada = "RELIBERACION_RECHAZADA";
        public const string ConfiguracionInvalidada = "CONFIGURACION_INVALIDADA";
        public const string Cierre = "CIERRE";
    }
}
