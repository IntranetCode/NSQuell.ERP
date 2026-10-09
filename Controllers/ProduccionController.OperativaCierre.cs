using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using static ERP.NSQuell.Models.ProduccionEjecucionVm;

namespace ERP.NSQuell.Controllers
{
    public sealed partial class ProduccionController
    {
        [HttpGet]
        public async Task<IActionResult> CierreOperativo(
            int programaProduccionId,
            bool soloLectura = false,
            string? enfoque = null)
        {
            if (!UsuarioEnSesion())
                return Unauthorized();

            if (programaProduccionId <= 0)
                return BadRequest(new { mensaje = "No se recibió un Programa de Producción válido." });

            var usuarioId = ObtenerUsuarioID();
            if (usuarioId <= 0)
                return Unauthorized();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.ReadCommitted);

            try
            {
                var actual = await CargarLadoCierreOperativoAsync(programaProduccionId, cn, tx);
                if (actual == null)
                {
                    await tx.RollbackAsync();
                    return NotFound();
                }

                var permisos = await ObtenerPermisosProduccionUsuarioAsync(usuarioId, cn, tx);
                var relacion = await ObtenerParejaLhRhProduccionAsync(programaProduccionId, cn, tx);

                ProduccionCierreLadoOperativoVm? pareja = null;
                var parejaConsistente = true;
                string? motivoInconsistencia = null;
                int? grupoLhRh = null;

                if (relacion != null)
                {
                    grupoLhRh = relacion.GrupoLhRh;
                    parejaConsistente = relacion.EsCompatibleFisicamente;
                    if (!parejaConsistente)
                    {
                        motivoInconsistencia = ResolverInconsistenciaParejaCierre(relacion);
                    }

                    pareja = await CargarLadoCierreOperativoAsync(relacion.ProgramaParejaID, cn, tx);
                    if (pareja == null)
                    {
                        parejaConsistente = false;
                        motivoInconsistencia = $"No se encontró una ejecución activa o histórica para el Programa pareja {relacion.ProgramaParejaID}.";
                    }
                }

                var ladoActual = DeterminarLadoLhRhConfiguracion(actual.NumeroParte, actual.ReferenciaSAP, actual.DescripcionParte);
                var ladoPareja = pareja == null
                    ? null
                    : DeterminarLadoLhRhConfiguracion(pareja.NumeroParte, pareja.ReferenciaSAP, pareja.DescripcionParte);

                actual.LadoLhRh = ladoActual;
                if (pareja != null) pareja.LadoLhRh = ladoPareja;

                await CompletarValidacionesCierreOperativoAsync(actual, relacion == null, cn, tx);
                if (pareja != null)
                    await CompletarValidacionesCierreOperativoAsync(pareja, false, cn, tx);

                await tx.RollbackAsync();

                var puedeGestionarLiberacion =
                    permisos.EsAdministradorERP ||
                    permisos.EsEncargadoProduccion ||
                    permisos.EsAuxiliarProduccion ||
                    permisos.EsTecnicoProduccion ||
                    permisos.EsOperadorProduccion;

                var puedeGestionarTerminacion = puedeGestionarLiberacion;
                var puedeGestionarTerminacionParcial = permisos.EsTecnicoProduccion || permisos.EsAuxiliarProduccion;
                var puedeGestionarCierreDocumental = permisos.EsAdministradorERP || permisos.EsEncargadoProduccion || permisos.EsAuxiliarProduccion;

                var vm = new ProduccionCierreOperativoVm
                {
                    FechaConsulta = DateTime.Now,
                    Enfoque = string.Equals(enfoque, ProduccionCierreOperativoEnfoque.Liberacion, StringComparison.OrdinalIgnoreCase)
                        ? ProduccionCierreOperativoEnfoque.Liberacion
                        : ProduccionCierreOperativoEnfoque.Cierre,
                    Actual = actual,
                    Pareja = pareja,
                    GrupoLhRh = grupoLhRh,
                    LadoActual = ladoActual,
                    LadoPareja = ladoPareja,
                    ParejaConsistente = parejaConsistente,
                    MotivoInconsistenciaPareja = motivoInconsistencia,
                    SoloLectura = soloLectura,
                    PuedeGestionarLiberacion = puedeGestionarLiberacion,
                    PuedeGestionarTerminacion = puedeGestionarTerminacion,
                    PuedeGestionarTerminacionParcial = puedeGestionarTerminacionParcial,
                    PuedeGestionarCierreDocumental = puedeGestionarCierreDocumental,
                    UsuarioNombre = permisos.Nombre
                };

                return PartialView("~/Views/ProduccionOperativa/Acciones/_CierreContenido.cshtml", vm);
            }
            catch (Exception ex)
            {
                try { await tx.RollbackAsync(); } catch { }
                return BadRequest(new { mensaje = "No fue posible consultar el cierre de Producción: " + ex.Message });
            }
        }

