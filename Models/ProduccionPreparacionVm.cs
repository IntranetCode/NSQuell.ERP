using System;
using System.Collections.Generic;
using System.Linq;

namespace ERP.NSQuell.Models
{
    public static class ProduccionPreparacionTipo
    {
        public const string SecadoMaterial = "SECADO_MATERIAL";
        public const string PrepararEmbalaje = "PREPARAR_EMBALAJE";
        public const string CambioMolde = "CAMBIO_MOLDE";
        public const string MateriaPrima = "MATERIA_PRIMA";
    }

    public static class ProduccionPreparacionEstado
    {
        public const string Pendiente = "PENDIENTE";
        public const string EnProceso = "EN_PROCESO";
        public const string Confirmada = "CONFIRMADA";
        public const string Cancelada = "CANCELADA";
    }

    public static class ProduccionChecklistFormato
    {
        public const string CambioMoldeCodigo = "GQ-F-PR01-03";
        public const string CambioMoldeVersion = "Ver.09";
        public const string ArranqueLiberacionCodigo = "GQ-F-PR01-06";
        public const string ArranqueLiberacionVersion = "Ver.10";
    }

    public static class ProduccionChecklistTipo
    {
        public const string CambioMolde = "CAMBIO_MOLDE";
        public const string ArranqueLiberacion = "ARRANQUE_LIBERACION";
    }

    public static class ProduccionChecklistMomento
    {
        public const string CambioMolde = "CAMBIO_MOLDE";
        public const string ArranqueProduccion = "ARRANQUE_PRODUCCION";
    }

    public static class ProduccionChecklistEstadoFlujo
    {
        public const string Pendiente = "PENDIENTE";
        public const string EnProceso = "EN_PROCESO";
        public const string Completo = "COMPLETO";
    }

    public static class ProduccionChecklistResultado
    {
        public const string Ok = "OK";
        public const string Nok = "NOK";
        public const string Na = "NA";
        public const string Si = "SI";
        public const string No = "NO";
    }

    public sealed class ProduccionPreparacionIndexVm
    {
        public DateTime FechaConsulta { get; set; } = DateTime.Now;
        public string? Filtro { get; set; }
        public int? MaquinaID { get; set; }
        public string? TipoTarea { get; set; }
        public bool PuedeVerTodo { get; set; }
        public bool PuedeGestionarCambioMolde { get; set; }
        public bool PuedeGestionarEmbalaje { get; set; }
        public bool PuedeGestionarSecado { get; set; }
        public List<ProduccionPreparacionTareaVm> Tareas { get; set; } = new();
        public List<ProduccionPreparacionMaquinaVm> Maquinas { get; set; } = new();

        public IEnumerable<ProduccionPreparacionTareaVm> EnProceso
        {
            get
            {
                foreach (var tarea in Tareas)
                    if (tarea.EstaEnProceso)
                        yield return tarea;
            }
        }

        public IEnumerable<ProduccionPreparacionTareaVm> Vencidas
        {
            get
            {
                foreach (var tarea in Tareas)
                    if (tarea.EsVencida)
                        yield return tarea;
            }
        }

        public IEnumerable<ProduccionPreparacionTareaVm> HacerAhora
        {
            get
            {
                foreach (var tarea in Tareas)
                    if (tarea.EsHacerAhora)
                        yield return tarea;
            }
        }

        public IEnumerable<ProduccionPreparacionTareaVm> Proximas
        {
            get
            {
                foreach (var tarea in Tareas)
                    if (tarea.EsProxima)
                        yield return tarea;
            }
        }

        public IEnumerable<ProduccionPreparacionTareaVm> Completadas
        {
            get
            {
                foreach (var tarea in Tareas)
                    if (tarea.EstaConfirmada)
                        yield return tarea;
            }
        }
    }

    public sealed class ProduccionPreparacionTareaVm
    {
        public int PreparacionAnticipadaID { get; set; }
        public int ProgramaProduccionID { get; set; }
        public int? EjecucionProduccionID { get; set; }
        public string TipoTarea { get; set; } = string.Empty;
        public string Estado { get; set; } = ProduccionPreparacionEstado.Pendiente;
        public DateTime FechaObjetivo { get; set; }
        public DateTime FechaAviso { get; set; }
        public int? UsuarioInicioID { get; set; }
        public string? UsuarioInicioNombre { get; set; }
        public DateTime? FechaInicioReal { get; set; }
        public int? UsuarioConfirmacionID { get; set; }
        public string? UsuarioConfirmacionNombre { get; set; }
        public DateTime? FechaConfirmacion { get; set; }
        public DateTime? FechaFinReal { get; set; }
        public int? DuracionRealMinutos { get; set; }
        public int? LimiteMinutosAplicado { get; set; }
        public bool ExcedioLimite { get; set; }
        public string? MotivoExceso { get; set; }
        public string? Observaciones { get; set; }
        public int? MaquinaID { get; set; }
        public string? MaquinaCodigo { get; set; }
        public string? MaquinaNombre { get; set; }
        public int MinutosMaxCambioMolde { get; set; } = 60;
        public int? ParteID { get; set; }
        public string? NumeroParte { get; set; }
        public string? ReferenciaSAP { get; set; }
        public string? DescripcionParte { get; set; }
        public int? MoldeID { get; set; }
        public string? MoldeCodigo { get; set; }
        public int? MoldeAnteriorID { get; set; }
        public string? MoldeAnteriorCodigo { get; set; }
        public int? SolicitudProduccionID { get; set; }
        public int? SolicitudProduccionDetalleID { get; set; }
        public string? NumeroOF { get; set; }
        public string? TipoSecado { get; set; }
        public decimal? HorasSecado { get; set; }

