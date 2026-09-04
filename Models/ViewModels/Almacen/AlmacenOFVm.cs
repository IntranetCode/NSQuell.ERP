namespace ERP.NSQuell.Models.ViewModels.Almacen;

public sealed class AlmacenOFIndexVm
{
    public bool Configurado { get; set; } = true;
    public string? MensajeConfiguracion { get; set; }

    public string? Busqueda { get; set; }
    public int? EstatusID { get; set; }
    public string? Area { get; set; }
    public DateTime? Desde { get; set; }
    public DateTime? Hasta { get; set; }

    public int Pagina { get; set; } = 1;
    public int TamanoPagina { get; set; } = 50;
    public int TotalRegistros { get; set; }

    public long TotalPiezas { get; set; }
    public int PendientesMP { get; set; }
    public int PendientesEmbalaje { get; set; }
    public int OFConMovimientos { get; set; }

    public int TotalPaginas =>
        Math.Max(1, (int)Math.Ceiling(TotalRegistros / (double)TamanoPagina));

    public List<AlmacenOFItemVm> Ordenes { get; set; } = new();
    public List<AlmacenOFEstatusFiltroVm> EstatusDisponibles { get; set; } = new();
}

public sealed class AlmacenOFEstatusFiltroVm
{
    public int EstatusID { get; set; }
    public string Nombre { get; set; } = string.Empty;
}

public sealed class AlmacenOFItemVm
{
    public int SolicitudProduccionID { get; set; }
    public string FolioSolicitud { get; set; } = string.Empty;
    public string NumeroOFRecibida { get; set; } = string.Empty;
    public string NumeroOFClave { get; set; } = string.Empty;

    public DateTime FechaSolicitud { get; set; }
    public DateTime? FechaRequerida { get; set; }
    public DateTime? FechaInicioPlaneada { get; set; }
    public DateTime? FechaFinPlaneada { get; set; }

    // NSQ_ALMACEN_OF_HISTORICO_LISTA_V1_8_2
    public DateTime? UltimaActualizacionAlmacen { get; set; }

    // ALMACEN_OF_MAQUINA_ITEM_V4_1
    public string Maquina { get; set; } = string.Empty;
    public string Cliente { get; set; } = string.Empty;
    public string Prioridad { get; set; } = "Normal";
    public int EstatusID { get; set; }
    public string EstatusNombre { get; set; } = string.Empty;
    public string ResponsablePlaneacionNombre { get; set; } = string.Empty;

    public int TotalRenglones { get; set; }
    public int TotalPiezas { get; set; }

    public string MaterialResumen { get; set; } = string.Empty;
    public string EmbalajeResumen { get; set; } = string.Empty;

    public decimal MpRequerida { get; set; }
    public decimal MpEntregada { get; set; }

    public decimal EmbalajeRequerido { get; set; }
    public decimal EmbalajeEntregado { get; set; }

    public long MovimientosMP { get; set; }
    public long MovimientosEmbalaje { get; set; }

    public bool TieneNumeroOF =>
        !string.IsNullOrWhiteSpace(NumeroOFRecibida)
        || !string.IsNullOrWhiteSpace(FolioSolicitud);

    public List<AlmacenOFEntregableVm> MaterialesEntrega { get; set; } = new();
    public List<AlmacenOFEntregableVm> EmbalajesEntrega { get; set; } = new();
    public List<AlmacenOFEntregableVm> PartesEntrega { get; set; } = new();

    public int PorcentajeMP => Porcentaje(MpRequerida, MpEntregada);
    public int PorcentajeEmbalaje => Porcentaje(EmbalajeRequerido, EmbalajeEntregado);

    public string EstadoMP => Estado(MpRequerida, MpEntregada);
    public string EstadoEmbalaje => Estado(EmbalajeRequerido, EmbalajeEntregado);

    public string ClaseMP => ClaseEstado(MpRequerida, MpEntregada);
    public string ClaseEmbalaje => ClaseEstado(EmbalajeRequerido, EmbalajeEntregado);

    public bool TieneActividad =>
        MovimientosMP + MovimientosEmbalaje > 0;

    // ALMACEN_OF_ESTADO_ENTREGA_V4_2
    public bool TieneRequerimientosAlmacen =>
        MaterialesEntrega.Any(x => x.Requerido > 0.0005m)
        || EmbalajesEntrega.Any(x => x.Requerido > 0.0005m)
        || PartesEntrega.Any(x => x.Requerido > 0.0005m);

    public bool TienePendientesAlmacen =>
        MaterialesEntrega.Any(x => x.Pendiente > 0.0005m)
        || EmbalajesEntrega.Any(x => x.Pendiente > 0.0005m)
        || PartesEntrega.Any(x => x.Pendiente > 0.0005m);

