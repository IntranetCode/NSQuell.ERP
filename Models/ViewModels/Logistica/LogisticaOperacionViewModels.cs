using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace ERP.NSQuell.Models.ViewModels.Logistica;

public sealed class LogisticaOperacionIndexVm
{
    public int Anio { get; set; }
    public int NumeroSemana { get; set; }

    /*
     * Vista operativa:
     * Dia | Semana | Mes
     *
     * Se conserva Anio/NumeroSemana para no romper la vista/controlador actual.
     */
    public string Vista { get; set; } = "Semana";
    public DateTime FechaReferencia { get; set; } = DateTime.Today;

    public DateTime FechaInicio { get; set; }
    public DateTime FechaFin { get; set; }

    public DateTime FechaAnterior { get; set; }
    public DateTime FechaSiguiente { get; set; }

    public string? Busqueda { get; set; }
    public int? ClienteID { get; set; }
    public string? Estatus { get; set; }
    public string? FormaEnvio { get; set; }
    public bool SoloExpeditados { get; set; }
    public bool SoloIncidencias { get; set; }

    public TimeSpan HoraInicio { get; set; } = new(6, 0, 0);
    public TimeSpan HoraFin { get; set; } = new(20, 0, 0);

    public List<LogisticaOperacionClienteVm> Clientes { get; set; } = new();
    public List<LogisticaOperacionFiltroClienteVm> ClientesFiltro { get; set; } = new();
    public List<LogisticaOperacionUnidadVm> Unidades { get; set; } = new();

    public int TotalClientes => Clientes.Count;
    public int TotalUnidades => Unidades.Count;
    public int UnidadesDisponibles => Unidades.Count(x => x.DisponibleEnPeriodo);
    public int UnidadesConOperacion => Unidades.Count(x => x.Eventos.Count > 0);
    public int ViajesActivos => Unidades.Sum(x => x.Eventos.Count(e => e.EsViajeActivo));

    public int TotalEventos =>
        Clientes.Sum(x => x.Eventos.Count);

    public int TotalRequerimientos =>
        Clientes.Sum(x => x.Eventos.Count(e => e.EsRequerimiento));

    public int TotalEmbarques =>
        Clientes.Sum(x => x.Eventos.Count(e => e.EsEmbarque));

    public int TotalProgramaciones =>
        Clientes.Sum(x => x.Eventos.Count(e => e.EsProgramacion));

    public int Programados =>
        Clientes.Sum(x => x.Eventos.Count(e => e.Estatus == "Programado"));

    public int Preparando =>
        Clientes.Sum(x => x.Eventos.Count(e => e.Estatus == "Preparando"));

    public int Preparados =>
        Clientes.Sum(x => x.Eventos.Count(e => e.Estatus == "Preparado"));

    public int Cargados =>
        Clientes.Sum(x => x.Eventos.Count(e => e.Estatus == "Cargado"));

    public int EnRuta =>
        Clientes.Sum(x => x.Eventos.Count(e => e.Estatus == "En ruta"));

    public int Entregados =>
        Clientes.Sum(x => x.Eventos.Count(e => e.Estatus == "Entregado"));

    public int ConIncidencia =>
        Clientes.Sum(x => x.Eventos.Count(e => e.TieneIncidencia));

    public int Expeditados =>
        Clientes.Sum(x => x.Eventos.Count(e => e.EsExpeditado));

    public long TotalPiezas =>
        Clientes.Sum(x => x.Eventos.Sum(e => (long)e.TotalPiezas));

    public long TotalPendienteProgramar =>
        Clientes.Sum(x => x.Eventos.Sum(e => (long)e.CantidadPendienteProgramar));

    public string SemanaTexto =>
        $"W{NumeroSemana:00} {Anio}";

    public string RangoTexto =>
        $"{FechaInicio:dd/MM/yyyy} - {FechaFin:dd/MM/yyyy}";

    public string PeriodoTexto =>
        Vista switch
        {
            "Dia" => FechaReferencia.ToString("dd/MM/yyyy"),
            "Mes" => FechaReferencia.ToString("MMMM yyyy"),
            _ => RangoTexto
        };

    public IEnumerable<DateTime> DiasPeriodo
    {
        get
        {
            for (var fecha = FechaInicio.Date; fecha <= FechaFin.Date; fecha = fecha.AddDays(1))
                yield return fecha;
        }
    }

    /*
     * Compatibilidad con la vista semanal actual.
     */
    public IEnumerable<DateTime> DiasSemana => DiasPeriodo;
}

public sealed class LogisticaOperacionClienteVm
{
    public int ClienteID { get; set; }
    public string Cliente { get; set; } = string.Empty;

    public List<LogisticaOperacionEventoVm> Eventos { get; set; } = new();

    public int TotalEventos => Eventos.Count;
    public int TotalRequerimientos => Eventos.Count(x => x.EsRequerimiento);
    public int TotalEmbarques => Eventos.Count(x => x.EsEmbarque);
    public int TotalProgramaciones => Eventos.Count(x => x.EsProgramacion);

    public long TotalPiezas =>
        Eventos.Sum(x => (long)x.TotalPiezas);

    public long TotalPendienteProgramar =>
        Eventos.Sum(x => (long)x.CantidadPendienteProgramar);

    public bool TieneExpeditados =>
        Eventos.Any(x => x.EsExpeditado);

    public bool TieneIncidencias =>
        Eventos.Any(x => x.TieneIncidencia);

