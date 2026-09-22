using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers;

// NSQ_PRODUCCION_DISTRIBUCION_OPERADOR_V13
public sealed partial class ProduccionController
{
    private static async Task<(int Id,string Nombre)?>
        ObtenerOperadorDistribucionV13Async(
            int programaProduccionId,
            DateTime momento,
            DateTime? alterno,
            SqlConnection cn,
            SqlTransaction? tx)
    {
        const string existeSql = @"
SELECT CONVERT(bit,CASE WHEN
    OBJECT_ID(N'dbo.Produccion_DistribucionOperadores',N'U') IS NOT NULL
THEN 1 ELSE 0 END);";

        await using (var existe = tx == null
            ? new SqlCommand(existeSql,cn)
            : new SqlCommand(existeSql,cn,tx))
        {
            if (!Convert.ToBoolean(await existe.ExecuteScalarAsync() ?? false))
                return null;
        }

        const string programaSql = @"
SELECT TOP(1)
    MaquinaID,
    ParteID
FROM dbo.Planeacion_ProgramaProduccion
WHERE ProgramaProduccionID=@ProgramaID
  AND Activo=1;";

        int? maquinaId = null;
        int? parteId = null;

        await using (var programaCmd = tx == null
            ? new SqlCommand(programaSql,cn)
            : new SqlCommand(programaSql,cn,tx))
        {
            programaCmd.Parameters.Add("@ProgramaID",SqlDbType.Int).Value = programaProduccionId;

            await using var rd = await programaCmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
                return null;

            maquinaId = rd["MaquinaID"] == DBNull.Value
                ? null
                : Convert.ToInt32(rd["MaquinaID"]);

            parteId = rd["ParteID"] == DBNull.Value
                ? null
                : Convert.ToInt32(rd["ParteID"]);
        }

        if (!maquinaId.HasValue)
            return null;

        const string sql = @"
SELECT TOP(1)
    d.OperadorID,
    LTRIM(RTRIM(CONCAT(
        ISNULL(p.Nombre,N''),N' ',
        ISNULL(p.ApellidoPaterno,N''),N' ',
        ISNULL(p.ApellidoMaterno,N'')))) AS OperadorNombre
FROM dbo.Produccion_DistribucionOperadores d
INNER JOIN dbo.Persona p
    ON p.PersonaID=d.OperadorID
WHERE d.Activo=1
  AND d.MaquinaID=@MaquinaID
  AND d.OperadorID IS NOT NULL
  AND
  (
      (@Momento>=d.Inicio AND @Momento<d.Fin)
      OR
      (@Alterno IS NOT NULL AND @Alterno>=d.Inicio AND @Alterno<d.Fin)
  )
ORDER BY
    CASE
        WHEN d.ProgramaProduccionID=@ProgramaID THEN 0
        WHEN d.ParteID=@ParteID THEN 1
        ELSE 2
    END,
    CASE
        WHEN @Momento>=d.Inicio AND @Momento<d.Fin THEN 0
        ELSE 1
    END,
    d.DistribucionID DESC;";

        await using var cmd = tx == null
            ? new SqlCommand(sql,cn)
            : new SqlCommand(sql,cn,tx);

        cmd.Parameters.Add("@ProgramaID",SqlDbType.Int).Value = programaProduccionId;
        cmd.Parameters.Add("@MaquinaID",SqlDbType.Int).Value = maquinaId.Value;
        cmd.Parameters.Add("@ParteID",SqlDbType.Int).Value =
            parteId.HasValue ? parteId.Value : DBNull.Value;
        cmd.Parameters.Add("@Momento",SqlDbType.DateTime2).Value = momento;
        cmd.Parameters.Add("@Alterno",SqlDbType.DateTime2).Value =
            alterno.HasValue ? alterno.Value : DBNull.Value;

        await using var reader = await cmd.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return null;

        return
        (
            Convert.ToInt32(reader["OperadorID"]),
            reader["OperadorNombre"]?.ToString()?.Trim() ?? string.Empty
        );
    }
}
