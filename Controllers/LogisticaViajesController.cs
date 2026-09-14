using ERP.NSQuell.Models.ViewModels.Logistica;
using ERP.NSQuell.Servicios;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LogisticaViajesController : Controller
{
    private readonly IConfiguration _configuration;
    private readonly IServicioAcceso _acceso;
    private readonly IWebHostEnvironment _environment;
    public LogisticaViajesController(IConfiguration configuration, IServicioAcceso acceso, IWebHostEnvironment environment)
    {
        _configuration = configuration;
        _acceso = acceso;
        _environment = environment;
    }

    private string ConnectionString =>
        _configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("No se encontró ConnectionStrings:DefaultConnection.");

    private int? UsuarioID => HttpContext.Session.GetInt32("UsuarioID");

    private string UsuarioNombre =>
        HttpContext.Session.GetString("NombreMostrar")
        ?? HttpContext.Session.GetString("Username")
        ?? User?.Identity?.Name
        ?? "Usuario";

    private async Task<IActionResult?> ValidarAccesoAsync()
    {
        if (!UsuarioID.HasValue || UsuarioID.Value <= 0) return RedirectToAction("Login", "Login");
        if (!await _acceso.TienePermisoAsync(UsuarioID.Value, "Tablero de Logística"))
        {
            TempData["LogisticaError"] = "Tu usuario no tiene permiso para acceder al módulo de Viajes.";
            return RedirectToAction("Index", "Home");
        }
        return null;
    }
    private async Task<SqlConnection> AbrirAsync(CancellationToken cancellationToken)
    {
        var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(cancellationToken);
        return cn;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? q = null, string? estatus = null, string? tipoViaje = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null, int pagina = 1, CancellationToken cancellationToken = default)
    {
        var acceso = await ValidarAccesoAsync();
        if (acceso != null) return acceso;
        q = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
        estatus = NormalizarEstatusFiltro(estatus);
        tipoViaje = NormalizarTipoViaje(tipoViaje, true);
        if (fechaDesde.HasValue) fechaDesde = fechaDesde.Value.Date;
        if (fechaHasta.HasValue) fechaHasta = fechaHasta.Value.Date;
        if (fechaDesde.HasValue && fechaHasta.HasValue && fechaHasta.Value < fechaDesde.Value)
        {
            TempData["LogisticaError"] = "La fecha final no puede ser anterior a la fecha inicial.";
            return RedirectToAction(nameof(Index));
        }
        pagina = Math.Max(1, pagina);
        const int tamanoPagina = 50;
        var offset = (pagina - 1) * tamanoPagina;
        var vm = new LogisticaViajesIndexVm { Busqueda = q, Estatus = estatus, TipoViaje = tipoViaje, FechaDesde = fechaDesde, FechaHasta = fechaHasta };
        await using var cn = await AbrirAsync(cancellationToken);
        const string sqlResumen = @"
SELECT COUNT_BIG(*) TotalViajes,
SUM(CASE WHEN Estatus=N'Programado' THEN 1 ELSE 0 END) Programados,
SUM(CASE WHEN Estatus=N'En curso' THEN 1 ELSE 0 END) EnCurso,
SUM(CASE WHEN Estatus=N'Completado' THEN 1 ELSE 0 END) Completados,
SUM(CASE WHEN Estatus=N'Cancelado' THEN 1 ELSE 0 END) Cancelados,
SUM(CASE WHEN FechaProgramada=CAST(GETDATE() AS date) AND Estatus<>N'Cancelado' THEN 1 ELSE 0 END) ViajesHoy,
SUM(CASE WHEN Estatus=N'En curso' AND FechaSalidaReal IS NOT NULL AND FechaRegresoReal IS NULL THEN 1 ELSE 0 END) RetornosPendientes
FROM dbo.Logistica_Viajes
WHERE Activo=1;";
        await using (var cmd = new SqlCommand(sqlResumen, cn))
        {
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await rd.ReadAsync(cancellationToken))
            {
                vm.TotalViajes = Convert.ToInt32(EnteroLargo(rd, "TotalViajes"));
                vm.Programados = Entero(rd, "Programados");
                vm.EnCurso = Entero(rd, "EnCurso");
                vm.Completados = Entero(rd, "Completados");
                vm.Cancelados = Entero(rd, "Cancelados");
                vm.ViajesHoy = Entero(rd, "ViajesHoy");
                vm.RetornosPendientes = Entero(rd, "RetornosPendientes");
            }
        }
        const string sqlTotal = @"
SELECT COUNT_BIG(*)
FROM dbo.Logistica_Viajes v
WHERE v.Activo=1
AND
(
    @Q IS NULL
    OR v.Folio LIKE N'%'+@Q+N'%'
    OR v.Origen LIKE N'%'+@Q+N'%'
    OR v.Destino LIKE N'%'+@Q+N'%'
    OR v.Motivo LIKE N'%'+@Q+N'%'
    OR v.OperadorTexto LIKE N'%'+@Q+N'%'
    OR EXISTS
    (
        SELECT 1
        FROM dbo.Logistica_ViajeParadas p
        WHERE p.ViajeID=v.ViajeID
        AND p.Activo=1
        AND
        (
            p.Lugar LIKE N'%'+@Q+N'%'
            OR p.Direccion LIKE N'%'+@Q+N'%'
            OR p.EntidadNombreSnapshot LIKE N'%'+@Q+N'%'
            OR p.ReferenciaFolioSnapshot LIKE N'%'+@Q+N'%'
            OR p.TipoOperacion LIKE N'%'+@Q+N'%'
        )
    )
)
AND(@Estatus IS NULL OR v.Estatus=@Estatus)
AND(@TipoViaje IS NULL OR v.TipoViaje=@TipoViaje)
AND(@FechaDesde IS NULL OR v.FechaProgramada>=@FechaDesde)
AND(@FechaHasta IS NULL OR v.FechaProgramada<=@FechaHasta);";
        long totalFiltrado;
        await using (var cmd = new SqlCommand(sqlTotal, cn))
        {
            AgregarFiltros(cmd, q, estatus, tipoViaje, fechaDesde, fechaHasta);
            totalFiltrado = Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken));
        }
        const string sql = @"
SELECT v.ViajeID,
ISNULL(v.Folio,N'') Folio,
ISNULL(v.TipoViaje,N'') TipoViaje,
ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(origen.Lugar,N''))),N''),ISNULL(v.Origen,N'')) Origen,
CASE WHEN ISNULL(v.EsMultiParada,0)=1 OR ISNULL(paradas.Operativas,0)>1
THEN ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(destino.Lugar,N''))),N''),ISNULL(v.Destino,N''))
ELSE ISNULL(v.Destino,N'') END Destino,
ISNULL(v.Motivo,N'') Motivo,
v.FechaProgramada,
v.HoraSalidaProgramada,
v.FechaSalidaReal,
v.FechaRegresoReal,
ISNULL(u.NumeroEconomico+CASE WHEN NULLIF(LTRIM(RTRIM(ISNULL(u.Placas,N''))),N'') IS NULL THEN N'' ELSE N' - '+LTRIM(RTRIM(u.Placas)) END,N'') Unidad,
ISNULL(NULLIF(LTRIM(RTRIM(v.OperadorNombreSnapshot)),N''),ISNULL(v.OperadorTexto,N'')) Operador,
ISNULL(v.Estatus,N'') Estatus,
ISNULL(v.TieneIncidencia,0) TieneIncidencia,
CONVERT(bit,CASE WHEN ISNULL(v.EsMultiParada,0)=1 OR ISNULL(paradas.Operativas,0)>1 THEN 1 ELSE 0 END) EsMultiParada,
ISNULL(paradas.TotalParadas,0) TotalParadas,
ISNULL(paradas.ParadasCompletadas,0) ParadasCompletadas,
ISNULL(paradas.ParadasPendientes,0) ParadasPendientes,
proxima.ViajeParadaID ProximaParadaID,
proxima.Secuencia ProximaSecuencia,
ISNULL(proxima.TipoParada,N'') ProximaTipoParada,
ISNULL(proxima.TipoOperacion,N'') ProximaOperacion,
ISNULL(proxima.Lugar,N'') ProximaParada,
ISNULL(proxima.Direccion,N'') ProximaDireccion,
proxima.FechaHoraLlegadaProgramada ProximaLlegadaProgramada,
ISNULL(proxima.Estatus,N'') ProximaParadaEstatus
FROM dbo.Logistica_Viajes v
LEFT JOIN dbo.Logistica_Unidades u ON u.UnidadID=v.UnidadID
OUTER APPLY
(
    SELECT CONVERT(int,COUNT_BIG(*)) TotalParadas,
    ISNULL(SUM(CASE WHEN p.Estatus IN(N'Completada',N'Omitida',N'Cancelada') THEN 1 ELSE 0 END),0) ParadasCompletadas,
    ISNULL(SUM(CASE WHEN p.Estatus IN(N'Pendiente',N'En camino',N'En sitio') THEN 1 ELSE 0 END),0) ParadasPendientes,
    ISNULL(SUM(CASE WHEN p.TipoParada<>N'Origen' AND ISNULL(p.CierraViaje,0)=0 THEN 1 ELSE 0 END),0) Operativas
    FROM dbo.Logistica_ViajeParadas p
    WHERE p.ViajeID=v.ViajeID AND p.Activo=1
) paradas
OUTER APPLY
(
    SELECT TOP(1) p.Lugar
    FROM dbo.Logistica_ViajeParadas p
    WHERE p.ViajeID=v.ViajeID AND p.Activo=1 AND p.TipoParada=N'Origen'
    ORDER BY p.Secuencia,p.ViajeParadaID
) origen
OUTER APPLY
(
    SELECT TOP(1) p.Lugar
    FROM dbo.Logistica_ViajeParadas p
    WHERE p.ViajeID=v.ViajeID AND p.Activo=1
    ORDER BY p.Secuencia DESC,p.ViajeParadaID DESC
) destino
OUTER APPLY
(
    SELECT TOP(1) p.ViajeParadaID,p.Secuencia,p.TipoParada,p.TipoOperacion,p.Lugar,p.Direccion,p.FechaHoraLlegadaProgramada,p.Estatus,p.CierraViaje
    FROM dbo.Logistica_ViajeParadas p
    WHERE p.ViajeID=v.ViajeID
    AND p.Activo=1
    AND p.TipoParada<>N'Origen'
    AND p.Estatus IN(N'Pendiente',N'En camino',N'En sitio')
    ORDER BY CASE WHEN ISNULL(p.CierraViaje,0)=1 THEN 1 ELSE 0 END,p.Secuencia,p.ViajeParadaID
) proxima
WHERE v.Activo=1
AND
(
    @Q IS NULL
    OR v.Folio LIKE N'%'+@Q+N'%'
    OR v.Origen LIKE N'%'+@Q+N'%'
    OR v.Destino LIKE N'%'+@Q+N'%'
    OR v.Motivo LIKE N'%'+@Q+N'%'
    OR v.OperadorTexto LIKE N'%'+@Q+N'%'
    OR EXISTS
    (
        SELECT 1
        FROM dbo.Logistica_ViajeParadas px
        WHERE px.ViajeID=v.ViajeID
        AND px.Activo=1
        AND
        (
            px.Lugar LIKE N'%'+@Q+N'%'
            OR px.Direccion LIKE N'%'+@Q+N'%'
            OR px.EntidadNombreSnapshot LIKE N'%'+@Q+N'%'
            OR px.ReferenciaFolioSnapshot LIKE N'%'+@Q+N'%'
            OR px.TipoOperacion LIKE N'%'+@Q+N'%'
        )
    )
)
AND(@Estatus IS NULL OR v.Estatus=@Estatus)
AND(@TipoViaje IS NULL OR v.TipoViaje=@TipoViaje)
AND(@FechaDesde IS NULL OR v.FechaProgramada>=@FechaDesde)
AND(@FechaHasta IS NULL OR v.FechaProgramada<=@FechaHasta)
ORDER BY CASE v.Estatus WHEN N'En curso' THEN 1 WHEN N'Programado' THEN 2 WHEN N'Completado' THEN 3 WHEN N'Cancelado' THEN 4 ELSE 5 END,
v.FechaProgramada DESC,v.HoraSalidaProgramada DESC,v.ViajeID DESC
OFFSET @Offset ROWS FETCH NEXT @TamanoPagina ROWS ONLY;";
        await using (var cmd = new SqlCommand(sql, cn))
        {
            AgregarFiltros(cmd, q, estatus, tipoViaje, fechaDesde, fechaHasta);
            cmd.Parameters.Add("@Offset", SqlDbType.Int).Value = offset;
            cmd.Parameters.Add("@TamanoPagina", SqlDbType.Int).Value = tamanoPagina;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await rd.ReadAsync(cancellationToken))
            {
                vm.Viajes.Add(new LogisticaViajeResumenVm
                {
                    ViajeID = Entero(rd, "ViajeID"),
                    Folio = Texto(rd, "Folio"),
                    TipoViaje = Texto(rd, "TipoViaje"),
                    Origen = Texto(rd, "Origen"),
                    Destino = Texto(rd, "Destino"),
                    Motivo = Texto(rd, "Motivo"),
                    FechaProgramada = Fecha(rd, "FechaProgramada") ?? DateTime.MinValue,
                    HoraSalidaProgramada = Hora(rd, "HoraSalidaProgramada"),
                    FechaSalidaReal = Fecha(rd, "FechaSalidaReal"),
                    FechaRegresoReal = Fecha(rd, "FechaRegresoReal"),
                    Unidad = Texto(rd, "Unidad"),
                    Operador = Texto(rd, "Operador"),
                    Estatus = Texto(rd, "Estatus"),
                    TieneIncidencia = Booleano(rd, "TieneIncidencia"),
                    EsMultiParada = Booleano(rd, "EsMultiParada"),
                    TotalParadas = Entero(rd, "TotalParadas"),
                    ParadasCompletadas = Entero(rd, "ParadasCompletadas"),
                    ParadasPendientes = Entero(rd, "ParadasPendientes"),
                    ProximaParadaID = EnteroNullable(rd, "ProximaParadaID"),
                    ProximaSecuencia = EnteroNullable(rd, "ProximaSecuencia"),
                    ProximaTipoParada = Texto(rd, "ProximaTipoParada"),
                    ProximaOperacion = Texto(rd, "ProximaOperacion"),
                    ProximaParada = Texto(rd, "ProximaParada"),
                    ProximaDireccion = Texto(rd, "ProximaDireccion"),
                    ProximaLlegadaProgramada = Fecha(rd, "ProximaLlegadaProgramada"),
                    ProximaParadaEstatus = Texto(rd, "ProximaParadaEstatus")
                });
            }
        }
        ViewBag.PaginaActual = pagina;
        ViewBag.TamanoPagina = tamanoPagina;
        ViewBag.TotalRegistros = totalFiltrado;
        ViewBag.TotalPaginas = Math.Max(1, (int)Math.Ceiling(totalFiltrado / (double)tamanoPagina));
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Crear(CancellationToken cancellationToken)
    {
        var acceso = await ValidarAccesoAsync();
        if (acceso != null) return acceso;
        await using var cn = await AbrirAsync(cancellationToken);
        var vm = new LogisticaViajeCrearVm { FechaProgramada = DateTime.Today };
        await CargarCatalogosAsync(vm, cn, cancellationToken);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Crear(LogisticaViajeCrearVm model, CancellationToken cancellationToken)
    {
        var acceso = await ValidarAccesoAsync();
        if (acceso != null) return acceso;
        Normalizar(model);
        model.EsMultiParada = string.Equals(model.TipoViaje, "Ruta multipropósito", StringComparison.OrdinalIgnoreCase);
        ValidarModelo(model);
        if (model.TipoViaje == "Entrega PT") ModelState.AddModelError(nameof(model.TipoViaje), "Las Entregas de producto terminado se crean desde Centro Operativo para conservar la relación Embarque ↔ Viaje.");
        await using var cn = await AbrirAsync(cancellationToken);
        if (!ModelState.IsValid)
        {
            await CargarCatalogosAsync(model, cn, cancellationToken);
            return View(model);
        }
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var operadorNombre = await ValidarRecursosInternosAsync(cn, tx, model.UnidadID, model.OperadorUsuarioID, cancellationToken);
            await ValidarDisponibilidadViajeAsync(cn, tx, model.FechaProgramada, model.HoraSalidaProgramada, model.UnidadID, model.OperadorUsuarioID, null, cancellationToken);
            if (model.RutaID.HasValue && model.RutaID.Value > 0)
            {
                const string sqlRuta = "SELECT COUNT_BIG(*) FROM dbo.Logistica_Rutas WITH(UPDLOCK,HOLDLOCK) WHERE RutaID=@RutaID AND Activo=1;";
                await using var cmdRuta = new SqlCommand(sqlRuta, cn, tx);
                cmdRuta.Parameters.Add("@RutaID", SqlDbType.Int).Value = model.RutaID.Value;
                if (Convert.ToInt64(await cmdRuta.ExecuteScalarAsync(cancellationToken)) <= 0) throw new InvalidOperationException("La ruta seleccionada no existe o está inactiva.");
            }
            const string sql = @"
INSERT dbo.Logistica_Viajes
(Folio,TipoViaje,TipoTransporte,Origen,Destino,Motivo,FechaProgramada,HoraSalidaProgramada,HoraRegresoProgramada,RutaID,UnidadID,OperadorUsuarioID,OperadorNombreSnapshot,OperadorTexto,TransportistaExterno,UnidadExterna,PlacasExternas,ChoferExterno,Estatus,TieneIncidencia,EsMultiParada,Observaciones,ResponsableUsuarioID,ResponsableNombreSnapshot,FechaCreacion,CreadoPor,Activo)
VALUES
(NULL,@TipoViaje,N'Interno',@Origen,@Destino,@Motivo,@FechaProgramada,@HoraSalidaProgramada,NULL,@RutaID,@UnidadID,@OperadorUsuarioID,@OperadorNombreSnapshot,@OperadorTexto,NULL,NULL,NULL,NULL,N'Programado',0,@EsMultiParada,@Observaciones,@UsuarioID,@UsuarioNombre,SYSDATETIME(),@UsuarioNombre,1);
SELECT CONVERT(int,SCOPE_IDENTITY());";
            int viajeId;
            await using (var cmd = new SqlCommand(sql, cn, tx))
            {
                cmd.Parameters.Add("@TipoViaje", SqlDbType.NVarChar, 50).Value = model.TipoViaje;
                cmd.Parameters.Add("@Origen", SqlDbType.NVarChar, 300).Value = model.Origen;
                cmd.Parameters.Add("@Destino", SqlDbType.NVarChar, 300).Value = model.Destino;
                cmd.Parameters.Add("@Motivo", SqlDbType.NVarChar, 500).Value = model.Motivo;
                cmd.Parameters.Add("@FechaProgramada", SqlDbType.Date).Value = model.FechaProgramada.Date;
                cmd.Parameters.Add("@HoraSalidaProgramada", SqlDbType.Time).Value = Db(model.HoraSalidaProgramada);
                cmd.Parameters.Add("@RutaID", SqlDbType.Int).Value = Db(model.RutaID);
                cmd.Parameters.Add("@UnidadID", SqlDbType.Int).Value = Db(model.UnidadID);
                cmd.Parameters.Add("@OperadorUsuarioID", SqlDbType.Int).Value = Db(model.OperadorUsuarioID);
                cmd.Parameters.Add("@OperadorNombreSnapshot", SqlDbType.NVarChar, 200).Value = Db(operadorNombre);
                cmd.Parameters.Add("@OperadorTexto", SqlDbType.NVarChar, 200).Value = Db(operadorNombre);
                cmd.Parameters.Add("@EsMultiParada", SqlDbType.Bit).Value = model.EsMultiParada;
                cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1200).Value = Db(model.Observaciones);
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = Db(UsuarioID);
                cmd.Parameters.Add("@UsuarioNombre", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                viajeId = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
            }
            var folio = $"VIA-{DateTime.Today:yyyy}-{viajeId:000000}";
            await EjecutarAsync(cn, tx, "UPDATE dbo.Logistica_Viajes SET Folio=@Folio WHERE ViajeID=@ViajeID;", cancellationToken, ("@Folio", folio), ("@ViajeID", viajeId));
            if (model.EsMultiParada) await AsegurarParadasBaseViajeAsync(cn, tx, viajeId, model.Origen, model.Destino, model.FechaProgramada, model.HoraSalidaProgramada, cancellationToken);
            var pendientes = new List<string>();
            if (!model.HoraSalidaProgramada.HasValue) pendientes.Add("hora de salida");
            if (!model.RutaID.HasValue) pendientes.Add("ruta");
            if (!model.UnidadID.HasValue) pendientes.Add("unidad");
            if (!model.OperadorUsuarioID.HasValue) pendientes.Add("chofer");
            if (model.EsMultiParada) pendientes.Add("paradas operativas");
            var detallePendientes = pendientes.Count > 0 ? $" Pendiente completar antes de iniciar: {string.Join(", ", pendientes.Distinct())}." : " Recursos completos.";
            await InsertarHistorialAsync(cn, tx, viajeId, "VIAJE_CREADO", null, "Programado", $"Viaje creado. Tipo: {model.TipoViaje}. Origen: {model.Origen}. Destino: {model.Destino}.{detallePendientes}", cancellationToken);
            await tx.CommitAsync(cancellationToken);
            TempData["LogisticaOk"] = model.EsMultiParada ? $"{folio} creado. Ahora agrega las paradas del recorrido desde el Detalle." : pendientes.Count > 0 ? $"{folio} programado. Completa {string.Join(", ", pendientes)} antes de iniciarlo." : $"{folio} programado correctamente.";
            return RedirectToAction(nameof(Detalle), new { id = viajeId });
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(cancellationToken); } catch { }
            ModelState.AddModelError("", ex.Message);
            await CargarCatalogosAsync(model, cn, cancellationToken);
            return View(model);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Detalle(int id, CancellationToken cancellationToken)
    {
        var acceso = await ValidarAccesoAsync();
        if (acceso != null) return acceso;
        if (id <= 0) return NotFound();
        await using var cn = await AbrirAsync(cancellationToken);
        var vm = await CargarDetalleAsync(cn, id, cancellationToken);
        return vm == null ? NotFound() : View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Editar(int id, CancellationToken cancellationToken)
    {
        var acceso = await ValidarAccesoAsync();
        if (acceso != null) return acceso;
        if (id <= 0) return NotFound();
        await using var cn = await AbrirAsync(cancellationToken);
        const string sql = @"
SELECT ViajeID,TipoViaje,Origen,Destino,Motivo,FechaProgramada,HoraSalidaProgramada,RutaID,UnidadID,OperadorUsuarioID,
ISNULL(NULLIF(LTRIM(RTRIM(OperadorNombreSnapshot)),N''),OperadorTexto) OperadorTexto,Observaciones,Estatus,ISNULL(EsMultiParada,0) EsMultiParada
FROM dbo.Logistica_Viajes
WHERE ViajeID=@ViajeID AND Activo=1;";
        LogisticaViajeEditarVm? vm = null;
        string estatus;
        await using (var cmd = new SqlCommand(sql, cn))
        {
            cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = id;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await rd.ReadAsync(cancellationToken)) return NotFound();
            estatus = Texto(rd, "Estatus");
            vm = new LogisticaViajeEditarVm
            {
                ViajeID = Entero(rd, "ViajeID"),
                TipoViaje = Texto(rd, "TipoViaje"),
                Origen = Texto(rd, "Origen"),
                Destino = Texto(rd, "Destino"),
                Motivo = Texto(rd, "Motivo"),
                FechaProgramada = Fecha(rd, "FechaProgramada") ?? DateTime.Today,
                HoraSalidaProgramada = Hora(rd, "HoraSalidaProgramada"),
                RutaID = EnteroNullable(rd, "RutaID"),
                UnidadID = EnteroNullable(rd, "UnidadID"),
                OperadorUsuarioID = EnteroNullable(rd, "OperadorUsuarioID"),
                OperadorTexto = TextoNullable(rd, "OperadorTexto"),
                Observaciones = TextoNullable(rd, "Observaciones"),
                EsMultiParada = Booleano(rd, "EsMultiParada")
            };
        }
        await CargarParadasEdicionAsync(cn, vm, cancellationToken);
        if (estatus != "Programado")
        {
            TempData["LogisticaError"] = "Solo los viajes Programados pueden completar o modificar su preparación.";
            return RedirectToAction(nameof(Detalle), new { id });
        }
        await CargarCatalogosAsync(vm, cn, cancellationToken);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Editar(LogisticaViajeEditarVm model, CancellationToken cancellationToken)
    {
        var acceso = await ValidarAccesoAsync();
        if (acceso != null) return acceso;
        Normalizar(model);
        ValidarModelo(model);
        if (model.TipoViaje == "Entrega PT") ModelState.AddModelError(nameof(model.TipoViaje), "Las Entregas PT vinculadas a Embarques se administran desde Centro Operativo.");
        await using var cn = await AbrirAsync(cancellationToken);
        if (!ModelState.IsValid)
        {
            await CargarCatalogosAsync(model, cn, cancellationToken);
            return View(model);
        }
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var actual = await ObtenerViajeParaActualizarAsync(cn, tx, model.ViajeID, cancellationToken) ?? throw new InvalidOperationException("El viaje no existe.");
            if (actual.Estatus != "Programado") throw new InvalidOperationException("Solo los viajes Programados pueden completar su preparación.");
            var embarque = await ObtenerEmbarqueActivoVinculadoAsync(cn, tx, model.ViajeID, cancellationToken);
            if (embarque.HasValue) throw new InvalidOperationException($"El viaje está vinculado al embarque {embarque.Value.Folio}. Su programación debe modificarse desde Centro Operativo.");
            const string sqlConteo = @"SELECT COUNT_BIG(*) FROM dbo.Logistica_ViajeParadas WITH(UPDLOCK,HOLDLOCK) WHERE ViajeID=@ViajeID AND Activo=1 AND TipoParada<>N'Origen' AND ISNULL(CierraViaje,0)=0;";
            long operativas;
            await using (var cmd = new SqlCommand(sqlConteo, cn, tx))
            {
                cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = model.ViajeID;
                operativas = Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken));
            }
            model.EsMultiParada = string.Equals(model.TipoViaje, "Ruta multipropósito", StringComparison.OrdinalIgnoreCase) || operativas > 1;
            var operadorNombre = await ValidarRecursosInternosAsync(cn, tx, model.UnidadID, model.OperadorUsuarioID, cancellationToken);
            await ValidarDisponibilidadViajeAsync(cn, tx, model.FechaProgramada, model.HoraSalidaProgramada, model.UnidadID, model.OperadorUsuarioID, model.ViajeID, cancellationToken);
            if (model.RutaID.HasValue && model.RutaID.Value > 0)
            {
                const string sqlRuta = "SELECT COUNT_BIG(*) FROM dbo.Logistica_Rutas WITH(UPDLOCK,HOLDLOCK) WHERE RutaID=@RutaID AND Activo=1;";
                await using var cmdRuta = new SqlCommand(sqlRuta, cn, tx);
                cmdRuta.Parameters.Add("@RutaID", SqlDbType.Int).Value = model.RutaID.Value;
                if (Convert.ToInt64(await cmdRuta.ExecuteScalarAsync(cancellationToken)) <= 0) throw new InvalidOperationException("La ruta seleccionada no existe o está inactiva.");
            }
            const string sql = @"
UPDATE dbo.Logistica_Viajes
SET TipoViaje=@TipoViaje,TipoTransporte=N'Interno',Origen=@Origen,Destino=@Destino,Motivo=@Motivo,FechaProgramada=@FechaProgramada,
HoraSalidaProgramada=@HoraSalidaProgramada,HoraRegresoProgramada=NULL,RutaID=@RutaID,UnidadID=@UnidadID,OperadorUsuarioID=@OperadorUsuarioID,
OperadorNombreSnapshot=@OperadorNombreSnapshot,OperadorTexto=@OperadorTexto,TransportistaExterno=NULL,UnidadExterna=NULL,PlacasExternas=NULL,
ChoferExterno=NULL,EsMultiParada=@EsMultiParada,Observaciones=@Observaciones,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
WHERE ViajeID=@ViajeID AND Activo=1 AND Estatus=N'Programado';
SELECT @@ROWCOUNT;";
            await using (var cmd = new SqlCommand(sql, cn, tx))
            {
                cmd.Parameters.Add("@TipoViaje", SqlDbType.NVarChar, 50).Value = model.TipoViaje;
                cmd.Parameters.Add("@Origen", SqlDbType.NVarChar, 300).Value = model.Origen;
                cmd.Parameters.Add("@Destino", SqlDbType.NVarChar, 300).Value = model.Destino;
                cmd.Parameters.Add("@Motivo", SqlDbType.NVarChar, 500).Value = model.Motivo;
                cmd.Parameters.Add("@FechaProgramada", SqlDbType.Date).Value = model.FechaProgramada.Date;
                cmd.Parameters.Add("@HoraSalidaProgramada", SqlDbType.Time).Value = Db(model.HoraSalidaProgramada);
                cmd.Parameters.Add("@RutaID", SqlDbType.Int).Value = Db(model.RutaID);
                cmd.Parameters.Add("@UnidadID", SqlDbType.Int).Value = Db(model.UnidadID);
                cmd.Parameters.Add("@OperadorUsuarioID", SqlDbType.Int).Value = Db(model.OperadorUsuarioID);
                cmd.Parameters.Add("@OperadorNombreSnapshot", SqlDbType.NVarChar, 200).Value = Db(operadorNombre);
                cmd.Parameters.Add("@OperadorTexto", SqlDbType.NVarChar, 200).Value = Db(operadorNombre);
                cmd.Parameters.Add("@EsMultiParada", SqlDbType.Bit).Value = model.EsMultiParada;
                cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1200).Value = Db(model.Observaciones);
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = model.ViajeID;
                if (Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) == 0) throw new InvalidOperationException("El viaje cambió mientras se intentaba actualizar.");
            }
            if (model.EsMultiParada || await ViajeTieneParadasActivasAsync(cn, tx, model.ViajeID, cancellationToken))
                await AsegurarParadasBaseViajeAsync(cn, tx, model.ViajeID, model.Origen, model.Destino, model.FechaProgramada, model.HoraSalidaProgramada, cancellationToken);
            await ActualizarIndicadorMultiParadaAsync(cn, tx, model.ViajeID, cancellationToken);
            var pendientes = new List<string>();
            if (!model.HoraSalidaProgramada.HasValue) pendientes.Add("hora de salida");
            if (!model.RutaID.HasValue) pendientes.Add("ruta");
            if (!model.UnidadID.HasValue) pendientes.Add("unidad");
            if (!model.OperadorUsuarioID.HasValue) pendientes.Add("chofer");
            var textoPendientes = pendientes.Count > 0 ? $" Pendiente: {string.Join(", ", pendientes)}." : " El viaje ya tiene todos los recursos requeridos.";
            await InsertarHistorialAsync(cn, tx, model.ViajeID, "PREPARACION_ACTUALIZADA", "Programado", "Programado", $"Preparación actualizada. Tipo: {model.TipoViaje}. Origen: {model.Origen}. Destino: {model.Destino}.{textoPendientes}", cancellationToken);
            await tx.CommitAsync(cancellationToken);
            TempData["LogisticaOk"] = pendientes.Count > 0 ? $"Preparación actualizada. Falta: {string.Join(", ", pendientes)}." : "Preparación actualizada correctamente.";
            return RedirectToAction(nameof(Detalle), new { id = model.ViajeID });
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(cancellationToken); } catch { }
            ModelState.AddModelError("", ex.Message);
            await CargarCatalogosAsync(model, cn, cancellationToken);
            return View(model);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegistrarSalida(LogisticaViajeSalidaVm model, CancellationToken cancellationToken)
    {
        var acceso = await ValidarAccesoAsync();
        if (acceso != null) return acceso;
        if (model.ViajeID <= 0)
        {
            TempData["LogisticaError"] = "El viaje indicado no es válido.";
            return RedirectToAction(nameof(Index));
        }
        model.Observaciones = model.Observaciones?.Trim();
        if (model.FechaSalida == default) ModelState.AddModelError(nameof(model.FechaSalida), "La fecha de salida es obligatoria.");
        if (model.FechaSalida > DateTime.Now.AddMinutes(5)) ModelState.AddModelError(nameof(model.FechaSalida), "La fecha de salida no puede estar en el futuro.");
        if (!model.KilometrajeSalida.HasValue) ModelState.AddModelError(nameof(model.KilometrajeSalida), "El kilometraje inicial es obligatorio.");
        else if (model.KilometrajeSalida.Value < 0) ModelState.AddModelError(nameof(model.KilometrajeSalida), "El kilometraje inicial no puede ser negativo.");
        if (!ModelState.IsValid)
        {
            TempData["LogisticaError"] = ObtenerErroresModelState();
            return RedirectToAction(nameof(Detalle), new { id = model.ViajeID });
        }
        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var embarque = await ObtenerEmbarqueActivoVinculadoAsync(cn, tx, model.ViajeID, cancellationToken);
            if (embarque.HasValue) throw new InvalidOperationException($"Este viaje corresponde al embarque {embarque.Value.Folio}. La salida física debe confirmarla el chofer asignado desde su portal.");
            if (await ViajeTieneParadasActivasAsync(cn, tx, model.ViajeID, cancellationToken))
                throw new InvalidOperationException("Este viaje tiene un recorrido por paradas. La salida debe confirmarla el chofer desde su portal para iniciar correctamente la ruta.");
            await ValidarViajeListoParaIniciarAsync(cn, tx, model.ViajeID, cancellationToken);
            const string sqlIncidencias = @"SELECT COUNT_BIG(*) FROM dbo.Logistica_ViajeIncidencias WITH(UPDLOCK,HOLDLOCK) WHERE ViajeID=@ViajeID AND Activo=1 AND Estatus IN(N'Abierta',N'En seguimiento') AND Severidad=N'Crítica';";
            await using (var cmd = new SqlCommand(sqlIncidencias, cn, tx))
            {
                cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = model.ViajeID;
                if (Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken)) > 0) throw new InvalidOperationException("El viaje tiene una incidencia crítica abierta. Ciérrala antes de iniciar.");
            }
            const string sql = @"