    public IEnumerable<LogisticaOperacionEventoVm> EventosDia(DateTime fecha) =>
        Eventos
            .Where(x => x.FechaVisual.Date == fecha.Date)
            .OrderBy(x => x.HoraProgramada ?? TimeSpan.Zero)
            .ThenBy(x => x.TipoOrden)
            .ThenBy(x => x.EventoID);
}

public sealed class LogisticaOperacionEventoVm
{
    /*
     * Requerimiento = necesidad proveniente del Release.
     * Programacion  = decisión de Logística todavía no convertida totalmente a embarque.
     * Embarque      = salida física ya creada.
     */
    public string TipoEvento { get; set; } = "Embarque";

    public int EventoID { get; set; }

    public int? ReleaseDetalleID { get; set; }
    public int? ParteID { get; set; }

    public int? EmbarqueID { get; set; }
    public int? ListaCargaProgramacionID { get; set; }

    public int ClienteID { get; set; }
    public string Cliente { get; set; } = string.Empty;

    public string Folio { get; set; } = string.Empty;
    public string FolioRelease { get; set; } = string.Empty;
    public string NumeroOF { get; set; } = string.Empty;
    public string NumeroParte { get; set; } = string.Empty;
    public string DescripcionParte { get; set; } = string.Empty;

    /*
     * FechaVisual es la fecha donde se pinta el evento en el Centro Operativo.
     *
     * Para Embarque/Programacion normalmente coincide con FechaProgramada.
     * Para requerimientos expeditados puede ser HOY aunque la FechaRequeridaOriginal
     * permanezca intacta.
     */
    public DateTime FechaProgramada { get; set; }
    public DateTime FechaVisual { get; set; }

    public TimeSpan? HoraProgramada { get; set; }

    /*
     * FechaRequeridaOriginal es la fecha real del Release y nunca debe modificarse.
     * FechaEntregaRequerida se conserva por compatibilidad con el controlador actual.
     */
    public DateTime? FechaRequeridaOriginal { get; set; }
    public DateTime? FechaEntregaRequerida { get; set; }

    public string Estatus { get; set; } = string.Empty;
    public string Criticidad { get; set; } = string.Empty;

    public string TipoOperacion { get; set; } = string.Empty;
    public string FormaEnvio { get; set; } = string.Empty;
    public string ModalidadEnvio { get; set; } = string.Empty;

    public string Destino { get; set; } = string.Empty;

    public int CantidadRequerida { get; set; }
    public int CantidadPendienteProgramar { get; set; }
    public int CantidadProgramada { get; set; }
    public int CantidadGenerada { get; set; }
    public int CantidadEnviada { get; set; }

    public int TotalPartidas { get; set; }
    public int TotalCajas { get; set; }
    public int TotalPiezas { get; set; }
    public int TotalPiezasPreparadas { get; set; }
    public int TotalPiezasDespachadas { get; set; }

    public bool TieneIncidencia { get; set; }
    public int IncidenciasAbiertas { get; set; }
    public int IncidenciasCriticas { get; set; }

    public int DocumentosObligatorios { get; set; }
    public int DocumentosCompletos { get; set; }

    public int Evidencias { get; set; }

    public string NumeroParteResumen { get; set; } = string.Empty;
    public string DescripcionResumen { get; set; } = string.Empty;

    /*
     * Token de concurrencia del embarque.
     * SQL Server lo expone como timestamp/rowversion y el controlador lo maneja en Base64.
     */
    public string? RowVersion { get; set; }

    public bool EsRequerimiento =>
        string.Equals(
            TipoEvento,
            "Requerimiento",
            StringComparison.OrdinalIgnoreCase);

    public bool EsProgramacion =>
        string.Equals(
            TipoEvento,
            "Programacion",
            StringComparison.OrdinalIgnoreCase);

    public bool EsEmbarque =>
        string.Equals(
            TipoEvento,
            "Embarque",
            StringComparison.OrdinalIgnoreCase);

    public string EventoKey =>
        EsRequerimiento
            ? $"R-{ReleaseDetalleID ?? EventoID}"
            : EsProgramacion
                ? $"P-{ListaCargaProgramacionID ?? EventoID}"
                : $"E-{EmbarqueID ?? EventoID}";

    public DateTime? FechaRequeridaReal =>
        FechaRequeridaOriginal ?? FechaEntregaRequerida;

    public int DiasAtraso
    {
        get
        {
            if (!FechaRequeridaReal.HasValue)
                return 0;

            var fechaComparacion =
                CantidadEnviada > 0 && EsEmbarque
                    ? FechaProgramada.Date
                    : DateTime.Today;

            return fechaComparacion.Date > FechaRequeridaReal.Value.Date
                ? (fechaComparacion.Date - FechaRequeridaReal.Value.Date).Days
                : 0;
        }
    }

    public bool EstaVencido =>
        DiasAtraso > 0;

    public bool TieneProgramacion =>
        CantidadProgramada > 0 || ListaCargaProgramacionID.HasValue;

    public bool TieneEmbarque =>
        EmbarqueID.HasValue;

    public bool EstaEnviado =>
        CantidadEnviada > 0
        || Estatus is "En ruta" or "Entregado";

