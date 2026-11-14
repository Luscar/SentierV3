namespace Sentier;

public interface IDecisionBuilder
{
    /// <summary>Branche prise quand le résultat égale <paramref name="valeur"/>.</summary>
    IDecisionBuilder Si(object valeur, string noeud);

    /// <summary>Branche par défaut, obligatoire.</summary>
    IDecisionBuilder Sinon(string noeud);

    IDecisionBuilder Valeur<T>(Parametre<T> parametre, T valeur);
    IDecisionBuilder Variable<T>(Parametre<T> parametre, Variable<T> variable);
}
