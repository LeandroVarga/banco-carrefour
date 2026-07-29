namespace BancoCarrefour.Consolidation.Worker.Sqs;

public sealed class SqsOptions
{
    public const string SectionName = "Sqs";

    public string QueueUrl { get; set; } = "http://localstack:4566/000000000000/financial-entry-registered";

    public string ServiceUrl { get; set; } = "http://localstack:4566";

    public string Region { get; set; } = "us-east-1";

    public string AccessKey { get; set; } = "test";

    public string SecretKey { get; set; } = "test";

    public int MaxNumberOfMessages { get; set; } = 10;

    public int WaitTimeSeconds { get; set; } = 10;

    public int VisibilityTimeoutSeconds { get; set; } = 5;
}