    /*
     * Un requerimiento es expeditado mientras su fecha requerida haya vencido
     * y todavía exista saldo operativo pendiente.
     *
     * Para Programacion/Embarque se conserva también Criticidad=Expeditado,
     * porque el controlador actual ya la utiliza.
     */
    public bool EsExpeditado =>
        (
            string.Equals(
                Criticidad,
                "Expeditado",
                StringComparison.OrdinalIgnoreCase)
        )
        ||
        (
            FechaRequeridaReal.HasValue
            && FechaRequeridaReal.Value.Date < DateTime.Today
            && !EstaEnviado
            && Estatus is not "Entregado" and not "Cancelado"
        );

    public bool SinProgramar =>
        EsRequerimiento
        && CantidadPendienteProgramar > 0
        && !TieneProgramacion;

    public bool ProgramadoNoGenerado =>
        EsProgramacion
        && CantidadProgramada > CantidadGenerada;

    public bool EmbarquePendienteSalida =>
        EsEmbarque
        && Estatus is not "En ruta" and not "Entregado" and not "Cancelado";

    /*
     * Inicialmente:
     * - Requerimiento: se podrá arrastrar para crear programación.
     * - Programación: se podrá arrastrar mientras no tenga cantidad generada.
     * - Embarque: el controlador actual permite Programado/Preparando.
     *
     * Las reglas reales deben volver a validarse en servidor.
     */
    public bool PuedeArrastrar =>
        EsRequerimiento
            ? CantidadPendienteProgramar > 0
            : EsProgramacion
                ? CantidadGenerada <= 0
                : EsEmbarque
                  && Estatus is "Programado" or "Preparando";

    public bool RequiereConfirmacionMover =>
        EsEmbarque
        && Estatus == "Preparado";

    public bool Bloqueado =>
        EsEmbarque
        && Estatus is "Cargado" or "En ruta" or "Entregado" or "Cancelado";

    public bool FaltaDefinirSalida =>
        EsEmbarque
        &&
        (
            string.IsNullOrWhiteSpace(FormaEnvio)
            ||
            string.Equals(
                FormaEnvio,
                "Pendiente",
                StringComparison.OrdinalIgnoreCase)
        );

    public int TipoOrden =>
        EsRequerimiento
            ? 0
            : EsProgramacion
                ? 1
                : 2;

    public decimal PorcentajePreparacion =>
        TotalPiezas <= 0
            ? 0
            : Math.Round(
                Math.Min(
                    100m,
                    (decimal)TotalPiezasPreparadas * 100m / TotalPiezas),
                1);

    public decimal PorcentajeDespachado =>
        TotalPiezas <= 0
            ? 0
            : Math.Round(
                Math.Min(
                    100m,
                    (decimal)TotalPiezasDespachadas * 100m / TotalPiezas),
                1);

    public string HoraTexto =>
        HoraProgramada.HasValue
            ? HoraProgramada.Value.ToString(@"hh\:mm")
            : EsRequerimiento
                ? "Requerido"
                : "Sin hora";

    public string FechaHoraTexto =>
        HoraProgramada.HasValue
            ? $"{FechaVisual:dd/MM/yyyy} {HoraProgramada.Value:hh\\:mm}"
            : FechaVisual.ToString("dd/MM/yyyy");

    public string FechaRequeridaTexto =>
        FechaRequeridaReal.HasValue
            ? FechaRequeridaReal.Value.ToString("dd/MM/yyyy")
            : string.Empty;

    public string AtrasoTexto =>
        DiasAtraso <= 0
            ? string.Empty
            : DiasAtraso == 1
                ? "+1 día"
                : $"+{DiasAtraso} días";

    public string FormaEnvioTexto =>
        FormaEnvio switch
        {
            "Interno" => "Entrega NS",
            "Cliente" => "Cliente recoge",
            "Paqueteria" => "Paquetería",
            "Pendiente" => "Por definir",
            _ => string.IsNullOrWhiteSpace(FormaEnvio)
                ? "Por definir"
                : FormaEnvio
        };

    public string Icono =>
        EsRequerimiento
            ? "fa-clipboard-list"
            : EsProgramacion
                ? "fa-cart-flatbed"
                : FormaEnvio switch
                {
                    "Cliente" => "fa-user-check",
                    "Paqueteria" => "fa-box",
                    _ => "fa-truck"
                };

    public string ClaseEstatus =>
        EsRequerimiento
            ? "op-requerimiento"
            : EsProgramacion && Estatus == "Por generar"
                ? "op-programacion"
                : Estatus switch
                {
                    "Programado" => "op-programado",
                    "Preparando" => "op-preparando",
                    "Preparado" => "op-preparado",
                    "Cargando" => "op-cargando",
                    "Cargado" => "op-cargado",
                    "En ruta" => "op-enruta",
                    "Entregado" => "op-entregado",
                    "Cancelado" => "op-cancelado",
                    _ => "op-pendiente"
                };

    public string ClaseEvento =>
        EsExpeditado
            ? $"{ClaseEstatus} op-expeditado"
            : ClaseEstatus;
}

public sealed class LogisticaOperacionFiltroClienteVm
{
    public int ClienteID { get; set; }
    public string Cliente { get; set; } = string.Empty;
}

public sealed class LogisticaOperacionMoverVm
{
    [Required]
    [Range(1, int.MaxValue)]
    public int EmbarqueID { get; set; }

    [Required]
    [DataType(DataType.Date)]
    public DateTime FechaCargaProgramada { get; set; }

    [Required]
    public TimeSpan? HoraCargaProgramada { get; set; }

    public string? RowVersion { get; set; }

    [StringLength(500)]
    public string? Observaciones { get; set; }
}

