namespace Sentier.Persistance;

/// <summary>Point d'arrêt distinct des instances en cours, tel que lu par la couche d'accès.</summary>
public sealed class LigneArret
{
    public string CleDefinition { get; set; } = "";
    public string NoeudCourant { get; set; } = "";
    public string? TypeAttente { get; set; }
}
