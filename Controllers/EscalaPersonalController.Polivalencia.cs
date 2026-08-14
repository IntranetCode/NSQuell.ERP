using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers;

public partial class EscalaPersonalController
{
    [HttpGet]
    public async Task<IActionResult> Polivalencia(int? parteId = null, string? q = null)
    {
        if (User.Identity?.IsAuthenticated != true)
            return RedirectToAction("Login", "Login");

        var vm = new PolivalenciaIndexVm
        {
            ParteID = parteId,
            Busqueda = q?.Trim()
        };

        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();

        vm.Configurado = await PolivalenciaConfiguradaAsync(cn);
        if (!vm.Configurado)
            return View(vm);

        const string resumenSql = @"
SELECT TOP (1)
    FuenteDocumento,VersionDocumento,Mes,Anio
FROM dbo.RRHH_PolivalenciaCompetencias
WHERE Activo=1
ORDER BY FechaVigencia DESC,PolivalenciaID DESC;

SELECT COUNT(DISTINCT PersonalID)
FROM dbo.RRHH_PolivalenciaCompetencias
WHERE Activo=1;

SELECT COUNT(DISTINCT ParteID)
FROM dbo.RRHH_PolivalenciaCompetencias
WHERE Activo=1 AND ParteID IS NOT NULL;

SELECT COUNT(DISTINCT ClaveMatriz)
FROM dbo.RRHH_PolivalenciaCompetencias
WHERE Activo=1 AND ParteID IS NULL;";

        await using (var cmd = new SqlCommand(resumenSql, cn))
        await using (var rd = await cmd.ExecuteReaderAsync())
        {
            if (await rd.ReadAsync())
            {
                vm.FuenteDocumento = rd["FuenteDocumento"]?.ToString() ?? string.Empty;
                vm.VersionDocumento = rd["VersionDocumento"]?.ToString() ?? string.Empty;
                vm.Periodo = $"{rd["Mes"]} {rd["Anio"]}";
            }

            if (await rd.NextResultAsync() && await rd.ReadAsync())
                vm.TotalOperadores = Convert.ToInt32(rd[0]);

            if (await rd.NextResultAsync() && await rd.ReadAsync())
                vm.TotalPartesMapeadas = Convert.ToInt32(rd[0]);

            if (await rd.NextResultAsync() && await rd.ReadAsync())
                vm.TotalPartesSinMapeo = Convert.ToInt32(rd[0]);
        }

        // Las opciones salen de ERP_Partes por ParteID. No se usa texto suelto de Excel.
        const string partesSql = @"
SELECT DISTINCT
    p.ParteID,
    p.NumeroParte,
    ISNULL(p.Descripcion,N'') AS DescripcionParte
FROM dbo.vw_RRHH_PolivalenciaOperadoresParte v
INNER JOIN dbo.ERP_Partes p
    ON p.ParteID=v.ParteID
   AND p.Activo=1
ORDER BY p.NumeroParte,ISNULL(p.Descripcion,N'');";

        await using (var cmd = new SqlCommand(partesSql, cn))
        await using (var rd = await cmd.ExecuteReaderAsync())
        {
            while (await rd.ReadAsync())
            {
                vm.Partes.Add(new PolivalenciaParteOpcionVm
                {
                    ParteID = Convert.ToInt32(rd["ParteID"]),
                    NumeroParte = rd["NumeroParte"]?.ToString() ?? string.Empty,
                    Descripcion = rd["DescripcionParte"]?.ToString() ?? string.Empty
                });
            }
        }

        // Se cuentan ParteID distintos por nivel. Así N1+N2+N3+N4 nunca puede
        // exceder PartesEvaluadas por duplicados históricos de la fuente.
        const string operadoresSql = @"
SELECT
    v.PersonalID,
    MAX(ISNULL(v.NumeroControl,N'')) AS NumeroControl,
    LTRIM(RTRIM(CONCAT(
        ISNULL(p.Nombre,N''),N' ',
        ISNULL(p.ApellidoPaterno,N''),N' ',
        ISNULL(p.ApellidoMaterno,N'')))) AS Nombre,
    ISNULL(p.Puesto,N'') AS Puesto,
    MAX(v.Nivel) AS NivelMaximo,
    COUNT(DISTINCT v.ParteID) AS PartesEvaluadas,
    COUNT(DISTINCT CASE WHEN v.Nivel=4 THEN v.ParteID END) AS Nivel4,
    COUNT(DISTINCT CASE WHEN v.Nivel=3 THEN v.ParteID END) AS Nivel3,
    COUNT(DISTINCT CASE WHEN v.Nivel=2 THEN v.ParteID END) AS Nivel2,
    COUNT(DISTINCT CASE WHEN v.Nivel=1 THEN v.ParteID END) AS Nivel1
FROM dbo.vw_RRHH_PolivalenciaOperadoresParte v
INNER JOIN dbo.Persona p
    ON p.PersonaID=v.PersonalID
   AND ISNULL(p.EsColaboradorActivo,1)=1
WHERE @Q IS NULL
   OR CONCAT(
        p.Nombre,N' ',p.ApellidoPaterno,N' ',p.ApellidoMaterno,N' ',
        v.NumeroControl,N' ',v.NumeroParte,N' ',v.ReferenciaSAP,N' ',v.DescripcionParte)
      LIKE N'%' + @Q + N'%'
GROUP BY
    v.PersonalID,
    p.Nombre,
    p.ApellidoPaterno,
    p.ApellidoMaterno,
    p.Puesto
ORDER BY MAX(v.Nivel) DESC,Nombre;";

        await using (var cmd = new SqlCommand(operadoresSql, cn))
        {
            cmd.Parameters.Add("@Q", SqlDbType.NVarChar, 200).Value =
                string.IsNullOrWhiteSpace(vm.Busqueda)
                    ? DBNull.Value
                    : vm.Busqueda;

            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                vm.Operadores.Add(new PolivalenciaOperadorResumenVm
                {
                    PersonalID = Convert.ToInt32(rd["PersonalID"]),
                    NumeroControl = rd["NumeroControl"]?.ToString() ?? string.Empty,
                    Nombre = rd["Nombre"]?.ToString() ?? string.Empty,
                    Puesto = rd["Puesto"]?.ToString() ?? string.Empty,
                    NivelMaximo = Convert.ToInt32(rd["NivelMaximo"]),
                    PartesEvaluadas = Convert.ToInt32(rd["PartesEvaluadas"]),
                    Nivel4 = Convert.ToInt32(rd["Nivel4"]),
                    Nivel3 = Convert.ToInt32(rd["Nivel3"]),
                    Nivel2 = Convert.ToInt32(rd["Nivel2"]),
                    Nivel1 = Convert.ToInt32(rd["Nivel1"])
                });
            }
        }