/*
 * Se usará cuando el usuario arrastre un requerimiento del Release
 * hacia un día/hora del Centro Operativo.
 */
public sealed class LogisticaOperacionProgramarReleaseVm
{
    [Required]
    [Range(1, int.MaxValue)]
    public int ReleaseDetalleID { get; set; }

    [Required]
    [Range(1, int.MaxValue)]
    public int ClienteID { get; set; }

    [Required]
    [Range(1, int.MaxValue)]
    public int ParteID { get; set; }

    [Required]
    [Range(1, int.MaxValue)]
    public int Cantidad { get; set; }

    [Required]
    [DataType(DataType.Date)]
    public DateTime FechaProgramadaCarga { get; set; }

 

    [StringLength(500)]
    public string? Observaciones { get; set; }
}

/*
 * Se usará para arrastrar una programación que todavía no fue convertida
 * en embarque.
 */
public sealed class LogisticaOperacionMoverProgramacionVm
{
    [Required]
    [Range(1, int.MaxValue)]
    public int ListaCargaProgramacionID { get; set; }

    [Required]
    [DataType(DataType.Date)]
    public DateTime FechaProgramadaCarga { get; set; }


    [StringLength(500)]
    public string? Observaciones { get; set; }
}

public sealed class LogisticaOperacionMovimientoRespuestaVm
{
    public bool Ok { get; set; }
    public string Mensaje { get; set; } = string.Empty;

    public int EmbarqueID { get; set; }

    public DateTime? FechaCargaProgramada { get; set; }
    public TimeSpan? HoraCargaProgramada { get; set; }

    public string? RowVersion { get; set; }

    public string FechaTexto =>
        FechaCargaProgramada.HasValue
            ? FechaCargaProgramada.Value.ToString("dd/MM/yyyy")
            : string.Empty;

    public string HoraTexto =>
        HoraCargaProgramada.HasValue
            ? HoraCargaProgramada.Value.ToString(@"hh\:mm")
            : string.Empty;
}

public sealed class LogisticaOperacionDetalleVm
{
    public int EmbarqueID { get; set; }

    public string Folio { get; set; } = string.Empty;

    public int ClienteID { get; set; }
    public string Cliente { get; set; } = string.Empty;

    public string Destino { get; set; } = string.Empty;
    public string DireccionEntrega { get; set; } = string.Empty;

    public DateTime? FechaCargaProgramada { get; set; }
    public TimeSpan? HoraCargaProgramada { get; set; }
    public DateTime? FechaEntregaProgramada { get; set; }

    public string Estatus { get; set; } = string.Empty;

    public string TipoOperacion { get; set; } = string.Empty;
    public string FormaEnvio { get; set; } = string.Empty;
    public string ModalidadEnvio { get; set; } = string.Empty;

    public string Transportista { get; set; } = string.Empty;
    public string GuiaReferencia { get; set; } = string.Empty;

    public string Ruta { get; set; } = string.Empty;
    public string Unidad { get; set; } = string.Empty;
    public string Chofer { get; set; } = string.Empty;

    public int TotalPartidas { get; set; }
    public int TotalCajas { get; set; }

    public int TotalPiezas { get; set; }
    public int TotalPiezasPreparadas { get; set; }
    public int TotalPiezasDespachadas { get; set; }

    public int PorcentajeAvance { get; set; }

    public bool TieneIncidencia { get; set; }
    public int IncidenciasAbiertas { get; set; }
    public int IncidenciasCriticas { get; set; }

    public int TotalDocumentos { get; set; }
    public int DocumentosFaltantes { get; set; }
    public int TotalEvidencias { get; set; }

    public bool DatosTransporteCompletos { get; set; }
    public bool PreparacionCompleta { get; set; }
    public bool DocumentacionCompleta { get; set; }

    public string ProximaAccion { get; set; } = string.Empty;
    public string ProximaAccionDetalle { get; set; } = string.Empty;

    public List<LogisticaOperacionPartidaVm> Partidas { get; set; } = new();
    public List<LogisticaOperacionHistorialVm> Historial { get; set; } = new();
    public List<LogisticaOperacionEvidenciaResumenVm> Evidencias { get; set; } = new();

    public bool PuedeDefinirSalida =>
        Estatus is "Programado" or "Preparando" or "Preparado";

    public bool PuedePreparar =>
        Estatus is "Programado" or "Preparando";

    public bool PuedeConfirmarCarga =>
        Estatus == "Preparado";

    public bool PuedeCerrar =>
        Estatus == "En ruta";

    public bool Cerrado =>
        Estatus is "Entregado" or "Cancelado";
}

public sealed class LogisticaOperacionPartidaVm
{
    public int EmbarqueDetalleID { get; set; }
    public int? ReleaseDetalleID { get; set; }

    public string FolioRelease { get; set; } = string.Empty;
    public string NumeroParte { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string NumeroOF { get; set; } = string.Empty;

    public int CantidadSolicitada { get; set; }
    public int CantidadPreparada { get; set; }
    public int CantidadDespachada { get; set; }

    public int PendientePreparar =>
        Math.Max(
            0,
            CantidadSolicitada - CantidadPreparada);
}

public sealed class LogisticaOperacionHistorialVm
{
    public int HistorialID { get; set; }
    public int EmbarqueID { get; set; }

    public string Evento { get; set; } = string.Empty;
    public string EstadoAnterior { get; set; } = string.Empty;
    public string EstadoNuevo { get; set; } = string.Empty;
    public string Observaciones { get; set; } = string.Empty;

