using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartInbox.Application.Messages.Classification;

namespace SmartInbox.Infrastructure.Messages.Classification;

internal sealed class InboundMessageBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<EmailProcessingOptions> options,
    TimeProvider timeProvider,
    ILogger<InboundMessageBackgroundService> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
            return Task.CompletedTask;

        return Task.WhenAll(Enumerable.Range(0, options.Value.MaxConcurrency)
            .Select(_ => RunWorkerAsync(stoppingToken)));
    }

    private async Task RunWorkerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<InboundMessageProcessor>();
                if (!await processor.ProcessNextAsync(cancellationToken))
                    await Task.Delay(TimeSpan.FromSeconds(options.Value.PollIntervalSeconds), timeProvider, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    "Email background processing iteration failed exceptionType={ExceptionType}",
                    exception.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(options.Value.PollIntervalSeconds), timeProvider, cancellationToken);
            }
        }
    }
}
