namespace Sentier.Tests;

public class SuiviTests
{
    private static readonly IDefinitionProcessus[] SousProcessus = [new Identification(), new Comptabilisation()];

    [Fact]
    public async Task Verification_des_instances()
    {
        using var banc = new Banc();
        await banc.Moteur.DemarrerAsync(banc.Demande());
        Assert.Empty(await banc.CreerSuivi().VerifierInstancesAsync());

        // Nœud renommé sans correspondance.
        var orpheline = Assert.Single(await banc.CreerSuivi([new MoteurTests.Renommee(false), .. SousProcessus]).VerifierInstancesAsync());
        Assert.Equal("verification", orpheline.Noeud);
        Assert.Equal("cle-1", orpheline.CleCorrelation);
        Assert.Contains("n'a pas de correspondance", orpheline.Raison);

        // Correspondance vers un nœud d'une autre nature : la tâche attendue ne pourrait plus être complétée.
        orpheline = Assert.Single(await banc.CreerSuivi([new AutreNature(), .. SousProcessus]).VerifierInstancesAsync());
        Assert.Contains("« Tache »", orpheline.Raison);
        Assert.Contains("« Signal »", orpheline.Raison);

        // Définition retirée.
        orpheline = Assert.Single(await banc.CreerSuivi(new Identification()).VerifierInstancesAsync());
        Assert.Contains("« DeclOrign » n'existe plus", orpheline.Raison);

        Assert.Empty(await banc.CreerSuivi([new MoteurTests.Renommee(true), .. SousProcessus]).VerifierInstancesAsync());
    }

    [Fact]
    public async Task Migration_des_instances()
    {
        using var banc = new Banc();
        var a = await banc.Moteur.DemarrerAsync(banc.Demande("a"));
        await banc.Moteur.DemarrerAsync(banc.Demande("b"));
        await banc.Moteur.DemarrerAsync(banc.Demande("c", 10m)); // arrêtée dans le sous-processus : rien à migrer

        // Les orphelines ne sont pas touchées.
        var resultat = await banc.CreerSuivi([new MoteurTests.Renommee(false), .. SousProcessus]).MigrerInstancesAsync();
        Assert.Empty(resultat.Migrations);
        Assert.Equal(3, resultat.Orphelines.Count); // a, b, et la tête de c arrêtée sur un nœud retiré

        resultat = await banc.CreerSuivi([new MoteurTests.Renommee(true), new Identification(), new Comptabilisation()]).MigrerInstancesAsync();
        var migration = Assert.Single(resultat.Migrations);
        Assert.Equal(new MigrationNoeud("DeclOrign", "verification", "controle", 2), migration);
        Assert.Equal(2, resultat.NombreInstancesMigrees);

        // La correspondance peut maintenant être retirée du code : les instances sont sur le nouveau nœud.
        var noeuds = await banc.Bd.QueryAsync<string>(
            "SELECT NOEUD_COURANT FROM AT_INSTANCE_TAXE WHERE CLE_CORRELATION IN ('a', 'b') AND ID_INSTANCE_PARENT IS NULL ORDER BY ID_INSTANCE");
        Assert.Equal(["controle", "controle"], noeuds);
        var sansCorrespondance = banc.CreerMoteur(new MoteurTests.Renommee(false), new Identification(), new Comptabilisation());
        var suite = await sansCorrespondance.CompleterTacheAsync(a.RefTache!.Value, "bob");
        Assert.Equal(StatutInstance.Terminee, suite.Statut);

        // Relancer la migration ne fait plus rien.
        resultat = await banc.CreerSuivi([new MoteurTests.Renommee(true), .. SousProcessus]).MigrerInstancesAsync();
        Assert.Empty(resultat.Migrations);
    }

    [Fact]
    public void Diagramme_d_une_definition()
    {
        using var banc = new Banc();
        var suivi = banc.CreerSuivi();
        Assert.Equal(["DeclOrign", "Identif", "Compta"], suivi.ClesDefinitions);

        var diagramme = suivi.DiagrammeDefinition("DeclOrign");
        Assert.StartsWith("flowchart LR", diagramme);
        Assert.StartsWith("flowchart TD", suivi.DiagrammeDefinition("DeclOrign", OrientationDiagramme.Verticale));
        Assert.Contains("n0[\"valider<br/>ValiderDeclaration\"]", diagramme);
        Assert.Contains("n3{\"a-verifier<br/>DoitEtreVerifiee\"}", diagramme);
        Assert.Contains("n4[/\"verification<br/>tâche VERIF\"/]", diagramme);
        Assert.Contains("n5[[\"comptabilisation<br/>Compta\"]]", diagramme);
        Assert.Contains("n7{{\"delai-grace<br/>2 j\"}}", diagramme);
        Assert.Contains("n9(((\"fin-doublon<br/>doublon\")))", diagramme);
        Assert.Contains("n3 -->|\"True\"| n4", diagramme);
        Assert.Contains("n3 -->|\"sinon\"| n5", diagramme);
        Assert.Contains("n4 -->|\"refusee\"| n0", diagramme);
        Assert.Contains("n6 -->|\"paiement-recu\"| n7", diagramme);
        Assert.Contains("n6 -->|\"paiement-refuse\"| n10", diagramme);
        Assert.DoesNotContain("classDef", diagramme);
        Assert.Throws<DefinitionInconnueException>(() => suivi.DiagrammeDefinition("absente"));
    }

