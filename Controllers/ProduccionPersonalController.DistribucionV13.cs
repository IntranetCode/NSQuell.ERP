using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Data;

namespace ERP.NSQuell.Controllers;

// NSQ_PRODUCCION_PERSONAL_DISTRIBUCION_V13
public sealed partial class ProduccionPersonalController
{
    private sealed class DistribucionTurnoV13
    {
        public int TurnoID { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string TipoTurno { get; set; } = string.Empty;
        public TimeSpan Inicio { get; set; }
        public TimeSpan Fin { get; set; }
        public bool Cruza { get; set; }
        public string Color { get; set; } = "#64748B";
        public int Orden { get; set; }
    }

    private sealed class DistribucionFilaV13
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
        public bool PiezaSugerida { get; set; }

        public int? OperadorID { get; set; }
        public string OperadorNombre { get; set; } = string.Empty;
        public string NumeroControlOperador { get; set; } = string.Empty;

        public DateTime Inicio { get; set; }
        public DateTime Fin { get; set; }
    }

    private sealed class OperadorV13
    {
        public int PersonaID { get; set; }
        public string NumeroControl { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public int? Nivel { get; set; }
    }

    private static (DateTime Inicio, DateTime Fin) VentanaDistribucionV13(
        DateTime fecha,
        DistribucionTurnoV13 turno)
    {
        var inicio = fecha.Date.Add(turno.Inicio);
        var fin = fecha.Date.Add(turno.Fin);

        if (turno.Cruza || fin <= inicio)
            fin = fin.AddDays(1);

        return (inicio, fin);
    }

