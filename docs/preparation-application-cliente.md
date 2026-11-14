# Préparer l'application cliente pour Sentier

Ce document couvre uniquement le branchement de Sentier dans une application : les packages, ce que l'application doit fournir, ce qu'elle peut fournir, et comment le moteur, les définitions et les handlers sont enregistrés dans le conteneur. L'écriture des processus et le pilotage des instances sont dans le [guide d'utilisation](guide-utilisation.md).

- [1. Vue d'ensemble](#1-vue-densemble)
- [2. Packages](#2-packages)
- [3. Abstractions obligatoires](#3-abstractions-obligatoires)
- [4. Abstractions facultatives](#4-abstractions-facultatives)
- [5. Enregistrement avec Autofac](#5-enregistrement-avec-autofac)
- [6. Enregistrement sans Autofac](#6-enregistrement-sans-autofac)
- [7. Handlers et leurs dépendances](#7-handlers-et-leurs-dépendances)
- [8. Base de données et transaction](#8-base-de-données-et-transaction)
- [9. Vérifications au démarrage](#9-vérifications-au-démarrage)
- [10. Liste de contrôle](#10-liste-de-contrôle)

## 1. Vue d'ensemble

| Élément | Qui le fournit | Obligatoire | Durée de vie |
|---|---|---|---|
| `OptionsSentier` | L'application, à l'enregistrement | Oui | Singleton |
| `IAccesBDSentier` | L'application (adaptateur maison ou `Sentier.Dapper`) | Oui | Par scope |
| `IBassinTaches` | L'application | Oui | Par scope ou singleton |
| `IResolveurSentier` | `Sentier.Autofac`, sinon l'application | Oui | Par scope |
| `IDefinitionProcessus` | L'application, une classe par processus | Au moins une | Singleton |
| `ICommande`, `IRequete<T>` | L'application | Selon les définitions | Une instance par résolution |
| `IEcouteurFinProcessus` | L'application | Non | Par scope |
| `TimeProvider` | L'application | Non (horloge du système par défaut) | Singleton |
| `RegistreDefinitions` | Sentier | — | Singleton |
| `IMoteurSentier` | Sentier | — | Par scope |

« Par scope » veut dire le scope de la requête HTTP ou du traitement en cours : le moteur, l'accès aux données et les handlers doivent partager la même connexion et la même transaction.

## 2. Packages

| Package | Quand l'installer | Dépendance |
|---|---|---|
| `Sentier` | Toujours | aucune |
| `Sentier.Autofac` | L'application utilise Autofac | Autofac 8.3.0 |
| `Sentier.Dapper` | L'application n'a pas sa couche d'accès aux données, ou pour les tests sur SQLite | Dapper 2.1.79 |

Les packages ciblent .NET 10. Le projet qui contient les définitions et les handlers n'a besoin que de `Sentier` ; seul le projet qui compose le conteneur référence `Sentier.Autofac`.

## 3. Abstractions obligatoires

### 3.1 `IAccesBDSentier` : l'accès aux données

Quatre méthodes, dont les signatures reprennent celles de `IAccesBDPlus`. L'adaptateur ne fait que déléguer :

```csharp
public class AccesBDSentier(IAccesBDPlus bd) : IAccesBDSentier
{
    public Task<int> ExecuteNonQueryAsync(string requeteSQL, object? parametres = null, CancellationToken cancellationToken = default)
        => bd.ExecuteNonQueryAsync(requeteSQL, parametres, cancellationToken);
    public Task<IEnumerable<T>> QueryAsync<T>(string requeteSQL, object? parametres = null, CancellationToken cancellationToken = default)
        => bd.QueryAsync<T>(requeteSQL, parametres, cancellationToken);
    public Task<T?> QuerySingleOrDefaultAsync<T>(string requeteSQL, object? parametres = null, CancellationToken cancellationToken = default)
        => bd.QuerySingleOrDefaultAsync<T>(requeteSQL, parametres, cancellationToken);
    public Task<long> GetSequenceAsync(string sequence, CancellationToken cancellationToken = default)
        => bd.GetSequenceAsync(sequence, cancellationToken);
}
```

Sans couche maison, `Sentier.Dapper` fournit `new AccesBDDapper(connexion, DialecteSentier.Oracle)` sur une connexion déjà ouverte.

Ce que l'implémentation doit respecter :

- Le SQL du moteur utilise des paramètres `:nom`, passés en objet anonyme, et des colonnes aliasées sans soulignement.
- `GetSequenceAsync` reçoit le nom complet de la séquence (`AT_S_INSTANCE_TAXE`) et retourne la valeur suivante.
- Le moteur n'ouvre, ne ferme ni ne valide rien : la connexion doit participer au `TransactionScope` de l'appelant (section 8).

### 3.2 `IBassinTaches` : le bassin de tâches

Un nœud interactif crée une tâche dans le bassin de l'application et attend sa complétion.

```csharp
public class BassinTaches(IServiceTaches taches) : IBassinTaches
{
    public Task<long> CreerTacheAsync(DemandeTache demande, CancellationToken cancellationToken)
        => taches.CreerAsync(demande.CodeTache, demande.IdAffaire, demande.AssignerAActeur ? demande.Acteur : null, cancellationToken);

    public Task AnnulerTacheAsync(long refTache, string acteur, CancellationToken cancellationToken)
        => taches.AnnulerAsync(refTache, acteur, cancellationToken);
}
```

- `CreerTacheAsync` retourne l'identifiant de la tâche dans le bassin. Le moteur le conserve ; l'application le repasse à `CompleterTacheAsync` quand la tâche est terminée.
- `DemandeTache` porte `CodeTache`, `IdInstance`, `IdAffaire`, `CleCorrelation`, `CleDefinition`, `Noeud`, `Acteur` et `AssignerAActeur`.
- `AnnulerTacheAsync` est appelé quand une instance arrêtée sur une tâche est annulée.

Le moteur exige un `IBassinTaches` même si aucune définition n'a de nœud interactif. Dans ce cas, enregistrez une implémentation qui lève une exception :

```csharp
public sealed class AucunBassin : IBassinTaches
{
    public Task<long> CreerTacheAsync(DemandeTache demande, CancellationToken cancellationToken)
        => throw new NotSupportedException("Cette application n'a pas de bassin de tâches.");
    public Task AnnulerTacheAsync(long refTache, string acteur, CancellationToken cancellationToken)
        => throw new NotSupportedException("Cette application n'a pas de bassin de tâches.");
}
```

### 3.3 `OptionsSentier`

| Option | Défaut | Effet |
|---|---|---|
| `Prefixe`, `Suffixe` | vides | Noms des tables et des séquences : `{PRÉFIXE}_INSTANCE_{SUFFIXE}`, `{PRÉFIXE}_S_INSTANCE_{SUFFIXE}`. |
| `HistoriqueActif` | `false` | Consigne chaque entrée et sortie de nœud dans la table `HISTORIQUE`. |
| `NombreMaxEtapes` | `1000` | Nombre maximal de nœuds exécutés par appel, contre les boucles sans fin. |

Le préfixe et le suffixe doivent être ceux utilisés pour générer les scripts de création des tables. Un nom qui n'est pas un identifiant simple (lettres, chiffres, soulignement) est refusé.

### 3.4 `IResolveurSentier` : la fabrique de handlers

Le moteur demande chaque commande et chaque requête au résolveur, par son type, au moment de l'exécuter. Avec `Sentier.Autofac`, le résolveur est fourni et branché sur le scope courant : il n'y a rien à écrire. Sans Autofac, voir la section 6.

## 4. Abstractions facultatives

### 4.1 `IEcouteurFinProcessus`

Avisé dans la transaction qui termine ou annule une instance. L'application peut en enregistrer zéro, un ou plusieurs ; tous reçoivent toutes les fins, y compris celles des sous-processus.

```csharp
public sealed class CloreDemande(IDepotDemandes demandes) : IEcouteurFinProcessus
{
    public Task SurFinAsync(FinProcessus fin, CancellationToken cancellationToken)
    {
        if (fin.EstSousProcessus || fin.CleDefinition != DemandeRemboursement.CleDefinition) return Task.CompletedTask;
        return demandes.MarquerCloseAsync(fin.IdAffaire, fin.NomFin, cancellationToken);
    }
}
```

### 4.2 `TimeProvider`

Le moteur lit l'heure locale de `TimeProvider` pour les dates de démarrage, de modification et d'échéance. Sans enregistrement, il prend `TimeProvider.System`. Enregistrez le vôtre si l'application a déjà une horloge commune, ou dans les tests pour faire passer les échéances.

### 4.3 Déclencheur d'échéances

Ce n'est pas une interface de Sentier, mais le moteur n'a pas de minuterie. Si une définition utilise `AttenteTemps` ou une escalade (`Echeance` sur un nœud interactif ou une attente de signal), l'application doit avoir un traitement planifié qui appelle `ListerEcheancesDuesAsync` puis `DeclencherEcheanceAsync` (guide, section 6.5). Sans lui, ces instances restent en attente.

## 5. Enregistrement avec Autofac

```csharp
// 1. Le moteur, les définitions, les handlers et les écouteurs des assemblies indiquées.
builder.RegisterModule(new ModuleSentier(
    o => { o.Prefixe = "AT"; o.Suffixe = "TAXE"; o.HistoriqueActif = true; },
    typeof(DemandeRemboursement).Assembly));

// 2. Les deux adaptateurs obligatoires.
builder.RegisterType<AccesBDSentier>().As<IAccesBDSentier>().InstancePerLifetimeScope();
builder.RegisterType<BassinTaches>().As<IBassinTaches>().InstancePerLifetimeScope();

// 3. Facultatif : l'horloge.
builder.RegisterInstance(TimeProvider.System).As<TimeProvider>();
```

L'ordre des enregistrements n'a pas d'importance. Si les services de l'application sont déclarés dans une `ServiceCollection`, versez-la dans Autofac avec `builder.Populate(services)` : les handlers y trouvent leurs dépendances.

### Ce que le module enregistre

| Types | Découverte | Enregistrement |
|---|---|---|
| `OptionsSentier` | Construit par le module | Instance unique |
| `IDefinitionProcessus` | Balayage | `As<IDefinitionProcessus>()`, singleton |
| `RegistreDefinitions` | — | Singleton ; construit et valide les définitions à sa première résolution |
| `ICommande`, `IRequete<T>` | Balayage | Sous leur propre type (`AsSelf`), une instance par résolution |
| `IEcouteurFinProcessus` | Balayage | `As<IEcouteurFinProcessus>()`, par scope |
| `IResolveurSentier` | — | Par scope ; résout les handlers dans le scope courant |
| `IMoteurSentier` | — | Par scope |
| `ISuiviSentier` | — | Par scope |

### Le balayage

- Passez toutes les assemblies qui contiennent des définitions, des handlers ou des écouteurs : `new ModuleSentier(configurer, assemblyA, assemblyB)`.
- Le balayage retient toutes les classes concrètes, publiques ou non, imbriquées comprises. `Filtre` en écarte : `new ModuleSentier(...) { Filtre = t => t.IsPublic }`.
- Une définition est un singleton : son constructeur ne doit pas dépendre d'un service par scope. Le plus simple est un constructeur sans paramètre.
- Un handler ou un écouteur situé dans une assembly non balayée s'enregistre à la main, de la même façon que le module :

```csharp
builder.RegisterType<EnvoyerCourriel>().AsSelf().InstancePerDependency();
builder.RegisterType<CloreDemande>().As<IEcouteurFinProcessus>().InstancePerLifetimeScope();
```

### Résoudre le moteur

`IMoteurSentier` s'injecte par constructeur comme tout service par scope. Ne le résolvez pas depuis le conteneur racine et ne le gardez pas dans un singleton : il tiendrait une connexion périmée.

## 6. Enregistrement sans Autofac

Vous construisez le moteur vous-même et fournissez le résolveur. Exemple avec le conteneur de Microsoft :

```csharp
public sealed class ResolveurServices(IServiceProvider services) : IResolveurSentier
{
    public object Resoudre(Type type) => services.GetRequiredService(type);
}
```

```csharp
var assembly = typeof(DemandeRemboursement).Assembly;
var concrets = assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false }).ToArray();

// Options et registre : singletons.
services.AddSingleton(new OptionsSentier { Prefixe = "AT", Suffixe = "TAXE" });
foreach (var type in concrets.Where(t => typeof(IDefinitionProcessus).IsAssignableFrom(t)))
    services.AddSingleton(typeof(IDefinitionProcessus), type);
services.AddSingleton<RegistreDefinitions>();

// Handlers : sous leur propre type.
foreach (var type in concrets.Where(t => typeof(ICommande).IsAssignableFrom(t) || typeof(IRequete).IsAssignableFrom(t)))
    services.AddTransient(type);

// Écouteurs (facultatifs).
foreach (var type in concrets.Where(t => typeof(IEcouteurFinProcessus).IsAssignableFrom(t)))
    services.AddScoped(typeof(IEcouteurFinProcessus), type);

// Adaptateurs, résolveur et moteur : par scope.
services.AddScoped<IAccesBDSentier, AccesBDSentier>();
services.AddScoped<IBassinTaches, BassinTaches>();
services.AddScoped<IResolveurSentier, ResolveurServices>();
services.AddScoped<IMoteurSentier>(sp => new MoteurSentier(
    sp.GetRequiredService<RegistreDefinitions>(),
    sp.GetRequiredService<IAccesBDSentier>(),
    sp.GetRequiredService<IResolveurSentier>(),
    sp.GetRequiredService<IBassinTaches>(),
    sp.GetServices<IEcouteurFinProcessus>(),
    sp.GetRequiredService<OptionsSentier>(),
    sp.GetService<TimeProvider>()));
services.AddScoped<ISuiviSentier>(sp => new SuiviSentier(
    sp.GetRequiredService<RegistreDefinitions>(),
    sp.GetRequiredService<IAccesBDSentier>(),
    sp.GetRequiredService<OptionsSentier>(),
    sp.GetService<TimeProvider>()));
```

Les règles sont les mêmes qu'avec Autofac : le registre est un singleton (la validation ne se fait qu'une fois), le moteur et le résolveur vivent dans le scope de l'appel.

## 7. Handlers et leurs dépendances

Une commande (`ICommande`) ou une requête (`IRequete<TResultat>`) est une classe ordinaire de l'application. Elle n'est liée à aucun nom : la définition la désigne par son type (`.Systeme<EnvoyerCourriel>(...)`, `.Decision<ExigeApprobation>(...)`), et le moteur demande ce type au résolveur.

```csharp
public sealed class EnvoyerCourriel(IServiceCourriel courriels) : ICommande
{
    public static readonly Parametre<string> Modele = Parametre.Texte("modele");

    public Task ExecuterAsync(ContexteCommande contexte, CancellationToken cancellationToken)
        => courriels.EnvoyerAsync(contexte.IdAffaire, contexte.Parametres.Lire(Modele)!, cancellationToken);
}
```

- **Enregistrement.** Le handler doit être résoluble sous son propre type. Le module Autofac s'en charge par balayage ; sinon, voir les sections 5 et 6.
- **Dépendances.** Elles arrivent par le constructeur et doivent être enregistrées par l'application. Résolu dans le scope courant, le handler reçoit les mêmes services par scope que le code qui a appelé le moteur, donc la même connexion et la même transaction.
- **Durée de vie.** Une nouvelle instance à chaque exécution : ne conservez pas d'état dans un handler, utilisez les variables de l'instance.
- **Paramètres.** Ils se déclarent en constantes statiques sur le handler ; la définition les fournit, et un paramètre obligatoire manquant est refusé à la validation.

La validation des définitions vérifie les paramètres, pas le conteneur : un handler non enregistré, ou dont une dépendance manque, n'échoue qu'à l'exécution du nœud. Un test de composition ferme ce trou :

```csharp
[Fact]
public void Tous_les_handlers_se_resolvent()
{
    using var conteneur = Composition.Construire();          // le conteneur de l'application
    using var scope = conteneur.BeginLifetimeScope();

    var handlers = typeof(DemandeRemboursement).Assembly.GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false })
        .Where(t => typeof(ICommande).IsAssignableFrom(t) || typeof(IRequete).IsAssignableFrom(t));

    foreach (var type in handlers) scope.Resolve(type);
    scope.Resolve<IMoteurSentier>();
}
```

## 8. Base de données et transaction

**Tables.** Le package n'exécute jamais de DDL. `SchemaSentier.Creation` retourne les scripts, à passer dans vos livraisons (Oracle) ou dans la préparation des tests (SQLite) :

```csharp
var options = new OptionsSentier { Prefixe = "AT", Suffixe = "TAXE" };
foreach (var script in SchemaSentier.Creation(DialecteSentier.Oracle, options))
    Console.WriteLine(script + ";");
```

Objets créés : `AT_INSTANCE_TAXE`, `AT_HISTORIQUE_TAXE`, et les séquences `AT_S_INSTANCE_TAXE` et `AT_S_HISTORIQUE_TAXE`. La table d'historique est créée même si `HistoriqueActif` est à `false`.

Une base créée avec une version antérieure doit recevoir la colonne `GROUPE_SIGNAL` (texte de 200, nulle) et son index sur la table des instances.

**Transaction.** Le moteur n'en gère aucune. Chaque appel se fait dans un `TransactionScope` ouvert par l'appelant, avec le flux asynchrone activé :

```csharp
using var portee = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
await moteur.DemarrerAsync(demande, ct);
portee.Complete();
```

Le moteur, les handlers, le bassin et les écouteurs travaillent dans cette transaction : tout est validé ou annulé d'un bloc.

## 9. Vérifications au démarrage

Au démarrage de l'application, dans un scope :

```csharp
var orphelines = await suivi.VerifierInstancesAsync(ct);   // ISuiviSentier
foreach (var o in orphelines)
    journal.LogError("Instance {Id} ({Definition}) orpheline sur {Noeud} : {Raison}", o.IdInstance, o.CleDefinition, o.Noeud, o.Raison);
```

Cet appel fait deux choses :

- il résout le suivi, donc construit le registre : une définition invalide lève `DefinitionInvalideException` au démarrage plutôt qu'au premier usage ;
- il liste les instances en cours que le code livré ne sait plus faire avancer : définition ou nœud retiré sans correspondance, ou nœud devenu d'une autre nature d'attente.

Le moteur n'exécute jamais cette vérification seul : c'est à l'application de l'appeler, au démarrage ou à la livraison d'une définition modifiée. `ISuiviSentier` offre aussi la migration des instances et les diagrammes Mermaid (guide, section 9).

## 10. Liste de contrôle

- [ ] Packages installés : `Sentier`, plus `Sentier.Autofac` et `Sentier.Dapper` selon le cas.
- [ ] Scripts de `SchemaSentier.Creation` passés sur la base, avec le même préfixe et le même suffixe que les options.
- [ ] `IAccesBDSentier` enregistré par scope, sur la connexion qui participe au `TransactionScope`.
- [ ] `IBassinTaches` enregistré.
- [ ] `ModuleSentier` enregistré avec toutes les assemblies de définitions, de handlers et d'écouteurs (ou enregistrement manuel de la section 6).
- [ ] Dépendances des handlers enregistrées ; test de composition en place.
- [ ] Chaque appel au moteur dans un `TransactionScope` avec `TransactionScopeAsyncFlowOption.Enabled`.
- [ ] Traitement planifié des échéances, si une définition utilise `AttenteTemps` ou `Echeance`.
- [ ] `ISuiviSentier.VerifierInstancesAsync` appelé au démarrage, ou à chaque livraison qui modifie une définition.
