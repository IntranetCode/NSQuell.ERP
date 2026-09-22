using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Data;

namespace ERP.NSQuell.Controllers;

// NSQ_PRODUCCION_PERSONAL_DISTRIBUCION_V14_3_1
public sealed partial class ProduccionPersonalController
{
    private sealed class PeriodoDistribucionV14
    {
        public string Vista { get; set; } = "dia";
        public DateTime FiltroDesde { get; set; }
        public DateTime FiltroHasta { get; set; }
        public DateTime EdicionDesde { get; set; }
        public DateTime EdicionHasta { get; set; }
        public DateTime FechaBase => EdicionDesde;
        public string Etiqueta { get; set; } = string.Empty;
        public List<SemanaMesV14> SemanasMes { get; set; } = new();
    }

    private sealed class SemanaMesV14
    {
        public DateTime Desde { get; set; }
        public DateTime Hasta { get; set; }
        public string Etiqueta { get; set; } = string.Empty;
        public bool Seleccionada { get; set; }
    }

    private sealed class OperadorExtraV14
    {
        public long ExtraID { get; set; }
        public int OperadorID { get; set; }
        public string NumeroControl { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
    }

    private sealed class FilaDistribucionV14
    {
        public long? DistribucionID { get; set; }

        public int? MaquinaID { get; set; }
        public string CentroClave { get; set; } = string.Empty;
        public string MaquinaCodigo { get; set; } = string.Empty;
        public string MaquinaNombre { get; set; } = string.Empty;
        public bool EsEspecial { get; set; }

        public int? ProgramaProduccionID { get; set; }
        public int? ParteID { get; set; }
        public string NumeroParte { get; set; } = string.Empty;
        public string ReferenciaSAP { get; set; } = string.Empty;
        public string DescripcionParte { get; set; } = string.Empty;
        public string OF { get; set; } = string.Empty;
        public bool PiezaProgramada { get; set; }
        public string OrigenPieza { get; set; } = string.Empty;

        public int? OperadorID { get; set; }
        public string OperadorNombre { get; set; } = string.Empty;
        public string NumeroControlOperador { get; set; } = string.Empty;

        public DateTime Inicio { get; set; }
        public DateTime Fin { get; set; }

        public List<OperadorExtraV14> Extras { get; set; } = new();
    }

    private sealed class ProgramaV14
    {
        public int ProgramaProduccionID { get; set; }
        public int? ParteID { get; set; }
        public string NumeroParte { get; set; } = string.Empty;
        public string ReferenciaSAP { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public string OF { get; set; } = string.Empty;
        public DateTime Inicio { get; set; }
        public DateTime Fin { get; set; }
    }

    private static PeriodoDistribucionV14 ResolverPeriodoDistribucionV14(
        string? vista,
        DateTime? fechaDesde,
        DateTime? fechaHasta,
        DateTime? semanaDesde)
    {
        var modo = (vista ?? "dia").Trim().ToLowerInvariant();
        var referencia = (fechaDesde ?? DateTime.Today).Date;

        if (modo == "semana")
        {
            var inicio = InicioSemanaV2(referencia);

            return new PeriodoDistribucionV14
            {
                Vista = "semana",
                FiltroDesde = inicio,
                FiltroHasta = inicio.AddDays(6),
                EdicionDesde = inicio,
                EdicionHasta = inicio.AddDays(6),
                Etiqueta = $"Semana {inicio:dd/MM} - {inicio.AddDays(6):dd/MM/yyyy}"
            };
        }

        if (modo == "rango")
        {
            var desde = referencia;
            var hasta = (fechaHasta ?? referencia).Date;

            if (hasta < desde)
                (desde, hasta) = (hasta, desde);

            if ((hasta - desde).TotalDays > 92)
                hasta = desde.AddDays(92);

            return new PeriodoDistribucionV14
            {
                Vista = "rango",
                FiltroDesde = desde,
                FiltroHasta = hasta,
                EdicionDesde = desde,
                EdicionHasta = hasta,
                Etiqueta = $"Rango {desde:dd/MM/yyyy} - {hasta:dd/MM/yyyy}"
            };
        }

        if (modo == "mes")
        {
            var mesInicio = new DateTime(referencia.Year, referencia.Month, 1);
            var mesFin = mesInicio.AddMonths(1).AddDays(-1);

            var semanaElegida = semanaDesde?.Date ?? InicioSemanaV2(referencia);

            if (semanaElegida < InicioSemanaV2(mesInicio) ||
                semanaElegida > mesFin)
            {
                semanaElegida = InicioSemanaV2(mesInicio);
            }

            var semanas = new List<SemanaMesV14>();
            var cursor = InicioSemanaV2(mesInicio);

            while (cursor <= mesFin)
            {
                var desdeSemana = cursor < mesInicio ? mesInicio : cursor;
                var hastaSemana = cursor.AddDays(6) > mesFin
                    ? mesFin
                    : cursor.AddDays(6);

                semanas.Add(new SemanaMesV14
                {
                    Desde = desdeSemana,
                    Hasta = hastaSemana,
                    Etiqueta = $"{desdeSemana:dd/MM} - {hastaSemana:dd/MM}",
                    Seleccionada =
                        semanaElegida >= cursor &&
                        semanaElegida <= cursor.AddDays(6)
                });

                cursor = cursor.AddDays(7);
            }

            var seleccionada = semanas.FirstOrDefault(x => x.Seleccionada)
                ?? semanas.First();

            foreach (var s in semanas)
                s.Seleccionada = ReferenceEquals(s, seleccionada);

            return new PeriodoDistribucionV14
            {
                Vista = "mes",
                FiltroDesde = mesInicio,
                FiltroHasta = mesFin,
                EdicionDesde = seleccionada.Desde,
                EdicionHasta = seleccionada.Hasta,
                Etiqueta = $"Mes {mesInicio:MMMM yyyy} · semana {seleccionada.Etiqueta}",
                SemanasMes = semanas
            };
        }

        return new PeriodoDistribucionV14
        {
            Vista = "dia",
            FiltroDesde = referencia,
            FiltroHasta = referencia,
            EdicionDesde = referencia,
            EdicionHasta = referencia,
            Etiqueta = referencia.ToString("dddd dd/MM/yyyy")
        };
    }

    private static IEnumerable<DateTime> FechasPeriodoV14(
        PeriodoDistribucionV14 periodo)
    {
        for (var d = periodo.EdicionDesde.Date;
             d <= periodo.EdicionHasta.Date;
             d = d.AddDays(1))
        {
            yield return d;
        }
    }

    private static async Task<bool> ConfiguradoV14Async(
        SqlConnection cn,
        SqlTransaction? tx)
    {
        const string sql = @"
SELECT CONVERT(bit,CASE WHEN
       OBJECT_ID(N'dbo.Produccion_DistribucionOperadores',N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.Produccion_DistribucionOperadoresHistorial',N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.Produccion_DistribucionOperadoresExtra',N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.Produccion_DistribucionOperadoresExtraHistorial',N'U') IS NOT NULL
THEN 1 ELSE 0 END);";

        await using var cmd = tx == null
            ? new SqlCommand(sql, cn)
            : new SqlCommand(sql, cn, tx);

        return Convert.ToBoolean(await cmd.ExecuteScalarAsync() ?? false);
    }

    private static async Task<List<ProgramaV14>> CargarProgramasMaquinaV14Async(
        int maquinaId,
        DateTime inicio,
        DateTime fin,
        SqlConnection cn,
        SqlTransaction? tx)
    {
        const string sql = @"
SELECT
    pp.ProgramaProduccionID,
    pp.ParteID,
    COALESCE(NULLIF(pp.NumeroParte,N''),ep.NumeroParte,N'') AS NumeroParte,
    COALESCE(NULLIF(pp.ReferenciaSAP,N''),ep.ReferenciaSAP,N'') AS ReferenciaSAP,
    COALESCE(
        NULLIF(pp.DesignacionDescripcionSAP,N''),
        NULLIF(ep.Designacion,N''),
        ep.Descripcion,
        N'') AS Descripcion,
    COALESCE(
        NULLIF(LTRIM(RTRIM(s.NumeroOFRecibida)),N''),
        NULLIF(LTRIM(RTRIM(s.FolioSolicitud)),N''),
        N'') AS [OF],
    pp.FechaInicioProgramada,
    ISNULL(
        pp.FechaFinProgramada,
        DATEADD(
            MINUTE,
            CONVERT(int,CEILING(ISNULL(pp.HorasProgramadas,1)*60)),
            pp.FechaInicioProgramada)) AS FechaFinProgramada
FROM dbo.Planeacion_ProgramaProduccion pp
LEFT JOIN dbo.ERP_Partes ep
    ON ep.ParteID=pp.ParteID
LEFT JOIN dbo.SolicitudesProduccion s
    ON s.SolicitudProduccionID=pp.SolicitudProduccionID
WHERE pp.Activo=1
  AND pp.MaquinaID=@MaquinaID
  AND pp.FechaInicioProgramada IS NOT NULL
  AND pp.FechaInicioProgramada<@Fin
  AND ISNULL(
        pp.FechaFinProgramada,
        DATEADD(
            MINUTE,
            CONVERT(int,CEILING(ISNULL(pp.HorasProgramadas,1)*60)),
            pp.FechaInicioProgramada))>@Inicio
  AND ISNULL(pp.EstatusID,1) NOT IN(6,9,99)
ORDER BY
    CASE
        WHEN pp.FechaInicioProgramada<=@Inicio
         AND ISNULL(
                pp.FechaFinProgramada,
                DATEADD(
                    MINUTE,
                    CONVERT(int,CEILING(ISNULL(pp.HorasProgramadas,1)*60)),
                    pp.FechaInicioProgramada))>@Inicio
        THEN 0 ELSE 1
    END,
    pp.FechaInicioProgramada,
    pp.ProgramaProduccionID;";

        var lista = new List<ProgramaV14>();

        await using var cmd = tx == null
            ? new SqlCommand(sql, cn)
            : new SqlCommand(sql, cn, tx);

        cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = maquinaId;
        cmd.Parameters.Add("@Inicio", SqlDbType.DateTime2).Value = inicio;
        cmd.Parameters.Add("@Fin", SqlDbType.DateTime2).Value = fin;

        await using var rd = await cmd.ExecuteReaderAsync();

        while (await rd.ReadAsync())
        {
            lista.Add(new ProgramaV14
            {
                ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),
                ParteID = rd["ParteID"] == DBNull.Value
                    ? null
                    : Convert.ToInt32(rd["ParteID"]),
                NumeroParte = rd["NumeroParte"]?.ToString()?.Trim() ?? string.Empty,
                ReferenciaSAP = rd["ReferenciaSAP"]?.ToString()?.Trim() ?? string.Empty,
                Descripcion = rd["Descripcion"]?.ToString()?.Trim() ?? string.Empty,
                OF = rd["OF"]?.ToString()?.Trim() ?? string.Empty,
                Inicio = Convert.ToDateTime(rd["FechaInicioProgramada"]),
                Fin = Convert.ToDateTime(rd["FechaFinProgramada"])
            });
        }

        return lista;
    }

