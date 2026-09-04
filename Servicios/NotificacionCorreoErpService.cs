using System.Net;
using ERP.NSQuell.Models.Opciones;
using Microsoft.Extensions.Options;

namespace ERP.NSQuell.Servicios;

/// <summary>
/// Adaptador único entre las notificaciones internas del ERP y el servicio SMTP existente.
/// Reutiliza ServicioNotificaciones, por lo que conserva Habilitado/SoloPruebas/ListaBlanca.
/// </summary>
public sealed class NotificacionCorreoErpService
{
    private readonly ServicioNotificaciones _correo;
    private readonly CorreoOpciones _opciones;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<NotificacionCorreoErpService> _logger;

    public NotificacionCorreoErpService(
        ServicioNotificaciones correo,
        IOptions<CorreoOpciones> opciones,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<NotificacionCorreoErpService> logger)
    {
        _correo = correo;
        _opciones = opciones.Value;
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
    }

    public async Task<ServicioNotificaciones.ResultadoEnvio> EnviarAUsuariosAsync(
        IEnumerable<int> usuarioIds,
        string titulo,
        string mensaje,
        string? urlDestino,
        string? codigoEvento = null,
        string? departamento = null,
        bool urgente = false,
        string? textoBoton = null)
    {
        var ids = (usuarioIds ?? Enumerable.Empty<int>())
            .Where(x => x > 0)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        if (ids.Count == 0)
            return new ServicioNotificaciones.ResultadoEnvio();

        var asunto = urgente
            ? $"[URGENTE][NS QUELL] {titulo}"
            : $"[NS QUELL] {titulo}";

        var urlAbsoluta = ConstruirUrlAbsoluta(urlDestino);
        var boton = string.IsNullOrWhiteSpace(textoBoton)
            ? ResolverTextoBoton(codigoEvento)
            : textoBoton.Trim();

        var html = ConstruirHtml(
            titulo,
            mensaje,
            departamento,
            codigoEvento,
            urlAbsoluta,
            boton,
            urgente);

        try
        {
            /*
             * En SoloPruebas NO intentamos mandar a los usuarios reales del
             * departamento porque el servicio SMTP los bloquearía por whitelist.
             * En su lugar se manda una copia de prueba a ListaBlanca.
             * En producción (SoloPruebas=false) sí se usan los UsuarioID reales.
             */
            if (_opciones.SoloPruebas)
            {
                var whitelist = (_opciones.ListaBlanca ?? string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (_opciones.MaxDestinatariosEnPrueba > 0)
                    whitelist = whitelist.Take(_opciones.MaxDestinatariosEnPrueba).ToList();

                var resultadoPrueba = new ServicioNotificaciones.ResultadoEnvio
                {
                    Encontrados = ids.Count
                };

                if (!_opciones.Habilitado)
                {
                    resultadoPrueba.FiltradosPorCandados = ids.Count;
                    resultadoPrueba.Mensajes.Add("Correo deshabilitado globalmente.");
                    return resultadoPrueba;
                }

                if (whitelist.Count == 0)
                {
                    resultadoPrueba.FiltradosPorCandados = ids.Count;
                    resultadoPrueba.Mensajes.Add("SoloPruebas activo pero ListaBlanca está vacía.");
                    return resultadoPrueba;
                }

                foreach (var correoPrueba in whitelist)
                {
                    await _correo.EnviarCorreoDirectoAsync(
                        correoPrueba,
                        "[PRUEBA] " + asunto,
                        html);
                    resultadoPrueba.Enviados++;
                }

                resultadoPrueba.Mensajes.Add(
                    $"SoloPruebas: evento dirigido a {ids.Count} usuario(s); copia enviada a {whitelist.Count} correo(s) de ListaBlanca.");

                return resultadoPrueba;
            }

            // Producción real: UsuarioID -> PersonaID -> Persona.Correo, BCC por lote.
            return await _correo.EnviarCursosAUsuariosAsync(
                ids,
                asunto,
                html,
                batchSize: 40);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Fallo enviando correo ERP para {CodigoEvento} a {Usuarios} usuario(s).",
                codigoEvento ?? "SIN_CODIGO",
                ids.Count);

            return new ServicioNotificaciones.ResultadoEnvio
            {
                Encontrados = ids.Count,
                Errores = 1,
                Mensajes = new List<string> { ex.Message }
            };
        }
    }

    public async Task<List<int>> ObtenerUsuariosDepartamentoAsync(string departamento)
    {
        departamento = (departamento ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(departamento))
            return new List<int>();

        var cs = _configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:DefaultConnection.");

        const string sql = """
SELECT DISTINCT
    u.UsuarioID
FROM dbo.Usuarios u
INNER JOIN dbo.Departamentos d
    ON d.DepartamentoID=u.DepartamentoID
INNER JOIN dbo.Persona p
    ON p.PersonaID=u.PersonaID
WHERE ISNULL(u.Activo,1)=1
  AND ISNULL(d.Activo,1)=1
  AND p.Correo IS NOT NULL
  AND LTRIM(RTRIM(p.Correo))<>N''
  AND d.NombreDepartamento COLLATE Latin1_General_100_CI_AI
      = @Departamento COLLATE Latin1_General_100_CI_AI
ORDER BY u.UsuarioID;
""";

        var salida = new List<int>();

        await using var cn = new Microsoft.Data.SqlClient.SqlConnection(cs);
        await cn.OpenAsync();

        await using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, cn);
        cmd.Parameters.Add("@Departamento", System.Data.SqlDbType.NVarChar, 150).Value = departamento;

        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
            salida.Add(Convert.ToInt32(rd["UsuarioID"]));

