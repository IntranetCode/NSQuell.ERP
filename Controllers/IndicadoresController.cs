using ERP.NSQuell.Models.ViewModels.Indicadores;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Globalization;
using System.Text;

namespace ERP.NSQuell.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class IndicadoresController : Controller
{
    private readonly IConfiguration _configuration;

    public IndicadoresController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    private string ConnectionString =>
        _configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("No se encontró ConnectionStrings:DefaultConnection.");

    [HttpGet("/Indicadores")]
    [HttpGet("/Indicadores/Index")]
    [HttpGet("/Direccion")]
    [HttpGet("/Direccion/Index")]
    public async Task<IActionResult> Index(
        string? periodo = null,
        string? semana = null,
        string? mes = null,
        DateTime? fecha = null,
        DateTime? desde = null,
        DateTime? hasta = null,
        string? area = null,
        CancellationToken cancellationToken = default)
    {
        var usuarioId = HttpContext.Session.GetInt32("UsuarioID");
        if (!usuarioId.HasValue)
            return RedirectToAction("Login", "Login");

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);

        if (!await TieneAccesoIndicadoresAsync(connection, usuarioId.Value, cancellationToken))
            return Forbid();

        var (fechaDesde, fechaHasta, modoPeriodo, periodoLimitado) = ResolverPeriodo(periodo, semana, mes, fecha, desde, hasta);

        var seccion = NormalizarSeccion(area);
        var vm = new IndicadoresDashboardVm
        {
            Desde = fechaDesde,
            Hasta = fechaHasta,
            GeneradoEn = DateTime.Now,
            Seccion = seccion,
            Periodo = modoPeriodo,
            PeriodoLimitado = periodoLimitado
        };

        if (seccion is "general" or "produccion")
            await CargarProduccionAsync(connection, vm, cancellationToken);

        if (seccion == "produccion")
        {
            await CargarOperadoresAsync(connection, vm, cancellationToken);
            await CargarMaquinasAsync(connection, vm, cancellationToken);
            await CargarTendenciaAsync(connection, vm, cancellationToken);
            await CargarProgramasProduccionAsync(connection, vm, cancellationToken);
            await CargarParosProduccionAsync(connection, vm, cancellationToken);
            await CargarPersonalApoyoProduccionAsync(connection, vm, cancellationToken);
        }

        if (seccion != "produccion")
            await CargarDepartamentosAsync(connection, vm, cancellationToken);

        if (seccion == "general")
            ConstruirAlertas(vm);

