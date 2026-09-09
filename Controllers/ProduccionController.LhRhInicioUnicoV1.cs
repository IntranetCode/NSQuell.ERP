using ERP.NSQuell.Models;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers
{
    public sealed partial class ProduccionController
    {
        // NSQ_LHRH_INICIO_UNICO_V1
        private sealed class ChecklistLhRhFuenteV1
        {
            public int ChecklistArranqueID { get; set; }
            public int EjecucionProduccionID { get; set; }
            public int ProgramaProduccionID { get; set; }
            public string CodigoFormato { get; set; } = string.Empty;
            public string VersionFormato { get; set; } = string.Empty;
            public string TipoChecklist { get; set; } = string.Empty;
            public string MomentoProceso { get; set; } = string.Empty;
            public DateTime FechaOperacion { get; set; }
            public int? TurnoID { get; set; }
            public string? TurnoNombre { get; set; }
            public int NumeroAplicacion { get; set; }
            public bool EsRecurrente { get; set; }
            public bool RequiereCambioMolde { get; set; }
        }

        private async Task<ProgramaParaProduccion> ObtenerProgramaParejaParaInicioUnicoLhRhV1Async(
            ProduccionParejaLhRhVm pareja,
            SqlConnection cn,
            SqlTransaction tx)
        {
            if (pareja == null || pareja.ProgramaParejaID <= 0)
                throw new InvalidOperationException("La pareja LH/RH no es valida.");

            if (pareja.EjecucionParejaID.HasValue && pareja.EjecucionParejaID.Value > 0)
                throw new InvalidOperationException($"La OF pareja {pareja.OFParejaTexto} ya tiene una ejecucion activa. No se creara un inicio LH/RH parcial.");

            var existente = await ObtenerEjecucionActivaPorProgramaAsync(pareja.ProgramaParejaID, cn, tx);
            if (existente.HasValue)
                throw new InvalidOperationException($"La OF pareja {pareja.OFParejaTexto} ya tiene la ejecucion {existente.Value}. No se creara un inicio LH/RH parcial.");

            var programaPareja = await ObtenerProgramaParaIniciarAsync(pareja.ProgramaParejaID, cn, tx);
            if (programaPareja == null)
                throw new InvalidOperationException($"La OF pareja {pareja.OFParejaTexto} ya no esta disponible para iniciar. Revisa Planeacion y Produccion antes de continuar.");

            if (!programaPareja.MaquinaID.HasValue || programaPareja.MaquinaID.Value <= 0)
                throw new InvalidOperationException($"La OF pareja {pareja.OFParejaTexto} no tiene maquina asignada.");

            if ((programaPareja.CantidadPlaneada ?? 0) <= 0)
                throw new InvalidOperationException($"La OF pareja {pareja.OFParejaTexto} no tiene cantidad programada valida.");

            var estadoCambioMolde = await ObtenerEstadoCambioMoldeProgramaAsync(pareja.ProgramaParejaID, cn, tx);
            if (estadoCambioMolde.RequiereCambioMolde &&
                !string.Equals(estadoCambioMolde.Estado, EstadoMoldeConfirmada, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"La OF pareja {pareja.OFParejaTexto} todavia no tiene confirmado el mismo cambio de molde fisico. " +
                    MensajeBloqueoCambioMolde(estadoCambioMolde.Estado));
            }

            return programaPareja;
        }

        private async Task<ChecklistLhRhFuenteV1?> ObtenerChecklistFuenteLhRhV1Async(
            int checklistArranqueId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT TOP(1)
    c.ChecklistArranqueID,
    c.EjecucionProduccionID,
    c.ProgramaProduccionID,
    c.CodigoFormato,
    ISNULL(c.VersionFormato,N'') AS VersionFormato,
    ISNULL(c.TipoChecklist,N'') AS TipoChecklist,
    ISNULL(c.MomentoProceso,N'') AS MomentoProceso,
    ISNULL(c.FechaOperacion,CONVERT(date,c.FechaChecklist)) AS FechaOperacion,
    c.TurnoID,
    c.TurnoNombre,
    ISNULL(c.NumeroAplicacion,1) AS NumeroAplicacion,
    ISNULL(c.EsRecurrente,0) AS EsRecurrente,
    ISNULL(c.RequiereCambioMolde,0) AS RequiereCambioMolde
FROM dbo.Produccion_ChecklistArranque c WITH(UPDLOCK,HOLDLOCK)
WHERE c.ChecklistArranqueID=@ChecklistArranqueID
  AND c.Activo=1;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ChecklistArranqueID", SqlDbType.Int).Value = checklistArranqueId;
            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync()) return null;

            return new ChecklistLhRhFuenteV1
            {
                ChecklistArranqueID = Convert.ToInt32(rd["ChecklistArranqueID"]),
                EjecucionProduccionID = Convert.ToInt32(rd["EjecucionProduccionID"]),
                ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),
                CodigoFormato = rd["CodigoFormato"]?.ToString()?.Trim() ?? string.Empty,
                VersionFormato = rd["VersionFormato"]?.ToString()?.Trim() ?? string.Empty,
                TipoChecklist = rd["TipoChecklist"]?.ToString()?.Trim() ?? string.Empty,
                MomentoProceso = rd["MomentoProceso"]?.ToString()?.Trim() ?? string.Empty,
                FechaOperacion = Convert.ToDateTime(rd["FechaOperacion"]),
                TurnoID = rd["TurnoID"] == DBNull.Value ? null : Convert.ToInt32(rd["TurnoID"]),
                TurnoNombre = rd["TurnoNombre"] == DBNull.Value ? null : rd["TurnoNombre"]?.ToString()?.Trim(),
                NumeroAplicacion = Convert.ToInt32(rd["NumeroAplicacion"]),
                EsRecurrente = Convert.ToBoolean(rd["EsRecurrente"]),
                RequiereCambioMolde = Convert.ToBoolean(rd["RequiereCambioMolde"])
            };
        }

        private async Task<int?> SincronizarChecklistLhRhProduccionV1Async(
            int checklistArranqueId,
            bool enviarACalidad,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            var fuente = await ObtenerChecklistFuenteLhRhV1Async(checklistArranqueId, cn, tx);
            if (fuente == null) return null;

            var pareja = await ObtenerParejaLhRhProduccionAsync(fuente.ProgramaProduccionID, cn, tx);
            if (pareja == null) return null;

            if (!pareja.EjecucionParejaID.HasValue || pareja.EjecucionParejaID.Value <= 0)
                throw new InvalidOperationException($"La produccion LH/RH grupo {pareja.GrupoLhRh} no tiene creada la ejecucion de {pareja.OFParejaTexto}. No se puede sincronizar un checklist fisico a medias.");

            var ejecucionPareja = await ObtenerEjecucionAsync(pareja.EjecucionParejaID.Value, cn, tx);
            if (ejecucionPareja == null)
                throw new InvalidOperationException($"No se encontro la ejecucion activa de {pareja.OFParejaTexto} para sincronizar el checklist LH/RH.");

            var checklistParejaId = await ObtenerOCrearChecklistFormatoAsync(
                ejecucion: ejecucionPareja,
                codigoFormato: fuente.CodigoFormato,
                versionFormato: fuente.VersionFormato,
                tipoChecklist: fuente.TipoChecklist,
                momentoProceso: fuente.MomentoProceso,
                fechaOperacion: fuente.FechaOperacion,
                turnoId: fuente.TurnoID,
                turnoNombre: fuente.TurnoNombre,
                esRecurrente: fuente.EsRecurrente,
                requiereCambioMolde: fuente.RequiereCambioMolde,
                numeroAplicacion: fuente.NumeroAplicacion,
                usuarioId: usuarioId,
                cn: cn,
                tx: tx);

            if (checklistParejaId <= 0 || checklistParejaId == fuente.ChecklistArranqueID)
                throw new InvalidOperationException("No fue posible resolver el checklist espejo de la pareja LH/RH.");

            const string sqlCopiar = @"
UPDATE destino
SET
    destino.Resultado=origen.Resultado,
    destino.Observaciones=origen.Observaciones,
    destino.Confirmado=origen.Confirmado,
    destino.ValorCapturado=origen.ValorCapturado,
    destino.Unidad=origen.Unidad,
    destino.Especificacion=origen.Especificacion,
    destino.Tolerancia=origen.Tolerancia,
    destino.UsuarioRespuestaID=origen.UsuarioRespuestaID,
    destino.FechaRespuesta=origen.FechaRespuesta,
    destino.UsuarioModificacionID=@UsuarioID,
    destino.FechaModificacion=GETDATE()
FROM dbo.Produccion_ChecklistArranqueDetalle destino
INNER JOIN dbo.Produccion_ChecklistArranqueDetalle origen
    ON origen.ChecklistArranqueID=@ChecklistOrigenID
   AND origen.PreguntaID=destino.PreguntaID
   AND origen.Activo=1
INNER JOIN dbo.ERP_ChecklistArranquePreguntas p
    ON p.PreguntaID=destino.PreguntaID
   AND p.Activo=1
WHERE destino.ChecklistArranqueID=@ChecklistDestinoID
  AND destino.Activo=1
  AND ISNULL(p.EsPreguntaCalidad,0)=0
  AND UPPER(LTRIM(RTRIM(ISNULL(p.GrupoResponsable,N'')))) NOT IN(N'CALIDAD',N'AUDITOR',N'AUDITOR DE CALIDAD')
  AND UPPER(ISNULL(p.Seccion,N'')) NOT LIKE N'%CALIDAD%'
  AND UPPER(ISNULL(p.Seccion,N'')) NOT LIKE N'%AUDITOR%'
  AND UPPER(ISNULL(p.ResponsableSugerido,N'')) NOT LIKE N'%CALIDAD%'
  AND UPPER(ISNULL(p.ResponsableSugerido,N'')) NOT LIKE N'%AUDITOR%';

UPDATE destino
SET
    destino.EstatusID=origen.EstatusID,
    destino.UsuarioProduccionID=origen.UsuarioProduccionID,
    destino.FechaCapturaProduccion=origen.FechaCapturaProduccion,
    destino.ObservacionesGenerales=origen.ObservacionesGenerales,
    destino.TecnicoEntregaPersonaID=CASE WHEN UPPER(ISNULL(origen.TipoChecklist,N''))=N'MONITOREO_PERIFERICOS' THEN origen.TecnicoEntregaPersonaID ELSE destino.TecnicoEntregaPersonaID END,
    destino.TecnicoEntregaNombre=CASE WHEN UPPER(ISNULL(origen.TipoChecklist,N''))=N'MONITOREO_PERIFERICOS' THEN origen.TecnicoEntregaNombre ELSE destino.TecnicoEntregaNombre END,
    destino.FechaEntregaTurno=CASE WHEN UPPER(ISNULL(origen.TipoChecklist,N''))=N'MONITOREO_PERIFERICOS' THEN origen.FechaEntregaTurno ELSE destino.FechaEntregaTurno END,
    destino.TecnicoRecibePersonaID=CASE WHEN UPPER(ISNULL(origen.TipoChecklist,N''))=N'MONITOREO_PERIFERICOS' THEN origen.TecnicoRecibePersonaID ELSE destino.TecnicoRecibePersonaID END,
    destino.TecnicoRecibeNombre=CASE WHEN UPPER(ISNULL(origen.TipoChecklist,N''))=N'MONITOREO_PERIFERICOS' THEN origen.TecnicoRecibeNombre ELSE destino.TecnicoRecibeNombre END,
    destino.FechaRecepcionTurno=CASE WHEN UPPER(ISNULL(origen.TipoChecklist,N''))=N'MONITOREO_PERIFERICOS' THEN origen.FechaRecepcionTurno ELSE destino.FechaRecepcionTurno END,
    destino.UsuarioModificacionID=@UsuarioID,
    destino.FechaModificacion=GETDATE()
FROM dbo.Produccion_ChecklistArranque destino
INNER JOIN dbo.Produccion_ChecklistArranque origen
    ON origen.ChecklistArranqueID=@ChecklistOrigenID
   AND origen.Activo=1
WHERE destino.ChecklistArranqueID=@ChecklistDestinoID
  AND destino.Activo=1;";

            await using (var cmd = new SqlCommand(sqlCopiar, cn, tx))
            {
                cmd.Parameters.Add("@ChecklistOrigenID", SqlDbType.Int).Value = fuente.ChecklistArranqueID;
                cmd.Parameters.Add("@ChecklistDestinoID", SqlDbType.Int).Value = checklistParejaId;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                await cmd.ExecuteNonQueryAsync();
            }

            if (enviarACalidad &&
                string.Equals(fuente.TipoChecklist, "ARRANQUE_LIBERACION", StringComparison.OrdinalIgnoreCase))
            {
                await CrearOActualizarSolicitudCalidadAsync(
                    checklistParejaId,
                    pareja.EjecucionParejaID.Value,
                    usuarioId,
                    cn,
                    tx);
            }

            return checklistParejaId;
        }
    }
}