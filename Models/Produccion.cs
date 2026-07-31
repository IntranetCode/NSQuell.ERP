using Microsoft.AspNetCore.Mvc.Rendering;

namespace ERP.NSQuell.Models;

public static class ProduccionEstatus
{
    public const int Pendiente = 1;
    public const int EnPreparacion = 2;
    public const int EnProduccion = 3;
    public const int Pausado = 4;
    public const int TerminadoParcial = 5;
    public const int Terminado = 6;
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

    public static bool EstaBloqueadoParaPlaneacion(int estatusId)
    {
        return estatusId == EnProduccion ||
               estatusId == Pausado ||
               estatusId == TerminadoParcial ||
               estatusId == Terminado ||
               estatusId == Cerrado;
    }
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

    public string EstatusNombre => ProduccionEstatus.Nombre(EstatusID);
    public string EstatusClase => ProduccionEstatus.ClaseBadge(EstatusID);

    public bool PuedeIniciar => ProduccionEstatus.PuedeIniciar(EstatusID);
    public bool PuedeRegistrarProduccion => ProduccionEstatus.PuedeRegistrarProduccion(EstatusID);
    public bool PuedePausar => ProduccionEstatus.PuedePausar(EstatusID);
    public bool PuedeTerminar => ProduccionEstatus.PuedeTerminar(EstatusID);

    public int CantidadTotalCapturada =>
        CantidadOKTotal + CantidadSospechosaTotal + CantidadScrapTotal;

    public int CantidadPendiente
    {
        get
        {
            if (!CantidadPlaneada.HasValue)
                return 0;

            return Math.Max(0, CantidadPlaneada.Value - CantidadOKTotal);
        }
    }

