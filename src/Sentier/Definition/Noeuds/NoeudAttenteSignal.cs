namespace Sentier;

internal sealed class NoeudAttenteSignal(string id) : Noeud(id)
{
    /// <summary>Signaux acceptés ; une cible nulle mène au nœud suivant.</summary>
    public Dictionary<string, string?> Signaux { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Transitions { get; } = new(StringComparer.Ordinal);

    public override TypeAttente? Attente => TypeAttente.Signal;

    public override IEnumerable<string> Cibles
        => base.Cibles.Concat(Signaux.Values.OfType<string>()).Concat(Transitions.Values);
}
