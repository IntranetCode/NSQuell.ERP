using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace ERP.NSQuell.Servicios.Produccion
{
    public enum CambioMoldeModoEvaluacion
    {
        FisicoActual = 1,
        ProyeccionPlaneacion = 2
    }

    public sealed class CambioMoldeEvaluacion
    {
        public int ProgramaProduccionID { get; set; }
        public int? MaquinaID { get; set; }
        public int? ParteID { get; set; }
        public int? MoldeRequeridoID { get; set; }
        public string? MoldeRequeridoCodigo { get; set; }
        public int? MoldeActualID { get; set; }
        public string? MoldeActualCodigo { get; set; }
        public int? MoldeDestinoID { get; set; }
        public string? MoldeDestinoCodigo { get; set; }
        public int? MoldeReferenciaID { get; set; }
        public string? MoldeReferenciaCodigo { get; set; }
        public int? ProgramaReferenciaID { get; set; }
        public bool ReferenciaEsProyectada { get; set; }
        public int? GrupoLhRh { get; set; }
        public DateTime? FechaInicioProgramada { get; set; }
        public int? SecuenciaMaquina { get; set; }
        public string EstadoMaquina { get; set; } = CambioMoldeService.EstadoMaquinaVacia;
        public string? EstadoTarea { get; set; }
        public bool TareaActiva { get; set; }
        public bool DecisionCongelada { get; set; }
        public bool RequiereCambioMolde { get; set; }
        public bool TieneDatosValidos { get; set; } = true;
        public string Motivo { get; set; } = string.Empty;
    }

    public static class CambioMoldeService
    {
        public const string EstadoMaquinaVacia = "VACIA";
        public const string EstadoMaquinaMontado = "MONTADO";
        public const string EstadoMaquinaEnCambio = "EN_CAMBIO";
        public const string EstadoTareaPendiente = "PENDIENTE";
        public const string EstadoTareaEnProceso = "EN_PROCESO";
        public const string EstadoTareaConfirmada = "CONFIRMADA";

        private sealed class ReferenciaProyectada
        {
            public int ProgramaProduccionID { get; set; }
            public int? ParteID { get; set; }
            public int? MoldeID { get; set; }
            public string? MoldeCodigo { get; set; }
        }

        public static async Task<CambioMoldeEvaluacion> EvaluarProgramaAsync(int programaProduccionId, SqlConnection cn, SqlTransaction? tx = null, bool actualizarSnapshot = false, CambioMoldeModoEvaluacion modo = CambioMoldeModoEvaluacion.FisicoActual)
        {
            var resultado = await EvaluarProgramasAsync(new[] { programaProduccionId }, cn, tx, actualizarSnapshot, modo);
            if (!resultado.TryGetValue(programaProduccionId, out var evaluacion)) throw new InvalidOperationException("No se encontró el programa de Producción para evaluar el cambio de molde.");
            return evaluacion;
        }

        public static async Task<Dictionary<int, CambioMoldeEvaluacion>> EvaluarProgramasAsync(IEnumerable<int> programasProduccionId, SqlConnection cn, SqlTransaction? tx = null, bool actualizarSnapshot = false, CambioMoldeModoEvaluacion modo = CambioMoldeModoEvaluacion.ProyeccionPlaneacion)
        {
            var ids = programasProduccionId.Where(x => x > 0).Distinct().ToList();
            var resultado = new Dictionary<int, CambioMoldeEvaluacion>();
            if (ids.Count == 0) return resultado;

            var parametros = string.Join(",", ids.Select((_, i) => "@P" + i));
            var sql = $@"
SELECT
    pp.ProgramaProduccionID,
    pp.MaquinaID,
    pp.ParteID,
    pp.MoldeID AS MoldeRequeridoID,
    COALESCE(NULLIF(LTRIM(RTRIM(pp.MoldeCodigo)),N''),molReq.CodigoMolde) AS MoldeRequeridoCodigo,
    pp.FechaInicioProgramada,
    pp.SecuenciaMaquina,
    pp.RequiereCambioMolde AS RequiereGuardado,
    pp.MoldeReferenciaCambioID,
    pp.ProgramaReferenciaCambioID,
    pp.MotivoCambioMolde,
    grupo.GrupoLhRh,
    estado.MoldeActualID,
    COALESCE(NULLIF(LTRIM(RTRIM(estado.MoldeActualCodigo)),N''),molAct.CodigoMolde) AS MoldeActualCodigo,
    estado.MoldeDestinoID,
    COALESCE(NULLIF(LTRIM(RTRIM(estado.MoldeDestinoCodigo)),N''),molDest.CodigoMolde) AS MoldeDestinoCodigo,
    ISNULL(NULLIF(LTRIM(RTRIM(estado.Estado)),N''),N'VACIA') AS EstadoMaquina,
    estado.ProgramaProduccionID AS ProgramaEstadoFisicoID,
    tarea.Estado AS EstadoTarea,
    ISNULL(tarea.Activo,0) AS TareaActiva
FROM dbo.Planeacion_ProgramaProduccion pp
LEFT JOIN dbo.ERP_Moldes molReq ON molReq.MoldeID=pp.MoldeID
LEFT JOIN dbo.Produccion_MaquinaMoldeEstado estado ON estado.MaquinaID=pp.MaquinaID
LEFT JOIN dbo.ERP_Moldes molAct ON molAct.MoldeID=estado.MoldeActualID
LEFT JOIN dbo.ERP_Moldes molDest ON molDest.MoldeID=estado.MoldeDestinoID
OUTER APPLY(SELECT CHARINDEX(N'NSQ_LHRH_PAIR:',ISNULL(pp.Observaciones,N'')) AS PosGrupo) pos
OUTER APPLY
(
    SELECT CASE WHEN pos.PosGrupo>0 THEN TRY_CONVERT(int,LEFT(SUBSTRING(pp.Observaciones,pos.PosGrupo+LEN(N'NSQ_LHRH_PAIR:'),50),CHARINDEX(N';',SUBSTRING(pp.Observaciones,pos.PosGrupo+LEN(N'NSQ_LHRH_PAIR:'),50)+N';')-1)) ELSE NULL END AS GrupoLhRh
) grupo
OUTER APPLY
(
    SELECT TOP(1) pa.Estado,pa.Activo
    FROM dbo.Produccion_PreparacionAnticipada pa
    WHERE pa.ProgramaProduccionID=pp.ProgramaProduccionID
      AND UPPER(LTRIM(RTRIM(pa.TipoTarea)))=N'CAMBIO_MOLDE'
    ORDER BY CASE WHEN pa.Activo=1 THEN 0 ELSE 1 END,pa.PreparacionAnticipadaID DESC
) tarea
WHERE pp.ProgramaProduccionID IN({parametros});";

            await using (var cmd = tx == null ? new SqlCommand(sql, cn) : new SqlCommand(sql, cn, tx))
            {
                for (var i = 0; i < ids.Count; i++) cmd.Parameters.Add("@P" + i, SqlDbType.Int).Value = ids[i];
                await using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                {
                    var programaId = Convert.ToInt32(rd["ProgramaProduccionID"]);
                    var maquinaId = NullableInt(rd, "MaquinaID");
                    var requeridoId = NullableInt(rd, "MoldeRequeridoID");
                    var requeridoCodigo = Texto(rd, "MoldeRequeridoCodigo");
                    var actualId = NullableInt(rd, "MoldeActualID");
                    var actualCodigo = Texto(rd, "MoldeActualCodigo");
                    var destinoId = NullableInt(rd, "MoldeDestinoID");
                    var destinoCodigo = Texto(rd, "MoldeDestinoCodigo");
                    var estadoTarea = Texto(rd, "EstadoTarea")?.ToUpperInvariant();
                    var tareaActiva = rd["TareaActiva"] != DBNull.Value && Convert.ToBoolean(rd["TareaActiva"]);
                    var congelada = tareaActiva && (string.Equals(estadoTarea, EstadoTareaEnProceso, StringComparison.OrdinalIgnoreCase) || string.Equals(estadoTarea, EstadoTareaConfirmada, StringComparison.OrdinalIgnoreCase));
                    var evaluacion = new CambioMoldeEvaluacion
                    {
                        ProgramaProduccionID = programaId,
                        MaquinaID = maquinaId,
                        ParteID = NullableInt(rd, "ParteID"),
                        MoldeRequeridoID = requeridoId,
                        MoldeRequeridoCodigo = requeridoCodigo,
                        MoldeActualID = actualId,
                        MoldeActualCodigo = actualCodigo,
                        MoldeDestinoID = destinoId,
                        MoldeDestinoCodigo = destinoCodigo,
                        MoldeReferenciaID = actualId,
                        MoldeReferenciaCodigo = actualCodigo,
                        ProgramaReferenciaID = NullableInt(rd, "ProgramaEstadoFisicoID"),
                        GrupoLhRh = NullableInt(rd, "GrupoLhRh"),
                        FechaInicioProgramada = NullableDateTime(rd, "FechaInicioProgramada"),
                        SecuenciaMaquina = NullableInt(rd, "SecuenciaMaquina"),
                        EstadoMaquina = Texto(rd, "EstadoMaquina")?.ToUpperInvariant() ?? EstadoMaquinaVacia,
                        EstadoTarea = estadoTarea,
                        TareaActiva = tareaActiva,
                        DecisionCongelada = congelada
                    };

                    if (congelada)
                    {
                        evaluacion.MoldeReferenciaID = NullableInt(rd, "MoldeReferenciaCambioID") ?? actualId;
                        evaluacion.ProgramaReferenciaID = NullableInt(rd, "ProgramaReferenciaCambioID") ?? evaluacion.ProgramaReferenciaID;
                        evaluacion.RequiereCambioMolde = rd["RequiereGuardado"] == DBNull.Value || Convert.ToBoolean(rd["RequiereGuardado"]);
                        evaluacion.Motivo = Texto(rd, "MotivoCambioMolde") ?? (string.Equals(estadoTarea, EstadoTareaConfirmada, StringComparison.OrdinalIgnoreCase) ? "La decisión quedó congelada porque el cambio de molde ya fue CONFIRMADO." : "La decisión quedó congelada porque el cambio de molde ya está EN PROCESO.");
                    }

                    resultado[programaId] = evaluacion;
                }
            }

            foreach (var evaluacion in resultado.Values.Where(x => !x.DecisionCongelada))
            {
                if (!evaluacion.MaquinaID.HasValue || evaluacion.MaquinaID.Value <= 0)
                {
                    evaluacion.TieneDatosValidos = false;
                    evaluacion.RequiereCambioMolde = true;
                    evaluacion.Motivo = "La OF no tiene una máquina válida asignada. No es posible determinar el cambio de molde.";
                    continue;
                }
                if (!evaluacion.MoldeRequeridoID.HasValue || evaluacion.MoldeRequeridoID.Value <= 0)
                {
                    evaluacion.TieneDatosValidos = false;
                    evaluacion.RequiereCambioMolde = true;
                    evaluacion.Motivo = "La OF no tiene MoldeID definido en Planeación/datos técnicos. Corrige el dato maestro antes de continuar.";
                    continue;
                }

                if (modo == CambioMoldeModoEvaluacion.ProyeccionPlaneacion && evaluacion.FechaInicioProgramada.HasValue)
                {
                    var anterior = await ObtenerProgramaAnteriorProyectadoAsync(evaluacion, cn, tx);
                    if (anterior != null)
                    {
                        evaluacion.ReferenciaEsProyectada = true;
                        evaluacion.MoldeReferenciaID = anterior.MoldeID;
                        evaluacion.MoldeReferenciaCodigo = anterior.MoldeCodigo;
                        evaluacion.ProgramaReferenciaID = anterior.ProgramaProduccionID;
                        var mismaParte = evaluacion.ParteID.HasValue && anterior.ParteID.HasValue && evaluacion.ParteID.Value == anterior.ParteID.Value;
                        var mismoMolde = evaluacion.MoldeRequeridoID.HasValue && anterior.MoldeID.HasValue && evaluacion.MoldeRequeridoID.Value == anterior.MoldeID.Value;
                        evaluacion.RequiereCambioMolde = !(mismaParte || mismoMolde);
                        evaluacion.Motivo = mismaParte
                            ? "La OF anterior proyectada corresponde a la misma pieza; se conserva la regla actual de Planeación y no se programa cambio de molde."
                            : mismoMolde
                                ? $"La OF anterior proyectada deja el mismo molde {evaluacion.MoldeRequeridoCodigo ?? evaluacion.MoldeRequeridoID.Value.ToString()}; no se programa cambio."
                                : $"La OF anterior proyectada deja el molde {anterior.MoldeCodigo ?? anterior.MoldeID?.ToString() ?? "sin identificar"} y esta OF requiere {evaluacion.MoldeRequeridoCodigo ?? evaluacion.MoldeRequeridoID.Value.ToString()}.";
                        continue;
                    }
                }

                evaluacion.ReferenciaEsProyectada = false;
                evaluacion.MoldeReferenciaID = evaluacion.MoldeActualID;
                evaluacion.MoldeReferenciaCodigo = evaluacion.MoldeActualCodigo;
                if (!evaluacion.MoldeActualID.HasValue || evaluacion.MoldeActualID.Value <= 0)
                {
                    evaluacion.RequiereCambioMolde = true;
                    evaluacion.Motivo = $"La máquina está vacía o no tiene molde físico registrado. Debe montarse el molde {evaluacion.MoldeRequeridoCodigo ?? evaluacion.MoldeRequeridoID.Value.ToString()}.";
                }
                else if (evaluacion.MoldeActualID.Value == evaluacion.MoldeRequeridoID.Value)
                {
                    evaluacion.RequiereCambioMolde = false;
                    evaluacion.Motivo = $"La máquina ya tiene físicamente montado el molde requerido {evaluacion.MoldeRequeridoCodigo ?? evaluacion.MoldeRequeridoID.Value.ToString()}. No requiere cambio.";
                }
                else
                {
                    evaluacion.RequiereCambioMolde = true;
                    evaluacion.Motivo = $"La máquina tiene físicamente el molde {evaluacion.MoldeActualCodigo ?? evaluacion.MoldeActualID.Value.ToString()} y la OF requiere {evaluacion.MoldeRequeridoCodigo ?? evaluacion.MoldeRequeridoID.Value.ToString()}.";
                }
            }

            if (actualizarSnapshot)
            {
                foreach (var evaluacion in resultado.Values)
                    if (!evaluacion.DecisionCongelada) await ActualizarSnapshotProgramaAsync(evaluacion, cn, tx);
            }
            return resultado;
        }

        public static async Task MarcarInicioCambioAsync(int programaProduccionId, int? ejecucionProduccionId, int usuarioId, SqlConnection cn, SqlTransaction tx)
        {
            var evaluacion = await EvaluarProgramaAsync(programaProduccionId, cn, tx, actualizarSnapshot: true, modo: CambioMoldeModoEvaluacion.FisicoActual);
            if (!evaluacion.TieneDatosValidos) throw new InvalidOperationException(evaluacion.Motivo);
            if (!evaluacion.MaquinaID.HasValue || !evaluacion.MoldeRequeridoID.HasValue) throw new InvalidOperationException("No fue posible resolver máquina y molde requerido para iniciar el cambio.");
            if (!evaluacion.RequiereCambioMolde) throw new InvalidOperationException("La fuente física indica que esta OF ya tiene montado el molde requerido y no necesita cambio de molde.");

            const string sql = @"
IF EXISTS(SELECT 1 FROM dbo.Produccion_MaquinaMoldeEstado WITH(UPDLOCK,HOLDLOCK) WHERE MaquinaID=@MaquinaID)
BEGIN
    IF EXISTS
    (
        SELECT 1 FROM dbo.Produccion_MaquinaMoldeEstado
        WHERE MaquinaID=@MaquinaID AND Estado=N'EN_CAMBIO' AND MoldeDestinoID IS NOT NULL AND MoldeDestinoID<>@MoldeDestinoID
    )
        THROW 51201,N'La máquina ya tiene otro cambio de molde físico EN PROCESO hacia un molde diferente.',1;
    UPDATE dbo.Produccion_MaquinaMoldeEstado
    SET Estado=N'EN_CAMBIO',MoldeDestinoID=@MoldeDestinoID,MoldeDestinoCodigo=@MoldeDestinoCodigo,ProgramaProduccionID=@ProgramaProduccionID,EjecucionProduccionID=@EjecucionProduccionID,GrupoLhRh=@GrupoLhRh,FechaUltimoMovimiento=SYSDATETIME(),UsuarioUltimoMovimientoID=@UsuarioID,Observaciones=LEFT(N'Inicio físico de cambio de molde. '+@Motivo,500)
    WHERE MaquinaID=@MaquinaID;
END
ELSE
BEGIN
    INSERT INTO dbo.Produccion_MaquinaMoldeEstado(MaquinaID,MoldeActualID,MoldeActualCodigo,Estado,MoldeDestinoID,MoldeDestinoCodigo,ProgramaProduccionID,EjecucionProduccionID,GrupoLhRh,FechaUltimoMovimiento,UsuarioUltimoMovimientoID,Observaciones)
    VALUES(@MaquinaID,NULL,NULL,N'EN_CAMBIO',@MoldeDestinoID,@MoldeDestinoCodigo,@ProgramaProduccionID,@EjecucionProduccionID,@GrupoLhRh,SYSDATETIME(),@UsuarioID,LEFT(N'Inicio físico de cambio de molde. '+@Motivo,500));
END;";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = evaluacion.MaquinaID.Value;
            cmd.Parameters.Add("@MoldeDestinoID", SqlDbType.Int).Value = evaluacion.MoldeRequeridoID.Value;
            cmd.Parameters.Add("@MoldeDestinoCodigo", SqlDbType.NVarChar, 100).Value = (object?)evaluacion.MoldeRequeridoCodigo ?? DBNull.Value;
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = (object?)ejecucionProduccionId ?? DBNull.Value;
            cmd.Parameters.Add("@GrupoLhRh", SqlDbType.Int).Value = (object?)evaluacion.GrupoLhRh ?? DBNull.Value;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            cmd.Parameters.Add("@Motivo", SqlDbType.NVarChar, 500).Value = evaluacion.Motivo;
            await cmd.ExecuteNonQueryAsync();
        }

        public static async Task ConfirmarCambioAsync(int programaProduccionId, int? ejecucionProduccionId, int usuarioId, SqlConnection cn, SqlTransaction tx)
        {
            var evaluacion = await EvaluarProgramaAsync(programaProduccionId, cn, tx, actualizarSnapshot: false, modo: CambioMoldeModoEvaluacion.FisicoActual);
            if (!evaluacion.MaquinaID.HasValue || evaluacion.MaquinaID.Value <= 0) throw new InvalidOperationException("La OF no tiene una máquina válida para confirmar el cambio de molde.");
            if (!evaluacion.MoldeRequeridoID.HasValue || evaluacion.MoldeRequeridoID.Value <= 0) throw new InvalidOperationException("La OF no tiene MoldeID requerido para confirmar el cambio de molde.");

            const string sql = @"
IF EXISTS(SELECT 1 FROM dbo.Produccion_MaquinaMoldeEstado WITH(UPDLOCK,HOLDLOCK) WHERE MaquinaID=@MaquinaID)
BEGIN
    IF EXISTS
    (
        SELECT 1 FROM dbo.Produccion_MaquinaMoldeEstado
        WHERE MaquinaID=@MaquinaID AND Estado=N'EN_CAMBIO' AND MoldeDestinoID IS NOT NULL AND MoldeDestinoID<>@MoldeID
    )
        THROW 51202,N'El molde destino físico registrado para la máquina no coincide con el molde de esta OF.',1;
    UPDATE dbo.Produccion_MaquinaMoldeEstado
    SET MoldeActualID=@MoldeID,MoldeActualCodigo=@MoldeCodigo,Estado=N'MONTADO',MoldeDestinoID=NULL,MoldeDestinoCodigo=NULL,ProgramaProduccionID=@ProgramaProduccionID,EjecucionProduccionID=@EjecucionProduccionID,GrupoLhRh=@GrupoLhRh,FechaUltimoMovimiento=SYSDATETIME(),UsuarioUltimoMovimientoID=@UsuarioID,Observaciones=LEFT(N'Cambio de molde confirmado físicamente. Molde montado: '+ISNULL(@MoldeCodigo,CONVERT(nvarchar(20),@MoldeID))+N'.',500)
    WHERE MaquinaID=@MaquinaID;
END
ELSE
BEGIN
    INSERT INTO dbo.Produccion_MaquinaMoldeEstado(MaquinaID,MoldeActualID,MoldeActualCodigo,Estado,MoldeDestinoID,MoldeDestinoCodigo,ProgramaProduccionID,EjecucionProduccionID,GrupoLhRh,FechaUltimoMovimiento,UsuarioUltimoMovimientoID,Observaciones)
    VALUES(@MaquinaID,@MoldeID,@MoldeCodigo,N'MONTADO',NULL,NULL,@ProgramaProduccionID,@EjecucionProduccionID,@GrupoLhRh,SYSDATETIME(),@UsuarioID,LEFT(N'Cambio de molde confirmado físicamente. Molde montado: '+ISNULL(@MoldeCodigo,CONVERT(nvarchar(20),@MoldeID))+N'.',500));
END;";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = evaluacion.MaquinaID.Value;
            cmd.Parameters.Add("@MoldeID", SqlDbType.Int).Value = evaluacion.MoldeRequeridoID.Value;
            cmd.Parameters.Add("@MoldeCodigo", SqlDbType.NVarChar, 100).Value = (object?)evaluacion.MoldeRequeridoCodigo ?? DBNull.Value;
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = (object?)ejecucionProduccionId ?? DBNull.Value;
            cmd.Parameters.Add("@GrupoLhRh", SqlDbType.Int).Value = (object?)evaluacion.GrupoLhRh ?? DBNull.Value;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task<ReferenciaProyectada?> ObtenerProgramaAnteriorProyectadoAsync(CambioMoldeEvaluacion actual, SqlConnection cn, SqlTransaction? tx)
        {
            const string sql = @"
SELECT TOP(1)
    ant.ProgramaProduccionID,
    ant.ParteID,
    ant.MoldeID,
    COALESCE(NULLIF(LTRIM(RTRIM(ant.MoldeCodigo)),N''),mol.CodigoMolde) AS MoldeCodigo
FROM dbo.Planeacion_ProgramaProduccion ant
LEFT JOIN dbo.ERP_Moldes mol ON mol.MoldeID=ant.MoldeID
WHERE ant.Activo=1
  AND ant.MaquinaID=@MaquinaID
  AND ant.ProgramaProduccionID<>@ProgramaProduccionID
  AND ant.FechaInicioProgramada IS NOT NULL
  AND ISNULL(ant.EstatusID,1)=1
  AND
  (
      ant.FechaInicioProgramada<@FechaInicio
      OR
      (
          ant.FechaInicioProgramada=@FechaInicio
          AND ISNULL(ant.SecuenciaMaquina,999999)<@SecuenciaMaquina
      )
      OR
      (
          ant.FechaInicioProgramada=@FechaInicio
          AND ISNULL(ant.SecuenciaMaquina,999999)=@SecuenciaMaquina
          AND ant.ProgramaProduccionID<@ProgramaProduccionID
      )
  )
  AND
  (
      @GrupoLhRh IS NULL
      OR ISNULL(ant.Observaciones,N'') NOT LIKE N'%NSQ_LHRH_PAIR:'+CONVERT(nvarchar(20),@GrupoLhRh)+N';%'
  )
ORDER BY ant.FechaInicioProgramada DESC,ISNULL(ant.SecuenciaMaquina,999999) DESC,ant.ProgramaProduccionID DESC;";
            await using var cmd = tx == null ? new SqlCommand(sql, cn) : new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = actual.MaquinaID!.Value;
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = actual.ProgramaProduccionID;
            cmd.Parameters.Add("@FechaInicio", SqlDbType.DateTime2).Value = actual.FechaInicioProgramada!.Value;
            cmd.Parameters.Add("@SecuenciaMaquina", SqlDbType.Int).Value = actual.SecuenciaMaquina ?? 999999;
            cmd.Parameters.Add("@GrupoLhRh", SqlDbType.Int).Value = (object?)actual.GrupoLhRh ?? DBNull.Value;
            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync()) return null;
            return new ReferenciaProyectada
            {
                ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),
                ParteID = NullableInt(rd, "ParteID"),
                MoldeID = NullableInt(rd, "MoldeID"),
                MoldeCodigo = Texto(rd, "MoldeCodigo")
            };
        }

        private static async Task ActualizarSnapshotProgramaAsync(CambioMoldeEvaluacion evaluacion, SqlConnection cn, SqlTransaction? tx)
        {
            const string sql = @"
UPDATE pp
SET RequiereCambioMolde=@RequiereCambioMolde,
    MoldeReferenciaCambioID=@MoldeReferenciaCambioID,
    ProgramaReferenciaCambioID=@ProgramaReferenciaCambioID,
    FechaEvaluacionCambioMolde=SYSDATETIME(),
    MotivoCambioMolde=LEFT(@Motivo,500)
FROM dbo.Planeacion_ProgramaProduccion pp
WHERE pp.ProgramaProduccionID=@ProgramaProduccionID
   OR
   (
       @GrupoLhRh IS NOT NULL
       AND pp.Activo=1
       AND pp.MaquinaID=@MaquinaID
       AND pp.MoldeID=@MoldeRequeridoID
       AND ISNULL(pp.Observaciones,N'') LIKE N'%NSQ_LHRH_PAIR:'+CONVERT(nvarchar(20),@GrupoLhRh)+N';%'
   );";
            await using var cmd = tx == null ? new SqlCommand(sql, cn) : new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@RequiereCambioMolde", SqlDbType.Bit).Value = evaluacion.RequiereCambioMolde;
            cmd.Parameters.Add("@MoldeReferenciaCambioID", SqlDbType.Int).Value = (object?)evaluacion.MoldeReferenciaID ?? DBNull.Value;
            cmd.Parameters.Add("@ProgramaReferenciaCambioID", SqlDbType.Int).Value = (object?)evaluacion.ProgramaReferenciaID ?? DBNull.Value;
            cmd.Parameters.Add("@Motivo", SqlDbType.NVarChar, 500).Value = evaluacion.Motivo;
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = evaluacion.ProgramaProduccionID;
            cmd.Parameters.Add("@GrupoLhRh", SqlDbType.Int).Value = (object?)evaluacion.GrupoLhRh ?? DBNull.Value;
            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = (object?)evaluacion.MaquinaID ?? DBNull.Value;
            cmd.Parameters.Add("@MoldeRequeridoID", SqlDbType.Int).Value = (object?)evaluacion.MoldeRequeridoID ?? DBNull.Value;
            await cmd.ExecuteNonQueryAsync();
        }

        private static int? NullableInt(SqlDataReader rd, string columna) => rd[columna] == DBNull.Value ? null : Convert.ToInt32(rd[columna]);
        private static DateTime? NullableDateTime(SqlDataReader rd, string columna) => rd[columna] == DBNull.Value ? null : Convert.ToDateTime(rd[columna]);
        private static string? Texto(SqlDataReader rd, string columna) => rd[columna] == DBNull.Value ? null : rd[columna]?.ToString()?.Trim();
    }
}
