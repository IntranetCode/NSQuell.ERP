using ERP.NSQuell.Models.ViewModels.Logistica;
using ERP.NSQuell.Servicios;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Globalization;

namespace ERP.NSQuell.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LogisticaListaCargaController : Controller
{
    private readonly IConfiguration _configuration;
    private readonly IServicioAcceso _acceso;

    public LogisticaListaCargaController(IConfiguration configuration, IServicioAcceso acceso)
    {
        _configuration = configuration;
        _acceso = acceso;
    }

    private string ConnectionString => _configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("No se encontró ConnectionStrings:DefaultConnection.");
    private int? UsuarioID => HttpContext.Session.GetInt32("UsuarioID");
    private string UsuarioNombre => HttpContext.Session.GetString("NombreMostrar") ?? HttpContext.Session.GetString("Username") ?? User?.Identity?.Name ?? "Usuario";

    private async Task<IActionResult?> ValidarAccesoAsync()
    {
        if (!UsuarioID.HasValue || UsuarioID.Value <= 0) return RedirectToAction("Login", "Login");
        if (!await _acceso.TienePermisoAsync(UsuarioID.Value, "Tablero de Logística")) return Forbid();
        return null;
    }

    private async Task<SqlConnection> AbrirAsync(CancellationToken cancellationToken)
    {
        var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(cancellationToken);
        return cn;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int? anio = null, int? semana = null, int? clienteId = null, string? q = null, string? criticidad = null, CancellationToken cancellationToken = default)
    {
        var acceso = await ValidarAccesoAsync();
        if (acceso != null) return acceso;
        q = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
        criticidad = NormalizarCriticidad(criticidad);
        var referencia = ResolverSemana(anio, semana);
        await using var cn = await AbrirAsync(cancellationToken);
        if (!await EstructuraDisponibleAsync(cn, cancellationToken))
        {
            ViewBag.ErrorConfiguracion = "Falta ejecutar la estructura SQL de Lista de carga semanal.";
            return View(new LogisticaListaCargaIndexVm
            {
                Anio = referencia.Anio,
                NumeroSemana = referencia.Semana,
                FechaInicio = referencia.Inicio,
                FechaFin = referencia.Fin,
                Busqueda = q,
                ClienteID = clienteId,
                Criticidad = criticidad
            });
        }
        var semanaDb = await ObtenerOCrearSemanaAsync(cn, referencia.Anio, referencia.Semana, referencia.Inicio, referencia.Fin, cancellationToken);
        var vm = new LogisticaListaCargaIndexVm
        {
            ListaCargaSemanaID = semanaDb.ListaCargaSemanaID,
            Anio = semanaDb.Anio,
            NumeroSemana = semanaDb.NumeroSemana,
            FechaInicio = semanaDb.FechaInicio,
            FechaFin = semanaDb.FechaFin,
            EstatusSemana = semanaDb.Estatus,
            ObservacionesSemana = semanaDb.Observaciones,
            Busqueda = q,
            ClienteID = clienteId,
            Criticidad = criticidad
        };
        vm.Filas = await CargarMatrizSemanalAsync(cn, vm.ListaCargaSemanaID!.Value, vm.FechaInicio, vm.FechaFin, clienteId, q, criticidad, cancellationToken);
        vm.Salidas = await CargarSalidasAsync(cn, vm.FechaInicio, vm.FechaFin, clienteId, q, criticidad, cancellationToken);
        vm.Clientes = await CargarClientesAsync(cn, vm.FechaInicio, vm.FechaFin, cancellationToken);
        ViewBag.SemanaAnterior = ObtenerSemanaRelativa(vm.FechaInicio, -7);
        ViewBag.SemanaSiguiente = ObtenerSemanaRelativa(vm.FechaInicio, 7);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarAjuste(LogisticaListaCargaAjusteVm model, int? anio = null, int? semana = null, CancellationToken cancellationToken = default)
    {
        var acceso = await ValidarAccesoAsync();
        if (acceso != null) return acceso;
        model.UbicacionManual = model.UbicacionManual?.Trim();
        model.Observaciones = model.Observaciones?.Trim();
        if (model.ListaCargaSemanaID <= 0) ModelState.AddModelError(nameof(model.ListaCargaSemanaID), "La semana no es válida.");
        if (!model.ClienteID.HasValue || model.ClienteID.Value <= 0) ModelState.AddModelError(nameof(model.ClienteID), "El cliente no es válido.");
        if (!model.ParteID.HasValue || model.ParteID.Value <= 0) ModelState.AddModelError(nameof(model.ParteID), "La parte no es válida.");
        if (!string.IsNullOrWhiteSpace(model.UbicacionManual) && model.UbicacionManual.Length > 100) ModelState.AddModelError(nameof(model.UbicacionManual), "La ubicación no puede exceder 100 caracteres.");
        if (model.CantidadAtrasoManual.HasValue && model.CantidadAtrasoManual.Value < 0) ModelState.AddModelError(nameof(model.CantidadAtrasoManual), "El atraso no puede ser negativo.");
        if (!string.IsNullOrWhiteSpace(model.Observaciones) && model.Observaciones.Length > 1000) ModelState.AddModelError(nameof(model.Observaciones), "Las observaciones no pueden exceder 1000 caracteres.");
        if (!ModelState.IsValid)
        {
            TempData["LogisticaError"] = ObtenerErroresModelState();
            return RedirectSemana(anio, semana);
        }
        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            const string sqlSemana = @"SELECT Estatus FROM dbo.Logistica_ListaCargaSemanas WITH(UPDLOCK,HOLDLOCK) WHERE ListaCargaSemanaID=@ListaCargaSemanaID AND Activo=1;";
            string estatusSemana;
            await using (var cmd = new SqlCommand(sqlSemana, cn, tx))
            {
                cmd.Parameters.Add("@ListaCargaSemanaID", SqlDbType.Int).Value = model.ListaCargaSemanaID;
                var valor = await cmd.ExecuteScalarAsync(cancellationToken);
                if (valor == null || valor == DBNull.Value) throw new InvalidOperationException("La semana seleccionada ya no existe.");
                estatusSemana = valor.ToString()?.Trim() ?? string.Empty;
            }
            if (estatusSemana == "Cerrada") throw new InvalidOperationException("La semana está cerrada y ya no permite ajustes.");
            const string sqlExiste = @"
SELECT TOP(1) ListaCargaAjusteID
FROM dbo.Logistica_ListaCargaAjustes WITH(UPDLOCK,HOLDLOCK)
WHERE ListaCargaSemanaID=@ListaCargaSemanaID
  AND ClienteID=@ClienteID
  AND ParteID=@ParteID
  AND Activo=1
ORDER BY ListaCargaAjusteID DESC;";
            int? ajusteId;
            await using (var cmd = new SqlCommand(sqlExiste, cn, tx))
            {
                cmd.Parameters.Add("@ListaCargaSemanaID", SqlDbType.Int).Value = model.ListaCargaSemanaID;
                cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = model.ClienteID.Value;
                cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = model.ParteID.Value;
                var valor = await cmd.ExecuteScalarAsync(cancellationToken);
                ajusteId = valor == null || valor == DBNull.Value ? null : Convert.ToInt32(valor);
            }
            if (ajusteId.HasValue)
            {
                const string sql = @"
UPDATE dbo.Logistica_ListaCargaAjustes
SET UbicacionManual=@UbicacionManual,
    CantidadAtrasoManual=@CantidadAtrasoManual,
    Observaciones=@Observaciones,
    FechaModificacion=SYSDATETIME(),
    ActualizadoPor=@Usuario
WHERE ListaCargaAjusteID=@AjusteID AND Activo=1;";
                await using var cmd = new SqlCommand(sql, cn, tx);
                cmd.Parameters.Add("@UbicacionManual", SqlDbType.NVarChar, 100).Value = Db(model.UbicacionManual);
                cmd.Parameters.Add("@CantidadAtrasoManual", SqlDbType.Int).Value = Db(model.CantidadAtrasoManual);
                cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1000).Value = Db(model.Observaciones);
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@AjusteID", SqlDbType.Int).Value = ajusteId.Value;
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            else
            {
                const string sql = @"
INSERT dbo.Logistica_ListaCargaAjustes
(ListaCargaSemanaID,ReleaseDetalleID,ClienteID,ParteID,UbicacionManual,CantidadAtrasoManual,Observaciones,Activo,FechaCreacion,CreadoPor)
VALUES
(@ListaCargaSemanaID,@ReleaseDetalleID,@ClienteID,@ParteID,@UbicacionManual,@CantidadAtrasoManual,@Observaciones,1,SYSDATETIME(),@Usuario);";
                await using var cmd = new SqlCommand(sql, cn, tx);
                cmd.Parameters.Add("@ListaCargaSemanaID", SqlDbType.Int).Value = model.ListaCargaSemanaID;
                cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = Db(model.ReleaseDetalleID);
                cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = model.ClienteID.Value;
                cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = model.ParteID.Value;
                cmd.Parameters.Add("@UbicacionManual", SqlDbType.NVarChar, 100).Value = Db(model.UbicacionManual);
                cmd.Parameters.Add("@CantidadAtrasoManual", SqlDbType.Int).Value = Db(model.CantidadAtrasoManual);
                cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1000).Value = Db(model.Observaciones);
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            await tx.CommitAsync(cancellationToken);
            TempData["LogisticaOk"] = "Ajuste de lista de carga guardado correctamente.";
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            TempData["LogisticaError"] = "No fue posible guardar el ajuste: " + ex.Message;
        }
        return RedirectSemana(anio, semana);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CambiarEstadoSemana(int listaCargaSemanaId, string estatus, CancellationToken cancellationToken = default)
    {
        var acceso = await ValidarAccesoAsync();
        if (acceso != null) return acceso;
        estatus = estatus?.Trim() ?? string.Empty;
        if (listaCargaSemanaId <= 0 || estatus is not "Abierta" and not "Cerrada")
        {
            TempData["LogisticaError"] = "La semana o el estatus indicado no es válido.";
            return RedirectToAction(nameof(Index));
        }
        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            const string sql = @"
UPDATE dbo.Logistica_ListaCargaSemanas
SET Estatus=@Estatus,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
WHERE ListaCargaSemanaID=@ListaCargaSemanaID AND Activo=1;
SELECT @@ROWCOUNT;";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@Estatus", SqlDbType.NVarChar, 30).Value = estatus;
            cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
            cmd.Parameters.Add("@ListaCargaSemanaID", SqlDbType.Int).Value = listaCargaSemanaId;
            if (Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) == 0) throw new InvalidOperationException("La semana ya no existe.");
            const string sqlLeer = @"SELECT Anio,NumeroSemana FROM dbo.Logistica_ListaCargaSemanas WHERE ListaCargaSemanaID=@ListaCargaSemanaID;";
            int anio;
            int semana;
            await using (var cmdLeer = new SqlCommand(sqlLeer, cn, tx))
            {
                cmdLeer.Parameters.Add("@ListaCargaSemanaID", SqlDbType.Int).Value = listaCargaSemanaId;
                await using var rd = await cmdLeer.ExecuteReaderAsync(cancellationToken);
                if (!await rd.ReadAsync(cancellationToken)) throw new InvalidOperationException("No fue posible recuperar la semana.");
                anio = Entero(rd, "Anio");
                semana = Entero(rd, "NumeroSemana");
            }
            await tx.CommitAsync(cancellationToken);
            TempData["LogisticaOk"] = estatus == "Cerrada" ? "Semana cerrada correctamente." : "Semana reabierta correctamente.";
            return RedirectToAction(nameof(Index), new { anio, semana });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            TempData["LogisticaError"] = "No fue posible cambiar el estado de la semana: " + ex.Message;
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarObservacionesSemana(int listaCargaSemanaId, string? observaciones, CancellationToken cancellationToken = default)
    {
        var acceso = await ValidarAccesoAsync();
        if (acceso != null) return acceso;
        observaciones = observaciones?.Trim();
        if (listaCargaSemanaId <= 0)
        {
            TempData["LogisticaError"] = "La semana no es válida.";
            return RedirectToAction(nameof(Index));
        }
        if (!string.IsNullOrWhiteSpace(observaciones) && observaciones.Length > 1000)
        {
            TempData["LogisticaError"] = "Las observaciones no pueden exceder 1000 caracteres.";
            return RedirectToAction(nameof(Index));
        }
        await using var cn = await AbrirAsync(cancellationToken);
        const string sql = @"
UPDATE dbo.Logistica_ListaCargaSemanas
SET Observaciones=@Observaciones,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
WHERE ListaCargaSemanaID=@ListaCargaSemanaID AND Activo=1;
SELECT Anio,NumeroSemana
FROM dbo.Logistica_ListaCargaSemanas
WHERE ListaCargaSemanaID=@ListaCargaSemanaID AND Activo=1;";
        int? anio = null;
        int? semana = null;
        await using (var cmd = new SqlCommand(sql, cn))
        {
            cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1000).Value = Db(observaciones);
            cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
            cmd.Parameters.Add("@ListaCargaSemanaID", SqlDbType.Int).Value = listaCargaSemanaId;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await rd.ReadAsync(cancellationToken))
            {
                anio = Entero(rd, "Anio");
                semana = Entero(rd, "NumeroSemana");
            }
        }
        TempData["LogisticaOk"] = "Observaciones de la semana actualizadas.";
        return RedirectToAction(nameof(Index), new { anio, semana });
    }

    private static async Task<List<LogisticaListaCargaFilaVm>> CargarMatrizSemanalAsync(SqlConnection cn, int listaCargaSemanaId, DateTime fechaInicio, DateTime fechaFin, int? clienteId, string? q, string? criticidad, CancellationToken cancellationToken)
    {
        var filas = new Dictionary<(int ClienteID, int ParteID), LogisticaListaCargaFilaVm>();
        const string sqlSemana = @"
SELECT d.ClienteID,ISNULL(d.Cliente,N'') AS Cliente,d.ParteID,ISNULL(d.NumeroParte,N'') AS Referencia,ISNULL(d.Descripcion,N'') AS Designacion,
SUM(ISNULL(d.CantidadRequerida,0)) AS TotalSemana,
SUM(CASE WHEN DATEDIFF(DAY,@FechaInicio,d.FechaRequerida)=0 THEN ISNULL(d.CantidadRequerida,0) ELSE 0 END) AS Lunes,
SUM(CASE WHEN DATEDIFF(DAY,@FechaInicio,d.FechaRequerida)=1 THEN ISNULL(d.CantidadRequerida,0) ELSE 0 END) AS Martes,
SUM(CASE WHEN DATEDIFF(DAY,@FechaInicio,d.FechaRequerida)=2 THEN ISNULL(d.CantidadRequerida,0) ELSE 0 END) AS Miercoles,
SUM(CASE WHEN DATEDIFF(DAY,@FechaInicio,d.FechaRequerida)=3 THEN ISNULL(d.CantidadRequerida,0) ELSE 0 END) AS Jueves,
SUM(CASE WHEN DATEDIFF(DAY,@FechaInicio,d.FechaRequerida)=4 THEN ISNULL(d.CantidadRequerida,0) ELSE 0 END) AS Viernes,
SUM(CASE WHEN DATEDIFF(DAY,@FechaInicio,d.FechaRequerida)=5 THEN ISNULL(d.CantidadRequerida,0) ELSE 0 END) AS Sabado,
SUM(ISNULL(d.CantidadProgramadaLogistica,0)) AS CantidadProgramadaLogistica,
SUM(ISNULL(d.PendienteProgramar,0)) AS PendienteProgramar,
SUM(ISNULL(d.CajasPTDisponibles,0)) AS CajasPTDisponibles,
SUM(ISNULL(d.PiezasPTDisponibles,0)) AS PiezasPTDisponibles
FROM dbo.vw_Logistica_DemandaRelease d
WHERE d.FechaRequerida>=@FechaInicio
AND d.FechaRequerida<=@FechaFin
AND d.ClienteID IS NOT NULL
AND d.ParteID IS NOT NULL
AND (@ClienteID IS NULL OR d.ClienteID=@ClienteID)
AND (@Q IS NULL OR d.Cliente LIKE N'%'+@Q+N'%' OR d.NumeroParte LIKE N'%'+@Q+N'%' OR d.Descripcion LIKE N'%'+@Q+N'%' OR d.NumeroOF LIKE N'%'+@Q+N'%')
GROUP BY d.ClienteID,d.Cliente,d.ParteID,d.NumeroParte,d.Descripcion;";
        await using (var cmd = new SqlCommand(sqlSemana, cn))
        {
            cmd.Parameters.Add("@FechaInicio", SqlDbType.Date).Value = fechaInicio.Date;
            cmd.Parameters.Add("@FechaFin", SqlDbType.Date).Value = fechaFin.Date;
            cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = Db(clienteId);
            cmd.Parameters.Add("@Q", SqlDbType.NVarChar, 250).Value = Db(q);
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await rd.ReadAsync(cancellationToken))
            {
                var cliente = Entero(rd, "ClienteID");
                var parte = Entero(rd, "ParteID");
                filas[(cliente, parte)] = new LogisticaListaCargaFilaVm
                {
                    ClienteID = cliente,
                    Cliente = Texto(rd, "Cliente"),
                    ParteID = parte,
                    Referencia = Texto(rd, "Referencia"),
                    Designacion = Texto(rd, "Designacion"),
                    InicioSemana = fechaInicio.Date,
                    FinSemana = fechaFin.Date,
                    TotalSemana = Entero(rd, "TotalSemana"),
                    Lunes = Entero(rd, "Lunes"),
                    Martes = Entero(rd, "Martes"),
                    Miercoles = Entero(rd, "Miercoles"),
                    Jueves = Entero(rd, "Jueves"),
                    Viernes = Entero(rd, "Viernes"),
                    Sabado = Entero(rd, "Sabado"),
                    CantidadProgramadaLogistica = Entero(rd, "CantidadProgramadaLogistica"),
                    PendienteProgramar = Entero(rd, "PendienteProgramar"),
                    CajasPTDisponibles = EnteroLargo(rd, "CajasPTDisponibles"),
                    PiezasPTDisponibles = EnteroLargo(rd, "PiezasPTDisponibles")
                };
            }
        }
        const string sqlAtrasos = @"
