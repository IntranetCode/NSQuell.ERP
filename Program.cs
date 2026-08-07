// Usings para nuestro módulo
//Configuración y conexión a base de datos derarrollo y productivo
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.IIS;
using Microsoft.EntityFrameworkCore;
using Org.BouncyCastle.Crypto;
using ERP.NSQuell.Areas.AdminUsuarios.Interfaces;
using ERP.NSQuell.Areas.AdminUsuarios.Services;
using ERP.NSQuell.Controllers;
using ERP.NSQuell.Helpers;
using ERP.NSQuell.Models;
using ERP.NSQuell.Models.Opciones;
//using ProyectoMatrix.Services;
using ERP.NSQuell.Servicios;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;


var builder = WebApplication.CreateBuilder(args);

// ? AGREGAR ESTAS LÍNEAS PARA ARCHIVOS GRANDES
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 268435456; // 256 MB
    options.ValueLengthLimit = int.MaxValue;
    options.ValueCountLimit = 10000; // RELEASE_EDICION_FORM_VALUE_LIMIT_V1_0
    options.MultipartHeadersLengthLimit = int.MaxValue;
});


// ? AGREGAR CONFIGURACIÓN DEL SERVIDOR
builder.WebHost.ConfigureKestrel(options =>
{
    // Escuchar en puerto 500 para todas las IPs
    //options.ListenAnyIP(5001);

    // ? AGREGAR LÍMITES PARA KESTREL TAMBIÉN
    options.Limits.MaxRequestBodySize = 268435456; // 256 MB
});

// ? AGREGAR CONFIGURACIÓN DEL SERVIDOR
//builder.WebHost.ConfigureKestrel(options =>
//{
// Escuchar en puerto 500 para todas las IPs
// options.ListenAnyIP(500);
//});


// Obtener la cadena de conexión desde appsettings.json
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// Registrar el contexto de la base de datos
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

// ? AGREGAR MVC Controllers // 1. Controladores, Vistas y Razor Pages
builder.Services.AddControllersWithViews().AddRazorRuntimeCompilation();

// Agregar servicios
builder.Services.AddRazorPages();



// 2. AÑADIDO: Habilita la validación del lado del cliente en toda la aplicación
builder.Services.AddRazorPages().AddViewOptions(options =>
{
    options.HtmlHelperOptions.ClientValidationEnabled = true;
});




// ? CONFIGURAR Session con opciones
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Agregar la autenticación antes de construir la app
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login/Login";
        options.LogoutPath = "/Login/Logout";
    });


builder.Services.AddAuthorization();





// 4. Registramos todos tus servicios
builder.Services.AddScoped<IUsuarioService, UsuarioService>();
//builder.Services.AddScoped<PerfilUsuarioService>();


builder.Services.AddSingleton<ISftpStorage, SftpStorage>();





// Registrar el servicio de notificacionesa
builder.Services.AddScoped<ServicioNotificaciones>();




//Vicular opciones de corrreo
builder.Services.Configure<CorreoOpciones>(
    builder.Configuration.GetSection("CorreoNotificaciones"));



//Restra el servicio de Bitacora
builder.Services.AddScoped<BitacoraService>();

//Se agrega el servicio para la ruta NAS

builder.Services.AddScoped<RutaNas>();



builder.Services.AddDistributedMemoryCache();



builder.Services.AddHttpContextAccessor();


builder.Services.AddSignalR();



//Registrando el nuevo servicio creado que es sobre acceso

builder.Services.AddScoped<IServicioAcceso, ServicioAcceso>();


