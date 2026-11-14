using Sentier.Persistance;

namespace Sentier;

/// <summary>Moteur passif : aucun état statique, aucun fil d'arrière-plan. Une instance du moteur par scope du client.</summary>
public sealed class MoteurSentier : IMoteurSentier
{
    private static readonly ResultatSollicitation NonTraitee = new(false, null);

    private readonly RegistreDefinitions _registre;
    private readonly IResolveurSentier _resolveur;
    private readonly IBassinTaches _bassin;
    private readonly IEcouteurFinProcessus[] _ecouteurs;
    private readonly OptionsSentier _options;
    private readonly Depot _depot;

    public MoteurSentier(
        RegistreDefinitions registre,
        IAccesBDSentier bd,
        IResolveurSentier resolveur,
        IBassinTaches bassin,
        IEnumerable<IEcouteurFinProcessus> ecouteurs,
        OptionsSentier options,
        TimeProvider? horloge = null)
    {
        _registre = registre;
        _resolveur = resolveur;
        _bassin = bassin;
        _ecouteurs = ecouteurs.ToArray();
        _options = options;
        _depot = new Depot(bd, new NomsSentier(options), horloge ?? TimeProvider.System);
    }

    /// <summary>Ce qui vaut pour un appel : l'acteur, la garde de boucle et l'instance où l'exécution s'est arrêtée.</summary>
    private sealed class Appel(string acteur, bool assigner, int maximum, CancellationToken ct)
    {
        private int _etapes;
        public string Acteur => acteur;
        public bool Assigner => assigner;
        public CancellationToken Ct => ct;
        public Instance? Feuille { get; set; }

        public void CompterEtape()
        {
            if (++_etapes > maximum) throw new BoucleSansFinException(maximum);
        }
    }

    public async Task<EtatInstance> DemarrerAsync(DemandeDemarrage demande, CancellationToken cancellationToken = default)
    {
        var definition = _registre.Obtenir(demande.CleDefinition);
        if (await _depot.ChargerTeteAsync(demande.CleCorrelation, cancellationToken) is not null)
            throw new CleCorrelationDejaUtiliseeException(demande.CleCorrelation);

        var variables = new Variables();
        variables.Appliquer(demande.Donnees);
        var instance = new Instance
        {
            IdAffaire = demande.IdAffaire,
            CleCorrelation = demande.CleCorrelation,
            CleDefinition = definition.Cle,
            NoeudCourant = definition.Debut,
            Variables = variables,
            ActeurDemarrage = demande.Acteur,
            DateDemarrage = _depot.Maintenant,
        };
        await _depot.InsererAsync(instance, cancellationToken);

        var appel = new Appel(demande.Acteur, demande.AssignerPremiereTache, _options.NombreMaxEtapes, cancellationToken);
        await AvancerAsync(appel, instance, definition.Debut);
        return await EtatAsync(appel);
    }

    public async Task<EtatInstance> CompleterTacheAsync(long refTache, string acteur, string? transition = null,
        Donnees? donnees = null, CancellationToken cancellationToken = default)
    {
        var instance = await _depot.ChargerParTacheAsync(refTache, cancellationToken) ?? throw new TacheIntrouvableException(refTache);
        var noeud = (NoeudInteractif)NoeudAttendu(instance);
        var cible = transition is null
            ? noeud.Suivant!
            : noeud.Transitions.TryGetValue(transition, out var nommee) ? nommee : throw new TransitionInconnueException(noeud.Id, transition);

        var appel = new Appel(acteur, false, _options.NombreMaxEtapes, cancellationToken);
        instance.Variables.Appliquer(donnees);
        if (noeud.Apres is not null) await ExecuterAsync(appel, instance, noeud.Id, noeud.Apres);
        await QuitterAttenteAsync(appel, instance, noeud, transition);
        await PoursuivreAsync(appel, instance, cible);
        return await EtatAsync(appel);
    }

    public async Task<ResultatSollicitation> SignalerAsync(string cleCorrelation, string signal, string acteur,
        string? transition = null, Donnees? donnees = null, string? cleDefinition = null, CancellationToken cancellationToken = default)
    {
        var enCours = await _depot.ChargerEnCoursAsync(cleCorrelation, cancellationToken);
        var feuille = enCours.FirstOrDefault(i => i.Attente == TypeAttente.Signal);
        return feuille is null
            ? NonTraitee
            : await RecevoirSignalAsync(feuille, signal, acteur, transition, donnees, cleDefinition, cancellationToken);
    }

