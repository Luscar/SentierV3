namespace Sentier.Tests;

public sealed class PreparerVerification(Journal journal) : ICommande
{
    public Task ExecuterAsync(ContexteCommande c, CancellationToken ct)
    {
        journal.Lignes.Add("avant-verification");
        return Task.CompletedTask;
    }
}
