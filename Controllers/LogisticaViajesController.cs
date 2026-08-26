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
        if (!UsuarioID.HasValue || UsuarioID.Value <= 0)
            return RedirectToAction("Login", "Login");

        if (!await _acceso.TienePermisoAsync(UsuarioID.Value, "Viajes"))
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
    public async Task<IActionResult> Index(
        string? q = null,
        string? estatus = null,
        string? tipoViaje = null,
        string? tipoTransporte = null,
        DateTime? fechaDesde = null,
        DateTime? fechaHasta = null,
        int pagina = 1,
        CancellationToken cancellationToken = default)
    {


        q = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
        estatus = NormalizarEstatusFiltro(estatus);
        tipoViaje = NormalizarTipoViaje(tipoViaje, permitirVacio: true);
        tipoTransporte = NormalizarTipoTransporte(tipoTransporte, permitirVacio: true);

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

        var vm = new LogisticaViajesIndexVm
        {
            Busqueda = q,
            Estatus = estatus,
            TipoViaje = tipoViaje,
            TipoTransporte = tipoTransporte,
            FechaDesde = fechaDesde,
            FechaHasta = fechaHasta
        };

        await using var cn = await AbrirAsync(cancellationToken);

        const string sqlResumen = @"
SELECT
    COUNT_BIG(*) AS TotalViajes,
    SUM(CASE WHEN Estatus=N'Programado' THEN 1 ELSE 0 END) AS Programados,
    SUM(CASE WHEN Estatus=N'En curso' THEN 1 ELSE 0 END) AS EnCurso,
    SUM(CASE WHEN Estatus=N'Completado' THEN 1 ELSE 0 END) AS Completados,
    SUM(CASE WHEN Estatus=N'Cancelado' THEN 1 ELSE 0 END) AS Cancelados,
    SUM(CASE WHEN FechaProgramada=CAST(GETDATE() AS date) AND Estatus<>N'Cancelado' THEN 1 ELSE 0 END) AS ViajesHoy,
    SUM(CASE WHEN Estatus=N'En curso' AND FechaSalidaReal IS NOT NULL AND FechaRegresoReal IS NULL THEN 1 ELSE 0 END) AS RetornosPendientes
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
  AND (@Q IS NULL OR v.Folio LIKE N'%'+@Q+N'%' OR v.Origen LIKE N'%'+@Q+N'%' OR v.Destino LIKE N'%'+@Q+N'%' OR v.Motivo LIKE N'%'+@Q+N'%' OR v.OperadorTexto LIKE N'%'+@Q+N'%')
  AND (@Estatus IS NULL OR v.Estatus=@Estatus)
  AND (@TipoViaje IS NULL OR v.TipoViaje=@TipoViaje)
  AND (@TipoTransporte IS NULL OR v.TipoTransporte=@TipoTransporte)
  AND (@FechaDesde IS NULL OR v.FechaProgramada>=@FechaDesde)
  AND (@FechaHasta IS NULL OR v.FechaProgramada<=@FechaHasta);";

        long totalFiltrado;
        await using (var cmd = new SqlCommand(sqlTotal, cn))
        {
            AgregarFiltros(cmd, q, estatus, tipoViaje, tipoTransporte, fechaDesde, fechaHasta);
            totalFiltrado = Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken));
        }

        const string sql = @"
SELECT
    v.ViajeID,
    ISNULL(v.Folio,N'') AS Folio,
    ISNULL(v.TipoViaje,N'') AS TipoViaje,
    ISNULL(v.TipoTransporte,N'') AS TipoTransporte,
    ISNULL(v.Origen,N'') AS Origen,
    ISNULL(v.Destino,N'') AS Destino,
    ISNULL(v.Motivo,N'') AS Motivo,
    v.FechaProgramada,
    v.HoraSalidaProgramada,
    v.FechaSalidaReal,
    v.FechaRegresoReal,
    CASE
        WHEN v.TipoTransporte=N'Interno'
        THEN ISNULL(u.NumeroEconomico + CASE WHEN NULLIF(u.Placas,N'') IS NULL THEN N'' ELSE N' - '+u.Placas END,N'')
        ELSE ISNULL(v.UnidadExterna,N'')
    END AS Unidad,
    CASE
        WHEN v.TipoTransporte=N'Interno' THEN ISNULL(v.OperadorTexto,N'')
        ELSE ISNULL(v.ChoferExterno,N'')
    END AS Operador,
    ISNULL(v.Estatus,N'') AS Estatus,
    ISNULL(v.TieneIncidencia,0) AS TieneIncidencia
FROM dbo.Logistica_Viajes v
LEFT JOIN dbo.Logistica_Unidades u ON u.UnidadID=v.UnidadID
WHERE v.Activo=1
  AND (@Q IS NULL OR v.Folio LIKE N'%'+@Q+N'%' OR v.Origen LIKE N'%'+@Q+N'%' OR v.Destino LIKE N'%'+@Q+N'%' OR v.Motivo LIKE N'%'+@Q+N'%' OR v.OperadorTexto LIKE N'%'+@Q+N'%')
  AND (@Estatus IS NULL OR v.Estatus=@Estatus)
  AND (@TipoViaje IS NULL OR v.TipoViaje=@TipoViaje)
  AND (@TipoTransporte IS NULL OR v.TipoTransporte=@TipoTransporte)
  AND (@FechaDesde IS NULL OR v.FechaProgramada>=@FechaDesde)
  AND (@FechaHasta IS NULL OR v.FechaProgramada<=@FechaHasta)
ORDER BY
    CASE v.Estatus
        WHEN N'En curso' THEN 1
        WHEN N'Programado' THEN 2
        WHEN N'Completado' THEN 3
        WHEN N'Cancelado' THEN 4
        ELSE 5
    END,
    v.FechaProgramada DESC,
    v.HoraSalidaProgramada DESC,
    v.ViajeID DESC
