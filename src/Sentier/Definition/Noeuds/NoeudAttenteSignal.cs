namespace Sentier;

internal sealed class NoeudAttenteSignal(string id) : Noeud(id)
{
    /// <summary>Signaux acceptés ; une cible nulle mène au nœud suivant.</summary>
    public Dictionary<string, string?> Signaux { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Transitions { get; } = new(StringComparer.Ordinal);
    public Escalade? Escalade { get; set; }
    public override Escalade? EscaladeDuNoeud => Escalade;

    /// <summary>Groupe de corrélation auquel l'instance s'abonne en arrivant sur le nœud ; nul sans abonnement.</summary>
    public Func<Variables, string?>? Groupe { get; set; }

    public override TypeAttente? Attente => TypeAttente.Signal;

    public override IEnumerable<string> Cibles
        => base.Cibles.Concat(Signaux.Values.OfType<string>()).Concat(Transitions.Values)
            .Concat(Escalade is null ? [] : [Escalade.Noeud]);
}
