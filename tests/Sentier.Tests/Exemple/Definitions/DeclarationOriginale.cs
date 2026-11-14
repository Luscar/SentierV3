namespace Sentier.Tests;

/// <summary>Définition d'essai, calquée sur l'esquisse du document de besoins.</summary>
public sealed class DeclarationOriginale : IDefinitionProcessus
{
    public static readonly Variable<string> Langue = Variable.Texte("langue");
    public static readonly Variable<decimal> Montant = Variable.Decimal("montant");
    public static readonly Variable<bool> Verifiee = Variable.Booleen("verifiee");
    public static readonly Variable<string> Reference = Variable.Texte("reference");

    public string Cle => "DeclOrign";

    public void Definir(IProcessusBuilder p) => p
        .Systeme<ValiderDeclaration>("valider")
        .Systeme<EnvoyerAvis>("notifier", a => a
            .Valeur(EnvoyerAvis.Modele, "AVIS_RECEPTION")
            .Variable(EnvoyerAvis.Langue, Langue))
        .SousProcessus("identification", Identification.CleDefinition)
        .Decision<DoitEtreVerifiee>("a-verifier", d => d
            .Si(true, "verification")
            .Sinon("comptabilisation"))
        .Interactif("verification", "VERIF", t => t
            .Avant<PreparerVerification>()
            .Apres<ConsignerVerification>()
            .Sur("refusee", "valider")
            .Sur("doublon", "fin-doublon"))
        .SousProcessus("comptabilisation", Comptabilisation.CleDefinition)
        .AttenteSignal("attente-paiement", "paiement-recu", s => s
            .Signal("paiement-refuse", "fin-annulee"))
        .AttenteTemps("delai-grace", TimeSpan.FromDays(2))
        .Fin("cotisee")

        .Fin("fin-doublon", "doublon")
        .Fin("fin-annulee", "annulee");
}
