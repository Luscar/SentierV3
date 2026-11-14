using System.Transactions;

namespace Sentier.Tests;

public class MoteurTests
{
    [Fact]
    public async Task Parcours_complet_de_la_declaration()
    {
        using var banc = new Banc();
        var moteur = banc.Moteur;

        // Démarrage : commandes, sous-processus sans attente et décision dans le même appel, jusqu'à la tâche.
        var etat = await moteur.DemarrerAsync(banc.Demande() with { AssignerPremiereTache = true });
        Assert.True(etat.EnCours);
        Assert.Equal("verification", etat.NoeudCourant);
        Assert.Equal(TypeAttente.Tache, etat.TypeAttente);
        Assert.Equal("VERIF", etat.CodeTache);
        Assert.Equal(101, etat.RefTache);
        Assert.Equal(["valider:42:alice", "avis:AVIS_RECEPTION:fr", "valider:42:alice", "avant-verification"], banc.Journal.Lignes);
        var tache = Assert.Single(banc.Bassin.Creees);
        Assert.True(tache.AssignerAActeur);
        Assert.Equal(42, tache.IdAffaire);
        var finEnfant = Assert.Single(banc.Ecouteur.Fins);
        Assert.True(finEnfant.EstSousProcessus);
        Assert.Equal("identifiee", finEnfant.NomFin);

        // Complétion : action « après », puis sous-processus qui attend à son tour.
        etat = await moteur.CompleterTacheAsync(101, "bob", donnees: new Donnees().Avec(DeclarationOriginale.Reference, "R-1"));
        Assert.Equal("comptabiliser", etat.NoeudCourant);
        Assert.Equal("Compta", etat.CleDefinitionActive);
        Assert.Equal("DeclOrign", etat.CleDefinition);
        Assert.NotEqual(etat.IdInstance, etat.IdInstanceActive);
        Assert.Equal("COMPTA", etat.CodeTache);
        Assert.Equal("apres-verification", banc.Journal.Lignes[^1]);
        Assert.False(banc.Bassin.Creees[^1].AssignerAActeur);

        // Compléter deux fois la même tâche ne fait pas avancer deux fois.
        await Assert.ThrowsAsync<TacheIntrouvableException>(() => moteur.CompleterTacheAsync(101, "bob"));

        // Fin du sous-processus : le parent reprend, avec les variables écrites en chemin.
        etat = await moteur.CompleterTacheAsync(102, "bob");
        Assert.Equal("attente-paiement", etat.NoeudCourant);
        Assert.Equal(etat.IdInstance, etat.IdInstanceActive);
        Assert.Equal(["paiement-recu", "paiement-refuse"], etat.SignauxAttendus);
        Assert.True(etat.Variables.Lire(DeclarationOriginale.Verifiee));
        Assert.Equal("R-1", etat.Variables.Lire(DeclarationOriginale.Reference));

        // Signal que rien n'attend : pas d'erreur.
        var signal = await moteur.SignalerAsync("cle-1", "inconnu", "sys");
        Assert.False(signal.Traitee);
        signal = await moteur.SignalerAsync("cle-1", "paiement-recu", "sys", cleDefinition: "Compta");
        Assert.False(signal.Traitee);

        signal = await moteur.SignalerAsync("cle-1", "paiement-recu", "sys");
        Assert.True(signal.Traitee);
        Assert.Equal("delai-grace", signal.Etat!.NoeudCourant);
        Assert.Equal(banc.Horloge.Maintenant.DateTime.AddDays(2), signal.Etat.DateEcheance);

        // Échéance : rien avant la date, puis déclenchement une seule fois.
        Assert.Empty(await moteur.ListerEcheancesDuesAsync(banc.Horloge.Maintenant.DateTime));
        Assert.False((await moteur.DeclencherEcheanceAsync(etat.IdInstance, "sys")).Traitee);
        banc.Horloge.Maintenant = banc.Horloge.Maintenant.AddDays(3);
        var echeance = Assert.Single(await moteur.ListerEcheancesDuesAsync(banc.Horloge.Maintenant.DateTime));
        var fin = await moteur.DeclencherEcheanceAsync(echeance.IdInstance, "sys");
        Assert.True(fin.Traitee);
        Assert.Equal(StatutInstance.Terminee, fin.Etat!.Statut);
        Assert.Equal("cotisee", fin.Etat.NomFin);
        Assert.False((await moteur.DeclencherEcheanceAsync(echeance.IdInstance, "sys")).Traitee);

        var derniere = banc.Ecouteur.Fins[^1];
        Assert.False(derniere.EstSousProcessus);
        Assert.Equal("cotisee", derniere.NomFin);

        var consulte = await moteur.ObtenirEtatAsync("cle-1");
        Assert.Equal(StatutInstance.Terminee, consulte!.Statut);
        Assert.Null(await moteur.ObtenirEtatAsync("absente"));
    }