var app = builder.Build();
// Activa el motor de creación de PDFs de Rotativa
if (app.Environment.IsDevelopment())
{
    // /dev/test-smtp-connect: prueba combos puerto/seguridad
    app.MapGet("/dev/test-smtp-connect", async (IConfiguration cfg) =>
    {
        var host = cfg["CorreoNotificaciones:SmtpHost"] ?? "mail.tu-dominio.com";
        var ports = new[] { 465, 587 };
        var securities = new[] { SecureSocketOptions.SslOnConnect, SecureSocketOptions.StartTls };

        var resultados = new List<object>();

        foreach (var port in ports)
        {
            foreach (var sec in securities)
            {
                using var client = new SmtpClient { Timeout = 5000 };
                try
                {
                    var sw = Stopwatch.StartNew();
                    await client.ConnectAsync(host, port, sec);
                    sw.Stop();

                    resultados.Add(new
                    {
                        host,
                        port,
                        security = sec.ToString(),
                        ok = true,
                        elapsedMs = sw.ElapsedMilliseconds,
                        capabilities = client.Capabilities.ToString(),
                        authMechs = client.AuthenticationMechanisms
                    });

                    await client.DisconnectAsync(true);
                }
                catch (Exception ex)
                {
                    resultados.Add(new
                    {
                        host,
                        port,
                        security = sec.ToString(),
                        ok = false,
                        error = ex.Message
                    });
                }
            }
        }

        return Results.Json(resultados);
    });

    // /dev/test-smtp-auth: conecta + autentica con config actual
    app.MapGet("/dev/test-smtp-auth", async (IConfiguration cfg) =>
    {
        var host = cfg["CorreoNotificaciones:SmtpHost"];
        var portStr = cfg["CorreoNotificaciones:SmtpPort"];
        var secStr = cfg["CorreoNotificaciones:Security"];
        var user = cfg["CorreoNotificaciones:Usuario"];
        var pass = cfg["CorreoNotificaciones:Contrasena"];

        if (string.IsNullOrWhiteSpace(host))
            return Results.Text("❌ ERROR: CorreoNotificaciones:SmtpHost vacío");

        if (string.IsNullOrWhiteSpace(portStr) || !int.TryParse(portStr, out int port))
            return Results.Text("❌ ERROR: CorreoNotificaciones:SmtpPort inválido");

        if (string.IsNullOrWhiteSpace(user))
            return Results.Text("❌ ERROR: CorreoNotificaciones:Usuario vacío (revisa user-secrets)");

        var security = secStr?.ToLower() switch
        {
            "starttls" => SecureSocketOptions.StartTls,
            "sslonconnect" => SecureSocketOptions.SslOnConnect,
            "auto" => SecureSocketOptions.Auto,
            _ => SecureSocketOptions.SslOnConnect
        };

        var resultado = new System.Text.StringBuilder();
        resultado.AppendLine("📋 Configuración:");
        resultado.AppendLine($"  Host: {host}");
        resultado.AppendLine($"  Port: {port}");
        resultado.AppendLine($"  Security: {secStr} → {security}");
        resultado.AppendLine($"  Usuario: {user}");
        resultado.AppendLine($"  Password: {(string.IsNullOrEmpty(pass) ? "❌ VACÍO" : "✅ OK")}");
        resultado.AppendLine();

        using var client = new SmtpClient
        {
            Timeout = 20000,
            ServerCertificateValidationCallback = (s, c, h, e) => true
        };

        try
        {
            resultado.AppendLine($"🔌 Conectando a {host}:{port}...");
            var sw = Stopwatch.StartNew();

            await client.ConnectAsync(host, port, security);
            sw.Stop();

            resultado.AppendLine($"✅ Conectado en {sw.ElapsedMilliseconds}ms");
            resultado.AppendLine($"   Capacidades: {client.Capabilities}");
            resultado.AppendLine($"   Mechs: {string.Join(", ", client.AuthenticationMechanisms)}");

            client.AuthenticationMechanisms.Remove("XOAUTH2");

            resultado.AppendLine();
            resultado.AppendLine($"🔐 Autenticando como {user}...");
            sw.Restart();

            await client.AuthenticateAsync(user, pass);
            sw.Stop();

            resultado.AppendLine($"✅ Autenticado en {sw.ElapsedMilliseconds}ms");

            await client.DisconnectAsync(true);
            resultado.AppendLine("🎉 TODO OK");
        }
        catch (SocketException ex)
        {
            resultado.AppendLine($"❌ ERROR DE RED: {ex.Message}");
            resultado.AppendLine($"   Código: {ex.SocketErrorCode}");
            resultado.AppendLine();
            resultado.AppendLine("💡 Posibles causas:");
            resultado.AppendLine("   - Firewall bloqueando puerto saliente");
            resultado.AppendLine("   - ISP bloqueando SMTP");
            resultado.AppendLine($"   - Host incorrecto (¿debería ser gatorXXXX.hostgator.com?)");
        }
        catch (MailKit.Security.AuthenticationException ex)
        {
            resultado.AppendLine($"❌ ERROR DE AUTENTICACIÓN: {ex.Message}");
            resultado.AppendLine();
            resultado.AppendLine("💡 Revisa user-secrets y contraseña");
        }
        catch (TimeoutException ex)
        {
            resultado.AppendLine($"❌ TIMEOUT: {ex.Message}");
            resultado.AppendLine();
            resultado.AppendLine($"💡 Prueba: Test-NetConnection {host} -Port {port}");
        }
        catch (Exception ex)
        {
            resultado.AppendLine($"❌ ERROR: {ex.GetType().Name}");
            resultado.AppendLine($"   {ex.Message}");
        }

        return Results.Text(resultado.ToString());
    });

    // /dev/smtp-probar: prueba genérica con parámetros
    app.MapGet("/dev/smtp-probar", async (string host, int port = 587, string security = "StartTls", string? user = null, string? pass = null) =>
    {
        SecureSocketOptions sec = security.Equals("StartTls", StringComparison.OrdinalIgnoreCase)
            ? SecureSocketOptions.StartTls
            : security.Equals("SslOnConnect", StringComparison.OrdinalIgnoreCase)
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.Auto;

        using var client = new SmtpClient { Timeout = 8000 };
        try
        {
            await client.ConnectAsync(host, port, sec);
            client.AuthenticationMechanisms.Remove("XOAUTH2");
            string mechs = string.Join(",", client.AuthenticationMechanisms);

            if (!string.IsNullOrWhiteSpace(user))
                await client.AuthenticateAsync(user, pass ?? "");

            await client.DisconnectAsync(true);
            return Results.Text($"OK: conectado {(string.IsNullOrWhiteSpace(user) ? "" : "y autenticado ")}en {host}:{port} ({sec}). Mechs: {mechs}");
        }
        catch (MailKit.Security.AuthenticationException ex)
        {
            return Results.Text($"AUTH FAIL: {ex.Message}");
        }
        catch (Exception ex)
        {
            return Results.Text($"ERROR: {ex.Message}");
        }
    });

    // /dev/mail-config: muestra config actual
    app.MapGet("/dev/mail-config", (IConfiguration cfg) =>
    {
        return Results.Json(new
        {
            Host = cfg["CorreoNotificaciones:SmtpHost"],
            Port = cfg["CorreoNotificaciones:SmtpPort"],
            Security = cfg["CorreoNotificaciones:Security"],
            Remitente = cfg["CorreoNotificaciones:Remitente"],
            Usuario = cfg["CorreoNotificaciones:Usuario"],
            SoloPruebas = cfg["CorreoNotificaciones:SoloPruebas"],
            ListaBlanca = cfg["CorreoNotificaciones:ListaBlanca"]
        });
    });

    // /dev/probar-correo: envía correo de prueba
    app.MapGet("/dev/probar-correo", async (IConfiguration cfg, ServicioNotificaciones notif, string? para) =>
    {
        try
        {
            string pickTo()
            {
                if (!string.IsNullOrWhiteSpace(para)) return para.Trim();
                var lista = (cfg["CorreoNotificaciones:ListaBlanca"] ?? "")
                    .Split(',', ';', ' ')
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToList();
                return lista.FirstOrDefault()
                       ?? (cfg["CorreoNotificaciones:Remitente"] ?? "").Trim();
            }

            var to = pickTo();
            if (string.IsNullOrWhiteSpace(to))
                return Results.Text("❌ ERROR: No hay destinatario");

            await notif.EnviarCorreoAsync(to);

            var html = $"<meta charset='utf-8'><h3>✅ OK</h3><p>Enviado a <b>{WebUtility.HtmlEncode(to)}</b></p>";
            return Results.Content(html, "text/html; charset=utf-8");
        }
        catch (Exception ex)
        {
            var html = $"<meta charset='utf-8'><h3>❌ ERROR</h3><pre>{WebUtility.HtmlEncode(ex.ToString())}</pre>";
            return Results.Content(html, "text/html; charset=utf-8");
        }
    });

    // /dev/probar-correo-persona: envía a una persona desde tabla Persona
    app.MapGet("/dev/probar-correo-persona", async (int personaId, ServicioNotificaciones notif) =>
    {
        try
        {
            var asunto = $"🔧 Prueba SMTP a persona #{personaId}";
            var html = $"<h3>Prueba a personaId={personaId}</h3>";
            await notif.EnviarAPersonaAsync(personaId, asunto, html);
            return Results.Text($"OK: enviado a personaId={personaId}");
        }
        catch (Exception ex)
        {
            return Results.Text($"ERROR: {ex.Message}");
        }
    });

    // /dev/probar-correo-bcc: envía BCC a múltiples personas
    app.MapGet("/dev/probar-correo-bcc", async (string ids, ServicioNotificaciones notif) =>
    {
        try
        {
            var lista = ids.Split(',', ';')
                .Select(s => s.Trim())
                .Where(s => int.TryParse(s, out _))
                .Select(int.Parse)
                .ToList();

            if (lista.Count == 0)
                return Results.Text("❌ ERROR: Proporciona ?ids=1,2,3");

            await notif.EnviarABccPersonasAsync(
                lista,
                "🧪 Prueba BCC",
                "<h2>Prueba BCC OK</h2><p>Esto salió desde el sistema.</p>");

            return Results.Text($"✅ OK: enviado BCC a {lista.Count} personas (ids={string.Join(",", lista)})");
        }
        catch (Exception ex)
        {
            return Results.Text($"❌ ERROR: {ex.Message}");
        }
    });
}

// Configurar el middleware                                             
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// ? COMENTAR O QUITAR ESTA LÍNEA para HTTP
// app.UseHttpsRedirection();



app.Use(async (context, next) =>
{
    context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
    context.Response.Headers["Pragma"] = "no-cache";
    context.Response.Headers["Expires"] = "0";
    await next();
});


app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

// ? Session DEBE ir antes de Authentication
app.UseSession();
app.UseAuthentication();
app.UseMiddleware<MiddlewareContextoSolicitud>();

app.UseAuthorization();


app.MapControllers();



// ? MAPEAR Controllers ANTES de RazorPages
app.MapControllerRoute(
    name: "universidad",
    pattern: "Universidad/{action=Index}/{id?}",
    defaults: new { controller = "Universidad" });

app.MapControllerRoute(
    name: "streaming-admin",
    pattern: "Streaming/Admin/{action=Index}/{id?}",
    defaults: new { controller = "StreamingAdmin" });

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Login}/{action=Login}/{id?}");



app.MapRazorPages();

app.Run();