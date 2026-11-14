using System.Reflection;
using Autofac;

namespace Sentier.Autofac;

/// <summary>
/// Enregistre le moteur et découvre, dans les assemblies fournies, les définitions de processus, les handlers
/// (commandes et requêtes) et les écouteurs de fin de processus.
/// Le client enregistre lui-même <see cref="IAccesBDSentier"/> et <see cref="IBassinTaches"/>.
/// </summary>
public sealed class ModuleSentier(Action<OptionsSentier> configurer, params Assembly[] assemblies) : global::Autofac.Module
{
    /// <summary>Restreint le balayage aux types retenus ; par défaut, tous les types des assemblies.</summary>
    public Func<Type, bool> Filtre { get; init; } = _ => true;

    protected override void Load(ContainerBuilder builder)
    {
        var options = new OptionsSentier();
        configurer(options);
        builder.RegisterInstance(options);

        builder.RegisterAssemblyTypes(assemblies)
            .Where(Filtre)
            .AssignableTo<IDefinitionProcessus>()
            .As<IDefinitionProcessus>()
            .SingleInstance();

        // Les définitions sont construites et validées une seule fois, à la première résolution.
        builder.RegisterType<RegistreDefinitions>().SingleInstance();

        builder.RegisterAssemblyTypes(assemblies)
            .Where(t => Filtre(t) && (typeof(ICommande).IsAssignableFrom(t) || typeof(IRequete).IsAssignableFrom(t)))
            .AsSelf()
            .InstancePerDependency();

        builder.RegisterAssemblyTypes(assemblies)
            .Where(Filtre)
            .AssignableTo<IEcouteurFinProcessus>()
            .As<IEcouteurFinProcessus>()
            .InstancePerLifetimeScope();

        // Le moteur et les handlers sont résolus dans le scope courant : ils partagent les services du client.
        builder.RegisterType<ResolveurAutofac>().As<IResolveurSentier>().InstancePerLifetimeScope();
        builder.Register(c => new MoteurSentier(
                c.Resolve<RegistreDefinitions>(),
                c.Resolve<IAccesBDSentier>(),
                c.Resolve<IResolveurSentier>(),
                c.Resolve<IBassinTaches>(),
                c.Resolve<IEnumerable<IEcouteurFinProcessus>>(),
                c.Resolve<OptionsSentier>(),
                c.ResolveOptional<TimeProvider>()))
            .As<IMoteurSentier>()
            .InstancePerLifetimeScope();
    }

    private sealed class ResolveurAutofac(ILifetimeScope scope) : IResolveurSentier
    {
        public object Resoudre(Type type) => scope.Resolve(type);
    }
}
