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
            return View(new LogisticaListaCargaIndexVm { Anio = referencia.Anio, NumeroSemana = referencia.Semana, FechaInicio = referencia.Inicio, FechaFin = referencia.Fin, Busqueda = q, ClienteID = clienteId, Criticidad = criticidad });
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
    public async Task<IActionResult> GuardarProgramacion(LogisticaListaCargaProgramarVm model, CancellationToken cancellationToken = default)
    {
        var acceso = await ValidarAccesoAsync();
        if (acceso != null) return acceso;
        model.Programaciones = model.Programaciones?.Where(x => x.Cantidad > 0).OrderBy(x => x.FechaProgramadaCarga).ToList() ?? new();
        if (model.ListaCargaSemanaID <= 0) ModelState.AddModelError(nameof(model.ListaCargaSemanaID), "La semana no es válida.");
        if (model.ClienteID <= 0) ModelState.AddModelError(nameof(model.ClienteID), "El cliente no es válido.");
        if (model.ParteID <= 0) ModelState.AddModelError(nameof(model.ParteID), "La parte no es válida.");
        if (model.Programaciones.Count == 0) ModelState.AddModelError(nameof(model.Programaciones), "Captura al menos una fecha y cantidad para programar.");
        if (model.Programaciones.Any(x => x.FechaProgramadaCarga == DateTime.MinValue)) ModelState.AddModelError(nameof(model.Programaciones), "Todas las programaciones deben tener una fecha válida.");
        if (!ModelState.IsValid)
        {
            TempData["LogisticaError"] = ObtenerErroresModelState();
            return RedirectSemana(model.Anio, model.NumeroSemana);
        }
        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            DateTime fechaInicio;
            DateTime fechaFin;
            const string sqlSemana = @"SELECT FechaInicio,FechaFin,Estatus FROM dbo.Logistica_ListaCargaSemanas WITH(UPDLOCK,HOLDLOCK) WHERE ListaCargaSemanaID=@Id AND Activo=1;";
            await using (var cmd = new SqlCommand(sqlSemana, cn, tx))
            {
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = model.ListaCargaSemanaID;
                await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
                if (!await rd.ReadAsync(cancellationToken)) throw new InvalidOperationException("La semana seleccionada ya no existe.");
                if (string.Equals(Texto(rd, "Estatus"), "Cerrada", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("La semana está cerrada y no permite modificar la programación.");
                fechaInicio = (Fecha(rd, "FechaInicio") ?? DateTime.MinValue).Date;
                fechaFin = (Fecha(rd, "FechaFin") ?? DateTime.MinValue).Date;
            }
            const string sqlBloqueados = @"
SELECT COUNT_BIG(*)
FROM dbo.Logistica_ListaCargaProgramacion p WITH(UPDLOCK,HOLDLOCK)
WHERE p.ListaCargaSemanaOrigenID=@SemanaID AND p.ClienteID=@ClienteID AND p.ParteID=@ParteID AND p.Activo=1 AND p.Estatus<>N'Cancelada'
AND EXISTS(SELECT 1 FROM dbo.Logistica_ListaCargaProgramacionEmbarques x WITH(UPDLOCK,HOLDLOCK) WHERE x.ListaCargaProgramacionID=p.ListaCargaProgramacionID AND x.Activo=1);";
            await using (var cmd = new SqlCommand(sqlBloqueados, cn, tx))
            {
                cmd.Parameters.Add("@SemanaID", SqlDbType.Int).Value = model.ListaCargaSemanaID;
                cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = model.ClienteID;
                cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = model.ParteID;
                if (Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken)) > 0) throw new InvalidOperationException("Esta partida ya tiene programación convertida en embarque. Solo puedes modificar cantidades que todavía no hayan generado embarque.");
            }
            const string sqlDesactivar = @"
UPDATE dbo.Logistica_ListaCargaProgramacion
SET Activo=0,Estatus=N'Cancelada',FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
WHERE ListaCargaSemanaOrigenID=@SemanaID AND ClienteID=@ClienteID AND ParteID=@ParteID AND Activo=1
AND NOT EXISTS(SELECT 1 FROM dbo.Logistica_ListaCargaProgramacionEmbarques x WHERE x.ListaCargaProgramacionID=Logistica_ListaCargaProgramacion.ListaCargaProgramacionID AND x.Activo=1);";
            await using (var cmd = new SqlCommand(sqlDesactivar, cn, tx))
            {
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@SemanaID", SqlDbType.Int).Value = model.ListaCargaSemanaID;
                cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = model.ClienteID;
                cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = model.ParteID;
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            var releases = new List<(int ReleaseDetalleID, DateTime FechaRequerida, int Disponible)>();
            const string sqlReleases = @"
SELECT d.ReleaseDetalleID,d.FechaRequerida,ISNULL(d.PendienteProgramar,0) Disponible
FROM dbo.vw_Logistica_DemandaRelease d
WHERE d.ClienteID=@ClienteID AND d.ParteID=@ParteID AND d.FechaRequerida<=@FechaFin AND ISNULL(d.PendienteProgramar,0)>0
ORDER BY d.FechaRequerida,d.ReleaseDetalleID;";
            await using (var cmd = new SqlCommand(sqlReleases, cn, tx))
            {
                cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = model.ClienteID;
                cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = model.ParteID;
                cmd.Parameters.Add("@FechaFin", SqlDbType.Date).Value = fechaFin;
                await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await rd.ReadAsync(cancellationToken)) releases.Add((Entero(rd, "ReleaseDetalleID"), (Fecha(rd, "FechaRequerida") ?? DateTime.MinValue).Date, Entero(rd, "Disponible")));
            }
            var totalDisponible = releases.Sum(x => x.Disponible);
            var totalSolicitado = model.Programaciones.Sum(x => x.Cantidad);
            if (totalDisponible <= 0) throw new InvalidOperationException("Ya no existe cantidad pendiente para programar para este cliente y parte.");
            if (totalSolicitado > totalDisponible) throw new InvalidOperationException($"Intentas programar {totalSolicitado:N0} PZA, pero actualmente solo existen {totalDisponible:N0} PZA pendientes.");
            var saldos = releases.Select(x => new { x.ReleaseDetalleID, x.FechaRequerida, Disponible = x.Disponible }).ToList();
            foreach (var detalle in model.Programaciones)
            {
                var faltante = detalle.Cantidad;
                for (var i = 0; i < saldos.Count && faltante > 0; i++)
                {
                    var saldo = saldos[i];
                    if (saldo.Disponible <= 0) continue;
                    var tomar = Math.Min(faltante, saldo.Disponible);
                    const string sqlInsert = @"
INSERT dbo.Logistica_ListaCargaProgramacion
(ListaCargaSemanaOrigenID,ReleaseDetalleID,ClienteID,ParteID,FechaRequeridaOriginal,FechaProgramadaCarga,CantidadProgramada,Estatus,Observaciones,Activo,FechaCreacion,CreadoPor)
VALUES
(@SemanaID,@ReleaseDetalleID,@ClienteID,@ParteID,@FechaOriginal,@FechaCarga,@Cantidad,N'Programada',@Observaciones,1,SYSDATETIME(),@Usuario);";
                    await using (var cmd = new SqlCommand(sqlInsert, cn, tx))
                    {
                        cmd.Parameters.Add("@SemanaID", SqlDbType.Int).Value = model.ListaCargaSemanaID;
                        cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = saldo.ReleaseDetalleID;
                        cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = model.ClienteID;
                        cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = model.ParteID;
                        cmd.Parameters.Add("@FechaOriginal", SqlDbType.Date).Value = saldo.FechaRequerida;
                        cmd.Parameters.Add("@FechaCarga", SqlDbType.Date).Value = detalle.FechaProgramadaCarga.Date;
                        cmd.Parameters.Add("@Cantidad", SqlDbType.Int).Value = tomar;
                        cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1000).Value = Db(string.IsNullOrWhiteSpace(detalle.Observaciones) ? null : detalle.Observaciones.Trim());
                        cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                        await cmd.ExecuteNonQueryAsync(cancellationToken);
                    }
                    saldos[i] = new { saldo.ReleaseDetalleID, saldo.FechaRequerida, Disponible = saldo.Disponible - tomar };
                    faltante -= tomar;
                }
                if (faltante > 0) throw new InvalidOperationException("No fue posible distribuir completamente la programación entre los Releases pendientes.");
            }
            await tx.CommitAsync(cancellationToken);
            TempData["LogisticaOk"] = $"Programación guardada correctamente. Total programado: {totalSolicitado:N0} PZA.";
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            TempData["LogisticaError"] = "No fue posible guardar la programación: " + ex.Message;
        }
        return RedirectSemana(model.Anio, model.NumeroSemana);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerarEmbarques(LogisticaListaCargaGenerarEmbarquesVm model, CancellationToken cancellationToken = default)
    {
        var acceso = await ValidarAccesoAsync();
        if (acceso != null) return acceso;
        if (model.ListaCargaSemanaID <= 0)
        {
            TempData["LogisticaError"] = "La semana seleccionada no es válida.";
            return RedirectSemana(model.Anio, model.NumeroSemana);
        }
        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            DateTime fechaInicioSemana;
            const string sqlSemana = @"SELECT FechaInicio,Estatus FROM dbo.Logistica_ListaCargaSemanas WITH(UPDLOCK,HOLDLOCK) WHERE ListaCargaSemanaID=@Id AND Activo=1;";
            await using (var cmd = new SqlCommand(sqlSemana, cn, tx))
            {
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = model.ListaCargaSemanaID;
                await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
                if (!await rd.ReadAsync(cancellationToken)) throw new InvalidOperationException("La semana seleccionada ya no existe.");
                fechaInicioSemana = (Fecha(rd, "FechaInicio") ?? DateTime.MinValue).Date;
                if (string.Equals(Texto(rd, "Estatus"), "Cerrada", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("La semana está cerrada y no permite generar embarques.");
            }
            var ids = (model.ProgramacionIDs ?? new List<int>()).Where(x => x > 0).Distinct().ToList();
            var programaciones = new List<(int ProgramacionID, int ReleaseDetalleID, int ClienteID, int ParteID, DateTime FechaOriginal, DateTime FechaCarga, int Cantidad, int PendienteGenerar, string Criticidad, string Cliente, string NumeroParte, string Descripcion, string FolioRelease, int? SolicitudProduccionID, string NumeroOF, DateTime? FechaCargaRelease, DateTime FechaRequerida, int? SecuenciaEntrega)>();
            const string sqlProgramaciones = @"
SELECT p.ListaCargaProgramacionID,p.ReleaseDetalleID,p.ClienteID,p.ParteID,p.FechaRequeridaOriginal,p.FechaProgramadaCarga,p.CantidadProgramada,
p.CantidadProgramada-ISNULL((SELECT SUM(x.CantidadAsignada) FROM dbo.Logistica_ListaCargaProgramacionEmbarques x WHERE x.ListaCargaProgramacionID=p.ListaCargaProgramacionID AND x.Activo=1),0) PendienteGenerar,
CASE WHEN p.FechaRequeridaOriginal<@FechaInicioSemana THEN N'Expeditado' ELSE N'Programado' END Criticidad,
ISNULL(cli.Nombre,N'') Cliente,ISNULL(d.NumeroParte,N'') NumeroParte,ISNULL(d.Descripcion,N'') Descripcion,ISNULL(d.FolioRelease,N'') FolioRelease,
d.SolicitudProduccionID,ISNULL(d.NumeroOF,N'') NumeroOF,d.FechaCarga FechaCargaRelease,d.FechaRequerida,d.SecuenciaEntrega
FROM dbo.Logistica_ListaCargaProgramacion p WITH(UPDLOCK,HOLDLOCK)
INNER JOIN dbo.vw_Logistica_DemandaRelease d ON d.ReleaseDetalleID=p.ReleaseDetalleID
LEFT JOIN dbo.ERP_Clientes cli ON cli.ClienteID=p.ClienteID
WHERE p.ListaCargaSemanaOrigenID=@SemanaID AND p.Activo=1 AND p.Estatus<>N'Cancelada'
AND (@FiltrarIDs=0 OR p.ListaCargaProgramacionID IN (SELECT TRY_CONVERT(int,value) FROM STRING_SPLIT(@IDs,',')))
AND p.CantidadProgramada>ISNULL((SELECT SUM(x.CantidadAsignada) FROM dbo.Logistica_ListaCargaProgramacionEmbarques x WHERE x.ListaCargaProgramacionID=p.ListaCargaProgramacionID AND x.Activo=1),0)
ORDER BY p.FechaProgramadaCarga,p.ClienteID,p.FechaRequeridaOriginal,p.ReleaseDetalleID;";
            await using (var cmd = new SqlCommand(sqlProgramaciones, cn, tx))
            {
                cmd.Parameters.Add("@SemanaID", SqlDbType.Int).Value = model.ListaCargaSemanaID;
                cmd.Parameters.Add("@FechaInicioSemana", SqlDbType.Date).Value = fechaInicioSemana;
                cmd.Parameters.Add("@FiltrarIDs", SqlDbType.Bit).Value = ids.Count > 0;
                cmd.Parameters.Add("@IDs", SqlDbType.NVarChar, -1).Value = ids.Count > 0 ? string.Join(",", ids) : string.Empty;
                await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await rd.ReadAsync(cancellationToken))
                {
                    var cantidad = Entero(rd, "PendienteGenerar");
                    if (cantidad <= 0) continue;
                    programaciones.Add((Entero(rd, "ListaCargaProgramacionID"), Entero(rd, "ReleaseDetalleID"), Entero(rd, "ClienteID"), Entero(rd, "ParteID"), (Fecha(rd, "FechaRequeridaOriginal") ?? DateTime.MinValue).Date, (Fecha(rd, "FechaProgramadaCarga") ?? DateTime.MinValue).Date, Entero(rd, "CantidadProgramada"), cantidad, Texto(rd, "Criticidad"), Texto(rd, "Cliente"), Texto(rd, "NumeroParte"), Texto(rd, "Descripcion"), Texto(rd, "FolioRelease"), EnteroNullable(rd, "SolicitudProduccionID"), Texto(rd, "NumeroOF"), Fecha(rd, "FechaCargaRelease"), (Fecha(rd, "FechaRequerida") ?? DateTime.MinValue).Date, EnteroNullable(rd, "SecuenciaEntrega")));
                }
            }
            if (programaciones.Count == 0) throw new InvalidOperationException("No existen cantidades pendientes de generar como embarque.");
            var embarquesCreados = 0;
            var piezasGeneradas = 0L;
            foreach (var grupo in programaciones.GroupBy(x => new { x.ClienteID, x.FechaCarga, x.Criticidad }).OrderBy(x => x.Key.FechaCarga).ThenBy(x => x.Key.ClienteID))
            {
                var primero = grupo.First();
                if (string.IsNullOrWhiteSpace(primero.Cliente)) throw new InvalidOperationException($"El cliente {primero.ClienteID} no tiene nombre válido.");
                const string sqlHeader = @"
INSERT dbo.Logistica_Embarques
(Folio,ClienteID,ClienteNombreSnapshot,Destino,DireccionEntrega,TipoOperacion,FormaEnvio,ModalidadEnvio,Transportista,GuiaReferencia,PasaAduana,FechaProgramada,FechaCargaProgramada,HoraCargaProgramada,FechaEntregaProgramada,HoraEntregaProgramada,Estatus,RutaID,UnidadID,OperadorTexto,ResponsableUsuarioID,ResponsableNombreSnapshot,Observaciones,FechaCreacion,CreadoPor,Activo)
VALUES
(NULL,@ClienteID,@Cliente,@Destino,NULL,N'Pendiente',N'Pendiente',NULL,NULL,NULL,NULL,@FechaCarga,@FechaCarga,NULL,@FechaEntrega,NULL,N'Programado',NULL,NULL,NULL,@UsuarioID,@Usuario,N'Embarque generado desde Lista de carga.',SYSDATETIME(),@Usuario,1);
SELECT CONVERT(int,SCOPE_IDENTITY());";
                int embarqueId;
                await using (var cmd = new SqlCommand(sqlHeader, cn, tx))
                {
                    cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = primero.ClienteID;
                    cmd.Parameters.Add("@Cliente", SqlDbType.NVarChar, 200).Value = primero.Cliente;
                    cmd.Parameters.Add("@Destino", SqlDbType.NVarChar, 300).Value = primero.Cliente;
                    cmd.Parameters.Add("@FechaCarga", SqlDbType.Date).Value = grupo.Key.FechaCarga;
                    cmd.Parameters.Add("@FechaEntrega", SqlDbType.Date).Value = grupo.Min(x => x.FechaRequerida);
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = Db(UsuarioID);
                    cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                    embarqueId = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
                }
                var folio = $"LOG-{DateTime.Today:yyyy}-{embarqueId:000000}";
                await using (var cmd = new SqlCommand("UPDATE dbo.Logistica_Embarques SET Folio=@Folio WHERE EmbarqueID=@Id;", cn, tx))
                {
                    cmd.Parameters.Add("@Folio", SqlDbType.NVarChar, 50).Value = folio;
                    cmd.Parameters.Add("@Id", SqlDbType.Int).Value = embarqueId;
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                }
                foreach (var releaseGrupo in grupo.GroupBy(x => x.ReleaseDetalleID))
                {
                    var r = releaseGrupo.First();
                    var cantidadDetalle = releaseGrupo.Sum(x => x.PendienteGenerar);
                    var detalleId = await InsertarDetalleProgramacionAsync(cn, tx, embarqueId, r.ReleaseDetalleID, r.ParteID, r.SolicitudProduccionID, r.SecuenciaEntrega, r.FolioRelease, r.FechaCargaRelease, r.FechaRequerida, r.NumeroParte, r.Descripcion, r.NumeroOF, cantidadDetalle, cancellationToken);
                    foreach (var p in releaseGrupo)
                    {
                        const string sqlRelacion = @"
INSERT dbo.Logistica_ListaCargaProgramacionEmbarques
(ListaCargaProgramacionID,EmbarqueID,EmbarqueDetalleID,CantidadAsignada,CantidadEnviada,Criticidad,Activo,FechaCreacion,CreadoPor)
VALUES(@ProgramacionID,@EmbarqueID,@DetalleID,@Cantidad,0,@Criticidad,1,SYSDATETIME(),@Usuario);";
                        await using var cmd = new SqlCommand(sqlRelacion, cn, tx);
                        cmd.Parameters.Add("@ProgramacionID", SqlDbType.Int).Value = p.ProgramacionID;
                        cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                        cmd.Parameters.Add("@DetalleID", SqlDbType.Int).Value = detalleId;
                        cmd.Parameters.Add("@Cantidad", SqlDbType.Int).Value = p.PendienteGenerar;
                        cmd.Parameters.Add("@Criticidad", SqlDbType.NVarChar, 30).Value = p.Criticidad;
                        cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                        await cmd.ExecuteNonQueryAsync(cancellationToken);
                        piezasGeneradas += p.PendienteGenerar;
                    }
                }
                const string sqlActualizar = @"
UPDATE p SET Estatus=CASE WHEN ISNULL(x.Generado,0)>=p.CantidadProgramada THEN N'Generada' ELSE N'Programada' END,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
FROM dbo.Logistica_ListaCargaProgramacion p
OUTER APPLY(SELECT SUM(e.CantidadAsignada) Generado FROM dbo.Logistica_ListaCargaProgramacionEmbarques e WHERE e.ListaCargaProgramacionID=p.ListaCargaProgramacionID AND e.Activo=1)x
WHERE p.ListaCargaProgramacionID IN(SELECT DISTINCT ListaCargaProgramacionID FROM dbo.Logistica_ListaCargaProgramacionEmbarques WHERE EmbarqueID=@EmbarqueID AND Activo=1);";
                await using (var cmd = new SqlCommand(sqlActualizar, cn, tx))
                {
                    cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                    cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                }
                const string sqlHistorial = @"
INSERT dbo.Logistica_EmbarqueHistorial(EmbarqueID,Evento,EstadoAnterior,EstadoNuevo,Observaciones,UsuarioID,UsuarioNombre,FechaEvento)
VALUES(@EmbarqueID,N'GENERADO_LISTA_CARGA',NULL,N'Programado',@Observaciones,@UsuarioID,@Usuario,SYSDATETIME());";
                await using (var cmd = new SqlCommand(sqlHistorial, cn, tx))
                {
                    cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                    cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1200).Value = $"Embarque generado desde Lista de carga. Criticidad: {grupo.Key.Criticidad}. Fecha de carga/salida programada: {grupo.Key.FechaCarga:dd/MM/yyyy}. Modalidad de salida pendiente de definir.";
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = Db(UsuarioID);
                    cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                }
                embarquesCreados++;
            }
            await tx.CommitAsync(cancellationToken);
            TempData["LogisticaOk"] = $"Se generaron {embarquesCreados:N0} embarque(s) por {piezasGeneradas:N0} PZA. La modalidad de salida queda pendiente hasta la ejecución.";
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(cancellationToken); } catch { }
            TempData["LogisticaError"] = "No fue posible generar los embarques: " + ex.Message;
        }
        return RedirectSemana(model.Anio, model.NumeroSemana);
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
SELECT d.ClienteID,ISNULL(d.Cliente,N'') Cliente,d.ParteID,ISNULL(d.NumeroParte,N'') Referencia,ISNULL(d.Descripcion,N'') Designacion,
SUM(ISNULL(d.PendienteProgramar,0)) TotalSemana,
SUM(CASE WHEN DATEDIFF(DAY,@FechaInicio,d.FechaRequerida)=0 THEN ISNULL(d.CantidadRequerida,0) ELSE 0 END) RequeridoLunes,
SUM(CASE WHEN DATEDIFF(DAY,@FechaInicio,d.FechaRequerida)=1 THEN ISNULL(d.CantidadRequerida,0) ELSE 0 END) RequeridoMartes,
SUM(CASE WHEN DATEDIFF(DAY,@FechaInicio,d.FechaRequerida)=2 THEN ISNULL(d.CantidadRequerida,0) ELSE 0 END) RequeridoMiercoles,
SUM(CASE WHEN DATEDIFF(DAY,@FechaInicio,d.FechaRequerida)=3 THEN ISNULL(d.CantidadRequerida,0) ELSE 0 END) RequeridoJueves,
SUM(CASE WHEN DATEDIFF(DAY,@FechaInicio,d.FechaRequerida)=4 THEN ISNULL(d.CantidadRequerida,0) ELSE 0 END) RequeridoViernes,
SUM(CASE WHEN DATEDIFF(DAY,@FechaInicio,d.FechaRequerida)=5 THEN ISNULL(d.CantidadRequerida,0) ELSE 0 END) RequeridoSabado,
SUM(ISNULL(d.CantidadProgramadaLogistica,0)) CantidadProgramadaLogistica,
SUM(ISNULL(d.PendienteProgramar,0)) PendienteProgramar,
SUM(ISNULL(d.CajasPTDisponibles,0)) CajasPTDisponibles,
SUM(ISNULL(d.PiezasPTDisponibles,0)) PiezasPTDisponibles
FROM dbo.vw_Logistica_DemandaRelease d
WHERE d.FechaRequerida>=@FechaInicio AND d.FechaRequerida<=@FechaFin
AND d.ClienteID IS NOT NULL AND d.ParteID IS NOT NULL
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
                    RequeridoLunes = Entero(rd, "RequeridoLunes"),
                    RequeridoMartes = Entero(rd, "RequeridoMartes"),
                    RequeridoMiercoles = Entero(rd, "RequeridoMiercoles"),
                    RequeridoJueves = Entero(rd, "RequeridoJueves"),
                    RequeridoViernes = Entero(rd, "RequeridoViernes"),
                    RequeridoSabado = Entero(rd, "RequeridoSabado"),
                    CantidadProgramadaLogistica = Entero(rd, "CantidadProgramadaLogistica"),
                    PendienteProgramar = Entero(rd, "PendienteProgramar"),
                    CajasPTDisponibles = EnteroLargo(rd, "CajasPTDisponibles"),
                    PiezasPTDisponibles = EnteroLargo(rd, "PiezasPTDisponibles")
                };
            }
        }
        const string sqlAtrasos = @"
