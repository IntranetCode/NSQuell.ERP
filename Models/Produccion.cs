using Microsoft.AspNetCore.Mvc.Rendering;
using static ERP.NSQuell.Models.ProduccionOperadorCajaVm;


namespace ERP.NSQuell.Models;

public static class ProduccionEstatus
{
    public const int Pendiente = 1;
    public const int EnPreparacion = 2;
    public const int EnProduccion = 3;
    public const int Pausado = 4;
    public const int TerminadoParcial = 5;
    public const int Terminado = 6;

    // Almacén ya recibió todas las cajas, pero falta cierre documental.
    public const int ListaCierreDocumental = 8;

    public const int Cerrado = 9;
    public const int Cancelado = 99;

    public static string Nombre(int estatusId)
    {
        return estatusId switch
        {
            Pendiente => "Pendiente",
            EnPreparacion => "En preparación",
            EnProduccion => "En producción",
            Pausado => "Pausado",
            TerminadoParcial => "Terminado parcial",
            Terminado => "Terminado",
            ListaCierreDocumental => "Lista para cierre documental",
            Cerrado => "Cerrado",
            Cancelado => "Cancelado",
            _ => "Desconocido"
        };
    }

    public static string ClaseBadge(int estatusId)
    {
        return estatusId switch
        {
            Pendiente => "bg-secondary",
            EnPreparacion => "bg-warning text-dark",
            EnProduccion => "bg-success",
            Pausado => "bg-danger",
            TerminadoParcial => "bg-info text-dark",
            Terminado => "bg-primary",
            ListaCierreDocumental => "bg-warning text-dark",
            Cerrado => "bg-dark",
            Cancelado => "bg-danger",
            _ => "bg-secondary"
        };
    }

    public static bool PuedeIniciar(int estatusId)
    {
        return estatusId == Pendiente ||
               estatusId == EnPreparacion ||
               estatusId == Pausado;
    }

    public static bool PuedeRegistrarProduccion(int estatusId)
    {
        return estatusId == EnProduccion;
    }

    public static bool PuedePausar(int estatusId)
    {
        return estatusId == EnProduccion;
    }

    public static bool PuedeTerminar(int estatusId)
    {
        return estatusId == EnProduccion ||
               estatusId == Pausado ||
               estatusId == TerminadoParcial;
    }

    public static bool PuedeCerrarDocumentalmente(int estatusId)
    {
        return estatusId == ListaCierreDocumental;
    }

    public static bool EstaBloqueadoParaPlaneacion(int estatusId)
    {
        return estatusId == EnProduccion ||
               estatusId == Pausado ||
               estatusId == TerminadoParcial ||
               estatusId == Terminado ||
               estatusId == ListaCierreDocumental ||
               estatusId == Cerrado;
    }
}

public static class ProgramaProduccionEstatus
{
    public const int Pendiente = 1;
    public const int EnPreparacion = 2;
    public const int EnProduccion = 3;
    public const int Pausado = 4;

    // Planeacion_ProgramaProduccion:
    // La producción terminó físicamente.
    public const int Terminado = 5;

    // Almacén ya recibió todas las cajas, pero falta cierre documental.
    public const int ListaCierreDocumental = 8;

    public const int Cerrado = 9;
    public const int Cancelado = 99;

    public static string Nombre(int estatusId)
    {
        return estatusId switch
        {
            Pendiente => "Pendiente",
            EnPreparacion => "En preparación",
            EnProduccion => "En producción",
            Pausado => "Pausado",
            Terminado => "Terminado",
            ListaCierreDocumental => "Lista para cierre documental",
            Cerrado => "Cerrado",
            Cancelado => "Cancelado",
            _ => "Desconocido"
        };
    }

    public static string ClaseBadge(int estatusId)
    {
        return estatusId switch
        {
            Pendiente => "bg-secondary",
            EnPreparacion => "bg-warning text-dark",
            EnProduccion => "bg-success",
            Pausado => "bg-danger",
            Terminado => "bg-primary",
            ListaCierreDocumental => "bg-warning text-dark",
            Cerrado => "bg-dark",
            Cancelado => "bg-danger",
            _ => "bg-secondary"
        };
    }
}

public static class ProduccionCajaEstatus
{
    public const int FormadaProduccion = 1;
    public const int PendienteCalidad = 2;
    public const int LiberadaCalidad = 3;
    public const int RetenidaGp12Scrap = 4;
    public const int ZonaVerde = 5;
    public const int SalidaProduccion = 6;
    public const int RecibidaAlmacenPt = 7;

    public static string Nombre(int estatusId)
    {
        return estatusId switch
        {
            FormadaProduccion => "Formada en Producción",
            PendienteCalidad => "Pendiente de Calidad",
            LiberadaCalidad => "Liberada por Calidad",
            RetenidaGp12Scrap => "Retenida / GP12 / Scrap",
            ZonaVerde => "Zona verde",
            SalidaProduccion => "Salida de Producción escaneada",
            RecibidaAlmacenPt => "Recibida por Almacén PT",
            _ => "Desconocido"
        };
    }
}
public static class ProduccionProductoIncompletoEstado
{
    public const string Disponible = "DISPONIBLE";
    public const string Reservada = "RESERVADA";
    public const string EnCompletado = "EN_COMPLETADO";
    public const string Completa = "COMPLETA";
    public const string Cancelada = "CANCELADA";
    public static string Nombre(string? estado)
    {
        return estado?.Trim().ToUpperInvariant() switch
        {
            Disponible => "Disponible",
            Reservada => "Reservada",
            EnCompletado => "En completado",
            Completa => "Completa",
            Cancelada => "Cancelada",
            _ => "Sin estado"
        };
    }
}
public static class ProduccionCajaTipo
{
    public const string Ok = "OK";
    public const string Sospechoso = "SOSPECHOSO";
    public const string Scrap = "SCRAP";
    public const string Retencion = "RETENCION";
    public const string Incompleta = "INCOMPLETA";
}
public static class ProduccionCajaOrigenMovimiento
{
    public const string Origen = "ORIGEN";
    public const string Completado = "COMPLETADO";
}
public static class ProduccionTipoEstatus
{
    public const string Programa = "PROGRAMA";
    public const string Ejecucion = "EJECUCION";
    public const string Caja = "CAJA";
}

public static class ProduccionEstadoOperativo
{
    public const string Pendiente = "PENDIENTE";
    public const string Preparando = "PREPARANDO";
    public const string EsperandoCalidad = "ESPERANDO_CALIDAD";
    public const string ArranqueControlado = "ARRANQUE_CONTROLADO";
    public const string PrimerasPiezas = "PRIMERAS_PIEZAS";
    public const string AjustesCalidad = "AJUSTES_CALIDAD";
    public const string LiberadaCalidad = "LIBERADA_CALIDAD";
    public const string Produciendo = "PRODUCIENDO";
    public const string Pausada = "PAUSADA";
    public const string ReliberacionPendiente = "RELIBERACION_PENDIENTE";
    public const string ReliberacionRechazada = "RELIBERACION_RECHAZADA";
    public const string ReliberacionAutorizada = "RELIBERACION_AUTORIZADA";
    public const string MaquinaLiberada = "MAQUINA_LIBERADA";
    public const string NoOperativo = "NO_OPERATIVO";
}

public sealed class ProduccionEjecucionVm
{
    public int EjecucionProduccionID { get; set; }
    public int ProgramaProduccionID { get; set; }
    public int? SolicitudProduccionID { get; set; }
    public int? SolicitudProduccionDetalleID { get; set; }
    public int? ReleaseID { get; set; }
    public int? ReleaseDetalleID { get; set; }

    public int? MaquinaID { get; set; }
    public string? MaquinaCodigo { get; set; }
    public string? MaquinaNombre { get; set; }

    public int? ParteID { get; set; }
    public string? NumeroParte { get; set; }
    public string? ReferenciaSAP { get; set; }
    public string? DescripcionParte { get; set; }

    public int? MoldeID { get; set; }
    public string? MoldeCodigo { get; set; }

    public int? OperadorID { get; set; }
    public string? OperadorNombre { get; set; }

    public DateTime? FechaInicioReal { get; set; }
    public DateTime? FechaFinReal { get; set; }

    public DateTime? FechaLiberacionMaquina { get; set; }
    public int? UsuarioLiberacionMaquinaID { get; set; }
    public string? ObservacionesLiberacionMaquina { get; set; }

    public bool MaquinaLiberada =>
        FechaLiberacionMaquina.HasValue;

    public bool PuedeLiberarMaquina =>
        EstatusID == ProduccionEstatus.EnProduccion &&
        !FechaLiberacionMaquina.HasValue;

    public int? CantidadPlaneada { get; set; }
    public int CantidadOKTotal { get; set; }
    public int CantidadSospechosaTotal { get; set; }
    public int CantidadScrapTotal { get; set; }

    public int EstatusID { get; set; } = ProduccionEstatus.Pendiente;
    public string? Observaciones { get; set; }

    public int? UsuarioCreacionID { get; set; }
    public DateTime FechaCreacion { get; set; }
    public int? UsuarioModificacionID { get; set; }
    public DateTime? FechaModificacion { get; set; }
    public bool Activo { get; set; } = true;

    public int? OperadorAuxiliarID { get; set; }
    public string? OperadorAuxiliarNombre { get; set; }
    public bool OperadoresModificadosManual { get; set; }
    public string? MotivoCambioOperadores { get; set; }
    public bool EsCambioMolde { get; set; }

    // =========================================================
    // CALIDAD - INSPECCIÓN ACTUAL / MÁS RECIENTE DE LA EJECUCIÓN
    // =========================================================
    public int? InspeccionCalidadID { get; set; }
    public string? EstadoCalidad { get; set; }
    public string? ResultadoCalidad { get; set; }
    public string? EtiquetaCalidad { get; set; }

    public bool CalidadLiberado { get; set; }
    public bool RequiereReliberacion { get; set; }
    public bool ConfiguracionCalidadInvalidada { get; set; }

    public DateTime? FechaNotificacionCalidad { get; set; }
    public DateTime? FechaAutorizacionPrearranque { get; set; }
    public DateTime? FechaLiberacionProduccion { get; set; }

    // =========================================================
    // CALIDAD - ÚLTIMA RELIBERACIÓN
    // =========================================================
    public int? ReliberacionID { get; set; }
    public int? NumeroReliberacion { get; set; }
    public string? ResultadoReliberacion { get; set; }
    public DateTime? FechaSolicitudReliberacion { get; set; }
    public DateTime? FechaValidacionReliberacion { get; set; }

    // =========================================================
    // PARO ACTUAL
    // =========================================================
    public bool TieneParoAbierto { get; set; }
    public int? ParoAbiertoID { get; set; }
    public DateTime? FechaInicioParoAbierto { get; set; }
    public bool ParoAbiertoMayorA15Minutos { get; set; }

    // =========================================================
    // ESTADO GENERAL DE PRODUCCIÓN
    // =========================================================
    public string EstatusNombre =>
        ProduccionEstatus.Nombre(EstatusID);

    public string EstatusClase =>
        ProduccionEstatus.ClaseBadge(EstatusID);

    public bool PuedeIniciar =>
        ProduccionEstatus.PuedeIniciar(EstatusID);

    public bool PuedeRegistrarProduccion =>
        ProduccionEstatus.PuedeRegistrarProduccion(EstatusID);

    public bool PuedePausar =>
        ProduccionEstatus.PuedePausar(EstatusID);

    public bool PuedeTerminar =>
        ProduccionEstatus.PuedeTerminar(EstatusID);

    public string EstadoOperativoClave
    {
        get
        {
            if (MaquinaLiberada)
                return ProduccionEstadoOperativo.MaquinaLiberada;

            if (ResultadoReliberacionEs("RECHAZADA"))
                return ProduccionEstadoOperativo.ReliberacionRechazada;

            if (ResultadoReliberacionEs("AUTORIZADA") &&
                EstatusID == ProduccionEstatus.EnPreparacion)
                return ProduccionEstadoOperativo.ReliberacionAutorizada;

            var reliberacionYaAutorizada = ResultadoReliberacionEs("AUTORIZADA");

            if (!reliberacionYaAutorizada &&
                (ResultadoReliberacionEs("PENDIENTE") ||
                 RequiereReliberacion ||
                 EstadoCalidadEs(CalidadEstados.PendienteReliberacion)))
                return ProduccionEstadoOperativo.ReliberacionPendiente;

            if (EstadoCalidadEs(CalidadEstados.DevueltoPrearranque) ||
                EstadoCalidadEs(CalidadEstados.AjustesSolicitados))
                return ProduccionEstadoOperativo.AjustesCalidad;

            if (EstadoCalidadEs(CalidadEstados.PendientePrimerasPiezas) &&
                !CalidadLiberado)
                return ProduccionEstadoOperativo.PrimerasPiezas;

            if (EstadoCalidadEs(CalidadEstados.PendientePrearranque) &&
                !CalidadLiberado)
                return ProduccionEstadoOperativo.EsperandoCalidad;

            if (EstadoCalidadEs(CalidadEstados.ArranqueAutorizado) &&
                !CalidadLiberado)
                return ProduccionEstadoOperativo.ArranqueControlado;

            if (CalidadLiberado &&
                !ConfiguracionCalidadInvalidada &&
                !RequiereReliberacion &&
                EstatusID == ProduccionEstatus.EnPreparacion &&
                (EstadoCalidadEs(CalidadEstados.ProduccionLiberada) ||
                 EstadoCalidadEs(CalidadEstados.MonitoreoActivo)))
                return ProduccionEstadoOperativo.LiberadaCalidad;

            if (EstatusID == ProduccionEstatus.Pausado)
                return ProduccionEstadoOperativo.Pausada;

            if (EstatusID == ProduccionEstatus.EnProduccion)
                return ProduccionEstadoOperativo.Produciendo;

            if (EstatusID == ProduccionEstatus.EnPreparacion)
                return ProduccionEstadoOperativo.Preparando;

            if (EstatusID == ProduccionEstatus.Pendiente)
                return ProduccionEstadoOperativo.Pendiente;

            return ProduccionEstadoOperativo.NoOperativo;
        }
    }
    

