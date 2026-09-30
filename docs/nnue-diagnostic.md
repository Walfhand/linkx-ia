# Diagnostic du NNUE et de la recherche

Mesures du 30 septembre 2026, Ryzen 7 5700X, .NET 10.0.102 et Node 22.23.1. Poids figés : NNUE H=512, SHA-256 `b2480d507370ebfd70ae26bb0225487e76842cb78cf39d91f14bca275eda3d34`. Le moteur C# initial est celui du commit `3367dd8` ; Marmelab reste figé sur `f8f07bdc6c042d11105ca0da24f00d32e1238e01`.

## Résultat

Deux limites apparaissent : des évaluations NNUE erronées, y compris à profondeur égale, et un ordre d'exploration qui rend certains paliers coûteux. La correction de l'ordre améliore nettement l'efficacité de calcul sur les positions étudiées ; les gains en parties restent modestes. Aucun nouveau poids n'a été entraîné dans ce diagnostic. Les pistes de V2 sont étudiées dans [la revue de littérature](nnue-literature.md).

## Matchs selon le temps de réflexion

Douze [départs de trois poses](../benchmarks/nnue-diagnostic-openings.json) ont été tirés avant les matchs, graine `20260930`. Chaque départ est absent des quatre lots d'entraînement, y compris par miroir horizontal et permutation des couleurs. Les premières poses appartiennent à des familles différentes des 17 dispositions initiales de génération ; cela ne signifie pas que toutes leurs suites possibles étaient absentes des données.

Chaque départ est joué avec les deux couleurs attribuées au candidat. Le professeur et le NNUE ont le même budget de recherche, après rejeu, sans limite artificielle de 100 000 nœuds. Cette première série utilisait un échauffement de 10 000 nœuds ; les comparaisons suivantes ont renforcé l'échauffement pour limiter les effets de compilation à la volée. Les parties effectives de ces campagnes n'ont rencontré aucune position du corpus.

| Budget par coup | Victoires / nuls / défaites du NNUE | Profondeur moyenne NNUE | Profondeur moyenne professeur | Trace complète |
| --- | --- | ---: | ---: | --- |
| 100 ms | 3 / 0 / 21 | 2,64 | 3,63 | [JSONL](../benchmarks/nnue-diagnostic-baseline-100.jsonl) |
| 1 seconde | 2 / 0 / 22 | 3,54 | 4,60 | [JSONL](../benchmarks/nnue-diagnostic-baseline-1000.jsonl) |
| 4 secondes | 7 / 0 / 17 | 4,31 | 5,74 | [JSONL](../benchmarks/nnue-diagnostic-baseline-4000.jsonl) |

Le score à 4 secondes est meilleur sur ces départs, mais le professeur conserve l'avantage. Vingt-quatre parties par budget ne suffisent pas à établir une courbe Elo. Les trajectoires diffèrent selon le budget ; les moyennes de profondeur ne sont pas des mesures sur positions identiques. Les compteurs de nœuds des deux moteurs n'ont pas exactement la même convention, notamment pour les itérations interrompues.

## Analyse des défaites

Les [320 coups du NNUE dans les 60 défaites](../benchmarks/nnue-diagnostic-losses.jsonl) ont été réanalysés avant et après la pose, avec 200 000 nœuds du professeur. Six positions ont ensuite été approfondies à deux millions de nœuds, avec des recherches NNUE et classiques à temps et profondeurs contrôlés.

Chaque coup enregistré est reproduit avec son nombre de nœuds d'origine, en vérifiant coup, score et profondeur. Les processus sont échauffés avant les comparaisons courtes. Les valeurs sont toujours remises du point de vue du joueur qui vient de choisir le coup, y compris lorsque son adversaire passe. Une baisse heuristique est une estimation ; elle n'est appelée erreur prouvée que lorsque l'on dispose d'une alternative non perdante prouvée et d'une continuation perdante prouvée.

Constats :

- **Cas 1 : victoire gâchée.** La position `25 2r11 3Lr32 4Lsr11 4Lsr15 4Ss3 3Ir16 2r16 4Tr13 3Ir12 12 4S3 4Tr33 4Lr38` est gagnante. Le coup joué `15` force une défaite ; `17` gagne. Le NNUE choisit `17` à profondeur 3, puis `15` à profondeur 4. La recherche ne rend donc pas une mauvaise approximation automatiquement fiable à chaque palier.
- **Cas 2 : erreur d'évaluation à profondeur égale.** Après `22 4L1 4Lsr26 4Tr21 4Tr12 4Ss6 3Ir13 3Ir14 4Lr17`, le NNUE à profondeur 3 choisit `15`, perdant. L'évaluation classique à la même profondeur choisit `4Lr26`, gagnant selon la réanalyse. Le temps de calcul n'explique pas à lui seul cette différence.
- **Cas 4 et 6 : palier suivant trop cher.** L'ancienne recherche NNUE prend environ 5,3 et 5,6 secondes pour terminer la profondeur 4. Une limite de 4 secondes laisse donc le coup de profondeur 3, qui mène à une défaite prouvée. L'alternative trouvée plus profondément est mieux évaluée, sans être pour autant une victoire prouvée.
- **Cas 5 : diagnostic indéterminé.** Le professeur, le classique et le NNUE retiennent le même coup ; l'analyse du fils établit une perte. Cela ne prouve pas qu'une autre action permettait d'éviter la défaite. Ce cas ne justifie pas d'attribuer la perte au seul réseau.

