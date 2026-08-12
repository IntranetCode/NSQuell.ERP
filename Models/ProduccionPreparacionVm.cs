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
        public const string Confirmada = "CONFIRMADA";
        public const string Cancelada = "CANCELADA";
    }

    public sealed class ProduccionPreparacionIndexVm
    {
        public DateTime FechaConsulta { get; set; } = DateTime.Now;

        public string? Filtro { get; set; }

        public int? MaquinaID { get; set; }

        public List<ProduccionPreparacionTareaVm> Tareas { get; set; } =
            new();

        public List<ProduccionPreparacionMaquinaVm> Maquinas { get; set; } =
            new();

        public IEnumerable<ProduccionPreparacionTareaVm> Vencidas
        {
            get
            {
                foreach (var tarea in Tareas)
                {
                    if (tarea.EsVencida)
                        yield return tarea;
                }
            }
        }

        public IEnumerable<ProduccionPreparacionTareaVm> HacerAhora
        {
            get
            {
                foreach (var tarea in Tareas)
                {
                    if (tarea.EsHacerAhora)
                        yield return tarea;
                }
            }
        }

        public IEnumerable<ProduccionPreparacionTareaVm> Proximas
        {
            get
            {
                foreach (var tarea in Tareas)
                {
                    if (tarea.EsProxima)
                        yield return tarea;
                }
            }
        }

        public IEnumerable<ProduccionPreparacionTareaVm> Completadas
        {
            get
            {
                foreach (var tarea in Tareas)
                {
                    if (tarea.EstaConfirmada)
                        yield return tarea;
                }
            }
        }
    }

    public sealed class ProduccionPreparacionTareaVm
    {
        public int PreparacionAnticipadaID { get; set; }

        public int ProgramaProduccionID { get; set; }

        public int? EjecucionProduccionID { get; set; }

        public string TipoTarea { get; set; } = string.Empty;

        public string Estado { get; set; } =
            ProduccionPreparacionEstado.Pendiente;

        public DateTime FechaObjetivo { get; set; }

        public DateTime FechaAviso { get; set; }

        public int? UsuarioConfirmacionID { get; set; }

        public string? UsuarioConfirmacionNombre { get; set; }

        public DateTime? FechaConfirmacion { get; set; }

        public string? Observaciones { get; set; }

        public int? MaquinaID { get; set; }

        public string? MaquinaCodigo { get; set; }

        public string? MaquinaNombre { get; set; }

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

        public bool EstaConfirmada
        {
            get
            {
                return string.Equals(
                    Estado,
                    ProduccionPreparacionEstado.Confirmada,
                    StringComparison.OrdinalIgnoreCase);
            }
        }

        public bool EstaCancelada
        {
            get
            {
                return string.Equals(
                    Estado,
                    ProduccionPreparacionEstado.Cancelada,
                    StringComparison.OrdinalIgnoreCase);
            }
        }

        public bool EstaPendiente
        {
            get
            {
                return !EstaConfirmada &&
                       !EstaCancelada;
            }
        }

        public bool EsVencida
        {
            get
            {
                return EstaPendiente &&
                       Ahora > FechaObjetivo;
            }
        }

        public bool EsHacerAhora
        {
            get
            {
                return EstaPendiente &&
                       Ahora >= FechaAviso &&
                       Ahora <= FechaObjetivo;
            }
        }

        public bool EsProxima
        {
            get
            {
                return EstaPendiente &&
                       Ahora < FechaAviso;
            }
        }

        public bool EsSecadoMaterial
        {
            get
            {
                return string.Equals(
                    TipoTarea,
                    ProduccionPreparacionTipo.SecadoMaterial,
                    StringComparison.OrdinalIgnoreCase);
            }
        }

        public bool EsPreparacionEmbalaje
        {
            get
            {
                return string.Equals(
                    TipoTarea,
                    ProduccionPreparacionTipo.PrepararEmbalaje,
                    StringComparison.OrdinalIgnoreCase);
            }
        }

        public bool EsCambioMolde
        {
            get
            {
                return string.Equals(
                    TipoTarea,
                    ProduccionPreparacionTipo.CambioMolde,
                    StringComparison.OrdinalIgnoreCase);
            }
        }

        public string TipoTareaNombre
        {
            get
            {
                if (EsSecadoMaterial)
                    return "Secado de material";

                if (EsPreparacionEmbalaje)
                    return "Preparar embalaje";

                if (EsCambioMolde)
                    return "Cambio de molde";

                return "Preparación";
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

                return MaquinaCodigo +
                       " - " +
                       MaquinaNombre;
            }
        }

        public string TextoParte
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(ReferenciaSAP))
                    return ReferenciaSAP;

                if (!string.IsNullOrWhiteSpace(NumeroParte))
                    return NumeroParte;

                return "Sin parte";
            }
        }

        public string TextoMaterial
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(MaterialCodigo) &&
                    !string.IsNullOrWhiteSpace(MaterialDescripcion))
                {
                    return MaterialCodigo +
                           " - " +
                           MaterialDescripcion;
                }

                if (!string.IsNullOrWhiteSpace(MaterialCodigo))
                    return MaterialCodigo;

                if (!string.IsNullOrWhiteSpace(MaterialDescripcion))
                    return MaterialDescripcion;

                return "Material sin especificar";
            }
        }

        public string TextoEmbalaje
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(EmbalajeCodigo) &&
                    !string.IsNullOrWhiteSpace(EmbalajeDescripcion))
                {
                    return EmbalajeCodigo +
                           " - " +
                           EmbalajeDescripcion;
                }

                if (!string.IsNullOrWhiteSpace(EmbalajeCodigo))
                    return EmbalajeCodigo;

                if (!string.IsNullOrWhiteSpace(EmbalajeDescripcion))
                    return EmbalajeDescripcion;

                return "Embalaje sin especificar";
            }
        }

        public string TextoMolde
        {
            get
            {
                return string.IsNullOrWhiteSpace(MoldeCodigo)
                    ? "Sin molde"
                    : MoldeCodigo;
            }
        }

        public string TextoCambioMolde
        {
            get
            {
                var anterior =
                    string.IsNullOrWhiteSpace(MoldeAnteriorCodigo)
                        ? "Sin molde anterior"
                        : MoldeAnteriorCodigo;

                var nuevo =
                    string.IsNullOrWhiteSpace(MoldeCodigo)
                        ? "Sin molde"
                        : MoldeCodigo;

                return anterior +
                       " → " +
                       nuevo;
            }
        }

        public string TextoOperadores
        {
            get
            {
                var principal =
                    string.IsNullOrWhiteSpace(
                        OperadorPrincipalNombre)
                        ? "Sin operador principal"
                        : OperadorPrincipalNombre;

                if (string.IsNullOrWhiteSpace(
                    OperadorAuxiliarNombre))
                {
                    return principal;
                }

                return principal +
                       " / Aux. " +
                       OperadorAuxiliarNombre;
            }
        }

        public TimeSpan TiempoRestante
        {
            get
            {
                return FechaObjetivo -
                       Ahora;
            }
        }

        public int MinutosRestantes
        {
            get
            {
                return (int)Math.Ceiling(
                    TiempoRestante.TotalMinutes);
            }
        }

        public string TextoTiempo
        {
            get
            {
                if (EstaConfirmada)
                {
                    return FechaConfirmacion.HasValue
                        ? "Confirmado " +
                          FechaConfirmacion.Value
                              .ToString("dd/MM HH:mm")
                        : "Confirmado";
                }

                var diferencia =
                    FechaObjetivo -
                    Ahora;

                var minutos =
                    (int)Math.Abs(
                        Math.Ceiling(
                            diferencia.TotalMinutes));

                var horas =
                    minutos / 60;

                var restoMinutos =
                    minutos % 60;

                var texto =
                    horas > 0
                        ? horas + " h " +
                          restoMinutos + " min"
                        : restoMinutos + " min";

                if (EsVencida)
                    return "Vencida hace " + texto;

                if (EsHacerAhora)
                    return "Objetivo en " + texto;

                return "Faltan " + texto;
            }
        }

        public DateTime? FechaDisponibleEstimada
        {
            get
            {
                if (!EsSecadoMaterial ||
                    !FechaConfirmacion.HasValue ||
                    !HorasSecado.HasValue ||
                    HorasSecado.Value <= 0)
                {
                    return null;
                }

                return FechaConfirmacion.Value
                    .AddHours(
                        Convert.ToDouble(
                            HorasSecado.Value));
            }
        }

        public bool SecadoLlegaraTarde
        {
            get
            {
                return EsSecadoMaterial &&
                       FechaDisponibleEstimada.HasValue &&
                       FechaArranque.HasValue &&
                       FechaDisponibleEstimada.Value >
                       FechaArranque.Value;
            }
        }

        public int MinutosRetrasoSecado
        {
            get
            {
                if (!SecadoLlegaraTarde ||
                    !FechaDisponibleEstimada.HasValue ||
                    !FechaArranque.HasValue)
                {
                    return 0;
                }

                return (int)Math.Ceiling(
                    (
                        FechaDisponibleEstimada.Value -
                        FechaArranque.Value
                    ).TotalMinutes);
            }
        }
    }

    public sealed class ProduccionPreparacionMaquinaVm
    {
        public int MaquinaID { get; set; }

        public string Codigo { get; set; } =
            string.Empty;

        public string? Nombre { get; set; }

        public string Texto
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Nombre))
                    return Codigo;

                return Codigo +
                       " - " +
                       Nombre;
            }
        }
    }

    public sealed class ProduccionPreparacionConfirmarVm
    {
        public int PreparacionAnticipadaID { get; set; }

        public string? Observaciones { get; set; }
    }
}