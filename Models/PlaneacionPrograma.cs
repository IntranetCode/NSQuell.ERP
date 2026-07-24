using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ERP.NSQuell.Models
{
    public class PlaneacionProgramaIndexVm
    {
        public int ProgramaProduccionID { get; set; }

        public int? ReleaseID { get; set; }
        public int? ReleaseDetalleID { get; set; }

        public string? FolioRelease { get; set; }

        public int? ClienteID { get; set; }
        public string? ClienteNombre { get; set; }

        public int? ParteID { get; set; }
        public string? NumeroParte { get; set; }
        public string? ReferenciaSAP { get; set; }
        public string? DesignacionDescripcionSAP { get; set; }

        public int CantidadRequerida { get; set; }
        public int PiezasDesdePT { get; set; }
        public int CantidadProgramada { get; set; }
        public int CantidadProducida { get; set; }
        public int CantidadPendiente { get; set; }

        public int? MaquinaID { get; set; }
        public string? MaquinaCodigo { get; set; }
        public string? MaquinaNombre { get; set; }

        public int? MoldeID { get; set; }
        public string? MoldeCodigo { get; set; }

        public string? CondicionProduccion { get; set; }
        public int? SecuenciaMaquina { get; set; }

        public DateTime? FechaInicioProgramada { get; set; }
        public DateTime? FechaFinProgramada { get; set; }
        public decimal? HorasProgramadas { get; set; }

        public int EstatusID { get; set; }
        public string EstatusNombre => PlaneacionProgramaEstatus.Nombre(EstatusID);

        public string? Observaciones { get; set; }
        public DateTime FechaCreacion { get; set; }

        public int? SolicitudProduccionID { get; set; }
        public int? SolicitudProduccionDetalleID { get; set; }

        public string? Color { get; set; }

        public TimeSpan? Cambio { get; set; }
        public TimeSpan? Arranque { get; set; }

        public bool TieneOF => SolicitudProduccionID.HasValue;

        public bool PuedeGenerarOF =>
            !TieneOF &&
            CantidadProgramada > 0 &&
            EstatusID == PlaneacionProgramaEstatus.Programado;
    }

    public class PlaneacionProgramaCrearDesdeNecesidadVm
    {
        public int ReleaseDetalleID { get; set; }

        public int? ReleaseID { get; set; }
        public string? FolioRelease { get; set; }

        public int? ClienteID { get; set; }
        public string? ClienteNombre { get; set; }

        public int? ParteID { get; set; }
        public string? NumeroParte { get; set; }
        public string? ReferenciaSAP { get; set; }
        public string? DesignacionDescripcionSAP { get; set; }

        public int CantidadRequerida { get; set; }
        public int PiezasDesdePT { get; set; }
        public int PiezasAProducir { get; set; }

        public int CantidadProgramada { get; set; }

        public int? MaquinaID { get; set; }
        public string? MaquinaCodigo { get; set; }
        public string? MaquinaNombre { get; set; }

        public int? MoldeID { get; set; }
        public string? MoldeCodigo { get; set; }

        public string? CondicionProduccion { get; set; } = PlaneacionProgramaCondicion.TerminarProduccion;

        public DateTime? FechaInicioProgramada { get; set; }
        public DateTime? FechaFinProgramada { get; set; }
        public decimal? HorasProgramadas { get; set; }

        public int? ObjetivoHora { get; set; }
        public string? Ciclo { get; set; }
        public int? Cavidades { get; set; }
        public decimal? PesoBrutoPieza { get; set; }

        public int? MaterialID { get; set; }
        public string? MaterialCodigo { get; set; }
        public string? MaterialDescripcion { get; set; }
        public decimal? CantidadMpKg { get; set; }

        public string? EmbalajeCodigo { get; set; }
        public string? EmbalajeDescripcion { get; set; }
        public decimal? PiezasPorEmbalaje { get; set; }
        public decimal? CantidadEmbalajes { get; set; }

        public string? Observaciones { get; set; }


        public int? PiezasPorCaja { get; set; }
        public int? QtyPorDia { get; set; }

        public int? MaquinaSustitutaID { get; set; }
        public string? MaquinaSustitutaCodigo { get; set; }
        public string? MaquinaSustitutaNombre { get; set; }

        public string? Color { get; set; }
        public string? TipoSecado { get; set; }
        public decimal? HorasSecado { get; set; }
        public string? HorasSecadoTexto { get; set; }



        public TimeSpan? Cambio { get; set; }
        public TimeSpan? Arranque { get; set; }

        public List<SelectListItem> Maquinas { get; set; } = new();
        public List<SelectListItem> Moldes { get; set; } = new();
        public List<SelectListItem> Condiciones { get; set; } = new();
    }

    public class PlaneacionProgramaMaquinaVm
    {
        public int? MaquinaID { get; set; }
        public string MaquinaCodigo { get; set; } = string.Empty;
        public string MaquinaNombre { get; set; } = string.Empty;

        public List<PlaneacionProgramaIndexVm> Programas { get; set; } = new();

        public PlaneacionProgramaIndexVm? Actual =>
            Programas
                .Where(x => x.EstatusID == PlaneacionProgramaEstatus.Programado ||
                            x.EstatusID == PlaneacionProgramaEstatus.EnPreparacion ||
                            x.EstatusID == PlaneacionProgramaEstatus.EnProduccion)
                .OrderBy(x => x.FechaInicioProgramada)
                .FirstOrDefault();

        public PlaneacionProgramaIndexVm? Siguiente =>
            Programas
                .Where(x => x.ProgramaProduccionID != (Actual?.ProgramaProduccionID ?? 0))
                .Where(x => x.EstatusID == PlaneacionProgramaEstatus.Programado)
                .OrderBy(x => x.FechaInicioProgramada)
                .FirstOrDefault();
    }


    public class PlaneacionProgramaNecesidadFiltroVm
    {
        public int? ClienteID { get; set; }
        public int? ParteID { get; set; }

        public DateTime? FechaDesde { get; set; }
        public DateTime? FechaHasta { get; set; }

        public bool SoloPendientes { get; set; }
        public bool SoloSinCapacidad { get; set; }
        public bool SoloSinMP { get; set; }

        public List<PlaneacionProgramaNecesidadVm> Necesidades { get; set; } = new();

        public List<SelectListItem> Clientes { get; set; } = new();
        public List<SelectListItem> Partes { get; set; } = new();

        public int TotalRenglones => Necesidades.Count;
        public int TotalRequerido => Necesidades.Sum(x => x.CantidadRequerida);
        public int TotalStock => Necesidades.Sum(x => x.PiezasDesdePT ?? 0);
        public int TotalAProducir => Necesidades.Sum(x => x.PiezasAProducir ?? 0);
        public decimal TotalMPRequerida => Necesidades.Sum(x => x.MPRequeridaKg ?? 0);
        public decimal TotalHoras => Necesidades.Sum(x => x.HorasNecesarias ?? 0);
    }

    public class PlaneacionProgramaNecesidadVm
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

        public int? ProgramaProduccionID { get; set; }
        public int? SolicitudProduccionID { get; set; }

        public int EstatusID { get; set; }

        public bool YaProgramado => ProgramaProduccionID.HasValue;

        public bool TieneMPInsuficiente =>
            (MPRequeridaKg ?? 0) > 0 &&
            (MPDisponibleKg ?? 0) < (MPRequeridaKg ?? 0);

        public bool TieneEmbalajeInsuficiente =>
            (EmbalajeRequerido ?? 0) > 0 &&
            (EmbalajeDisponible ?? 0) < (EmbalajeRequerido ?? 0);

        // Datos técnicos para detalle de Programa de Planeación
        public string? Ciclo { get; set; }
        public int? Cavidades { get; set; }
        public decimal? PesoBrutoPieza { get; set; }
        public int? PiezasPorCaja { get; set; }
        public int? QtyPorDia { get; set; }

        public int? MaquinaSustitutaID { get; set; }
        public string? MaquinaSustitutaCodigo { get; set; }
        public string? MaquinaSustitutaNombre { get; set; }

        public string? Color { get; set; }
        public string? TipoSecado { get; set; }
        public decimal? HorasSecado { get; set; }
        public string? HorasSecadoTexto { get; set; }
    }

    public class PlaneacionProgramaMaquinasVm
    {
        public DateTime FechaDesde { get; set; } = DateTime.Today;
        public DateTime FechaHasta { get; set; } = DateTime.Today.AddDays(7);

        public List<PlaneacionProgramaMaquinaVm> Maquinas { get; set; } = new();

        public int TotalProgramas => Maquinas.Sum(x => x.Programas.Count);
        public int TotalPiezasProgramadas => Maquinas.Sum(x => x.Programas.Sum(p => p.CantidadProgramada));
        public int TotalPiezasPendientes => Maquinas.Sum(x => x.Programas.Sum(p => p.CantidadPendiente));
        public decimal TotalHorasProgramadas => Maquinas.Sum(x => x.Programas.Sum(p => p.HorasProgramadas ?? 0));
    }

    public static class PlaneacionProgramaEstatus
    {
        public const int Programado = 1;
        public const int EnPreparacion = 2;
        public const int EnProduccion = 3;
        public const int Pausado = 4;
        public const int Terminado = 5;
        public const int Cerrado = 9;
        public const int Cancelado = 99;

        public static string Nombre(int estatusId)
        {
            return estatusId switch
            {
                Programado => "Programado",
                EnPreparacion => "En preparación",
                EnProduccion => "En producción",
                Pausado => "Pausado / Interrumpido",
                Terminado => "Terminado",
                Cerrado => "Cerrado",
                Cancelado => "Cancelado",
                _ => "Sin estatus"
            };
        }
    }

    public static class PlaneacionProgramaCondicion
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

        public static List<SelectListItem> SelectList()
        {
            return new List<SelectListItem>
            {
                new SelectListItem
                {
                    Value = TerminarProduccion,
                    Text = "T.P - Terminar producción actual"
                },
                new SelectListItem
                {
                    Value = InterrumpirProduccion,
                    Text = "I.P - Interrumpir producción actual"
                }
            };
        }
    }
}
