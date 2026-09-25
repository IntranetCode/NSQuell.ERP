using System;
using System.Collections.Generic;
using System.Linq;

namespace ERP.NSQuell.Models;

public sealed class ProduccionSeguimientoTecnicoOperativoVm
{
    public DateTime FechaConsulta { get; set; } = DateTime.Now;
    public int ProgramaProduccionID { get; set; }
    public int? EjecucionProduccionID { get; set; }
    public string NumeroOF { get; set; } = string.Empty;
    public string NumeroParte { get; set; } = string.Empty;
    public string? ReferenciaSAP { get; set; }
    public string? DescripcionParte { get; set; }
    public string Maquina { get; set; } = string.Empty;
    public string? Molde { get; set; }
    public string EstadoGeneral { get; set; } = string.Empty;
    public string EstadoGeneralTexto { get; set; } = string.Empty;
    public DateTime? FechaInicioProgramada { get; set; }
    public DateTime? FechaFinProgramada { get; set; }
    public DateTime? FechaInicioReal { get; set; }
    public DateTime? FechaFinReal { get; set; }
    public bool EsParejaLhRh { get; set; }
    public int? GrupoLhRh { get; set; }
    public string? LadoActual { get; set; }
    public int? ProgramaParejaID { get; set; }
    public int? EjecucionParejaID { get; set; }
    public string? NumeroOFPareja { get; set; }
    public string? LadoPareja { get; set; }
    public List<ProduccionSeguimientoTecnicoLadoVm> Lados { get; set; } = new();
    public List<ProduccionSeguimientoTecnicoCapturaVm> CapturasRecientes { get; set; } = new();
    public List<ProduccionSeguimientoTecnicoParoVm> ParosRecientes { get; set; } = new();

    public int CantidadPlaneadaTotal => Lados.Sum(x => x.CantidadPlaneada);
    public int CantidadOKTotal => Lados.Sum(x => x.CantidadOK);
    public int CantidadSospechosaTotal => Lados.Sum(x => x.CantidadSospechosa);
    public int CantidadScrapTotal => Lados.Sum(x => x.CantidadScrap);
    public int CantidadClasificadaTotal => CantidadOKTotal + CantidadSospechosaTotal + CantidadScrapTotal;
    public int CapturasRegistradasTotal => Lados.Sum(x => x.CapturasRegistradas);
    public int ObjetivoAcumuladoTotal => Lados.Sum(x => x.ObjetivoAcumulado);
    public bool TieneParoAbierto => Lados.Any(x => x.ParoAbierto != null);
    public bool RequiereReliberacion => Lados.Any(x => x.Calidad?.RequiereReliberacion == true || x.Calidad?.ConfiguracionInvalidada == true);
    public bool TieneHallazgosCalidad => Lados.Any(x => x.Calidad?.MonitoreosConHallazgo > 0 || x.Calidad?.DisposicionesPendientes > 0);
    public decimal PorcentajeAvance
    {
        get
        {
            if (CantidadPlaneadaTotal <= 0) return 0m;
            return Math.Round(Math.Min(100m, (decimal)CantidadOKTotal * 100m / CantidadPlaneadaTotal), 1);
        }
    }
    public decimal PorcentajeCumplimientoHoras
    {
        get
        {
            if (ObjetivoAcumuladoTotal <= 0) return 0m;
            return Math.Round((decimal)CantidadOKTotal * 100m / ObjetivoAcumuladoTotal, 1);
        }
    }
    public ProduccionSeguimientoTecnicoCapturaVm? UltimaCaptura => CapturasRecientes.OrderByDescending(x => x.FechaHoraFin).ThenByDescending(x => x.RegistroHoraID).FirstOrDefault();
    public ProduccionSeguimientoTecnicoLecturaVm? UltimaLecturaContador => Lados.Where(x => x.UltimaLecturaContador != null).Select(x => x.UltimaLecturaContador!).OrderByDescending(x => x.FechaLectura).ThenByDescending(x => x.LecturaContadorID).FirstOrDefault();
    public ProduccionSeguimientoTecnicoConfiguracionVm? ConfiguracionFisicaReferencia => Lados.Select(x => x.Configuracion).FirstOrDefault(x => x != null);
    public bool ConfiguracionFisicaSincronizada
    {
        get
        {
            if (!EsParejaLhRh || Lados.Count < 2) return true;
            var configuraciones = Lados.Select(x => x.Configuracion).ToList();
            if (configuraciones.Any(x => x == null)) return false;
            var a = configuraciones[0]!;
            var b = configuraciones[1]!;
            return Math.Abs(a.TiempoCicloSegundos - b.TiempoCicloSegundos) <= 0.0001m && a.ContadorInicioVigencia == b.ContadorInicioVigencia;
        }
    }
}