    private static async Task<List<FilaDistribucionV14>> CargarFilasDistribucionV14Async(
        DateTime fecha,
        DistribucionTurnoV13 turno,
        SqlConnection cn,
        SqlTransaction? tx)
    {
        var ventana = VentanaDistribucionV13(fecha, turno);
        var filas = new List<FilaDistribucionV14>();

        const string baseSql = @"
SELECT
    m.MaquinaID,
    m.Codigo AS MaquinaCodigo,
    m.Nombre AS MaquinaNombre,

    d.DistribucionID,
    d.ProgramaProduccionID,
    d.ParteID,
    d.OperadorID,
    ISNULL(d.OrigenPieza,N'') AS OrigenPieza,

    op.NumeroControl AS NumeroControlOperador,
    LTRIM(RTRIM(CONCAT(
        ISNULL(op.Nombre,N''),N' ',
        ISNULL(op.ApellidoPaterno,N''),N' ',
        ISNULL(op.ApellidoMaterno,N'')))) AS OperadorNombre,

    p.NumeroParte,
    p.ReferenciaSAP,
    COALESCE(NULLIF(p.Designacion,N''),p.Descripcion,N'') AS DescripcionParte
FROM dbo.ERP_Maquinas m
LEFT JOIN dbo.Produccion_DistribucionOperadores d
    ON d.Activo=1
   AND d.FechaTrabajo=@Fecha
   AND d.TurnoID=@TurnoID
   AND d.MaquinaID=m.MaquinaID
LEFT JOIN dbo.Persona op
    ON op.PersonaID=d.OperadorID
LEFT JOIN dbo.ERP_Partes p
    ON p.ParteID=d.ParteID
WHERE m.Activo=1
  AND UPPER(ISNULL(m.Area,N''))
      COLLATE Modern_Spanish_CI_AI LIKE N'%INYE%'
ORDER BY
    CASE m.Codigo
        WHEN N'90-T' THEN 10
        WHEN N'120-T' THEN 20
        WHEN N'160-T' THEN 30
        WHEN N'160-3' THEN 40
        WHEN N'160-4' THEN 50
        WHEN N'200-T' THEN 60
        WHEN N'280-T' THEN 70
        WHEN N'280-2' THEN 80
        WHEN N'400-T' THEN 90
        WHEN N'500-T' THEN 100
        WHEN N'1200-T' THEN 110
        ELSE 999
    END,
    m.Codigo;";

        await using (var cmd = tx == null
            ? new SqlCommand(baseSql, cn)
            : new SqlCommand(baseSql, cn, tx))
        {
            cmd.Parameters.Add("@Fecha", SqlDbType.Date).Value = fecha.Date;
            cmd.Parameters.Add("@TurnoID", SqlDbType.Int).Value = turno.TurnoID;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                filas.Add(new FilaDistribucionV14
                {
                    DistribucionID = rd["DistribucionID"] == DBNull.Value
                        ? null
                        : Convert.ToInt64(rd["DistribucionID"]),
                    MaquinaID = Convert.ToInt32(rd["MaquinaID"]),
                    CentroClave = rd["MaquinaCodigo"]?.ToString()?.Trim() ?? string.Empty,
                    MaquinaCodigo = rd["MaquinaCodigo"]?.ToString()?.Trim() ?? string.Empty,
                    MaquinaNombre = rd["MaquinaNombre"]?.ToString()?.Trim() ?? string.Empty,
                    ProgramaProduccionID = rd["ProgramaProduccionID"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(rd["ProgramaProduccionID"]),
                    ParteID = rd["ParteID"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(rd["ParteID"]),
                    NumeroParte = rd["NumeroParte"]?.ToString()?.Trim() ?? string.Empty,
                    ReferenciaSAP = rd["ReferenciaSAP"]?.ToString()?.Trim() ?? string.Empty,
                    DescripcionParte = rd["DescripcionParte"]?.ToString()?.Trim() ?? string.Empty,
                    OrigenPieza = rd["OrigenPieza"]?.ToString()?.Trim() ?? string.Empty,
                    OperadorID = rd["OperadorID"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(rd["OperadorID"]),
                    OperadorNombre = rd["OperadorNombre"]?.ToString()?.Trim() ?? string.Empty,
                    NumeroControlOperador = rd["NumeroControlOperador"]?.ToString()?.Trim() ?? string.Empty,
                    Inicio = ventana.Inicio,
                    Fin = ventana.Fin
                });
            }
        }

        // Si después de una selección manual aparece una OF/programa real,
        // la pieza del programa pasa a ser la efectiva sin bloquear la planeación previa.
        foreach (var fila in filas.Where(x => x.MaquinaID.HasValue))
        {
            var programas = await CargarProgramasMaquinaV14Async(
                fila.MaquinaID!.Value,
                ventana.Inicio,
                ventana.Fin,
                cn,
                tx);

            var programa = programas.FirstOrDefault();

            if (programa == null)
                continue;

            fila.ProgramaProduccionID = programa.ProgramaProduccionID;
            fila.ParteID = programa.ParteID;
            fila.NumeroParte = programa.NumeroParte;
            fila.ReferenciaSAP = programa.ReferenciaSAP;
            fila.DescripcionParte = programa.Descripcion;
            fila.OF = programa.OF;
            fila.PiezaProgramada = true;
            fila.OrigenPieza = "PROGRAMA";
        }

        const string extraSql = @"
SELECT
    e.ExtraID,
    e.MaquinaID,
    e.CentroEspecial,
    e.OperadorID,
    ISNULL(p.NumeroControl,N'') AS NumeroControl,
    LTRIM(RTRIM(CONCAT(
        ISNULL(p.Nombre,N''),N' ',
        ISNULL(p.ApellidoPaterno,N''),N' ',
        ISNULL(p.ApellidoMaterno,N'')))) AS Nombre
FROM dbo.Produccion_DistribucionOperadoresExtra e
INNER JOIN dbo.Persona p
    ON p.PersonaID=e.OperadorID
WHERE e.Activo=1
  AND e.FechaTrabajo=@Fecha
  AND e.TurnoID=@TurnoID
ORDER BY e.ExtraID;";

        await using (var extraCmd = tx == null
            ? new SqlCommand(extraSql, cn)
            : new SqlCommand(extraSql, cn, tx))
        {
            extraCmd.Parameters.Add("@Fecha", SqlDbType.Date).Value = fecha.Date;
            extraCmd.Parameters.Add("@TurnoID", SqlDbType.Int).Value = turno.TurnoID;

            await using var rd = await extraCmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                var extra = new OperadorExtraV14
                {
                    ExtraID = Convert.ToInt64(rd["ExtraID"]),
                    OperadorID = Convert.ToInt32(rd["OperadorID"]),
                    NumeroControl = rd["NumeroControl"]?.ToString()?.Trim() ?? string.Empty,
                    Nombre = rd["Nombre"]?.ToString()?.Trim() ?? string.Empty
                };

                var maquinaId = rd["MaquinaID"] == DBNull.Value
                    ? (int?)null
                    : Convert.ToInt32(rd["MaquinaID"]);

                var centro = rd["CentroEspecial"]?.ToString()?.Trim() ?? string.Empty;

                var destino = maquinaId.HasValue
                    ? filas.FirstOrDefault(x => x.MaquinaID == maquinaId.Value)
                    : filas.FirstOrDefault(x =>
                        x.EsEspecial &&
                        string.Equals(
                            x.CentroClave,
                            centro,
                            StringComparison.OrdinalIgnoreCase));

                destino?.Extras.Add(extra);
            }
        }

        const string tornilloSql = @"
SELECT TOP(1)
    d.DistribucionID,
    d.ProgramaProduccionID,
    d.ParteID,
    d.OperadorID,
    ISNULL(d.OrigenPieza,N'') AS OrigenPieza,
    ISNULL(op.NumeroControl,N'') AS NumeroControl,
    LTRIM(RTRIM(CONCAT(
        ISNULL(op.Nombre,N''),N' ',
        ISNULL(op.ApellidoPaterno,N''),N' ',
        ISNULL(op.ApellidoMaterno,N'')))) AS OperadorNombre
FROM dbo.Produccion_DistribucionOperadores d
LEFT JOIN dbo.Persona op
    ON op.PersonaID=d.OperadorID
WHERE d.Activo=1
  AND d.FechaTrabajo=@Fecha
  AND d.TurnoID=@TurnoID
  AND UPPER(ISNULL(d.CentroEspecial,N''))=N'TORNILLO'
ORDER BY d.DistribucionID DESC;";

        var tornillo = new FilaDistribucionV14
        {
            CentroClave = "TORNILLO",
            MaquinaCodigo = "TORNILLO",
            MaquinaNombre = "TORNILLO",
            EsEspecial = true,
            NumeroParte = "TORNILLO",
            DescripcionParte = "Puesto especial",
            Inicio = ventana.Inicio,
            Fin = ventana.Fin
        };

        await using (var cmd = tx == null
            ? new SqlCommand(tornilloSql, cn)
            : new SqlCommand(tornilloSql, cn, tx))
        {
            cmd.Parameters.Add("@Fecha", SqlDbType.Date).Value = fecha.Date;
            cmd.Parameters.Add("@TurnoID", SqlDbType.Int).Value = turno.TurnoID;

            await using var rd = await cmd.ExecuteReaderAsync();

            if (await rd.ReadAsync())
            {
                tornillo.DistribucionID = Convert.ToInt64(rd["DistribucionID"]);
                tornillo.ProgramaProduccionID = rd["ProgramaProduccionID"] == DBNull.Value
                    ? null
                    : Convert.ToInt32(rd["ProgramaProduccionID"]);
                tornillo.ParteID = rd["ParteID"] == DBNull.Value
                    ? null
                    : Convert.ToInt32(rd["ParteID"]);
                tornillo.OperadorID = rd["OperadorID"] == DBNull.Value
                    ? null
                    : Convert.ToInt32(rd["OperadorID"]);
                tornillo.OrigenPieza = rd["OrigenPieza"]?.ToString()?.Trim() ?? string.Empty;
                tornillo.OperadorNombre = rd["OperadorNombre"]?.ToString()?.Trim() ?? string.Empty;
                tornillo.NumeroControlOperador = rd["NumeroControl"]?.ToString()?.Trim() ?? string.Empty;
            }
        }

        // Extras de TORNILLO se cargan en una segunda consulta pequeña para no
        // duplicar las máquinas del tablero.
        const string tornilloExtraSql = @"
SELECT
    e.ExtraID,
    e.OperadorID,
    ISNULL(p.NumeroControl,N'') AS NumeroControl,
    LTRIM(RTRIM(CONCAT(
        ISNULL(p.Nombre,N''),N' ',
        ISNULL(p.ApellidoPaterno,N''),N' ',
        ISNULL(p.ApellidoMaterno,N'')))) AS Nombre
FROM dbo.Produccion_DistribucionOperadoresExtra e
INNER JOIN dbo.Persona p
    ON p.PersonaID=e.OperadorID
WHERE e.Activo=1
  AND e.FechaTrabajo=@Fecha
  AND e.TurnoID=@TurnoID
  AND UPPER(ISNULL(e.CentroEspecial,N''))=N'TORNILLO'
ORDER BY e.ExtraID;";

        await using (var cmd = tx == null
            ? new SqlCommand(tornilloExtraSql, cn)
            : new SqlCommand(tornilloExtraSql, cn, tx))
        {
            cmd.Parameters.Add("@Fecha", SqlDbType.Date).Value = fecha.Date;
            cmd.Parameters.Add("@TurnoID", SqlDbType.Int).Value = turno.TurnoID;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                tornillo.Extras.Add(new OperadorExtraV14
                {
                    ExtraID = Convert.ToInt64(rd["ExtraID"]),
                    OperadorID = Convert.ToInt32(rd["OperadorID"]),
                    NumeroControl = rd["NumeroControl"]?.ToString()?.Trim() ?? string.Empty,
                    Nombre = rd["Nombre"]?.ToString()?.Trim() ?? string.Empty
                });
            }
        }

        filas.Add(tornillo);
        return filas;
    }