        public decimal? CantidadMpKg { get; set; }
        public decimal CantidadMpRecibidaProduccionKg { get; set; }
        public decimal CantidadMpPendienteRecepcionKg => Math.Max(0m, (CantidadMpKg ?? 0m) - CantidadMpRecibidaProduccionKg);

        public bool EsMateriaPrima => string.Equals(TipoTarea, ProduccionPreparacionTipo.MateriaPrima, StringComparison.OrdinalIgnoreCase);
        public string? MaterialCodigo { get; set; }
        public string? MaterialDescripcion { get; set; }
        public string? EmbalajeCodigo { get; set; }
        public string? EmbalajeDescripcion { get; set; }
        public decimal? PiezasPorEmbalaje { get; set; }
        public decimal? CantidadEmbalajes { get; set; }
        public int CantidadProgramada { get; set; }
        public DateTime? FechaInicioProgramada { get; set; }
        public DateTime? FechaFinProgramada { get; set; }
        public DateTime? FechaCambioMolde { get; set; }
        public DateTime? FechaArranque { get; set; }
        public int? OperadorPrincipalID { get; set; }
        public string? OperadorPrincipalNombre { get; set; }
        public int? OperadorAuxiliarID { get; set; }
        public string? OperadorAuxiliarNombre { get; set; }

        public int? GrupoLhRh { get; set; }
        public string? LadoLhRh { get; set; }
        public int? ProgramaParejaID { get; set; }
        public int? EjecucionParejaID { get; set; }
        public string? NumeroOFPareja { get; set; }
        public int? ParteParejaID { get; set; }
        public string? NumeroPartePareja { get; set; }
        public string? ReferenciaSAPPareja { get; set; }
        public string? DescripcionPartePareja { get; set; }

        public ProduccionChecklistVm? ChecklistCambioMolde { get; set; }
        public bool TieneChecklistCambioMolde => ChecklistCambioMolde != null;

        public bool EsParejaLhRh =>
            GrupoLhRh.HasValue &&
            ProgramaParejaID.HasValue &&
            ProgramaParejaID.Value > 0;
        public DateTime Ahora { get; set; } = DateTime.Now;

        public bool EstaConfirmada => string.Equals(Estado, ProduccionPreparacionEstado.Confirmada, StringComparison.OrdinalIgnoreCase);
        public bool EstaCancelada => string.Equals(Estado, ProduccionPreparacionEstado.Cancelada, StringComparison.OrdinalIgnoreCase);
        public bool EstaEnProceso => string.Equals(Estado, ProduccionPreparacionEstado.EnProceso, StringComparison.OrdinalIgnoreCase);
        public bool EstaPendiente => string.Equals(Estado, ProduccionPreparacionEstado.Pendiente, StringComparison.OrdinalIgnoreCase);

        public bool EsVencida => EstaPendiente && Ahora > FechaObjetivo;
        public bool EsHacerAhora => EstaPendiente && Ahora >= FechaAviso && Ahora <= FechaObjetivo;
        public bool EsProxima => EstaPendiente && Ahora < FechaAviso;

        public bool EsSecadoMaterial => string.Equals(TipoTarea, ProduccionPreparacionTipo.SecadoMaterial, StringComparison.OrdinalIgnoreCase);
        public bool EsPreparacionEmbalaje => string.Equals(TipoTarea, ProduccionPreparacionTipo.PrepararEmbalaje, StringComparison.OrdinalIgnoreCase);
        public bool EsCambioMolde => string.Equals(TipoTarea, ProduccionPreparacionTipo.CambioMolde, StringComparison.OrdinalIgnoreCase);

        public string TipoTareaNombre
        {
            get
            {
                if (EsMateriaPrima) return "Materia prima";
                if (EsSecadoMaterial) return "Secado de material";
                if (EsPreparacionEmbalaje) return "Preparar embalaje";
                if (EsCambioMolde) return "Cambio de molde";
                return "Preparación";
            }
        }

        public string TextoMaquina
        {
            get
            {
                if (string.IsNullOrWhiteSpace(MaquinaCodigo)) return "Sin máquina";
                if (string.IsNullOrWhiteSpace(MaquinaNombre)) return MaquinaCodigo;
                return MaquinaCodigo + " - " + MaquinaNombre;
            }
        }

