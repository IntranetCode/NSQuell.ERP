using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ERP.NSQuell.Models.ViewModels
{
    public sealed class CalidadHCCPlantillaItemViewModel
    {
        public int PlantillaHCCID { get; set; }
        public int ParteID { get; set; }
        public string NumeroParte { get; set; } = "";
        public string? Designacion { get; set; }
        public string? Cliente { get; set; }
        public string? NumeroHCC { get; set; }
        public string? VersionFormato { get; set; }
        public DateTime? FechaModificacionFormato { get; set; }
        public int Caracteristicas { get; set; }
        public int Checklist { get; set; }
        public bool EsVigente { get; set; }
        public bool ParteActiva { get; set; }
        public bool EsRelacionPrincipal { get; set; }
        public string? MetodoMapeo { get; set; }
        public decimal? ConfianzaMapeo { get; set; }
        public string? MateriaPrima { get; set; }
        public string? TiempoSecadoTexto { get; set; }
    }

    public sealed class CalidadHCCIndexViewModel
    {
        public string? Busqueda { get; set; }
        public List<CalidadHCCPlantillaItemViewModel> Plantillas { get; set; } = new();
    }

    public sealed class CalidadHCCCaracteristicaViewModel
    {
        public int CaracteristicaHCCID { get; set; }
        public int Orden { get; set; }
        public string TipoCaracteristica { get; set; } = "";
        public string Nombre { get; set; } = "";
        public decimal? ValorNominal { get; set; }
        public decimal? ToleranciaMas { get; set; }
        public decimal? ToleranciaMenos { get; set; }
        public decimal? LimiteInferior { get; set; }
        public decimal? LimiteSuperior { get; set; }
        public string? Unidad { get; set; }
        public string? Instrumento { get; set; }
        public string? CodigoGauge { get; set; }
        public List<int> Cavidades { get; set; } = new();
    }

    public sealed class CalidadHCCChecklistViewModel
    {
        public int ChecklistHCCID { get; set; }
        public int Orden { get; set; }
        public string Descripcion { get; set; } = "";
        public bool PermiteNA { get; set; }
    }

    public sealed class CalidadHCCPlantillaDetalleViewModel
    {
        public int PlantillaHCCID { get; set; }
        public int ParteID { get; set; }
        public string NumeroParte { get; set; } = "";
        public bool ParteActiva { get; set; }
        public bool EsRelacionPrincipal { get; set; }
        public string? MetodoMapeo { get; set; }
        public decimal? ConfianzaMapeo { get; set; }
        public string? Designacion { get; set; }
        public string? Cliente { get; set; }
        public string? NumeroHCC { get; set; }
        public string? VersionFormato { get; set; }
        public DateTime? FechaRevision { get; set; }
        public string? NumeroDibujo { get; set; }
        public string? Proceso { get; set; }
        public string? PlanControl { get; set; }
        public string? CodigoResina { get; set; }
        public string? MateriaPrima { get; set; }
        public string? TiempoSecado { get; set; }
        public int NumeroTiros { get; set; } = 3;
        public int? Cavidades { get; set; }
        public string? ArchivoOrigen { get; set; }
        public string? HojaOrigen { get; set; }
        public List<CalidadHCCCaracteristicaViewModel> Caracteristicas { get; set; } = new();
        public List<CalidadHCCChecklistViewModel> Checklist { get; set; } = new();
    }

    public sealed class CalidadHCCCapturaViewModel
    {
        [Range(1, int.MaxValue)]
        public int PlantillaHCCID { get; set; }

        [Range(1, int.MaxValue)]
        public int ParteID { get; set; }

        public int? InspeccionID { get; set; }
        public string? OrdenFabricacion { get; set; }

        [Required]
        public DateTime Fecha { get; set; } = DateTime.Today;

        public string? Turno { get; set; }
        public TimeSpan? Hora { get; set; }
        public int? MaquinaID { get; set; }
        public string? MaquinaTexto { get; set; }

        [Required]
        public string TipoEvento { get; set; } = "M";

        public string? OperadorTexto { get; set; }
        public string? AuditorTexto { get; set; }
        public string? Observaciones { get; set; }
        public List<CalidadHCCMedicionPostViewModel> Mediciones { get; set; } = new();
        public List<CalidadHCCChecklistPostViewModel> Checklist { get; set; } = new();
        public CalidadHCCPlantillaDetalleViewModel? Plantilla { get; set; }
    }

    public sealed class CalidadHCCMedicionPostViewModel
    {
        public int CaracteristicaHCCID { get; set; }
        public int NumeroTiro { get; set; }
        public int NumeroCavidad { get; set; }
        public decimal? ValorNumerico { get; set; }
        public string? ValorTexto { get; set; }
        public string Resultado { get; set; } = "OK";
        public string? Observaciones { get; set; }
    }

    public sealed class CalidadHCCChecklistPostViewModel
    {
        public int ChecklistHCCID { get; set; }
        public string Resultado { get; set; } = "OK";
        public string? Observaciones { get; set; }
    }

    public sealed class CalidadHCCRegistroItemViewModel
    {
        public long RegistroHCCID { get; set; }
        public DateTime Fecha { get; set; }
        public string? Turno { get; set; }
        public string? OrdenFabricacion { get; set; }
        public string TipoEvento { get; set; } = "";
        public string NumeroParte { get; set; } = "";
        public string? VersionFormato { get; set; }
        public string? Auditor { get; set; }
        public string Estado { get; set; } = "";
    }
}
