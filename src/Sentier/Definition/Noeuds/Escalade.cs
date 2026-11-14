namespace Sentier;

/// <summary>Échéance d'un nœud interactif ou d'une attente de signal : le délai dépassé, l'instance va au nœud d'escalade.</summary>
internal sealed record Escalade(TimeSpan? Duree, Variable<DateTime>? Date, string Noeud)
{
    public DateTime Calculer(DateTime maintenant, Variables variables, string noeud)
    {
        if (Duree is { } duree) return maintenant + duree;
        if (!variables.Contient(Date!))
            throw new SentierException($"Nœud « {noeud} » : la variable « {Date} » de l'échéance n'a pas été écrite.");
        return variables.Lire(Date!);
    }
}