SELECT d.ClienteID,ISNULL(d.Cliente,N'') Cliente,d.ParteID,ISNULL(d.NumeroParte,N'') Referencia,ISNULL(d.Descripcion,N'') Designacion,
SUM(ISNULL(d.PendienteProgramar,0)) Atraso
FROM dbo.vw_Logistica_DemandaRelease d
WHERE d.FechaRequerida<@FechaInicio AND ISNULL(d.PendienteProgramar,0)>0
AND d.ClienteID IS NOT NULL AND d.ParteID IS NOT NULL
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
                    fila = new LogisticaListaCargaFilaVm { ClienteID = cliente, Cliente = Texto(rd, "Cliente"), ParteID = parte, Referencia = Texto(rd, "Referencia"), Designacion = Texto(rd, "Designacion"), InicioSemana = fechaInicio.Date, FinSemana = fechaFin.Date };
                    filas[(cliente, parte)] = fila;
                }
                fila.Atraso = Entero(rd, "Atraso");
            }
        }
        await CargarReleasesPendientesAsync(cn, filas, fechaInicio, fechaFin, clienteId, q, cancellationToken);
        await CargarProgramacionesAsync(cn, filas, listaCargaSemanaId, fechaInicio, fechaFin, clienteId, cancellationToken);
        await CargarDiasHabitualesAsync(cn, filas, cancellationToken);
        const string sqlUbicaciones = @"SELECT ParteID,PiezasAlmacen,PiezasGP12,PiezasProduccion,PiezasLocalizadas,Ubicacion FROM dbo.vw_Logistica_UbicacionMaterial;";
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
        const string sqlAjustes = @"SELECT ClienteID,ParteID,UbicacionManual,Observaciones FROM dbo.Logistica_ListaCargaAjustes WHERE ListaCargaSemanaID=@ListaCargaSemanaID AND Activo=1;";
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
                if (!string.IsNullOrWhiteSpace(ubicacion)) fila.Ubicacion = ubicacion;
                fila.Observaciones = TextoNullable(rd, "Observaciones");
            }
        }
        foreach (var fila in filas.Values) if (string.IsNullOrWhiteSpace(fila.Ubicacion)) fila.Ubicacion = "SIN MATERIAL";
        var resultado = filas.Values.AsEnumerable();
        if (criticidad == "Expeditado") resultado = resultado.Where(x => x.EsExpeditado);
        else if (criticidad == "Programado") resultado = resultado.Where(x => !x.EsExpeditado);
        return resultado.OrderByDescending(x => x.EsExpeditado).ThenBy(x => x.Cliente).ThenBy(x => x.Referencia).ToList();
    }

    private static async Task CargarReleasesPendientesAsync(SqlConnection cn, Dictionary<(int ClienteID, int ParteID), LogisticaListaCargaFilaVm> filas, DateTime fechaInicio, DateTime fechaFin, int? clienteId, string? q, CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT d.ReleaseDetalleID,d.ReleaseID,ISNULL(d.FolioRelease,N'') FolioRelease,d.ClienteID,d.ParteID,
ISNULL(d.NumeroParte,N'') NumeroParte,ISNULL(d.Descripcion,N'') Descripcion,ISNULL(d.NumeroOF,N'') NumeroOF,
d.FechaRequerida,ISNULL(d.CantidadRequerida,0) CantidadRequerida,ISNULL(d.PendienteProgramar,0) CantidadPendiente,
ISNULL((SELECT SUM(p.CantidadProgramada) FROM dbo.Logistica_ListaCargaProgramacion p WHERE p.ReleaseDetalleID=d.ReleaseDetalleID AND p.Activo=1 AND p.Estatus<>N'Cancelada'),0) CantidadYaProgramada
FROM dbo.vw_Logistica_DemandaRelease d
WHERE d.FechaRequerida<=@FechaFin AND ISNULL(d.PendienteProgramar,0)>0
AND d.ClienteID IS NOT NULL AND d.ParteID IS NOT NULL
AND (@ClienteID IS NULL OR d.ClienteID=@ClienteID)
AND (@Q IS NULL OR d.Cliente LIKE N'%'+@Q+N'%' OR d.NumeroParte LIKE N'%'+@Q+N'%' OR d.Descripcion LIKE N'%'+@Q+N'%' OR d.NumeroOF LIKE N'%'+@Q+N'%')
ORDER BY d.FechaRequerida,d.ReleaseDetalleID;";
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@FechaFin", SqlDbType.Date).Value = fechaFin.Date;
        cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = Db(clienteId);
        cmd.Parameters.Add("@Q", SqlDbType.NVarChar, 250).Value = Db(q);
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await rd.ReadAsync(cancellationToken))
        {
            var cliente = Entero(rd, "ClienteID");
            var parte = Entero(rd, "ParteID");
            if (!filas.TryGetValue((cliente, parte), out var fila)) continue;
            fila.Releases.Add(new LogisticaListaCargaReleasePendienteVm
            {
                ReleaseDetalleID = Entero(rd, "ReleaseDetalleID"),
                ReleaseID = Entero(rd, "ReleaseID"),
                FolioRelease = Texto(rd, "FolioRelease"),
                ClienteID = cliente,
                ParteID = parte,
                NumeroParte = Texto(rd, "NumeroParte"),
                Descripcion = Texto(rd, "Descripcion"),
                NumeroOF = Texto(rd, "NumeroOF"),
                FechaRequerida = Fecha(rd, "FechaRequerida") ?? DateTime.MinValue,
                CantidadRequerida = Entero(rd, "CantidadRequerida"),
                CantidadPendiente = Entero(rd, "CantidadPendiente"),
                CantidadYaProgramada = Entero(rd, "CantidadYaProgramada"),
                EsExpeditado = (Fecha(rd, "FechaRequerida") ?? DateTime.MaxValue).Date < fechaInicio.Date
            });
        }
    }

    private static async Task CargarProgramacionesAsync(SqlConnection cn, Dictionary<(int ClienteID, int ParteID), LogisticaListaCargaFilaVm> filas, int listaCargaSemanaId, DateTime fechaInicio, DateTime fechaFin, int? clienteId, CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT p.ListaCargaProgramacionID,p.ListaCargaSemanaOrigenID,p.ReleaseDetalleID,p.ClienteID,p.ParteID,p.FechaRequeridaOriginal,p.FechaProgramadaCarga,p.CantidadProgramada,p.EsReprogramacion,p.Estatus,p.Observaciones,p.Activo,
ISNULL(p.CantidadGeneradaEmbarque,0) CantidadGeneradaEmbarque,ISNULL(p.CantidadEnviada,0) CantidadEnviada
FROM dbo.vw_Logistica_ListaCargaProgramacionEstado p
WHERE p.Activo=1 AND p.Estatus<>N'Cancelada'
AND (@ClienteID IS NULL OR p.ClienteID=@ClienteID)
AND (p.ListaCargaSemanaOrigenID=@ListaCargaSemanaID OR p.FechaProgramadaCarga BETWEEN @FechaInicio AND @FechaFin)
ORDER BY p.FechaProgramadaCarga,p.ListaCargaProgramacionID;";
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@ListaCargaSemanaID", SqlDbType.Int).Value = listaCargaSemanaId;
        cmd.Parameters.Add("@FechaInicio", SqlDbType.Date).Value = fechaInicio.Date;
        cmd.Parameters.Add("@FechaFin", SqlDbType.Date).Value = fechaFin.Date;
        cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = Db(clienteId);
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await rd.ReadAsync(cancellationToken))
        {
            var cliente = Entero(rd, "ClienteID");
            var parte = Entero(rd, "ParteID");
            if (!filas.TryGetValue((cliente, parte), out var fila)) continue;
            var programacion = new LogisticaListaCargaProgramacionVm
            {
                ListaCargaProgramacionID = Entero(rd, "ListaCargaProgramacionID"),
                ListaCargaSemanaOrigenID = EnteroNullable(rd, "ListaCargaSemanaOrigenID"),
                ReleaseDetalleID = Entero(rd, "ReleaseDetalleID"),
                ClienteID = cliente,
                ParteID = parte,
                FechaRequeridaOriginal = Fecha(rd, "FechaRequeridaOriginal") ?? DateTime.MinValue,
                FechaProgramadaCarga = Fecha(rd, "FechaProgramadaCarga") ?? DateTime.MinValue,
                CantidadProgramada = Entero(rd, "CantidadProgramada"),
                CantidadGeneradaEmbarque = Entero(rd, "CantidadGeneradaEmbarque"),
                CantidadEnviada = Entero(rd, "CantidadEnviada"),
                Estatus = Texto(rd, "Estatus"),
                Observaciones = TextoNullable(rd, "Observaciones"),
                EsReprogramacion = Booleano(rd, "EsReprogramacion"),
                Activo = Booleano(rd, "Activo")
            };
            fila.Programaciones.Add(programacion);
            if (programacion.FechaProgramadaCarga.Date < fechaInicio.Date || programacion.FechaProgramadaCarga.Date > fechaFin.Date) continue;
            switch ((programacion.FechaProgramadaCarga.Date - fechaInicio.Date).Days)
            {
                case 0: fila.Lunes += programacion.CantidadProgramada; break;
                case 1: fila.Martes += programacion.CantidadProgramada; break;
                case 2: fila.Miercoles += programacion.CantidadProgramada; break;
                case 3: fila.Jueves += programacion.CantidadProgramada; break;
                case 4: fila.Viernes += programacion.CantidadProgramada; break;
                case 5: fila.Sabado += programacion.CantidadProgramada; break;
            }
        }
    }

    private static async Task CargarDiasHabitualesAsync(SqlConnection cn, Dictionary<(int ClienteID, int ParteID), LogisticaListaCargaFilaVm> filas, CancellationToken cancellationToken)
    {
        if (filas.Count == 0) return;
        const string sql = @"
SELECT ClienteDiaCargaID,ClienteID,DiaSemana,Prioridad,VigenciaDesde,VigenciaHasta,Observaciones,Activo
FROM dbo.Logistica_ClienteDiasCarga
WHERE Activo=1
AND (VigenciaDesde IS NULL OR VigenciaDesde<=CAST(GETDATE() AS date))
AND (VigenciaHasta IS NULL OR VigenciaHasta>=CAST(GETDATE() AS date))
ORDER BY ClienteID,Prioridad,DiaSemana;";
        await using var cmd = new SqlCommand(sql, cn);
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await rd.ReadAsync(cancellationToken))
        {
            var clienteId = Entero(rd, "ClienteID");
            foreach (var fila in filas.Values.Where(x => x.ClienteID == clienteId))
            {
                fila.DiasHabituales.Add(new LogisticaListaCargaDiaHabitualVm
                {
                    ClienteDiaCargaID = Entero(rd, "ClienteDiaCargaID"),
                    ClienteID = clienteId,
                    DiaSemana = Entero(rd, "DiaSemana"),
                    Prioridad = Entero(rd, "Prioridad"),
                    VigenciaDesde = Fecha(rd, "VigenciaDesde"),
                    VigenciaHasta = Fecha(rd, "VigenciaHasta"),
                    Observaciones = TextoNullable(rd, "Observaciones"),
                    Activo = Booleano(rd, "Activo")
                });
            }
        }
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
SELECT DISTINCT d.ClienteID,d.Cliente
FROM dbo.vw_Logistica_DemandaRelease d
WHERE d.ClienteID IS NOT NULL AND d.FechaRequerida<=@FechaFin
AND (d.FechaRequerida>=@FechaInicio OR ISNULL(d.PendienteProgramar,0)>0)
ORDER BY d.Cliente;";
        await using (var cmd = new SqlCommand(sql, cn))
        {
            cmd.Parameters.Add("@FechaInicio", SqlDbType.Date).Value = fechaInicio.Date;
            cmd.Parameters.Add("@FechaFin", SqlDbType.Date).Value = fechaFin.Date;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await rd.ReadAsync(cancellationToken)) resultado.Add(new LogisticaListaCargaClienteVm { ClienteID = Entero(rd, "ClienteID"), Cliente = Texto(rd, "Cliente") });
        }
        if (resultado.Count == 0) return resultado;
        const string sqlDias = @"
