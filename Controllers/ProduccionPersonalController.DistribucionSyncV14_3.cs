using ERP.NSQuell.Models;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers;

// NSQ_PRODUCCION_PERSONAL_DISTRIBUCION_SYNC_V14_3
public sealed partial class ProduccionPersonalController
{
    private sealed class DistribucionPrincipalV143
    {
        public long DistribucionID { get; set; }
        public DateTime FechaTrabajo { get; set; }
        public int TurnoID { get; set; }
        public int MaquinaID { get; set; }
        public DateTime Inicio { get; set; }
        public DateTime Fin { get; set; }
        public int? OperadorID { get; set; }
        public string OperadorNombre { get; set; } = string.Empty;
    }

    private static async Task<bool> DistribucionV143DisponibleAsync(
        SqlConnection cn,
        SqlTransaction? tx)
    {
        const string sql = @"
SELECT CONVERT(bit,CASE WHEN
       OBJECT_ID(N'dbo.Produccion_DistribucionOperadores',N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.Produccion_DistribucionOperadoresHistorial',N'U') IS NOT NULL
THEN 1 ELSE 0 END);";

        await using var cmd = tx == null
            ? new SqlCommand(sql, cn)
            : new SqlCommand(sql, cn, tx);

        return Convert.ToBoolean(await cmd.ExecuteScalarAsync() ?? false);
    }

    private static async Task AplicarDistribucionPrincipalV143Async(
        List<ProduccionPersonalV7SegmentoVm> segmentos,
        DateTime desde,
        DateTime hasta,
        SqlConnection cn,
        SqlTransaction? tx)
    {
        if (segmentos.Count == 0)
            return;

        if (!await DistribucionV143DisponibleAsync(cn, tx))
            return;

        const string sql = @"
SELECT
    d.DistribucionID,
    d.FechaTrabajo,
    d.TurnoID,
    d.MaquinaID,
    d.Inicio,
    d.Fin,
    d.OperadorID,
    LTRIM(RTRIM(CONCAT(
        ISNULL(p.Nombre,N''),N' ',
        ISNULL(p.ApellidoPaterno,N''),N' ',
        ISNULL(p.ApellidoMaterno,N'')))) AS OperadorNombre
FROM dbo.Produccion_DistribucionOperadores d
LEFT JOIN dbo.Persona p
    ON p.PersonaID=d.OperadorID
WHERE d.Activo=1
  AND d.MaquinaID IS NOT NULL
  AND d.Inicio<@Hasta
  AND d.Fin>@Desde
ORDER BY d.FechaTrabajo,d.TurnoID,d.MaquinaID,d.DistribucionID DESC;";

        var distribuciones = new List<DistribucionPrincipalV143>();

        await using (var cmd = tx == null
            ? new SqlCommand(sql, cn)
            : new SqlCommand(sql, cn, tx))
        {
            cmd.Parameters.Add("@Desde", SqlDbType.DateTime2).Value = desde;
            cmd.Parameters.Add("@Hasta", SqlDbType.DateTime2).Value = hasta;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                distribuciones.Add(new DistribucionPrincipalV143
                {
                    DistribucionID = Convert.ToInt64(rd["DistribucionID"]),
                    FechaTrabajo = Convert.ToDateTime(rd["FechaTrabajo"]).Date,
                    TurnoID = Convert.ToInt32(rd["TurnoID"]),
                    MaquinaID = Convert.ToInt32(rd["MaquinaID"]),
                    Inicio = Convert.ToDateTime(rd["Inicio"]),
                    Fin = Convert.ToDateTime(rd["Fin"]),
                    OperadorID = rd["OperadorID"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(rd["OperadorID"]),
                    OperadorNombre = rd["OperadorNombre"]?.ToString()?.Trim() ?? string.Empty
                });
            }
        }

        foreach (var segmento in segmentos)
        {
            // Historial terminado y ejecución real en curso conservan su operador
            // operativo/histórico. La distribución manda en segmentos no iniciados.
            if (segmento.Fin <= DateTime.Now ||
                (segmento.ProduccionActiva &&
                 DateTime.Now >= segmento.Inicio &&
                 DateTime.Now < segmento.Fin))
            {
                continue;
            }

            var encontrada = distribuciones
                .Where(x =>
                    x.MaquinaID == segmento.MaquinaID &&
                    x.TurnoID == segmento.TurnoID &&
                    x.FechaTrabajo == segmento.FechaTrabajo.Date &&
                    x.Inicio < segmento.Fin &&
                    x.Fin > segmento.Inicio)
                .OrderByDescending(x =>
                    Math.Min(x.Fin.Ticks, segmento.Fin.Ticks) -
                    Math.Max(x.Inicio.Ticks, segmento.Inicio.Ticks))
                .ThenByDescending(x => x.DistribucionID)
                .FirstOrDefault();

            if (encontrada == null)
                continue;

            segmento.TieneAsignacionEspecifica = true;
            segmento.OperadorAsignadoID = encontrada.OperadorID;
            segmento.OperadorAsignadoNombre = encontrada.OperadorNombre;
        }
    }

    private static async Task SincronizarDistribucionDesdeDetalleOfV143Async(
        DateTime fechaTrabajo,
        int turnoId,
        int maquinaId,
        int programaProduccionId,
        int? parteId,
        int? operadorId,
        DateTime inicioTurno,
        DateTime finTurno,
        string? motivo,
        string? justificacion,
        int usuarioId,
        SqlConnection cn,
        SqlTransaction tx)
    {
        if (!await DistribucionV143DisponibleAsync(cn, tx))
            return;

        const string actualSql = @"
SELECT TOP(1)
    DistribucionID,
    ProgramaProduccionID,
    ParteID,
    OperadorID
FROM dbo.Produccion_DistribucionOperadores WITH(UPDLOCK,HOLDLOCK)
WHERE Activo=1
  AND FechaTrabajo=@Fecha
  AND TurnoID=@TurnoID
  AND MaquinaID=@MaquinaID
ORDER BY DistribucionID DESC;";

        long? distribucionId = null;
        int? programaAnterior = null;
        int? parteAnterior = null;
        int? operadorAnterior = null;

        await using (var actual = new SqlCommand(actualSql, cn, tx))
        {
            actual.Parameters.Add("@Fecha", SqlDbType.Date).Value = fechaTrabajo.Date;
            actual.Parameters.Add("@TurnoID", SqlDbType.Int).Value = turnoId;
            actual.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = maquinaId;

            await using var rd = await actual.ExecuteReaderAsync();

            if (await rd.ReadAsync())
            {
                distribucionId = Convert.ToInt64(rd["DistribucionID"]);
                programaAnterior = rd["ProgramaProduccionID"] == DBNull.Value
                    ? null
                    : Convert.ToInt32(rd["ProgramaProduccionID"]);
                parteAnterior = rd["ParteID"] == DBNull.Value
                    ? null
                    : Convert.ToInt32(rd["ParteID"]);
                operadorAnterior = rd["OperadorID"] == DBNull.Value
                    ? null
                    : Convert.ToInt32(rd["OperadorID"]);
            }
        }

        if (distribucionId.HasValue)
        {
            const string updateSql = @"
UPDATE dbo.Produccion_DistribucionOperadores
SET OperadorID=@OperadorID,
    Inicio=@Inicio,
    Fin=@Fin,
    ProgramaProduccionID=COALESCE(ProgramaProduccionID,@ProgramaID),
    ParteID=COALESCE(ParteID,@ParteID),
    OrigenPieza=CASE
        WHEN ParteID IS NULL AND @ParteID IS NOT NULL THEN N'PROGRAMA'
        ELSE OrigenPieza
    END,
    UsuarioModificacionID=@Usuario,
    FechaModificacion=SYSDATETIME()
WHERE DistribucionID=@DistribucionID
  AND Activo=1;";

            await using var update = new SqlCommand(updateSql, cn, tx);
            update.Parameters.Add("@OperadorID", SqlDbType.Int).Value =
                operadorId.HasValue ? operadorId.Value : DBNull.Value;
            update.Parameters.Add("@Inicio", SqlDbType.DateTime2).Value = inicioTurno;
            update.Parameters.Add("@Fin", SqlDbType.DateTime2).Value = finTurno;
            update.Parameters.Add("@ProgramaID", SqlDbType.Int).Value = programaProduccionId;
            update.Parameters.Add("@ParteID", SqlDbType.Int).Value =
                parteId.HasValue ? parteId.Value : DBNull.Value;
            update.Parameters.Add("@Usuario", SqlDbType.Int).Value = usuarioId;
            update.Parameters.Add("@DistribucionID", SqlDbType.BigInt).Value = distribucionId.Value;
            await update.ExecuteNonQueryAsync();
        }
        else
        {
            const string insertSql = @"
INSERT dbo.Produccion_DistribucionOperadores
(
    FechaTrabajo,
    TurnoID,
    MaquinaID,
    CentroEspecial,
    ProgramaProduccionID,
    ParteID,
    OperadorID,
    Inicio,
    Fin,
    OrigenPieza,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
OUTPUT INSERTED.DistribucionID
VALUES
(
    @Fecha,
    @TurnoID,
    @MaquinaID,
    NULL,
    @ProgramaID,
    @ParteID,
    @OperadorID,
    @Inicio,
    @Fin,
    N'PROGRAMA',
    @Usuario,
    SYSDATETIME(),
    1
);";

            await using var insert = new SqlCommand(insertSql, cn, tx);
            insert.Parameters.Add("@Fecha", SqlDbType.Date).Value = fechaTrabajo.Date;
            insert.Parameters.Add("@TurnoID", SqlDbType.Int).Value = turnoId;
            insert.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = maquinaId;
            insert.Parameters.Add("@ProgramaID", SqlDbType.Int).Value = programaProduccionId;
            insert.Parameters.Add("@ParteID", SqlDbType.Int).Value =
                parteId.HasValue ? parteId.Value : DBNull.Value;
            insert.Parameters.Add("@OperadorID", SqlDbType.Int).Value =
                operadorId.HasValue ? operadorId.Value : DBNull.Value;
            insert.Parameters.Add("@Inicio", SqlDbType.DateTime2).Value = inicioTurno;
            insert.Parameters.Add("@Fin", SqlDbType.DateTime2).Value = finTurno;
            insert.Parameters.Add("@Usuario", SqlDbType.Int).Value = usuarioId;

            distribucionId = Convert.ToInt64(await insert.ExecuteScalarAsync());
        }

        if (operadorAnterior == operadorId &&
            programaAnterior == programaProduccionId &&
            parteAnterior == parteId)
        {
            return;
        }

        const string histSql = @"
INSERT dbo.Produccion_DistribucionOperadoresHistorial
(
    DistribucionID,
    FechaTrabajo,
    TurnoID,
    MaquinaID,
    CentroEspecial,
    ProgramaAnteriorID,
    ProgramaNuevoID,
    ParteAnteriorID,
    ParteNuevaID,
    OperadorAnteriorID,
    OperadorNuevoID,
    Motivo,
    Justificacion,
    UsuarioID,
    FechaMovimiento
)
VALUES
(
    @DistribucionID,
    @Fecha,
    @TurnoID,
    @MaquinaID,
    NULL,
    @ProgramaAnteriorID,
    @ProgramaNuevoID,
    @ParteAnteriorID,
    @ParteNuevaID,
    @OperadorAnteriorID,
    @OperadorNuevoID,
    @Motivo,
    @Justificacion,
    @Usuario,
    SYSDATETIME()
);";

        await using var hist = new SqlCommand(histSql, cn, tx);
        hist.Parameters.Add("@DistribucionID", SqlDbType.BigInt).Value = distribucionId!.Value;
        hist.Parameters.Add("@Fecha", SqlDbType.Date).Value = fechaTrabajo.Date;
        hist.Parameters.Add("@TurnoID", SqlDbType.Int).Value = turnoId;
        hist.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = maquinaId;
        hist.Parameters.Add("@ProgramaAnteriorID", SqlDbType.Int).Value =
            programaAnterior.HasValue ? programaAnterior.Value : DBNull.Value;
        hist.Parameters.Add("@ProgramaNuevoID", SqlDbType.Int).Value = programaProduccionId;
        hist.Parameters.Add("@ParteAnteriorID", SqlDbType.Int).Value =
            parteAnterior.HasValue ? parteAnterior.Value : DBNull.Value;
        hist.Parameters.Add("@ParteNuevaID", SqlDbType.Int).Value =
            parteId.HasValue ? parteId.Value : DBNull.Value;
        hist.Parameters.Add("@OperadorAnteriorID", SqlDbType.Int).Value =
            operadorAnterior.HasValue ? operadorAnterior.Value : DBNull.Value;
        hist.Parameters.Add("@OperadorNuevoID", SqlDbType.Int).Value =
            operadorId.HasValue ? operadorId.Value : DBNull.Value;
        hist.Parameters.Add("@Motivo", SqlDbType.NVarChar, 60).Value =
            string.IsNullOrWhiteSpace(motivo)
                ? "DETALLE_OF"
                : motivo.Trim()[..Math.Min(60, motivo.Trim().Length)];
        hist.Parameters.Add("@Justificacion", SqlDbType.NVarChar, 500).Value =
            string.IsNullOrWhiteSpace(justificacion)
                ? DBNull.Value
                : justificacion.Trim()[..Math.Min(500, justificacion.Trim().Length)];
        hist.Parameters.Add("@Usuario", SqlDbType.Int).Value = usuarioId;
        await hist.ExecuteNonQueryAsync();
    }
}
