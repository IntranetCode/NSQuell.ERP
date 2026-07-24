using Microsoft.AspNetCore.Mvc.Rendering;
using static ERP.NSQuell.Models.PlaneacionReleaseEstatus;

namespace ERP.NSQuell.Models
{
    public class PlaneacionReleaseIndexVm
    {
        public int ReleaseID { get; set; }
        public string? FolioRelease { get; set; }
        public int? ClienteID { get; set; }
        public string? ClienteNombre { get; set; }
        public DateTime FechaRecepcion { get; set; }
        public string? VersionRelease { get; set; }
        public int EstatusID { get; set; }
        public string? EstatusNombre { get; set; }
        public int TotalRenglones { get; set; }
        public int TotalPiezasRequeridas { get; set; }
        public int TotalPiezasAProducir { get; set; }
        public DateTime FechaCreacion { get; set; }

        public int TotalEntregas { get; set; }

        public string? FolioCliente { get; set; }

        public string? ArchivoOrigenNombre { get; set; }

        public string? PlantillaImportacion { get; set; }

        public bool ImportadoDesdeArchivo { get; set; }

    }

    public class PlaneacionReleaseCrearVm
    {
        public int? ClienteID { get; set; }
        public string? ClienteNombre { get; set; }

        public string? FolioRelease { get; set; }

        public string? FolioCliente { get; set; }

        public DateTime FechaRecepcion { get; set; } = DateTime.Today;

        public string? VersionRelease { get; set; }

        public string? ArchivoOrigenNombre { get; set; }

        public IFormFile? ArchivoRelease { get; set; }

        public string? PlantillaImportacion { get; set; }

        public bool ImportadoDesdeArchivo { get; set; }

        public string? Observaciones { get; set; }

        public int EstatusID { get; set; }

        public List<PlaneacionReleaseRenglonCrearVm> Renglones { get; set; } = new();

        public List<SelectListItem> Clientes { get; set; } = new();

        public List<SelectListItem> Partes { get; set; } = new();
    }

    public class PlaneacionReleaseRenglonCrearVm
    {
        public int Renglon { get; set; }

        public int? ParteID { get; set; }

        public string? NumeroParte { get; set; }
        public string? ReferenciaSAP { get; set; }
        public string? DesignacionDescripcionSAP { get; set; }

        public string? Observaciones { get; set; }

        public string? UnidadMedidaCliente { get; set; }

        public string? ContratoCliente { get; set; }
        public List<PlaneacionReleaseEntregaCrearVm> Entregas { get; set; } = new();
    }

    public class PlaneacionReleaseEntregaCrearVm
    {
        public int SecuenciaEntrega { get; set; }

        public DateTime? FechaRequerida { get; set; }

        public int CantidadRequerida { get; set; }

        public DateTime? FechaCarga { get; set; }
    }

    public class PlaneacionReleaseDetalleCrearVm
    {
        public int? ReleaseDetalleID { get; set; }

        public int Renglon { get; set; }

        public int? ParteID { get; set; }
        public string? NumeroParte { get; set; }
        public string? ReferenciaSAP { get; set; }
        public string? DesignacionDescripcionSAP { get; set; }

        public DateTime? FechaRequerida { get; set; }
        public int CantidadRequerida { get; set; }

        public int? PTDisponibleAlCalcular { get; set; }
        public int? ProduccionProgramadaPendiente { get; set; }

        public int? PiezasDesdePT { get; set; }
        public int? PiezasAProducir { get; set; }

        public int? MaterialID { get; set; }
        public string? MaterialCodigo { get; set; }
        public string? MaterialDescripcion { get; set; }
        public decimal? PesoBrutoPieza { get; set; }

        public decimal? MPRequeridaKg { get; set; }
        public decimal? MPDisponibleKg { get; set; }

        public string? EmbalajeCodigo { get; set; }
        public string? EmbalajeDescripcion { get; set; }
        public decimal? PiezasPorEmbalaje { get; set; }
        public decimal? EmbalajeRequerido { get; set; }
        public decimal? EmbalajeDisponible { get; set; }

        public int? MoldeID { get; set; }
        public string? MoldeCodigo { get; set; }

        public int? MaquinaSugeridaID { get; set; }
        public string? MaquinaSugeridaCodigo { get; set; }
        public string? MaquinaSugeridaNombre { get; set; }

        public int? ObjetivoHora { get; set; }
        public decimal? HorasNecesarias { get; set; }

