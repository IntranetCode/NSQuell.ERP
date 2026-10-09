using ERP.NSQuell.Models;
using ERP.NSQuell.Servicios.Produccion;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace ERP.NSQuell.Controllers
{
    public sealed partial class ProduccionController
    {
        // NSQ_INTERRUPCION_PLANEACION_V1_1. Misma operación transaccional; entrada desde Planeación.
        [HttpGet("/PlaneacionCalendarioMaquinas/InterrupcionOperativa", Name = "NSQ_INTERRUPCION_PLANEACION")]
        public async Task<IActionResult> InterrupcionOperativa(int programaProduccionId, bool soloLectura = false)
        {
            if (!UsuarioEnSesion() || ObtenerUsuarioID() <= 0) return Unauthorized();
            if (programaProduccionId <= 0) return BadRequest(new { mensaje = "Programa inválido." });
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            try
            {
                var actual = await CargarLadoCierreOperativoAsync(programaProduccionId, cn, tx);
                if (actual == null) { await tx.RollbackAsync(); return NotFound(); }
                var permisos = await ObtenerPermisosProduccionUsuarioAsync(ObtenerUsuarioID(), cn, tx);
                var autorizado = permisos.EsAdministradorERP || permisos.EsEncargadoProduccion ||
                    permisos.EsAuxiliarProduccion || permisos.EsTecnicoProduccion ||
                    await EsUsuarioPlaneacionInterrupcionAsync(ObtenerUsuarioID(), cn, tx);
                var relacion = await ObtenerParejaLhRhProduccionAsync(programaProduccionId, cn, tx);
                var pareja = relacion == null ? null : await CargarLadoCierreOperativoAsync(relacion.ProgramaParejaID, cn, tx);
                var consistente = relacion == null || (relacion.EsCompatibleFisicamente && pareja != null);
                var vm = new ProduccionCierreOperativoVm
                {
                    Actual = actual, Pareja = pareja, GrupoLhRh = relacion?.GrupoLhRh,
                    ParejaConsistente = consistente,
                    MotivoInconsistenciaPareja = consistente ? null : "La pareja LH/RH debe corresponder a la misma operación física.",
                    SoloLectura = soloLectura || !autorizado,
                    PuedeGestionarTerminacion = autorizado
                };
                await tx.RollbackAsync();
                return PartialView("~/Views/ProduccionOperativa/Acciones/_InterrupcionContenido.cshtml", vm);
            }
            catch (Exception ex)
            {
                try { await tx.RollbackAsync(); } catch { }
                return BadRequest(new { mensaje = "No fue posible consultar la interrupción: " + ex.Message });
            }
        }

        [HttpPost("/PlaneacionCalendarioMaquinas/InterrumpirOperativa", Name = "NSQ_INTERRUMPIR_PLANEACION")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> InterrumpirOperativa(int programaProduccionId, string? justificacion)
        {
            if (!UsuarioEnSesion() || ObtenerUsuarioID() <= 0) return Unauthorized();
            justificacion = justificacion?.Trim();
            if (programaProduccionId <= 0 || string.IsNullOrWhiteSpace(justificacion) || justificacion.Length > 500)
                return BadRequest(new { ok = false, mensaje = "Escribe la justificación (hasta 500 caracteres)." });

            var usuarioId = ObtenerUsuarioID();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var permisos = await ObtenerPermisosProduccionUsuarioAsync(usuarioId, cn, tx);
                if (!(permisos.EsAdministradorERP || permisos.EsEncargadoProduccion ||
                    permisos.EsAuxiliarProduccion || permisos.EsTecnicoProduccion ||
                    await EsUsuarioPlaneacionInterrupcionAsync(usuarioId, cn, tx)))
                {
                    await tx.RollbackAsync();
                    return StatusCode(403, new { ok = false, mensaje = "No tienes permiso para interrumpir producción." });
                }
                var actual = await CargarLadoCierreOperativoAsync(programaProduccionId, cn, tx)
                    ?? throw new InvalidOperationException("No se encontró la ejecución vigente.");
                var relacion = await ObtenerParejaLhRhProduccionAsync(programaProduccionId, cn, tx);
                var lados = new List<ProduccionCierreLadoOperativoVm> { actual };
                if (relacion != null)
                {
                    if (!relacion.EsCompatibleFisicamente || !relacion.EjecucionParejaID.HasValue)
                        throw new InvalidOperationException("No se puede interrumpir una sola OF de una pareja LH/RH inconsistente.");
                    var pareja = await CargarLadoCierreOperativoAsync(relacion.ProgramaParejaID, cn, tx)
                        ?? throw new InvalidOperationException("No se encontró la ejecución LH/RH pareja.");
                    if (pareja.MaquinaID != actual.MaquinaID || pareja.EjecucionProduccionID == actual.EjecucionProduccionID)
                        throw new InvalidOperationException("La pareja LH/RH ya no corresponde a la misma máquina.");
                    lados.Add(pareja);
                }

                var yaInterrumpidas = 0;
                foreach (var lado in lados.OrderBy(x => x.ProgramaProduccionID))
                {
                    await using var estado = new SqlCommand(@"
SELECT CASE WHEN e.EstatusID=@TerminadoParcial AND e.FechaLiberacionMaquina IS NOT NULL AND EXISTS
(
    SELECT 1 FROM dbo.Produccion_Eventos corte
    WHERE corte.TipoEvento=N'INTERRUPCION_OPERATIVA' AND corte.ReferenciaTabla=N'Planeacion_ProgramaProduccion'
      AND corte.ReferenciaID=pp.ProgramaProduccionID
) THEN 1 WHEN e.EstatusID IN(@Preparacion,@Produccion,@Pausado) AND pp.EstatusID IN(1,2,3,4) THEN 0 ELSE -1 END
FROM dbo.Planeacion_ProgramaProduccion pp WITH(UPDLOCK,HOLDLOCK)
INNER JOIN dbo.Produccion_Ejecucion e WITH(UPDLOCK,HOLDLOCK) ON e.ProgramaProduccionID=pp.ProgramaProduccionID AND e.Activo=1
WHERE pp.ProgramaProduccionID=@Programa AND pp.Activo=1 AND e.EjecucionProduccionID=@Ejecucion
  AND NOT EXISTS(SELECT 1 FROM dbo.Produccion_Ejecucion nueva WHERE nueva.ProgramaProduccionID=pp.ProgramaProduccionID AND nueva.Activo=1 AND nueva.EjecucionProduccionID>e.EjecucionProduccionID);", cn, tx);
                    estado.Parameters.Add("@Programa", SqlDbType.Int).Value = lado.ProgramaProduccionID;
                    estado.Parameters.Add("@Ejecucion", SqlDbType.Int).Value = lado.EjecucionProduccionID;
                    estado.Parameters.Add("@TerminadoParcial", SqlDbType.Int).Value = ProduccionEstatus.TerminadoParcial;
                    estado.Parameters.Add("@Preparacion", SqlDbType.Int).Value = ProduccionEstatus.EnPreparacion;
                    estado.Parameters.Add("@Produccion", SqlDbType.Int).Value = ProduccionEstatus.EnProduccion;
                    estado.Parameters.Add("@Pausado", SqlDbType.Int).Value = ProduccionEstatus.Pausado;
                    var valor = await estado.ExecuteScalarAsync();
                    if (valor == null || valor == DBNull.Value || Convert.ToInt32(valor) < 0)
                        throw new InvalidOperationException("La OF cambió o ya no está en preparación, producción o pausa. Actualiza el modal.");
                    yaInterrumpidas += Convert.ToInt32(valor);
                }
                if (yaInterrumpidas == lados.Count)
                {
                    await tx.RollbackAsync();
                    return Json(new { ok = true, mensaje = "La operación ya fue interrumpida y su saldo ya fue actualizado." });
                }
                if (yaInterrumpidas != 0)
                    throw new InvalidOperationException("Los lados LH/RH tienen estados distintos. No se modificó ninguno.");

                DateTime fechaCorte;
                await using (var reloj = new SqlCommand("SELECT SYSDATETIME();", cn, tx))
                    fechaCorte = Convert.ToDateTime(await reloj.ExecuteScalarAsync());
                var resultados = new List<TerminacionAnticipadaResultado>();
                foreach (var lado in lados.OrderBy(x => x.ProgramaProduccionID))
                {
                    var resultado = await TerminacionAnticipadaProduccionService.EjecutarAsync(
                        lado.EjecucionProduccionID, justificacion, usuarioId, cn, tx,
                        interrupcionOperativa: true, fechaCorte: fechaCorte);
                    resultados.Add(resultado);
                    // Evento de negocio: motivo completo, totales y referencia permanente al programa histórico.
                    var nota = $"Interrupción {fechaCorte:yyyy-MM-dd HH:mm:ss}. Planeado={resultado.CantidadPlaneada}; OK={resultado.CantidadOK}; sospechoso={resultado.CantidadSospechosa}; scrap={resultado.CantidadScrap}. Justificación: {justificacion}";
                    if (nota.Length > 800) throw new InvalidOperationException("La nota supera la capacidad del historial.");
                    await using var evento = new SqlCommand(@"
INSERT INTO dbo.Produccion_Eventos(EjecucionProduccionID,TipoEvento,EstadoAnterior,EstadoNuevo,Descripcion,ReferenciaTabla,ReferenciaID,UsuarioID,FechaEvento)
VALUES(@Ejecucion,N'INTERRUPCION_OPERATIVA',@Anterior,N'Terminado parcial',@Nota,N'Planeacion_ProgramaProduccion',@Programa,@Usuario,@Corte);", cn, tx);
                    evento.Parameters.Add("@Ejecucion", SqlDbType.Int).Value = lado.EjecucionProduccionID;
                    evento.Parameters.Add("@Anterior", SqlDbType.NVarChar, 40).Value = ProduccionEstatus.Nombre(lado.EstatusEjecucionID);
                    evento.Parameters.Add("@Nota", SqlDbType.NVarChar, 800).Value = nota;
                    evento.Parameters.Add("@Programa", SqlDbType.BigInt).Value = lado.ProgramaProduccionID;
                    evento.Parameters.Add("@Usuario", SqlDbType.Int).Value = usuarioId;
                    evento.Parameters.Add("@Corte", SqlDbType.DateTime2).Value = fechaCorte;
                    await evento.ExecuteNonQueryAsync();
                }
                var saldos = new List<object>();
                foreach (var lado in lados.OrderBy(x => x.ProgramaProduccionID))
                {
                    var saldo = await DevolverSaldoReleaseInterrupcionAsync(lado.ProgramaProduccionID, usuarioId, fechaCorte, cn, tx);
                    saldos.Add(new { lado.OFTexto, saldo });
                }
                await tx.CommitAsync();
                return Json(new
                {
                    ok = true, fechaCorte, saldos,
                    mensaje = "Producción interrumpida. Calendario cortado y máquina liberada. Se conservaron los registros de " +
                        $"{resultados.Sum(x => x.CantidadOK):N0} piezas OK, {resultados.Sum(x => x.CantidadSospechosa):N0} sospechosas y {resultados.Sum(x => x.CantidadScrap):N0} scrap. " +
                        "El saldo pendiente de los releases vinculados ya puede volver a programarse."
                });
            }
            catch (Exception ex)
            {
                try { await tx.RollbackAsync(); } catch { }
                return BadRequest(new { ok = false, mensaje = "No fue posible interrumpir producción: " + ex.Message });
            }
        }

        private static async Task<bool> EsUsuarioPlaneacionInterrupcionAsync(
            int usuarioId, SqlConnection cn, SqlTransaction tx)
        {
            // Resolver el rol activo por nombre, sin depender de un RolID específico.
            await using var cmd = new SqlCommand(@"
SELECT CAST(CASE WHEN EXISTS
(
    SELECT 1 FROM dbo.Usuarios u
    INNER JOIN dbo.Roles r ON r.RolID=u.RolID AND r.Activo=1
    WHERE u.UsuarioID=@Usuario AND u.Activo=1
      AND UPPER(LTRIM(RTRIM(r.NombreRol))) COLLATE Latin1_General_CI_AI=N'PLANEACION'
) THEN 1 ELSE 0 END AS bit);", cn, tx);
            cmd.Parameters.Add("@Usuario", SqlDbType.Int).Value = usuarioId;
            return Convert.ToBoolean(await cmd.ExecuteScalarAsync());
        }

        private static async Task<int?> DevolverSaldoReleaseInterrupcionAsync(
            int programaId, int usuarioId, DateTime corte, SqlConnection cn, SqlTransaction tx)
        {
            // Recalcular desde los programas conservados, sin incrementar el saldo ni descontarlo dos veces.
            // Otras OF siguen cubriendo su cantidad programada; las interrumpidas cubren sólo sus piezas OK.
            const string sql = @"
DECLARE @ReleaseDetalleID INT;
SELECT @ReleaseDetalleID=ReleaseDetalleID FROM dbo.Planeacion_ProgramaProduccion WITH(UPDLOCK,HOLDLOCK)
WHERE ProgramaProduccionID=@Programa AND Activo=1;
IF @ReleaseDetalleID IS NULL BEGIN SELECT CAST(NULL AS INT); RETURN; END;
IF NOT EXISTS(SELECT 1 FROM dbo.Planeacion_ReleaseDetalle WITH(UPDLOCK,HOLDLOCK) WHERE ReleaseDetalleID=@ReleaseDetalleID AND Activo=1)
    THROW 51520,'El release vinculado no está activo. No se modificó la producción.',1;

DECLARE @Cubierto INT;
SELECT @Cubierto=ISNULL(SUM(CASE WHEN interrumpida.ReferenciaID IS NOT NULL THEN ISNULL(pp.CantidadProducida,0)+ISNULL(pp.PiezasDesdePT,0) ELSE ISNULL(pp.CantidadProgramada,0) END),0)
FROM dbo.Planeacion_ProgramaProduccion pp WITH(UPDLOCK,HOLDLOCK)
LEFT JOIN
(
    SELECT DISTINCT ReferenciaID FROM dbo.Produccion_Eventos
    WHERE TipoEvento=N'INTERRUPCION_OPERATIVA' AND ReferenciaTabla=N'Planeacion_ProgramaProduccion'
) interrumpida ON interrumpida.ReferenciaID=pp.ProgramaProduccionID
WHERE pp.ReleaseDetalleID=@ReleaseDetalleID AND pp.Activo=1 AND ISNULL(pp.EstatusID,1)<>99;

DECLARE @Pendiente INT, @ProgramaVigente INT, @SolicitudVigente INT;
SELECT @Pendiente=CASE WHEN CantidadRequerida>@Cubierto THEN CantidadRequerida-@Cubierto ELSE 0 END
FROM dbo.Planeacion_ReleaseDetalle WITH(UPDLOCK,HOLDLOCK) WHERE ReleaseDetalleID=@ReleaseDetalleID AND Activo=1;
SELECT TOP(1) @ProgramaVigente=pp.ProgramaProduccionID,@SolicitudVigente=pp.SolicitudProduccionID
FROM dbo.Planeacion_ProgramaProduccion pp
WHERE pp.ReleaseDetalleID=@ReleaseDetalleID AND pp.Activo=1 AND ISNULL(pp.EstatusID,1) NOT IN(9,99)
  AND NOT EXISTS(SELECT 1 FROM dbo.Produccion_Eventos ev WHERE ev.TipoEvento=N'INTERRUPCION_OPERATIVA'
    AND ev.ReferenciaTabla=N'Planeacion_ProgramaProduccion' AND ev.ReferenciaID=pp.ProgramaProduccionID)
ORDER BY pp.ProgramaProduccionID DESC;

UPDATE d
SET ProduccionProgramadaPendiente=@Cubierto,
    PiezasAProducir=@Pendiente,
    MPRequeridaKg=CASE WHEN COALESCE(d.PesoBrutoPieza,dt.PesoBrutoPieza,0)>0 THEN ROUND(@Pendiente*COALESCE(d.PesoBrutoPieza,dt.PesoBrutoPieza),4) ELSE 0 END,
    EmbalajeRequerido=CASE WHEN COALESCE(d.PiezasPorEmbalaje,dt.PiezasPorEmbalaje,0)>0 THEN CEILING(@Pendiente/COALESCE(NULLIF(d.PiezasPorEmbalaje,0),NULLIF(dt.PiezasPorEmbalaje,0))) ELSE 0 END,
    HorasNecesarias=CASE WHEN ISNULL(d.ObjetivoHora,0)>0 THEN CONVERT(decimal(18,2),@Pendiente*1.0/d.ObjetivoHora) ELSE NULL END,
    ProgramaProduccionID=@ProgramaVigente,
    SolicitudProduccionID=@SolicitudVigente,
    FechaProgramado=CASE WHEN @ProgramaVigente IS NULL THEN NULL ELSE FechaProgramado END,
    UsuarioProgramoID=CASE WHEN @ProgramaVigente IS NULL THEN NULL ELSE UsuarioProgramoID END,
    EstatusID=CASE WHEN @ProgramaVigente IS NULL THEN @Calculado ELSE d.EstatusID END,
    UsuarioModificacionID=@Usuario,FechaModificacion=@Corte
FROM dbo.Planeacion_ReleaseDetalle d
LEFT JOIN dbo.ERP_ParteDatosTecnicos dt ON dt.ParteID=d.ParteID AND dt.Activo=1
WHERE d.ReleaseDetalleID=@ReleaseDetalleID AND d.Activo=1;
SELECT @Pendiente;";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@Programa", SqlDbType.Int).Value = programaId;
            cmd.Parameters.Add("@Usuario", SqlDbType.Int).Value = usuarioId;
            cmd.Parameters.Add("@Corte", SqlDbType.DateTime2).Value = corte;
            cmd.Parameters.Add("@Calculado", SqlDbType.Int).Value = PlaneacionReleaseEstatus.Calculado;
            var valor = await cmd.ExecuteScalarAsync();
            return valor == null || valor == DBNull.Value ? null : Convert.ToInt32(valor);
        }
    }
}
