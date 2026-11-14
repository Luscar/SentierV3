namespace Sentier;

/// <summary>Définition construite et validée ; immuable.</summary>
public sealed class DefinitionProcessus
{
    private readonly Dictionary<string, Noeud> _parId;
    private readonly Dictionary<string, string> _correspondances;

    internal DefinitionProcessus(string cle, List<Noeud> noeuds, Dictionary<string, Noeud> parId, Dictionary<string, string> correspondances)
    {
        Cle = cle;
        ListeNoeuds = noeuds.ToArray();
        _parId = parId;
        _correspondances = new(correspondances, StringComparer.Ordinal);
    }

    public string Cle { get; }

    /// <summary>Identifiants des nœuds, dans l'ordre de la définition.</summary>
    public IReadOnlyList<string> Noeuds => ListeNoeuds.Select(n => n.Id).ToArray();

    internal IReadOnlyList<Noeud> ListeNoeuds { get; }

    internal string Debut => ListeNoeuds[0].Id;

    internal Noeud Noeud(string id) => _parId.TryGetValue(id, out var noeud) ? noeud : throw new NoeudInconnuException(Cle, id);

    /// <summary>Résout un identifiant de nœud conservé avec une instance, en suivant les correspondances.</summary>
    internal string? Resoudre(string id)
    {
        for (var i = 0; i <= _correspondances.Count; i++)
        {
            if (_parId.ContainsKey(id)) return id;
            if (!_correspondances.TryGetValue(id, out var nouveau)) return null;
            id = nouveau;
        }
        return null;
    }

    /// <summary>Construit et valide une définition ; lève <see cref="DefinitionInvalideException"/>.</summary>
    public static DefinitionProcessus Construire(IDefinitionProcessus definition)
    {
        var builder = new ProcessusBuilder(Garde.Nom(definition.Cle, "définition"));
        definition.Definir(builder);
        return builder.Construire();
    }
}

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
