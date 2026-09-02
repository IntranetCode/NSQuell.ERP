using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace ERP.NSQuell.Models.ViewModels;

public sealed class CalidadHCCBandejaViewModel
{
    public string? Busqueda { get; set; }
    public string? Estado { get; set; }
    public int Pendientes { get; set; }
    public int CompletadasHoy { get; set; }
    public int SinPlantilla { get; set; }
    public int TotalMostrados => Requerimientos.Count;
    public List<CalidadHCCRequerimientoItemViewModel> Requerimientos { get; } = new();
    public List<CalidadHCCSinPlantillaViewModel> InspeccionesSinPlantilla { get; } = new();
}

public sealed class CalidadHCCRequerimientoItemViewModel
{
    public long RequerimientoHCCID { get; set; }
    public int PlantillaHCCID { get; set; }
    public int ParteID { get; set; }
    public int? InspeccionID { get; set; }
    public int? EjecucionProduccionID { get; set; }
    public int? CambioTurnoID { get; set; }
    public string TipoOrigen { get; set; } = string.Empty;
    public string TipoEventoSugerido { get; set; } = string.Empty;
    public string OrdenFabricacion { get; set; } = string.Empty;
    public string ClienteNombre { get; set; } = string.Empty;
    public string NumeroParte { get; set; } = string.Empty;
    public string DescripcionParte { get; set; } = string.Empty;
    public string MaquinaTexto { get; set; } = string.Empty;
    public string Turno { get; set; } = string.Empty;
    public string OperadorTexto { get; set; } = string.Empty;
    public DateTime FechaHoraRequerida { get; set; }
    public string Estado { get; set; } = string.Empty;
    public long? RegistroHCCID { get; set; }
    public string NumeroHCC { get; set; } = string.Empty;
    public string VersionFormato { get; set; } = string.Empty;
    public int Caracteristicas { get; set; }
    public int ChecklistItems { get; set; }

    public string TipoOrigenTexto => TipoOrigen switch
    {
        "ARRANQUE" => "Arranque / cambio de producción",
        "CAMBIO_TURNO" => "Cambio de turno",
        "RELIBERACION" => "Reliberación",
        "MONITOREO" => "Monitoreo",
        _ => TipoOrigen
    };

    public string EventoTexto => TipoEventoSugerido switch
    {
        "L" => "Liberación (L)",
        "M" => "Monitoreo (M)",
        "RL" => "Reliberación (RL)",
        _ => TipoEventoSugerido
    };

    public bool EstaPendiente => Estado == "PENDIENTE" || Estado == "EN_CAPTURA";
}

public sealed class CalidadHCCSinPlantillaViewModel
{
    public int InspeccionID { get; set; }
    public int? ParteID { get; set; }
    public string OrdenFabricacion { get; set; } = string.Empty;
    public string NumeroParte { get; set; } = string.Empty;
    public string DescripcionParte { get; set; } = string.Empty;
    public string ClienteNombre { get; set; } = string.Empty;
    public string Maquina { get; set; } = string.Empty;
}

public sealed class CalidadHCCPlantillasIndexViewModel
{
    public string? Busqueda { get; set; }
    public List<CalidadHCCPlantillaResumenViewModel> Plantillas { get; } = new();
}

public sealed class CalidadHCCPlantillaResumenViewModel
{
    public int PlantillaHCCID { get; set; }
    public int ParteID { get; set; }
    public string NumeroParte { get; set; } = string.Empty;
    public string DescripcionParte { get; set; } = string.Empty;
    public string Cliente { get; set; } = string.Empty;
    public string NumeroHCC { get; set; } = string.Empty;
    public string VersionFormato { get; set; } = string.Empty;
    public DateTime? FechaRevision { get; set; }
    public bool EsVigente { get; set; }
    public int Caracteristicas { get; set; }
    public int Cavidades { get; set; }
    public int ChecklistItems { get; set; }
}