UPDATE dbo.Logistica_Viajes
SET Estatus=N'En curso',FechaSalidaReal=@FechaSalida,KilometrajeSalida=@KilometrajeSalida,
Observaciones=CASE WHEN @Observaciones IS NULL THEN Observaciones WHEN NULLIF(LTRIM(RTRIM(ISNULL(Observaciones,N''))),N'') IS NULL THEN @Observaciones ELSE CONCAT(Observaciones,NCHAR(13),NCHAR(10),@Observaciones) END,
FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
WHERE ViajeID=@ViajeID AND Activo=1 AND Estatus=N'Programado';
SELECT @@ROWCOUNT;";
            await using (var cmd = new SqlCommand(sql, cn, tx))
            {
                cmd.Parameters.Add("@FechaSalida", SqlDbType.DateTime2).Value = model.FechaSalida;
                cmd.Parameters.Add("@KilometrajeSalida", SqlDbType.Int).Value = model.KilometrajeSalida.Value;
                cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1000).Value = Db(model.Observaciones);
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = model.ViajeID;
                if (Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) == 0) throw new InvalidOperationException("El viaje cambió mientras se registraba la salida.");
            }
            var historial = $"Viaje independiente iniciado el {model.FechaSalida:dd/MM/yyyy HH:mm}. Kilometraje inicial: {model.KilometrajeSalida.Value:N0} km.";
            if (!string.IsNullOrWhiteSpace(model.Observaciones)) historial += $" Observaciones: {model.Observaciones}";
            await InsertarHistorialAsync(cn, tx, model.ViajeID, "VIAJE_INICIADO", "Programado", "En curso", historial, cancellationToken);
            await tx.CommitAsync(cancellationToken);
            TempData["LogisticaOk"] = "Viaje independiente iniciado correctamente.";
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(cancellationToken); } catch { }
            TempData["LogisticaError"] = ex.Message;
        }
        return RedirectToAction(nameof(Detalle), new { id = model.ViajeID });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegistrarRetorno(LogisticaViajeRetornoVm model, CancellationToken cancellationToken)
    {
        var acceso = await ValidarAccesoAsync();
        if (acceso != null) return acceso;
        if (model.ViajeID <= 0)
        {
            TempData["LogisticaError"] = "El viaje indicado no es válido.";
            return RedirectToAction(nameof(Index));
        }
        model.Observaciones = model.Observaciones?.Trim();
        if (model.FechaRegreso == default) ModelState.AddModelError(nameof(model.FechaRegreso), "La fecha de regreso es obligatoria.");
        if (model.FechaRegreso > DateTime.Now.AddMinutes(5)) ModelState.AddModelError(nameof(model.FechaRegreso), "La fecha de regreso no puede estar en el futuro.");
        if (!model.KilometrajeRegreso.HasValue) ModelState.AddModelError(nameof(model.KilometrajeRegreso), "El kilometraje final es obligatorio.");
        else if (model.KilometrajeRegreso.Value < 0) ModelState.AddModelError(nameof(model.KilometrajeRegreso), "El kilometraje final no puede ser negativo.");
        if (model.PagoGasolina.HasValue && model.PagoGasolina.Value < 0) ModelState.AddModelError(nameof(model.PagoGasolina), "El pago de gasolina no puede ser negativo.");
        if (!ModelState.IsValid)
        {
            TempData["LogisticaError"] = ObtenerErroresModelState();
            return RedirectToAction(nameof(Detalle), new { id = model.ViajeID });
        }
        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var embarque = await ObtenerEmbarqueActivoVinculadoAsync(cn, tx, model.ViajeID, cancellationToken);
            if (embarque.HasValue) throw new InvalidOperationException($"Este viaje corresponde al embarque {embarque.Value.Folio}. El regreso debe registrarlo el chofer desde su portal.");
            if (await ViajeTieneParadasActivasAsync(cn, tx, model.ViajeID, cancellationToken))
                throw new InvalidOperationException("Este viaje utiliza un recorrido por paradas. El retorno debe registrarlo el chofer desde su portal después de resolver toda la ruta.");
            const string sqlActual = @"SELECT Estatus,FechaSalidaReal,FechaRegresoReal,KilometrajeSalida FROM dbo.Logistica_Viajes WITH(UPDLOCK,HOLDLOCK) WHERE ViajeID=@ViajeID AND Activo=1;";
            string estatus;
            DateTime? fechaSalida, fechaRegresoActual;
            int? kilometrajeSalida;
            await using (var cmd = new SqlCommand(sqlActual, cn, tx))
            {
                cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = model.ViajeID;
                await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
                if (!await rd.ReadAsync(cancellationToken)) throw new InvalidOperationException("El viaje no existe.");
                estatus = Texto(rd, "Estatus");
                fechaSalida = Fecha(rd, "FechaSalidaReal");
                fechaRegresoActual = Fecha(rd, "FechaRegresoReal");
                kilometrajeSalida = EnteroNullable(rd, "KilometrajeSalida");
            }
            if (estatus != "En curso") throw new InvalidOperationException("Solo un viaje En curso puede registrar regreso.");
            if (!fechaSalida.HasValue) throw new InvalidOperationException("El viaje no tiene una salida registrada.");
            if (!kilometrajeSalida.HasValue) throw new InvalidOperationException("El viaje no tiene kilometraje inicial registrado.");
            if (fechaRegresoActual.HasValue) throw new InvalidOperationException("El regreso ya fue registrado.");
            if (model.FechaRegreso < fechaSalida.Value) throw new InvalidOperationException("La fecha de regreso no puede ser anterior a la salida.");
            if (model.KilometrajeRegreso!.Value < kilometrajeSalida.Value) throw new InvalidOperationException($"El kilometraje final no puede ser menor al inicial ({kilometrajeSalida.Value:N0} km).");
            const string sql = @"
UPDATE dbo.Logistica_Viajes
SET Estatus=N'Completado',FechaRegresoReal=@FechaRegreso,KilometrajeRegreso=@KilometrajeRegreso,PagoGasolina=@PagoGasolina,
Observaciones=CASE WHEN @Observaciones IS NULL THEN Observaciones WHEN NULLIF(LTRIM(RTRIM(ISNULL(Observaciones,N''))),N'') IS NULL THEN @Observaciones ELSE CONCAT(Observaciones,NCHAR(13),NCHAR(10),@Observaciones) END,
FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
WHERE ViajeID=@ViajeID AND Activo=1 AND Estatus=N'En curso' AND FechaRegresoReal IS NULL;
SELECT @@ROWCOUNT;";
            await using (var cmd = new SqlCommand(sql, cn, tx))
            {
                cmd.Parameters.Add("@FechaRegreso", SqlDbType.DateTime2).Value = model.FechaRegreso;
                cmd.Parameters.Add("@KilometrajeRegreso", SqlDbType.Int).Value = model.KilometrajeRegreso.Value;
                var pGasolina = cmd.Parameters.Add("@PagoGasolina", SqlDbType.Decimal);
                pGasolina.Precision = 18;
                pGasolina.Scale = 2;
                pGasolina.Value = Db(model.PagoGasolina);
                cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1000).Value = Db(model.Observaciones);
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = model.ViajeID;
                if (Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) == 0) throw new InvalidOperationException("El viaje cambió mientras se registraba el regreso.");
            }
            var kmUtilizados = model.KilometrajeRegreso.Value - kilometrajeSalida.Value;
            var historial = $"Regreso de viaje independiente registrado el {model.FechaRegreso:dd/MM/yyyy HH:mm}. Kilometraje final: {model.KilometrajeRegreso.Value:N0} km. KM utilizados: {kmUtilizados:N0} km.";
            if (model.PagoGasolina.HasValue) historial += $" Pago de gasolina: ${model.PagoGasolina.Value:N2}.";
            if (!string.IsNullOrWhiteSpace(model.Observaciones)) historial += $" Observaciones: {model.Observaciones}";
            await InsertarHistorialAsync(cn, tx, model.ViajeID, "RETORNO_REGISTRADO", "En curso", "Completado", historial, cancellationToken);
            await tx.CommitAsync(cancellationToken);
            TempData["LogisticaOk"] = $"Regreso registrado. Recorrido total: {kmUtilizados:N0} km.";
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(cancellationToken); } catch { }
            TempData["LogisticaError"] = ex.Message;
        }
        return RedirectToAction(nameof(Detalle), new { id = model.ViajeID });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancelar(LogisticaViajeCancelarVm model, CancellationToken cancellationToken)
    {
        var acceso = await ValidarAccesoAsync();
        if (acceso != null) return acceso;
        model.Motivo = model.Motivo?.Trim() ?? string.Empty;
        if (model.ViajeID <= 0 || string.IsNullOrWhiteSpace(model.Motivo))
        {
            TempData["LogisticaError"] = "El viaje y el motivo de cancelación son obligatorios.";
            return model.ViajeID > 0 ? RedirectToAction(nameof(Detalle), new { id = model.ViajeID }) : RedirectToAction(nameof(Index));
        }
        if (model.Motivo.Length > 1000)
        {
            TempData["LogisticaError"] = "El motivo no puede exceder 1,000 caracteres.";
            return RedirectToAction(nameof(Detalle), new { id = model.ViajeID });
        }
        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var viaje = await ObtenerViajeParaActualizarAsync(cn, tx, model.ViajeID, cancellationToken) ?? throw new InvalidOperationException("El viaje no existe.");
            if (viaje.Estatus != "Programado") throw new InvalidOperationException("Solo un viaje Programado puede cancelarse.");
            var embarque = await ObtenerEmbarqueActivoVinculadoAsync(cn, tx, model.ViajeID, cancellationToken);
            if (embarque.HasValue) throw new InvalidOperationException($"El viaje está vinculado al embarque {embarque.Value.Folio}. Cancela o modifica el embarque desde Centro Operativo para mantener ambos procesos sincronizados.");
            const string sql = @"
UPDATE dbo.Logistica_Viajes
SET Estatus=N'Cancelado',MotivoCancelacion=@Motivo,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
WHERE ViajeID=@ViajeID AND Activo=1 AND Estatus=N'Programado';

UPDATE dbo.Logistica_ViajeParadas
SET Estatus=N'Cancelada',FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
WHERE ViajeID=@ViajeID AND Activo=1 AND Estatus IN(N'Pendiente',N'En camino',N'En sitio');

UPDATE ve
SET Activo=0
FROM dbo.Logistica_ViajeEmbarques ve
INNER JOIN dbo.Logistica_Embarques e ON e.EmbarqueID=ve.EmbarqueID
WHERE ve.ViajeID=@ViajeID AND ve.Activo=1 AND e.Estatus=N'Cancelado';

SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.Logistica_Viajes WHERE ViajeID=@ViajeID AND Estatus=N'Cancelado') THEN 1 ELSE 0 END;";
            await using (var cmd = new SqlCommand(sql, cn, tx))
            {
                cmd.Parameters.Add("@Motivo", SqlDbType.NVarChar, 1000).Value = model.Motivo;
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = model.ViajeID;
                if (Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) != 1) throw new InvalidOperationException("El viaje cambió mientras se intentaba cancelar.");
            }
            await InsertarHistorialAsync(cn, tx, model.ViajeID, "VIAJE_CANCELADO", "Programado", "Cancelado", $"Motivo: {model.Motivo}", cancellationToken);
            await tx.CommitAsync(cancellationToken);
            TempData["LogisticaOk"] = "Viaje cancelado correctamente.";
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(cancellationToken); } catch { }
            TempData["LogisticaError"] = ex.Message;
        }
        return RedirectToAction(nameof(Detalle), new { id = model.ViajeID });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegistrarIncidencia(LogisticaViajeIncidenciaCrearVm model, CancellationToken cancellationToken)
    {
        var acceso = await ValidarAccesoAsync();
        if (acceso != null) return acceso;
        model.Tipo = model.Tipo?.Trim() ?? string.Empty;
        model.Severidad = model.Severidad?.Trim() ?? string.Empty;
        model.Descripcion = model.Descripcion?.Trim() ?? string.Empty;
        model.Responsable = model.Responsable?.Trim();
        var tiposPermitidos = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Unidad", "Operador", "Tráfico", "Retraso", "Ruta", "Material", "Recolección", "Cliente / destino", "Seguridad", "Otro" };
        var severidadesPermitidas = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Baja", "Media", "Alta", "Crítica" };
        if (model.ViajeID <= 0) ModelState.AddModelError(nameof(model.ViajeID), "El viaje no es válido.");
        if (!tiposPermitidos.Contains(model.Tipo)) ModelState.AddModelError(nameof(model.Tipo), "Selecciona un tipo de incidencia válido.");
        if (!severidadesPermitidas.Contains(model.Severidad)) ModelState.AddModelError(nameof(model.Severidad), "Selecciona una severidad válida.");
        if (string.IsNullOrWhiteSpace(model.Descripcion)) ModelState.AddModelError(nameof(model.Descripcion), "La descripción es obligatoria.");
        else if (model.Descripcion.Length > 1200) ModelState.AddModelError(nameof(model.Descripcion), "La descripción no puede exceder 1,200 caracteres.");
        if (!ModelState.IsValid)
        {
            TempData["LogisticaError"] = ObtenerErroresModelState();
            return model.ViajeID > 0 ? RedirectToAction(nameof(Detalle), new { id = model.ViajeID }) : RedirectToAction(nameof(Index));
        }
        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var viaje = await ObtenerViajeParaActualizarAsync(cn, tx, model.ViajeID, cancellationToken) ?? throw new InvalidOperationException("El viaje no existe.");
            if (viaje.Estatus is "Cancelado" or "Completado") throw new InvalidOperationException("Ya no pueden registrarse incidencias operativas en un viaje cancelado o completado.");
            const string sqlDuplicado = @"SELECT COUNT_BIG(*) FROM dbo.Logistica_ViajeIncidencias WITH(UPDLOCK,HOLDLOCK) WHERE ViajeID=@ViajeID AND Activo=1 AND Estatus IN(N'Abierta',N'En seguimiento') AND Tipo=@Tipo AND Descripcion=@Descripcion;";
            await using (var cmd = new SqlCommand(sqlDuplicado, cn, tx))
            {
                cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = model.ViajeID;
                cmd.Parameters.Add("@Tipo", SqlDbType.NVarChar, 80).Value = model.Tipo;
                cmd.Parameters.Add("@Descripcion", SqlDbType.NVarChar, 1200).Value = model.Descripcion;
                if (Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken)) > 0) throw new InvalidOperationException("Ya existe una incidencia abierta con el mismo tipo y descripción.");
            }
            const string sql = @"
