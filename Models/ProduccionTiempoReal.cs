using System;
using System.Collections.Generic;
using static ERP.NSQuell.Models.ProduccionOperadorCajaVm;

namespace ERP.NSQuell.Models
{
    public static class ProduccionTipoLecturaContador
    {
        public const string InicioCorrida = "INICIO_CORRIDA";
        public const string FinBloque = "FIN_BLOQUE";
        public const string CambioConfiguracion = "CAMBIO_CONFIGURACION";
        public const string CambioTurno = "CAMBIO_TURNO";
        public const string ReinicioContador = "REINICIO_CONTADOR";
        public const string TiempoExtra = "TIEMPO_EXTRA";

        public static string Nombre(string? tipo)
        {
            return tipo?.Trim().ToUpperInvariant() switch
            {
                InicioCorrida => "Inicio de corrida",
                FinBloque => "Fin de bloque",
                CambioConfiguracion => "Cambio de configuración",
                CambioTurno => "Cambio de turno",
                ReinicioContador => "Reinicio de contador",
                TiempoExtra => "Tiempo extra",
                _ => "Lectura de contador"
            };
        }
    }

    public static class ProduccionTipoMovimientoBonus
    {
        public const string ProduccionHoraProvisional = "PRODUCCION_HORA_PROVISIONAL";
        public const string ScrapConfirmadoCalidad = "SCRAP_CONFIRMADO_CALIDAD";
        public const string RecuperacionCalidad = "RECUPERACION_CALIDAD";
        public const string ScrapConfirmadoGP12 = "SCRAP_CONFIRMADO_GP12";
        public const string CorreccionPositiva = "CORRECCION_POSITIVA";
        public const string CorreccionNegativa = "CORRECCION_NEGATIVA";

        public static string Nombre(string? tipo)
        {
            return tipo?.Trim().ToUpperInvariant() switch
            {
                ProduccionHoraProvisional => "Producción por hora",
                ScrapConfirmadoCalidad => "Scrap confirmado por Calidad",
                RecuperacionCalidad => "Material recuperado por Calidad",
                ScrapConfirmadoGP12 => "Scrap confirmado por GP12",
                CorreccionPositiva => "Corrección positiva",
                CorreccionNegativa => "Corrección negativa",
                _ => "Movimiento de bonus"
            };
        }
    }

    public sealed class ProduccionConfiguracionCorridaVm
    {
        public int ConfiguracionCorridaID { get; set; }
        public int EjecucionProduccionID { get; set; }

        public int CavidadesUsadas { get; set; }
        public decimal TiempoCicloSegundos { get; set; }
        public decimal ObjetivoHoraCalculado { get; set; }

        public long? ContadorInicioVigencia { get; set; }
        public long? ContadorFinVigencia { get; set; }

        public DateTime FechaInicioVigencia { get; set; }
        public DateTime? FechaFinVigencia { get; set; }

        public bool EsConfiguracionInicial { get; set; }

        public string? MotivoCambio { get; set; }

        public int? TecnicoProduccionID { get; set; }
        public string? TecnicoProduccionNombre { get; set; }

        public int? UsuarioCreacionID { get; set; }
        public DateTime FechaCreacion { get; set; }

        public int? UsuarioModificacionID { get; set; }
        public DateTime? FechaModificacion { get; set; }

        public string? CavidadesConfiguradas { get; set; }
        public bool Activo { get; set; } = true;

        public bool EstaVigente =>
            Activo &&
            !FechaFinVigencia.HasValue;

        public int ObjetivoHoraOperativo =>
            ObjetivoHoraCalculado > 0
                ? (int)Math.Round(
                    ObjetivoHoraCalculado,
                    0,
                    MidpointRounding.AwayFromZero)
                : 0;

        public string TextoConfiguracion =>
            $"{CavidadesUsadas:N0} cavidad(es) · " +
            $"{TiempoCicloSegundos:0.####} s · " +
            $"{ObjetivoHoraOperativo:N0} pzas/h";
    }

    public sealed class ProduccionConfiguracionTecnicoPostVm
    {
        public int EjecucionProduccionID { get; set; }

        // Cavidades de la ejecución desde la cual se abrió la pantalla.
        public int CavidadesUsadas { get; set; }
        public string? CavidadesConfiguradas { get; set; }

