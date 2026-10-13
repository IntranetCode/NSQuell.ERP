using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Servicios;

// NSQ_ALERTAS_STOCK_MP_V2_0. Un correo por episodio/material u OF, no por cada revisión.
public sealed class AlmacenStockAlertaService
{
    private readonly IConfiguration _config;
    private readonly ICorreoErpBackgroundQueue _queue;
    private readonly ILogger<AlmacenStockAlertaService> _logger;
    public AlmacenStockAlertaService(IConfiguration config,ICorreoErpBackgroundQueue queue,ILogger<AlmacenStockAlertaService> logger)
    {_config=config;_queue=queue;_logger=logger;}
    public async Task RevisarAsync(CancellationToken ct=default)
    {
        try
        {
            await using var cn=new SqlConnection(_config.GetConnectionString("DefaultConnection"));await cn.OpenAsync(ct);
            await using(var cmd=new SqlCommand("dbo.usp_NSQ_AlmacenStockAlertasV1",cn){CommandType=CommandType.StoredProcedure,CommandTimeout=20})
                await cmd.ExecuteNonQueryAsync(ct);
            // Cancelar correos pendientes de una escasez ya resuelta. Conservar el historial de enviados.
            await using(var cmd=new SqlCommand("""
UPDATE q SET Estado=N'CANCELADO',UltimoResultado=N'Faltante resuelto antes de enviar.'
FROM dbo.NSQ_CorreoSalida q INNER JOIN dbo.NSQ_StockAlertasMP a
 ON q.Referencia=CONCAT(N'STOCK:',a.AlertaID)
WHERE a.Activa=0 AND q.Estado IN(N'PENDIENTE',N'REINTENTO',N'BLOQUEADO') AND q.CodigoEvento=N'MP_STOCK_ALERTA_V2';
""",cn))await cmd.ExecuteNonQueryAsync(ct);
            var avisos=new List<(int ID,string Titulo,string Mensaje,string Url)>();
            await using(var cmd=new SqlCommand("SELECT AlertaID,Titulo,Mensaje,UrlDestino FROM dbo.NSQ_StockAlertasMP WHERE Activa=1 AND MaterialID IS NOT NULL AND CorreoEncolado IS NULL ORDER BY AlertaID;",cn))
            {await using var rd=await cmd.ExecuteReaderAsync(ct);while(await rd.ReadAsync(ct))avisos.Add((Convert.ToInt32(rd[0]),Convert.ToString(rd[1])??"",Convert.ToString(rd[2])??"",Convert.ToString(rd[3])??""));}
            foreach(var a in avisos)
            {
                var ids=new List<int>();
                // Los destinatarios son exactamente los usuarios de Almacén de la notificación interna.
                await using(var cmd=new SqlCommand("SELECT DISTINCT n.UsuarioId FROM dbo.Notificaciones n INNER JOIN dbo.Usuarios u ON u.UsuarioID=n.UsuarioId AND u.Activo=1 WHERE n.CodigoEvento=N'MP_STOCK_ALERTA_V1' AND n.IdOrigen=@ID;",cn))
                {cmd.Parameters.Add("@ID",SqlDbType.Int).Value=a.ID;await using var rd=await cmd.ExecuteReaderAsync(ct);while(await rd.ReadAsync(ct))ids.Add(Convert.ToInt32(rd[0]));}
                if(ids.Count==0){_logger.LogWarning("Alerta MP #{ID}: no hay usuarios activos de Almacén para avisar.",a.ID);continue;}
                if(await _queue.EnqueueAsync(new CorreoErpTrabajo(ids,a.Titulo,a.Mensaje,a.Url,"MP_STOCK_ALERTA_V2","Almacén",true,"Revisar faltante",$"STOCK:{a.ID}"),ct))
                {await using var cmd=new SqlCommand("UPDATE dbo.NSQ_StockAlertasMP SET CorreoEncolado=SYSDATETIME() WHERE AlertaID=@ID AND Activa=1 AND CorreoEncolado IS NULL;",cn);cmd.Parameters.Add("@ID",SqlDbType.Int).Value=a.ID;await cmd.ExecuteNonQueryAsync(ct);}
            }
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested){}
        catch(Exception ex){_logger.LogError(ex,"No se pudo revisar o avisar stock MP; la operación confirmada se conserva y el monitor reintentará.");}
    }
}

public sealed class AlmacenStockAlertaWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    public AlmacenStockAlertaWorker(IServiceScopeFactory scopes){_scopes=scopes;}
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(30));
        try{do{using var scope=_scopes.CreateScope();await scope.ServiceProvider.GetRequiredService<AlmacenStockAlertaService>().RevisarAsync(stoppingToken);}while(await timer.WaitForNextTickAsync(stoppingToken));}
        catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){}
    }
}