    [Fact]
    public async Task Decision_et_transitions_nommees()
    {
        using var banc = new Banc();

        // Montant bas : la décision saute la vérification et va au sous-processus.
        var etat = await banc.Moteur.DemarrerAsync(banc.Demande("bas", 10m));
        Assert.Equal("comptabiliser", etat.NoeudCourant);

        // Transition nommée vers une autre fin.
        etat = await banc.Moteur.DemarrerAsync(banc.Demande("haut"));
        etat = await banc.Moteur.CompleterTacheAsync(etat.RefTache!.Value, "bob", "doublon");
        Assert.Equal(StatutInstance.Terminee, etat.Statut);
        Assert.Equal("doublon", etat.NomFin);

        // Retour en arrière : la tâche refusée ramène au début, donc à une nouvelle tâche.
        etat = await banc.Moteur.DemarrerAsync(banc.Demande("retour"));
        var suite = await banc.Moteur.CompleterTacheAsync(etat.RefTache!.Value, "bob", "refusee");
        Assert.Equal("verification", suite.NoeudCourant);
        Assert.NotEqual(etat.RefTache, suite.RefTache);

        await Assert.ThrowsAsync<TransitionInconnueException>(() => banc.Moteur.CompleterTacheAsync(suite.RefTache!.Value, "bob", "nimporte"));
        await Assert.ThrowsAsync<CleCorrelationDejaUtiliseeException>(() => banc.Moteur.DemarrerAsync(banc.Demande("retour")));
    }

    [Fact]
    public async Task Signal_diffuse_et_signal_avec_son_propre_noeud()
    {
        using var banc = new Banc();
        foreach (var cle in new[] { "a", "b" })
        {
            var etat = await banc.Moteur.DemarrerAsync(banc.Demande(cle, 10m));
            await banc.Moteur.CompleterTacheAsync(etat.RefTache!.Value, "bob");
        }

        var etats = await banc.Moteur.DiffuserSignalAsync("paiement-refuse", "sys");
        Assert.Equal(2, etats.Count);
        Assert.All(etats, e => Assert.Equal("annulee", e.NomFin));
        Assert.Empty(await banc.Moteur.DiffuserSignalAsync("paiement-refuse", "sys"));
    }

    [Fact]
    public async Task Annulation_de_l_arbre()
    {
        using var banc = new Banc();
        var etat = await banc.Moteur.DemarrerAsync(banc.Demande("x", 10m)); // arrêté dans le sous-processus
        var annule = await banc.Moteur.AnnulerAsync("x", "chef");

        Assert.Equal(StatutInstance.Annulee, annule!.Statut);
        Assert.Equal([etat.RefTache!.Value], banc.Bassin.Annulees);
        Assert.Equal(2, banc.Ecouteur.Fins.Count(f => f.Statut == StatutInstance.Annulee));
        await Assert.ThrowsAsync<TacheIntrouvableException>(() => banc.Moteur.CompleterTacheAsync(etat.RefTache.Value, "bob"));

        // L'historique dit où chaque instance a été annulée, de la feuille vers la tête.
        var annulations = await banc.Bd.QueryAsync<string>(
            "SELECT ID_NOEUD || ':' || LOGON FROM AT_HIST_INSTC_PROCS_TAXE WHERE ID_EVEN = 'ANNULATION' ORDER BY NO_SEQ_HIST_INSTC_PROCS");
        Assert.Equal(["comptabiliser:chef", "comptabilisation:chef"], annulations);
    }

