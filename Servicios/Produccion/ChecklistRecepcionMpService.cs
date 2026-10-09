using ERP.NSQuell.Models;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Servicios.Produccion;

public static class ChecklistRecepcionMpService
{
    public sealed record Evidencia(int PreguntaID, int? DetalleID, string Texto);
    // Validar todas las OF enlazadas; una recepción parcial aceptada conserva la regla vigente.
    private const string Consulta = """
WITH Programas AS
(
 SELECT c.ChecklistArranqueID,c.ProgramaProduccionID FROM dbo.Produccion_ChecklistArranque c
 WHERE c.ChecklistArranqueID=@Checklist AND c.Activo=1 AND c.ProgramaProduccionID IS NOT NULL
 UNION
 SELECT v.ChecklistArranqueID,v.ProgramaProduccionID FROM dbo.Produccion_ChecklistProgramas v
 WHERE v.ChecklistArranqueID=@Checklist AND v.Activo=1
), Contextos AS
(
 SELECT pp.ProgramaProduccionID,pp.SolicitudProduccionID,pp.SolicitudProduccionDetalleID,pp.MaterialID,
   COALESCE(pp.CantidadMpKg,sd.CantidadMpKg,0) AS Requerida,
   ISNULL(sd.HorasSecado,0) AS HorasSecado
 FROM Programas p INNER JOIN dbo.Planeacion_ProgramaProduccion pp ON pp.ProgramaProduccionID=p.ProgramaProduccionID AND pp.Activo=1
 LEFT JOIN dbo.SolicitudesProduccionDetalle sd ON sd.SolicitudProduccionDetalleID=pp.SolicitudProduccionDetalleID AND sd.Activo=1
)
SELECT mapa.PreguntaID,detalle.ChecklistArranqueDetalleID
FROM dbo.NSQ_ChecklistMPAutomatico mapa
INNER JOIN dbo.ERP_ChecklistArranquePreguntas pregunta ON pregunta.PreguntaID=mapa.PreguntaID AND pregunta.Activo=1
INNER JOIN dbo.Produccion_ChecklistArranque c ON c.ChecklistArranqueID=@Checklist AND c.Activo=1
LEFT JOIN dbo.Produccion_ChecklistArranqueDetalle detalle ON detalle.ChecklistArranqueID=c.ChecklistArranqueID AND detalle.PreguntaID=mapa.PreguntaID AND detalle.Activo=1
WHERE mapa.Activo=1 AND pregunta.TipoChecklist=c.TipoChecklist AND pregunta.EsPreguntaCalidad=0
 AND pregunta.TextoPregunta=mapa.TextoPreguntaSnapshot
 AND EXISTS(SELECT 1 FROM Contextos ctx WHERE ctx.Requerida>0.0005)
 AND NOT EXISTS
 (
  SELECT 1 FROM Contextos ctx WHERE ctx.Requerida>0.0005 AND NOT EXISTS
  (
   SELECT 1 FROM dbo.Produccion_RecepcionMateriales r WITH(HOLDLOCK)
   WHERE r.Activo=1 AND r.TipoOrigen=N'MP' AND r.SolicitudProduccionID=ctx.SolicitudProduccionID
    AND (r.SolicitudProduccionDetalleID IS NULL OR r.SolicitudProduccionDetalleID=ctx.SolicitudProduccionDetalleID)
    AND r.MaterialSolicitadoID=ctx.MaterialID
    AND r.EstadoRecepcion IN(N'RECIBIDO_COMPLETO',N'RECIBIDO_PARCIAL') AND r.CantidadRecibidaProduccion>0.0005
    AND r.FechaRecepcion IS NOT NULL
    AND (mapa.RequiereSecado=0 OR ctx.HorasSecado<=0 OR EXISTS
    (
     SELECT 1 FROM dbo.Produccion_SecadoMaterial sm WITH(HOLDLOCK) WHERE sm.RecepcionMaterialID=r.RecepcionMaterialID
       AND sm.Activo=1 AND (sm.MinutosSecadoRequeridos<=0 OR sm.CantidadFinalizadaKg>0.0005)
    ))
  )
 );
""";

    public static async Task<Dictionary<int,Evidencia>> ObtenerAsync(int checklistId, SqlConnection cn, SqlTransaction? tx=null)
    {
        var salida=new Dictionary<int,Evidencia>();
        await using var cmd=new SqlCommand(Consulta,cn,tx);
        cmd.Parameters.Add("@Checklist",SqlDbType.Int).Value=checklistId;
        await using var rd=await cmd.ExecuteReaderAsync();
        while(await rd.ReadAsync())
        {
            var id=Convert.ToInt32(rd["PreguntaID"]);
            salida[id]=new Evidencia(id,rd["ChecklistArranqueDetalleID"]==DBNull.Value?null:Convert.ToInt32(rd["ChecklistArranqueDetalleID"]),
                "OK automático: MP recibida y aceptada por Producción. Secado verificado cuando aplica.");
        }
        return salida;
    }

    public static async Task DecorarAsync(int checklistId,IEnumerable<ProduccionChecklistPreguntaVm> preguntas,SqlConnection cn,SqlTransaction? tx=null)
    {
        var automaticas=await ObtenerAsync(checklistId,cn,tx);
        foreach(var p in preguntas)
        {
            if(!automaticas.TryGetValue(p.PreguntaID,out var evidencia))continue;
            p.AutomaticaPorRecepcionMP=true; p.EvidenciaRecepcionMP=evidencia.Texto;
            p.Resultado="OK";p.Confirmado=true;
        }
    }

    public static async Task AplicarAsync(int checklistId,int usuarioId,SqlConnection cn,SqlTransaction tx)
    {
        var automaticas=await ObtenerAsync(checklistId,cn,tx);
        foreach(var evidencia in automaticas.Values)
        {
            await using var cmd=new SqlCommand("""
UPDATE dbo.Produccion_ChecklistArranqueDetalle WITH(UPDLOCK,HOLDLOCK)
SET Resultado=N'OK',Confirmado=1,Observaciones=@Evidencia,UsuarioRespuestaID=@Usuario,FechaRespuesta=GETDATE(),
 UsuarioModificacionID=@Usuario,FechaModificacion=GETDATE()
WHERE ChecklistArranqueID=@Checklist AND PreguntaID=@Pregunta AND Activo=1;
IF @@ROWCOUNT=0
 INSERT dbo.Produccion_ChecklistArranqueDetalle(ChecklistArranqueID,PreguntaID,Resultado,Observaciones,Confirmado,UsuarioRespuestaID,FechaRespuesta,Activo,UsuarioCreacionID,FechaCreacion)
 VALUES(@Checklist,@Pregunta,N'OK',@Evidencia,1,@Usuario,GETDATE(),1,@Usuario,GETDATE());
""",cn,tx);
            cmd.Parameters.Add("@Checklist",SqlDbType.Int).Value=checklistId;
            cmd.Parameters.Add("@Pregunta",SqlDbType.Int).Value=evidencia.PreguntaID;
            cmd.Parameters.Add("@Usuario",SqlDbType.Int).Value=usuarioId;
            cmd.Parameters.Add("@Evidencia",SqlDbType.NVarChar,500).Value=evidencia.Texto;
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
