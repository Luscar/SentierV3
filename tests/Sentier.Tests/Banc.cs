using System.Data.SQLite;
using Sentier.Dapper;
using Sentier.Persistance;

namespace Sentier.Tests;

/// <summary>Un moteur complet sur une base SQLite en mémoire, comme dans les tests du client.</summary>
public sealed class Banc : IDisposable
{
    public SQLiteConnection Connexion { get; } = new("Data Source=:memory:");
    public OptionsSentier Options { get; } = new() { Prefixe = "AT", Suffixe = "TAXE", HistoriqueActif = true };
    public Journal Journal { get; } = new();
    public BassinEnMemoire Bassin { get; } = new();
    public EcouteurEnMemoire Ecouteur { get; } = new();
    public HorlogeReglable Horloge { get; } = new();
    public AccesBDDapper Bd { get; }
    public MoteurSentier Moteur { get; }

    public Banc(params IDefinitionProcessus[] definitions)
    {
        Connexion.Open();
        Bd = new AccesBDDapper(Connexion, DialecteSentier.Sqlite);
        foreach (var script in SchemaSentier.Creation(DialecteSentier.Sqlite, Options))
            Bd.ExecuteNonQueryAsync(script).GetAwaiter().GetResult();

        if (definitions.Length == 0) definitions = [new DeclarationOriginale(), new Identification(), new Comptabilisation()];
        Moteur = CreerMoteur(definitions);
    }

    public MoteurSentier CreerMoteur(params IDefinitionProcessus[] definitions)
        => new(new RegistreDefinitions(definitions), Bd, new Resolveur(this), Bassin, [Ecouteur], Options, Horloge);

    public DemandeDemarrage Demande(string cle = "cle-1", decimal montant = 5000m) => new("DeclOrign", 42, cle, "alice")
    {
        Donnees = new Donnees().Avec(DeclarationOriginale.Langue, "fr").Avec(DeclarationOriginale.Montant, montant),
    };

    public void Dispose() => Connexion.Dispose();

    private sealed class Resolveur(Banc banc) : IResolveurSentier
    {
        public object Resoudre(Type type)
            => type.GetConstructors()[0].GetParameters().Length == 0
                ? Activator.CreateInstance(type)!
                : Activator.CreateInstance(type, banc.Journal)!;
    }
}
