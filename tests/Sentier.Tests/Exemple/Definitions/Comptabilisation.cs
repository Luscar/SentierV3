namespace Sentier.Tests;

/// <summary>Sous-processus avec un point d'attente.</summary>
public sealed class Comptabilisation : IDefinitionProcessus
{
    public const string CleDefinition = "Compta";
    public string Cle => CleDefinition;

    public void Definir(IProcessusBuilder p) => p
        .Interactif("comptabiliser", "COMPTA")
        .Fin("comptabilisee");
}
