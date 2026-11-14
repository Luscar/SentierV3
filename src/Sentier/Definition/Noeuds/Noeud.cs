namespace Sentier;

internal abstract class Noeud(string id)
{
    public string Id { get; } = id;

    /// <summary>Nœud suivant dans le déroulement normal ; fixé à la construction.</summary>
    public string? Suivant { get; set; }

    public bool SuivantExplicite { get; set; }

    public virtual bool ExigeSuivant => true;

    public virtual TypeAttente? Attente => null;

    /// <summary>Échéance d'escalade du nœud, s'il en a une.</summary>
    public virtual Escalade? EscaladeDuNoeud => null;

    /// <summary>
    /// Nœud visé quand l'attente est interrompue (<see cref="IMoteurSentier.InterrompreAttenteAsync"/>) ; nul pour
    /// le nœud suivant. Ne vaut que pour une attente de signal ou de temps.
    /// </summary>
    public string? Interruption { get; set; }

    /// <summary>Toutes les sorties possibles, pour la validation.</summary>
    public virtual IEnumerable<string> Cibles
        => (Suivant is null ? [] : new[] { Suivant }).Concat(Interruption is null ? [] : [Interruption]);
}
