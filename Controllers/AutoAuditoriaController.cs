using System.Data;
using ERP.NSQuell.Servicios.AutoAuditoria;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace ERP.NSQuell.Controllers;

[Authorize]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
[Route("AutoAuditoria")]
public sealed class AutoAuditoriaController : Controller
{
    private readonly string _cnn;
    private readonly IConfiguration _cfg;
    private readonly IHostEnvironment _environment;
    private AaEntorno Entorno => AaEntorno.From(_cfg,_environment);
    private bool PortalConfigurado => Entorno.PublicConfigured && (!Entorno.Local || (HttpContext.Request.Host.Host.Equals("localhost",StringComparison.OrdinalIgnoreCase) || HttpContext.Request.Host.Host=="127.0.0.1" || HttpContext.Request.Host.Host=="::1"));
    private readonly ERP.NSQuell.Servicios.ServicioNotificaciones _correo;
    public AutoAuditoriaController(IConfiguration cfg, ERP.NSQuell.Servicios.ServicioNotificaciones correo, IHostEnvironment environment)
    {
        _cnn=cfg.GetConnectionString("DefaultConnection")??throw new InvalidOperationException("DefaultConnection no configurada.");
        _correo=correo;
        _cfg=cfg;
        _environment=environment;
    }
    private int? UserId => int.TryParse(User.FindFirst("UsuarioID")?.Value,out var uid)&&uid>0&&HttpContext.Session.GetInt32("UsuarioID")==uid?uid:null;
    private async Task<bool> Access(SqlConnection cn)
    {
        if(User.Identity?.IsAuthenticated!=true || UserId is not int uid) return false;
        // Do not touch production even if somebody accidentally executes the code there.
        const string exists="SELECT CASE WHEN OBJECT_ID(N'aa.Instalacion',N'U') IS NOT NULL THEN 1 ELSE 0 END";
        await using(var c=new SqlCommand(exists,cn)){if(Convert.ToInt32(await c.ExecuteScalarAsync())!=1)return false;}
        const string sql=@"SELECT CAST(CASE WHEN EXISTS(SELECT 1 FROM aa.Instalacion WHERE Id=1 AND Activo=1 AND EsTest=@aaTest AND Servidor=@aaServer AND Servidor=CONVERT(nvarchar(128),SERVERPROPERTY('ServerName')) AND BaseDatos=DB_NAME() AND DB_NAME()=N'ERP_QUELL') AND EXISTS(SELECT 1 FROM dbo.Usuarios u CROSS APPLY dbo.fn_PermisosEfectivosUsuario(u.UsuarioID) f JOIN dbo.SubMenus sm ON sm.SubMenuID=f.SubMenuID JOIN dbo.Menus m ON m.MenuID=sm.MenuID JOIN dbo.MenuGrupo g ON g.MenuGrupoID=m.MenuGrupoID WHERE u.UsuarioID=@u AND u.Activo=1 AND f.TienePermiso=1 AND sm.Activo=1 AND m.Activo=1 AND g.Activo=1 AND LTRIM(RTRIM(g.Nombre)) COLLATE Latin1_General_100_CI_AI IN(N'COMPRAS',N'Formularios externos') AND (u.RolID=1 OR EXISTS(SELECT 1 FROM dbo.Departamentos d WHERE d.DepartamentoID=u.DepartamentoID AND ISNULL(d.Activo,0)=1 AND LTRIM(RTRIM(d.NombreDepartamento)) COLLATE Latin1_General_100_CI_AI IN(N'COMPRAS',N'SGC',N'SISTEMA DE GESTION DE CALIDAD',N'SISTEMA DE GESTION DE LA CALIDAD',N'GESTION DE CALIDAD',N'GESTION DE LA CALIDAD',N'SISTEMAS DE GESTION DE CALIDAD'))) AND sm.UrlEnlace='/AutoAuditoria/Index' AND sm.Nombre=N'Ver Autoauditorías externas') THEN 1 ELSE 0 END AS bit)";
        await using var cmd=new SqlCommand(sql,cn);cmd.Parameters.Add("@u",SqlDbType.Int).Value=uid;Entorno.Bind(cmd);return (bool)(await cmd.ExecuteScalarAsync()??false);
    }
    private static string S(SqlDataReader r,int i)=>r.IsDBNull(i)?"":r.GetString(i);
    private static DateTime? D(SqlDataReader r,int i)=>r.IsDBNull(i)?null:r.GetDateTime(i);
    private const string ListSql=@"SELECT s.Id,s.Folio,s.Empresa,s.CorreoInvitacion,s.CreadaUtc,s.VenceUtc,s.RespondidaUtc,s.RevocadaUtc,ISNULL(a.Verde,0),ISNULL(a.Amarillo,0),ISNULL(a.Rojo,0),ISNULL(a.Nb,0) FROM aa.Solicitudes s OUTER APPLY(SELECT SUM(CASE WHEN Valoracion='VERDE' THEN 1 ELSE 0 END) Verde,SUM(CASE WHEN Valoracion='AMARILLO' THEN 1 ELSE 0 END) Amarillo,SUM(CASE WHEN Valoracion='ROJO' THEN 1 ELSE 0 END) Rojo,SUM(CASE WHEN Valoracion='n.b.' THEN 1 ELSE 0 END) Nb FROM aa.Respuestas WHERE SolicitudID=s.Id)a";
    private static AaSolicitud Row(SqlDataReader r)=>new(r.GetInt64(0),S(r,1),S(r,2),S(r,3),r.GetDateTime(4),r.GetDateTime(5),D(r,6),D(r,7),r.GetInt32(8),r.GetInt32(9),r.GetInt32(10),r.GetInt32(11));
    private string Link(string token)=>Entorno.PublicBase+"/AutoAuditoria/Responder#token="+token; // fixed configured origin; never trust Host header
    private async Task<AaPanel> Panel(SqlConnection cn,string? link=null,string? notice=null,string? error=null)
    {
        var p=new AaPanel{Enlace=link,Aviso=notice,Error=error};
        await using(var cmd=new SqlCommand("SELECT ProveedorID,Nombre,Correo,Direccion,Contacto FROM dbo.ERP_Proveedores WHERE Activo=1 ORDER BY Nombre,ProveedorID",cn))
        await using(var r=await cmd.ExecuteReaderAsync())while(await r.ReadAsync())p.Proveedores.Add(new(r.GetInt32(0),S(r,1),S(r,2),S(r,3),S(r,4)));
        await using(var cmd=new SqlCommand(ListSql+" ORDER BY s.Id DESC",cn))
        await using(var r=await cmd.ExecuteReaderAsync())while(await r.ReadAsync())p.Solicitudes.Add(Row(r));
        return p;
    }
    // NSQ_AA_EMAIL_TEST_V3: token is never stored in the email queue or logs.
    // Await the SMTP result for TEST, after the invitation transaction is committed.
    private async Task<AaPanel> EnviarInvitacionAsync(SqlConnection cn,string token,string notice)
    {
        string link=Link(token), empresa="", correo="", folio="";
        long id=0; DateTime vence=default;
        await using(var cmd=new SqlCommand(@"SELECT Id,Empresa,CorreoInvitacion,Folio,VenceUtc FROM aa.Solicitudes WHERE TokenHash=@h AND RespondidaUtc IS NULL AND RevocadaUtc IS NULL AND VenceUtc>SYSUTCDATETIME()",cn))
        {
            cmd.Parameters.Add("@h",SqlDbType.Binary,32).Value=AutoAuditoriaPublica.Hash(token);
            await using var r=await cmd.ExecuteReaderAsync();
            if(await r.ReadAsync()){id=r.GetInt64(0);empresa=S(r,1);correo=S(r,2);folio=S(r,3);vence=r.GetDateTime(4);}
        }
        if(id==0)return await Panel(cn,error:"La invitación ya no está vigente. Genere otro enlace.");
        string evt;
        var env=Entorno;
        var prueba=env.Prod?"":"<p style=\"background:#fff4e8;padding:12px;border-left:4px solid #ed7a22\"><strong>PRUEBA INTERNA:</strong> invitación de prueba. No reenviar a proveedores.</p>";
        var e=System.Net.WebUtility.HtmlEncode(empresa);
        var f=System.Net.WebUtility.HtmlEncode(folio);
        var l=System.Net.WebUtility.HtmlEncode(link);
        string html=$"""
<!doctype html><html lang="es"><head><meta charset="utf-8"></head><body style="margin:0;background:#eef2f7;font:15px Segoe UI,Arial,sans-serif;color:#24354a">
<table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#eef2f7"><tr><td align="center" style="padding:26px 12px">
<table role="presentation" width="600" cellspacing="0" cellpadding="0" style="width:100%;max-width:600px;background:white;border:1px solid #dce5ee;border-radius:14px;overflow:hidden"><tr><td style="background:#12345a;border-bottom:4px solid #ed7a22;padding:24px;color:white"><table role="presentation" cellspacing="0" cellpadding="0"><tr><td bgcolor="#ffffff" style="padding:7px 9px;background:#ffffff;border-radius:6px"><img src="cid:nsq-aa-logo@nsquell.com.mx" alt="NS Quell" width="76" style="display:block;width:76px;height:auto;border:0"></td><td style="padding-left:14px;font-size:13px;letter-spacing:2px;font-weight:bold;color:#ffc184">NS QUELL</td></tr></table><h1 style="font-size:23px;margin:12px 0 0">Autoauditoría de fabricantes</h1></td></tr>
<tr><td style="padding:26px;line-height:1.65"><p style="margin-top:0"><strong>Estimado equipo de {e}:</strong></p><p>Les invitamos a completar nuestra autoauditoría de fabricantes. El cuestionario consta de 18 puntos y no requiere usuario ni contraseña.</p>{prueba}<table role="presentation" cellspacing="0" cellpadding="0" style="margin:24px 0"><tr><td bgcolor="#174b80" style="background:#174b80;border-radius:8px;padding:14px 20px"><a href="{l}" style="color:#ffffff;font-weight:bold;text-decoration:none;display:block">Responder autoauditoría</a></td></tr></table><div style="font-size:13px;color:#52647b"><strong>Folio:</strong> {f}<br><strong>Vigencia:</strong> {vence:dd/MM/yyyy HH:mm} UTC<br>GQ-F-CP02-05 · Ver. 01</div><p style="font-size:12px;word-break:break-all">Si el botón no funciona, copie este enlace:<br>{l}</p><p>Atentamente,<br><strong>Compras / SGC · NS Quell</strong></p></td></tr><tr><td style="background:#f5f8fb;padding:15px 26px;font-size:12px;color:#64748b">Invitación privada. <a href="https://nsquell.nsgroup.com.mx/" style="color:#174b80">Sitio web NS Quell</a></td></tr></table></td></tr></table></body></html>
""";
        try{evt=env.Prod&&!env.ExternalMail?"CORREO_BLOQUEADO":await _correo.EnviarInvitacionAutoAuditoriaTestAsync(correo,(env.Prod?"":"[TEST] ")+"NS Quell | Invitación a autoauditoría",html,produccion:env.Prod);}
        catch{evt="CORREO_INCIERTO";}
        string? auditWarning=null;
        try{
            await using var log=new SqlCommand("INSERT aa.Historial(SolicitudID,Evento,UsuarioID) VALUES(@id,@evt,@u)",cn);
            log.Parameters.Add("@id",SqlDbType.BigInt).Value=id;
            log.Parameters.Add("@evt",SqlDbType.VarChar,20).Value=evt;
            log.Parameters.Add("@u",SqlDbType.Int).Value=UserId!.Value;
            await log.ExecuteNonQueryAsync();
        }catch(SqlException){auditWarning=" No se pudo registrar el resultado de correo en el historial.";}
        return evt=="CORREO_ENVIADO"
            ? await Panel(cn,link,notice+" Correo aceptado por SMTP para "+correo+"."+auditWarning)
            : await Panel(cn,link,notice,error:(evt=="CORREO_BLOQUEADO"?"Correo no enviado: envío externo deshabilitado en este entorno, destinatario fuera de ListaBlanca TEST o correo global deshabilitado.":evt switch {"CORREO_FALTA_HOST"=>"Correo no enviado: falta CorreoNotificaciones:SmtpHost en el proceso IIS.","CORREO_FALTA_FROM"=>"Correo no enviado: falta CorreoNotificaciones:Remitente.","CORREO_FALTA_PASS"=>"Correo no enviado: falta la credencial SMTP (NSQ_SMTP_PASSWORD o CorreoNotificaciones:Contrasena).", "CORREO_AUTH"=>"Correo no enviado: autenticación SMTP rechazada.","CORREO_CONEXION"=>"Correo no enviado: conexión SMTP/TLS no establecida.","CORREO_CONFIG"=>"Correo no enviado: configuración SMTP incompleta.",_=>"SMTP no confirmó el envío. Revise el correo antes de renovar."})+" La invitación sigue vigente; puede copiar el enlace."+auditWarning);
    }