    [Fact]
    public async Task Diagramme_d_une_instance()
    {
        using var banc = new Banc();
        var suivi = banc.CreerSuivi();
        Assert.Null(await suivi.DiagrammeInstanceAsync("absente"));

        // Arrêtée dans un sous-processus : la tête puis l'enfant, chacun avec son nœud courant.
        await banc.Moteur.DemarrerAsync(banc.Demande("x", 10m));
        var diagramme = await suivi.DiagrammeInstanceAsync("x");
        Assert.StartsWith("flowchart LR", diagramme);
        Assert.Contains("subgraph i0[\"DeclOrign — instance 1 (en cours)\"]\n        direction LR", diagramme);
        Assert.Contains("subgraph i1[\"Compta — instance 3 (en cours)\"]", diagramme);
        Assert.Contains("i0n0[\"valider<br/>ValiderDeclaration\"]:::visite", diagramme);
        Assert.Contains("i0n5[[\"comptabilisation<br/>Compta\"]]:::courant", diagramme);
        Assert.Contains("i0n4[/\"verification<br/>tâche VERIF\"/]\n", diagramme); // jamais traversé
        Assert.Contains("i1n0[/\"comptabiliser<br/>tâche COMPTA\"/]:::courant", diagramme);
        Assert.Contains("classDef courant", diagramme);

        // Terminée : seule la tête, arrêtée sur sa fin.
        var etat = await banc.Moteur.DemarrerAsync(banc.Demande("y"));
        await banc.Moteur.CompleterTacheAsync(etat.RefTache!.Value, "bob", "doublon");
        diagramme = await suivi.DiagrammeInstanceAsync("y");
        Assert.Contains("(terminée)", diagramme);
        Assert.Contains("i0n9(((\"fin-doublon<br/>doublon\"))):::courant", diagramme);
        Assert.DoesNotContain("subgraph i1", diagramme);
    }

    [Fact]
    public async Task Pages_html()
    {
        using var banc = new Banc();
        banc.Options.UrlMermaid = "https://intranet.exemple/mermaid.mjs";
        var suivi = banc.CreerSuivi([new MoteurTests.Renommee(true), .. SousProcessus]);

        var page = suivi.PageDefinition("DeclOrign");
        Assert.StartsWith("<!doctype html>", page);
        Assert.Contains("<title>Définition DeclOrign</title>", page);
        Assert.Contains("import mermaid from \"https://intranet.exemple/mermaid.mjs\"", page);
        // Le diagramme est encodé dans la page : le navigateur le rend à Mermaid tel quel.
        Assert.Contains("<pre class=\"mermaid\">\nflowchart LR\n", page);
        Assert.Contains("n0[/&quot;controle&lt;br/&gt;tâche VERIF&quot;/]", page);
        Assert.Contains("<td><code>controle</code></td><td>Interactif</td><td>Tâche VERIF</td><td><code>cotisee</code></td>", page);
        Assert.Contains("<td><code>verification</code></td><td><code>controle</code></td>", page); // correspondance

        suivi = banc.CreerSuivi();
        Assert.Null(await suivi.PageInstanceAsync("absente"));
        await banc.Moteur.DemarrerAsync(banc.Demande("x<1>", 10m)); // arrêtée dans le sous-processus
        page = (await suivi.PageInstanceAsync("x<1>"))!;
        Assert.Contains("<title>Instance x&lt;1&gt;</title>", page);
        Assert.DoesNotContain("x<1>", page);
        Assert.Contains("<h2>Variables de l'instance 3</h2>", page);
        Assert.Contains("<td><code>langue</code></td><td><code>&quot;fr&quot;</code></td>", page);
        Assert.Contains("<h2>Nœuds de Compta</h2>", page);
        Assert.Contains("<tr class=\"courant\"><td><code>comptabiliser</code></td>", page);
        Assert.Contains("<td><code>a-verifier</code></td><td>SORTIE</td><td><code>False</code></td><td>alice</td>", page);
        Assert.Contains(":::courant", page);
    }