OFFSET @Offset ROWS FETCH NEXT @TamanoPagina ROWS ONLY;";

        await using (var cmd = new SqlCommand(sql, cn))
        {
            AgregarFiltros(cmd, q, estatus, tipoViaje, tipoTransporte, fechaDesde, fechaHasta);
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
                    TipoTransporte = Texto(rd, "TipoTransporte"),
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
                    TieneIncidencia = Booleano(rd, "TieneIncidencia")
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

        await using var cn = await AbrirAsync(cancellationToken);

        var vm = new LogisticaViajeCrearVm
        {
            TipoTransporte = "Interno",
            FechaProgramada = DateTime.Today
        };

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
        ValidarModelo(model);
        await using var cn = await AbrirAsync(cancellationToken);
        if (!ModelState.IsValid)
        {
            await CargarCatalogosAsync(model, cn, cancellationToken);
            return View(model);
        }
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            string? operadorNombre = null;
            if (model.TipoTransporte == "Interno" && model.OperadorUsuarioID.HasValue && model.OperadorUsuarioID.Value > 0)
                operadorNombre = await ObtenerNombreOperadorInternoAsync(cn, tx, model.OperadorUsuarioID.Value, cancellationToken);
            const string sql = @"
INSERT dbo.Logistica_Viajes
(
    Folio,TipoViaje,TipoTransporte,Origen,Destino,Motivo,
    FechaProgramada,HoraSalidaProgramada,HoraRegresoProgramada,
    RutaID,UnidadID,OperadorUsuarioID,OperadorNombreSnapshot,OperadorTexto,
    TransportistaExterno,UnidadExterna,PlacasExternas,ChoferExterno,
    Estatus,TieneIncidencia,Observaciones,
    ResponsableUsuarioID,ResponsableNombreSnapshot,
    FechaCreacion,CreadoPor,Activo
)
VALUES
(
    NULL,@TipoViaje,@TipoTransporte,@Origen,@Destino,@Motivo,
    @FechaProgramada,@HoraSalidaProgramada,@HoraRegresoProgramada,
    @RutaID,@UnidadID,@OperadorUsuarioID,@OperadorNombreSnapshot,@OperadorTexto,
    @TransportistaExterno,@UnidadExterna,@PlacasExternas,@ChoferExterno,
    N'Programado',0,@Observaciones,
    @UsuarioID,@UsuarioNombre,
    SYSDATETIME(),@UsuarioNombre,1
);
SELECT CONVERT(int,SCOPE_IDENTITY());";
            int viajeId;
            await using (var cmd = new SqlCommand(sql, cn, tx))
            {
                cmd.Parameters.Add("@TipoViaje", SqlDbType.NVarChar, 50).Value = model.TipoViaje;
                cmd.Parameters.Add("@TipoTransporte", SqlDbType.NVarChar, 30).Value = model.TipoTransporte;
                cmd.Parameters.Add("@Origen", SqlDbType.NVarChar, 300).Value = model.Origen;
                cmd.Parameters.Add("@Destino", SqlDbType.NVarChar, 300).Value = model.Destino;
                cmd.Parameters.Add("@Motivo", SqlDbType.NVarChar, 500).Value = model.Motivo;
                cmd.Parameters.Add("@FechaProgramada", SqlDbType.Date).Value = model.FechaProgramada.Date;
                cmd.Parameters.Add("@HoraSalidaProgramada", SqlDbType.Time).Value = Db(model.HoraSalidaProgramada);
                cmd.Parameters.Add("@HoraRegresoProgramada", SqlDbType.Time).Value = Db(model.HoraRegresoProgramada);
                cmd.Parameters.Add("@RutaID", SqlDbType.Int).Value = Db(model.RutaID);
                cmd.Parameters.Add("@UnidadID", SqlDbType.Int).Value = model.TipoTransporte == "Interno" ? Db(model.UnidadID) : DBNull.Value;
                cmd.Parameters.Add("@OperadorUsuarioID", SqlDbType.Int).Value = model.TipoTransporte == "Interno" ? Db(model.OperadorUsuarioID) : DBNull.Value;
                cmd.Parameters.Add("@OperadorNombreSnapshot", SqlDbType.NVarChar, 200).Value = model.TipoTransporte == "Interno" ? Db(operadorNombre) : DBNull.Value;
                cmd.Parameters.Add("@OperadorTexto", SqlDbType.NVarChar, 200).Value = model.TipoTransporte == "Interno" ? Db(operadorNombre) : DBNull.Value;
                cmd.Parameters.Add("@TransportistaExterno", SqlDbType.NVarChar, 200).Value = model.TipoTransporte == "Externo" ? Db(model.TransportistaExterno) : DBNull.Value;
                cmd.Parameters.Add("@UnidadExterna", SqlDbType.NVarChar, 100).Value = model.TipoTransporte == "Externo" ? Db(model.UnidadExterna) : DBNull.Value;
                cmd.Parameters.Add("@PlacasExternas", SqlDbType.NVarChar, 100).Value = model.TipoTransporte == "Externo" ? Db(model.PlacasExternas) : DBNull.Value;
                cmd.Parameters.Add("@ChoferExterno", SqlDbType.NVarChar, 200).Value = model.TipoTransporte == "Externo" ? Db(model.ChoferExterno) : DBNull.Value;
                cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1200).Value = Db(model.Observaciones);
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = Db(UsuarioID);
                cmd.Parameters.Add("@UsuarioNombre", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                viajeId = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
            }
            var folio = $"VIA-{DateTime.Today:yyyy}-{viajeId:000000}";
            await EjecutarAsync(cn, tx, "UPDATE dbo.Logistica_Viajes SET Folio=@Folio WHERE ViajeID=@ViajeID;", cancellationToken, ("@Folio", folio), ("@ViajeID", viajeId));
            await InsertarHistorialAsync(cn, tx, viajeId, "VIAJE_CREADO", null, "Programado", $"Viaje creado. Tipo: {model.TipoViaje}. Transporte: {model.TipoTransporte}. Origen: {model.Origen}. Destino: {model.Destino}.", cancellationToken);
            await tx.CommitAsync(cancellationToken);
            TempData["LogisticaOk"] = $"{folio} creado correctamente. Completa los recursos pendientes antes de iniciar el viaje.";
            return RedirectToAction(nameof(Detalle), new { id = viajeId });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            ModelState.AddModelError("", ex.Message);
            await CargarCatalogosAsync(model, cn, cancellationToken);
            return View(model);
        }
    }
    [HttpGet]
    public async Task<IActionResult> Detalle(int id, CancellationToken cancellationToken)
    {


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
SELECT ViajeID,TipoViaje,TipoTransporte,Origen,Destino,Motivo,
       FechaProgramada,HoraSalidaProgramada,HoraRegresoProgramada,
       RutaID,UnidadID,OperadorUsuarioID,OperadorTexto,
       TransportistaExterno,UnidadExterna,PlacasExternas,ChoferExterno,
       Observaciones,Estatus
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
                TipoTransporte = Texto(rd, "TipoTransporte"),
                Origen = Texto(rd, "Origen"),
                Destino = Texto(rd, "Destino"),
                Motivo = Texto(rd, "Motivo"),
                FechaProgramada = Fecha(rd, "FechaProgramada") ?? DateTime.Today,
                HoraSalidaProgramada = Hora(rd, "HoraSalidaProgramada"),
                HoraRegresoProgramada = Hora(rd, "HoraRegresoProgramada"),
                RutaID = EnteroNullable(rd, "RutaID"),
                UnidadID = EnteroNullable(rd, "UnidadID"),
                OperadorUsuarioID = EnteroNullable(rd, "OperadorUsuarioID"),
                OperadorTexto = TextoNullable(rd, "OperadorTexto"),
                TransportistaExterno = TextoNullable(rd, "TransportistaExterno"),
                UnidadExterna = TextoNullable(rd, "UnidadExterna"),
                PlacasExternas = TextoNullable(rd, "PlacasExternas"),
                ChoferExterno = TextoNullable(rd, "ChoferExterno"),
                Observaciones = TextoNullable(rd, "Observaciones")
            };
        }
        if (estatus != "Programado")
        {
            TempData["LogisticaError"] = "Solo los viajes Programados pueden editarse.";
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
            string? operadorNombre = null;
            if (model.TipoTransporte == "Interno" && model.OperadorUsuarioID.HasValue && model.OperadorUsuarioID.Value > 0)
                operadorNombre = await ObtenerNombreOperadorInternoAsync(cn, tx, model.OperadorUsuarioID.Value, cancellationToken);
            const string sql = @"
UPDATE dbo.Logistica_Viajes
SET TipoViaje=@TipoViaje,TipoTransporte=@TipoTransporte,Origen=@Origen,Destino=@Destino,Motivo=@Motivo,
    FechaProgramada=@FechaProgramada,HoraSalidaProgramada=@HoraSalidaProgramada,HoraRegresoProgramada=@HoraRegresoProgramada,
    RutaID=@RutaID,UnidadID=@UnidadID,OperadorUsuarioID=@OperadorUsuarioID,
    OperadorNombreSnapshot=@OperadorNombreSnapshot,OperadorTexto=@OperadorTexto,
    TransportistaExterno=@TransportistaExterno,UnidadExterna=@UnidadExterna,
    PlacasExternas=@PlacasExternas,ChoferExterno=@ChoferExterno,
    Observaciones=@Observaciones,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
WHERE ViajeID=@ViajeID AND Activo=1 AND Estatus=N'Programado';
SELECT @@ROWCOUNT;";
            await using (var cmd = new SqlCommand(sql, cn, tx))
            {
                cmd.Parameters.Add("@TipoViaje", SqlDbType.NVarChar, 50).Value = model.TipoViaje;
                cmd.Parameters.Add("@TipoTransporte", SqlDbType.NVarChar, 30).Value = model.TipoTransporte;
                cmd.Parameters.Add("@Origen", SqlDbType.NVarChar, 300).Value = model.Origen;
                cmd.Parameters.Add("@Destino", SqlDbType.NVarChar, 300).Value = model.Destino;
                cmd.Parameters.Add("@Motivo", SqlDbType.NVarChar, 500).Value = model.Motivo;
                cmd.Parameters.Add("@FechaProgramada", SqlDbType.Date).Value = model.FechaProgramada.Date;
                cmd.Parameters.Add("@HoraSalidaProgramada", SqlDbType.Time).Value = Db(model.HoraSalidaProgramada);
                cmd.Parameters.Add("@HoraRegresoProgramada", SqlDbType.Time).Value = Db(model.HoraRegresoProgramada);
                cmd.Parameters.Add("@RutaID", SqlDbType.Int).Value = Db(model.RutaID);
                cmd.Parameters.Add("@UnidadID", SqlDbType.Int).Value = model.TipoTransporte == "Interno" ? Db(model.UnidadID) : DBNull.Value;
                cmd.Parameters.Add("@OperadorUsuarioID", SqlDbType.Int).Value = model.TipoTransporte == "Interno" ? Db(model.OperadorUsuarioID) : DBNull.Value;
                cmd.Parameters.Add("@OperadorNombreSnapshot", SqlDbType.NVarChar, 200).Value = model.TipoTransporte == "Interno" ? Db(operadorNombre) : DBNull.Value;
                cmd.Parameters.Add("@OperadorTexto", SqlDbType.NVarChar, 200).Value = model.TipoTransporte == "Interno" ? Db(operadorNombre) : DBNull.Value;
                cmd.Parameters.Add("@TransportistaExterno", SqlDbType.NVarChar, 200).Value = model.TipoTransporte == "Externo" ? Db(model.TransportistaExterno) : DBNull.Value;
                cmd.Parameters.Add("@UnidadExterna", SqlDbType.NVarChar, 100).Value = model.TipoTransporte == "Externo" ? Db(model.UnidadExterna) : DBNull.Value;
                cmd.Parameters.Add("@PlacasExternas", SqlDbType.NVarChar, 100).Value = model.TipoTransporte == "Externo" ? Db(model.PlacasExternas) : DBNull.Value;
                cmd.Parameters.Add("@ChoferExterno", SqlDbType.NVarChar, 200).Value = model.TipoTransporte == "Externo" ? Db(model.ChoferExterno) : DBNull.Value;
                cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1200).Value = Db(model.Observaciones);
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = model.ViajeID;
                if (Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) == 0) throw new InvalidOperationException("El viaje cambió mientras se intentaba actualizar.");
            }
            await InsertarHistorialAsync(cn, tx, model.ViajeID, "PREPARACION_ACTUALIZADA", "Programado", "Programado", $"Preparación actualizada. Tipo: {model.TipoViaje}. Transporte: {model.TipoTransporte}. Origen: {model.Origen}. Destino: {model.Destino}.", cancellationToken);
            await tx.CommitAsync(cancellationToken);
            TempData["LogisticaOk"] = "Preparación del viaje actualizada correctamente.";
            return RedirectToAction(nameof(Detalle), new { id = model.ViajeID });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
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
        if (model.KilometrajeSalida.HasValue && model.KilometrajeSalida.Value < 0) ModelState.AddModelError(nameof(model.KilometrajeSalida), "El kilometraje no puede ser negativo.");
        if (!ModelState.IsValid)
        {
            TempData["LogisticaError"] = ObtenerErroresModelState();
            return RedirectToAction(nameof(Detalle), new { id = model.ViajeID });
        }
        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            await ValidarViajeListoParaIniciarAsync(cn, tx, model.ViajeID, cancellationToken);
            const string sqlIncidencias = @"