    [HttpGet("Diagnostico")]
    public async Task<IActionResult> Diagnostico()
    {
        if(UserId is not int uid)return Forbid();
        await using var cn=new SqlConnection(_cnn);await cn.OpenAsync();
        await using(var admin=new SqlCommand("SELECT COUNT(*) FROM dbo.Usuarios WHERE UsuarioID=@u AND Activo=1 AND RolID=1",cn))
        {admin.Parameters.Add("@u",SqlDbType.Int).Value=uid;if(Convert.ToInt32(await admin.ExecuteScalarAsync())!=1)return Forbid();}
        string real="",db="";bool avisos=false,config=false;
        await using(var cmd=new SqlCommand("SELECT CONVERT(nvarchar(128),SERVERPROPERTY('ServerName')),DB_NAME(),CASE WHEN OBJECT_ID(N'aa.AvisosRespuesta',N'U') IS NOT NULL THEN 1 ELSE 0 END,CASE WHEN OBJECT_ID(N'aa.V4Config',N'U') IS NOT NULL THEN 1 ELSE 0 END",cn))
        await using(var r=await cmd.ExecuteReaderAsync()){if(await r.ReadAsync()){real=r.GetString(0);db=r.GetString(1);avisos=r.GetInt32(2)==1;config=r.GetInt32(3)==1;}}
        var env=Entorno;
        return Json(new{version="V7",ambienteAspNet=_environment.EnvironmentName,modo=env.Prod?"PROD":"TEST",servidorSqlReal=real,baseDatos=db,servidorEsperado=env.Server,urlErp=env.ErpBase,urlPortal=env.PublicBase,portalConfigurado=PortalConfigurado,envioExterno=env.ExternalMail,tablaAvisos=avisos,configAvisos=config,correo=_correo.DiagnosticoAutoAuditoria()});
    }

