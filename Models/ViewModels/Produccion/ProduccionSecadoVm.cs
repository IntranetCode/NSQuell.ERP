using System;
using System.Collections.Generic;
using System.Linq;

namespace ERP.NSQuell.Models
{
    public static class ProduccionSecadoTipoProceso
    {
        public const string Secado = "SECADO";
        public const string Deshumidificado = "DESHUMIDIFICADO";
    }

    public static class ProduccionSecadoEstadoMaterial
    {
        public const string Pendiente = "PENDIENTE";
        public const string Parcial = "PARCIAL";
        public const string EnProceso = "EN_PROCESO";
        public const string Finalizado = "FINALIZADO";
        public const string Cancelado = "CANCELADO";
    }

    public static class ProduccionSecadoEstadoCarga
    {
        public const string Pendiente = "PENDIENTE";
        public const string EnProceso = "EN_PROCESO";
        public const string Finalizada = "FINALIZADA";
        public const string Cancelada = "CANCELADA";
    }

    public static class ProduccionSecadoReglas
    {
        public const decimal ToleranciaCantidad = 0.0005m;
    }

    public sealed class ProduccionSecadoIndexVm
    {
        public DateTime FechaConsulta { get; set; } = DateTime.Now;
        public string? Filtro { get; set; }
        public int? MaquinaID { get; set; }
        public bool PuedeGestionarSecado { get; set; }

        public ProduccionSecadoConfiguracionVm Configuracion { get; set; } = new();

        public List<ProduccionPreparacionMaquinaVm> Maquinas { get; set; } = new();

        public List<ProduccionSecadoTolvaVm> Tolvas { get; set; } = new();

       
        public List<ProduccionSecadoMaterialVm> Materiales { get; set; } = new();

       
        public List<ProduccionPreparacionTareaVm> PendientesPlaneacion { get; set; } = new();

        public int TotalPendientesOperativos =>
            Materiales.Count(x => x.EstaPendiente || x.EstaParcial);

        // Programados por Planeación que todavía esperan recepción de MP.
        public int TotalPendientesPlaneacion =>
            PendientesPlaneacion.Count;

        // Total general de pendientes mostrado en el tablero.
        public int TotalPendientes =>
            TotalPendientesOperativos + TotalPendientesPlaneacion;

        public int TotalEnProceso =>
            Materiales.Count(x => x.EstaEnProceso || x.TieneCargaEnProceso);

        public int TotalFinalizados =>
            Materiales.Count(x => x.EstaFinalizado);

        public int TotalAlertasEspera =>
            Materiales.Count(x => x.DebeAlertarEspera);

        public int TotalCargasRetrasadas =>
            Materiales
                .SelectMany(x => x.Cargas)
                .Count(x => x.EstaRetrasada);

        public int TotalTolvasOcupadas =>
            Tolvas.Count(x => x.EstaOcupada);

        public int TotalTolvasDisponibles =>
            Tolvas.Count(x => x.DisponibleParaUsar);

        public IEnumerable<ProduccionSecadoMaterialVm> Pendientes =>
            Materiales
                .Where(x =>
                    !x.EstaFinalizado &&
                    !x.EstaCancelado &&
                    !x.TieneCargaEnProceso)
                .OrderByDescending(x => x.DebeAlertarEspera)
                .ThenBy(x => x.FechaInicioSecadoObjetivo ?? DateTime.MaxValue)
                .ThenBy(x => x.FechaArranqueProduccion ?? DateTime.MaxValue);

    
        public IEnumerable<ProduccionPreparacionTareaVm> ProgramadosEsperandoMaterial =>
            PendientesPlaneacion
                .Where(x => x.EstaPendiente || x.EstaEnProceso)
                .OrderBy(x => x.FechaAviso)
                .ThenBy(x => x.FechaObjetivo)
                .ThenBy(x => x.ProgramaProduccionID);

