using Microsoft.Data.SqlClient;
using System.Data;
using System.Text.RegularExpressions;

namespace ERP.NSQuell.Controllers
{
    public partial class CalidadController
    {
        // NSQ_LHRH_INICIO_UNICO_V1
        // Comparte solo el checklist fisico de liberacion de maquina.
        // HCC, HIP, ayuda visual, matriz y la autorizacion final siguen por inspeccion/OF.
        private async Task<int?> SincronizarChecklistLhRhAuditorV1Async(
            int checklistOrigenId,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sqlOrigen = @"
SELECT TOP(1)
    c.ChecklistArranqueID,
    c.ProgramaProduccionID,
    c.CodigoFormato,
    ISNULL(c.VersionFormato,N'') AS VersionFormato,
    ISNULL(c.TipoChecklist,N'') AS TipoChecklist,
    ISNULL(c.NumeroAplicacion,1) AS NumeroAplicacion,
    pp.Observaciones
FROM dbo.Produccion_ChecklistArranque c WITH(UPDLOCK,HOLDLOCK)
INNER JOIN dbo.Planeacion_ProgramaProduccion pp WITH(UPDLOCK,HOLDLOCK)
    ON pp.ProgramaProduccionID=c.ProgramaProduccionID
   AND pp.Activo=1
WHERE c.ChecklistArranqueID=@ChecklistArranqueID
  AND c.Activo=1;";

            int programaOrigenId;
            string codigoFormato;
            string versionFormato;
            string tipoChecklist;
            int numeroAplicacion;
            string observacionesPrograma;

            await using (var cmd = new SqlCommand(sqlOrigen, cn, tx))
            {
                cmd.Parameters.Add("@ChecklistArranqueID", SqlDbType.Int).Value = checklistOrigenId;
                await using var rd = await cmd.ExecuteReaderAsync();
                if (!await rd.ReadAsync()) return null;

                programaOrigenId = Convert.ToInt32(rd["ProgramaProduccionID"]);
                codigoFormato = rd["CodigoFormato"]?.ToString()?.Trim() ?? string.Empty;
                versionFormato = rd["VersionFormato"]?.ToString()?.Trim() ?? string.Empty;
                tipoChecklist = rd["TipoChecklist"]?.ToString()?.Trim() ?? string.Empty;
                numeroAplicacion = Convert.ToInt32(rd["NumeroAplicacion"]);
                observacionesPrograma = rd["Observaciones"] == DBNull.Value ? string.Empty : rd["Observaciones"]?.ToString() ?? string.Empty;
            }

            // Calidad comparte solo el checklist de ARRANQUE_LIBERACION.
            if (!string.Equals(tipoChecklist, "ARRANQUE_LIBERACION", StringComparison.OrdinalIgnoreCase))
                return null;

            var match = Regex.Match(observacionesPrograma, @"NSQ_LHRH_PAIR:(\d+);", RegexOptions.IgnoreCase);
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out var grupoLhRh) || grupoLhRh <= 0)
                return null;

            const string sqlDestino = @"
SELECT TOP(1)
    c2.ChecklistArranqueID
FROM dbo.Planeacion_ProgramaProduccion pp2 WITH(UPDLOCK,HOLDLOCK)
INNER JOIN dbo.Produccion_Ejecucion e2 WITH(UPDLOCK,HOLDLOCK)
    ON e2.ProgramaProduccionID=pp2.ProgramaProduccionID
   AND e2.Activo=1
   AND e2.EstatusID NOT IN(9,99)
INNER JOIN dbo.Produccion_ChecklistArranque c2 WITH(UPDLOCK,HOLDLOCK)
    ON c2.EjecucionProduccionID=e2.EjecucionProduccionID
   AND c2.ProgramaProduccionID=pp2.ProgramaProduccionID
   AND c2.Activo=1
WHERE pp2.Activo=1
  AND pp2.ProgramaProduccionID<>@ProgramaOrigenID
  AND pp2.Observaciones LIKE N'%NSQ_LHRH_PAIR:'+CONVERT(NVARCHAR(20),@GrupoLhRh)+N';%'
  AND c2.CodigoFormato=@CodigoFormato
  AND ISNULL(c2.VersionFormato,N'')=@VersionFormato
  AND ISNULL(c2.TipoChecklist,N'')=@TipoChecklist
  AND ISNULL(c2.NumeroAplicacion,1)=@NumeroAplicacion
ORDER BY c2.ChecklistArranqueID DESC;";

            int? checklistDestinoId = null;
            await using (var cmd = new SqlCommand(sqlDestino, cn, tx))
            {
                cmd.Parameters.Add("@ProgramaOrigenID", SqlDbType.Int).Value = programaOrigenId;
                cmd.Parameters.Add("@GrupoLhRh", SqlDbType.Int).Value = grupoLhRh;
                cmd.Parameters.Add("@CodigoFormato", SqlDbType.NVarChar, 50).Value = codigoFormato;
                cmd.Parameters.Add("@VersionFormato", SqlDbType.NVarChar, 30).Value = versionFormato;
                cmd.Parameters.Add("@TipoChecklist", SqlDbType.NVarChar, 50).Value = tipoChecklist;
                cmd.Parameters.Add("@NumeroAplicacion", SqlDbType.Int).Value = numeroAplicacion;
                var result = await cmd.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value)
                    checklistDestinoId = Convert.ToInt32(result);
            }

            // Registros antiguos pueden no tener aun ejecucion/checklist espejo.
            // No se bloquea Calidad por datos historicos previos al parche.
            if (!checklistDestinoId.HasValue || checklistDestinoId.Value <= 0)
                return null;

            const string sqlSincronizar = @"