Le plafond actuel de 100 000 nœuds de l'API est également mesuré dans les contre-factuels. Il peut empêcher d'utiliser le budget temporel complet. Le diagnostic n'a pas modifié ce garde-fou.

## Correction réalisée et contrôle indépendant

La recherche essaie plus tôt les réponses ayant déjà provoqué une coupure dans l'arbre : deux réponses prioritaires par profondeur et par couleur, puis un historique des coups efficaces. Les couleurs sont séparées pour respecter les passes forcées. Ces informations restent propres à une recherche. Les règles, les poids NNUE et les limites de temps sont conservés.

Le test de la première position échouait avant la correction sous 100 000 nœuds. Il passe maintenant : victoire prouvée en **52 099 nœuds**, contre **117 859** pour la preuve complète de l'ancienne recherche. À profondeur fixée, les douze comparaisons (six positions, profondeurs 3 et 4) gardent exactement les mêmes scores. À profondeur 4, le nombre de nœuds diminue d'un facteur **1,6 à 17,4** selon la position. Ces facteurs concernent ces six cas, pas toutes les parties. [Contrôles détaillés](../benchmarks/nnue-diagnostic-search-checks.json).

Un [second jeu de douze départs](../benchmarks/nnue-validation-openings.json), graine `20261001`, a été réservé avant la modification, avec d'autres premières poses. Il n'a pas servi à choisir les positions de régression. Les deux versions sont comparées séparément au même professeur, avec un échauffement de 200 000 nœuds au maximum :

| Budget | Ancienne recherche | Ordre amélioré | Profondeur moyenne avant → après |
| --- | --- | --- | --- |
| 100 ms | 0 / 0 / 24 | 3 / 0 / 21 | 2,75 → 3,34 |
| 1 seconde | 4 / 0 / 20 | 5 / 0 / 19 | 3,70 → 4,43 |

Traces : [avant 100 ms](../benchmarks/nnue-validation-baseline-100.jsonl), [après 100 ms](../benchmarks/nnue-validation-ordered-100.jsonl), [avant 1 s](../benchmarks/nnue-validation-baseline-1000.jsonl), [après 1 s](../benchmarks/nnue-validation-ordered-1000.jsonl).

Ce gain en victoires est trop petit pour annoncer une hausse Elo établie. Le gain de calcul à profondeur égale est, lui, reproductible à budget de nœuds. Les 966 états distincts des trajectoires de validation ont été ajoutés aux exclusions des futures générations : le fichier de réservation contient désormais 1 226 positions, dont les 260 fins exactes initiales. Aucune des trajectoires de validation ne recoupe le corpus NNUE v1.

## Vérifications

- 154 tests unitaires et 22 tests d'intégration .NET ; le nouveau test tactique a été observé rouge puis vert.
- Six tests Node : génération reproductible, états réservés, traces légales, point de vue des scores, preuve d'une erreur et échanges avec l'analyseur.
- 260 / 260 fins exactes et coups optimaux retrouvés avec le NNUE, puis 260 / 260 avec l'évaluation classique.
- Contrôle historique du classique modifié : 34–0 contre le premier coup légal et 3–31 contre Marmelab à 100 ms. Le diagnostic historique initial était 4–30 contre Marmelab ; cette faible différence ne permet pas de conclure à une amélioration ou régression de force. [Rapport](../benchmarks/nnue-diagnostic-classical.json).

## Reproduction

Compiler l'ancienne version `3367dd8` dans un checkout séparé pour obtenir l'assembly de référence et ses dépendances. Les scripts de diagnostic proviennent de la version actuelle ; `--assembly` choisit le moteur évalué. Les manifestes JSONL conservent empreintes des assemblies, du modèle, des données et des ouvertures.

```bash
LINKX_REFERENCE=/chemin/linkx-reference node --test scripts/diagnose-nnue.test.mjs
node scripts/diagnose-nnue.mjs --mode matches --reference /chemin/linkx-reference --openings benchmarks/nnue-diagnostic-openings.json --assembly /chemin/ancien-build/LinkxAi.Analysis.dll --budget 1000 --output /tmp/diagnostic-1000.jsonl
node scripts/diagnose-nnue.mjs --mode analyse --reference /chemin/linkx-reference --assembly /chemin/ancien-build/LinkxAi.Analysis.dll --log benchmarks/nnue-diagnostic-baseline-100.jsonl --log benchmarks/nnue-diagnostic-baseline-1000.jsonl --log benchmarks/nnue-diagnostic-baseline-4000.jsonl --output /tmp/diagnostic-losses.jsonl
```

Les quatre corpus locaux doivent correspondre aux empreintes du manifeste du modèle. Les fichiers existants ne sont pas écrasés. Chaque ligne de partie conserve son record intégral et tous les coups analysés ; une dernière ligne `summary` marque une campagne terminée. Les temps sont locaux et excluent HTTP, réseau et Dokploy. Le service Docker neuronal et la promotion des poids restent des étapes distinctes.