    public async Task<IReadOnlyList<EtatInstance>> DiffuserSignalAsync(string signal, string acteur, string? transition = null,
        Donnees? donnees = null, string? cleDefinition = null, CancellationToken cancellationToken = default)
    {
        var etats = new List<EtatInstance>();
        foreach (var instance in await _depot.EnAttenteSignalAsync(cleDefinition, cancellationToken))
        {
            var resultat = await RecevoirSignalAsync(instance, signal, acteur, transition, donnees, cleDefinition, cancellationToken);
            if (resultat.Etat is not null) etats.Add(resultat.Etat);
        }
        return etats;
    }

    private async Task<ResultatSollicitation> RecevoirSignalAsync(Instance instance, string signal, string acteur,
        string? transition, Donnees? donnees, string? cleDefinition, CancellationToken ct)
    {
        if (cleDefinition is not null && cleDefinition != instance.CleDefinition) return NonTraitee;
        if (NoeudAttendu(instance) is not NoeudAttenteSignal noeud || !noeud.Signaux.TryGetValue(signal, out var cible)) return NonTraitee;
        if (transition is not null)
            cible = noeud.Transitions.TryGetValue(transition, out var nommee) ? nommee : throw new TransitionInconnueException(noeud.Id, transition);

        var appel = new Appel(acteur, false, _options.NombreMaxEtapes, ct);
        instance.Variables.Appliquer(donnees);
        await QuitterAttenteAsync(appel, instance, noeud, transition ?? signal);
        await PoursuivreAsync(appel, instance, cible ?? noeud.Suivant!);
        return new ResultatSollicitation(true, await EtatAsync(appel));
    }

    public async Task<IReadOnlyList<Echeance>> ListerEcheancesDuesAsync(DateTime instant, CancellationToken cancellationToken = default)
        => (await _depot.EcheancesDuesAsync(instant, cancellationToken))
            .Select(i => new Echeance(i.Id, i.CleCorrelation, i.CleDefinition, i.NoeudCourant, i.DateEcheance!.Value))
            .ToArray();

    public async Task<ResultatSollicitation> DeclencherEcheanceAsync(long idInstance, string acteur, CancellationToken cancellationToken = default)
    {
        var instance = await _depot.ChargerAsync(idInstance, cancellationToken);
        if (instance is not { EnCours: true, Attente: TypeAttente.Temps } || instance.DateEcheance > _depot.Maintenant) return NonTraitee;

        var noeud = NoeudAttendu(instance);
        var appel = new Appel(acteur, false, _options.NombreMaxEtapes, cancellationToken);
        await QuitterAttenteAsync(appel, instance, noeud, null);
        await PoursuivreAsync(appel, instance, noeud.Suivant!);
        return new ResultatSollicitation(true, await EtatAsync(appel));
    }

    public async Task<EtatInstance?> AnnulerAsync(string cleCorrelation, string acteur, CancellationToken cancellationToken = default)
    {
        // De la feuille vers la tête : la tâche ouverte est annulée avant ses parents.
        foreach (var instance in await _depot.ChargerEnCoursAsync(cleCorrelation, cancellationToken))
        {
            if (instance is { Attente: TypeAttente.Tache, RefTache: { } refTache })
                await _bassin.AnnulerTacheAsync(refTache, acteur, cancellationToken);
            instance.CesserAttente();
            instance.Statut = StatutInstance.Annulee;
            instance.DateFin = _depot.Maintenant;
            await _depot.MettreAJourAsync(instance, cancellationToken);
            await SignalerFinAsync(instance, acteur, cancellationToken);
        }
        return await ObtenirEtatAsync(cleCorrelation, cancellationToken);
    }

    public async Task<EtatInstance?> ObtenirEtatAsync(string cleCorrelation, CancellationToken cancellationToken = default)
    {
        var tete = await _depot.ChargerTeteAsync(cleCorrelation, cancellationToken);
        if (tete is null) return null;
        if (!tete.EnCours || tete.Attente != TypeAttente.SousProcessus) return Etat(tete, tete);

        var enCours = await _depot.ChargerEnCoursAsync(cleCorrelation, cancellationToken);
        return Etat(tete, enCours.FirstOrDefault(i => i.Attente != TypeAttente.SousProcessus) ?? tete);
    }

    public async Task<IReadOnlyList<InstanceOrpheline>> VerifierInstancesAsync(CancellationToken cancellationToken = default)
        => (await _depot.EnCoursAsync(cancellationToken))
            .Where(i => !_registre.Definitions.Any(d => d.Cle == i.CleDefinition && d.Resoudre(i.NoeudCourant) is not null))
            .Select(i => new InstanceOrpheline(i.Id, i.CleCorrelation, i.CleDefinition, i.NoeudCourant))
            .ToArray();