        public DateTime? FechaInicioSugerida { get; set; }
        public DateTime? FechaFinEstimada { get; set; }

        public bool? DaTiempo { get; set; }
        public string? MensajeCapacidad { get; set; }

        public int? ProgramaProduccionID { get; set; }
        public int? SolicitudProduccionID { get; set; }

        public DateTime? FechaCarga { get; set; }
        public int EstatusID { get; set; } = PlaneacionReleaseEstatus.Capturado;
    }

    public class PlaneacionReleaseDetalleVm
    {
        public int ReleaseID { get; set; }
        public string? FolioRelease { get; set; }

        public int? ClienteID { get; set; }
        public string? ClienteNombre { get; set; }

        public DateTime FechaRecepcion { get; set; }
        public string? VersionRelease { get; set; }
        public string? ArchivoOrigenNombre { get; set; }
        public string? Observaciones { get; set; }

        public int EstatusID { get; set; }
        public string? EstatusNombre { get; set; }

        public string? FolioCliente { get; set; }

        public string? PlantillaImportacion { get; set; }

        public bool ImportadoDesdeArchivo { get; set; }

        public List<PlaneacionReleaseDetalleRenglonVm> Detalles { get; set; } = new();

        public int TotalRenglones => Detalles.Count;
        public int TotalPiezasRequeridas => Detalles.Sum(x => x.CantidadRequerida);
        public int TotalPiezasDesdePT => Detalles.Sum(x => x.PiezasDesdePT ?? 0);
        public int TotalPiezasAProducir => Detalles.Sum(x => x.PiezasAProducir ?? 0);
        public decimal TotalMPRequeridaKg => Detalles.Sum(x => x.MPRequeridaKg ?? 0);
        public decimal TotalHorasNecesarias => Detalles.Sum(x => x.HorasNecesarias ?? 0);
    }

    public class PlaneacionReleaseDetalleRenglonVm
    {
        public int ReleaseDetalleID { get; set; }
        public int Renglon { get; set; }

        public int? ParteID { get; set; }
        public string? NumeroParte { get; set; }
        public string? ReferenciaSAP { get; set; }
        public string? DesignacionDescripcionSAP { get; set; }

        public DateTime FechaRequerida { get; set; }
        public int CantidadRequerida { get; set; }

        public int? PTDisponibleAlCalcular { get; set; }
        public int? ProduccionProgramadaPendiente { get; set; }

        public int? PiezasDesdePT { get; set; }
        public int? PiezasAProducir { get; set; }

        public int? MaterialID { get; set; }
        public string? MaterialCodigo { get; set; }
        public string? MaterialDescripcion { get; set; }
        public decimal? PesoBrutoPieza { get; set; }

        public decimal? MPRequeridaKg { get; set; }
        public decimal? MPDisponibleKg { get; set; }

        public string? EmbalajeCodigo { get; set; }
        public string? EmbalajeDescripcion { get; set; }
        public decimal? PiezasPorEmbalaje { get; set; }
        public decimal? EmbalajeRequerido { get; set; }
        public decimal? EmbalajeDisponible { get; set; }

        public int? MoldeID { get; set; }
        public string? MoldeCodigo { get; set; }

        public int? MaquinaSugeridaID { get; set; }
        public string? MaquinaSugeridaCodigo { get; set; }
        public string? MaquinaSugeridaNombre { get; set; }

        public int? ObjetivoHora { get; set; }
        public decimal? HorasNecesarias { get; set; }

        public DateTime? FechaInicioSugerida { get; set; }
        public DateTime? FechaFinEstimada { get; set; }

        public bool? DaTiempo { get; set; }
        public string? MensajeCapacidad { get; set; }

        public int? ProgramaProduccionID { get; set; }
        public int? SolicitudProduccionID { get; set; }

        public int EstatusID { get; set; }

        public int? ReleaseRenglonID { get; set; }
        public int? SecuenciaEntrega { get; set; }

        public DateTime? FechaCarga { get; set; }

        public string? UnidadMedidaCliente { get; set; }

        public string? ContratoCliente { get; set; }
    }


    public class PlaneacionNecesidadFiltroVm
    {
        public int? ClienteID { get; set; }
        public int? ParteID { get; set; }

        public DateTime? FechaDesde { get; set; }
        public DateTime? FechaHasta { get; set; }

        public bool SoloPendientes { get; set; }
        public bool SoloSinCapacidad { get; set; }
        public bool SoloSinMP { get; set; }

        public List<PlaneacionNecesidadVm> Necesidades { get; set; } = new();