    private static async Task<string?> AdvertenciaCruceOperadorV14Async(
        int operadorId,
        DateTime inicio,
        DateTime fin,
        int? maquinaId,
        string? centroEspecial,
        SqlConnection cn,
        SqlTransaction tx)
    {
        const string sql = @"
SELECT TOP(1) Detalle
FROM
(
    SELECT
        1 AS Orden,
        CONCAT(
            N'Ya está programado en ',
            ISNULL(m.Codigo,d.CentroEspecial),
            N' dentro de este horario.') AS Detalle
    FROM dbo.Produccion_DistribucionOperadores d
    LEFT JOIN dbo.ERP_Maquinas m
        ON m.MaquinaID=d.MaquinaID
    WHERE d.Activo=1
      AND d.OperadorID=@OperadorID
      AND d.Inicio<@Fin
      AND d.Fin>@Inicio
      AND NOT
      (
          (@MaquinaID IS NOT NULL AND d.MaquinaID=@MaquinaID)
          OR
          (@MaquinaID IS NULL
           AND UPPER(ISNULL(d.CentroEspecial,N''))=
               UPPER(ISNULL(@CentroEspecial,N'')))
      )

    UNION ALL

    SELECT
        2 AS Orden,
        CONCAT(
            N'Ya está agregado como apoyo en ',
            ISNULL(m.Codigo,e.CentroEspecial),
            N' dentro de este horario.') AS Detalle
    FROM dbo.Produccion_DistribucionOperadoresExtra e
    LEFT JOIN dbo.ERP_Maquinas m
        ON m.MaquinaID=e.MaquinaID
    WHERE e.Activo=1
      AND e.OperadorID=@OperadorID
      AND e.Inicio<@Fin
      AND e.Fin>@Inicio
      AND NOT
      (
          (@MaquinaID IS NOT NULL AND e.MaquinaID=@MaquinaID)
          OR
          (@MaquinaID IS NULL
           AND UPPER(ISNULL(e.CentroEspecial,N''))=
               UPPER(ISNULL(@CentroEspecial,N'')))
      )

    UNION ALL

    SELECT
        3 AS Orden,
        CONCAT(
            N'Ya tiene otra OF',
            CASE WHEN NULLIF(pp.MaquinaCodigo,N'') IS NULL
                 THEN N''
                 ELSE N' en '+pp.MaquinaCodigo END,
            N' que cruza este horario.') AS Detalle
    FROM dbo.Produccion_ProgramaPersonalAsignaciones a
    INNER JOIN dbo.Planeacion_ProgramaProduccion pp
        ON pp.ProgramaProduccionID=a.ProgramaProduccionID
    WHERE a.Activo=1
      AND a.OperadorID=@OperadorID
      AND a.Inicio<@Fin
      AND a.Fin>@Inicio
      AND (@MaquinaID IS NULL OR ISNULL(pp.MaquinaID,0)<>@MaquinaID)
) X
ORDER BY Orden;";

        await using var cmd = new SqlCommand(sql, cn, tx);

        cmd.Parameters.Add("@OperadorID", SqlDbType.Int).Value = operadorId;
        cmd.Parameters.Add("@Inicio", SqlDbType.DateTime2).Value = inicio;
        cmd.Parameters.Add("@Fin", SqlDbType.DateTime2).Value = fin;
        cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value =
            maquinaId.HasValue
                ? maquinaId.Value
                : DBNull.Value;
        cmd.Parameters.Add("@CentroEspecial", SqlDbType.NVarChar, 50).Value =
            string.IsNullOrWhiteSpace(centroEspecial)
                ? DBNull.Value
                : centroEspecial.Trim();

        var value = await cmd.ExecuteScalarAsync();

        return value == null || value == DBNull.Value
            ? null
            : value.ToString()?.Trim();
    }

