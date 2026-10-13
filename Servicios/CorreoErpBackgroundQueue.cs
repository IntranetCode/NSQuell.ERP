// NSQ_CORREOS_DURABLES_V2_0: SQL conserva pendientes al reiniciar IIS; sin SMTP en requests.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ERP.NSQuell.Models.Opciones;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using System.Data;

namespace ERP.NSQuell.Servicios;

public sealed record CorreoErpTrabajo(IReadOnlyList<int> UsuarioIds,string Titulo,string Mensaje,string? UrlDestino,
    string? CodigoEvento,string? Departamento,bool Urgente=false,string? TextoBoton=null,string? ClaveUnica=null);

public interface ICorreoErpBackgroundQueue
{
    bool TryEnqueue(CorreoErpTrabajo trabajo);
    Task<bool> EnqueueAsync(CorreoErpTrabajo trabajo,CancellationToken ct=default);
}

public sealed class CorreoErpBackgroundQueue : ICorreoErpBackgroundQueue
{
    private readonly string _cs;
    private readonly CorreoOpciones _opciones;
    private readonly ILogger<CorreoErpBackgroundQueue> _logger;
    public CorreoErpBackgroundQueue(IConfiguration config,IOptions<CorreoOpciones> opciones,ILogger<CorreoErpBackgroundQueue> logger)
    {_cs=config.GetConnectionString("DefaultConnection")??throw new InvalidOperationException("Falta conexión SQL.");_opciones=opciones.Value;_logger=logger;}
    public bool TryEnqueue(CorreoErpTrabajo trabajo)=>EnqueueAsync(trabajo).GetAwaiter().GetResult();
    public async Task<bool> EnqueueAsync(CorreoErpTrabajo trabajo,CancellationToken ct=default)
    {
        try
        {
            var ids=trabajo.UsuarioIds.Where(x=>x>0).Distinct().OrderBy(x=>x).ToArray();if(ids.Length==0)return false;
            await using var cn=new SqlConnection(_cs);await cn.OpenAsync(ct);
            // Varios usuarios con el mismo correo reciben una sola copia. Usuarios sin correo quedan visibles en la cola.
            var destino=new List<int>();
            var idsJson=JsonSerializer.Serialize(ids);
            await using(var cmd=new SqlCommand("""
SELECT MIN(u.UsuarioID) FROM dbo.Usuarios u
LEFT JOIN dbo.Persona p ON p.PersonaID=u.PersonaID
INNER JOIN OPENJSON(@IDs) j ON u.UsuarioID=TRY_CONVERT(int,j.value)
WHERE u.Activo=1 GROUP BY COALESCE(NULLIF(LOWER(LTRIM(RTRIM(p.Correo))),N''),CONCAT(N'UID:',u.UsuarioID));
""",cn))
            {
                cmd.Parameters.Add("@IDs",SqlDbType.NVarChar,-1).Value=idsJson;
                await using var rd=await cmd.ExecuteReaderAsync(ct);while(await rd.ReadAsync(ct))destino.Add(Convert.ToInt32(rd[0]));
            }
            if(destino.Count==0)return false;
            // El adaptador copia a ListaBlanca en pruebas. Una copia por evento, no una por cada usuario real.
            if(_opciones.SoloPruebas)destino=destino.Take(1).ToList();
            await using var tx=(SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable,ct);
            foreach(var id in destino)
            {
                var payload=JsonSerializer.Serialize(trabajo with{UsuarioIds=new[]{id}});
                var clave=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes((trabajo.ClaveUnica??JsonSerializer.Serialize(trabajo with{UsuarioIds=Array.Empty<int>()}))+"|"+id+"|"+_opciones.SoloPruebas)));
                await using var cmd=new SqlCommand("""
IF NOT EXISTS(SELECT 1 FROM dbo.NSQ_CorreoSalida WITH(UPDLOCK,HOLDLOCK) WHERE Clave=@Clave)
 INSERT dbo.NSQ_CorreoSalida(Clave,UsuarioID,CodigoEvento,Departamento,Referencia,Contenido,Estado,Intentos,Creado,ProximoIntento)
 VALUES(@Clave,@Usuario,@Codigo,@Departamento,@Referencia,@Contenido,N'PENDIENTE',0,SYSDATETIME(),SYSDATETIME());
""",cn,tx);
                cmd.Parameters.Add("@Clave",SqlDbType.Char,64).Value=clave;cmd.Parameters.Add("@Usuario",SqlDbType.Int).Value=id;
                cmd.Parameters.Add("@Codigo",SqlDbType.NVarChar,80).Value=(object?)trabajo.CodigoEvento??DBNull.Value;
                cmd.Parameters.Add("@Departamento",SqlDbType.NVarChar,150).Value=(object?)trabajo.Departamento??DBNull.Value;
                cmd.Parameters.Add("@Referencia",SqlDbType.NVarChar,200).Value=(object?)trabajo.ClaveUnica??DBNull.Value;
                cmd.Parameters.Add("@Contenido",SqlDbType.NVarChar,-1).Value=payload;await cmd.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);return true;
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
        catch(Exception ex){_logger.LogError(ex,"No se pudo persistir correo {Codigo}. Revisar SQL NSQ_CorreoSalida.",trabajo.CodigoEvento);return false;}
    }
}

