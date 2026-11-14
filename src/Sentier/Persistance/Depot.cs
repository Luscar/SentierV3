namespace Sentier.Persistance;

/// <summary>SQL du package, commun à Oracle et à SQLite. Seule la génération d'identifiant passe par la couche d'accès.</summary>
internal sealed class Depot(IAccesBDSentier bd, NomsSentier noms, TimeProvider horloge)
{
    private string Selection => $"""
        SELECT NO_SEQ_INSTC_PROCS AS IdInstance, ID_AFFA AS IdAffaire, CLE_CORREL AS CleCorrelation,
               CLE_DEFIN AS CleDefinition, NO_SEQ_PARN_INSTC_PROCS AS IdInstanceParent, ID_NOEUD_COUR AS NoeudCourant,
               TE_SEQ_INSTC_PROCS AS Statut, NOM_FIN AS NomFin, TYP_ATTEN AS TypeAttente, REF_TACHE AS RefTache,
               DAT_ECHNC AS DateEcheance, GROUPE_SIGNAL AS GroupeSignal, VAR_JSON AS Donnees, NO_VERSI AS NoVersion,
               LOGON_CREA AS ActeurDemarrage, DH_CREA AS DateDemarrage, DH_FIN AS DateFin
          FROM {noms.TableInstance}
        """;

    public DateTime Maintenant => horloge.GetLocalNow().DateTime;