        public int? CavidadesUsadasPareja { get; set; }
        public string? CavidadesConfiguradasPareja { get; set; }

        // Datos físicos compartidos por LH/RH.
        public decimal TiempoCicloSegundos { get; set; }
        public long? ContadorMaquinaActual { get; set; }

        // Un solo motivo porque el cambio es una intervención física conjunta.
        public string? MotivoCambio { get; set; }
    }

    public sealed class ProduccionConfiguracionTecnicoVm
    {
        public int EjecucionProduccionID { get; set; }
        public int ProgramaProduccionID { get; set; }

        public int? MaquinaID { get; set; }
        public string? MaquinaCodigo { get; set; }
        public string? MaquinaNombre { get; set; }

        public int? ParteID { get; set; }
        public string? NumeroParte { get; set; }
        public string? ReferenciaSAP { get; set; }

        public int? CavidadesBD { get; set; }
        public decimal? TiempoCicloBD { get; set; }

        public List<int> CavidadesDisponibles { get; set; } = new();

        public ProduccionConfiguracionCorridaVm? ConfiguracionActual { get; set; }

        public long? UltimoContadorMaquina { get; set; }

        public List<ProduccionConfiguracionCorridaVm>
            HistorialConfiguraciones
        { get; set; } = new();

        public ProduccionParejaLhRhVm? ParejaLhRh { get; set; }

        public string? LadoLhRh { get; set; }
        public string? LadoParejaLhRh { get; set; }

       
        public int? CavidadesBDPareja { get; set; }
        public decimal? TiempoCicloBDPareja { get; set; }

        public List<int> CavidadesDisponiblesPareja { get; set; } = new();

     
        public ProduccionConfiguracionCorridaVm? ConfiguracionActualPareja { get; set; }

        public long? UltimoContadorMaquinaPareja { get; set; }

        public List<ProduccionConfiguracionCorridaVm>
            HistorialConfiguracionesPareja
        { get; set; } = new();

       
        public bool TieneConfiguracionActual =>
            ConfiguracionActual != null &&
            ConfiguracionActual.EstaVigente;

        public int? CavidadesActuales =>
            ConfiguracionActual?.CavidadesUsadas;

        public string? CavidadesConfiguradasActuales =>
            ConfiguracionActual?.CavidadesConfiguradas;

        public decimal? CicloActual =>
            ConfiguracionActual?.TiempoCicloSegundos;

        public int? ObjetivoHoraActual =>
            ConfiguracionActual != null
                ? ConfiguracionActual.ObjetivoHoraOperativo
                : null;

        public long? ContadorBaseActual =>
            ConfiguracionActual?.ContadorInicioVigencia;

       
        public bool EsParejaLhRh =>
            ParejaLhRh != null &&
            ParejaLhRh.TieneEjecucionPareja;

        public int? GrupoLhRh =>
            ParejaLhRh?.GrupoLhRh;

        public int? EjecucionParejaID =>
            ParejaLhRh?.EjecucionParejaID;

        public int? ProgramaParejaID =>
            ParejaLhRh?.ProgramaParejaID;

        public bool TieneConfiguracionActualPareja =>
            ConfiguracionActualPareja != null &&
            ConfiguracionActualPareja.EstaVigente;

        public int? CavidadesActualesPareja =>
            ConfiguracionActualPareja?.CavidadesUsadas;

        public string? CavidadesConfiguradasActualesPareja =>
            ConfiguracionActualPareja?.CavidadesConfiguradas;

        public decimal? CicloActualPareja =>
            ConfiguracionActualPareja?.TiempoCicloSegundos;

        public int? ObjetivoHoraActualPareja =>
            ConfiguracionActualPareja != null
                ? ConfiguracionActualPareja.ObjetivoHoraOperativo
                : null;

        public long? ContadorBasePareja =>
            ConfiguracionActualPareja?.ContadorInicioVigencia;

        public bool CicloCompartidoSincronizado
        {
            get
            {
                if (!EsParejaLhRh)
                    return true;

                if (!TieneConfiguracionActual ||
                    !TieneConfiguracionActualPareja)
                {
                    return false;
                }

                return Math.Abs(
                    ConfiguracionActual!.TiempoCicloSegundos -
                    ConfiguracionActualPareja!.TiempoCicloSegundos) < 0.0001m;
            }
        }

