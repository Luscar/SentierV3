using Sentier.Persistance;

namespace Sentier;

/// <summary>Suivi et exploitation, à part du pilotage. Une instance par scope du client, comme le moteur.</summary>
public sealed class SuiviSentier : ISuiviSentier
{
    private readonly RegistreDefinitions _registre;
    private readonly Depot _depot;
    private readonly MoteurSentier _moteur;

    /// <param name="moteur">Le moteur du même scope : le repositionnement exécute les nœuds par lui.</param>
    public SuiviSentier(RegistreDefinitions registre, IAccesBDSentier bd, OptionsSentier options, MoteurSentier moteur,
        TimeProvider? horloge = null)
    {
        _registre = registre;
        _moteur = moteur;
        _depot = new Depot(bd, new NomsSentier(options), horloge ?? TimeProvider.System);
    }

    public IReadOnlyList<string> ClesDefinitions => _registre.Definitions.Select(d => d.Cle).ToArray();

    public async Task<IReadOnlyList<InstanceOrpheline>> VerifierInstancesAsync(CancellationToken cancellationToken = default)
    {
        var orphelines = new List<InstanceOrpheline>();
        foreach (var arret in await _depot.ArretsAsync(cancellationToken))
            if (Examiner(arret) is { Raison: { } raison })
                await AjouterOrphelinesAsync(orphelines, arret, raison, cancellationToken);
        return orphelines;
    }

    public async Task<ResultatMigration> MigrerInstancesAsync(CancellationToken cancellationToken = default)
    {
        var migrations = new List<MigrationNoeud>();
        var orphelines = new List<InstanceOrpheline>();
        foreach (var arret in await _depot.ArretsAsync(cancellationToken))
        {
            var (nouveau, raison) = Examiner(arret);
            if (raison is not null)
                await AjouterOrphelinesAsync(orphelines, arret, raison, cancellationToken);
            else if (nouveau != arret.NoeudCourant)
                migrations.Add(new MigrationNoeud(arret.CleDefinition, arret.NoeudCourant, nouveau!,
                    await _depot.DeplacerAsync(arret, nouveau!, cancellationToken)));
        }
        return new ResultatMigration(migrations, orphelines);
    }

    public Task<EtatInstance> RepositionnerAsync(string cleCorrelation, string noeud, string acteur, Donnees? donnees = null,
        CancellationToken cancellationToken = default)
        => _moteur.RepositionnerAsync(cleCorrelation, noeud, acteur, donnees, cancellationToken);

    public async Task<IReadOnlyList<EvenementHistorique>> LireHistoriqueAsync(string cleCorrelation, CancellationToken cancellationToken = default)
        => (await _depot.HistoriqueAsync(cleCorrelation, cancellationToken))
            .Select(l => new EvenementHistorique(l.IdInstance, l.CleDefinition, l.Noeud, l.Evenement, l.Transition, l.Acteur, l.DateEvenement))
            .ToArray();

    public string DiagrammeDefinition(string cleDefinition) => DiagrammeMermaid.Definition(_registre.Obtenir(cleDefinition));

    public async Task<string?> DiagrammeInstanceAsync(string cleCorrelation, CancellationToken cancellationToken = default)
    {
        var tete = await _depot.ChargerTeteAsync(cleCorrelation, cancellationToken);
        if (tete is null) return null;

        // De la tête à la feuille : la tête, puis ses sous-processus en cours.
        var instances = new List<Instance> { tete };
        if (tete.EnCours)
            instances.AddRange((await _depot.ChargerEnCoursAsync(cleCorrelation, cancellationToken))
                .Where(i => i.Id != tete.Id).OrderBy(i => i.Id));

        var arbre = new List<(string, DefinitionProcessus, string?, IReadOnlySet<string>)>();
        foreach (var instance in instances)
        {
            var definition = _registre.Obtenir(instance.CleDefinition);
            var visites = await _depot.NoeudsVisitesAsync(instance.Id, cancellationToken);
            var statut = instance.Statut switch
            {
                StatutInstance.EnCours => "en cours",
                StatutInstance.Terminee => "terminée",
                _ => "annulée",
            };
            arbre.Add(($"{definition.Cle} — instance {instance.Id} ({statut})", definition,
                definition.Resoudre(instance.NoeudCourant), visites));
        }
        return DiagrammeMermaid.Instance(arbre);
    }

    /// <summary>Nœud de la définition où l'arrêt se résout, ou la raison pour laquelle les instances sont orphelines.</summary>
    private (string? Noeud, string? Raison) Examiner(LigneArret arret)
    {
        var definition = _registre.Definitions.FirstOrDefault(d => d.Cle == arret.CleDefinition);
        if (definition is null) return (null, $"La définition « {arret.CleDefinition} » n'existe plus.");

        var id = definition.Resoudre(arret.NoeudCourant);
        if (id is null) return (null, $"Le nœud « {arret.NoeudCourant} » n'existe plus et n'a pas de correspondance.");

        var attendue = Codes.Attente(arret.TypeAttente);
        var nature = definition.Noeud(id).Attente;
        return nature == attendue
            ? (id, null)
            : (null, $"L'instance attend « {attendue} », mais le nœud « {id} » est de nature « {nature?.ToString() ?? "sans attente"} ».");
    }

    private async Task AjouterOrphelinesAsync(List<InstanceOrpheline> orphelines, LigneArret arret, string raison, CancellationToken ct)
        => orphelines.AddRange((await _depot.ArretesSurAsync(arret, ct))
            .Select(i => new InstanceOrpheline(i.Id, i.CleCorrelation, i.CleDefinition, i.NoeudCourant, raison)));
}