        if (parteId.HasValue && parteId.Value > 0)
        {
            const string competenciasSql = @"
SELECT
    v.PersonalID,
    pte.ParteID,
    v.NumeroControl,
    v.Nivel,
    pte.NumeroParte,
    ISNULL(pte.ReferenciaSAP,N'') AS ReferenciaSAP,
    ISNULL(pte.Designacion,N'') AS Designacion,
    ISNULL(pte.Descripcion,N'') AS DescripcionParte,
    LTRIM(RTRIM(CONCAT(
        ISNULL(p.Nombre,N''),N' ',
        ISNULL(p.ApellidoPaterno,N''),N' ',
        ISNULL(p.ApellidoMaterno,N'')))) AS Nombre,
    ISNULL(p.Puesto,N'') AS Puesto,
    CASE WHEN EXISTS
    (
        SELECT 1
        FROM dbo.RRHH_EscalaAsignaciones a
        INNER JOIN dbo.RRHH_EscalasPersonal e
            ON e.EscalaID=a.EscalaID
           AND e.Activo=1
           AND e.Estado=N'Publicada'
        WHERE a.Activo=1
          AND a.PersonalID=v.PersonalID
          AND CONVERT(date,GETDATE()) BETWEEN a.FechaInicio AND a.FechaFin
    ) THEN 1 ELSE 0 END AS EnEscalaActual
FROM dbo.vw_RRHH_PolivalenciaOperadoresParte v
INNER JOIN dbo.ERP_Partes pte
    ON pte.ParteID=v.ParteID
   AND pte.Activo=1
INNER JOIN dbo.Persona p
    ON p.PersonaID=v.PersonalID
   AND ISNULL(p.EsColaboradorActivo,1)=1
WHERE pte.ParteID=@ParteID
  AND
  (
      @Q IS NULL
      OR CONCAT(
          p.Nombre,N' ',p.ApellidoPaterno,N' ',p.ApellidoMaterno,N' ',
          v.NumeroControl,N' ',pte.NumeroParte,N' ',pte.ReferenciaSAP,N' ',
          pte.Designacion,N' ',pte.Descripcion)
         LIKE N'%' + @Q + N'%'
  )
ORDER BY EnEscalaActual DESC,v.Nivel DESC,Nombre;";

            await using var cmd = new SqlCommand(competenciasSql, cn);
            cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = parteId.Value;
            cmd.Parameters.Add("@Q", SqlDbType.NVarChar, 200).Value =
                string.IsNullOrWhiteSpace(vm.Busqueda)
                    ? DBNull.Value
                    : vm.Busqueda;

            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                vm.Competencias.Add(new PolivalenciaCompetenciaVm
                {
                    PersonalID = Convert.ToInt32(rd["PersonalID"]),
                    ParteID = Convert.ToInt32(rd["ParteID"]),
                    NumeroControl = rd["NumeroControl"]?.ToString() ?? string.Empty,
                    Nombre = rd["Nombre"]?.ToString() ?? string.Empty,
                    Puesto = rd["Puesto"]?.ToString() ?? string.Empty,
                    NumeroParte = rd["NumeroParte"]?.ToString() ?? string.Empty,
                    ReferenciaSAP = rd["ReferenciaSAP"]?.ToString() ?? string.Empty,
                    Designacion = rd["Designacion"]?.ToString() ?? string.Empty,
                    DescripcionParte = rd["DescripcionParte"]?.ToString() ?? string.Empty,
                    Nivel = Convert.ToInt32(rd["Nivel"]),
                    EnEscalaActual = Convert.ToBoolean(rd["EnEscalaActual"])
                });
            }
        }

        const string sinMapeoSql = @"