SELECT COUNT_BIG(*)
FROM dbo.Logistica_ViajeIncidencias WITH(UPDLOCK,HOLDLOCK)
WHERE ViajeID=@ViajeID AND Activo=1 AND Estatus IN(N'Abierta',N'En seguimiento') AND Severidad=N'Crítica';";
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
                cmd.Parameters.Add("@KilometrajeSalida", SqlDbType.Int).Value = Db(model.KilometrajeSalida);
                cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1000).Value = Db(model.Observaciones);
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = model.ViajeID;
                if (Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) == 0) throw new InvalidOperationException("El viaje cambió mientras se registraba la salida.");
            }
            var historial = $"Viaje iniciado el {model.FechaSalida:dd/MM/yyyy HH:mm}.";
            if (model.KilometrajeSalida.HasValue) historial += $" Kilometraje inicial: {model.KilometrajeSalida.Value:N0} km.";
            if (!string.IsNullOrWhiteSpace(model.Observaciones)) historial += $" Observaciones: {model.Observaciones}";
            await InsertarHistorialAsync(cn, tx, model.ViajeID, "VIAJE_INICIADO", "Programado", "En curso", historial, cancellationToken);
            await tx.CommitAsync(cancellationToken);
            TempData["LogisticaOk"] = "Viaje iniciado correctamente.";
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
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
        if (model.KilometrajeRegreso.HasValue && model.KilometrajeRegreso.Value < 0) ModelState.AddModelError(nameof(model.KilometrajeRegreso), "El kilometraje no puede ser negativo.");
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
            const string sqlActual = @"
