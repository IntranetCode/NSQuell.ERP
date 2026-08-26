
using ERP.NSQuell.Models.ViewModels.Logistica;
using ERP.NSQuell.Servicios;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LogisticaEmbarquesController : Controller
{
    private readonly IConfiguration _configuration;
    private readonly IServicioAcceso _acceso;
    private readonly IWebHostEnvironment _environment;

    public LogisticaEmbarquesController(IConfiguration configuration, IServicioAcceso acceso, IWebHostEnvironment environment)
    {
        _configuration = configuration;
        _acceso = acceso;
        _environment = environment;
    }

    private string ConnectionString =>
        _configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("No se encontro ConnectionStrings:DefaultConnection.");

    private int? UsuarioID => HttpContext.Session.GetInt32("UsuarioID");

    private string UsuarioNombre =>
        HttpContext.Session.GetString("NombreMostrar")
        ?? HttpContext.Session.GetString("Username")
        ?? User?.Identity?.Name
        ?? "Usuario";

    private async Task<IActionResult?> ValidarAccesoAsync(string submenu)
    {
        if (!UsuarioID.HasValue)
            return RedirectToAction("Login", "Login");

        if (!await _acceso.TienePermisoAsync(UsuarioID.Value, submenu))
            return Forbid();

        return null;
    }

    private async Task<SqlConnection> AbrirAsync(CancellationToken cancellationToken)
    {
        var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(cancellationToken);
        return cn;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? periodo = "hoy", int? mes = null, int? anio = null, CancellationToken cancellationToken = default)
    {
        var acceso = await ValidarAccesoAsync("Tablero de Logística");
        if (acceso != null) return acceso;

        var vm = new LogisticaIndexVm();
        await using var cn = await AbrirAsync(cancellationToken);

        if (!await TieneFase1Async(cn, cancellationToken))
        {
            ViewBag.ErrorConfiguracion = "Falta ejecutar 29_LOGISTICA_FASE1_RELEASE_TEST_v1.0.sql.";
            return View(vm);
        }

        var hoy = DateTime.Today;
        periodo = (periodo ?? "hoy").Trim().ToLowerInvariant();
        var periodosPermitidos = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "hoy","semana","mes","seleccionar_mes","vencidos"
    };
        if (!periodosPermitidos.Contains(periodo)) periodo = "hoy";

        DateTime? fechaDesde;
        DateTime? fechaHasta;
        string tituloPeriodo;

        switch (periodo)
        {
            case "vencidos":
                fechaDesde = null;
                fechaHasta = hoy.AddDays(-1);
                tituloPeriodo = "Vencidos por programar";
                break;

            case "semana":
                var diferenciaLunes = ((int)hoy.DayOfWeek + 6) % 7;
                fechaDesde = hoy.AddDays(-diferenciaLunes);
                fechaHasta = fechaDesde.Value.AddDays(6);
                tituloPeriodo = $"Esta semana · {fechaDesde:dd/MM/yyyy} al {fechaHasta:dd/MM/yyyy}";
                break;

            case "mes":
                fechaDesde = new DateTime(hoy.Year, hoy.Month, 1);
                fechaHasta = fechaDesde.Value.AddMonths(1).AddDays(-1);
                tituloPeriodo = $"Este mes · {fechaDesde:MMMM yyyy}";
                break;

            case "seleccionar_mes":
                var anioSeleccionado = anio.HasValue && anio.Value >= 2020 && anio.Value <= 2100 ? anio.Value : hoy.Year;
                var mesSeleccionado = mes.HasValue && mes.Value >= 1 && mes.Value <= 12 ? mes.Value : hoy.Month;
                fechaDesde = new DateTime(anioSeleccionado, mesSeleccionado, 1);
                fechaHasta = fechaDesde.Value.AddMonths(1).AddDays(-1);
                mes = mesSeleccionado;
                anio = anioSeleccionado;
                tituloPeriodo = $"Programación · {fechaDesde:MMMM yyyy}";
                break;

            default:
                periodo = "hoy";
                fechaDesde = hoy;
                fechaHasta = hoy;
                tituloPeriodo = $"Próximos a programar hoy · {hoy:dd/MM/yyyy}";
                break;
        }

        vm.Demandas = await CargarDemandasAsync(cn, null, fechaDesde, fechaHasta, true, null, cancellationToken);

        const string sqlEmbarques = @"
SELECT TOP (300)
    EmbarqueID,
    ISNULL(Folio,N'') AS Folio,
    ISNULL(ClienteNombreSnapshot,N'') AS Cliente,
    ISNULL(Destino,N'') AS Destino,
    FechaCargaProgramada,
    HoraCargaProgramada,
    FechaEntregaProgramada,
    HoraEntregaProgramada,
    ISNULL(Estatus,N'') AS Estatus,
    ISNULL(TieneIncidencia,0) AS TieneIncidencia,
    TotalPartidas,
    TotalCajas,
    TotalPiezasSolicitadas,
    TotalPiezasDespachadas
FROM dbo.vw_Logistica_Tablero
ORDER BY
    CASE WHEN Estatus IN(N'Entregado',N'Cancelado') THEN 1 ELSE 0 END,
    COALESCE(FechaCargaProgramada,FechaProgramada),
    EmbarqueID DESC;";

        await using (var cmd = new SqlCommand(sqlEmbarques, cn))
        {
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await rd.ReadAsync(cancellationToken))
            {
                vm.Embarques.Add(new LogisticaEmbarqueResumenVm
                {
                    EmbarqueID = Entero(rd, "EmbarqueID"),
                    Folio = Texto(rd, "Folio"),
                    Cliente = Texto(rd, "Cliente"),
                    Destino = Texto(rd, "Destino"),
                    FechaCargaProgramada = Fecha(rd, "FechaCargaProgramada"),
                    HoraCargaProgramada = Hora(rd, "HoraCargaProgramada"),
                    FechaEntregaProgramada = Fecha(rd, "FechaEntregaProgramada"),
                    HoraEntregaProgramada = Hora(rd, "HoraEntregaProgramada"),
                    Estatus = Texto(rd, "Estatus"),
                    TieneIncidencia = Booleano(rd, "TieneIncidencia"),
                    TotalPartidas = Entero(rd, "TotalPartidas"),
                    TotalCajas = Entero(rd, "TotalCajas"),
                    TotalPiezasSolicitadas = Entero(rd, "TotalPiezasSolicitadas"),
                    TotalPiezasDespachadas = Entero(rd, "TotalPiezasDespachadas")
                });
            }
        }

        vm.DemandasPendientes = vm.Demandas.Count(x => x.PendienteProgramar > 0);
        vm.PiezasPendientes = vm.Demandas.Sum(x => (long)x.PendienteProgramar);
        vm.PiezasPTListas = vm.Demandas.Sum(x => x.PiezasPTDisponibles);
        vm.EmbarquesActivos = vm.Embarques.Count(x => x.Estatus is not "Entregado" and not "Cancelado");
        vm.CargasHoy = vm.Embarques.Count(x => x.FechaCargaProgramada?.Date == hoy);
        vm.EntregasAtrasadas = vm.Embarques.Count(x => x.Estatus is not "Entregado" and not "Cancelado" && x.FechaEntregaProgramada.HasValue && x.FechaEntregaProgramada.Value.Date < hoy);
        vm.EmbarquesPreparados = vm.Embarques.Count(x => x.Estatus == "Preparado");
        vm.EmbarquesCargados = vm.Embarques.Count(x => x.Estatus == "Cargado");
        vm.EmbarquesEnRuta = vm.Embarques.Count(x => x.Estatus == "En ruta");
        vm.EmbarquesEntregados = vm.Embarques.Count(x => x.Estatus == "Entregado");
        vm.EmbarquesConIncidencia = vm.Embarques.Count(x => x.TieneIncidencia);
        vm.CajasMovilizadas = vm.Embarques.Where(x => x.Estatus is "Cargado" or "En ruta" or "Entregado").Sum(x => (long)x.TotalCajas);
        vm.PiezasMovilizadas = vm.Embarques.Sum(x => (long)x.TotalPiezasDespachadas);

        var inicioPeriodo = new DateTime(hoy.Year, hoy.Month, 1);
        var finPeriodo = inicioPeriodo.AddMonths(1);

        const string sqlResumenClientes = @"
SELECT
    e.ClienteID,
    ISNULL(NULLIF(LTRIM(RTRIM(e.ClienteNombreSnapshot)),N''),N'Sin cliente') AS Cliente,
    COUNT_BIG(*) AS TotalEmbarques,
    SUM(CASE WHEN e.Estatus=N'Preparado' THEN 1 ELSE 0 END) AS Preparados,
    SUM(CASE WHEN e.Estatus=N'Cargado' THEN 1 ELSE 0 END) AS Cargados,
    SUM(CASE WHEN e.Estatus=N'En ruta' THEN 1 ELSE 0 END) AS EnRuta,
    SUM(CASE WHEN e.Estatus=N'Entregado' THEN 1 ELSE 0 END) AS Entregados,
    SUM(CASE WHEN ISNULL(e.TieneIncidencia,0)=1 THEN 1 ELSE 0 END) AS ConIncidencia,
    ISNULL(SUM(CASE WHEN e.Estatus IN(N'Cargado',N'En ruta',N'Entregado') THEN ISNULL(c.TotalCajas,0) ELSE 0 END),0) AS TotalCajas,
    ISNULL(SUM(ISNULL(d.TotalPiezasDespachadas,0)),0) AS TotalPiezas,
    SUM(CASE WHEN e.Estatus=N'Entregado' AND e.FechaEntrega IS NOT NULL AND e.FechaEntregaProgramada IS NOT NULL AND CAST(e.FechaEntrega AS date)<=e.FechaEntregaProgramada THEN 1 ELSE 0 END) AS EntregasATiempo,
    SUM(CASE WHEN e.Estatus=N'Entregado' AND e.FechaEntrega IS NOT NULL AND e.FechaEntregaProgramada IS NOT NULL AND CAST(e.FechaEntrega AS date)>e.FechaEntregaProgramada THEN 1 ELSE 0 END) AS EntregasAtrasadas
FROM dbo.Logistica_Embarques e
OUTER APPLY
(
    SELECT COUNT_BIG(*) AS TotalCajas
    FROM dbo.Logistica_EmbarqueCajas ec
    WHERE ec.EmbarqueID=e.EmbarqueID
      AND ec.EstatusSeleccion=N'Despachada'
) c
OUTER APPLY
(
    SELECT ISNULL(SUM(ed.CantidadDespachada),0) AS TotalPiezasDespachadas
    FROM dbo.Logistica_EmbarqueDetalle ed
    WHERE ed.EmbarqueID=e.EmbarqueID
      AND ed.Activo=1
) d
WHERE e.Activo=1
  AND COALESCE(e.FechaEntregaProgramada,e.FechaCargaProgramada,e.FechaProgramada)>=@InicioPeriodo
  AND COALESCE(e.FechaEntregaProgramada,e.FechaCargaProgramada,e.FechaProgramada)<@FinPeriodo
GROUP BY e.ClienteID,ISNULL(NULLIF(LTRIM(RTRIM(e.ClienteNombreSnapshot)),N''),N'Sin cliente')
ORDER BY COUNT_BIG(*) DESC,Cliente;";

        await using (var cmd = new SqlCommand(sqlResumenClientes, cn))
        {
            cmd.Parameters.Add("@InicioPeriodo", SqlDbType.Date).Value = inicioPeriodo;
            cmd.Parameters.Add("@FinPeriodo", SqlDbType.Date).Value = finPeriodo;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);

            while (await rd.ReadAsync(cancellationToken))
            {
                vm.ResumenClientes.Add(new LogisticaResumenClienteVm
                {
                    ClienteID = Entero(rd, "ClienteID"),
                    Cliente = Texto(rd, "Cliente"),
                    TotalEmbarques = Convert.ToInt32(EnteroLargo(rd, "TotalEmbarques")),
                    Preparados = Entero(rd, "Preparados"),
                    Cargados = Entero(rd, "Cargados"),
                    EnRuta = Entero(rd, "EnRuta"),
                    Entregados = Entero(rd, "Entregados"),
                    ConIncidencia = Entero(rd, "ConIncidencia"),
                    TotalCajas = EnteroLargo(rd, "TotalCajas"),
                    TotalPiezas = EnteroLargo(rd, "TotalPiezas"),
                    EntregasATiempo = Entero(rd, "EntregasATiempo"),
                    EntregasAtrasadas = Entero(rd, "EntregasAtrasadas")
                });
            }
        }

        ViewBag.Periodo = periodo;
        ViewBag.PeriodoProgramacion = tituloPeriodo;
        ViewBag.FechaDesdeProgramacion = fechaDesde;
        ViewBag.FechaHastaProgramacion = fechaHasta;
        ViewBag.MesSeleccionado = mes ?? hoy.Month;
        ViewBag.AnioSeleccionado = anio ?? hoy.Year;
        ViewBag.PeriodoResumen = inicioPeriodo.ToString("MMMM yyyy");

        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Crear(int? clienteId, int? releaseDetalleId, CancellationToken cancellationToken)
    {
        var acceso = await ValidarAccesoAsync("Nuevo embarque");
        if (acceso != null) return acceso;
        await using var cn = await AbrirAsync(cancellationToken);
        if (!await TieneFase1Async(cn, cancellationToken))
        {
            TempData["LogisticaError"] = "Falta la estructura de Logística Fase 1.";
            return RedirectToAction(nameof(Index));
        }
        LogisticaDemandaVm? demandaInicial = null;
        if (releaseDetalleId.HasValue && releaseDetalleId.Value > 0)
        {
            demandaInicial = await ObtenerDemandaAsync(cn, releaseDetalleId.Value, null, cancellationToken);
            if (demandaInicial == null || demandaInicial.PendienteProgramar <= 0)
            {
                TempData["LogisticaError"] = "La entrega seleccionada ya no tiene cantidad pendiente por programar.";
                return RedirectToAction(nameof(Index));
            }
            if (!demandaInicial.ClienteID.HasValue || demandaInicial.ClienteID.Value <= 0)
            {
                TempData["LogisticaError"] = "La entrega seleccionada no tiene un cliente válido.";
                return RedirectToAction(nameof(Index));
            }
            clienteId = demandaInicial.ClienteID.Value;
        }
        var vm = new LogisticaCrearVm
        {
            ClienteID = clienteId,
            TipoOperacion = "Nacional",
            FormaEnvio = "Interno",
            FechaCargaProgramada = DateTime.Today,
            FechaEntregaProgramada = DateTime.Today
        };
        vm.Clientes = await CargarClientesPendientesAsync(cn, cancellationToken);
        if (clienteId.HasValue && clienteId.Value > 0)
        {
            vm.Partidas = await CargarPartidasClienteAsync(cn, clienteId.Value, null, cancellationToken);
            if (demandaInicial != null)
            {
                var partida = vm.Partidas.FirstOrDefault(x => x.ReleaseDetalleID == demandaInicial.ReleaseDetalleID);
                if (partida != null)
                {
                    partida.Seleccionada = true;
                    partida.CantidadSolicitada = demandaInicial.PendienteProgramar;
                    partida.CajaIDs = SeleccionarCajasSugeridas(partida.CajasDisponibles, partida.CantidadSolicitada);
                    vm.Destino = demandaInicial.Cliente;
                    vm.FechaCargaProgramada = demandaInicial.FechaCarga?.Date ?? demandaInicial.FechaEntrega.Date.AddDays(-1);
                    vm.FechaEntregaProgramada = demandaInicial.FechaEntrega.Date;
                }
            }
            else if (vm.Partidas.Count > 0)
            {
                var primera = vm.Partidas.OrderBy(x => x.FechaEntrega).First();
                vm.Destino = vm.Clientes.FirstOrDefault(x => x.Id == clienteId.Value)?.Texto ?? string.Empty;
                vm.FechaCargaProgramada = primera.FechaCarga?.Date ?? primera.FechaEntrega.Date.AddDays(-1);
                vm.FechaEntregaProgramada = primera.FechaEntrega.Date;
            }
        }
        await CargarCatalogosAsync(vm, cn, cancellationToken);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Crear(LogisticaCrearVm vm, CancellationToken cancellationToken)
    {
        var acceso = await ValidarAccesoAsync("Nuevo embarque");
        if (acceso != null) return acceso;
        vm.Destino = vm.Destino?.Trim() ?? string.Empty;
        vm.DireccionEntrega = vm.DireccionEntrega?.Trim();
        vm.OperadorTexto = vm.OperadorTexto?.Trim();
        vm.Observaciones = vm.Observaciones?.Trim();
        vm.Transportista = vm.Transportista?.Trim();
        vm.GuiaReferencia = vm.GuiaReferencia?.Trim();
        vm.TipoOperacion = NormalizarTipoOperacion(vm.TipoOperacion);
        vm.FormaEnvio = NormalizarFormaEnvio(vm.FormaEnvio);
        vm.ModalidadEnvio = NormalizarModalidadEnvio(vm.ModalidadEnvio);
        vm.Partidas ??= new List<LogisticaCrearPartidaVm>();
        var seleccionadas = vm.Partidas.Where(x => x.Seleccionada).ToList();
        if (!vm.ClienteID.HasValue || vm.ClienteID.Value <= 0) ModelState.AddModelError(nameof(vm.ClienteID), "Selecciona un cliente.");
        if (seleccionadas.Count == 0) ModelState.AddModelError(nameof(vm.Partidas), "Selecciona al menos una entrega pendiente.");
        if (seleccionadas.GroupBy(x => x.ReleaseDetalleID).Any(g => g.Count() > 1)) ModelState.AddModelError(nameof(vm.Partidas), "Una misma entrega no puede seleccionarse más de una vez.");
        foreach (var partida in seleccionadas)
        {
            if (partida.ReleaseDetalleID <= 0) ModelState.AddModelError(nameof(vm.Partidas), "Existe una entrega seleccionada no válida.");
            if (partida.CantidadSolicitada <= 0) ModelState.AddModelError(nameof(vm.Partidas), $"La cantidad de la entrega {partida.ReleaseDetalleID} debe ser mayor a cero.");
        }
        var cajasRepetidas = seleccionadas.SelectMany(x => x.CajaIDs ?? new List<int>()).Where(x => x > 0).GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (cajasRepetidas.Count > 0) ModelState.AddModelError(nameof(vm.Partidas), "Una misma caja PT no puede asignarse a más de una partida.");
        if (string.IsNullOrWhiteSpace(vm.TipoOperacion)) ModelState.AddModelError(nameof(vm.TipoOperacion), "Selecciona Nacional o Exportación.");
        if (string.IsNullOrWhiteSpace(vm.FormaEnvio)) ModelState.AddModelError(nameof(vm.FormaEnvio), "Selecciona Entrega interna/local o Paquetería/Transportista.");
        if (string.IsNullOrWhiteSpace(vm.Destino)) ModelState.AddModelError(nameof(vm.Destino), "El destino es obligatorio.");
        if (vm.Destino.Length > 300) ModelState.AddModelError(nameof(vm.Destino), "El destino no puede exceder 300 caracteres.");
        if (vm.FechaCargaProgramada == default) ModelState.AddModelError(nameof(vm.FechaCargaProgramada), "La fecha de carga es obligatoria.");
        if (vm.FechaEntregaProgramada == default) ModelState.AddModelError(nameof(vm.FechaEntregaProgramada), "La fecha de entrega es obligatoria.");
        if (vm.FechaCargaProgramada != default && vm.FechaEntregaProgramada != default && vm.FechaEntregaProgramada.Date < vm.FechaCargaProgramada.Date) ModelState.AddModelError(nameof(vm.FechaEntregaProgramada), "La fecha de entrega no puede ser anterior a la fecha de carga.");
        if (vm.TipoOperacion == "Nacional") vm.PasaAduana = null;
        if (vm.TipoOperacion == "Exportacion" && !vm.PasaAduana.HasValue) ModelState.AddModelError(nameof(vm.PasaAduana), "Indica si la exportación pasa por aduana.");
        if (vm.FormaEnvio == "Interno")
        {
            if (!vm.RutaID.HasValue || vm.RutaID.Value <= 0) ModelState.AddModelError(nameof(vm.RutaID), "Selecciona una ruta.");
            if (!vm.UnidadID.HasValue || vm.UnidadID.Value <= 0) ModelState.AddModelError(nameof(vm.UnidadID), "Selecciona una unidad.");
            if (string.IsNullOrWhiteSpace(vm.OperadorTexto)) ModelState.AddModelError(nameof(vm.OperadorTexto), "Captura el operador.");
            vm.ModalidadEnvio = null;
            vm.Transportista = null;
            vm.GuiaReferencia = null;
        }
        else if (vm.FormaEnvio == "Paqueteria")
        {
            vm.RutaID = null;
            vm.UnidadID = null;
            vm.OperadorTexto = null;
            if (string.IsNullOrWhiteSpace(vm.ModalidadEnvio)) ModelState.AddModelError(nameof(vm.ModalidadEnvio), "Selecciona modalidad Terrestre, Aérea o Marítima.");
            if (string.IsNullOrWhiteSpace(vm.Transportista)) ModelState.AddModelError(nameof(vm.Transportista), "Captura la compañía o transportista.");
            if (vm.TipoOperacion == "Exportacion" && vm.PasaAduana == false && string.IsNullOrWhiteSpace(vm.GuiaReferencia)) ModelState.AddModelError(nameof(vm.GuiaReferencia), "Captura la guía o referencia de la exportación.");
        }
        await using var cn = await AbrirAsync(cancellationToken);
        vm.Clientes = await CargarClientesPendientesAsync(cn, cancellationToken);
        if (!ModelState.IsValid)
        {
            if (vm.ClienteID.HasValue && vm.ClienteID.Value > 0) vm.Partidas = await CargarPartidasClienteAsync(cn, vm.ClienteID.Value, vm.Partidas, cancellationToken);
            await CargarCatalogosAsync(vm, cn, cancellationToken);
            return View(vm);
        }
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var demandasValidadas = new List<(LogisticaCrearPartidaVm Formulario, LogisticaDemandaVm Demanda)>();
            foreach (var partida in seleccionadas)
            {
                var demanda = await ObtenerDemandaAsync(cn, partida.ReleaseDetalleID, tx, cancellationToken) ?? throw new InvalidOperationException($"La entrega {partida.ReleaseDetalleID} ya no está disponible.");
                if (!demanda.ClienteID.HasValue || demanda.ClienteID.Value <= 0) throw new InvalidOperationException($"La entrega {demanda.FolioRelease} no tiene un cliente válido.");
                if (demanda.ClienteID.Value != vm.ClienteID!.Value) throw new InvalidOperationException("No se pueden mezclar entregas de clientes distintos en un mismo embarque.");
                if (!demanda.ParteID.HasValue || demanda.ParteID.Value <= 0) throw new InvalidOperationException($"La entrega {demanda.FolioRelease} no tiene una parte válida.");
                if (demanda.PendienteProgramar <= 0) throw new InvalidOperationException($"La entrega {demanda.FolioRelease} / {demanda.NumeroParte} ya no tiene cantidad pendiente.");
                if (partida.CantidadSolicitada <= 0 || partida.CantidadSolicitada > demanda.PendienteProgramar) throw new InvalidOperationException($"La cantidad para {demanda.NumeroParte} debe estar entre 1 y {demanda.PendienteProgramar:N0} PZA.");
                demandasValidadas.Add((partida, demanda));
            }
            if (demandasValidadas.Count == 0) throw new InvalidOperationException("Selecciona al menos una entrega.");
            var cliente = demandasValidadas[0].Demanda.Cliente?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(cliente)) throw new InvalidOperationException("El cliente seleccionado no tiene un nombre válido.");
            const string sqlHeader = @"
INSERT dbo.Logistica_Embarques
(Folio,ClienteID,ClienteNombreSnapshot,Destino,DireccionEntrega,TipoOperacion,FormaEnvio,ModalidadEnvio,Transportista,GuiaReferencia,PasaAduana,FechaProgramada,FechaCargaProgramada,HoraCargaProgramada,FechaEntregaProgramada,HoraEntregaProgramada,Estatus,RutaID,UnidadID,OperadorTexto,ResponsableUsuarioID,ResponsableNombreSnapshot,Observaciones,FechaCreacion,CreadoPor,Activo)
VALUES
(NULL,@ClienteID,@ClienteNombre,@Destino,@DireccionEntrega,@TipoOperacion,@FormaEnvio,@ModalidadEnvio,@Transportista,@GuiaReferencia,@PasaAduana,@FechaCarga,@FechaCarga,@HoraCarga,@FechaEntrega,@HoraEntrega,N'Programado',@RutaID,@UnidadID,@OperadorTexto,@UsuarioID,@UsuarioNombre,@Observaciones,SYSDATETIME(),@UsuarioNombre,1);
SELECT CONVERT(int,SCOPE_IDENTITY());";
            int embarqueId;
            await using (var cmd = new SqlCommand(sqlHeader, cn, tx))
            {
                cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = vm.ClienteID.Value;
                cmd.Parameters.Add("@ClienteNombre", SqlDbType.NVarChar, 200).Value = cliente;
                cmd.Parameters.Add("@Destino", SqlDbType.NVarChar, 300).Value = vm.Destino;
                cmd.Parameters.Add("@DireccionEntrega", SqlDbType.NVarChar, 600).Value = Db(vm.DireccionEntrega);
                cmd.Parameters.Add("@TipoOperacion", SqlDbType.NVarChar, 30).Value = vm.TipoOperacion;
                cmd.Parameters.Add("@FormaEnvio", SqlDbType.NVarChar, 30).Value = vm.FormaEnvio;
                cmd.Parameters.Add("@ModalidadEnvio", SqlDbType.NVarChar, 30).Value = Db(vm.ModalidadEnvio);
                cmd.Parameters.Add("@Transportista", SqlDbType.NVarChar, 200).Value = Db(vm.Transportista);
                cmd.Parameters.Add("@GuiaReferencia", SqlDbType.NVarChar, 150).Value = Db(vm.GuiaReferencia);
                cmd.Parameters.Add("@PasaAduana", SqlDbType.Bit).Value = Db(vm.PasaAduana);
                cmd.Parameters.Add("@FechaCarga", SqlDbType.Date).Value = vm.FechaCargaProgramada.Date;
                cmd.Parameters.Add("@HoraCarga", SqlDbType.Time).Value = Db(vm.HoraCargaProgramada);
                cmd.Parameters.Add("@FechaEntrega", SqlDbType.Date).Value = vm.FechaEntregaProgramada.Date;
                cmd.Parameters.Add("@HoraEntrega", SqlDbType.Time).Value = Db(vm.HoraEntregaProgramada);
                cmd.Parameters.Add("@RutaID", SqlDbType.Int).Value = Db(vm.RutaID);
                cmd.Parameters.Add("@UnidadID", SqlDbType.Int).Value = Db(vm.UnidadID);
                cmd.Parameters.Add("@OperadorTexto", SqlDbType.NVarChar, 200).Value = Db(vm.OperadorTexto);
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = Db(UsuarioID);
                cmd.Parameters.Add("@UsuarioNombre", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1200).Value = Db(vm.Observaciones);
                embarqueId = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
            }
            var folio = $"LOG-{DateTime.Today:yyyy}-{embarqueId:000000}";
            await EjecutarAsync(cn, tx, "UPDATE dbo.Logistica_Embarques SET Folio=@Folio WHERE EmbarqueID=@EmbarqueID;", cancellationToken, ("@Folio", folio), ("@EmbarqueID", embarqueId));
            var totalCajasReservadas = 0;
            var totalPiezas = 0;
            foreach (var item in demandasValidadas)
            {
                var detalleId = await InsertarDetalleAsync(cn, tx, embarqueId, item.Demanda, item.Formulario.CantidadSolicitada, cancellationToken);
                var cajas = await ReservarCajasInicialesAsync(cn, tx, embarqueId, detalleId, item.Demanda, item.Formulario.CajaIDs, item.Formulario.CantidadSolicitada, cancellationToken);
                totalCajasReservadas += cajas;
                totalPiezas += item.Formulario.CantidadSolicitada;
            }
            var estadoInicial = totalCajasReservadas > 0 ? "Preparando" : "Programado";
            if (totalCajasReservadas > 0)
            {
                await EjecutarAsync(cn, tx, @"UPDATE dbo.Logistica_Embarques SET Estatus=N'Preparando',FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario WHERE EmbarqueID=@EmbarqueID AND Activo=1;", cancellationToken, ("@Usuario", UsuarioNombre), ("@EmbarqueID", embarqueId));
            }
            await InsertarHistorialAsync(cn, tx, embarqueId, "PROGRAMACION_CREADA", null, estadoInicial, $"Programación creada por cliente. Cliente: {cliente}. Partidas: {demandasValidadas.Count:N0}. Piezas: {totalPiezas:N0}. Cajas reservadas: {totalCajasReservadas:N0}. Forma de envío: {vm.FormaEnvio}. Destino: {vm.Destino}.", cancellationToken);
            await tx.CommitAsync(cancellationToken);
            TempData["LogisticaOk"] = totalCajasReservadas > 0 ? $"{folio} creado con {demandasValidadas.Count:N0} partida(s), {totalPiezas:N0} piezas y {totalCajasReservadas:N0} caja(s) PT reservada(s)." : $"{folio} creado con {demandasValidadas.Count:N0} partida(s) y {totalPiezas:N0} piezas. Las cajas PT podrán reservarse posteriormente.";
            return RedirectToAction(nameof(Detalle), new { id = embarqueId });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            ModelState.AddModelError("", ex.Message);
            if (vm.ClienteID.HasValue && vm.ClienteID.Value > 0) vm.Partidas = await CargarPartidasClienteAsync(cn, vm.ClienteID.Value, vm.Partidas, cancellationToken);
            await CargarCatalogosAsync(vm, cn, cancellationToken);
            return View(vm);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Detalle(int id, CancellationToken cancellationToken)
    {
        var acceso = await ValidarAccesoAsync("Tablero de Logística");
        if (acceso != null) return acceso;

        await using var cn = await AbrirAsync(cancellationToken);
        var vm = await CargarDetalleAsync(cn, id, cancellationToken);
        return vm == null ? NotFound() : View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AgregarDetalle(LogisticaAgregarDetalleVm model, CancellationToken cancellationToken)
    {
        var acceso = await ValidarAccesoAsync("Tablero de Logística");
        if (acceso != null) return acceso;
        if (model.EmbarqueID <= 0)
        {
            TempData["LogisticaError"] = "El embarque indicado no es válido.";
            return RedirectToAction(nameof(Index));
        }
        if (model.ReleaseDetalleID <= 0)
        {
            TempData["LogisticaError"] = "Selecciona una entrega de Release válida.";
            return RedirectToAction(nameof(Detalle), new { id = model.EmbarqueID });
        }
        if (model.CantidadSolicitada <= 0)
        {
            TempData["LogisticaError"] = "La cantidad solicitada debe ser mayor a cero.";
            return RedirectToAction(nameof(Detalle), new { id = model.EmbarqueID });
        }
        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var header = await ObtenerHeaderAsync(cn, tx, model.EmbarqueID, cancellationToken) ?? throw new InvalidOperationException("El embarque no existe.");
            if (header.Estatus is not "Programado" and not "Preparando") throw new InvalidOperationException("Solo se pueden agregar partidas mientras el embarque está en preparación.");
            if (header.ClienteID <= 0 || string.IsNullOrWhiteSpace(header.Cliente)) throw new InvalidOperationException("El embarque no tiene un cliente válido.");
            if (string.IsNullOrWhiteSpace(header.Destino)) throw new InvalidOperationException("El embarque no tiene un destino válido.");
            var demanda = await ObtenerDemandaAsync(cn, model.ReleaseDetalleID, tx, cancellationToken) ?? throw new InvalidOperationException("La entrega del Release ya no está disponible.");
            if (!demanda.ClienteID.HasValue || demanda.ClienteID.Value <= 0) throw new InvalidOperationException("La entrega seleccionada no tiene un cliente válido.");
            if (demanda.ClienteID.Value != header.ClienteID) throw new InvalidOperationException("No se pueden mezclar clientes distintos en el mismo embarque.");
            if (!demanda.ParteID.HasValue || demanda.ParteID.Value <= 0) throw new InvalidOperationException("La entrega seleccionada no tiene una parte válida.");
            if (demanda.PendienteProgramar <= 0) throw new InvalidOperationException("La entrega seleccionada ya no tiene cantidad pendiente por programar.");
            if (model.CantidadSolicitada > demanda.PendienteProgramar) throw new InvalidOperationException($"La cantidad solicitada supera el pendiente actual. Pendiente: {demanda.PendienteProgramar:N0} PZA.");
            const string sqlDuplicado = @"SELECT COUNT_BIG(*) FROM dbo.Logistica_EmbarqueDetalle WITH(UPDLOCK,HOLDLOCK) WHERE EmbarqueID=@EmbarqueID AND ReleaseDetalleID=@ReleaseDetalleID AND Activo=1;";
            await using (var cmd = new SqlCommand(sqlDuplicado, cn, tx))
            {
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = model.EmbarqueID;
                cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = model.ReleaseDetalleID;
                if (Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken)) > 0) throw new InvalidOperationException("Esta entrega del Release ya está agregada al embarque.");
            }
            await InsertarDetalleAsync(cn, tx, model.EmbarqueID, demanda, model.CantidadSolicitada, cancellationToken);
            await EjecutarAsync(cn, tx, @"UPDATE dbo.Logistica_Embarques SET Estatus=N'Preparando',FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario WHERE EmbarqueID=@EmbarqueID AND Activo=1 AND Estatus IN(N'Programado',N'Preparando');", cancellationToken, ("@Usuario", UsuarioNombre), ("@EmbarqueID", model.EmbarqueID));
            await InsertarHistorialAsync(cn, tx, model.EmbarqueID, "PARTIDA_AGREGADA", header.Estatus, "Preparando", $"Se agregó Release {demanda.FolioRelease}, parte {demanda.NumeroParte}, OF {demanda.NumeroOF}, cantidad {model.CantidadSolicitada:N0} PZA.", cancellationToken);
            await tx.CommitAsync(cancellationToken);
            TempData["LogisticaOk"] = "Partida agregada correctamente.";
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            TempData["LogisticaError"] = ex.Message;
        }
        return RedirectToAction(nameof(Detalle), new { id = model.EmbarqueID });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReservarCaja(int embarqueId, int embarqueDetalleId, int cajaId, CancellationToken cancellationToken)
    {
        var acceso = await ValidarAccesoAsync("Tablero de Logística");
        if (acceso != null) return acceso;

        if (embarqueId <= 0 || embarqueDetalleId <= 0 || cajaId <= 0)
        {
            TempData["LogisticaError"] = "Los datos de la reserva no son válidos.";
            return embarqueId > 0
                ? RedirectToAction(nameof(Detalle), new { id = embarqueId })
                : RedirectToAction(nameof(Index));
        }

        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        try
        {
            var header = await ObtenerHeaderAsync(cn, tx, embarqueId, cancellationToken)
                ?? throw new InvalidOperationException("El embarque no existe.");

            if (header.Estatus is not "Programado" and not "Preparando")
                throw new InvalidOperationException("Las cajas solo se pueden modificar durante la preparación.");

            const string sqlDetalle = @"
SELECT
    d.ParteID,
    d.SolicitudProduccionID,
    d.CantidadSolicitada,
    ISNULL(d.NumeroParteSnapshot,N'') AS NumeroParte,
    ISNULL(d.NumeroOFSnapshot,N'') AS NumeroOF,
    ISNULL((
        SELECT SUM(ec.CantidadAsignada)
        FROM dbo.Logistica_EmbarqueCajas ec
        WHERE ec.EmbarqueDetalleID=d.EmbarqueDetalleID
          AND ec.Activo=1
    ),0) AS CantidadAsignada
FROM dbo.Logistica_EmbarqueDetalle d WITH (UPDLOCK,HOLDLOCK)
WHERE d.EmbarqueDetalleID=@DetalleID
  AND d.EmbarqueID=@EmbarqueID
  AND d.Activo=1;";

            int parteId;
            int? solicitudId;
            int solicitado;
            int asignado;
            string numeroParte;
            string numeroOF;

            await using (var cmd = new SqlCommand(sqlDetalle, cn, tx))
            {
                cmd.Parameters.Add("@DetalleID", SqlDbType.Int).Value = embarqueDetalleId;
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;

                await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);

                if (!await rd.ReadAsync(cancellationToken))
                    throw new InvalidOperationException("La partida logística no existe.");

                parteId = Entero(rd, "ParteID");
                solicitudId = EnteroNullable(rd, "SolicitudProduccionID");
                solicitado = Entero(rd, "CantidadSolicitada");
                asignado = Entero(rd, "CantidadAsignada");
                numeroParte = Texto(rd, "NumeroParte");
                numeroOF = Texto(rd, "NumeroOF");
            }

            if (!solicitudId.HasValue)
                throw new InvalidOperationException("La partida todavía no tiene OF. No se pueden reservar cajas.");

            const string sqlCaja = @"
SELECT
    c.CajaID,
    c.ParteID,
    c.SolicitudProduccionID,
    c.Disponible,
    ISNULL(c.Etiqueta,N'') AS Etiqueta,
    ISNULL(c.LoteEtiqueta,N'') AS Lote
FROM dbo.AlmacenPT_Cajas base WITH (UPDLOCK,HOLDLOCK)
INNER JOIN dbo.vw_Logistica_CajasDisponibles c ON c.CajaID=base.CajaID
WHERE base.CajaID=@CajaID
  AND base.Activo=1;";

            int cajaParte;
            int? cajaSolicitud;
            int disponible;
            string etiqueta;
            string lote;

            await using (var cmd = new SqlCommand(sqlCaja, cn, tx))
            {
                cmd.Parameters.Add("@CajaID", SqlDbType.Int).Value = cajaId;

                await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);

                if (!await rd.ReadAsync(cancellationToken))
                    throw new InvalidOperationException("La caja ya no está disponible para Logística.");

                cajaParte = Entero(rd, "ParteID");
                cajaSolicitud = EnteroNullable(rd, "SolicitudProduccionID");
                disponible = Entero(rd, "Disponible");
                etiqueta = Texto(rd, "Etiqueta");
                lote = Texto(rd, "Lote");
            }

            if (cajaParte != parteId)
                throw new InvalidOperationException("La caja no corresponde a la parte de esta partida.");

            if (cajaSolicitud != solicitudId)
                throw new InvalidOperationException("La caja no corresponde a la OF de esta partida.");

            var pendiente = Math.Max(0, solicitado - asignado);

            if (disponible <= 0 || disponible > pendiente)
                throw new InvalidOperationException($"La caja tiene {disponible:N0} piezas y el pendiente es {pendiente:N0}. La operación trabaja con cajas completas.");

            const string sqlInsert = @"
