namespace Sentier;

/// <summary>Commande traitée côté client : nœud systémique, action avant ou après un nœud interactif.</summary>
public interface ICommande
{
    Task ExecuterAsync(ContexteCommande contexte, CancellationToken cancellationToken);
}