INSERT dbo.Logistica_ViajeIncidencias(ViajeID,Tipo,Severidad,Descripcion,Responsable,Estatus,FechaRegistro,UsuarioRegistroID,UsuarioRegistro,Activo)
VALUES(@ViajeID,@Tipo,@Severidad,@Descripcion,@Responsable,N'Abierta',SYSDATETIME(),@UsuarioID,@UsuarioNombre,1);
SELECT CONVERT(int,SCOPE_IDENTITY());";
            int incidenciaId;
            await using (var cmd = new SqlCommand(sql, cn, tx))
            {
                cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = model.ViajeID;
                cmd.Parameters.Add("@Tipo", SqlDbType.NVarChar, 80).Value = model.Tipo;
                cmd.Parameters.Add("@Severidad", SqlDbType.NVarChar, 20).Value = model.Severidad;
                cmd.Parameters.Add("@Descripcion", SqlDbType.NVarChar, 1200).Value = model.Descripcion;
                cmd.Parameters.Add("@Responsable", SqlDbType.NVarChar, 200).Value = Db(model.Responsable);
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = Db(UsuarioID);
                cmd.Parameters.Add("@UsuarioNombre", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                incidenciaId = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
            }
            await EjecutarAsync(cn, tx, @"UPDATE dbo.Logistica_Viajes SET TieneIncidencia=1,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario WHERE ViajeID=@ViajeID AND Activo=1;", cancellationToken, ("@Usuario", UsuarioNombre), ("@ViajeID", model.ViajeID));
            await InsertarHistorialAsync(cn, tx, model.ViajeID, "INCIDENCIA_REGISTRADA", viaje.Estatus, viaje.Estatus, $"Incidencia VINC-{incidenciaId:000000}. {model.Tipo} / {model.Severidad}. {model.Descripcion}", cancellationToken);
            await tx.CommitAsync(cancellationToken);
            TempData["LogisticaOk"] = $"Incidencia VINC-{incidenciaId:000000} registrada.";
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(cancellationToken); } catch { }
            TempData["LogisticaError"] = ex.Message;
        }
        return RedirectToAction(nameof(Detalle), new { id = model.ViajeID });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CerrarIncidencia(LogisticaViajeIncidenciaCerrarVm model, CancellationToken cancellationToken)
    {
        var acceso = await ValidarAccesoAsync();
        if (acceso != null) return acceso;
        model.Solucion = model.Solucion?.Trim() ?? string.Empty;
        if (model.ViajeID <= 0 || model.ViajeIncidenciaID <= 0 || string.IsNullOrWhiteSpace(model.Solucion))
        {
            TempData["LogisticaError"] = "Incidencia, viaje y solución son obligatorios.";
            return model.ViajeID > 0 ? RedirectToAction(nameof(Detalle), new { id = model.ViajeID }) : RedirectToAction(nameof(Index));
        }
        if (model.Solucion.Length > 1200)
        {
            TempData["LogisticaError"] = "La solución no puede exceder 1,200 caracteres.";
            return RedirectToAction(nameof(Detalle), new { id = model.ViajeID });
        }
        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var viaje = await ObtenerViajeParaActualizarAsync(cn, tx, model.ViajeID, cancellationToken) ?? throw new InvalidOperationException("El viaje no existe.");
            const string sqlCerrar = @"
UPDATE dbo.Logistica_Viaje no existe.");
            const string sqlCerrar = @"
UPDATE dbo.Logistica_ViajeIncidencias
SET Estatus=N'Cerrada',Solucion=@Solucion,FechaCierre=SYSDATETIME(),UsuarioCierreID=@UsuarioID,UsuarioCierre=@UsuarioNombre
WHERE ViajeIncidenciaID=@IncidenciaID AND ViajeID=@ViajeID AND Activo=1 AND Estatus IN(N'Abierta',N'En seguimiento');
SELECT @@ROWCOUNT;";
            await using (var cmd = new SqlCommand(sqlCerrar, cn, tx))
            {
                cmd.Parameters.Add("@Solucion", SqlDbType.NVarChar, 1200).Value = model.Solucion;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = Db(UsuarioID);
                cmd.Parameters.Add("@UsuarioNombre", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@IncidenciaID", SqlDbType.Int).Value = model.ViajeIncidenciaID;
                cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = model.ViajeID;
                if (Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) == 0) throw new InvalidOperationException("La incidencia no existe, ya fue cerrada o cambió durante la operación.");
            }
            const string sqlPendientes = @"SELECT COUNT_BIG(*) FROM dbo.Logistica_ViajeIncidencias WHERE ViajeID=@ViajeID AND Activo=1 AND Estatus IN(N'Abierta',N'En seguimiento');";
            long pendientes;
            await using (var cmd = new SqlCommand(sqlPendientes, cn, tx))
            {
                cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = model.ViajeID;
                pendientes = Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken));
            }
            await EjecutarAsync(cn, tx, @"UPDATE dbo.Logistica_Viajes SET TieneIncidencia=@TieneIncidencia,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario WHERE ViajeID=@ViajeID AND Activo=1;", cancellationToken, ("@TieneIncidencia", pendientes > 0), ("@Usuario", UsuarioNombre), ("@ViajeID", model.ViajeID));
            await InsertarHistorialAsync(cn, tx, model.ViajeID, "INCIDENCIA_CERRADA", viaje.Estatus, viaje.Estatus, $"Incidencia VINC-{model.ViajeIncidenciaID:000000} cerrada. Solución: {model.Solucion}", cancellationToken);
            await tx.CommitAsync(cancellationToken);
            TempData["LogisticaOk"] = pendientes > 0 ? $"Incidencia cerrada. Quedan {pendientes:N0} incidencia(s) abiertas." : "Incidencia cerrada. El viaje ya no tiene incidencias abiertas.";
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(cancellationToken); } catch { }
            TempData["LogisticaError"] = ex.Message;
        }
        return RedirectToAction(nameof(Detalle), new { id = model.ViajeID });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(115_343_360)]
    public async Task<IActionResult> SubirEvidencia(int viajeId, string? tipoEvidencia, IFormFile? archivo, List<IFormFile>? archivos, string? observaciones, CancellationToken cancellationToken)
    {
        var acceso = await ValidarAccesoAsync();
        if (acceso != null) return acceso;
        if (viajeId <= 0)
        {
            TempData["LogisticaError"] = "El viaje indicado no es válido.";
            return RedirectToAction(nameof(Index));
        }
        tipoEvidencia = tipoEvidencia?.Trim() ?? "General";
        observaciones = observaciones?.Trim();
        var tiposPermitidos = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Salida", "Unidad", "Ruta", "Gasolina", "Regreso", "Incidencia", "General" };
        if (!tiposPermitidos.Contains(tipoEvidencia))
        {
            TempData["LogisticaError"] = "Selecciona un tipo de evidencia válido.";
            return RedirectToAction(nameof(Detalle), new { id = viajeId });
        }
        var evidencias = (archivos ?? new List<IFormFile>()).Where(x => x != null && x.Length > 0).ToList();
        if (archivo != null && archivo.Length > 0 && !evidencias.Any(x => x.FileName == archivo.FileName && x.Length == archivo.Length)) evidencias.Add(archivo);
        if (evidencias.Count == 0)
        {
            TempData["LogisticaError"] = "Selecciona al menos una fotografía o archivo.";
            return RedirectToAction(nameof(Detalle), new { id = viajeId });
        }
        if (evidencias.Count > 15)
        {
            TempData["LogisticaError"] = "Puedes subir como máximo 15 evidencias por operación.";
            return RedirectToAction(nameof(Detalle), new { id = viajeId });
        }
        if (evidencias.Sum(x => x.Length) > 100L * 1024L * 1024L)
        {
            TempData["LogisticaError"] = "El tamaño total de las evidencias no puede exceder 100 MB.";
            return RedirectToAction(nameof(Detalle), new { id = viajeId });
        }
        var permitidas = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp", ".heic", ".heif", ".pdf" };
        foreach (var evidencia in evidencias)
        {
            if (evidencia.Length > 10L * 1024L * 1024L)
            {
                TempData["LogisticaError"] = $"{Path.GetFileName(evidencia.FileName)} excede el máximo de 10 MB.";
                return RedirectToAction(nameof(Detalle), new { id = viajeId });
            }
            var extension = Path.GetExtension(evidencia.FileName).ToLowerInvariant();
            if (!permitidas.Contains(extension))
            {
                TempData["LogisticaError"] = $"{Path.GetFileName(evidencia.FileName)} no es válido. Se permiten JPG, JPEG, PNG, WEBP, HEIC, HEIF o PDF.";
                return RedirectToAction(nameof(Detalle), new { id = viajeId });
            }
        }
        var rutasFisicas = new List<string>();
        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var viaje = await ObtenerViajeParaActualizarAsync(cn, tx, viajeId, cancellationToken) ?? throw new InvalidOperationException("El viaje no existe.");
            if (viaje.Estatus == "Cancelado") throw new InvalidOperationException("No se pueden agregar evidencias a un viaje cancelado.");
            var carpetaRelativa = Path.Combine("Logistica", "Viajes", viajeId.ToString(), "Evidencias");
            var carpetaFisica = Path.Combine(_environment.ContentRootPath, "App_Data", carpetaRelativa);
            Directory.CreateDirectory(carpetaFisica);
            const string sql = @"
INSERT dbo.Logistica_ViajeEvidencias
(ViajeID,TipoEvidencia,NombreOriginal,NombreFisico,RutaRelativa,TipoContenido,TamanoBytes,Observaciones,UsuarioCargaID,UsuarioCargaNombre,FechaCarga,Activo,FechaCreacion,CreadoPor)
VALUES
(@ViajeID,@TipoEvidencia,@NombreOriginal,@NombreFisico,@RutaRelativa,@TipoContenido,@TamanoBytes,@Observaciones,@UsuarioID,@UsuarioNombre,SYSDATETIME(),1,SYSDATETIME(),@UsuarioNombre);
SELECT CONVERT(int,SCOPE_IDENTITY());";
            var ids = new List<int>();
            foreach (var evidencia in evidencias)
            {
                var nombreOriginal = Path.GetFileName(evidencia.FileName);
                var extension = Path.GetExtension(nombreOriginal).ToLowerInvariant();
                var nombreFisico = $"{Guid.NewGuid():N}{extension}";
                var rutaFisica = Path.Combine(carpetaFisica, nombreFisico);
                await using (var stream = new FileStream(rutaFisica, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                    await evidencia.CopyToAsync(stream, cancellationToken);
                rutasFisicas.Add(rutaFisica);
                var rutaRelativa = Path.Combine("App_Data", carpetaRelativa, nombreFisico).Replace('\\', '/');
                await using var cmd = new SqlCommand(sql, cn, tx);
                cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
                cmd.Parameters.Add("@TipoEvidencia", SqlDbType.NVarChar, 50).Value = tipoEvidencia;
                cmd.Parameters.Add("@NombreOriginal", SqlDbType.NVarChar, 260).Value = nombreOriginal;
                cmd.Parameters.Add("@NombreFisico", SqlDbType.NVarChar, 260).Value = nombreFisico;
                cmd.Parameters.Add("@RutaRelativa", SqlDbType.NVarChar, 600).Value = rutaRelativa;
                cmd.Parameters.Add("@TipoContenido", SqlDbType.NVarChar, 150).Value = evidencia.ContentType ?? "application/octet-stream";
                cmd.Parameters.Add("@TamanoBytes", SqlDbType.BigInt).Value = evidencia.Length;
                cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1000).Value = Db(observaciones);
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = Db(UsuarioID);
                cmd.Parameters.Add("@UsuarioNombre", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                ids.Add(Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)));
            }
            await InsertarHistorialAsync(cn, tx, viajeId, "EVIDENCIAS_AGREGADAS", viaje.Estatus, viaje.Estatus, $"{ids.Count:N0} evidencia(s) agregadas al viaje. Tipo: {tipoEvidencia}.", cancellationToken);
            await tx.CommitAsync(cancellationToken);
            TempData["LogisticaOk"] = $"{ids.Count:N0} evidencia(s) cargadas correctamente.";
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(cancellationToken); } catch { }
            foreach (var ruta in rutasFisicas)
            {
                if (!System.IO.File.Exists(ruta)) continue;
                try { System.IO.File.Delete(ruta); } catch { }
            }
            TempData["LogisticaError"] = ex.Message;
        }
        return RedirectToAction(nameof(Detalle), new { id = viajeId });
    }

    [HttpGet]
    public async Task<IActionResult> VerEvidencia(int id, CancellationToken cancellationToken)
    {
        var acceso = await ValidarAccesoAsync();
        if (acceso != null) return acceso;
        if (id <= 0) return NotFound();
        await using var cn = await AbrirAsync(cancellationToken);
        const string sql = @"SELECT TOP(1) NombreOriginal,RutaRelativa,TipoContenido FROM dbo.Logistica_ViajeEvidencias WHERE ViajeEvidenciaID=@Id AND Activo=1;";
        string nombreOriginal, rutaRelativa, tipoContenido;
        await using (var cmd = new SqlCommand(sql, cn))
        {
            cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await rd.ReadAsync(cancellationToken)) return NotFound();
            nombreOriginal = Texto(rd, "NombreOriginal");
            rutaRelativa = Texto(rd, "RutaRelativa");
            tipoContenido = Texto(rd, "TipoContenido");
        }
        string rutaFisica;
        try { rutaFisica = ResolverRutaFisicaEvidenciaViaje(rutaRelativa); }
        catch { return NotFound(); }
        if (!System.IO.File.Exists(rutaFisica)) return NotFound();
        return PhysicalFile(rutaFisica, string.IsNullOrWhiteSpace(tipoContenido) ? "application/octet-stream" : tipoContenido, string.IsNullOrWhiteSpace(nombreOriginal) ? Path.GetFileName(rutaFisica) : nombreOriginal);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EliminarEvidencia(int viajeId, int evidenciaId, CancellationToken cancellationToken)
    {
        var acceso = await ValidarAccesoAsync();
        if (acceso != null) return acceso;
        if (viajeId <= 0 || evidenciaId <= 0)
        {
            TempData["LogisticaError"] = "La evidencia indicada no es válida.";
            return viajeId > 0 ? RedirectToAction(nameof(Detalle), new { id = viajeId }) : RedirectToAction(nameof(Index));
        }
        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var viaje = await ObtenerViajeParaActualizarAsync(cn, tx, viajeId, cancellationToken) ?? throw new InvalidOperationException("El viaje no existe.");
            const string sqlActual = @"
SELECT ISNULL(e.TipoEvidencia,N'') TipoEvidencia,v.FechaSalidaReal,v.FechaRegresoReal
FROM dbo.Logistica_ViajeEvidencias e WITH(UPDLOCK,HOLDLOCK)
INNER JOIN dbo.Logistica_Viajes v WITH(UPDLOCK,HOLDLOCK) ON v.ViajeID=e.ViajeID
WHERE e.ViajeEvidenciaID=@EvidenciaID AND e.ViajeID=@ViajeID AND e.Activo=1 AND v.Activo=1;";
            string tipoEvidencia;
            DateTime? fechaSalida, fechaRegreso;
            await using (var cmd = new SqlCommand(sqlActual, cn, tx))
            {
                cmd.Parameters.Add("@EvidenciaID", SqlDbType.Int).Value = evidenciaId;
                cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
                await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
                if (!await rd.ReadAsync(cancellationToken)) throw new InvalidOperationException("La evidencia no existe o ya fue retirada.");
                tipoEvidencia = Texto(rd, "TipoEvidencia");
                fechaSalida = Fecha(rd, "FechaSalidaReal");
                fechaRegreso = Fecha(rd, "FechaRegresoReal");
            }
            if (tipoEvidencia.Equals("Salida", StringComparison.OrdinalIgnoreCase) && fechaSalida.HasValue)
                throw new InvalidOperationException("Esta evidencia pertenece a una salida ya confirmada y debe conservarse como respaldo histórico.");
            if (tipoEvidencia.Equals("Regreso", StringComparison.OrdinalIgnoreCase) && fechaRegreso.HasValue)
                throw new InvalidOperationException("Esta evidencia pertenece a un regreso ya confirmado y debe conservarse como respaldo histórico.");
            const string sql = @"UPDATE dbo.Logistica_ViajeEvidencias SET Activo=0,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario WHERE ViajeEvidenciaID=@EvidenciaID AND ViajeID=@ViajeID AND Activo=1; SELECT @@ROWCOUNT;";
            await using (var cmd = new SqlCommand(sql, cn, tx))
            {
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@EvidenciaID", SqlDbType.Int).Value = evidenciaId;
                cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
                if (Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) == 0) throw new InvalidOperationException("La evidencia no existe o ya fue retirada.");
            }
            await InsertarHistorialAsync(cn, tx, viajeId, "EVIDENCIA_RETIRADA", viaje.Estatus, viaje.Estatus, $"Se retiró la evidencia VEVI-{evidenciaId:000000}. Tipo: {tipoEvidencia}.", cancellationToken);
            await tx.CommitAsync(cancellationToken);
            TempData["LogisticaOk"] = "Evidencia retirada.";
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(cancellationToken); } catch { }
            TempData["LogisticaError"] = ex.Message;
        }
        return RedirectToAction(nameof(Detalle), new { id = viajeId });
    }

    private string ResolverRutaFisicaEvidenciaViaje(string rutaRelativa)
    {
        var ruta = (rutaRelativa ?? string.Empty).Replace('\\', '/').Trim();
        if (string.IsNullOrWhiteSpace(ruta)) throw new InvalidOperationException("La evidencia no tiene una ruta física válida.");
        string raiz;
        string relativa;
        if (ruta.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase) || ruta.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase))
        {
            raiz = !string.IsNullOrWhiteSpace(_environment.WebRootPath) ? _environment.WebRootPath : Path.Combine(_environment.ContentRootPath, "wwwroot");
            relativa = ruta.TrimStart('/');
        }
        else
        {
            raiz = _environment.ContentRootPath;
            relativa = ruta.TrimStart('/');
        }
        var raizCompleta = Path.GetFullPath(raiz).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var rutaCompleta = Path.GetFullPath(Path.Combine(raiz, relativa.Replace('/', Path.DirectorySeparatorChar)));
        if (!rutaCompleta.StartsWith(raizCompleta, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("La ruta de evidencia está fuera del directorio permitido.");
        return rutaCompleta;
    }
    private async Task<LogisticaViajeDetalleVm?> CargarDetalleAsync(SqlConnection cn, int viajeId, CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT v.ViajeID,ISNULL(v.Folio,N'') Folio,ISNULL(v.TipoViaje,N'') TipoViaje,ISNULL(v.Origen,N'') Origen,ISNULL(v.Destino,N'') Destino,ISNULL(v.Motivo,N'') Motivo,
v.FechaProgramada,v.HoraSalidaProgramada,v.FechaSalidaReal,v.FechaRegresoReal,v.RutaID,ISNULL(r.Codigo+N' - '+r.Nombre,N'') Ruta,v.UnidadID,
ISNULL(u.NumeroEconomico+CASE WHEN NULLIF(LTRIM(RTRIM(ISNULL(u.Placas,N''))),N'') IS NULL THEN N'' ELSE N' - '+LTRIM(RTRIM(u.Placas)) END,N'') Unidad,
v.OperadorUsuarioID,ISNULL(NULLIF(LTRIM(RTRIM(v.OperadorNombreSnapshot)),N''),ISNULL(v.OperadorTexto,N'')) Operador,ISNULL(v.Estatus,N'') Estatus,
ISNULL(v.Observaciones,N'') Observaciones,ISNULL(v.TieneIncidencia,0) TieneIncidencia,ISNULL(v.EsMultiParada,0) EsMultiParada,
v.KilometrajeSalida,v.KilometrajeRegreso,v.PagoGasolina,v.ResponsableUsuarioID,ISNULL(v.ResponsableNombreSnapshot,N'') UsuarioResponsable,v.FechaCreacion,ISNULL(v.CreadoPor,N'') CreadoPor
FROM dbo.Logistica_Viajes v
LEFT JOIN dbo.Logistica_Rutas r ON r.RutaID=v.RutaID
LEFT JOIN dbo.Logistica_Unidades u ON u.UnidadID=v.UnidadID
WHERE v.ViajeID=@ViajeID AND v.Activo=1;";
        LogisticaViajeDetalleVm? vm = null;
        await using (var cmd = new SqlCommand(sql, cn))
        {
            cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await rd.ReadAsync(cancellationToken)) return null;
            vm = new LogisticaViajeDetalleVm
            {
                ViajeID = Entero(rd, "ViajeID"),
                Folio = Texto(rd, "Folio"),
                TipoViaje = Texto(rd, "TipoViaje"),
                Origen = Texto(rd, "Origen"),
                Destino = Texto(rd, "Destino"),
                Motivo = Texto(rd, "Motivo"),
                FechaProgramada = Fecha(rd, "FechaProgramada") ?? DateTime.MinValue,
                HoraSalidaProgramada = Hora(rd, "HoraSalidaProgramada"),
                FechaSalidaReal = Fecha(rd, "FechaSalidaReal"),
                FechaRegresoReal = Fecha(rd, "FechaRegresoReal"),
                RutaID = EnteroNullable(rd, "RutaID"),
                Ruta = Texto(rd, "Ruta"),
                UnidadID = EnteroNullable(rd, "UnidadID"),
                Unidad = Texto(rd, "Unidad"),
                OperadorUsuarioID = EnteroNullable(rd, "OperadorUsuarioID"),
                Operador = Texto(rd, "Operador"),
                Estatus = Texto(rd, "Estatus"),
                Observaciones = Texto(rd, "Observaciones"),
                TieneIncidencia = Booleano(rd, "TieneIncidencia"),
                EsMultiParada = Booleano(rd, "EsMultiParada"),
                KilometrajeSalida = EnteroNullable(rd, "KilometrajeSalida"),
                KilometrajeRegreso = EnteroNullable(rd, "KilometrajeRegreso"),
                PagoGasolina = DecimalNullable(rd, "PagoGasolina"),
                UsuarioResponsableID = EnteroNullable(rd, "ResponsableUsuarioID"),
                UsuarioResponsable = Texto(rd, "UsuarioResponsable"),
                FechaCreacion = Fecha(rd, "FechaCreacion") ?? DateTime.MinValue,
                CreadoPor = Texto(rd, "CreadoPor")
            };
        }
        await CargarParadasViajeAsync(cn, vm, cancellationToken);
        const string sqlHistorial = @"SELECT HistorialID,ViajeID,Evento,ISNULL(EstadoAnterior,N'') EstadoAnterior,ISNULL(EstadoNuevo,N'') EstadoNuevo,ISNULL(Observaciones,N'') Observaciones,UsuarioID,ISNULL(UsuarioNombre,N'') Usuario,FechaEvento FROM dbo.Logistica_ViajeHistorial WHERE ViajeID=@ViajeID ORDER BY FechaEvento DESC,HistorialID DESC;";
        await using (var cmd = new SqlCommand(sqlHistorial, cn))
        {
            cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await rd.ReadAsync(cancellationToken))
            {
                vm.Historial.Add(new LogisticaViajeHistorialVm
                {
                    HistorialID = Entero(rd, "HistorialID"),
                    ViajeID = Entero(rd, "ViajeID"),
                    Evento = Texto(rd, "Evento"),
                    EstadoAnterior = Texto(rd, "EstadoAnterior"),
                    EstadoNuevo = Texto(rd, "EstadoNuevo"),
                    Observaciones = Texto(rd, "Observaciones"),
                    UsuarioID = EnteroNullable(rd, "UsuarioID"),
                    Usuario = Texto(rd, "Usuario"),
                    FechaEvento = Fecha(rd, "FechaEvento") ?? DateTime.MinValue
                });
            }
        }
        const string sqlIncidencias = @"SELECT ViajeIncidenciaID,ViajeID,Tipo,Severidad,Descripcion,Estatus,ISNULL(Responsable,N'') Responsable,FechaRegistro,FechaCierre FROM dbo.Logistica_ViajeIncidencias WHERE ViajeID=@ViajeID AND Activo=1 ORDER BY CASE WHEN Estatus IN(N'Abierta',N'En seguimiento') THEN 0 ELSE 1 END,CASE Severidad WHEN N'Crítica' THEN 1 WHEN N'Alta' THEN 2 WHEN N'Media' THEN 3 WHEN N'Baja' THEN 4 ELSE 5 END,FechaRegistro DESC,ViajeIncidenciaID DESC;";
        await using (var cmd = new SqlCommand(sqlIncidencias, cn))
        {
            cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await rd.ReadAsync(cancellationToken))
            {
                vm.Incidencias.Add(new LogisticaViajeIncidenciaVm
                {
                    ViajeIncidenciaID = Entero(rd, "ViajeIncidenciaID"),
                    ViajeID = Entero(rd, "ViajeID"),
                    Tipo = Texto(rd, "Tipo"),
                    Severidad = Texto(rd, "Severidad"),
                    Descripcion = Texto(rd, "Descripcion"),
                    Estatus = Texto(rd, "Estatus"),
                    Responsable = Texto(rd, "Responsable"),
                    FechaRegistro = Fecha(rd, "FechaRegistro") ?? DateTime.MinValue,
                    FechaCierre = Fecha(rd, "FechaCierre")
                });
            }
        }
        await CargarEvidenciasViajeAsync(cn, vm, cancellationToken);
        vm.TieneIncidencia = vm.Incidencias.Any(x => x.EstaAbierta);
        return vm;
    }

    private async Task AsegurarParadasBaseViajeAsync(SqlConnection cn, SqlTransaction tx, int viajeId, string origen, string destinoRetorno, DateTime fechaProgramada, TimeSpan? horaSalidaProgramada, CancellationToken cancellationToken)
    {
        origen = origen?.Trim() ?? string.Empty;
        destinoRetorno = destinoRetorno?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(origen)) throw new InvalidOperationException("El viaje necesita un origen para construir su recorrido.");
        if (string.IsNullOrWhiteSpace(destinoRetorno)) throw new InvalidOperationException("El viaje necesita un destino final o retorno.");
        var fechaHoraSalida = horaSalidaProgramada.HasValue ? fechaProgramada.Date.Add(horaSalidaProgramada.Value) : (DateTime?)null;
        const string sqlEstado = @"