SELECT DISTINCT ClaveMatriz,EncabezadoMatriz
FROM dbo.RRHH_PolivalenciaCompetencias
WHERE Activo=1 AND ParteID IS NULL
ORDER BY ClaveMatriz;";

        await using (var cmd = new SqlCommand(sinMapeoSql, cn))
        await using (var rd = await cmd.ExecuteReaderAsync())
        {
            while (await rd.ReadAsync())
            {
                vm.SinMapeo.Add(new PolivalenciaSinMapeoVm
                {
                    ClaveMatriz = rd["ClaveMatriz"]?.ToString() ?? string.Empty,
                    EncabezadoMatriz = rd["EncabezadoMatriz"]?.ToString() ?? string.Empty
                });
            }
        }

        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> PolivalenciaDetalle(
        int personalId,
        int? nivel = null,
        string? q = null)
    {
        if (User.Identity?.IsAuthenticated != true)
            return RedirectToAction("Login", "Login");

        if (personalId <= 0)
            return NotFound();

        var vm = new PolivalenciaOperadorDetalleVm
        {
            PersonalID = personalId,
            NivelFiltro = nivel is >= 1 and <= 4 ? nivel : null,
            Busqueda = q?.Trim()
        };

        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();

        vm.Configurado = await PolivalenciaConfiguradaAsync(cn);
        if (!vm.Configurado)
            return View(vm);

        const string personaSql = @"
SELECT TOP (1)
    p.PersonaID,
    ISNULL(v.NumeroControl,N'') AS NumeroControl,
    LTRIM(RTRIM(CONCAT(
        ISNULL(p.Nombre,N''),N' ',
        ISNULL(p.ApellidoPaterno,N''),N' ',
        ISNULL(p.ApellidoMaterno,N'')))) AS Nombre,
    ISNULL(p.Puesto,N'') AS Puesto
FROM dbo.Persona p
INNER JOIN dbo.vw_RRHH_PolivalenciaOperadoresParte v
    ON v.PersonalID=p.PersonaID
WHERE p.PersonaID=@PersonalID
  AND ISNULL(p.EsColaboradorActivo,1)=1;";

        await using (var cmd = new SqlCommand(personaSql, cn))
        {
            cmd.Parameters.Add("@PersonalID", SqlDbType.Int).Value = personalId;
            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync())
                return NotFound();

            vm.NumeroControl = rd["NumeroControl"]?.ToString() ?? string.Empty;
            vm.Nombre = rd["Nombre"]?.ToString() ?? string.Empty;
            vm.Puesto = rd["Puesto"]?.ToString() ?? string.Empty;
        }

        // Estado actual en Escala, si existe. Es informativo; la competencia sigue
        // ligada por PersonalID + ParteID independientemente de la semana.
        const string escalaActualSql = @"
SELECT TOP (1)
    e.Folio,
    ISNULL(f.Nombre,N'') AS FuncionNombre,
    LTRIM(RTRIM(CONCAT(ISNULL(m.Codigo,N''),
        CASE WHEN NULLIF(m.Nombre,N'') IS NULL THEN N'' ELSE N' - ' + m.Nombre END))) AS MaquinaNombre,
    ISNULL(t.Nombre,N'') AS TurnoNombre
FROM dbo.RRHH_EscalaAsignaciones a
INNER JOIN dbo.RRHH_EscalasPersonal e
    ON e.EscalaID=a.EscalaID
   AND e.Activo=1
   AND e.Estado=N'Publicada'
