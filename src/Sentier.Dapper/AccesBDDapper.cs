using System.Data;
using System.Text.RegularExpressions;
using Dapper;

namespace Sentier.Dapper;

/// <summary>
/// Accès aux données sur la connexion du client, déjà ouverte. Le moteur n'ouvre ni ne ferme la connexion et
/// n'utilise aucun objet transaction : la connexion participe au TransactionScope du client.
/// Les appels étant asynchrones, Dapper exige que les commandes créées par la connexion soient des DbCommand
/// (c'est le cas d'Oracle et de SQLite).
/// </summary>
public sealed partial class AccesBDDapper(IDbConnection connexion, DialecteSentier dialecte) : IAccesBDSentier
{
    public Task<int> ExecuteNonQueryAsync(string requeteSQL, object? parametres = null, CancellationToken cancellationToken = default)
        => connexion.ExecuteAsync(new CommandDefinition(requeteSQL, parametres, cancellationToken: cancellationToken));

    public Task<IEnumerable<T>> QueryAsync<T>(string requeteSQL, object? parametres = null, CancellationToken cancellationToken = default)
        => connexion.QueryAsync<T>(new CommandDefinition(requeteSQL, parametres, cancellationToken: cancellationToken));

    public Task<T?> QuerySingleOrDefaultAsync<T>(string requeteSQL, object? parametres = null, CancellationToken cancellationToken = default)
        => connexion.QuerySingleOrDefaultAsync<T?>(new CommandDefinition(requeteSQL, parametres, cancellationToken: cancellationToken));

    /// <summary>Seul point propre à chaque base : séquence Oracle, ou table à auto-incrément du même nom sous SQLite.</summary>
    public async Task<long> GetSequenceAsync(string sequence, CancellationToken cancellationToken = default)
    {
        if (!Identifiant().IsMatch(sequence)) throw new SentierException($"« {sequence} » n'est pas un nom de séquence valide.");

        if (dialecte == DialecteSentier.Oracle)
            return await connexion.ExecuteScalarAsync<long>(
                new CommandDefinition($"SELECT {sequence}.NEXTVAL FROM DUAL", cancellationToken: cancellationToken));

        await connexion.ExecuteAsync(new CommandDefinition($"INSERT INTO {sequence} DEFAULT VALUES", cancellationToken: cancellationToken));
        return await connexion.ExecuteScalarAsync<long>(
            new CommandDefinition("SELECT last_insert_rowid()", cancellationToken: cancellationToken));
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]*$")]
    private static partial Regex Identifiant();
}