SELECT d.ClienteID,ISNULL(d.Cliente,N'') AS Cliente,d.ParteID,ISNULL(d.NumeroParte,N'') AS Referencia,ISNULL(d.Descripcion,N'') AS Designacion,
SUM(ISNULL(d.PendienteProgramar,0)) AS Atraso
FROM dbo.vw_Logistica_DemandaRelease d
WHERE d.FechaRequerida<@FechaInicio
AND ISNULL(d.PendienteProgramar,0)>0
AND d.ClienteID IS NOT NULL
AND d.ParteID IS NOT NULL
AND (@ClienteID IS NULL OR d.ClienteID=@ClienteID)
AND (@Q IS NULL OR d.Cliente LIKE N'%'+@Q+N'%' OR d.NumeroParte LIKE N'%'+@Q+N'%' OR d.Descripcion LIKE N'%'+@Q+N'%' OR d.NumeroOF LIKE N'%'+@Q+N'%')
GROUP BY d.ClienteID,d.Cliente,d.ParteID,d.NumeroParte,d.Descripcion;";
        await using (var cmd = new SqlCommand(sqlAtrasos, cn))
        {
            cmd.Parameters.Add("@FechaInicio", SqlDbType.Date).Value = fechaInicio.Date;
            cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = Db(clienteId);
            cmd.Parameters.Add("@Q", SqlDbType.NVarChar, 250).Value = Db(q);
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await rd.ReadAsync(cancellationToken))
            {
                var cliente = Entero(rd, "ClienteID");
                var parte = Entero(rd, "ParteID");
                if (!filas.TryGetValue((cliente, parte), out var fila))
                {
                    fila = new LogisticaListaCargaFilaVm
                    {
                        ClienteID = cliente,
                        Cliente = Texto(rd, "Cliente"),
                        ParteID = parte,
                        Referencia = Texto(rd, "Referencia"),
                        Designacion = Texto(rd, "Designacion"),
                        InicioSemana = fechaInicio.Date,
                        FinSemana = fechaFin.Date
                    };
                    filas[(cliente, parte)] = fila;
                }
                fila.Atraso = Entero(rd, "Atraso");
            }
        }
        const string sqlEnviado = @"
