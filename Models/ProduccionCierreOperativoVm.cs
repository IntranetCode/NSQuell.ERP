using System;
using System.Collections.Generic;
using System.Linq;

namespace ERP.NSQuell.Models;

public static class ProduccionCierreOperativoEnfoque
{
    public const string Liberacion = "LIBERACION";
    public const string Cierre = "CIERRE";
}

public sealed class ProduccionCierreOperativoVm
{
    public DateTime FechaConsulta { get; set; } = DateTime.Now;
    public string Enfoque { get; set; } = ProduccionCierreOperativoEnfoque.Cierre;
    public ProduccionCierreLadoOperativoVm Actual { get; set; } = new();
    public ProduccionCierreLadoOperativoVm? Pareja { get; set; }
    public int? GrupoLhRh { get; set; }
    public string? LadoActual { get; set; }
    public string? LadoPareja { get; set; }
    public bool ParejaConsistente { get; set; } = true;
    public string? MotivoInconsistenciaPareja { get; set; }
    public bool SoloLectura { get; set; }
    public bool PuedeGestionarLiberacion { get; set; }
    public bool PuedeGestionarTerminacion { get; set; }
    public bool PuedeGestionarTerminacionParcial { get; set; }
    public bool PuedeGestionarCierreDocumental { get; set; }
    public string? UsuarioNombre { get; set; }

    public bool EsLhRh => Pareja != null || GrupoLhRh.HasValue;

    public IEnumerable<ProduccionCierreLadoOperativoVm> Lados
    {
        get
        {
            yield return Actual;
            if (Pareja != null) yield return Pareja;
        }
    }

    public bool MaquinaLiberadaConjunta => ParejaConsistente && Lados.All(x => x.MaquinaLiberada);
    public bool ProduccionTerminadaConjunta => ParejaConsistente && Lados.All(x => x.EsProduccionTerminada);
    public bool ListaCierreDocumentalConjunta => ParejaConsistente && Lados.All(x => x.EstaListaCierreDocumental);
    public bool CerradaConjunta => ParejaConsistente && Lados.All(x => x.EstaCerrada);
    public bool TodasCajasRecibidasConjunto => ParejaConsistente && Lados.All(x => x.TodasCajasRecibidasAlmacen);
    public int TotalCajasConjunto => Lados.Sum(x => x.TotalCajas);
    public int CajasRecibidasAlmacenConjunto => Lados.Sum(x => x.CajasRecibidasAlmacen);
    public int CajasPendientesRecepcionConjunto => Lados.Sum(x => x.CajasPendientesRecepcionAlmacen);
    public bool TienePendientesCalidad => Lados.Any(x => x.BloqueosTerminacion.Any(b => ContienePendienteCalidad(b)));
    public bool EsperandoAlmacen => ProduccionTerminadaConjunta && !TodasCajasRecibidasConjunto && !ListaCierreDocumentalConjunta && !CerradaConjunta;
    public bool EsperandoPasoAListaCierre => ProduccionTerminadaConjunta && TodasCajasRecibidasConjunto && !ListaCierreDocumentalConjunta && !CerradaConjunta;

    public bool PuedeLiberarMaquina =>
        !SoloLectura &&
        PuedeGestionarLiberacion &&
        ParejaConsistente &&
        Lados.All(x => x.PuedeLiberarMaquina);

    public bool PuedeTerminarProduccion =>
        !SoloLectura &&
        PuedeGestionarTerminacion &&
        ParejaConsistente &&
        MaquinaLiberadaConjunta &&
        Lados.All(x => x.PuedeTerminarProduccionNormal);

    public bool PuedeTerminarParcial =>
        !SoloLectura &&
        !EsLhRh &&
        PuedeGestionarTerminacionParcial &&
        Actual.PuedeTerminarParcial;

    public bool PuedePrepararCierreDocumental =>
        !SoloLectura &&
        PuedeGestionarCierreDocumental &&
        ParejaConsistente &&
        ProduccionTerminadaConjunta &&
        TodasCajasRecibidasConjunto &&
        Lados.All(x => x.PuedePasarAListaCierreDocumental);

    public bool PuedeCerrarDocumental =>
        !SoloLectura &&
        PuedeGestionarCierreDocumental &&
        ParejaConsistente &&
        ListaCierreDocumentalConjunta &&
        TodasCajasRecibidasConjunto;

