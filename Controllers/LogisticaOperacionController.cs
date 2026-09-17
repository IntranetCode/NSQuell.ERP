using ERP.NSQuell.Models.ViewModels.Logistica;
using ERP.NSQuell.Servicios;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Globalization;

namespace ERP.NSQuell.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LogisticaOperacionController : Controller
{
    private readonly IConfiguration _configuration;
    private readonly IServicioAcceso _acceso;
    private readonly IWebHostEnvironment _environment;

    public LogisticaOperacionController(
        IConfiguration configuration,
        IServicioAcceso acceso,
        IWebHostEnvironment environment)
    {
        _configuration = configuration;
        _acceso = acceso;
        _environment = environment;
    }

    private string ConnectionString =>
        _configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("No se encontró ConnectionStrings:DefaultConnection.");

    private int? UsuarioID =>
        HttpContext.Session.GetInt32("UsuarioID");

    private string UsuarioNombre =>
        HttpContext.Session.GetString("NombreMostrar")
        ?? HttpContext.Session.GetString("Username")
        ?? User?.Identity?.Name
        ?? "Usuario";

    private async Task<IActionResult?> ValidarAccesoAsync()
    {
        if (!UsuarioID.HasValue || UsuarioID.Value <= 0)
            return RedirectToAction("Login", "Login");

        if (!await _acceso.TienePermisoAsync(UsuarioID.Value, "Tablero de Logística"))
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
    public async Task<IActionResult> Index(string vista = "Semana", DateTime? fecha = null, int? anio = null, int? semana = null, string? q = null, int? clienteId = null, string? estatus = null, string? formaEnvio = null, bool soloExpeditados = false, bool soloIncidencias = false, CancellationToken cancellationToken = default)
    {
        var acceso = await ValidarAccesoAsync();
        if (acceso != null) return acceso;
        var periodo = ResolverPeriodo(vista, fecha, anio, semana);
        q = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
        estatus = string.IsNullOrWhiteSpace(estatus) ? null : estatus.Trim();
        formaEnvio = string.IsNullOrWhiteSpace(formaEnvio) ? null : NormalizarFormaEnvio(formaEnvio);
        await using var cn = await AbrirAsync(cancellationToken);
        var vm = await CargarOperacionAsync(cn, periodo.Vista, periodo.Referencia, periodo.Anio, periodo.Semana, periodo.Inicio, periodo.Fin, periodo.Anterior, periodo.Siguiente, q, clienteId, estatus, formaEnvio, soloExpeditados, soloIncidencias, cancellationToken);
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> ObtenerSemana(string vista = "Semana", DateTime? fecha = null, int? anio = null, int? semana = null, string? q = null, int? clienteId = null, string? estatus = null, string? formaEnvio = null, bool soloExpeditados = false, bool soloIncidencias = false, CancellationToken cancellationToken = default)
    {
        var acceso = await ValidarAccesoAsync();
        if (acceso != null) return acceso;
        var periodo = ResolverPeriodo(vista, fecha, anio, semana);
        q = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
        estatus = string.IsNullOrWhiteSpace(estatus) ? null : estatus.Trim();
        formaEnvio = string.IsNullOrWhiteSpace(formaEnvio) ? null : NormalizarFormaEnvio(formaEnvio);
        await using var cn = await AbrirAsync(cancellationToken);
        var vm = await CargarOperacionAsync(cn, periodo.Vista, periodo.Referencia, periodo.Anio, periodo.Semana, periodo.Inicio, periodo.Fin, periodo.Anterior, periodo.Siguiente, q, clienteId, estatus, formaEnvio, soloExpeditados, soloIncidencias, cancellationToken);
        return Json(new
        {
            ok = true,
            vista = vm.Vista,
            fechaReferencia = vm.FechaReferencia.ToString("yyyy-MM-dd"),
            anio = vm.Anio,
            semana = vm.NumeroSemana,
            semanaTexto = vm.SemanaTexto,
            periodoTexto = vm.PeriodoTexto,
            rangoTexto = vm.RangoTexto,
            fechaInicio = vm.FechaInicio.ToString("yyyy-MM-dd"),
            fechaFin = vm.FechaFin.ToString("yyyy-MM-dd"),
            anterior = vm.FechaAnterior.ToString("yyyy-MM-dd"),
            siguiente = vm.FechaSiguiente.ToString("yyyy-MM-dd"),
            resumen = new
            {
                clientes = vm.TotalClientes,
                eventos = vm.TotalEventos,
                requerimientos = vm.TotalRequerimientos,
                embarques = vm.TotalEmbarques,
                programaciones = vm.TotalProgramaciones,
                pendienteProgramar = vm.TotalPendienteProgramar,
                programados = vm.Programados,
                preparando = vm.Preparando,
                preparados = vm.Preparados,
                cargados = vm.Cargados,
                enRuta = vm.EnRuta,
                entregados = vm.Entregados,
                incidencias = vm.ConIncidencia,
                expeditados = vm.Expeditados,
                piezas = vm.TotalPiezas,
                unidades = vm.TotalUnidades,
                unidadesDisponibles = vm.UnidadesDisponibles,
                unidadesConOperacion = vm.UnidadesConOperacion,
                viajesActivos = vm.ViajesActivos
            },
            clientes = vm.Clientes.Select(c => new
            {
                clienteId = c.ClienteID,
                cliente = c.Cliente,
                totalEventos = c.TotalEventos,
                totalRequerimientos = c.TotalRequerimientos,
                totalEmbarques = c.TotalEmbarques,
                totalProgramaciones = c.TotalProgramaciones,
                totalPendienteProgramar = c.TotalPendienteProgramar,
                totalPiezas = c.TotalPiezas,
                tieneExpeditados = c.TieneExpeditados,
                tieneIncidencias = c.TieneIncidencias,
                eventos = c.Eventos.Select(EventoJson).ToList()
            }).ToList(),
            unidades = vm.Unidades.Select(u => new
            {
                u.UnidadID,
                u.NumeroEconomico,
                u.Placas,
                u.Marca,
                u.Modelo,
                u.CapacidadPiezas,
                u.CapacidadPesoKg,
                u.UnidadTexto,
                u.DescripcionUnidad,
                u.DisponibleEnPeriodo,
                u.EstadoActual,
                eventos = u.Eventos.Select(UnidadEventoJson).ToList()
            }).ToList()
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ProgramarRelease(LogisticaOperacionProgramarReleaseVm model, CancellationToken cancellationToken = default)
    {
        var acceso = await ValidarAccesoAsync();
        if (acceso != null) return acceso;
        if (model.ReleaseDetalleID <= 0 || model.ClienteID <= 0 || model.ParteID <= 0) return BadRequest(new { ok = false, mensaje = "Los datos del Release no son válidos." });
        if (model.Cantidad <= 0) return BadRequest(new { ok = false, mensaje = "La cantidad debe ser mayor a cero." });
        if (model.FechaProgramadaCarga == default) return BadRequest(new { ok = false, mensaje = "Selecciona el día programado." });
        if (model.FechaProgramadaCarga.Date < DateTime.Today) return BadRequest(new { ok = false, mensaje = "No puedes programar un Release en un día que ya pasó." });
        model.Observaciones = string.IsNullOrWhiteSpace(model.Observaciones) ? null : model.Observaciones.Trim();
        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            const string sqlDemanda = @"
SELECT TOP(1) d.ReleaseDetalleID,d.ClienteID,d.ParteID,ISNULL(d.FolioRelease,N'') FolioRelease,ISNULL(d.Cliente,N'') Cliente,ISNULL(d.NumeroParte,N'') NumeroParte,d.FechaRequerida,
ISNULL(d.CantidadRequerida,0) CantidadRequerida,ISNULL(d.PendienteProgramar,0) PendienteProgramar,ISNULL(p.ProgramadoActivo,0) ProgramadoActivo
FROM dbo.vw_Logistica_DemandaRelease d
OUTER APPLY
(
    SELECT ISNULL(SUM(x.CantidadProgramada),0) ProgramadoActivo
    FROM dbo.Logistica_ListaCargaProgramacion x
    WHERE x.ReleaseDetalleID=d.ReleaseDetalleID AND x.Activo=1 AND ISNULL(x.Estatus,N'Programada')<>N'Cancelada'
) p
WHERE d.ReleaseDetalleID=@ReleaseDetalleID;";
            int clienteId, parteId, pendiente;
            string folioRelease, cliente, numeroParte;
            DateTime fechaRequerida;
            await using (var cmd = new SqlCommand(sqlDemanda, cn, tx))
            {
                cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = model.ReleaseDetalleID;
                await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
                if (!await rd.ReadAsync(cancellationToken)) throw new InvalidOperationException("El Release ya no está disponible.");
                clienteId = Entero(rd, "ClienteID");
                parteId = Entero(rd, "ParteID");
                var pendienteVista = Entero(rd, "PendienteProgramar");
                var cantidadRequerida = Entero(rd, "CantidadRequerida");
                var programadoActivo = Entero(rd, "ProgramadoActivo");
                pendiente = Math.Min(Math.Max(0, pendienteVista), Math.Max(0, cantidadRequerida - programadoActivo));
                folioRelease = Texto(rd, "FolioRelease");
                cliente = Texto(rd, "Cliente");
                numeroParte = Texto(rd, "NumeroParte");
                fechaRequerida = (Fecha(rd, "FechaRequerida") ?? DateTime.MinValue).Date;
            }
            if (clienteId != model.ClienteID || parteId != model.ParteID) throw new InvalidOperationException("El cliente o la parte no coinciden con el Release.");
            if (pendiente <= 0) throw new InvalidOperationException("Este Release ya no tiene cantidad pendiente por programar.");
            if (model.Cantidad > pendiente) throw new InvalidOperationException($"Intentas programar {model.Cantidad:N0} PZA, pero el Release solo tiene {pendiente:N0} PZA pendientes.");
            var semanaId = await ObtenerOCrearSemanaIdAsync(cn, tx, model.FechaProgramadaCarga.Date, cancellationToken);
            const string sqlInsert = @"
INSERT dbo.Logistica_ListaCargaProgramacion
(ListaCargaSemanaOrigenID,ReleaseDetalleID,ClienteID,ParteID,FechaRequeridaOriginal,FechaProgramadaCarga,HoraProgramadaCarga,CantidadProgramada,Estatus,Observaciones,Activo,FechaCreacion,CreadoPor)
VALUES
(@SemanaID,@ReleaseDetalleID,@ClienteID,@ParteID,@FechaRequerida,@FechaCarga,NULL,@Cantidad,N'Programada',@Observaciones,1,SYSDATETIME(),@Usuario);
SELECT CONVERT(int,SCOPE_IDENTITY());";
            int programacionId;
            await using (var cmd = new SqlCommand(sqlInsert, cn, tx))
            {
                cmd.Parameters.Add("@SemanaID", SqlDbType.Int).Value = semanaId;
                cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = model.ReleaseDetalleID;
                cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = clienteId;
                cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = parteId;
                cmd.Parameters.Add("@FechaRequerida", SqlDbType.Date).Value = fechaRequerida;
                cmd.Parameters.Add("@FechaCarga", SqlDbType.Date).Value = model.FechaProgramadaCarga.Date;
                cmd.Parameters.Add("@Cantidad", SqlDbType.Int).Value = model.Cantidad;
                cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1000).Value = Db(model.Observaciones);
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                programacionId = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
            }
            await tx.CommitAsync(cancellationToken);
            return Json(new { ok = true, mensaje = $"{folioRelease} programado para {model.FechaProgramadaCarga:dd/MM/yyyy}.", programacionId, releaseDetalleId = model.ReleaseDetalleID, clienteId, cliente, parteId, numeroParte, cantidad = model.Cantidad, fechaRequerida = fechaRequerida.ToString("yyyy-MM-dd"), fechaProgramada = model.FechaProgramadaCarga.ToString("yyyy-MM-dd"), horaProgramada = (string?)null });
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(cancellationToken); } catch { }
            return BadRequest(new { ok = false, mensaje = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MoverProgramacion(LogisticaOperacionMoverProgramacionVm model, CancellationToken cancellationToken = default)
    {
        var acceso = await ValidarAccesoAsync();
        if (acceso != null) return acceso;
        if (model.ListaCargaProgramacionID <= 0) return BadRequest(new { ok = false, mensaje = "La programación no es válida." });
        if (model.FechaProgramadaCarga == default) return BadRequest(new { ok = false, mensaje = "Selecciona el nuevo día." });
        if (model.FechaProgramadaCarga.Date < DateTime.Today) return BadRequest(new { ok = false, mensaje = "No puedes mover la programación a un día que ya terminó." });
        model.Observaciones = string.IsNullOrWhiteSpace(model.Observaciones) ? null : model.Observaciones.Trim();
        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            const string sqlActual = @"
SELECT p.ListaCargaProgramacionID,p.ReleaseDetalleID,p.ClienteID,p.ParteID,p.FechaRequeridaOriginal,p.FechaProgramadaCarga,p.CantidadProgramada,ISNULL(p.Estatus,N'Programada') Estatus,
ISNULL(g.CantidadGenerada,0) CantidadGenerada,ISNULL(g.CantidadEnviada,0) CantidadEnviada
FROM dbo.Logistica_ListaCargaProgramacion p WITH(UPDLOCK,HOLDLOCK)
OUTER APPLY
(
    SELECT SUM(CONVERT(bigint,x.CantidadAsignada)) CantidadGenerada,SUM(CONVERT(bigint,x.CantidadEnviada)) CantidadEnviada
    FROM dbo.Logistica_ListaCargaProgramacionEmbarques x WITH(UPDLOCK,HOLDLOCK)
    WHERE x.ListaCargaProgramacionID=p.ListaCargaProgramacionID AND x.Activo=1
) g
WHERE p.ListaCargaProgramacionID=@ID AND p.Activo=1;";
            int releaseDetalleId, clienteId, parteId, cantidadProgramada, cantidadGenerada, cantidadEnviada;
            DateTime fechaOriginal, fechaAnterior;
            string estatus;
            await using (var cmd = new SqlCommand(sqlActual, cn, tx))
            {
                cmd.Parameters.Add("@ID", SqlDbType.Int).Value = model.ListaCargaProgramacionID;
                await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
                if (!await rd.ReadAsync(cancellationToken)) throw new InvalidOperationException("La programación ya no existe.");
                releaseDetalleId = Entero(rd, "ReleaseDetalleID");
                clienteId = Entero(rd, "ClienteID");
                parteId = Entero(rd, "ParteID");
                fechaOriginal = (Fecha(rd, "FechaRequeridaOriginal") ?? DateTime.MinValue).Date;
                fechaAnterior = (Fecha(rd, "FechaProgramadaCarga") ?? DateTime.MinValue).Date;
                cantidadProgramada = Entero(rd, "CantidadProgramada");
                cantidadGenerada = Convert.ToInt32(EnteroLargo(rd, "CantidadGenerada"));
                cantidadEnviada = Convert.ToInt32(EnteroLargo(rd, "CantidadEnviada"));
                estatus = Texto(rd, "Estatus");
            }
            if (estatus.Equals("Cancelada", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("La programación está cancelada.");
            if (cantidadProgramada <= 0) throw new InvalidOperationException("La programación no contiene una cantidad válida.");
            if (cantidadGenerada > cantidadProgramada) throw new InvalidOperationException("La programación tiene más cantidad generada que programada. Revisa su trazabilidad.");
            if (cantidadGenerada >= cantidadProgramada) throw new InvalidOperationException("Toda la programación ya fue convertida a embarque. Debes reprogramar el embarque, no la programación.");
            if (fechaAnterior == model.FechaProgramadaCarga.Date) throw new InvalidOperationException("La programación ya se encuentra en ese día.");
            var semanaId = await ObtenerOCrearSemanaIdAsync(cn, tx, model.FechaProgramadaCarga.Date, cancellationToken);
            if (cantidadGenerada == 0)
            {
                const string sqlUpdate = @"
UPDATE dbo.Logistica_ListaCargaProgramacion
SET ListaCargaSemanaOrigenID=@SemanaID,FechaProgramadaCarga=@FechaCarga,HoraProgramadaCarga=NULL,
Observaciones=CASE WHEN @Observaciones IS NULL THEN Observaciones WHEN NULLIF(LTRIM(RTRIM(ISNULL(Observaciones,N''))),N'') IS NULL THEN @Observaciones ELSE CONCAT(Observaciones,NCHAR(13),NCHAR(10),@Observaciones) END,
FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
WHERE ListaCargaProgramacionID=@ID AND Activo=1;";
                await using var cmd = new SqlCommand(sqlUpdate, cn, tx);
                cmd.Parameters.Add("@SemanaID", SqlDbType.Int).Value = semanaId;
                cmd.Parameters.Add("@FechaCarga", SqlDbType.Date).Value = model.FechaProgramadaCarga.Date;
                cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1000).Value = Db(model.Observaciones);
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@ID", SqlDbType.Int).Value = model.ListaCargaProgramacionID;
                if (await cmd.ExecuteNonQueryAsync(cancellationToken) != 1) throw new InvalidOperationException("No fue posible mover la programación.");
                await tx.CommitAsync(cancellationToken);
                return Json(new { ok = true, mensaje = $"Programación movida del {fechaAnterior:dd/MM/yyyy} al {model.FechaProgramadaCarga:dd/MM/yyyy}.", programacionId = model.ListaCargaProgramacionID, programacionOrigenId = model.ListaCargaProgramacionID, saldoSeparado = false, cantidad = cantidadProgramada, fechaRequerida = fechaOriginal.ToString("yyyy-MM-dd"), fechaProgramada = model.FechaProgramadaCarga.ToString("yyyy-MM-dd"), horaProgramada = (string?)null });
            }
            var saldo = cantidadProgramada - cantidadGenerada;
            const string sqlCerrarOrigen = @"
UPDATE dbo.Logistica_ListaCargaProgramacion
SET CantidadProgramada=@CantidadGenerada,
Estatus=CASE WHEN @CantidadEnviada>=@CantidadGenerada THEN N'Cumplida' WHEN @CantidadEnviada>0 THEN N'Parcial' ELSE N'Generada' END,
FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
WHERE ListaCargaProgramacionID=@ID AND Activo=1;";
            await using (var cmd = new SqlCommand(sqlCerrarOrigen, cn, tx))
            {
                cmd.Parameters.Add("@CantidadGenerada", SqlDbType.Int).Value = cantidadGenerada;
                cmd.Parameters.Add("@CantidadEnviada", SqlDbType.Int).Value = cantidadEnviada;
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@ID", SqlDbType.Int).Value = model.ListaCargaProgramacionID;
                if (await cmd.ExecuteNonQueryAsync(cancellationToken) != 1) throw new InvalidOperationException("No fue posible proteger la porción ya convertida a embarque.");
            }
            var observacionResidual = $"Saldo Expeditado separado automáticamente de PROG-{model.ListaCargaProgramacionID:000000}. Programado original: {cantidadProgramada:N0} PZA. Ya generado: {cantidadGenerada:N0} PZA. Saldo reprogramado: {saldo:N0} PZA.";
            if (!string.IsNullOrWhiteSpace(model.Observaciones)) observacionResidual += $" {model.Observaciones}";
            if (observacionResidual.Length > 1000) observacionResidual = observacionResidual[..1000];
            const string sqlInsert = @"
INSERT dbo.Logistica_ListaCargaProgramacion
(ListaCargaSemanaOrigenID,ReleaseDetalleID,ClienteID,ParteID,FechaRequeridaOriginal,FechaProgramadaCarga,HoraProgramadaCarga,CantidadProgramada,EsReprogramacion,Estatus,Observaciones,Activo,FechaCreacion,CreadoPor)
VALUES
(@SemanaID,@ReleaseDetalleID,@ClienteID,@ParteID,@FechaOriginal,@FechaCarga,NULL,@Cantidad,1,N'Parcial',@Observaciones,1,SYSDATETIME(),@Usuario);
SELECT CONVERT(int,SCOPE_IDENTITY());";
            int nuevaProgramacionId;
            await using (var cmd = new SqlCommand(sqlInsert, cn, tx))
            {
                cmd.Parameters.Add("@SemanaID", SqlDbType.Int).Value = semanaId;
                cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = releaseDetalleId;
                cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = clienteId;
                cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = parteId;
                cmd.Parameters.Add("@FechaOriginal", SqlDbType.Date).Value = fechaOriginal;
                cmd.Parameters.Add("@FechaCarga", SqlDbType.Date).Value = model.FechaProgramadaCarga.Date;
                cmd.Parameters.Add("@Cantidad", SqlDbType.Int).Value = saldo;
                cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1000).Value = observacionResidual;
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                nuevaProgramacionId = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
            }
            await tx.CommitAsync(cancellationToken);
            return Json(new
            {
                ok = true,
                mensaje = $"Se protegieron {cantidadGenerada:N0} PZA ya ligadas a embarque y se reprogramaron {saldo:N0} PZA Expeditadas al {model.FechaProgramadaCarga:dd/MM/yyyy}.",
                programacionId = nuevaProgramacionId,
                programacionOrigenId = model.ListaCargaProgramacionID,
                saldoSeparado = true,
                cantidad = saldo,
                fechaRequerida = fechaOriginal.ToString("yyyy-MM-dd"),
                fechaProgramada = model.FechaProgramadaCarga.ToString("yyyy-MM-dd"),
                horaProgramada = (string?)null
            });
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(cancellationToken); } catch { }
            return BadRequest(new { ok = false, mensaje = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> ObtenerDetalle(int embarqueId, CancellationToken cancellationToken = default)
    {
        var acceso = await ValidarAccesoAsync();
        if (acceso != null) return acceso;
        if (embarqueId <= 0) return BadRequest(new { ok = false, mensaje = "El embarque indicado no es válido." });
        try
        {
            await using var cn = await AbrirAsync(cancellationToken);
            var vm = await CargarDetalleAsync(cn, embarqueId, cancellationToken);
            if (vm == null) return NotFound(new { ok = false, mensaje = "El embarque no existe o ya no está activo." });
            return Json(new
            {
                ok = true,
                embarque = new
                {
                    vm.EmbarqueID,
                    vm.Folio,
                    vm.ClienteID,
                    vm.Cliente,
                    vm.Destino,
                    vm.DireccionEntrega,
                    fechaCargaProgramada = vm.FechaCargaProgramada?.ToString("yyyy-MM-dd"),
                    horaCargaProgramada = vm.HoraCargaProgramada?.ToString(@"hh\:mm"),
                    fechaEntregaProgramada = vm.FechaEntregaProgramada?.ToString("yyyy-MM-dd"),
                    vm.Estatus,
                    vm.TipoOperacion,
                    vm.FormaEnvio,
                    vm.ModalidadEnvio,
                    vm.Transportista,
                    vm.GuiaReferencia,
                    vm.Ruta,
                    vm.Unidad,
                    vm.Chofer,
                    vm.TotalPartidas,
                    vm.TotalCajas,
                    vm.TotalPiezas,
                    vm.TotalPiezasPreparadas,
                    vm.TotalPiezasDespachadas,
                    vm.PorcentajeAvance,
                    vm.TieneIncidencia,
                    vm.IncidenciasAbiertas,
                    vm.IncidenciasCriticas,
                    vm.TotalDocumentos,
                    vm.DocumentosFaltantes,
                    vm.TotalEvidencias,
                    vm.DatosTransporteCompletos,
                    vm.PreparacionCompleta,
                    vm.DocumentacionCompleta,
                    vm.ProximaAccion,
                    vm.ProximaAccionDetalle,
                    vm.PuedeDefinirSalida,
                    vm.PuedePreparar,
                    vm.PuedeConfirmarCarga,
                    vm.PuedeCerrar,
                    vm.Cerrado,
                    partidas = vm.Partidas.Select(x => new
                    {
                        x.EmbarqueDetalleID,
                        x.ReleaseDetalleID,
                        x.FolioRelease,
                        x.NumeroParte,
                        x.Descripcion,
                        x.NumeroOF,
                        x.CantidadSolicitada,
                        x.CantidadPreparada,
                        x.CantidadDespachada,
                        x.PendientePreparar
                    }).ToList(),
                    historial = vm.Historial.Select(x => new
                    {
                        x.HistorialID,
                        x.Evento,
                        x.EstadoAnterior,
                        x.EstadoNuevo,
                        x.Observaciones,
                        x.UsuarioID,
                        x.Usuario,
                        fecha = x.Fecha.ToString("dd/MM/yyyy HH:mm")
                    }).ToList(),
                    evidencias = vm.Evidencias.Select(x => new
                    {
                        x.EvidenciaID,
                        x.ViajeID,
                        x.Origen,
                        x.TipoEvidencia,
                        x.NombreOriginal,
                        x.TipoContenido,
                        x.TamanoBytes,
                        x.TamanoTexto,
                        x.Observaciones,
                        x.EsImagen,
                        x.EsPdf,
                        x.Icono,
                        fechaCarga = x.FechaCarga.ToString("dd/MM/yyyy HH:mm"),
                        x.Usuario
                    }).ToList()
                }
            });
        }
        catch (SqlException ex)
        {
            return StatusCode(500, new { ok = false, tipo = "SQL", mensaje = ex.Message, numero = ex.Number, embarqueId });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { ok = false, tipo = "GENERAL", mensaje = ex.Message, embarqueId });
        }
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReprogramarDragDrop(LogisticaOperacionMoverVm model, CancellationToken cancellationToken = default)
    {
        var acceso = await ValidarAccesoAsync();
        if (acceso != null) return acceso;
        if (model.EmbarqueID <= 0) return BadRequest(new { ok = false, mensaje = "El embarque indicado no es válido." });
        if (model.FechaCargaProgramada == default) return BadRequest(new { ok = false, mensaje = "La nueva fecha es obligatoria." });
        if (!model.HoraCargaProgramada.HasValue) return BadRequest(new { ok = false, mensaje = "La nueva hora es obligatoria." });
        var nuevaFechaHora = model.FechaCargaProgramada.Date.Add(model.HoraCargaProgramada.Value);
        if (nuevaFechaHora < DateTime.Now) return BadRequest(new { ok = false, mensaje = "No puedes mover el embarque a una fecha u hora que ya pasó." });
        if (string.IsNullOrWhiteSpace(model.RowVersion)) return Conflict(new { ok = false, recargar = true, mensaje = "No se recibió la versión del embarque. Recarga el calendario." });
        byte[] rowVersionOriginal;
        try { rowVersionOriginal = Convert.FromBase64String(model.RowVersion); }
        catch { return Conflict(new { ok = false, recargar = true, mensaje = "La versión del embarque no es válida. Recarga el calendario." }); }
        model.Observaciones = model.Observaciones?.Trim();
        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            const string sqlActual = @"
SELECT ISNULL(e.Folio,N'') Folio,ISNULL(e.ClienteNombreSnapshot,N'') Cliente,ISNULL(e.Estatus,N'') Estatus,e.FechaCargaProgramada,e.HoraCargaProgramada,e.FechaSalida,
ISNULL(e.FormaEnvio,N'Pendiente') FormaEnvio,e.RutaID,e.UnidadID,e.ChoferUsuarioID,CONVERT(varbinary(8),e.RowVersion) RowVersion,
v.ViajeID,ISNULL(v.Estatus,N'') EstatusViaje,ISNULL(c.CajasCargadas,0) CajasCargadas,ISNULL(c.CajasDespachadas,0) CajasDespachadas
FROM dbo.Logistica_Embarques e WITH(UPDLOCK,HOLDLOCK)
OUTER APPLY
(
    SELECT TOP(1) vx.ViajeID,vx.Estatus
    FROM dbo.Logistica_ViajeEmbarques ve
    INNER JOIN dbo.Logistica_Viajes vx WITH(UPDLOCK,HOLDLOCK) ON vx.ViajeID=ve.ViajeID AND vx.Activo=1
    WHERE ve.EmbarqueID=e.EmbarqueID AND ve.Activo=1 AND vx.Estatus<>N'Cancelado'
    ORDER BY CASE WHEN vx.Estatus=N'Programado' THEN 0 WHEN vx.Estatus=N'En curso' THEN 1 ELSE 2 END,ve.ViajeEmbarqueID DESC
) v
OUTER APPLY
(
    SELECT
    COUNT(DISTINCT CASE WHEN ec.EstatusSeleccion=N'Cargada' THEN ec.CajaID END) CajasCargadas,
    COUNT(DISTINCT CASE WHEN ec.EstatusSeleccion=N'Despachada' THEN ec.CajaID END) CajasDespachadas
    FROM dbo.Logistica_EmbarqueCajas ec WITH(UPDLOCK,HOLDLOCK)
    WHERE ec.EmbarqueID=e.EmbarqueID AND ec.Activo=1
) c
WHERE e.EmbarqueID=@EmbarqueID AND e.Activo=1;";
            string folio;
            string cliente;
            string estatus;
            string formaEnvio;
            string estatusViaje;
            DateTime? fechaAnterior;
            DateTime? fechaSalida;
            TimeSpan? horaAnterior;
            int? rutaId;
            int? unidadId;
            int? choferId;
            int? viajeId;
            int cajasCargadas;
            int cajasDespachadas;
            byte[] rowVersionActual;
            await using (var cmd = new SqlCommand(sqlActual, cn, tx))
            {
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = model.EmbarqueID;
                await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
                if (!await rd.ReadAsync(cancellationToken)) throw new InvalidOperationException("El embarque ya no existe.");
                folio = Texto(rd, "Folio");
                cliente = Texto(rd, "Cliente");
                estatus = Texto(rd, "Estatus");
                fechaAnterior = Fecha(rd, "FechaCargaProgramada");
                horaAnterior = Hora(rd, "HoraCargaProgramada");
                fechaSalida = Fecha(rd, "FechaSalida");
                formaEnvio = NormalizarFormaEnvio(Texto(rd, "FormaEnvio"));
                rutaId = EnteroNullable(rd, "RutaID");
                unidadId = EnteroNullable(rd, "UnidadID");
                choferId = EnteroNullable(rd, "ChoferUsuarioID");
                viajeId = EnteroNullable(rd, "ViajeID");
                estatusViaje = Texto(rd, "EstatusViaje");
                cajasCargadas = Entero(rd, "CajasCargadas");
                cajasDespachadas = Entero(rd, "CajasDespachadas");
                rowVersionActual = Bytes(rd, "RowVersion");
            }
            if (!rowVersionActual.SequenceEqual(rowVersionOriginal))
            {
                await tx.RollbackAsync(cancellationToken);
                return Conflict(new { ok = false, recargar = true, mensaje = "Este embarque fue modificado por otro usuario. Recarga el calendario." });
            }
            if (estatus is "Cargando" or "En ruta" or "Entregado" or "Cancelado") throw new InvalidOperationException($"El embarque ya no puede reprogramarse porque está en estatus {estatus}.");
            if (estatus is not "Programado" and not "Preparando" and not "Preparado" and not "Cargado") throw new InvalidOperationException($"El embarque se encuentra en un estado que no permite reprogramación: {estatus}.");
            if (fechaSalida.HasValue || cajasDespachadas > 0) throw new InvalidOperationException($"El embarque {folio} ya registró salida física de planta y no puede reprogramarse.");
            if (estatus != "Cargado" && cajasCargadas > 0) throw new InvalidOperationException($"El embarque {folio} tiene {cajasCargadas:N0} caja(s) físicamente cargadas pero su estatus todavía es {estatus}. Revisa la carga antes de reprogramar.");
            if (viajeId.HasValue && estatusViaje != "Programado") throw new InvalidOperationException($"El viaje asociado ya está en estatus {estatusViaje} y no puede reprogramarse.");
            if (fechaAnterior?.Date == model.FechaCargaProgramada.Date && horaAnterior == model.HoraCargaProgramada) throw new InvalidOperationException("El embarque ya se encuentra en esa fecha y hora.");
            if (formaEnvio == "Interno" && unidadId.HasValue && choferId.HasValue) await ValidarDisponibilidadProgramacionAsync(cn, tx, model.FechaCargaProgramada.Date, model.HoraCargaProgramada.Value, unidadId.Value, choferId.Value, model.EmbarqueID, viajeId, cancellationToken);
            const string sqlUpdate = @"
UPDATE dbo.Logistica_Embarques
SET FechaProgramada=@FechaCarga,FechaCargaProgramada=@FechaCarga,HoraCargaProgramada=@HoraCarga,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
OUTPUT CONVERT(varbinary(8),INSERTED.RowVersion)
WHERE EmbarqueID=@EmbarqueID AND Activo=1 AND RowVersion=@RowVersion;";
            byte[] nuevaRowVersion;
            await using (var cmd = new SqlCommand(sqlUpdate, cn, tx))
            {
                cmd.Parameters.Add("@FechaCarga", SqlDbType.Date).Value = model.FechaCargaProgramada.Date;
                cmd.Parameters.Add("@HoraCarga", SqlDbType.Time).Value = model.HoraCargaProgramada.Value;
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = model.EmbarqueID;
                cmd.Parameters.Add("@RowVersion", SqlDbType.Binary, 8).Value = rowVersionOriginal;
                var resultado = await cmd.ExecuteScalarAsync(cancellationToken);
                if (resultado == null || resultado == DBNull.Value) throw new DBConcurrencyException("El embarque cambió mientras se intentaba reprogramar.");
                nuevaRowVersion = (byte[])resultado;
            }
            int? viajeSincronizadoId = viajeId;
            var viajeCreado = false;
            if (formaEnvio == "Interno")
            {
                if (rutaId.HasValue && rutaId.Value > 0 && unidadId.HasValue && unidadId.Value > 0 && choferId.HasValue && choferId.Value > 0)
                {
                    var choferNombre = await ObtenerNombreChoferFlujoAsync(cn, tx, choferId.Value, cancellationToken);
                    viajeSincronizadoId = await AsegurarViajeEntregaNsAsync(cn, tx, model.EmbarqueID, model.FechaCargaProgramada.Date, model.HoraCargaProgramada.Value, rutaId.Value, unidadId.Value, choferId.Value, choferNombre, cancellationToken);
                    viajeCreado = !viajeId.HasValue && viajeSincronizadoId > 0;
                }
                else if (viajeId.HasValue)
                {
                    const string sqlViaje = @"
UPDATE dbo.Logistica_Viajes
SET FechaProgramada=@Fecha,HoraSalidaProgramada=@Hora,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
WHERE ViajeID=@ViajeID AND Activo=1 AND Estatus=N'Programado';
DECLARE @FilasViaje int=@@ROWCOUNT;
UPDATE dbo.Logistica_ViajeParadas
SET FechaHoraSalidaProgramada=@FechaHora,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
WHERE ViajeID=@ViajeID AND Activo=1 AND TipoParada=N'Origen' AND Estatus=N'Pendiente';
SELECT @FilasViaje;";
                    await using var cmd = new SqlCommand(sqlViaje, cn, tx);
                    cmd.Parameters.Add("@Fecha", SqlDbType.Date).Value = model.FechaCargaProgramada.Date;
                    cmd.Parameters.Add("@Hora", SqlDbType.Time).Value = model.HoraCargaProgramada.Value;
                    cmd.Parameters.Add("@FechaHora", SqlDbType.DateTime2).Value = nuevaFechaHora;
                    cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                    cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId.Value;
                    if (Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) != 1) throw new InvalidOperationException("No fue posible sincronizar la reprogramación del viaje.");
                    await InsertarHistorialViajeAsync(cn, tx, viajeId.Value, "REPROGRAMADO_DESDE_EMBARQUE", "Programado", "Programado", $"Nueva salida programada: {model.FechaCargaProgramada:dd/MM/yyyy} {model.HoraCargaProgramada.Value:hh\\:mm}.", cancellationToken);
                }
            }
            var anterior = FormatearFechaHora(fechaAnterior, horaAnterior);
            var nueva = FormatearFechaHora(model.FechaCargaProgramada, model.HoraCargaProgramada);
            var historial = $"Reprogramación desde Centro Operativo. Cliente: {cliente}. Carga: {anterior} → {nueva}.";
            if (estatus == "Cargado") historial += " El embarque conserva su carga física confirmada.";
            if (viajeSincronizadoId.HasValue) historial += viajeCreado ? $" Se creó el nuevo viaje VIA-{viajeSincronizadoId.Value:000000} porque el viaje anterior fue cancelado o ya no estaba activo." : $" Viaje VIA-{viajeSincronizadoId.Value:000000} sincronizado.";
            if (formaEnvio == "Interno" && !viajeSincronizadoId.HasValue) historial += " La fecha quedó programada, pero falta completar ruta, unidad o chofer para generar el nuevo viaje.";
            if (!string.IsNullOrWhiteSpace(model.Observaciones)) historial += $" Observaciones: {model.Observaciones}";
            await InsertarHistorialAsync(cn, tx, model.EmbarqueID, "REPROGRAMACION_CALENDARIO", estatus, estatus, historial, cancellationToken);
            await tx.CommitAsync(cancellationToken);
            var mensaje = $"{folio} reprogramado a {nueva}.";
            if (estatus == "Cargado") mensaje += " La carga física permanece intacta.";
            if (viajeCreado && viajeSincronizadoId.HasValue) mensaje += $" Se generó el nuevo viaje VIA-{viajeSincronizadoId.Value:000000}.";
            else if (formaEnvio == "Interno" && !viajeSincronizadoId.HasValue) mensaje += " Falta completar los recursos de Entrega para generar el viaje.";
            return Json(new
            {
                ok = true,
                mensaje,
                embarqueId = model.EmbarqueID,
                viajeId = viajeSincronizadoId,
                viajeCreado,
                fechaCargaProgramada = model.FechaCargaProgramada.ToString("yyyy-MM-dd"),
                horaCargaProgramada = model.HoraCargaProgramada.Value.ToString(@"hh\:mm"),
                rowVersion = Convert.ToBase64String(nuevaRowVersion)
            });
        }
        catch (DBConcurrencyException ex)
        {
            try { await tx.RollbackAsync(cancellationToken); } catch { }
            return Conflict(new { ok = false, recargar = true, mensaje = ex.Message });
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(cancellationToken); } catch { }
            return BadRequest(new { ok = false, mensaje = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(104_857_600)]
    public async Task<IActionResult> CerrarConEvidencia(int embarqueId, string? rowVersion, DateTime fechaEntrega, string? receptorNombre, string? folioRemision, string? observaciones, string? observacionesEvidencia, List<IFormFile>? evidencias, CancellationToken cancellationToken = default)
    {
        var acceso = await ValidarAccesoAsync();
        if (acceso != null) return acceso;
        receptorNombre = receptorNombre?.Trim();
        folioRemision = folioRemision?.Trim();
        observaciones = observaciones?.Trim();
        observacionesEvidencia = observacionesEvidencia?.Trim();
        evidencias = (evidencias ?? new List<IFormFile>()).Where(x => x != null && x.Length > 0).ToList();
        if (embarqueId <= 0) return BadRequest(new { ok = false, mensaje = "El embarque indicado no es válido." });
        if (string.IsNullOrWhiteSpace(receptorNombre)) return BadRequest(new { ok = false, mensaje = "Captura quién recibió la mercancía." });
        if (receptorNombre.Length > 200) return BadRequest(new { ok = false, mensaje = "El nombre de quien recibe no puede exceder 200 caracteres." });
        if (!string.IsNullOrWhiteSpace(folioRemision) && folioRemision.Length > 100) return BadRequest(new { ok = false, mensaje = "El folio de remisión no puede exceder 100 caracteres." });
        if (!string.IsNullOrWhiteSpace(observaciones) && observaciones.Length > 1200) return BadRequest(new { ok = false, mensaje = "Las observaciones no pueden exceder 1,200 caracteres." });
        if (!string.IsNullOrWhiteSpace(observacionesEvidencia) && observacionesEvidencia.Length > 1000) return BadRequest(new { ok = false, mensaje = "Las observaciones de evidencia no pueden exceder 1,000 caracteres." });
        if (fechaEntrega == default) return BadRequest(new { ok = false, mensaje = "Captura la fecha y hora de entrega." });
        if (fechaEntrega > DateTime.Now.AddMinutes(5)) return BadRequest(new { ok = false, mensaje = "La fecha de entrega no puede estar en el futuro." });
        if (evidencias.Count == 0) return BadRequest(new { ok = false, mensaje = "Adjunta al menos una evidencia de entrega antes de cerrar." });
        if (evidencias.Count > 15) return BadRequest(new { ok = false, mensaje = "Puedes adjuntar como máximo 15 evidencias por entrega." });
        if (evidencias.Sum(x => x.Length) > 100L * 1024L * 1024L) return BadRequest(new { ok = false, mensaje = "El tamaño total de las evidencias no puede exceder 100 MB." });
        foreach (var evidencia in evidencias)
        {
            var validacion = ValidarEvidencia(evidencia);
            if (!validacion.Ok) return BadRequest(new { ok = false, mensaje = validacion.Mensaje });
        }
        if (string.IsNullOrWhiteSpace(rowVersion)) return Conflict(new { ok = false, recargar = true, mensaje = "No se recibió la versión del embarque. Recarga el flujo." });
        byte[] versionOriginal;
        try { versionOriginal = Convert.FromBase64String(rowVersion); }
        catch { return Conflict(new { ok = false, recargar = true, mensaje = "La versión del embarque no es válida. Recarga el flujo." }); }
        var rutasFisicasCreadas = new List<string>();
        var evidenciasIds = new List<int>();
        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            const string sqlHeader = @"
SELECT ISNULL(Folio,N'') Folio,ISNULL(Estatus,N'') Estatus,ISNULL(FormaEnvio,N'Pendiente') FormaEnvio,FechaSalida,CONVERT(varbinary(8),RowVersion) RowVersion
FROM dbo.Logistica_Embarques WITH(UPDLOCK,HOLDLOCK)
WHERE EmbarqueID=@EmbarqueID AND Activo=1;";
            string folio;
            string estatus;
            string formaEnvio;
            DateTime? fechaSalida;
            byte[] versionActual;
            await using (var cmd = new SqlCommand(sqlHeader, cn, tx))
            {
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
                if (!await rd.ReadAsync(cancellationToken)) throw new InvalidOperationException("El embarque no existe.");
                folio = Texto(rd, "Folio");
                estatus = Texto(rd, "Estatus");
                formaEnvio = NormalizarFormaEnvio(Texto(rd, "FormaEnvio"));
                fechaSalida = Fecha(rd, "FechaSalida");
                versionActual = Bytes(rd, "RowVersion");
            }
            if (!versionActual.SequenceEqual(versionOriginal)) throw new DBConcurrencyException("El embarque fue modificado por otro usuario. Recarga el flujo.");
            if (estatus != "En ruta") throw new InvalidOperationException($"Solo un embarque En ruta puede cerrarse como Entregado. Estado actual: {estatus}.");
            if (formaEnvio == "Interno") throw new InvalidOperationException("Las entregas realizadas con unidad propia deben cerrarse desde el portal del chofer y su parada de entrega.");
            if (formaEnvio is not "Cliente" and not "Paqueteria") throw new InvalidOperationException("La forma de salida actual no permite cerrar la entrega desde Centro Operativo.");
            if (!fechaSalida.HasValue) throw new InvalidOperationException("El embarque está En ruta pero no tiene una fecha de salida registrada.");
            if (fechaEntrega < fechaSalida.Value) throw new InvalidOperationException("La fecha de entrega no puede ser anterior a la salida de planta.");
            const string sqlValidacion = @"
SELECT COUNT_BIG(*) TotalPartidas,
ISNULL(SUM(CONVERT(bigint,ISNULL(d.CantidadSolicitada,0))),0) TotalSolicitado,
ISNULL(SUM(CONVERT(bigint,ISNULL(d.CantidadDespachada,0))),0) TotalDespachado,
ISNULL(SUM(CASE WHEN ISNULL(d.CantidadDespachada,0)<=0 THEN 1 ELSE 0 END),0) PartidasSinDespacho,
ISNULL(SUM(CASE WHEN ISNULL(d.CantidadDespachada,0)>ISNULL(d.CantidadSolicitada,0) THEN 1 ELSE 0 END),0) PartidasInconsistentes
FROM dbo.Logistica_EmbarqueDetalle d WITH(UPDLOCK,HOLDLOCK)
WHERE d.EmbarqueID=@EmbarqueID AND d.Activo=1;
SELECT COUNT_BIG(*)
FROM dbo.Logistica_Incidencias i WITH(UPDLOCK,HOLDLOCK)
WHERE i.EmbarqueID=@EmbarqueID AND i.Activo=1 AND i.Estatus IN(N'Abierta',N'En seguimiento') AND i.Severidad=N'Crítica';";
            long totalPartidas;
            long totalSolicitado;
            long totalDespachado;
            long partidasSinDespacho;
            long partidasInconsistentes;
            long incidenciasCriticas;
            await using (var cmd = new SqlCommand(sqlValidacion, cn, tx))
            {
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
                if (!await rd.ReadAsync(cancellationToken)) throw new InvalidOperationException("No fue posible validar las partidas del embarque.");
                totalPartidas = EnteroLargo(rd, "TotalPartidas");
                totalSolicitado = EnteroLargo(rd, "TotalSolicitado");
                totalDespachado = EnteroLargo(rd, "TotalDespachado");
                partidasSinDespacho = EnteroLargo(rd, "PartidasSinDespacho");
                partidasInconsistentes = EnteroLargo(rd, "PartidasInconsistentes");
                incidenciasCriticas = 0;
                if (await rd.NextResultAsync(cancellationToken) && await rd.ReadAsync(cancellationToken)) incidenciasCriticas = Convert.ToInt64(rd.GetValue(0));
            }
            if (totalPartidas <= 0) throw new InvalidOperationException("El embarque no contiene partidas.");
            if (totalSolicitado <= 0) throw new InvalidOperationException("El embarque no contiene una cantidad solicitada válida.");
            if (totalDespachado <= 0) throw new InvalidOperationException("El embarque no contiene una cantidad despachada válida.");
            if (partidasSinDespacho > 0) throw new InvalidOperationException("Existen partidas sin despachar.");
            if (partidasInconsistentes > 0) throw new InvalidOperationException("Existen cantidades despachadas superiores a las solicitadas.");
            if (totalDespachado != totalSolicitado) throw new InvalidOperationException($"La entrega no puede cerrarse porque la cantidad despachada no coincide con la solicitada. Solicitado: {totalSolicitado:N0} PZA. Despachado: {totalDespachado:N0} PZA.");
            if (incidenciasCriticas > 0) throw new InvalidOperationException("Existen incidencias críticas abiertas. No se puede cerrar la entrega.");
            await DesvincularViajePorCambioFormaEnvioAsync(cn, tx, embarqueId, formaEnvio, $"Limpieza preventiva de relación de viaje antes del cierre externo de {folio}.", cancellationToken);
            var carpetaRelativa = Path.Combine("Logistica", "Evidencias", embarqueId.ToString());
            var carpetaFisica = Path.Combine(_environment.ContentRootPath, "App_Data", carpetaRelativa);
            Directory.CreateDirectory(carpetaFisica);
            const string sqlEvidencia = @"
INSERT dbo.Logistica_EmbarqueEvidencias
(EmbarqueID,TipoEvidencia,NombreOriginal,NombreFisico,RutaRelativa,TipoContenido,TamanoBytes,Observaciones,UsuarioID,UsuarioNombre,FechaCarga,Activo)
VALUES
(@EmbarqueID,N'Entrega',@NombreOriginal,@NombreFisico,@RutaRelativa,@TipoContenido,@TamanoBytes,@Observaciones,@UsuarioID,@UsuarioNombre,SYSDATETIME(),1);
SELECT CONVERT(int,SCOPE_IDENTITY());";
            foreach (var evidencia in evidencias)
            {
                var nombreOriginal = Path.GetFileName(evidencia.FileName);
                var extension = Path.GetExtension(nombreOriginal).ToLowerInvariant();
                var nombreFisico = $"{Guid.NewGuid():N}{extension}";
                var tipoContenido = string.IsNullOrWhiteSpace(evidencia.ContentType) ? "application/octet-stream" : evidencia.ContentType.Trim();
                var rutaFisica = Path.Combine(carpetaFisica, nombreFisico);
                await using (var stream = new FileStream(rutaFisica, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                {
                    await evidencia.CopyToAsync(stream, cancellationToken);
                }
                rutasFisicasCreadas.Add(rutaFisica);
                var rutaRelativa = Path.Combine("App_Data", carpetaRelativa, nombreFisico).Replace('\\', '/');
                await using var cmd = new SqlCommand(sqlEvidencia, cn, tx);
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                cmd.Parameters.Add("@NombreOriginal", SqlDbType.NVarChar, 260).Value = nombreOriginal;
                cmd.Parameters.Add("@NombreFisico", SqlDbType.NVarChar, 260).Value = nombreFisico;
                cmd.Parameters.Add("@RutaRelativa", SqlDbType.NVarChar, 600).Value = rutaRelativa;
                cmd.Parameters.Add("@TipoContenido", SqlDbType.NVarChar, 150).Value = tipoContenido;
                cmd.Parameters.Add("@TamanoBytes", SqlDbType.BigInt).Value = evidencia.Length;
                cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1000).Value = Db(observacionesEvidencia);
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = Db(UsuarioID);
                cmd.Parameters.Add("@UsuarioNombre", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                var evidenciaId = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
                if (evidenciaId <= 0) throw new InvalidOperationException($"No fue posible registrar la evidencia {nombreOriginal}.");
                evidenciasIds.Add(evidenciaId);
            }
            const string sqlEntrega = @"
UPDATE dbo.Logistica_EmbarqueDetalle
SET CantidadEntregada=ISNULL(CantidadDespachada,0),FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
WHERE EmbarqueID=@EmbarqueID AND Activo=1;
UPDATE dbo.Logistica_Embarques
SET Estatus=N'Entregado',
FechaEntrega=@FechaEntrega,
EntregaPorUsuarioID=@UsuarioID,
ReceptorNombre=@Receptor,
FolioRemision=@Remision,
Observaciones=CASE WHEN @Observaciones IS NULL THEN Observaciones WHEN NULLIF(LTRIM(RTRIM(ISNULL(Observaciones,N''))),N'') IS NULL THEN @Observaciones ELSE CONCAT(Observaciones,NCHAR(13),NCHAR(10),@Observaciones) END,
FechaModificacion=SYSDATETIME(),
ActualizadoPor=@Usuario
OUTPUT CONVERT(varbinary(8),INSERTED.RowVersion)
WHERE EmbarqueID=@EmbarqueID AND Activo=1 AND Estatus=N'En ruta' AND RowVersion=@RowVersion;";
            byte[] nuevaVersion;
            await using (var cmd = new SqlCommand(sqlEntrega, cn, tx))
            {
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                cmd.Parameters.Add("@FechaEntrega", SqlDbType.DateTime2).Value = fechaEntrega;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = Db(UsuarioID);
                cmd.Parameters.Add("@Receptor", SqlDbType.NVarChar, 200).Value = receptorNombre;
                cmd.Parameters.Add("@Remision", SqlDbType.NVarChar, 100).Value = Db(string.IsNullOrWhiteSpace(folioRemision) ? null : folioRemision);
                cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1200).Value = Db(string.IsNullOrWhiteSpace(observaciones) ? null : observaciones);
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@RowVersion", SqlDbType.Timestamp).Value = versionOriginal;
                var resultado = await cmd.ExecuteScalarAsync(cancellationToken);
                if (resultado == null || resultado == DBNull.Value) throw new DBConcurrencyException("El embarque cambió mientras se confirmaba la entrega. Recarga el flujo.");
                nuevaVersion = (byte[])resultado;
            }
            await InsertarHistorialAsync(cn, tx, embarqueId, "EVIDENCIAS_ENTREGA", "En ruta", "En ruta", $"{evidenciasIds.Count:N0} evidencia(s) de entrega registradas desde Centro Operativo.", cancellationToken);
            var formaTexto = formaEnvio == "Cliente" ? "Cliente recoge" : "Paquetería";
            var descripcion = $"Entrega confirmada desde Centro Operativo. Forma de salida: {formaTexto}. Receptor: {receptorNombre}. Fecha: {fechaEntrega:dd/MM/yyyy HH:mm}. Solicitado: {totalSolicitado:N0} PZA. Entregado: {totalDespachado:N0} PZA. Evidencias: {evidenciasIds.Count:N0}.";
            if (!string.IsNullOrWhiteSpace(folioRemision)) descripcion += $" Remisión: {folioRemision}.";
            if (!string.IsNullOrWhiteSpace(observaciones)) descripcion += $" Observaciones: {observaciones}";
            await InsertarHistorialAsync(cn, tx, embarqueId, "ENTREGA_CONFIRMADA_CENTRO_OPERATIVO", "En ruta", "Entregado", descripcion, cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return Json(new { ok = true, mensaje = $"{folio} entregado correctamente.", embarqueId, estatus = "Entregado", fechaEntrega = fechaEntrega.ToString("yyyy-MM-ddTHH:mm:ss"), receptor = receptorNombre, folioRemision, evidencias = evidenciasIds.Count, evidenciasIds, rowVersion = Convert.ToBase64String(nuevaVersion) });
        }
        catch (DBConcurrencyException ex)
        {
            try { await tx.RollbackAsync(cancellationToken); } catch { }
            foreach (var ruta in rutasFisicasCreadas) EliminarArchivoSiExiste(ruta);
            return Conflict(new { ok = false, recargar = true, mensaje = ex.Message });
        }
        catch (SqlException ex)
        {
            try { await tx.RollbackAsync(cancellationToken); } catch { }
            foreach (var ruta in rutasFisicasCreadas) EliminarArchivoSiExiste(ruta);
            return BadRequest(new { ok = false, tipo = "SQL", numero = ex.Number, mensaje = ex.Message });
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(cancellationToken); } catch { }
            foreach (var ruta in rutasFisicasCreadas) EliminarArchivoSiExiste(ruta);
            return BadRequest(new { ok = false, mensaje = ex.Message });
        }
    }

    private async Task<LogisticaOperacionIndexVm> CargarOperacionAsync(SqlConnection cn, string vista, DateTime fechaReferencia, int anio, int semana, DateTime fechaInicio, DateTime fechaFin, DateTime fechaAnterior, DateTime fechaSiguiente, string? q, int? clienteId, string? estatus, string? formaEnvio, bool soloExpeditados, bool soloIncidencias, CancellationToken cancellationToken)
    {
        var vm = new LogisticaOperacionIndexVm
        {
            Vista = vista,
            FechaReferencia = fechaReferencia.Date,
            Anio = anio,
            NumeroSemana = semana,
            FechaInicio = fechaInicio.Date,
            FechaFin = fechaFin.Date,
            FechaAnterior = fechaAnterior.Date,
            FechaSiguiente = fechaSiguiente.Date,
            Busqueda = q,
            ClienteID = clienteId,
            Estatus = estatus,
            FormaEnvio = formaEnvio,
            SoloExpeditados = soloExpeditados,
            SoloIncidencias = soloIncidencias
        };
        var eventos = new List<LogisticaOperacionEventoVm>();
        var mostrarRequerimientos = string.IsNullOrWhiteSpace(formaEnvio) && (string.IsNullOrWhiteSpace(estatus) || estatus.Equals("Requerido", StringComparison.OrdinalIgnoreCase) || estatus.Equals("Sin programar", StringComparison.OrdinalIgnoreCase));
        var mostrarProgramaciones = string.IsNullOrWhiteSpace(formaEnvio) && (string.IsNullOrWhiteSpace(estatus) || estatus.Equals("Por generar", StringComparison.OrdinalIgnoreCase) || estatus.Equals("Programacion", StringComparison.OrdinalIgnoreCase) || estatus.Equals("Programación", StringComparison.OrdinalIgnoreCase));
        if (mostrarRequerimientos) await CargarDemandaReleaseAsync(cn, eventos, fechaInicio, fechaFin, q, clienteId, cancellationToken);
        if (mostrarProgramaciones) await CargarProgramacionesPendientesAsync(cn, eventos, fechaInicio, fechaFin, q, clienteId, cancellationToken);
        if (string.IsNullOrWhiteSpace(estatus) || !estatus.Equals("Requerido", StringComparison.OrdinalIgnoreCase) && !estatus.Equals("Sin programar", StringComparison.OrdinalIgnoreCase) && !estatus.Equals("Por generar", StringComparison.OrdinalIgnoreCase) && !estatus.Equals("Programacion", StringComparison.OrdinalIgnoreCase) && !estatus.Equals("Programación", StringComparison.OrdinalIgnoreCase))
            await CargarEmbarquesAsync(cn, eventos, fechaInicio, fechaFin, q, clienteId, estatus, formaEnvio, cancellationToken);
        if (soloExpeditados) eventos = eventos.Where(x => x.EsExpeditado).ToList();
        if (soloIncidencias) eventos = eventos.Where(x => x.TieneIncidencia).ToList();
        foreach (var grupo in eventos.GroupBy(x => new { x.ClienteID, x.Cliente }).OrderBy(x => x.Key.Cliente))
        {
            vm.Clientes.Add(new LogisticaOperacionClienteVm
            {
                ClienteID = grupo.Key.ClienteID,
                Cliente = string.IsNullOrWhiteSpace(grupo.Key.Cliente) ? $"Cliente {grupo.Key.ClienteID}" : grupo.Key.Cliente,
                Eventos = grupo.OrderBy(x => x.FechaVisual).ThenBy(x => x.HoraProgramada ?? TimeSpan.Zero).ThenBy(x => x.TipoOrden).ThenBy(x => x.EventoID).ToList()
            });
        }
        vm.ClientesFiltro = await CargarClientesFiltroAsync(cn, cancellationToken);
        vm.Unidades = await CargarUnidadesOperacionAsync(cn, fechaInicio, fechaFin, cancellationToken);
        return vm;
    }

    private async Task<List<LogisticaOperacionUnidadVm>> CargarUnidadesOperacionAsync(SqlConnection cn, DateTime fechaInicio, DateTime fechaFin, CancellationToken cancellationToken)
    {
        var unidades = new Dictionary<int, LogisticaOperacionUnidadVm>();
        var eventosViaje = new Dictionary<int, LogisticaOperacionUnidadEventoVm>();
        const string sql = @"
SELECT u.UnidadID,ISNULL(u.NumeroEconomico,N'') NumeroEconomico,ISNULL(u.Placas,N'') Placas,ISNULL(u.Marca,N'') Marca,ISNULL(u.Modelo,N'') Modelo,u.CapacidadPiezas,u.CapacidadPesoKg,ISNULL(u.Activo,0) Activo
FROM dbo.Logistica_Unidades u
WHERE u.Activo=1
ORDER BY u.NumeroEconomico,u.UnidadID;
SELECT v.ViajeID,ISNULL(v.Folio,N'') Folio,ISNULL(v.TipoViaje,N'') TipoViaje,ISNULL(v.TipoTransporte,N'') TipoTransporte,v.UnidadID,
ISNULL(u.NumeroEconomico,N'') NumeroEconomico,v.OperadorUsuarioID,COALESCE(NULLIF(LTRIM(RTRIM(v.OperadorNombreSnapshot)),N''),NULLIF(LTRIM(RTRIM(v.OperadorTexto)),N''),N'') Operador,
v.RutaID,ISNULL(CASE WHEN r.RutaID IS NULL THEN N'' ELSE CONCAT(ISNULL(r.Codigo,N''),CASE WHEN NULLIF(LTRIM(RTRIM(ISNULL(r.Nombre,N''))),N'') IS NULL THEN N'' ELSE N' - '+r.Nombre END) END,N'') Ruta,
ISNULL(v.Origen,N'') Origen,ISNULL(v.Destino,N'') Destino,ISNULL(v.Motivo,N'') Motivo,v.FechaProgramada,v.HoraSalidaProgramada,v.FechaSalidaReal,v.FechaRegresoReal,
v.KilometrajeSalida,v.KilometrajeRegreso,ISNULL(v.Estatus,N'') Estatus,ISNULL(v.TieneIncidencia,0) TieneIncidencia,
ISNULL(i.IncidenciasAbiertas,0) IncidenciasAbiertas,ISNULL(i.IncidenciasCriticas,0) IncidenciasCriticas,
ISNULL(ev.EvidenciasSalida,0) EvidenciasSalida,ISNULL(ev.EvidenciasRegreso,0) EvidenciasRegreso,ISNULL(ev.EvidenciasTrayecto,0) EvidenciasTrayecto,ISNULL(ev.EvidenciasIncidencia,0) EvidenciasIncidencia,
CONVERT(varbinary(8),v.RowVersion) RowVersion
FROM dbo.Logistica_Viajes v
INNER JOIN dbo.Logistica_Unidades u ON u.UnidadID=v.UnidadID AND u.Activo=1
LEFT JOIN dbo.Logistica_Rutas r ON r.RutaID=v.RutaID
OUTER APPLY
(
    SELECT CONVERT(int,COUNT_BIG(*)) IncidenciasAbiertas,ISNULL(SUM(CASE WHEN x.Severidad=N'Crítica' THEN 1 ELSE 0 END),0) IncidenciasCriticas
    FROM dbo.Logistica_ViajeIncidencias x
    WHERE x.ViajeID=v.ViajeID AND x.Activo=1 AND x.Estatus IN(N'Abierta',N'En seguimiento')
) i
OUTER APPLY
(
    SELECT
    ISNULL(SUM(CASE WHEN x.TipoEvidencia=N'Salida' THEN 1 ELSE 0 END),0) EvidenciasSalida,
    ISNULL(SUM(CASE WHEN x.TipoEvidencia=N'Regreso' THEN 1 ELSE 0 END),0) EvidenciasRegreso,
    ISNULL(SUM(CASE WHEN x.TipoEvidencia IN(N'Ruta',N'Trayecto') THEN 1 ELSE 0 END),0) EvidenciasTrayecto,
    ISNULL(SUM(CASE WHEN x.TipoEvidencia=N'Incidencia' THEN 1 ELSE 0 END),0) EvidenciasIncidencia
    FROM dbo.Logistica_ViajeEvidencias x
    WHERE x.ViajeID=v.ViajeID AND x.Activo=1
) ev
WHERE v.Activo=1 AND v.UnidadID IS NOT NULL
AND
(
    (v.FechaProgramada<@Hasta AND ISNULL(v.Estatus,N'')=N'Programado')
    OR
    (v.FechaSalidaReal<@Hasta AND (v.FechaRegresoReal IS NULL OR v.FechaRegresoReal>=@Desde))
    OR
    (v.FechaProgramada>=@Desde AND v.FechaProgramada<@Hasta)
)
ORDER BY u.NumeroEconomico,v.FechaProgramada,v.HoraSalidaProgramada,v.ViajeID;
SELECT ve.ViajeEmbarqueID,ve.ViajeID,ve.EmbarqueID,ve.OrdenEntrega,ISNULL(e.Folio,N'') Folio,e.ClienteID,
ISNULL(e.ClienteNombreSnapshot,N'') Cliente,ISNULL(e.Destino,N'') Destino,ISNULL(e.Estatus,N'') Estatus,e.FechaCargaProgramada,e.HoraCargaProgramada,e.FechaEntregaProgramada,
ISNULL(d.TotalPartidas,0) TotalPartidas,ISNULL(d.TotalPiezas,0) TotalPiezas
FROM dbo.Logistica_ViajeEmbarques ve
INNER JOIN dbo.Logistica_Viajes v ON v.ViajeID=ve.ViajeID AND v.Activo=1
INNER JOIN dbo.Logistica_Embarques e ON e.EmbarqueID=ve.EmbarqueID AND e.Activo=1
OUTER APPLY
(
    SELECT CONVERT(int,COUNT_BIG(*)) TotalPartidas,ISNULL(SUM(x.CantidadSolicitada),0) TotalPiezas
    FROM dbo.Logistica_EmbarqueDetalle x
    WHERE x.EmbarqueID=e.EmbarqueID AND x.Activo=1
) d
WHERE ve.Activo=1 AND
(
    (v.FechaProgramada<@Hasta AND ISNULL(v.Estatus,N'')=N'Programado')
    OR
    (v.FechaSalidaReal<@Hasta AND (v.FechaRegresoReal IS NULL OR v.FechaRegresoReal>=@Desde))
    OR
    (v.FechaProgramada>=@Desde AND v.FechaProgramada<@Hasta)
)
ORDER BY ve.ViajeID,ISNULL(ve.OrdenEntrega,2147483647),ve.ViajeEmbarqueID;";
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@Desde", SqlDbType.DateTime2).Value = fechaInicio.Date;
        cmd.Parameters.Add("@Hasta", SqlDbType.DateTime2).Value = fechaFin.Date.AddDays(1);
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await rd.ReadAsync(cancellationToken))
        {
            var unidadId = Entero(rd, "UnidadID");
            unidades[unidadId] = new LogisticaOperacionUnidadVm
            {
                UnidadID = unidadId,
                NumeroEconomico = Texto(rd, "NumeroEconomico"),
                Placas = Texto(rd, "Placas"),
                Marca = Texto(rd, "Marca"),
                Modelo = Texto(rd, "Modelo"),
                CapacidadPiezas = EnteroNullable(rd, "CapacidadPiezas"),
                CapacidadPesoKg = DecimalNullable(rd, "CapacidadPesoKg"),
                Activo = Booleano(rd, "Activo")
            };
        }
        if (await rd.NextResultAsync(cancellationToken))
        {
            while (await rd.ReadAsync(cancellationToken))
            {
                var unidadId = Entero(rd, "UnidadID");
                if (!unidades.TryGetValue(unidadId, out var unidad)) continue;
                var viajeId = Entero(rd, "ViajeID");
                var evento = new LogisticaOperacionUnidadEventoVm
                {
                    ViajeID = viajeId,
                    Folio = Texto(rd, "Folio"),
                    TipoViaje = Texto(rd, "TipoViaje"),
                    TipoTransporte = Texto(rd, "TipoTransporte"),
                    UnidadID = unidadId,
                    NumeroEconomico = Texto(rd, "NumeroEconomico"),
                    OperadorUsuarioID = EnteroNullable(rd, "OperadorUsuarioID"),
                    Operador = Texto(rd, "Operador"),
                    RutaID = EnteroNullable(rd, "RutaID"),
                    Ruta = Texto(rd, "Ruta"),
                    Origen = Texto(rd, "Origen"),
                    Destino = Texto(rd, "Destino"),
                    Motivo = Texto(rd, "Motivo"),
                    FechaProgramada = (Fecha(rd, "FechaProgramada") ?? DateTime.Today).Date,
                    HoraSalidaProgramada = Hora(rd, "HoraSalidaProgramada"),
                    FechaSalidaReal = Fecha(rd, "FechaSalidaReal"),
                    FechaRegresoReal = Fecha(rd, "FechaRegresoReal"),
                    KilometrajeSalida = EnteroNullable(rd, "KilometrajeSalida"),
                    KilometrajeRegreso = EnteroNullable(rd, "KilometrajeRegreso"),
                    Estatus = Texto(rd, "Estatus"),
                    TieneIncidencia = Booleano(rd, "TieneIncidencia"),
                    IncidenciasAbiertas = Entero(rd, "IncidenciasAbiertas"),
                    IncidenciasCriticas = Entero(rd, "IncidenciasCriticas"),
                    EvidenciasSalida = Entero(rd, "EvidenciasSalida"),
                    EvidenciasRegreso = Entero(rd, "EvidenciasRegreso"),
                    EvidenciasTrayecto = Entero(rd, "EvidenciasTrayecto"),
                    EvidenciasIncidencia = Entero(rd, "EvidenciasIncidencia"),
                    RowVersion = Convert.ToBase64String(Bytes(rd, "RowVersion"))
                };
                unidad.Eventos.Add(evento);
                eventosViaje[viajeId] = evento;
            }
        }
        if (await rd.NextResultAsync(cancellationToken))
        {
            while (await rd.ReadAsync(cancellationToken))
            {
                var viajeId = Entero(rd, "ViajeID");
                if (!eventosViaje.TryGetValue(viajeId, out var viaje)) continue;
                viaje.Embarques.Add(new LogisticaOperacionViajeEmbarqueVm
                {
                    ViajeEmbarqueID = Entero(rd, "ViajeEmbarqueID"),
                    ViajeID = viajeId,
                    EmbarqueID = Entero(rd, "EmbarqueID"),
                    OrdenEntrega = EnteroNullable(rd, "OrdenEntrega"),
                    Folio = Texto(rd, "Folio"),
                    ClienteID = Entero(rd, "ClienteID"),
                    Cliente = Texto(rd, "Cliente"),
                    Destino = Texto(rd, "Destino"),
                    Estatus = Texto(rd, "Estatus"),
                    TotalPartidas = Entero(rd, "TotalPartidas"),
                    TotalPiezas = Entero(rd, "TotalPiezas"),
                    FechaCargaProgramada = Fecha(rd, "FechaCargaProgramada"),
                    HoraCargaProgramada = Hora(rd, "HoraCargaProgramada"),
                    FechaEntregaProgramada = Fecha(rd, "FechaEntregaProgramada")
                });
            }
        }
        return unidades.Values.OrderBy(x => x.NumeroEconomico).ThenBy(x => x.UnidadID).ToList();
    }
    private async Task CargarDemandaReleaseAsync(SqlConnection cn, List<LogisticaOperacionEventoVm> eventos, DateTime fechaInicio, DateTime fechaFin, string? q, int? clienteId, CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT d.ReleaseDetalleID,d.ClienteID,ISNULL(d.Cliente,N'') Cliente,d.ParteID,ISNULL(d.NumeroParte,N'') NumeroParte,ISNULL(d.Descripcion,N'') Descripcion,
ISNULL(d.FolioRelease,N'') FolioRelease,ISNULL(d.NumeroOF,N'') NumeroOF,d.FechaRequerida,ISNULL(d.CantidadRequerida,0) CantidadRequerida,
ISNULL(d.CantidadProgramadaLogistica,0) CantidadProgramadaLogistica,ISNULL(d.PendienteProgramar,0) PendienteProgramar,ISNULL(p.ProgramadoActivo,0) ProgramadoActivo
FROM dbo.vw_Logistica_DemandaRelease d
OUTER APPLY
(
    SELECT ISNULL(SUM(x.CantidadProgramada),0) ProgramadoActivo
    FROM dbo.Logistica_ListaCargaProgramacion x
    WHERE x.ReleaseDetalleID=d.ReleaseDetalleID AND x.Activo=1 AND ISNULL(x.Estatus,N'Programada')<>N'Cancelada'
) p
WHERE d.ClienteID IS NOT NULL AND d.ParteID IS NOT NULL AND d.FechaRequerida<=@FechaFin
AND (@ClienteID IS NULL OR d.ClienteID=@ClienteID)
AND (@Q IS NULL OR d.Cliente LIKE N'%'+@Q+N'%' OR d.NumeroParte LIKE N'%'+@Q+N'%' OR d.Descripcion LIKE N'%'+@Q+N'%' OR d.NumeroOF LIKE N'%'+@Q+N'%' OR d.FolioRelease LIKE N'%'+@Q+N'%')
ORDER BY d.FechaRequerida,d.Cliente,d.NumeroParte,d.ReleaseDetalleID;";
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@FechaFin", SqlDbType.Date).Value = fechaFin.Date;
        cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = Db(clienteId);
        cmd.Parameters.Add("@Q", SqlDbType.NVarChar, 250).Value = Db(q);
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await rd.ReadAsync(cancellationToken))
        {
            var fechaRequerida = (Fecha(rd, "FechaRequerida") ?? DateTime.MinValue).Date;
            if (fechaRequerida == DateTime.MinValue.Date) continue;
            var cantidadRequerida = Entero(rd, "CantidadRequerida");
            var pendienteVista = Entero(rd, "PendienteProgramar");
            var programadoActivo = Entero(rd, "ProgramadoActivo");
            var pendientePorProgramacion = Math.Max(0, cantidadRequerida - programadoActivo);
            var pendiente = Math.Min(Math.Max(0, pendienteVista), pendientePorProgramacion);
            if (pendiente <= 0) continue;
            var fechaVisual = ResolverFechaVisualPendiente(fechaRequerida, fechaInicio, fechaFin);
            var releaseDetalleId = Entero(rd, "ReleaseDetalleID");
            eventos.Add(new LogisticaOperacionEventoVm
            {
                TipoEvento = "Requerimiento",
                EventoID = releaseDetalleId,
                ReleaseDetalleID = releaseDetalleId,
                ParteID = Entero(rd, "ParteID"),
                ClienteID = Entero(rd, "ClienteID"),
                Cliente = Texto(rd, "Cliente"),
                Folio = Texto(rd, "FolioRelease"),
                FolioRelease = Texto(rd, "FolioRelease"),
                NumeroOF = Texto(rd, "NumeroOF"),
                NumeroParte = Texto(rd, "NumeroParte"),
                DescripcionParte = Texto(rd, "Descripcion"),
                FechaProgramada = fechaVisual,
                FechaVisual = fechaVisual,
                FechaRequeridaOriginal = fechaRequerida,
                FechaEntregaRequerida = fechaRequerida,
                Estatus = "Sin programar",
                Criticidad = fechaRequerida < DateTime.Today ? "Expeditado" : "Requerido",
                CantidadRequerida = cantidadRequerida,
                CantidadProgramada = Math.Max(Entero(rd, "CantidadProgramadaLogistica"), programadoActivo),
                CantidadPendienteProgramar = pendiente,
                CantidadGenerada = 0,
                CantidadEnviada = 0,
                TotalPartidas = 1,
                TotalPiezas = pendiente,
                NumeroParteResumen = Texto(rd, "NumeroParte"),
                DescripcionResumen = Texto(rd, "Descripcion")
            });
        }
    }
    [HttpGet]
    public async Task<IActionResult> ObtenerFlujo(int embarqueId, CancellationToken cancellationToken = default)
    {
        var acceso = await ValidarAccesoAsync();
        if (acceso != null) return acceso;
        if (embarqueId <= 0) return BadRequest(new { ok = false, mensaje = "El embarque indicado no es válido." });
        try
        {
            await using var cn = await AbrirAsync(cancellationToken);
            var vm = await CargarFlujoAsync(cn, embarqueId, cancellationToken);
            if (vm == null) return NotFound(new { ok = false, mensaje = "El embarque no existe o ya no está activo." });
            var pt = await ObtenerEstadoPtFlujoAsync(cn, embarqueId, cancellationToken);
            var partesPt = await ObtenerDisponibilidadPtPorParteAsync(cn, embarqueId, cancellationToken);
            var requeridoPt = partesPt.Sum(x => x.Requerido);
            var disponibleAplicablePt = partesPt.Sum(x => Math.Min(x.Disponible, x.Requerido));
            var faltantePt = partesPt.Sum(x => x.Faltante);
            var disponibilidadPtCompleta = partesPt.Count > 0 && partesPt.All(x => x.Suficiente);
            var coberturaPt = requeridoPt <= 0 ? 0 : Math.Min(100, Math.Round(disponibleAplicablePt * 100d / requeridoPt, 1));
            string? viajeFolio = null;
            string? viajeEstatus = null;
            string? viajeRowVersion = null;
            if (vm.ViajeID.HasValue && vm.ViajeID.Value > 0)
            {
                const string sqlViaje = @"
SELECT ISNULL(Folio,N'') Folio,ISNULL(Estatus,N'') Estatus,CONVERT(varbinary(8),RowVersion) RowVersion
FROM dbo.Logistica_Viajes
WHERE ViajeID=@ViajeID AND Activo=1;";
                await using var cmd = new SqlCommand(sqlViaje, cn);
                cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = vm.ViajeID.Value;
                await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
                if (await rd.ReadAsync(cancellationToken))
                {
                    viajeFolio = Texto(rd, "Folio");
                    viajeEstatus = Texto(rd, "Estatus");
                    viajeRowVersion = Convert.ToBase64String(Bytes(rd, "RowVersion"));
                }
            }
            var puedeReprogramar = vm.Estatus == "Cargado" || ((vm.Estatus is "Programado" or "Preparando" or "Preparado") && vm.TotalCajasCargadas == 0);
            var puedeCancelarEmbarque = (vm.Estatus is "Programado" or "Preparando" or "Preparado") && vm.TotalCajasCargadas == 0;
            var puedeCancelarViaje = vm.ViajeID.HasValue && viajeEstatus == "Programado" && vm.Estatus is "Programado" or "Preparando" or "Preparado" or "Cargado";
            return Json(new
            {
                ok = true,
                flujo = new
                {
                    vm.EmbarqueID,
                    vm.Folio,
                    vm.ClienteID,
                    vm.Cliente,
                    vm.Destino,
                    vm.Estatus,
                    vm.ViajeID,
                    viajeFolio,
                    viajeEstatus,
                    viajeRowVersion,
                    fechaCargaProgramada = vm.FechaCargaProgramada?.ToString("yyyy-MM-dd"),
                    horaCargaProgramada = vm.HoraCargaProgramada?.ToString(@"hh\:mm"),
                    fechaEntregaProgramada = vm.FechaEntregaProgramada?.ToString("yyyy-MM-dd"),
                    vm.TotalPiezas,
                    vm.TotalPiezasPreparadas,
                    vm.TotalPiezasDespachadas,
                    vm.TotalCajas,
                    vm.TotalCajasCargadas,
                    vm.DocumentosFaltantes,
                    vm.TotalEvidencias,
                    vm.IncidenciasAbiertas,
                    vm.IncidenciasCriticas,
                    vm.PasoActual,
                    vm.RowVersion,
                    vm.Cerrado,
                    puedeReprogramar,
                    puedeCancelarViaje,
                    puedeCancelarEmbarque,
                    disponibilidadPT = new
                    {
                        piezasProducidas = pt.PiezasProducidas,
                        piezasPTLibres = pt.PiezasPTLibres,
                        piezasPTReservadas = pt.PiezasPTReservadas,
                        piezasPTUtilizables = disponibleAplicablePt,
                        cajasPTLibres = pt.CajasPTLibres,
                        cajasPTReservadas = pt.CajasPTReservadas,
                        requerido = requeridoPt,
                        faltante = faltantePt,
                        suficiente = disponibilidadPtCompleta,
                        cobertura = coberturaPt,
                        partes = partesPt.Select(x => new
                        {
                            parteId = x.ParteID,
                            numeroParte = x.NumeroParte,
                            requerido = x.Requerido,
                            libre = x.Libre,
                            reservado = x.Reservado,
                            disponible = x.Disponible,
                            faltante = x.Faltante,
                            suficiente = x.Suficiente
                        }).ToList()
                    },
                    salida = vm.Salida,
                    pasos = vm.Pasos.Select(x => new { x.Numero, x.Clave, x.Titulo, x.Descripcion, x.Icono, x.Completo, x.Disponible, x.Actual, x.EstadoTexto }).ToList(),
                    evidencias = vm.Evidencias.Select(x => new
                    {
                        x.EvidenciaID,
                        x.ViajeID,
                        x.Origen,
                        x.TipoEvidencia,
                        x.NombreOriginal,
                        x.TipoContenido,
                        x.TamanoBytes,
                        x.TamanoTexto,
                        x.Observaciones,
                        x.EsImagen,
                        x.EsPdf,
                        x.Icono,
                        fechaCarga = x.FechaCarga.ToString("dd/MM/yyyy HH:mm"),
                        x.Usuario
                    }).ToList(),
                    rutas = vm.Rutas,
                    unidades = vm.Unidades,
                    choferes = vm.Choferes
                }
            });
        }
        catch (SqlException ex)
        {
            return StatusCode(500, new { ok = false, tipo = "SQL", mensaje = ex.Message, numero = ex.Number, embarqueId });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { ok = false, tipo = "GENERAL", mensaje = ex.Message, embarqueId });
        }
    }


    private async Task<List<(int CajaID, int ParteID, int? SolicitudProduccionID, string NumeroOF, string Etiqueta, int NumeroCaja, string Lote, DateTime FechaEntrada, int Cantidad, string Ubicacion, bool Seleccionada, string EstatusSeleccion)>> ObtenerCajasCandidatasCargaAsync(SqlConnection cn, SqlTransaction? tx, int embarqueId, int clienteId, CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT c.CajaID,c.ParteID,c.SolicitudProduccionID,ISNULL(c.NumeroOF,N'') NumeroOF,ISNULL(c.Etiqueta,N'') Etiqueta,ISNULL(c.NumeroCaja,0) NumeroCaja,
ISNULL(c.LoteEtiqueta,N'') Lote,c.FechaEntrada,ISNULL(inv.Disponible,0) InventarioDisponible,ISNULL(v.Disponible,0) DisponibleLogistica,
CONCAT(ISNULL(u.Almacen,N''),CASE WHEN NULLIF(LTRIM(RTRIM(ISNULL(u.Rack,N''))),N'') IS NULL THEN N'' ELSE N' / '+u.Rack END,
CASE WHEN NULLIF(LTRIM(RTRIM(ISNULL(u.Nivel,N''))),N'') IS NULL THEN N'' ELSE N' / '+u.Nivel END,
CASE WHEN NULLIF(LTRIM(RTRIM(ISNULL(u.Posicion,N''))),N'') IS NULL THEN N'' ELSE N' / '+u.Posicion END) Ubicacion,
ISNULL(a.AsignadoActual,0) AsignadoActual,ISNULL(a.EstatusSeleccion,N'') EstatusSeleccion
FROM dbo.AlmacenPT_Cajas c
INNER JOIN dbo.ERP_Partes p ON p.ParteID=c.ParteID AND p.ClienteID=@ClienteID
INNER JOIN dbo.vw_AlmacenPTInventarioCaja inv ON inv.CajaID=c.CajaID
LEFT JOIN dbo.vw_Logistica_CajasDisponibles v ON v.CajaID=c.CajaID
LEFT JOIN dbo.ERP_Ubicaciones u ON u.UbicacionID=c.UbicacionID
OUTER APPLY
(
    SELECT SUM(CONVERT(bigint,ec.CantidadAsignada)) AsignadoActual,
    CASE
        WHEN SUM(CASE WHEN ec.EstatusSeleccion=N'Despachada' THEN 1 ELSE 0 END)=COUNT_BIG(*) THEN N'Despachada'
        WHEN SUM(CASE WHEN ec.EstatusSeleccion=N'Cargada' THEN 1 ELSE 0 END)=COUNT_BIG(*) THEN N'Cargada'
        WHEN SUM(CASE WHEN ec.EstatusSeleccion=N'Reservada' THEN 1 ELSE 0 END)=COUNT_BIG(*) THEN N'Reservada'
        ELSE N'Mixta'
    END EstatusSeleccion
    FROM dbo.Logistica_EmbarqueCajas ec
    WHERE ec.EmbarqueID=@EmbarqueID AND ec.CajaID=c.CajaID AND ec.Activo=1 AND ec.EstatusSeleccion IN(N'Reservada',N'Cargada',N'Despachada')
) a
WHERE c.Activo=1
AND UPPER(LTRIM(RTRIM(ISNULL(c.EstadoCalidad,N''))))=N'LIBERADO'
AND ISNULL(inv.Retenido,0)=0
AND ISNULL(inv.Disponible,0)>0
AND EXISTS(SELECT 1 FROM dbo.Logistica_EmbarqueDetalle d WHERE d.EmbarqueID=@EmbarqueID AND d.Activo=1 AND d.ParteID=c.ParteID AND d.CantidadSolicitada>0)
AND NOT EXISTS(SELECT 1 FROM dbo.Logistica_EmbarqueCajas ec WHERE ec.CajaID=c.CajaID AND ec.EmbarqueID<>@EmbarqueID AND ec.Activo=1)
AND
(
    (ISNULL(a.AsignadoActual,0)>0 AND ISNULL(a.AsignadoActual,0)=ISNULL(inv.Disponible,0))
    OR
    (ISNULL(a.AsignadoActual,0)=0 AND ISNULL(v.Disponible,0)>0 AND ISNULL(v.Disponible,0)=ISNULL(inv.Disponible,0))
)
ORDER BY c.ParteID,CASE WHEN ISNULL(a.AsignadoActual,0)>0 THEN 0 ELSE 1 END,c.FechaEntrada,c.CajaID;";
        var resultado = new List<(int CajaID, int ParteID, int? SolicitudProduccionID, string NumeroOF, string Etiqueta, int NumeroCaja, string Lote, DateTime FechaEntrada, int Cantidad, string Ubicacion, bool Seleccionada, string EstatusSeleccion)>();
        await using var cmd = new SqlCommand(sql, cn);
        if (tx != null) cmd.Transaction = tx;
        cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
        cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = clienteId;
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await rd.ReadAsync(cancellationToken))
        {
            var asignado = EnteroLargo(rd, "AsignadoActual");
            resultado.Add((Entero(rd, "CajaID"), Entero(rd, "ParteID"), EnteroNullable(rd, "SolicitudProduccionID"), Texto(rd, "NumeroOF"), Texto(rd, "Etiqueta"), Entero(rd, "NumeroCaja"), Texto(rd, "Lote"), Fecha(rd, "FechaEntrada") ?? DateTime.MaxValue, Entero(rd, "InventarioDisponible"), Texto(rd, "Ubicacion"), asignado > 0, Texto(rd, "EstatusSeleccion")));
        }
        return resultado;
    }

    [HttpGet]
    public async Task<IActionResult> ObtenerCargaFisica(int embarqueId, CancellationToken cancellationToken = default)
    {
        var acceso = await ValidarAccesoCargaFisicaAsync(embarqueId, cancellationToken);
        if (acceso != null) return acceso;
        if (embarqueId <= 0) return BadRequest(new { ok = false, mensaje = "El embarque indicado no es válido." });
        try
        {
            await using var cn = await AbrirAsync(cancellationToken);
            const string sqlHeader = @"
SELECT EmbarqueID,ClienteID,ISNULL(Folio,N'') Folio,ISNULL(Estatus,N'') Estatus,CONVERT(varbinary(8),RowVersion) RowVersion
FROM dbo.Logistica_Embarques
WHERE EmbarqueID=@EmbarqueID AND Activo=1;";
            int clienteId;
            string folio, estatus;
            byte[] rowVersion;
            await using (var cmd = new SqlCommand(sqlHeader, cn))
            {
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
                if (!await rd.ReadAsync(cancellationToken)) return NotFound(new { ok = false, mensaje = "El embarque no existe." });
                clienteId = Entero(rd, "ClienteID");
                folio = Texto(rd, "Folio");
                estatus = Texto(rd, "Estatus");
                rowVersion = Bytes(rd, "RowVersion");
            }
            var resumen = await ObtenerResumenCargaFisicaAsync(cn, null, embarqueId, cancellationToken);
            var candidatas = await ObtenerCajasCandidatasCargaAsync(cn, null, embarqueId, clienteId, cancellationToken);
            var partesPt = await ObtenerDisponibilidadPtPorParteAsync(cn, embarqueId, cancellationToken);
            var puedeConfirmar = estatus == "Cargando" && resumen.TotalSolicitado > 0 && resumen.PiezasCargadas > 0 && resumen.PiezasReservadas == 0 && resumen.CajasAsignadas > 0 && resumen.CajasCargadas == resumen.CajasAsignadas;
            var cargaParcial = resumen.PiezasCargadas > 0 && resumen.PiezasCargadas < resumen.TotalSolicitado;
            var partes = partesPt.Select(parte =>
            {
                var cajasParte = candidatas.Where(x => x.ParteID == parte.ParteID).ToList();
                var seleccionadas = cajasParte.Where(x => x.Seleccionada).ToList();
                return new
                {
                    parteId = parte.ParteID,
                    numeroParte = parte.NumeroParte,
                    requerido = parte.Requerido,
                    disponible = parte.Disponible,
                    faltante = parte.Faltante,
                    suficiente = parte.Suficiente,
                    piezasSeleccionadas = seleccionadas.Sum(x => (long)x.Cantidad),
                    cajasSeleccionadas = seleccionadas.Count,
                    cajas = cajasParte.Select(x => new
                    {
                        cajaId = x.CajaID,
                        x.ParteID,
                        etiqueta = x.Etiqueta,
                        numeroCaja = x.NumeroCaja,
                        numeroOF = x.NumeroOF,
                        lote = x.Lote,
                        cantidad = x.Cantidad,
                        ubicacion = x.Ubicacion,
                        fechaEntrada = x.FechaEntrada == DateTime.MaxValue ? null : x.FechaEntrada.ToString("dd/MM/yyyy HH:mm"),
                        seleccionada = x.Seleccionada,
                        estatus = x.EstatusSeleccion
                    }).ToList()
                };
            }).ToList();
            return Json(new
            {
                ok = true,
                embarqueId,
                folio,
                estatus,
                totalPiezas = resumen.TotalSolicitado,
                piezasCargadas = resumen.PiezasCargadas,
                piezasReservadas = resumen.PiezasReservadas,
                piezasPendientes = Math.Max(0, resumen.TotalSolicitado - resumen.PiezasCargadas),
                cajasAsignadas = resumen.CajasAsignadas,
                cajasCargadas = resumen.CajasCargadas,
                puedeConfirmar,
                cargaParcial,
                rowVersion = Convert.ToBase64String(rowVersion),
                partes
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { ok = false, mensaje = ex.Message });
        }
    }


    private async Task<(int CajasSeleccionadas, long PiezasSeleccionadas, List<string> Etiquetas)> AplicarSeleccionManualCajasAsync(SqlConnection cn, SqlTransaction tx, int embarqueId, int clienteId, IReadOnlyCollection<int> cajasSeleccionadas, CancellationToken cancellationToken)
    {
        var ids = cajasSeleccionadas.Where(x => x > 0).Distinct().ToList();
        if (ids.Count == 0) throw new InvalidOperationException("Selecciona al menos una caja disponible.");
        var candidatas = await ObtenerCajasCandidatasCargaAsync(cn, tx, embarqueId, clienteId, cancellationToken);
        var mapa = candidatas.ToDictionary(x => x.CajaID);
        var noDisponibles = ids.Where(x => !mapa.ContainsKey(x)).ToList();
        if (noDisponibles.Count > 0) throw new InvalidOperationException("Una o más cajas seleccionadas ya no están disponibles. Actualiza la lista de cajas.");
        const string sqlDetalles = @"
SELECT d.EmbarqueDetalleID,d.ParteID,d.SolicitudProduccionID,ISNULL(d.NumeroOFSnapshot,N'') NumeroOF,ISNULL(d.FechaEntregaReleaseSnapshot,CONVERT(date,'99991231')) FechaEntrega,ISNULL(d.CantidadSolicitada,0) CantidadSolicitada
FROM dbo.Logistica_EmbarqueDetalle d WITH(UPDLOCK,HOLDLOCK)
WHERE d.EmbarqueID=@EmbarqueID AND d.Activo=1 AND d.CantidadSolicitada>0
ORDER BY d.ParteID,ISNULL(d.FechaEntregaReleaseSnapshot,CONVERT(date,'99991231')),d.EmbarqueDetalleID;";
        var detalles = new List<(int DetalleID, int ParteID, int? SolicitudProduccionID, string NumeroOF, DateTime FechaEntrega, int Pendiente)>();
        await using (var cmd = new SqlCommand(sqlDetalles, cn, tx))
        {
            cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await rd.ReadAsync(cancellationToken)) detalles.Add((Entero(rd, "EmbarqueDetalleID"), Entero(rd, "ParteID"), EnteroNullable(rd, "SolicitudProduccionID"), Texto(rd, "NumeroOF"), Fecha(rd, "FechaEntrega") ?? DateTime.MaxValue, Entero(rd, "CantidadSolicitada")));
        }
        if (detalles.Count == 0) throw new InvalidOperationException("El embarque no contiene partidas activas.");
        foreach (var grupo in ids.Select(x => mapa[x]).GroupBy(x => x.ParteID))
        {
            var requerido = detalles.Where(x => x.ParteID == grupo.Key).Sum(x => (long)x.Pendiente);
            var seleccionado = grupo.Sum(x => (long)x.Cantidad);
            if (requerido <= 0) throw new InvalidOperationException($"La parte ID {grupo.Key} ya no tiene cantidad pendiente en el embarque.");
            if (seleccionado > requerido) throw new InvalidOperationException($"Las cajas seleccionadas para la parte ID {grupo.Key} contienen {seleccionado:N0} PZA, pero el embarque solo requiere {requerido:N0} PZA. No se permite partir una caja física.");
        }
        const string sqlLiberar = @"
UPDATE dbo.Logistica_EmbarqueCajas
SET Activo=0,EstatusSeleccion=N'Liberada',FechaLiberacion=SYSDATETIME(),UsuarioLiberacionID=@UsuarioID,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
WHERE EmbarqueID=@EmbarqueID AND Activo=1 AND EstatusSeleccion IN(N'Reservada',N'Cargada');";
        await using (var cmd = new SqlCommand(sqlLiberar, cn, tx))
        {
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = Db(UsuarioID);
            cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
            cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        const string sqlInsert = @"
INSERT dbo.Logistica_EmbarqueCajas
(EmbarqueID,EmbarqueDetalleID,CajaID,CantidadAsignada,EstatusSeleccion,FechaSeleccion,UsuarioSeleccionID,UsuarioSeleccionNombre,Activo,FechaCreacion,CreadoPor)
VALUES
(@EmbarqueID,@DetalleID,@CajaID,@Cantidad,N'Cargada',SYSDATETIME(),@UsuarioID,@UsuarioNombre,1,SYSDATETIME(),@UsuarioNombre);";
        long piezas = 0;
        var etiquetas = new List<string>();
        foreach (var caja in ids.Select(x => mapa[x]).OrderBy(x => x.ParteID).ThenBy(x => x.FechaEntrada).ThenBy(x => x.CajaID))
        {
            var restante = caja.Cantidad;
            var indices = Enumerable.Range(0, detalles.Count)
                .Where(i => detalles[i].ParteID == caja.ParteID && detalles[i].Pendiente > 0)
                .OrderBy(i =>
                {
                    var d = detalles[i];
                    if (caja.SolicitudProduccionID.HasValue && d.SolicitudProduccionID == caja.SolicitudProduccionID) return 0;
                    if (!string.IsNullOrWhiteSpace(caja.NumeroOF) && string.Equals(d.NumeroOF, caja.NumeroOF, StringComparison.OrdinalIgnoreCase)) return 1;
                    return 2;
                })
                .ThenBy(i => detalles[i].FechaEntrega)
                .ThenBy(i => detalles[i].DetalleID)
                .ToList();
            if (indices.Count == 0) throw new InvalidOperationException($"La caja {caja.Etiqueta} no corresponde a una partida pendiente del embarque.");
            foreach (var indice in indices)
            {
                if (restante <= 0) break;
                var detalle = detalles[indice];
                var tomar = Math.Min(restante, detalle.Pendiente);
                if (tomar <= 0) continue;
                await using var cmd = new SqlCommand(sqlInsert, cn, tx);
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                cmd.Parameters.Add("@DetalleID", SqlDbType.Int).Value = detalle.DetalleID;
                cmd.Parameters.Add("@CajaID", SqlDbType.Int).Value = caja.CajaID;
                cmd.Parameters.Add("@Cantidad", SqlDbType.Int).Value = tomar;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = Db(UsuarioID);
                cmd.Parameters.Add("@UsuarioNombre", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                await cmd.ExecuteNonQueryAsync(cancellationToken);
                detalles[indice] = (detalle.DetalleID, detalle.ParteID, detalle.SolicitudProduccionID, detalle.NumeroOF, detalle.FechaEntrega, detalle.Pendiente - tomar);
                restante -= tomar;
            }
            if (restante > 0) throw new InvalidOperationException($"La caja {caja.Etiqueta} no cabe completa dentro de la cantidad pendiente de su número de parte. No se permite una caja parcial.");
            piezas += caja.Cantidad;
            etiquetas.Add(string.IsNullOrWhiteSpace(caja.Etiqueta) ? $"Caja {caja.CajaID}" : caja.Etiqueta);
        }
        return (ids.Count, piezas, etiquetas);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SeleccionarCajasCarga(int embarqueId, List<int>? cajaIds, string? rowVersion, CancellationToken cancellationToken = default)
    {
        var acceso = await ValidarAccesoCargaFisicaAsync(embarqueId, cancellationToken);
        if (acceso != null) return acceso;
        cajaIds = (cajaIds ?? new List<int>()).Where(x => x > 0).Distinct().ToList();
        if (embarqueId <= 0) return BadRequest(new { ok = false, mensaje = "El embarque indicado no es válido." });
        if (cajaIds.Count == 0) return BadRequest(new { ok = false, mensaje = "Selecciona al menos una caja." });
        if (string.IsNullOrWhiteSpace(rowVersion)) return Conflict(new { ok = false, recargar = true, mensaje = "No se recibió la versión actual del embarque. Recarga la carga física." });
        byte[] versionOriginal;
        try { versionOriginal = Convert.FromBase64String(rowVersion); }
        catch { return Conflict(new { ok = false, recargar = true, mensaje = "La versión del embarque no es válida. Recarga la carga física." }); }
        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            const string sqlHeader = @"
SELECT ClienteID,ISNULL(Folio,N'') Folio,ISNULL(Estatus,N'') Estatus,CONVERT(varbinary(8),RowVersion) RowVersion
FROM dbo.Logistica_Embarques WITH(UPDLOCK,HOLDLOCK)
WHERE EmbarqueID=@EmbarqueID AND Activo=1;";
            int clienteId;
            string folio, estatus;
            byte[] versionActual;
            await using (var cmd = new SqlCommand(sqlHeader, cn, tx))
            {
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
                if (!await rd.ReadAsync(cancellationToken)) throw new InvalidOperationException("El embarque no existe.");
                clienteId = Entero(rd, "ClienteID");
                folio = Texto(rd, "Folio");
                estatus = Texto(rd, "Estatus");
                versionActual = Bytes(rd, "RowVersion");
            }
            if (!versionActual.SequenceEqual(versionOriginal)) throw new DBConcurrencyException("El embarque fue modificado por otro usuario. Actualiza la carga física.");
            if (estatus is not "Programado" and not "Preparando" and not "Preparado" and not "Cargando") throw new InvalidOperationException($"No se pueden seleccionar cajas cuando el embarque está en estatus {estatus}.");
            const string sqlIncidencias = @"SELECT COUNT_BIG(*) FROM dbo.Logistica_Incidencias WITH(UPDLOCK,HOLDLOCK) WHERE EmbarqueID=@EmbarqueID AND Activo=1 AND Estatus IN(N'Abierta',N'En seguimiento') AND Severidad=N'Crítica';";
            await using (var cmd = new SqlCommand(sqlIncidencias, cn, tx))
            {
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                if (Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken)) > 0) throw new InvalidOperationException("El embarque tiene una incidencia crítica abierta. Resuélvela antes de confirmar cajas.");
            }
            var seleccion = await AplicarSeleccionManualCajasAsync(cn, tx, embarqueId, clienteId, cajaIds, cancellationToken);
            const string sqlEstado = @"
UPDATE dbo.Logistica_Embarques
SET Estatus=N'Cargando',FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
OUTPUT CONVERT(varbinary(8),INSERTED.RowVersion)
WHERE EmbarqueID=@EmbarqueID AND Activo=1 AND Estatus IN(N'Programado',N'Preparando',N'Preparado',N'Cargando') AND RowVersion=@RowVersion;";
            byte[] nuevaVersion;
            await using (var cmd = new SqlCommand(sqlEstado, cn, tx))
            {
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                cmd.Parameters.Add("@RowVersion", SqlDbType.Timestamp).Value = versionOriginal;
                var valor = await cmd.ExecuteScalarAsync(cancellationToken);
                if (valor == null || valor == DBNull.Value) throw new DBConcurrencyException("El embarque cambió mientras se guardaba la selección de cajas.");
                nuevaVersion = (byte[])valor;
            }
            var resumen = await ObtenerResumenCargaFisicaAsync(cn, tx, embarqueId, cancellationToken);
            var etiquetas = string.Join(", ", seleccion.Etiquetas.Take(20));
            if (seleccion.Etiquetas.Count > 20) etiquetas += $" y {seleccion.Etiquetas.Count - 20:N0} más";
            await InsertarHistorialAsync(cn, tx, embarqueId, "CAJAS_SELECCIONADAS_MANUAL", estatus, "Cargando", $"Selección manual de cajas para carga física. Cajas: {seleccion.CajasSeleccionadas:N0}. Piezas seleccionadas: {seleccion.PiezasSeleccionadas:N0} de {resumen.TotalSolicitado:N0}. Cajas: {etiquetas}.", cancellationToken);
            await tx.CommitAsync(cancellationToken);
            var cargaParcial = resumen.PiezasCargadas < resumen.TotalSolicitado;
            return Json(new
            {
                ok = true,
                mensaje = cargaParcial ? $"{folio}: selección guardada con {resumen.PiezasCargadas:N0} de {resumen.TotalSolicitado:N0} PZA. Al confirmar la carga, el faltante pasará a Expeditado." : $"{folio}: selección completa guardada.",
                embarqueId,
                estatus = "Cargando",
                totalPiezas = resumen.TotalSolicitado,
                piezasCargadas = resumen.PiezasCargadas,
                piezasPendientes = Math.Max(0, resumen.TotalSolicitado - resumen.PiezasCargadas),
                cajasCargadas = resumen.CajasCargadas,
                cargaParcial,
                puedeConfirmar = resumen.PiezasCargadas > 0 && resumen.PiezasReservadas == 0 && resumen.CajasAsignadas > 0 && resumen.CajasCargadas == resumen.CajasAsignadas,
                rowVersion = Convert.ToBase64String(nuevaVersion)
            });
        }
        catch (DBConcurrencyException ex)
        {
            try { await tx.RollbackAsync(cancellationToken); } catch { }
            return Conflict(new { ok = false, recargar = true, mensaje = ex.Message });
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(cancellationToken); } catch { }
            return BadRequest(new { ok = false, mensaje = ex.Message });
        }
    }



    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EscanearCajaCarga(int embarqueId, string? codigo, CancellationToken cancellationToken = default)
    {
        var acceso = await ValidarAccesoCargaFisicaAsync(embarqueId, cancellationToken);
        if (acceso != null) return acceso;
        codigo = codigo?.Trim();
        if (embarqueId <= 0) return BadRequest(new { ok = false, mensaje = "El embarque indicado no es válido." });
        if (string.IsNullOrWhiteSpace(codigo)) return BadRequest(new { ok = false, mensaje = "Escanea una etiqueta de caja." });
        if (codigo.Length > 500) return BadRequest(new { ok = false, mensaje = "El código escaneado excede la longitud permitida." });
        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            const string sqlHeader = @"
SELECT e.EmbarqueID,e.ClienteID,ISNULL(e.Folio,N'') Folio,ISNULL(e.Estatus,N'') Estatus,e.FechaCargaProgramada,e.HoraCargaProgramada,CONVERT(varbinary(8),e.RowVersion) RowVersion
FROM dbo.Logistica_Embarques e WITH(UPDLOCK,HOLDLOCK)
WHERE e.EmbarqueID=@EmbarqueID AND e.Activo=1;";
            int clienteId;
            string folio, estatus;
            DateTime? fechaCarga;
            TimeSpan? horaCarga;
            byte[] rowVersionActual;
            await using (var cmd = new SqlCommand(sqlHeader, cn, tx))
            {
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
                if (!await rd.ReadAsync(cancellationToken)) throw new InvalidOperationException("El embarque no existe.");
                clienteId = Entero(rd, "ClienteID");
                folio = Texto(rd, "Folio");
                estatus = Texto(rd, "Estatus");
                fechaCarga = Fecha(rd, "FechaCargaProgramada");
                horaCarga = Hora(rd, "HoraCargaProgramada");
                rowVersionActual = Bytes(rd, "RowVersion");
            }
            if (estatus is not "Programado" and not "Preparando" and not "Preparado" and not "Cargando") throw new InvalidOperationException($"El embarque {folio} no admite carga física en estatus {estatus}.");
            if (!fechaCarga.HasValue || !horaCarga.HasValue) throw new InvalidOperationException("El embarque todavía no tiene fecha y hora de carga programadas.");
            const string sqlCaja = @"
SELECT TOP(1)c.CajaID,c.CajaProduccionID,c.ParteID,p.ClienteID,c.SolicitudProduccionID,ISNULL(c.NumeroOF,N'') NumeroOF,
ISNULL(c.Etiqueta,N'') Etiqueta,ISNULL(c.NumeroCaja,0) NumeroCaja,ISNULL(c.CantidadInicial,0) CantidadInicial,
ISNULL(c.LoteEtiqueta,N'') Lote,ISNULL(c.EstadoCalidad,N'') EstadoCalidad,ISNULL(inv.Disponible,0) InventarioDisponible,
ISNULL(inv.Retenido,0) Retenido,ISNULL(v.Disponible,0) DisponibleLogistica,ISNULL(p.NumeroParte,N'') NumeroParte
FROM dbo.AlmacenPT_Cajas c WITH(UPDLOCK,HOLDLOCK)
INNER JOIN dbo.ERP_Partes p ON p.ParteID=c.ParteID
INNER JOIN dbo.vw_AlmacenPTInventarioCaja inv ON inv.CajaID=c.CajaID
LEFT JOIN dbo.vw_Logistica_CajasDisponibles v ON v.CajaID=c.CajaID
LEFT JOIN dbo.Produccion_Cajas pc ON pc.CajaProduccionID=c.CajaProduccionID AND pc.Activo=1
WHERE c.Activo=1 AND
(
LTRIM(RTRIM(ISNULL(c.Etiqueta,N'')))=@Codigo
OR LTRIM(RTRIM(ISNULL(pc.FolioCaja,N'')))=@Codigo
OR LTRIM(RTRIM(ISNULL(pc.EtiquetaFolio,N'')))=@Codigo
OR LTRIM(RTRIM(ISNULL(pc.Etiqueta,N'')))=@Codigo
OR LTRIM(RTRIM(ISNULL(pc.CodigoBarrasOrigen,N'')))=@Codigo
OR c.CajaID=TRY_CONVERT(int,@Codigo)
OR c.CajaProduccionID=TRY_CONVERT(bigint,@Codigo)
)
ORDER BY CASE
WHEN LTRIM(RTRIM(ISNULL(c.Etiqueta,N'')))=@Codigo THEN 0
WHEN LTRIM(RTRIM(ISNULL(pc.FolioCaja,N'')))=@Codigo THEN 1
WHEN LTRIM(RTRIM(ISNULL(pc.EtiquetaFolio,N'')))=@Codigo THEN 2
WHEN LTRIM(RTRIM(ISNULL(pc.Etiqueta,N'')))=@Codigo THEN 3
WHEN LTRIM(RTRIM(ISNULL(pc.CodigoBarrasOrigen,N'')))=@Codigo THEN 4
WHEN c.CajaID=TRY_CONVERT(int,@Codigo) THEN 5 ELSE 6 END,c.CajaID;";
            int cajaId, parteId, cajaClienteId, numeroCaja, inventarioDisponible, disponibleLogistica;
            int? cajaSolicitudProduccionId;
            long retenido;
            string etiqueta, numeroOF, lote, estadoCalidad, numeroParte;
            await using (var cmd = new SqlCommand(sqlCaja, cn, tx))
            {
                cmd.Parameters.Add("@Codigo", SqlDbType.NVarChar, 500).Value = codigo;
                await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
                if (!await rd.ReadAsync(cancellationToken)) throw new InvalidOperationException($"No se encontró una caja PT activa para el código {codigo}.");
                cajaId = Entero(rd, "CajaID");
                parteId = Entero(rd, "ParteID");
                cajaClienteId = Entero(rd, "ClienteID");
                cajaSolicitudProduccionId = EnteroNullable(rd, "SolicitudProduccionID");
                numeroOF = Texto(rd, "NumeroOF");
                etiqueta = Texto(rd, "Etiqueta");
                numeroCaja = Entero(rd, "NumeroCaja");
                lote = Texto(rd, "Lote");
                estadoCalidad = Texto(rd, "EstadoCalidad");
                inventarioDisponible = Entero(rd, "InventarioDisponible");
                retenido = EnteroLargo(rd, "Retenido");
                disponibleLogistica = Entero(rd, "DisponibleLogistica");
                numeroParte = Texto(rd, "NumeroParte");
            }
            if (cajaClienteId != clienteId) throw new InvalidOperationException($"La caja {etiqueta} pertenece a otro cliente y no puede cargarse en {folio}.");
            if (!string.Equals(estadoCalidad, "Liberado", StringComparison.OrdinalIgnoreCase) || retenido > 0) throw new InvalidOperationException($"La caja {etiqueta} no está liberada por Calidad o se encuentra retenida.");
            if (inventarioDisponible <= 0) throw new InvalidOperationException($"La caja {etiqueta} ya no tiene existencia física disponible en PT.");
            const string sqlRelaciones = @"
SELECT ec.EmbarqueCajaID,ec.EmbarqueID,ec.EmbarqueDetalleID,ISNULL(ec.CantidadAsignada,0) CantidadAsignada,ISNULL(ec.EstatusSeleccion,N'') EstatusSeleccion
FROM dbo.Logistica_EmbarqueCajas ec WITH(UPDLOCK,HOLDLOCK)
WHERE ec.CajaID=@CajaID AND ec.Activo=1
ORDER BY ec.EmbarqueID,ec.EmbarqueDetalleID,ec.EmbarqueCajaID;";
            var relaciones = new List<(int EmbarqueCajaID, int EmbarqueID, int EmbarqueDetalleID, int Cantidad, string Estatus)>();
            await using (var cmd = new SqlCommand(sqlRelaciones, cn, tx))
            {
                cmd.Parameters.Add("@CajaID", SqlDbType.Int).Value = cajaId;
                await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await rd.ReadAsync(cancellationToken)) relaciones.Add((Entero(rd, "EmbarqueCajaID"), Entero(rd, "EmbarqueID"), Entero(rd, "EmbarqueDetalleID"), Entero(rd, "CantidadAsignada"), Texto(rd, "EstatusSeleccion")));
            }
            var relacionOtroEmbarque = relaciones.FirstOrDefault(x => x.EmbarqueID != embarqueId);
            if (relacionOtroEmbarque.EmbarqueID > 0) throw new InvalidOperationException($"La caja {etiqueta} ya está reservada o cargada en el embarque ID {relacionOtroEmbarque.EmbarqueID}.");
            var relacionesActuales = relaciones.Where(x => x.EmbarqueID == embarqueId).ToList();
            var yaEscaneada = relacionesActuales.Count > 0 && relacionesActuales.All(x => x.Estatus == "Cargada");
            var autoAsignada = false;
            long cantidadCajaAsignada = 0;
            if (yaEscaneada)
            {
                cantidadCajaAsignada = relacionesActuales.Sum(x => (long)x.Cantidad);
                var resumenYa = await ObtenerResumenCargaFisicaAsync(cn, tx, embarqueId, cancellationToken);
                await tx.CommitAsync(cancellationToken);
                return Json(new { ok = true, yaEscaneada = true, mensaje = $"La caja {etiqueta} ya había sido escaneada para este embarque.", caja = new { cajaId, etiqueta, numeroCaja, numeroParte, numeroOF, lote, cantidad = cantidadCajaAsignada }, totalPiezas = resumenYa.TotalSolicitado, piezasCargadas = resumenYa.PiezasCargadas, piezasPendientes = Math.Max(0, resumenYa.TotalSolicitado - resumenYa.PiezasCargadas), cajasCargadas = resumenYa.CajasCargadas, rowVersion = Convert.ToBase64String(rowVersionActual) });
            }
            if (relacionesActuales.Count > 0)
            {
                if (relacionesActuales.Any(x => x.Estatus is not "Reservada" and not "Cargada")) throw new InvalidOperationException($"La caja {etiqueta} tiene un estado logístico que no permite carga física.");
                cantidadCajaAsignada = relacionesActuales.Sum(x => (long)x.Cantidad);
                if (cantidadCajaAsignada != inventarioDisponible) throw new InvalidOperationException($"La caja {etiqueta} contiene {inventarioDisponible:N0} PZA físicas disponibles, pero solo {cantidadCajaAsignada:N0} PZA están reservadas para este embarque. No se permite cargar físicamente una caja parcial.");
                const string sqlCargar = @"
UPDATE dbo.Logistica_EmbarqueCajas
SET EstatusSeleccion=N'Cargada',FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
WHERE EmbarqueID=@EmbarqueID AND CajaID=@CajaID AND Activo=1 AND EstatusSeleccion=N'Reservada';";
                await using var cmd = new SqlCommand(sqlCargar, cn, tx);
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                cmd.Parameters.Add("@CajaID", SqlDbType.Int).Value = cajaId;
                var filas = await cmd.ExecuteNonQueryAsync(cancellationToken);
                if (filas <= 0) throw new InvalidOperationException($"La caja {etiqueta} cambió mientras se registraba la carga física.");
            }
            else
            {
                if (disponibleLogistica <= 0) throw new InvalidOperationException($"La caja {etiqueta} ya no tiene saldo disponible para Logística.");
                const string sqlPartidas = @"
SELECT d.EmbarqueDetalleID,d.CantidadSolicitada,ISNULL(a.Asignado,0) Asignado,d.SolicitudProduccionID,ISNULL(d.NumeroOFSnapshot,N'') NumeroOF,d.FechaEntregaReleaseSnapshot
FROM dbo.Logistica_EmbarqueDetalle d WITH(UPDLOCK,HOLDLOCK)
OUTER APPLY
(
SELECT ISNULL(SUM(ec.CantidadAsignada),0) Asignado
FROM dbo.Logistica_EmbarqueCajas ec WITH(UPDLOCK,HOLDLOCK)
WHERE ec.EmbarqueDetalleID=d.EmbarqueDetalleID AND ec.EmbarqueID=d.EmbarqueID AND ec.Activo=1
) a
WHERE d.EmbarqueID=@EmbarqueID AND d.Activo=1 AND d.ParteID=@ParteID AND d.CantidadSolicitada>ISNULL(a.Asignado,0)
ORDER BY CASE
WHEN @SolicitudProduccionID IS NOT NULL AND d.SolicitudProduccionID=@SolicitudProduccionID THEN 0
WHEN NULLIF(@NumeroOF,N'') IS NOT NULL AND UPPER(LTRIM(RTRIM(ISNULL(d.NumeroOFSnapshot,N''))))=UPPER(LTRIM(RTRIM(@NumeroOF))) THEN 1
ELSE 2 END,
ISNULL(d.FechaEntregaReleaseSnapshot,CONVERT(date,'99991231')),d.EmbarqueDetalleID;";
                var partidas = new List<(int DetalleID, int Pendiente)>();
                await using (var cmd = new SqlCommand(sqlPartidas, cn, tx))
                {
                    cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                    cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = parteId;
                    cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = Db(cajaSolicitudProduccionId);
                    cmd.Parameters.Add("@NumeroOF", SqlDbType.NVarChar, 100).Value = numeroOF;
                    await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
                    while (await rd.ReadAsync(cancellationToken))
                    {
                        var solicitado = Entero(rd, "CantidadSolicitada");
                        var asignado = Entero(rd, "Asignado");
                        var pendiente = Math.Max(0, solicitado - asignado);
                        if (pendiente > 0) partidas.Add((Entero(rd, "EmbarqueDetalleID"), pendiente));
                    }
                }
                if (partidas.Count == 0) throw new InvalidOperationException($"El embarque no tiene una partida pendiente compatible con la parte {numeroParte}.");
                var pendienteTotal = partidas.Sum(x => (long)x.Pendiente);
                if (disponibleLogistica > pendienteTotal) throw new InvalidOperationException($"La caja {etiqueta} tiene {disponibleLogistica:N0} PZA disponibles, pero al embarque solo le faltan {pendienteTotal:N0} PZA de la parte {numeroParte}. No se permite cargar una caja física parcialmente.");
                const string sqlInsert = @"
INSERT dbo.Logistica_EmbarqueCajas
(EmbarqueID,EmbarqueDetalleID,CajaID,CantidadAsignada,EstatusSeleccion,FechaSeleccion,UsuarioSeleccionID,UsuarioSeleccionNombre,Activo,FechaCreacion,CreadoPor)
VALUES
(@EmbarqueID,@DetalleID,@CajaID,@Cantidad,N'Cargada',SYSDATETIME(),@UsuarioID,@UsuarioNombre,1,SYSDATETIME(),@UsuarioNombre);";
                var restante = disponibleLogistica;
                foreach (var partida in partidas)
                {
                    if (restante <= 0) break;
                    var tomar = Math.Min(restante, partida.Pendiente);
                    await using var cmd = new SqlCommand(sqlInsert, cn, tx);
                    cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                    cmd.Parameters.Add("@DetalleID", SqlDbType.Int).Value = partida.DetalleID;
                    cmd.Parameters.Add("@CajaID", SqlDbType.Int).Value = cajaId;
                    cmd.Parameters.Add("@Cantidad", SqlDbType.Int).Value = tomar;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = Db(UsuarioID);
                    cmd.Parameters.Add("@UsuarioNombre", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                    restante -= tomar;
                    cantidadCajaAsignada += tomar;
                }
                if (restante > 0) throw new InvalidOperationException($"No fue posible distribuir completamente la caja {etiqueta} entre las partidas del embarque.");
                autoAsignada = true;
            }
            const string sqlEstado = @"
UPDATE dbo.Logistica_Embarques
SET Estatus=N'Cargando',FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
OUTPUT CONVERT(varbinary(8),INSERTED.RowVersion)
WHERE EmbarqueID=@EmbarqueID AND Activo=1 AND Estatus IN(N'Programado',N'Preparando',N'Preparado',N'Cargando');";
            byte[] nuevaVersion;
            await using (var cmd = new SqlCommand(sqlEstado, cn, tx))
            {
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                var resultado = await cmd.ExecuteScalarAsync(cancellationToken);
                if (resultado == null || resultado == DBNull.Value) throw new InvalidOperationException("El embarque cambió de estado mientras se registraba la carga.");
                nuevaVersion = (byte[])resultado;
            }
            var resumen = await ObtenerResumenCargaFisicaAsync(cn, tx, embarqueId, cancellationToken);
            await InsertarHistorialAsync(cn, tx, embarqueId, "CAJA_CARGADA_ESCANEO", estatus, "Cargando", $"Carga normal por escaneo. Caja {etiqueta}. Parte {numeroParte}. OF {(string.IsNullOrWhiteSpace(numeroOF) ? "-" : numeroOF)}. Lote {(string.IsNullOrWhiteSpace(lote) ? "-" : lote)}. Cantidad {cantidadCajaAsignada:N0} PZA.{(autoAsignada ? " La caja fue asignada automáticamente durante el escaneo." : " La caja ya estaba reservada para el embarque.")}", cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return Json(new { ok = true, yaEscaneada = false, autoAsignada, mensaje = autoAsignada ? $"Caja {etiqueta} asignada y cargada correctamente." : $"Caja {etiqueta} cargada correctamente.", caja = new { cajaId, etiqueta, numeroCaja, numeroParte, numeroOF, lote, cantidad = cantidadCajaAsignada }, totalPiezas = resumen.TotalSolicitado, piezasCargadas = resumen.PiezasCargadas, piezasPendientes = Math.Max(0, resumen.TotalSolicitado - resumen.PiezasCargadas), cajasCargadas = resumen.CajasCargadas, cargaCompleta = resumen.TotalSolicitado > 0 && resumen.PiezasCargadas == resumen.TotalSolicitado && resumen.PiezasReservadas == 0, cargaParcial = resumen.PiezasCargadas > 0 && resumen.PiezasCargadas < resumen.TotalSolicitado, puedeConfirmar = resumen.PiezasCargadas > 0 && resumen.PiezasReservadas == 0 && resumen.CajasAsignadas > 0 && resumen.CajasCargadas == resumen.CajasAsignadas, rowVersion = Convert.ToBase64String(nuevaVersion) });
        }
        catch (DBConcurrencyException ex)
        {
            try { await tx.RollbackAsync(cancellationToken); } catch { }
            return Conflict(new { ok = false, recargar = true, mensaje = ex.Message });
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(cancellationToken); } catch { }
            return BadRequest(new { ok = false, mensaje = ex.Message });
        }
    }

    private async Task<(long TotalOriginal, long TotalCargado, long Faltante, string ResumenPartes)> AplicarCargaParcialAsync(SqlConnection cn, SqlTransaction tx, int embarqueId, CancellationToken cancellationToken)
    {
        const string sqlHeader = @"
SELECT ClienteID,FechaCargaProgramada
FROM dbo.Logistica_Embarques WITH(UPDLOCK,HOLDLOCK)
WHERE EmbarqueID=@EmbarqueID AND Activo=1;";
        int clienteId;
        DateTime fechaCargaProgramada;
        await using (var cmd = new SqlCommand(sqlHeader, cn, tx))
        {
            cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await rd.ReadAsync(cancellationToken)) throw new InvalidOperationException("El embarque no existe.");
            clienteId = Entero(rd, "ClienteID");
            fechaCargaProgramada = (Fecha(rd, "FechaCargaProgramada") ?? DateTime.Today).Date;
        }
        const string sqlDetalles = @"
SELECT d.EmbarqueDetalleID,d.ReleaseDetalleID,d.ParteID,ISNULL(d.NumeroParteSnapshot,N'') NumeroParte,d.FechaEntregaReleaseSnapshot,ISNULL(d.CantidadSolicitada,0) CantidadSolicitada,
ISNULL(c.CantidadCargada,0) CantidadCargada
FROM dbo.Logistica_EmbarqueDetalle d WITH(UPDLOCK,HOLDLOCK)
OUTER APPLY
(
    SELECT SUM(CONVERT(bigint,ec.CantidadAsignada)) CantidadCargada
    FROM dbo.Logistica_EmbarqueCajas ec WITH(UPDLOCK,HOLDLOCK)
    WHERE ec.EmbarqueID=d.EmbarqueID AND ec.EmbarqueDetalleID=d.EmbarqueDetalleID AND ec.Activo=1 AND ec.EstatusSeleccion IN(N'Cargada',N'Despachada')
) c
WHERE d.EmbarqueID=@EmbarqueID AND d.Activo=1 AND d.CantidadSolicitada>0
ORDER BY d.EmbarqueDetalleID;";
        var detalles = new List<(int DetalleID, int? ReleaseDetalleID, int ParteID, string NumeroParte, DateTime? FechaRequerida, int Solicitado, int Cargado)>();
        await using (var cmd = new SqlCommand(sqlDetalles, cn, tx))
        {
            cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await rd.ReadAsync(cancellationToken))
            {
                var solicitado = Entero(rd, "CantidadSolicitada");
                var cargadoLong = EnteroLargo(rd, "CantidadCargada");
                if (cargadoLong > solicitado) throw new InvalidOperationException($"La partida {Entero(rd, "EmbarqueDetalleID")} tiene más piezas cargadas que solicitadas.");
                detalles.Add((Entero(rd, "EmbarqueDetalleID"), EnteroNullable(rd, "ReleaseDetalleID"), Entero(rd, "ParteID"), Texto(rd, "NumeroParte"), Fecha(rd, "FechaEntregaReleaseSnapshot"), solicitado, Convert.ToInt32(cargadoLong)));
            }
        }
        var totalOriginal = detalles.Sum(x => (long)x.Solicitado);
        var totalCargado = detalles.Sum(x => (long)x.Cargado);
        if (totalOriginal <= 0) throw new InvalidOperationException("El embarque no contiene piezas programadas.");
        if (totalCargado <= 0) throw new InvalidOperationException("No hay piezas cargadas para confirmar.");
        if (totalCargado >= totalOriginal) return (totalOriginal, totalCargado, 0, string.Empty);
        var programacionesAfectadas = new HashSet<int>();
        var resumenPartes = new Dictionary<(int ParteID, string NumeroParte), (long Programado, long Cargado)>();
        foreach (var detalle in detalles)
        {
            var clave = (detalle.ParteID, string.IsNullOrWhiteSpace(detalle.NumeroParte) ? $"Parte {detalle.ParteID}" : detalle.NumeroParte);
            resumenPartes.TryGetValue(clave, out var acumulado);
            resumenPartes[clave] = (acumulado.Programado + detalle.Solicitado, acumulado.Cargado + detalle.Cargado);
            if (detalle.Cargado == detalle.Solicitado) continue;
            const string sqlRelaciones = @"
SELECT x.ListaCargaProgramacionID,ISNULL(x.CantidadAsignada,0) CantidadAsignada,ISNULL(x.CantidadEnviada,0) CantidadEnviada
FROM dbo.Logistica_ListaCargaProgramacionEmbarques x WITH(UPDLOCK,HOLDLOCK)
INNER JOIN dbo.Logistica_ListaCargaProgramacion p WITH(UPDLOCK,HOLDLOCK) ON p.ListaCargaProgramacionID=x.ListaCargaProgramacionID
WHERE x.EmbarqueID=@EmbarqueID AND x.EmbarqueDetalleID=@DetalleID AND x.Activo=1
ORDER BY p.FechaRequeridaOriginal,p.ListaCargaProgramacionID;";
            var relaciones = new List<(int ProgramacionID, int Asignada, int Enviada)>();
            await using (var cmd = new SqlCommand(sqlRelaciones, cn, tx))
            {
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                cmd.Parameters.Add("@DetalleID", SqlDbType.Int).Value = detalle.DetalleID;
                await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await rd.ReadAsync(cancellationToken)) relaciones.Add((Entero(rd, "ListaCargaProgramacionID"), Entero(rd, "CantidadAsignada"), Entero(rd, "CantidadEnviada")));
            }
            if (relaciones.Any(x => x.Enviada > 0)) throw new InvalidOperationException("No puede ajustarse una carga parcial después de registrar cantidades enviadas.");
            if (relaciones.Count > 0)
            {
                var totalRelacion = relaciones.Sum(x => (long)x.Asignada);
                if (totalRelacion != detalle.Solicitado) throw new InvalidOperationException($"La partida {detalle.DetalleID} no coincide con la cantidad originada en Lista de carga. Solicitado: {detalle.Solicitado:N0}. Relacionado: {totalRelacion:N0}.");
                var restante = detalle.Cargado;
                foreach (var relacion in relaciones)
                {
                    programacionesAfectadas.Add(relacion.ProgramacionID);
                    var conservar = Math.Min(restante, relacion.Asignada);
                    if (conservar > 0)
                    {
                        const string sqlActualizarRelacion = @"
UPDATE dbo.Logistica_ListaCargaProgramacionEmbarques
SET CantidadAsignada=@Cantidad,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
WHERE ListaCargaProgramacionID=@ProgramacionID AND EmbarqueID=@EmbarqueID AND EmbarqueDetalleID=@DetalleID AND Activo=1;";
                        await using var cmd = new SqlCommand(sqlActualizarRelacion, cn, tx);
                        cmd.Parameters.Add("@Cantidad", SqlDbType.Int).Value = conservar;
                        cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                        cmd.Parameters.Add("@ProgramacionID", SqlDbType.Int).Value = relacion.ProgramacionID;
                        cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                        cmd.Parameters.Add("@DetalleID", SqlDbType.Int).Value = detalle.DetalleID;
                        await cmd.ExecuteNonQueryAsync(cancellationToken);
                        restante -= conservar;
                    }
                    else
                    {
                        const string sqlDesactivarRelacion = @"
UPDATE dbo.Logistica_ListaCargaProgramacionEmbarques
SET Activo=0,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
WHERE ListaCargaProgramacionID=@ProgramacionID AND EmbarqueID=@EmbarqueID AND EmbarqueDetalleID=@DetalleID AND Activo=1;";
                        await using var cmd = new SqlCommand(sqlDesactivarRelacion, cn, tx);
                        cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                        cmd.Parameters.Add("@ProgramacionID", SqlDbType.Int).Value = relacion.ProgramacionID;
                        cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                        cmd.Parameters.Add("@DetalleID", SqlDbType.Int).Value = detalle.DetalleID;
                        await cmd.ExecuteNonQueryAsync(cancellationToken);
                    }
                }
                if (restante > 0) throw new InvalidOperationException($"No fue posible conservar {restante:N0} PZA cargadas de la partida {detalle.DetalleID}.");
            }
            else
            {
                var faltanteDetalle = detalle.Solicitado - detalle.Cargado;
                if (faltanteDetalle > 0)
                {
                    if (!detalle.ReleaseDetalleID.HasValue || detalle.ReleaseDetalleID.Value <= 0) throw new InvalidOperationException($"La partida {detalle.DetalleID} no tiene relación con Lista de carga ni Release. No es posible devolver automáticamente {faltanteDetalle:N0} PZA a Expeditado.");
                    var semanaId = await ObtenerOCrearSemanaIdAsync(cn, tx, fechaCargaProgramada, cancellationToken);
                    const string sqlCrearResidual = @"
INSERT dbo.Logistica_ListaCargaProgramacion
(ListaCargaSemanaOrigenID,ReleaseDetalleID,ClienteID,ParteID,FechaRequeridaOriginal,FechaProgramadaCarga,HoraProgramadaCarga,CantidadProgramada,EsReprogramacion,Estatus,Observaciones,Activo,FechaCreacion,CreadoPor)
VALUES
(@SemanaID,@ReleaseDetalleID,@ClienteID,@ParteID,@FechaRequerida,@FechaCarga,NULL,@Cantidad,1,N'Parcial',@Observaciones,1,SYSDATETIME(),@Usuario);
SELECT CONVERT(int,SCOPE_IDENTITY());";
                    await using var cmd = new SqlCommand(sqlCrearResidual, cn, tx);
                    cmd.Parameters.Add("@SemanaID", SqlDbType.Int).Value = semanaId;
                    cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = detalle.ReleaseDetalleID.Value;
                    cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = clienteId;
                    cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = detalle.ParteID;
                    cmd.Parameters.Add("@FechaRequerida", SqlDbType.Date).Value = (detalle.FechaRequerida ?? fechaCargaProgramada).Date;
                    cmd.Parameters.Add("@FechaCarga", SqlDbType.Date).Value = fechaCargaProgramada;
                    cmd.Parameters.Add("@Cantidad", SqlDbType.Int).Value = faltanteDetalle;
                    cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1000).Value = $"Saldo creado automáticamente por carga parcial del embarque ID {embarqueId}.";
                    cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                    programacionesAfectadas.Add(Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)));
                }
            }
            if (detalle.Cargado > 0)
            {
                const string sqlDetalle = @"
UPDATE dbo.Logistica_EmbarqueDetalle
SET CantidadSolicitada=@Cantidad,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
WHERE EmbarqueDetalleID=@DetalleID AND EmbarqueID=@EmbarqueID AND Activo=1;";
                await using var cmd = new SqlCommand(sqlDetalle, cn, tx);
                cmd.Parameters.Add("@Cantidad", SqlDbType.Int).Value = detalle.Cargado;
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@DetalleID", SqlDbType.Int).Value = detalle.DetalleID;
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            else
            {
                const string sqlDetalle = @"
UPDATE dbo.Logistica_EmbarqueDetalle
SET Activo=0,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
WHERE EmbarqueDetalleID=@DetalleID AND EmbarqueID=@EmbarqueID AND Activo=1;";
                await using var cmd = new SqlCommand(sqlDetalle, cn, tx);
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@DetalleID", SqlDbType.Int).Value = detalle.DetalleID;
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        if (programacionesAfectadas.Count > 0)
        {
            const string sqlProgramaciones = @"
UPDATE p
SET Estatus=
CASE
    WHEN ISNULL(t.Enviado,0)>=p.CantidadProgramada THEN N'Cumplida'
    WHEN ISNULL(t.Enviado,0)>0 THEN N'Parcial'
    WHEN ISNULL(t.Generado,0)>=p.CantidadProgramada THEN N'Generada'
    ELSE N'Parcial'
END,
FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
FROM dbo.Logistica_ListaCargaProgramacion p
OUTER APPLY
(
    SELECT ISNULL(SUM(x.CantidadAsignada),0) Generado,ISNULL(SUM(x.CantidadEnviada),0) Enviado
    FROM dbo.Logistica_ListaCargaProgramacionEmbarques x
    WHERE x.ListaCargaProgramacionID=p.ListaCargaProgramacionID AND x.Activo=1
) t
WHERE p.ListaCargaProgramacionID IN(SELECT TRY_CONVERT(int,value) FROM STRING_SPLIT(@IDs,','));";
            await using var cmd = new SqlCommand(sqlProgramaciones, cn, tx);
            cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
            cmd.Parameters.Add("@IDs", SqlDbType.NVarChar, -1).Value = string.Join(",", programacionesAfectadas);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        var resumen = string.Join(" | ", resumenPartes.OrderBy(x => x.Key.NumeroParte).Select(x => $"{x.Key.NumeroParte}: programado {x.Value.Programado:N0}, cargado {x.Value.Cargado:N0}, faltante {Math.Max(0, x.Value.Programado - x.Value.Cargado):N0} PZA"));
        return (totalOriginal, totalCargado, totalOriginal - totalCargado, resumen);
    }



    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(52_428_800)]
    public async Task<IActionResult> ConfirmarCargaRapida(int embarqueId, string? rowVersion, string? motivo, bool aceptaResponsabilidad, List<IFormFile>? evidencias, CancellationToken cancellationToken = default)
    {
        var acceso = await ValidarAccesoCargaFisicaAsync(embarqueId, cancellationToken);
        if (acceso != null) return acceso;
        motivo = motivo?.Trim();
        evidencias = (evidencias ?? new List<IFormFile>()).Where(x => x != null && x.Length > 0).ToList();
        if (embarqueId <= 0) return BadRequest(new { ok = false, mensaje = "El embarque indicado no es válido." });
        if (string.IsNullOrWhiteSpace(rowVersion)) return Conflict(new { ok = false, recargar = true, mensaje = "No se recibió la versión actual del embarque. Recarga el flujo." });
        if (string.IsNullOrWhiteSpace(motivo)) return BadRequest(new { ok = false, mensaje = "La carga rápida es una excepción operativa. Captura el motivo." });
        if (motivo.Length > 1000) return BadRequest(new { ok = false, mensaje = "El motivo no puede exceder 1,000 caracteres." });
        if (!aceptaResponsabilidad) return BadRequest(new { ok = false, mensaje = "Debes confirmar que verificaste físicamente la mercancía y aceptas la responsabilidad de utilizar carga rápida." });
        if (evidencias.Count == 0) return BadRequest(new { ok = false, mensaje = "Adjunta al menos una fotografía de la mercancía o tarimas que serán cargadas." });
        if (evidencias.Count > 5) return BadRequest(new { ok = false, mensaje = "Puedes adjuntar como máximo 5 fotografías para la carga rápida." });
        if (evidencias.Sum(x => x.Length) > 50L * 1024L * 1024L) return BadRequest(new { ok = false, mensaje = "El tamaño total de las evidencias no puede exceder 50 MB." });
        foreach (var evidencia in evidencias)
        {
            var validacion = ValidarEvidencia(evidencia);
            if (!validacion.Ok) return BadRequest(new { ok = false, mensaje = validacion.Mensaje });
            if (Path.GetExtension(Path.GetFileName(evidencia.FileName)).Equals(".pdf", StringComparison.OrdinalIgnoreCase)) return BadRequest(new { ok = false, mensaje = "Para carga rápida la evidencia debe ser fotografía." });
        }
        byte[] versionOriginal;
        try { versionOriginal = Convert.FromBase64String(rowVersion); }
        catch { return Conflict(new { ok = false, recargar = true, mensaje = "La versión del embarque no es válida. Recarga el flujo." }); }
        var rutasFisicasCreadas = new List<string>();
        var evidenciasIds = new List<int>();
        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            const string sqlHeader = @"
SELECT e.ClienteID,ISNULL(e.Folio,N'') Folio,ISNULL(e.Estatus,N'') Estatus,e.FechaCargaProgramada,e.HoraCargaProgramada,CONVERT(varbinary(8),e.RowVersion) RowVersion
FROM dbo.Logistica_Embarques e WITH(UPDLOCK,HOLDLOCK)
WHERE e.EmbarqueID=@EmbarqueID AND e.Activo=1;";
            int clienteId;
            string folio, estatus;
            DateTime? fechaCarga;
            TimeSpan? horaCarga;
            byte[] versionActual;
            await using (var cmd = new SqlCommand(sqlHeader, cn, tx))
            {
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
                if (!await rd.ReadAsync(cancellationToken)) throw new InvalidOperationException("El embarque no existe.");
                clienteId = Entero(rd, "ClienteID");
                folio = Texto(rd, "Folio");
                estatus = Texto(rd, "Estatus");
                fechaCarga = Fecha(rd, "FechaCargaProgramada");
                horaCarga = Hora(rd, "HoraCargaProgramada");
                versionActual = Bytes(rd, "RowVersion");
            }
            if (!versionActual.SequenceEqual(versionOriginal)) throw new DBConcurrencyException("El embarque fue modificado por otro usuario. Recarga el flujo.");
            if (estatus is not "Programado" and not "Preparando" and not "Preparado" and not "Cargando") throw new InvalidOperationException($"La carga rápida no está disponible para un embarque en estatus {estatus}.");
            if (!fechaCarga.HasValue || !horaCarga.HasValue) throw new InvalidOperationException("El embarque debe tener fecha y hora programadas antes de utilizar carga rápida.");
            const string sqlIncidencias = @"SELECT COUNT_BIG(*) FROM dbo.Logistica_Incidencias WITH(UPDLOCK,HOLDLOCK) WHERE EmbarqueID=@EmbarqueID AND Activo=1 AND Estatus IN(N'Abierta',N'En seguimiento') AND Severidad=N'Crítica';";
            await using (var cmd = new SqlCommand(sqlIncidencias, cn, tx))
            {
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                if (Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken)) > 0) throw new InvalidOperationException("El embarque tiene una incidencia crítica abierta. La carga rápida no puede utilizarse hasta resolverla.");
            }
            var seleccion = await PrepararCajasCargaRapidaAsync(cn, tx, embarqueId, clienteId, cancellationToken);
            var resumenAntes = await ObtenerResumenCargaFisicaAsync(cn, tx, embarqueId, cancellationToken);
            if (resumenAntes.TotalSolicitado <= 0) throw new InvalidOperationException("El embarque no contiene piezas programadas.");
            if (resumenAntes.PiezasCargadas <= 0 || resumenAntes.CajasAsignadas <= 0) throw new InvalidOperationException("No fue posible determinar cajas físicas para la carga rápida.");
            if (resumenAntes.PiezasReservadas > 0) throw new InvalidOperationException($"Todavía existen {resumenAntes.PiezasReservadas:N0} PZA reservadas sin confirmar físicamente.");
            if (resumenAntes.PiezasCargadas > resumenAntes.TotalSolicitado) throw new InvalidOperationException("La selección de carga rápida supera la cantidad programada.");
            if (resumenAntes.CajasCargadas != resumenAntes.CajasAsignadas) throw new InvalidOperationException($"No se puede completar la carga rápida. Cajas asignadas: {resumenAntes.CajasAsignadas:N0}. Cajas confirmadas: {resumenAntes.CajasCargadas:N0}.");
            var esParcial = resumenAntes.PiezasCargadas < resumenAntes.TotalSolicitado;
            var ajusteParcial = esParcial
                ? await AplicarCargaParcialAsync(cn, tx, embarqueId, cancellationToken)
                : (resumenAntes.TotalSolicitado, resumenAntes.PiezasCargadas, 0L, string.Empty);
            var carpetaRelativa = Path.Combine("Logistica", "Evidencias", embarqueId.ToString());
            var carpetaFisica = Path.Combine(_environment.ContentRootPath, "App_Data", carpetaRelativa);
            Directory.CreateDirectory(carpetaFisica);
            var observacionEvidencia = $"Carga rápida/emergencia sin escaneo individual. Motivo: {motivo}. El usuario {UsuarioNombre} confirmó que verificó físicamente la mercancía y aceptó la responsabilidad.";
            if (observacionEvidencia.Length > 1000) observacionEvidencia = observacionEvidencia[..1000];
            const string sqlEvidencia = @"
INSERT dbo.Logistica_EmbarqueEvidencias
(EmbarqueID,TipoEvidencia,NombreOriginal,NombreFisico,RutaRelativa,TipoContenido,TamanoBytes,Observaciones,UsuarioID,UsuarioNombre,FechaCarga,Activo)
VALUES
(@EmbarqueID,N'Carga',@NombreOriginal,@NombreFisico,@RutaRelativa,@TipoContenido,@TamanoBytes,@Observaciones,@UsuarioID,@UsuarioNombre,SYSDATETIME(),1);
SELECT CONVERT(int,SCOPE_IDENTITY());";
            foreach (var evidencia in evidencias)
            {
                var nombreOriginal = Path.GetFileName(evidencia.FileName);
                var extension = Path.GetExtension(nombreOriginal).ToLowerInvariant();
                var nombreFisico = $"{Guid.NewGuid():N}{extension}";
                var tipoContenido = string.IsNullOrWhiteSpace(evidencia.ContentType) ? "application/octet-stream" : evidencia.ContentType.Trim();
                var rutaFisica = Path.Combine(carpetaFisica, nombreFisico);
                await using (var stream = new FileStream(rutaFisica, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                {
                    await evidencia.CopyToAsync(stream, cancellationToken);
                }
                rutasFisicasCreadas.Add(rutaFisica);
                var rutaRelativa = Path.Combine("App_Data", carpetaRelativa, nombreFisico).Replace('\\', '/');
                await using var cmd = new SqlCommand(sqlEvidencia, cn, tx);
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                cmd.Parameters.Add("@NombreOriginal", SqlDbType.NVarChar, 260).Value = nombreOriginal;
                cmd.Parameters.Add("@NombreFisico", SqlDbType.NVarChar, 260).Value = nombreFisico;
                cmd.Parameters.Add("@RutaRelativa", SqlDbType.NVarChar, 600).Value = rutaRelativa;
                cmd.Parameters.Add("@TipoContenido", SqlDbType.NVarChar, 150).Value = tipoContenido;
                cmd.Parameters.Add("@TamanoBytes", SqlDbType.BigInt).Value = evidencia.Length;
                cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1000).Value = observacionEvidencia;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = Db(UsuarioID);
                cmd.Parameters.Add("@UsuarioNombre", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                var evidenciaId = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
                if (evidenciaId <= 0) throw new InvalidOperationException($"No fue posible registrar la evidencia {nombreOriginal}.");
                evidenciasIds.Add(evidenciaId);
            }
            const string sqlUpdate = @"
UPDATE dbo.Logistica_Embarques
SET Estatus=N'Cargado',FechaCarga=SYSDATETIME(),CargaPorUsuarioID=@UsuarioID,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
OUTPUT CONVERT(varbinary(8),INSERTED.RowVersion)
WHERE EmbarqueID=@EmbarqueID AND Activo=1 AND Estatus IN(N'Programado',N'Preparando',N'Preparado',N'Cargando') AND RowVersion=@RowVersion;";
            byte[] nuevaVersion;
            await using (var cmd = new SqlCommand(sqlUpdate, cn, tx))
            {
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = Db(UsuarioID);
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                cmd.Parameters.Add("@RowVersion", SqlDbType.Timestamp).Value = versionOriginal;
                var resultado = await cmd.ExecuteScalarAsync(cancellationToken);
                if (resultado == null || resultado == DBNull.Value) throw new DBConcurrencyException("El embarque cambió mientras se confirmaba la carga rápida.");
                nuevaVersion = (byte[])resultado;
            }
            var etiquetasTexto = string.Join(", ", seleccion.Etiquetas.Take(20));
            if (seleccion.Etiquetas.Count > 20) etiquetasTexto += $" y {seleccion.Etiquetas.Count - 20:N0} más";
            var historial = esParcial
                ? $"CARGA RÁPIDA PARCIAL. Programado originalmente: {ajusteParcial.Item1:N0} PZA. Cargado: {ajusteParcial.Item2:N0} PZA. Faltante Expeditado: {ajusteParcial.Item3:N0} PZA. {ajusteParcial.Item4}. Motivo: {motivo}. Usuario: {UsuarioNombre}. Evidencias: {evidenciasIds.Count:N0}. Cajas: {etiquetasTexto}. PT se descontará al confirmar la salida."
                : $"CARGA RÁPIDA confirmada sin escaneo individual. Cajas: {resumenAntes.CajasCargadas:N0}. Piezas: {resumenAntes.PiezasCargadas:N0}. Motivo: {motivo}. Usuario: {UsuarioNombre}. Evidencias: {evidenciasIds.Count:N0}. Cajas: {etiquetasTexto}. PT se descontará al confirmar la salida.";
            if (historial.Length > 1900) historial = historial[..1900];
            await InsertarHistorialAsync(cn, tx, embarqueId, esParcial ? "CARGA_RAPIDA_PARCIAL" : "CARGA_RAPIDA_CONFIRMADA", estatus, "Cargado", historial, cancellationToken);
            await tx.CommitAsync(cancellationToken);
            var mensaje = esParcial
                ? $"{folio} cargado rápidamente con {ajusteParcial.Item2:N0} PZA. Las {ajusteParcial.Item3:N0} PZA faltantes quedaron Expeditadas."
                : $"{folio} cargado mediante Carga rápida con {resumenAntes.PiezasCargadas:N0} PZA.";
            return Json(new
            {
                ok = true,
                mensaje,
                embarqueId,
                estatus = "Cargado",
                cargaParcial = esParcial,
                cajasCargadas = resumenAntes.CajasCargadas,
                piezasCargadas = ajusteParcial.Item2,
                totalProgramadoOriginal = ajusteParcial.Item1,
                piezasExpeditadas = ajusteParcial.Item3,
                evidencias = evidenciasIds.Count,
                evidenciasIds,
                rowVersion = Convert.ToBase64String(nuevaVersion)
            });
        }
        catch (DBConcurrencyException ex)
        {
            try { await tx.RollbackAsync(cancellationToken); } catch { }
            foreach (var ruta in rutasFisicasCreadas) EliminarArchivoSiExiste(ruta);
            return Conflict(new { ok = false, recargar = true, mensaje = ex.Message });
        }
        catch (SqlException ex)
        {
            try { await tx.RollbackAsync(cancellationToken); } catch { }
            foreach (var ruta in rutasFisicasCreadas) EliminarArchivoSiExiste(ruta);
            return BadRequest(new { ok = false, tipo = "SQL", numero = ex.Number, mensaje = ex.Message });
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(cancellationToken); } catch { }
            foreach (var ruta in rutasFisicasCreadas) EliminarArchivoSiExiste(ruta);
            return BadRequest(new { ok = false, mensaje = ex.Message });
        }
    }


    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmarCargaFisica(int embarqueId, string? rowVersion, CancellationToken cancellationToken = default)
    {
        var acceso = await ValidarAccesoCargaFisicaAsync(embarqueId, cancellationToken);
        if (acceso != null) return acceso;
        if (embarqueId <= 0) return BadRequest(new { ok = false, mensaje = "El embarque indicado no es válido." });
        if (string.IsNullOrWhiteSpace(rowVersion)) return Conflict(new { ok = false, recargar = true, mensaje = "No se recibió la versión actual del embarque. Recarga el flujo." });
        byte[] versionOriginal;
        try { versionOriginal = Convert.FromBase64String(rowVersion); }
        catch { return Conflict(new { ok = false, recargar = true, mensaje = "La versión del embarque no es válida. Recarga el flujo." }); }
        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            const string sqlHeader = @"
SELECT ISNULL(Folio,N'') Folio,ISNULL(Estatus,N'') Estatus,CONVERT(varbinary(8),RowVersion) RowVersion
FROM dbo.Logistica_Embarques WITH(UPDLOCK,HOLDLOCK)
WHERE EmbarqueID=@EmbarqueID AND Activo=1;";
            string folio, estatus;
            byte[] versionActual;
            await using (var cmd = new SqlCommand(sqlHeader, cn, tx))
            {
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
                if (!await rd.ReadAsync(cancellationToken)) throw new InvalidOperationException("El embarque no existe.");
                folio = Texto(rd, "Folio");
                estatus = Texto(rd, "Estatus");
                versionActual = Bytes(rd, "RowVersion");
            }
            if (!versionActual.SequenceEqual(versionOriginal)) throw new DBConcurrencyException("El embarque fue modificado mientras se realizaba la carga. Recarga el flujo.");
            if (estatus != "Cargando") throw new InvalidOperationException($"Solo un embarque en Cargando puede confirmar su carga física. Estado actual: {estatus}.");
            var resumenAntes = await ObtenerResumenCargaFisicaAsync(cn, tx, embarqueId, cancellationToken);
            if (resumenAntes.TotalSolicitado <= 0) throw new InvalidOperationException("El embarque no contiene piezas programadas.");
            if (resumenAntes.PiezasCargadas <= 0) throw new InvalidOperationException("No hay cajas físicamente cargadas. Escanea o selecciona al menos una caja antes de confirmar.");
            if (resumenAntes.PiezasReservadas > 0) throw new InvalidOperationException($"Todavía existen {resumenAntes.PiezasReservadas:N0} PZA reservadas que no han sido confirmadas físicamente.");
            if (resumenAntes.PiezasCargadas > resumenAntes.TotalSolicitado) throw new InvalidOperationException($"Las piezas cargadas ({resumenAntes.PiezasCargadas:N0}) superan lo programado ({resumenAntes.TotalSolicitado:N0}).");
            if (resumenAntes.CajasAsignadas <= 0) throw new InvalidOperationException("El embarque no contiene cajas asignadas.");
            if (resumenAntes.CajasCargadas != resumenAntes.CajasAsignadas) throw new InvalidOperationException($"La carga física está incompleta. Cajas asignadas: {resumenAntes.CajasAsignadas:N0}. Cajas confirmadas: {resumenAntes.CajasCargadas:N0}.");
            const string sqlInconsistencias = @"
SELECT COUNT_BIG(*)
FROM dbo.Logistica_EmbarqueCajas WITH(UPDLOCK,HOLDLOCK)
WHERE EmbarqueID=@EmbarqueID AND Activo=1 AND EstatusSeleccion<>N'Cargada';
SELECT COUNT_BIG(*)
FROM dbo.Logistica_Incidencias WITH(UPDLOCK,HOLDLOCK)
WHERE EmbarqueID=@EmbarqueID AND Activo=1 AND Estatus IN(N'Abierta',N'En seguimiento') AND Severidad=N'Crítica';";
            long cajasNoCargadas;
            long incidenciasCriticas;
            await using (var cmd = new SqlCommand(sqlInconsistencias, cn, tx))
            {
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
                cajasNoCargadas = await rd.ReadAsync(cancellationToken) ? Convert.ToInt64(rd.GetValue(0)) : 0;
                incidenciasCriticas = 0;
                if (await rd.NextResultAsync(cancellationToken) && await rd.ReadAsync(cancellationToken)) incidenciasCriticas = Convert.ToInt64(rd.GetValue(0));
            }
            if (cajasNoCargadas > 0) throw new InvalidOperationException("Existe al menos una caja activa cuya carga física todavía no ha sido confirmada.");
            if (incidenciasCriticas > 0) throw new InvalidOperationException("Existen incidencias críticas abiertas. No se puede confirmar la carga.");
            var esParcial = resumenAntes.PiezasCargadas < resumenAntes.TotalSolicitado;
            var ajusteParcial = esParcial
                ? await AplicarCargaParcialAsync(cn, tx, embarqueId, cancellationToken)
                : (resumenAntes.TotalSolicitado, resumenAntes.PiezasCargadas, 0L, string.Empty);
            const string sqlUpdate = @"
UPDATE dbo.Logistica_Embarques
SET Estatus=N'Cargado',FechaCarga=SYSDATETIME(),CargaPorUsuarioID=@UsuarioID,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
OUTPUT CONVERT(varbinary(8),INSERTED.RowVersion)
WHERE EmbarqueID=@EmbarqueID AND Activo=1 AND Estatus=N'Cargando' AND RowVersion=@RowVersion;";
            byte[] nuevaVersion;
            await using (var cmd = new SqlCommand(sqlUpdate, cn, tx))
            {
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = Db(UsuarioID);
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                cmd.Parameters.Add("@RowVersion", SqlDbType.Timestamp).Value = versionOriginal;
                var resultado = await cmd.ExecuteScalarAsync(cancellationToken);
                if (resultado == null || resultado == DBNull.Value) throw new DBConcurrencyException("El embarque cambió mientras se confirmaba la carga.");
                nuevaVersion = (byte[])resultado;
            }
            if (esParcial)
            {
                var historial = $"Carga parcial de PT confirmada. Programado originalmente: {ajusteParcial.Item1:N0} PZA. Cargado: {ajusteParcial.Item2:N0} PZA. Faltante: {ajusteParcial.Item3:N0} PZA. {ajusteParcial.Item4}. El faltante quedó nuevamente disponible en Lista de carga con estatus Parcial/Expeditado. PT todavía no se descuenta; el descuento se realizará al confirmar la salida de planta.";
                if (historial.Length > 1900) historial = historial[..1900];
                await InsertarHistorialAsync(cn, tx, embarqueId, "CARGA_PARCIAL_PT", "Cargando", "Cargado", historial, cancellationToken);
            }
            else
            {
                await InsertarHistorialAsync(cn, tx, embarqueId, "CARGA_FISICA_CONFIRMADA", "Cargando", "Cargado", $"Carga física normal confirmada. Cajas: {resumenAntes.CajasCargadas:N0}. Piezas cargadas: {resumenAntes.PiezasCargadas:N0} de {resumenAntes.TotalSolicitado:N0}. PT todavía no se descuenta; el descuento se realizará al confirmar la salida de planta.", cancellationToken);
            }
            await tx.CommitAsync(cancellationToken);
            var mensaje = esParcial
                ? $"{folio} cargado parcialmente con {ajusteParcial.Item2:N0} PZA. Las {ajusteParcial.Item3:N0} PZA faltantes quedaron Expeditadas para un siguiente embarque."
                : $"{folio} cargado correctamente. Ya puede continuar a Salida de planta cuando la documentación esté completa.";
            return Json(new
            {
                ok = true,
                mensaje,
                embarqueId,
                estatus = "Cargado",
                cargaParcial = esParcial,
                totalProgramadoOriginal = ajusteParcial.Item1,
                totalPiezas = ajusteParcial.Item2,
                piezasCargadas = ajusteParcial.Item2,
                piezasExpeditadas = ajusteParcial.Item3,
                cajasCargadas = resumenAntes.CajasCargadas,
                rowVersion = Convert.ToBase64String(nuevaVersion)
            });
        }
        catch (DBConcurrencyException ex)
        {
            try { await tx.RollbackAsync(cancellationToken); } catch { }
            return Conflict(new { ok = false, recargar = true, mensaje = ex.Message });
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(cancellationToken); } catch { }
            return BadRequest(new { ok = false, mensaje = ex.Message });
        }
    }


    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmarSalidaPlanta(int embarqueId, string? rowVersion, CancellationToken cancellationToken = default)
    {
        var acceso = await ValidarAccesoAsync();
        if (acceso != null) return acceso;
        if (embarqueId <= 0) return BadRequest(new { ok = false, mensaje = "El embarque indicado no es válido." });
        if (string.IsNullOrWhiteSpace(rowVersion)) return Conflict(new { ok = false, recargar = true, mensaje = "No se recibió la versión actual del embarque. Recarga el flujo." });
        byte[] versionOriginal;
        try { versionOriginal = Convert.FromBase64String(rowVersion); }
        catch { return Conflict(new { ok = false, recargar = true, mensaje = "La versión del embarque no es válida. Recarga el flujo." }); }
        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            const string sqlHeader = @"
SELECT e.Folio,e.Estatus,e.TipoOperacion,e.FormaEnvio,e.ModalidadEnvio,e.PasaAduana,e.RutaID,e.UnidadID,e.ChoferUsuarioID,CONVERT(varbinary(8),e.RowVersion) RowVersion,
v.ViajeID,ISNULL(v.Estatus,N'') EstatusViaje,v.RutaID RutaViajeID,v.UnidadID UnidadViajeID,v.OperadorUsuarioID ChoferViajeID
FROM dbo.Logistica_Embarques e WITH(UPDLOCK,HOLDLOCK)
OUTER APPLY
(
    SELECT TOP(1) vx.ViajeID,vx.Estatus,vx.RutaID,vx.UnidadID,vx.OperadorUsuarioID
    FROM dbo.Logistica_ViajeEmbarques ve
    INNER JOIN dbo.Logistica_Viajes vx WITH(UPDLOCK,HOLDLOCK) ON vx.ViajeID=ve.ViajeID AND vx.Activo=1
    WHERE ve.EmbarqueID=e.EmbarqueID AND ve.Activo=1 AND vx.Estatus<>N'Cancelado'
    ORDER BY CASE WHEN vx.Estatus=N'Programado' THEN 0 WHEN vx.Estatus=N'En curso' THEN 1 ELSE 2 END,ve.ViajeEmbarqueID DESC
) v
WHERE e.EmbarqueID=@EmbarqueID AND e.Activo=1;";
            string folio;
            string estatus;
            string tipoOperacion;
            string formaEnvio;
            string modalidadEnvio;
            bool? pasaAduana;
            int? rutaId;
            int? unidadId;
            int? choferId;
            int? viajeId;
            string estatusViaje;
            int? rutaViajeId;
            int? unidadViajeId;
            int? choferViajeId;
            byte[] versionActual;
            await using (var cmd = new SqlCommand(sqlHeader, cn, tx))
            {
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
                if (!await rd.ReadAsync(cancellationToken)) throw new InvalidOperationException("El embarque no existe o ya no está activo.");
                folio = Texto(rd, "Folio");
                estatus = Texto(rd, "Estatus");
                tipoOperacion = NormalizarTipoOperacionFlujo(Texto(rd, "TipoOperacion"));
                formaEnvio = NormalizarFormaEnvio(Texto(rd, "FormaEnvio"));
                modalidadEnvio = NormalizarModalidadEnvioFlujo(Texto(rd, "ModalidadEnvio"));
                pasaAduana = rd.IsDBNull(rd.GetOrdinal("PasaAduana")) ? null : Convert.ToBoolean(rd["PasaAduana"]);
                rutaId = EnteroNullable(rd, "RutaID");
                unidadId = EnteroNullable(rd, "UnidadID");
                choferId = EnteroNullable(rd, "ChoferUsuarioID");
                viajeId = EnteroNullable(rd, "ViajeID");
                estatusViaje = Texto(rd, "EstatusViaje");
                rutaViajeId = EnteroNullable(rd, "RutaViajeID");
                unidadViajeId = EnteroNullable(rd, "UnidadViajeID");
                choferViajeId = EnteroNullable(rd, "ChoferViajeID");
                versionActual = Bytes(rd, "RowVersion");
            }
            if (!versionActual.SequenceEqual(versionOriginal)) throw new DBConcurrencyException("El embarque fue modificado por otro usuario. Recarga el flujo.");
            if (estatus != "Cargado") throw new InvalidOperationException($"Solo un embarque Cargado puede confirmar su salida de planta. Estado actual: {estatus}.");
            if (formaEnvio == "Cliente") throw new InvalidOperationException("Cliente recoge no utiliza En ruta. Confirma la recolección del cliente para despachar PT y cerrar el embarque directamente como Entregado.");
            if (formaEnvio == "Interno")
            {
                if (!viajeId.HasValue || viajeId.Value <= 0) throw new InvalidOperationException("La Entrega interna no tiene un viaje relacionado. Guarda nuevamente la configuración de salida antes de iniciar.");
                if (estatusViaje != "Programado") throw new InvalidOperationException($"El viaje VIA-{viajeId.Value:000000} debe estar Programado para poder iniciar. Estado actual: {estatusViaje}.");
                if (!rutaId.HasValue || rutaId.Value <= 0) throw new InvalidOperationException("El embarque no tiene una ruta asignada.");
                if (!unidadId.HasValue || unidadId.Value <= 0) throw new InvalidOperationException("El embarque no tiene una unidad asignada.");
                if (!choferId.HasValue || choferId.Value <= 0) throw new InvalidOperationException("El embarque no tiene un chofer asignado.");
                if (rutaViajeId != rutaId || unidadViajeId != unidadId || choferViajeId != choferId) throw new InvalidOperationException($"La configuración del embarque {folio} no coincide con su viaje VIA-{viajeId.Value:000000}. Guarda nuevamente la salida desde Centro Operativo para sincronizar ambos.");
                await ValidarRecursosInternosDisponiblesAlIniciarAsync(cn, tx, viajeId.Value, embarqueId, rutaId.Value, unidadId.Value, choferId.Value, cancellationToken);
                var embarquesViaje = await ValidarYObtenerEmbarquesViajeListosParaSalidaAsync(cn, tx, viajeId.Value, cancellationToken);
                var fechaSalidaInterna = DateTime.Now;
                const string sqlIniciarViaje = @"
UPDATE dbo.Logistica_Viajes
SET Estatus=N'En curso',FechaSalidaReal=@FechaSalida,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
WHERE ViajeID=@ViajeID AND Activo=1 AND Estatus=N'Programado';
SELECT @@ROWCOUNT;";
                await using (var cmd = new SqlCommand(sqlIniciarViaje, cn, tx))
                {
                    cmd.Parameters.Add("@FechaSalida", SqlDbType.DateTime2).Value = fechaSalidaInterna;
                    cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                    cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId.Value;
                    if (Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) != 1) throw new InvalidOperationException("El viaje cambió mientras se intentaba iniciar.");
                }
                await InicializarParadasViajeDesdeOperacionAsync(cn, tx, viajeId.Value, fechaSalidaInterna, cancellationToken);
                var despacho = await DespacharEmbarquesViajeDesdeOperacionAsync(cn, tx, viajeId.Value, embarqueId, embarquesViaje, fechaSalidaInterna, cancellationToken);
                await InsertarHistorialViajeAsync(cn, tx, viajeId.Value, "VIAJE_INICIADO_LOGISTICA", "Programado", "En curso", $"Viaje iniciado desde Centro Operativo por {UsuarioNombre} el {fechaSalidaInterna:dd/MM/yyyy HH:mm}. Embarques despachados: {despacho.TotalDespachados:N0}.", cancellationToken);
                const string sqlFinalInterno = @"SELECT ISNULL(Estatus,N'') Estatus,FechaSalida,CONVERT(varbinary(8),RowVersion) RowVersion FROM dbo.Logistica_Embarques WITH(UPDLOCK,HOLDLOCK) WHERE EmbarqueID=@EmbarqueID AND Activo=1;";
                string estatusFinalInterno;
                DateTime? fechaFinalInterna;
                byte[] nuevaVersionInterna;
                await using (var cmd = new SqlCommand(sqlFinalInterno, cn, tx))
                {
                    cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                    await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
                    if (!await rd.ReadAsync(cancellationToken)) throw new InvalidOperationException("No fue posible comprobar el estado final del embarque.");
                    estatusFinalInterno = Texto(rd, "Estatus");
                    fechaFinalInterna = Fecha(rd, "FechaSalida");
                    nuevaVersionInterna = Bytes(rd, "RowVersion");
                }
                if (estatusFinalInterno != "En ruta" && estatusFinalInterno != "Entregado") throw new InvalidOperationException($"El viaje inició, pero el embarque {folio} quedó en estado {estatusFinalInterno}.");
                await tx.CommitAsync(cancellationToken);
                return Json(new { ok = true, mensaje = embarquesViaje.Count > 1 ? $"Viaje VIA-{viajeId.Value:000000} iniciado correctamente. Se despacharon {embarquesViaje.Count:N0} embarques vinculados." : $"{folio} salió de planta correctamente y el viaje VIA-{viajeId.Value:000000} quedó En curso.", embarqueId, viajeId, estatus = "En ruta", formaEnvio, fechaSalida = fechaFinalInterna?.ToString("yyyy-MM-ddTHH:mm:ss"), referenciaOperacion = despacho.ReferenciaPrincipal, rowVersion = Convert.ToBase64String(nuevaVersionInterna) });
            }
            if (formaEnvio != "Paqueteria") throw new InvalidOperationException("La forma de salida actual no utiliza este proceso de salida de planta.");
            await ValidarDocumentacionSalidaPlantaAsync(cn, tx, embarqueId, tipoOperacion, formaEnvio, modalidadEnvio, pasaAduana, cancellationToken);
            var resumen = await ObtenerResumenCargaFisicaAsync(cn, tx, embarqueId, cancellationToken);
            if (resumen.TotalSolicitado <= 0) throw new InvalidOperationException("El embarque no contiene piezas programadas.");
            if (resumen.CajasAsignadas <= 0) throw new InvalidOperationException("El embarque no contiene cajas para despachar.");
            if (resumen.PiezasReservadas > 0) throw new InvalidOperationException($"Todavía existen {resumen.PiezasReservadas:N0} PZA reservadas que no han sido confirmadas mediante carga física.");
            if (resumen.PiezasCargadas != resumen.TotalSolicitado) throw new InvalidOperationException($"La carga física está incompleta. Programadas: {resumen.TotalSolicitado:N0} PZA. Cargadas: {resumen.PiezasCargadas:N0} PZA.");
            if (resumen.CajasCargadas != resumen.CajasAsignadas) throw new InvalidOperationException($"La carga física está incompleta. Cajas asignadas: {resumen.CajasAsignadas:N0}. Cajas cargadas: {resumen.CajasCargadas:N0}.");
            const string sqlIncidencias = @"SELECT COUNT_BIG(*) FROM dbo.Logistica_Incidencias WITH(UPDLOCK,HOLDLOCK) WHERE EmbarqueID=@EmbarqueID AND Activo=1 AND Estatus IN(N'Abierta',N'En seguimiento') AND Severidad=N'Crítica';";
            await using (var cmd = new SqlCommand(sqlIncidencias, cn, tx))
            {
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                if (Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken)) > 0) throw new InvalidOperationException("Existen incidencias críticas abiertas. No se puede confirmar la salida de planta.");
            }
            string referenciaOperacion;
            bool yaDespachado;
            await using (var sp = new SqlCommand("dbo.usp_Logistica_DespacharEmbarque", cn, tx))
            {
                sp.CommandType = CommandType.StoredProcedure;
                sp.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                sp.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = Db(UsuarioID);
                sp.Parameters.Add("@UsuarioNombre", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                await using var rd = await sp.ExecuteReaderAsync(cancellationToken);
                if (!await rd.ReadAsync(cancellationToken)) throw new InvalidOperationException("El procedimiento de despacho terminó sin devolver confirmación.");
                referenciaOperacion = Texto(rd, "ReferenciaOperacion");
                yaDespachado = Booleano(rd, "YaDespachado");
            }
            await ActualizarListaCargaSalidaOperacionAsync(cn, tx, embarqueId, cancellationToken);
            const string sqlEstadoFinal = @"SELECT ISNULL(Estatus,N'') Estatus,FechaSalida,CONVERT(varbinary(8),RowVersion) RowVersion FROM dbo.Logistica_Embarques WITH(UPDLOCK,HOLDLOCK) WHERE EmbarqueID=@EmbarqueID AND Activo=1;";
            string estatusFinal;
            DateTime? fechaSalida;
            byte[] nuevaVersion;
            await using (var cmd = new SqlCommand(sqlEstadoFinal, cn, tx))
            {
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
                if (!await rd.ReadAsync(cancellationToken)) throw new InvalidOperationException("No fue posible comprobar el estado final del embarque.");
                estatusFinal = Texto(rd, "Estatus");
                fechaSalida = Fecha(rd, "FechaSalida");
                nuevaVersion = Bytes(rd, "RowVersion");
            }
            if (estatusFinal != "En ruta" && estatusFinal != "Entregado") throw new InvalidOperationException($"El despacho terminó, pero el embarque quedó en estado {estatusFinal}.");
            var descripcion = $"Salida de planta confirmada desde Centro Operativo. Forma de salida: Paquetería. Fecha: {(fechaSalida ?? DateTime.Now):dd/MM/yyyy HH:mm}.";
            if (!string.IsNullOrWhiteSpace(referenciaOperacion)) descripcion += $" Referencia PT: {referenciaOperacion}.";
            await InsertarHistorialAsync(cn, tx, embarqueId, "SALIDA_CONFIRMADA_CENTRO_OPERATIVO", "Cargado", "En ruta", descripcion, cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return Json(new { ok = true, mensaje = yaDespachado ? $"{folio} ya tenía registrada su salida de PT y quedó confirmado En ruta." : $"{folio} salió de planta correctamente.", embarqueId, estatus = "En ruta", formaEnvio, fechaSalida = fechaSalida?.ToString("yyyy-MM-ddTHH:mm:ss"), referenciaOperacion, rowVersion = Convert.ToBase64String(nuevaVersion) });
        }
        catch (DBConcurrencyException ex)
        {
            try { await tx.RollbackAsync(cancellationToken); } catch { }
            return Conflict(new { ok = false, recargar = true, mensaje = ex.Message });
        }
        catch (SqlException ex)
        {
            try { await tx.RollbackAsync(cancellationToken); } catch { }
            return BadRequest(new { ok = false, tipo = "SQL", numero = ex.Number, mensaje = ex.Message });
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(cancellationToken); } catch { }
            return BadRequest(new { ok = false, mensaje = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(104_857_600)]
    public async Task<IActionResult> ConfirmarRecoleccionCliente(int embarqueId, string? rowVersion, string? receptorNombre, string? folioRemision, string? observaciones, string? observacionesEvidencia, List<IFormFile>? evidencias, CancellationToken cancellationToken = default)
    {
        var acceso = await ValidarAccesoAsync();
        if (acceso != null) return acceso;
        receptorNombre = receptorNombre?.Trim();
        folioRemision = folioRemision?.Trim();
        observaciones = observaciones?.Trim();
        observacionesEvidencia = observacionesEvidencia?.Trim();
        evidencias = (evidencias ?? new List<IFormFile>()).Where(x => x != null && x.Length > 0).ToList();
        if (embarqueId <= 0) return BadRequest(new { ok = false, mensaje = "El embarque indicado no es válido." });
        if (string.IsNullOrWhiteSpace(rowVersion)) return Conflict(new { ok = false, recargar = true, mensaje = "No se recibió la versión del embarque. Recarga el flujo." });
        if (!string.IsNullOrWhiteSpace(folioRemision) && folioRemision.Length > 100) return BadRequest(new { ok = false, mensaje = "El folio de remisión no puede exceder 100 caracteres." });
        if (!string.IsNullOrWhiteSpace(observaciones) && observaciones.Length > 1200) return BadRequest(new { ok = false, mensaje = "Las observaciones no pueden exceder 1,200 caracteres." });
        if (!string.IsNullOrWhiteSpace(observacionesEvidencia) && observacionesEvidencia.Length > 1000) return BadRequest(new { ok = false, mensaje = "Las observaciones de evidencia no pueden exceder 1,000 caracteres." });
        if (evidencias.Count == 0) return BadRequest(new { ok = false, mensaje = "Adjunta al menos una evidencia de la recolección antes de entregar la mercancía." });
        if (evidencias.Count > 15) return BadRequest(new { ok = false, mensaje = "Puedes adjuntar como máximo 15 evidencias." });
        if (evidencias.Sum(x => x.Length) > 100L * 1024L * 1024L) return BadRequest(new { ok = false, mensaje = "El tamaño total de las evidencias no puede exceder 100 MB." });
        foreach (var evidencia in evidencias)
        {
            var validacion = ValidarEvidencia(evidencia);
            if (!validacion.Ok) return BadRequest(new { ok = false, mensaje = validacion.Mensaje });
        }
        byte[] versionOriginal;
        try { versionOriginal = Convert.FromBase64String(rowVersion); }
        catch { return Conflict(new { ok = false, recargar = true, mensaje = "La versión del embarque no es válida. Recarga el flujo." }); }
        var rutasFisicasCreadas = new List<string>();
        var evidenciasIds = new List<int>();
        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            const string sqlHeader = @"
SELECT ISNULL(Folio,N'') Folio,ISNULL(Estatus,N'') Estatus,ISNULL(TipoOperacion,N'Pendiente') TipoOperacion,
ISNULL(FormaEnvio,N'Pendiente') FormaEnvio,ISNULL(ModalidadEnvio,N'') ModalidadEnvio,PasaAduana,
ISNULL(ChoferExterno,N'') ChoferExterno,CONVERT(varbinary(8),RowVersion) RowVersion
FROM dbo.Logistica_Embarques WITH(UPDLOCK,HOLDLOCK)
WHERE EmbarqueID=@EmbarqueID AND Activo=1;";
            string folio;
            string estatus;
            string tipoOperacion;
            string formaEnvio;
            string modalidadEnvio;
            string choferExterno;
            bool? pasaAduana;
            byte[] versionActual;
            await using (var cmd = new SqlCommand(sqlHeader, cn, tx))
            {
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
                if (!await rd.ReadAsync(cancellationToken)) throw new InvalidOperationException("El embarque no existe o ya no está activo.");
                folio = Texto(rd, "Folio");
                estatus = Texto(rd, "Estatus");
                tipoOperacion = NormalizarTipoOperacionFlujo(Texto(rd, "TipoOperacion"));
                formaEnvio = NormalizarFormaEnvio(Texto(rd, "FormaEnvio"));
                modalidadEnvio = NormalizarModalidadEnvioFlujo(Texto(rd, "ModalidadEnvio"));
                choferExterno = Texto(rd, "ChoferExterno");
                pasaAduana = rd.IsDBNull(rd.GetOrdinal("PasaAduana")) ? null : Convert.ToBoolean(rd["PasaAduana"]);
                versionActual = Bytes(rd, "RowVersion");
            }
            if (!versionActual.SequenceEqual(versionOriginal)) throw new DBConcurrencyException("El embarque fue modificado por otro usuario. Recarga el flujo.");
            if (estatus != "Cargado") throw new InvalidOperationException($"Solo un embarque Cargado puede entregarse al cliente. Estado actual: {estatus}.");
            if (formaEnvio != "Cliente") throw new InvalidOperationException("Este embarque no está configurado como Cliente recoge.");
            if (string.IsNullOrWhiteSpace(receptorNombre)) receptorNombre = choferExterno;
            if (string.IsNullOrWhiteSpace(receptorNombre)) throw new InvalidOperationException("Captura quién recibe o recoge la mercancía.");
            if (receptorNombre.Length > 200) throw new InvalidOperationException("El nombre de quien recibe no puede exceder 200 caracteres.");
            await ValidarDocumentacionSalidaPlantaAsync(cn, tx, embarqueId, tipoOperacion, formaEnvio, modalidadEnvio, pasaAduana, cancellationToken);
            var resumen = await ObtenerResumenCargaFisicaAsync(cn, tx, embarqueId, cancellationToken);
            if (resumen.TotalSolicitado <= 0) throw new InvalidOperationException("El embarque no contiene piezas programadas.");
            if (resumen.CajasAsignadas <= 0) throw new InvalidOperationException("El embarque no contiene cajas para entregar.");
            if (resumen.PiezasReservadas > 0) throw new InvalidOperationException($"Todavía existen {resumen.PiezasReservadas:N0} PZA reservadas sin confirmar físicamente.");
            if (resumen.PiezasCargadas != resumen.TotalSolicitado) throw new InvalidOperationException($"La carga está incompleta. Programadas: {resumen.TotalSolicitado:N0} PZA. Cargadas: {resumen.PiezasCargadas:N0} PZA.");
            if (resumen.CajasCargadas != resumen.CajasAsignadas) throw new InvalidOperationException($"La carga está incompleta. Cajas asignadas: {resumen.CajasAsignadas:N0}. Cajas cargadas: {resumen.CajasCargadas:N0}.");
            const string sqlIncidencias = @"SELECT COUNT_BIG(*) FROM dbo.Logistica_Incidencias WITH(UPDLOCK,HOLDLOCK) WHERE EmbarqueID=@EmbarqueID AND Activo=1 AND Estatus IN(N'Abierta',N'En seguimiento') AND Severidad=N'Crítica';";
            await using (var cmd = new SqlCommand(sqlIncidencias, cn, tx))
            {
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                if (Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken)) > 0) throw new InvalidOperationException("Existen incidencias críticas abiertas. No se puede entregar la mercancía.");
            }
            await DesvincularViajePorCambioFormaEnvioAsync(cn, tx, embarqueId, "Cliente", $"Limpieza preventiva de viaje antes de confirmar la recolección de {folio}.", cancellationToken);
            string referenciaOperacion;
            bool yaDespachado;
            await using (var sp = new SqlCommand("dbo.usp_Logistica_DespacharEmbarque", cn, tx))
            {
                sp.CommandType = CommandType.StoredProcedure;
                sp.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                sp.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = Db(UsuarioID);
                sp.Parameters.Add("@UsuarioNombre", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                await using var rd = await sp.ExecuteReaderAsync(cancellationToken);
                if (!await rd.ReadAsync(cancellationToken)) throw new InvalidOperationException("El procedimiento de despacho terminó sin devolver confirmación.");
                referenciaOperacion = Texto(rd, "ReferenciaOperacion");
                yaDespachado = Booleano(rd, "YaDespachado");
            }
            await ActualizarListaCargaSalidaOperacionAsync(cn, tx, embarqueId, cancellationToken);
            const string sqlPostDespacho = @"
SELECT ISNULL(e.Estatus,N'') Estatus,e.FechaSalida,CONVERT(varbinary(8),e.RowVersion) RowVersion,
ISNULL(d.TotalSolicitado,0) TotalSolicitado,ISNULL(d.TotalDespachado,0) TotalDespachado
FROM dbo.Logistica_Embarques e WITH(UPDLOCK,HOLDLOCK)
OUTER APPLY
(
    SELECT ISNULL(SUM(CONVERT(bigint,ISNULL(x.CantidadSolicitada,0))),0) TotalSolicitado,
    ISNULL(SUM(CONVERT(bigint,ISNULL(x.CantidadDespachada,0))),0) TotalDespachado
    FROM dbo.Logistica_EmbarqueDetalle x WITH(UPDLOCK,HOLDLOCK)
    WHERE x.EmbarqueID=e.EmbarqueID AND x.Activo=1
) d
WHERE e.EmbarqueID=@EmbarqueID AND e.Activo=1;";
            string estatusDespacho;
            DateTime? fechaSalida;
            byte[] versionDespacho;
            long totalSolicitado;
            long totalDespachado;
            await using (var cmd = new SqlCommand(sqlPostDespacho, cn, tx))
            {
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
                if (!await rd.ReadAsync(cancellationToken)) throw new InvalidOperationException("No fue posible comprobar el despacho del embarque.");
                estatusDespacho = Texto(rd, "Estatus");
                fechaSalida = Fecha(rd, "FechaSalida");
                versionDespacho = Bytes(rd, "RowVersion");
                totalSolicitado = EnteroLargo(rd, "TotalSolicitado");
                totalDespachado = EnteroLargo(rd, "TotalDespachado");
            }
            if (estatusDespacho != "En ruta" && estatusDespacho != "Entregado") throw new InvalidOperationException($"El despacho de PT terminó, pero el embarque quedó en estado {estatusDespacho}.");
            if (totalSolicitado <= 0 || totalDespachado != totalSolicitado) throw new InvalidOperationException($"El despacho no coincide con lo solicitado. Solicitado: {totalSolicitado:N0} PZA. Despachado: {totalDespachado:N0} PZA.");
            var fechaEntrega = fechaSalida ?? DateTime.Now;
            var carpetaRelativa = Path.Combine("Logistica", "Evidencias", embarqueId.ToString());
            var carpetaFisica = Path.Combine(_environment.ContentRootPath, "App_Data", carpetaRelativa);
            Directory.CreateDirectory(carpetaFisica);
            const string sqlEvidencia = @"
INSERT dbo.Logistica_EmbarqueEvidencias
(EmbarqueID,TipoEvidencia,NombreOriginal,NombreFisico,RutaRelativa,TipoContenido,TamanoBytes,Observaciones,UsuarioID,UsuarioNombre,FechaCarga,Activo)
VALUES
(@EmbarqueID,N'Entrega',@NombreOriginal,@NombreFisico,@RutaRelativa,@TipoContenido,@TamanoBytes,@Observaciones,@UsuarioID,@UsuarioNombre,SYSDATETIME(),1);
SELECT CONVERT(int,SCOPE_IDENTITY());";
            foreach (var evidencia in evidencias)
            {
                var nombreOriginal = Path.GetFileName(evidencia.FileName);
                var extension = Path.GetExtension(nombreOriginal).ToLowerInvariant();
                var nombreFisico = $"{Guid.NewGuid():N}{extension}";
                var tipoContenido = string.IsNullOrWhiteSpace(evidencia.ContentType) ? "application/octet-stream" : evidencia.ContentType.Trim();
                var rutaFisica = Path.Combine(carpetaFisica, nombreFisico);
                await using (var stream = new FileStream(rutaFisica, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                {
                    await evidencia.CopyToAsync(stream, cancellationToken);
                }
                rutasFisicasCreadas.Add(rutaFisica);
                var rutaRelativa = Path.Combine("App_Data", carpetaRelativa, nombreFisico).Replace('\\', '/');
                await using var cmd = new SqlCommand(sqlEvidencia, cn, tx);
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                cmd.Parameters.Add("@NombreOriginal", SqlDbType.NVarChar, 260).Value = nombreOriginal;
                cmd.Parameters.Add("@NombreFisico", SqlDbType.NVarChar, 260).Value = nombreFisico;
                cmd.Parameters.Add("@RutaRelativa", SqlDbType.NVarChar, 600).Value = rutaRelativa;
                cmd.Parameters.Add("@TipoContenido", SqlDbType.NVarChar, 150).Value = tipoContenido;
                cmd.Parameters.Add("@TamanoBytes", SqlDbType.BigInt).Value = evidencia.Length;
                cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1000).Value = Db(observacionesEvidencia);
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = Db(UsuarioID);
                cmd.Parameters.Add("@UsuarioNombre", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                var evidenciaId = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
                if (evidenciaId <= 0) throw new InvalidOperationException($"No fue posible registrar la evidencia {nombreOriginal}.");
                evidenciasIds.Add(evidenciaId);
            }
            const string sqlCerrar = @"
UPDATE dbo.Logistica_EmbarqueDetalle
SET CantidadEntregada=ISNULL(CantidadDespachada,0),FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
WHERE EmbarqueID=@EmbarqueID AND Activo=1;
UPDATE dbo.Logistica_Embarques
SET Estatus=N'Entregado',
FechaSalida=COALESCE(FechaSalida,@FechaEntrega),
FechaEntrega=@FechaEntrega,
EntregaPorUsuarioID=@UsuarioID,
ReceptorNombre=@Receptor,
FolioRemision=@Remision,
Observaciones=CASE WHEN @Observaciones IS NULL THEN Observaciones WHEN NULLIF(LTRIM(RTRIM(ISNULL(Observaciones,N''))),N'') IS NULL THEN @Observaciones ELSE CONCAT(Observaciones,NCHAR(13),NCHAR(10),@Observaciones) END,
FechaModificacion=SYSDATETIME(),
ActualizadoPor=@Usuario
OUTPUT CONVERT(varbinary(8),INSERTED.RowVersion)
WHERE EmbarqueID=@EmbarqueID AND Activo=1 AND Estatus IN(N'En ruta',N'Entregado') AND RowVersion=@RowVersion;";
            byte[] nuevaVersion;
            await using (var cmd = new SqlCommand(sqlCerrar, cn, tx))
            {
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                cmd.Parameters.Add("@FechaEntrega", SqlDbType.DateTime2).Value = fechaEntrega;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = Db(UsuarioID);
                cmd.Parameters.Add("@Receptor", SqlDbType.NVarChar, 200).Value = receptorNombre;
                cmd.Parameters.Add("@Remision", SqlDbType.NVarChar, 100).Value = Db(string.IsNullOrWhiteSpace(folioRemision) ? null : folioRemision);
                cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1200).Value = Db(string.IsNullOrWhiteSpace(observaciones) ? null : observaciones);
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@RowVersion", SqlDbType.Timestamp).Value = versionDespacho;
                var resultado = await cmd.ExecuteScalarAsync(cancellationToken);
                if (resultado == null || resultado == DBNull.Value) throw new DBConcurrencyException("El embarque cambió mientras se confirmaba la recolección.");
                nuevaVersion = (byte[])resultado;
            }
            var salidaDescripcion = $"Salida física registrada durante la recolección del cliente. Fecha: {fechaEntrega:dd/MM/yyyy HH:mm}.";
            if (!string.IsNullOrWhiteSpace(referenciaOperacion)) salidaDescripcion += $" Referencia PT: {referenciaOperacion}.";
            await InsertarHistorialAsync(cn, tx, embarqueId, "SALIDA_CONFIRMADA_CENTRO_OPERATIVO", "Cargado", estatusDespacho, salidaDescripcion, cancellationToken);
            await InsertarHistorialAsync(cn, tx, embarqueId, "EVIDENCIAS_ENTREGA", estatusDespacho, estatusDespacho, $"{evidenciasIds.Count:N0} evidencia(s) de recolección registradas.", cancellationToken);
            var descripcion = $"Recolección del cliente confirmada. Receptor: {receptorNombre}. Fecha: {fechaEntrega:dd/MM/yyyy HH:mm}. Solicitado: {totalSolicitado:N0} PZA. Entregado: {totalDespachado:N0} PZA. Evidencias: {evidenciasIds.Count:N0}.";
            if (!string.IsNullOrWhiteSpace(folioRemision)) descripcion += $" Remisión: {folioRemision}.";
            if (!string.IsNullOrWhiteSpace(observaciones)) descripcion += $" Observaciones: {observaciones}";
            await InsertarHistorialAsync(cn, tx, embarqueId, "RECOLECCION_CLIENTE_CONFIRMADA", estatusDespacho, "Entregado", descripcion, cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return Json(new { ok = true, mensaje = yaDespachado ? $"{folio} quedó cerrado como Entregado. La salida de PT ya estaba registrada." : $"{folio} entregado al cliente y cerrado correctamente.", embarqueId, estatus = "Entregado", formaEnvio = "Cliente", cierreDirecto = true, fechaSalida = fechaEntrega.ToString("yyyy-MM-ddTHH:mm:ss"), fechaEntrega = fechaEntrega.ToString("yyyy-MM-ddTHH:mm:ss"), receptor = receptorNombre, folioRemision, evidencias = evidenciasIds.Count, evidenciasIds, referenciaOperacion, rowVersion = Convert.ToBase64String(nuevaVersion) });
        }
        catch (DBConcurrencyException ex)
        {
            try { await tx.RollbackAsync(cancellationToken); } catch { }
            foreach (var ruta in rutasFisicasCreadas) EliminarArchivoSiExiste(ruta);
            return Conflict(new { ok = false, recargar = true, mensaje = ex.Message });
        }
        catch (SqlException ex)
        {
            try { await tx.RollbackAsync(cancellationToken); } catch { }
            foreach (var ruta in rutasFisicasCreadas) EliminarArchivoSiExiste(ruta);
            return BadRequest(new { ok = false, tipo = "SQL", numero = ex.Number, mensaje = ex.Message });
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(cancellationToken); } catch { }
            foreach (var ruta in rutasFisicasCreadas) EliminarArchivoSiExiste(ruta);
            return BadRequest(new { ok = false, mensaje = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ProgramarReleasesSeleccionados([FromForm] List<int> releaseDetalleIds, [FromForm] DateTime fechaProgramadaCarga, [FromForm] string? observaciones, CancellationToken cancellationToken = default)
    {
        var acceso = await ValidarAccesoAsync();
        if (acceso != null) return acceso;
        releaseDetalleIds = (releaseDetalleIds ?? new List<int>()).Where(x => x > 0).Distinct().ToList();
        if (releaseDetalleIds.Count == 0) return BadRequest(new { ok = false, mensaje = "Selecciona al menos un Release." });
        if (fechaProgramadaCarga == default) return BadRequest(new { ok = false, mensaje = "Selecciona el día programado." });
        if (fechaProgramadaCarga.Date < DateTime.Today) return BadRequest(new { ok = false, mensaje = "No puedes programar Releases en un día que ya pasó." });
        observaciones = string.IsNullOrWhiteSpace(observaciones) ? null : observaciones.Trim();
        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var semanaId = await ObtenerOCrearSemanaIdAsync(cn, tx, fechaProgramadaCarga.Date, cancellationToken);
            var programados = new List<object>();
            const string sqlDemanda = @"
SELECT TOP(1) d.ReleaseDetalleID,d.ClienteID,d.ParteID,ISNULL(d.FolioRelease,N'') FolioRelease,ISNULL(d.Cliente,N'') Cliente,ISNULL(d.NumeroParte,N'') NumeroParte,d.FechaRequerida,
ISNULL(d.CantidadRequerida,0) CantidadRequerida,ISNULL(d.PendienteProgramar,0) PendienteProgramar,ISNULL(p.ProgramadoActivo,0) ProgramadoActivo
FROM dbo.vw_Logistica_DemandaRelease d
OUTER APPLY
(
    SELECT ISNULL(SUM(x.CantidadProgramada),0) ProgramadoActivo
    FROM dbo.Logistica_ListaCargaProgramacion x
    WHERE x.ReleaseDetalleID=d.ReleaseDetalleID AND x.Activo=1 AND ISNULL(x.Estatus,N'Programada')<>N'Cancelada'
) p
WHERE d.ReleaseDetalleID=@ReleaseDetalleID;";
            const string sqlInsert = @"
INSERT dbo.Logistica_ListaCargaProgramacion
(ListaCargaSemanaOrigenID,ReleaseDetalleID,ClienteID,ParteID,FechaRequeridaOriginal,FechaProgramadaCarga,HoraProgramadaCarga,CantidadProgramada,Estatus,Observaciones,Activo,FechaCreacion,CreadoPor)
VALUES
(@SemanaID,@ReleaseDetalleID,@ClienteID,@ParteID,@FechaRequerida,@FechaCarga,NULL,@Cantidad,N'Programada',@Observaciones,1,SYSDATETIME(),@Usuario);
SELECT CONVERT(int,SCOPE_IDENTITY());";
            foreach (var releaseDetalleId in releaseDetalleIds)
            {
                int clienteId, parteId, pendiente;
                string folioRelease, cliente, numeroParte;
                DateTime fechaRequerida;
                await using (var cmd = new SqlCommand(sqlDemanda, cn, tx))
                {
                    cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = releaseDetalleId;
                    await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
                    if (!await rd.ReadAsync(cancellationToken)) throw new InvalidOperationException($"El ReleaseDetalleID {releaseDetalleId} ya no está disponible.");
                    clienteId = Entero(rd, "ClienteID");
                    parteId = Entero(rd, "ParteID");
                    var pendienteVista = Entero(rd, "PendienteProgramar");
                    var cantidadRequerida = Entero(rd, "CantidadRequerida");
                    var programadoActivo = Entero(rd, "ProgramadoActivo");
                    pendiente = Math.Min(Math.Max(0, pendienteVista), Math.Max(0, cantidadRequerida - programadoActivo));
                    folioRelease = Texto(rd, "FolioRelease");
                    cliente = Texto(rd, "Cliente");
                    numeroParte = Texto(rd, "NumeroParte");
                    fechaRequerida = (Fecha(rd, "FechaRequerida") ?? DateTime.MinValue).Date;
                }
                if (pendiente <= 0) throw new InvalidOperationException($"{folioRelease} ya no tiene cantidad pendiente por programar.");
                int programacionId;
                await using (var cmd = new SqlCommand(sqlInsert, cn, tx))
                {
                    cmd.Parameters.Add("@SemanaID", SqlDbType.Int).Value = semanaId;
                    cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = releaseDetalleId;
                    cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = clienteId;
                    cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = parteId;
                    cmd.Parameters.Add("@FechaRequerida", SqlDbType.Date).Value = fechaRequerida;
                    cmd.Parameters.Add("@FechaCarga", SqlDbType.Date).Value = fechaProgramadaCarga.Date;
                    cmd.Parameters.Add("@Cantidad", SqlDbType.Int).Value = pendiente;
                    cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1000).Value = Db(observaciones);
                    cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                    programacionId = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
                }
                programados.Add(new { programacionId, releaseDetalleId, folioRelease, cliente, numeroParte, cantidad = pendiente });
            }
            await tx.CommitAsync(cancellationToken);
            return Json(new { ok = true, mensaje = $"{programados.Count} Release(s) programado(s) para {fechaProgramadaCarga:dd/MM/yyyy}.", total = programados.Count, fechaProgramada = fechaProgramadaCarga.ToString("yyyy-MM-dd"), horaProgramada = (string?)null, programados });
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(cancellationToken); } catch { }
            return BadRequest(new { ok = false, mensaje = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarBorradorSalida(LogisticaOperacionSalidaVm model, CancellationToken cancellationToken = default)
    {
        var acceso = await ValidarAccesoAsync();
        if (acceso != null) return acceso;
        if (model.EmbarqueID <= 0) return BadRequest(new { ok = false, mensaje = "El embarque indicado no es válido." });
        if (string.IsNullOrWhiteSpace(model.RowVersion)) return Conflict(new { ok = false, recargar = true, mensaje = "No se recibió la versión del embarque. Recarga el flujo." });
        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var resultado = await GuardarSalidaOperacionAsync(cn, tx, model, false, cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return Json(new
            {
                ok = true,
                completo = resultado.Completo,
                mensaje = resultado.Completo ? "Información guardada. La forma de salida ya está completa y puedes continuar." : "Borrador guardado. Puedes cerrar y continuar después.",
                rowVersion = Convert.ToBase64String(resultado.RowVersion)
            });
        }
        catch (DBConcurrencyException ex)
        {
            try { await tx.RollbackAsync(cancellationToken); } catch { }
            return Conflict(new { ok = false, recargar = true, mensaje = ex.Message });
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(cancellationToken); } catch { }
            return BadRequest(new { ok = false, mensaje = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmarSalidaConfigurada(LogisticaOperacionSalidaVm model, CancellationToken cancellationToken = default)
    {
        var acceso = await ValidarAccesoAsync();
        if (acceso != null) return acceso;
        if (model.EmbarqueID <= 0) return BadRequest(new { ok = false, mensaje = "El embarque indicado no es válido." });
        if (string.IsNullOrWhiteSpace(model.RowVersion)) return Conflict(new { ok = false, recargar = true, mensaje = "No se recibió la versión del embarque. Recarga el flujo." });
        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var resultado = await GuardarSalidaOperacionAsync(cn, tx, model, true, cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return Json(new
            {
                ok = true,
                completo = true,
                siguientePaso = "preparacion",
                mensaje = "Forma de salida confirmada. Ya puedes continuar con la preparación del embarque.",
                rowVersion = Convert.ToBase64String(resultado.RowVersion)
            });
        }
        catch (DBConcurrencyException ex)
        {
            try { await tx.RollbackAsync(cancellationToken); } catch { }
            return Conflict(new { ok = false, recargar = true, mensaje = ex.Message });
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(cancellationToken); } catch { }
            return BadRequest(new { ok = false, mensaje = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> ObtenerProgramacionesParaEmbarque(int clienteId, DateTime fecha, CancellationToken cancellationToken = default)
    {
        var acceso = await ValidarAccesoAsync();
        if (acceso != null) return acceso;
        if (clienteId <= 0) return BadRequest(new { ok = false, mensaje = "El cliente indicado no es válido." });
        if (fecha == default) return BadRequest(new { ok = false, mensaje = "La fecha indicada no es válida." });
        await using var cn = await AbrirAsync(cancellationToken);
        const string sql = @"
SELECT p.ListaCargaProgramacionID,p.ReleaseDetalleID,p.ClienteID,p.ParteID,ISNULL(c.Nombre,N'') Cliente,p.FechaProgramadaCarga,p.FechaRequeridaOriginal,p.CantidadProgramada,
ISNULL(g.CantidadGenerada,0) CantidadGenerada,ISNULL(p.Estatus,N'Programada') EstatusProgramacion,ISNULL(d.FolioRelease,N'') FolioRelease,ISNULL(d.NumeroParte,N'') NumeroParte,ISNULL(d.Descripcion,N'') Descripcion,ISNULL(d.NumeroOF,N'') NumeroOF
FROM dbo.Logistica_ListaCargaProgramacion p
INNER JOIN dbo.ERP_Clientes c ON c.ClienteID=p.ClienteID
LEFT JOIN dbo.vw_Logistica_DemandaRelease d ON d.ReleaseDetalleID=p.ReleaseDetalleID
OUTER APPLY
(
    SELECT ISNULL(SUM(x.CantidadAsignada),0) CantidadGenerada
    FROM dbo.Logistica_ListaCargaProgramacionEmbarques x
    WHERE x.ListaCargaProgramacionID=p.ListaCargaProgramacionID AND x.Activo=1
) g
WHERE p.Activo=1
AND ISNULL(p.Estatus,N'Programada')<>N'Cancelada'
AND p.ClienteID=@ClienteID
AND p.FechaProgramadaCarga=@Fecha
AND p.CantidadProgramada>ISNULL(g.CantidadGenerada,0)
ORDER BY p.FechaRequeridaOriginal,p.ReleaseDetalleID,p.ListaCargaProgramacionID;";
        var vm = new LogisticaOperacionPrepararEmbarqueVm { ClienteID = clienteId, FechaProgramada = fecha.Date };
        await using (var cmd = new SqlCommand(sql, cn))
        {
            cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = clienteId;
            cmd.Parameters.Add("@Fecha", SqlDbType.Date).Value = fecha.Date;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await rd.ReadAsync(cancellationToken))
            {
                var cantidadProgramada = Entero(rd, "CantidadProgramada");
                var cantidadGenerada = Entero(rd, "CantidadGenerada");
                var pendiente = Math.Max(0, cantidadProgramada - cantidadGenerada);
                if (pendiente <= 0) continue;
                if (string.IsNullOrWhiteSpace(vm.Cliente)) vm.Cliente = Texto(rd, "Cliente");
                var fechaRequerida = (Fecha(rd, "FechaRequeridaOriginal") ?? DateTime.MinValue).Date;
                var estatusProgramacion = Texto(rd, "EstatusProgramacion");
                vm.Programaciones.Add(new LogisticaOperacionProgramacionEmbarqueVm
                {
                    ListaCargaProgramacionID = Entero(rd, "ListaCargaProgramacionID"),
                    ReleaseDetalleID = Entero(rd, "ReleaseDetalleID"),
                    ClienteID = Entero(rd, "ClienteID"),
                    ParteID = Entero(rd, "ParteID"),
                    FolioRelease = Texto(rd, "FolioRelease"),
                    NumeroParte = Texto(rd, "NumeroParte"),
                    Descripcion = Texto(rd, "Descripcion"),
                    NumeroOF = Texto(rd, "NumeroOF"),
                    FechaRequerida = fechaRequerida,
                    FechaProgramada = (Fecha(rd, "FechaProgramadaCarga") ?? fecha.Date).Date,
                    CantidadProgramada = cantidadProgramada,
                    CantidadGenerada = cantidadGenerada,
                    Criticidad = estatusProgramacion.Equals("Parcial", StringComparison.OrdinalIgnoreCase) || fechaRequerida < DateTime.Today ? "Expeditado" : "Programado"
                });
            }
        }
        if (vm.Programaciones.Count == 0) return NotFound(new { ok = false, mensaje = "Ya no existen Releases programados pendientes de convertir a embarque para ese cliente y día." });
        return Json(new
        {
            ok = true,
            clienteId = vm.ClienteID,
            cliente = vm.Cliente,
            fechaProgramada = vm.FechaProgramada.ToString("yyyy-MM-dd"),
            fechaTexto = vm.FechaTexto,
            totalProgramaciones = vm.TotalProgramaciones,
            totalPiezas = vm.TotalPiezas,
            programaciones = vm.Programaciones.Select(x => new
            {
                x.ListaCargaProgramacionID,
                x.ReleaseDetalleID,
                x.ClienteID,
                x.ParteID,
                x.FolioRelease,
                x.NumeroParte,
                x.Descripcion,
                x.NumeroOF,
                fechaRequerida = x.FechaRequerida.ToString("yyyy-MM-dd"),
                fechaRequeridaTexto = x.FechaRequeridaTexto,
                fechaProgramada = x.FechaProgramada.ToString("yyyy-MM-dd"),
                x.CantidadProgramada,
                x.CantidadGenerada,
                x.CantidadPendiente,
                x.Criticidad,
                x.EsExpeditado
            }).ToList()
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CrearEmbarqueDesdeProgramaciones(LogisticaOperacionGenerarEmbarqueVm model, CancellationToken cancellationToken = default)
    {
        var acceso = await ValidarAccesoAsync();
        if (acceso != null) return acceso;
        model.ProgramacionIDs = (model.ProgramacionIDs ?? new List<int>()).Where(x => x > 0).Distinct().ToList();
        if (model.ClienteID <= 0) return BadRequest(new { ok = false, mensaje = "El cliente indicado no es válido." });
        if (model.FechaProgramada == default) return BadRequest(new { ok = false, mensaje = "La fecha programada no es válida." });
        if (!model.HoraProgramada.HasValue) return BadRequest(new { ok = false, mensaje = "Selecciona la hora programada del embarque." });
        if (model.ProgramacionIDs.Count == 0) return BadRequest(new { ok = false, mensaje = "Selecciona al menos un Release programado para iniciar el embarque." });
        var fechaHora = model.FechaProgramada.Date.Add(model.HoraProgramada.Value);
        if (fechaHora < DateTime.Now) return BadRequest(new { ok = false, mensaje = "La fecha y hora del embarque no pueden estar en el pasado." });
        model.Observaciones = string.IsNullOrWhiteSpace(model.Observaciones) ? null : model.Observaciones.Trim();
        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var idsTexto = string.Join(",", model.ProgramacionIDs);
            const string sqlProgramaciones = @"
SELECT p.ListaCargaProgramacionID,p.ReleaseDetalleID,p.ClienteID,p.ParteID,p.FechaRequeridaOriginal,p.FechaProgramadaCarga,p.CantidadProgramada,ISNULL(p.Estatus,N'Programada') EstatusProgramacion,
p.CantidadProgramada-ISNULL(g.CantidadGenerada,0) PendienteGenerar,ISNULL(cli.Nombre,N'') Cliente,ISNULL(d.NumeroParte,N'') NumeroParte,
ISNULL(d.Descripcion,N'') Descripcion,ISNULL(d.FolioRelease,N'') FolioRelease,d.SolicitudProduccionID,ISNULL(d.NumeroOF,N'') NumeroOF,
d.FechaCarga FechaCargaRelease,d.FechaRequerida,d.SecuenciaEntrega
FROM dbo.Logistica_ListaCargaProgramacion p WITH(UPDLOCK,HOLDLOCK)
INNER JOIN dbo.vw_Logistica_DemandaRelease d ON d.ReleaseDetalleID=p.ReleaseDetalleID
LEFT JOIN dbo.ERP_Clientes cli ON cli.ClienteID=p.ClienteID
OUTER APPLY
(
    SELECT ISNULL(SUM(x.CantidadAsignada),0) CantidadGenerada
    FROM dbo.Logistica_ListaCargaProgramacionEmbarques x WITH(UPDLOCK,HOLDLOCK)
    WHERE x.ListaCargaProgramacionID=p.ListaCargaProgramacionID AND x.Activo=1
) g
WHERE p.Activo=1
AND ISNULL(p.Estatus,N'Programada')<>N'Cancelada'
AND p.ListaCargaProgramacionID IN(SELECT TRY_CONVERT(int,value) FROM STRING_SPLIT(@IDs,','))
AND p.CantidadProgramada>ISNULL(g.CantidadGenerada,0)
ORDER BY p.FechaRequeridaOriginal,p.ReleaseDetalleID,p.ListaCargaProgramacionID;";
            var programaciones = new List<(int ProgramacionID, int ReleaseDetalleID, int ClienteID, int ParteID, DateTime FechaProgramada, int Pendiente, string Cliente, string NumeroParte, string Descripcion, string FolioRelease, int? SolicitudProduccionID, string NumeroOF, DateTime? FechaCargaRelease, DateTime FechaRequerida, int? SecuenciaEntrega, string Criticidad)>();
            await using (var cmd = new SqlCommand(sqlProgramaciones, cn, tx))
            {
                cmd.Parameters.Add("@IDs", SqlDbType.NVarChar, -1).Value = idsTexto;
                await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await rd.ReadAsync(cancellationToken))
                {
                    var fechaRequerida = (Fecha(rd, "FechaRequerida") ?? Fecha(rd, "FechaRequeridaOriginal") ?? DateTime.MinValue).Date;
                    var pendiente = Entero(rd, "PendienteGenerar");
                    if (pendiente <= 0) continue;
                    var estatusProgramacion = Texto(rd, "EstatusProgramacion");
                    programaciones.Add((
                        Entero(rd, "ListaCargaProgramacionID"),
                        Entero(rd, "ReleaseDetalleID"),
                        Entero(rd, "ClienteID"),
                        Entero(rd, "ParteID"),
                        (Fecha(rd, "FechaProgramadaCarga") ?? DateTime.MinValue).Date,
                        pendiente,
                        Texto(rd, "Cliente"),
                        Texto(rd, "NumeroParte"),
                        Texto(rd, "Descripcion"),
                        Texto(rd, "FolioRelease"),
                        EnteroNullable(rd, "SolicitudProduccionID"),
                        Texto(rd, "NumeroOF"),
                        Fecha(rd, "FechaCargaRelease"),
                        fechaRequerida,
                        EnteroNullable(rd, "SecuenciaEntrega"),
                        estatusProgramacion.Equals("Parcial", StringComparison.OrdinalIgnoreCase) || fechaRequerida < DateTime.Today ? "Expeditado" : "Programado"
                    ));
                }
            }
            if (programaciones.Count != model.ProgramacionIDs.Count) throw new InvalidOperationException("Una o más programaciones ya fueron generadas, canceladas o modificadas. Recarga el calendario.");
            if (programaciones.Any(x => x.ClienteID != model.ClienteID)) throw new InvalidOperationException("Todas las programaciones seleccionadas deben pertenecer al mismo cliente.");
            if (programaciones.Any(x => x.FechaProgramada != model.FechaProgramada.Date)) throw new InvalidOperationException("Todas las programaciones seleccionadas deben pertenecer al día desde el que estás iniciando el embarque.");
            var cliente = programaciones.Select(x => x.Cliente).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(cliente)) throw new InvalidOperationException("El cliente no tiene un nombre válido.");
            var totalPiezas = programaciones.Sum(x => x.Pendiente);
            var fechaEntrega = programaciones.Min(x => x.FechaRequerida);
            const string sqlHeader = @"
INSERT dbo.Logistica_Embarques
(Folio,ClienteID,ClienteNombreSnapshot,Destino,DireccionEntrega,TipoOperacion,FormaEnvio,ModalidadEnvio,Transportista,GuiaReferencia,PasaAduana,FechaProgramada,FechaCargaProgramada,HoraCargaProgramada,FechaEntregaProgramada,HoraEntregaProgramada,Estatus,RutaID,UnidadID,OperadorTexto,ResponsableUsuarioID,ResponsableNombreSnapshot,Observaciones,FechaCreacion,CreadoPor,Activo)
VALUES
(NULL,@ClienteID,@Cliente,@Destino,NULL,N'Pendiente',N'Pendiente',NULL,NULL,NULL,NULL,@FechaCarga,@FechaCarga,@HoraCarga,@FechaEntrega,NULL,N'Programado',NULL,NULL,NULL,@UsuarioID,@Usuario,@Observaciones,SYSDATETIME(),@Usuario,1);
SELECT CONVERT(int,SCOPE_IDENTITY());";
            int embarqueId;
            await using (var cmd = new SqlCommand(sqlHeader, cn, tx))
            {
                cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = model.ClienteID;
                cmd.Parameters.Add("@Cliente", SqlDbType.NVarChar, 200).Value = cliente;
                cmd.Parameters.Add("@Destino", SqlDbType.NVarChar, 300).Value = cliente;
                cmd.Parameters.Add("@FechaCarga", SqlDbType.Date).Value = model.FechaProgramada.Date;
                cmd.Parameters.Add("@HoraCarga", SqlDbType.Time).Value = model.HoraProgramada.Value;
                cmd.Parameters.Add("@FechaEntrega", SqlDbType.Date).Value = fechaEntrega;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = Db(UsuarioID);
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1200).Value = Db(string.IsNullOrWhiteSpace(model.Observaciones) ? "Embarque iniciado desde Centro Operativo. Pendiente definir forma de salida." : model.Observaciones);
                embarqueId = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
            }
            var folio = $"LOG-{DateTime.Today:yyyy}-{embarqueId:000000}";
            await using (var cmd = new SqlCommand("UPDATE dbo.Logistica_Embarques SET Folio=@Folio WHERE EmbarqueID=@Id;", cn, tx))
            {
                cmd.Parameters.Add("@Folio", SqlDbType.NVarChar, 50).Value = folio;
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = embarqueId;
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            foreach (var releaseGrupo in programaciones.GroupBy(x => x.ReleaseDetalleID))
            {
                var r = releaseGrupo.First();
                var cantidadDetalle = releaseGrupo.Sum(x => x.Pendiente);
                var detalleId = await InsertarDetalleProgramacionOperacionAsync(cn, tx, embarqueId, r.ReleaseDetalleID, r.ParteID, r.SolicitudProduccionID, r.SecuenciaEntrega, r.FolioRelease, r.FechaCargaRelease, r.FechaRequerida, r.NumeroParte, r.Descripcion, r.NumeroOF, cantidadDetalle, cancellationToken);
                foreach (var p in releaseGrupo)
                {
                    const string sqlRelacion = @"
INSERT dbo.Logistica_ListaCargaProgramacionEmbarques
(ListaCargaProgramacionID,EmbarqueID,EmbarqueDetalleID,CantidadAsignada,CantidadEnviada,Criticidad,Activo,FechaCreacion,CreadoPor)
VALUES
(@ProgramacionID,@EmbarqueID,@DetalleID,@Cantidad,0,@Criticidad,1,SYSDATETIME(),@Usuario);";
                    await using var cmd = new SqlCommand(sqlRelacion, cn, tx);
                    cmd.Parameters.Add("@ProgramacionID", SqlDbType.Int).Value = p.ProgramacionID;
                    cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                    cmd.Parameters.Add("@DetalleID", SqlDbType.Int).Value = detalleId;
                    cmd.Parameters.Add("@Cantidad", SqlDbType.Int).Value = p.Pendiente;
                    cmd.Parameters.Add("@Criticidad", SqlDbType.NVarChar, 30).Value = p.Criticidad;
                    cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                }
            }
            const string sqlActualizarProgramaciones = @"
UPDATE p
SET Estatus=CASE
    WHEN ISNULL(x.Enviado,0)>=p.CantidadProgramada THEN N'Cumplida'
    WHEN ISNULL(x.Enviado,0)>0 THEN N'Parcial'
    WHEN ISNULL(x.Generado,0)>=p.CantidadProgramada THEN N'Generada'
    WHEN p.Estatus=N'Parcial' THEN N'Parcial'
    ELSE N'Programada'
END,
FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
FROM dbo.Logistica_ListaCargaProgramacion p
OUTER APPLY
(
    SELECT ISNULL(SUM(e.CantidadAsignada),0) Generado,ISNULL(SUM(e.CantidadEnviada),0) Enviado
    FROM dbo.Logistica_ListaCargaProgramacionEmbarques e
    WHERE e.ListaCargaProgramacionID=p.ListaCargaProgramacionID AND e.Activo=1
) x
WHERE p.ListaCargaProgramacionID IN(SELECT TRY_CONVERT(int,value) FROM STRING_SPLIT(@IDs,','));";
            await using (var cmd = new SqlCommand(sqlActualizarProgramaciones, cn, tx))
            {
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@IDs", SqlDbType.NVarChar, -1).Value = idsTexto;
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            await InsertarHistorialAsync(cn, tx, embarqueId, "GENERADO_CENTRO_OPERATIVO", null, "Programado", $"Embarque generado desde Centro Operativo con {programaciones.Count:N0} programación(es) y {totalPiezas:N0} PZA. Carga programada: {model.FechaProgramada:dd/MM/yyyy} {model.HoraProgramada.Value:hh\\:mm}. Pendiente definir forma de salida.", cancellationToken);
            const string sqlVersion = "SELECT CONVERT(varbinary(8),RowVersion) FROM dbo.Logistica_Embarques WHERE EmbarqueID=@Id;";
            byte[] rowVersion;
            await using (var cmd = new SqlCommand(sqlVersion, cn, tx))
            {
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = embarqueId;
                rowVersion = (byte[])(await cmd.ExecuteScalarAsync(cancellationToken) ?? Array.Empty<byte>());
            }
            await tx.CommitAsync(cancellationToken);
            return Json(new LogisticaOperacionCrearEmbarqueResultadoVm
            {
                Ok = true,
                Mensaje = $"{folio} creado correctamente. Ya puedes continuar con la forma de salida.",
                EmbarqueID = embarqueId,
                Folio = folio,
                ClienteID = model.ClienteID,
                FechaCargaProgramada = model.FechaProgramada.Date,
                HoraCargaProgramada = model.HoraProgramada.Value,
                TotalProgramaciones = programaciones.Count,
                TotalPiezas = totalPiezas,
                RowVersion = rowVersion.Length > 0 ? Convert.ToBase64String(rowVersion) : null
            });
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(cancellationToken); } catch { }
            return BadRequest(new { ok = false, mensaje = ex.Message });
        }
    }

    private static async Task<int> InsertarDetalleProgramacionOperacionAsync(SqlConnection cn, SqlTransaction tx, int embarqueId, int releaseDetalleId, int parteId, int? solicitudProduccionId, int? secuenciaEntrega, string folioRelease, DateTime? fechaCargaRelease, DateTime fechaRequerida, string numeroParte, string descripcion, string numeroOF, int cantidad, CancellationToken cancellationToken)
    {
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
    private async Task CargarEmbarquesAsync(SqlConnection cn, List<LogisticaOperacionEventoVm> eventos, DateTime fechaInicio, DateTime fechaFin, string? q, int? clienteId, string? estatus, string? formaEnvio, CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT e.EmbarqueID,e.ClienteID,ISNULL(NULLIF(LTRIM(RTRIM(e.ClienteNombreSnapshot)),N''),N'Sin cliente') Cliente,ISNULL(e.Folio,N'') Folio,ISNULL(e.Destino,N'') Destino,
e.FechaCargaProgramada,e.HoraCargaProgramada,e.FechaEntregaProgramada,ISNULL(e.Estatus,N'') Estatus,ISNULL(e.TipoOperacion,N'') TipoOperacion,ISNULL(e.FormaEnvio,N'') FormaEnvio,
ISNULL(e.ModalidadEnvio,N'') ModalidadEnvio,ISNULL(e.TieneIncidencia,0) TieneIncidencia,CONVERT(varbinary(8),e.RowVersion) RowVersion,
ISNULL(d.TotalPartidas,0) TotalPartidas,ISNULL(d.TotalPiezas,0) TotalPiezas,ISNULL(d.TotalDespachadas,0) TotalDespachadas,ISNULL(d.FechaRequeridaMin,e.FechaEntregaProgramada) FechaRequeridaMin,
ISNULL(c.TotalCajas,0) TotalCajas,ISNULL(c.TotalPreparadas,0) TotalPreparadas,ISNULL(i.IncidenciasAbiertas,0) IncidenciasAbiertas,ISNULL(i.IncidenciasCriticas,0) IncidenciasCriticas,
ISNULL(ev.TotalEvidencias,0) TotalEvidencias,ISNULL(d.NumeroParteResumen,N'') NumeroParteResumen,ISNULL(d.DescripcionResumen,N'') DescripcionResumen
FROM dbo.Logistica_Embarques e
OUTER APPLY
(
    SELECT COUNT_BIG(*) TotalPartidas,ISNULL(SUM(ed.CantidadSolicitada),0) TotalPiezas,ISNULL(SUM(ed.CantidadDespachada),0) TotalDespachadas,
    MIN(ISNULL(ed.NumeroParteSnapshot,N'')) NumeroParteResumen,MIN(ISNULL(ed.DescripcionParteSnapshot,N'')) DescripcionResumen,MIN(ed.FechaEntregaReleaseSnapshot) FechaRequeridaMin
    FROM dbo.Logistica_EmbarqueDetalle ed WHERE ed.EmbarqueID=e.EmbarqueID AND ed.Activo=1
) d
OUTER APPLY
(
    SELECT COUNT_BIG(DISTINCT ec.CajaID) TotalCajas,ISNULL(SUM(ec.CantidadAsignada),0) TotalPreparadas FROM dbo.Logistica_EmbarqueCajas ec WHERE ec.EmbarqueID=e.EmbarqueID AND ec.Activo=1
) c
OUTER APPLY
(
    SELECT COUNT_BIG(*) IncidenciasAbiertas,SUM(CASE WHEN inc.Severidad=N'Crítica' THEN 1 ELSE 0 END) IncidenciasCriticas FROM dbo.Logistica_Incidencias inc
    WHERE inc.EmbarqueID=e.EmbarqueID AND inc.Activo=1 AND inc.Estatus IN(N'Abierta',N'En seguimiento')
) i
OUTER APPLY
(
    SELECT COUNT_BIG(*) TotalEvidencias FROM dbo.Logistica_EmbarqueEvidencias ee WHERE ee.EmbarqueID=e.EmbarqueID AND ee.Activo=1
) ev
WHERE e.Activo=1 AND e.FechaCargaProgramada<@Hasta
AND (e.FechaCargaProgramada>=@Desde OR e.Estatus NOT IN(N'En ruta',N'Entregado',N'Cancelado'))
AND (@ClienteID IS NULL OR e.ClienteID=@ClienteID)
AND (@Estatus IS NULL OR e.Estatus=@Estatus)
AND (@FormaEnvio IS NULL OR e.FormaEnvio=@FormaEnvio)
AND (@Q IS NULL OR e.Folio LIKE N'%'+@Q+N'%' OR e.ClienteNombreSnapshot LIKE N'%'+@Q+N'%' OR e.Destino LIKE N'%'+@Q+N'%' OR EXISTS
(
    SELECT 1 FROM dbo.Logistica_EmbarqueDetalle dx WHERE dx.EmbarqueID=e.EmbarqueID AND dx.Activo=1
    AND (dx.NumeroParteSnapshot LIKE N'%'+@Q+N'%' OR dx.NumeroOFSnapshot LIKE N'%'+@Q+N'%' OR dx.FolioReleaseSnapshot LIKE N'%'+@Q+N'%')
))
ORDER BY e.ClienteNombreSnapshot,e.FechaCargaProgramada,e.HoraCargaProgramada,e.EmbarqueID;";
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@Desde", SqlDbType.Date).Value = fechaInicio.Date;
        cmd.Parameters.Add("@Hasta", SqlDbType.Date).Value = fechaFin.Date.AddDays(1);
        cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = Db(clienteId);
        cmd.Parameters.Add("@Estatus", SqlDbType.NVarChar, 30).Value = Db(estatus);
        cmd.Parameters.Add("@FormaEnvio", SqlDbType.NVarChar, 30).Value = Db(formaEnvio);
        cmd.Parameters.Add("@Q", SqlDbType.NVarChar, 250).Value = Db(q);
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await rd.ReadAsync(cancellationToken))
        {
            var fechaCarga = (Fecha(rd, "FechaCargaProgramada") ?? DateTime.MinValue).Date;
            if (fechaCarga == DateTime.MinValue.Date) continue;
            var estatusActual = Texto(rd, "Estatus");
            var fechaRequerida = Fecha(rd, "FechaRequeridaMin")?.Date;
            var pendienteSalida = estatusActual is not "En ruta" and not "Entregado" and not "Cancelado";
            var fechaVisual = pendienteSalida ? ResolverFechaVisualPendiente(fechaCarga, fechaInicio, fechaFin) : fechaCarga;
            var totalDespachado = Entero(rd, "TotalDespachadas");
            eventos.Add(new LogisticaOperacionEventoVm
            {
                TipoEvento = "Embarque",
                EventoID = Entero(rd, "EmbarqueID"),
                EmbarqueID = Entero(rd, "EmbarqueID"),
                ClienteID = Entero(rd, "ClienteID"),
                Cliente = Texto(rd, "Cliente"),
                Folio = Texto(rd, "Folio"),
                FechaProgramada = fechaCarga,
                FechaVisual = fechaVisual,
                HoraProgramada = Hora(rd, "HoraCargaProgramada"),
                FechaRequeridaOriginal = fechaRequerida,
                FechaEntregaRequerida = fechaRequerida,
                Estatus = estatusActual,
                Criticidad = fechaRequerida.HasValue && fechaRequerida.Value < DateTime.Today && pendienteSalida ? "Expeditado" : "Programado",
                TipoOperacion = Texto(rd, "TipoOperacion"),
                FormaEnvio = Texto(rd, "FormaEnvio"),
                ModalidadEnvio = Texto(rd, "ModalidadEnvio"),
                Destino = Texto(rd, "Destino"),
                TotalPartidas = Entero(rd, "TotalPartidas"),
                TotalCajas = Entero(rd, "TotalCajas"),
                TotalPiezas = Entero(rd, "TotalPiezas"),
                TotalPiezasPreparadas = Entero(rd, "TotalPreparadas"),
                TotalPiezasDespachadas = totalDespachado,
                CantidadEnviada = estatusActual is "En ruta" or "Entregado" ? totalDespachado : 0,
                TieneIncidencia = Booleano(rd, "TieneIncidencia"),
                IncidenciasAbiertas = Entero(rd, "IncidenciasAbiertas"),
                IncidenciasCriticas = Entero(rd, "IncidenciasCriticas"),
                Evidencias = Entero(rd, "TotalEvidencias"),
                NumeroParteResumen = Texto(rd, "NumeroParteResumen"),
                DescripcionResumen = Texto(rd, "DescripcionResumen"),
                RowVersion = Convert.ToBase64String(Bytes(rd, "RowVersion"))
            });
        }
    }
    private async Task CargarProgramacionesPendientesAsync(SqlConnection cn, List<LogisticaOperacionEventoVm> eventos, DateTime fechaInicio, DateTime fechaFin, string? q, int? clienteId, CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT p.ListaCargaProgramacionID,p.ReleaseDetalleID,p.ClienteID,p.ParteID,ISNULL(c.Nombre,N'') Cliente,p.FechaProgramadaCarga,p.HoraProgramadaCarga,p.FechaRequeridaOriginal,p.CantidadProgramada,
ISNULL(g.CantidadGenerada,0) CantidadGenerada,ISNULL(g.CantidadEnviada,0) CantidadEnviada,ISNULL(p.Estatus,N'Programada') Estatus,
ISNULL(d.FolioRelease,N'') FolioRelease,ISNULL(d.NumeroParte,N'') NumeroParte,ISNULL(d.Descripcion,N'') Descripcion,ISNULL(d.NumeroOF,N'') NumeroOF,ISNULL(d.CantidadRequerida,0) CantidadRequerida
FROM dbo.Logistica_ListaCargaProgramacion p
INNER JOIN dbo.ERP_Clientes c ON c.ClienteID=p.ClienteID
LEFT JOIN dbo.vw_Logistica_DemandaRelease d ON d.ReleaseDetalleID=p.ReleaseDetalleID
OUTER APPLY
(
    SELECT ISNULL(SUM(x.CantidadAsignada),0) CantidadGenerada,ISNULL(SUM(x.CantidadEnviada),0) CantidadEnviada
    FROM dbo.Logistica_ListaCargaProgramacionEmbarques x
    WHERE x.ListaCargaProgramacionID=p.ListaCargaProgramacionID AND x.Activo=1
) g
WHERE p.Activo=1 AND ISNULL(p.Estatus,N'Programada')<>N'Cancelada' AND p.FechaProgramadaCarga<@Hasta
AND p.CantidadProgramada>ISNULL(g.CantidadGenerada,0)
AND (@ClienteID IS NULL OR p.ClienteID=@ClienteID)
AND (@Q IS NULL OR c.Nombre LIKE N'%'+@Q+N'%' OR d.FolioRelease LIKE N'%'+@Q+N'%' OR d.NumeroParte LIKE N'%'+@Q+N'%' OR d.NumeroOF LIKE N'%'+@Q+N'%')
ORDER BY c.Nombre,p.FechaProgramadaCarga,p.HoraProgramadaCarga,p.ListaCargaProgramacionID;";
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@Hasta", SqlDbType.Date).Value = fechaFin.Date.AddDays(1);
        cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = Db(clienteId);
        cmd.Parameters.Add("@Q", SqlDbType.NVarChar, 250).Value = Db(q);
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await rd.ReadAsync(cancellationToken))
        {
            var cantidadProgramada = Entero(rd, "CantidadProgramada");
            var cantidadGenerada = Entero(rd, "CantidadGenerada");
            var pendiente = Math.Max(0, cantidadProgramada - cantidadGenerada);
            if (pendiente <= 0) continue;
            var fechaProgramada = (Fecha(rd, "FechaProgramadaCarga") ?? DateTime.MinValue).Date;
            if (fechaProgramada == DateTime.MinValue.Date) continue;
            var fechaRequerida = (Fecha(rd, "FechaRequeridaOriginal") ?? DateTime.MinValue).Date;
            var fechaVisual = ResolverFechaVisualPendiente(fechaProgramada, fechaInicio, fechaFin);
            var programacionId = Entero(rd, "ListaCargaProgramacionID");
            var estatusProgramacion = Texto(rd, "Estatus");
            eventos.Add(new LogisticaOperacionEventoVm
            {
                TipoEvento = "Programacion",
                EventoID = programacionId,
                ListaCargaProgramacionID = programacionId,
                ReleaseDetalleID = EnteroNullable(rd, "ReleaseDetalleID"),
                ParteID = EnteroNullable(rd, "ParteID"),
                ClienteID = Entero(rd, "ClienteID"),
                Cliente = Texto(rd, "Cliente"),
                Folio = $"PROG-{programacionId:000000}",
                FolioRelease = Texto(rd, "FolioRelease"),
                NumeroOF = Texto(rd, "NumeroOF"),
                NumeroParte = Texto(rd, "NumeroParte"),
                DescripcionParte = Texto(rd, "Descripcion"),
                FechaProgramada = fechaProgramada,
                FechaVisual = fechaVisual,
                HoraProgramada = Hora(rd, "HoraProgramadaCarga"),
                FechaRequeridaOriginal = fechaRequerida == DateTime.MinValue.Date ? null : fechaRequerida,
                FechaEntregaRequerida = fechaRequerida == DateTime.MinValue.Date ? null : fechaRequerida,
                Estatus = "Por generar",
                Criticidad = estatusProgramacion.Equals("Parcial", StringComparison.OrdinalIgnoreCase) || fechaRequerida != DateTime.MinValue.Date && fechaRequerida < DateTime.Today ? "Expeditado" : "Programado",
                FormaEnvio = "Pendiente",
                CantidadRequerida = Entero(rd, "CantidadRequerida"),
                CantidadProgramada = cantidadProgramada,
                CantidadGenerada = cantidadGenerada,
                CantidadEnviada = Entero(rd, "CantidadEnviada"),
                TotalPartidas = 1,
                TotalPiezas = pendiente,
                NumeroParteResumen = Texto(rd, "NumeroParte"),
                DescripcionResumen = Texto(rd, "Descripcion")
            });
        }
    }


    private async Task<List<LogisticaOperacionFiltroClienteVm>> CargarClientesFiltroAsync(SqlConnection cn, CancellationToken cancellationToken)
    {
        var lista = new List<LogisticaOperacionFiltroClienteVm>();
        const string sql = @"
SELECT DISTINCT ClienteID,Cliente FROM
(
    SELECT d.ClienteID,ISNULL(NULLIF(LTRIM(RTRIM(d.Cliente)),N''),CONCAT(N'Cliente ',d.ClienteID)) Cliente FROM dbo.vw_Logistica_DemandaRelease d WHERE d.ClienteID IS NOT NULL
    UNION
    SELECT e.ClienteID,ISNULL(NULLIF(LTRIM(RTRIM(e.ClienteNombreSnapshot)),N''),CONCAT(N'Cliente ',e.ClienteID)) Cliente FROM dbo.Logistica_Embarques e WHERE e.Activo=1 AND e.ClienteID IS NOT NULL
    UNION
    SELECT p.ClienteID,ISNULL(NULLIF(LTRIM(RTRIM(c.Nombre)),N''),CONCAT(N'Cliente ',p.ClienteID)) Cliente FROM dbo.Logistica_ListaCargaProgramacion p INNER JOIN dbo.ERP_Clientes c ON c.ClienteID=p.ClienteID WHERE p.Activo=1
) x ORDER BY Cliente;";
        await using var cmd = new SqlCommand(sql, cn);
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await rd.ReadAsync(cancellationToken)) lista.Add(new LogisticaOperacionFiltroClienteVm { ClienteID = Entero(rd, "ClienteID"), Cliente = Texto(rd, "Cliente") });
        return lista;
    }

    private async Task<LogisticaOperacionDetalleVm?> CargarDetalleAsync(SqlConnection cn, int embarqueId, CancellationToken cancellationToken)
    {
        const string sqlHeader = @"
SELECT
    e.EmbarqueID,e.ClienteID,ISNULL(e.Folio,N'') Folio,ISNULL(e.ClienteNombreSnapshot,N'') Cliente,
    ISNULL(e.Destino,N'') Destino,ISNULL(e.DireccionEntrega,N'') DireccionEntrega,e.FechaCargaProgramada,
    e.HoraCargaProgramada,e.FechaEntregaProgramada,ISNULL(e.Estatus,N'') Estatus,
    ISNULL(e.TipoOperacion,N'') TipoOperacion,ISNULL(e.FormaEnvio,N'') FormaEnvio,
    ISNULL(e.ModalidadEnvio,N'') ModalidadEnvio,ISNULL(e.Transportista,N'') Transportista,
    ISNULL(e.GuiaReferencia,N'') GuiaReferencia,
    ISNULL(r.Codigo+N' - '+r.Nombre,N'') Ruta,
    ISNULL(u.NumeroEconomico+CASE WHEN NULLIF(LTRIM(RTRIM(ISNULL(u.Placas,N''))),N'') IS NULL THEN N'' ELSE N' - '+u.Placas END,N'') Unidad,
    COALESCE(NULLIF(LTRIM(RTRIM(e.ChoferNombreSnapshot)),N''),NULLIF(LTRIM(RTRIM(e.OperadorTexto)),N''),NULLIF(LTRIM(RTRIM(e.ChoferExterno)),N''),N'') Chofer,
    ISNULL(e.TieneIncidencia,0) TieneIncidencia,
    ISNULL(d.TotalPartidas,0) TotalPartidas,ISNULL(d.TotalPiezas,0) TotalPiezas,ISNULL(d.TotalDespachadas,0) TotalDespachadas,
    ISNULL(c.TotalCajas,0) TotalCajas,ISNULL(c.TotalPreparadas,0) TotalPreparadas,
    ISNULL(i.IncidenciasAbiertas,0) IncidenciasAbiertas,ISNULL(i.IncidenciasCriticas,0) IncidenciasCriticas,
    ISNULL(doc.TotalDocumentos,0) TotalDocumentos,ISNULL(doc.DocumentosNoValidados,0) DocumentosNoValidados
FROM dbo.Logistica_Embarques e
LEFT JOIN dbo.Logistica_Rutas r ON r.RutaID=e.RutaID
LEFT JOIN dbo.Logistica_Unidades u ON u.UnidadID=e.UnidadID
OUTER APPLY
(
    SELECT COUNT_BIG(*) TotalPartidas,ISNULL(SUM(ed.CantidadSolicitada),0) TotalPiezas,ISNULL(SUM(ed.CantidadDespachada),0) TotalDespachadas
    FROM dbo.Logistica_EmbarqueDetalle ed
    WHERE ed.EmbarqueID=e.EmbarqueID AND ed.Activo=1
) d
OUTER APPLY
(
    SELECT COUNT_BIG(DISTINCT ec.CajaID) TotalCajas,ISNULL(SUM(ec.CantidadAsignada),0) TotalPreparadas
    FROM dbo.Logistica_EmbarqueCajas ec
    WHERE ec.EmbarqueID=e.EmbarqueID AND ec.Activo=1
) c
OUTER APPLY
(
    SELECT COUNT_BIG(*) IncidenciasAbiertas,ISNULL(SUM(CASE WHEN inc.Severidad=N'Crítica' THEN 1 ELSE 0 END),0) IncidenciasCriticas
    FROM dbo.Logistica_Incidencias inc
    WHERE inc.EmbarqueID=e.EmbarqueID AND inc.Activo=1 AND inc.Estatus IN(N'Abierta',N'En seguimiento')
) i
OUTER APPLY
(
    SELECT COUNT_BIG(*) TotalDocumentos,ISNULL(SUM(CASE WHEN ISNULL(x.Validado,0)=0 THEN 1 ELSE 0 END),0) DocumentosNoValidados
    FROM dbo.Logistica_EmbarqueDocumentos x
    WHERE x.EmbarqueID=e.EmbarqueID AND x.Activo=1
) doc
WHERE e.EmbarqueID=@EmbarqueID AND e.Activo=1;";
        LogisticaOperacionDetalleVm? vm;
        await using (var cmd = new SqlCommand(sqlHeader, cn))
        {
            cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await rd.ReadAsync(cancellationToken)) return null;
            var estatus = Texto(rd, "Estatus");
            var forma = NormalizarFormaEnvio(Texto(rd, "FormaEnvio"));
            var total = Entero(rd, "TotalPiezas");
            var preparadas = Entero(rd, "TotalPreparadas");
            var ruta = Texto(rd, "Ruta");
            var unidad = Texto(rd, "Unidad");
            var chofer = Texto(rd, "Chofer");
            var transportista = Texto(rd, "Transportista");
            vm = new LogisticaOperacionDetalleVm
            {
                EmbarqueID = Entero(rd, "EmbarqueID"),
                Folio = Texto(rd, "Folio"),
                ClienteID = Entero(rd, "ClienteID"),
                Cliente = Texto(rd, "Cliente"),
                Destino = Texto(rd, "Destino"),
                DireccionEntrega = Texto(rd, "DireccionEntrega"),
                FechaCargaProgramada = Fecha(rd, "FechaCargaProgramada"),
                HoraCargaProgramada = Hora(rd, "HoraCargaProgramada"),
                FechaEntregaProgramada = Fecha(rd, "FechaEntregaProgramada"),
                Estatus = estatus,
                TipoOperacion = Texto(rd, "TipoOperacion"),
                FormaEnvio = forma,
                ModalidadEnvio = Texto(rd, "ModalidadEnvio"),
                Transportista = transportista,
                GuiaReferencia = Texto(rd, "GuiaReferencia"),
                Ruta = ruta,
                Unidad = unidad,
                Chofer = chofer,
                TotalPartidas = Entero(rd, "TotalPartidas"),
                TotalCajas = Entero(rd, "TotalCajas"),
                TotalPiezas = total,
                TotalPiezasPreparadas = preparadas,
                TotalPiezasDespachadas = Entero(rd, "TotalDespachadas"),
                PorcentajeAvance = CalcularPorcentajeAvance(estatus),
                TieneIncidencia = Booleano(rd, "TieneIncidencia"),
                IncidenciasAbiertas = Entero(rd, "IncidenciasAbiertas"),
                IncidenciasCriticas = Entero(rd, "IncidenciasCriticas"),
                TotalDocumentos = Entero(rd, "TotalDocumentos"),
                DocumentosFaltantes = Entero(rd, "DocumentosNoValidados"),
                DatosTransporteCompletos = DatosTransporteCompletos(forma, ruta, unidad, chofer, transportista),
                PreparacionCompleta = total > 0 && preparadas >= total,
                DocumentacionCompleta = Entero(rd, "DocumentosNoValidados") == 0
            };
            CalcularProximaAccion(vm);
        }

        const string sqlPartidas = @"
SELECT d.EmbarqueDetalleID,d.ReleaseDetalleID,ISNULL(d.FolioReleaseSnapshot,N'') FolioRelease,
ISNULL(d.NumeroParteSnapshot,N'') NumeroParte,ISNULL(d.DescripcionParteSnapshot,N'') Descripcion,
ISNULL(d.NumeroOFSnapshot,N'') NumeroOF,ISNULL(d.CantidadSolicitada,0) CantidadSolicitada,
ISNULL(d.CantidadDespachada,0) CantidadDespachada,ISNULL(c.CantidadPreparada,0) CantidadPreparada
FROM dbo.Logistica_EmbarqueDetalle d
OUTER APPLY
(
    SELECT ISNULL(SUM(ec.CantidadAsignada),0) CantidadPreparada
    FROM dbo.Logistica_EmbarqueCajas ec
    WHERE ec.EmbarqueDetalleID=d.EmbarqueDetalleID AND ec.EmbarqueID=d.EmbarqueID AND ec.Activo=1
) c
WHERE d.EmbarqueID=@EmbarqueID AND d.Activo=1
ORDER BY d.EmbarqueDetalleID;";
        await using (var cmd = new SqlCommand(sqlPartidas, cn))
        {
            cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await rd.ReadAsync(cancellationToken))
            {
                vm.Partidas.Add(new LogisticaOperacionPartidaVm
                {
                    EmbarqueDetalleID = Entero(rd, "EmbarqueDetalleID"),
                    ReleaseDetalleID = EnteroNullable(rd, "ReleaseDetalleID"),
                    FolioRelease = Texto(rd, "FolioRelease"),
                    NumeroParte = Texto(rd, "NumeroParte"),
                    Descripcion = Texto(rd, "Descripcion"),
                    NumeroOF = Texto(rd, "NumeroOF"),
                    CantidadSolicitada = Entero(rd, "CantidadSolicitada"),
                    CantidadPreparada = Entero(rd, "CantidadPreparada"),
                    CantidadDespachada = Entero(rd, "CantidadDespachada")
                });
            }
        }

        const string sqlHistorial = @"
SELECT HistorialID,EmbarqueID,ISNULL(Evento,N'') Evento,ISNULL(EstadoAnterior,N'') EstadoAnterior,
ISNULL(EstadoNuevo,N'') EstadoNuevo,ISNULL(Observaciones,N'') Observaciones,UsuarioID,
ISNULL(UsuarioNombre,N'') Usuario,FechaEvento
FROM dbo.Logistica_EmbarqueHistorial
WHERE EmbarqueID=@EmbarqueID
ORDER BY FechaEvento DESC,HistorialID DESC;";
        await using (var cmd = new SqlCommand(sqlHistorial, cn))
        {
            cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await rd.ReadAsync(cancellationToken))
            {
                vm.Historial.Add(new LogisticaOperacionHistorialVm
                {
                    HistorialID = Entero(rd, "HistorialID"),
                    EmbarqueID = Entero(rd, "EmbarqueID"),
                    Evento = Texto(rd, "Evento"),
                    EstadoAnterior = Texto(rd, "EstadoAnterior"),
                    EstadoNuevo = Texto(rd, "EstadoNuevo"),
                    Observaciones = Texto(rd, "Observaciones"),
                    UsuarioID = EnteroNullable(rd, "UsuarioID"),
                    Usuario = Texto(rd, "Usuario"),
                    Fecha = Fecha(rd, "FechaEvento") ?? DateTime.MinValue
                });
            }
        }

        vm.Evidencias = await CargarEvidenciasCompartidasAsync(cn, embarqueId, cancellationToken);
        vm.TotalEvidencias = vm.Evidencias.Count;
        return vm;
    }

    private async Task<List<LogisticaOperacionEvidenciaResumenVm>> CargarEvidenciasCompartidasAsync(SqlConnection cn, int embarqueId, CancellationToken cancellationToken)
    {
        var lista = new List<LogisticaOperacionEvidenciaResumenVm>();
        const string sql = @"
SELECT
    ee.EvidenciaID,
    CAST(NULL AS int) AS ViajeID,
    N'Embarque' AS Origen,
    ISNULL(ee.TipoEvidencia,N'') AS TipoEvidencia,
    ISNULL(ee.NombreOriginal,N'') AS NombreOriginal,
    ISNULL(ee.TipoContenido,N'') AS TipoContenido,
    ISNULL(ee.TamanoBytes,0) AS TamanoBytes,
    ISNULL(ee.Observaciones,N'') AS Observaciones,
    ee.FechaCarga,
    ISNULL(ee.UsuarioNombre,N'') AS Usuario
FROM dbo.Logistica_EmbarqueEvidencias ee
WHERE ee.EmbarqueID=@EmbarqueID AND ee.Activo=1
UNION ALL
SELECT
    ve.ViajeEvidenciaID AS EvidenciaID,
    ve.ViajeID,
    N'Viaje' AS Origen,
    ISNULL(ve.TipoEvidencia,N'') AS TipoEvidencia,
    ISNULL(ve.NombreOriginal,N'') AS NombreOriginal,
    ISNULL(ve.TipoContenido,N'') AS TipoContenido,
    ISNULL(ve.TamanoBytes,0) AS TamanoBytes,
    ISNULL(ve.Observaciones,N'') AS Observaciones,
    ve.FechaCarga,
    ISNULL(ve.UsuarioCargaNombre,N'') AS Usuario
FROM dbo.Logistica_ViajeEmbarques rel
INNER JOIN dbo.Logistica_Viajes v ON v.ViajeID=rel.ViajeID AND v.Activo=1
INNER JOIN dbo.Logistica_ViajeEvidencias ve ON ve.ViajeID=v.ViajeID AND ve.Activo=1
WHERE rel.EmbarqueID=@EmbarqueID AND rel.Activo=1
UNION ALL
SELECT
    pe.ViajeParadaEvidenciaID AS EvidenciaID,
    p.ViajeID,
    N'Parada' AS Origen,
    ISNULL(pe.TipoEvidencia,N'') AS TipoEvidencia,
    ISNULL(pe.NombreOriginal,N'') AS NombreOriginal,
    ISNULL(pe.TipoContenido,N'') AS TipoContenido,
    ISNULL(pe.TamanoBytes,0) AS TamanoBytes,
    ISNULL(pe.Observaciones,N'') AS Observaciones,
    pe.FechaCarga,
    ISNULL(pe.UsuarioCargaNombre,N'') AS Usuario
FROM dbo.Logistica_ViajeEmbarques rel
INNER JOIN dbo.Logistica_Viajes v ON v.ViajeID=rel.ViajeID AND v.Activo=1
INNER JOIN dbo.Logistica_ViajeParadas p ON p.ViajeID=rel.ViajeID AND p.Activo=1
AND
(
    (rel.ViajeParadaID IS NOT NULL AND p.ViajeParadaID=rel.ViajeParadaID)
    OR
    (rel.ViajeParadaID IS NULL AND ISNULL(p.ReferenciaTipo,N'')=N'Embarque' AND p.ReferenciaID=@EmbarqueID)
)
INNER JOIN dbo.Logistica_ViajeParadaEvidencias pe ON pe.ViajeParadaID=p.ViajeParadaID AND pe.Activo=1
WHERE rel.EmbarqueID=@EmbarqueID AND rel.Activo=1
ORDER BY FechaCarga DESC,EvidenciaID DESC;";
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await rd.ReadAsync(cancellationToken))
        {
            lista.Add(new LogisticaOperacionEvidenciaResumenVm
            {
                EvidenciaID = Entero(rd, "EvidenciaID"),
                ViajeID = EnteroNullable(rd, "ViajeID"),
                Origen = Texto(rd, "Origen"),
                TipoEvidencia = Texto(rd, "TipoEvidencia"),
                NombreOriginal = Texto(rd, "NombreOriginal"),
                TipoContenido = Texto(rd, "TipoContenido"),
                TamanoBytes = EnteroLargo(rd, "TamanoBytes"),
                Observaciones = Texto(rd, "Observaciones"),
                FechaCarga = Fecha(rd, "FechaCarga") ?? DateTime.MinValue,
                Usuario = Texto(rd, "Usuario")
            });
        }
        return lista;
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
(
    EmbarqueID,
    Evento,
    EstadoAnterior,
    EstadoNuevo,
    Observaciones,
    UsuarioID,
    UsuarioNombre,
    FechaEvento
)
VALUES
(
    @EmbarqueID,
    @Evento,
    @Anterior,
    @Nuevo,
    @Observaciones,
    @UsuarioID,
    @UsuarioNombre,
    SYSDATETIME()
);";

        await using var cmd =
            new SqlCommand(
                sql,
                cn,
                tx);

        cmd.Parameters.Add(
            "@EmbarqueID",
            SqlDbType.Int).Value =
            embarqueId;

        cmd.Parameters.Add(
            "@Evento",
            SqlDbType.NVarChar,
            80).Value =
            evento;

        cmd.Parameters.Add(
            "@Anterior",
            SqlDbType.NVarChar,
            20).Value =
            Db(anterior);

        cmd.Parameters.Add(
            "@Nuevo",
            SqlDbType.NVarChar,
            20).Value =
            Db(nuevo);

        cmd.Parameters.Add(
            "@Observaciones",
            SqlDbType.NVarChar,
            1200).Value =
            Db(observaciones);

        cmd.Parameters.Add(
            "@UsuarioID",
            SqlDbType.Int).Value =
            Db(UsuarioID);

        cmd.Parameters.Add(
            "@UsuarioNombre",
            SqlDbType.NVarChar,
            200).Value =
            UsuarioNombre;

        await cmd.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private async Task InsertarHistorialViajeAsync(SqlConnection cn, SqlTransaction tx, int viajeId, string evento, string? anterior, string? nuevo, string? observaciones, CancellationToken cancellationToken)
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

    private async Task<int> AsegurarParadasEntregaViajeAsync(SqlConnection cn, SqlTransaction tx, int viajeId, int embarqueId, string folioEmbarque, int clienteId, string cliente, string destino, string direccion, DateTime fechaProgramada, TimeSpan horaProgramada, DateTime? fechaEntregaProgramada, TimeSpan? horaEntregaProgramada, CancellationToken cancellationToken)
    {
        var lugarEntrega = string.IsNullOrWhiteSpace(destino) ? (string.IsNullOrWhiteSpace(cliente) ? $"Cliente {clienteId}" : cliente) : destino;
        var fechaHoraSalida = fechaProgramada.Date.Add(horaProgramada);
        DateTime? fechaHoraEntrega = fechaEntregaProgramada.HasValue ? fechaEntregaProgramada.Value.Date.Add(horaEntregaProgramada ?? TimeSpan.Zero) : null;
        int? viajeEmbarqueId = null;
        int? paradaEntregaId = null;
        const string sqlRelacionActual = @"SELECT TOP(1) ve.ViajeEmbarqueID,ve.ViajeParadaID FROM dbo.Logistica_ViajeEmbarques ve WITH(UPDLOCK,HOLDLOCK) WHERE ve.ViajeID=@ViajeID AND ve.EmbarqueID=@EmbarqueID AND ve.Activo=1 ORDER BY ve.ViajeEmbarqueID DESC;";
        await using (var cmd = new SqlCommand(sqlRelacionActual, cn, tx))
        {
            cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
            cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await rd.ReadAsync(cancellationToken))
            {
                viajeEmbarqueId = Entero(rd, "ViajeEmbarqueID");
                paradaEntregaId = EnteroNullable(rd, "ViajeParadaID");
            }
        }
        if (paradaEntregaId.HasValue)
        {
            const string sqlValidar = @"SELECT COUNT_BIG(*) FROM dbo.Logistica_ViajeParadas WHERE ViajeParadaID=@ParadaID AND ViajeID=@ViajeID AND Activo=1 AND TipoParada=N'Entrega';";
            await using var cmd = new SqlCommand(sqlValidar, cn, tx);
            cmd.Parameters.Add("@ParadaID", SqlDbType.Int).Value = paradaEntregaId.Value;
            cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
            if (Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken)) <= 0) paradaEntregaId = null;
        }
        if (!paradaEntregaId.HasValue)
        {
            const string sqlReferencia = @"SELECT TOP(1) ViajeParadaID FROM dbo.Logistica_ViajeParadas WITH(UPDLOCK,HOLDLOCK) WHERE ViajeID=@ViajeID AND Activo=1 AND TipoParada=N'Entrega' AND ReferenciaTipo=N'Embarque' AND ReferenciaID=@EmbarqueID ORDER BY Secuencia,ViajeParadaID;";
            await using var cmd = new SqlCommand(sqlReferencia, cn, tx);
            cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
            cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
            var valor = await cmd.ExecuteScalarAsync(cancellationToken);
            if (valor != null && valor != DBNull.Value) paradaEntregaId = Convert.ToInt32(valor);
        }
        long totalParadas;
        int? paradaOrigenId;
        int? paradaCierreId;
        int? secuenciaCierre;
        int maxSecuencia;
        const string sqlEstadoParadas = @"
SELECT COUNT_BIG(*) TotalParadas,ISNULL(MAX(Secuencia),0) MaxSecuencia FROM dbo.Logistica_ViajeParadas WITH(UPDLOCK,HOLDLOCK) WHERE ViajeID=@ViajeID AND Activo=1;
SELECT TOP(1) ViajeParadaID FROM dbo.Logistica_ViajeParadas WITH(UPDLOCK,HOLDLOCK) WHERE ViajeID=@ViajeID AND Activo=1 AND TipoParada=N'Origen' ORDER BY Secuencia,ViajeParadaID;
SELECT TOP(1) ViajeParadaID,Secuencia FROM dbo.Logistica_ViajeParadas WITH(UPDLOCK,HOLDLOCK) WHERE ViajeID=@ViajeID AND Activo=1 AND CierraViaje=1 ORDER BY Secuencia DESC,ViajeParadaID DESC;";
        await using (var cmd = new SqlCommand(sqlEstadoParadas, cn, tx))
        {
            cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await rd.ReadAsync(cancellationToken)) throw new InvalidOperationException("No fue posible revisar las paradas del viaje.");
            totalParadas = Convert.ToInt64(rd["TotalParadas"]);
            maxSecuencia = Entero(rd, "MaxSecuencia");
            paradaOrigenId = null;
            paradaCierreId = null;
            secuenciaCierre = null;
            if (await rd.NextResultAsync(cancellationToken) && await rd.ReadAsync(cancellationToken)) paradaOrigenId = Entero(rd, "ViajeParadaID");
            if (await rd.NextResultAsync(cancellationToken) && await rd.ReadAsync(cancellationToken))
            {
                paradaCierreId = Entero(rd, "ViajeParadaID");
                secuenciaCierre = Entero(rd, "Secuencia");
            }
        }
        if (totalParadas == 0)
        {
            const string sqlOrigen = @"INSERT dbo.Logistica_ViajeParadas(ViajeID,Secuencia,TipoParada,TipoOperacion,EntidadTipo,EntidadNombreSnapshot,Lugar,FechaHoraSalidaProgramada,Estatus,RequiereEvidencia,CierraViaje,Observaciones,Activo,FechaCreacion,CreadoPor) VALUES(@ViajeID,1,N'Origen',N'Salida',N'Planta',N'NS QUELL',N'NS QUELL',@FechaSalida,N'Pendiente',0,0,N'Salida de planta.',1,SYSDATETIME(),@Usuario); SELECT CONVERT(int,SCOPE_IDENTITY());";
            await using (var cmd = new SqlCommand(sqlOrigen, cn, tx))
            {
                cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
                cmd.Parameters.Add("@FechaSalida", SqlDbType.DateTime2).Value = fechaHoraSalida;
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                paradaOrigenId = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
            }
            const string sqlEntrega = @"INSERT dbo.Logistica_ViajeParadas(ViajeID,Secuencia,TipoParada,TipoOperacion,EntidadTipo,EntidadID,EntidadNombreSnapshot,Lugar,Direccion,ReferenciaTipo,ReferenciaID,ReferenciaFolioSnapshot,FechaHoraLlegadaProgramada,Estatus,RequiereEvidencia,CierraViaje,Observaciones,Activo,FechaCreacion,CreadoPor) VALUES(@ViajeID,2,N'Entrega',N'Entrega PT',N'Cliente',@ClienteID,@Cliente,@Lugar,@Direccion,N'Embarque',@EmbarqueID,@Folio,@FechaEntrega,N'Pendiente',1,0,N'Entrega asociada automáticamente al embarque.',1,SYSDATETIME(),@Usuario); SELECT CONVERT(int,SCOPE_IDENTITY());";
            await using (var cmd = new SqlCommand(sqlEntrega, cn, tx))
            {
                cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
                cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = clienteId;
                cmd.Parameters.Add("@Cliente", SqlDbType.NVarChar, 200).Value = cliente;
                cmd.Parameters.Add("@Lugar", SqlDbType.NVarChar, 300).Value = lugarEntrega;
                cmd.Parameters.Add("@Direccion", SqlDbType.NVarChar, 600).Value = Db(string.IsNullOrWhiteSpace(direccion) ? null : direccion);
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                cmd.Parameters.Add("@Folio", SqlDbType.NVarChar, 120).Value = folioEmbarque;
                cmd.Parameters.Add("@FechaEntrega", SqlDbType.DateTime2).Value = Db(fechaHoraEntrega);
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                paradaEntregaId = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
            }
            const string sqlRetorno = @"INSERT dbo.Logistica_ViajeParadas(ViajeID,Secuencia,TipoParada,TipoOperacion,EntidadTipo,EntidadNombreSnapshot,Lugar,Estatus,RequiereEvidencia,CierraViaje,Observaciones,Activo,FechaCreacion,CreadoPor) VALUES(@ViajeID,3,N'Retorno',N'Retorno',N'Planta',N'NS QUELL',N'NS QUELL',N'Pendiente',0,1,N'Retorno a planta.',1,SYSDATETIME(),@Usuario); SELECT CONVERT(int,SCOPE_IDENTITY());";
            await using (var cmd = new SqlCommand(sqlRetorno, cn, tx))
            {
                cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                paradaCierreId = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
            }
            await InsertarHistorialParadaViajeAsync(cn, tx, paradaOrigenId.Value, "PARADA_CREADA", null, "Pendiente", "Origen NS QUELL creado automáticamente.", cancellationToken);
            await InsertarHistorialParadaViajeAsync(cn, tx, paradaEntregaId.Value, "PARADA_CREADA_DESDE_EMBARQUE", null, "Pendiente", $"Entrega creada para {folioEmbarque} - {cliente}.", cancellationToken);
            await InsertarHistorialParadaViajeAsync(cn, tx, paradaCierreId.Value, "PARADA_CREADA", null, "Pendiente", "Retorno NS QUELL creado automáticamente.", cancellationToken);
        }
        else
        {
            if (!paradaOrigenId.HasValue) throw new InvalidOperationException($"El viaje VIA-{viajeId:000000} no tiene una parada Origen. Revisa la migración de múltiples paradas.");
            if (!paradaEntregaId.HasValue)
            {
                const string sqlLibre = @"
SELECT TOP(1) p.ViajeParadaID
FROM dbo.Logistica_ViajeParadas p WITH(UPDLOCK,HOLDLOCK)
WHERE p.ViajeID=@ViajeID AND p.Activo=1 AND p.TipoParada=N'Entrega'
AND (p.ReferenciaID IS NULL OR (p.ReferenciaTipo=N'Embarque' AND p.ReferenciaID=@EmbarqueID))
AND NOT EXISTS(SELECT 1 FROM dbo.Logistica_ViajeEmbarques ve WITH(UPDLOCK,HOLDLOCK) WHERE ve.ViajeParadaID=p.ViajeParadaID AND ve.Activo=1)
ORDER BY p.Secuencia,p.ViajeParadaID;";
                await using var cmd = new SqlCommand(sqlLibre, cn, tx);
                cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                var valor = await cmd.ExecuteScalarAsync(cancellationToken);
                if (valor != null && valor != DBNull.Value) paradaEntregaId = Convert.ToInt32(valor);
            }
            if (!paradaEntregaId.HasValue)
            {
                var nuevaSecuencia = maxSecuencia + 1;
                if (paradaCierreId.HasValue && secuenciaCierre.HasValue)
                {
                    nuevaSecuencia = secuenciaCierre.Value;
                    const string sqlMoverCierre = @"UPDATE dbo.Logistica_ViajeParadas SET Secuencia=@NuevaSecuencia,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario WHERE ViajeParadaID=@ParadaID AND ViajeID=@ViajeID AND Activo=1;";
                    await using var cmd = new SqlCommand(sqlMoverCierre, cn, tx);
                    cmd.Parameters.Add("@NuevaSecuencia", SqlDbType.Int).Value = maxSecuencia + 1;
                    cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                    cmd.Parameters.Add("@ParadaID", SqlDbType.Int).Value = paradaCierreId.Value;
                    cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                }
                const string sqlNuevaEntrega = @"INSERT dbo.Logistica_ViajeParadas(ViajeID,Secuencia,TipoParada,TipoOperacion,EntidadTipo,EntidadID,EntidadNombreSnapshot,Lugar,Direccion,ReferenciaTipo,ReferenciaID,ReferenciaFolioSnapshot,FechaHoraLlegadaProgramada,Estatus,RequiereEvidencia,CierraViaje,Observaciones,Activo,FechaCreacion,CreadoPor) VALUES(@ViajeID,@Secuencia,N'Entrega',N'Entrega PT',N'Cliente',@ClienteID,@Cliente,@Lugar,@Direccion,N'Embarque',@EmbarqueID,@Folio,@FechaEntrega,N'Pendiente',1,0,N'Entrega agregada automáticamente desde embarque.',1,SYSDATETIME(),@Usuario); SELECT CONVERT(int,SCOPE_IDENTITY());";
                await using var cmdInsert = new SqlCommand(sqlNuevaEntrega, cn, tx);
                cmdInsert.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
                cmdInsert.Parameters.Add("@Secuencia", SqlDbType.Int).Value = nuevaSecuencia;
                cmdInsert.Parameters.Add("@ClienteID", SqlDbType.Int).Value = clienteId;
                cmdInsert.Parameters.Add("@Cliente", SqlDbType.NVarChar, 200).Value = cliente;
                cmdInsert.Parameters.Add("@Lugar", SqlDbType.NVarChar, 300).Value = lugarEntrega;
                cmdInsert.Parameters.Add("@Direccion", SqlDbType.NVarChar, 600).Value = Db(string.IsNullOrWhiteSpace(direccion) ? null : direccion);
                cmdInsert.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
                cmdInsert.Parameters.Add("@Folio", SqlDbType.NVarChar, 120).Value = folioEmbarque;
                cmdInsert.Parameters.Add("@FechaEntrega", SqlDbType.DateTime2).Value = Db(fechaHoraEntrega);
                cmdInsert.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                paradaEntregaId = Convert.ToInt32(await cmdInsert.ExecuteScalarAsync(cancellationToken));
                await InsertarHistorialParadaViajeAsync(cn, tx, paradaEntregaId.Value, "PARADA_CREADA_DESDE_EMBARQUE", null, "Pendiente", $"Entrega agregada para {folioEmbarque} - {cliente}.", cancellationToken);
            }
            if (!paradaCierreId.HasValue)
            {
                const string sqlMax = @"SELECT ISNULL(MAX(Secuencia),0) FROM dbo.Logistica_ViajeParadas WITH(UPDLOCK,HOLDLOCK) WHERE ViajeID=@ViajeID AND Activo=1;";
                int secuenciaRetorno;
                await using (var cmdMax = new SqlCommand(sqlMax, cn, tx))
                {
                    cmdMax.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
                    secuenciaRetorno = Convert.ToInt32(await cmdMax.ExecuteScalarAsync(cancellationToken)) + 1;
                }
                const string sqlRetorno = @"INSERT dbo.Logistica_ViajeParadas(ViajeID,Secuencia,TipoParada,TipoOperacion,EntidadTipo,EntidadNombreSnapshot,Lugar,Estatus,RequiereEvidencia,CierraViaje,Observaciones,Activo,FechaCreacion,CreadoPor) VALUES(@ViajeID,@Secuencia,N'Retorno',N'Retorno',N'Planta',N'NS QUELL',N'NS QUELL',N'Pendiente',0,1,N'Retorno a planta.',1,SYSDATETIME(),@Usuario); SELECT CONVERT(int,SCOPE_IDENTITY());";
                await using var cmdRetorno = new SqlCommand(sqlRetorno, cn, tx);
                cmdRetorno.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
                cmdRetorno.Parameters.Add("@Secuencia", SqlDbType.Int).Value = secuenciaRetorno;
                cmdRetorno.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                paradaCierreId = Convert.ToInt32(await cmdRetorno.ExecuteScalarAsync(cancellationToken));
                await InsertarHistorialParadaViajeAsync(cn, tx, paradaCierreId.Value, "PARADA_CREADA", null, "Pendiente", "Retorno NS QUELL agregado automáticamente.", cancellationToken);
            }
        }
        const string sqlActualizarOrigen = @"UPDATE dbo.Logistica_ViajeParadas SET TipoParada=N'Origen',TipoOperacion=N'Salida',EntidadTipo=N'Planta',EntidadNombreSnapshot=N'NS QUELL',Lugar=N'NS QUELL',FechaHoraSalidaProgramada=@FechaSalida,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario WHERE ViajeParadaID=@ParadaID AND ViajeID=@ViajeID AND Activo=1;";
        await using (var cmd = new SqlCommand(sqlActualizarOrigen, cn, tx))
        {
            cmd.Parameters.Add("@FechaSalida", SqlDbType.DateTime2).Value = fechaHoraSalida;
            cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
            cmd.Parameters.Add("@ParadaID", SqlDbType.Int).Value = paradaOrigenId!.Value;
            cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        const string sqlActualizarEntrega = @"
UPDATE p SET TipoParada=N'Entrega',TipoOperacion=N'Entrega PT',EntidadTipo=N'Cliente',EntidadID=@ClienteID,EntidadNombreSnapshot=@Cliente,Lugar=@Lugar,Direccion=@Direccion,ReferenciaTipo=N'Embarque',ReferenciaID=@EmbarqueID,ReferenciaFolioSnapshot=@Folio,
FechaHoraLlegadaProgramada=CASE WHEN @FechaEntrega IS NULL THEN p.FechaHoraLlegadaProgramada WHEN ISNULL(v.EsMultiParada,0)=1 AND p.FechaHoraLlegadaProgramada IS NOT NULL THEN p.FechaHoraLlegadaProgramada ELSE @FechaEntrega END,
RequiereEvidencia=1,CierraViaje=0,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
FROM dbo.Logistica_ViajeParadas p INNER JOIN dbo.Logistica_Viajes v ON v.ViajeID=p.ViajeID
WHERE p.ViajeParadaID=@ParadaID AND p.ViajeID=@ViajeID AND p.Activo=1;";
        await using (var cmd = new SqlCommand(sqlActualizarEntrega, cn, tx))
        {
            cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = clienteId;
            cmd.Parameters.Add("@Cliente", SqlDbType.NVarChar, 200).Value = cliente;
            cmd.Parameters.Add("@Lugar", SqlDbType.NVarChar, 300).Value = lugarEntrega;
            cmd.Parameters.Add("@Direccion", SqlDbType.NVarChar, 600).Value = Db(string.IsNullOrWhiteSpace(direccion) ? null : direccion);
            cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
            cmd.Parameters.Add("@Folio", SqlDbType.NVarChar, 120).Value = folioEmbarque;
            cmd.Parameters.Add("@FechaEntrega", SqlDbType.DateTime2).Value = Db(fechaHoraEntrega);
            cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
            cmd.Parameters.Add("@ParadaID", SqlDbType.Int).Value = paradaEntregaId!.Value;
            cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        if (viajeEmbarqueId.HasValue)
        {
            const string sqlRelacion = @"UPDATE dbo.Logistica_ViajeEmbarques SET ViajeParadaID=@ViajeParadaID,Activo=1 WHERE ViajeEmbarqueID=@ViajeEmbarqueID AND ViajeID=@ViajeID AND EmbarqueID=@EmbarqueID;";
            await using var cmd = new SqlCommand(sqlRelacion, cn, tx);
            cmd.Parameters.Add("@ViajeParadaID", SqlDbType.Int).Value = paradaEntregaId.Value;
            cmd.Parameters.Add("@ViajeEmbarqueID", SqlDbType.Int).Value = viajeEmbarqueId.Value;
            cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
            cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        else
        {
            const string sqlRelacion = @"INSERT dbo.Logistica_ViajeEmbarques(ViajeID,EmbarqueID,OrdenEntrega,ViajeParadaID,Activo) VALUES(@ViajeID,@EmbarqueID,1,@ViajeParadaID,1);";
            await using var cmd = new SqlCommand(sqlRelacion, cn, tx);
            cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
            cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
            cmd.Parameters.Add("@ViajeParadaID", SqlDbType.Int).Value = paradaEntregaId.Value;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        const string sqlOrdenar = @"
;WITH Entregas AS
(
    SELECT ViajeParadaID,ROW_NUMBER() OVER(ORDER BY Secuencia,ViajeParadaID) OrdenEntrega
    FROM dbo.Logistica_ViajeParadas
    WHERE ViajeID=@ViajeID AND Activo=1 AND TipoParada=N'Entrega'
)
UPDATE ve SET OrdenEntrega=e.OrdenEntrega
FROM dbo.Logistica_ViajeEmbarques ve
INNER JOIN Entregas e ON e.ViajeParadaID=ve.ViajeParadaID
WHERE ve.ViajeID=@ViajeID AND ve.Activo=1;
UPDATE dbo.Logistica_Viajes
SET EsMultiParada=CASE WHEN (SELECT COUNT_BIG(*) FROM dbo.Logistica_ViajeParadas WHERE ViajeID=@ViajeID AND Activo=1)>3 THEN 1 ELSE EsMultiParada END,
FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
WHERE ViajeID=@ViajeID AND Activo=1;";
        await using (var cmd = new SqlCommand(sqlOrdenar, cn, tx))
        {
            cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
            cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        return paradaEntregaId.Value;
    }
    private async Task InsertarHistorialParadaViajeAsync(SqlConnection cn, SqlTransaction tx, int viajeParadaId, string evento, string? anterior, string? nuevo, string? observaciones, CancellationToken cancellationToken)
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

    private async Task<int> AsegurarViajeEntregaNsAsync(SqlConnection cn, SqlTransaction tx, int embarqueId, DateTime fechaProgramada, TimeSpan horaProgramada, int rutaId, int unidadId, int choferUsuarioId, string choferNombre, CancellationToken cancellationToken)
    {
        const string sqlEmbarque = @"
SELECT e.ClienteID,ISNULL(e.Folio,N'') Folio,ISNULL(e.ClienteNombreSnapshot,N'') Cliente,ISNULL(e.Destino,N'') Destino,ISNULL(e.DireccionEntrega,N'') DireccionEntrega,e.FechaEntregaProgramada,e.HoraEntregaProgramada
FROM dbo.Logistica_Embarques e WITH(UPDLOCK,HOLDLOCK)
WHERE e.EmbarqueID=@EmbarqueID AND e.Activo=1;";
        int clienteId;
        string folioEmbarque, cliente, destino, direccion;
        DateTime? fechaEntregaProgramada;
        TimeSpan? horaEntregaProgramada;
        await using (var cmd = new SqlCommand(sqlEmbarque, cn, tx))
        {
            cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await rd.ReadAsync(cancellationToken)) throw new InvalidOperationException("El embarque ya no existe.");
            clienteId = Entero(rd, "ClienteID");
            folioEmbarque = Texto(rd, "Folio");
            cliente = Texto(rd, "Cliente");
            destino = Texto(rd, "Destino");
            direccion = Texto(rd, "DireccionEntrega");
            fechaEntregaProgramada = Fecha(rd, "FechaEntregaProgramada");
            horaEntregaProgramada = Hora(rd, "HoraEntregaProgramada");
        }
        const string sqlRelacionado = @"
SELECT TOP(1) v.ViajeID,ISNULL(v.Estatus,N'') Estatus
FROM dbo.Logistica_ViajeEmbarques ve WITH(UPDLOCK,HOLDLOCK)
INNER JOIN dbo.Logistica_Viajes v WITH(UPDLOCK,HOLDLOCK) ON v.ViajeID=ve.ViajeID
WHERE ve.EmbarqueID=@EmbarqueID AND ve.Activo=1 AND v.Activo=1
ORDER BY CASE WHEN v.Estatus IN(N'Programado',N'En curso') THEN 0 ELSE 1 END,ve.ViajeEmbarqueID DESC;";
        int? viajeId = null;
        string estatusViaje = string.Empty;
        await using (var cmd = new SqlCommand(sqlRelacionado, cn, tx))
        {
            cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await rd.ReadAsync(cancellationToken))
            {
                viajeId = Entero(rd, "ViajeID");
                estatusViaje = Texto(rd, "Estatus");
            }
        }
        var destinoCompleto = string.IsNullOrWhiteSpace(direccion) ? destino : $"{destino} - {direccion}";
        var motivo = $"Entrega del embarque {folioEmbarque} para {cliente}.";
        if (viajeId.HasValue)
        {
            if (estatusViaje != "Programado") throw new InvalidOperationException($"El embarque ya está relacionado con el viaje VIA-{viajeId.Value:000000} en estatus {estatusViaje}; ya no puede cambiarse su programación o recursos.");
            await ValidarDisponibilidadProgramacionAsync(cn, tx, fechaProgramada.Date, horaProgramada, unidadId, choferUsuarioId, embarqueId, viajeId.Value, cancellationToken);
            const string sqlUpdate = @"
UPDATE dbo.Logistica_Viajes
SET TipoViaje=CASE WHEN ISNULL(EsMultiParada,0)=1 THEN N'Ruta multipropósito' ELSE N'Entrega PT' END,
TipoTransporte=N'Interno',
Origen=N'NS QUELL',
Destino=CASE WHEN ISNULL(EsMultiParada,0)=1 THEN Destino ELSE @Destino END,
Motivo=CASE WHEN ISNULL(EsMultiParada,0)=1 AND NULLIF(LTRIM(RTRIM(ISNULL(Motivo,N''))),N'') IS NOT NULL THEN Motivo ELSE @Motivo END,
FechaProgramada=@FechaProgramada,
HoraSalidaProgramada=@HoraSalidaProgramada,
RutaID=@RutaID,
UnidadID=@UnidadID,
OperadorUsuarioID=@ChoferUsuarioID,
OperadorNombreSnapshot=@ChoferNombre,
OperadorTexto=@ChoferNombre,
TransportistaExterno=NULL,
UnidadExterna=NULL,
PlacasExternas=NULL,
ChoferExterno=NULL,
FechaModificacion=SYSDATETIME(),
ActualizadoPor=@Usuario
WHERE ViajeID=@ViajeID AND Activo=1 AND Estatus=N'Programado';
SELECT @@ROWCOUNT;";
            await using (var cmd = new SqlCommand(sqlUpdate, cn, tx))
            {
                cmd.Parameters.Add("@Destino", SqlDbType.NVarChar, 300).Value = destinoCompleto;
                cmd.Parameters.Add("@Motivo", SqlDbType.NVarChar, 500).Value = motivo;
                cmd.Parameters.Add("@FechaProgramada", SqlDbType.Date).Value = fechaProgramada.Date;
                cmd.Parameters.Add("@HoraSalidaProgramada", SqlDbType.Time).Value = horaProgramada;
                cmd.Parameters.Add("@RutaID", SqlDbType.Int).Value = rutaId;
                cmd.Parameters.Add("@UnidadID", SqlDbType.Int).Value = unidadId;
                cmd.Parameters.Add("@ChoferUsuarioID", SqlDbType.Int).Value = choferUsuarioId;
                cmd.Parameters.Add("@ChoferNombre", SqlDbType.NVarChar, 200).Value = choferNombre;
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId.Value;
                if (Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) != 1) throw new InvalidOperationException("No fue posible sincronizar el viaje de Entrega.");
            }
            var paradaEntregaId = await AsegurarParadasEntregaViajeAsync(cn, tx, viajeId.Value, embarqueId, folioEmbarque, clienteId, cliente, destino, direccion, fechaProgramada, horaProgramada, fechaEntregaProgramada, horaEntregaProgramada, cancellationToken);
            await InsertarHistorialViajeAsync(cn, tx, viajeId.Value, "SINCRONIZADO_DESDE_EMBARQUE", "Programado", "Programado", $"Viaje sincronizado con {folioEmbarque}. Programación: {fechaProgramada:dd/MM/yyyy} {horaProgramada:hh\\:mm}. Chofer: {choferNombre}. Parada de entrega ID {paradaEntregaId}.", cancellationToken);
            return viajeId.Value;
        }
        await ValidarDisponibilidadProgramacionAsync(cn, tx, fechaProgramada.Date, horaProgramada, unidadId, choferUsuarioId, embarqueId, null, cancellationToken);
        const string sqlInsert = @"
INSERT dbo.Logistica_Viajes
(Folio,TipoViaje,TipoTransporte,Origen,Destino,Motivo,FechaProgramada,HoraSalidaProgramada,RutaID,UnidadID,OperadorUsuarioID,OperadorNombreSnapshot,OperadorTexto,TransportistaExterno,UnidadExterna,PlacasExternas,ChoferExterno,Estatus,TieneIncidencia,EsMultiParada,Observaciones,ResponsableUsuarioID,ResponsableNombreSnapshot,FechaCreacion,CreadoPor,Activo)
VALUES
(NULL,N'Entrega PT',N'Interno',N'NS QUELL',@Destino,@Motivo,@FechaProgramada,@HoraSalidaProgramada,@RutaID,@UnidadID,@ChoferUsuarioID,@ChoferNombre,@ChoferNombre,NULL,NULL,NULL,NULL,N'Programado',0,0,@Observaciones,@UsuarioID,@Usuario,SYSDATETIME(),@Usuario,1);
SELECT CONVERT(int,SCOPE_IDENTITY());";
        await using (var cmd = new SqlCommand(sqlInsert, cn, tx))
        {
            cmd.Parameters.Add("@Destino", SqlDbType.NVarChar, 300).Value = destinoCompleto;
            cmd.Parameters.Add("@Motivo", SqlDbType.NVarChar, 500).Value = motivo;
            cmd.Parameters.Add("@FechaProgramada", SqlDbType.Date).Value = fechaProgramada.Date;
            cmd.Parameters.Add("@HoraSalidaProgramada", SqlDbType.Time).Value = horaProgramada;
            cmd.Parameters.Add("@RutaID", SqlDbType.Int).Value = rutaId;
            cmd.Parameters.Add("@UnidadID", SqlDbType.Int).Value = unidadId;
            cmd.Parameters.Add("@ChoferUsuarioID", SqlDbType.Int).Value = choferUsuarioId;
            cmd.Parameters.Add("@ChoferNombre", SqlDbType.NVarChar, 200).Value = choferNombre;
            cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1200).Value = $"Viaje generado automáticamente desde Centro Operativo para el embarque {folioEmbarque}.";
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = Db(UsuarioID);
            cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
            viajeId = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
        }
        var folioViaje = $"VIA-{DateTime.Today:yyyy}-{viajeId.Value:000000}";
        await using (var cmd = new SqlCommand("UPDATE dbo.Logistica_Viajes SET Folio=@Folio WHERE ViajeID=@ViajeID;", cn, tx))
        {
            cmd.Parameters.Add("@Folio", SqlDbType.NVarChar, 50).Value = folioViaje;
            cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId.Value;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        var paradaId = await AsegurarParadasEntregaViajeAsync(cn, tx, viajeId.Value, embarqueId, folioEmbarque, clienteId, cliente, destino, direccion, fechaProgramada, horaProgramada, fechaEntregaProgramada, horaEntregaProgramada, cancellationToken);
        await InsertarHistorialViajeAsync(cn, tx, viajeId.Value, "VIAJE_CREADO_DESDE_EMBARQUE", null, "Programado", $"{folioViaje} creado automáticamente para la entrega {folioEmbarque}. Parada de entrega ID {paradaId}.", cancellationToken);
        await InsertarHistorialAsync(cn, tx, embarqueId, "VIAJE_ENTREGA_CREADO", null, "Programado", $"Viaje {folioViaje} relacionado automáticamente con el embarque en la parada {paradaId}.", cancellationToken);
        return viajeId.Value;
    }

    private static object EventoJson(LogisticaOperacionEventoVm e)
    {
        return new
        {
            e.EventoKey,
            e.TipoEvento,
            e.EventoID,
            e.ReleaseDetalleID,
            e.ParteID,
            e.EmbarqueID,
            e.ListaCargaProgramacionID,
            e.ClienteID,
            e.Cliente,
            e.Folio,
            e.FolioRelease,
            e.NumeroOF,
            e.NumeroParte,
            e.DescripcionParte,
            fecha = e.FechaVisual.ToString("yyyy-MM-dd"),
            fechaProgramada = e.FechaProgramada.ToString("yyyy-MM-dd"),
            hora = e.HoraProgramada?.ToString(@"hh\:mm"),
            e.HoraTexto,
            e.FechaHoraTexto,
            fechaRequerida = e.FechaRequeridaReal?.ToString("yyyy-MM-dd"),
            e.FechaRequeridaTexto,
            e.DiasAtraso,
            e.AtrasoTexto,
            e.Estatus,
            e.Criticidad,
            e.TipoOperacion,
            e.FormaEnvio,
            e.FormaEnvioTexto,
            e.ModalidadEnvio,
            e.Destino,
            e.CantidadRequerida,
            e.CantidadPendienteProgramar,
            e.CantidadProgramada,
            e.CantidadGenerada,
            e.CantidadEnviada,
            e.TotalPartidas,
            e.TotalCajas,
            e.TotalPiezas,
            e.TotalPiezasPreparadas,
            e.TotalPiezasDespachadas,
            e.PorcentajePreparacion,
            e.PorcentajeDespachado,
            e.TieneIncidencia,
            e.IncidenciasAbiertas,
            e.IncidenciasCriticas,
            e.Evidencias,
            e.NumeroParteResumen,
            e.DescripcionResumen,
            e.RowVersion,
            e.EsRequerimiento,
            e.EsProgramacion,
            e.EsEmbarque,
            e.EsExpeditado,
            e.SinProgramar,
            e.ProgramadoNoGenerado,
            e.EmbarquePendienteSalida,
            e.PuedeArrastrar,
            e.RequiereConfirmacionMover,
            e.Bloqueado,
            e.FaltaDefinirSalida,
            e.Icono,
            e.ClaseEstatus,
            e.ClaseEvento
        };
    }
    private async Task<int> ObtenerOCrearSemanaIdAsync(SqlConnection cn, SqlTransaction tx, DateTime fecha, CancellationToken cancellationToken)
    {
        var inicio = InicioSemana(fecha);
        var fin = inicio.AddDays(5);
        var anio = ISOWeek.GetYear(inicio);
        var semana = ISOWeek.GetWeekOfYear(inicio);
        const string sqlBuscar = @"
SELECT TOP(1) ListaCargaSemanaID,ISNULL(Estatus,N'Abierta') Estatus
FROM dbo.Logistica_ListaCargaSemanas WITH(UPDLOCK,HOLDLOCK)
WHERE Anio=@Anio AND NumeroSemana=@Semana AND Activo=1;";
        await using (var cmd = new SqlCommand(sqlBuscar, cn, tx))
        {
            cmd.Parameters.Add("@Anio", SqlDbType.Int).Value = anio;
            cmd.Parameters.Add("@Semana", SqlDbType.Int).Value = semana;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await rd.ReadAsync(cancellationToken))
            {
                if (Texto(rd, "Estatus").Equals("Cerrada", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"La semana W{semana:00} {anio} está cerrada.");
                return Entero(rd, "ListaCargaSemanaID");
            }
        }
        const string sqlInsert = @"
INSERT dbo.Logistica_ListaCargaSemanas(Anio,NumeroSemana,FechaInicio,FechaFin,Estatus,Observaciones,Activo,FechaCreacion,CreadoPor)
VALUES(@Anio,@Semana,@Inicio,@Fin,N'Abierta',NULL,1,SYSDATETIME(),@Usuario);
SELECT CONVERT(int,SCOPE_IDENTITY());";
        await using (var cmd = new SqlCommand(sqlInsert, cn, tx))
        {
            cmd.Parameters.Add("@Anio", SqlDbType.Int).Value = anio;
            cmd.Parameters.Add("@Semana", SqlDbType.Int).Value = semana;
            cmd.Parameters.Add("@Inicio", SqlDbType.Date).Value = inicio;
            cmd.Parameters.Add("@Fin", SqlDbType.Date).Value = fin;
            cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
        }
    }

    // AGREGAR: resolver filtro Dia/Semana/Mes.
    private static (string Vista, DateTime Referencia, int Anio, int Semana, DateTime Inicio, DateTime Fin, DateTime Anterior, DateTime Siguiente) ResolverPeriodo(string? vista, DateTime? fecha, int? anio, int? semana)
    {
        var vistaFinal = NormalizarVista(vista);
        DateTime referencia;
        if (fecha.HasValue) referencia = fecha.Value.Date;
        else if (anio.HasValue && semana.HasValue)
        {
            try { referencia = ISOWeek.ToDateTime(anio.Value, semana.Value, DayOfWeek.Monday).Date; }
            catch { referencia = DateTime.Today; }
        }
        else referencia = DateTime.Today;
        DateTime inicio, fin, anterior, siguiente;
        if (vistaFinal == "Dia")
        {
            inicio = referencia; fin = referencia; anterior = referencia.AddDays(-1); siguiente = referencia.AddDays(1);
        }
        else if (vistaFinal == "Mes")
        {
            inicio = new DateTime(referencia.Year, referencia.Month, 1); fin = inicio.AddMonths(1).AddDays(-1); anterior = inicio.AddMonths(-1); siguiente = inicio.AddMonths(1);
        }
        else
        {
            inicio = InicioSemana(referencia); fin = inicio.AddDays(5); anterior = inicio.AddDays(-7); siguiente = inicio.AddDays(7);
        }
        var anioIso = ISOWeek.GetYear(InicioSemana(referencia));
        var semanaIso = ISOWeek.GetWeekOfYear(InicioSemana(referencia));
        return (vistaFinal, referencia, anioIso, semanaIso, inicio.Date, fin.Date, anterior.Date, siguiente.Date);
    }

    // AGREGAR
    private static string NormalizarVista(string? vista)
    {
        vista = (vista ?? string.Empty).Trim();
        if (vista.Equals("Dia", StringComparison.OrdinalIgnoreCase) || vista.Equals("Día", StringComparison.OrdinalIgnoreCase)) return "Dia";
        if (vista.Equals("Mes", StringComparison.OrdinalIgnoreCase)) return "Mes";
        return "Semana";
    }

    // AGREGAR
    private static DateTime InicioSemana(DateTime fecha)
    {
        var diferencia = ((int)fecha.DayOfWeek + 6) % 7;
        return fecha.Date.AddDays(-diferencia);
    }

    // AGREGAR: si un pendiente venció, lo muestra en el día operativo actual; si se consulta otro periodo, lo arrastra al primer día visible.
    private static DateTime ResolverFechaVisualPendiente(DateTime fechaOriginal, DateTime fechaInicio, DateTime fechaFin)
    {
        fechaOriginal = fechaOriginal.Date;
        var hoy = DateTime.Today;
        if (fechaOriginal >= fechaInicio.Date && fechaOriginal <= fechaFin.Date)
        {
            if (fechaOriginal < hoy && hoy >= fechaInicio.Date && hoy <= fechaFin.Date) return hoy;
            return fechaOriginal;
        }
        if (fechaOriginal < fechaInicio.Date)
        {
            if (hoy >= fechaInicio.Date && hoy <= fechaFin.Date) return hoy;
            return fechaInicio.Date;
        }
        return fechaOriginal;
    }


    private static (
        int Anio,
        int Semana,
        DateTime Inicio,
        DateTime Fin)
        ResolverSemana(
            int? anio,
            int? semana)
    {
        var hoy =
            DateTime.Today;

        var anioFinal =
            anio.HasValue
            &&
            anio.Value >= 2020
            &&
            anio.Value <= 2100
                ? anio.Value
                : ISOWeek.GetYear(
                    hoy);

        var semanaFinal =
            semana.HasValue
            &&
            semana.Value >= 1
            &&
            semana.Value <= 53
                ? semana.Value
                : ISOWeek.GetWeekOfYear(
                    hoy);

        DateTime inicio;

        try
        {
            inicio =
                ISOWeek.ToDateTime(
                    anioFinal,
                    semanaFinal,
                    DayOfWeek.Monday);
        }
        catch
        {
            anioFinal =
                ISOWeek.GetYear(
                    hoy);

            semanaFinal =
                ISOWeek.GetWeekOfYear(
                    hoy);

            inicio =
                ISOWeek.ToDateTime(
                    anioFinal,
                    semanaFinal,
                    DayOfWeek.Monday);
        }

        return (
            anioFinal,
            semanaFinal,
            inicio.Date,
            inicio.AddDays(5).Date);
    }

    private async Task<List<(int EmbarqueID, string Folio)>> ValidarYObtenerEmbarquesViajeListosParaSalidaAsync(SqlConnection cn, SqlTransaction tx, int viajeId, CancellationToken cancellationToken)
    {
        var datos = new List<(int EmbarqueID, string Folio, string Estatus, string TipoOperacion, string FormaEnvio, string ModalidadEnvio, bool? PasaAduana)>();
        const string sql = @"
SELECT e.EmbarqueID,ISNULL(e.Folio,N'') Folio,ISNULL(e.Estatus,N'') Estatus,ISNULL(e.TipoOperacion,N'Pendiente') TipoOperacion,ISNULL(e.FormaEnvio,N'Pendiente') FormaEnvio,ISNULL(e.ModalidadEnvio,N'') ModalidadEnvio,e.PasaAduana
FROM dbo.Logistica_ViajeEmbarques ve WITH(UPDLOCK,HOLDLOCK)
INNER JOIN dbo.Logistica_Embarques e WITH(UPDLOCK,HOLDLOCK) ON e.EmbarqueID=ve.EmbarqueID AND e.Activo=1
WHERE ve.ViajeID=@ViajeID AND ve.Activo=1
ORDER BY ISNULL(ve.OrdenEntrega,2147483647),ve.ViajeEmbarqueID;";
        await using (var cmd = new SqlCommand(sql, cn, tx))
        {
            cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await rd.ReadAsync(cancellationToken))
            {
                datos.Add((Entero(rd, "EmbarqueID"), Texto(rd, "Folio"), Texto(rd, "Estatus"), NormalizarTipoOperacionFlujo(Texto(rd, "TipoOperacion")), NormalizarFormaEnvio(Texto(rd, "FormaEnvio")), NormalizarModalidadEnvioFlujo(Texto(rd, "ModalidadEnvio")), rd.IsDBNull(rd.GetOrdinal("PasaAduana")) ? null : Convert.ToBoolean(rd["PasaAduana"])));
            }
        }
        if (datos.Count == 0) throw new InvalidOperationException($"El viaje VIA-{viajeId:000000} no tiene embarques activos relacionados.");
        foreach (var item in datos)
        {
            if (item.FormaEnvio != "Interno") throw new InvalidOperationException($"El embarque {item.Folio} pertenece al viaje pero su forma de envío ya no es Entrega interna.");
            if (item.Estatus == "Cargando") throw new InvalidOperationException($"El embarque {item.Folio} todavía tiene la carga física en proceso.");
            if (item.Estatus != "Cargado") throw new InvalidOperationException($"El embarque {item.Folio} debe estar Cargado antes de iniciar el viaje. Estado actual: {item.Estatus}.");
            await ValidarDocumentacionSalidaPlantaAsync(cn, tx, item.EmbarqueID, item.TipoOperacion, item.FormaEnvio, item.ModalidadEnvio, item.PasaAduana, cancellationToken);
            var resumen = await ObtenerResumenCargaFisicaAsync(cn, tx, item.EmbarqueID, cancellationToken);
            if (resumen.TotalSolicitado <= 0) throw new InvalidOperationException($"El embarque {item.Folio} no contiene piezas programadas.");
            if (resumen.CajasAsignadas <= 0) throw new InvalidOperationException($"El embarque {item.Folio} no contiene cajas asignadas.");
            if (resumen.PiezasReservadas > 0) throw new InvalidOperationException($"El embarque {item.Folio} todavía tiene {resumen.PiezasReservadas:N0} PZA reservadas sin confirmar mediante carga física.");
            if (resumen.PiezasCargadas != resumen.TotalSolicitado) throw new InvalidOperationException($"El embarque {item.Folio} tiene una carga incompleta. Programadas: {resumen.TotalSolicitado:N0} PZA. Cargadas: {resumen.PiezasCargadas:N0} PZA.");
            if (resumen.CajasCargadas != resumen.CajasAsignadas) throw new InvalidOperationException($"El embarque {item.Folio} tiene una carga incompleta. Cajas asignadas: {resumen.CajasAsignadas:N0}. Cajas cargadas: {resumen.CajasCargadas:N0}.");
            const string sqlIncidencias = @"SELECT COUNT_BIG(*) FROM dbo.Logistica_Incidencias WITH(UPDLOCK,HOLDLOCK) WHERE EmbarqueID=@EmbarqueID AND Activo=1 AND Estatus IN(N'Abierta',N'En seguimiento') AND Severidad=N'Crítica';";
            await using var cmd = new SqlCommand(sqlIncidencias, cn, tx);
            cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = item.EmbarqueID;
            if (Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken)) > 0) throw new InvalidOperationException($"El embarque {item.Folio} tiene incidencias críticas abiertas.");
        }
        return datos.Select(x => (x.EmbarqueID, x.Folio)).ToList();
    }

    private async Task<(int TotalDespachados, string? ReferenciaPrincipal)> DespacharEmbarquesViajeDesdeOperacionAsync(SqlConnection cn, SqlTransaction tx, int viajeId, int embarquePrincipalId, IReadOnlyCollection<(int EmbarqueID, string Folio)> embarques, DateTime fechaSalida, CancellationToken cancellationToken)
    {
        var totalDespachados = 0;
        string? referenciaPrincipal = null;
        foreach (var embarque in embarques)
        {
            string referenciaOperacion;
            bool yaDespachado;
            await using (var sp = new SqlCommand("dbo.usp_Logistica_DespacharEmbarque", cn, tx))
            {
                sp.CommandType = CommandType.StoredProcedure;
                sp.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarque.EmbarqueID;
                sp.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = Db(UsuarioID);
                sp.Parameters.Add("@UsuarioNombre", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                await using var rd = await sp.ExecuteReaderAsync(cancellationToken);
                if (!await rd.ReadAsync(cancellationToken)) throw new InvalidOperationException($"No fue posible despachar el embarque {embarque.Folio}.");
                referenciaOperacion = Texto(rd, "ReferenciaOperacion");
                yaDespachado = Booleano(rd, "YaDespachado");
            }
            const string sqlFecha = @"UPDATE dbo.Logistica_Embarques SET FechaSalida=COALESCE(FechaSalida,@FechaSalida),FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario WHERE EmbarqueID=@EmbarqueID AND Activo=1 AND Estatus IN(N'En ruta',N'Entregado');";
            await using (var cmd = new SqlCommand(sqlFecha, cn, tx))
            {
                cmd.Parameters.Add("@FechaSalida", SqlDbType.DateTime2).Value = fechaSalida;
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarque.EmbarqueID;
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            await ActualizarListaCargaSalidaOperacionAsync(cn, tx, embarque.EmbarqueID, cancellationToken);
            var descripcion = $"Salida física confirmada desde Centro Operativo al iniciar el viaje VIA-{viajeId:000000} por {UsuarioNombre} el {fechaSalida:dd/MM/yyyy HH:mm}.";
            if (!string.IsNullOrWhiteSpace(referenciaOperacion)) descripcion += $" Referencia PT: {referenciaOperacion}.";
            if (yaDespachado) descripcion += " El despacho de PT ya se encontraba registrado.";
            await InsertarHistorialAsync(cn, tx, embarque.EmbarqueID, "SALIDA_CONFIRMADA_CENTRO_OPERATIVO", "Cargado", "En ruta", descripcion, cancellationToken);
            if (embarque.EmbarqueID == embarquePrincipalId) referenciaPrincipal = referenciaOperacion;
            totalDespachados++;
        }
        return (totalDespachados, referenciaPrincipal);
    }

    private async Task InicializarParadasViajeDesdeOperacionAsync(SqlConnection cn, SqlTransaction tx, int viajeId, DateTime fechaSalida, CancellationToken cancellationToken)
    {
        const string sqlConteo = @"SELECT COUNT_BIG(*) FROM dbo.Logistica_ViajeParadas WITH(UPDLOCK,HOLDLOCK) WHERE ViajeID=@ViajeID AND Activo=1;";
        await using (var cmd = new SqlCommand(sqlConteo, cn, tx))
        {
            cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
            if (Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken)) == 0) return;
        }
        int? origenId = null;
        string estatusOrigen = string.Empty;
        const string sqlOrigen = @"SELECT TOP(1) ViajeParadaID,ISNULL(Estatus,N'') Estatus FROM dbo.Logistica_ViajeParadas WITH(UPDLOCK,HOLDLOCK) WHERE ViajeID=@ViajeID AND Activo=1 AND TipoParada=N'Origen' ORDER BY Secuencia,ViajeParadaID;";
        await using (var cmd = new SqlCommand(sqlOrigen, cn, tx))
        {
            cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await rd.ReadAsync(cancellationToken))
            {
                origenId = Entero(rd, "ViajeParadaID");
                estatusOrigen = Texto(rd, "Estatus");
            }
        }
        if (!origenId.HasValue) throw new InvalidOperationException($"El viaje VIA-{viajeId:000000} no tiene una parada Origen.");
        if (estatusOrigen is "Omitida" or "Cancelada") throw new InvalidOperationException("La parada Origen no se encuentra disponible para iniciar.");
        if (estatusOrigen != "Completada")
        {
            const string sqlUpdateOrigen = @"UPDATE dbo.Logistica_ViajeParadas SET Estatus=N'Completada',FechaLlegadaReal=COALESCE(FechaLlegadaReal,@Fecha),FechaSalidaReal=@Fecha,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario WHERE ViajeParadaID=@ParadaID AND ViajeID=@ViajeID AND Activo=1 AND Estatus NOT IN(N'Completada',N'Omitida',N'Cancelada');SELECT @@ROWCOUNT;";
            await using var cmd = new SqlCommand(sqlUpdateOrigen, cn, tx);
            cmd.Parameters.Add("@Fecha", SqlDbType.DateTime2).Value = fechaSalida;
            cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
            cmd.Parameters.Add("@ParadaID", SqlDbType.Int).Value = origenId.Value;
            cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
            if (Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) != 1) throw new InvalidOperationException("No fue posible completar la parada Origen.");
            await InsertarHistorialParadaViajeAsync(cn, tx, origenId.Value, "SALIDA_ORIGEN_LOGISTICA", estatusOrigen, "Completada", $"Salida iniciada desde Centro Operativo el {fechaSalida:dd/MM/yyyy HH:mm}.", cancellationToken);
        }
        const string sqlActiva = @"SELECT TOP(1) ViajeParadaID FROM dbo.Logistica_ViajeParadas WITH(UPDLOCK,HOLDLOCK) WHERE ViajeID=@ViajeID AND Activo=1 AND Estatus IN(N'En camino',N'En sitio') ORDER BY Secuencia,ViajeParadaID;";
        await using (var cmd = new SqlCommand(sqlActiva, cn, tx))
        {
            cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
            if (await cmd.ExecuteScalarAsync(cancellationToken) is not null) return;
        }
        int? siguienteId = null;
        string lugar = string.Empty;
        const string sqlSiguiente = @"SELECT TOP(1) ViajeParadaID,ISNULL(Lugar,N'') Lugar FROM dbo.Logistica_ViajeParadas WITH(UPDLOCK,HOLDLOCK) WHERE ViajeID=@ViajeID AND Activo=1 AND TipoParada<>N'Origen' AND Estatus=N'Pendiente' ORDER BY Secuencia,ViajeParadaID;";
        await using (var cmd = new SqlCommand(sqlSiguiente, cn, tx))
        {
            cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await rd.ReadAsync(cancellationToken))
            {
                siguienteId = Entero(rd, "ViajeParadaID");
                lugar = Texto(rd, "Lugar");
            }
        }
        if (!siguienteId.HasValue) return;
        const string sqlUpdateSiguiente = @"UPDATE dbo.Logistica_ViajeParadas SET Estatus=N'En camino',FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario WHERE ViajeParadaID=@ParadaID AND ViajeID=@ViajeID AND Activo=1 AND Estatus=N'Pendiente';SELECT @@ROWCOUNT;";
        await using (var cmd = new SqlCommand(sqlUpdateSiguiente, cn, tx))
        {
            cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
            cmd.Parameters.Add("@ParadaID", SqlDbType.Int).Value = siguienteId.Value;
            cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
            if (Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) == 1) await InsertarHistorialParadaViajeAsync(cn, tx, siguienteId.Value, "EN_CAMINO", "Pendiente", "En camino", $"Viaje iniciado desde Centro Operativo. Siguiente destino: {lugar}.", cancellationToken);
        }
    }
    private static (bool Ok, string Mensaje) ValidarEvidencia(IFormFile archivo)
    {
        const long maximo = 10 * 1024 * 1024;
        if (archivo == null || archivo.Length <= 0) return (false, "El archivo está vacío.");
        if (archivo.Length > maximo) return (false, $"El archivo {Path.GetFileName(archivo.FileName)} excede el máximo de 10 MB.");

        var nombre = Path.GetFileName(archivo.FileName);
        var extension = Path.GetExtension(nombre).ToLowerInvariant();
        var extensiones = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",".jpeg",".png",".webp",".heic",".heif",".pdf"
    };

        if (!extensiones.Contains(extension))
            return (false, $"{nombre}: solo se permiten JPG, JPEG, PNG, WEBP, HEIC, HEIF o PDF.");

        var contenido = archivo.ContentType?.Trim() ?? string.Empty;

        if (extension == ".pdf")
        {
            if (!contenido.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
                return (false, $"{nombre}: el tipo de contenido no corresponde a un PDF.");
            return (true, string.Empty);
        }

        if (!contenido.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return (false, $"{nombre}: el archivo seleccionado no fue reconocido como imagen.");

        return (true, string.Empty);
    }

    private async Task<(int CajasSeleccionadas, long PiezasSeleccionadas, long PiezasFaltantes, List<string> Etiquetas)> PrepararCajasCargaRapidaAsync(SqlConnection cn, SqlTransaction tx, int embarqueId, int clienteId, CancellationToken cancellationToken)
    {
        const string sqlDetalles = @"
SELECT d.ParteID,SUM(CONVERT(bigint,d.CantidadSolicitada)) Requerido
FROM dbo.Logistica_EmbarqueDetalle d WITH(UPDLOCK,HOLDLOCK)
WHERE d.EmbarqueID=@EmbarqueID AND d.Activo=1 AND d.CantidadSolicitada>0
GROUP BY d.ParteID;";
        var requeridos = new Dictionary<int, long>();
        await using (var cmd = new SqlCommand(sqlDetalles, cn, tx))
        {
            cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await rd.ReadAsync(cancellationToken)) requeridos[Entero(rd, "ParteID")] = EnteroLargo(rd, "Requerido");
        }
        if (requeridos.Count == 0) throw new InvalidOperationException("El embarque no contiene partidas válidas para realizar carga rápida.");
        var candidatasCompletas = await ObtenerCajasCandidatasCargaAsync(cn, tx, embarqueId, clienteId, cancellationToken);
        var seleccionIds = new List<int>();
        foreach (var requerido in requeridos.OrderBy(x => x.Key))
        {
            var cajasParte = candidatasCompletas
                .Where(x => x.ParteID == requerido.Key)
                .Select(x => (x.CajaID, x.ParteID, x.SolicitudProduccionID, x.NumeroOF, x.Etiqueta, x.NumeroCaja, x.Lote, x.FechaEntrada, x.Cantidad))
                .ToList();
            var seleccionadas = SeleccionarCajasHastaLimiteCargaRapida(cajasParte, requerido.Value);
            seleccionIds.AddRange(seleccionadas.Select(x => x.CajaID));
        }
        if (seleccionIds.Count == 0) throw new InvalidOperationException("No existe ninguna caja completa disponible que pueda cargarse sin exceder las cantidades programadas.");
        var seleccion = await AplicarSeleccionManualCajasAsync(cn, tx, embarqueId, clienteId, seleccionIds, cancellationToken);
        var totalSolicitado = requeridos.Sum(x => x.Value);
        return (seleccion.CajasSeleccionadas, seleccion.PiezasSeleccionadas, Math.Max(0, totalSolicitado - seleccion.PiezasSeleccionadas), seleccion.Etiquetas);
    }
    private static List<(int CajaID, int ParteID, int? SolicitudProduccionID, string NumeroOF, string Etiqueta, int NumeroCaja, string Lote, DateTime FechaEntrada, int Cantidad)> SeleccionarCajasHastaLimiteCargaRapida(IReadOnlyList<(int CajaID, int ParteID, int? SolicitudProduccionID, string NumeroOF, string Etiqueta, int NumeroCaja, string Lote, DateTime FechaEntrada, int Cantidad)> candidatas, long limite)
    {
        if (limite <= 0) return new();
        var ordenadas = candidatas.Where(x => x.Cantidad > 0 && x.Cantidad <= limite).OrderBy(x => x.FechaEntrada).ThenBy(x => x.CajaID).ToList();
        if (ordenadas.Count == 0) return new();
        var estados = new Dictionary<long, (long Anterior, int Indice)> { [0] = (0, -1) };
        for (var i = 0; i < ordenadas.Count; i++)
        {
            var cantidad = (long)ordenadas[i].Cantidad;
            var previas = estados.Keys.OrderByDescending(x => x).ToList();
            foreach (var sumaAnterior in previas)
            {
                var nuevaSuma = sumaAnterior + cantidad;
                if (nuevaSuma > limite || estados.ContainsKey(nuevaSuma)) continue;
                estados[nuevaSuma] = (sumaAnterior, i);
            }
        }
        var mejor = estados.Keys.Max();
        if (mejor <= 0) return new();
        var indices = new List<int>();
        var suma = mejor;
        while (suma > 0)
        {
            if (!estados.TryGetValue(suma, out var paso) || paso.Indice < 0) return new();
            indices.Add(paso.Indice);
            suma = paso.Anterior;
        }
        indices.Reverse();
        return indices.Select(x => ordenadas[x]).ToList();
    }
    private static bool DatosTransporteCompletos(
        string forma,
        string ruta,
        string unidad,
        string chofer,
        string transportista)
    {
        forma =
            NormalizarFormaEnvio(
                forma);

        return forma switch
        {
            "Interno" =>
                !string.IsNullOrWhiteSpace(ruta)
                &&
                !string.IsNullOrWhiteSpace(unidad)
                &&
                !string.IsNullOrWhiteSpace(chofer),

            "Cliente" =>
                !string.IsNullOrWhiteSpace(chofer),

            "Paqueteria" =>
                !string.IsNullOrWhiteSpace(
                    transportista),

            _ => false
        };
    }

    private static void CalcularProximaAccion(LogisticaOperacionDetalleVm vm)
    {
        if (vm.Estatus == "Cancelado")
        {
            vm.ProximaAccion = "Cancelado";
            vm.ProximaAccionDetalle = "El embarque ya no tiene acciones operativas.";
            return;
        }
        if (vm.Estatus == "Entregado")
        {
            vm.ProximaAccion = "Entregado";
            vm.ProximaAccionDetalle = "El proceso se encuentra cerrado.";
            return;
        }
        if (string.IsNullOrWhiteSpace(vm.FormaEnvio) || vm.FormaEnvio == "Pendiente")
        {
            vm.ProximaAccion = "Definir salida";
            vm.ProximaAccionDetalle = "Selecciona cómo saldrá el embarque.";
            return;
        }
        if (vm.Estatus is "Programado" or "Preparando")
        {
            vm.ProximaAccion = "Preparar carga";
            vm.ProximaAccionDetalle = "Verifica PT disponible y continúa con la carga cuando la mercancía esté lista.";
            return;
        }
        if (vm.Estatus == "Preparado")
        {
            vm.ProximaAccion = "Iniciar carga física";
            vm.ProximaAccionDetalle = "Escanea las cajas o utiliza Carga rápida si la operación lo requiere.";
            return;
        }
        if (vm.Estatus == "Cargando")
        {
            vm.ProximaAccion = "Continuar carga física";
            vm.ProximaAccionDetalle = "Completa las cajas pendientes y confirma la carga.";
            return;
        }
        if (vm.Estatus == "Cargado")
        {
            if (!vm.DocumentacionCompleta)
            {
                vm.ProximaAccion = "Completar documentación";
                vm.ProximaAccionDetalle = "La carga ya está completa. Termina la documentación obligatoria antes de liberar la mercancía.";
                return;
            }
            if (string.Equals(vm.FormaEnvio, "Cliente", StringComparison.OrdinalIgnoreCase))
            {
                vm.ProximaAccion = "Confirmar recolección";
                vm.ProximaAccionDetalle = "Registra quién recibe y la evidencia. Al confirmar se despacha PT y el embarque queda Entregado.";
                return;
            }
            vm.ProximaAccion = "Confirmar salida de planta";
            vm.ProximaAccionDetalle = string.Equals(vm.FormaEnvio, "Interno", StringComparison.OrdinalIgnoreCase) ? "La carga está completa. Inicia el viaje de la unidad NS." : "La carga está completa. Confirma la salida física de la mercancía.";
            return;
        }
        if (vm.Estatus == "En ruta")
        {
            if (string.Equals(vm.FormaEnvio, "Cliente", StringComparison.OrdinalIgnoreCase))
            {
                vm.ProximaAccion = "Cerrar recolección";
                vm.ProximaAccionDetalle = "Este embarque proviene del flujo anterior de Cliente recoge. Registra receptor y evidencia para cerrarlo.";
                return;
            }
            vm.ProximaAccion = "Confirmar entrega";
            vm.ProximaAccionDetalle = string.Equals(vm.FormaEnvio, "Interno", StringComparison.OrdinalIgnoreCase) ? "El chofer debe completar la parada de entrega." : "Adjunta evidencia y registra al receptor.";
            return;
        }
        vm.ProximaAccion = vm.Estatus;
        vm.ProximaAccionDetalle = "Revisa el estado actual.";
    }
    private static int CalcularPorcentajeAvance(
        string? estatus)
    {
        return estatus switch
        {
            "Programado" => 10,
            "Preparando" => 30,
            "Preparado" => 50,
            "Cargando" => 60,
            "Cargado" => 70,
            "En ruta" => 85,
            "Entregado" => 100,
            "Cancelado" => 0,
            _ => 0
        };
    }

    private static string NormalizarFormaEnvio(
        string? valor)
    {
        var texto =
            (valor ?? string.Empty)
                .Trim();

        if (string.IsNullOrWhiteSpace(
                texto)
            ||
            texto.Equals(
                "Pendiente",
                StringComparison.OrdinalIgnoreCase)
            ||
            texto.Equals(
                "Por definir",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Pendiente";
        }

        if (texto.Equals(
                "Interno",
                StringComparison.OrdinalIgnoreCase)
            ||
            texto.Equals(
                "Entrega NS",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Interno";
        }

        if (texto.Equals(
                "Cliente",
                StringComparison.OrdinalIgnoreCase)
            ||
            texto.Equals(
                "Cliente recoge",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Cliente";
        }

        if (texto.Equals(
                "Paqueteria",
                StringComparison.OrdinalIgnoreCase)
            ||
            texto.Equals(
                "Paquetería",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Paqueteria";
        }

        return texto;
    }

    private static string FormatearFechaHora(
        DateTime? fecha,
        TimeSpan? hora)
    {
        if (!fecha.HasValue)
            return "Sin fecha";

        var texto =
            fecha.Value
                .ToString("dd/MM/yyyy");

        if (hora.HasValue)
            texto +=
                $" {hora.Value:hh\\:mm}";

        return texto;
    }

    private static void EliminarArchivoSiExiste(
        string? ruta)
    {
        if (string.IsNullOrWhiteSpace(
                ruta))
            return;

        if (!System.IO.File.Exists(
                ruta))
            return;

        try
        {
            System.IO.File.Delete(
                ruta);
        }
        catch
        {
        }
    }

    private static object Db(
        object? value) =>
        value ?? DBNull.Value;

    private static string Texto(
        SqlDataReader rd,
        string columna)
    {
        var i =
            rd.GetOrdinal(
                columna);

        return rd.IsDBNull(i)
            ? string.Empty
            : Convert.ToString(
                rd.GetValue(i))?
                .Trim()
              ?? string.Empty;
    }

    private static int Entero(
        SqlDataReader rd,
        string columna)
    {
        var i =
            rd.GetOrdinal(
                columna);

        return rd.IsDBNull(i)
            ? 0
            : Convert.ToInt32(
                rd.GetValue(i));
    }

    private static int? EnteroNullable(
        SqlDataReader rd,
        string columna)
    {
        var i =
            rd.GetOrdinal(
                columna);

        return rd.IsDBNull(i)
            ? null
            : Convert.ToInt32(
                rd.GetValue(i));
    }

    private static long EnteroLargo(
        SqlDataReader rd,
        string columna)
    {
        var i =
            rd.GetOrdinal(
                columna);

        return rd.IsDBNull(i)
            ? 0L
            : Convert.ToInt64(
                rd.GetValue(i));
    }

    private static DateTime? Fecha(
        SqlDataReader rd,
        string columna)
    {
        var i =
            rd.GetOrdinal(
                columna);

        return rd.IsDBNull(i)
            ? null
            : Convert.ToDateTime(
                rd.GetValue(i));
    }

    private static TimeSpan? Hora(
        SqlDataReader rd,
        string columna)
    {
        var i =
            rd.GetOrdinal(
                columna);

        if (rd.IsDBNull(i))
            return null;

        var valor =
            rd.GetValue(i);

        if (valor is TimeSpan ts)
            return ts;

        return TimeSpan.TryParse(
            valor.ToString(),
            out var resultado)
                ? resultado
                : null;
    }

    private static bool Booleano(
        SqlDataReader rd,
        string columna)
    {
        var i =
            rd.GetOrdinal(
                columna);

        return !rd.IsDBNull(i)
               &&
               Convert.ToBoolean(
                   rd.GetValue(i));
    }

    private async Task<LogisticaOperacionFlujoVm?> CargarFlujoAsync(SqlConnection cn, int embarqueId, CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT e.EmbarqueID,e.ClienteID,ISNULL(e.Folio,N'') Folio,ISNULL(e.ClienteNombreSnapshot,N'') Cliente,ISNULL(e.Destino,N'') Destino,
e.FechaCargaProgramada,e.HoraCargaProgramada,e.FechaEntregaProgramada,ISNULL(e.Estatus,N'') Estatus,
ISNULL(e.TipoOperacion,N'Pendiente') TipoOperacion,ISNULL(e.FormaEnvio,N'Pendiente') FormaEnvio,ISNULL(e.ModalidadEnvio,N'') ModalidadEnvio,
ISNULL(e.Transportista,N'') Transportista,ISNULL(e.GuiaReferencia,N'') GuiaReferencia,e.PasaAduana,e.RutaID,e.UnidadID,e.ChoferUsuarioID,
ISNULL(e.ChoferNombreSnapshot,N'') ChoferNombreSnapshot,ISNULL(e.ChoferExterno,N'') ChoferExterno,ISNULL(e.UnidadExterna,N'') UnidadExterna,
ISNULL(e.PlacasExternas,N'') PlacasExternas,CONVERT(varbinary(8),e.RowVersion) RowVersion,
ISNULL(d.TotalPiezas,0) TotalPiezas,ISNULL(d.TotalDespachadas,0) TotalDespachadas,
ISNULL(c.TotalPreparadas,0) TotalPreparadas,ISNULL(c.TotalCajas,0) TotalCajas,ISNULL(c.TotalCargadas,0) TotalCargadas,
ISNULL(i.IncidenciasAbiertas,0) IncidenciasAbiertas,ISNULL(i.IncidenciasCriticas,0) IncidenciasCriticas,v.ViajeID
FROM dbo.Logistica_Embarques e
OUTER APPLY
(
    SELECT ISNULL(SUM(x.CantidadSolicitada),0) TotalPiezas,ISNULL(SUM(x.CantidadDespachada),0) TotalDespachadas
    FROM dbo.Logistica_EmbarqueDetalle x
    WHERE x.EmbarqueID=e.EmbarqueID AND x.Activo=1
) d
OUTER APPLY
(
    SELECT ISNULL(SUM(CASE WHEN x.EstatusSeleccion IN(N'Reservada',N'Cargada',N'Despachada') THEN x.CantidadAsignada ELSE 0 END),0) TotalPreparadas,
    COUNT(DISTINCT CASE WHEN x.EstatusSeleccion IN(N'Reservada',N'Cargada',N'Despachada') THEN x.CajaID END) TotalCajas,
    COUNT(DISTINCT CASE WHEN x.EstatusSeleccion IN(N'Cargada',N'Despachada') THEN x.CajaID END) TotalCargadas
    FROM dbo.Logistica_EmbarqueCajas x
    WHERE x.EmbarqueID=e.EmbarqueID AND (x.Activo=1 OR x.EstatusSeleccion=N'Despachada')
) c
OUTER APPLY
(
    SELECT CONVERT(int,COUNT_BIG(*)) IncidenciasAbiertas,ISNULL(SUM(CASE WHEN x.Severidad=N'Crítica' THEN 1 ELSE 0 END),0) IncidenciasCriticas
    FROM dbo.Logistica_Incidencias x
    WHERE x.EmbarqueID=e.EmbarqueID AND x.Activo=1 AND x.Estatus IN(N'Abierta',N'En seguimiento')
) i
OUTER APPLY
(
    SELECT TOP(1) rel.ViajeID
    FROM dbo.Logistica_ViajeEmbarques rel
    INNER JOIN dbo.Logistica_Viajes vx ON vx.ViajeID=rel.ViajeID AND vx.Activo=1
    WHERE rel.EmbarqueID=e.EmbarqueID AND rel.Activo=1
    ORDER BY CASE WHEN vx.Estatus IN(N'Programado',N'En curso') THEN 0 ELSE 1 END,rel.ViajeEmbarqueID DESC
) v
WHERE e.EmbarqueID=@EmbarqueID AND e.Activo=1;";
        LogisticaOperacionFlujoVm vm;
        string tipoOperacion, formaEnvio, modalidadEnvio;
        bool? pasaAduana;
        int? rutaId, unidadId, choferId;
        await using (var cmd = new SqlCommand(sql, cn))
        {
            cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await rd.ReadAsync(cancellationToken)) return null;
            tipoOperacion = NormalizarTipoOperacionFlujo(Texto(rd, "TipoOperacion"));
            formaEnvio = NormalizarFormaEnvio(Texto(rd, "FormaEnvio"));
            modalidadEnvio = NormalizarModalidadEnvioFlujo(Texto(rd, "ModalidadEnvio"));
            pasaAduana = rd.IsDBNull(rd.GetOrdinal("PasaAduana")) ? null : Convert.ToBoolean(rd["PasaAduana"]);
            rutaId = EnteroNullable(rd, "RutaID");
            unidadId = EnteroNullable(rd, "UnidadID");
            choferId = EnteroNullable(rd, "ChoferUsuarioID");
            vm = new LogisticaOperacionFlujoVm
            {
                EmbarqueID = Entero(rd, "EmbarqueID"),
                Folio = Texto(rd, "Folio"),
                ClienteID = Entero(rd, "ClienteID"),
                Cliente = Texto(rd, "Cliente"),
                Destino = Texto(rd, "Destino"),
                Estatus = Texto(rd, "Estatus"),
                FechaCargaProgramada = Fecha(rd, "FechaCargaProgramada"),
                HoraCargaProgramada = Hora(rd, "HoraCargaProgramada"),
                FechaEntregaProgramada = Fecha(rd, "FechaEntregaProgramada"),
                TotalPiezas = Entero(rd, "TotalPiezas"),
                TotalPiezasPreparadas = Entero(rd, "TotalPreparadas"),
                TotalPiezasDespachadas = Entero(rd, "TotalDespachadas"),
                TotalCajas = Entero(rd, "TotalCajas"),
                TotalCajasCargadas = Entero(rd, "TotalCargadas"),
                ViajeID = EnteroNullable(rd, "ViajeID"),
                IncidenciasAbiertas = Entero(rd, "IncidenciasAbiertas"),
                IncidenciasCriticas = Entero(rd, "IncidenciasCriticas"),
                RowVersion = Convert.ToBase64String(Bytes(rd, "RowVersion")),
                Salida = new LogisticaOperacionSalidaVm
                {
                    EmbarqueID = embarqueId,
                    TipoOperacion = tipoOperacion == "Pendiente" ? string.Empty : tipoOperacion,
                    FormaEnvio = formaEnvio == "Pendiente" ? string.Empty : formaEnvio,
                    ModalidadEnvio = modalidadEnvio,
                    Transportista = Texto(rd, "Transportista"),
                    GuiaReferencia = Texto(rd, "GuiaReferencia"),
                    PasaAduana = pasaAduana,
                    RutaID = rutaId,
                    UnidadID = unidadId,
                    ChoferUsuarioID = choferId,
                    ChoferNombreSnapshot = Texto(rd, "ChoferNombreSnapshot"),
                    ChoferExterno = Texto(rd, "ChoferExterno"),
                    UnidadExterna = Texto(rd, "UnidadExterna"),
                    PlacasExternas = Texto(rd, "PlacasExternas"),
                    RowVersion = Convert.ToBase64String(Bytes(rd, "RowVersion"))
                }
            };
        }
        vm.Evidencias = await CargarEvidenciasCompartidasAsync(cn, embarqueId, cancellationToken);
        vm.TotalEvidencias = vm.Evidencias.Count;
        var pt = await ObtenerEstadoPtFlujoAsync(cn, embarqueId, cancellationToken);
        var partesPt = await ObtenerDisponibilidadPtPorParteAsync(cn, embarqueId, cancellationToken);
        var salidaCompleta = SalidaOperacionCompleta(tipoOperacion, formaEnvio, modalidadEnvio, pasaAduana, rutaId, unidadId, choferId, vm.Salida.ChoferExterno, vm.Salida.Transportista, vm.Salida.PlacasExternas);
        var faltantes = salidaCompleta ? await ObtenerDocumentosFaltantesFlujoAsync(cn, embarqueId, tipoOperacion, formaEnvio, modalidadEnvio, pasaAduana, cancellationToken) : new List<string>();
        vm.DocumentosFaltantes = salidaCompleta ? faltantes.Count : 0;
        var programacionCompleta = vm.FechaCargaProgramada.HasValue && vm.HoraCargaProgramada.HasValue && vm.TotalPiezas > 0;
        var disponibilidadPtCompleta = vm.Estatus is "Cargado" or "En ruta" or "Entregado" || partesPt.Count > 0 && partesPt.All(x => x.Suficiente);
        var hayPtCargable = vm.Estatus is "Cargado" or "En ruta" or "Entregado" || partesPt.Any(x => x.Disponible > 0);
        var documentosCompletos = salidaCompleta && faltantes.Count == 0;
        var cargaCompleta = vm.Estatus is "En ruta" or "Entregado" || vm.Estatus == "Cargado" && vm.TotalCajas > 0 && vm.TotalCajasCargadas >= vm.TotalCajas;
        var salidaPlantaCompleta = vm.Estatus is "En ruta" or "Entregado";
        var entregaCompleta = vm.Estatus == "Entregado";
        var clienteRecoge = formaEnvio == "Cliente";
        if (vm.Estatus == "Cancelado")
        {
            programacionCompleta = false;
            disponibilidadPtCompleta = false;
            hayPtCargable = false;
            documentosCompletos = false;
            cargaCompleta = false;
            salidaPlantaCompleta = false;
            entregaCompleta = false;
        }
        var requeridoPt = partesPt.Sum(x => x.Requerido);
        var disponiblePt = partesPt.Sum(x => Math.Min(x.Disponible, x.Requerido));
        var faltantePt = partesPt.Sum(x => x.Faltante);
        var descripcionPt = disponibilidadPtCompleta ? $"{requeridoPt:N0} PZA requeridas. {disponiblePt:N0} PZA disponibles." : hayPtCargable ? $"{disponiblePt:N0} de {requeridoPt:N0} PZA disponibles. Puedes cargar lo disponible; {faltantePt:N0} PZA pasarán a Expeditado al confirmar." : $"No hay PT disponible para cargar. Faltan {faltantePt:N0} PZA.";
        var descripcionCarga = cargaCompleta ? $"{vm.TotalCajasCargadas:N0} de {vm.TotalCajas:N0} cajas cargadas." : "Escanea, selecciona cajas disponibles o utiliza Carga rápida.";
        var descripcionDocumentos = !salidaCompleta ? "Define primero la forma de salida." : documentosCompletos ? "Documentación obligatoria completa." : $"Faltan {faltantes.Count:N0} documento(s). Deben quedar completos antes de salir.";
        var paso1 = new LogisticaOperacionPasoVm { Numero = 1, Clave = "programacion", Titulo = "Programación", Descripcion = programacionCompleta ? "Fecha, hora y cantidad definidas." : "Completa fecha, hora y cantidad.", Icono = "fa-calendar-check", Completo = programacionCompleta };
        var paso2 = new LogisticaOperacionPasoVm { Numero = 2, Clave = "salida", Titulo = "Forma de salida", Descripcion = salidaCompleta ? "Datos de salida completos." : "Define cómo saldrá la mercancía.", Icono = "fa-route", Completo = salidaCompleta };
        var paso3 = new LogisticaOperacionPasoVm { Numero = 3, Clave = "preparacion", Titulo = "Disponibilidad PT", Descripcion = descripcionPt, Icono = "fa-boxes-stacked", Completo = disponibilidadPtCompleta };
        var paso4 = new LogisticaOperacionPasoVm { Numero = 4, Clave = "documentos", Titulo = "Documentación", Descripcion = descripcionDocumentos, Icono = "fa-file-circle-check", Completo = documentosCompletos };
        var paso5 = new LogisticaOperacionPasoVm { Numero = 5, Clave = "carga", Titulo = "Carga física", Descripcion = descripcionCarga, Icono = "fa-dolly", Completo = cargaCompleta };
        var paso6 = clienteRecoge
            ? new LogisticaOperacionPasoVm { Numero = 6, Clave = "salidaPlanta", Titulo = "Entrega al cliente", Descripcion = entregaCompleta ? "Recolección confirmada." : "Registra quién recoge y adjunta evidencia.", Icono = "fa-handshake", Completo = entregaCompleta }
            : new LogisticaOperacionPasoVm { Numero = 6, Clave = "salidaPlanta", Titulo = "Salida de planta", Descripcion = salidaPlantaCompleta ? "La mercancía ya salió de planta." : formaEnvio == "Interno" ? "Inicia el viaje de la unidad NS." : "Confirma la salida física de la mercancía.", Icono = "fa-truck-fast", Completo = salidaPlantaCompleta };
        var paso7 = clienteRecoge
            ? new LogisticaOperacionPasoVm { Numero = 7, Clave = "entrega", Titulo = "Cierre", Descripcion = entregaCompleta ? "Embarque cerrado al entregar al cliente." : "Se completa automáticamente al confirmar la recolección.", Icono = "fa-circle-check", Completo = entregaCompleta }
            : new LogisticaOperacionPasoVm { Numero = 7, Clave = "entrega", Titulo = "Entrega", Descripcion = entregaCompleta ? "Entrega confirmada." : formaEnvio == "Interno" ? "El chofer confirma la entrega desde su parada." : "Registra receptor y evidencia para cerrar.", Icono = "fa-circle-check", Completo = entregaCompleta };
        vm.Pasos.AddRange(new[] { paso1, paso2, paso3, paso4, paso5, paso6, paso7 });
        if (!vm.Cerrado)
        {
            paso1.Disponible = true;
            paso2.Disponible = programacionCompleta;
            paso3.Disponible = programacionCompleta;
            paso4.Disponible = programacionCompleta && salidaCompleta;
            paso5.Disponible = programacionCompleta && salidaCompleta && hayPtCargable && vm.Estatus is "Programado" or "Preparando" or "Preparado" or "Cargando";
            paso6.Disponible = cargaCompleta && documentosCompletos;
            paso7.Disponible = clienteRecoge ? vm.Estatus == "En ruta" : salidaPlantaCompleta;
            LogisticaOperacionPasoVm actual;
            if (!programacionCompleta) actual = paso1;
            else if (!salidaCompleta) actual = paso2;
            else if (!disponibilidadPtCompleta && !hayPtCargable) actual = paso3;
            else if (!cargaCompleta) actual = paso5;
            else if (!documentosCompletos) actual = paso4;
            else if (clienteRecoge && !entregaCompleta) actual = vm.Estatus == "En ruta" ? paso7 : paso6;
            else if (!salidaPlantaCompleta) actual = paso6;
            else actual = paso7;
            vm.PasoActual = actual.Numero;
            actual.Actual = true;
        }
        else vm.PasoActual = vm.Estatus == "Entregado" ? 7 : 1;
        var catalogos = await CargarCatalogosFlujoAsync(cn, cancellationToken);
        vm.Rutas = catalogos.Rutas;
        vm.Unidades = catalogos.Unidades;
        vm.Choferes = catalogos.Choferes;
        return vm;
    }

    private async Task<IActionResult?> ValidarAccesoCargaFisicaAsync(int embarqueId, CancellationToken cancellationToken)
    {
        if (!UsuarioID.HasValue || UsuarioID.Value <= 0)
            return RedirectToAction("Login", "Login");
        if (embarqueId <= 0)
            return BadRequest(new { ok = false, mensaje = "El embarque indicado no es válido." });
        if (await _acceso.TienePermisoAsync(UsuarioID.Value, "Tablero de Logística"))
            return null;
        await using var cn = await AbrirAsync(cancellationToken);
        const string sql = @"
SELECT COUNT_BIG(*)
FROM dbo.Logistica_ViajeEmbarques ve
INNER JOIN dbo.Logistica_Viajes v
    ON v.ViajeID=ve.ViajeID
    AND v.Activo=1
INNER JOIN dbo.Logistica_Embarques e
    ON e.EmbarqueID=ve.EmbarqueID
    AND e.Activo=1
INNER JOIN dbo.Usuarios u
    ON u.UsuarioID=@UsuarioID
    AND ISNULL(u.Activo,0)=1
INNER JOIN dbo.Persona p
    ON p.PersonaID=u.PersonaID
INNER JOIN dbo.Departamentos d
    ON d.DepartamentoID=u.DepartamentoID
    AND ISNULL(d.Activo,0)=1
WHERE ve.EmbarqueID=@EmbarqueID
  AND ve.Activo=1
  AND v.OperadorUsuarioID=@UsuarioID
  AND v.Estatus IN(N'Programado',N'En curso')
  AND ISNULL(e.FormaEnvio,N'')=N'Interno'
  AND UPPER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(d.NombreDepartamento,N''))),N'Í',N'I'),N'Ó',N'O'))=N'LOGISTICA'
  AND UPPER(LTRIM(RTRIM(ISNULL(p.Puesto,N'')))) LIKE N'%CHOFER%';";
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
        cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = UsuarioID.Value;
        var permitido = Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!permitido)
            return Forbid();
        return null;
    }
    private async Task<(byte[] RowVersion, bool Completo)> GuardarSalidaOperacionAsync(SqlConnection cn, SqlTransaction tx, LogisticaOperacionSalidaVm model, bool confirmar, CancellationToken cancellationToken)
    {
        model.TipoOperacion = NormalizarTipoOperacionFlujo(model.TipoOperacion);
        model.FormaEnvio = NormalizarFormaEnvio(model.FormaEnvio);
        model.ModalidadEnvio = NormalizarModalidadEnvioFlujo(model.ModalidadEnvio);
        model.Transportista = model.Transportista?.Trim();
        model.GuiaReferencia = model.GuiaReferencia?.Trim();
        model.ChoferNombreSnapshot = model.ChoferNombreSnapshot?.Trim();
        model.ChoferExterno = model.ChoferExterno?.Trim();
        model.UnidadExterna = model.UnidadExterna?.Trim();
        model.PlacasExternas = model.PlacasExternas?.Trim();
        if (string.IsNullOrWhiteSpace(model.TipoOperacion)) model.TipoOperacion = "Pendiente";
        if (string.IsNullOrWhiteSpace(model.FormaEnvio)) model.FormaEnvio = "Pendiente";
        if (!string.IsNullOrWhiteSpace(model.Transportista) && model.Transportista.Length > 200) throw new InvalidOperationException("La compañía o paquetería no puede exceder 200 caracteres.");
        if (!string.IsNullOrWhiteSpace(model.GuiaReferencia) && model.GuiaReferencia.Length > 150) throw new InvalidOperationException("La guía o referencia no puede exceder 150 caracteres.");
        if (!string.IsNullOrWhiteSpace(model.ChoferExterno) && model.ChoferExterno.Length > 200) throw new InvalidOperationException("El nombre de quien recoge no puede exceder 200 caracteres.");
        if (!string.IsNullOrWhiteSpace(model.UnidadExterna) && model.UnidadExterna.Length > 100) throw new InvalidOperationException("La unidad externa no puede exceder 100 caracteres.");
        if (!string.IsNullOrWhiteSpace(model.PlacasExternas) && model.PlacasExternas.Length > 100) throw new InvalidOperationException("Las placas externas no pueden exceder 100 caracteres.");
        byte[] versionOriginal;
        try { versionOriginal = Convert.FromBase64String(model.RowVersion!); }
        catch { throw new DBConcurrencyException("La versión del embarque no es válida. Recarga el flujo."); }
        const string sqlActual = @"
SELECT ISNULL(Folio,N'') Folio,ISNULL(Estatus,N'') Estatus,ISNULL(FormaEnvio,N'Pendiente') FormaEnvio,FechaCargaProgramada,HoraCargaProgramada,CONVERT(varbinary(8),RowVersion) RowVersion
FROM dbo.Logistica_Embarques WITH(UPDLOCK,HOLDLOCK)
WHERE EmbarqueID=@EmbarqueID AND Activo=1;";
        string folio, estatus, formaAnterior;
        DateTime? fechaCargaProgramada;
        TimeSpan? horaCargaProgramada;
        byte[] versionActual;
        await using (var cmd = new SqlCommand(sqlActual, cn, tx))
        {
            cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = model.EmbarqueID;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await rd.ReadAsync(cancellationToken)) throw new InvalidOperationException("El embarque ya no existe.");
            folio = Texto(rd, "Folio");
            estatus = Texto(rd, "Estatus");
            formaAnterior = NormalizarFormaEnvio(Texto(rd, "FormaEnvio"));
            fechaCargaProgramada = Fecha(rd, "FechaCargaProgramada");
            horaCargaProgramada = Hora(rd, "HoraCargaProgramada");
            versionActual = Bytes(rd, "RowVersion");
        }
        if (!versionActual.SequenceEqual(versionOriginal)) throw new DBConcurrencyException("El embarque fue modificado por otro usuario. Recarga el flujo.");
        if (estatus is "Cargando" or "Cargado" or "En ruta" or "Entregado" or "Cancelado") throw new InvalidOperationException($"La forma de salida ya no puede modificarse porque el embarque está en estatus {estatus}.");
        const string sqlLimpiarViajesCancelados = @"
UPDATE ve
SET Activo=0
FROM dbo.Logistica_ViajeEmbarques ve
INNER JOIN dbo.Logistica_Viajes v ON v.ViajeID=ve.ViajeID
WHERE ve.EmbarqueID=@EmbarqueID AND ve.Activo=1 AND v.Activo=1 AND v.Estatus=N'Cancelado';";
        await using (var cmd = new SqlCommand(sqlLimpiarViajesCancelados, cn, tx))
        {
            cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = model.EmbarqueID;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        int? viajeActualId = null;
        string estatusViajeActual = string.Empty;
        const string sqlViajeActual = @"
SELECT TOP(1) v.ViajeID,ISNULL(v.Estatus,N'') Estatus
FROM dbo.Logistica_ViajeEmbarques ve WITH(UPDLOCK,HOLDLOCK)
INNER JOIN dbo.Logistica_Viajes v WITH(UPDLOCK,HOLDLOCK) ON v.ViajeID=ve.ViajeID AND v.Activo=1
WHERE ve.EmbarqueID=@EmbarqueID AND ve.Activo=1 AND v.Estatus<>N'Cancelado'
ORDER BY CASE WHEN v.Estatus=N'Programado' THEN 0 WHEN v.Estatus=N'En curso' THEN 1 ELSE 2 END,ve.ViajeEmbarqueID DESC;";
        await using (var cmd = new SqlCommand(sqlViajeActual, cn, tx))
        {
            cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = model.EmbarqueID;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await rd.ReadAsync(cancellationToken))
            {
                viajeActualId = Entero(rd, "ViajeID");
                estatusViajeActual = Texto(rd, "Estatus");
            }
        }
        if (model.TipoOperacion == "Exportacion")
        {
            if (model.FormaEnvio == "Interno") throw new InvalidOperationException("Una exportación no puede utilizar Entrega. Selecciona Cliente recoge o Paquetería.");
        }
        else model.PasaAduana = null;
        if (model.FormaEnvio == "Interno")
        {
            model.ModalidadEnvio = null;
            model.Transportista = null;
            model.GuiaReferencia = null;
            model.ChoferExterno = null;
            model.UnidadExterna = null;
            model.PlacasExternas = null;
        }
        else if (model.FormaEnvio == "Cliente")
        {
            model.RutaID = null;
            model.UnidadID = null;
            model.ChoferUsuarioID = null;
            model.ChoferNombreSnapshot = null;
            model.ModalidadEnvio = null;
            model.Transportista = null;
            model.GuiaReferencia = null;
        }
        else if (model.FormaEnvio == "Paqueteria")
        {
            model.RutaID = null;
            model.UnidadID = null;
            model.ChoferUsuarioID = null;
            model.ChoferNombreSnapshot = null;
        }
        else
        {
            model.ModalidadEnvio = null;
            model.Transportista = null;
            model.GuiaReferencia = null;
            model.RutaID = null;
            model.UnidadID = null;
            model.ChoferUsuarioID = null;
            model.ChoferNombreSnapshot = null;
            model.ChoferExterno = null;
            model.UnidadExterna = null;
            model.PlacasExternas = null;
            model.PasaAduana = null;
        }
        string? operador = null;
        if (model.FormaEnvio == "Interno" && model.ChoferUsuarioID.HasValue)
        {
            operador = await ObtenerNombreChoferFlujoAsync(cn, tx, model.ChoferUsuarioID.Value, cancellationToken);
            model.ChoferNombreSnapshot = operador;
        }
        else if (model.FormaEnvio == "Cliente") operador = model.ChoferExterno;
        else if (model.FormaEnvio == "Paqueteria") operador = string.IsNullOrWhiteSpace(model.ChoferExterno) ? model.Transportista : model.ChoferExterno;
        if (model.RutaID.HasValue)
        {
            await using var cmd = new SqlCommand("SELECT COUNT_BIG(*) FROM dbo.Logistica_Rutas WHERE RutaID=@ID AND Activo=1;", cn, tx);
            cmd.Parameters.Add("@ID", SqlDbType.Int).Value = model.RutaID.Value;
            if (Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken)) <= 0) throw new InvalidOperationException("La ruta seleccionada ya no está activa.");
        }
        if (model.UnidadID.HasValue)
        {
            await using var cmd = new SqlCommand("SELECT COUNT_BIG(*) FROM dbo.Logistica_Unidades WHERE UnidadID=@ID AND Activo=1;", cn, tx);
            cmd.Parameters.Add("@ID", SqlDbType.Int).Value = model.UnidadID.Value;
            if (Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken)) <= 0) throw new InvalidOperationException("La unidad seleccionada ya no está activa.");
        }
        var completo = SalidaOperacionCompleta(model.TipoOperacion, model.FormaEnvio, model.ModalidadEnvio, model.PasaAduana, model.RutaID, model.UnidadID, model.ChoferUsuarioID, model.ChoferExterno, model.Transportista, model.PlacasExternas);
        if (confirmar && !completo)
        {
            if (model.TipoOperacion is not "Nacional" and not "Exportacion") throw new InvalidOperationException("Selecciona si el embarque es Nacional o Exportación.");
            if (model.TipoOperacion == "Exportacion" && !model.PasaAduana.HasValue) throw new InvalidOperationException("Indica si la exportación pasa por aduana.");
            if (model.TipoOperacion == "Exportacion" && model.FormaEnvio == "Interno") throw new InvalidOperationException("En Exportación solo se permite Cliente recoge o Paquetería.");
            if (model.FormaEnvio is not "Interno" and not "Cliente" and not "Paqueteria")
            {
                if (model.TipoOperacion == "Exportacion") throw new InvalidOperationException("Selecciona Cliente recoge o Paquetería.");
                throw new InvalidOperationException("Selecciona Entrega, Cliente recoge o Paquetería.");
            }
            if (model.FormaEnvio == "Interno")
            {
                if (!model.RutaID.HasValue) throw new InvalidOperationException("Selecciona una ruta.");
                if (!model.UnidadID.HasValue) throw new InvalidOperationException("Selecciona una unidad.");
                if (!model.ChoferUsuarioID.HasValue) throw new InvalidOperationException("Selecciona un chofer.");
            }
            else if (model.FormaEnvio == "Cliente")
            {
                if (string.IsNullOrWhiteSpace(model.ChoferExterno)) throw new InvalidOperationException("Captura quién recoge la mercancía.");
            }
            else if (model.FormaEnvio == "Paqueteria")
            {
                if (string.IsNullOrWhiteSpace(model.ModalidadEnvio)) throw new InvalidOperationException("Selecciona Terrestre, Aérea o Marítima.");
                if (string.IsNullOrWhiteSpace(model.Transportista)) throw new InvalidOperationException("Captura la compañía o paquetería.");
                if (string.IsNullOrWhiteSpace(model.PlacasExternas)) throw new InvalidOperationException("Captura las placas del vehículo de la paquetería.");
            }
            throw new InvalidOperationException("Completa la información obligatoria antes de continuar.");
        }
        if (model.FormaEnvio == "Interno" && viajeActualId.HasValue)
        {
            if (estatusViajeActual != "Programado") throw new InvalidOperationException($"El viaje VIA-{viajeActualId.Value:000000} ya está en estatus {estatusViajeActual}. La configuración de Entrega ya no puede modificarse.");
            if (!completo) throw new InvalidOperationException($"El embarque ya tiene el viaje VIA-{viajeActualId.Value:000000} programado. Para modificar una Entrega debes conservar ruta, unidad y chofer completos.");
        }
        int? viajeDesvinculadoId = null;
        if (model.FormaEnvio != "Interno" && viajeActualId.HasValue)
        {
            var formaTexto = model.FormaEnvio switch
            {
                "Cliente" => "Cliente recoge",
                "Paqueteria" => "Paquetería",
                _ => "Por definir"
            };
            viajeDesvinculadoId = await DesvincularViajePorCambioFormaEnvioAsync(cn, tx, model.EmbarqueID, model.FormaEnvio, $"La forma de salida de {folio} cambió de Entrega a {formaTexto}.", cancellationToken);
            viajeActualId = null;
            estatusViajeActual = string.Empty;
        }
        var viajeExcluir = model.FormaEnvio == "Interno" && viajeActualId.HasValue && estatusViajeActual == "Programado" ? viajeActualId : null;
        if (model.FormaEnvio == "Interno" && model.UnidadID.HasValue && model.ChoferUsuarioID.HasValue && fechaCargaProgramada.HasValue && horaCargaProgramada.HasValue)
            await ValidarDisponibilidadProgramacionAsync(cn, tx, fechaCargaProgramada.Value.Date, horaCargaProgramada.Value, model.UnidadID.Value, model.ChoferUsuarioID.Value, model.EmbarqueID, viajeExcluir, cancellationToken);
        const string sqlUpdate = @"
UPDATE dbo.Logistica_Embarques
SET TipoOperacion=@TipoOperacion,FormaEnvio=@FormaEnvio,ModalidadEnvio=@ModalidadEnvio,Transportista=@Transportista,GuiaReferencia=@GuiaReferencia,
PasaAduana=@PasaAduana,RutaID=@RutaID,UnidadID=@UnidadID,OperadorTexto=@Operador,ChoferUsuarioID=@ChoferUsuarioID,
ChoferNombreSnapshot=@ChoferNombreSnapshot,ChoferExterno=@ChoferExterno,UnidadExterna=@UnidadExterna,PlacasExternas=@PlacasExternas,
FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
OUTPUT CONVERT(varbinary(8),INSERTED.RowVersion)
WHERE EmbarqueID=@EmbarqueID AND Activo=1
AND Estatus NOT IN(N'Cargando',N'Cargado',N'En ruta',N'Entregado',N'Cancelado')
AND RowVersion=@RowVersion;";
        byte[] nuevaVersion;
        await using (var cmd = new SqlCommand(sqlUpdate, cn, tx))
        {
            cmd.Parameters.Add("@TipoOperacion", SqlDbType.NVarChar, 30).Value = model.TipoOperacion;
            cmd.Parameters.Add("@FormaEnvio", SqlDbType.NVarChar, 30).Value = model.FormaEnvio;
            cmd.Parameters.Add("@ModalidadEnvio", SqlDbType.NVarChar, 30).Value = Db(model.ModalidadEnvio);
            cmd.Parameters.Add("@Transportista", SqlDbType.NVarChar, 200).Value = Db(model.Transportista);
            cmd.Parameters.Add("@GuiaReferencia", SqlDbType.NVarChar, 150).Value = Db(model.GuiaReferencia);
            cmd.Parameters.Add("@PasaAduana", SqlDbType.Bit).Value = Db(model.PasaAduana);
            cmd.Parameters.Add("@RutaID", SqlDbType.Int).Value = Db(model.RutaID);
            cmd.Parameters.Add("@UnidadID", SqlDbType.Int).Value = Db(model.UnidadID);
            cmd.Parameters.Add("@Operador", SqlDbType.NVarChar, 200).Value = Db(operador);
            cmd.Parameters.Add("@ChoferUsuarioID", SqlDbType.Int).Value = Db(model.FormaEnvio == "Interno" ? model.ChoferUsuarioID : null);
            cmd.Parameters.Add("@ChoferNombreSnapshot", SqlDbType.NVarChar, 200).Value = Db(model.FormaEnvio == "Interno" ? operador : null);
            cmd.Parameters.Add("@ChoferExterno", SqlDbType.NVarChar, 200).Value = Db(model.FormaEnvio == "Interno" ? null : model.ChoferExterno);
            cmd.Parameters.Add("@UnidadExterna", SqlDbType.NVarChar, 100).Value = Db(model.FormaEnvio == "Interno" ? null : model.UnidadExterna);
            cmd.Parameters.Add("@PlacasExternas", SqlDbType.NVarChar, 100).Value = Db(model.FormaEnvio == "Interno" ? null : model.PlacasExternas);
            cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
            cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = model.EmbarqueID;
            cmd.Parameters.Add("@RowVersion", SqlDbType.Timestamp).Value = versionOriginal;
            var resultado = await cmd.ExecuteScalarAsync(cancellationToken);
            if (resultado == null || resultado == DBNull.Value) throw new DBConcurrencyException("El embarque cambió mientras se guardaba la forma de salida. Recarga el flujo.");
            nuevaVersion = (byte[])resultado;
        }
        int? viajeId = null;
        if (model.FormaEnvio == "Interno" && completo && (confirmar || viajeActualId.HasValue))
        {
            if (!fechaCargaProgramada.HasValue || !horaCargaProgramada.HasValue) throw new InvalidOperationException("El embarque no tiene fecha y hora programadas.");
            viajeId = await AsegurarViajeEntregaNsAsync(cn, tx, model.EmbarqueID, fechaCargaProgramada.Value.Date, horaCargaProgramada.Value, model.RutaID!.Value, model.UnidadID!.Value, model.ChoferUsuarioID!.Value, operador ?? string.Empty, cancellationToken);
        }
        var evento = confirmar ? "MODALIDAD_ENVIO_DEFINIDA" : "BORRADOR_SALIDA_GUARDADO";
        var textoFormaNueva = model.FormaEnvio switch
        {
            "Interno" => "Entrega",
            "Cliente" => "Cliente recoge",
            "Paqueteria" => "Paquetería",
            _ => "Por definir"
        };
        var textoFormaAnterior = formaAnterior switch
        {
            "Interno" => "Entrega",
            "Cliente" => "Cliente recoge",
            "Paqueteria" => "Paquetería",
            _ => "Por definir"
        };
        var descripcion = confirmar
            ? $"Forma de salida confirmada desde Centro Operativo. {textoFormaAnterior} → {textoFormaNueva}."
            : $"Borrador de forma de salida guardado desde Centro Operativo. Avance: {(completo ? "completo" : "incompleto")}. Forma: {textoFormaAnterior} → {textoFormaNueva}.";
        if (viajeId.HasValue) descripcion += $" Viaje VIA-{viajeId.Value:000000} relacionado y sincronizado.";
        if (viajeDesvinculadoId.HasValue) descripcion += $" VIA-{viajeDesvinculadoId.Value:000000} desvinculado por cambio de forma de salida.";
        await InsertarHistorialAsync(cn, tx, model.EmbarqueID, evento, estatus, estatus, descripcion, cancellationToken);
        return (nuevaVersion, completo);
    }
    private async Task<(long PiezasProducidas, long PiezasPTLibres, long PiezasPTReservadas, long PiezasPTUtilizables, int CajasPTLibres, int CajasPTReservadas)> ObtenerEstadoPtFlujoAsync(SqlConnection cn, int embarqueId, CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT ISNULL(SUM(CONVERT(bigint,ISNULL(pp.CantidadProducida,0))),0) AS PiezasProducidas
FROM dbo.Planeacion_ProgramaProduccion pp
INNER JOIN dbo.SolicitudesProduccionDetalle spd
    ON spd.SolicitudProduccionDetalleID=pp.SolicitudProduccionDetalleID
   AND spd.Activo=1
INNER JOIN dbo.SolicitudesProduccion sp
    ON sp.SolicitudProduccionID=pp.SolicitudProduccionID
WHERE pp.Activo=1
  AND pp.SolicitudProduccionID IS NOT NULL
  AND pp.SolicitudProduccionDetalleID IS NOT NULL
  AND EXISTS
  (
      SELECT 1
      FROM dbo.Logistica_EmbarqueDetalle d
      WHERE d.EmbarqueID=@EmbarqueID
        AND d.Activo=1
        AND d.ParteID=spd.ParteID
        AND
        (
            (d.SolicitudProduccionDetalleID IS NOT NULL
             AND d.SolicitudProduccionDetalleID=pp.SolicitudProduccionDetalleID)
            OR
            (d.SolicitudProduccionDetalleID IS NULL
             AND d.SolicitudProduccionID IS NOT NULL
             AND d.SolicitudProduccionID=pp.SolicitudProduccionID)
            OR
            (
                d.SolicitudProduccionDetalleID IS NULL
                AND d.SolicitudProduccionID IS NULL
                AND NULLIF(LTRIM(RTRIM(ISNULL(d.NumeroOFSnapshot,N''))),N'') IS NOT NULL
                AND UPPER(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(d.NumeroOFSnapshot,N''))),NCHAR(39),N'/'),N'’',N'/'),N'´',N'/'),N'`',N'/'))
                    =UPPER(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(COALESCE(NULLIF(sp.NumeroOFRecibida,N''),NULLIF(sp.FolioSolicitud,N''),CONCAT(N'OF-ID-',sp.SolicitudProduccionID)))),NCHAR(39),N'/'),N'’',N'/'),N'´',N'/'),N'`',N'/'))
            )
        )
  );

SELECT
    ISNULL(SUM(CONVERT(bigint,c.Disponible)),0) AS PiezasPTLibres,
    COUNT_BIG(*) AS CajasPTLibres
FROM dbo.vw_Logistica_CajasDisponibles c
INNER JOIN dbo.ERP_Partes p
    ON p.ParteID=c.ParteID
INNER JOIN dbo.Logistica_Embarques e
    ON e.EmbarqueID=@EmbarqueID
   AND e.Activo=1
WHERE c.Disponible>0
  AND p.ClienteID=e.ClienteID
  AND EXISTS
  (
      SELECT 1
      FROM dbo.Logistica_EmbarqueDetalle d
      WHERE d.EmbarqueID=e.EmbarqueID
        AND d.Activo=1
        AND d.ParteID=c.ParteID
  );

SELECT
    ISNULL(SUM(r.Cantidad),0) AS PiezasPTReservadas,
    COUNT_BIG(*) AS CajasPTReservadas
FROM
(
    SELECT
        ec.CajaID,
        SUM(CONVERT(bigint,ISNULL(ec.CantidadAsignada,0))) AS Cantidad
    FROM dbo.Logistica_EmbarqueCajas ec
    INNER JOIN dbo.AlmacenPT_Cajas c
        ON c.CajaID=ec.CajaID
       AND c.Activo=1
    INNER JOIN dbo.ERP_Partes p
        ON p.ParteID=c.ParteID
    INNER JOIN dbo.Logistica_Embarques e
        ON e.EmbarqueID=ec.EmbarqueID
       AND e.Activo=1
       AND e.ClienteID=p.ClienteID
    WHERE ec.EmbarqueID=@EmbarqueID
      AND ec.Activo=1
      AND ec.EstatusSeleccion IN(N'Reservada',N'Cargada',N'Despachada')
    GROUP BY ec.CajaID
) r;";
        long producidas = 0;
        long libres = 0;
        long reservadas = 0;
        int cajasLibres = 0;
        int cajasReservadas = 0;
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await rd.ReadAsync(cancellationToken))
            producidas = rd.IsDBNull(0) ? 0 : Convert.ToInt64(rd.GetValue(0));
        if (await rd.NextResultAsync(cancellationToken) && await rd.ReadAsync(cancellationToken))
        {
            libres = rd.IsDBNull(rd.GetOrdinal("PiezasPTLibres")) ? 0 : Convert.ToInt64(rd["PiezasPTLibres"]);
            cajasLibres = rd.IsDBNull(rd.GetOrdinal("CajasPTLibres")) ? 0 : Convert.ToInt32(rd["CajasPTLibres"]);
        }
        if (await rd.NextResultAsync(cancellationToken) && await rd.ReadAsync(cancellationToken))
        {
            reservadas = rd.IsDBNull(rd.GetOrdinal("PiezasPTReservadas")) ? 0 : Convert.ToInt64(rd["PiezasPTReservadas"]);
            cajasReservadas = rd.IsDBNull(rd.GetOrdinal("CajasPTReservadas")) ? 0 : Convert.ToInt32(rd["CajasPTReservadas"]);
        }
        var utilizables = Math.Max(0, libres) + Math.Max(0, reservadas);
        return (
            Math.Max(0, producidas),
            Math.Max(0, libres),
            Math.Max(0, reservadas),
            utilizables,
            Math.Max(0, cajasLibres),
            Math.Max(0, cajasReservadas));
    }
    private async Task<(List<LogisticaOperacionCatalogoVm> Rutas, List<LogisticaOperacionCatalogoVm> Unidades, List<LogisticaOperacionCatalogoVm> Choferes)> CargarCatalogosFlujoAsync(SqlConnection cn, CancellationToken cancellationToken)
    {
        var rutas = new List<LogisticaOperacionCatalogoVm>();
        var unidades = new List<LogisticaOperacionCatalogoVm>();
        var choferes = new List<LogisticaOperacionCatalogoVm>();
        const string sql = @"
SELECT RutaID,Codigo+N' - '+Nombre Texto
FROM dbo.Logistica_Rutas
WHERE Activo=1
ORDER BY Codigo;
SELECT UnidadID,NumeroEconomico+CASE WHEN NULLIF(LTRIM(RTRIM(ISNULL(Placas,N''))),N'') IS NULL THEN N'' ELSE N' - '+Placas END Texto
FROM dbo.Logistica_Unidades
WHERE Activo=1
ORDER BY NumeroEconomico;
SELECT DISTINCT U.UsuarioID,LTRIM(RTRIM(CONCAT(ISNULL(P.Nombre,N''),N' ',ISNULL(P.ApellidoPaterno,N''),N' ',ISNULL(P.ApellidoMaterno,N'')))) Texto
FROM dbo.Usuarios U
INNER JOIN dbo.Persona P ON P.PersonaID=U.PersonaID
INNER JOIN dbo.Departamentos D ON D.DepartamentoID=U.DepartamentoID
WHERE U.Activo=1
AND D.Activo=1
AND UPPER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(D.NombreDepartamento,N''))),N'Í',N'I'),N'Ó',N'O'))=N'LOGISTICA'
AND UPPER(LTRIM(RTRIM(ISNULL(P.Puesto,N'')))) LIKE N'%CHOFER%'
ORDER BY Texto;";
        await using var cmd = new SqlCommand(sql, cn);
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await rd.ReadAsync(cancellationToken)) rutas.Add(new LogisticaOperacionCatalogoVm { Id = Entero(rd, "RutaID"), Texto = Texto(rd, "Texto") });
        if (await rd.NextResultAsync(cancellationToken))
        {
            while (await rd.ReadAsync(cancellationToken)) unidades.Add(new LogisticaOperacionCatalogoVm { Id = Entero(rd, "UnidadID"), Texto = Texto(rd, "Texto") });
        }
        if (await rd.NextResultAsync(cancellationToken))
        {
            while (await rd.ReadAsync(cancellationToken)) choferes.Add(new LogisticaOperacionCatalogoVm { Id = Entero(rd, "UsuarioID"), Texto = Texto(rd, "Texto") });
        }
        return (rutas, unidades, choferes);
    }
    private static bool SalidaOperacionCompleta(string? tipoOperacion, string? formaEnvio, string? modalidadEnvio, bool? pasaAduana, int? rutaId, int? unidadId, int? choferUsuarioId, string? choferExterno, string? transportista, string? placasExternas)
    {
        var tipo = NormalizarTipoOperacionFlujo(tipoOperacion);
        var forma = NormalizarFormaEnvio(formaEnvio);
        var modalidad = NormalizarModalidadEnvioFlujo(modalidadEnvio);
        if (tipo is not "Nacional" and not "Exportacion") return false;
        if (tipo == "Exportacion")
        {
            if (!pasaAduana.HasValue) return false;
            if (forma == "Interno") return false;
        }
        if (forma == "Interno")
        {
            if (tipo == "Exportacion") return false;
            return rutaId.HasValue && rutaId.Value > 0
                && unidadId.HasValue && unidadId.Value > 0
                && choferUsuarioId.HasValue && choferUsuarioId.Value > 0;
        }
        if (forma == "Cliente") return !string.IsNullOrWhiteSpace(choferExterno);
        if (forma == "Paqueteria")
            return modalidad is "Terrestre" or "Aereo" or "Maritimo"
                && !string.IsNullOrWhiteSpace(transportista)
                && !string.IsNullOrWhiteSpace(placasExternas);
        return false;
    }

    private async Task<int?> DesvincularViajePorCambioFormaEnvioAsync(SqlConnection cn, SqlTransaction tx, int embarqueId, string nuevaFormaEnvio, string motivo, CancellationToken cancellationToken)
    {
        nuevaFormaEnvio = NormalizarFormaEnvio(nuevaFormaEnvio);
        if (nuevaFormaEnvio == "Interno") return null;
        const string sqlRelacion = @"
SELECT ve.ViajeEmbarqueID,ve.ViajeID,ve.ViajeParadaID,ISNULL(v.Estatus,N'') EstatusViaje
FROM dbo.Logistica_ViajeEmbarques ve WITH(UPDLOCK,HOLDLOCK)
INNER JOIN dbo.Logistica_Viajes v WITH(UPDLOCK,HOLDLOCK) ON v.ViajeID=ve.ViajeID AND v.Activo=1
WHERE ve.EmbarqueID=@EmbarqueID AND ve.Activo=1 AND ISNULL(v.Estatus,N'')<>N'Cancelado'
ORDER BY CASE WHEN v.Estatus=N'Programado' THEN 0 WHEN v.Estatus=N'En curso' THEN 1 ELSE 2 END,ve.ViajeEmbarqueID DESC;";
        var relaciones = new List<(int ViajeEmbarqueID, int ViajeID, int? ViajeParadaID, string Estatus)>();
        await using (var cmd = new SqlCommand(sqlRelacion, cn, tx))
        {
            cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await rd.ReadAsync(cancellationToken))
                relaciones.Add((Entero(rd, "ViajeEmbarqueID"), Entero(rd, "ViajeID"), EnteroNullable(rd, "ViajeParadaID"), Texto(rd, "EstatusViaje")));
        }
        if (relaciones.Count == 0) return null;
        if (relaciones.Count > 1) throw new InvalidOperationException($"El embarque tiene {relaciones.Count:N0} viajes activos relacionados. Corrige la inconsistencia antes de cambiar la forma de salida.");
        var relacion = relaciones[0];
        if (relacion.Estatus == "En curso") throw new InvalidOperationException($"El viaje VIA-{relacion.ViajeID:000000} ya está En curso. La forma de salida ya no puede cambiarse.");
        if (relacion.Estatus == "Completado") throw new InvalidOperationException($"El viaje VIA-{relacion.ViajeID:000000} ya está Completado. La forma de salida ya no puede cambiarse.");
        if (relacion.Estatus != "Programado") throw new InvalidOperationException($"El viaje VIA-{relacion.ViajeID:000000} se encuentra en estatus {relacion.Estatus} y no puede modificarse desde el embarque.");
        var paradaId = relacion.ViajeParadaID;
        if (!paradaId.HasValue)
        {
            const string sqlParada = @"
SELECT TOP(1) ViajeParadaID
FROM dbo.Logistica_ViajeParadas WITH(UPDLOCK,HOLDLOCK)
WHERE ViajeID=@ViajeID AND Activo=1 AND ReferenciaTipo=N'Embarque' AND ReferenciaID=@EmbarqueID
ORDER BY Secuencia,ViajeParadaID;";
            await using var cmd = new SqlCommand(sqlParada, cn, tx);
            cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = relacion.ViajeID;
            cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
            var valor = await cmd.ExecuteScalarAsync(cancellationToken);
            if (valor != null && valor != DBNull.Value) paradaId = Convert.ToInt32(valor);
        }
        const string sqlConteos = @"
SELECT COUNT_BIG(*)
FROM dbo.Logistica_ViajeEmbarques ve
INNER JOIN dbo.Logistica_Embarques e ON e.EmbarqueID=ve.EmbarqueID AND e.Activo=1
WHERE ve.ViajeID=@ViajeID AND ve.Activo=1 AND ve.EmbarqueID<>@EmbarqueID AND ISNULL(e.Estatus,N'')<>N'Cancelado';

SELECT COUNT_BIG(*)
FROM dbo.Logistica_ViajeParadas p
WHERE p.ViajeID=@ViajeID AND p.Activo=1
AND p.TipoParada<>N'Origen'
AND ISNULL(p.CierraViaje,0)=0
AND (@ParadaID IS NULL OR p.ViajeParadaID<>@ParadaID)
AND NOT(ISNULL(p.ReferenciaTipo,N'')=N'Embarque' AND p.ReferenciaID=@EmbarqueID);";
        long otrosEmbarques;
        long otrasParadas;
        await using (var cmd = new SqlCommand(sqlConteos, cn, tx))
        {
            cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = relacion.ViajeID;
            cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
            cmd.Parameters.Add("@ParadaID", SqlDbType.Int).Value = Db(paradaId);
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            otrosEmbarques = await rd.ReadAsync(cancellationToken) ? Convert.ToInt64(rd.GetValue(0)) : 0;
            otrasParadas = 0;
            if (await rd.NextResultAsync(cancellationToken) && await rd.ReadAsync(cancellationToken)) otrasParadas = Convert.ToInt64(rd.GetValue(0));
        }
        var cancelarViajeCompleto = otrosEmbarques == 0 && otrasParadas == 0;
        if (cancelarViajeCompleto)
        {
            const string sqlCancelar = @"
UPDATE dbo.Logistica_Viajes
SET Estatus=N'Cancelado',MotivoCancelacion=@Motivo,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
WHERE ViajeID=@ViajeID AND Activo=1 AND Estatus=N'Programado';

UPDATE dbo.Logistica_ViajeEmbarques
SET Activo=0
WHERE ViajeID=@ViajeID AND Activo=1;

UPDATE dbo.Logistica_ViajeParadas
SET Estatus=N'Cancelada',FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
WHERE ViajeID=@ViajeID AND Activo=1 AND Estatus=N'Pendiente';";
            await using (var cmd = new SqlCommand(sqlCancelar, cn, tx))
            {
                cmd.Parameters.Add("@Motivo", SqlDbType.NVarChar, 1000).Value = motivo;
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = relacion.ViajeID;
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            if (paradaId.HasValue)
                await InsertarHistorialParadaViajeAsync(cn, tx, paradaId.Value, "PARADA_CANCELADA_CAMBIO_FORMA", "Pendiente", "Cancelada", motivo, cancellationToken);
            await InsertarHistorialViajeAsync(cn, tx, relacion.ViajeID, "VIAJE_CANCELADO_CAMBIO_FORMA", "Programado", "Cancelado", motivo, cancellationToken);
            return relacion.ViajeID;
        }
        if (paradaId.HasValue)
        {
            await InsertarHistorialParadaViajeAsync(cn, tx, paradaId.Value, "PARADA_RETIRADA_CAMBIO_FORMA", "Pendiente", "Cancelada", motivo, cancellationToken);
            const string sqlQuitarParada = @"
UPDATE dbo.Logistica_ViajeParadas
SET Estatus=N'Cancelada',Activo=0,Secuencia=Secuencia+1000000+ViajeParadaID,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
WHERE ViajeParadaID=@ParadaID AND ViajeID=@ViajeID AND Activo=1;";
            await using var cmd = new SqlCommand(sqlQuitarParada, cn, tx);
            cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
            cmd.Parameters.Add("@ParadaID", SqlDbType.Int).Value = paradaId.Value;
            cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = relacion.ViajeID;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        const string sqlDesvincular = @"
UPDATE dbo.Logistica_ViajeEmbarques
SET Activo=0
WHERE ViajeEmbarqueID=@ViajeEmbarqueID AND Activo=1;

UPDATE dbo.Logistica_ViajeParadas
SET Secuencia=Secuencia+2000000
WHERE ViajeID=@ViajeID AND Activo=1;

;WITH Ordenadas AS
(
    SELECT ViajeParadaID,ROW_NUMBER() OVER(ORDER BY Secuencia,ViajeParadaID) NuevaSecuencia
    FROM dbo.Logistica_ViajeParadas
    WHERE ViajeID=@ViajeID AND Activo=1
)
UPDATE p
SET Secuencia=o.NuevaSecuencia,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
FROM dbo.Logistica_ViajeParadas p
INNER JOIN Ordenadas o ON o.ViajeParadaID=p.ViajeParadaID;

;WITH Entregas AS
(
    SELECT ViajeParadaID,ROW_NUMBER() OVER(ORDER BY Secuencia,ViajeParadaID) OrdenEntrega
    FROM dbo.Logistica_ViajeParadas
    WHERE ViajeID=@ViajeID AND Activo=1 AND TipoParada=N'Entrega'
)
UPDATE ve
SET OrdenEntrega=e.OrdenEntrega
FROM dbo.Logistica_ViajeEmbarques ve
INNER JOIN Entregas e ON e.ViajeParadaID=ve.ViajeParadaID
WHERE ve.ViajeID=@ViajeID AND ve.Activo=1;

UPDATE v
SET EsMultiParada=CASE WHEN x.TotalOperativas>1 THEN 1 ELSE 0 END,
FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
FROM dbo.Logistica_Viajes v
CROSS APPLY
(
    SELECT COUNT_BIG(*) TotalOperativas
    FROM dbo.Logistica_ViajeParadas p
    WHERE p.ViajeID=v.ViajeID AND p.Activo=1 AND p.TipoParada<>N'Origen' AND ISNULL(p.CierraViaje,0)=0
) x
WHERE v.ViajeID=@ViajeID AND v.Activo=1;";
        await using (var cmd = new SqlCommand(sqlDesvincular, cn, tx))
        {
            cmd.Parameters.Add("@ViajeEmbarqueID", SqlDbType.Int).Value = relacion.ViajeEmbarqueID;
            cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = relacion.ViajeID;
            cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        await InsertarHistorialViajeAsync(cn, tx, relacion.ViajeID, "EMBARQUE_RETIRADO_CAMBIO_FORMA", "Programado", "Programado", $"Embarque {embarqueId} retirado del recorrido. {motivo}", cancellationToken);
        return relacion.ViajeID;
    }
    private static async Task ValidarDisponibilidadProgramacionAsync(SqlConnection cn, SqlTransaction tx, DateTime fecha, TimeSpan hora, int unidadId, int choferId, int? embarqueExcluir, int? viajeExcluir, CancellationToken cancellationToken)
    {
        if (unidadId <= 0) throw new InvalidOperationException("La unidad seleccionada no es válida.");
        if (choferId <= 0) throw new InvalidOperationException("El chofer seleccionado no es válido.");
        var fechaHora = fecha.Date.Add(hora);
        const string sql = @"
SELECT TOP(1) Fuente,Folio,Recurso
FROM
(
    SELECT N'EMBARQUE' Fuente,ISNULL(e.Folio,N'') Folio,CASE WHEN e.ChoferUsuarioID=@ChoferID THEN N'CHOFER' ELSE N'UNIDAD' END Recurso
    FROM dbo.Logistica_Embarques e WITH(UPDLOCK,HOLDLOCK)
    WHERE e.Activo=1
    AND e.Estatus NOT IN(N'Entregado',N'Cancelado')
    AND ISNULL(e.FormaEnvio,N'')=N'Interno'
    AND (@EmbarqueExcluir IS NULL OR e.EmbarqueID<>@EmbarqueExcluir)
    AND e.FechaCargaProgramada=@Fecha
    AND e.HoraCargaProgramada=@Hora
    AND (e.ChoferUsuarioID=@ChoferID OR e.UnidadID=@UnidadID)
    UNION ALL
    SELECT N'VIAJE',ISNULL(v.Folio,N''),CASE WHEN v.OperadorUsuarioID=@ChoferID THEN N'CHOFER' ELSE N'UNIDAD' END
    FROM dbo.Logistica_Viajes v WITH(UPDLOCK,HOLDLOCK)
    WHERE v.Activo=1
    AND v.Estatus=N'Programado'
    AND (@ViajeExcluir IS NULL OR v.ViajeID<>@ViajeExcluir)
    AND v.FechaProgramada=@Fecha
    AND v.HoraSalidaProgramada=@Hora
    AND (v.OperadorUsuarioID=@ChoferID OR v.UnidadID=@UnidadID)
) X;";
        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@ChoferID", SqlDbType.Int).Value = choferId;
        cmd.Parameters.Add("@UnidadID", SqlDbType.Int).Value = unidadId;
        cmd.Parameters.Add("@Fecha", SqlDbType.Date).Value = fecha.Date;
        cmd.Parameters.Add("@Hora", SqlDbType.Time).Value = hora;
        cmd.Parameters.Add("@EmbarqueExcluir", SqlDbType.Int).Value = Db(embarqueExcluir);
        cmd.Parameters.Add("@ViajeExcluir", SqlDbType.Int).Value = Db(viajeExcluir);
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await rd.ReadAsync(cancellationToken)) return;
        var fuente = Texto(rd, "Fuente");
        var folio = Texto(rd, "Folio");
        var recurso = Texto(rd, "Recurso");
        var descripcion = fuente == "VIAJE" ? "viaje" : "embarque";
        if (recurso == "CHOFER") throw new InvalidOperationException($"El chofer ya tiene el {descripcion} {folio} programado exactamente para {fechaHora:dd/MM/yyyy HH:mm}. Cambia la hora o deja el chofer pendiente.");
        throw new InvalidOperationException($"La unidad ya tiene el {descripcion} {folio} programado exactamente para {fechaHora:dd/MM/yyyy HH:mm}. Cambia la hora o deja la unidad pendiente.");
    }

    private static async Task ValidarRecursosInternosDisponiblesAlIniciarAsync(SqlConnection cn, SqlTransaction tx, int viajeId, int embarqueId, int rutaId, int unidadId, int choferId, CancellationToken cancellationToken)
    {
        const string sqlRuta = @"SELECT COUNT_BIG(*) FROM dbo.Logistica_Rutas WITH(UPDLOCK,HOLDLOCK) WHERE RutaID=@RutaID AND Activo=1;";
        await using (var cmd = new SqlCommand(sqlRuta, cn, tx))
        {
            cmd.Parameters.Add("@RutaID", SqlDbType.Int).Value = rutaId;
            if (Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken)) <= 0) throw new InvalidOperationException("La ruta asignada ya no se encuentra activa.");
        }
        const string sqlUnidad = @"SELECT COUNT_BIG(*) FROM dbo.Logistica_Unidades WITH(UPDLOCK,HOLDLOCK) WHERE UnidadID=@UnidadID AND Activo=1;";
        await using (var cmd = new SqlCommand(sqlUnidad, cn, tx))
        {
            cmd.Parameters.Add("@UnidadID", SqlDbType.Int).Value = unidadId;
            if (Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken)) <= 0) throw new InvalidOperationException("La unidad asignada ya no se encuentra activa.");
        }
        const string sqlChofer = @"
SELECT COUNT_BIG(*)
FROM dbo.Usuarios U WITH(UPDLOCK,HOLDLOCK)
INNER JOIN dbo.Persona P ON P.PersonaID=U.PersonaID
INNER JOIN dbo.Departamentos D ON D.DepartamentoID=U.DepartamentoID
WHERE U.UsuarioID=@ChoferID AND U.Activo=1 AND D.Activo=1
AND UPPER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(D.NombreDepartamento,N''))),N'Í',N'I'),N'Ó',N'O'))=N'LOGISTICA'
AND UPPER(LTRIM(RTRIM(ISNULL(P.Puesto,N'')))) LIKE N'%CHOFER%';";
        await using (var cmd = new SqlCommand(sqlChofer, cn, tx))
        {
            cmd.Parameters.Add("@ChoferID", SqlDbType.Int).Value = choferId;
            if (Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken)) <= 0) throw new InvalidOperationException("El chofer asignado ya no es un usuario activo de Logística con puesto de Chofer.");
        }
        const string sqlOcupado = @"
SELECT TOP(1) Fuente,Folio,Recurso
FROM
(
    SELECT N'VIAJE' Fuente,ISNULL(v.Folio,N'') Folio,CASE WHEN v.OperadorUsuarioID=@ChoferID THEN N'CHOFER' ELSE N'UNIDAD' END Recurso,ISNULL(v.FechaSalidaReal,CAST(v.FechaProgramada AS datetime2)) FechaOrden
    FROM dbo.Logistica_Viajes v WITH(UPDLOCK,HOLDLOCK)
    WHERE v.Activo=1 AND v.ViajeID<>@ViajeID AND v.Estatus=N'En curso' AND v.FechaRegresoReal IS NULL AND (v.OperadorUsuarioID=@ChoferID OR v.UnidadID=@UnidadID)
    UNION ALL
    SELECT N'EMBARQUE',ISNULL(e.Folio,N''),CASE WHEN e.ChoferUsuarioID=@ChoferID THEN N'CHOFER' ELSE N'UNIDAD' END,ISNULL(e.FechaSalida,CAST(e.FechaCargaProgramada AS datetime2))
    FROM dbo.Logistica_Embarques e WITH(UPDLOCK,HOLDLOCK)
    WHERE e.Activo=1 AND e.EmbarqueID<>@EmbarqueID AND ISNULL(e.FormaEnvio,N'')=N'Interno' AND e.Estatus=N'En ruta' AND (e.ChoferUsuarioID=@ChoferID OR e.UnidadID=@UnidadID)
    AND NOT EXISTS
    (
        SELECT 1
        FROM dbo.Logistica_ViajeEmbarques ve
        INNER JOIN dbo.Logistica_Viajes vx ON vx.ViajeID=ve.ViajeID AND vx.Activo=1 AND vx.Estatus=N'En curso' AND vx.FechaRegresoReal IS NULL
        WHERE ve.EmbarqueID=e.EmbarqueID AND ve.Activo=1
    )
) X
ORDER BY FechaOrden;";
        await using var cmdOcupado = new SqlCommand(sqlOcupado, cn, tx);
        cmdOcupado.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
        cmdOcupado.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
        cmdOcupado.Parameters.Add("@ChoferID", SqlDbType.Int).Value = choferId;
        cmdOcupado.Parameters.Add("@UnidadID", SqlDbType.Int).Value = unidadId;
        await using var rd = await cmdOcupado.ExecuteReaderAsync(cancellationToken);
        if (!await rd.ReadAsync(cancellationToken)) return;
        var fuente = Texto(rd, "Fuente");
        var folio = Texto(rd, "Folio");
        var recurso = Texto(rd, "Recurso");
        var descripcion = fuente == "VIAJE" ? "viaje" : "embarque";
        if (recurso == "CHOFER") throw new InvalidOperationException($"El chofer todavía se encuentra atendiendo el {descripcion} {folio}. Debe registrar su retorno antes de iniciar esta salida.");
        throw new InvalidOperationException($"La unidad todavía se encuentra ocupada por el {descripcion} {folio}. Debe registrar su retorno antes de iniciar esta salida.");
    }
    private static async Task<string> ObtenerNombreChoferFlujoAsync(SqlConnection cn, SqlTransaction tx, int usuarioId, CancellationToken cancellationToken)
    {
        if (usuarioId <= 0) throw new InvalidOperationException("El chofer seleccionado no es válido.");
        const string sql = @"
SELECT TOP(1) LTRIM(RTRIM(CONCAT(ISNULL(P.Nombre,N''),N' ',ISNULL(P.ApellidoPaterno,N''),N' ',ISNULL(P.ApellidoMaterno,N''))))
FROM dbo.Usuarios U WITH(UPDLOCK,HOLDLOCK)
INNER JOIN dbo.Persona P ON P.PersonaID=U.PersonaID
INNER JOIN dbo.Departamentos D ON D.DepartamentoID=U.DepartamentoID
WHERE U.UsuarioID=@UsuarioID
AND U.Activo=1
AND D.Activo=1
AND UPPER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(D.NombreDepartamento,N''))),N'Í',N'I'),N'Ó',N'O'))=N'LOGISTICA'
AND UPPER(LTRIM(RTRIM(ISNULL(P.Puesto,N'')))) LIKE N'%CHOFER%';";
        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
        var resultado = await cmd.ExecuteScalarAsync(cancellationToken);
        var nombre = resultado == null || resultado == DBNull.Value ? string.Empty : Convert.ToString(resultado)?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(nombre)) throw new InvalidOperationException("El chofer seleccionado no pertenece a Logística, está inactivo o no tiene un puesto de Chofer.");
        return nombre;
    }
    private async Task<List<string>> ObtenerDocumentosFaltantesFlujoAsync(SqlConnection cn, int embarqueId, string tipoOperacion, string formaEnvio, string? modalidadEnvio, bool? pasaAduana, CancellationToken cancellationToken)
    {
        var requeridos = ObtenerDefinicionDocumentosFlujo(tipoOperacion, formaEnvio, modalidadEnvio, pasaAduana).Where(x => x.Obligatorio).ToList();
        var validados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        const string sql = "SELECT DISTINCT ISNULL(TipoDocumento,N'') TipoDocumento FROM dbo.Logistica_EmbarqueDocumentos WHERE EmbarqueID=@EmbarqueID AND Activo=1 AND Validado=1;";
        await using (var cmd = new SqlCommand(sql, cn))
        {
            cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await rd.ReadAsync(cancellationToken))
            {
                var tipo = NormalizarTipoDocumentoFlujo(Texto(rd, "TipoDocumento"));
                if (!string.IsNullOrWhiteSpace(tipo)) validados.Add(tipo);
            }
        }
        var faltantes = new List<string>();
        foreach (var requerido in requeridos) if (!DocumentoCumpleRequisitoFlujo(requerido.TipoDocumento, validados)) faltantes.Add(requerido.TipoDocumento);
        return faltantes;
    }

    private async Task ValidarDocumentacionCargaAsync(SqlConnection cn, SqlTransaction tx, int embarqueId, string tipoOperacion, string formaEnvio, string? modalidadEnvio, bool? pasaAduana, CancellationToken cancellationToken)
    {
        var requeridos = ObtenerDefinicionDocumentosFlujo(tipoOperacion, formaEnvio, modalidadEnvio, pasaAduana).Where(x => x.Obligatorio).ToList();
        if (requeridos.Count == 0) throw new InvalidOperationException("No se pudo determinar la documentación obligatoria. Verifica primero la forma de salida.");
        var validados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        const string sql = @"
SELECT DISTINCT ISNULL(TipoDocumento,N'') TipoDocumento
FROM dbo.Logistica_EmbarqueDocumentos WITH(UPDLOCK,HOLDLOCK)
WHERE EmbarqueID=@EmbarqueID AND Activo=1 AND Validado=1;";
        await using (var cmd = new SqlCommand(sql, cn, tx))
        {
            cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await rd.ReadAsync(cancellationToken))
            {
                var tipo = NormalizarTipoDocumentoFlujo(Texto(rd, "TipoDocumento"));
                if (!string.IsNullOrWhiteSpace(tipo)) validados.Add(tipo);
            }
        }
        var faltantes = requeridos.Where(x => !DocumentoCumpleRequisitoFlujo(x.TipoDocumento, validados)).Select(x => x.TipoDocumento).ToList();
        if (faltantes.Count > 0) throw new InvalidOperationException($"No puede iniciar la carga física. Faltan documentos obligatorios validados: {string.Join(", ", faltantes)}.");
    }

    private async Task ValidarDocumentacionSalidaPlantaAsync(SqlConnection cn, SqlTransaction tx, int embarqueId, string tipoOperacion, string formaEnvio, string? modalidadEnvio, bool? pasaAduana, CancellationToken cancellationToken)
    {
        var requeridos = ObtenerDefinicionDocumentosFlujo(tipoOperacion, formaEnvio, modalidadEnvio, pasaAduana)
            .Where(x => x.Obligatorio)
            .ToList();

        if (requeridos.Count == 0)
            throw new InvalidOperationException("No se pudo determinar la documentación obligatoria para confirmar la salida de planta.");

        var validados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        const string sql = @"
SELECT DISTINCT ISNULL(TipoDocumento,N'') TipoDocumento
FROM dbo.Logistica_EmbarqueDocumentos WITH(UPDLOCK,HOLDLOCK)
WHERE EmbarqueID=@EmbarqueID
AND Activo=1
AND Validado=1;";

        await using (var cmd = new SqlCommand(sql, cn, tx))
        {
            cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;

            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);

            while (await rd.ReadAsync(cancellationToken))
            {
                var tipo = NormalizarTipoDocumentoFlujo(Texto(rd, "TipoDocumento"));

                if (!string.IsNullOrWhiteSpace(tipo))
                    validados.Add(tipo);
            }
        }

        var faltantes = requeridos
            .Where(x => !DocumentoCumpleRequisitoFlujo(x.TipoDocumento, validados))
            .Select(x => x.TipoDocumento)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (faltantes.Count > 0)
            throw new InvalidOperationException($"No se puede confirmar la salida de planta. Faltan documentos obligatorios validados: {string.Join(", ", faltantes)}.");
    }

    private async Task ActualizarListaCargaSalidaOperacionAsync(SqlConnection cn, SqlTransaction tx, int embarqueId, CancellationToken cancellationToken)
    {
        const string sql = @"
UPDATE x
SET CantidadEnviada=x.CantidadAsignada,
    FechaModificacion=SYSDATETIME(),
    ActualizadoPor=@Usuario
FROM dbo.Logistica_ListaCargaProgramacionEmbarques x
WHERE x.EmbarqueID=@EmbarqueID
AND x.Activo=1;

UPDATE p
SET Estatus=
CASE
    WHEN ISNULL(t.Enviado,0)>=p.CantidadProgramada THEN N'Cumplida'
    WHEN ISNULL(t.Enviado,0)>0 THEN N'Parcial'
    WHEN ISNULL(t.Generado,0)>=p.CantidadProgramada THEN N'Generada'
    ELSE N'Programada'
END,
FechaModificacion=SYSDATETIME(),
ActualizadoPor=@Usuario
FROM dbo.Logistica_ListaCargaProgramacion p
OUTER APPLY
(
    SELECT
        SUM(x.CantidadAsignada) Generado,
        SUM(x.CantidadEnviada) Enviado
    FROM dbo.Logistica_ListaCargaProgramacionEmbarques x
    WHERE x.ListaCargaProgramacionID=p.ListaCargaProgramacionID
    AND x.Activo=1
) t
WHERE EXISTS
(
    SELECT 1
    FROM dbo.Logistica_ListaCargaProgramacionEmbarques x
    WHERE x.ListaCargaProgramacionID=p.ListaCargaProgramacionID
    AND x.EmbarqueID=@EmbarqueID
    AND x.Activo=1
);";

        await using var cmd = new SqlCommand(sql, cn, tx);

        cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
        cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
    private async Task<(long TotalSolicitado, long PiezasCargadas, long PiezasReservadas, int CajasAsignadas, int CajasCargadas)> ObtenerResumenCargaFisicaAsync(SqlConnection cn, SqlTransaction? tx, int embarqueId, CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT
ISNULL((SELECT SUM(CONVERT(bigint,d.CantidadSolicitada)) FROM dbo.Logistica_EmbarqueDetalle d WHERE d.EmbarqueID=@EmbarqueID AND d.Activo=1),0) TotalSolicitado,
ISNULL((SELECT SUM(CONVERT(bigint,ec.CantidadAsignada)) FROM dbo.Logistica_EmbarqueCajas ec WHERE ec.EmbarqueID=@EmbarqueID AND ec.Activo=1 AND ec.EstatusSeleccion IN(N'Cargada',N'Despachada')),0) PiezasCargadas,
ISNULL((SELECT SUM(CONVERT(bigint,ec.CantidadAsignada)) FROM dbo.Logistica_EmbarqueCajas ec WHERE ec.EmbarqueID=@EmbarqueID AND ec.Activo=1 AND ec.EstatusSeleccion=N'Reservada'),0) PiezasReservadas,
ISNULL((SELECT COUNT_BIG(DISTINCT ec.CajaID) FROM dbo.Logistica_EmbarqueCajas ec WHERE ec.EmbarqueID=@EmbarqueID AND ec.Activo=1 AND ec.EstatusSeleccion IN(N'Reservada',N'Cargada',N'Despachada')),0) CajasAsignadas,
ISNULL((SELECT COUNT_BIG(DISTINCT ec.CajaID) FROM dbo.Logistica_EmbarqueCajas ec WHERE ec.EmbarqueID=@EmbarqueID AND ec.Activo=1 AND ec.EstatusSeleccion IN(N'Cargada',N'Despachada')),0) CajasCargadas;";
        await using var cmd = new SqlCommand(sql, cn);
        if (tx != null) cmd.Transaction = tx;
        cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await rd.ReadAsync(cancellationToken)) return (0, 0, 0, 0, 0);
        return (
            EnteroLargo(rd, "TotalSolicitado"),
            EnteroLargo(rd, "PiezasCargadas"),
            EnteroLargo(rd, "PiezasReservadas"),
            Convert.ToInt32(EnteroLargo(rd, "CajasAsignadas")),
            Convert.ToInt32(EnteroLargo(rd, "CajasCargadas"))
        );
    }
    private static List<(string TipoDocumento, string AreaResponsable, bool Obligatorio)> ObtenerDefinicionDocumentosFlujo(string? tipoOperacion, string? formaEnvio, string? modalidadEnvio = null, bool? pasaAduana = null)
    {
        var tipo = NormalizarTipoOperacionFlujo(tipoOperacion);
        var forma = NormalizarFormaEnvio(formaEnvio);
        var modalidad = NormalizarModalidadEnvioFlujo(modalidadEnvio);
        if (tipo == "Pendiente" || forma == "Pendiente" || string.IsNullOrWhiteSpace(tipo) || string.IsNullOrWhiteSpace(forma)) return new();
        var documentos = new List<(string TipoDocumento, string AreaResponsable, bool Obligatorio)>();
        if (forma == "Interno") documentos.Add(("Factura firmada y sellada / Remisión", "Finanzas", true));
        else if (forma == "Cliente") documentos.Add(("Factura firmada por transportista / Remisión", "Finanzas", true));
        else if (forma == "Paqueteria")
        {
            documentos.Add(("Factura firmada por transportista / Remisión", "Finanzas", true));
            documentos.Add(("Guía", "Logística", true));
            documentos.Add(("Packing List", "Planeación", true));
            documentos.Add(("Carta Porte", "Planeación", true));
        }
        if (tipo == "Exportacion")
        {
            if (forma != "Paqueteria")
            {
                documentos.Add(("Commercial Invoice / Carta Porte", "Planeación", true));
                documentos.Add(("Packing List", "Planeación", true));
            }
            if (pasaAduana == true && (modalidad == "Aereo" || modalidad == "Maritimo")) documentos.Add(("Carta de instrucciones", "Planeación", true));
            if (pasaAduana == false && forma != "Paqueteria") documentos.Add(("Guía", "Logística", true));
            documentos.Add(("XML", "Finanzas", false));
            documentos.Add(("Booking", "Planeación", false));
            documentos.Add(("Pedimento", "Aduanas", false));
        }
        return documentos.GroupBy(x => x.TipoDocumento, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).ToList();
    }

    private async Task<List<(int ParteID, string NumeroParte, long Requerido, long Libre, long Reservado, long Disponible, long Faltante, bool Suficiente)>> ObtenerDisponibilidadPtPorParteAsync(SqlConnection cn, int embarqueId, CancellationToken cancellationToken)
    {
        const string sql = @"
WITH Requerido AS
(
    SELECT d.ParteID,MAX(ISNULL(NULLIF(LTRIM(RTRIM(d.NumeroParteSnapshot)),N''),p.NumeroParte)) NumeroParte,SUM(CONVERT(bigint,ISNULL(d.CantidadSolicitada,0))) Requerido
    FROM dbo.Logistica_EmbarqueDetalle d
    INNER JOIN dbo.ERP_Partes p ON p.ParteID=d.ParteID
    INNER JOIN dbo.Logistica_Embarques e ON e.EmbarqueID=d.EmbarqueID AND e.Activo=1 AND e.ClienteID=p.ClienteID
    WHERE d.EmbarqueID=@EmbarqueID AND d.Activo=1 AND d.CantidadSolicitada>0
    GROUP BY d.ParteID
)
SELECT r.ParteID,r.NumeroParte,r.Requerido,ISNULL(l.Libre,0) Libre,ISNULL(a.Reservado,0) Reservado,ISNULL(l.Libre,0)+ISNULL(a.Reservado,0) Disponible
FROM Requerido r
OUTER APPLY
(
    SELECT ISNULL(SUM(CONVERT(bigint,c.Disponible)),0) Libre
    FROM dbo.vw_Logistica_CajasDisponibles c
    INNER JOIN dbo.ERP_Partes p ON p.ParteID=c.ParteID
    INNER JOIN dbo.Logistica_Embarques e ON e.EmbarqueID=@EmbarqueID AND e.Activo=1 AND e.ClienteID=p.ClienteID
    WHERE c.ParteID=r.ParteID AND c.Disponible>0
) l
OUTER APPLY
(
    SELECT ISNULL(SUM(CONVERT(bigint,ec.CantidadAsignada)),0) Reservado
    FROM dbo.Logistica_EmbarqueCajas ec
    INNER JOIN dbo.AlmacenPT_Cajas c ON c.CajaID=ec.CajaID AND c.Activo=1
    INNER JOIN dbo.ERP_Partes p ON p.ParteID=c.ParteID
    INNER JOIN dbo.Logistica_Embarques e ON e.EmbarqueID=ec.EmbarqueID AND e.Activo=1 AND e.ClienteID=p.ClienteID
    WHERE ec.EmbarqueID=@EmbarqueID AND ec.Activo=1 AND ec.EstatusSeleccion IN(N'Reservada',N'Cargada',N'Despachada') AND c.ParteID=r.ParteID
) a
ORDER BY r.NumeroParte,r.ParteID;";
        var resultado = new List<(int ParteID, string NumeroParte, long Requerido, long Libre, long Reservado, long Disponible, long Faltante, bool Suficiente)>();
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await rd.ReadAsync(cancellationToken))
        {
            var requerido = EnteroLargo(rd, "Requerido");
            var libre = EnteroLargo(rd, "Libre");
            var reservado = EnteroLargo(rd, "Reservado");
            var disponible = EnteroLargo(rd, "Disponible");
            resultado.Add((Entero(rd, "ParteID"), Texto(rd, "NumeroParte"), requerido, libre, reservado, disponible, Math.Max(0, requerido - disponible), disponible >= requerido));
        }
        return resultado;
    }

   

    private static bool DocumentoCumpleRequisitoFlujo(string requisito, IReadOnlyCollection<string> validados)
    {
        if (requisito.Equals("Factura firmada y sellada / Remisión", StringComparison.OrdinalIgnoreCase) || requisito.Equals("Factura firmada por transportista / Remisión", StringComparison.OrdinalIgnoreCase))
            return validados.Contains("Factura", StringComparer.OrdinalIgnoreCase) || validados.Contains("Remisión", StringComparer.OrdinalIgnoreCase);
        if (requisito.Equals("Commercial Invoice / Carta Porte", StringComparison.OrdinalIgnoreCase))
            return validados.Contains("Commercial Invoice", StringComparer.OrdinalIgnoreCase) || validados.Contains("Carta Porte", StringComparer.OrdinalIgnoreCase);
        return validados.Contains(requisito, StringComparer.OrdinalIgnoreCase);
    }

    private static object UnidadEventoJson(LogisticaOperacionUnidadEventoVm e)
    {
        return new
        {
            e.ViajeID,
            e.Folio,
            e.TipoViaje,
            e.TipoTransporte,
            e.UnidadID,
            e.NumeroEconomico,
            e.OperadorUsuarioID,
            e.Operador,
            e.RutaID,
            e.Ruta,
            e.Origen,
            e.Destino,
            e.Motivo,
            fechaProgramada = e.FechaProgramada.ToString("yyyy-MM-dd"),
            horaSalidaProgramada = e.HoraSalidaProgramada?.ToString(@"hh\:mm"),
            fechaSalidaReal = e.FechaSalidaReal?.ToString("yyyy-MM-ddTHH:mm:ss"),
            fechaRegresoReal = e.FechaRegresoReal?.ToString("yyyy-MM-ddTHH:mm:ss"),
            e.KilometrajeSalida,
            e.KilometrajeRegreso,
            e.KilometrosRecorridos,
            e.Estatus,
            e.EstadoVisual,
            e.TieneIncidencia,
            e.IncidenciasAbiertas,
            e.IncidenciasCriticas,
            e.EvidenciasSalida,
            e.EvidenciasRegreso,
            e.EvidenciasTrayecto,
            e.EvidenciasIncidencia,
            e.TotalEmbarques,
            e.TotalPiezas,
            e.ClienteResumen,
            e.HoraTexto,
            e.EsViajeActivo,
            e.BloqueaDisponibilidad,
            e.Icono,
            e.ClaseEstatus,
            e.RowVersion,
            embarques = e.Embarques.Select(x => new
            {
                x.ViajeEmbarqueID,
                x.EmbarqueID,
                x.OrdenEntrega,
                x.Folio,
                x.ClienteID,
                x.Cliente,
                x.Destino,
                x.Estatus,
                x.TotalPartidas,
                x.TotalPiezas,
                fechaCargaProgramada = x.FechaCargaProgramada?.ToString("yyyy-MM-dd"),
                horaCargaProgramada = x.HoraCargaProgramada?.ToString(@"hh\:mm"),
                fechaEntregaProgramada = x.FechaEntregaProgramada?.ToString("yyyy-MM-dd")
            }).ToList()
        };
    }

    // 5) AGREGAR: helper necesario para CapacidadPesoKg.
    private static decimal? DecimalNullable(SqlDataReader rd, string columna)
    {
        var i = rd.GetOrdinal(columna);
        return rd.IsDBNull(i) ? null : Convert.ToDecimal(rd.GetValue(i));
    }

    private static string NormalizarTipoDocumentoFlujo(string? valor)
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

    private static string NormalizarTipoOperacionFlujo(string? valor)
    {
        valor = valor?.Trim() ?? string.Empty;
        if (valor.Equals("Nacional", StringComparison.OrdinalIgnoreCase)) return "Nacional";
        if (valor.Equals("Exportacion", StringComparison.OrdinalIgnoreCase) || valor.Equals("Exportación", StringComparison.OrdinalIgnoreCase)) return "Exportacion";
        if (valor.Equals("Pendiente", StringComparison.OrdinalIgnoreCase) || valor.Equals("Por definir", StringComparison.OrdinalIgnoreCase)) return "Pendiente";
        return string.Empty;
    }

    private static string NormalizarModalidadEnvioFlujo(string? valor)
    {
        valor = valor?.Trim() ?? string.Empty;
        if (valor.Equals("Terrestre", StringComparison.OrdinalIgnoreCase)) return "Terrestre";
        if (valor.Equals("Aereo", StringComparison.OrdinalIgnoreCase) || valor.Equals("Aéreo", StringComparison.OrdinalIgnoreCase)) return "Aereo";
        if (valor.Equals("Maritimo", StringComparison.OrdinalIgnoreCase) || valor.Equals("Marítimo", StringComparison.OrdinalIgnoreCase)) return "Maritimo";
        return string.Empty;
    }
    private static byte[] Bytes(
        SqlDataReader rd,
        string columna)
    {
        var i =
            rd.GetOrdinal(
                columna);

        return rd.IsDBNull(i)
            ? Array.Empty<byte>()
            : (byte[])rd.GetValue(i);
    }
}