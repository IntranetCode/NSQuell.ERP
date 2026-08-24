using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers;

// NSQ_CALENDARIO_EXACTO_V20
public sealed partial class PlaneacionCalendarioMaquinasController
{
    private static DateTime HoraExactaCalendario(DateTime value)
    {
        var baseHora = new DateTime(
            value.Year,
            value.Month,
            value.Day,
            value.Hour,
            0,
            0,
            value.Kind);

        return value.Minute >= 30
            ? baseHora.AddHours(1)
            : baseHora;
    }

    private static async Task<CalculoCola> CalcularPosicionExactaCalendarioAsync(
        int maquinaId,
        int programaExcluirId,
        int? parteId,
        int? moldeId,
        int? parteAnteriorId,
        int? moldeAnteriorId,
        DateTime inicioSolicitado,
        decimal horasProduccion,
        SqlConnection cn,
        SqlTransaction tx,
        bool trabajarDomingo)
    {
        var cambio = HoraExactaCalendario(inicioSolicitado);

        if (!trabajarDomingo && cambio.DayOfWeek == DayOfWeek.Sunday)
        {
            throw new InvalidOperationException(
                "La hora seleccionada cae en domingo y el calendario no está habilitado para trabajar domingo.");
        }

        if (cambio < DateTime.Now.AddMinutes(-1))
        {
            throw new InvalidOperationException(
                "No se puede mover una OF a una hora anterior al momento actual.");
        }

        if (horasProduccion <= 0)
            horasProduccion = 1m;

        var mismaParte =
            parteId.HasValue &&
            parteAnteriorId.HasValue &&
            parteId.Value == parteAnteriorId.Value;

        var mismoMolde =
            moldeId.HasValue &&
            moldeAnteriorId.HasValue &&
            moldeId.Value == moldeAnteriorId.Value;

        var horasCambio =
            !mismaParte && !mismoMolde
                ? 1m
                : 0m;

        var arranque =
            SumarHorasOperativas(
                cambio,
                horasCambio,
                trabajarDomingo);

        var fin =
            SumarHorasOperativas(
                arranque,
                horasProduccion,
                trabajarDomingo);

        var finBloqueDuro =
            await ObtenerFinCruceMaquinaBloqueadaAsync(
                maquinaId,
                programaExcluirId,
                cambio,
                fin,
                cn,
                tx);

        if (finBloqueDuro.HasValue)
        {
            throw new InvalidOperationException(
                "La OF no puede colocarse exactamente a las " +
                cambio.ToString("dd/MM/yyyy HH:mm") +
                " porque existe una producción, preparación, calidad o bloque no movible en esa máquina. " +
                "La primera referencia libre después del conflicto es " +
                finBloqueDuro.Value.ToString("dd/MM/yyyy HH:mm") + ".");
        }

        var parejaLhRhId =
            await ObtenerParejaExactaLhRhIdAsync(
                programaExcluirId,
                cn,
                tx);

        var conflictoMoviblePrevio =
            await ObtenerConflictoMoviblePrevioExactoAsync(
                maquinaId,
                programaExcluirId,
                parejaLhRhId,
                cambio,
                cn,
                tx);

        if (conflictoMoviblePrevio != null)
        {
            throw new InvalidOperationException(
                "La hora " +
                cambio.ToString("dd/MM/yyyy HH:mm") +
                " cae dentro de " +
                conflictoMoviblePrevio.Descripcion +
                " (" +
                conflictoMoviblePrevio.Inicio.ToString("HH:mm") +
                " - " +
                conflictoMoviblePrevio.Fin.ToString("HH:mm") +
                "). Suelta la OF en una hora libre; el sistema solo recorrerá automáticamente los programas que comienzan a partir de la hora seleccionada.");
        }

        if (moldeId.HasValue)
        {
            var conflictoMolde =
                await ObtenerConflictoDuroMoldeExactoAsync(
                    moldeId.Value,
                    maquinaId,
                    programaExcluirId,
                    cambio,
                    fin,
                    cn,
                    tx);

            if (conflictoMolde != null)
            {
                throw new InvalidOperationException(
                    "La OF no puede colocarse exactamente a las " +
                    cambio.ToString("dd/MM/yyyy HH:mm") +
                    " porque el molde está comprometido por " +
                    conflictoMolde.Descripcion +
                    ". Fin del conflicto: " +
                    conflictoMolde.Fin.ToString("dd/MM/yyyy HH:mm") + ".");
            }
        }

        return new CalculoCola
        {
            Cambio = cambio,
            Arranque = arranque,
            Fin = fin,
            HorasCambio = horasCambio,
            MoldeLiberado = null
        };
    }

