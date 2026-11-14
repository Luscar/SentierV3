namespace Sentier.Tests;

public class DefinitionTests
{
    private sealed class Definition(Action<IProcessusBuilder> definir) : IDefinitionProcessus
    {
        public string Cle => "Essai";
        public void Definir(IProcessusBuilder p) => definir(p);
    }

    private static IReadOnlyList<string> Erreurs(Action<IProcessusBuilder> definir)
        => Assert.Throws<DefinitionInvalideException>(() => DefinitionProcessus.Construire(new Definition(definir))).Erreurs;

    [Fact]
    public void La_definition_de_l_esquisse_est_valide()
    {
        var definition = DefinitionProcessus.Construire(new DeclarationOriginale());
        Assert.Equal("valider", definition.Noeuds[0]);
        Assert.Equal(11, definition.Noeuds.Count);
    }

    [Fact]
    public void Parametre_obligatoire_non_fourni()
        => Assert.Contains("« langue »", Assert.Single(Erreurs(p => p
            .Systeme<EnvoyerAvis>("notifier", a => a.Valeur(EnvoyerAvis.Modele, "X"))
            .Fin("fin"))));

    [Fact]
    public void Decision_sans_branche_par_defaut_et_valeur_du_mauvais_type()
    {
        var erreurs = Erreurs(p => p
            .Decision<DoitEtreVerifiee>("d", d => d.Si("oui", "fin"))
            .Fin("fin"));
        Assert.Contains(erreurs, e => e.Contains("Boolean"));
        Assert.Contains(erreurs, e => e.Contains("Sinon"));
    }

    [Fact]
    public void Identifiant_en_double_cible_inexistante_noeud_inatteignable_et_sans_fin()
    {
        var erreurs = Erreurs(p => p
            .AttenteSignal("a", "s", s => s.Sur("x", "nulle-part"))
            .Fin("fin")
            .AttenteSignal("a", "s")
            .AttenteSignal("perdu", "s").Aller("perdu"));
        Assert.Contains(erreurs, e => e.Contains("« a » est utilisé par 2"));
        Assert.Contains(erreurs, e => e.Contains("« nulle-part », qui n'existe pas"));
        Assert.Contains(erreurs, e => e.Contains("« perdu » est inatteignable"));

        erreurs = Erreurs(p => p
            .Decision("d", Variable.Booleen("v"), d => d.Si(true, "fin").Sinon("rond"))
            .AttenteSignal("rond", "s").Aller("rond")
            .Fin("fin"));
        Assert.Contains("« rond » n'aboutit à aucune fin", Assert.Single(erreurs));
    }

    [Fact]
    public void Dernier_noeud_qui_n_est_pas_une_fin()
        => Assert.Contains(Erreurs(p => p.AttenteSignal("a", "s")), e => e.Contains("n'est pas une fin"));

    [Fact]
    public void Sous_processus_inconnu()
        => Assert.Throws<DefinitionInvalideException>(() => new RegistreDefinitions([new DeclarationOriginale()]));
}
