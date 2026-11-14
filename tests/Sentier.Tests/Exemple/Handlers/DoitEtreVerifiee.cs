namespace Sentier.Tests;

public sealed class DoitEtreVerifiee : IRequete<bool>
{
    public Task<bool> ExecuterAsync(ContexteRequete c, CancellationToken ct)
        => Task.FromResult(c.Variables.Lire(DeclarationOriginale.Montant) > 1000m);
}
