using System;
using System.Collections.Generic;

namespace ERP.NSQuell.Models;

public sealed class ProduccionBonusIndexVm
{
    public DateTime FechaReferencia { get; set; }
    public DateTime SemanaInicio { get; set; }
    public DateTime SemanaFin { get; set; }
    public bool EsSemanaActual { get; set; }
    public int TotalOperadores { get; set; }
    public int TotalMovimientos { get; set; }
    public long TotalPiezasAbonadas { get; set; }
    public long TotalPiezasDescontadas { get; set; }
    public long BonusNetoSemana { get; set; }
    public List<ProduccionBonusRankingItemVm> Ranking { get; set; } = new();

    public string SemanaTexto => $"{SemanaInicio:dd/MM/yyyy} - {SemanaFin:dd/MM/yyyy}";
    public string SemanaTitulo => EsSemanaActual ? $"Semana actual · {SemanaTexto}" : $"Semana · {SemanaTexto}";
    public bool TieneMovimientos => TotalMovimientos > 0;
}

public sealed class ProduccionBonusRankingItemVm
{
    public int Posicion { get; set; }
    public int OperadorID { get; set; }
    public string OperadorNombre { get; set; } = string.Empty;
    public long PiezasAbonadas { get; set; }
    public long PiezasDescontadas { get; set; }
    public long BonusNeto { get; set; }
    public int TotalMovimientos { get; set; }
    public int TotalOF { get; set; }
    public DateTime? UltimoMovimiento { get; set; }

    public bool EsPrimerLugar => Posicion == 1;
    public bool EsSegundoLugar => Posicion == 2;
    public bool EsTercerLugar => Posicion == 3;
    public bool EsPodio => Posicion >= 1 && Posicion <= 3;

    public string Medalla => Posicion switch
    {
        1 => "🥇",
        2 => "🥈",
        3 => "🥉",
        _ => string.Empty
    };

    public string PosicionTexto => EsPodio ? $"{Medalla} {Posicion}" : Posicion.ToString();
}

public sealed class ProduccionBonusDetalleOperadorVm
{
    public int OperadorID { get; set; }
    public string OperadorNombre { get; set; } = string.Empty;
    public DateTime FechaReferencia { get; set; }
    public DateTime SemanaInicio { get; set; }
    public DateTime SemanaFin { get; set; }
    public bool EsSemanaActual { get; set; }
    public int PosicionRanking { get; set; }
    public int TotalOF { get; set; }
    public int TotalMovimientos { get; set; }
    public long PiezasAbonadas { get; set; }
    public long PiezasDescontadas { get; set; }
    public long BonusNeto { get; set; }
    public List<ProduccionBonusOfResumenVm> OrdenesFabricacion { get; set; } = new();

    public string SemanaTexto => $"{SemanaInicio:dd/MM/yyyy} - {SemanaFin:dd/MM/yyyy}";
    public bool TieneMovimientos => TotalMovimientos > 0;

    public string PosicionTexto => PosicionRanking switch
    {
        1 => "🥇 1er lugar",
        2 => "🥈 2do lugar",
        3 => "🥉 3er lugar",
        > 3 => $"Lugar #{PosicionRanking}",
        _ => "Sin posición"
    };
}

public sealed class ProduccionBonusOfResumenVm
{
    public int EjecucionProduccionID { get; set; }
    public int? ProgramaProduccionID { get; set; }
    public int? SolicitudProduccionID { get; set; }
    public int? SolicitudProduccionDetalleID { get; set; }
    public string? NumeroOF { get; set; }
    public int? ParteID { get; set; }
    public string? NumeroParte { get; set; }
    public string? ReferenciaSAP { get; set; }
    public string? DescripcionParte { get; set; }
    public int? MaquinaID { get; set; }
    public string? MaquinaCodigo { get; set; }
    public string? MaquinaNombre { get; set; }
    public DateTime? FechaInicioReal { get; set; }
    public DateTime? FechaFinReal { get; set; }
    public long PiezasAbonadas { get; set; }
    public long PiezasDescontadas { get; set; }
    public long BonusNeto { get; set; }
    public int TotalMovimientos { get; set; }
    public int TotalCapturas { get; set; }
    public int TotalCapturasTiempoExtra { get; set; }
    public List<ProduccionBonusMovimientoDetalleVm> Movimientos { get; set; } = new();