    public string EstadoOperativoNombre =>
        EstadoOperativoClave switch
        {
            ProduccionEstadoOperativo.Pendiente =>
                "PENDIENTE",

            ProduccionEstadoOperativo.Preparando =>
                "EN PREPARACIÓN",

            ProduccionEstadoOperativo.EsperandoCalidad =>
                "ESPERANDO CALIDAD",

            ProduccionEstadoOperativo.ArranqueControlado =>
                "ARRANQUE AUTORIZADO",

            ProduccionEstadoOperativo.PrimerasPiezas =>
                "PRIMERAS PIEZAS EN VALIDACIÓN",

            ProduccionEstadoOperativo.AjustesCalidad =>
                "AJUSTES REQUERIDOS",

            ProduccionEstadoOperativo.LiberadaCalidad =>
                "LIBERADA POR CALIDAD",

            ProduccionEstadoOperativo.Produciendo =>
                "PRODUCIENDO",

            ProduccionEstadoOperativo.Pausada =>
                "PAUSADA",

            ProduccionEstadoOperativo.ReliberacionPendiente =>
                "PENDIENTE DE RELIBERACIÓN",

            ProduccionEstadoOperativo.ReliberacionRechazada =>
                "RELIBERACIÓN RECHAZADA",

            ProduccionEstadoOperativo.ReliberacionAutorizada =>
                "RELIBERACIÓN AUTORIZADA",

            ProduccionEstadoOperativo.MaquinaLiberada =>
                "MÁQUINA LIBERADA",

            _ => EstatusNombre.ToUpperInvariant()
        };

    public string EstadoOperativoDescripcion
    {
        get
        {
            switch (EstadoOperativoClave)
            {
                case ProduccionEstadoOperativo.MaquinaLiberada:
                    return FechaLiberacionMaquina.HasValue
                        ? $"Corrida física finalizada {FechaLiberacionMaquina.Value:HH:mm}"
                        : "Corrida física finalizada";

                case ProduccionEstadoOperativo.ReliberacionRechazada:
                    return NumeroReliberacion.HasValue
                        ? $"Reliberación #{NumeroReliberacion.Value} rechazada por Calidad"
                        : "Calidad rechazó la reliberación";

                case ProduccionEstadoOperativo.ReliberacionPendiente:
                    if (NumeroReliberacion.HasValue)
                        return $"Reliberación #{NumeroReliberacion.Value} pendiente de Calidad";

                    if (ParoAbiertoMayorA15Minutos)
                        return "Paro >15 min · requiere nueva autorización";

                    return "Esperando nueva autorización de Calidad";

                case ProduccionEstadoOperativo.ReliberacionAutorizada:
                    return NumeroReliberacion.HasValue
                        ? $"Reliberación #{NumeroReliberacion.Value} autorizada · lista para reiniciar"
                        : "Lista para reiniciar producción";

                case ProduccionEstadoOperativo.AjustesCalidad:
                    if (EstadoCalidadEs(CalidadEstados.DevueltoPrearranque))
                        return "Prearranque devuelto por Calidad";

                    return "Calidad solicitó ajustes antes de liberar";

                case ProduccionEstadoOperativo.PrimerasPiezas:
                    return "Calidad está validando las primeras piezas";

                case ProduccionEstadoOperativo.EsperandoCalidad:
                    return FechaNotificacionCalidad.HasValue
                        ? $"Prearranque enviado {FechaNotificacionCalidad.Value:HH:mm}"
                        : "Prearranque pendiente de revisión";

                case ProduccionEstadoOperativo.ArranqueControlado:
                    return "Puede realizar arranque controlado y primeras piezas";

                case ProduccionEstadoOperativo.LiberadaCalidad:
                    return FechaLiberacionProduccion.HasValue
                        ? $"Liberada {FechaLiberacionProduccion.Value:HH:mm} · lista para iniciar serie"
                        : "Lista para iniciar serie";

                case ProduccionEstadoOperativo.Produciendo:
                    return FechaInicioReal.HasValue
                        ? $"Inicio real {FechaInicioReal.Value:HH:mm}"
                        : "Ejecución en producción";

                case ProduccionEstadoOperativo.Pausada:
                    return TieneParoAbierto && FechaInicioParoAbierto.HasValue
                        ? $"Paro activo desde {FechaInicioParoAbierto.Value:HH:mm}"
                        : "Ejecución pausada";

                case ProduccionEstadoOperativo.Preparando:
                    return "Checklist / preparación en proceso";

                case ProduccionEstadoOperativo.Pendiente:
                    return "Pendiente de iniciar preparación";

                default:
                    return EstatusNombre;
            }
        }
    }

    public string EstadoOperativoClase =>
        EstadoOperativoClave switch
        {
            ProduccionEstadoOperativo.Pendiente =>
                "prod-operativo-pendiente",

            ProduccionEstadoOperativo.Preparando =>
                "prod-operativo-preparacion",

            ProduccionEstadoOperativo.EsperandoCalidad =>
                "prod-operativo-esperando-calidad",

            ProduccionEstadoOperativo.ArranqueControlado =>
                "prod-operativo-arranque",

            ProduccionEstadoOperativo.PrimerasPiezas =>
                "prod-operativo-primeras-piezas",

            ProduccionEstadoOperativo.AjustesCalidad =>
                "prod-operativo-ajustes",

            ProduccionEstadoOperativo.LiberadaCalidad =>
                "prod-operativo-liberada-calidad",

            ProduccionEstadoOperativo.Produciendo =>
                "prod-operativo-produciendo",

            ProduccionEstadoOperativo.Pausada =>
                "prod-operativo-pausada",

            ProduccionEstadoOperativo.ReliberacionPendiente =>
                "prod-operativo-reliberacion",

            ProduccionEstadoOperativo.ReliberacionRechazada =>
                "prod-operativo-bloqueado",

            ProduccionEstadoOperativo.ReliberacionAutorizada =>
                "prod-operativo-reliberacion-autorizada",

            ProduccionEstadoOperativo.MaquinaLiberada =>
                "prod-operativo-maquina-liberada",

            _ => "prod-operativo-pendiente"
        };

    public int EstadoOperativoPrioridad =>
        EstadoOperativoClave switch
        {
            ProduccionEstadoOperativo.ReliberacionRechazada => 10,
            ProduccionEstadoOperativo.ReliberacionPendiente => 20,
            ProduccionEstadoOperativo.AjustesCalidad => 30,
            ProduccionEstadoOperativo.PrimerasPiezas => 40,
            ProduccionEstadoOperativo.EsperandoCalidad => 45,
            ProduccionEstadoOperativo.Pausada => 50,
            ProduccionEstadoOperativo.ReliberacionAutorizada => 60,
            ProduccionEstadoOperativo.LiberadaCalidad => 65,
            ProduccionEstadoOperativo.ArranqueControlado => 70,
            ProduccionEstadoOperativo.Preparando => 80,
            ProduccionEstadoOperativo.Produciendo => 90,
            ProduccionEstadoOperativo.Pendiente => 100,
            ProduccionEstadoOperativo.MaquinaLiberada => 200,
            _ => 999
        };

    public bool EsOperacionActiva =>
        !MaquinaLiberada &&
        (EstatusID == ProduccionEstatus.Pendiente ||
         EstatusID == ProduccionEstatus.EnPreparacion ||
         EstatusID == ProduccionEstatus.EnProduccion ||
         EstatusID == ProduccionEstatus.Pausado);

    public bool EsPreparacionOperativa =>
        EstadoOperativoClave == ProduccionEstadoOperativo.Preparando ||
        EstadoOperativoClave == ProduccionEstadoOperativo.ArranqueControlado ||
        EstadoOperativoClave == ProduccionEstadoOperativo.AjustesCalidad;

    public bool EsEsperandoCalidad =>
        EstadoOperativoClave == ProduccionEstadoOperativo.EsperandoCalidad ||
        EstadoOperativoClave == ProduccionEstadoOperativo.PrimerasPiezas;

    public bool EsLiberadaPorCalidad =>
        EstadoOperativoClave == ProduccionEstadoOperativo.LiberadaCalidad;

    public bool EsProduciendo =>
        EstadoOperativoClave == ProduccionEstadoOperativo.Produciendo;

    public bool EsPausada =>
        EstadoOperativoClave == ProduccionEstadoOperativo.Pausada;

    public bool EsReliberacion =>
        EstadoOperativoClave == ProduccionEstadoOperativo.ReliberacionPendiente ||
        EstadoOperativoClave == ProduccionEstadoOperativo.ReliberacionRechazada ||
        EstadoOperativoClave == ProduccionEstadoOperativo.ReliberacionAutorizada;

    private bool EstadoCalidadEs(string estado)
    {
        return string.Equals(
            EstadoCalidad,
            estado,
            StringComparison.OrdinalIgnoreCase);
    }

    private bool ResultadoReliberacionEs(string resultado)
    {
        return string.Equals(
            ResultadoReliberacion,
            resultado,
            StringComparison.OrdinalIgnoreCase);
    }

    public int CantidadTotalCapturada =>
        CantidadOKTotal +
        CantidadSospechosaTotal +
        CantidadScrapTotal;

    public int CantidadPendiente
    {
        get
        {
            if (!CantidadPlaneada.HasValue)
                return 0;

            return Math.Max(
                0,
                CantidadPlaneada.Value - CantidadOKTotal);
        }
    }

    public sealed class ProduccionLiberarMaquinaPostVm
    {
        public int EjecucionProduccionID { get; set; }
        public string? Observaciones { get; set; }
    }

    public decimal PorcentajeAvance
    {
        get
        {
            if (!CantidadPlaneada.HasValue ||
                CantidadPlaneada.Value <= 0)
            {
                return 0;
            }

            return Math.Round(
                (decimal)CantidadOKTotal /
                CantidadPlaneada.Value *
                100m,
                2);
        }
    }

    public string TituloPrograma
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(ReferenciaSAP))
                return ReferenciaSAP;

            if (!string.IsNullOrWhiteSpace(NumeroParte))
                return NumeroParte;

            return $"Programa {ProgramaProduccionID}";
        }
    }

    public string TextoMaquina =>
        string.IsNullOrWhiteSpace(MaquinaCodigo)
            ? "Sin máquina"
            : $"{MaquinaCodigo} - {MaquinaNombre}";

    public string TextoOperador =>
        string.IsNullOrWhiteSpace(OperadorNombre)
            ? "Sin operador"
            : OperadorNombre;
}
public sealed class ProduccionRegistroHoraVm
{
    public int RegistroHoraID { get; set; }
    public int EjecucionProduccionID { get; set; }
    public int ProgramaProduccionID { get; set; }
    public int? SolicitudProduccionID { get; set; }

    public int? MaquinaID { get; set; }
    public int? OperadorID { get; set; }

    public DateTime FechaProduccion { get; set; } = DateTime.Today;
    public TimeSpan HoraInicio { get; set; }
    public TimeSpan HoraFin { get; set; }

    public int CantidadOK { get; set; }
    public int CantidadSospechosa { get; set; }
    public int CantidadScrap { get; set; }

    public string? Observaciones { get; set; }

    public int? UsuarioCreacionID { get; set; }
    public DateTime FechaCreacion { get; set; }
    public int? UsuarioModificacionID { get; set; }
    public DateTime? FechaModificacion { get; set; }
    public bool Activo { get; set; } = true;

    public int? ObjetivoHora { get; set; }
    public int? ObjetivoBloque { get; set; }
    public bool? CumplioObjetivo { get; set; }
    public int? DiferenciaObjetivo { get; set; }
    public decimal? PorcentajeCumplimiento { get; set; }

    // =========================================================
    // PRODUCCIÓN REAL MEDIANTE CONTADOR DE MÁQUINA
    // =========================================================
    public int? PiezasCalculadasContador { get; set; }
    public decimal? MinutosProductivos { get; set; }

    // =========================================================
    // TIEMPO EXTRA
    // =========================================================
    public bool EsTiempoExtra { get; set; }
    public string? TipoBloque { get; set; }
    public int? TiempoExtraID { get; set; }
    public int? NumeroCorteTiempoExtra { get; set; }

