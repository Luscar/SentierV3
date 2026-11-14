using System.Text.Json;

namespace Sentier;

/// <summary>Variables d'une instance. Conservées en JSON avec l'instance.</summary>
public sealed class Variables
{
    private readonly Dictionary<string, JsonElement> _valeurs;

    public Variables() => _valeurs = new(StringComparer.Ordinal);

    private Variables(Dictionary<string, JsonElement> valeurs) => _valeurs = valeurs;

    public bool Contient<T>(Variable<T> variable) => _valeurs.ContainsKey(variable.Nom);

    /// <summary>Lit la variable ; absente, retourne la valeur par défaut du type.</summary>
    public T? Lire<T>(Variable<T> variable)
        => _valeurs.TryGetValue(variable.Nom, out var element) ? element.Deserialize<T>() : default;

    public Variables Ecrire<T>(Variable<T> variable, T valeur)
    {
        _valeurs[variable.Nom] = JsonSerializer.SerializeToElement(valeur);
        return this;
    }

    public void Retirer<T>(Variable<T> variable) => _valeurs.Remove(variable.Nom);

    public IReadOnlyCollection<string> Noms => _valeurs.Keys;

    internal void Appliquer(Donnees? donnees)
    {
        if (donnees is null) return;
        foreach (var (nom, valeur) in donnees.Valeurs) _valeurs[nom] = valeur;
    }

    internal void Fusionner(Variables autres)
    {
        foreach (var (nom, valeur) in autres._valeurs) _valeurs[nom] = valeur;
    }

    internal Variables Copier() => new(new Dictionary<string, JsonElement>(_valeurs, StringComparer.Ordinal));

    internal string VersJson() => JsonSerializer.Serialize(_valeurs);

    internal static Variables DeJson(string? json)
        => string.IsNullOrWhiteSpace(json)
            ? new Variables()
            : new Variables(new Dictionary<string, JsonElement>(
                JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? [], StringComparer.Ordinal));
}

/// <summary>Contenu porté par un démarrage, une complétion de tâche ou un signal ; versé dans les variables de l'instance.</summary>
public sealed class Donnees
{
    internal Dictionary<string, JsonElement> Valeurs { get; } = new(StringComparer.Ordinal);

    public Donnees Avec<T>(Variable<T> variable, T valeur)
    {
        Valeurs[variable.Nom] = JsonSerializer.SerializeToElement(valeur);
        return this;
    }
}

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
