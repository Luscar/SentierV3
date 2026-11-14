namespace Sentier;

/// <param name="Traitee">Faux quand rien n'attendait la sollicitation ; l'instance n'a pas bougé.</param>
public sealed record ResultatSollicitation(bool Traitee, EtatInstance? Etat);
