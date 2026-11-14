namespace Sentier;

/// <summary>Variable d'instance typée, déclarée une fois en constante sur la classe de la définition.</summary>
public sealed class Variable<T>
{
    internal Variable(string nom) => Nom = Garde.Nom(nom, "variable");

    /// <summary>Nom conservé avec l'instance.</summary>
    public string Nom { get; }

    public override string ToString() => Nom;
}

public static class Variable
{
    public static Variable<T> De<T>(string nom) => new(nom);
    public static Variable<string> Texte(string nom) => new(nom);
    public static Variable<int> Entier(string nom) => new(nom);
    public static Variable<long> Long(string nom) => new(nom);
    public static Variable<bool> Booleen(string nom) => new(nom);
    public static Variable<decimal> Decimal(string nom) => new(nom);
    public static Variable<DateTime> Date(string nom) => new(nom);
}
