using System.Text.RegularExpressions;

namespace Sentier.Persistance;

/// <summary>Noms des tables ({PRÉFIXE}_{nom}_{SUFFIXE}) et des séquences ({PRÉFIXE}_S_{nom}_{SUFFIXE}).</summary>
public sealed partial class NomsSentier
{
    public NomsSentier(OptionsSentier options)
    {
        TableInstance = Nom(options.Prefixe, "INSTANCE", options.Suffixe);
        TableHistorique = Nom(options.Prefixe, "HISTORIQUE", options.Suffixe);
        SequenceInstance = Nom(options.Prefixe, "S_INSTANCE", options.Suffixe);
        SequenceHistorique = Nom(options.Prefixe, "S_HISTORIQUE", options.Suffixe);
    }

    public string TableInstance { get; }
    public string TableHistorique { get; }
    public string SequenceInstance { get; }
    public string SequenceHistorique { get; }

    private static string Nom(string prefixe, string nom, string suffixe)
    {
        var complet = string.Join('_', new[] { prefixe, nom, suffixe }.Where(p => !string.IsNullOrEmpty(p)));
        // Les noms sont insérés dans le SQL : on refuse tout ce qui n'est pas un identifiant simple.
        return Identifiant().IsMatch(complet)
            ? complet
            : throw new SentierException($"« {complet} » n'est pas un nom de table ou de séquence valide.");
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]*$")]
    private static partial Regex Identifiant();
}