        public IEnumerable<ProduccionSecadoMaterialVm> EnProceso =>
            Materiales
                .Where(x => x.TieneCargaEnProceso)
                .OrderBy(x =>
                    x.Cargas
                        .Where(c => c.EstaEnProceso)
                        .Select(c => c.FechaFinEsperada ?? DateTime.MaxValue)
                        .DefaultIfEmpty(DateTime.MaxValue)
                        .Min());
    }

    public sealed class ProduccionSecadoConfiguracionVm
    {
        public int ConfiguracionID { get; set; }
        public string Codigo { get; set; } = "GENERAL";
        public string Nombre { get; set; } = string.Empty;
        public int MargenEntregaAntesSecadoMinutos { get; set; } = 30;
        public int MinutosAlertaEsperaInicio { get; set; } = 20;
        public int MinutosAvisoProximoFin { get; set; } = 15;
        public int MinutosToleranciaFin { get; set; } = 10;
    }

    public sealed class ProduccionSecadoMaterialVm
    {
        public long SecadoMaterialID { get; set; }
        public long RecepcionMaterialID { get; set; }
        public int SolicitudProduccionID { get; set; }
        public int? SolicitudProduccionDetalleID { get; set; }
        public int? ProgramaProduccionID { get; set; }
        public int? EjecucionProduccionID { get; set; }
        public int? MaquinaProgramadaID { get; set; }
        public string? MaquinaProgramadaCodigo { get; set; }
        public string? MaquinaProgramadaNombre { get; set; }
        public int MaterialID { get; set; }
        public string NumeroOF { get; set; } = string.Empty;
        public string MaterialCodigo { get; set; } = string.Empty;
        public string? MaterialDescripcion { get; set; }
        public string? TipoMP { get; set; }
        public string? Lote { get; set; }
        public decimal CantidadRecibidaKg { get; set; }
        public decimal CantidadAsignadaKg { get; set; }
        public decimal CantidadFinalizadaKg { get; set; }
        public string? TipoSecadoOrigen { get; set; }
        public string TipoProceso { get; set; } = ProduccionSecadoTipoProceso.Secado;
        public decimal HorasSecadoRequeridas { get; set; }
        public int MinutosSecadoRequeridos { get; set; }
        public int MargenEntregaAntesSecadoMinutos { get; set; }
        public DateTime FechaRecepcionProduccion { get; set; }
        public DateTime? FechaArranqueProduccion { get; set; }
        public DateTime? FechaInicioSecadoObjetivo { get; set; }
        public DateTime? FechaLimiteEntregaMaterial { get; set; }
        public DateTime? FechaObjetivoFinSecado { get; set; }
        public DateTime? FechaPrimerInicioSecado { get; set; }
        public DateTime? FechaUltimoFinSecado { get; set; }
        public int? MinutosEsperaInicio { get; set; }
        public int? MinutosRetrasoFinal { get; set; }
        public string Estado { get; set; } = ProduccionSecadoEstadoMaterial.Pendiente;
        public string? Observaciones { get; set; }
        public int? TolvaSugeridaID { get; set; }
        public string? TolvaSugeridaCodigo { get; set; }
        public string? TolvaSugeridaNombre { get; set; }
        public decimal? TolvaSugeridaCapacidadKg { get; set; }
        public DateTime Ahora { get; set; } = DateTime.Now;
        public int MinutosAlertaEsperaInicio { get; set; } = 20;

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
        public List<ProduccionSecadoCargaVm> Cargas { get; set; } = new();

        public decimal CantidadPendienteAsignarKg =>
            Math.Max(0m, CantidadRecibidaKg - CantidadAsignadaKg);

        public decimal CantidadPendienteFinalizarKg =>
            Math.Max(0m, CantidadAsignadaKg - CantidadFinalizadaKg);

        public decimal CantidadPendienteTotalKg =>
            Math.Max(0m, CantidadRecibidaKg - CantidadFinalizadaKg);

