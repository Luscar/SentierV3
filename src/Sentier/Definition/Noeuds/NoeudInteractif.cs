namespace Sentier;

internal sealed class NoeudInteractif(string id, string codeTache) : Noeud(id)
{
    public string CodeTache { get; } = codeTache;
    public AppelHandler? Avant { get; set; }
    public AppelHandler? Apres { get; set; }
    public Dictionary<string, string> Transitions { get; } = new(StringComparer.Ordinal);

    public override TypeAttente? Attente => TypeAttente.Tache;

    public override IEnumerable<string> Cibles => base.Cibles.Concat(Transitions.Values);
}