INSERT dbo.Logistica_EmbarqueCajas
(
    EmbarqueID,EmbarqueDetalleID,CajaID,CantidadAsignada,EstatusSeleccion,
    FechaSeleccion,UsuarioSeleccionID,UsuarioSeleccionNombre,
    Activo,FechaCreacion,CreadoPor
)
VALUES
(
    @EmbarqueID,@DetalleID,@CajaID,@Cantidad,N'Reservada',
    SYSDATETIME(),@UsuarioID,@UsuarioNombre,
    1,SYSDATETIME(),@UsuarioNombre
);";

            await using (var cmd = new SqlCommand(sqlInsert, cn, tx))
            {
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                cmd.Parameters.Add("@DetalleID", SqlDbType.Int).Value = embarqueDetalleId;
                cmd.Parameters.Add("@CajaID", SqlDbType.Int).Value = cajaId;
                cmd.Parameters.Add("@Cantidad", SqlDbType.Int).Value = disponible;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = Db(UsuarioID);
                cmd.Parameters.Add("@UsuarioNombre", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await EjecutarAsync(
                cn,
                tx,
                @"UPDATE dbo.Logistica_Embarques
SET Estatus=N'Preparando',
    FechaModificacion=SYSDATETIME(),
    ActualizadoPor=@Usuario
WHERE EmbarqueID=@Id
  AND Activo=1
  AND Estatus IN(N'Programado',N'Preparando');",
                cancellationToken,
                ("@Usuario", UsuarioNombre),
                ("@Id", embarqueId));

            await InsertarHistorialAsync(
                cn,
                tx,
                embarqueId,
                "CAJA_RESERVADA",
                header.Estatus,
                "Preparando",
                $"Caja {etiqueta} / lote {lote} reservada. Parte {numeroParte}, OF {numeroOF}, cantidad {disponible:N0} PZA.",
                cancellationToken);

            await tx.CommitAsync(cancellationToken);
            TempData["LogisticaOk"] = "Caja reservada correctamente.";
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            TempData["LogisticaError"] = ex.Message;
        }

        return RedirectToAction(nameof(Detalle), new { id = embarqueId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LiberarCaja(int embarqueId, int embarqueCajaId, CancellationToken cancellationToken)
    {
        var acceso = await ValidarAccesoAsync("Tablero de Logística");
        if (acceso != null) return acceso;

        if (embarqueId <= 0 || embarqueCajaId <= 0)
        {
            TempData["LogisticaError"] = "La caja indicada no es válida.";
            return embarqueId > 0
                ? RedirectToAction(nameof(Detalle), new { id = embarqueId })
                : RedirectToAction(nameof(Index));
        }

        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        try
        {
            var header = await ObtenerHeaderAsync(cn, tx, embarqueId, cancellationToken)
                ?? throw new InvalidOperationException("El embarque no existe.");

            if (header.Estatus is not "Programado" and not "Preparando")
                throw new InvalidOperationException("Ya no se pueden liberar cajas en este estado.");

            const string sqlCaja = @"
SELECT
    ec.EmbarqueCajaID,
    ec.CajaID,
    ec.CantidadAsignada,
    ISNULL(c.Etiqueta,N'') AS Etiqueta,
    ISNULL(c.LoteEtiqueta,N'') AS Lote,
    ISNULL(c.NumeroParte,N'') AS NumeroParte,
    ISNULL(c.NumeroOF,N'') AS NumeroOF
FROM dbo.Logistica_EmbarqueCajas ec WITH (UPDLOCK,HOLDLOCK)
INNER JOIN dbo.AlmacenPT_Cajas c ON c.CajaID=ec.CajaID
WHERE ec.EmbarqueCajaID=@EmbarqueCajaID
  AND ec.EmbarqueID=@EmbarqueID
  AND ec.Activo=1;";

            string etiqueta;
            string lote;
            string numeroParte;
            string numeroOF;
            int cantidad;

            await using (var cmd = new SqlCommand(sqlCaja, cn, tx))
            {
                cmd.Parameters.Add("@EmbarqueCajaID", SqlDbType.Int).Value = embarqueCajaId;
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;

                await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);

                if (!await rd.ReadAsync(cancellationToken))
                    throw new InvalidOperationException("La caja ya no estaba reservada.");

                etiqueta = Texto(rd, "Etiqueta");
                lote = Texto(rd, "Lote");
                numeroParte = Texto(rd, "NumeroParte");
                numeroOF = Texto(rd, "NumeroOF");
                cantidad = Entero(rd, "CantidadAsignada");
            }

            const string sqlUpdate = @"
UPDATE dbo.Logistica_EmbarqueCajas
SET Activo=0,
    EstatusSeleccion=N'Liberada',
    FechaLiberacion=SYSDATETIME(),
    UsuarioLiberacionID=@UsuarioID,
    FechaModificacion=SYSDATETIME(),
    ActualizadoPor=@Usuario
WHERE EmbarqueCajaID=@EmbarqueCajaID
  AND EmbarqueID=@EmbarqueID
  AND Activo=1;
SELECT @@ROWCOUNT;";

            int afectados;

            await using (var cmd = new SqlCommand(sqlUpdate, cn, tx))
            {
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = Db(UsuarioID);
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@EmbarqueCajaID", SqlDbType.Int).Value = embarqueCajaId;
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;

                afectados = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
            }

            if (afectados == 0)
                throw new InvalidOperationException("La caja cambió mientras se intentaba liberar.");

            await InsertarHistorialAsync(
                cn,
                tx,
                embarqueId,
                "CAJA_LIBERADA",
                header.Estatus,
                header.Estatus,
                $"Caja {etiqueta} / lote {lote} liberada. Parte {numeroParte}, OF {numeroOF}, cantidad {cantidad:N0} PZA.",
                cancellationToken);

            await tx.CommitAsync(cancellationToken);
            TempData["LogisticaOk"] = "Caja liberada.";
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            TempData["LogisticaError"] = ex.Message;
        }

        return RedirectToAction(nameof(Detalle), new { id = embarqueId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarcarPreparado(int embarqueId, CancellationToken cancellationToken)
    {
        var acceso = await ValidarAccesoAsync("Tablero de Logística");
        if (acceso != null) return acceso;

        if (embarqueId <= 0)
        {
            TempData["LogisticaError"] = "El embarque indicado no es válido.";
            return RedirectToAction(nameof(Index));
        }

        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        try
        {
            var header = await ObtenerHeaderAsync(cn, tx, embarqueId, cancellationToken)
                ?? throw new InvalidOperationException("El embarque no existe.");

            if (header.Estatus is not "Programado" and not "Preparando")
                throw new InvalidOperationException("El embarque no está en preparación.");

            if (header.ClienteID <= 0 || string.IsNullOrWhiteSpace(header.Cliente))
                throw new InvalidOperationException("No se puede liberar la preparación porque el embarque no tiene un cliente válido.");

            if (string.IsNullOrWhiteSpace(header.Destino))
                throw new InvalidOperationException("No se puede liberar la preparación porque el embarque no tiene un destino válido.");

            const string sqlValidacion = @"
SELECT COUNT_BIG(*)
FROM dbo.Logistica_EmbarqueDetalle d WITH (UPDLOCK,HOLDLOCK)
OUTER APPLY
(
    SELECT SUM(ec.CantidadAsignada) AS Cantidad
    FROM dbo.Logistica_EmbarqueCajas ec
    WHERE ec.EmbarqueDetalleID=d.EmbarqueDetalleID
      AND ec.Activo=1
) c
WHERE d.EmbarqueID=@EmbarqueID
  AND d.Activo=1
  AND ISNULL(c.Cantidad,0)<>d.CantidadSolicitada;";

            long faltantes;
            await using (var cmd = new SqlCommand(sqlValidacion, cn, tx))
            {
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                faltantes = Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken));
            }

            if (faltantes > 0)
                throw new InvalidOperationException("Todavía hay partidas cuya cantidad preparada no coincide con la cantidad programada.");

            const string sqlPartidas = @"
SELECT COUNT_BIG(*)
FROM dbo.Logistica_EmbarqueDetalle WITH (UPDLOCK,HOLDLOCK)
WHERE EmbarqueID=@EmbarqueID
  AND Activo=1
  AND CantidadSolicitada>0;";

            await using (var cmd = new SqlCommand(sqlPartidas, cn, tx))
            {
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                var partidas = Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken));

                if (partidas <= 0)
                    throw new InvalidOperationException("El embarque no contiene partidas válidas.");
            }

            const string sqlUpdate = @"
UPDATE dbo.Logistica_Embarques
SET Estatus=N'Preparado',
    FechaPreparacion=SYSDATETIME(),
    PreparadoPorUsuarioID=@UsuarioID,
    FechaModificacion=SYSDATETIME(),
    ActualizadoPor=@Usuario
WHERE EmbarqueID=@EmbarqueID
  AND Activo=1
  AND Estatus IN(N'Programado',N'Preparando');
SELECT @@ROWCOUNT;";

            int afectados;
            await using (var cmd = new SqlCommand(sqlUpdate, cn, tx))
            {
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = Db(UsuarioID);
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                afectados = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
            }

            if (afectados == 0)
                throw new InvalidOperationException("El embarque cambió de estado mientras se confirmaba la preparación.");

            await InsertarHistorialAsync(
                cn,
                tx,
                embarqueId,
                "PREPARACION_COMPLETA",
                header.Estatus,
                "Preparado",
                $"Preparación liberada. Cliente: {header.Cliente}. Destino: {header.Destino}. Todas las cantidades requeridas quedaron preparadas.",
                cancellationToken);

            await tx.CommitAsync(cancellationToken);
            TempData["LogisticaOk"] = "Embarque preparado.";
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            TempData["LogisticaError"] = ex.Message;
        }

        return RedirectToAction(nameof(Detalle), new { id = embarqueId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarcarCargado(int embarqueId, CancellationToken cancellationToken)
    {
        var acceso = await ValidarAccesoAsync("Tablero de Logística");
        if (acceso != null) return acceso;

        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(cancellationToken);
        try
        {
            var header = await ObtenerHeaderAsync(cn, tx, embarqueId, cancellationToken)
                ?? throw new InvalidOperationException("El embarque no existe.");
            if (header.Estatus != "Preparado")
                throw new InvalidOperationException("Solo un embarque Preparado puede marcarse como Cargado.");

            await EjecutarAsync(
                cn,
                tx,
                @"
UPDATE dbo.Logistica_EmbarqueCajas
SET EstatusSeleccion=N'Cargada',FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
WHERE EmbarqueID=@Id AND Activo=1;
UPDATE dbo.Logistica_Embarques
SET Estatus=N'Cargado',FechaCarga=SYSDATETIME(),CargaPorUsuarioID=@UsuarioID,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
WHERE EmbarqueID=@Id;",
                cancellationToken,
                ("@Usuario", UsuarioNombre),
                ("@Id", embarqueId),
                ("@UsuarioID", UsuarioID));
            await InsertarHistorialAsync(
                cn,
                tx,
                embarqueId,
                "CARGA_COMPLETA",
                "Preparado",
                "Cargado",
                "Carga fisica confirmada. PT aun no se descuenta.",
                cancellationToken);

            await tx.CommitAsync(cancellationToken);
            TempData["LogisticaOk"] = "Carga confirmada. Ahora puede validarse la salida.";
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            TempData["LogisticaError"] = ex.Message;
        }

        return RedirectToAction(nameof(Detalle), new { id = embarqueId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Despachar(int embarqueId, CancellationToken cancellationToken)
    {
        var acceso = await ValidarAccesoAsync("Tablero de Logística");
        if (acceso != null) return acceso;
        if (embarqueId <= 0)
        {
            TempData["LogisticaError"] = "El embarque indicado no es válido.";
            return RedirectToAction(nameof(Index));
        }
        try
        {
            await using var cn = await AbrirAsync(cancellationToken);
            const string sql = @"
SELECT e.EmbarqueID,ISNULL(e.Estatus,N'') AS Estatus,e.ClienteID,ISNULL(NULLIF(LTRIM(RTRIM(e.ClienteNombreSnapshot)),N''),N'') AS Cliente,
ISNULL(NULLIF(LTRIM(RTRIM(e.Destino)),N''),N'') AS Destino,ISNULL(NULLIF(LTRIM(RTRIM(e.TipoOperacion)),N''),N'Nacional') AS TipoOperacion,
ISNULL(NULLIF(LTRIM(RTRIM(e.FormaEnvio)),N''),N'Interno') AS FormaEnvio,ISNULL(NULLIF(LTRIM(RTRIM(e.ModalidadEnvio)),N''),N'') AS ModalidadEnvio,
ISNULL(NULLIF(LTRIM(RTRIM(e.Transportista)),N''),N'') AS Transportista,ISNULL(NULLIF(LTRIM(RTRIM(e.GuiaReferencia)),N''),N'') AS GuiaReferencia,e.PasaAduana,
e.RutaID,e.UnidadID,ISNULL(NULLIF(LTRIM(RTRIM(e.OperadorTexto)),N''),N'') AS Operador,
ISNULL((SELECT SUM(d.CantidadSolicitada) FROM dbo.Logistica_EmbarqueDetalle d WHERE d.EmbarqueID=e.EmbarqueID AND d.Activo=1),0) AS TotalSolicitado,
ISNULL((SELECT SUM(ec.CantidadAsignada) FROM dbo.Logistica_EmbarqueCajas ec WHERE ec.EmbarqueID=e.EmbarqueID AND ec.Activo=1),0) AS TotalAsignado,
ISNULL((SELECT COUNT_BIG(*) FROM dbo.Logistica_EmbarqueCajas ec WHERE ec.EmbarqueID=e.EmbarqueID AND ec.Activo=1 AND ISNULL(ec.EstatusSeleccion,N'')<>N'Cargada'),0) AS CajasNoCargadas,
ISNULL((SELECT COUNT_BIG(*) FROM dbo.Logistica_Incidencias i WHERE i.EmbarqueID=e.EmbarqueID AND i.Activo=1 AND i.Estatus IN(N'Abierta',N'En seguimiento') AND i.Severidad=N'Crítica'),0) AS IncidenciasCriticas
FROM dbo.Logistica_Embarques e WHERE e.EmbarqueID=@EmbarqueID AND e.Activo=1;";
            string estatus, tipoOperacion, formaEnvio, modalidadEnvio, transportista, guia, operador, cliente, destino;
            int clienteId, totalSolicitado, totalAsignado;
            int? rutaId, unidadId;
            bool? pasaAduana;
            long cajasNoCargadas, incidenciasCriticas;
            await using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
                if (!await rd.ReadAsync(cancellationToken)) throw new InvalidOperationException("El embarque no existe.");
                estatus = Texto(rd, "Estatus");
                clienteId = Entero(rd, "ClienteID");
                cliente = Texto(rd, "Cliente");
                destino = Texto(rd, "Destino");
                tipoOperacion = NormalizarTipoOperacion(Texto(rd, "TipoOperacion"));
                if (string.IsNullOrWhiteSpace(tipoOperacion)) tipoOperacion = "Nacional";
                formaEnvio = NormalizarFormaEnvio(Texto(rd, "FormaEnvio"));
                if (string.IsNullOrWhiteSpace(formaEnvio)) formaEnvio = "Interno";
                modalidadEnvio = NormalizarModalidadEnvio(Texto(rd, "ModalidadEnvio"));
                transportista = Texto(rd, "Transportista");
                guia = Texto(rd, "GuiaReferencia");
                pasaAduana = rd.IsDBNull(rd.GetOrdinal("PasaAduana")) ? null : Convert.ToBoolean(rd["PasaAduana"]);
                rutaId = EnteroNullable(rd, "RutaID");
                unidadId = EnteroNullable(rd, "UnidadID");
                operador = Texto(rd, "Operador");
                totalSolicitado = Entero(rd, "TotalSolicitado");
                totalAsignado = Entero(rd, "TotalAsignado");
                cajasNoCargadas = EnteroLargo(rd, "CajasNoCargadas");
                incidenciasCriticas = EnteroLargo(rd, "IncidenciasCriticas");
            }
            if (estatus is not "Cargado" and not "En ruta" and not "Entregado") throw new InvalidOperationException("Solo un embarque Cargado puede confirmar su salida.");
            if (estatus == "Cargado")
            {
                if (clienteId <= 0 || string.IsNullOrWhiteSpace(cliente)) throw new InvalidOperationException("El embarque no tiene un cliente válido.");
                if (string.IsNullOrWhiteSpace(destino)) throw new InvalidOperationException("El embarque no tiene un destino válido.");
                if (formaEnvio == "Interno")
                {
                    if (!rutaId.HasValue) throw new InvalidOperationException("El embarque interno no tiene ruta asignada.");
                    if (!unidadId.HasValue) throw new InvalidOperationException("El embarque interno no tiene unidad asignada.");
                    if (string.IsNullOrWhiteSpace(operador)) throw new InvalidOperationException("El embarque interno no tiene operador asignado.");
                }
                else if (formaEnvio == "Paqueteria")
                {
                    if (string.IsNullOrWhiteSpace(modalidadEnvio)) throw new InvalidOperationException("Selecciona la modalidad del envío.");
                    if (string.IsNullOrWhiteSpace(transportista)) throw new InvalidOperationException("El envío no tiene compañía o transportista.");
                    if (tipoOperacion == "Exportacion" && pasaAduana == false && string.IsNullOrWhiteSpace(guia)) throw new InvalidOperationException("La exportación sin aduana debe tener una guía o referencia.");
                }
                if (totalSolicitado <= 0) throw new InvalidOperationException("El embarque no contiene piezas para despachar.");
                if (totalAsignado != totalSolicitado) throw new InvalidOperationException($"La preparación está incompleta. Programado: {totalSolicitado:N0} PZA. Preparado: {totalAsignado:N0} PZA.");
                if (cajasNoCargadas > 0) throw new InvalidOperationException(cajasNoCargadas == 1 ? "Existe una caja activa cuya carga física no está confirmada." : $"Existen {cajasNoCargadas:N0} cajas activas cuya carga física no está confirmada.");
                if (incidenciasCriticas > 0) throw new InvalidOperationException("El embarque tiene incidencias críticas abiertas.");
                var faltantes = await ObtenerDocumentosFaltantesAsync(cn, embarqueId, tipoOperacion, modalidadEnvio, pasaAduana, cancellationToken);
                if (faltantes.Count > 0) throw new InvalidOperationException($"No se puede confirmar la salida. Faltan documentos obligatorios validados: {string.Join(", ", faltantes)}.");
            }
            await using var sp = new SqlCommand("dbo.usp_Logistica_DespacharEmbarque", cn) { CommandType = CommandType.StoredProcedure };
            sp.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
            sp.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = Db(UsuarioID);
            sp.Parameters.Add("@UsuarioNombre", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
            await using var resultado = await sp.ExecuteReaderAsync(cancellationToken);
            if (!await resultado.ReadAsync(cancellationToken)) throw new InvalidOperationException("El procedimiento de despacho terminó sin devolver confirmación.");
            var referencia = Texto(resultado, "ReferenciaOperacion");
            var yaDespachado = Booleano(resultado, "YaDespachado");
            TempData["LogisticaOk"] = yaDespachado ? "El embarque ya había sido despachado. No se generaron movimientos PT duplicados." : string.IsNullOrWhiteSpace(referencia) ? "Salida validada y PT descontado correctamente." : $"Salida validada y PT descontado correctamente. Referencia: {referencia}.";
        }
        catch (SqlException ex)
        {
            TempData["LogisticaError"] = $"No fue posible confirmar la salida: {ex.Message}";
        }
        catch (Exception ex)
        {
            TempData["LogisticaError"] = ex.Message;
        }
        return RedirectToAction(nameof(Detalle), new { id = embarqueId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reprogramar(
    LogisticaReprogramarVm model,
    CancellationToken cancellationToken)
    {
        var acceso =
            await ValidarAccesoAsync("Tablero de Logística");

        if (acceso != null)
            return acceso;

        if (model.FechaEntregaProgramada.Date <
            model.FechaCargaProgramada.Date)
        {
            ModelState.AddModelError(
                nameof(model.FechaEntregaProgramada),
                "La fecha de entrega no puede ser anterior a la fecha de carga.");
        }

        model.Motivo =
            model.Motivo?.Trim() ?? string.Empty;

        model.Observaciones =
            model.Observaciones?.Trim();

        var motivosPermitidos = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
    {
        "Cliente",
        "Producción",
        "Producto no disponible",
        "Calidad",
        "Unidad no disponible",
        "Operador",
        "Transportista",
        "Cambio de ruta",
        "Retraso operativo",
        "Otro"
    };

        if (!motivosPermitidos.Contains(model.Motivo))
        {
            ModelState.AddModelError(
                nameof(model.Motivo),
                "Selecciona un motivo de reprogramación válido.");
        }

        if (!ModelState.IsValid)
        {
            TempData["LogisticaError"] =
                "Revisa los datos de la reprogramación.";

            return RedirectToAction(
                nameof(Detalle),
                new { id = model.EmbarqueID });
        }

        await using var cn =
            await AbrirAsync(cancellationToken);

        await using var tx =
            (SqlTransaction)await cn.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        try
        {
            const string sqlActual = @"
SELECT
    EmbarqueID,
    ISNULL(Estatus,N'') AS Estatus,
    FechaCargaProgramada,
    HoraCargaProgramada,
    FechaEntregaProgramada,
    HoraEntregaProgramada
FROM dbo.Logistica_Embarques WITH (UPDLOCK,HOLDLOCK)
WHERE
    EmbarqueID=@EmbarqueID
    AND Activo=1;";

            string estatus;
            DateTime? fechaCargaAnterior;
            TimeSpan? horaCargaAnterior;
            DateTime? fechaEntregaAnterior;
            TimeSpan? horaEntregaAnterior;

            await using (var cmd =
                new SqlCommand(sqlActual, cn, tx))
            {
                cmd.Parameters.Add(
                    "@EmbarqueID",
                    SqlDbType.Int).Value =
                    model.EmbarqueID;

                await using var rd =
                    await cmd.ExecuteReaderAsync(
                        cancellationToken);

                if (!await rd.ReadAsync(cancellationToken))
                {
                    throw new InvalidOperationException(
                        "El embarque no existe.");
                }

                estatus =
                    Texto(rd, "Estatus");

                fechaCargaAnterior =
                    Fecha(rd, "FechaCargaProgramada");

                horaCargaAnterior =
                    Hora(rd, "HoraCargaProgramada");

                fechaEntregaAnterior =
                    Fecha(rd, "FechaEntregaProgramada");

                horaEntregaAnterior =
                    Hora(rd, "HoraEntregaProgramada");
            }

            if (estatus is "En ruta" or "Entregado" or "Cancelado")
            {
                throw new InvalidOperationException(
                    $"No se puede reprogramar un embarque con estatus {estatus}.");
            }

            var sinCambios =
                fechaCargaAnterior?.Date ==
                    model.FechaCargaProgramada.Date
                &&
                horaCargaAnterior ==
                    model.HoraCargaProgramada
                &&
                fechaEntregaAnterior?.Date ==
                    model.FechaEntregaProgramada.Date
                &&
                horaEntregaAnterior ==
                    model.HoraEntregaProgramada;

            if (sinCambios)
            {
                throw new InvalidOperationException(
                    "Las nuevas fechas y horas son iguales a la programación actual.");
            }

            const string sqlUpdate = @"
UPDATE dbo.Logistica_Embarques
SET
    FechaProgramada = @FechaCarga,
    FechaCargaProgramada = @FechaCarga,
    HoraCargaProgramada = @HoraCarga,
    FechaEntregaProgramada = @FechaEntrega,
    HoraEntregaProgramada = @HoraEntrega,
    FechaModificacion = SYSDATETIME(),
    ActualizadoPor = @Usuario
WHERE
    EmbarqueID = @EmbarqueID
    AND Activo = 1;";

            await using (var cmd =
                new SqlCommand(sqlUpdate, cn, tx))
            {
                cmd.Parameters.Add(
                    "@FechaCarga",
                    SqlDbType.Date).Value =
                    model.FechaCargaProgramada.Date;

                cmd.Parameters.Add(
                    "@HoraCarga",
                    SqlDbType.Time).Value =
                    Db(model.HoraCargaProgramada);

                cmd.Parameters.Add(
                    "@FechaEntrega",
                    SqlDbType.Date).Value =
                    model.FechaEntregaProgramada.Date;

                cmd.Parameters.Add(
                    "@HoraEntrega",
                    SqlDbType.Time).Value =
                    Db(model.HoraEntregaProgramada);

                cmd.Parameters.Add(
                    "@Usuario",
                    SqlDbType.NVarChar,
                    200).Value =
                    UsuarioNombre;

                cmd.Parameters.Add(
                    "@EmbarqueID",
                    SqlDbType.Int).Value =
                    model.EmbarqueID;

                var afectados =
                    await cmd.ExecuteNonQueryAsync(
                        cancellationToken);

                if (afectados == 0)
                {
                    throw new InvalidOperationException(
                        "No fue posible actualizar la programación.");
                }
            }

            var cargaAnterior =
                FormatearFechaHoraLogistica(
                    fechaCargaAnterior,
                    horaCargaAnterior);

            var cargaNueva =
                FormatearFechaHoraLogistica(
                    model.FechaCargaProgramada,
                    model.HoraCargaProgramada);

            var entregaAnterior =
                FormatearFechaHoraLogistica(
                    fechaEntregaAnterior,
                    horaEntregaAnterior);

            var entregaNueva =
                FormatearFechaHoraLogistica(
                    model.FechaEntregaProgramada,
                    model.HoraEntregaProgramada);

            var observacionHistorial =
                $"Motivo: {model.Motivo}. " +
                $"Carga: {cargaAnterior} → {cargaNueva}. " +
                $"Entrega: {entregaAnterior} → {entregaNueva}.";

            if (!string.IsNullOrWhiteSpace(
                model.Observaciones))
            {
                observacionHistorial +=
                    $" Observaciones: {model.Observaciones}";
            }

            await InsertarHistorialAsync(
                cn,
                tx,
                model.EmbarqueID,
                "REPROGRAMACION",
                estatus,
                estatus,
                observacionHistorial,
                cancellationToken);

            await tx.CommitAsync(cancellationToken);

            TempData["LogisticaOk"] =
                "El embarque fue reprogramado correctamente.";
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);

            TempData["LogisticaError"] =
                $"No fue posible reprogramar el embarque: {ex.Message}";
        }

        return RedirectToAction(
            nameof(Detalle),
            new { id = model.EmbarqueID });
    }

    private async Task InsertarHistorialAsync(
    SqlConnection cn,
    SqlTransaction tx,
    int embarqueId,
    string evento,
    string? anterior,
    string? nuevo,
    string? observaciones,
    CancellationToken cancellationToken)
    {
        const string sql = @"
INSERT dbo.Logistica_EmbarqueHistorial
(EmbarqueID,Evento,EstadoAnterior,EstadoNuevo,Observaciones,UsuarioID,UsuarioNombre,FechaEvento)
VALUES(@EmbarqueID,@Evento,@Anterior,@Nuevo,@Observaciones,@UsuarioID,@UsuarioNombre,SYSDATETIME());";

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
        cmd.Parameters.Add("@Evento", SqlDbType.NVarChar, 80).Value = evento;
        cmd.Parameters.Add("@Anterior", SqlDbType.NVarChar, 20).Value = Db(anterior);
        cmd.Parameters.Add("@Nuevo", SqlDbType.NVarChar, 20).Value = Db(nuevo);
        cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1200).Value = Db(observaciones);
        cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = Db(UsuarioID);
        cmd.Parameters.Add("@UsuarioNombre", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
    private static string FormatearFechaHoraLogistica(
    DateTime? fecha,
    TimeSpan? hora)
    {
        if (!fecha.HasValue)
            return "Sin fecha";

        var texto =
            fecha.Value.ToString("dd/MM/yyyy");

        if (hora.HasValue)
        {
            texto +=
                $" {hora.Value:hh\\:mm}";
        }

        return texto;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Entregar(LogisticaEntregaVm model, CancellationToken cancellationToken)
    {
        var acceso = await ValidarAccesoAsync("Tablero de Logística");
        if (acceso != null) return acceso;

        if (model.EmbarqueID <= 0)
        {
            TempData["LogisticaError"] = "El embarque indicado no es válido.";
            return RedirectToAction(nameof(Index));
        }

        model.ReceptorNombre = model.ReceptorNombre?.Trim() ?? string.Empty;
        model.FolioRemision = model.FolioRemision?.Trim();
        model.Observaciones = model.Observaciones?.Trim();

        if (string.IsNullOrWhiteSpace(model.ReceptorNombre))
            ModelState.AddModelError(nameof(model.ReceptorNombre), "Captura el nombre de quien recibe.");

        if (!ModelState.IsValid)
        {
            TempData["LogisticaError"] = "Captura correctamente los datos de la entrega.";
            return RedirectToAction(nameof(Detalle), new { id = model.EmbarqueID });
        }

        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        try
        {
            var header = await ObtenerHeaderAsync(cn, tx, model.EmbarqueID, cancellationToken)
                ?? throw new InvalidOperationException("El embarque no existe.");

            if (header.Estatus == "Entregado")
                throw new InvalidOperationException("El embarque ya se encuentra entregado.");

            if (header.Estatus == "Cancelado")
                throw new InvalidOperationException("No se puede confirmar la entrega de un embarque cancelado.");

            if (header.Estatus != "En ruta")
                throw new InvalidOperationException("Solo un embarque En ruta puede marcarse como Entregado.");

            const string sqlValidacion = @"
SELECT
    COUNT_BIG(*) AS TotalPartidas,
    ISNULL(SUM(d.CantidadSolicitada),0) AS TotalSolicitado,
    ISNULL(SUM(d.CantidadDespachada),0) AS TotalDespachado,
    SUM(CASE WHEN d.CantidadDespachada<=0 THEN 1 ELSE 0 END) AS PartidasSinDespacho,
    SUM(CASE WHEN d.CantidadDespachada>d.CantidadSolicitada THEN 1 ELSE 0 END) AS PartidasInconsistentes
FROM dbo.Logistica_EmbarqueDetalle d WITH (UPDLOCK,HOLDLOCK)
WHERE d.EmbarqueID=@EmbarqueID AND d.Activo=1;

SELECT COUNT_BIG(*)
FROM dbo.Logistica_Incidencias i WITH (UPDLOCK,HOLDLOCK)
WHERE i.EmbarqueID=@EmbarqueID
  AND i.Activo=1
  AND i.Estatus IN(N'Abierta',N'En seguimiento')
  AND i.Severidad=N'Crítica';";

            long totalPartidas;
            int totalSolicitado;
            int totalDespachado;
            long partidasSinDespacho;
            long partidasInconsistentes;
            long incidenciasCriticas;

            await using (var cmd = new SqlCommand(sqlValidacion, cn, tx))
            {
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = model.EmbarqueID;
                await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);

                if (!await rd.ReadAsync(cancellationToken))
                    throw new InvalidOperationException("No fue posible validar las partidas del embarque.");

                totalPartidas = EnteroLargo(rd, "TotalPartidas");
                totalSolicitado = Entero(rd, "TotalSolicitado");
                totalDespachado = Entero(rd, "TotalDespachado");
                partidasSinDespacho = EnteroLargo(rd, "PartidasSinDespacho");
                partidasInconsistentes = EnteroLargo(rd, "PartidasInconsistentes");

                incidenciasCriticas = 0;
                if (await rd.NextResultAsync(cancellationToken) && await rd.ReadAsync(cancellationToken))
                    incidenciasCriticas = Convert.ToInt64(rd.GetValue(0));
            }

            if (totalPartidas <= 0)
                throw new InvalidOperationException("El embarque no contiene partidas para entregar.");

            if (totalSolicitado <= 0 || totalDespachado <= 0)
                throw new InvalidOperationException("El embarque no contiene cantidades despachadas válidas.");

            if (partidasSinDespacho > 0)
                throw new InvalidOperationException("Existen partidas sin cantidad despachada. No puede cerrarse la entrega.");

            if (partidasInconsistentes > 0)
                throw new InvalidOperationException("Existen cantidades despachadas superiores a las solicitadas. Revisa el embarque antes de cerrar.");

            if (incidenciasCriticas > 0)
                throw new InvalidOperationException(incidenciasCriticas == 1
                    ? "Existe una incidencia crítica abierta. Debe cerrarse antes de confirmar la entrega."
                    : $"Existen {incidenciasCriticas:N0} incidencias críticas abiertas. Deben cerrarse antes de confirmar la entrega.");

            const string sqlEntrega = @"
UPDATE dbo.Logistica_EmbarqueDetalle
SET CantidadEntregada=CantidadDespachada,
    FechaModificacion=SYSDATETIME(),
    ActualizadoPor=@Usuario
WHERE EmbarqueID=@EmbarqueID AND Activo=1;

UPDATE dbo.Logistica_Embarques
SET Estatus=N'Entregado',
    FechaEntrega=SYSDATETIME(),
    EntregaPorUsuarioID=@UsuarioID,
    ReceptorNombre=@Receptor,
    FolioRemision=@Remision,
    Observaciones=
        CASE
            WHEN @Observaciones IS NULL THEN Observaciones
            WHEN NULLIF(LTRIM(RTRIM(ISNULL(Observaciones,N''))),N'') IS NULL THEN @Observaciones
            ELSE CONCAT(Observaciones,NCHAR(13),NCHAR(10),@Observaciones)
        END,
    FechaModificacion=SYSDATETIME(),
    ActualizadoPor=@Usuario
WHERE EmbarqueID=@EmbarqueID
  AND Activo=1
  AND Estatus=N'En ruta';

SELECT @@ROWCOUNT;";

            int afectados;

            await using (var cmd = new SqlCommand(sqlEntrega, cn, tx))
            {
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = model.EmbarqueID;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = Db(UsuarioID);
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@Receptor", SqlDbType.NVarChar, 200).Value = model.ReceptorNombre;
                cmd.Parameters.Add("@Remision", SqlDbType.NVarChar, 100).Value = Db(string.IsNullOrWhiteSpace(model.FolioRemision) ? null : model.FolioRemision);
                cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1200).Value = Db(string.IsNullOrWhiteSpace(model.Observaciones) ? null : model.Observaciones);
                afectados = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
            }

            if (afectados == 0)
                throw new InvalidOperationException("El embarque cambió de estado mientras se confirmaba la entrega.");

            var textoHistorial = $"Entrega confirmada. Receptor: {model.ReceptorNombre}. Cantidad entregada: {totalDespachado:N0} PZA.";

            if (!string.IsNullOrWhiteSpace(model.FolioRemision))
                textoHistorial += $" Remisión: {model.FolioRemision}.";

            if (!string.IsNullOrWhiteSpace(model.Observaciones))
                textoHistorial += $" Observaciones: {model.Observaciones}";

            await InsertarHistorialAsync(cn, tx, model.EmbarqueID, "ENTREGA_CONFIRMADA", "En ruta", "Entregado", textoHistorial, cancellationToken);

            await tx.CommitAsync(cancellationToken);
            TempData["LogisticaOk"] = $"Entrega confirmada correctamente. {totalDespachado:N0} PZA entregadas a {model.ReceptorNombre}.";
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            TempData["LogisticaError"] = ex.Message;
        }

        return RedirectToAction(nameof(Detalle), new { id = model.EmbarqueID });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancelar(int embarqueId, string? motivo, CancellationToken cancellationToken)
    {
        var acceso = await ValidarAccesoAsync("Tablero de Logística");
        if (acceso != null) return acceso;

        if (string.IsNullOrWhiteSpace(motivo))
        {
            TempData["LogisticaError"] = "Captura el motivo de cancelacion.";
            return RedirectToAction(nameof(Detalle), new { id = embarqueId });
        }

        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(cancellationToken);
        try
        {
            var header = await ObtenerHeaderAsync(cn, tx, embarqueId, cancellationToken)
                ?? throw new InvalidOperationException("El embarque no existe.");
            if (header.Estatus is "Cargado" or "En ruta" or "Entregado")
                throw new InvalidOperationException("No se puede cancelar un embarque cargado, despachado o entregado desde esta pantalla.");

            await EjecutarAsync(
                cn,
                tx,
                @"
UPDATE dbo.Logistica_EmbarqueCajas
SET Activo=0,EstatusSeleccion=N'Liberada',FechaLiberacion=SYSDATETIME(),UsuarioLiberacionID=@UsuarioID,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
WHERE EmbarqueID=@Id AND Activo=1;
UPDATE dbo.Logistica_Embarques
SET Estatus=N'Cancelado',MotivoCancelacion=@Motivo,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
WHERE EmbarqueID=@Id;",
                cancellationToken,
                ("@UsuarioID", UsuarioID),
                ("@Usuario", UsuarioNombre),
                ("@Id", embarqueId),
                ("@Motivo", motivo.Trim()));
            await InsertarHistorialAsync(
                cn,
                tx,
                embarqueId,
                "CANCELACION",
                header.Estatus,
                "Cancelado",
                motivo.Trim(),
                cancellationToken);

            await tx.CommitAsync(cancellationToken);
            TempData["LogisticaOk"] = "Programacion cancelada y cajas liberadas.";
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            TempData["LogisticaError"] = ex.Message;
        }

        return RedirectToAction(nameof(Detalle), new { id = embarqueId });
    }

    private static async Task<bool> TieneFase1Async(SqlConnection cn, CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT CASE WHEN OBJECT_ID(N'dbo.vw_Logistica_DemandaRelease',N'V') IS NOT NULL
 AND COL_LENGTH(N'dbo.Logistica_EmbarqueDetalle',N'ReleaseDetalleID') IS NOT NULL
 AND COL_LENGTH(N'dbo.Logistica_Embarques',N'FechaCargaProgramada') IS NOT NULL
 THEN 1 ELSE 0 END;";
        await using var cmd = new SqlCommand(sql, cn);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private static async Task<List<LogisticaDemandaVm>> CargarDemandasAsync(
        SqlConnection cn,
        string? q,
        DateTime? fechaDesde,
        DateTime? fechaHasta,
        bool soloPendientes,
        int? clienteId,
        CancellationToken cancellationToken)
    {
        var rows = new List<LogisticaDemandaVm>();
        const string sql = @"
SELECT TOP (500)
    ReleaseDetalleID,ReleaseID,ISNULL(FolioRelease,N'') AS FolioRelease,ClienteID,
    ISNULL(Cliente,N'') AS Cliente,ParteID,ISNULL(NumeroParte,N'') AS NumeroParte,
    ISNULL(Descripcion,N'') AS Descripcion,SecuenciaEntrega,FechaCarga,FechaRequerida,
    CantidadRequerida,CantidadProgramadaLogistica,PendienteProgramar,SolicitudProduccionID,
    ISNULL(NumeroOF,N'') AS NumeroOF,CajasPTDisponibles,PiezasPTDisponibles
FROM dbo.vw_Logistica_DemandaRelease
WHERE (@SoloPendientes=0 OR PendienteProgramar>0)
  AND (@ClienteID IS NULL OR ClienteID=@ClienteID)
  AND (@Q IS NULL OR FolioRelease LIKE N'%' + @Q + N'%' OR Cliente LIKE N'%' + @Q + N'%' OR NumeroParte LIKE N'%' + @Q + N'%' OR NumeroOF LIKE N'%' + @Q + N'%')
  AND (@FechaDesde IS NULL OR COALESCE(FechaCarga,DATEADD(DAY,-1,FechaRequerida))>=@FechaDesde)
  AND (@FechaHasta IS NULL OR COALESCE(FechaCarga,DATEADD(DAY,-1,FechaRequerida))<=@FechaHasta)
ORDER BY COALESCE(FechaCarga,DATEADD(DAY,-1,FechaRequerida)),FechaRequerida,Cliente,NumeroParte;";

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@Q", SqlDbType.NVarChar, 250).Value = string.IsNullOrWhiteSpace(q) ? DBNull.Value : q.Trim();
        cmd.Parameters.Add("@FechaDesde", SqlDbType.Date).Value = fechaDesde.HasValue ? fechaDesde.Value.Date : DBNull.Value;
        cmd.Parameters.Add("@FechaHasta", SqlDbType.Date).Value = fechaHasta.HasValue ? fechaHasta.Value.Date : DBNull.Value;
        cmd.Parameters.Add("@SoloPendientes", SqlDbType.Bit).Value = soloPendientes;
        cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = Db(clienteId);
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await rd.ReadAsync(cancellationToken))
            rows.Add(MapDemanda(rd));
        return rows;
    }

    private static async Task<LogisticaDemandaVm?> ObtenerDemandaAsync(
        SqlConnection cn,
        int releaseDetalleId,
        SqlTransaction? tx,
        CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT TOP (1)
    ReleaseDetalleID,ReleaseID,ISNULL(FolioRelease,N'') AS FolioRelease,ClienteID,
    ISNULL(Cliente,N'') AS Cliente,ParteID,ISNULL(NumeroParte,N'') AS NumeroParte,
    ISNULL(Descripcion,N'') AS Descripcion,SecuenciaEntrega,FechaCarga,FechaRequerida,
    CantidadRequerida,CantidadProgramadaLogistica,PendienteProgramar,SolicitudProduccionID,
    ISNULL(NumeroOF,N'') AS NumeroOF,CajasPTDisponibles,PiezasPTDisponibles
FROM dbo.vw_Logistica_DemandaRelease
WHERE ReleaseDetalleID=@ReleaseDetalleID;";
        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = releaseDetalleId;
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        return await rd.ReadAsync(cancellationToken) ? MapDemanda(rd) : null;
    }

    private static LogisticaDemandaVm MapDemanda(SqlDataReader rd)
    {
        return new LogisticaDemandaVm
        {
            ReleaseDetalleID = Entero(rd, "ReleaseDetalleID"),
            ReleaseID = Entero(rd, "ReleaseID"),
            FolioRelease = Texto(rd, "FolioRelease"),
            ClienteID = EnteroNullable(rd, "ClienteID"),
            Cliente = Texto(rd, "Cliente"),
            ParteID = EnteroNullable(rd, "ParteID"),
            NumeroParte = Texto(rd, "NumeroParte"),
            Descripcion = Texto(rd, "Descripcion"),
            SecuenciaEntrega = EnteroNullable(rd, "SecuenciaEntrega"),
            FechaCarga = Fecha(rd, "FechaCarga"),
            FechaEntrega = Fecha(rd, "FechaRequerida") ?? DateTime.MinValue,
            CantidadRequerida = Entero(rd, "CantidadRequerida"),
            CantidadProgramada = Entero(rd, "CantidadProgramadaLogistica"),
            PendienteProgramar = Entero(rd, "PendienteProgramar"),
            SolicitudProduccionID = EnteroNullable(rd, "SolicitudProduccionID"),
            NumeroOF = Texto(rd, "NumeroOF"),
            CajasPTDisponibles = EnteroLargo(rd, "CajasPTDisponibles"),
            PiezasPTDisponibles = EnteroLargo(rd, "PiezasPTDisponibles")
        };
    }

    private static async Task<int> InsertarDetalleAsync(
        SqlConnection cn,
        SqlTransaction tx,
        int embarqueId,
        LogisticaDemandaVm demanda,
        int cantidad,
        CancellationToken cancellationToken)
    {
        if (!demanda.ParteID.HasValue)
            throw new InvalidOperationException("El Release no tiene ParteID vinculada.");

        int? solicitudDetalleId = null;
        if (demanda.SolicitudProduccionID.HasValue)
        {
            const string sqlOfDetalle = @"
SELECT TOP (1) SolicitudProduccionDetalleID
FROM dbo.SolicitudesProduccionDetalle
WHERE SolicitudProduccionID=@SolicitudProduccionID AND ParteID=@ParteID AND Activo=1
ORDER BY CASE WHEN Renglon=@Secuencia THEN 0 ELSE 1 END,SolicitudProduccionDetalleID;";
            await using var cmd = new SqlCommand(sqlOfDetalle, cn, tx);
            cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = demanda.SolicitudProduccionID.Value;
            cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = demanda.ParteID.Value;
            cmd.Parameters.Add("@Secuencia", SqlDbType.Int).Value = Db(demanda.SecuenciaEntrega);
            var value = await cmd.ExecuteScalarAsync(cancellationToken);
            if (value != null && value != DBNull.Value)
                solicitudDetalleId = Convert.ToInt32(value);
        }

        const string sql = @"
INSERT dbo.Logistica_EmbarqueDetalle
(
    EmbarqueID,ParteID,SolicitudProduccionID,SolicitudProduccionDetalleID,
    ReleaseDetalleID,FolioReleaseSnapshot,FechaCargaReleaseSnapshot,FechaEntregaReleaseSnapshot,
    SecuenciaEntregaSnapshot,NumeroParteSnapshot,DescripcionParteSnapshot,NumeroOFSnapshot,
    CantidadSolicitada,CantidadDespachada,Activo,FechaCreacion
)
OUTPUT INSERTED.EmbarqueDetalleID
VALUES
(
    @EmbarqueID,@ParteID,@SolicitudProduccionID,@SolicitudProduccionDetalleID,
    @ReleaseDetalleID,@FolioRelease,@FechaCarga,@FechaEntrega,@SecuenciaEntrega,
    @NumeroParte,@Descripcion,@NumeroOF,@Cantidad,0,1,SYSDATETIME()
);";

        await using var insert = new SqlCommand(sql, cn, tx);
        insert.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
        insert.Parameters.Add("@ParteID", SqlDbType.Int).Value = demanda.ParteID.Value;
        insert.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = Db(demanda.SolicitudProduccionID);
        insert.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value = Db(solicitudDetalleId);
        insert.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = demanda.ReleaseDetalleID;
        insert.Parameters.Add("@FolioRelease", SqlDbType.NVarChar, 80).Value = demanda.FolioRelease;
        insert.Parameters.Add("@FechaCarga", SqlDbType.Date).Value = Db(demanda.FechaCarga?.Date);
        insert.Parameters.Add("@FechaEntrega", SqlDbType.Date).Value = demanda.FechaEntrega.Date;
        insert.Parameters.Add("@SecuenciaEntrega", SqlDbType.Int).Value = Db(demanda.SecuenciaEntrega);
        insert.Parameters.Add("@NumeroParte", SqlDbType.NVarChar, 150).Value = demanda.NumeroParte;
        insert.Parameters.Add("@Descripcion", SqlDbType.NVarChar, 300).Value = Db(string.IsNullOrWhiteSpace(demanda.Descripcion) ? null : demanda.Descripcion);
        insert.Parameters.Add("@NumeroOF", SqlDbType.NVarChar, 80).Value = Db(string.IsNullOrWhiteSpace(demanda.NumeroOF) ? null : demanda.NumeroOF);
        insert.Parameters.Add("@Cantidad", SqlDbType.Int).Value = cantidad;
        return Convert.ToInt32(
            await insert.ExecuteScalarAsync(cancellationToken));
    }

    // LOGISTICA_PROGRAMACION_RAPIDA_V1_4
    private static async Task<List<LogisticaCajaDisponibleVm>> CargarCajasParaDemandaAsync(
        SqlConnection cn,
        LogisticaDemandaVm demanda,
        CancellationToken cancellationToken)
    {
        var resultado = new List<LogisticaCajaDisponibleVm>();

        if (!demanda.ParteID.HasValue || demanda.ParteID.Value <= 0)
            return resultado;

        if (!demanda.SolicitudProduccionID.HasValue
            && string.IsNullOrWhiteSpace(NormalizarOF(demanda.NumeroOF)))
        {
            // Sin OF interna ni texto OF confiable no se infieren lotes por similitud.
            return resultado;
        }

        const string sql = @"
SELECT
    c.CajaID,
    c.Etiqueta,
    c.NumeroCaja,
    c.NumeroParte,
    ISNULL(c.NumeroOF,N'') AS NumeroOF,
    ISNULL(c.LoteEtiqueta,N'') AS Lote,
    ISNULL(c.UbicacionCodigo,N'') AS Ubicacion,
    c.Disponible
FROM dbo.vw_Logistica_CajasDisponibles c
WHERE c.ParteID=@ParteID
  AND
  (
      (@SolicitudProduccionID IS NOT NULL AND c.SolicitudProduccionID=@SolicitudProduccionID)
      OR
      (
          @SolicitudProduccionID IS NULL
          AND UPPER(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(c.NumeroOF,N''))),NCHAR(39),N'/'),N'’',N'/'),N'´',N'/'),N'`',N'/'))
              = UPPER(@NumeroOFNormalizada)
      )
  )
ORDER BY c.NumeroCaja,c.CajaID;";

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = demanda.ParteID.Value;
        cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = Db(demanda.SolicitudProduccionID);
        cmd.Parameters.Add("@NumeroOFNormalizada", SqlDbType.NVarChar, 80).Value = NormalizarOF(demanda.NumeroOF);

        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await rd.ReadAsync(cancellationToken))
        {
            resultado.Add(new LogisticaCajaDisponibleVm
            {
                CajaID = Entero(rd, "CajaID"),
                Etiqueta = Texto(rd, "Etiqueta"),
                NumeroCaja = Entero(rd, "NumeroCaja"),
                NumeroParte = Texto(rd, "NumeroParte"),
                NumeroOF = Texto(rd, "NumeroOF"),
                Lote = Texto(rd, "Lote"),
                Ubicacion = Texto(rd, "Ubicacion"),
                Disponible = Entero(rd, "Disponible")
            });
        }

        return resultado;
    }

    private static List<int> SeleccionarCajasSugeridas(
        IReadOnlyList<LogisticaCajaDisponibleVm> cajas,
        int cantidadObjetivo)
    {
        var ids = new List<int>();
        var restante = Math.Max(0, cantidadObjetivo);

        foreach (var caja in cajas
            .Where(x => x.Disponible > 0)
            .OrderBy(x => x.NumeroCaja)
            .ThenBy(x => x.CajaID))
        {
            if (restante <= 0) break;
            if (caja.Disponible > restante) continue;

            ids.Add(caja.CajaID);
            restante -= caja.Disponible;
        }

        return ids;
    }

    private async Task<int> ReservarCajasInicialesAsync(
        SqlConnection cn,
        SqlTransaction tx,
        int embarqueId,
        int embarqueDetalleId,
        LogisticaDemandaVm demanda,
        IReadOnlyCollection<int>? cajaIds,
        int cantidadProgramada,
        CancellationToken cancellationToken)
    {
        var ids = (cajaIds ?? Array.Empty<int>())
            .Where(x => x > 0)
            .Distinct()
            .ToList();

        if (ids.Count == 0)
            return 0;

        if (!demanda.ParteID.HasValue)
            throw new InvalidOperationException("La demanda no tiene ParteID para validar los lotes PT.");

        if (!demanda.SolicitudProduccionID.HasValue
            && string.IsNullOrWhiteSpace(NormalizarOF(demanda.NumeroOF)))
        {
            throw new InvalidOperationException(
                "La salida todavia no tiene OF vinculada. No se pueden reservar lotes PT de forma segura.");
        }

        var acumulado = 0;
        var reservadas = 0;

        foreach (var cajaId in ids)
        {
            const string sqlCaja = @"
SELECT
    c.CajaID,
    c.ParteID,
    c.SolicitudProduccionID,
    ISNULL(c.NumeroOF,N'') AS NumeroOF,
    ISNULL(c.LoteEtiqueta,N'') AS Lote,
    ISNULL(inv.Disponible,0) AS Disponible,
    ISNULL(inv.Retenido,0) AS Retenido,
    ISNULL(c.EstadoCalidad,N'') AS EstadoCalidad
FROM dbo.AlmacenPT_Cajas c WITH (UPDLOCK,HOLDLOCK)
INNER JOIN dbo.vw_AlmacenPTInventarioCaja inv ON inv.CajaID=c.CajaID
WHERE c.CajaID=@CajaID
  AND c.Activo=1
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.Logistica_EmbarqueCajas ec WITH (UPDLOCK,HOLDLOCK)
      WHERE ec.CajaID=c.CajaID AND ec.Activo=1
  );";

            int parteId;
            int? solicitudId;
            string numeroOF;
            string lote;
            int disponible;
            int retenido;
            string calidad;

            await using (var cmd = new SqlCommand(sqlCaja, cn, tx))
            {
                cmd.Parameters.Add("@CajaID", SqlDbType.Int).Value = cajaId;
                await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
                if (!await rd.ReadAsync(cancellationToken))
                    throw new InvalidOperationException($"El lote/caja PT {cajaId} ya no esta disponible.");

                parteId = Entero(rd, "ParteID");
                solicitudId = EnteroNullable(rd, "SolicitudProduccionID");
                numeroOF = Texto(rd, "NumeroOF");
                lote = Texto(rd, "Lote");
                disponible = Entero(rd, "Disponible");
                retenido = Entero(rd, "Retenido");
                calidad = Texto(rd, "EstadoCalidad");
            }

            if (parteId != demanda.ParteID.Value)
                throw new InvalidOperationException($"El lote {lote} no corresponde a la parte {demanda.NumeroParte}.");

            if (demanda.SolicitudProduccionID.HasValue)
            {
                if (solicitudId != demanda.SolicitudProduccionID)
                    throw new InvalidOperationException($"El lote {lote} no corresponde a la OF {demanda.NumeroOF}.");
            }
            else if (!string.Equals(
                NormalizarOF(numeroOF),
                NormalizarOF(demanda.NumeroOF),
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"El lote {lote} no coincide con la OF de la salida seleccionada.");
            }

            if (!string.Equals(calidad, "Liberado", StringComparison.OrdinalIgnoreCase) || retenido > 0 || disponible <= 0)
                throw new InvalidOperationException($"El lote {lote} ya no esta liberado/disponible en PT.");

            if (acumulado + disponible > cantidadProgramada)
                throw new InvalidOperationException($"Los lotes seleccionados exceden la cantidad programada ({cantidadProgramada:N0} PZA). Quita el lote {lote}.");

            const string sqlReserva = @"
INSERT dbo.Logistica_EmbarqueCajas
(EmbarqueID,EmbarqueDetalleID,CajaID,CantidadAsignada,EstatusSeleccion,FechaSeleccion,
 UsuarioSeleccionID,UsuarioSeleccionNombre,Activo,FechaCreacion,CreadoPor)
VALUES
(@EmbarqueID,@EmbarqueDetalleID,@CajaID,@Cantidad,N'Reservada',SYSDATETIME(),
 @UsuarioID,@UsuarioNombre,1,SYSDATETIME(),@UsuarioNombre);";

            await using (var reserva = new SqlCommand(sqlReserva, cn, tx))
            {
                reserva.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                reserva.Parameters.Add("@EmbarqueDetalleID", SqlDbType.Int).Value = embarqueDetalleId;
                reserva.Parameters.Add("@CajaID", SqlDbType.Int).Value = cajaId;
                reserva.Parameters.Add("@Cantidad", SqlDbType.Int).Value = disponible;
                reserva.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = Db(UsuarioID);
                reserva.Parameters.Add("@UsuarioNombre", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                await reserva.ExecuteNonQueryAsync(cancellationToken);
            }

            acumulado += disponible;
            reservadas++;
        }

        return reservadas;
    }

    private static string NormalizarOF(string? numeroOF)
    {
        if (string.IsNullOrWhiteSpace(numeroOF))
            return string.Empty;

        return numeroOF.Trim()
            .Replace("'", "/", StringComparison.Ordinal)
            .Replace("’", "/", StringComparison.Ordinal)
            .Replace("´", "/", StringComparison.Ordinal)
            .Replace("`", "/", StringComparison.Ordinal);
    }

    private async Task<LogisticaDetalleVm?> CargarDetalleAsync(SqlConnection cn, int embarqueId, CancellationToken cancellationToken)
    {
        const string sqlHeader = @"
SELECT e.EmbarqueID,ISNULL(e.Folio,N'') AS Folio,e.ClienteID,ISNULL(e.ClienteNombreSnapshot,N'') AS ClienteNombreSnapshot,ISNULL(e.Destino,N'') AS Destino,
ISNULL(e.DireccionEntrega,N'') AS DireccionEntrega,ISNULL(e.TipoOperacion,N'Nacional') AS TipoOperacion,
ISNULL(e.FormaEnvio,N'Interno') AS FormaEnvio,ISNULL(e.ModalidadEnvio,N'') AS ModalidadEnvio,ISNULL(e.Transportista,N'') AS Transportista,
ISNULL(e.GuiaReferencia,N'') AS GuiaReferencia,e.PasaAduana,ISNULL(e.Estatus,N'') AS Estatus,e.FechaCargaProgramada,e.HoraCargaProgramada,
e.FechaEntregaProgramada,e.HoraEntregaProgramada,ISNULL(r.Codigo+N' - '+r.Nombre,N'') AS Ruta,
ISNULL(u.NumeroEconomico+CASE WHEN NULLIF(u.Placas,N'') IS NULL THEN N'' ELSE N' - '+u.Placas END,N'') AS Unidad,
ISNULL(e.OperadorTexto,N'') AS Operador,ISNULL(e.Observaciones,N'') AS Observaciones,ISNULL(e.ReferenciaOperacion,N'') AS ReferenciaOperacion,
e.FechaPreparacion,e.FechaCarga,e.FechaSalida,e.FechaEntrega,ISNULL(e.TieneIncidencia,0) AS TieneIncidencia
FROM dbo.Logistica_Embarques e
LEFT JOIN dbo.Logistica_Rutas r ON r.RutaID=e.RutaID
LEFT JOIN dbo.Logistica_Unidades u ON u.UnidadID=e.UnidadID
WHERE e.EmbarqueID=@EmbarqueID AND e.Activo=1;";
        var vm = new LogisticaDetalleVm();
        await using (var cmd = new SqlCommand(sqlHeader, cn))
        {
            cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await rd.ReadAsync(cancellationToken)) return null;
            vm.EmbarqueID = Entero(rd, "EmbarqueID");
            vm.Folio = Texto(rd, "Folio");
            vm.ClienteID = Entero(rd, "ClienteID");
            vm.Cliente = Texto(rd, "ClienteNombreSnapshot");
            vm.Destino = Texto(rd, "Destino");
            vm.DireccionEntrega = Texto(rd, "DireccionEntrega");
            vm.TipoOperacion = NormalizarTipoOperacion(Texto(rd, "TipoOperacion"));
            if (string.IsNullOrWhiteSpace(vm.TipoOperacion)) vm.TipoOperacion = "Nacional";
            vm.FormaEnvio = NormalizarFormaEnvio(Texto(rd, "FormaEnvio"));
            if (string.IsNullOrWhiteSpace(vm.FormaEnvio)) vm.FormaEnvio = "Interno";
            vm.ModalidadEnvio = NormalizarModalidadEnvio(Texto(rd, "ModalidadEnvio"));
            vm.Transportista = Texto(rd, "Transportista");
            vm.GuiaReferencia = Texto(rd, "GuiaReferencia");
            vm.PasaAduana = rd.IsDBNull(rd.GetOrdinal("PasaAduana")) ? null : Convert.ToBoolean(rd["PasaAduana"]);
            vm.Estatus = Texto(rd, "Estatus");
            vm.FechaCargaProgramada = Fecha(rd, "FechaCargaProgramada");
            vm.HoraCargaProgramada = Hora(rd, "HoraCargaProgramada");
            vm.FechaEntregaProgramada = Fecha(rd, "FechaEntregaProgramada");
            vm.HoraEntregaProgramada = Hora(rd, "HoraEntregaProgramada");
            vm.Ruta = Texto(rd, "Ruta");
            vm.Unidad = Texto(rd, "Unidad");
            vm.Operador = Texto(rd, "Operador");
            vm.Observaciones = Texto(rd, "Observaciones");
            vm.ReferenciaOperacion = Texto(rd, "ReferenciaOperacion");
            vm.FechaPreparacion = Fecha(rd, "FechaPreparacion");
            vm.FechaCarga = Fecha(rd, "FechaCarga");
            vm.FechaSalida = Fecha(rd, "FechaSalida");
            vm.FechaEntrega = Fecha(rd, "FechaEntrega");
            vm.TieneIncidencia = Booleano(rd, "TieneIncidencia");
        }
        const string sqlPartidas = @"
SELECT d.EmbarqueDetalleID,d.ReleaseDetalleID,ISNULL(d.FolioReleaseSnapshot,N'') AS FolioRelease,
ISNULL(d.NumeroParteSnapshot,N'') AS NumeroParteSnapshot,ISNULL(d.DescripcionParteSnapshot,N'') AS Descripcion,d.SolicitudProduccionID,
ISNULL(d.NumeroOFSnapshot,N'') AS NumeroOF,d.FechaCargaReleaseSnapshot,d.FechaEntregaReleaseSnapshot,d.CantidadSolicitada,d.CantidadDespachada,
ISNULL((SELECT SUM(ec.CantidadAsignada) FROM dbo.Logistica_EmbarqueCajas ec WHERE ec.EmbarqueDetalleID=d.EmbarqueDetalleID AND (ec.Activo=1 OR ec.EstatusSeleccion=N'Despachada')),0) AS CantidadAsignada
FROM dbo.Logistica_EmbarqueDetalle d
WHERE d.EmbarqueID=@EmbarqueID AND d.Activo=1
ORDER BY d.EmbarqueDetalleID;";
        await using (var cmd = new SqlCommand(sqlPartidas, cn))
        {
            cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await rd.ReadAsync(cancellationToken))
            {
                vm.Partidas.Add(new LogisticaDetallePartidaVm
                {
                    EmbarqueDetalleID = Entero(rd, "EmbarqueDetalleID"),
                    ReleaseDetalleID = EnteroNullable(rd, "ReleaseDetalleID"),
                    FolioRelease = Texto(rd, "FolioRelease"),
                    NumeroParte = Texto(rd, "NumeroParteSnapshot"),
                    Descripcion = Texto(rd, "Descripcion"),
                    SolicitudProduccionID = EnteroNullable(rd, "SolicitudProduccionID"),
                    NumeroOF = Texto(rd, "NumeroOF"),
                    FechaCargaRelease = Fecha(rd, "FechaCargaReleaseSnapshot"),
                    FechaEntregaRelease = Fecha(rd, "FechaEntregaReleaseSnapshot"),
                    CantidadSolicitada = Entero(rd, "CantidadSolicitada"),
                    CantidadDespachada = Entero(rd, "CantidadDespachada"),
                    CantidadAsignada = Entero(rd, "CantidadAsignada")
                });
            }
        }
        const string sqlCajas = @"
SELECT ec.EmbarqueCajaID,ec.EmbarqueDetalleID,ec.CajaID,ISNULL(c.Etiqueta,N'') AS Etiqueta,c.NumeroCaja,
ISNULL(p.NumeroParte,N'') AS NumeroParte,ISNULL(c.NumeroOF,N'') AS NumeroOF,ISNULL(c.LoteEtiqueta,N'') AS Lote,
ec.CantidadAsignada,ISNULL(ec.EstatusSeleccion,N'') AS EstatusSeleccion
FROM dbo.Logistica_EmbarqueCajas ec
INNER JOIN dbo.AlmacenPT_Cajas c ON c.CajaID=ec.CajaID
INNER JOIN dbo.ERP_Partes p ON p.ParteID=c.ParteID
WHERE ec.EmbarqueID=@EmbarqueID AND (ec.Activo=1 OR ec.EstatusSeleccion=N'Despachada')
ORDER BY ec.EmbarqueDetalleID,c.NumeroCaja;";
        await using (var cmd = new SqlCommand(sqlCajas, cn))
        {
            cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await rd.ReadAsync(cancellationToken))
            {
                vm.CajasAsignadas.Add(new LogisticaCajaAsignadaVm
                {
                    EmbarqueCajaID = Entero(rd, "EmbarqueCajaID"),
                    EmbarqueDetalleID = Entero(rd, "EmbarqueDetalleID"),
                    CajaID = Entero(rd, "CajaID"),
                    Etiqueta = Texto(rd, "Etiqueta"),
                    NumeroCaja = Entero(rd, "NumeroCaja"),
                    NumeroParte = Texto(rd, "NumeroParte"),
                    NumeroOF = Texto(rd, "NumeroOF"),
                    Lote = Texto(rd, "Lote"),
                    CantidadAsignada = Entero(rd, "CantidadAsignada"),
                    Estatus = Texto(rd, "EstatusSeleccion")
                });
            }
        }
        const string sqlDisponibles = @"
SELECT d.EmbarqueDetalleID,c.CajaID,c.Etiqueta,c.NumeroCaja,c.NumeroParte,ISNULL(c.NumeroOF,N'') AS NumeroOF,
ISNULL(c.LoteEtiqueta,N'') AS Lote,ISNULL(c.UbicacionCodigo,N'') AS Ubicacion,c.Disponible
FROM dbo.Logistica_EmbarqueDetalle d
INNER JOIN dbo.vw_Logistica_CajasDisponibles c ON c.ParteID=d.ParteID AND c.SolicitudProduccionID=d.SolicitudProduccionID
WHERE d.EmbarqueID=@EmbarqueID AND d.Activo=1
ORDER BY d.EmbarqueDetalleID,c.NumeroCaja,c.CajaID;";
        await using (var cmd = new SqlCommand(sqlDisponibles, cn))
        {
            cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await rd.ReadAsync(cancellationToken))
            {
                vm.CajasDisponibles.Add(new LogisticaCajaDisponibleVm
                {
                    EmbarqueDetalleID = Entero(rd, "EmbarqueDetalleID"),
                    CajaID = Entero(rd, "CajaID"),
                    Etiqueta = Texto(rd, "Etiqueta"),
                    NumeroCaja = Entero(rd, "NumeroCaja"),
                    NumeroParte = Texto(rd, "NumeroParte"),
                    NumeroOF = Texto(rd, "NumeroOF"),
                    Lote = Texto(rd, "Lote"),
                    Ubicacion = Texto(rd, "Ubicacion"),
                    Disponible = Entero(rd, "Disponible")
                });
            }
        }
        vm.DemandasDisponibles = await CargarDemandasAsync(cn, null, DateTime.Today.AddDays(-30), DateTime.Today.AddMonths(6), true, vm.ClienteID, cancellationToken);
        var usados = vm.Partidas.Where(x => x.ReleaseDetalleID.HasValue).Select(x => x.ReleaseDetalleID!.Value).ToHashSet();
        vm.DemandasDisponibles = vm.DemandasDisponibles.Where(x => !usados.Contains(x.ReleaseDetalleID)).ToList();
        const string sqlHistorial = @"