    /// <summary>Exécute les nœuds de l'instance à partir de <paramref name="depart"/>, jusqu'à une attente ou une fin.</summary>
    private async Task AvancerAsync(Appel appel, Instance instance, string depart)
    {
        var definition = _registre.Obtenir(instance.CleDefinition);
        var courant = depart;
        while (true)
        {
            appel.CompterEtape();
            var noeud = definition.Noeud(courant);
            instance.NoeudCourant = noeud.Id;
            await HistoriserAsync(appel, instance, noeud.Id, "ENTREE", null);

            switch (noeud)
            {
                case NoeudSysteme systeme:
                    await ExecuterAsync(appel, instance, noeud.Id, systeme.Commande);
                    await HistoriserAsync(appel, instance, noeud.Id, "SORTIE", null);
                    courant = systeme.Suivant!;
                    break;

                case NoeudDecision decision:
                    var resultat = await DeciderAsync(appel, instance, decision);
                    await HistoriserAsync(appel, instance, noeud.Id, "SORTIE", resultat?.ToString());
                    courant = decision.Choisir(resultat);
                    break;

                case NoeudInteractif interactif:
                    if (interactif.Avant is not null) await ExecuterAsync(appel, instance, noeud.Id, interactif.Avant);
                    instance.RefTache = await _bassin.CreerTacheAsync(
                        new DemandeTache(interactif.CodeTache, instance.Id, instance.IdAffaire, instance.CleCorrelation,
                            instance.CleDefinition, noeud.Id, appel.Acteur, appel.Assigner), appel.Ct);
                    await AttendreAsync(appel, instance, TypeAttente.Tache);
                    return;

                case NoeudAttenteSignal:
                    await AttendreAsync(appel, instance, TypeAttente.Signal);
                    return;

                case NoeudAttenteTemps temps:
                    instance.DateEcheance = temps.Duree is { } duree ? _depot.Maintenant + duree : instance.Variables.Lire(temps.Date!);
                    await AttendreAsync(appel, instance, TypeAttente.Temps);
                    return;

                case NoeudSousProcessus sousProcessus:
                    await AttendreAsync(appel, instance, TypeAttente.SousProcessus);
                    var enfant = await DemarrerEnfantAsync(appel, instance, sousProcessus);
                    if (enfant.Statut != StatutInstance.Terminee) return; // l'enfant attend : le parent aussi
                    courant = await ReprendreParentAsync(appel, instance, sousProcessus, enfant);
                    break;

                case NoeudFin fin:
                    instance.Statut = StatutInstance.Terminee;
                    instance.NomFin = fin.NomFin;
                    instance.DateFin = _depot.Maintenant;
                    await _depot.MettreAJourAsync(instance, appel.Ct);
                    await SignalerFinAsync(instance, appel.Acteur, appel.Ct);
                    appel.Feuille = instance;
                    return;
            }
        }
    }

    /// <summary>Avance l'instance puis, chaque fois qu'un sous-processus se termine, reprend son parent.</summary>
    private async Task PoursuivreAsync(Appel appel, Instance instance, string depart)
    {
        await AvancerAsync(appel, instance, depart);
        while (instance is { Statut: StatutInstance.Terminee, IdParent: { } idParent })
        {
            var parent = await _depot.ChargerAsync(idParent, appel.Ct)
                ?? throw new SentierException($"L'instance parente {idParent} est introuvable.");
            var noeud = (NoeudSousProcessus)NoeudAttendu(parent);
            var suite = await ReprendreParentAsync(appel, parent, noeud, instance);
            instance = parent;
            await AvancerAsync(appel, instance, suite);
        }
    }

    private async Task<Instance> DemarrerEnfantAsync(Appel appel, Instance parent, NoeudSousProcessus noeud)
    {
        var definition = _registre.Obtenir(noeud.CleDefinition);
        var enfant = new Instance
        {
            IdAffaire = parent.IdAffaire,
            CleCorrelation = parent.CleCorrelation,
            CleDefinition = definition.Cle,
            IdParent = parent.Id,
            NoeudCourant = definition.Debut,
            Variables = parent.Variables.Copier(), // entrées : l'enfant reçoit les variables du parent
            ActeurDemarrage = appel.Acteur,
            DateDemarrage = _depot.Maintenant,
        };
        await _depot.InsererAsync(enfant, appel.Ct);
        await AvancerAsync(appel, enfant, definition.Debut);
        return enfant;
    }

    private async Task<string> ReprendreParentAsync(Appel appel, Instance parent, NoeudSousProcessus noeud, Instance enfant)
    {
        parent.Variables.Fusionner(enfant.Variables); // sorties : les variables de l'enfant reviennent au parent
        await QuitterAttenteAsync(appel, parent, noeud, enfant.NomFin);
        return enfant.NomFin is not null && noeud.Sorties.TryGetValue(enfant.NomFin, out var cible) ? cible : noeud.Suivant!;
    }

    private async Task AttendreAsync(Appel appel, Instance instance, TypeAttente attente)
    {
        instance.Attente = attente;
        await _depot.MettreAJourAsync(instance, appel.Ct);
        appel.Feuille = instance;
    }

