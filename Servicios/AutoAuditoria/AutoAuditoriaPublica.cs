using System.Data;
using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;

namespace ERP.NSQuell.Servicios.AutoAuditoria;

// NSQ_AA_TEST_V1: this is a separate process of the same ERP project, not an anonymous MVC controller.
public static class AutoAuditoriaPublica
{
    private static readonly string[] Points = { "2.1","2.3","3.1","3.2","4.4","5.1","5.4","5.5","6.1.3","6.2.1","6.2.3","6.3.1","6.4.1","6.4.3","6.5.3","6.5.4","7.1","7.3" };
    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    public static byte[] Hash(string token) => SHA256.HashData(Convert.FromHexString(token));
    public static bool ValidEmail(string? email) => !string.IsNullOrWhiteSpace(email) && email.Length <= 150 && MailAddress.TryCreate(email, out var m) && string.Equals(m.Address, email, StringComparison.OrdinalIgnoreCase);
    private static byte[]? GetHash(HttpContext ctx)
    {
        if (!ctx.Request.Headers.TryGetValue("X-NSQ-Token", out var val) || val.Count != 1) return null;
        var token = val.ToString();
        return Regex.IsMatch(token, "\\A[a-f0-9]{64}\\z", RegexOptions.CultureInvariant) ? Hash(token) : null;
    }
    private static IResult Unavailable() => Results.Json(new { message = "La invitación no está disponible." }, statusCode: 410);
    private static IResult Bad(string m) => Results.Json(new { message = m }, statusCode: 400);
    private static IResult Failure() => Results.Json(new { message = "No fue posible confirmar la operación. Intente nuevamente." }, statusCode: 503);
    private static bool IsTestServer(string server,string db) => !server.Contains("PROD",StringComparison.OrdinalIgnoreCase) && !db.Contains("PROD",StringComparison.OrdinalIgnoreCase) && !new[]{"master","model","msdb","tempdb"}.Contains(db,StringComparer.OrdinalIgnoreCase);
    public sealed record Answer(string? Id,string? Rating,string? Note);
    public sealed record Submission(string? Date,string? Address,string? Name,string? Email,bool Confirmed,Answer[]? Answers);

    // Read-only diagnostic of the ERP's real DefaultConnection, using the normal http launch profile.
    public static async Task DiagnosticarAsync(WebApplicationBuilder builder)
    {
        var connection=builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("No existe DefaultConnection.");
        var cs=new SqlConnectionStringBuilder(connection);
        Console.WriteLine("Servidor de conexion: "+cs.DataSource);
        Console.WriteLine("Base configurada: "+cs.InitialCatalog);
        await using var cn=new SqlConnection(connection);await cn.OpenAsync();
        await using var cmd=new SqlCommand("SELECT CONVERT(nvarchar(128),SERVERPROPERTY('ServerName')), DB_NAME()",cn);
        await using var r=await cmd.ExecuteReaderAsync();
        if(!await r.ReadAsync())throw new InvalidOperationException("No fue posible identificar SQL Server.");
        var server=r.GetString(0);var db=r.GetString(1);
        Console.WriteLine("Instancia real: "+server);
        Console.WriteLine("Base real: "+db);
        Console.WriteLine(IsTestServer(server,db)?"Confirme que se trata de una copia aislada de TEST antes de instalar SQL.":"BLOQUEADO: posible entorno PROD o base de sistema. No ejecute el instalador SQL.");
    }

