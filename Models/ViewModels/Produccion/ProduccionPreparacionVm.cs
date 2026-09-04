using ERP.NSQuell.Models;

public static class ProduccionRecepcionMaterialTipo
{
    public const string MP = "MP";
    public const string Embalaje = "EMBALAJE";
}

public static class ProduccionRecepcionMaterialEstado
{
    public const string Pendiente = "PENDIENTE";
    public const string RecibidoCompleto = "RECIBIDO_COMPLETO";
    public const string RecibidoParcial = "RECIBIDO_PARCIAL";
    public const string NoRecibido = "NO_RECIBIDO";
}

public static class ProduccionRecepcionMaterialEstadoAclaracion
{
    public const string NoAplica = "NO_APLICA";
    public const string Pendiente = "PENDIENTE";
    public const string Resuelta = "RESUELTA";
}

public static class ProduccionRecepcionMaterialDecision
{
    public const string Completo = "COMPLETO";
    public const string Parcial = "PARCIAL";
    public const string NoRecibido = "NO_RECIBIDO";
}

public sealed class ProduccionPreparacionMaterialesVm
{
    public DateTime FechaConsulta { get; set; } = DateTime.Now;
    public string? Filtro { get; set; }
    public int? MaquinaID { get; set; }
    public bool PuedeGestionarMateriales { get; set; }
    public List<ProduccionPreparacionMaquinaVm> Maquinas { get; set; } = new();
    public List<ProduccionRecepcionMaterialVm> Recepciones { get; set; } = new();
    public List<ProduccionMaterialEsperadoVm> MaterialesEsperados { get; set; } = new();

    public int PendientesAlmacen =>
    MaterialesEsperados.Count(x => x.CantidadPendienteAlmacen > 0.0005m);

    public int EntregasParcialesAlmacen =>
        MaterialesEsperados.Count(x => x.EsEntregaParcial);

    public int PendientesConfirmacion =>
        Recepciones.Count(x => x.EstaPendiente);
    public int Pendientes =>
        Recepciones.Count(x => x.EstaPendiente);

    public int RecibidosCompletos =>
        Recepciones.Count(x => x.EstaRecibidoCompleto);

    public int ConDiferencia =>
        Recepciones.Count(x => x.TieneDiferencia);

    public int AclaracionesPendientes =>
        Recepciones.Count(x => x.AclaracionPendiente);

    public int RecibidosHoy =>
        Recepciones.Count(x =>
            x.FechaRecepcion.HasValue &&
            x.FechaRecepcion.Value.Date == FechaConsulta.Date);

    public IEnumerable<ProduccionRecepcionMaterialVm> PendientesRecepcion =>
        Recepciones
            .Where(x => x.EstaPendiente)
            .OrderBy(x => x.FechaEntregaAlmacen)
            .ThenBy(x => x.NumeroOF);

    public IEnumerable<ProduccionRecepcionMaterialVm> RecepcionesConDiferencia =>
        Recepciones
            .Where(x => x.TieneDiferencia)
            .OrderByDescending(x => x.FechaRecepcion);
}

public sealed class ProduccionMaterialEsperadoVm
{
    public string TipoOrigen { get; set; } = string.Empty;
    public int SolicitudProduccionID { get; set; }
    public int? SolicitudProduccionDetalleID { get; set; }
    public int? ProgramaProduccionID { get; set; }
    public string NumeroOF { get; set; } = string.Empty;

    public int? MaquinaID { get; set; }
    public string? MaquinaCodigo { get; set; }
    public string? MaquinaNombre { get; set; }

    public int? ParteID { get; set; }
    public string? NumeroParte { get; set; }
    public string? DescripcionParte { get; set; }

