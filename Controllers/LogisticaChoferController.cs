using ERP.NSQuell.Models.ViewModels.Logistica;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LogisticaChoferController : Controller
{
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;

    public LogisticaChoferController(IConfiguration configuration, IWebHostEnvironment environment)
    {
        _configuration = configuration;
        _environment = environment;
    }

    private string ConnectionString => _configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("No se encontró ConnectionStrings:DefaultConnection.");
    private int? UsuarioID => HttpContext.Session.GetInt32("UsuarioID");
    private string UsuarioNombre => HttpContext.Session.GetString("NombreMostrar") ?? HttpContext.Session.GetString("Username") ?? User?.Identity?.Name ?? "Usuario";

    private async Task<SqlConnection> AbrirAsync(CancellationToken cancellationToken)
    {
        var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(cancellationToken);
        return cn;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
    {
        if (!UsuarioID.HasValue || UsuarioID.Value <= 0) return RedirectToAction("Login", "Login");

        await using var cn = await AbrirAsync(cancellationToken);

        if (!await UsuarioEsChoferAsync(cn, UsuarioID.Value, cancellationToken))
            return AccesoDenegadoChofer();

        var viajes = await ObtenerViajesChoferAsync(cn, UsuarioID.Value, cancellationToken);

        ViewBag.NombreChofer = UsuarioNombre;
        ViewBag.TotalViajes = viajes.Count;
        ViewBag.Programados = viajes.Count(x => x.Estatus == "Programado");
        ViewBag.EnCurso = viajes.Count(x => x.Estatus == "En curso");
        ViewBag.CompletadosHoy = viajes.Count(x => x.Estatus == "Completado" && x.FechaRegresoReal.HasValue && x.FechaRegresoReal.Value.Date == DateTime.Today);

        return View(viajes);
    }

    [HttpGet]
    public async Task<IActionResult> Detalle(int id, CancellationToken cancellationToken = default)
    {
        if (!UsuarioID.HasValue || UsuarioID.Value <= 0) return RedirectToAction("Login", "Login");
        if (id <= 0) return NotFound();

        await using var cn = await AbrirAsync(cancellationToken);

        if (!await UsuarioEsChoferAsync(cn, UsuarioID.Value, cancellationToken))
            return AccesoDenegadoChofer();

        if (!await ViajeAsignadoAlChoferAsync(cn, id, UsuarioID.Value, cancellationToken))
        {
            TempData["LogisticaError"] = "Este viaje no se encuentra asignado al chofer conectado.";
            return RedirectToAction(nameof(Index));
        }

        var vm = await CargarDetalleChoferAsync(cn, id, UsuarioID.Value, cancellationToken);

        if (vm == null) return NotFound();

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegistrarSalida(int viajeId, int? kilometrajeSalida, List<IFormFile>? evidencias, string? observaciones, CancellationToken cancellationToken = default)
    {
        if (!UsuarioID.HasValue || UsuarioID.Value <= 0) return RedirectToAction("Login", "Login");
        if (viajeId <= 0) return RedirectToAction(nameof(Index));
        observaciones = observaciones?.Trim();
        evidencias = (evidencias ?? new List<IFormFile>()).Where(x => x != null && x.Length > 0).ToList();
        if (kilometrajeSalida.HasValue && kilometrajeSalida.Value < 0)
        {
            TempData["LogisticaError"] = "El kilometraje inicial no puede ser negativo.";
            return RedirectToAction(nameof(Detalle), new { id = viajeId });
        }
        if (evidencias.Count == 0)
        {
            TempData["LogisticaError"] = "Debes agregar al menos una fotografía antes de registrar la salida.";
            return RedirectToAction(nameof(Detalle), new { id = viajeId });
        }
        if (evidencias.Count > 15)
        {
            TempData["LogisticaError"] = "Puedes agregar como máximo 15 evidencias por salida.";
            return RedirectToAction(nameof(Detalle), new { id = viajeId });
        }
        var imagenes = new List<IFormFile>();
        var archivos = new List<IFormFile>();
        foreach (var evidencia in evidencias)
        {
            var extension = Path.GetExtension(evidencia.FileName).ToLowerInvariant();
            if (EsExtensionImagen(extension))
            {
                var validacion = ValidarFotoEvidencia(evidencia);
                if (!validacion.Ok)
                {
                    TempData["LogisticaError"] = validacion.Mensaje;
                    return RedirectToAction(nameof(Detalle), new { id = viajeId });
                }
                imagenes.Add(evidencia);
                continue;
            }
            if (extension == ".pdf")
            {
                var validacion = ValidarArchivoEvidencia(evidencia);
                if (!validacion.Ok)
                {
                    TempData["LogisticaError"] = validacion.Mensaje;
                    return RedirectToAction(nameof(Detalle), new { id = viajeId });
                }
                archivos.Add(evidencia);
                continue;
            }
            TempData["LogisticaError"] = $"El archivo {Path.GetFileName(evidencia.FileName)} no es válido. Solo se permiten imágenes o PDF.";
            return RedirectToAction(nameof(Detalle), new { id = viajeId });
        }
        if (imagenes.Count == 0)
        {
            TempData["LogisticaError"] = "Debes agregar al menos una fotografía. Un PDF por sí solo no permite registrar la salida.";
            return RedirectToAction(nameof(Detalle), new { id = viajeId });
        }
        await using var cn = await AbrirAsync(cancellationToken);
        if (!await UsuarioEsChoferAsync(cn, UsuarioID.Value, cancellationToken)) return AccesoDenegadoChofer();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var rutasFisicas = new List<string>();
        try
        {
            var viaje = await ObtenerViajeChoferParaActualizarAsync(cn, tx, viajeId, UsuarioID.Value, cancellationToken) ?? throw new InvalidOperationException("El viaje no existe o ya no se encuentra asignado al chofer conectado.");
            if (viaje.Estatus != "Programado") throw new InvalidOperationException("Solo un viaje Programado puede registrar salida.");
            if (!viaje.UnidadID.HasValue || viaje.UnidadID.Value <= 0) throw new InvalidOperationException("El viaje no tiene una unidad asignada.");
            await ValidarChoferYUnidadDisponiblesAsync(cn, tx, viajeId, UsuarioID.Value, viaje.UnidadID.Value, cancellationToken);
            await ValidarEmbarquesViajeListosParaSalidaAsync(cn, tx, viajeId, cancellationToken);
            var ahora = DateTime.Now;
            const string sqlUpdate = @"UPDATE dbo.Logistica_Viajes SET Estatus=N'En curso',FechaSalidaReal=@FechaSalida,KilometrajeSalida=@KilometrajeSalida,Observaciones=CASE WHEN @Observaciones IS NULL THEN Observaciones WHEN NULLIF(LTRIM(RTRIM(ISNULL(Observaciones,N''))),N'') IS NULL THEN @Observaciones ELSE CONCAT(Observaciones,NCHAR(13),NCHAR(10),@Observaciones) END,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario WHERE ViajeID=@ViajeID AND OperadorUsuarioID=@UsuarioID AND Activo=1 AND Estatus=N'Programado'; SELECT @@ROWCOUNT;";
            await using (var cmd = new SqlCommand(sqlUpdate, cn, tx))
            {
                cmd.Parameters.Add("@FechaSalida", SqlDbType.DateTime2).Value = ahora;
                cmd.Parameters.Add("@KilometrajeSalida", SqlDbType.Int).Value = Db(kilometrajeSalida);
                cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1000).Value = Db(string.IsNullOrWhiteSpace(observaciones) ? null : observaciones);
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = UsuarioID.Value;
                if (Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) != 1) throw new InvalidOperationException("El viaje cambió mientras registrabas la salida.");
            }
            await InicializarParadasAlSalirAsync(cn, tx, viajeId, ahora, cancellationToken);
            for (var i = 0; i < imagenes.Count; i++)
            {
                var evidencia = imagenes[i];
                var guardado = await GuardarArchivoEvidenciaAsync(viajeId, evidencia, $"SALIDA_FOTO_{i + 1:00}", cancellationToken);
                rutasFisicas.Add(guardado.RutaFisica);
                await InsertarEvidenciaAsync(cn, tx, viajeId, "Salida", guardado.NombreOriginal, guardado.NombreFisico, guardado.RutaRelativa, guardado.TipoContenido, guardado.TamanoBytes, observaciones, cancellationToken);
            }
            for (var i = 0; i < archivos.Count; i++)
            {
                var evidencia = archivos[i];
                var guardado = await GuardarArchivoEvidenciaAsync(viajeId, evidencia, $"SALIDA_ARCHIVO_{i + 1:00}", cancellationToken);
                rutasFisicas.Add(guardado.RutaFisica);
                await InsertarEvidenciaAsync(cn, tx, viajeId, "Salida", guardado.NombreOriginal, guardado.NombreFisico, guardado.RutaRelativa, guardado.TipoContenido, guardado.TamanoBytes, string.IsNullOrWhiteSpace(observaciones) ? "Archivo adicional de salida." : $"Archivo adicional de salida. {observaciones}", cancellationToken);
            }
            await DespacharEmbarquesViajeAsync(cn, tx, viajeId, ahora, cancellationToken);
            var descripcion = $"Salida registrada por el chofer el {ahora:dd/MM/yyyy HH:mm}. Fotografías: {imagenes.Count}.";
            if (archivos.Count > 0) descripcion += $" Archivos adicionales: {archivos.Count}.";
            if (kilometrajeSalida.HasValue) descripcion += $" Kilometraje inicial: {kilometrajeSalida.Value:N0} km.";
            if (!string.IsNullOrWhiteSpace(observaciones)) descripcion += $" Observaciones: {observaciones}";
            await InsertarHistorialAsync(cn, tx, viajeId, "SALIDA_REGISTRADA_CHOFER", "Programado", "En curso", descripcion, cancellationToken);
            await tx.CommitAsync(cancellationToken);
            TempData["LogisticaOk"] = "Salida registrada correctamente. La primera parada quedó En camino.";
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(cancellationToken); } catch { }
            foreach (var ruta in rutasFisicas)
            {
                if (string.IsNullOrWhiteSpace(ruta) || !System.IO.File.Exists(ruta)) continue;
                try { System.IO.File.Delete(ruta); } catch { }
            }
            TempData["LogisticaError"] = "No fue posible registrar la salida: " + ex.Message;
        }
        return RedirectToAction(nameof(Detalle), new { id = viajeId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegistrarLlegadaParada(int viajeId, int viajeParadaId, string? observaciones, CancellationToken cancellationToken = default)
    {
        if (!UsuarioID.HasValue || UsuarioID.Value <= 0) return RedirectToAction("Login", "Login");
        if (viajeId <= 0 || viajeParadaId <= 0) return RedirectToAction(nameof(Index));
        observaciones = observaciones?.Trim();
        await using var cn = await AbrirAsync(cancellationToken);
        if (!await UsuarioEsChoferAsync(cn, UsuarioID.Value, cancellationToken)) return AccesoDenegadoChofer();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            const string sql = @"SELECT ISNULL(v.Estatus,N'') EstatusViaje,p.Secuencia,ISNULL(p.TipoParada,N'') TipoParada,ISNULL(p.Lugar,N'') Lugar,ISNULL(p.Estatus,N'') EstatusParada,ISNULL(p.CierraViaje,0) CierraViaje FROM dbo.Logistica_ViajeParadas p WITH(UPDLOCK,HOLDLOCK) INNER JOIN dbo.Logistica_Viajes v WITH(UPDLOCK,HOLDLOCK) ON v.ViajeID=p.ViajeID WHERE p.ViajeParadaID=@ParadaID AND p.ViajeID=@ViajeID AND p.Activo=1 AND v.Activo=1 AND v.OperadorUsuarioID=@UsuarioID;";
            int secuencia;
            string estatusViaje, tipoParada, lugar, estatusParada;
            await using (var cmd = new SqlCommand(sql, cn, tx))
            {
                cmd.Parameters.Add("@ParadaID", SqlDbType.Int).Value = viajeParadaId;
                cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = UsuarioID.Value;
                await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
                if (!await rd.ReadAsync(cancellationToken)) throw new InvalidOperationException("La parada no existe o no pertenece a este viaje.");
                estatusViaje = Texto(rd, "EstatusViaje");
                secuencia = Entero(rd, "Secuencia");
                tipoParada = Texto(rd, "TipoParada");
                lugar = Texto(rd, "Lugar");
                estatusParada = Texto(rd, "EstatusParada");
            }
            if (estatusViaje != "En curso") throw new InvalidOperationException("El viaje debe estar En curso para registrar una llegada.");
            if (tipoParada == "Origen") throw new InvalidOperationException("La salida del Origen ya se registra al iniciar el viaje.");
            if (estatusParada is "Completada" or "Omitida" or "Cancelada") throw new InvalidOperationException("La parada ya fue resuelta.");
            if (estatusParada == "En sitio")
            {
                await tx.RollbackAsync(cancellationToken);
                TempData["LogisticaOk"] = $"Ya te encuentras registrado en {lugar}.";
                return RedirectToAction(nameof(Detalle), new { id = viajeId });
            }
            await ValidarParadasPreviasResueltasAsync(cn, tx, viajeId, secuencia, cancellationToken);
            var ahora = DateTime.Now;
            const string sqlUpdate = @"UPDATE dbo.Logistica_ViajeParadas SET Estatus=N'En sitio',FechaLlegadaReal=COALESCE(FechaLlegadaReal,@Fecha),Observaciones=CASE WHEN @Observaciones IS NULL THEN Observaciones WHEN NULLIF(LTRIM(RTRIM(ISNULL(Observaciones,N''))),N'') IS NULL THEN @Observaciones ELSE CONCAT(Observaciones,NCHAR(13),NCHAR(10),@Observaciones) END,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario WHERE ViajeParadaID=@ParadaID AND ViajeID=@ViajeID AND Activo=1 AND Estatus IN(N'Pendiente',N'En camino'); SELECT @@ROWCOUNT;";
            await using (var cmd = new SqlCommand(sqlUpdate, cn, tx))
            {
                cmd.Parameters.Add("@Fecha", SqlDbType.DateTime2).Value = ahora;
                cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1200).Value = Db(string.IsNullOrWhiteSpace(observaciones) ? null : observaciones);
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@ParadaID", SqlDbType.Int).Value = viajeParadaId;
                cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
                if (Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) != 1) throw new InvalidOperationException("La parada cambió mientras registrabas la llegada.");
            }
            await InsertarHistorialParadaAsync(cn, tx, viajeParadaId, "LLEGADA_REGISTRADA", estatusParada, "En sitio", $"Llegada a {lugar} registrada por {UsuarioNombre} el {ahora:dd/MM/yyyy HH:mm}.", cancellationToken);
            await InsertarHistorialAsync(cn, tx, viajeId, "LLEGADA_PARADA", "En curso", "En curso", $"Llegada a parada #{secuencia}: {lugar}.", cancellationToken);
            await tx.CommitAsync(cancellationToken);
            TempData["LogisticaOk"] = $"Llegada a {lugar} registrada correctamente.";
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
    public async Task<IActionResult> CompletarParada(int viajeId, int viajeParadaId, List<IFormFile>? evidencias, string? observaciones, string? receptorNombre, string? folioRemision, CancellationToken cancellationToken = default)
    {
        if (!UsuarioID.HasValue || UsuarioID.Value <= 0) return RedirectToAction("Login", "Login");
        if (viajeId <= 0 || viajeParadaId <= 0) return RedirectToAction(nameof(Index));
        observaciones = observaciones?.Trim();
        receptorNombre = receptorNombre?.Trim();
        folioRemision = folioRemision?.Trim();
        if (!string.IsNullOrWhiteSpace(receptorNombre) && receptorNombre.Length > 200)
        {
            TempData["LogisticaError"] = "El nombre de quien recibe no puede exceder 200 caracteres.";
            return RedirectToAction(nameof(Detalle), new { id = viajeId });
        }
        if (!string.IsNullOrWhiteSpace(folioRemision) && folioRemision.Length > 100)
        {
            TempData["LogisticaError"] = "El folio de remisión no puede exceder 100 caracteres.";
            return RedirectToAction(nameof(Detalle), new { id = viajeId });
        }
        if (!string.IsNullOrWhiteSpace(observaciones) && observaciones.Length > 1200)
        {
            TempData["LogisticaError"] = "Las observaciones no pueden exceder 1200 caracteres.";
            return RedirectToAction(nameof(Detalle), new { id = viajeId });
        }
        evidencias = (evidencias ?? new List<IFormFile>()).Where(x => x != null && x.Length > 0).ToList();
        if (evidencias.Count > 15)
        {
            TempData["LogisticaError"] = "Puedes agregar como máximo 15 evidencias por parada.";
            return RedirectToAction(nameof(Detalle), new { id = viajeId });
        }
        var imagenes = new List<IFormFile>();
        var archivos = new List<IFormFile>();
        foreach (var evidencia in evidencias)
        {
            var extension = Path.GetExtension(evidencia.FileName).ToLowerInvariant();
            if (EsExtensionImagen(extension))
            {
                var validacion = ValidarFotoEvidencia(evidencia);
                if (!validacion.Ok)
                {
                    TempData["LogisticaError"] = validacion.Mensaje;
                    return RedirectToAction(nameof(Detalle), new { id = viajeId });
                }
                imagenes.Add(evidencia);
                continue;
            }
            if (extension == ".pdf")
            {
                var validacion = ValidarArchivoEvidencia(evidencia);
                if (!validacion.Ok)
                {
                    TempData["LogisticaError"] = validacion.Mensaje;
                    return RedirectToAction(nameof(Detalle), new { id = viajeId });
                }
                archivos.Add(evidencia);
                continue;
            }
            TempData["LogisticaError"] = $"El archivo {Path.GetFileName(evidencia.FileName)} no es válido. Solo se permiten fotografías o archivos PDF.";
            return RedirectToAction(nameof(Detalle), new { id = viajeId });
        }
        await using var cn = await AbrirAsync(cancellationToken);
        if (!await UsuarioEsChoferAsync(cn, UsuarioID.Value, cancellationToken)) return AccesoDenegadoChofer();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var rutasFisicas = new List<string>();
        try
        {
            const string sql = @"
SELECT ISNULL(v.Estatus,N'') EstatusViaje,p.Secuencia,ISNULL(p.TipoParada,N'') TipoParada,ISNULL(p.TipoOperacion,N'') TipoOperacion,
ISNULL(p.Lugar,N'') Lugar,ISNULL(p.Estatus,N'') EstatusParada,ISNULL(p.RequiereEvidencia,0) RequiereEvidencia,
ISNULL(p.CierraViaje,0) CierraViaje,ISNULL(p.ReferenciaTipo,N'') ReferenciaTipo,p.ReferenciaID
FROM dbo.Logistica_ViajeParadas p WITH(UPDLOCK,HOLDLOCK)
INNER JOIN dbo.Logistica_Viajes v WITH(UPDLOCK,HOLDLOCK) ON v.ViajeID=p.ViajeID
WHERE p.ViajeParadaID=@ParadaID AND p.ViajeID=@ViajeID AND p.Activo=1 AND v.Activo=1 AND v.OperadorUsuarioID=@UsuarioID;";
            int secuencia;
            string estatusViaje, tipoParada, tipoOperacion, lugar, estatusParada, referenciaTipo;
            int? referenciaId;
            bool requiereEvidencia, cierraViaje;
            await using (var cmd = new SqlCommand(sql, cn, tx))
            {
                cmd.Parameters.Add("@ParadaID", SqlDbType.Int).Value = viajeParadaId;
                cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = UsuarioID.Value;
                await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
                if (!await rd.ReadAsync(cancellationToken)) throw new InvalidOperationException("La parada no existe o no pertenece al viaje asignado.");
                estatusViaje = Texto(rd, "EstatusViaje");
                secuencia = Entero(rd, "Secuencia");
                tipoParada = Texto(rd, "TipoParada");
                tipoOperacion = Texto(rd, "TipoOperacion");
                lugar = Texto(rd, "Lugar");
                estatusParada = Texto(rd, "EstatusParada");
                requiereEvidencia = Booleano(rd, "RequiereEvidencia");
                cierraViaje = Booleano(rd, "CierraViaje");
                referenciaTipo = Texto(rd, "ReferenciaTipo");
                referenciaId = EnteroNullable(rd, "ReferenciaID");
            }
            if (estatusViaje != "En curso") throw new InvalidOperationException("El viaje debe estar En curso para completar una parada.");
            if (tipoParada == "Origen") throw new InvalidOperationException("La parada de Origen se completa al registrar la salida.");
            if (cierraViaje) throw new InvalidOperationException("La parada final de retorno se completa al registrar el regreso.");
            if (estatusParada is "Completada" or "Omitida" or "Cancelada") throw new InvalidOperationException("La parada ya fue resuelta.");
            if (estatusParada != "En sitio") throw new InvalidOperationException("Primero debes registrar la llegada a esta parada.");
            await ValidarParadasPreviasResueltasAsync(cn, tx, viajeId, secuencia, cancellationToken);
            var esEntrega = tipoParada.Equals("Entrega", StringComparison.OrdinalIgnoreCase);
            if ((requiereEvidencia || esEntrega) && imagenes.Count == 0) throw new InvalidOperationException("Esta parada requiere al menos una fotografía como evidencia.");
            if (esEntrega && string.IsNullOrWhiteSpace(receptorNombre)) throw new InvalidOperationException("Debes capturar el nombre de la persona que recibe el material.");
            for (var i = 0; i < imagenes.Count; i++)
            {
                var evidencia = imagenes[i];
                var guardado = await GuardarArchivoEvidenciaAsync(viajeId, evidencia, $"PARADA_{viajeParadaId}_FOTO_{i + 1:00}", cancellationToken);
                rutasFisicas.Add(guardado.RutaFisica);
                await InsertarEvidenciaParadaAsync(cn, tx, viajeParadaId, "Foto", guardado.NombreOriginal, guardado.NombreFisico, guardado.RutaRelativa, guardado.TipoContenido, guardado.TamanoBytes, observaciones, cancellationToken);
            }
            for (var i = 0; i < archivos.Count; i++)
            {
                var evidencia = archivos[i];
                var guardado = await GuardarArchivoEvidenciaAsync(viajeId, evidencia, $"PARADA_{viajeParadaId}_ARCHIVO_{i + 1:00}", cancellationToken);
                rutasFisicas.Add(guardado.RutaFisica);
                await InsertarEvidenciaParadaAsync(cn, tx, viajeParadaId, "Archivo", guardado.NombreOriginal, guardado.NombreFisico, guardado.RutaRelativa, guardado.TipoContenido, guardado.TamanoBytes, observaciones, cancellationToken);
            }
            var ahora = DateTime.Now;
            var resultadoEmbarques = await CompletarEmbarquesParadaAsync(cn, tx, viajeId, viajeParadaId, ahora, receptorNombre, folioRemision, cancellationToken);
            if (esEntrega && referenciaTipo.Equals("Embarque", StringComparison.OrdinalIgnoreCase) && referenciaId.HasValue && resultadoEmbarques.Vinculados == 0) throw new InvalidOperationException("La parada está configurada como entrega de embarque, pero ya no existe una relación activa con el embarque.");
            const string sqlUpdate = @"
UPDATE dbo.Logistica_ViajeParadas
SET Estatus=N'Completada',
FechaLlegadaReal=COALESCE(FechaLlegadaReal,@Fecha),
FechaSalidaReal=@Fecha,
Observaciones=CASE WHEN @Observaciones IS NULL THEN Observaciones WHEN NULLIF(LTRIM(RTRIM(ISNULL(Observaciones,N''))),N'') IS NULL THEN @Observaciones ELSE CONCAT(Observaciones,NCHAR(13),NCHAR(10),@Observaciones) END,
FechaModificacion=SYSDATETIME(),
ActualizadoPor=@Usuario
WHERE ViajeParadaID=@ParadaID AND ViajeID=@ViajeID AND Activo=1 AND Estatus=N'En sitio';
SELECT @@ROWCOUNT;";
            await using (var cmd = new SqlCommand(sqlUpdate, cn, tx))
            {
                cmd.Parameters.Add("@Fecha", SqlDbType.DateTime2).Value = ahora;
                cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1200).Value = Db(string.IsNullOrWhiteSpace(observaciones) ? null : observaciones);
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@ParadaID", SqlDbType.Int).Value = viajeParadaId;
                cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
                if (Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) != 1) throw new InvalidOperationException("La parada cambió mientras se intentaba completar.");
            }
            var descripcion = $"{tipoOperacion} completada en {lugar}. Fotografías: {imagenes.Count}.";
            if (archivos.Count > 0) descripcion += $" Archivos PDF: {archivos.Count}.";
            if (resultadoEmbarques.EntregadosAhora > 0) descripcion += $" Embarques entregados: {resultadoEmbarques.EntregadosAhora}.";
            if (esEntrega && !string.IsNullOrWhiteSpace(receptorNombre)) descripcion += $" Recibió: {receptorNombre}.";
            if (esEntrega && !string.IsNullOrWhiteSpace(folioRemision)) descripcion += $" Remisión: {folioRemision}.";
            if (!string.IsNullOrWhiteSpace(observaciones)) descripcion += $" Observaciones: {observaciones}";
            await InsertarHistorialParadaAsync(cn, tx, viajeParadaId, "PARADA_COMPLETADA", estatusParada, "Completada", descripcion, cancellationToken);
            await InsertarHistorialAsync(cn, tx, viajeId, "PARADA_COMPLETADA", "En curso", "En curso", $"Parada #{secuencia} completada: {lugar}. Fotografías: {imagenes.Count}. Archivos: {archivos.Count}. Embarques entregados: {resultadoEmbarques.EntregadosAhora}.{(esEntrega && !string.IsNullOrWhiteSpace(receptorNombre) ? $" Recibió: {receptorNombre}." : string.Empty)}{(esEntrega && !string.IsNullOrWhiteSpace(folioRemision) ? $" Remisión: {folioRemision}." : string.Empty)}", cancellationToken);
            await ActivarSiguienteParadaAsync(cn, tx, viajeId, cancellationToken);
            await tx.CommitAsync(cancellationToken);
            TempData["LogisticaOk"] = resultadoEmbarques.EntregadosAhora > 0 ? $"Parada #{secuencia} completada. {resultadoEmbarques.EntregadosAhora} embarque(s) quedaron Entregados." : $"Parada #{secuencia} completada correctamente.";
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(cancellationToken); } catch { }
            foreach (var ruta in rutasFisicas)
            {
                if (string.IsNullOrWhiteSpace(ruta) || !System.IO.File.Exists(ruta)) continue;
                try { System.IO.File.Delete(ruta); } catch { }
            }
            TempData["LogisticaError"] = "No fue posible completar la parada: " + ex.Message;
        }
        return RedirectToAction(nameof(Detalle), new { id = viajeId });
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> OmitirParada(int viajeId, int viajeParadaId, string? motivo, CancellationToken cancellationToken = default)
    {
        if (!UsuarioID.HasValue || UsuarioID.Value <= 0) return RedirectToAction("Login", "Login");
        if (viajeId <= 0 || viajeParadaId <= 0) return RedirectToAction(nameof(Index));
        motivo = motivo?.Trim();
        if (string.IsNullOrWhiteSpace(motivo))
        {
            TempData["LogisticaError"] = "Captura el motivo por el que se omitirá la parada.";
            return RedirectToAction(nameof(Detalle), new { id = viajeId });
        }
        await using var cn = await AbrirAsync(cancellationToken);
        if (!await UsuarioEsChoferAsync(cn, UsuarioID.Value, cancellationToken)) return AccesoDenegadoChofer();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            const string sql = @"SELECT ISNULL(v.Estatus,N'') EstatusViaje,p.Secuencia,ISNULL(p.TipoParada,N'') TipoParada,ISNULL(p.Lugar,N'') Lugar,ISNULL(p.Estatus,N'') EstatusParada,ISNULL(p.CierraViaje,0) CierraViaje,CASE WHEN EXISTS(SELECT 1 FROM dbo.Logistica_ViajeEmbarques ve INNER JOIN dbo.Logistica_Embarques e ON e.EmbarqueID=ve.EmbarqueID AND e.Activo=1 WHERE ve.ViajeParadaID=p.ViajeParadaID AND ve.Activo=1 AND e.Estatus<>N'Cancelado') THEN 1 ELSE 0 END TieneEmbarque FROM dbo.Logistica_ViajeParadas p WITH(UPDLOCK,HOLDLOCK) INNER JOIN dbo.Logistica_Viajes v WITH(UPDLOCK,HOLDLOCK) ON v.ViajeID=p.ViajeID WHERE p.ViajeParadaID=@ParadaID AND p.ViajeID=@ViajeID AND p.Activo=1 AND v.Activo=1 AND v.OperadorUsuarioID=@UsuarioID;";
            int secuencia;
            string estatusViaje, tipoParada, lugar, estatusParada;
            bool cierraViaje, tieneEmbarque;
            await using (var cmd = new SqlCommand(sql, cn, tx))
            {
                cmd.Parameters.Add("@ParadaID", SqlDbType.Int).Value = viajeParadaId;
                cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = UsuarioID.Value;
                await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
                if (!await rd.ReadAsync(cancellationToken)) throw new InvalidOperationException("La parada no existe.");
                estatusViaje = Texto(rd, "EstatusViaje");
                secuencia = Entero(rd, "Secuencia");
                tipoParada = Texto(rd, "TipoParada");
                lugar = Texto(rd, "Lugar");
                estatusParada = Texto(rd, "EstatusParada");
                cierraViaje = Booleano(rd, "CierraViaje");
                tieneEmbarque = Booleano(rd, "TieneEmbarque");
            }
            if (estatusViaje != "En curso") throw new InvalidOperationException("Solo puedes omitir una parada mientras el viaje está En curso.");
            if (tipoParada == "Origen" || cierraViaje) throw new InvalidOperationException("No puedes omitir el Origen ni el Retorno final.");
            if (estatusParada is "Completada" or "Omitida" or "Cancelada") throw new InvalidOperationException("La parada ya fue resuelta.");
            if (tieneEmbarque) throw new InvalidOperationException("Esta parada tiene un embarque activo y no puede omitirse. Debes resolver o cancelar primero el embarque.");
            await ValidarParadasPreviasResueltasAsync(cn, tx, viajeId, secuencia, cancellationToken);
            var ahora = DateTime.Now;
            const string sqlUpdate = @"UPDATE dbo.Logistica_ViajeParadas SET Estatus=N'Omitida',FechaSalidaReal=COALESCE(FechaSalidaReal,@Fecha),Observaciones=CASE WHEN NULLIF(LTRIM(RTRIM(ISNULL(Observaciones,N''))),N'') IS NULL THEN CONCAT(N'Omitida: ',@Motivo) ELSE CONCAT(Observaciones,NCHAR(13),NCHAR(10),N'Omitida: ',@Motivo) END,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario WHERE ViajeParadaID=@ParadaID AND ViajeID=@ViajeID AND Activo=1 AND Estatus IN(N'Pendiente',N'En camino',N'En sitio'); SELECT @@ROWCOUNT;";
            await using (var cmd = new SqlCommand(sqlUpdate, cn, tx))
            {
                cmd.Parameters.Add("@Fecha", SqlDbType.DateTime2).Value = ahora;
                cmd.Parameters.Add("@Motivo", SqlDbType.NVarChar, 1000).Value = motivo;
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@ParadaID", SqlDbType.Int).Value = viajeParadaId;
                cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
                if (Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) != 1) throw new InvalidOperationException("La parada cambió mientras se intentaba omitir.");
            }
            await InsertarHistorialParadaAsync(cn, tx, viajeParadaId, "PARADA_OMITIDA", estatusParada, "Omitida", $"Motivo: {motivo}", cancellationToken);
            await InsertarHistorialAsync(cn, tx, viajeId, "PARADA_OMITIDA", "En curso", "En curso", $"Parada #{secuencia} {lugar} omitida. Motivo: {motivo}", cancellationToken);
            await ActivarSiguienteParadaAsync(cn, tx, viajeId, cancellationToken);
            await tx.CommitAsync(cancellationToken);
            TempData["LogisticaOk"] = "Parada omitida. La siguiente parada quedó disponible.";
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
    public async Task<IActionResult> RegistrarRetorno(int viajeId, int? kilometrajeRegreso, List<IFormFile>? evidencias, string? observaciones, CancellationToken cancellationToken = default)
    {
        if (!UsuarioID.HasValue || UsuarioID.Value <= 0) return RedirectToAction("Login", "Login");
        if (viajeId <= 0) return RedirectToAction(nameof(Index));
        observaciones = observaciones?.Trim();
        evidencias = (evidencias ?? new List<IFormFile>()).Where(x => x != null && x.Length > 0).ToList();
        if (kilometrajeRegreso.HasValue && kilometrajeRegreso.Value < 0)
        {
            TempData["LogisticaError"] = "El kilometraje final no puede ser negativo.";
            return RedirectToAction(nameof(Detalle), new { id = viajeId });
        }
        if (evidencias.Count == 0)
        {
            TempData["LogisticaError"] = "Debes agregar al menos una fotografía antes de registrar el regreso.";
            return RedirectToAction(nameof(Detalle), new { id = viajeId });
        }
        if (evidencias.Count > 15)
        {
            TempData["LogisticaError"] = "Puedes agregar como máximo 15 evidencias por regreso.";
            return RedirectToAction(nameof(Detalle), new { id = viajeId });
        }
        var imagenes = new List<IFormFile>();
        var archivos = new List<IFormFile>();
        foreach (var evidencia in evidencias)
        {
            var extension = Path.GetExtension(evidencia.FileName).ToLowerInvariant();
            if (EsExtensionImagen(extension))
            {
                var validacion = ValidarFotoEvidencia(evidencia);
                if (!validacion.Ok)
                {
                    TempData["LogisticaError"] = validacion.Mensaje;
                    return RedirectToAction(nameof(Detalle), new { id = viajeId });
                }
                imagenes.Add(evidencia);
                continue;
            }
            if (extension == ".pdf")
            {
                var validacion = ValidarArchivoEvidencia(evidencia);
                if (!validacion.Ok)
                {
                    TempData["LogisticaError"] = validacion.Mensaje;
                    return RedirectToAction(nameof(Detalle), new { id = viajeId });
                }
                archivos.Add(evidencia);
                continue;
            }
            TempData["LogisticaError"] = $"El archivo {Path.GetFileName(evidencia.FileName)} no es válido. Solo se permiten imágenes o PDF.";
            return RedirectToAction(nameof(Detalle), new { id = viajeId });
        }
        if (imagenes.Count == 0)
        {
            TempData["LogisticaError"] = "Debes agregar al menos una fotografía. Un PDF por sí solo no permite registrar el regreso.";
            return RedirectToAction(nameof(Detalle), new { id = viajeId });
        }
        await using var cn = await AbrirAsync(cancellationToken);
        if (!await UsuarioEsChoferAsync(cn, UsuarioID.Value, cancellationToken)) return AccesoDenegadoChofer();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var rutasFisicas = new List<string>();
        try
        {
            var viaje = await ObtenerViajeChoferParaActualizarAsync(cn, tx, viajeId, UsuarioID.Value, cancellationToken) ?? throw new InvalidOperationException("El viaje no existe o ya no se encuentra asignado al chofer conectado.");
            if (viaje.Estatus != "En curso") throw new InvalidOperationException("Solo un viaje En curso puede registrar regreso.");
            if (!viaje.FechaSalidaReal.HasValue) throw new InvalidOperationException("El viaje todavía no tiene una salida registrada.");
            if (viaje.FechaRegresoReal.HasValue) throw new InvalidOperationException("El regreso de este viaje ya fue registrado.");
            if (!viaje.UnidadID.HasValue || viaje.UnidadID.Value <= 0) throw new InvalidOperationException("El viaje no tiene una unidad asignada.");
            if (viaje.KilometrajeSalida.HasValue && kilometrajeRegreso.HasValue && kilometrajeRegreso.Value < viaje.KilometrajeSalida.Value) throw new InvalidOperationException($"El kilometraje final no puede ser menor al inicial ({viaje.KilometrajeSalida.Value:N0} km).");
            await ValidarParadasViajeResueltasParaRetornoAsync(cn, tx, viajeId, cancellationToken);
            await ValidarEmbarquesViajeEntregadosAsync(cn, tx, viajeId, cancellationToken);
            const string sqlRetorno = @"
SELECT TOP(1) ViajeParadaID,Secuencia,ISNULL(Lugar,N'') Lugar,ISNULL(Estatus,N'') Estatus
FROM dbo.Logistica_ViajeParadas WITH(UPDLOCK,HOLDLOCK)
WHERE ViajeID=@ViajeID AND Activo=1 AND ISNULL(CierraViaje,0)=1
ORDER BY Secuencia DESC,ViajeParadaID DESC;";
            int retornoId;
            int retornoSecuencia;
            string retornoLugar;
            string retornoEstatus;
            await using (var cmd = new SqlCommand(sqlRetorno, cn, tx))
            {
                cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
                await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
                if (!await rd.ReadAsync(cancellationToken)) throw new InvalidOperationException("El viaje no tiene configurada una parada final de retorno.");
                retornoId = Entero(rd, "ViajeParadaID");
                retornoSecuencia = Entero(rd, "Secuencia");
                retornoLugar = Texto(rd, "Lugar");
                retornoEstatus = Texto(rd, "Estatus");
            }
            if (retornoEstatus == "Completada") throw new InvalidOperationException("La parada de retorno ya se encuentra completada.");
            if (retornoEstatus is "Omitida" or "Cancelada") throw new InvalidOperationException("La parada final de retorno se encuentra cancelada u omitida y debe corregirse antes de cerrar el viaje.");
            var ahora = DateTime.Now;
            const string sqlCompletarRetorno = @"
UPDATE dbo.Logistica_ViajeParadas
SET Estatus=N'Completada',
FechaLlegadaReal=COALESCE(FechaLlegadaReal,@Fecha),
FechaSalidaReal=COALESCE(FechaSalidaReal,@Fecha),
Observaciones=CASE WHEN @Observaciones IS NULL THEN Observaciones WHEN NULLIF(LTRIM(RTRIM(ISNULL(Observaciones,N''))),N'') IS NULL THEN @Observaciones ELSE CONCAT(Observaciones,NCHAR(13),NCHAR(10),@Observaciones) END,
FechaModificacion=SYSDATETIME(),
ActualizadoPor=@Usuario
WHERE ViajeParadaID=@ParadaID
AND ViajeID=@ViajeID
AND Activo=1
AND Estatus IN(N'Pendiente',N'En camino',N'En sitio');
SELECT @@ROWCOUNT;";
            await using (var cmd = new SqlCommand(sqlCompletarRetorno, cn, tx))
            {
                cmd.Parameters.Add("@Fecha", SqlDbType.DateTime2).Value = ahora;
                cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1200).Value = Db(string.IsNullOrWhiteSpace(observaciones) ? null : observaciones);
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@ParadaID", SqlDbType.Int).Value = retornoId;
                cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
                if (Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) != 1) throw new InvalidOperationException("La parada de retorno cambió mientras registrabas el regreso.");
            }
            const string sqlUpdate = @"