        public string TextoParte
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(ReferenciaSAP)) return ReferenciaSAP;
                if (!string.IsNullOrWhiteSpace(NumeroParte)) return NumeroParte;
                return "Sin parte";
            }
        }

        public string TextoMaterial
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(MaterialCodigo) && !string.IsNullOrWhiteSpace(MaterialDescripcion))
                    return MaterialCodigo + " - " + MaterialDescripcion;
                if (!string.IsNullOrWhiteSpace(MaterialCodigo)) return MaterialCodigo;
                if (!string.IsNullOrWhiteSpace(MaterialDescripcion)) return MaterialDescripcion;
                return "Material sin especificar";
            }
        }

        public string TextoEmbalaje
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(EmbalajeCodigo) && !string.IsNullOrWhiteSpace(EmbalajeDescripcion))
                    return EmbalajeCodigo + " - " + EmbalajeDescripcion;
                if (!string.IsNullOrWhiteSpace(EmbalajeCodigo)) return EmbalajeCodigo;
                if (!string.IsNullOrWhiteSpace(EmbalajeDescripcion)) return EmbalajeDescripcion;
                return "Embalaje sin especificar";
            }
        }

        public string TextoMolde => string.IsNullOrWhiteSpace(MoldeCodigo) ? "Sin molde" : MoldeCodigo;

        public string TextoCambioMolde
        {
            get
            {
                var anterior = string.IsNullOrWhiteSpace(MoldeAnteriorCodigo) ? "Sin molde anterior" : MoldeAnteriorCodigo;
                var nuevo = string.IsNullOrWhiteSpace(MoldeCodigo) ? "Sin molde" : MoldeCodigo;
                return anterior + " → " + nuevo;
            }
        }

        public string TextoOperadores
        {
            get
            {
                var principal = string.IsNullOrWhiteSpace(OperadorPrincipalNombre) ? "Sin operador principal" : OperadorPrincipalNombre;
                if (string.IsNullOrWhiteSpace(OperadorAuxiliarNombre)) return principal;
                return principal + " / Aux. " + OperadorAuxiliarNombre;
            }
        }

        public int LimiteCambioMoldeMinutos
        {
            get
            {
                if (LimiteMinutosAplicado.HasValue && LimiteMinutosAplicado.Value > 0)
                    return LimiteMinutosAplicado.Value;
                return MinutosMaxCambioMolde > 0 ? MinutosMaxCambioMolde : 60;
            }
        }

        public int? DuracionProgramadaCambioMoldeMinutos
        {
            get
            {
                if (!EsCambioMolde || !FechaCambioMolde.HasValue || !FechaArranque.HasValue) return null;
                var minutos = (int)Math.Round((FechaArranque.Value - FechaCambioMolde.Value).TotalMinutes);
                return minutos < 0 ? null : minutos;
            }
        }

        public int MinutosTranscurridosCambioMolde
        {
            get
            {
                if (!EsCambioMolde || !FechaInicioReal.HasValue) return 0;
                var fin = FechaFinReal ?? FechaConfirmacion ?? Ahora;
                var minutos = (int)Math.Ceiling((fin - FechaInicioReal.Value).TotalMinutes);
                return Math.Max(0, minutos);
            }
        }

        public int MinutosRestantesLimiteCambioMolde => Math.Max(0, LimiteCambioMoldeMinutos - MinutosTranscurridosCambioMolde);
        public int MinutosExcesoCambioMolde => Math.Max(0, MinutosTranscurridosCambioMolde - LimiteCambioMoldeMinutos);

        public string NivelAlertaCambioMolde
        {
            get
            {
                if (!EsCambioMolde || !EstaEnProceso) return "NINGUNA";
                var transcurridos = MinutosTranscurridosCambioMolde;
                var limite = LimiteCambioMoldeMinutos;
                if (transcurridos >= limite) return "EXCEDIDO";
                if (transcurridos >= Math.Max(0, limite - 10)) return "CRITICO";
                if (transcurridos >= Math.Max(0, limite - 30)) return "ADVERTENCIA";
                return "NORMAL";
            }
        }

        public TimeSpan TiempoRestante => FechaObjetivo - Ahora;
        public int MinutosRestantes => (int)Math.Ceiling(TiempoRestante.TotalMinutes);

        public string TextoTiempo
        {
            get
            {
                if (EstaEnProceso && EsCambioMolde)
                    return $"En proceso · {MinutosTranscurridosCambioMolde} de {LimiteCambioMoldeMinutos} min";
                if (EstaConfirmada)
                    return FechaConfirmacion.HasValue ? "Confirmado " + FechaConfirmacion.Value.ToString("dd/MM HH:mm") : "Confirmado";
                var diferencia = FechaObjetivo - Ahora;
                var minutos = (int)Math.Abs(Math.Ceiling(diferencia.TotalMinutes));
                var horas = minutos / 60;
                var restoMinutos = minutos % 60;
                var texto = horas > 0 ? horas + " h " + restoMinutos + " min" : restoMinutos + " min";
                if (EsVencida) return "Vencida hace " + texto;
                if (EsHacerAhora) return "Objetivo en " + texto;
                return "Faltan " + texto;
            }
        }

        public DateTime? FechaDisponibleEstimada
        {
            get
            {
                if (!EsSecadoMaterial || !FechaConfirmacion.HasValue || !HorasSecado.HasValue || HorasSecado.Value <= 0) return null;
                return FechaConfirmacion.Value.AddHours(Convert.ToDouble(HorasSecado.Value));
            }
        }

        public bool SecadoLlegaraTarde =>
            EsSecadoMaterial &&
            FechaDisponibleEstimada.HasValue &&
            FechaArranque.HasValue &&
            FechaDisponibleEstimada.Value > FechaArranque.Value;

        public int MinutosRetrasoSecado
        {
            get
            {
                if (!SecadoLlegaraTarde || !FechaDisponibleEstimada.HasValue || !FechaArranque.HasValue) return 0;
                return (int)Math.Ceiling((FechaDisponibleEstimada.Value - FechaArranque.Value).TotalMinutes);
            }
        }
    }
    public sealed class ProduccionChecklistVm
    {
        public int ChecklistArranqueID { get; set; }
        public int? EjecucionProduccionID { get; set; }
        public int ProgramaProduccionID { get; set; }
        public int? SolicitudProduccionID { get; set; }
        public int? SolicitudProduccionDetalleID { get; set; }
        public int? ReleaseID { get; set; }
        public int? ReleaseDetalleID { get; set; }
        public DateTime FechaChecklist { get; set; }
        public int? MaquinaID { get; set; }
        public string? MaquinaCodigo { get; set; }
        public string? MaquinaNombre { get; set; }
        public int? MoldeID { get; set; }
        public string? MoldeCodigo { get; set; }
        public int? ParteID { get; set; }
        public string? NumeroParte { get; set; }
        public string? ReferenciaSAP { get; set; }
        public string? DescripcionParte { get; set; }
        public string CodigoFormato { get; set; } = string.Empty;
        public string? VersionFormato { get; set; }
        public int EstatusID { get; set; }
        public string EstadoFlujo { get; set; } = ProduccionChecklistEstadoFlujo.Pendiente;
        public string? TipoChecklist { get; set; }
        public string? MomentoProceso { get; set; }
        public string? ObservacionesGenerales { get; set; }
        public int? GrupoLhRh { get; set; }
        public bool EsOperacionLhRh { get; set; }
        public int? UsuarioCreacionID { get; set; }
        public DateTime? FechaCreacion { get; set; }
        public int? UsuarioModificacionID { get; set; }
        public DateTime? FechaModificacion { get; set; }
        public bool Activo { get; set; } = true;
        public List<ProduccionChecklistProgramaVm> Programas { get; set; } = new();
        public List<ProduccionChecklistSeccionVm> Secciones { get; set; } = new();

        public int TotalSecciones => Secciones.Count(x => x.TotalPreguntas > 0);
        public int SeccionesCompletas => Secciones.Count(x => x.TotalPreguntas > 0 && x.EstaCompleta);
        public int TotalPreguntas => Secciones.Sum(x => x.TotalPreguntas);
        public int RespuestasContestadas => Secciones.Sum(x => x.RespuestasContestadas);
        public int PreguntasObligatoriasPendientes => Secciones.Sum(x => x.PreguntasObligatoriasPendientes);
        public bool EstaPendiente => string.Equals(EstadoFlujo, ProduccionChecklistEstadoFlujo.Pendiente, StringComparison.OrdinalIgnoreCase);
        public bool EstaEnProceso => string.Equals(EstadoFlujo, ProduccionChecklistEstadoFlujo.EnProceso, StringComparison.OrdinalIgnoreCase);
        public bool EstaCompleto => string.Equals(EstadoFlujo, ProduccionChecklistEstadoFlujo.Completo, StringComparison.OrdinalIgnoreCase);
        public bool PuedeFinalizar => TotalPreguntas > 0 && PreguntasObligatoriasPendientes == 0 && Secciones.Where(x => x.TotalPreguntas > 0).All(x => x.EstaCompleta);
        public int PorcentajeAvance
        {
            get
            {
                if (TotalPreguntas <= 0) return 0;
                return Math.Min(100, Math.Max(0, (int)Math.Round(RespuestasContestadas * 100m / TotalPreguntas)));
            }
        }
        public string TextoAvance => $"{RespuestasContestadas} de {TotalPreguntas} respuestas";
        public string TextoAvanceSecciones => $"{SeccionesCompletas} de {TotalSecciones} secciones completas";
        public string TextoMaquina
        {
            get
            {
                if (string.IsNullOrWhiteSpace(MaquinaCodigo)) return "Sin máquina";
                if (string.IsNullOrWhiteSpace(MaquinaNombre)) return MaquinaCodigo;
                return MaquinaCodigo + " - " + MaquinaNombre;
            }
        }
    }

    public sealed class ProduccionChecklistProgramaVm
    {
        public long ChecklistProgramaID { get; set; }
        public int ChecklistArranqueID { get; set; }
        public int ProgramaProduccionID { get; set; }
        public int? PreparacionAnticipadaID { get; set; }
        public int? EjecucionProduccionID { get; set; }
        public string? LadoLhRh { get; set; }
        public bool EsPrincipal { get; set; }
        public string? NumeroOF { get; set; }
        public int? ParteID { get; set; }
        public string? NumeroParte { get; set; }
        public string? ReferenciaSAP { get; set; }
        public string? DescripcionParte { get; set; }
        public int? UsuarioCreacionID { get; set; }
        public DateTime? FechaCreacion { get; set; }
        public int? UsuarioModificacionID { get; set; }
        public DateTime? FechaModificacion { get; set; }
        public bool Activo { get; set; } = true;

        public string TextoOF => string.IsNullOrWhiteSpace(NumeroOF) ? $"Programa {ProgramaProduccionID}" : NumeroOF;
        public string TextoLado => string.IsNullOrWhiteSpace(LadoLhRh) ? string.Empty : LadoLhRh.Trim().ToUpperInvariant();
    }

    public sealed class ProduccionChecklistSeccionVm
    {
        public string Seccion { get; set; } = string.Empty;
        public int OrdenSeccion { get; set; }
        public List<ProduccionChecklistPreguntaVm> Preguntas { get; set; } = new();

        public int TotalPreguntas => Preguntas.Count(x => x.Activo);
        public int RespuestasContestadas => Preguntas.Count(x => x.Activo && x.EstaRespondida);
        public int PreguntasObligatorias => Preguntas.Count(x => x.Activo && x.EsObligatoria);
        public int PreguntasObligatoriasPendientes => Preguntas.Count(x => x.Activo && x.EsObligatoria && !x.RespuestaValida);
        public bool TieneRespuestas => Preguntas.Any(x => x.Activo && x.EstaRespondida);
        public bool EstaCompleta => TotalPreguntas > 0 && Preguntas.Where(x => x.Activo).All(x => !x.EsObligatoria || x.RespuestaValida);

        public string Estado
        {
            get
            {
                if (EstaCompleta) return ProduccionChecklistEstadoFlujo.Completo;
                if (TieneRespuestas) return ProduccionChecklistEstadoFlujo.EnProceso;
                return ProduccionChecklistEstadoFlujo.Pendiente;
            }
        }

        public string TextoAvance => $"{RespuestasContestadas} de {TotalPreguntas}";
    }

    public sealed class ProduccionChecklistPreguntaVm
    {
        public int PreguntaID { get; set; }
        public string CodigoFormato { get; set; } = string.Empty;
        public string? VersionFormato { get; set; }
        public string Seccion { get; set; } = string.Empty;
        public int OrdenSeccion { get; set; }
        public int OrdenPregunta { get; set; }
        public string TextoPregunta { get; set; } = string.Empty;
        public string? ResponsableSugerido { get; set; }
        public bool RequiereObservacionSiNOK { get; set; }
        public bool RequiereObservacionSiNA { get; set; }
        public string? TipoChecklist { get; set; }
        public string? MomentoProceso { get; set; }
        public string? TipoRespuesta { get; set; }
        public string? EstadoPredeterminado { get; set; }
        public bool EsPreguntaCalidad { get; set; }
        public string? GrupoResponsable { get; set; }
        public bool EsRecurrente { get; set; }
        public bool EsObligatoria { get; set; } = true;
        public bool PermiteNA { get; set; } = true;
        public bool Activo { get; set; } = true;

        public int? ChecklistArranqueDetalleID { get; set; }
        public string? Resultado { get; set; }
        public string? Observaciones { get; set; }
        public int? UsuarioRespuestaID { get; set; }
        public string? UsuarioRespuestaNombre { get; set; }
        public DateTime? FechaRespuesta { get; set; }
        public bool Confirmado { get; set; }
        public string? ValorCapturado { get; set; }
        public string? Unidad { get; set; }
        public string? Especificacion { get; set; }
        public string? Tolerancia { get; set; }

        public bool EstaRespondida =>
            Confirmado ||
            !string.IsNullOrWhiteSpace(Resultado) ||
            !string.IsNullOrWhiteSpace(ValorCapturado);

        public bool EsOk =>
            string.Equals(Resultado, ProduccionChecklistResultado.Ok, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Resultado, ProduccionChecklistResultado.Si, StringComparison.OrdinalIgnoreCase);

        public bool EsNok =>
            string.Equals(Resultado, ProduccionChecklistResultado.Nok, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Resultado, ProduccionChecklistResultado.No, StringComparison.OrdinalIgnoreCase);

        public bool EsNa =>
            string.Equals(Resultado, ProduccionChecklistResultado.Na, StringComparison.OrdinalIgnoreCase);

        public bool RequiereObservacionActual =>
            (EsNok && RequiereObservacionSiNOK) ||
            (EsNa && RequiereObservacionSiNA);

        public bool RespuestaValida
        {
            get
            {
                if (!Activo) return true;
                if (!EstaRespondida) return false;
                if (EsNa && !PermiteNA) return false;
                if (RequiereObservacionActual && string.IsNullOrWhiteSpace(Observaciones)) return false;
                return true;
            }
        }
    }



    public sealed class ProduccionChecklistGuardarSeccionVm
    {
        public int PreparacionAnticipadaID { get; set; }
        public int? ChecklistArranqueID { get; set; }
        public int OrdenSeccion { get; set; }
        public List<ProduccionChecklistRespuestaCapturaVm> Respuestas { get; set; } = new();
    }

    public sealed class ProduccionChecklistRespuestaCapturaVm
    {
        public int PreguntaID { get; set; }
        public string? Resultado { get; set; }
        public string? Observaciones { get; set; }
        public string? ValorCapturado { get; set; }
    }

    public sealed class ProduccionChecklistFinalizarVm
    {
        public int PreparacionAnticipadaID { get; set; }
        public int ChecklistArranqueID { get; set; }
        public string? ObservacionesGenerales { get; set; }
    }


    public sealed class ProduccionPreparacionMaquinaVm
    {
        public int MaquinaID { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string? Nombre { get; set; }
        public int MinutosMaxCambioMolde { get; set; } = 60;
        public string Texto => string.IsNullOrWhiteSpace(Nombre) ? Codigo : Codigo + " - " + Nombre;
        public string TextoLimiteCambioMolde
        {
            get
            {
                var minutos = MinutosMaxCambioMolde > 0 ? MinutosMaxCambioMolde : 60;
                var horas = minutos / 60;
                var resto = minutos % 60;
                if (horas > 0 && resto > 0) return $"Máximo {horas} h {resto} min";
                if (horas > 0) return $"Máximo {horas} h";
                return $"Máximo {minutos} min";
            }
        }
    }

    public sealed class ProduccionPreparacionConfirmarVm
    {
        public int PreparacionAnticipadaID { get; set; }
        public string? Observaciones { get; set; }
    }

    public sealed class ProduccionPreparacionIniciarCambioVm
    {
        public int PreparacionAnticipadaID { get; set; }
    }

    public sealed class ProduccionPreparacionFinalizarCambioVm
    {
        public int PreparacionAnticipadaID { get; set; }
        public string? Observaciones { get; set; }
        public string? MotivoExceso { get; set; }
    }

    public static class ProduccionRecepcionMaterialTipo
    {
        public const string MP = "MP";
        public const string Embalaje = "EMBALAJE";
    }

    public static class ProduccionRecepcionMaterialEstado
    {
        public const string Pendiente = "PENDIENTE";
        public const string RecibidoCompleto = "RECIBIDO_COMPLETO";
        public const string RecibidoParcial = "RECIBIDO_PARCIAL";
        public const string NoRecibido = "NO_RECIBIDO";
    }

    public static class ProduccionRecepcionMaterialEstadoAclaracion
    {
        public const string NoAplica = "NO_APLICA";
        public const string Pendiente = "PENDIENTE";
        public const string Resuelta = "RESUELTA";
    }

    public static class ProduccionRecepcionMaterialDecision
    {
        public const string Completo = "COMPLETO";
        public const string Parcial = "PARCIAL";
        public const string NoRecibido = "NO_RECIBIDO";
    }

    public sealed class ProduccionPreparacionMaterialesVm
    {
        public DateTime FechaConsulta { get; set; } = DateTime.Now;
        public string? Filtro { get; set; }
        public int? MaquinaID { get; set; }
        public bool PuedeGestionarMateriales { get; set; }
        public List<ProduccionPreparacionMaquinaVm> Maquinas { get; set; } = new();
        public List<ProduccionRecepcionMaterialVm> Recepciones { get; set; } = new();
        public List<ProduccionMaterialEsperadoVm> MaterialesEsperados { get; set; } = new();

        public int PendientesAlmacen =>
        MaterialesEsperados.Count(x => x.CantidadPendienteAlmacen > 0.0005m);

        public int EntregasParcialesAlmacen =>
            MaterialesEsperados.Count(x => x.EsEntregaParcial);

        public int PendientesConfirmacion =>
            Recepciones.Count(x => x.EstaPendiente);
        public int Pendientes =>
            Recepciones.Count(x => x.EstaPendiente);

        public int RecibidosCompletos =>
            Recepciones.Count(x => x.EstaRecibidoCompleto);

        public int ConDiferencia =>
            Recepciones.Count(x => x.TieneDiferencia);

        public int AclaracionesPendientes =>
            Recepciones.Count(x => x.AclaracionPendiente);

        public int RecibidosHoy =>
            Recepciones.Count(x =>
                x.FechaRecepcion.HasValue &&
                x.FechaRecepcion.Value.Date == FechaConsulta.Date);

        public IEnumerable<ProduccionRecepcionMaterialVm> PendientesRecepcion =>
            Recepciones
                .Where(x => x.EstaPendiente)
                .OrderBy(x => x.FechaEntregaAlmacen)
                .ThenBy(x => x.NumeroOF);

        public IEnumerable<ProduccionRecepcionMaterialVm> RecepcionesConDiferencia =>
            Recepciones
                .Where(x => x.TieneDiferencia)
                .OrderByDescending(x => x.FechaRecepcion);
    }

    public sealed class ProduccionMaterialEsperadoVm
    {
        public string TipoOrigen { get; set; } = string.Empty;
        public int SolicitudProduccionID { get; set; }
        public int? SolicitudProduccionDetalleID { get; set; }
        public int? ProgramaProduccionID { get; set; }
        public string NumeroOF { get; set; } = string.Empty;

        public int? MaquinaID { get; set; }
        public string? MaquinaCodigo { get; set; }
        public string? MaquinaNombre { get; set; }

        public int? ParteID { get; set; }
        public string? NumeroParte { get; set; }
        public string? DescripcionParte { get; set; }

        public int CatalogoID { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string? Descripcion { get; set; }
        public string Unidad { get; set; } = string.Empty;

        public decimal CantidadRequerida { get; set; }
        public decimal CantidadEntregadaAlmacen { get; set; }
        public decimal CantidadConfirmadaProduccion { get; set; }

        public int? GrupoLhRh { get; set; }
        public string? LadoLhRh { get; set; }
        public int? ProgramaParejaID { get; set; }
        public int? EjecucionParejaID { get; set; }
        public string? NumeroOFPareja { get; set; }
        public int? ParteParejaID { get; set; }
        public string? NumeroPartePareja { get; set; }
        public string? ReferenciaSAPPareja { get; set; }
        public string? DescripcionPartePareja { get; set; }
        public bool EsParejaLhRh => GrupoLhRh.HasValue && ProgramaParejaID.HasValue && ProgramaParejaID.Value > 0;

        public DateTime? FechaArranque { get; set; }

        public bool EsMateriaPrima =>
            string.Equals(
                TipoOrigen,
                ProduccionRecepcionMaterialTipo.MP,
                StringComparison.OrdinalIgnoreCase);

        public bool EsEmbalaje =>
            string.Equals(
                TipoOrigen,
                ProduccionRecepcionMaterialTipo.Embalaje,
                StringComparison.OrdinalIgnoreCase);

        public string TipoOrigenTexto =>
            EsMateriaPrima
                ? "Materia prima"
                : EsEmbalaje
                    ? "Embalaje"
                    : TipoOrigen;

        public decimal CantidadPendienteAlmacen =>
            Math.Max(0m, CantidadRequerida - CantidadEntregadaAlmacen);

        public bool SinEntrega =>
            CantidadEntregadaAlmacen <= 0.0005m &&
            CantidadPendienteAlmacen > 0.0005m;

        public bool EsEntregaParcial =>
            CantidadEntregadaAlmacen > 0.0005m &&
            CantidadPendienteAlmacen > 0.0005m;

        public bool EntregaAlmacenCompleta =>
            CantidadRequerida > 0.0005m &&
            CantidadPendienteAlmacen <= 0.0005m;

        public string EstadoAlmacen
        {
            get
            {
                if (EntregaAlmacenCompleta)
                    return "COMPLETO";
                if (EsEntregaParcial)
                    return "PARCIAL";
                return "PENDIENTE";
            }
        }

        public string EstadoAlmacenTexto
        {
            get
            {
                if (EntregaAlmacenCompleta)
                    return "Entregado por Almacén";
                if (EsEntregaParcial)
                    return "Entrega parcial";
                return "Pendiente de Almacén";
            }
        }

        public string TextoMaterial
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Codigo) &&
                    !string.IsNullOrWhiteSpace(Descripcion))
                    return $"{Codigo} - {Descripcion}";

                if (!string.IsNullOrWhiteSpace(Codigo))
                    return Codigo;

                if (!string.IsNullOrWhiteSpace(Descripcion))
                    return Descripcion;

                return "Sin información";
            }
        }

        public string TextoMaquina
        {
            get
            {
                if (string.IsNullOrWhiteSpace(MaquinaCodigo))
                    return "Sin máquina";

                if (string.IsNullOrWhiteSpace(MaquinaNombre))
                    return MaquinaCodigo;

                return $"{MaquinaCodigo} - {MaquinaNombre}";
            }
        }
    }

    public sealed class ProduccionRecepcionMaterialVm
    {
        public long RecepcionMaterialID { get; set; }
        public string TipoOrigen { get; set; } = string.Empty;
        public long MovimientoAlmacenID { get; set; }

        public int SolicitudProduccionID { get; set; }
        public int? SolicitudProduccionDetalleID { get; set; }
        public int? ProgramaProduccionID { get; set; }
        public int? EjecucionProduccionID { get; set; }

        public string NumeroOF { get; set; } = string.Empty;

        public int? MaquinaID { get; set; }
        public string? MaquinaCodigo { get; set; }
        public string? MaquinaNombre { get; set; }

        public int? ParteID { get; set; }
        public string? NumeroParte { get; set; }
        public string? DescripcionParte { get; set; }

        public int? MaterialSolicitadoID { get; set; }
        public int? MaterialEntregadoID { get; set; }

        public int? EmbalajeSolicitadoID { get; set; }
        public int? EmbalajeEntregadoID { get; set; }

        public string? CodigoSolicitado { get; set; }
        public string? DescripcionSolicitada { get; set; }

        public string CodigoEntregado { get; set; } = string.Empty;
        public string? DescripcionEntregada { get; set; }

        public string? TipoMP { get; set; }
        public string? Lote { get; set; }
        public string Unidad { get; set; } = string.Empty;

        public decimal CantidadEntregadaAlmacen { get; set; }
        public decimal? CantidadRecibidaProduccion { get; set; }
        public decimal? CantidadDiferencia { get; set; }

        public decimal CantidadRequeridaOF { get; set; }
        public decimal CantidadEntregadaAcumuladaAlmacen { get; set; }
        public decimal CantidadRecibidaAcumuladaProduccion { get; set; }

        public DateTime FechaEntregaAlmacen { get; set; }
        public int? UsuarioEntregaAlmacenID { get; set; }
        public string? UsuarioEntregaAlmacenNombre { get; set; }
        public string? ReferenciaOperacion { get; set; }
        public string? ObservacionesAlmacen { get; set; }

        public string EstadoRecepcion { get; set; } =
            ProduccionRecepcionMaterialEstado.Pendiente;

        public string? MotivoDiferencia { get; set; }
        public string? ObservacionesRecepcion { get; set; }

        public int? UsuarioRecepcionID { get; set; }
        public string? UsuarioRecepcionNombre { get; set; }
        public DateTime? FechaRecepcion { get; set; }

        public int? GrupoLhRh { get; set; }
        public string? LadoLhRh { get; set; }
        public int? ProgramaParejaID { get; set; }
        public int? EjecucionParejaID { get; set; }
        public string? NumeroOFPareja { get; set; }
        public int? ParteParejaID { get; set; }
        public string? NumeroPartePareja { get; set; }
        public string? ReferenciaSAPPareja { get; set; }
        public string? DescripcionPartePareja { get; set; }
        public bool EsParejaLhRh => GrupoLhRh.HasValue && ProgramaParejaID.HasValue && ProgramaParejaID.Value > 0;

        public string EstadoAclaracion { get; set; } =
            ProduccionRecepcionMaterialEstadoAclaracion.NoAplica;

        public string? ResolucionAclaracion { get; set; }
        public DateTime? FechaResolucion { get; set; }

        public bool EsMateriaPrima =>
            string.Equals(
                TipoOrigen,
                ProduccionRecepcionMaterialTipo.MP,
                StringComparison.OrdinalIgnoreCase);

        public bool EsEmbalaje =>
            string.Equals(
                TipoOrigen,
                ProduccionRecepcionMaterialTipo.Embalaje,
                StringComparison.OrdinalIgnoreCase);

        public bool EstaPendiente =>
            string.Equals(
                EstadoRecepcion,
                ProduccionRecepcionMaterialEstado.Pendiente,
                StringComparison.OrdinalIgnoreCase);

        public bool EstaRecibidoCompleto =>
            string.Equals(
                EstadoRecepcion,
                ProduccionRecepcionMaterialEstado.RecibidoCompleto,
                StringComparison.OrdinalIgnoreCase);

        public bool EstaRecibidoParcial =>
            string.Equals(
                EstadoRecepcion,
                ProduccionRecepcionMaterialEstado.RecibidoParcial,
                StringComparison.OrdinalIgnoreCase);

        public bool EstaNoRecibido =>
            string.Equals(
                EstadoRecepcion,
                ProduccionRecepcionMaterialEstado.NoRecibido,
                StringComparison.OrdinalIgnoreCase);

        public bool AclaracionPendiente =>
            string.Equals(
                EstadoAclaracion,
                ProduccionRecepcionMaterialEstadoAclaracion.Pendiente,
                StringComparison.OrdinalIgnoreCase);

        public bool TieneDiferencia =>
            EstaRecibidoParcial ||
            EstaNoRecibido ||
            (CantidadDiferencia.HasValue && CantidadDiferencia.Value > 0.0005m);

        public bool EsSustitucion
        {
            get
            {
                if (string.IsNullOrWhiteSpace(CodigoSolicitado) ||
                    string.IsNullOrWhiteSpace(CodigoEntregado))
                    return false;

                return !string.Equals(
                    CodigoSolicitado.Trim(),
                    CodigoEntregado.Trim(),
                    StringComparison.OrdinalIgnoreCase);
            }
        }

        public string TipoOrigenTexto =>
            EsMateriaPrima
                ? "Materia prima"
                : EsEmbalaje
                    ? "Embalaje"
                    : TipoOrigen;

        public string TipoMPTexto
        {
            get
            {
                if (!EsMateriaPrima)
                    return string.Empty;

                if (string.Equals(TipoMP, "V", StringComparison.OrdinalIgnoreCase))
                    return "Virgen";

                if (string.Equals(TipoMP, "M", StringComparison.OrdinalIgnoreCase))
                    return "Molido";

                return "Sin especificar";
            }
        }

        public string TextoMaquina
        {
            get
            {
                if (string.IsNullOrWhiteSpace(MaquinaCodigo))
                    return "Sin máquina";

                if (string.IsNullOrWhiteSpace(MaquinaNombre))
                    return MaquinaCodigo;

                return $"{MaquinaCodigo} - {MaquinaNombre}";
            }
        }

        public string TextoSolicitado
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(CodigoSolicitado) &&
                    !string.IsNullOrWhiteSpace(DescripcionSolicitada))
                    return $"{CodigoSolicitado} - {DescripcionSolicitada}";

                if (!string.IsNullOrWhiteSpace(CodigoSolicitado))
                    return CodigoSolicitado;

                if (!string.IsNullOrWhiteSpace(DescripcionSolicitada))
                    return DescripcionSolicitada;

                return "Sin información";
            }
        }

        public string TextoEntregado
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(CodigoEntregado) &&
                    !string.IsNullOrWhiteSpace(DescripcionEntregada))
                    return $"{CodigoEntregado} - {DescripcionEntregada}";

                if (!string.IsNullOrWhiteSpace(CodigoEntregado))
                    return CodigoEntregado;

                if (!string.IsNullOrWhiteSpace(DescripcionEntregada))
                    return DescripcionEntregada;

                return "Sin información";
            }
        }

        public decimal CantidadPendientePorEntregar =>
            Math.Max(
                0m,
                CantidadRequeridaOF -
                CantidadEntregadaAcumuladaAlmacen);

        public decimal CantidadPendientePorConfirmar =>
            Math.Max(
                0m,
                CantidadEntregadaAcumuladaAlmacen -
                CantidadRecibidaAcumuladaProduccion);

        public string EstadoRecepcionTexto
        {
            get
            {
                if (EstaPendiente)
                    return "Pendiente de confirmar";

                if (EstaRecibidoCompleto)
                    return "Recibido";

                if (EstaRecibidoParcial)
                    return "Recibido parcialmente";

                if (EstaNoRecibido)
                    return "No recibido";

                return EstadoRecepcion;
            }
        }
    }

    public sealed class ProduccionConfirmarRecepcionMaterialVm
    {
        public long RecepcionMaterialID { get; set; }

        public string Decision { get; set; } = string.Empty;

        public decimal? CantidadRecibida { get; set; }

        public string? MotivoDiferencia { get; set; }

        public string? Observaciones { get; set; }
    }
}