    public decimal PorcentajeAvance
    {
        get
        {
            if (!CantidadPlaneada.HasValue || CantidadPlaneada.Value <= 0)
                return 0;

            return Math.Round((decimal)CantidadOKTotal / CantidadPlaneada.Value * 100m, 2);
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

    public int TotalCapturado =>
        CantidadOK + CantidadSospechosa + CantidadScrap;

    public string RangoHora =>
        $"{HoraInicio:hh\\:mm} - {HoraFin:hh\\:mm}";

    public bool TieneCaptura =>
        CantidadOK > 0 ||
        CantidadSospechosa > 0 ||
        CantidadScrap > 0 ||
        !string.IsNullOrWhiteSpace(Observaciones);
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

    public List<ProduccionProgramaDisponibleVm> ProgramasDisponibles { get; set; } = new();
    public List<ProduccionEjecucionVm> Ejecuciones { get; set; } = new();

    public int TotalDisponibles => ProgramasDisponibles.Count;

    public int Total => Ejecuciones.Count;

    public int Pendientes =>
        Ejecuciones.Count(x => x.EstatusID == ProduccionEstatus.Pendiente);

    public int EnPreparacion =>
        Ejecuciones.Count(x => x.EstatusID == ProduccionEstatus.EnPreparacion);

    public int EnProduccion =>
        Ejecuciones.Count(x => x.EstatusID == ProduccionEstatus.EnProduccion);

    public int Pausados =>
        Ejecuciones.Count(x => x.EstatusID == ProduccionEstatus.Pausado);

    public int Terminados =>
        Ejecuciones.Count(x =>
            x.EstatusID == ProduccionEstatus.Terminado ||
            x.EstatusID == ProduccionEstatus.TerminadoParcial);
}

public sealed class ProduccionDetalleVm
{
    public ProduccionEjecucionVm Ejecucion { get; set; } = new();

    public List<ProduccionRegistroHoraVm> RegistrosHora { get; set; } = new();

    public List<ProduccionParoVm> Paros { get; set; } = new();

    public List<SelectListItem> MotivosParo { get; set; } = new();

    public int TotalOK =>
        RegistrosHora.Where(x => x.Activo).Sum(x => x.CantidadOK);

    public int TotalSospechoso =>
        RegistrosHora.Where(x => x.Activo).Sum(x => x.CantidadSospechosa);

    public int TotalScrap =>
        RegistrosHora.Where(x => x.Activo).Sum(x => x.CantidadScrap);

    public int TotalCapturado =>
        TotalOK + TotalSospechoso + TotalScrap;

    public bool TieneParoAbierto =>
        Paros.Any(x => x.Activo && x.EstaAbierto);

    public ProduccionParoVm? ParoAbierto =>
        Paros.FirstOrDefault(x => x.Activo && x.EstaAbierto);

    public ProduccionChecklistResumenVm? ChecklistResumen { get; set; }

    public ProduccionCalidadResumenVm? CalidadResumen { get; set; }

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

    public int CantidadOK { get; set; }
    public int CantidadSospechosa { get; set; }
    public int CantidadScrap { get; set; }

    public string? Observaciones { get; set; }
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

    public int EstatusID { get; set; }
    public string EstatusNombre => ProduccionEstatus.Nombre(EstatusID);
    public string EstatusClase => ProduccionEstatus.ClaseBadge(EstatusID);

    public DateTime FechaProduccion { get; set; } = DateTime.Today;
    public TimeSpan HoraInicioSugerida { get; set; }
    public TimeSpan HoraFinSugerida { get; set; }

    public bool TieneParoAbierto { get; set; }
    public int? ParoAbiertoID { get; set; }

    public List<SelectListItem> MotivosParo { get; set; } = new();

    public string RangoHoraSugerido =>
        $"{HoraInicioSugerida:hh\\:mm} - {HoraFinSugerida:hh\\:mm}";

    public int Pendiente
    {
        get
        {
            if (!CantidadPlaneada.HasValue)
                return 0;

            return Math.Max(0, CantidadPlaneada.Value - CantidadOKTotal);
        }
    }
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

    public int? MaquinaID { get; set; }
    public string? MaquinaCodigo { get; set; }
    public string? MaquinaNombre { get; set; }

    public int? MoldeID { get; set; }
    public string? MoldeCodigo { get; set; }

    public int? ParteID { get; set; }
    public string? NumeroParte { get; set; }
    public string? ReferenciaSAP { get; set; }
    public string? DescripcionParte { get; set; }

    public string CodigoFormato { get; set; } = "GQ-F-PR01-06";
    public string? VersionFormato { get; set; } = "Ver.10";

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

    // Estado del proceso relacionado en Calidad.
    public int? CalidadInspeccionID { get; set; }
    public string? CalidadEstado { get; set; }
    public string? CalidadMotivoDevolucion { get; set; }
    public DateTime? FechaNotificacionCalidad { get; set; }
    public DateTime? FechaLiberacionCalidad { get; set; }

    public bool TieneProcesoCalidad => CalidadInspeccionID.HasValue;
    public bool ProduccionLiberadaPorCalidad =>
        string.Equals(CalidadEstado, CalidadEstados.ProduccionLiberada, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(CalidadEstado, CalidadEstados.MonitoreoActivo, StringComparison.OrdinalIgnoreCase);

    public List<ProduccionChecklistSeccionVm> Secciones { get; set; } = new();

    public string EstatusNombre => ProduccionChecklistEstatus.Nombre(EstatusID);
    public string EstatusClase => ProduccionChecklistEstatus.ClaseBadge(EstatusID);

    public bool PuedeEditarProduccion =>
        ProduccionChecklistEstatus.PuedeEditarProduccion(EstatusID);

    public bool PuedeValidarCalidad =>
        ProduccionChecklistEstatus.PuedeValidarCalidad(EstatusID);

    public bool EstaLiberadoParaSerie =>
        ProduccionChecklistEstatus.EstaLiberadoParaSerie(EstatusID);

    public int TotalPreguntas =>
        Secciones.Sum(x => x.Preguntas.Count);

    public int TotalRespondidas =>
        Secciones.Sum(x => x.Preguntas.Count(p => !string.IsNullOrWhiteSpace(p.Resultado)));

    public int TotalOK =>
        Secciones.Sum(x => x.Preguntas.Count(p => p.Resultado == "OK"));

    public int TotalNOK =>
        Secciones.Sum(x => x.Preguntas.Count(p => p.Resultado == "NOK"));

    public int TotalNA =>
        Secciones.Sum(x => x.Preguntas.Count(p => p.Resultado == "NA"));

    public bool TieneNOK =>
        TotalNOK > 0;

    public bool EstaCompletoProduccion
    {
        get
        {
            var preguntasProduccion = Secciones
                .Where(x =>
                    !x.EsSeccionCalidad)
                .SelectMany(x => x.Preguntas)
                .Where(x => x.Activo)
                .ToList();

            if (!preguntasProduccion.Any())
                return false;

            return preguntasProduccion.All(x =>
                !string.IsNullOrWhiteSpace(x.Resultado));
        }
    }

    public string TextoMaquina =>
        string.IsNullOrWhiteSpace(MaquinaCodigo)
            ? "Sin máquina"
            : $"{MaquinaCodigo} - {MaquinaNombre}";

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

    public string TextoFormato =>
        string.IsNullOrWhiteSpace(VersionFormato)
            ? CodigoFormato
            : $"{CodigoFormato} {VersionFormato}";
}

public sealed class ProduccionChecklistSeccionVm
{
    public string Seccion { get; set; } = string.Empty;
    public int OrdenSeccion { get; set; }

    public string? ResponsableSugerido { get; set; }

    public List<ProduccionChecklistPreguntaVm> Preguntas { get; set; } = new();

    public bool EsSeccionCalidad =>
        Seccion.Contains("CALIDAD", StringComparison.OrdinalIgnoreCase) ||
        (ResponsableSugerido?.Contains("CALIDAD", StringComparison.OrdinalIgnoreCase) ?? false);

    public int TotalPreguntas =>
        Preguntas.Count;

    public int TotalRespondidas =>
        Preguntas.Count(x => !string.IsNullOrWhiteSpace(x.Resultado));

    public int TotalNOK =>
        Preguntas.Count(x => x.Resultado == "NOK");

    public bool EstaCompleta =>
        Preguntas.All(x => !string.IsNullOrWhiteSpace(x.Resultado));
}

public sealed class ProduccionChecklistPreguntaVm
{
    public int ChecklistArranqueDetalleID { get; set; }
    public int ChecklistArranqueID { get; set; }

    public int PreguntaID { get; set; }

    public string CodigoFormato { get; set; } = "GQ-F-PR01-06";
    public string? VersionFormato { get; set; } = "Ver.10";

    public string Seccion { get; set; } = string.Empty;
    public int OrdenSeccion { get; set; }
    public int OrdenPregunta { get; set; }

    public string TextoPregunta { get; set; } = string.Empty;

    public string? ResponsableSugerido { get; set; }

    public bool RequiereObservacionSiNOK { get; set; } = true;

    public string? Resultado { get; set; }

    public string? Observaciones { get; set; }

    public int? UsuarioRespuestaID { get; set; }
    public DateTime? FechaRespuesta { get; set; }

    public bool Activo { get; set; } = true;

    public bool EsOK => Resultado == "OK";
    public bool EsNOK => Resultado == "NOK";
    public bool EsNA => Resultado == "NA";

    public bool RequiereObservacion =>
        Resultado == "NOK" && RequiereObservacionSiNOK;

    public bool TieneErrorCaptura =>
        RequiereObservacion &&
        string.IsNullOrWhiteSpace(Observaciones);

    public string ResultadoTexto
    {
        get
        {
            if (Resultado == "OK") return "OK";
            if (Resultado == "NOK") return "NOK";
            if (Resultado == "NA") return "N/A";

            return "Sin respuesta";
        }
    }

    public string ResultadoClase
    {
        get
        {
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
}