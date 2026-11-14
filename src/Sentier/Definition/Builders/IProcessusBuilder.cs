namespace Sentier;

/// <summary>
/// Builder fluent. Les nœuds s'enchaînent dans l'ordre du texte ; seuls les embranchements et les retours en
/// arrière sont explicites. Le premier nœud déclaré est le départ.
/// </summary>
public interface IProcessusBuilder
{
    /// <summary>Nœud systémique : lance une commande, puis continue.</summary>
    IProcessusBuilder Systeme<TCommande>(string id, Action<IParametresBuilder>? parametres = null) where TCommande : ICommande;

    /// <summary>Nœud décisionnel sur le résultat d'une requête.</summary>
    IProcessusBuilder Decision<TRequete>(string id, Action<IDecisionBuilder> decision) where TRequete : IRequete;

    /// <summary>Nœud décisionnel sur la valeur d'une variable de l'instance.</summary>
    IProcessusBuilder Decision<T>(string id, Variable<T> variable, Action<IDecisionBuilder> decision);

    /// <summary>Nœud interactif : crée une tâche dans le bassin et attend sa complétion.</summary>
    IProcessusBuilder Interactif(string id, string codeTache, Action<IInteractifBuilder>? tache = null);

    /// <summary>Attend un signal nommé.</summary>
    IProcessusBuilder AttenteSignal(string id, string signal, Action<IAttenteSignalBuilder>? attente = null);

    /// <summary>Attend une durée.</summary>
    IProcessusBuilder AttenteTemps(string id, TimeSpan duree, Action<IAttenteTempsBuilder>? attente = null);

    /// <summary>Attend la date portée par une variable de l'instance.</summary>
    IProcessusBuilder AttenteTemps(string id, Variable<DateTime> date, Action<IAttenteTempsBuilder>? attente = null);

    /// <summary>Démarre une instance enfant d'une autre définition ; le parent reprend à la fin de l'enfant.</summary>
    IProcessusBuilder SousProcessus(string id, string cleDefinition, Action<ISousProcessusBuilder>? sorties = null);

    /// <summary>Fin nommée ; sans nom, la fin porte l'identifiant du nœud.</summary>
    IProcessusBuilder Fin(string id, string? nomFin = null);

    /// <summary>Le nœud précédent mène à ce nœud plutôt qu'au suivant dans le texte.</summary>
    IProcessusBuilder Aller(string noeud);

    /// <summary>Un nœud retiré ou renommé : les instances arrêtées sur l'ancien sont replacées sur le nouveau.</summary>
    IProcessusBuilder Correspondance(string ancienNoeud, string nouveauNoeud);
}