public sealed class CalidadHCCPlantillaViewModel
{
    public int PlantillaHCCID { get; set; }
    public int ParteID { get; set; }
    public string CodigoFormato { get; set; } = string.Empty;
    public string NumeroHCC { get; set; } = string.Empty;
    public string VersionFormato { get; set; } = string.Empty;
    public DateTime? FechaModificacionFormato { get; set; }
    public string Cliente { get; set; } = string.Empty;
    public string NumeroParte { get; set; } = string.Empty;
    public string ReferenciaSAP { get; set; } = string.Empty;
    public string Designacion { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string NumeroDibujo { get; set; } = string.Empty;
    public string Proceso { get; set; } = string.Empty;
    public string ReferenciaPlanControl { get; set; } = string.Empty;
    public string CodigoResina { get; set; } = string.Empty;
    public string MateriaPrima { get; set; } = string.Empty;
    public string TiempoSecadoTexto { get; set; } = string.Empty;
    public string TipoSecado { get; set; } = string.Empty;
    public decimal? HorasSecado { get; set; }
    public decimal? TemperaturaSecado { get; set; }
    public string UnidadTemperatura { get; set; } = string.Empty;
    public int NumeroTirosDefault { get; set; } = 3;
    public int? CavidadesDeclaradas { get; set; }
    public string ArchivoOrigen { get; set; } = string.Empty;
    public string HojaOrigen { get; set; } = string.Empty;
    public bool EsVigente { get; set; }
    public List<CalidadHCCCaracteristicaViewModel> Caracteristicas { get; } = new();
    public List<CalidadHCCChecklistItemViewModel> Checklist { get; } = new();
    public List<int> CavidadesDisponibles { get; } = new();
}

public sealed class CalidadHCCCaracteristicaViewModel
{
    public int CaracteristicaHCCID { get; set; }
    public int Orden { get; set; }
    public string TipoCaracteristica { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string EspecificacionTexto { get; set; } = string.Empty;
    public decimal? ValorNominal { get; set; }
    public decimal? ToleranciaMas { get; set; }
    public decimal? ToleranciaMenos { get; set; }
    public decimal? LimiteInferior { get; set; }
    public decimal? LimiteSuperior { get; set; }
    public string Unidad { get; set; } = string.Empty;
    public string Instrumento { get; set; } = string.Empty;
    public string CodigoGauge { get; set; } = string.Empty;
    public List<int> Cavidades { get; } = new();

    public bool EsVisual =>
        TipoCaracteristica.Equals("VISUAL", StringComparison.OrdinalIgnoreCase) ||
        Instrumento.Contains("VISUAL", StringComparison.OrdinalIgnoreCase);

    public bool EsNumerica =>
        !EsVisual &&
        (ValorNominal.HasValue || LimiteInferior.HasValue || LimiteSuperior.HasValue);

    public bool TieneLimites => LimiteInferior.HasValue || LimiteSuperior.HasValue;

    public string RangoTexto
    {
        get
        {
            if (LimiteInferior.HasValue && LimiteSuperior.HasValue)
                return $"{LimiteInferior.Value:0.######} – {LimiteSuperior.Value:0.######} {Unidad}".Trim();
            if (ValorNominal.HasValue && (ToleranciaMas.HasValue || ToleranciaMenos.HasValue))
                return $"{ValorNominal.Value:0.######} +{ToleranciaMas.GetValueOrDefault():0.######} / -{ToleranciaMenos.GetValueOrDefault():0.######} {Unidad}".Trim();
            if (ValorNominal.HasValue)
                return $"{ValorNominal.Value:0.######} {Unidad}".Trim();
            return EspecificacionTexto;
        }
    }
}

public sealed class CalidadHCCChecklistItemViewModel
{
    public int ChecklistHCCID { get; set; }
    public int Orden { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public bool PermiteNA { get; set; }
}

public sealed class CalidadHCCCapturaViewModel
{
    public long RequerimientoHCCID { get; set; }
    public int PlantillaHCCID { get; set; }
    public int ParteID { get; set; }
    public int? InspeccionID { get; set; }
    public int? EjecucionProduccionID { get; set; }
    public int? ProgramaProduccionID { get; set; }
    public int? SolicitudProduccionID { get; set; }
    public int? SolicitudProduccionDetalleID { get; set; }
    public string TipoOrigen { get; set; } = string.Empty;
    public string TipoEvento { get; set; } = string.Empty;
    public string OrdenFabricacion { get; set; } = string.Empty;
    public string ClienteNombre { get; set; } = string.Empty;
    public string NumeroParte { get; set; } = string.Empty;
    public string DescripcionParte { get; set; } = string.Empty;
    public int? MaquinaID { get; set; }
    public string MaquinaTexto { get; set; } = string.Empty;
    public string Turno { get; set; } = string.Empty;
    public string OperadorTexto { get; set; } = string.Empty;
    public string AuditorTexto { get; set; } = string.Empty;
    public DateTime FechaHoraRequerida { get; set; }
    public CalidadHCCPlantillaViewModel Plantilla { get; set; } = new();
    public List<int> CavidadesSeleccionadas { get; } = new();
    public string CavidadesConfiguradas => string.Join(",", CavidadesSeleccionadas);
    public int CantidadCavidadesConfiguradas => CavidadesSeleccionadas.Count;
}

public sealed class CalidadHCCCapturaPostViewModel
{
    [Range(1, long.MaxValue)]
    public long RequerimientoHCCID { get; set; }

