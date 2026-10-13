using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Servicios;

// Correos accionables a Producción: una entrega pendiente, no cada edición de un checklist.
public sealed class ProduccionRecepcionCorreoWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly string _cs;
    private readonly ILogger<ProduccionRecepcionCorreoWorker> _logger;
    public ProduccionRecepcionCorreoWorker(IServiceScopeFactory scopes,IConfiguration config,ILogger<ProduccionRecepcionCorreoWorker> logger)
    {_scopes=scopes;_cs=config.GetConnectionString("DefaultConnection")??throw new InvalidOperationException("Falta conexión SQL.");_logger=logger;}
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(30));
        try
        {
            do
            {
                try{await RevisarAsync(stoppingToken);}
                catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
                catch(Exception ex){_logger.LogError(ex,"No se pudo revisar recepción pendiente para correo; se reintentará.");}
            }while(await timer.WaitForNextTickAsync(stoppingToken));
        }catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){}
    }
    private async Task RevisarAsync(CancellationToken ct)
    {
        using var scope=_scopes.CreateScope();var correo=scope.ServiceProvider.GetRequiredService<NotificacionCorreoErpService>();
        var queue=scope.ServiceProvider.GetRequiredService<ICorreoErpBackgroundQueue>();
        await using var cn=new SqlConnection(_cs);await cn.OpenAsync(ct);
        var filas=new List<(string Clave,string Codigo,string OF,string Material,decimal Kg,int Programa)>();
        const string sql="""
;WITH Pendientes AS(
 SELECT CONCAT(N'RECEPCION_MP:',r.MovimientoAlmacenID) Clave,N'PROD_RECEPCION_MP' Codigo,
 COALESCE(NULLIF(s.NumeroOFRecibida,N''),s.FolioSolicitud) NumeroOF,
 CONCAT(m.Codigo,N' · ',m.Nombre) Material,MAX(r.CantidadEntregadaAlmacen) Kg,MIN(pp.ProgramaProduccionID) Programa
 FROM dbo.Produccion_RecepcionMateriales r
 INNER JOIN dbo.AlmacenMP_Movimientos mm ON mm.MovimientoID=r.MovimientoAlmacenID AND mm.Activo=1
 INNER JOIN dbo.ERP_Materiales m ON m.MaterialID=mm.MaterialID
 INNER JOIN dbo.SolicitudesProduccion s ON s.SolicitudProduccionID=r.SolicitudProduccionID AND s.Activo=1 AND s.EstatusID<>99
 INNER JOIN dbo.Planeacion_ProgramaProduccion pp ON pp.SolicitudProduccionID=s.SolicitudProduccionID AND pp.Activo=1 AND pp.EstatusID IN(1,2,3,4) AND NOT EXISTS(SELECT 1 FROM dbo.Produccion_Ejecucion e WHERE e.ProgramaProduccionID=pp.ProgramaProduccionID AND e.Activo=1 AND e.FechaFinReal IS NOT NULL)
 WHERE r.Activo=1 AND r.TipoOrigen=N'MP' AND r.EstadoRecepcion=N'PENDIENTE'
 GROUP BY r.MovimientoAlmacenID,s.NumeroOFRecibida,s.FolioSolicitud,m.Codigo,m.Nombre
 UNION ALL
 SELECT CONCAT(N'RECEPCION_PURGA:',p.PurgaID),N'PROD_RECEPCION_PURGA',COALESCE(NULLIF(s.NumeroOFRecibida,N''),s.FolioSolicitud),
 CONCAT(m.Codigo,N' · ',m.Nombre),p.CantidadKg,MIN(pp.ProgramaProduccionID)
 FROM dbo.NSQ_AlmacenMP_Purgas p
 INNER JOIN dbo.AlmacenMP_Movimientos mm ON mm.MovimientoID=p.MovimientoID AND mm.Activo=1
 INNER JOIN dbo.ERP_Materiales m ON m.MaterialID=mm.MaterialID
 INNER JOIN dbo.SolicitudesProduccion s ON s.SolicitudProduccionID=p.SolicitudProduccionID AND s.Activo=1 AND s.EstatusID<>99
 INNER JOIN dbo.Planeacion_ProgramaProduccion pp ON pp.SolicitudProduccionID=s.SolicitudProduccionID AND pp.Activo=1 AND pp.EstatusID IN(1,2,3,4) AND NOT EXISTS(SELECT 1 FROM dbo.Produccion_Ejecucion e WHERE e.ProgramaProduccionID=pp.ProgramaProduccionID AND e.Activo=1 AND e.FechaFinReal IS NOT NULL)
 WHERE p.Activo=1 AND p.FechaRecepcionProduccion IS NULL
 GROUP BY p.PurgaID,s.NumeroOFRecibida,s.FolioSolicitud,m.Codigo,m.Nombre,p.CantidadKg
)
SELECT TOP(100) Clave,Codigo,NumeroOF,Material,Kg,Programa FROM Pendientes p
WHERE NOT EXISTS(SELECT 1 FROM dbo.NSQ_CorreoSalida q WHERE q.Referencia=p.Clave AND q.CodigoEvento=p.Codigo AND q.Estado<>N'CANCELADO')
ORDER BY Clave;
""";
        await using(var cmd=new SqlCommand(sql,cn))
        {await using var rd=await cmd.ExecuteReaderAsync(ct);while(await rd.ReadAsync(ct))filas.Add((Convert.ToString(rd[0])??"",Convert.ToString(rd[1])??"",Convert.ToString(rd[2])??"",Convert.ToString(rd[3])??"",Convert.ToDecimal(rd[4]),Convert.ToInt32(rd[5])));}
        if(filas.Count==0)return;
        var usuarios=await correo.ObtenerUsuariosDepartamentoAsync("Producción");
        if(usuarios.Count==0){_logger.LogWarning("No hay destinatarios de Producción con correo registrado para recibir avisos de MP.");return;}
        foreach(var p in filas)
        {
            var purga=p.Codigo=="PROD_RECEPCION_PURGA";
            await queue.EnqueueAsync(new CorreoErpTrabajo(usuarios,$"Recepción {(purga?"de purga":"de MP")} pendiente · {p.OF}",
                $"Almacén entregó {p.Kg:0.###} KG de {p.Material}. Producción debe confirmar lo recibido{(purga?"; esta purga es independiente de la cantidad de la OF":"")}. OF: {p.OF}.",
                $"/Produccion/CalendarioOperativo?programaProduccionId={p.Programa}",p.Codigo,"Producción",false,"Recepcionar material",p.Clave),ct);
        }
    }
}