    public int CatalogoID { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public string Unidad { get; set; } = string.Empty;

    public decimal CantidadRequerida { get; set; }
    public decimal CantidadEntregadaAlmacen { get; set; }
    public decimal CantidadConfirmadaProduccion { get; set; }

    public int? GrupoLhRh { get; set; }
    public string? LadoLhRh { get; set; }
    public int? ProgramaParejaID { get; set; }
    public int? EjecucionParejaID { get; set; }
    public string? NumeroOFPareja { get; set; }
    public int? ParteParejaID { get; set; }
    public string? NumeroPartePareja { get; set; }
    public string? ReferenciaSAPPareja { get; set; }
    public string? DescripcionPartePareja { get; set; }
    public bool EsParejaLhRh => GrupoLhRh.HasValue && ProgramaParejaID.HasValue && ProgramaParejaID.Value > 0;

    public DateTime? FechaArranque { get; set; }

    public bool EsMateriaPrima =>
        string.Equals(
            TipoOrigen,
            ProduccionRecepcionMaterialTipo.MP,
            StringComparison.OrdinalIgnoreCase);

    public bool EsEmbalaje =>
        string.Equals(
            TipoOrigen,
            ProduccionRecepcionMaterialTipo.Embalaje,
            StringComparison.OrdinalIgnoreCase);

    public string TipoOrigenTexto =>
        EsMateriaPrima
            ? "Materia prima"
            : EsEmbalaje
                ? "Embalaje"
                : TipoOrigen;

    public decimal CantidadPendienteAlmacen =>
        Math.Max(0m, CantidadRequerida - CantidadEntregadaAlmacen);

    public bool SinEntrega =>
        CantidadEntregadaAlmacen <= 0.0005m &&
        CantidadPendienteAlmacen > 0.0005m;

    public bool EsEntregaParcial =>
        CantidadEntregadaAlmacen > 0.0005m &&
        CantidadPendienteAlmacen > 0.0005m;

    public bool EntregaAlmacenCompleta =>
        CantidadRequerida > 0.0005m &&
        CantidadPendienteAlmacen <= 0.0005m;

    public string EstadoAlmacen
    {
        get
        {
            if (EntregaAlmacenCompleta)
                return "COMPLETO";
            if (EsEntregaParcial)
                return "PARCIAL";
            return "PENDIENTE";
        }
    }

    public string EstadoAlmacenTexto
    {
        get
        {
            if (EntregaAlmacenCompleta)
                return "Entregado por Almacén";
            if (EsEntregaParcial)
                return "Entrega parcial";
            return "Pendiente de Almacén";
        }
    }

    public string TextoMaterial
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Codigo) &&
                !string.IsNullOrWhiteSpace(Descripcion))
                return $"{Codigo} - {Descripcion}";

            if (!string.IsNullOrWhiteSpace(Codigo))
                return Codigo;

            if (!string.IsNullOrWhiteSpace(Descripcion))
                return Descripcion;

            return "Sin información";
        }
    }

    public string TextoMaquina
    {
        get
        {
            if (string.IsNullOrWhiteSpace(MaquinaCodigo))
                return "Sin máquina";

            if (string.IsNullOrWhiteSpace(MaquinaNombre))
                return MaquinaCodigo;

            return $"{MaquinaCodigo} - {MaquinaNombre}";
        }
    }
}

public sealed class ProduccionRecepcionMaterialVm
{
    public long RecepcionMaterialID { get; set; }
    public string TipoOrigen { get; set; } = string.Empty;
    public long MovimientoAlmacenID { get; set; }

    public int SolicitudProduccionID { get; set; }
    public int? SolicitudProduccionDetalleID { get; set; }
    public int? ProgramaProduccionID { get; set; }
    public int? EjecucionProduccionID { get; set; }

    public string NumeroOF { get; set; } = string.Empty;

    public int? MaquinaID { get; set; }
    public string? MaquinaCodigo { get; set; }
    public string? MaquinaNombre { get; set; }

    public int? ParteID { get; set; }
    public string? NumeroParte { get; set; }
    public string? DescripcionParte { get; set; }

    public int? MaterialSolicitadoID { get; set; }
    public int? MaterialEntregadoID { get; set; }

    public int? EmbalajeSolicitadoID { get; set; }
    public int? EmbalajeEntregadoID { get; set; }

    public string? CodigoSolicitado { get; set; }
    public string? DescripcionSolicitada { get; set; }

    public string CodigoEntregado { get; set; } = string.Empty;
    public string? DescripcionEntregada { get; set; }

    public string? TipoMP { get; set; }
    public string? Lote { get; set; }
    public string Unidad { get; set; } = string.Empty;

    public decimal CantidadEntregadaAlmacen { get; set; }
    public decimal? CantidadRecibidaProduccion { get; set; }
    public decimal? CantidadDiferencia { get; set; }

    public decimal CantidadRequeridaOF { get; set; }
    public decimal CantidadEntregadaAcumuladaAlmacen { get; set; }
    public decimal CantidadRecibidaAcumuladaProduccion { get; set; }

    public DateTime FechaEntregaAlmacen { get; set; }
    public int? UsuarioEntregaAlmacenID { get; set; }
    public string? UsuarioEntregaAlmacenNombre { get; set; }
    public string? ReferenciaOperacion { get; set; }
    public string? ObservacionesAlmacen { get; set; }

    public string EstadoRecepcion { get; set; } =
        ProduccionRecepcionMaterialEstado.Pendiente;

    public string? MotivoDiferencia { get; set; }
    public string? ObservacionesRecepcion { get; set; }

    public int? UsuarioRecepcionID { get; set; }
    public string? UsuarioRecepcionNombre { get; set; }
    public DateTime? FechaRecepcion { get; set; }

    public int? GrupoLhRh { get; set; }
    public string? LadoLhRh { get; set; }
    public int? ProgramaParejaID { get; set; }
    public int? EjecucionParejaID { get; set; }
    public string? NumeroOFPareja { get; set; }
    public int? ParteParejaID { get; set; }
    public string? NumeroPartePareja { get; set; }
    public string? ReferenciaSAPPareja { get; set; }
    public string? DescripcionPartePareja { get; set; }
    public bool EsParejaLhRh => GrupoLhRh.HasValue && ProgramaParejaID.HasValue && ProgramaParejaID.Value > 0;

    public string EstadoAclaracion { get; set; } =
        ProduccionRecepcionMaterialEstadoAclaracion.NoAplica;