SELECT ClienteDiaCargaID,ClienteID,DiaSemana,Prioridad,VigenciaDesde,VigenciaHasta,Observaciones,Activo
FROM dbo.Logistica_ClienteDiasCarga
WHERE Activo=1
AND (VigenciaDesde IS NULL OR VigenciaDesde<=@FechaFin)
AND (VigenciaHasta IS NULL OR VigenciaHasta>=@FechaInicio)
ORDER BY ClienteID,Prioridad,DiaSemana;";
        await using (var cmd = new SqlCommand(sqlDias, cn))
        {
            cmd.Parameters.Add("@FechaInicio", SqlDbType.Date).Value = fechaInicio.Date;
            cmd.Parameters.Add("@FechaFin", SqlDbType.Date).Value = fechaFin.Date;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await rd.ReadAsync(cancellationToken))
            {
                var cliente = resultado.FirstOrDefault(x => x.ClienteID == Entero(rd, "ClienteID"));
                if (cliente == null) continue;
                cliente.DiasHabituales.Add(new LogisticaListaCargaDiaHabitualVm
                {
                    ClienteDiaCargaID = Entero(rd, "ClienteDiaCargaID"),
                    ClienteID = cliente.ClienteID,
                    DiaSemana = Entero(rd, "DiaSemana"),
                    Prioridad = Entero(rd, "Prioridad"),
                    VigenciaDesde = Fecha(rd, "VigenciaDesde"),
                    VigenciaHasta = Fecha(rd, "VigenciaHasta"),
                    Observaciones = TextoNullable(rd, "Observaciones"),
                    Activo = Booleano(rd, "Activo")
                });
            }
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

    private static async Task<int> InsertarDetalleProgramacionAsync(SqlConnection cn, SqlTransaction tx, int embarqueId, int releaseDetalleId, int parteId, int? solicitudProduccionId, int? secuenciaEntrega, string folioRelease, DateTime? fechaCargaRelease, DateTime fechaRequerida, string numeroParte, string descripcion, string numeroOF, int cantidad, CancellationToken cancellationToken)
    {
        if (embarqueId <= 0 || releaseDetalleId <= 0 || parteId <= 0 || cantidad <= 0) throw new InvalidOperationException("Los datos para generar la partida del embarque no son válidos.");
        int? solicitudDetalleId = null;
        if (solicitudProduccionId.HasValue && solicitudProduccionId.Value > 0)
        {
            const string sqlSolicitud = @"SELECT TOP(1) SolicitudProduccionDetalleID FROM dbo.SolicitudesProduccionDetalle WHERE SolicitudProduccionID=@SolicitudID AND ParteID=@ParteID AND Activo=1 ORDER BY CASE WHEN Renglon=@Secuencia THEN 0 ELSE 1 END,SolicitudProduccionDetalleID;";
            await using var cmd = new SqlCommand(sqlSolicitud, cn, tx);
            cmd.Parameters.Add("@SolicitudID", SqlDbType.Int).Value = solicitudProduccionId.Value;
            cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = parteId;
            cmd.Parameters.Add("@Secuencia", SqlDbType.Int).Value = Db(secuenciaEntrega);
            var valor = await cmd.ExecuteScalarAsync(cancellationToken);
            if (valor != null && valor != DBNull.Value) solicitudDetalleId = Convert.ToInt32(valor);
        }
        const string sql = @"
INSERT dbo.Logistica_EmbarqueDetalle
(EmbarqueID,ParteID,SolicitudProduccionID,SolicitudProduccionDetalleID,ReleaseDetalleID,FolioReleaseSnapshot,FechaCargaReleaseSnapshot,FechaEntregaReleaseSnapshot,SecuenciaEntregaSnapshot,NumeroParteSnapshot,DescripcionParteSnapshot,NumeroOFSnapshot,CantidadSolicitada,CantidadDespachada,Activo,FechaCreacion)
OUTPUT INSERTED.EmbarqueDetalleID
VALUES
(@EmbarqueID,@ParteID,@SolicitudProduccionID,@SolicitudProduccionDetalleID,@ReleaseDetalleID,@FolioRelease,@FechaCargaRelease,@FechaRequerida,@SecuenciaEntrega,@NumeroParte,@Descripcion,@NumeroOF,@Cantidad,0,1,SYSDATETIME());";
        await using var insert = new SqlCommand(sql, cn, tx);
        insert.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
        insert.Parameters.Add("@ParteID", SqlDbType.Int).Value = parteId;
        insert.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = Db(solicitudProduccionId);
        insert.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value = Db(solicitudDetalleId);
        insert.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = releaseDetalleId;
        insert.Parameters.Add("@FolioRelease", SqlDbType.NVarChar, 80).Value = folioRelease ?? string.Empty;
        insert.Parameters.Add("@FechaCargaRelease", SqlDbType.Date).Value = Db(fechaCargaRelease?.Date);
        insert.Parameters.Add("@FechaRequerida", SqlDbType.Date).Value = fechaRequerida.Date;
        insert.Parameters.Add("@SecuenciaEntrega", SqlDbType.Int).Value = Db(secuenciaEntrega);
        insert.Parameters.Add("@NumeroParte", SqlDbType.NVarChar, 120).Value = numeroParte ?? string.Empty;
        insert.Parameters.Add("@Descripcion", SqlDbType.NVarChar, 500).Value = descripcion ?? string.Empty;
        insert.Parameters.Add("@NumeroOF", SqlDbType.NVarChar, 100).Value = numeroOF ?? string.Empty;
        insert.Parameters.Add("@Cantidad", SqlDbType.Int).Value = cantidad;
        return Convert.ToInt32(await insert.ExecuteScalarAsync(cancellationToken));
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
AND OBJECT_ID(N'dbo.Logistica_ListaCargaProgramacion',N'U') IS NOT NULL
AND OBJECT_ID(N'dbo.Logistica_ListaCargaProgramacionEmbarques',N'U') IS NOT NULL
AND OBJECT_ID(N'dbo.Logistica_ClienteDiasCarga',N'U') IS NOT NULL
AND OBJECT_ID(N'dbo.vw_Logistica_DemandaRelease',N'V') IS NOT NULL
AND OBJECT_ID(N'dbo.vw_Logistica_ListaCargaProgramacionEstado',N'V') IS NOT NULL
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