    [Fact]
    public async Task Repositionnement_d_une_instance()
    {
        using var banc = new Banc();
        var suivi = banc.CreerSuivi();
        var etat = await banc.Moteur.DemarrerAsync(banc.Demande());
        var tache = etat.RefTache!.Value;
        banc.Journal.Lignes.Clear();

        // Vers un point d'attente plus loin : la tâche ouverte est annulée, sans action « après ».
        etat = await suivi.RepositionnerAsync("cle-1", "attente-paiement", "support");
        Assert.Equal("attente-paiement", etat.NoeudCourant);
        Assert.Equal(TypeAttente.Signal, etat.TypeAttente);
        Assert.Null(etat.RefTache);
        Assert.Equal([tache], banc.Bassin.Annulees);
        Assert.Empty(banc.Journal.Lignes);
        await Assert.ThrowsAsync<TacheIntrouvableException>(() => banc.Moteur.CompleterTacheAsync(tache, "bob"));

        // Vers un nœud systémique : l'exécution reprend de là, jusqu'au prochain point d'attente.
        etat = await suivi.RepositionnerAsync("cle-1", "valider", "support",
            new Donnees().Avec(DeclarationOriginale.Montant, 10m)); // variable corrigée : la décision saute la vérification
        Assert.Equal("comptabiliser", etat.NoeudCourant);
        Assert.Equal("Compta", etat.CleDefinitionActive);
        Assert.Equal("valider:42:support", banc.Journal.Lignes[0]);

        // L'instance active est le sous-processus : le nœud visé est cherché dans sa définition.
        await Assert.ThrowsAsync<NoeudInconnuException>(() => suivi.RepositionnerAsync("cle-1", "valider", "support"));
        etat = await suivi.RepositionnerAsync("cle-1", "comptabilisee", "support");
        Assert.Equal("attente-paiement", etat.NoeudCourant); // l'enfant terminé, le parent a repris

        await Assert.ThrowsAsync<SentierException>(() => suivi.RepositionnerAsync("absente", "valider", "support"));
        await banc.Moteur.AnnulerAsync("cle-1", "chef");
        await Assert.ThrowsAsync<SentierException>(() => suivi.RepositionnerAsync("cle-1", "valider", "support"));
    }

    [Fact]
    public async Task Repositionnement_d_une_instance_orpheline()
    {
        using var banc = new Banc();
        await banc.Moteur.DemarrerAsync(banc.Demande());

        // Le nœud « verification » n'existe plus et n'a pas de correspondance : le support replace l'instance.
        var suivi = banc.CreerSuivi([new MoteurTests.Renommee(false), .. SousProcessus]);
        Assert.Single(await suivi.VerifierInstancesAsync());
        var etat = await suivi.RepositionnerAsync("cle-1", "controle", "support");
        Assert.Equal("controle", etat.NoeudCourant);
        Assert.Empty(await suivi.VerifierInstancesAsync());
    }

    [Fact]
    public async Task Lecture_de_l_historique()
    {
        using var banc = new Banc();
        var suivi = banc.CreerSuivi();
        Assert.Empty(await suivi.LireHistoriqueAsync("x"));

        await banc.Moteur.DemarrerAsync(banc.Demande("x", 10m)); // arrêtée dans le sous-processus « Compta »
        await suivi.RepositionnerAsync("x", "comptabilisee", "support");
        await banc.Moteur.AnnulerAsync("x", "chef");

        var historique = await suivi.LireHistoriqueAsync("x");
        Assert.Equal(new EvenementHistorique(1, "DeclOrign", "valider", "ENTREE", null, "alice", banc.Horloge.Maintenant.DateTime), historique[0]);
        Assert.Equal(["DeclOrign", "Identif", "Compta"], historique.Select(e => e.CleDefinition).Distinct());
        Assert.Contains(historique, e => e is { Noeud: "a-verifier", Evenement: "SORTIE", Transition: "False" });
        Assert.Contains(historique, e => e is { Noeud: "comptabiliser", Evenement: "SORTIE", Transition: "repositionnement", Acteur: "support" });
        Assert.Equal(new { Noeud = "attente-paiement", Evenement = "ANNULATION", Acteur = (string?)"chef" },
            historique.Select(e => new { e.Noeud, e.Evenement, e.Acteur }).Last());
    }

    /// <summary>La tâche « verification » est remplacée par une attente de signal : la correspondance ne peut pas s'appliquer.</summary>
    private sealed class AutreNature : IDefinitionProcessus
    {
        public string Cle => "DeclOrign";

        public void Definir(IProcessusBuilder p) => p
            .AttenteSignal("controle", "controle-fait")
            .Fin("cotisee")
            .Correspondance("verification", "controle");
    }
}
