namespace Sentier;

/// <summary>Instances d'une définition replacées d'un ancien nœud sur le nœud de sa correspondance.</summary>
public sealed record MigrationNoeud(string CleDefinition, string AncienNoeud, string NouveauNoeud, int NombreInstances);