    public int? UsuarioID { get; set; }
    public string Usuario { get; set; } = string.Empty;

    public DateTime Fecha { get; set; }

    public string FechaTexto =>
        Fecha.ToString("dd/MM/yyyy HH:mm");
}

public sealed class LogisticaOperacionEvidenciaResumenVm
{
    public int EvidenciaID { get; set; }
    public int? ViajeID { get; set; }
    public string Origen { get; set; } = string.Empty;
    public string TipoEvidencia { get; set; } = string.Empty;
    public string NombreOriginal { get; set; } = string.Empty;
    public string TipoContenido { get; set; } = string.Empty;
    public long TamanoBytes { get; set; }
    public string Observaciones { get; set; } = string.Empty;
    public DateTime FechaCarga { get; set; }
    public string Usuario { get; set; } = string.Empty;

    public bool EsDeViaje => string.Equals(Origen, "Viaje", StringComparison.OrdinalIgnoreCase);
    public bool EsDeEmbarque => string.Equals(Origen, "Embarque", StringComparison.OrdinalIgnoreCase);
    public bool EsImagen => TipoContenido.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
    public bool EsPdf => TipoContenido.Equals("application/pdf", StringComparison.OrdinalIgnoreCase);
    public string Extension => Path.GetExtension(NombreOriginal)?.ToLowerInvariant() ?? string.Empty;
    public string Icono => Extension switch
    {
        ".pdf" => "fa-file-pdf",
        ".jpg" or ".jpeg" or ".png" or ".webp" or ".heic" or ".heif" => "fa-image",
        _ => "fa-file"
    };
    public string TamanoTexto => TamanoBytes <= 0 ? "-" : TamanoBytes < 1024 ? $"{TamanoBytes:N0} B" : TamanoBytes < 1024L * 1024L ? $"{TamanoBytes / 1024d:N1} KB" : $"{TamanoBytes / 1024d / 1024d:N1} MB";
}

public sealed class LogisticaOperacionCerrarVm
{
    [Required]
    [Range(1, int.MaxValue)]
    public int EmbarqueID { get; set; }

    [Required(ErrorMessage = "Captura quién recibió la mercancía.")]
    [StringLength(200)]
    [Display(Name = "Receptor")]
    public string ReceptorNombre { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Fecha y hora de entrega")]
    public DateTime FechaEntrega { get; set; } = DateTime.Now;

    [StringLength(100)]
    [Display(Name = "Folio de remisión")]
    public string? FolioRemision { get; set; }

    [StringLength(1200)]
    public string? Observaciones { get; set; }

    public List<IFormFile> Evidencias { get; set; } = new();

    [StringLength(500)]
    [Display(Name = "Observaciones de evidencia")]
    public string? ObservacionesEvidencia { get; set; }

    public string? RowVersion { get; set; }
}

public sealed class LogisticaOperacionGenerarEmbarqueVm
{
    [Required]
    [Range(1, int.MaxValue)]
    public int ClienteID { get; set; }

    [Required]
    [DataType(DataType.Date)]
    public DateTime FechaProgramada { get; set; }

    [Required(ErrorMessage = "Selecciona la hora programada del embarque.")]
    public TimeSpan? HoraProgramada { get; set; }

    [Required(ErrorMessage = "Selecciona al menos una programación.")]
    public List<int> ProgramacionIDs { get; set; } = new();

    [StringLength(1000)]
    public string? Observaciones { get; set; }
}

public sealed class LogisticaOperacionPrepararEmbarqueVm
{
    public int ClienteID { get; set; }
    public string Cliente { get; set; } = string.Empty;

    public DateTime FechaProgramada { get; set; }

    public List<LogisticaOperacionProgramacionEmbarqueVm> Programaciones { get; set; } = new();

    public int TotalProgramaciones => Programaciones.Count;

    public int TotalPiezas => Programaciones.Sum(x => x.CantidadPendiente);

    public bool TieneProgramaciones => Programaciones.Count > 0;

    public string FechaTexto => FechaProgramada.ToString("dd/MM/yyyy");
}

public sealed class LogisticaOperacionProgramacionEmbarqueVm
{
    public int ListaCargaProgramacionID { get; set; }

    public int ReleaseDetalleID { get; set; }

    public int ClienteID { get; set; }

    public int ParteID { get; set; }

    public string FolioRelease { get; set; } = string.Empty;

    public string NumeroParte { get; set; } = string.Empty;

    public string Descripcion { get; set; } = string.Empty;

    public string NumeroOF { get; set; } = string.Empty;

    public DateTime FechaRequerida { get; set; }

    public DateTime FechaProgramada { get; set; }

    public int CantidadProgramada { get; set; }

    public int CantidadGenerada { get; set; }

    public string Criticidad { get; set; } = string.Empty;

    public int CantidadPendiente => Math.Max(0, CantidadProgramada - CantidadGenerada);

    public bool DisponibleParaEmbarque => CantidadPendiente > 0;

    public bool EsExpeditado =>
        string.Equals(Criticidad, "Expeditado", StringComparison.OrdinalIgnoreCase)
        || FechaRequerida.Date < DateTime.Today;

    public string FechaRequeridaTexto => FechaRequerida.ToString("dd/MM/yyyy");
}

public sealed class LogisticaOperacionCrearEmbarqueResultadoVm
{
    public bool Ok { get; set; }

    public string Mensaje { get; set; } = string.Empty;

    public int EmbarqueID { get; set; }