UPDATE dbo.Logistica_Viajes
SET Estatus=N'Completado',
FechaRegresoReal=@FechaRegreso,
KilometrajeRegreso=@KilometrajeRegreso,
Observaciones=CASE WHEN @Observaciones IS NULL THEN Observaciones WHEN NULLIF(LTRIM(RTRIM(ISNULL(Observaciones,N''))),N'') IS NULL THEN @Observaciones ELSE CONCAT(Observaciones,NCHAR(13),NCHAR(10),@Observaciones) END,
FechaModificacion=SYSDATETIME(),
ActualizadoPor=@Usuario
WHERE ViajeID=@ViajeID
AND OperadorUsuarioID=@UsuarioID
AND Activo=1
AND Estatus=N'En curso'
AND FechaRegresoReal IS NULL;
SELECT @@ROWCOUNT;";
            await using (var cmd = new SqlCommand(sqlUpdate, cn, tx))
            {
                cmd.Parameters.Add("@FechaRegreso", SqlDbType.DateTime2).Value = ahora;
                cmd.Parameters.Add("@KilometrajeRegreso", SqlDbType.Int).Value = Db(kilometrajeRegreso);
                cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1000).Value = Db(string.IsNullOrWhiteSpace(observaciones) ? null : observaciones);
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = UsuarioID.Value;
                if (Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) != 1) throw new InvalidOperationException("El viaje cambió mientras registrabas el regreso.");
            }
            var embarquesRetornados = await SincronizarRetornoEmbarquesAsync(cn, tx, viajeId, ahora, kilometrajeRegreso, observaciones, cancellationToken);
            for (var i = 0; i < imagenes.Count; i++)
            {
                var evidencia = imagenes[i];
                var guardado = await GuardarArchivoEvidenciaAsync(viajeId, evidencia, $"REGRESO_FOTO_{i + 1:00}", cancellationToken);
                rutasFisicas.Add(guardado.RutaFisica);
                await InsertarEvidenciaAsync(cn, tx, viajeId, "Regreso", guardado.NombreOriginal, guardado.NombreFisico, guardado.RutaRelativa, guardado.TipoContenido, guardado.TamanoBytes, observaciones, cancellationToken);
            }
            for (var i = 0; i < archivos.Count; i++)
            {
                var evidencia = archivos[i];
                var guardado = await GuardarArchivoEvidenciaAsync(viajeId, evidencia, $"REGRESO_ARCHIVO_{i + 1:00}", cancellationToken);
                rutasFisicas.Add(guardado.RutaFisica);
                await InsertarEvidenciaAsync(cn, tx, viajeId, "Regreso", guardado.NombreOriginal, guardado.NombreFisico, guardado.RutaRelativa, guardado.TipoContenido, guardado.TamanoBytes, string.IsNullOrWhiteSpace(observaciones) ? "Archivo adicional de regreso." : $"Archivo adicional de regreso. {observaciones}", cancellationToken);
            }
            await InsertarHistorialParadaAsync(cn, tx, retornoId, "RETORNO_PLANTA_REGISTRADO", retornoEstatus, "Completada", $"Regreso a {retornoLugar} registrado por {UsuarioNombre} el {ahora:dd/MM/yyyy HH:mm}.", cancellationToken);
            var descripcion = $"Regreso registrado por el chofer el {ahora:dd/MM/yyyy HH:mm}. Fotografías: {imagenes.Count}.";
            if (archivos.Count > 0) descripcion += $" Archivos adicionales: {archivos.Count}.";
            if (kilometrajeRegreso.HasValue) descripcion += $" Kilometraje final: {kilometrajeRegreso.Value:N0} km.";
            if (viaje.KilometrajeSalida.HasValue && kilometrajeRegreso.HasValue) descripcion += $" Distancia recorrida: {Math.Max(0, kilometrajeRegreso.Value - viaje.KilometrajeSalida.Value):N0} km.";
            if (!string.IsNullOrWhiteSpace(observaciones)) descripcion += $" Observaciones: {observaciones}";
            descripcion += $" Parada final #{retornoSecuencia} completada.";
            if (embarquesRetornados > 0) descripcion += $" Retorno sincronizado con {embarquesRetornados:N0} embarque(s).";
            await InsertarHistorialAsync(cn, tx, viajeId, "RETORNO_REGISTRADO_CHOFER", "En curso", "Completado", descripcion, cancellationToken);
            await tx.CommitAsync(cancellationToken);
            TempData["LogisticaOk"] = embarquesRetornados > 0 ? $"Regreso registrado correctamente. El viaje, la parada de retorno y {embarquesRetornados:N0} embarque(s) quedaron cerrados operativamente." : "Regreso registrado correctamente. El viaje y su parada de retorno quedaron completados.";
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(cancellationToken); } catch { }
            foreach (var ruta in rutasFisicas)
            {
                if (string.IsNullOrWhiteSpace(ruta) || !System.IO.File.Exists(ruta)) continue;
                try { System.IO.File.Delete(ruta); } catch { }
            }
            TempData["LogisticaError"] = "No fue posible registrar el regreso: " + ex.Message;
        }
        return RedirectToAction(nameof(Detalle), new { id = viajeId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegistrarIncidencia(int viajeId, string? tipo, string? severidad, string? descripcion, List<IFormFile>? evidencias, CancellationToken cancellationToken = default)
    {
        if (!UsuarioID.HasValue || UsuarioID.Value <= 0) return RedirectToAction("Login", "Login");
        if (viajeId <= 0) return RedirectToAction(nameof(Index));
        tipo = tipo?.Trim() ?? string.Empty;
        severidad = severidad?.Trim() ?? string.Empty;
        descripcion = descripcion?.Trim() ?? string.Empty;
        evidencias = (evidencias ?? new List<IFormFile>()).Where(x => x != null && x.Length > 0).ToList();
        var tiposPermitidos = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Unidad","Operador","Tráfico","Retraso","Ruta","Material","Recolección","Cliente / destino","Seguridad","Otro"
    };
        var severidadesPermitidas = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Baja","Media","Alta","Crítica"
    };
        if (!tiposPermitidos.Contains(tipo))
        {
            TempData["LogisticaError"] = "Selecciona un tipo de incidencia válido.";
            return RedirectToAction(nameof(Detalle), new { id = viajeId });
        }
        if (!severidadesPermitidas.Contains(severidad))
        {
            TempData["LogisticaError"] = "Selecciona una severidad válida.";
            return RedirectToAction(nameof(Detalle), new { id = viajeId });
        }
        if (string.IsNullOrWhiteSpace(descripcion))
        {
            TempData["LogisticaError"] = "Describe la incidencia.";
            return RedirectToAction(nameof(Detalle), new { id = viajeId });
        }
        if (descripcion.Length > 1200)
        {
            TempData["LogisticaError"] = "La descripción no puede exceder 1200 caracteres.";
            return RedirectToAction(nameof(Detalle), new { id = viajeId });
        }
        if (evidencias.Count > 15)
        {
            TempData["LogisticaError"] = "Puedes agregar como máximo 15 evidencias por incidencia.";
            return RedirectToAction(nameof(Detalle), new { id = viajeId });
        }
        var imagenes = new List<IFormFile>();
        var archivos = new List<IFormFile>();
        foreach (var evidencia in evidencias)
        {
            var extension = Path.GetExtension(evidencia.FileName).ToLowerInvariant();
            if (EsExtensionImagen(extension))
            {
                var validacion = ValidarFotoEvidencia(evidencia);
                if (!validacion.Ok)
                {
                    TempData["LogisticaError"] = validacion.Mensaje;
                    return RedirectToAction(nameof(Detalle), new { id = viajeId });
                }
                imagenes.Add(evidencia);
                continue;
            }
            if (extension == ".pdf")
            {
                var validacion = ValidarArchivoEvidencia(evidencia);
                if (!validacion.Ok)
                {
                    TempData["LogisticaError"] = validacion.Mensaje;
                    return RedirectToAction(nameof(Detalle), new { id = viajeId });
                }
                archivos.Add(evidencia);
                continue;
            }
            TempData["LogisticaError"] = $"El archivo {Path.GetFileName(evidencia.FileName)} no es válido. Solo se permiten fotografías o archivos PDF.";
            return RedirectToAction(nameof(Detalle), new { id = viajeId });
        }
        await using var cn = await AbrirAsync(cancellationToken);
        if (!await UsuarioEsChoferAsync(cn, UsuarioID.Value, cancellationToken)) return AccesoDenegadoChofer();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var rutasFisicas = new List<string>();
        try
        {
            var viaje = await ObtenerViajeChoferParaActualizarAsync(cn, tx, viajeId, UsuarioID.Value, cancellationToken) ?? throw new InvalidOperationException("El viaje no existe o no está asignado al chofer conectado.");
            if (viaje.Estatus is "Cancelado" or "Completado") throw new InvalidOperationException("Ya no pueden registrarse incidencias operativas en este viaje.");
            const string sql = @"
INSERT dbo.Logistica_ViajeIncidencias
(ViajeID,Tipo,Severidad,Descripcion,Responsable,Estatus,FechaRegistro,UsuarioRegistroID,UsuarioRegistro,Activo)
VALUES
(@ViajeID,@Tipo,@Severidad,@Descripcion,@Responsable,N'Abierta',SYSDATETIME(),@UsuarioID,@Usuario,1);
SELECT CONVERT(int,SCOPE_IDENTITY());";
            int incidenciaId;
            await using (var cmd = new SqlCommand(sql, cn, tx))
            {
                cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
                cmd.Parameters.Add("@Tipo", SqlDbType.NVarChar, 80).Value = tipo;
                cmd.Parameters.Add("@Severidad", SqlDbType.NVarChar, 20).Value = severidad;
                cmd.Parameters.Add("@Descripcion", SqlDbType.NVarChar, 1200).Value = descripcion;
                cmd.Parameters.Add("@Responsable", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = UsuarioID.Value;
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                incidenciaId = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
            }
            await using (var cmd = new SqlCommand(@"UPDATE dbo.Logistica_Viajes SET TieneIncidencia=1,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario WHERE ViajeID=@ViajeID AND OperadorUsuarioID=@UsuarioID AND Activo=1;", cn, tx))
            {
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = UsuarioID.Value;
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            var evidenciaIds = new List<int>();
            for (var i = 0; i < imagenes.Count; i++)
            {
                var evidencia = imagenes[i];
                var guardado = await GuardarArchivoEvidenciaAsync(viajeId, evidencia, $"INCIDENCIA_{incidenciaId}_FOTO_{i + 1:00}", cancellationToken);
                rutasFisicas.Add(guardado.RutaFisica);
                var evidenciaId = await InsertarEvidenciaAsync(cn, tx, viajeId, "Incidencia", guardado.NombreOriginal, guardado.NombreFisico, guardado.RutaRelativa, guardado.TipoContenido, guardado.TamanoBytes, $"Incidencia VINC-{incidenciaId:000000}. {descripcion}", cancellationToken);
                evidenciaIds.Add(evidenciaId);
            }
            for (var i = 0; i < archivos.Count; i++)
            {
                var evidencia = archivos[i];
                var guardado = await GuardarArchivoEvidenciaAsync(viajeId, evidencia, $"INCIDENCIA_{incidenciaId}_ARCHIVO_{i + 1:00}", cancellationToken);
                rutasFisicas.Add(guardado.RutaFisica);
                var evidenciaId = await InsertarEvidenciaAsync(cn, tx, viajeId, "Incidencia", guardado.NombreOriginal, guardado.NombreFisico, guardado.RutaRelativa, guardado.TipoContenido, guardado.TamanoBytes, $"Incidencia VINC-{incidenciaId:000000}. {descripcion}", cancellationToken);
                evidenciaIds.Add(evidenciaId);
            }
            var historial = $"Incidencia VINC-{incidenciaId:000000} registrada por el chofer. {tipo} / {severidad}. {descripcion}";
            if (imagenes.Count > 0) historial += $" Fotografías: {imagenes.Count}.";
            if (archivos.Count > 0) historial += $" Archivos PDF: {archivos.Count}.";
            if (evidenciaIds.Count > 0) historial += $" Total evidencias: {evidenciaIds.Count}.";
            await InsertarHistorialAsync(cn, tx, viajeId, "INCIDENCIA_REGISTRADA_CHOFER", viaje.Estatus, viaje.Estatus, historial, cancellationToken);
            await tx.CommitAsync(cancellationToken);
            TempData["LogisticaOk"] = evidenciaIds.Count > 0
                ? $"Incidencia VINC-{incidenciaId:000000} registrada correctamente con {evidenciaIds.Count} evidencia(s)."
                : $"Incidencia VINC-{incidenciaId:000000} registrada correctamente.";
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(cancellationToken); } catch { }
            foreach (var ruta in rutasFisicas)
            {
                if (string.IsNullOrWhiteSpace(ruta) || !System.IO.File.Exists(ruta)) continue;
                try { System.IO.File.Delete(ruta); } catch { }
            }
            TempData["LogisticaError"] = "No fue posible registrar la incidencia: " + ex.Message;
        }
        return RedirectToAction(nameof(Detalle), new { id = viajeId });
    }

    [HttpGet]
    public async Task<IActionResult> VerEvidencia(int id, CancellationToken cancellationToken = default)
    {
        if (!UsuarioID.HasValue || UsuarioID.Value <= 0) return RedirectToAction("Login", "Login");
        if (id <= 0) return NotFound();

        await using var cn = await AbrirAsync(cancellationToken);

        if (!await UsuarioEsChoferAsync(cn, UsuarioID.Value, cancellationToken))
            return AccesoDenegadoChofer();

        const string sql = @"
SELECT e.ViajeEvidenciaID,e.ViajeID,e.NombreOriginal,e.RutaRelativa,e.TipoContenido
FROM dbo.Logistica_ViajeEvidencias e
INNER JOIN dbo.Logistica_Viajes v ON v.ViajeID=e.ViajeID
WHERE e.ViajeEvidenciaID=@EvidenciaID
AND e.Activo=1
AND v.Activo=1
AND v.OperadorUsuarioID=@UsuarioID;";

        string nombreOriginal, rutaRelativa, tipoContenido;

        await using (var cmd = new SqlCommand(sql, cn))
        {
            cmd.Parameters.Add("@EvidenciaID", SqlDbType.Int).Value = id;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = UsuarioID.Value;

            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);

            if (!await rd.ReadAsync(cancellationToken))
                return NotFound();

            nombreOriginal = Texto(rd, "NombreOriginal");
            rutaRelativa = Texto(rd, "RutaRelativa");
            tipoContenido = Texto(rd, "TipoContenido");
        }

        var rutaFisica = ResolverRutaFisica(rutaRelativa);

        if (!System.IO.File.Exists(rutaFisica))
            return NotFound();

        var bytes = await System.IO.File.ReadAllBytesAsync(rutaFisica, cancellationToken);

        return File(bytes, string.IsNullOrWhiteSpace(tipoContenido) ? "application/octet-stream" : tipoContenido, nombreOriginal);
    }

    [HttpGet]
    public async Task<IActionResult> ObtenerEmbarquesCarga(int viajeId, CancellationToken cancellationToken = default)
    {
        if (!UsuarioID.HasValue || UsuarioID.Value <= 0) return Unauthorized(new { ok = false, mensaje = "La sesión terminó. Inicia sesión nuevamente." });
        if (viajeId <= 0) return BadRequest(new { ok = false, mensaje = "El viaje indicado no es válido." });
        await using var cn = await AbrirAsync(cancellationToken);
        if (!await UsuarioEsChoferAsync(cn, UsuarioID.Value, cancellationToken)) return Forbid();
        if (!await ViajeAsignadoAlChoferAsync(cn, viajeId, UsuarioID.Value, cancellationToken)) return Forbid();
        const string sql = @"
SELECT e.EmbarqueID,ISNULL(e.Folio,N'') Folio,ISNULL(e.ClienteNombreSnapshot,N'') Cliente,ISNULL(e.Destino,N'') Destino,ISNULL(e.Estatus,N'') Estatus,ISNULL(e.FormaEnvio,N'Pendiente') FormaEnvio,
ve.ViajeParadaID,p.Secuencia SecuenciaParada,ISNULL(p.TipoParada,N'') TipoParada,ISNULL(p.TipoOperacion,N'') TipoOperacion,ISNULL(p.Lugar,N'') LugarParada,
ISNULL(d.TotalSolicitado,0) TotalSolicitado,ISNULL(c.PiezasCargadas,0) PiezasCargadas,ISNULL(c.PiezasReservadas,0) PiezasReservadas,ISNULL(c.CajasAsignadas,0) CajasAsignadas,ISNULL(c.CajasCargadas,0) CajasCargadas
FROM dbo.Logistica_ViajeEmbarques ve
INNER JOIN dbo.Logistica_Viajes v ON v.ViajeID=ve.ViajeID AND v.Activo=1
INNER JOIN dbo.Logistica_Embarques e ON e.EmbarqueID=ve.EmbarqueID AND e.Activo=1
LEFT JOIN dbo.Logistica_ViajeParadas p ON p.ViajeParadaID=ve.ViajeParadaID AND p.Activo=1
OUTER APPLY(SELECT ISNULL(SUM(CONVERT(bigint,x.CantidadSolicitada)),0) TotalSolicitado FROM dbo.Logistica_EmbarqueDetalle x WHERE x.EmbarqueID=e.EmbarqueID AND x.Activo=1)d
OUTER APPLY(SELECT COUNT(DISTINCT x.CajaID) CajasAsignadas,COUNT(DISTINCT CASE WHEN x.EstatusSeleccion IN(N'Cargada',N'Despachada') THEN x.CajaID END) CajasCargadas,ISNULL(SUM(CASE WHEN x.EstatusSeleccion IN(N'Cargada',N'Despachada') THEN CONVERT(bigint,x.CantidadAsignada) ELSE 0 END),0) PiezasCargadas,ISNULL(SUM(CASE WHEN x.EstatusSeleccion=N'Reservada' THEN CONVERT(bigint,x.CantidadAsignada) ELSE 0 END),0) PiezasReservadas FROM dbo.Logistica_EmbarqueCajas x WHERE x.EmbarqueID=e.EmbarqueID AND x.Activo=1)c
WHERE ve.ViajeID=@ViajeID AND ve.Activo=1 AND v.OperadorUsuarioID=@UsuarioID AND ISNULL(e.FormaEnvio,N'')=N'Interno'
ORDER BY ISNULL(p.Secuencia,2147483647),ISNULL(ve.OrdenEntrega,2147483647),ve.ViajeEmbarqueID;";
        var embarques = new List<object>();
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
        cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = UsuarioID.Value;
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await rd.ReadAsync(cancellationToken))
        {
            var estatus = Texto(rd, "Estatus");
            var totalSolicitado = EnteroLargo(rd, "TotalSolicitado");
            var piezasCargadas = EnteroLargo(rd, "PiezasCargadas");
            var piezasReservadas = EnteroLargo(rd, "PiezasReservadas");
            var cajasAsignadas = Entero(rd, "CajasAsignadas");
            var cajasCargadas = Entero(rd, "CajasCargadas");
            var cargaCompleta = totalSolicitado > 0 && piezasCargadas == totalSolicitado && piezasReservadas == 0;
            embarques.Add(new
            {
                embarqueId = Entero(rd, "EmbarqueID"),
                folio = Texto(rd, "Folio"),
                cliente = Texto(rd, "Cliente"),
                destino = Texto(rd, "Destino"),
                estatus,
                viajeParadaId = EnteroNullable(rd, "ViajeParadaID"),
                secuenciaParada = EnteroNullable(rd, "SecuenciaParada"),
                tipoParada = Texto(rd, "TipoParada"),
                tipoOperacion = Texto(rd, "TipoOperacion"),
                lugarParada = Texto(rd, "LugarParada"),
                totalPiezas = totalSolicitado,
                piezasCargadas,
                piezasReservadas,
                piezasPendientes = Math.Max(0, totalSolicitado - piezasCargadas),
                cajasAsignadas,
                cajasCargadas,
                cargaCompleta,
                puedeEscanear = estatus is "Preparado" or "Cargando",
                puedeConfirmarCarga = estatus == "Cargando" && cargaCompleta,
                listoParaSalida = estatus == "Cargado"
            });
        }
        return Json(new { ok = true, viajeId, embarques });
    }

    private static async Task ValidarEmbarquesViajeEntregadosAsync(SqlConnection cn, SqlTransaction tx, int viajeId, CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT TOP(1) ISNULL(e.Folio,N'') Folio,ISNULL(e.Estatus,N'') Estatus
FROM dbo.Logistica_ViajeEmbarques ve
INNER JOIN dbo.Logistica_Embarques e WITH(UPDLOCK,HOLDLOCK) ON e.EmbarqueID=ve.EmbarqueID
WHERE ve.ViajeID=@ViajeID
AND ve.Activo=1
AND e.Activo=1
AND e.Estatus NOT IN(N'Entregado',N'Cancelado')
ORDER BY ISNULL(ve.OrdenEntrega,2147483647),ve.ViajeEmbarqueID;";
        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await rd.ReadAsync(cancellationToken)) return;
        var folio = Texto(rd, "Folio");
        var estatus = Texto(rd, "Estatus");
        throw new InvalidOperationException($"Todavía no puedes registrar el regreso. El embarque {folio} continúa en estatus {estatus}.");
    }

    private async Task<int> SincronizarRetornoEmbarquesAsync(SqlConnection cn, SqlTransaction tx, int viajeId, DateTime fechaRetorno, int? kilometrajeRetorno, string? observaciones, CancellationToken cancellationToken)
    {
        const string sqlObtener = @"
SELECT e.EmbarqueID,ISNULL(e.Folio,N'') Folio
FROM dbo.Logistica_ViajeEmbarques ve WITH(UPDLOCK,HOLDLOCK)
INNER JOIN dbo.Logistica_Embarques e WITH(UPDLOCK,HOLDLOCK)
    ON e.EmbarqueID=ve.EmbarqueID
WHERE ve.ViajeID=@ViajeID
AND ve.Activo=1
AND e.Activo=1
AND e.Estatus=N'Entregado'
AND ISNULL(e.FormaEnvio,N'')=N'Interno'
AND e.FechaRetornoUnidad IS NULL
ORDER BY ISNULL(ve.OrdenEntrega,2147483647),ve.ViajeEmbarqueID;";
        var embarques = new List<(int EmbarqueID, string Folio)>();
        await using (var cmd = new SqlCommand(sqlObtener, cn, tx))
        {
            cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await rd.ReadAsync(cancellationToken))
            {
                embarques.Add((Entero(rd, "EmbarqueID"), Texto(rd, "Folio")));
            }
        }
        if (embarques.Count == 0) return 0;
        var observacionRetorno = $"Retorno de unidad registrado automáticamente desde el viaje VIA-{viajeId:000000}.";
        if (!string.IsNullOrWhiteSpace(observaciones)) observacionRetorno += $" {observaciones.Trim()}";
        var sincronizados = 0;
        foreach (var embarque in embarques)
        {
            const string sqlActualizar = @"
UPDATE dbo.Logistica_Embarques
SET FechaRetornoUnidad=@FechaRetorno,
KilometrajeRetorno=@KilometrajeRetorno,
ObservacionesRetorno=CASE
    WHEN NULLIF(LTRIM(RTRIM(ISNULL(ObservacionesRetorno,N''))),N'') IS NULL THEN @ObservacionesRetorno
    ELSE CONCAT(ObservacionesRetorno,NCHAR(13),NCHAR(10),@ObservacionesRetorno)
END,
RetornoPorNombre=@UsuarioNombre,
FechaModificacion=SYSDATETIME(),
ActualizadoPor=@UsuarioNombre
WHERE EmbarqueID=@EmbarqueID
AND Activo=1
AND Estatus=N'Entregado'
AND ISNULL(FormaEnvio,N'')=N'Interno'
AND FechaRetornoUnidad IS NULL;
SELECT @@ROWCOUNT;";
            await using (var cmd = new SqlCommand(sqlActualizar, cn, tx))
            {
                cmd.Parameters.Add("@FechaRetorno", SqlDbType.DateTime2).Value = fechaRetorno;
                cmd.Parameters.Add("@KilometrajeRetorno", SqlDbType.Int).Value = Db(kilometrajeRetorno);
                cmd.Parameters.Add("@ObservacionesRetorno", SqlDbType.NVarChar, 1200).Value = observacionRetorno;
                cmd.Parameters.Add("@UsuarioNombre", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarque.EmbarqueID;
                var afectados = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
                if (afectados != 1) throw new InvalidOperationException($"No fue posible registrar el retorno de unidad del embarque {embarque.Folio}.");
            }
            var descripcion = $"Retorno de unidad confirmado desde el viaje VIA-{viajeId:000000} por {UsuarioNombre} el {fechaRetorno:dd/MM/yyyy HH:mm}.";
            if (kilometrajeRetorno.HasValue) descripcion += $" Kilometraje de retorno: {kilometrajeRetorno.Value:N0} km.";
            if (!string.IsNullOrWhiteSpace(observaciones)) descripcion += $" Observaciones: {observaciones.Trim()}";
            await InsertarHistorialEmbarqueAsync(cn, tx, embarque.EmbarqueID, "RETORNO_UNIDAD_CONFIRMADO", "Entregado", "Entregado", descripcion, cancellationToken);
            sincronizados++;
        }
        return sincronizados;
    }
    private async Task<List<LogisticaViajeDetalleVm>> ObtenerViajesChoferAsync(SqlConnection cn, int usuarioId, CancellationToken cancellationToken)
    {
        var lista = new List<LogisticaViajeDetalleVm>();
        const string sql = @"
SELECT v.ViajeID,ISNULL(v.Folio,N'') Folio,ISNULL(v.TipoViaje,N'') TipoViaje,ISNULL(v.Origen,N'') Origen,ISNULL(v.Destino,N'') Destino,ISNULL(v.Motivo,N'') Motivo,v.FechaProgramada,v.HoraSalidaProgramada,v.FechaSalidaReal,v.FechaRegresoReal,v.RutaID,ISNULL(r.Codigo+N' - '+r.Nombre,N'') Ruta,v.UnidadID,ISNULL(u.NumeroEconomico+CASE WHEN NULLIF(u.Placas,N'') IS NULL THEN N'' ELSE N' - '+u.Placas END,N'') Unidad,v.OperadorUsuarioID,ISNULL(NULLIF(v.OperadorNombreSnapshot,N''),ISNULL(v.OperadorTexto,N'')) Operador,ISNULL(v.Estatus,N'') Estatus,ISNULL(v.TieneIncidencia,0) TieneIncidencia,v.KilometrajeSalida,v.KilometrajeRegreso,ISNULL(v.Observaciones,N'') Observaciones,v.ResponsableUsuarioID,ISNULL(v.ResponsableNombreSnapshot,N'') UsuarioResponsable,v.FechaCreacion,ISNULL(v.CreadoPor,N'') CreadoPor,
CONVERT(bit,CASE WHEN ISNULL(v.EsMultiParada,0)=1 OR ISNULL(ps.Operativas,0)>1 THEN 1 ELSE 0 END) EsMultiParada,
ISNULL(ps.TotalParadas,0) TotalParadas,ISNULL(ps.Completadas,0) ParadasCompletadas,ISNULL(ps.Pendientes,0) ParadasPendientes,
prox.ViajeParadaID ProximaParadaID,prox.Secuencia ProximaSecuencia,ISNULL(prox.TipoParada,N'') ProximaTipoParada,ISNULL(prox.TipoOperacion,N'') ProximaOperacion,ISNULL(prox.Lugar,N'') ProximaLugar,ISNULL(prox.Direccion,N'') ProximaDireccion,prox.FechaHoraLlegadaProgramada ProximaLlegada,ISNULL(prox.Estatus,N'') ProximaEstatus
FROM dbo.Logistica_Viajes v
LEFT JOIN dbo.Logistica_Rutas r ON r.RutaID=v.RutaID
LEFT JOIN dbo.Logistica_Unidades u ON u.UnidadID=v.UnidadID
OUTER APPLY(SELECT CONVERT(int,COUNT_BIG(*)) TotalParadas,ISNULL(SUM(CASE WHEN p.Estatus IN(N'Completada',N'Omitida',N'Cancelada') THEN 1 ELSE 0 END),0) Completadas,ISNULL(SUM(CASE WHEN p.Estatus IN(N'Pendiente',N'En camino',N'En sitio') THEN 1 ELSE 0 END),0) Pendientes,ISNULL(SUM(CASE WHEN p.TipoParada<>N'Origen' AND ISNULL(p.CierraViaje,0)=0 THEN 1 ELSE 0 END),0) Operativas FROM dbo.Logistica_ViajeParadas p WHERE p.ViajeID=v.ViajeID AND p.Activo=1)ps
OUTER APPLY(SELECT TOP(1) p.ViajeParadaID,p.Secuencia,p.TipoParada,p.TipoOperacion,p.Lugar,p.Direccion,p.FechaHoraLlegadaProgramada,p.Estatus,p.CierraViaje FROM dbo.Logistica_ViajeParadas p WHERE p.ViajeID=v.ViajeID AND p.Activo=1 AND p.TipoParada<>N'Origen' AND p.Estatus IN(N'Pendiente',N'En camino',N'En sitio') ORDER BY CASE WHEN p.CierraViaje=1 THEN 1 ELSE 0 END,p.Secuencia,p.ViajeParadaID)prox
WHERE v.Activo=1 AND v.OperadorUsuarioID=@UsuarioID AND(v.Estatus IN(N'Programado',N'En curso') OR(v.Estatus=N'Completado' AND ISNULL(v.FechaRegresoReal,v.FechaProgramada)>=DATEADD(DAY,-7,CAST(GETDATE() AS date))))
ORDER BY CASE v.Estatus WHEN N'En curso' THEN 0 WHEN N'Programado' THEN 1 ELSE 2 END,v.FechaProgramada,v.HoraSalidaProgramada,v.ViajeID;";
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await rd.ReadAsync(cancellationToken))
        {
            var vm = new LogisticaViajeDetalleVm
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
                TieneIncidencia = Booleano(rd, "TieneIncidencia"),
                EsMultiParada = Booleano(rd, "EsMultiParada"),
                TotalParadas = Entero(rd, "TotalParadas"),
                ParadasCompletadas = Entero(rd, "ParadasCompletadas"),
                ParadasPendientes = Entero(rd, "ParadasPendientes"),
                KilometrajeSalida = EnteroNullable(rd, "KilometrajeSalida"),
                KilometrajeRegreso = EnteroNullable(rd, "KilometrajeRegreso"),
                Observaciones = Texto(rd, "Observaciones"),
                UsuarioResponsableID = EnteroNullable(rd, "ResponsableUsuarioID"),
                UsuarioResponsable = Texto(rd, "UsuarioResponsable"),
                FechaCreacion = Fecha(rd, "FechaCreacion") ?? DateTime.MinValue,
                CreadoPor = Texto(rd, "CreadoPor")
            };
            var proximaId = EnteroNullable(rd, "ProximaParadaID");
            if (proximaId.HasValue)
            {
                vm.Paradas.Add(new LogisticaViajeParadaVm
                {
                    ViajeParadaID = proximaId.Value,
                    ViajeID = vm.ViajeID,
                    Secuencia = Entero(rd, "ProximaSecuencia"),
                    TipoParada = Texto(rd, "ProximaTipoParada"),
                    TipoOperacion = Texto(rd, "ProximaOperacion"),
                    Lugar = Texto(rd, "ProximaLugar"),
                    Direccion = Texto(rd, "ProximaDireccion"),
                    FechaHoraLlegadaProgramada = Fecha(rd, "ProximaLlegada"),
                    Estatus = Texto(rd, "ProximaEstatus"),
                    Activo = true
                });
            }
            lista.Add(vm);
        }
        return lista;
    }

    private async Task<LogisticaViajeDetalleVm?> CargarDetalleChoferAsync(SqlConnection cn, int viajeId, int usuarioId, CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT
    v.ViajeID,
    ISNULL(v.Folio,N'') Folio,
    ISNULL(v.TipoViaje,N'') TipoViaje,
    ISNULL(v.Origen,N'') Origen,
    ISNULL(v.Destino,N'') Destino,
    ISNULL(v.Motivo,N'') Motivo,
    v.FechaProgramada,
    v.HoraSalidaProgramada,
    v.FechaSalidaReal,
    v.FechaRegresoReal,
    v.RutaID,
    ISNULL(r.Codigo+N' - '+r.Nombre,N'') Ruta,
    v.UnidadID,
    ISNULL(u.NumeroEconomico+CASE WHEN NULLIF(u.Placas,N'') IS NULL THEN N'' ELSE N' - '+u.Placas END,N'') Unidad,
    v.OperadorUsuarioID,
    ISNULL(NULLIF(v.OperadorNombreSnapshot,N''),ISNULL(v.OperadorTexto,N'')) Operador,
    ISNULL(v.Estatus,N'') Estatus,
    ISNULL(v.Observaciones,N'') Observaciones,
    ISNULL(v.TieneIncidencia,0) TieneIncidencia,
ISNULL(v.EsMultiParada,0) EsMultiParada,
    v.KilometrajeSalida,
    v.KilometrajeRegreso,
    v.ResponsableUsuarioID,
    ISNULL(v.ResponsableNombreSnapshot,N'') UsuarioResponsable,
    v.FechaCreacion,
    ISNULL(v.CreadoPor,N'') CreadoPor,
    v.PagoGasolina
FROM dbo.Logistica_Viajes v
LEFT JOIN dbo.Logistica_Rutas r ON r.RutaID=v.RutaID
LEFT JOIN dbo.Logistica_Unidades u ON u.UnidadID=v.UnidadID
WHERE v.ViajeID=@ViajeID
AND v.OperadorUsuarioID=@UsuarioID
AND v.Activo=1;";

        LogisticaViajeDetalleVm? vm = null;

        await using (var cmd = new SqlCommand(sql, cn))
        {
            cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;

            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);

            if (!await rd.ReadAsync(cancellationToken))
                return null;

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
                UsuarioResponsableID = EnteroNullable(rd, "ResponsableUsuarioID"),
                UsuarioResponsable = Texto(rd, "UsuarioResponsable"),
                FechaCreacion = Fecha(rd, "FechaCreacion") ?? DateTime.MinValue,
                CreadoPor = Texto(rd, "CreadoPor"),
                PagoGasolina = DecimalNullable(rd, "PagoGasolina")
            };
        }

        const string sqlEvidencias = @"
