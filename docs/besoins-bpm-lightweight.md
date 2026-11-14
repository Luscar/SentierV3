# Besoins — BPM lightweight (package NuGet)

> Statut : ébauche. Les éléments marqués **[Proposition]** ne viennent pas du besoin initial ; ils sont à valider ou à rejeter.

## 1. Vision

Un moteur de processus minimaliste, livré en package NuGet, qui s'embarque dans l'application cliente (pas de serveur, pas de service d'arrière-plan imposé). Le moteur orchestre ; toute la logique d'affaires reste côté client, derrière des interfaces d'abstraction.

Principe directeur : à chaque sollicitation (démarrage, fin de tâche, signal, échéance), le moteur exécute une suite d'étapes **dans une seule transaction, celle du client**, jusqu'à atteindre un point d'attente ou la fin du processus.

## 2. Transaction

- **B-TX-1** Le client fournit au moteur une `IDbConnection` déjà ouverte : c'est la seule poignée d'infrastructure reçue. Il n'y a pas d'objet transaction à passer.
- **B-TX-2** La transaction est ambiante : l'API du client ouvre un `TransactionScope`, et la connexion y participe. Le moteur n'ouvre et ne ferme jamais la connexion, ne crée pas de `TransactionScope` et ne valide ni n'annule rien.
- **B-TX-8** Le moteur exécute ses commandes sur cette connexion sans objet transaction, comme la couche d'accès du client (au-dessus de Dapper). La garantie « tout ou rien » repose sur le `TransactionScope` du client.
- **B-TX-11** Décision : le moteur ne manipule pas la `IDbConnection` directement. Il passe par un port, `IAccesBDSentier`, dont les quatre méthodes (`ExecuteNonQueryAsync`, `QueryAsync`, `QuerySingleOrDefaultAsync`, `GetSequenceAsync`) ont la signature de celles de la couche d'accès du client (`IAccesBDPlus`, au-dessus de Dapper). Le client fournit un adaptateur de pure délégation ; le package `Sentier.Dapper` fournit une implémentation sur une `IDbConnection` pour qui n'a pas de couche d'accès. `GetSequenceAsync` porte la génération d'identifiant (B-PER-5).
- **B-TX-9** Le moteur étant asynchrone, le `TransactionScope` du client doit être créé avec le flux asynchrone activé (`TransactionScopeAsyncFlowOption.Enabled`). À documenter comme prérequis.
- **B-TX-10** L'application cliente fonctionne déjà de cette façon (commandes sans objet transaction), sous Oracle comme sous SQLite ; le package s'aligne sur ce fonctionnement.

## 3. Définition de processus (builder fluent)

- **B-DEF-1** Une définition se construit en code avec un builder fluent, sans XML ni BPMN ni concepteur graphique.
- **B-DEF-8** **Exigence primordiale : un builder facile à lire et à modifier.** Quelqu'un qui ne connaît pas le moteur doit comprendre le processus en lisant la définition. Concrètement :
  - la définition se lit de haut en bas, dans l'ordre du déroulement normal ;
  - l'enchaînement est implicite : un nœud mène au suivant dans le texte, sans transition à écrire ; seuls les embranchements et les retours en arrière sont explicites ;
  - chaque nœud tient sur une ligne, ou sur un petit bloc pour un nœud interactif ou décisionnel ;
  - les commandes et les requêtes sont des types, pas des chaînes, pour profiter du compilateur et de la navigation dans l'IDE ;
  - une définition par classe, nommée d'après le processus ;
  - ajouter, retirer ou déplacer une étape ne demande de toucher qu'à l'endroit concerné.
- **B-DEF-9** Les sous-processus sont le moyen principal de garder une définition courte : un bloc d'étapes cohérent ou réutilisé devient une définition à part, appelée par une seule ligne.
- **B-DEF-2** Une définition est immuable une fois construite.
- **B-DEF-3** **[Proposition]** La définition est validée à la construction : nœud de départ unique, nœuds inatteignables, transitions vers un nœud inexistant, décision sans branche par défaut, identifiants en double, chemin qui n'aboutit à aucune fin.
- **B-DEF-4** Décision : les définitions vivent uniquement en code ; elles ne sont pas enregistrées en base. Une instance n'est **pas** liée à une version : elle ne conserve que la clé de sa définition, son nœud courant et ses données. Quand le code change, toutes les instances en cours suivent la nouvelle définition à leur prochaine sollicitation (comportement attendu par les utilisateurs, comme avec RHPAM aujourd'hui).
- **B-DEF-5** Conséquence : l'identifiant d'un nœud est un contrat. Le renommer ou le retirer touche les instances qui y sont arrêtées.
- **B-DEF-6** Migration des instances en cours : quand un nœud est retiré ou renommé, la définition déclare une correspondance « ancien nœud → nouveau nœud ». Une instance arrêtée sur l'ancien nœud est replacée sur le nouveau à sa prochaine sollicitation.
- **B-DEF-7** Décision : une interface de suivi (`ISuiviSentier`), séparée des points d'entrée du moteur, regroupe ce qui ne fait avancer aucune instance. Le moteur ne l'appelle jamais ; le client l'exécute quand une définition change. Elle offre :
  - la vérification des instances en cours que le code livré ne sait plus faire avancer (définition ou nœud retiré sans correspondance, nœud devenu d'une autre nature d'attente) ;
  - la migration : les correspondances (B-DEF-6) sont appliquées en base aux instances en cours, ce qui permet de les retirer ensuite du code ;
  - le diagramme Mermaid d'une définition, et celui d'un arbre d'instances avec le nœud courant et, si l'historique est actif, les nœuds traversés.

