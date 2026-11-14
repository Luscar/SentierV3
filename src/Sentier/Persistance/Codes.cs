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
        "ANNULEE" => StatutInstance.Annulee,
        _ => throw new SentierException($"Le statut d'instance « {code} » lu en base est inconnu."),
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
        null => null,
        _ => throw new SentierException($"Le type d'attente « {code} » lu en base est inconnu."),
    };
}