        public bool RequiereNuevaCarga =>
            CantidadPendienteAsignarKg > ProduccionSecadoReglas.ToleranciaCantidad;

        public bool EstaPendiente =>
            string.Equals(Estado, ProduccionSecadoEstadoMaterial.Pendiente, StringComparison.OrdinalIgnoreCase);

        public bool EstaParcial =>
            string.Equals(Estado, ProduccionSecadoEstadoMaterial.Parcial, StringComparison.OrdinalIgnoreCase);

        public bool EstaEnProceso =>
            string.Equals(Estado, ProduccionSecadoEstadoMaterial.EnProceso, StringComparison.OrdinalIgnoreCase);

        public bool EstaFinalizado =>
            string.Equals(Estado, ProduccionSecadoEstadoMaterial.Finalizado, StringComparison.OrdinalIgnoreCase);

        public bool EstaCancelado =>
            string.Equals(Estado, ProduccionSecadoEstadoMaterial.Cancelado, StringComparison.OrdinalIgnoreCase);

        public bool TieneCargaEnProceso =>
            Cargas.Any(x => x.EstaEnProceso);

        public bool TieneCargasPendientes =>
            Cargas.Any(x => x.EstaPendiente);

        public int MinutosEsperaActual
        {
            get
            {
                if (FechaPrimerInicioSecado.HasValue)
                    return Math.Max(0, MinutosEsperaInicio ?? (int)Math.Floor((FechaPrimerInicioSecado.Value - FechaRecepcionProduccion).TotalMinutes));

                return Math.Max(0, (int)Math.Floor((Ahora - FechaRecepcionProduccion).TotalMinutes));
            }
        }

        public bool DebeAlertarEspera =>
            !FechaPrimerInicioSecado.HasValue &&
            !EstaFinalizado &&
            !EstaCancelado &&
            MinutosEsperaActual >= MinutosAlertaEsperaInicio;

        public bool RecepcionTardia =>
            FechaLimiteEntregaMaterial.HasValue &&
            FechaRecepcionProduccion > FechaLimiteEntregaMaterial.Value;

        public int MinutosRetrasoRecepcion
        {
            get
            {
                if (!RecepcionTardia || !FechaLimiteEntregaMaterial.HasValue)
                    return 0;

                return Math.Max(0, (int)Math.Floor((FechaRecepcionProduccion - FechaLimiteEntregaMaterial.Value).TotalMinutes));
            }
        }

        public string TipoProcesoTexto =>
            string.Equals(TipoProceso, ProduccionSecadoTipoProceso.Deshumidificado, StringComparison.OrdinalIgnoreCase)
                ? "Deshumidificado"
                : "Secado";

        public string TipoMPTexto
        {
            get
            {
                if (string.Equals(TipoMP, "V", StringComparison.OrdinalIgnoreCase))
                    return "Virgen";
                if (string.Equals(TipoMP, "M", StringComparison.OrdinalIgnoreCase))
                    return "Molido";
                return string.Empty;
            }
        }