        public bool ContadorCompartidoSincronizado
        {
            get
            {
                if (!EsParejaLhRh)
                    return true;

                if (!TieneConfiguracionActual ||
                    !TieneConfiguracionActualPareja)
                {
                    return false;
                }

                return ConfiguracionActual!.ContadorInicioVigencia.HasValue &&
                       ConfiguracionActualPareja!.ContadorInicioVigencia.HasValue &&
                       ConfiguracionActual.ContadorInicioVigencia.Value ==
                       ConfiguracionActualPareja.ContadorInicioVigencia.Value;
            }
        }

        public bool ConfiguracionFisicaSincronizada =>
            CicloCompartidoSincronizado &&
            ContadorCompartidoSincronizado;

       
        public string TextoParte =>
            !string.IsNullOrWhiteSpace(ReferenciaSAP)
                ? ReferenciaSAP
                : !string.IsNullOrWhiteSpace(NumeroParte)
                    ? NumeroParte
                    : "Sin parte";

        public string TextoMaquina =>
            string.IsNullOrWhiteSpace(MaquinaCodigo)
                ? "Sin máquina"
                : string.IsNullOrWhiteSpace(MaquinaNombre)
                    ? MaquinaCodigo
                    : $"{MaquinaCodigo} - {MaquinaNombre}";

        public string TextoOFPareja =>
            ParejaLhRh?.OFParejaTexto ??
            "Sin OF pareja";

        public string TextoPartePareja =>
            ParejaLhRh?.ParteParejaTexto ??
            "Sin parte pareja";
    }

    public sealed class ProduccionContadorMaquinaLecturaVm
    {
        public long LecturaContadorID { get; set; }

        public int EjecucionProduccionID { get; set; }
        public int? ConfiguracionCorridaID { get; set; }

        public int? MaquinaID { get; set; }

        public int? OperadorID { get; set; }
        public string? OperadorNombre { get; set; }

        public string TipoLectura { get; set; } = string.Empty;

        public long ValorContador { get; set; }

        public DateTime FechaLectura { get; set; }

        public bool EsReinicioContador { get; set; }

        public string? MotivoReinicio { get; set; }
        public string? Observaciones { get; set; }

        public int? RegistroHoraID { get; set; }

        public int? UsuarioCreacionID { get; set; }
        public DateTime FechaCreacion { get; set; }

        public bool Activo { get; set; } = true;

        public string TipoLecturaNombre =>
            ProduccionTipoLecturaContador.Nombre(TipoLectura);
    }

    public sealed class ProduccionRegistroHoraSegmentoVm
    {
        public long RegistroHoraSegmentoID { get; set; }

        public int RegistroHoraID { get; set; }
        public int EjecucionProduccionID { get; set; }
        public int ConfiguracionCorridaID { get; set; }

        public int NumeroSegmento { get; set; }

        public DateTime FechaHoraInicio { get; set; }
        public DateTime FechaHoraFin { get; set; }

        public decimal MinutosProductivos { get; set; }

        public long ContadorInicial { get; set; }
        public long ContadorFinal { get; set; }

        public long CiclosPeriodo { get; set; }

        public int CavidadesUsadas { get; set; }

        public decimal TiempoCicloSegundos { get; set; }

        public long PiezasCalculadas { get; set; }

        public decimal ObjetivoHoraCalculado { get; set; }
        public decimal? ObjetivoSegmentoCalculado { get; set; }

        public string? Observaciones { get; set; }

        public int? UsuarioCreacionID { get; set; }
        public DateTime FechaCreacion { get; set; }

        public bool Activo { get; set; } = true;

        public int ObjetivoSegmentoOperativo =>
            ObjetivoSegmentoCalculado.HasValue &&
            ObjetivoSegmentoCalculado.Value > 0
                ? (int)Math.Round(
                    ObjetivoSegmentoCalculado.Value,
                    0,
                    MidpointRounding.AwayFromZero)
                : 0;

        public string Rango =>
            $"{FechaHoraInicio:HH:mm} - {FechaHoraFin:HH:mm}";
    }

