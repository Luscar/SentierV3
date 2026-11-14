namespace Sentier;

/// <summary>Adaptateur vers le bassin de tâches du client.</summary>
public interface IBassinTaches
{
    /// <summary>Crée une tâche pour un nœud interactif et retourne son identifiant dans le bassin.</summary>
    Task<long> CreerTacheAsync(DemandeTache demande, CancellationToken cancellationToken);

    /// <summary>Annule une tâche ouverte (annulation de l'instance).</summary>
    Task AnnulerTacheAsync(long refTache, string acteur, CancellationToken cancellationToken);
}
