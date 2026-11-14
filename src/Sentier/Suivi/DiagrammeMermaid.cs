using System.Globalization;
using System.Text;

namespace Sentier;

/// <summary>Rend une définition en flowchart Mermaid. Les nœuds reçoivent un identifiant neutre ; le leur est dans le libellé.</summary>
internal static class DiagrammeMermaid
{
    private const string Styles = """
            classDef visite fill:#e6f4ea,stroke:#34a853,color:#000
            classDef courant fill:#fde293,stroke:#e37400,stroke-width:3px,color:#000
        """;

    private static string Entete(OrientationDiagramme orientation) => orientation == OrientationDiagramme.Horizontale ? "LR" : "TD";

    public static string Definition(DefinitionProcessus definition, OrientationDiagramme orientation)
    {
        var sortie = new StringBuilder($"flowchart {Entete(orientation)}\n");
        Ecrire(sortie, definition, "n", "    ", null, new HashSet<string>());
        return sortie.ToString();
    }

    /// <param name="arbre">De la tête à la feuille : chaque instance, sa définition et les nœuds qu'elle a traversés.</param>
    public static string Instance(
        IReadOnlyList<(string Titre, DefinitionProcessus Definition, string? Courant, IReadOnlySet<string> Visites)> arbre,
        OrientationDiagramme orientation)
    {
        var sortie = new StringBuilder($"flowchart {Entete(orientation)}\n");
        for (var i = 0; i < arbre.Count; i++)
        {
            var (titre, definition, courant, visites) = arbre[i];
            sortie.Append($"    subgraph i{i}[\"{Texte(titre)}\"]\n");
            sortie.Append($"        direction {Entete(orientation)}\n");
            Ecrire(sortie, definition, $"i{i}n", "        ", courant, visites);
            sortie.Append("    end\n");
        }
        sortie.Append(Styles).Append('\n');
        return sortie.ToString();
    }

    private static void Ecrire(StringBuilder sortie, DefinitionProcessus definition, string prefixe, string retrait,
        string? courant, IReadOnlySet<string> visites)
    {
        var noeuds = definition.ListeNoeuds;
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < noeuds.Count; i++) ids[noeuds[i].Id] = $"{prefixe}{i}";

        foreach (var noeud in noeuds)
        {
            var classe = noeud.Id == courant ? ":::courant" : visites.Contains(noeud.Id) ? ":::visite" : "";
            sortie.Append($"{retrait}{ids[noeud.Id]}{Forme(noeud)}{classe}\n");
        }

        foreach (var noeud in noeuds)
            foreach (var (libelle, cible, pointille) in Sorties(noeud))
            {
                var fleche = pointille ? "-.->" : "-->";
                var etiquette = libelle is null ? "" : $"|\"{Texte(libelle)}\"|";
                sortie.Append($"{retrait}{ids[noeud.Id]} {fleche}{etiquette} {ids[cible]}\n");
            }
    }

    private static string Forme(Noeud noeud) => noeud switch
    {
        NoeudSysteme systeme => $"[\"{Libelle(noeud, systeme.Commande.Type.Name)}\"]",
        NoeudDecision decision => $"{{\"{Libelle(noeud, decision.Requete?.Type.Name)}\"}}",
        NoeudInteractif interactif => $"[/\"{Libelle(noeud, $"tâche {interactif.CodeTache}")}\"/]",
        NoeudAttenteSignal signal => $"([\"{Libelle(noeud, signal.Groupe is null ? "signal" : "signal de groupe")}\"])",
        NoeudAttenteTemps temps => $"{{{{\"{Libelle(noeud, temps.Duree is { } duree ? Duree(duree) : $"date {temps.Date}")}\"}}}}",
        NoeudSousProcessus sousProcessus => $"[[\"{Libelle(noeud, sousProcessus.CleDefinition)}\"]]",
        NoeudFin fin => $"(((\"{Libelle(noeud, fin.NomFin == noeud.Id ? null : fin.NomFin)}\")))",
        _ => $"[\"{Texte(noeud.Id)}\"]",
    };

    /// <summary>Sorties d'un nœud : libellé de la flèche, nœud visé, et pointillé pour une escalade.</summary>
    internal static IEnumerable<(string? Libelle, string Cible, bool Pointille)> Sorties(Noeud noeud)
    {
        switch (noeud)
        {
            case NoeudDecision decision:
                foreach (var (valeur, cible) in decision.Branches)
                    yield return (Convert.ToString(valeur, CultureInfo.InvariantCulture), cible, false);
                yield return ("sinon", decision.Sinon!, false);
                yield break;

            case NoeudAttenteSignal signal:
                foreach (var (nom, cible) in signal.Signaux) yield return (nom, cible ?? signal.Suivant!, false);
                foreach (var (nom, cible) in signal.Transitions) yield return (nom, cible, false);
                break;

            case NoeudInteractif interactif:
                yield return (null, interactif.Suivant!, false);
                foreach (var (nom, cible) in interactif.Transitions) yield return (nom, cible, false);
                break;

            case NoeudSousProcessus sousProcessus:
                yield return (null, sousProcessus.Suivant!, false);
                foreach (var (nom, cible) in sousProcessus.Sorties) yield return (nom, cible, false);
                break;

            case NoeudFin:
                yield break;

            default:
                yield return (null, noeud.Suivant!, false);
                break;
        }

        if (noeud.EscaladeDuNoeud is { } escalade)
            yield return ($"échéance {(escalade.Duree is { } duree ? Duree(duree) : escalade.Date!.ToString())}", escalade.Noeud, true);
    }

    private static string Libelle(Noeud noeud, string? detail)
        => detail is null ? Texte(noeud.Id) : $"{Texte(noeud.Id)}<br/>{Texte(detail)}";

    internal static string Duree(TimeSpan duree)
        => duree.Ticks % TimeSpan.TicksPerDay == 0 ? $"{duree.Days} j"
            : duree.Ticks % TimeSpan.TicksPerHour == 0 ? $"{(long)duree.TotalHours} h"
            : duree.ToString("c", CultureInfo.InvariantCulture);

    /// <summary>Texte sûr entre guillemets dans Mermaid.</summary>
    private static string Texte(string texte)
        => texte.Replace("&", "#amp;").Replace("\"", "#quot;").Replace("<", "#lt;").Replace(">", "#gt;");
}
