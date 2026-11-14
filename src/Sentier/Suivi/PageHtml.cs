using System.Globalization;
using System.Text;
using Sentier.Persistance;

namespace Sentier;

/// <summary>Pages HTML autonomes du suivi : les détails d'une définition ou d'une instance, avec leur diagramme Mermaid.</summary>
internal static class PageHtml
{
    private const string Css = """
        body { font-family: system-ui, sans-serif; margin: 2rem; color: #1f2328; background: #fff; }
        h1 { font-size: 1.5rem; } h2 { font-size: 1.15rem; margin-top: 2rem; border-bottom: 1px solid #d0d7de; padding-bottom: .3rem; }
        table { border-collapse: collapse; margin: .5rem 0; }
        th, td { border: 1px solid #d0d7de; padding: .3rem .6rem; text-align: left; vertical-align: top; font-size: .9rem; }
        th { background: #f6f8fa; }
        code { font-family: ui-monospace, monospace; font-size: .85rem; }
        .mermaid { overflow-x: auto; background: #fff; }
        .vide { color: #656d76; font-style: italic; }
        tr.courant td { background: #fde293; }
        """;

    public static string Definition(DefinitionProcessus definition, string urlMermaid)
    {
        var corps = new StringBuilder();
        corps.Append($"<h1>Définition {E(definition.Cle)}</h1>\n");
        Diagramme(corps, DiagrammeMermaid.Definition(definition, OrientationDiagramme.Horizontale));
        Noeuds(corps, definition, null);

        if (definition.Correspondances.Count > 0)
        {
            corps.Append("<h2>Correspondances</h2>\n");
            Table(corps, ["Ancien nœud", "Nouveau nœud"], definition.Correspondances.Select(c => new[] { Code(c.Key), Code(c.Value) }));
        }
        return Page($"Définition {definition.Cle}", corps, urlMermaid);
    }

    public static string Instance(string cleCorrelation, IReadOnlyList<(Instance Instance, DefinitionProcessus Definition, string? Courant)> arbre,
        string diagramme, IReadOnlyList<EvenementHistorique> historique, string urlMermaid)
    {
        var tete = arbre[0].Instance;
        var corps = new StringBuilder();
        corps.Append($"<h1>Instance {E(cleCorrelation)}</h1>\n");
        Table(corps, ["Clé de corrélation", "Identifiant d'affaires", "Définition", "Statut", "Fin", "Démarrée", "Par", "Terminée"],
        [[
            Code(tete.CleCorrelation), E(tete.IdAffaire.ToString(CultureInfo.InvariantCulture)), Code(tete.CleDefinition),
            E(Statut(tete.Statut)), Code(tete.NomFin), E(Date(tete.DateDemarrage)), E(tete.ActeurDemarrage), E(Date(tete.DateFin)),
        ]]);
        Diagramme(corps, diagramme);

        corps.Append("<h2>Arbre d'instances</h2>\n");
        Table(corps, ["Instance", "Définition", "Statut", "Nœud courant", "Attente", "Tâche", "Échéance", "Groupe de signal"],
            arbre.Select(a => new[]
            {
                E(a.Instance.Id.ToString(CultureInfo.InvariantCulture)), Code(a.Definition.Cle), E(Statut(a.Instance.Statut)),
                Code(a.Courant ?? a.Instance.NoeudCourant), E(a.Instance.Attente?.ToString()),
                E(a.Instance.RefTache?.ToString(CultureInfo.InvariantCulture)), E(Date(a.Instance.DateEcheance)), Code(a.Instance.GroupeSignal),
            }));

        var active = arbre[^1];
        corps.Append($"<h2>Variables de l'instance {active.Instance.Id}</h2>\n");
        Table(corps, ["Variable", "Valeur"], active.Instance.Variables.EnTexte().Select(v => new[] { Code(v.Nom), Code(v.Json) }));

        Noeuds(corps, active.Definition, active.Courant);

        corps.Append("<h2>Historique</h2>\n");
        Table(corps, ["Date", "Instance", "Définition", "Nœud", "Événement", "Transition", "Acteur"],
            historique.Select(e => new[]
            {
                E(Date(e.Date)), E(e.IdInstance.ToString(CultureInfo.InvariantCulture)), Code(e.CleDefinition), Code(e.Noeud),
                E(e.Evenement), Code(e.Transition), E(e.Acteur),
            }));
        return Page($"Instance {cleCorrelation}", corps, urlMermaid);
    }

