using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Sentier.Autofac;
using Sentier.Dapper;
using Sentier.Persistance;

namespace Sentier.Tests;

public class AutofacTests
{
    [Fact]
    public async Task Le_module_enregistre_le_moteur_et_decouvre_definitions_et_handlers()
    {
        using var connexion = new System.Data.SQLite.SQLiteConnection("Data Source=:memory:");
        connexion.Open();

        // Comme dans l'application : services Microsoft versés dans Autofac, puis une ligne pour Sentier.
        var services = new ServiceCollection();
        services.AddSingleton<Journal>();
        var builder = new ContainerBuilder();
        builder.Populate(services);
        builder.RegisterModule(new ModuleSentier(o => { o.Prefixe = "AT"; o.Suffixe = "TAXE"; }, typeof(AutofacTests).Assembly)
        {
            Filtre = t => t.IsPublic, // écarte les définitions d'essai imbriquées dans les classes de test
        });
        builder.Register(_ => new AccesBDDapper(connexion, DialecteSentier.Sqlite)).As<IAccesBDSentier>().InstancePerLifetimeScope();
        builder.RegisterType<BassinEnMemoire>().As<IBassinTaches>().SingleInstance();
        using var conteneur = builder.Build();

        await using var scope = conteneur.BeginLifetimeScope();
        var bd = scope.Resolve<IAccesBDSentier>();
        foreach (var script in SchemaSentier.Creation(DialecteSentier.Sqlite, scope.Resolve<OptionsSentier>()))
            await bd.ExecuteNonQueryAsync(script);

        var moteur = scope.Resolve<IMoteurSentier>();
        var etat = await moteur.DemarrerAsync(new DemandeDemarrage("DeclOrign", 7, "cle", "alice")
        {
            Donnees = new Donnees().Avec(DeclarationOriginale.Langue, "fr").Avec(DeclarationOriginale.Montant, 2000m),
        });

        Assert.Equal("verification", etat.NoeudCourant);
        Assert.Contains("avis:AVIS_RECEPTION:fr", scope.Resolve<Journal>().Lignes);
        Assert.Contains("DeclOrign", scope.Resolve<ISuiviSentier>().ClesDefinitions);
        Assert.Empty(await scope.Resolve<ISuiviSentier>().VerifierInstancesAsync());
        Assert.Single(scope.Resolve<IEnumerable<IEcouteurFinProcessus>>()); // EcouteurEnMemoire, découvert par balayage
    }
}