SELECT
    ViajeEvidenciaID,
    ViajeID,
    ISNULL(TipoEvidencia,N'') TipoEvidencia,
    ISNULL(NombreOriginal,N'') NombreOriginal,
    ISNULL(NombreFisico,N'') NombreFisico,
    ISNULL(RutaRelativa,N'') RutaRelativa,
    ISNULL(TipoContenido,N'') TipoContenido,
    ISNULL(TamanoBytes,0) TamanoBytes,
    ISNULL(Observaciones,N'') Observaciones,
    UsuarioCargaID,
    ISNULL(UsuarioCargaNombre,N'') UsuarioCargaNombre,
    FechaCarga
FROM dbo.Logistica_ViajeEvidencias
WHERE ViajeID=@ViajeID
AND Activo=1
ORDER BY FechaCarga DESC,ViajeEvidenciaID DESC;";

        await using (var cmd = new SqlCommand(sqlEvidencias, cn))
        {
            cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
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

        const string sqlHistorial = @"
SELECT
    HistorialID,
    ViajeID,
    Evento,
    ISNULL(EstadoAnterior,N'') EstadoAnterior,
    ISNULL(EstadoNuevo,N'') EstadoNuevo,
    ISNULL(Observaciones,N'') Observaciones,
    UsuarioID,
    ISNULL(UsuarioNombre,N'') Usuario,
    FechaEvento
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
    ViajeIncidenciaID,
    ViajeID,
    Tipo,
    Severidad,
    Descripcion,
    Estatus,
    ISNULL(Responsable,N'') Responsable,
    FechaRegistro,
    FechaCierre
FROM dbo.Logistica_ViajeIncidencias
WHERE ViajeID=@ViajeID
AND Activo=1
ORDER BY
    CASE WHEN Estatus IN(N'Abierta',N'En seguimiento') THEN 0 ELSE 1 END,
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
        await CargarParadasChoferAsync(cn, vm, cancellationToken);

        vm.TieneIncidencia = vm.Incidencias.Any(x => x.EstaAbierta);

        return vm;
    }

    private static async Task<bool> UsuarioEsChoferAsync(SqlConnection cn, int usuarioId, CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT COUNT_BIG(*)
FROM dbo.Usuarios u
INNER JOIN dbo.Persona p ON p.PersonaID=u.PersonaID
INNER JOIN dbo.Departamentos d ON d.DepartamentoID=u.DepartamentoID
WHERE u.UsuarioID=@UsuarioID
AND ISNULL(u.Activo,0)=1
AND ISNULL(d.Activo,0)=1
AND UPPER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(d.NombreDepartamento,N''))),N'Í',N'I'),N'Ó',N'O'))=N'LOGISTICA'
AND UPPER(LTRIM(RTRIM(ISNULL(p.Puesto,N'')))) LIKE N'%CHOFER%';";

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;

        return Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static async Task<bool> ViajeAsignadoAlChoferAsync(SqlConnection cn, int viajeId, int usuarioId, CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT COUNT_BIG(*)
FROM dbo.Logistica_Viajes
WHERE ViajeID=@ViajeID
AND OperadorUsuarioID=@UsuarioID
AND Activo=1;";

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
        cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;

        return Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static async Task<(string Estatus, int? UnidadID, DateTime? FechaSalidaReal, DateTime? FechaRegresoReal, int? KilometrajeSalida)?> ObtenerViajeChoferParaActualizarAsync(SqlConnection cn, SqlTransaction tx, int viajeId, int usuarioId, CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT ISNULL(Estatus,N'') Estatus,UnidadID,FechaSalidaReal,FechaRegresoReal,KilometrajeSalida
FROM dbo.Logistica_Viajes WITH(UPDLOCK,HOLDLOCK)
WHERE ViajeID=@ViajeID
AND OperadorUsuarioID=@UsuarioID
AND Activo=1;";

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
        cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;

        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);

        if (!await rd.ReadAsync(cancellationToken))
            return null;

        return (
            Texto(rd, "Estatus"),
            EnteroNullable(rd, "UnidadID"),
            Fecha(rd, "FechaSalidaReal"),
            Fecha(rd, "FechaRegresoReal"),
            EnteroNullable(rd, "KilometrajeSalida")
        );
    }

    private async Task<(int Vinculados, int EntregadosAhora)> CompletarEmbarquesParadaAsync(SqlConnection cn, SqlTransaction tx, int viajeId, int viajeParadaId, DateTime fechaEntrega, string? receptorNombre, string? folioRemision, CancellationToken cancellationToken)
    {
        receptorNombre = receptorNombre?.Trim();
        folioRemision = folioRemision?.Trim();
        const string sql = @"
SELECT e.EmbarqueID,ISNULL(e.Folio,N'') Folio,ISNULL(e.Estatus,N'') Estatus,ISNULL(e.FormaEnvio,N'') FormaEnvio,
ISNULL(d.TotalPartidas,0) TotalPartidas,ISNULL(d.TotalSolicitado,0) TotalSolicitado,ISNULL(d.TotalDespachado,0) TotalDespachado,
ISNULL(d.PartidasSinDespacho,0) PartidasSinDespacho,ISNULL(d.PartidasInconsistentes,0) PartidasInconsistentes
FROM dbo.Logistica_ViajeEmbarques ve WITH(UPDLOCK,HOLDLOCK)
INNER JOIN dbo.Logistica_Embarques e WITH(UPDLOCK,HOLDLOCK) ON e.EmbarqueID=ve.EmbarqueID AND e.Activo=1
OUTER APPLY
(
    SELECT COUNT_BIG(*) TotalPartidas,
    ISNULL(SUM(CONVERT(bigint,x.CantidadSolicitada)),0) TotalSolicitado,
    ISNULL(SUM(CONVERT(bigint,ISNULL(x.CantidadDespachada,0))),0) TotalDespachado,
    ISNULL(SUM(CASE WHEN ISNULL(x.CantidadDespachada,0)<=0 THEN 1 ELSE 0 END),0) PartidasSinDespacho,
    ISNULL(SUM(CASE WHEN ISNULL(x.CantidadDespachada,0)>ISNULL(x.CantidadSolicitada,0) THEN 1 ELSE 0 END),0) PartidasInconsistentes
    FROM dbo.Logistica_EmbarqueDetalle x WITH(UPDLOCK,HOLDLOCK)
    WHERE x.EmbarqueID=e.EmbarqueID AND x.Activo=1
) d
WHERE ve.ViajeID=@ViajeID AND ve.ViajeParadaID=@ViajeParadaID AND ve.Activo=1 AND e.Estatus<>N'Cancelado'
ORDER BY ISNULL(ve.OrdenEntrega,2147483647),ve.ViajeEmbarqueID;";
        var embarques = new List<(int EmbarqueID, string Folio, string Estatus, string FormaEnvio, long TotalPartidas, long TotalSolicitado, long TotalDespachado, long PartidasSinDespacho, long PartidasInconsistentes)>();
        await using (var cmd = new SqlCommand(sql, cn, tx))
        {
            cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
            cmd.Parameters.Add("@ViajeParadaID", SqlDbType.Int).Value = viajeParadaId;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await rd.ReadAsync(cancellationToken))
            {
                embarques.Add((Entero(rd, "EmbarqueID"), Texto(rd, "Folio"), Texto(rd, "Estatus"), Texto(rd, "FormaEnvio"), EnteroLargo(rd, "TotalPartidas"), EnteroLargo(rd, "TotalSolicitado"), EnteroLargo(rd, "TotalDespachado"), EnteroLargo(rd, "PartidasSinDespacho"), EnteroLargo(rd, "PartidasInconsistentes")));
            }
        }
        if (embarques.Count > 0 && string.IsNullOrWhiteSpace(receptorNombre)) throw new InvalidOperationException("Debes registrar el nombre de la persona que recibe antes de confirmar la entrega.");
        if (!string.IsNullOrWhiteSpace(receptorNombre) && receptorNombre.Length > 200) throw new InvalidOperationException("El nombre de quien recibe no puede exceder 200 caracteres.");
        if (!string.IsNullOrWhiteSpace(folioRemision) && folioRemision.Length > 100) throw new InvalidOperationException("El folio de remisión no puede exceder 100 caracteres.");
        var entregadosAhora = 0;
        foreach (var embarque in embarques)
        {
            if (!embarque.FormaEnvio.Equals("Interno", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"El embarque {embarque.Folio} no corresponde a una Entrega realizada por unidad interna.");
            if (embarque.Estatus == "Entregado") continue;
            if (embarque.Estatus != "En ruta") throw new InvalidOperationException($"El embarque {embarque.Folio} continúa en estatus {embarque.Estatus}. Solo puede entregarse cuando está En ruta.");
            if (embarque.TotalPartidas <= 0) throw new InvalidOperationException($"El embarque {embarque.Folio} no contiene partidas.");
            if (embarque.TotalSolicitado <= 0) throw new InvalidOperationException($"El embarque {embarque.Folio} no contiene una cantidad solicitada válida.");
            if (embarque.PartidasSinDespacho > 0) throw new InvalidOperationException($"El embarque {embarque.Folio} contiene partidas sin despachar.");
            if (embarque.PartidasInconsistentes > 0) throw new InvalidOperationException($"El embarque {embarque.Folio} contiene cantidades despachadas superiores a las solicitadas.");
            if (embarque.TotalDespachado != embarque.TotalSolicitado) throw new InvalidOperationException($"El embarque {embarque.Folio} no está completamente despachado. Solicitadas: {embarque.TotalSolicitado:N0} PZA. Despachadas: {embarque.TotalDespachado:N0} PZA.");
            const string sqlEntregar = @"
UPDATE dbo.Logistica_EmbarqueDetalle
SET CantidadEntregada=ISNULL(CantidadDespachada,0),FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
WHERE EmbarqueID=@EmbarqueID AND Activo=1;
UPDATE dbo.Logistica_Embarques
SET Estatus=N'Entregado',
FechaEntrega=@FechaEntrega,
ReceptorNombre=@ReceptorNombre,
FolioRemision=@FolioRemision,
EntregaPorUsuarioID=@UsuarioID,
FechaModificacion=SYSDATETIME(),
ActualizadoPor=@Usuario
WHERE EmbarqueID=@EmbarqueID AND Activo=1 AND Estatus=N'En ruta';
SELECT @@ROWCOUNT;";
            await using (var cmd = new SqlCommand(sqlEntregar, cn, tx))
            {
                cmd.Parameters.Add("@FechaEntrega", SqlDbType.DateTime2).Value = fechaEntrega;
                cmd.Parameters.Add("@ReceptorNombre", SqlDbType.NVarChar, 200).Value = receptorNombre!;
                cmd.Parameters.Add("@FolioRemision", SqlDbType.NVarChar, 100).Value = Db(string.IsNullOrWhiteSpace(folioRemision) ? null : folioRemision);
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = Db(UsuarioID);
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarque.EmbarqueID;
                if (Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) != 1) throw new InvalidOperationException($"El embarque {embarque.Folio} cambió mientras se confirmaba la entrega.");
            }
            var historial = $"Entrega confirmada desde la parada {viajeParadaId} del viaje VIA-{viajeId:000000} por {UsuarioNombre} el {fechaEntrega:dd/MM/yyyy HH:mm}. Recibió: {receptorNombre}.";
            if (!string.IsNullOrWhiteSpace(folioRemision)) historial += $" Remisión: {folioRemision}.";
            await InsertarHistorialEmbarqueAsync(cn, tx, embarque.EmbarqueID, "ENTREGA_CONFIRMADA_POR_CHOFER", "En ruta", "Entregado", historial, cancellationToken);
            entregadosAhora++;
        }
        return (embarques.Count, entregadosAhora);
    }
    private static async Task ValidarParadasViajeResueltasParaRetornoAsync(SqlConnection cn, SqlTransaction tx, int viajeId, CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT TOP(1) Secuencia,ISNULL(TipoOperacion,N'') TipoOperacion,ISNULL(Lugar,N'') Lugar,ISNULL(Estatus,N'') Estatus
FROM dbo.Logistica_ViajeParadas WITH(UPDLOCK,HOLDLOCK)
WHERE ViajeID=@ViajeID AND Activo=1 AND TipoParada<>N'Origen' AND ISNULL(CierraViaje,0)=0
AND Estatus NOT IN(N'Completada',N'Omitida',N'Cancelada')
ORDER BY Secuencia,ViajeParadaID;";
        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await rd.ReadAsync(cancellationToken)) return;
        var secuencia = Entero(rd, "Secuencia");
        var operacion = Texto(rd, "TipoOperacion");
        var lugar = Texto(rd, "Lugar");
        var estatus = Texto(rd, "Estatus");
        throw new InvalidOperationException($"Todavía no puedes registrar el regreso. La parada #{secuencia} {operacion} - {lugar} continúa en estatus {estatus}.");
    }
    private static async Task ValidarChoferYUnidadDisponiblesAsync(SqlConnection cn, SqlTransaction tx, int viajeId, int usuarioId, int unidadId, CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT TOP(1) Fuente,Folio,Recurso
FROM
(
    SELECT N'VIAJE' Fuente,ISNULL(v.Folio,N'') Folio,CASE WHEN v.OperadorUsuarioID=@UsuarioID THEN N'CHOFER' ELSE N'UNIDAD' END Recurso,ISNULL(v.FechaSalidaReal,CAST(v.FechaProgramada AS datetime2)) FechaOrden
    FROM dbo.Logistica_Viajes v WITH(UPDLOCK,HOLDLOCK)
    WHERE v.Activo=1 AND v.ViajeID<>@ViajeID AND v.Estatus=N'En curso' AND v.FechaRegresoReal IS NULL AND (v.OperadorUsuarioID=@UsuarioID OR v.UnidadID=@UnidadID)
    UNION ALL
    SELECT N'EMBARQUE',ISNULL(e.Folio,N''),CASE WHEN e.ChoferUsuarioID=@UsuarioID THEN N'CHOFER' ELSE N'UNIDAD' END,ISNULL(e.FechaSalida,CAST(e.FechaCargaProgramada AS datetime2))
    FROM dbo.Logistica_Embarques e WITH(UPDLOCK,HOLDLOCK)
    WHERE e.Activo=1 AND ISNULL(e.FormaEnvio,N'')=N'Interno' AND e.Estatus=N'En ruta' AND (e.ChoferUsuarioID=@UsuarioID OR e.UnidadID=@UnidadID)
    AND NOT EXISTS
    (
        SELECT 1
        FROM dbo.Logistica_ViajeEmbarques ve
        INNER JOIN dbo.Logistica_Viajes vx ON vx.ViajeID=ve.ViajeID AND vx.Activo=1 AND vx.Estatus=N'En curso' AND vx.FechaRegresoReal IS NULL
        WHERE ve.EmbarqueID=e.EmbarqueID AND ve.Activo=1
    )
) X
ORDER BY FechaOrden;";
        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
        cmd.Parameters.Add("@UnidadID", SqlDbType.Int).Value = unidadId;
        cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await rd.ReadAsync(cancellationToken)) return;
        var fuente = Texto(rd, "Fuente");
        var folio = Texto(rd, "Folio");
        var recurso = Texto(rd, "Recurso");
        var descripcion = fuente == "VIAJE" ? "viaje" : "embarque";
        if (recurso == "CHOFER") throw new InvalidOperationException($"Todavía estás atendiendo el {descripcion} {folio}. Primero registra el regreso antes de iniciar otro viaje.");
        throw new InvalidOperationException($"La unidad continúa ocupada por el {descripcion} {folio}. No puede iniciar otro viaje todavía.");
    }

    private async Task<int> InsertarEvidenciaAsync(SqlConnection cn, SqlTransaction tx, int viajeId, string tipo, string nombreOriginal, string nombreFisico, string rutaRelativa, string tipoContenido, long tamanoBytes, string? observaciones, CancellationToken cancellationToken)
    {
        const string sql = @"
INSERT dbo.Logistica_ViajeEvidencias
(ViajeID,TipoEvidencia,NombreOriginal,NombreFisico,RutaRelativa,TipoContenido,TamanoBytes,Observaciones,UsuarioCargaID,UsuarioCargaNombre,FechaCarga,Activo)
VALUES
(@ViajeID,@Tipo,@NombreOriginal,@NombreFisico,@RutaRelativa,@TipoContenido,@TamanoBytes,@Observaciones,@UsuarioID,@Usuario,SYSDATETIME(),1);
SELECT CONVERT(int,SCOPE_IDENTITY());";

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
        cmd.Parameters.Add("@Tipo", SqlDbType.NVarChar, 50).Value = tipo;
        cmd.Parameters.Add("@NombreOriginal", SqlDbType.NVarChar, 260).Value = nombreOriginal;
        cmd.Parameters.Add("@NombreFisico", SqlDbType.NVarChar, 260).Value = nombreFisico;
        cmd.Parameters.Add("@RutaRelativa", SqlDbType.NVarChar, 600).Value = rutaRelativa;
        cmd.Parameters.Add("@TipoContenido", SqlDbType.NVarChar, 150).Value = tipoContenido;
        cmd.Parameters.Add("@TamanoBytes", SqlDbType.BigInt).Value = tamanoBytes;
        cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1000).Value = Db(observaciones);
        cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = Db(UsuarioID);
        cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;

        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
    }

    private async Task InsertarHistorialAsync(SqlConnection cn, SqlTransaction tx, int viajeId, string evento, string? anterior, string? nuevo, string? observaciones, CancellationToken cancellationToken)
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

    private async Task<(string NombreOriginal, string NombreFisico, string RutaRelativa, string RutaFisica, string TipoContenido, long TamanoBytes)> GuardarArchivoEvidenciaAsync(int viajeId, IFormFile archivo, string prefijo, CancellationToken cancellationToken)
    {
        if (viajeId <= 0) throw new InvalidOperationException("El viaje indicado no es válido.");
        if (archivo == null || archivo.Length <= 0) throw new InvalidOperationException("El archivo de evidencia está vacío.");
        var nombreOriginal = Path.GetFileName(archivo.FileName);
        var extension = Path.GetExtension(nombreOriginal).ToLowerInvariant();
        var prefijoSeguro = string.Concat((prefijo ?? "EVIDENCIA").Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-'));
        if (string.IsNullOrWhiteSpace(prefijoSeguro)) prefijoSeguro = "EVIDENCIA";
        var nombreFisico = $"{prefijoSeguro}_{DateTime.Now:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}{extension}";
        var carpetaRelativa = Path.Combine("Logistica", "Viajes", viajeId.ToString(), "Evidencias");
        var carpetaFisica = Path.Combine(_environment.ContentRootPath, "App_Data", carpetaRelativa);
        Directory.CreateDirectory(carpetaFisica);
        var rutaFisica = Path.Combine(carpetaFisica, nombreFisico);
        await using (var stream = new FileStream(rutaFisica, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
        {
            await archivo.CopyToAsync(stream, cancellationToken);
        }
        var rutaRelativa = Path.Combine("App_Data", carpetaRelativa, nombreFisico).Replace('\\', '/');
        var tipoContenido = string.IsNullOrWhiteSpace(archivo.ContentType) ? "application/octet-stream" : archivo.ContentType.Trim();
        return (nombreOriginal, nombreFisico, rutaRelativa, rutaFisica, tipoContenido, archivo.Length);
    }

    private static async Task<List<(int EmbarqueID, string Folio, string Estatus)>> ObtenerEmbarquesViajeAsync(SqlConnection cn, SqlTransaction tx, int viajeId, CancellationToken cancellationToken)
    {
        var resultado = new List<(int EmbarqueID, string Folio, string Estatus)>();
        const string sql = @"
SELECT e.EmbarqueID,ISNULL(e.Folio,N'') Folio,ISNULL(e.Estatus,N'') Estatus
FROM dbo.Logistica_ViajeEmbarques ve WITH(UPDLOCK,HOLDLOCK)
INNER JOIN dbo.Logistica_Embarques e WITH(UPDLOCK,HOLDLOCK) ON e.EmbarqueID=ve.EmbarqueID
WHERE ve.ViajeID=@ViajeID AND ve.Activo=1 AND e.Activo=1
ORDER BY ISNULL(ve.OrdenEntrega,2147483647),ve.ViajeEmbarqueID;";
        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await rd.ReadAsync(cancellationToken))
            resultado.Add((Entero(rd, "EmbarqueID"), Texto(rd, "Folio"), Texto(rd, "Estatus")));
        return resultado;
    }

    private static async Task ValidarEmbarquesViajeListosParaSalidaAsync(SqlConnection cn, SqlTransaction tx, int viajeId, CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT TOP(1)
    e.EmbarqueID,
    ISNULL(e.Folio,N'') Folio,
    ISNULL(e.Estatus,N'') Estatus,
    ISNULL(d.TotalSolicitado,0) TotalSolicitado,
    ISNULL(c.PiezasCargadas,0) PiezasCargadas,
    ISNULL(c.TotalCajas,0) TotalCajas,
    ISNULL(c.CajasNoCargadas,0) CajasNoCargadas,
    ISNULL(i.IncidenciasCriticas,0) IncidenciasCriticas
FROM dbo.Logistica_ViajeEmbarques ve
INNER JOIN dbo.Logistica_Embarques e WITH(UPDLOCK,HOLDLOCK)
    ON e.EmbarqueID=ve.EmbarqueID
    AND e.Activo=1
OUTER APPLY
(
    SELECT ISNULL(SUM(CONVERT(bigint,x.CantidadSolicitada)),0) TotalSolicitado
    FROM dbo.Logistica_EmbarqueDetalle x
    WHERE x.EmbarqueID=e.EmbarqueID
      AND x.Activo=1
) d
OUTER APPLY
(
    SELECT
        COUNT(DISTINCT x.CajaID) TotalCajas,
        COUNT(DISTINCT CASE WHEN x.EstatusSeleccion NOT IN(N'Cargada',N'Despachada') THEN x.CajaID END) CajasNoCargadas,
        ISNULL(SUM(CASE WHEN x.EstatusSeleccion IN(N'Cargada',N'Despachada') THEN CONVERT(bigint,x.CantidadAsignada) ELSE 0 END),0) PiezasCargadas
    FROM dbo.Logistica_EmbarqueCajas x
    WHERE x.EmbarqueID=e.EmbarqueID
      AND x.Activo=1
) c
OUTER APPLY
(
    SELECT COUNT_BIG(*) IncidenciasCriticas
    FROM dbo.Logistica_Incidencias x
    WHERE x.EmbarqueID=e.EmbarqueID
      AND x.Activo=1
      AND x.Estatus IN(N'Abierta',N'En seguimiento')
      AND x.Severidad=N'Crítica'
) i
WHERE ve.ViajeID=@ViajeID
  AND ve.Activo=1
  AND
  (
      e.Estatus<>N'Cargado'
      OR ISNULL(d.TotalSolicitado,0)<=0
      OR ISNULL(c.PiezasCargadas,0)<>ISNULL(d.TotalSolicitado,0)
      OR ISNULL(c.TotalCajas,0)<=0
      OR ISNULL(c.CajasNoCargadas,0)>0
      OR ISNULL(i.IncidenciasCriticas,0)>0
  )
ORDER BY ISNULL(ve.OrdenEntrega,2147483647),ve.ViajeEmbarqueID;";
        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await rd.ReadAsync(cancellationToken)) return;
        var folio = Texto(rd, "Folio");
        var estatus = Texto(rd, "Estatus");
        var totalSolicitado = EnteroLargo(rd, "TotalSolicitado");
        var piezasCargadas = EnteroLargo(rd, "PiezasCargadas");
        var totalCajas = Entero(rd, "TotalCajas");
        var cajasNoCargadas = Entero(rd, "CajasNoCargadas");
        var incidencias = Entero(rd, "IncidenciasCriticas");
        if (estatus == "Cargando")
            throw new InvalidOperationException($"El embarque {folio} tiene la carga física en proceso. Termina de escanear las cajas y confirma la carga antes de registrar la salida.");
        if (estatus != "Cargado")
            throw new InvalidOperationException($"El embarque {folio} aún está en estatus {estatus}. Debe completar preparación, documentación y carga física antes de salir.");
        if (totalSolicitado <= 0)
            throw new InvalidOperationException($"El embarque {folio} no contiene piezas programadas.");
        if (piezasCargadas != totalSolicitado)
            throw new InvalidOperationException($"El embarque {folio} todavía no tiene completa su carga física. Programadas: {totalSolicitado:N0} PZA. Cargadas: {piezasCargadas:N0} PZA.");
        if (totalCajas <= 0)
            throw new InvalidOperationException($"El embarque {folio} no tiene cajas asignadas.");
        if (cajasNoCargadas > 0)
            throw new InvalidOperationException($"El embarque {folio} tiene {cajasNoCargadas:N0} caja(s) pendientes de escanear/cargar.");
        if (incidencias > 0)
            throw new InvalidOperationException($"El embarque {folio} tiene incidencias críticas abiertas.");
    }
    private async Task ActualizarListaCargaSalidaChoferAsync(SqlConnection cn, SqlTransaction tx, int embarqueId, CancellationToken cancellationToken)
    {
        const string sql = @"
UPDATE x
SET CantidadEnviada=x.CantidadAsignada,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
FROM dbo.Logistica_ListaCargaProgramacionEmbarques x
WHERE x.EmbarqueID=@EmbarqueID AND x.Activo=1;

UPDATE p
SET Estatus=
CASE
    WHEN ISNULL(t.Enviado,0)>=p.CantidadProgramada THEN N'Cumplida'
    WHEN ISNULL(t.Enviado,0)>0 THEN N'Parcial'
    WHEN ISNULL(t.Generado,0)>=p.CantidadProgramada THEN N'Generada'
    ELSE N'Programada'
END,
FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
FROM dbo.Logistica_ListaCargaProgramacion p
OUTER APPLY
(
    SELECT SUM(x.CantidadAsignada) Generado,SUM(x.CantidadEnviada) Enviado
    FROM dbo.Logistica_ListaCargaProgramacionEmbarques x
    WHERE x.ListaCargaProgramacionID=p.ListaCargaProgramacionID AND x.Activo=1
) t
WHERE EXISTS
(
    SELECT 1
    FROM dbo.Logistica_ListaCargaProgramacionEmbarques x
    WHERE x.ListaCargaProgramacionID=p.ListaCargaProgramacionID AND x.EmbarqueID=@EmbarqueID AND x.Activo=1
);";
        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
        cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task InsertarHistorialEmbarqueAsync(SqlConnection cn, SqlTransaction tx, int embarqueId, string evento, string? anterior, string? nuevo, string? observaciones, CancellationToken cancellationToken)
    {
        const string sql = @"
INSERT dbo.Logistica_EmbarqueHistorial
(EmbarqueID,Evento,EstadoAnterior,EstadoNuevo,Observaciones,UsuarioID,UsuarioNombre,FechaEvento)
VALUES
(@EmbarqueID,@Evento,@Anterior,@Nuevo,@Observaciones,@UsuarioID,@UsuarioNombre,SYSDATETIME());";
        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarqueId;
        cmd.Parameters.Add("@Evento", SqlDbType.NVarChar, 80).Value = evento;
        cmd.Parameters.Add("@Anterior", SqlDbType.NVarChar, 30).Value = Db(anterior);
        cmd.Parameters.Add("@Nuevo", SqlDbType.NVarChar, 30).Value = Db(nuevo);
        cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1200).Value = Db(observaciones);
        cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = Db(UsuarioID);
        cmd.Parameters.Add("@UsuarioNombre", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task DespacharEmbarquesViajeAsync(SqlConnection cn, SqlTransaction tx, int viajeId, DateTime fechaSalida, CancellationToken cancellationToken)
    {
        var embarques = await ObtenerEmbarquesViajeAsync(cn, tx, viajeId, cancellationToken);
        if (embarques.Count == 0) return;

        foreach (var embarque in embarques)
        {
            if (embarque.Estatus == "En ruta") continue;
            if (embarque.Estatus != "Cargado") throw new InvalidOperationException($"El embarque {embarque.Folio} debe estar Cargado antes de iniciar el viaje.");

            await using (var sp = new SqlCommand("dbo.usp_Logistica_DespacharEmbarque", cn, tx) { CommandType = CommandType.StoredProcedure })
            {
                sp.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarque.EmbarqueID;
                sp.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = Db(UsuarioID);
                sp.Parameters.Add("@UsuarioNombre", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                await using var rd = await sp.ExecuteReaderAsync(cancellationToken);
                if (!await rd.ReadAsync(cancellationToken)) throw new InvalidOperationException($"No fue posible confirmar la salida del embarque {embarque.Folio}.");
            }

            const string sqlFecha = @"
UPDATE dbo.Logistica_Embarques
SET FechaSalida=@FechaSalida,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario
WHERE EmbarqueID=@EmbarqueID AND Activo=1 AND Estatus=N'En ruta';";
            await using (var cmd = new SqlCommand(sqlFecha, cn, tx))
            {
                cmd.Parameters.Add("@FechaSalida", SqlDbType.DateTime2).Value = fechaSalida;
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@EmbarqueID", SqlDbType.Int).Value = embarque.EmbarqueID;
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await ActualizarListaCargaSalidaChoferAsync(cn, tx, embarque.EmbarqueID, cancellationToken);
            await InsertarHistorialEmbarqueAsync(cn, tx, embarque.EmbarqueID, "SALIDA_CONFIRMADA_POR_CHOFER", "Cargado", "En ruta", $"Salida física confirmada desde el viaje VIA-{viajeId:000000} por {UsuarioNombre} a las {fechaSalida:dd/MM/yyyy HH:mm}.", cancellationToken);
        }
    }

    private async Task InicializarParadasAlSalirAsync(SqlConnection cn, SqlTransaction tx, int viajeId, DateTime fechaSalida, CancellationToken cancellationToken)
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
        if (!origenId.HasValue) throw new InvalidOperationException("El viaje tiene paradas pero no tiene una parada Origen configurada.");
        if (estatusOrigen != "Completada")
        {
            const string sqlUpdate = @"UPDATE dbo.Logistica_ViajeParadas SET Estatus=N'Completada',FechaLlegadaReal=COALESCE(FechaLlegadaReal,@Fecha),FechaSalidaReal=@Fecha,FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario WHERE ViajeParadaID=@ParadaID AND ViajeID=@ViajeID AND Activo=1;";
            await using var cmd = new SqlCommand(sqlUpdate, cn, tx);
            cmd.Parameters.Add("@Fecha", SqlDbType.DateTime2).Value = fechaSalida;
            cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
            cmd.Parameters.Add("@ParadaID", SqlDbType.Int).Value = origenId.Value;
            cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            await InsertarHistorialParadaAsync(cn, tx, origenId.Value, "SALIDA_ORIGEN", estatusOrigen, "Completada", $"Salida de planta registrada el {fechaSalida:dd/MM/yyyy HH:mm}.", cancellationToken);
        }
        await ActivarSiguienteParadaAsync(cn, tx, viajeId, cancellationToken);
    }

    private static async Task ValidarParadasPreviasResueltasAsync(SqlConnection cn, SqlTransaction tx, int viajeId, int secuencia, CancellationToken cancellationToken)
    {
        const string sql = @"SELECT TOP(1) Secuencia,ISNULL(Lugar,N'') Lugar,ISNULL(Estatus,N'') Estatus FROM dbo.Logistica_ViajeParadas WITH(UPDLOCK,HOLDLOCK) WHERE ViajeID=@ViajeID AND Activo=1 AND Secuencia<@Secuencia AND Estatus NOT IN(N'Completada',N'Omitida',N'Cancelada') ORDER BY Secuencia,ViajeParadaID;";
        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
        cmd.Parameters.Add("@Secuencia", SqlDbType.Int).Value = secuencia;
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await rd.ReadAsync(cancellationToken)) throw new InvalidOperationException($"Primero debes resolver la parada #{Entero(rd, "Secuencia")} {Texto(rd, "Lugar")} ({Texto(rd, "Estatus")}).");
    }

    private async Task ActivarSiguienteParadaAsync(SqlConnection cn, SqlTransaction tx, int viajeId, CancellationToken cancellationToken)
    {
        int? paradaId = null;
        string estatus = string.Empty;
        string lugar = string.Empty;
        const string sql = @"SELECT TOP(1) ViajeParadaID,ISNULL(Estatus,N'') Estatus,ISNULL(Lugar,N'') Lugar FROM dbo.Logistica_ViajeParadas WITH(UPDLOCK,HOLDLOCK) WHERE ViajeID=@ViajeID AND Activo=1 AND TipoParada<>N'Origen' AND Estatus IN(N'Pendiente',N'En camino',N'En sitio') ORDER BY Secuencia,ViajeParadaID;";
        await using (var cmd = new SqlCommand(sql, cn, tx))
        {
            cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await rd.ReadAsync(cancellationToken))
            {
                paradaId = Entero(rd, "ViajeParadaID");
                estatus = Texto(rd, "Estatus");
                lugar = Texto(rd, "Lugar");
            }
        }
        if (!paradaId.HasValue || estatus != "Pendiente") return;
        const string sqlUpdate = @"UPDATE dbo.Logistica_ViajeParadas SET Estatus=N'En camino',FechaModificacion=SYSDATETIME(),ActualizadoPor=@Usuario WHERE ViajeParadaID=@ParadaID AND ViajeID=@ViajeID AND Activo=1 AND Estatus=N'Pendiente';";
        await using (var cmd = new SqlCommand(sqlUpdate, cn, tx))
        {
            cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
            cmd.Parameters.Add("@ParadaID", SqlDbType.Int).Value = paradaId.Value;
            cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
            if (await cmd.ExecuteNonQueryAsync(cancellationToken) > 0) await InsertarHistorialParadaAsync(cn, tx, paradaId.Value, "EN_CAMINO", "Pendiente", "En camino", $"El chofer continúa hacia {lugar}.", cancellationToken);
        }
    }

    private async Task<int> InsertarEvidenciaParadaAsync(SqlConnection cn, SqlTransaction tx, int viajeParadaId, string tipo, string nombreOriginal, string nombreFisico, string rutaRelativa, string tipoContenido, long tamanoBytes, string? observaciones, CancellationToken cancellationToken)
    {
        const string sql = @"INSERT dbo.Logistica_ViajeParadaEvidencias(ViajeParadaID,TipoEvidencia,NombreOriginal,NombreFisico,RutaRelativa,TipoContenido,TamanoBytes,Observaciones,UsuarioCargaID,UsuarioCargaNombre,FechaCarga,Activo) VALUES(@ViajeParadaID,@Tipo,@NombreOriginal,@NombreFisico,@RutaRelativa,@TipoContenido,@TamanoBytes,@Observaciones,@UsuarioID,@Usuario,SYSDATETIME(),1); SELECT CONVERT(int,SCOPE_IDENTITY());";
        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@ViajeParadaID", SqlDbType.Int).Value = viajeParadaId;
        cmd.Parameters.Add("@Tipo", SqlDbType.NVarChar, 80).Value = tipo;
        cmd.Parameters.Add("@NombreOriginal", SqlDbType.NVarChar, 260).Value = nombreOriginal;
        cmd.Parameters.Add("@NombreFisico", SqlDbType.NVarChar, 260).Value = nombreFisico;
        cmd.Parameters.Add("@RutaRelativa", SqlDbType.NVarChar, 600).Value = rutaRelativa;
        cmd.Parameters.Add("@TipoContenido", SqlDbType.NVarChar, 150).Value = Db(tipoContenido);
        cmd.Parameters.Add("@TamanoBytes", SqlDbType.BigInt).Value = tamanoBytes;
        cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1000).Value = Db(observaciones);
        cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = Db(UsuarioID);
        cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
    }

    private async Task CargarParadasChoferAsync(SqlConnection cn, LogisticaViajeDetalleVm vm, CancellationToken cancellationToken)
    {
        vm.Paradas.Clear();
        const string sql = @"
SELECT p.ViajeParadaID,p.ViajeID,p.Secuencia,ISNULL(p.TipoParada,N'') TipoParada,ISNULL(p.TipoOperacion,N'') TipoOperacion,ISNULL(p.EntidadTipo,N'') EntidadTipo,p.EntidadID,ISNULL(p.EntidadNombreSnapshot,N'') EntidadNombreSnapshot,ISNULL(p.Lugar,N'') Lugar,ISNULL(p.Direccion,N'') Direccion,ISNULL(p.ReferenciaTipo,N'') ReferenciaTipo,p.ReferenciaID,ISNULL(p.ReferenciaFolioSnapshot,N'') ReferenciaFolioSnapshot,p.FechaHoraLlegadaProgramada,p.FechaHoraSalidaProgramada,p.FechaLlegadaReal,p.FechaSalidaReal,ISNULL(p.Estatus,N'Pendiente') Estatus,ISNULL(p.RequiereEvidencia,0) RequiereEvidencia,ISNULL(p.CierraViaje,0) CierraViaje,ISNULL(p.ContactoNombre,N'') ContactoNombre,ISNULL(p.ContactoTelefono,N'') ContactoTelefono,ISNULL(p.Observaciones,N'') Observaciones,CONVERT(varbinary(8),p.RowVersion) RowVersion,
ISNULL(e.TotalEmbarques,0) TotalEmbarques,ISNULL(e.Entregados,0) EmbarquesEntregados,ISNULL(ev.TotalEvidencias,0) TotalEvidencias
FROM dbo.Logistica_ViajeParadas p
OUTER APPLY(SELECT CONVERT(int,COUNT_BIG(*)) TotalEmbarques,ISNULL(SUM(CASE WHEN em.Estatus=N'Entregado' THEN 1 ELSE 0 END),0) Entregados FROM dbo.Logistica_ViajeEmbarques ve INNER JOIN dbo.Logistica_Embarques em ON em.EmbarqueID=ve.EmbarqueID AND em.Activo=1 WHERE ve.ViajeParadaID=p.ViajeParadaID AND ve.Activo=1)e
OUTER APPLY(SELECT CONVERT(int,COUNT_BIG(*)) TotalEvidencias FROM dbo.Logistica_ViajeParadaEvidencias x WHERE x.ViajeParadaID=p.ViajeParadaID AND x.Activo=1)ev
WHERE p.ViajeID=@ViajeID AND p.Activo=1 ORDER BY p.Secuencia,p.ViajeParadaID;";
        await using (var cmd = new SqlCommand(sql, cn))
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
                    Activo = true,
                    TotalEmbarques = Entero(rd, "TotalEmbarques"),
                    EmbarquesEntregados = Entero(rd, "EmbarquesEntregados"),
                    TotalEvidencias = Entero(rd, "TotalEvidencias"),
                    RowVersion = Convert.ToBase64String((byte[])rd["RowVersion"])
                });
            }
        }
        if (vm.Paradas.Count == 0)
        {
            vm.TotalParadas = 0;
            vm.ParadasCompletadas = 0;
            vm.ParadasPendientes = 0;
            return;
        }
        var porId = vm.Paradas.ToDictionary(x => x.ViajeParadaID);
        const string sqlEmbarques = @"SELECT ve.ViajeEmbarqueID,ve.ViajeID,ve.ViajeParadaID,ve.EmbarqueID,ve.OrdenEntrega,ISNULL(e.Folio,N'') Folio,ISNULL(e.ClienteNombreSnapshot,N'') Cliente,ISNULL(e.Destino,N'') Destino,ISNULL(e.Estatus,N'') Estatus,ISNULL(d.TotalPiezas,0) TotalPiezas FROM dbo.Logistica_ViajeEmbarques ve INNER JOIN dbo.Logistica_Embarques e ON e.EmbarqueID=ve.EmbarqueID AND e.Activo=1 OUTER APPLY(SELECT ISNULL(SUM(x.CantidadSolicitada),0) TotalPiezas FROM dbo.Logistica_EmbarqueDetalle x WHERE x.EmbarqueID=e.EmbarqueID AND x.Activo=1)d WHERE ve.ViajeID=@ViajeID AND ve.Activo=1 AND ve.ViajeParadaID IS NOT NULL ORDER BY ISNULL(ve.OrdenEntrega,2147483647),ve.ViajeEmbarqueID;";
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
    private static (bool Ok, string Mensaje) ValidarFotoEvidencia(IFormFile archivo)
    {
        const long maximo = 10 * 1024 * 1024;
        if (archivo == null || archivo.Length <= 0) return (false, "Una de las fotografías está vacía.");
        if (archivo.Length > maximo) return (false, $"La fotografía {Path.GetFileName(archivo.FileName)} excede el máximo de 10 MB.");
        var extension = Path.GetExtension(archivo.FileName).ToLowerInvariant();
        var permitidas = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp", ".heic", ".heif" };
        if (!permitidas.Contains(extension)) return (false, $"{Path.GetFileName(archivo.FileName)} no es una fotografía válida. Usa JPG, JPEG, PNG, WEBP, HEIC o HEIF.");
        if (!string.IsNullOrWhiteSpace(archivo.ContentType) && !archivo.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return (false, $"{Path.GetFileName(archivo.FileName)} no fue reconocido como imagen.");
        return (true, string.Empty);
    }
    private string ResolverRutaFisica(string rutaRelativa)
    {
        if (string.IsNullOrWhiteSpace(rutaRelativa)) return string.Empty;
        var limpia = rutaRelativa.Replace('\\', '/').Trim().TrimStart('/');
        if (string.IsNullOrWhiteSpace(limpia)) return string.Empty;
        var esAppData = limpia.StartsWith("App_Data/Logistica/Viajes/", StringComparison.OrdinalIgnoreCase);
        var esUploadsAnterior = limpia.StartsWith("uploads/logistica/viajes/", StringComparison.OrdinalIgnoreCase);
        if (!esAppData && !esUploadsAnterior) return string.Empty;
        var raiz = esAppData ? _environment.ContentRootPath : !string.IsNullOrWhiteSpace(_environment.WebRootPath) ? _environment.WebRootPath : Path.Combine(_environment.ContentRootPath, "wwwroot");
        var partes = limpia.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (partes.Length == 0 || partes.Any(x => x == "." || x == "..")) return string.Empty;
        try
        {
            var raizCompleta = Path.GetFullPath(raiz);
            var rutaCompleta = Path.GetFullPath(Path.Combine(new[] { raizCompleta }.Concat(partes).ToArray()));
            var prefijoRaiz = raizCompleta.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!rutaCompleta.StartsWith(prefijoRaiz, StringComparison.OrdinalIgnoreCase)) return string.Empty;
            return rutaCompleta;
        }
        catch
        {
            return string.Empty;
        }
    }
    private static (bool Ok, string Mensaje) ValidarArchivoEvidencia(IFormFile archivo)
    {
        const long maximo = 10 * 1024 * 1024;
        if (archivo == null || archivo.Length <= 0) return (false, "Uno de los archivos está vacío.");
        if (archivo.Length > maximo) return (false, $"El archivo {Path.GetFileName(archivo.FileName)} excede el máximo de 10 MB.");
        var extension = Path.GetExtension(archivo.FileName).ToLowerInvariant();
        var permitidas = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",".jpeg",".png",".webp",".heic",".heif",".pdf"
    };
        if (!permitidas.Contains(extension)) return (false, $"{Path.GetFileName(archivo.FileName)} no es válido. Se permiten JPG, JPEG, PNG, WEBP, HEIC, HEIF o PDF.");
        return (true, string.Empty);
    }
    private IActionResult AccesoDenegadoChofer()
    {
        TempData["LogisticaError"] = "Esta pantalla es exclusiva para choferes del departamento de Logística.";
        return RedirectToAction("Index", "Home");
    }

    private static object Db(object? value) => value ?? DBNull.Value;

    private static string Texto(SqlDataReader rd, string columna)
    {
        var i = rd.GetOrdinal(columna);
        return rd.IsDBNull(i) ? string.Empty : rd.GetValue(i)?.ToString()?.Trim() ?? string.Empty;
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

    private static DateTime? Fecha(SqlDataReader rd, string columna)
    {
        var i = rd.GetOrdinal(columna);
        return rd.IsDBNull(i) ? null : Convert.ToDateTime(rd.GetValue(i));
    }

    private static TimeSpan? Hora(SqlDataReader rd, string columna)
    {
        var i = rd.GetOrdinal(columna);

        if (rd.IsDBNull(i))
            return null;

        var valor = rd.GetValue(i);

        if (valor is TimeSpan ts)
            return ts;

        return TimeSpan.TryParse(valor.ToString(), out var resultado) ? resultado : null;
    }

    private static bool Booleano(SqlDataReader rd, string columna)
    {
        var i = rd.GetOrdinal(columna);
        return !rd.IsDBNull(i) && Convert.ToBoolean(rd.GetValue(i));
    }
    private static decimal? DecimalNullable(SqlDataReader rd, string columna)
    {
        var i = rd.GetOrdinal(columna);
        return rd.IsDBNull(i) ? null : Convert.ToDecimal(rd.GetValue(i));
    }

    private static long EnteroLargo(SqlDataReader rd, string columna)
    {
        var i = rd.GetOrdinal(columna);
        return rd.IsDBNull(i) ? 0L : Convert.ToInt64(rd.GetValue(i));
    }

    private static bool EsExtensionImagen(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) return false;

        return extension.ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".webp" or ".heic" or ".heif";
    }


}