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

        var entorno = ResolverEntornoErp();
        var prefijoEntorno = entorno == "TEST" ? "[TEST]" : string.Empty;

        var asunto = urgente
            ? $"{prefijoEntorno}[URGENTE][NS QUELL] {titulo}"
            : $"{prefijoEntorno}[NS QUELL] {titulo}";

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
            urgente,
            entorno);

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
        var baseUrl = ResolverBaseUrlErp();

        if (Uri.TryCreate(destino, UriKind.Absolute, out var absoluta)
            && (absoluta.Scheme == Uri.UriSchemeHttp || absoluta.Scheme == Uri.UriSchemeHttps))
        {
            if (absoluta.IsLoopback
                || absoluta.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || absoluta.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase))
            {
                return baseUrl + absoluta.PathAndQuery + absoluta.Fragment;
            }

            return absoluta.ToString();
        }

        if (!destino.StartsWith('/') || destino.StartsWith("//", StringComparison.Ordinal))
            return null;

        return baseUrl + destino;
    }

    private string ResolverBaseUrlErp()
    {
        var configurada =
            Environment.GetEnvironmentVariable("NSQ_ERP_BASE_URL")
            ?? _configuration["CorreoNotificaciones:BaseUrlERP"];

        if (!string.IsNullOrWhiteSpace(configurada)
            && Uri.TryCreate(configurada.Trim(), UriKind.Absolute, out var uriConfigurada)
            && (uriConfigurada.Scheme == Uri.UriSchemeHttps
                || uriConfigurada.Scheme == Uri.UriSchemeHttp))
        {
            return configurada.Trim().TrimEnd('/');
        }

        var cs =
            _configuration.GetConnectionString("DefaultConnection")
            ?? string.Empty;

        if (cs.Contains("ERP_PROD", StringComparison.OrdinalIgnoreCase))
            return "https://erp.quell.nsgroup.com.mx";

        if (cs.Contains("ERP_TEST", StringComparison.OrdinalIgnoreCase)
            || cs.Contains("INTRANET_DEV_DB", StringComparison.OrdinalIgnoreCase))
        {
            return "https://erpnsqt.nsgroup.com.mx";
        }

        return _environment.IsProduction()
            ? "https://erp.quell.nsgroup.com.mx"
            : "https://erpnsqt.nsgroup.com.mx";
    }

    private string ResolverEntornoErp()
    {
        var baseUrl = ResolverBaseUrlErp();

        return baseUrl.Contains(
            "erpnsqt.nsgroup.com.mx",
            StringComparison.OrdinalIgnoreCase)
                ? "TEST"
                : "PRODUCCION";
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
        bool urgente,
        string entorno)
    {
        var tituloHtml = WebUtility.HtmlEncode(titulo ?? string.Empty);
        var mensajeHtml = WebUtility.HtmlEncode(mensaje ?? string.Empty)
            .Replace("\r\n", "<br>")
            .Replace("\n", "<br>");

        var departamentoHtml =
            WebUtility.HtmlEncode(
                string.IsNullOrWhiteSpace(departamento)
                    ? "No especificado"
                    : departamento.Trim());

        var codigoHtml =
            WebUtility.HtmlEncode(
                string.IsNullOrWhiteSpace(codigoEvento)
                    ? "SIN_CODIGO"
                    : codigoEvento.Trim());

        var entornoHtml = WebUtility.HtmlEncode(entorno);
        var fechaHtml = WebUtility.HtmlEncode(
            DateTime.Now.ToString("dd/MM/yyyy HH:mm"));

        var botonHtml = WebUtility.HtmlEncode(textoBoton);
        var urlHtml = WebUtility.HtmlEncode(urlAbsoluta ?? string.Empty);

        var etiqueta = urgente
            ? "<span style=\"display:inline-block;padding:6px 10px;border-radius:999px;background:#fee2e2;color:#991b1b;font-size:11px;font-weight:800;letter-spacing:.5px\">ATENCION REQUERIDA</span>"
            : "<span style=\"display:inline-block;padding:6px 10px;border-radius:999px;background:#dbeafe;color:#1e40af;font-size:11px;font-weight:800;letter-spacing:.5px\">NOTIFICACION ERP</span>";

        var alerta = urgente
            ? """
              <tr>
                <td style="padding:0 28px 18px 28px">
                  <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0"
                         style="background:#fff7ed;border-left:4px solid #f97316;border-radius:8px">
                    <tr>
                      <td style="padding:13px 15px;color:#9a3412;font-size:14px;line-height:1.45;font-weight:700">
                        La operacion requiere atencion del departamento responsable.
                      </td>
                    </tr>
                  </table>
                </td>
              </tr>
              """
            : string.Empty;

        var cta = string.IsNullOrWhiteSpace(urlAbsoluta)
            ? """
              <tr>
                <td style="padding:4px 28px 22px 28px;color:#64748b;font-size:12px">
                  Este evento no tiene un acceso directo seguro. Ingresa al ERP desde tu menu habitual.
                </td>
              </tr>
              """
            : $"""
              <tr>
                <td style="padding:4px 28px 24px 28px">
                  <a href="{urlHtml}"
                     style="display:inline-block;background:#f47b20;color:#ffffff;text-decoration:none;font-size:14px;font-weight:800;padding:12px 18px;border-radius:8px">
                    {botonHtml}
                  </a>
                </td>
              </tr>
              """;

        return $"""
<!doctype html>
<html>
<head>
  <meta charset="utf-8">
  <meta name="color-scheme" content="light">
  <meta name="supported-color-schemes" content="light">
</head>
<body style="margin:0;padding:0;background:#eef2f7;font-family:Segoe UI,Arial,sans-serif;color:#172033">
  <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0"
         style="width:100%;background:#eef2f7;margin:0;padding:0">
    <tr>
      <td align="center" style="padding:28px 12px">
        <table role="presentation" width="680" cellspacing="0" cellpadding="0" border="0"
               style="width:100%;max-width:680px;background:#ffffff;border:1px solid #dbe2ea;border-radius:14px;overflow:hidden">

          <tr>
            <td style="background:#0b2341;padding:20px 28px;border-bottom:4px solid #f47b20">
              <div style="font-size:12px;font-weight:800;letter-spacing:1px;color:#fdba74">
                NS QUELL ERP
              </div>
              <div style="margin-top:10px">{etiqueta}</div>
              <div style="margin-top:10px;font-size:22px;line-height:1.25;font-weight:800;color:#ffffff">
                {tituloHtml}
              </div>
            </td>
          </tr>

          <tr>
            <td style="padding:24px 28px 18px 28px">
              <div style="font-size:15px;line-height:1.65;color:#334155">
                {mensajeHtml}
              </div>
            </td>
          </tr>

          {alerta}
          {cta}

          <tr>
            <td style="padding:0 28px 24px 28px">
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0"
                     style="background:#f8fafc;border:1px solid #e2e8f0;border-radius:10px">
                <tr>
                  <td style="padding:15px 16px">
                    <div style="font-size:13px;font-weight:800;color:#0f172a;margin-bottom:10px">
                      Detalles
                    </div>

                    <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0"
                           style="font-size:12px;color:#64748b">
                      <tr>
                        <td style="padding:3px 0;width:180px;font-weight:700;color:#475569">
                          Departamento responsable
                        </td>
                        <td style="padding:3px 0">{departamentoHtml}</td>
                      </tr>
                      <tr>
                        <td style="padding:3px 0;font-weight:700;color:#475569">
                          Evento
                        </td>
                        <td style="padding:3px 0">{codigoHtml}</td>
                      </tr>
                      <tr>
                        <td style="padding:3px 0;font-weight:700;color:#475569">
                          Entorno
                        </td>
                        <td style="padding:3px 0">{entornoHtml}</td>
                      </tr>
                      <tr>
                        <td style="padding:3px 0;font-weight:700;color:#475569">
                          Fecha
                        </td>
                        <td style="padding:3px 0">{fechaHtml}</td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          <tr>
            <td style="padding:14px 28px;background:#f8fafc;border-top:1px solid #e2e8f0;color:#94a3b8;font-size:11px">
              Notificacion automatica de NS Quell ERP. No es necesario responder a este correo.
            </td>
          </tr>

        </table>
      </td>
    </tr>
  </table>
</body>
</html>
""";
    }
}