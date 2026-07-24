using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers;

public partial class PlaneacionReleaseController
{
    private async Task<int> AutocompletarFechasCargaImportadasAsync()
    {
        const string sql = @"
SET NOCOUNT ON;

UPDATE d
SET d.FechaCarga =
    DATEADD(DAY, -1, CONVERT(DATE, d.FechaRequerida))
FROM dbo.Planeacion_ReleaseDetalle d
INNER JOIN dbo.Planeacion_Releases r
    ON r.ReleaseID = d.ReleaseID
WHERE r.Activo = 1
  AND d.Activo = 1
  AND ISNULL(r.ImportadoDesdeArchivo, 0) = 1
  AND d.FechaCarga IS NULL
  AND d.FechaRequerida IS NOT NULL;
=======
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
>>>>>>> origin/Rama_Adrian

SELECT @@ROWCOUNT;";

        await using var cn =
            new SqlConnection(ConnectionString);

        await cn.OpenAsync();

        await using var cmd =
            new SqlCommand(sql, cn);

        return Convert.ToInt32(
            await cmd.ExecuteScalarAsync());
    }
<<<<<<< HEAD
}
=======
}
>>>>>>> origin/Rama_Adrian
