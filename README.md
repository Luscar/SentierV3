# SentierV3

Moteur de processus léger, livré en packages NuGet (.NET 10) et embarqué dans l'application cliente. Les besoins sont dans [docs/besoins-bpm-lightweight.md](docs/besoins-bpm-lightweight.md) ; le mode d'emploi complet est dans [docs/guide-utilisation.md](docs/guide-utilisation.md).

| Package | Rôle | Dépendances |
|---|---|---|
| `Sentier` | Builder de définitions, moteur, SQL des tables | aucune |
| `Sentier.Dapper` | `IAccesBDSentier` sur une `IDbConnection` (Oracle, SQLite) | Dapper 2.1.79 |
| `Sentier.Autofac` | Module qui enregistre le moteur et découvre définitions et handlers | Autofac 8.3.0 |

## Construire

```
dotnet test
dotnet pack -c Release      # packages dans artifacts/
```

## Brancher dans l'application

Le moteur accède à la base par `IAccesBDSentier`, dont les quatre méthodes ont la signature de celles de `IAccesBDPlus`. L'adaptateur côté client n'est que de la délégation :

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

Enregistrement :

```csharp
builder.RegisterModule(new ModuleSentier(o => { o.Prefixe = "AT"; o.Suffixe = "TAXE"; }, typeof(DeclarationOriginale).Assembly));
builder.RegisterType<AccesBDSentier>().As<IAccesBDSentier>().InstancePerLifetimeScope();
builder.RegisterType<BassinTaches>().As<IBassinTaches>().InstancePerLifetimeScope();   // adaptateur vers le bassin
```

Sans couche d'accès maison, `Sentier.Dapper` fournit `new AccesBDDapper(connexion, DialecteSentier.Oracle)`.

## Prérequis et conventions

- Le moteur ne gère aucune transaction : le client ouvre un `TransactionScope` avec `TransactionScopeAsyncFlowOption.Enabled`.
- Le SQL utilise des paramètres `:nom` et des colonnes aliasées sans soulignement, lus par Dapper sous Oracle comme sous SQLite.
- Tables `{PRÉFIXE}_INSTANCE_{SUFFIXE}` et `{PRÉFIXE}_HISTORIQUE_{SUFFIXE}`, séquences `{PRÉFIXE}_S_INSTANCE_{SUFFIXE}` et `{PRÉFIXE}_S_HISTORIQUE_{SUFFIXE}`. `SchemaSentier.Creation(dialecte, options)` retourne les scripts de création ; le package ne les exécute jamais.
- `GetSequenceAsync` est le seul point propre à chaque base. `AccesBDDapper` lit `NEXTVAL` sous Oracle ; sous SQLite, une séquence est une table à auto-incrément du même nom.
- Les dates sont en heure locale (`TimeProvider`, remplaçable à l'enregistrement).

## Exemple de définition

Voir `tests/Sentier.Tests/Processus.cs` : la définition de l'esquisse des besoins, ses handlers et ses sous-processus.