public sealed class ProduccionSeguimientoTecnicoLadoVm
{
    public int ProgramaProduccionID { get; set; }
    public int EjecucionProduccionID { get; set; }
    public string NumeroOF { get; set; } = string.Empty;
    public string? LadoLhRh { get; set; }
    public int EstatusID { get; set; }
    public string EstatusNombre => ProduccionEstatus.Nombre(EstatusID);
    public int? MaquinaID { get; set; }
    public string? MaquinaCodigo { get; set; }
    public string? MaquinaNombre { get; set; }
    public int? ParteID { get; set; }
    public string? NumeroParte { get; set; }
    public string? ReferenciaSAP { get; set; }
    public string? DescripcionParte { get; set; }
    public int? MoldeID { get; set; }
    public string? MoldeCodigo { get; set; }
    public int? OperadorID { get; set; }
    public string? OperadorNombre { get; set; }
    public int? OperadorAuxiliarID { get; set; }
    public string? OperadorAuxiliarNombre { get; set; }
    public DateTime? FechaInicioProgramada { get; set; }
    public DateTime? FechaFinProgramada { get; set; }
    public DateTime? FechaInicioReal { get; set; }
    public DateTime? FechaFinReal { get; set; }
    public int CantidadPlaneada { get; set; }
    public int CantidadOK { get; set; }
    public int CantidadSospechosa { get; set; }
    public int CantidadScrap { get; set; }
    public int CapturasRegistradas { get; set; }
    public int ObjetivoAcumulado { get; set; }

    // NSQ_SERIE_OBJETIVO_TEORICO_BD_V1
    public int? ObjetivoHoraTeorico { get; set; }
    public string? ObjetivoHoraTeoricoFuente { get; set; }

    public DateTime? FechaUltimaCaptura { get; set; }
    public ProduccionSeguimientoTecnicoConfiguracionVm? Configuracion { get; set; }
    public ProduccionSeguimientoTecnicoLecturaVm? UltimaLecturaContador { get; set; }
    public ProduccionSeguimientoTecnicoCalidadVm? Calidad { get; set; }
    public ProduccionSeguimientoTecnicoParoVm? ParoAbierto { get; set; }
    public List<ProduccionSeguimientoTecnicoCapturaVm> CapturasRecientes { get; set; } = new();
    public List<ProduccionSeguimientoTecnicoParoVm> ParosRecientes { get; set; } = new();
    public int CantidadClasificada => CantidadOK + CantidadSospechosa + CantidadScrap;
    public decimal PorcentajeAvance => CantidadPlaneada <= 0 ? 0m : Math.Round(Math.Min(100m, (decimal)CantidadOK * 100m / CantidadPlaneada), 1);
    public decimal PorcentajeCumplimientoHoras => ObjetivoAcumulado <= 0 ? 0m : Math.Round((decimal)CantidadOK * 100m / ObjetivoAcumulado, 1);
    public string ParteTexto => !string.IsNullOrWhiteSpace(ReferenciaSAP) ? ReferenciaSAP! : !string.IsNullOrWhiteSpace(NumeroParte) ? NumeroParte! : "Sin parte";
}

public sealed class ProduccionSeguimientoTecnicoConfiguracionVm
{
    public int ConfiguracionCorridaID { get; set; }
    public int EjecucionProduccionID { get; set; }
    public int CavidadesUsadas { get; set; }
    public string? CavidadesConfiguradas { get; set; }
    public decimal TiempoCicloSegundos { get; set; }
    public decimal ObjetivoHoraCalculado { get; set; }
    public long? ContadorInicioVigencia { get; set; }
    public DateTime FechaInicioVigencia { get; set; }
    public bool EsConfiguracionInicial { get; set; }
    public string? MotivoCambio { get; set; }
    public int? TecnicoProduccionID { get; set; }
    public string? TecnicoProduccionNombre { get; set; }
    public int ObjetivoHoraOperativo => ObjetivoHoraCalculado > 0 ? (int)Math.Round(ObjetivoHoraCalculado, 0, MidpointRounding.AwayFromZero) : 0;
    public string CavidadesTexto => !string.IsNullOrWhiteSpace(CavidadesConfiguradas) ? CavidadesConfiguradas! : CavidadesUsadas.ToString("N0");
}

public sealed class ProduccionSeguimientoTecnicoLecturaVm
{
    public long LecturaContadorID { get; set; }
    public int EjecucionProduccionID { get; set; }
    public long ValorContador { get; set; }
    public DateTime FechaLectura { get; set; }
    public string? TipoLectura { get; set; }
    public bool EsReinicioContador { get; set; }
}

