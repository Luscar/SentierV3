namespace Sentier.Persistance;

/// <summary>Ligne de la table INSTC_PROCS telle que lue par la couche d'accès (colonnes aliasées sans soulignement).</summary>
public sealed class LigneInstance
{
    public long IdInstance { get; set; }
    public long IdAffaire { get; set; }
    public string CleCorrelation { get; set; } = "";
    public string CleDefinition { get; set; } = "";
    public long? IdInstanceParent { get; set; }
    public string NoeudCourant { get; set; } = "";
    public string Statut { get; set; } = "";
    public string? NomFin { get; set; }
    public string? TypeAttente { get; set; }
    public long? RefTache { get; set; }
    public DateTime? DateEcheance { get; set; }
    public string? GroupeSignal { get; set; }
    public string? Donnees { get; set; }
    public int NoVersion { get; set; }
    public string? ActeurDemarrage { get; set; }
    public DateTime DateDemarrage { get; set; }
    public DateTime? DateFin { get; set; }
}
