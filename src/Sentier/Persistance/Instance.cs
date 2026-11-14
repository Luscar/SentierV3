namespace Sentier.Persistance;

/// <summary>Instance en mémoire pendant un appel.</summary>
internal sealed class Instance
{
    public long Id { get; set; }
    public required long IdAffaire { get; init; }
    public required string CleCorrelation { get; init; }
    public required string CleDefinition { get; init; }
    public long? IdParent { get; init; }
    public required string NoeudCourant { get; set; }
    public StatutInstance Statut { get; set; }
    public string? NomFin { get; set; }
    public TypeAttente? Attente { get; set; }
    public long? RefTache { get; set; }
    public DateTime? DateEcheance { get; set; }
    public string? GroupeSignal { get; set; }
    public required Variables Variables { get; init; }
    public int NoVersion { get; set; }
    public string ActeurDemarrage { get; init; } = "";
    public DateTime DateDemarrage { get; init; }
    public DateTime? DateFin { get; set; }

    public bool EnCours => Statut == StatutInstance.EnCours;

    public void CesserAttente()
    {
        Attente = null;
        RefTache = null;
        DateEcheance = null;
        GroupeSignal = null;
    }
}
