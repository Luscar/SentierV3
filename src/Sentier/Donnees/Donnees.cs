using System.Text.Json;

namespace Sentier;

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