    public string Folio { get; set; } = string.Empty;

    public int ClienteID { get; set; }

    public DateTime FechaCargaProgramada { get; set; }

    public TimeSpan HoraCargaProgramada { get; set; }

    public int TotalProgramaciones { get; set; }

    public int TotalPiezas { get; set; }

    public string? RowVersion { get; set; }

    public string FechaTexto => FechaCargaProgramada.ToString("dd/MM/yyyy");

    public string HoraTexto => HoraCargaProgramada.ToString(@"hh\:mm");
}
public sealed class LogisticaOperacionResultadoVm
{
    public bool Ok { get; set; }
    public string Mensaje { get; set; } = string.Empty;

    public int? ReleaseDetalleID { get; set; }
    public int? ListaCargaProgramacionID { get; set; }
    public int? EmbarqueID { get; set; }

    public string? Folio { get; set; }
    public string? RowVersion { get; set; }
}


public sealed class LogisticaOperacionFlujoVm
{
    public int EmbarqueID { get; set; }
    public string Folio { get; set; } = string.Empty;
    public int ClienteID { get; set; }
    public string Cliente { get; set; } = string.Empty;
    public string Destino { get; set; } = string.Empty;
    public string Estatus { get; set; } = string.Empty;
    public DateTime? FechaCargaProgramada { get; set; }
    public TimeSpan? HoraCargaProgramada { get; set; }
    public DateTime? FechaEntregaProgramada { get; set; }
    public int TotalPiezas { get; set; }
    public int TotalPiezasPreparadas { get; set; }
    public int TotalPiezasDespachadas { get; set; }
    public int TotalCajas { get; set; }
    public int TotalCajasCargadas { get; set; }
    public int DocumentosFaltantes { get; set; }
    public int TotalEvidencias { get; set; }

    public int? ViajeID { get; set; }
    public List<LogisticaOperacionEvidenciaResumenVm> Evidencias { get; set; } = new();

    public int IncidenciasAbiertas { get; set; }
    public int IncidenciasCriticas { get; set; }
    public int PasoActual { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public LogisticaOperacionSalidaVm Salida { get; set; } = new();
    public List<LogisticaOperacionPasoVm> Pasos { get; set; } = new();
    public List<LogisticaOperacionCatalogoVm> Rutas { get; set; } = new();
    public List<LogisticaOperacionCatalogoVm> Unidades { get; set; } = new();
    public List<LogisticaOperacionCatalogoVm> Choferes { get; set; } = new();
    public bool Cerrado => Estatus is "Entregado" or "Cancelado";
}

public sealed class LogisticaOperacionPasoVm
{
    public int Numero { get; set; }
    public string Clave { get; set; } = string.Empty;
    public string Titulo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string Icono { get; set; } = string.Empty;
    public bool Completo { get; set; }
    public bool Disponible { get; set; }
    public bool Actual { get; set; }
    public string EstadoTexto => Completo ? "Completo" : Actual ? "En proceso" : Disponible ? "Pendiente" : "Bloqueado";
}

public sealed class LogisticaOperacionCatalogoVm
{
    public int Id { get; set; }
    public string Texto { get; set; } = string.Empty;
}

public sealed class LogisticaOperacionSalidaVm
{
    [Required]
    [Range(1, int.MaxValue)]
    public int EmbarqueID { get; set; }

    [StringLength(30)]
    public string? TipoOperacion { get; set; }

    [StringLength(30)]
    public string? FormaEnvio { get; set; }

    [StringLength(30)]
    public string? ModalidadEnvio { get; set; }

    [StringLength(200)]
    public string? Transportista { get; set; }

    [StringLength(150)]
    public string? GuiaReferencia { get; set; }

    public bool? PasaAduana { get; set; }
    public int? RutaID { get; set; }
    public int? UnidadID { get; set; }
    public int? ChoferUsuarioID { get; set; }

    [StringLength(200)]
    public string? ChoferNombreSnapshot { get; set; }

    [StringLength(200)]
    public string? ChoferExterno { get; set; }

    [StringLength(100)]
    public string? UnidadExterna { get; set; }

    [StringLength(100)]
    public string? PlacasExternas { get; set; }

