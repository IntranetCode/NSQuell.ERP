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
ISNULL((SELECT SUM(x.CantidadAsignada) FROM dbo.Logistica_ListaCargaProgramacionEmbarques x WHERE x.ListaCargaProgramacionID=p.ListaCargaProgramacionID AND x.Activo=1),0) CantidadGenerada
FROM dbo.Logistica_ListaCargaProgramacion p WITH(UPDLOCK,HOLDLOCK)
WHERE p.ListaCargaProgramacionID=@ID AND p.Activo=1;";
            DateTime fechaOriginal, fechaAnterior;
            int cantidadGenerada;
            string estatus;
            await using (var cmd = new SqlCommand(sqlActual, cn, tx))
            {
                cmd.Parameters.Add("@ID", SqlDbType.Int).Value = model.ListaCargaProgramacionID;
                await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
                if (!await rd.ReadAsync(cancellationToken)) throw new InvalidOperationException("La programación ya no existe.");
                fechaOriginal = (Fecha(rd, "FechaRequeridaOriginal") ?? DateTime.MinValue).Date;
                fechaAnterior = (Fecha(rd, "FechaProgramadaCarga") ?? DateTime.MinValue).Date;
                cantidadGenerada = Entero(rd, "CantidadGenerada");
                estatus = Texto(rd, "Estatus");
            }
            if (estatus.Equals("Cancelada", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("La programación está cancelada.");
            if (cantidadGenerada > 0) throw new InvalidOperationException("La programación ya fue convertida total o parcialmente a embarque. Debes mover el embarque.");
            if (fechaAnterior == model.FechaProgramadaCarga.Date) throw new InvalidOperationException("La programación ya se encuentra en ese día.");
            var semanaId = await ObtenerOCrearSemanaIdAsync(cn, tx, model.FechaProgramadaCarga.Date, cancellationToken);
            const string sqlUpdate = @"
UPDATE dbo.Logistica_ListaCargaProgramacion
SET ListaCargaSemanaOrigenID=@SemanaID,
    FechaProgramadaCarga=@FechaCarga,
    HoraProgramadaCarga=NULL,
    Observaciones=CASE WHEN @Observaciones IS NULL THEN Observaciones WHEN NULLIF(LTRIM(RTRIM(ISNULL(Observaciones,N''))),N'') IS NULL THEN @Observaciones ELSE CONCAT(Observaciones,NCHAR(13),NCHAR(10),@Observaciones) END,
    FechaModificacion=SYSDATETIME(),
    ActualizadoPor=@Usuario
WHERE ListaCargaProgramacionID=@ID AND Activo=1;";
            await using (var cmd = new SqlCommand(sqlUpdate, cn, tx))
            {
                cmd.Parameters.Add("@SemanaID", SqlDbType.Int).Value = semanaId;
                cmd.Parameters.Add("@FechaCarga", SqlDbType.Date).Value = model.FechaProgramadaCarga.Date;
                cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1000).Value = Db(model.Observaciones);
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@ID", SqlDbType.Int).Value = model.ListaCargaProgramacionID;
                if (await cmd.ExecuteNonQueryAsync(cancellationToken) != 1) throw new InvalidOperationException("No fue posible mover la programación.");
            }
            await tx.CommitAsync(cancellationToken);
            return Json(new { ok = true, mensaje = $"Programación movida del {fechaAnterior:dd/MM/yyyy} al {model.FechaProgramadaCarga:dd/MM/yyyy}.", programacionId = model.ListaCargaProgramacionID, fechaRequerida = fechaOriginal.ToString("yyyy-MM-dd"), fechaProgramada = model.FechaProgramadaCarga.ToString("yyyy-MM-dd"), horaProgramada = (string?)null });
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
                        x.TipoEvidencia,
                        x.NombreOriginal,
                        x.TipoContenido,
                        fechaCarga = x.FechaCarga.ToString("dd/MM/yyyy HH:mm"),
                        x.Usuario
                    }).ToList()
                }
            });
        }
        catch (SqlException ex)
        {
            return StatusCode(500, new
            {
                ok = false,
                tipo = "SQL",
                mensaje = ex.Message,
                numero = ex.Number,
                embarqueId
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                ok = false,
                tipo = "GENERAL",
                mensaje = ex.Message,
                embarqueId
            });
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
SELECT ISNULL(Folio,N'') Folio,ISNULL(ClienteNombreSnapshot,N'') Cliente,ISNULL(Estatus,N'') Estatus,FechaCargaProgramada,HoraCargaProgramada,
ISNULL(FormaEnvio,N'Pendiente') FormaEnvio,UnidadID,ChoferUsuarioID,CONVERT(varbinary(8),RowVersion) RowVersion
FROM dbo.Logistica_Embarques WITH(UPDLOCK,HOLDLOCK)
WHERE EmbarqueID=@EmbarqueID AND Activo=1;";
            string folio, cliente, estatus, formaEnvio;
            DateTime? fechaAnterior;
            TimeSpan? horaAnterior;
            int? unidadId, choferId;
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
                formaEnvio = NormalizarFormaEnvio(Texto(rd, "FormaEnvio"));
                unidadId = EnteroNullable(rd, "UnidadID");
                choferId = EnteroNullable(rd, "ChoferUsuarioID");
                rowVersionActual = Bytes(rd, "RowVersion");
            }
            if (!rowVersionActual.SequenceEqual(rowVersionOriginal))
            {
                await tx.RollbackAsync(cancellationToken);
                return Conflict(new { ok = false, recargar = true, mensaje = "Este embarque fue modificado por otro usuario. Recarga el calendario." });
            }
            if (estatus is not "Programado" and not "Preparando") throw new InvalidOperationException($"El embarque {folio} no puede moverse porque está en estatus {estatus}.");
            if (fechaAnterior?.Date == model.FechaCargaProgramada.Date && horaAnterior == model.HoraCargaProgramada) throw new InvalidOperationException("El embarque ya se encuentra en esa fecha y hora.");
            if (formaEnvio == "Interno" && unidadId.HasValue)
            {
                const string sqlUnidadOcupada = @"
SELECT TOP(1) e.Folio
FROM dbo.Logistica_Embarques e WITH(UPDLOCK,HOLDLOCK)
WHERE e.Activo=1
AND e.EmbarqueID<>@EmbarqueID
AND e.UnidadID=@UnidadID
AND ISNULL(e.FormaEnvio,N'')=N'Interno'
AND e.Estatus NOT IN(N'Entregado',N'Cancelado')
AND e.FechaCargaProgramada=@Fecha
AND e.HoraCargaProgramada=@Hora;";
                await using var cmdUnidad = new SqlCommand(sqlUnidadOcupada, cn, tx);
                cmdUnidad.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = model.EmbarqueID;
                cmdUnidad.Parameters.Add("@UnidadID", SqlDbType.Int).Value = unidadId.Value;
                cmdUnidad.Parameters.Add("@Fecha", SqlDbType.Date).Value = model.FechaCargaProgramada.Date;
                cmdUnidad.Parameters.Add("@Hora", SqlDbType.Time).Value = model.HoraCargaProgramada.Value;
                var conflicto = await cmdUnidad.ExecuteScalarAsync(cancellationToken);
                if (conflicto != null && conflicto != DBNull.Value) throw new InvalidOperationException($"La unidad seleccionada ya está ocupada en ese horario por el embarque {conflicto}.");
            }
            if (formaEnvio == "Interno" && choferId.HasValue)
            {
                const string sqlChoferOcupado = @"
SELECT TOP(1) e.Folio
FROM dbo.Logistica_Embarques e WITH(UPDLOCK,HOLDLOCK)
WHERE e.Activo=1
AND e.EmbarqueID<>@EmbarqueID
AND e.ChoferUsuarioID=@ChoferID
AND ISNULL(e.FormaEnvio,N'')=N'Interno'
AND e.Estatus NOT IN(N'Entregado',N'Cancelado')
AND e.FechaCargaProgramada=@Fecha
AND e.HoraCargaProgramada=@Hora;";
                await using var cmdChofer = new SqlCommand(sqlChoferOcupado, cn, tx);
                cmdChofer.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = model.EmbarqueID;
                cmdChofer.Parameters.Add("@ChoferID", SqlDbType.Int).Value = choferId.Value;
                cmdChofer.Parameters.Add("@Fecha", SqlDbType.Date).Value = model.FechaCargaProgramada.Date;
                cmdChofer.Parameters.Add("@Hora", SqlDbType.Time).Value = model.HoraCargaProgramada.Value;
                var conflicto = await cmdChofer.ExecuteScalarAsync(cancellationToken);
                if (conflicto != null && conflicto != DBNull.Value) throw new InvalidOperationException($"El chofer seleccionado ya está asignado en ese horario al embarque {conflicto}.");
            }
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
                cmd.Parameters.Add("@RowVersion", SqlDbType.Timestamp).Value = rowVersionOriginal;
                var resultado = await cmd.ExecuteScalarAsync(cancellationToken);
                if (resultado == null || resultado == DBNull.Value)
                {
                    await tx.RollbackAsync(cancellationToken);
                    return Conflict(new { ok = false, recargar = true, mensaje = "El embarque cambió mientras se intentaba mover." });
                }
                nuevaRowVersion = (byte[])resultado;
            }
            var anterior = FormatearFechaHora(fechaAnterior, horaAnterior);
            var nueva = FormatearFechaHora(model.FechaCargaProgramada, model.HoraCargaProgramada);
            var historial = $"Reprogramación mediante arrastre en Centro Operativo. Cliente: {cliente}. Carga: {anterior} → {nueva}.";
            if (!string.IsNullOrWhiteSpace(model.Observaciones)) historial += $" Observaciones: {model.Observaciones}";
            await InsertarHistorialAsync(cn, tx, model.EmbarqueID, "REPROGRAMACION_CALENDARIO", estatus, estatus, historial, cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return Json(new { ok = true, mensaje = $"{folio} reprogramado a {nueva}.", embarqueId = model.EmbarqueID, fechaCargaProgramada = model.FechaCargaProgramada.ToString("yyyy-MM-dd"), horaCargaProgramada = model.HoraCargaProgramada.Value.ToString(@"hh\:mm"), rowVersion = Convert.ToBase64String(nuevaRowVersion) });
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(cancellationToken); } catch { }
            return BadRequest(new { ok = false, mensaje = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(10_485_760)]
    public async Task<IActionResult> CerrarConEvidencia(
        LogisticaOperacionCerrarVm model,
        CancellationToken cancellationToken = default)
    {
        var acceso = await ValidarAccesoAsync();
        if (acceso != null) return acceso;

        model.ReceptorNombre =
            model.ReceptorNombre?.Trim()
            ?? string.Empty;

        model.FolioRemision =
            model.FolioRemision?.Trim();

        model.Observaciones =
            model.Observaciones?.Trim();

        model.ObservacionesEvidencia =
            model.ObservacionesEvidencia?.Trim();

        if (model.EmbarqueID <= 0)
            return BadRequest(new
            {
                ok = false,
                mensaje =
                    "El embarque indicado no es válido."
            });

        if (string.IsNullOrWhiteSpace(
                model.ReceptorNombre))
        {
            return BadRequest(new
            {
                ok = false,
                mensaje =
                    "Captura quién recibió la mercancía."
            });
        }

        if (model.FechaEntrega == default)
            return BadRequest(new
            {
                ok = false,
                mensaje =
                    "Captura la fecha y hora de entrega."
            });

        if (model.FechaEntrega >
            DateTime.Now.AddMinutes(5))
        {
            return BadRequest(new
            {
                ok = false,
                mensaje =
                    "La fecha de entrega no puede estar en el futuro."
            });
        }

        if (model.Evidencia == null ||
            model.Evidencia.Length <= 0)
        {
            return BadRequest(new
            {
                ok = false,
                mensaje =
                    "Adjunta la evidencia de entrega antes de cerrar."
            });
        }

        if (string.IsNullOrWhiteSpace(
            model.RowVersion))
        {
            return Conflict(new
            {
                ok = false,
                recargar = true,
                mensaje =
                    "No se recibió la versión del embarque. Recarga el calendario."
            });
        }

        byte[] rowVersionOriginal;

        try
        {
            rowVersionOriginal =
                Convert.FromBase64String(
                    model.RowVersion);
        }
        catch
        {
            return Conflict(new
            {
                ok = false,
                recargar = true,
                mensaje =
                    "La versión del embarque no es válida."
            });
        }

        var validacion =
            ValidarEvidencia(
                model.Evidencia);

        if (!validacion.Ok)
            return BadRequest(new
            {
                ok = false,
                mensaje =
                    validacion.Mensaje
            });

        var nombreOriginal =
            Path.GetFileName(
                model.Evidencia.FileName);

        var extension =
            Path.GetExtension(
                nombreOriginal)
                .ToLowerInvariant();

        var tipoContenido =
            model.Evidencia.ContentType?
                .Trim()
            ?? string.Empty;

        string? rutaFisicaCreada = null;

        await using var cn =
            await AbrirAsync(
                cancellationToken);

        await using var tx =
            (SqlTransaction)await cn
                .BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);

        try
        {
            const string sqlHeader = @"
SELECT
    ISNULL(Folio,N'') AS Folio,
    ISNULL(Estatus,N'') AS Estatus,
    FechaSalida,
    CONVERT(varbinary(8),RowVersion) AS RowVersion
FROM dbo.Logistica_Embarques WITH(UPDLOCK,HOLDLOCK)
WHERE EmbarqueID=@EmbarqueID
AND Activo=1;";

            string folio;
            string estatus;
            DateTime? fechaSalida;
            byte[] rowVersionActual;

            await using (var cmd =
                new SqlCommand(
                    sqlHeader,
                    cn,
                    tx))
            {
                cmd.Parameters.Add(
                    "@EmbarqueID",
                    SqlDbType.Int).Value =
                    model.EmbarqueID;

                await using var rd =
                    await cmd.ExecuteReaderAsync(
                        cancellationToken);

                if (!await rd.ReadAsync(
                        cancellationToken))
                {
                    throw new InvalidOperationException(
                        "El embarque no existe.");
                }

                folio =
                    Texto(rd, "Folio");

                estatus =
                    Texto(rd, "Estatus");

                fechaSalida =
                    Fecha(rd, "FechaSalida");

                rowVersionActual =
                    Bytes(rd, "RowVersion");
            }

            if (!rowVersionActual.SequenceEqual(
                    rowVersionOriginal))
            {
                await tx.RollbackAsync(
                    cancellationToken);

                return Conflict(new
                {
                    ok = false,
                    recargar = true,
                    mensaje =
                        "El embarque fue modificado por otro usuario. Recarga el calendario."
                });
            }

            if (estatus != "En ruta")
                throw new InvalidOperationException(
                    "Solo un embarque En ruta puede cerrarse como Entregado.");

            if (fechaSalida.HasValue &&
                model.FechaEntrega <
                fechaSalida.Value)
            {
                throw new InvalidOperationException(
                    "La fecha de entrega no puede ser anterior a la salida.");
            }

            const string sqlValidacion = @"
SELECT
    COUNT_BIG(*) AS TotalPartidas,
    ISNULL(SUM(d.CantidadSolicitada),0) AS TotalSolicitado,
    ISNULL(SUM(d.CantidadDespachada),0) AS TotalDespachado,
    SUM(CASE WHEN d.CantidadDespachada<=0 THEN 1 ELSE 0 END) AS PartidasSinDespacho,
    SUM(CASE WHEN d.CantidadDespachada>d.CantidadSolicitada THEN 1 ELSE 0 END) AS PartidasInconsistentes
FROM dbo.Logistica_EmbarqueDetalle d WITH(UPDLOCK,HOLDLOCK)
WHERE d.EmbarqueID=@EmbarqueID
AND d.Activo=1;

SELECT COUNT_BIG(*)
FROM dbo.Logistica_Incidencias i WITH(UPDLOCK,HOLDLOCK)
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

            await using (var cmd =
                new SqlCommand(
                    sqlValidacion,
                    cn,
                    tx))
            {
                cmd.Parameters.Add(
                    "@EmbarqueID",
                    SqlDbType.Int).Value =
                    model.EmbarqueID;

                await using var rd =
                    await cmd.ExecuteReaderAsync(
                        cancellationToken);

                if (!await rd.ReadAsync(
                        cancellationToken))
                {
                    throw new InvalidOperationException(
                        "No fue posible validar las partidas.");
                }

                totalPartidas =
                    EnteroLargo(
                        rd,
                        "TotalPartidas");

                totalSolicitado =
                    Entero(
                        rd,
                        "TotalSolicitado");

                totalDespachado =
                    Entero(
                        rd,
                        "TotalDespachado");

                partidasSinDespacho =
                    EnteroLargo(
                        rd,
                        "PartidasSinDespacho");

                partidasInconsistentes =
                    EnteroLargo(
                        rd,
                        "PartidasInconsistentes");

                incidenciasCriticas = 0;

                if (await rd.NextResultAsync(
                        cancellationToken)
                    &&
                    await rd.ReadAsync(
                        cancellationToken))
                {
                    incidenciasCriticas =
                        Convert.ToInt64(
                            rd.GetValue(0));
                }
            }

            if (totalPartidas <= 0)
                throw new InvalidOperationException(
                    "El embarque no contiene partidas.");

            if (totalSolicitado <= 0 ||
                totalDespachado <= 0)
            {
                throw new InvalidOperationException(
                    "El embarque no contiene cantidades despachadas válidas.");
            }

            if (partidasSinDespacho > 0)
                throw new InvalidOperationException(
                    "Existen partidas sin despachar.");

            if (partidasInconsistentes > 0)
                throw new InvalidOperationException(
                    "Existen cantidades despachadas superiores a las solicitadas.");

            if (incidenciasCriticas > 0)
                throw new InvalidOperationException(
                    "Existen incidencias críticas abiertas.");

            var carpetaRelativa =
                Path.Combine(
                    "Logistica",
                    "Evidencias",
                    model.EmbarqueID.ToString());

            var carpetaFisica =
                Path.Combine(
                    _environment.ContentRootPath,
                    "App_Data",
                    carpetaRelativa);

            Directory.CreateDirectory(
                carpetaFisica);

            var nombreFisico =
                $"{Guid.NewGuid():N}{extension}";

            rutaFisicaCreada =
                Path.Combine(
                    carpetaFisica,
                    nombreFisico);

            await using (var stream =
                new FileStream(
                    rutaFisicaCreada,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    true))
            {
                await model.Evidencia
                    .CopyToAsync(
                        stream,
                        cancellationToken);
            }

            var rutaRelativa =
                Path.Combine(
                    "App_Data",
                    carpetaRelativa,
                    nombreFisico)
                .Replace('\\', '/');

            const string sqlEvidencia = @"
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
    N'Entrega',
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

            await using (var cmd =
                new SqlCommand(
                    sqlEvidencia,
                    cn,
                    tx))
            {
                cmd.Parameters.Add(
                    "@EmbarqueID",
                    SqlDbType.Int).Value =
                    model.EmbarqueID;

                cmd.Parameters.Add(
                    "@NombreOriginal",
                    SqlDbType.NVarChar,
                    260).Value =
                    nombreOriginal;

                cmd.Parameters.Add(
                    "@NombreFisico",
                    SqlDbType.NVarChar,
                    260).Value =
                    nombreFisico;

                cmd.Parameters.Add(
                    "@RutaRelativa",
                    SqlDbType.NVarChar,
                    600).Value =
                    rutaRelativa;

                cmd.Parameters.Add(
                    "@TipoContenido",
                    SqlDbType.NVarChar,
                    150).Value =
                    tipoContenido;

                cmd.Parameters.Add(
                    "@TamanoBytes",
                    SqlDbType.BigInt).Value =
                    model.Evidencia.Length;

                cmd.Parameters.Add(
                    "@Observaciones",
                    SqlDbType.NVarChar,
                    1000).Value =
                    Db(model.ObservacionesEvidencia);

                cmd.Parameters.Add(
                    "@UsuarioID",
                    SqlDbType.Int).Value =
                    Db(UsuarioID);

                cmd.Parameters.Add(
                    "@UsuarioNombre",
                    SqlDbType.NVarChar,
                    200).Value =
                    UsuarioNombre;

                evidenciaId =
                    Convert.ToInt32(
                        await cmd.ExecuteScalarAsync(
                            cancellationToken));
            }

            const string sqlEntrega = @"
UPDATE dbo.Logistica_EmbarqueDetalle
SET
    CantidadEntregada=CantidadDespachada,
    FechaModificacion=SYSDATETIME(),
    ActualizadoPor=@Usuario
WHERE EmbarqueID=@EmbarqueID
AND Activo=1;

UPDATE dbo.Logistica_Embarques
SET
    Estatus=N'Entregado',
    FechaEntrega=@FechaEntrega,
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
OUTPUT CONVERT(varbinary(8),INSERTED.RowVersion)
WHERE EmbarqueID=@EmbarqueID
AND Activo=1
AND Estatus=N'En ruta'
AND RowVersion=@RowVersion;";

            byte[] nuevaVersion;

            await using (var cmd =
                new SqlCommand(
                    sqlEntrega,
                    cn,
                    tx))
            {
                cmd.Parameters.Add(
                    "@EmbarqueID",
                    SqlDbType.Int).Value =
                    model.EmbarqueID;

                cmd.Parameters.Add(
                    "@FechaEntrega",
                    SqlDbType.DateTime2).Value =
                    model.FechaEntrega;

                cmd.Parameters.Add(
                    "@UsuarioID",
                    SqlDbType.Int).Value =
                    Db(UsuarioID);

                cmd.Parameters.Add(
                    "@Usuario",
                    SqlDbType.NVarChar,
                    200).Value =
                    UsuarioNombre;

                cmd.Parameters.Add(
                    "@Receptor",
                    SqlDbType.NVarChar,
                    200).Value =
                    model.ReceptorNombre;

                cmd.Parameters.Add(
                    "@Remision",
                    SqlDbType.NVarChar,
                    100).Value =
                    Db(model.FolioRemision);

                cmd.Parameters.Add(
                    "@Observaciones",
                    SqlDbType.NVarChar,
                    1200).Value =
                    Db(model.Observaciones);

                cmd.Parameters.Add(
                    "@RowVersion",
                    SqlDbType.Timestamp).Value =
                    rowVersionOriginal;

                var resultado =
                    await cmd.ExecuteScalarAsync(
                        cancellationToken);

                if (resultado == null ||
                    resultado == DBNull.Value)
                {
                    throw new DBConcurrencyException(
                        "El embarque cambió mientras se confirmaba la entrega.");
                }

                nuevaVersion =
                    (byte[])resultado;
            }

            await InsertarHistorialAsync(
                cn,
                tx,
                model.EmbarqueID,
                "EVIDENCIA_CIERRE",
                "En ruta",
                "En ruta",
                $"Evidencia EVI-{evidenciaId:000000} agregada desde Centro Operativo. Archivo: {nombreOriginal}.",
                cancellationToken);

            var descripcion =
                $"Entrega cerrada desde Centro Operativo. Receptor: {model.ReceptorNombre}. Fecha: {model.FechaEntrega:dd/MM/yyyy HH:mm}. Cantidad entregada: {totalDespachado:N0} PZA. Evidencia: EVI-{evidenciaId:000000}.";

            if (!string.IsNullOrWhiteSpace(
                    model.FolioRemision))
            {
                descripcion +=
                    $" Remisión: {model.FolioRemision}.";
            }

            if (!string.IsNullOrWhiteSpace(
                    model.Observaciones))
            {
                descripcion +=
                    $" Observaciones: {model.Observaciones}";
            }

            await InsertarHistorialAsync(
                cn,
                tx,
                model.EmbarqueID,
                "CIERRE_DESDE_CALENDARIO",
                "En ruta",
                "Entregado",
                descripcion,
                cancellationToken);

            await tx.CommitAsync(
                cancellationToken);

            return Json(new
            {
                ok = true,
                mensaje =
                    $"{folio} entregado correctamente.",
                embarqueId =
                    model.EmbarqueID,
                estatus =
                    "Entregado",
                evidenciaId,
                rowVersion =
                    Convert.ToBase64String(
                        nuevaVersion)
            });
        }
        catch (DBConcurrencyException ex)
        {
            try
            {
                await tx.RollbackAsync(
                    cancellationToken);
            }
            catch
            {
            }

            EliminarArchivoSiExiste(
                rutaFisicaCreada);

            return Conflict(new
            {
                ok = false,
                recargar = true,
                mensaje = ex.Message
            });
        }
        catch (Exception ex)
        {
            try
            {
                await tx.RollbackAsync(
                    cancellationToken);
            }
            catch
            {
            }

            EliminarArchivoSiExiste(
                rutaFisicaCreada);

            return BadRequest(new
            {
                ok = false,
                mensaje = ex.Message
            });
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
    ISNULL(SUM(CASE WHEN x.TipoEvidencia=N'Trayecto' THEN 1 ELSE 0 END),0) EvidenciasTrayecto,
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
                    salida = vm.Salida,
                    pasos = vm.Pasos.Select(x => new { x.Numero, x.Clave, x.Titulo, x.Descripcion, x.Icono, x.Completo, x.Disponible, x.Actual, x.EstadoTexto }).ToList(),
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
                Criticidad = fechaRequerida != DateTime.MinValue.Date && fechaRequerida < DateTime.Today ? "Expeditado" : "Programado",
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
    e.EmbarqueID,
    e.ClienteID,
    ISNULL(e.Folio,N'') AS Folio,
    ISNULL(e.ClienteNombreSnapshot,N'') AS Cliente,
    ISNULL(e.Destino,N'') AS Destino,
    ISNULL(e.DireccionEntrega,N'') AS DireccionEntrega,
    e.FechaCargaProgramada,
    e.HoraCargaProgramada,
    e.FechaEntregaProgramada,
    ISNULL(e.Estatus,N'') AS Estatus,
    ISNULL(e.TipoOperacion,N'') AS TipoOperacion,
    ISNULL(e.FormaEnvio,N'') AS FormaEnvio,
    ISNULL(e.ModalidadEnvio,N'') AS ModalidadEnvio,
    ISNULL(e.Transportista,N'') AS Transportista,
    ISNULL(e.GuiaReferencia,N'') AS GuiaReferencia,
    ISNULL(r.Codigo+N' - '+r.Nombre,N'') AS Ruta,
    ISNULL(u.NumeroEconomico+CASE WHEN NULLIF(LTRIM(RTRIM(ISNULL(u.Placas,N''))),N'') IS NULL THEN N'' ELSE N' - '+u.Placas END,N'') AS Unidad,
    COALESCE(
        NULLIF(LTRIM(RTRIM(e.ChoferNombreSnapshot)),N''),
        NULLIF(LTRIM(RTRIM(e.OperadorTexto)),N''),
        NULLIF(LTRIM(RTRIM(e.ChoferExterno)),N''),
        N''
    ) AS Chofer,
    ISNULL(e.TieneIncidencia,0) AS TieneIncidencia,
    ISNULL(d.TotalPartidas,0) AS TotalPartidas,
    ISNULL(d.TotalPiezas,0) AS TotalPiezas,
    ISNULL(d.TotalDespachadas,0) AS TotalDespachadas,
    ISNULL(c.TotalCajas,0) AS TotalCajas,
    ISNULL(c.TotalPreparadas,0) AS TotalPreparadas,
    ISNULL(i.IncidenciasAbiertas,0) AS IncidenciasAbiertas,
    ISNULL(i.IncidenciasCriticas,0) AS IncidenciasCriticas,
    ISNULL(doc.TotalDocumentos,0) AS TotalDocumentos,
    ISNULL(doc.DocumentosNoValidados,0) AS DocumentosNoValidados,
    ISNULL(ev.TotalEvidencias,0) AS TotalEvidencias
FROM dbo.Logistica_Embarques e
LEFT JOIN dbo.Logistica_Rutas r ON r.RutaID=e.RutaID
LEFT JOIN dbo.Logistica_Unidades u ON u.UnidadID=e.UnidadID
OUTER APPLY
(
    SELECT
        COUNT_BIG(*) AS TotalPartidas,
        ISNULL(SUM(ed.CantidadSolicitada),0) AS TotalPiezas,
        ISNULL(SUM(ed.CantidadDespachada),0) AS TotalDespachadas
    FROM dbo.Logistica_EmbarqueDetalle ed
    WHERE ed.EmbarqueID=e.EmbarqueID
      AND ed.Activo=1
) d
OUTER APPLY
(
    SELECT
        COUNT_BIG(DISTINCT ec.CajaID) AS TotalCajas,
        ISNULL(SUM(ec.CantidadAsignada),0) AS TotalPreparadas
    FROM dbo.Logistica_EmbarqueCajas ec
    WHERE ec.EmbarqueID=e.EmbarqueID
      AND ec.Activo=1
) c
OUTER APPLY
(
    SELECT
        COUNT_BIG(*) AS IncidenciasAbiertas,
        ISNULL(SUM(CASE WHEN inc.Severidad=N'Crítica' THEN 1 ELSE 0 END),0) AS IncidenciasCriticas
    FROM dbo.Logistica_Incidencias inc
    WHERE inc.EmbarqueID=e.EmbarqueID
      AND inc.Activo=1
      AND inc.Estatus IN(N'Abierta',N'En seguimiento')
) i
OUTER APPLY
(
    SELECT
        COUNT_BIG(*) AS TotalDocumentos,
        ISNULL(SUM(CASE WHEN ISNULL(x.Validado,0)=0 THEN 1 ELSE 0 END),0) AS DocumentosNoValidados
    FROM dbo.Logistica_EmbarqueDocumentos x
    WHERE x.EmbarqueID=e.EmbarqueID
      AND x.Activo=1
) doc
OUTER APPLY
(
    SELECT COUNT_BIG(*) AS TotalEvidencias
    FROM dbo.Logistica_EmbarqueEvidencias ee
    WHERE ee.EmbarqueID=e.EmbarqueID
      AND ee.Activo=1
) ev
WHERE e.EmbarqueID=@EmbarqueID
  AND e.Activo=1;";

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
                TotalEvidencias = Entero(rd, "TotalEvidencias"),
                DatosTransporteCompletos = DatosTransporteCompletos(forma, ruta, unidad, chofer, transportista),
                PreparacionCompleta = total > 0 && preparadas >= total,
                DocumentacionCompleta = Entero(rd, "DocumentosNoValidados") == 0
            };
            CalcularProximaAccion(vm);
        }

        const string sqlPartidas = @"
