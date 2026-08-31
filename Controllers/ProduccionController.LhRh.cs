using ERP.NSQuell.Models;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers
{
    public sealed partial class ProduccionController
    {
        private sealed class BloqueoMaquinaInicioLhRh
        {
            public int EjecucionProduccionID { get; set; }
            public int ProgramaProduccionID { get; set; }
            public int EstatusID { get; set; }
            public string? MaquinaCodigo { get; set; }
            public string? OperadorNombre { get; set; }
            public DateTime? FechaInicioReal { get; set; }
        }

        private async Task<ProduccionParejaLhRhVm?> ObtenerParejaLhRhProduccionAsync(int programaProduccionId, SqlConnection cn, SqlTransaction? tx = null)
        {
            if (programaProduccionId <= 0) return null;

            const string sql = @"
WITH Origen AS
(
    SELECT
        pp.ProgramaProduccionID,
        pp.MaquinaID,
        pp.MoldeID,
        pp.MoldeCodigo,
        pp.FechaInicioProgramada,
        pp.FechaFinProgramada,
        grupo.GrupoLhRh
    FROM dbo.Planeacion_ProgramaProduccion pp
    CROSS APPLY
    (
        SELECT CHARINDEX(N'NSQ_LHRH_PAIR:',ISNULL(pp.Observaciones,N'')) AS PosGrupo
    ) posicion
    CROSS APPLY
    (
        SELECT TRY_CONVERT
        (
            INT,
            LEFT
            (
                SUBSTRING(pp.Observaciones,posicion.PosGrupo+LEN(N'NSQ_LHRH_PAIR:'),50),
                CHARINDEX(N';',SUBSTRING(pp.Observaciones,posicion.PosGrupo+LEN(N'NSQ_LHRH_PAIR:'),50)+N';')-1
            )
        ) AS GrupoLhRh
    ) grupo
    WHERE pp.ProgramaProduccionID=@ProgramaProduccionID
      AND pp.Activo=1
      AND posicion.PosGrupo>0
      AND grupo.GrupoLhRh IS NOT NULL
)
SELECT
    o.GrupoLhRh,
    o.ProgramaProduccionID,
    pareja.ProgramaProduccionID AS ProgramaParejaID,
    pareja.SolicitudProduccionID AS SolicitudProduccionParejaID,
    s.FolioSolicitud AS FolioSolicitudPareja,
    s.NumeroOFRecibida AS NumeroOFPareja,
    ejecucion.EjecucionProduccionID AS EjecucionParejaID,
    ejecucion.EstatusID AS EstatusEjecucionParejaID,
    ISNULL(pareja.EstatusID,1) AS EstatusProgramaParejaID,
    pareja.ParteID AS ParteParejaID,
    pareja.NumeroParte AS NumeroPartePareja,
    pareja.ReferenciaSAP AS ReferenciaSAPPareja,
    pareja.DesignacionDescripcionSAP AS DescripcionPartePareja,
    pareja.MaquinaID AS MaquinaParejaID,
    COALESCE(NULLIF(pareja.MaquinaCodigo,N''),maquina.Codigo) AS MaquinaParejaCodigo,
    COALESCE(NULLIF(pareja.MaquinaNombre,N''),maquina.Nombre) AS MaquinaParejaNombre,
    pareja.MoldeID AS MoldeParejaID,
    pareja.MoldeCodigo AS MoldeParejaCodigo,
    CONVERT(INT,ISNULL(pareja.CantidadProgramada,0)) AS CantidadProgramadaPareja,
    CONVERT(INT,ISNULL(ejecucion.CantidadOKTotal,ISNULL(pareja.CantidadProducida,0))) AS CantidadProducidaPareja,
    pareja.FechaInicioProgramada AS FechaInicioProgramadaPareja,
    pareja.FechaFinProgramada AS FechaFinProgramadaPareja,
    CONVERT(bit,CASE WHEN o.MaquinaID IS NOT NULL AND pareja.MaquinaID=o.MaquinaID THEN 1 ELSE 0 END) AS MismaMaquina,
    CONVERT(bit,
        CASE
            WHEN o.MoldeID IS NOT NULL AND pareja.MoldeID IS NOT NULL
                THEN CASE WHEN o.MoldeID=pareja.MoldeID THEN 1 ELSE 0 END
            WHEN NULLIF(LTRIM(RTRIM(ISNULL(o.MoldeCodigo,N''))),N'') IS NOT NULL
             AND UPPER(LTRIM(RTRIM(ISNULL(o.MoldeCodigo,N''))))=
                 UPPER(LTRIM(RTRIM(ISNULL(pareja.MoldeCodigo,N''))))
                THEN 1
            ELSE 0
        END
    ) AS MismoMolde,
    CONVERT(bit,
        CASE
            WHEN o.FechaInicioProgramada=pareja.FechaInicioProgramada
             AND
             (
                    o.FechaFinProgramada=pareja.FechaFinProgramada
                 OR (o.FechaFinProgramada IS NULL AND pareja.FechaFinProgramada IS NULL)
             )
                THEN 1
            ELSE 0
        END
    ) AS MismaVentanaProgramada
FROM Origen o
INNER JOIN dbo.Planeacion_ProgramaProduccion pareja
    ON pareja.Activo=1
   AND pareja.ProgramaProduccionID<>o.ProgramaProduccionID
   AND pareja.Observaciones LIKE N'%NSQ_LHRH_PAIR:'+CONVERT(NVARCHAR(20),o.GrupoLhRh)+N';%'
LEFT JOIN dbo.SolicitudesProduccion s
    ON s.SolicitudProduccionID=pareja.SolicitudProduccionID
   AND s.Activo=1
LEFT JOIN dbo.ERP_Maquinas maquina
    ON maquina.MaquinaID=pareja.MaquinaID
OUTER APPLY
(
    SELECT TOP(1)
        e.EjecucionProduccionID,
        e.EstatusID,
        e.CantidadOKTotal
    FROM dbo.Produccion_Ejecucion e
    WHERE e.ProgramaProduccionID=pareja.ProgramaProduccionID
      AND e.Activo=1
      AND e.EstatusID NOT IN(9,99)
    ORDER BY e.EjecucionProduccionID DESC
) ejecucion
ORDER BY pareja.ProgramaProduccionID;";

            await using var cmd = tx == null ? new SqlCommand(sql, cn) : new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;

            var resultados = new List<ProduccionParejaLhRhVm>();

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                resultados.Add(new ProduccionParejaLhRhVm
                {
                    GrupoLhRh = Convert.ToInt32(rd["GrupoLhRh"]),
                    ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),
                    ProgramaParejaID = Convert.ToInt32(rd["ProgramaParejaID"]),
                    SolicitudProduccionParejaID = rd["SolicitudProduccionParejaID"] == DBNull.Value ? null : Convert.ToInt32(rd["SolicitudProduccionParejaID"]),
                    FolioSolicitudPareja = rd["FolioSolicitudPareja"] == DBNull.Value ? null : rd["FolioSolicitudPareja"]?.ToString()?.Trim(),
                    NumeroOFPareja = rd["NumeroOFPareja"] == DBNull.Value ? null : rd["NumeroOFPareja"]?.ToString()?.Trim(),
                    EjecucionParejaID = rd["EjecucionParejaID"] == DBNull.Value ? null : Convert.ToInt32(rd["EjecucionParejaID"]),
                    EstatusEjecucionParejaID = rd["EstatusEjecucionParejaID"] == DBNull.Value ? null : Convert.ToInt32(rd["EstatusEjecucionParejaID"]),
                    EstatusProgramaParejaID = Convert.ToInt32(rd["EstatusProgramaParejaID"]),
                    ParteParejaID = rd["ParteParejaID"] == DBNull.Value ? null : Convert.ToInt32(rd["ParteParejaID"]),
                    NumeroPartePareja = rd["NumeroPartePareja"] == DBNull.Value ? null : rd["NumeroPartePareja"]?.ToString()?.Trim(),
                    ReferenciaSAPPareja = rd["ReferenciaSAPPareja"] == DBNull.Value ? null : rd["ReferenciaSAPPareja"]?.ToString()?.Trim(),
                    DescripcionPartePareja = rd["DescripcionPartePareja"] == DBNull.Value ? null : rd["DescripcionPartePareja"]?.ToString()?.Trim(),
                    MaquinaParejaID = rd["MaquinaParejaID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaquinaParejaID"]),
                    MaquinaParejaCodigo = rd["MaquinaParejaCodigo"] == DBNull.Value ? null : rd["MaquinaParejaCodigo"]?.ToString()?.Trim(),
                    MaquinaParejaNombre = rd["MaquinaParejaNombre"] == DBNull.Value ? null : rd["MaquinaParejaNombre"]?.ToString()?.Trim(),
                    MoldeParejaID = rd["MoldeParejaID"] == DBNull.Value ? null : Convert.ToInt32(rd["MoldeParejaID"]),
                    MoldeParejaCodigo = rd["MoldeParejaCodigo"] == DBNull.Value ? null : rd["MoldeParejaCodigo"]?.ToString()?.Trim(),
                    CantidadProgramadaPareja = Convert.ToInt32(rd["CantidadProgramadaPareja"]),
                    CantidadProducidaPareja = Convert.ToInt32(rd["CantidadProducidaPareja"]),
                    FechaInicioProgramadaPareja = rd["FechaInicioProgramadaPareja"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaInicioProgramadaPareja"]),
                    FechaFinProgramadaPareja = rd["FechaFinProgramadaPareja"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaFinProgramadaPareja"]),
                    MismaMaquina = Convert.ToBoolean(rd["MismaMaquina"]),
                    MismoMolde = Convert.ToBoolean(rd["MismoMolde"]),
                    MismaVentanaProgramada = Convert.ToBoolean(rd["MismaVentanaProgramada"])
                });
            }

            if (resultados.Count > 1)
                throw new InvalidOperationException($"El Programa {programaProduccionId} tiene más de una contraparte con el mismo grupo LH/RH. Revisa Planeación antes de continuar.");

            return resultados.FirstOrDefault();
        }

        private static void ValidarParejaLhRhParaInicio(ProduccionParejaLhRhVm? pareja)
        {
            if (pareja == null) return;

            if (!pareja.MismaMaquina)
                throw new InvalidOperationException($"La pareja LH/RH está inconsistente: el Programa {pareja.ProgramaParejaID} no tiene la misma máquina. Corrige Planeación antes de iniciar Producción.");

            if (!pareja.MismoMolde)
                throw new InvalidOperationException($"La pareja LH/RH está inconsistente: el Programa {pareja.ProgramaParejaID} no tiene el mismo molde. Corrige Planeación antes de iniciar Producción.");

            if (!pareja.MismaVentanaProgramada)
                throw new InvalidOperationException($"La pareja LH/RH está inconsistente: el Programa {pareja.ProgramaParejaID} no conserva la misma ventana de producción. Corrige Planeación antes de iniciar Producción.");

            if (pareja.ParejaEnProduccion)
                throw new InvalidOperationException($"La OF pareja {pareja.OFParejaTexto} ya se encuentra produciendo. No se permitirá incorporar la segunda OF después de haber iniciado serie; ambas deben completar Preparación y Calidad antes del arranque conjunto.");

            if (pareja.ParejaPausada)
                throw new InvalidOperationException($"La OF pareja {pareja.OFParejaTexto} está pausada. No puede iniciarse la otra OF mientras exista una pausa física en la producción conjunta.");
        }


        private async Task<BloqueoMaquinaInicioLhRh?> ObtenerBloqueoMaquinaParaInicioAsync(int programaProduccionId, int maquinaId, int? programaParejaLhRhId, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
SELECT TOP(1)
    e.EjecucionProduccionID,
    e.ProgramaProduccionID,
    e.MaquinaCodigo,
    e.OperadorNombre,
    e.EstatusID,
    e.FechaInicioReal
FROM dbo.Produccion_Ejecucion e WITH(UPDLOCK,HOLDLOCK)
WHERE e.Activo=1
  AND e.MaquinaID=@MaquinaID
  AND e.ProgramaProduccionID<>@ProgramaProduccionID
  AND (@ProgramaParejaLhRhID IS NULL OR e.ProgramaProduccionID<>@ProgramaParejaLhRhID)
  AND e.FechaLiberacionMaquina IS NULL
  AND e.EstatusID IN(@EnPreparacion,@EnProduccion,@Pausado)
  AND NOT
  (
      e.EstatusID=@Pausado
      AND EXISTS
      (
          SELECT 1
          FROM dbo.Produccion_Paros p WITH(UPDLOCK,HOLDLOCK)
          WHERE p.EjecucionProduccionID=e.EjecucionProduccionID
            AND p.ProgramaProduccionID=e.ProgramaProduccionID
            AND p.MaquinaID=e.MaquinaID
            AND p.Activo=1
            AND p.FechaFinParo IS NULL
            AND ISNULL(p.EsInterrupcionUrgente,0)=1
            AND
            (
                p.ProgramaUrgenteID=@ProgramaProduccionID
                OR
                (
                    @ProgramaParejaLhRhID IS NOT NULL
                    AND p.ProgramaUrgenteID=@ProgramaParejaLhRhID
                )
            )
      )
  )
ORDER BY e.EjecucionProduccionID DESC;";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = maquinaId;
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;
            cmd.Parameters.Add("@ProgramaParejaLhRhID", SqlDbType.Int).Value = (object?)programaParejaLhRhId ?? DBNull.Value;
            cmd.Parameters.Add("@EnPreparacion", SqlDbType.Int).Value = ProduccionEstatus.EnPreparacion;
            cmd.Parameters.Add("@EnProduccion", SqlDbType.Int).Value = ProduccionEstatus.EnProduccion;
            cmd.Parameters.Add("@Pausado", SqlDbType.Int).Value = ProduccionEstatus.Pausado;
            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync()) return null;
            return new BloqueoMaquinaInicioLhRh
            {
                EjecucionProduccionID = Convert.ToInt32(rd["EjecucionProduccionID"]),
                ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),
                MaquinaCodigo = rd["MaquinaCodigo"] == DBNull.Value ? null : rd["MaquinaCodigo"]?.ToString()?.Trim(),
                OperadorNombre = rd["OperadorNombre"] == DBNull.Value ? null : rd["OperadorNombre"]?.ToString()?.Trim(),
                EstatusID = Convert.ToInt32(rd["EstatusID"]),
                FechaInicioReal = rd["FechaInicioReal"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaInicioReal"])
            };
        }
        private static string ConstruirMensajeBloqueoMaquinaInicio(BloqueoMaquinaInicioLhRh bloqueo, string? maquinaPrograma)
        {
            var maquinaTexto = !string.IsNullOrWhiteSpace(bloqueo.MaquinaCodigo) ? bloqueo.MaquinaCodigo.Trim() : !string.IsNullOrWhiteSpace(maquinaPrograma) ? maquinaPrograma.Trim() : "seleccionada";
            var estatusTexto = bloqueo.EstatusID switch
            {
                ProduccionEstatus.EnPreparacion => "en preparación",
                ProduccionEstatus.EnProduccion => "en producción",
                ProduccionEstatus.Pausado => "pausada",
                _ => "activa"
            };
            var mensaje = $"No puedes iniciar esta OF porque la máquina {maquinaTexto} está ocupada por el Programa {bloqueo.ProgramaProduccionID}, ejecución {bloqueo.EjecucionProduccionID}, actualmente {estatusTexto}.";
            if (!string.IsNullOrWhiteSpace(bloqueo.OperadorNombre)) mensaje += " Operador: " + bloqueo.OperadorNombre.Trim() + ".";
            if (bloqueo.FechaInicioReal.HasValue) mensaje += " Inicio real: " + bloqueo.FechaInicioReal.Value.ToString("dd/MM/yyyy HH:mm") + ".";
            mensaje += " Termina o libera la ejecución anterior antes de iniciar la siguiente preparación.";
            return mensaje;
        }

        private static bool CoincidenOperadoresLhRh(ProduccionEjecucionVm origen, ProduccionEjecucionVm pareja)
        {
            if (origen.OperadorID.HasValue && pareja.OperadorID.HasValue)
                return origen.OperadorID.Value == pareja.OperadorID.Value;

            var nombreOrigen = origen.OperadorNombre?.Trim();
            var nombrePareja = pareja.OperadorNombre?.Trim();

            return !string.IsNullOrWhiteSpace(nombreOrigen) &&
                   !string.IsNullOrWhiteSpace(nombrePareja) &&
                   string.Equals(nombreOrigen, nombrePareja, StringComparison.OrdinalIgnoreCase);
        }

        private sealed class ContextoReinicioLhRhInterno
        {
            public Guid GrupoParoLhRh { get; set; }
            public int ParoOrigenID { get; set; }
            public int ParoParejaID { get; set; }
            public DateTime FechaInicioFisica { get; set; }
            public DateTime FechaFinFisica { get; set; }
        }

        private sealed class ParoLhRhAbiertoInterno
        {
            public int ParoID { get; set; }
            public int EjecucionProduccionID { get; set; }
            public int ProgramaProduccionID { get; set; }
            public DateTime FechaInicioParo { get; set; }
            public bool EsInterrupcionUrgente { get; set; }
        }

        private async Task<int> InsertarParoProduccionInternoAsync(ProduccionEjecucionVm ejecucion, int? motivoParoId, string? motivoParoTexto, string? descripcion, DateTime fechaInicioParo, Guid? grupoParoLhRh, bool esParoLhRh, int usuarioId, SqlConnection cn, SqlTransaction tx)
        {
            motivoParoTexto = string.IsNullOrWhiteSpace(motivoParoTexto) ? null : motivoParoTexto.Trim();
            descripcion = string.IsNullOrWhiteSpace(descripcion) ? null : descripcion.Trim();

            if (motivoParoTexto?.Length > 200) throw new InvalidOperationException("El motivo del paro no puede superar 200 caracteres.");
            if (descripcion?.Length > 500) throw new InvalidOperationException("La descripción del paro no puede superar 500 caracteres.");

            const string sql = @"
INSERT INTO dbo.Produccion_Paros
(
    EjecucionProduccionID,
    ProgramaProduccionID,
    SolicitudProduccionID,
    MaquinaID,
    OperadorID,
    FechaInicioParo,
    MotivoParoID,
    MotivoParoTexto,
    Descripcion,
    EsParoLhRh,
    GrupoParoLhRh,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
OUTPUT INSERTED.ParoID
VALUES
(
    @EjecucionProduccionID,
    @ProgramaProduccionID,
    @SolicitudProduccionID,
    @MaquinaID,
    @OperadorID,
    @FechaInicioParo,
    @MotivoParoID,
    @MotivoParoTexto,
    @Descripcion,
    @EsParoLhRh,
    @GrupoParoLhRh,
    @UsuarioID,
    GETDATE(),
    1
);";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucion.EjecucionProduccionID;
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = ejecucion.ProgramaProduccionID;
            cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = (object?)ejecucion.SolicitudProduccionID ?? DBNull.Value;
            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = (object?)ejecucion.MaquinaID ?? DBNull.Value;
            cmd.Parameters.Add("@OperadorID", SqlDbType.Int).Value = (object?)ejecucion.OperadorID ?? DBNull.Value;
            cmd.Parameters.Add("@FechaInicioParo", SqlDbType.DateTime).Value = fechaInicioParo;
            cmd.Parameters.Add("@MotivoParoID", SqlDbType.Int).Value = (object?)motivoParoId ?? DBNull.Value;
            cmd.Parameters.Add("@MotivoParoTexto", SqlDbType.NVarChar, 200).Value = (object?)motivoParoTexto ?? DBNull.Value;
            cmd.Parameters.Add("@Descripcion", SqlDbType.NVarChar, 500).Value = (object?)descripcion ?? DBNull.Value;
            cmd.Parameters.Add("@EsParoLhRh", SqlDbType.Bit).Value = esParoLhRh;
            cmd.Parameters.Add("@GrupoParoLhRh", SqlDbType.UniqueIdentifier).Value = grupoParoLhRh.HasValue ? grupoParoLhRh.Value : DBNull.Value;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;

            var resultado = await cmd.ExecuteScalarAsync();
            if (resultado == null || resultado == DBNull.Value) throw new InvalidOperationException("No fue posible crear el paro de Producción.");
            return Convert.ToInt32(resultado);
        }

        private async Task<List<ParoLhRhAbiertoInterno>> ObtenerParosAbiertosGrupoLhRhAsync(Guid grupoParoLhRh, SqlConnection cn, SqlTransaction tx)
        {
            var lista = new List<ParoLhRhAbiertoInterno>();

            const string sql = @"
SELECT
    ParoID,
    EjecucionProduccionID,
    ProgramaProduccionID,
    FechaInicioParo,
    ISNULL(EsInterrupcionUrgente,0) AS EsInterrupcionUrgente
FROM dbo.Produccion_Paros WITH(UPDLOCK,HOLDLOCK)
WHERE Activo=1
  AND FechaFinParo IS NULL
  AND ISNULL(EsParoLhRh,0)=1
  AND GrupoParoLhRh=@GrupoParoLhRh
ORDER BY ProgramaProduccionID,ParoID;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@GrupoParoLhRh", SqlDbType.UniqueIdentifier).Value = grupoParoLhRh;
            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(new ParoLhRhAbiertoInterno
                {
                    ParoID = Convert.ToInt32(rd["ParoID"]),
                    EjecucionProduccionID = Convert.ToInt32(rd["EjecucionProduccionID"]),
                    ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),
                    FechaInicioParo = Convert.ToDateTime(rd["FechaInicioParo"]),
                    EsInterrupcionUrgente = rd["EsInterrupcionUrgente"] != DBNull.Value && Convert.ToBoolean(rd["EsInterrupcionUrgente"])
                });
            }

            return lista;
        }
        private async Task<ContextoReinicioLhRhInterno?> ObtenerContextoReinicioLhRhAsync(int paroOrigenId, int paroParejaId, int ejecucionOrigenId, int ejecucionParejaId, SqlConnection cn, SqlTransaction tx)
        {
            if (paroOrigenId <= 0 || paroParejaId <= 0 || ejecucionOrigenId <= 0 || ejecucionParejaId <= 0) return null;
            const string sql = @"
SELECT TOP(1)
    p1.GrupoParoLhRh,
    p1.ParoID AS ParoOrigenID,
    p2.ParoID AS ParoParejaID,
    CASE WHEN p1.FechaInicioParo<=p2.FechaInicioParo THEN p1.FechaInicioParo ELSE p2.FechaInicioParo END AS FechaInicioFisica,
    CASE WHEN p1.FechaFinParo>=p2.FechaFinParo THEN p1.FechaFinParo ELSE p2.FechaFinParo END AS FechaFinFisica
FROM dbo.Produccion_Paros p1 WITH(UPDLOCK,HOLDLOCK)
INNER JOIN dbo.Produccion_Paros p2 WITH(UPDLOCK,HOLDLOCK)
    ON p2.GrupoParoLhRh=p1.GrupoParoLhRh
WHERE p1.ParoID=@ParoOrigenID
  AND p2.ParoID=@ParoParejaID
  AND p1.EjecucionProduccionID=@EjecucionOrigenID
  AND p2.EjecucionProduccionID=@EjecucionParejaID
  AND p1.Activo=1
  AND p2.Activo=1
  AND p1.FechaFinParo IS NOT NULL
  AND p2.FechaFinParo IS NOT NULL
  AND ISNULL(p1.EsParoLhRh,0)=1
  AND ISNULL(p2.EsParoLhRh,0)=1
  AND p1.GrupoParoLhRh IS NOT NULL
  AND p2.GrupoParoLhRh=p1.GrupoParoLhRh
  AND ISNULL(p1.EsInterrupcionUrgente,0)=ISNULL(p2.EsInterrupcionUrgente,0)
  AND
  (
      ISNULL(p1.EsInterrupcionUrgente,0)=0
      OR
      (
          p1.ProgramaUrgenteID IS NOT NULL
          AND p2.ProgramaUrgenteID=p1.ProgramaUrgenteID
      )
  );";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ParoOrigenID", SqlDbType.Int).Value = paroOrigenId;
            cmd.Parameters.Add("@ParoParejaID", SqlDbType.Int).Value = paroParejaId;
            cmd.Parameters.Add("@EjecucionOrigenID", SqlDbType.Int).Value = ejecucionOrigenId;
            cmd.Parameters.Add("@EjecucionParejaID", SqlDbType.Int).Value = ejecucionParejaId;
            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync()) return null;
            return new ContextoReinicioLhRhInterno
            {
                GrupoParoLhRh = (Guid)rd["GrupoParoLhRh"],
                ParoOrigenID = Convert.ToInt32(rd["ParoOrigenID"]),
                ParoParejaID = Convert.ToInt32(rd["ParoParejaID"]),
                FechaInicioFisica = Convert.ToDateTime(rd["FechaInicioFisica"]),
                FechaFinFisica = Convert.ToDateTime(rd["FechaFinFisica"])
            };
        }
        private static async Task SincronizarFinParejaLhRhDesdeProgramaAsync(int programaFuenteId, int programaParejaId, int usuarioId, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
UPDATE pareja
SET pareja.FechaFinProgramada=fuente.FechaFinProgramada,
    pareja.UsuarioModificacionID=@UsuarioID,
    pareja.FechaModificacion=GETDATE()
FROM dbo.Planeacion_ProgramaProduccion pareja
INNER JOIN dbo.Planeacion_ProgramaProduccion fuente
    ON fuente.ProgramaProduccionID=@ProgramaFuenteID
   AND fuente.Activo=1
WHERE pareja.ProgramaProduccionID=@ProgramaParejaID
  AND pareja.Activo=1;

IF @@ROWCOUNT<>1
    THROW 51670,'No fue posible sincronizar la fecha final de la pareja LH/RH después del paro.',1;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ProgramaFuenteID", SqlDbType.Int).Value = programaFuenteId;
            cmd.Parameters.Add("@ProgramaParejaID", SqlDbType.Int).Value = programaParejaId;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            await cmd.ExecuteNonQueryAsync();
        }

    }
}