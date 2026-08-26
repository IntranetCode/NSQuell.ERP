using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers
{
    public sealed partial class ProduccionController
    {
        // NSQ_PREPARACION_MOLDE_BRIDGE_V1
        // MP/secado y embalaje NO participan en el bloqueo.

        private const string EstadoMoldeNoAplica = "NO_APLICA";
        private const string EstadoMoldePendiente = "PENDIENTE";
        private const string EstadoMoldeEnProceso = "EN_PROCESO";
        private const string EstadoMoldeConfirmada = "CONFIRMADA";

        private sealed class EstadoCambioMoldeProduccionInterno
        {
            public int ProgramaProduccionID { get; set; }
            public bool RequiereCambioMolde { get; set; }
            public string Estado { get; set; } = EstadoMoldeNoAplica;
        }

        private async Task<Dictionary<int, string>> ObtenerEstadosCambioMoldeBandejaAsync(
            SqlConnection cn,
            SqlTransaction? tx = null)
        {
            var estados = await CargarEstadosCambioMoldeProduccionAsync(null, cn, tx);
            return estados.ToDictionary(x => x.Key, x => x.Value.Estado);
        }

        private async Task<EstadoCambioMoldeProduccionInterno> ObtenerEstadoCambioMoldeProgramaAsync(
            int programaProduccionId,
            SqlConnection cn,
            SqlTransaction? tx = null)
        {
            var estados = await CargarEstadosCambioMoldeProduccionAsync(programaProduccionId, cn, tx);

            if (estados.TryGetValue(programaProduccionId, out var estado))
                return estado;

            return new EstadoCambioMoldeProduccionInterno
            {
                ProgramaProduccionID = programaProduccionId,
                RequiereCambioMolde = false,
                Estado = EstadoMoldeNoAplica
            };
        }

        private async Task<Dictionary<int, EstadoCambioMoldeProduccionInterno>>
            CargarEstadosCambioMoldeProduccionAsync(
                int? programaProduccionId,
                SqlConnection cn,
                SqlTransaction? tx)
        {
            var result = new Dictionary<int, EstadoCambioMoldeProduccionInterno>();

            const string sql = @"
SELECT
    pp.ProgramaProduccionID,
    CONVERT(bit,
        CASE
            WHEN anterior.MoldeID IS NOT NULL AND pp.MoldeID IS NOT NULL
                THEN CASE WHEN anterior.MoldeID <> pp.MoldeID THEN 1 ELSE 0 END
            WHEN NULLIF(LTRIM(RTRIM(ISNULL(anterior.MoldeCodigo,N''))),N'') IS NULL
              OR NULLIF(LTRIM(RTRIM(ISNULL(pp.MoldeCodigo,N''))),N'') IS NULL
                THEN 0
            WHEN UPPER(LTRIM(RTRIM(anterior.MoldeCodigo))) <>
                 UPPER(LTRIM(RTRIM(pp.MoldeCodigo)))
                THEN 1
            ELSE 0
        END
    ) AS RequiereCambioMolde,
    tarea.Estado AS EstadoTarea,
    ISNULL(tarea.Activo,0) AS TareaActiva
FROM dbo.Planeacion_ProgramaProduccion pp
OUTER APPLY
(
    SELECT TOP (1)
        ant.ProgramaProduccionID,
        ant.MoldeID,
        ant.MoldeCodigo
    FROM dbo.Planeacion_ProgramaProduccion ant
    WHERE ant.Activo=1
      AND ant.ProgramaProduccionID<>pp.ProgramaProduccionID
      AND ant.MaquinaID=pp.MaquinaID
      AND ant.FechaInicioProgramada<pp.FechaInicioProgramada
      AND ISNULL(ant.EstatusID,1)<>99
    ORDER BY ant.FechaInicioProgramada DESC, ant.ProgramaProduccionID DESC
) anterior
OUTER APPLY
(
    SELECT TOP (1)
        pa.Estado,
        pa.Activo
    FROM dbo.Produccion_PreparacionAnticipada pa
    WHERE pa.ProgramaProduccionID=pp.ProgramaProduccionID
      AND pa.TipoTarea=N'CAMBIO_MOLDE'
    ORDER BY pa.PreparacionAnticipadaID DESC
) tarea
WHERE pp.Activo=1
  AND pp.MaquinaID IS NOT NULL
  AND pp.FechaInicioProgramada IS NOT NULL
  AND ISNULL(pp.EstatusID,1) NOT IN(5,6,9,99)
  AND (@ProgramaProduccionID IS NULL OR pp.ProgramaProduccionID=@ProgramaProduccionID);";

            await using var cmd =
                tx == null ? new SqlCommand(sql, cn) : new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value =
                programaProduccionId.HasValue ? programaProduccionId.Value : DBNull.Value;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                var id = Convert.ToInt32(rd["ProgramaProduccionID"]);
                var requiere = rd["RequiereCambioMolde"] != DBNull.Value &&
                               Convert.ToBoolean(rd["RequiereCambioMolde"]);
                var tareaActiva = rd["TareaActiva"] != DBNull.Value &&
                                  Convert.ToBoolean(rd["TareaActiva"]);
                var estadoTarea = rd["EstadoTarea"] == DBNull.Value
                    ? null
                    : rd["EstadoTarea"]?.ToString()?.Trim()?.ToUpperInvariant();

                var estado = EstadoMoldeNoAplica;

                if (requiere)
                {
                    if (tareaActiva &&
                        string.Equals(estadoTarea, EstadoMoldeConfirmada, StringComparison.OrdinalIgnoreCase))
                    {
                        estado = EstadoMoldeConfirmada;
                    }
                    else if (tareaActiva &&
                             string.Equals(estadoTarea, EstadoMoldeEnProceso, StringComparison.OrdinalIgnoreCase))
                    {
                        estado = EstadoMoldeEnProceso;
                    }
                    else
                    {
                        estado = EstadoMoldePendiente;
                    }
                }

                result[id] = new EstadoCambioMoldeProduccionInterno
                {
                    ProgramaProduccionID = id,
                    RequiereCambioMolde = requiere,
                    Estado = estado
                };
            }

            return result;
        }

        private static string MensajeBloqueoCambioMolde(string estado)
        {
            if (string.Equals(estado, EstadoMoldeEnProceso, StringComparison.OrdinalIgnoreCase))
            {
                return
                    "La OF no puede iniciar preparación porque el cambio de molde se encuentra EN PROCESO. " +
                    "Finaliza y confirma el cambio de molde en Preparación de producción.";
            }

            return
                "La OF no puede iniciar preparación porque tiene PENDIENTE EL CAMBIO DE MOLDE. " +
                "Realiza y confirma el cambio de molde en Preparación de producción antes de continuar.";
        }
    }
}