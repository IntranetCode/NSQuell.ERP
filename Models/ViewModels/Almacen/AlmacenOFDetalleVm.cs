namespace ERP.NSQuell.Models.ViewModels.Almacen;

public sealed class AlmacenOFDetalleVm
{
    public int SolicitudProduccionID { get; set; }
    public string FolioSolicitud { get; set; } = string.Empty;
    public string NumeroOFRecibida { get; set; } = string.Empty;
    public string NumeroOFClave { get; set; } = string.Empty;
    public string Cliente { get; set; } = string.Empty;
    public string Maquina { get; set; } = string.Empty;
    public string Prioridad { get; set; } = string.Empty;
    public int EstatusID { get; set; }
    public string EstatusNombre { get; set; } = string.Empty;
    public string ResponsablePlaneacion { get; set; } = string.Empty;
    public DateTime FechaSolicitud { get; set; }
    public DateTime? FechaRequerida { get; set; }
    public DateTime? FechaInicioPlaneada { get; set; }
    public DateTime? FechaFinPlaneada { get; set; }
    public List<AlmacenOFDetalleRenglonVm> Renglones { get; set; } = new();
    public List<AlmacenOFEntregaHistorialVm> Entregas { get; set; } = new();

    public decimal MpRequerida => Renglones.Sum(x => x.MpRequerida);
    public decimal MpEntregada => Renglones.Sum(x => x.MpEntregada);
    public decimal MpPendiente => Math.Max(0m, MpRequerida - MpEntregada);
    public decimal EmbalajeRequerido => Renglones.Sum(x => x.EmbalajeRequerido);
    public decimal EmbalajeEntregado => Renglones.Sum(x => x.EmbalajeEntregado);
    public decimal EmbalajePendiente => Math.Max(0m, EmbalajeRequerido - EmbalajeEntregado);
}

public sealed class AlmacenOFDetalleRenglonVm
{
    public int Renglon { get; set; }
    public string NumeroParte { get; set; } = string.Empty;
    public string DescripcionParte { get; set; } = string.Empty;
    public decimal CantidadPiezas { get; set; }
    public int MaterialID { get; set; }
    public string MaterialCodigo { get; set; } = string.Empty;
    public string MaterialDescripcion { get; set; } = string.Empty;
    public decimal MpRequerida { get; set; }
    public decimal MpEntregada { get; set; }
    public decimal MpPendiente => Math.Max(0m, MpRequerida - MpEntregada);
    public int EmbalajeID { get; set; }
    public string EmbalajeCodigo { get; set; } = string.Empty;
    public string EmbalajeDescripcion { get; set; } = string.Empty;
    public decimal EmbalajeRequerido { get; set; }
    public decimal EmbalajeEntregado { get; set; }
    public decimal EmbalajePendiente => Math.Max(0m, EmbalajeRequerido - EmbalajeEntregado);
}

public sealed class AlmacenOFEntregaHistorialVm
{
    public string Area { get; set; } = string.Empty;
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string TipoMovimiento { get; set; } = string.Empty;
    public decimal Cantidad { get; set; }
    public string Unidad { get; set; } = string.Empty;
    public DateTime Fecha { get; set; }
    public string Responsable { get; set; } = string.Empty;
    public string Recibio { get; set; } = string.Empty;
    public string Observaciones { get; set; } = string.Empty;
}