SELECT COUNT_BIG(*) Total,MIN(ViajeParadaID) ViajeParadaID FROM dbo.Logistica_ViajeParadas WITH(UPDLOCK,HOLDLOCK) WHERE ViajeID=@ViajeID AND Activo=1 AND TipoParada=N'Origen';
SELECT COUNT_BIG(*) Total,MIN(ViajeParadaID) ViajeParadaID FROM dbo.Logistica_ViajeParadas WITH(UPDLOCK,HOLDLOCK) WHERE ViajeID=@ViajeID AND Activo=1 AND (TipoParada=N'Retorno' OR ISNULL(CierraViaje,0)=1);";
        long totalOrigen, totalRetorno;
        int? origenId, retornoId;
        await using (var cmd = new SqlCommand(sqlEstado, cn, tx))
        {
            cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            await rd.ReadAsync(cancellationToken);
            totalOrigen = Convert.ToInt64(rd["Total"]);
            origenId = rd.IsDBNull(rd.GetOrdinal("ViajeParadaID")) ? null : Convert.ToInt32(rd["ViajeParadaID"]);
            totalRetorno = 0;
            retornoId = null;
            if (await rd.NextResultAsync(cancellationToken) && await rd.ReadAsync(cancellationToken))
            {
                totalRetorno = Convert.ToInt64(rd["Total"]);
                retornoId = rd.IsDBNull(rd.GetOrdinal("ViajeParadaID")) ? null : Convert.ToInt32(rd["ViajeParadaID"]);
            }
        }
        if (totalOrigen > 1) throw new InvalidOperationException("El viaje tiene más de una parada Origen activa.");
        if (totalRetorno > 1) throw new InvalidOperationException("El viaje tiene más de una parada final de Retorno activa.");
        if (!origenId.HasValue)
        {
            const string sqlMover = @"UPDATE dbo.Logistica_ViajeParadas SET Secuencia=Secuencia+10000 WHERE ViajeID=@ViajeID AND Activo=1;";
            await using (var cmd = new SqlCommand(sqlMover, cn, tx))
            {
                cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            const string sqlOrigen = @"
INSERT dbo.Logistica_ViajeParadas
(ViajeID,Secuencia,TipoParada,TipoOperacion,EntidadTipo,EntidadNombreSnapshot,Lugar,FechaHoraSalidaProgramada,Estatus,RequiereEvidencia,CierraViaje,Observaciones,Activo,FechaCreacion,CreadoPor)
VALUES
(@ViajeID,1,N'Origen',N'Salida',N'Planta',@Origen,@Origen,@FechaSalida,N'Pendiente',0,0,N'Inicio del recorrido.',1,SYSDATETIME(),@Usuario);
SELECT CONVERT(int,SCOPE_IDENTITY());";
            await using var cmdOrigen = new SqlCommand(sqlOrigen, cn, tx);
            cmdOrigen.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
            cmdOrigen.Parameters.Add("@Origen", SqlDbType.NVarChar, 300).Value = origen;
            cmdOrigen.Parameters.Add("@FechaSalida", SqlDbType.DateTime2).Value = Db(fechaHoraSalida);
            cmdOrigen.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
            origenId = Convert.ToInt32(await cmdOrigen.ExecuteScalarAsync(cancellationToken));
        }
        else
        {
            const string sqlOrigen = @"UPDATE dbo.Logistica_ViajeParadas SET TipoParada=N'Origen',TipoOperacion=N'Salida',EntidadTipo=N'Planta',EntidadNombreSnapshot=@Origen,Lugar=@Origen,FechaHoraSalidaProgramada=@FechaSalida,CierraViaje=0,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario WHERE ViajeParadaID=@ParadaID AND ViajeID=@ViajeID AND Activo=1;";
            await using var cmd = new SqlCommand(sqlOrigen, cn, tx);
            cmd.Parameters.Add("@Origen", SqlDbType.NVarChar, 300).Value = origen;
            cmd.Parameters.Add("@FechaSalida", SqlDbType.DateTime2).Value = Db(fechaHoraSalida);
            cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
            cmd.Parameters.Add("@ParadaID", SqlDbType.Int).Value = origenId.Value;
            cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        if (!retornoId.HasValue)
        {
            const string sqlRetorno = @"
DECLARE @Secuencia int=(SELECT ISNULL(MAX(Secuencia),0)+1 FROM dbo.Logistica_ViajeParadas WITH(UPDLOCK,HOLDLOCK) WHERE ViajeID=@ViajeID AND Activo=1);
INSERT dbo.Logistica_ViajeParadas
(ViajeID,Secuencia,TipoParada,TipoOperacion,EntidadTipo,EntidadNombreSnapshot,Lugar,Estatus,RequiereEvidencia,CierraViaje,Observaciones,Activo,FechaCreacion,CreadoPor)
VALUES
(@ViajeID,@Secuencia,N'Retorno',N'Retorno',N'Planta',@Destino,@Destino,N'Pendiente',0,1,N'Cierre del recorrido.',1,SYSDATETIME(),@Usuario);
SELECT CONVERT(int,SCOPE_IDENTITY());";
            await using var cmdRetorno = new SqlCommand(sqlRetorno, cn, tx);
            cmdRetorno.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
            cmdRetorno.Parameters.Add("@Destino", SqlDbType.NVarChar, 300).Value = destinoRetorno;
            cmdRetorno.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
            retornoId = Convert.ToInt32(await cmdRetorno.ExecuteScalarAsync(cancellationToken));
        }
        else
        {
            const string sqlRetorno = @"UPDATE dbo.Logistica_ViajeParadas SET TipoParada=N'Retorno',TipoOperacion=N'Retorno',EntidadTipo=N'Planta',EntidadNombreSnapshot=@Destino,Lugar=@Destino,CierraViaje=1,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario WHERE ViajeParadaID=@ParadaID AND ViajeID=@ViajeID AND Activo=1;";
            await using var cmd = new SqlCommand(sqlRetorno, cn, tx);
            cmd.Parameters.Add("@Destino", SqlDbType.NVarChar, 300).Value = destinoRetorno;
            cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
            cmd.Parameters.Add("@ParadaID", SqlDbType.Int).Value = retornoId.Value;
            cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        const string sqlExtremos = @"
DECLARE @Offset int=(SELECT ISNULL(MAX(Secuencia),0)+10000 FROM dbo.Logistica_ViajeParadas WITH(UPDLOCK,HOLDLOCK) WHERE ViajeID=@ViajeID AND Activo=1);
UPDATE dbo.Logistica_ViajeParadas SET Secuencia=Secuencia+@Offset WHERE ViajeID=@ViajeID AND Activo=1 AND ViajeParadaID<>@OrigenID;
UPDATE dbo.Logistica_ViajeParadas SET Secuencia=1 WHERE ViajeParadaID=@OrigenID AND ViajeID=@ViajeID AND Activo=1;
DECLARE @Ultima int=(SELECT ISNULL(MAX(Secuencia),1)+@Offset FROM dbo.Logistica_ViajeParadas WHERE ViajeID=@ViajeID AND Activo=1 AND ViajeParadaID<>@RetornoID);
UPDATE dbo.Logistica_ViajeParadas SET Secuencia=@Ultima WHERE ViajeParadaID=@RetornoID AND ViajeID=@ViajeID AND Activo=1;";
        await using (var cmd = new SqlCommand(sqlExtremos, cn, tx))
        {
            cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
            cmd.Parameters.Add("@OrigenID", SqlDbType.Int).Value = origenId.Value;
            cmd.Parameters.Add("@RetornoID", SqlDbType.Int).Value = retornoId.Value;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        await ReordenarSecuenciasParadasAsync(cn, tx, viajeId, cancellationToken);
    }

    private async Task CargarParadasViajeAsync(SqlConnection cn, LogisticaViajeDetalleVm vm, CancellationToken cancellationToken)
    {
        vm.Paradas.Clear();
        const string sqlParadas = @"
SELECT p.ViajeParadaID,p.ViajeID,p.Secuencia,ISNULL(p.TipoParada,N'') TipoParada,ISNULL(p.TipoOperacion,N'') TipoOperacion,ISNULL(p.EntidadTipo,N'') EntidadTipo,p.EntidadID,
ISNULL(p.EntidadNombreSnapshot,N'') EntidadNombreSnapshot,ISNULL(p.Lugar,N'') Lugar,ISNULL(p.Direccion,N'') Direccion,ISNULL(p.ReferenciaTipo,N'') ReferenciaTipo,p.ReferenciaID,
ISNULL(p.ReferenciaFolioSnapshot,N'') ReferenciaFolioSnapshot,p.FechaHoraLlegadaProgramada,p.FechaHoraSalidaProgramada,p.FechaLlegadaReal,p.FechaSalidaReal,
ISNULL(p.Estatus,N'Pendiente') Estatus,ISNULL(p.RequiereEvidencia,0) RequiereEvidencia,ISNULL(p.CierraViaje,0) CierraViaje,
ISNULL(p.ContactoNombre,N'') ContactoNombre,ISNULL(p.ContactoTelefono,N'') ContactoTelefono,ISNULL(p.Observaciones,N'') Observaciones,ISNULL(p.Activo,0) Activo,
CONVERT(varbinary(8),p.RowVersion) RowVersion,ISNULL(e.TotalEmbarques,0) TotalEmbarques,ISNULL(e.EmbarquesEntregados,0) EmbarquesEntregados,ISNULL(ev.TotalEvidencias,0) TotalEvidencias
FROM dbo.Logistica_ViajeParadas p
OUTER APPLY
(
    SELECT CONVERT(int,COUNT_BIG(*)) TotalEmbarques,ISNULL(SUM(CASE WHEN em.Estatus=N'Entregado' THEN 1 ELSE 0 END),0) EmbarquesEntregados
    FROM dbo.Logistica_ViajeEmbarques ve
    INNER JOIN dbo.Logistica_Embarques em ON em.EmbarqueID=ve.EmbarqueID AND em.Activo=1
    WHERE ve.ViajeParadaID=p.ViajeParadaID AND ve.Activo=1
) e
OUTER APPLY
(
    SELECT CONVERT(int,COUNT_BIG(*)) TotalEvidencias FROM dbo.Logistica_ViajeParadaEvidencias x WHERE x.ViajeParadaID=p.ViajeParadaID AND x.Activo=1
) ev
WHERE p.ViajeID=@ViajeID AND p.Activo=1
ORDER BY p.Secuencia,p.ViajeParadaID;";
        await using (var cmd = new SqlCommand(sqlParadas, cn))
        {
            cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = vm.ViajeID;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await rd.ReadAsync(cancellationToken))
            {
                vm.Paradas.Add(new LogisticaViajeParadaVm
                {
                    ViajeParadaID = Entero(rd, "ViajeParadaID"),
                    ViajeID = Entero(rd, "ViajeID"),
                    Secuencia = Entero(rd, "Secuencia"),
                    TipoParada = Texto(rd, "TipoParada"),
                    TipoOperacion = Texto(rd, "TipoOperacion"),
                    EntidadTipo = Texto(rd, "EntidadTipo"),
                    EntidadID = EnteroNullable(rd, "EntidadID"),
                    EntidadNombreSnapshot = Texto(rd, "EntidadNombreSnapshot"),
                    Lugar = Texto(rd, "Lugar"),
                    Direccion = Texto(rd, "Direccion"),
                    ReferenciaTipo = Texto(rd, "ReferenciaTipo"),
                    ReferenciaID = EnteroNullable(rd, "ReferenciaID"),
                    ReferenciaFolioSnapshot = Texto(rd, "ReferenciaFolioSnapshot"),
                    FechaHoraLlegadaProgramada = Fecha(rd, "FechaHoraLlegadaProgramada"),
                    FechaHoraSalidaProgramada = Fecha(rd, "FechaHoraSalidaProgramada"),
                    FechaLlegadaReal = Fecha(rd, "FechaLlegadaReal"),
                    FechaSalidaReal = Fecha(rd, "FechaSalidaReal"),
                    Estatus = Texto(rd, "Estatus"),
                    RequiereEvidencia = Booleano(rd, "RequiereEvidencia"),
                    CierraViaje = Booleano(rd, "CierraViaje"),
                    ContactoNombre = Texto(rd, "ContactoNombre"),
                    ContactoTelefono = Texto(rd, "ContactoTelefono"),
                    Observaciones = Texto(rd, "Observaciones"),
                    Activo = Booleano(rd, "Activo"),
                    TotalEmbarques = Entero(rd, "TotalEmbarques"),
                    EmbarquesEntregados = Entero(rd, "EmbarquesEntregados"),
                    TotalEvidencias = Entero(rd, "TotalEvidencias"),
                    RowVersion = Convert.ToBase64String((byte[])rd["RowVersion"])
                });
            }
        }
        var porId = vm.Paradas.ToDictionary(x => x.ViajeParadaID);
        const string sqlEmbarques = @"
SELECT ve.ViajeEmbarqueID,ve.ViajeID,ve.ViajeParadaID,ve.EmbarqueID,ve.OrdenEntrega,ISNULL(e.Folio,N'') Folio,ISNULL(e.ClienteNombreSnapshot,N'') Cliente,
ISNULL(e.Destino,N'') Destino,ISNULL(e.Estatus,N'') Estatus,ISNULL(d.TotalPiezas,0) TotalPiezas
FROM dbo.Logistica_ViajeEmbarques ve
INNER JOIN dbo.Logistica_Embarques e ON e.EmbarqueID=ve.EmbarqueID AND e.Activo=1
OUTER APPLY(SELECT ISNULL(SUM(x.CantidadSolicitada),0) TotalPiezas FROM dbo.Logistica_EmbarqueDetalle x WHERE x.EmbarqueID=e.EmbarqueID AND x.Activo=1)d
WHERE ve.ViajeID=@ViajeID AND ve.Activo=1 AND ve.ViajeParadaID IS NOT NULL
ORDER BY ISNULL(ve.OrdenEntrega,2147483647),ve.ViajeEmbarqueID;";
        await using (var cmd = new SqlCommand(sqlEmbarques, cn))
        {
            cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = vm.ViajeID;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await rd.ReadAsync(cancellationToken))
            {
                var paradaId = Entero(rd, "ViajeParadaID");
                if (!porId.TryGetValue(paradaId, out var parada)) continue;
                parada.Embarques.Add(new LogisticaViajeParadaEmbarqueVm
                {
                    ViajeEmbarqueID = Entero(rd, "ViajeEmbarqueID"),
                    ViajeID = Entero(rd, "ViajeID"),
                    ViajeParadaID = paradaId,
                    EmbarqueID = Entero(rd, "EmbarqueID"),
                    Folio = Texto(rd, "Folio"),
                    Cliente = Texto(rd, "Cliente"),
                    Destino = Texto(rd, "Destino"),
                    OrdenEntrega = EnteroNullable(rd, "OrdenEntrega"),
                    TotalPiezas = Entero(rd, "TotalPiezas"),
                    Estatus = Texto(rd, "Estatus")
                });
            }
        }
        const string sqlEvidencias = @"SELECT ViajeParadaEvidenciaID,ViajeParadaID,ISNULL(TipoEvidencia,N'') TipoEvidencia,ISNULL(NombreOriginal,N'') NombreOriginal,ISNULL(NombreFisico,N'') NombreFisico,ISNULL(RutaRelativa,N'') RutaRelativa,ISNULL(TipoContenido,N'') TipoContenido,ISNULL(TamanoBytes,0) TamanoBytes,ISNULL(Observaciones,N'') Observaciones,UsuarioCargaID,ISNULL(UsuarioCargaNombre,N'') UsuarioCargaNombre,FechaCarga FROM dbo.Logistica_ViajeParadaEvidencias WHERE ViajeParadaID IN(SELECT ViajeParadaID FROM dbo.Logistica_ViajeParadas WHERE ViajeID=@ViajeID AND Activo=1) AND Activo=1 ORDER BY FechaCarga DESC,ViajeParadaEvidenciaID DESC;";
        await using (var cmd = new SqlCommand(sqlEvidencias, cn))
        {
            cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = vm.ViajeID;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await rd.ReadAsync(cancellationToken))
            {
                var paradaId = Entero(rd, "ViajeParadaID");
                if (!porId.TryGetValue(paradaId, out var parada)) continue;
                parada.Evidencias.Add(new LogisticaViajeParadaEvidenciaVm
                {
                    ViajeParadaEvidenciaID = Entero(rd, "ViajeParadaEvidenciaID"),
                    ViajeParadaID = paradaId,
                    TipoEvidencia = Texto(rd, "TipoEvidencia"),
                    NombreOriginal = Texto(rd, "NombreOriginal"),
                    NombreFisico = Texto(rd, "NombreFisico"),
                    RutaRelativa = Texto(rd, "RutaRelativa"),
                    TipoContenido = Texto(rd, "TipoContenido"),
                    TamanoBytes = EnteroLargo(rd, "TamanoBytes"),
                    Observaciones = Texto(rd, "Observaciones"),
                    UsuarioCargaID = EnteroNullable(rd, "UsuarioCargaID"),
                    UsuarioCargaNombre = Texto(rd, "UsuarioCargaNombre"),
                    FechaCarga = Fecha(rd, "FechaCarga") ?? DateTime.MinValue
                });
            }
        }
        const string sqlHistorial = @"SELECT ViajeParadaHistorialID,ViajeParadaID,Evento,ISNULL(EstadoAnterior,N'') EstadoAnterior,ISNULL(EstadoNuevo,N'') EstadoNuevo,ISNULL(Observaciones,N'') Observaciones,UsuarioID,ISNULL(UsuarioNombre,N'') UsuarioNombre,FechaEvento FROM dbo.Logistica_ViajeParadaHistorial WHERE ViajeParadaID IN(SELECT ViajeParadaID FROM dbo.Logistica_ViajeParadas WHERE ViajeID=@ViajeID) ORDER BY FechaEvento DESC,ViajeParadaHistorialID DESC;";
        await using (var cmd = new SqlCommand(sqlHistorial, cn))
        {
            cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = vm.ViajeID;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await rd.ReadAsync(cancellationToken))
            {
                var paradaId = Entero(rd, "ViajeParadaID");
                if (!porId.TryGetValue(paradaId, out var parada)) continue;
                parada.Historial.Add(new LogisticaViajeParadaHistorialVm
                {
                    ViajeParadaHistorialID = Entero(rd, "ViajeParadaHistorialID"),
                    ViajeParadaID = paradaId,
                    Evento = Texto(rd, "Evento"),
                    EstadoAnterior = Texto(rd, "EstadoAnterior"),
                    EstadoNuevo = Texto(rd, "EstadoNuevo"),
                    Observaciones = Texto(rd, "Observaciones"),
                    UsuarioID = EnteroNullable(rd, "UsuarioID"),
                    UsuarioNombre = Texto(rd, "UsuarioNombre"),
                    FechaEvento = Fecha(rd, "FechaEvento") ?? DateTime.MinValue
                });
            }
        }
        vm.TotalParadas = vm.Paradas.Count;
        vm.ParadasCompletadas = vm.Paradas.Count(x => x.Estatus is "Completada" or "Omitida" or "Cancelada");
        vm.ParadasPendientes = vm.Paradas.Count(x => x.Estatus is "Pendiente" or "En camino" or "En sitio");
        vm.EsMultiParada = vm.EsMultiParada || vm.Paradas.Count(x => x.TipoParada != "Origen" && !x.CierraViaje) > 1;
    }

    private async Task InsertarHistorialParadaAsync(SqlConnection cn, SqlTransaction tx, int viajeParadaId, string evento, string? anterior, string? nuevo, string? observaciones, CancellationToken cancellationToken)
    {
        const string sql = @"INSERT dbo.Logistica_ViajeParadaHistorial(ViajeParadaID,Evento,EstadoAnterior,EstadoNuevo,Observaciones,UsuarioID,UsuarioNombre,FechaEvento) VALUES(@ViajeParadaID,@Evento,@Anterior,@Nuevo,@Observaciones,@UsuarioID,@UsuarioNombre,SYSDATETIME());";
        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@ViajeParadaID", SqlDbType.Int).Value = viajeParadaId;
        cmd.Parameters.Add("@Evento", SqlDbType.NVarChar, 80).Value = evento;
        cmd.Parameters.Add("@Anterior", SqlDbType.NVarChar, 20).Value = Db(anterior);
        cmd.Parameters.Add("@Nuevo", SqlDbType.NVarChar, 20).Value = Db(nuevo);
        cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1200).Value = Db(observaciones);
        cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = Db(UsuarioID);
        cmd.Parameters.Add("@UsuarioNombre", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ActualizarOrdenEntregaViajeAsync(SqlConnection cn, SqlTransaction tx, int viajeId, CancellationToken cancellationToken)
    {
        const string sql = @"
;WITH X AS
(
    SELECT ve.ViajeEmbarqueID,ROW_NUMBER() OVER(ORDER BY p.Secuencia,p.ViajeParadaID,ve.ViajeEmbarqueID) OrdenEntrega
    FROM dbo.Logistica_ViajeEmbarques ve
    INNER JOIN dbo.Logistica_ViajeParadas p ON p.ViajeParadaID=ve.ViajeParadaID AND p.Activo=1
    WHERE ve.ViajeID=@ViajeID AND ve.Activo=1
)
UPDATE ve SET OrdenEntrega=x.OrdenEntrega FROM dbo.Logistica_ViajeEmbarques ve INNER JOIN X x ON x.ViajeEmbarqueID=ve.ViajeEmbarqueID;";
        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task ActualizarIndicadorMultiParadaAsync(SqlConnection cn, SqlTransaction tx, int viajeId, CancellationToken cancellationToken)
    {
        const string sql = @"
UPDATE v
SET EsMultiParada=CASE WHEN v.TipoViaje=N'Ruta multipropósito' OR x.Operativas>1 THEN 1 ELSE 0 END,
FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
FROM dbo.Logistica_Viajes v
CROSS APPLY
(
    SELECT COUNT_BIG(*) Operativas
    FROM dbo.Logistica_ViajeParadas p
    WHERE p.ViajeID=v.ViajeID AND p.Activo=1 AND p.TipoParada<>N'Origen' AND ISNULL(p.CierraViaje,0)=0
)x
WHERE v.ViajeID=@ViajeID AND v.Activo=1;";
        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
        cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
    private static async Task ReordenarSecuenciasParadasAsync(SqlConnection cn, SqlTransaction tx, int viajeId, CancellationToken cancellationToken)
    {
        const string sql = @"
DECLARE @Offset int=(SELECT ISNULL(MAX(Secuencia),0)+10000 FROM dbo.Logistica_ViajeParadas WHERE ViajeID=@ViajeID);
UPDATE dbo.Logistica_ViajeParadas SET Secuencia=Secuencia+@Offset WHERE ViajeID=@ViajeID AND Activo=1;
;WITH X AS
(
    SELECT ViajeParadaID,ROW_NUMBER() OVER(ORDER BY Secuencia,ViajeParadaID) NuevaSecuencia
    FROM dbo.Logistica_ViajeParadas
    WHERE ViajeID=@ViajeID AND Activo=1
)
UPDATE p SET Secuencia=x.NuevaSecuencia FROM dbo.Logistica_ViajeParadas p INNER JOIN X x ON x.ViajeParadaID=p.ViajeParadaID;";
        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void NormalizarParadaCaptura(LogisticaViajeParadaCapturaVm model)
    {
        model.TipoParada = NormalizarTipoParada(model.TipoParada);
        model.TipoOperacion = model.TipoOperacion?.Trim();
        model.EntidadTipo = model.EntidadTipo?.Trim();
        model.EntidadNombreSnapshot = model.EntidadNombreSnapshot?.Trim();
        model.Lugar = model.Lugar?.Trim() ?? string.Empty;
        model.Direccion = model.Direccion?.Trim();
        model.ReferenciaTipo = model.ReferenciaTipo?.Trim();
        model.ReferenciaFolioSnapshot = model.ReferenciaFolioSnapshot?.Trim();
        model.ContactoNombre = model.ContactoNombre?.Trim();
        model.ContactoTelefono = model.ContactoTelefono?.Trim();
        model.Observaciones = model.Observaciones?.Trim();
    }

    private static string NormalizarTipoParada(string? valor)
    {
        valor = valor?.Trim() ?? string.Empty;
        if (valor.Equals("Origen", StringComparison.OrdinalIgnoreCase)) return "Origen";
        if (valor.Equals("Entrega", StringComparison.OrdinalIgnoreCase)) return "Entrega";
        if (valor.Equals("Recoleccion", StringComparison.OrdinalIgnoreCase) || valor.Equals("Recolección", StringComparison.OrdinalIgnoreCase)) return "Recoleccion";
        if (valor.Equals("Traslado", StringComparison.OrdinalIgnoreCase)) return "Traslado";
        if (valor.Equals("Servicio", StringComparison.OrdinalIgnoreCase)) return "Servicio";
        if (valor.Equals("Retorno", StringComparison.OrdinalIgnoreCase)) return "Retorno";
        if (valor.Equals("Otro", StringComparison.OrdinalIgnoreCase)) return "Otro";
        return string.Empty;
    }
    private static decimal? DecimalNullable(SqlDataReader rd, string columna)
    {
        var i = rd.GetOrdinal(columna);
        return rd.IsDBNull(i) ? null : Convert.ToDecimal(rd.GetValue(i));
    }

    private static async Task CargarEvidenciasViajeAsync(SqlConnection cn, LogisticaViajeDetalleVm vm, CancellationToken cancellationToken)
    {
        vm.Evidencias.Clear();
        const string sql = @"
SELECT ViajeEvidenciaID,ViajeID,ISNULL(TipoEvidencia,N'') AS TipoEvidencia,ISNULL(NombreOriginal,N'') AS NombreOriginal,
ISNULL(NombreFisico,N'') AS NombreFisico,ISNULL(RutaRelativa,N'') AS RutaRelativa,ISNULL(TipoContenido,N'') AS TipoContenido,
ISNULL(TamanoBytes,0) AS TamanoBytes,ISNULL(Observaciones,N'') AS Observaciones,UsuarioCargaID,
ISNULL(UsuarioCargaNombre,N'') AS UsuarioCargaNombre,FechaCarga
FROM dbo.Logistica_ViajeEvidencias
WHERE ViajeID=@ViajeID AND Activo=1
ORDER BY FechaCarga DESC,ViajeEvidenciaID DESC;";
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = vm.ViajeID;
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await rd.ReadAsync(cancellationToken))
        {
            vm.Evidencias.Add(new LogisticaViajeEvidenciaVm
            {
                ViajeEvidenciaID = Entero(rd, "ViajeEvidenciaID"),
                ViajeID = Entero(rd, "ViajeID"),
                TipoEvidencia = Texto(rd, "TipoEvidencia"),
                NombreOriginal = Texto(rd, "NombreOriginal"),
                NombreFisico = Texto(rd, "NombreFisico"),
                RutaRelativa = Texto(rd, "RutaRelativa"),
                TipoContenido = Texto(rd, "TipoContenido"),
                TamanoBytes = EnteroLargo(rd, "TamanoBytes"),
                Observaciones = Texto(rd, "Observaciones"),
                UsuarioCargaID = EnteroNullable(rd, "UsuarioCargaID"),
                UsuarioCargaNombre = Texto(rd, "UsuarioCargaNombre"),
                FechaCarga = Fecha(rd, "FechaCarga") ?? DateTime.MinValue
            });
        }
    }
    private async Task CargarCatalogosAsync(LogisticaViajeCrearVm vm, SqlConnection cn, CancellationToken cancellationToken)
    {
        vm.Rutas.Clear();
        vm.Unidades.Clear();
        vm.Operadores.Clear();
        const string sql = @"
SELECT RutaID,Codigo+N' - '+Nombre AS Texto
FROM dbo.Logistica_Rutas
WHERE Activo=1
ORDER BY Codigo;
SELECT UnidadID,NumeroEconomico+CASE WHEN NULLIF(LTRIM(RTRIM(ISNULL(Placas,N''))),N'') IS NULL THEN N'' ELSE N' - '+LTRIM(RTRIM(Placas)) END AS Texto
FROM dbo.Logistica_Unidades
WHERE Activo=1
ORDER BY NumeroEconomico;
SELECT DISTINCT U.UsuarioID AS Id,
LTRIM(RTRIM(CONCAT(ISNULL(P.Nombre,N''),N' ',ISNULL(P.ApellidoPaterno,N''),N' ',ISNULL(P.ApellidoMaterno,N''))))+
CASE WHEN NULLIF(LTRIM(RTRIM(ISNULL(P.Puesto,N''))),N'') IS NULL THEN N'' ELSE N' - '+LTRIM(RTRIM(P.Puesto)) END AS Texto
FROM dbo.Usuarios U
INNER JOIN dbo.Persona P ON P.PersonaID=U.PersonaID
INNER JOIN dbo.Departamentos D ON D.DepartamentoID=U.DepartamentoID
WHERE U.Activo=1
AND D.Activo=1
AND UPPER(REPLACE(LTRIM(RTRIM(ISNULL(D.NombreDepartamento,N''))),N'Í',N'I'))=N'LOGISTICA'
AND UPPER(LTRIM(RTRIM(ISNULL(P.Puesto,N'')))) LIKE N'%CHOFER%'
ORDER BY Texto;";
        await using var cmd = new SqlCommand(sql, cn);
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await rd.ReadAsync(cancellationToken))
            vm.Rutas.Add(new LogisticaViajeSelectVm { Id = Entero(rd, "RutaID"), Texto = Texto(rd, "Texto") });
        if (await rd.NextResultAsync(cancellationToken))
            while (await rd.ReadAsync(cancellationToken))
                vm.Unidades.Add(new LogisticaViajeSelectVm { Id = Entero(rd, "UnidadID"), Texto = Texto(rd, "Texto") });
        if (await rd.NextResultAsync(cancellationToken))
            while (await rd.ReadAsync(cancellationToken))
                vm.Operadores.Add(new LogisticaViajeSelectVm { Id = Entero(rd, "Id"), Texto = Texto(rd, "Texto") });
    }
    private async Task CargarCatalogosAsync(LogisticaViajeEditarVm vm, SqlConnection cn, CancellationToken cancellationToken)
    {
        vm.Rutas.Clear();
        vm.Unidades.Clear();
        vm.Operadores.Clear();
        const string sql = @"
SELECT RutaID,Codigo+N' - '+Nombre AS Texto
FROM dbo.Logistica_Rutas
WHERE Activo=1
ORDER BY Codigo;
SELECT UnidadID,NumeroEconomico+CASE WHEN NULLIF(LTRIM(RTRIM(ISNULL(Placas,N''))),N'') IS NULL THEN N'' ELSE N' - '+LTRIM(RTRIM(Placas)) END AS Texto
FROM dbo.Logistica_Unidades
WHERE Activo=1
ORDER BY NumeroEconomico;
SELECT DISTINCT U.UsuarioID AS Id,
LTRIM(RTRIM(CONCAT(ISNULL(P.Nombre,N''),N' ',ISNULL(P.ApellidoPaterno,N''),N' ',ISNULL(P.ApellidoMaterno,N''))))+
CASE WHEN NULLIF(LTRIM(RTRIM(ISNULL(P.Puesto,N''))),N'') IS NULL THEN N'' ELSE N' - '+LTRIM(RTRIM(P.Puesto)) END AS Texto
FROM dbo.Usuarios U
INNER JOIN dbo.Persona P ON P.PersonaID=U.PersonaID
INNER JOIN dbo.Departamentos D ON D.DepartamentoID=U.DepartamentoID
WHERE U.Activo=1
AND D.Activo=1
AND UPPER(REPLACE(LTRIM(RTRIM(ISNULL(D.NombreDepartamento,N''))),N'Í',N'I'))=N'LOGISTICA'
AND UPPER(LTRIM(RTRIM(ISNULL(P.Puesto,N'')))) LIKE N'%CHOFER%'
ORDER BY Texto;";
        await using var cmd = new SqlCommand(sql, cn);
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await rd.ReadAsync(cancellationToken))
            vm.Rutas.Add(new LogisticaViajeSelectVm { Id = Entero(rd, "RutaID"), Texto = Texto(rd, "Texto") });
        if (await rd.NextResultAsync(cancellationToken))
            while (await rd.ReadAsync(cancellationToken))
                vm.Unidades.Add(new LogisticaViajeSelectVm { Id = Entero(rd, "UnidadID"), Texto = Texto(rd, "Texto") });
        if (await rd.NextResultAsync(cancellationToken))
            while (await rd.ReadAsync(cancellationToken))
                vm.Operadores.Add(new LogisticaViajeSelectVm { Id = Entero(rd, "Id"), Texto = Texto(rd, "Texto") });
    }

    private static async Task<string?> ValidarRecursosInternosAsync(SqlConnection cn, SqlTransaction tx, int? unidadId, int? operadorUsuarioId, CancellationToken cancellationToken)
    {
        // LOGISTICA_VIAJES_CHOFERES_V5: al programar los recursos pueden quedar pendientes.
        if (unidadId.HasValue && unidadId.Value > 0)
        {
            const string sqlUnidad = @"SELECT COUNT_BIG(*) FROM dbo.Logistica_Unidades WITH(UPDLOCK,HOLDLOCK) WHERE UnidadID=@UnidadID AND Activo=1;";
            await using var cmdUnidad = new SqlCommand(sqlUnidad, cn, tx);
            cmdUnidad.Parameters.Add("@UnidadID", SqlDbType.Int).Value = unidadId.Value;
            if (Convert.ToInt64(await cmdUnidad.ExecuteScalarAsync(cancellationToken)) <= 0) throw new InvalidOperationException("La unidad seleccionada no existe o se encuentra inactiva.");
        }
        if (!operadorUsuarioId.HasValue || operadorUsuarioId.Value <= 0) return null;
        const string sqlOperador = @"
SELECT TOP(1) LTRIM(RTRIM(CONCAT(ISNULL(P.Nombre,N''),N' ',ISNULL(P.ApellidoPaterno,N''),N' ',ISNULL(P.ApellidoMaterno,N''))))
FROM dbo.Usuarios U WITH(UPDLOCK,HOLDLOCK)
INNER JOIN dbo.Persona P ON P.PersonaID=U.PersonaID
INNER JOIN dbo.Departamentos D ON D.DepartamentoID=U.DepartamentoID
WHERE U.UsuarioID=@UsuarioID AND U.Activo=1 AND D.Activo=1
AND UPPER(REPLACE(LTRIM(RTRIM(ISNULL(D.NombreDepartamento,N''))),N'Í',N'I'))=N'LOGISTICA'
AND UPPER(LTRIM(RTRIM(ISNULL(P.Puesto,N'')))) LIKE N'%CHOFER%';";
        await using var cmdOperador = new SqlCommand(sqlOperador, cn, tx);
        cmdOperador.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = operadorUsuarioId.Value;
        var valor = await cmdOperador.ExecuteScalarAsync(cancellationToken);
        var nombre = valor == null || valor == DBNull.Value ? string.Empty : valor.ToString()?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(nombre)) throw new InvalidOperationException("El operador seleccionado debe ser un usuario activo de Logística con puesto de Chofer.");
        return nombre;
    }
    private static async Task<(string Estatus, string Folio)?> ObtenerViajeParaActualizarAsync(
        SqlConnection cn,
        SqlTransaction tx,
        int viajeId,
        CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT ISNULL(Estatus,N'') AS Estatus,ISNULL(Folio,N'') AS Folio
FROM dbo.Logistica_Viajes WITH (UPDLOCK,HOLDLOCK)
WHERE ViajeID=@ViajeID AND Activo=1;";

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;

        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);

        if (!await rd.ReadAsync(cancellationToken))
            return null;

        return (Texto(rd, "Estatus"), Texto(rd, "Folio"));
    }

    private async Task InsertarHistorialAsync(
        SqlConnection cn,
        SqlTransaction tx,
        int viajeId,
        string evento,
        string? anterior,
        string? nuevo,
        string? observaciones,
        CancellationToken cancellationToken)
    {
        const string sql = @"
INSERT dbo.Logistica_ViajeHistorial
(ViajeID,Evento,EstadoAnterior,EstadoNuevo,Observaciones,UsuarioID,UsuarioNombre,FechaEvento)
VALUES
(@ViajeID,@Evento,@Anterior,@Nuevo,@Observaciones,@UsuarioID,@UsuarioNombre,SYSDATETIME());";

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
        cmd.Parameters.Add("@Evento", SqlDbType.NVarChar, 80).Value = evento;
        cmd.Parameters.Add("@Anterior", SqlDbType.NVarChar, 30).Value = Db(anterior);
        cmd.Parameters.Add("@Nuevo", SqlDbType.NVarChar, 30).Value = Db(nuevo);
        cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1200).Value = Db(observaciones);
        cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = Db(UsuarioID);
        cmd.Parameters.Add("@UsuarioNombre", SqlDbType.NVarChar, 200).Value = UsuarioNombre;

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AgregarFiltros(SqlCommand cmd, string? q, string? estatus, string? tipoViaje, DateTime? fechaDesde, DateTime? fechaHasta)
    {
        cmd.Parameters.Add("@Q", SqlDbType.NVarChar, 250).Value = string.IsNullOrWhiteSpace(q) ? DBNull.Value : q.Trim();
        cmd.Parameters.Add("@Estatus", SqlDbType.NVarChar, 30).Value = string.IsNullOrWhiteSpace(estatus) ? DBNull.Value : estatus.Trim();
        cmd.Parameters.Add("@TipoViaje", SqlDbType.NVarChar, 50).Value = string.IsNullOrWhiteSpace(tipoViaje) ? DBNull.Value : tipoViaje.Trim();
        cmd.Parameters.Add("@FechaDesde", SqlDbType.Date).Value = fechaDesde.HasValue ? fechaDesde.Value.Date : DBNull.Value;
        cmd.Parameters.Add("@FechaHasta", SqlDbType.Date).Value = fechaHasta.HasValue ? fechaHasta.Value.Date : DBNull.Value;
    }

    [HttpGet]
    public async Task<IActionResult> Choferes(CancellationToken cancellationToken)
    {
        if (!UsuarioID.HasValue || UsuarioID.Value <= 0) return RedirectToAction("Login", "Login");
        var permisoViajes = await _acceso.TienePermisoAsync(UsuarioID.Value, "Viajes");
        var permisoTablero = await _acceso.TienePermisoAsync(UsuarioID.Value, "Tablero de Logística");
        if (!permisoViajes && !permisoTablero)
        {
            TempData["LogisticaError"] = "No tienes permiso para consultar la disponibilidad de choferes.";
            return RedirectToAction("Index", "LogisticaEmbarques");
        }
        var vm = new LogisticaChoferesVm(); await using var cn = await AbrirAsync(cancellationToken);
        const string sql = @"
WITH C AS
(
 SELECT DISTINCT U.UsuarioID,
 LTRIM(RTRIM(CONCAT(ISNULL(P.Nombre,N''),N' ',ISNULL(P.ApellidoPaterno,N''),N' ',ISNULL(P.ApellidoMaterno,N'')))) Nombre
 FROM dbo.Usuarios U INNER JOIN dbo.Persona P ON P.PersonaID=U.PersonaID INNER JOIN dbo.Departamentos D ON D.DepartamentoID=U.DepartamentoID
 WHERE U.Activo=1 AND D.Activo=1 AND UPPER(REPLACE(LTRIM(RTRIM(ISNULL(D.NombreDepartamento,N''))),N'Í',N'I'))=N'LOGISTICA' AND UPPER(LTRIM(RTRIM(ISNULL(P.Puesto,N'')))) LIKE N'%CHOFER%'
)
SELECT C.UsuarioID,C.Nombre,
 A.Fuente FuenteActual,A.Folio FolioActual,A.Destino DestinoActual,A.Fecha SalidaActual,
 P.Fuente FuenteProxima,P.Folio FolioProximo,P.Destino DestinoProximo,P.Fecha ProximaSalida
FROM C
OUTER APPLY
(
 SELECT TOP(1) X.Fuente,X.Folio,X.Destino,X.Fecha FROM
 (
  SELECT N'Viaje' Fuente,ISNULL(v.Folio,N'') Folio,ISNULL(v.Destino,N'') Destino,COALESCE(v.FechaSalidaReal,CAST(v.FechaProgramada AS datetime2)) Fecha
  FROM dbo.Logistica_Viajes v WHERE v.Activo=1 AND v.Estatus=N'En curso' AND v.OperadorUsuarioID=C.UsuarioID
  UNION ALL
  SELECT N'Embarque',ISNULL(e.Folio,N''),ISNULL(e.Destino,N''),COALESCE(e.FechaSalida,CAST(e.FechaCargaProgramada AS datetime2),CAST(e.FechaProgramada AS datetime2))
  FROM dbo.Logistica_Embarques e WHERE e.Activo=1 AND e.Estatus=N'En ruta' AND UPPER(LTRIM(RTRIM(ISNULL(e.OperadorTexto,N''))))=UPPER(LTRIM(RTRIM(C.Nombre)))
 ) X ORDER BY X.Fecha DESC
) A
OUTER APPLY
(
 SELECT TOP(1) X.Fuente,X.Folio,X.Destino,X.Fecha FROM
 (
  SELECT N'Viaje' Fuente,ISNULL(v.Folio,N'') Folio,ISNULL(v.Destino,N'') Destino,CAST(v.FechaProgramada AS datetime2) Fecha
  FROM dbo.Logistica_Viajes v WHERE v.Activo=1 AND v.Estatus=N'Programado' AND v.OperadorUsuarioID=C.UsuarioID AND v.FechaProgramada>=CAST(GETDATE() AS date)
  UNION ALL
  SELECT N'Embarque',ISNULL(e.Folio,N''),ISNULL(e.Destino,N''),COALESCE(CAST(e.FechaCargaProgramada AS datetime2),CAST(e.FechaProgramada AS datetime2))
  FROM dbo.Logistica_Embarques e WHERE e.Activo=1 AND e.Estatus IN(N'Programado',N'Preparando',N'Preparado') AND UPPER(LTRIM(RTRIM(ISNULL(e.OperadorTexto,N''))))=UPPER(LTRIM(RTRIM(C.Nombre))) AND COALESCE(e.FechaCargaProgramada,e.FechaProgramada)>=CAST(GETDATE() AS date)
 ) X ORDER BY X.Fecha
) P
ORDER BY CASE WHEN A.Folio IS NOT NULL THEN 1 WHEN P.Folio IS NOT NULL THEN 2 ELSE 3 END,C.Nombre;";
        await using var cmd = new SqlCommand(sql, cn); await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await rd.ReadAsync(cancellationToken))
        {
            var actual = !rd.IsDBNull(rd.GetOrdinal("FolioActual")); var proxima = !rd.IsDBNull(rd.GetOrdinal("FolioProximo"));
            vm.Choferes.Add(new LogisticaChoferEstadoVm { UsuarioID=Entero(rd,"UsuarioID"),Chofer=Texto(rd,"Nombre"),Estado=actual?"En viaje":proxima?"Programado":"Disponible",FuenteActual=Texto(rd,"FuenteActual"),FolioActual=Texto(rd,"FolioActual"),DestinoActual=Texto(rd,"DestinoActual"),SalidaActual=Fecha(rd,"SalidaActual"),FuenteProxima=Texto(rd,"FuenteProxima"),FolioProximo=Texto(rd,"FolioProximo"),DestinoProximo=Texto(rd,"DestinoProximo"),ProximaSalida=Fecha(rd,"ProximaSalida") });
        }
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Calendario(CancellationToken cancellationToken)
    {
        var acceso = await ValidarAccesoAsync();
        if (acceso != null) return acceso;
        await using var cn = await AbrirAsync(cancellationToken);
        const string sql = @"SELECT CASE WHEN OBJECT_ID(N'dbo.Logistica_Viajes',N'U') IS NOT NULL THEN 1 ELSE 0 END;";
        await using var cmd = new SqlCommand(sql, cn);
        if (Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) != 1)
        {
            TempData["LogisticaError"] = "Falta la estructura de Viajes.";
            return RedirectToAction(nameof(Index));
        }
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> EventosCalendario(DateTime desde, DateTime hasta, string? estatus = null, string? q = null, CancellationToken cancellationToken = default)
    {
        var acceso = await ValidarAccesoAsync();
        if (acceso != null) return acceso;
        desde = desde.Date;
        hasta = hasta.Date;
        if (hasta < desde) return BadRequest(new { ok = false, mensaje = "El rango de fechas no es válido." });
        if ((hasta - desde).TotalDays > 120) return BadRequest(new { ok = false, mensaje = "El calendario solo puede consultar hasta 120 días por solicitud." });
        q = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
        estatus = NormalizarEstatusFiltro(estatus);
        await using var cn = await AbrirAsync(cancellationToken);
        const string sql = @"
SELECT v.ViajeID,ISNULL(v.Folio,N'') AS Folio,ISNULL(v.TipoViaje,N'') AS TipoViaje,ISNULL(v.Origen,N'') AS Origen,
ISNULL(v.Destino,N'') AS Destino,ISNULL(v.Motivo,N'') AS Motivo,v.FechaProgramada,v.HoraSalidaProgramada,
ISNULL(v.Estatus,N'') AS Estatus,ISNULL(v.TieneIncidencia,0) AS TieneIncidencia,
ISNULL(u.NumeroEconomico+CASE WHEN NULLIF(LTRIM(RTRIM(ISNULL(u.Placas,N''))),N'') IS NULL THEN N'' ELSE N' - '+LTRIM(RTRIM(u.Placas)) END,N'') AS Unidad,
ISNULL(NULLIF(LTRIM(RTRIM(v.OperadorNombreSnapshot)),N''),ISNULL(v.OperadorTexto,N'')) AS Operador
FROM dbo.Logistica_Viajes v
LEFT JOIN dbo.Logistica_Unidades u ON u.UnidadID=v.UnidadID
WHERE v.Activo=1
AND v.FechaProgramada>=@Desde
AND v.FechaProgramada<DATEADD(DAY,1,@Hasta)
AND(@Estatus IS NULL OR v.Estatus=@Estatus)
AND(@Q IS NULL OR v.Folio LIKE N'%'+@Q+N'%' OR v.Origen LIKE N'%'+@Q+N'%' OR v.Destino LIKE N'%'+@Q+N'%' OR v.Motivo LIKE N'%'+@Q+N'%' OR v.OperadorTexto LIKE N'%'+@Q+N'%')
ORDER BY v.FechaProgramada,v.HoraSalidaProgramada,v.ViajeID;";
        var eventos = new List<object>();
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@Desde", SqlDbType.Date).Value = desde;
        cmd.Parameters.Add("@Hasta", SqlDbType.Date).Value = hasta;
        cmd.Parameters.Add("@Estatus", SqlDbType.NVarChar, 30).Value = Db(estatus);
        cmd.Parameters.Add("@Q", SqlDbType.NVarChar, 250).Value = Db(q);
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await rd.ReadAsync(cancellationToken))
        {
            var viajeId = Entero(rd, "ViajeID");
            var fecha = Fecha(rd, "FechaProgramada");
            if (!fecha.HasValue) continue;
            eventos.Add(new
            {
                viajeId,
                fecha = fecha.Value.ToString("yyyy-MM-dd"),
                hora = Hora(rd, "HoraSalidaProgramada")?.ToString(@"hh\:mm") ?? "",
                titulo = string.IsNullOrWhiteSpace(Texto(rd, "Folio")) ? $"Viaje {viajeId}" : Texto(rd, "Folio"),
                tipoViaje = Texto(rd, "TipoViaje"),
                origen = Texto(rd, "Origen"),
                destino = Texto(rd, "Destino"),
                motivo = Texto(rd, "Motivo"),
                unidad = Texto(rd, "Unidad"),
                operador = Texto(rd, "Operador"),
                estatus = Texto(rd, "Estatus"),
                incidencia = Booleano(rd, "TieneIncidencia"),
                url = Url.Action(nameof(Detalle), "LogisticaViajes", new { id = viajeId })
            });
        }
        return Json(new { ok = true, desde = desde.ToString("yyyy-MM-dd"), hasta = hasta.ToString("yyyy-MM-dd"), total = eventos.Count, eventos });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AgregarParada(LogisticaViajeParadaCapturaVm model, CancellationToken cancellationToken = default)
    {
        var acceso = await ValidarAccesoAsync();
        if (acceso != null) return acceso;
        NormalizarParadaCaptura(model);
        if (model.ViajeID <= 0) { TempData["LogisticaError"] = "El viaje indicado no es válido."; return RedirectToAction(nameof(Index)); }
        if (string.IsNullOrWhiteSpace(model.TipoParada)) { TempData["LogisticaError"] = "Selecciona un tipo de parada válido."; return RedirectToAction(nameof(Detalle), new { id = model.ViajeID }); }
        if (model.TipoParada == "Origen") { TempData["LogisticaError"] = "El origen del viaje no se agrega como una parada intermedia."; return RedirectToAction(nameof(Detalle), new { id = model.ViajeID }); }
        if (string.IsNullOrWhiteSpace(model.Lugar) && !model.EmbarqueID.HasValue) { TempData["LogisticaError"] = "Captura el lugar de la parada."; return RedirectToAction(nameof(Detalle), new { id = model.ViajeID }); }
        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            string folioViaje, estatusViaje;
            const string sqlViaje = @"SELECT ISNULL(Folio,N'') Folio,ISNULL(Estatus,N'') Estatus FROM dbo.Logistica_Viajes WITH(UPDLOCK,HOLDLOCK) WHERE ViajeID=@ViajeID AND Activo=1;";
            await using (var cmdViaje = new SqlCommand(sqlViaje, cn, tx))
            {
                cmdViaje.Parameters.Add("@ViajeID", SqlDbType.Int).Value = model.ViajeID;
                await using var rd = await cmdViaje.ExecuteReaderAsync(cancellationToken);
                if (!await rd.ReadAsync(cancellationToken)) throw new InvalidOperationException("El viaje no existe.");
                folioViaje = Texto(rd, "Folio");
                estatusViaje = Texto(rd, "Estatus");
            }
            if (estatusViaje is not "Programado" and not "En curso") throw new InvalidOperationException($"No se pueden agregar paradas a un viaje en estatus {estatusViaje}.");
            if (model.TipoParada == "Retorno") model.CierraViaje = true;
            else model.CierraViaje = false;
            int? relacionViajeId = null;
            int? relacionParadaId = null;
            if (model.EmbarqueID.HasValue && model.EmbarqueID.Value > 0)
            {
                const string sqlEmbarque = @"
SELECT e.EmbarqueID,e.ClienteID,ISNULL(e.Folio,N'') Folio,ISNULL(e.ClienteNombreSnapshot,N'') Cliente,ISNULL(e.Destino,N'') Destino,ISNULL(e.DireccionEntrega,N'') Direccion,
rel.ViajeID,rel.ViajeParadaID
FROM dbo.Logistica_Embarques e WITH(UPDLOCK,HOLDLOCK)
OUTER APPLY
(
    SELECT TOP(1) ve.ViajeID,ve.ViajeParadaID
    FROM dbo.Logistica_ViajeEmbarques ve WITH(UPDLOCK,HOLDLOCK)
    WHERE ve.EmbarqueID=e.EmbarqueID AND ve.Activo=1
    ORDER BY ve.ViajeEmbarqueID DESC
) rel
WHERE e.EmbarqueID=@EmbarqueID AND e.Activo=1;";
                await using var cmdEmbarque = new SqlCommand(sqlEmbarque, cn, tx);
                cmdEmbarque.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = model.EmbarqueID.Value;
                await using var rd = await cmdEmbarque.ExecuteReaderAsync(cancellationToken);
                if (!await rd.ReadAsync(cancellationToken)) throw new InvalidOperationException("El embarque seleccionado no existe.");
                relacionViajeId = EnteroNullable(rd, "ViajeID");
                relacionParadaId = EnteroNullable(rd, "ViajeParadaID");
                if (relacionViajeId.HasValue && relacionViajeId.Value != model.ViajeID) throw new InvalidOperationException($"El embarque ya se encuentra relacionado con el viaje VIA-{relacionViajeId.Value:000000}.");
                if (relacionViajeId == model.ViajeID && relacionParadaId.HasValue) throw new InvalidOperationException("El embarque ya tiene una parada de entrega dentro de este viaje.");
                model.TipoParada = "Entrega";
                model.TipoOperacion = "Entrega PT";
                model.EntidadTipo = "Cliente";
                model.EntidadID = Entero(rd, "ClienteID");
                model.EntidadNombreSnapshot = Texto(rd, "Cliente");
                model.Lugar = string.IsNullOrWhiteSpace(Texto(rd, "Destino")) ? Texto(rd, "Cliente") : Texto(rd, "Destino");
                model.Direccion = TextoNullable(rd, "Direccion");
                model.ReferenciaTipo = "Embarque";
                model.ReferenciaID = model.EmbarqueID.Value;
                model.ReferenciaFolioSnapshot = Texto(rd, "Folio");
                model.RequiereEvidencia = true;
                model.CierraViaje = false;
            }
            if (model.CierraViaje)
            {
                const string sqlCierre = @"SELECT COUNT_BIG(*) FROM dbo.Logistica_ViajeParadas WITH(UPDLOCK,HOLDLOCK) WHERE ViajeID=@ViajeID AND Activo=1 AND CierraViaje=1;";
                await using var cmdCierre = new SqlCommand(sqlCierre, cn, tx);
                cmdCierre.Parameters.Add("@ViajeID", SqlDbType.Int).Value = model.ViajeID;
                if (Convert.ToInt64(await cmdCierre.ExecuteScalarAsync(cancellationToken)) > 0) throw new InvalidOperationException("El viaje ya tiene una parada final de retorno.");
            }
            int maxSecuencia;
            int? secuenciaCierre;
            int maxSecuenciaResuelta;
            const string sqlSecuencias = @"
SELECT ISNULL(MAX(Secuencia),0) MaxSecuencia FROM dbo.Logistica_ViajeParadas WITH(UPDLOCK,HOLDLOCK) WHERE ViajeID=@ViajeID AND Activo=1;
SELECT TOP(1) Secuencia FROM dbo.Logistica_ViajeParadas WITH(UPDLOCK,HOLDLOCK) WHERE ViajeID=@ViajeID AND Activo=1 AND CierraViaje=1 ORDER BY Secuencia DESC;
SELECT ISNULL(MAX(Secuencia),0) MaxResuelta FROM dbo.Logistica_ViajeParadas WITH(UPDLOCK,HOLDLOCK) WHERE ViajeID=@ViajeID AND Activo=1 AND Estatus IN(N'Completada',N'Omitida');";
            await using (var cmdSecuencias = new SqlCommand(sqlSecuencias, cn, tx))
            {
                cmdSecuencias.Parameters.Add("@ViajeID", SqlDbType.Int).Value = model.ViajeID;
                await using var rd = await cmdSecuencias.ExecuteReaderAsync(cancellationToken);
                await rd.ReadAsync(cancellationToken);
                maxSecuencia = Entero(rd, "MaxSecuencia");
                secuenciaCierre = null;
                maxSecuenciaResuelta = 0;
                if (await rd.NextResultAsync(cancellationToken) && await rd.ReadAsync(cancellationToken)) secuenciaCierre = Entero(rd, "Secuencia");
                if (await rd.NextResultAsync(cancellationToken) && await rd.ReadAsync(cancellationToken)) maxSecuenciaResuelta = Entero(rd, "MaxResuelta");
            }
            var secuencia = model.Secuencia > 0 ? model.Secuencia : secuenciaCierre ?? maxSecuencia + 1;
            secuencia = Math.Max(2, secuencia);
            if (secuenciaCierre.HasValue && !model.CierraViaje) secuencia = Math.Min(secuencia, secuenciaCierre.Value);
            else secuencia = Math.Min(secuencia, maxSecuencia + 1);
            if (estatusViaje == "En curso" && secuencia <= maxSecuenciaResuelta) throw new InvalidOperationException("No puedes insertar una nueva parada antes de paradas que ya fueron atendidas.");
            if (secuencia <= maxSecuencia)
            {
                var desplazamiento = maxSecuencia + 10000;
                const string sqlMover = @"
UPDATE dbo.Logistica_ViajeParadas SET Secuencia=Secuencia+@Desplazamiento WHERE ViajeID=@ViajeID AND Activo=1 AND Secuencia>=@Secuencia;
UPDATE dbo.Logistica_ViajeParadas SET Secuencia=Secuencia-@Desplazamiento+1 WHERE ViajeID=@ViajeID AND Activo=1 AND Secuencia>=@Secuencia+@Desplazamiento;";
                await using var cmdMover = new SqlCommand(sqlMover, cn, tx);
                cmdMover.Parameters.Add("@Desplazamiento", SqlDbType.Int).Value = desplazamiento;
                cmdMover.Parameters.Add("@ViajeID", SqlDbType.Int).Value = model.ViajeID;
                cmdMover.Parameters.Add("@Secuencia", SqlDbType.Int).Value = secuencia;
                await cmdMover.ExecuteNonQueryAsync(cancellationToken);
            }
            const string sqlInsert = @"
INSERT dbo.Logistica_ViajeParadas(ViajeID,Secuencia,TipoParada,TipoOperacion,EntidadTipo,EntidadID,EntidadNombreSnapshot,Lugar,Direccion,ReferenciaTipo,ReferenciaID,ReferenciaFolioSnapshot,FechaHoraLlegadaProgramada,FechaHoraSalidaProgramada,Estatus,RequiereEvidencia,CierraViaje,ContactoNombre,ContactoTelefono,Observaciones,Activo,FechaCreacion,CreadoPor)
VALUES(@ViajeID,@Secuencia,@TipoParada,@TipoOperacion,@EntidadTipo,@EntidadID,@EntidadNombre,@Lugar,@Direccion,@ReferenciaTipo,@ReferenciaID,@ReferenciaFolio,@LlegadaProgramada,@SalidaProgramada,N'Pendiente',@RequiereEvidencia,@CierraViaje,@ContactoNombre,@ContactoTelefono,@Observaciones,1,SYSDATETIME(),@Usuario);
SELECT CONVERT(int,SCOPE_IDENTITY());";
            int paradaId;
            await using (var cmdInsert = new SqlCommand(sqlInsert, cn, tx))
            {
                cmdInsert.Parameters.Add("@ViajeID", SqlDbType.Int).Value = model.ViajeID;
                cmdInsert.Parameters.Add("@Secuencia", SqlDbType.Int).Value = secuencia;
                cmdInsert.Parameters.Add("@TipoParada", SqlDbType.NVarChar, 30).Value = model.TipoParada;
                cmdInsert.Parameters.Add("@TipoOperacion", SqlDbType.NVarChar, 80).Value = Db(model.TipoOperacion);
                cmdInsert.Parameters.Add("@EntidadTipo", SqlDbType.NVarChar, 30).Value = Db(model.EntidadTipo);
                cmdInsert.Parameters.Add("@EntidadID", SqlDbType.Int).Value = Db(model.EntidadID);
                cmdInsert.Parameters.Add("@EntidadNombre", SqlDbType.NVarChar, 200).Value = Db(model.EntidadNombreSnapshot);
                cmdInsert.Parameters.Add("@Lugar", SqlDbType.NVarChar, 300).Value = model.Lugar;
                cmdInsert.Parameters.Add("@Direccion", SqlDbType.NVarChar, 600).Value = Db(model.Direccion);
                cmdInsert.Parameters.Add("@ReferenciaTipo", SqlDbType.NVarChar, 50).Value = Db(model.ReferenciaTipo);
                cmdInsert.Parameters.Add("@ReferenciaID", SqlDbType.Int).Value = Db(model.ReferenciaID);
                cmdInsert.Parameters.Add("@ReferenciaFolio", SqlDbType.NVarChar, 120).Value = Db(model.ReferenciaFolioSnapshot);
                cmdInsert.Parameters.Add("@LlegadaProgramada", SqlDbType.DateTime2).Value = Db(model.FechaHoraLlegadaProgramada);
                cmdInsert.Parameters.Add("@SalidaProgramada", SqlDbType.DateTime2).Value = Db(model.FechaHoraSalidaProgramada);
                cmdInsert.Parameters.Add("@RequiereEvidencia", SqlDbType.Bit).Value = model.RequiereEvidencia;
                cmdInsert.Parameters.Add("@CierraViaje", SqlDbType.Bit).Value = model.CierraViaje;
                cmdInsert.Parameters.Add("@ContactoNombre", SqlDbType.NVarChar, 200).Value = Db(model.ContactoNombre);
                cmdInsert.Parameters.Add("@ContactoTelefono", SqlDbType.NVarChar, 50).Value = Db(model.ContactoTelefono);
                cmdInsert.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1200).Value = Db(model.Observaciones);
                cmdInsert.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                paradaId = Convert.ToInt32(await cmdInsert.ExecuteScalarAsync(cancellationToken));
            }
            if (model.EmbarqueID.HasValue && model.EmbarqueID.Value > 0)
            {
                const string sqlRelacion = @"
UPDATE dbo.Logistica_ViajeEmbarques SET ViajeParadaID=@ViajeParadaID,Activo=1 WHERE ViajeID=@ViajeID AND EmbarqueID=@EmbarqueID AND Activo=1;
IF @@ROWCOUNT=0 INSERT dbo.Logistica_ViajeEmbarques(ViajeID,EmbarqueID,OrdenEntrega,ViajeParadaID,Activo) VALUES(@ViajeID,@EmbarqueID,1,@ViajeParadaID,1);";
                await using var cmdRelacion = new SqlCommand(sqlRelacion, cn, tx);
                cmdRelacion.Parameters.Add("@ViajeParadaID", SqlDbType.Int).Value = paradaId;
                cmdRelacion.Parameters.Add("@ViajeID", SqlDbType.Int).Value = model.ViajeID;
                cmdRelacion.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = model.EmbarqueID.Value;
                await cmdRelacion.ExecuteNonQueryAsync(cancellationToken);
            }
            await ActualizarOrdenEntregaViajeAsync(cn, tx, model.ViajeID, cancellationToken);
            await ActualizarIndicadorMultiParadaAsync(cn, tx, model.ViajeID, cancellationToken);
            await InsertarHistorialParadaAsync(cn, tx, paradaId, "PARADA_CREADA", null, "Pendiente", $"Parada #{secuencia} agregada al viaje {folioViaje}. Tipo: {model.TipoParada}. Lugar: {model.Lugar}.", cancellationToken);
            await InsertarHistorialAsync(cn, tx, model.ViajeID, "PARADA_AGREGADA", estatusViaje, estatusViaje, $"Parada #{secuencia}: {model.TipoParada} - {model.Lugar}.", cancellationToken);
            await tx.CommitAsync(cancellationToken);
            TempData["LogisticaOk"] = $"Parada #{secuencia} agregada correctamente.";
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(cancellationToken); } catch { }
            TempData["LogisticaError"] = ex.Message;
        }
        return RedirectToAction(nameof(Detalle), new { id = model.ViajeID });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditarParada(LogisticaViajeParadaCapturaVm model, CancellationToken cancellationToken = default)
    {
        var acceso = await ValidarAccesoAsync();
        if (acceso != null) return acceso;
        NormalizarParadaCaptura(model);
        if (model.ViajeID <= 0 || model.ViajeParadaID <= 0) { TempData["LogisticaError"] = "La parada indicada no es válida."; return model.ViajeID > 0 ? RedirectToAction(nameof(Detalle), new { id = model.ViajeID }) : RedirectToAction(nameof(Index)); }
        if (string.IsNullOrWhiteSpace(model.Lugar)) { TempData["LogisticaError"] = "Captura el lugar de la parada."; return RedirectToAction(nameof(Detalle), new { id = model.ViajeID }); }
        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            string estatusViaje, estatusParada, tipoActual, referenciaTipoActual, entidadTipoActual, entidadNombreActual, referenciaFolioActual;
            int? referenciaIdActual, entidadIdActual;
            bool cierraViaje, ligadaEmbarque;
            byte[] versionActual;
            const string sqlActual = @"
SELECT ISNULL(v.Estatus,N'') EstatusViaje,ISNULL(p.Estatus,N'') EstatusParada,ISNULL(p.TipoParada,N'') TipoParada,ISNULL(p.CierraViaje,0) CierraViaje,
ISNULL(p.ReferenciaTipo,N'') ReferenciaTipo,p.ReferenciaID,ISNULL(p.ReferenciaFolioSnapshot,N'') ReferenciaFolioSnapshot,ISNULL(p.EntidadTipo,N'') EntidadTipo,p.EntidadID,
ISNULL(p.EntidadNombreSnapshot,N'') EntidadNombreSnapshot,CONVERT(varbinary(8),p.RowVersion) RowVersion,
CASE WHEN EXISTS(SELECT 1 FROM dbo.Logistica_ViajeEmbarques ve WHERE ve.ViajeParadaID=p.ViajeParadaID AND ve.Activo=1) THEN 1 ELSE 0 END LigadaEmbarque
FROM dbo.Logistica_ViajeParadas p WITH(UPDLOCK,HOLDLOCK)
INNER JOIN dbo.Logistica_Viajes v WITH(UPDLOCK,HOLDLOCK) ON v.ViajeID=p.ViajeID
WHERE p.ViajeParadaID=@ParadaID AND p.ViajeID=@ViajeID AND p.Activo=1;";
            await using (var cmdActual = new SqlCommand(sqlActual, cn, tx))
            {
                cmdActual.Parameters.Add("@ParadaID", SqlDbType.Int).Value = model.ViajeParadaID;
                cmdActual.Parameters.Add("@ViajeID", SqlDbType.Int).Value = model.ViajeID;
                await using var rd = await cmdActual.ExecuteReaderAsync(cancellationToken);
                if (!await rd.ReadAsync(cancellationToken)) throw new InvalidOperationException("La parada no existe.");
                estatusViaje = Texto(rd, "EstatusViaje");
                estatusParada = Texto(rd, "EstatusParada");
                tipoActual = Texto(rd, "TipoParada");
                cierraViaje = Booleano(rd, "CierraViaje");
                referenciaTipoActual = Texto(rd, "ReferenciaTipo");
                referenciaIdActual = EnteroNullable(rd, "ReferenciaID");
                referenciaFolioActual = Texto(rd, "ReferenciaFolioSnapshot");
                entidadTipoActual = Texto(rd, "EntidadTipo");
                entidadIdActual = EnteroNullable(rd, "EntidadID");
                entidadNombreActual = Texto(rd, "EntidadNombreSnapshot");
                versionActual = (byte[])rd["RowVersion"];
                ligadaEmbarque = Booleano(rd, "LigadaEmbarque");
            }
            if (estatusViaje is not "Programado" and not "En curso") throw new InvalidOperationException($"El viaje está en estatus {estatusViaje} y ya no permite modificar sus paradas.");
            if (estatusParada is "Completada" or "Omitida") throw new InvalidOperationException("Una parada ya resuelta no puede modificarse.");
            if (!string.IsNullOrWhiteSpace(model.RowVersion))
            {
                byte[] versionRecibida;
                try { versionRecibida = Convert.FromBase64String(model.RowVersion); }
                catch { throw new InvalidOperationException("La versión de la parada no es válida. Recarga el viaje."); }
                if (!versionActual.SequenceEqual(versionRecibida)) throw new DBConcurrencyException("La parada fue modificada por otro usuario. Recarga el viaje.");
            }
            var tipoFinal = tipoActual == "Origen" || cierraViaje || ligadaEmbarque ? tipoActual : NormalizarTipoParada(model.TipoParada);
            if (string.IsNullOrWhiteSpace(tipoFinal)) throw new InvalidOperationException("Selecciona un tipo de parada válido.");
            var operacionFinal = ligadaEmbarque ? "Entrega PT" : model.TipoOperacion;
            var referenciaTipo = ligadaEmbarque ? referenciaTipoActual : model.ReferenciaTipo;
            var referenciaId = ligadaEmbarque ? referenciaIdActual : model.ReferenciaID;
            var referenciaFolio = ligadaEmbarque ? referenciaFolioActual : model.ReferenciaFolioSnapshot;
            var entidadTipo = ligadaEmbarque ? entidadTipoActual : model.EntidadTipo;
            var entidadId = ligadaEmbarque ? entidadIdActual : model.EntidadID;
            var entidadNombre = ligadaEmbarque ? entidadNombreActual : model.EntidadNombreSnapshot;
            const string sqlUpdate = @"
UPDATE dbo.Logistica_ViajeParadas SET TipoParada=@TipoParada,TipoOperacion=@TipoOperacion,EntidadTipo=@EntidadTipo,EntidadID=@EntidadID,EntidadNombreSnapshot=@EntidadNombre,
Lugar=@Lugar,Direccion=@Direccion,ReferenciaTipo=@ReferenciaTipo,ReferenciaID=@ReferenciaID,ReferenciaFolioSnapshot=@ReferenciaFolio,
FechaHoraLlegadaProgramada=@LlegadaProgramada,FechaHoraSalidaProgramada=@SalidaProgramada,RequiereEvidencia=@RequiereEvidencia,
ContactoNombre=@ContactoNombre,ContactoTelefono=@ContactoTelefono,Observaciones=@Observaciones,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
WHERE ViajeParadaID=@ParadaID AND ViajeID=@ViajeID AND Activo=1;";
            await using (var cmdUpdate = new SqlCommand(sqlUpdate, cn, tx))
            {
                cmdUpdate.Parameters.Add("@TipoParada", SqlDbType.NVarChar, 30).Value = tipoFinal;
                cmdUpdate.Parameters.Add("@TipoOperacion", SqlDbType.NVarChar, 80).Value = Db(operacionFinal);
                cmdUpdate.Parameters.Add("@EntidadTipo", SqlDbType.NVarChar, 30).Value = Db(entidadTipo);
                cmdUpdate.Parameters.Add("@EntidadID", SqlDbType.Int).Value = Db(entidadId);
                cmdUpdate.Parameters.Add("@EntidadNombre", SqlDbType.NVarChar, 200).Value = Db(entidadNombre);
                cmdUpdate.Parameters.Add("@Lugar", SqlDbType.NVarChar, 300).Value = model.Lugar;
                cmdUpdate.Parameters.Add("@Direccion", SqlDbType.NVarChar, 600).Value = Db(model.Direccion);
                cmdUpdate.Parameters.Add("@ReferenciaTipo", SqlDbType.NVarChar, 50).Value = Db(referenciaTipo);
                cmdUpdate.Parameters.Add("@ReferenciaID", SqlDbType.Int).Value = Db(referenciaId);
                cmdUpdate.Parameters.Add("@ReferenciaFolio", SqlDbType.NVarChar, 120).Value = Db(referenciaFolio);
                cmdUpdate.Parameters.Add("@LlegadaProgramada", SqlDbType.DateTime2).Value = Db(model.FechaHoraLlegadaProgramada);
                cmdUpdate.Parameters.Add("@SalidaProgramada", SqlDbType.DateTime2).Value = Db(model.FechaHoraSalidaProgramada);
                cmdUpdate.Parameters.Add("@RequiereEvidencia", SqlDbType.Bit).Value = ligadaEmbarque || model.RequiereEvidencia;
                cmdUpdate.Parameters.Add("@ContactoNombre", SqlDbType.NVarChar, 200).Value = Db(model.ContactoNombre);
                cmdUpdate.Parameters.Add("@ContactoTelefono", SqlDbType.NVarChar, 50).Value = Db(model.ContactoTelefono);
                cmdUpdate.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1200).Value = Db(model.Observaciones);
                cmdUpdate.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmdUpdate.Parameters.Add("@ParadaID", SqlDbType.Int).Value = model.ViajeParadaID;
                cmdUpdate.Parameters.Add("@ViajeID", SqlDbType.Int).Value = model.ViajeID;
                if (await cmdUpdate.ExecuteNonQueryAsync(cancellationToken) != 1) throw new InvalidOperationException("No fue posible modificar la parada.");
            }
            await ActualizarIndicadorMultiParadaAsync(cn, tx, model.ViajeID, cancellationToken);
            await InsertarHistorialParadaAsync(cn, tx, model.ViajeParadaID, "PARADA_EDITADA", estatusParada, estatusParada, $"Parada actualizada. Tipo: {tipoFinal}. Lugar: {model.Lugar}.", cancellationToken);
            await InsertarHistorialAsync(cn, tx, model.ViajeID, "PARADA_EDITADA", estatusViaje, estatusViaje, $"Se modificó la parada #{model.Secuencia}: {model.Lugar}.", cancellationToken);
            await tx.CommitAsync(cancellationToken);
            TempData["LogisticaOk"] = "Parada actualizada correctamente.";
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(cancellationToken); } catch { }
            TempData["LogisticaError"] = ex.Message;
        }
        return RedirectToAction(nameof(Detalle), new { id = model.ViajeID });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MoverParada(int viajeId, int viajeParadaId, string direccion, CancellationToken cancellationToken = default)
    {
        var acceso = await ValidarAccesoAsync();
        if (acceso != null) return acceso;
        direccion = direccion?.Trim().ToLowerInvariant() ?? string.Empty;
        if (viajeId <= 0 || viajeParadaId <= 0 || direccion is not "subir" and not "bajar") { TempData["LogisticaError"] = "El movimiento de la parada no es válido."; return viajeId > 0 ? RedirectToAction(nameof(Detalle), new { id = viajeId }) : RedirectToAction(nameof(Index)); }
        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            int secuenciaActual;
            string tipoActual, estatusParada, estatusViaje;
            bool cierraActual;
            const string sqlActual = @"SELECT p.Secuencia,ISNULL(p.TipoParada,N'') TipoParada,ISNULL(p.Estatus,N'') EstatusParada,ISNULL(p.CierraViaje,0) CierraViaje,ISNULL(v.Estatus,N'') EstatusViaje FROM dbo.Logistica_ViajeParadas p WITH(UPDLOCK,HOLDLOCK) INNER JOIN dbo.Logistica_Viajes v WITH(UPDLOCK,HOLDLOCK) ON v.ViajeID=p.ViajeID WHERE p.ViajeParadaID=@ParadaID AND p.ViajeID=@ViajeID AND p.Activo=1;";
            await using (var cmdActual = new SqlCommand(sqlActual, cn, tx))
            {
                cmdActual.Parameters.Add("@ParadaID", SqlDbType.Int).Value = viajeParadaId;
                cmdActual.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
                await using var rd = await cmdActual.ExecuteReaderAsync(cancellationToken);
                if (!await rd.ReadAsync(cancellationToken)) throw new InvalidOperationException("La parada no existe.");
                secuenciaActual = Entero(rd, "Secuencia");
                tipoActual = Texto(rd, "TipoParada");
                estatusParada = Texto(rd, "EstatusParada");
                cierraActual = Booleano(rd, "CierraViaje");
                estatusViaje = Texto(rd, "EstatusViaje");
            }
            if (estatusViaje is not "Programado" and not "En curso") throw new InvalidOperationException($"El viaje está en estatus {estatusViaje} y ya no permite reordenar paradas.");
            if (tipoActual == "Origen") throw new InvalidOperationException("La parada Origen siempre debe permanecer al inicio.");
            if (cierraActual) throw new InvalidOperationException("La parada que cierra el viaje siempre debe permanecer al final.");
            if (estatusParada is "Completada" or "Omitida") throw new InvalidOperationException("Una parada ya atendida no puede reordenarse.");
            const string sqlVecinaSubir = @"SELECT TOP(1) ViajeParadaID,Secuencia,ISNULL(TipoParada,N'') TipoParada,ISNULL(Estatus,N'') Estatus,ISNULL(CierraViaje,0) CierraViaje FROM dbo.Logistica_ViajeParadas WITH(UPDLOCK,HOLDLOCK) WHERE ViajeID=@ViajeID AND Activo=1 AND Secuencia<@Secuencia ORDER BY Secuencia DESC,ViajeParadaID DESC;";
            const string sqlVecinaBajar = @"SELECT TOP(1) ViajeParadaID,Secuencia,ISNULL(TipoParada,N'') TipoParada,ISNULL(Estatus,N'') Estatus,ISNULL(CierraViaje,0) CierraViaje FROM dbo.Logistica_ViajeParadas WITH(UPDLOCK,HOLDLOCK) WHERE ViajeID=@ViajeID AND Activo=1 AND Secuencia>@Secuencia ORDER BY Secuencia,ViajeParadaID;";
            int paradaVecinaId, secuenciaVecina;
            string tipoVecina, estatusVecina;
            bool cierraVecina;
            await using (var cmdVecina = new SqlCommand(direccion == "subir" ? sqlVecinaSubir : sqlVecinaBajar, cn, tx))
            {
                cmdVecina.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
                cmdVecina.Parameters.Add("@Secuencia", SqlDbType.Int).Value = secuenciaActual;
                await using var rd = await cmdVecina.ExecuteReaderAsync(cancellationToken);
                if (!await rd.ReadAsync(cancellationToken)) throw new InvalidOperationException("La parada ya se encuentra en el límite permitido.");
                paradaVecinaId = Entero(rd, "ViajeParadaID");
                secuenciaVecina = Entero(rd, "Secuencia");
                tipoVecina = Texto(rd, "TipoParada");
                estatusVecina = Texto(rd, "Estatus");
                cierraVecina = Booleano(rd, "CierraViaje");
            }
            if (tipoVecina == "Origen") throw new InvalidOperationException("No puedes mover una parada antes del Origen.");
            if (cierraVecina) throw new InvalidOperationException("No puedes mover una parada después del Retorno final.");
            if (estatusViaje == "En curso" && estatusVecina is "Completada" or "Omitida") throw new InvalidOperationException("No puedes reordenar una parada antes de otra que ya fue atendida.");
            const string sqlMax = @"SELECT ISNULL(MAX(Secuencia),0)+10000 FROM dbo.Logistica_ViajeParadas WITH(UPDLOCK,HOLDLOCK) WHERE ViajeID=@ViajeID;";
            int temporal;
            await using (var cmdMax = new SqlCommand(sqlMax, cn, tx))
            {
                cmdMax.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
                temporal = Convert.ToInt32(await cmdMax.ExecuteScalarAsync(cancellationToken));
            }
            const string sqlSwap = @"
UPDATE dbo.Logistica_ViajeParadas SET Secuencia=@Temporal,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario WHERE ViajeParadaID=@Actual AND ViajeID=@ViajeID;
UPDATE dbo.Logistica_ViajeParadas SET Secuencia=@SecuenciaActual,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario WHERE ViajeParadaID=@Vecina AND ViajeID=@ViajeID;
UPDATE dbo.Logistica_ViajeParadas SET Secuencia=@SecuenciaVecina,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario WHERE ViajeParadaID=@Actual AND ViajeID=@ViajeID;";
            await using (var cmdSwap = new SqlCommand(sqlSwap, cn, tx))
            {
                cmdSwap.Parameters.Add("@Temporal", SqlDbType.Int).Value = temporal;
                cmdSwap.Parameters.Add("@SecuenciaActual", SqlDbType.Int).Value = secuenciaActual;
                cmdSwap.Parameters.Add("@SecuenciaVecina", SqlDbType.Int).Value = secuenciaVecina;
                cmdSwap.Parameters.Add("@Actual", SqlDbType.Int).Value = viajeParadaId;
                cmdSwap.Parameters.Add("@Vecina", SqlDbType.Int).Value = paradaVecinaId;
                cmdSwap.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
                cmdSwap.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                await cmdSwap.ExecuteNonQueryAsync(cancellationToken);
            }
            await ActualizarOrdenEntregaViajeAsync(cn, tx, viajeId, cancellationToken);
            await InsertarHistorialParadaAsync(cn, tx, viajeParadaId, "PARADA_REORDENADA", estatusParada, estatusParada, $"Secuencia {secuenciaActual} → {secuenciaVecina}.", cancellationToken);
            await InsertarHistorialAsync(cn, tx, viajeId, "RUTA_REORDENADA", estatusViaje, estatusViaje, $"Parada {viajeParadaId} movida de posición {secuenciaActual} a {secuenciaVecina}.", cancellationToken);
            await tx.CommitAsync(cancellationToken);
            TempData["LogisticaOk"] = "Ruta reordenada correctamente.";
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(cancellationToken); } catch { }
            TempData["LogisticaError"] = ex.Message;
        }
        return RedirectToAction(nameof(Detalle), new { id = viajeId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EliminarParada(int viajeId, int viajeParadaId, CancellationToken cancellationToken = default)
    {
        var acceso = await ValidarAccesoAsync();
        if (acceso != null) return acceso;
        if (viajeId <= 0 || viajeParadaId <= 0) { TempData["LogisticaError"] = "La parada indicada no es válida."; return viajeId > 0 ? RedirectToAction(nameof(Detalle), new { id = viajeId }) : RedirectToAction(nameof(Index)); }
        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            string estatusViaje, tipoParada, lugar;
            bool cierraViaje;
            const string sqlActual = @"
SELECT ISNULL(v.Estatus,N'') EstatusViaje,ISNULL(p.TipoParada,N'') TipoParada,ISNULL(p.Lugar,N'') Lugar,ISNULL(p.CierraViaje,0) CierraViaje,
CASE WHEN EXISTS(SELECT 1 FROM dbo.Logistica_ViajeEmbarques ve WHERE ve.ViajeParadaID=p.ViajeParadaID AND ve.Activo=1) THEN 1 ELSE 0 END TieneEmbarque
FROM dbo.Logistica_ViajeParadas p WITH(UPDLOCK,HOLDLOCK)
INNER JOIN dbo.Logistica_Viajes v WITH(UPDLOCK,HOLDLOCK) ON v.ViajeID=p.ViajeID
WHERE p.ViajeParadaID=@ParadaID AND p.ViajeID=@ViajeID AND p.Activo=1;";
            bool tieneEmbarque;
            await using (var cmdActual = new SqlCommand(sqlActual, cn, tx))
            {
                cmdActual.Parameters.Add("@ParadaID", SqlDbType.Int).Value = viajeParadaId;
                cmdActual.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
                await using var rd = await cmdActual.ExecuteReaderAsync(cancellationToken);
                if (!await rd.ReadAsync(cancellationToken)) throw new InvalidOperationException("La parada no existe.");
                estatusViaje = Texto(rd, "EstatusViaje");
                tipoParada = Texto(rd, "TipoParada");
                lugar = Texto(rd, "Lugar");
                cierraViaje = Booleano(rd, "CierraViaje");
                tieneEmbarque = Booleano(rd, "TieneEmbarque");
            }
            if (estatusViaje != "Programado") throw new InvalidOperationException("Una parada solo puede eliminarse antes de iniciar el viaje. Durante el recorrido utiliza Omitir parada.");
            if (tipoParada == "Origen") throw new InvalidOperationException("No puedes eliminar el Origen del viaje.");
            if (cierraViaje) throw new InvalidOperationException("No puedes eliminar la parada que cierra el viaje.");
            if (tieneEmbarque) throw new InvalidOperationException("Esta parada corresponde a un embarque. Primero debes retirar o reasignar ese embarque desde su flujo.");
            const string sqlEliminar = @"UPDATE dbo.Logistica_ViajeParadas SET Activo=0,Secuencia=Secuencia+1000000+ViajeParadaID,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario WHERE ViajeParadaID=@ParadaID AND ViajeID=@ViajeID AND Activo=1;";
            await using (var cmdEliminar = new SqlCommand(sqlEliminar, cn, tx))
            {
                cmdEliminar.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmdEliminar.Parameters.Add("@ParadaID", SqlDbType.Int).Value = viajeParadaId;
                cmdEliminar.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
                if (await cmdEliminar.ExecuteNonQueryAsync(cancellationToken) != 1) throw new InvalidOperationException("No fue posible retirar la parada.");
            }
            await ReordenarSecuenciasParadasAsync(cn, tx, viajeId, cancellationToken);
            await ActualizarOrdenEntregaViajeAsync(cn, tx, viajeId, cancellationToken);
            await ActualizarIndicadorMultiParadaAsync(cn, tx, viajeId, cancellationToken);
            await InsertarHistorialParadaAsync(cn, tx, viajeParadaId, "PARADA_ELIMINADA", "Pendiente", "Eliminada", $"Parada retirada antes de iniciar. Lugar: {lugar}.", cancellationToken);
            await InsertarHistorialAsync(cn, tx, viajeId, "PARADA_ELIMINADA", "Programado", "Programado", $"Se retiró la parada {tipoParada} - {lugar}.", cancellationToken);
            await tx.CommitAsync(cancellationToken);
            TempData["LogisticaOk"] = "Parada retirada correctamente.";
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(cancellationToken); } catch { }
            TempData["LogisticaError"] = ex.Message;
        }
        return RedirectToAction(nameof(Detalle), new { id = viajeId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> OmitirParada(int viajeId, int viajeParadaId, string? motivo, CancellationToken cancellationToken = default)
    {
        var acceso = await ValidarAccesoAsync();
        if (acceso != null) return acceso;
        motivo = motivo?.Trim();
        if (viajeId <= 0 || viajeParadaId <= 0 || string.IsNullOrWhiteSpace(motivo)) { TempData["LogisticaError"] = "La parada y el motivo son obligatorios."; return viajeId > 0 ? RedirectToAction(nameof(Detalle), new { id = viajeId }) : RedirectToAction(nameof(Index)); }
        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            string estatusViaje, estatusParada, tipoParada, lugar;
            bool cierraViaje, tieneEmbarque;
            const string sqlActual = @"
SELECT ISNULL(v.Estatus,N'') EstatusViaje,ISNULL(p.Estatus,N'') EstatusParada,ISNULL(p.TipoParada,N'') TipoParada,ISNULL(p.Lugar,N'') Lugar,ISNULL(p.CierraViaje,0) CierraViaje,
CASE WHEN EXISTS(SELECT 1 FROM dbo.Logistica_ViajeEmbarques ve INNER JOIN dbo.Logistica_Embarques e ON e.EmbarqueID=ve.EmbarqueID AND e.Activo=1 WHERE ve.ViajeParadaID=p.ViajeParadaID AND ve.Activo=1 AND e.Estatus<>N'Entregado') THEN 1 ELSE 0 END TieneEmbarque
FROM dbo.Logistica_ViajeParadas p WITH(UPDLOCK,HOLDLOCK)
INNER JOIN dbo.Logistica_Viajes v WITH(UPDLOCK,HOLDLOCK) ON v.ViajeID=p.ViajeID
WHERE p.ViajeParadaID=@ParadaID AND p.ViajeID=@ViajeID AND p.Activo=1;";
            await using (var cmdActual = new SqlCommand(sqlActual, cn, tx))
            {
                cmdActual.Parameters.Add("@ParadaID", SqlDbType.Int).Value = viajeParadaId;
                cmdActual.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
                await using var rd = await cmdActual.ExecuteReaderAsync(cancellationToken);
                if (!await rd.ReadAsync(cancellationToken)) throw new InvalidOperationException("La parada no existe.");
                estatusViaje = Texto(rd, "EstatusViaje");
                estatusParada = Texto(rd, "EstatusParada");
                tipoParada = Texto(rd, "TipoParada");
                lugar = Texto(rd, "Lugar");
                cierraViaje = Booleano(rd, "CierraViaje");
                tieneEmbarque = Booleano(rd, "TieneEmbarque");
            }
            if (estatusViaje != "En curso") throw new InvalidOperationException("Solo se pueden omitir paradas mientras el viaje está En curso.");
            if (estatusParada is "Completada" or "Omitida") throw new InvalidOperationException("La parada ya fue resuelta.");
            if (tipoParada == "Origen" || cierraViaje) throw new InvalidOperationException("No puedes omitir el Origen ni el Retorno final.");
            if (tieneEmbarque) throw new InvalidOperationException("La parada tiene un embarque pendiente de entrega y no puede omitirse directamente.");
            const string sqlOmitir = @"
UPDATE dbo.Logistica_ViajeParadas SET Estatus=N'Omitida',FechaSalidaReal=COALESCE(FechaSalidaReal,SYSDATETIME()),
Observaciones=CASE WHEN NULLIF(LTRIM(RTRIM(ISNULL(Observaciones,N''))),N'') IS NULL THEN @Motivo ELSE CONCAT(Observaciones,NCHAR(13),NCHAR(10),N'Omitida: ',@Motivo) END,
FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
WHERE ViajeParadaID=@ParadaID AND ViajeID=@ViajeID AND Activo=1 AND Estatus NOT IN(N'Completada',N'Omitida');";
            await using (var cmdOmitir = new SqlCommand(sqlOmitir, cn, tx))
            {
                cmdOmitir.Parameters.Add("@Motivo", SqlDbType.NVarChar, 1000).Value = motivo;
                cmdOmitir.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmdOmitir.Parameters.Add("@ParadaID", SqlDbType.Int).Value = viajeParadaId;
                cmdOmitir.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
                if (await cmdOmitir.ExecuteNonQueryAsync(cancellationToken) != 1) throw new InvalidOperationException("No fue posible omitir la parada.");
            }
            await InsertarHistorialParadaAsync(cn, tx, viajeParadaId, "PARADA_OMITIDA", estatusParada, "Omitida", $"Motivo: {motivo}", cancellationToken);
            await InsertarHistorialAsync(cn, tx, viajeId, "PARADA_OMITIDA", "En curso", "En curso", $"Se omitió {tipoParada} - {lugar}. Motivo: {motivo}", cancellationToken);
            await tx.CommitAsync(cancellationToken);
            TempData["LogisticaOk"] = "Parada omitida. El viaje puede continuar con la siguiente.";
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(cancellationToken); } catch { }
            TempData["LogisticaError"] = ex.Message;
        }
        return RedirectToAction(nameof(Detalle), new { id = viajeId });
    }

    private static async Task CargarParadasEdicionAsync(SqlConnection cn, LogisticaViajeEditarVm vm, CancellationToken cancellationToken)
    {
        vm.Paradas.Clear();
        const string sql = @"
SELECT p.ViajeParadaID,p.ViajeID,p.Secuencia,ISNULL(p.TipoParada,N'') TipoParada,ISNULL(p.TipoOperacion,N'') TipoOperacion,
ISNULL(p.EntidadTipo,N'') EntidadTipo,p.EntidadID,ISNULL(p.EntidadNombreSnapshot,N'') EntidadNombreSnapshot,
ISNULL(p.Lugar,N'') Lugar,ISNULL(p.Direccion,N'') Direccion,ISNULL(p.ReferenciaTipo,N'') ReferenciaTipo,p.ReferenciaID,
ISNULL(p.ReferenciaFolioSnapshot,N'') ReferenciaFolioSnapshot,p.FechaHoraLlegadaProgramada,p.FechaHoraSalidaProgramada,
ISNULL(p.RequiereEvidencia,0) RequiereEvidencia,ISNULL(p.CierraViaje,0) CierraViaje,ISNULL(p.ContactoNombre,N'') ContactoNombre,
ISNULL(p.ContactoTelefono,N'') ContactoTelefono,ISNULL(p.Observaciones,N'') Observaciones,CONVERT(varbinary(8),p.RowVersion) RowVersion,
rel.EmbarqueID
FROM dbo.Logistica_ViajeParadas p
OUTER APPLY
(
    SELECT TOP(1) ve.EmbarqueID
    FROM dbo.Logistica_ViajeEmbarques ve
    WHERE ve.ViajeParadaID=p.ViajeParadaID AND ve.ViajeID=p.ViajeID AND ve.Activo=1
    ORDER BY ve.ViajeEmbarqueID
) rel
WHERE p.ViajeID=@ViajeID AND p.Activo=1
ORDER BY p.Secuencia,p.ViajeParadaID;";
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = vm.ViajeID;
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await rd.ReadAsync(cancellationToken))
        {
            vm.Paradas.Add(new LogisticaViajeParadaCapturaVm
            {
                ViajeID = vm.ViajeID,
                ViajeParadaID = Entero(rd, "ViajeParadaID"),
                Secuencia = Entero(rd, "Secuencia"),
                TipoParada = Texto(rd, "TipoParada"),
                TipoOperacion = TextoNullable(rd, "TipoOperacion"),
                EntidadTipo = TextoNullable(rd, "EntidadTipo"),
                EntidadID = EnteroNullable(rd, "EntidadID"),
                EntidadNombreSnapshot = TextoNullable(rd, "EntidadNombreSnapshot"),
                Lugar = Texto(rd, "Lugar"),
                Direccion = TextoNullable(rd, "Direccion"),
                ReferenciaTipo = TextoNullable(rd, "ReferenciaTipo"),
                ReferenciaID = EnteroNullable(rd, "ReferenciaID"),
                ReferenciaFolioSnapshot = TextoNullable(rd, "ReferenciaFolioSnapshot"),
                FechaHoraLlegadaProgramada = Fecha(rd, "FechaHoraLlegadaProgramada"),
                FechaHoraSalidaProgramada = Fecha(rd, "FechaHoraSalidaProgramada"),
                RequiereEvidencia = Booleano(rd, "RequiereEvidencia"),
                CierraViaje = Booleano(rd, "CierraViaje"),
                ContactoNombre = TextoNullable(rd, "ContactoNombre"),
                ContactoTelefono = TextoNullable(rd, "ContactoTelefono"),
                Observaciones = TextoNullable(rd, "Observaciones"),
                EmbarqueID = EnteroNullable(rd, "EmbarqueID"),
                RowVersion = Convert.ToBase64String((byte[])rd["RowVersion"])
            });
        }
    }

    private static async Task<(int EmbarqueID, string Folio, string Estatus)?> ObtenerEmbarqueActivoVinculadoAsync(SqlConnection cn, SqlTransaction tx, int viajeId, CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT TOP(1)e.EmbarqueID,ISNULL(e.Folio,N'') Folio,ISNULL(e.Estatus,N'') Estatus
FROM dbo.Logistica_ViajeEmbarques ve WITH(UPDLOCK,HOLDLOCK)
INNER JOIN dbo.Logistica_Embarques e WITH(UPDLOCK,HOLDLOCK) ON e.EmbarqueID=ve.EmbarqueID
WHERE ve.ViajeID=@ViajeID AND ve.Activo=1 AND e.Activo=1 AND e.Estatus<>N'Cancelado'
ORDER BY ISNULL(ve.OrdenEntrega,2147483647),ve.ViajeEmbarqueID;";
        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await rd.ReadAsync(cancellationToken)) return null;
        return (Entero(rd, "EmbarqueID"), Texto(rd, "Folio"), Texto(rd, "Estatus"));
    }

    private static async Task<bool> ViajeTieneParadasActivasAsync(SqlConnection cn, SqlTransaction tx, int viajeId, CancellationToken cancellationToken)
    {
        const string sql = @"SELECT COUNT_BIG(*) FROM dbo.Logistica_ViajeParadas WITH(UPDLOCK,HOLDLOCK) WHERE ViajeID=@ViajeID AND Activo=1;";
        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private void Normalizar(LogisticaViajeCrearVm model)
    {
        model.TipoViaje = NormalizarTipoViaje(model.TipoViaje, false);
        model.Origen = model.Origen?.Trim() ?? string.Empty;
        model.Destino = model.Destino?.Trim() ?? string.Empty;
        model.Motivo = model.Motivo?.Trim() ?? string.Empty;
        model.OperadorTexto = model.OperadorTexto?.Trim();
        model.Observaciones = model.Observaciones?.Trim();
    }
    private void Normalizar(LogisticaViajeEditarVm model)
    {
        model.TipoViaje = NormalizarTipoViaje(model.TipoViaje, false);
        model.Origen = model.Origen?.Trim() ?? string.Empty;
        model.Destino = model.Destino?.Trim() ?? string.Empty;
        model.Motivo = model.Motivo?.Trim() ?? string.Empty;
        model.OperadorTexto = model.OperadorTexto?.Trim();
        model.Observaciones = model.Observaciones?.Trim();
    }
    private void ValidarModelo(LogisticaViajeCrearVm model)
    {
        if (string.IsNullOrWhiteSpace(model.TipoViaje)) ModelState.AddModelError(nameof(model.TipoViaje), "Selecciona un tipo de viaje válido.");
        if (string.IsNullOrWhiteSpace(model.Origen)) ModelState.AddModelError(nameof(model.Origen), "El origen es obligatorio.");
        if (string.IsNullOrWhiteSpace(model.Destino)) ModelState.AddModelError(nameof(model.Destino), "El destino es obligatorio.");
        if (string.IsNullOrWhiteSpace(model.Motivo)) ModelState.AddModelError(nameof(model.Motivo), "El motivo del viaje es obligatorio.");
        if (model.FechaProgramada == default) ModelState.AddModelError(nameof(model.FechaProgramada), "La fecha programada es obligatoria.");
    }

    private void ValidarModelo(LogisticaViajeEditarVm model)
    {
        if (model.ViajeID <= 0) ModelState.AddModelError(nameof(model.ViajeID), "El viaje no es válido.");
        if (string.IsNullOrWhiteSpace(model.TipoViaje)) ModelState.AddModelError(nameof(model.TipoViaje), "Selecciona un tipo de viaje válido.");
        if (string.IsNullOrWhiteSpace(model.Origen)) ModelState.AddModelError(nameof(model.Origen), "El origen es obligatorio.");
        if (string.IsNullOrWhiteSpace(model.Destino)) ModelState.AddModelError(nameof(model.Destino), "El destino es obligatorio.");
        if (string.IsNullOrWhiteSpace(model.Motivo)) ModelState.AddModelError(nameof(model.Motivo), "El motivo del viaje es obligatorio.");
        if (model.FechaProgramada == default) ModelState.AddModelError(nameof(model.FechaProgramada), "La fecha programada es obligatoria.");
    }

    private static async Task<string> ObtenerNombreOperadorInternoAsync(SqlConnection cn, SqlTransaction tx, int operadorUsuarioId, CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT TOP(1)
LTRIM(RTRIM(CONCAT(ISNULL(P.Nombre,N''),N' ',ISNULL(P.ApellidoPaterno,N''),N' ',ISNULL(P.ApellidoMaterno,N'')))) AS NombreCompleto
FROM dbo.Usuarios U
INNER JOIN dbo.Persona P ON P.PersonaID=U.PersonaID
INNER JOIN dbo.Departamentos D ON D.DepartamentoID=U.DepartamentoID
WHERE U.UsuarioID=@UsuarioID
AND U.Activo=1
AND D.Activo=1
AND UPPER(REPLACE(LTRIM(RTRIM(ISNULL(D.NombreDepartamento,N''))),N'Í',N'I'))=N'LOGISTICA'
AND UPPER(LTRIM(RTRIM(ISNULL(P.Puesto,N'')))) LIKE N'%CHOFER%';";
        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = operadorUsuarioId;
        var valor = await cmd.ExecuteScalarAsync(cancellationToken);
        var nombre = valor == null || valor == DBNull.Value ? string.Empty : valor.ToString()?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(nombre)) throw new InvalidOperationException("El operador seleccionado debe ser un usuario activo del departamento de Logística y tener un puesto de Chofer.");
        return nombre;

    }


    private static async Task ValidarViajeListoParaIniciarAsync(SqlConnection cn, SqlTransaction tx, int viajeId, CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT TipoViaje,Origen,Destino,Motivo,FechaProgramada,HoraSalidaProgramada,RutaID,UnidadID,OperadorUsuarioID,Estatus
FROM dbo.Logistica_Viajes WITH(UPDLOCK,HOLDLOCK)
WHERE ViajeID=@ViajeID
AND Activo=1;";

        string estatus, tipoViaje, origen, destino, motivo;
        DateTime? fechaProgramada;
        TimeSpan? horaSalida;
        int? rutaId, unidadId, operadorUsuarioId;

        await using (var cmd = new SqlCommand(sql, cn, tx))
        {
            cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);

            if (!await rd.ReadAsync(cancellationToken)) throw new InvalidOperationException("El viaje no existe.");

            estatus = Texto(rd, "Estatus");
            tipoViaje = Texto(rd, "TipoViaje");
            origen = Texto(rd, "Origen");
            destino = Texto(rd, "Destino");
            motivo = Texto(rd, "Motivo");
            fechaProgramada = Fecha(rd, "FechaProgramada");
            horaSalida = Hora(rd, "HoraSalidaProgramada");
            rutaId = EnteroNullable(rd, "RutaID");
            unidadId = EnteroNullable(rd, "UnidadID");
            operadorUsuarioId = EnteroNullable(rd, "OperadorUsuarioID");
        }

        if (estatus != "Programado") throw new InvalidOperationException("Solo un viaje Programado puede iniciar.");

        var faltantes = new List<string>();

        if (string.IsNullOrWhiteSpace(tipoViaje)) faltantes.Add("tipo de salida");
        if (string.IsNullOrWhiteSpace(origen)) faltantes.Add("origen");
        if (string.IsNullOrWhiteSpace(destino)) faltantes.Add("destino");
        if (string.IsNullOrWhiteSpace(motivo)) faltantes.Add("motivo");
        if (!fechaProgramada.HasValue) faltantes.Add("fecha programada");
        if (!horaSalida.HasValue) faltantes.Add("hora de salida");
        if (!rutaId.HasValue || rutaId.Value <= 0) faltantes.Add("ruta");
        if (!unidadId.HasValue || unidadId.Value <= 0) faltantes.Add("unidad");
        if (!operadorUsuarioId.HasValue || operadorUsuarioId.Value <= 0) faltantes.Add("chofer");

        if (faltantes.Count > 0)
            throw new InvalidOperationException($"El viaje todavía no está listo para iniciar. Completa: {string.Join(", ", faltantes.Distinct())}.");

        const string sqlRuta = "SELECT COUNT_BIG(*) FROM dbo.Logistica_Rutas WHERE RutaID=@RutaID AND Activo=1;";

        await using (var cmd = new SqlCommand(sqlRuta, cn, tx))
        {
            cmd.Parameters.Add("@RutaID", SqlDbType.Int).Value = rutaId!.Value;

            if (Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken)) <= 0)
                throw new InvalidOperationException("La ruta asignada ya no está activa.");
        }

        const string sqlUnidad = "SELECT COUNT_BIG(*) FROM dbo.Logistica_Unidades WHERE UnidadID=@UnidadID AND Activo=1;";

        await using (var cmd = new SqlCommand(sqlUnidad, cn, tx))
        {
            cmd.Parameters.Add("@UnidadID", SqlDbType.Int).Value = unidadId!.Value;

            if (Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken)) <= 0)
                throw new InvalidOperationException("La unidad asignada ya no está activa.");
        }

        const string sqlOperador = @"