    public bool TieneCambioConfiguracion { get; set; }
    public bool TieneReinicioContador { get; set; }

    public List<ProduccionRegistroHoraSegmentoVm> Segmentos { get; set; } = new();

    public long? ContadorInicial =>
        Segmentos.Count > 0
            ? Segmentos
                .OrderBy(x => x.NumeroSegmento)
                .First()
                .ContadorInicial
            : null;

    public long? ContadorFinal =>
        Segmentos.Count > 0
            ? Segmentos
                .OrderByDescending(x => x.NumeroSegmento)
                .First()
                .ContadorFinal
            : null;

    public long CiclosCalculados =>
        Segmentos
            .Where(x => x.Activo)
            .Sum(x => x.CiclosPeriodo);

    public int TotalCapturado =>
        CantidadOK +
        CantidadSospechosa +
        CantidadScrap;

    public string RangoHora =>
        $"{HoraInicio:hh\\:mm} - {HoraFin:hh\\:mm}";

    public bool TieneCaptura =>
        CantidadOK > 0 ||
        CantidadSospechosa > 0 ||
        CantidadScrap > 0 ||
        !string.IsNullOrWhiteSpace(Observaciones);

    public bool EsCorteTiempoExtra =>
        EsTiempoExtra &&
        TiempoExtraID.HasValue &&
        NumeroCorteTiempoExtra.HasValue;

    public string TipoBloqueTexto =>
        EsCorteTiempoExtra
            ? $"Tiempo extra · corte #{NumeroCorteTiempoExtra}"
            : EsTiempoExtra
                ? "Tiempo extra"
                : "Producción normal";
}
public sealed class ProduccionParoVm
{
    public int ParoID { get; set; }

    public int EjecucionProduccionID { get; set; }
    public int ProgramaProduccionID { get; set; }
    public int? SolicitudProduccionID { get; set; }

    public int? MaquinaID { get; set; }
    public int? OperadorID { get; set; }

    public DateTime FechaInicioParo { get; set; } = DateTime.Now;
    public DateTime? FechaFinParo { get; set; }
    public int? DuracionMinutos { get; set; }

    public int? MotivoParoID { get; set; }
    public string? MotivoParoTexto { get; set; }
    public string? Descripcion { get; set; }

    public bool EsMayorA15Minutos { get; set; }

    public int? UsuarioCreacionID { get; set; }
    public DateTime FechaCreacion { get; set; }
    public int? UsuarioModificacionID { get; set; }
    public DateTime? FechaModificacion { get; set; }
    public bool Activo { get; set; } = true;

    public bool EstaAbierto => !FechaFinParo.HasValue;

    public string DuracionTexto
    {
        get
        {
            var minutos = DuracionMinutos;

            if (!minutos.HasValue && EstaAbierto)
                minutos = (int)Math.Max(0, (DateTime.Now - FechaInicioParo).TotalMinutes);

            if (!minutos.HasValue)
                return "-";

            if (minutos.Value < 60)
                return $"{minutos.Value} min";

            var horas = minutos.Value / 60;
            var resto = minutos.Value % 60;

            return $"{horas} h {resto} min";
        }
    }
}

public sealed class ProduccionMotivoParoVm
{
    public int MotivoParoID { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public bool RequiereComentario { get; set; }
    public bool Activo { get; set; } = true;
}

public sealed class ProduccionBandejaVm
{
    public string? Busqueda { get; set; }
    public int? MaquinaID { get; set; }
    public int? EstatusID { get; set; }
    public DateTime? FechaDesde { get; set; }
    public DateTime? FechaHasta { get; set; }

    public List<SelectListItem> Maquinas { get; set; } = new();
    public List<SelectListItem> Estatus { get; set; } = new();

    public List<ProduccionProgramaDisponibleVm> ProgramasDisponibles { get; set; }
        = new();

    public List<ProduccionProgramaDisponibleVm> ProximosAIniciar { get; set; }
        = new();

    public List<ProduccionAlertaReprogramacionVm> AlertasReprogramacion { get; set; }
        = new();

    public List<ProduccionEjecucionVm> Ejecuciones { get; set; }
        = new();

    public int TotalDisponibles =>
        ProgramasDisponibles.Count;

    public int TotalProximosAIniciar =>
        ProximosAIniciar.Count;

    public int TotalAlertasReprogramacion =>
        AlertasReprogramacion.Count;

    public int TotalAlertasMuyRecientes =>
        AlertasReprogramacion.Count(
            x => x.EsMuyReciente);

    public int TotalAlertasRecientes =>
        AlertasReprogramacion.Count(
            x => !x.EsMuyReciente);

    public bool TieneProximosAIniciar =>
        ProximosAIniciar.Count > 0;

    public bool TieneAlertasReprogramacion =>
        AlertasReprogramacion.Count > 0;

    // =========================================================
    // CONTADORES GENERALES ANTERIORES
    // Se conservan para no romper otras referencias.
    // =========================================================
    public int Total =>
        Ejecuciones.Count;

    public int Pendientes =>
        Ejecuciones.Count(
            x => x.EstatusID == ProduccionEstatus.Pendiente);

    public int EnPreparacion =>
        Ejecuciones.Count(
            x => x.EstatusID == ProduccionEstatus.EnPreparacion);

    public int EnProduccion =>
        Ejecuciones.Count(
            x => x.EstatusID == ProduccionEstatus.EnProduccion);

    public int Pausados =>
        Ejecuciones.Count(
            x => x.EstatusID == ProduccionEstatus.Pausado);

    public int Terminados =>
        Ejecuciones.Count(
            x =>
                x.EstatusID == ProduccionEstatus.Terminado ||
                x.EstatusID == ProduccionEstatus.TerminadoParcial);

    // =========================================================
    // CONTADORES DEL NUEVO PANEL OPERATIVO
    // =========================================================
    public int TotalOperativas =>
        Ejecuciones.Count(
            x => x.EsOperacionActiva);

    public int PreparacionOperativa =>
        Ejecuciones.Count(
            x =>
                x.EsOperacionActiva &&
                x.EsPreparacionOperativa);

    public int EsperandoCalidad =>
        Ejecuciones.Count(
            x =>
                x.EsOperacionActiva &&
                x.EsEsperandoCalidad);

    public int LiberadasPorCalidad =>
        Ejecuciones.Count(
            x =>
                x.EsOperacionActiva &&
                x.EsLiberadaPorCalidad);

    public int ProduciendoOperativas =>
        Ejecuciones.Count(
            x =>
                x.EsOperacionActiva &&
                x.EsProduciendo);

    public int PausadasOperativas =>
        Ejecuciones.Count(
            x =>
                x.EsOperacionActiva &&
                x.EsPausada);

    public int EnReliberacion =>
        Ejecuciones.Count(
            x =>
                x.EsOperacionActiva &&
                x.EsReliberacion);

    public int MaquinasLiberadas =>
        Ejecuciones.Count(
            x => x.MaquinaLiberada);

    public int ReliberacionesPendientes =>
        Ejecuciones.Count(
            x =>
                x.EstadoOperativoClave ==
                ProduccionEstadoOperativo.ReliberacionPendiente);

    public int ReliberacionesRechazadas =>
        Ejecuciones.Count(
            x =>
                x.EstadoOperativoClave ==
                ProduccionEstadoOperativo.ReliberacionRechazada);

    public int ReliberacionesAutorizadas =>
        Ejecuciones.Count(
            x =>
                x.EstadoOperativoClave ==
                ProduccionEstadoOperativo.ReliberacionAutorizada);

    public int PrimerasPiezasPendientes =>
        Ejecuciones.Count(
            x =>
                x.EstadoOperativoClave ==
                ProduccionEstadoOperativo.PrimerasPiezas);
}
public sealed class ProduccionAlertaReprogramacionVm
{
    public int ReprogramacionHistorialID { get; set; }

    public int ProgramaProduccionID { get; set; }

    public int? ProgramaOrigenMovimientoID { get; set; }

    public int? MaquinaAnteriorID { get; set; }
    public string? MaquinaAnteriorCodigo { get; set; }
    public string? MaquinaAnteriorNombre { get; set; }

    public int? MaquinaNuevaID { get; set; }
    public string? MaquinaNuevaCodigo { get; set; }
    public string? MaquinaNuevaNombre { get; set; }

    public DateTime? InicioAnterior { get; set; }
    public DateTime? InicioNuevo { get; set; }

    public DateTime? FinAnterior { get; set; }
    public DateTime? FinNuevo { get; set; }

    public TimeSpan? CambioAnterior { get; set; }
    public TimeSpan? CambioNuevo { get; set; }

    public TimeSpan? ArranqueAnterior { get; set; }
    public TimeSpan? ArranqueNuevo { get; set; }

    public string? NumeroParte { get; set; }
    public string? ReferenciaSAP { get; set; }
    public string? DescripcionParte { get; set; }
    public string? MoldeCodigo { get; set; }

    public string? TipoMovimiento { get; set; }

    public bool EsMovimientoAutomatico { get; set; }

    public string? Motivo { get; set; }

    public int? UsuarioID { get; set; }

    public string? UsuarioNombre { get; set; }

    public DateTime FechaCambio { get; set; }

    public bool EsMuyReciente =>
        FechaCambio >= DateTime.Now.AddHours(-2);

    public bool EsReciente =>
        FechaCambio >= DateTime.Now.AddHours(-24);

    public string NivelAlerta =>
        EsMuyReciente
            ? "MUY_RECIENTE"
            : "RECIENTE";

    public string FechaCambioTexto =>
        FechaCambio.ToString("dd/MM/yyyy HH:mm");

    public string TextoParte
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(ReferenciaSAP))
                return ReferenciaSAP;

            if (!string.IsNullOrWhiteSpace(NumeroParte))
                return NumeroParte;

            return $"Programa {ProgramaProduccionID}";
        }
    }

    public string TextoMaquinaAnterior
    {
        get
        {
            if (string.IsNullOrWhiteSpace(MaquinaAnteriorCodigo))
                return "Sin máquina";

            if (string.IsNullOrWhiteSpace(MaquinaAnteriorNombre))
                return MaquinaAnteriorCodigo;

            return $"{MaquinaAnteriorCodigo} - {MaquinaAnteriorNombre}";
        }
    }

    public string TextoMaquinaNueva
    {
        get
        {
            if (string.IsNullOrWhiteSpace(MaquinaNuevaCodigo))
                return "Sin máquina";

            if (string.IsNullOrWhiteSpace(MaquinaNuevaNombre))
                return MaquinaNuevaCodigo;

            return $"{MaquinaNuevaCodigo} - {MaquinaNuevaNombre}";
        }
    }

    public bool CambioMaquina =>
        MaquinaAnteriorID != MaquinaNuevaID;

    public bool CambioInicio =>
        InicioAnterior != InicioNuevo;

    public bool CambioFin =>
        FinAnterior != FinNuevo;

    public bool CambioHorario =>
        CambioInicio || CambioFin;

    public string Titulo
    {
        get
        {
            if (EsMovimientoAutomatico)
                return "Programa recorrido automáticamente";

            if (CambioMaquina)
                return "Programa cambiado de máquina";

            if (CambioHorario)
                return "Programa reprogramado";

            return "Programación actualizada";
        }
    }

    public string Mensaje
    {
        get
        {
            var cambios = new List<string>();

            if (CambioMaquina)
            {
                cambios.Add(
                    $"Máquina: {TextoMaquinaAnterior} → " +
                    $"{TextoMaquinaNueva}");
            }

            if (CambioInicio)
            {
                cambios.Add(
                    $"Inicio: {FormatearFecha(InicioAnterior)} → " +
                    $"{FormatearFecha(InicioNuevo)}");
            }

            if (CambioFin)
            {
                cambios.Add(
                    $"Fin: {FormatearFecha(FinAnterior)} → " +
                    $"{FormatearFecha(FinNuevo)}");
            }

            if (CambiosCambioMolde())
            {
                cambios.Add(
                    $"Cambio de molde: " +
                    $"{FormatearHora(CambioAnterior)} → " +
                    $"{FormatearHora(CambioNuevo)}");
            }

            if (CambiosArranque())
            {
                cambios.Add(
                    $"Arranque: " +
                    $"{FormatearHora(ArranqueAnterior)} → " +
                    $"{FormatearHora(ArranqueNuevo)}");
            }

            if (cambios.Count == 0)
            {
                cambios.Add(
                    "Se actualizó la programación.");
            }

            return string.Join(" · ", cambios);
        }
    }

    private bool CambiosCambioMolde()
    {
        return CambioAnterior != CambioNuevo;
    }

    private bool CambiosArranque()
    {
        return ArranqueAnterior != ArranqueNuevo;
    }

    private static string FormatearFecha(DateTime? fecha)
    {
        return fecha.HasValue
            ? fecha.Value.ToString("dd/MM/yyyy HH:mm")
            : "Sin fecha";
    }

    private static string FormatearHora(TimeSpan? hora)
    {
        return hora.HasValue
            ? hora.Value.ToString(@"hh\:mm")
            : "Sin hora";
    }
}

