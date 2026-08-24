using System;
using System.Collections.Generic;

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
        public const string CorreccionPositiva = "CORRECCION_POSITIVA";
        public const string CorreccionNegativa = "CORRECCION_NEGATIVA";

        public static string Nombre(string? tipo)
        {
            return tipo?.Trim().ToUpperInvariant() switch
            {
                ProduccionHoraProvisional => "Producción por hora",
                ScrapConfirmadoCalidad => "Scrap confirmado por Calidad",
                RecuperacionCalidad => "Material recuperado por Calidad",
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

        public int CavidadesUsadas { get; set; }

        public decimal TiempoCicloSegundos { get; set; }

        public long? ContadorMaquinaActual { get; set; }

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

        // Datos maestros. Solo referencia/sugerencia.
        public int? CavidadesBD { get; set; }
        public decimal? TiempoCicloBD { get; set; }

        // Datos reales confirmados por el técnico.
        public ProduccionConfiguracionCorridaVm? ConfiguracionActual { get; set; }

        public long? UltimoContadorMaquina { get; set; }

        public List<ProduccionConfiguracionCorridaVm>
            HistorialConfiguraciones
        { get; set; } = new();

        public bool TieneConfiguracionActual =>
            ConfiguracionActual != null &&
            ConfiguracionActual.EstaVigente;

        public int? CavidadesActuales =>
            ConfiguracionActual?.CavidadesUsadas;

        public decimal? CicloActual =>
            ConfiguracionActual?.TiempoCicloSegundos;

        public int? ObjetivoHoraActual =>
            ConfiguracionActual != null
                ? ConfiguracionActual.ObjetivoHoraOperativo
                : null;

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
}