namespace Sentier;

public interface IAttenteSignalBuilder
{
    /// <summary>Autre signal accepté par ce nœud, menant à son propre nœud.</summary>
    IAttenteSignalBuilder Signal(string signal, string noeud);

    /// <summary>Transition nommée, que le signal peut porter.</summary>
    IAttenteSignalBuilder Sur(string transition, string noeud);

    /// <summary>
    /// Abonne l'instance à un groupe de corrélation tant qu'elle attend sur ce nœud : un signal envoyé au groupe
    /// l'atteint. La variable doit avoir été écrite avant l'arrivée sur le nœud.
    /// </summary>
    IAttenteSignalBuilder Groupe(Variable<string> groupe);

    /// <summary>Abonne l'instance à un groupe de corrélation fixe.</summary>
    IAttenteSignalBuilder Groupe(string groupe);

    /// <summary>Escalade : si aucun signal n'est reçu après ce délai, l'instance va à <paramref name="noeud"/>.</summary>
    IAttenteSignalBuilder Echeance(TimeSpan duree, string noeud);

    /// <summary>Escalade à la date portée par une variable, qui doit avoir été écrite avant l'arrivée sur le nœud.</summary>
    IAttenteSignalBuilder Echeance(Variable<DateTime> date, string noeud);
}
