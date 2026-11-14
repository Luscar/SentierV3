namespace Sentier;

/// <summary>Instance en cours que le code livré ne sait plus faire avancer ; <see cref="Raison"/> dit pourquoi.</summary>
public sealed record InstanceOrpheline(long IdInstance, string CleCorrelation, string CleDefinition, string Noeud, string Raison);