    public string? RowVersion { get; set; }
}

public sealed class LogisticaOperacionUnidadVm
{
    public int UnidadID { get; set; }
    public string NumeroEconomico { get; set; } = string.Empty;
    public string Placas { get; set; } = string.Empty;
    public string Marca { get; set; } = string.Empty;
    public string Modelo { get; set; } = string.Empty;
    public int? CapacidadPiezas { get; set; }
    public decimal? CapacidadPesoKg { get; set; }
    public bool Activo { get; set; } = true;
    public List<LogisticaOperacionUnidadEventoVm> Eventos { get; set; } = new();
    public string UnidadTexto => string.IsNullOrWhiteSpace(NumeroEconomico) ? $"Unidad {UnidadID}" : NumeroEconomico;
    public string DescripcionUnidad
    {
        get
        {
            var datos = new List<string>();
            if (!string.IsNullOrWhiteSpace(Marca)) datos.Add(Marca);
            if (!string.IsNullOrWhiteSpace(Modelo)) datos.Add(Modelo);
            if (!string.IsNullOrWhiteSpace(Placas)) datos.Add(Placas);
            return datos.Count == 0 ? "Sin datos adicionales" : string.Join(" · ", datos);
        }
    }
    public bool DisponibleEnPeriodo => Activo && !Eventos.Any(x => x.BloqueaDisponibilidad);
    public bool TieneViajeEnCurso => Eventos.Any(x => x.Estatus.Equals("En curso", StringComparison.OrdinalIgnoreCase));
    public bool TieneViajeProgramado => Eventos.Any(x => x.Estatus.Equals("Programado", StringComparison.OrdinalIgnoreCase));
    public string EstadoActual => TieneViajeEnCurso ? "En viaje" : TieneViajeProgramado ? "Programada" : "Disponible";
    public IEnumerable<LogisticaOperacionUnidadEventoVm> EventosDia(DateTime fecha) => Eventos.Where(x => x.IntersectaFecha(fecha)).OrderBy(x => x.FechaHoraInicio).ThenBy(x => x.ViajeID);
}

public sealed class LogisticaOperacionUnidadEventoVm
{
    public int ViajeID { get; set; }
    public string Folio { get; set; } = string.Empty;
    public string TipoViaje { get; set; } = string.Empty;
    public string TipoTransporte { get; set; } = string.Empty;
    public int UnidadID { get; set; }
    public string NumeroEconomico { get; set; } = string.Empty;
    public int? OperadorUsuarioID { get; set; }
    public string Operador { get; set; } = string.Empty;
    public int? RutaID { get; set; }
    public string Ruta { get; set; } = string.Empty;
    public string Origen { get; set; } = string.Empty;
    public string Destino { get; set; } = string.Empty;
    public string Motivo { get; set; } = string.Empty;
    public DateTime FechaProgramada { get; set; }
    public TimeSpan? HoraSalidaProgramada { get; set; }
    public DateTime? FechaSalidaReal { get; set; }
    public DateTime? FechaRegresoReal { get; set; }
    public int? KilometrajeSalida { get; set; }
    public int? KilometrajeRegreso { get; set; }
    public string Estatus { get; set; } = string.Empty;
    public bool TieneIncidencia { get; set; }
    public int IncidenciasAbiertas { get; set; }
    public int IncidenciasCriticas { get; set; }
    public int EvidenciasSalida { get; set; }
    public int EvidenciasRegreso { get; set; }
    public int EvidenciasTrayecto { get; set; }
    public int EvidenciasIncidencia { get; set; }
    public List<LogisticaOperacionViajeEmbarqueVm> Embarques { get; set; } = new();
    public string? RowVersion { get; set; }
    public DateTime FechaHoraInicio => FechaSalidaReal ?? FechaProgramada.Date.Add(HoraSalidaProgramada ?? TimeSpan.Zero);
    public DateTime? FechaHoraFin => FechaRegresoReal;
    public bool EsViajeActivo => Estatus is "Programado" or "En curso";
    public bool BloqueaDisponibilidad => Estatus is "Programado" or "En curso";
    public bool EstaEnCurso => Estatus.Equals("En curso", StringComparison.OrdinalIgnoreCase);
    public bool EstaFinalizado => Estatus.Equals("Finalizado", StringComparison.OrdinalIgnoreCase);
    public bool EstaCancelado => Estatus.Equals("Cancelado", StringComparison.OrdinalIgnoreCase);
    public bool TieneSalida => FechaSalidaReal.HasValue;
    public bool TieneRegreso => FechaRegresoReal.HasValue;
    public bool RequiereKilometrajeInicial => EstaEnCurso && !KilometrajeSalida.HasValue;
    public bool RequiereKilometrajeFinal => TieneSalida && !TieneRegreso;
    public int? KilometrosRecorridos => KilometrajeSalida.HasValue && KilometrajeRegreso.HasValue && KilometrajeRegreso.Value >= KilometrajeSalida.Value ? KilometrajeRegreso.Value - KilometrajeSalida.Value : null;
    public string ClienteResumen
    {
        get
        {
            var clientes = Embarques.Select(x => x.Cliente).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return clientes.Count switch { 0 => string.IsNullOrWhiteSpace(Destino) ? "Sin destino" : Destino, 1 => clientes[0], _ => $"{clientes[0]} +{clientes.Count - 1}" };
        }
    }
    public int TotalEmbarques => Embarques.Count;
    public int TotalPiezas => Embarques.Sum(x => x.TotalPiezas);
    public string HoraTexto => FechaSalidaReal.HasValue ? FechaSalidaReal.Value.ToString("HH:mm") : HoraSalidaProgramada.HasValue ? HoraSalidaProgramada.Value.ToString(@"hh\:mm") : "Sin hora";
    public string EstadoVisual => EstaCancelado ? "Cancelado" : TieneSalida && !TieneRegreso ? "En ruta" : TieneRegreso || EstaFinalizado ? "Finalizado" : "Programado";
    public string Icono => EstadoVisual switch { "En ruta" => "fa-truck-fast", "Finalizado" => "fa-circle-check", "Cancelado" => "fa-ban", _ => "fa-truck" };
    public string ClaseEstatus => EstadoVisual switch { "En ruta" => "unidad-enruta", "Finalizado" => "unidad-finalizada", "Cancelado" => "unidad-cancelada", _ => "unidad-programada" };
    public bool IntersectaFecha(DateTime fecha)
    {
        var dia = fecha.Date;
        var inicio = FechaHoraInicio.Date;
        if (FechaHoraFin.HasValue) return dia >= inicio && dia <= FechaHoraFin.Value.Date;
        return dia >= inicio && EsViajeActivo;
    }
}

public sealed class LogisticaOperacionViajeEmbarqueVm
{
    public int ViajeEmbarqueID { get; set; }
    public int ViajeID { get; set; }
    public int EmbarqueID { get; set; }
    public int? OrdenEntrega { get; set; }
    public string Folio { get; set; } = string.Empty;
    public int ClienteID { get; set; }
    public string Cliente { get; set; } = string.Empty;
    public string Destino { get; set; } = string.Empty;
    public string Estatus { get; set; } = string.Empty;
    public int TotalPartidas { get; set; }
    public int TotalPiezas { get; set; }
    public DateTime? FechaCargaProgramada { get; set; }
    public TimeSpan? HoraCargaProgramada { get; set; }
    public DateTime? FechaEntregaProgramada { get; set; }
}

public sealed class LogisticaOperacionViajeDetalleVm
{
    public int ViajeID { get; set; }
    public string Folio { get; set; } = string.Empty;
    public string Estatus { get; set; } = string.Empty;
    public int? UnidadID { get; set; }
    public string Unidad { get; set; } = string.Empty;
    public string Placas { get; set; } = string.Empty;
    public int? OperadorUsuarioID { get; set; }
    public string Operador { get; set; } = string.Empty;
    public string Origen { get; set; } = string.Empty;
    public string Destino { get; set; } = string.Empty;
    public string Motivo { get; set; } = string.Empty;
    public DateTime FechaProgramada { get; set; }
    public TimeSpan? HoraSalidaProgramada { get; set; }
    public DateTime? FechaSalidaReal { get; set; }
    public DateTime? FechaRegresoReal { get; set; }
    public int? KilometrajeSalida { get; set; }
    public int? KilometrajeRegreso { get; set; }
    public int? KilometrosRecorridos => KilometrajeSalida.HasValue && KilometrajeRegreso.HasValue && KilometrajeRegreso.Value >= KilometrajeSalida.Value ? KilometrajeRegreso.Value - KilometrajeSalida.Value : null;
    public bool TieneIncidencia { get; set; }
    public int IncidenciasAbiertas { get; set; }
    public List<LogisticaOperacionViajeEmbarqueVm> Embarques { get; set; } = new();
    public List<LogisticaOperacionViajeEvidenciaVm> Evidencias { get; set; } = new();
    public string? RowVersion { get; set; }
    public bool PuedeIniciar => Estatus == "Programado" && UnidadID.HasValue && OperadorUsuarioID.HasValue && HoraSalidaProgramada.HasValue;
    public bool PuedeFinalizar => Estatus == "En curso" && FechaSalidaReal.HasValue && KilometrajeSalida.HasValue;
    public bool Cerrado => Estatus is "Finalizado" or "Cancelado";
}

public sealed class LogisticaOperacionViajeEvidenciaVm
{
    public int ViajeEvidenciaID { get; set; }
    public int ViajeID { get; set; }
    public string TipoEvidencia { get; set; } = string.Empty;
    public string NombreOriginal { get; set; } = string.Empty;
    public string TipoContenido { get; set; } = string.Empty;
    public long TamanoBytes { get; set; }
    public string Observaciones { get; set; } = string.Empty;
    public string UsuarioCargaNombre { get; set; } = string.Empty;
    public DateTime FechaCarga { get; set; }
    public bool EsImagen => TipoContenido.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
    public string TamanoTexto => TamanoBytes <= 0 ? "-" : TamanoBytes < 1024 ? $"{TamanoBytes:N0} B" : TamanoBytes < 1024L * 1024L ? $"{TamanoBytes / 1024d:N1} KB" : $"{TamanoBytes / 1024d / 1024d:N1} MB";
}

public sealed class LogisticaOperacionViajeIniciarVm
{
    [Required]
    [Range(1, int.MaxValue)]
    public int ViajeID { get; set; }

