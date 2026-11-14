namespace Sentier;

/// <summary>
/// Suivi et exploitation, à part du pilotage des instances (<see cref="IMoteurSentier"/>). Le moteur n'appelle
/// jamais ces opérations : le client les exécute quand il le décide, par exemple à la livraison d'une définition modifiée.
/// </summary>
public interface ISuiviSentier
{
    /// <summary>Clés des définitions enregistrées.</summary>
    IReadOnlyList<string> ClesDefinitions { get; }

    /// <summary>
    /// Instances en cours que le code livré ne sait plus faire avancer : définition retirée, nœud retiré sans
    /// correspondance, ou nœud devenu d'une autre nature d'attente. Ne modifie rien.
    /// </summary>
    Task<IReadOnlyList<InstanceOrpheline>> VerifierInstancesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Applique en base les correspondances des définitions : les instances en cours arrêtées sur un ancien nœud
    /// sont replacées sur le nouveau. Les correspondances peuvent ensuite être retirées du code. Retourne aussi
    /// les instances orphelines, qui ne sont pas touchées. S'exécute dans la transaction du client.
    /// </summary>
    Task<ResultatMigration> MigrerInstancesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Opération de support : replace l'instance active de l'arbre (la tête, ou le sous-processus en cours) sur un
    /// nœud de sa définition, puis l'exécute à partir de ce nœud jusqu'au prochain point d'attente ou à la fin.
    /// La tâche ouverte est annulée dans le bassin, sans son action « après » ; l'échéance et l'abonnement au
    /// groupe en cours sont abandonnés. Fonctionne aussi pour une instance orpheline. S'exécute dans la
    /// transaction du client. Lève <see cref="SentierException"/> si aucune instance n'est en cours pour cette clé.
    /// </summary>
    Task<EtatInstance> RepositionnerAsync(string cleCorrelation, string noeud, string acteur, Donnees? donnees = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Historique de l'arbre d'instances d'une clé de corrélation, dans l'ordre des événements. Vide si
    /// l'historique n'était pas actif (<see cref="OptionsSentier.HistoriqueActif"/>) ou si la clé est inconnue.
    /// </summary>
    Task<IReadOnlyList<EvenementHistorique>> LireHistoriqueAsync(string cleCorrelation, CancellationToken cancellationToken = default);

    /// <summary>Diagramme Mermaid (flowchart) d'une définition, horizontal par défaut.</summary>
    string DiagrammeDefinition(string cleDefinition, OrientationDiagramme orientation = OrientationDiagramme.Horizontale);

    /// <summary>
    /// Diagramme Mermaid de l'arbre d'instances d'une clé de corrélation : la définition de la tête et celle de
    /// chaque sous-processus en cours, avec le nœud courant en évidence et, si l'historique est actif, les nœuds
    /// déjà traversés. Nul si la clé est inconnue.
    /// </summary>
    Task<string?> DiagrammeInstanceAsync(string cleCorrelation, OrientationDiagramme orientation = OrientationDiagramme.Horizontale,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Page HTML autonome d'une définition : son diagramme Mermaid, ses nœuds avec leurs sorties et ses
    /// correspondances. Le navigateur charge Mermaid à l'adresse <see cref="OptionsSentier.UrlMermaid"/>.
    /// </summary>
    string PageDefinition(string cleDefinition);

    /// <summary>
    /// Page HTML autonome de l'arbre d'instances d'une clé de corrélation : son diagramme Mermaid, l'état de
    /// chaque instance, les variables et les nœuds de l'instance active, et l'historique. Nul si la clé est inconnue.
    /// </summary>
    Task<string?> PageInstanceAsync(string cleCorrelation, CancellationToken cancellationToken = default);
}
