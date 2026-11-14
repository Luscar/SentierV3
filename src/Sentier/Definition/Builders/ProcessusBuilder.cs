namespace Sentier;

internal sealed class ProcessusBuilder(string cle) : IProcessusBuilder
{
    private readonly List<Noeud> _noeuds = [];
    private readonly Dictionary<string, string> _correspondances = new(StringComparer.Ordinal);
    private readonly List<string> _erreurs = [];

    public IProcessusBuilder Systeme<TCommande>(string id, Action<IParametresBuilder>? parametres = null) where TCommande : ICommande
        => Ajouter(new NoeudSysteme(id, Appel(typeof(TCommande), parametres, id)));

    public IProcessusBuilder Decision<TRequete>(string id, Action<IDecisionBuilder> decision) where TRequete : IRequete
    {
        var typeResultat = AppelHandler.TypeResultatRequete(typeof(TRequete));
        if (typeResultat is null)
        {
            _erreurs.Add($"Nœud « {id} » : {typeof(TRequete).Name} doit implémenter IRequete<TResultat>.");
            typeResultat = typeof(object);
        }
        var appel = new AppelHandler(typeof(TRequete)) { InvocateurRequete = AppelHandler.CreerInvocateur(typeResultat) };
        var noeud = new NoeudDecision(id) { Requete = appel, TypeResultat = typeResultat };
        decision(new DecisionBuilder(noeud, appel, _erreurs));
        VerifierParametres(appel, id);
        return Ajouter(noeud);
    }

    public IProcessusBuilder Decision<T>(string id, Variable<T> variable, Action<IDecisionBuilder> decision)
    {
        var noeud = new NoeudDecision(id) { LectureVariable = v => v.Lire(variable), TypeResultat = typeof(T) };
        decision(new DecisionBuilder(noeud, null, _erreurs));
        return Ajouter(noeud);
    }

    public IProcessusBuilder Interactif(string id, string codeTache, Action<IInteractifBuilder>? tache = null)
    {
        var noeud = new NoeudInteractif(id, codeTache);
        tache?.Invoke(new InteractifBuilder(this, noeud, _erreurs));
        return Ajouter(noeud);
    }

    public IProcessusBuilder AttenteSignal(string id, string signal, Action<IAttenteSignalBuilder>? attente = null)
    {
        var noeud = new NoeudAttenteSignal(id);
        noeud.Signaux[signal] = null;
        attente?.Invoke(new AttenteSignalBuilder(noeud, _erreurs));
        return Ajouter(noeud);
    }

    public IProcessusBuilder AttenteTemps(string id, TimeSpan duree, Action<IAttenteTempsBuilder>? attente = null)
        => AttenteTemps(new NoeudAttenteTemps(id) { Duree = duree }, attente);

    public IProcessusBuilder AttenteTemps(string id, Variable<DateTime> date, Action<IAttenteTempsBuilder>? attente = null)
        => AttenteTemps(new NoeudAttenteTemps(id) { Date = date }, attente);

    private ProcessusBuilder AttenteTemps(NoeudAttenteTemps noeud, Action<IAttenteTempsBuilder>? attente)
    {
        attente?.Invoke(new AttenteTempsBuilder(noeud, _erreurs));
        return Ajouter(noeud);
    }

    public IProcessusBuilder SousProcessus(string id, string cleDefinition, Action<ISousProcessusBuilder>? sorties = null)
    {
        var noeud = new NoeudSousProcessus(id, cleDefinition);
        sorties?.Invoke(new SousProcessusBuilder(noeud));
        return Ajouter(noeud);
    }

    public IProcessusBuilder Fin(string id, string? nomFin = null) => Ajouter(new NoeudFin(id, nomFin ?? id));

    public IProcessusBuilder Aller(string noeud)
    {
        var precedent = _noeuds.LastOrDefault();
        if (precedent is null || !precedent.ExigeSuivant)
            _erreurs.Add($"Aller(« {noeud} ») doit suivre un nœud qui a une suite (ni décision, ni fin).");
        else
        {
            precedent.Suivant = noeud;
            precedent.SuivantExplicite = true;
        }
        return this;
    }

    public IProcessusBuilder Correspondance(string ancienNoeud, string nouveauNoeud)
    {
        _correspondances[ancienNoeud] = nouveauNoeud;
        return this;
    }

    internal AppelHandler Appel(Type type, Action<IParametresBuilder>? parametres, string id)
    {
        var appel = new AppelHandler(type);
        parametres?.Invoke(new ParametresBuilder(appel));
        VerifierParametres(appel, id);
        return appel;
    }

    private void VerifierParametres(AppelHandler appel, string id)
    {
        foreach (var manquant in appel.ParametresManquants())
            _erreurs.Add($"Nœud « {id} » : le paramètre obligatoire « {manquant} » de {appel.Type.Name} n'est pas fourni.");
    }

