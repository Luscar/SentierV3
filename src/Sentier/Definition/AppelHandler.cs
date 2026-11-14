using System.Reflection;

namespace Sentier;

/// <summary>Appel d'un handler avec ses paramètres, tel que fixé dans la définition.</summary>
internal sealed class AppelHandler(Type type)
{
    private readonly List<(string Nom, Func<Variables, object?> Valeur)> _affectations = [];

    public Type Type { get; } = type;

    /// <summary>Pour une requête : appelle IRequete&lt;T&gt; sans réflexion à l'exécution.</summary>
    public Func<object, ContexteRequete, CancellationToken, Task<object?>>? InvocateurRequete { get; init; }

    public void Affecter(string nom, Func<Variables, object?> valeur) => _affectations.Add((nom, valeur));

    public Parametres Resoudre(Variables variables)
    {
        var parametres = new Parametres();
        foreach (var (nom, valeur) in _affectations) parametres.Definir(nom, valeur(variables));
        return parametres;
    }

    /// <summary>Paramètres obligatoires déclarés en constantes sur le handler et absents de l'appel.</summary>
    public IEnumerable<string> ParametresManquants()
    {
        var fournis = _affectations.Select(a => a.Nom).ToHashSet(StringComparer.Ordinal);
        return Type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => typeof(ParametreBase).IsAssignableFrom(f.FieldType))
            .Select(f => f.GetValue(null))
            .OfType<ParametreBase>()
            .Where(p => p.Obligatoire && !fournis.Contains(p.Nom))
            .Select(p => p.Nom);
    }

    public static Type? TypeResultatRequete(Type typeRequete)
        => typeRequete.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequete<>))
            ?.GetGenericArguments()[0];

    public static Func<object, ContexteRequete, CancellationToken, Task<object?>> CreerInvocateur(Type typeResultat)
        => (Func<object, ContexteRequete, CancellationToken, Task<object?>>)typeof(AppelHandler)
            .GetMethod(nameof(Invocateur), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(typeResultat)
            .Invoke(null, null)!;

    private static Func<object, ContexteRequete, CancellationToken, Task<object?>> Invocateur<T>()
        => async (handler, contexte, ct) => await ((IRequete<T>)handler).ExecuterAsync(contexte, ct);
}
