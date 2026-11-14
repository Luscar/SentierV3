namespace Sentier.Tests;

public sealed class BassinEnMemoire : IBassinTaches
{
    private long _prochain = 100;
    public List<DemandeTache> Creees { get; } = [];
    public List<long> Annulees { get; } = [];

    public Task<long> CreerTacheAsync(DemandeTache demande, CancellationToken cancellationToken)
    {
        Creees.Add(demande);
        return Task.FromResult(++_prochain);
    }

    public Task AnnulerTacheAsync(long refTache, string acteur, CancellationToken cancellationToken)
    {
        Annulees.Add(refTache);
        return Task.CompletedTask;
    }
}