    private static async Task ValidarOperadorDistribucionV14Async(
        int operadorId,
        int? parteId,
        SqlConnection cn,
        SqlTransaction tx)
    {
        var candidatos = await CargarOperadoresDistribucionV13Async(
            parteId,
            cn,
            tx);

        if (!candidatos.Any(x => x.PersonaID == operadorId))
        {
            throw new InvalidOperationException(
                parteId.HasValue
                    ? "El operador no está activo o no tiene polivalencia válida para la pieza seleccionada."
                    : "El operador ya no está activo en el catálogo de operadores.");
        }
    }

    private static async Task<long?> CargarDistribucionActualV14Async(
        DateTime fecha,
        int turnoId,
        int? maquinaId,
        string? centroEspecial,
        SqlConnection cn,
        SqlTransaction tx,
        Action<int?,int?,int?> valores)
    {
        const string sql = @"
SELECT TOP(1)
    DistribucionID,
    ProgramaProduccionID,
    ParteID,
    OperadorID
FROM dbo.Produccion_DistribucionOperadores WITH(UPDLOCK,HOLDLOCK)
WHERE Activo=1
  AND FechaTrabajo=@Fecha
  AND TurnoID=@TurnoID
  AND
  (
      (@MaquinaID IS NOT NULL AND MaquinaID=@MaquinaID)
      OR
      (@MaquinaID IS NULL
       AND UPPER(ISNULL(CentroEspecial,N''))=
           UPPER(ISNULL(@CentroEspecial,N'')))
  )
ORDER BY DistribucionID DESC;";

        await using var cmd = new SqlCommand(sql, cn, tx);

        cmd.Parameters.Add("@Fecha", SqlDbType.Date).Value = fecha.Date;
        cmd.Parameters.Add("@TurnoID", SqlDbType.Int).Value = turnoId;
        cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value =
            maquinaId.HasValue ? maquinaId.Value : DBNull.Value;
        cmd.Parameters.Add("@CentroEspecial", SqlDbType.NVarChar, 50).Value =
            string.IsNullOrWhiteSpace(centroEspecial)
                ? DBNull.Value
                : centroEspecial.Trim();

        await using var rd = await cmd.ExecuteReaderAsync();

        if (!await rd.ReadAsync())
        {
            valores(null, null, null);
            return null;
        }

        int? programa = rd["ProgramaProduccionID"] == DBNull.Value
            ? null
            : Convert.ToInt32(rd["ProgramaProduccionID"]);

        int? parte = rd["ParteID"] == DBNull.Value
            ? null
            : Convert.ToInt32(rd["ParteID"]);

        int? operador = rd["OperadorID"] == DBNull.Value
            ? null
            : Convert.ToInt32(rd["OperadorID"]);

        valores(programa, parte, operador);

        return Convert.ToInt64(rd["DistribucionID"]);
    }

    [HttpGet("DistribucionV14")]
    public async Task<IActionResult> DistribucionV14(
        string? vista,
        DateTime? fechaDesde,
        DateTime? fechaHasta,
        DateTime? semanaDesde,
        int? turnoId)
    {
        if (!UsuarioEnSesion())
            return Unauthorized();

        var periodo = ResolverPeriodoDistribucionV14(
            vista,
            fechaDesde,
            fechaHasta,
            semanaDesde);

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        if (!await ConfiguradoV14Async(cn, null))
        {
            return Json(new
            {
                ok = false,
                configurado = false,
                message = "Ejecuta NSQ_PRODUCCION_PERSONAL_MULTIOPERADOR_V14.sql en la misma base que usa localhost."
            });
        }

        var turnos = await CargarTurnosDistribucionV13Async(cn, null);

        var turno = turnoId.HasValue
            ? turnos.FirstOrDefault(x => x.TurnoID == turnoId.Value)
            : null;

        turno ??= turnos.FirstOrDefault(x =>
            string.Equals(
                x.TipoTurno,
                "Regular",
                StringComparison.OrdinalIgnoreCase));

        turno ??= turnos.FirstOrDefault();

        if (turno == null)
            return Json(new { ok = false, configurado = true, message = "No existen turnos activos." });

        var filas = await CargarFilasDistribucionV14Async(
            periodo.FechaBase,
            turno,
            cn,
            null);

        return Json(new
        {
            ok = true,
            configurado = true,

            periodo = new
            {
                vista = periodo.Vista,
                filtroDesde = periodo.FiltroDesde.ToString("yyyy-MM-dd"),
                filtroHasta = periodo.FiltroHasta.ToString("yyyy-MM-dd"),
                edicionDesde = periodo.EdicionDesde.ToString("yyyy-MM-dd"),
                edicionHasta = periodo.EdicionHasta.ToString("yyyy-MM-dd"),
                fechaBase = periodo.FechaBase.ToString("yyyy-MM-dd"),
                diasAplicacion = (periodo.EdicionHasta - periodo.EdicionDesde).Days + 1,
                etiqueta = periodo.Etiqueta,
                semanasMes = periodo.SemanasMes.Select(x => new
                {
                    desde = x.Desde.ToString("yyyy-MM-dd"),
                    hasta = x.Hasta.ToString("yyyy-MM-dd"),
                    etiqueta = x.Etiqueta,
                    seleccionada = x.Seleccionada
                })
            },

            turnoSeleccionadoId = turno.TurnoID,

            turnos = turnos.Select(x => new
            {
                turnoID = x.TurnoID,
                nombre = x.Nombre,
                tipo = x.TipoTurno,
                inicio = x.Inicio.ToString(@"hh\:mm"),
                fin = x.Fin.ToString(@"hh\:mm"),
                cruza = x.Cruza,
                color = x.Color,
                orden = x.Orden
            }),

            filas = filas.Select(x => new
            {
                distribucionID = x.DistribucionID,
                maquinaID = x.MaquinaID,
                centroClave = x.CentroClave,
                maquinaCodigo = x.MaquinaCodigo,
                maquinaNombre = x.MaquinaNombre,
                esEspecial = x.EsEspecial,

                programaProduccionID = x.ProgramaProduccionID,
                parteID = x.ParteID,
                numeroParte = x.NumeroParte,
                referenciaSAP = x.ReferenciaSAP,
                descripcionParte = x.DescripcionParte,
                of = x.OF,
                piezaProgramada = x.PiezaProgramada,
                origenPieza = x.OrigenPieza,

                operadorID = x.OperadorID,
                operadorNombre = x.OperadorNombre,
                numeroControlOperador = x.NumeroControlOperador,

                extras = x.Extras.Select(e => new
                {
                    extraID = e.ExtraID,
                    operadorID = e.OperadorID,
                    numeroControl = e.NumeroControl,
                    nombre = e.Nombre
                }),

                inicio = x.Inicio,
                fin = x.Fin
            })
        });
    }

    [HttpGet("DistribucionV14/Candidatos")]
    public async Task<IActionResult> CandidatosDistribucionV14(int? parteId)
    {
        if (!UsuarioEnSesion())
            return Unauthorized();

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        List<OperadorV13> operadores;

        try
        {
            operadores = await CargarOperadoresDistribucionV13Async(
                parteId,
                cn,
                null);
        }
        catch (SqlException)
        {
            operadores = await CargarOperadoresBasicosV13Async(cn);
        }

        return Json(new
        {
            ok = true,
            operadores = operadores.Select(x => new
            {
                personaID = x.PersonaID,
                numeroControl = x.NumeroControl,
                nombre = x.Nombre,
                nivel = x.Nivel
            })
        });
    }

    [HttpGet("DistribucionV14/Programas")]
    public async Task<IActionResult> ProgramasDistribucionV14(
        string? vista,
        DateTime? fechaDesde,
        DateTime? fechaHasta,
        DateTime? semanaDesde,
        int turnoId,
        int maquinaId)
    {
        if (!UsuarioEnSesion())
            return Unauthorized();

        var periodo = ResolverPeriodoDistribucionV14(
            vista,
            fechaDesde,
            fechaHasta,
            semanaDesde);

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        var turno = (await CargarTurnosDistribucionV13Async(cn, null))
            .FirstOrDefault(x => x.TurnoID == turnoId);

        if (turno == null)
            return BadRequest(new { ok = false, message = "Turno no válido." });

        var programas = new List<ProgramaV14>();

        foreach (var fecha in FechasPeriodoV14(periodo))
        {
            var ventana = VentanaDistribucionV13(fecha, turno);

            foreach (var programa in await CargarProgramasMaquinaV14Async(
                maquinaId,
                ventana.Inicio,
                ventana.Fin,
                cn,
                null))
            {
                if (programas.All(x =>
                    x.ProgramaProduccionID != programa.ProgramaProduccionID))
                {
                    programas.Add(programa);
                }
            }
        }

        return Json(new
        {
            ok = true,
            programas = programas
                .OrderBy(x => x.Inicio)
                .Select(x => new
                {
                    programaProduccionID = x.ProgramaProduccionID,
                    parteID = x.ParteID,
                    numeroParte = x.NumeroParte,
                    referenciaSAP = x.ReferenciaSAP,
                    descripcion = x.Descripcion,
                    of = x.OF,
                    inicio = x.Inicio,
                    fin = x.Fin
                })
        });
    }

