using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers;

public sealed partial class ProduccionController
{
    [HttpGet]
    public async Task<IActionResult> OperadoresPolivalenciaParte(
        int? parteId = null,
        int? maquinaId = null,
        DateTime? fechaHora = null,
        int? programaId = null)
    {
        if (!UsuarioEnSesion())
            return Unauthorized();

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        const string objetosSql = @"
SELECT CONVERT(bit,CASE WHEN
       OBJECT_ID(N'dbo.RRHH_EscalasPersonal',N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.RRHH_EscalaAsignaciones',N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.RRHH_EscalaTurnos',N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.RRHH_FuncionesPersonal',N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.vw_RRHH_PolivalenciaOperadoresParte',N'V') IS NOT NULL
   AND OBJECT_ID(N'dbo.Planeacion_ProgramaProduccion',N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.Persona',N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.ERP_Partes',N'U') IS NOT NULL
THEN 1 ELSE 0 END);";

        await using (var objetos = new SqlCommand(objetosSql, cn))
        {
            if (!Convert.ToBoolean(await objetos.ExecuteScalarAsync() ?? false))
            {
                return Json(new
                {
                    ok = true,
                    configurado = false,
                    tieneMatriz = false,
                    escalaEncontrada = false,
                    escalaPublicada = false,
                    fallbackOperadores = true,
                    operadores = Array.Empty<object>(),
                    auxiliares = Array.Empty<object>()
                });
            }
        }

        var parteSolicitadaId = parteId;
        var parteProgramaId = (int?)null;
        var numeroPartePrograma = string.Empty;
        var referenciaSapPrograma = string.Empty;
        var parteResuelta = parteId.GetValueOrDefault();
        var maquinaResuelta = maquinaId;
        var momento = fechaHora ?? DateTime.Now;

        if (programaId.HasValue && programaId.Value > 0)
        {
            const string programaSql = @"
SELECT TOP (1)
    ParteID,
    NumeroParte,
    ReferenciaSAP,
    MaquinaID,
    FechaInicioProgramada
FROM dbo.Planeacion_ProgramaProduccion
WHERE ProgramaProduccionID=@ProgramaProduccionID
  AND Activo=1;";

            await using (var programaCmd = new SqlCommand(programaSql, cn))
            {
                programaCmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaId.Value;
                await using var programaRd = await programaCmd.ExecuteReaderAsync();
                if (await programaRd.ReadAsync())
                {
                    if (programaRd["ParteID"] != DBNull.Value)
                        parteProgramaId = Convert.ToInt32(programaRd["ParteID"]);

                    numeroPartePrograma =
                        programaRd["NumeroParte"] == DBNull.Value
                            ? string.Empty
                            : programaRd["NumeroParte"]?.ToString()?.Trim() ?? string.Empty;

                    referenciaSapPrograma =
                        programaRd["ReferenciaSAP"] == DBNull.Value
                            ? string.Empty
                            : programaRd["ReferenciaSAP"]?.ToString()?.Trim() ?? string.Empty;

                    if (programaRd["MaquinaID"] != DBNull.Value)
                        maquinaResuelta = Convert.ToInt32(programaRd["MaquinaID"]);

                    if (programaRd["FechaInicioProgramada"] != DBNull.Value)
                        momento = Convert.ToDateTime(programaRd["FechaInicioProgramada"]);
                }
            }

            var parteMatriz =
                await ResolverPartePolivalenciaConsultaAsync(
                    programaId.Value,
                    parteResuelta > 0 ? parteResuelta : parteProgramaId,
                    cn);

            if (parteMatriz.HasValue)
                parteResuelta = parteMatriz.Value;
            else if (parteResuelta <= 0 && parteProgramaId.HasValue)
                parteResuelta = parteProgramaId.Value;
        }

        int? escalaId = null;
        string escalaFolio = string.Empty;
        string escalaEstado = string.Empty;

        const string escalaSql = @"
SELECT TOP (1)
    e.EscalaID,
    e.Folio,
    e.Estado
FROM dbo.RRHH_EscalasPersonal e
WHERE e.Activo=1
  AND e.Estado IN (N'Publicada',N'Borrador')
  AND CONVERT(date,@FechaHora) BETWEEN e.FechaInicio AND e.FechaFin
ORDER BY
    CASE WHEN e.Estado=N'Publicada' THEN 0 ELSE 1 END,
    ISNULL(e.FechaPublicacion,e.FechaRegistro) DESC,
    e.EscalaID DESC;";

        await using (var escalaCmd = new SqlCommand(escalaSql, cn))
        {
            escalaCmd.Parameters.Add("@FechaHora", SqlDbType.DateTime2).Value = momento;
            await using var rd = await escalaCmd.ExecuteReaderAsync();
            if (await rd.ReadAsync())
            {
                escalaId = Convert.ToInt32(rd["EscalaID"]);
                escalaFolio = rd["Folio"]?.ToString() ?? string.Empty;
                escalaEstado = rd["Estado"]?.ToString() ?? string.Empty;
            }
        }

        var escalaEncontrada = escalaId.HasValue;
        var escalaPublicada = escalaEncontrada &&
            string.Equals(escalaEstado, "Publicada", StringComparison.OrdinalIgnoreCase);

        var tieneMatriz = false;
        if (parteResuelta > 0)
        {
            const string tieneMatrizSql = @"
SELECT CONVERT(bit,CASE WHEN EXISTS
(
    SELECT 1
    FROM dbo.vw_RRHH_PolivalenciaOperadoresParte
    WHERE ParteID=@ParteID
) THEN 1 ELSE 0 END);";

            await using var matrix = new SqlCommand(tieneMatrizSql, cn);
            matrix.Parameters.Add("@ParteID", SqlDbType.Int).Value = parteResuelta;
            tieneMatriz = Convert.ToBoolean(await matrix.ExecuteScalarAsync() ?? false);
        }

        var operadores = new List<object>();
        var auxiliares = new List<object>();
        var operadoresEscalaTotal = 0;
        var operadoresEvaluadosEscalaTotal = 0;
        var auxiliaresCatalogoTotal = 0;

        /* ============================================================
           OPERADOR PRINCIPAL - REGLA V11

           Si la ParteID de la OF tiene matriz de polivalencia:
             - mostrar SOLO personas con Nivel N1-N4 para esa ParteID;
             - no incluir personas SIN NIVEL;
             - quienes estan en la Escala aplicable aparecen primero;
             - despues ordenar N4 -> N1.

           Si la ParteID NO tiene matriz:
             - conservar catalogo general de operadores activos para no
               bloquear piezas aun no incorporadas a Polivalencia.
           ============================================================ */
        if (tieneMatriz && parteResuelta > 0)
        {
            const string operadoresEvaluadosSql = @"
SELECT
    p.PersonaID AS PersonalID,
    CONVERT(INT,v.Nivel) AS Nivel,
    v.NumeroControl,
    LTRIM(RTRIM(CONCAT(ISNULL(p.Nombre,N''),N' ',ISNULL(p.ApellidoPaterno,N''),N' ',ISNULL(p.ApellidoMaterno,N'')))) AS Nombre,
    ISNULL(p.Puesto,N'') AS Puesto,
    escala.FuncionNombre,
    escala.TurnoNombre,
    escala.TurnoColor,
    escala.MaquinaCodigo,
    CONVERT(bit,CASE WHEN escala.PersonalID IS NULL THEN 0 ELSE 1 END) AS EnEscala
FROM dbo.vw_RRHH_PolivalenciaOperadoresParte v
INNER JOIN dbo.Persona p
    ON p.PersonaID=v.PersonalID
   AND ISNULL(p.EsColaboradorActivo,1)=1
OUTER APPLY
(
    SELECT TOP (1)
        a.PersonalID,
        f.Nombre AS FuncionNombre,
        et.Nombre AS TurnoNombre,
        et.Color AS TurnoColor,
        m.Codigo AS MaquinaCodigo
    FROM dbo.RRHH_EscalaAsignaciones a
    INNER JOIN dbo.RRHH_FuncionesPersonal f
        ON f.FuncionID=a.FuncionID
       AND f.Activo=1
    LEFT JOIN dbo.RRHH_EscalaTurnos et
        ON et.EscalaTurnoID=a.EscalaTurnoID
       AND et.Activo=1
    LEFT JOIN dbo.ERP_Maquinas m
        ON m.MaquinaID=a.MaquinaID
    WHERE @EscalaID IS NOT NULL
      AND a.Activo=1
      AND a.EscalaID=@EscalaID
      AND a.PersonalID=p.PersonaID
      AND UPPER(LTRIM(RTRIM(f.Nombre)))=N'OPERADOR'
      AND CONVERT(date,@FechaHora) BETWEEN a.FechaInicio AND a.FechaFin
    ORDER BY a.AsignacionID DESC
) escala
WHERE v.ParteID=@ParteID
  AND v.Nivel BETWEEN 1 AND 4
  AND
  (
      UPPER(LTRIM(RTRIM(ISNULL(v.PuestoMatriz,N''))))=N'OPERADOR'
      OR UPPER(LTRIM(RTRIM(ISNULL(p.Puesto,N''))))=N'OPERADOR'
      OR EXISTS
      (
          SELECT 1
          FROM dbo.RRHH_EscalaAsignaciones ah
          INNER JOIN dbo.RRHH_FuncionesPersonal fh
              ON fh.FuncionID=ah.FuncionID
             AND fh.Activo=1
          WHERE ah.Activo=1
            AND ah.PersonalID=p.PersonaID
            AND UPPER(LTRIM(RTRIM(fh.Nombre)))=N'OPERADOR'
      )
  )
ORDER BY
    CASE WHEN escala.PersonalID IS NULL THEN 1 ELSE 0 END,
    v.Nivel DESC,
    Nombre;";

            await using var cmd = new SqlCommand(operadoresEvaluadosSql, cn);
            cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = parteResuelta;
            cmd.Parameters.Add("@EscalaID", SqlDbType.Int).Value = escalaId.HasValue ? (object)escalaId.Value : DBNull.Value;
            cmd.Parameters.Add("@FechaHora", SqlDbType.DateTime2).Value = momento;

            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                var enEscala = Convert.ToBoolean(rd["EnEscala"]);
                if (enEscala)
                    operadoresEvaluadosEscalaTotal++;

                operadores.Add(new
                {
                    personaID = Convert.ToInt32(rd["PersonalID"]),
                    nombre = rd["Nombre"]?.ToString() ?? string.Empty,
                    nivel = Convert.ToInt32(rd["Nivel"]),
                    numeroControl = rd["NumeroControl"]?.ToString() ?? string.Empty,
                    puesto = rd["Puesto"]?.ToString() ?? string.Empty,
                    funcion = rd["FuncionNombre"]?.ToString() ?? string.Empty,
                    turnoNombre = rd["TurnoNombre"]?.ToString() ?? string.Empty,
                    turnoColor = rd["TurnoColor"]?.ToString() ?? string.Empty,
                    maquinaCodigo = rd["MaquinaCodigo"]?.ToString() ?? string.Empty,
                    enEscala
                });
            }
        }
        else
        {
            const string operadoresGeneralesSql = @"
SELECT
    p.PersonaID AS PersonalID,
    LTRIM(RTRIM(CONCAT(ISNULL(p.Nombre,N''),N' ',ISNULL(p.ApellidoPaterno,N''),N' ',ISNULL(p.ApellidoMaterno,N'')))) AS Nombre,
    ISNULL(p.Puesto,N'') AS Puesto,
    escala.FuncionNombre,
    escala.TurnoNombre,
    escala.TurnoColor,
    escala.MaquinaCodigo,
    CONVERT(bit,CASE WHEN escala.PersonalID IS NULL THEN 0 ELSE 1 END) AS EnEscala
FROM dbo.Persona p
OUTER APPLY
(
    SELECT TOP (1)
        a.PersonalID,
        f.Nombre AS FuncionNombre,
        et.Nombre AS TurnoNombre,
        et.Color AS TurnoColor,
        m.Codigo AS MaquinaCodigo
    FROM dbo.RRHH_EscalaAsignaciones a
    INNER JOIN dbo.RRHH_FuncionesPersonal f
        ON f.FuncionID=a.FuncionID
       AND f.Activo=1
    LEFT JOIN dbo.RRHH_EscalaTurnos et
        ON et.EscalaTurnoID=a.EscalaTurnoID
       AND et.Activo=1
    LEFT JOIN dbo.ERP_Maquinas m
        ON m.MaquinaID=a.MaquinaID
    WHERE @EscalaID IS NOT NULL
      AND a.Activo=1
      AND a.EscalaID=@EscalaID
      AND a.PersonalID=p.PersonaID
      AND UPPER(LTRIM(RTRIM(f.Nombre)))=N'OPERADOR'
      AND CONVERT(date,@FechaHora) BETWEEN a.FechaInicio AND a.FechaFin
    ORDER BY a.AsignacionID DESC
) escala
WHERE ISNULL(p.EsColaboradorActivo,1)=1
  AND
  (
      UPPER(LTRIM(RTRIM(ISNULL(p.Puesto,N''))))=N'OPERADOR'
      OR EXISTS
      (
          SELECT 1
          FROM dbo.RRHH_EscalaAsignaciones ah
          INNER JOIN dbo.RRHH_FuncionesPersonal fh
              ON fh.FuncionID=ah.FuncionID
             AND fh.Activo=1
          WHERE ah.Activo=1
            AND ah.PersonalID=p.PersonaID
            AND UPPER(LTRIM(RTRIM(fh.Nombre)))=N'OPERADOR'
      )
  )
ORDER BY
    CASE WHEN escala.PersonalID IS NULL THEN 1 ELSE 0 END,
    Nombre;";

            await using var cmd = new SqlCommand(operadoresGeneralesSql, cn);
            cmd.Parameters.Add("@EscalaID", SqlDbType.Int).Value = escalaId.HasValue ? (object)escalaId.Value : DBNull.Value;
            cmd.Parameters.Add("@FechaHora", SqlDbType.DateTime2).Value = momento;

            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                operadores.Add(new
                {
                    personaID = Convert.ToInt32(rd["PersonalID"]),
                    nombre = rd["Nombre"]?.ToString() ?? string.Empty,
                    nivel = (int?)null,
                    numeroControl = string.Empty,
                    puesto = rd["Puesto"]?.ToString() ?? string.Empty,
                    funcion = rd["FuncionNombre"]?.ToString() ?? string.Empty,
                    turnoNombre = rd["TurnoNombre"]?.ToString() ?? string.Empty,
                    turnoColor = rd["TurnoColor"]?.ToString() ?? string.Empty,
                    maquinaCodigo = rd["MaquinaCodigo"]?.ToString() ?? string.Empty,
                    enEscala = Convert.ToBoolean(rd["EnEscala"])
                });
            }
        }

        var fallbackOperadores = !tieneMatriz;
        /* ============================================================
           AUXILIAR
           El auxiliar no se filtra por ParteID ni por nivel.
           Se muestra el catalogo completo de personas activas con
           Puesto Auxiliar o con historial/asignacion de funcion Auxiliar.
           ============================================================ */
        const string auxiliaresSql = @"
