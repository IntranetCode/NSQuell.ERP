using System;
using System.Collections.Generic;
using System.Linq;

namespace ERP.NSQuell.Models;

public sealed class ProduccionHistorialOperativoVm
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
    public bool EsParejaLhRh { get; set; }
    public int? GrupoLhRh { get; set; }
    public string? LadoActual { get; set; }
    public int? ProgramaParejaID { get; set; }
    public int? EjecucionParejaID { get; set; }
    public string? NumeroOFPareja { get; set; }
    public string? LadoPareja { get; set; }
    public List<ProduccionHistorialOperativoPasoVm> Pasos { get; set; } = new();
    public List<ProduccionHistorialOperativoChecklistVm> Checklists { get; set; } = new();
    public List<ProduccionHistorialOperativoCapturaVm> CapturasHora { get; set; } = new();
    public int PasosCompletos => Pasos.Count(x => x.Aplica && x.Completado);
    public int PasosAplicables => Pasos.Count(x => x.Aplica);
    public int TotalCapturas => CapturasHora.Count;
    public int TotalOK => CapturasHora.Sum(x => x.CantidadOK);
    public int TotalSospechosa => CapturasHora.Sum(x => x.CantidadSospechosa);
    public int TotalScrap => CapturasHora.Sum(x => x.CantidadScrap);
}

public sealed class ProduccionHistorialOperativoPasoVm
{
    public int Orden { get; set; }
    public string Clave { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Etapa { get; set; } = string.Empty;
    public string AreaResponsable { get; set; } = string.Empty;
    public string Estado { get; set; } = string.Empty;
    public bool Aplica { get; set; }
    public bool Completado { get; set; }
    public bool EnProceso { get; set; }
    public bool Bloqueado { get; set; }
    public string? Detalle { get; set; }
    public string? MotivoBloqueo { get; set; }
    public DateTime? FechaObjetivo { get; set; }
    public DateTime? FechaInicioReal { get; set; }
    public DateTime? FechaFinReal { get; set; }
}

public sealed class ProduccionHistorialOperativoChecklistVm
{
    public int ChecklistArranqueID { get; set; }
    public int? ProgramaProduccionID { get; set; }
    public int? EjecucionProduccionID { get; set; }
    public string CodigoFormato { get; set; } = string.Empty;
    public string? VersionFormato { get; set; }
    public string? TipoChecklist { get; set; }
    public string? MomentoProceso { get; set; }
    public string? EstadoFlujo { get; set; }
    public int EstatusID { get; set; }
    public string? ObservacionesGenerales { get; set; }
    public string? ObservacionesCalidad { get; set; }
    public DateTime? FechaCapturaProduccion { get; set; }
    public DateTime? FechaValidacionCalidad { get; set; }
    public DateTime FechaCreacion { get; set; }
    public DateTime? FechaModificacion { get; set; }
    public bool EsCompartidoLhRh { get; set; }
    public int ProgramasVinculados { get; set; }
    public List<ProduccionHistorialOperativoPreguntaVm> Preguntas { get; set; } = new();
    public int Respondidas => Preguntas.Count(x => x.TieneRespuesta);
    public int TotalPreguntas => Preguntas.Count;
}

public sealed class ProduccionHistorialOperativoPreguntaVm
{
    public int PreguntaID { get; set; }
    public string Seccion { get; set; } = string.Empty;
    public int OrdenSeccion { get; set; }
    public int OrdenPregunta { get; set; }
    public string TextoPregunta { get; set; } = string.Empty;
    public string? TipoRespuesta { get; set; }
    public string? ResponsableSugerido { get; set; }
    public string? Resultado { get; set; }
    public string? ValorCapturado { get; set; }
    public string? Observaciones { get; set; }
    public bool Confirmado { get; set; }
    public int? UsuarioRespuestaID { get; set; }
    public string? UsuarioRespuestaNombre { get; set; }
    public DateTime? FechaRespuesta { get; set; }
    public string? Unidad { get; set; }
    public string? Especificacion { get; set; }
    public string? Tolerancia { get; set; }
    public bool TieneRespuesta => Confirmado || !string.IsNullOrWhiteSpace(Resultado) || !string.IsNullOrWhiteSpace(ValorCapturado) || !string.IsNullOrWhiteSpace(Observaciones);
    public string RespuestaVisible => !string.IsNullOrWhiteSpace(ValorCapturado) ? ValorCapturado!.Trim() : !string.IsNullOrWhiteSpace(Resultado) ? Resultado!.Trim() : "Sin respuesta";
}

public sealed class ProduccionHistorialOperativoCapturaVm
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
    public bool EsTiempoExtra { get; set; }
    public string? TipoBloque { get; set; }
    public bool TieneCambioConfiguracion { get; set; }
    public bool TieneReinicioContador { get; set; }
    public bool AjustadoPorCalidad { get; set; }
    public string? Observaciones { get; set; }
    public DateTime FechaCreacion { get; set; }
    public int TotalClasificado => CantidadOK + CantidadSospechosa + CantidadScrap;
    public string RangoHora => $"{HoraInicio:hh\\:mm} - {HoraFin:hh\\:mm}";
}
