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

    public LogisticaViajesController(IConfiguration configuration, IServicioAcceso acceso)
    {
        _configuration = configuration;
        _acceso = acceso;
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
            if (model.TipoTransporte == "Interno")
                await ValidarRecursosInternosAsync(cn, tx, model.UnidadID, model.OperadorTexto, cancellationToken);

            const string sql = @"
INSERT dbo.Logistica_Viajes
(
    Folio,TipoViaje,TipoTransporte,Origen,Destino,Motivo,
    FechaProgramada,HoraSalidaProgramada,HoraRegresoProgramada,
    RutaID,UnidadID,OperadorTexto,
    TransportistaExterno,UnidadExterna,PlacasExternas,ChoferExterno,
    Estatus,TieneIncidencia,Observaciones,
    ResponsableUsuarioID,ResponsableNombreSnapshot,
    FechaCreacion,CreadoPor,Activo
)
VALUES
(
    NULL,@TipoViaje,@TipoTransporte,@Origen,@Destino,@Motivo,
    @FechaProgramada,@HoraSalidaProgramada,@HoraRegresoProgramada,
    @RutaID,@UnidadID,@OperadorTexto,
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
                cmd.Parameters.Add("@OperadorTexto", SqlDbType.NVarChar, 200).Value = model.TipoTransporte == "Interno" ? Db(model.OperadorTexto) : DBNull.Value;
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

            await EjecutarAsync(
                cn,
                tx,
                "UPDATE dbo.Logistica_Viajes SET Folio=@Folio WHERE ViajeID=@ViajeID;",
                cancellationToken,
                ("@Folio", folio),
                ("@ViajeID", viajeId));

            await InsertarHistorialAsync(
                cn,
                tx,
                viajeId,
                "VIAJE_CREADO",
                null,
                "Programado",
                $"Viaje creado. Tipo: {model.TipoViaje}. Transporte: {model.TipoTransporte}. Origen: {model.Origen}. Destino: {model.Destino}.",
                cancellationToken);

            await tx.CommitAsync(cancellationToken);

            TempData["LogisticaOk"] = $"{folio} creado correctamente.";
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
        
        if (id <= 0) return NotFound();

        await using var cn = await AbrirAsync(cancellationToken);

        const string sql = @"
SELECT
    ViajeID,TipoViaje,TipoTransporte,Origen,Destino,Motivo,
    FechaProgramada,HoraSalidaProgramada,HoraRegresoProgramada,
    RutaID,UnidadID,OperadorTexto,
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

            if (!await rd.ReadAsync(cancellationToken))
                return NotFound();

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
            var actual = await ObtenerViajeParaActualizarAsync(cn, tx, model.ViajeID, cancellationToken)
                ?? throw new InvalidOperationException("El viaje no existe.");

            if (actual.Estatus != "Programado")
                throw new InvalidOperationException("Solo los viajes Programados pueden editarse.");

            if (model.TipoTransporte == "Interno")
                await ValidarRecursosInternosAsync(cn, tx, model.UnidadID, model.OperadorTexto, cancellationToken);

            const string sql = @"
UPDATE dbo.Logistica_Viajes
SET TipoViaje=@TipoViaje,
    TipoTransporte=@TipoTransporte,
    Origen=@Origen,
    Destino=@Destino,
    Motivo=@Motivo,
    FechaProgramada=@FechaProgramada,
    HoraSalidaProgramada=@HoraSalidaProgramada,
    HoraRegresoProgramada=@HoraRegresoProgramada,
    RutaID=@RutaID,
    UnidadID=@UnidadID,
    OperadorTexto=@OperadorTexto,
    TransportistaExterno=@TransportistaExterno,
    UnidadExterna=@UnidadExterna,
    PlacasExternas=@PlacasExternas,
    ChoferExterno=@ChoferExterno,
    Observaciones=@Observaciones,
    FechaModificacion=SYSDATETIME(),
    ActualizadoPor=@Usuario
WHERE ViajeID=@ViajeID
  AND Activo=1
  AND Estatus=N'Programado';
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
                cmd.Parameters.Add("@OperadorTexto", SqlDbType.NVarChar, 200).Value = model.TipoTransporte == "Interno" ? Db(model.OperadorTexto) : DBNull.Value;
                cmd.Parameters.Add("@TransportistaExterno", SqlDbType.NVarChar, 200).Value = model.TipoTransporte == "Externo" ? Db(model.TransportistaExterno) : DBNull.Value;
                cmd.Parameters.Add("@UnidadExterna", SqlDbType.NVarChar, 100).Value = model.TipoTransporte == "Externo" ? Db(model.UnidadExterna) : DBNull.Value;
                cmd.Parameters.Add("@PlacasExternas", SqlDbType.NVarChar, 100).Value = model.TipoTransporte == "Externo" ? Db(model.PlacasExternas) : DBNull.Value;
                cmd.Parameters.Add("@ChoferExterno", SqlDbType.NVarChar, 200).Value = model.TipoTransporte == "Externo" ? Db(model.ChoferExterno) : DBNull.Value;
                cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1200).Value = Db(model.Observaciones);
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = model.ViajeID;

                if (Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) == 0)
                    throw new InvalidOperationException("El viaje cambió mientras se intentaba actualizar.");
            }

            await InsertarHistorialAsync(
                cn,
                tx,
                model.ViajeID,
                "VIAJE_EDITADO",
                "Programado",
                "Programado",
                $"Programación actualizada. Tipo: {model.TipoViaje}. Transporte: {model.TipoTransporte}. Origen: {model.Origen}. Destino: {model.Destino}.",
                cancellationToken);

            await tx.CommitAsync(cancellationToken);
            TempData["LogisticaOk"] = "Viaje actualizado correctamente.";

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
        
        if (model.ViajeID <= 0)
        {
            TempData["LogisticaError"] = "El viaje indicado no es válido.";
            return RedirectToAction(nameof(Index));
        }

        model.Observaciones = model.Observaciones?.Trim();

        if (model.FechaSalida == default)
            ModelState.AddModelError(nameof(model.FechaSalida), "La fecha de salida es obligatoria.");

        if (model.FechaSalida > DateTime.Now.AddMinutes(5))
            ModelState.AddModelError(nameof(model.FechaSalida), "La fecha de salida no puede estar en el futuro.");

        if (model.KilometrajeSalida.HasValue && model.KilometrajeSalida.Value < 0)
            ModelState.AddModelError(nameof(model.KilometrajeSalida), "El kilometraje no puede ser negativo.");

        if (!ModelState.IsValid)
        {
            TempData["LogisticaError"] = ObtenerErroresModelState();
            return RedirectToAction(nameof(Detalle), new { id = model.ViajeID });
        }

        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        try
        {
            var viaje = await ObtenerViajeParaActualizarAsync(cn, tx, model.ViajeID, cancellationToken)
                ?? throw new InvalidOperationException("El viaje no existe.");

            if (viaje.Estatus != "Programado")
                throw new InvalidOperationException("Solo un viaje Programado puede registrar salida.");

            const string sql = @"
UPDATE dbo.Logistica_Viajes
SET Estatus=N'En curso',
    FechaSalidaReal=@FechaSalida,
    KilometrajeSalida=@KilometrajeSalida,
    Observaciones=CASE
        WHEN @Observaciones IS NULL THEN Observaciones
        WHEN NULLIF(LTRIM(RTRIM(ISNULL(Observaciones,N''))),N'') IS NULL THEN @Observaciones
        ELSE CONCAT(Observaciones,NCHAR(13),NCHAR(10),@Observaciones)
    END,
    FechaModificacion=SYSDATETIME(),
    ActualizadoPor=@Usuario
WHERE ViajeID=@ViajeID
  AND Activo=1
  AND Estatus=N'Programado';
SELECT @@ROWCOUNT;";

            await using (var cmd = new SqlCommand(sql, cn, tx))
            {
                cmd.Parameters.Add("@FechaSalida", SqlDbType.DateTime2).Value = model.FechaSalida;
                cmd.Parameters.Add("@KilometrajeSalida", SqlDbType.Int).Value = Db(model.KilometrajeSalida);
                cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1000).Value = Db(model.Observaciones);
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = model.ViajeID;

                if (Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) == 0)
                    throw new InvalidOperationException("El viaje cambió mientras se registraba la salida.");
            }

            var historial = $"Salida registrada el {model.FechaSalida:dd/MM/yyyy HH:mm}.";
            if (model.KilometrajeSalida.HasValue)
                historial += $" Kilometraje: {model.KilometrajeSalida.Value:N0} km.";
            if (!string.IsNullOrWhiteSpace(model.Observaciones))
                historial += $" Observaciones: {model.Observaciones}";

            await InsertarHistorialAsync(
                cn,
                tx,
                model.ViajeID,
                "SALIDA_REGISTRADA",
                "Programado",
                "En curso",
                historial,
                cancellationToken);

            await tx.CommitAsync(cancellationToken);
            TempData["LogisticaOk"] = "Salida del viaje registrada correctamente.";
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
        

        if (model.ViajeID <= 0)
        {
            TempData["LogisticaError"] = "El viaje indicado no es válido.";
            return RedirectToAction(nameof(Index));
        }

        model.Observaciones = model.Observaciones?.Trim();

        if (model.FechaRegreso == default)
            ModelState.AddModelError(nameof(model.FechaRegreso), "La fecha de regreso es obligatoria.");

        if (model.FechaRegreso > DateTime.Now.AddMinutes(5))
            ModelState.AddModelError(nameof(model.FechaRegreso), "La fecha de regreso no puede estar en el futuro.");

        if (model.KilometrajeRegreso.HasValue && model.KilometrajeRegreso.Value < 0)
            ModelState.AddModelError(nameof(model.KilometrajeRegreso), "El kilometraje no puede ser negativo.");

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
SELECT
    Estatus,
    FechaSalidaReal,
    FechaRegresoReal,
    KilometrajeSalida
