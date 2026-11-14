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

    /// <summary>Chaque variable et sa valeur en JSON, pour l'affichage.</summary>
    internal IEnumerable<(string Nom, string Json)> EnTexte() => _valeurs.OrderBy(v => v.Key, StringComparer.Ordinal).Select(v => (v.Key, v.Value.GetRawText()));

    internal string VersJson() => JsonSerializer.Serialize(_valeurs);

    internal static Variables DeJson(string? json)
        => string.IsNullOrWhiteSpace(json)
            ? new Variables()
            : new Variables(new Dictionary<string, JsonElement>(
                JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? [], StringComparer.Ordinal));
}
