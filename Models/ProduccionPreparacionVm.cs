using System;
using System.Collections.Generic;

namespace ERP.NSQuell.Models
{
    public static class ProduccionPreparacionTipo
    {
        public const string SecadoMaterial = "SECADO_MATERIAL";
        public const string PrepararEmbalaje = "PREPARAR_EMBALAJE";
        public const string CambioMolde = "CAMBIO_MOLDE";
    }

    public static class ProduccionPreparacionEstado
    {
        public const string Pendiente = "PENDIENTE";
        public const string EnProceso = "EN_PROCESO";
        public const string Confirmada = "CONFIRMADA";
        public const string Cancelada = "CANCELADA";
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
}