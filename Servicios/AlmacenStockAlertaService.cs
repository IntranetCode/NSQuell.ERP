using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Servicios;

// NSQ_ALERTAS_STOCK_MP_V1_0. Las notificaciones internas se registran después de la operación.
public sealed class AlmacenStockAlertaService
{
    private readonly IConfiguration _config;
    private readonly ILogger<AlmacenStockAlertaService> _logger;
    public AlmacenStockAlertaService(IConfiguration config, ILogger<AlmacenStockAlertaService> logger)
    { _config = config; _logger = logger; }
    public async Task RevisarAsync(CancellationToken ct = default)
    {
        try
        {
            await using var cn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            await cn.OpenAsync(ct);
            await using var cmd = new SqlCommand("dbo.usp_NSQ_AlmacenStockAlertasV1", cn)
            { CommandType = CommandType.StoredProcedure, CommandTimeout = 15 };
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        { _logger.LogError(ex, "No se pudo revisar stock MP. La operación confirmada se conserva; el monitor reintentará."); }
    }
}

public sealed class AlmacenStockAlertaWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    public AlmacenStockAlertaWorker(IServiceScopeFactory scopeFactory) { _scopeFactory = scopeFactory; }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try
        {
            do
            {
                using var scope = _scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<AlmacenStockAlertaService>().RevisarAsync(stoppingToken);
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
