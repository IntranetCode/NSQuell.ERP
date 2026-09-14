using ERP.NSQuell.Models;
using ERP.NSQuell.Servicios.Produccion;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System;
using System.Data;
using System.Threading.Tasks;

namespace ERP.NSQuell.Controllers
{
    public partial class CalidadController
    {
        // NSQ_FIN_ANTICIPADO_V1
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> FinalizarProduccionDesdeCalidad(
            int id,
            string? motivo)
        {
            var usuarioId = ObtenerUsuarioIdActual();

            if (!usuarioId.HasValue || usuarioId.Value <= 0)
                return Unauthorized();

            if (id <= 0)
                return NotFound();

            motivo = motivo?.Trim();

            if (string.IsNullOrWhiteSpace(motivo) || motivo.Length < 10)
            {
                TempData["Error"] =
                    "Para indicar que la producción no continuará debes registrar una justificación de al menos 10 caracteres.";

                return Redirect($"/Calidad/Detalle/{id}#liberacion");
            }

            if (motivo.Length > 500)
                motivo = motivo[..500];

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var tx =
                (SqlTransaction)await cn.BeginTransactionAsync(
                    IsolationLevel.Serializable);

            try
            {
                const string sqlPermiso = @"
SELECT CAST(
    CASE
        WHEN u.RolID=1 THEN 1
        WHEN EXISTS
        (
            SELECT 1
            FROM dbo.fn_PermisosEfectivosUsuario(@UsuarioID) pe
            INNER JOIN dbo.SubMenus sm
                ON sm.SubMenuID=pe.SubMenuID
            WHERE pe.TienePermiso=1
              AND ISNULL(sm.Activo,1)=1
              AND
              (
                  UPPER(ISNULL(sm.Nombre,N'')) COLLATE Latin1_General_CI_AI LIKE N'%CALIDAD%'
                  OR UPPER(ISNULL(sm.UrlEnlace,N'')) LIKE N'%/CALIDAD%'
              )
        ) THEN 1
        ELSE 0
    END
AS BIT)
FROM dbo.Usuarios u
WHERE u.UsuarioID=@UsuarioID
  AND u.Activo=1;";

                await using (var cmdPermiso =
                    new SqlCommand(sqlPermiso, cn, tx))
                {
                    cmdPermiso.Parameters.Add(
                        "@UsuarioID",
                        SqlDbType.Int).Value = usuarioId.Value;

                    var valorPermiso =
                        await cmdPermiso.ExecuteScalarAsync();

                    var autorizado =
                        valorPermiso != null &&
                        valorPermiso != DBNull.Value &&
                        Convert.ToBoolean(valorPermiso);

                    if (!autorizado)
                    {
                        await tx.RollbackAsync();
                        return StatusCode(403);
                    }
                }

                const string sqlEjecucion = @"
SELECT TOP(1)
    EjecucionProduccionID
FROM dbo.Calidad_Inspecciones WITH(UPDLOCK,HOLDLOCK)
WHERE InspeccionID=@InspeccionID
  AND ISNULL(Estado,N'')<>@Cerrada;";

                int ejecucionProduccionId;

                await using (var cmd = new SqlCommand(sqlEjecucion, cn, tx))
                {
                    cmd.Parameters.Add(
                        "@InspeccionID",
                        SqlDbType.Int).Value = id;

                    cmd.Parameters.Add(
                        "@Cerrada",
                        SqlDbType.NVarChar,
                        50).Value = CalidadEstados.Cerrada;

                    var valor = await cmd.ExecuteScalarAsync();

                    if (valor == null || valor == DBNull.Value)
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] =
                            "La inspección ya no tiene una ejecución activa que pueda finalizarse.";
                        return Redirect($"/Calidad/Detalle/{id}#liberacion");
                    }

                    ejecucionProduccionId = Convert.ToInt32(valor);
                }

                var resultado =
                    await TerminacionAnticipadaProduccionService.EjecutarAsync(
                        ejecucionProduccionId,
                        motivo,
                        usuarioId.Value,
                        cn,
                        tx);

                await tx.CommitAsync();

                TempData["Mensaje"] =
                    $"Producción finalizada anticipadamente desde Calidad. " +
                    $"OK {resultado.CantidadOK:N0}, " +
                    $"sospechoso {resultado.CantidadSospechosa:N0}, " +
                    $"scrap {resultado.CantidadScrap:N0}. " +
                    $"La reliberación quedó cancelada porque ya no habrá reinicio de serie.";

