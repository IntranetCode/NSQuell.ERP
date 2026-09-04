using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Servicios;

public sealed class ProduccionBloqueoCorreoService
{
    private readonly string _connectionString;
    private readonly NotificacionCorreoErpService _correoErp;
    private readonly ILogger<ProduccionBloqueoCorreoService> _logger;

    private sealed record Bloqueo(
        string Clave,
        string Codigo,
        int InspeccionID,
        int EjecucionProduccionID,
        string Departamento,
        string Titulo,
        string Mensaje,
        string UrlDestino,
        DateTime FechaInicio);

    private sealed record Seguimiento(
        string Clave,
        DateTime FechaProximoEnvio,
        bool Activo);

    public ProduccionBloqueoCorreoService(
        IConfiguration configuration,
        NotificacionCorreoErpService correoErp,
        ILogger<ProduccionBloqueoCorreoService> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:DefaultConnection.");
        _correoErp = correoErp;
        _logger = logger;
    }

    public async Task ProcesarAsync(CancellationToken cancellationToken)
    {
        if (!await ExisteTablaSeguimientoAsync(cancellationToken))
        {
            _logger.LogWarning(
                "No existe dbo.NotificacionCorreoBloqueos. Ejecuta NSQ_NOTIFICACIONES_CORREO_V10_SCHEMA.sql.");
            return;
        }

        var ahora = DateTime.Now;
        var bloqueos = await ObtenerBloqueosAsync(cancellationToken);
        var clavesActivas = bloqueos.Select(x => x.Clave).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var bloqueo in bloqueos)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var seguimiento = await ObtenerSeguimientoAsync(bloqueo.Clave, cancellationToken);

            if (seguimiento == null)
            {
                // Un bloqueo interdepartamental debe avisarse en cuanto se detecta.
                // Después de este primer aviso, la tabla durable impone intervalos de 15 minutos.
                await CrearSeguimientoAsync(
                    bloqueo,
                    ahora,
                    cancellationToken);

                seguimiento = await ObtenerSeguimientoAsync(bloqueo.Clave, cancellationToken);
            }

            if (seguimiento == null || !seguimiento.Activo || ahora < seguimiento.FechaProximoEnvio)
                continue;

