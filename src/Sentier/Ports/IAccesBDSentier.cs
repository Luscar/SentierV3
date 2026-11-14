namespace Sentier;

/// <summary>
/// Accès aux données dont le moteur a besoin. Les signatures reprennent celles de la couche d'accès du client
/// (au-dessus de Dapper), pour que l'adaptateur ne soit que de la délégation. Les commandes s'exécutent sans objet
/// transaction : la garantie « tout ou rien » vient du TransactionScope du client.
/// </summary>
public interface IAccesBDSentier
{
    Task<int> ExecuteNonQueryAsync(string requeteSQL, object? parametres = null, CancellationToken cancellationToken = default);
    Task<IEnumerable<T>> QueryAsync<T>(string requeteSQL, object? parametres = null, CancellationToken cancellationToken = default);
    Task<T?> QuerySingleOrDefaultAsync<T>(string requeteSQL, object? parametres = null, CancellationToken cancellationToken = default);
    Task<long> GetSequenceAsync(string sequence, CancellationToken cancellationToken = default);
}