        public List<SelectListItem> Clientes { get; set; } = new();
        public List<SelectListItem> Partes { get; set; } = new();

        public int TotalRenglones => Necesidades.Count;
        public int TotalRequerido => Necesidades.Sum(x => x.CantidadRequerida);
        public int TotalDesdePT => Necesidades.Sum(x => x.PiezasDesdePT ?? 0);
        public int TotalAProducir => Necesidades.Sum(x => x.PiezasAProducir ?? 0);
        public decimal TotalMPRequerida => Necesidades.Sum(x => x.MPRequeridaKg ?? 0);
        public decimal TotalHoras => Necesidades.Sum(x => x.HorasNecesarias ?? 0);

        public List<PlaneacionNecesidadPeriodoVm> ResumenPeriodos { get; set; } = new();
    }

    public class PlaneacionNecesidadVm
    {
        public int ReleaseID { get; set; }
        public int ReleaseDetalleID { get; set; }

        public string? FolioRelease { get; set; }

        public int? ClienteID { get; set; }
        public string? ClienteNombre { get; set; }

        public int Renglon { get; set; }

        public int? ParteID { get; set; }
        public string? NumeroParte { get; set; }
        public string? ReferenciaSAP { get; set; }
        public string? DesignacionDescripcionSAP { get; set; }

        public DateTime FechaRecepcion { get; set; }
        public DateTime FechaRequerida { get; set; }

        public int CantidadRequerida { get; set; }

        public int? PTDisponibleAlCalcular { get; set; }
        public int? ProduccionProgramadaPendiente { get; set; }

        public int? PiezasDesdePT { get; set; }
        public int? PiezasAProducir { get; set; }

        public int? MaterialID { get; set; }
        public string? MaterialCodigo { get; set; }
        public string? MaterialDescripcion { get; set; }

        public decimal? MPRequeridaKg { get; set; }
        public decimal? MPDisponibleKg { get; set; }

        public string? EmbalajeCodigo { get; set; }
        public string? EmbalajeDescripcion { get; set; }
        public decimal? EmbalajeRequerido { get; set; }
        public decimal? EmbalajeDisponible { get; set; }

        public int? MaquinaSugeridaID { get; set; }
        public string? MaquinaSugeridaCodigo { get; set; }
        public string? MaquinaSugeridaNombre { get; set; }

        public int? MoldeID { get; set; }
        public string? MoldeCodigo { get; set; }

        public int? ObjetivoHora { get; set; }
        public decimal? HorasNecesarias { get; set; }

        public DateTime? FechaInicioSugerida { get; set; }
        public DateTime? FechaFinEstimada { get; set; }

        public bool? DaTiempo { get; set; }
        public string? MensajeCapacidad { get; set; }

        public int EstatusID { get; set; }

        public int? ProgramaProduccionID { get; set; }
        public bool YaProgramado => ProgramaProduccionID.HasValue;

        public bool TieneMPInsuficiente =>
            (MPRequeridaKg ?? 0) > 0 &&
            (MPDisponibleKg ?? 0) < (MPRequeridaKg ?? 0);

        public bool TieneEmbalajeInsuficiente =>
            (EmbalajeRequerido ?? 0) > 0 &&
            (EmbalajeDisponible ?? 0) < (EmbalajeRequerido ?? 0);
    }


    public static class PlaneacionReleaseEstatus
    {
        public const int Capturado = 1;
        public const int Calculado = 2;
        public const int Programado = 3;
        public const int ConOF = 4;
        public const int Cerrado = 9;
        public const int Cancelado = 99;

        public static string Nombre(int estatusId)
        {
            return estatusId switch
            {
                Capturado => "Capturado",
                Calculado => "Calculado",
                Programado => "Programado",
                ConOF => "Con OF",
                Cerrado => "Cerrado",
                Cancelado => "Cancelado",
                _ => "Desconocido"
            };
        }

        public class PlaneacionNecesidadPeriodoVm
        {
            public string Periodo { get; set; } = string.Empty;

            public DateTime FechaDesde { get; set; }
            public DateTime FechaHasta { get; set; }

            public int Renglones { get; set; }

            public int CantidadRequerida { get; set; }
            public int PiezasDesdePT { get; set; }
            public int ProduccionProgramadaPendiente { get; set; }
            public int PiezasAProducir { get; set; }

            public decimal MPRequeridaKg { get; set; }
            public decimal HorasNecesarias { get; set; }
        }
    }
}