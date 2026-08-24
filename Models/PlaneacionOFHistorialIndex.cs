namespace ERP.NSQuell.Models;

public sealed class PlaneacionOFHistorialIndexVm
{
    public string? Busqueda { get; set; }
    public DateTime? FechaDesde { get; set; }
    public DateTime? FechaHasta { get; set; }

    public List<PlaneacionOFHistorialItemVm> Items { get; set; } = new();
}

public sealed class PlaneacionOFHistorialItemVm
{
    public int SolicitudProduccionID { get; set; }
    public string FolioSolicitud { get; set; } = string.Empty;
    public string? NumeroOFRecibida { get; set; }

    public string Cliente { get; set; } = string.Empty;
    public string TipoOF { get; set; } = "RELEASE";

    public int EstatusID { get; set; }
    public string EstatusNombre { get; set; } = string.Empty;

    public DateTime FechaSolicitud { get; set; }
    public DateTime? FechaRequerida { get; set; }
    public DateTime? FechaInicioPlaneada { get; set; }
    public DateTime? FechaFinPlaneada { get; set; }

    public DateTime? FechaInicioReal { get; set; }
    public DateTime? FechaFinReal { get; set; }
    public DateTime FechaReferencia { get; set; }

    public int TotalRenglones { get; set; }
    public int CantidadPlaneada { get; set; }
    public int CantidadOK { get; set; }
    public int CantidadSospechosa { get; set; }
    public int CantidadScrap { get; set; }
    public int TotalEjecuciones { get; set; }

    public string Partes { get; set; } = string.Empty;
    public string Maquinas { get; set; } = string.Empty;
    public string Personal { get; set; } = string.Empty;

    public decimal RendimientoOK
    {
        get
        {
            var total = CantidadOK + CantidadSospechosa + CantidadScrap;
            return total <= 0
                ? 0m
                : Math.Round(CantidadOK * 100m / total, 2);
        }
    }
}
