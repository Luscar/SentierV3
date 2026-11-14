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
        var bd = new AccesBDEntrelace(banc.Bd, "UPDATE AT_INSTANCE_TAXE SET NO_VERSION = NO_VERSION + 1");
        var moteur = new MoteurSentier(new RegistreDefinitions([new DeclarationOriginale(), new Identification(), new Comptabilisation()]),
            bd, new ResolveurJournal(banc.Journal), banc.Bassin, [], banc.Options, banc.Horloge);
        await Assert.ThrowsAsync<ConflitConcurrenceException>(() => moteur.CompleterTacheAsync(etat.RefTache!.Value, "bob", "doublon"));
    }

    [Fact]
    public async Task Noeud_renomme_et_verification_au_demarrage()
    {
        using var banc = new Banc();
        var etat = await banc.Moteur.DemarrerAsync(banc.Demande());

        // Nouvelle version du code : le nœud « verification » s'appelle maintenant « controle ».
        var sansCorrespondance = banc.CreerMoteur(new Renommee(false), new Identification(), new Comptabilisation());
        var orpheline = Assert.Single(await sansCorrespondance.VerifierInstancesAsync());
        Assert.Equal("verification", orpheline.Noeud);
        await Assert.ThrowsAsync<NoeudInconnuException>(() => sansCorrespondance.CompleterTacheAsync(etat.RefTache!.Value, "bob"));

        var avecCorrespondance = banc.CreerMoteur(new Renommee(true), new Identification(), new Comptabilisation());
        Assert.Empty(await avecCorrespondance.VerifierInstancesAsync());
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
            "SELECT NOEUD || ':' || EVENEMENT FROM AT_HISTORIQUE_TAXE WHERE ID_INSTANCE = 1 ORDER BY ID_HISTORIQUE");
        Assert.Equal(
            ["valider:ENTREE", "valider:SORTIE", "notifier:ENTREE", "notifier:SORTIE", "identification:ENTREE",
             "identification:SORTIE", "a-verifier:ENTREE", "a-verifier:SORTIE", "verification:ENTREE"],
            noeuds);
    }

    private sealed class Renommee(bool correspondance) : IDefinitionProcessus
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

    /// <summary>Exécute un SQL concurrent juste avant la première mise à jour d'instance.</summary>
    private sealed class AccesBDEntrelace(IAccesBDSentier bd, string sqlConcurrent) : IAccesBDSentier
    {
        private bool _fait;

        public async Task<int> ExecuteNonQueryAsync(string requeteSQL, object? parametres = null, CancellationToken cancellationToken = default)
        {
            if (!_fait && requeteSQL.TrimStart().StartsWith("UPDATE"))
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
