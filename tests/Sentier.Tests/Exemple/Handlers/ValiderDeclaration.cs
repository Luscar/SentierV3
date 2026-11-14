namespace Sentier.Tests;

public sealed class ValiderDeclaration(Journal journal) : ICommande
{
    public Task ExecuterAsync(ContexteCommande c, CancellationToken ct)
    {
        journal.Lignes.Add($"valider:{c.IdAffaire}:{c.Acteur}");
        return Task.CompletedTask;
    }
}