    private static bool ContienePendienteCalidad(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return false;
        return texto.Contains("Calidad", StringComparison.OrdinalIgnoreCase) ||
               texto.Contains("reliber", StringComparison.OrdinalIgnoreCase) ||
               texto.Contains("monitoreo", StringComparison.OrdinalIgnoreCase) ||
               texto.Contains("disposici", StringComparison.OrdinalIgnoreCase) ||
               texto.Contains("configuraci", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class ProduccionCierreLadoOperativoVm
{
    public int EjecucionProduccionID { get; set; }
    public int ProgramaProduccionID { get; set; }
    public string OFTexto { get; set; } = string.Empty;
    public string? LadoLhRh { get; set; }
    public int? MaquinaID { get; set; }
    public string? MaquinaCodigo { get; set; }
    public string? MaquinaNombre { get; set; }
    public int? ParteID { get; set; }
    public string? NumeroParte { get; set; }
    public string? ReferenciaSAP { get; set; }
    public string? DescripcionParte { get; set; }
    public int? MoldeID { get; set; }
    public string? MoldeCodigo { get; set; }
    public int EstatusEjecucionID { get; set; }
    public int EstatusProgramaID { get; set; }
    public DateTime? FechaInicioReal { get; set; }
    public DateTime? FechaFinReal { get; set; }
    public DateTime? FechaLiberacionMaquina { get; set; }
    public int CantidadPlaneada { get; set; }
    public int CantidadOK { get; set; }
    public int CantidadSospechosa { get; set; }
    public int CantidadScrap { get; set; }
    public int TotalCajas { get; set; }
    public int CajasFormadas { get; set; }
    public int CajasPendientesCalidad { get; set; }
    public int CajasLiberadasCalidad { get; set; }
    public int CajasRetenidas { get; set; }
    public int CajasZonaVerde { get; set; }
    public int CajasSalidaProduccion { get; set; }
    public int CajasRecibidasAlmacen { get; set; }
    public bool TieneParoAbierto { get; set; }
    public bool TieneTiempoExtraActivo { get; set; }
    public bool LiberacionPermitidaRegla { get; set; }
    public string? MensajeLiberacion { get; set; }
    public bool TerminacionPermitidaRegla { get; set; }
    public List<string> BloqueosTerminacion { get; set; } = new();
    public bool TerminacionParcialPermitidaRegla { get; set; }
    public string? MotivoTerminacionParcial { get; set; }
    public DateTime? FechaAutorizacionTerminacionParcial { get; set; }

    public string EstatusEjecucionTexto => ProduccionEstatus.Nombre(EstatusEjecucionID);
    public string EstatusProgramaTexto => ProgramaProduccionEstatus.Nombre(EstatusProgramaID);
    public string ParteTexto =>
        !string.IsNullOrWhiteSpace(ReferenciaSAP) ? ReferenciaSAP! :
        !string.IsNullOrWhiteSpace(NumeroParte) ? NumeroParte! :
        "Sin parte";
    public string MaquinaTexto =>
        !string.IsNullOrWhiteSpace(MaquinaCodigo) && !string.IsNullOrWhiteSpace(MaquinaNombre) ? $"{MaquinaCodigo} - {MaquinaNombre}" :
        !string.IsNullOrWhiteSpace(MaquinaCodigo) ? MaquinaCodigo! :
        !string.IsNullOrWhiteSpace(MaquinaNombre) ? MaquinaNombre! :
        "Sin máquina";
    public bool MaquinaLiberada => FechaLiberacionMaquina.HasValue;
    public bool EsProduccionTerminada =>
        EstatusEjecucionID is ProduccionEstatus.TerminadoParcial or ProduccionEstatus.Terminado or ProduccionEstatus.ListaCierreDocumental or ProduccionEstatus.Cerrado;
    public bool EstaListaCierreDocumental =>
        EstatusEjecucionID == ProduccionEstatus.ListaCierreDocumental &&
        EstatusProgramaID == ProgramaProduccionEstatus.ListaCierreDocumental;
    public bool EstaCerrada =>
        EstatusEjecucionID == ProduccionEstatus.Cerrado &&
        EstatusProgramaID == ProgramaProduccionEstatus.Cerrado;
    public int CajasPendientesRecepcionAlmacen => Math.Max(0, TotalCajas - CajasRecibidasAlmacen);
    public bool TodasCajasRecibidasAlmacen => CajasPendientesRecepcionAlmacen == 0;
    public bool PuedeLiberarMaquina =>
        !MaquinaLiberada &&
        EstatusEjecucionID == ProduccionEstatus.EnProduccion &&
        LiberacionPermitidaRegla;
    public bool PuedeTerminarProduccionNormal =>
        (EstatusEjecucionID is ProduccionEstatus.EnProduccion or ProduccionEstatus.Pausado) &&
        TerminacionPermitidaRegla;
    public bool PuedeTerminarParcial =>
        EstatusEjecucionID == ProduccionEstatus.Pausado &&
        TerminacionParcialPermitidaRegla;
    public bool PuedePasarAListaCierreDocumental =>
        EsProduccionTerminada &&
        !EstaListaCierreDocumental &&
        !EstaCerrada &&
        TodasCajasRecibidasAlmacen &&
        EstatusProgramaID == ProgramaProduccionEstatus.Terminado;
}

public sealed class ProduccionLiberarMaquinaOperativaPostVm
{
    public int EjecucionProduccionID { get; set; }
    public string? Observaciones { get; set; }
}

public sealed class ProduccionTerminarOperativaPostVm
{
    public int EjecucionProduccionID { get; set; }
    public bool TerminarParcial { get; set; }
    public string? Observaciones { get; set; }
}

public sealed class ProduccionPrepararCierreDocumentalOperativaPostVm
{
    public int EjecucionProduccionID { get; set; }
}

public sealed class ProduccionCerrarDocumentalOperativaPostVm
{
    public int EjecucionProduccionID { get; set; }
    public string? Observaciones { get; set; }
}
