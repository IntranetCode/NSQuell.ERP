using Microsoft.AspNetCore.Mvc.Rendering;

namespace ERP.NSQuell.Models
{
    public class PlaneacionOFIndexVm
    {
        public int SolicitudProduccionID { get; set; }

        public string? FolioSolicitud { get; set; }
        public string? NumeroOFRecibida { get; set; }

        public DateTime FechaSolicitud { get; set; }
        public DateTime? FechaRequerida { get; set; }

        public DateTime? FechaInicioPlaneada { get; set; }
        public DateTime? FechaFinPlaneada { get; set; }

        public string? Cliente { get; set; }

        public string Prioridad { get; set; } = "Normal";

        public int EstatusID { get; set; }
        public string EstatusNombre { get; set; } = string.Empty;

        public int TotalRenglones { get; set; }
        public int TotalPiezas { get; set; }

        public string? ResponsablePlaneacionNombre { get; set; }
    }

    public class PlaneacionOFCrearVm
    {
        public string? FolioSolicitud { get; set; }
        public string? NumeroOFRecibida { get; set; }

        public DateTime FechaSolicitud { get; set; } = DateTime.Today;
        public DateTime? FechaRequerida { get; set; }

        public DateTime? FechaInicioPlaneada { get; set; }
        public DateTime? FechaFinPlaneada { get; set; }

        public int? ClienteID { get; set; }
        public string? ClienteNombre { get; set; }

        public string? OrigenSolicitud { get; set; } = "Dirección";
        public string Prioridad { get; set; } = "Normal";

        public string? NotasGenerales { get; set; }

        public List<PlaneacionOFDetalleCrearVm> Detalles { get; set; } = new();

        public List<SelectListItem> Clientes { get; set; } = new();
        public List<SelectListItem> Partes { get; set; } = new();
        public List<SelectListItem> Moldes { get; set; } = new();
        public List<SelectListItem> Maquinas { get; set; } = new();
    }

    public class PlaneacionOFDetalleCrearVm
    {
        public int Renglon { get; set; }

        public int? ParteID { get; set; }
        public int? MoldeID { get; set; }

        public string ReferenciaSAP { get; set; } = string.Empty;
        public string DesignacionDescripcionSAP { get; set; } = string.Empty;

        public int CantidadPiezas { get; set; }

        public decimal? HorasPlaneadas { get; set; }

        public string? NumeroMoldeTexto { get; set; }

        public string? Color { get; set; }
        public int? Cavidades { get; set; }
        public int? ObjetivoHora { get; set; }
        public int? PiezasPorCaja { get; set; }

        // Datos técnicos autollenados desde ERP_ParteDatosTecnicos
        public string? Ciclo { get; set; }

        public string? TipoSecado { get; set; }
        public decimal? HorasSecado { get; set; }

        public decimal? PesoBrutoPieza { get; set; }

        public string? MaterialCodigo { get; set; }
        public string? MaterialDescripcion { get; set; }

        public string? EmbalajeCodigo { get; set; }
        public string? EmbalajeDescripcion { get; set; }

        public decimal? PiezasPorEmbalaje { get; set; }

        // Calculados
        public decimal? CantidadEmbalajes { get; set; }
        public decimal? CantidadMpKg { get; set; }

        public TimeSpan? Cambio { get; set; }
        public TimeSpan? Arranque { get; set; }

        public string? Notas { get; set; }

        public List<PlaneacionOFAsignacionMaquinaCrearVm> AsignacionesMaquina { get; set; } = new();
    }

    public class PlaneacionOFAsignacionMaquinaCrearVm
    {
        public int MaquinaID { get; set; }
        public int? MoldeID { get; set; }

        public int CantidadAsignada { get; set; }

        public decimal? HorasEstimadas { get; set; }

        public int Secuencia { get; set; } = 1;

        public string? CondicionProduccion { get; set; }

        public DateTime? FechaProgramadaTentativa { get; set; }

        public TimeSpan? HoraInicioTentativa { get; set; }
        public TimeSpan? HoraFinTentativa { get; set; }

        public string? Observaciones { get; set; }
    }

    // ============================================================
    // VIEWMODEL DETALLE
    // ============================================================
    public class PlaneacionOFDetalleVm
    {
        public int SolicitudProduccionID { get; set; }

        public string? FolioSolicitud { get; set; }
        public string? NumeroOFRecibida { get; set; }

        public DateTime FechaSolicitud { get; set; }
        public DateTime? FechaRequerida { get; set; }

        public DateTime? FechaInicioPlaneada { get; set; }
        public DateTime? FechaFinPlaneada { get; set; }

        public string? Cliente { get; set; }

        public string Prioridad { get; set; } = "Normal";

        public int EstatusID { get; set; }
        public string EstatusNombre { get; set; } = string.Empty;

        public string? NotasGenerales { get; set; }

        public string? ResponsablePlaneacionNombre { get; set; }

        public List<PlaneacionOFDetalleRenglonVm> Detalles { get; set; } = new();
        public List<PlaneacionOFHistorialVm> Historial { get; set; } = new();
    }

    public class PlaneacionOFDetalleRenglonVm
    {
        public int SolicitudProduccionDetalleID { get; set; }

        public int Renglon { get; set; }