public sealed class CorreoErpBackgroundWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly string _cs;
    private readonly ILogger<CorreoErpBackgroundWorker> _logger;
    public CorreoErpBackgroundWorker(IServiceScopeFactory scopes,IConfiguration config,ILogger<CorreoErpBackgroundWorker> logger)
    {_scopes=scopes;_cs=config.GetConnectionString("DefaultConnection")??throw new InvalidOperationException("Falta conexión SQL.");_logger=logger;}
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while(!stoppingToken.IsCancellationRequested)
        {
            try{if(await ProcesarUnoAsync(stoppingToken))continue;}
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception ex){_logger.LogError(ex,"No se pudo procesar cola durable SMTP; se reintentará.");}
            try{await Task.Delay(TimeSpan.FromSeconds(15),stoppingToken);}catch(OperationCanceledException){break;}
        }
    }
    private async Task<bool> ProcesarUnoAsync(CancellationToken ct)
    {
        await using var cn=new SqlConnection(_cs);await cn.OpenAsync(ct);
        var lease=Guid.NewGuid();long id;int intentos;string contenido;
        await using(var cmd=new SqlCommand("""
;WITH siguiente AS(
 SELECT TOP(1)* FROM dbo.NSQ_CorreoSalida WITH(UPDLOCK,READPAST,ROWLOCK)
 WHERE ((Estado IN(N'PENDIENTE',N'REINTENTO',N'BLOQUEADO') AND ProximoIntento<=SYSDATETIME())
 OR (Estado=N'PROCESANDO' AND ReservaHasta<SYSDATETIME())) AND Intentos<8
 ORDER BY ProximoIntento,CorreoID)
UPDATE siguiente SET Estado=N'PROCESANDO',Reserva=@Lease,ReservaHasta=DATEADD(MINUTE,5,SYSDATETIME())
OUTPUT inserted.CorreoID,inserted.Intentos,inserted.Contenido;
""",cn))
        {
            cmd.Parameters.Add("@Lease",SqlDbType.UniqueIdentifier).Value=lease;
            await using var rd=await cmd.ExecuteReaderAsync(ct);if(!await rd.ReadAsync(ct))return false;
            id=Convert.ToInt64(rd[0]);intentos=Convert.ToInt32(rd[1]);contenido=Convert.ToString(rd[2])??"";
        }
        string estado,detalle;bool enviado=false;int espera;
        try
        {
            var job=JsonSerializer.Deserialize<CorreoErpTrabajo>(contenido)??throw new InvalidOperationException("Contenido de correo inválido.");
            await using(var vigente=new SqlCommand("""
SELECT CASE
 WHEN NOT EXISTS(SELECT 1 FROM dbo.Usuarios WHERE UsuarioID=@UsuarioID AND Activo=1) THEN 0
 WHEN @Codigo=N'MP_STOCK_ALERTA_V2' THEN CASE WHEN EXISTS(SELECT 1 FROM dbo.NSQ_StockAlertasMP WHERE Activa=1 AND CONCAT(N'STOCK:',AlertaID)=@Referencia) THEN 1 ELSE 0 END
 WHEN @Codigo=N'PROD_RECEPCION_MP' THEN CASE WHEN EXISTS(SELECT 1 FROM dbo.Produccion_RecepcionMateriales WHERE Activo=1 AND EstadoRecepcion=N'PENDIENTE' AND CONCAT(N'RECEPCION_MP:',MovimientoAlmacenID)=@Referencia) THEN 1 ELSE 0 END
 WHEN @Codigo=N'PROD_RECEPCION_PURGA' THEN CASE WHEN EXISTS(SELECT 1 FROM dbo.NSQ_AlmacenMP_Purgas WHERE Activo=1 AND FechaRecepcionProduccion IS NULL AND CONCAT(N'RECEPCION_PURGA:',PurgaID)=@Referencia) THEN 1 ELSE 0 END
 ELSE 1 END;
""",cn))
            {
                vigente.Parameters.Add("@UsuarioID",SqlDbType.Int).Value=job.UsuarioIds.FirstOrDefault();
                vigente.Parameters.Add("@Codigo",SqlDbType.NVarChar,80).Value=(object?)job.CodigoEvento??DBNull.Value;
                vigente.Parameters.Add("@Referencia",SqlDbType.NVarChar,200).Value=(object?)job.ClaveUnica??DBNull.Value;
                if(Convert.ToInt32(await vigente.ExecuteScalarAsync(ct))==0)
                {
                    await using var cancelar=new SqlCommand("UPDATE dbo.NSQ_CorreoSalida SET Estado=N'CANCELADO',UltimoResultado=N'Pendiente resuelto antes de enviar.',Reserva=NULL,ReservaHasta=NULL WHERE CorreoID=@ID AND Reserva=@Lease;",cn);
                    cancelar.Parameters.Add("@ID",SqlDbType.BigInt).Value=id;cancelar.Parameters.Add("@Lease",SqlDbType.UniqueIdentifier).Value=lease;
                    await cancelar.ExecuteNonQueryAsync(ct);return true;
                }
            }
            using var scope=_scopes.CreateScope();var correo=scope.ServiceProvider.GetRequiredService<NotificacionCorreoErpService>();
            var res=await correo.EnviarAUsuariosAsync(job.UsuarioIds,job.Titulo,job.Mensaje,job.UrlDestino,job.CodigoEvento,job.Departamento,job.Urgente,job.TextoBoton);
            enviado=res.Enviados>0&&res.Errores==0;
            var bloqueado=res.Errores==0&&!enviado;
            estado=enviado?"ENVIADO":bloqueado?"BLOQUEADO":intentos+1>=8?"ERROR":"REINTENTO";
            espera=bloqueado?360:(int)Math.Min(60,Math.Pow(2,intentos));
            detalle=$"Encontrados={res.Encontrados}; Enviados={res.Enviados}; Bloqueados={res.FiltradosPorCandados}; Errores={res.Errores}. "+string.Join(" | ",res.Mensajes.Take(3));
        }
        catch(Exception ex){estado=intentos+1>=8?"ERROR":"REINTENTO";espera=(int)Math.Min(60,Math.Pow(2,intentos));detalle=ex.Message;}
        await using(var cmd=new SqlCommand("""
UPDATE dbo.NSQ_CorreoSalida SET Estado=@Estado,Intentos=Intentos+CASE WHEN @Estado=N'BLOQUEADO' THEN 0 ELSE 1 END,
 UltimoIntento=SYSDATETIME(),Enviado=CASE WHEN @Enviado=1 THEN SYSDATETIME() ELSE Enviado END,
 ProximoIntento=DATEADD(MINUTE,@Espera,SYSDATETIME()),UltimoResultado=@Resultado,Reserva=NULL,ReservaHasta=NULL
WHERE CorreoID=@ID AND Reserva=@Lease;
""",cn))
        {
            cmd.Parameters.Add("@ID",SqlDbType.BigInt).Value=id;cmd.Parameters.Add("@Lease",SqlDbType.UniqueIdentifier).Value=lease;
            cmd.Parameters.Add("@Estado",SqlDbType.NVarChar,20).Value=estado;cmd.Parameters.Add("@Enviado",SqlDbType.Bit).Value=enviado;
            cmd.Parameters.Add("@Espera",SqlDbType.Int).Value=espera;cmd.Parameters.Add("@Resultado",SqlDbType.NVarChar,1000).Value=detalle[..Math.Min(detalle.Length,1000)];
            await cmd.ExecuteNonQueryAsync(ct);
        }
        _logger.LogInformation("Correo SQL #{ID}: {Estado}.",id,estado);return true;
    }
}