    private ProcessusBuilder Ajouter(Noeud noeud)
    {
        if (string.IsNullOrWhiteSpace(noeud.Id)) _erreurs.Add("Un nœud n'a pas d'identifiant.");
        _noeuds.Add(noeud);
        return this;
    }

    public DefinitionProcessus Construire()
    {
        var erreurs = new List<string>(_erreurs);
        if (_noeuds.Count == 0) erreurs.Add("La définition n'a aucun nœud.");

        foreach (var double_ in _noeuds.GroupBy(n => n.Id, StringComparer.Ordinal).Where(g => g.Count() > 1))
            erreurs.Add($"L'identifiant « {double_.Key} » est utilisé par {double_.Count()} nœuds.");

        // Enchaînement implicite : un nœud mène au suivant dans le texte.
        for (var i = 0; i < _noeuds.Count; i++)
        {
            var noeud = _noeuds[i];
            if (!noeud.ExigeSuivant || noeud.Suivant is not null) continue;
            if (i + 1 < _noeuds.Count) noeud.Suivant = _noeuds[i + 1].Id;
            else erreurs.Add($"Le nœud « {noeud.Id} » est le dernier et n'est pas une fin.");
        }

        var parId = new Dictionary<string, Noeud>(StringComparer.Ordinal);
        foreach (var noeud in _noeuds) parId.TryAdd(noeud.Id, noeud);

        foreach (var noeud in _noeuds)
        {
            foreach (var cible in noeud.Cibles.Where(c => !parId.ContainsKey(c)).Distinct())
                erreurs.Add($"Le nœud « {noeud.Id} » mène à « {cible} », qui n'existe pas.");
            if (noeud is NoeudDecision { Sinon: null })
                erreurs.Add($"La décision « {noeud.Id} » n'a pas de branche par défaut (Sinon).");
        }

        foreach (var (ancien, nouveau) in _correspondances)
        {
            if (parId.ContainsKey(ancien)) erreurs.Add($"Correspondance : « {ancien} » est encore un nœud de la définition.");
            if (!parId.ContainsKey(nouveau) && !_correspondances.ContainsKey(nouveau))
                erreurs.Add($"Correspondance : « {ancien} » mène à « {nouveau} », qui n'existe pas.");
        }

        if (_noeuds.Count > 0)
        {
            var atteints = Atteignables([_noeuds[0].Id], n => parId.TryGetValue(n, out var x) ? x.Cibles : []);
            foreach (var noeud in _noeuds.Where(n => !atteints.Contains(n.Id)))
                erreurs.Add($"Le nœud « {noeud.Id} » est inatteignable.");

            // Chaque nœud doit pouvoir aboutir à une fin : on remonte depuis les fins.
            var entrants = _noeuds.SelectMany(n => n.Cibles.Select(c => (Cible: c, Source: n.Id)))
                .ToLookup(x => x.Cible, x => x.Source, StringComparer.Ordinal);
            var aboutissent = Atteignables(_noeuds.OfType<NoeudFin>().Select(f => f.Id), n => entrants[n]);
            foreach (var noeud in _noeuds.Where(n => atteints.Contains(n.Id) && !aboutissent.Contains(n.Id)))
                erreurs.Add($"Le nœud « {noeud.Id} » n'aboutit à aucune fin.");
        }

        if (erreurs.Count > 0) throw new DefinitionInvalideException(cle, erreurs);
        return new DefinitionProcessus(cle, _noeuds, parId, _correspondances);
    }

    private static HashSet<string> Atteignables(IEnumerable<string> departs, Func<string, IEnumerable<string>> voisins)
    {
        var vus = new HashSet<string>(StringComparer.Ordinal);
        var pile = new Stack<string>(departs);
        while (pile.Count > 0)
        {
            var courant = pile.Pop();
            if (!vus.Add(courant)) continue;
            foreach (var voisin in voisins(courant)) pile.Push(voisin);
        }
        return vus;
    }

    private sealed class ParametresBuilder(AppelHandler appel) : IParametresBuilder
    {
        public IParametresBuilder Valeur<T>(Parametre<T> parametre, T valeur)
        {
            appel.Affecter(parametre.Nom, _ => valeur);
            return this;
        }

        public IParametresBuilder Variable<T>(Parametre<T> parametre, Variable<T> variable)
        {
            appel.Affecter(parametre.Nom, v => v.Lire(variable));
            return this;
        }
    }

    private sealed class DecisionBuilder(NoeudDecision noeud, AppelHandler? appel, List<string> erreurs) : IDecisionBuilder
    {
        public IDecisionBuilder Si(object valeur, string cible)
        {
            if (valeur is null || !noeud.TypeResultat.IsInstanceOfType(valeur))
                erreurs.Add($"Décision « {noeud.Id} » : la valeur « {valeur} » n'est pas du type {noeud.TypeResultat.Name} du résultat.");
            else
                noeud.Branches.Add((valeur, cible));
            return this;
        }

        public IDecisionBuilder Sinon(string cible)
        {
            noeud.Sinon = cible;
            return this;
        }