    [HttpGet("DistribucionV14/Partes")]
    public async Task<IActionResult> BuscarPartesDistribucionV14(string? q)
    {
        if (!UsuarioEnSesion())
            return Unauthorized();

        var termino = (q ?? string.Empty).Trim();

        if (termino.Length < 2)
            return Json(new { ok = true, partes = Array.Empty<object>() });

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        const string sql = @"
SELECT TOP(40)
    ParteID,
    NumeroParte,
    ISNULL(ReferenciaSAP,N'') AS ReferenciaSAP,
    COALESCE(NULLIF(Designacion,N''),Descripcion,N'') AS Descripcion
FROM dbo.ERP_Partes
WHERE Activo=1
  AND
  (
      NumeroParte LIKE N'%'+@Q+N'%'
      OR ISNULL(ReferenciaSAP,N'') LIKE N'%'+@Q+N'%'
      OR ISNULL(Designacion,N'') LIKE N'%'+@Q+N'%'
      OR ISNULL(Descripcion,N'') LIKE N'%'+@Q+N'%'
  )
ORDER BY NumeroParte,ParteID;";

        var partes = new List<object>();

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@Q", SqlDbType.NVarChar, 120).Value = termino;

        await using var rd = await cmd.ExecuteReaderAsync();

        while (await rd.ReadAsync())
        {
            partes.Add(new
            {
                parteID = Convert.ToInt32(rd["ParteID"]),
                numeroParte = rd["NumeroParte"]?.ToString()?.Trim() ?? string.Empty,
                referenciaSAP = rd["ReferenciaSAP"]?.ToString()?.Trim() ?? string.Empty,
                descripcion = rd["Descripcion"]?.ToString()?.Trim() ?? string.Empty
            });
        }

        return Json(new { ok = true, partes });
    }

    [HttpGet("DistribucionV14/Advertencia")]
    public async Task<IActionResult> AdvertenciaDistribucionV14(
        string? vista,
        DateTime? fechaDesde,
        DateTime? fechaHasta,
        DateTime? semanaDesde,
        int turnoId,
        int? maquinaId,
        string? centroEspecial,
        int operadorId)
    {
        if (!UsuarioEnSesion())
            return Unauthorized();

        var periodo = ResolverPeriodoDistribucionV14(
            vista,
            fechaDesde,
            fechaHasta,
            semanaDesde);

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        await using var tx =
            (SqlTransaction)await cn.BeginTransactionAsync(
                IsolationLevel.ReadCommitted);

        try
        {
            var turno = (await CargarTurnosDistribucionV13Async(cn, tx))
                .FirstOrDefault(x => x.TurnoID == turnoId)
                ?? throw new InvalidOperationException("Turno no válido.");

            string? warning = null;

            foreach (var fecha in FechasPeriodoV14(periodo))
            {
                var ventana = VentanaDistribucionV13(fecha, turno);

                warning = await AdvertenciaCruceOperadorV14Async(
                    operadorId,
                    ventana.Inicio,
                    ventana.Fin,
                    maquinaId,
                    centroEspecial,
                    cn,
                    tx);

                if (!string.IsNullOrWhiteSpace(warning))
                    break;
            }

            await tx.RollbackAsync();

            return Json(new
            {
                ok = true,
                warning
            });
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(); } catch { }

            return BadRequest(new
            {
                ok = false,
                message = ex.Message
            });
        }
    }

    [HttpPost("DistribucionV14/Guardar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarDistribucionV14(
        string? vista,
        DateTime? fechaDesde,
        DateTime? fechaHasta,
        DateTime? semanaDesde,
        int turnoId,
        int? maquinaId,
        string? centroEspecial,
        int? programaProduccionId,
        int? parteId,
        int? operadorId,
        string? motivo,
        string? justificacion)
    {
        if (!UsuarioEnSesion())
            return Unauthorized();

        var periodo = ResolverPeriodoDistribucionV14(
            vista,
            fechaDesde,
            fechaHasta,
            semanaDesde);

        centroEspecial = string.IsNullOrWhiteSpace(centroEspecial)
            ? null
            : centroEspecial.Trim().ToUpperInvariant();

        if (!maquinaId.HasValue && string.IsNullOrWhiteSpace(centroEspecial))
            return BadRequest(new { ok = false, message = "Debes indicar máquina o centro especial." });

        if (maquinaId.HasValue && centroEspecial != null)
            return BadRequest(new { ok = false, message = "La asignación no puede ser máquina y centro especial a la vez." });

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        await using var tx =
            (SqlTransaction)await cn.BeginTransactionAsync(
                IsolationLevel.Serializable);

        try
        {
            if (!await ConfiguradoV14Async(cn, tx))
                throw new InvalidOperationException("Falta ejecutar NSQ_PRODUCCION_PERSONAL_MULTIOPERADOR_V14.sql.");

            var turno = (await CargarTurnosDistribucionV13Async(cn, tx))
                .FirstOrDefault(x => x.TurnoID == turnoId)
                ?? throw new InvalidOperationException("Turno no válido.");

            if (operadorId.HasValue)
            {
                await ValidarOperadorDistribucionV14Async(
                    operadorId.Value,
                    parteId,
                    cn,
                    tx);
            }

            var razon = string.IsNullOrWhiteSpace(motivo)
                ? null
                : motivo.Trim().ToUpperInvariant();

            var detalle = string.IsNullOrWhiteSpace(justificacion)
                ? null
                : justificacion.Trim();

            var usuarioId = UsuarioID();
            var requiereMotivo = false;
            string? advertencia = null;

            foreach (var fecha in FechasPeriodoV14(periodo))
            {
                var ventana = VentanaDistribucionV13(fecha, turno);

                int? programaAnterior = null;
                int? parteAnterior = null;
                int? operadorAnterior = null;

                var distribucionId = await CargarDistribucionActualV14Async(
                    fecha,
                    turnoId,
                    maquinaId,
                    centroEspecial,
                    cn,
                    tx,
                    (p, parte, op) =>
                    {
                        programaAnterior = p;
                        parteAnterior = parte;
                        operadorAnterior = op;
                    });

                if (distribucionId.HasValue &&
                    operadorAnterior != operadorId)
                {
                    requiereMotivo = true;
                }

                if (operadorId.HasValue && advertencia == null)
                {
                    advertencia = await AdvertenciaCruceOperadorV14Async(
                        operadorId.Value,
                        ventana.Inicio,
                        ventana.Fin,
                        maquinaId,
                        centroEspecial,
                        cn,
                        tx);
                }

                var origenPieza = programaProduccionId.HasValue
                    ? "PROGRAMA"
                    : parteId.HasValue
                        ? "MANUAL"
                        : "SIN_PIEZA";

                if (distribucionId.HasValue)
                {
                    const string updateSql = @"
UPDATE dbo.Produccion_DistribucionOperadores
SET ProgramaProduccionID=@ProgramaID,
    ParteID=@ParteID,
    OperadorID=@OperadorID,
    Inicio=@Inicio,
    Fin=@Fin,
    OrigenPieza=@OrigenPieza,
    UsuarioModificacionID=@Usuario,
    FechaModificacion=SYSDATETIME()
WHERE DistribucionID=@ID
  AND Activo=1;";

                    await using var cmd = new SqlCommand(updateSql, cn, tx);

                    cmd.Parameters.Add("@ProgramaID", SqlDbType.Int).Value =
                        programaProduccionId.HasValue ? programaProduccionId.Value : DBNull.Value;
                    cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value =
                        parteId.HasValue ? parteId.Value : DBNull.Value;
                    cmd.Parameters.Add("@OperadorID", SqlDbType.Int).Value =
                        operadorId.HasValue ? operadorId.Value : DBNull.Value;
                    cmd.Parameters.Add("@Inicio", SqlDbType.DateTime2).Value = ventana.Inicio;
                    cmd.Parameters.Add("@Fin", SqlDbType.DateTime2).Value = ventana.Fin;
                    cmd.Parameters.Add("@OrigenPieza", SqlDbType.NVarChar, 30).Value = origenPieza;
                    cmd.Parameters.Add("@Usuario", SqlDbType.Int).Value = usuarioId;
                    cmd.Parameters.Add("@ID", SqlDbType.BigInt).Value = distribucionId.Value;

                    await cmd.ExecuteNonQueryAsync();
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
    @CentroEspecial,
    @ProgramaID,
    @ParteID,
    @OperadorID,
    @Inicio,
    @Fin,
    @OrigenPieza,
    @Usuario,
    SYSDATETIME(),
    1
);";

                    await using var cmd = new SqlCommand(insertSql, cn, tx);

                    cmd.Parameters.Add("@Fecha", SqlDbType.Date).Value = fecha;
                    cmd.Parameters.Add("@TurnoID", SqlDbType.Int).Value = turnoId;
                    cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value =
                        maquinaId.HasValue ? maquinaId.Value : DBNull.Value;
                    cmd.Parameters.Add("@CentroEspecial", SqlDbType.NVarChar, 50).Value =
                        centroEspecial == null ? DBNull.Value : centroEspecial;
                    cmd.Parameters.Add("@ProgramaID", SqlDbType.Int).Value =
                        programaProduccionId.HasValue ? programaProduccionId.Value : DBNull.Value;
                    cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value =
                        parteId.HasValue ? parteId.Value : DBNull.Value;
                    cmd.Parameters.Add("@OperadorID", SqlDbType.Int).Value =
                        operadorId.HasValue ? operadorId.Value : DBNull.Value;
                    cmd.Parameters.Add("@Inicio", SqlDbType.DateTime2).Value = ventana.Inicio;
                    cmd.Parameters.Add("@Fin", SqlDbType.DateTime2).Value = ventana.Fin;
                    cmd.Parameters.Add("@OrigenPieza", SqlDbType.NVarChar, 30).Value = origenPieza;
                    cmd.Parameters.Add("@Usuario", SqlDbType.Int).Value = usuarioId;

                    distribucionId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
                }

                var evento = operadorAnterior != operadorId
                    ? operadorAnterior.HasValue
                        ? "CAMBIO_OPERADOR"
                        : "ASIGNACION_INICIAL"
                    : programaAnterior != programaProduccionId ||
                      parteAnterior != parteId
                        ? "CAMBIO_PIEZA"
                        : "ACTUALIZACION";

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
    @CentroEspecial,
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
                hist.Parameters.Add("@Fecha", SqlDbType.Date).Value = fecha;
                hist.Parameters.Add("@TurnoID", SqlDbType.Int).Value = turnoId;
                hist.Parameters.Add("@MaquinaID", SqlDbType.Int).Value =
                    maquinaId.HasValue ? maquinaId.Value : DBNull.Value;
                hist.Parameters.Add("@CentroEspecial", SqlDbType.NVarChar, 50).Value =
                    centroEspecial == null ? DBNull.Value : centroEspecial;
                hist.Parameters.Add("@ProgramaAnteriorID", SqlDbType.Int).Value =
                    programaAnterior.HasValue ? programaAnterior.Value : DBNull.Value;
                hist.Parameters.Add("@ProgramaNuevoID", SqlDbType.Int).Value =
                    programaProduccionId.HasValue ? programaProduccionId.Value : DBNull.Value;
                hist.Parameters.Add("@ParteAnteriorID", SqlDbType.Int).Value =
                    parteAnterior.HasValue ? parteAnterior.Value : DBNull.Value;
                hist.Parameters.Add("@ParteNuevaID", SqlDbType.Int).Value =
                    parteId.HasValue ? parteId.Value : DBNull.Value;
                hist.Parameters.Add("@OperadorAnteriorID", SqlDbType.Int).Value =
                    operadorAnterior.HasValue ? operadorAnterior.Value : DBNull.Value;
                hist.Parameters.Add("@OperadorNuevoID", SqlDbType.Int).Value =
                    operadorId.HasValue ? operadorId.Value : DBNull.Value;
                hist.Parameters.Add("@Motivo", SqlDbType.NVarChar, 60).Value =
                    operadorAnterior != operadorId && !string.IsNullOrWhiteSpace(razon)
                        ? razon
                        : evento;
                hist.Parameters.Add("@Justificacion", SqlDbType.NVarChar, 500).Value =
                    string.IsNullOrWhiteSpace(detalle) ? DBNull.Value : detalle;
                hist.Parameters.Add("@Usuario", SqlDbType.Int).Value = usuarioId;

                await hist.ExecuteNonQueryAsync();
            }

            if (requiereMotivo &&
                (string.IsNullOrWhiteSpace(razon) ||
                 string.IsNullOrWhiteSpace(detalle) ||
                 detalle.Length < 5))
            {
                throw new InvalidOperationException(
                    "Para cambiar o retirar un operador selecciona un motivo y escribe una justificación de al menos 5 caracteres.");
            }

            await tx.CommitAsync();

            return Json(new
            {
                ok = true,
                warning = advertencia,
                diasAplicados = (periodo.EdicionHasta - periodo.EdicionDesde).Days + 1
            });
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(); } catch { }

            return BadRequest(new
            {
                ok = false,
                message = ex.Message
            });
        }
    }

