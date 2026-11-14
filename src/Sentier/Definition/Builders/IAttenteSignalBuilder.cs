namespace Sentier;

public interface IAttenteSignalBuilder
{
    /// <summary>Autre signal accepté par ce nœud, menant à son propre nœud.</summary>
    IAttenteSignalBuilder Signal(string signal, string noeud);

    /// <summary>Transition nommée, que le signal peut porter.</summary>
    IAttenteSignalBuilder Sur(string transition, string noeud);
}
