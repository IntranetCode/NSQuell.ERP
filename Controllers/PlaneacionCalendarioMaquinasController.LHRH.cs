using Microsoft.Data.SqlClient;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;

namespace ERP.NSQuell.Controllers;

// NSQ_LHRH_CALENDARIO_V1
public sealed partial class PlaneacionCalendarioMaquinasController
{
    private async Task<int?> SincronizarParejaLhRhCalendarioAsync(
        ProgramaBase programa,
        MaquinaCompatible maquinaDestino,
        DateTime fechaCambio,
        DateTime fechaArranque,
        DateTime fechaFin,
        decimal horasProduccion,
        int secuencia,
        int usuarioId,
        SqlConnection cn,
        SqlTransaction tx)
    {
        var parejaId = await ObtenerProgramaParejaLhRhIdAsync(
            programa.ProgramaProduccionID,
            cn,
            tx);

        if (!parejaId.HasValue)
            return null;

        var bloqueo = await ObtenerMotivoBloqueoMovimientoAsync(
            parejaId.Value,
            cn,
            tx,
            bloquear: true);

        if (!string.IsNullOrWhiteSpace(bloqueo))
        {
            throw new InvalidOperationException(
                "La pareja LH/RH no puede moverse junto con este programa: " +
                bloqueo);
        }

        var pareja = await ObtenerProgramaBaseAsync(
            parejaId.Value,
            cn,
            tx,
            bloquear: true);

        if (pareja == null)
            return null;

        var compatiblesPareja = await ObtenerMaquinasCompatiblesAsync(
            pareja,
            cn,
            tx);

        if (!compatiblesPareja.Any(x =>
                x.MaquinaID == maquinaDestino.MaquinaID))
        {
            throw new InvalidOperationException(
                "La máquina destino no está configurada como compatible para la pareja LH/RH. " +
                "Corrige los datos técnicos de ambas partes antes de moverlas juntas.");
        }

        await ActualizarProgramaAsync(
            pareja,
            maquinaDestino,
            fechaCambio,
            fechaArranque,
            fechaFin,
            horasProduccion,
            secuencia + 1,
            usuarioId,
            cn,
            tx);

        await SincronizarDocumentosRelacionadosAsync(
            pareja,
            maquinaDestino,
            fechaCambio,
            fechaArranque,
            fechaFin,
            horasProduccion,
            cn,
            tx);

        await InsertarHistorialMovimientoAsync(
            pareja,
            maquinaDestino,
            fechaCambio,
            fechaArranque,
            fechaFin,
            horasProduccion,
            usuarioId,
            "Movimiento automático por pareja LH/RH. " +
            $"Programa origen #{programa.ProgramaProduccionID}.",
            cn,
            tx);

        return parejaId.Value;
    }

    private static async Task<int?> ObtenerProgramaParejaLhRhIdAsync(
        int programaProduccionId,
        SqlConnection cn,
        SqlTransaction tx)
    {
        const string sql = @"
DECLARE @Observaciones NVARCHAR(500);

SELECT
    @Observaciones = Observaciones
FROM dbo.Planeacion_ProgramaProduccion
WHERE ProgramaProduccionID = @ProgramaProduccionID
  AND Activo = 1;

DECLARE @Pos INT =
    CHARINDEX(N'NSQ_LHRH_PAIR:', ISNULL(@Observaciones,N''));

IF @Pos <= 0
BEGIN
    SELECT CAST(NULL AS INT);
    RETURN;
END;

DECLARE @Inicio INT = @Pos + LEN(N'NSQ_LHRH_PAIR:');
DECLARE @Resto NVARCHAR(100) = SUBSTRING(@Observaciones,@Inicio,100);
DECLARE @Fin INT = CHARINDEX(N';',@Resto);

IF @Fin > 0
    SET @Resto = LEFT(@Resto,@Fin-1);

DECLARE @Grupo INT = TRY_CONVERT(INT,LTRIM(RTRIM(@Resto)));

SELECT TOP (1)
    pp.ProgramaProduccionID
FROM dbo.Planeacion_ProgramaProduccion pp
WHERE pp.Activo = 1
  AND pp.ProgramaProduccionID <> @ProgramaProduccionID
  AND pp.Observaciones LIKE
      N'%NSQ_LHRH_PAIR:' + CONVERT(NVARCHAR(20),@Grupo) + N';%'
ORDER BY pp.ProgramaProduccionID;";

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value =
            programaProduccionId;

        var result = await cmd.ExecuteScalarAsync();

        return result == null || result == DBNull.Value
            ? null
            : Convert.ToInt32(result);
    }
}
