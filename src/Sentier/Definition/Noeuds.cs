using System.Reflection;

namespace Sentier;

internal abstract class Noeud(string id)
{
    public string Id { get; } = id;

    /// <summary>Nœud suivant dans le déroulement normal ; fixé à la construction.</summary>
    public string? Suivant { get; set; }

    public bool SuivantExplicite { get; set; }

    public virtual bool ExigeSuivant => true;

    public virtual TypeAttente? Attente => null;

    /// <summary>Toutes les sorties possibles, pour la validation.</summary>
    public virtual IEnumerable<string> Cibles => Suivant is null ? [] : [Suivant];
}

internal sealed class NoeudSysteme(string id, AppelHandler commande) : Noeud(id)
{
    public AppelHandler Commande { get; } = commande;
}

internal sealed class NoeudDecision(string id) : Noeud(id)
{
    public AppelHandler? Requete { get; init; }
    public Func<Variables, object?>? LectureVariable { get; init; }
    public required Type TypeResultat { get; init; }
    public List<(object Valeur, string Noeud)> Branches { get; } = [];
    public string? Sinon { get; set; }

    public override bool ExigeSuivant => false;

    public override IEnumerable<string> Cibles => Branches.Select(b => b.Noeud).Concat(Sinon is null ? [] : [Sinon]);

    public string Choisir(object? resultat)
        => Branches.Where(b => b.Valeur.Equals(resultat)).Select(b => b.Noeud).FirstOrDefault() ?? Sinon!;
}

internal sealed class NoeudInteractif(string id, string codeTache) : Noeud(id)
{
    public string CodeTache { get; } = codeTache;
    public AppelHandler? Avant { get; set; }
    public AppelHandler? Apres { get; set; }
    public Dictionary<string, string> Transitions { get; } = new(StringComparer.Ordinal);

    public override TypeAttente? Attente => TypeAttente.Tache;

    public override IEnumerable<string> Cibles => base.Cibles.Concat(Transitions.Values);
}

internal sealed class NoeudAttenteSignal(string id) : Noeud(id)
{
    /// <summary>Signaux acceptés ; une cible nulle mène au nœud suivant.</summary>
    public Dictionary<string, string?> Signaux { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Transitions { get; } = new(StringComparer.Ordinal);

    public override TypeAttente? Attente => TypeAttente.Signal;

    public override IEnumerable<string> Cibles
        => base.Cibles.Concat(Signaux.Values.OfType<string>()).Concat(Transitions.Values);
}

internal sealed class NoeudAttenteTemps(string id) : Noeud(id)
{
    public TimeSpan? Duree { get; init; }
    public Variable<DateTime>? Date { get; init; }

    public override TypeAttente? Attente => TypeAttente.Temps;
}

internal sealed class NoeudSousProcessus(string id, string cleDefinition) : Noeud(id)
{
    public string CleDefinition { get; } = cleDefinition;
    public Dictionary<string, string> Sorties { get; } = new(StringComparer.Ordinal);

    public override TypeAttente? Attente => TypeAttente.SousProcessus;

    public override IEnumerable<string> Cibles => base.Cibles.Concat(Sorties.Values);
}

internal sealed class NoeudFin(string id, string nomFin) : Noeud(id)
{
    public string NomFin { get; } = nomFin;

    public override bool ExigeSuivant => false;

    public override IEnumerable<string> Cibles => [];
}

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
