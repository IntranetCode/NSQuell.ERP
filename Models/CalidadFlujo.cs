using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ERP.NSQuell.Models
{
    [Table("Calidad_PrimerasPiezasIntentos")]
    public class CalidadPrimeraPiezaIntento
    {
        [Key]
        public int IntentoID { get; set; }

        public int InspeccionID { get; set; }
        public int NumeroIntento { get; set; }

        public DateTime FechaInicio { get; set; } = DateTime.Now;
        public DateTime? FechaFin { get; set; }

        public bool CincoDisparosSegregados { get; set; }

        [Range(0, 5)]
        public int CantidadDisparosPresentados { get; set; }

        public bool? ValidacionDimensional { get; set; }
        public bool? ValidacionApariencia { get; set; }
        public bool? ValidacionGauge { get; set; }
        public bool? ValidacionConductividad { get; set; }

        [Required]
        [StringLength(20)]
        public string Resultado { get; set; } = CalidadResultadoIntento.Pendiente;

        public bool AjusteSolicitado { get; set; }

        [StringLength(1000)]
        public string? Observaciones { get; set; }

        public int? UsuarioCalidadID { get; set; }
        public int? UsuarioCreacionID { get; set; }
        public DateTime FechaCreacion { get; set; } = DateTime.Now;
        public int? UsuarioModificacionID { get; set; }
        public DateTime? FechaModificacion { get; set; }
        public bool Activo { get; set; } = true;

        [ForeignKey(nameof(InspeccionID))]
        public CalidadInspeccion? Inspeccion { get; set; }
    }

    [Table("Calidad_MonitoreosProceso")]
    public class CalidadMonitoreoProceso
    {
        [Key]
        public int MonitoreoID { get; set; }

        public int InspeccionID { get; set; }
        public int EjecucionProduccionID { get; set; }
        public int? RegistroHoraID { get; set; }

        public int NumeroHora { get; set; }
        public DateTime FechaHoraProgramada { get; set; }
        public DateTime? FechaHoraRevision { get; set; }

        public int CantidadProducidaPeriodo { get; set; }
        public int CantidadRevisadaMuestra { get; set; }

        [Required]
        [StringLength(20)]
        public string Resultado { get; set; } = CalidadResultadoMonitoreo.Pendiente;

        [StringLength(20)]
        public string? DefectoCodigo { get; set; }

        [StringLength(500)]
        public string? DefectoDescripcion { get; set; }

        public int CantidadSospechosa { get; set; }
        public int CantidadNoRecuperable { get; set; }

        public bool RequiereSeleccion { get; set; }
        public bool RequiereRetrabajo { get; set; }

        [StringLength(20)]
        public string? ResponsableRetrabajo { get; set; }

        [StringLength(1000)]
        public string? Observaciones { get; set; }

        public int? UsuarioCalidadID { get; set; }
        public int? UsuarioCreacionID { get; set; }
        public DateTime FechaCreacion { get; set; } = DateTime.Now;
        public int? UsuarioModificacionID { get; set; }
        public DateTime? FechaModificacion { get; set; }
        public bool Activo { get; set; } = true;

        [ForeignKey(nameof(InspeccionID))]
        public CalidadInspeccion? Inspeccion { get; set; }

        public ICollection<CalidadDisposicionMaterial> Disposiciones { get; set; } =
            new List<CalidadDisposicionMaterial>();
    }

  
    [Table("Calidad_DisposicionesMaterial")]
    public class CalidadDisposicionMaterial
    {
        [Key]
        public int DisposicionID { get; set; }
        public int InspeccionID { get; set; }
        public int? MonitoreoID { get; set; }
        [Required]
        [StringLength(20)]
        public string TipoMaterial { get; set; } = CalidadTipoMaterial.Sospechoso;
        public int CantidadAfectada { get; set; }
        [StringLength(20)]
        public string? Etiqueta { get; set; }
        [Required]
        [StringLength(20)]
        public string Disposicion { get; set; } = CalidadTipoDisposicion.Pendiente;
        [StringLength(250)]
        public string? Responsable { get; set; }
        public int? DepartamentoResponsableID { get; set; }
        public int? UsuarioResponsableID { get; set; }
        [Required]
        [StringLength(30)]
        public string EstadoTratamiento { get; set; } = CalidadEstadoTratamiento.PendienteAsignacion;
        public DateTime? FechaInicioTratamiento { get; set; }
        public DateTime? FechaFinTratamiento { get; set; }
        public DateTime FechaInicio { get; set; } = DateTime.Now;
        public DateTime? FechaFin { get; set; }
        public int CantidadLiberada { get; set; }
        public int CantidadScrap { get; set; }
        [Required]
        [StringLength(20)]
        public string ResultadoFinal { get; set; } = CalidadResultadoDisposicion.Pendiente;
        [StringLength(1000)]
        public string? Observaciones { get; set; }
        public int? UsuarioCreacionID { get; set; }
        public DateTime FechaCreacion { get; set; } = DateTime.Now;
        public int? UsuarioModificacionID { get; set; }
        public DateTime? FechaModificacion { get; set; }
        public bool Activo { get; set; } = true;
        [ForeignKey(nameof(InspeccionID))]
        public CalidadInspeccion? Inspeccion { get; set; }
        [ForeignKey(nameof(MonitoreoID))]
        public CalidadMonitoreoProceso? Monitoreo { get; set; }
    }

    public static class CalidadEstadoTratamiento
    {
        public const string PendienteAsignacion = "PENDIENTE_ASIGNACION";
        public const string Asignada = "ASIGNADA";
        public const string EnProceso = "EN_PROCESO";
        public const string PendienteReinspeccion = "PENDIENTE_REINSPECCION";
        public const string Concluida = "CONCLUIDA";
    }

    [Table("Calidad_CajasLiberadas")]
    public class CalidadCajaLiberada
    {
        [Key]
        public int CajaLiberadaID { get; set; }

        public int InspeccionID { get; set; }
        public int EjecucionProduccionID { get; set; }
        public long? CajaProduccionID { get; set; }

        [Required]
        [StringLength(100)]
        public string FolioCaja { get; set; } = string.Empty;

        public int CantidadPiezas { get; set; }
        public bool EstandarPackCumple { get; set; }
        public bool EtiquetaProductoCorrecta { get; set; }

        [StringLength(50)]
        public string? NumeroOperadorEtiqueta { get; set; }

        public bool TecnicoConfirmoInformacion { get; set; }

        public DateTime? FechaCierreProduccion { get; set; }
        public int? UsuarioCierreProduccionID { get; set; }

        public DateTime? FechaValidacionCalidad { get; set; }
        public int? UsuarioValidacionCalidadID { get; set; }

        [StringLength(100)]
        public string? EtiquetaLiberacion { get; set; }

        [StringLength(100)]
        public string? Tarima { get; set; }

        [StringLength(20)]
        public string? Destino { get; set; }

        [Required]
        [StringLength(20)]
        public string Estado { get; set; } = CalidadEstadoCaja.Pendiente;

        [StringLength(1000)]
        public string? Observaciones { get; set; }

        public int? UsuarioCreacionID { get; set; }
        public DateTime FechaCreacion { get; set; } = DateTime.Now;
        public int? UsuarioModificacionID { get; set; }
        public DateTime? FechaModificacion { get; set; }
        public bool Activo { get; set; } = true;

        [ForeignKey(nameof(InspeccionID))]
        public CalidadInspeccion? Inspeccion { get; set; }

        public CalidadGP12? RegistroGP12 { get; set; }
    }

    [Table("Calidad_MuestrasResguardo")]
    public class CalidadMuestraResguardo
    {
        [Key]
        public int MuestraResguardoID { get; set; }

        public int InspeccionID { get; set; }
        public int EjecucionProduccionID { get; set; }

        [Required]
        [StringLength(30)]
        public string Momento { get; set; } = CalidadMomentoMuestra.FinProduccion;

        public int CantidadDisparos { get; set; } = 2;
        public bool MuestraCalidadConfirmada { get; set; }
        public bool MuestraProduccionConfirmada { get; set; }

        [StringLength(250)]
        public string? UbicacionCalidad { get; set; }

        [StringLength(250)]
        public string? UbicacionProduccion { get; set; }

        public DateTime? FechaResguardo { get; set; }
        public int? UsuarioResponsableID { get; set; }

        [StringLength(1000)]
        public string? Observaciones { get; set; }

        public int? UsuarioCreacionID { get; set; }
        public DateTime FechaCreacion { get; set; } = DateTime.Now;
        public int? UsuarioModificacionID { get; set; }
        public DateTime? FechaModificacion { get; set; }
        public bool Activo { get; set; } = true;

        [ForeignKey(nameof(InspeccionID))]
        public CalidadInspeccion? Inspeccion { get; set; }
    }

    [Table("Calidad_Reliberaciones")]
    public class CalidadReliberacion
    {
        [Key]
        public int ReliberacionID { get; set; }

        public int InspeccionID { get; set; }
        public int EjecucionProduccionID { get; set; }
        public int ParoID { get; set; }
        public int NumeroReliberacion { get; set; }

        [StringLength(500)]
        public string? Motivo { get; set; }

        public DateTime FechaSolicitud { get; set; } = DateTime.Now;
        public int? UsuarioSolicitudID { get; set; }

        public DateTime? FechaValidacion { get; set; }
        public int? UsuarioCalidadID { get; set; }

        [Required]
        [StringLength(20)]
        public string Resultado { get; set; } = CalidadResultadoReliberacion.Pendiente;

        [StringLength(1000)]
        public string? Observaciones { get; set; }

        public int? UsuarioCreacionID { get; set; }
        public DateTime FechaCreacion { get; set; } = DateTime.Now;
        public int? UsuarioModificacionID { get; set; }
        public DateTime? FechaModificacion { get; set; }
        public bool Activo { get; set; } = true;

        [ForeignKey(nameof(InspeccionID))]
        public CalidadInspeccion? Inspeccion { get; set; }
    }

    [Table("Calidad_CatalogoDefectos")]
    public class CalidadCatalogoDefecto
    {
        [Key]
        public int CatalogoDefectoID { get; set; }

        [Required]
        [StringLength(10)]
        public string Codigo { get; set; } = string.Empty;

        [Required]
        [StringLength(150)]
        public string Nombre { get; set; } = string.Empty;

        public bool Activo { get; set; } = true;
        public DateTime FechaCreacion { get; set; } = DateTime.Now;

        public ICollection<CalidadGP12Defecto> DefectosGP12 { get; set; } =
            new List<CalidadGP12Defecto>();
    }

    [Table("Calidad_GP12")]
    public class CalidadGP12
    {
        [Key]
        public int GP12ID { get; set; }

        public int InspeccionID { get; set; }
        public int CajaLiberadaID { get; set; }

        public DateTime FechaEntrada { get; set; } = DateTime.Now;
        public int CantidadEntrada { get; set; }

        [StringLength(500)]
        public string? Motivo { get; set; }

        [Required]
        [StringLength(30)]
        public string Estado { get; set; } = CalidadEstadoGP12.EnInspeccion;

        public DateTime? FechaSalida { get; set; }
        public int? CantidadSalida { get; set; }

        public int? UsuarioEntradaID { get; set; }
        public int? UsuarioSalidaID { get; set; }

        [StringLength(1000)]
        public string? Observaciones { get; set; }

        public int? UsuarioCreacionID { get; set; }
        public DateTime FechaCreacion { get; set; } = DateTime.Now;
        public int? UsuarioModificacionID { get; set; }
        public DateTime? FechaModificacion { get; set; }
        public bool Activo { get; set; } = true;

        [ForeignKey(nameof(InspeccionID))]
        public CalidadInspeccion? Inspeccion { get; set; }

        [ForeignKey(nameof(CajaLiberadaID))]
        public CalidadCajaLiberada? CajaLiberada { get; set; }

        public ICollection<CalidadGP12Revision> Revisiones { get; set; } =
            new List<CalidadGP12Revision>();
    }

    [Table("Calidad_GP12_Revisiones")]
    public class CalidadGP12Revision
    {
        [Key]
        public int RevisionGP12ID { get; set; }

        public int GP12ID { get; set; }
        public int NumeroRevision { get; set; }
        public DateTime FechaRevision { get; set; } = DateTime.Now;

        public int CantidadRevisada { get; set; }
        public int CantidadOK { get; set; }
        public int CantidadNOK { get; set; }

        [Required]
        [StringLength(10)]
        public string Resultado { get; set; } = CalidadResultadoGP12.Nok;

        [StringLength(1000)]
        public string? Observaciones { get; set; }

        public int? UsuarioCalidadID { get; set; }
        public int? UsuarioCreacionID { get; set; }
        public DateTime FechaCreacion { get; set; } = DateTime.Now;
        public bool Activo { get; set; } = true;

        [ForeignKey(nameof(GP12ID))]
        public CalidadGP12? GP12 { get; set; }

        public ICollection<CalidadGP12Defecto> Defectos { get; set; } =
            new List<CalidadGP12Defecto>();
    }

    [Table("Calidad_GP12_Defectos")]
    public class CalidadGP12Defecto
    {
        [Key]
        public int DefectoGP12ID { get; set; }

        public int RevisionGP12ID { get; set; }
        public int CatalogoDefectoID { get; set; }
        public int Cantidad { get; set; }

        [StringLength(500)]
        public string? Observaciones { get; set; }

        public int? UsuarioCreacionID { get; set; }
        public DateTime FechaCreacion { get; set; } = DateTime.Now;
        public bool Activo { get; set; } = true;

        [ForeignKey(nameof(RevisionGP12ID))]
        public CalidadGP12Revision? Revision { get; set; }

        [ForeignKey(nameof(CatalogoDefectoID))]
        public CalidadCatalogoDefecto? CatalogoDefecto { get; set; }
    }

    public static class CalidadResultadoIntento
    {
        public const string Pendiente = "PENDIENTE";
        public const string Ok = "OK";
        public const string Nok = "NOK";
        public const string Cancelado = "CANCELADO";
    }

    public static class CalidadResultadoMonitoreo
    {
        public const string Pendiente = "PENDIENTE";
        public const string Conforme = "CONFORME";
        public const string Sospechoso = "SOSPECHOSO";
        public const string NoConforme = "NO_CONFORME";
        public const string Reinspeccion = "REINSPECCION";
    }

    public static class CalidadResponsable
    {
        public const string Produccion = "PRODUCCION";
        public const string Calidad = "CALIDAD";
    }

    public static class CalidadTipoMaterial
    {
        public const string Sospechoso = "SOSPECHOSO";
        public const string NoConforme = "NO_CONFORME";
    }

    public static class CalidadTipoDisposicion
    {
        public const string Pendiente = "PENDIENTE";
        public const string Seleccion = "SELECCION";
        public const string Retrabajo = "RETRABAJO";
        public const string Scrap = "SCRAP";
        public const string Liberado = "LIBERADO";
    }

    public static class CalidadResultadoDisposicion
    {
        public const string Pendiente = "PENDIENTE";
        public const string Liberado = "LIBERADO";
        public const string Scrap = "SCRAP";
        public const string Cancelado = "CANCELADO";
    }

    public static class CalidadDestinoCaja
    {
        public const string Almacen = "ALMACEN";
        public const string GP12 = "GP12";
    }

    public static class CalidadEstadoCaja
    {
        public const string Pendiente = "PENDIENTE";
        public const string Liberada = "LIBERADA";
        public const string EnGP12 = "EN_GP12";
        public const string Devuelta = "DEVUELTA";
        public const string Entregada = "ENTREGADA";
        public const string Cancelada = "CANCELADA";
    }

    public static class CalidadDecisionCaja
    {
        public const string Liberar = "LIBERAR";
        public const string GP12 = "GP12";
        public const string Devolver = "DEVOLVER";

        public static bool EsValida(string? decision)
        {
            if (string.IsNullOrWhiteSpace(decision))
                return false;

            var valor = decision.Trim().ToUpperInvariant();

            return valor == Liberar ||
                   valor == GP12 ||
                   valor == Devolver;
        }
    }

    public static class CalidadResultadoCaja
    {
        public const string Liberada = "LIBERADA";
        public const string GP12 = "GP12";
        public const string Devuelta = "DEVUELTA";
        public const string LiberadaGP12 = "LIBERADA_GP12";
        public const string GP12Nok = "GP12_NOK";
    }

    public static class CalidadMomentoMuestra
    {
        public const string FinProduccion = "FIN_PRODUCCION";
        public const string CambioMolde = "CAMBIO_MOLDE";
    }

    public static class CalidadResultadoReliberacion
    {
        public const string Pendiente = "PENDIENTE";
        public const string Autorizada = "AUTORIZADA";
        public const string Rechazada = "RECHAZADA";
        public const string Cancelada = "CANCELADA";
    }

    public static class CalidadEstadoGP12
    {
        public const string EnInspeccion = "EN_INSPECCION";
        public const string NokReinspeccion = "NOK_REINSPECCION";
        public const string Liberado = "LIBERADO";
        public const string Cerrado = "CERRADO";
        public const string Cancelado = "CANCELADO";
    }

    public static class CalidadResultadoGP12
    {
        public const string Ok = "OK";
        public const string Nok = "NOK";
    }
    public static class CalidadChecklistResultado
    {
        public const string Ok = "OK";
        public const string Nok = "NOK";
        public const string NoAplica = "NA";

        public static bool EsValido(string? resultado)
        {
            if (string.IsNullOrWhiteSpace(resultado))
                return false;

            var valor = resultado.Trim().ToUpperInvariant();

            return valor == Ok ||
                   valor == Nok ||
                   valor == NoAplica ||
                   valor == "N/A";
        }
    }

    public static class CalidadTipoProceso
    {
        public const string LiberacionPrearranque =
            "LIBERACIÓN DE PREARRANQUE";

        public const string ReliberacionParoMayor15 =
            "RELIBERACIÓN POR PARO MAYOR A 15 MIN";

        public static bool EsReliberacion(string? proceso)
        {
            if (string.IsNullOrWhiteSpace(proceso))
                return false;

            var valor = proceso
                .Trim()
                .ToUpperInvariant()
                .Replace("Á", "A")
                .Replace("É", "E")
                .Replace("Í", "I")
                .Replace("Ó", "O")
                .Replace("Ú", "U");

            return valor.Contains("RELIBERACION", StringComparison.Ordinal);
        }
    }

}