    [Fact]
    public async Task Attente_de_temps_sur_une_variable_absente()
    {
        using var banc = new Banc(new Rappel());
        await Assert.ThrowsAsync<SentierException>(() => banc.Moteur.DemarrerAsync(new DemandeDemarrage("Rappel", 1, "r1", "alice")));

        var date = banc.Horloge.Maintenant.DateTime.AddDays(3);
        var etat = await banc.Moteur.DemarrerAsync(
            new DemandeDemarrage("Rappel", 1, "r2", "alice") { Donnees = new Donnees().Avec(Rappel.Date, date) });
        Assert.Equal(date, etat.DateEcheance);
    }

    [Fact]
    public async Task Code_inconnu_en_base()
    {
        using var banc = new Banc();
        await banc.Moteur.DemarrerAsync(banc.Demande());
        await banc.Bd.ExecuteNonQueryAsync("UPDATE AT_INSTC_PROCS_TAXE SET TE_SEQ_INSTC_PROCS = 'BIZARRE'");
        await Assert.ThrowsAsync<SentierException>(() => banc.Moteur.ObtenirEtatAsync("cle-1"));
    }

    private sealed class Rappel : IDefinitionProcessus
    {
        public static readonly Variable<DateTime> Date = Variable.Date("date");
        public string Cle => "Rappel";

        public void Definir(IProcessusBuilder p) => p.AttenteTemps("attendre", Date).Fin("fin");
    }

    [Fact]
    public async Task Tout_ou_rien_avec_le_TransactionScope_du_client()
    {
        using var banc = new Banc();
        using (var portee = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            banc.Connexion.EnlistTransaction(Transaction.Current);
            await banc.Moteur.DemarrerAsync(banc.Demande("tx"));
            Assert.NotNull(await banc.Moteur.ObtenirEtatAsync("tx"));
            // pas de Complete() : le client annule
        }
        Assert.Null(await banc.Moteur.ObtenirEtatAsync("tx"));
    }

    [Fact]
    public async Task Deux_appels_simultanes_sur_la_meme_instance()
    {
        using var banc = new Banc();
        var etat = await banc.Moteur.DemarrerAsync(banc.Demande());

        // Un autre appel a modifié l'instance entre la lecture et l'écriture.
        var bd = new AccesBDEntrelace(banc.Bd, "UPDATE", "UPDATE AT_INSTC_PROCS_TAXE SET NO_VERSI = NO_VERSI + 1");
        var moteur = new MoteurSentier(new RegistreDefinitions([new DeclarationOriginale(), new Identification(), new Comptabilisation()]),
            bd, new ResolveurJournal(banc.Journal), banc.Bassin, [], banc.Options, banc.Horloge);
        await Assert.ThrowsAsync<ConflitConcurrenceException>(() => moteur.CompleterTacheAsync(etat.RefTache!.Value, "bob", "doublon"));
    }

    [Fact]
    public async Task Deux_demarrages_simultanes_avec_la_meme_cle()
    {
        using var banc = new Banc();

        // Un autre appel a démarré une instance avec la même clé entre la vérification et l'insertion.
        var bd = new AccesBDEntrelace(banc.Bd, "INSERT INTO AT_INSTC_PROCS_TAXE", """
            INSERT INTO AT_INSTC_PROCS_TAXE (NO_SEQ_INSTC_PROCS, ID_AFFA, CLE_CORREL, CLE_DEFIN, ID_NOEUD_COUR, TE_SEQ_INSTC_PROCS,
                                          VAR_JSON, NO_VERSI, DH_CREA, DH_MODIF)
            VALUES (999, 42, 'cle-1', 'DeclOrign', 'verification', 'EN_COURS', '{}', 1, '2026-01-15', '2026-01-15')
            """);
        var moteur = new MoteurSentier(new RegistreDefinitions([new DeclarationOriginale(), new Identification(), new Comptabilisation()]),
            bd, new ResolveurJournal(banc.Journal), banc.Bassin, [], banc.Options, banc.Horloge);
        var erreur = await Assert.ThrowsAsync<CleCorrelationDejaUtiliseeException>(() => moteur.DemarrerAsync(banc.Demande()));
        Assert.NotNull(erreur.InnerException); // le refus vient de l'index unique de la base

        var tetes = await banc.Bd.QueryAsync<long>(
            "SELECT NO_SEQ_INSTC_PROCS FROM AT_INSTC_PROCS_TAXE WHERE CLE_CORREL = 'cle-1' AND NO_SEQ_PARN_INSTC_PROCS IS NULL");
        Assert.Equal([999L], tetes);

        // Les sous-processus partagent la clé de leur tête sans heurter l'index.
        var etat = await banc.Moteur.DemarrerAsync(banc.Demande("autre", 10m));
        Assert.NotEqual(etat.IdInstance, etat.IdInstanceActive);
    }