    public bool EntregaCompletaAlmacen =>
        TieneRequerimientosAlmacen
        && !TienePendientesAlmacen;
    // NSQ_DEVOLUCION_MATERIALES_V1_2
    public bool TieneDevolucionPendiente =>
        MaterialesEntrega.Any(x => x.TieneDevolucionPendiente)
        || EmbalajesEntrega.Any(x => x.TieneDevolucionPendiente);

    public int DevolucionesPendientes =>
        MaterialesEntrega.Count(x => x.TieneDevolucionPendiente)
        + EmbalajesEntrega.Count(x => x.TieneDevolucionPendiente);

    public DateTime? UltimaDevolucionFecha =>
        MaterialesEntrega
            .Concat(EmbalajesEntrega)
            .Where(x => x.TieneDevolucionPendiente)
            .Select(x => x.FechaDevolucion)
            .Where(x => x.HasValue)
            .OrderByDescending(x => x)
            .FirstOrDefault();
    public string PrioridadClase =>
        Prioridad.Trim().ToUpperInvariant() switch
        {
            "URGENTE" => "urgente",
            "ALTA" => "alta",
            "BAJA" => "baja",
            _ => "normal"
        };

    private static int Porcentaje(decimal requerido, decimal atendido)
    {
        if (requerido <= 0)
            return 0;

        var porcentaje = atendido / requerido * 100m;
        return Convert.ToInt32(Math.Clamp(Math.Round(porcentaje, 0), 0m, 100m));
    }


    private static string Estado(decimal requerido, decimal atendido)
    {
        if (requerido <= 0)
            return "No aplica";

        if (atendido <= 0)
            return "Pendiente";

        if (atendido + 0.0005m < requerido)
            return "Parcial";

        return "Completo";
    }

    private static string ClaseEstado(decimal requerido, decimal atendido)
    {
        if (requerido <= 0)
            return "na";

        if (atendido <= 0)
            return "pendiente";

        if (atendido + 0.0005m < requerido)
            return "parcial";

        return "completo";
    }
}

public sealed class AlmacenOFEntregableVm
{
    public int SolicitudProduccionID { get; set; }
    public int CatalogoID { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string Unidad { get; set; } = string.Empty;
    public decimal Requerido { get; set; }

    // Para MP/Embalaje, Entregado representa lo ACEPTADO por Produccion.
    // EntregadoFisico conserva el neto de movimientos de Almacen.
    public decimal Entregado { get; set; }
    public decimal EntregadoFisico { get; set; }
    public decimal EnValidacionProduccion { get; set; }

    public long? DevolucionMaterialID { get; set; }
    public decimal? CantidadDevuelta { get; set; }
    public string? MotivoDevolucion { get; set; }
    // NSQ_DEVOLUCION_MATERIALES_V1_3
    public string? ComentarioDevolucion { get; set; }
    // NSQ_DEVOLUCION_MATERIALES_V1_4
    public string? UsuarioDevolucionNombre { get; set; }
    public DateTime? FechaDevolucion { get; set; }

    public bool TieneDevolucionPendiente =>
        DevolucionMaterialID.HasValue
        && DevolucionMaterialID.Value > 0;

    // ALMACEN_OF_PT_DISPONIBLE_V4_1
    public decimal DisponibleInventario { get; set; }
    public bool RequiereInventarioDisponible { get; set; }

    public decimal Pendiente => Math.Max(0m, Requerido - Entregado);


    public int Porcentaje
    {
        get
        {
            if (Requerido <= 0) return 0;
            var valor = Entregado / Requerido * 100m;
            return Convert.ToInt32(Math.Clamp(Math.Round(valor, 0), 0m, 100m));
        }
    }

    public string Estado
    {
        get
        {
            if (TieneDevolucionPendiente)
                return "Devuelto - reentregar";

            if (EnValidacionProduccion > 0.0005m)
                return "Esperando aceptación de Producción";

            if (Requerido <= 0) return "No aplica";
            if (Entregado <= 0) return "Pendiente";
            if (Entregado + 0.0005m < Requerido) return "Parcial";
            return "Completo";
        }
    }
    public string Clase
    {
        get
        {
            if (TieneDevolucionPendiente)
                return "devuelto";

            if (EnValidacionProduccion > 0.0005m)
                return "validacion";

            if (Requerido <= 0) return "na";
            if (Entregado <= 0) return "pendiente";
            if (Entregado + 0.0005m < Requerido) return "parcial";
            return "completo";
        }
    }


    public bool PuedeEntregar =>
        CatalogoID > 0
        && Pendiente > 0
        && EnValidacionProduccion <= 0.0005m
        && (!RequiereInventarioDisponible || DisponibleInventario > 0);
}
