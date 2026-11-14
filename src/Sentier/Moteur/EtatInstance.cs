namespace Sentier;

/// <summary>État atteint par une instance de tête et par le nœud actif de son arbre.</summary>
public sealed record EtatInstance
{
    /// <summary>Identifiant de l'instance de tête (le « numéro de processus »).</summary>
    public required long IdInstance { get; init; }
    public required long IdAffaire { get; init; }
    public required string CleCorrelation { get; init; }
    public required string CleDefinition { get; init; }
    public required StatutInstance Statut { get; init; }
    public string? NomFin { get; init; }
    public bool EnCours => Statut == StatutInstance.EnCours;

    /// <summary>Instance qui porte le nœud actif : la tête ou un sous-processus.</summary>
    public required long IdInstanceActive { get; init; }
    public required string CleDefinitionActive { get; init; }
    public required string NoeudCourant { get; init; }
    public TypeAttente? TypeAttente { get; init; }
    public string? CodeTache { get; init; }
    public long? RefTache { get; init; }
    /// <summary>Échéance d'une attente de temps, ou de l'escalade d'une tâche ou d'une attente de signal.</summary>
    public DateTime? DateEcheance { get; init; }
    /// <summary>Groupe de corrélation auquel l'instance est abonnée pendant son attente de signal.</summary>
    public string? GroupeSignal { get; init; }
    public IReadOnlyList<string> SignauxAttendus { get; init; } = [];
    public required Variables Variables { get; init; }
}