SELECT dem.ClienteID,dem.ParteID,SUM(ISNULL(ed.CantidadDespachada,0)) AS Enviado
FROM dbo.vw_Logistica_DemandaRelease dem
INNER JOIN dbo.Logistica_EmbarqueDetalle ed ON ed.ReleaseDetalleID=dem.ReleaseDetalleID AND ed.Activo=1
INNER JOIN dbo.Logistica_Embarques e ON e.EmbarqueID=ed.EmbarqueID AND e.Activo=1 AND e.Estatus<>N'Cancelado'
WHERE dem.ClienteID IS NOT NULL
AND dem.ParteID IS NOT NULL
AND dem.FechaRequerida<=@FechaFin
AND (@ClienteID IS NULL OR dem.ClienteID=@ClienteID)
GROUP BY dem.ClienteID,dem.ParteID;";
        await using (var cmd = new SqlCommand(sqlEnviado, cn))
        {
            cmd.Parameters.Add("@FechaFin", SqlDbType.Date).Value = fechaFin.Date;
            cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = Db(clienteId);
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await rd.ReadAsync(cancellationToken))
            {
                var key = (Entero(rd, "ClienteID"), Entero(rd, "ParteID"));
                if (filas.TryGetValue(key, out var fila)) fila.Enviado = Entero(rd, "Enviado");
            }
        }
        const string sqlUbicaciones = @"
