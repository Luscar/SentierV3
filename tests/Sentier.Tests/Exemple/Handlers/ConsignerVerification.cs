namespace Sentier.Tests;

public sealed class ConsignerVerification(Journal journal) : ICommande
{
    public Task ExecuterAsync(ContexteCommande c, CancellationToken ct)
    {
        journal.Lignes.Add("apres-verification");
        c.Variables.Ecrire(DeclarationOriginale.Verifiee, true);
        return Task.CompletedTask;
    }
}