    [HttpPost("DistribucionV14/Extra")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarExtraDistribucionV14(
        string? vista,
        DateTime? fechaDesde,
        DateTime? fechaHasta,
        DateTime? semanaDesde,
        int turnoId,
        int? maquinaId,
        string? centroEspecial,
        int? operadorAnteriorId,
        int? operadorNuevoId,
        bool eliminar,
        int? parteId,
        string? motivo,
        string? justificacion)
    {
        if (!UsuarioEnSesion())
            return Unauthorized();

        var periodo = ResolverPeriodoDistribucionV14(
            vista,
            fechaDesde,
            fechaHasta,
            semanaDesde);

        centroEspecial = string.IsNullOrWhiteSpace(centroEspecial)
            ? null
            : centroEspecial.Trim().ToUpperInvariant();

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        await using var tx =
            (SqlTransaction)await cn.BeginTransactionAsync(
                IsolationLevel.Serializable);

        try
        {
            if (!await ConfiguradoV14Async(cn, tx))
                throw new InvalidOperationException("Falta ejecutar NSQ_PRODUCCION_PERSONAL_MULTIOPERADOR_V14.sql.");

            var turno = (await CargarTurnosDistribucionV13Async(cn, tx))
                .FirstOrDefault(x => x.TurnoID == turnoId)
                ?? throw new InvalidOperationException("Turno no válido.");

            if (!eliminar && !operadorNuevoId.HasValue)
                throw new InvalidOperationException("Selecciona un operador.");

            if (operadorNuevoId.HasValue)
            {
                await ValidarOperadorDistribucionV14Async(
                    operadorNuevoId.Value,
                    parteId,
                    cn,
                    tx);
            }

            var esCambio = operadorAnteriorId.HasValue;

            var razon = string.IsNullOrWhiteSpace(motivo)
                ? null
                : motivo.Trim().ToUpperInvariant();

            var detalle = string.IsNullOrWhiteSpace(justificacion)
                ? null
                : justificacion.Trim();

            if (esCambio &&
                (string.IsNullOrWhiteSpace(razon) ||
                 string.IsNullOrWhiteSpace(detalle) ||
                 detalle.Length < 5))
            {
                throw new InvalidOperationException(
                    "Para cambiar o retirar un operador adicional selecciona un motivo y escribe una justificación.");
            }

            var usuarioId = UsuarioID();
            string? advertencia = null;

            foreach (var fecha in FechasPeriodoV14(periodo))
            {
                var ventana = VentanaDistribucionV13(fecha, turno);

                if (operadorNuevoId.HasValue && advertencia == null)
                {
                    advertencia = await AdvertenciaCruceOperadorV14Async(
                        operadorNuevoId.Value,
                        ventana.Inicio,
                        ventana.Fin,
                        maquinaId,
                        centroEspecial,
                        cn,
                        tx);
                }

                long? extraId = null;

                if (operadorAnteriorId.HasValue)
                {
                    const string actualSql = @"
SELECT TOP(1) ExtraID
FROM dbo.Produccion_DistribucionOperadoresExtra WITH(UPDLOCK,HOLDLOCK)
WHERE Activo=1
  AND FechaTrabajo=@Fecha
  AND TurnoID=@TurnoID
  AND OperadorID=@OperadorAnteriorID
  AND
  (
      (@MaquinaID IS NOT NULL AND MaquinaID=@MaquinaID)
      OR
      (@MaquinaID IS NULL
       AND UPPER(ISNULL(CentroEspecial,N''))=
           UPPER(ISNULL(@CentroEspecial,N'')))
  )
ORDER BY ExtraID DESC;";

                    await using var actual = new SqlCommand(actualSql, cn, tx);

                    actual.Parameters.Add("@Fecha", SqlDbType.Date).Value = fecha;
                    actual.Parameters.Add("@TurnoID", SqlDbType.Int).Value = turnoId;
                    actual.Parameters.Add("@OperadorAnteriorID", SqlDbType.Int).Value = operadorAnteriorId.Value;
                    actual.Parameters.Add("@MaquinaID", SqlDbType.Int).Value =
                        maquinaId.HasValue ? maquinaId.Value : DBNull.Value;
                    actual.Parameters.Add("@CentroEspecial", SqlDbType.NVarChar, 50).Value =
                        centroEspecial == null ? DBNull.Value : centroEspecial;

                    var value = await actual.ExecuteScalarAsync();

                    if (value != null && value != DBNull.Value)
                        extraId = Convert.ToInt64(value);
                }

                if (eliminar)
                {
                    if (extraId.HasValue)
                    {
                        const string delSql = @"
UPDATE dbo.Produccion_DistribucionOperadoresExtra
SET Activo=0,
    UsuarioModificacionID=@Usuario,
    FechaModificacion=SYSDATETIME()
WHERE ExtraID=@ExtraID;";

                        await using var del = new SqlCommand(delSql, cn, tx);
                        del.Parameters.Add("@Usuario", SqlDbType.Int).Value = usuarioId;
                        del.Parameters.Add("@ExtraID", SqlDbType.BigInt).Value = extraId.Value;
                        await del.ExecuteNonQueryAsync();
                    }
                }
                else if (extraId.HasValue)
                {
                    const string updateSql = @"
UPDATE dbo.Produccion_DistribucionOperadoresExtra
SET OperadorID=@OperadorNuevoID,
    Inicio=@Inicio,
    Fin=@Fin,
    UsuarioModificacionID=@Usuario,
    FechaModificacion=SYSDATETIME(),
    Activo=1
WHERE ExtraID=@ExtraID;";

                    await using var update = new SqlCommand(updateSql, cn, tx);
                    update.Parameters.Add("@OperadorNuevoID", SqlDbType.Int).Value = operadorNuevoId!.Value;
                    update.Parameters.Add("@Inicio", SqlDbType.DateTime2).Value = ventana.Inicio;
                    update.Parameters.Add("@Fin", SqlDbType.DateTime2).Value = ventana.Fin;
                    update.Parameters.Add("@Usuario", SqlDbType.Int).Value = usuarioId;
                    update.Parameters.Add("@ExtraID", SqlDbType.BigInt).Value = extraId.Value;
                    await update.ExecuteNonQueryAsync();
                }
                else
                {
                    const string existsPrimarySql = @"
SELECT CONVERT(bit,CASE WHEN EXISTS
(
    SELECT 1
    FROM dbo.Produccion_DistribucionOperadores
    WHERE Activo=1
      AND FechaTrabajo=@Fecha
      AND TurnoID=@TurnoID
      AND OperadorID=@OperadorID
      AND
      (
          (@MaquinaID IS NOT NULL AND MaquinaID=@MaquinaID)
          OR
          (@MaquinaID IS NULL
           AND UPPER(ISNULL(CentroEspecial,N''))=
               UPPER(ISNULL(@CentroEspecial,N'')))
      )
)
THEN 1 ELSE 0 END);";

                    await using (var existsPrimary = new SqlCommand(existsPrimarySql, cn, tx))
                    {
                        existsPrimary.Parameters.Add("@Fecha", SqlDbType.Date).Value = fecha;
                        existsPrimary.Parameters.Add("@TurnoID", SqlDbType.Int).Value = turnoId;
                        existsPrimary.Parameters.Add("@OperadorID", SqlDbType.Int).Value = operadorNuevoId!.Value;
                        existsPrimary.Parameters.Add("@MaquinaID", SqlDbType.Int).Value =
                            maquinaId.HasValue ? maquinaId.Value : DBNull.Value;
                        existsPrimary.Parameters.Add("@CentroEspecial", SqlDbType.NVarChar, 50).Value =
                            centroEspecial == null ? DBNull.Value : centroEspecial;

                        if (Convert.ToBoolean(await existsPrimary.ExecuteScalarAsync() ?? false))
                        {
                            throw new InvalidOperationException(
                                "Ese operador ya es el operador principal de esta máquina.");
                        }
                    }

                    const string insertSql = @"
IF NOT EXISTS
(
    SELECT 1
    FROM dbo.Produccion_DistribucionOperadoresExtra
    WHERE Activo=1
      AND FechaTrabajo=@Fecha
      AND TurnoID=@TurnoID
      AND OperadorID=@OperadorID
      AND
      (
          (@MaquinaID IS NOT NULL AND MaquinaID=@MaquinaID)
          OR
          (@MaquinaID IS NULL
           AND UPPER(ISNULL(CentroEspecial,N''))=
               UPPER(ISNULL(@CentroEspecial,N'')))
      )
)
BEGIN
    INSERT dbo.Produccion_DistribucionOperadoresExtra
    (
        FechaTrabajo,
        TurnoID,
        MaquinaID,
        CentroEspecial,
        OperadorID,
        Inicio,
        Fin,
        UsuarioCreacionID,
        FechaCreacion,
        Activo
    )
    OUTPUT INSERTED.ExtraID
    VALUES
    (
        @Fecha,
        @TurnoID,
        @MaquinaID,
        @CentroEspecial,
        @OperadorID,
        @Inicio,
        @Fin,
        @Usuario,
        SYSDATETIME(),
        1
    );
END;";

                    await using var insert = new SqlCommand(insertSql, cn, tx);

                    insert.Parameters.Add("@Fecha", SqlDbType.Date).Value = fecha;
                    insert.Parameters.Add("@TurnoID", SqlDbType.Int).Value = turnoId;
                    insert.Parameters.Add("@MaquinaID", SqlDbType.Int).Value =
                        maquinaId.HasValue ? maquinaId.Value : DBNull.Value;
                    insert.Parameters.Add("@CentroEspecial", SqlDbType.NVarChar, 50).Value =
                        centroEspecial == null ? DBNull.Value : centroEspecial;
                    insert.Parameters.Add("@OperadorID", SqlDbType.Int).Value = operadorNuevoId!.Value;
                    insert.Parameters.Add("@Inicio", SqlDbType.DateTime2).Value = ventana.Inicio;
                    insert.Parameters.Add("@Fin", SqlDbType.DateTime2).Value = ventana.Fin;
                    insert.Parameters.Add("@Usuario", SqlDbType.Int).Value = usuarioId;

                    var result = await insert.ExecuteScalarAsync();

                    if (result != null && result != DBNull.Value)
                        extraId = Convert.ToInt64(result);
                }

                if (extraId.HasValue)
                {
                    const string histSql = @"
INSERT dbo.Produccion_DistribucionOperadoresExtraHistorial
(
    ExtraID,
    FechaTrabajo,
    TurnoID,
    MaquinaID,
    CentroEspecial,
    OperadorAnteriorID,
    OperadorNuevoID,
    Motivo,
    Justificacion,
    UsuarioID,
    FechaMovimiento
)
VALUES
(
    @ExtraID,
    @Fecha,
    @TurnoID,
    @MaquinaID,
    @CentroEspecial,
    @OperadorAnteriorID,
    @OperadorNuevoID,
    @Motivo,
    @Justificacion,
    @Usuario,
    SYSDATETIME()
);";

                    await using var hist = new SqlCommand(histSql, cn, tx);

                    hist.Parameters.Add("@ExtraID", SqlDbType.BigInt).Value = extraId.Value;
                    hist.Parameters.Add("@Fecha", SqlDbType.Date).Value = fecha;
                    hist.Parameters.Add("@TurnoID", SqlDbType.Int).Value = turnoId;
                    hist.Parameters.Add("@MaquinaID", SqlDbType.Int).Value =
                        maquinaId.HasValue ? maquinaId.Value : DBNull.Value;
                    hist.Parameters.Add("@CentroEspecial", SqlDbType.NVarChar, 50).Value =
                        centroEspecial == null ? DBNull.Value : centroEspecial;
                    hist.Parameters.Add("@OperadorAnteriorID", SqlDbType.Int).Value =
                        operadorAnteriorId.HasValue ? operadorAnteriorId.Value : DBNull.Value;
                    hist.Parameters.Add("@OperadorNuevoID", SqlDbType.Int).Value =
                        eliminar || !operadorNuevoId.HasValue ? DBNull.Value : operadorNuevoId.Value;
                    hist.Parameters.Add("@Motivo", SqlDbType.NVarChar, 80).Value =
                        !string.IsNullOrWhiteSpace(razon)
                            ? razon
                            : operadorAnteriorId.HasValue
                                ? "CAMBIO_OPERADOR_ADICIONAL"
                                : "AGREGAR_OPERADOR_ADICIONAL";
                    hist.Parameters.Add("@Justificacion", SqlDbType.NVarChar, 500).Value =
                        string.IsNullOrWhiteSpace(detalle) ? DBNull.Value : detalle;
                    hist.Parameters.Add("@Usuario", SqlDbType.Int).Value = usuarioId;

                    await hist.ExecuteNonQueryAsync();
                }
            }

            await tx.CommitAsync();

            return Json(new
            {
                ok = true,
                warning = advertencia,
                diasAplicados = (periodo.EdicionHasta - periodo.EdicionDesde).Days + 1
            });
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(); } catch { }

            return BadRequest(new
            {
                ok = false,
                message = ex.Message
            });
        }
    }

