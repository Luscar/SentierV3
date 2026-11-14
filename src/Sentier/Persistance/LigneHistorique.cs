namespace Sentier.Persistance;

/// <summary>Ligne de la table HISTORIQUE telle que lue par la couche d'accès (colonnes aliasées sans soulignement).</summary>
public sealed class LigneHistorique
{
    public long IdInstance { get; set; }
    public string CleDefinition { get; set; } = "";
    public string Noeud { get; set; } = "";
    public string Evenement { get; set; } = "";
    public string? Transition { get; set; }
    public string? Acteur { get; set; }
    public DateTime DateEvenement { get; set; }
}
