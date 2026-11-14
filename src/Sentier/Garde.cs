namespace Sentier;

internal static class Garde
{
    public static string Nom(string? nom, string quoi)
        => string.IsNullOrWhiteSpace(nom) ? throw new ArgumentException($"Le nom d'un(e) {quoi} est obligatoire.") : nom;
}
