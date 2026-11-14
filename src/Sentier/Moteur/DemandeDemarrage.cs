namespace Sentier;

public sealed record DemandeDemarrage(string CleDefinition, long IdAffaire, string CleCorrelation, string Acteur)
{
    public Donnees? Donnees { get; init; }

    /// <summary>Transmis au bassin pour les tâches créées par cet appel ; l'assignation reste sa décision.</summary>
    public bool AssignerPremiereTache { get; init; }
}
