namespace Sentier;

internal sealed class NoeudAttenteTemps(string id) : Noeud(id)
{
    public TimeSpan? Duree { get; init; }
    public Variable<DateTime>? Date { get; init; }

    public override TypeAttente? Attente => TypeAttente.Temps;
}