        // NSQ_FINALIZACION_JUSTIFICADA_V1_1: revisión y cierre físico atómicos.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> FinalizarConJustificacionOperativa(
            int ejecucionProduccionID, bool confirmar = false,
            string? revision = null, string? justificacion = null,
            bool aceptarPendientes = false)
        {
            if (!UsuarioEnSesion() || ObtenerUsuarioID() <= 0) return Unauthorized();
            if (ejecucionProduccionID <= 0)
                return BadRequest(new { ok = false, mensaje = "Ejecución inválida." });
            var usuarioId = ObtenerUsuarioID();
            justificacion = justificacion?.Trim();
            if (justificacion?.Length > 400)
                return BadRequest(new { ok = false, mensaje = "La justificación admite hasta 400 caracteres." });

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var permisos = await ObtenerPermisosProduccionUsuarioAsync(usuarioId, cn, tx);
                if (!(permisos.EsAdministradorERP || permisos.EsEncargadoProduccion ||
                      permisos.EsAuxiliarProduccion || permisos.EsTecnicoProduccion || permisos.EsOperadorProduccion))
                {
                    await tx.RollbackAsync();
                    return StatusCode(403, new { ok = false, mensaje = "No tienes permiso para finalizar producción." });
                }
                var actual = await ObtenerEjecucionAsync(ejecucionProduccionID, cn, tx)
                    ?? throw new InvalidOperationException("No se encontró la ejecución activa.");
                var relacion = await ObtenerParejaLhRhProduccionAsync(actual.ProgramaProduccionID, cn, tx);
                var lados = new System.Collections.Generic.List<ProduccionEjecucionVm> { actual };
                if (relacion != null)
                {
                    if (!relacion.EsCompatibleFisicamente || !relacion.EjecucionParejaID.HasValue)
                        throw new InvalidOperationException("La pareja LH/RH no es consistente. No se puede finalizar una sola OF de la operación física.");
                    var pareja = await ObtenerEjecucionAsync(relacion.EjecucionParejaID.Value, cn, tx)
                        ?? throw new InvalidOperationException("No se encontró la ejecución pareja LH/RH.");
                    if (pareja.MaquinaID != actual.MaquinaID || pareja.EjecucionProduccionID == actual.EjecucionProduccionID)
                        throw new InvalidOperationException("La pareja LH/RH ya no corresponde a la misma máquina.");
                    lados.Add(pareja);
                }
                if (lados.All(x => x.EstatusID is ProduccionEstatus.Terminado or ProduccionEstatus.ListaCierreDocumental or ProduccionEstatus.Cerrado))
                {
                    await tx.RollbackAsync();
                    return Json(new { ok = true, finalizada = true, mensaje = "La producción ya está finalizada." });
                }
                if (lados.Any(x => x.EstatusID is not (ProduccionEstatus.EnPreparacion or ProduccionEstatus.EnProduccion or ProduccionEstatus.Pausado)))
                    throw new InvalidOperationException("La ejecución debe estar en preparación, producción o pausa. No se alterarán estados ya cerrados o cancelados.");

                var pendientes = new System.Collections.Generic.List<string>();
                var estado = new System.Text.StringBuilder();
                foreach (var lado in lados.OrderBy(x => x.EjecucionProduccionID))
                {
                    await using (var programa = new SqlCommand(@"SELECT COUNT(*) FROM dbo.Planeacion_ProgramaProduccion WITH(UPDLOCK,HOLDLOCK)
WHERE ProgramaProduccionID=@ID AND Activo=1 AND EstatusID IN(1,2,3,4);", cn, tx))
                    {
                        programa.Parameters.Add("@ID", SqlDbType.Int).Value = lado.ProgramaProduccionID;
                        if (Convert.ToInt32(await programa.ExecuteScalarAsync()) != 1)
                            throw new InvalidOperationException("El programa cambió de estado o ya no está activo. Actualiza el modal.");
                    }
                    await RecalcularTotalesEjecucionAsync(lado.EjecucionProduccionID, usuarioId, cn, tx);
                    var validacion = await ValidarTerminarProduccionAsync(lado.EjecucionProduccionID, false, cn, tx);
                    var cierre = await CargarLadoCierreOperativoAsync(lado.ProgramaProduccionID, cn, tx)
                        ?? throw new InvalidOperationException("No se pudo revisar el cierre de una OF.");
                    if (cierre.EjecucionProduccionID != lado.EjecucionProduccionID)
                        throw new InvalidOperationException("La ejecución vigente cambió. Actualiza el modal.");
                    var prefijo = cierre.OFTexto + ": ";
                    pendientes.AddRange(validacion.Bloqueos.Select(x => prefijo + x));
                    if (!lado.FechaInicioReal.HasValue || lado.EstatusID == ProduccionEstatus.EnPreparacion)
                        pendientes.Add(prefijo + "La producción en serie no se ha iniciado.");
                    if (!lado.FechaLiberacionMaquina.HasValue)
                    {
                        var liberacion = await ValidarLiberacionMaquinaAsync(lado.EjecucionProduccionID, cn, tx);
                        if (!liberacion.Permitido) pendientes.Add(prefijo + liberacion.Mensaje);
                    }
                    if (cierre.CajasPendientesRecepcionAlmacen > 0)
                        pendientes.Add(prefijo + cierre.CajasPendientesRecepcionAlmacen + " caja(s) pendientes de recepción en Almacén PT.");
                    // También se revisan sesiones pausadas: no deben quedar abiertas tras el cierre.
                    await using (var cmd = new SqlCommand(@"SELECT COUNT(*) FROM dbo.Produccion_TiempoExtra WITH(UPDLOCK,HOLDLOCK)
WHERE EjecucionProduccionID=@ID AND Activo=1 AND FechaHoraFin IS NULL AND Estado IN(N'EN_CURSO',N'PAUSADO');", cn, tx))
                    {
                        cmd.Parameters.Add("@ID", SqlDbType.Int).Value = lado.EjecucionProduccionID;
                        if (Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0)
                            pendientes.Add(prefijo + "Tiempo extra abierto: se cancelará la sesión pendiente al finalizar; los cortes registrados se conservarán.");
                    }
                    estado.Append(lado.EjecucionProduccionID).Append('|').Append(lado.EstatusID)
                        .Append('|').Append(cierre.CantidadOK).Append('|').Append(cierre.CantidadPlaneada)
                        .Append('|').Append(lado.FechaLiberacionMaquina?.Ticks).Append(';');
                }
                pendientes = pendientes.Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
                estado.Append(string.Join("\n", pendientes));
                string revisionActual;
                using (var sha = System.Security.Cryptography.SHA256.Create())
                    revisionActual = Convert.ToBase64String(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(estado.ToString())));
                if (!confirmar || !string.Equals(revision, revisionActual, StringComparison.Ordinal))
                {
                    await tx.RollbackAsync();
                    return Json(new { ok = true, finalizada = false, pendientes, revision = revisionActual,
                        mensaje = confirmar ? "Los pendientes cambiaron. Revísalos y confirma nuevamente." : "Revisión completada. Confirma la finalización." });
                }
                if (pendientes.Count > 0 && (!aceptarPendientes || string.IsNullOrWhiteSpace(justificacion)))
                {
                    await tx.RollbackAsync();
                    return BadRequest(new { ok = false, mensaje = "Escribe la justificación y acepta los pendientes para finalizar." });
                }
                var correlacion = Guid.NewGuid().ToString("N");
                var ahora = DateTime.Now;
                var nota = "Finalización de producción. Usuario " + usuarioId + ". " +
                    (string.IsNullOrWhiteSpace(justificacion) ? "Sin pendientes." : justificacion);
                var auditoria = "[Finalización operativa " + ahora.ToString("yyyy-MM-dd HH:mm:ss") + "] " + nota +
                    "\r\nPendientes al finalizar: " + (pendientes.Count == 0 ? "ninguno" : string.Join(" | ", pendientes));
                foreach (var lado in lados.OrderBy(x => x.EjecucionProduccionID))
                {
                    const string sql = @"
UPDATE dbo.Produccion_Paros
SET FechaFinParo=@Ahora, DuracionMinutos=DATEDIFF(MINUTE,FechaInicioParo,@Ahora),
    EsMayorA15Minutos=CASE WHEN DATEDIFF(MINUTE,FechaInicioParo,@Ahora)>15 THEN 1 ELSE 0 END,
    UsuarioModificacionID=@Usuario, FechaModificacion=@Ahora
WHERE EjecucionProduccionID=@ID AND Activo=1 AND FechaFinParo IS NULL;
UPDATE dbo.Produccion_TiempoExtra
SET Estado=N'CANCELADO', FechaHoraFin=@Ahora, UsuarioCancelacionID=@Usuario,
    FechaCancelacion=@Ahora, MotivoCancelacion=@Nota,
    UsuarioModificacionID=@Usuario, FechaModificacion=@Ahora
WHERE EjecucionProduccionID=@ID AND Activo=1 AND FechaHoraFin IS NULL AND Estado IN(N'EN_CURSO',N'PAUSADO');
UPDATE dbo.Produccion_Ejecucion
SET EstatusID=@Terminado, FechaFinReal=@Ahora,
    FechaLiberacionMaquina=ISNULL(FechaLiberacionMaquina,@Ahora),
    UsuarioLiberacionMaquinaID=CASE WHEN FechaLiberacionMaquina IS NULL THEN @Usuario ELSE UsuarioLiberacionMaquinaID END,
    ObservacionesLiberacionMaquina=CASE WHEN FechaLiberacionMaquina IS NULL THEN @Nota ELSE ObservacionesLiberacionMaquina END,
    UsuarioModificacionID=@Usuario, FechaModificacion=@Ahora
WHERE EjecucionProduccionID=@ID AND Activo=1 AND EstatusID IN(2,3,4);
SELECT @@ROWCOUNT;";
                    await using var cmd = new SqlCommand(sql, cn, tx);
                    cmd.Parameters.Add("@ID", SqlDbType.Int).Value = lado.EjecucionProduccionID;
                    cmd.Parameters.Add("@Usuario", SqlDbType.Int).Value = usuarioId;
                    cmd.Parameters.Add("@Ahora", SqlDbType.DateTime).Value = ahora;
                    cmd.Parameters.Add("@Terminado", SqlDbType.Int).Value = ProduccionEstatus.Terminado;
                    cmd.Parameters.Add("@Nota", SqlDbType.NVarChar, 500).Value = nota;
                    if (Convert.ToInt32(await cmd.ExecuteScalarAsync()) != 1)
                        throw new InvalidOperationException("La ejecución cambió durante la finalización. No se aplicó un cierre parcial.");
                    await MarcarProgramaTerminadoAsync(lado.ProgramaProduccionID, usuarioId, cn, tx);
                    await RegistrarFinalizacionOperativaAuditoriaAsync(usuarioId, lado.EjecucionProduccionID, correlacion, auditoria, cn, tx);
                }
                await ProcesarRetornoInterrupcionUrgenteAsync(actual.ProgramaProduccionID, usuarioId, cn, tx);
                await tx.CommitAsync();
                return Json(new { ok = true, finalizada = true,
                    mensaje = lados.Count == 2 ? "Producciones LH/RH finalizadas y máquina liberada. Se registró la finalización." : "Producción finalizada y máquina liberada. Se registró la finalización." });
            }
            catch (Exception ex)
            {
                try { await tx.RollbackAsync(); } catch { }
                return BadRequest(new { ok = false, mensaje = "No fue posible finalizar producción: " + ex.Message });
            }
        }



        // La bitácora existente admite mensajes de 500 caracteres. Se segmenta
        // el registro completo, sin recortar la justificación ni los pendientes.
        private static async Task RegistrarFinalizacionOperativaAuditoriaAsync(
            int usuarioId, int ejecucionId, string correlacion, string auditoria,
            SqlConnection cn, SqlTransaction tx)
        {
            int capacidad;
            await using (var capacidadCmd = new SqlCommand(@"
SELECT max_length FROM sys.columns
WHERE object_id=OBJECT_ID(N'bitacora.Registros') AND name=N'Mensaje' AND system_type_id=231;", cn, tx))
            {
                var valor = await capacidadCmd.ExecuteScalarAsync();
                if (valor == null || valor == DBNull.Value)
                    throw new InvalidOperationException("No se encontró el campo de mensaje de la bitácora. No se finalizó la producción.");
                var bytes = Convert.ToInt32(valor);
                capacidad = bytes == -1 ? 500 : bytes / 2;
            }
            if (capacidad < 100)
                throw new InvalidOperationException("El mensaje de bitácora tiene un tamaño incompatible con el registro de finalización.");
            var maxParte = Math.Min(400, capacidad - 80);
            var partes = new System.Collections.Generic.List<string>();
            for (var inicio = 0; inicio < auditoria.Length;)
            {
                var largo = Math.Min(maxParte, auditoria.Length - inicio);
                if (inicio + largo < auditoria.Length && char.IsHighSurrogate(auditoria[inicio + largo - 1])) largo--;
                partes.Add(auditoria.Substring(inicio, largo));
                inicio += largo;
            }
            const string sql = @"
INSERT INTO bitacora.Registros
    (FechaHoraUtc,IdUsuario,Accion,Resultado,Severidad,Mensaje,Modulo,Entidad,EntidadId,SolicitudId)
VALUES
    (SYSUTCDATETIME(),@Usuario,N'FINALIZAR_PRODUCCION',N'OK',1,@Mensaje,N'Produccion',N'Produccion_Ejecucion',@EntidadId,@Correlacion);";
            for (var i = 0; i < partes.Count; i++)
            {
                var mensaje = "Finalización " + correlacion + " [" + (i + 1) + "/" + partes.Count + "] " + partes[i];
                if (mensaje.Length > capacidad)
                    throw new InvalidOperationException("El registro de finalización excede la capacidad de la bitácora.");
                await using var cmd = new SqlCommand(sql, cn, tx);
                cmd.Parameters.Add("@Usuario", SqlDbType.Int).Value = usuarioId;
                cmd.Parameters.Add("@Mensaje", SqlDbType.NVarChar, capacidad).Value = mensaje;
                cmd.Parameters.Add("@EntidadId", SqlDbType.NVarChar, 120).Value = ejecucionId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                cmd.Parameters.Add("@Correlacion", SqlDbType.NVarChar, 64).Value = correlacion;
                await cmd.ExecuteNonQueryAsync();
            }
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LiberarMaquinaOperativa(ProduccionLiberarMaquinaOperativaPostVm vm)
        {
            if (!UsuarioEnSesion())
                return Unauthorized();

            if (vm.EjecucionProduccionID <= 0)
                return BadRequest(new { mensaje = "No se recibió una ejecución de Producción válida." });

            var observaciones = string.IsNullOrWhiteSpace(vm.Observaciones) ? null : vm.Observaciones.Trim();
            if (observaciones?.Length > 500)
                return BadRequest(new { mensaje = "Las observaciones de liberación no pueden superar 500 caracteres." });

            var usuarioId = ObtenerUsuarioID();
            await using (var cn = new SqlConnection(ConnectionString))
            {
                await cn.OpenAsync();
                var permisos = await ObtenerPermisosProduccionUsuarioAsync(usuarioId, cn);
                var autorizado =
                    permisos.EsAdministradorERP ||
                    permisos.EsEncargadoProduccion ||
                    permisos.EsAuxiliarProduccion ||
                    permisos.EsTecnicoProduccion ||
                    permisos.EsOperadorProduccion;

                if (!autorizado)
                    return StatusCode(403, new { mensaje = "No tienes permiso para liberar la máquina." });
            }

            LimpiarMensajesOperacionCierre();

            await LiberarMaquina(new ProduccionLiberarMaquinaPostVm
            {
                EjecucionProduccionID = vm.EjecucionProduccionID,
                Observaciones = observaciones
            });

            return RespuestaJsonDesdeTempDataCierre("Máquina liberada correctamente.");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TerminarOperativa(ProduccionTerminarOperativaPostVm vm)
        {
            if (!UsuarioEnSesion())
                return Unauthorized();

            if (vm.EjecucionProduccionID <= 0)
                return BadRequest(new { mensaje = "No se recibió una ejecución de Producción válida." });

            var observaciones = string.IsNullOrWhiteSpace(vm.Observaciones) ? null : vm.Observaciones.Trim();
            if (observaciones?.Length > 500)
                return BadRequest(new { mensaje = "Las observaciones no pueden superar 500 caracteres." });

            var usuarioId = ObtenerUsuarioID();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var ejecucion = await ObtenerEjecucionAsync(vm.EjecucionProduccionID, cn);
            if (ejecucion == null)
                return NotFound(new { mensaje = "No se encontró la ejecución de Producción." });

            var permisos = await ObtenerPermisosProduccionUsuarioAsync(usuarioId, cn);
            var puedeTerminarNormal =
                permisos.EsAdministradorERP ||
                permisos.EsEncargadoProduccion ||
                permisos.EsAuxiliarProduccion ||
                permisos.EsTecnicoProduccion ||
                permisos.EsOperadorProduccion;

            if (vm.TerminarParcial)
            {
                if (!permisos.EsTecnicoProduccion && !permisos.EsAuxiliarProduccion)
                    return StatusCode(403, new { mensaje = "La terminación parcial autorizada por Planeación solo puede ejecutarla un Auxiliar o Técnico de Producción." });

                var relacionParcial = await ObtenerParejaLhRhProduccionAsync(ejecucion.ProgramaProduccionID, cn);
                if (relacionParcial != null)
                    return BadRequest(new { mensaje = "La terminación parcial no puede ejecutarse sobre una producción LH/RH. La pareja física debe resolverse como una sola operación." });

                LimpiarMensajesOperacionCierre();
                await Terminar(new ProduccionTerminarPostVm
                {
                    EjecucionProduccionID = vm.EjecucionProduccionID,
                    TerminarParcial = true,
                    Observaciones = observaciones
                });

                return RespuestaJsonDesdeTempDataCierre("Terminación parcial ejecutada correctamente.");
            }

            if (!puedeTerminarNormal)
                return StatusCode(403, new { mensaje = "No tienes permiso para terminar la producción." });

            if (!ejecucion.FechaLiberacionMaquina.HasValue)
                return BadRequest(new { mensaje = "Primero debes liberar físicamente la máquina antes de terminar la producción." });

            var relacion = await ObtenerParejaLhRhProduccionAsync(ejecucion.ProgramaProduccionID, cn);
            if (relacion == null)
            {
                LimpiarMensajesOperacionCierre();
                await Terminar(new ProduccionTerminarPostVm
                {
                    EjecucionProduccionID = vm.EjecucionProduccionID,
                    TerminarParcial = false,
                    Observaciones = observaciones
                });

                return RespuestaJsonDesdeTempDataCierre("Producción terminada correctamente.");
            }

            return await TerminarLhRhOperativaAsync(ejecucion, relacion, observaciones, usuarioId, cn);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PrepararCierreDocumentalOperativa(ProduccionPrepararCierreDocumentalOperativaPostVm vm)
        {
            if (!UsuarioEnSesion())
                return Unauthorized();

            if (vm.EjecucionProduccionID <= 0)
                return BadRequest(new { mensaje = "No se recibió una ejecución de Producción válida." });

            var usuarioId = ObtenerUsuarioID();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);

            try
            {
                var ejecucion = await ObtenerEjecucionAsync(vm.EjecucionProduccionID, cn, tx);
                if (ejecucion == null)
                {
                    await tx.RollbackAsync();
                    return NotFound(new { mensaje = "No se encontró la ejecución de Producción." });
                }

                var permisos = await ObtenerPermisosProduccionUsuarioAsync(usuarioId, cn, tx);
                if (!PuedeGestionarCierreDocumentalOperativo(permisos))
                {
                    await tx.RollbackAsync();
                    return StatusCode(403, new { mensaje = "El paso a cierre documental solo puede ejecutarlo Supervisión/Auxiliar de Producción." });
                }

                var relacion = await ObtenerParejaLhRhProduccionAsync(ejecucion.ProgramaProduccionID, cn, tx);
                var lados = await ObtenerLadosCierreTransaccionAsync(ejecucion.ProgramaProduccionID, relacion, cn, tx);
                ValidarParejaCierreOperativo(relacion, lados.actual, lados.pareja);

                if (lados.actual.EstaListaCierreDocumental && (lados.pareja == null || lados.pareja.EstaListaCierreDocumental))
                {
                    await tx.RollbackAsync();
                    return Json(new { ok = true, mensaje = "La producción ya se encuentra lista para cierre documental." });
                }

                if (!lados.actual.PuedePasarAListaCierreDocumental || (lados.pareja != null && !lados.pareja.PuedePasarAListaCierreDocumental))
                {
                    await tx.RollbackAsync();
                    return BadRequest(new { mensaje = ConstruirMensajePendienteCierreDocumental(lados.actual, lados.pareja) });
                }

                var ejecucionParejaId = lados.pareja?.EjecucionProduccionID;
                var programaParejaId = lados.pareja?.ProgramaProduccionID;
                var esperadas = lados.pareja == null ? 1 : 2;

                const string sqlEjecucion = @"
UPDATE dbo.Produccion_Ejecucion
SET EstatusID=@ListaCierreDocumental,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=GETDATE()
WHERE Activo=1
  AND EstatusID IN(@TerminadoParcial,@Terminado)
  AND
  (
      EjecucionProduccionID=@EjecucionProduccionID
      OR (@EjecucionParejaID IS NOT NULL AND EjecucionProduccionID=@EjecucionParejaID)
  );
SELECT @@ROWCOUNT;";

                int filasEjecucion;
                await using (var cmd = new SqlCommand(sqlEjecucion, cn, tx))
                {
                    cmd.Parameters.Add("@ListaCierreDocumental", SqlDbType.Int).Value = ProduccionEstatus.ListaCierreDocumental;
                    cmd.Parameters.Add("@TerminadoParcial", SqlDbType.Int).Value = ProduccionEstatus.TerminadoParcial;
                    cmd.Parameters.Add("@Terminado", SqlDbType.Int).Value = ProduccionEstatus.Terminado;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = lados.actual.EjecucionProduccionID;
                    cmd.Parameters.Add("@EjecucionParejaID", SqlDbType.Int).Value = (object?)ejecucionParejaId ?? DBNull.Value;
                    filasEjecucion = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                }

                if (filasEjecucion != esperadas)
                    throw new InvalidOperationException("La ejecución cambió de estado mientras se preparaba el cierre documental. No se aplicaron cambios parciales.");

                const string sqlPrograma = @"
UPDATE dbo.Planeacion_ProgramaProduccion
SET EstatusID=@ListaCierreDocumental,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=GETDATE()
WHERE Activo=1
  AND EstatusID=@Terminado
  AND
  (
      ProgramaProduccionID=@ProgramaProduccionID
      OR (@ProgramaParejaID IS NOT NULL AND ProgramaProduccionID=@ProgramaParejaID)
  );
SELECT @@ROWCOUNT;";

                int filasPrograma;
                await using (var cmd = new SqlCommand(sqlPrograma, cn, tx))
                {
                    cmd.Parameters.Add("@ListaCierreDocumental", SqlDbType.Int).Value = ProgramaProduccionEstatus.ListaCierreDocumental;
                    cmd.Parameters.Add("@Terminado", SqlDbType.Int).Value = ProgramaProduccionEstatus.Terminado;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = lados.actual.ProgramaProduccionID;
                    cmd.Parameters.Add("@ProgramaParejaID", SqlDbType.Int).Value = (object?)programaParejaId ?? DBNull.Value;
                    filasPrograma = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                }

                if (filasPrograma != esperadas)
                    throw new InvalidOperationException("El Programa cambió de estado mientras se preparaba el cierre documental. No se aplicaron cambios parciales.");

                await tx.CommitAsync();

                return Json(new
                {
                    ok = true,
                    mensaje = lados.pareja == null
                        ? "Todas las cajas ya fueron recibidas por Almacén PT. La OF quedó en estatus 8: Lista para cierre documental."
                        : "Todas las cajas LH/RH ya fueron recibidas por Almacén PT. Ambas OF quedaron juntas en estatus 8: Lista para cierre documental."
                });
            }
            catch (Exception ex)
            {
                try { await tx.RollbackAsync(); } catch { }
                return BadRequest(new { mensaje = "No fue posible preparar el cierre documental: " + ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CerrarDocumentalOperativa(ProduccionCerrarDocumentalOperativaPostVm vm)
        {
            if (!UsuarioEnSesion())
                return Unauthorized();

            if (vm.EjecucionProduccionID <= 0)
                return BadRequest(new { mensaje = "No se recibió una ejecución de Producción válida." });

            var observaciones = string.IsNullOrWhiteSpace(vm.Observaciones) ? null : vm.Observaciones.Trim();
            if (observaciones?.Length > 500)
                return BadRequest(new { mensaje = "Las observaciones del cierre documental no pueden superar 500 caracteres." });

            var usuarioId = ObtenerUsuarioID();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);

            try
            {
                var ejecucion = await ObtenerEjecucionAsync(vm.EjecucionProduccionID, cn, tx);
                if (ejecucion == null)
                {
                    await tx.RollbackAsync();
                    return NotFound(new { mensaje = "No se encontró la ejecución de Producción." });
                }

                var permisos = await ObtenerPermisosProduccionUsuarioAsync(usuarioId, cn, tx);
                if (!PuedeGestionarCierreDocumentalOperativo(permisos))
                {
                    await tx.RollbackAsync();
                    return StatusCode(403, new { mensaje = "El cierre documental solo puede ejecutarlo Supervisión/Auxiliar de Producción." });
                }

                var relacion = await ObtenerParejaLhRhProduccionAsync(ejecucion.ProgramaProduccionID, cn, tx);
                var lados = await ObtenerLadosCierreTransaccionAsync(ejecucion.ProgramaProduccionID, relacion, cn, tx);
                ValidarParejaCierreOperativo(relacion, lados.actual, lados.pareja);

                if (lados.actual.EstaCerrada && (lados.pareja == null || lados.pareja.EstaCerrada))
                {
                    await tx.RollbackAsync();
                    return Json(new { ok = true, mensaje = "La producción ya se encuentra cerrada documentalmente." });
                }

                if (!lados.actual.EstaListaCierreDocumental || (lados.pareja != null && !lados.pareja.EstaListaCierreDocumental))
                {
                    await tx.RollbackAsync();
                    return BadRequest(new { mensaje = "La producción todavía no está en estatus 8 (Lista para cierre documental) en todos los lados de la operación física." });
                }

                if (!lados.actual.TodasCajasRecibidasAlmacen || (lados.pareja != null && !lados.pareja.TodasCajasRecibidasAlmacen))
                {
                    await tx.RollbackAsync();
                    return BadRequest(new { mensaje = "No se puede cerrar: todavía existen cajas que Almacén PT no ha recibido." });
                }

                var ejecucionParejaId = lados.pareja?.EjecucionProduccionID;
                var programaParejaId = lados.pareja?.ProgramaProduccionID;
                var esperadas = lados.pareja == null ? 1 : 2;

                const string sqlEjecucion = @"
UPDATE dbo.Produccion_Ejecucion
SET EstatusID=@Cerrado,
    FechaFinReal=ISNULL(FechaFinReal,GETDATE()),
    Observaciones=
        CASE
            WHEN @Observaciones IS NULL OR LTRIM(RTRIM(@Observaciones))=N'' THEN Observaciones
            WHEN Observaciones IS NULL OR LTRIM(RTRIM(Observaciones))=N'' THEN N'Cierre documental: '+@Observaciones
            ELSE Observaciones+CHAR(13)+CHAR(10)+N'Cierre documental: '+@Observaciones
        END,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=GETDATE()
WHERE Activo=1
  AND EstatusID=@ListaCierreDocumental
  AND
  (
      EjecucionProduccionID=@EjecucionProduccionID
      OR (@EjecucionParejaID IS NOT NULL AND EjecucionProduccionID=@EjecucionParejaID)
  );
SELECT @@ROWCOUNT;";

                int filasEjecucion;
                await using (var cmd = new SqlCommand(sqlEjecucion, cn, tx))
                {
                    cmd.Parameters.Add("@Cerrado", SqlDbType.Int).Value = ProduccionEstatus.Cerrado;
                    cmd.Parameters.Add("@ListaCierreDocumental", SqlDbType.Int).Value = ProduccionEstatus.ListaCierreDocumental;
                    cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 500).Value = (object?)observaciones ?? DBNull.Value;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = lados.actual.EjecucionProduccionID;
                    cmd.Parameters.Add("@EjecucionParejaID", SqlDbType.Int).Value = (object?)ejecucionParejaId ?? DBNull.Value;
                    filasEjecucion = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                }

                if (filasEjecucion != esperadas)
                    throw new InvalidOperationException("La ejecución cambió de estado durante el cierre documental. No se permitirá un cierre parcial.");

                const string sqlPrograma = @"
UPDATE dbo.Planeacion_ProgramaProduccion
SET EstatusID=@Cerrado,
    FechaFinReal=ISNULL(FechaFinReal,GETDATE()),
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=GETDATE()
WHERE Activo=1
  AND EstatusID=@ListaCierreDocumental
  AND
  (
      ProgramaProduccionID=@ProgramaProduccionID
      OR (@ProgramaParejaID IS NOT NULL AND ProgramaProduccionID=@ProgramaParejaID)
  );
SELECT @@ROWCOUNT;";

                int filasPrograma;
                await using (var cmd = new SqlCommand(sqlPrograma, cn, tx))
                {
                    cmd.Parameters.Add("@Cerrado", SqlDbType.Int).Value = ProgramaProduccionEstatus.Cerrado;
                    cmd.Parameters.Add("@ListaCierreDocumental", SqlDbType.Int).Value = ProgramaProduccionEstatus.ListaCierreDocumental;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = lados.actual.ProgramaProduccionID;
                    cmd.Parameters.Add("@ProgramaParejaID", SqlDbType.Int).Value = (object?)programaParejaId ?? DBNull.Value;
                    filasPrograma = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                }

                if (filasPrograma != esperadas)
                    throw new InvalidOperationException("El Programa cambió de estado durante el cierre documental. No se permitirá un cierre parcial.");

                await tx.CommitAsync();

                return Json(new
                {
                    ok = true,
                    mensaje = lados.pareja == null
                        ? "Cierre documental completado. La OF quedó en estatus 9: Cerrado."
                        : "Cierre documental completado. Las dos OF LH/RH quedaron juntas en estatus 9: Cerrado."
                });
            }
            catch (Exception ex)
            {
                try { await tx.RollbackAsync(); } catch { }
                return BadRequest(new { mensaje = "No fue posible cerrar documentalmente la producción: " + ex.Message });
            }
        }

        private async Task<IActionResult> TerminarLhRhOperativaAsync(
            ProduccionEjecucionVm ejecucion,
            ProduccionParejaLhRhVm relacion,
            string? observaciones,
            int usuarioId,
            SqlConnection cn)
        {
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);

            try
            {
                if (!relacion.EsCompatibleFisicamente)
                    throw new InvalidOperationException(ResolverInconsistenciaParejaCierre(relacion));

                if (!relacion.EjecucionParejaID.HasValue || relacion.EjecucionParejaID.Value <= 0)
                    throw new InvalidOperationException("No se encontró la ejecución pareja LH/RH. No se permitirá terminar una sola OF de la operación física.");

                var actual = await ObtenerEjecucionAsync(ejecucion.EjecucionProduccionID, cn, tx);
                var pareja = await ObtenerEjecucionAsync(relacion.EjecucionParejaID.Value, cn, tx);

                if (actual == null || pareja == null)
                    throw new InvalidOperationException("No fue posible recuperar las dos ejecuciones LH/RH.");

                if (actual.MaquinaID != pareja.MaquinaID)
                    throw new InvalidOperationException("Las dos ejecuciones LH/RH ya no pertenecen a la misma máquina.");

                if (!actual.FechaLiberacionMaquina.HasValue || !pareja.FechaLiberacionMaquina.HasValue)
                    throw new InvalidOperationException("La máquina debe estar liberada para las dos OF LH/RH antes de terminar la producción.");

                if (actual.EstatusID is not (ProduccionEstatus.EnProduccion or ProduccionEstatus.Pausado) ||
                    pareja.EstatusID is not (ProduccionEstatus.EnProduccion or ProduccionEstatus.Pausado))
                {
                    throw new InvalidOperationException("Las dos OF LH/RH deben encontrarse en un estado terminable y consistente.");
                }

                await RecalcularTotalesEjecucionAsync(actual.EjecucionProduccionID, usuarioId, cn, tx);
                await RecalcularTotalesEjecucionAsync(pareja.EjecucionProduccionID, usuarioId, cn, tx);

                var validacionActual = await ValidarTerminarProduccionAsync(actual.EjecucionProduccionID, false, cn, tx);
                var validacionPareja = await ValidarTerminarProduccionAsync(pareja.EjecucionProduccionID, false, cn, tx);

                var ladoActual = await CargarLadoCierreOperativoAsync(actual.ProgramaProduccionID, cn, tx);
                var ladoPareja = await CargarLadoCierreOperativoAsync(pareja.ProgramaProduccionID, cn, tx);

                if (!validacionActual.Permitido)
                    throw new InvalidOperationException($"{ladoActual?.OFTexto ?? $"Programa {actual.ProgramaProduccionID}"}: {validacionActual.Mensaje}");

                if (!validacionPareja.Permitido)
                    throw new InvalidOperationException($"{ladoPareja?.OFTexto ?? $"Programa {pareja.ProgramaProduccionID}"}: {validacionPareja.Mensaje}");

                const string sqlCerrar = @"
UPDATE dbo.Produccion_Ejecucion
SET FechaFinReal=GETDATE(),
    EstatusID=@Terminado,
    Observaciones=
        CASE
            WHEN @Observaciones IS NULL OR LTRIM(RTRIM(@Observaciones))=N'' THEN Observaciones
            WHEN Observaciones IS NULL OR LTRIM(RTRIM(Observaciones))=N'' THEN @Observaciones
            ELSE Observaciones+CHAR(13)+CHAR(10)+@Observaciones
        END,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=GETDATE()
WHERE Activo=1
  AND FechaLiberacionMaquina IS NOT NULL
  AND EstatusID IN(@EnProduccion,@Pausado)
  AND EjecucionProduccionID IN(@EjecucionA,@EjecucionB);
SELECT @@ROWCOUNT;";

                int filas;
                await using (var cmd = new SqlCommand(sqlCerrar, cn, tx))
                {
                    cmd.Parameters.Add("@Terminado", SqlDbType.Int).Value = ProduccionEstatus.Terminado;
                    cmd.Parameters.Add("@EnProduccion", SqlDbType.Int).Value = ProduccionEstatus.EnProduccion;
                    cmd.Parameters.Add("@Pausado", SqlDbType.Int).Value = ProduccionEstatus.Pausado;
                    cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 500).Value = (object?)observaciones ?? DBNull.Value;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    cmd.Parameters.Add("@EjecucionA", SqlDbType.Int).Value = actual.EjecucionProduccionID;
                    cmd.Parameters.Add("@EjecucionB", SqlDbType.Int).Value = pareja.EjecucionProduccionID;
                    filas = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                }

                if (filas != 2)
                    throw new InvalidOperationException("Las ejecuciones LH/RH cambiaron de estado durante la terminación. No se aplicará una terminación parcial.");

                await MarcarProgramaTerminadoAsync(actual.ProgramaProduccionID, usuarioId, cn, tx);
                await MarcarProgramaTerminadoAsync(pareja.ProgramaProduccionID, usuarioId, cn, tx);

                await tx.CommitAsync();

                return Json(new
                {
                    ok = true,
                    mensaje = "Producción LH/RH terminada correctamente. Las dos OF quedaron terminadas en la misma operación y ahora deben completar la recepción de cajas en Almacén PT antes del cierre documental."
                });
            }
            catch (Exception ex)
            {
                try { await tx.RollbackAsync(); } catch { }
                return BadRequest(new { mensaje = "No fue posible terminar la producción LH/RH: " + ex.Message });
            }
        }

        private async Task CompletarValidacionesCierreOperativoAsync(
            ProduccionCierreLadoOperativoVm lado,
            bool validarTerminacionParcial,
            SqlConnection cn,
            SqlTransaction tx)
        {
            if (lado.EjecucionProduccionID <= 0)
                return;

            if (lado.EstatusEjecucionID == ProduccionEstatus.EnProduccion && !lado.MaquinaLiberada)
            {
                var liberacion = await ValidarLiberacionMaquinaAsync(lado.EjecucionProduccionID, cn, tx);
                lado.LiberacionPermitidaRegla = liberacion.Permitido;
                lado.MensajeLiberacion = liberacion.Mensaje;
            }
            else if (lado.MaquinaLiberada)
            {
                lado.LiberacionPermitidaRegla = false;
                lado.MensajeLiberacion = "La máquina ya fue liberada.";
            }

            if (lado.EstatusEjecucionID is ProduccionEstatus.EnProduccion or ProduccionEstatus.Pausado)
            {
                var terminacion = await ValidarTerminarProduccionAsync(lado.EjecucionProduccionID, false, cn, tx);
                lado.TerminacionPermitidaRegla = terminacion.Permitido;
                lado.BloqueosTerminacion = terminacion.Bloqueos.ToList();
            }

            if (validarTerminacionParcial && lado.EstatusEjecucionID == ProduccionEstatus.Pausado)
            {
                var parcial = await ValidarTerminarProduccionAsync(lado.EjecucionProduccionID, true, cn, tx);
                lado.TerminacionParcialPermitidaRegla = parcial.Permitido && parcial.ParoAutorizacionTerminacionParcialID.HasValue;
                lado.MotivoTerminacionParcial = parcial.MotivoAutorizacionTerminacionParcial;
                lado.FechaAutorizacionTerminacionParcial = parcial.FechaAutorizacionTerminacionParcial;
            }
        }

        private async Task<ProduccionCierreLadoOperativoVm?> CargarLadoCierreOperativoAsync(
            int programaProduccionId,
            SqlConnection cn,
            SqlTransaction? tx)
        {
            const string sql = @"
SELECT TOP(1)
    e.EjecucionProduccionID,
    e.ProgramaProduccionID,
    COALESCE(NULLIF(LTRIM(RTRIM(s.NumeroOFRecibida)),N''),NULLIF(LTRIM(RTRIM(s.FolioSolicitud)),N''),N'Programa '+CONVERT(NVARCHAR(20),e.ProgramaProduccionID)) AS OFTexto,
    e.MaquinaID,
    e.MaquinaCodigo,
    e.MaquinaNombre,
    e.ParteID,
    e.NumeroParte,
    e.ReferenciaSAP,
    e.DescripcionParte,
    e.MoldeID,
    e.MoldeCodigo,
    e.EstatusID AS EstatusEjecucionID,
    pp.EstatusID AS EstatusProgramaID,
    e.FechaInicioReal,
    e.FechaFinReal,
    e.FechaLiberacionMaquina,
    ISNULL(e.CantidadPlaneada,0) AS CantidadPlaneada,
    ISNULL(e.CantidadOKTotal,0) AS CantidadOK,
    ISNULL(e.CantidadSospechosaTotal,0) AS CantidadSospechosa,
    ISNULL(e.CantidadScrapTotal,0) AS CantidadScrap,
    ISNULL(cajas.TotalCajas,0) AS TotalCajas,
    ISNULL(cajas.CajasFormadas,0) AS CajasFormadas,
    ISNULL(cajas.CajasPendientesCalidad,0) AS CajasPendientesCalidad,
    ISNULL(cajas.CajasLiberadasCalidad,0) AS CajasLiberadasCalidad,
    ISNULL(cajas.CajasRetenidas,0) AS CajasRetenidas,
    ISNULL(cajas.CajasZonaVerde,0) AS CajasZonaVerde,
    ISNULL(cajas.CajasSalidaProduccion,0) AS CajasSalidaProduccion,
    ISNULL(cajas.CajasRecibidasAlmacen,0) AS CajasRecibidasAlmacen,
    CAST(CASE WHEN EXISTS
    (
        SELECT 1
        FROM dbo.Produccion_Paros p
        WHERE p.EjecucionProduccionID=e.EjecucionProduccionID
          AND p.Activo=1
          AND p.FechaFinParo IS NULL
    ) THEN 1 ELSE 0 END AS BIT) AS TieneParoAbierto,
    CAST(CASE WHEN EXISTS
    (
        SELECT 1
        FROM dbo.Produccion_TiempoExtra te
        WHERE te.EjecucionProduccionID=e.EjecucionProduccionID
          AND te.Activo=1
          AND te.FechaHoraFin IS NULL
          AND UPPER(LTRIM(RTRIM(ISNULL(te.Estado,N'')))) IN(N'EN_CURSO',N'PAUSADO')
    ) THEN 1 ELSE 0 END AS BIT) AS TieneTiempoExtraActivo
FROM dbo.Produccion_Ejecucion e
INNER JOIN dbo.Planeacion_ProgramaProduccion pp
    ON pp.ProgramaProduccionID=e.ProgramaProduccionID
   AND pp.Activo=1
LEFT JOIN dbo.SolicitudesProduccion s
    ON s.SolicitudProduccionID=e.SolicitudProduccionID
   AND s.Activo=1
OUTER APPLY
(
    SELECT
        COUNT(1) AS TotalCajas,
        SUM(CASE WHEN c.EstadoCajaID=@CajaFormada THEN 1 ELSE 0 END) AS CajasFormadas,
        SUM(CASE WHEN c.EstadoCajaID=@CajaPendienteCalidad THEN 1 ELSE 0 END) AS CajasPendientesCalidad,
        SUM(CASE WHEN c.EstadoCajaID=@CajaLiberadaCalidad THEN 1 ELSE 0 END) AS CajasLiberadasCalidad,
        SUM(CASE WHEN c.EstadoCajaID=@CajaRetenida THEN 1 ELSE 0 END) AS CajasRetenidas,
        SUM(CASE WHEN c.EstadoCajaID=@CajaZonaVerde THEN 1 ELSE 0 END) AS CajasZonaVerde,
        SUM(CASE WHEN c.EstadoCajaID=@CajaSalidaProduccion THEN 1 ELSE 0 END) AS CajasSalidaProduccion,
        SUM(CASE WHEN c.EstadoCajaID=@CajaRecibidaAlmacen THEN 1 ELSE 0 END) AS CajasRecibidasAlmacen
    FROM dbo.Produccion_Cajas c
    WHERE c.EjecucionProduccionID=e.EjecucionProduccionID
      AND c.Activo=1
) cajas
WHERE e.ProgramaProduccionID=@ProgramaProduccionID
  AND e.Activo=1
ORDER BY e.EjecucionProduccionID DESC;";

            await using var cmd = tx == null ? new SqlCommand(sql, cn) : new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;
            cmd.Parameters.Add("@CajaFormada", SqlDbType.Int).Value = ProduccionCajaEstatus.FormadaProduccion;
            cmd.Parameters.Add("@CajaPendienteCalidad", SqlDbType.Int).Value = ProduccionCajaEstatus.PendienteCalidad;
            cmd.Parameters.Add("@CajaLiberadaCalidad", SqlDbType.Int).Value = ProduccionCajaEstatus.LiberadaCalidad;
            cmd.Parameters.Add("@CajaRetenida", SqlDbType.Int).Value = ProduccionCajaEstatus.RetenidaGp12Scrap;
            cmd.Parameters.Add("@CajaZonaVerde", SqlDbType.Int).Value = ProduccionCajaEstatus.ZonaVerde;
            cmd.Parameters.Add("@CajaSalidaProduccion", SqlDbType.Int).Value = ProduccionCajaEstatus.SalidaProduccion;
            cmd.Parameters.Add("@CajaRecibidaAlmacen", SqlDbType.Int).Value = ProduccionCajaEstatus.RecibidaAlmacenPt;

            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync()) return null;

            return new ProduccionCierreLadoOperativoVm
            {
                EjecucionProduccionID = Convert.ToInt32(rd["EjecucionProduccionID"]),
                ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),
                OFTexto = rd["OFTexto"]?.ToString()?.Trim() ?? $"Programa {programaProduccionId}",
                MaquinaID = rd["MaquinaID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaquinaID"]),
                MaquinaCodigo = rd["MaquinaCodigo"] == DBNull.Value ? null : rd["MaquinaCodigo"]?.ToString()?.Trim(),
                MaquinaNombre = rd["MaquinaNombre"] == DBNull.Value ? null : rd["MaquinaNombre"]?.ToString()?.Trim(),
                ParteID = rd["ParteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ParteID"]),
                NumeroParte = rd["NumeroParte"] == DBNull.Value ? null : rd["NumeroParte"]?.ToString()?.Trim(),
                ReferenciaSAP = rd["ReferenciaSAP"] == DBNull.Value ? null : rd["ReferenciaSAP"]?.ToString()?.Trim(),
                DescripcionParte = rd["DescripcionParte"] == DBNull.Value ? null : rd["DescripcionParte"]?.ToString()?.Trim(),
                MoldeID = rd["MoldeID"] == DBNull.Value ? null : Convert.ToInt32(rd["MoldeID"]),
                MoldeCodigo = rd["MoldeCodigo"] == DBNull.Value ? null : rd["MoldeCodigo"]?.ToString()?.Trim(),
                EstatusEjecucionID = Convert.ToInt32(rd["EstatusEjecucionID"]),
                EstatusProgramaID = Convert.ToInt32(rd["EstatusProgramaID"]),
                FechaInicioReal = rd["FechaInicioReal"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaInicioReal"]),
                FechaFinReal = rd["FechaFinReal"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaFinReal"]),
                FechaLiberacionMaquina = rd["FechaLiberacionMaquina"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaLiberacionMaquina"]),
                CantidadPlaneada = Convert.ToInt32(rd["CantidadPlaneada"]),
                CantidadOK = Convert.ToInt32(rd["CantidadOK"]),
                CantidadSospechosa = Convert.ToInt32(rd["CantidadSospechosa"]),
                CantidadScrap = Convert.ToInt32(rd["CantidadScrap"]),
                TotalCajas = Convert.ToInt32(rd["TotalCajas"]),
                CajasFormadas = Convert.ToInt32(rd["CajasFormadas"]),
                CajasPendientesCalidad = Convert.ToInt32(rd["CajasPendientesCalidad"]),
                CajasLiberadasCalidad = Convert.ToInt32(rd["CajasLiberadasCalidad"]),
                CajasRetenidas = Convert.ToInt32(rd["CajasRetenidas"]),
                CajasZonaVerde = Convert.ToInt32(rd["CajasZonaVerde"]),
                CajasSalidaProduccion = Convert.ToInt32(rd["CajasSalidaProduccion"]),
                CajasRecibidasAlmacen = Convert.ToInt32(rd["CajasRecibidasAlmacen"]),
                TieneParoAbierto = rd["TieneParoAbierto"] != DBNull.Value && Convert.ToBoolean(rd["TieneParoAbierto"]),
                TieneTiempoExtraActivo = rd["TieneTiempoExtraActivo"] != DBNull.Value && Convert.ToBoolean(rd["TieneTiempoExtraActivo"])
            };
        }

        private async Task<(ProduccionCierreLadoOperativoVm actual, ProduccionCierreLadoOperativoVm? pareja)> ObtenerLadosCierreTransaccionAsync(
            int programaProduccionId,
            ProduccionParejaLhRhVm? relacion,
            SqlConnection cn,
            SqlTransaction tx)
        {
            var actual = await CargarLadoCierreOperativoAsync(programaProduccionId, cn, tx)
                ?? throw new InvalidOperationException("No se encontró la ejecución principal de Producción.");

            ProduccionCierreLadoOperativoVm? pareja = null;
            if (relacion != null)
            {
                pareja = await CargarLadoCierreOperativoAsync(relacion.ProgramaParejaID, cn, tx)
                    ?? throw new InvalidOperationException($"No se encontró la ejecución pareja del Programa {relacion.ProgramaParejaID}.");
            }

            return (actual, pareja);
        }

        private static void ValidarParejaCierreOperativo(
            ProduccionParejaLhRhVm? relacion,
            ProduccionCierreLadoOperativoVm actual,
            ProduccionCierreLadoOperativoVm? pareja)
        {
            if (relacion == null)
            {
                if (pareja != null)
                    throw new InvalidOperationException("Se recibió una ejecución pareja sin relación LH/RH válida.");
                return;
            }

            if (!relacion.EsCompatibleFisicamente)
                throw new InvalidOperationException(ResolverInconsistenciaParejaCierre(relacion));

            if (pareja == null)
                throw new InvalidOperationException("No se encontró la ejecución pareja LH/RH.");

            if (actual.MaquinaID != pareja.MaquinaID)
                throw new InvalidOperationException("Las dos ejecuciones LH/RH ya no pertenecen a la misma máquina.");
        }

        private static string ResolverInconsistenciaParejaCierre(ProduccionParejaLhRhVm relacion)
        {
            if (!relacion.MismaMaquina)
                return $"La pareja LH/RH grupo {relacion.GrupoLhRh} ya no conserva la misma máquina.";
            if (!relacion.MismoMolde)
                return $"La pareja LH/RH grupo {relacion.GrupoLhRh} ya no conserva el mismo molde.";
            if (!relacion.MismaVentanaProgramada)
                return $"La pareja LH/RH grupo {relacion.GrupoLhRh} ya no conserva la misma ventana programada.";
            return $"La pareja LH/RH grupo {relacion.GrupoLhRh} es inconsistente.";
        }

        private static bool PuedeGestionarCierreDocumentalOperativo(ProduccionPermisosUsuario permisos)
        {
            return permisos.EsAdministradorERP || permisos.EsEncargadoProduccion || permisos.EsAuxiliarProduccion;
        }

        private static string ConstruirMensajePendienteCierreDocumental(
            ProduccionCierreLadoOperativoVm actual,
            ProduccionCierreLadoOperativoVm? pareja)
        {
            var lados = pareja == null ? new[] { actual } : new[] { actual, pareja };
            var mensajes = lados.Select(lado =>
            {
                if (!lado.EsProduccionTerminada)
                    return $"{lado.OFTexto}: la Producción todavía no está terminada.";
                if (!lado.TodasCajasRecibidasAlmacen)
                    return $"{lado.OFTexto}: faltan {lado.CajasPendientesRecepcionAlmacen:N0} caja(s) por recibir en Almacén PT.";
                if (lado.EstatusProgramaID != ProgramaProduccionEstatus.Terminado)
                    return $"{lado.OFTexto}: el Programa no se encuentra en estatus Terminado.";
                return $"{lado.OFTexto}: el estado cambió y debe volver a consultarse.";
            });

            return string.Join(" ", mensajes);
        }

        private void LimpiarMensajesOperacionCierre()
        {
            TempData.Remove("Error");
            TempData.Remove("Success");
            TempData.Remove("Info");
        }

        private IActionResult RespuestaJsonDesdeTempDataCierre(string mensajePredeterminado)
        {
            var error = TempData["Error"]?.ToString();
            if (!string.IsNullOrWhiteSpace(error))
                return BadRequest(new { ok = false, mensaje = error });

            var success = TempData["Success"]?.ToString();
            if (!string.IsNullOrWhiteSpace(success))
                return Json(new { ok = true, mensaje = success });

            var info = TempData["Info"]?.ToString();
            if (!string.IsNullOrWhiteSpace(info))
                return Json(new { ok = true, mensaje = info });

            return Json(new { ok = true, mensaje = mensajePredeterminado });
        }
    }
}
