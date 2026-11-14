namespace Sentier;

public interface IInteractifBuilder
{
    /// <summary>Commande exécutée dans la transaction qui crée la tâche.</summary>
    IInteractifBuilder Avant<TCommande>(Action<IParametresBuilder>? parametres = null) where TCommande : ICommande;

    /// <summary>Commande exécutée dans la transaction qui complète la tâche.</summary>
    IInteractifBuilder Apres<TCommande>(Action<IParametresBuilder>? parametres = null) where TCommande : ICommande;

    /// <summary>Transition nommée, choisie par l'appelant à la complétion.</summary>
    IInteractifBuilder Sur(string transition, string noeud);
}