        public IDecisionBuilder Valeur<T>(Parametre<T> parametre, T valeur)
        {
            Appel()?.Affecter(parametre.Nom, _ => valeur);
            return this;
        }

        public IDecisionBuilder Variable<T>(Parametre<T> parametre, Variable<T> variable)
        {
            Appel()?.Affecter(parametre.Nom, v => v.Lire(variable));
            return this;
        }

        private AppelHandler? Appel()
        {
            if (appel is null) erreurs.Add($"Décision « {noeud.Id} » : une décision sur une variable n'a pas de paramètres.");
            return appel;
        }
    }

    private static Escalade? Escalader(Noeud noeud, Escalade? actuelle, Escalade nouvelle, List<string> erreurs)
    {
        if (actuelle is null) return nouvelle;
        erreurs.Add($"Nœud « {noeud.Id} » : l'échéance est déclarée deux fois.");
        return actuelle;
    }

    private static void Interrompre(Noeud noeud, string cible, List<string> erreurs)
    {
        if (noeud.Interruption is not null) erreurs.Add($"Nœud « {noeud.Id} » : la sortie d'interruption est déclarée deux fois.");
        else noeud.Interruption = cible;
    }

    private sealed class InteractifBuilder(ProcessusBuilder builder, NoeudInteractif noeud, List<string> erreurs) : IInteractifBuilder
    {
        public IInteractifBuilder Echeance(TimeSpan duree, string cible)
        {
            noeud.Escalade = Escalader(noeud, noeud.Escalade, new Escalade(duree, null, cible), erreurs);
            return this;
        }

        public IInteractifBuilder Echeance(Variable<DateTime> date, string cible)
        {
            noeud.Escalade = Escalader(noeud, noeud.Escalade, new Escalade(null, date, cible), erreurs);
            return this;
        }

        public IInteractifBuilder Avant<TCommande>(Action<IParametresBuilder>? parametres = null) where TCommande : ICommande
        {
            noeud.Avant = builder.Appel(typeof(TCommande), parametres, noeud.Id);
            return this;
        }

        public IInteractifBuilder Apres<TCommande>(Action<IParametresBuilder>? parametres = null) where TCommande : ICommande
        {
            noeud.Apres = builder.Appel(typeof(TCommande), parametres, noeud.Id);
            return this;
        }

        public IInteractifBuilder Sur(string transition, string cible)
        {
            noeud.Transitions[transition] = cible;
            return this;
        }
    }

    private sealed class AttenteSignalBuilder(NoeudAttenteSignal noeud, List<string> erreurs) : IAttenteSignalBuilder
    {
        public IAttenteSignalBuilder Groupe(Variable<string> groupe) => Abonner(v => v.Lire(groupe));

        public IAttenteSignalBuilder Groupe(string groupe)
        {
            if (string.IsNullOrWhiteSpace(groupe)) erreurs.Add($"Nœud « {noeud.Id} » : le groupe de corrélation est vide.");
            return Abonner(_ => groupe);
        }

        private IAttenteSignalBuilder Abonner(Func<Variables, string?> groupe)
        {
            if (noeud.Groupe is not null) erreurs.Add($"Nœud « {noeud.Id} » : le groupe de corrélation est déclaré deux fois.");
            noeud.Groupe = groupe;
            return this;
        }

        public IAttenteSignalBuilder Echeance(TimeSpan duree, string cible)
        {
            noeud.Escalade = Escalader(noeud, noeud.Escalade, new Escalade(duree, null, cible), erreurs);
            return this;
        }

        public IAttenteSignalBuilder Echeance(Variable<DateTime> date, string cible)
        {
            noeud.Escalade = Escalader(noeud, noeud.Escalade, new Escalade(null, date, cible), erreurs);
            return this;
        }

        public IAttenteSignalBuilder Signal(string signal, string cible)
        {
            if (!noeud.Signaux.TryAdd(signal, cible))
                erreurs.Add($"Nœud « {noeud.Id} » : le signal « {signal} » est déclaré deux fois.");
            return this;
        }

        public IAttenteSignalBuilder Sur(string transition, string cible)
        {
            noeud.Transitions[transition] = cible;
            return this;
        }

        public IAttenteSignalBuilder SurInterruption(string cible)
        {
            Interrompre(noeud, cible, erreurs);
            return this;
        }
    }

    private sealed class AttenteTempsBuilder(NoeudAttenteTemps noeud, List<string> erreurs) : IAttenteTempsBuilder
    {
        public IAttenteTempsBuilder SurInterruption(string cible)
        {
            Interrompre(noeud, cible, erreurs);
            return this;
        }
    }

    private sealed class SousProcessusBuilder(NoeudSousProcessus noeud) : ISousProcessusBuilder
    {
        public ISousProcessusBuilder Sur(string nomFin, string cible)
        {
            noeud.Sorties[nomFin] = cible;
            return this;
        }
    }
}