    public sealed class ProduccionBonusOperadorMovimientoVm
    {
        public long MovimientoBonusID { get; set; }

        public int OperadorID { get; set; }
        public string? OperadorNombre { get; set; }

        public int? EjecucionProduccionID { get; set; }
        public int? RegistroHoraID { get; set; }

        public int? MonitoreoID { get; set; }
        public int? DisposicionID { get; set; }

        public string TipoMovimiento { get; set; } = string.Empty;

        public int PiezasMovimiento { get; set; }

        public int? PiezasReferencia { get; set; }

        public string Motivo { get; set; } = string.Empty;

        public string? ReferenciaEvento { get; set; }

        public int? UsuarioCreacionID { get; set; }

        public DateTime FechaMovimiento { get; set; }

        public bool Activo { get; set; } = true;

        public bool EsAbono =>
            PiezasMovimiento > 0;

        public bool EsDescuento =>
            PiezasMovimiento < 0;

        public string TipoMovimientoNombre =>
            ProduccionTipoMovimientoBonus.Nombre(TipoMovimiento);

        public string Signo =>
            PiezasMovimiento > 0
                ? "+"
                : string.Empty;
    }

    public sealed class ProduccionBonusOperadorResumenVm
    {
        public int OperadorID { get; set; }
        public string? OperadorNombre { get; set; }

        public int TotalProduccionAbonada { get; set; }
        public int TotalDescontado { get; set; }

        public int BonusActualPiezas { get; set; }

        public List<ProduccionBonusOperadorMovimientoVm>
            Movimientos
        { get; set; } = new();
    }

    public static class ProduccionTiempoExtraEstado
    {
        public const string EnCurso = "EN_CURSO";
        public const string Pausado = "PAUSADO";
        public const string Finalizado = "FINALIZADO";
        public const string Cancelado = "CANCELADO";

        public static string Nombre(string? estado)
        {
            return estado?.Trim().ToUpperInvariant() switch
            {
                EnCurso => "En curso",
                Pausado => "Pausado",
                Finalizado => "Finalizado",
                Cancelado => "Cancelado",
                _ => "Sin estado"
            };
        }
    }

    public static class ProduccionTiempoExtraMotivo
    {
        public const string RecuperarAtraso = "RECUPERAR_ATRASO";
        public const string CompletarOF = "COMPLETAR_OF";
        public const string ProduccionAdicionalAutorizada = "PRODUCCION_ADICIONAL_AUTORIZADA";
        public const string AjusteProceso = "AJUSTE_PROCESO";
        public const string Otro = "OTRO";

        public static string Nombre(string? motivo)
        {
            return motivo?.Trim().ToUpperInvariant() switch
            {
                RecuperarAtraso => "Recuperar atraso",
                CompletarOF => "Completar OF",
                ProduccionAdicionalAutorizada => "Producción adicional autorizada",
                AjusteProceso => "Ajuste de proceso",
                Otro => "Otro",
                _ => string.IsNullOrWhiteSpace(motivo) ? "Sin motivo" : motivo
            };
        }
    }

    public sealed class ProduccionTiempoExtraVm
    {
        public int TiempoExtraID { get; set; }
        public int EjecucionProduccionID { get; set; }

        public int OperadorInicioID { get; set; }
        public string? OperadorInicioNombre { get; set; }

        public int? OperadorFinID { get; set; }
        public string? OperadorFinNombre { get; set; }

        public int? ConfiguracionCorridaInicioID { get; set; }

        public DateTime FechaHoraInicio { get; set; }
        public DateTime FechaHoraUltimoCorte { get; set; }
        public DateTime? FechaHoraFin { get; set; }

        public long ContadorInicio { get; set; }
        public long ContadorUltimoCorte { get; set; }
        public long? ContadorFin { get; set; }

        public string Estado { get; set; } = ProduccionTiempoExtraEstado.EnCurso;

        public string Motivo { get; set; } = string.Empty;
        public string? Observaciones { get; set; }

        public int UsuarioCreacionID { get; set; }
        public DateTime FechaCreacion { get; set; }

        public int? UsuarioModificacionID { get; set; }
        public DateTime? FechaModificacion { get; set; }