SELECT
    d.EmbarqueDetalleID,
    d.ReleaseDetalleID,
    ISNULL(d.FolioReleaseSnapshot,N'') AS FolioRelease,
    ISNULL(d.NumeroParteSnapshot,N'') AS NumeroParte,
    ISNULL(d.DescripcionParteSnapshot,N'') AS Descripcion,
    ISNULL(d.NumeroOFSnapshot,N'') AS NumeroOF,
    ISNULL(d.CantidadSolicitada,0) AS CantidadSolicitada,
    ISNULL(d.CantidadDespachada,0) AS CantidadDespachada,
    ISNULL(c.CantidadPreparada,0) AS CantidadPreparada
FROM dbo.Logistica_EmbarqueDetalle d
OUTER APPLY
(
    SELECT ISNULL(SUM(ec.CantidadAsignada),0) AS CantidadPreparada
    FROM dbo.Logistica_EmbarqueCajas ec
    WHERE ec.EmbarqueDetalleID=d.EmbarqueDetalleID
      AND ec.EmbarqueID=d.EmbarqueID
      AND ec.Activo=1
) c
WHERE d.EmbarqueID=@EmbarqueID
  AND d.Activo=1
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
SELECT
    HistorialID,
    EmbarqueID,
    ISNULL(Evento,N'') AS Evento,
    ISNULL(EstadoAnterior,N'') AS EstadoAnterior,
    ISNULL(EstadoNuevo,N'') AS EstadoNuevo,
    ISNULL(Observaciones,N'') AS Observaciones,
    UsuarioID,
    ISNULL(UsuarioNombre,N'') AS Usuario,
    FechaEvento
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

        const string sqlEvidencias = @"
