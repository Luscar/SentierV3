namespace Sentier;

public interface ISousProcessusBuilder
{
    /// <summary>Le nom de la fin atteinte par l'enfant choisit le nœud de reprise du parent.</summary>
    ISousProcessusBuilder Sur(string nomFin, string noeud);
}
