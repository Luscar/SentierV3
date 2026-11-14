namespace Sentier;

public class SentierException(string message, Exception? cause = null) : Exception(message, cause);

/// <summary>La définition est refusée à la construction ; <see cref="Erreurs"/> liste tous les problèmes.</summary>
public sealed class DefinitionInvalideException(string cle, IReadOnlyList<string> erreurs)
    : SentierException($"Définition « {cle} » invalide :{Environment.NewLine}- {string.Join(Environment.NewLine + "- ", erreurs)}")
{
    public IReadOnlyList<string> Erreurs { get; } = erreurs;
}

public sealed class DefinitionInconnueException(string cle)
    : SentierException($"Aucune définition de processus n'a la clé « {cle} ».");

/// <summary>Une instance de tête existe déjà avec cette clé de corrélation.</summary>
public sealed class CleCorrelationDejaUtiliseeException(string cle, Exception? cause = null)
    : SentierException($"La clé de corrélation « {cle} » est déjà utilisée.", cause);

/// <summary>Aucune instance en cours n'attend cette tâche (déjà complétée, annulée ou inconnue).</summary>
public sealed class TacheIntrouvableException(long refTache)
    : SentierException($"Aucune instance en cours n'attend la tâche {refTache}.");

public sealed class TransitionInconnueException(string noeud, string transition)
    : SentierException($"Le nœud « {noeud} » n'a pas de transition « {transition} ».");

/// <summary>L'instance est arrêtée sur un nœud qui n'existe plus et qui n'a pas de correspondance.</summary>
public sealed class NoeudInconnuException(string cleDefinition, string noeud)
    : SentierException($"La définition « {cleDefinition} » n'a pas de nœud « {noeud} » ni de correspondance pour lui.");

/// <summary>L'instance a été modifiée par un autre appel depuis sa lecture.</summary>
public sealed class ConflitConcurrenceException(long idInstance)
    : SentierException($"L'instance {idInstance} a été modifiée par un autre appel.");

public sealed class BoucleSansFinException(int maximum)
    : SentierException($"Plus de {maximum} nœuds exécutés dans un même appel : boucle sans fin probable.");