SELECT FechaEvento,Evento,ISNULL(EstadoAnterior,N'') AS EstadoAnterior,ISNULL(EstadoNuevo,N'') AS EstadoNuevo,
ISNULL(Observaciones,N'') AS Observaciones,ISNULL(UsuarioNombre,N'') AS Usuario
FROM dbo.Logistica_EmbarqueHistorial
WHERE EmbarqueID=@EmbarqueID
ORDER BY FechaEvento DESC,HistorialID DESC;";
        await using (var cmd = new SqlCommand(sqlHistorial, cn))
        {
            cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await rd.ReadAsync(cancellationToken))
            {
                vm.Historial.Add(new LogisticaHistorialVm
                {
                    Fecha = Fecha(rd, "FechaEvento") ?? DateTime.MinValue,
                    Evento = Texto(rd, "Evento"),
                    EstadoAnterior = Texto(rd, "EstadoAnterior"),
                    EstadoNuevo = Texto(rd, "EstadoNuevo"),
                    Observaciones = Texto(rd, "Observaciones"),
                    Usuario = Texto(rd, "Usuario")
                });
            }
        }
        await CargarIncidenciasAsync(cn, vm, cancellationToken);
        await CargarEvidenciasAsync(cn, vm, cancellationToken);
        await CargarDocumentosAsync(cn, vm, cancellationToken);
        await CargarRetornoAsync(cn, vm, cancellationToken);
        CalcularEstadoOperativo(vm);
        return vm;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(10_485_760)]
    public async Task<IActionResult> SubirEvidencia(int embarqueId, string tipoEvidencia, IFormFile? archivo, string? observaciones, CancellationToken cancellationToken)
    {
        var acceso = await ValidarAccesoAsync("Tablero de Logística");
        if (acceso != null) return acceso;

        if (embarqueId <= 0)
        {
            TempData["LogisticaError"] = "El embarque indicado no es válido.";
            return RedirectToAction(nameof(Index));
        }

        tipoEvidencia = tipoEvidencia?.Trim() ?? string.Empty;
        observaciones = observaciones?.Trim();

        var tiposEvidenciaPermitidos = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Carga",
        "Salida",
        "Entrega",
        "Incidencia"
    };

        if (!tiposEvidenciaPermitidos.Contains(tipoEvidencia))
        {
            TempData["LogisticaError"] = "Selecciona un tipo de evidencia válido.";
            return RedirectToAction(nameof(Detalle), new { id = embarqueId });
        }

        if (archivo == null || archivo.Length <= 0)
        {
            TempData["LogisticaError"] = "Selecciona un archivo de evidencia.";
            return RedirectToAction(nameof(Detalle), new { id = embarqueId });
        }

        const long tamanoMaximo = 10 * 1024 * 1024;

        if (archivo.Length > tamanoMaximo)
        {
            TempData["LogisticaError"] = "El archivo excede el tamaño máximo permitido de 10 MB.";
            return RedirectToAction(nameof(Detalle), new { id = embarqueId });
        }

        var nombreOriginal = Path.GetFileName(archivo.FileName);
        var extension = Path.GetExtension(nombreOriginal).ToLowerInvariant();

        var extensionesPermitidas = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".pdf"
    };

        if (!extensionesPermitidas.Contains(extension))
        {
            TempData["LogisticaError"] = "Solo se permiten archivos JPG, JPEG, PNG o PDF.";
            return RedirectToAction(nameof(Detalle), new { id = embarqueId });
        }

        var tiposContenidoPermitidos = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "image/jpeg", "image/pjpeg" },
            [".jpeg"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "image/jpeg", "image/pjpeg" },
            [".png"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "image/png" },
            [".pdf"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "application/pdf" }
        };

        var tipoContenido = archivo.ContentType?.Trim() ?? string.Empty;

        if (!tiposContenidoPermitidos.TryGetValue(extension, out var contenidosPermitidos) || !contenidosPermitidos.Contains(tipoContenido))
        {
            TempData["LogisticaError"] = "El tipo de contenido del archivo no coincide con su extensión.";
            return RedirectToAction(nameof(Detalle), new { id = embarqueId });
        }

        string? rutaFisicaCreada = null;

        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        try
        {
            var header = await ObtenerHeaderAsync(cn, tx, embarqueId, cancellationToken)
                ?? throw new InvalidOperationException("El embarque no existe.");

            if (header.Estatus == "Cancelado")
                throw new InvalidOperationException("No se pueden agregar evidencias a un embarque cancelado.");

            ValidarTipoEvidenciaPorEstado(header.Estatus, tipoEvidencia);

            var carpetaRelativa = Path.Combine("Logistica", "Evidencias", embarqueId.ToString());
            var carpetaFisica = Path.Combine(_environment.ContentRootPath, "App_Data", carpetaRelativa);

            Directory.CreateDirectory(carpetaFisica);

            var nombreFisico = $"{Guid.NewGuid():N}{extension}";
            rutaFisicaCreada = Path.Combine(carpetaFisica, nombreFisico);

            await using (var stream = new FileStream(rutaFisicaCreada, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                await archivo.CopyToAsync(stream, cancellationToken);
            }

            var rutaRelativa = Path.Combine("App_Data", carpetaRelativa, nombreFisico).Replace('\\', '/');

            const string sql = @"
INSERT dbo.Logistica_EmbarqueEvidencias
(
    EmbarqueID,
    TipoEvidencia,
    NombreOriginal,
    NombreFisico,
    RutaRelativa,
    TipoContenido,
    TamanoBytes,
    Observaciones,
    UsuarioID,
    UsuarioNombre,
    FechaCarga,
    Activo
)
VALUES
(
    @EmbarqueID,
    @TipoEvidencia,
    @NombreOriginal,
    @NombreFisico,
    @RutaRelativa,
    @TipoContenido,
    @TamanoBytes,
    @Observaciones,
    @UsuarioID,
    @UsuarioNombre,
    SYSDATETIME(),
    1
);
SELECT CONVERT(int,SCOPE_IDENTITY());";

            int evidenciaId;

            await using (var cmd = new SqlCommand(sql, cn, tx))
            {
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                cmd.Parameters.Add("@TipoEvidencia", SqlDbType.NVarChar, 50).Value = tipoEvidencia;
                cmd.Parameters.Add("@NombreOriginal", SqlDbType.NVarChar, 260).Value = nombreOriginal;
                cmd.Parameters.Add("@NombreFisico", SqlDbType.NVarChar, 260).Value = nombreFisico;
                cmd.Parameters.Add("@RutaRelativa", SqlDbType.NVarChar, 600).Value = rutaRelativa;
                cmd.Parameters.Add("@TipoContenido", SqlDbType.NVarChar, 150).Value = tipoContenido;
                cmd.Parameters.Add("@TamanoBytes", SqlDbType.BigInt).Value = archivo.Length;
                cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1000).Value = Db(string.IsNullOrWhiteSpace(observaciones) ? null : observaciones);
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = Db(UsuarioID);
                cmd.Parameters.Add("@UsuarioNombre", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                evidenciaId = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
            }

            var descripcionHistorial = $"Evidencia EVI-{evidenciaId:000000} agregada. Tipo: {tipoEvidencia}. Archivo: {nombreOriginal}.";

            if (!string.IsNullOrWhiteSpace(observaciones))
                descripcionHistorial += $" Observaciones: {observaciones}";

            await InsertarHistorialAsync(cn, tx, embarqueId, "EVIDENCIA_AGREGADA", header.Estatus, header.Estatus, descripcionHistorial, cancellationToken);

            await tx.CommitAsync(cancellationToken);

            TempData["LogisticaOk"] = $"Evidencia EVI-{evidenciaId:000000} cargada correctamente.";
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(rutaFisicaCreada) && System.IO.File.Exists(rutaFisicaCreada))
            {
                try
                {
                    System.IO.File.Delete(rutaFisicaCreada);
                }
                catch
                {
                }
            }

            TempData["LogisticaError"] = $"No fue posible cargar la evidencia: {ex.Message}";
        }

        return RedirectToAction(nameof(Detalle), new { id = embarqueId });
    }

    [HttpGet]
    public async Task<IActionResult> ObtenerEntregasCliente(int clienteId, CancellationToken cancellationToken)
    {
        var acceso = await ValidarAccesoAsync("Nuevo embarque");
        if (acceso != null) return acceso;
        if (clienteId <= 0) return BadRequest(new { ok = false, mensaje = "Selecciona un cliente válido." });
        await using var cn = await AbrirAsync(cancellationToken);
        var demandas = await CargarDemandasAsync(cn, null, null, null, true, clienteId, cancellationToken);
        var resultado = demandas.Where(x => x.PendienteProgramar > 0).Select(x => new
        {
            releaseDetalleId = x.ReleaseDetalleID,
            releaseId = x.ReleaseID,
            folioRelease = x.FolioRelease,
            clienteId = x.ClienteID,
            cliente = x.Cliente,
            parteId = x.ParteID,
            numeroParte = x.NumeroParte,
            descripcion = x.Descripcion,
            numeroOF = x.NumeroOF,
            fechaCarga = x.FechaCarga?.ToString("yyyy-MM-dd"),
            fechaEntrega = x.FechaEntrega.ToString("yyyy-MM-dd"),
            cantidadRequerida = x.CantidadRequerida,
            cantidadProgramada = x.CantidadProgramada,
            pendienteProgramar = x.PendienteProgramar,
            cajasPTDisponibles = x.CajasPTDisponibles,
            piezasPTDisponibles = x.PiezasPTDisponibles,
            expeditado = x.FechaEntrega.Date < DateTime.Today
        }).ToList();
        return Json(new { ok = true, clienteId, total = resultado.Count, entregas = resultado });
    }

    [HttpGet]
    public async Task<IActionResult> ObtenerCajasEntrega(int releaseDetalleId, CancellationToken cancellationToken)
    {
        var acceso = await ValidarAccesoAsync("Nuevo embarque");
        if (acceso != null) return acceso;
        if (releaseDetalleId <= 0) return BadRequest(new { ok = false, mensaje = "La entrega no es válida." });
        await using var cn = await AbrirAsync(cancellationToken);
        var demanda = await ObtenerDemandaAsync(cn, releaseDetalleId, null, cancellationToken);
        if (demanda == null || demanda.PendienteProgramar <= 0) return NotFound(new { ok = false, mensaje = "La entrega ya no tiene cantidad pendiente." });
        var cajas = await CargarCajasParaDemandaAsync(cn, demanda, cancellationToken);
        return Json(new
        {
            ok = true,
            releaseDetalleId = demanda.ReleaseDetalleID,
            numeroParte = demanda.NumeroParte,
            numeroOF = demanda.NumeroOF,
            pendienteProgramar = demanda.PendienteProgramar,
            cajas = cajas.Select(x => new
            {
                cajaId = x.CajaID,
                etiqueta = x.Etiqueta,
                numeroCaja = x.NumeroCaja,
                numeroParte = x.NumeroParte,
                numeroOF = x.NumeroOF,
                lote = x.Lote,
                ubicacion = x.Ubicacion,
                disponible = x.Disponible
            }).ToList()
        });
    }
    private static void ValidarTipoEvidenciaPorEstado(string estatus, string tipoEvidencia)
    {
        if (string.Equals(tipoEvidencia, "Carga", StringComparison.OrdinalIgnoreCase))
        {
            if (estatus is not "Preparado" and not "Cargado" and not "En ruta" and not "Entregado")
                throw new InvalidOperationException("La evidencia de carga solo puede registrarse a partir de la preparación completa.");

            return;
        }

        if (string.Equals(tipoEvidencia, "Salida", StringComparison.OrdinalIgnoreCase))
        {
            if (estatus is not "Cargado" and not "En ruta" and not "Entregado")
                throw new InvalidOperationException("La evidencia de salida solo puede registrarse cuando la carga ya fue confirmada.");

            return;
        }

        if (string.Equals(tipoEvidencia, "Entrega", StringComparison.OrdinalIgnoreCase))
        {
            if (estatus is not "En ruta" and not "Entregado")
                throw new InvalidOperationException("La evidencia de entrega solo puede registrarse durante el tránsito o después de confirmar la entrega.");

            return;
        }

        if (string.Equals(tipoEvidencia, "Incidencia", StringComparison.OrdinalIgnoreCase))
        {
            if (estatus == "Cancelado")
                throw new InvalidOperationException("No se pueden registrar evidencias de incidencia en un embarque cancelado.");

            return;
        }

        throw new InvalidOperationException("El tipo de evidencia no es válido.");
    }

    [HttpGet]
    public async Task<IActionResult> VerEvidencia(int id, CancellationToken cancellationToken)
    {
        var acceso = await ValidarAccesoAsync("Tablero de Logística");
        if (acceso != null) return acceso;

        if (id <= 0)
            return NotFound();

        await using var cn = await AbrirAsync(cancellationToken);

        const string sql = @"
SELECT
    EvidenciaID,
    EmbarqueID,
    NombreOriginal,
    NombreFisico,
    RutaRelativa,
    TipoContenido,
    TamanoBytes
FROM dbo.Logistica_EmbarqueEvidencias
WHERE EvidenciaID=@EvidenciaID
  AND Activo=1;";

        string nombreOriginal;
        string nombreFisico;
        string tipoContenido;
        string rutaRelativa;

        await using (var cmd = new SqlCommand(sql, cn))
        {
            cmd.Parameters.Add("@EvidenciaID", SqlDbType.Int).Value = id;

            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);

            if (!await rd.ReadAsync(cancellationToken))
                return NotFound();

            nombreOriginal = Texto(rd, "NombreOriginal");
            nombreFisico = Texto(rd, "NombreFisico");
            rutaRelativa = Texto(rd, "RutaRelativa");
            tipoContenido = Texto(rd, "TipoContenido");
        }

        var rutaFisica = ObtenerRutaFisicaEvidencia(rutaRelativa, nombreFisico);

        if (!System.IO.File.Exists(rutaFisica))
            return NotFound();

        var stream = new FileStream(rutaFisica, FileMode.Open, FileAccess.Read, FileShare.Read);

        if (string.IsNullOrWhiteSpace(tipoContenido))
            tipoContenido = "application/octet-stream";

        Response.Headers.ContentDisposition = $"inline; filename=\"{SanitizarNombreCabecera(nombreOriginal)}\"";

        return File(stream, tipoContenido);
    }

    [HttpGet]
    public async Task<IActionResult> DescargarEvidencia(int id, CancellationToken cancellationToken)
    {
        var acceso = await ValidarAccesoAsync("Tablero de Logística");
        if (acceso != null) return acceso;

        if (id <= 0)
            return NotFound();

        await using var cn = await AbrirAsync(cancellationToken);

        const string sql = @"
SELECT
    EvidenciaID,
    EmbarqueID,
    NombreOriginal,
    NombreFisico,
    RutaRelativa,
    TipoContenido
FROM dbo.Logistica_EmbarqueEvidencias
WHERE EvidenciaID=@EvidenciaID
  AND Activo=1;";

        string nombreOriginal;
        string nombreFisico;
        string rutaRelativa;
        string tipoContenido;

        await using (var cmd = new SqlCommand(sql, cn))
        {
            cmd.Parameters.Add("@EvidenciaID", SqlDbType.Int).Value = id;

            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);

            if (!await rd.ReadAsync(cancellationToken))
                return NotFound();

            nombreOriginal = Texto(rd, "NombreOriginal");
            nombreFisico = Texto(rd, "NombreFisico");
            rutaRelativa = Texto(rd, "RutaRelativa");
            tipoContenido = Texto(rd, "TipoContenido");
        }

        var rutaFisica = ObtenerRutaFisicaEvidencia(rutaRelativa, nombreFisico);

        if (!System.IO.File.Exists(rutaFisica))
            return NotFound();

        if (string.IsNullOrWhiteSpace(tipoContenido))
            tipoContenido = "application/octet-stream";

        var stream = new FileStream(rutaFisica, FileMode.Open, FileAccess.Read, FileShare.Read);

        return File(stream, tipoContenido, string.IsNullOrWhiteSpace(nombreOriginal) ? nombreFisico : nombreOriginal);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EliminarEvidencia(int evidenciaId, int embarqueId, string? motivo, CancellationToken cancellationToken)
    {
        var acceso = await ValidarAccesoAsync("Tablero de Logística");
        if (acceso != null) return acceso;

        if (evidenciaId <= 0 || embarqueId <= 0)
        {
            TempData["LogisticaError"] = "La evidencia indicada no es válida.";
            return embarqueId > 0
                ? RedirectToAction(nameof(Detalle), new { id = embarqueId })
                : RedirectToAction(nameof(Index));
        }

        motivo = motivo?.Trim();

        if (string.IsNullOrWhiteSpace(motivo))
        {
            TempData["LogisticaError"] = "Debes indicar el motivo para retirar la evidencia.";
            return RedirectToAction(nameof(Detalle), new { id = embarqueId });
        }

        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        try
        {
            var header = await ObtenerHeaderAsync(cn, tx, embarqueId, cancellationToken)
                ?? throw new InvalidOperationException("El embarque no existe.");

            const string sqlObtener = @"
SELECT
    EvidenciaID,
    TipoEvidencia,
    NombreOriginal,
    Activo
FROM dbo.Logistica_EmbarqueEvidencias WITH (UPDLOCK,HOLDLOCK)
WHERE EvidenciaID=@EvidenciaID
  AND EmbarqueID=@EmbarqueID;";

            string tipoEvidencia;
            string nombreOriginal;
            bool activo;

            await using (var cmd = new SqlCommand(sqlObtener, cn, tx))
            {
                cmd.Parameters.Add("@EvidenciaID", SqlDbType.Int).Value = evidenciaId;
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;

                await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);

                if (!await rd.ReadAsync(cancellationToken))
                    throw new InvalidOperationException("La evidencia no existe o no pertenece a este embarque.");

                tipoEvidencia = Texto(rd, "TipoEvidencia");
                nombreOriginal = Texto(rd, "NombreOriginal");
                activo = Booleano(rd, "Activo");
            }

            if (!activo)
                throw new InvalidOperationException("La evidencia ya se encuentra retirada.");

            const string sqlDesactivar = @"
UPDATE dbo.Logistica_EmbarqueEvidencias
SET Activo=0
WHERE EvidenciaID=@EvidenciaID
  AND EmbarqueID=@EmbarqueID
  AND Activo=1;
SELECT @@ROWCOUNT;";

            await using (var cmd = new SqlCommand(sqlDesactivar, cn, tx))
            {
                cmd.Parameters.Add("@EvidenciaID", SqlDbType.Int).Value = evidenciaId;
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;

                var afectados = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));

                if (afectados == 0)
                    throw new InvalidOperationException("La evidencia cambió mientras intentabas retirarla.");
            }

            await InsertarHistorialAsync(
                cn,
                tx,
                embarqueId,
                "EVIDENCIA_RETIRADA",
                header.Estatus,
                header.Estatus,
                $"Evidencia EVI-{evidenciaId:000000} retirada. Tipo: {tipoEvidencia}. Archivo: {nombreOriginal}. Motivo: {motivo}",
                cancellationToken);

            await tx.CommitAsync(cancellationToken);

            TempData["LogisticaOk"] = $"Evidencia EVI-{evidenciaId:000000} retirada correctamente. El archivo permanece conservado para auditoría.";
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            TempData["LogisticaError"] = $"No fue posible retirar la evidencia: {ex.Message}";
        }

        return RedirectToAction(nameof(Detalle), new { id = embarqueId });
    }

    private async Task CargarEvidenciasAsync(SqlConnection cn, LogisticaDetalleVm vm, CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT
    EvidenciaID,
    EmbarqueID,
    ISNULL(TipoEvidencia,N'') AS TipoEvidencia,
    ISNULL(NombreOriginal,N'') AS NombreOriginal,
    ISNULL(TipoContenido,N'') AS TipoContenido,
    ISNULL(TamanoBytes,0) AS TamanoBytes,
    ISNULL(Observaciones,N'') AS Observaciones,
    UsuarioID,
    ISNULL(UsuarioNombre,N'') AS UsuarioNombre,
    FechaCarga
FROM dbo.Logistica_EmbarqueEvidencias
WHERE EmbarqueID=@EmbarqueID
  AND Activo=1
ORDER BY FechaCarga DESC,EvidenciaID DESC;";

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = vm.EmbarqueID;

        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);

        while (await rd.ReadAsync(cancellationToken))
        {
            vm.Evidencias.Add(new LogisticaEvidenciaVm
            {
                EvidenciaID = Entero(rd, "EvidenciaID"),
                EmbarqueID = Entero(rd, "EmbarqueID"),
                TipoEvidencia = Texto(rd, "TipoEvidencia"),
                NombreOriginal = Texto(rd, "NombreOriginal"),
                TipoContenido = Texto(rd, "TipoContenido"),
                TamanoBytes = EnteroLargo(rd, "TamanoBytes"),
                Observaciones = Texto(rd, "Observaciones"),
                UsuarioID = EnteroNullable(rd, "UsuarioID"),
                UsuarioNombre = Texto(rd, "UsuarioNombre"),
                FechaCarga = Fecha(rd, "FechaCarga") ?? DateTime.MinValue
            });
        }
    }

    private string ObtenerRutaFisicaEvidencia(string rutaRelativa, string nombreFisico)
    {
        var raizPermitida = Path.GetFullPath(Path.Combine(_environment.ContentRootPath, "App_Data", "Logistica", "Evidencias"));

        string rutaFisica;

        if (!string.IsNullOrWhiteSpace(rutaRelativa))
        {
            var relativa = rutaRelativa.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);

            if (relativa.StartsWith($"App_Data{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                relativa = relativa[("App_Data" + Path.DirectorySeparatorChar).Length..];

            rutaFisica = Path.GetFullPath(Path.Combine(_environment.ContentRootPath, "App_Data", relativa));
        }
        else
        {
            rutaFisica = Path.GetFullPath(Path.Combine(raizPermitida, nombreFisico));
        }

        if (!rutaFisica.StartsWith(raizPermitida + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("La ruta de la evidencia no es válida.");

        return rutaFisica;
    }

    private static string SanitizarNombreCabecera(string? nombre)
    {
        nombre = Path.GetFileName(nombre ?? "evidencia");
        return nombre.Replace("\"", string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);
    }


    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(12 * 1024 * 1024)]
    public async Task<IActionResult> SubirDocumento(int embarqueId, string tipoDocumento, IFormFile? archivo, string? observaciones, CancellationToken cancellationToken)
    {
        var acceso = await ValidarAccesoAsync("Tablero de Logística");
        if (acceso != null) return acceso;
        if (embarqueId <= 0)
        {
            TempData["LogisticaError"] = "El embarque indicado no es válido.";
            return RedirectToAction(nameof(Index));
        }
        tipoDocumento = NormalizarTipoDocumento(tipoDocumento);
        observaciones = observaciones?.Trim();
        if (string.IsNullOrWhiteSpace(tipoDocumento) || !TiposDocumentoPermitidos.Contains(tipoDocumento))
        {
            TempData["LogisticaError"] = "Selecciona un tipo de documento válido.";
            return RedirectToAction(nameof(Detalle), new { id = embarqueId });
        }
        if (archivo == null || archivo.Length <= 0)
        {
            TempData["LogisticaError"] = "Selecciona un archivo.";
            return RedirectToAction(nameof(Detalle), new { id = embarqueId });
        }
        const long tamanoMaximo = 10 * 1024 * 1024;
        if (archivo.Length > tamanoMaximo)
        {
            TempData["LogisticaError"] = "El archivo excede el tamaño máximo permitido de 10 MB.";
            return RedirectToAction(nameof(Detalle), new { id = embarqueId });
        }
        var nombreOriginal = Path.GetFileName(archivo.FileName);
        var extension = Path.GetExtension(nombreOriginal).ToLowerInvariant();
        var extensionesPermitidas = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".pdf", ".xml", ".xlsx", ".xls", ".docx", ".doc", ".jpg", ".jpeg", ".png" };
        if (!extensionesPermitidas.Contains(extension))
        {
            TempData["LogisticaError"] = "Solo se permiten archivos PDF, XML, Excel, Word, JPG, JPEG o PNG.";
            return RedirectToAction(nameof(Detalle), new { id = embarqueId });
        }
        string? rutaFisicaCreada = null;
        var tipoContenido = archivo.ContentType?.Trim() ?? "application/octet-stream";
        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var header = await ObtenerHeaderAsync(cn, tx, embarqueId, cancellationToken) ?? throw new InvalidOperationException("El embarque no existe.");
            if (header.Estatus == "Cancelado") throw new InvalidOperationException("No se pueden agregar documentos a un embarque cancelado.");
            const string sqlOperacion = @"
SELECT ISNULL(NULLIF(LTRIM(RTRIM(TipoOperacion)),N''),N'Nacional') AS TipoOperacion,
ISNULL(NULLIF(LTRIM(RTRIM(ModalidadEnvio)),N''),N'') AS ModalidadEnvio,
PasaAduana
FROM dbo.Logistica_Embarques WITH(UPDLOCK,HOLDLOCK)
WHERE EmbarqueID=@EmbarqueID AND Activo=1;";
            string tipoOperacion;
            string modalidadEnvio;
            bool? pasaAduana;
            await using (var cmd = new SqlCommand(sqlOperacion, cn, tx))
            {
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
                if (!await rd.ReadAsync(cancellationToken)) throw new InvalidOperationException("El embarque ya no está disponible.");
                tipoOperacion = NormalizarTipoOperacion(Texto(rd, "TipoOperacion"));
                modalidadEnvio = NormalizarModalidadEnvio(Texto(rd, "ModalidadEnvio"));
                pasaAduana = rd.IsDBNull(rd.GetOrdinal("PasaAduana")) ? null : Convert.ToBoolean(rd["PasaAduana"]);
            }
            if (string.IsNullOrWhiteSpace(tipoOperacion)) tipoOperacion = "Nacional";
            var definiciones = ObtenerDefinicionDocumentos(tipoOperacion, modalidadEnvio, pasaAduana);
            var esObligatorio = false;
            var areaResponsable = ObtenerAreaDocumento(tipoDocumento);
            if (tipoDocumento is "Commercial Invoice" or "Carta Porte")
            {
                var definicionAlternativa = definiciones.FirstOrDefault(x => x.TipoDocumento == "Commercial Invoice / Carta Porte");
                if (!string.IsNullOrWhiteSpace(definicionAlternativa.TipoDocumento))
                {
                    esObligatorio = definicionAlternativa.Obligatorio;
                    areaResponsable = definicionAlternativa.AreaResponsable;
                }
            }
            else
            {
                var definicion = definiciones.FirstOrDefault(x => string.Equals(x.TipoDocumento, tipoDocumento, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(definicion.TipoDocumento))
                {
                    esObligatorio = definicion.Obligatorio;
                    areaResponsable = definicion.AreaResponsable;
                }
            }
            const string sqlDuplicado = @"SELECT COUNT_BIG(*) FROM dbo.Logistica_EmbarqueDocumentos WITH(UPDLOCK,HOLDLOCK) WHERE EmbarqueID=@EmbarqueID AND TipoDocumento=@TipoDocumento AND Activo=1;";
            await using (var cmd = new SqlCommand(sqlDuplicado, cn, tx))
            {
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                cmd.Parameters.Add("@TipoDocumento", SqlDbType.NVarChar, 80).Value = tipoDocumento;
                if (Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken)) > 0) throw new InvalidOperationException($"Ya existe un documento activo de tipo {tipoDocumento}. Retíralo antes de cargar una nueva versión.");
            }
            var carpetaRelativa = Path.Combine("Logistica", "Documentos", embarqueId.ToString());
            var carpetaFisica = Path.Combine(_environment.ContentRootPath, "App_Data", carpetaRelativa);
            Directory.CreateDirectory(carpetaFisica);
            var nombreFisico = $"{Guid.NewGuid():N}{extension}";
            rutaFisicaCreada = Path.Combine(carpetaFisica, nombreFisico);
            await using (var stream = new FileStream(rutaFisicaCreada, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await archivo.CopyToAsync(stream, cancellationToken);
            var rutaRelativa = Path.Combine("App_Data", carpetaRelativa, nombreFisico).Replace('\\', '/');
            const string sqlInsert = @"
INSERT dbo.Logistica_EmbarqueDocumentos
(EmbarqueID,TipoDocumento,NombreOriginal,NombreFisico,RutaRelativa,TipoContenido,TamanoBytes,AreaResponsable,EsObligatorio,Validado,UsuarioCargaID,UsuarioCargaNombre,FechaCarga,UsuarioValidaID,UsuarioValidaNombre,FechaValidacion,Observaciones,Activo,FechaCreacion)
VALUES
(@EmbarqueID,@TipoDocumento,@NombreOriginal,@NombreFisico,@RutaRelativa,@TipoContenido,@TamanoBytes,@AreaResponsable,@EsObligatorio,0,@UsuarioID,@UsuarioNombre,SYSDATETIME(),NULL,NULL,NULL,@Observaciones,1,SYSDATETIME());
SELECT CONVERT(int,SCOPE_IDENTITY());";
            int documentoId;
            await using (var cmd = new SqlCommand(sqlInsert, cn, tx))
            {
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                cmd.Parameters.Add("@TipoDocumento", SqlDbType.NVarChar, 80).Value = tipoDocumento;
                cmd.Parameters.Add("@NombreOriginal", SqlDbType.NVarChar, 260).Value = nombreOriginal;
                cmd.Parameters.Add("@NombreFisico", SqlDbType.NVarChar, 260).Value = nombreFisico;
                cmd.Parameters.Add("@RutaRelativa", SqlDbType.NVarChar, 600).Value = rutaRelativa;
                cmd.Parameters.Add("@TipoContenido", SqlDbType.NVarChar, 150).Value = tipoContenido;
                cmd.Parameters.Add("@TamanoBytes", SqlDbType.BigInt).Value = archivo.Length;
                cmd.Parameters.Add("@AreaResponsable", SqlDbType.NVarChar, 80).Value = areaResponsable;
                cmd.Parameters.Add("@EsObligatorio", SqlDbType.Bit).Value = esObligatorio;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = Db(UsuarioID);
                cmd.Parameters.Add("@UsuarioNombre", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1000).Value = Db(string.IsNullOrWhiteSpace(observaciones) ? null : observaciones);
                documentoId = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
            }
            await InsertarHistorialAsync(cn, tx, embarqueId, "DOCUMENTO_AGREGADO", header.Estatus, header.Estatus, $"Documento DOC-{documentoId:000000} agregado. Tipo: {tipoDocumento}. Archivo: {nombreOriginal}. Responsable: {areaResponsable}.", cancellationToken);
            await tx.CommitAsync(cancellationToken);
            TempData["LogisticaOk"] = $"Documento DOC-{documentoId:000000} cargado correctamente. Queda pendiente de validación.";
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(rutaFisicaCreada) && System.IO.File.Exists(rutaFisicaCreada))
            {
                try { System.IO.File.Delete(rutaFisicaCreada); } catch { }
            }
            TempData["LogisticaError"] = $"No fue posible cargar el documento: {ex.Message}";
        }
        return RedirectToAction(nameof(Detalle), new { id = embarqueId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ValidarDocumento(int documentoId, int embarqueId, CancellationToken cancellationToken)
    {
        var acceso = await ValidarAccesoAsync("Tablero de Logística");
        if (acceso != null) return acceso;
        if (documentoId <= 0 || embarqueId <= 0)
        {
            TempData["LogisticaError"] = "El documento indicado no es válido.";
            return embarqueId > 0 ? RedirectToAction(nameof(Detalle), new { id = embarqueId }) : RedirectToAction(nameof(Index));
        }

        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var header = await ObtenerHeaderAsync(cn, tx, embarqueId, cancellationToken)
                ?? throw new InvalidOperationException("El embarque no existe.");

            const string sql = @"
UPDATE dbo.Logistica_EmbarqueDocumentos
SET Validado=1,UsuarioValidaID=@UsuarioID,UsuarioValidaNombre=@UsuarioNombre,FechaValidacion=SYSDATETIME()
WHERE EmbarqueDocumentoID=@DocumentoID AND EmbarqueID=@EmbarqueID AND Activo=1 AND Validado=0;
SELECT @@ROWCOUNT;";
            await using (var cmd = new SqlCommand(sql, cn, tx))
            {
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = Db(UsuarioID);
                cmd.Parameters.Add("@UsuarioNombre", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@DocumentoID", SqlDbType.Int).Value = documentoId;
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                if (Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) == 0)
                    throw new InvalidOperationException("El documento no existe, ya fue validado o cambió durante la operación.");
            }

            await InsertarHistorialAsync(cn, tx, embarqueId, "DOCUMENTO_VALIDADO", header.Estatus, header.Estatus,
                $"Documento DOC-{documentoId:000000} validado por {UsuarioNombre}.", cancellationToken);
            await tx.CommitAsync(cancellationToken);
            TempData["LogisticaOk"] = "Documento validado correctamente.";
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            TempData["LogisticaError"] = $"No fue posible validar el documento: {ex.Message}";
        }
        return RedirectToAction(nameof(Detalle), new { id = embarqueId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RetirarDocumento(int documentoId, int embarqueId, string? motivo, CancellationToken cancellationToken)
    {
        var acceso = await ValidarAccesoAsync("Tablero de Logística");
        if (acceso != null) return acceso;
        motivo = motivo?.Trim();
        if (documentoId <= 0 || embarqueId <= 0 || string.IsNullOrWhiteSpace(motivo))
        {
            TempData["LogisticaError"] = "Documento y motivo de retiro son obligatorios.";
            return embarqueId > 0 ? RedirectToAction(nameof(Detalle), new { id = embarqueId }) : RedirectToAction(nameof(Index));
        }

        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var header = await ObtenerHeaderAsync(cn, tx, embarqueId, cancellationToken)
                ?? throw new InvalidOperationException("El embarque no existe.");
            const string sql = @"
UPDATE dbo.Logistica_EmbarqueDocumentos
SET Activo=0,
Observaciones=CASE WHEN NULLIF(LTRIM(RTRIM(ISNULL(Observaciones,N''))),N'') IS NULL THEN CONCAT(N'Retirado: ',@Motivo)
ELSE CONCAT(Observaciones,NCHAR(13),NCHAR(10),N'Retirado: ',@Motivo) END
WHERE EmbarqueDocumentoID=@DocumentoID AND EmbarqueID=@EmbarqueID AND Activo=1;
SELECT @@ROWCOUNT;";
            await using (var cmd = new SqlCommand(sql, cn, tx))
            {
                cmd.Parameters.Add("@Motivo", SqlDbType.NVarChar, 1000).Value = motivo;
                cmd.Parameters.Add("@DocumentoID", SqlDbType.Int).Value = documentoId;
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                if (Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) == 0)
                    throw new InvalidOperationException("El documento no existe, ya fue retirado o cambió durante la operación.");
            }

            await InsertarHistorialAsync(cn, tx, embarqueId, "DOCUMENTO_RETIRADO", header.Estatus, header.Estatus,
                $"Documento DOC-{documentoId:000000} retirado. Motivo: {motivo}", cancellationToken);
            await tx.CommitAsync(cancellationToken);
            TempData["LogisticaOk"] = "Documento retirado. El archivo físico permanece conservado para auditoría.";
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            TempData["LogisticaError"] = $"No fue posible retirar el documento: {ex.Message}";
        }
        return RedirectToAction(nameof(Detalle), new { id = embarqueId });
    }

    [HttpGet]
    public async Task<IActionResult> VerDocumento(int id, CancellationToken cancellationToken)
    {
        var acceso = await ValidarAccesoAsync("Tablero de Logística");
        if (acceso != null) return acceso;
        if (id <= 0) return NotFound();

        await using var cn = await AbrirAsync(cancellationToken);
        const string sql = @"SELECT NombreOriginal,NombreFisico,RutaRelativa,ISNULL(TipoContenido,N'application/octet-stream') AS TipoContenido FROM dbo.Logistica_EmbarqueDocumentos WHERE EmbarqueDocumentoID=@DocumentoID AND Activo=1;";
        string nombreOriginal, nombreFisico, rutaRelativa, tipoContenido;
        await using (var cmd = new SqlCommand(sql, cn))
        {
            cmd.Parameters.Add("@DocumentoID", SqlDbType.Int).Value = id;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await rd.ReadAsync(cancellationToken)) return NotFound();
            nombreOriginal = Texto(rd, "NombreOriginal");
            nombreFisico = Texto(rd, "NombreFisico");
            rutaRelativa = Texto(rd, "RutaRelativa");
            tipoContenido = Texto(rd, "TipoContenido");
        }

        var rutaFisica = ObtenerRutaFisicaDocumento(rutaRelativa, nombreFisico);
        if (!System.IO.File.Exists(rutaFisica)) return NotFound();
        var stream = new FileStream(rutaFisica, FileMode.Open, FileAccess.Read, FileShare.Read);
        Response.Headers.ContentDisposition = $"inline; filename=\"{SanitizarNombreCabecera(nombreOriginal)}\"";
        return File(stream, string.IsNullOrWhiteSpace(tipoContenido) ? "application/octet-stream" : tipoContenido);
    }

    private async Task CargarDocumentosAsync(SqlConnection cn, LogisticaDetalleVm vm, CancellationToken cancellationToken)
    {
        vm.Documentos.Clear();
        vm.DocumentosRequeridos.Clear();
        const string sql = @"
SELECT EmbarqueDocumentoID,EmbarqueID,ISNULL(TipoDocumento,N'') AS TipoDocumento,ISNULL(NombreOriginal,N'') AS NombreOriginal,
ISNULL(NombreFisico,N'') AS NombreFisico,ISNULL(RutaRelativa,N'') AS RutaRelativa,ISNULL(TipoContenido,N'') AS TipoContenido,
ISNULL(TamanoBytes,0) AS TamanoBytes,ISNULL(AreaResponsable,N'') AS AreaResponsable,ISNULL(EsObligatorio,0) AS EsObligatorio,
ISNULL(Validado,0) AS Validado,UsuarioCargaID,ISNULL(UsuarioCargaNombre,N'') AS UsuarioCargaNombre,FechaCarga,UsuarioValidaID,
ISNULL(UsuarioValidaNombre,N'') AS UsuarioValidaNombre,FechaValidacion,ISNULL(Observaciones,N'') AS Observaciones,ISNULL(Activo,0) AS Activo
FROM dbo.Logistica_EmbarqueDocumentos
WHERE EmbarqueID=@EmbarqueID AND Activo=1
ORDER BY FechaCarga DESC,EmbarqueDocumentoID DESC;";
        await using (var cmd = new SqlCommand(sql, cn))
        {
            cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = vm.EmbarqueID;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await rd.ReadAsync(cancellationToken))
            {
                vm.Documentos.Add(new LogisticaDocumentoVm
                {
                    EmbarqueDocumentoID = Entero(rd, "EmbarqueDocumentoID"),
                    EmbarqueID = Entero(rd, "EmbarqueID"),
                    TipoDocumento = Texto(rd, "TipoDocumento"),
                    NombreOriginal = Texto(rd, "NombreOriginal"),
                    NombreFisico = Texto(rd, "NombreFisico"),
                    RutaRelativa = Texto(rd, "RutaRelativa"),
                    TipoContenido = Texto(rd, "TipoContenido"),
                    TamanoBytes = EnteroLargo(rd, "TamanoBytes"),
                    AreaResponsable = Texto(rd, "AreaResponsable"),
                    EsObligatorio = Booleano(rd, "EsObligatorio"),
                    Validado = Booleano(rd, "Validado"),
                    UsuarioCargaID = EnteroNullable(rd, "UsuarioCargaID"),
                    UsuarioCargaNombre = Texto(rd, "UsuarioCargaNombre"),
                    FechaCarga = Fecha(rd, "FechaCarga") ?? DateTime.MinValue,
                    UsuarioValidaID = EnteroNullable(rd, "UsuarioValidaID"),
                    UsuarioValidaNombre = Texto(rd, "UsuarioValidaNombre"),
                    FechaValidacion = Fecha(rd, "FechaValidacion"),
                    Observaciones = Texto(rd, "Observaciones"),
                    Activo = Booleano(rd, "Activo")
                });
            }
        }
        foreach (var definicion in ObtenerDefinicionDocumentos(vm.TipoOperacion, vm.ModalidadEnvio, vm.PasaAduana))
        {
            LogisticaDocumentoVm? documento;
            if (definicion.TipoDocumento == "Commercial Invoice / Carta Porte")
            {
                documento = vm.Documentos.Where(x => x.TipoDocumento.Equals("Commercial Invoice", StringComparison.OrdinalIgnoreCase) || x.TipoDocumento.Equals("Carta Porte", StringComparison.OrdinalIgnoreCase)).OrderByDescending(x => x.Validado).ThenByDescending(x => x.FechaCarga).FirstOrDefault();
            }
            else
            {
                documento = vm.Documentos.Where(x => x.TipoDocumento.Equals(definicion.TipoDocumento, StringComparison.OrdinalIgnoreCase)).OrderByDescending(x => x.Validado).ThenByDescending(x => x.FechaCarga).FirstOrDefault();
            }
            vm.DocumentosRequeridos.Add(new LogisticaDocumentoRequeridoVm
            {
                TipoDocumento = definicion.TipoDocumento,
                AreaResponsable = definicion.AreaResponsable,
                Obligatorio = definicion.Obligatorio,
                Cargado = documento != null,
                Validado = documento?.Validado == true,
                Documento = documento
            });
        }
    }

    private static async Task<List<string>> ObtenerDocumentosFaltantesAsync(SqlConnection cn, int embarqueId, string tipoOperacion, string? modalidadEnvio, bool? pasaAduana, CancellationToken cancellationToken)
    {
        var requeridos = ObtenerDefinicionDocumentos(tipoOperacion, modalidadEnvio, pasaAduana).Where(x => x.Obligatorio).ToList();
        var validados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        const string sql = @"SELECT DISTINCT ISNULL(TipoDocumento,N'') AS TipoDocumento FROM dbo.Logistica_EmbarqueDocumentos WHERE EmbarqueID=@EmbarqueID AND Activo=1 AND Validado=1;";
        await using (var cmd = new SqlCommand(sql, cn))
        {
            cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await rd.ReadAsync(cancellationToken))
            {
                var tipo = NormalizarTipoDocumento(Texto(rd, "TipoDocumento"));
                if (!string.IsNullOrWhiteSpace(tipo)) validados.Add(tipo);
            }
        }
        var faltantes = new List<string>();
        foreach (var requerido in requeridos)
        {
            if (requerido.TipoDocumento == "Commercial Invoice / Carta Porte")
            {
                if (!validados.Contains("Commercial Invoice") && !validados.Contains("Carta Porte")) faltantes.Add("Commercial Invoice o Carta Porte");
                continue;
            }
            if (!validados.Contains(requerido.TipoDocumento)) faltantes.Add(requerido.TipoDocumento);
        }
        return faltantes;
    }

    private static List<(string TipoDocumento, string AreaResponsable, bool Obligatorio)> ObtenerDefinicionDocumentos(string? tipoOperacion, string? modalidadEnvio = null, bool? pasaAduana = null)
    {
        var tipo = NormalizarTipoOperacion(tipoOperacion);
        var modalidad = NormalizarModalidadEnvio(modalidadEnvio);
        var documentos = new List<(string TipoDocumento, string AreaResponsable, bool Obligatorio)>();
        if (tipo == "Nacional")
        {
            documentos.Add(("Factura", "Finanzas", true));
            documentos.Add(("Remisión", "Almacén", false));
            return documentos;
        }
        if (tipo == "Exportacion")
        {
            documentos.Add(("Commercial Invoice / Carta Porte", "Planeación", true));
            documentos.Add(("Packing List", "Planeación", true));
            documentos.Add(("Factura", "Finanzas", true));
            if (pasaAduana == true && (modalidad == "Aereo" || modalidad == "Maritimo")) documentos.Add(("Carta de instrucciones", "Planeación", true));
            if (pasaAduana == false) documentos.Add(("Guía", "Logística", true));
            documentos.Add(("XML", "Finanzas", false));
            documentos.Add(("Booking", "Planeación", false));
            documentos.Add(("Pedimento", "Aduanas", false));
        }
        return documentos;
    }

    private static readonly HashSet<string> TiposDocumentoPermitidos = new(StringComparer.OrdinalIgnoreCase)
{
    "Commercial Invoice","Carta Porte","Packing List","Factura","Remisión","Guía","XML","Carta de instrucciones","Booking","Pedimento","Otro"
};

    private static string NormalizarTipoDocumento(string? valor)
    {
        valor = valor?.Trim() ?? string.Empty;
        if (valor.Equals("Commercial Invoice", StringComparison.OrdinalIgnoreCase) || valor.Equals("Commercial List", StringComparison.OrdinalIgnoreCase) || valor.Equals("Comercial List", StringComparison.OrdinalIgnoreCase)) return "Commercial Invoice";
        if (valor.Equals("Carta Porte", StringComparison.OrdinalIgnoreCase)) return "Carta Porte";
        if (valor.Equals("Packing List", StringComparison.OrdinalIgnoreCase)) return "Packing List";
        if (valor.Equals("Factura", StringComparison.OrdinalIgnoreCase)) return "Factura";
        if (valor.Equals("Remision", StringComparison.OrdinalIgnoreCase) || valor.Equals("Remisión", StringComparison.OrdinalIgnoreCase)) return "Remisión";
        if (valor.Equals("Guia", StringComparison.OrdinalIgnoreCase) || valor.Equals("Guía", StringComparison.OrdinalIgnoreCase)) return "Guía";
        if (valor.Equals("XML", StringComparison.OrdinalIgnoreCase)) return "XML";
        if (valor.Equals("Carta de instrucciones", StringComparison.OrdinalIgnoreCase)) return "Carta de instrucciones";
        if (valor.Equals("Booking", StringComparison.OrdinalIgnoreCase)) return "Booking";
        if (valor.Equals("Pedimento", StringComparison.OrdinalIgnoreCase)) return "Pedimento";
        if (valor.Equals("Otro", StringComparison.OrdinalIgnoreCase)) return "Otro";
        return string.Empty;
    }

    private static string ObtenerAreaDocumento(string tipoDocumento) => tipoDocumento switch
    {
        "Commercial Invoice" => "Planeación",
        "Carta Porte" => "Planeación",
        "Packing List" => "Planeación",
        "Factura" => "Finanzas",
        "XML" => "Finanzas",
        "Carta de instrucciones" => "Planeación",
        "Booking" => "Planeación",
        "Pedimento" => "Aduanas",
        "Remisión" => "Almacén",
        "Guía" => "Logística",
        _ => "Logística"
    };
    private static string NormalizarTipoOperacion(string? valor)
    {
        valor = valor?.Trim() ?? string.Empty;
        if (valor.Equals("Nacional", StringComparison.OrdinalIgnoreCase)) return "Nacional";
        if (valor.Equals("Exportacion", StringComparison.OrdinalIgnoreCase) || valor.Equals("Exportación", StringComparison.OrdinalIgnoreCase)) return "Exportacion";
        return string.Empty;
    }

    private static string NormalizarFormaEnvio(string? valor)
    {
        valor = valor?.Trim() ?? string.Empty;
        if (valor.Equals("Interno", StringComparison.OrdinalIgnoreCase) || valor.Equals("Interna", StringComparison.OrdinalIgnoreCase) || valor.Equals("Local", StringComparison.OrdinalIgnoreCase)) return "Interno";
        if (valor.Equals("Paqueteria", StringComparison.OrdinalIgnoreCase) || valor.Equals("Paquetería", StringComparison.OrdinalIgnoreCase) || valor.Equals("Transportista", StringComparison.OrdinalIgnoreCase)) return "Paqueteria";
        return string.Empty;
    }

    private static string NormalizarModalidadEnvio(string? valor)
    {
        valor = valor?.Trim() ?? string.Empty;
        if (valor.Equals("Terrestre", StringComparison.OrdinalIgnoreCase)) return "Terrestre";
        if (valor.Equals("Aereo", StringComparison.OrdinalIgnoreCase) || valor.Equals("Aéreo", StringComparison.OrdinalIgnoreCase)) return "Aereo";
        if (valor.Equals("Maritimo", StringComparison.OrdinalIgnoreCase) || valor.Equals("Marítimo", StringComparison.OrdinalIgnoreCase)) return "Maritimo";
        return string.Empty;
    }
    private string ObtenerRutaFisicaDocumento(string rutaRelativa, string nombreFisico)
    {
        var raizPermitida = Path.GetFullPath(Path.Combine(_environment.ContentRootPath, "App_Data", "Logistica", "Documentos"));
        string rutaFisica;
        if (!string.IsNullOrWhiteSpace(rutaRelativa))
        {
            var relativa = rutaRelativa.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            if (relativa.StartsWith($"App_Data{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                relativa = relativa[("App_Data" + Path.DirectorySeparatorChar).Length..];
            rutaFisica = Path.GetFullPath(Path.Combine(_environment.ContentRootPath, "App_Data", relativa));
        }
        else rutaFisica = Path.GetFullPath(Path.Combine(raizPermitida, nombreFisico));

        if (!rutaFisica.StartsWith(raizPermitida + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("La ruta del documento no es válida.");
        return rutaFisica;
    }

    private static void CalcularEstadoOperativo(LogisticaDetalleVm vm)
    {
        var ahora = DateTime.Now;
        vm.TotalPiezasSolicitadas = vm.Partidas.Sum(x => x.CantidadSolicitada);
        vm.TotalPiezasPreparadas = vm.Partidas.Sum(x => x.CantidadAsignada);
        vm.TotalPiezasDespachadas = vm.Partidas.Sum(x => x.CantidadDespachada);
        vm.TotalCajasAsignadas = vm.CajasAsignadas.Count;
        vm.TotalCajasCargadas = vm.CajasAsignadas.Count(x => string.Equals(x.Estatus, "Cargada", StringComparison.OrdinalIgnoreCase) || string.Equals(x.Estatus, "Despachada", StringComparison.OrdinalIgnoreCase));
        vm.FechaHoraCargaProgramada = CombinarFechaHora(vm.FechaCargaProgramada, vm.HoraCargaProgramada);
        vm.FechaHoraEntregaProgramada = CombinarFechaHora(vm.FechaEntregaProgramada, vm.HoraEntregaProgramada);
        vm.PorcentajeAvance = vm.Estatus switch
        {
            "Programado" => 10,
            "Preparando" => CalcularAvancePreparacion(vm),
            "Preparado" => 55,
            "Cargado" => 70,
            "En ruta" => 85,
            "Entregado" => 100,
            "Cancelado" => 0,
            _ => 0
        };
        switch (vm.Estatus)
        {
            case "Programado":
                vm.ProximaAccion = "Iniciar preparación";
                vm.ProximaAccionDetalle = vm.TotalPiezasPreparadas > 0 ? $"Ya existen {vm.TotalPiezasPreparadas:N0} piezas reservadas." : "Asignar las cajas PT correspondientes al embarque.";
                break;
            case "Preparando":
                if (!vm.PreparacionCompleta)
                {
                    vm.ProximaAccion = "Completar preparación";
                    vm.ProximaAccionDetalle = $"Faltan {vm.PiezasPendientesPreparar:N0} piezas por preparar.";
                }
                else
                {
                    vm.ProximaAccion = "Confirmar embarque preparado";
                    vm.ProximaAccionDetalle = "La cantidad requerida ya está completamente asignada.";
                }
                break;
            case "Preparado":
                vm.ProximaAccion = "Confirmar carga física";
                vm.ProximaAccionDetalle = vm.FormaEnvio == "Paqueteria" ? "El producto está preparado y puede entregarse al transportista." : "El producto está preparado y puede cargarse en la unidad.";
                break;
            case "Cargado":
                vm.ProximaAccion = vm.DocumentacionCompleta ? "Validar salida" : "Completar documentación";
                vm.ProximaAccionDetalle = vm.DocumentacionCompleta ? "Confirmar checklist de salida y despachar el embarque." : $"Faltan {vm.DocumentosFaltantes} documento(s) obligatorio(s) por cargar o validar.";
                break;
            case "En ruta":
                vm.ProximaAccion = "Confirmar entrega";
                vm.ProximaAccionDetalle = "El embarque se encuentra en tránsito hacia el cliente.";
                break;
            case "Entregado":
                if (vm.FormaEnvio == "Interno" && !vm.UnidadRetornada && !string.IsNullOrWhiteSpace(vm.Unidad))
                {
                    vm.ProximaAccion = "Registrar retorno de unidad";
                    vm.ProximaAccionDetalle = "La entrega está cerrada; falta confirmar el retorno operativo de la unidad.";
                }
                else
                {
                    vm.ProximaAccion = "Proceso completado";
                    vm.ProximaAccionDetalle = vm.UnidadRetornada ? $"Entrega completada y unidad retornada el {vm.FechaRetornoUnidad:dd/MM/yyyy HH:mm}." : "El embarque ya fue recibido por el cliente.";
                }
                break;
            case "Cancelado":
                vm.ProximaAccion = "Programación cancelada";
                vm.ProximaAccionDetalle = "Este embarque ya no continúa en el flujo.";
                break;
            default:
                vm.ProximaAccion = "Revisar embarque";
                vm.ProximaAccionDetalle = "Estatus no reconocido.";
                break;
        }
        if (vm.FechaHoraCargaProgramada.HasValue)
        {
            vm.MinutosParaCarga = (int)Math.Round((vm.FechaHoraCargaProgramada.Value - ahora).TotalMinutes);
            vm.CargaAtrasada = vm.Estatus is "Programado" or "Preparando" or "Preparado" && ahora > vm.FechaHoraCargaProgramada.Value;
        }
        if (vm.FechaHoraEntregaProgramada.HasValue)
        {
            vm.MinutosParaEntrega = (int)Math.Round((vm.FechaHoraEntregaProgramada.Value - ahora).TotalMinutes);
            vm.EntregaAtrasada = vm.Estatus is not "Entregado" and not "Cancelado" && ahora > vm.FechaHoraEntregaProgramada.Value;
        }
        vm.EnRiesgo = false;
        vm.MensajeRiesgo = string.Empty;
        if (vm.Estatus is "Entregado" or "Cancelado")
        {
        }
        else if (vm.EntregaAtrasada)
        {
            vm.MensajeRiesgo = "Entrega Expeditada: la fecha u hora comprometida de entrega ya fue superada y requiere atención prioritaria.";
        }
        else if (vm.CargaAtrasada)
        {
            vm.MensajeRiesgo = "La hora programada de carga ya fue superada.";
        }
        else if (vm.FechaHoraCargaProgramada.HasValue && vm.MinutosParaCarga.HasValue && vm.MinutosParaCarga.Value <= 60 && vm.Estatus is "Programado" or "Preparando")
        {
            vm.EnRiesgo = true;
            vm.MensajeRiesgo = vm.PiezasPendientesPreparar > 0 ? $"La carga está próxima y faltan {vm.PiezasPendientesPreparar:N0} piezas por preparar." : "La carga está próxima y todavía no se ha confirmado la preparación.";
        }
        else if (vm.FechaHoraEntregaProgramada.HasValue && vm.MinutosParaEntrega.HasValue && vm.MinutosParaEntrega.Value <= 120 && vm.Estatus != "En ruta")
        {
            vm.EnRiesgo = true;
            vm.MensajeRiesgo = "La entrega está próxima y el embarque todavía no se encuentra en ruta.";
        }
        if (vm.Estatus == "Cancelado")
        {
            vm.EstadoGeneral = "Cancelado";
            vm.EstadoGeneralClase = "secondary";
            vm.EstadoGeneralIcono = "fa-ban";
        }
        else if (vm.Estatus == "Entregado")
        {
            vm.EstadoGeneral = "Entregado";
            vm.EstadoGeneralClase = "success";
            vm.EstadoGeneralIcono = "fa-circle-check";
        }
        else if (vm.EntregaAtrasada)
        {
            vm.EstadoGeneral = "Expeditado";
            vm.EstadoGeneralClase = "danger";
            vm.EstadoGeneralIcono = "fa-bolt";
        }
        else if (vm.CargaAtrasada)
        {
            vm.EstadoGeneral = "Carga atrasada";
            vm.EstadoGeneralClase = "danger";
            vm.EstadoGeneralIcono = "fa-triangle-exclamation";
        }
        else if (vm.EnRiesgo)
        {
            vm.EstadoGeneral = "En riesgo";
            vm.EstadoGeneralClase = "warning";
            vm.EstadoGeneralIcono = "fa-clock";
        }
        else
        {
            vm.EstadoGeneral = "En tiempo";
            vm.EstadoGeneralClase = "success";
            vm.EstadoGeneralIcono = "fa-circle-check";
        }
        vm.Checklist = new List<LogisticaChecklistVm>();
        if (vm.FormaEnvio == "Interno")
        {
            vm.Checklist.Add(new LogisticaChecklistVm
            {
                Codigo = "RUTA",
                Concepto = "Ruta asignada",
                Descripcion = vm.TieneRuta ? vm.Ruta : "El embarque todavía no tiene ruta.",
                Completo = vm.TieneRuta
            });
            vm.Checklist.Add(new LogisticaChecklistVm
            {
                Codigo = "UNIDAD",
                Concepto = "Unidad asignada",
                Descripcion = vm.TieneUnidad ? vm.Unidad : "El embarque todavía no tiene unidad.",
                Completo = vm.TieneUnidad
            });
            vm.Checklist.Add(new LogisticaChecklistVm
            {
                Codigo = "OPERADOR",
                Concepto = "Operador asignado",
                Descripcion = vm.TieneOperador ? vm.Operador : "El embarque todavía no tiene operador.",
                Completo = vm.TieneOperador
            });
        }
        else
        {
            var modalidadCorrecta = !string.IsNullOrWhiteSpace(vm.ModalidadEnvio);
            var transportistaCorrecto = !string.IsNullOrWhiteSpace(vm.Transportista);
            vm.Checklist.Add(new LogisticaChecklistVm
            {
                Codigo = "MODALIDAD",
                Concepto = "Modalidad de envío",
                Descripcion = modalidadCorrecta ? vm.ModalidadEnvio : "Falta seleccionar Terrestre, Aérea o Marítima.",
                Completo = modalidadCorrecta
            });
            vm.Checklist.Add(new LogisticaChecklistVm
            {
                Codigo = "TRANSPORTISTA",
                Concepto = "Transportista / compañía",
                Descripcion = transportistaCorrecto ? vm.Transportista : "Falta indicar la compañía o transportista.",
                Completo = transportistaCorrecto
            });
        }
        vm.Checklist.Add(new LogisticaChecklistVm
        {
            Codigo = "PRODUCTO",
            Concepto = "Producto preparado",
            Descripcion = $"{vm.TotalPiezasPreparadas:N0} de {vm.TotalPiezasSolicitadas:N0} piezas",
            Completo = vm.PreparacionCompleta
        });
        vm.Checklist.Add(new LogisticaChecklistVm
        {
            Codigo = "CARGA",
            Concepto = "Carga física confirmada",
            Descripcion = vm.CargaCompleta ? "Carga completa." : "La carga física todavía no se ha confirmado.",
            Completo = vm.CargaCompleta
        });
        vm.Checklist.Add(new LogisticaChecklistVm
        {
            Codigo = "DOCUMENTOS",
            Concepto = "Documentación completa",
            Descripcion = vm.DocumentacionCompleta ? $"{vm.DocumentosObligatoriosCompletos} de {vm.DocumentosObligatorios} documentos obligatorios validados." : $"{vm.DocumentosFaltantes} documento(s) obligatorio(s) pendiente(s) de carga o validación.",
            Completo = vm.DocumentacionCompleta
        });
        vm.Checklist.Add(new LogisticaChecklistVm
        {
            Codigo = "INCIDENCIAS",
            Concepto = "Sin incidencias críticas",
            Descripcion = vm.IncidenciasCriticas == 0 ? "Sin bloqueos críticos." : $"{vm.IncidenciasCriticas} incidencia(s) crítica(s) abierta(s).",
            Completo = vm.IncidenciasCriticas == 0
        });
    }

    private static DateTime? CombinarFechaHora(
    DateTime? fecha,
    TimeSpan? hora)
    {
        if (!fecha.HasValue)
            return null;

        return fecha.Value.Date.Add(
            hora ?? TimeSpan.Zero);
    }

    private static int CalcularAvancePreparacion(
        LogisticaDetalleVm vm)
    {
        if (vm.TotalPiezasSolicitadas <= 0)
            return 15;

        var proporcion =
            Math.Clamp(
                (decimal)vm.TotalPiezasPreparadas
                / vm.TotalPiezasSolicitadas,
                0m,
                1m);

        // Programación = 10%
        // Preparación ocupa del 10 al 50%
        return 10 + (int)Math.Round(proporcion * 40m);
    }

    private async Task CargarIncidenciasAsync(SqlConnection cn, LogisticaDetalleVm vm, CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT i.IncidenciaID,i.EmbarqueID,ISNULL(CONVERT(NVARCHAR(50),i.Folio),N'') AS Folio,
       ISNULL(i.Tipo,N'') AS Tipo,ISNULL(i.Severidad,N'') AS Severidad,
       ISNULL(i.Descripcion,N'') AS Descripcion,ISNULL(i.Responsable,N'') AS Responsable,
       ISNULL(i.Estatus,N'') AS Estatus,i.FechaRegistro,i.FechaCompromiso,i.FechaCierre,
       ISNULL(i.Solucion,N'') AS Solucion,ISNULL(i.UsuarioRegistro,N'') AS UsuarioRegistro
FROM dbo.Logistica_Incidencias i
WHERE i.EmbarqueID=@EmbarqueID AND i.Activo=1
ORDER BY
    CASE WHEN i.Estatus IN(N'Abierta',N'En seguimiento') THEN 0 ELSE 1 END,
    CASE i.Severidad
        WHEN N'Crítica' THEN 1
        WHEN N'Alta' THEN 2
        WHEN N'Media' THEN 3
        WHEN N'Baja' THEN 4
        ELSE 5
    END,
    i.FechaRegistro DESC,
    i.IncidenciaID DESC;";

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = vm.EmbarqueID;

        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);

        while (await rd.ReadAsync(cancellationToken))
        {
            vm.Incidencias.Add(new LogisticaIncidenciaVm
            {
                IncidenciaID = Entero(rd, "IncidenciaID"),
                EmbarqueID = Entero(rd, "EmbarqueID"),
                Folio = Texto(rd, "Folio"),
                Tipo = Texto(rd, "Tipo"),
                Severidad = Texto(rd, "Severidad"),
                Descripcion = Texto(rd, "Descripcion"),
                Responsable = Texto(rd, "Responsable"),
                Estatus = Texto(rd, "Estatus"),
                FechaRegistro = Fecha(rd, "FechaRegistro") ?? DateTime.MinValue,
                FechaCompromiso = Fecha(rd, "FechaCompromiso"),
                FechaCierre = Fecha(rd, "FechaCierre"),
                Solucion = Texto(rd, "Solucion"),
                UsuarioRegistro = Texto(rd, "UsuarioRegistro")
            });
        }

        vm.IncidenciasAbiertas = vm.Incidencias.Count(x => x.Estatus is "Abierta" or "En seguimiento");
        vm.IncidenciasCriticas = vm.Incidencias.Count(x =>
            (x.Estatus is "Abierta" or "En seguimiento") &&
            string.Equals(x.Severidad, "Crítica", StringComparison.OrdinalIgnoreCase));

        vm.TieneIncidencia = vm.IncidenciasAbiertas > 0;
    }


    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegistrarIncidencia(LogisticaIncidenciaCrearVm model, CancellationToken cancellationToken)
    {
        var acceso = await ValidarAccesoAsync("Tablero de Logística");
        if (acceso != null) return acceso;

        if (model.EmbarqueID <= 0)
        {
            TempData["LogisticaError"] = "El embarque indicado no es válido.";
            return RedirectToAction(nameof(Index));
        }

        model.Tipo = model.Tipo?.Trim() ?? string.Empty;
        model.Severidad = model.Severidad?.Trim() ?? string.Empty;
        model.Descripcion = model.Descripcion?.Trim() ?? string.Empty;
        model.Responsable = model.Responsable?.Trim();

        var tiposPermitidos = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Producto incompleto",
        "Caja dañada",
        "Unidad no disponible",
        "Operador",
        "Producción",
        "Calidad",
        "Transporte",
        "Dirección / cliente",
        "Retraso",
        "Rechazo de entrega",
        "Diferencia de cantidades",
        "Otro"
    };

        var severidadesPermitidas = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Baja",
        "Media",
        "Alta",
        "Crítica"
    };

        if (string.IsNullOrWhiteSpace(model.Descripcion))
            ModelState.AddModelError(nameof(model.Descripcion), "La descripción es obligatoria.");

        if (!tiposPermitidos.Contains(model.Tipo))
            ModelState.AddModelError(nameof(model.Tipo), "Selecciona un tipo de incidencia válido.");

        if (!severidadesPermitidas.Contains(model.Severidad))
            ModelState.AddModelError(nameof(model.Severidad), "Selecciona una severidad válida.");

        if (!ModelState.IsValid)
        {
            TempData["LogisticaError"] = "Completa correctamente los datos de la incidencia.";
            return RedirectToAction(nameof(Detalle), new { id = model.EmbarqueID });
        }

        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        try
        {
            var header = await ObtenerHeaderAsync(cn, tx, model.EmbarqueID, cancellationToken)
                ?? throw new InvalidOperationException("El embarque no existe.");

            if (header.Estatus == "Cancelado")
                throw new InvalidOperationException("No se pueden registrar incidencias en un embarque cancelado.");

            const string sqlDuplicado = @"
SELECT COUNT_BIG(*)
FROM dbo.Logistica_Incidencias WITH (UPDLOCK,HOLDLOCK)
WHERE EmbarqueID=@EmbarqueID
  AND Activo=1
  AND Estatus IN(N'Abierta',N'En seguimiento')
  AND Tipo=@Tipo
  AND Descripcion=@Descripcion;";

            await using (var cmd = new SqlCommand(sqlDuplicado, cn, tx))
            {
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = model.EmbarqueID;
                cmd.Parameters.Add("@Tipo", SqlDbType.NVarChar, 80).Value = model.Tipo;
                cmd.Parameters.Add("@Descripcion", SqlDbType.NVarChar, 1200).Value = model.Descripcion;

                var duplicados = Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken));

                if (duplicados > 0)
                    throw new InvalidOperationException("Ya existe una incidencia abierta con el mismo tipo y descripción.");
            }

            const string sql = @"
INSERT dbo.Logistica_Incidencias
(
    EmbarqueID,Tipo,Severidad,Descripcion,Responsable,Estatus,
    FechaRegistro,FechaCompromiso,UsuarioRegistroID,UsuarioRegistro,Activo
)
VALUES
(
    @EmbarqueID,@Tipo,@Severidad,@Descripcion,@Responsable,N'Abierta',
    SYSDATETIME(),@FechaCompromiso,@UsuarioID,@UsuarioNombre,1
);
SELECT CONVERT(int,SCOPE_IDENTITY());";

            int incidenciaId;

            await using (var cmd = new SqlCommand(sql, cn, tx))
            {
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = model.EmbarqueID;
                cmd.Parameters.Add("@Tipo", SqlDbType.NVarChar, 80).Value = model.Tipo;
                cmd.Parameters.Add("@Severidad", SqlDbType.NVarChar, 20).Value = model.Severidad;
                cmd.Parameters.Add("@Descripcion", SqlDbType.NVarChar, 1200).Value = model.Descripcion;
                cmd.Parameters.Add("@Responsable", SqlDbType.NVarChar, 200).Value = Db(string.IsNullOrWhiteSpace(model.Responsable) ? null : model.Responsable);
                cmd.Parameters.Add("@FechaCompromiso", SqlDbType.DateTime2).Value = Db(model.FechaCompromiso);
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = Db(UsuarioID);
                cmd.Parameters.Add("@UsuarioNombre", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                incidenciaId = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
            }

            await EjecutarAsync(cn, tx,
                @"UPDATE dbo.Logistica_Embarques
SET TieneIncidencia=1,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
WHERE EmbarqueID=@EmbarqueID AND Activo=1;",
                cancellationToken,
                ("@Usuario", UsuarioNombre),
                ("@EmbarqueID", model.EmbarqueID));

            var posteriorEntrega = header.Estatus == "Entregado";

            var historial = $"Incidencia INC-{incidenciaId:000000}. {model.Tipo} / {model.Severidad}. {model.Descripcion}";

            if (posteriorEntrega)
                historial = "Incidencia posterior al cierre de entrega. " + historial;

            if (!string.IsNullOrWhiteSpace(model.Responsable))
                historial += $" Responsable: {model.Responsable}.";

            if (model.FechaCompromiso.HasValue)
                historial += $" Compromiso: {model.FechaCompromiso.Value:dd/MM/yyyy HH:mm}.";

            await InsertarHistorialAsync(cn, tx, model.EmbarqueID, posteriorEntrega ? "INCIDENCIA_POST_ENTREGA" : "INCIDENCIA_REGISTRADA", header.Estatus, header.Estatus, historial, cancellationToken);

            await tx.CommitAsync(cancellationToken);

            TempData["LogisticaOk"] = posteriorEntrega
                ? $"Incidencia INC-{incidenciaId:000000} registrada después de la entrega. El embarque permanece Entregado."
                : $"Incidencia INC-{incidenciaId:000000} registrada correctamente.";
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            TempData["LogisticaError"] = $"No fue posible registrar la incidencia: {ex.Message}";
        }

        return RedirectToAction(nameof(Detalle), new { id = model.EmbarqueID });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CerrarIncidencia(LogisticaIncidenciaCerrarVm model, CancellationToken cancellationToken)
    {
        var acceso = await ValidarAccesoAsync("Tablero de Logística");
        if (acceso != null) return acceso;

        if (model.EmbarqueID <= 0 || model.IncidenciaID <= 0)
        {
            TempData["LogisticaError"] = "La incidencia indicada no es válida.";
            return model.EmbarqueID > 0
                ? RedirectToAction(nameof(Detalle), new { id = model.EmbarqueID })
                : RedirectToAction(nameof(Index));
        }

        model.Solucion = model.Solucion?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(model.Solucion))
            ModelState.AddModelError(nameof(model.Solucion), "Debes indicar la solución aplicada.");

        if (!ModelState.IsValid)
        {
            TempData["LogisticaError"] = "Debes indicar la solución aplicada.";
            return RedirectToAction(nameof(Detalle), new { id = model.EmbarqueID });
        }

        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        try
        {
            var header = await ObtenerHeaderAsync(cn, tx, model.EmbarqueID, cancellationToken)
                ?? throw new InvalidOperationException("El embarque no existe.");

            const string sqlObtener = @"
SELECT Tipo,Severidad,Estatus
FROM dbo.Logistica_Incidencias WITH (UPDLOCK,HOLDLOCK)
WHERE IncidenciaID=@IncidenciaID
  AND EmbarqueID=@EmbarqueID
  AND Activo=1;";

            string tipo;
            string severidad;
            string estatus;

            await using (var cmd = new SqlCommand(sqlObtener, cn, tx))
            {
                cmd.Parameters.Add("@IncidenciaID", SqlDbType.Int).Value = model.IncidenciaID;
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = model.EmbarqueID;

                await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);

                if (!await rd.ReadAsync(cancellationToken))
                    throw new InvalidOperationException("La incidencia no existe o no pertenece a este embarque.");

                tipo = Texto(rd, "Tipo");
                severidad = Texto(rd, "Severidad");
                estatus = Texto(rd, "Estatus");
            }

            if (estatus == "Cerrada")
                throw new InvalidOperationException("La incidencia ya se encuentra cerrada.");

            if (estatus is not "Abierta" and not "En seguimiento")
                throw new InvalidOperationException($"La incidencia se encuentra en un estado no válido para cierre: {estatus}.");

            const string sqlCerrar = @"
UPDATE dbo.Logistica_Incidencias
SET Estatus=N'Cerrada',
    Solucion=@Solucion,
    FechaCierre=SYSDATETIME(),
    UsuarioCierreID=@UsuarioID,
    UsuarioCierre=@UsuarioNombre
WHERE IncidenciaID=@IncidenciaID
  AND EmbarqueID=@EmbarqueID
  AND Activo=1
  AND Estatus IN(N'Abierta',N'En seguimiento');

SELECT @@ROWCOUNT;";

            await using (var cmd = new SqlCommand(sqlCerrar, cn, tx))
            {
                cmd.Parameters.Add("@Solucion", SqlDbType.NVarChar, 1200).Value = model.Solucion;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = Db(UsuarioID);
                cmd.Parameters.Add("@UsuarioNombre", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@IncidenciaID", SqlDbType.Int).Value = model.IncidenciaID;
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = model.EmbarqueID;

                var afectados = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));

                if (afectados == 0)
                    throw new InvalidOperationException("La incidencia cambió de estado mientras intentabas cerrarla.");
            }

            const string sqlPendientes = @"