FROM dbo.Logistica_Viajes WITH (UPDLOCK,HOLDLOCK)
WHERE ViajeID=@ViajeID AND Activo=1;";

            string estatus;
            DateTime? fechaSalida;
            DateTime? fechaRegresoActual;
            int? kilometrajeSalida;

            await using (var cmd = new SqlCommand(sqlActual, cn, tx))
            {
                cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = model.ViajeID;
                await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);

                if (!await rd.ReadAsync(cancellationToken))
                    throw new InvalidOperationException("El viaje no existe.");

                estatus = Texto(rd, "Estatus");
                fechaSalida = Fecha(rd, "FechaSalidaReal");
                fechaRegresoActual = Fecha(rd, "FechaRegresoReal");
                kilometrajeSalida = EnteroNullable(rd, "KilometrajeSalida");
            }

            if (estatus != "En curso")
                throw new InvalidOperationException("Solo un viaje En curso puede registrar regreso.");

            if (!fechaSalida.HasValue)
                throw new InvalidOperationException("El viaje no tiene una salida registrada.");

            if (fechaRegresoActual.HasValue)
                throw new InvalidOperationException("El regreso ya fue registrado.");

            if (model.FechaRegreso < fechaSalida.Value)
                throw new InvalidOperationException("La fecha de regreso no puede ser anterior a la salida.");

            if (kilometrajeSalida.HasValue &&
                model.KilometrajeRegreso.HasValue &&
                model.KilometrajeRegreso.Value < kilometrajeSalida.Value)
            {
                throw new InvalidOperationException(
                    $"El kilometraje de regreso no puede ser menor al de salida ({kilometrajeSalida.Value:N0} km).");
            }

            const string sql = @"
