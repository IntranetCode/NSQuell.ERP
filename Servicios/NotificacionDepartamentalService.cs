// NSQ_NOTIFICACIONES_V3E_DEPARTAMENTOS
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Servicios;

public sealed class NotificacionDepartamentalService
{
    private readonly string _connectionString;
    private readonly ILogger<NotificacionDepartamentalService> _logger;

    public NotificacionDepartamentalService(
        IConfiguration configuration,
        ILogger<NotificacionDepartamentalService> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:DefaultConnection.");
        _logger = logger;
    }

    public async Task<string?> ResolverAreaAsync(string? controller)
    {
        controller = controller?.Trim();
        if (string.IsNullOrWhiteSpace(controller)) return null;

        if (controller.StartsWith("Planeacion", StringComparison.OrdinalIgnoreCase)) return "Planeación";
        if (controller.StartsWith("Produccion", StringComparison.OrdinalIgnoreCase) || controller.StartsWith("AgendaOperativa", StringComparison.OrdinalIgnoreCase)) return "Producción";
        if (controller.StartsWith("Calidad", StringComparison.OrdinalIgnoreCase)) return "Calidad";
        if (controller.StartsWith("GP12", StringComparison.OrdinalIgnoreCase)) return "GP12";
        if (controller.StartsWith("Almacen", StringComparison.OrdinalIgnoreCase)) return "Almacén";
        if (controller.StartsWith("Logistica", StringComparison.OrdinalIgnoreCase)) return "Logística";
        if (controller.StartsWith("Compras", StringComparison.OrdinalIgnoreCase)) return "Compras";
        if (controller.StartsWith("Mantenimiento", StringComparison.OrdinalIgnoreCase)) return "Mantenimiento";
        if (controller.StartsWith("RRHH", StringComparison.OrdinalIgnoreCase) || controller.StartsWith("RecursosHumanos", StringComparison.OrdinalIgnoreCase)) return "Recursos Humanos";

        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();
        const string sql = """
SELECT TOP(1) mg.Nombre
FROM dbo.SubMenus sm
INNER JOIN dbo.Menus m ON m.MenuID=sm.MenuID
INNER JOIN dbo.MenuGrupo mg ON mg.MenuGrupoID=m.MenuGrupoID
WHERE ISNULL(sm.Activo,1)=1 AND ISNULL(m.Activo,1)=1 AND ISNULL(mg.Activo,1)=1
  AND (REPLACE(LTRIM(RTRIM(ISNULL(sm.UrlEnlace,''))),'~','') LIKE @Prefijo
       OR REPLACE(LTRIM(RTRIM(ISNULL(sm.UrlEnlace,''))),'~','')=@Raiz)
ORDER BY CASE WHEN REPLACE(LTRIM(RTRIM(ISNULL(sm.UrlEnlace,''))),'~','') LIKE @Prefijo THEN 0 ELSE 1 END,sm.SubMenuID;
""";
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@Prefijo", SqlDbType.VarChar, 500).Value = $"/{controller}/%";
        cmd.Parameters.Add("@Raiz", SqlDbType.VarChar, 500).Value = $"/{controller}";
        var value = await cmd.ExecuteScalarAsync();
        return value == null || value == DBNull.Value ? null : value.ToString()?.Trim();
    }

    public async Task NotificarAsync(
        string area,
        string controller,
        string action,
        int idOrigen,
        string? actor,
        int? solicitudProduccionId)
    {
        area = (area ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(area)) return;

        controller = (controller ?? string.Empty).Trim();
        action = (action ?? string.Empty).Trim();
        actor = string.IsNullOrWhiteSpace(actor) ? "Usuario ERP" : actor.Trim();
        var destinatarios = await ObtenerDestinatariosDepartamentoAsync(area);
        if (destinatarios.Count == 0)
        {
            _logger.LogWarning("Sin usuarios activos asignados al Departamento {Area}.", area);
            return;
        }

        var titulo = Recortar($"{area}: {Humanizar(action)}", 200);
        var mensaje = Recortar($"{actor} ejecutó {Humanizar(action)} en el módulo {area}.", 500);
        var tipo = "EVENTO_DEPARTAMENTO";
        var tablaOrigen = Recortar(controller, 40);
        var ahora = DateTime.Now;
        var expira = ahora.AddDays(30);
        var urlDestino = solicitudProduccionId.HasValue && solicitudProduccionId.Value > 0
            ? $"/SolicitudesProduccion/Detalle/{solicitudProduccionId.Value}?soloLectura=1"
            : null;

        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            const string insertSql = """
IF NOT EXISTS
(
    SELECT 1
    FROM dbo.Notificaciones WITH(UPDLOCK,HOLDLOCK)
    WHERE UsuarioId=@UsuarioID AND Tipo=@Tipo AND TablaOrigen=@TablaOrigen
      AND IdOrigen=@IdOrigen AND Titulo=@Titulo
      AND FechaCreacion>=DATEADD(SECOND,-5,@Ahora) AND FechaEliminacion IS NULL
)
BEGIN
    INSERT dbo.Notificaciones
    (
        Tipo,Titulo,Mensaje,IdOrigen,TablaOrigen,UsuarioId,EmpresaId,
        FechaCreacion,FechaExpiracion,EsLeida,FechaEliminacion,EsArchivada,UrlDestino
    )
    VALUES
    (
        @Tipo,@Titulo,@Mensaje,@IdOrigen,@TablaOrigen,@UsuarioID,NULL,
        @Ahora,@Expira,0,NULL,0,@UrlDestino
    );
END;
""";
            foreach (var usuarioId in destinatarios)
            {
                await using var cmd = new SqlCommand(insertSql, cn, tx);
                cmd.Parameters.Add("@Tipo", SqlDbType.NVarChar, 30).Value = tipo;
                cmd.Parameters.Add("@Titulo", SqlDbType.NVarChar, 200).Value = titulo;
                cmd.Parameters.Add("@Mensaje", SqlDbType.NVarChar, 500).Value = mensaje;
                cmd.Parameters.Add("@IdOrigen", SqlDbType.Int).Value = Math.Max(0, idOrigen);
                cmd.Parameters.Add("@TablaOrigen", SqlDbType.NVarChar, 40).Value = tablaOrigen;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                cmd.Parameters.Add("@Ahora", SqlDbType.DateTime).Value = ahora;
                cmd.Parameters.Add("@Expira", SqlDbType.DateTime).Value = expira;
                cmd.Parameters.Add("@UrlDestino", SqlDbType.NVarChar, 500).Value = (object?)urlDestino ?? DBNull.Value;
                await cmd.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
        }
        catch
        {
            try { await tx.RollbackAsync(); } catch { }
            throw;
        }
    }