SELECT Estatus,FechaSalidaReal,FechaRegresoReal,KilometrajeSalida
FROM dbo.Logistica_Viajes WITH(UPDLOCK,HOLDLOCK)
WHERE ViajeID=@ViajeID AND Activo=1;";
            string estatus;
            DateTime? fechaSalida;
            DateTime? fechaRegresoActual;
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
            if (fechaRegresoActual.HasValue) throw new InvalidOperationException("El regreso ya fue registrado.");
            if (model.FechaRegreso < fechaSalida.Value) throw new InvalidOperationException("La fecha de regreso no puede ser anterior a la salida.");
            if (kilometrajeSalida.HasValue && model.KilometrajeRegreso.HasValue && model.KilometrajeRegreso.Value < kilometrajeSalida.Value) throw new InvalidOperationException($"El kilometraje de regreso no puede ser menor al de salida ({kilometrajeSalida.Value:N0} km).");
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
                cmd.Parameters.Add("@KilometrajeRegreso", SqlDbType.Int).Value = Db(model.KilometrajeRegreso);
                var pGasolina = cmd.Parameters.Add("@PagoGasolina", SqlDbType.Decimal);
                pGasolina.Precision = 18;
                pGasolina.Scale = 2;
                pGasolina.Value = Db(model.PagoGasolina);
                cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1000).Value = Db(model.Observaciones);
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = model.ViajeID;
                if (Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) == 0) throw new InvalidOperationException("El viaje cambió mientras se registraba el regreso.");
            }
            var historial = $"Regreso registrado el {model.FechaRegreso:dd/MM/yyyy HH:mm}.";
            if (model.KilometrajeRegreso.HasValue) historial += $" Kilometraje final: {model.KilometrajeRegreso.Value:N0} km.";
            if (kilometrajeSalida.HasValue && model.KilometrajeRegreso.HasValue) historial += $" KM utilizados: {(model.KilometrajeRegreso.Value - kilometrajeSalida.Value):N0} km.";
            if (model.PagoGasolina.HasValue) historial += $" Pago de gasolina: ${model.PagoGasolina.Value:N2}.";
            if (!string.IsNullOrWhiteSpace(model.Observaciones)) historial += $" Observaciones: {model.Observaciones}";
            await InsertarHistorialAsync(cn, tx, model.ViajeID, "RETORNO_REGISTRADO", "En curso", "Completado", historial, cancellationToken);
            await tx.CommitAsync(cancellationToken);
            TempData["LogisticaOk"] = "Regreso registrado y viaje completado correctamente.";
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            TempData["LogisticaError"] = ex.Message;
        }
        return RedirectToAction(nameof(Detalle), new { id = model.ViajeID });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancelar(LogisticaViajeCancelarVm model, CancellationToken cancellationToken)
    {


        model.Motivo = model.Motivo?.Trim() ?? string.Empty;

        if (model.ViajeID <= 0 || string.IsNullOrWhiteSpace(model.Motivo))
        {
            TempData["LogisticaError"] = "El viaje y el motivo de cancelación son obligatorios.";
            return model.ViajeID > 0
                ? RedirectToAction(nameof(Detalle), new { id = model.ViajeID })
                : RedirectToAction(nameof(Index));
        }

        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        try
        {
            var viaje = await ObtenerViajeParaActualizarAsync(cn, tx, model.ViajeID, cancellationToken)
                ?? throw new InvalidOperationException("El viaje no existe.");

            if (viaje.Estatus != "Programado")
                throw new InvalidOperationException("Solo un viaje Programado puede cancelarse.");

            const string sql = @"
UPDATE dbo.Logistica_Viajes
SET Estatus=N'Cancelado',
    MotivoCancelacion=@Motivo,
    FechaModificacion=SYSDATETIME(),
    ActualizadoPor=@Usuario
WHERE ViajeID=@ViajeID
  AND Activo=1
  AND Estatus=N'Programado';
SELECT @@ROWCOUNT;";

            await using (var cmd = new SqlCommand(sql, cn, tx))
            {
                cmd.Parameters.Add("@Motivo", SqlDbType.NVarChar, 1000).Value = model.Motivo;
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = model.ViajeID;

                if (Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) == 0)
                    throw new InvalidOperationException("El viaje cambió mientras se intentaba cancelar.");
            }

            await InsertarHistorialAsync(
                cn,
                tx,
                model.ViajeID,
                "VIAJE_CANCELADO",
                "Programado",
                "Cancelado",
                $"Motivo: {model.Motivo}",
                cancellationToken);

            await tx.CommitAsync(cancellationToken);
            TempData["LogisticaOk"] = "Viaje cancelado correctamente.";
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            TempData["LogisticaError"] = ex.Message;
        }

        return RedirectToAction(nameof(Detalle), new { id = model.ViajeID });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegistrarIncidencia(
        LogisticaViajeIncidenciaCrearVm model,
        CancellationToken cancellationToken)
    {


        model.Tipo = model.Tipo?.Trim() ?? string.Empty;
        model.Severidad = model.Severidad?.Trim() ?? string.Empty;
        model.Descripcion = model.Descripcion?.Trim() ?? string.Empty;
        model.Responsable = model.Responsable?.Trim();

        var tiposPermitidos = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Unidad",
            "Operador",
            "Tráfico",
            "Retraso",
            "Ruta",
            "Material",
            "Recolección",
            "Cliente / destino",
            "Seguridad",
            "Otro"
        };

        var severidadesPermitidas = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Baja","Media","Alta","Crítica"
        };

        if (model.ViajeID <= 0)
            ModelState.AddModelError(nameof(model.ViajeID), "El viaje no es válido.");

        if (!tiposPermitidos.Contains(model.Tipo))
            ModelState.AddModelError(nameof(model.Tipo), "Selecciona un tipo de incidencia válido.");

        if (!severidadesPermitidas.Contains(model.Severidad))
            ModelState.AddModelError(nameof(model.Severidad), "Selecciona una severidad válida.");

        if (string.IsNullOrWhiteSpace(model.Descripcion))
            ModelState.AddModelError(nameof(model.Descripcion), "La descripción es obligatoria.");

        if (!ModelState.IsValid)
        {
            TempData["LogisticaError"] = ObtenerErroresModelState();
            return model.ViajeID > 0
                ? RedirectToAction(nameof(Detalle), new { id = model.ViajeID })
                : RedirectToAction(nameof(Index));
        }

        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        try
        {
            var viaje = await ObtenerViajeParaActualizarAsync(cn, tx, model.ViajeID, cancellationToken)
                ?? throw new InvalidOperationException("El viaje no existe.");

            if (viaje.Estatus == "Cancelado")
                throw new InvalidOperationException("No se pueden registrar incidencias en un viaje cancelado.");

            const string sqlDuplicado = @"
SELECT COUNT_BIG(*)
FROM dbo.Logistica_ViajeIncidencias WITH (UPDLOCK,HOLDLOCK)
WHERE ViajeID=@ViajeID
  AND Activo=1
  AND Estatus IN(N'Abierta',N'En seguimiento')
  AND Tipo=@Tipo
  AND Descripcion=@Descripcion;";

            await using (var cmd = new SqlCommand(sqlDuplicado, cn, tx))
            {
                cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = model.ViajeID;
                cmd.Parameters.Add("@Tipo", SqlDbType.NVarChar, 80).Value = model.Tipo;
                cmd.Parameters.Add("@Descripcion", SqlDbType.NVarChar, 1200).Value = model.Descripcion;

                if (Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken)) > 0)
                    throw new InvalidOperationException("Ya existe una incidencia abierta con el mismo tipo y descripción.");
            }

            const string sql = @"