    [Fact]
    public async Task Noeud_renomme()
    {
        using var banc = new Banc();
        var etat = await banc.Moteur.DemarrerAsync(banc.Demande());

        // Nouvelle version du code : le nœud « verification » s'appelle maintenant « controle ».
        var sansCorrespondance = banc.CreerMoteur(new Renommee(false), new Identification(), new Comptabilisation());
        await Assert.ThrowsAsync<NoeudInconnuException>(() => sansCorrespondance.CompleterTacheAsync(etat.RefTache!.Value, "bob"));

        var avecCorrespondance = banc.CreerMoteur(new Renommee(true), new Identification(), new Comptabilisation());
        Assert.Equal("controle", (await avecCorrespondance.ObtenirEtatAsync("cle-1"))!.NoeudCourant);
        var suite = await avecCorrespondance.CompleterTacheAsync(etat.RefTache!.Value, "bob");
        Assert.Equal(StatutInstance.Terminee, suite.Statut);
    }

    [Fact]
    public async Task Garde_contre_les_boucles_sans_fin()
    {
        using var banc = new Banc(new Boucle());
        await Assert.ThrowsAsync<BoucleSansFinException>(() => banc.Moteur.DemarrerAsync(new DemandeDemarrage("Boucle", 1, "b", "alice")));
    }

    [Fact]
    public async Task Historique_des_passages()
    {
        using var banc = new Banc();
        await banc.Moteur.DemarrerAsync(banc.Demande());
        var noeuds = await banc.Bd.QueryAsync<string>(
            "SELECT ID_NOEUD || ':' || ID_EVEN FROM AT_HIST_INSTC_PROCS_TAXE WHERE NO_SEQ_INSTC_PROCS = 1 ORDER BY NO_SEQ_HIST_INSTC_PROCS");
        Assert.Equal(
            ["valider:ENTREE", "valider:SORTIE", "notifier:ENTREE", "notifier:SORTIE", "identification:ENTREE",
             "identification:SORTIE", "a-verifier:ENTREE", "a-verifier:SORTIE", "verification:ENTREE"],
            noeuds);
    }

    [Fact]
    public async Task Abonnement_a_un_groupe_de_correlation()
    {
        using var banc = new Banc(new Blocage());
        DemandeDemarrage Demande(string cle, string groupe)
            => new("Blocage", 1, cle, "alice") { Donnees = new Donnees().Avec(Blocage.Groupe, groupe) };

        var etat = await banc.Moteur.DemarrerAsync(Demande("a", "EMP-1"));
        Assert.Equal("EMP-1", etat.GroupeSignal);
        await banc.Moteur.DemarrerAsync(Demande("b", "EMP-1"));
        await banc.Moteur.DemarrerAsync(Demande("c", "EMP-2"));

        // Seules les instances abonnées au groupe avancent ; l'abonnement cesse avec l'attente.
        Assert.Empty(await banc.Moteur.SignalerGroupeAsync("EMP-1", "inconnu", "sys"));
        var etats = await banc.Moteur.SignalerGroupeAsync("EMP-1", "debloquee", "sys");
        Assert.Equal(["a", "b"], etats.Select(e => e.CleCorrelation));
        Assert.All(etats, e => Assert.Equal(StatutInstance.Terminee, e.Statut));
        Assert.Empty(await banc.Moteur.SignalerGroupeAsync("EMP-1", "debloquee", "sys"));

        var reste = await banc.Moteur.ObtenirEtatAsync("c");
        Assert.True(reste!.EnCours);
        Assert.Equal("EMP-2", reste.GroupeSignal);

        // Le signal ciblé par clé de corrélation atteint toujours le nœud, et désabonne l'instance.
        Assert.True((await banc.Moteur.SignalerAsync("c", "debloquee", "sys")).Traitee);
        Assert.Empty(await banc.Moteur.SignalerGroupeAsync("EMP-2", "debloquee", "sys"));

        // Variable du groupe absente à l'arrivée sur le nœud : erreur plutôt qu'un abonnement vide.
        await Assert.ThrowsAsync<SentierException>(() => banc.Moteur.DemarrerAsync(new DemandeDemarrage("Blocage", 1, "d", "alice")));
    }

