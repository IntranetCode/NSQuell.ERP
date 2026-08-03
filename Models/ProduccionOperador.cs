using System;
using System.Collections.Generic;

namespace ERP.NSQuell.Models
{
    public sealed class ProduccionOperadorCajasVm
    {
        public int EjecucionProduccionID { get; set; }
        public int ProgramaProduccionID { get; set; }
        public int? SolicitudProduccionID { get; set; }

        public string? FolioSolicitud { get; set; }
        public string? NumeroOFRecibida { get; set; }

        public string? ClienteNombre { get; set; }

        public string? MaquinaCodigo { get; set; }
        public string? MaquinaNombre { get; set; }

        public string? NumeroParte { get; set; }
        public string? ReferenciaSAP { get; set; }
        public string? DescripcionParte { get; set; }

        public string? MoldeCodigo { get; set; }

        public string? MaterialCodigo { get; set; }
        public string? MaterialDescripcion { get; set; }

        public string? EmbalajeCodigo { get; set; }
        public string? EmbalajeDescripcion { get; set; }

        public int CantidadPlaneada { get; set; }
        public int CantidadOKTotal { get; set; }
        public int CantidadSospechosaTotal { get; set; }
        public int CantidadScrapTotal { get; set; }

        public int CantidadOKEnCajas { get; set; }
        public int CantidadSospechosaEnCajas { get; set; }
        public int CantidadScrapEnCajas { get; set; }
        public int CantidadRetencionEnCajas { get; set; }

        public int SiguienteNumeroCaja { get; set; }

        public bool PuedeFormarCaja { get; set; }
        public bool TieneParoAbierto { get; set; }
        public int EstatusID { get; set; }

        public List<ProduccionOperadorCajaVm> Cajas { get; set; } =
            new List<ProduccionOperadorCajaVm>();

        public int DisponibleOK
        {
            get
            {
                var disponible = CantidadOKTotal - CantidadOKEnCajas;
                return disponible < 0 ? 0 : disponible;
            }
        }

        public int DisponibleSospechoso
        {
            get
            {
                var disponible = CantidadSospechosaTotal - CantidadSospechosaEnCajas;
                return disponible < 0 ? 0 : disponible;
            }
        }

        public int DisponibleScrap
        {
            get
            {
                var disponible = CantidadScrapTotal - CantidadScrapEnCajas;
                return disponible < 0 ? 0 : disponible;
            }
        }

        public int TotalEnCajas
        {
            get
            {
                return CantidadOKEnCajas +
                       CantidadSospechosaEnCajas +
                       CantidadScrapEnCajas +
                       CantidadRetencionEnCajas;
            }
        }
    }

    public sealed class ProduccionOperadorCajaVm
    {
        public int CajaProduccionID { get; set; }
        public int EjecucionProduccionID { get; set; }
        public int ProgramaProduccionID { get; set; }

        public int NumeroCaja { get; set; }
        public string? FolioCaja { get; set; }

        public int CantidadPiezas { get; set; }
        public string TipoCaja { get; set; } = "OK";

        public string? LoteMaterial { get; set; }
        public string? EtiquetaFolio { get; set; }

        public bool EtiquetaVerde { get; set; }

        public int EstadoCajaID { get; set; }
        public string EstadoCajaNombre { get; set; } = "Formada en Producción";

        public DateTime FechaFormacion { get; set; }
        public int? UsuarioFormacionID { get; set; }

        public DateTime? FechaSolicitudCalidad { get; set; }
        public int? UsuarioSolicitudCalidadID { get; set; }

        public DateTime? FechaLiberacionCalidad { get; set; }
        public int? UsuarioCalidadID { get; set; }

        public string? ResultadoCalidad { get; set; }
        public string? MotivoCalidad { get; set; }

        public DateTime? FechaZonaVerde { get; set; }
        public int? UsuarioZonaVerdeID { get; set; }

        public DateTime? FechaSalidaProduccion { get; set; }
        public int? UsuarioSalidaProduccionID { get; set; }

        public DateTime? FechaRecepcionAlmacen { get; set; }
        public int? UsuarioAlmacenID { get; set; }

        public string? Observaciones { get; set; }

        public bool EstaFormada
        {
            get { return EstadoCajaID == 1; }
        }

        public bool EstaPendienteCalidad
        {
            get { return EstadoCajaID == 2; }
        }

        public bool EstaLiberadaCalidad
        {
            get { return EstadoCajaID == 3 && EtiquetaVerde; }
        }

        public bool EstaRetenida
        {
            get { return EstadoCajaID == 4; }
        }

        public bool EstaEnZonaVerde
        {
            get { return EstadoCajaID == 5; }
        }

        public bool TieneSalidaProduccion
        {
            get { return EstadoCajaID == 6; }
        }

        public bool RecibidaAlmacen
        {
            get { return EstadoCajaID == 7; }
        }

        public bool PuedeSolicitarCalidad
        {
            get { return EstadoCajaID == 1; }
        }

        public bool PuedeMoverZonaVerde
        {
            get { return EstadoCajaID == 3 && EtiquetaVerde; }
        }

        public bool PuedeEscanearSalidaProduccion
        {
            get { return EstadoCajaID == 5; }
        }
    }
}