SELECT COUNT_BIG(*)
FROM dbo.Logistica_Incidencias
WHERE EmbarqueID=@EmbarqueID
  AND Activo=1
  AND Estatus IN(N'Abierta',N'En seguimiento');";

            long pendientes;

            await using (var cmd = new SqlCommand(sqlPendientes, cn, tx))
            {
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = model.EmbarqueID;
                pendientes = Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken));
            }

            await EjecutarAsync(cn, tx,
                @"UPDATE dbo.Logistica_Embarques
SET TieneIncidencia=@TieneIncidencia,
    FechaModificacion=SYSDATETIME(),
    ActualizadoPor=@Usuario
WHERE EmbarqueID=@EmbarqueID AND Activo=1;",
                cancellationToken,
                ("@TieneIncidencia", pendientes > 0),
                ("@Usuario", UsuarioNombre),
                ("@EmbarqueID", model.EmbarqueID));

            var observacion = $"Incidencia INC-{model.IncidenciaID:000000} cerrada. {tipo} / {severidad}. Solución: {model.Solucion}";

            if (header.Estatus == "Entregado")
                observacion += " Cierre realizado después de la entrega; el embarque permanece Entregado.";

            await InsertarHistorialAsync(cn, tx, model.EmbarqueID, "INCIDENCIA_CERRADA", header.Estatus, header.Estatus, observacion, cancellationToken);

            await tx.CommitAsync(cancellationToken);

            TempData["LogisticaOk"] = pendientes > 0
                ? $"Incidencia INC-{model.IncidenciaID:000000} cerrada. Quedan {pendientes:N0} incidencia(s) abiertas."
                : $"Incidencia INC-{model.IncidenciaID:000000} cerrada. El embarque ya no tiene incidencias abiertas.";
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            TempData["LogisticaError"] = $"No fue posible cerrar la incidencia: {ex.Message}";
        }

        return RedirectToAction(nameof(Detalle), new { id = model.EmbarqueID });
    }

    [HttpGet]
    public async Task<IActionResult> Calendario(CancellationToken cancellationToken)
    {
        var acceso = await ValidarAccesoAsync("Tablero de Logística");
        if (acceso != null) return acceso;
        await using var cn = await AbrirAsync(cancellationToken);
        if (!await TieneFase1Async(cn, cancellationToken))
        {
            TempData["LogisticaError"] = "Falta la estructura de Logística Fase 1.";
            return RedirectToAction(nameof(Index));
        }
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> EventosCalendario(DateTime desde, DateTime hasta, string? estatus = null, string? q = null, CancellationToken cancellationToken = default)
    {
        var acceso = await ValidarAccesoAsync("Tablero de Logística");
        if (acceso != null) return acceso;

        desde = desde.Date;
        hasta = hasta.Date;

        if (hasta < desde)
            return BadRequest(new { ok = false, mensaje = "El rango de fechas del calendario no es válido." });

        if ((hasta - desde).TotalDays > 120)
            return BadRequest(new { ok = false, mensaje = "El calendario solo puede consultar hasta 120 días por solicitud." });

        q = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
        estatus = string.IsNullOrWhiteSpace(estatus) ? null : estatus.Trim();

        await using var cn = await AbrirAsync(cancellationToken);

        if (!await TieneFase1Async(cn, cancellationToken))
            return BadRequest(new { ok = false, mensaje = "Falta la estructura de Logística Fase 1." });

        var filas = new List<(
            int EmbarqueID,
            int ClienteID,
            string Cliente,
            string Folio,
            string Destino,
            DateTime FechaEntrega,
            TimeSpan? HoraEntrega,
            string Estatus,
            bool Incidencia,
            int EmbarqueDetalleID,
            string FolioRelease,
            string NumeroOF,
            string NumeroParte,
            string Descripcion,
            int CantidadSolicitada,
            int CantidadDespachada)>();

        const string sql = @"
SELECT
    e.EmbarqueID,
    ISNULL(e.ClienteID,0) AS ClienteID,
    ISNULL(NULLIF(LTRIM(RTRIM(e.ClienteNombreSnapshot)),N''),N'Sin cliente') AS Cliente,
    ISNULL(e.Folio,N'') AS Folio,
    ISNULL(e.Destino,N'') AS Destino,
    e.FechaEntregaProgramada,
    e.HoraEntregaProgramada,
    ISNULL(e.Estatus,N'') AS Estatus,
    ISNULL(e.TieneIncidencia,0) AS TieneIncidencia,

    d.EmbarqueDetalleID,
    ISNULL(d.FolioReleaseSnapshot,N'') AS FolioRelease,
    ISNULL(d.NumeroOFSnapshot,N'') AS NumeroOF,
    ISNULL(d.NumeroParteSnapshot,N'') AS NumeroParte,
    ISNULL(d.DescripcionParteSnapshot,N'') AS Descripcion,
    ISNULL(d.CantidadSolicitada,0) AS CantidadSolicitada,
    ISNULL(d.CantidadDespachada,0) AS CantidadDespachada
FROM dbo.Logistica_Embarques e
INNER JOIN dbo.Logistica_EmbarqueDetalle d
    ON d.EmbarqueID=e.EmbarqueID
   AND d.Activo=1
WHERE e.Activo=1
  AND e.FechaEntregaProgramada IS NOT NULL
  AND e.FechaEntregaProgramada>=@Desde
  AND e.FechaEntregaProgramada<DATEADD(DAY,1,@Hasta)
  AND (@Estatus IS NULL OR e.Estatus=@Estatus)
  AND
  (
      @Q IS NULL
      OR e.Folio LIKE N'%'+@Q+N'%'
      OR e.ClienteNombreSnapshot LIKE N'%'+@Q+N'%'
      OR e.Destino LIKE N'%'+@Q+N'%'
      OR EXISTS
      (
          SELECT 1
          FROM dbo.Logistica_EmbarqueDetalle dq
          WHERE dq.EmbarqueID=e.EmbarqueID
            AND dq.Activo=1
            AND
            (
                dq.NumeroOFSnapshot LIKE N'%'+@Q+N'%'
                OR dq.NumeroParteSnapshot LIKE N'%'+@Q+N'%'
                OR dq.FolioReleaseSnapshot LIKE N'%'+@Q+N'%'
                OR dq.DescripcionParteSnapshot LIKE N'%'+@Q+N'%'
            )
      )
  )
ORDER BY
    e.FechaEntregaProgramada,
    e.ClienteNombreSnapshot,
    e.HoraEntregaProgramada,
    e.EmbarqueID,
    d.EmbarqueDetalleID;";

        await using (var cmd = new SqlCommand(sql, cn))
        {
            cmd.Parameters.Add("@Desde", SqlDbType.Date).Value = desde;
            cmd.Parameters.Add("@Hasta", SqlDbType.Date).Value = hasta;
            cmd.Parameters.Add("@Estatus", SqlDbType.NVarChar, 20).Value =
                string.IsNullOrWhiteSpace(estatus)
                    ? DBNull.Value
                    : estatus;

            cmd.Parameters.Add("@Q", SqlDbType.NVarChar, 250).Value =
                string.IsNullOrWhiteSpace(q)
                    ? DBNull.Value
                    : q;

            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);

            while (await rd.ReadAsync(cancellationToken))
            {
                var fechaEntrega = Fecha(rd, "FechaEntregaProgramada");
                if (!fechaEntrega.HasValue) continue;

                filas.Add((
                    EmbarqueID: Entero(rd, "EmbarqueID"),
                    ClienteID: Entero(rd, "ClienteID"),
                    Cliente: Texto(rd, "Cliente"),
                    Folio: Texto(rd, "Folio"),
                    Destino: Texto(rd, "Destino"),
                    FechaEntrega: fechaEntrega.Value.Date,
                    HoraEntrega: Hora(rd, "HoraEntregaProgramada"),
                    Estatus: Texto(rd, "Estatus"),
                    Incidencia: Booleano(rd, "TieneIncidencia"),
                    EmbarqueDetalleID: Entero(rd, "EmbarqueDetalleID"),
                    FolioRelease: Texto(rd, "FolioRelease"),
                    NumeroOF: Texto(rd, "NumeroOF"),
                    NumeroParte: Texto(rd, "NumeroParte"),
                    Descripcion: Texto(rd, "Descripcion"),
                    CantidadSolicitada: Entero(rd, "CantidadSolicitada"),
                    CantidadDespachada: Entero(rd, "CantidadDespachada")
                ));
            }
        }

        /*
         * El calendario ya NO genera:
         * - eventos de carga;
         * - releases pendientes;
         * - un evento por embarque.
         *
         * Ahora genera:
         * CLIENTE + FECHA DE ENTREGA = UN SOLO EVENTO.
         *
         * Dentro del evento viajan todas las OF/partidas que corresponden
         * a ese cliente para ese día.
         */
        var grupos = filas
            .GroupBy(x => new
            {
                ClaveCliente = x.ClienteID > 0
                    ? $"ID:{x.ClienteID}"
                    : $"NOMBRE:{x.Cliente.Trim().ToUpperInvariant()}",
                Fecha = x.FechaEntrega.Date
            })
            .OrderBy(x => x.Key.Fecha)
            .ThenBy(x => x.Select(y => y.Cliente).FirstOrDefault());

        var eventos = grupos.Select((grupo, indice) =>
        {
            var registros = grupo.ToList();

            var clienteId = registros
                .Where(x => x.ClienteID > 0)
                .Select(x => x.ClienteID)
                .FirstOrDefault();

            var cliente = registros
                .Select(x => x.Cliente?.Trim())
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
                ?? "Sin cliente";

            var embarquesIds = registros
                .Select(x => x.EmbarqueID)
                .Distinct()
                .ToList();

            var folios = registros
                .Select(x => x.Folio?.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var destinos = registros
                .Select(x => x.Destino?.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var ofs = registros
                .Select(x => x.NumeroOF?.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var estados = registros
                .Select(x => x.Estatus?.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var horas = registros
                .Where(x => x.HoraEntrega.HasValue)
                .Select(x => x.HoraEntrega!.Value)
                .ToList();

            var todosEntregados = registros.All(x =>
                string.Equals(x.Estatus, "Entregado", StringComparison.OrdinalIgnoreCase));

            var todosCancelados = registros.All(x =>
                string.Equals(x.Estatus, "Cancelado", StringComparison.OrdinalIgnoreCase));

            var tieneEnRuta = registros.Any(x =>
                string.Equals(x.Estatus, "En ruta", StringComparison.OrdinalIgnoreCase));

            var tieneCargado = registros.Any(x =>
                string.Equals(x.Estatus, "Cargado", StringComparison.OrdinalIgnoreCase));

            var tienePreparado = registros.Any(x =>
                string.Equals(x.Estatus, "Preparado", StringComparison.OrdinalIgnoreCase));

            var tienePreparando = registros.Any(x =>
                string.Equals(x.Estatus, "Preparando", StringComparison.OrdinalIgnoreCase));

            var tieneProgramado = registros.Any(x =>
                string.Equals(x.Estatus, "Programado", StringComparison.OrdinalIgnoreCase));

            var expeditado =
                grupo.Key.Fecha < DateTime.Today &&
                registros.Any(x =>
                    !string.Equals(x.Estatus, "Entregado", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(x.Estatus, "Cancelado", StringComparison.OrdinalIgnoreCase));

            string estatusGrupo;

            if (todosEntregados)
                estatusGrupo = "Entregado";
            else if (todosCancelados)
                estatusGrupo = "Cancelado";
            else if (tieneEnRuta)
                estatusGrupo = "En ruta";
            else if (estados.Count == 1)
                estatusGrupo = estados[0];
            else
                estatusGrupo = "Mixto";

            var destinoResumen = destinos.Count switch
            {
                0 => string.Empty,
                1 => destinos[0]!,
                _ => $"{destinos.Count:N0} destinos"
            };

            var folioResumen = folios.Count switch
            {
                0 => string.Empty,
                1 => folios[0]!,
                _ => $"{folios.Count:N0} embarques"
            };

            var horaResumen = horas.Count > 0
                ? horas.Min().ToString(@"hh\:mm")
                : string.Empty;

            var urlGrupo = embarquesIds.Count == 1
                ? Url.Action(
                    nameof(Detalle),
                    "LogisticaEmbarques",
                    new { id = embarquesIds[0] }) ?? string.Empty
                : string.Empty;

            var entregas = registros
                .OrderBy(x => x.HoraEntrega ?? TimeSpan.MaxValue)
                .ThenBy(x => x.NumeroOF)
                .ThenBy(x => x.NumeroParte)
                .ThenBy(x => x.EmbarqueID)
                .Select(x => new
                {
                    embarqueId = x.EmbarqueID,
                    embarqueDetalleId = x.EmbarqueDetalleID,

                    folio = x.Folio,
                    folioRelease = x.FolioRelease,

                    numeroOF = x.NumeroOF,
                    numeroParte = x.NumeroParte,
                    descripcion = x.Descripcion,

                    cantidad = x.CantidadSolicitada,
                    cantidadDespachada = x.CantidadDespachada,

                    destino = x.Destino,
                    estatus = x.Estatus,

                    hora = x.HoraEntrega?.ToString(@"hh\:mm") ?? string.Empty,

                    incidencia = x.Incidencia,

                    url = Url.Action(
                        nameof(Detalle),
                        "LogisticaEmbarques",
                        new { id = x.EmbarqueID })
                })
                .ToList();

            return new
            {
                id = $"CLIENTE-{indice + 1}-{grupo.Key.Fecha:yyyyMMdd}",

                tipo = "CLIENTE_ENTREGA",

                clienteId,
                cliente,

                titulo = cliente,

                fecha = grupo.Key.Fecha.ToString("yyyy-MM-dd"),
                hora = horaResumen,

                estatus = estatusGrupo,

                expeditado,
                incidencia = registros.Any(x => x.Incidencia),

                destino = destinoResumen,
                folio = folioResumen,

                totalOfs = ofs.Count,
                totalEmbarques = embarquesIds.Count,
                totalPartidas = registros.Count,
                totalPiezas = registros.Sum(x => (long)x.CantidadSolicitada),

                programados = registros.Count(x =>
                    string.Equals(x.Estatus, "Programado", StringComparison.OrdinalIgnoreCase)),

                preparando = registros.Count(x =>
                    string.Equals(x.Estatus, "Preparando", StringComparison.OrdinalIgnoreCase)),

                preparados = registros.Count(x =>
                    string.Equals(x.Estatus, "Preparado", StringComparison.OrdinalIgnoreCase)),

                cargados = registros.Count(x =>
                    string.Equals(x.Estatus, "Cargado", StringComparison.OrdinalIgnoreCase)),

                enRuta = registros.Count(x =>
                    string.Equals(x.Estatus, "En ruta", StringComparison.OrdinalIgnoreCase)),

                entregados = registros.Count(x =>
                    string.Equals(x.Estatus, "Entregado", StringComparison.OrdinalIgnoreCase)),

                cancelados = registros.Count(x =>
                    string.Equals(x.Estatus, "Cancelado", StringComparison.OrdinalIgnoreCase)),

                tieneProgramado,
                tienePreparando,
                tienePreparado,
                tieneCargado,
                tieneEnRuta,
                todosEntregados,
                todosCancelados,

                url = urlGrupo,

                entregas
            };
        }).ToList();

        return Json(new
        {
            ok = true,
            desde = desde.ToString("yyyy-MM-dd"),
            hasta = hasta.ToString("yyyy-MM-dd"),

            /*
             * total = bloques visibles en calendario,
             * no número de embarques.
             */
            total = eventos.Count,

            totalEmbarques = filas
                .Select(x => x.EmbarqueID)
                .Distinct()
                .Count(),

            totalOfs = filas
                .Select(x => x.NumeroOF?.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),

            eventos
        });
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegistrarRetorno(LogisticaRetornoVm model, CancellationToken cancellationToken)
    {
        var acceso = await ValidarAccesoAsync("Tablero de Logística");
        if (acceso != null) return acceso;

        if (model.EmbarqueID <= 0)
        {
            TempData["LogisticaError"] = "El embarque indicado no es válido.";
            return RedirectToAction(nameof(Index));
        }

        model.Observaciones = model.Observaciones?.Trim() ?? string.Empty;

        if (model.FechaRetorno == default)
            ModelState.AddModelError(nameof(model.FechaRetorno), "La fecha de retorno es obligatoria.");

        if (model.FechaRetorno > DateTime.Now.AddMinutes(5))
            ModelState.AddModelError(nameof(model.FechaRetorno), "La fecha de retorno no puede estar en el futuro.");

        if (model.KilometrajeRetorno.HasValue && model.KilometrajeRetorno.Value < 0)
            ModelState.AddModelError(nameof(model.KilometrajeRetorno), "El kilometraje de retorno no puede ser negativo.");

        if (model.Observaciones.Length > 1000)
            ModelState.AddModelError(nameof(model.Observaciones), "Las observaciones no pueden exceder 1000 caracteres.");

        if (!ModelState.IsValid)
        {
            TempData["LogisticaError"] = string.Join(" ", ModelState.Values.SelectMany(x => x.Errors).Select(x => x.ErrorMessage).Where(x => !string.IsNullOrWhiteSpace(x)));
            return RedirectToAction(nameof(Detalle), new { id = model.EmbarqueID });
        }

        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        try
        {
            const string sqlActual = @"
SELECT
    EmbarqueID,
    ISNULL(Estatus,N'') AS Estatus,
    UnidadID,
    FechaSalida,
    FechaEntrega,
    FechaRetornoUnidad,
    KilometrajeRetorno
FROM dbo.Logistica_Embarques WITH (UPDLOCK,HOLDLOCK)
WHERE EmbarqueID=@EmbarqueID
  AND Activo=1;";

            string estatus;
            int? unidadId;
            DateTime? fechaSalida;
            DateTime? fechaEntrega;
            DateTime? fechaRetornoActual;
            int? kilometrajeActual;

            await using (var cmd = new SqlCommand(sqlActual, cn, tx))
            {
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = model.EmbarqueID;
                await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);

                if (!await rd.ReadAsync(cancellationToken))
                    throw new InvalidOperationException("El embarque no existe.");

                estatus = Texto(rd, "Estatus");
                unidadId = EnteroNullable(rd, "UnidadID");
                fechaSalida = Fecha(rd, "FechaSalida");
                fechaEntrega = Fecha(rd, "FechaEntrega");
                fechaRetornoActual = Fecha(rd, "FechaRetornoUnidad");
                kilometrajeActual = EnteroNullable(rd, "KilometrajeRetorno");
            }

            if (estatus != "Entregado")
                throw new InvalidOperationException("El retorno de la unidad solo puede registrarse después de confirmar la entrega.");

            if (!unidadId.HasValue)
                throw new InvalidOperationException("El embarque no tiene una unidad asignada. No existe una unidad que retornar.");

            if (!fechaSalida.HasValue)
                throw new InvalidOperationException("El embarque no tiene una salida registrada.");

            if (!fechaEntrega.HasValue)
                throw new InvalidOperationException("El embarque no tiene una entrega confirmada.");

            if (fechaRetornoActual.HasValue)
                throw new InvalidOperationException($"La unidad ya fue registrada como retornada el {fechaRetornoActual.Value:dd/MM/yyyy HH:mm}.");

            if (model.FechaRetorno < fechaSalida.Value)
                throw new InvalidOperationException("La fecha de retorno no puede ser anterior a la salida del embarque.");

            if (model.FechaRetorno < fechaEntrega.Value)
                throw new InvalidOperationException("La fecha de retorno no puede ser anterior a la confirmación de entrega.");

            const string sqlUpdate = @"
UPDATE dbo.Logistica_Embarques
SET FechaRetornoUnidad=@FechaRetorno,
    KilometrajeRetorno=@KilometrajeRetorno,
    ObservacionesRetorno=@ObservacionesRetorno,
    RetornoPorUsuarioID=@UsuarioID,
    RetornoPorNombre=@UsuarioNombre,
    FechaModificacion=SYSDATETIME(),
    ActualizadoPor=@UsuarioNombre
WHERE EmbarqueID=@EmbarqueID
  AND Activo=1
  AND Estatus=N'Entregado'
  AND FechaRetornoUnidad IS NULL;

SELECT @@ROWCOUNT;";

            int afectados;

            await using (var cmd = new SqlCommand(sqlUpdate, cn, tx))
            {
                cmd.Parameters.Add("@FechaRetorno", SqlDbType.DateTime2).Value = model.FechaRetorno;
                cmd.Parameters.Add("@KilometrajeRetorno", SqlDbType.Int).Value = Db(model.KilometrajeRetorno);
                cmd.Parameters.Add("@ObservacionesRetorno", SqlDbType.NVarChar, 1000).Value = Db(string.IsNullOrWhiteSpace(model.Observaciones) ? null : model.Observaciones);
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = Db(UsuarioID);
                cmd.Parameters.Add("@UsuarioNombre", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = model.EmbarqueID;
                afectados = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
            }

            if (afectados == 0)
                throw new InvalidOperationException("El retorno ya fue registrado o el embarque cambió mientras se realizaba la operación.");

            var historial = $"Retorno de unidad confirmado. Fecha de retorno: {model.FechaRetorno:dd/MM/yyyy HH:mm}.";

            if (model.KilometrajeRetorno.HasValue)
                historial += $" Kilometraje: {model.KilometrajeRetorno.Value:N0} km.";

            if (!string.IsNullOrWhiteSpace(model.Observaciones))
                historial += $" Observaciones: {model.Observaciones}";

            await InsertarHistorialAsync(
                cn,
                tx,
                model.EmbarqueID,
                "RETORNO_UNIDAD",
                "Entregado",
                "Entregado",
                historial,
                cancellationToken);

            await tx.CommitAsync(cancellationToken);

            TempData["LogisticaOk"] = "Retorno de la unidad registrado correctamente.";
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            TempData["LogisticaError"] = $"No fue posible registrar el retorno: {ex.Message}";
        }

        return RedirectToAction(nameof(Detalle), new { id = model.EmbarqueID });
    }

    private static async Task CargarRetornoAsync(SqlConnection cn, LogisticaDetalleVm vm, CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT
    FechaRetornoUnidad,
    KilometrajeRetorno,
    ISNULL(ObservacionesRetorno,N'') AS ObservacionesRetorno,
    ISNULL(RetornoPorNombre,N'') AS UsuarioRetorno
FROM dbo.Logistica_Embarques
WHERE EmbarqueID=@EmbarqueID
  AND Activo=1;";

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = vm.EmbarqueID;

        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);

        if (!await rd.ReadAsync(cancellationToken))
            return;

        vm.FechaRetornoUnidad = Fecha(rd, "FechaRetornoUnidad");
        vm.KilometrajeRetorno = EnteroNullable(rd, "KilometrajeRetorno");
        vm.ObservacionesRetorno = Texto(rd, "ObservacionesRetorno");
        vm.UsuarioRetorno = Texto(rd, "UsuarioRetorno");
        vm.UnidadRetornada = vm.FechaRetornoUnidad.HasValue;
    }

    private async Task CargarCatalogosAsync(LogisticaCrearVm vm, SqlConnection cn, CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT RutaID,Codigo + N' - ' + Nombre AS Texto FROM dbo.Logistica_Rutas WHERE Activo=1 ORDER BY Codigo;
SELECT UnidadID,NumeroEconomico + CASE WHEN NULLIF(Placas,N'') IS NULL THEN N'' ELSE N' - ' + Placas END AS Texto
FROM dbo.Logistica_Unidades WHERE Activo=1 ORDER BY NumeroEconomico;";
        await using var cmd = new SqlCommand(sql, cn);
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await rd.ReadAsync(cancellationToken))
            vm.Rutas.Add(new LogisticaSelectVm { Id = Entero(rd, "RutaID"), Texto = Texto(rd, "Texto") });
        if (await rd.NextResultAsync(cancellationToken))
        {
            while (await rd.ReadAsync(cancellationToken))
                vm.Unidades.Add(new LogisticaSelectVm { Id = Entero(rd, "UnidadID"), Texto = Texto(rd, "Texto") });
        }
    }

    private static async Task<(int ClienteID, string Cliente, string Destino, string Estatus)?> ObtenerHeaderAsync(
    SqlConnection cn,
    SqlTransaction tx,
    int embarqueId,
    CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT
    ClienteID,
    ISNULL(NULLIF(LTRIM(RTRIM(ClienteNombreSnapshot)),N''),N'') AS Cliente,
    ISNULL(NULLIF(LTRIM(RTRIM(Destino)),N''),N'') AS Destino,
    ISNULL(Estatus,N'') AS Estatus
FROM dbo.Logistica_Embarques WITH (UPDLOCK,HOLDLOCK)
WHERE EmbarqueID=@EmbarqueID
  AND Activo=1;";

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;

        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);

        if (!await rd.ReadAsync(cancellationToken))
            return null;

        return (
            Entero(rd, "ClienteID"),
            Texto(rd, "Cliente"),
            Texto(rd, "Destino"),
            Texto(rd, "Estatus"));
    }
    private static async Task<List<LogisticaSelectVm>> CargarClientesPendientesAsync(SqlConnection cn, CancellationToken cancellationToken)
    {
        var resultado = new List<LogisticaSelectVm>();
        const string sql = @"
SELECT ClienteID,Cliente
FROM
(
    SELECT DISTINCT ClienteID,LTRIM(RTRIM(ISNULL(Cliente,N''))) AS Cliente
    FROM dbo.vw_Logistica_DemandaRelease
    WHERE PendienteProgramar>0
      AND ClienteID IS NOT NULL
      AND ClienteID>0
      AND NULLIF(LTRIM(RTRIM(ISNULL(Cliente,N''))),N'') IS NOT NULL
) q
ORDER BY Cliente;";
        await using var cmd = new SqlCommand(sql, cn);
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await rd.ReadAsync(cancellationToken))
        {
            resultado.Add(new LogisticaSelectVm
            {
                Id = Entero(rd, "ClienteID"),
                Texto = Texto(rd, "Cliente")
            });
        }
        return resultado;
    }

    private async Task<List<LogisticaCrearPartidaVm>> CargarPartidasClienteAsync(SqlConnection cn, int clienteId, IReadOnlyCollection<LogisticaCrearPartidaVm>? seleccionActual, CancellationToken cancellationToken)
    {
        var resultado = new List<LogisticaCrearPartidaVm>();
        if (clienteId <= 0) return resultado;
        var demandas = await CargarDemandasAsync(cn, null, null, null, true, clienteId, cancellationToken);
        var anteriores = (seleccionActual ?? Array.Empty<LogisticaCrearPartidaVm>()).Where(x => x.ReleaseDetalleID > 0).GroupBy(x => x.ReleaseDetalleID).ToDictionary(x => x.Key, x => x.First());
        foreach (var demanda in demandas.Where(x => x.PendienteProgramar > 0))
        {
            anteriores.TryGetValue(demanda.ReleaseDetalleID, out var anterior);
            var cajas = await CargarCajasParaDemandaAsync(cn, demanda, cancellationToken);
            var partida = new LogisticaCrearPartidaVm
            {
                Seleccionada = anterior?.Seleccionada ?? false,
                ReleaseDetalleID = demanda.ReleaseDetalleID,
                CantidadSolicitada = anterior?.CantidadSolicitada > 0 ? Math.Min(anterior.CantidadSolicitada, demanda.PendienteProgramar) : demanda.PendienteProgramar,
                CajaIDs = anterior?.CajaIDs?.Where(id => cajas.Any(c => c.CajaID == id)).Distinct().ToList() ?? new List<int>(),
                FolioRelease = demanda.FolioRelease,
                NumeroParte = demanda.NumeroParte,
                Descripcion = demanda.Descripcion,
                NumeroOF = demanda.NumeroOF,
                FechaCarga = demanda.FechaCarga,
                FechaEntrega = demanda.FechaEntrega,
                PendienteProgramar = demanda.PendienteProgramar,
                PiezasPTDisponibles = demanda.PiezasPTDisponibles,
                CajasDisponibles = cajas
            };
            resultado.Add(partida);
        }
        return resultado;
    }
    private static async Task EjecutarAsync(
        SqlConnection cn,
        SqlTransaction tx,
        string sql,
        CancellationToken cancellationToken,
        params (string Nombre, object? Valor)[] parametros)
    {
        await using var cmd = new SqlCommand(sql, cn, tx);
        foreach (var p in parametros)
            cmd.Parameters.AddWithValue(p.Nombre, p.Valor ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static object Db(object? value) => value ?? DBNull.Value;

    private static string Texto(SqlDataReader rd, string columna)
    {
        var i = rd.GetOrdinal(columna);
        return rd.IsDBNull(i) ? string.Empty : rd.GetValue(i)?.ToString() ?? string.Empty;
    }

    private static int Entero(SqlDataReader rd, string columna)
    {
        var i = rd.GetOrdinal(columna);
        return rd.IsDBNull(i) ? 0 : Convert.ToInt32(rd.GetValue(i));
    }

    private static int? EnteroNullable(SqlDataReader rd, string columna)
    {
        var i = rd.GetOrdinal(columna);
        return rd.IsDBNull(i) ? null : Convert.ToInt32(rd.GetValue(i));
    }

    private static long EnteroLargo(SqlDataReader rd, string columna)
    {
        var i = rd.GetOrdinal(columna);
        return rd.IsDBNull(i) ? 0L : Convert.ToInt64(rd.GetValue(i));
    }

    private static DateTime? Fecha(SqlDataReader rd, string columna)
    {
        var i = rd.GetOrdinal(columna);
        return rd.IsDBNull(i) ? null : Convert.ToDateTime(rd.GetValue(i));
    }

    private static TimeSpan? Hora(SqlDataReader rd, string columna)
    {
        var i = rd.GetOrdinal(columna);
        if (rd.IsDBNull(i)) return null;
        var value = rd.GetValue(i);
        if (value is TimeSpan ts) return ts;
        return TimeSpan.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    private static bool Booleano(SqlDataReader rd, string columna)
    {
        var i = rd.GetOrdinal(columna);
        return !rd.IsDBNull(i) && Convert.ToBoolean(rd.GetValue(i));
    }
}
