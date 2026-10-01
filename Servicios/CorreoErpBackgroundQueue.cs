// NSQ_SMTP_BACKGROUND_QUEUE_V1_0
// Cola en memoria para sacar el transporte SMTP del request HTTP.
// La notificacion interna sigue persistiendo en SQL antes de encolar el correo.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ERP.NSQuell.Servicios;

public sealed record CorreoErpTrabajo(
    IReadOnlyList<int> UsuarioIds,
    string Titulo,
    string Mensaje,
    string? UrlDestino,
    string? CodigoEvento,
    string? Departamento,
    bool Urgente = false,
    string? TextoBoton = null);

public interface ICorreoErpBackgroundQueue
{
    bool TryEnqueue(CorreoErpTrabajo trabajo);
    ValueTask<CorreoErpTrabajo> DequeueAsync(CancellationToken cancellationToken);
}

public sealed class CorreoErpBackgroundQueue : ICorreoErpBackgroundQueue
{
    private readonly Channel<CorreoErpTrabajo> _channel;

    public CorreoErpBackgroundQueue()
    {
        _channel = Channel.CreateBounded<CorreoErpTrabajo>(
            new BoundedChannelOptions(1000)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false
            });
    }

    public bool TryEnqueue(CorreoErpTrabajo trabajo)
        => _channel.Writer.TryWrite(trabajo);

    public ValueTask<CorreoErpTrabajo> DequeueAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAsync(cancellationToken);
}

public sealed class CorreoErpBackgroundWorker : BackgroundService
{
    private readonly ICorreoErpBackgroundQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CorreoErpBackgroundWorker> _logger;

    public CorreoErpBackgroundWorker(
        ICorreoErpBackgroundQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<CorreoErpBackgroundWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            CorreoErpTrabajo trabajo;

            try
            {
                trabajo = await _queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var correo = scope.ServiceProvider.GetRequiredService<NotificacionCorreoErpService>();

                var resultado = await correo.EnviarAUsuariosAsync(
                    trabajo.UsuarioIds,
                    trabajo.Titulo,
                    trabajo.Mensaje,
                    trabajo.UrlDestino,
                    trabajo.CodigoEvento,
                    trabajo.Departamento,
                    trabajo.Urgente,
                    trabajo.TextoBoton);

                _logger.LogInformation(
                    "SMTP background {CodigoEvento}: encontrados={Encontrados}; enviados={Enviados}; bloqueados={Bloqueados}; errores={Errores}.",
                    trabajo.CodigoEvento ?? "SIN_CODIGO",
                    resultado.Encontrados,
                    resultado.Enviados,
                    resultado.FiltradosPorCandados,
                    resultado.Errores);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error en SMTP background para {CodigoEvento}. La operacion ERP ya habia terminado.",
                    trabajo.CodigoEvento ?? "SIN_CODIGO");
            }
        }
    }
}