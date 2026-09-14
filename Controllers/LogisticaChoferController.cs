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
                var validacionFoto = ValidarFotoEvidencia(evidencia);

                if (!validacionFoto.Ok)
                {
                    TempData["LogisticaError"] = validacionFoto.Mensaje;
                    return RedirectToAction(nameof(Detalle), new { id = viajeId });
                }

                imagenes.Add(evidencia);
                continue;
            }

            if (extension == ".pdf")
            {
                var validacionArchivo = ValidarArchivoEvidencia(evidencia);

                if (!validacionArchivo.Ok)
                {
                    TempData["LogisticaError"] = validacionArchivo.Mensaje;
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

        if (!await UsuarioEsChoferAsync(cn, UsuarioID.Value, cancellationToken))
            return AccesoDenegadoChofer();

        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var rutasFisicas = new List<string>();

        try
        {
            var viaje = await ObtenerViajeChoferParaActualizarAsync(cn, tx, viajeId, UsuarioID.Value, cancellationToken)
                ?? throw new InvalidOperationException("El viaje no existe o ya no se encuentra asignado al chofer conectado.");

            if (viaje.Estatus != "Programado")
                throw new InvalidOperationException("Solo un viaje Programado puede registrar salida.");

            if (!viaje.UnidadID.HasValue || viaje.UnidadID.Value <= 0)
                throw new InvalidOperationException("El viaje no tiene una unidad asignada.");

            await ValidarChoferYUnidadDisponiblesAsync(cn, tx, viajeId, UsuarioID.Value, viaje.UnidadID.Value, cancellationToken);

            await ValidarEmbarquesViajeListosParaSalidaAsync(cn, tx, viajeId, cancellationToken);

            var ahora = DateTime.Now;

            const string sqlUpdate = @"
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
AND OperadorUsuarioID=@UsuarioID
AND Activo=1
AND Estatus=N'Programado';
SELECT @@ROWCOUNT;";

            await using (var cmd = new SqlCommand(sqlUpdate, cn, tx))
            {
                cmd.Parameters.Add("@FechaSalida", SqlDbType.DateTime2).Value = ahora;
                cmd.Parameters.Add("@KilometrajeSalida", SqlDbType.Int).Value = Db(kilometrajeSalida);
                cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1000).Value = Db(string.IsNullOrWhiteSpace(observaciones) ? null : observaciones);
                cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 200).Value = UsuarioNombre;
                cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = UsuarioID.Value;

                if (Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) != 1)
                    throw new InvalidOperationException("El viaje cambió mientras registrabas la salida.");
            }

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

            var descripcion = $"Salida registrada por el chofer el {ahora:dd/MM/yyyy HH:mm}. Fotografías: {imagenes.Count}.";

            if (archivos.Count > 0)
                descripcion += $" Archivos adicionales: {archivos.Count}.";

            if (kilometrajeSalida.HasValue)
                descripcion += $" Kilometraje inicial: {kilometrajeSalida.Value:N0} km.";

            if (!string.IsNullOrWhiteSpace(observaciones))
                descripcion += $" Observaciones: {observaciones}";

            await DespacharEmbarquesViajeAsync(cn, tx, viajeId, ahora, cancellationToken);

            await InsertarHistorialAsync(cn, tx, viajeId, "SALIDA_REGISTRADA_CHOFER", "Programado", "En curso", descripcion, cancellationToken);

            await tx.CommitAsync(cancellationToken);

            TempData["LogisticaOk"] = "Salida registrada correctamente. Buen viaje.";
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
                var validacionFoto = ValidarFotoEvidencia(evidencia);

                if (!validacionFoto.Ok)
                {
                    TempData["LogisticaError"] = validacionFoto.Mensaje;
                    return RedirectToAction(nameof(Detalle), new { id = viajeId });
                }

                imagenes.Add(evidencia);
                continue;
            }

            if (extension == ".pdf")
            {
                var validacionArchivo = ValidarArchivoEvidencia(evidencia);

                if (!validacionArchivo.Ok)
                {
                    TempData["LogisticaError"] = validacionArchivo.Mensaje;
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

        if (!await UsuarioEsChoferAsync(cn, UsuarioID.Value, cancellationToken))
            return AccesoDenegadoChofer();

        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var rutasFisicas = new List<string>();

        try
        {
            var viaje = await ObtenerViajeChoferParaActualizarAsync(cn, tx, viajeId, UsuarioID.Value, cancellationToken)
                ?? throw new InvalidOperationException("El viaje no existe o ya no se encuentra asignado al chofer conectado.");

            if (viaje.Estatus != "En curso")
                throw new InvalidOperationException("Solo un viaje En curso puede registrar regreso.");

            if (!viaje.FechaSalidaReal.HasValue)
                throw new InvalidOperationException("El viaje todavía no tiene una salida registrada.");

            if (viaje.FechaRegresoReal.HasValue)
                throw new InvalidOperationException("El regreso de este viaje ya fue registrado.");

            if (viaje.KilometrajeSalida.HasValue && kilometrajeRegreso.HasValue && kilometrajeRegreso.Value < viaje.KilometrajeSalida.Value)
                throw new InvalidOperationException($"El kilometraje final no puede ser menor al inicial ({viaje.KilometrajeSalida.Value:N0} km).");

            var ahora = DateTime.Now;

            const string sqlUpdate = @"
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

                if (Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) != 1)
                    throw new InvalidOperationException("El viaje cambió mientras registrabas el regreso.");
            }

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

            var descripcion = $"Regreso registrado por el chofer el {ahora:dd/MM/yyyy HH:mm}. Fotografías: {imagenes.Count}.";

            if (archivos.Count > 0)
                descripcion += $" Archivos adicionales: {archivos.Count}.";

            if (kilometrajeRegreso.HasValue)
                descripcion += $" Kilometraje final: {kilometrajeRegreso.Value:N0} km.";

            if (viaje.KilometrajeSalida.HasValue && kilometrajeRegreso.HasValue)
                descripcion += $" Distancia recorrida: {Math.Max(0, kilometrajeRegreso.Value - viaje.KilometrajeSalida.Value):N0} km.";

            if (!string.IsNullOrWhiteSpace(observaciones))
                descripcion += $" Observaciones: {observaciones}";

            await InsertarHistorialAsync(cn, tx, viajeId, "RETORNO_REGISTRADO_CHOFER", "En curso", "Completado", descripcion, cancellationToken);

            await tx.CommitAsync(cancellationToken);

            TempData["LogisticaOk"] = "Regreso registrado correctamente. El viaje quedó completado.";
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
    public async Task<IActionResult> RegistrarIncidencia(int viajeId, string? tipo, string? severidad, string? descripcion, IFormFile? evidencia, CancellationToken cancellationToken = default)
    {
        if (!UsuarioID.HasValue || UsuarioID.Value <= 0) return RedirectToAction("Login", "Login");
        if (viajeId <= 0) return RedirectToAction(nameof(Index));

        tipo = tipo?.Trim() ?? string.Empty;
        severidad = severidad?.Trim() ?? string.Empty;
        descripcion = descripcion?.Trim() ?? string.Empty;

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

        if (evidencia != null && evidencia.Length > 0)
        {
            var validacion = ValidarArchivoEvidencia(evidencia);

            if (!validacion.Ok)
            {
                TempData["LogisticaError"] = validacion.Mensaje;
                return RedirectToAction(nameof(Detalle), new { id = viajeId });
            }
        }

        await using var cn = await AbrirAsync(cancellationToken);

        if (!await UsuarioEsChoferAsync(cn, UsuarioID.Value, cancellationToken))
            return AccesoDenegadoChofer();

        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        string? rutaFisica = null;

        try
        {
            var viaje = await ObtenerViajeChoferParaActualizarAsync(cn, tx, viajeId, UsuarioID.Value, cancellationToken)
                ?? throw new InvalidOperationException("El viaje no existe o no está asignado al chofer conectado.");

            if (viaje.Estatus is "Cancelado" or "Completado")
                throw new InvalidOperationException("Ya no pueden registrarse incidencias operativas en este viaje.");

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

            int? evidenciaId = null;

            if (evidencia != null && evidencia.Length > 0)
            {
                var archivoGuardado = await GuardarArchivoEvidenciaAsync(viajeId, evidencia, "INCIDENCIA", cancellationToken);
                rutaFisica = archivoGuardado.RutaFisica;

                evidenciaId = await InsertarEvidenciaAsync(cn, tx, viajeId, "Incidencia", archivoGuardado.NombreOriginal, archivoGuardado.NombreFisico, archivoGuardado.RutaRelativa, archivoGuardado.TipoContenido, archivoGuardado.TamanoBytes, $"Incidencia VINC-{incidenciaId:000000}. {descripcion}", cancellationToken);
            }

            var historial = $"Incidencia VINC-{incidenciaId:000000} registrada por el chofer. {tipo} / {severidad}. {descripcion}";

            if (evidenciaId.HasValue)
                historial += $" Evidencia VE-{evidenciaId.Value:000000}.";

            await InsertarHistorialAsync(cn, tx, viajeId, "INCIDENCIA_REGISTRADA_CHOFER", viaje.Estatus, viaje.Estatus, historial, cancellationToken);

            await tx.CommitAsync(cancellationToken);

            TempData["LogisticaOk"] = $"Incidencia VINC-{incidenciaId:000000} registrada correctamente.";
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(cancellationToken); } catch { }

            if (!string.IsNullOrWhiteSpace(rutaFisica) && System.IO.File.Exists(rutaFisica))
            {
                try { System.IO.File.Delete(rutaFisica); } catch { }
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
    private async Task<List<LogisticaViajeDetalleVm>> ObtenerViajesChoferAsync(SqlConnection cn, int usuarioId, CancellationToken cancellationToken)
    {
        var lista = new List<LogisticaViajeDetalleVm>();

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
    ISNULL(v.TieneIncidencia,0) TieneIncidencia,
    v.KilometrajeSalida,
    v.KilometrajeRegreso,
    ISNULL(v.Observaciones,N'') Observaciones,
    v.ResponsableUsuarioID,
    ISNULL(v.ResponsableNombreSnapshot,N'') UsuarioResponsable,
    v.FechaCreacion,
    ISNULL(v.CreadoPor,N'') CreadoPor
FROM dbo.Logistica_Viajes v
LEFT JOIN dbo.Logistica_Rutas r ON r.RutaID=v.RutaID
LEFT JOIN dbo.Logistica_Unidades u ON u.UnidadID=v.UnidadID
WHERE v.Activo=1
AND v.OperadorUsuarioID=@UsuarioID
AND
(
    v.Estatus IN(N'Programado',N'En curso')
    OR
    (
        v.Estatus=N'Completado'
        AND ISNULL(v.FechaRegresoReal,v.FechaProgramada)>=DATEADD(DAY,-7,CAST(GETDATE() AS date))
    )
)
ORDER BY
    CASE v.Estatus
        WHEN N'En curso' THEN 0
        WHEN N'Programado' THEN 1
        ELSE 2
    END,
    v.FechaProgramada,
    v.HoraSalidaProgramada,
    v.ViajeID;";

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;

        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);

        while (await rd.ReadAsync(cancellationToken))
        {
            lista.Add(new LogisticaViajeDetalleVm
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
                KilometrajeSalida = EnteroNullable(rd, "KilometrajeSalida"),
                KilometrajeRegreso = EnteroNullable(rd, "KilometrajeRegreso"),
                Observaciones = Texto(rd, "Observaciones"),
                UsuarioResponsableID = EnteroNullable(rd, "ResponsableUsuarioID"),
                UsuarioResponsable = Texto(rd, "UsuarioResponsable"),
                FechaCreacion = Fecha(rd, "FechaCreacion") ?? DateTime.MinValue,
                CreadoPor = Texto(rd, "CreadoPor")
            });
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

    private static async Task ValidarChoferYUnidadDisponiblesAsync(SqlConnection cn, SqlTransaction tx, int viajeId, int usuarioId, int unidadId, CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT TOP(1)
    ISNULL(Folio,N'') Folio,
    CASE WHEN OperadorUsuarioID=@UsuarioID THEN N'CHOFER' ELSE N'UNIDAD' END Recurso
FROM dbo.Logistica_Viajes WITH(UPDLOCK,HOLDLOCK)
WHERE Activo=1
AND ViajeID<>@ViajeID
AND Estatus=N'En curso'
AND FechaRegresoReal IS NULL
AND
(
    OperadorUsuarioID=@UsuarioID
    OR UnidadID=@UnidadID
)
ORDER BY FechaSalidaReal;";

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
        cmd.Parameters.Add("@UnidadID", SqlDbType.Int).Value = unidadId;
        cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;

        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);

        if (!await rd.ReadAsync(cancellationToken))
            return;

        var folio = Texto(rd, "Folio");
        var recurso = Texto(rd, "Recurso");

        if (recurso == "CHOFER")
            throw new InvalidOperationException($"Todavía tienes el viaje {folio} En curso. Primero registra el regreso.");

        throw new InvalidOperationException($"La unidad continúa ocupada por el viaje {folio}. No puede iniciar otro viaje todavía.");
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
        var nombreOriginal = Path.GetFileName(archivo.FileName);
        var extension = Path.GetExtension(nombreOriginal).ToLowerInvariant();
        var nombreFisico = $"{prefijo}_{DateTime.Now:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}{extension}";

        var raiz = !string.IsNullOrWhiteSpace(_environment.WebRootPath)
            ? _environment.WebRootPath
            : Path.Combine(_environment.ContentRootPath, "wwwroot");

        var carpeta = Path.Combine(raiz, "uploads", "logistica", "viajes", viajeId.ToString());

        Directory.CreateDirectory(carpeta);

        var rutaFisica = Path.Combine(carpeta, nombreFisico);

        await using (var stream = new FileStream(rutaFisica, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await archivo.CopyToAsync(stream, cancellationToken);
        }

        var rutaRelativa = $"/uploads/logistica/viajes/{viajeId}/{nombreFisico}";

        return (nombreOriginal, nombreFisico, rutaRelativa, rutaFisica, archivo.ContentType ?? "application/octet-stream", archivo.Length);
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
        COUNT(DISTINCT CASE
            WHEN x.EstatusSeleccion NOT IN(N'Cargada',N'Despachada')
            THEN x.CajaID
            ELSE NULL
        END) CajasNoCargadas,
        ISNULL(SUM(
            CASE
                WHEN x.EstatusSeleccion IN(N'Cargada',N'Despachada')
                THEN CONVERT(bigint,x.CantidadAsignada)
                ELSE 0
            END
        ),0) PiezasCargadas
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
);";

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@ViajeID", SqlDbType.Int).Value = viajeId;

        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);

        if (!await rd.ReadAsync(cancellationToken))
            return;

        var folio = Texto(rd, "Folio");
        var estatus = Texto(rd, "Estatus");
        var totalSolicitado = EnteroLargo(rd, "TotalSolicitado");
        var piezasCargadas = EnteroLargo(rd, "PiezasCargadas");
        var totalCajas = Entero(rd, "TotalCajas");
        var cajasNoCargadas = Entero(rd, "CajasNoCargadas");
        var incidencias = Entero(rd, "IncidenciasCriticas");

        if (estatus != "Cargado")
            throw new InvalidOperationException($"El embarque {folio} aún está en estatus {estatus}. Debe completar Documentación y Carga física antes de salir.");

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
        var raiz = !string.IsNullOrWhiteSpace(_environment.WebRootPath)
            ? _environment.WebRootPath
            : Path.Combine(_environment.ContentRootPath, "wwwroot");

        var limpia = (rutaRelativa ?? string.Empty).Replace('\\', '/').TrimStart('/');
        var partes = limpia.Split('/', StringSplitOptions.RemoveEmptyEntries);

        return Path.Combine(new[] { raiz }.Concat(partes).ToArray());
    }

    private static (bool Ok, string Mensaje) ValidarArchivoEvidencia(IFormFile archivo)
    {
        const long maximo = 10 * 1024 * 1024;

        if (archivo.Length <= 0)
            return (false, "El archivo está vacío.");

        if (archivo.Length > maximo)
            return (false, "La evidencia no puede exceder 10 MB.");

        var extension = Path.GetExtension(archivo.FileName).ToLowerInvariant();

        var permitidas = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg",".jpeg",".png",".pdf"
        };

        if (!permitidas.Contains(extension))
            return (false, "Solo se permiten evidencias JPG, JPEG, PNG o PDF.");

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