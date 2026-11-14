namespace Sentier;

/// <summary>Toutes les définitions de l'application, construites et validées une fois.</summary>
public sealed class RegistreDefinitions
{
    private readonly Dictionary<string, DefinitionProcessus> _definitions = new(StringComparer.Ordinal);

    public RegistreDefinitions(IEnumerable<IDefinitionProcessus> definitions)
    {
        foreach (var definition in definitions)
        {
            var construite = DefinitionProcessus.Construire(definition);
            if (!_definitions.TryAdd(construite.Cle, construite))
                throw new SentierException($"Deux définitions ont la clé « {construite.Cle} ».");
        }

        foreach (var definition in _definitions.Values)
        {
            var inconnus = definition.ListeNoeuds.OfType<NoeudSousProcessus>()
                .Where(n => !_definitions.ContainsKey(n.CleDefinition))
                .Select(n => $"Le nœud « {n.Id} » appelle le sous-processus « {n.CleDefinition} », qui n'existe pas.")
                .ToArray();
            if (inconnus.Length > 0) throw new DefinitionInvalideException(definition.Cle, inconnus);
        }
    }

    public IReadOnlyCollection<DefinitionProcessus> Definitions => _definitions.Values;

    public DefinitionProcessus Obtenir(string cle)
        => _definitions.TryGetValue(cle, out var definition) ? definition : throw new DefinitionInconnueException(cle);
}