    private Task QuitterAttenteAsync(Appel appel, Instance instance, Noeud noeud, string? transition)
    {
        instance.CesserAttente();
        return HistoriserAsync(appel, instance, noeud.Id, "SORTIE", transition);
    }

    /// <summary>
    /// Nœud où l'instance est arrêtée. Si le nœud a été retiré ou renommé, l'instance est replacée sur le nœud
    /// de la correspondance, qui doit être un point d'attente de même nature.
    /// </summary>
    private Noeud NoeudAttendu(Instance instance)
    {
        var definition = _registre.Obtenir(instance.CleDefinition);
        var id = definition.Resoudre(instance.NoeudCourant) ?? throw new NoeudInconnuException(instance.CleDefinition, instance.NoeudCourant);
        var noeud = definition.Noeud(id);
        if (noeud.Attente != instance.Attente)
            throw new SentierException(
                $"L'instance {instance.Id} attend « {instance.Attente} » sur « {instance.NoeudCourant} », mais le nœud « {id} » de la définition n'est pas de cette nature.");
        instance.NoeudCourant = id;
        return noeud;
    }

    private Task ExecuterAsync(Appel appel, Instance instance, string noeud, AppelHandler commande)
    {
        var handler = (ICommande)_resolveur.Resoudre(commande.Type);
        return handler.ExecuterAsync(new ContexteCommande
        {
            IdInstance = instance.Id,
            IdAffaire = instance.IdAffaire,
            CleCorrelation = instance.CleCorrelation,
            CleDefinition = instance.CleDefinition,
            Noeud = noeud,
            Acteur = appel.Acteur,
            Variables = instance.Variables,
            Parametres = commande.Resoudre(instance.Variables),
        }, appel.Ct);
    }

    private Task<object?> DeciderAsync(Appel appel, Instance instance, NoeudDecision decision)
    {
        if (decision.Requete is not { } requete) return Task.FromResult(decision.LectureVariable!(instance.Variables));
        return requete.InvocateurRequete!(_resolveur.Resoudre(requete.Type), new ContexteRequete
        {
            IdInstance = instance.Id,
            IdAffaire = instance.IdAffaire,
            CleCorrelation = instance.CleCorrelation,
            CleDefinition = instance.CleDefinition,
            Noeud = decision.Id,
            Acteur = appel.Acteur,
            Variables = instance.Variables,
            Parametres = requete.Resoudre(instance.Variables),
        }, appel.Ct);
    }

    private Task HistoriserAsync(Appel appel, Instance instance, string noeud, string evenement, string? transition)
        => _options.HistoriqueActif
            ? _depot.HistoriserAsync(instance, noeud, evenement, transition, appel.Acteur, appel.Ct)
            : Task.CompletedTask;

    private async Task SignalerFinAsync(Instance instance, string acteur, CancellationToken ct)
    {
        var fin = new FinProcessus(instance.Id, instance.IdParent, instance.IdAffaire, instance.CleCorrelation,
            instance.CleDefinition, instance.Statut, instance.NomFin, acteur);
        foreach (var ecouteur in _ecouteurs) await ecouteur.SurFinAsync(fin, ct);
    }

    private async Task<EtatInstance> EtatAsync(Appel appel)
    {
        var feuille = appel.Feuille!;
        var tete = feuille.IdParent is null
            ? feuille
            : await _depot.ChargerTeteAsync(feuille.CleCorrelation, appel.Ct)
                ?? throw new SentierException($"L'instance de tête de « {feuille.CleCorrelation} » est introuvable.");
        return Etat(tete, feuille);
    }

    private EtatInstance Etat(Instance tete, Instance feuille)
    {
        var definition = _registre.Obtenir(feuille.CleDefinition);
        var noeud = definition.Resoudre(feuille.NoeudCourant) is { } id ? definition.Noeud(id) : null;
        return new EtatInstance
        {
            IdInstance = tete.Id,
            IdAffaire = tete.IdAffaire,
            CleCorrelation = tete.CleCorrelation,
            CleDefinition = tete.CleDefinition,
            Statut = tete.Statut,
            NomFin = tete.NomFin,
            IdInstanceActive = feuille.Id,
            CleDefinitionActive = feuille.CleDefinition,
            NoeudCourant = noeud?.Id ?? feuille.NoeudCourant,
            TypeAttente = feuille.Attente,
            CodeTache = feuille.Attente == TypeAttente.Tache ? (noeud as NoeudInteractif)?.CodeTache : null,
            RefTache = feuille.RefTache,
            DateEcheance = feuille.DateEcheance,
            SignauxAttendus = feuille.Attente == TypeAttente.Signal && noeud is NoeudAttenteSignal attente ? attente.Signaux.Keys.ToArray() : [],
            Variables = feuille.Variables.Copier(),
        };
    }
}
