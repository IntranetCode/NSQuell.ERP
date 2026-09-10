using Microsoft.Data.SqlClient;
using System.Data;
namespace ERP.NSQuell.Controllers;
public partial class PlaneacionProgramaController
{
    private static async Task SincronizarHorasCanonicasV9Async(int programaProduccionId, decimal horas, SqlConnection cn, SqlTransaction tx)
    {
        if(programaProduccionId<=0 || horas<=0) return;
        const string sql=@"
UPDATE dbo.Planeacion_ProgramaProduccion SET HorasProgramadas=@Horas WHERE ProgramaProduccionID=@ProgramaID AND Activo=1;
UPDATE sd SET sd.HorasPlaneadas=@Horas
FROM dbo.SolicitudesProduccionDetalle sd
INNER JOIN dbo.Planeacion_ProgramaProduccion pp ON pp.SolicitudProduccionDetalleID=sd.SolicitudProduccionDetalleID
WHERE pp.ProgramaProduccionID=@ProgramaID AND pp.Activo=1 AND sd.Activo=1;";
        await using var cmd=new SqlCommand(sql,cn,tx);cmd.Parameters.Add("@ProgramaID",SqlDbType.Int).Value=programaProduccionId;var p=cmd.Parameters.Add("@Horas",SqlDbType.Decimal);p.Precision=18;p.Scale=4;p.Value=horas;await cmd.ExecuteNonQueryAsync();
    }
}