    public async Task<int?> ResolverSolicitudProduccionIdAsync(string tipoReferencia, int referenciaId)
    {
        if (referenciaId <= 0 || string.IsNullOrWhiteSpace(tipoReferencia)) return null;
        tipoReferencia = tipoReferencia.Trim().ToUpperInvariant();

        string? sql = tipoReferencia switch
        {
            "PROGRAMA" => "SELECT SolicitudProduccionID FROM dbo.Planeacion_ProgramaProduccion WHERE ProgramaProduccionID=@ID AND SolicitudProduccionID IS NOT NULL;",
            "EJECUCION" => "SELECT SolicitudProduccionID FROM dbo.Produccion_Ejecucion WHERE EjecucionProduccionID=@ID AND SolicitudProduccionID IS NOT NULL;",
            "INSPECCION" => "SELECT SolicitudProduccionID FROM dbo.Calidad_Inspecciones WHERE InspeccionID=@ID AND SolicitudProduccionID IS NOT NULL;",
            "GP12" => "SELECT SolicitudProduccionID FROM dbo.GP12_Solicitudes WHERE SolicitudGP12ID=@ID AND SolicitudProduccionID IS NOT NULL;",
            "SCRAP" => "SELECT SolicitudProduccionID FROM dbo.AlmacenScrap_Registros WHERE ScrapRegistroID=@ID AND SolicitudProduccionID IS NOT NULL;",
            "EMBARQUE" => "SELECT CASE WHEN COUNT(DISTINCT SolicitudProduccionID)=1 THEN MIN(SolicitudProduccionID) ELSE NULL END FROM dbo.Logistica_EmbarqueDetalle WHERE EmbarqueID=@ID AND SolicitudProduccionID IS NOT NULL AND ISNULL(Activo,1)=1;",
            _ => null
        };
        if (sql == null) return null;

        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@ID", SqlDbType.Int).Value = referenciaId;
        var value = await cmd.ExecuteScalarAsync();
        return value == null || value == DBNull.Value ? null : Convert.ToInt32(value);
    }

    private async Task<List<int>> ObtenerDestinatariosDepartamentoAsync(string area)
    {
        const string sql = """
SELECT DISTINCT u.UsuarioID
FROM dbo.Usuarios u
INNER JOIN dbo.Departamentos d ON d.DepartamentoID=u.DepartamentoID
WHERE ISNULL(u.Activo,1)=1
  AND ISNULL(d.Activo,1)=1
  AND d.NombreDepartamento COLLATE Latin1_General_100_CI_AI = @Area COLLATE Latin1_General_100_CI_AI
ORDER BY u.UsuarioID;
""";
        var salida = new List<int>();
        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@Area", SqlDbType.NVarChar, 150).Value = area;
        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync()) salida.Add(Convert.ToInt32(rd["UsuarioID"]));
        return salida;
    }

    private static string Humanizar(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "actualización";
        var s = value.Trim();
        var chars = new List<char>(s.Length + 8);
        for (var i = 0; i < s.Length; i++)
        {
            if (i > 0 && char.IsUpper(s[i]) && !char.IsUpper(s[i - 1])) chars.Add(' ');
            chars.Add(s[i]);
        }
        return new string(chars.ToArray());
    }

    private static string Recortar(string value, int max)
    {
        value ??= string.Empty;
        return value.Length <= max ? value : value[..max];
    }
}
