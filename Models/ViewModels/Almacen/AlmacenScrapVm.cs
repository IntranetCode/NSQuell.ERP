using System.ComponentModel.DataAnnotations;

namespace ERP.NSQuell.Models.ViewModels.Almacen;

public sealed class AlmacenScrapIndexVm
{
    public bool Configurado { get; set; } = true;
    public string? MensajeConfiguracion { get; set; }

    public string? Busqueda { get; set; }
    public string? Origen { get; set; }
    public string? Estatus { get; set; }

    public int TotalRegistros { get; set; }
    public int PendientesRecepcion { get; set; }
    public int Recibidos { get; set; }
    public int Molidos { get; set; }
    public decimal KgMolidos { get; set; }

    public List<AlmacenScrapRegistroListaVm> Registros { get; set; } = new();
}


public sealed class AlmacenScrapRecepcionesVm
{
    public bool Configurado { get; set; } = true;
    public string? MensajeConfiguracion { get; set; }
    public string? Busqueda { get; set; }
    public string? Origen { get; set; }

    public int TotalPendientes { get; set; }
    public int PendientesCalidad { get; set; }
    public int PendientesGP12 { get; set; }

    public List<AlmacenScrapRegistroListaVm> Registros { get; set; } = new();
}


public sealed class AlmacenScrapHistorialIndexVm
{
    public bool Configurado { get; set; } = true;
    public string? MensajeConfiguracion { get; set; }
    public string? Busqueda { get; set; }
    public string? Origen { get; set; }
    public string? Evento { get; set; }
    public int TotalEventos { get; set; }

    public List<AlmacenScrapHistorialListaVm> Eventos { get; set; } = new();
}

public sealed class AlmacenScrapHistorialListaVm
{
    public long ScrapHistorialID { get; set; }
    public long ScrapRegistroID { get; set; }
    public DateTime FechaEvento { get; set; }
    public string Evento { get; set; } = string.Empty;
    public string EstatusAnterior { get; set; } = string.Empty;
    public string EstatusNuevo { get; set; } = string.Empty;
    public string Detalle { get; set; } = string.Empty;
    public string UsuarioNombre { get; set; } = string.Empty;
    public string Origen { get; set; } = string.Empty;
    public string NumeroOF { get; set; } = string.Empty;
    public string NumeroParte { get; set; } = string.Empty;
    public string Lote { get; set; } = string.Empty;
    public string CodigoBarras { get; set; } = string.Empty;

    public string OrigenTexto =>
        Origen.ToUpperInvariant() switch
        {
            "CALIDAD" => "Calidad",
            "GP12" => "GP12",
            "ENTRADA_SCRAP" => "Entrada Scrap",
            _ => Origen
        };

    public string EventoTexto =>
        Evento.ToUpperInvariant() switch
        {
            "RECEPCION_ESCANER" => "Recepción por escáner",
            "ENVIADO_A_ALMACEN" => "Enviado a Almacén",
            "RECEPCION_CONFIRMADA" => "Recepción confirmada",
            "MP_MOLIDO_GENERADO" => "MP Molido generado",
            _ => Evento
        };
}

public sealed class AlmacenScrapRegistroListaVm
{
    public long ScrapRegistroID { get; set; }
    public string Origen { get; set; } = string.Empty;
    public string OrigenReferencia { get; set; } = string.Empty;
    public string CodigoBarras { get; set; } = string.Empty;
    public string NumeroOF { get; set; } = string.Empty;
    public int? SolicitudProduccionID { get; set; }
    public int? ParteID { get; set; }
    public string NumeroParte { get; set; } = string.Empty;
    public string Designacion { get; set; } = string.Empty;
    public int CantidadPiezas { get; set; }
    public string Lote { get; set; } = string.Empty;
    public string Estatus { get; set; } = string.Empty;
    public DateTime FechaCreacion { get; set; }
    public DateTime? FechaRecepcion { get; set; }
    public string RecibidoPorNombre { get; set; } = string.Empty;
    public int? MaterialIDMolido { get; set; }
    public string MaterialMolido { get; set; } = string.Empty;
    public decimal? PesoMolidoKg { get; set; }
    public long? MPMovimientoID { get; set; }
    public DateTime? FechaMolido { get; set; }
    public string Observaciones { get; set; } = string.Empty;

    public bool PuedeConfirmarRecepcion =>
        Estatus.Equals("PENDIENTE_RECEPCION", StringComparison.OrdinalIgnoreCase);

    public bool PuedeRealizarMolido =>
        Estatus.Equals("RECIBIDO", StringComparison.OrdinalIgnoreCase)
        && !MPMovimientoID.HasValue;