public sealed class ProduccionDetalleVm
{
    public ProduccionEjecucionVm Ejecucion { get; set; } = new();

    public List<ProduccionRegistroHoraVm> RegistrosHora { get; set; } = new();

    public List<ProduccionParoVm> Paros { get; set; } = new();

    public List<SelectListItem> MotivosParo { get; set; } = new();

    public int TotalOK =>
        RegistrosHora
            .Where(x => x.Activo)
            .Sum(x => x.CantidadOK);

    public int TotalSospechoso =>
        RegistrosHora
            .Where(x => x.Activo)
            .Sum(x => x.CantidadSospechosa);

    public int TotalScrap =>
        RegistrosHora
            .Where(x => x.Activo)
            .Sum(x => x.CantidadScrap);

    public int TotalCapturado =>
        TotalOK +
        TotalSospechoso +
        TotalScrap;

    public bool TieneParoAbierto =>
        Paros.Any(x =>
            x.Activo &&
            x.EstaAbierto);

    public ProduccionParoVm? ParoAbierto =>
        Paros.FirstOrDefault(x =>
            x.Activo &&
            x.EstaAbierto);

    public ProduccionChecklistResumenVm? ChecklistResumen { get; set; }

    public ProduccionCalidadResumenVm? CalidadResumen { get; set; }

    public List<ProduccionRecepcionOFVm> RecepcionesOF { get; set; } = new();

    public ProduccionMonitoreoTurnoAvisoVm? MonitoreoTurnoActual { get; set; }

    public ProduccionCambioTurnoTecnicoVm? CambioTurnoTecnico { get; set; }

    
    public ProduccionConfiguracionTecnicoVm? ConfiguracionTiempoReal { get; set; }

  
    public ProduccionBonusOperadorResumenVm? BonusOperadorActual { get; set; }

    public bool MostrarAvisoMonitoreoTurno =>
        MonitoreoTurnoActual?.ChecklistPendiente == true;

    public bool TieneConfiguracionTiempoReal =>
        ConfiguracionTiempoReal?.TieneConfiguracionActual == true;
}

public sealed class ProduccionIniciarRequestVm
{
    public int ProgramaProduccionID { get; set; }
    public int? OperadorID { get; set; }
    public string? OperadorNombre { get; set; }
    public string? Observaciones { get; set; }
}

public sealed class ProduccionRegistroHoraPostVm
{
    public int EjecucionProduccionID { get; set; }

    public DateTime FechaProduccion { get; set; } = DateTime.Today;

    public string HoraInicio { get; set; } = string.Empty;
    public string HoraFin { get; set; } = string.Empty;

    public long? ContadorMaquinaActual { get; set; }

    public int CantidadOK { get; set; }
    public bool OkModificadoManual { get; set; }

    public int CantidadSospechosa { get; set; }
    public int CantidadScrap { get; set; }


    public bool EsTiempoExtra { get; set; }
    public int? MinutosTiempoExtra { get; set; }

    public int? TiempoExtraID { get; set; }
    public int? NumeroCorteTiempoExtra { get; set; }

    public bool FinalizarTiempoExtra { get; set; }

    public string? Observaciones { get; set; }

    public List<ProduccionRegistroDefectoPostVm> DefectosScrap { get; set; } = new();
}
public sealed class ProduccionParoPostVm
{
    public int EjecucionProduccionID { get; set; }

    public int? MotivoParoID { get; set; }
    public string? MotivoParoTexto { get; set; }
    public string? Descripcion { get; set; }
}

public sealed class ProduccionCerrarParoPostVm
{
    public int ParoID { get; set; }
    public string? ObservacionesCierre { get; set; }
}

public sealed class ProduccionTerminarPostVm
{
    public int EjecucionProduccionID { get; set; }
    public bool TerminarParcial { get; set; }
    public string? Observaciones { get; set; }
}

public sealed class ProduccionOperadorTabletVm
{
    public int EjecucionProduccionID { get; set; }
    public int ProgramaProduccionID { get; set; }

    public int? SolicitudProduccionID { get; set; }
    public string? FolioSolicitud { get; set; }
    public string? NumeroOFRecibida { get; set; }

    public int? MaquinaID { get; set; }
    public string? MaquinaCodigo { get; set; }
    public string? MaquinaNombre { get; set; }

    public int? ParteID { get; set; }
    public string? NumeroParte { get; set; }
    public string? ReferenciaSAP { get; set; }
    public string? DescripcionParte { get; set; }

    public int? OperadorID { get; set; }
    public string? OperadorNombre { get; set; }

    public int? CantidadPlaneada { get; set; }
    public int CantidadOKTotal { get; set; }
    public int CantidadSospechosaTotal { get; set; }
    public int CantidadScrapTotal { get; set; }

    public int? ObjetivoHora { get; set; }
    public decimal? Ciclo { get; set; }
    public int? Cavidades { get; set; }

    public ProduccionConfiguracionCorridaVm? ConfiguracionActual { get; set; }
    public long? UltimoContadorMaquina { get; set; }
    public int BonusOperadorActual { get; set; }

    public int? CavidadesEnUso =>
        ConfiguracionActual?.CavidadesUsadas ??
        Cavidades;

    public decimal? CicloEnUso =>
        ConfiguracionActual?.TiempoCicloSegundos ??
        Ciclo;

    public int? ObjetivoHoraEnUso =>
        ConfiguracionActual != null
            ? ConfiguracionActual.ObjetivoHoraOperativo
            : ObjetivoHora;

    public bool TieneConfiguracionReal =>
        ConfiguracionActual?.EstaVigente == true;


    public ProduccionTiempoExtraVm? TiempoExtraActivo { get; set; }

    public List<ProduccionTiempoExtraVm> HistorialTiempoExtra { get; set; } = new();

    public bool PuedeIniciarTiempoExtra { get; set; }

    public List<ProduccionCatalogoDefectoVm> CatalogoDefectos { get; set; } = new();
    public DateTime FechaHoraServidor { get; set; } = DateTime.Now;

    public bool TieneTiempoExtraActivo =>
        TiempoExtraActivo?.EstaEnCurso == true;

    public bool TiempoExtraRequiereCorte =>
        TiempoExtraActivo?.RequiereCorte60 == true;

    public DateTime? ProximoCorteTiempoExtra =>
        TiempoExtraActivo?.FechaHoraProximoCorte;

    public int EstatusID { get; set; }

    public string EstatusNombre =>
        ProduccionEstatus.Nombre(EstatusID);

    public string EstatusClase =>
        ProduccionEstatus.ClaseBadge(EstatusID);

    public List<ProduccionCapturaHoraFilaVm> HorasCaptura { get; set; } = new();

    public DateTime FechaProduccion { get; set; } = DateTime.Today;
    public TimeSpan HoraInicioSugerida { get; set; }
    public TimeSpan HoraFinSugerida { get; set; }

    public bool TieneParoAbierto { get; set; }
    public int? ParoAbiertoID { get; set; }

    public DateTime? FechaLiberacionMaquina { get; set; }

    public bool MaquinaLiberada =>
        FechaLiberacionMaquina.HasValue;

    public List<ProduccionHistorialTurnoVm> HistorialTurnos { get; set; } = new();

    public List<ProduccionCambioTurnoHistorialVm> HistorialCambiosTurno { get; set; } = new();

    public List<SelectListItem> MotivosParo { get; set; } = new();

    public string RangoHoraSugerido =>
        $"{HoraInicioSugerida:hh\\:mm} - {HoraFinSugerida:hh\\:mm}";

    public int Pendiente
    {
        get
        {
            if (!CantidadPlaneada.HasValue)
                return 0;

            return Math.Max(
                0,
                CantidadPlaneada.Value -
                CantidadOKTotal);
        }
    }
}
public static class ProduccionCambioTurnoEstado
{
    public const string PendienteRecepcion = "PENDIENTE_RECEPCION";
    public const string Recibido = "RECIBIDO";
    public const string Cancelado = "CANCELADO";
}
public static class ProduccionCambioTurnoOrigen
{
    public const string Escala = "ESCALA";
    public const string Tecnico = "TECNICO";
    public const string Manual = "MANUAL";
}

public sealed class ProduccionCambioTurnoSugerenciaVm
{
    public int CambioTurnoSugerenciaID { get; set; }
    public int EjecucionProduccionID { get; set; }
    public int ProgramaProduccionID { get; set; }
    public int OperadorSugeridoID { get; set; }
    public string OperadorSugeridoNombre { get; set; } = string.Empty;
    public int UsuarioTecnicoID { get; set; }
    public string TecnicoNombre { get; set; } = string.Empty;
    public DateTime FechaSugerencia { get; set; }
    public string? Observaciones { get; set; }
    public bool Utilizada { get; set; }
    public bool Activo { get; set; } = true;
    public int? UsuarioModificacionID { get; set; }
    public DateTime? FechaModificacion { get; set; }
    public string? TurnoNombre { get; set; }
    public int? NivelPolivalencia { get; set; }
    public bool EnEscala { get; set; }
    public bool EstaVigente => Activo && !Utilizada;
    public string TextoOrigen => "Sugerencia del técnico";
    public string TextoOperador => string.IsNullOrWhiteSpace(OperadorSugeridoNombre) ? $"Operador {OperadorSugeridoID}" : OperadorSugeridoNombre;
    public string TextoFecha => FechaSugerencia.ToString("dd/MM/yyyy HH:mm");
}

public sealed class ProduccionCambioTurnoSugerenciaPostVm
{
    public int EjecucionProduccionID { get; set; }
    public int OperadorSugeridoID { get; set; }
    public string? Observaciones { get; set; }
}

public sealed class ProduccionCambioTurnoTecnicoVm
{
    public int EjecucionProduccionID { get; set; }
    public int ProgramaProduccionID { get; set; }
    public int? MaquinaID { get; set; }
    public string? MaquinaCodigo { get; set; }
    public string? MaquinaNombre { get; set; }
    public int? ParteID { get; set; }
    public string? NumeroParte { get; set; }
    public string? ReferenciaSAP { get; set; }
    public int? OperadorActualID { get; set; }
    public string? OperadorActualNombre { get; set; }
    public ProduccionCambioTurnoSugerenciaVm? SugerenciaActual { get; set; }
    public List<ProduccionCambioTurnoCandidatoVm> Operadores { get; set; } = new();
    public bool TieneMatrizPolivalencia { get; set; }
    public bool EscalaEncontrada { get; set; }
    public string? EscalaFolio { get; set; }
    public bool TieneSugerencia => SugerenciaActual?.EstaVigente == true;
    public string TextoMaquina => string.IsNullOrWhiteSpace(MaquinaCodigo) ? "Sin máquina" : string.IsNullOrWhiteSpace(MaquinaNombre) ? MaquinaCodigo : $"{MaquinaCodigo} - {MaquinaNombre}";
    public string TextoParte => !string.IsNullOrWhiteSpace(ReferenciaSAP) ? ReferenciaSAP : !string.IsNullOrWhiteSpace(NumeroParte) ? NumeroParte : "Sin parte";
    public string TextoOperadorActual => string.IsNullOrWhiteSpace(OperadorActualNombre) ? "Sin operador" : OperadorActualNombre;
}
public sealed class ProduccionCambioTurnoCandidatoVm
{
    public int PersonaID { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public int? Nivel { get; set; }
    public bool EnEscala { get; set; }
    public int? TurnoID { get; set; }
    public string? TurnoNombre { get; set; }
    public TimeSpan? HoraInicioTurno { get; set; }
    public int? MinutosParaInicio { get; set; }
    public bool EsSugerido { get; set; }
    public string TextoNivel => Nivel.HasValue ? $"N{Nivel.Value}" : "Sin nivel";
}
public sealed class ProduccionCambioTurnoResumenVm
{
    public int EjecucionProduccionID { get; set; }
    public int ProgramaProduccionID { get; set; }
    public int? MaquinaID { get; set; }
    public string? MaquinaCodigo { get; set; }
    public string? MaquinaNombre { get; set; }
    public int? ParteID { get; set; }
    public string? NumeroParte { get; set; }
    public string? ReferenciaSAP { get; set; }
    public int OperadorSalienteID { get; set; }
    public string OperadorSalienteNombre { get; set; } = string.Empty;
    public int CantidadOK { get; set; }
    public int CantidadSospechosa { get; set; }
    public int CantidadScrap { get; set; }
    public int? UltimoRegistroHoraID { get; set; }
    public string? UltimaHoraTexto { get; set; }
    public int TotalCajas { get; set; }
    public int TotalCajasEntregadas { get; set; }
    public int TotalCajasPendientes { get; set; }
    public bool TieneMatrizPolivalencia { get; set; }
    public bool EscalaEncontrada { get; set; }
    public string? EscalaFolio { get; set; }
    public int? OperadorSugeridoID { get; set; }
    public string? OperadorSugeridoNombre { get; set; }
    public string? TurnoSugeridoNombre { get; set; }
    public bool SugeridoPorTecnico { get; set; }
    public int? CambioTurnoSugerenciaID { get; set; }
    public int? UsuarioTecnicoSugerenciaID { get; set; }
    public string? TecnicoSugerenciaNombre { get; set; }
    public DateTime? FechaSugerenciaTecnico { get; set; }
    public string? ObservacionesSugerenciaTecnico { get; set; }
    public bool PuedeEntregar { get; set; }
    public string? MotivoBloqueo { get; set; }
    public List<ProduccionCambioTurnoCandidatoVm> Operadores { get; set; } = new();
    public bool TieneOperadorSugerido => OperadorSugeridoID.HasValue && OperadorSugeridoID.Value > 0;
    public string OrigenSugerenciaTexto => SugeridoPorTecnico ? "Sugerido por técnico" : TieneOperadorSugerido ? "Sugerido por escala" : "Sin sugerencia";
}
public sealed class ProduccionCambioTurnoEntregaPostVm
{
    public int EjecucionProduccionID { get; set; }
    public int OperadorEntranteID { get; set; }
    public string? Observaciones { get; set; }
}
public sealed class ProduccionCambioTurnoRecepcionPostVm
{
    public int CambioTurnoID { get; set; }
    public string? ObservacionesRecepcion { get; set; }
}
public sealed class ProduccionCambioTurnoPendienteVm
{
    public int CambioTurnoID { get; set; }
    public int EjecucionProduccionID { get; set; }
    public int ProgramaProduccionID { get; set; }
    public int OperadorSalienteID { get; set; }
    public string OperadorSalienteNombre { get; set; } = string.Empty;
    public int OperadorEntranteID { get; set; }
    public string OperadorEntranteNombre { get; set; } = string.Empty;
    public DateTime FechaEntrega { get; set; }
    public int CantidadOK { get; set; }
    public int CantidadSospechosa { get; set; }
    public int CantidadScrap { get; set; }
    public int TotalCajas { get; set; }
    public int TotalCajasEntregadas { get; set; }
    public string? Observaciones { get; set; }
    public string? MaquinaCodigo { get; set; }
    public string? ReferenciaSAP { get; set; }
    public string? NumeroParte { get; set; }
}

public sealed class ProduccionCapturaHoraFilaVm
{
    public int NumeroHora { get; set; }