    [Required]
    [MaxLength(2)]
    public string TipoEvento { get; set; } = string.Empty;

    [Required]
    [MaxLength(500)]
    public string CavidadesConfiguradas { get; set; } = string.Empty;

    [MaxLength(5000)]
    public string? Observaciones { get; set; }

    public List<CalidadHCCMedicionPostViewModel> Mediciones { get; set; } = new();
    public List<CalidadHCCChecklistPostViewModel> Checklist { get; set; } = new();
}

public sealed class CalidadHCCMedicionPostViewModel
{
    public int CaracteristicaHCCID { get; set; }
    public int NumeroTiro { get; set; }
    public int NumeroCavidad { get; set; }
    public string? Valor { get; set; }
    public string? Resultado { get; set; }
    public string? Observaciones { get; set; }
}

public sealed class CalidadHCCChecklistPostViewModel
{
    public int ChecklistHCCID { get; set; }
    public string? Resultado { get; set; }
    public string? Observaciones { get; set; }
}

public sealed class CalidadHCCRegistroDetalleViewModel
{
    public long RegistroHCCID { get; set; }
    public long? RequerimientoHCCID { get; set; }
    public DateTime Fecha { get; set; }
    public TimeSpan? Hora { get; set; }
    public string Turno { get; set; } = string.Empty;
    public string TipoEvento { get; set; } = string.Empty;
    public string OrdenFabricacion { get; set; } = string.Empty;
    public string MaquinaTexto { get; set; } = string.Empty;
    public string OperadorTexto { get; set; } = string.Empty;
    public string AuditorTexto { get; set; } = string.Empty;
    public string CavidadesConfiguradas { get; set; } = string.Empty;
    public int CantidadCavidadesConfiguradas { get; set; }
    public string ResultadoGeneral { get; set; } = string.Empty;
    public string Observaciones { get; set; } = string.Empty;
    public CalidadHCCPlantillaViewModel Plantilla { get; set; } = new();
    public List<CalidadHCCMedicionGuardadaViewModel> Mediciones { get; } = new();
    public List<CalidadHCCChecklistGuardadoViewModel> Checklist { get; } = new();
}

public sealed class CalidadHCCMedicionGuardadaViewModel
{
    public int CaracteristicaHCCID { get; set; }
    public int NumeroTiro { get; set; }
    public int NumeroCavidad { get; set; }
    public decimal? ValorNumerico { get; set; }
    public string ValorTexto { get; set; } = string.Empty;
    public string Resultado { get; set; } = string.Empty;
}

public sealed class CalidadHCCChecklistGuardadoViewModel
{
    public int ChecklistHCCID { get; set; }
    public string Resultado { get; set; } = string.Empty;
    public string Observaciones { get; set; } = string.Empty;
}

public static class CalidadHCCParsing
{
    public static List<int> ParsearCavidades(string? texto)
    {
        var salida = new SortedSet<int>();
        if (string.IsNullOrWhiteSpace(texto)) return salida.ToList();

        foreach (var token in texto.Split(new[] { ',', ';', ' ', '|', '/' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0 && n <= 999)
                salida.Add(n);
        }
        return salida.ToList();
    }

    public static bool TryDecimalFlexible(string? texto, out decimal valor)
    {
        valor = 0;
        if (string.IsNullOrWhiteSpace(texto)) return false;
        var s = texto.Trim().Replace(" ", string.Empty);
        if (decimal.TryParse(s, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out valor)) return true;
        if (decimal.TryParse(s, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.GetCultureInfo("es-MX"), out valor)) return true;
        s = s.Replace(',', '.');
        return decimal.TryParse(s, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out valor);
    }
}
