// LOGISTICA_UX_RAPIDA_V1_4
using ERP.NSQuell.Models.ViewModels.Logistica;
using ERP.NSQuell.Servicios;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LogisticaController : Controller
{
    private readonly IConfiguration _configuration;
    private readonly IServicioAcceso _acceso;

    public LogisticaController(IConfiguration configuration, IServicioAcceso acceso)
    {
        _configuration = configuration;
        _acceso = acceso;
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
    public async Task<IActionResult> Index(
        string? q,
        DateTime? fechaDesde,
        DateTime? fechaHasta,
        string? estatus,
        CancellationToken cancellationToken)
    {
        var acceso = await ValidarAccesoAsync("Tablero de Logística");
        if (acceso != null) return acceso;

        var vm = new LogisticaIndexVm
        {
            Busqueda = q?.Trim(),
            FechaDesde = fechaDesde?.Date,
            FechaHasta = fechaHasta?.Date,
            Estatus = estatus?.Trim()
        };

        await using var cn = await AbrirAsync(cancellationToken);

        if (!await TieneFase1Async(cn, cancellationToken))
        {
            ViewBag.ErrorConfiguracion = "Falta ejecutar 29_LOGISTICA_FASE1_RELEASE_TEST_v1.0.sql.";
            return View(vm);
        }

        vm.Demandas = await CargarDemandasAsync(
            cn,
            vm.Busqueda,
            vm.FechaDesde,
            vm.FechaHasta,
            true,
            null,
            cancellationToken);

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
WHERE
    (@Estatus IS NULL OR Estatus = @Estatus)
    AND
    (
        @Q IS NULL
        OR Folio LIKE N'%' + @Q + N'%'
        OR ClienteNombreSnapshot LIKE N'%' + @Q + N'%'
        OR Destino LIKE N'%' + @Q + N'%'
    )
    AND (@FechaDesde IS NULL OR COALESCE(FechaCargaProgramada,FechaProgramada) >= @FechaDesde)
    AND (@FechaHasta IS NULL OR COALESCE(FechaEntregaProgramada,FechaProgramada) <= @FechaHasta)
ORDER BY
    CASE WHEN Estatus IN(N'Entregado',N'Cancelado') THEN 1 ELSE 0 END,
    COALESCE(FechaCargaProgramada,FechaProgramada),
    EmbarqueID DESC;";

        await using (var cmd = new SqlCommand(sqlEmbarques, cn))
        {
            cmd.Parameters.Add("@Q", SqlDbType.NVarChar, 250).Value =
                string.IsNullOrWhiteSpace(vm.Busqueda) ? DBNull.Value : vm.Busqueda;
            cmd.Parameters.Add("@Estatus", SqlDbType.NVarChar, 20).Value =
                string.IsNullOrWhiteSpace(vm.Estatus) ? DBNull.Value : vm.Estatus;
            cmd.Parameters.Add("@FechaDesde", SqlDbType.Date).Value =
                vm.FechaDesde.HasValue ? vm.FechaDesde.Value.Date : DBNull.Value;
            cmd.Parameters.Add("@FechaHasta", SqlDbType.Date).Value =
                vm.FechaHasta.HasValue ? vm.FechaHasta.Value.Date : DBNull.Value;

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
        vm.CargasHoy = vm.Embarques.Count(x => x.FechaCargaProgramada?.Date == DateTime.Today);
        vm.EntregasAtrasadas = vm.Embarques.Count(x =>
            x.Estatus is not "Entregado" and not "Cancelado"
            && x.FechaEntregaProgramada.HasValue
            && x.FechaEntregaProgramada.Value.Date < DateTime.Today);

        return View(vm);
    }

    [HttpGet]
    // LOGISTICA_PROGRAMACION_RAPIDA_V1_4
    public async Task<IActionResult> Crear(int? releaseDetalleId, CancellationToken cancellationToken)
    {
        var acceso = await ValidarAccesoAsync("Nuevo embarque");
        if (acceso != null) return acceso;

        if (!releaseDetalleId.HasValue || releaseDetalleId.Value <= 0)
            return RedirectToAction(nameof(Index));

        await using var cn = await AbrirAsync(cancellationToken);
        if (!await TieneFase1Async(cn, cancellationToken))
        {
            TempData["LogisticaError"] = "Falta la estructura de Logistica Fase 1 en TEST.";
            return RedirectToAction(nameof(Index));
        }

        var demanda = await ObtenerDemandaAsync(
            cn,
            releaseDetalleId.Value,
            null,
            cancellationToken);

        if (demanda == null || demanda.PendienteProgramar <= 0)
        {
            TempData["LogisticaError"] = "La salida seleccionada ya no tiene cantidad pendiente por programar.";
            return RedirectToAction(nameof(Index));
        }

        var vm = new LogisticaCrearVm
        {
            ReleaseDetalleID = demanda.ReleaseDetalleID,
            CantidadSolicitada = demanda.PendienteProgramar,
            Destino = demanda.Cliente,
            FechaCargaProgramada = demanda.FechaCarga?.Date ?? demanda.FechaEntrega.Date.AddDays(-1),
            FechaEntregaProgramada = demanda.FechaEntrega.Date,
            Demanda = demanda
        };

        vm.CajasDisponibles = await CargarCajasParaDemandaAsync(
            cn,
            demanda,
            cancellationToken);

        vm.CajaIDs = SeleccionarCajasSugeridas(
            vm.CajasDisponibles,
            demanda.PendienteProgramar);

        await CargarCatalogosAsync(vm, cn, cancellationToken);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Crear(LogisticaCrearVm vm, CancellationToken cancellationToken)
    {
        var acceso = await ValidarAccesoAsync("Nuevo embarque");
        if (acceso != null) return acceso;

        if (vm.FechaEntregaProgramada.Date < vm.FechaCargaProgramada.Date)
            ModelState.AddModelError(nameof(vm.FechaEntregaProgramada), "La fecha de entrega no puede ser anterior a la carga.");

        await using var cn = await AbrirAsync(cancellationToken);
        var demanda = await ObtenerDemandaAsync(cn, vm.ReleaseDetalleID, null, cancellationToken);

        if (demanda == null)
        {
            ModelState.AddModelError(nameof(vm.ReleaseDetalleID), "La entrega del Release ya no esta disponible.");
        }
        else
        {
            vm.Demanda = demanda;
            if (vm.CantidadSolicitada <= 0 || vm.CantidadSolicitada > demanda.PendienteProgramar)
                ModelState.AddModelError(nameof(vm.CantidadSolicitada), $"La cantidad debe estar entre 1 y {demanda.PendienteProgramar:N0}.");
        }

        if (!ModelState.IsValid)
        {
            if (demanda != null)
            {
                vm.Demanda = demanda;
                vm.CajasDisponibles = await CargarCajasParaDemandaAsync(
                    cn,
                    demanda,
                    cancellationToken);
            }
            await CargarCatalogosAsync(vm, cn, cancellationToken);
            return View(vm);
        }

        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            demanda = await ObtenerDemandaAsync(cn, vm.ReleaseDetalleID, tx, cancellationToken)
                ?? throw new InvalidOperationException("La entrega del Release ya no esta disponible.");

            if (vm.CantidadSolicitada > demanda.PendienteProgramar)
                throw new InvalidOperationException("La cantidad pendiente del Release cambio mientras se guardaba.");
            if (!demanda.ClienteID.HasValue)
                throw new InvalidOperationException("El Release no tiene ClienteID.");

            const string sqlHeader = @"
INSERT dbo.Logistica_Embarques
(
    Folio,ClienteID,ClienteNombreSnapshot,Destino,DireccionEntrega,
    FechaProgramada,FechaCargaProgramada,HoraCargaProgramada,
    FechaEntregaProgramada,HoraEntregaProgramada,Estatus,RutaID,UnidadID,
    OperadorTexto,ResponsableUsuarioID,ResponsableNombreSnapshot,
    Observaciones,FechaCreacion,CreadoPor,Activo
)
VALUES
(
    NULL,@ClienteID,@ClienteNombre,@Destino,@DireccionEntrega,
    @FechaCargaProgramada,@FechaCargaProgramada,@HoraCargaProgramada,
    @FechaEntregaProgramada,@HoraEntregaProgramada,N'Programado',@RutaID,@UnidadID,
    @OperadorTexto,@UsuarioID,@UsuarioNombre,@Observaciones,SYSDATETIME(),@UsuarioNombre,1
);
SELECT CONVERT(int,SCOPE_IDENTITY());";

            int embarqueId;
            await using (var cmd = new SqlCommand(sqlHeader, cn, tx))
            {
                cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = demanda.ClienteID.Value;
                cmd.Parameters.Add("@ClienteNombre", SqlDbType.NVarChar, 200).Value = demanda.Cliente;
                cmd.Parameters.Add("@Destino", SqlDbType.NVarChar, 300).Value = vm.Destino.Trim();
                cmd.Parameters.Add("@DireccionEntrega", SqlDbType.NVarChar, 600).Value = Db(vm.DireccionEntrega?.Trim());
                cmd.Parameters.Add("@FechaCargaProgramada", SqlDbType.Date).Value = vm.FechaCargaProgramada.Date;
                cmd.Parameters.Add("@HoraCargaProgramada", SqlDbType.Time).Value = Db(vm.HoraCargaProgramada);
                cmd.Parameters.Add("@FechaEntregaProgramada", SqlDbType.Date).Value = vm.FechaEntregaProgramada.Date;
                cmd.Parameters.Add("@HoraEntregaProgramada", SqlDbType.Time).Value = Db(vm.HoraEntregaProgramada);
                cmd.Parameters.Add("@RutaID", SqlDbType.Int).Value = Db(vm.RutaID);
                cmd.Parameters.Add("@UnidadID", SqlDbType.Int).Value = Db(vm.UnidadID);
                cmd.Parameters.Add("@OperadorTexto", SqlDbType.NVarChar, 200).Value = Db(vm.OperadorTexto?.Trim());
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = Db(UsuarioID);
                cmd.Parameters.Add("@UsuarioNombre", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1200).Value = Db(vm.Observaciones?.Trim());
                embarqueId = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
            }

            var folio = $"LOG-{DateTime.Today:yyyy}-{embarqueId:000000}";
            await EjecutarAsync(
                cn,
                tx,
                "UPDATE dbo.Logistica_Embarques SET Folio=@Folio WHERE EmbarqueID=@EmbarqueID;",
                cancellationToken,
                ("@Folio", folio),
                ("@EmbarqueID", embarqueId));

            var embarqueDetalleId = await InsertarDetalleAsync(
                cn,
                tx,
                embarqueId,
                demanda,
                vm.CantidadSolicitada,
                cancellationToken);

            var cajasReservadas = await ReservarCajasInicialesAsync(
                cn,
                tx,
                embarqueId,
                embarqueDetalleId,
                demanda,
                vm.CajaIDs,
                vm.CantidadSolicitada,
                cancellationToken);

            var estadoInicial = cajasReservadas > 0
                ? "Preparando"
                : "Programado";

            if (cajasReservadas > 0)
            {
                await EjecutarAsync(
                    cn,
                    tx,
                    @"UPDATE dbo.Logistica_Embarques
SET Estatus=N'Preparando',FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
WHERE EmbarqueID=@EmbarqueID;",
                    cancellationToken,
                    ("@Usuario", UsuarioNombre),
                    ("@EmbarqueID", embarqueId));
            }

            await InsertarHistorialAsync(
                cn,
                tx,
                embarqueId,
                "PROGRAMACION_CREADA",
                null,
                estadoInicial,
                cajasReservadas > 0
                    ? $"Programacion creada desde Release {demanda.FolioRelease}. Se reservaron {cajasReservadas} lote(s) PT automaticamente."
                    : $"Programacion creada desde Release {demanda.FolioRelease}. Aun no habia lotes PT disponibles para reservar.",
                cancellationToken);

            await tx.CommitAsync(cancellationToken);
            TempData["LogisticaOk"] = cajasReservadas > 0
                ? $"{folio} programado con {cajasReservadas} lote(s) PT reservado(s)."
                : $"{folio} programado. Se enlazaran lotes PT cuando esten disponibles.";
            return RedirectToAction(nameof(Detalle), new { id = embarqueId });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            ModelState.AddModelError("", ex.Message);
            demanda = await ObtenerDemandaAsync(
                cn,
                vm.ReleaseDetalleID,
                null,
                cancellationToken);
            if (demanda != null)
            {
                vm.Demanda = demanda;
                vm.CajasDisponibles = await CargarCajasParaDemandaAsync(
                    cn,
                    demanda,
                    cancellationToken);
            }
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

        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var header = await ObtenerHeaderAsync(cn, tx, model.EmbarqueID, cancellationToken)
                ?? throw new InvalidOperationException("El embarque no existe.");
            if (header.Estatus is not "Programado" and not "Preparando")
                throw new InvalidOperationException("Solo se pueden agregar partidas mientras el embarque esta en preparacion.");

            var demanda = await ObtenerDemandaAsync(cn, model.ReleaseDetalleID, tx, cancellationToken)
                ?? throw new InvalidOperationException("La entrega del Release ya no esta disponible.");
            if (demanda.ClienteID != header.ClienteID)
                throw new InvalidOperationException("No se pueden mezclar clientes distintos en el mismo embarque.");
            if (model.CantidadSolicitada <= 0 || model.CantidadSolicitada > demanda.PendienteProgramar)
                throw new InvalidOperationException("La cantidad solicitada supera el pendiente del Release.");

            await InsertarDetalleAsync(cn, tx, model.EmbarqueID, demanda, model.CantidadSolicitada, cancellationToken);
            await EjecutarAsync(
                cn,
                tx,
                "UPDATE dbo.Logistica_Embarques SET Estatus=N'Preparando',FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario WHERE EmbarqueID=@Id;",
                cancellationToken,
                ("@Usuario", UsuarioNombre),
                ("@Id", model.EmbarqueID));
            await InsertarHistorialAsync(
                cn,
                tx,
                model.EmbarqueID,
                "PARTIDA_AGREGADA",
                header.Estatus,
                "Preparando",
                $"Se agrego Release {demanda.FolioRelease}, parte {demanda.NumeroParte}.",
                cancellationToken);

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

        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var header = await ObtenerHeaderAsync(cn, tx, embarqueId, cancellationToken)
                ?? throw new InvalidOperationException("El embarque no existe.");
            if (header.Estatus is not "Programado" and not "Preparando")
                throw new InvalidOperationException("Las cajas solo se pueden modificar durante la preparacion.");

            const string sqlDetalle = @"
SELECT
    d.ParteID,d.SolicitudProduccionID,d.CantidadSolicitada,
    ISNULL((SELECT SUM(ec.CantidadAsignada) FROM dbo.Logistica_EmbarqueCajas ec WHERE ec.EmbarqueDetalleID=d.EmbarqueDetalleID AND ec.Activo=1),0) AS CantidadAsignada
FROM dbo.Logistica_EmbarqueDetalle d WITH (UPDLOCK,HOLDLOCK)
WHERE d.EmbarqueDetalleID=@DetalleID AND d.EmbarqueID=@EmbarqueID AND d.Activo=1;";

            int parteId;
            int? solicitudId;
            int solicitado;
            int asignado;
            await using (var cmd = new SqlCommand(sqlDetalle, cn, tx))
            {
                cmd.Parameters.Add("@DetalleID", SqlDbType.Int).Value = embarqueDetalleId;
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
                if (!await rd.ReadAsync(cancellationToken))
                    throw new InvalidOperationException("La partida logistica no existe.");
                parteId = Entero(rd, "ParteID");
                solicitudId = EnteroNullable(rd, "SolicitudProduccionID");
                solicitado = Entero(rd, "CantidadSolicitada");
                asignado = Entero(rd, "CantidadAsignada");
            }

            if (!solicitudId.HasValue)
                throw new InvalidOperationException("La partida todavia no tiene OF. No se pueden reservar cajas.");

            await using (var lockCmd = new SqlCommand("SELECT CajaID FROM dbo.AlmacenPT_Cajas WITH (UPDLOCK,HOLDLOCK) WHERE CajaID=@CajaID AND Activo=1;", cn, tx))
            {
                lockCmd.Parameters.Add("@CajaID", SqlDbType.Int).Value = cajaId;
                var locked = await lockCmd.ExecuteScalarAsync(cancellationToken);
                if (locked == null || locked == DBNull.Value)
                    throw new InvalidOperationException("La caja no existe o ya no esta activa.");
            }

            const string sqlCaja = @"
SELECT TOP (1) CajaID,ParteID,SolicitudProduccionID,Disponible
FROM dbo.vw_Logistica_CajasDisponibles
WHERE CajaID=@CajaID;";

            int cajaParte;
            int? cajaSolicitud;
            int disponible;
            await using (var cmd = new SqlCommand(sqlCaja, cn, tx))
            {
                cmd.Parameters.Add("@CajaID", SqlDbType.Int).Value = cajaId;
                await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
                if (!await rd.ReadAsync(cancellationToken))
                    throw new InvalidOperationException("La caja ya no esta disponible para Logistica.");
                cajaParte = Entero(rd, "ParteID");
                cajaSolicitud = EnteroNullable(rd, "SolicitudProduccionID");
                disponible = Entero(rd, "Disponible");
            }

            if (cajaParte != parteId)
                throw new InvalidOperationException("La caja no corresponde a la parte de esta partida.");
            if (cajaSolicitud != solicitudId)
                throw new InvalidOperationException("La caja no corresponde a la OF de esta partida.");

            var pendiente = Math.Max(0, solicitado - asignado);
            if (disponible <= 0 || disponible > pendiente)
                throw new InvalidOperationException($"La caja tiene {disponible:N0} piezas y el pendiente es {pendiente:N0}. La Fase 1 trabaja con cajas completas.");

            const string sqlInsert = @"
INSERT dbo.Logistica_EmbarqueCajas
(
    EmbarqueID,EmbarqueDetalleID,CajaID,CantidadAsignada,EstatusSeleccion,
    FechaSeleccion,UsuarioSeleccionID,UsuarioSeleccionNombre,Activo,FechaCreacion,CreadoPor
)
VALUES
(
    @EmbarqueID,@DetalleID,@CajaID,@Cantidad,N'Reservada',SYSDATETIME(),
    @UsuarioID,@UsuarioNombre,1,SYSDATETIME(),@UsuarioNombre
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
                "UPDATE dbo.Logistica_Embarques SET Estatus=N'Preparando',FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario WHERE EmbarqueID=@Id;",
                cancellationToken,
                ("@Usuario", UsuarioNombre),
                ("@Id", embarqueId));

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

        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(cancellationToken);
        try
        {
            var header = await ObtenerHeaderAsync(cn, tx, embarqueId, cancellationToken)
                ?? throw new InvalidOperationException("El embarque no existe.");
            if (header.Estatus is not "Programado" and not "Preparando")
                throw new InvalidOperationException("Ya no se pueden liberar cajas en este estado.");

            const string sql = @"
UPDATE dbo.Logistica_EmbarqueCajas
SET Activo=0,EstatusSeleccion=N'Liberada',FechaLiberacion=SYSDATETIME(),
    UsuarioLiberacionID=@UsuarioID,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
WHERE EmbarqueCajaID=@CajaID AND EmbarqueID=@EmbarqueID AND Activo=1;
SELECT @@ROWCOUNT;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = Db(UsuarioID);
            cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
            cmd.Parameters.Add("@CajaID", SqlDbType.Int).Value = embarqueCajaId;
            cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
            var rows = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
            if (rows == 0)
                throw new InvalidOperationException("La caja ya no estaba reservada.");

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

        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var header = await ObtenerHeaderAsync(cn, tx, embarqueId, cancellationToken)
                ?? throw new InvalidOperationException("El embarque no existe.");
            if (header.Estatus is not "Programado" and not "Preparando")
                throw new InvalidOperationException("El embarque no esta en preparacion.");

            const string sqlValidacion = @"
SELECT COUNT_BIG(*)
FROM dbo.Logistica_EmbarqueDetalle d
OUTER APPLY
(
    SELECT SUM(ec.CantidadAsignada) AS Cantidad
    FROM dbo.Logistica_EmbarqueCajas ec
    WHERE ec.EmbarqueDetalleID=d.EmbarqueDetalleID AND ec.Activo=1
) c
WHERE d.EmbarqueID=@EmbarqueID AND d.Activo=1
  AND ISNULL(c.Cantidad,0)<>d.CantidadSolicitada;";

            await using (var cmd = new SqlCommand(sqlValidacion, cn, tx))
            {
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                var faltantes = Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken));
                if (faltantes > 0)
                    throw new InvalidOperationException("Todavia hay partidas cuya cantidad preparada no coincide con la cantidad programada.");
            }

            await EjecutarAsync(
                cn,
                tx,
                "UPDATE dbo.Logistica_Embarques SET Estatus=N'Preparado',FechaPreparacion=SYSDATETIME(),PreparadoPorUsuarioID=@UsuarioID,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario WHERE EmbarqueID=@Id;",
                cancellationToken,
                ("@UsuarioID", UsuarioID),
                ("@Usuario", UsuarioNombre),
                ("@Id", embarqueId));
            await InsertarHistorialAsync(
                cn,
                tx,
                embarqueId,
                "PREPARACION_COMPLETA",
                header.Estatus,
                "Preparado",
                "Todas las cajas requeridas quedaron preparadas.",
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

        try
        {
            await using var cn = await AbrirAsync(cancellationToken);
            await using var cmd = new SqlCommand("dbo.usp_Logistica_DespacharEmbarque", cn)
            {
                CommandType = CommandType.StoredProcedure
            };

            cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = Db(UsuarioID);
            cmd.Parameters.Add("@UsuarioNombre", SqlDbType.NVarChar, 200).Value = UsuarioNombre;

            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);

            if (!await rd.ReadAsync(cancellationToken))
                throw new InvalidOperationException("El procedimiento de despacho terminó sin devolver confirmación.");

            var referencia = Texto(rd, "ReferenciaOperacion");
            var yaDespachado = Booleano(rd, "YaDespachado");

            if (yaDespachado)
            {
                TempData["LogisticaOk"] = string.IsNullOrWhiteSpace(referencia)
                    ? "El embarque ya había sido despachado. No se generaron movimientos PT duplicados."
                    : $"El embarque ya había sido despachado. No se generaron movimientos PT duplicados. Referencia: {referencia}.";
            }
            else
            {
                TempData["LogisticaOk"] = string.IsNullOrWhiteSpace(referencia)
                    ? "Salida validada y PT descontado correctamente."
                    : $"Salida validada y PT descontado correctamente. Referencia: {referencia}.";
            }
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
    public async Task<IActionResult> Entregar(LogisticaEntregaVm model, CancellationToken cancellationToken)
    {
        var acceso = await ValidarAccesoAsync("Tablero de Logística");
        if (acceso != null) return acceso;

        if (!ModelState.IsValid)
        {
            TempData["LogisticaError"] = "Captura el nombre de quien recibe.";
            return RedirectToAction(nameof(Detalle), new { id = model.EmbarqueID });
        }

        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(cancellationToken);
        try
        {
            var header = await ObtenerHeaderAsync(cn, tx, model.EmbarqueID, cancellationToken)
                ?? throw new InvalidOperationException("El embarque no existe.");
            if (header.Estatus != "En ruta")
                throw new InvalidOperationException("Solo un embarque En ruta puede marcarse como Entregado.");

            await EjecutarAsync(
                cn,
                tx,
                @"
UPDATE dbo.Logistica_EmbarqueDetalle
SET CantidadEntregada=CantidadDespachada,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
WHERE EmbarqueID=@Id AND Activo=1;
UPDATE dbo.Logistica_Embarques
SET Estatus=N'Entregado',FechaEntrega=SYSDATETIME(),EntregaPorUsuarioID=@UsuarioID,
    ReceptorNombre=@Receptor,FolioRemision=@Remision,
    Observaciones=CASE WHEN @Obs IS NULL THEN Observaciones WHEN NULLIF(LTRIM(RTRIM(Observaciones)),N'') IS NULL THEN @Obs ELSE CONCAT(Observaciones,NCHAR(13),NCHAR(10),@Obs) END,
    FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
WHERE EmbarqueID=@Id;",
                cancellationToken,
                ("@Usuario", UsuarioNombre),
                ("@Id", model.EmbarqueID),
                ("@UsuarioID", UsuarioID),
                ("@Receptor", model.ReceptorNombre.Trim()),
                ("@Remision", model.FolioRemision),
                ("@Obs", model.Observaciones));
            await InsertarHistorialAsync(
                cn,
                tx,
                model.EmbarqueID,
                "ENTREGA_CONFIRMADA",
                "En ruta",
                "Entregado",
                $"Entrega confirmada a {model.ReceptorNombre.Trim()}.",
                cancellationToken);

            await tx.CommitAsync(cancellationToken);
            TempData["LogisticaOk"] = "Entrega confirmada.";
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

    [HttpGet]
    public async Task<IActionResult> Catalogos(CancellationToken cancellationToken)
    {
        var acceso = await ValidarAccesoAsync("Rutas y unidades");
        if (acceso != null) return acceso;

        await using var cn = await AbrirAsync(cancellationToken);
        var vm = new LogisticaCatalogosVm();
        const string sql = @"
SELECT RutaID,Codigo,Nombre,ISNULL(Descripcion,N'') AS Descripcion,Activo
FROM dbo.Logistica_Rutas ORDER BY Activo DESC,Codigo;
SELECT UnidadID,NumeroEconomico,ISNULL(Placas,N'') AS Placas,ISNULL(Marca,N'') AS Marca,
       ISNULL(Modelo,N'') AS Modelo,CapacidadPiezas,Activo
FROM dbo.Logistica_Unidades ORDER BY Activo DESC,NumeroEconomico;";

        await using var cmd = new SqlCommand(sql, cn);
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await rd.ReadAsync(cancellationToken))
        {
            vm.Rutas.Add(new LogisticaRutaVm
            {
                RutaID = Entero(rd, "RutaID"),
                Codigo = Texto(rd, "Codigo"),
                Nombre = Texto(rd, "Nombre"),
                Descripcion = Texto(rd, "Descripcion"),
                Activo = Booleano(rd, "Activo")
            });
        }
        if (await rd.NextResultAsync(cancellationToken))
        {
            while (await rd.ReadAsync(cancellationToken))
            {
                vm.Unidades.Add(new LogisticaUnidadVm
                {
                    UnidadID = Entero(rd, "UnidadID"),
                    NumeroEconomico = Texto(rd, "NumeroEconomico"),
                    Placas = Texto(rd, "Placas"),
                    Marca = Texto(rd, "Marca"),
                    Modelo = Texto(rd, "Modelo"),
                    CapacidadPiezas = EnteroNullable(rd, "CapacidadPiezas"),
                    Activo = Booleano(rd, "Activo")
                });
            }
        }
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarRuta(LogisticaRutaVm model, CancellationToken cancellationToken)
    {
        var acceso = await ValidarAccesoAsync("Rutas y unidades");
        if (acceso != null) return acceso;

        if (string.IsNullOrWhiteSpace(model.Codigo) || string.IsNullOrWhiteSpace(model.Nombre))
        {
            TempData["LogisticaError"] = "Codigo y nombre de ruta son obligatorios.";
            return RedirectToAction(nameof(Catalogos));
        }

        try
        {
            await using var cn = await AbrirAsync(cancellationToken);
            const string sql = @"
INSERT dbo.Logistica_Rutas(Codigo,Nombre,Descripcion,Activo,FechaCreacion,CreadoPor)
VALUES(@Codigo,@Nombre,@Descripcion,1,SYSDATETIME(),@Usuario);";
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@Codigo", SqlDbType.NVarChar, 30).Value = model.Codigo.Trim();
            cmd.Parameters.Add("@Nombre", SqlDbType.NVarChar, 150).Value = model.Nombre.Trim();
            cmd.Parameters.Add("@Descripcion", SqlDbType.NVarChar, 500).Value = Db(model.Descripcion?.Trim());
            cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 120).Value = UsuarioNombre;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            TempData["LogisticaOk"] = "Ruta creada.";
        }
        catch (Exception ex)
        {
            TempData["LogisticaError"] = ex.Message;
        }
        return RedirectToAction(nameof(Catalogos));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarUnidad(LogisticaUnidadVm model, CancellationToken cancellationToken)
    {
        var acceso = await ValidarAccesoAsync("Rutas y unidades");
        if (acceso != null) return acceso;

        if (string.IsNullOrWhiteSpace(model.NumeroEconomico))
        {
            TempData["LogisticaError"] = "El numero economico es obligatorio.";
            return RedirectToAction(nameof(Catalogos));
        }

        try
        {
            await using var cn = await AbrirAsync(cancellationToken);
            const string sql = @"
INSERT dbo.Logistica_Unidades(NumeroEconomico,Placas,Marca,Modelo,CapacidadPiezas,Activo,FechaCreacion,CreadoPor)
VALUES(@NumeroEconomico,@Placas,@Marca,@Modelo,@CapacidadPiezas,1,SYSDATETIME(),@Usuario);";
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@NumeroEconomico", SqlDbType.NVarChar, 50).Value = model.NumeroEconomico.Trim();
            cmd.Parameters.Add("@Placas", SqlDbType.NVarChar, 30).Value = Db(model.Placas?.Trim());
            cmd.Parameters.Add("@Marca", SqlDbType.NVarChar, 80).Value = Db(model.Marca?.Trim());
            cmd.Parameters.Add("@Modelo", SqlDbType.NVarChar, 80).Value = Db(model.Modelo?.Trim());
            cmd.Parameters.Add("@CapacidadPiezas", SqlDbType.Int).Value = Db(model.CapacidadPiezas);
            cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 120).Value = UsuarioNombre;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            TempData["LogisticaOk"] = "Unidad creada.";
        }
        catch (Exception ex)
        {
            TempData["LogisticaError"] = ex.Message;
        }
        return RedirectToAction(nameof(Catalogos));
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
  AND (@FechaDesde IS NULL OR FechaRequerida>=@FechaDesde)
  AND (@FechaHasta IS NULL OR FechaRequerida<=@FechaHasta)
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
SELECT e.EmbarqueID,ISNULL(e.Folio,N'') AS Folio,e.ClienteID,e.ClienteNombreSnapshot,e.Destino,
       ISNULL(e.DireccionEntrega,N'') AS DireccionEntrega,e.Estatus,e.FechaCargaProgramada,e.HoraCargaProgramada,
       e.FechaEntregaProgramada,e.HoraEntregaProgramada,
       ISNULL(r.Codigo + N' - ' + r.Nombre,N'') AS Ruta,
       ISNULL(u.NumeroEconomico + CASE WHEN NULLIF(u.Placas,N'') IS NULL THEN N'' ELSE N' - ' + u.Placas END,N'') AS Unidad,
       ISNULL(e.OperadorTexto,N'') AS Operador,ISNULL(e.Observaciones,N'') AS Observaciones,
       ISNULL(e.ReferenciaOperacion,N'') AS ReferenciaOperacion,
       e.FechaPreparacion,e.FechaCarga,e.FechaSalida,e.FechaEntrega,e.TieneIncidencia
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
       d.NumeroParteSnapshot,ISNULL(d.DescripcionParteSnapshot,N'') AS Descripcion,d.SolicitudProduccionID,
       ISNULL(d.NumeroOFSnapshot,N'') AS NumeroOF,d.FechaCargaReleaseSnapshot,d.FechaEntregaReleaseSnapshot,
       d.CantidadSolicitada,d.CantidadDespachada,
       ISNULL((SELECT SUM(ec.CantidadAsignada) FROM dbo.Logistica_EmbarqueCajas ec WHERE ec.EmbarqueDetalleID=d.EmbarqueDetalleID AND ec.Activo=1),0) AS CantidadAsignada
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
       ec.CantidadAsignada,ec.EstatusSeleccion
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
INNER JOIN dbo.vw_Logistica_CajasDisponibles c
    ON c.ParteID=d.ParteID AND c.SolicitudProduccionID=d.SolicitudProduccionID
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

        vm.DemandasDisponibles = await CargarDemandasAsync(
            cn,
            null,
            DateTime.Today.AddDays(-30),
            DateTime.Today.AddMonths(6),
            true,
            vm.ClienteID,
            cancellationToken);
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
        CalcularEstadoOperativo(vm);
        return vm;
    }

    private static void CalcularEstadoOperativo(LogisticaDetalleVm vm)
    {
        var ahora = DateTime.Now;

        vm.TotalPiezasSolicitadas =
            vm.Partidas.Sum(x => x.CantidadSolicitada);

        vm.TotalPiezasPreparadas =
            vm.Partidas.Sum(x => x.CantidadAsignada);

        vm.TotalPiezasDespachadas =
            vm.Partidas.Sum(x => x.CantidadDespachada);

        vm.TotalCajasAsignadas =
            vm.CajasAsignadas.Count;

        vm.TotalCajasCargadas =
            vm.CajasAsignadas.Count(x =>
                string.Equals(
                    x.Estatus,
                    "Cargada",
                    StringComparison.OrdinalIgnoreCase)
                ||
                string.Equals(
                    x.Estatus,
                    "Despachada",
                    StringComparison.OrdinalIgnoreCase));

        vm.FechaHoraCargaProgramada =
            CombinarFechaHora(
                vm.FechaCargaProgramada,
                vm.HoraCargaProgramada);

        vm.FechaHoraEntregaProgramada =
            CombinarFechaHora(
                vm.FechaEntregaProgramada,
                vm.HoraEntregaProgramada);

        // =====================================================
        // PORCENTAJE DE AVANCE
        // =====================================================

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

        // =====================================================
        // PROXIMA ACCION
        // =====================================================

        switch (vm.Estatus)
        {
            case "Programado":

                vm.ProximaAccion = "Iniciar preparación";

                vm.ProximaAccionDetalle =
                    vm.TotalPiezasPreparadas > 0
                        ? $"Ya existen {vm.TotalPiezasPreparadas:N0} piezas reservadas."
                        : "Asignar las cajas PT correspondientes al embarque.";

                break;

            case "Preparando":

                if (!vm.PreparacionCompleta)
                {
                    vm.ProximaAccion = "Completar preparación";

                    vm.ProximaAccionDetalle =
                        $"Faltan {vm.PiezasPendientesPreparar:N0} piezas por preparar.";
                }
                else
                {
                    vm.ProximaAccion = "Confirmar embarque preparado";

                    vm.ProximaAccionDetalle =
                        "La cantidad requerida ya está completamente asignada.";
                }

                break;

            case "Preparado":

                vm.ProximaAccion = "Confirmar carga física";

                vm.ProximaAccionDetalle =
                    "El producto está preparado y puede cargarse en la unidad.";

                break;

            case "Cargado":

                vm.ProximaAccion = "Validar salida";

                vm.ProximaAccionDetalle =
                    "Confirmar checklist de salida y despachar el embarque.";

                break;

            case "En ruta":

                vm.ProximaAccion = "Confirmar entrega";

                vm.ProximaAccionDetalle =
                    "El embarque se encuentra en tránsito hacia el cliente.";

                break;

            case "Entregado":

                vm.ProximaAccion = "Proceso completado";

                vm.ProximaAccionDetalle =
                    "El embarque ya fue recibido por el cliente.";

                break;

            case "Cancelado":

                vm.ProximaAccion = "Programación cancelada";

                vm.ProximaAccionDetalle =
                    "Este embarque ya no continúa en el flujo.";

                break;

            default:

                vm.ProximaAccion = "Revisar embarque";
                vm.ProximaAccionDetalle = "Estatus no reconocido.";

                break;
        }

        // =====================================================
        // TIEMPOS
        // =====================================================

        if (vm.FechaHoraCargaProgramada.HasValue)
        {
            vm.MinutosParaCarga =
                (int)Math.Round(
                    (vm.FechaHoraCargaProgramada.Value - ahora)
                    .TotalMinutes);

            vm.CargaAtrasada =
                vm.Estatus is "Programado" or "Preparando" or "Preparado"
                && ahora > vm.FechaHoraCargaProgramada.Value;
        }

        if (vm.FechaHoraEntregaProgramada.HasValue)
        {
            vm.MinutosParaEntrega =
                (int)Math.Round(
                    (vm.FechaHoraEntregaProgramada.Value - ahora)
                    .TotalMinutes);

            vm.EntregaAtrasada =
                vm.Estatus is not "Entregado"
                and not "Cancelado"
                && ahora > vm.FechaHoraEntregaProgramada.Value;
        }

        // =====================================================
        // RIESGO PREVENTIVO
        // =====================================================

        vm.EnRiesgo = false;
        vm.MensajeRiesgo = string.Empty;

        if (vm.Estatus is "Entregado" or "Cancelado")
        {
            // Ya no evaluamos riesgo operativo.
        }
        else if (vm.EntregaAtrasada)
        {
            vm.MensajeRiesgo =
                "La hora comprometida de entrega ya fue superada.";
        }
        else if (vm.CargaAtrasada)
        {
            vm.MensajeRiesgo =
                "La hora programada de carga ya fue superada.";
        }
        else if (
            vm.FechaHoraCargaProgramada.HasValue
            && vm.MinutosParaCarga.HasValue
            && vm.MinutosParaCarga.Value <= 60
            && vm.Estatus is "Programado" or "Preparando")
        {
            vm.EnRiesgo = true;

            vm.MensajeRiesgo =
                vm.PiezasPendientesPreparar > 0
                    ? $"La carga está próxima y faltan {vm.PiezasPendientesPreparar:N0} piezas por preparar."
                    : "La carga está próxima y todavía no se ha confirmado la preparación.";
        }
        else if (
            vm.FechaHoraEntregaProgramada.HasValue
            && vm.MinutosParaEntrega.HasValue
            && vm.MinutosParaEntrega.Value <= 120
            && vm.Estatus != "En ruta")
        {
            vm.EnRiesgo = true;

            vm.MensajeRiesgo =
                "La entrega está próxima y el embarque todavía no se encuentra en ruta.";
        }

        // =====================================================
        // ESTADO EJECUTIVO
        // =====================================================

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
        else if (vm.EntregaAtrasada || vm.CargaAtrasada)
        {
            vm.EstadoGeneral = "Atrasado";
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

        // =====================================================
        // CHECKLIST
        // =====================================================

        vm.Checklist = new List<LogisticaChecklistVm>
    {
        new()
        {
            Codigo = "RUTA",
            Concepto = "Ruta asignada",
            Descripcion = vm.TieneRuta
                ? vm.Ruta
                : "El embarque todavía no tiene ruta.",
            Completo = vm.TieneRuta
        },

        new()
        {
            Codigo = "UNIDAD",
            Concepto = "Unidad asignada",
            Descripcion = vm.TieneUnidad
                ? vm.Unidad
                : "El embarque todavía no tiene unidad.",
            Completo = vm.TieneUnidad
        },

        new()
        {
            Codigo = "OPERADOR",
            Concepto = "Operador asignado",
            Descripcion = vm.TieneOperador
                ? vm.Operador
                : "El embarque todavía no tiene operador.",
            Completo = vm.TieneOperador
        },

        new()
        {
            Codigo = "PRODUCTO",
            Concepto = "Producto preparado",
            Descripcion =
                $"{vm.TotalPiezasPreparadas:N0} de {vm.TotalPiezasSolicitadas:N0} piezas",
            Completo = vm.PreparacionCompleta
        },

        new()
        {
            Codigo = "CARGA",
            Concepto = "Carga física confirmada",
            Descripcion = vm.CargaCompleta
                ? "Carga completa."
                : "La carga física todavía no se ha confirmado.",
            Completo = vm.CargaCompleta
        },

        new()
        {
            Codigo = "INCIDENCIAS",
            Concepto = "Sin incidencias críticas",
            Descripcion = vm.IncidenciasCriticas == 0
                ? "Sin bloqueos críticos."
                : $"{vm.IncidenciasCriticas} incidencia(s) crítica(s) abierta(s).",
            Completo = vm.IncidenciasCriticas == 0
        }
    };
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
        if (hasta < desde) return BadRequest(new { ok = false, mensaje = "El rango de fechas del calendario no es válido." });
        if ((hasta - desde).TotalDays > 120) return BadRequest(new { ok = false, mensaje = "El calendario solo puede consultar hasta 120 días por solicitud." });
        q = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
        estatus = string.IsNullOrWhiteSpace(estatus) ? null : estatus.Trim();
        await using var cn = await AbrirAsync(cancellationToken);
        if (!await TieneFase1Async(cn, cancellationToken))
            return BadRequest(new { ok = false, mensaje = "Falta la estructura de Logística Fase 1." });
        var eventos = new List<object>();
        const string sqlEmbarques = @"
SELECT
    e.EmbarqueID,
    ISNULL(e.Folio,N'') AS Folio,
    ISNULL(e.ClienteNombreSnapshot,N'') AS Cliente,
    ISNULL(e.Destino,N'') AS Destino,
    e.FechaCargaProgramada,
    e.HoraCargaProgramada,
    e.FechaEntregaProgramada,
    e.HoraEntregaProgramada,
    ISNULL(e.Estatus,N'') AS Estatus,
    ISNULL(e.TieneIncidencia,0) AS TieneIncidencia,
    ISNULL
    (
        (
            SELECT COUNT(*)
            FROM dbo.Logistica_EmbarqueCajas ec
            WHERE ec.EmbarqueID=e.EmbarqueID
              AND ec.Activo=1
        ),
        0
    ) AS TotalCajas,
    ISNULL
    (
        (
            SELECT SUM(d.CantidadSolicitada)
            FROM dbo.Logistica_EmbarqueDetalle d
            WHERE d.EmbarqueID=e.EmbarqueID
              AND d.Activo=1
        ),
        0
    ) AS TotalPiezas
FROM dbo.Logistica_Embarques e
WHERE e.Activo=1
  AND (@Estatus IS NULL OR e.Estatus=@Estatus)
  AND
  (
      @Q IS NULL
      OR e.Folio LIKE N'%'+@Q+N'%'
      OR e.ClienteNombreSnapshot LIKE N'%'+@Q+N'%'
      OR e.Destino LIKE N'%'+@Q+N'%'
  )
  AND
  (
      (e.FechaCargaProgramada>=@Desde AND e.FechaCargaProgramada<DATEADD(DAY,1,@Hasta))
      OR
      (e.FechaEntregaProgramada>=@Desde AND e.FechaEntregaProgramada<DATEADD(DAY,1,@Hasta))
  )
ORDER BY
    COALESCE(e.FechaCargaProgramada,e.FechaEntregaProgramada),
    e.HoraCargaProgramada,
    e.EmbarqueID;";
        await using (var cmd = new SqlCommand(sqlEmbarques, cn))
        {
            cmd.Parameters.Add("@Desde", SqlDbType.Date).Value = desde;
            cmd.Parameters.Add("@Hasta", SqlDbType.Date).Value = hasta;
            cmd.Parameters.Add("@Estatus", SqlDbType.NVarChar, 20).Value = string.IsNullOrWhiteSpace(estatus) ? DBNull.Value : estatus;
            cmd.Parameters.Add("@Q", SqlDbType.NVarChar, 250).Value = string.IsNullOrWhiteSpace(q) ? DBNull.Value : q;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await rd.ReadAsync(cancellationToken))
            {
                var embarqueId = Entero(rd, "EmbarqueID");
                var folio = Texto(rd, "Folio");
                var cliente = Texto(rd, "Cliente");
                var destino = Texto(rd, "Destino");
                var fechaCarga = Fecha(rd, "FechaCargaProgramada");
                var horaCarga = Hora(rd, "HoraCargaProgramada");
                var fechaEntrega = Fecha(rd, "FechaEntregaProgramada");
                var horaEntrega = Hora(rd, "HoraEntregaProgramada");
                var estado = Texto(rd, "Estatus");
                var incidencia = Booleano(rd, "TieneIncidencia");
                var totalCajas = Entero(rd, "TotalCajas");
                var totalPiezas = Entero(rd, "TotalPiezas");
                if (fechaCarga.HasValue && fechaCarga.Value.Date >= desde && fechaCarga.Value.Date <= hasta)
                {
                    eventos.Add(new
                    {
                        id = $"CARGA-{embarqueId}",
                        embarqueId,
                        tipo = "CARGA",
                        fecha = fechaCarga.Value.ToString("yyyy-MM-dd"),
                        hora = horaCarga?.ToString(@"hh\:mm") ?? "",
                        titulo = string.IsNullOrWhiteSpace(folio) ? $"Embarque {embarqueId}" : folio,
                        cliente,
                        destino,
                        estatus = estado,
                        totalCajas,
                        totalPiezas,
                        incidencia,
                        url = Url.Action(nameof(Detalle), "Logistica", new { id = embarqueId })
                    });
                }
                if (fechaEntrega.HasValue && fechaEntrega.Value.Date >= desde && fechaEntrega.Value.Date <= hasta)
                {
                    eventos.Add(new
                    {
                        id = $"ENTREGA-{embarqueId}",
                        embarqueId,
                        tipo = "ENTREGA",
                        fecha = fechaEntrega.Value.ToString("yyyy-MM-dd"),
                        hora = horaEntrega?.ToString(@"hh\:mm") ?? "",
                        titulo = string.IsNullOrWhiteSpace(folio) ? $"Embarque {embarqueId}" : folio,
                        cliente,
                        destino,
                        estatus = estado,
                        totalCajas,
                        totalPiezas,
                        incidencia,
                        url = Url.Action(nameof(Detalle), "Logistica", new { id = embarqueId })
                    });
                }
            }
        }
        var demandas = await CargarDemandasAsync(cn, q, desde, hasta, true, null, cancellationToken);
        foreach (var demanda in demandas.Where(x => x.PendienteProgramar > 0))
        {
            var fechaCarga = (demanda.FechaCarga ?? demanda.FechaEntrega.AddDays(-1)).Date;
            if (fechaCarga < desde || fechaCarga > hasta) continue;
            eventos.Add(new
            {
                id = $"PENDIENTE-{demanda.ReleaseDetalleID}",
                releaseDetalleId = demanda.ReleaseDetalleID,
                tipo = "PENDIENTE",
                fecha = fechaCarga.ToString("yyyy-MM-dd"),
                hora = "",
                titulo = string.IsNullOrWhiteSpace(demanda.FolioRelease) ? "Release pendiente" : demanda.FolioRelease,
                cliente = demanda.Cliente,
                destino = "",
                estatus = "Pendiente programar",
                numeroParte = demanda.NumeroParte,
                numeroOF = demanda.NumeroOF,
                totalCajas = demanda.CajasPTDisponibles,
                totalPiezas = demanda.PendienteProgramar,
                incidencia = demanda.PiezasPTDisponibles < demanda.PendienteProgramar,
                url = Url.Action(nameof(Crear), "Logistica", new { releaseDetalleId = demanda.ReleaseDetalleID })
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

    private static async Task<(int ClienteID, string Estatus)?> ObtenerHeaderAsync(
        SqlConnection cn,
        SqlTransaction tx,
        int embarqueId,
        CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT ClienteID,Estatus
FROM dbo.Logistica_Embarques WITH (UPDLOCK,HOLDLOCK)
WHERE EmbarqueID=@EmbarqueID AND Activo=1;";
        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await rd.ReadAsync(cancellationToken)) return null;
        return (Entero(rd, "ClienteID"), Texto(rd, "Estatus"));
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
