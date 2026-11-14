using Sentier.Persistance;

namespace Sentier;

/// <summary>Suivi et exploitation, à part du pilotage. Une instance par scope du client, comme le moteur.</summary>
public sealed class SuiviSentier : ISuiviSentier
{
    private readonly RegistreDefinitions _registre;
    private readonly Depot _depot;
    private readonly MoteurSentier _moteur;
    private readonly OptionsSentier _options;

    /// <param name="moteur">Le moteur du même scope : le repositionnement exécute les nœuds par lui.</param>
    public SuiviSentier(RegistreDefinitions registre, IAccesBDSentier bd, OptionsSentier options, MoteurSentier moteur,
        TimeProvider? horloge = null)
    {
        _registre = registre;
        _moteur = moteur;
        _options = options;
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

    public string DiagrammeDefinition(string cleDefinition, OrientationDiagramme orientation = OrientationDiagramme.Horizontale)
        => DiagrammeMermaid.Definition(_registre.Obtenir(cleDefinition), orientation);

    public async Task<string?> DiagrammeInstanceAsync(string cleCorrelation, OrientationDiagramme orientation = OrientationDiagramme.Horizontale,
        CancellationToken cancellationToken = default)
        => await ChargerArbreAsync(cleCorrelation, cancellationToken) is { } arbre
            ? await DiagrammeAsync(arbre, orientation, cancellationToken)
            : null;

    public string PageDefinition(string cleDefinition) => PageHtml.Definition(_registre.Obtenir(cleDefinition), _options.UrlMermaid);

    public async Task<string?> PageInstanceAsync(string cleCorrelation, CancellationToken cancellationToken = default)
    {
        if (await ChargerArbreAsync(cleCorrelation, cancellationToken) is not { } arbre) return null;
        return PageHtml.Instance(cleCorrelation, arbre,
            await DiagrammeAsync(arbre, OrientationDiagramme.Horizontale, cancellationToken),
            await LireHistoriqueAsync(cleCorrelation, cancellationToken), _options.UrlMermaid);
    }

    /// <summary>De la tête à la feuille : la tête, puis ses sous-processus en cours ; nul si la clé est inconnue.</summary>
    private async Task<List<(Instance Instance, DefinitionProcessus Definition, string? Courant)>?> ChargerArbreAsync(string cleCorrelation, CancellationToken ct)
    {
        var tete = await _depot.ChargerTeteAsync(cleCorrelation, ct);
        if (tete is null) return null;

        var instances = new List<Instance> { tete };
        if (tete.EnCours)
            instances.AddRange((await _depot.ChargerEnCoursAsync(cleCorrelation, ct)).Where(i => i.Id != tete.Id).OrderBy(i => i.Id));

        return instances.Select(i =>
        {
            var definition = _registre.Obtenir(i.CleDefinition);
            return (i, definition, definition.Resoudre(i.NoeudCourant));
        }).ToList();
    }

    private async Task<string> DiagrammeAsync(List<(Instance Instance, DefinitionProcessus Definition, string? Courant)> arbre,
        OrientationDiagramme orientation, CancellationToken ct)
    {
        var blocs = new List<(string, DefinitionProcessus, string?, IReadOnlySet<string>)>();
        foreach (var (instance, definition, courant) in arbre)
        {
            var statut = instance.Statut switch
            {
                StatutInstance.EnCours => "en cours",
                StatutInstance.Terminee => "terminée",
                _ => "annulée",
            };
            blocs.Add(($"{definition.Cle} — instance {instance.Id} ({statut})", definition, courant,
                await _depot.NoeudsVisitesAsync(instance.Id, ct)));
        }
        return DiagrammeMermaid.Instance(blocs, orientation);
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