INSERT dbo.Logistica_ViajeIncidencias
(
    ViajeID,Tipo,Severidad,Descripcion,Responsable,Estatus,
    FechaRegistro,UsuarioRegistroID,UsuarioRegistro,Activo
)
VALUES
(
    @ViajeID,@Tipo,@Severidad,@Descripcion,@Responsable,N'Abierta',
    SYSDATETIME(),@UsuarioID,@UsuarioNombre,1
);
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

            await EjecutarAsync(
                cn,
                tx,
                @"UPDATE dbo.Logistica_Viajes
SET TieneIncidencia=1,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
WHERE ViajeID=@ViajeID AND Activo=1;",
                cancellationToken,
                ("@Usuario", UsuarioNombre),
                ("@ViajeID", model.ViajeID));

            await InsertarHistorialAsync(
                cn,
                tx,
                model.ViajeID,
                "INCIDENCIA_REGISTRADA",
                viaje.Estatus,
                viaje.Estatus,
                $"Incidencia VINC-{incidenciaId:000000}. {model.Tipo} / {model.Severidad}. {model.Descripcion}",
                cancellationToken);

            await tx.CommitAsync(cancellationToken);
            TempData["LogisticaOk"] = $"Incidencia VINC-{incidenciaId:000000} registrada.";
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            TempData["LogisticaError"] = ex.Message;
        }

        return RedirectToAction(nameof(Detalle), new { id = model.ViajeID });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CerrarIncidencia(
        LogisticaViajeIncidenciaCerrarVm model,
        CancellationToken cancellationToken)
    {


        model.Solucion = model.Solucion?.Trim() ?? string.Empty;

        if (model.ViajeID <= 0 ||
            model.ViajeIncidenciaID <= 0 ||
            string.IsNullOrWhiteSpace(model.Solucion))
        {
            TempData["LogisticaError"] = "Incidencia, viaje y solución son obligatorios.";
            return model.ViajeID > 0
                ? RedirectToAction(nameof(Detalle), new { id = model.ViajeID })
                : RedirectToAction(nameof(Index));
        }

        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        try
        {
            var viaje = await ObtenerViajeParaActualizarAsync(cn, tx, model.ViajeID, cancellationToken)
                ?? throw new InvalidOperationException("El viaje no existe.");

            const string sqlCerrar = @"
UPDATE dbo.Logistica_ViajeIncidencias
SET Estatus=N'Cerrada',
    Solucion=@Solucion,
    FechaCierre=SYSDATETIME(),
    UsuarioCierreID=@UsuarioID,
    UsuarioCierre=@UsuarioNombre
WHERE ViajeIncidenciaID=@IncidenciaID
  AND ViajeID=@ViajeID
  AND Activo=1
  AND Estatus IN(N'Abierta',N'En seguimiento');
SELECT @@ROWCOUNT;";

            await using (var cmd = new SqlCommand(sqlCerrar, cn, tx))
            {
                cmd.Parameters.Add("@Solucion", SqlDbType.NVarChar, 1200).Value = model.Solucion;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = Db(UsuarioID);
                cmd.Parameters.Add("@UsuarioNombre", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@IncidenciaID", SqlDbType.Int).Value = model.ViajeIncidenciaID;
                cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = model.ViajeID;

                if (Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) == 0)
                    throw new InvalidOperationException("La incidencia no existe, ya fue cerrada o cambió durante la operación.");
            }

            const string sqlPendientes = @"
SELECT COUNT_BIG(*)
FROM dbo.Logistica_ViajeIncidencias
WHERE ViajeID=@ViajeID
  AND Activo=1
  AND Estatus IN(N'Abierta',N'En seguimiento');";

            long pendientes;

            await using (var cmd = new SqlCommand(sqlPendientes, cn, tx))
            {
                cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = model.ViajeID;
                pendientes = Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken));
            }

            await EjecutarAsync(
                cn,
                tx,
                @"UPDATE dbo.Logistica_Viajes
SET TieneIncidencia=@TieneIncidencia,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
WHERE ViajeID=@ViajeID AND Activo=1;",
                cancellationToken,
                ("@TieneIncidencia", pendientes > 0),
                ("@Usuario", UsuarioNombre),
                ("@ViajeID", model.ViajeID));

            await InsertarHistorialAsync(
                cn,
                tx,
                model.ViajeID,
                "INCIDENCIA_CERRADA",
                viaje.Estatus,
                viaje.Estatus,
                $"Incidencia VINC-{model.ViajeIncidenciaID:000000} cerrada. Solución: {model.Solucion}",
                cancellationToken);

            await tx.CommitAsync(cancellationToken);

            TempData["LogisticaOk"] = pendientes > 0
                ? $"Incidencia cerrada. Quedan {pendientes:N0} incidencia(s) abiertas."
                : "Incidencia cerrada. El viaje ya no tiene incidencias abiertas.";
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            TempData["LogisticaError"] = ex.Message;
        }

        return RedirectToAction(nameof(Detalle), new { id = model.ViajeID });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(10_485_760)]
    public async Task<IActionResult> SubirEvidencia(int viajeId, string? tipoEvidencia, IFormFile? archivo, string? observaciones, CancellationToken cancellationToken)
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
        if (archivo == null || archivo.Length <= 0)
        {
            TempData["LogisticaError"] = "Selecciona una fotografía o archivo.";
            return RedirectToAction(nameof(Detalle), new { id = viajeId });
        }
        const long maximo = 10 * 1024 * 1024;
        if (archivo.Length > maximo)
        {
            TempData["LogisticaError"] = "El archivo excede el máximo permitido de 10 MB.";
            return RedirectToAction(nameof(Detalle), new { id = viajeId });
        }
        var nombreOriginal = Path.GetFileName(archivo.FileName);
        var extension = Path.GetExtension(nombreOriginal).ToLowerInvariant();
        var permitidas = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".pdf" };
        if (!permitidas.Contains(extension))
        {
            TempData["LogisticaError"] = "Solo se permiten archivos JPG, JPEG, PNG o PDF.";
            return RedirectToAction(nameof(Detalle), new { id = viajeId });
        }
        string? rutaFisicaCreada = null;
        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var viaje = await ObtenerViajeParaActualizarAsync(cn, tx, viajeId, cancellationToken) ?? throw new InvalidOperationException("El viaje no existe.");
            if (viaje.Estatus == "Cancelado") throw new InvalidOperationException("No se pueden agregar evidencias a un viaje cancelado.");
            var carpetaRelativa = Path.Combine("Logistica", "Viajes", viajeId.ToString(), "Evidencias");
            var carpetaFisica = Path.Combine(_environment.ContentRootPath, "App_Data", carpetaRelativa);
            Directory.CreateDirectory(carpetaFisica);
            var nombreFisico = $"{Guid.NewGuid():N}{extension}";
            rutaFisicaCreada = Path.Combine(carpetaFisica, nombreFisico);
            await using (var stream = new FileStream(rutaFisicaCreada, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await archivo.CopyToAsync(stream, cancellationToken);
            var rutaRelativa = Path.Combine("App_Data", carpetaRelativa, nombreFisico).Replace('\\', '/');
            const string sql = @"
INSERT dbo.Logistica_ViajeEvidencias
(ViajeID,TipoEvidencia,NombreOriginal,NombreFisico,RutaRelativa,TipoContenido,TamanoBytes,Observaciones,UsuarioCargaID,UsuarioCargaNombre,FechaCarga,Activo,FechaCreacion,CreadoPor)
VALUES
(@ViajeID,@TipoEvidencia,@NombreOriginal,@NombreFisico,@RutaRelativa,@TipoContenido,@TamanoBytes,@Observaciones,@UsuarioID,@UsuarioNombre,SYSDATETIME(),1,SYSDATETIME(),@UsuarioNombre);
SELECT CONVERT(int,SCOPE_IDENTITY());";
            int evidenciaId;
            await using (var cmd = new SqlCommand(sql, cn, tx))
            {
                cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
                cmd.Parameters.Add("@TipoEvidencia", SqlDbType.NVarChar, 50).Value = tipoEvidencia;
                cmd.Parameters.Add("@NombreOriginal", SqlDbType.NVarChar, 260).Value = nombreOriginal;
                cmd.Parameters.Add("@NombreFisico", SqlDbType.NVarChar, 260).Value = nombreFisico;
                cmd.Parameters.Add("@RutaRelativa", SqlDbType.NVarChar, 600).Value = rutaRelativa;
                cmd.Parameters.Add("@TipoContenido", SqlDbType.NVarChar, 150).Value = archivo.ContentType ?? "application/octet-stream";
                cmd.Parameters.Add("@TamanoBytes", SqlDbType.BigInt).Value = archivo.Length;
                cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1000).Value = Db(observaciones);
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = Db(UsuarioID);
                cmd.Parameters.Add("@UsuarioNombre", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                evidenciaId = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
            }
            await InsertarHistorialAsync(cn, tx, viajeId, "EVIDENCIA_AGREGADA", viaje.Estatus, viaje.Estatus, $"Evidencia VEVI-{evidenciaId:000000} agregada. Tipo: {tipoEvidencia}. Archivo: {nombreOriginal}.", cancellationToken);
            await tx.CommitAsync(cancellationToken);
            TempData["LogisticaOk"] = "Evidencia cargada correctamente.";
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(rutaFisicaCreada) && System.IO.File.Exists(rutaFisicaCreada))
            {
                try { System.IO.File.Delete(rutaFisicaCreada); } catch { }
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
        const string sql = @"
SELECT TOP(1) NombreOriginal,RutaRelativa,TipoContenido
FROM dbo.Logistica_ViajeEvidencias
WHERE ViajeEvidenciaID=@Id AND Activo=1;";
        string nombreOriginal;
        string rutaRelativa;
        string tipoContenido;
        await using (var cmd = new SqlCommand(sql, cn))
        {
            cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await rd.ReadAsync(cancellationToken)) return NotFound();
            nombreOriginal = Texto(rd, "NombreOriginal");
            rutaRelativa = Texto(rd, "RutaRelativa");
            tipoContenido = Texto(rd, "TipoContenido");
        }
        var rutaFisica = Path.Combine(_environment.ContentRootPath, rutaRelativa.Replace('/', Path.DirectorySeparatorChar));
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
            const string sql = @"
UPDATE dbo.Logistica_ViajeEvidencias
SET Activo=0,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
WHERE ViajeEvidenciaID=@EvidenciaID AND ViajeID=@ViajeID AND Activo=1;
SELECT @@ROWCOUNT;";
            await using (var cmd = new SqlCommand(sql, cn, tx))
            {
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@EvidenciaID", SqlDbType.Int).Value = evidenciaId;
                cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
                if (Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) == 0) throw new InvalidOperationException("La evidencia no existe o ya fue retirada.");
            }
            await InsertarHistorialAsync(cn, tx, viajeId, "EVIDENCIA_RETIRADA", viaje.Estatus, viaje.Estatus, $"Se retiró la evidencia VEVI-{evidenciaId:000000}.", cancellationToken);
            await tx.CommitAsync(cancellationToken);
            TempData["LogisticaOk"] = "Evidencia retirada.";
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            TempData["LogisticaError"] = ex.Message;
        }
        return RedirectToAction(nameof(Detalle), new { id = viajeId });
    }
    private async Task<LogisticaViajeDetalleVm?> CargarDetalleAsync(SqlConnection cn, int viajeId, CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT v.ViajeID,ISNULL(v.Folio,N'') AS Folio,ISNULL(v.TipoViaje,N'') AS TipoViaje,ISNULL(v.TipoTransporte,N'') AS TipoTransporte,
ISNULL(v.Origen,N'') AS Origen,ISNULL(v.Destino,N'') AS Destino,ISNULL(v.Motivo,N'') AS Motivo,v.FechaProgramada,v.HoraSalidaProgramada,v.HoraRegresoProgramada,
v.FechaSalidaReal,v.FechaRegresoReal,v.RutaID,ISNULL(r.Codigo+N' - '+r.Nombre,N'') AS Ruta,v.UnidadID,
ISNULL(u.NumeroEconomico+CASE WHEN NULLIF(u.Placas,N'') IS NULL THEN N'' ELSE N' - '+u.Placas END,N'') AS Unidad,
v.OperadorUsuarioID,ISNULL(NULLIF(v.OperadorNombreSnapshot,N''),ISNULL(v.OperadorTexto,N'')) AS Operador,
ISNULL(v.TransportistaExterno,N'') AS TransportistaExterno,ISNULL(v.UnidadExterna,N'') AS UnidadExterna,ISNULL(v.PlacasExternas,N'') AS PlacasExternas,
ISNULL(v.ChoferExterno,N'') AS ChoferExterno,ISNULL(v.Estatus,N'') AS Estatus,ISNULL(v.Observaciones,N'') AS Observaciones,
ISNULL(v.TieneIncidencia,0) AS TieneIncidencia,v.KilometrajeSalida,v.KilometrajeRegreso,v.PagoGasolina,v.ResponsableUsuarioID,
ISNULL(v.ResponsableNombreSnapshot,N'') AS UsuarioResponsable,v.FechaCreacion,ISNULL(v.CreadoPor,N'') AS CreadoPor
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
                TipoTransporte = Texto(rd, "TipoTransporte"),
                Origen = Texto(rd, "Origen"),
                Destino = Texto(rd, "Destino"),
                Motivo = Texto(rd, "Motivo"),
                FechaProgramada = Fecha(rd, "FechaProgramada") ?? DateTime.MinValue,
                HoraSalidaProgramada = Hora(rd, "HoraSalidaProgramada"),
                HoraRegresoProgramada = Hora(rd, "HoraRegresoProgramada"),
                FechaSalidaReal = Fecha(rd, "FechaSalidaReal"),
                FechaRegresoReal = Fecha(rd, "FechaRegresoReal"),
                RutaID = EnteroNullable(rd, "RutaID"),
                Ruta = Texto(rd, "Ruta"),
                UnidadID = EnteroNullable(rd, "UnidadID"),
                Unidad = Texto(rd, "Unidad"),
                OperadorUsuarioID = EnteroNullable(rd, "OperadorUsuarioID"),
                Operador = Texto(rd, "Operador"),
                TransportistaExterno = Texto(rd, "TransportistaExterno"),
                UnidadExterna = Texto(rd, "UnidadExterna"),
                PlacasExternas = Texto(rd, "PlacasExternas"),
                ChoferExterno = Texto(rd, "ChoferExterno"),
                Estatus = Texto(rd, "Estatus"),
                Observaciones = Texto(rd, "Observaciones"),
                TieneIncidencia = Booleano(rd, "TieneIncidencia"),
                KilometrajeSalida = EnteroNullable(rd, "KilometrajeSalida"),
                KilometrajeRegreso = EnteroNullable(rd, "KilometrajeRegreso"),
                PagoGasolina = DecimalNullable(rd, "PagoGasolina"),
                UsuarioResponsableID = EnteroNullable(rd, "ResponsableUsuarioID"),
                UsuarioResponsable = Texto(rd, "UsuarioResponsable"),
                FechaCreacion = Fecha(rd, "FechaCreacion") ?? DateTime.MinValue,
                CreadoPor = Texto(rd, "CreadoPor")
            };
        }
        const string sqlHistorial = @"