SELECT COUNT_BIG(*)
FROM dbo.Usuarios U
INNER JOIN dbo.Persona P ON P.PersonaID=U.PersonaID
INNER JOIN dbo.Departamentos D ON D.DepartamentoID=U.DepartamentoID
WHERE U.UsuarioID=@UsuarioID
AND U.Activo=1
AND D.Activo=1
AND UPPER(REPLACE(LTRIM(RTRIM(ISNULL(D.NombreDepartamento,N''))),N'Í',N'I'))=N'LOGISTICA'
AND UPPER(LTRIM(RTRIM(ISNULL(P.Puesto,N'')))) LIKE N'%CHOFER%';";

        await using (var cmd = new SqlCommand(sqlOperador, cn, tx))
        {
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = operadorUsuarioId!.Value;

            if (Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken)) <= 0)
                throw new InvalidOperationException("El chofer asignado ya no es un usuario activo de Logística con puesto de Chofer.");
        }

        const string sqlOcupado = @"
SELECT TOP(1)
    ISNULL(Folio,N'') Folio,
    CASE WHEN OperadorUsuarioID=@ChoferID THEN N'CHOFER' ELSE N'UNIDAD' END Recurso
FROM dbo.Logistica_Viajes WITH(UPDLOCK,HOLDLOCK)
WHERE Activo=1
AND ViajeID<>@ViajeID
AND Estatus=N'En curso'
AND FechaRegresoReal IS NULL
AND
(
    OperadorUsuarioID=@ChoferID
    OR UnidadID=@UnidadID
)
ORDER BY FechaSalidaReal;";

        await using (var cmd = new SqlCommand(sqlOcupado, cn, tx))
        {
            cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
            cmd.Parameters.Add("@ChoferID", SqlDbType.Int).Value = operadorUsuarioId.Value;
            cmd.Parameters.Add("@UnidadID", SqlDbType.Int).Value = unidadId.Value;

            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);

            if (await rd.ReadAsync(cancellationToken))
            {
                var folio = Texto(rd, "Folio");
                var recurso = Texto(rd, "Recurso");

                if (recurso == "CHOFER")
                    throw new InvalidOperationException($"El chofer no puede iniciar este viaje porque todavía está en el viaje {folio}.");

                throw new InvalidOperationException($"La unidad no puede salir porque todavía está ocupada por el viaje {folio}.");
            }
        }
    }
    private static string NormalizarTipoViaje(string? valor, bool permitirVacio)
    {
        valor = valor?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(valor)) return string.Empty;
        var permitidos = new[]
        {
        "Entrega PT",
        "Ruta multipropósito",
        "Ruta de personal",
        "Recolección de MP",
        "Traslado entre plantas",
        "Recolección de material",
        "Entrega / recolección especial",
        "Otro"
    };
        return permitidos.FirstOrDefault(x => string.Equals(x, valor, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
    }


    private static async Task ValidarDisponibilidadViajeAsync(SqlConnection cn, SqlTransaction tx, DateTime fecha, TimeSpan? horaSalida, int? unidadId, int? operadorUsuarioId, int? viajeExcluir, CancellationToken cancellationToken)
    {
        if (!horaSalida.HasValue || !unidadId.HasValue || unidadId.Value <= 0 || !operadorUsuarioId.HasValue || operadorUsuarioId.Value <= 0) return;

        const string sqlEnCurso = @"
SELECT TOP(1)
    ISNULL(Folio,N'') Folio,
    CASE WHEN OperadorUsuarioID=@ChoferID THEN N'CHOFER' ELSE N'UNIDAD' END Recurso
FROM dbo.Logistica_Viajes WITH(UPDLOCK,HOLDLOCK)
WHERE Activo=1
AND Estatus=N'En curso'
AND FechaRegresoReal IS NULL
AND (@ViajeExcluir IS NULL OR ViajeID<>@ViajeExcluir)
AND
(
    OperadorUsuarioID=@ChoferID
    OR UnidadID=@UnidadID
)
ORDER BY FechaSalidaReal;";

        await using (var cmd = new SqlCommand(sqlEnCurso, cn, tx))
        {
            cmd.Parameters.Add("@ChoferID", SqlDbType.Int).Value = operadorUsuarioId.Value;
            cmd.Parameters.Add("@UnidadID", SqlDbType.Int).Value = unidadId.Value;
            cmd.Parameters.Add("@ViajeExcluir", SqlDbType.Int).Value = Db(viajeExcluir);

            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);

            if (await rd.ReadAsync(cancellationToken))
            {
                var folio = Texto(rd, "Folio");
                var recurso = Texto(rd, "Recurso");

                if (recurso == "CHOFER") throw new InvalidOperationException($"El chofer seleccionado todavía está realizando el viaje {folio}.");
                throw new InvalidOperationException($"La unidad seleccionada todavía está realizando el viaje {folio}.");
            }
        }

        const string sqlProgramado = @"
SELECT TOP(1)
    ISNULL(Folio,N'') Folio,
    CASE WHEN OperadorUsuarioID=@ChoferID THEN N'CHOFER' ELSE N'UNIDAD' END Recurso
FROM dbo.Logistica_Viajes WITH(UPDLOCK,HOLDLOCK)
WHERE Activo=1
AND Estatus=N'Programado'
AND (@ViajeExcluir IS NULL OR ViajeID<>@ViajeExcluir)
AND FechaProgramada=@Fecha
AND HoraSalidaProgramada=@Hora
AND
(
    OperadorUsuarioID=@ChoferID
    OR UnidadID=@UnidadID
);";

        await using (var cmd = new SqlCommand(sqlProgramado, cn, tx))
        {
            cmd.Parameters.Add("@ChoferID", SqlDbType.Int).Value = operadorUsuarioId.Value;
            cmd.Parameters.Add("@UnidadID", SqlDbType.Int).Value = unidadId.Value;
            cmd.Parameters.Add("@ViajeExcluir", SqlDbType.Int).Value = Db(viajeExcluir);
            cmd.Parameters.Add("@Fecha", SqlDbType.Date).Value = fecha.Date;
            cmd.Parameters.Add("@Hora", SqlDbType.Time).Value = horaSalida.Value;

            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);

            if (await rd.ReadAsync(cancellationToken))
            {
                var folio = Texto(rd, "Folio");
                var recurso = Texto(rd, "Recurso");

                if (recurso == "CHOFER") throw new InvalidOperationException($"El chofer ya tiene el viaje {folio} programado para {fecha:dd/MM/yyyy} a las {horaSalida.Value:hh\\:mm}.");
                throw new InvalidOperationException($"La unidad ya tiene el viaje {folio} programado para {fecha:dd/MM/yyyy} a las {horaSalida.Value:hh\\:mm}.");
            }
        }
    }
    private static string? NormalizarEstatusFiltro(string? valor)
    {
        valor = valor?.Trim();

        if (string.IsNullOrWhiteSpace(valor))
            return null;

        var permitidos = new[] { "Programado", "En curso", "Completado", "Cancelado" };

        return permitidos.FirstOrDefault(x =>
            string.Equals(x, valor, StringComparison.OrdinalIgnoreCase));
    }

    private string ObtenerErroresModelState()
    {
        var errores = ModelState.Values
            .SelectMany(x => x.Errors)
            .Select(x => x.ErrorMessage)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToList();

        return errores.Count == 0
            ? "Revisa los datos capturados."
            : string.Join(" ", errores);
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

    private static string? TextoNullable(SqlDataReader rd, string columna)
    {
        var i = rd.GetOrdinal(columna);
        return rd.IsDBNull(i) ? null : rd.GetValue(i)?.ToString();
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

        if (value is TimeSpan ts)
            return ts;

        return TimeSpan.TryParse(value?.ToString(), out var parsed)
            ? parsed
            : null;
    }

    private static bool Booleano(SqlDataReader rd, string columna)
    {
        var i = rd.GetOrdinal(columna);
        return !rd.IsDBNull(i) && Convert.ToBoolean(rd.GetValue(i));
    }
}
