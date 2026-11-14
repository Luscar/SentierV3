namespace Sentier;

public sealed record Echeance(long IdInstance, string CleCorrelation, string CleDefinition, string Noeud, DateTime DateEcheance);