Esquisse d'API, à titre indicatif seulement :

```csharp
public class DeclarationOriginale : IDefinitionProcessus
{
    public static readonly Variable<string> Langue = Variable.Texte("langue");   // variable déclarée une fois, typée

    public void Definir(IProcessusBuilder p) => p
        .Systeme<ValiderDeclaration>("valider")                       // sans paramètre : l'id d'affaires suffit
        .Systeme<EnvoyerAvis>("notifier", p => p                      // avec paramètres
            .Valeur(EnvoyerAvis.Modele, "AVIS_RECEPTION")             //   valeur statique
            .Variable(EnvoyerAvis.Langue, Langue))                    //   variable de l'instance
        .SousProcessus("identification", TypeProcessus.IDENTIFICATION)
        .Decision<DoitEtreVerifiee>("a-verifier", d => d
            .Si(true, "verification")
            .Sinon("comptabilisation"))
        .Interactif("verification", "VERIF", t => t
            .Avant<PreparerVerification>()
            .Apres<ConsignerVerification>()
            .Sur("refusee", "valider")           // retour en arrière
            .Sur("doublon", "fin-doublon"))
        .SousProcessus("comptabilisation", TypeProcessus.COMPTABILISATION)
        .AttenteSignal("attente-paiement", "paiement-recu")
        .AttenteTemps("delai-grace", TimeSpan.FromDays(2))
        .Fin("cotisee")

        .Fin("fin-doublon", "doublon");          // autre issue, atteinte par embranchement
}
```

## 4. Types de nœuds

| Nœud | Comportement | Point d'attente |
|---|---|---|
| Systémique | Lance une commande traitée côté client, puis continue. | Non |
| Décisionnel | Lance une requête côté client ; le résultat choisit la branche. | Non |
| Interactif | Action pré-interactive, création d'une tâche dans le bassin, attente, puis action post-interactive à la complétion. | Oui |
| Attente de temps | Suspend l'instance jusqu'à une échéance (durée ou date). | Oui |
| Attente de signal | Suspend l'instance jusqu'à la réception d'un signal nommé. | Oui |
| Sous-processus | Démarre une instance enfant d'une autre définition ; le parent reprend à la fin de l'enfant. | Si l'enfant attend |

Précisions :

- **B-N-7** Un nœud interactif porte un code de tâche, transmis au bassin à la création de la tâche.
- **B-N-8** Un nœud décisionnel choisit sa branche soit sur le résultat d'une requête traitée côté client (cas le plus fréquent), soit sur la valeur d'une variable de l'instance.
- **B-N-9** Fins multiples et nommées : un processus peut se terminer à plusieurs endroits, et chaque fin porte un nom qui décrit l'issue (par exemple « déclaration cotisée », « déclaration annulée », « doublon »).
  - Le nom de la fin est conservé avec l'instance et transmis au signalement de fin de processus (B-ABS-2).
  - **[Proposition]** Dans un processus parent, le nom de la fin d'un sous-processus peut choisir la transition sortante du nœud sous-processus, comme le fait le résultat d'une tâche.