SELECT HistorialID,ViajeID,Evento,ISNULL(EstadoAnterior,N'') AS EstadoAnterior,ISNULL(EstadoNuevo,N'') AS EstadoNuevo,
ISNULL(Observaciones,N'') AS Observaciones,UsuarioID,ISNULL(UsuarioNombre,N'') AS Usuario,FechaEvento
FROM dbo.Logistica_ViajeHistorial WHERE ViajeID=@ViajeID ORDER BY FechaEvento DESC,HistorialID DESC;";
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
        const string sqlIncidencias = @"
SELECT ViajeIncidenciaID,ViajeID,Tipo,Severidad,Descripcion,Estatus,ISNULL(Responsable,N'') AS Responsable,FechaRegistro,FechaCierre
FROM dbo.Logistica_ViajeIncidencias
WHERE ViajeID=@ViajeID AND Activo=1
ORDER BY CASE WHEN Estatus IN(N'Abierta',N'En seguimiento') THEN 0 ELSE 1 END,
CASE Severidad WHEN N'Crítica' THEN 1 WHEN N'Alta' THEN 2 WHEN N'Media' THEN 3 WHEN N'Baja' THEN 4 ELSE 5 END,FechaRegistro DESC,ViajeIncidenciaID DESC;";
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

    private static async Task<string> ValidarRecursosInternosAsync(SqlConnection cn, SqlTransaction tx, int? unidadId, int? operadorUsuarioId, CancellationToken cancellationToken)
    {
        if (!unidadId.HasValue || unidadId.Value <= 0) throw new InvalidOperationException("Selecciona una unidad interna válida.");
        if (!operadorUsuarioId.HasValue || operadorUsuarioId.Value <= 0) throw new InvalidOperationException("Selecciona un operador/chofer válido.");
        const string sqlUnidad = @"SELECT COUNT_BIG(*) FROM dbo.Logistica_Unidades WITH(UPDLOCK,HOLDLOCK) WHERE UnidadID=@UnidadID AND Activo=1;";
        await using (var cmd = new SqlCommand(sqlUnidad, cn, tx))
        {
            cmd.Parameters.Add("@UnidadID", SqlDbType.Int).Value = unidadId.Value;
            if (Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken)) <= 0) throw new InvalidOperationException("La unidad seleccionada no existe o se encuentra inactiva.");
        }
        const string sqlOperador = @"