    public DateTime FechaProduccion { get; set; }

    public TimeSpan HoraInicio { get; set; }
    public TimeSpan HoraFin { get; set; }

    public int CantidadOK { get; set; }
    public int CantidadSospechosa { get; set; }
    public int CantidadScrap { get; set; }

    public string? Observaciones { get; set; }

    public bool Capturada { get; set; }
    public bool Disponible { get; set; }
    public bool Vencida { get; set; }

    public int? RegistroHoraID { get; set; }

    public int? ObjetivoHora { get; set; }
    public int? ObjetivoBloque { get; set; }

    public int? OperadorID { get; set; }
    public string? OperadorNombre { get; set; }

    // =========================================================
    // CONTADOR / SEGMENTOS
    // =========================================================
    public int? PiezasCalculadasContador { get; set; }

    public decimal? MinutosProductivos { get; set; }

    public bool TieneCambioConfiguracion { get; set; }

    public bool TieneReinicioContador { get; set; }

    public List<ProduccionRegistroHoraSegmentoVm> Segmentos { get; set; } = new();

    // =========================================================
    // TIEMPO EXTRA
    // =========================================================
    public bool EsTiempoExtra { get; set; }

    public string? TipoBloque { get; set; }

    public int? TiempoExtraID { get; set; }

    public int? NumeroCorteTiempoExtra { get; set; }

    public bool EsCorteTiempoExtra =>
        EsTiempoExtra &&
        TiempoExtraID.HasValue &&
        NumeroCorteTiempoExtra.HasValue;

    public long? ContadorInicial =>
        Segmentos.Count > 0
            ? Segmentos
                .OrderBy(x => x.NumeroSegmento)
                .First()
                .ContadorInicial
            : null;

    public long? ContadorFinal =>
        Segmentos.Count > 0
            ? Segmentos
                .OrderByDescending(x => x.NumeroSegmento)
                .First()
                .ContadorFinal
            : null;

    public int? CavidadesUsadas =>
        Segmentos.Count == 1
            ? Segmentos[0].CavidadesUsadas
            : null;

    public decimal? TiempoCicloSegundos =>
        Segmentos.Count == 1
            ? Segmentos[0].TiempoCicloSegundos
            : null;

    public bool TieneMultiplesConfiguraciones =>
        Segmentos
            .Where(x => x.Activo)
            .Select(x => x.ConfiguracionCorridaID)
            .Distinct()
            .Count() > 1;

    public string RangoHora =>
        $"{HoraInicio:hh\\:mm} - {HoraFin:hh\\:mm}";

    public string EstadoTexto
    {
        get
        {
            if (Capturada)
                return EsCorteTiempoExtra
                    ? $"Tiempo extra #{NumeroCorteTiempoExtra} capturado"
                    : "Capturada";

            if (Vencida)
                return "Pendiente vencida";

            if (Disponible)
                return "Disponible";

            return "Próxima";
        }
    }

    public string EstadoClase
    {
        get
        {
            if (Capturada)
                return "bg-success";

            if (Vencida)
                return "bg-danger";

            if (Disponible)
                return "bg-warning text-dark";

            return "bg-secondary";
        }
    }

    public int DiferenciaObjetivo =>
        ObjetivoBloque.HasValue &&
        ObjetivoBloque.Value > 0
            ? CantidadOK -
              ObjetivoBloque.Value
            : 0;

    public decimal PorcentajeCumplimiento =>
        ObjetivoBloque.HasValue &&
        ObjetivoBloque.Value > 0
            ? Math.Round(
                (decimal)CantidadOK /
                ObjetivoBloque.Value *
                100m,
                1)
            : 0m;

    public bool CumplioObjetivo =>
        ObjetivoBloque.HasValue &&
        ObjetivoBloque.Value > 0 &&
        CantidadOK >=
        ObjetivoBloque.Value;

    public int PiezasFaltantes =>
        ObjetivoBloque.HasValue &&
        ObjetivoBloque.Value >
        CantidadOK
            ? ObjetivoBloque.Value -
              CantidadOK
            : 0;

    public int PiezasSobreObjetivo =>
        ObjetivoBloque.HasValue &&
        CantidadOK >
        ObjetivoBloque.Value
            ? CantidadOK -
              ObjetivoBloque.Value
            : 0;
}
public sealed class ProduccionRecepcionOFVm
{
    public int RecepcionOFID { get; set; }
    public int EjecucionProduccionID { get; set; }
    public int ProgramaProduccionID { get; set; }

    public string TipoRecepcion { get; set; } = "";
    public string? Codigo { get; set; }
    public string? Descripcion { get; set; }

    public string? Lote { get; set; }
    public string? NumeroUI { get; set; }
    public string? EtiquetaInicio { get; set; }
    public string? EtiquetaFin { get; set; }

    public decimal? Cantidad { get; set; }
    public string? Unidad { get; set; }

    public string? EntregadoPor { get; set; }
    public string? RecibidoPor { get; set; }

    public DateTime FechaRecepcion { get; set; }
    public string? Observaciones { get; set; }

    public long MovimientoID { get; set; }

    public string OrigenRegistro { get; set; } = string.Empty;

    public string TipoMovimiento { get; set; } = string.Empty;

    public int? SolicitudProduccionID { get; set; }

    public string? NumeroOF { get; set; }

    public string? ReferenciaOperacion { get; set; }

    public bool EsRegistroAutomaticoAlmacen =>
        string.Equals(
            OrigenRegistro,
            "ALMACEN",
            StringComparison.OrdinalIgnoreCase);

    public bool EsRegistroManualProduccion =>
        string.Equals(
            OrigenRegistro,
            "PRODUCCION",
            StringComparison.OrdinalIgnoreCase);

    public string TextoTipo
    {
        get
        {
            if (string.Equals(TipoRecepcion, "MP", StringComparison.OrdinalIgnoreCase))
                return "Materia prima";

            if (string.Equals(TipoRecepcion, "COMPONENTE", StringComparison.OrdinalIgnoreCase))
                return "Componente";

            if (string.Equals(TipoRecepcion, "EMBALAJE", StringComparison.OrdinalIgnoreCase))
                return "Embalaje";

            if (string.Equals(TipoRecepcion, "ETIQUETA", StringComparison.OrdinalIgnoreCase))
                return "Etiqueta";

            return TipoRecepcion;
        }
    }
}

public sealed class ProduccionHistorialEjecucionVm
{
    public int EjecucionProduccionID { get; set; }
    public int ProgramaProduccionID { get; set; }

    public int? SolicitudProduccionID { get; set; }

    public int? MaquinaID { get; set; }
    public string? MaquinaCodigo { get; set; }
    public string? MaquinaNombre { get; set; }

    public int? ParteID { get; set; }
    public string? NumeroParte { get; set; }
    public string? ReferenciaSAP { get; set; }
    public string? DescripcionParte { get; set; }

    public DateTime? FechaInicioReal { get; set; }
    public DateTime? FechaFinReal { get; set; }

    public int CantidadPlaneada { get; set; }
    public int CantidadOK { get; set; }
    public int CantidadSospechosa { get; set; }
    public int CantidadScrap { get; set; }

    public int ObjetivoAcumulado { get; set; }
    public int HorasCapturadas { get; set; }

    public decimal PorcentajeCumplimiento { get; set; }

    public int EstatusID { get; set; }

    public string? OperadorPrincipalNombre { get; set; }

    public int TotalCambiosTurno { get; set; }
    public int TotalParos { get; set; }

    // Se utiliza en historial del operador.
    public int? PersonaConsultaID { get; set; }
    public int CantidadOKOperador { get; set; }
    public int CantidadSospechosaOperador { get; set; }
    public int CantidadScrapOperador { get; set; }
    public int ObjetivoOperador { get; set; }
    public int HorasOperador { get; set; }
    public decimal PorcentajeCumplimientoOperador { get; set; }

    public string EstatusNombre =>
        ProduccionEstatus.Nombre(EstatusID);

    public string EstatusClase =>
        ProduccionEstatus.ClaseBadge(EstatusID);

    public string TextoMaquina =>
        string.IsNullOrWhiteSpace(MaquinaCodigo)
            ? "Sin máquina"
            : string.IsNullOrWhiteSpace(MaquinaNombre)
                ? MaquinaCodigo
                : $"{MaquinaCodigo} - {MaquinaNombre}";

    public string TextoParte =>
        !string.IsNullOrWhiteSpace(ReferenciaSAP)
            ? ReferenciaSAP
            : !string.IsNullOrWhiteSpace(NumeroParte)
                ? NumeroParte
                : $"Programa {ProgramaProduccionID}";

    public bool CumplioObjetivo =>
        ObjetivoAcumulado > 0 &&
        CantidadOK >= ObjetivoAcumulado;

    public bool CumplioObjetivoOperador =>
        ObjetivoOperador > 0 &&
        CantidadOKOperador >= ObjetivoOperador;
}

public sealed class ProduccionHistorialVm
{
    public string? Busqueda { get; set; }
    public DateTime? FechaDesde { get; set; }
    public DateTime? FechaHasta { get; set; }

    public bool EsVistaOperador { get; set; }

    public List<ProduccionHistorialEjecucionVm> Producciones { get; set; }
        = new();

    public int Total => Producciones.Count;

    public int Cerradas =>
        Producciones.Count(x =>
            x.EstatusID == ProduccionEstatus.Cerrado);

    public int Terminadas =>
        Producciones.Count(x =>
            x.EstatusID == ProduccionEstatus.Terminado ||
            x.EstatusID == ProduccionEstatus.TerminadoParcial);

    public int ListaCierre =>
        Producciones.Count(x =>
            x.EstatusID == ProduccionEstatus.ListaCierreDocumental);
}
public static class ProduccionChecklistEstatus
{
    public const int PendienteProduccion = 1;
    public const int CapturadoPorProduccion = 2;
    public const int PendienteValidacionCalidad = 3;
    public const int ValidadoPorCalidad = 4;
    public const int RechazadoRequiereAjuste = 5;
    public const int Cancelado = 99;

    public static string Nombre(int estatusId)
    {
        return estatusId switch
        {
            PendienteProduccion => "Pendiente producción",
            CapturadoPorProduccion => "Capturado por producción",
            PendienteValidacionCalidad => "Pendiente validación calidad",
            ValidadoPorCalidad => "Validado por calidad",
            RechazadoRequiereAjuste => "Rechazado / requiere ajuste",
            Cancelado => "Cancelado",
            _ => "Desconocido"
        };
    }

    public static string ClaseBadge(int estatusId)
    {
        return estatusId switch
        {
            PendienteProduccion => "bg-secondary",
            CapturadoPorProduccion => "bg-info text-dark",
            PendienteValidacionCalidad => "bg-warning text-dark",
            ValidadoPorCalidad => "bg-success",
            RechazadoRequiereAjuste => "bg-danger",
            Cancelado => "bg-dark",
            _ => "bg-secondary"
        };
    }