    private static void Noeuds(StringBuilder corps, DefinitionProcessus definition, string? courant)
    {
        corps.Append($"<h2>Nœuds de {E(definition.Cle)}</h2>\n");
        Table(corps, ["Nœud", "Type", "Détail", "Sorties"],
            definition.ListeNoeuds.Select(n =>
            {
                var (type, detail) = Decrire(n);
                var sorties = string.Join("<br>", DiagrammeMermaid.Sorties(n)
                    .Select(s => (s.Libelle is null ? "" : $"{E(s.Libelle)} → ") + Code(s.Cible)));
                return new[] { Code(n.Id), E(type), E(detail), sorties };
            }),
            definition.ListeNoeuds.Select(n => n.Id == courant));
    }

    private static (string Type, string? Detail) Decrire(Noeud noeud) => noeud switch
    {
        NoeudSysteme n => ("Systémique", $"Commande {n.Commande.Type.Name}"),
        NoeudDecision n => ("Décision", n.Requete is { } requete ? $"Requête {requete.Type.Name}" : "Sur une variable"),
        NoeudInteractif n => ("Interactif", string.Join(" ; ", new[]
        {
            $"Tâche {n.CodeTache}",
            n.Avant is null ? null : $"avant : {n.Avant.Type.Name}",
            n.Apres is null ? null : $"après : {n.Apres.Type.Name}",
        }.OfType<string>())),
        NoeudAttenteSignal n => ("Attente de signal", n.Groupe is null ? null : "Abonnée à un groupe de corrélation"),
        NoeudAttenteTemps n => ("Attente de temps", n.Duree is { } duree ? DiagrammeMermaid.Duree(duree) : $"Date de la variable {n.Date}"),
        NoeudSousProcessus n => ("Sous-processus", $"Définition {n.CleDefinition}"),
        NoeudFin n => ("Fin", $"Nom de fin : {n.NomFin}"),
        _ => (noeud.GetType().Name, null),
    };

    private static void Diagramme(StringBuilder corps, string mermaid)
        => corps.Append("<h2>Diagramme</h2>\n<pre class=\"mermaid\">\n").Append(E(mermaid)).Append("</pre>\n");

    private static void Table(StringBuilder corps, string[] entetes, IEnumerable<string[]> lignes, IEnumerable<bool>? enEvidence = null)
    {
        var liste = lignes.ToArray();
        if (liste.Length == 0)
        {
            corps.Append("<p class=\"vide\">Aucun.</p>\n");
            return;
        }
        var evidence = enEvidence?.ToArray();
        corps.Append("<table>\n<tr>").AppendJoin("", entetes.Select(e => $"<th>{E(e)}</th>")).Append("</tr>\n");
        for (var i = 0; i < liste.Length; i++)
            corps.Append(evidence?[i] == true ? "<tr class=\"courant\">" : "<tr>")
                .AppendJoin("", liste[i].Select(c => $"<td>{c}</td>")).Append("</tr>\n");
        corps.Append("</table>\n");
    }

    private static string Page(string titre, StringBuilder corps, string urlMermaid) => $$"""
        <!doctype html>
        <html lang="fr">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>{{E(titre)}}</title>
        <style>
        {{Css}}
        </style>
        </head>
        <body>
        {{corps}}<script type="module">
        import mermaid from "{{E(urlMermaid)}}";
        mermaid.initialize({ startOnLoad: true, securityLevel: "strict", flowchart: { useMaxWidth: false } });
        </script>
        </body>
        </html>

        """;

    private static string Statut(StatutInstance statut) => statut switch
    {
        StatutInstance.EnCours => "En cours",
        StatutInstance.Terminee => "Terminée",
        _ => "Annulée",
    };

    private static string? Date(DateTime? date) => date?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static string Code(string? texte) => string.IsNullOrEmpty(texte) ? "" : $"<code>{E(texte)}</code>";

    /// <summary>Échappe le texte pour le HTML ; la page est en UTF-8, les accents restent tels quels.</summary>
    private static string E(string? texte) => (texte ?? "")
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;");
}
