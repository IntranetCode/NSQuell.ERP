using System.Data;
using System.Net;
using Microsoft.Data.SqlClient;
using ERP.NSQuell.Servicios;

namespace ERP.NSQuell.Servicios.AutoAuditoria;

// NSQ_AA_V4_NOTIFICACIONES: only the internal ERP process starts this worker.
// Persistent per-recipient state. A stale in-flight SMTP operation is not retried automatically.
public sealed class AaAvisosRespuestaWorker : BackgroundService
{
    private readonly IConfiguration _cfg;
    private readonly IHostEnvironment _environment;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<AaAvisosRespuestaWorker> _logger;
    private const string Db="ERP_QUELL";
    private sealed record Job(long Id,string Para,Guid Reserva,string Folio,string Empresa,DateTime Fecha,int V,int A,int R,int Nb);
    public AaAvisosRespuestaWorker(IConfiguration cfg,IServiceScopeFactory scopes,ILogger<AaAvisosRespuestaWorker> logger,IHostEnvironment environment)
    { _cfg=cfg;_scopes=scopes;_logger=logger;_environment=environment; }
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Yield during application startup. No SMTP or new SQL calls in the external portal.
        await Task.Delay(TimeSpan.FromSeconds(5),ct);
        while(!ct.IsCancellationRequested)
        {
            try
            {
                var cs=_cfg.GetConnectionString("DefaultConnection");
                if(!string.IsNullOrWhiteSpace(cs))
                {
                    // Quick prefilter prevents this TEST worker from polling other databases.
                    var env=AaEntorno.From(_cfg,_environment);
                    var b=new SqlConnectionStringBuilder(cs);
                    if(string.Equals(b.InitialCatalog,Db,StringComparison.OrdinalIgnoreCase))
                    {
                        for(int i=0;i<4&&!ct.IsCancellationRequested;i++)
                        {
                            var job=await ClaimAsync(cs,env,ct);if(job is null)break;
                            await SendAsync(cs,job,env,ct);
                        }
                    }
                }
            }
            catch(OperationCanceledException) when(ct.IsCancellationRequested){break;}
            catch(Exception ex){_logger.LogWarning("Autoauditoria: trabajador de avisos {Tipo}",ex.GetType().Name);}
            try{await Task.Delay(TimeSpan.FromSeconds(15),ct);}catch(OperationCanceledException) when(ct.IsCancellationRequested){break;}
        }
    }
    private static async Task<bool> EnabledAsync(SqlConnection cn,AaEntorno env,CancellationToken ct)
    {
        await using(var cmd=new SqlCommand("SELECT CASE WHEN CONVERT(nvarchar(128),SERVERPROPERTY('ServerName'))=@aaServer AND DB_NAME()=N'ERP_QUELL' AND OBJECT_ID(N'aa.V4Config',N'U') IS NOT NULL AND OBJECT_ID(N'aa.AvisosRespuesta',N'U') IS NOT NULL AND OBJECT_ID(N'aa.Instalacion',N'U') IS NOT NULL THEN 1 ELSE 0 END",cn))
        {env.Bind(cmd);if(Convert.ToInt32(await cmd.ExecuteScalarAsync(ct))!=1)return false;}
        await using var check=new SqlCommand("SELECT CASE WHEN EXISTS(SELECT 1 FROM aa.V4Config WHERE Id=1 AND Activo=1) AND EXISTS(SELECT 1 FROM aa.Instalacion WHERE Id=1 AND Activo=1 AND EsTest=@aaTest AND Servidor=@aaServer AND Servidor=CONVERT(nvarchar(128),SERVERPROPERTY('ServerName')) AND BaseDatos=DB_NAME() AND DB_NAME()=N'ERP_QUELL') THEN 1 ELSE 0 END",cn);
        env.Bind(check);return Convert.ToInt32(await check.ExecuteScalarAsync(ct))==1;
    }
    private async Task<Job?> ClaimAsync(string cs,AaEntorno env,CancellationToken ct)
    {
        await using var cn=new SqlConnection(cs);await cn.OpenAsync(ct);if(!await EnabledAsync(cn,env,ct))return null;
        var lease=Guid.NewGuid();
        const string sql="""
SET NOCOUNT ON; SET XACT_ABORT ON;
BEGIN TRAN;
DECLARE @lock int;
EXEC @lock=sys.sp_getapplock @Resource=N'NSQ_AA_V4_AVISOS',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=1000;
IF @lock<0 BEGIN ROLLBACK; RETURN; END;
IF NOT EXISTS(SELECT 1 FROM aa.V4Config WHERE Id=1 AND Activo=1) OR NOT EXISTS(SELECT 1 FROM aa.Instalacion WHERE Id=1 AND Activo=1 AND EsTest=@aaTest AND Servidor=@aaServer AND Servidor=CONVERT(nvarchar(128),SERVERPROPERTY('ServerName')) AND BaseDatos=DB_NAME() AND DB_NAME()=N'ERP_QUELL') BEGIN ROLLBACK; RETURN; END;
INSERT aa.AvisosRespuesta(SolicitudID,Destinatario)
SELECT s.Id,d.Correo FROM aa.Solicitudes s CROSS JOIN (VALUES(N'compras.gq@nsquell.com.mx'),(N'sgc.gq@nsquell.com.mx')) d(Correo)
CROSS JOIN aa.V4Config c WHERE c.Id=1 AND c.Activo=1 AND s.RespondidaUtc>=c.InicioUtc AND s.RespondidaUtc IS NOT NULL
AND NOT EXISTS(SELECT 1 FROM aa.AvisosRespuesta a WHERE a.SolicitudID=s.Id AND a.Destinatario=d.Correo);
-- If a process died during sending, do not risk duplicate SMTP delivery.
UPDATE aa.AvisosRespuesta SET Estado='INCIERTO',Reserva=NULL WHERE Estado='ENVIANDO' AND UltimoIntentoUtc<DATEADD(minute,-5,SYSUTCDATETIME());
;WITH q AS(SELECT TOP(1) * FROM aa.AvisosRespuesta WITH(UPDLOCK,ROWLOCK) WHERE Estado IN('PENDIENTE','BLOQUEADO','CONFIG') AND ProximoIntentoUtc<=SYSUTCDATETIME() ORDER BY SolicitudID,Destinatario)
UPDATE q SET Estado='ENVIANDO',Reserva=@lease,Intentos=Intentos+1,UltimoIntentoUtc=SYSUTCDATETIME();
COMMIT;
SELECT a.SolicitudID,a.Destinatario,s.Folio,s.Empresa,s.RespondidaUtc,
(SELECT COUNT(*) FROM aa.Respuestas r WHERE r.SolicitudID=s.Id AND r.Valoracion='VERDE'),
(SELECT COUNT(*) FROM aa.Respuestas r WHERE r.SolicitudID=s.Id AND r.Valoracion='AMARILLO'),
(SELECT COUNT(*) FROM aa.Respuestas r WHERE r.SolicitudID=s.Id AND r.Valoracion='ROJO'),
(SELECT COUNT(*) FROM aa.Respuestas r WHERE r.SolicitudID=s.Id AND r.Valoracion='n.b.')
FROM aa.AvisosRespuesta a JOIN aa.Solicitudes s ON s.Id=a.SolicitudID WHERE a.Reserva=@lease AND a.Estado='ENVIANDO';
""";
        await using var cmd=new SqlCommand(sql,cn);cmd.Parameters.Add("@lease",SqlDbType.UniqueIdentifier).Value=lease;env.Bind(cmd);
        await using var r=await cmd.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))return null;
        return new Job(r.GetInt64(0),r.GetString(1),lease,r.GetString(2),r.GetString(3),r.GetDateTime(4),r.GetInt32(5),r.GetInt32(6),r.GetInt32(7),r.GetInt32(8));
    }
    private async Task SendAsync(string cs,Job job,AaEntorno env,CancellationToken ct)
    {
        string state;
        try
        {
            using var scope=_scopes.CreateScope();var mail=scope.ServiceProvider.GetRequiredService<ServicioNotificaciones>();
            var empresa=WebUtility.HtmlEncode(job.Empresa);var folio=WebUtility.HtmlEncode(job.Folio);
            var detalle=WebUtility.HtmlEncode(env.ErpBase+"/AutoAuditoria/Detalle/"+job.Id);
            var etiqueta=env.Prod?"ERP NS Quell":"ERP TEST";
            var aviso=env.Prod?"Requiere acceso interno al ERP.":"PRUEBA INTERNA. Requiere acceso interno al ERP TEST.";
            var html=$"""
<!doctype html><html lang="es"><head><meta charset="utf-8"></head><body style="margin:0;background:#eef2f7;font:15px Segoe UI,Arial,sans-serif;color:#24354a">
<table role="presentation" width="100%" cellpadding="0" cellspacing="0" bgcolor="#eef2f7"><tr><td align="center" style="padding:24px 12px"><table role="presentation" width="600" cellpadding="0" cellspacing="0" bgcolor="#ffffff" style="width:100%;max-width:600px;border:1px solid #dce5ee"><tr><td bgcolor="#12345a" style="background:#12345a;border-bottom:4px solid #ed7a22;padding:22px;color:#ffffff"><table role="presentation" cellspacing="0" cellpadding="0"><tr><td bgcolor="#ffffff" style="padding:8px"><img src="cid:nsq-aa-logo@nsquell.com.mx" alt="NS Quell" width="76" style="display:block;width:76px;height:auto;border:0"></td><td style="padding-left:14px;color:#ffffff;font-weight:bold">NS QUELL</td></tr></table><h1 style="font-size:23px;margin:16px 0 0;color:#ffffff">Autoauditoría recibida</h1></td></tr>
<tr><td style="padding:26px;line-height:1.6"><p><strong>Equipo de Compras y SGC:</strong></p><p>La empresa <strong>{empresa}</strong> ha completado su autoauditoría de fabricantes. Las respuestas se guardaron en {etiqueta}.</p><p><strong>Folio:</strong> {folio}<br><strong>Fecha de recepción:</strong> {job.Fecha:dd/MM/yyyy HH:mm} UTC<br>GQ-F-CP02-05 · Ver. 01</p>
<table role="presentation" width="100%" cellspacing="0" cellpadding="9" style="border:1px solid #dce5ee;text-align:center"><tr><td bgcolor="#dcfce7" style="color:#166534">Verde<br><strong>{job.V}</strong></td><td bgcolor="#fef3c7" style="color:#854d0e">Amarillo<br><strong>{job.A}</strong></td><td bgcolor="#fee2e2" style="color:#991b1b">Rojo<br><strong>{job.R}</strong></td><td bgcolor="#e2e8f0" style="color:#334155">n.b.<br><strong>{job.Nb}</strong></td></tr></table>
<p>Consulte las respuestas y observaciones dentro de Compras → Autoauditorías. La clasificación global continúa pendiente de validación por SGC.</p><table role="presentation" cellspacing="0" cellpadding="0" style="margin:20px 0"><tr><td bgcolor="#174b80" style="background:#174b80;padding:13px 20px"><a style="color:#ffffff;font-weight:bold;text-decoration:none" href="{detalle}">Ver detalle</a></td></tr></table><p style="font-size:12px">{aviso}</p><p>Atentamente,<br><strong>NS Quell · Sistema de formularios</strong></p></td></tr></table></td></tr></table></body></html>
""";
            var result=await mail.EnviarInvitacionAutoAuditoriaTestAsync(job.Para,(env.Prod?"":"[TEST] ")+"NS Quell | Autoauditoría recibida | "+job.Folio,html,true,env.Prod);
            state=result switch{"CORREO_ENVIADO"=>"ENVIADO","CORREO_BLOQUEADO"=>"BLOQUEADO","CORREO_CONFIG" or "CORREO_FALTA_HOST" or "CORREO_FALTA_FROM" or "CORREO_FALTA_PASS" or "CORREO_AUTH" or "CORREO_CONEXION"=>"CONFIG",_=>"INCIERTO"};
        }
        catch(Exception ex){_logger.LogWarning("Aviso respuesta: {Tipo}",ex.GetType().Name);state="INCIERTO";}
        // Record outcome even if stopping after SMTP accepted. No automatic retry of uncertain outcomes.
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var cn=new SqlConnection(cs);await cn.OpenAsync(deadline.Token);
        await using var cmd=new SqlCommand("UPDATE aa.AvisosRespuesta SET Estado=@s,Reserva=NULL,EnviadoUtc=CASE WHEN @s='ENVIADO' THEN SYSUTCDATETIME() ELSE EnviadoUtc END,ProximoIntentoUtc=DATEADD(minute,5,SYSUTCDATETIME()) WHERE SolicitudID=@id AND Destinatario=@d AND Reserva=@r AND Estado='ENVIANDO'",cn);
        cmd.Parameters.Add("@s",SqlDbType.VarChar,20).Value=state;cmd.Parameters.Add("@id",SqlDbType.BigInt).Value=job.Id;cmd.Parameters.Add("@d",SqlDbType.NVarChar,150).Value=job.Para;cmd.Parameters.Add("@r",SqlDbType.UniqueIdentifier).Value=job.Reserva;await cmd.ExecuteNonQueryAsync(deadline.Token);
    }
}