INSERT INTO dbo.Produccion_ChecklistArranqueDetalle
(
    ChecklistArranqueID,
    PreguntaID,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
SELECT
    @ChecklistDestinoID,
    p.PreguntaID,
    @UsuarioID,
    GETDATE(),
    1
FROM dbo.ERP_ChecklistArranquePreguntas p
WHERE p.CodigoFormato=@CodigoFormato
  AND ISNULL(p.VersionFormato,N'')=@VersionFormato
  AND p.Activo=1
  AND
  (
        ISNULL(p.EsPreguntaCalidad,0)=1
     OR UPPER(ISNULL(p.Seccion,N'')) LIKE N'%CALIDAD%'
     OR UPPER(ISNULL(p.Seccion,N'')) LIKE N'%AUDITOR%'
     OR UPPER(ISNULL(p.ResponsableSugerido,N'')) LIKE N'%CALIDAD%'
     OR UPPER(ISNULL(p.ResponsableSugerido,N'')) LIKE N'%AUDITOR%'
  )
  AND UPPER(ISNULL(p.Seccion,N'')) NOT LIKE N'%PARO%'
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.Produccion_ChecklistArranqueDetalle d WITH(UPDLOCK,HOLDLOCK)
      WHERE d.ChecklistArranqueID=@ChecklistDestinoID
        AND d.PreguntaID=p.PreguntaID
        AND d.Activo=1
  );

UPDATE destino
SET
    destino.Resultado=origen.Resultado,
    destino.Observaciones=origen.Observaciones,
    destino.Confirmado=origen.Confirmado,
    destino.ValorCapturado=origen.ValorCapturado,
    destino.Unidad=origen.Unidad,
    destino.Especificacion=origen.Especificacion,
    destino.Tolerancia=origen.Tolerancia,
    destino.UsuarioRespuestaID=origen.UsuarioRespuestaID,
    destino.FechaRespuesta=origen.FechaRespuesta,
    destino.UsuarioModificacionID=@UsuarioID,
    destino.FechaModificacion=GETDATE()
FROM dbo.Produccion_ChecklistArranqueDetalle destino
INNER JOIN dbo.Produccion_ChecklistArranqueDetalle origen
    ON origen.ChecklistArranqueID=@ChecklistOrigenID
   AND origen.PreguntaID=destino.PreguntaID
   AND origen.Activo=1
INNER JOIN dbo.ERP_ChecklistArranquePreguntas p
    ON p.PreguntaID=destino.PreguntaID
   AND p.Activo=1
WHERE destino.ChecklistArranqueID=@ChecklistDestinoID
  AND destino.Activo=1
  AND
  (
        ISNULL(p.EsPreguntaCalidad,0)=1
     OR UPPER(ISNULL(p.Seccion,N'')) LIKE N'%CALIDAD%'
     OR UPPER(ISNULL(p.Seccion,N'')) LIKE N'%AUDITOR%'
     OR UPPER(ISNULL(p.ResponsableSugerido,N'')) LIKE N'%CALIDAD%'
     OR UPPER(ISNULL(p.ResponsableSugerido,N'')) LIKE N'%AUDITOR%'
  )
  AND UPPER(ISNULL(p.Seccion,N'')) NOT LIKE N'%PARO%';

UPDATE destino
SET
    destino.UsuarioCalidadID=origen.UsuarioCalidadID,
    destino.FechaValidacionCalidad=origen.FechaValidacionCalidad,
    destino.ObservacionesCalidad=origen.ObservacionesCalidad,
    destino.UsuarioModificacionID=@UsuarioID,
    destino.FechaModificacion=GETDATE()
FROM dbo.Produccion_ChecklistArranque destino
INNER JOIN dbo.Produccion_ChecklistArranque origen
    ON origen.ChecklistArranqueID=@ChecklistOrigenID
   AND origen.Activo=1
WHERE destino.ChecklistArranqueID=@ChecklistDestinoID
  AND destino.Activo=1;";

            await using (var cmd = new SqlCommand(sqlSincronizar, cn, tx))
            {
                cmd.Parameters.Add("@ChecklistOrigenID", SqlDbType.Int).Value = checklistOrigenId;
                cmd.Parameters.Add("@ChecklistDestinoID", SqlDbType.Int).Value = checklistDestinoId.Value;
                cmd.Parameters.Add("@CodigoFormato", SqlDbType.NVarChar, 50).Value = codigoFormato;
                cmd.Parameters.Add("@VersionFormato", SqlDbType.NVarChar, 30).Value = versionFormato;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                await cmd.ExecuteNonQueryAsync();
            }

            return checklistDestinoId.Value;
        }
    }
}