namespace BancoCarrefour.Consolidation.Application;

public sealed class ProjectionValidationException(string message) : InvalidOperationException(message);
