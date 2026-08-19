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
        const string sql = @"
SELECT
    COUNT(*) AS RegistrosHora,
    COUNT(DISTINCT OperadorID) AS Operadores,
    SUM(CONVERT(BIGINT,ISNULL(CantidadOK,0))) AS PiezasOK,
    SUM(CONVERT(BIGINT,ISNULL(CantidadSospechosa,0))) AS PiezasSospechosas,
    SUM(CONVERT(BIGINT,ISNULL(CantidadScrap,0))) AS PiezasScrap,
    SUM(CONVERT(BIGINT,COALESCE(NULLIF(ObjetivoBloque,0),NULLIF(ObjetivoHora,0),0))) AS Objetivo,
    SUM(CONVERT(DECIMAL(18,2),
        CASE
            WHEN HoraFin>=HoraInicio THEN DATEDIFF(MINUTE,HoraInicio,HoraFin)
            ELSE 1440+DATEDIFF(MINUTE,HoraInicio,HoraFin)
        END)) AS MinutosProduccion
FROM dbo.Produccion_RegistroHora
WHERE Activo=1
  AND FechaProduccion>=@Desde
  AND FechaProduccion<@HastaExclusiva;

SELECT
    SUM(CONVERT(DECIMAL(18,2),
        CASE
            WHEN DuracionMinutos IS NOT NULL THEN DuracionMinutos
            WHEN FechaFinParo IS NOT NULL THEN DATEDIFF(MINUTE,FechaInicioParo,FechaFinParo)
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
WITH R AS
(
    SELECT
        rh.OperadorID,
        COUNT(*) AS Registros,
        SUM(CONVERT(BIGINT,ISNULL(rh.CantidadOK,0))) AS PiezasOK,
        SUM(CONVERT(BIGINT,ISNULL(rh.CantidadSospechosa,0))) AS PiezasSospechosas,
        SUM(CONVERT(BIGINT,ISNULL(rh.CantidadScrap,0))) AS PiezasScrap,
        SUM(CONVERT(BIGINT,COALESCE(NULLIF(rh.ObjetivoBloque,0),NULLIF(rh.ObjetivoHora,0),0))) AS Objetivo,
        SUM(CONVERT(DECIMAL(18,2),CASE WHEN rh.HoraFin>=rh.HoraInicio
            THEN DATEDIFF(MINUTE,rh.HoraInicio,rh.HoraFin)
            ELSE 1440+DATEDIFF(MINUTE,rh.HoraInicio,rh.HoraFin) END)) AS MinutosProduccion,
        MAX(NULLIF(LTRIM(RTRIM(e.OperadorNombre)),N'')) AS OperadorSnapshot
    FROM dbo.Produccion_RegistroHora rh
    LEFT JOIN dbo.Produccion_Ejecucion e
        ON e.EjecucionProduccionID=rh.EjecucionProduccionID
    WHERE rh.Activo=1
      AND rh.OperadorID IS NOT NULL
      AND rh.FechaProduccion>=@Desde
      AND rh.FechaProduccion<@HastaExclusiva
    GROUP BY rh.OperadorID
),
P AS
(
    SELECT
        OperadorID,
        SUM(CONVERT(DECIMAL(18,2),CASE
            WHEN DuracionMinutos IS NOT NULL THEN DuracionMinutos
            WHEN FechaFinParo IS NOT NULL THEN DATEDIFF(MINUTE,FechaInicioParo,FechaFinParo)
            ELSE 0 END)) AS MinutosParo
    FROM dbo.Produccion_Paros
    WHERE Activo=1
      AND OperadorID IS NOT NULL
      AND FechaInicioParo>=@Desde
      AND FechaInicioParo<@HastaExclusiva
    GROUP BY OperadorID
)
SELECT TOP (30)
    r.OperadorID,
    COALESCE(
        NULLIF(LTRIM(RTRIM(CONCAT(per.Nombre,N' ',per.ApellidoPaterno,N' ',per.ApellidoMaterno))),N''),
        r.OperadorSnapshot,
        CONCAT(N'Operador #',r.OperadorID)
    ) AS Operador,
    ISNULL(per.NumeroControl,N'') AS NumeroControl,
    r.PiezasOK,
    r.PiezasSospechosas,
    r.PiezasScrap,
    r.Objetivo,
    r.MinutosProduccion,
    ISNULL(p.MinutosParo,0) AS MinutosParo,
    r.Registros
FROM R r
LEFT JOIN P p ON p.OperadorID=r.OperadorID
LEFT JOIN dbo.Persona per ON per.PersonaID=r.OperadorID
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
                Registros = Int(reader, "Registros")
            });
        }

        vm.Operadores = vm.Operadores
            .OrderByDescending(x => x.OeePct)
            .ThenByDescending(x => x.PiezasOK)
            .Take(12)
            .ToList();
    }

    private static async Task CargarMaquinasAsync(
        SqlConnection connection,
        IndicadoresDashboardVm vm,
        CancellationToken cancellationToken)
    {
        const string sql = @"
WITH R AS
(
    SELECT
        rh.MaquinaID,
        SUM(CONVERT(BIGINT,ISNULL(rh.CantidadOK,0))) AS PiezasOK,
        SUM(CONVERT(BIGINT,ISNULL(rh.CantidadSospechosa,0))) AS PiezasSospechosas,
        SUM(CONVERT(BIGINT,ISNULL(rh.CantidadScrap,0))) AS PiezasScrap,
        SUM(CONVERT(BIGINT,COALESCE(NULLIF(rh.ObjetivoBloque,0),NULLIF(rh.ObjetivoHora,0),0))) AS Objetivo,
        SUM(CONVERT(DECIMAL(18,2),CASE WHEN rh.HoraFin>=rh.HoraInicio
            THEN DATEDIFF(MINUTE,rh.HoraInicio,rh.HoraFin)
            ELSE 1440+DATEDIFF(MINUTE,rh.HoraInicio,rh.HoraFin) END)) AS MinutosProduccion,
        MAX(NULLIF(LTRIM(RTRIM(e.MaquinaNombre)),N'')) AS MaquinaSnapshot
    FROM dbo.Produccion_RegistroHora rh
    LEFT JOIN dbo.Produccion_Ejecucion e
        ON e.EjecucionProduccionID=rh.EjecucionProduccionID
    WHERE rh.Activo=1
      AND rh.MaquinaID IS NOT NULL
      AND rh.FechaProduccion>=@Desde
      AND rh.FechaProduccion<@HastaExclusiva
    GROUP BY rh.MaquinaID
),
P AS
(
    SELECT
        MaquinaID,
        SUM(CONVERT(DECIMAL(18,2),CASE
            WHEN DuracionMinutos IS NOT NULL THEN DuracionMinutos
            WHEN FechaFinParo IS NOT NULL THEN DATEDIFF(MINUTE,FechaInicioParo,FechaFinParo)
            ELSE 0 END)) AS MinutosParo
    FROM dbo.Produccion_Paros
    WHERE Activo=1
      AND MaquinaID IS NOT NULL
      AND FechaInicioParo>=@Desde
      AND FechaInicioParo<@HastaExclusiva
    GROUP BY MaquinaID
)
SELECT TOP (20)
    r.MaquinaID,
    COALESCE(NULLIF(LTRIM(RTRIM(m.Codigo)),N''),NULLIF(LTRIM(RTRIM(r.MaquinaSnapshot)),N''),CONCAT(N'Máquina #',r.MaquinaID)) AS Maquina,
    r.PiezasOK,
    r.PiezasSospechosas,
    r.PiezasScrap,
    r.Objetivo,
    r.MinutosProduccion,
    ISNULL(p.MinutosParo,0) AS MinutosParo
FROM R r
LEFT JOIN P p ON p.MaquinaID=r.MaquinaID
LEFT JOIN dbo.ERP_Maquinas m ON m.MaquinaID=r.MaquinaID
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
                MinutosParo = Decimal(reader, "MinutosParo")
            });
        }

        vm.Maquinas = vm.Maquinas
            .OrderByDescending(x => x.OeePct)
            .ThenByDescending(x => x.PiezasOK)
            .Take(8)
            .ToList();
    }

    private static async Task CargarTendenciaAsync(
        SqlConnection connection,
        IndicadoresDashboardVm vm,
        CancellationToken cancellationToken)
    {
        const string sql = @"
WITH R AS
(
    SELECT
        FechaProduccion AS Fecha,
        SUM(CONVERT(BIGINT,ISNULL(CantidadOK,0))) AS PiezasOK,
        SUM(CONVERT(BIGINT,ISNULL(CantidadSospechosa,0))) AS PiezasSospechosas,
        SUM(CONVERT(BIGINT,ISNULL(CantidadScrap,0))) AS PiezasScrap,
        SUM(CONVERT(BIGINT,COALESCE(NULLIF(ObjetivoBloque,0),NULLIF(ObjetivoHora,0),0))) AS Objetivo,
        SUM(CONVERT(DECIMAL(18,2),CASE WHEN HoraFin>=HoraInicio
            THEN DATEDIFF(MINUTE,HoraInicio,HoraFin)
            ELSE 1440+DATEDIFF(MINUTE,HoraInicio,HoraFin) END)) AS MinutosProduccion
    FROM dbo.Produccion_RegistroHora
    WHERE Activo=1
      AND FechaProduccion>=@Desde
      AND FechaProduccion<@HastaExclusiva
    GROUP BY FechaProduccion
),
P AS
(
    SELECT
        CAST(FechaInicioParo AS date) AS Fecha,
        SUM(CONVERT(DECIMAL(18,2),CASE
            WHEN DuracionMinutos IS NOT NULL THEN DuracionMinutos
            WHEN FechaFinParo IS NOT NULL THEN DATEDIFF(MINUTE,FechaInicioParo,FechaFinParo)
            ELSE 0 END)) AS MinutosParo
    FROM dbo.Produccion_Paros
    WHERE Activo=1
      AND FechaInicioParo>=@Desde
      AND FechaInicioParo<@HastaExclusiva
    GROUP BY CAST(FechaInicioParo AS date)
)
SELECT r.Fecha,r.PiezasOK,r.PiezasSospechosas,r.PiezasScrap,r.Objetivo,r.MinutosProduccion,ISNULL(p.MinutosParo,0) AS MinutosParo
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