        return salida;
    }

    private string? ConstruirUrlAbsoluta(string? urlDestino)
    {
        if (string.IsNullOrWhiteSpace(urlDestino))
            return null;

        var destino = urlDestino.Trim();

        if (Uri.TryCreate(destino, UriKind.Absolute, out var absoluta)
            && (absoluta.Scheme == Uri.UriSchemeHttp || absoluta.Scheme == Uri.UriSchemeHttps))
        {
            return absoluta.ToString();
        }

        if (!destino.StartsWith('/') || destino.StartsWith("//", StringComparison.Ordinal))
            return null;

        var baseUrl =
            Environment.GetEnvironmentVariable("NSQ_ERP_BASE_URL")
            ?? _configuration["CorreoNotificaciones:BaseUrlERP"];

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = _environment.IsDevelopment()
                ? "http://localhost:5053"
                : "https://erp.quell.nsgroup.com.mx";
        }

        return baseUrl.TrimEnd('/') + destino;
    }

    private static string ResolverTextoBoton(string? codigoEvento)
    {
        var codigo = (codigoEvento ?? string.Empty).Trim().ToUpperInvariant();

        if (codigo == "OF_CREADA") return "Consultar OF";
        if (codigo.StartsWith("ALMACEN_MP_")) return "Ver movimiento MP";
        if (codigo.StartsWith("ALMACEN_EMBALAJE_")) return "Ver movimiento de embalaje";
        if (codigo == "PLANEACION_REPROGRAMACION") return "Ver reprogramación";
        if (codigo.Contains("CALIDAD")) return "Atender en Calidad";
        if (codigo.Contains("GP12")) return "Abrir GP12";
        if (codigo.Contains("LOGISTICA") || codigo.Contains("EMBARQUE")) return "Ver embarque";

        return "Abrir en ERP";
    }

    private static string ConstruirHtml(
        string titulo,
        string mensaje,
        string? departamento,
        string? codigoEvento,
        string? urlAbsoluta,
        string textoBoton,
        bool urgente)
    {
        var tituloHtml = WebUtility.HtmlEncode(titulo ?? string.Empty);
        var mensajeHtml = WebUtility.HtmlEncode(mensaje ?? string.Empty)
            .Replace("\r\n", "<br>")
            .Replace("\n", "<br>");
        var departamentoHtml = WebUtility.HtmlEncode(departamento ?? string.Empty);
        var codigoHtml = WebUtility.HtmlEncode(codigoEvento ?? string.Empty);
        var botonHtml = WebUtility.HtmlEncode(textoBoton);
        var urlHtml = WebUtility.HtmlEncode(urlAbsoluta ?? string.Empty);

        var alerta = urgente
            ? "<div style=\"margin:0 0 16px;padding:12px 14px;border-radius:10px;background:#fff3cd;color:#7a4d00;font-weight:700\">Producción está detenida o en riesgo de detenerse. Atiende esta tarea cuanto antes.</div>"
            : string.Empty;

        var cta = string.IsNullOrWhiteSpace(urlAbsoluta)
            ? string.Empty
            : $"<p style=\"margin:22px 0 8px\"><a href=\"{urlHtml}\" style=\"display:inline-block;padding:12px 18px;border-radius:10px;background:#f47b20;color:#ffffff;text-decoration:none;font-weight:800\">{botonHtml}</a></p>";

        var departamentoFila = string.IsNullOrWhiteSpace(departamento)
            ? string.Empty
            : $"<div style=\"margin-top:10px;color:#64748b;font-size:13px\"><strong>Departamento responsable:</strong> {departamentoHtml}</div>";

        var codigoFila = string.IsNullOrWhiteSpace(codigoEvento)
            ? string.Empty
            : $"<div style=\"margin-top:4px;color:#94a3b8;font-size:12px\">Evento: {codigoHtml}</div>";

        return $"""
<!doctype html>
<html>
<body style="margin:0;padding:24px;background:#f5f7fb;font-family:Segoe UI,Arial,sans-serif;color:#172033">
  <div style="max-width:680px;margin:0 auto;background:#ffffff;border:1px solid #e5e7eb;border-radius:16px;overflow:hidden">
    <div style="padding:18px 22px;background:#0f172a;color:#ffffff">
      <div style="font-size:13px;color:#ffb86b;font-weight:800">NS QUELL ERP</div>
      <div style="margin-top:4px;font-size:22px;font-weight:800">{tituloHtml}</div>
    </div>
    <div style="padding:22px">
      {alerta}
      <div style="font-size:15px;line-height:1.55">{mensajeHtml}</div>
      {departamentoFila}
      {codigoFila}
      {cta}
      <div style="margin-top:22px;padding-top:14px;border-top:1px solid #e5e7eb;color:#94a3b8;font-size:12px">
        Notificación generada automáticamente por NS Quell ERP.
      </div>
    </div>
  </div>
</body>
</html>
""";
    }
}