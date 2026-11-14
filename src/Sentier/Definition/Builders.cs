namespace Sentier;

/// <summary>Une définition de processus : une classe par processus, nommée d'après lui.</summary>
public interface IDefinitionProcessus
{
    /// <summary>Clé de la définition, conservée avec chaque instance (le code du type de processus).</summary>
    string Cle { get; }

    void Definir(IProcessusBuilder p);
}

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
    IProcessusBuilder AttenteTemps(string id, TimeSpan duree);

    /// <summary>Attend la date portée par une variable de l'instance.</summary>
    IProcessusBuilder AttenteTemps(string id, Variable<DateTime> date);

    /// <summary>Démarre une instance enfant d'une autre définition ; le parent reprend à la fin de l'enfant.</summary>
    IProcessusBuilder SousProcessus(string id, string cleDefinition, Action<ISousProcessusBuilder>? sorties = null);

    /// <summary>Fin nommée ; sans nom, la fin porte l'identifiant du nœud.</summary>
    IProcessusBuilder Fin(string id, string? nomFin = null);

    /// <summary>Le nœud précédent mène à ce nœud plutôt qu'au suivant dans le texte.</summary>
    IProcessusBuilder Aller(string noeud);

    /// <summary>Un nœud retiré ou renommé : les instances arrêtées sur l'ancien sont replacées sur le nouveau.</summary>
    IProcessusBuilder Correspondance(string ancienNoeud, string nouveauNoeud);
}

public interface IParametresBuilder
{
    /// <summary>Valeur statique.</summary>
    IParametresBuilder Valeur<T>(Parametre<T> parametre, T valeur);

    /// <summary>Valeur lue d'une variable de l'instance au moment de l'appel.</summary>
    IParametresBuilder Variable<T>(Parametre<T> parametre, Variable<T> variable);
}

public interface IDecisionBuilder
{
    /// <summary>Branche prise quand le résultat égale <paramref name="valeur"/>.</summary>
    IDecisionBuilder Si(object valeur, string noeud);

    /// <summary>Branche par défaut, obligatoire.</summary>
    IDecisionBuilder Sinon(string noeud);

    IDecisionBuilder Valeur<T>(Parametre<T> parametre, T valeur);
    IDecisionBuilder Variable<T>(Parametre<T> parametre, Variable<T> variable);
}

public interface IInteractifBuilder
{
    /// <summary>Commande exécutée dans la transaction qui crée la tâche.</summary>
    IInteractifBuilder Avant<TCommande>(Action<IParametresBuilder>? parametres = null) where TCommande : ICommande;

    /// <summary>Commande exécutée dans la transaction qui complète la tâche.</summary>
    IInteractifBuilder Apres<TCommande>(Action<IParametresBuilder>? parametres = null) where TCommande : ICommande;

    /// <summary>Transition nommée, choisie par l'appelant à la complétion.</summary>
    IInteractifBuilder Sur(string transition, string noeud);
}

public interface IAttenteSignalBuilder
{
    /// <summary>Autre signal accepté par ce nœud, menant à son propre nœud.</summary>
    IAttenteSignalBuilder Signal(string signal, string noeud);

    /// <summary>Transition nommée, que le signal peut porter.</summary>
    IAttenteSignalBuilder Sur(string transition, string noeud);
}

public interface ISousProcessusBuilder
{
    /// <summary>Le nom de la fin atteinte par l'enfant choisit le nœud de reprise du parent.</summary>
    ISousProcessusBuilder Sur(string nomFin, string noeud);
}