    public static bool PuedeEditarProduccion(int estatusId)
    {
        return estatusId == PendienteProduccion ||
               estatusId == CapturadoPorProduccion ||
               estatusId == RechazadoRequiereAjuste;
    }

    public static bool PuedeValidarCalidad(int estatusId)
    {
        return estatusId == PendienteValidacionCalidad;
    }

    public static bool EstaLiberadoParaSerie(int estatusId)
    {
        return estatusId == ValidadoPorCalidad;
    }
}

public sealed class ProduccionChecklistArranqueVm
{
    public int ChecklistArranqueID { get; set; }
    public int EjecucionProduccionID { get; set; }
    public int ProgramaProduccionID { get; set; }
    public int? SolicitudProduccionID { get; set; }
    public int? SolicitudProduccionDetalleID { get; set; }
    public int? ReleaseID { get; set; }
    public int? ReleaseDetalleID { get; set; }

    public DateTime FechaChecklist { get; set; } = DateTime.Now;
    public DateTime? FechaOperacion { get; set; }

    public int? MaquinaID { get; set; }
    public string? MaquinaCodigo { get; set; }
    public string? MaquinaNombre { get; set; }

    public int? MoldeID { get; set; }
    public string? MoldeCodigo { get; set; }

    public int? ParteID { get; set; }
    public string? NumeroParte { get; set; }
    public string? ReferenciaSAP { get; set; }
    public string? DescripcionParte { get; set; }

    public string CodigoFormato { get; set; } = string.Empty;
    public string? VersionFormato { get; set; }
    public string TipoChecklist { get; set; } = string.Empty;
    public string MomentoProceso { get; set; } = string.Empty;

    public int? TurnoID { get; set; }
    public string? TurnoNombre { get; set; }
    public int NumeroAplicacion { get; set; } = 1;
    public bool EsRecurrente { get; set; }
    public bool RequiereCambioMolde { get; set; }

    public int EstatusID { get; set; } = ProduccionChecklistEstatus.PendienteProduccion;

    public int? UsuarioProduccionID { get; set; }
    public DateTime? FechaCapturaProduccion { get; set; }
    public int? UsuarioCalidadID { get; set; }
    public DateTime? FechaValidacionCalidad { get; set; }

    public string? ObservacionesGenerales { get; set; }
    public string? ObservacionesCalidad { get; set; }

    public int? UsuarioCreacionID { get; set; }
    public DateTime FechaCreacion { get; set; }
    public int? UsuarioModificacionID { get; set; }
    public DateTime? FechaModificacion { get; set; }
    public bool Activo { get; set; } = true;

    public int? CalidadInspeccionID { get; set; }
    public string? CalidadEstado { get; set; }
    public string? CalidadMotivoDevolucion { get; set; }
    public DateTime? FechaNotificacionCalidad { get; set; }
    public DateTime? FechaLiberacionCalidad { get; set; }

    public int? TecnicoEntregaPersonaID { get; set; }
    public string? TecnicoEntregaNombre { get; set; }
    public DateTime? FechaEntregaTurno { get; set; }

    public int? TecnicoRecibePersonaID { get; set; }
    public string? TecnicoRecibeNombre { get; set; }
    public DateTime? FechaRecepcionTurno { get; set; }

    public List<ProduccionMonitoreoPerifericoProblemaVm> ProblemasPerifericos { get; set; } = new();

    public List<ProduccionChecklistSeccionVm> Secciones { get; set; } = new();

    public bool TieneProcesoCalidad => CalidadInspeccionID.HasValue;
    public bool ProduccionLiberadaPorCalidad => string.Equals(CalidadEstado, CalidadEstados.ProduccionLiberada, StringComparison.OrdinalIgnoreCase) || string.Equals(CalidadEstado, CalidadEstados.MonitoreoActivo, StringComparison.OrdinalIgnoreCase);

    public string EstatusNombre => ProduccionChecklistEstatus.Nombre(EstatusID);
    public string EstatusClase => ProduccionChecklistEstatus.ClaseBadge(EstatusID);
    public bool PuedeEditarProduccion => ProduccionChecklistEstatus.PuedeEditarProduccion(EstatusID);
    public bool PuedeValidarCalidad => ProduccionChecklistEstatus.PuedeValidarCalidad(EstatusID);
    public bool EstaLiberadoParaSerie => ProduccionChecklistEstatus.EstaLiberadoParaSerie(EstatusID);

    public int TotalPreguntas => Secciones.Sum(x => x.Preguntas.Count(p => p.Activo));
    public int TotalRespondidas => Secciones.Sum(x => x.Preguntas.Count(p => p.Activo && p.Confirmado));
    public int TotalPendientes => Math.Max(0, TotalPreguntas - TotalRespondidas);
    public int TotalOK => Secciones.Sum(x => x.Preguntas.Count(p => p.Activo && p.Confirmado && p.Resultado == "OK"));
    public int TotalNOK => Secciones.Sum(x => x.Preguntas.Count(p => p.Activo && p.Confirmado && p.Resultado == "NOK"));
    public int TotalNA => Secciones.Sum(x => x.Preguntas.Count(p => p.Activo && p.Confirmado && p.Resultado == "NA"));
    public bool TieneNOK => TotalNOK > 0;
    public bool EstaCompletoProduccion => TotalPreguntas > 0 && TotalPendientes == 0;

    public string TextoMaquina => string.IsNullOrWhiteSpace(MaquinaCodigo) ? "Sin máquina" : $"{MaquinaCodigo} - {MaquinaNombre}";
    public string TextoFormato => string.IsNullOrWhiteSpace(VersionFormato) ? CodigoFormato : $"{CodigoFormato} {VersionFormato}";
    public string TextoTurno => TurnoID.HasValue ? $"{TurnoID} - {TurnoNombre}" : "No aplica";

    public string TextoParte
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(ReferenciaSAP)) return ReferenciaSAP;
            if (!string.IsNullOrWhiteSpace(NumeroParte)) return NumeroParte;
            return "Sin parte";
        }
    }

  
    public string TituloChecklist
    {
        get
        {
            return TipoChecklist switch
            {
                "CAMBIO_MOLDE" => "Desmontaje y montaje de molde",
                "MONITOREO_PARAMETROS" => "Monitoreo de parámetros",
                "MONITOREO_PERIFERICOS" => "Monitoreo de periféricos",
                _ => "Checklist de arranque y liberación de máquina"
            };
        }
    }
}

public sealed class ProduccionHistorialTurnoVm
{
    public int NumeroTurno { get; set; }
    public string TurnoNombre { get; set; } = string.Empty;
    public int OperadorID { get; set; }
    public string OperadorNombre { get; set; } = string.Empty;
    public DateTime FechaInicio { get; set; }
    public DateTime? FechaFin { get; set; }
    public int CantidadOK { get; set; }
    public int CantidadSospechosa { get; set; }
    public int CantidadScrap { get; set; }
    public int ObjetivoTotal { get; set; }
    public decimal PorcentajeCumplimiento { get; set; }
    public bool CumplioObjetivo { get; set; }
    public List<ProduccionCapturaHoraFilaVm> Horas { get; set; } = new();
}
public sealed class ProduccionCambioTurnoHistorialVm
{
    public int CambioTurnoID { get; set; }
    public int OperadorSalienteID { get; set; }
    public string OperadorSalienteNombre { get; set; } = string.Empty;
    public int OperadorEntranteID { get; set; }
    public string OperadorEntranteNombre { get; set; } = string.Empty;
    public string? TurnoSalienteNombre { get; set; }
    public string? TurnoEntranteNombre { get; set; }
    public DateTime FechaEntrega { get; set; }
    public DateTime? FechaRecepcion { get; set; }
    public string Estado { get; set; } = string.Empty;
    public string? OrigenOperadorEntrante { get; set; }
    public int CantidadOK { get; set; }
    public int CantidadSospechosa { get; set; }
    public int CantidadScrap { get; set; }
    public int TotalCajas { get; set; }
    public int TotalCajasEntregadas { get; set; }
    public int TotalCajasPendientes { get; set; }
    public string? ObservacionesEntrega { get; set; }
    public string? ObservacionesRecepcion { get; set; }
}
public class ProduccionMonitoreoTurnoAvisoVm
{
    public int ChecklistArranqueID { get; set; }
    public int EjecucionProduccionID { get; set; }
    public int TurnoID { get; set; }
    public string TurnoNombre { get; set; } = string.Empty;
    public DateTime FechaOperacion { get; set; }
    public string? MaquinaCodigo { get; set; }
    public string? MaquinaNombre { get; set; }
    public int EstatusID { get; set; }
    public int TotalPreguntas { get; set; }
    public int TotalConfirmadas { get; set; }
    public bool EntregaTurnoRegistrada { get; set; }

    public bool ChecklistPendiente =>
        TotalPreguntas == 0 ||
        TotalConfirmadas < TotalPreguntas ||
        !EntregaTurnoRegistrada;

    public decimal PorcentajeAvance =>
        TotalPreguntas > 0
            ? Math.Round((decimal)TotalConfirmadas * 100m / TotalPreguntas, 2)
            : 0m;
}

public sealed class ProduccionChecklistSeccionVm
{
    public string Seccion { get; set; } = string.Empty;
    public int OrdenSeccion { get; set; }
    public string? ResponsableSugerido { get; set; }
    public List<ProduccionChecklistPreguntaVm> Preguntas { get; set; } = new();

    public bool EsSeccionCalidad => Preguntas.Any(x => x.EsPreguntaCalidad) || Seccion.Contains("CALIDAD", StringComparison.OrdinalIgnoreCase) || (ResponsableSugerido?.Contains("CALIDAD", StringComparison.OrdinalIgnoreCase) ?? false);

    public int TotalPreguntas => Preguntas.Count(x => x.Activo);
    public int TotalRespondidas => Preguntas.Count(x => x.Activo && x.Confirmado);
    public int TotalPendientes => Math.Max(0, TotalPreguntas - TotalRespondidas);
    public int TotalNOK => Preguntas.Count(x => x.Activo && x.Confirmado && x.Resultado == "NOK");
    public int TotalNA => Preguntas.Count(x => x.Activo && x.Confirmado && x.Resultado == "NA");
    public bool EstaCompleta => TotalPreguntas > 0 && TotalPendientes == 0;
}

public sealed class ProduccionChecklistPreguntaVm
{
    public int ChecklistArranqueDetalleID { get; set; }
    public int ChecklistArranqueID { get; set; }
    public int PreguntaID { get; set; }

    public string CodigoFormato { get; set; } = string.Empty;
    public string? VersionFormato { get; set; }
    public string TipoChecklist { get; set; } = string.Empty;
    public string MomentoProceso { get; set; } = string.Empty;
    public string TipoRespuesta { get; set; } = "ESTADO";
    public string EstadoPredeterminado { get; set; } = "OK";

    public string Seccion { get; set; } = string.Empty;
    public int OrdenSeccion { get; set; }
    public int OrdenPregunta { get; set; }
    public string TextoPregunta { get; set; } = string.Empty;

    public string? ResponsableSugerido { get; set; }
    public string? GrupoResponsable { get; set; }

    public bool RequiereObservacionSiNOK { get; set; } = true;
    public bool RequiereObservacionSiNA { get; set; } = true;
    public bool EsPreguntaCalidad { get; set; }
    public bool EsRecurrente { get; set; }

    public string? Resultado { get; set; }
    public string? Observaciones { get; set; }
    public bool Confirmado { get; set; }

    public string? ValorCapturado { get; set; }
    public string? Unidad { get; set; }
    public string? Especificacion { get; set; }
    public string? Tolerancia { get; set; }

    public int? UsuarioRespuestaID { get; set; }
    public DateTime? FechaRespuesta { get; set; }
    public bool Activo { get; set; } = true;

    public bool EsOK => Confirmado && Resultado == "OK";
    public bool EsNOK => Confirmado && Resultado == "NOK";
    public bool EsNA => Confirmado && Resultado == "NA";
    public bool RequiereCapturarValor => TipoRespuesta == "NUMERICO" || TipoRespuesta == "ESTADO_Y_VALOR";
    public bool RequiereObservacion => Confirmado && ((Resultado == "NOK" && RequiereObservacionSiNOK) || (Resultado == "NA" && RequiereObservacionSiNA));
    public bool TieneErrorCaptura => !Confirmado || (RequiereObservacion && string.IsNullOrWhiteSpace(Observaciones)) || (RequiereCapturarValor && string.IsNullOrWhiteSpace(ValorCapturado));

    public string ResultadoTexto
    {
        get
        {
            if (!Confirmado) return "Pendiente";
            if (Resultado == "OK") return "OK";
            if (Resultado == "NOK") return "NOK";
            if (Resultado == "NA") return "N/A";
            return "Pendiente";
        }
    }

    public string ResultadoClase
    {
        get
        {
            if (!Confirmado) return "bg-warning text-dark";
            if (Resultado == "OK") return "bg-success";
            if (Resultado == "NOK") return "bg-danger";
            if (Resultado == "NA") return "bg-secondary";
            return "bg-light text-dark border";
        }
    }
}

