using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers;

public sealed partial class ProduccionController
{
    private static async Task<(int? Id,string? Nombre,int AsignacionID)?> ObtenerOperadorProgramadoTurnoV7Async(
        int programaId, DateTime momento, SqlConnection cn, SqlTransaction? tx)
    {
        const string existe="SELECT CONVERT(bit,CASE WHEN OBJECT_ID(N'dbo.Produccion_ProgramaPersonalAsignaciones',N'U') IS NULL THEN 0 ELSE 1 END);";
        await using(var ecmd=tx==null?new SqlCommand(existe,cn):new SqlCommand(existe,cn,tx))
            if(!Convert.ToBoolean(await ecmd.ExecuteScalarAsync()??false)) return null;

        const string sql=@"
SELECT TOP(1) a.AsignacionPersonalID,a.OperadorID,
 LTRIM(RTRIM(CONCAT(ISNULL(p.Nombre,N''),N' ',ISNULL(p.ApellidoPaterno,N''),N' ',ISNULL(p.ApellidoMaterno,N'')))) Nombre
FROM dbo.Produccion_ProgramaPersonalAsignaciones a
LEFT JOIN dbo.Persona p ON p.PersonaID=a.OperadorID
WHERE a.ProgramaProduccionID=@Programa AND a.Activo=1
  AND @Momento>=a.Inicio AND @Momento<a.Fin
ORDER BY a.AsignacionPersonalID DESC;";
        await using var cmd=tx==null?new SqlCommand(sql,cn):new SqlCommand(sql,cn,tx);
        cmd.Parameters.Add("@Programa",SqlDbType.Int).Value=programaId;
        cmd.Parameters.Add("@Momento",SqlDbType.DateTime2).Value=momento;
        await using var rd=await cmd.ExecuteReaderAsync();
        if(!await rd.ReadAsync())return null;
        return(rd["OperadorID"]==DBNull.Value?null:Convert.ToInt32(rd["OperadorID"]),rd["Nombre"]==DBNull.Value?null:rd["Nombre"]?.ToString()?.Trim(),Convert.ToInt32(rd["AsignacionPersonalID"]));
    }
}