;WITH Candidatos AS
(
    SELECT DISTINCT p.PersonaID
    FROM dbo.Persona p
    WHERE ISNULL(p.EsColaboradorActivo,1)=1
      AND
      (
          UPPER(LTRIM(RTRIM(ISNULL(p.Puesto,N'')))) LIKE N'%AUXILIAR%'
          OR EXISTS
          (
              SELECT 1
              FROM dbo.RRHH_EscalaAsignaciones a
              INNER JOIN dbo.RRHH_FuncionesPersonal f
                  ON f.FuncionID=a.FuncionID
                 AND f.Activo=1
              WHERE a.Activo=1
                AND a.PersonalID=p.PersonaID
                AND UPPER(LTRIM(RTRIM(f.Nombre))) LIKE N'%AUXILIAR%'
          )
      )
)
SELECT
    p.PersonaID AS PersonalID,
    LTRIM(RTRIM(CONCAT(ISNULL(p.Nombre,N''),N' ',ISNULL(p.ApellidoPaterno,N''),N' ',ISNULL(p.ApellidoMaterno,N'')))) AS Nombre,
    ISNULL(p.Puesto,N'') AS Puesto,
    escala.FuncionNombre,
    escala.TurnoNombre,
    escala.TurnoColor,
    escala.MaquinaCodigo,
    CONVERT(bit,CASE WHEN escala.PersonalID IS NULL THEN 0 ELSE 1 END) AS EnEscala
