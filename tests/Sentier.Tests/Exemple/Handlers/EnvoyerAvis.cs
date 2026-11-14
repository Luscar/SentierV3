namespace Sentier.Tests;

public sealed class EnvoyerAvis(Journal journal) : ICommande
{
    public static readonly Parametre<string> Modele = Parametre.Texte("modele");
    public static readonly Parametre<string> Langue = Parametre.Texte("langue");

    public Task ExecuterAsync(ContexteCommande c, CancellationToken ct)
    {
        journal.Lignes.Add($"avis:{c.Parametres.Lire(Modele)}:{c.Parametres.Lire(Langue)}");
        return Task.CompletedTask;
    }
}
