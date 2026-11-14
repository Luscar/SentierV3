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

    internal IReadOnlyDictionary<string, string> Correspondances => _correspondances;

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