SELECT TOP(1)
LTRIM(RTRIM(CONCAT(ISNULL(P.Nombre,N''),N' ',ISNULL(P.ApellidoPaterno,N''),N' ',ISNULL(P.ApellidoMaterno,N'')))) AS NombreCompleto
FROM dbo.Usuarios U WITH(UPDLOCK,HOLDLOCK)
INNER JOIN dbo.Persona P ON P.PersonaID=U.PersonaID
INNER JOIN dbo.Departamentos D ON D.DepartamentoID=U.DepartamentoID
WHERE U.UsuarioID=@UsuarioID
AND U.Activo=1
AND D.Activo=1
AND UPPER(REPLACE(LTRIM(RTRIM(ISNULL(D.NombreDepartamento,N''))),N'Í',N'I'))=N'LOGISTICA'
AND UPPER(LTRIM(RTRIM(ISNULL(P.Puesto,N'')))) LIKE N'%CHOFER%';";
        await using var cmdOperador = new SqlCommand(sqlOperador, cn, tx);
        cmdOperador.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = operadorUsuarioId.Value;
        var valor = await cmdOperador.ExecuteScalarAsync(cancellationToken);
        var nombre = valor == null || valor == DBNull.Value ? string.Empty : valor.ToString()?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(nombre)) throw new InvalidOperationException("El operador seleccionado debe ser un usuario activo del departamento de Logística y tener un puesto de Chofer.");
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

    private static void AgregarFiltros(
     SqlCommand cmd,
     string? q,
     string? estatus,
     string? tipoViaje,
     string? tipoTransporte,
     DateTime? fechaDesde,
     DateTime? fechaHasta)
    {
        cmd.Parameters.Add("@Q", SqlDbType.NVarChar, 250).Value =
            string.IsNullOrWhiteSpace(q)
                ? DBNull.Value
                : q.Trim();

        cmd.Parameters.Add("@Estatus", SqlDbType.NVarChar, 30).Value =
            string.IsNullOrWhiteSpace(estatus)
                ? DBNull.Value
                : estatus.Trim();

        cmd.Parameters.Add("@TipoViaje", SqlDbType.NVarChar, 50).Value =
            string.IsNullOrWhiteSpace(tipoViaje)
                ? DBNull.Value
                : tipoViaje.Trim();

        cmd.Parameters.Add("@TipoTransporte", SqlDbType.NVarChar, 30).Value =
            string.IsNullOrWhiteSpace(tipoTransporte)
                ? DBNull.Value
                : tipoTransporte.Trim();

        cmd.Parameters.Add("@FechaDesde", SqlDbType.Date).Value =
            fechaDesde.HasValue
                ? fechaDesde.Value.Date
                : DBNull.Value;

        cmd.Parameters.Add("@FechaHasta", SqlDbType.Date).Value =
            fechaHasta.HasValue
                ? fechaHasta.Value.Date
                : DBNull.Value;
    }

    [HttpGet]
    public async Task<IActionResult> Calendario(CancellationToken cancellationToken)
    {

        await using var cn = await AbrirAsync(cancellationToken);

        const string sql = @"
SELECT CASE WHEN OBJECT_ID(N'dbo.Logistica_Viajes',N'U') IS NOT NULL
THEN 1 ELSE 0 END;";

        await using var cmd = new SqlCommand(sql, cn);

        if (Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) != 1)
        {
            TempData["LogisticaError"] = "Falta la estructura de Viajes.";
            return RedirectToAction(nameof(Index));
        }

        return View();
    }

    [HttpGet]
    public async Task<IActionResult> EventosCalendario(
        DateTime desde,
        DateTime hasta,
        string? estatus = null,
        string? q = null,
        string? tipoTransporte = null,
        CancellationToken cancellationToken = default)
    {

        desde = desde.Date;
        hasta = hasta.Date;

        if (hasta < desde)
            return BadRequest(new { ok = false, mensaje = "El rango de fechas no es válido." });

        if ((hasta - desde).TotalDays > 120)
            return BadRequest(new { ok = false, mensaje = "El calendario solo puede consultar hasta 120 días por solicitud." });

        q = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
        estatus = string.IsNullOrWhiteSpace(estatus) ? null : estatus.Trim();
        tipoTransporte = string.IsNullOrWhiteSpace(tipoTransporte) ? null : tipoTransporte.Trim();

        await using var cn = await AbrirAsync(cancellationToken);

        const string sql = @"
SELECT
    v.ViajeID,
    ISNULL(v.Folio,N'') AS Folio,
    ISNULL(v.TipoViaje,N'') AS TipoViaje,
    ISNULL(v.TipoTransporte,N'') AS TipoTransporte,
    ISNULL(v.Origen,N'') AS Origen,
    ISNULL(v.Destino,N'') AS Destino,
    ISNULL(v.Motivo,N'') AS Motivo,
    v.FechaProgramada,
    v.HoraSalidaProgramada,
    ISNULL(v.Estatus,N'') AS Estatus,
    ISNULL(v.TieneIncidencia,0) AS TieneIncidencia,
    CASE
        WHEN v.TipoTransporte=N'Interno'
            THEN ISNULL(u.NumeroEconomico + CASE WHEN NULLIF(u.Placas,N'') IS NULL THEN N'' ELSE N' - '+u.Placas END,N'')
        ELSE ISNULL(v.UnidadExterna,N'')
    END AS Unidad,
    CASE
        WHEN v.TipoTransporte=N'Interno' THEN ISNULL(v.OperadorTexto,N'')
        ELSE ISNULL(v.ChoferExterno,N'')
    END AS Operador
FROM dbo.Logistica_Viajes v
LEFT JOIN dbo.Logistica_Unidades u ON u.UnidadID=v.UnidadID
WHERE v.Activo=1
  AND v.FechaProgramada>=@Desde
  AND v.FechaProgramada<DATEADD(DAY,1,@Hasta)
  AND (@Estatus IS NULL OR v.Estatus=@Estatus)
  AND (@TipoTransporte IS NULL OR v.TipoTransporte=@TipoTransporte)
  AND
  (
      @Q IS NULL
      OR v.Folio LIKE N'%'+@Q+N'%'
      OR v.Origen LIKE N'%'+@Q+N'%'
      OR v.Destino LIKE N'%'+@Q+N'%'
      OR v.Motivo LIKE N'%'+@Q+N'%'
      OR v.OperadorTexto LIKE N'%'+@Q+N'%'
      OR v.ChoferExterno LIKE N'%'+@Q+N'%'
  )
ORDER BY v.FechaProgramada,v.HoraSalidaProgramada,v.ViajeID;";

        var eventos = new List<object>();

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@Desde", SqlDbType.Date).Value = desde;
        cmd.Parameters.Add("@Hasta", SqlDbType.Date).Value = hasta;
        cmd.Parameters.Add("@Estatus", SqlDbType.NVarChar, 30).Value = Db(estatus);
        cmd.Parameters.Add("@TipoTransporte", SqlDbType.NVarChar, 30).Value = Db(tipoTransporte);
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
                tipoTransporte = Texto(rd, "TipoTransporte"),
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

        return Json(new
        {
            ok = true,
            desde = desde.ToString("yyyy-MM-dd"),
            hasta = hasta.ToString("yyyy-MM-dd"),
            total = eventos.Count,
            eventos
        });
    }
    private void Normalizar(LogisticaViajeCrearVm model)
    {
        model.TipoViaje = NormalizarTipoViaje(model.TipoViaje, permitirVacio: false);
        model.TipoTransporte = NormalizarTipoTransporte(model.TipoTransporte, permitirVacio: false);
        model.Origen = model.Origen?.Trim() ?? string.Empty;
        model.Destino = model.Destino?.Trim() ?? string.Empty;
        model.Motivo = model.Motivo?.Trim() ?? string.Empty;
        model.OperadorTexto = model.OperadorTexto?.Trim();
        model.TransportistaExterno = model.TransportistaExterno?.Trim();
        model.UnidadExterna = model.UnidadExterna?.Trim();
        model.PlacasExternas = model.PlacasExternas?.Trim();
        model.ChoferExterno = model.ChoferExterno?.Trim();
        model.Observaciones = model.Observaciones?.Trim();
        if (model.TipoTransporte == "Interno")
        {
            model.TransportistaExterno = null;
            model.UnidadExterna = null;
            model.PlacasExternas = null;
            model.ChoferExterno = null;
        }
        else if (model.TipoTransporte == "Externo")
        {
            model.UnidadID = null;
            model.OperadorUsuarioID = null;
            model.OperadorTexto = null;
        }
    }

    private void Normalizar(LogisticaViajeEditarVm model)
    {
        model.TipoViaje = NormalizarTipoViaje(model.TipoViaje, permitirVacio: false);
        model.TipoTransporte = NormalizarTipoTransporte(model.TipoTransporte, permitirVacio: false);
        model.Origen = model.Origen?.Trim() ?? string.Empty;
        model.Destino = model.Destino?.Trim() ?? string.Empty;
        model.Motivo = model.Motivo?.Trim() ?? string.Empty;
        model.OperadorTexto = model.OperadorTexto?.Trim();
        model.TransportistaExterno = model.TransportistaExterno?.Trim();
        model.UnidadExterna = model.UnidadExterna?.Trim();
        model.PlacasExternas = model.PlacasExternas?.Trim();
        model.ChoferExterno = model.ChoferExterno?.Trim();
        model.Observaciones = model.Observaciones?.Trim();
        if (model.TipoTransporte == "Interno")
        {
            model.TransportistaExterno = null;
            model.UnidadExterna = null;
            model.PlacasExternas = null;
            model.ChoferExterno = null;
        }
        else if (model.TipoTransporte == "Externo")
        {
            model.UnidadID = null;
            model.OperadorUsuarioID = null;
            model.OperadorTexto = null;
        }
    }

    private void ValidarModelo(LogisticaViajeCrearVm model)
    {
        if (string.IsNullOrWhiteSpace(model.TipoViaje)) ModelState.AddModelError(nameof(model.TipoViaje), "Selecciona un tipo de viaje válido.");
        if (string.IsNullOrWhiteSpace(model.TipoTransporte)) ModelState.AddModelError(nameof(model.TipoTransporte), "Selecciona un tipo de transporte válido.");
        if (string.IsNullOrWhiteSpace(model.Origen)) ModelState.AddModelError(nameof(model.Origen), "El origen es obligatorio.");
        if (string.IsNullOrWhiteSpace(model.Destino)) ModelState.AddModelError(nameof(model.Destino), "El destino es obligatorio.");
        if (string.IsNullOrWhiteSpace(model.Motivo)) ModelState.AddModelError(nameof(model.Motivo), "El motivo del viaje es obligatorio.");
        if (model.FechaProgramada == default) ModelState.AddModelError(nameof(model.FechaProgramada), "La fecha programada es obligatoria.");
    }

    private void ValidarModelo(LogisticaViajeEditarVm model)
    {
        if (model.ViajeID <= 0) ModelState.AddModelError(nameof(model.ViajeID), "El viaje no es válido.");
        if (string.IsNullOrWhiteSpace(model.TipoViaje)) ModelState.AddModelError(nameof(model.TipoViaje), "Selecciona un tipo de viaje válido.");
        if (string.IsNullOrWhiteSpace(model.TipoTransporte)) ModelState.AddModelError(nameof(model.TipoTransporte), "Selecciona un tipo de transporte válido.");
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
SELECT TipoViaje,TipoTransporte,Origen,Destino,Motivo,FechaProgramada,HoraSalidaProgramada,RutaID,UnidadID,OperadorUsuarioID,
TransportistaExterno,UnidadExterna,PlacasExternas,ChoferExterno,Estatus
FROM dbo.Logistica_Viajes WITH(UPDLOCK,HOLDLOCK)
WHERE ViajeID=@ViajeID AND Activo=1;";
        string estatus, tipoViaje, tipoTransporte, origen, destino, motivo, transportista, unidadExterna, placasExternas, choferExterno;
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
            tipoTransporte = Texto(rd, "TipoTransporte");
            origen = Texto(rd, "Origen");
            destino = Texto(rd, "Destino");
            motivo = Texto(rd, "Motivo");
            fechaProgramada = Fecha(rd, "FechaProgramada");
            horaSalida = Hora(rd, "HoraSalidaProgramada");
            rutaId = EnteroNullable(rd, "RutaID");
            unidadId = EnteroNullable(rd, "UnidadID");
            operadorUsuarioId = EnteroNullable(rd, "OperadorUsuarioID");
            transportista = Texto(rd, "TransportistaExterno");
            unidadExterna = Texto(rd, "UnidadExterna");
            placasExternas = Texto(rd, "PlacasExternas");
            choferExterno = Texto(rd, "ChoferExterno");
        }
        if (estatus != "Programado") throw new InvalidOperationException("Solo un viaje Programado puede iniciar.");
        var faltantes = new List<string>();
        if (string.IsNullOrWhiteSpace(tipoViaje)) faltantes.Add("tipo de viaje");
        if (string.IsNullOrWhiteSpace(tipoTransporte)) faltantes.Add("tipo de transporte");
        if (string.IsNullOrWhiteSpace(origen)) faltantes.Add("origen");
        if (string.IsNullOrWhiteSpace(destino)) faltantes.Add("destino");
        if (string.IsNullOrWhiteSpace(motivo)) faltantes.Add("motivo");
        if (!fechaProgramada.HasValue) faltantes.Add("fecha programada");
        if (!horaSalida.HasValue) faltantes.Add("hora de salida programada");
        if (tipoTransporte == "Interno")
        {
            if (!rutaId.HasValue || rutaId.Value <= 0) faltantes.Add("ruta");
            if (!unidadId.HasValue || unidadId.Value <= 0) faltantes.Add("unidad");
            if (!operadorUsuarioId.HasValue || operadorUsuarioId.Value <= 0) faltantes.Add("operador");
            if (faltantes.Count == 0)
            {
                const string sqlRuta = @"SELECT COUNT_BIG(*) FROM dbo.Logistica_Rutas WHERE RutaID=@RutaID AND Activo=1;";
                await using (var cmd = new SqlCommand(sqlRuta, cn, tx))
                {
                    cmd.Parameters.Add("@RutaID", SqlDbType.Int).Value = rutaId!.Value;
                    if (Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken)) <= 0) faltantes.Add("ruta activa");
                }
                const string sqlUnidad = @"SELECT COUNT_BIG(*) FROM dbo.Logistica_Unidades WHERE UnidadID=@UnidadID AND Activo=1;";
                await using (var cmd = new SqlCommand(sqlUnidad, cn, tx))
                {
                    cmd.Parameters.Add("@UnidadID", SqlDbType.Int).Value = unidadId!.Value;
                    if (Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken)) <= 0) faltantes.Add("unidad activa");
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
                    if (Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken)) <= 0) faltantes.Add("operador activo de Logística con puesto Chofer");
                }
            }
        }
        else if (tipoTransporte == "Externo")
        {
            if (string.IsNullOrWhiteSpace(transportista)) faltantes.Add("transportista");
            if (string.IsNullOrWhiteSpace(unidadExterna)) faltantes.Add("unidad externa");
            if (string.IsNullOrWhiteSpace(placasExternas)) faltantes.Add("placas");
            if (string.IsNullOrWhiteSpace(choferExterno)) faltantes.Add("chofer");
        }
        else faltantes.Add("tipo de transporte");
        if (faltantes.Count > 0) throw new InvalidOperationException($"El viaje todavía no está listo para iniciar. Completa o corrige: {string.Join(", ", faltantes.Distinct())}.");
    }
    private static string NormalizarTipoViaje(string? valor, bool permitirVacio)
    {
        valor = valor?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(valor)) return string.Empty;
        var permitidos = new[]
        {
        "Entrega PT",
        "Ruta de personal",
        "Recolección de MP",
        "Traslado entre plantas",
        "Recolección de material",
        "Entrega / recolección especial",
        "Otro"
    };
        return permitidos.FirstOrDefault(x => string.Equals(x, valor, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
    }

    private static string NormalizarTipoTransporte(string? valor, bool permitirVacio)
    {
        valor = valor?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(valor))
            return permitirVacio ? string.Empty : string.Empty;

        if (valor.Equals("Interno", StringComparison.OrdinalIgnoreCase))
            return "Interno";

        if (valor.Equals("Externo", StringComparison.OrdinalIgnoreCase))
            return "Externo";

        return string.Empty;
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