    [Required]
    [Display(Name = "Fecha y hora de salida")]
    public DateTime FechaSalida { get; set; } = DateTime.Now;

    [Required(ErrorMessage = "El kilometraje inicial es obligatorio.")]
    [Range(0, int.MaxValue, ErrorMessage = "El kilometraje inicial no es válido.")]
    public int? KilometrajeInicial { get; set; }

    public List<IFormFile> Evidencias { get; set; } = new();

    [StringLength(500)]
    public string? ObservacionesEvidencia { get; set; }

    public string? RowVersion { get; set; }
}

public sealed class LogisticaOperacionViajeFinalizarVm
{
    [Required]
    [Range(1, int.MaxValue)]
    public int ViajeID { get; set; }

    [Required]
    [Display(Name = "Fecha y hora de regreso")]
    public DateTime FechaRegreso { get; set; } = DateTime.Now;

    [Required(ErrorMessage = "El kilometraje final es obligatorio.")]
    [Range(0, int.MaxValue, ErrorMessage = "El kilometraje final no es válido.")]
    public int? KilometrajeFinal { get; set; }

    public List<IFormFile> Evidencias { get; set; } = new();

    [StringLength(500)]
    public string? ObservacionesEvidencia { get; set; }

    public string? RowVersion { get; set; }
}

public sealed class LogisticaOperacionViajeEvidenciaCargaVm
{
    [Required]
    [Range(1, int.MaxValue)]
    public int ViajeID { get; set; }

    [Required]
    [StringLength(30)]
    public string TipoEvidencia { get; set; } = "Trayecto";

    [Required]
    public List<IFormFile> Evidencias { get; set; } = new();

    [StringLength(500)]
    public string? Observaciones { get; set; }
}