    [Fact]
    public async Task Escalade_d_une_attente_de_signal_et_d_une_tache()
    {
        using var banc = new Banc(new Escalades());
        var moteur = banc.Moteur;
        var debut = banc.Horloge.Maintenant.DateTime;

        var etat = await moteur.DemarrerAsync(new DemandeDemarrage("Escalades", 1, "e", "alice"));
        Assert.Equal("attente", etat.NoeudCourant);
        Assert.Equal(debut.AddDays(5), etat.DateEcheance);

        // Pas encore due : rien ne bouge.
        Assert.Empty(await moteur.ListerEcheancesDuesAsync(banc.Horloge.Maintenant.DateTime));
        Assert.False((await moteur.DeclencherEcheanceAsync(etat.IdInstance, "sys")).Traitee);

        // Délai dépassé sans signal : l'instance va au nœud d'escalade, ici une tâche avec sa propre échéance.
        banc.Horloge.Maintenant = banc.Horloge.Maintenant.AddDays(6);
        var due = Assert.Single(await moteur.ListerEcheancesDuesAsync(banc.Horloge.Maintenant.DateTime));
        var escalade = await moteur.DeclencherEcheanceAsync(due.IdInstance, "sys");
        Assert.True(escalade.Traitee);
        Assert.Equal("relance", escalade.Etat!.NoeudCourant);
        Assert.Null(escalade.Etat.GroupeSignal);
        Assert.Equal(banc.Horloge.Maintenant.DateTime.AddDays(1), escalade.Etat.DateEcheance);
        var refTache = escalade.Etat.RefTache!.Value;

        // Délai de la tâche dépassé : elle est annulée dans le bassin, sans action « après ».
        banc.Horloge.Maintenant = banc.Horloge.Maintenant.AddDays(2);
        var fin = await moteur.DeclencherEcheanceAsync(etat.IdInstance, "sys");
        Assert.Equal("abandonnee", fin.Etat!.NomFin);
        Assert.Equal([refTache], banc.Bassin.Annulees);
        Assert.Empty(banc.Journal.Lignes);
        await Assert.ThrowsAsync<TacheIntrouvableException>(() => moteur.CompleterTacheAsync(refTache, "bob"));

        var transitions = await banc.Bd.QueryAsync<string>(
            "SELECT ID_NOEUD FROM AT_HIST_INSTC_PROCS_TAXE WHERE ID_CHEM = 'echeance' ORDER BY NO_SEQ_HIST_INSTC_PROCS");
        Assert.Equal(["attente", "relance"], transitions);
    }

    [Fact]
    public async Task Une_attente_quittee_a_temps_n_a_plus_d_echeance()
    {
        using var banc = new Banc(new Escalades());
        var etat = await banc.Moteur.DemarrerAsync(new DemandeDemarrage("Escalades", 1, "e", "alice"));
        var signal = await banc.Moteur.SignalerAsync("e", "reponse", "sys");
        Assert.Equal("traitee", signal.Etat!.NomFin);

        banc.Horloge.Maintenant = banc.Horloge.Maintenant.AddDays(6);
        Assert.Empty(await banc.Moteur.ListerEcheancesDuesAsync(banc.Horloge.Maintenant.DateTime));
        Assert.False((await banc.Moteur.DeclencherEcheanceAsync(etat.IdInstance, "sys")).Traitee);
    }

