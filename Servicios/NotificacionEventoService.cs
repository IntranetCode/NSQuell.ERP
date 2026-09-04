// NSQ_NOTIFICACIONES_V3E_DEPARTAMENTOS
using ERP.NSQuell.Models;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Servicios;

public sealed class NotificacionEventoService
{
    private readonly string _connectionString;
    private readonly ILogger<NotificacionEventoService> _logger;
    private readonly NotificacionCorreoErpService _correoErp;

    public NotificacionEventoService(
        IConfiguration configuration,
        NotificacionCorreoErpService correoErp,
        ILogger<NotificacionEventoService> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:DefaultConnection.");
        _logger = logger;
        _correoErp = correoErp;
    }

    private sealed record OfCreadaDatos(
        int SolicitudProduccionID,
        string NumeroOF,
        string Cliente,
        string NumeroParte,
        string ReferenciaSAP,
        string DescripcionParte,
        int TotalPiezas,
        int TotalRenglones,
        string Actor,
        DateTime FechaCreacion);

    public async Task PublicarOfCreadaAsync(int solicitudProduccionId, int actorUsuarioId)
    {
        // Se ejecuta despues del COMMIT. La notificacion nunca debe revertir la OF.
        try
        {
            if (solicitudProduccionId <= 0)
                return;

            var datos = await ObtenerOfCreadaAsync(solicitudProduccionId);
            if (datos == null)
            {
                _logger.LogWarning("OF_CREADA omitida: no existe SolicitudProduccionID={Id}.", solicitudProduccionId);
                return;
            }

            var numeroOf = Texto(datos.NumeroOF, $"#{solicitudProduccionId}");
            var cliente = Texto(datos.Cliente, "Sin cliente");
            var parte = Texto(datos.NumeroParte, Texto(datos.ReferenciaSAP, "Sin numero de parte"));
            var descripcion = Texto(datos.DescripcionParte, "Sin descripcion");
            var actor = Texto(datos.Actor, actorUsuarioId > 0 ? $"Usuario #{actorUsuarioId}" : "Usuario ERP");

            var evento = new NotificacionEvento
            {
                CodigoEvento = "OF_CREADA",
                Tipo = "OF_CREADA",
                Titulo = Recortar($"Nueva Orden de Fabricacion {numeroOf}", 200),
                Mensaje = Recortar(
                    $"Cliente: {cliente}. Parte: {parte}. Descripcion: {descripcion}. " +
                    $"Cantidad: {datos.TotalPiezas:N0} pzas. Renglones: {datos.TotalRenglones}. Creada por: {actor}.",
                    500),
                IdOrigen = solicitudProduccionId,
                TablaOrigen = "SolicitudesProduccion",
                // Vista neutral de solo lectura. Ningun departamento necesita entrar a Planeacion.
                UrlDestino = $"/SolicitudesProduccion/Detalle/{solicitudProduccionId}?soloLectura=1",
                TodosUsuariosActivos = true,
                EnviarNavbar = true,
                ActorUsuarioID = actorUsuarioId > 0 ? actorUsuarioId : null,
                FechaEvento = datos.FechaCreacion
            };

            var insertadas = await PublicarAsync(evento);
            _logger.LogInformation(
                "OF_CREADA global publicada. SolicitudProduccionID={Id}; destinatarios nuevos={Insertadas}.",
                solicitudProduccionId,
                insertadas);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Fallo publicando OF_CREADA para SolicitudProduccionID={Id}. La OF ya estaba confirmada y NO se revierte.",
                solicitudProduccionId);
        }
    }

    public async Task<int> PublicarAsync(NotificacionEvento evento)
    {
        ArgumentNullException.ThrowIfNull(evento);
        if (!evento.EnviarNavbar)
            return 0;

        var codigoEvento = Recortar(evento.CodigoEvento?.Trim() ?? string.Empty, 80);
        var tipo = Recortar(evento.Tipo?.Trim() ?? string.Empty, 30);
        var titulo = Recortar(evento.Titulo?.Trim() ?? string.Empty, 200);
        var mensaje = evento.Mensaje == null ? null : Recortar(evento.Mensaje.Trim(), 500);
        var tablaOrigen = Recortar(evento.TablaOrigen?.Trim() ?? string.Empty, 40);
        var urlDestino = NormalizarRutaLocal(evento.UrlDestino);

        if (string.IsNullOrWhiteSpace(codigoEvento)) throw new InvalidOperationException("CodigoEvento es obligatorio.");
        if (string.IsNullOrWhiteSpace(tipo)) throw new InvalidOperationException("Tipo es obligatorio.");
        if (string.IsNullOrWhiteSpace(titulo)) throw new InvalidOperationException("Titulo es obligatorio.");
        if (string.IsNullOrWhiteSpace(tablaOrigen)) throw new InvalidOperationException("TablaOrigen es obligatoria.");
        if (evento.IdOrigen <= 0) throw new InvalidOperationException("IdOrigen debe ser mayor que cero.");

        var destinatarios = await ObtenerDestinatariosAsync(evento);
        if (destinatarios.Count == 0)
        {
            _logger.LogWarning("Evento {CodigoEvento}/{IdOrigen} sin destinatarios internos.", codigoEvento, evento.IdOrigen);
            return 0;
        }

        var ahora = DateTime.Now;
        var expira = ahora.AddDays(30);
        var insertadas = 0;

        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);

        try
        {
            const string sql = """
IF NOT EXISTS
(
    SELECT 1
    FROM dbo.Notificaciones WITH(UPDLOCK,HOLDLOCK)
    WHERE CodigoEvento=@CodigoEvento
      AND IdOrigen=@IdOrigen
      AND UsuarioId=@UsuarioID
      AND FechaEliminacion IS NULL
)
BEGIN
    INSERT dbo.Notificaciones
    (
        Tipo,Titulo,Mensaje,IdOrigen,TablaOrigen,
        UsuarioId,EmpresaId,FechaCreacion,FechaExpiracion,
        EsLeida,FechaEliminacion,EsArchivada,CodigoEvento,UrlDestino
    )
    VALUES
    (
        @Tipo,@Titulo,@Mensaje,@IdOrigen,@TablaOrigen,
        @UsuarioID,NULL,@Ahora,@Expira,
        0,NULL,0,@CodigoEvento,@UrlDestino
    );
    SELECT CAST(1 AS int);
END
ELSE
BEGIN
    SELECT CAST(0 AS int);
END;
""";

            foreach (var usuarioId in destinatarios)
            {
                await using var cmd = new SqlCommand(sql, cn, tx);
                cmd.Parameters.Add("@Tipo", SqlDbType.NVarChar, 30).Value = tipo;
                cmd.Parameters.Add("@Titulo", SqlDbType.NVarChar, 200).Value = titulo;
                cmd.Parameters.Add("@Mensaje", SqlDbType.NVarChar, 500).Value = (object?)mensaje ?? DBNull.Value;
                cmd.Parameters.Add("@IdOrigen", SqlDbType.Int).Value = evento.IdOrigen;
                cmd.Parameters.Add("@TablaOrigen", SqlDbType.NVarChar, 40).Value = tablaOrigen;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                cmd.Parameters.Add("@Ahora", SqlDbType.DateTime).Value = ahora;
                cmd.Parameters.Add("@Expira", SqlDbType.DateTime).Value = expira;
                cmd.Parameters.Add("@CodigoEvento", SqlDbType.NVarChar, 80).Value = codigoEvento;
                cmd.Parameters.Add("@UrlDestino", SqlDbType.NVarChar, 500).Value = (object?)urlDestino ?? DBNull.Value;

                var value = await cmd.ExecuteScalarAsync();
                insertadas += value == null || value == DBNull.Value ? 0 : Convert.ToInt32(value);
            }

            await tx.CommitAsync();
        }
        catch
        {
            try { await tx.RollbackAsync(); } catch { }
            throw;
        }

        // NSQ_NOTIFICACIONES_CORREO_V10
        // Solo se envia correo cuando el evento produjo al menos una notificacion nueva.
        if (insertadas > 0)
        {
            try
            {
                var resultadoCorreo = await _correoErp.EnviarAUsuariosAsync(
                    destinatarios,
                    titulo,
                    mensaje ?? string.Empty,
                    urlDestino,
                    codigoEvento,
                    departamento: null);

                _logger.LogInformation(
                    "Correo evento {CodigoEvento}: encontrados={Encontrados}; enviados={Enviados}; bloqueados={Bloqueados}; errores={Errores}.",
                    codigoEvento,
                    resultadoCorreo.Encontrados,
                    resultadoCorreo.Enviados,
                    resultadoCorreo.FiltradosPorCandados,
                    resultadoCorreo.Errores);
            }
            catch (Exception exCorreo)
            {
                _logger.LogError(
                    exCorreo,
                    "El evento interno {CodigoEvento}/{IdOrigen} se guardo, pero fallo su correo.",
                    codigoEvento,
                    evento.IdOrigen);
            }
        }

        return insertadas;
    }

    private async Task<List<int>> ObtenerDestinatariosAsync(NotificacionEvento evento)
    {
        var departamentos = (evento.DepartamentosDestinoIds ?? Array.Empty<int>())
            .Where(x => x > 0).Distinct().ToHashSet();
        var usuariosExplicitos = (evento.UsuariosDestinoIds ?? Array.Empty<int>())
            .Where(x => x > 0).Distinct().ToHashSet();

        if (!evento.TodosUsuariosActivos && departamentos.Count == 0 && usuariosExplicitos.Count == 0)
            return new List<int>();

        const string sql = """
SELECT
    u.UsuarioID,
    u.DepartamentoID
FROM dbo.Usuarios u
WHERE ISNULL(u.Activo,1)=1
ORDER BY u.UsuarioID;
""";

        var salida = new List<int>();
        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        await using var rd = await cmd.ExecuteReaderAsync();

        while (await rd.ReadAsync())
        {
            var usuarioId = Convert.ToInt32(rd["UsuarioID"]);
            var departamentoId = rd["DepartamentoID"] == DBNull.Value
                ? (int?)null
                : Convert.ToInt32(rd["DepartamentoID"]);

            if (evento.TodosUsuariosActivos
                || usuariosExplicitos.Contains(usuarioId)
                || (departamentoId.HasValue && departamentos.Contains(departamentoId.Value)))
            {
                salida.Add(usuarioId);
            }
        }

        return salida.Distinct().OrderBy(x => x).ToList();
    }

    private async Task<OfCreadaDatos?> ObtenerOfCreadaAsync(int solicitudProduccionId)
    {
        const string sql = """
SELECT TOP(1)
    s.SolicitudProduccionID,
    COALESCE(NULLIF(LTRIM(RTRIM(s.NumeroOFRecibida)),N''),NULLIF(LTRIM(RTRIM(s.FolioSolicitud)),N''),CONCAT(N'#',s.SolicitudProduccionID)) AS NumeroOF,
    COALESCE(NULLIF(LTRIM(RTRIM(c.Nombre)),N''),NULLIF(LTRIM(RTRIM(s.ClienteNombre)),N''),N'') AS Cliente,
    ISNULL(t.TotalPiezas,0) AS TotalPiezas,
    ISNULL(t.TotalRenglones,0) AS TotalRenglones,
    ISNULL(det.NumeroParte,N'') AS NumeroParte,
    ISNULL(det.ReferenciaSAP,N'') AS ReferenciaSAP,
    ISNULL(det.DescripcionParte,N'') AS DescripcionParte,
    LTRIM(RTRIM(CONCAT(ISNULL(pa.Nombre,N''),N' ',ISNULL(pa.ApellidoPaterno,N''),N' ',ISNULL(pa.ApellidoMaterno,N'')))) AS Actor,
    s.FechaCreacion
FROM dbo.SolicitudesProduccion s
LEFT JOIN dbo.ERP_Clientes c ON c.ClienteID=s.ClienteID
LEFT JOIN dbo.Usuarios ua ON ua.UsuarioID=s.UsuarioCreacionID
LEFT JOIN dbo.Persona pa ON pa.PersonaID=ua.PersonaID
OUTER APPLY
(
    SELECT COUNT(*) AS TotalRenglones,SUM(CONVERT(bigint,ISNULL(d.CantidadPiezas,0))) AS TotalPiezas
    FROM dbo.SolicitudesProduccionDetalle d
    WHERE d.SolicitudProduccionID=s.SolicitudProduccionID AND ISNULL(d.Activo,1)=1
) t
OUTER APPLY
(
    SELECT TOP(1)
        COALESCE(NULLIF(LTRIM(RTRIM(p.NumeroParte)),N''),NULLIF(LTRIM(RTRIM(d.ReferenciaSAP)),N''),N'') AS NumeroParte,
        ISNULL(NULLIF(LTRIM(RTRIM(d.ReferenciaSAP)),N''),N'') AS ReferenciaSAP,
        ISNULL(NULLIF(LTRIM(RTRIM(d.DesignacionDescripcionSAP)),N''),N'') AS DescripcionParte
    FROM dbo.SolicitudesProduccionDetalle d
    LEFT JOIN dbo.ERP_Partes p ON p.ParteID=d.ParteID
    WHERE d.SolicitudProduccionID=s.SolicitudProduccionID AND ISNULL(d.Activo,1)=1
    ORDER BY d.Renglon,d.SolicitudProduccionDetalleID
) det
WHERE s.SolicitudProduccionID=@SolicitudProduccionID
  AND ISNULL(s.Activo,1)=1;
""";

        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = solicitudProduccionId;
        await using var rd = await cmd.ExecuteReaderAsync();
        if (!await rd.ReadAsync()) return null;

        var totalPiezas64 = rd["TotalPiezas"] == DBNull.Value ? 0L : Convert.ToInt64(rd["TotalPiezas"]);
        var totalPiezas = totalPiezas64 > int.MaxValue ? int.MaxValue : Convert.ToInt32(totalPiezas64);

        return new OfCreadaDatos(
            Convert.ToInt32(rd["SolicitudProduccionID"]),
            rd["NumeroOF"]?.ToString()?.Trim() ?? string.Empty,
            rd["Cliente"]?.ToString()?.Trim() ?? string.Empty,
            rd["NumeroParte"]?.ToString()?.Trim() ?? string.Empty,
            rd["ReferenciaSAP"]?.ToString()?.Trim() ?? string.Empty,
            rd["DescripcionParte"]?.ToString()?.Trim() ?? string.Empty,
            totalPiezas,
            rd["TotalRenglones"] == DBNull.Value ? 0 : Convert.ToInt32(rd["TotalRenglones"]),
            rd["Actor"]?.ToString()?.Trim() ?? string.Empty,
            Convert.ToDateTime(rd["FechaCreacion"]));
    }

    private static string? NormalizarRutaLocal(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var value = url.Trim();
        if (!value.StartsWith('/') || value.StartsWith("//", StringComparison.Ordinal))
            throw new InvalidOperationException("UrlDestino debe ser una ruta local del ERP que comience con /.");
        return Recortar(value, 500);
    }

    private static string Texto(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string Recortar(string value, int max)
    {
        value ??= string.Empty;
        return value.Length <= max ? value : value[..max];
    }
}
