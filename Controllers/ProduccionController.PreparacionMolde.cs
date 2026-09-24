using ERP.NSQuell.Servicios.Produccion;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers
{
    public sealed partial class ProduccionController
    {
        // NSQ_PREPARACION_MOLDE_FUENTE_UNICA_V2
        // Bandeja = proyección de Planeación centralizada.
        // Inicio real = comparación contra el molde físico actual de la máquina.
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

        private async Task<Dictionary<int, string>> ObtenerEstadosCambioMoldeBandejaAsync(SqlConnection cn, SqlTransaction? tx = null)
        {
            var result = new Dictionary<int, string>();
            const string sql = @"
SELECT pp.ProgramaProduccionID
FROM dbo.Planeacion_ProgramaProduccion pp
WHERE pp.Activo=1
  AND pp.MaquinaID IS NOT NULL
  AND pp.FechaInicioProgramada IS NOT NULL
  AND ISNULL(pp.EstatusID,1) NOT IN(5,6,9,99)
ORDER BY pp.FechaInicioProgramada,ISNULL(pp.SecuenciaMaquina,999999),pp.ProgramaProduccionID;";
            var ids = new List<int>();
            await using (var cmd = tx == null ? new SqlCommand(sql, cn) : new SqlCommand(sql, cn, tx))
            {
                await using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync()) ids.Add(Convert.ToInt32(rd["ProgramaProduccionID"]));
            }
            var evaluaciones = await CambioMoldeService.EvaluarProgramasAsync(ids, cn, tx, actualizarSnapshot: true, modo: CambioMoldeModoEvaluacion.ProyeccionPlaneacion);
            foreach (var evaluacion in evaluaciones.Values) result[evaluacion.ProgramaProduccionID] = ResolverEstadoCambioMolde(evaluacion);
            return result;
        }

        private async Task<EstadoCambioMoldeProduccionInterno> ObtenerEstadoCambioMoldeProgramaAsync(int programaProduccionId, SqlConnection cn, SqlTransaction? tx = null)
        {
            var evaluacion = await CambioMoldeService.EvaluarProgramaAsync(programaProduccionId, cn, tx, actualizarSnapshot: true, modo: CambioMoldeModoEvaluacion.FisicoActual);
            return new EstadoCambioMoldeProduccionInterno
            {
                ProgramaProduccionID = programaProduccionId,
                RequiereCambioMolde = evaluacion.RequiereCambioMolde,
                Estado = ResolverEstadoCambioMolde(evaluacion)
            };
        }

        private static string ResolverEstadoCambioMolde(CambioMoldeEvaluacion evaluacion)
        {
            if (!evaluacion.RequiereCambioMolde) return EstadoMoldeNoAplica;
            if (evaluacion.TareaActiva && string.Equals(evaluacion.EstadoTarea, EstadoMoldeConfirmada, StringComparison.OrdinalIgnoreCase)) return EstadoMoldeConfirmada;
            if (evaluacion.TareaActiva && string.Equals(evaluacion.EstadoTarea, EstadoMoldeEnProceso, StringComparison.OrdinalIgnoreCase)) return EstadoMoldeEnProceso;
            return EstadoMoldePendiente;
        }

        private static string MensajeBloqueoCambioMolde(string estado)
        {
            if (string.Equals(estado, EstadoMoldeEnProceso, StringComparison.OrdinalIgnoreCase))
                return "La OF no puede iniciar preparación porque el cambio de molde se encuentra EN PROCESO. Finaliza y confirma el cambio de molde en Preparación de producción.";
            return "La OF no puede iniciar preparación porque tiene PENDIENTE EL CAMBIO DE MOLDE. Realiza y confirma el cambio de molde en Preparación de producción antes de continuar.";
        }
    }
}