- **B-N-1** Nœud interactif : l'action **pré** s'exécute dans la transaction qui crée la tâche ; l'action **post** s'exécute dans la transaction qui signale la complétion de la tâche. Les deux sont facultatives.
- **B-N-2** Transitions nommées : un nœud d'attente (interactif ou signal) peut avoir plusieurs transitions sortantes nommées. L'appelant choisit la transition par son nom en complétant la tâche ou en envoyant le signal ; il ne désigne jamais un nœud. Remplace le `codeProchaineTache` de `AvancerATacheSuivanteAsync`.
- **B-N-3** Sous-processus : l'enfant s'exécute dans la même transaction que le parent tant qu'il n'atteint pas de point d'attente.
- **B-N-4** **[Proposition]** Passage de données entre parent et enfant : entrées à l'appel, sorties au retour.
- **B-N-5** Décision : échéance sur un nœud interactif ou une attente de signal, pour éviter les instances bloquées indéfiniment. Le nœud déclare un délai (durée, ou date portée par une variable) et un nœud d'escalade. Le délai dépassé, le déclenchement de l'échéance (B-API-4) envoie l'instance au nœud d'escalade ; pour un nœud interactif, la tâche ouverte est annulée dans le bassin et l'action **post** n'est pas exécutée. Un nœud quitté à temps n'a plus d'échéance.
- **B-N-10** Décision : abonnement à un groupe de corrélation. Une attente de signal peut déclarer un groupe (valeur fixe, ou valeur d'une variable). L'instance s'y abonne en arrivant sur le nœud et s'en désabonne en le quittant, quelle que soit la sortie. Un signal envoyé au groupe (B-API-3) atteint toutes les instances abonnées. Exemple : les instances arrêtées sur « blocage de cotisation » s'abonnent au groupe du blocage, et le déblocage dans le domaine leur envoie un seul signal. Une instance peut s'abonner à des groupes différents à des nœuds différents.
- **B-N-6** **[Proposition]** Garde contre les boucles sans fin : nombre maximal d'étapes par appel, configurable.

## 5. Abstractions côté client

Le moteur ne connaît que des interfaces ; le client fournit les implémentations.

- **B-ABS-1** **Bassin de tâches** : adaptateur vers la couche existante. Au minimum : créer une tâche pour un nœud interactif, annuler une tâche ouverte. Le bassin reste propriétaire de l'assignation, des files et de l'affichage.
- **B-ABS-2** **Signalement de fin de processus** : le client est avisé quand une instance se termine (complétée, annulée), dans la même transaction.
- **B-ABS-3** **Commandes** : les nœuds systémiques et les actions pré/post lancent des commandes traitées par des handlers implémentés côté client.
- **B-ABS-4** **Requêtes** : les nœuds décisionnels s'appuient sur des requêtes traitées côté client.
- **B-ABS-9** **Paramètres des commandes et des requêtes.** Une commande ou une requête peut recevoir des paramètres, fixés dans la définition, provenant :
  - d'une valeur statique ;
  - d'une variable de l'instance.
- **B-ABS-10** **Contexte toujours transmis.** Chaque handler reçoit, en plus des paramètres, un contexte fourni par le moteur. L'identifiant d'affaires y figure toujours ; s'y ajoutent la clé de corrélation, l'acteur, le type de processus et l'accès aux variables. Une commande qui n'a besoin que de l'identifiant d'affaires ne déclare donc aucun paramètre.
- **B-ABS-11** Décision : ni modèle, ni chaîne magique. Il n'y a pas d'objet commande ou requête à écrire, et aucun nom de paramètre ou de variable n'est répété en chaîne entre la définition et le handler.
  - Un paramètre est une clé typée, déclarée une seule fois en constante sur la classe du handler (`Parametre<T>`), sur le même principe que les variables (B-ET-8). La définition et le handler utilisent cette constante.
  - Dans la définition : `Valeur(parametre, valeur)` (statique) et `Variable(parametre, variable)` (variable de l'instance). Le compilateur vérifie que la valeur ou la variable est du type du paramètre.
  - Dans le handler : `contexte.Parametres.Lire(parametre)`, déjà du bon type.
  - Un handler qui a seulement besoin d'une variable de l'instance la lit directement par sa constante (`contexte.Variables.Lire(variable)`), sans paramètre. Les paramètres servent à ce qui change d'un appel à l'autre de la même commande.
  - À la construction, la définition est refusée si un paramètre déclaré obligatoire par le handler n'est pas fourni par l'appel.
  - Non repris de V2 : les paramètres nommés par chaîne (`Valeur("nom", …)`, `Parametres.Lire<T>("nom")`) et la liaison aux propriétés d'une classe (`CommandeSentier<TParametres>`).
- **B-ABS-8** La classe désignée dans la définition est le handler lui-même : elle implémente une interface asynchrone du package (du genre `ICommande`, et `IRequete<TResultat>` pour une décision), reçoit le contexte et peut injecter ses propres services. Le package ne dépend pas de MediatR, pour rester utilisable par d'autres applications ; un client qui le souhaite peut écrire un handler qui relaie vers MediatR.

Esquisse d'un handler, à titre indicatif seulement :

```csharp
public class EnvoyerAvis(IServiceAvis avis) : ICommande
{
    public static readonly Parametre<string> Modele = Parametre.Texte("modele");
    public static readonly Parametre<string> Langue = Parametre.Texte("langue");

    public Task ExecuterAsync(ContexteCommande c, CancellationToken ct)
        => avis.EnvoyerAsync(c.IdAffaire, c.Parametres.Lire(Modele), c.Parametres.Lire(Langue), ct);
}
```

- **B-ABS-5** **Échéances** : le moteur n'a ni minuterie ni fil d'arrière-plan. Il enregistre la date d'échéance avec l'instance ; c'est un composant externe au package qui réveille le moteur par l'interface de service (B-API-4).
- **B-ABS-6** **[Proposition]** **Signalement des autres événements du cycle de vie** (instance démarrée, nœud entré/sorti, instance suspendue), utile pour l'audit et le suivi sans alourdir le moteur.
- **B-ABS-7** **Injection de dépendances** : l'application cliente utilise Autofac. Elle ouvre un scope et appelle le moteur à travers une couche de service enregistrée dans ses propres injections. L'enregistrement du package doit tenir en une ligne.
  - Le cœur du package ne dépend d'aucun conteneur.
  - **[Proposition]** Un package compagnon fournit un module Autofac (`builder.RegisterModule(...)`) qui enregistre le moteur et découvre, par balayage d'assemblies, les définitions de processus et les handlers.
  - Le moteur et les handlers sont résolus dans le scope courant de l'application ; ils partagent donc ses services (dépôts, contexte utilisateur).

## 6. Points d'entrée du moteur

Le moteur obtient la `IDbConnection` du client (B-TX-1) ; ses opérations sont exposées par une interface de service.

- **B-API-1** Démarrer une instance : définition, identifiant d'affaires, clé de corrélation fournie par le client, variables initiales, acteur.
- **B-API-2** Compléter une tâche interactive, avec le nom de la transition choisie et des données.
- **B-API-3** Envoyer un signal, de deux façons :
  - **ciblé** : à l'instance désignée par une clé de corrélation ;
  - **diffusé** : à toutes les instances en attente de ce signal, avec un filtre facultatif par définition ;
  - **de groupe** : aux instances abonnées à un groupe de corrélation (B-N-10), avec le même filtre facultatif.
- **B-API-7** Un signal peut porter le nom d'une transition et des données qui initialisent ou modifient des variables de l'instance.
- **B-API-9** Chaque opération qui fait avancer une instance (démarrage, complétion, signal, échéance) retourne l'état atteint : nœud courant, type d'attente, et pour un nœud interactif le code et l'identifiant de la tâche créée. L'appelant n'a jamais à interroger en boucle.
- **B-API-10** L'état d'une instance (B-API-6) inclut les signaux qu'elle attend.
- **B-API-8** Signal conditionnel : si rien n'attend ce signal, l'appel ne lève pas d'erreur et le résultat l'indique.
- **B-API-4** Échéances : le moteur expose la liste des échéances dues à un instant donné (attentes de temps et escalades, B-N-5), et l'opération qui déclenche une échéance. Un composant externe au package appelle ces deux opérations.
- **B-API-5** **[Proposition]** Annuler une instance : annule les tâches ouvertes dans le bassin, les échéances et les sous-processus enfants.
- **B-API-6** Consulter l'état d'une instance par clé de corrélation (en cours ou non, nœud courant, données). Le moteur est la source de vérité sur la vie d'une instance.

## 7. État et données

- **B-ET-1** Chaque instance porte des variables : fournies au démarrage, alimentées par le contenu des complétions de tâche et des signaux, lisibles par les commandes et les requêtes, modifiables par les commandes. Elles peuvent orienter un nœud décisionnel (B-N-8).
- **B-ET-8** Les variables d'une définition sont déclarées une fois, en constantes typées sur la classe de la définition (comme les `Variable<T>` de Sentier V2), puis réutilisées partout : paramètres, décisions, lecture et écriture dans les handlers. Le nom n'est écrit qu'à la déclaration ; c'est lui qui est conservé avec l'instance.
- **B-ET-2** Clé de corrélation sur l'instance : valeur opaque fournie par le client au démarrage, unique, sans signification d'affaires pour le moteur. Les sous-processus héritent de la clé de leur processus de tête.
- **B-ET-7** Identifiant d'affaires sur l'instance : un `long` fourni au démarrage, conservé par le moteur sans qu'il en connaisse le sens, hérité par les sous-processus et remis à chaque handler (B-ABS-10).
- **B-ET-6** Un seul nœud actif par arbre d'instances (processus de tête et sous-processus) : pas de parallélisme. Un signal ciblé par clé de corrélation atteint donc sans ambiguïté le nœud en attente, qu'il soit dans le processus de tête ou dans un sous-processus.
- **B-ET-3** **[Proposition]** Accès concurrent : deux appels simultanés sur la même instance ne doivent pas la corrompre (verrou de ligne ou numéro de version optimiste).
- **B-ET-4** **[Proposition]** Idempotence : compléter deux fois la même tâche ou recevoir deux fois la même échéance ne fait pas avancer l'instance deux fois.
- **B-ET-5** **[Proposition]** Historique minimal des passages de nœuds (qui, quand, résultat), activable.

### 7.1 Persistance

- **B-PER-1** Le package conserve lui-même l'état des instances, dans ses propres tables, à travers la connexion du client.
- **B-PER-2** Bases visées : Oracle en production, SQLite pour les tests. Le SQL du package doit fonctionner sur les deux.
- **B-PER-3** Nom des tables : `{PRÉFIXE}_{nom}_{SUFFIXE}`, le préfixe et le suffixe étant fournis à l'enregistrement du package (exemple de la convention du client : `AT_DECLA_TAXE`).
- **B-PER-4** Les noms de tables et de colonnes ci-dessous sont provisoires ; ils seront révisés par le client pour respecter les mnémoniques de son environnement, puis fixés dans le package. La seule configuration est le préfixe et le suffixe passés à l'enregistrement ; ils s'appliquent aux noms de tables, pas aux colonnes.
- **B-PER-5** Les identifiants sont des `long`, alimentés par des séquences Oracle. Sous SQLite, où les séquences n'existent pas, le package utilise l'auto-incrément ; la génération d'identifiant est donc le seul point propre à chaque base.
- **B-PER-8** Nom des séquences : `{PRÉFIXE}_S_{nom}_{SUFFIXE}` (exemple de la convention du client : `AT_S_NO_DECL_TAXE`). Le nom propre à chaque séquence sera révisé avec les colonnes.
- **B-PER-7** **[Proposition]** Pour le reste, SQL commun aux deux bases : contrôle de concurrence par numéro de version (pas de verrou de ligne), variables d'instance en JSON dans une colonne texte.
- **B-PER-6** **[Proposition]** Le package fournit les scripts de création des tables pour Oracle ; il n'exécute aucune migration de schéma lui-même.

Table **INSTANCE** — une ligne par instance (processus de tête ou sous-processus) :

| Colonne (provisoire) | Type logique | Rôle |
|---|---|---|
| ID_INSTANCE | Nombre (`long`) | Identifiant, alimenté par séquence |
| ID_AFFAIRE | Nombre (`long`) | Identifiant d'affaires fourni au démarrage ; hérité par les sous-processus |
| CLE_CORRELATION | Texte | Clé fournie par le client ; héritée par les sous-processus |
| CLE_DEFINITION | Texte | Définition exécutée |
| ID_INSTANCE_PARENT | Nombre, nul | Instance appelante, pour un sous-processus |
| NOEUD_COURANT | Texte | Identifiant du nœud où l'instance est arrêtée |
| STATUT | Texte court | En cours, terminée, annulée |
| NOM_FIN | Texte, nul | Nom de la fin atteinte (B-N-9) |
| TYPE_ATTENTE | Texte court, nul | Tâche, signal, temps, sous-processus |
| REF_TACHE | Nombre, nul | Identifiant de la tâche dans le bassin, pour un nœud interactif |
| DATE_ECHEANCE | Date-heure, nul | Échéance d'une attente de temps ou d'une escalade |
| GROUPE_SIGNAL | Texte, nul | Groupe de corrélation auquel l'instance est abonnée pendant son attente de signal (B-N-10) |
| DONNEES | Texte long (CLOB) | Variables de l'instance, en JSON |
| NO_VERSION | Nombre | Contrôle de concurrence optimiste |
| ACTEUR_DEMARRAGE | Texte | Acteur qui a démarré l'instance |
| DATE_DEMARRAGE | Date-heure | |
| DATE_MODIFICATION | Date-heure | |
| DATE_FIN | Date-heure, nul | |

Table **HISTORIQUE** — facultative (B-ET-5), une ligne par passage de nœud :

| Colonne (provisoire) | Type logique | Rôle |
|---|---|---|
| ID_HISTORIQUE | Nombre (`long`) | Identifiant, alimenté par séquence |
| ID_INSTANCE | Nombre | Instance concernée |
| NOEUD | Texte | Nœud traversé |
| EVENEMENT | Texte court | Entrée, sortie, annulation |
| TRANSITION | Texte, nul | Transition empruntée à la sortie |
| ACTEUR | Texte, nul | Acteur de l'appel |
| DATE_EVENEMENT | Date-heure | |

Il n'y a pas de table pour les signaux attendus, les abonnements aux groupes ni les échéances : avec un seul nœud actif par instance, le nœud courant et la définition en code suffisent à les retrouver.

## 8. Exigences non fonctionnelles

- **B-NF-1** Léger : aucune dépendance obligatoire hors BCL et `System.Data`.
- **B-NF-6** Cible : .NET 10.
- **B-NF-2** **[Proposition]** Aucun état statique ni fil d'arrière-plan ; le moteur est passif et ne travaille que lorsqu'on l'appelle.
- **B-NF-3** **[Proposition]** Testabilité : implémentations en mémoire des abstractions fournies dans un package de test distinct, pour tester une définition sans base de données.
- **B-NF-4** **[Proposition]** Observabilité : `ILogger` et `ActivitySource` facultatifs.
- **B-NF-5** **[Proposition]** Export d'une définition en diagramme Mermaid pour la documentation.

## 9. Cohabitation avec RHPAM

Contexte : l'application utilise déjà RHPAM à travers `IServiceProcessusAffaires` (implémentations RHPAM, bouchon et service courant). La cohabitation des deux moteurs est permanente, pas une étape de transition.

> Cette section est basée sur l'interface, sur l'implémentation RHPAM, sur `TypeProcessus` et sur le catalogue, tels que montrés par le client.

### 9.1 Constat sur l'interface actuelle

| Méthode actuelle | Ce qu'elle fait aujourd'hui (implémentation RHPAM) | Équivalent dans le nouveau moteur |
|---|---|---|
| `DemarrerProcessusEventuellementAsync` | Crée un travail asynchrone qui démarrera le processus plus tard | Reste côté client |
| `DemarrerProcessusAsync` | Refuse si un processus du même type est en cours pour l'identifiant, puis démarre sans assignation | Démarrage (B-API-1) |
| `DemarrerProcessusSynchroneAsync` | Identique, mais assigne la première tâche au `logon` appelant | Démarrage avec l'option d'assignation (B-COH-15) |
| `EstProcessusEnCoursAsync` | Cherche les instances par variable de processus, filtre par type | Table de corrélation, puis état de l'instance (B-API-6) |
| `EstProcessusDeclarationEnCoursAsync` | Même recherche, sur une liste fixe de types liés à une déclaration | Idem, sur un ensemble de types |
| `TerminerTacheAsync<T>` | Termine la tâche dans le bassin | Compléter une tâche (B-API-2) |
| `TerminerTacheSiEnAttenteAsync<T>` | Retrouve le sous-processus du type demandé, lit ses signaux en attente, émet celui qui figure dans `nomsSignaux` ; retourne le numéro du processus de tête | Signal ciblé conditionnel (B-API-3, B-API-8) |
| `TerminerTacheEnAttenteAsync` | Même mécanique sur le sous-processus « tâche en attente » d'une période | Signal ciblé |
| `AvancerATacheSuivanteAsync<T>` | Émet le signal, puis interroge RHPAM en boucle (jusqu'à dix essais espacés) pour vérifier que la tâche créée porte le code attendu | Signal ciblé ; le résultat de l'appel donne tout de suite la tâche créée (B-API-9) |

Constats :

- **`codeProchaineTache` n'impose pas le prochain nœud** : il sert à vérifier après coup que le processus est bien arrivé sur la tâche attendue. La boucle d'attente n'existe que parce que RHPAM avance de façon asynchrone.
- **Le sous-processus « tâche en attente »** sert uniquement à attendre un signal. Dans le nouveau moteur, c'est un simple nœud d'attente de signal.
- **Le contrôle « déjà en cours » puis le démarrage** sont deux appels distants distincts : deux démarrages simultanés peuvent passer tous les deux.
- **Les variables initiales** sont construites par une fabrique côté client, selon le type de processus.

Trois problèmes de fond :

- **Une même notion sous quatre noms** : `identifiant`, `numeroIdentificationSysteme`, `numeroDeclaration`, `numeroPeriodeDeclaration` sont tous des clés d'affaires servant à retrouver une instance.
- **Le modèle de processus fuit chez l'appelant** : noms de signaux, code de la prochaine tâche.
- **Aucune transaction** : normal pour RHPAM (appel distant), mais c'est justement ce que le nouveau moteur doit corriger.

### 9.2 Principe retenu

- **B-COH-1** Le package NuGet ne contient **aucun** concept d'affaires : ni déclaration, ni période, ni formulaire, ni énumération du client. Il transporte un identifiant d'affaires (B-ET-7) sans l'interpréter. `IServiceProcessusAffaires` reste côté client, comme façade d'affaires.
- **B-COH-2** Une implémentation de `IServiceProcessusAffaires` par moteur (RHPAM, nouveau moteur), plus une implémentation d'aiguillage qui délègue à la bonne selon le catalogue et la table de corrélation. Les appelants actuels (une cinquantaine de références) ne changent pas.
- **B-COH-17** Signal diffusé : chaque implémentation le traite pour son propre moteur ; l'implémentation d'aiguillage peut l'envoyer aux deux si le besoin se présente.
- **B-COH-3** Aiguillage en deux temps, entièrement côté client. Le catalogue associe une catégorie de processus, plus un critère facultatif (le type de formulaire), à un `TypeProcessus`. Le `TypeProcessus` porte ensuite son mode de traitement, c'est-à-dire son moteur. Aujourd'hui tous les types sont en mode RHPAM ; basculer un type revient à changer ce mode.
- **B-COH-18** Le code d'un `TypeProcessus` (par exemple `DeclOrign`) sert de clé de définition dans le nouveau moteur, comme il sert d'identifiant de définition dans RHPAM.
- **B-COH-4** Une instance reste sur le moteur qui l'a démarrée jusqu'à sa fin. Aucune migration d'instance entre moteurs. L'aiguillage s'appuie sur la table de corrélation (section 9.4).
- **B-COH-5** Les tâches du bassin portent le moteur d'origine, pour que la complétion d'une tâche soit acheminée au bon moteur.
- **B-COH-6** Un processus ne peut pas appeler un sous-processus hébergé par l'autre moteur. Comme chaque sous-processus a son propre `TypeProcessus` (B-COH-16) et qu'il y a un moteur par type, la configuration doit placer un parent et ses sous-processus sur le même moteur ; les types liés basculent donc ensemble.
- **B-COH-7** La garantie transactionnelle ne vaut que pour le nouveau moteur. La façade doit le documenter : un appel acheminé vers RHPAM reste hors transaction.

### 9.3 Ce que l'interface actuelle impose au nouveau moteur

- **B-COH-8** Clé de corrélation, recherche et signal par cette clé : voir B-ET-2, B-API-3 et B-API-6.
- **B-COH-10** Signal conditionnel (équivalent de `TerminerTacheSiEnAttenteAsync`) : voir B-API-8.
- **B-COH-11** Un nœud d'attente peut accepter plusieurs signaux, chacun menant à sa propre transition. L'appelant n'a plus à essayer une liste de noms.
- **B-COH-12** Les signaux et les complétions de tâche transportent un contenu (le `T contenu` actuel), versé dans les données de l'instance.
- **B-COH-13** L'acteur (le `logon` actuel) est transmis au moteur à chaque appel, rendu disponible aux handlers et transmis au bassin de tâches à la création d'une tâche. L'assignation automatique à l'acteur reste une décision du bassin, pas du moteur.
- **B-COH-14** Les identifiants d'instance et de tâche sont des `long`, comme dans l'interface actuelle : le numéro de processus retourné est l'identifiant d'instance du moteur.
- **B-COH-15** Une seule opération de démarrage, transactionnelle, avec une option « assigner la première tâche à l'acteur ». L'option est transmise au bassin de tâches, qui fait l'assignation. Les deux démarrages actuels peuvent ainsi devenir une seule méthode. Le démarrage différé (« éventuellement ») reste côté client : c'est un message asynchrone qui appelle ce démarrage.
- **B-COH-16** Décision : `TypeSousProcessus` est retiré et remplacé par `TypeProcessus`. Un sous-processus est une définition comme une autre. Là où l'interface cible un sous-processus, le type sert de filtre : le signal n'est remis que si le nœud en attente appartient à une instance de ce type.

### 9.4 Table de corrélation (côté client)

Décision : le client génère une clé de corrélation au démarrage d'un processus et la conserve dans une table de lien. On ne cherche plus une instance par les variables de processus qui contiennent les identifiants d'affaires.

- **B-COR-1** La table appartient au client, pas au package. Elle est commune aux deux moteurs : la clé de corrélation est unique quel que soit le moteur.
- **B-COR-2** Contenu d'une ligne :

| Colonne | Rôle |
|---|---|
| Identifiant d'affaires | Celui de l'agrégat (déclaration, période, etc.) |
| Type de processus | `TypeProcessus` du processus de tête |
| Clé de corrélation | Générée par le client, transmise au moteur au démarrage |
| Moteur **[Proposition]** | Moteur qui héberge l'instance |
| Statut, dates | Indicatifs ; mis à jour par les écouteurs de fin de processus |

- **B-COR-3** La table ne contient que les processus de tête. Les sous-processus n'y figurent pas : ils héritent de la clé de corrélation de leur parent, et le moteur achemine le signal au nœud actif de l'arbre (B-ET-6). Un sous-processus peut ainsi être exécuté plusieurs fois (boucle dans le parent) sans effet sur la table.
- **B-COR-4** La façade résout (identifiant d'affaires, type de processus) en (moteur, clé de corrélation), puis appelle le moteur avec la clé.
- **B-COR-5** Source de vérité : le moteur BPM, pas la table. Pour « le processus est-il en cours ? », la façade résout la clé dans la table puis interroge le moteur.
- **B-COR-6** Fin de processus : un écouteur met la table à jour. Pour RHPAM, ce sont ses écouteurs d'événements de fin. Pour le nouveau moteur, c'est le signalement de fin de processus (B-ABS-2), dans la transaction qui termine l'instance.
- **B-COR-7** Nouveau moteur : la ligne est écrite sur la même connexion, donc dans la même transaction, que le démarrage.
- **B-COR-11** Unicité : une seule instance en cours par (identifiant d'affaires, type de processus), règle déjà appliquée par le code actuel. Comme le statut de la table n'est qu'indicatif, la façade confirme auprès du moteur avant de refuser un démarrage.
- **B-COR-12** La table doit permettre de chercher un processus en cours pour un identifiant d'affaires parmi un ensemble de types (besoin de `EstProcessusDeclarationEnCoursAsync`).
- **B-COR-13** La table de corrélation devient une troisième stratégie d'obtention de processus, à côté des deux qui existent déjà chez le client (par table d'événements, par variable de processus).
- **B-COR-8** **[Proposition]** Conserver le moteur sur la ligne, même s'il se déduit du catalogue. Si une entrée du catalogue change de moteur, les instances déjà en cours doivent encore être acheminées vers l'ancien.
- **B-COR-9** **[Proposition]** RHPAM : l'appel distant et l'écriture dans la table ne sont pas atomiques. Prévoir l'ordre des opérations et une réconciliation (ligne sans instance, instance sans ligne).
- **B-COR-10** Reprise de l'existant : les instances RHPAM déjà en cours doivent recevoir une ligne dans la table (et une clé de corrélation) pour que l'aiguillage soit uniforme. Travail à planifier côté client.

## 10. Hors périmètre

- Concepteur graphique, import/export BPMN.
- Parallélisme (passerelles fork/join) : non prévu pour le moment.
- Gestion des utilisateurs, rôles et assignations (responsabilité du bassin de tâches).
- Reprises automatiques, compensation, sagas distribuées.
- Définitions enregistrées en base et versionnées.
- Migration d'instances de RHPAM vers le nouveau moteur.
- Multi-locataire (plusieurs organisations isolées dans une même base) : supposé non requis.

## 11. Questions ouvertes

Aucune pour l'instant. Restent à faire côté client : la révision des noms de tables, de colonnes et de séquences (section 7.1), et le tri des éléments encore marqués **[Proposition]**.

Hypothèse retenue : un signal diffusé ou de groupe fait avancer plusieurs instances dans la même transaction (jugé acceptable) ; l'échec d'une seule annule l'envoi pour toutes.

Limite connue du signal de groupe : le moteur ne mémorise pas les signaux. Une instance qui arrive sur l'attente pendant que le signal est envoyé peut le manquer. Le domaine doit sérialiser les deux (verrou sur la ligne du blocage, lue par la requête qui précède l'attente et modifiée par le déblocage) ; l'escalade (B-N-5) sert de filet.