        return View(vm);
    }

    private async Task<bool> TieneAccesoIndicadoresAsync(
        SqlConnection connection,
        int usuarioId,
        CancellationToken cancellationToken)
    {
        var rolId = HttpContext.Session.GetInt32("RolID");
        var rol = HttpContext.Session.GetString("NombreRol")
                  ?? HttpContext.Session.GetString("Rol")
                  ?? string.Empty;

        if (rolId is 1 or 2 or 3)
            return true;

        var rolNormalizado = Normalizar(rol);
        if (rolNormalizado.Contains("ADMIN", StringComparison.Ordinal)
            || rolNormalizado.Contains("DIRECCION", StringComparison.Ordinal)
            || rolNormalizado.Contains("GERENCIA", StringComparison.Ordinal))
        {
            return true;
        }

        const string sql = @"
SELECT TOP (1) d.NombreDepartamento
FROM dbo.Usuarios u
LEFT JOIN dbo.Departamentos d ON d.DepartamentoID=u.DepartamentoID
WHERE u.UsuarioID=@UsuarioID
  AND ISNULL(u.Activo,1)=1;";

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
        var value = await command.ExecuteScalarAsync(cancellationToken);
        var departamento = Normalizar(value?.ToString());

        return departamento is "DIRECCION" or "GERENCIA";
    }

    private static async Task CargarProduccionAsync(
        SqlConnection connection,
        IndicadoresDashboardVm vm,
        CancellationToken cancellationToken)
    {
        // NSQ_INDICADORES_PRODUCCION_ANALITICO_V1
        // El estandar del KPI NO usa ObjetivoHora/ObjetivoBloque de Produccion_RegistroHora
        // ni Produccion_ConfiguracionCorrida. Primero usa el snapshot guardado por Planeacion
        // y solo si falta, cae al maestro ERP_ParteDatosTecnicos.
        const string sql = @"
WITH B AS
(
    SELECT
        rh.RegistroHoraID,
        rh.ProgramaProduccionID,
        rh.OperadorID,
        ISNULL(rh.CantidadOK,0) AS CantidadOK,
        ISNULL(rh.CantidadSospechosa,0) AS CantidadSospechosa,
        ISNULL(rh.CantidadScrap,0) AS CantidadScrap,
        minutos.MinutosBloque,
        COALESCE(NULLIF(pp.ObjetivoHora,0),NULLIF(dt.ObjetivoHora,0),0) AS ObjetivoHoraEstandar
    FROM dbo.Produccion_RegistroHora rh
    LEFT JOIN dbo.Planeacion_ProgramaProduccion pp
        ON pp.ProgramaProduccionID=rh.ProgramaProduccionID
    LEFT JOIN dbo.Produccion_Ejecucion e
        ON e.EjecucionProduccionID=rh.EjecucionProduccionID
    OUTER APPLY
    (
        SELECT TOP(1) dt0.ObjetivoHora
        FROM dbo.ERP_ParteDatosTecnicos dt0
        WHERE dt0.ParteID=COALESCE(pp.ParteID,e.ParteID)
          AND dt0.Activo=1
        ORDER BY dt0.ParteDatoTecnicoID DESC
    ) dt
    CROSS APPLY
    (
        SELECT CONVERT(DECIMAL(18,2),
            CASE
                WHEN rh.HoraFin>=rh.HoraInicio THEN DATEDIFF(MINUTE,rh.HoraInicio,rh.HoraFin)
                ELSE 1440+DATEDIFF(MINUTE,rh.HoraInicio,rh.HoraFin)
            END) AS MinutosBloque
    ) minutos
    WHERE rh.Activo=1
      AND rh.FechaProduccion>=@Desde
      AND rh.FechaProduccion<@HastaExclusiva
)
SELECT
    COUNT(*) AS RegistrosHora,
    COUNT(DISTINCT OperadorID) AS Operadores,
    SUM(CONVERT(BIGINT,CantidadOK)) AS PiezasOK,
    SUM(CONVERT(BIGINT,CantidadSospechosa)) AS PiezasSospechosas,
    SUM(CONVERT(BIGINT,CantidadScrap)) AS PiezasScrap,
    SUM(CONVERT(BIGINT,ROUND(CONVERT(DECIMAL(18,4),ObjetivoHoraEstandar)*MinutosBloque/60.0,0))) AS Objetivo,
    SUM(MinutosBloque) AS MinutosProduccion,
    COUNT(DISTINCT CASE WHEN ObjetivoHoraEstandar<=0 THEN ProgramaProduccionID END) AS ProgramasSinEstandar
FROM B;

SELECT
    SUM(CONVERT(DECIMAL(18,2),
        CASE
            WHEN FechaFinParo IS NULL THEN
                CASE WHEN FechaInicioParo>=GETDATE() THEN 0
                     ELSE DATEDIFF(MINUTE,FechaInicioParo,GETDATE()) END
            WHEN DuracionMinutos IS NOT NULL THEN DuracionMinutos
            WHEN FechaFinParo>=FechaInicioParo THEN DATEDIFF(MINUTE,FechaInicioParo,FechaFinParo)
            ELSE 0
        END)) AS MinutosParo
FROM dbo.Produccion_Paros
WHERE Activo=1
  AND FechaInicioParo>=@Desde
  AND FechaInicioParo<@HastaExclusiva;";

        await using var command = new SqlCommand(sql, connection);
        AddPeriodo(command, vm.Desde, vm.Hasta);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (await reader.ReadAsync(cancellationToken))
        {
            vm.Produccion.RegistrosHora = Int(reader, "RegistrosHora");
            vm.Produccion.Operadores = Int(reader, "Operadores");
            vm.Produccion.PiezasOK = Long(reader, "PiezasOK");
            vm.Produccion.PiezasSospechosas = Long(reader, "PiezasSospechosas");
            vm.Produccion.PiezasScrap = Long(reader, "PiezasScrap");
            vm.Produccion.Objetivo = Long(reader, "Objetivo");
            vm.Produccion.MinutosProduccion = Decimal(reader, "MinutosProduccion");
            vm.Produccion.ProgramasSinEstandar = Int(reader, "ProgramasSinEstandar");
        }

        if (await reader.NextResultAsync(cancellationToken)
            && await reader.ReadAsync(cancellationToken))
        {
            vm.Produccion.MinutosParo = Decimal(reader, "MinutosParo");
        }
    }

    private static async Task CargarOperadoresAsync(
        SqlConnection connection,
        IndicadoresDashboardVm vm,
        CancellationToken cancellationToken)
    {
        const string sql = @"
WITH B AS
(
    SELECT
        rh.OperadorID,
        ISNULL(rh.CantidadOK,0) AS CantidadOK,
        ISNULL(rh.CantidadSospechosa,0) AS CantidadSospechosa,
        ISNULL(rh.CantidadScrap,0) AS CantidadScrap,
        minutos.MinutosBloque,
        CONVERT(BIGINT,ROUND(CONVERT(DECIMAL(18,4),COALESCE(NULLIF(pp.ObjetivoHora,0),NULLIF(dt.ObjetivoHora,0),0))*minutos.MinutosBloque/60.0,0)) AS ObjetivoBloque,
        NULLIF(LTRIM(RTRIM(e.OperadorNombre)),N'') AS OperadorSnapshot,
        COALESCE(NULLIF(LTRIM(RTRIM(pp.NumeroParte)),N''),NULLIF(LTRIM(RTRIM(e.NumeroParte)),N''),N'Sin parte') AS NumeroParte,
        COALESCE(NULLIF(LTRIM(RTRIM(pp.DesignacionDescripcionSAP)),N''),NULLIF(LTRIM(RTRIM(e.DescripcionParte)),N''),N'') AS DescripcionParte,
        NULLIF(LTRIM(RTRIM(rh.Turno)),N'') AS Turno,
        COALESCE(NULLIF(LTRIM(RTRIM(m.Codigo)),N''),NULLIF(LTRIM(RTRIM(e.MaquinaCodigo)),N''),NULLIF(LTRIM(RTRIM(pp.MaquinaCodigo)),N''),N'Sin máquina') AS Maquina,
        COALESCE(NULLIF(LTRIM(RTRIM(s.NumeroOFRecibida)),N''),NULLIF(LTRIM(RTRIM(s.FolioSolicitud)),N''),CONCAT(N'Programa ',rh.ProgramaProduccionID)) AS NumeroOF
    FROM dbo.Produccion_RegistroHora rh
    LEFT JOIN dbo.Produccion_Ejecucion e
        ON e.EjecucionProduccionID=rh.EjecucionProduccionID
    LEFT JOIN dbo.Planeacion_ProgramaProduccion pp
        ON pp.ProgramaProduccionID=rh.ProgramaProduccionID
    LEFT JOIN dbo.SolicitudesProduccion s
        ON s.SolicitudProduccionID=COALESCE(rh.SolicitudProduccionID,e.SolicitudProduccionID,pp.SolicitudProduccionID)
    LEFT JOIN dbo.ERP_Maquinas m
        ON m.MaquinaID=COALESCE(rh.MaquinaID,e.MaquinaID,pp.MaquinaID)
    OUTER APPLY
    (
        SELECT TOP(1) dt0.ObjetivoHora
        FROM dbo.ERP_ParteDatosTecnicos dt0
        WHERE dt0.ParteID=COALESCE(pp.ParteID,e.ParteID)
          AND dt0.Activo=1
        ORDER BY dt0.ParteDatoTecnicoID DESC
    ) dt
    CROSS APPLY
    (
        SELECT CONVERT(DECIMAL(18,2),CASE WHEN rh.HoraFin>=rh.HoraInicio
            THEN DATEDIFF(MINUTE,rh.HoraInicio,rh.HoraFin)
            ELSE 1440+DATEDIFF(MINUTE,rh.HoraInicio,rh.HoraFin) END) AS MinutosBloque
    ) minutos
    WHERE rh.Activo=1
      AND rh.OperadorID IS NOT NULL
      AND rh.FechaProduccion>=@Desde
      AND rh.FechaProduccion<@HastaExclusiva
),
R AS
(
    SELECT
        OperadorID,
        COUNT(*) AS Registros,
        COUNT(DISTINCT NumeroParte) AS PartesTrabajadas,
        COUNT(DISTINCT Turno) AS TurnosTrabajados,
        COUNT(DISTINCT Maquina) AS MaquinasTrabajadas,
        COUNT(DISTINCT NumeroOF) AS OfTrabajadas,
        SUM(CONVERT(BIGINT,CantidadOK)) AS PiezasOK,
        SUM(CONVERT(BIGINT,CantidadSospechosa)) AS PiezasSospechosas,
        SUM(CONVERT(BIGINT,CantidadScrap)) AS PiezasScrap,
        SUM(ObjetivoBloque) AS Objetivo,
        SUM(MinutosBloque) AS MinutosProduccion,
        MAX(OperadorSnapshot) AS OperadorSnapshot
    FROM B
    GROUP BY OperadorID
),
P AS
(
    SELECT
        COALESCE(p.OperadorID,e.OperadorID) AS OperadorID,
        SUM(CONVERT(DECIMAL(18,2),CASE
            WHEN p.FechaFinParo IS NULL THEN CASE WHEN p.FechaInicioParo>=GETDATE() THEN 0 ELSE DATEDIFF(MINUTE,p.FechaInicioParo,GETDATE()) END
            WHEN p.DuracionMinutos IS NOT NULL THEN p.DuracionMinutos
            WHEN p.FechaFinParo>=p.FechaInicioParo THEN DATEDIFF(MINUTE,p.FechaInicioParo,p.FechaFinParo)
            ELSE 0 END)) AS MinutosParo
    FROM dbo.Produccion_Paros p
    LEFT JOIN dbo.Produccion_Ejecucion e ON e.EjecucionProduccionID=p.EjecucionProduccionID
    WHERE p.Activo=1
      AND COALESCE(p.OperadorID,e.OperadorID) IS NOT NULL
      AND p.FechaInicioParo>=@Desde
      AND p.FechaInicioParo<@HastaExclusiva
    GROUP BY COALESCE(p.OperadorID,e.OperadorID)
),
ParteAgg AS
(
    SELECT OperadorID,NumeroParte,MAX(DescripcionParte) AS DescripcionParte,
           SUM(CONVERT(BIGINT,CantidadOK)) AS PiezasOK,SUM(ObjetivoBloque) AS Objetivo
    FROM B GROUP BY OperadorID,NumeroParte
),
ParteRank AS
(
    SELECT *,ROW_NUMBER() OVER(PARTITION BY OperadorID ORDER BY
        CASE WHEN Objetivo>0 THEN CONVERT(DECIMAL(18,4),PiezasOK)*100/Objetivo ELSE 0 END DESC,
        PiezasOK DESC,NumeroParte) AS rn
    FROM ParteAgg WHERE Objetivo>0
),
TurnoAgg AS
(
    SELECT OperadorID,Turno,SUM(CONVERT(BIGINT,CantidadOK)) AS PiezasOK,SUM(ObjetivoBloque) AS Objetivo
    FROM B WHERE Turno IS NOT NULL GROUP BY OperadorID,Turno
),
TurnoRank AS
(
    SELECT *,ROW_NUMBER() OVER(PARTITION BY OperadorID ORDER BY
        CASE WHEN Objetivo>0 THEN CONVERT(DECIMAL(18,4),PiezasOK)*100/Objetivo ELSE 0 END DESC,
        PiezasOK DESC,Turno) AS rn
    FROM TurnoAgg WHERE Objetivo>0
),
MaquinaAgg AS
(
    SELECT OperadorID,Maquina,SUM(CONVERT(BIGINT,CantidadOK)) AS PiezasOK,SUM(ObjetivoBloque) AS Objetivo
    FROM B GROUP BY OperadorID,Maquina
),
MaquinaRank AS
(
    SELECT *,ROW_NUMBER() OVER(PARTITION BY OperadorID ORDER BY
        CASE WHEN Objetivo>0 THEN CONVERT(DECIMAL(18,4),PiezasOK)*100/Objetivo ELSE 0 END DESC,
        PiezasOK DESC,Maquina) AS rn
    FROM MaquinaAgg WHERE Objetivo>0
),
OfAgg AS
(
    SELECT OperadorID,NumeroOF,SUM(CONVERT(BIGINT,CantidadOK)) AS PiezasOK,SUM(ObjetivoBloque) AS Objetivo
    FROM B GROUP BY OperadorID,NumeroOF
),
OfRank AS
(
    SELECT *,ROW_NUMBER() OVER(PARTITION BY OperadorID ORDER BY
        CASE WHEN Objetivo>0 THEN CONVERT(DECIMAL(18,4),PiezasOK)*100/Objetivo ELSE 0 END DESC,
        PiezasOK DESC,NumeroOF) AS rn
    FROM OfAgg WHERE Objetivo>0
)
SELECT TOP (40)
    r.OperadorID,
    COALESCE(
        NULLIF(LTRIM(RTRIM(CONCAT(per.Nombre,N' ',per.ApellidoPaterno,N' ',per.ApellidoMaterno))),N''),
        r.OperadorSnapshot,
        CONCAT(N'Operador #',r.OperadorID)
    ) AS Operador,
    ISNULL(per.NumeroControl,N'') AS NumeroControl,
    r.PiezasOK,r.PiezasSospechosas,r.PiezasScrap,r.Objetivo,r.MinutosProduccion,
    ISNULL(p.MinutosParo,0) AS MinutosParo,r.Registros,
    r.PartesTrabajadas,r.TurnosTrabajados,r.MaquinasTrabajadas,r.OfTrabajadas,
    ISNULL(bp.NumeroParte,N'') AS MejorParte,
    ISNULL(bp.DescripcionParte,N'') AS MejorParteDescripcion,
    ISNULL(bp.PiezasOK,0) AS MejorParteOK,
    CONVERT(DECIMAL(18,2),CASE WHEN ISNULL(bp.Objetivo,0)>0 THEN CONVERT(DECIMAL(18,4),bp.PiezasOK)*100/bp.Objetivo ELSE 0 END) AS MejorParteRqtPct,
    ISNULL(bt.Turno,N'') AS MejorTurno,
    ISNULL(bt.PiezasOK,0) AS MejorTurnoOK,
    CONVERT(DECIMAL(18,2),CASE WHEN ISNULL(bt.Objetivo,0)>0 THEN CONVERT(DECIMAL(18,4),bt.PiezasOK)*100/bt.Objetivo ELSE 0 END) AS MejorTurnoRqtPct,
    ISNULL(bm.Maquina,N'') AS MejorMaquina,
    CONVERT(DECIMAL(18,2),CASE WHEN ISNULL(bm.Objetivo,0)>0 THEN CONVERT(DECIMAL(18,4),bm.PiezasOK)*100/bm.Objetivo ELSE 0 END) AS MejorMaquinaRqtPct,
    ISNULL(bo.NumeroOF,N'') AS MejorOF,
    CONVERT(DECIMAL(18,2),CASE WHEN ISNULL(bo.Objetivo,0)>0 THEN CONVERT(DECIMAL(18,4),bo.PiezasOK)*100/bo.Objetivo ELSE 0 END) AS MejorOFRqtPct
FROM R r
LEFT JOIN P p ON p.OperadorID=r.OperadorID
LEFT JOIN dbo.Persona per ON per.PersonaID=r.OperadorID
LEFT JOIN ParteRank bp ON bp.OperadorID=r.OperadorID AND bp.rn=1
LEFT JOIN TurnoRank bt ON bt.OperadorID=r.OperadorID AND bt.rn=1
LEFT JOIN MaquinaRank bm ON bm.OperadorID=r.OperadorID AND bm.rn=1
LEFT JOIN OfRank bo ON bo.OperadorID=r.OperadorID AND bo.rn=1
ORDER BY r.PiezasOK DESC,r.OperadorID;";

        await using var command = new SqlCommand(sql, connection);
        AddPeriodo(command, vm.Desde, vm.Hasta);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            vm.Operadores.Add(new IndicadoresOperadorKpiVm
            {
                OperadorID = Int(reader, "OperadorID"),
                Operador = Text(reader, "Operador"),
                NumeroControl = Text(reader, "NumeroControl"),
                PiezasOK = Long(reader, "PiezasOK"),
                PiezasSospechosas = Long(reader, "PiezasSospechosas"),
                PiezasScrap = Long(reader, "PiezasScrap"),
                Objetivo = Long(reader, "Objetivo"),
                MinutosProduccion = Decimal(reader, "MinutosProduccion"),
                MinutosParo = Decimal(reader, "MinutosParo"),
                Registros = Int(reader, "Registros"),
                PartesTrabajadas = Int(reader, "PartesTrabajadas"),
                TurnosTrabajados = Int(reader, "TurnosTrabajados"),
                MaquinasTrabajadas = Int(reader, "MaquinasTrabajadas"),
                OfTrabajadas = Int(reader, "OfTrabajadas"),
                MejorParte = Text(reader, "MejorParte"),
                MejorParteDescripcion = Text(reader, "MejorParteDescripcion"),
                MejorParteOK = Long(reader, "MejorParteOK"),
                MejorParteRqtPct = Decimal(reader, "MejorParteRqtPct"),
                MejorTurno = Text(reader, "MejorTurno"),
                MejorTurnoOK = Long(reader, "MejorTurnoOK"),
                MejorTurnoRqtPct = Decimal(reader, "MejorTurnoRqtPct"),
                MejorMaquina = Text(reader, "MejorMaquina"),
                MejorMaquinaRqtPct = Decimal(reader, "MejorMaquinaRqtPct"),
                MejorOF = Text(reader, "MejorOF"),
                MejorOFRqtPct = Decimal(reader, "MejorOFRqtPct")
            });
        }

        vm.Operadores = vm.Operadores
            .OrderByDescending(x => x.OeePct)
            .ThenByDescending(x => x.PiezasOK)
            .Take(20)
            .ToList();
    }

    private static async Task CargarMaquinasAsync(
        SqlConnection connection,
        IndicadoresDashboardVm vm,
        CancellationToken cancellationToken)
    {
        const string sql = @"
WITH B AS
(
    SELECT
        rh.MaquinaID,
        ISNULL(rh.CantidadOK,0) AS CantidadOK,
        ISNULL(rh.CantidadSospechosa,0) AS CantidadSospechosa,
        ISNULL(rh.CantidadScrap,0) AS CantidadScrap,
        minutos.MinutosBloque,
        CONVERT(BIGINT,ROUND(CONVERT(DECIMAL(18,4),COALESCE(NULLIF(pp.ObjetivoHora,0),NULLIF(dt.ObjetivoHora,0),0))*minutos.MinutosBloque/60.0,0)) AS ObjetivoBloque,
        NULLIF(LTRIM(RTRIM(e.MaquinaNombre)),N'') AS MaquinaSnapshot,
        COALESCE(NULLIF(LTRIM(RTRIM(pp.NumeroParte)),N''),NULLIF(LTRIM(RTRIM(e.NumeroParte)),N''),N'Sin parte') AS NumeroParte,
        COALESCE(NULLIF(LTRIM(RTRIM(pp.DesignacionDescripcionSAP)),N''),NULLIF(LTRIM(RTRIM(e.DescripcionParte)),N''),N'') AS DescripcionParte,
        NULLIF(LTRIM(RTRIM(rh.Turno)),N'') AS Turno,
        COALESCE(
            NULLIF(LTRIM(RTRIM(CONCAT(per.Nombre,N' ',per.ApellidoPaterno,N' ',per.ApellidoMaterno))),N''),
            NULLIF(LTRIM(RTRIM(e.OperadorNombre)),N''),
            N'Sin operador') AS Operador,
        COALESCE(NULLIF(LTRIM(RTRIM(s.NumeroOFRecibida)),N''),NULLIF(LTRIM(RTRIM(s.FolioSolicitud)),N''),CONCAT(N'Programa ',rh.ProgramaProduccionID)) AS NumeroOF
    FROM dbo.Produccion_RegistroHora rh
    LEFT JOIN dbo.Produccion_Ejecucion e ON e.EjecucionProduccionID=rh.EjecucionProduccionID
    LEFT JOIN dbo.Planeacion_ProgramaProduccion pp ON pp.ProgramaProduccionID=rh.ProgramaProduccionID
    LEFT JOIN dbo.SolicitudesProduccion s ON s.SolicitudProduccionID=COALESCE(rh.SolicitudProduccionID,e.SolicitudProduccionID,pp.SolicitudProduccionID)
    LEFT JOIN dbo.Persona per ON per.PersonaID=rh.OperadorID
    OUTER APPLY
    (
        SELECT TOP(1) dt0.ObjetivoHora
        FROM dbo.ERP_ParteDatosTecnicos dt0
        WHERE dt0.ParteID=COALESCE(pp.ParteID,e.ParteID)
          AND dt0.Activo=1
        ORDER BY dt0.ParteDatoTecnicoID DESC
    ) dt
    CROSS APPLY
    (
        SELECT CONVERT(DECIMAL(18,2),CASE WHEN rh.HoraFin>=rh.HoraInicio
            THEN DATEDIFF(MINUTE,rh.HoraInicio,rh.HoraFin)
            ELSE 1440+DATEDIFF(MINUTE,rh.HoraInicio,rh.HoraFin) END) AS MinutosBloque
    ) minutos
    WHERE rh.Activo=1
      AND rh.MaquinaID IS NOT NULL
      AND rh.FechaProduccion>=@Desde
      AND rh.FechaProduccion<@HastaExclusiva
),
R AS
(
    SELECT
        MaquinaID,
        COUNT(DISTINCT NumeroParte) AS PartesTrabajadas,
        COUNT(DISTINCT Turno) AS TurnosTrabajados,
        COUNT(DISTINCT Operador) AS OperadoresTrabajados,
        COUNT(DISTINCT NumeroOF) AS OfTrabajadas,
        SUM(CONVERT(BIGINT,CantidadOK)) AS PiezasOK,
        SUM(CONVERT(BIGINT,CantidadSospechosa)) AS PiezasSospechosas,
        SUM(CONVERT(BIGINT,CantidadScrap)) AS PiezasScrap,
        SUM(ObjetivoBloque) AS Objetivo,
        SUM(MinutosBloque) AS MinutosProduccion,
        MAX(MaquinaSnapshot) AS MaquinaSnapshot
    FROM B GROUP BY MaquinaID
),
P AS
(
    SELECT COALESCE(p.MaquinaID,e.MaquinaID,pp.MaquinaID) AS MaquinaID,
        SUM(CONVERT(DECIMAL(18,2),CASE
            WHEN p.FechaFinParo IS NULL THEN CASE WHEN p.FechaInicioParo>=GETDATE() THEN 0 ELSE DATEDIFF(MINUTE,p.FechaInicioParo,GETDATE()) END
            WHEN p.DuracionMinutos IS NOT NULL THEN p.DuracionMinutos
            WHEN p.FechaFinParo>=p.FechaInicioParo THEN DATEDIFF(MINUTE,p.FechaInicioParo,p.FechaFinParo)
            ELSE 0 END)) AS MinutosParo
    FROM dbo.Produccion_Paros p
    LEFT JOIN dbo.Produccion_Ejecucion e ON e.EjecucionProduccionID=p.EjecucionProduccionID
    LEFT JOIN dbo.Planeacion_ProgramaProduccion pp ON pp.ProgramaProduccionID=p.ProgramaProduccionID
    WHERE p.Activo=1
      AND COALESCE(p.MaquinaID,e.MaquinaID,pp.MaquinaID) IS NOT NULL
      AND p.FechaInicioParo>=@Desde
      AND p.FechaInicioParo<@HastaExclusiva
    GROUP BY COALESCE(p.MaquinaID,e.MaquinaID,pp.MaquinaID)
),
ParteAgg AS
(
    SELECT MaquinaID,NumeroParte,MAX(DescripcionParte) AS DescripcionParte,
           SUM(CONVERT(BIGINT,CantidadOK)) AS PiezasOK,SUM(ObjetivoBloque) AS Objetivo
    FROM B GROUP BY MaquinaID,NumeroParte
),
ParteRank AS
(
    SELECT *,ROW_NUMBER() OVER(PARTITION BY MaquinaID ORDER BY
        CASE WHEN Objetivo>0 THEN CONVERT(DECIMAL(18,4),PiezasOK)*100/Objetivo ELSE 0 END DESC,
        PiezasOK DESC,NumeroParte) AS rn FROM ParteAgg WHERE Objetivo>0
),
TurnoAgg AS
(
    SELECT MaquinaID,Turno,SUM(CONVERT(BIGINT,CantidadOK)) AS PiezasOK,SUM(ObjetivoBloque) AS Objetivo
    FROM B WHERE Turno IS NOT NULL GROUP BY MaquinaID,Turno
),
TurnoRank AS
(
    SELECT *,ROW_NUMBER() OVER(PARTITION BY MaquinaID ORDER BY
        CASE WHEN Objetivo>0 THEN CONVERT(DECIMAL(18,4),PiezasOK)*100/Objetivo ELSE 0 END DESC,
        PiezasOK DESC,Turno) AS rn FROM TurnoAgg WHERE Objetivo>0
),
OperadorAgg AS
(
    SELECT MaquinaID,Operador,SUM(CONVERT(BIGINT,CantidadOK)) AS PiezasOK,SUM(ObjetivoBloque) AS Objetivo
    FROM B GROUP BY MaquinaID,Operador
),
OperadorRank AS
(
    SELECT *,ROW_NUMBER() OVER(PARTITION BY MaquinaID ORDER BY
        CASE WHEN Objetivo>0 THEN CONVERT(DECIMAL(18,4),PiezasOK)*100/Objetivo ELSE 0 END DESC,
        PiezasOK DESC,Operador) AS rn FROM OperadorAgg WHERE Objetivo>0
),
OfAgg AS
(
    SELECT MaquinaID,NumeroOF,SUM(CONVERT(BIGINT,CantidadOK)) AS PiezasOK,SUM(ObjetivoBloque) AS Objetivo
    FROM B GROUP BY MaquinaID,NumeroOF
),
OfRank AS
(
    SELECT *,ROW_NUMBER() OVER(PARTITION BY MaquinaID ORDER BY
        CASE WHEN Objetivo>0 THEN CONVERT(DECIMAL(18,4),PiezasOK)*100/Objetivo ELSE 0 END DESC,
        PiezasOK DESC,NumeroOF) AS rn FROM OfAgg WHERE Objetivo>0
)
SELECT TOP (30)
    r.MaquinaID,
    COALESCE(NULLIF(LTRIM(RTRIM(m.Codigo)),N''),NULLIF(LTRIM(RTRIM(r.MaquinaSnapshot)),N''),CONCAT(N'Máquina #',r.MaquinaID)) AS Maquina,
    r.PiezasOK,r.PiezasSospechosas,r.PiezasScrap,r.Objetivo,r.MinutosProduccion,
    ISNULL(p.MinutosParo,0) AS MinutosParo,
    r.PartesTrabajadas,r.TurnosTrabajados,r.OperadoresTrabajados,r.OfTrabajadas,
    ISNULL(bp.NumeroParte,N'') AS MejorParte,
    ISNULL(bp.DescripcionParte,N'') AS MejorParteDescripcion,
    CONVERT(DECIMAL(18,2),CASE WHEN ISNULL(bp.Objetivo,0)>0 THEN CONVERT(DECIMAL(18,4),bp.PiezasOK)*100/bp.Objetivo ELSE 0 END) AS MejorParteRqtPct,
    ISNULL(bt.Turno,N'') AS MejorTurno,
    CONVERT(DECIMAL(18,2),CASE WHEN ISNULL(bt.Objetivo,0)>0 THEN CONVERT(DECIMAL(18,4),bt.PiezasOK)*100/bt.Objetivo ELSE 0 END) AS MejorTurnoRqtPct,
    ISNULL(bop.Operador,N'') AS MejorOperador,
    CONVERT(DECIMAL(18,2),CASE WHEN ISNULL(bop.Objetivo,0)>0 THEN CONVERT(DECIMAL(18,4),bop.PiezasOK)*100/bop.Objetivo ELSE 0 END) AS MejorOperadorRqtPct,
    ISNULL(bo.NumeroOF,N'') AS MejorOF,
    CONVERT(DECIMAL(18,2),CASE WHEN ISNULL(bo.Objetivo,0)>0 THEN CONVERT(DECIMAL(18,4),bo.PiezasOK)*100/bo.Objetivo ELSE 0 END) AS MejorOFRqtPct
FROM R r
LEFT JOIN P p ON p.MaquinaID=r.MaquinaID
LEFT JOIN dbo.ERP_Maquinas m ON m.MaquinaID=r.MaquinaID
LEFT JOIN ParteRank bp ON bp.MaquinaID=r.MaquinaID AND bp.rn=1
LEFT JOIN TurnoRank bt ON bt.MaquinaID=r.MaquinaID AND bt.rn=1
LEFT JOIN OperadorRank bop ON bop.MaquinaID=r.MaquinaID AND bop.rn=1
LEFT JOIN OfRank bo ON bo.MaquinaID=r.MaquinaID AND bo.rn=1
ORDER BY r.PiezasOK DESC,r.MaquinaID;";

        await using var command = new SqlCommand(sql, connection);
        AddPeriodo(command, vm.Desde, vm.Hasta);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            vm.Maquinas.Add(new IndicadoresMaquinaKpiVm
            {
                MaquinaID = Int(reader, "MaquinaID"),
                Maquina = Text(reader, "Maquina"),
                PiezasOK = Long(reader, "PiezasOK"),
                PiezasSospechosas = Long(reader, "PiezasSospechosas"),
                PiezasScrap = Long(reader, "PiezasScrap"),
                Objetivo = Long(reader, "Objetivo"),
                MinutosProduccion = Decimal(reader, "MinutosProduccion"),
                MinutosParo = Decimal(reader, "MinutosParo"),
                PartesTrabajadas = Int(reader, "PartesTrabajadas"),
                TurnosTrabajados = Int(reader, "TurnosTrabajados"),
                OperadoresTrabajados = Int(reader, "OperadoresTrabajados"),
                OfTrabajadas = Int(reader, "OfTrabajadas"),
                MejorParte = Text(reader, "MejorParte"),
                MejorParteDescripcion = Text(reader, "MejorParteDescripcion"),
                MejorParteRqtPct = Decimal(reader, "MejorParteRqtPct"),
                MejorTurno = Text(reader, "MejorTurno"),
                MejorTurnoRqtPct = Decimal(reader, "MejorTurnoRqtPct"),
                MejorOperador = Text(reader, "MejorOperador"),
                MejorOperadorRqtPct = Decimal(reader, "MejorOperadorRqtPct"),
                MejorOF = Text(reader, "MejorOF"),
                MejorOFRqtPct = Decimal(reader, "MejorOFRqtPct")
            });
        }

        vm.Maquinas = vm.Maquinas
            .OrderByDescending(x => x.OeePct)
            .ThenByDescending(x => x.PiezasOK)
            .Take(12)
            .ToList();
    }

    private static async Task CargarTendenciaAsync(
        SqlConnection connection,
        IndicadoresDashboardVm vm,
        CancellationToken cancellationToken)
    {
        const string sql = @"
WITH B AS
(
    SELECT
        rh.FechaProduccion AS Fecha,
        ISNULL(rh.CantidadOK,0) AS CantidadOK,
        ISNULL(rh.CantidadSospechosa,0) AS CantidadSospechosa,
        ISNULL(rh.CantidadScrap,0) AS CantidadScrap,
        minutos.MinutosBloque,
        COALESCE(NULLIF(pp.ObjetivoHora,0),NULLIF(dt.ObjetivoHora,0),0) AS ObjetivoHoraEstandar
    FROM dbo.Produccion_RegistroHora rh
    LEFT JOIN dbo.Planeacion_ProgramaProduccion pp ON pp.ProgramaProduccionID=rh.ProgramaProduccionID
    LEFT JOIN dbo.Produccion_Ejecucion e ON e.EjecucionProduccionID=rh.EjecucionProduccionID
    OUTER APPLY
    (
        SELECT TOP(1) dt0.ObjetivoHora
        FROM dbo.ERP_ParteDatosTecnicos dt0
        WHERE dt0.ParteID=COALESCE(pp.ParteID,e.ParteID)
          AND dt0.Activo=1
        ORDER BY dt0.ParteDatoTecnicoID DESC
    ) dt
    CROSS APPLY
    (
        SELECT CONVERT(DECIMAL(18,2),CASE WHEN rh.HoraFin>=rh.HoraInicio
            THEN DATEDIFF(MINUTE,rh.HoraInicio,rh.HoraFin)
            ELSE 1440+DATEDIFF(MINUTE,rh.HoraInicio,rh.HoraFin) END) AS MinutosBloque
    ) minutos
    WHERE rh.Activo=1
      AND rh.FechaProduccion>=@Desde
      AND rh.FechaProduccion<@HastaExclusiva
),
R AS
(
    SELECT Fecha,
        SUM(CONVERT(BIGINT,CantidadOK)) AS PiezasOK,
        SUM(CONVERT(BIGINT,CantidadSospechosa)) AS PiezasSospechosas,
        SUM(CONVERT(BIGINT,CantidadScrap)) AS PiezasScrap,
        SUM(CONVERT(BIGINT,ROUND(CONVERT(DECIMAL(18,4),ObjetivoHoraEstandar)*MinutosBloque/60.0,0))) AS Objetivo,
        SUM(MinutosBloque) AS MinutosProduccion
    FROM B
    GROUP BY Fecha
),
P AS
(
    SELECT CAST(FechaInicioParo AS date) AS Fecha,
        SUM(CONVERT(DECIMAL(18,2),CASE
            WHEN FechaFinParo IS NULL THEN CASE WHEN FechaInicioParo>=GETDATE() THEN 0 ELSE DATEDIFF(MINUTE,FechaInicioParo,GETDATE()) END
            WHEN DuracionMinutos IS NOT NULL THEN DuracionMinutos
            WHEN FechaFinParo>=FechaInicioParo THEN DATEDIFF(MINUTE,FechaInicioParo,FechaFinParo)
            ELSE 0 END)) AS MinutosParo
    FROM dbo.Produccion_Paros
    WHERE Activo=1
      AND FechaInicioParo>=@Desde
      AND FechaInicioParo<@HastaExclusiva
    GROUP BY CAST(FechaInicioParo AS date)
)
SELECT r.Fecha,r.PiezasOK,r.PiezasSospechosas,r.PiezasScrap,r.Objetivo,r.MinutosProduccion,
       ISNULL(p.MinutosParo,0) AS MinutosParo
FROM R r
LEFT JOIN P p ON p.Fecha=r.Fecha
ORDER BY r.Fecha;";

        await using var command = new SqlCommand(sql, connection);
        AddPeriodo(command, vm.Desde, vm.Hasta);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            vm.Tendencia.Add(new IndicadoresTendenciaDiaVm
            {
                Fecha = Convert.ToDateTime(reader["Fecha"]),
                PiezasOK = Long(reader, "PiezasOK"),
                PiezasSospechosas = Long(reader, "PiezasSospechosas"),
                PiezasScrap = Long(reader, "PiezasScrap"),
                Objetivo = Long(reader, "Objetivo"),
                MinutosProduccion = Decimal(reader, "MinutosProduccion"),
                MinutosParo = Decimal(reader, "MinutosParo")
            });
        }
    }

    private static async Task CargarProgramasProduccionAsync(
        SqlConnection connection,
        IndicadoresDashboardVm vm,
        CancellationToken cancellationToken)
    {
        const string sql = @"
WITH B AS
(
    SELECT
        rh.ProgramaProduccionID,
        rh.EjecucionProduccionID,
        ISNULL(rh.CantidadOK,0) AS CantidadOK,
        ISNULL(rh.CantidadSospechosa,0) AS CantidadSospechosa,
        ISNULL(rh.CantidadScrap,0) AS CantidadScrap,
        minutos.MinutosBloque,
        CONVERT(BIGINT,ROUND(CONVERT(DECIMAL(18,4),COALESCE(NULLIF(pp.ObjetivoHora,0),NULLIF(dt.ObjetivoHora,0),0))*minutos.MinutosBloque/60.0,0)) AS ObjetivoBloque,
        COALESCE(NULLIF(pp.ObjetivoHora,0),NULLIF(dt.ObjetivoHora,0),0) AS ObjetivoHoraEstandar,
        COALESCE(NULLIF(LTRIM(RTRIM(pp.Ciclo)),N''),NULLIF(CONVERT(NVARCHAR(80),dt.Ciclo),N''),N'') AS CicloEstandar,
        COALESCE(NULLIF(pp.Cavidades,0),NULLIF(dt.Cavidades,0)) AS CavidadesEstandar,
        CASE
            WHEN ISNULL(pp.ObjetivoHora,0)>0 THEN N'Snapshot de Planeación'
            WHEN ISNULL(dt.ObjetivoHora,0)>0 THEN N'Maestro técnico'
            ELSE N'Sin estándar'
        END AS FuenteEstandar,
        pp.SolicitudProduccionID,
        COALESCE(NULLIF(LTRIM(RTRIM(pp.NumeroParte)),N''),NULLIF(LTRIM(RTRIM(e.NumeroParte)),N''),N'Sin parte') AS Parte,
        COALESCE(NULLIF(LTRIM(RTRIM(pp.DesignacionDescripcionSAP)),N''),NULLIF(LTRIM(RTRIM(e.DescripcionParte)),N''),N'') AS DescripcionParte,
        COALESCE(NULLIF(LTRIM(RTRIM(m.Codigo)),N''),NULLIF(LTRIM(RTRIM(pp.MaquinaCodigo)),N''),NULLIF(LTRIM(RTRIM(e.MaquinaCodigo)),N''),N'Sin máquina') AS Maquina,
        COALESCE(NULLIF(LTRIM(RTRIM(s.NumeroOFRecibida)),N''),NULLIF(LTRIM(RTRIM(s.FolioSolicitud)),N''),CONCAT(N'Programa ',rh.ProgramaProduccionID)) AS NumeroOF,
        NULLIF(LTRIM(RTRIM(rh.Turno)),N'') AS Turno,
        COALESCE(
            NULLIF(LTRIM(RTRIM(CONCAT(per.Nombre,N' ',per.ApellidoPaterno,N' ',per.ApellidoMaterno))),N''),
            NULLIF(LTRIM(RTRIM(e.OperadorNombre)),N''),
            N'Sin operador') AS Operador
    FROM dbo.Produccion_RegistroHora rh
    LEFT JOIN dbo.Planeacion_ProgramaProduccion pp ON pp.ProgramaProduccionID=rh.ProgramaProduccionID
    LEFT JOIN dbo.SolicitudesProduccion s ON s.SolicitudProduccionID=COALESCE(rh.SolicitudProduccionID,pp.SolicitudProduccionID)
    LEFT JOIN dbo.ERP_Maquinas m ON m.MaquinaID=COALESCE(rh.MaquinaID,pp.MaquinaID)
    LEFT JOIN dbo.Produccion_Ejecucion e ON e.EjecucionProduccionID=rh.EjecucionProduccionID
    LEFT JOIN dbo.Persona per ON per.PersonaID=rh.OperadorID
    OUTER APPLY
    (
        SELECT TOP(1) dt0.ObjetivoHora,dt0.Ciclo,dt0.Cavidades
        FROM dbo.ERP_ParteDatosTecnicos dt0
        WHERE dt0.ParteID=COALESCE(pp.ParteID,e.ParteID)
          AND dt0.Activo=1
        ORDER BY dt0.ParteDatoTecnicoID DESC
    ) dt
    CROSS APPLY
    (
        SELECT CONVERT(DECIMAL(18,2),CASE WHEN rh.HoraFin>=rh.HoraInicio
            THEN DATEDIFF(MINUTE,rh.HoraInicio,rh.HoraFin)
            ELSE 1440+DATEDIFF(MINUTE,rh.HoraInicio,rh.HoraFin) END) AS MinutosBloque
    ) minutos
    WHERE rh.Activo=1
      AND rh.FechaProduccion>=@Desde
      AND rh.FechaProduccion<@HastaExclusiva
),
R AS
(
    SELECT
        ProgramaProduccionID,
        MAX(EjecucionProduccionID) AS EjecucionProduccionID,
        MAX(SolicitudProduccionID) AS SolicitudProduccionID,
        MAX(NumeroOF) AS NumeroOF,
        MAX(Parte) AS Parte,
        MAX(DescripcionParte) AS DescripcionParte,
        MAX(Maquina) AS Maquina,
        MAX(ObjetivoHoraEstandar) AS ObjetivoHoraEstandar,
        MAX(CicloEstandar) AS CicloEstandar,
        MAX(CavidadesEstandar) AS CavidadesEstandar,
        MAX(FuenteEstandar) AS FuenteEstandar,
        COUNT(DISTINCT Turno) AS TurnosTrabajados,
        COUNT(DISTINCT Operador) AS OperadoresTrabajados,
        SUM(CONVERT(BIGINT,CantidadOK)) AS PiezasOK,
        SUM(CONVERT(BIGINT,CantidadSospechosa)) AS PiezasSospechosas,
        SUM(CONVERT(BIGINT,CantidadScrap)) AS PiezasScrap,
        SUM(ObjetivoBloque) AS Objetivo,
        SUM(MinutosBloque) AS MinutosProduccion
    FROM B GROUP BY ProgramaProduccionID
),
P AS
(
    SELECT ProgramaProduccionID,
        SUM(CONVERT(DECIMAL(18,2),CASE
            WHEN FechaFinParo IS NULL THEN CASE WHEN FechaInicioParo>=GETDATE() THEN 0 ELSE DATEDIFF(MINUTE,FechaInicioParo,GETDATE()) END
            WHEN DuracionMinutos IS NOT NULL THEN DuracionMinutos
            WHEN FechaFinParo>=FechaInicioParo THEN DATEDIFF(MINUTE,FechaInicioParo,FechaFinParo)
            ELSE 0 END)) AS MinutosParo
    FROM dbo.Produccion_Paros
    WHERE Activo=1
      AND FechaInicioParo>=@Desde
      AND FechaInicioParo<@HastaExclusiva
    GROUP BY ProgramaProduccionID
),
TurnoAgg AS
(
    SELECT ProgramaProduccionID,Turno,SUM(CONVERT(BIGINT,CantidadOK)) AS PiezasOK,SUM(ObjetivoBloque) AS Objetivo
    FROM B WHERE Turno IS NOT NULL GROUP BY ProgramaProduccionID,Turno
),
TurnoRank AS
(
    SELECT *,ROW_NUMBER() OVER(PARTITION BY ProgramaProduccionID ORDER BY
        CASE WHEN Objetivo>0 THEN CONVERT(DECIMAL(18,4),PiezasOK)*100/Objetivo ELSE 0 END DESC,
        PiezasOK DESC,Turno) AS rn FROM TurnoAgg WHERE Objetivo>0
),
OperadorAgg AS
(
    SELECT ProgramaProduccionID,Operador,SUM(CONVERT(BIGINT,CantidadOK)) AS PiezasOK,SUM(ObjetivoBloque) AS Objetivo
    FROM B GROUP BY ProgramaProduccionID,Operador
),
OperadorRank AS
(
    SELECT *,ROW_NUMBER() OVER(PARTITION BY ProgramaProduccionID ORDER BY
        CASE WHEN Objetivo>0 THEN CONVERT(DECIMAL(18,4),PiezasOK)*100/Objetivo ELSE 0 END DESC,
        PiezasOK DESC,Operador) AS rn FROM OperadorAgg WHERE Objetivo>0
)
SELECT TOP(60)
    r.*,
    ISNULL(p.MinutosParo,0) AS MinutosParo,
    ISNULL(bt.Turno,N'') AS MejorTurno,
    CONVERT(DECIMAL(18,2),CASE WHEN ISNULL(bt.Objetivo,0)>0 THEN CONVERT(DECIMAL(18,4),bt.PiezasOK)*100/bt.Objetivo ELSE 0 END) AS MejorTurnoRqtPct,
    ISNULL(bop.Operador,N'') AS MejorOperador,
    CONVERT(DECIMAL(18,2),CASE WHEN ISNULL(bop.Objetivo,0)>0 THEN CONVERT(DECIMAL(18,4),bop.PiezasOK)*100/bop.Objetivo ELSE 0 END) AS MejorOperadorRqtPct
FROM R r
LEFT JOIN P p ON p.ProgramaProduccionID=r.ProgramaProduccionID
LEFT JOIN TurnoRank bt ON bt.ProgramaProduccionID=r.ProgramaProduccionID AND bt.rn=1
LEFT JOIN OperadorRank bop ON bop.ProgramaProduccionID=r.ProgramaProduccionID AND bop.rn=1
ORDER BY r.NumeroOF DESC,r.ProgramaProduccionID DESC;";

        await using var command = new SqlCommand(sql, connection);
        AddPeriodo(command, vm.Desde, vm.Hasta);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            vm.ProgramasProduccion.Add(new IndicadoresProgramaProduccionVm
            {
                ProgramaProduccionID = Int(reader, "ProgramaProduccionID"),
                EjecucionProduccionID = Int(reader, "EjecucionProduccionID"),
                SolicitudProduccionID = reader["SolicitudProduccionID"] == DBNull.Value ? null : Convert.ToInt32(reader["SolicitudProduccionID"]),
                NumeroOF = Text(reader, "NumeroOF"),
                Parte = Text(reader, "Parte"),
                DescripcionParte = Text(reader, "DescripcionParte"),
                Maquina = Text(reader, "Maquina"),
                ObjetivoHoraEstandar = Int(reader, "ObjetivoHoraEstandar"),
                CicloEstandar = Text(reader, "CicloEstandar"),
                CavidadesEstandar = reader["CavidadesEstandar"] == DBNull.Value ? null : Convert.ToInt32(reader["CavidadesEstandar"]),
                FuenteEstandar = Text(reader, "FuenteEstandar"),
                PiezasOK = Long(reader, "PiezasOK"),
                PiezasSospechosas = Long(reader, "PiezasSospechosas"),
                PiezasScrap = Long(reader, "PiezasScrap"),
                Objetivo = Long(reader, "Objetivo"),
                MinutosProduccion = Decimal(reader, "MinutosProduccion"),
                MinutosParo = Decimal(reader, "MinutosParo"),
                TurnosTrabajados = Int(reader, "TurnosTrabajados"),
                OperadoresTrabajados = Int(reader, "OperadoresTrabajados"),
                MejorTurno = Text(reader, "MejorTurno"),
                MejorTurnoRqtPct = Decimal(reader, "MejorTurnoRqtPct"),
                MejorOperador = Text(reader, "MejorOperador"),
                MejorOperadorRqtPct = Decimal(reader, "MejorOperadorRqtPct")
            });
        }
    }

    // NSQ_INDICADORES_PAROS_MAQUINA_V1_4
    private static async Task CargarParosProduccionAsync(
        SqlConnection connection,
        IndicadoresDashboardVm vm,
        CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT TOP(12)
    COALESCE(NULLIF(LTRIM(RTRIM(MotivoParoTexto)),N''),N'Sin motivo') AS Motivo,
    COUNT(*) AS Eventos,
    SUM(CONVERT(DECIMAL(18,2),CASE
        WHEN FechaFinParo IS NULL THEN CASE WHEN FechaInicioParo>=GETDATE() THEN 0 ELSE DATEDIFF(MINUTE,FechaInicioParo,GETDATE()) END
        WHEN DuracionMinutos IS NOT NULL THEN DuracionMinutos
        WHEN FechaFinParo>=FechaInicioParo THEN DATEDIFF(MINUTE,FechaInicioParo,FechaFinParo)
        ELSE 0 END)) AS Minutos
FROM dbo.Produccion_Paros
WHERE Activo=1
  AND FechaInicioParo>=@Desde
  AND FechaInicioParo<@HastaExclusiva
GROUP BY COALESCE(NULLIF(LTRIM(RTRIM(MotivoParoTexto)),N''),N'Sin motivo')
ORDER BY Minutos DESC,Eventos DESC;

WITH P AS
(
    SELECT
        COALESCE(p.MaquinaID,e.MaquinaID,pp.MaquinaID) AS MaquinaID,
        COUNT(*) AS Eventos,
        SUM(CONVERT(DECIMAL(18,2),CASE
            WHEN p.FechaFinParo IS NULL THEN CASE WHEN p.FechaInicioParo>=GETDATE() THEN 0 ELSE DATEDIFF(MINUTE,p.FechaInicioParo,GETDATE()) END
            WHEN p.DuracionMinutos IS NOT NULL THEN p.DuracionMinutos
            WHEN p.FechaFinParo>=p.FechaInicioParo THEN DATEDIFF(MINUTE,p.FechaInicioParo,p.FechaFinParo)
            ELSE 0 END)) AS Minutos
    FROM dbo.Produccion_Paros p
    LEFT JOIN dbo.Produccion_Ejecucion e
        ON e.EjecucionProduccionID=p.EjecucionProduccionID
    LEFT JOIN dbo.Planeacion_ProgramaProduccion pp
        ON pp.ProgramaProduccionID=p.ProgramaProduccionID
    WHERE p.Activo=1
      AND p.FechaInicioParo>=@Desde
      AND p.FechaInicioParo<@HastaExclusiva
    GROUP BY COALESCE(p.MaquinaID,e.MaquinaID,pp.MaquinaID)
),
R AS
(
    SELECT
        rh.MaquinaID,
        SUM(CONVERT(DECIMAL(18,2),CASE
            WHEN rh.HoraFin>=rh.HoraInicio THEN DATEDIFF(MINUTE,rh.HoraInicio,rh.HoraFin)
            ELSE 1440+DATEDIFF(MINUTE,rh.HoraInicio,rh.HoraFin)
        END)) AS MinutosProduccion
    FROM dbo.Produccion_RegistroHora rh
    WHERE rh.Activo=1
      AND rh.MaquinaID IS NOT NULL
      AND rh.FechaProduccion>=@Desde
      AND rh.FechaProduccion<@HastaExclusiva
    GROUP BY rh.MaquinaID
)
SELECT
    m.MaquinaID,
    ISNULL(NULLIF(LTRIM(RTRIM(m.Codigo)),N''),CONCAT(N'Maquina #',m.MaquinaID)) AS Maquina,
    ISNULL(m.Nombre,N'') AS Nombre,
    ISNULL(p.Eventos,0) AS Eventos,
    ISNULL(p.Minutos,0) AS Minutos,
    ISNULL(r.MinutosProduccion,0) AS MinutosProduccion
FROM dbo.ERP_Maquinas m
LEFT JOIN P p ON p.MaquinaID=m.MaquinaID
LEFT JOIN R r ON r.MaquinaID=m.MaquinaID
WHERE ISNULL(m.Activo,1)=1
ORDER BY
    CASE WHEN ISNULL(p.Minutos,0)>0 THEN 0 ELSE 1 END,
    ISNULL(p.Minutos,0) DESC,
    m.Codigo,
    m.MaquinaID;";

        await using var command = new SqlCommand(sql, connection);
        AddPeriodo(command, vm.Desde, vm.Hasta);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var minutos = Decimal(reader, "Minutos");
            vm.ParosMotivos.Add(new IndicadoresParoMotivoVm
            {
                Motivo = Text(reader, "Motivo"),
                Eventos = Int(reader, "Eventos"),
                Minutos = minutos,
                PorcentajeParo = vm.Produccion.MinutosParo <= 0m
                    ? 0m
                    : Math.Clamp(minutos * 100m / vm.Produccion.MinutosParo,0m,100m)
            });
        }

        if (await reader.NextResultAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var minutos = Decimal(reader, "Minutos");
                vm.ParosMaquinas.Add(new IndicadoresParoMaquinaVm
                {
                    MaquinaID = Int(reader, "MaquinaID"),
                    Maquina = Text(reader, "Maquina"),
                    Nombre = Text(reader, "Nombre"),
                    Eventos = Int(reader, "Eventos"),
                    Minutos = minutos,
                    MinutosProduccion = Decimal(reader, "MinutosProduccion"),
                    PorcentajeParoTotal = vm.Produccion.MinutosParo <= 0m
                        ? 0m
                        : Math.Clamp(minutos * 100m / vm.Produccion.MinutosParo,0m,100m)
                });
            }
        }
    }
    // NSQ_INDICADORES_PERSONAL_V2
    // Personal real confirmado en Produccion_Ejecucion; no usa la sugerencia de Programacion de Personal como resultado.
    private static async Task CargarPersonalApoyoProduccionAsync(
        SqlConnection connection,
        IndicadoresDashboardVm vm,
        CancellationToken cancellationToken)
    {
        const string sql = @"
WITH B AS
(
    SELECT
        rol.TipoRol,
        CASE
            WHEN rol.PersonaID IS NOT NULL THEN CONCAT(N'ID:',rol.PersonaID)
            ELSE CONCAT(N'N:',UPPER(LTRIM(RTRIM(ISNULL(rol.NombreSnapshot,N'')))))
        END AS PersonaKey,
        rol.PersonaID,
        COALESCE
        (
            NULLIF(LTRIM(RTRIM(CONCAT(ISNULL(per.Nombre,N''),N' ',ISNULL(per.ApellidoPaterno,N''),N' ',ISNULL(per.ApellidoMaterno,N'')))),N''),
            NULLIF(LTRIM(RTRIM(rol.NombreSnapshot)),N''),
            N'Sin nombre'
        ) AS Nombre,
        ISNULL(per.NumeroControl,N'') AS NumeroControl,
        ISNULL(rh.CantidadOK,0) AS CantidadOK,
        ISNULL(rh.CantidadSospechosa,0) AS CantidadSospechosa,
        ISNULL(rh.CantidadScrap,0) AS CantidadScrap,
        minutos.MinutosBloque,
        CONVERT(BIGINT,ROUND(CONVERT(DECIMAL(18,4),COALESCE(NULLIF(pp.ObjetivoHora,0),NULLIF(dt.ObjetivoHora,0),0))*minutos.MinutosBloque/60.0,0)) AS ObjetivoBloque,
        COALESCE(NULLIF(LTRIM(RTRIM(pp.NumeroParte)),N''),NULLIF(LTRIM(RTRIM(e.NumeroParte)),N''),N'Sin parte') AS NumeroParte,
        COALESCE(NULLIF(LTRIM(RTRIM(pp.DesignacionDescripcionSAP)),N''),NULLIF(LTRIM(RTRIM(e.DescripcionParte)),N''),N'') AS DescripcionParte,
        NULLIF(LTRIM(RTRIM(rh.Turno)),N'') AS Turno,
        COALESCE(NULLIF(LTRIM(RTRIM(m.Codigo)),N''),NULLIF(LTRIM(RTRIM(e.MaquinaCodigo)),N''),NULLIF(LTRIM(RTRIM(pp.MaquinaCodigo)),N''),N'Sin máquina') AS Maquina,
        COALESCE(NULLIF(LTRIM(RTRIM(s.NumeroOFRecibida)),N''),NULLIF(LTRIM(RTRIM(s.FolioSolicitud)),N''),CONCAT(N'Programa ',rh.ProgramaProduccionID)) AS NumeroOF
    FROM dbo.Produccion_RegistroHora rh
    INNER JOIN dbo.Produccion_Ejecucion e
        ON e.EjecucionProduccionID=rh.EjecucionProduccionID
    LEFT JOIN dbo.Planeacion_ProgramaProduccion pp
        ON pp.ProgramaProduccionID=rh.ProgramaProduccionID
    LEFT JOIN dbo.SolicitudesProduccion s
        ON s.SolicitudProduccionID=COALESCE(rh.SolicitudProduccionID,e.SolicitudProduccionID,pp.SolicitudProduccionID)
    LEFT JOIN dbo.ERP_Maquinas m
        ON m.MaquinaID=COALESCE(rh.MaquinaID,e.MaquinaID,pp.MaquinaID)
    CROSS APPLY
    (
        VALUES
            (N'AUXILIAR',e.OperadorAuxiliarID,e.OperadorAuxiliarNombre),
            (N'TECNICO',e.TecnicoProduccionID,e.TecnicoProduccionNombre)
    ) rol(TipoRol,PersonaID,NombreSnapshot)
    LEFT JOIN dbo.Persona per
        ON per.PersonaID=rol.PersonaID
    OUTER APPLY
    (
        SELECT TOP(1) dt0.ObjetivoHora
        FROM dbo.ERP_ParteDatosTecnicos dt0
        WHERE dt0.ParteID=COALESCE(pp.ParteID,e.ParteID)
          AND dt0.Activo=1
        ORDER BY dt0.ParteDatoTecnicoID DESC
    ) dt
    CROSS APPLY
    (
        SELECT CONVERT(DECIMAL(18,2),CASE WHEN rh.HoraFin>=rh.HoraInicio
            THEN DATEDIFF(MINUTE,rh.HoraInicio,rh.HoraFin)
            ELSE 1440+DATEDIFF(MINUTE,rh.HoraInicio,rh.HoraFin) END) AS MinutosBloque
    ) minutos
    WHERE rh.Activo=1
      AND rh.FechaProduccion>=@Desde
      AND rh.FechaProduccion<@HastaExclusiva
      AND
      (
          rol.PersonaID IS NOT NULL
          OR NULLIF(LTRIM(RTRIM(rol.NombreSnapshot)),N'') IS NOT NULL
      )
),
R AS
(
    SELECT
        TipoRol,PersonaKey,
        MAX(PersonaID) AS PersonaID,
        MAX(Nombre) AS Nombre,
        MAX(NumeroControl) AS NumeroControl,
        COUNT(*) AS Registros,
        COUNT(DISTINCT NumeroParte) AS PartesTrabajadas,
        COUNT(DISTINCT Turno) AS TurnosTrabajados,
        COUNT(DISTINCT Maquina) AS MaquinasTrabajadas,
        COUNT(DISTINCT NumeroOF) AS OfTrabajadas,
        SUM(CONVERT(BIGINT,CantidadOK)) AS PiezasOK,
        SUM(CONVERT(BIGINT,CantidadSospechosa)) AS PiezasSospechosas,
        SUM(CONVERT(BIGINT,CantidadScrap)) AS PiezasScrap,
        SUM(ObjetivoBloque) AS Objetivo,
        SUM(MinutosBloque) AS MinutosProduccion
    FROM B
    GROUP BY TipoRol,PersonaKey
),
P AS
(
    SELECT
        rol.TipoRol,
        CASE
            WHEN rol.PersonaID IS NOT NULL THEN CONCAT(N'ID:',rol.PersonaID)
            ELSE CONCAT(N'N:',UPPER(LTRIM(RTRIM(ISNULL(rol.NombreSnapshot,N'')))))
        END AS PersonaKey,
        SUM(CONVERT(DECIMAL(18,2),CASE
            WHEN p.FechaFinParo IS NULL THEN CASE WHEN p.FechaInicioParo>=GETDATE() THEN 0 ELSE DATEDIFF(MINUTE,p.FechaInicioParo,GETDATE()) END
            WHEN p.DuracionMinutos IS NOT NULL THEN p.DuracionMinutos
            WHEN p.FechaFinParo>=p.FechaInicioParo THEN DATEDIFF(MINUTE,p.FechaInicioParo,p.FechaFinParo)
            ELSE 0 END)) AS MinutosParo
    FROM dbo.Produccion_Paros p
    INNER JOIN dbo.Produccion_Ejecucion e
        ON e.EjecucionProduccionID=p.EjecucionProduccionID
    CROSS APPLY
    (
        VALUES
            (N'AUXILIAR',e.OperadorAuxiliarID,e.OperadorAuxiliarNombre),
            (N'TECNICO',e.TecnicoProduccionID,e.TecnicoProduccionNombre)
    ) rol(TipoRol,PersonaID,NombreSnapshot)
    WHERE p.Activo=1
      AND p.FechaInicioParo>=@Desde
      AND p.FechaInicioParo<@HastaExclusiva
      AND
      (
          rol.PersonaID IS NOT NULL
          OR NULLIF(LTRIM(RTRIM(rol.NombreSnapshot)),N'') IS NOT NULL
      )
    GROUP BY
        rol.TipoRol,
        CASE
            WHEN rol.PersonaID IS NOT NULL THEN CONCAT(N'ID:',rol.PersonaID)
            ELSE CONCAT(N'N:',UPPER(LTRIM(RTRIM(ISNULL(rol.NombreSnapshot,N'')))))
        END
),
ParteAgg AS
(
    SELECT TipoRol,PersonaKey,NumeroParte,MAX(DescripcionParte) AS DescripcionParte,
           SUM(CONVERT(BIGINT,CantidadOK)) AS PiezasOK,SUM(ObjetivoBloque) AS Objetivo
    FROM B GROUP BY TipoRol,PersonaKey,NumeroParte
),
ParteRank AS
(
    SELECT *,ROW_NUMBER() OVER(PARTITION BY TipoRol,PersonaKey ORDER BY
        CASE WHEN Objetivo>0 THEN CONVERT(DECIMAL(18,4),PiezasOK)*100/Objetivo ELSE 0 END DESC,
        PiezasOK DESC,NumeroParte) AS rn FROM ParteAgg WHERE Objetivo>0
),
TurnoAgg AS
(
    SELECT TipoRol,PersonaKey,Turno,SUM(CONVERT(BIGINT,CantidadOK)) AS PiezasOK,SUM(ObjetivoBloque) AS Objetivo
    FROM B WHERE Turno IS NOT NULL GROUP BY TipoRol,PersonaKey,Turno
),
TurnoRank AS
(
    SELECT *,ROW_NUMBER() OVER(PARTITION BY TipoRol,PersonaKey ORDER BY
        CASE WHEN Objetivo>0 THEN CONVERT(DECIMAL(18,4),PiezasOK)*100/Objetivo ELSE 0 END DESC,
        PiezasOK DESC,Turno) AS rn FROM TurnoAgg WHERE Objetivo>0
),
MaquinaAgg AS
(
    SELECT TipoRol,PersonaKey,Maquina,SUM(CONVERT(BIGINT,CantidadOK)) AS PiezasOK,SUM(ObjetivoBloque) AS Objetivo
    FROM B GROUP BY TipoRol,PersonaKey,Maquina
),
MaquinaRank AS
(
    SELECT *,ROW_NUMBER() OVER(PARTITION BY TipoRol,PersonaKey ORDER BY
        CASE WHEN Objetivo>0 THEN CONVERT(DECIMAL(18,4),PiezasOK)*100/Objetivo ELSE 0 END DESC,
        PiezasOK DESC,Maquina) AS rn FROM MaquinaAgg WHERE Objetivo>0
),
OfAgg AS
(
    SELECT TipoRol,PersonaKey,NumeroOF,SUM(CONVERT(BIGINT,CantidadOK)) AS PiezasOK,SUM(ObjetivoBloque) AS Objetivo
    FROM B GROUP BY TipoRol,PersonaKey,NumeroOF
),
OfRank AS
(
    SELECT *,ROW_NUMBER() OVER(PARTITION BY TipoRol,PersonaKey ORDER BY
        CASE WHEN Objetivo>0 THEN CONVERT(DECIMAL(18,4),PiezasOK)*100/Objetivo ELSE 0 END DESC,
        PiezasOK DESC,NumeroOF) AS rn FROM OfAgg WHERE Objetivo>0
)
SELECT
    r.TipoRol,r.PersonaID,r.Nombre,r.NumeroControl,
    r.PiezasOK,r.PiezasSospechosas,r.PiezasScrap,r.Objetivo,r.MinutosProduccion,
    ISNULL(p.MinutosParo,0) AS MinutosParo,r.Registros,
    r.PartesTrabajadas,r.TurnosTrabajados,r.MaquinasTrabajadas,r.OfTrabajadas,
    ISNULL(bp.NumeroParte,N'') AS MejorParte,
    ISNULL(bp.DescripcionParte,N'') AS MejorParteDescripcion,
    ISNULL(bp.PiezasOK,0) AS MejorParteOK,
    CONVERT(DECIMAL(18,2),CASE WHEN ISNULL(bp.Objetivo,0)>0 THEN CONVERT(DECIMAL(18,4),bp.PiezasOK)*100/bp.Objetivo ELSE 0 END) AS MejorParteRqtPct,
    ISNULL(bt.Turno,N'') AS MejorTurno,
    ISNULL(bt.PiezasOK,0) AS MejorTurnoOK,
    CONVERT(DECIMAL(18,2),CASE WHEN ISNULL(bt.Objetivo,0)>0 THEN CONVERT(DECIMAL(18,4),bt.PiezasOK)*100/bt.Objetivo ELSE 0 END) AS MejorTurnoRqtPct,
    ISNULL(bm.Maquina,N'') AS MejorMaquina,
    CONVERT(DECIMAL(18,2),CASE WHEN ISNULL(bm.Objetivo,0)>0 THEN CONVERT(DECIMAL(18,4),bm.PiezasOK)*100/bm.Objetivo ELSE 0 END) AS MejorMaquinaRqtPct,
    ISNULL(bo.NumeroOF,N'') AS MejorOF,
    CONVERT(DECIMAL(18,2),CASE WHEN ISNULL(bo.Objetivo,0)>0 THEN CONVERT(DECIMAL(18,4),bo.PiezasOK)*100/bo.Objetivo ELSE 0 END) AS MejorOFRqtPct
FROM R r
LEFT JOIN P p ON p.TipoRol=r.TipoRol AND p.PersonaKey=r.PersonaKey
LEFT JOIN ParteRank bp ON bp.TipoRol=r.TipoRol AND bp.PersonaKey=r.PersonaKey AND bp.rn=1
LEFT JOIN TurnoRank bt ON bt.TipoRol=r.TipoRol AND bt.PersonaKey=r.PersonaKey AND bt.rn=1
LEFT JOIN MaquinaRank bm ON bm.TipoRol=r.TipoRol AND bm.PersonaKey=r.PersonaKey AND bm.rn=1
LEFT JOIN OfRank bo ON bo.TipoRol=r.TipoRol AND bo.PersonaKey=r.PersonaKey AND bo.rn=1
ORDER BY r.TipoRol,r.PiezasOK DESC,r.Nombre;";

        await using var command = new SqlCommand(sql, connection);
        AddPeriodo(command, vm.Desde, vm.Hasta);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var item = new IndicadoresPersonalApoyoKpiVm
            {
                Rol = Text(reader, "TipoRol"),
                PersonaID = reader["PersonaID"] == DBNull.Value ? null : Convert.ToInt32(reader["PersonaID"]),
                Nombre = Text(reader, "Nombre"),
                NumeroControl = Text(reader, "NumeroControl"),
                PiezasOK = Long(reader, "PiezasOK"),
                PiezasSospechosas = Long(reader, "PiezasSospechosas"),
                PiezasScrap = Long(reader, "PiezasScrap"),
                Objetivo = Long(reader, "Objetivo"),
                MinutosProduccion = Decimal(reader, "MinutosProduccion"),
                MinutosParo = Decimal(reader, "MinutosParo"),
                Registros = Int(reader, "Registros"),
                PartesTrabajadas = Int(reader, "PartesTrabajadas"),
                TurnosTrabajados = Int(reader, "TurnosTrabajados"),
                MaquinasTrabajadas = Int(reader, "MaquinasTrabajadas"),
                OfTrabajadas = Int(reader, "OfTrabajadas"),
                MejorParte = Text(reader, "MejorParte"),
                MejorParteDescripcion = Text(reader, "MejorParteDescripcion"),
                MejorParteOK = Long(reader, "MejorParteOK"),
                MejorParteRqtPct = Decimal(reader, "MejorParteRqtPct"),
                MejorTurno = Text(reader, "MejorTurno"),
                MejorTurnoOK = Long(reader, "MejorTurnoOK"),
                MejorTurnoRqtPct = Decimal(reader, "MejorTurnoRqtPct"),
                MejorMaquina = Text(reader, "MejorMaquina"),
                MejorMaquinaRqtPct = Decimal(reader, "MejorMaquinaRqtPct"),
                MejorOF = Text(reader, "MejorOF"),
                MejorOFRqtPct = Decimal(reader, "MejorOFRqtPct")
            };

            if (item.Rol.Equals("TECNICO", StringComparison.OrdinalIgnoreCase))
                vm.Tecnicos.Add(item);
            else if (item.Rol.Equals("AUXILIAR", StringComparison.OrdinalIgnoreCase))
                vm.Auxiliares.Add(item);
        }

        vm.Tecnicos = vm.Tecnicos
            .OrderByDescending(x => x.OeePct)
            .ThenByDescending(x => x.PiezasOK)
            .Take(20)
            .ToList();

        vm.Auxiliares = vm.Auxiliares
            .OrderByDescending(x => x.OeePct)
            .ThenByDescending(x => x.PiezasOK)
            .Take(20)
            .ToList();
    }

    private static async Task CargarDepartamentosAsync(
        SqlConnection connection,
        IndicadoresDashboardVm vm,
        CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT
    COUNT(*) AS Programas,
    ISNULL(SUM(CONVERT(BIGINT,ISNULL(CantidadProgramada,0))),0) AS Programado,
    ISNULL(SUM(CONVERT(BIGINT,ISNULL(CantidadProducida,0))),0) AS Producido,
    ISNULL(SUM(CONVERT(BIGINT,CASE WHEN CantidadProgramada>ISNULL(CantidadProducida,0)
        THEN CantidadProgramada-ISNULL(CantidadProducida,0) ELSE 0 END)),0) AS Pendiente,
    SUM(CASE WHEN ISNULL(CantidadProgramada,0)>ISNULL(CantidadProducida,0) THEN 1 ELSE 0 END) AS ConPendiente,
    SUM(CASE WHEN FechaInicioReal IS NOT NULL THEN 1 ELSE 0 END) AS ArranquesRegistrados,
    SUM(CASE WHEN FechaInicioReal IS NOT NULL AND FechaInicioProgramada IS NOT NULL
                  AND FechaInicioReal<=FechaInicioProgramada THEN 1 ELSE 0 END) AS ArranquesATiempo
FROM dbo.Planeacion_ProgramaProduccion
WHERE Activo=1
  AND FechaInicioProgramada>=@Desde
  AND FechaInicioProgramada<@HastaExclusiva;

SELECT COUNT(DISTINCT ProgramaProduccionID) AS Reprogramados
FROM dbo.Planeacion_ProgramaReprogramacionHistorial
WHERE FechaCambio>=@Desde
  AND FechaCambio<@HastaExclusiva;

SELECT
    COUNT(*) AS Inspecciones,
    SUM(CASE WHEN Liberado=1 THEN 1 ELSE 0 END) AS Liberadas,
    SUM(CASE WHEN EnContencion=1 THEN 1 ELSE 0 END) AS Contenciones,
    SUM(CASE WHEN EsScrap=1 THEN 1 ELSE 0 END) AS Scrap,
    ISNULL(SUM(CantidadTotal),0) AS CantidadTotal,
    ISNULL(SUM(CantidadRevisada),0) AS CantidadRevisada,
    ISNULL(SUM(CantidadPendiente),0) AS CantidadPendiente,
    SUM(CASE WHEN RequiereGP12=1 THEN 1 ELSE 0 END) AS RequierenGP12,
    SUM(CASE WHEN RequiereReliberacion=1 THEN 1 ELSE 0 END) AS RequierenReliberacion,
    SUM(CASE WHEN CumplioTiempoObjetivoInicial=1 THEN 1 ELSE 0 END) AS CumplieronTiempoObjetivo,
    SUM(CASE WHEN CumplioTiempoObjetivoInicial IS NOT NULL THEN 1 ELSE 0 END) AS LiberacionesConTiempo,
    AVG(CONVERT(DECIMAL(18,2),NULLIF(MinutosLiberacionInicial,0))) AS MinutosLiberacionPromedio
FROM dbo.Calidad_Inspecciones
WHERE FechaCreacion>=@Desde
  AND FechaCreacion<@HastaExclusiva;

SELECT
    COUNT(*) AS Solicitudes,
    ISNULL(SUM(CantidadSolicitada),0) AS Solicitado,
    ISNULL(SUM(CantidadProcesada),0) AS Procesado,
    ISNULL(SUM(CantidadPendiente),0) AS Pendiente
FROM dbo.GP12_Solicitudes
WHERE Activo=1
  AND FechaSolicitud>=@Desde
  AND FechaSolicitud<@HastaExclusiva;

SELECT COUNT(*) AS SolicitudesPendientes
FROM dbo.GP12_Solicitudes
WHERE Activo=1
  AND CantidadPendiente>0;

SELECT
    COUNT(*) AS Inspecciones,
    ISNULL(SUM(CantidadRevisada),0) AS Revisado,
    ISNULL(SUM(CantidadOK),0) AS OK,
    ISNULL(SUM(CantidadNOK),0) AS NOK,
    ISNULL(SUM(CantidadRetrabajada),0) AS Retrabajado,
    ISNULL(SUM(CantidadScrap),0) AS Scrap
FROM dbo.GP12_Inspecciones
WHERE Activo=1
  AND FechaCreacion>=@Desde
  AND FechaCreacion<@HastaExclusiva;

SELECT
    (SELECT COUNT(*) FROM dbo.AlmacenMP_Movimientos
        WHERE Activo=1 AND FechaMovimiento>=@Desde AND FechaMovimiento<@HastaExclusiva) AS MovimientosMP,
    (SELECT COUNT(*) FROM dbo.AlmacenPT_Movimientos
        WHERE Activo=1 AND FechaMovimiento>=@Desde AND FechaMovimiento<@HastaExclusiva) AS MovimientosPT,
    (SELECT COUNT(*) FROM dbo.AlmacenEmbalajes_Movimientos
        WHERE Activo=1 AND FechaMovimiento>=@Desde AND FechaMovimiento<@HastaExclusiva) AS MovimientosEmbalajes,
    (SELECT COUNT(*) FROM dbo.AlmacenMP_Movimientos
        WHERE Activo=1 AND FechaMovimiento>=@Desde AND FechaMovimiento<@HastaExclusiva
          AND UPPER(LTRIM(RTRIM(TipoMovimiento))) IN(N'ENTRADA',N'RETORNO',N'AJUSTE',N'AJUSTE POSITIVO',N'AJUSTEPOSITIVO')) AS EntradasMP,
    (SELECT COUNT(*) FROM dbo.AlmacenMP_Movimientos
        WHERE Activo=1 AND FechaMovimiento>=@Desde AND FechaMovimiento<@HastaExclusiva
          AND UPPER(LTRIM(RTRIM(TipoMovimiento))) IN(N'SALIDA',N'CONSUMO',N'SCRAP',N'AJUSTE NEGATIVO',N'AJUSTENEGATIVO')) AS SalidasMP,
    (SELECT COUNT(*) FROM dbo.AlmacenPT_Movimientos
        WHERE Activo=1 AND FechaMovimiento>=@Desde AND FechaMovimiento<@HastaExclusiva
          AND UPPER(LTRIM(RTRIM(TipoMovimiento))) IN(N'ENTRADA',N'RETORNO',N'AJUSTE',N'AJUSTE POSITIVO',N'AJUSTEPOSITIVO')) AS EntradasPT,
    (SELECT COUNT(*) FROM dbo.AlmacenPT_Movimientos
        WHERE Activo=1 AND FechaMovimiento>=@Desde AND FechaMovimiento<@HastaExclusiva
          AND UPPER(LTRIM(RTRIM(TipoMovimiento))) IN(N'SALIDA',N'CONSUMO',N'EMBARQUE',N'SCRAP',N'AJUSTE NEGATIVO',N'AJUSTENEGATIVO')) AS SalidasPT,
    (SELECT COUNT(*) FROM dbo.Calidad_ScrapEntregas
        WHERE Activo=1 AND Estado=N'PENDIENTE_RECEPCION') AS ScrapPendienteRecepcion,
    (SELECT COUNT(*) FROM dbo.Calidad_ScrapEntregas
        WHERE Activo=1 AND Estado IN(N'RECIBIDO_ALMACEN',N'PENDIENTE_MOLIENDA')) AS ScrapPendienteMolienda,
    (SELECT COUNT(*) FROM dbo.vw_AlmacenMPInventario
        WHERE StockMinimo>0 AND Disponible<=StockMinimo) AS MPBajoMinimo,
    (SELECT COUNT(*) FROM dbo.vw_AlmacenPTInventario
        WHERE Semaforo=N'ROJO') AS PTEnRojo,
    (SELECT COUNT(*) FROM dbo.vw_AlmacenEmbalajesInventario
        WHERE Semaforo=N'ROJO') AS EmbalajesEnRojo;

SELECT
    COUNT(*) AS Embarques,
    SUM(CASE WHEN FechaEntrega IS NOT NULL THEN 1 ELSE 0 END) AS Entregados,
    SUM(CASE WHEN FechaEntrega IS NOT NULL
                  AND CAST(FechaEntrega AS date)<=ISNULL(FechaEntregaProgramada,FechaProgramada)
             THEN 1 ELSE 0 END) AS EntregadosATiempo,
    SUM(CASE WHEN TieneIncidencia=1 THEN 1 ELSE 0 END) AS Incidencias,
    SUM(CASE WHEN ISNULL(FechaEntregaProgramada,FechaProgramada)<CAST(GETDATE() AS date)
                  AND FechaEntrega IS NULL
                  AND UPPER(ISNULL(Estatus,N''))<>N'CANCELADO'
             THEN 1 ELSE 0 END) AS Atrasados
FROM dbo.Logistica_Embarques
WHERE Activo=1
  AND FechaProgramada>=@Desde
  AND FechaProgramada<@HastaExclusiva;

SELECT
    SUM(CASE WHEN UPPER(ISNULL(Estatus,N'')) NOT IN(N'CERRADA',N'CERRADO',N'RESUELTA',N'RESUELTO',N'CANCELADA',N'CANCELADO')
             THEN 1 ELSE 0 END) AS IncidenciasAbiertas,
    SUM(CASE WHEN UPPER(ISNULL(Estatus,N'')) NOT IN(N'CERRADA',N'CERRADO',N'RESUELTA',N'RESUELTO',N'CANCELADA',N'CANCELADO')
                  AND (UPPER(ISNULL(Severidad,N'')) LIKE N'CRIT%' OR UPPER(ISNULL(Severidad,N''))=N'ALTA')
             THEN 1 ELSE 0 END) AS CriticasAbiertas
FROM dbo.Logistica_Incidencias
WHERE Activo=1;

SELECT
    (SELECT COUNT(*) FROM dbo.ComprasSolicitudes
        WHERE Activo=1 AND FechaSolicitud>=@Desde AND FechaSolicitud<@HastaExclusiva) AS Solicitudes,
    (SELECT COUNT(*) FROM dbo.vw_ComprasSolicitudes_Flujo WHERE EsFinal=0) AS PendientesFlujo,
    (SELECT COUNT(*) FROM dbo.vw_ComprasSolicitudes_Flujo
        WHERE EsFinal=0 AND UPPER(ISNULL(Prioridad,N'')) IN(N'URGENTE',N'CRITICA',N'CRÍTICA',N'ALTA')) AS UrgentesPendientes,
    (SELECT COUNT(*) FROM dbo.ComprasOrdenes
        WHERE Activo=1 AND FechaOrden>=@Desde AND FechaOrden<@HastaExclusiva) AS OrdenesCompra,
    (SELECT ISNULL(SUM(ISNULL(Total,0)),0) FROM dbo.ComprasOrdenes
        WHERE Activo=1 AND FechaOrden>=@Desde AND FechaOrden<@HastaExclusiva) AS MontoOrdenes,
    (SELECT ISNULL(AVG(CONVERT(DECIMAL(18,2),DiasEnEstatus)),0)
        FROM dbo.vw_ComprasSolicitudes_Flujo WHERE EsFinal=0) AS PromedioDiasEnEstatus;

SELECT
    COUNT(*) AS Recepciones,
    SUM(CASE WHEN o.FechaEntregaEstimada IS NOT NULL
                  AND r.FechaRecepcion<=o.FechaEntregaEstimada THEN 1 ELSE 0 END) AS RecepcionesATiempo
FROM dbo.ComprasRecepciones r
INNER JOIN dbo.ComprasOrdenes o ON o.OrdenCompraID=r.OrdenCompraID
WHERE r.Activo=1
  AND r.FechaRecepcion>=@Desde
  AND r.FechaRecepcion<@HastaExclusiva;";

        await using var command = new SqlCommand(sql, connection);
        AddPeriodo(command, vm.Desde, vm.Hasta);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (await reader.ReadAsync(cancellationToken))
        {
            vm.Planeacion.Programas = Int(reader, "Programas");
            vm.Planeacion.Programado = Long(reader, "Programado");
            vm.Planeacion.Producido = Long(reader, "Producido");
            vm.Planeacion.Pendiente = Long(reader, "Pendiente");
            vm.Planeacion.ProgramasConPendiente = Int(reader, "ConPendiente");
            vm.Planeacion.ArranquesRegistrados = Int(reader, "ArranquesRegistrados");
            vm.Planeacion.ArranquesATiempo = Int(reader, "ArranquesATiempo");
        }

        if (await reader.NextResultAsync(cancellationToken) && await reader.ReadAsync(cancellationToken))
            vm.Planeacion.Reprogramados = Int(reader, "Reprogramados");

        if (await reader.NextResultAsync(cancellationToken) && await reader.ReadAsync(cancellationToken))
        {
            vm.Calidad.Inspecciones = Int(reader, "Inspecciones");
            vm.Calidad.Liberadas = Int(reader, "Liberadas");
            vm.Calidad.Contenciones = Int(reader, "Contenciones");
            vm.Calidad.Scrap = Int(reader, "Scrap");
            vm.Calidad.CantidadTotal = Decimal(reader, "CantidadTotal");
            vm.Calidad.CantidadRevisada = Decimal(reader, "CantidadRevisada");
            vm.Calidad.CantidadPendiente = Decimal(reader, "CantidadPendiente");
            vm.Calidad.RequierenGP12 = Int(reader, "RequierenGP12");
            vm.Calidad.RequierenReliberacion = Int(reader, "RequierenReliberacion");
            vm.Calidad.CumplieronTiempoObjetivo = Int(reader, "CumplieronTiempoObjetivo");
            vm.Calidad.LiberacionesConTiempo = Int(reader, "LiberacionesConTiempo");
            vm.Calidad.MinutosLiberacionPromedio = Decimal(reader, "MinutosLiberacionPromedio");
        }

        if (await reader.NextResultAsync(cancellationToken) && await reader.ReadAsync(cancellationToken))
        {
            vm.GP12.Solicitudes = Int(reader, "Solicitudes");
            vm.GP12.Solicitado = Decimal(reader, "Solicitado");
            vm.GP12.Procesado = Decimal(reader, "Procesado");
            vm.GP12.Pendiente = Decimal(reader, "Pendiente");
        }

        if (await reader.NextResultAsync(cancellationToken) && await reader.ReadAsync(cancellationToken))
            vm.GP12.SolicitudesPendientes = Int(reader, "SolicitudesPendientes");

        if (await reader.NextResultAsync(cancellationToken) && await reader.ReadAsync(cancellationToken))
        {
            vm.GP12.Inspecciones = Int(reader, "Inspecciones");
            vm.GP12.Revisado = Decimal(reader, "Revisado");
            vm.GP12.OK = Decimal(reader, "OK");
            vm.GP12.NOK = Decimal(reader, "NOK");
            vm.GP12.Retrabajado = Decimal(reader, "Retrabajado");
            vm.GP12.Scrap = Decimal(reader, "Scrap");
        }

        if (await reader.NextResultAsync(cancellationToken) && await reader.ReadAsync(cancellationToken))
        {
            vm.Almacen.MovimientosMP = Int(reader, "MovimientosMP");
            vm.Almacen.MovimientosPT = Int(reader, "MovimientosPT");
            vm.Almacen.MovimientosEmbalajes = Int(reader, "MovimientosEmbalajes");
            vm.Almacen.EntradasMP = Int(reader, "EntradasMP");
            vm.Almacen.SalidasMP = Int(reader, "SalidasMP");
            vm.Almacen.EntradasPT = Int(reader, "EntradasPT");
            vm.Almacen.SalidasPT = Int(reader, "SalidasPT");
            vm.Almacen.ScrapPendienteRecepcion = Int(reader, "ScrapPendienteRecepcion");
            vm.Almacen.ScrapRecibidoPendienteMolienda = Int(reader, "ScrapPendienteMolienda");
            vm.Almacen.MPBajoMinimo = Int(reader, "MPBajoMinimo");
            vm.Almacen.PTEnRojo = Int(reader, "PTEnRojo");
            vm.Almacen.EmbalajesEnRojo = Int(reader, "EmbalajesEnRojo");
        }

        if (await reader.NextResultAsync(cancellationToken) && await reader.ReadAsync(cancellationToken))
        {
            vm.Logistica.Embarques = Int(reader, "Embarques");
            vm.Logistica.Entregados = Int(reader, "Entregados");
            vm.Logistica.EntregadosATiempo = Int(reader, "EntregadosATiempo");
            vm.Logistica.Incidencias = Int(reader, "Incidencias");
            vm.Logistica.Atrasados = Int(reader, "Atrasados");
        }

        if (await reader.NextResultAsync(cancellationToken) && await reader.ReadAsync(cancellationToken))
        {
            vm.Logistica.IncidenciasAbiertas = Int(reader, "IncidenciasAbiertas");
            vm.Logistica.CriticasAbiertas = Int(reader, "CriticasAbiertas");
        }

        if (await reader.NextResultAsync(cancellationToken) && await reader.ReadAsync(cancellationToken))
        {
            vm.Compras.Solicitudes = Int(reader, "Solicitudes");
            vm.Compras.PendientesFlujo = Int(reader, "PendientesFlujo");
            vm.Compras.UrgentesPendientes = Int(reader, "UrgentesPendientes");
            vm.Compras.OrdenesCompra = Int(reader, "OrdenesCompra");
            vm.Compras.MontoOrdenes = Decimal(reader, "MontoOrdenes");
            vm.Compras.PromedioDiasEnEstatus = Decimal(reader, "PromedioDiasEnEstatus");
        }

        if (await reader.NextResultAsync(cancellationToken) && await reader.ReadAsync(cancellationToken))
        {
            vm.Compras.Recepciones = Int(reader, "Recepciones");
            vm.Compras.RecepcionesATiempo = Int(reader, "RecepcionesATiempo");
        }
    }

    private static void ConstruirAlertas(IndicadoresDashboardVm vm)
    {
        if (vm.Produccion.RegistrosHora == 0)
        {
            vm.Alertas.Add(new IndicadoresAlertaVm
            {
                Nivel = "warning",
                Area = "Producción",
                Titulo = "Sin captura de producción en el periodo",
                Detalle = "No hay registros por hora para calcular rendimiento operativo."
            });
        }
        else
        {
            if (vm.Produccion.OeePct < 75m)
            {
                vm.Alertas.Add(new IndicadoresAlertaVm
                {
                    Nivel = "danger",
                    Area = "Producción",
                    Titulo = $"OEE en {vm.Produccion.OeePct:N1}%",
                    Detalle = "El OEE del periodo está por debajo de 75%. Revisar rendimiento, calidad y paros."
                });
            }

            if (vm.Produccion.ScrapPct > 3m)
            {
                vm.Alertas.Add(new IndicadoresAlertaVm
                {
                    Nivel = "warning",
                    Area = "Producción / Calidad",
                    Titulo = $"Scrap en {vm.Produccion.ScrapPct:N2}%",
                    Detalle = "El porcentaje de scrap del periodo supera el umbral de seguimiento de 3%."
                });
            }
        }

        if (vm.Planeacion.ProgramasConPendiente > 0)
        {
            vm.Alertas.Add(new IndicadoresAlertaVm
            {
                Nivel = "info",
                Area = "Planeación",
                Titulo = $"{vm.Planeacion.ProgramasConPendiente:N0} programa(s) con pendiente",
                Detalle = "Existen programas cuya cantidad producida todavía no alcanza la cantidad programada."
            });
        }

        if (vm.Calidad.Contenciones > 0)
        {
            vm.Alertas.Add(new IndicadoresAlertaVm
            {
                Nivel = "warning",
                Area = "Calidad",
                Titulo = $"{vm.Calidad.Contenciones:N0} inspección(es) en contención",
                Detalle = "Revisar liberaciones, reliberaciones y material retenido del periodo."
            });
        }

        if (vm.GP12.SolicitudesPendientes > 0)
        {
            vm.Alertas.Add(new IndicadoresAlertaVm
            {
                Nivel = "info",
                Area = "GP12",
                Titulo = $"{vm.GP12.SolicitudesPendientes:N0} solicitud(es) con material pendiente",
                Detalle = "Hay solicitudes GP12 activas con cantidad pendiente de procesar."
            });
        }

        if (vm.Almacen.ScrapPendienteRecepcion > 0)
        {
            vm.Alertas.Add(new IndicadoresAlertaVm
            {
                Nivel = "warning",
                Area = "Almacén",
                Titulo = $"{vm.Almacen.ScrapPendienteRecepcion:N0} entrega(s) de Scrap sin recibir",
                Detalle = "Calidad ya originó estas entregas y falta la confirmación física de Almacén."
            });
        }

        var alertasStock = vm.Almacen.MPBajoMinimo + vm.Almacen.PTEnRojo + vm.Almacen.EmbalajesEnRojo;
        if (alertasStock > 0)
        {
            vm.Alertas.Add(new IndicadoresAlertaVm
            {
                Nivel = "warning",
                Area = "Almacén",
                Titulo = $"{alertasStock:N0} referencia(s) en nivel crítico de inventario",
                Detalle = "La lectura combina MP bajo mínimo y semáforos rojos de PT y embalajes."
            });
        }

        if (vm.Logistica.Atrasados > 0)
        {
            vm.Alertas.Add(new IndicadoresAlertaVm
            {
                Nivel = "danger",
                Area = "Logística",
                Titulo = $"{vm.Logistica.Atrasados:N0} embarque(s) atrasado(s)",
                Detalle = "Tienen fecha programada vencida y no cuentan con fecha de entrega."
            });
        }

        if (vm.Compras.UrgentesPendientes > 0)
        {
            vm.Alertas.Add(new IndicadoresAlertaVm
            {
                Nivel = "warning",
                Area = "Compras",
                Titulo = $"{vm.Compras.UrgentesPendientes:N0} solicitud(es) prioritarias pendientes",
                Detalle = "Hay solicitudes de prioridad alta, urgente o crítica que continúan abiertas en el flujo."
            });
        }

        if (vm.Alertas.Count == 0)
        {
            vm.Alertas.Add(new IndicadoresAlertaVm
            {
                Nivel = "success",
                Area = "Operación",
                Titulo = "Sin excepciones críticas en los indicadores monitoreados",
                Detalle = "Los datos disponibles del periodo no activaron reglas de atención."
            });
        }
    }

    private static (DateTime Desde, DateTime Hasta, string Periodo, bool Limitado) ResolverPeriodo(
        string? periodo,
        string? semana,
        string? mes,
        DateTime? fecha,
        DateTime? desde,
        DateTime? hasta)
    {
        var modo = Normalizar(periodo).ToLowerInvariant();

        if (modo is not ("dia" or "semana" or "mes" or "rango"))
        {
            modo = !string.IsNullOrWhiteSpace(semana) ? "semana"
                : !string.IsNullOrWhiteSpace(mes) ? "mes"
                : fecha.HasValue ? "dia"
                : (desde.HasValue || hasta.HasValue) ? "rango"
                : "semana";
        }

        if (modo == "dia")
        {
            var day = (fecha ?? desde ?? hasta ?? DateTime.Today).Date;
            return (day, day, "dia", false);
        }

        if (modo == "mes")
        {
            DateTime firstDay;
            if (!string.IsNullOrWhiteSpace(mes)
                && DateTime.TryParseExact(
                    mes.Trim(),
                    "yyyy-MM",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsedMonth))
            {
                firstDay = new DateTime(parsedMonth.Year, parsedMonth.Month, 1);
            }
            else
            {
                var reference = (fecha ?? desde ?? hasta ?? DateTime.Today).Date;
                firstDay = new DateTime(reference.Year, reference.Month, 1);
            }

            return (firstDay, firstDay.AddMonths(1).AddDays(-1), "mes", false);
        }

        if (modo == "rango")
        {
            var start = (desde ?? fecha ?? hasta ?? DateTime.Today).Date;
            var finish = (hasta ?? fecha ?? desde ?? start).Date;
            if (finish < start)
                (start, finish) = (finish, start);

            const int maxDias = 93;
            var limitado = (finish - start).Days + 1 > maxDias;
            if (limitado)
                finish = start.AddDays(maxDias - 1);

            return (start, finish, "rango", limitado);
        }

        if (!string.IsNullOrWhiteSpace(semana))
        {
            var value = semana.Trim().ToUpperInvariant();
            var parts = value.Split("-W", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (parts.Length == 2
                && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)
                && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var week)
                && year is >= 2000 and <= 2100
                && week >= 1
                && week <= ISOWeek.GetWeeksInYear(year))
            {
                var monday = ISOWeek.ToDateTime(year, week, DayOfWeek.Monday).Date;
                return (monday, monday.AddDays(6), "semana", false);
            }
        }

        var referenceWeek = (fecha ?? desde ?? hasta ?? DateTime.Today).Date;
        var daysFromMonday = ((int)referenceWeek.DayOfWeek + 6) % 7;
        var weekStart = referenceWeek.AddDays(-daysFromMonday);
        return (weekStart, weekStart.AddDays(6), "semana", false);
    }

    private static string NormalizarSeccion(string? area)
    {
        var value = Normalizar(area).ToLowerInvariant();
        return value switch
        {
            "produccion" => "produccion",
            "planeacion" => "planeacion",
            "calidad" => "calidad",
            "gp12" => "gp12",
            "almacen" => "almacen",
            "logistica" => "logistica",
            "compras" => "compras",
            _ => "general"
        };
    }

    private static void AddPeriodo(SqlCommand command, DateTime desde, DateTime hasta)
    {
        command.Parameters.Add("@Desde", SqlDbType.DateTime2).Value = desde.Date;
        command.Parameters.Add("@HastaExclusiva", SqlDbType.DateTime2).Value = hasta.Date.AddDays(1);
    }

    private static string Normalizar(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var normalized = text.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
        return new string(normalized
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray())
            .Normalize(NormalizationForm.FormC);
    }

    private static int Int(SqlDataReader reader, string column)
    {
        var i = reader.GetOrdinal(column);
        return reader.IsDBNull(i) ? 0 : Convert.ToInt32(reader.GetValue(i));
    }

    private static long Long(SqlDataReader reader, string column)
    {
        var i = reader.GetOrdinal(column);
        return reader.IsDBNull(i) ? 0L : Convert.ToInt64(reader.GetValue(i));
    }

    private static decimal Decimal(SqlDataReader reader, string column)
    {
        var i = reader.GetOrdinal(column);
        return reader.IsDBNull(i) ? 0m : Convert.ToDecimal(reader.GetValue(i));
    }

    private static string Text(SqlDataReader reader, string column)
    {
        var i = reader.GetOrdinal(column);
        return reader.IsDBNull(i) ? string.Empty : reader.GetValue(i)?.ToString() ?? string.Empty;
    }
}
