namespace Sentier;

/// <summary>Contexte remis à chaque handler par le moteur.</summary>
public abstract class ContexteSentier
{
    public required long IdInstance { get; init; }
    public required long IdAffaire { get; init; }
    public required string CleCorrelation { get; init; }
    /// <summary>Clé de la définition exécutée (le type de processus).</summary>
    public required string CleDefinition { get; init; }
    public required string Noeud { get; init; }
    public required string Acteur { get; init; }
    public Variables Variables { get; init; } = new();
    public Parametres Parametres { get; init; } = new();
}

public sealed class ContexteCommande : ContexteSentier;

public sealed class ContexteRequete : ContexteSentier;
