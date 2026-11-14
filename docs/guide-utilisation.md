# Guide d'utilisation de Sentier

Ce guide explique comment préparer l'application cliente pour accueillir le moteur, puis comment écrire ses processus, ses commandes et ses requêtes, et comment piloter les instances.

L'exemple suivi tout au long du guide est une demande de remboursement. Un exemple complet et testé se trouve aussi dans `tests/Sentier.Tests/Exemple/`.

- [1. Les notions](#1-les-notions)
- [2. Préparer l'application cliente](#2-préparer-lapplication-cliente)
- [3. Définir un processus](#3-définir-un-processus)
- [4. Écrire une commande](#4-écrire-une-commande)
- [5. Écrire une requête](#5-écrire-une-requête)
- [6. Piloter les instances](#6-piloter-les-instances)
- [7. Réagir à la fin d'un processus](#7-réagir-à-la-fin-dun-processus)
- [8. Faire évoluer une définition](#8-faire-évoluer-une-définition)
- [9. Suivre et exploiter](#9-suivre-et-exploiter)
- [10. Tester](#10-tester)
- [11. Exceptions](#11-exceptions)

## 1. Les notions

| Notion | Ce que c'est |
|---|---|
| Définition | Une classe `IDefinitionProcessus` qui décrit les nœuds d'un processus. Elle est identifiée par sa clé. |
| Instance | Une exécution d'une définition, conservée dans la table `INSTANCE`. |
| Nœud | Une étape de la définition, identifiée par un texte unique dans la définition. |
| Commande | Une classe `ICommande` du client, exécutée par un nœud systémique ou autour d'une tâche. |
| Requête | Une classe `IRequete<TResultat>` du client, dont le résultat choisit la branche d'une décision. |
| Variable | Une donnée typée conservée avec l'instance (en JSON). |
| Paramètre | Une donnée typée passée à une commande ou à une requête, fixée dans la définition. |
| Clé de corrélation | L'identifiant d'affaires choisi par le client pour retrouver une instance. Elle est unique parmi les instances de tête. |
| Point d'attente | Un nœud où l'instance s'arrête : tâche, signal, délai ou sous-processus. |

Le moteur est passif : il n'a ni fil d'arrière-plan, ni état statique, ni transaction propre. Chaque appel exécute les nœuds à la suite, jusqu'au prochain point d'attente ou jusqu'à une fin, dans la transaction du client.

## 2. Préparer l'application cliente

Le détail du branchement (abstractions obligatoires et facultatives, enregistrement des handlers, sans Autofac) est dans [preparation-application-cliente.md](preparation-application-cliente.md).

### 2.1 Installer les packages

| Package | Quand l'installer |
|---|---|
| `Sentier` | Toujours. Il contient le builder, le moteur et le SQL des tables. |
| `Sentier.Autofac` | Si l'application utilise Autofac. Il enregistre le moteur et découvre les définitions et les handlers. |
| `Sentier.Dapper` | Si l'application n'a pas sa propre couche d'accès aux données, ou pour les tests sur SQLite. |

Les packages ciblent .NET 10.

### 2.2 Créer les tables

Le package n'exécute jamais de DDL. `SchemaSentier.Creation` retourne les scripts, que vous passez dans vos livraisons (Oracle) ou dans la préparation de vos tests (SQLite) :

```csharp
var options = new OptionsSentier { Prefixe = "AT", Suffixe = "TAXE" };
foreach (var script in SchemaSentier.Creation(DialecteSentier.Oracle, options))
    Console.WriteLine(script + ";");
```

Avec ces options, les objets créés sont :

| Objet | Nom |
|---|---|
| Table des instances | `AT_INSTANCE_TAXE` |
| Table de l'historique | `AT_HISTORIQUE_TAXE` |
| Séquences | `AT_S_INSTANCE_TAXE`, `AT_S_HISTORIQUE_TAXE` |

Le préfixe et le suffixe doivent être les mêmes dans les scripts et dans l'enregistrement du moteur (section 2.5).

### 2.3 Fournir l'accès aux données

Le moteur passe par `IAccesBDSentier`, qui a quatre méthodes. Deux façons de le fournir :

**Avec la couche d'accès de l'application.** Les signatures reprennent celles de `IAccesBDPlus`, donc l'adaptateur ne fait que déléguer :

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

**Avec `Sentier.Dapper`.** `new AccesBDDapper(connexion, DialecteSentier.Oracle)` sur une connexion déjà ouverte. Le moteur n'ouvre ni ne ferme la connexion.

Le SQL du moteur utilise des paramètres `:nom`.

### 2.4 Brancher le bassin de tâches

Un nœud interactif crée une tâche dans le bassin de l'application et attend qu'elle soit complétée. Vous écrivez l'adaptateur `IBassinTaches` :

```csharp
public class BassinTaches(IServiceTaches taches) : IBassinTaches
{
    public Task<long> CreerTacheAsync(DemandeTache demande, CancellationToken cancellationToken)
        => taches.CreerAsync(demande.CodeTache, demande.IdAffaire, demande.AssignerAActeur ? demande.Acteur : null, cancellationToken);

    public Task AnnulerTacheAsync(long refTache, string acteur, CancellationToken cancellationToken)
        => taches.AnnulerAsync(refTache, acteur, cancellationToken);
}
```

- `CreerTacheAsync` retourne l'identifiant de la tâche dans le bassin. Le moteur le conserve (`REF_TACHE`) : c'est avec lui que l'application complétera la tâche.
- `DemandeTache` porte le code de tâche du nœud, l'instance, l'affaire, la clé de corrélation, la définition, le nœud et l'acteur de l'appel.
- `AssignerAActeur` reprend `AssignerPremiereTache` de la demande de démarrage. L'assignation reste la décision du bassin.
- `AnnulerTacheAsync` est appelé quand une instance arrêtée sur une tâche est annulée.

### 2.5 Enregistrer le moteur

Avec Autofac, une ligne pour le moteur, puis vos deux adaptateurs :

```csharp
builder.RegisterModule(new ModuleSentier(
    o => { o.Prefixe = "AT"; o.Suffixe = "TAXE"; o.HistoriqueActif = true; },
    typeof(DemandeRemboursement).Assembly));
builder.RegisterType<AccesBDSentier>().As<IAccesBDSentier>().InstancePerLifetimeScope();
builder.RegisterType<BassinTaches>().As<IBassinTaches>().InstancePerLifetimeScope();
```

Le module balaie les assemblies fournies et enregistre :

| Types trouvés | Enregistrement |
|---|---|
| `IDefinitionProcessus` | Singleton. Les définitions sont construites et validées une seule fois, à la première résolution du moteur. |
| `ICommande`, `IRequete<T>` | Sous leur propre type, une instance par résolution. |
| `IEcouteurFinProcessus` | Une instance par scope. |
| `IMoteurSentier` | Une instance par scope. |

Les commandes et les requêtes sont résolues dans le scope courant : elles reçoivent par leur constructeur les mêmes services que le reste de la requête HTTP ou du traitement en cours.

`Filtre` restreint le balayage, par exemple `new ModuleSentier(...) { Filtre = t => t.IsPublic }`. Si un `TimeProvider` est enregistré dans le conteneur, le moteur l'utilise ; sinon il prend l'horloge du système. Les dates sont en heure locale.

Options disponibles :

| Option | Défaut | Effet |
|---|---|---|
| `Prefixe`, `Suffixe` | vides | Noms des tables et des séquences. |
| `HistoriqueActif` | `false` | Consigne chaque entrée et sortie de nœud, et chaque annulation, dans la table `HISTORIQUE`. |
| `NombreMaxEtapes` | `1000` | Nombre maximal de nœuds exécutés par appel, contre les boucles sans fin. |

**Sans Autofac**, vous construisez le moteur vous-même et fournissez un `IResolveurSentier` qui crée les handlers :

```csharp
var registre = new RegistreDefinitions([new DemandeRemboursement(), new Paiement()]);   // à garder en singleton
var moteur = new MoteurSentier(registre, accesBD, resolveur, bassin, ecouteurs, options);
```

### 2.6 Ouvrir la transaction

Le moteur ne gère aucune transaction. Chaque appel au moteur doit se faire dans un `TransactionScope` ouvert par le client, avec le flux asynchrone activé :

```csharp
using var portee = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

var idDemande = await demandes.CreerAsync(saisie, ct);
await moteur.DemarrerAsync(new DemandeDemarrage(DemandeRemboursement.CleDefinition, idDemande, $"REMB-{idDemande}", utilisateur), ct);

portee.Complete();
```

Tout ce que font le moteur, les commandes, le bassin et les écouteurs pendant l'appel est validé ou annulé d'un bloc avec le reste du travail du client. La connexion utilisée par `IAccesBDSentier` doit donc participer au scope.

### 2.7 Vérifier les instances au démarrage

Le moteur ne vérifie rien seul. Quand une définition change, c'est à l'application d'appeler `ISuiviSentier.VerifierInstancesAsync`, qui liste les instances en cours que le code livré ne sait plus faire avancer (section 9). L'appeler au démarrage de l'application est le plus simple :

```csharp
var orphelines = await suivi.VerifierInstancesAsync(ct);
foreach (var o in orphelines)
    journal.LogError("Instance {Id} ({Definition}) orpheline sur {Noeud} : {Raison}", o.IdInstance, o.CleDefinition, o.Noeud, o.Raison);
```

La résolution du suivi construit aussi le registre, donc une définition invalide est signalée dès le démarrage plutôt qu'au premier usage.

## 3. Définir un processus

### 3.1 La classe de définition

Une classe par processus. `Cle` est le code du type de processus, conservé avec chaque instance : ne le changez plus une fois des instances créées.

```csharp
public sealed class DemandeRemboursement : IDefinitionProcessus
{
    public const string CleDefinition = "DemRemb";

    public static readonly Variable<decimal> Montant = Variable.Decimal("montant");
    public static readonly Variable<string> Langue = Variable.Texte("langue");
    public static readonly Variable<string> NoteApprobateur = Variable.Texte("noteApprobateur");
    public static readonly Variable<bool> PaiementEmis = Variable.Booleen("paiementEmis");

    public string Cle => CleDefinition;

    public void Definir(IProcessusBuilder p) => p
        .Systeme<EnregistrerDemande>("enregistrer")
        .Systeme<EnvoyerCourriel>("accuser-reception", a => a
            .Valeur(EnvoyerCourriel.Modele, "ACCUSE_RECEPTION")
            .Variable(EnvoyerCourriel.Langue, Langue))
        .Decision<ExigeApprobation>("a-approuver", d => d
            .Valeur(ExigeApprobation.Seuil, 500m)
            .Si(true, "approbation")
            .Sinon("paiement"))
        .Interactif("approbation", "APPROB_REMB", t => t
            .Avant<PreparerDossier>()
            .Apres<ConsignerApprobation>()
            .Sur("refusee", "fin-refusee")
            .Sur("a-corriger", "enregistrer"))
        .SousProcessus("paiement", Paiement.CleDefinition, s => s
            .Sur("echec", "fin-refusee"))
        .AttenteSignal("attente-confirmation", "paiement-confirme", s => s
            .Signal("paiement-rejete", "fin-refusee"))
        .AttenteTemps("delai-contestation", TimeSpan.FromDays(30))
        .Fin("remboursee")

        .Fin("fin-refusee", "refusee");
}

public sealed class Paiement : IDefinitionProcessus
{
    public const string CleDefinition = "Paiement";
    public string Cle => CleDefinition;

    public void Definir(IProcessusBuilder p) => p
        .Systeme<EmettrePaiement>("emettre")
        .Decision("resultat", DemandeRemboursement.PaiementEmis, d => d
            .Si(true, "emis")
            .Sinon("echec"))
        .Fin("emis")
        .Fin("echec");
}
```

### 3.2 L'enchaînement des nœuds

- Le premier nœud déclaré est le départ.
- Un nœud mène au nœud suivant dans le texte. Seuls les embranchements et les retours en arrière s'écrivent.
- `.Aller("noeud")`, placé après un nœud, le fait mener à ce nœud plutôt qu'au suivant dans le texte.
- Une décision et une fin n'ont pas de suite implicite : les nœuds écrits après une fin ne sont atteints que par une branche, une transition ou un `Aller`.
- Les identifiants de nœuds sont conservés avec les instances. Pour en renommer un, voir la section 8.

### 3.3 Les types de nœuds

| Méthode | Rôle | L'instance s'arrête ? |
|---|---|---|
| `Systeme<TCommande>(id, parametres?)` | Exécute une commande, puis continue. | Non |
| `Decision<TRequete>(id, d => ...)` | Choisit une branche selon le résultat d'une requête. | Non |
| `Decision(id, variable, d => ...)` | Choisit une branche selon la valeur d'une variable. | Non |
| `Interactif(id, codeTache, t => ...)` | Crée une tâche dans le bassin et attend sa complétion. | Oui |
| `AttenteSignal(id, signal, s => ...)` | Attend un signal nommé. | Oui |
| `AttenteTemps(id, duree)` ou `AttenteTemps(id, variableDate)` | Attend une durée, ou la date portée par une variable. | Oui |
| `SousProcessus(id, cleDefinition, s => ...)` | Démarre une instance enfant d'une autre définition. | Seulement si l'enfant s'arrête |
| `Fin(id, nomFin?)` | Termine l'instance. Sans nom, la fin porte l'identifiant du nœud. | — |

**Décision.** `Si(valeur, noeud)` ajoute une branche, `Sinon(noeud)` est obligatoire. La valeur de `Si` doit être du type exact du résultat : pour un résultat `decimal`, écrivez `Si(500m, ...)` et non `Si(500, ...)`. Les branches sont comparées dans l'ordre par égalité.

**Nœud interactif.**

- `Avant<TCommande>()` s'exécute dans l'appel qui crée la tâche, juste avant sa création.
- `Apres<TCommande>()` s'exécute dans l'appel qui complète la tâche.
- `Sur(transition, noeud)` déclare une transition nommée. À la complétion, l'appelant choisit une transition ; sans transition, l'instance va au nœud suivant.
- `Echeance(duree, noeud)` ou `Echeance(variableDate, noeud)` déclare une escalade (voir plus bas).

**Attente d'un signal.**

- Le signal donné à `AttenteSignal` mène au nœud suivant.
- `Signal(autreSignal, noeud)` accepte un autre signal, qui mène à son propre nœud.
- `Sur(transition, noeud)` déclare une transition que l'émetteur du signal peut nommer pour choisir la destination.
- `Groupe(variable)` ou `Groupe("valeur")` abonne l'instance à un groupe de corrélation (voir plus bas).
- `Echeance(duree, noeud)` ou `Echeance(variableDate, noeud)` déclare une escalade (voir plus bas).

**Groupe de corrélation.** Une attente de signal peut s'abonner à un groupe : un seul envoi au groupe atteint alors toutes les instances abonnées (section 6.4).

```csharp
public static readonly Variable<string> GroupeBlocage = Variable.Texte("groupeBlocage");

.Systeme<TrouverBlocage>("trouver-blocage")                    // écrit GroupeBlocage
.Decision<CotisationEstBloquee>("cotisation-bloquee", d => d
    .Si(true, "attente-deblocage")
    .Sinon("cotiser"))
.AttenteSignal("attente-deblocage", "cotisation-debloquee", s => s
    .Groupe(GroupeBlocage)
    .Echeance(TimeSpan.FromDays(30), "cotisation-bloquee"))    // filet : revérifie le blocage
.Systeme<Cotiser>("cotiser")
```

- L'instance s'abonne en arrivant sur le nœud et se désabonne en le quittant, quelle que soit la sortie (signal, échéance, annulation). Elle peut s'abonner à d'autres groupes à d'autres nœuds.
- Avec une variable, celle-ci doit avoir été écrite avant l'arrivée sur le nœud : absente ou vide, le moteur lève `SentierException`.
- Le groupe est une valeur opaque ; donnez-lui un nom qui dit ce qui est attendu, par exemple `BLOCAGE-COTISATION:employeur-123`.
- Le signal ciblé par clé de corrélation atteint toujours ce nœud.
- **Le moteur ne mémorise pas les signaux.** Une instance qui arrive sur l'attente pendant que le signal de groupe est envoyé peut le manquer : sa transaction a lu « bloquée », l'envoi ne la voit pas encore, et elle attend ensuite un signal qui ne reviendra pas. Sérialisez les deux dans le domaine : la requête qui précède l'attente lit la ligne du blocage avec `FOR UPDATE`, et le déblocage modifie cette même ligne dans la transaction qui envoie le signal. L'échéance de l'exemple est un filet, pas la correction.

**Escalade.** `Echeance` sur un nœud interactif ou une attente de signal évite qu'une instance reste bloquée indéfiniment : le délai dépassé, l'instance va au nœud d'escalade.

```csharp
.Interactif("approbation", "APPROB", t => t
    .Echeance(TimeSpan.FromDays(10), "relance-gestionnaire"))
```

- L'échéance est calculée à l'arrivée sur le nœud. Avec une variable, celle-ci doit avoir été écrite avant : absente, le moteur lève `SentierException`.
- Rien ne se déclenche seul : c'est le traitement planifié des échéances qui fait avancer l'instance (section 6.5).
- Pour un nœud interactif, la tâche ouverte est annulée dans le bassin (`AnnulerTacheAsync`) et la commande `Apres` n'est pas exécutée.
- Un nœud quitté à temps n'a plus d'échéance.
- Dans l'historique, la sortie par escalade porte la transition `echeance`.
- Si une nouvelle version de la définition retire l'échéance du nœud, les instances déjà arrêtées dessus continuent d'attendre, sans échéance.

**Attente d'un délai.** L'échéance est calculée à l'arrivée sur le nœud. Avec une variable, celle-ci doit avoir été écrite avant : absente, le moteur lève `SentierException`. Le moteur ne déclenche rien seul (section 6.5).

**Sous-processus.**

- L'enfant reçoit une copie des variables du parent. À sa fin, ses variables sont reversées dans celles du parent.
- L'enfant partage la clé de corrélation et l'identifiant d'affaire du parent.
- `Sur(nomFin, noeud)` choisit le nœud de reprise du parent selon la fin atteinte par l'enfant. Sans correspondance, le parent va au nœud suivant.
- Si l'enfant n'a aucun point d'attente, il s'exécute en entier dans le même appel et le parent continue aussitôt.
- La définition appelée doit exister dans le registre, sinon celui-ci refuse de se construire.

### 3.4 Les variables

Déclarez chaque variable une seule fois, en constante statique sur la classe de la définition. Il n'y a ni modèle à écrire ni nom en chaîne dans le reste du code.

| Fabrique | Type |
|---|---|
| `Variable.Texte` | `string` |
| `Variable.Entier` | `int` |
| `Variable.Long` | `long` |
| `Variable.Booleen` | `bool` |
| `Variable.Decimal` | `decimal` |
| `Variable.Date` | `DateTime` |
| `Variable.De<T>` | tout type sérialisable en JSON |

Les variables entrent dans une instance de trois façons :

- par les `Donnees` d'un démarrage, d'une complétion de tâche ou d'un signal ;
- par une commande, avec `contexte.Variables.Ecrire(...)` ;
- par un sous-processus, à sa fin.

Une variable absente se lit à la valeur par défaut de son type. Le nom donné à la fabrique est celui conservé en base : ne le changez pas tant que des instances en cours le portent.

Gardez les variables pour ce qui sert au déroulement (décisions, paramètres, échéances). Les données d'affaires restent dans vos tables, retrouvées par `IdAffaire`.

### 3.5 La validation

La définition est validée à la construction du registre. `DefinitionInvalideException` liste tous les problèmes d'un coup dans `Erreurs` :

- aucun nœud, nœud sans identifiant, identifiant utilisé deux fois ;
- dernier nœud qui n'est pas une fin ;
- branche, transition ou `Aller` vers un nœud qui n'existe pas ;
- décision sans `Sinon`, ou valeur de `Si` d'un autre type que le résultat ;
- nœud inatteignable, ou nœud qui n'aboutit à aucune fin ;
- paramètre obligatoire d'une commande ou d'une requête non fourni ;
- signal, groupe de corrélation ou échéance déclaré deux fois sur un même nœud ;
- correspondance invalide ;
- sous-processus inconnu, ou deux définitions de même clé.

Un test unitaire suffit à garantir que toutes les définitions sont valides :

```csharp
[Fact]
public void Les_definitions_sont_valides()
    => _ = new RegistreDefinitions([new DemandeRemboursement(), new Paiement()]);
```

## 4. Écrire une commande

Une commande est une classe qui implémente `ICommande`. Ses dépendances arrivent par le constructeur, depuis le conteneur.

```csharp
public sealed class EnvoyerCourriel(IServiceCourriel courriels) : ICommande
{
    public static readonly Parametre<string> Modele = Parametre.Texte("modele");
    public static readonly Parametre<string> Langue = Parametre.Texte("langue").Facultatif();

    public Task ExecuterAsync(ContexteCommande contexte, CancellationToken cancellationToken)
        => courriels.EnvoyerAsync(
            contexte.IdAffaire,
            contexte.Parametres.Lire(Modele)!,
            contexte.Parametres.Lire(Langue) ?? "fr",
            cancellationToken);
}
```

### Le contexte

| Propriété | Contenu |
|---|---|
| `IdInstance` | L'instance qui exécute le nœud (l'enfant, dans un sous-processus). |
| `IdAffaire`, `CleCorrelation` | Ceux donnés au démarrage. |
| `CleDefinition`, `Noeud` | La définition et le nœud en cours. |
| `Acteur` | L'acteur de l'appel au moteur qui a mené à ce nœud. |
| `Variables` | Les variables de l'instance, en lecture et en écriture. |
| `Parametres` | Les paramètres fixés dans la définition pour cet appel. |

### Les paramètres

Un paramètre se déclare une seule fois, en constante statique sur la classe du handler, avec les mêmes fabriques que les variables (`Parametre.Texte`, `Parametre.Decimal`, `Parametre.De<T>`, etc.).

- Un paramètre est obligatoire par défaut : une définition qui appelle la commande sans le fournir est refusée à la validation.
- `.Facultatif()` le rend facultatif : absent, il se lit à la valeur par défaut du type.

La définition fournit la valeur de deux façons :

```csharp
.Systeme<EnvoyerCourriel>("accuser-reception", a => a
    .Valeur(EnvoyerCourriel.Modele, "ACCUSE_RECEPTION")            // valeur fixe
    .Variable(EnvoyerCourriel.Langue, DemandeRemboursement.Langue)) // lue de l'instance au moment de l'appel
```

Le compilateur vérifie que le type de la valeur ou de la variable est celui du paramètre. La même commande peut ainsi servir à plusieurs nœuds et à plusieurs processus avec des paramètres différents.

### Écrire dans les variables

```csharp
public sealed class EmettrePaiement(IServicePaiements paiements) : ICommande
{
    public async Task ExecuterAsync(ContexteCommande contexte, CancellationToken cancellationToken)
    {
        var emis = await paiements.EmettreAsync(contexte.IdAffaire, contexte.Variables.Lire(DemandeRemboursement.Montant), cancellationToken);
        contexte.Variables.Ecrire(DemandeRemboursement.PaiementEmis, emis);
    }
}
```

Les variables écrites sont enregistrées avec l'instance au prochain point d'attente ou à la fin.

### Règles

- Une commande s'exécute dans la transaction de l'appel au moteur. Une exception la fait échouer et, si le client ne complète pas son scope, rien n'est conservé.
- N'ouvrez pas de transaction dans une commande et n'y faites pas d'effet non transactionnel irréversible sans précaution : la transaction peut encore être annulée après.
- Une commande n'appelle pas le moteur. Pour avancer, elle écrit des variables que la définition utilise ensuite.

## 5. Écrire une requête

Une requête implémente `IRequete<TResultat>`. Elle lit et retourne une valeur, sans rien modifier ; son résultat choisit la branche d'un nœud `Decision`.

```csharp
public sealed class ExigeApprobation : IRequete<bool>
{
    public static readonly Parametre<decimal> Seuil = Parametre.Decimal("seuil");

    public Task<bool> ExecuterAsync(ContexteRequete contexte, CancellationToken cancellationToken)
        => Task.FromResult(contexte.Variables.Lire(DemandeRemboursement.Montant) > contexte.Parametres.Lire(Seuil));
}
```

Dans la définition, les paramètres et les branches se déclarent ensemble :

```csharp
.Decision<ExigeApprobation>("a-approuver", d => d
    .Valeur(ExigeApprobation.Seuil, 500m)
    .Si(true, "approbation")
    .Sinon("paiement"))
```

Le résultat peut être de n'importe quel type comparable par égalité : `bool`, `string`, `int`, une énumération. Avec plusieurs valeurs :

```csharp
.Decision<DeterminerCircuit>("circuit", d => d
    .Si(Circuit.Rapide, "paiement")
    .Si(Circuit.Complet, "approbation")
    .Sinon("fin-refusee"))
```

Quand la valeur est déjà dans une variable, la requête est inutile : utilisez `Decision(id, variable, d => ...)`.

Le contexte d'une requête a les mêmes propriétés que celui d'une commande.

## 6. Piloter les instances

L'application appelle `IMoteurSentier`, toujours dans un `TransactionScope` (section 2.6). Chaque appel reçoit un `acteur`, transmis aux commandes, au bassin, aux écouteurs et à l'historique.

### 6.1 Démarrer

```csharp
var etat = await moteur.DemarrerAsync(
    new DemandeDemarrage(DemandeRemboursement.CleDefinition, IdAffaire: 42, CleCorrelation: "REMB-42", Acteur: "alice")
    {
        Donnees = new Donnees()
            .Avec(DemandeRemboursement.Montant, 750m)
            .Avec(DemandeRemboursement.Langue, "fr"),
        AssignerPremiereTache = true,
    }, ct);
```

- `IdAffaire` est l'identifiant de l'objet d'affaires, remis à chaque commande et au bassin.
- `CleCorrelation` sert ensuite à signaler, annuler et consulter. Une clé déjà utilisée par une instance de tête lève `CleCorrelationDejaUtiliseeException`.
- `AssignerPremiereTache` est transmis au bassin pour les tâches créées par cet appel.

### 6.2 Lire l'état

Chaque appel retourne un `EtatInstance` ; `ObtenirEtatAsync(cleCorrelation)` le donne à tout moment (ou `null` si la clé est inconnue).

| Propriété | Contenu |
|---|---|
| `IdInstance` | L'instance de tête : le numéro de processus. |
| `Statut`, `EnCours`, `NomFin` | `EnCours`, `Terminee` ou `Annulee`, et le nom de la fin atteinte. |
| `IdInstanceActive`, `CleDefinitionActive` | L'instance qui porte le nœud actif : la tête ou un sous-processus. |
| `NoeudCourant`, `TypeAttente` | Où l'instance est arrêtée, et sur quoi (`Tache`, `Signal`, `Temps`). |
| `CodeTache`, `RefTache` | La tâche attendue. |
| `DateEcheance` | L'échéance attendue : attente de temps, ou escalade d'une tâche ou d'une attente de signal. |
| `GroupeSignal` | Le groupe de corrélation auquel l'instance est abonnée. |
| `SignauxAttendus` | Les signaux acceptés par le nœud. |
| `Variables` | Une copie des variables de l'instance active. |

### 6.3 Compléter une tâche

Quand l'utilisateur termine sa tâche dans le bassin, l'application en avise le moteur avec la référence de la tâche :

```csharp
// suite normale
await moteur.CompleterTacheAsync(refTache, "bob", cancellationToken: ct);

// transition nommée, avec des données versées dans les variables
await moteur.CompleterTacheAsync(refTache, "bob", "refusee",
    new Donnees().Avec(DemandeRemboursement.NoteApprobateur, "Pièce manquante"), ct);
```

- Les données sont versées dans les variables avant la commande `Apres`.
- `TacheIntrouvableException` si aucune instance en cours n'attend cette tâche : compléter deux fois la même tâche ne fait pas avancer deux fois.
- `TransitionInconnueException` si la transition n'est pas déclarée sur le nœud.

### 6.4 Envoyer un signal

```csharp
// à une instance précise
var resultat = await moteur.SignalerAsync("REMB-42", "paiement-confirme", "systeme-bancaire", cancellationToken: ct);
if (!resultat.Traitee) { /* rien n'attendait ce signal */ }

// à toutes les instances qui l'attendent
var etats = await moteur.DiffuserSignalAsync("paiement-rejete", "systeme-bancaire", cancellationToken: ct);

// aux instances abonnées à un groupe de corrélation
var debloquees = await moteur.SignalerGroupeAsync("BLOCAGE-COTISATION:employeur-123", "cotisation-debloquee", "agent", cancellationToken: ct);
```

- Un signal que rien n'attend n'est pas une erreur : `Traitee` vaut `false` et l'instance ne bouge pas. Le signal n'est pas mis en file ; il est perdu.
- `cleDefinition` limite la remise aux instances de cette définition.
- `transition` et `donnees` s'utilisent comme pour une tâche.
- `SignalerGroupeAsync` ne lit que les instances abonnées au groupe, et retourne l'état de celles qui ont avancé. Une instance abonnée qui n'attend pas ce signal est ignorée.
- Un signal diffusé ou de groupe fait avancer toutes les instances dans la transaction de l'appel : si un handler échoue pour l'une d'elles, rien n'est validé pour aucune.

### 6.5 Déclencher les échéances

Le moteur n'a pas de minuterie. Un traitement planifié du client liste les échéances dues et les déclenche une à une, chacune dans sa transaction :

```csharp
foreach (var echeance in await moteur.ListerEcheancesDuesAsync(DateTime.Now, ct))
{
    using var portee = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
    await moteur.DeclencherEcheanceAsync(echeance.IdInstance, "planificateur", ct);
    portee.Complete();
}
```

La liste contient les attentes de temps et les escalades (section 3.3) ; l'appelant n'a pas à les distinguer.

`DeclencherEcheanceAsync` est sans effet (`Traitee` à `false`) si l'échéance n'est pas due ou a déjà été déclenchée : le traitement peut tourner en plusieurs exemplaires ou être relancé.

### 6.6 Annuler

```csharp
await moteur.AnnulerAsync("REMB-42", "chef", ct);
```

L'annulation touche tout l'arbre : l'instance de tête et ses sous-processus en cours passent à `Annulee`, la tâche ouverte est annulée dans le bassin, et les écouteurs sont avisés pour chaque instance. Si l'historique est actif, chaque instance annulée y reçoit un événement `ANNULATION` sur son nœud courant.

### 6.7 Appels simultanés

Chaque instance porte un numéro de version. Si deux appels modifient la même instance en même temps, le second lève `ConflitConcurrenceException` : laissez la transaction s'annuler, puis relisez l'état avant de décider de recommencer.

## 7. Réagir à la fin d'un processus

Un `IEcouteurFinProcessus` est avisé dans la transaction qui termine ou annule une instance. Le module Autofac découvre les écouteurs par balayage.

```csharp
public sealed class CloreDemande(IDepotDemandes demandes) : IEcouteurFinProcessus
{
    public Task SurFinAsync(FinProcessus fin, CancellationToken cancellationToken)
    {
        if (fin.EstSousProcessus || fin.CleDefinition != DemandeRemboursement.CleDefinition) return Task.CompletedTask;

        return fin.Statut == StatutInstance.Annulee
            ? demandes.MarquerAnnuleeAsync(fin.IdAffaire, cancellationToken)
            : demandes.MarquerCloseAsync(fin.IdAffaire, fin.NomFin!, cancellationToken);
    }
}
```

Tous les écouteurs reçoivent toutes les fins, y compris celles des sous-processus : filtrez sur `EstSousProcessus` et `CleDefinition`.

## 8. Faire évoluer une définition

Une définition est du code : une nouvelle version de l'application s'applique aux instances déjà en cours, qui reprennent là où elles étaient arrêtées. Les nœuds où aucune instance ne s'arrête (systémiques, décisions) se modifient librement.

Pour retirer ou renommer un point d'attente, déclarez une correspondance. Les instances arrêtées sur l'ancien nœud sont replacées sur le nouveau :

```csharp
public void Definir(IProcessusBuilder p) => p
    // ...
    .Interactif("controle", "APPROB_REMB")      // s'appelait « approbation »
    // ...
    .Correspondance("approbation", "controle");
```

- Le nouveau nœud doit être un point d'attente de même nature que l'ancien (une tâche pour une tâche, un signal pour un signal).
- L'ancien identifiant ne doit plus être un nœud de la définition.
- Les correspondances s'enchaînent : `a` vers `b`, puis `b` vers `c`.
- Sans correspondance, l'instance lève `NoeudInconnuException` au prochain appel. `ISuiviSentier.VerifierInstancesAsync` la signale avant, et `MigrerInstancesAsync` applique les correspondances en base (section 9).

Ne changez ni la clé d'une définition ni le nom d'une variable tant que des instances en cours les portent.

## 9. Suivre et exploiter

`ISuiviSentier` regroupe les outils de suivi et de support : vérification, migration, repositionnement, historique et diagrammes. Elle est séparée de `IMoteurSentier` et enregistrée par scope comme lui. Le moteur n'appelle jamais ces opérations : l'application les exécute quand elle le décide.

### 9.1 Vérifier les instances

```csharp
var orphelines = await suivi.VerifierInstancesAsync(ct);
```

Une instance en cours est orpheline, avec la raison dans `Raison`, si :

- sa définition n'existe plus ;
- son nœud courant n'existe plus et n'a pas de correspondance ;
- le nœud où elle se résout n'est pas de la même nature d'attente (elle attend une tâche, le nœud attend un signal).

L'appel ne modifie rien et ne lit pas les variables des instances saines : il regarde les points d'arrêt distincts, puis ne charge que les orphelines.

### 9.2 Migrer les instances

Une correspondance (section 8) replace une instance à sa prochaine sollicitation, et doit donc rester dans le code tant qu'une instance est arrêtée sur l'ancien nœud. `MigrerInstancesAsync` applique les correspondances en base tout de suite :

```csharp
using var portee = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
var resultat = await suivi.MigrerInstancesAsync(ct);
portee.Complete();

foreach (var m in resultat.Migrations)
    journal.LogInformation("{Definition} : {Nombre} instance(s) de {Ancien} vers {Nouveau}", m.CleDefinition, m.NombreInstances, m.AncienNoeud, m.NouveauNoeud);
```

- Seul le nœud courant change ; la tâche, l'échéance, le groupe et les variables restent tels quels.
- Les orphelines ne sont pas touchées ; elles sont retournées dans `Orphelines`.
- Relancer la migration ne fait rien de plus.
- Quand `Orphelines` est vide après la migration, les correspondances peuvent être retirées du code à la livraison suivante.
- La migration n'écrit rien dans l'historique.

### 9.3 Repositionner une instance

Opération de support, pour une instance coincée : elle la replace sur un nœud de sa définition et l'exécute à partir de là, jusqu'au prochain point d'attente ou à la fin.

```csharp
using var portee = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
var etat = await suivi.RepositionnerAsync("REMB-42", "approbation", "support", cancellationToken: ct);
portee.Complete();
```

- L'instance visée est l'instance active de l'arbre : la tête, ou le sous-processus en cours. Le nœud est cherché dans la définition de cette instance ; inconnu, l'appel lève `NoeudInconnuException`.
- Le nœud visé est exécuté comme à une arrivée normale : une commande s'exécute, un nœud interactif crée une nouvelle tâche, une fin termine l'instance (et le parent reprend, pour un sous-processus).
- La tâche ouverte est annulée dans le bassin, sans sa commande `Apres`. L'échéance et l'abonnement au groupe en cours sont abandonnés.
- `donnees` corrige des variables avant la reprise, par exemple celle qui a mené l'instance au mauvais endroit.
- L'opération fonctionne pour une instance orpheline (section 9.1) : c'est le moyen de la sortir d'un nœud retiré sans correspondance.
- `SentierException` si aucune instance n'est en cours pour cette clé (inconnue, terminée ou annulée).
- Dans l'historique, la sortie du nœud quitté porte la transition `repositionnement` et l'acteur de l'appel.

Aucun contrôle n'empêche de sauter des étapes : réservez cette opération au support.

### 9.4 Lire l'historique

```csharp
foreach (var e in await suivi.LireHistoriqueAsync("REMB-42", ct))
    Console.WriteLine($"{e.Date:g} {e.CleDefinition}/{e.Noeud} {e.Evenement} {e.Transition} ({e.Acteur})");
```

La liste couvre tout l'arbre de la clé de corrélation, sous-processus compris, dans l'ordre des événements. `Evenement` vaut `ENTREE`, `SORTIE` ou `ANNULATION` ; `Transition` porte la branche, le signal ou la transition empruntée à la sortie, `echeance` pour une escalade et `repositionnement` pour un repositionnement. La liste est vide si `HistoriqueActif` était à `false` ou si la clé est inconnue.

### 9.5 Diagrammes Mermaid

```csharp
foreach (var cle in suivi.ClesDefinitions)
    File.WriteAllText($"{cle}.mmd", suivi.DiagrammeDefinition(cle));

string? diagramme = await suivi.DiagrammeInstanceAsync("REMB-42", ct);   // null si la clé est inconnue
```

`DiagrammeDefinition` retourne un `flowchart` Mermaid de la définition :

| Nœud | Forme | Détail affiché |
|---|---|---|
| Systémique | Rectangle | La commande |
| Décision | Losange | La requête |
| Interactif | Parallélogramme | Le code de tâche |
| Attente de signal | Stade | « signal » ou « signal de groupe » |
| Attente de temps | Hexagone | La durée ou la variable de date |
| Sous-processus | Rectangle à doubles bords | La définition appelée |
| Fin | Double cercle | Le nom de la fin, s'il diffère de l'identifiant |

Les flèches portent la valeur d'une branche, le signal ou la transition ; une escalade est en pointillé.

`DiagrammeInstanceAsync` dessine l'arbre d'une clé de corrélation : un bloc pour l'instance de tête, puis un par sous-processus en cours. Le nœud courant de chaque instance est en évidence. Si l'historique est actif, les nœuds déjà traversés sont colorés ; sinon seul le nœud courant l'est. Pour une instance terminée ou annulée, seule la tête est dessinée.

## 10. Tester

### Une commande ou une requête seule

Un handler se teste sans moteur : construisez le contexte à la main.

```csharp
var contexte = new ContexteRequete
{
    IdInstance = 1, IdAffaire = 42, CleCorrelation = "REMB-42",
    CleDefinition = DemandeRemboursement.CleDefinition, Noeud = "a-approuver", Acteur = "test",
    Variables = new Variables().Ecrire(DemandeRemboursement.Montant, 750m),
    Parametres = new Parametres().Avec(ExigeApprobation.Seuil, 500m),
};

Assert.True(await new ExigeApprobation().ExecuterAsync(contexte, CancellationToken.None));
```

### Un processus complet

Le moteur tourne tel quel sur SQLite en mémoire, avec `Sentier.Dapper` et un bassin de test :

```csharp
using var connexion = new SQLiteConnection("Data Source=:memory:");
connexion.Open();

var options = new OptionsSentier { Prefixe = "AT", Suffixe = "TAXE" };
var bd = new AccesBDDapper(connexion, DialecteSentier.Sqlite);
foreach (var script in SchemaSentier.Creation(DialecteSentier.Sqlite, options))
    await bd.ExecuteNonQueryAsync(script);

var moteur = new MoteurSentier(
    new RegistreDefinitions([new DemandeRemboursement(), new Paiement()]),
    bd, resolveur, bassinDeTest, [], options, horlogeDeTest);

var etat = await moteur.DemarrerAsync(new DemandeDemarrage(DemandeRemboursement.CleDefinition, 42, "REMB-42", "alice")
{
    Donnees = new Donnees().Avec(DemandeRemboursement.Montant, 750m),
});

Assert.Equal("approbation", etat.NoeudCourant);
```

`tests/Sentier.Tests/Banc.cs` et `tests/Sentier.Tests/Doublures/` montrent un banc complet : bassin en mémoire, écouteur, résolveur et horloge réglable (un `TimeProvider`) pour faire passer les échéances.

## 11. Exceptions

Toutes dérivent de `SentierException`.

| Exception | Cause |
|---|---|
| `DefinitionInvalideException` | La définition est refusée à la construction ; `Erreurs` liste les problèmes. |
| `DefinitionInconnueException` | Aucune définition n'a cette clé. |
| `CleCorrelationDejaUtiliseeException` | Une instance de tête existe déjà avec cette clé de corrélation. |
| `TacheIntrouvableException` | Aucune instance en cours n'attend cette tâche. |
| `TransitionInconnueException` | La transition n'est pas déclarée sur le nœud. |
| `NoeudInconnuException` | L'instance est arrêtée sur un nœud retiré, sans correspondance. |
| `ConflitConcurrenceException` | L'instance a été modifiée par un autre appel. |
| `BoucleSansFinException` | Plus de `NombreMaxEtapes` nœuds exécutés dans un même appel. |
