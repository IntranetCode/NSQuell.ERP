using System.Collections.Generic;

namespace ERP.NSQuell.Models;

public sealed class ProduccionInicioSerieOperativaVm
{
    public ProduccionEjecucionVm Ejecucion { get; set; } = new();
    public ProduccionConfiguracionCorridaVm? Configuracion { get; set; }
    public ProduccionCalidadResumenVm? Calidad { get; set; }

    public ProduccionParejaLhRhVm? ParejaLhRh { get; set; }
    public ProduccionEjecucionVm? EjecucionPareja { get; set; }
    public ProduccionConfiguracionCorridaVm? ConfiguracionPareja { get; set; }
    public ProduccionCalidadResumenVm? CalidadPareja { get; set; }

    public bool EsReinicioActual { get; set; }
    public bool EsReinicioPareja { get; set; }
    public bool PuedeEjecutarUsuario { get; set; }
    public bool YaEstaEnSerie { get; set; }
    public bool PuedeConfirmar { get; set; }

    public List<string> Bloqueos { get; set; } = new();

    public bool EsParejaLhRh => ParejaLhRh != null;
    public bool EsReinicio => EsReinicioActual || EsReinicioPareja;

    public string NumeroOF =>
        !string.IsNullOrWhiteSpace(Ejecucion.NumeroOFRecibida)
            ? Ejecucion.NumeroOFRecibida!
            : !string.IsNullOrWhiteSpace(Ejecucion.FolioSolicitud)
                ? Ejecucion.FolioSolicitud!
                : "Sin OF capturada";

    public string NumeroOFPareja
    {
        get
        {
            if (EjecucionPareja != null)
            {
                if (!string.IsNullOrWhiteSpace(EjecucionPareja.NumeroOFRecibida))
                    return EjecucionPareja.NumeroOFRecibida!;
                if (!string.IsNullOrWhiteSpace(EjecucionPareja.FolioSolicitud))
                    return EjecucionPareja.FolioSolicitud!;
            }

            if (!string.IsNullOrWhiteSpace(ParejaLhRh?.NumeroOFPareja))
                return ParejaLhRh.NumeroOFPareja!;
            if (!string.IsNullOrWhiteSpace(ParejaLhRh?.FolioSolicitudPareja))
                return ParejaLhRh.FolioSolicitudPareja!;
            if (ParejaLhRh != null && ParejaLhRh.ProgramaParejaID > 0)
                return $"Programa {ParejaLhRh.ProgramaParejaID}";

            return "Sin OF pareja";
        }
    }

    public string ParteActual =>
        !string.IsNullOrWhiteSpace(Ejecucion.ReferenciaSAP)
            ? Ejecucion.ReferenciaSAP!
            : !string.IsNullOrWhiteSpace(Ejecucion.NumeroParte)
                ? Ejecucion.NumeroParte!
                : "Sin parte";

    public string PartePareja =>
        EjecucionPareja != null && !string.IsNullOrWhiteSpace(EjecucionPareja.ReferenciaSAP)
            ? EjecucionPareja.ReferenciaSAP!
            : EjecucionPareja != null && !string.IsNullOrWhiteSpace(EjecucionPareja.NumeroParte)
                ? EjecucionPareja.NumeroParte!
                : !string.IsNullOrWhiteSpace(ParejaLhRh?.ReferenciaSAPPareja)
                    ? ParejaLhRh.ReferenciaSAPPareja!
                    : !string.IsNullOrWhiteSpace(ParejaLhRh?.NumeroPartePareja)
                        ? ParejaLhRh.NumeroPartePareja!
                        : "Sin parte";

    public bool ConfiguracionLista =>
        Configuracion != null &&
        Configuracion.EstaVigente &&
        Configuracion.CavidadesUsadas > 0 &&
        Configuracion.TiempoCicloSegundos > 0 &&
        Configuracion.ContadorInicioVigencia.HasValue;

    public bool ConfiguracionParejaLista =>
        !EsParejaLhRh ||
        (ConfiguracionPareja != null &&
         ConfiguracionPareja.EstaVigente &&
         ConfiguracionPareja.CavidadesUsadas > 0 &&
         ConfiguracionPareja.TiempoCicloSegundos > 0 &&
         ConfiguracionPareja.ContadorInicioVigencia.HasValue);

    public bool CalidadLista => Calidad?.PuedeIniciarSerie == true;
    public bool CalidadParejaLista => !EsParejaLhRh || CalidadPareja?.PuedeIniciarSerie == true;

    public bool ConfiguracionFisicaLhRhSincronizada =>
        !EsParejaLhRh ||
        (ConfiguracionLista &&
         ConfiguracionParejaLista &&
         Configuracion != null &&
         ConfiguracionPareja != null &&
         System.Math.Abs(Configuracion.TiempoCicloSegundos - ConfiguracionPareja.TiempoCicloSegundos) <= 0.0001m &&
         Configuracion.ContadorInicioVigencia == ConfiguracionPareja.ContadorInicioVigencia);

    public string TituloAccion => EsReinicio
        ? "Reiniciar producción en serie"
        : "Iniciar producción en serie";

    public string TextoBoton => EsReinicio
        ? "Reiniciar serie ahora"
        : "Iniciar serie";
}
