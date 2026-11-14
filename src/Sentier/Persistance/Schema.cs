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

/// <summary>
/// Scripts de création des tables. Le package ne les exécute jamais : le client les passe dans ses propres
/// livraisons (Oracle) ou dans la préparation de ses tests (SQLite).
/// </summary>
public static class SchemaSentier
{
    public static IReadOnlyList<string> Creation(DialecteSentier dialecte, OptionsSentier options)
    {
        var noms = new NomsSentier(options);
        var oracle = dialecte == DialecteSentier.Oracle;
        var (nombre, entier, texte, court, clob, date) = oracle
            ? ("NUMBER(19)", "NUMBER(10)", "VARCHAR2(200)", "VARCHAR2(10)", "CLOB", "TIMESTAMP")
            : ("INTEGER", "INTEGER", "TEXT", "TEXT", "TEXT", "DATETIME");

        var scripts = new List<string>
        {
            $"""
            CREATE TABLE {noms.TableInstance} (
                ID_INSTANCE        {nombre} NOT NULL,
                ID_AFFAIRE         {nombre} NOT NULL,
                CLE_CORRELATION    {texte} NOT NULL,
                CLE_DEFINITION     {texte} NOT NULL,
                ID_INSTANCE_PARENT {nombre},
                NOEUD_COURANT      {texte} NOT NULL,
                STATUT             {court} NOT NULL,
                NOM_FIN            {texte},
                TYPE_ATTENTE       {court},
                REF_TACHE          {nombre},
                DATE_ECHEANCE      {date},
                DONNEES            {clob} NOT NULL,
                NO_VERSION         {entier} NOT NULL,
                ACTEUR_DEMARRAGE   {texte},
                DATE_DEMARRAGE     {date} NOT NULL,
                DATE_MODIFICATION  {date} NOT NULL,
                DATE_FIN           {date},
                CONSTRAINT {noms.TableInstance}_PK PRIMARY KEY (ID_INSTANCE)
            )
            """,
            $"CREATE INDEX {noms.TableInstance}_I1 ON {noms.TableInstance} (CLE_CORRELATION)",
            $"CREATE INDEX {noms.TableInstance}_I2 ON {noms.TableInstance} (REF_TACHE)",
            $"CREATE INDEX {noms.TableInstance}_I3 ON {noms.TableInstance} (STATUT, TYPE_ATTENTE, DATE_ECHEANCE)",
            $"""
            CREATE TABLE {noms.TableHistorique} (
                ID_HISTORIQUE  {nombre} NOT NULL,
                ID_INSTANCE    {nombre} NOT NULL,
                NOEUD          {texte} NOT NULL,
                EVENEMENT      {court} NOT NULL,
                TRANSITION     {texte},
                ACTEUR         {texte},
                DATE_EVENEMENT {date} NOT NULL,
                CONSTRAINT {noms.TableHistorique}_PK PRIMARY KEY (ID_HISTORIQUE)
            )
            """,
            $"CREATE INDEX {noms.TableHistorique}_I1 ON {noms.TableHistorique} (ID_INSTANCE)",
        };

        // Seul point propre à chaque base : sous SQLite, une séquence est une table à auto-incrément du même nom.
        foreach (var sequence in new[] { noms.SequenceInstance, noms.SequenceHistorique })
            scripts.Add(oracle
                ? $"CREATE SEQUENCE {sequence}"
                : $"CREATE TABLE {sequence} (ID INTEGER PRIMARY KEY AUTOINCREMENT)");

        return scripts;
    }
}