    public string OrigenTexto =>
        Origen.ToUpperInvariant() switch
        {
            "CALIDAD" => "Calidad",
            "GP12" => "GP12",
            "ENTRADA_SCRAP" => "Entrada Scrap",
            _ => Origen
        };

    public string EstatusTexto =>
        Estatus.ToUpperInvariant() switch
        {
            "PENDIENTE_RECEPCION" => "Pendiente de recepción",
            "RECIBIDO" => "Recibido",
            "MOLIDO" => "MP Molido generado",
            _ => Estatus
        };
}

public sealed class AlmacenScrapEntradaVm
{
    [Required(ErrorMessage = "Escanea al menos un código de Scrap.")]
    [StringLength(50000)]
    [Display(Name = "Códigos escaneados")]
    public string CodigosEscaneados { get; set; } = string.Empty;

    [StringLength(800)]
    public string? Observaciones { get; set; }

    public List<AlmacenScrapCodigoPreviewVm> Codigos { get; set; } = new();

    public int TotalCodigos => Codigos.Count;
    public int TotalValidos => Codigos.Count(x => x.PuedeRegistrar);
    public int TotalConAdvertencia =>
        Codigos.Count(x => x.PuedeRegistrar && !string.IsNullOrWhiteSpace(x.Advertencia));
}

public sealed class AlmacenScrapCodigoPreviewVm
{
    public string CodigoOriginal { get; set; } = string.Empty;
    public string NumeroOF { get; set; } = string.Empty;
    public string NumeroParte { get; set; } = string.Empty;
    public string Designacion { get; set; } = string.Empty;
    public int CantidadPiezas { get; set; }
    public string Lote { get; set; } = string.Empty;

    public int? ParteID { get; set; }
    public string ParteDescripcion { get; set; } = string.Empty;

    public int? MaterialID { get; set; }
    public string MaterialCodigo { get; set; } = string.Empty;
    public string MaterialNombre { get; set; } = string.Empty;

    public bool PuedeRegistrar { get; set; }
    public string Error { get; set; } = string.Empty;
    public string Advertencia { get; set; } = string.Empty;

    public string MaterialTexto =>
        MaterialID.HasValue
            ? string.Join(
                " · ",
                new[] { MaterialCodigo, MaterialNombre }
                    .Where(x => !string.IsNullOrWhiteSpace(x)))
            : "Sin material MP único";
}

public sealed class AlmacenScrapMolidoVm
{
    [Required]
    public long ScrapRegistroID { get; set; }

    public string Origen { get; set; } = string.Empty;
    public string CodigoBarras { get; set; } = string.Empty;
    public string NumeroOF { get; set; } = string.Empty;
    public string NumeroParte { get; set; } = string.Empty;
    public string Designacion { get; set; } = string.Empty;
    public int CantidadPiezas { get; set; }
    public string Lote { get; set; } = string.Empty;
    public string Estatus { get; set; } = string.Empty;

    public int? ParteID { get; set; }
    public int? MaterialID { get; set; }
    public string MaterialCodigo { get; set; } = string.Empty;
    public string MaterialNombre { get; set; } = string.Empty;
    public string MaterialError { get; set; } = string.Empty;

    [Required(ErrorMessage = "Captura el peso real del MP molido.")]
    [Range(typeof(decimal), "0.001", "999999999", ErrorMessage = "El peso debe ser mayor que 0 KG.")]
    [Display(Name = "Peso de MP Molido")]
    public decimal PesoMolidoKg { get; set; }

    [Required(ErrorMessage = "Selecciona la ubicación de ingreso del MP molido.")]
    [Display(Name = "Ubicación MP")]
    public int? UbicacionID { get; set; }

    [StringLength(800)]
    public string? ObservacionesMolido { get; set; }

    public List<AlmacenSelectVm> Ubicaciones { get; set; } = new();

    public bool PuedeGuardar =>
        Estatus.Equals("RECIBIDO", StringComparison.OrdinalIgnoreCase)
        && ParteID.HasValue
        && MaterialID.HasValue
        && string.IsNullOrWhiteSpace(MaterialError);
}

public sealed class AlmacenScrapDetalleVm
{
    public AlmacenScrapRegistroListaVm Registro { get; set; } = new();
    public List<AlmacenScrapHistorialVm> Historial { get; set; } = new();
}

public sealed class AlmacenScrapHistorialVm
{
    public long ScrapHistorialID { get; set; }
    public DateTime FechaEvento { get; set; }
    public string Evento { get; set; } = string.Empty;
    public string EstatusAnterior { get; set; } = string.Empty;
    public string EstatusNuevo { get; set; } = string.Empty;
    public string Detalle { get; set; } = string.Empty;
    public string UsuarioNombre { get; set; } = string.Empty;
}
