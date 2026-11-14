namespace Sentier;

internal sealed class NoeudSousProcessus(string id, string cleDefinition) : Noeud(id)
{
    public string CleDefinition { get; } = cleDefinition;
    public Dictionary<string, string> Sorties { get; } = new(StringComparer.Ordinal);

    public override TypeAttente? Attente => TypeAttente.SousProcessus;

    public override IEnumerable<string> Cibles => base.Cibles.Concat(Sorties.Values);
}
