# Besoins — BPM lightweight (package NuGet)

> Statut : ébauche. Les éléments marqués **[Proposition]** ne viennent pas du besoin initial ; ils sont à valider ou à rejeter.

## 1. Vision

Un moteur de processus minimaliste, livré en package NuGet, qui s'embarque dans l'application cliente (pas de serveur, pas de service d'arrière-plan imposé). Le moteur orchestre ; toute la logique d'affaires reste côté client, derrière des interfaces d'abstraction.

Principe directeur : à chaque sollicitation (démarrage, fin de tâche, signal, échéance), le moteur exécute une suite d'étapes **dans une seule transaction fournie par le client**, jusqu'à atteindre un point d'attente ou la fin du processus.

## 2. Transaction

- **B-TX-1** Le client injecte une `IDbTransaction` à chaque appel du moteur. C'est la seule dépendance d'infrastructure reçue.
- **B-TX-2** Le moteur n'ouvre, ne valide et n'annule jamais la transaction. Le commit et le rollback appartiennent au client.
- **B-TX-3** Toutes les étapes exécutées lors d'un même appel (nœuds systémiques, décisions, création de tâches, changements d'état du moteur) partagent cette transaction : tout passe ou rien ne passe.
- **B-TX-4** La transaction est transmise aux handlers de commandes et de requêtes du client pour qu'ils y participent.
- **B-TX-5** **[Proposition]** En cas d'exception dans un handler, le moteur ne rattrape rien et ne fait aucune reprise : l'exception remonte et le client annule la transaction. L'instance reste donc dans son dernier état validé.
- **B-TX-6** **[Proposition]** Documenter clairement que les effets hors base de données (courriel, appel HTTP) faits dans un handler ne sont pas transactionnels ; le patron recommandé côté client est une boîte d'envoi (outbox).

## 3. Définition de processus (builder fluent)

- **B-DEF-1** Une définition se construit en code avec un builder fluent très simple, sans XML ni BPMN ni concepteur graphique.
- **B-DEF-2** Une définition est immuable une fois construite.
- **B-DEF-3** **[Proposition]** La définition est validée à la construction (`Build()`) : nœud de départ unique, nœuds inatteignables, transitions vers un nœud inexistant, décision sans branche par défaut, identifiants en double.
- **B-DEF-4** **[Proposition]** Chaque définition porte une clé et une version. Une instance en cours reste liée à la version avec laquelle elle a démarré.

Esquisse d'API, à titre indicatif seulement :

```csharp
var definition = ProcessDefinition.Create("demande-conge", version: 1)
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

- **B-N-1** Nœud interactif : l'action **pré** s'exécute dans la transaction qui crée la tâche ; l'action **post** s'exécute dans la transaction qui signale la complétion de la tâche. Les deux sont facultatives.
- **B-N-2** **[Proposition]** Le résultat de la tâche interactive (ex. « approuvé » / « refusé ») peut déterminer la transition sortante, sans nœud décisionnel supplémentaire.
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
- **B-ABS-5** **[Proposition]** **Planification des échéances** : le moteur n'a pas de fil d'arrière-plan. Il déclare l'échéance au client (interface de planification) et le client rappelle le moteur à l'heure venue. Variante : le moteur expose « quelles échéances sont dues ? » et l'hôte interroge périodiquement.
- **B-ABS-6** **[Proposition]** **Signalement des autres événements du cycle de vie** (instance démarrée, nœud entré/sorti, instance suspendue), utile pour l'audit et le suivi sans alourdir le moteur.
- **B-ABS-7** **[Proposition]** Enregistrement simple par injection de dépendances (`services.AddBpm(...)`), sans l'imposer : le moteur doit rester utilisable sans conteneur.

## 6. Points d'entrée du moteur

Chaque appel reçoit la `IDbTransaction` du client.

- **B-API-1** Démarrer une instance (définition, données initiales, clé d'affaires).
- **B-API-2** Signaler la complétion d'une tâche interactive (avec son résultat).
- **B-API-3** Envoyer un signal à une instance.
- **B-API-4** Signaler qu'une échéance est atteinte.
- **B-API-5** **[Proposition]** Annuler une instance : annule les tâches ouvertes dans le bassin, les échéances et les sous-processus enfants.
- **B-API-6** **[Proposition]** Consulter l'état d'une instance (nœud courant, statut, données).

## 7. État et données

- **B-ET-1** **[Proposition]** Chaque instance porte un contexte de données (variables) lisible par les commandes et les requêtes, et modifiable par les commandes.
- **B-ET-2** **[Proposition]** Clé d'affaires / de corrélation sur l'instance, pour retrouver « le processus du dossier X » et adresser un signal sans connaître l'identifiant technique.
- **B-ET-3** **[Proposition]** Accès concurrent : deux appels simultanés sur la même instance ne doivent pas la corrompre (verrou de ligne ou numéro de version optimiste).
- **B-ET-4** **[Proposition]** Idempotence : compléter deux fois la même tâche ou recevoir deux fois la même échéance ne fait pas avancer l'instance deux fois.
- **B-ET-5** **[Proposition]** Historique minimal des passages de nœuds (qui, quand, résultat), activable.

## 8. Exigences non fonctionnelles

- **B-NF-1** Léger : aucune dépendance obligatoire hors BCL et `System.Data`.
- **B-NF-2** **[Proposition]** Aucun état statique ni fil d'arrière-plan ; le moteur est passif et ne travaille que lorsqu'on l'appelle.
- **B-NF-3** **[Proposition]** Testabilité : implémentations en mémoire des abstractions fournies dans un package de test distinct, pour tester une définition sans base de données.
- **B-NF-4** **[Proposition]** Observabilité : `ILogger` et `ActivitySource` facultatifs.
- **B-NF-5** **[Proposition]** Export d'une définition en diagramme Mermaid pour la documentation.

## 9. Hors périmètre (proposé)

- Concepteur graphique, import/export BPMN.
- Passerelles parallèles (fork/join) — à confirmer, voir question 3.
- Gestion des utilisateurs, rôles et assignations (responsabilité du bassin de tâches).
- Reprises automatiques, compensation, sagas distribuées.
- Migration d'instances en cours vers une nouvelle version de définition.

## 10. Questions ouvertes

1. **Persistance de l'état du moteur.** Avec seulement une `IDbTransaction`, où vit l'état des instances ? Soit le moteur écrit lui-même ses tables via `transaction.Connection` (il faut alors gérer les dialectes SQL et le schéma), soit le client implémente une interface de stockage (plus léger pour le package, plus de travail pour le client).
2. **Synchrone ou asynchrone.** `IDbTransaction` est une interface synchrone ; les handlers côté client seront-ils `async` ? Si oui, viser plutôt `DbTransaction`.
3. **Parallélisme.** Un seul nœud actif par instance (beaucoup plus simple), ou branches parallèles ?
4. **Forme des commandes/requêtes.** Objets typés avec handlers génériques (`ICommandHandler<T>`), ou un répartiteur unique fourni par le client (compatible MediatR ou autre) ?
5. **Définitions.** Uniquement en code, ou aussi sérialisables (JSON) pour être stockées et versionnées en base ?
6. **Signaux.** Adressés à une instance précise, ou diffusés à toutes les instances en attente de ce signal ?
7. **Cibles .NET.** `netstandard2.0`, ou .NET 8+ seulement ?
8. **Multi-locataire.** Besoin d'un identifiant de locataire sur les instances ?