    [HttpGet("DistribucionV14/Pdf")]
    public async Task<IActionResult> PdfDistribucionV14(
        DateTime fechaDesde,
        DateTime fechaHasta,
        string? turnos,
        [FromServices] IWebHostEnvironment environment)
    {
        if (!UsuarioEnSesion())
            return Unauthorized();

        var desde = fechaDesde.Date;
        var hasta = fechaHasta.Date;

        if (hasta < desde)
            (desde, hasta) = (hasta, desde);

        if ((hasta - desde).TotalDays > 31)
            return BadRequest("El rango máximo del PDF es de 32 días.");

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        if (!await ConfiguradoV14Async(cn, null))
            return BadRequest("Falta ejecutar la estructura V14.");

        var todosTurnos = await CargarTurnosDistribucionV13Async(cn, null);
        var ids = new HashSet<int>();

        foreach (var token in (turnos ?? string.Empty).Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(token, out var id))
                ids.Add(id);
        }

        var seleccionados = ids.Count == 0
            ? todosTurnos
            : todosTurnos.Where(x => ids.Contains(x.TurnoID)).ToList();

        if (seleccionados.Count == 0)
            return BadRequest("No se seleccionaron turnos válidos.");

        var grupos = new List<(DateTime Fecha, DistribucionTurnoV13 Turno, List<FilaDistribucionV14> Filas)>();

