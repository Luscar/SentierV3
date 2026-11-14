namespace Sentier;

public sealed record FinProcessus(
    long IdInstance,
    long? IdInstanceParent,
    long IdAffaire,
    string CleCorrelation,
    string CleDefinition,
    StatutInstance Statut,
    string? NomFin,
    string Acteur)
{
    public bool EstSousProcessus => IdInstanceParent is not null;
}
