using Amazon.SQS;
using Amazon.SQS.Model;
using BancoCarrefour.Consolidation.Application;
using BancoCarrefour.Consolidation.Application.ApplyFinancialEntry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Text.Json;

namespace BancoCarrefour.Consolidation.Worker.Sqs;

public sealed class SqsFinancialEntryConsumer(
    IAmazonSQS sqs,
    IServiceScopeFactory scopeFactory,
    IOptions<SqsOptions> options,
    ILogger<SqsFinancialEntryConsumer> logger)
{
    public async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        var request = new ReceiveMessageRequest
        {
            QueueUrl = options.Value.QueueUrl,
            MaxNumberOfMessages = Math.Clamp(options.Value.MaxNumberOfMessages, 1, 10),
            WaitTimeSeconds = Math.Clamp(options.Value.WaitTimeSeconds, 0, 20),
            VisibilityTimeout = Math.Max(1, options.Value.VisibilityTimeoutSeconds),
            MessageSystemAttributeNames = ["ApproximateReceiveCount"],
            MessageAttributeNames = ["All"]
        };

        var response = await sqs.ReceiveMessageAsync(request, cancellationToken);
        foreach (var message in response.Messages)
        {
            await ProcessAsync(message, cancellationToken);
        }
    }

    private async Task ProcessAsync(Message message, CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var receiveCount = GetReceiveCount(message);
        using var activity = Observability.ActivitySource.StartActivity("consolidation.sqs.process");

        Observability.EventsConsumed.Add(1);
        activity?.SetTag("messaging.system", "aws.sqs");
        activity?.SetTag("messaging.message.id", message.MessageId);
        activity?.SetTag("messaging.receive_count", receiveCount);

        try
        {
            var command = FinancialEntryMessageParser.Parse(message.Body);
            activity?.SetTag("event.id", command.EventId);
            activity?.SetTag("correlation.id", command.CorrelationId);
            activity?.SetTag("merchant.id", command.MerchantId);
            activity?.SetTag("business.date", command.BusinessDate);

            using var scope = scopeFactory.CreateScope();
            var useCase = scope.ServiceProvider.GetRequiredService<IApplyFinancialEntryUseCase>();
            var result = await useCase.ApplyAsync(command, cancellationToken);

            await sqs.DeleteMessageAsync(options.Value.QueueUrl, message.ReceiptHandle, cancellationToken);
            Observability.EventsDeleted.Add(1);
            activity?.SetStatus(ActivityStatusCode.Ok);

            if (result.Duplicate)
            {
                Observability.EventsDuplicated.Add(1);
                logger.LogInformation(
                    "Evento duplicado descartado após commit idempotente. MessageId={MessageId}; EventId={EventId}; CorrelationId={CorrelationId}; ApproximateReceiveCount={ApproximateReceiveCount}",
                    message.MessageId,
                    command.EventId,
                    command.CorrelationId,
                    receiveCount);
            }
            else
            {
                Observability.EventsProcessed.Add(1);
                logger.LogInformation(
                    "Evento aplicado e mensagem SQS excluída. MessageId={MessageId}; EventId={EventId}; MerchantId={MerchantId}; BusinessDate={BusinessDate}; CorrelationId={CorrelationId}; ApproximateReceiveCount={ApproximateReceiveCount}",
                    message.MessageId,
                    command.EventId,
                    command.MerchantId,
                    result.BusinessDate,
                    command.CorrelationId,
                    receiveCount);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ProjectionValidationException exception)
        {
            Observability.EventsProcessingFailed.Add(1);
            activity?.SetStatus(ActivityStatusCode.Error, "projection validation error");
            logger.LogWarning(
                exception,
                "Mensagem SQS inválida mantida para redelivery e DLQ por redrive policy. MessageId={MessageId}; ApproximateReceiveCount={ApproximateReceiveCount}",
                message.MessageId,
                receiveCount);
        }
        catch (JsonException exception)
        {
            Observability.EventsProcessingFailed.Add(1);
            activity?.SetStatus(ActivityStatusCode.Error, "json error");
            logger.LogWarning(
                exception,
                "Mensagem SQS não pôde ser desserializada e foi mantida para redelivery. MessageId={MessageId}; ApproximateReceiveCount={ApproximateReceiveCount}",
                message.MessageId,
                receiveCount);
        }
        catch (Exception exception)
        {
            Observability.EventsProcessingFailed.Add(1);
            activity?.SetStatus(ActivityStatusCode.Error, "processing failed");
            logger.LogWarning(
                exception,
                "Falha no processamento SQS. Mensagem não será excluída. MessageId={MessageId}; ApproximateReceiveCount={ApproximateReceiveCount}",
                message.MessageId,
                receiveCount);
        }
        finally
        {
            Observability.EventProcessDuration.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        }
    }

    private static int GetReceiveCount(Message message)
    {
        return message.Attributes.TryGetValue("ApproximateReceiveCount", out var value)
            && int.TryParse(value, out var count)
                ? count
                : 0;
    }
}