    [Fact]
    public async Task Echeance_retiree_de_la_definition()
    {
        using var banc = new Banc(new Escalades());
        var etat = await banc.Moteur.DemarrerAsync(new DemandeDemarrage("Escalades", 1, "e", "alice"));
        banc.Horloge.Maintenant = banc.Horloge.Maintenant.AddDays(6);

        // Nouvelle version du code, sans échéance : l'instance continue d'attendre et n'est plus listée.
        var moteur = banc.CreerMoteur(new Escalades(false));
        Assert.False((await moteur.DeclencherEcheanceAsync(etat.IdInstance, "sys")).Traitee);
        Assert.Empty(await moteur.ListerEcheancesDuesAsync(banc.Horloge.Maintenant.DateTime));
        Assert.Equal("attente", (await moteur.ObtenirEtatAsync("e"))!.NoeudCourant);
    }

    private sealed class Blocage : IDefinitionProcessus
    {
        public static readonly Variable<string> Groupe = Variable.Texte("groupe");
        public string Cle => "Blocage";

        public void Definir(IProcessusBuilder p) => p
            .AttenteSignal("attente-deblocage", "debloquee", s => s.Groupe(Groupe))
            .Fin("cotisee");
    }

    private sealed class Escalades(bool avecEcheance = true) : IDefinitionProcessus
    {
        public string Cle => "Escalades";

        public void Definir(IProcessusBuilder p)
        {
            if (!avecEcheance)
            {
                p.AttenteSignal("attente", "reponse", s => s.Groupe("LOT")).Fin("traitee");
                return;
            }

            p.AttenteSignal("attente", "reponse", s => s
                    .Groupe("LOT")
                    .Echeance(TimeSpan.FromDays(5), "relance"))
                .Fin("traitee")

                .Interactif("relance", "RELANCE", t => t
                    .Apres<ConsignerVerification>()
                    .Echeance(TimeSpan.FromDays(1), "fin-abandon"))
                .Aller("traitee")
                .Fin("fin-abandon", "abandonnee");
        }
    }

    internal sealed class Renommee(bool correspondance) : IDefinitionProcessus
    {
        public string Cle => "DeclOrign";

        public void Definir(IProcessusBuilder p)
        {
            p.Interactif("controle", "VERIF").Fin("cotisee");
            if (correspondance) p.Correspondance("verification", "controle");
        }
    }

    private sealed class Boucle : IDefinitionProcessus
    {
        public static readonly Variable<bool> Sortir = Variable.Booleen("sortir");
        public string Cle => "Boucle";

        public void Definir(IProcessusBuilder p) => p
            .Decision("tourner", Sortir, d => d.Si(true, "fin").Sinon("tourner"))
            .Fin("fin");
    }

    private sealed class ResolveurJournal(Journal journal) : IResolveurSentier
    {
        public object Resoudre(Type type) => Activator.CreateInstance(type, journal)!;
    }

    /// <summary>Exécute un SQL concurrent juste avant la première commande qui commence par <paramref name="avant"/>.</summary>
    private sealed class AccesBDEntrelace(IAccesBDSentier bd, string avant, string sqlConcurrent) : IAccesBDSentier
    {
        private bool _fait;

        public async Task<int> ExecuteNonQueryAsync(string requeteSQL, object? parametres = null, CancellationToken cancellationToken = default)
        {
            if (!_fait && requeteSQL.TrimStart().StartsWith(avant))
            {
                _fait = true;
                await bd.ExecuteNonQueryAsync(sqlConcurrent, null, cancellationToken);
            }
            return await bd.ExecuteNonQueryAsync(requeteSQL, parametres, cancellationToken);
        }

        public Task<IEnumerable<T>> QueryAsync<T>(string requeteSQL, object? parametres = null, CancellationToken cancellationToken = default)
            => bd.QueryAsync<T>(requeteSQL, parametres, cancellationToken);

        public Task<T?> QuerySingleOrDefaultAsync<T>(string requeteSQL, object? parametres = null, CancellationToken cancellationToken = default)
            => bd.QuerySingleOrDefaultAsync<T>(requeteSQL, parametres, cancellationToken);

        public Task<long> GetSequenceAsync(string sequence, CancellationToken cancellationToken = default)
            => bd.GetSequenceAsync(sequence, cancellationToken);
    }
}