                return Redirect($"/Calidad/Detalle/{id}#cajas");
            }
            catch (Exception ex)
            {
                try { await tx.RollbackAsync(); } catch { }

                TempData["Error"] =
                    "No fue posible finalizar la producción desde Calidad: " +
                    ex.Message;

                return Redirect($"/Calidad/Detalle/{id}#liberacion");
            }
        }

        // NSQ_MONITOREO_CANONICO_V1
        // Ajusta únicamente monitoreos PENDIENTES sin RegistroHora.
        // Los monitoreos ya atendidos o vinculados conservan toda su trazabilidad.
        private async Task NormalizarMonitoreosCanonicosAsync(
            int inspeccionId,
            int? usuarioId)
        {
            if (inspeccionId <= 0)
                return;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            const string sql = @"
SET NOCOUNT ON;

DECLARE @ProgramaProduccionID INT;
DECLARE @EjecucionProduccionID INT;
DECLARE @CantidadPlaneada INT = 0;
DECLARE @ParteID INT;
DECLARE @HorasProgramadas DECIMAL(18,4);
DECLARE @ObjetivoHora INT;
DECLARE @Horas INT = 1;

SELECT TOP(1)
    @ProgramaProduccionID=i.ProgramaProduccionID,
    @EjecucionProduccionID=i.EjecucionProduccionID
FROM dbo.Calidad_Inspecciones i
WHERE i.InspeccionID=@InspeccionID;

IF @ProgramaProduccionID IS NULL OR @EjecucionProduccionID IS NULL
    RETURN;

SELECT TOP(1)
    @CantidadPlaneada=ISNULL(e.CantidadPlaneada,0),
    @ParteID=e.ParteID
FROM dbo.Produccion_Ejecucion e
WHERE e.EjecucionProduccionID=@EjecucionProduccionID
  AND e.Activo=1;

SELECT TOP(1)
    @HorasProgramadas=pp.HorasProgramadas
FROM dbo.Planeacion_ProgramaProduccion pp
WHERE pp.ProgramaProduccionID=@ProgramaProduccionID
  AND pp.Activo=1;

SELECT TOP(1)
    @ObjetivoHora=dt.ObjetivoHora
FROM dbo.ERP_ParteDatosTecnicos dt
WHERE dt.ParteID=@ParteID
  AND dt.Activo=1
ORDER BY dt.ParteDatoTecnicoID DESC;

IF @HorasProgramadas IS NOT NULL AND @HorasProgramadas>0
BEGIN
    SET @Horas=CONVERT(INT,CEILING(@HorasProgramadas));
END
ELSE IF @CantidadPlaneada>0 AND ISNULL(@ObjetivoHora,0)>0
BEGIN
    SET @Horas=CONVERT(
        INT,
        CEILING(
            CONVERT(DECIMAL(18,4),@CantidadPlaneada) /
            NULLIF(@ObjetivoHora,0)
        )
    );
END;

IF @Horas<1 SET @Horas=1;

/* V9 del operador usa horas normales numeradas desde 1.
   Una fila NumeroHora<=0 es residuo de cambio de molde / prearranque y
   no forma parte de HorasProgramadas. Sólo se retira si sigue pendiente
   y nunca fue vinculada con una captura de Producción. */
UPDATE dbo.Calidad_MonitoreosProceso
SET
    Activo=0,
    Observaciones=RIGHT(CONCAT(
        ISNULL(Observaciones,N''),
        CASE WHEN NULLIF(LTRIM(RTRIM(ISNULL(Observaciones,N''))),N'') IS NULL
             THEN N'' ELSE CHAR(13)+CHAR(10) END,
        N'Desactivado automáticamente: corresponde a cambio de molde/prearranque y no a una hora programada normal de Producción.'
    ),1000),
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=SYSDATETIME()
WHERE InspeccionID=@InspeccionID
  AND Activo=1
  AND NumeroHora<=0
  AND UPPER(LTRIM(RTRIM(ISNULL(Resultado,N'PENDIENTE'))))=N'PENDIENTE'
  AND RegistroHoraID IS NULL;

/* Deben quedar como máximo @Horas revisiones activas del plan normal.
   Sólo se desactivan excedentes que todavía no tienen captura ni revisión. */
;WITH Ordenados AS
(
    SELECT
        m.MonitoreoID,
        ROW_NUMBER() OVER
        (
            ORDER BY
                m.FechaHoraProgramada,
                m.NumeroHora,
                m.MonitoreoID
        ) AS rn
    FROM dbo.Calidad_MonitoreosProceso m
    WHERE m.InspeccionID=@InspeccionID
      AND m.Activo=1
)
UPDATE m
SET
    Activo=0,
    Observaciones=RIGHT(CONCAT(
        ISNULL(m.Observaciones,N''),
        CASE WHEN NULLIF(LTRIM(RTRIM(ISNULL(m.Observaciones,N''))),N'') IS NULL
             THEN N'' ELSE CHAR(13)+CHAR(10) END,
        N'Desactivado automáticamente: excede las ',@Horas,
        N' hora(s) programadas normales de Producción.'
    ),1000),
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=SYSDATETIME()
FROM dbo.Calidad_MonitoreosProceso m
INNER JOIN Ordenados o
    ON o.MonitoreoID=m.MonitoreoID
WHERE o.rn>@Horas
  AND m.Activo=1
  AND UPPER(LTRIM(RTRIM(ISNULL(m.Resultado,N'PENDIENTE'))))=N'PENDIENTE'
  AND m.RegistroHoraID IS NULL;";

            await using var cmd = new SqlCommand(sql, cn);

            cmd.Parameters.Add(
                "@InspeccionID",
                SqlDbType.Int).Value = inspeccionId;

            cmd.Parameters.Add(
                "@UsuarioID",
                SqlDbType.Int).Value =
                (object?)usuarioId ?? DBNull.Value;

            await cmd.ExecuteNonQueryAsync();
        }
    }
}
