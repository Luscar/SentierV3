namespace Sentier;

/// <summary>Avisé dans la transaction qui termine ou annule une instance.</summary>
public interface IEcouteurFinProcessus
{
    Task SurFinAsync(FinProcessus fin, CancellationToken cancellationToken);
}
