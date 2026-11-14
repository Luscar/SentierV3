# Besoins — BPM lightweight (package NuGet)

> Statut : ébauche. Les éléments marqués **[Proposition]** ne viennent pas du besoin initial ; ils sont à valider ou à rejeter.

## 1. Vision

Un moteur de processus minimaliste, livré en package NuGet, qui s'embarque dans l'application cliente (pas de serveur, pas de service d'arrière-plan imposé). Le moteur orchestre ; toute la logique d'affaires reste côté client, derrière des interfaces d'abstraction.

Principe directeur : à chaque sollicitation (démarrage, fin de tâche, signal, échéance), le moteur exécute une suite d'étapes **dans une seule transaction, celle du client**, jusqu'à atteindre un point d'attente ou la fin du processus.

## 2. Transaction

- **B-TX-1** Le client fournit au moteur une `DbConnection` déjà ouverte : c'est la seule poignée d'infrastructure reçue. Il n'y a pas d'objet transaction à passer.
- **B-TX-2** La transaction est ambiante : l'API du client ouvre un `TransactionScope`, et la connexion y participe. Le moteur n'ouvre et ne ferme jamais la connexion, ne crée pas de `TransactionScope` et ne valide ni n'annule rien.
- **B-TX-8** Le moteur exécute ses commandes sur cette connexion sans objet transaction, comme la couche d'accès du client (au-dessus de Dapper). La garantie « tout ou rien » repose sur le `TransactionScope` du client.
- **B-TX-9** Le moteur étant asynchrone, le `TransactionScope` du client doit être créé avec le flux asynchrone activé (`TransactionScopeAsyncFlowOption.Enabled`). À documenter comme prérequis.
- **B-TX-10** **[À vérifier]** Tous les fournisseurs ne participent pas à un `TransactionScope` ; il faut confirmer par un test que le fournisseur SQLite utilisé en test le fait. Sinon, les tests ne valideront pas l'annulation en cas d'erreur, même si Oracle la garantit en production.
- **B-TX-3** Toutes les étapes exécutées lors d'un même appel (nœuds systémiques, décisions, création de tâches, changements d'état du moteur) partagent cette transaction : tout passe ou rien ne passe.
- **B-TX-4** Les handlers du client travaillent sur la même connexion, obtenue du scope courant ; le moteur n'a rien à leur transmettre.
- **B-TX-5** **[Proposition]** En cas d'exception dans un handler, le moteur ne rattrape rien et ne fait aucune reprise : l'exception remonte et le client annule la transaction. L'instance reste donc dans son dernier état validé.
- **B-TX-7** Toute l'API du moteur et tous les handlers sont asynchrones (`Task`, `CancellationToken`).
- **B-TX-6** **[Proposition]** Documenter clairement que les effets hors base de données (courriel, appel HTTP) faits dans un handler ne sont pas transactionnels ; le patron recommandé côté client est une boîte d'envoi (outbox).

## 3. Définition de processus (builder fluent)

- **B-DEF-1** Une définition se construit en code avec un builder fluent très simple, sans XML ni BPMN ni concepteur graphique.
- **B-DEF-2** Une définition est immuable une fois construite.
- **B-DEF-3** **[Proposition]** La définition est validée à la construction (`Build()`) : nœud de départ unique, nœuds inatteignables, transitions vers un nœud inexistant, décision sans branche par défaut, identifiants en double.
- **B-DEF-4** Décision : les définitions vivent uniquement en code ; elles ne sont pas enregistrées en base. Une instance n'est **pas** liée à une version : elle ne conserve que la clé de sa définition, son nœud courant et ses données. Quand le code change, toutes les instances en cours suivent la nouvelle définition à leur prochaine sollicitation (comportement attendu par les utilisateurs, comme avec RHPAM aujourd'hui).
- **B-DEF-5** Conséquence : l'identifiant d'un nœud est un contrat. Le renommer ou le retirer touche les instances qui y sont arrêtées.
- **B-DEF-6** Migration des instances en cours : quand un nœud est retiré ou renommé, la définition déclare une correspondance « ancien nœud → nouveau nœud ». Une instance arrêtée sur l'ancien nœud est replacée sur le nouveau à sa prochaine sollicitation.
- **B-DEF-7** **[Proposition]** Au démarrage de l'application, une vérification signale les instances arrêtées sur un nœud inconnu et sans correspondance.