public sealed class ProduccionChecklistGuardarVm
{
    public int ChecklistArranqueID { get; set; }

    public int EjecucionProduccionID { get; set; }

    public string? ObservacionesGenerales { get; set; }

    public List<ProduccionChecklistRespuestaPostVm> Respuestas { get; set; } = new();

    public bool EnviarACalidad { get; set; }

    public bool GuardarBorrador { get; set; }
}

public sealed class ProduccionChecklistRespuestaPostVm
{
    public int ChecklistArranqueDetalleID { get; set; }
    public int PreguntaID { get; set; }
    public string? Resultado { get; set; }
    public string? Observaciones { get; set; }
    public bool Confirmado { get; set; }
    public string? ValorCapturado { get; set; }
}

public sealed class ProduccionChecklistValidacionCalidadVm
{
    public int ChecklistArranqueID { get; set; }

    public bool Aprobado { get; set; }

    public string? ObservacionesCalidad { get; set; }
}

public sealed class ProduccionChecklistResumenVm
{
    public int ChecklistArranqueID { get; set; }
    public int EjecucionProduccionID { get; set; }
    public int ProgramaProduccionID { get; set; }

    public string? MaquinaCodigo { get; set; }
    public string? MaquinaNombre { get; set; }

    public string? ReferenciaSAP { get; set; }
    public string? NumeroParte { get; set; }
    public string? DescripcionParte { get; set; }

    public string CodigoFormato { get; set; } = "GQ-F-PR01-06";
    public string? VersionFormato { get; set; } = "Ver.10";

    public int EstatusID { get; set; }
    public DateTime FechaChecklist { get; set; }

    public int TotalPreguntas { get; set; }
    public int TotalRespondidas { get; set; }
    public int TotalNOK { get; set; }

    public string EstatusNombre => ProduccionChecklistEstatus.Nombre(EstatusID);
    public string EstatusClase => ProduccionChecklistEstatus.ClaseBadge(EstatusID);

    public string TextoParte
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(ReferenciaSAP))
                return ReferenciaSAP;

            if (!string.IsNullOrWhiteSpace(NumeroParte))
                return NumeroParte;

            return "Sin parte";
        }
    }

    public string TextoMaquina =>
        string.IsNullOrWhiteSpace(MaquinaCodigo)
            ? "Sin máquina"
            : $"{MaquinaCodigo} - {MaquinaNombre}";
}




public sealed class ProduccionCalidadResumenVm
{
    public int InspeccionID { get; set; }
    public int EjecucionProduccionID { get; set; }
    public int ChecklistArranqueID { get; set; }

    public string Estado { get; set; } = string.Empty;
    public string? ResultadoCalidad { get; set; }
    public string? Etiqueta { get; set; }
    public string? MotivoDevolucion { get; set; }

    public DateTime? FechaNotificacionCalidad { get; set; }
    public DateTime? FechaAutorizacionPrearranque { get; set; }
    public DateTime? FechaLiberacionProduccion { get; set; }

    public bool ConfiguracionInvalidada { get; set; }
    public bool RequiereReliberacion { get; set; }
    public bool Liberado { get; set; }

    public int TotalMonitoreos { get; set; }
    public int MonitoreosPendientes { get; set; }
    public int MonitoreosVencidos { get; set; }
    public int MonitoreosConformes { get; set; }
    public int MonitoreosConHallazgo { get; set; }
    public int DisposicionesPendientes { get; set; }
    public DateTime? ProximoMonitoreo { get; set; }

    public bool TieneMonitoreoHorario => TotalMonitoreos > 0;

    public bool PuedeIniciarSerie =>
        Liberado &&
        !ConfiguracionInvalidada &&
        !RequiereReliberacion &&
        (string.Equals(Estado, CalidadEstados.ProduccionLiberada, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(Estado, CalidadEstados.MonitoreoActivo, StringComparison.OrdinalIgnoreCase));

    public string EstadoTexto => Estado switch
    {
        CalidadEstados.PendientePrearranque => "Pendiente de prearranque",
        CalidadEstados.DevueltoPrearranque => "Devuelto a Produccion",
        CalidadEstados.ArranqueAutorizado => "Arranque controlado autorizado",
        CalidadEstados.PendientePrimerasPiezas => "Primeras piezas en revision",
        CalidadEstados.AjustesSolicitados => "Ajustes solicitados",
        CalidadEstados.ProduccionLiberada => "Produccion liberada",
        CalidadEstados.MonitoreoActivo => "Monitoreo horario activo",
        CalidadEstados.PendienteReliberacion => "Pendiente de reliberacion",
        _ => string.IsNullOrWhiteSpace(Estado) ? "Sin proceso de Calidad" : Estado.Replace("_", " ")
    };

    public string ClaseBadge => Estado switch
    {
        CalidadEstados.ProduccionLiberada => "bg-success",
        CalidadEstados.MonitoreoActivo => "bg-success",
        CalidadEstados.DevueltoPrearranque => "bg-danger",
        CalidadEstados.AjustesSolicitados => "bg-danger",
        CalidadEstados.PendienteReliberacion => "bg-danger",
        CalidadEstados.ArranqueAutorizado => "bg-info text-dark",
        CalidadEstados.PendientePrimerasPiezas => "bg-info text-dark",
        _ => "bg-warning text-dark"
    };
}




public sealed class ProduccionProgramaDisponibleVm
{
    public int ProgramaProduccionID { get; set; }

    public int? SolicitudProduccionID { get; set; }
    public int? SolicitudProduccionDetalleID { get; set; }
    public int? ReleaseID { get; set; }
    public int? ReleaseDetalleID { get; set; }

    public string? FolioSolicitud { get; set; }
    public string? NumeroOFRecibida { get; set; }

    public int? MaquinaID { get; set; }
    public string? MaquinaCodigo { get; set; }
    public string? MaquinaNombre { get; set; }

    public int? ParteID { get; set; }
    public string? NumeroParte { get; set; }
    public string? ReferenciaSAP { get; set; }
    public string? DescripcionParte { get; set; }

    public int? MoldeID { get; set; }
    public string? MoldeCodigo { get; set; }

    public int? CantidadProgramada { get; set; }

    public DateTime? FechaInicioProgramada { get; set; }
    public DateTime? FechaFinProgramada { get; set; }

    public int EstatusID { get; set; }

    public int? OperadorSugeridoID { get; set; }
    public string? OperadorSugeridoNombre { get; set; }
    public string? TurnoSugeridoNombre { get; set; }
    public string? TurnoSugeridoColor { get; set; }
    public int? EscalaAsignacionID { get; set; }

    public string TituloPrograma
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(NumeroOFRecibida))
                return NumeroOFRecibida;

            if (!string.IsNullOrWhiteSpace(FolioSolicitud))
                return FolioSolicitud;

            return $"Programa {ProgramaProduccionID}";
        }
    }

    public string TextoParte
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(ReferenciaSAP))
                return ReferenciaSAP;

            if (!string.IsNullOrWhiteSpace(NumeroParte))
                return NumeroParte;

            return "Sin parte";
        }
    }

    public string TextoMaquina =>
        string.IsNullOrWhiteSpace(MaquinaCodigo)
            ? "Sin máquina"
            : $"{MaquinaCodigo} - {MaquinaNombre}";

    public bool PuedeIniciar =>
        MaquinaID.HasValue &&
        CantidadProgramada.HasValue &&
        CantidadProgramada.Value > 0;

    public int MinutosParaIniciar
    {
        get
        {
            if (!FechaInicioProgramada.HasValue)
                return 0;

            return (int)Math.Ceiling(
                (FechaInicioProgramada.Value - DateTime.Now)
                .TotalMinutes);
        }
    }

    public string TextoInicioProximo
    {
        get
        {
            if (!FechaInicioProgramada.HasValue)
                return "Sin horario programado";

            var minutos = MinutosParaIniciar;

            if (minutos < 0)
                return $"Inicio vencido hace {Math.Abs(minutos)} min";

            if (minutos == 0)
                return "Debe iniciar ahora";

            if (minutos == 1)
                return "Inicia en 1 minuto";

            return $"Inicia en {minutos} minutos";
        }
    }
}

public class ProduccionMonitoreoPerifericoProblemaVm
{
    public int MonitoreoPerifericoProblemaID { get; set; }
    public int ChecklistArranqueID { get; set; }
    public int EjecucionProduccionID { get; set; }
    public DateTime FechaOperacion { get; set; }
    public int TurnoID { get; set; }
    public string? TurnoNombre { get; set; }
    public int? MaquinaID { get; set; }
    public string? MaquinaCodigo { get; set; }
    public string? MaquinaNombre { get; set; }
    public string DescripcionFalla { get; set; } = string.Empty;
    public string? CausaRaiz { get; set; }
    public string? Acciones { get; set; }
    public bool Solucionado { get; set; }
    public DateTime? FechaSolucion { get; set; }
    public int? UsuarioSolucionID { get; set; }
    public string? UsuarioSolucionNombre { get; set; }
}

public class ProduccionMonitoreoPerifericoProblemaPostVm
{
    public int MonitoreoPerifericoProblemaID { get; set; }
    public int ChecklistArranqueID { get; set; }
    public string DescripcionFalla { get; set; } = string.Empty;
    public string? CausaRaiz { get; set; }
    public string? Acciones { get; set; }
    public bool Solucionado { get; set; }
}

public class ProduccionEntregaTurnoPerifericosPostVm
{
    public int ChecklistArranqueID { get; set; }
    public int? TecnicoRecibePersonaID { get; set; }
    public string? TecnicoRecibeNombre { get; set; }
}

public sealed class ProduccionCajaIncompletaDisponibleVm
{
    public long CajaProduccionID { get; set; }
    public int EjecucionProduccionID { get; set; }
    public int ProgramaProduccionID { get; set; }
    public int? SolicitudProduccionID { get; set; }
    public int? SolicitudProduccionDetalleID { get; set; }
    public int? ParteID { get; set; }
    public string? NumeroParte { get; set; }
    public string? ReferenciaSAP { get; set; }
    public string? DescripcionParte { get; set; }
    public string? FolioCaja { get; set; }
    public string? EtiquetaBlanca { get; set; }
    public int CantidadPiezas { get; set; }
    public int CapacidadObjetivoCaja { get; set; }
    public int CantidadPendienteCompletar { get; set; }
    public string EstadoProductoIncompleto { get; set; } = ProduccionProductoIncompletoEstado.Disponible;
    public int? EjecucionReservaID { get; set; }
    public int? ProgramaReservaID { get; set; }
    public int? SolicitudReservaID { get; set; }
    public int? SolicitudDetalleReservaID { get; set; }
    public DateTime FechaFormacion { get; set; }
    public DateTime? FechaReservaIncompleto { get; set; }
    public DateTime? FechaCompletadoIncompleto { get; set; }
    public string? UbicacionProductoIncompleto { get; set; }
    public DateTime? FechaIngresoProductoIncompleto { get; set; }
    public int? UsuarioIngresoProductoIncompletoID { get; set; }
    public string? UsuarioIngresoProductoIncompletoNombre { get; set; }
    public string? NumeroOFOrigen { get; set; }
    public string? NumeroOFDestino { get; set; }
    public string? MaquinaOrigenCodigo { get; set; }
    public string? MaquinaOrigenNombre { get; set; }
    public string? OperadorOrigenNombre { get; set; }
    public string? TurnoOrigenNombre { get; set; }
    public string? MaterialCodigo { get; set; }
    public string? MaterialDescripcion { get; set; }
    public string TextoEstado => ProduccionProductoIncompletoEstado.Nombre(EstadoProductoIncompleto);
    public bool EstaDisponible => string.Equals(EstadoProductoIncompleto, ProduccionProductoIncompletoEstado.Disponible, StringComparison.OrdinalIgnoreCase) && !EjecucionReservaID.HasValue;
    public bool EstaReservada => string.Equals(EstadoProductoIncompleto, ProduccionProductoIncompletoEstado.Reservada, StringComparison.OrdinalIgnoreCase);
    public bool EstaEnCompletado => string.Equals(EstadoProductoIncompleto, ProduccionProductoIncompletoEstado.EnCompletado, StringComparison.OrdinalIgnoreCase);
    public bool EstaCompleta => string.Equals(EstadoProductoIncompleto, ProduccionProductoIncompletoEstado.Completa, StringComparison.OrdinalIgnoreCase);
    public bool EstaCancelada => string.Equals(EstadoProductoIncompleto, ProduccionProductoIncompletoEstado.Cancelada, StringComparison.OrdinalIgnoreCase);
    public decimal PorcentajeLlenado => CapacidadObjetivoCaja > 0 ? Math.Round((decimal)CantidadPiezas * 100m / CapacidadObjetivoCaja, 1) : 0m;
    public string TextoCantidad => $"{CantidadPiezas:N0} / {CapacidadObjetivoCaja:N0}";
    public string TextoParte => !string.IsNullOrWhiteSpace(ReferenciaSAP) ? ReferenciaSAP : !string.IsNullOrWhiteSpace(NumeroParte) ? NumeroParte : "Sin parte";
    public string TextoUbicacion => string.IsNullOrWhiteSpace(UbicacionProductoIncompleto) ? "Sin ubicación" : UbicacionProductoIncompleto;
    public string TextoOFOrigen => string.IsNullOrWhiteSpace(NumeroOFOrigen) ? $"Programa {ProgramaProduccionID}" : NumeroOFOrigen;
    public string TextoOFDestino => string.IsNullOrWhiteSpace(NumeroOFDestino) ? "Sin asignar" : NumeroOFDestino;
    public int DiasEnAlmacen
    {
        get
        {
            var fechaBase = FechaIngresoProductoIncompleto ?? FechaFormacion;
            return Math.Max(0, (DateTime.Today - fechaBase.Date).Days);
        }
    }
    public bool TieneAntiguedadMedia => DiasEnAlmacen >= 7 && DiasEnAlmacen < 15 && !EstaCompleta && !EstaCancelada;
    public bool TieneAntiguedadAlta => DiasEnAlmacen >= 15 && !EstaCompleta && !EstaCancelada;
    public string TextoAntiguedad
    {
        get
        {
            if (DiasEnAlmacen <= 0) return "Ingresó hoy";
            if (DiasEnAlmacen == 1) return "1 día en resguardo";
            return $"{DiasEnAlmacen:N0} días en resguardo";
        }
    }
}