public sealed class ProduccionSeguimientoTecnicoCalidadVm
{
    public int InspeccionID { get; set; }
    public string Estado { get; set; } = string.Empty;
    public string? ResultadoCalidad { get; set; }
    public string? Etiqueta { get; set; }
    public string? MotivoDevolucion { get; set; }
    public DateTime? FechaNotificacionCalidad { get; set; }
    public DateTime? FechaLiberacionProduccion { get; set; }
    public bool ConfiguracionInvalidada { get; set; }
    public bool RequiereReliberacion { get; set; }
    public bool Liberado { get; set; }
    public int TotalMonitoreos { get; set; }
    public int MonitoreosPendientes { get; set; }
    public int MonitoreosVencidos { get; set; }
    public int MonitoreosConformes { get; set; }
    public int MonitoreosConHallazgo { get; set; }
    public int DisposicionesPendientes { get; set; }
    public DateTime? ProximoMonitoreo { get; set; }
    public string EstadoTexto => Estado switch
    {
        "PENDIENTE_PREARRANQUE" => "Pendiente de prearranque",
        "DEVUELTO_PREARRANQUE" => "Devuelto a Producción",
        "ARRANQUE_AUTORIZADO" => "Arranque controlado autorizado",
        "PENDIENTE_PRIMERAS_PIEZAS" => "Primeras piezas en revisión",
        "AJUSTES_SOLICITADOS" => "Ajustes solicitados",
        "PRODUCCION_LIBERADA" => "Producción liberada",
        "MONITOREO_ACTIVO" => "Monitoreo horario activo",
        "PENDIENTE_RELIBERACION" => "Pendiente de reliberación",
        _ => string.IsNullOrWhiteSpace(Estado) ? "Sin proceso de Calidad" : Estado.Replace("_", " ")
    };
}

public sealed class ProduccionSeguimientoTecnicoCapturaVm
{
    public int RegistroHoraID { get; set; }
    public int EjecucionProduccionID { get; set; }
    public int ProgramaProduccionID { get; set; }
    public string NumeroOF { get; set; } = string.Empty;
    public string? LadoLhRh { get; set; }
    public int? OperadorID { get; set; }
    public string? OperadorNombre { get; set; }
    public DateTime FechaProduccion { get; set; }
    public TimeSpan HoraInicio { get; set; }
    public TimeSpan HoraFin { get; set; }
    public int CantidadOK { get; set; }
    public int CantidadSospechosa { get; set; }
    public int CantidadScrap { get; set; }
    public int? ObjetivoHora { get; set; }
    public int? ObjetivoBloque { get; set; }
    public bool? CumplioObjetivo { get; set; }
    public int? DiferenciaObjetivo { get; set; }
    public decimal? PorcentajeCumplimiento { get; set; }
    public int? PiezasCalculadasContador { get; set; }
    public decimal? MinutosProductivos { get; set; }
    public bool TieneCambioConfiguracion { get; set; }
    public bool TieneReinicioContador { get; set; }
    public bool AjustadoPorCalidad { get; set; }
    public string? Observaciones { get; set; }
    public DateTime FechaCreacion { get; set; }
    public DateTime FechaHoraInicio => FechaProduccion.Date.Add(HoraInicio);
    public DateTime FechaHoraFin
    {
        get
        {
            var fin = FechaProduccion.Date.Add(HoraFin);
            return fin <= FechaHoraInicio ? fin.AddDays(1) : fin;
        }
    }
    public int TotalClasificado => CantidadOK + CantidadSospechosa + CantidadScrap;
    public string RangoHora => $"{HoraInicio:hh\\:mm} - {HoraFin:hh\\:mm}";
}

public sealed class ProduccionSeguimientoTecnicoParoVm
{
    public int ParoID { get; set; }
    public int EjecucionProduccionID { get; set; }
    public int ProgramaProduccionID { get; set; }
    public string NumeroOF { get; set; } = string.Empty;
    public string? LadoLhRh { get; set; }
    public DateTime FechaInicioParo { get; set; }
    public DateTime? FechaFinParo { get; set; }
    public int DuracionMinutos { get; set; }
    public string? MotivoParoTexto { get; set; }
    public string? Descripcion { get; set; }
    public bool EsMayorA15Minutos { get; set; }
    public bool EsInterrupcionUrgente { get; set; }
    public int? ProgramaUrgenteID { get; set; }
    public bool EstaAbierto => !FechaFinParo.HasValue;
    public string DuracionTexto
    {
        get
        {
            var minutos = Math.Max(0, DuracionMinutos);
            if (minutos < 60) return $"{minutos} min";
            return $"{minutos / 60} h {minutos % 60} min";
        }
    }
}