        public string ReferenciaSAP { get; set; } = string.Empty;
        public string DesignacionDescripcionSAP { get; set; } = string.Empty;

        public int CantidadPiezas { get; set; }

        public decimal? HorasPlaneadas { get; set; }

        public string? Molde { get; set; }

        public string? Color { get; set; }
        public int? Cavidades { get; set; }
        public int? ObjetivoHora { get; set; }
        public int? PiezasPorCaja { get; set; }

        public string? Ciclo { get; set; }

        public string? TipoSecado { get; set; }
        public decimal? HorasSecado { get; set; }

        public decimal? PesoBrutoPieza { get; set; }

        public string? MaterialCodigo { get; set; }
        public string? MaterialDescripcion { get; set; }

        public string? EmbalajeCodigo { get; set; }
        public string? EmbalajeDescripcion { get; set; }

        public decimal? PiezasPorEmbalaje { get; set; }
        public decimal? CantidadEmbalajes { get; set; }
        public decimal? CantidadMpKg { get; set; }

        public TimeSpan? Cambio { get; set; }
        public TimeSpan? Arranque { get; set; }

        public string? Notas { get; set; }

        public List<PlaneacionOFAsignacionMaquinaVm> AsignacionesMaquina { get; set; } = new();
    }

    public class PlaneacionOFAsignacionMaquinaVm
    {
        public int AsignacionMaquinaID { get; set; }

        public string Maquina { get; set; } = string.Empty;
        public string? Molde { get; set; }

        public int CantidadAsignada { get; set; }
        public decimal? HorasEstimadas { get; set; }

        public int Secuencia { get; set; }

        public string? CondicionProduccion { get; set; }

        public DateTime? FechaProgramadaTentativa { get; set; }

        public TimeSpan? HoraInicioTentativa { get; set; }
        public TimeSpan? HoraFinTentativa { get; set; }

        public string? Observaciones { get; set; }
    }

    public class PlaneacionOFHistorialVm
    {
        public DateTime FechaMovimiento { get; set; }

        public string Movimiento { get; set; } = string.Empty;
        public string? Comentario { get; set; }

        public int? EstatusAnteriorID { get; set; }
        public int EstatusNuevoID { get; set; }

        public string Usuario { get; set; } = string.Empty;
    }

    // ============================================================
    // RESPUESTA AJAX PARA AUTOLLENADO
    // ============================================================
    public class PlaneacionOFParteInfoVm
    {
        public int ParteID { get; set; }

        public int? ClienteID { get; set; }
        public string? ClienteNombre { get; set; }

        public string NumeroParte { get; set; } = string.Empty;
        public string? ReferenciaSAP { get; set; }

        public string Descripcion { get; set; } = string.Empty;
        public string? Designacion { get; set; }

        public string? Color { get; set; }
        public int? Cavidades { get; set; }

        public int? ObjetivoHora { get; set; }
        public int? PiezasPorCaja { get; set; }

        public int? MoldePrincipalID { get; set; }
        public string? MoldeCodigo { get; set; }

        public int? MaquinaPrincipalID { get; set; }
        public int? MaquinaSustitutaID { get; set; }

        public string? Ciclo { get; set; }

        public string? TipoSecado { get; set; }
        public decimal? HorasSecado { get; set; }

        public decimal? PesoBrutoPieza { get; set; }

        public string? MaterialCodigo { get; set; }
        public string? MaterialDescripcion { get; set; }

        public string? EmbalajeCodigo { get; set; }
        public string? EmbalajeDescripcion { get; set; }

        public decimal? PiezasPorEmbalaje { get; set; }
    }

    // ============================================================
    // HELPERS
    
    public static class PlaneacionOFEstatus
    {
        public const int Capturada = 1;
        public const int EnRevisionPlaneacion = 2;
        public const int PendienteValidacionMP = 3;
        public const int MPInsuficiente = 4;
        public const int CompraMPEnProceso = 5;
        public const int MPRecibida = 6;
        public const int MPLiberadaCalidad = 7;
        public const int ListaProduccion = 8;
        public const int EnProduccion = 9;
        public const int Cerrada = 10;
        public const int Cancelada = 11;

        public static string Nombre(int estatusID)
        {
            return estatusID switch
            {
                Capturada => "Capturada",
                EnRevisionPlaneacion => "En revisión de Planeación",
                PendienteValidacionMP => "Pendiente validación de MP",
                MPInsuficiente => "MP insuficiente / Requiere compra",
                CompraMPEnProceso => "Compra de MP en proceso",
                MPRecibida => "MP recibida en almacén",
                MPLiberadaCalidad => "MP liberada por Calidad",
                ListaProduccion => "Lista para producción",
                EnProduccion => "En producción",
                Cerrada => "Cerrada",
                Cancelada => "Cancelada",
                _ => "Sin estatus"
            };
        }
    }

    public static class PlaneacionOFCondicion
    {
        public const string TerminarProduccion = "T.P";
        public const string InterrumpirProduccion = "I.P";

        public static string Nombre(string? condicion)
        {
            return condicion switch
            {
                TerminarProduccion => "T.P - Terminar producción actual",
                InterrumpirProduccion => "I.P - Interrumpir producción actual",
                _ => "Sin condición"
            };
        }
    }
}