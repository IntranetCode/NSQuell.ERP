using ERP.NSQuell.Models;
using Microsoft.Data.SqlClient;
using System;
using System.Data;
using System.Threading.Tasks;

namespace ERP.NSQuell.Servicios.Produccion
{
    // NSQ_FIN_ANTICIPADO_V1
    public sealed class TerminacionAnticipadaResultado
    {
        public int EjecucionProduccionID { get; init; }
        public int ProgramaProduccionID { get; init; }
        public int CantidadPlaneada { get; init; }
        public int CantidadOK { get; init; }
        public int CantidadSospechosa { get; init; }
        public int CantidadScrap { get; init; }
        public int MonitoreosCancelados { get; init; }
        public int ReliberacionesCanceladas { get; init; }
        public int? InspeccionID { get; init; }
    }

    public static class TerminacionAnticipadaProduccionService
    {
        public static async Task<TerminacionAnticipadaResultado> EjecutarAsync(
            int ejecucionProduccionId,
            string motivo,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            if (ejecucionProduccionId <= 0)
                throw new InvalidOperationException("No se recibió una ejecución válida.");

            motivo = motivo?.Trim() ?? string.Empty;

            if (motivo.Length < 10)
                throw new InvalidOperationException("La finalización anticipada requiere una justificación de al menos 10 caracteres.");

            if (motivo.Length > 500)
                motivo = motivo[..500];

            if (usuarioId <= 0)
                throw new InvalidOperationException("No se pudo identificar al usuario que realiza el cierre.");

            const string sql = @"
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Ahora DATETIME2 = SYSDATETIME();
DECLARE @ProgramaProduccionID INT;
DECLARE @EstatusID INT;
DECLARE @FechaInicioReal DATETIME2;
DECLARE @CantidadPlaneada INT;
DECLARE @CantidadOK INT = 0;
DECLARE @CantidadSospechosa INT = 0;
DECLARE @CantidadScrap INT = 0;
DECLARE @InspeccionID INT;
DECLARE @EstadoCalidadAnterior NVARCHAR(50);
DECLARE @MonitoreosCancelados INT = 0;
DECLARE @ReliberacionesCanceladas INT = 0;
DECLARE @Nota NVARCHAR(1000);

SELECT
    @ProgramaProduccionID = e.ProgramaProduccionID,
    @EstatusID = e.EstatusID,
    @FechaInicioReal = e.FechaInicioReal,
    @CantidadPlaneada = ISNULL(e.CantidadPlaneada,0)
FROM dbo.Produccion_Ejecucion e WITH(UPDLOCK,HOLDLOCK)
WHERE e.EjecucionProduccionID=@EjecucionProduccionID
  AND e.Activo=1;

IF @ProgramaProduccionID IS NULL
    THROW 51501,'No se encontró la ejecución activa de Producción.',1;

IF @FechaInicioReal IS NULL
    THROW 51502,'La producción todavía no ha iniciado físicamente. Utilice el flujo de cancelación/reprogramación correspondiente.',1;

IF @EstatusID NOT IN (@EnPreparacion,@EnProduccion,@Pausado)
    THROW 51503,'La ejecución ya no se encuentra en un estado activo que permita finalizar anticipadamente.',1;

SELECT
    @CantidadOK = ISNULL(SUM(ISNULL(rh.CantidadOK,0)),0),
    @CantidadSospechosa = ISNULL(SUM(ISNULL(rh.CantidadSospechosa,0)),0),
    @CantidadScrap = ISNULL(SUM(ISNULL(rh.CantidadScrap,0)),0)
FROM dbo.Produccion_RegistroHora rh WITH(UPDLOCK,HOLDLOCK)
WHERE rh.EjecucionProduccionID=@EjecucionProduccionID
  AND rh.Activo=1;

SET @Nota = LEFT(CONCAT(
    N'[FIN ANTICIPADO] ', @Motivo,
    N' | Totales físicos registrados al cierre: OK=', @CantidadOK,
    N', Sospechoso=', @CantidadSospechosa,
    N', Scrap=', @CantidadScrap,
    N', Planeado=', @CantidadPlaneada, N'.'),1000);

UPDATE dbo.Produccion_Paros
SET FechaFinParo=@Ahora,
    DuracionMinutos=CASE WHEN FechaInicioParo IS NULL THEN ISNULL(DuracionMinutos,0) ELSE DATEDIFF(MINUTE,FechaInicioParo,@Ahora) END,
    EsMayorA15Minutos=CASE WHEN FechaInicioParo IS NOT NULL AND DATEDIFF(MINUTE,FechaInicioParo,@Ahora)>15 THEN 1 ELSE ISNULL(EsMayorA15Minutos,0) END,
    Descripcion=RIGHT(CONCAT(ISNULL(Descripcion,N''),CASE WHEN NULLIF(LTRIM(RTRIM(ISNULL(Descripcion,N''))),N'') IS NULL THEN N'' ELSE CHAR(13)+CHAR(10) END,@Nota),1000),
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=@Ahora
WHERE EjecucionProduccionID=@EjecucionProduccionID
  AND Activo=1
  AND FechaFinParo IS NULL;

UPDATE dbo.Produccion_TiempoExtra
SET FechaHoraFin=ISNULL(FechaHoraFin,@Ahora),
    Estado=N'CANCELADO',
    UsuarioCancelacionID=@UsuarioID,
    FechaCancelacion=ISNULL(FechaCancelacion,@Ahora),
    MotivoCancelacion=LEFT(CONCAT(N'Cancelado por finalización anticipada de producción. ',@Motivo),500),
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=@Ahora
WHERE EjecucionProduccionID=@EjecucionProduccionID
  AND Activo=1
  AND FechaHoraFin IS NULL;

UPDATE dbo.Produccion_Ejecucion
SET CantidadOKTotal=@CantidadOK,
    CantidadSospechosaTotal=@CantidadSospechosa,
    CantidadScrapTotal=@CantidadScrap,
    FechaFinReal=@Ahora,
    EstatusID=@TerminadoParcial,
    Observaciones=RIGHT(CONCAT(ISNULL(Observaciones,N''),CASE WHEN NULLIF(LTRIM(RTRIM(ISNULL(Observaciones,N''))),N'') IS NULL THEN N'' ELSE CHAR(13)+CHAR(10) END,@Nota),1000),
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=@Ahora
WHERE EjecucionProduccionID=@EjecucionProduccionID
  AND Activo=1;

IF @@ROWCOUNT<>1
    THROW 51504,'La ejecución cambió mientras se intentaba finalizar anticipadamente.',1;

UPDATE dbo.Planeacion_ProgramaProduccion
SET EstatusID=@ProgramaTerminado,
    FechaFinReal=@Ahora,
    HorasReales=CASE WHEN FechaInicioReal IS NOT NULL THEN CONVERT(DECIMAL(18,2),DATEDIFF(MINUTE,FechaInicioReal,@Ahora)/60.0) ELSE HorasReales END,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=@Ahora
WHERE ProgramaProduccionID=@ProgramaProduccionID
  AND Activo=1;

SELECT TOP(1)
    @InspeccionID=i.InspeccionID,
    @EstadoCalidadAnterior=i.Estado
FROM dbo.Calidad_Inspecciones i WITH(UPDLOCK,HOLDLOCK)
WHERE i.EjecucionProduccionID=@EjecucionProduccionID
  AND ISNULL(i.Estado,N'')<>@CalidadCerrada
ORDER BY i.InspeccionID DESC;

IF @InspeccionID IS NOT NULL
BEGIN
    UPDATE dbo.Calidad_MonitoreosProceso
    SET Activo=0,
        Observaciones=RIGHT(CONCAT(ISNULL(Observaciones,N''),CASE WHEN NULLIF(LTRIM(RTRIM(ISNULL(Observaciones,N''))),N'') IS NULL THEN N'' ELSE CHAR(13)+CHAR(10) END,N'Cancelado: la producción finalizó anticipadamente y esta revisión futura ya no corresponde.'),1000),
        UsuarioModificacionID=@UsuarioID,
        FechaModificacion=@Ahora
    WHERE InspeccionID=@InspeccionID
      AND Activo=1
      AND UPPER(LTRIM(RTRIM(ISNULL(Resultado,N'PENDIENTE'))))=N'PENDIENTE'
      AND RegistroHoraID IS NULL;

    SET @MonitoreosCancelados=@@ROWCOUNT;

    UPDATE dbo.Calidad_Reliberaciones
    SET Resultado=@ReliberacionCancelada,
        FechaValidacion=ISNULL(FechaValidacion,@Ahora),
        Observaciones=RIGHT(CONCAT(ISNULL(Observaciones,N''),CASE WHEN NULLIF(LTRIM(RTRIM(ISNULL(Observaciones,N''))),N'') IS NULL THEN N'' ELSE CHAR(13)+CHAR(10) END,N'Reliberación cancelada porque la producción finalizó anticipadamente. No autoriza reinicio. Motivo: ',@Motivo),1000),
        UsuarioModificacionID=@UsuarioID,
        FechaModificacion=@Ahora
    WHERE InspeccionID=@InspeccionID
      AND Activo=1
      AND UPPER(LTRIM(RTRIM(ISNULL(Resultado,N''))))=@ReliberacionPendiente;

    SET @ReliberacionesCanceladas=@@ROWCOUNT;

    UPDATE dbo.Calidad_Inspecciones
    SET RequiereReliberacion=0,
        ConfiguracionInvalidada=0,
        Liberado=0,
        Estado=@CalidadPendienteCaja,
        Observaciones=RIGHT(CONCAT(ISNULL(Observaciones,N''),CASE WHEN NULLIF(LTRIM(RTRIM(ISNULL(Observaciones,N''))),N'') IS NULL THEN N'' ELSE CHAR(13)+CHAR(10) END,@Nota),1000),
        UsuarioModificacionID=@UsuarioID,
        FechaModificacion=@Ahora
    WHERE InspeccionID=@InspeccionID
      AND ISNULL(Estado,N'')<>@CalidadCerrada;

    INSERT INTO dbo.Calidad_InspeccionHistorial
    (InspeccionID,Movimiento,EstadoAnterior,EstadoNuevo,ResultadoCalidad,Etiqueta,Comentario,UsuarioID,FechaMovimiento)
    VALUES
    (@InspeccionID,N'FIN_PRODUCCION_ANTICIPADO',@EstadoCalidadAnterior,@CalidadPendienteCaja,NULL,NULL,@Nota,@UsuarioID,@Ahora);
END;

SELECT
    @EjecucionProduccionID AS EjecucionProduccionID,
    @ProgramaProduccionID AS ProgramaProduccionID,
    @CantidadPlaneada AS CantidadPlaneada,
    @CantidadOK AS CantidadOK,
    @CantidadSospechosa AS CantidadSospechosa,
    @CantidadScrap AS CantidadScrap,
    @MonitoreosCancelados AS MonitoreosCancelados,
    @ReliberacionesCanceladas AS ReliberacionesCanceladas,
    @InspeccionID AS InspeccionID;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
            cmd.Parameters.Add("@Motivo", SqlDbType.NVarChar, 500).Value = motivo;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            cmd.Parameters.Add("@EnPreparacion", SqlDbType.Int).Value = ProduccionEstatus.EnPreparacion;
            cmd.Parameters.Add("@EnProduccion", SqlDbType.Int).Value = ProduccionEstatus.EnProduccion;
            cmd.Parameters.Add("@Pausado", SqlDbType.Int).Value = ProduccionEstatus.Pausado;
            cmd.Parameters.Add("@TerminadoParcial", SqlDbType.Int).Value = ProduccionEstatus.TerminadoParcial;
            cmd.Parameters.Add("@ProgramaTerminado", SqlDbType.Int).Value = ProgramaProduccionEstatus.Terminado;
            cmd.Parameters.Add("@CalidadCerrada", SqlDbType.NVarChar, 50).Value = CalidadEstados.Cerrada;
            cmd.Parameters.Add("@CalidadPendienteCaja", SqlDbType.NVarChar, 50).Value = CalidadEstados.PendienteLiberacionCaja;
            cmd.Parameters.Add("@ReliberacionPendiente", SqlDbType.NVarChar, 20).Value = CalidadResultadoReliberacion.Pendiente;
            cmd.Parameters.Add("@ReliberacionCancelada", SqlDbType.NVarChar, 20).Value = CalidadResultadoReliberacion.Cancelada;

            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync())
                throw new InvalidOperationException("La finalización anticipada no devolvió un resultado.");

            return new TerminacionAnticipadaResultado
            {
                EjecucionProduccionID = Convert.ToInt32(rd["EjecucionProduccionID"]),
                ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),
                CantidadPlaneada = Convert.ToInt32(rd["CantidadPlaneada"]),
                CantidadOK = Convert.ToInt32(rd["CantidadOK"]),
                CantidadSospechosa = Convert.ToInt32(rd["CantidadSospechosa"]),
                CantidadScrap = Convert.ToInt32(rd["CantidadScrap"]),
                MonitoreosCancelados = Convert.ToInt32(rd["MonitoreosCancelados"]),
                ReliberacionesCanceladas = Convert.ToInt32(rd["ReliberacionesCanceladas"]),
                InspeccionID = rd["InspeccionID"] == DBNull.Value ? null : Convert.ToInt32(rd["InspeccionID"])
            };
        }
    }
}
