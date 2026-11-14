namespace Sentier;

/// <summary>Paramètres d'un appel de commande ou de requête, fixés dans la définition.</summary>
public sealed class Parametres
{
    private readonly Dictionary<string, object?> _valeurs = new(StringComparer.Ordinal);

    /// <summary>Pour construire un contexte dans le test unitaire d'un handler.</summary>
    public Parametres Avec<T>(Parametre<T> parametre, T valeur)
    {
        _valeurs[parametre.Nom] = valeur;
        return this;
    }

    internal void Definir(string nom, object? valeur) => _valeurs[nom] = valeur;

    public bool Contient<T>(Parametre<T> parametre) => _valeurs.ContainsKey(parametre.Nom);

    public T? Lire<T>(Parametre<T> parametre)
    {
        if (_valeurs.TryGetValue(parametre.Nom, out var valeur)) return (T?)valeur;
        if (parametre.Obligatoire)
            throw new SentierException($"Le paramètre obligatoire « {parametre.Nom} » n'a pas été fourni.");
        return default;
    }
}
