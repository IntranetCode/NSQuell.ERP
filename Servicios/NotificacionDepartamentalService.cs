// NSQ_NOTIFICACIONES_DEPARTAMENTALES_V1_1
using Microsoft.Data.SqlClient;
using System.Data;
using System.Net;

namespace ERP.NSQuell.Servicios;

public sealed class NotificacionDepartamentalService
{
    private readonly string _connectionString;
    private readonly ServicioNotificaciones _correo;
    private readonly ILogger<NotificacionDepartamentalService> _logger;

    // MODO PRUEBAS TEMPORAL: ningún correo departamental sale a usuarios reales.
    private const string CorreoPruebas = "sistemas.gq@nsquell.com.mx";

    public NotificacionDepartamentalService(
        IConfiguration configuration,
        ServicioNotificaciones correo,
        ILogger<NotificacionDepartamentalService> logger)
    {
        _connectionString =
            configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Falta ConnectionStrings:DefaultConnection.");

        _correo = correo;
        _logger = logger;
    }

    private sealed record Destinatario(
        int UsuarioID,
        int PersonaID,
        string Correo);

    public async Task<string?> ResolverAreaAsync(string? controller)
    {
        controller = controller?.Trim();

        if (string.IsNullOrWhiteSpace(controller))
            return null;

        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();

        const string sql = """
SELECT TOP(1)
    mg.Nombre
FROM dbo.SubMenus sm
INNER JOIN dbo.Menus m
    ON m.MenuID=sm.MenuID
INNER JOIN dbo.MenuGrupo mg
    ON mg.MenuGrupoID=m.MenuGrupoID
WHERE ISNULL(sm.Activo,1)=1
  AND ISNULL(m.Activo,1)=1
  AND ISNULL(mg.Activo,1)=1
  AND
  (
      REPLACE(LTRIM(RTRIM(ISNULL(sm.UrlEnlace,''))),'~','')
          LIKE @Prefijo
      OR
      REPLACE(LTRIM(RTRIM(ISNULL(sm.UrlEnlace,''))),'~','')
          = @Raiz
  )
ORDER BY
    CASE
        WHEN REPLACE(LTRIM(RTRIM(ISNULL(sm.UrlEnlace,''))),'~','')
             LIKE @Prefijo
        THEN 0 ELSE 1
    END,
    sm.SubMenuID;
""";

        await using var cmd = new SqlCommand(sql,cn);
        cmd.Parameters.Add("@Prefijo",SqlDbType.VarChar,500).Value =
            $"/{controller}/%";
        cmd.Parameters.Add("@Raiz",SqlDbType.VarChar,500).Value =
            $"/{controller}";

        var value = await cmd.ExecuteScalarAsync();

        if (value != null && value != DBNull.Value)
        {
            var area = value.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(area))
                return area;
        }

        // Fallback únicamente si el catálogo de navegación no tiene
        // una URL que permita resolver el dueño del controller.
        if (controller.StartsWith(
                "Planeacion",
                StringComparison.OrdinalIgnoreCase))
            return "Planeación";

        if (controller.StartsWith(
                "Produccion",
                StringComparison.OrdinalIgnoreCase)
            || controller.StartsWith(
                "AgendaOperativa",
                StringComparison.OrdinalIgnoreCase))
            return "Producción";

        if (controller.StartsWith(
                "Calidad",
                StringComparison.OrdinalIgnoreCase)
            || controller.StartsWith(
                "GP12",
                StringComparison.OrdinalIgnoreCase))
            return "Calidad";

        if (controller.StartsWith(
                "Almacen",
                StringComparison.OrdinalIgnoreCase))
            return "Almacén";

        if (controller.StartsWith(
                "Logistica",
                StringComparison.OrdinalIgnoreCase))
            return "Logística";

        if (controller.StartsWith(
                "Compras",
                StringComparison.OrdinalIgnoreCase))
            return "Compras";

        if (controller.StartsWith(
                "Mantenimiento",
                StringComparison.OrdinalIgnoreCase))
            return "Mantenimiento";