    private static async Task<bool> TablaDistribucionV13Async(
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

    private static async Task<List<DistribucionTurnoV13>> CargarTurnosDistribucionV13Async(
        SqlConnection cn,
        SqlTransaction? tx)
    {
        const string sql = @"
SELECT
    TurnoID,
    Nombre,
    TipoTurno,
    HoraInicio,
    HoraFin,
    CruzaDiaSiguiente,
    Color,
    Orden
FROM dbo.RRHH_Turnos
WHERE Activo=1
  AND EsFlexible=0
  AND HoraInicio IS NOT NULL
  AND HoraFin IS NOT NULL
ORDER BY Orden,TurnoID;";

        var lista = new List<DistribucionTurnoV13>();

        await using var cmd = tx == null
            ? new SqlCommand(sql, cn)
            : new SqlCommand(sql, cn, tx);

        await using var rd = await cmd.ExecuteReaderAsync();

        while (await rd.ReadAsync())
        {
            lista.Add(new DistribucionTurnoV13
            {
                TurnoID = Convert.ToInt32(rd["TurnoID"]),
                Nombre = rd["Nombre"]?.ToString()?.Trim() ?? string.Empty,
                TipoTurno = rd["TipoTurno"]?.ToString()?.Trim() ?? string.Empty,
                Inicio = (TimeSpan)rd["HoraInicio"],
                Fin = (TimeSpan)rd["HoraFin"],
                Cruza = Convert.ToBoolean(rd["CruzaDiaSiguiente"]),
                Color = rd["Color"]?.ToString()?.Trim() ?? "#64748B",
                Orden = Convert.ToInt32(rd["Orden"])
            });
        }

        return lista;
    }

    private static async Task<List<OperadorV13>> CargarOperadoresDistribucionV13Async(
        int? parteId,
        SqlConnection cn,
        SqlTransaction? tx)
    {
        var tieneVista = false;

        const string vistaSql = @"
SELECT CONVERT(bit,CASE WHEN
    OBJECT_ID(N'dbo.vw_RRHH_PolivalenciaOperadoresParte',N'V') IS NOT NULL
THEN 1 ELSE 0 END);";

        await using (var check = tx == null
            ? new SqlCommand(vistaSql, cn)
            : new SqlCommand(vistaSql, cn, tx))
        {
            tieneVista = Convert.ToBoolean(await check.ExecuteScalarAsync() ?? false);
        }

        var usarMatriz = false;

        if (parteId.HasValue && parteId.Value > 0 && tieneVista)
        {
            const string matrizSql = @"
SELECT CONVERT(bit,CASE WHEN EXISTS
(
    SELECT 1
    FROM dbo.vw_RRHH_PolivalenciaOperadoresParte
    WHERE ParteID=@ParteID
      AND Nivel BETWEEN 1 AND 4
)
THEN 1 ELSE 0 END);";

            await using var matrizCmd = tx == null
                ? new SqlCommand(matrizSql, cn)
                : new SqlCommand(matrizSql, cn, tx);

            matrizCmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = parteId.Value;

            usarMatriz = Convert.ToBoolean(await matrizCmd.ExecuteScalarAsync() ?? false);
        }

        string sql;

        if (usarMatriz)
        {
            sql = @"
SELECT DISTINCT
    p.PersonaID,
    ISNULL(p.NumeroControl,N'') AS NumeroControl,
    LTRIM(RTRIM(CONCAT(
        ISNULL(p.Nombre,N''),N' ',
        ISNULL(p.ApellidoPaterno,N''),N' ',
        ISNULL(p.ApellidoMaterno,N'')))) AS Nombre,
    CONVERT(int,v.Nivel) AS Nivel
FROM dbo.vw_RRHH_PolivalenciaOperadoresParte v
INNER JOIN dbo.Persona p
    ON p.PersonaID=v.PersonalID
   AND ISNULL(p.EsColaboradorActivo,1)=1
WHERE v.ParteID=@ParteID
  AND v.Nivel BETWEEN 1 AND 4
ORDER BY Nivel DESC,Nombre,p.PersonaID;";
        }
        else
        {
            sql = @"
SELECT DISTINCT
    p.PersonaID,
    ISNULL(p.NumeroControl,N'') AS NumeroControl,
    LTRIM(RTRIM(CONCAT(
        ISNULL(p.Nombre,N''),N' ',
        ISNULL(p.ApellidoPaterno,N''),N' ',
        ISNULL(p.ApellidoMaterno,N'')))) AS Nombre,
    CAST(NULL AS int) AS Nivel
FROM dbo.Persona p
WHERE ISNULL(p.EsColaboradorActivo,1)=1
  AND
  (
      UPPER(ISNULL(p.Puesto,N'')) COLLATE Modern_Spanish_CI_AI LIKE N'%OPERADOR%'
      OR EXISTS
      (
          SELECT 1
          FROM dbo.RRHH_PolivalenciaCompetencias c
          WHERE c.PersonalID=p.PersonaID
            AND c.Activo=1
            AND UPPER(LTRIM(RTRIM(ISNULL(c.PuestoMatriz,N''))))
                COLLATE Modern_Spanish_CI_AI=N'OPERADOR'
      )
  )
ORDER BY Nombre,p.PersonaID;";
        }

        var lista = new List<OperadorV13>();

        await using var cmd = tx == null
            ? new SqlCommand(sql, cn)
            : new SqlCommand(sql, cn, tx);

        if (usarMatriz)
            cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = parteId!.Value;

        await using var rd = await cmd.ExecuteReaderAsync();

        while (await rd.ReadAsync())
        {
            lista.Add(new OperadorV13
            {
                PersonaID = Convert.ToInt32(rd["PersonaID"]),
                NumeroControl = rd["NumeroControl"]?.ToString()?.Trim() ?? string.Empty,
                Nombre = rd["Nombre"]?.ToString()?.Trim() ?? string.Empty,
                Nivel = rd["Nivel"] == DBNull.Value ? null : Convert.ToInt32(rd["Nivel"])
            });
        }

        return lista;
    }

    private static async Task<List<DistribucionFilaV13>> CargarFilasDistribucionV13Async(
        DateTime fecha,
        DistribucionTurnoV13 turno,
        SqlConnection cn,
        SqlTransaction? tx)
    {
        var ventana = VentanaDistribucionV13(fecha, turno);

        const string sql = @"
SELECT
    m.MaquinaID,
    m.Codigo AS MaquinaCodigo,
    m.Nombre AS MaquinaNombre,

    d.DistribucionID,
    d.ProgramaProduccionID AS ProgramaGuardadoID,
    d.ParteID AS ParteGuardadaID,
    d.OperadorID,

    op.NumeroControl AS NumeroControlOperador,
    LTRIM(RTRIM(CONCAT(
        ISNULL(op.Nombre,N''),N' ',
        ISNULL(op.ApellidoPaterno,N''),N' ',
        ISNULL(op.ApellidoMaterno,N'')))) AS OperadorNombre,

    pSel.NumeroParte AS NumeroParteGuardado,
    pSel.ReferenciaSAP AS ReferenciaSAPGuardado,
    COALESCE(NULLIF(pSel.Designacion,N''),pSel.Descripcion,N'') AS DescripcionGuardada,

    prog.ProgramaProduccionID AS ProgramaSugeridoID,
    prog.ParteID AS ParteSugeridaID,
    prog.NumeroParte AS NumeroParteSugerido,
    prog.ReferenciaSAP AS ReferenciaSAPSugerida,
    prog.Descripcion AS DescripcionSugerida,
    prog.OF
FROM dbo.ERP_Maquinas m
LEFT JOIN dbo.Produccion_DistribucionOperadores d
    ON d.Activo=1
   AND d.FechaTrabajo=@Fecha
   AND d.TurnoID=@TurnoID
   AND d.MaquinaID=m.MaquinaID
LEFT JOIN dbo.Persona op
    ON op.PersonaID=d.OperadorID
LEFT JOIN dbo.ERP_Partes pSel
    ON pSel.ParteID=d.ParteID
OUTER APPLY
(
    SELECT TOP(1)
        pp.ProgramaProduccionID,
        pp.ParteID,
        ISNULL(pp.NumeroParte,N'') AS NumeroParte,
        ISNULL(ep.ReferenciaSAP,N'') AS ReferenciaSAP,
        COALESCE(
            NULLIF(pp.DesignacionDescripcionSAP,N''),
            NULLIF(ep.Designacion,N''),
            ep.Descripcion,
            N'') AS Descripcion,
        COALESCE(
            NULLIF(LTRIM(RTRIM(s.NumeroOFRecibida)),N''),
            NULLIF(LTRIM(RTRIM(s.FolioSolicitud)),N''),
            N'') AS [OF]
    FROM dbo.Planeacion_ProgramaProduccion pp
    LEFT JOIN dbo.ERP_Partes ep
        ON ep.ParteID=pp.ParteID
    LEFT JOIN dbo.SolicitudesProduccion s
        ON s.SolicitudProduccionID=pp.SolicitudProduccionID
    WHERE pp.Activo=1
      AND pp.MaquinaID=m.MaquinaID
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
        pp.ProgramaProduccionID
) prog
WHERE m.Activo=1
  AND UPPER(ISNULL(m.Area,N'')) COLLATE Modern_Spanish_CI_AI LIKE N'%INYE%'
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

        var lista = new List<DistribucionFilaV13>();

        await using (var cmd = tx == null
            ? new SqlCommand(sql, cn)
            : new SqlCommand(sql, cn, tx))
        {
            cmd.Parameters.Add("@Fecha", SqlDbType.Date).Value = fecha.Date;
            cmd.Parameters.Add("@TurnoID", SqlDbType.Int).Value = turno.TurnoID;
            cmd.Parameters.Add("@Inicio", SqlDbType.DateTime2).Value = ventana.Inicio;
            cmd.Parameters.Add("@Fin", SqlDbType.DateTime2).Value = ventana.Fin;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                var guardado = rd["DistribucionID"] != DBNull.Value;

                int? parteGuardada = rd["ParteGuardadaID"] == DBNull.Value
                    ? null
                    : Convert.ToInt32(rd["ParteGuardadaID"]);

                int? programaGuardado = rd["ProgramaGuardadoID"] == DBNull.Value
                    ? null
                    : Convert.ToInt32(rd["ProgramaGuardadoID"]);

                int? parteSugerida = rd["ParteSugeridaID"] == DBNull.Value
                    ? null
                    : Convert.ToInt32(rd["ParteSugeridaID"]);

                int? programaSugerido = rd["ProgramaSugeridoID"] == DBNull.Value
                    ? null
                    : Convert.ToInt32(rd["ProgramaSugeridoID"]);

                lista.Add(new DistribucionFilaV13
                {
                    DistribucionID = guardado ? Convert.ToInt64(rd["DistribucionID"]) : null,
                    MaquinaID = Convert.ToInt32(rd["MaquinaID"]),
                    CentroClave = rd["MaquinaCodigo"]?.ToString()?.Trim() ?? string.Empty,
                    MaquinaCodigo = rd["MaquinaCodigo"]?.ToString()?.Trim() ?? string.Empty,
                    MaquinaNombre = rd["MaquinaNombre"]?.ToString()?.Trim() ?? string.Empty,
                    ProgramaProduccionID = programaGuardado ?? programaSugerido,
                    ParteID = parteGuardada ?? parteSugerida,
                    NumeroParte = parteGuardada.HasValue
                        ? rd["NumeroParteGuardado"]?.ToString()?.Trim() ?? string.Empty
                        : rd["NumeroParteSugerido"]?.ToString()?.Trim() ?? string.Empty,
                    ReferenciaSAP = parteGuardada.HasValue
                        ? rd["ReferenciaSAPGuardado"]?.ToString()?.Trim() ?? string.Empty
                        : rd["ReferenciaSAPSugerida"]?.ToString()?.Trim() ?? string.Empty,
                    DescripcionParte = parteGuardada.HasValue
                        ? rd["DescripcionGuardada"]?.ToString()?.Trim() ?? string.Empty
                        : rd["DescripcionSugerida"]?.ToString()?.Trim() ?? string.Empty,
                    OF = programaGuardado.HasValue
                        ? string.Empty
                        : rd["OF"]?.ToString()?.Trim() ?? string.Empty,
                    PiezaSugerida = !parteGuardada.HasValue && parteSugerida.HasValue,
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

        const string especialSql = @"
SELECT TOP(1)
    d.DistribucionID,
    d.ProgramaProduccionID,
    d.ParteID,
    d.OperadorID,
    op.NumeroControl,
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

        var tornillo = new DistribucionFilaV13
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
            ? new SqlCommand(especialSql, cn)
            : new SqlCommand(especialSql, cn, tx))
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
                tornillo.OperadorNombre = rd["OperadorNombre"]?.ToString()?.Trim() ?? string.Empty;
                tornillo.NumeroControlOperador = rd["NumeroControl"]?.ToString()?.Trim() ?? string.Empty;
            }
        }

        lista.Add(tornillo);
        return lista;
    }

    private static async Task<List<DistribucionFilaV13>> CargarFilasDistribucionBasicaV13Async(
        DateTime fecha,
        DistribucionTurnoV13 turno,
        SqlConnection cn)
    {
        var ventana = VentanaDistribucionV13(fecha,turno);
        var lista = new List<DistribucionFilaV13>();

        const string sql = @"
SELECT
    m.MaquinaID,
    m.Codigo AS MaquinaCodigo,
    m.Nombre AS MaquinaNombre,
    d.DistribucionID,
    d.ProgramaProduccionID,
    d.ParteID,
    d.OperadorID,
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
  AND UPPER(ISNULL(m.Area,N'')) COLLATE Modern_Spanish_CI_AI LIKE N'%INYE%'
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

        await using (var cmd = new SqlCommand(sql,cn))
        {
            cmd.Parameters.Add("@Fecha",SqlDbType.Date).Value=fecha.Date;
            cmd.Parameters.Add("@TurnoID",SqlDbType.Int).Value=turno.TurnoID;

            await using var rd=await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                lista.Add(new DistribucionFilaV13
                {
                    DistribucionID=rd["DistribucionID"]==DBNull.Value ? null : Convert.ToInt64(rd["DistribucionID"]),
                    MaquinaID=Convert.ToInt32(rd["MaquinaID"]),
                    CentroClave=rd["MaquinaCodigo"]?.ToString()?.Trim() ?? string.Empty,
                    MaquinaCodigo=rd["MaquinaCodigo"]?.ToString()?.Trim() ?? string.Empty,
                    MaquinaNombre=rd["MaquinaNombre"]?.ToString()?.Trim() ?? string.Empty,
                    ProgramaProduccionID=rd["ProgramaProduccionID"]==DBNull.Value ? null : Convert.ToInt32(rd["ProgramaProduccionID"]),
                    ParteID=rd["ParteID"]==DBNull.Value ? null : Convert.ToInt32(rd["ParteID"]),
                    NumeroParte=rd["NumeroParte"]?.ToString()?.Trim() ?? string.Empty,
                    ReferenciaSAP=rd["ReferenciaSAP"]?.ToString()?.Trim() ?? string.Empty,
                    DescripcionParte=rd["DescripcionParte"]?.ToString()?.Trim() ?? string.Empty,
                    OperadorID=rd["OperadorID"]==DBNull.Value ? null : Convert.ToInt32(rd["OperadorID"]),
                    OperadorNombre=rd["OperadorNombre"]?.ToString()?.Trim() ?? string.Empty,
                    NumeroControlOperador=rd["NumeroControlOperador"]?.ToString()?.Trim() ?? string.Empty,
                    Inicio=ventana.Inicio,
                    Fin=ventana.Fin
                });
            }
        }

        const string tornilloSql=@"
SELECT TOP(1)
    d.DistribucionID,
    d.ProgramaProduccionID,
    d.ParteID,
    d.OperadorID,
    op.NumeroControl,
    LTRIM(RTRIM(CONCAT(
        ISNULL(op.Nombre,N''),N' ',
        ISNULL(op.ApellidoPaterno,N''),N' ',
        ISNULL(op.ApellidoMaterno,N'')))) AS OperadorNombre
FROM dbo.Produccion_DistribucionOperadores d
LEFT JOIN dbo.Persona op ON op.PersonaID=d.OperadorID
WHERE d.Activo=1
  AND d.FechaTrabajo=@Fecha
  AND d.TurnoID=@TurnoID
  AND UPPER(ISNULL(d.CentroEspecial,N''))=N'TORNILLO'
ORDER BY d.DistribucionID DESC;";

        var tornillo=new DistribucionFilaV13
        {
            CentroClave="TORNILLO",
            MaquinaCodigo="TORNILLO",
            MaquinaNombre="TORNILLO",
            EsEspecial=true,
            NumeroParte="TORNILLO",
            DescripcionParte="Puesto especial",
            Inicio=ventana.Inicio,
            Fin=ventana.Fin
        };

        await using (var cmd=new SqlCommand(tornilloSql,cn))
        {
            cmd.Parameters.Add("@Fecha",SqlDbType.Date).Value=fecha.Date;
            cmd.Parameters.Add("@TurnoID",SqlDbType.Int).Value=turno.TurnoID;
            await using var rd=await cmd.ExecuteReaderAsync();
            if (await rd.ReadAsync())
            {
                tornillo.DistribucionID=Convert.ToInt64(rd["DistribucionID"]);
                tornillo.ProgramaProduccionID=rd["ProgramaProduccionID"]==DBNull.Value ? null : Convert.ToInt32(rd["ProgramaProduccionID"]);
                tornillo.ParteID=rd["ParteID"]==DBNull.Value ? null : Convert.ToInt32(rd["ParteID"]);
                tornillo.OperadorID=rd["OperadorID"]==DBNull.Value ? null : Convert.ToInt32(rd["OperadorID"]);
                tornillo.OperadorNombre=rd["OperadorNombre"]?.ToString()?.Trim() ?? string.Empty;
                tornillo.NumeroControlOperador=rd["NumeroControl"]?.ToString()?.Trim() ?? string.Empty;
            }
        }

        lista.Add(tornillo);
        return lista;
    }