    [HttpGet("")][HttpGet("Index")]
    public async Task<IActionResult> Index()
    {
        await using var cn=new SqlConnection(_cnn);await cn.OpenAsync();if(!await Access(cn))return Forbid();return View(await Panel(cn,error:PortalConfigurado?null:"Portal externo sin URL HTTPS configurada para este IIS. No se generarán ni renovarán invitaciones hasta corregirlo."));
    }
    [HttpPost("Crear")]
    public async Task<IActionResult> Crear(int? proveedorId,string? empresa,string? correo,int vigencia=15)
    {
        await using var cn=new SqlConnection(_cnn);await cn.OpenAsync();if(!await Access(cn))return Forbid();
        if(!PortalConfigurado)return View("Index",await Panel(cn,error:"Configure primero la URL HTTPS del portal externo de este ambiente."));
        string address="",contact="",catalogEmail="";empresa=empresa?.Trim();correo=correo?.Trim();
        if(proveedorId is >0){await using(var cmd=new SqlCommand("SELECT Nombre,Correo,Direccion,Contacto FROM dbo.ERP_Proveedores WHERE ProveedorID=@id AND Activo=1",cn)){cmd.Parameters.Add("@id",SqlDbType.Int).Value=proveedorId.Value;await using var r=await cmd.ExecuteReaderAsync();if(await r.ReadAsync()){empresa=S(r,0);catalogEmail=S(r,1);address=S(r,2);contact=S(r,3);}else{empresa=null;}}if(string.IsNullOrWhiteSpace(correo))correo=catalogEmail;}
        else proveedorId=null;
        if(!ModelState.IsValid||string.IsNullOrWhiteSpace(empresa)||empresa.Length>250||!AutoAuditoriaPublica.ValidEmail(correo)||vigencia is not (7 or 15 or 30))return View("Index",await Panel(cn,error:"Seleccione un proveedor activo o capture la razón social, un correo válido y la vigencia."));
        var token=AutoAuditoriaPublica.NewToken();var folio="AA-"+DateTime.UtcNow.ToString("yyyyMMdd")+"-"+Guid.NewGuid().ToString("N")[..16].ToUpperInvariant();
        await using(var cmd=new SqlCommand(@"SET XACT_ABORT ON; BEGIN TRAN;
INSERT aa.Solicitudes(Folio,ProveedorID,Empresa,CorreoInvitacion,DireccionInicial,ContactoInicial,TokenHash,VenceUtc,CreadaPor) VALUES(@f,@p,@e,@c,@d,@n,@h,DATEADD(day,@v,SYSUTCDATETIME()),@u);
DECLARE @id bigint=SCOPE_IDENTITY(); INSERT aa.Historial(SolicitudID,Evento,UsuarioID) VALUES(@id,'CREADA',@u); COMMIT;",cn)){
            cmd.Parameters.Add("@f",SqlDbType.VarChar,40).Value=folio;cmd.Parameters.Add("@p",SqlDbType.Int).Value=(object?)proveedorId??DBNull.Value;cmd.Parameters.Add("@e",SqlDbType.NVarChar,250).Value=empresa;cmd.Parameters.Add("@c",SqlDbType.NVarChar,150).Value=correo!;cmd.Parameters.Add("@d",SqlDbType.NVarChar,300).Value=address;cmd.Parameters.Add("@n",SqlDbType.NVarChar,150).Value=contact;cmd.Parameters.Add("@h",SqlDbType.Binary,32).Value=AutoAuditoriaPublica.Hash(token);cmd.Parameters.Add("@v",SqlDbType.Int).Value=vigencia;cmd.Parameters.Add("@u",SqlDbType.Int).Value=UserId!.Value;await cmd.ExecuteNonQueryAsync();}
        return View("Index",await EnviarInvitacionAsync(cn,token,"Invitación creada. "+folio+"."));
    }
    [HttpPost("Renovar")]
    public async Task<IActionResult> Renovar(long id,int vigencia=15)
    {
        await using var cn=new SqlConnection(_cnn);await cn.OpenAsync();if(!await Access(cn))return Forbid();if(!ModelState.IsValid||vigencia is not (7 or 15 or 30))return BadRequest();
        if(!PortalConfigurado)return View("Index",await Panel(cn,error:"Configure primero la URL HTTPS del portal externo de este ambiente."));
        var token=AutoAuditoriaPublica.NewToken();
        await using var cmd=new SqlCommand(@"SET XACT_ABORT ON; BEGIN TRAN; UPDATE aa.Solicitudes SET TokenHash=@h,VenceUtc=DATEADD(day,@v,SYSUTCDATETIME()) WHERE Id=@id AND RespondidaUtc IS NULL AND RevocadaUtc IS NULL; IF @@ROWCOUNT=0 BEGIN ROLLBACK; SELECT 0; RETURN; END; INSERT aa.Historial(SolicitudID,Evento,UsuarioID) VALUES(@id,'RENOVADA',@u); COMMIT; SELECT 1;",cn);
        cmd.Parameters.Add("@h",SqlDbType.Binary,32).Value=AutoAuditoriaPublica.Hash(token);cmd.Parameters.Add("@v",SqlDbType.Int).Value=vigencia;cmd.Parameters.Add("@id",SqlDbType.BigInt).Value=id;cmd.Parameters.Add("@u",SqlDbType.Int).Value=UserId!.Value;
        if(Convert.ToInt32(await cmd.ExecuteScalarAsync())!=1)return View("Index",await Panel(cn,error:"No se puede renovar una invitación revocada o respondida."));
        return View("Index",await EnviarInvitacionAsync(cn,token,"Nuevo enlace generado. El anterior dejó de ser válido."));
    }
    [HttpPost("Revocar")]
    public async Task<IActionResult> Revocar(long id)
    {
        await using var cn=new SqlConnection(_cnn);await cn.OpenAsync();if(!await Access(cn))return Forbid();if(!ModelState.IsValid)return BadRequest();
        await using var cmd=new SqlCommand(@"SET XACT_ABORT ON; BEGIN TRAN; UPDATE aa.Solicitudes SET RevocadaUtc=SYSUTCDATETIME(),RevocadaPor=@u WHERE Id=@id AND RespondidaUtc IS NULL AND RevocadaUtc IS NULL; IF @@ROWCOUNT=0 BEGIN ROLLBACK; SELECT 0; RETURN; END; INSERT aa.Historial(SolicitudID,Evento,UsuarioID) VALUES(@id,'REVOCADA',@u); COMMIT; SELECT 1;",cn);
        cmd.Parameters.Add("@id",SqlDbType.BigInt).Value=id;cmd.Parameters.Add("@u",SqlDbType.Int).Value=UserId!.Value;
        if(Convert.ToInt32(await cmd.ExecuteScalarAsync())!=1)return View("Index",await Panel(cn,error:"La invitación ya no puede revocarse."));
        return RedirectToAction(nameof(Index));
    }

    // NSQ_AA_V4_STATS: read-only aggregate dashboard, same authorization as administration.
    [HttpGet("Estadisticas")]
    public async Task<IActionResult> Estadisticas()
    {
        await using var cn=new SqlConnection(_cnn);await cn.OpenAsync();if(!await Access(cn))return Forbid();
        var vm=new AaEstadisticas();
        await using(var cmd=new SqlCommand(ListSql+" ORDER BY s.Id DESC",cn))
        await using(var r=await cmd.ExecuteReaderAsync())while(await r.ReadAsync())vm.Solicitudes.Add(Row(r));
        const string puntos=@"SELECT p.Punto,p.Texto,p.Asterisco,
SUM(CASE WHEN s.Id IS NOT NULL AND r.Valoracion='VERDE' THEN 1 ELSE 0 END),
SUM(CASE WHEN s.Id IS NOT NULL AND r.Valoracion='AMARILLO' THEN 1 ELSE 0 END),
SUM(CASE WHEN s.Id IS NOT NULL AND r.Valoracion='ROJO' THEN 1 ELSE 0 END),
SUM(CASE WHEN s.Id IS NOT NULL AND r.Valoracion='n.b.' THEN 1 ELSE 0 END)
FROM aa.Preguntas p LEFT JOIN aa.Respuestas r ON r.Version=p.Version AND r.Punto=p.Punto
LEFT JOIN aa.Solicitudes s ON s.Id=r.SolicitudID AND s.RespondidaUtc IS NOT NULL
WHERE p.Version=1 GROUP BY p.Punto,p.Texto,p.Asterisco,p.Orden ORDER BY p.Orden";
        await using(var cmd=new SqlCommand(puntos,cn))
        await using(var r=await cmd.ExecuteReaderAsync())while(await r.ReadAsync())vm.Puntos.Add(new(S(r,0),S(r,1),r.GetBoolean(2),r.GetInt32(3),r.GetInt32(4),r.GetInt32(5),r.GetInt32(6)));
        // Administrators can audit the exact current effective permissions; no credentials are read.
        await using(var admin=new SqlCommand("SELECT COUNT(*) FROM dbo.Usuarios WHERE UsuarioID=@u AND Activo=1 AND RolID=1",cn))
        {admin.Parameters.Add("@u",SqlDbType.Int).Value=UserId!.Value;vm.EsAdministrador=Convert.ToInt32(await admin.ExecuteScalarAsync())==1;}
        if(vm.EsAdministrador)
        {
            const string sql=@"SELECT DISTINCT u.UsuarioID,u.Username,LTRIM(RTRIM(CONCAT(p.Nombre,N' ',p.ApellidoPaterno,N' ',p.ApellidoMaterno))),ISNULL(d.NombreDepartamento,N'Sin departamento'),ISNULL(ro.NombreRol,N'Sin rol')
FROM dbo.Usuarios u LEFT JOIN dbo.Persona p ON p.PersonaID=u.PersonaID LEFT JOIN dbo.Departamentos d ON d.DepartamentoID=u.DepartamentoID LEFT JOIN dbo.Roles ro ON ro.RolID=u.RolID
WHERE u.Activo=1 AND (u.RolID=1 OR EXISTS(SELECT 1 FROM dbo.Departamentos d WHERE d.DepartamentoID=u.DepartamentoID AND ISNULL(d.Activo,0)=1 AND LTRIM(RTRIM(d.NombreDepartamento)) COLLATE Latin1_General_100_CI_AI IN(N'COMPRAS',N'SGC',N'SISTEMA DE GESTION DE CALIDAD',N'SISTEMA DE GESTION DE LA CALIDAD',N'GESTION DE CALIDAD',N'GESTION DE LA CALIDAD',N'SISTEMAS DE GESTION DE CALIDAD'))) AND EXISTS(SELECT 1 FROM dbo.fn_PermisosEfectivosUsuario(u.UsuarioID) f JOIN dbo.SubMenus sm ON sm.SubMenuID=f.SubMenuID JOIN dbo.Menus m ON m.MenuID=sm.MenuID JOIN dbo.MenuGrupo g ON g.MenuGrupoID=m.MenuGrupoID WHERE f.TienePermiso=1 AND sm.Activo=1 AND m.Activo=1 AND g.Activo=1 AND LTRIM(RTRIM(g.Nombre)) COLLATE Latin1_General_100_CI_AI IN(N'COMPRAS',N'Formularios externos') AND sm.UrlEnlace='/AutoAuditoria/Index' AND sm.Nombre=N'Ver Autoauditorías externas') ORDER BY 4,3,1";
            await using var cmd=new SqlCommand(sql,cn);await using var r=await cmd.ExecuteReaderAsync();while(await r.ReadAsync())vm.Accesos.Add(new(r.GetInt32(0),S(r,1),S(r,2),S(r,3),S(r,4)));
        }
        return View(vm);
    }
    [HttpGet("Detalle/{id:long}")]
    public async Task<IActionResult> Detalle(long id)
    {
        await using var cn=new SqlConnection(_cnn);await cn.OpenAsync();if(!await Access(cn))return Forbid();
        var d=new AaDetalle();
        await using(var cmd=new SqlCommand(ListSql+" WHERE s.Id=@id; SELECT FechaAuditoria,DireccionRespuesta,Responsable,CorreoRespuesta FROM aa.Solicitudes WHERE Id=@id; SELECT p.Punto,p.Asterisco,p.Texto,r.Valoracion,r.Observacion FROM aa.Respuestas r JOIN aa.Preguntas p ON p.Version=r.Version AND p.Punto=r.Punto WHERE r.SolicitudID=@id ORDER BY p.Orden; SELECT Evento,FechaUtc,UsuarioID FROM aa.Historial WHERE SolicitudID=@id ORDER BY Id;",cn)){
            cmd.Parameters.Add("@id",SqlDbType.BigInt).Value=id;await using var r=await cmd.ExecuteReaderAsync();if(!await r.ReadAsync())return NotFound();d.Solicitud=Row(r);
            if(await r.NextResultAsync()&&await r.ReadAsync()){d.FechaAuditoria=D(r,0);d.Direccion=S(r,1);d.Responsable=S(r,2);d.CorreoRespuesta=S(r,3);}
            if(await r.NextResultAsync())while(await r.ReadAsync())d.Respuestas.Add(new(S(r,0),r.GetBoolean(1),S(r,2),S(r,3),S(r,4)));
            if(await r.NextResultAsync())while(await r.ReadAsync())d.Historial.Add(new(S(r,0),r.GetDateTime(1),r.IsDBNull(2)?null:r.GetInt32(2)));
        }
        await using(var check=new SqlCommand("SELECT CASE WHEN OBJECT_ID(N'aa.AvisosRespuesta',N'U') IS NULL THEN 0 ELSE 1 END",cn))
        if(Convert.ToInt32(await check.ExecuteScalarAsync())==1)
        {
            await using var cmd=new SqlCommand("SELECT Destinatario,Estado,Intentos,UltimoIntentoUtc,EnviadoUtc FROM aa.AvisosRespuesta WHERE SolicitudID=@id ORDER BY Destinatario",cn);
            cmd.Parameters.Add("@id",SqlDbType.BigInt).Value=id;await using var r=await cmd.ExecuteReaderAsync();while(await r.ReadAsync())d.Avisos.Add(new(S(r,0),S(r,1),r.GetInt32(2),D(r,3),D(r,4)));
        }
        return View(d);
    }
}
