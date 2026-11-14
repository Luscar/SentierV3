namespace Sentier.Persistance;

/// <summary>SQL du package, commun à Oracle et à SQLite. Seule la génération d'identifiant passe par la couche d'accès.</summary>
internal sealed class Depot(IAccesBDSentier bd, NomsSentier noms, TimeProvider horloge)
{
    private string Selection => $"""
        SELECT ID_INSTANCE AS IdInstance, ID_AFFAIRE AS IdAffaire, CLE_CORRELATION AS CleCorrelation,
               CLE_DEFINITION AS CleDefinition, ID_INSTANCE_PARENT AS IdInstanceParent, NOEUD_COURANT AS NoeudCourant,
               STATUT AS Statut, NOM_FIN AS NomFin, TYPE_ATTENTE AS TypeAttente, REF_TACHE AS RefTache,
               DATE_ECHEANCE AS DateEcheance, GROUPE_SIGNAL AS GroupeSignal, DONNEES AS Donnees, NO_VERSION AS NoVersion,
               ACTEUR_DEMARRAGE AS ActeurDemarrage, DATE_DEMARRAGE AS DateDemarrage, DATE_FIN AS DateFin
          FROM {noms.TableInstance}
        """;

    public DateTime Maintenant => horloge.GetLocalNow().DateTime;

    public async Task InsererAsync(Instance instance, CancellationToken ct)
    {
        instance.Id = await bd.GetSequenceAsync(noms.SequenceInstance, ct);
        instance.NoVersion = 1;
        await bd.ExecuteNonQueryAsync($"""
            INSERT INTO {noms.TableInstance}
                (ID_INSTANCE, ID_AFFAIRE, CLE_CORRELATION, CLE_DEFINITION, ID_INSTANCE_PARENT, NOEUD_COURANT, STATUT,
                 NOM_FIN, TYPE_ATTENTE, REF_TACHE, DATE_ECHEANCE, GROUPE_SIGNAL, DONNEES, NO_VERSION, ACTEUR_DEMARRAGE,
                 DATE_DEMARRAGE, DATE_MODIFICATION, DATE_FIN)
            VALUES
                (:IdInstance, :IdAffaire, :CleCorrelation, :CleDefinition, :IdInstanceParent, :NoeudCourant, :Statut,
                 :NomFin, :TypeAttente, :RefTache, :DateEcheance, :GroupeSignal, :Donnees, :NoVersion, :ActeurDemarrage,
                 :DateDemarrage, :DateModification, :DateFin)
            """,
            new
            {
                IdInstance = instance.Id,
                instance.IdAffaire,
                instance.CleCorrelation,
                instance.CleDefinition,
                IdInstanceParent = instance.IdParent,
                instance.NoeudCourant,
                Statut = Codes.De(instance.Statut),
                instance.NomFin,
                TypeAttente = Codes.De(instance.Attente),
                instance.RefTache,
                instance.DateEcheance,
                instance.GroupeSignal,
                Donnees = instance.Variables.VersJson(),
                instance.NoVersion,
                instance.ActeurDemarrage,
                instance.DateDemarrage,
                DateModification = instance.DateDemarrage,
                instance.DateFin,
            }, ct);
    }

    /// <summary>Contrôle de concurrence optimiste : la mise à jour échoue si la version a changé.</summary>
    public async Task MettreAJourAsync(Instance instance, CancellationToken ct)
    {
        var lignes = await bd.ExecuteNonQueryAsync($"""
            UPDATE {noms.TableInstance}
               SET NOEUD_COURANT = :NoeudCourant, STATUT = :Statut, NOM_FIN = :NomFin, TYPE_ATTENTE = :TypeAttente,
                   REF_TACHE = :RefTache, DATE_ECHEANCE = :DateEcheance, GROUPE_SIGNAL = :GroupeSignal, DONNEES = :Donnees,
                   NO_VERSION = NO_VERSION + 1, DATE_MODIFICATION = :DateModification, DATE_FIN = :DateFin
             WHERE ID_INSTANCE = :IdInstance AND NO_VERSION = :NoVersion
            """,
            new
            {
                instance.NoeudCourant,
                Statut = Codes.De(instance.Statut),
                instance.NomFin,
                TypeAttente = Codes.De(instance.Attente),
                instance.RefTache,
                instance.DateEcheance,
                instance.GroupeSignal,
                Donnees = instance.Variables.VersJson(),
                DateModification = Maintenant,
                instance.DateFin,
                IdInstance = instance.Id,
                instance.NoVersion,
            }, ct);
        if (lignes != 1) throw new ConflitConcurrenceException(instance.Id);
        instance.NoVersion++;
    }

