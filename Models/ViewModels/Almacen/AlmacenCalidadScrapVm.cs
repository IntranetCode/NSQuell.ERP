using System.ComponentModel.DataAnnotations;

namespace ERP.NSQuell.Models.ViewModels.Almacen;

public sealed class AlmacenCalidadScrapBandejaVm
{
    public bool Configurado { get; set; } = true;
    public string? MensajeConfiguracion { get; set; }
    public string? Busqueda { get; set; }
    public string? Estado { get; set; }

    public int Total { get; set; }
    public int PendientesRecepcion { get; set; }
    public int RecibidosAlmacen { get; set; }
    public int Molidos { get; set; }
    public int PiezasPendientes { get; set; }
    public decimal KgMolidos { get; set; }

    public List<AlmacenCalidadScrapItemVm> Registros { get; set; } = new();
    public List<AlmacenSelectVm> UbicacionesScrap { get; set; } = new();
    public List<AlmacenSelectVm> UbicacionesMP { get; set; } = new();
}

public sealed class AlmacenCalidadScrapItemVm
{
    public long ScrapEntregaID { get; set; }
    public int InspeccionID { get; set; }
    public int? DisposicionID { get; set; }
    public int? EjecucionProduccionID { get; set; }
    public int? SolicitudProduccionID { get; set; }
    public int? SolicitudProduccionDetalleID { get; set; }
    public int? ParteID { get; set; }
    public string NumeroParte { get; set; } = string.Empty;
    public string OrdenFabricacion { get; set; } = string.Empty;
    public int CantidadScrap { get; set; }
    public string Estado { get; set; } = string.Empty;
    public string Origen { get; set; } = string.Empty;
    public int? GP12SolicitudID { get; set; }
    public int? GP12InspeccionID { get; set; }
    public DateTime FechaCreacion { get; set; }
    public DateTime? FechaEntrega { get; set; }
    public string EntregadoPor { get; set; } = string.Empty;
    public DateTime? FechaRecepcion { get; set; }
    public string RecibidoPor { get; set; } = string.Empty;
    public string UbicacionScrap { get; set; } = string.Empty;
    public DateTime? FechaMolienda { get; set; }
    public string MolidoPor { get; set; } = string.Empty;
    public decimal? CantidadMolida { get; set; }
    public string Observaciones { get; set; } = string.Empty;

    public bool PuedeRecibir =>
        Estado.Equals("PENDIENTE_RECEPCION", StringComparison.OrdinalIgnoreCase);

    public bool PuedeMoler =>
        Estado.Equals("RECIBIDO_ALMACEN", StringComparison.OrdinalIgnoreCase)
        || Estado.Equals("PENDIENTE_MOLIENDA", StringComparison.OrdinalIgnoreCase);

    public string OrigenTexto =>
        Origen.ToUpperInvariant() switch
        {
            "GP12" => "GP12",
            "CALIDAD" => "Calidad",
            _ => string.IsNullOrWhiteSpace(Origen) ? "Calidad" : Origen
        };

    public string EstadoTexto =>
        Estado.ToUpperInvariant() switch
        {
            "PENDIENTE_RECEPCION" => "Pendiente de recepción",
            "RECIBIDO_ALMACEN" => "Recibido en Almacén",
            "PENDIENTE_MOLIENDA" => "Pendiente de molienda",
            "MOLIDO" => "Molido / ingresado a MP",
            "CANCELADO" => "Cancelado",
            _ => Estado
        };
}

public sealed class AlmacenRecibirScrapPostVm
{
    [Required]
    public long ScrapEntregaID { get; set; }

    [Required(ErrorMessage = "Selecciona una ubicación activa de SCRAP.")]
    public int? UbicacionScrapID { get; set; }
}

public sealed class AlmacenMoliendaScrapPostVm
{
    [Required]
    public long ScrapEntregaID { get; set; }

    [Required(ErrorMessage = "Captura el peso real obtenido después de la molienda.")]
    [Range(typeof(decimal), "0.001", "999999999", ErrorMessage = "La cantidad molida debe ser mayor que 0 KG.")]
    public decimal CantidadMolida { get; set; }

    [Required(ErrorMessage = "Selecciona una ubicación activa de MP.")]
    public int? UbicacionMPID { get; set; }

    [StringLength(500)]
    public string? Observaciones { get; set; }
}
