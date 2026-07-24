using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers;

public partial class PlaneacionReleaseController
{
    private async Task<int> AutocompletarFechasCargaImportadasAsync()
    {
        const string sql = @"
UPDATE detalle
SET detalle.FechaCarga =
    DATEADD(DAY, -1, CONVERT(DATE, detalle.FechaRequerida))
FROM dbo.Planeacion_ReleaseDetalle detalle
INNER JOIN dbo.Planeacion_Releases release
    ON release.ReleaseID = detalle.ReleaseID
WHERE release.Activo = 1
  AND detalle.Activo = 1
  AND ISNULL(release.ImportadoDesdeArchivo, 0) = 1
  AND detalle.FechaCarga IS NULL
  AND detalle.FechaRequerida IS NOT NULL;

SELECT @@ROWCOUNT;";

        await using var cn =
            new SqlConnection(ConnectionString);

        await cn.OpenAsync();

        await using var cmd =
            new SqlCommand(sql, cn);

        return Convert.ToInt32(
            await cmd.ExecuteScalarAsync());
    }
}