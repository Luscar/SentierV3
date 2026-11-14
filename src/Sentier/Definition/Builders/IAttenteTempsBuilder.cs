namespace Sentier;

public interface IAttenteTempsBuilder
{
    /// <summary>
    /// Nœud visé quand l'attente est interrompue (<see cref="IMoteurSentier.InterrompreAttenteAsync"/>).
    /// Sans cette déclaration, l'interruption mène au nœud suivant, comme l'échéance.
    /// </summary>
    IAttenteTempsBuilder SurInterruption(string noeud);
}
