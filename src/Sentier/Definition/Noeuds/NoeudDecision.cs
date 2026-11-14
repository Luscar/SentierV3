namespace Sentier;

internal sealed class NoeudDecision(string id) : Noeud(id)
{
    public AppelHandler? Requete { get; init; }
    public Func<Variables, object?>? LectureVariable { get; init; }
    public required Type TypeResultat { get; init; }
    public List<(object Valeur, string Noeud)> Branches { get; } = [];
    public string? Sinon { get; set; }

    public override bool ExigeSuivant => false;

    public override IEnumerable<string> Cibles => Branches.Select(b => b.Noeud).Concat(Sinon is null ? [] : [Sinon]);

    public string Choisir(object? resultat)
        => Branches.Where(b => b.Valeur.Equals(resultat)).Select(b => b.Noeud).FirstOrDefault() ?? Sinon!;
}
