namespace Sentier;

internal sealed class NoeudFin(string id, string nomFin) : Noeud(id)
{
    public string NomFin { get; } = nomFin;

    public override bool ExigeSuivant => false;

    public override IEnumerable<string> Cibles => [];
}
