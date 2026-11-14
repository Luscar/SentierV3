namespace Sentier.Persistance;

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
                NO_SEQ_INSTC_PROCS      {nombre} NOT NULL,
                ID_AFFA                 {nombre} NOT NULL,
                CLE_CORREL              {texte} NOT NULL,
                CLE_DEFIN               {texte} NOT NULL,
                NO_SEQ_PARN_INSTC_PROCS {nombre},
                ID_NOEUD_COUR           {texte} NOT NULL,
                TE_SEQ_INSTC_PROCS      {court} NOT NULL,
                NOM_FIN                 {texte},
                TYP_ATTEN               {court},
                REF_TACHE               {nombre},
                DAT_ECHNC               {date},
                CLE_GROUP_CORREL_SIGNL  {texte},
                VAR_JSON                {clob} NOT NULL,
                NO_VERSI                {entier} NOT NULL,
                LOGON_CREA              {texte},
                DH_CREA                 {date} NOT NULL,
                DH_MODIF                {date} NOT NULL,
                DH_FIN                  {date},
                CONSTRAINT PK_01_{noms.TableInstance} PRIMARY KEY (NO_SEQ_INSTC_PROCS)
            )
            """,
            // Une seule instance de tête par clé de corrélation ; les sous-processus, qui partagent la clé, sont exclus.
            oracle
                ? $"CREATE UNIQUE INDEX IX_01_{noms.TableInstance} ON {noms.TableInstance} (CASE WHEN NO_SEQ_PARN_INSTC_PROCS IS NULL THEN CLE_CORREL END)"
                : $"CREATE UNIQUE INDEX IX_01_{noms.TableInstance} ON {noms.TableInstance} (CLE_CORREL) WHERE NO_SEQ_PARN_INSTC_PROCS IS NULL",
            $"CREATE INDEX IX_02_{noms.TableInstance} ON {noms.TableInstance} (CLE_CORREL)",
            $"CREATE INDEX IX_03_{noms.TableInstance} ON {noms.TableInstance} (REF_TACHE)",
            $"CREATE INDEX IX_04_{noms.TableInstance} ON {noms.TableInstance} (TE_SEQ_INSTC_PROCS, TYP_ATTEN, DAT_ECHNC)",
            $"CREATE INDEX IX_05_{noms.TableInstance} ON {noms.TableInstance} (CLE_GROUP_CORREL_SIGNL)",
            $"""
            CREATE TABLE {noms.TableHistorique} (
                NO_SEQ_HIST_INSTC_PROCS {nombre} NOT NULL,
                NO_SEQ_INSTC_PROCS      {nombre} NOT NULL,
                ID_NOEUD                {texte} NOT NULL,
                ID_EVEN                 {court} NOT NULL,
                ID_CHEM                 {texte},
                LOGON                   {texte},
                DH_CREA                 {date} NOT NULL,
                CONSTRAINT PK_01_{noms.TableHistorique} PRIMARY KEY (NO_SEQ_HIST_INSTC_PROCS)
            )
            """,
            $"CREATE INDEX IX_01_{noms.TableHistorique} ON {noms.TableHistorique} (NO_SEQ_INSTC_PROCS)",
        };

        // Seul point propre à chaque base : sous SQLite, une séquence est une table à auto-incrément du même nom.
        foreach (var sequence in new[] { noms.SequenceInstance, noms.SequenceHistorique })
            scripts.Add(oracle
                ? $"CREATE SEQUENCE {sequence}"
                : $"CREATE TABLE {sequence} (ID INTEGER PRIMARY KEY AUTOINCREMENT)");

        return scripts;
    }
}