            await EnviarRecordatorioAsync(bloqueo, ahora, cancellationToken);
        }

        await MarcarResueltosAsync(clavesActivas, ahora, cancellationToken);
    }

    private async Task EnviarRecordatorioAsync(
        Bloqueo bloqueo,
        DateTime ahora,
        CancellationToken cancellationToken)
    {
        var usuarios = await _correoErp.ObtenerUsuariosDepartamentoAsync(bloqueo.Departamento);

        string resultadoTexto;
        DateTime? fechaEnvio = null;

        if (usuarios.Count == 0)
        {
            resultadoTexto = $"Sin usuarios activos con correo en {bloqueo.Departamento}.";
            _logger.LogWarning(
                "Bloqueo {Clave}: no hay usuarios con correo en {Departamento}.",
                bloqueo.Clave,
                bloqueo.Departamento);
        }
        else
        {
            var transcurridos = Math.Max(0, (int)Math.Floor((ahora - bloqueo.FechaInicio).TotalMinutes));
            var mensaje =
                $"{bloqueo.Mensaje} Tiempo acumulado del bloqueo: {transcurridos} minuto(s). " +
                "Este correo se repetirá cada 15 minutos mientras la condición siga pendiente.";

            var res = await _correoErp.EnviarAUsuariosAsync(
                usuarios,
                bloqueo.Titulo,
                mensaje,
                bloqueo.UrlDestino,
                bloqueo.Codigo,
                bloqueo.Departamento,
                urgente: true,
                textoBoton: "Atender bloqueo ahora");

            fechaEnvio = res.Enviados > 0 ? ahora : null;
            resultadoTexto =
                $"Encontrados={res.Encontrados}; Enviados={res.Enviados}; " +
                $"Bloqueados={res.FiltradosPorCandados}; Errores={res.Errores}; " +
                string.Join(" | ", res.Mensajes.Take(3));
        }

        await ActualizarIntentoAsync(
            bloqueo.Clave,
            ahora,
            fechaEnvio,
            ahora.AddMinutes(15),
            resultadoTexto,
            cancellationToken);
    }

    private async Task<List<Bloqueo>> ObtenerBloqueosAsync(CancellationToken cancellationToken)
    {
        const string sql = """
;WITH UltimaReliberacion AS
(
    SELECT
        r.InspeccionID,
        r.ReliberacionID,
        r.FechaSolicitud,
        r.Resultado,
        ROW_NUMBER() OVER
        (
            PARTITION BY r.InspeccionID
            ORDER BY r.NumeroReliberacion DESC, r.ReliberacionID DESC
        ) AS rn
    FROM dbo.Calidad_Reliberaciones r
    WHERE ISNULL(r.Activo,1)=1
),
Base AS
(
    SELECT
        ci.InspeccionID,
        ci.EjecucionProduccionID,
        UPPER(LTRIM(RTRIM(ISNULL(ci.Estado,N'')))) AS Estado,
        ISNULL(NULLIF(LTRIM(RTRIM(ci.OrdenTrabajo)),N''), CONCAT(N'Inspección #',ci.InspeccionID)) AS OrdenTrabajo,
        ISNULL(NULLIF(LTRIM(RTRIM(ci.NumeroParte)),N''),N'Sin número de parte') AS NumeroParte,
        ISNULL(NULLIF(LTRIM(RTRIM(ci.Maquina)),N''),N'Sin máquina') AS Maquina,
        ISNULL(NULLIF(LTRIM(RTRIM(ci.MotivoDevolucion)),N''),N'') AS MotivoDevolucion,
        COALESCE(ci.FechaModificacion,ci.FechaNotificacionCalidad,ci.FechaCreacion,GETDATE()) AS FechaEstado,
        ci.FechaNotificacionCalidad,
        ur.FechaSolicitud AS FechaSolicitudReliberacion,
        CASE
            WHEN UPPER(LTRIM(RTRIM(ISNULL(ci.Estado,N''))))=N'PENDIENTE_RELIBERACION'
                 AND ur.FechaSolicitud IS NOT NULL
                 AND EXISTS
                 (
                     SELECT 1
                     FROM dbo.Calidad_PrimerasPiezasIntentos i
                     WHERE i.InspeccionID=ci.InspeccionID
                       AND ISNULL(i.Activo,1)=1
                       AND i.FechaCreacion>=ur.FechaSolicitud
                 )
                THEN 1
            ELSE 0
        END AS ReliberacionConPiezas
    FROM dbo.Calidad_Inspecciones ci
    LEFT JOIN UltimaReliberacion ur
        ON ur.InspeccionID=ci.InspeccionID
       AND ur.rn=1
    WHERE ci.EjecucionProduccionID IS NOT NULL
      AND ci.EjecucionProduccionID>0
      AND UPPER(LTRIM(RTRIM(ISNULL(ci.Estado,N'')))) IN
      (
          N'PENDIENTE_PREARRANQUE',
          N'DEVUELTO_PREARRANQUE',
          N'PENDIENTE_PRIMERAS_PIEZAS',
          N'AJUSTES_SOLICITADOS',
          N'PENDIENTE_RELIBERACION'
      )
)
SELECT
    InspeccionID,
    EjecucionProduccionID,
    Estado,
    OrdenTrabajo,
    NumeroParte,
    Maquina,
    MotivoDevolucion,
    CASE
        WHEN Estado=N'PENDIENTE_PREARRANQUE' THEN N'Calidad'
        WHEN Estado=N'PENDIENTE_PRIMERAS_PIEZAS' THEN N'Calidad'
        WHEN Estado=N'PENDIENTE_RELIBERACION' AND ReliberacionConPiezas=1 THEN N'Calidad'
        ELSE N'Producción'
    END AS DepartamentoResponsable,
    CASE
        WHEN Estado=N'PENDIENTE_PREARRANQUE'
            THEN COALESCE(FechaNotificacionCalidad,FechaEstado)
        ELSE FechaEstado
    END AS FechaInicioBloqueo
FROM Base;
""";

        var salida = new List<Bloqueo>();

        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync(cancellationToken);

        await using var cmd = new SqlCommand(sql, cn);
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);

        while (await rd.ReadAsync(cancellationToken))
        {
            var inspeccionId = Convert.ToInt32(rd["InspeccionID"]);
            var ejecucionId = Convert.ToInt32(rd["EjecucionProduccionID"]);
            var estado = rd["Estado"]?.ToString()?.Trim() ?? string.Empty;
            var orden = rd["OrdenTrabajo"]?.ToString()?.Trim() ?? $"Inspección #{inspeccionId}";
            var parte = rd["NumeroParte"]?.ToString()?.Trim() ?? "Sin número de parte";
            var maquina = rd["Maquina"]?.ToString()?.Trim() ?? "Sin máquina";
            var motivo = rd["MotivoDevolucion"]?.ToString()?.Trim() ?? string.Empty;
            var departamento = rd["DepartamentoResponsable"]?.ToString()?.Trim() ?? string.Empty;
            var fechaInicio = Convert.ToDateTime(rd["FechaInicioBloqueo"]);

            var responsableCalidad = departamento.Equals("Calidad", StringComparison.OrdinalIgnoreCase);
            var url = responsableCalidad
                ? $"/Calidad/Detalle/{inspeccionId}"
                : $"/Produccion/Detalle/{ejecucionId}";

            var (codigo, titulo, mensaje) = estado switch
            {
                "PENDIENTE_PREARRANQUE" =>
                    ("BLOQUEO_CALIDAD_PREARRANQUE",
                     $"Calidad pendiente · {orden}",
                     $"Producción está esperando la autorización del checklist de prearranque. Parte: {parte}. Máquina: {maquina}."),

                "PENDIENTE_PRIMERAS_PIEZAS" =>
                    ("BLOQUEO_CALIDAD_PRIMERAS_PIEZAS",
                     $"Liberación de primeras piezas pendiente · {orden}",
                     $"Producción no puede iniciar la serie hasta que Calidad concluya la validación y liberación de primeras piezas. Parte: {parte}. Máquina: {maquina}."),

                "DEVUELTO_PREARRANQUE" =>
                    ("BLOQUEO_PRODUCCION_PREARRANQUE_DEVUELTO",
                     $"Prearranque devuelto a Producción · {orden}",
                     $"Calidad devolvió el prearranque. Producción debe corregirlo para poder continuar. Parte: {parte}. Máquina: {maquina}. Motivo: {Texto(motivo,"Revisar detalle en ERP")}."),

                "AJUSTES_SOLICITADOS" =>
                    ("BLOQUEO_PRODUCCION_AJUSTES",
                     $"Ajustes solicitados por Calidad · {orden}",
                     $"Producción debe realizar los ajustes solicitados y presentar nuevas primeras piezas. Parte: {parte}. Máquina: {maquina}. Motivo: {Texto(motivo,"Revisar detalle en ERP")}."),

                "PENDIENTE_RELIBERACION" when responsableCalidad =>
                    ("BLOQUEO_CALIDAD_RELIBERACION",
                     $"Reliberación pendiente de Calidad · {orden}",
                     $"Producción ya presentó nuevas primeras piezas y está esperando la decisión de Calidad para reanudar la serie. Parte: {parte}. Máquina: {maquina}."),

                "PENDIENTE_RELIBERACION" =>
                    ("BLOQUEO_PRODUCCION_RELIBERACION",
                     $"Reliberación requiere nuevas piezas · {orden}",
                     $"Producción debe generar y presentar nuevas primeras piezas para que Calidad pueda reliberar la corrida. Parte: {parte}. Máquina: {maquina}."),

                _ =>
                    ("BLOQUEO_PRODUCCION",
                     $"Bloqueo de producción · {orden}",
                     $"La corrida requiere atención del departamento {departamento}. Parte: {parte}. Máquina: {maquina}.")
            };

            salida.Add(new Bloqueo(
                Clave: $"{codigo}:{inspeccionId}",
                Codigo: codigo,
                InspeccionID: inspeccionId,
                EjecucionProduccionID: ejecucionId,
                Departamento: departamento,
                Titulo: titulo,
                Mensaje: mensaje,
                UrlDestino: url,
                FechaInicio: fechaInicio));
        }

        return salida;
    }

    private async Task<bool> ExisteTablaSeguimientoAsync(CancellationToken cancellationToken)
    {
        const string sql = "SELECT CASE WHEN OBJECT_ID(N'dbo.NotificacionCorreoBloqueos',N'U') IS NULL THEN 0 ELSE 1 END;";

        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync(cancellationToken);
        await using var cmd = new SqlCommand(sql, cn);

        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private async Task<Seguimiento?> ObtenerSeguimientoAsync(
        string clave,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT TOP(1)
    ClaveBloqueo,
    FechaProximoEnvio,
    Activo
FROM dbo.NotificacionCorreoBloqueos
WHERE ClaveBloqueo=@Clave;
""";

        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync(cancellationToken);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@Clave", SqlDbType.NVarChar, 200).Value = clave;

        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await rd.ReadAsync(cancellationToken))
            return null;

        return new Seguimiento(
            rd["ClaveBloqueo"].ToString() ?? clave,
            Convert.ToDateTime(rd["FechaProximoEnvio"]),
            Convert.ToBoolean(rd["Activo"]));
    }

    private async Task CrearSeguimientoAsync(
        Bloqueo bloqueo,
        DateTime proximoEnvio,
        CancellationToken cancellationToken)
    {
        const string sql = """
IF NOT EXISTS
(
    SELECT 1
    FROM dbo.NotificacionCorreoBloqueos WITH(UPDLOCK,HOLDLOCK)
    WHERE ClaveBloqueo=@Clave
)
BEGIN
    INSERT dbo.NotificacionCorreoBloqueos
    (
        ClaveBloqueo,CodigoBloqueo,Entidad,EntidadID,DepartamentoNombre,
        FechaInicioBloqueo,FechaProximoEnvio,Intentos,Activo,
        FechaCreacion,FechaModificacion
    )
    VALUES
    (
        @Clave,@Codigo,N'Calidad_Inspecciones',@EntidadID,@Departamento,
        @FechaInicio,@FechaProximo,0,1,SYSDATETIME(),SYSDATETIME()
    );
END;
""";

        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        try
        {
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@Clave", SqlDbType.NVarChar, 200).Value = bloqueo.Clave;
            cmd.Parameters.Add("@Codigo", SqlDbType.NVarChar, 80).Value = bloqueo.Codigo;
            cmd.Parameters.Add("@EntidadID", SqlDbType.Int).Value = bloqueo.InspeccionID;
            cmd.Parameters.Add("@Departamento", SqlDbType.NVarChar, 150).Value = bloqueo.Departamento;
            cmd.Parameters.Add("@FechaInicio", SqlDbType.DateTime2).Value = bloqueo.FechaInicio;
            cmd.Parameters.Add("@FechaProximo", SqlDbType.DateTime2).Value = proximoEnvio;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            try { await tx.RollbackAsync(cancellationToken); } catch { }
            throw;
        }
    }

    private async Task ActualizarIntentoAsync(
        string clave,
        DateTime intento,
        DateTime? envio,
        DateTime proximo,
        string resultado,
        CancellationToken cancellationToken)
    {
        const string sql = """
UPDATE dbo.NotificacionCorreoBloqueos
SET
    FechaUltimoIntento=@Intento,
    FechaUltimoEnvio=CASE WHEN @Envio IS NULL THEN FechaUltimoEnvio ELSE @Envio END,
    FechaProximoEnvio=@Proximo,
    Intentos=Intentos+1,
    UltimoResultado=@Resultado,
    FechaModificacion=SYSDATETIME()
WHERE ClaveBloqueo=@Clave
  AND Activo=1;
""";

        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync(cancellationToken);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@Clave", SqlDbType.NVarChar, 200).Value = clave;
        cmd.Parameters.Add("@Intento", SqlDbType.DateTime2).Value = intento;
        cmd.Parameters.Add("@Envio", SqlDbType.DateTime2).Value = (object?)envio ?? DBNull.Value;
        cmd.Parameters.Add("@Proximo", SqlDbType.DateTime2).Value = proximo;
        cmd.Parameters.Add("@Resultado", SqlDbType.NVarChar, 1000).Value = Recortar(resultado, 1000);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task MarcarResueltosAsync(
        HashSet<string> clavesActivas,
        DateTime ahora,
        CancellationToken cancellationToken)
    {
        const string selectSql = "SELECT ClaveBloqueo FROM dbo.NotificacionCorreoBloqueos WHERE Activo=1;";
        var registradas = new List<string>();

        await using (var cn = new SqlConnection(_connectionString))
        {
            await cn.OpenAsync(cancellationToken);
            await using var cmd = new SqlCommand(selectSql, cn);
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);

            while (await rd.ReadAsync(cancellationToken))
                registradas.Add(rd["ClaveBloqueo"].ToString() ?? string.Empty);
        }

        var resueltas = registradas
            .Where(x => !string.IsNullOrWhiteSpace(x) && !clavesActivas.Contains(x))
            .ToList();

        if (resueltas.Count == 0)
            return;

        const string updateSql = """
UPDATE dbo.NotificacionCorreoBloqueos
SET
    Activo=0,
    FechaResolucion=@Ahora,
    FechaModificacion=SYSDATETIME(),
    UltimoResultado=CASE
        WHEN UltimoResultado IS NULL OR LTRIM(RTRIM(UltimoResultado))=N''
            THEN N'Bloqueo resuelto.'
        ELSE UltimoResultado + N' | Bloqueo resuelto.'
    END
WHERE ClaveBloqueo=@Clave
  AND Activo=1;
""";

        await using var cn2 = new SqlConnection(_connectionString);
        await cn2.OpenAsync(cancellationToken);

        foreach (var clave in resueltas)
        {
            await using var cmd = new SqlCommand(updateSql, cn2);
            cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahora;
            cmd.Parameters.Add("@Clave", SqlDbType.NVarChar, 200).Value = clave;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static string Texto(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string Recortar(string? value, int max)
    {
        value ??= string.Empty;
        return value.Length <= max ? value : value[..max];
    }
}

public sealed class ProduccionBloqueoCorreoHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ProduccionBloqueoCorreoHostedService> _logger;

    public ProduccionBloqueoCorreoHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<ProduccionBloqueoCorreoHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Espera breve para permitir que la aplicación termine de iniciar.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<ProduccionBloqueoCorreoService>();
                await service.ProcesarAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error procesando recordatorios de bloqueos de Producción.");
            }

            try
            {
                // Se revisa cada minuto; la tabla durable impide enviar antes de cada intervalo de 15 minutos.
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}