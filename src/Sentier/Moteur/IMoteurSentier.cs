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

    /// <summary>
    /// Signal envoyé aux instances abonnées à un groupe de corrélation (nœud d'attente déclaré avec Groupe) ;
    /// retourne l'état de celles qui ont avancé. Une instance qui n'attend pas ce signal est ignorée.
    /// </summary>
    Task<IReadOnlyList<EtatInstance>> SignalerGroupeAsync(string groupeCorrelation, string signal, string acteur,
        string? transition = null, Donnees? donnees = null, string? cleDefinition = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Échéances dues à un instant donné : attentes de temps, et escalades de tâches ou d'attentes de signal.
    /// Un composant externe les déclenche une à une.
    /// </summary>
    Task<IReadOnlyList<Echeance>> ListerEcheancesDuesAsync(DateTime instant, CancellationToken cancellationToken = default);

    /// <summary>
    /// Déclenche une échéance. Sans effet si elle n'est pas due ou a déjà été déclenchée. Pour une escalade,
    /// la tâche ouverte est annulée dans le bassin et l'instance va au nœud d'escalade.
    /// </summary>
    Task<ResultatSollicitation> DeclencherEcheanceAsync(long idInstance, string acteur, CancellationToken cancellationToken = default);

    /// <summary>
    /// Interrompt l'attente de signal ou de temps de l'instance active : l'échéance et l'abonnement au groupe sont
    /// abandonnés, et l'instance va au nœud déclaré par SurInterruption, sinon au nœud suivant. Sans effet si
    /// l'instance n'est pas arrêtée sur une attente de signal ou de temps : le résultat l'indique.
    /// </summary>
    /// <param name="noeud">Garde : l'attente n'est interrompue que si l'instance est arrêtée sur ce nœud.</param>
    Task<ResultatSollicitation> InterrompreAttenteAsync(string cleCorrelation, string acteur, string? noeud = null,
        Donnees? donnees = null, CancellationToken cancellationToken = default);

    /// <summary>Annule l'arbre d'instances : tâches ouvertes dans le bassin, échéances et sous-processus.</summary>
    Task<EtatInstance?> AnnulerAsync(string cleCorrelation, string acteur, CancellationToken cancellationToken = default);

    Task<EtatInstance?> ObtenirEtatAsync(string cleCorrelation, CancellationToken cancellationToken = default);
}