FROM Candidatos c
INNER JOIN dbo.Persona p
    ON p.PersonaID=c.PersonaID
OUTER APPLY
(
    SELECT TOP (1)
        a.PersonalID,
        f.Nombre AS FuncionNombre,
        et.Nombre AS TurnoNombre,
        et.Color AS TurnoColor,
        m.Codigo AS MaquinaCodigo
    FROM dbo.RRHH_EscalaAsignaciones a
    INNER JOIN dbo.RRHH_FuncionesPersonal f
        ON f.FuncionID=a.FuncionID
       AND f.Activo=1
    LEFT JOIN dbo.RRHH_EscalaTurnos et
        ON et.EscalaTurnoID=a.EscalaTurnoID
       AND et.Activo=1
    LEFT JOIN dbo.ERP_Maquinas m
        ON m.MaquinaID=a.MaquinaID
    WHERE @EscalaID IS NOT NULL
      AND a.Activo=1
      AND a.EscalaID=@EscalaID
      AND a.PersonalID=p.PersonaID
      AND
      (
          UPPER(LTRIM(RTRIM(f.Nombre))) LIKE N'%AUXILIAR%'
          OR UPPER(LTRIM(RTRIM(ISNULL(p.Puesto,N'')))) LIKE N'%AUXILIAR%'
      )
      AND CONVERT(date,@FechaHora) BETWEEN a.FechaInicio AND a.FechaFin
    ORDER BY a.AsignacionID DESC
) escala
ORDER BY
    CASE WHEN escala.PersonalID IS NULL THEN 1 ELSE 0 END,
    Nombre;";

        await using (var cmd = new SqlCommand(auxiliaresSql, cn))
        {
            cmd.Parameters.Add("@EscalaID", SqlDbType.Int).Value = escalaId.HasValue ? (object)escalaId.Value : DBNull.Value;
            cmd.Parameters.Add("@FechaHora", SqlDbType.DateTime2).Value = momento;

            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                auxiliaresCatalogoTotal++;
                auxiliares.Add(new
                {
                    personaID = Convert.ToInt32(rd["PersonalID"]),
                    nombre = rd["Nombre"]?.ToString() ?? string.Empty,
                    nivel = (int?)null,
                    numeroControl = string.Empty,
                    puesto = rd["Puesto"]?.ToString() ?? string.Empty,
                    funcion = rd["FuncionNombre"]?.ToString() ?? string.Empty,
                    turnoNombre = rd["TurnoNombre"]?.ToString() ?? string.Empty,
                    turnoColor = rd["TurnoColor"]?.ToString() ?? string.Empty,
                    maquinaCodigo = rd["MaquinaCodigo"]?.ToString() ?? string.Empty,
                    enEscala = Convert.ToBoolean(rd["EnEscala"])
                });
            }
        }

        return Json(new
        {
            ok = true,
            configurado = true,
            tieneMatriz,
            escalaEncontrada,
            escalaPublicada,
            escalaId,
            escalaFolio,
            escalaEstado,
            parteId = parteResuelta,
            parteSolicitadaId,
            parteProgramaId,
            numeroParte = numeroPartePrograma,
            referenciaSap = referenciaSapPrograma,
            maquinaId = maquinaResuelta,
            fechaHora = momento,
            operadoresEscalaTotal,
            operadoresEvaluadosEscalaTotal,
            auxiliaresCatalogoTotal,
            fallbackOperadores,
            operadores,
            auxiliares
        });
    }

    private async Task<int?> ResolverPartePolivalenciaConsultaAsync(
        int programaProduccionId,
        int? partePreferida,
        SqlConnection cn)
    {
        const string sql = @"
IF OBJECT_ID(N'dbo.vw_RRHH_PolivalenciaOperadoresParte',N'V') IS NULL
   OR OBJECT_ID(N'dbo.Planeacion_ProgramaProduccion',N'U') IS NULL
   OR OBJECT_ID(N'dbo.ERP_Partes',N'U') IS NULL
BEGIN
    SELECT CAST(NULL AS INT);
    RETURN;
END;

DECLARE @ParteDirecta INT = @PartePreferida;

IF @ParteDirecta IS NOT NULL
   AND EXISTS
   (
       SELECT 1
       FROM dbo.vw_RRHH_PolivalenciaOperadoresParte v
       WHERE v.ParteID=@ParteDirecta
   )
BEGIN
    SELECT @ParteDirecta;
    RETURN;
END;

DECLARE @PartePrograma INT;
DECLARE @NumeroParte NVARCHAR(120);
DECLARE @ReferenciaSAP NVARCHAR(120);

SELECT TOP (1)
    @PartePrograma=pp.ParteID,
    @NumeroParte=NULLIF(LTRIM(RTRIM(pp.NumeroParte)),N''),
    @ReferenciaSAP=NULLIF(LTRIM(RTRIM(pp.ReferenciaSAP)),N'')
FROM dbo.Planeacion_ProgramaProduccion pp
WHERE pp.ProgramaProduccionID=@ProgramaProduccionID
  AND pp.Activo=1;

IF @PartePrograma IS NOT NULL
   AND EXISTS
   (
       SELECT 1
       FROM dbo.vw_RRHH_PolivalenciaOperadoresParte v
       WHERE v.ParteID=@PartePrograma
   )
BEGIN
    SELECT @PartePrograma;
    RETURN;
END;

;WITH PartesMatriz AS
(
    SELECT DISTINCT
        ep.ParteID,
        UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(ep.NumeroParte,N''))),N'.',N''),N'-',N''),N' ',N''),N'_',N''),N'/',N'')) AS NumeroNorm,
        UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(ep.ReferenciaSAP,N''))),N'.',N''),N'-',N''),N' ',N''),N'_',N''),N'/',N'')) AS SapNorm
    FROM dbo.vw_RRHH_PolivalenciaOperadoresParte v
    INNER JOIN dbo.ERP_Partes ep
        ON ep.ParteID=v.ParteID
       AND ep.Activo=1
),
ProgramaNorm AS
(
    SELECT
        UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(@NumeroParte,N''),N'.',N''),N'-',N''),N' ',N''),N'_',N''),N'/',N'')) AS NumeroNorm,
        UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(@ReferenciaSAP,N''),N'.',N''),N'-',N''),N' ',N''),N'_',N''),N'/',N'')) AS SapNorm
),
Candidatos AS
(
    SELECT DISTINCT pm.ParteID
    FROM PartesMatriz pm
    CROSS JOIN ProgramaNorm pn
    WHERE
        (pn.NumeroNorm<>N'' AND pm.NumeroNorm=pn.NumeroNorm)
        OR
        (pn.SapNorm<>N'' AND pm.SapNorm=pn.SapNorm)
)
SELECT CASE WHEN COUNT(*)=1 THEN MAX(ParteID) ELSE NULL END
FROM Candidatos;";

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;
        cmd.Parameters.Add("@PartePreferida", SqlDbType.Int).Value =
            partePreferida.HasValue && partePreferida.Value > 0
                ? partePreferida.Value
                : DBNull.Value;

        var value = await cmd.ExecuteScalarAsync();
        return value == null || value == DBNull.Value
            ? null
            : Convert.ToInt32(value);
    }


    private async Task<bool> ParteTienePolivalenciaProduccionAsync(
        int parteId,
        SqlConnection cn,
        SqlTransaction tx)
    {
        if (parteId <= 0)
            return false;

        const string sql = @"
IF OBJECT_ID(N'dbo.vw_RRHH_PolivalenciaOperadoresParte',N'V') IS NULL
    SELECT CONVERT(bit,0);
ELSE
    SELECT CONVERT(bit,CASE WHEN EXISTS
    (
        SELECT 1
        FROM dbo.vw_RRHH_PolivalenciaOperadoresParte
        WHERE ParteID=@ParteID
    ) THEN 1 ELSE 0 END);";

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = parteId;
        return Convert.ToBoolean(await cmd.ExecuteScalarAsync() ?? false);
    }

    private async Task<int?> ObtenerNivelPolivalenciaProduccionAsync(
        int parteId,
        int personaId,
        SqlConnection cn,
        SqlTransaction tx)
    {
        const string sql = @"
IF OBJECT_ID(N'dbo.vw_RRHH_PolivalenciaOperadoresParte',N'V') IS NULL
    SELECT CAST(NULL AS INT);
ELSE
    SELECT TOP (1) CONVERT(INT,Nivel)
    FROM dbo.vw_RRHH_PolivalenciaOperadoresParte
    WHERE ParteID=@ParteID
      AND PersonalID=@PersonalID
    ORDER BY Nivel DESC;";

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = parteId;
        cmd.Parameters.Add("@PersonalID", SqlDbType.Int).Value = personaId;
        var value = await cmd.ExecuteScalarAsync();
        return value == null || value == DBNull.Value ? null : Convert.ToInt32(value);
    }

    private async Task<int?> ResolverPartePolivalenciaProgramaAsync(
        int programaProduccionId,
        int? partePreferida,
        SqlConnection cn,
        SqlTransaction tx)
    {
        const string sql = @"
IF OBJECT_ID(N'dbo.vw_RRHH_PolivalenciaOperadoresParte',N'V') IS NULL
   OR OBJECT_ID(N'dbo.Planeacion_ProgramaProduccion',N'U') IS NULL
   OR OBJECT_ID(N'dbo.ERP_Partes',N'U') IS NULL
BEGIN
    SELECT CAST(NULL AS INT);
    RETURN;
END;

DECLARE @ParteDirecta INT = @PartePreferida;

IF @ParteDirecta IS NOT NULL
   AND EXISTS
   (
       SELECT 1
       FROM dbo.vw_RRHH_PolivalenciaOperadoresParte v
       WHERE v.ParteID=@ParteDirecta
   )
BEGIN
    SELECT @ParteDirecta;
    RETURN;
END;

DECLARE @PartePrograma INT;
DECLARE @NumeroParte NVARCHAR(120);
DECLARE @ReferenciaSAP NVARCHAR(120);

SELECT TOP (1)
    @PartePrograma=pp.ParteID,
    @NumeroParte=NULLIF(LTRIM(RTRIM(pp.NumeroParte)),N''),
    @ReferenciaSAP=NULLIF(LTRIM(RTRIM(pp.ReferenciaSAP)),N'')
FROM dbo.Planeacion_ProgramaProduccion pp
WHERE pp.ProgramaProduccionID=@ProgramaProduccionID
  AND pp.Activo=1;

IF @PartePrograma IS NOT NULL
   AND EXISTS
   (
       SELECT 1
       FROM dbo.vw_RRHH_PolivalenciaOperadoresParte v
       WHERE v.ParteID=@PartePrograma
   )
BEGIN
    SELECT @PartePrograma;
    RETURN;
END;

;WITH PartesMatriz AS
(
    SELECT DISTINCT
        ep.ParteID,
        UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(ep.NumeroParte,N''))),N'.',N''),N'-',N''),N' ',N''),N'_',N''),N'/',N'')) AS NumeroNorm,
        UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(ep.ReferenciaSAP,N''))),N'.',N''),N'-',N''),N' ',N''),N'_',N''),N'/',N'')) AS SapNorm
    FROM dbo.vw_RRHH_PolivalenciaOperadoresParte v
    INNER JOIN dbo.ERP_Partes ep
        ON ep.ParteID=v.ParteID
       AND ep.Activo=1
),
ProgramaNorm AS
(
    SELECT
        UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(@NumeroParte,N''),N'.',N''),N'-',N''),N' ',N''),N'_',N''),N'/',N'')) AS NumeroNorm,
        UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(@ReferenciaSAP,N''),N'.',N''),N'-',N''),N' ',N''),N'_',N''),N'/',N'')) AS SapNorm
),
Candidatos AS
(
    SELECT DISTINCT pm.ParteID
    FROM PartesMatriz pm
    CROSS JOIN ProgramaNorm pn
    WHERE
        (pn.NumeroNorm<>N'' AND pm.NumeroNorm=pn.NumeroNorm)
        OR
        (pn.SapNorm<>N'' AND pm.SapNorm=pn.SapNorm)
)
SELECT CASE WHEN COUNT(*)=1 THEN MAX(ParteID) ELSE NULL END
FROM Candidatos;";

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;
        cmd.Parameters.Add("@PartePreferida", SqlDbType.Int).Value =
            partePreferida.HasValue && partePreferida.Value > 0
                ? partePreferida.Value
                : DBNull.Value;

        var value = await cmd.ExecuteScalarAsync();
        return value == null || value == DBNull.Value
            ? null
            : Convert.ToInt32(value);
    }


    private async Task<bool> PersonaAsignadaEscalaProduccionAsync(
        int personaId,
        int? maquinaId,
        DateTime momento,
        bool esAuxiliar,
        SqlConnection cn,
        SqlTransaction tx)
    {
        const string sql = @"
IF OBJECT_ID(N'dbo.RRHH_EscalasPersonal',N'U') IS NULL
   OR OBJECT_ID(N'dbo.RRHH_EscalaAsignaciones',N'U') IS NULL
   OR OBJECT_ID(N'dbo.RRHH_FuncionesPersonal',N'U') IS NULL
BEGIN
    SELECT CONVERT(bit,0);
    RETURN;
END;

;WITH EscalaAplicable AS
(
    SELECT TOP (1) e.EscalaID
    FROM dbo.RRHH_EscalasPersonal e
    WHERE e.Activo=1
      AND e.Estado IN (N'Publicada',N'Borrador')
      AND CONVERT(date,@FechaHora) BETWEEN e.FechaInicio AND e.FechaFin
    ORDER BY
        CASE WHEN e.Estado=N'Publicada' THEN 0 ELSE 1 END,
        ISNULL(e.FechaPublicacion,e.FechaRegistro) DESC,
        e.EscalaID DESC
)
SELECT CONVERT(bit,CASE WHEN EXISTS
(
    SELECT 1
    FROM dbo.RRHH_EscalaAsignaciones a
    INNER JOIN EscalaAplicable ea
        ON ea.EscalaID=a.EscalaID
    INNER JOIN dbo.RRHH_FuncionesPersonal f
        ON f.FuncionID=a.FuncionID
       AND f.Activo=1
    INNER JOIN dbo.Persona p
        ON p.PersonaID=a.PersonalID
       AND ISNULL(p.EsColaboradorActivo,1)=1
    WHERE a.Activo=1
      AND a.PersonalID=@PersonalID
      AND CONVERT(date,@FechaHora) BETWEEN a.FechaInicio AND a.FechaFin
      AND
      (
          (@EsAuxiliar=0 AND UPPER(LTRIM(RTRIM(f.Nombre)))=N'OPERADOR')
          OR
          (@EsAuxiliar=1 AND
              (
                  UPPER(LTRIM(RTRIM(f.Nombre))) LIKE N'%AUXILIAR%'
                  OR UPPER(LTRIM(RTRIM(ISNULL(p.Puesto,N'')))) LIKE N'%AUXILIAR%'
              ))
      )
) THEN 1 ELSE 0 END);";

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@PersonalID", SqlDbType.Int).Value = personaId;
        cmd.Parameters.Add("@FechaHora", SqlDbType.DateTime2).Value = momento;
        cmd.Parameters.Add("@EsAuxiliar", SqlDbType.Bit).Value = esAuxiliar;
        return Convert.ToBoolean(await cmd.ExecuteScalarAsync() ?? false);
    }


    private async Task<bool> ExisteOperadorEscalaEvaluadoProduccionAsync(
        int parteId,
        DateTime momento,
        SqlConnection cn,
        SqlTransaction tx)
    {
        if (parteId <= 0)
            return false;

        const string sql = @"
IF OBJECT_ID(N'dbo.RRHH_EscalasPersonal',N'U') IS NULL
   OR OBJECT_ID(N'dbo.RRHH_EscalaAsignaciones',N'U') IS NULL
   OR OBJECT_ID(N'dbo.RRHH_FuncionesPersonal',N'U') IS NULL
   OR OBJECT_ID(N'dbo.vw_RRHH_PolivalenciaOperadoresParte',N'V') IS NULL
BEGIN
    SELECT CONVERT(bit,0);
    RETURN;
END;

;WITH EscalaAplicable AS
(
    SELECT TOP (1) e.EscalaID
    FROM dbo.RRHH_EscalasPersonal e
    WHERE e.Activo=1
      AND e.Estado IN (N'Publicada',N'Borrador')
      AND CONVERT(date,@FechaHora) BETWEEN e.FechaInicio AND e.FechaFin
    ORDER BY
        CASE WHEN e.Estado=N'Publicada' THEN 0 ELSE 1 END,
        ISNULL(e.FechaPublicacion,e.FechaRegistro) DESC,
        e.EscalaID DESC
)
SELECT CONVERT(bit,CASE WHEN EXISTS
(
    SELECT 1
    FROM dbo.RRHH_EscalaAsignaciones a
    INNER JOIN EscalaAplicable ea
        ON ea.EscalaID=a.EscalaID
    INNER JOIN dbo.RRHH_FuncionesPersonal f
        ON f.FuncionID=a.FuncionID
       AND f.Activo=1
    INNER JOIN dbo.vw_RRHH_PolivalenciaOperadoresParte v
        ON v.PersonalID=a.PersonalID
       AND v.ParteID=@ParteID
    WHERE a.Activo=1
      AND UPPER(LTRIM(RTRIM(f.Nombre)))=N'OPERADOR'
      AND CONVERT(date,@FechaHora) BETWEEN a.FechaInicio AND a.FechaFin
) THEN 1 ELSE 0 END);";

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = parteId;
        cmd.Parameters.Add("@FechaHora", SqlDbType.DateTime2).Value = momento;
        return Convert.ToBoolean(await cmd.ExecuteScalarAsync() ?? false);
    }

    private async Task<bool> PersonaEsOperadorActivoProduccionAsync(
        int personaId,
        SqlConnection cn,
        SqlTransaction tx)
    {
        const string sql = @"
SELECT CONVERT(bit,CASE WHEN EXISTS
(
    SELECT 1
    FROM dbo.Persona p
    WHERE p.PersonaID=@PersonalID
      AND ISNULL(p.EsColaboradorActivo,1)=1
      AND
      (
          UPPER(LTRIM(RTRIM(ISNULL(p.Puesto,N''))))=N'OPERADOR'
          OR EXISTS
          (
              SELECT 1
              FROM dbo.RRHH_EscalaAsignaciones a
              INNER JOIN dbo.RRHH_FuncionesPersonal f
                  ON f.FuncionID=a.FuncionID
                 AND f.Activo=1
              WHERE a.Activo=1
                AND a.PersonalID=p.PersonaID
                AND UPPER(LTRIM(RTRIM(f.Nombre)))=N'OPERADOR'
          )
      )
) THEN 1 ELSE 0 END);";

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@PersonalID", SqlDbType.Int).Value = personaId;
        return Convert.ToBoolean(await cmd.ExecuteScalarAsync() ?? false);
    }

    private async Task<bool> PersonaEsAuxiliarActivoProduccionAsync(
        int personaId,
        SqlConnection cn,
        SqlTransaction tx)
    {
        const string sql = @"
SELECT CONVERT(bit,CASE WHEN EXISTS
(
    SELECT 1
    FROM dbo.Persona p
    WHERE p.PersonaID=@PersonalID
      AND ISNULL(p.EsColaboradorActivo,1)=1
      AND
      (
          UPPER(LTRIM(RTRIM(ISNULL(p.Puesto,N'')))) LIKE N'%AUXILIAR%'
          OR EXISTS
          (
              SELECT 1
              FROM dbo.RRHH_EscalaAsignaciones a
              INNER JOIN dbo.RRHH_FuncionesPersonal f
                  ON f.FuncionID=a.FuncionID
                 AND f.Activo=1
              WHERE a.Activo=1
                AND a.PersonalID=p.PersonaID
                AND UPPER(LTRIM(RTRIM(f.Nombre))) LIKE N'%AUXILIAR%'
          )
      )
) THEN 1 ELSE 0 END);";

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@PersonalID", SqlDbType.Int).Value = personaId;
        return Convert.ToBoolean(await cmd.ExecuteScalarAsync() ?? false);
    }

}