SELECT ParteID,PiezasAlmacen,PiezasGP12,PiezasProduccion,PiezasLocalizadas,Ubicacion
FROM dbo.vw_Logistica_UbicacionMaterial;";
        await using (var cmd = new SqlCommand(sqlUbicaciones, cn))
        {
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await rd.ReadAsync(cancellationToken))
            {
                var parteId = Entero(rd, "ParteID");
                foreach (var fila in filas.Values.Where(x => x.ParteID == parteId))
                {
                    fila.PiezasAlmacen = Decimal(rd, "PiezasAlmacen");
                    fila.PiezasGP12 = Decimal(rd, "PiezasGP12");
                    fila.PiezasProduccion = Decimal(rd, "PiezasProduccion");
                    fila.PiezasLocalizadas = Decimal(rd, "PiezasLocalizadas");
                    fila.Ubicacion = Texto(rd, "Ubicacion");
                }
            }
        }
        const string sqlAjustes = @"
SELECT ClienteID,ParteID,UbicacionManual,CantidadAtrasoManual,Observaciones
FROM dbo.Logistica_ListaCargaAjustes
WHERE ListaCargaSemanaID=@ListaCargaSemanaID
AND Activo=1;";
        await using (var cmd = new SqlCommand(sqlAjustes, cn))
        {
            cmd.Parameters.Add("@ListaCargaSemanaID", SqlDbType.Int).Value = listaCargaSemanaId;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await rd.ReadAsync(cancellationToken))
            {
                if (rd["ClienteID"] == DBNull.Value || rd["ParteID"] == DBNull.Value) continue;
                var key = (Convert.ToInt32(rd["ClienteID"]), Convert.ToInt32(rd["ParteID"]));
                if (!filas.TryGetValue(key, out var fila)) continue;
                var ubicacion = TextoNullable(rd, "UbicacionManual");
                var atraso = EnteroNullable(rd, "CantidadAtrasoManual");
                var observaciones = TextoNullable(rd, "Observaciones");
                if (!string.IsNullOrWhiteSpace(ubicacion)) fila.Ubicacion = ubicacion;
                if (atraso.HasValue) fila.Atraso = atraso.Value;
                fila.Observaciones = observaciones;
            }
        }
        foreach (var fila in filas.Values)
            if (string.IsNullOrWhiteSpace(fila.Ubicacion)) fila.Ubicacion = "SIN MATERIAL";
        var resultado = filas.Values.AsEnumerable();
        if (criticidad == "Expeditado") resultado = resultado.Where(x => x.EsExpeditado);
        else if (criticidad == "Programado") resultado = resultado.Where(x => !x.EsExpeditado);
        return resultado.OrderByDescending(x => x.EsExpeditado).ThenBy(x => x.Cliente).ThenBy(x => x.Referencia).ToList();
    }
    private static decimal Decimal(SqlDataReader rd, string columna)
    {
        var i = rd.GetOrdinal(columna);
        return rd.IsDBNull(i) ? 0m : Convert.ToDecimal(rd.GetValue(i));
    }
    private static async Task<List<LogisticaListaCargaSalidaVm>> CargarSalidasAsync(SqlConnection cn, DateTime fechaInicio, DateTime fechaFin, int? clienteId, string? q, string? criticidad, CancellationToken cancellationToken)
    {
        var resultado = new List<LogisticaListaCargaSalidaVm>();
        const string sql = @"
SELECT ViajeID,Folio,Fecha,LugarEnvio,Chofer,HoraSalida,HoraRegreso,TipoSalida,Criticidad,TipoUnidad,Unidad,Estatus
FROM dbo.vw_Logistica_ListaCargaSalidas
WHERE Fecha>=@FechaInicio
  AND Fecha<=@FechaFin
  AND (@Q IS NULL OR Folio LIKE N'%'+@Q+N'%' OR LugarEnvio LIKE N'%'+@Q+N'%' OR Chofer LIKE N'%'+@Q+N'%' OR Unidad LIKE N'%'+@Q+N'%')
  AND (@Criticidad IS NULL OR Criticidad=@Criticidad)
ORDER BY Fecha,HoraSalida,ViajeID;";
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@FechaInicio", SqlDbType.Date).Value = fechaInicio.Date;
        cmd.Parameters.Add("@FechaFin", SqlDbType.Date).Value = fechaFin.Date;
        cmd.Parameters.Add("@Q", SqlDbType.NVarChar, 250).Value = Db(q);
        cmd.Parameters.Add("@Criticidad", SqlDbType.NVarChar, 30).Value = Db(criticidad);
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await rd.ReadAsync(cancellationToken))
        {
            resultado.Add(new LogisticaListaCargaSalidaVm
            {
                ViajeID = Entero(rd, "ViajeID"),
                Folio = Texto(rd, "Folio"),
                Fecha = Fecha(rd, "Fecha") ?? DateTime.MinValue,
                LugarEnvio = Texto(rd, "LugarEnvio"),
                Chofer = Texto(rd, "Chofer"),
                HoraSalida = Hora(rd, "HoraSalida"),
                HoraRegreso = Hora(rd, "HoraRegreso"),
                TipoSalida = Texto(rd, "TipoSalida"),
                Criticidad = Texto(rd, "Criticidad"),
                TipoUnidad = Texto(rd, "TipoUnidad"),
                Unidad = Texto(rd, "Unidad"),
                Estatus = Texto(rd, "Estatus")
            });
        }
        return resultado;
    }

    private static async Task<List<LogisticaListaCargaClienteVm>> CargarClientesAsync(SqlConnection cn, DateTime fechaInicio, DateTime fechaFin, CancellationToken cancellationToken)
    {
        var resultado = new List<LogisticaListaCargaClienteVm>();
        const string sql = @"
SELECT DISTINCT ClienteID,Cliente
FROM dbo.vw_Logistica_DemandaRelease
WHERE ClienteID IS NOT NULL
  AND FechaRequerida<=@FechaFin
  AND (FechaRequerida>=@FechaInicio OR PendienteProgramar>0)
ORDER BY Cliente;";
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@FechaInicio", SqlDbType.Date).Value = fechaInicio.Date;
        cmd.Parameters.Add("@FechaFin", SqlDbType.Date).Value = fechaFin.Date;
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await rd.ReadAsync(cancellationToken))
        {
            resultado.Add(new LogisticaListaCargaClienteVm
            {
                ClienteID = Entero(rd, "ClienteID"),
                Cliente = Texto(rd, "Cliente")
            });
        }
        return resultado;
    }

    private async Task<LogisticaListaCargaSemanaVm> ObtenerOCrearSemanaAsync(SqlConnection cn, int anio, int numeroSemana, DateTime fechaInicio, DateTime fechaFin, CancellationToken cancellationToken)
    {
        const string sqlBuscar = @"
SELECT TOP(1) ListaCargaSemanaID,Anio,NumeroSemana,FechaInicio,FechaFin,Estatus,Observaciones,Activo
FROM dbo.Logistica_ListaCargaSemanas
WHERE Anio=@Anio AND NumeroSemana=@NumeroSemana AND Activo=1;";
        await using (var cmd = new SqlCommand(sqlBuscar, cn))
        {
            cmd.Parameters.Add("@Anio", SqlDbType.Int).Value = anio;
            cmd.Parameters.Add("@NumeroSemana", SqlDbType.Int).Value = numeroSemana;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await rd.ReadAsync(cancellationToken)) return MapearSemana(rd);
        }
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            const string sqlBuscarBloqueado = @"
SELECT TOP(1) ListaCargaSemanaID,Anio,NumeroSemana,FechaInicio,FechaFin,Estatus,Observaciones,Activo
FROM dbo.Logistica_ListaCargaSemanas WITH(UPDLOCK,HOLDLOCK)
WHERE Anio=@Anio AND NumeroSemana=@NumeroSemana AND Activo=1;";
            await using (var cmd = new SqlCommand(sqlBuscarBloqueado, cn, tx))
            {
                cmd.Parameters.Add("@Anio", SqlDbType.Int).Value = anio;
                cmd.Parameters.Add("@NumeroSemana", SqlDbType.Int).Value = numeroSemana;
                await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
                if (await rd.ReadAsync(cancellationToken))
                {
                    var existente = MapearSemana(rd);
                    await tx.CommitAsync(cancellationToken);
                    return existente;
                }
            }
            const string sqlInsert = @"
