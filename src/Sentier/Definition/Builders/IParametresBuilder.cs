namespace Sentier;

public interface IParametresBuilder
{
    /// <summary>Valeur statique.</summary>
    IParametresBuilder Valeur<T>(Parametre<T> parametre, T valeur);

    /// <summary>Valeur lue d'une variable de l'instance au moment de l'appel.</summary>
    IParametresBuilder Variable<T>(Parametre<T> parametre, Variable<T> variable);
}