    public async Task<Instance?> ChargerAsync(long idInstance, CancellationToken ct)
        => Vers(await bd.QuerySingleOrDefaultAsync<LigneInstance>(
            $"{Selection} WHERE ID_INSTANCE = :IdInstance", new { IdInstance = idInstance }, ct));

    public async Task<Instance?> ChargerTeteAsync(string cleCorrelation, CancellationToken ct)
        => Vers(await bd.QuerySingleOrDefaultAsync<LigneInstance>(
            $"{Selection} WHERE CLE_CORRELATION = :CleCorrelation AND ID_INSTANCE_PARENT IS NULL",
            new { CleCorrelation = cleCorrelation }, ct));

    /// <summary>Instances en cours de l'arbre, de la plus récente (la feuille) à la tête.</summary>
    public async Task<List<Instance>> ChargerEnCoursAsync(string cleCorrelation, CancellationToken ct)
        => Vers(await bd.QueryAsync<LigneInstance>(
            $"{Selection} WHERE CLE_CORRELATION = :CleCorrelation AND STATUT = 'EN_COURS' ORDER BY ID_INSTANCE DESC",
            new { CleCorrelation = cleCorrelation }, ct));

    public async Task<Instance?> ChargerParTacheAsync(long refTache, CancellationToken ct)
        => Vers(await bd.QuerySingleOrDefaultAsync<LigneInstance>(
            $"{Selection} WHERE REF_TACHE = :RefTache AND STATUT = 'EN_COURS' AND TYPE_ATTENTE = 'TACHE'",
            new { RefTache = refTache }, ct));

    public async Task<List<Instance>> EnAttenteSignalAsync(string? cleDefinition, CancellationToken ct)
        => Vers(cleDefinition is null
            ? await bd.QueryAsync<LigneInstance>(
                $"{Selection} WHERE STATUT = 'EN_COURS' AND TYPE_ATTENTE = 'SIGNAL' ORDER BY ID_INSTANCE", null, ct)
            : await bd.QueryAsync<LigneInstance>(
                $"{Selection} WHERE STATUT = 'EN_COURS' AND TYPE_ATTENTE = 'SIGNAL' AND CLE_DEFINITION = :CleDefinition ORDER BY ID_INSTANCE",
                new { CleDefinition = cleDefinition }, ct));

    /// <summary>Instances abonnées au groupe de corrélation : seules celles-là sont lues.</summary>
    public async Task<List<Instance>> AbonneesAuGroupeAsync(string groupe, CancellationToken ct)
        => Vers(await bd.QueryAsync<LigneInstance>(
            $"{Selection} WHERE GROUPE_SIGNAL = :GroupeSignal AND STATUT = 'EN_COURS' AND TYPE_ATTENTE = 'SIGNAL' ORDER BY ID_INSTANCE",
            new { GroupeSignal = groupe }, ct));

    /// <summary>Attentes de temps et escalades dues. Le filtre sur le type d'attente suit l'index (STATUT, TYPE_ATTENTE, DATE_ECHEANCE).</summary>
    public async Task<List<Instance>> EcheancesDuesAsync(DateTime instant, CancellationToken ct)
        => Vers(await bd.QueryAsync<LigneInstance>(
            $"{Selection} WHERE STATUT = 'EN_COURS' AND TYPE_ATTENTE IN ('TEMPS', 'TACHE', 'SIGNAL') AND DATE_ECHEANCE <= :Instant ORDER BY DATE_ECHEANCE",
            new { Instant = instant }, ct));

    /// <summary>Points d'arrêt distincts des instances en cours : quelques lignes, sans les variables.</summary>
    public async Task<List<LigneArret>> ArretsAsync(CancellationToken ct)
        => (await bd.QueryAsync<LigneArret>($"""
            SELECT DISTINCT CLE_DEFINITION AS CleDefinition, NOEUD_COURANT AS NoeudCourant, TYPE_ATTENTE AS TypeAttente
              FROM {noms.TableInstance}
             WHERE STATUT = 'EN_COURS'
             ORDER BY CLE_DEFINITION, NOEUD_COURANT, TYPE_ATTENTE
            """, null, ct)).ToList();

