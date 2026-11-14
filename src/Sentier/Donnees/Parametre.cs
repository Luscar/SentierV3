namespace Sentier;

/// <summary>Base non générique d'un paramètre, pour la validation des définitions.</summary>
public abstract class ParametreBase
{
    private protected ParametreBase(string nom, bool obligatoire)
    {
        Nom = Garde.Nom(nom, "paramètre");
        Obligatoire = obligatoire;
    }

    public string Nom { get; }

    /// <summary>Un paramètre obligatoire doit être fourni par chaque appel dans une définition.</summary>
    public bool Obligatoire { get; }

    public override string ToString() => Nom;
}

/// <summary>Paramètre typé d'une commande ou d'une requête, déclaré une fois en constante sur la classe du handler.</summary>
public sealed class Parametre<T> : ParametreBase
{
    internal Parametre(string nom, bool obligatoire = true) : base(nom, obligatoire) { }

    /// <summary>Retourne le même paramètre, facultatif : absent, il se lit à la valeur par défaut du type.</summary>
    public Parametre<T> Facultatif() => new(Nom, false);
}

public static class Parametre
{
    public static Parametre<T> De<T>(string nom) => new(nom);
    public static Parametre<string> Texte(string nom) => new(nom);
    public static Parametre<int> Entier(string nom) => new(nom);
    public static Parametre<long> Long(string nom) => new(nom);
    public static Parametre<bool> Booleen(string nom) => new(nom);
    public static Parametre<decimal> Decimal(string nom) => new(nom);
    public static Parametre<DateTime> Date(string nom) => new(nom);
}