    public string NumeroOFTexto => !string.IsNullOrWhiteSpace(NumeroOF)
        ? NumeroOF
        : SolicitudProduccionID.HasValue
            ? $"OF-ID-{SolicitudProduccionID.Value}"
            : $"Ejecución #{EjecucionProduccionID}";

    public string MaquinaTexto => !string.IsNullOrWhiteSpace(MaquinaCodigo) && !string.IsNullOrWhiteSpace(MaquinaNombre)
        ? $"{MaquinaCodigo} - {MaquinaNombre}"
        : !string.IsNullOrWhiteSpace(MaquinaCodigo)
            ? MaquinaCodigo
            : !string.IsNullOrWhiteSpace(MaquinaNombre)
                ? MaquinaNombre
                : "Sin máquina";

    public string ParteTexto => !string.IsNullOrWhiteSpace(NumeroParte) && !string.IsNullOrWhiteSpace(DescripcionParte)
        ? $"{NumeroParte} - {DescripcionParte}"
        : !string.IsNullOrWhiteSpace(NumeroParte)
            ? NumeroParte
            : "Sin parte";
}

public sealed class ProduccionBonusMovimientoDetalleVm
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
    public bool Activo { get; set; }

    public DateTime? FechaProduccion { get; set; }
    public TimeSpan? HoraInicio { get; set; }
    public TimeSpan? HoraFin { get; set; }
    public bool EsTiempoExtra { get; set; }
    public int? NumeroCorteTiempoExtra { get; set; }
    public string? TipoBloque { get; set; }
    public int? CantidadOK { get; set; }
    public int? CantidadSospechosa { get; set; }
    public int? CantidadScrap { get; set; }

    public bool EsAbono => PiezasMovimiento > 0;
    public bool EsDescuento => PiezasMovimiento < 0;
    public long PiezasAbsolutas => Math.Abs((long)PiezasMovimiento);

    public string Signo => PiezasMovimiento > 0
        ? "+"
        : PiezasMovimiento < 0
            ? "-"
            : string.Empty;

    public string TipoMovimientoNombre => ProduccionTipoMovimientoBonus.Nombre(TipoMovimiento);

    public string TipoRegistroTexto => EsTiempoExtra
        ? NumeroCorteTiempoExtra.HasValue
            ? $"Tiempo extra · corte #{NumeroCorteTiempoExtra.Value}"
            : "Tiempo extra"
        : RegistroHoraID.HasValue
            ? "Captura normal"
            : "Ajuste";

    public string RangoHoraTexto
    {
        get
        {
            if (!HoraInicio.HasValue || !HoraFin.HasValue) return string.Empty;
            return $"{HoraInicio.Value:hh\\:mm} - {HoraFin.Value:hh\\:mm}";
        }
    }
}

public sealed class ProduccionBonusHistorialVm
{
    public DateTime FechaReferencia { get; set; }
    public int NumeroSemanas { get; set; } = 12;
    public List<ProduccionBonusSemanaResumenVm> Semanas { get; set; } = new();

    public bool TieneSemanas => Semanas.Count > 0;
}

public sealed class ProduccionBonusSemanaResumenVm
{
    public DateTime SemanaInicio { get; set; }
    public DateTime SemanaFin { get; set; }
    public bool EsSemanaActual { get; set; }
    public int TotalOperadores { get; set; }
    public int TotalMovimientos { get; set; }
    public long PiezasAbonadas { get; set; }
    public long PiezasDescontadas { get; set; }
    public long BonusNeto { get; set; }
    public int? PrimerLugarOperadorID { get; set; }
    public string? PrimerLugarOperadorNombre { get; set; }
    public long PrimerLugarBonus { get; set; }

    public string SemanaTexto => $"{SemanaInicio:dd/MM/yyyy} - {SemanaFin:dd/MM/yyyy}";
}

public sealed class ProduccionBonusResumenGeneralVm
{
    public DateTime FechaDesde { get; set; }
    public DateTime FechaHasta { get; set; }
    public int TotalOperadores { get; set; }
    public int TotalMovimientos { get; set; }
    public long PiezasAbonadas { get; set; }
    public long PiezasDescontadas { get; set; }
    public long BonusNeto { get; set; }

    public string PeriodoTexto => $"{FechaDesde:dd/MM/yyyy} - {FechaHasta:dd/MM/yyyy}";
}