    public static async Task RunAsync(WebApplicationBuilder builder)
    {
        // TEST only. Deliberately no ERP MVC, static-file middleware, login, jobs or session.
        var connection = Environment.GetEnvironmentVariable("NSQ_AA_PUBLIC_SQL");
        var mode=Environment.GetEnvironmentVariable("NSQ_AA_MODE")??"TEST";
        bool prod=mode.Equals("PROD",StringComparison.OrdinalIgnoreCase);
        bool hosted=string.Equals(typeof(AutoAuditoriaPublica).Assembly.GetName().Name,"NSQuell.AutoAuditoria.Portal",StringComparison.Ordinal);
        if(prod&&!hosted)throw new InvalidOperationException("PROD requiere la publicación independiente del portal.");
        if(!prod&&!mode.Equals("TEST",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Modo portal inválido.");
        var expectedDb = prod?"ERP_QUELL":Environment.GetEnvironmentVariable("NSQ_AA_TEST_DATABASE");
        var expectedServer = Environment.GetEnvironmentVariable(prod?"NSQ_AA_PROD_SERVER":"NSQ_AA_TEST_SERVER");
        var host = hosted?Environment.GetEnvironmentVariable("NSQ_AA_PUBLIC_HOST"):"127.0.0.1";
        var expectedHost=prod?"erp.quell.nsgroup.com.mx":"erpnsqt.nsgroup.com.mx";
        if(hosted&&(!string.Equals(host,expectedHost,StringComparison.OrdinalIgnoreCase)||string.IsNullOrWhiteSpace(expectedServer)||!expectedServer.EndsWith(prod?"\\ERP_PROD":"\\ERP_TEST",StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("Host o instancia del portal no válida.");
        bool privilegedTest = Environment.GetEnvironmentVariable("NSQ_AA_PRIVILEGED_TEST") == "1";
        if (string.IsNullOrWhiteSpace(connection) || string.IsNullOrWhiteSpace(expectedDb) || string.IsNullOrWhiteSpace(expectedServer) || (!prod&&!IsTestServer(expectedServer,expectedDb)))
            throw new InvalidOperationException("Portal TEST: faltan los parametros de conexion a una base aislada de pruebas.");
        var cs = new SqlConnectionStringBuilder(connection);
        if (!string.Equals(cs.InitialCatalog,expectedDb,StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Base TEST distinta de la esperada.");
        await using (var check = new SqlConnection(connection))
        {
            await check.OpenAsync();
            await using (var identity = new SqlCommand("SELECT CONVERT(nvarchar(128),SERVERPROPERTY('ServerName')),DB_NAME()",check))
            await using (var reader = await identity.ExecuteReaderAsync())
            {
                if (!await reader.ReadAsync() || !string.Equals(reader.GetString(0),expectedServer,StringComparison.OrdinalIgnoreCase) || !string.Equals(reader.GetString(1),expectedDb,StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Instancia o base SQL diferente de la autorizada para TEST.");
            }
            await using var cmd = new SqlCommand("aa.PublicoEstado",check){CommandType=CommandType.StoredProcedure};
            await using var r=await cmd.ExecuteReaderAsync();
            if(!await r.ReadAsync() || !r.GetBoolean(0) || ((hosted || !privilegedTest) && !r.GetBoolean(1))) throw new InvalidOperationException("Instalacion TEST deshabilitada o cuenta SQL sin aislamiento. Use una cuenta dedicada.");
        }
        // Bind loopback only; never public Internet in this build.
        var portText=Environment.GetEnvironmentVariable("NSQ_AA_PORT") ?? "5054";
        if(!int.TryParse(portText,out var port) || port<1024 || port>65535) throw new InvalidOperationException("Puerto TEST invalido.");
        builder.Configuration["AllowedHosts"]=host!;
        if(!hosted)builder.WebHost.ConfigureKestrel(k=>{k.Listen(IPAddress.Loopback,port);k.Limits.MaxRequestBodySize=131072;k.Limits.RequestHeadersTimeout=TimeSpan.FromSeconds(15);});
        else builder.Services.Configure<Microsoft.AspNetCore.Builder.IISServerOptions>(o=>o.MaxRequestBodySize=131072);
        // Never log tokens, request bodies or SQL payloads through HTTP logging.
        builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics",LogLevel.Warning);
        builder.Services.AddRateLimiter(o=>{
            o.RejectionStatusCode=429;
            o.GlobalLimiter=PartitionedRateLimiter.Create<HttpContext,string>(ctx=>RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString()??"local",_=>new FixedWindowRateLimiterOptions{PermitLimit=60,Window=TimeSpan.FromMinutes(1),QueueLimit=0,AutoReplenishment=true}));
        });
        await using var resource=typeof(AutoAuditoriaPublica).Assembly.GetManifestResourceStream("NSQ.AutoAuditoria.Responder.html") ?? throw new InvalidOperationException("Recurso HTML no instalado.");
        using var sr=new StreamReader(resource,Encoding.UTF8);
        var html=await sr.ReadToEndAsync();
        var match=Regex.Match(html,"<script>([\\s\\S]*?)</script>");
        if(!match.Success || Regex.Matches(html,"<script(?:\\s|>)").Count!=1) throw new InvalidOperationException("HTML del portal inesperado.");
        string scriptHash=Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(match.Groups[1].Value.Replace("\r\n","\n").Replace('\r','\n'))));
        var app=builder.Build();
        app.Use(async(ctx,next)=>{
            // Only local test host, explicit routes, no redirect to ERP.
            if (!string.Equals(ctx.Request.Host.Host,host,StringComparison.OrdinalIgnoreCase) || (hosted?(!ctx.Request.IsHttps||!string.Equals(ctx.Request.PathBase.Value,"/FormularioExterno",StringComparison.OrdinalIgnoreCase)):!IPAddress.IsLoopback(ctx.Connection.RemoteIpAddress??IPAddress.None))) {ctx.Response.StatusCode=404;return;}
            if(hosted)ctx.Response.Headers["Strict-Transport-Security"]="max-age=31536000";
            ctx.Response.Headers["X-NSQ-AA-Version"]="V7";
            ctx.Response.Headers["X-NSQ-AA-Environment"]=prod?"PROD":"TEST";
            ctx.Response.Headers["Cache-Control"]="no-store, max-age=0";
            ctx.Response.Headers["Referrer-Policy"]="no-referrer";
            ctx.Response.Headers["X-Content-Type-Options"]="nosniff";
            ctx.Response.Headers["X-Frame-Options"]="DENY";
            ctx.Response.Headers["X-Robots-Tag"]="noindex, nofollow, noarchive";
            ctx.Response.Headers["Content-Security-Policy"]=$"default-src 'none'; script-src 'sha256-{scriptHash}'; style-src 'unsafe-inline'; img-src data:; connect-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'; object-src 'none'";
            await next();
        });
        app.UseRateLimiter();
        app.MapGet("/AutoAuditoria/Responder",()=>Results.Content(html,"text/html; charset=utf-8",Encoding.UTF8));
        app.MapPost("/AutoAuditoria/Acceso",async (HttpContext ctx)=>{
            var h=GetHash(ctx); if(h==null || !ctx.Request.HasJsonContentType()) return Unavailable();
            try{
                await using var cn=new SqlConnection(connection);await cn.OpenAsync(ctx.RequestAborted);
                await using var cmd=new SqlCommand("aa.PublicoAcceso",cn){CommandType=CommandType.StoredProcedure,CommandTimeout=15};
                cmd.Parameters.Add("@Hash",SqlDbType.Binary,32).Value=h;
                await using var r=await cmd.ExecuteReaderAsync(ctx.RequestAborted);
                if(!await r.ReadAsync(ctx.RequestAborted)) return Unavailable();
                return Results.Json(new {company=r.GetString(0),address=r.IsDBNull(1)?"":r.GetString(1),email=r.GetString(2),contact=r.IsDBNull(3)?"":r.GetString(3)});
            }catch(SqlException){return Failure();}
        });
        app.MapPost("/AutoAuditoria/Enviar",async (HttpContext ctx)=>{
            var h=GetHash(ctx);if(h==null) return Unavailable();
            if(!ctx.Request.HasJsonContentType()) return Bad("Formato de solicitud inválido.");
            Submission? s;
            try{s=await ctx.Request.ReadFromJsonAsync<Submission>(cancellationToken:ctx.RequestAborted);}catch(JsonException){return Bad("Datos inválidos.");}catch(BadHttpRequestException){return Bad("Solicitud inválida o demasiado grande.");}
            if(s==null || !s.Confirmed || !DateTime.TryParseExact(s.Date,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date) || date.Year<1900 || date.Date>DateTime.UtcNow.Date.AddDays(1) || string.IsNullOrWhiteSpace(s.Address)||s.Address.Trim().Length>300||string.IsNullOrWhiteSpace(s.Name)||s.Name.Trim().Length>150||!ValidEmail(s.Email?.Trim())||s.Answers==null||s.Answers.Length!=18) return Bad("Revise los datos generales y confirme la evaluación.");
            var seen=new HashSet<string>(StringComparer.Ordinal);int nb=0;
            foreach(var a in s.Answers){if(a==null||a.Id==null||!Points.Contains(a.Id,StringComparer.Ordinal)||!seen.Add(a.Id)||a.Rating is not ("VERDE" or "AMARILLO" or "ROJO" or "n.b.")||(a.Note?.Length??0)>2000||(a.Rating!="VERDE"&&string.IsNullOrWhiteSpace(a.Note))) return Bad("Complete las 18 valoraciones y sus justificaciones.");if(a.Rating=="n.b.")nb++;}
            if(nb>3) return Bad("Solo se permiten tres respuestas n.b.");
            try{
                var json=JsonSerializer.Serialize(s.Answers.Select(a=>new{id=a.Id,rating=a.Rating,note=a.Note?.Trim()??""}));
                await using var cn=new SqlConnection(connection);await cn.OpenAsync(ctx.RequestAborted);
                await using var cmd=new SqlCommand("aa.PublicoEnviar",cn){CommandType=CommandType.StoredProcedure,CommandTimeout=20};
                cmd.Parameters.Add("@Hash",SqlDbType.Binary,32).Value=h;
                cmd.Parameters.Add("@Fecha",SqlDbType.Date).Value=date;
                cmd.Parameters.Add("@Direccion",SqlDbType.NVarChar,300).Value=s.Address.Trim();
                cmd.Parameters.Add("@Nombre",SqlDbType.NVarChar,150).Value=s.Name.Trim();
                cmd.Parameters.Add("@Correo",SqlDbType.NVarChar,150).Value=s.Email!.Trim();
                cmd.Parameters.Add("@Respuestas",SqlDbType.NVarChar,-1).Value=json;
                var folio=await cmd.ExecuteScalarAsync(ctx.RequestAborted);
                if(folio is not string value || value.Length==0) return Failure();
                return Results.Json(new{ok=true,folio=value});
            }catch(SqlException ex) when(ex.Number==51001){return Unavailable();}catch(SqlException ex) when(ex.Number==51002){return Bad("Revise las valoraciones y los datos proporcionados.");}catch(SqlException){return Failure();}
        });
        // Unknown paths (including /Login, /Menu, /Compras, /swagger, assets) get a 404.
        await app.RunAsync();
    }
}
