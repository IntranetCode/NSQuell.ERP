using System.ComponentModel.DataAnnotations;

namespace ERP.NSQuell.Models.ViewModels.Logistica;

public sealed class LogisticaCatalogosVm
{
    public List<LogisticaRutaVm> Rutas { get; set; } = new();

    public List<LogisticaUnidadVm> Unidades { get; set; } = new();

    public int TotalRutas => Rutas.Count;

    public int RutasActivas => Rutas.Count(x => x.Activo);

    public int RutasInactivas => Rutas.Count(x => !x.Activo);

    public int TotalUnidades => Unidades.Count;

    public int UnidadesActivas => Unidades.Count(x => x.Activo);

    public int UnidadesInactivas => Unidades.Count(x => !x.Activo);
}

public sealed class LogisticaRutaVm
{
    public int RutaID { get; set; }

    [Required(ErrorMessage = "El código de la ruta es obligatorio.")]
    [StringLength(
        30,
        ErrorMessage = "El código no puede exceder 30 caracteres.")]
    [Display(Name = "Código")]
    public string Codigo { get; set; } = string.Empty;

    [Required(ErrorMessage = "El nombre de la ruta es obligatorio.")]
    [StringLength(
        150,
        ErrorMessage = "El nombre no puede exceder 150 caracteres.")]
    [Display(Name = "Nombre")]
    public string Nombre { get; set; } = string.Empty;

    [StringLength(
        500,
        ErrorMessage = "La descripción no puede exceder 500 caracteres.")]
    [Display(Name = "Descripción")]
    public string? Descripcion { get; set; }

    public bool Activo { get; set; }

    public string EstatusTexto =>
        Activo
            ? "Activa"
            : "Inactiva";
}

public sealed class LogisticaUnidadVm
{
    public int UnidadID { get; set; }

    [Required(
        ErrorMessage = "El número económico es obligatorio.")]
    [StringLength(
        50,
        ErrorMessage = "El número económico no puede exceder 50 caracteres.")]
    [Display(Name = "Número económico")]
    public string NumeroEconomico { get; set; } = string.Empty;

    [StringLength(
        30,
        ErrorMessage = "Las placas no pueden exceder 30 caracteres.")]
    [Display(Name = "Placas")]
    public string? Placas { get; set; }

    [StringLength(
        80,
        ErrorMessage = "La marca no puede exceder 80 caracteres.")]
    [Display(Name = "Marca")]
    public string? Marca { get; set; }

    [StringLength(
        80,
        ErrorMessage = "El modelo no puede exceder 80 caracteres.")]
    [Display(Name = "Modelo")]
    public string? Modelo { get; set; }

    [Range(
        1,
        int.MaxValue,
        ErrorMessage = "La capacidad debe ser mayor a cero.")]
    [Display(Name = "Capacidad en piezas")]
    public int? CapacidadPiezas { get; set; }

    public bool Activo { get; set; }

    public string EstatusTexto =>
        Activo
            ? "Activa"
            : "Inactiva";

    public string DescripcionUnidad
    {
        get
        {
            var datos = new List<string>();

            if (!string.IsNullOrWhiteSpace(Marca))
                datos.Add(Marca);

            if (!string.IsNullOrWhiteSpace(Modelo))
                datos.Add(Modelo);

            if (!string.IsNullOrWhiteSpace(Placas))
                datos.Add(Placas);

            return datos.Count > 0
                ? string.Join(" · ", datos)
                : NumeroEconomico;
        }
    }
}