Esquisse d'API, à titre indicatif seulement :

```csharp
var definition = ProcessDefinition.Create("demande-conge")
    .System("valider", new ValiderDemande())
    .Decision("besoin-approbation", new BesoinApprobationQuery(), d => d
        .When(true, "approuver")
        .Otherwise("enregistrer"))
    .Interactive("approuver", i => i
        .Pre(new PreparerApprobation())
        .Post(new ConsignerDecision())
        .On("approuve", "enregistrer")
        .On("refuse", "notifier-refus"))
    .WaitTime("delai-grace", TimeSpan.FromDays(2))
    .WaitSignal("attente-paiement", "paiement-recu")
    .SubProcess("archivage", "archivage-dossier")
    .System("enregistrer", new EnregistrerConge())
    .End()
    .Build();
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
- **B-N-1** Nœud interactif : l'action **pré** s'exécute dans la transaction qui crée la tâche ; l'action **post** s'exécute dans la transaction qui signale la complétion de la tâche. Les deux sont facultatives.
- **B-N-2** Transitions nommées : un nœud d'attente (interactif ou signal) peut avoir plusieurs transitions sortantes nommées. L'appelant choisit la transition par son nom en complétant la tâche ou en envoyant le signal ; il ne désigne jamais un nœud. Remplace le `codeProchaineTache` de `AvancerATacheSuivanteAsync`.
- **B-N-3** Sous-processus : l'enfant s'exécute dans la même transaction que le parent tant qu'il n'atteint pas de point d'attente.
- **B-N-4** **[Proposition]** Passage de données entre parent et enfant : entrées à l'appel, sorties au retour.
- **B-N-5** **[Proposition]** Échéance sur un nœud interactif ou une attente de signal (délai dépassé → transition d'escalade), pour éviter les instances bloquées indéfiniment.
- **B-N-6** **[Proposition]** Garde contre les boucles sans fin : nombre maximal d'étapes par appel, configurable.

## 5. Abstractions côté client

Le moteur ne connaît que des interfaces ; le client fournit les implémentations.

- **B-ABS-1** **Bassin de tâches** : adaptateur vers la couche existante. Au minimum : créer une tâche pour un nœud interactif, annuler une tâche ouverte. Le bassin reste propriétaire de l'assignation, des files et de l'affichage.
- **B-ABS-2** **Signalement de fin de processus** : le client est avisé quand une instance se termine (complétée, annulée), dans la même transaction.
- **B-ABS-3** **Commandes** : les nœuds systémiques et les actions pré/post lancent des commandes traitées par des handlers implémentés côté client.
- **B-ABS-4** **Requêtes** : les nœuds décisionnels s'appuient sur des requêtes traitées côté client.
- **B-ABS-8** Les handlers sont typés et asynchrones, sur des interfaces définies par le package (du genre `ICommandHandler<TCommande>` et `IQueryHandler<TRequete, TResultat>`). Le package ne dépend pas de MediatR, pour rester utilisable par d'autres applications ; un client qui le souhaite peut écrire un handler qui relaie vers MediatR.
- **B-ABS-5** **Échéances** : le moteur n'a ni minuterie ni fil d'arrière-plan. Il enregistre la date d'échéance avec l'instance ; c'est un composant externe au package qui réveille le moteur par l'interface de service (B-API-4).
- **B-ABS-6** **[Proposition]** **Signalement des autres événements du cycle de vie** (instance démarrée, nœud entré/sorti, instance suspendue), utile pour l'audit et le suivi sans alourdir le moteur.
- **B-ABS-7** **Injection de dépendances** : l'application cliente utilise Autofac. Elle ouvre un scope et appelle le moteur à travers une couche de service enregistrée dans ses propres injections. L'enregistrement du package doit tenir en une ligne.
  - Le cœur du package ne dépend d'aucun conteneur.
  - **[Proposition]** Un package compagnon fournit un module Autofac (`builder.RegisterModule(...)`) qui enregistre le moteur et découvre, par balayage d'assemblies, les définitions de processus et les handlers.
  - Le moteur et les handlers sont résolus dans le scope courant de l'application ; ils partagent donc ses services (dépôts, contexte utilisateur).

## 6. Points d'entrée du moteur

Le moteur obtient la `DbConnection` du client (B-TX-1) ; ses opérations sont exposées par une interface de service.

- **B-API-1** Démarrer une instance : définition, clé de corrélation fournie par le client, données initiales, acteur.
- **B-API-2** Compléter une tâche interactive, avec le nom de la transition choisie et des données.
- **B-API-3** Envoyer un signal, de deux façons :
  - **ciblé** : à l'instance désignée par une clé de corrélation ;
  - **diffusé** : à toutes les instances en attente de ce signal, avec un filtre facultatif par définition.
- **B-API-7** Un signal peut porter le nom d'une transition et des données qui initialisent ou modifient des variables de l'instance.
- **B-API-9** Chaque opération qui fait avancer une instance (démarrage, complétion, signal, échéance) retourne l'état atteint : nœud courant, type d'attente, et pour un nœud interactif le code et l'identifiant de la tâche créée. L'appelant n'a jamais à interroger en boucle.
- **B-API-10** L'état d'une instance (B-API-6) inclut les signaux qu'elle attend.
- **B-API-8** Signal conditionnel : si rien n'attend ce signal, l'appel ne lève pas d'erreur et le résultat l'indique.
- **B-API-4** Échéances : le moteur expose la liste des échéances dues à un instant donné, et l'opération qui déclenche une échéance. Un composant externe au package appelle ces deux opérations.
- **B-API-5** **[Proposition]** Annuler une instance : annule les tâches ouvertes dans le bassin, les échéances et les sous-processus enfants.
- **B-API-6** Consulter l'état d'une instance par clé de corrélation (en cours ou non, nœud courant, données). Le moteur est la source de vérité sur la vie d'une instance.

## 7. État et données

- **B-ET-1** **[Proposition]** Chaque instance porte un contexte de données (variables) lisible par les commandes et les requêtes, et modifiable par les commandes.
- **B-ET-2** Clé de corrélation sur l'instance : valeur opaque fournie par le client au démarrage, unique, sans signification d'affaires pour le moteur. Les sous-processus héritent de la clé de leur processus de tête.
- **B-ET-6** Un seul nœud actif par arbre d'instances (processus de tête et sous-processus) : pas de parallélisme. Un signal ciblé par clé de corrélation atteint donc sans ambiguïté le nœud en attente, qu'il soit dans le processus de tête ou dans un sous-processus.
- **B-ET-3** **[Proposition]** Accès concurrent : deux appels simultanés sur la même instance ne doivent pas la corrompre (verrou de ligne ou numéro de version optimiste).
- **B-ET-4** **[Proposition]** Idempotence : compléter deux fois la même tâche ou recevoir deux fois la même échéance ne fait pas avancer l'instance deux fois.
- **B-ET-5** **[Proposition]** Historique minimal des passages de nœuds (qui, quand, résultat), activable.

### 7.1 Persistance

- **B-PER-1** Le package conserve lui-même l'état des instances, dans ses propres tables, à travers la connexion du client.
- **B-PER-2** Bases visées : Oracle en production, SQLite pour les tests. Le SQL du package doit fonctionner sur les deux.
- **B-PER-3** Nom des tables : `{PRÉFIXE}_{nom}_{SUFFIXE}`, le préfixe et le suffixe étant fournis à l'enregistrement du package (exemple de la convention du client : `AT_DECLA_TAXE`).
- **B-PER-4** Les noms de tables et de colonnes ci-dessous sont provisoires ; ils seront révisés par le client pour respecter les mnémoniques de son environnement, puis fixés dans le package. La seule configuration est le préfixe et le suffixe passés à l'enregistrement ; ils s'appliquent aux noms de tables, pas aux colonnes.
- **B-PER-5** **[Proposition]** Pour rester portable entre Oracle et SQLite : identifiants générés par le package (pas de séquence ni d'auto-incrément), contrôle de concurrence par numéro de version (pas de verrou de ligne), données d'instance en JSON dans une colonne texte.
- **B-PER-6** **[Proposition]** Le package fournit les scripts de création des tables pour Oracle ; il n'exécute aucune migration de schéma lui-même.

Table **INSTANCE** — une ligne par instance (processus de tête ou sous-processus) :

| Colonne (provisoire) | Type logique | Rôle |
|---|---|---|
| ID_INSTANCE | Texte 36 | Identifiant généré par le package |
| CLE_CORRELATION | Texte | Clé fournie par le client ; héritée par les sous-processus |
| CLE_DEFINITION | Texte | Définition exécutée |
| ID_INSTANCE_PARENT | Texte 36, nul | Instance appelante, pour un sous-processus |
| NOEUD_COURANT | Texte | Identifiant du nœud où l'instance est arrêtée |
| STATUT | Texte court | En cours, terminée, annulée |
| TYPE_ATTENTE | Texte court, nul | Tâche, signal, temps, sous-processus |
| REF_TACHE | Nombre, nul | Identifiant de la tâche dans le bassin, pour un nœud interactif |
| DATE_ECHEANCE | Date-heure, nul | Échéance d'une attente de temps ou d'une escalade |
| DONNEES | Texte long (CLOB) | Variables de l'instance, en JSON |
| NO_VERSION | Nombre | Contrôle de concurrence optimiste |
| ACTEUR_DEMARRAGE | Texte | Acteur qui a démarré l'instance |
| DATE_DEMARRAGE | Date-heure | |
| DATE_MODIFICATION | Date-heure | |
| DATE_FIN | Date-heure, nul | |

Table **HISTORIQUE** — facultative (B-ET-5), une ligne par passage de nœud :

| Colonne (provisoire) | Type logique | Rôle |
|---|---|---|
| ID_HISTORIQUE | Texte 36 | Identifiant généré par le package |
| ID_INSTANCE | Texte 36 | Instance concernée |
| NOEUD | Texte | Nœud traversé |
| EVENEMENT | Texte court | Entrée, sortie |
| TRANSITION | Texte, nul | Transition empruntée à la sortie |
| ACTEUR | Texte, nul | Acteur de l'appel |
| DATE_EVENEMENT | Date-heure | |

Il n'y a pas de table pour les signaux attendus ni pour les échéances : avec un seul nœud actif par instance, le nœud courant et la définition en code suffisent à les retrouver.

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

- **B-COH-1** Le package NuGet ne contient **aucun** concept d'affaires : ni déclaration, ni période, ni formulaire, ni énumération du client. `IServiceProcessusAffaires` reste côté client, comme façade d'affaires.
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
- **B-COH-14** Les identifiants de tâche restent compatibles avec les `long` de l'interface actuelle.
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

1. **Numéro de processus retourné.** L'interface retourne un `long` (numéro de processus RHPAM). Qu'en font les appelants ? Le nouveau moteur identifie ses instances par un texte généré (B-PER-5). Soit la table de corrélation fournit un numéro, soit le moteur passe à un identifiant numérique, ce qui demande une séquence Oracle.
2. **Fabrique de variables.** Les variables initiales construites par type de processus restent-elles nécessaires, ou les handlers liront-ils directement les données d'affaires à partir de l'identifiant ?
3. **Fournisseur SQLite et `TransactionScope`.** À vérifier par un test (B-TX-10).

Hypothèse retenue : un signal diffusé fait avancer plusieurs instances dans la même transaction (cas rare, jugé acceptable).
