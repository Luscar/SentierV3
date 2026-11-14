namespace Sentier;

/// <summary>Marqueur non générique ; implémenter <see cref="IRequete{TResultat}"/>.</summary>
public interface IRequete;

/// <summary>Requête traitée côté client ; son résultat choisit la branche d'un nœud décisionnel.</summary>
public interface IRequete<TResultat> : IRequete
{
    Task<TResultat> ExecuterAsync(ContexteRequete contexte, CancellationToken cancellationToken);
}
