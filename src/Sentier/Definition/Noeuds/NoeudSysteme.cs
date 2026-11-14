namespace Sentier;

internal sealed class NoeudSysteme(string id, AppelHandler commande) : Noeud(id)
{
    public AppelHandler Commande { get; } = commande;
}