SELECT
    EvidenciaID,
    ISNULL(TipoEvidencia,N'') AS TipoEvidencia,
    ISNULL(NombreOriginal,N'') AS NombreOriginal,
    ISNULL(TipoContenido,N'') AS TipoContenido,
    FechaCarga,
    ISNULL(UsuarioNombre,N'') AS Usuario
FROM dbo.Logistica_EmbarqueEvidencias
WHERE EmbarqueID=@EmbarqueID
  AND Activo=1
ORDER BY FechaCarga DESC,EvidenciaID DESC;";

        await using (var cmd = new SqlCommand(sqlEvidencias, cn))
        {
            cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await rd.ReadAsync(cancellationToken))
            {
                vm.Evidencias.Add(new LogisticaOperacionEvidenciaResumenVm
                {
                    EvidenciaID = Entero(rd, "EvidenciaID"),
                    TipoEvidencia = Texto(rd, "TipoEvidencia"),
                    NombreOriginal = Texto(rd, "NombreOriginal"),
                    TipoContenido = Texto(rd, "TipoContenido"),
                    FechaCarga = Fecha(rd, "FechaCarga") ?? DateTime.MinValue,
                    Usuario = Texto(rd, "Usuario")
                });
            }
        }

        return vm;
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

    // AGREGAR: obtiene o crea la semana de ListaCarga correspondiente a la fecha donde se programa.
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

    private static (
        bool Ok,
        string Mensaje)
        ValidarEvidencia(
            IFormFile archivo)
    {
        const long maximo =
            10 * 1024 * 1024;

        if (archivo.Length <= 0)
            return (
                false,
                "El archivo está vacío.");

        if (archivo.Length > maximo)
            return (
                false,
                "El archivo excede 10 MB.");

        var nombre =
            Path.GetFileName(
                archivo.FileName);

        var extension =
            Path.GetExtension(
                nombre)
                .ToLowerInvariant();

        var extensiones =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase)
            {
                ".jpg",
                ".jpeg",
                ".png",
                ".pdf"
            };

        if (!extensiones.Contains(
                extension))
        {
            return (
                false,
                "Solo se permiten JPG, JPEG, PNG o PDF.");
        }

        var tipos =
            new Dictionary<
                string,
                HashSet<string>>(
                StringComparer.OrdinalIgnoreCase)
            {
                [".jpg"] =
                    new(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        "image/jpeg",
                        "image/pjpeg"
                    },

                [".jpeg"] =
                    new(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        "image/jpeg",
                        "image/pjpeg"
                    },

                [".png"] =
                    new(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        "image/png"
                    },

                [".pdf"] =
                    new(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        "application/pdf"
                    }
            };

        var contenido =
            archivo.ContentType?
                .Trim()
            ?? string.Empty;

        if (!tipos.TryGetValue(
                extension,
                out var permitidos)
            ||
            !permitidos.Contains(
                contenido))
        {
            return (
                false,
                "El tipo de contenido no coincide con la extensión.");
        }

        return (
            true,
            string.Empty);
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

    private static void CalcularProximaAccion(
        LogisticaOperacionDetalleVm vm)
    {
        if (vm.Estatus == "Cancelado")
        {
            vm.ProximaAccion =
                "Cancelado";

            vm.ProximaAccionDetalle =
                "El embarque ya no tiene acciones operativas.";

            return;
        }

        if (vm.Estatus == "Entregado")
        {
            vm.ProximaAccion =
                "Entregado";

            vm.ProximaAccionDetalle =
                "El proceso se encuentra cerrado.";

            return;
        }

        if (string.IsNullOrWhiteSpace(
                vm.FormaEnvio)
            ||
            vm.FormaEnvio == "Pendiente")
        {
            vm.ProximaAccion =
                "Definir salida";

            vm.ProximaAccionDetalle =
                "Selecciona cómo saldrá el embarque.";

            return;
        }

        if (vm.Estatus
            is "Programado"
            or "Preparando")
        {
            vm.ProximaAccion =
                "Preparar carga";

            vm.ProximaAccionDetalle =
                "Asigna las cajas necesarias.";

            return;
        }

        if (vm.Estatus == "Preparado")
        {
            vm.ProximaAccion =
                "Confirmar carga";

            vm.ProximaAccionDetalle =
                "La preparación está completa.";

            return;
        }

        if (vm.Estatus == "Cargado")
        {
            vm.ProximaAccion =
                "Registrar salida";

            vm.ProximaAccionDetalle =
                "Valida documentos y confirma la salida.";

            return;
        }

        if (vm.Estatus == "En ruta")
        {
            vm.ProximaAccion =
                "Confirmar entrega";

            vm.ProximaAccionDetalle =
                "Adjunta evidencia y registra al receptor.";

            return;
        }

        vm.ProximaAccion =
            vm.Estatus;

        vm.ProximaAccionDetalle =
            "Revisa el estado actual.";
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
ISNULL(ev.TotalEvidencias,0) TotalEvidencias,ISNULL(i.IncidenciasAbiertas,0) IncidenciasAbiertas,ISNULL(i.IncidenciasCriticas,0) IncidenciasCriticas
FROM dbo.Logistica_Embarques e
OUTER APPLY
(
    SELECT ISNULL(SUM(x.CantidadSolicitada),0) TotalPiezas,ISNULL(SUM(x.CantidadDespachada),0) TotalDespachadas
    FROM dbo.Logistica_EmbarqueDetalle x
    WHERE x.EmbarqueID=e.EmbarqueID AND x.Activo=1
) d
OUTER APPLY
(
    SELECT ISNULL(SUM(x.CantidadAsignada),0) TotalPreparadas,CONVERT(int,COUNT_BIG(*)) TotalCajas,
    ISNULL(SUM(CASE WHEN x.EstatusSeleccion IN(N'Cargada',N'Despachada') THEN 1 ELSE 0 END),0) TotalCargadas
    FROM dbo.Logistica_EmbarqueCajas x
    WHERE x.EmbarqueID=e.EmbarqueID AND x.Activo=1
) c
OUTER APPLY
(
    SELECT CONVERT(int,COUNT_BIG(*)) TotalEvidencias
    FROM dbo.Logistica_EmbarqueEvidencias x
    WHERE x.EmbarqueID=e.EmbarqueID AND x.Activo=1
) ev
OUTER APPLY
(
    SELECT CONVERT(int,COUNT_BIG(*)) IncidenciasAbiertas,
    ISNULL(SUM(CASE WHEN x.Severidad=N'Crítica' THEN 1 ELSE 0 END),0) IncidenciasCriticas
    FROM dbo.Logistica_Incidencias x
    WHERE x.EmbarqueID=e.EmbarqueID AND x.Activo=1 AND x.Estatus IN(N'Abierta',N'En seguimiento')
) i
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
                TotalEvidencias = Entero(rd, "TotalEvidencias"),
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
        var salidaCompleta = SalidaOperacionCompleta(tipoOperacion, formaEnvio, modalidadEnvio, pasaAduana, rutaId, unidadId, choferId, vm.Salida.ChoferExterno, vm.Salida.Transportista);
        var faltantes = salidaCompleta ? await ObtenerDocumentosFaltantesFlujoAsync(cn, embarqueId, tipoOperacion, formaEnvio, modalidadEnvio, pasaAduana, cancellationToken) : new List<string>();
        vm.DocumentosFaltantes = salidaCompleta ? faltantes.Count : 0;
        var programacionCompleta = vm.FechaCargaProgramada.HasValue && vm.HoraCargaProgramada.HasValue && vm.TotalPiezas > 0;
        var preparacionCompleta = vm.TotalPiezas > 0 && vm.TotalPiezasPreparadas >= vm.TotalPiezas;
        var cargaCompleta = vm.Estatus is "Cargado" or "En ruta" or "Entregado";
        var documentosCompletos = salidaCompleta && faltantes.Count == 0;
        var salidaPlantaCompleta = vm.Estatus is "En ruta" or "Entregado";
        var entregaCompleta = vm.Estatus == "Entregado";
        if (vm.Estatus == "Cancelado")
        {
            programacionCompleta = salidaCompleta = preparacionCompleta = cargaCompleta = documentosCompletos = salidaPlantaCompleta = entregaCompleta = false;
        }
        vm.Pasos.Add(new LogisticaOperacionPasoVm { Numero = 1, Clave = "programacion", Titulo = "Programación", Descripcion = programacionCompleta ? "Fecha, hora y cantidad definidas." : "Completa fecha, hora y cantidad del embarque.", Icono = "fa-calendar-check", Completo = programacionCompleta });
        vm.Pasos.Add(new LogisticaOperacionPasoVm { Numero = 2, Clave = "salida", Titulo = "Forma de salida", Descripcion = salidaCompleta ? "Datos de transporte completos." : "Define Nacional/Exportación y cómo saldrá la mercancía.", Icono = "fa-route", Completo = salidaCompleta });
        vm.Pasos.Add(new LogisticaOperacionPasoVm { Numero = 3, Clave = "preparacion", Titulo = "Preparación PT", Descripcion = preparacionCompleta ? $"{vm.TotalPiezasPreparadas:N0} de {vm.TotalPiezas:N0} PZA preparadas." : $"{vm.TotalPiezasPreparadas:N0} de {vm.TotalPiezas:N0} PZA preparadas.", Icono = "fa-boxes-stacked", Completo = preparacionCompleta });
        vm.Pasos.Add(new LogisticaOperacionPasoVm { Numero = 4, Clave = "carga", Titulo = "Carga física", Descripcion = cargaCompleta ? "Carga física confirmada." : "Confirma cajas y carga física del embarque.", Icono = "fa-dolly", Completo = cargaCompleta });
        vm.Pasos.Add(new LogisticaOperacionPasoVm { Numero = 5, Clave = "documentos", Titulo = "Documentación", Descripcion = !salidaCompleta ? "Primero define la forma de salida." : documentosCompletos ? "Documentación obligatoria completa." : $"Faltan {faltantes.Count:N0} documento(s) obligatorio(s) validado(s).", Icono = "fa-file-circle-check", Completo = documentosCompletos });
        vm.Pasos.Add(new LogisticaOperacionPasoVm { Numero = 6, Clave = "salidaPlanta", Titulo = "Salida de planta", Descripcion = salidaPlantaCompleta ? "El embarque ya salió de planta." : "Valida documentos, evidencias y confirma la salida.", Icono = "fa-truck-fast", Completo = salidaPlantaCompleta });
        vm.Pasos.Add(new LogisticaOperacionPasoVm { Numero = 7, Clave = "entrega", Titulo = "Entrega", Descripcion = entregaCompleta ? "Entrega confirmada." : "Captura receptor y evidencia para cerrar.", Icono = "fa-circle-check", Completo = entregaCompleta });
        var anterioresCompletos = true;
        foreach (var paso in vm.Pasos.OrderBy(x => x.Numero))
        {
            paso.Disponible = anterioresCompletos && !vm.Cerrado;
            anterioresCompletos = anterioresCompletos && paso.Completo;
        }
        var actual = vm.Pasos.FirstOrDefault(x => !x.Completo && x.Disponible) ?? vm.Pasos.FirstOrDefault(x => !x.Completo) ?? vm.Pasos.Last();
        vm.PasoActual = actual.Numero;
        actual.Actual = !vm.Cerrado;
        var catalogos = await CargarCatalogosFlujoAsync(cn, cancellationToken);
        vm.Rutas = catalogos.Rutas;
        vm.Unidades = catalogos.Unidades;
        vm.Choferes = catalogos.Choferes;
        return vm;
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
        try { versionOriginal = Convert.FromBase64String(model.RowVersion!); } catch { throw new DBConcurrencyException("La versión del embarque no es válida. Recarga el flujo."); }
        const string sqlActual = @"
SELECT ISNULL(Folio,N'') Folio,ISNULL(Estatus,N'') Estatus,ISNULL(FormaEnvio,N'Pendiente') FormaEnvio,CONVERT(varbinary(8),RowVersion) RowVersion
FROM dbo.Logistica_Embarques WITH(UPDLOCK,HOLDLOCK)
WHERE EmbarqueID=@EmbarqueID AND Activo=1;";
        string folio, estatus, formaAnterior;
        byte[] versionActual;
        await using (var cmd = new SqlCommand(sqlActual, cn, tx))
        {
            cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = model.EmbarqueID;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await rd.ReadAsync(cancellationToken)) throw new InvalidOperationException("El embarque ya no existe.");
            folio = Texto(rd, "Folio");
            estatus = Texto(rd, "Estatus");
            formaAnterior = NormalizarFormaEnvio(Texto(rd, "FormaEnvio"));
            versionActual = Bytes(rd, "RowVersion");
        }
        if (!versionActual.SequenceEqual(versionOriginal)) throw new DBConcurrencyException("El embarque fue modificado por otro usuario. Recarga el flujo.");
        if (estatus is "Cargado" or "En ruta" or "Entregado" or "Cancelado") throw new InvalidOperationException($"La forma de salida ya no puede modificarse porque el embarque está en estatus {estatus}.");
        if (model.TipoOperacion == "Nacional") model.PasaAduana = null;
        if (model.FormaEnvio == "Interno")
        {
            model.ModalidadEnvio = null; model.Transportista = null; model.GuiaReferencia = null; model.ChoferExterno = null; model.UnidadExterna = null; model.PlacasExternas = null;
        }
        else if (model.FormaEnvio == "Cliente")
        {
            model.RutaID = null; model.UnidadID = null; model.ChoferUsuarioID = null; model.ChoferNombreSnapshot = null; model.ModalidadEnvio = null; model.Transportista = null; model.GuiaReferencia = null;
        }
        else if (model.FormaEnvio == "Paqueteria")
        {
            model.RutaID = null; model.UnidadID = null; model.ChoferUsuarioID = null; model.ChoferNombreSnapshot = null;
        }
        else
        {
            model.ModalidadEnvio = null; model.Transportista = null; model.GuiaReferencia = null; model.PasaAduana = model.TipoOperacion == "Exportacion" ? model.PasaAduana : null;
            model.RutaID = null; model.UnidadID = null; model.ChoferUsuarioID = null; model.ChoferNombreSnapshot = null; model.ChoferExterno = null; model.UnidadExterna = null; model.PlacasExternas = null;
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
        var completo = SalidaOperacionCompleta(model.TipoOperacion, model.FormaEnvio, model.ModalidadEnvio, model.PasaAduana, model.RutaID, model.UnidadID, model.ChoferUsuarioID, model.ChoferExterno, model.Transportista);
        if (confirmar && !completo)
        {
            if (model.TipoOperacion is not "Nacional" and not "Exportacion") throw new InvalidOperationException("Selecciona si el embarque es Nacional o Exportación.");
            if (model.TipoOperacion == "Exportacion" && !model.PasaAduana.HasValue) throw new InvalidOperationException("Indica si la exportación pasa por aduana.");
            if (model.FormaEnvio is not "Interno" and not "Cliente" and not "Paqueteria") throw new InvalidOperationException("Selecciona Entrega NS, Cliente recoge o Paquetería.");
            if (model.FormaEnvio == "Interno")
            {
                if (!model.RutaID.HasValue) throw new InvalidOperationException("Selecciona una ruta.");
                if (!model.UnidadID.HasValue) throw new InvalidOperationException("Selecciona una unidad.");
                if (!model.ChoferUsuarioID.HasValue) throw new InvalidOperationException("Selecciona un chofer.");
            }
            else if (model.FormaEnvio == "Cliente" && string.IsNullOrWhiteSpace(model.ChoferExterno)) throw new InvalidOperationException("Captura quién recoge la mercancía.");
            else if (model.FormaEnvio == "Paqueteria")
            {
                if (string.IsNullOrWhiteSpace(model.ModalidadEnvio)) throw new InvalidOperationException("Selecciona Terrestre, Aérea o Marítima.");
                if (string.IsNullOrWhiteSpace(model.Transportista)) throw new InvalidOperationException("Captura la compañía o paquetería.");
            }
            throw new InvalidOperationException("Completa la información obligatoria antes de continuar.");
        }
        const string sqlUpdate = @"
UPDATE dbo.Logistica_Embarques
SET TipoOperacion=@TipoOperacion,FormaEnvio=@FormaEnvio,ModalidadEnvio=@ModalidadEnvio,Transportista=@Transportista,GuiaReferencia=@GuiaReferencia,PasaAduana=@PasaAduana,
RutaID=@RutaID,UnidadID=@UnidadID,OperadorTexto=@Operador,ChoferUsuarioID=@ChoferUsuarioID,ChoferNombreSnapshot=@ChoferNombreSnapshot,
ChoferExterno=@ChoferExterno,UnidadExterna=@UnidadExterna,PlacasExternas=@PlacasExternas,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
OUTPUT CONVERT(varbinary(8),INSERTED.RowVersion)
WHERE EmbarqueID=@EmbarqueID AND Activo=1 AND Estatus NOT IN(N'Cargado',N'En ruta',N'Entregado',N'Cancelado') AND RowVersion=@RowVersion;";
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
        var evento = confirmar ? "MODALIDAD_ENVIO_DEFINIDA" : "BORRADOR_SALIDA_GUARDADO";
        var textoForma = model.FormaEnvio switch { "Interno" => "Entrega NS", "Cliente" => "Cliente recoge", "Paqueteria" => "Paquetería", _ => "Por definir" };
        var descripcion = confirmar ? $"Forma de salida confirmada desde Centro Operativo. {textoForma}." : $"Borrador de forma de salida guardado desde Centro Operativo. Avance: {(completo ? "completo" : "incompleto")}. Forma: {textoForma}.";
        await InsertarHistorialAsync(cn, tx, model.EmbarqueID, evento, estatus, estatus, descripcion, cancellationToken);
        return (nuevaVersion, completo);
    }

    private async Task<(List<LogisticaOperacionCatalogoVm> Rutas, List<LogisticaOperacionCatalogoVm> Unidades, List<LogisticaOperacionCatalogoVm> Choferes)> CargarCatalogosFlujoAsync(SqlConnection cn, CancellationToken cancellationToken)
    {
        var rutas = new List<LogisticaOperacionCatalogoVm>();
        var unidades = new List<LogisticaOperacionCatalogoVm>();
        var choferes = new List<LogisticaOperacionCatalogoVm>();
        const string sql = @"
SELECT RutaID,Codigo+N' - '+Nombre Texto FROM dbo.Logistica_Rutas WHERE Activo=1 ORDER BY Codigo;
SELECT UnidadID,NumeroEconomico+CASE WHEN NULLIF(LTRIM(RTRIM(ISNULL(Placas,N''))),N'') IS NULL THEN N'' ELSE N' - '+Placas END Texto FROM dbo.Logistica_Unidades WHERE Activo=1 ORDER BY NumeroEconomico;
SELECT U.UsuarioID,LTRIM(RTRIM(CONCAT(ISNULL(P.Nombre,N''),N' ',ISNULL(P.ApellidoPaterno,N''),N' ',ISNULL(P.ApellidoMaterno,N'')))) Texto
FROM dbo.Usuarios U
INNER JOIN dbo.Persona P ON P.PersonaID=U.PersonaID
INNER JOIN dbo.Departamentos D ON D.DepartamentoID=U.DepartamentoID
WHERE U.Activo=1 AND D.Activo=1 AND UPPER(REPLACE(LTRIM(RTRIM(ISNULL(D.NombreDepartamento,N''))),N'Í',N'I'))=N'LOGISTICA'
AND UPPER(LTRIM(RTRIM(ISNULL(P.Puesto,N'')))) LIKE N'%CHOFER%' ORDER BY Texto;";
        await using var cmd = new SqlCommand(sql, cn);
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await rd.ReadAsync(cancellationToken)) rutas.Add(new LogisticaOperacionCatalogoVm { Id = Entero(rd, "RutaID"), Texto = Texto(rd, "Texto") });
        if (await rd.NextResultAsync(cancellationToken)) while (await rd.ReadAsync(cancellationToken)) unidades.Add(new LogisticaOperacionCatalogoVm { Id = Entero(rd, "UnidadID"), Texto = Texto(rd, "Texto") });
        if (await rd.NextResultAsync(cancellationToken)) while (await rd.ReadAsync(cancellationToken)) choferes.Add(new LogisticaOperacionCatalogoVm { Id = Entero(rd, "UsuarioID"), Texto = Texto(rd, "Texto") });
        return (rutas, unidades, choferes);
    }

    private static bool SalidaOperacionCompleta(string? tipoOperacion, string? formaEnvio, string? modalidadEnvio, bool? pasaAduana, int? rutaId, int? unidadId, int? choferUsuarioId, string? choferExterno, string? transportista)
    {
        var tipo = NormalizarTipoOperacionFlujo(tipoOperacion);
        var forma = NormalizarFormaEnvio(formaEnvio);
        var modalidad = NormalizarModalidadEnvioFlujo(modalidadEnvio);
        if (tipo is not "Nacional" and not "Exportacion") return false;
        if (tipo == "Exportacion" && !pasaAduana.HasValue) return false;
        if (forma == "Interno") return rutaId.HasValue && rutaId.Value > 0 && unidadId.HasValue && unidadId.Value > 0 && choferUsuarioId.HasValue && choferUsuarioId.Value > 0;
        if (forma == "Cliente") return !string.IsNullOrWhiteSpace(choferExterno);
        if (forma == "Paqueteria") return modalidad is "Terrestre" or "Aereo" or "Maritimo" && !string.IsNullOrWhiteSpace(transportista);
        return false;
    }

    private static async Task<string> ObtenerNombreChoferFlujoAsync(SqlConnection cn, SqlTransaction tx, int usuarioId, CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT TOP(1) LTRIM(RTRIM(CONCAT(ISNULL(P.Nombre,N''),N' ',ISNULL(P.ApellidoPaterno,N''),N' ',ISNULL(P.ApellidoMaterno,N''))))
FROM dbo.Usuarios U
INNER JOIN dbo.Persona P ON P.PersonaID=U.PersonaID
INNER JOIN dbo.Departamentos D ON D.DepartamentoID=U.DepartamentoID
WHERE U.UsuarioID=@UsuarioID AND U.Activo=1 AND D.Activo=1
AND UPPER(REPLACE(LTRIM(RTRIM(ISNULL(D.NombreDepartamento,N''))),N'Í',N'I'))=N'LOGISTICA'
AND UPPER(LTRIM(RTRIM(ISNULL(P.Puesto,N'')))) LIKE N'%CHOFER%';";
        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
        var resultado = await cmd.ExecuteScalarAsync(cancellationToken);
        var nombre = resultado == null || resultado == DBNull.Value ? string.Empty : Convert.ToString(resultado)?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(nombre)) throw new InvalidOperationException("El chofer seleccionado no pertenece a Logística o no tiene puesto de Chofer.");
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