INSERT dbo.Logistica_ListaCargaSemanas
(Anio,NumeroSemana,FechaInicio,FechaFin,Estatus,Observaciones,Activo,FechaCreacion,CreadoPor)
VALUES
(@Anio,@NumeroSemana,@FechaInicio,@FechaFin,N'Abierta',NULL,1,SYSDATETIME(),@Usuario);
SELECT CONVERT(int,SCOPE_IDENTITY());";
            int id;
            await using (var cmd = new SqlCommand(sqlInsert, cn, tx))
            {
                cmd.Parameters.Add("@Anio", SqlDbType.Int).Value = anio;
                cmd.Parameters.Add("@NumeroSemana", SqlDbType.Int).Value = numeroSemana;
                cmd.Parameters.Add("@FechaInicio", SqlDbType.Date).Value = fechaInicio.Date;
                cmd.Parameters.Add("@FechaFin", SqlDbType.Date).Value = fechaFin.Date;
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                id = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
            }
            await tx.CommitAsync(cancellationToken);
            return new LogisticaListaCargaSemanaVm
            {
                ListaCargaSemanaID = id,
                Anio = anio,
                NumeroSemana = numeroSemana,
                FechaInicio = fechaInicio.Date,
                FechaFin = fechaFin.Date,
                Estatus = "Abierta",
                Activo = true
            };
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static LogisticaListaCargaSemanaVm MapearSemana(SqlDataReader rd)
    {
        return new LogisticaListaCargaSemanaVm
        {
            ListaCargaSemanaID = Entero(rd, "ListaCargaSemanaID"),
            Anio = Entero(rd, "Anio"),
            NumeroSemana = Entero(rd, "NumeroSemana"),
            FechaInicio = Fecha(rd, "FechaInicio") ?? DateTime.MinValue,
            FechaFin = Fecha(rd, "FechaFin") ?? DateTime.MinValue,
            Estatus = Texto(rd, "Estatus"),
            Observaciones = TextoNullable(rd, "Observaciones"),
            Activo = Booleano(rd, "Activo")
        };
    }

    private static (int Anio, int Semana, DateTime Inicio, DateTime Fin) ResolverSemana(int? anio, int? semana)
    {
        DateTime referencia;
        if (anio.HasValue && semana.HasValue && anio.Value >= 2020 && anio.Value <= 2100 && semana.Value >= 1 && semana.Value <= 53)
        {
            try
            {
                referencia = ISOWeek.ToDateTime(anio.Value, semana.Value, DayOfWeek.Monday);
            }
            catch
            {
                referencia = DateTime.Today;
            }
        }
        else referencia = DateTime.Today;
        var inicio = InicioSemana(referencia);
        var anioIso = ISOWeek.GetYear(inicio);
        var semanaIso = ISOWeek.GetWeekOfYear(inicio);
        return (anioIso, semanaIso, inicio, inicio.AddDays(5));
    }

    private static object ObtenerSemanaRelativa(DateTime fechaInicio, int dias)
    {
        var fecha = fechaInicio.Date.AddDays(dias);
        return new
        {
            anio = ISOWeek.GetYear(fecha),
            semana = ISOWeek.GetWeekOfYear(fecha)
        };
    }

    private static DateTime InicioSemana(DateTime fecha)
    {
        var diferencia = ((int)fecha.DayOfWeek + 6) % 7;
        return fecha.Date.AddDays(-diferencia);
    }

    private static string? NormalizarCriticidad(string? valor)
    {
        valor = valor?.Trim();
        if (string.IsNullOrWhiteSpace(valor)) return null;
        if (valor.Equals("Expeditado", StringComparison.OrdinalIgnoreCase)) return "Expeditado";
        if (valor.Equals("Programado", StringComparison.OrdinalIgnoreCase)) return "Programado";
        return null;
    }

    private async Task<bool> EstructuraDisponibleAsync(SqlConnection cn, CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT CASE WHEN
OBJECT_ID(N'dbo.Logistica_ListaCargaSemanas',N'U') IS NOT NULL
AND OBJECT_ID(N'dbo.Logistica_ListaCargaAjustes',N'U') IS NOT NULL
AND OBJECT_ID(N'dbo.vw_Logistica_DemandaRelease',N'V') IS NOT NULL
AND OBJECT_ID(N'dbo.vw_Logistica_ListaCargaSalidas',N'V') IS NOT NULL
AND OBJECT_ID(N'dbo.vw_Logistica_UbicacionMaterial',N'V') IS NOT NULL
THEN 1 ELSE 0 END;";
        await using var cmd = new SqlCommand(sql, cn);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private IActionResult RedirectSemana(int? anio, int? semana)
    {
        return anio.HasValue && semana.HasValue
            ? RedirectToAction(nameof(Index), new { anio, semana })
            : RedirectToAction(nameof(Index));
    }

    private string ObtenerErroresModelState()
    {
        var errores = ModelState.Values.SelectMany(x => x.Errors).Select(x => x.ErrorMessage).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
        return errores.Count > 0 ? string.Join(" ", errores) : "Revisa la información capturada.";
    }

    private static object Db(object? valor) => valor ?? DBNull.Value;

    private static string Texto(SqlDataReader rd, string columna)
    {
        var i = rd.GetOrdinal(columna);
        return rd.IsDBNull(i) ? string.Empty : rd.GetValue(i)?.ToString()?.Trim() ?? string.Empty;
    }

    private static string? TextoNullable(SqlDataReader rd, string columna)
    {
        var i = rd.GetOrdinal(columna);
        if (rd.IsDBNull(i)) return null;
        var valor = rd.GetValue(i)?.ToString()?.Trim();
        return string.IsNullOrWhiteSpace(valor) ? null : valor;
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

    private static bool Booleano(SqlDataReader rd, string columna)
    {
        var i = rd.GetOrdinal(columna);
        return !rd.IsDBNull(i) && Convert.ToBoolean(rd.GetValue(i));
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
        var valor = rd.GetValue(i);
        if (valor is TimeSpan ts) return ts;
        if (valor is DateTime dt) return dt.TimeOfDay;
        return TimeSpan.TryParse(valor?.ToString(), out var resultado) ? resultado : null;
    }
}