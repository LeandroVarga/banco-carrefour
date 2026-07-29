namespace BancoCarrefour.Ledger.Infrastructure.Outbox;

public sealed class SqsOptions
{
    public const string SectionName = "Sqs";

    public string QueueUrl { get; set; } = "http://localstack:4566/000000000000/financial-entry-registered";

    public string ServiceUrl { get; set; } = "http://localstack:4566";

    public string Region { get; set; } = "us-east-1";

    public string AccessKey { get; set; } = "test";

    public string SecretKey { get; set; } = "test";
}