LEFT JOIN dbo.RRHH_FuncionesPersonal f
    ON f.FuncionID=a.FuncionID
LEFT JOIN dbo.ERP_Maquinas m
    ON m.MaquinaID=a.MaquinaID
LEFT JOIN dbo.RRHH_EscalaTurnos t
    ON t.EscalaTurnoID=a.EscalaTurnoID
WHERE a.Activo=1
  AND a.PersonalID=@PersonalID
  AND CONVERT(date,GETDATE()) BETWEEN a.FechaInicio AND a.FechaFin
ORDER BY a.AsignacionID DESC;";

        await using (var cmd = new SqlCommand(escalaActualSql, cn))
        {
            cmd.Parameters.Add("@PersonalID", SqlDbType.Int).Value = personalId;
            await using var rd = await cmd.ExecuteReaderAsync();
            if (await rd.ReadAsync())
            {
                vm.EnEscalaActual = true;
                vm.EscalaFolio = rd["Folio"]?.ToString() ?? string.Empty;
                vm.FuncionActual = rd["FuncionNombre"]?.ToString() ?? string.Empty;
                vm.MaquinaActual = rd["MaquinaNombre"]?.ToString() ?? string.Empty;
                vm.TurnoActual = rd["TurnoNombre"]?.ToString() ?? string.Empty;
            }
        }

        // La ParteID viene de la matriz, pero todos los datos identificativos se leen
        // otra vez de ERP_Partes/ERP_Clientes/ERP_ParteDatosTecnicos. De esta forma
        // Produccion y Polivalencia muestran exactamente la misma pieza maestra.
        const string partesDetalleSql = @"
SELECT
    p.ParteID,
    p.NumeroParte,
    ISNULL(p.ReferenciaSAP,N'') AS ReferenciaSAP,
    ISNULL(p.Designacion,N'') AS Designacion,
    ISNULL(p.Descripcion,N'') AS Descripcion,
    ISNULL(cli.Codigo,N'') AS ClienteCodigo,
    ISNULL(cli.Nombre,N'') AS ClienteNombre,
    v.Nivel,
    ISNULL(dt.TipoProceso,N'') AS TipoProceso,
    LTRIM(RTRIM(CONCAT(ISNULL(mp.Codigo,N''),
        CASE WHEN NULLIF(mp.Nombre,N'') IS NULL THEN N'' ELSE N' - ' + mp.Nombre END))) AS MaquinaPrincipal,
    LTRIM(RTRIM(CONCAT(ISNULL(ms.Codigo,N''),
        CASE WHEN NULLIF(ms.Nombre,N'') IS NULL THEN N'' ELSE N' - ' + ms.Nombre END))) AS MaquinaSustituta,
    ISNULL(dt.Ciclo,N'') AS Ciclo,
    dt.ObjetivoHora,
    ISNULL(dt.MaterialCodigo,N'') AS MaterialCodigo,
    ISNULL(dt.MaterialDescripcion,N'') AS MaterialDescripcion
FROM dbo.vw_RRHH_PolivalenciaOperadoresParte v
INNER JOIN dbo.ERP_Partes p
    ON p.ParteID=v.ParteID
   AND p.Activo=1
LEFT JOIN dbo.ERP_Clientes cli
    ON cli.ClienteID=p.ClienteID
OUTER APPLY
(
    SELECT TOP (1)
        d.TipoProceso,
        d.MaquinaPrincipalID,
        d.MaquinaSustitutaID,
        d.Ciclo,
        d.ObjetivoHora,
        d.MaterialCodigo,
        d.MaterialDescripcion
    FROM dbo.ERP_ParteDatosTecnicos d
    WHERE d.ParteID=p.ParteID
      AND d.Activo=1
    ORDER BY ISNULL(d.FechaModificacion,d.FechaCreacion) DESC,d.ParteDatoTecnicoID DESC
) dt
LEFT JOIN dbo.ERP_Maquinas mp
    ON mp.MaquinaID=dt.MaquinaPrincipalID
LEFT JOIN dbo.ERP_Maquinas ms
    ON ms.MaquinaID=dt.MaquinaSustitutaID
WHERE v.PersonalID=@PersonalID
  AND (@Nivel IS NULL OR v.Nivel=@Nivel)
  AND
  (
      @Q IS NULL
      OR CONCAT(
          p.NumeroParte,N' ',p.ReferenciaSAP,N' ',p.Designacion,N' ',p.Descripcion,N' ',
          cli.Codigo,N' ',cli.Nombre,N' ',dt.TipoProceso,N' ',mp.Codigo,N' ',mp.Nombre)
          LIKE N'%' + @Q + N'%'
  )