UPDATE dbo.Logistica_Viajes
SET Estatus=N'Completado',
    FechaRegresoReal=@FechaRegreso,
    KilometrajeRegreso=@KilometrajeRegreso,
    Observaciones=CASE
        WHEN @Observaciones IS NULL THEN Observaciones
        WHEN NULLIF(LTRIM(RTRIM(ISNULL(Observaciones,N''))),N'') IS NULL THEN @Observaciones
        ELSE CONCAT(Observaciones,NCHAR(13),NCHAR(10),@Observaciones)
    END,
    FechaModificacion=SYSDATETIME(),
    ActualizadoPor=@Usuario
WHERE ViajeID=@ViajeID
  AND Activo=1
  AND Estatus=N'En curso'
  AND FechaRegresoReal IS NULL;
SELECT @@ROWCOUNT;";

            await using (var cmd = new SqlCommand(sql, cn, tx))
            {
                cmd.Parameters.Add("@FechaRegreso", SqlDbType.DateTime2).Value = model.FechaRegreso;
                cmd.Parameters.Add("@KilometrajeRegreso", SqlDbType.Int).Value = Db(model.KilometrajeRegreso);
                cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1000).Value = Db(model.Observaciones);
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = model.ViajeID;

                if (Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) == 0)
                    throw new InvalidOperationException("El viaje cambió mientras se registraba el regreso.");
            }

            var historial = $"Regreso registrado el {model.FechaRegreso:dd/MM/yyyy HH:mm}.";
            if (model.KilometrajeRegreso.HasValue)
                historial += $" Kilometraje: {model.KilometrajeRegreso.Value:N0} km.";
            if (!string.IsNullOrWhiteSpace(model.Observaciones))
                historial += $" Observaciones: {model.Observaciones}";

            await InsertarHistorialAsync(
                cn,
                tx,
                model.ViajeID,
                "RETORNO_REGISTRADO",
                "En curso",
                "Completado",
                historial,
                cancellationToken);

            await tx.CommitAsync(cancellationToken);
            TempData["LogisticaOk"] = "Regreso registrado y viaje completado.";
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

    private async Task<LogisticaViajeDetalleVm?> CargarDetalleAsync(
        SqlConnection cn,
        int viajeId,
        CancellationToken cancellationToken)
    {
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
    v.HoraRegresoProgramada,
    v.FechaSalidaReal,
    v.FechaRegresoReal,
    v.RutaID,
    ISNULL(r.Codigo + N' - ' + r.Nombre,N'') AS Ruta,
    v.UnidadID,
    ISNULL(u.NumeroEconomico + CASE WHEN NULLIF(u.Placas,N'') IS NULL THEN N'' ELSE N' - '+u.Placas END,N'') AS Unidad,
    ISNULL(v.OperadorTexto,N'') AS Operador,
    ISNULL(v.TransportistaExterno,N'') AS TransportistaExterno,
    ISNULL(v.UnidadExterna,N'') AS UnidadExterna,
    ISNULL(v.PlacasExternas,N'') AS PlacasExternas,
    ISNULL(v.ChoferExterno,N'') AS ChoferExterno,
    ISNULL(v.Estatus,N'') AS Estatus,
    ISNULL(v.Observaciones,N'') AS Observaciones,
    ISNULL(v.TieneIncidencia,0) AS TieneIncidencia,
    v.KilometrajeSalida,
    v.KilometrajeRegreso,
    v.ResponsableUsuarioID,
    ISNULL(v.ResponsableNombreSnapshot,N'') AS UsuarioResponsable,
    v.FechaCreacion,
    ISNULL(v.CreadoPor,N'') AS CreadoPor
FROM dbo.Logistica_Viajes v
LEFT JOIN dbo.Logistica_Rutas r ON r.RutaID=v.RutaID
LEFT JOIN dbo.Logistica_Unidades u ON u.UnidadID=v.UnidadID
WHERE v.ViajeID=@ViajeID
  AND v.Activo=1;";

        LogisticaViajeDetalleVm? vm = null;

        await using (var cmd = new SqlCommand(sql, cn))
        {
            cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;

            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);

            if (!await rd.ReadAsync(cancellationToken))
                return null;

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
                UsuarioResponsableID = EnteroNullable(rd, "ResponsableUsuarioID"),
                UsuarioResponsable = Texto(rd, "UsuarioResponsable"),
                FechaCreacion = Fecha(rd, "FechaCreacion") ?? DateTime.MinValue,
                CreadoPor = Texto(rd, "CreadoPor")
            };
        }

        const string sqlHistorial = @"
