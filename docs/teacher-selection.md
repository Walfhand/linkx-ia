# Sélection du professeur

Le professeur initial pour les futures données d'entraînement est le moteur maître Marmelab,
figé au commit `f8f07bdc6c042d11105ca0da24f00d32e1238e01`. Notre première recherche C# reste un candidat.

La décision repose sur le [rapport enregistré](../benchmarks/teacher-validation.json),
mesuré sur un Ryzen 7 5700X avec .NET 10.0.102 et Node.js 22.23.1 :

| Vérification | Résultat |
| --- | --- |
| C# sur 260 fins de partie résolues indépendamment | 260 résultats exacts et coups optimaux retrouvés |
| Marmelab sur les mêmes positions | 260 résultats prouvés et coups optimaux retrouvés |
| C# contre le premier coup légal | 34 victoires, 0 nul, 0 défaite |
| C# contre Marmelab | 4 victoires, 0 nul, 30 défaites |

Les duels couvrent le plateau vide et les 16 ouvertures du tournoi, chacun joué deux fois
avec les couleurs échangées. Chaque recherche dispose de 100 ms, après rejeu de la position.
Le maître est appelé directement, sans livre d'ouverture. Ce sont des diagnostics locaux,
pas une estimation de l'Elo du tournoi, et ils ne prouvent pas une force identique à d'autres budgets.

Le banc exact est construit à partir de 100 parties générées avec une graine fixe. Un solveur
exhaustif indépendant utilise les règles TypeScript et remonte uniquement les résultats terminaux,
sans heuristique. Il retient les positions où il reste au plus dix pièces et rejette les calculs
incomplets. Parmi les 260 positions, 124 proposent des coups ayant des résultats différents.
Onze cas représentatifs restent dans les tests unitaires ; les scripts du README reconstruisent
le banc étendu et les duels. Le candidat C# utilise les règles C# déjà comparées à la référence.

Une bonne performance en fin de partie ne garantit pas la justesse de toutes les estimations
en ouverture ou en milieu de partie. Les futurs exemples devront conserver la provenance,
la profondeur, le nombre de nœuds et le caractère prouvé ou estimé du score. Le point de vue
du score est celui du joueur au trait dans la position analysée.

Ces positions de validation resteront hors du corpus d'entraînement, y compris leurs miroirs
et les permutations des couleurs. Un nouveau professeur ou réseau ne remplacera la référence
qu'après des résultats favorables sur des parties et ouvertures tenues à part, à coût égal.
Une baisse de l'erreur d'apprentissage ne suffit pas à démontrer une amélioration du jeu.

## Recherche C# actuelle

La recherche partage `GamePosition` avec le moteur de règles. Elle utilise l'approfondissement
itératif, alpha-bêta et une table de transposition propre à chaque appel. Un budget de nœuds
permet les tests reproductibles ; un arrêt injecté permet de respecter le délai HTTP et l'annulation.
Seul le dernier palier terminé est conservé. Le premier coup légal reste disponible en secours.

L'évaluation combine les deux distances de connexion, la réserve disponible et la taille
des zones. Elle estime encore imparfaitement les futurs appuis liés à la gravité. Les duels
montrent précisément pourquoi elle n'est pas promue professeur malgré sa réussite au banc exact.
L'API utilise cette recherche comme amélioration du premier coup légal, avec un plafond de
4,5 secondes, au moins 500 ms de marge sur `deadline_ms`, et un plafond de 100 000 nœuds.

La validation locale Docker a terminé quatre parties signées à deux appels simultanés,
sans coup illégal. Deux recherches concurrentes sur plateau vide, avec un délai annoncé
de six secondes, ont répondu en 1 993 et 1 951 ms. Ces temps décrivent cette machine et
ce moteur ; ils devront être remesurés sur Dokploy et avec le futur réseau.
