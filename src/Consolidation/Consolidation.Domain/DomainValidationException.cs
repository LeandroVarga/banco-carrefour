namespace BancoCarrefour.Consolidation.Domain;

public sealed class DomainValidationException(string message) : Exception(message);