ORDER BY v.Nivel DESC,p.NumeroParte,p.ParteID;";

        await using (var cmd = new SqlCommand(partesDetalleSql, cn))
        {
            cmd.Parameters.Add("@PersonalID", SqlDbType.Int).Value = personalId;
            cmd.Parameters.Add("@Nivel", SqlDbType.Int).Value =
                vm.NivelFiltro.HasValue ? vm.NivelFiltro.Value : DBNull.Value;
            cmd.Parameters.Add("@Q", SqlDbType.NVarChar, 200).Value =
                string.IsNullOrWhiteSpace(vm.Busqueda)
                    ? DBNull.Value
                    : vm.Busqueda;

            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                vm.Partes.Add(new PolivalenciaParteDetalleVm
                {
                    ParteID = Convert.ToInt32(rd["ParteID"]),
                    NumeroParte = rd["NumeroParte"]?.ToString() ?? string.Empty,
                    ReferenciaSAP = rd["ReferenciaSAP"]?.ToString() ?? string.Empty,
                    Designacion = rd["Designacion"]?.ToString() ?? string.Empty,
                    Descripcion = rd["Descripcion"]?.ToString() ?? string.Empty,
                    ClienteCodigo = rd["ClienteCodigo"]?.ToString() ?? string.Empty,
                    ClienteNombre = rd["ClienteNombre"]?.ToString() ?? string.Empty,
                    Nivel = Convert.ToInt32(rd["Nivel"]),
                    TipoProceso = rd["TipoProceso"]?.ToString() ?? string.Empty,
                    MaquinaPrincipal = rd["MaquinaPrincipal"]?.ToString() ?? string.Empty,
                    MaquinaSustituta = rd["MaquinaSustituta"]?.ToString() ?? string.Empty,
                    Ciclo = rd["Ciclo"]?.ToString() ?? string.Empty,
                    ObjetivoHora = rd["ObjetivoHora"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(rd["ObjetivoHora"]),
                    MaterialCodigo = rd["MaterialCodigo"]?.ToString() ?? string.Empty,
                    MaterialDescripcion = rd["MaterialDescripcion"]?.ToString() ?? string.Empty
                });
            }
        }

        // Los KPIs deben representar todas las piezas del operador, no solo el filtro visual.
        const string conteosSql = @"
SELECT
    MAX(v.Nivel) AS NivelMaximo,
    COUNT(DISTINCT v.ParteID) AS PartesEvaluadas,
    COUNT(DISTINCT CASE WHEN v.Nivel=4 THEN v.ParteID END) AS Nivel4,
    COUNT(DISTINCT CASE WHEN v.Nivel=3 THEN v.ParteID END) AS Nivel3,
    COUNT(DISTINCT CASE WHEN v.Nivel=2 THEN v.ParteID END) AS Nivel2,
    COUNT(DISTINCT CASE WHEN v.Nivel=1 THEN v.ParteID END) AS Nivel1
FROM dbo.vw_RRHH_PolivalenciaOperadoresParte v
WHERE v.PersonalID=@PersonalID;";

        await using (var cmd = new SqlCommand(conteosSql, cn))
        {
            cmd.Parameters.Add("@PersonalID", SqlDbType.Int).Value = personalId;
            await using var rd = await cmd.ExecuteReaderAsync();
            if (await rd.ReadAsync() && rd["NivelMaximo"] != DBNull.Value)
            {
                vm.NivelMaximo = Convert.ToInt32(rd["NivelMaximo"]);
                vm.PartesEvaluadas = Convert.ToInt32(rd["PartesEvaluadas"]);
                vm.Nivel4 = Convert.ToInt32(rd["Nivel4"]);
                vm.Nivel3 = Convert.ToInt32(rd["Nivel3"]);
                vm.Nivel2 = Convert.ToInt32(rd["Nivel2"]);
                vm.Nivel1 = Convert.ToInt32(rd["Nivel1"]);
            }
        }

        return View(vm);
    }

    private static async Task<bool> PolivalenciaConfiguradaAsync(SqlConnection cn)
    {
        const string existeSql = @"
SELECT CASE
    WHEN OBJECT_ID(N'dbo.RRHH_PolivalenciaCompetencias',N'U') IS NOT NULL
     AND OBJECT_ID(N'dbo.vw_RRHH_PolivalenciaOperadoresParte',N'V') IS NOT NULL
    THEN 1 ELSE 0 END;";

        await using var existe = new SqlCommand(existeSql, cn);
        return Convert.ToInt32(await existe.ExecuteScalarAsync()) == 1;
    }
}
