namespace Sentier;

/// <summary>Configuration du package.</summary>
public sealed class OptionsSentier
{
    /// <summary>Préfixe des tables et des séquences : {PRÉFIXE}_{nom}_{SUFFIXE}.</summary>
    public string Prefixe { get; set; } = "";
    public string Suffixe { get; set; } = "";
    /// <summary>Consigne chaque passage de nœud dans la table HISTORIQUE.</summary>
    public bool HistoriqueActif { get; set; }
    /// <summary>Garde contre les boucles sans fin : nombre maximal de nœuds exécutés par appel.</summary>
    public int NombreMaxEtapes { get; set; } = 1000;
    /// <summary>
    /// Adresse du module Mermaid (ESM) chargé par les pages HTML du suivi. Sans accès à Internet depuis le
    /// navigateur, hébergez le fichier et indiquez ici son adresse.
    /// </summary>
    public string UrlMermaid { get; set; } = "https://cdn.jsdelivr.net/npm/mermaid@11.4.1/dist/mermaid.esm.min.mjs";
}