        for (var fecha = desde; fecha <= hasta; fecha = fecha.AddDays(1))
        {
            foreach (var turno in seleccionados)
            {
                grupos.Add((
                    fecha,
                    turno,
                    await CargarFilasDistribucionV14Async(
                        fecha,
                        turno,
                        cn,
                        null)
                ));
            }
        }

        ConfigurarQuestPdfDistribucionV13();

        byte[]? logo = null;

        var webRoot = string.IsNullOrWhiteSpace(environment.WebRootPath)
            ? System.IO.Path.Combine(environment.ContentRootPath, "wwwroot")
            : environment.WebRootPath;

        var logoPath = System.IO.Path.Combine(webRoot, "Imagenes", "logo-quell.png");

        if (System.IO.File.Exists(logoPath))
            logo = await System.IO.File.ReadAllBytesAsync(logoPath);

        static string EtiquetaTurnoPdfV143(DistribucionTurnoV13 turno)
        {
            var tipo = (turno.TipoTurno ?? string.Empty).Trim();

            if (string.Equals(tipo, "Mixto", StringComparison.OrdinalIgnoreCase))
                return "TURNO MIXTO";

            if (string.Equals(tipo, "12x12", StringComparison.OrdinalIgnoreCase))
                return turno.Inicio.Hours < 12
                    ? "TURNO 12x12 DÍA"
                    : "TURNO 12x12 NOCHE";

            if (turno.Inicio == new TimeSpan(7, 0, 0) &&
                turno.Fin == new TimeSpan(15, 0, 0))
                return "TURNO 1";

            if (turno.Inicio == new TimeSpan(15, 0, 0) &&
                turno.Fin == new TimeSpan(22, 30, 0))
                return "TURNO 2";

            if (turno.Inicio == new TimeSpan(22, 30, 0) &&
                turno.Fin == new TimeSpan(7, 0, 0))
                return "TURNO 3";

            return $"TURNO {turno.Nombre}".Trim();
        }

        static void HeaderCellV143(IContainer cell, string text)
        {
            cell.Background("#0B5A93")
                .PaddingVertical(3.5f)
                .PaddingHorizontal(5)
                .AlignMiddle()
                .Text(text)
                .FontColor(Colors.White)
                .FontSize(7)
                .Bold();
        }

        static void BodyCellV143(
            IContainer cell,
            string text,
            bool strong,
            bool alternate)
        {
            IContainer container = cell;

            if (alternate)
                container = container.Background("#F7FAFD");

            var descriptor = container
                .BorderBottom(0.4f)
                .BorderColor("#D7E1EB")
                .PaddingVertical(3.1f)
                .PaddingHorizontal(4.5f)
                .AlignMiddle()
                .Text(text)
                .FontSize(6.8f)
                .FontColor("#203C57");

            if (strong)
                descriptor.Bold();
        }

        var documento = Document.Create(document =>
        {
            foreach (var grupo in grupos)
            {
                document.Page(page =>
                {
                    var etiquetaTurno = EtiquetaTurnoPdfV143(grupo.Turno);
                    var horario = $"{grupo.Turno.Inicio:hh\\:mm} - {grupo.Turno.Fin:hh\\:mm}";

                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(11);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x =>
                        x.FontSize(7)
                         .FontColor("#203C57"));

                    page.Header()
                        .PaddingBottom(5)
                        .Row(row =>
                        {
                            if (logo != null)
                            {
                                row.ConstantItem(52)
                                    .Height(30)
                                    .AlignMiddle()
                                    .Image(logo)
                                    .FitArea();
                            }
                            else
                            {
                                row.ConstantItem(52)
                                    .AlignMiddle()
                                    .Text("NS QUELL")
                                    .Bold()
                                    .FontSize(11)
                                    .FontColor("#0B5A93");
                            }

                            row.RelativeItem()
                                .AlignMiddle()
                                .Column(col =>
                                {
                                    col.Spacing(1);

                                    col.Item()
                                        .Text("DISTRIBUCIÓN DE PERSONAL")
                                        .Bold()
                                        .FontSize(12.5f)
                                        .FontColor("#0B4F87");

                                    col.Item()
                                        .Text($"{grupo.Fecha:dd/MM/yyyy}  |  {etiquetaTurno}  |  {horario}")
                                        .FontSize(7.2f)
                                        .FontColor("#62778D");
                                });

                            row.ConstantItem(112)
                                .AlignRight()
                                .AlignMiddle()
                                .Text($"Generado {DateTime.Now:dd/MM/yyyy HH:mm}")
                                .FontSize(5.8f)
                                .FontColor("#8593A2");
                        });

                    page.Content()
                        .Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.ConstantColumn(82);
                                columns.RelativeColumn(1.2f);
                                columns.RelativeColumn(1.65f);
                                columns.RelativeColumn(2.25f);
                            });

                            table.Header(header =>
                            {
                                HeaderCellV143(header.Cell(), "MÁQUINA / PUESTO");
                                HeaderCellV143(header.Cell(), "PIEZA / REFERENCIA");
                                HeaderCellV143(header.Cell(), "DESCRIPCIÓN");
                                HeaderCellV143(header.Cell(), "OPERADORES");
                            });

                            var rowIndex = 0;

                            foreach (var fila in grupo.Filas)
                            {
                                var alternate = rowIndex % 2 == 1;
                                rowIndex++;

                                var referencia = fila.EsEspecial
                                    ? "TORNILLO"
                                    : string.Join(
                                        " / ",
                                        new[]
                                        {
                                            fila.NumeroParte,
                                            fila.ReferenciaSAP
                                        }
                                        .Where(x => !string.IsNullOrWhiteSpace(x)));

                                if (string.IsNullOrWhiteSpace(referencia))
                                    referencia = "SIN PIEZA PROGRAMADA";

                                var operadores = new List<string>();

                                if (fila.OperadorID.HasValue)
                                {
                                    operadores.Add(
                                        string.IsNullOrWhiteSpace(fila.NumeroControlOperador)
                                            ? fila.OperadorNombre
                                            : $"{fila.NumeroControlOperador} - {fila.OperadorNombre}");
                                }

                                operadores.AddRange(
                                    fila.Extras.Select(x =>
                                        string.IsNullOrWhiteSpace(x.NumeroControl)
                                            ? $"+ {x.Nombre}"
                                            : $"+ {x.NumeroControl} - {x.Nombre}"));

                                if (operadores.Count == 0)
                                    operadores.Add("SIN ASIGNAR");

                                BodyCellV143(
                                    table.Cell(),
                                    fila.EsEspecial
                                        ? "TORNILLO"
                                        : string.IsNullOrWhiteSpace(fila.MaquinaNombre)
                                            ? fila.MaquinaCodigo
                                            : $"{fila.MaquinaCodigo}\n{fila.MaquinaNombre}",
                                    true,
                                    alternate);

                                BodyCellV143(
                                    table.Cell(),
                                    referencia,
                                    true,
                                    alternate);

                                BodyCellV143(
                                    table.Cell(),
                                    fila.EsEspecial
                                        ? "Puesto especial"
                                        : string.IsNullOrWhiteSpace(fila.DescripcionParte)
                                            ? "-"
                                            : fila.DescripcionParte,
                                    false,
                                    alternate);

                                BodyCellV143(
                                    table.Cell(),
                                    string.Join("\n", operadores),
                                    fila.OperadorID.HasValue || fila.Extras.Count > 0,
                                    alternate);
                            }
                        });

                    page.Footer()
                        .PaddingTop(4)
                        .Row(row =>
                        {
                            row.RelativeItem()
                                .Text("NS Quell | Distribución de Personal")
                                .FontSize(5.8f)
                                .FontColor("#8795A4");

                            row.ConstantItem(80)
                                .AlignRight()
                                .Text(text =>
                                {
                                    text.DefaultTextStyle(x =>
                                        x.FontSize(5.8f)
                                         .FontColor("#8795A4"));

                                    text.Span("Página ");
                                    text.CurrentPageNumber();
                                    text.Span(" / ");
                                    text.TotalPages();
                                });
                        });
                });
            }
        });

        var pdf = documento.GeneratePdf();

        var nombreVisible = desde == hasta
            ? $"DISTRIBUCIÓN DE PERSONAL - {desde:dd-MM-yyyy}.pdf"
            : $"DISTRIBUCIÓN DE PERSONAL - {desde:dd-MM-yyyy} AL {hasta:dd-MM-yyyy}.pdf";

        var nombreAscii = desde == hasta
            ? $"DISTRIBUCION DE PERSONAL - {desde:dd-MM-yyyy}.pdf"
            : $"DISTRIBUCION DE PERSONAL - {desde:dd-MM-yyyy} AL {hasta:dd-MM-yyyy}.pdf";

        Response.Headers["Content-Disposition"] =
            $"inline; filename=\"{nombreAscii}\"; filename*=UTF-8''{Uri.EscapeDataString(nombreVisible)}";

        return File(pdf, "application/pdf");
    }


}