public sealed class ProduccionProductoIncompletoIndexVm
{
    public string? Busqueda { get; set; }
    public string? Estado { get; set; }
    public int? ParteID { get; set; }
    public string? Ubicacion { get; set; }
    public bool SoloConAntiguedad { get; set; }
    public List<ProduccionCajaIncompletaDisponibleVm> Cajas { get; set; } = new();
    public int TotalEtiquetas => Cajas.Count;
    public int Disponibles => Cajas.Count(x => x.EstaDisponible);
    public int Reservadas => Cajas.Count(x => x.EstaReservada);
    public int EnCompletado => Cajas.Count(x => x.EstaEnCompletado);
    public int Completas => Cajas.Count(x => x.EstaCompleta);
    public int SinUbicacion => Cajas.Count(x => string.IsNullOrWhiteSpace(x.UbicacionProductoIncompleto) && !x.EstaCompleta && !x.EstaCancelada);
    public int ConAntiguedad => Cajas.Count(x => x.TieneAntiguedadMedia || x.TieneAntiguedadAlta);
    public int ConAntiguedadAlta => Cajas.Count(x => x.TieneAntiguedadAlta);
    public int PiezasResguardadas => Cajas.Where(x => !x.EstaCompleta && !x.EstaCancelada).Sum(x => x.CantidadPiezas);
    public int PiezasPendientesCompletar => Cajas.Where(x => !x.EstaCompleta && !x.EstaCancelada).Sum(x => x.CantidadPendienteCompletar);
    public bool TieneCajas => Cajas.Count > 0;
}

public sealed class ProduccionProductoIncompletoDetalleVm
{
    public ProduccionCajaIncompletaDisponibleVm Caja { get; set; } = new();
    public List<ProduccionProductoIncompletoMovimientoVm> Movimientos { get; set; } = new();
    public int TotalPiezasOrigen => Movimientos.Where(x => string.Equals(x.TipoMovimiento, ProduccionCajaOrigenMovimiento.Origen, StringComparison.OrdinalIgnoreCase)).Sum(x => x.CantidadPiezas);
    public int TotalPiezasCompletado => Movimientos.Where(x => string.Equals(x.TipoMovimiento, ProduccionCajaOrigenMovimiento.Completado, StringComparison.OrdinalIgnoreCase)).Sum(x => x.CantidadPiezas);
    public int TotalTrazado => TotalPiezasOrigen + TotalPiezasCompletado;
}

public sealed class ProduccionProductoIncompletoMovimientoVm
{
    public long CajaOrigenDetalleID { get; set; }
    public long CajaProduccionID { get; set; }
    public string TipoMovimiento { get; set; } = string.Empty;
    public int EjecucionProduccionID { get; set; }
    public int ProgramaProduccionID { get; set; }
    public int? SolicitudProduccionID { get; set; }
    public int? SolicitudProduccionDetalleID { get; set; }
    public int? ReleaseID { get; set; }
    public int? ReleaseDetalleID { get; set; }
    public int CantidadPiezas { get; set; }
    public string? NumeroOF { get; set; }
    public string? NumeroParte { get; set; }
    public string? ReferenciaSAP { get; set; }
    public string? MaquinaCodigo { get; set; }
    public string? MaquinaNombre { get; set; }
    public string? OperadorNombre { get; set; }
    public DateTime FechaMovimiento { get; set; }
    public int? UsuarioID { get; set; }
    public string? UsuarioNombre { get; set; }
    public string? Observaciones { get; set; }
    public string TextoTipoMovimiento => string.Equals(TipoMovimiento, ProduccionCajaOrigenMovimiento.Origen, StringComparison.OrdinalIgnoreCase) ? "Origen" : string.Equals(TipoMovimiento, ProduccionCajaOrigenMovimiento.Completado, StringComparison.OrdinalIgnoreCase) ? "Completado" : TipoMovimiento;
    public string TextoParte => !string.IsNullOrWhiteSpace(ReferenciaSAP) ? ReferenciaSAP : !string.IsNullOrWhiteSpace(NumeroParte) ? NumeroParte : "Sin parte";
    public string TextoMaquina => string.IsNullOrWhiteSpace(MaquinaCodigo) ? "Sin máquina" : string.IsNullOrWhiteSpace(MaquinaNombre) ? MaquinaCodigo : $"{MaquinaCodigo} - {MaquinaNombre}";
}

public sealed class ProduccionProductoIncompletoUbicacionPostVm
{
    public long CajaProduccionID { get; set; }
    public string? UbicacionProductoIncompleto { get; set; }
}
public sealed class ProduccionReservarCajaIncompletaPostVm
{
    public long CajaProduccionID { get; set; }
    public int EjecucionProduccionID { get; set; }
}
public sealed class ProduccionCompletarCajaIncompletaPostVm
{
    public long CajaProduccionID { get; set; }
    public int EjecucionProduccionID { get; set; }
    public int CantidadPiezas { get; set; }
    public string? Observaciones { get; set; }
}

public sealed class ProduccionEtiquetasBlancasInicioVm
{
    public int ProgramaProduccionID { get; set; }
    public int ParteID { get; set; }
    public string? NumeroParte { get; set; }
    public string? ReferenciaSAP { get; set; }
    public int CantidadProgramada { get; set; }
    public List<ProduccionCajaIncompletaDisponibleVm> Etiquetas { get; set; } = new();
    public string TextoParte =>
        !string.IsNullOrWhiteSpace(ReferenciaSAP)
            ? ReferenciaSAP
            : !string.IsNullOrWhiteSpace(NumeroParte)
                ? NumeroParte
                : $"Parte {ParteID}";
}

public sealed class ProduccionOperadorCajasVm
{
    public int EjecucionProduccionID { get; set; }
    public int ProgramaProduccionID { get; set; }
    public int? SolicitudProduccionID { get; set; }

    public int? SolicitudProduccionDetalleID { get; set; }
    public string? FolioSolicitud { get; set; }
    public string? NumeroOFRecibida { get; set; }
    public string? ClienteNombre { get; set; }

    public string? MaquinaCodigo { get; set; }
    public string? MaquinaNombre { get; set; }

    public int? ParteID { get; set; }

    public string? NumeroParte { get; set; }
    public string? ReferenciaSAP { get; set; }
    public string? DescripcionParte { get; set; }

    public string? MoldeCodigo { get; set; }

    public string? MaterialCodigo { get; set; }
    public string? MaterialDescripcion { get; set; }

    public string? EmbalajeCodigo { get; set; }
    public string? EmbalajeDescripcion { get; set; }

    public int CantidadPlaneada { get; set; }
    public int CantidadOKTotal { get; set; }
    public int CantidadSospechosaTotal { get; set; }
    public int CantidadScrapTotal { get; set; }

    public int EstatusID { get; set; }
    public bool TieneParoAbierto { get; set; }

    public DateTime? FechaLiberacionMaquina { get; set; }

    public bool MaquinaLiberada =>
        FechaLiberacionMaquina.HasValue;

    public List<ProduccionOperadorCajaVm> Cajas { get; set; } = new();

    public List<ProduccionCajaIncompletaDisponibleVm> CajasIncompletasDisponibles { get; set; } = new();

    public int CantidadOKEnCajas { get; set; }
    public int CantidadSospechosaEnCajas { get; set; }
    public int CantidadScrapEnCajas { get; set; }
    public int CantidadRetencionEnCajas { get; set; }

    public int CantidadOKPlaneadaEmpacada => Math.Min(CantidadPlaneada, CantidadOKEnCajas);
    public int CantidadOKExcedenteProducida => Math.Max(0, CantidadOKTotal - CantidadPlaneada);
    public int CantidadOKExcedentePendienteResguardo
    {
        get
        {
            var incompletoPropio = Cajas.Where(x => x.EsProductoIncompleto && x.ActivoParaCalculo).Sum(x => x.CantidadPiezas);
            return Math.Max(0, CantidadOKTotal - CantidadPlaneada - incompletoPropio);
        }
    }
    public bool TieneExcedentePendiente => CantidadOKExcedentePendienteResguardo > 0;
    public bool TieneCajasIncompletasDisponibles => CajasIncompletasDisponibles.Count > 0;

    public int SiguienteNumeroCaja { get; set; } = 1;
    public bool PuedeFormarCaja { get; set; }

    public decimal? PiezasPorEmbalaje { get; set; }
    public decimal? CantidadEmbalajes { get; set; }

    public int PiezasPorCajaSugeridas
    {
        get
        {
            if (!PiezasPorEmbalaje.HasValue || PiezasPorEmbalaje.Value <= 0)
                return 0;

            return Convert.ToInt32(Math.Floor(PiezasPorEmbalaje.Value));
        }
    }

    public int CajasEsperadas
    {
        get
        {
            if (!CantidadEmbalajes.HasValue || CantidadEmbalajes.Value <= 0)
                return 0;

            return Convert.ToInt32(Math.Ceiling(CantidadEmbalajes.Value));
        }
    }

    public int CajasOKFormadas
    {
        get
        {
            return Cajas
                .Where(x => string.Equals(x.TipoCaja, "OK", StringComparison.OrdinalIgnoreCase))
                .Count();
        }
    }

    public int CajasPendientes
    {
        get
        {
            return Math.Max(0, CajasEsperadas - CajasOKFormadas);
        }
    }

    public int PiezasOKEmpacadas => CantidadOKEnCajas;

    public int PiezasPendientesEmpacar
    {
        get
        {
            return Math.Max(0, CantidadOKTotal - PiezasOKEmpacadas);
        }
    }

    public int PiezasPlaneadasPendientesDeCaja
    {
        get
        {
            return Math.Max(0, CantidadPlaneada - PiezasOKEmpacadas);
        }
    }

    public int CantidadSugeridaSiguienteCaja
    {
        get
        {
            if (PiezasPendientesEmpacar <= 0)
                return 0;

            if (PiezasPorCajaSugeridas <= 0)
                return PiezasPendientesEmpacar;

            return Math.Min(PiezasPendientesEmpacar, PiezasPorCajaSugeridas);
        }
    }

    public bool TieneConfiguracionEmbalaje
    {
        get
        {
            return PiezasPorEmbalaje.HasValue &&
                   PiezasPorEmbalaje.Value > 0 &&
                   CantidadEmbalajes.HasValue &&
                   CantidadEmbalajes.Value > 0;
        }
    }

    public sealed class ProduccionEscanearCajaPostVm
    {
        public int EjecucionProduccionID { get; set; }
        public string CodigoBarras { get; set; } = string.Empty;
    }
    public int CantidadOKDisponible =>
        Math.Max(0, CantidadOKTotal - CantidadOKEnCajas);

    public int CantidadSospechosaDisponible =>
        Math.Max(
            0,
            CantidadSospechosaTotal
            - CantidadSospechosaEnCajas
            - CantidadRetencionEnCajas);

    public int CantidadScrapDisponible =>
        Math.Max(0, CantidadScrapTotal - CantidadScrapEnCajas);

    public string EstatusNombre =>
        ProduccionEstatus.Nombre(EstatusID);

    public string TextoOF
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(NumeroOFRecibida))
                return NumeroOFRecibida;

            if (!string.IsNullOrWhiteSpace(FolioSolicitud))
                return FolioSolicitud;

            return $"Programa {ProgramaProduccionID}";
        }
    }

    public string TextoParte
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(ReferenciaSAP))
                return ReferenciaSAP;

            if (!string.IsNullOrWhiteSpace(NumeroParte))
                return NumeroParte;

            return "Sin parte";
        }
    }

    public string TextoMaquina =>
        string.IsNullOrWhiteSpace(MaquinaCodigo)
            ? "Sin máquina"
            : string.IsNullOrWhiteSpace(MaquinaNombre)
                ? MaquinaCodigo
                : $"{MaquinaCodigo} - {MaquinaNombre}";
}