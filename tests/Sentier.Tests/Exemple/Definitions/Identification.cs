namespace Sentier.Tests;

/// <summary>Sous-processus sans point d'attente : s'exécute dans la transaction du parent.</summary>
public sealed class Identification : IDefinitionProcessus
{
    public const string CleDefinition = "Identif";
    public string Cle => CleDefinition;

    public void Definir(IProcessusBuilder p) => p
        .Systeme<ValiderDeclaration>("identifier")
        .Fin("identifiee");
}
