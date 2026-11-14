namespace Sentier;

/// <summary>
/// Points d'entrée du moteur. Chaque appel exécute les nœuds jusqu'au prochain point d'attente ou jusqu'à la fin,
/// dans la transaction ambiante du client (TransactionScope avec TransactionScopeAsyncFlowOption.Enabled).
/// </summary>
public interface IMoteurSentier
{
    Task<EtatInstance> DemarrerAsync(DemandeDemarrage demande, CancellationToken cancellationToken = default);

    /// <summary>Complète la tâche d'un nœud interactif. Lève <see cref="TacheIntrouvableException"/> si rien ne l'attend.</summary>
    Task<EtatInstance> CompleterTacheAsync(long refTache, string acteur, string? transition = null, Donnees? donnees = null,
        CancellationToken cancellationToken = default);

    /// <summary>Signal ciblé. Si rien n'attend ce signal, aucune erreur : le résultat l'indique.</summary>
    /// <param name="cleDefinition">Filtre : le signal n'est remis que si le nœud en attente appartient à une instance de cette définition.</param>
    Task<ResultatSollicitation> SignalerAsync(string cleCorrelation, string signal, string acteur, string? transition = null,
        Donnees? donnees = null, string? cleDefinition = null, CancellationToken cancellationToken = default);

    /// <summary>Signal diffusé à toutes les instances qui l'attendent ; retourne l'état de celles qui ont avancé.</summary>
    Task<IReadOnlyList<EtatInstance>> DiffuserSignalAsync(string signal, string acteur, string? transition = null,
        Donnees? donnees = null, string? cleDefinition = null, CancellationToken cancellationToken = default);

    /// <summary>Échéances dues à un instant donné ; un composant externe les déclenche une à une.</summary>
    Task<IReadOnlyList<Echeance>> ListerEcheancesDuesAsync(DateTime instant, CancellationToken cancellationToken = default);

    /// <summary>Déclenche une échéance. Sans effet si elle n'est pas due ou a déjà été déclenchée.</summary>
    Task<ResultatSollicitation> DeclencherEcheanceAsync(long idInstance, string acteur, CancellationToken cancellationToken = default);

    /// <summary>Annule l'arbre d'instances : tâches ouvertes dans le bassin, échéances et sous-processus.</summary>
    Task<EtatInstance?> AnnulerAsync(string cleCorrelation, string acteur, CancellationToken cancellationToken = default);

    Task<EtatInstance?> ObtenirEtatAsync(string cleCorrelation, CancellationToken cancellationToken = default);

    /// <summary>À lancer au démarrage de l'application : instances arrêtées sur un nœud inconnu et sans correspondance.</summary>
    Task<IReadOnlyList<InstanceOrpheline>> VerifierInstancesAsync(CancellationToken cancellationToken = default);
}

public sealed record DemandeDemarrage(string CleDefinition, long IdAffaire, string CleCorrelation, string Acteur)
{
    public Donnees? Donnees { get; init; }

    /// <summary>Transmis au bassin pour les tâches créées par cet appel ; l'assignation reste sa décision.</summary>
    public bool AssignerPremiereTache { get; init; }
}

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
    public DateTime? DateEcheance { get; init; }
    public IReadOnlyList<string> SignauxAttendus { get; init; } = [];
    public required Variables Variables { get; init; }
}

/// <param name="Traitee">Faux quand rien n'attendait la sollicitation ; l'instance n'a pas bougé.</param>
public sealed record ResultatSollicitation(bool Traitee, EtatInstance? Etat);

public sealed record Echeance(long IdInstance, string CleCorrelation, string CleDefinition, string Noeud, DateTime DateEcheance);

public sealed record InstanceOrpheline(long IdInstance, string CleCorrelation, string CleDefinition, string Noeud);