        public int? UsuarioCancelacionID { get; set; }
        public DateTime? FechaCancelacion { get; set; }
        public string? MotivoCancelacion { get; set; }

        public bool Activo { get; set; } = true;

        public List<ProduccionTiempoExtraCorteVm> Cortes { get; set; } = new();

        public bool EstaEnCurso =>
            Activo &&
            string.Equals(
                Estado,
                ProduccionTiempoExtraEstado.EnCurso,
                StringComparison.OrdinalIgnoreCase) &&
            !FechaHoraFin.HasValue;

        public bool EstaFinalizado =>
            string.Equals(
                Estado,
                ProduccionTiempoExtraEstado.Finalizado,
                StringComparison.OrdinalIgnoreCase);

        public bool EstaCancelado =>
            string.Equals(
                Estado,
                ProduccionTiempoExtraEstado.Cancelado,
                StringComparison.OrdinalIgnoreCase);

        public DateTime FechaHoraProximoCorte =>
            FechaHoraUltimoCorte.AddMinutes(60);

        public int NumeroSiguienteCorte =>
            Cortes.Count == 0
                ? 1
                : Cortes.Max(x => x.NumeroCorte) + 1;

        public double SegundosDesdeUltimoCorte
        {
            get
            {
                var fin = FechaHoraFin ?? DateTime.Now;
                return Math.Max(
                    0,
                    (fin - FechaHoraUltimoCorte).TotalSeconds);
            }
        }

        public int MinutosDesdeUltimoCorte =>
            (int)Math.Floor(
                SegundosDesdeUltimoCorte / 60d);

        public bool RequiereCorte60 =>
            EstaEnCurso &&
            DateTime.Now >= FechaHoraProximoCorte;

        public string EstadoNombre =>
            ProduccionTiempoExtraEstado.Nombre(Estado);

        public string MotivoNombre =>
            ProduccionTiempoExtraMotivo.Nombre(Motivo);

        public string RangoTexto =>
            FechaHoraFin.HasValue
                ? $"{FechaHoraInicio:dd/MM/yyyy HH:mm} - {FechaHoraFin.Value:dd/MM/yyyy HH:mm}"
                : $"{FechaHoraInicio:dd/MM/yyyy HH:mm} - En curso";
    }

    public sealed class ProduccionTiempoExtraCorteVm
    {
        public int RegistroHoraID { get; set; }
        public int TiempoExtraID { get; set; }
        public int NumeroCorte { get; set; }

        public int OperadorID { get; set; }
        public string? OperadorNombre { get; set; }

        public DateTime FechaHoraInicio { get; set; }
        public DateTime FechaHoraFin { get; set; }

        public long? ContadorInicial { get; set; }
        public long? ContadorFinal { get; set; }

        public int PiezasCalculadasContador { get; set; }

        public int CantidadOK { get; set; }
        public int CantidadSospechosa { get; set; }
        public int CantidadScrap { get; set; }

        public decimal MinutosProductivos { get; set; }

        public int? ObjetivoBloque { get; set; }

        public bool EsCorteFinal { get; set; }

        public string? Observaciones { get; set; }

        public int TotalClasificado =>
            CantidadOK +
            CantidadSospechosa +
            CantidadScrap;

        public string RangoTexto =>
            $"{FechaHoraInicio:HH:mm} - {FechaHoraFin:HH:mm}";
    }

    public sealed class ProduccionTiempoExtraIniciarPostVm
    {
        public int EjecucionProduccionID { get; set; }

        public string Motivo { get; set; } =
            ProduccionTiempoExtraMotivo.CompletarOF;

        public string? Observaciones { get; set; }
    }

    public sealed class ProduccionTiempoExtraCortePostVm
    {
        public int TiempoExtraID { get; set; }
        public int EjecucionProduccionID { get; set; }

        public long? ContadorMaquinaActual { get; set; }

        public int CantidadOK { get; set; }
        public bool OkModificadoManual { get; set; }

        public int CantidadSospechosa { get; set; }
        public int CantidadScrap { get; set; }

        public string? Observaciones { get; set; }

        public bool FinalizarTiempoExtra { get; set; }

        public List<ProduccionRegistroDefectoPostVm> DefectosScrap { get; set; } = new();
    }


}