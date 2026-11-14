namespace Sentier;

public sealed record DemandeTache(
    string CodeTache,
    long IdInstance,
    long IdAffaire,
    string CleCorrelation,
    string CleDefinition,
    string Noeud,
    string Acteur,
    bool AssignerAActeur);
