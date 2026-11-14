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
        Assert.StartsWith("flowchart TD", diagramme);
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
        Assert.Contains("subgraph i0[\"DeclOrign — instance 1 (en cours)\"]", diagramme);
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
