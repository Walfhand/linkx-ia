# Première boucle d'apprentissage

Premier cycle exécuté le 30 septembre 2026, à partir du NNUE H=512 de [nnue-v1](../nnue-v1). La collecte utilise le moteur courant, des positions hypothétiques de ses recherches, puis une réanalyse indépendante par Marmelab. Cette adaptation est motivée dans [la revue de littérature](../../docs/nnue-literature.md).

**Le candidat sélectionné est `seed-43`. Il obtient de meilleurs scores observés sur le contrôle final, mais le seuil prudent de promotion n'est pas atteint. Le parent reste la référence.** Les poids des trois essais sont conservés ici ; aucune mise en production neuronale n'a été faite.

## Collecte et entraînement

- 1 000 nouvelles parties, quatre lots aux graines 64101 à 64104 : cycle de deux parties du NNUE contre lui-même, une contre le professeur et une contre le classique, avec couleurs variées.
- Recherche des acteurs limitée à 5 000 nœuds ; 10 % d'exploration pour le NNUE ; préfixes aléatoires de zéro à trois poses.
- Deux échantillons au maximum prélevés dans chaque recherche d'un acteur C#. Les positions restent légales et non terminales ; le prélèvement ne change pas la décision du moteur.
- Réanalyse des positions retenues par le professeur figé, jusqu'à 200 000 nœuds, contre 50 000 pour la première génération.
- **31 628 positions ajoutées : 11 709 jouées et 19 919 hypothétiques.** Ces dernières n'héritent jamais du résultat d'une partie où elles n'ont pas été jouées.
- Rejeu cumulé : 166 432 lignes brutes ; 98 021 positions distinctes d'entraînement, 5 164 de validation et 4 617 de test. Les deux derniers ensembles gardent exactement les clés du parent. Les analyses plus profondes remplacent les doublons moins informatifs uniquement dans l'entraînement.
- Collecte : environ **18,6 minutes**. Réentraînement sur Radeon RX 7900 XTX : **78,2 secondes de calcul GPU cumulé**, hors préparation des données et export.

Les trois essais repartent des mêmes poids du parent, avec des ordres de minibatches différents. La graine de partition reste 42. L'arrêt anticipé peut conserver le parent à l'époque zéro si les mises à jour dégradent toutes la validation.

| Essai | Meilleure époque | MSE validation | MSE test | Matchs de sélection contre le parent |
| --- | ---: | ---: | ---: | --- |
| [seed-42](seed-42) | 2 | 0,3317 | 0,4942 | 10 victoires / 14 défaites |
| [seed-43](seed-43) | 3 | 0,3229 | 0,4323 | 11 victoires / 13 défaites |
| [seed-44](seed-44) | 2 | 0,3331 | 0,4130 | 10 victoires / 14 défaites |

Le parent avait une MSE de 0,3477 en validation et 0,4407 sur ce test. Le choix du candidat utilise les matchs de développement, avec la MSE de validation pour départager ; la MSE de test ne sélectionne pas le modèle.

## Contrôle final

Le premier panel de départs inédits a croisé trois positions du corpus en cours de partie. Il a été [archivé](../../benchmarks/loop-v1/screening) et n'a pas servi à la décision finale. Le candidat sélectionné est resté identique.

Le contrôle corrigé utilise **64 nouveaux départs de cinq poses**, chacun joué avec les deux couleurs, à **100 ms par coup**. Leurs cellules et réserves sont choisies de sorte qu'aucune continuation ne puisse rejoindre un état du corpus, même après miroir horizontal ou permutation des couleurs. Cette exclusion est possible parce que les cellules ne disparaissent jamais et que les réserves ne remontent jamais. Les 384 parties finales ont effectivement zéro recoupement avec le corpus.

| Confrontation | Victoires | Nuls | Défaites | Score |
| --- | ---: | ---: | ---: | ---: |
| Candidat contre parent | 75 | 0 | 53 | 58,6 % |
| Candidat contre Marmelab | 43 | 0 | 85 | 33,6 % |
| Parent contre Marmelab | 30 | 0 | 98 | 23,4 % |

Ce sont des résultats sur ce panel, pas un Elo de tournoi. Le candidat reste inférieur au professeur. Le protocole de promotion exige au moins 64 paires d'ouvertures, une borne basse unilatérale de Hoeffding supérieure à 50 % contre le parent, aucune baisse du score observé contre Marmelab, aucun recoupement et la réussite des fins exactes. La borne basse du candidat est **43,3 %**, sous l'hypothèse d'indépendance des départs appariés. Ce seuil conservateur ne permet donc pas sa promotion malgré le score observé encourageant.

[Décision](../../benchmarks/loop-v1/decision.json) · [Choix et contrôles](../../benchmarks/loop-v1/selection.json) · [Départs](../../benchmarks/loop-v1/promotion-openings.json) · [Contre parent](../../benchmarks/loop-v1/candidate-parent.jsonl) · [Contre Marmelab](../../benchmarks/loop-v1/candidate-teacher.jsonl) · [Parent contre Marmelab](../../benchmarks/loop-v1/parent-teacher.jsonl).

## Vérifications et suite

Les trois exports GPU/PyTorch CPU/C# sont vérifiés sur 77 positions chacun. Le candidat retrouve **260 / 260** résultats exacts et coups optimaux. Les partitions restent figées et les nouvelles trajectoires d'évaluation rejoignent les exclusions des futures collectes.

La boucle est relançable avec `training/loop.py --iterations 2` sur le même répertoire : elle conserve les données de la première itération, génère un nouveau lot et reprend depuis le modèle de référence. Le professeur de réanalyse reste Marmelab dans ce premier mode. Le prélèvement suivi de réanalyse n'est pas une extraction complète des bornes alpha-bêta de TreeStrap.

La prochaine comparaison doit vérifier si le gain observé se reproduit et analyser les faiblesses restantes, avec une diversité d'adversaires et de positions. La publication des poids permet d'examiner ce candidat même sans promotion automatique.

La [confirmation suivante sur 1 024 nouvelles parties par confrontation](../../benchmarks/confirmation-v1) mesure 53,66 % contre le parent, puis 24,17 % contre Marmelab contre 21,04 % pour le parent. Le candidat conserve un avantage observé, mais sa borne basse de 48,25 % reste insuffisante pour la promotion. Cette expérience ajoute des données au bilan sans réécrire la décision initiale.
