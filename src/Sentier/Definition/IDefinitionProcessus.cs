namespace Sentier;

/// <summary>Une définition de processus : une classe par processus, nommée d'après lui.</summary>
public interface IDefinitionProcessus
{
    /// <summary>Clé de la définition, conservée avec chaque instance (le code du type de processus).</summary>
    string Cle { get; }

    void Definir(IProcessusBuilder p);
}
