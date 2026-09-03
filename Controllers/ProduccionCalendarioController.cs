using ERP.NSQuell.Models;
using ERP.NSQuell.Servicios.Produccion;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using ERP.NSQuell.Servicios.Planeacion;
using System.Globalization;

namespace ERP.NSQuell.Controllers
{
    [Route("Produccion/CalendarioOperativo")]
    public sealed class ProduccionCalendarioController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly AgendaOperativaService _agendaOperativaService;
        private readonly ILogger<ProduccionCalendarioController> _logger;
        private readonly IPlaneacionSecuenciaService _planeacionSecuenciaService;

        public ProduccionCalendarioController(
            IConfiguration configuration,
            AgendaOperativaService agendaOperativaService,
            ILogger<ProduccionCalendarioController> logger,
            IPlaneacionSecuenciaService planeacionSecuenciaService)
        {
            _configuration = configuration;
            _agendaOperativaService = agendaOperativaService;
            _logger = logger;
            _planeacionSecuenciaService = planeacionSecuenciaService;
        }

        private string ConnectionString =>
            _configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "No se encontró la cadena de conexión DefaultConnection.");

        private static class EstatusPrograma
        {
            public const int Programado = 1;
            public const int EnPreparacion = 2;
            public const int EnProduccion = 3;
            public const int Pausado = 4;
            public const int TerminadoParcial = 5;
            public const int Terminado = 6;
            public const int Cerrado = 9;
            public const int Cancelado = 99;
        }

        [HttpGet("")]
        [HttpGet("Index")]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> Index(string? vista, DateTime? fecha, DateTime? rangoInicio, DateTime? rangoFin)
        {
            if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");
            var usuarioId = ObtenerUsuarioID();
            if (usuarioId <= 0) return RedirectToAction("Login", "Login");
            var periodo = ResolverPeriodo(vista, fecha, rangoInicio, rangoFin);
            var ahora = DateTime.Now;
            try
            {
                await using var cn = new SqlConnection(ConnectionString);
                await cn.OpenAsync();
                var maquinas = await ObtenerMaquinasCalendarioAsync(periodo.Inicio, periodo.Fin, cn);
                var proyeccion = await _planeacionSecuenciaService.ProyectarInterrupcionesActivasAsync(ahora, trabajarDomingo: false, cn);
                AplicarProyeccionInterrupcionesCalendario(maquinas, proyeccion);
                var filtrosOperativos = new AgendaOperativaFiltroVm
                {
                    MaquinaID = null,
                    Busqueda = null,
                    Area = null,
                    Estado = null,
                    SoloAtencion = false,
                    SoloBloqueadas = false,
                    IncluirProduciendo = true,
                    VentanaHoras = 72
                };
                var agendaOperativa = await _agendaOperativaService.ObtenerAgendaAsync(filtrosOperativos, usuarioId, periodo.Inicio, periodo.Fin);
                var programasVisibles = maquinas.SelectMany(x => x.Bloques).Select(x => x.ProgramaProduccionID).Distinct().ToHashSet();
                var operativaPorPrograma = agendaOperativa.Items
                    .Where(x => programasVisibles.Contains(x.ProgramaProduccionID))
                    .GroupBy(x => x.ProgramaProduccionID)
                    .ToDictionary(x => x.Key, x => x.OrderBy(y => y.OrdenPrioridad).ThenByDescending(y => y.RequiereAtencionInmediata).First());
                var vm = new PlaneacionCalendarioMaquinasVm
                {
                    Vista = periodo.Vista,
                    InicioPeriodo = periodo.Inicio,
                    FinPeriodo = periodo.Fin,
                    FechaReferencia = fecha,
                    RangoInicio = periodo.RangoInicio,
                    RangoFin = periodo.RangoFin,
                    Ahora = ahora,
                    Maquinas = maquinas
                };
                ViewBag.ModoProduccion = true;
                ViewBag.EsCalendarioOperativo = true;
                ViewBag.OperativaPorPrograma = operativaPorPrograma;
                ViewBag.ResumenOperativo = agendaOperativa.Resumen;
                ViewBag.FechaConsultaOperativa = agendaOperativa.FechaConsulta;
                ViewBag.SolicitudesReprogramacion = new List<SolicitudReprogramacionCalendarioVm>();
                ViewBag.TotalSolicitudesReprogramacion = 0;
                ViewBag.HayInterrupcionesActivas = proyeccion.HayInterrupcionesActivas;
                ViewBag.TotalInterrupcionesActivas = proyeccion.TotalInterrupcionesActivas;
                ViewBag.FechaCalculoProyeccion = proyeccion.FechaCalculo;
                return View("~/Views/ProduccionCalendario/Index.cshtml", vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar el Calendario Operativo de Producción. UsuarioID: {UsuarioID}.", usuarioId);
                TempData["Error"] = "No fue posible cargar el Calendario Operativo de Producción: " + ex.Message;
                var vm = new PlaneacionCalendarioMaquinasVm
                {
                    Vista = periodo.Vista,
                    InicioPeriodo = periodo.Inicio,
                    FinPeriodo = periodo.Fin,
                    FechaReferencia = fecha,
                    RangoInicio = periodo.RangoInicio,
                    RangoFin = periodo.RangoFin,
                    Ahora = ahora,
                    Maquinas = new List<PlaneacionCalendarioMaquinaVm>()
                };
                ViewBag.ModoProduccion = true;
                ViewBag.EsCalendarioOperativo = true;
                ViewBag.OperativaPorPrograma = new Dictionary<int, AgendaOperativaItemVm>();
                ViewBag.ResumenOperativo = new AgendaOperativaResumenVm();
                ViewBag.FechaConsultaOperativa = ahora;
                ViewBag.SolicitudesReprogramacion = new List<SolicitudReprogramacionCalendarioVm>();
                ViewBag.TotalSolicitudesReprogramacion = 0;
                ViewBag.HayInterrupcionesActivas = false;
                ViewBag.TotalInterrupcionesActivas = 0;
                ViewBag.FechaCalculoProyeccion = ahora;
                return View("~/Views/ProduccionCalendario/Index.cshtml", vm);
            }
        }

        private static void AplicarProyeccionInterrupcionesCalendario(List<PlaneacionCalendarioMaquinaVm> maquinas, PlaneacionProyeccionInterrupcionesResultado proyeccion)
        {
            if (maquinas == null || maquinas.Count == 0 || proyeccion.Programas == null || proyeccion.Programas.Count == 0) return;
            var proyecciones = proyeccion.Programas.GroupBy(x => x.ProgramaProduccionID).ToDictionary(x => x.Key, x => x.First());
            foreach (var maquina in maquinas)
            {
                foreach (var bloque in maquina.Bloques)
                {
                    if (!proyecciones.TryGetValue(bloque.ProgramaProduccionID, out var programa)) continue;
                    bloque.InicioProyectado = programa.InicioProyectado;
                    bloque.FinProyectado = programa.FinProyectado;
                    bloque.EsProgramaRaizInterrupcion = programa.EsProgramaRaizInterrupcion;
                    bloque.ParoProyeccionID = programa.ParoID;
                    bloque.TipoInterrupcionProyectada = programa.TipoInterrupcion ?? string.Empty;
                    bloque.MotivoInterrupcionProyectada = programa.MotivoParo ?? string.Empty;
                    bloque.MinutosImpactoInterrupcion = programa.MinutosImpactoInterrupcion;
                    bloque.MinutosDesplazamientoProyectado = programa.MinutosDesplazamiento;
                }
            }
        }

        private async Task<List<PlaneacionCalendarioMaquinaVm>>
            ObtenerMaquinasCalendarioAsync(
                DateTime inicio,
                DateTime fin,
                SqlConnection cn)
        {
            var maquinas =
                new List<PlaneacionCalendarioMaquinaVm>();

            const string sqlMaquinas = @"
SELECT
    MaquinaID,
    Codigo,
    Nombre
FROM dbo.ERP_Maquinas
WHERE Activo=1
  AND UPPER(REPLACE(ISNULL(Codigo,N''),N' ',N''))<>N'1200T'
  AND UPPER(REPLACE(ISNULL(Nombre,N''),N' ',N'')) NOT LIKE N'%1200T%'
ORDER BY Codigo,Nombre;";

            await using (var cmd = new SqlCommand(sqlMaquinas, cn))
            await using (var rd = await cmd.ExecuteReaderAsync())
            {
                while (await rd.ReadAsync())
                {
                    maquinas.Add(
                        new PlaneacionCalendarioMaquinaVm
                        {
                            MaquinaID = Entero(rd, "MaquinaID"),
                            Codigo = Texto(rd, "Codigo") ?? "-",
                            Nombre = Texto(rd, "Nombre") ?? "-",
                            Carriles = 1,
                            Bloques =
                                new List<PlaneacionCalendarioBloqueVm>()
                        });
                }
            }

            const string sqlProgramas = @"
SELECT
    pp.ProgramaProduccionID,
    pp.MaquinaID,
    pp.MaquinaCodigo,
    pp.MaquinaNombre,

    pp.ParteID,
    pp.NumeroParte,
    pp.ReferenciaSAP,
    pp.DesignacionDescripcionSAP AS DescripcionParte,

    pp.MoldeID,
    pp.MoldeCodigo,

    pp.ReleaseDetalleID,
    pp.SolicitudProduccionID,
    pp.SolicitudProduccionDetalleID,

    pp.FechaInicioProgramada,

    ISNULL
    (
        pp.FechaFinProgramada,
        DATEADD
        (
            MINUTE,
            CAST
            (
                CEILING(ISNULL(pp.HorasProgramadas,1)*60)
                AS INT
            ),
            pp.FechaInicioProgramada
        )
    ) AS FechaFinProgramada,

    ISNULL(pp.HorasProgramadas,0) AS HorasProgramadas,
    pp.Cambio,
    pp.Arranque,

    ISNULL(pp.CantidadProgramada,0) AS CantidadProgramada,
    ISNULL(pp.CantidadProducida,0) AS CantidadProducida,
    ISNULL(pp.EstatusID,1) AS EstatusID,

    ISNULL(c.Nombre,r.ClienteNombre) AS ClienteNombre,
    ISNULL(NULLIF(r.FolioRelease,''),'Programa') AS FolioRelease,

    t.MaquinaPrincipalID,
    mp.Codigo AS MaquinaPrincipalCodigo,
    mp.Nombre AS MaquinaPrincipalNombre,

    t.MaquinaSustitutaID,
    ms.Codigo AS MaquinaSustitutaCodigo,
    ms.Nombre AS MaquinaSustitutaNombre,

    pe.EjecucionProduccionID,
    pe.EstatusID AS EstatusProduccionID,
    pe.OperadorID AS OperadorRealID,
    pe.OperadorNombre AS OperadorRealNombre,
    pe.FechaInicioReal,

    opPrincipal.PersonaID AS OperadorProgramadoID,
    opPrincipal.NombreCompleto AS OperadorProgramadoNombre,

    opAuxiliar.PersonaID AS OperadorAuxiliarProgramadoID,
    opAuxiliar.NombreCompleto AS OperadorAuxiliarProgramadoNombre,

    turno.EscalaAsignacionID,
    turno.TurnoProgramadoNombre,
    turno.TurnoProgramadoColor,

    ci.InspeccionID AS InspeccionCalidadID,
    ci.Estado AS EstadoCalidad,
    ISNULL(ci.ConfiguracionInvalidada,0)
        AS ConfiguracionCalidadInvalidada,
    ISNULL(ci.RequiereReliberacion,0)
        AS RequiereReliberacion

FROM dbo.Planeacion_ProgramaProduccion pp

LEFT JOIN dbo.Planeacion_ReleaseDetalle rd
    ON rd.ReleaseDetalleID=pp.ReleaseDetalleID

LEFT JOIN dbo.Planeacion_Releases r
    ON r.ReleaseID=rd.ReleaseID

LEFT JOIN dbo.ERP_Clientes c
    ON c.ClienteID=r.ClienteID

LEFT JOIN dbo.ERP_ParteDatosTecnicos t
    ON t.ParteID=pp.ParteID
   AND t.Activo=1

LEFT JOIN dbo.ERP_Maquinas mp
    ON mp.MaquinaID=t.MaquinaPrincipalID

LEFT JOIN dbo.ERP_Maquinas ms
    ON ms.MaquinaID=t.MaquinaSustitutaID

OUTER APPLY
(
    SELECT TOP(1)
        e.EjecucionProduccionID,
        e.EstatusID,
        e.OperadorID,
        e.OperadorNombre,
        e.FechaInicioReal
    FROM dbo.Produccion_Ejecucion e
    WHERE e.ProgramaProduccionID=pp.ProgramaProduccionID
      AND e.Activo=1
    ORDER BY e.EjecucionProduccionID DESC
) pe

OUTER APPLY
(
    SELECT TOP(1)
        po.PersonaID,
        LTRIM
        (
            RTRIM
            (
                ISNULL(p.Nombre,'')+' '+
                ISNULL(p.ApellidoPaterno,'')+' '+
                ISNULL(p.ApellidoMaterno,'')
            )
        ) AS NombreCompleto
    FROM dbo.Planeacion_ProgramaOperadores po
    LEFT JOIN dbo.Persona p
        ON p.PersonaID=po.PersonaID
    WHERE po.ProgramaProduccionID=pp.ProgramaProduccionID
      AND po.Activo=1
      AND UPPER(ISNULL(po.RolOperador,''))='PRINCIPAL'
    ORDER BY po.ProgramaOperadorID
) opPrincipal

OUTER APPLY
(
    SELECT TOP(1)
        po.PersonaID,
        LTRIM
        (
            RTRIM
            (
                ISNULL(p.Nombre,'')+' '+
                ISNULL(p.ApellidoPaterno,'')+' '+
                ISNULL(p.ApellidoMaterno,'')
            )
        ) AS NombreCompleto
    FROM dbo.Planeacion_ProgramaOperadores po
    LEFT JOIN dbo.Persona p
        ON p.PersonaID=po.PersonaID
    WHERE po.ProgramaProduccionID=pp.ProgramaProduccionID
      AND po.Activo=1
      AND UPPER(ISNULL(po.RolOperador,''))='AUXILIAR'
    ORDER BY po.ProgramaOperadorID
) opAuxiliar

OUTER APPLY
(
    SELECT TOP(1)
        a.AsignacionID AS EscalaAsignacionID,
        et.Nombre AS TurnoProgramadoNombre,
        et.Color AS TurnoProgramadoColor
    FROM dbo.RRHH_EscalaAsignaciones a
    INNER JOIN dbo.RRHH_EscalasPersonal esc
        ON esc.EscalaID=a.EscalaID
       AND esc.Activo=1
       AND esc.Estado=N'Publicada'
    INNER JOIN dbo.RRHH_EscalaTurnos et
        ON et.EscalaID=a.EscalaID
       AND et.EscalaTurnoID=a.EscalaTurnoID
    WHERE a.Activo=1
      AND a.PersonalID=opPrincipal.PersonaID
      AND a.MaquinaID=pp.MaquinaID
      AND CAST(pp.FechaInicioProgramada AS date)
            >=CAST(a.FechaInicio AS date)
      AND CAST(pp.FechaInicioProgramada AS date)
            <=CAST(a.FechaFin AS date)
      AND
      (
           ISNULL(et.EsFlexible,0)=1
        OR et.HoraInicio IS NULL
        OR et.HoraFin IS NULL

        OR
        (
            ISNULL(et.CruzaDiaSiguiente,0)=0
            AND CAST(pp.FechaInicioProgramada AS time)
                >=et.HoraInicio
            AND CAST(pp.FechaInicioProgramada AS time)
                <et.HoraFin
        )

        OR
        (
            ISNULL(et.CruzaDiaSiguiente,0)=1
            AND
            (
                CAST(pp.FechaInicioProgramada AS time)
                    >=et.HoraInicio
                OR CAST(pp.FechaInicioProgramada AS time)
                    <et.HoraFin
            )
        )
      )
    ORDER BY et.Orden,a.AsignacionID DESC
) turno

OUTER APPLY
(
    SELECT TOP(1)
        cins.InspeccionID,
        cins.Estado,
        cins.ConfiguracionInvalidada,
        cins.RequiereReliberacion
    FROM dbo.Calidad_Inspecciones cins
    WHERE cins.ProgramaProduccionID=
        pp.ProgramaProduccionID
    ORDER BY cins.InspeccionID DESC
) ci

WHERE pp.Activo=1
  AND pp.SolicitudProduccionID IS NOT NULL
  AND pp.SolicitudProduccionDetalleID IS NOT NULL
  AND ISNULL(pp.EstatusID,1)<>@EstatusCancelado
  AND pp.MaquinaID IS NOT NULL
  AND pp.FechaInicioProgramada IS NOT NULL
  AND pp.FechaInicioProgramada<@Fin

  AND ISNULL
  (
      pp.FechaFinProgramada,
      DATEADD
      (
          MINUTE,
          CAST
          (
              CEILING(ISNULL(pp.HorasProgramadas,1)*60)
              AS INT
          ),
          pp.FechaInicioProgramada
      )
  )>@Inicio

ORDER BY
    pp.MaquinaID,
    pp.FechaInicioProgramada,
    pp.SecuenciaMaquina,
    pp.ProgramaProduccionID;";

            var bloques =
                new List<PlaneacionCalendarioBloqueVm>();

            var ahora = DateTime.Now;

            await using (var cmd =
                new SqlCommand(sqlProgramas, cn))
            {
                cmd.Parameters.Add(
                    "@Inicio",
                    SqlDbType.DateTime).Value = inicio;

                cmd.Parameters.Add(
                    "@Fin",
                    SqlDbType.DateTime).Value = fin;

                cmd.Parameters.Add(
                    "@EstatusCancelado",
                    SqlDbType.Int).Value =
                    EstatusPrograma.Cancelado;

                await using var rd =
                    await cmd.ExecuteReaderAsync();

                while (await rd.ReadAsync())
                {
                    var programaProduccionId =
                        Entero(
                            rd,
                            "ProgramaProduccionID");

                    var estatusId =
                        Entero(
                            rd,
                            "EstatusID");

                    var estatusProduccionId =
                        NullableEntero(
                            rd,
                            "EstatusProduccionID");

                    var inicioPrograma =
                        Fecha(
                            rd,
                            "FechaInicioProgramada");

                    var finPrograma =
                        Fecha(
                            rd,
                            "FechaFinProgramada");

                    var cantidadProgramada =
                        Entero(
                            rd,
                            "CantidadProgramada");

                    var cantidadProducida =
                        Entero(
                            rd,
                            "CantidadProducida");

                    var ordinalFechaInicioReal =
                        rd.GetOrdinal(
                            "FechaInicioReal");

                    var fechaInicioReal =
                        rd.IsDBNull(ordinalFechaInicioReal)
                            ? (DateTime?)null
                            : Convert.ToDateTime(
                                rd.GetValue(
                                    ordinalFechaInicioReal));

                    var mostrarAlertaNoInicio = false;
                    var alertaNoInicioCritica = false;
                    var minutosAtrasoInicio = 0;
                    var textoAlertaNoInicio =
                        string.Empty;

                    var programaCerrado =
                        estatusId == EstatusPrograma.Terminado ||
                        estatusId == EstatusPrograma.Cerrado ||
                        estatusId == EstatusPrograma.Cancelado;

                    if (!programaCerrado &&
                        inicioPrograma <= ahora &&
                        !fechaInicioReal.HasValue)
                    {
                        var sigueSinIniciar =
                            !estatusProduccionId.HasValue ||
                            estatusProduccionId ==
                                EstatusPrograma.Programado ||
                            estatusProduccionId ==
                                EstatusPrograma.EnPreparacion;

                        if (sigueSinIniciar)
                        {
                            minutosAtrasoInicio =
                                Math.Max(
                                    1,
                                    (int)Math.Floor(
                                        (ahora -
                                         inicioPrograma)
                                        .TotalMinutes));

                            mostrarAlertaNoInicio = true;

                            alertaNoInicioCritica =
                                minutosAtrasoInicio >= 15;

                            textoAlertaNoInicio =
                                alertaNoInicioCritica
                                    ? $"Producción no inició. Atraso: {minutosAtrasoInicio} min."
                                    : $"Producción pendiente de iniciar. Atraso: {minutosAtrasoInicio} min.";
                        }
                    }

                    bloques.Add(
                        new PlaneacionCalendarioBloqueVm
                        {
                            ProgramaProduccionID =
                                programaProduccionId,

                            SolicitudProduccionID =
                                NullableEntero(
                                    rd,
                                    "SolicitudProduccionID"),

                            MaquinaID =
                                NullableEntero(
                                    rd,
                                    "MaquinaID") ?? 0,

                            MaquinaCodigo =
                                Texto(
                                    rd,
                                    "MaquinaCodigo")
                                ?? string.Empty,

                            ClienteNombre =
                                Texto(
                                    rd,
                                    "ClienteNombre")
                                ?? string.Empty,

                            NumeroParte =
                                Texto(
                                    rd,
                                    "NumeroParte")
                                ?? string.Empty,

                            ReferenciaSAP =
                                Texto(
                                    rd,
                                    "ReferenciaSAP")
                                ?? string.Empty,

                            Descripcion =
                                Texto(
                                    rd,
                                    "DescripcionParte")
                                ?? string.Empty,

                            MoldeCodigo =
                                Texto(
                                    rd,
                                    "MoldeCodigo")
                                ?? string.Empty,

                            CantidadProgramada =
                                cantidadProgramada,

                            CantidadProducida =
                                cantidadProducida,

                            Inicio =
                                inicioPrograma,

                            Fin =
                                finPrograma,

                            HorasProgramadas =
                                Decimal(
                                    rd,
                                    "HorasProgramadas"),

                            Cambio =
                                NullableTiempo(
                                    rd,
                                    "Cambio"),

                            Arranque =
                                NullableTiempo(
                                    rd,
                                    "Arranque"),

                            EstatusID =
                                estatusId,

                            EstaEnLinea =
                                estatusProduccionId ==
                                EstatusPrograma.EnProduccion,

                            DentroHorarioProgramado =
                                ahora >= inicioPrograma &&
                                ahora < finPrograma,

                            MaquinaPrincipalID =
                                NullableEntero(
                                    rd,
                                    "MaquinaPrincipalID"),

                            MaquinaPrincipalCodigo =
                                Texto(
                                    rd,
                                    "MaquinaPrincipalCodigo")
                                ?? string.Empty,

                            MaquinaPrincipalNombre =
                                Texto(
                                    rd,
                                    "MaquinaPrincipalNombre")
                                ?? string.Empty,

                            MaquinaSustitutaID =
                                NullableEntero(
                                    rd,
                                    "MaquinaSustitutaID"),

                            MaquinaSustitutaCodigo =
                                Texto(
                                    rd,
                                    "MaquinaSustitutaCodigo")
                                ?? string.Empty,

                            MaquinaSustitutaNombre =
                                Texto(
                                    rd,
                                    "MaquinaSustitutaNombre")
                                ?? string.Empty,

                            EstatusProduccionID =
                                estatusProduccionId,

                            EstatusProduccionNombre =
                                NombreEstatusProduccion(
                                    estatusProduccionId),

                            EjecucionProduccionID =
                                NullableEntero(
                                    rd,
                                    "EjecucionProduccionID"),

                            OperadorProgramadoID =
                                NullableEntero(
                                    rd,
                                    "OperadorProgramadoID"),

                            OperadorProgramadoNombre =
                                Texto(
                                    rd,
                                    "OperadorProgramadoNombre")
                                ?? string.Empty,

                            OperadorAuxiliarProgramadoID =
                                NullableEntero(
                                    rd,
                                    "OperadorAuxiliarProgramadoID"),

                            OperadorAuxiliarProgramadoNombre =
                                Texto(
                                    rd,
                                    "OperadorAuxiliarProgramadoNombre")
                                ?? string.Empty,

                            OperadorRealID =
                                NullableEntero(
                                    rd,
                                    "OperadorRealID"),

                            OperadorRealNombre =
                                Texto(
                                    rd,
                                    "OperadorRealNombre")
                                ?? string.Empty,

                            TurnoProgramadoNombre =
                                Texto(
                                    rd,
                                    "TurnoProgramadoNombre")
                                ?? string.Empty,

                            TurnoProgramadoColor =
                                Texto(
                                    rd,
                                    "TurnoProgramadoColor")
                                ?? string.Empty,

                            EscalaAsignacionID =
                                NullableEntero(
                                    rd,
                                    "EscalaAsignacionID"),

                            InspeccionCalidadID =
                                NullableEntero(
                                    rd,
                                    "InspeccionCalidadID"),

                            EstadoCalidad =
                                Texto(
                                    rd,
                                    "EstadoCalidad")
                                ?? string.Empty,

                            ConfiguracionCalidadInvalidada =
                                Booleano(
                                    rd,
                                    "ConfiguracionCalidadInvalidada"),

                            RequiereReliberacion =
                                Booleano(
                                    rd,
                                    "RequiereReliberacion"),

                            MostrarAlertaNoInicio =
                                mostrarAlertaNoInicio,

                            AlertaNoInicioCritica =
                                alertaNoInicioCritica,

                            MinutosAtrasoInicio =
                                minutosAtrasoInicio,

                            TextoAlertaNoInicio =
                                textoAlertaNoInicio
                        });
                }
            }

            foreach (var maquina in maquinas)
            {
                maquina.Bloques =
                    bloques
                    .Where(
                        x =>
                        x.MaquinaID ==
                        maquina.MaquinaID)
                    .OrderBy(x => x.Inicio)
                    .ThenBy(
                        x =>
                        x.ProgramaProduccionID)
                    .ToList();

                AsignarCarriles(maquina);
            }

            return maquinas;
        }

        private static void AsignarCarriles(
            PlaneacionCalendarioMaquinaVm maquina)
        {
            var finPorCarril =
                new List<DateTime>();

            foreach (var bloque in
                maquina.Bloques
                    .OrderBy(x => x.Inicio))
            {
                var carril = -1;

                for (var i = 0;
                     i < finPorCarril.Count;
                     i++)
                {
                    if (finPorCarril[i] <=
                        bloque.Inicio)
                    {
                        carril = i;
                        break;
                    }
                }

                if (carril < 0)
                {
                    carril =
                        finPorCarril.Count;

                    finPorCarril.Add(
                        bloque.Fin);
                }
                else
                {
                    finPorCarril[carril] =
                        bloque.Fin;
                }

                bloque.Carril = carril;
            }

            maquina.Carriles =
                Math.Max(
                    1,
                    finPorCarril.Count);
        }

        private static PeriodoCalendario ResolverPeriodo(
            string? vista,
            DateTime? fecha,
            DateTime? rangoInicio,
            DateTime? rangoFin)
        {
            var vistaNormalizada =
                (vista ?? "semana")
                .Trim()
                .ToLowerInvariant();

            var fechaBase =
                (fecha ?? DateTime.Today).Date;

            if (vistaNormalizada == "dia")
            {
                return new PeriodoCalendario
                {
                    Vista = "dia",
                    TextoVista = "Día",
                    Titulo =
                        fechaBase.ToString(
                            "dddd dd 'de' MMMM 'de' yyyy",
                            new CultureInfo("es-MX")),
                    Inicio = fechaBase,
                    Fin = fechaBase.AddDays(1),
                    Anterior =
                        fechaBase.AddDays(-1),
                    Siguiente =
                        fechaBase.AddDays(1)
                };
            }

            if (vistaNormalizada == "mes")
            {
                var inicioMes =
                    new DateTime(
                        fechaBase.Year,
                        fechaBase.Month,
                        1);

                var finMes =
                    inicioMes.AddMonths(1);

                return new PeriodoCalendario
                {
                    Vista = "mes",
                    TextoVista = "Mes",
                    Titulo =
                        inicioMes.ToString(
                            "MMMM yyyy",
                            new CultureInfo("es-MX")),
                    Inicio = inicioMes,
                    Fin = finMes,
                    Anterior =
                        inicioMes.AddMonths(-1),
                    Siguiente =
                        inicioMes.AddMonths(1)
                };
            }

            if (vistaNormalizada == "rango")
            {
                var inicio =
                    (rangoInicio ??
                     fechaBase).Date;

                var finInclusive =
                    (rangoFin ??
                     inicio.AddDays(6)).Date;

                if (finInclusive < inicio)
                    finInclusive = inicio;

                if ((finInclusive - inicio)
                    .TotalDays > 30)
                {
                    finInclusive =
                        inicio.AddDays(30);
                }

                return new PeriodoCalendario
                {
                    Vista = "rango",
                    TextoVista = "Rango",
                    Titulo =
                        $"{inicio:dd/MM/yyyy} - {finInclusive:dd/MM/yyyy}",
                    Inicio = inicio,
                    Fin = finInclusive.AddDays(1),
                    Anterior =
                        inicio.AddDays(
                            -(finInclusive -
                              inicio).Days - 1),
                    Siguiente =
                        inicio.AddDays(
                            (finInclusive -
                             inicio).Days + 1),
                    RangoInicio = inicio,
                    RangoFin = finInclusive
                };
            }

            var diasDesdeLunes =
                ((int)fechaBase.DayOfWeek + 6)
                % 7;

            var inicioSemana =
                fechaBase.AddDays(
                    -diasDesdeLunes);

            return new PeriodoCalendario
            {
                Vista = "semana",
                TextoVista = "Semana",
                Titulo =
                    $"{inicioSemana:dd/MM/yyyy} - {inicioSemana.AddDays(6):dd/MM/yyyy}",
                Inicio = inicioSemana,
                Fin = inicioSemana.AddDays(7),
                Anterior =
                    inicioSemana.AddDays(-7),
                Siguiente =
                    inicioSemana.AddDays(7)
            };
        }

        private bool UsuarioEnSesion()
        {
            return HttpContext.Session
                .GetInt32("UsuarioID")
                .HasValue;
        }

        private int ObtenerUsuarioID()
        {
            return HttpContext.Session
                .GetInt32("UsuarioID")
                ?? 0;
        }

        private static string NombreEstatusProduccion(
            int? estatusId)
        {
            return estatusId switch
            {
                1 => "Pendiente",
                2 => "En preparación",
                3 => "En producción",
                4 => "Pausado",
                5 => "Terminado parcial",
                6 => "Terminado",
                9 => "Cerrado",
                99 => "Cancelado",
                _ => "Sin ejecución"
            };
        }

        private static int Entero(
            SqlDataReader rd,
            string columna)
        {
            var ordinal =
                rd.GetOrdinal(columna);

            return rd.IsDBNull(ordinal)
                ? 0
                : Convert.ToInt32(
                    rd.GetValue(ordinal));
        }

        private static int? NullableEntero(
            SqlDataReader rd,
            string columna)
        {
            var ordinal =
                rd.GetOrdinal(columna);

            return rd.IsDBNull(ordinal)
                ? null
                : Convert.ToInt32(
                    rd.GetValue(ordinal));
        }

        private static decimal Decimal(
            SqlDataReader rd,
            string columna)
        {
            var ordinal =
                rd.GetOrdinal(columna);

            return rd.IsDBNull(ordinal)
                ? 0m
                : Convert.ToDecimal(
                    rd.GetValue(ordinal));
        }

        private static DateTime Fecha(
            SqlDataReader rd,
            string columna)
        {
            var ordinal =
                rd.GetOrdinal(columna);

            return rd.IsDBNull(ordinal)
                ? DateTime.MinValue
                : Convert.ToDateTime(
                    rd.GetValue(ordinal));
        }

        private static TimeSpan? NullableTiempo(
            SqlDataReader rd,
            string columna)
        {
            var ordinal =
                rd.GetOrdinal(columna);

            return rd.IsDBNull(ordinal)
                ? null
                : (TimeSpan)rd.GetValue(
                    ordinal);
        }

        private static string? Texto(
            SqlDataReader rd,
            string columna)
        {
            var ordinal =
                rd.GetOrdinal(columna);

            return rd.IsDBNull(ordinal)
                ? null
                : rd.GetValue(ordinal)
                    ?.ToString()
                    ?.Trim();
        }

        private static bool Booleano(
            SqlDataReader rd,
            string columna)
        {
            var ordinal =
                rd.GetOrdinal(columna);

            return
                !rd.IsDBNull(ordinal) &&
                Convert.ToBoolean(
                    rd.GetValue(ordinal));
        }

        private sealed class PeriodoCalendario
        {
            public string Vista { get; set; } =
                "semana";

            public string TextoVista { get; set; } =
                "Semana";

            public string Titulo { get; set; } =
                string.Empty;

            public DateTime Inicio { get; set; }
            public DateTime Fin { get; set; }

            public DateTime Anterior { get; set; }
            public DateTime Siguiente { get; set; }

            public DateTime? RangoInicio { get; set; }
            public DateTime? RangoFin { get; set; }
        }
    }
}