        if (controller.StartsWith(
                "RRHH",
                StringComparison.OrdinalIgnoreCase)
            || controller.StartsWith(
                "RecursosHumanos",
                StringComparison.OrdinalIgnoreCase))
            return "Recursos Humanos";

        return null;
    }

    public async Task NotificarAsync(
        string area,
        string controller,
        string action,
        int idOrigen,
        string? actor)
    {
        area = (area ?? string.Empty).Trim();
        controller = (controller ?? string.Empty).Trim();
        action = (action ?? string.Empty).Trim();
        actor = string.IsNullOrWhiteSpace(actor)
            ? "Usuario ERP"
            : actor.Trim();

        if (string.IsNullOrWhiteSpace(area))
            return;

        var destinatarios =
            await ObtenerDestinatariosAsync(area);

        if (destinatarios.Count == 0)
        {
            _logger.LogWarning(
                "Sin destinatarios para notificación departamental {Area}.",
                area);
            return;
        }

        var titulo = Recortar(
            $"{area}: {Humanizar(action)}",
            200);

        var mensaje = Recortar(
            $"{actor} ejecutó {Humanizar(action)} " +
            $"en el módulo {area}.",
            500);

        var tipo = Recortar("EVENTO_DEPARTAMENTO",30);
        var tablaOrigen = Recortar(controller,40);
        var ahora = DateTime.Now;
        var expira = ahora.AddDays(30);

        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();

        await using var tx =
            (SqlTransaction)await cn.BeginTransactionAsync();

        try
        {
            const string insertSql = """
IF NOT EXISTS
(
    SELECT 1
    FROM dbo.Notificaciones n WITH(UPDLOCK,HOLDLOCK)
    WHERE n.UsuarioId=@UsuarioID
      AND n.Tipo=@Tipo
      AND n.TablaOrigen=@TablaOrigen
      AND n.IdOrigen=@IdOrigen
      AND n.Titulo=@Titulo
      AND n.FechaCreacion>=DATEADD(SECOND,-5,@Ahora)
      AND n.FechaEliminacion IS NULL
)
BEGIN
    INSERT dbo.Notificaciones
    (
        Tipo,
        Titulo,
        Mensaje,
        IdOrigen,
        TablaOrigen,
        UsuarioId,
        EmpresaId,
        FechaCreacion,
        FechaExpiracion,
        EsLeida,
        FechaEliminacion,
        EsArchivada
    )
    VALUES
    (
        @Tipo,
        @Titulo,
        @Mensaje,
        @IdOrigen,
        @TablaOrigen,
        @UsuarioID,
        NULL,
        @Ahora,
        @Expira,
        0,
        NULL,
        0
    );
END;
""";

            foreach (var d in destinatarios)
            {
                await using var cmd =
                    new SqlCommand(insertSql,cn,tx);

                cmd.Parameters.Add(
                    "@Tipo",
                    SqlDbType.NVarChar,
                    30).Value = tipo;

                cmd.Parameters.Add(
                    "@Titulo",
                    SqlDbType.NVarChar,
                    200).Value = titulo;

                cmd.Parameters.Add(
                    "@Mensaje",
                    SqlDbType.NVarChar,
                    500).Value = mensaje;

                cmd.Parameters.Add(
                    "@IdOrigen",
                    SqlDbType.Int).Value = idOrigen;

                cmd.Parameters.Add(
                    "@TablaOrigen",
                    SqlDbType.NVarChar,
                    40).Value = tablaOrigen;

                cmd.Parameters.Add(
                    "@UsuarioID",
                    SqlDbType.Int).Value = d.UsuarioID;

                cmd.Parameters.Add(
                    "@Ahora",
                    SqlDbType.DateTime).Value = ahora;

                cmd.Parameters.Add(
                    "@Expira",
                    SqlDbType.DateTime).Value = expira;

                await cmd.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
        }
        catch
        {
            try
            {
                await tx.RollbackAsync();
            }
            catch
            {
            }

            throw;
        }

        var html =
            "<div style='font-family:Segoe UI,Arial,sans-serif'>" +
            "<h2 style='margin:0 0 12px'>NS Quell ERP</h2>" +
            "<p><strong>MODO PRUEBAS</strong></p>" +
            "<p><strong>Área destino real:</strong> " +
            WebUtility.HtmlEncode(area) +
            "</p>" +
            "<p>" +
            WebUtility.HtmlEncode(mensaje) +
            "</p>" +
            "<p style='color:#64748b;font-size:12px'>" +
            WebUtility.HtmlEncode(
                ahora.ToString("dd/MM/yyyy HH:mm")) +
            "</p></div>";

        // NSQ_NOTIFICACIONES_DEPARTAMENTALES_V1_1_TEST_ROUTE
        // Durante pruebas, TODO correo departamental se redirige a Sistemas.
        // Las notificaciones del navbar sí siguen creándose para el área real.
        await _correo.EnviarCorreoDirectoAsync(
            CorreoPruebas,
            "[PRUEBA] " + titulo,
            html);

        _logger.LogInformation(
            "Notificación {Area}: {Usuarios} usuarios ERP. " +
            "Correo departamental redirigido temporalmente a {CorreoPruebas}.",
            area,
            destinatarios.Count,
            CorreoPruebas);
    }

    private async Task<List<Destinatario>>
        ObtenerDestinatariosAsync(string area)
    {
        var salida = new List<Destinatario>();

        await using var cn =
            new SqlConnection(_connectionString);

        await cn.OpenAsync();

        const string sql = """
SELECT DISTINCT
    u.UsuarioID,
    u.PersonaID,
    LTRIM(RTRIM(p.Correo)) AS Correo
FROM dbo.Usuarios u
INNER JOIN dbo.Persona p
    ON p.PersonaID=u.PersonaID
LEFT JOIN dbo.Departamentos d
    ON d.DepartamentoID=u.DepartamentoID
WHERE ISNULL(u.Activo,1)=1
  AND ISNULL(p.EsColaboradorActivo,1)=1
  AND NULLIF(LTRIM(RTRIM(ISNULL(p.Correo,''))),'') IS NOT NULL
  AND
  (
      ISNULL(d.NombreDepartamento,'')
          COLLATE Latin1_General_100_CI_AI
          = @Area COLLATE Latin1_General_100_CI_AI
      OR EXISTS
      (
          SELECT 1
          FROM dbo.fn_PermisosEfectivosUsuario(u.UsuarioID) pe
          INNER JOIN dbo.SubMenus sm
              ON sm.SubMenuID=pe.SubMenuID
          INNER JOIN dbo.Menus m
              ON m.MenuID=sm.MenuID
          INNER JOIN dbo.MenuGrupo mg
              ON mg.MenuGrupoID=m.MenuGrupoID
          WHERE pe.TienePermiso=1
            AND ISNULL(sm.Activo,1)=1
            AND ISNULL(m.Activo,1)=1
            AND ISNULL(mg.Activo,1)=1
            AND mg.Nombre COLLATE Latin1_General_100_CI_AI
                = @Area COLLATE Latin1_General_100_CI_AI
      )
  )
ORDER BY u.UsuarioID;
""";

        await using var cmd =
            new SqlCommand(sql,cn);

        cmd.Parameters.Add(
            "@Area",
            SqlDbType.NVarChar,
            150).Value = area;

        await using var rd =
            await cmd.ExecuteReaderAsync();

        while (await rd.ReadAsync())
        {
            salida.Add(
                new Destinatario(
                    Convert.ToInt32(rd["UsuarioID"]),
                    Convert.ToInt32(rd["PersonaID"]),
                    rd["Correo"]?.ToString()?.Trim()
                        ?? string.Empty));
        }

        return salida;
    }

    private static string Humanizar(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "actualización";

        var s = value.Trim();
        var chars = new List<char>(s.Length + 8);

        for (var i=0;i<s.Length;i++)
        {
            if (
                i>0
                && char.IsUpper(s[i])
                && !char.IsUpper(s[i-1])
            )
            {
                chars.Add(' ');
            }

            chars.Add(s[i]);
        }

        return new string(chars.ToArray());
    }

    private static string Recortar(string value,int max)
    {
        value ??= string.Empty;
        return value.Length<=max
            ? value
            : value[..max];
    }
}