    private static async Task<List<OperadorV13>> CargarOperadoresBasicosV13Async(SqlConnection cn)
    {
        const string sql=@"
SELECT
    p.PersonaID,
    ISNULL(p.NumeroControl,N'') AS NumeroControl,
    LTRIM(RTRIM(CONCAT(
        ISNULL(p.Nombre,N''),N' ',
        ISNULL(p.ApellidoPaterno,N''),N' ',
        ISNULL(p.ApellidoMaterno,N'')))) AS Nombre
FROM dbo.Persona p
WHERE ISNULL(p.EsColaboradorActivo,1)=1
  AND UPPER(ISNULL(p.Puesto,N'')) COLLATE Modern_Spanish_CI_AI LIKE N'%OPERADOR%'
ORDER BY Nombre,p.PersonaID;";

        var lista=new List<OperadorV13>();
        await using var cmd=new SqlCommand(sql,cn);
        await using var rd=await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            lista.Add(new OperadorV13
            {
                PersonaID=Convert.ToInt32(rd["PersonaID"]),
                NumeroControl=rd["NumeroControl"]?.ToString()?.Trim() ?? string.Empty,
                Nombre=rd["Nombre"]?.ToString()?.Trim() ?? string.Empty,
                Nivel=null
            });
        }
        return lista;
    }
    [HttpGet("DistribucionV13")]
    public async Task<IActionResult> DistribucionV13(DateTime? fecha, int? turnoId)
    {
        try
        {
            return await DistribucionV13Core(fecha,turnoId);
        }
        catch (SqlException ex)
        {
            return StatusCode(500,new
            {
                ok=false,
                sqlNumber=ex.Number,
                message=$"Error SQL al cargar la distribución: {ex.Message}"
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500,new
            {
                ok=false,
                message=$"Error al cargar la distribución: {ex.Message}"
            });
        }
    }

    private async Task<IActionResult> DistribucionV13Core(DateTime? fecha, int? turnoId)
    {
        if (!UsuarioEnSesion())
            return Unauthorized();

        var dia = (fecha ?? DateTime.Today).Date;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        if (!await TablaDistribucionV13Async(cn, null))
        {
            return Json(new
            {
                ok = false,
                configurado = false,
                message = "Ejecuta NSQ_PRODUCCION_PERSONAL_DISTRIBUCION_V13.sql."
            });
        }

        var turnos = await CargarTurnosDistribucionV13Async(cn, null);

        if (turnos.Count == 0)
            return Json(new { ok = false, configurado = true, message = "No existen turnos activos con horario." });

        DistribucionTurnoV13? seleccionado = turnoId.HasValue
            ? turnos.FirstOrDefault(x => x.TurnoID == turnoId.Value)
            : null;

        if (seleccionado == null)
        {
            if (dia == DateTime.Today)
            {
                var ahora = DateTime.Now.TimeOfDay;

                seleccionado = turnos
                    .Where(x => string.Equals(x.TipoTurno, "Regular", StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault(x => x.Cruza
                        ? ahora >= x.Inicio || ahora < x.Fin
                        : ahora >= x.Inicio && ahora < x.Fin);
            }

            seleccionado ??= turnos.FirstOrDefault(x =>
                string.Equals(x.TipoTurno, "Regular", StringComparison.OrdinalIgnoreCase));

            seleccionado ??= turnos.First();
        }

        List<DistribucionFilaV13> filas;
        try
        {
            filas = await CargarFilasDistribucionV13Async(dia, seleccionado, cn, null);
        }
        catch (SqlException ex)
        {
            System.Diagnostics.Debug.WriteLine($"V13 filas avanzadas: {ex.Message}");
            filas = await CargarFilasDistribucionBasicaV13Async(dia, seleccionado, cn);
        }

        List<OperadorV13> operadores;
        try
        {
            operadores = await CargarOperadoresDistribucionV13Async(null, cn, null);
        }
        catch (SqlException ex)
        {
            System.Diagnostics.Debug.WriteLine($"V13 catalogo operadores: {ex.Message}");
            operadores = await CargarOperadoresBasicosV13Async(cn);
        }

        return Json(new
        {
            ok = true,
            configurado = true,
            fecha = dia.ToString("yyyy-MM-dd"),
            turnoSeleccionadoId = seleccionado.TurnoID,
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
            operadores = operadores.Select(x => new
            {
                personaID = x.PersonaID,
                numeroControl = x.NumeroControl,
                nombre = x.Nombre,
                nivel = x.Nivel
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
                piezaSugerida = x.PiezaSugerida,
                operadorID = x.OperadorID,
                operadorNombre = x.OperadorNombre,
                numeroControlOperador = x.NumeroControlOperador,
                inicio = x.Inicio,
                fin = x.Fin
            })
        });
    }

    [HttpGet("DistribucionV13/Candidatos")]
    public async Task<IActionResult> CandidatosDistribucionV13(int? parteId)
    {
        if (!UsuarioEnSesion())
            return Unauthorized();

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        var operadores = await CargarOperadoresDistribucionV13Async(parteId, cn, null);

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

    [HttpGet("DistribucionV13/Programas")]
    public async Task<IActionResult> ProgramasDistribucionV13(
        DateTime fecha,
        int turnoId,
        int maquinaId)
    {
        if (!UsuarioEnSesion())
            return Unauthorized();

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        var turnos = await CargarTurnosDistribucionV13Async(cn, null);
        var turno = turnos.FirstOrDefault(x => x.TurnoID == turnoId);

        if (turno == null)
            return BadRequest(new { ok = false, message = "Turno no valido." });

        var ventana = VentanaDistribucionV13(fecha.Date, turno);

        const string sql = @"
SELECT
    pp.ProgramaProduccionID,
    pp.ParteID,
    ISNULL(pp.NumeroParte,N'') AS NumeroParte,
    ISNULL(p.ReferenciaSAP,N'') AS ReferenciaSAP,
    COALESCE(
        NULLIF(pp.DesignacionDescripcionSAP,N''),
        NULLIF(p.Designacion,N''),
        p.Descripcion,
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
LEFT JOIN dbo.ERP_Partes p
    ON p.ParteID=pp.ParteID
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
ORDER BY pp.FechaInicioProgramada,pp.ProgramaProduccionID;";

        var items = new List<object>();

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = maquinaId;
        cmd.Parameters.Add("@Inicio", SqlDbType.DateTime2).Value = ventana.Inicio;
        cmd.Parameters.Add("@Fin", SqlDbType.DateTime2).Value = ventana.Fin;

        await using var rd = await cmd.ExecuteReaderAsync();

        while (await rd.ReadAsync())
        {
            items.Add(new
            {
                programaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),
                parteID = rd["ParteID"] == DBNull.Value ? (int?)null : Convert.ToInt32(rd["ParteID"]),
                numeroParte = rd["NumeroParte"]?.ToString()?.Trim() ?? string.Empty,
                referenciaSAP = rd["ReferenciaSAP"]?.ToString()?.Trim() ?? string.Empty,
                descripcion = rd["Descripcion"]?.ToString()?.Trim() ?? string.Empty,
                of = rd["OF"]?.ToString()?.Trim() ?? string.Empty,
                inicio = Convert.ToDateTime(rd["FechaInicioProgramada"]),
                fin = Convert.ToDateTime(rd["FechaFinProgramada"])
            });
        }

        return Json(new { ok = true, programas = items });
    }

    [HttpGet("DistribucionV13/Partes")]
    public async Task<IActionResult> BuscarPartesDistribucionV13(string? q)
    {
        if (!UsuarioEnSesion())
            return Unauthorized();

        var termino = (q ?? string.Empty).Trim();

        if (termino.Length < 2)
            return Json(new { ok = true, partes = Array.Empty<object>() });

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        const string sql = @"
SELECT TOP(30)
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

        var items = new List<object>();

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@Q", SqlDbType.NVarChar, 120).Value = termino;

        await using var rd = await cmd.ExecuteReaderAsync();

        while (await rd.ReadAsync())
        {
            items.Add(new
            {
                parteID = Convert.ToInt32(rd["ParteID"]),
                numeroParte = rd["NumeroParte"]?.ToString()?.Trim() ?? string.Empty,
                referenciaSAP = rd["ReferenciaSAP"]?.ToString()?.Trim() ?? string.Empty,
                descripcion = rd["Descripcion"]?.ToString()?.Trim() ?? string.Empty
            });
        }

        return Json(new { ok = true, partes = items });
    }

    [HttpPost("DistribucionV13/Guardar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarDistribucionV13(
        DateTime fecha,
        int turnoId,
        int? maquinaId,
        string? centroEspecial,
        int? programaProduccionId,
        int? parteId,
        int? operadorId,
        string? justificacion)
    {
        if (!UsuarioEnSesion())
            return Unauthorized();

        fecha = fecha.Date;

        centroEspecial = string.IsNullOrWhiteSpace(centroEspecial)
            ? null
            : centroEspecial.Trim().ToUpperInvariant();

        if (!maquinaId.HasValue && string.IsNullOrWhiteSpace(centroEspecial))
            return BadRequest(new { ok = false, message = "Debes indicar maquina o centro especial." });

        if (maquinaId.HasValue && !string.IsNullOrWhiteSpace(centroEspecial))
            return BadRequest(new { ok = false, message = "Una fila no puede ser maquina y centro especial al mismo tiempo." });

        if (!string.IsNullOrWhiteSpace(centroEspecial) && centroEspecial != "TORNILLO")
            return BadRequest(new { ok = false, message = "Centro especial no reconocido." });

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        await using var tx =
            (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);

        try
        {
            if (!await TablaDistribucionV13Async(cn, tx))
                throw new InvalidOperationException("Ejecuta NSQ_PRODUCCION_PERSONAL_DISTRIBUCION_V13.sql.");

            var turnos = await CargarTurnosDistribucionV13Async(cn, tx);
            var turno = turnos.FirstOrDefault(x => x.TurnoID == turnoId);

            if (turno == null)
                throw new InvalidOperationException("Turno no valido.");

            var ventana = VentanaDistribucionV13(fecha, turno);

            if (maquinaId.HasValue)
            {
                const string maqSql = @"
SELECT CONVERT(bit,CASE WHEN EXISTS
(
    SELECT 1
    FROM dbo.ERP_Maquinas
    WHERE MaquinaID=@MaquinaID
      AND Activo=1
      AND UPPER(ISNULL(Area,N'')) COLLATE Modern_Spanish_CI_AI LIKE N'%INYE%'
)
THEN 1 ELSE 0 END);";

                await using var maqCmd = new SqlCommand(maqSql, cn, tx);
                maqCmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = maquinaId.Value;

                if (!Convert.ToBoolean(await maqCmd.ExecuteScalarAsync() ?? false))
                    throw new InvalidOperationException("La maquina no esta activa en Inyeccion.");
            }

            if (parteId.HasValue)
            {
                const string parteSql = @"
SELECT CONVERT(bit,CASE WHEN EXISTS
(
    SELECT 1
    FROM dbo.ERP_Partes
    WHERE ParteID=@ParteID
      AND Activo=1
)
THEN 1 ELSE 0 END);";

                await using var parteCmd = new SqlCommand(parteSql, cn, tx);
                parteCmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = parteId.Value;

                if (!Convert.ToBoolean(await parteCmd.ExecuteScalarAsync() ?? false))
                    throw new InvalidOperationException("La pieza seleccionada ya no esta activa.");
            }

            if (programaProduccionId.HasValue)
            {
                const string progSql = @"
SELECT CONVERT(bit,CASE WHEN EXISTS
(
    SELECT 1
    FROM dbo.Planeacion_ProgramaProduccion
    WHERE ProgramaProduccionID=@ProgramaID
      AND Activo=1
)
THEN 1 ELSE 0 END);";

                await using var progCmd = new SqlCommand(progSql, cn, tx);
                progCmd.Parameters.Add("@ProgramaID", SqlDbType.Int).Value = programaProduccionId.Value;

                if (!Convert.ToBoolean(await progCmd.ExecuteScalarAsync() ?? false))
                    programaProduccionId = null;
            }

            if (operadorId.HasValue)
            {
                var candidatos = await CargarOperadoresDistribucionV13Async(parteId, cn, tx);

                if (!candidatos.Any(x => x.PersonaID == operadorId.Value))
                    throw new InvalidOperationException("El operador no es candidato valido para esta distribucion.");
            }

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
  AND
  (
      (@MaquinaID IS NOT NULL AND MaquinaID=@MaquinaID)
      OR
      (@MaquinaID IS NULL AND UPPER(ISNULL(CentroEspecial,N''))=@CentroEspecial)
  )
ORDER BY DistribucionID DESC;";

            long? distribucionId = null;
            int? programaAnterior = null;
            int? parteAnterior = null;
            int? operadorAnterior = null;

            await using (var actualCmd = new SqlCommand(actualSql, cn, tx))
            {
                actualCmd.Parameters.Add("@Fecha", SqlDbType.Date).Value = fecha;
                actualCmd.Parameters.Add("@TurnoID", SqlDbType.Int).Value = turnoId;
                actualCmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value =
                    maquinaId.HasValue ? maquinaId.Value : DBNull.Value;
                actualCmd.Parameters.Add("@CentroEspecial", SqlDbType.NVarChar, 50).Value =
                    centroEspecial == null ? DBNull.Value : centroEspecial;

                await using var rd = await actualCmd.ExecuteReaderAsync();

                if (await rd.ReadAsync())
                {
                    distribucionId = Convert.ToInt64(rd["DistribucionID"]);
                    programaAnterior = rd["ProgramaProduccionID"] == DBNull.Value
                        ? null : Convert.ToInt32(rd["ProgramaProduccionID"]);
                    parteAnterior = rd["ParteID"] == DBNull.Value
                        ? null : Convert.ToInt32(rd["ParteID"]);
                    operadorAnterior = rd["OperadorID"] == DBNull.Value
                        ? null : Convert.ToInt32(rd["OperadorID"]);
                }
            }

            var cambiaOperador = distribucionId.HasValue && operadorAnterior != operadorId;
            var motivoTexto = string.IsNullOrWhiteSpace(justificacion)
                ? null : justificacion.Trim();

            if (cambiaOperador &&
                (string.IsNullOrWhiteSpace(motivoTexto) || motivoTexto.Length < 5))
            {
                throw new InvalidOperationException(
                    "Para cambiar o retirar un operador debes escribir una justificacion de al menos 5 caracteres.");
            }

            if (motivoTexto?.Length > 500)
                throw new InvalidOperationException("La justificacion no puede superar 500 caracteres.");

            if (operadorId.HasValue)
            {
                const string cruceSql = @"
SELECT TOP(1)
    d.DistribucionID,
    ISNULL(m.Codigo,d.CentroEspecial) AS Centro
FROM dbo.Produccion_DistribucionOperadores d WITH(UPDLOCK,HOLDLOCK)
LEFT JOIN dbo.ERP_Maquinas m
    ON m.MaquinaID=d.MaquinaID
WHERE d.Activo=1
  AND d.OperadorID=@OperadorID
  AND d.Inicio<@Fin
  AND d.Fin>@Inicio
  AND (@DistribucionID IS NULL OR d.DistribucionID<>@DistribucionID)
ORDER BY d.Inicio,d.DistribucionID;";

                await using var cruceCmd = new SqlCommand(cruceSql, cn, tx);
                cruceCmd.Parameters.Add("@OperadorID", SqlDbType.Int).Value = operadorId.Value;
                cruceCmd.Parameters.Add("@Inicio", SqlDbType.DateTime2).Value = ventana.Inicio;
                cruceCmd.Parameters.Add("@Fin", SqlDbType.DateTime2).Value = ventana.Fin;
                cruceCmd.Parameters.Add("@DistribucionID", SqlDbType.BigInt).Value =
                    distribucionId.HasValue ? distribucionId.Value : DBNull.Value;

                await using var cruceRd = await cruceCmd.ExecuteReaderAsync();

                if (await cruceRd.ReadAsync())
                {
                    var centro = cruceRd["Centro"]?.ToString()?.Trim() ?? "otro puesto";

                    throw new InvalidOperationException(
                        $"El operador ya esta distribuido en {centro} dentro de un horario que se traslapa.");
                }
            }

            var usuarioId = UsuarioID();

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

                await using var updateCmd = new SqlCommand(updateSql, cn, tx);
                AddSaveParams(updateCmd);
                updateCmd.Parameters.Add("@ID", SqlDbType.BigInt).Value = distribucionId.Value;
                await updateCmd.ExecuteNonQueryAsync();
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

                await using var insertCmd = new SqlCommand(insertSql, cn, tx);
                AddSaveParams(insertCmd);
                distribucionId = Convert.ToInt64(await insertCmd.ExecuteScalarAsync());
            }

            void AddSaveParams(SqlCommand cmd)
            {
                if (!cmd.Parameters.Contains("@Fecha"))
                    cmd.Parameters.Add("@Fecha", SqlDbType.Date).Value = fecha;
                if (!cmd.Parameters.Contains("@TurnoID"))
                    cmd.Parameters.Add("@TurnoID", SqlDbType.Int).Value = turnoId;
                if (!cmd.Parameters.Contains("@MaquinaID"))
                    cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value =
                        maquinaId.HasValue ? maquinaId.Value : DBNull.Value;
                if (!cmd.Parameters.Contains("@CentroEspecial"))
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
                cmd.Parameters.Add("@OrigenPieza", SqlDbType.NVarChar, 30).Value =
                    programaProduccionId.HasValue ? "PROGRAMA"
                    : parteId.HasValue ? "MANUAL"
                    : "SIN_PIEZA";
                cmd.Parameters.Add("@Usuario", SqlDbType.Int).Value = usuarioId;
            }

            var motivo =
                !operadorAnterior.HasValue && operadorId.HasValue ? "ASIGNACION_INICIAL"
                : cambiaOperador ? "CAMBIO_OPERADOR"
                : parteAnterior != parteId || programaAnterior != programaProduccionId ? "CAMBIO_PIEZA"
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

            await using (var histCmd = new SqlCommand(histSql, cn, tx))
            {
                histCmd.Parameters.Add("@DistribucionID", SqlDbType.BigInt).Value = distribucionId!.Value;
                histCmd.Parameters.Add("@Fecha", SqlDbType.Date).Value = fecha;
                histCmd.Parameters.Add("@TurnoID", SqlDbType.Int).Value = turnoId;
                histCmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value =
                    maquinaId.HasValue ? maquinaId.Value : DBNull.Value;
                histCmd.Parameters.Add("@CentroEspecial", SqlDbType.NVarChar, 50).Value =
                    centroEspecial == null ? DBNull.Value : centroEspecial;
                histCmd.Parameters.Add("@ProgramaAnteriorID", SqlDbType.Int).Value =
                    programaAnterior.HasValue ? programaAnterior.Value : DBNull.Value;
                histCmd.Parameters.Add("@ProgramaNuevoID", SqlDbType.Int).Value =
                    programaProduccionId.HasValue ? programaProduccionId.Value : DBNull.Value;
                histCmd.Parameters.Add("@ParteAnteriorID", SqlDbType.Int).Value =
                    parteAnterior.HasValue ? parteAnterior.Value : DBNull.Value;
                histCmd.Parameters.Add("@ParteNuevaID", SqlDbType.Int).Value =
                    parteId.HasValue ? parteId.Value : DBNull.Value;
                histCmd.Parameters.Add("@OperadorAnteriorID", SqlDbType.Int).Value =
                    operadorAnterior.HasValue ? operadorAnterior.Value : DBNull.Value;
                histCmd.Parameters.Add("@OperadorNuevoID", SqlDbType.Int).Value =
                    operadorId.HasValue ? operadorId.Value : DBNull.Value;
                histCmd.Parameters.Add("@Motivo", SqlDbType.NVarChar, 60).Value = motivo;
                histCmd.Parameters.Add("@Justificacion", SqlDbType.NVarChar, 500).Value =
                    string.IsNullOrWhiteSpace(motivoTexto) ? DBNull.Value : motivoTexto;
                histCmd.Parameters.Add("@Usuario", SqlDbType.Int).Value = usuarioId;
                await histCmd.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();

            return Json(new { ok = true, distribucionID = distribucionId, motivo });
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(); } catch { }

            return BadRequest(new { ok = false, message = ex.Message });
        }
    }

    [HttpGet("DistribucionV13/Pdf")]
    public async Task<IActionResult> PdfDistribucionV13(
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
            return BadRequest("El rango maximo del PDF es de 32 dias.");

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        if (!await TablaDistribucionV13Async(cn, null))
            return BadRequest("Ejecuta NSQ_PRODUCCION_PERSONAL_DISTRIBUCION_V13.sql.");

        var todosTurnos = await CargarTurnosDistribucionV13Async(cn, null);
        var ids = new HashSet<int>();

        foreach (var token in (turnos ?? string.Empty).Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(token, out var id))
                ids.Add(id);
        }

        var seleccionados = ids.Count == 0
            ? todosTurnos
            : todosTurnos.Where(x => ids.Contains(x.TurnoID)).ToList();

        if (seleccionados.Count == 0)
            return BadRequest("No se seleccionaron turnos validos.");

        var grupos = new List<(DateTime Fecha, DistribucionTurnoV13 Turno, List<DistribucionFilaV13> Filas)>();

        for (var fecha = desde; fecha <= hasta; fecha = fecha.AddDays(1))
        {
            foreach (var turno in seleccionados)
            {
                grupos.Add((
                    fecha,
                    turno,
                    await CargarFilasDistribucionV13Async(fecha, turno, cn, null)
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

        var documento = Document.Create(container =>
        {
            foreach (var grupo in grupos)
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(18);
                    page.DefaultTextStyle(x => x.FontSize(8));

                    page.Header()
                        .PaddingBottom(8)
                        .Row(row =>
                        {
                            if (logo != null)
                            {
                                row.ConstantItem(95)
                                    .Height(38)
                                    .Image(logo)
                                    .FitArea();
                            }
                            else
                            {
                                row.ConstantItem(95)
                                    .Text("NS QUELL")
                                    .Bold()
                                    .FontSize(15);
                            }

                            row.RelativeItem()
                                .AlignMiddle()
                                .Column(col =>
                                {
                                    col.Item()
                                        .Text("DISTRIBUCION DE OPERADORES")
                                        .Bold()
                                        .FontSize(15);

                                    col.Item()
                                        .Text($"{grupo.Fecha:dd/MM/yyyy} | {grupo.Turno.Nombre} | {grupo.Turno.TipoTurno}")
                                        .FontSize(9);
                                });

                            row.ConstantItem(150)
                                .AlignRight()
                                .AlignMiddle()
                                .Column(col =>
                                {
                                    col.Item()
                                        .Text("PROGRAMACION DE PERSONAL")
                                        .Bold()
                                        .FontSize(8);

                                    col.Item()
                                        .Text($"Generado {DateTime.Now:dd/MM/yyyy HH:mm}")
                                        .FontSize(7);
                                });
                        });

                    page.Content()
                        .Column(col =>
                        {
                            col.Spacing(6);

                            col.Item()
                                .Background("#EEF5FC")
                                .Border(1)
                                .BorderColor("#B8CCE4")
                                .Padding(7)
                                .Text($"Turno: {grupo.Turno.Nombre}    Horario: {grupo.Turno.Inicio:hh\\:mm} - {grupo.Turno.Fin:hh\\:mm}")
                                .Bold()
                                .FontSize(9);

                            col.Item()
                                .Table(table =>
                                {
                                    table.ColumnsDefinition(cols =>
                                    {
                                        cols.ConstantColumn(82);
                                        cols.RelativeColumn(1.35f);
                                        cols.RelativeColumn(1.55f);
                                        cols.RelativeColumn(2.2f);
                                    });

                                    table.Header(header =>
                                    {
                                        HeaderCell(header.Cell(), "MAQUINA / PUESTO");
                                        HeaderCell(header.Cell(), "PIEZA / REFERENCIA");
                                        HeaderCell(header.Cell(), "DESCRIPCION");
                                        HeaderCell(header.Cell(), "OPERADOR");
                                    });

                                    foreach (var fila in grupo.Filas)
                                    {
                                        var operador = string.IsNullOrWhiteSpace(fila.OperadorNombre)
                                            ? "SIN ASIGNAR"
                                            : string.IsNullOrWhiteSpace(fila.NumeroControlOperador)
                                                ? fila.OperadorNombre
                                                : $"{fila.NumeroControlOperador} - {fila.OperadorNombre}";

                                        var referencia = fila.EsEspecial
                                            ? "TORNILLO"
                                            : string.Join(
                                                " / ",
                                                new[] { fila.NumeroParte, fila.ReferenciaSAP }
                                                    .Where(x => !string.IsNullOrWhiteSpace(x)));

                                        if (string.IsNullOrWhiteSpace(referencia))
                                            referencia = "SIN PIEZA PROGRAMADA";

                                        BodyCell(
                                            table.Cell(),
                                            fila.EsEspecial
                                                ? "TORNILLO"
                                                : $"{fila.MaquinaCodigo}\n{fila.MaquinaNombre}",
                                            true);

                                        BodyCell(table.Cell(), referencia, true);

                                        BodyCell(
                                            table.Cell(),
                                            fila.EsEspecial
                                                ? "Puesto especial de distribucion"
                                                : string.IsNullOrWhiteSpace(fila.DescripcionParte)
                                                    ? "Sin descripcion"
                                                    : fila.DescripcionParte,
                                            false);

                                        BodyCell(table.Cell(), operador, !string.IsNullOrWhiteSpace(fila.OperadorNombre));
                                    }
                                });

                            var asignados = grupo.Filas.Count(x => x.OperadorID.HasValue);
                            var faltantes = grupo.Filas.Count - asignados;

                            col.Item()
                                .AlignRight()
                                .Text($"Asignados: {asignados}   |   Sin asignar: {faltantes}")
                                .Bold()
                                .FontSize(8);
                        });

                    page.Footer()
                        .AlignCenter()
                        .Text(text =>
                        {
                            text.Span("NS Quell | Distribucion de operadores | ");
                            text.CurrentPageNumber();
                            text.Span(" / ");
                            text.TotalPages();
                        });
                });
            }
        });

        var pdf = documento.GeneratePdf();

        var nombre = desde == hasta
            ? $"Distribucion_Operadores_{desde:yyyyMMdd}.pdf"
            : $"Distribucion_Operadores_{desde:yyyyMMdd}_{hasta:yyyyMMdd}.pdf";

        Response.Headers["Content-Disposition"] = $"inline; filename=\"{nombre}\"";

        return File(pdf, "application/pdf");

        static void HeaderCell(QuestPDF.Infrastructure.IContainer cell, string text)
        {
            cell.Background("#0D4E8B")
                .Padding(5)
                .Text(text)
                .FontColor("#FFFFFF")
                .Bold();
        }

        static void BodyCell(QuestPDF.Infrastructure.IContainer cell, string text, bool strong)
        {
            var descriptor = cell
                .BorderBottom(0.5f)
                .BorderColor("#D6E0EA")
                .PaddingVertical(5)
                .PaddingHorizontal(4)
                .Text(text);

            if (strong)
                descriptor.Bold();
        }
    }

    private void ConfigurarQuestPdfDistribucionV13()
    {
        // NSQ_PRODUCCION_PERSONAL_PDF_QUESTPDF_LICENSE_V14_2
        // Replica el criterio ya usado por PlaneacionPrograma:
        // si QuestPDF:License no esta configurado, usar Community.
        // Si existe configuracion explicita, respetarla.
        var licenciaConfigurada = _configuration["QuestPDF:License"];

        var licencia = string.IsNullOrWhiteSpace(licenciaConfigurada)
            ? "COMMUNITY"
            : licenciaConfigurada.Trim().ToUpperInvariant();

        QuestPDF.Settings.License = licencia switch
        {
            "COMMUNITY" => LicenseType.Community,
            "PROFESSIONAL" => LicenseType.Professional,
            "ENTERPRISE" => LicenseType.Enterprise,
            _ => throw new InvalidOperationException(
                "Configuracion QuestPDF:License invalida. Usa Community, Professional o Enterprise.")
        };
    }
}
