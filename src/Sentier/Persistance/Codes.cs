namespace Sentier.Persistance;

internal static class Codes
{
    public static string De(StatutInstance statut) => statut switch
    {
        StatutInstance.EnCours => "EN_COURS",
        StatutInstance.Terminee => "TERMINEE",
        _ => "ANNULEE",
    };

    public static StatutInstance Statut(string code) => code switch
    {
        "EN_COURS" => StatutInstance.EnCours,
        "TERMINEE" => StatutInstance.Terminee,
        _ => StatutInstance.Annulee,
    };

    public static string? De(TypeAttente? attente) => attente switch
    {
        TypeAttente.Tache => "TACHE",
        TypeAttente.Signal => "SIGNAL",
        TypeAttente.Temps => "TEMPS",
        TypeAttente.SousProcessus => "SOUS_PROC",
        _ => null,
    };

    public static TypeAttente? Attente(string? code) => code switch
    {
        "TACHE" => TypeAttente.Tache,
        "SIGNAL" => TypeAttente.Signal,
        "TEMPS" => TypeAttente.Temps,
        "SOUS_PROC" => TypeAttente.SousProcessus,
        _ => null,
    };
}