    public async Task InsererAsync(Instance instance, CancellationToken ct)
    {
        instance.Id = await bd.GetSequenceAsync(noms.SequenceInstance, ct);
        instance.NoVersion = 1;
        await bd.ExecuteNonQueryAsync($"""
            INSERT INTO {noms.TableInstance}
                (NO_SEQ_INSTC_PROCS, ID_AFFA, CLE_CORREL, CLE_DEFIN, NO_SEQ_PARN_INSTC_PROCS, ID_NOEUD_COUR, TE_SEQ_INSTC_PROCS,
                 NOM_FIN, TYP_ATTEN, REF_TACHE, DAT_ECHNC, GROUPE_SIGNAL, VAR_JSON, NO_VERSI, LOGON_CREA,
                 DH_CREA, DH_MODIF, DH_FIN)
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
               SET ID_NOEUD_COUR = :NoeudCourant, TE_SEQ_INSTC_PROCS = :Statut, NOM_FIN = :NomFin, TYP_ATTEN = :TypeAttente,
                   REF_TACHE = :RefTache, DAT_ECHNC = :DateEcheance, GROUPE_SIGNAL = :GroupeSignal, VAR_JSON = :Donnees,
                   NO_VERSI = NO_VERSI + 1, DH_MODIF = :DateModification, DH_FIN = :DateFin
             WHERE NO_SEQ_INSTC_PROCS = :IdInstance AND NO_VERSI = :NoVersion
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
            $"{Selection} WHERE NO_SEQ_INSTC_PROCS = :IdInstance", new { IdInstance = idInstance }, ct));

    public async Task<Instance?> ChargerTeteAsync(string cleCorrelation, CancellationToken ct)
        => Vers(await bd.QuerySingleOrDefaultAsync<LigneInstance>(
            $"{Selection} WHERE CLE_CORREL = :CleCorrelation AND NO_SEQ_PARN_INSTC_PROCS IS NULL",
            new { CleCorrelation = cleCorrelation }, ct));

    /// <summary>Instances en cours de l'arbre, de la plus récente (la feuille) à la tête.</summary>
    public async Task<List<Instance>> ChargerEnCoursAsync(string cleCorrelation, CancellationToken ct)
        => Vers(await bd.QueryAsync<LigneInstance>(
            $"{Selection} WHERE CLE_CORREL = :CleCorrelation AND TE_SEQ_INSTC_PROCS = 'EN_COURS' ORDER BY NO_SEQ_INSTC_PROCS DESC",
            new { CleCorrelation = cleCorrelation }, ct));

    public async Task<Instance?> ChargerParTacheAsync(long refTache, CancellationToken ct)
        => Vers(await bd.QuerySingleOrDefaultAsync<LigneInstance>(
            $"{Selection} WHERE REF_TACHE = :RefTache AND TE_SEQ_INSTC_PROCS = 'EN_COURS' AND TYP_ATTEN = 'TACHE'",
            new { RefTache = refTache }, ct));

    public async Task<List<Instance>> EnAttenteSignalAsync(string? cleDefinition, CancellationToken ct)
        => Vers(cleDefinition is null
            ? await bd.QueryAsync<LigneInstance>(
                $"{Selection} WHERE TE_SEQ_INSTC_PROCS = 'EN_COURS' AND TYP_ATTEN = 'SIGNAL' ORDER BY NO_SEQ_INSTC_PROCS", null, ct)
            : await bd.QueryAsync<LigneInstance>(
                $"{Selection} WHERE TE_SEQ_INSTC_PROCS = 'EN_COURS' AND TYP_ATTEN = 'SIGNAL' AND CLE_DEFIN = :CleDefinition ORDER BY NO_SEQ_INSTC_PROCS",
                new { CleDefinition = cleDefinition }, ct));

    /// <summary>Instances abonnées au groupe de corrélation : seules celles-là sont lues.</summary>
    public async Task<List<Instance>> AbonneesAuGroupeAsync(string groupe, CancellationToken ct)
        => Vers(await bd.QueryAsync<LigneInstance>(
            $"{Selection} WHERE GROUPE_SIGNAL = :GroupeSignal AND TE_SEQ_INSTC_PROCS = 'EN_COURS' AND TYP_ATTEN = 'SIGNAL' ORDER BY NO_SEQ_INSTC_PROCS",
            new { GroupeSignal = groupe }, ct));

    /// <summary>Attentes de temps et escalades dues. Le filtre sur le type d'attente suit l'index (TE_SEQ_INSTC_PROCS, TYP_ATTEN, DAT_ECHNC).</summary>
    public async Task<List<Instance>> EcheancesDuesAsync(DateTime instant, CancellationToken ct)
        => Vers(await bd.QueryAsync<LigneInstance>(
            $"{Selection} WHERE TE_SEQ_INSTC_PROCS = 'EN_COURS' AND TYP_ATTEN IN ('TEMPS', 'TACHE', 'SIGNAL') AND DAT_ECHNC <= :Instant ORDER BY DAT_ECHNC",
            new { Instant = instant }, ct));

    /// <summary>Points d'arrêt distincts des instances en cours : quelques lignes, sans les variables.</summary>
    public async Task<List<LigneArret>> ArretsAsync(CancellationToken ct)
        => (await bd.QueryAsync<LigneArret>($"""
            SELECT DISTINCT CLE_DEFIN AS CleDefinition, ID_NOEUD_COUR AS NoeudCourant, TYP_ATTEN AS TypeAttente
              FROM {noms.TableInstance}
             WHERE TE_SEQ_INSTC_PROCS = 'EN_COURS'
             ORDER BY CLE_DEFIN, ID_NOEUD_COUR, TYP_ATTEN
            """, null, ct)).ToList();

    public async Task<List<Instance>> ArretesSurAsync(LigneArret arret, CancellationToken ct)
        => Vers(await bd.QueryAsync<LigneInstance>(
            $"{Selection} WHERE TE_SEQ_INSTC_PROCS = 'EN_COURS' AND CLE_DEFIN = :CleDefinition AND ID_NOEUD_COUR = :NoeudCourant AND {FiltreAttente(arret)} ORDER BY NO_SEQ_INSTC_PROCS",
            new { arret.CleDefinition, arret.NoeudCourant, arret.TypeAttente }, ct));

    /// <summary>Replace sur un nouveau nœud toutes les instances en cours d'un point d'arrêt ; retourne leur nombre.</summary>
    public Task<int> DeplacerAsync(LigneArret arret, string nouveauNoeud, CancellationToken ct)
        => bd.ExecuteNonQueryAsync($"""
            UPDATE {noms.TableInstance}
               SET ID_NOEUD_COUR = :NouveauNoeud, NO_VERSI = NO_VERSI + 1, DH_MODIF = :DateModification
             WHERE TE_SEQ_INSTC_PROCS = 'EN_COURS' AND CLE_DEFIN = :CleDefinition AND ID_NOEUD_COUR = :NoeudCourant AND {FiltreAttente(arret)}
            """,
            new { NouveauNoeud = nouveauNoeud, DateModification = Maintenant, arret.CleDefinition, arret.NoeudCourant, arret.TypeAttente }, ct);

    private static string FiltreAttente(LigneArret arret) => arret.TypeAttente is null ? "TYP_ATTEN IS NULL AND :TypeAttente IS NULL" : "TYP_ATTEN = :TypeAttente";

    /// <summary>Historique de tout l'arbre d'une clé de corrélation, dans l'ordre des événements.</summary>
    public async Task<List<LigneHistorique>> HistoriqueAsync(string cleCorrelation, CancellationToken ct)
        => (await bd.QueryAsync<LigneHistorique>($"""
            SELECT H.NO_SEQ_INSTC_PROCS AS IdInstance, I.CLE_DEFIN AS CleDefinition, H.ID_NOEUD AS Noeud, H.ID_EVEN AS Evenement,
                   H.ID_CHEM AS Transition, H.LOGON AS Acteur, H.DH_CREA AS DateEvenement
              FROM {noms.TableHistorique} H
              JOIN {noms.TableInstance} I ON I.NO_SEQ_INSTC_PROCS = H.NO_SEQ_INSTC_PROCS
             WHERE I.CLE_CORREL = :CleCorrelation
             ORDER BY H.NO_SEQ_HIST_INSTC_PROCS
            """, new { CleCorrelation = cleCorrelation }, ct)).ToList();

    public async Task<IReadOnlySet<string>> NoeudsVisitesAsync(long idInstance, CancellationToken ct)
        => (await bd.QueryAsync<string>(
            $"SELECT DISTINCT ID_NOEUD FROM {noms.TableHistorique} WHERE NO_SEQ_INSTC_PROCS = :IdInstance", new { IdInstance = idInstance }, ct))
            .ToHashSet(StringComparer.Ordinal);

    public async Task HistoriserAsync(Instance instance, string noeud, string evenement, string? transition, string acteur, CancellationToken ct)
    {
        var id = await bd.GetSequenceAsync(noms.SequenceHistorique, ct);
        await bd.ExecuteNonQueryAsync($"""
            INSERT INTO {noms.TableHistorique} (NO_SEQ_HIST_INSTC_PROCS, NO_SEQ_INSTC_PROCS, ID_NOEUD, ID_EVEN, ID_CHEM, LOGON, DH_CREA)
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