    public async Task<List<Instance>> ArretesSurAsync(LigneArret arret, CancellationToken ct)
        => Vers(await bd.QueryAsync<LigneInstance>(
            $"{Selection} WHERE STATUT = 'EN_COURS' AND CLE_DEFINITION = :CleDefinition AND NOEUD_COURANT = :NoeudCourant AND {FiltreAttente(arret)} ORDER BY ID_INSTANCE",
            new { arret.CleDefinition, arret.NoeudCourant, arret.TypeAttente }, ct));

    /// <summary>Replace sur un nouveau nœud toutes les instances en cours d'un point d'arrêt ; retourne leur nombre.</summary>
    public Task<int> DeplacerAsync(LigneArret arret, string nouveauNoeud, CancellationToken ct)
        => bd.ExecuteNonQueryAsync($"""
            UPDATE {noms.TableInstance}
               SET NOEUD_COURANT = :NouveauNoeud, NO_VERSION = NO_VERSION + 1, DATE_MODIFICATION = :DateModification
             WHERE STATUT = 'EN_COURS' AND CLE_DEFINITION = :CleDefinition AND NOEUD_COURANT = :NoeudCourant AND {FiltreAttente(arret)}
            """,
            new { NouveauNoeud = nouveauNoeud, DateModification = Maintenant, arret.CleDefinition, arret.NoeudCourant, arret.TypeAttente }, ct);

    private static string FiltreAttente(LigneArret arret) => arret.TypeAttente is null ? "TYPE_ATTENTE IS NULL AND :TypeAttente IS NULL" : "TYPE_ATTENTE = :TypeAttente";

    public async Task<IReadOnlySet<string>> NoeudsVisitesAsync(long idInstance, CancellationToken ct)
        => (await bd.QueryAsync<string>(
            $"SELECT DISTINCT NOEUD FROM {noms.TableHistorique} WHERE ID_INSTANCE = :IdInstance", new { IdInstance = idInstance }, ct))
            .ToHashSet(StringComparer.Ordinal);

    public async Task HistoriserAsync(Instance instance, string noeud, string evenement, string? transition, string acteur, CancellationToken ct)
    {
        var id = await bd.GetSequenceAsync(noms.SequenceHistorique, ct);
        await bd.ExecuteNonQueryAsync($"""
            INSERT INTO {noms.TableHistorique} (ID_HISTORIQUE, ID_INSTANCE, NOEUD, EVENEMENT, TRANSITION, ACTEUR, DATE_EVENEMENT)
            VALUES (:IdHistorique, :IdInstance, :Noeud, :Evenement, :Transition, :Acteur, :DateEvenement)
            """,
            new
            {
                IdHistorique = id,
                IdInstance = instance.Id,
                Noeud = noeud,
                Evenement = evenement,
                Transition = transition,
                Acteur = acteur,
                DateEvenement = Maintenant,
            }, ct);
    }

    private static List<Instance> Vers(IEnumerable<LigneInstance> lignes) => lignes.Select(l => Vers(l)!).ToList();

    private static Instance? Vers(LigneInstance? ligne) => ligne is null ? null : new Instance
    {
        Id = ligne.IdInstance,
        IdAffaire = ligne.IdAffaire,
        CleCorrelation = ligne.CleCorrelation,
        CleDefinition = ligne.CleDefinition,
        IdParent = ligne.IdInstanceParent,
        NoeudCourant = ligne.NoeudCourant,
        Statut = Codes.Statut(ligne.Statut),
        NomFin = ligne.NomFin,
        Attente = Codes.Attente(ligne.TypeAttente),
        RefTache = ligne.RefTache,
        DateEcheance = ligne.DateEcheance,
        GroupeSignal = ligne.GroupeSignal,
        Variables = Variables.DeJson(ligne.Donnees),
        NoVersion = ligne.NoVersion,
        ActeurDemarrage = ligne.ActeurDemarrage ?? "",
        DateDemarrage = ligne.DateDemarrage,
        DateFin = ligne.DateFin,
    };
}