        public string TextoMaterial
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(MaterialCodigo) && !string.IsNullOrWhiteSpace(MaterialDescripcion))
                    return $"{MaterialCodigo} - {MaterialDescripcion}";
                if (!string.IsNullOrWhiteSpace(MaterialCodigo))
                    return MaterialCodigo;
                return MaterialDescripcion ?? "Sin material";
            }
        }

        public string TextoMaquina
        {
            get
            {
                if (string.IsNullOrWhiteSpace(MaquinaProgramadaCodigo))
                    return "Sin máquina";
                if (string.IsNullOrWhiteSpace(MaquinaProgramadaNombre))
                    return MaquinaProgramadaCodigo;
                return $"{MaquinaProgramadaCodigo} - {MaquinaProgramadaNombre}";
            }
        }

        public string TextoTolvaSugerida
        {
            get
            {
                if (string.IsNullOrWhiteSpace(TolvaSugeridaCodigo) && string.IsNullOrWhiteSpace(TolvaSugeridaNombre))
                    return "Sin tolva asignada";
                if (string.IsNullOrWhiteSpace(TolvaSugeridaNombre))
                    return TolvaSugeridaCodigo ?? "Sin tolva";
                if (string.IsNullOrWhiteSpace(TolvaSugeridaCodigo))
                    return TolvaSugeridaNombre;
                return $"{TolvaSugeridaCodigo} - {TolvaSugeridaNombre}";
            }
        }

        public string EstadoTexto
        {
            get
            {
                if (EstaFinalizado) return "Finalizado";
                if (EstaEnProceso || TieneCargaEnProceso) return "En proceso";
                if (EstaParcial) return "Secado parcial";
                if (EstaCancelado) return "Cancelado";
                return "Pendiente de secado";
            }
        }
    }

    public sealed class ProduccionSecadoCargaVm
    {
        public long SecadoCargaID { get; set; }
        public long SecadoMaterialID { get; set; }
        public int NumeroCarga { get; set; }
        public int TolvaIDActual { get; set; }
        public int? MaquinaTolvaID { get; set; }
        public string? MaquinaTolvaCodigo { get; set; }
        public string? MaquinaTolvaNombre { get; set; }
        public string TolvaCodigo { get; set; } = string.Empty;
        public string TolvaNombre { get; set; } = string.Empty;
        public decimal CantidadKg { get; set; }
        public decimal CapacidadTolvaKgSnapshot { get; set; }
        public int DuracionRequeridaMinutos { get; set; }
        public string Estado { get; set; } = ProduccionSecadoEstadoCarga.Pendiente;
        public DateTime FechaDisponibleDesde { get; set; }
        public DateTime FechaAsignacionTolva { get; set; }
        public DateTime? FechaInicioReal { get; set; }
        public DateTime? FechaFinEsperada { get; set; }
        public DateTime? FechaFinReal { get; set; }
        public int? MinutosEsperaAntesInicio { get; set; }
        public int? DuracionRealMinutos { get; set; }
        public int? MinutosExcesoSecado { get; set; }
        public bool ExcedioTiempo { get; set; }
        public bool FinalizoAntesTiempo { get; set; }
        public string? MotivoFinalizacionAnticipada { get; set; }
        public int? UsuarioInicioID { get; set; }
        public string? UsuarioInicioNombre { get; set; }
        public int? UsuarioFinID { get; set; }
        public string? UsuarioFinNombre { get; set; }
        public string? Observaciones { get; set; }
        public DateTime Ahora { get; set; } = DateTime.Now;
        public int MinutosAvisoProximoFin { get; set; } = 15;
        public int MinutosToleranciaFin { get; set; } = 10;
        public int? GrupoLhRh { get; set; }
        public List<ProduccionSecadoCargaMaterialVm> Materiales { get; set; } = new();
        public bool EsCargaConjunta => Materiales.Count(x => x.Activo) > 1;
        public decimal CantidadComponentesKg => Materiales.Where(x => x.Activo).Sum(x => x.CantidadKg);
        public List<ProduccionSecadoSegmentoVm> Segmentos { get; set; } = new();

        public bool EstaPendiente =>
            string.Equals(Estado, ProduccionSecadoEstadoCarga.Pendiente, StringComparison.OrdinalIgnoreCase);

        public bool EstaEnProceso =>
            string.Equals(Estado, ProduccionSecadoEstadoCarga.EnProceso, StringComparison.OrdinalIgnoreCase);

        public bool EstaFinalizada =>
            string.Equals(Estado, ProduccionSecadoEstadoCarga.Finalizada, StringComparison.OrdinalIgnoreCase);

        public bool EstaCancelada =>
            string.Equals(Estado, ProduccionSecadoEstadoCarga.Cancelada, StringComparison.OrdinalIgnoreCase);

        public int MinutosTranscurridos
        {
            get
            {
                if (!FechaInicioReal.HasValue)
                    return 0;

                var fin = FechaFinReal ?? Ahora;
                return Math.Max(0, (int)Math.Floor((fin - FechaInicioReal.Value).TotalMinutes));
            }
        }

        public int MinutosRestantes
        {
            get
            {
                if (!EstaEnProceso || !FechaFinEsperada.HasValue)
                    return 0;

                return Math.Max(0, (int)Math.Ceiling((FechaFinEsperada.Value - Ahora).TotalMinutes));
            }
        }

        public int MinutosRetrasoActual
        {
            get
            {
                if (!EstaEnProceso || !FechaFinEsperada.HasValue || Ahora <= FechaFinEsperada.Value)
                    return 0;

                return Math.Max(0, (int)Math.Floor((Ahora - FechaFinEsperada.Value).TotalMinutes));
            }
        }

        public bool TiempoRequeridoCumplido =>
            EstaEnProceso &&
            FechaFinEsperada.HasValue &&
            Ahora >= FechaFinEsperada.Value;

        public bool EstaProximaAFinalizar =>
            EstaEnProceso &&
            FechaFinEsperada.HasValue &&
            Ahora < FechaFinEsperada.Value &&
            MinutosRestantes <= MinutosAvisoProximoFin;

        public bool EstaRetrasada =>
            EstaEnProceso &&
            FechaFinEsperada.HasValue &&
            Ahora > FechaFinEsperada.Value.AddMinutes(MinutosToleranciaFin);

        public string TextoTolva
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(TolvaCodigo) && !string.IsNullOrWhiteSpace(TolvaNombre))
                    return $"{TolvaCodigo} - {TolvaNombre}";
                if (!string.IsNullOrWhiteSpace(TolvaNombre))
                    return TolvaNombre;
                return TolvaCodigo;
            }
        }

        public string EstadoTexto
        {
            get
            {
                if (EstaFinalizada) return "Finalizada";
                if (EstaCancelada) return "Cancelada";
                if (EstaRetrasada) return "Retrasada";
                if (TiempoRequeridoCumplido) return "Tiempo cumplido";
                if (EstaProximaAFinalizar) return "Próxima a terminar";
                if (EstaEnProceso) return "En secado";
                return "Pendiente";
            }
        }
    }

    public sealed class ProduccionSecadoCargaMaterialVm
    {
        public long SecadoCargaMaterialID { get; set; }
        public long SecadoCargaID { get; set; }
        public long SecadoMaterialID { get; set; }
        public int? ProgramaProduccionID { get; set; }
        public int? EjecucionProduccionID { get; set; }
        public string? NumeroOF { get; set; }
        public string? LadoLhRh { get; set; }
        public int MaterialID { get; set; }
        public string? MaterialCodigo { get; set; }
        public string? MaterialDescripcion { get; set; }
        public decimal CantidadKg { get; set; }
        public bool EsPrincipal { get; set; }
        public bool Activo { get; set; } = true;
        public string TextoOF
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(LadoLhRh) && !string.IsNullOrWhiteSpace(NumeroOF)) return $"{LadoLhRh} · {NumeroOF}";
                if (!string.IsNullOrWhiteSpace(NumeroOF)) return NumeroOF;
                return "OF sin identificar";
            }
        }
    }
    public sealed class ProduccionSecadoTolvaVm
    {
        public int TolvaID { get; set; }
        public int MaquinaID { get; set; }
        public string MaquinaCodigo { get; set; } = string.Empty;
        public string? MaquinaNombre { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public decimal CapacidadKg { get; set; }
        public string TipoProcesoPermitido { get; set; } = "AMBOS";
        public bool DisponibleOperativamente { get; set; }
        public bool EsDatoTemporal { get; set; }
        public bool Activo { get; set; }
        public bool EstaOcupada { get; set; }
        public long? SecadoCargaIDActiva { get; set; }
        public string? NumeroOFActiva { get; set; }
        public string? MaterialCodigoActivo { get; set; }
        public DateTime? FechaFinEsperadaActiva { get; set; }

        public bool DisponibleParaUsar =>
            Activo &&
            DisponibleOperativamente &&
            !EstaOcupada;

        public string Texto
        {
            get
            {
                var capacidad = $"{CapacidadKg:0.####} KG";
                var maquina = string.IsNullOrWhiteSpace(MaquinaCodigo) ? string.Empty : $" · {MaquinaCodigo}";
                return $"{Nombre}{maquina} · {capacidad}";
            }
        }

        public string EstadoTexto
        {
            get
            {
                if (!Activo) return "Inactiva";
                if (!DisponibleOperativamente) return "No disponible";
                if (EstaOcupada) return "Ocupada";
                return "Disponible";
            }
        }
    }

    public sealed class ProduccionSecadoSegmentoVm
    {
        public long SecadoCargaSegmentoID { get; set; }
        public long SecadoCargaID { get; set; }
        public int TolvaID { get; set; }
        public string? TolvaCodigo { get; set; }
        public string? TolvaNombre { get; set; }
        public int NumeroSegmento { get; set; }
        public DateTime FechaInicio { get; set; }
        public DateTime? FechaFin { get; set; }
        public int? MinutosSegmento { get; set; }
        public bool EsCambioTolva { get; set; }
        public bool ReiniciaTiempoRequerido { get; set; }
        public string? MotivoCambio { get; set; }
        public int? UsuarioInicioID { get; set; }
        public string? UsuarioInicioNombre { get; set; }
        public int? UsuarioFinID { get; set; }
        public string? UsuarioFinNombre { get; set; }

        public bool EstaAbierto => !FechaFin.HasValue;

        public string TextoTolva
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(TolvaCodigo) && !string.IsNullOrWhiteSpace(TolvaNombre))
                    return $"{TolvaCodigo} - {TolvaNombre}";
                return TolvaNombre ?? TolvaCodigo ?? "Sin tolva";
            }
        }
    }

    public sealed class ProduccionIniciarSecadoComponenteVm
    {
        public long SecadoMaterialID { get; set; }
        public decimal CantidadKg { get; set; }
    }
    public sealed class ProduccionIniciarSecadoVm
    {
        public long SecadoMaterialID { get; set; }
        public int TolvaID { get; set; }
        public decimal CantidadKg { get; set; }
        public List<ProduccionIniciarSecadoComponenteVm> Componentes { get; set; } = new();
        public string? Observaciones { get; set; }
    }

    public sealed class ProduccionCambiarTolvaVm
    {
        public long SecadoCargaID { get; set; }
        public int TolvaNuevaID { get; set; }
        public string? MotivoCambio { get; set; }
    }

    public sealed class ProduccionFinalizarSecadoVm
    {
        public long SecadoCargaID { get; set; }
        public bool ConfirmarFinalizacionAnticipada { get; set; }
        public string? MotivoFinalizacionAnticipada { get; set; }
        public string? Observaciones { get; set; }
    }

    public sealed class ProduccionSecadoHistorialVm
    {
        public long SecadoHistorialID { get; set; }
        public long SecadoMaterialID { get; set; }
        public long? SecadoCargaID { get; set; }
        public string Evento { get; set; } = string.Empty;
        public string? EstadoAnterior { get; set; }
        public string? EstadoNuevo { get; set; }
        public int? TolvaAnteriorID { get; set; }
        public string? TolvaAnteriorTexto { get; set; }
        public int? TolvaNuevaID { get; set; }
        public string? TolvaNuevaTexto { get; set; }
        public decimal? CantidadKg { get; set; }
        public string? Comentario { get; set; }
        public int? UsuarioID { get; set; }
        public string? UsuarioNombre { get; set; }
        public DateTime FechaEvento { get; set; }
    }
}