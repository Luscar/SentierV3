namespace Sentier;

public interface IInteractifBuilder
{
    /// <summary>Commande exécutée dans la transaction qui crée la tâche.</summary>
    IInteractifBuilder Avant<TCommande>(Action<IParametresBuilder>? parametres = null) where TCommande : ICommande;

    /// <summary>Commande exécutée dans la transaction qui complète la tâche.</summary>
    IInteractifBuilder Apres<TCommande>(Action<IParametresBuilder>? parametres = null) where TCommande : ICommande;

    /// <summary>Transition nommée, choisie par l'appelant à la complétion.</summary>
    IInteractifBuilder Sur(string transition, string noeud);

    /// <summary>Escalade : si la tâche n'est pas complétée après ce délai, elle est annulée et l'instance va à <paramref name="noeud"/>.</summary>
    IInteractifBuilder Echeance(TimeSpan duree, string noeud);

    /// <summary>Escalade à la date portée par une variable, qui doit avoir été écrite avant l'arrivée sur le nœud.</summary>
    IInteractifBuilder Echeance(Variable<DateTime> date, string noeud);
}