    public string? ResolucionAclaracion { get; set; }
    public DateTime? FechaResolucion { get; set; }

    public bool EsMateriaPrima =>
        string.Equals(
            TipoOrigen,
            ProduccionRecepcionMaterialTipo.MP,
            StringComparison.OrdinalIgnoreCase);

    public bool EsEmbalaje =>
        string.Equals(
            TipoOrigen,
            ProduccionRecepcionMaterialTipo.Embalaje,
            StringComparison.OrdinalIgnoreCase);

    public bool EstaPendiente =>
        string.Equals(
            EstadoRecepcion,
            ProduccionRecepcionMaterialEstado.Pendiente,
            StringComparison.OrdinalIgnoreCase);

    public bool EstaRecibidoCompleto =>
        string.Equals(
            EstadoRecepcion,
            ProduccionRecepcionMaterialEstado.RecibidoCompleto,
            StringComparison.OrdinalIgnoreCase);

    public bool EstaRecibidoParcial =>
        string.Equals(
            EstadoRecepcion,
            ProduccionRecepcionMaterialEstado.RecibidoParcial,
            StringComparison.OrdinalIgnoreCase);

    public bool EstaNoRecibido =>
        string.Equals(
            EstadoRecepcion,
            ProduccionRecepcionMaterialEstado.NoRecibido,
            StringComparison.OrdinalIgnoreCase);

    public bool AclaracionPendiente =>
        string.Equals(
            EstadoAclaracion,
            ProduccionRecepcionMaterialEstadoAclaracion.Pendiente,
            StringComparison.OrdinalIgnoreCase);

    public bool TieneDiferencia =>
        EstaRecibidoParcial ||
        EstaNoRecibido ||
        (CantidadDiferencia.HasValue && CantidadDiferencia.Value > 0.0005m);

    public bool EsSustitucion
    {
        get
        {
            if (string.IsNullOrWhiteSpace(CodigoSolicitado) ||
                string.IsNullOrWhiteSpace(CodigoEntregado))
                return false;

            return !string.Equals(
                CodigoSolicitado.Trim(),
                CodigoEntregado.Trim(),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    public string TipoOrigenTexto =>
        EsMateriaPrima
            ? "Materia prima"
            : EsEmbalaje
                ? "Embalaje"
                : TipoOrigen;

    public string TipoMPTexto
    {
        get
        {
            if (!EsMateriaPrima)
                return string.Empty;

            if (string.Equals(TipoMP, "V", StringComparison.OrdinalIgnoreCase))
                return "Virgen";

            if (string.Equals(TipoMP, "M", StringComparison.OrdinalIgnoreCase))
                return "Molido";

            return "Sin especificar";
        }
    }

    public string TextoMaquina
    {
        get
        {
            if (string.IsNullOrWhiteSpace(MaquinaCodigo))
                return "Sin máquina";

            if (string.IsNullOrWhiteSpace(MaquinaNombre))
                return MaquinaCodigo;

            return $"{MaquinaCodigo} - {MaquinaNombre}";
        }
    }

    public string TextoSolicitado
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(CodigoSolicitado) &&
                !string.IsNullOrWhiteSpace(DescripcionSolicitada))
                return $"{CodigoSolicitado} - {DescripcionSolicitada}";

            if (!string.IsNullOrWhiteSpace(CodigoSolicitado))
                return CodigoSolicitado;

            if (!string.IsNullOrWhiteSpace(DescripcionSolicitada))
                return DescripcionSolicitada;

            return "Sin información";
        }
    }

    public string TextoEntregado
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(CodigoEntregado) &&
                !string.IsNullOrWhiteSpace(DescripcionEntregada))
                return $"{CodigoEntregado} - {DescripcionEntregada}";

            if (!string.IsNullOrWhiteSpace(CodigoEntregado))
                return CodigoEntregado;

            if (!string.IsNullOrWhiteSpace(DescripcionEntregada))
                return DescripcionEntregada;

            return "Sin información";
        }
    }

    public decimal CantidadPendientePorEntregar =>
        Math.Max(
            0m,
            CantidadRequeridaOF -
            CantidadEntregadaAcumuladaAlmacen);

    public decimal CantidadPendientePorConfirmar =>
        Math.Max(
            0m,
            CantidadEntregadaAcumuladaAlmacen -
            CantidadRecibidaAcumuladaProduccion);

    public string EstadoRecepcionTexto
    {
        get
        {
            if (EstaPendiente)
                return "Pendiente de confirmar";

            if (EstaRecibidoCompleto)
                return "Recibido";

            if (EstaRecibidoParcial)
                return "Recibido parcialmente";

            if (EstaNoRecibido)
                return "No recibido";

            return EstadoRecepcion;
        }
    }
}

public sealed class ProduccionConfirmarRecepcionMaterialVm
{
    public long RecepcionMaterialID { get; set; }

    public string Decision { get; set; } = string.Empty;

    public decimal? CantidadRecibida { get; set; }

    public string? MotivoDiferencia { get; set; }

    public string? Observaciones { get; set; }
}