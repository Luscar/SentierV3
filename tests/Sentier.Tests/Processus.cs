namespace Sentier.Tests;

// Définitions et handlers d'essai, calqués sur l'esquisse du document de besoins.

public sealed class Journal
{
    public List<string> Lignes { get; } = [];
}

public sealed class ValiderDeclaration(Journal journal) : ICommande
{
    public Task ExecuterAsync(ContexteCommande c, CancellationToken ct)
    {
        journal.Lignes.Add($"valider:{c.IdAffaire}:{c.Acteur}");
        return Task.CompletedTask;
    }
}

public sealed class EnvoyerAvis(Journal journal) : ICommande
{
    public static readonly Parametre<string> Modele = Parametre.Texte("modele");
    public static readonly Parametre<string> Langue = Parametre.Texte("langue");

    public Task ExecuterAsync(ContexteCommande c, CancellationToken ct)
    {
        journal.Lignes.Add($"avis:{c.Parametres.Lire(Modele)}:{c.Parametres.Lire(Langue)}");
        return Task.CompletedTask;
    }
}

public sealed class DoitEtreVerifiee : IRequete<bool>
{
    public Task<bool> ExecuterAsync(ContexteRequete c, CancellationToken ct)
        => Task.FromResult(c.Variables.Lire(DeclarationOriginale.Montant) > 1000m);
}

public sealed class PreparerVerification(Journal journal) : ICommande
{
    public Task ExecuterAsync(ContexteCommande c, CancellationToken ct)
    {
        journal.Lignes.Add("avant-verification");
        return Task.CompletedTask;
    }
}

public sealed class ConsignerVerification(Journal journal) : ICommande
{
    public Task ExecuterAsync(ContexteCommande c, CancellationToken ct)
    {
        journal.Lignes.Add("apres-verification");
        c.Variables.Ecrire(DeclarationOriginale.Verifiee, true);
        return Task.CompletedTask;
    }
}

public sealed class DeclarationOriginale : IDefinitionProcessus
{
    public static readonly Variable<string> Langue = Variable.Texte("langue");
    public static readonly Variable<decimal> Montant = Variable.Decimal("montant");
    public static readonly Variable<bool> Verifiee = Variable.Booleen("verifiee");
    public static readonly Variable<string> Reference = Variable.Texte("reference");

    public string Cle => "DeclOrign";

    public void Definir(IProcessusBuilder p) => p
        .Systeme<ValiderDeclaration>("valider")
        .Systeme<EnvoyerAvis>("notifier", a => a
            .Valeur(EnvoyerAvis.Modele, "AVIS_RECEPTION")
            .Variable(EnvoyerAvis.Langue, Langue))
        .SousProcessus("identification", Identification.CleDefinition)
        .Decision<DoitEtreVerifiee>("a-verifier", d => d
            .Si(true, "verification")
            .Sinon("comptabilisation"))
        .Interactif("verification", "VERIF", t => t
            .Avant<PreparerVerification>()
            .Apres<ConsignerVerification>()
            .Sur("refusee", "valider")
            .Sur("doublon", "fin-doublon"))
        .SousProcessus("comptabilisation", Comptabilisation.CleDefinition)
        .AttenteSignal("attente-paiement", "paiement-recu", s => s
            .Signal("paiement-refuse", "fin-annulee"))
        .AttenteTemps("delai-grace", TimeSpan.FromDays(2))
        .Fin("cotisee")

        .Fin("fin-doublon", "doublon")
        .Fin("fin-annulee", "annulee");
}

/// <summary>Sous-processus sans point d'attente : s'exécute dans la transaction du parent.</summary>
public sealed class Identification : IDefinitionProcessus
{
    public const string CleDefinition = "Identif";
    public string Cle => CleDefinition;

    public void Definir(IProcessusBuilder p) => p
        .Systeme<ValiderDeclaration>("identifier")
        .Fin("identifiee");
}

/// <summary>Sous-processus avec un point d'attente.</summary>
public sealed class Comptabilisation : IDefinitionProcessus
{
    public const string CleDefinition = "Compta";
    public string Cle => CleDefinition;

    public void Definir(IProcessusBuilder p) => p
        .Interactif("comptabiliser", "COMPTA")
        .Fin("comptabilisee");
}

public sealed class BassinEnMemoire : IBassinTaches
{
    private long _prochain = 100;
    public List<DemandeTache> Creees { get; } = [];
    public List<long> Annulees { get; } = [];

    public Task<long> CreerTacheAsync(DemandeTache demande, CancellationToken cancellationToken)
    {
        Creees.Add(demande);
        return Task.FromResult(++_prochain);
    }

    public Task AnnulerTacheAsync(long refTache, string acteur, CancellationToken cancellationToken)
    {
        Annulees.Add(refTache);
        return Task.CompletedTask;
    }
}

public sealed class EcouteurEnMemoire : IEcouteurFinProcessus
{
    public List<FinProcessus> Fins { get; } = [];

    public Task SurFinAsync(FinProcessus fin, CancellationToken cancellationToken)
    {
        Fins.Add(fin);
        return Task.CompletedTask;
    }
}

public sealed class HorlogeReglable : TimeProvider
{
    public DateTimeOffset Maintenant { get; set; } = new(2026, 1, 15, 9, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Maintenant;
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}