    private sealed class ConflictoProgramaExacto
    {
        public string Descripcion { get; set; } = "otro programa";
        public DateTime Inicio { get; set; }
        public DateTime Fin { get; set; }
    }

    private static async Task<int?> ObtenerParejaExactaLhRhIdAsync(
        int programaProduccionId,
        SqlConnection cn,
        SqlTransaction tx)
    {
        const string sql = @"
DECLARE @Observaciones NVARCHAR(500);
SELECT @Observaciones=Observaciones
FROM dbo.Planeacion_ProgramaProduccion
WHERE ProgramaProduccionID=@ProgramaProduccionID
  AND Activo=1;

DECLARE @Pos INT=CHARINDEX(N'NSQ_LHRH_PAIR:',ISNULL(@Observaciones,N''));
IF @Pos<=0
BEGIN
    SELECT CAST(NULL AS INT);
    RETURN;
END;

DECLARE @Inicio INT=@Pos+LEN(N'NSQ_LHRH_PAIR:');
DECLARE @Resto NVARCHAR(100)=SUBSTRING(@Observaciones,@Inicio,100);
DECLARE @Fin INT=CHARINDEX(N';',@Resto);
IF @Fin>0 SET @Resto=LEFT(@Resto,@Fin-1);
DECLARE @Grupo INT=TRY_CONVERT(INT,LTRIM(RTRIM(@Resto)));

SELECT TOP (1) pp.ProgramaProduccionID
FROM dbo.Planeacion_ProgramaProduccion pp
WHERE pp.Activo=1
  AND pp.ProgramaProduccionID<>@ProgramaProduccionID
  AND @Grupo IS NOT NULL
  AND pp.Observaciones LIKE N'%NSQ_LHRH_PAIR:'+CONVERT(NVARCHAR(20),@Grupo)+N';%'
ORDER BY pp.ProgramaProduccionID;";

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;
        var value = await cmd.ExecuteScalarAsync();
        return value == null || value == DBNull.Value ? null : Convert.ToInt32(value);
    }

    private static async Task<ConflictoProgramaExacto?> ObtenerConflictoMoviblePrevioExactoAsync(
        int maquinaId,
        int programaExcluirId,
        int? parejaExcluirId,
        DateTime inicio,
        SqlConnection cn,
        SqlTransaction tx)
    {
        const string sql = @"
SELECT TOP (1)
    pp.ProgramaProduccionID,
    pp.FechaInicioProgramada,
    ISNULL
    (
        pp.FechaFinProgramada,
        DATEADD(MINUTE,CAST(CEILING(ISNULL(pp.HorasProgramadas,1)*60) AS INT),pp.FechaInicioProgramada)
    ) AS FechaFinProgramada,
    COALESCE(NULLIF(s.NumeroOFRecibida,N''),NULLIF(s.FolioSolicitud,N''),N'') AS FolioOF
FROM dbo.Planeacion_ProgramaProduccion pp WITH (UPDLOCK,HOLDLOCK)
LEFT JOIN dbo.SolicitudesProduccion s ON s.SolicitudProduccionID=pp.SolicitudProduccionID
WHERE pp.Activo=1
  AND pp.MaquinaID=@MaquinaID
  AND pp.ProgramaProduccionID<>@ProgramaProduccionID
  AND (@ParejaID IS NULL OR pp.ProgramaProduccionID<>@ParejaID)
  AND pp.FechaInicioProgramada IS NOT NULL
  AND pp.FechaInicioProgramada<@Inicio
  AND ISNULL
      (
          pp.FechaFinProgramada,
          DATEADD(MINUTE,CAST(CEILING(ISNULL(pp.HorasProgramadas,1)*60) AS INT),pp.FechaInicioProgramada)
      )>@Inicio
  AND ISNULL(pp.EstatusID,1)=1
  AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.Produccion_Ejecucion e
          WHERE e.ProgramaProduccionID=pp.ProgramaProduccionID
            AND e.Activo=1
      )
  AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.Calidad_Inspecciones ci
          WHERE ci.ProgramaProduccionID=pp.ProgramaProduccionID
      )
