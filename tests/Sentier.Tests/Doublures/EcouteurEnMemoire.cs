namespace Sentier.Tests;

public sealed class EcouteurEnMemoire : IEcouteurFinProcessus
{
    public List<FinProcessus> Fins { get; } = [];

    public Task SurFinAsync(FinProcessus fin, CancellationToken cancellationToken)
    {
        Fins.Add(fin);
        return Task.CompletedTask;
    }
}
