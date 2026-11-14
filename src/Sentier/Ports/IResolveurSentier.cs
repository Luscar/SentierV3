namespace Sentier;

/// <summary>Fournit les handlers ; branché sur le conteneur du client.</summary>
public interface IResolveurSentier
{
    object Resoudre(Type type);
}
