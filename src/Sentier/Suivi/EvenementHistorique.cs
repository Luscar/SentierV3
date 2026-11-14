namespace Sentier;

/// <summary>Événement de l'historique d'une instance : « ENTREE », « SORTIE » ou « ANNULATION » sur un nœud.</summary>
public sealed record EvenementHistorique(
    long IdInstance,
    string CleDefinition,
    string Noeud,
    string Evenement,
    string? Transition,
    string? Acteur,
    DateTime Date);