ORDER BY pp.FechaInicioProgramada DESC;";

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = maquinaId;
        cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaExcluirId;
        cmd.Parameters.Add("@ParejaID", SqlDbType.Int).Value = parejaExcluirId.HasValue ? parejaExcluirId.Value : DBNull.Value;
        cmd.Parameters.Add("@Inicio", SqlDbType.DateTime).Value = inicio;

        await using var rd = await cmd.ExecuteReaderAsync();
        if (!await rd.ReadAsync()) return null;

        var folio = rd["FolioOF"]?.ToString()?.Trim();
        var programa = Convert.ToInt32(rd["ProgramaProduccionID"]);
        return new ConflictoProgramaExacto
        {
            Descripcion = !string.IsNullOrWhiteSpace(folio) ? $"la OF {folio}" : $"el Programa {programa}",
            Inicio = Convert.ToDateTime(rd["FechaInicioProgramada"]),
            Fin = Convert.ToDateTime(rd["FechaFinProgramada"])
        };
    }

    private sealed class ConflictoMoldeExacto
    {
        public string Descripcion { get; set; } = "otro programa";
        public DateTime Fin { get; set; }
    }

    private static async Task<ConflictoMoldeExacto?> ObtenerConflictoDuroMoldeExactoAsync(
        int moldeId,
        int maquinaDestinoId,
        int programaExcluirId,
        DateTime inicio,
        DateTime fin,
        SqlConnection cn,
        SqlTransaction tx)
    {
        const string sql = @"
SELECT TOP (1)
    pp.ProgramaProduccionID,
    ISNULL(NULLIF(pp.MaquinaCodigo,N''),ISNULL(m.Codigo,N'SIN MÁQUINA')) AS MaquinaCodigo,
    ISNULL
    (
        pp.FechaFinProgramada,
        DATEADD
        (
            MINUTE,
            CAST(CEILING(ISNULL(pp.HorasProgramadas,1)*60) AS INT),
            pp.FechaInicioProgramada
        )
    ) AS FechaFinProgramada,
    COALESCE(NULLIF(s.NumeroOFRecibida,N''),NULLIF(s.FolioSolicitud,N''),N'') AS FolioOF
FROM dbo.Planeacion_ProgramaProduccion pp WITH (UPDLOCK,HOLDLOCK)
LEFT JOIN dbo.ERP_Maquinas m ON m.MaquinaID=pp.MaquinaID
LEFT JOIN dbo.SolicitudesProduccion s ON s.SolicitudProduccionID=pp.SolicitudProduccionID
WHERE pp.Activo=1
  AND pp.MoldeID=@MoldeID
  AND pp.ProgramaProduccionID<>@ProgramaProduccionID
  AND pp.FechaInicioProgramada IS NOT NULL
  AND pp.FechaInicioProgramada<@Fin
  AND ISNULL
      (
          pp.FechaFinProgramada,
          DATEADD(MINUTE,CAST(CEILING(ISNULL(pp.HorasProgramadas,1)*60) AS INT),pp.FechaInicioProgramada)
      )>@Inicio
  AND
  (
      ISNULL(pp.MaquinaID,0)<>@MaquinaDestinoID
      OR ISNULL(pp.EstatusID,1)<>1
      OR EXISTS
         (
             SELECT 1
             FROM dbo.Produccion_Ejecucion e
             WHERE e.ProgramaProduccionID=pp.ProgramaProduccionID
               AND e.Activo=1
         )
      OR EXISTS
         (
             SELECT 1
             FROM dbo.Calidad_Inspecciones ci
             WHERE ci.ProgramaProduccionID=pp.ProgramaProduccionID
         )
  )
ORDER BY
    CASE WHEN ISNULL(pp.MaquinaID,0)<>@MaquinaDestinoID THEN 0 ELSE 1 END,
    pp.FechaInicioProgramada;";

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@MoldeID", SqlDbType.Int).Value = moldeId;
        cmd.Parameters.Add("@MaquinaDestinoID", SqlDbType.Int).Value = maquinaDestinoId;
        cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaExcluirId;
        cmd.Parameters.Add("@Inicio", SqlDbType.DateTime).Value = inicio;
        cmd.Parameters.Add("@Fin", SqlDbType.DateTime).Value = fin;

        await using var rd = await cmd.ExecuteReaderAsync();
        if (!await rd.ReadAsync())
            return null;

        var folio = rd["FolioOF"]?.ToString()?.Trim();
        var maquina = rd["MaquinaCodigo"]?.ToString()?.Trim();
        var programa = Convert.ToInt32(rd["ProgramaProduccionID"]);

        return new ConflictoMoldeExacto
        {
            Descripcion =
                !string.IsNullOrWhiteSpace(folio)
                    ? $"OF {folio} en {maquina}"
                    : $"Programa {programa} en {maquina}",
            Fin = Convert.ToDateTime(rd["FechaFinProgramada"])
        };
    }
}