SELECT HistorialID,ViajeID,Evento,ISNULL(EstadoAnterior,N'') AS EstadoAnterior,
       ISNULL(EstadoNuevo,N'') AS EstadoNuevo,ISNULL(Observaciones,N'') AS Observaciones,
       UsuarioID,ISNULL(UsuarioNombre,N'') AS Usuario,FechaEvento
FROM dbo.Logistica_ViajeHistorial
WHERE ViajeID=@ViajeID
ORDER BY FechaEvento DESC,HistorialID DESC;";

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
SELECT
    ViajeIncidenciaID,ViajeID,Tipo,Severidad,Descripcion,Estatus,
    ISNULL(Responsable,N'') AS Responsable,FechaRegistro,FechaCierre
FROM dbo.Logistica_ViajeIncidencias
WHERE ViajeID=@ViajeID
  AND Activo=1
ORDER BY
    CASE WHEN Estatus IN(N'Abierta',N'En seguimiento') THEN 0 ELSE 1 END,
    CASE Severidad
        WHEN N'Crítica' THEN 1
        WHEN N'Alta' THEN 2
        WHEN N'Media' THEN 3
        WHEN N'Baja' THEN 4
        ELSE 5
    END,
    FechaRegistro DESC,
    ViajeIncidenciaID DESC;";

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

        vm.TieneIncidencia = vm.Incidencias.Any(x => x.EstaAbierta);
        return vm;
    }

    private async Task CargarCatalogosAsync(
        LogisticaViajeCrearVm vm,
        SqlConnection cn,
        CancellationToken cancellationToken)
    {
        vm.Rutas.Clear();
        vm.Unidades.Clear();

        const string sql = @"
SELECT RutaID,Codigo + N' - ' + Nombre AS Texto
FROM dbo.Logistica_Rutas
WHERE Activo=1
ORDER BY Codigo;

SELECT UnidadID,NumeroEconomico + CASE WHEN NULLIF(Placas,N'') IS NULL THEN N'' ELSE N' - '+Placas END AS Texto
FROM dbo.Logistica_Unidades
WHERE Activo=1
ORDER BY NumeroEconomico;";

        await using var cmd = new SqlCommand(sql, cn);
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);

        while (await rd.ReadAsync(cancellationToken))
            vm.Rutas.Add(new LogisticaViajeSelectVm { Id = Entero(rd, "RutaID"), Texto = Texto(rd, "Texto") });

        if (await rd.NextResultAsync(cancellationToken))
        {
            while (await rd.ReadAsync(cancellationToken))
                vm.Unidades.Add(new LogisticaViajeSelectVm { Id = Entero(rd, "UnidadID"), Texto = Texto(rd, "Texto") });
        }
    }

    private async Task CargarCatalogosAsync(
        LogisticaViajeEditarVm vm,
        SqlConnection cn,
        CancellationToken cancellationToken)
    {
        vm.Rutas.Clear();
        vm.Unidades.Clear();

        const string sql = @"
SELECT RutaID,Codigo + N' - ' + Nombre AS Texto
FROM dbo.Logistica_Rutas
WHERE Activo=1
ORDER BY Codigo;

SELECT UnidadID,NumeroEconomico + CASE WHEN NULLIF(Placas,N'') IS NULL THEN N'' ELSE N' - '+Placas END AS Texto
FROM dbo.Logistica_Unidades
WHERE Activo=1
ORDER BY NumeroEconomico;";

        await using var cmd = new SqlCommand(sql, cn);
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);

        while (await rd.ReadAsync(cancellationToken))
            vm.Rutas.Add(new LogisticaViajeSelectVm { Id = Entero(rd, "RutaID"), Texto = Texto(rd, "Texto") });

        if (await rd.NextResultAsync(cancellationToken))
        {
            while (await rd.ReadAsync(cancellationToken))
                vm.Unidades.Add(new LogisticaViajeSelectVm { Id = Entero(rd, "UnidadID"), Texto = Texto(rd, "Texto") });
        }
    }

    private static async Task ValidarRecursosInternosAsync(
        SqlConnection cn,
        SqlTransaction tx,
        int? unidadId,
        string? operador,
        CancellationToken cancellationToken)
    {
        if (!unidadId.HasValue || unidadId.Value <= 0)
            throw new InvalidOperationException("Selecciona una unidad interna válida.");

        if (string.IsNullOrWhiteSpace(operador))
            throw new InvalidOperationException("Captura el operador/chofer del viaje.");

        const string sqlUnidad = @"
SELECT COUNT_BIG(*)
FROM dbo.Logistica_Unidades WITH (UPDLOCK,HOLDLOCK)
WHERE UnidadID=@UnidadID AND Activo=1;";

        await using var cmd = new SqlCommand(sqlUnidad, cn, tx);
        cmd.Parameters.Add("@UnidadID", SqlDbType.Int).Value = unidadId.Value;

        if (Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken)) <= 0)
            throw new InvalidOperationException("La unidad seleccionada no existe o se encuentra inactiva.");
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
            model.OperadorTexto = null;
        }
    }

    private void ValidarModelo(LogisticaViajeCrearVm model)
    {
        if (string.IsNullOrWhiteSpace(model.TipoViaje))
            ModelState.AddModelError(nameof(model.TipoViaje), "Selecciona un tipo de viaje válido.");

        if (string.IsNullOrWhiteSpace(model.TipoTransporte))
            ModelState.AddModelError(nameof(model.TipoTransporte), "Selecciona un tipo de transporte válido.");

        if (string.IsNullOrWhiteSpace(model.Origen))
            ModelState.AddModelError(nameof(model.Origen), "El origen es obligatorio.");

        if (string.IsNullOrWhiteSpace(model.Destino))
            ModelState.AddModelError(nameof(model.Destino), "El destino es obligatorio.");

        if (string.IsNullOrWhiteSpace(model.Motivo))
            ModelState.AddModelError(nameof(model.Motivo), "El motivo del viaje es obligatorio.");

        if (model.FechaProgramada == default)
            ModelState.AddModelError(nameof(model.FechaProgramada), "La fecha programada es obligatoria.");

        if (model.TipoTransporte == "Interno")
        {
            if (!model.UnidadID.HasValue || model.UnidadID.Value <= 0)
                ModelState.AddModelError(nameof(model.UnidadID), "Selecciona una unidad interna.");

            if (string.IsNullOrWhiteSpace(model.OperadorTexto))
                ModelState.AddModelError(nameof(model.OperadorTexto), "Captura el operador/chofer.");
        }
    }

    private void ValidarModelo(LogisticaViajeEditarVm model)
    {
        if (model.ViajeID <= 0)
            ModelState.AddModelError(nameof(model.ViajeID), "El viaje no es válido.");

        if (string.IsNullOrWhiteSpace(model.TipoViaje))
            ModelState.AddModelError(nameof(model.TipoViaje), "Selecciona un tipo de viaje válido.");

        if (string.IsNullOrWhiteSpace(model.TipoTransporte))
            ModelState.AddModelError(nameof(model.TipoTransporte), "Selecciona un tipo de transporte válido.");

        if (string.IsNullOrWhiteSpace(model.Origen))
            ModelState.AddModelError(nameof(model.Origen), "El origen es obligatorio.");

        if (string.IsNullOrWhiteSpace(model.Destino))
            ModelState.AddModelError(nameof(model.Destino), "El destino es obligatorio.");

        if (string.IsNullOrWhiteSpace(model.Motivo))
            ModelState.AddModelError(nameof(model.Motivo), "El motivo del viaje es obligatorio.");

        if (model.FechaProgramada == default)
            ModelState.AddModelError(nameof(model.FechaProgramada), "La fecha programada es obligatoria.");

        if (model.TipoTransporte == "Interno")
        {
            if (!model.UnidadID.HasValue || model.UnidadID.Value <= 0)
                ModelState.AddModelError(nameof(model.UnidadID), "Selecciona una unidad interna.");

            if (string.IsNullOrWhiteSpace(model.OperadorTexto))
                ModelState.AddModelError(nameof(model.OperadorTexto), "Captura el operador/chofer.");
        }
    }

    private static string NormalizarTipoViaje(string? valor, bool permitirVacio)
    {
        valor = valor?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(valor))
            return permitirVacio ? string.Empty : string.Empty;

        var permitidos = new[]
        {
            "Ruta de personal",
            "Recolección de MP",
            "Traslado entre plantas",
            "Recolección de material",
            "Entrega / recolección especial",
            "Otro"
        };

        return permitidos.FirstOrDefault(x =>
            string.Equals(x, valor, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
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
