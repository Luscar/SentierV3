namespace Sentier;

/// <summary>Commande traitée côté client : nœud systémique, action avant ou après un nœud interactif.</summary>
public interface ICommande
{
    Task ExecuterAsync(ContexteCommande contexte, CancellationToken cancellationToken);
}

/// <summary>Marqueur non générique ; implémenter <see cref="IRequete{TResultat}"/>.</summary>
public interface IRequete;

/// <summary>Requête traitée côté client ; son résultat choisit la branche d'un nœud décisionnel.</summary>
public interface IRequete<TResultat> : IRequete
{
    Task<TResultat> ExecuterAsync(ContexteRequete contexte, CancellationToken cancellationToken);
}

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

/// <summary>Adaptateur vers le bassin de tâches du client.</summary>
public interface IBassinTaches
{
    /// <summary>Crée une tâche pour un nœud interactif et retourne son identifiant dans le bassin.</summary>
    Task<long> CreerTacheAsync(DemandeTache demande, CancellationToken cancellationToken);

    /// <summary>Annule une tâche ouverte (annulation de l'instance).</summary>
    Task AnnulerTacheAsync(long refTache, string acteur, CancellationToken cancellationToken);
}

public sealed record DemandeTache(
    string CodeTache,
    long IdInstance,
    long IdAffaire,
    string CleCorrelation,
    string CleDefinition,
    string Noeud,
    string Acteur,
    bool AssignerAActeur);

/// <summary>Avisé dans la transaction qui termine ou annule une instance.</summary>
public interface IEcouteurFinProcessus
{
    Task SurFinAsync(FinProcessus fin, CancellationToken cancellationToken);
}

public sealed record FinProcessus(
    long IdInstance,
    long? IdInstanceParent,
    long IdAffaire,
    string CleCorrelation,
    string CleDefinition,
    StatutInstance Statut,
    string? NomFin,
    string Acteur)
{
    public bool EstSousProcessus => IdInstanceParent is not null;
}

/// <summary>Fournit les handlers ; branché sur le conteneur du client.</summary>
public interface IResolveurSentier
{
    object Resoudre(Type type);
}

/// <summary>
/// Accès aux données dont le moteur a besoin. Les signatures reprennent celles de la couche d'accès du client
/// (au-dessus de Dapper), pour que l'adaptateur ne soit que de la délégation. Les commandes s'exécutent sans objet
/// transaction : la garantie « tout ou rien » vient du TransactionScope du client.
/// </summary>
public interface IAccesBDSentier
{
    Task<int> ExecuteNonQueryAsync(string requeteSQL, object? parametres = null, CancellationToken cancellationToken = default);
    Task<IEnumerable<T>> QueryAsync<T>(string requeteSQL, object? parametres = null, CancellationToken cancellationToken = default);
    Task<T?> QuerySingleOrDefaultAsync<T>(string requeteSQL, object? parametres = null, CancellationToken cancellationToken = default);
    Task<long> GetSequenceAsync(string sequence, CancellationToken cancellationToken = default);
}

public enum StatutInstance { EnCours, Terminee, Annulee }

public enum TypeAttente { Tache, Signal, Temps, SousProcessus }

public enum DialecteSentier { Oracle, Sqlite }

/// <summary>Configuration du package.</summary>
public sealed class OptionsSentier
{
    /// <summary>Préfixe des tables et des séquences : {PRÉFIXE}_{nom}_{SUFFIXE}.</summary>
    public string Prefixe { get; set; } = "";
    public string Suffixe { get; set; } = "";
    /// <summary>Consigne chaque passage de nœud dans la table HISTORIQUE.</summary>
    public bool HistoriqueActif { get; set; }
    /// <summary>Garde contre les boucles sans fin : nombre maximal de nœuds exécutés par appel.</summary>
    public int NombreMaxEtapes { get; set; } = 1000;
}
