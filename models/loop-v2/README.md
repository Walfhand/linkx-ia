# Cycle 2 — collecte ciblée sur les désaccords

Ce cycle est terminé. Le candidat **seed-42** obtient **69 victoires et 59 défaites contre le parent**, puis **36 victoires et 92 défaites contre Marmelab**, à 100 ms par coup. Son avantage observé reste insuffisant selon le critère de promotion fixé. **Le parent conservé est NNUE v1 H512.**

La [confirmation précédente](../../benchmarks/confirmation-v1) n'avait pas promu le candidat du premier cycle. Les trois essais reprennent donc les poids du parent initial, tout en conservant les données de ce premier cycle. Le réseau reste `294 → 512` partagé entre deux perspectives, puis `1024 → 32 → 1`, soit **183 873 paramètres**.

## Collecte et apprentissage

- **1 000 nouvelles parties**, en quatre lots de 250, graines 65101 à 65104. Environ la moitié sont des auto-parties ; les autres opposent le modèle au professeur ou au moteur classique.
- Acteurs à 5 000 nœuds. À chaque recherche concernée, huit états hypothétiques sont prélevés. Une analyse courte du professeur à 5 000 nœuds les compare à la valeur statique du NNUE.
- Deux états sont retenus lorsque le réservoir le permet : un tirage uniforme et le plus grand désaccord restant. Le tirage ne modifie pas l'exploration des coups joués. Chaque état retenu reçoit une réanalyse indépendante du professeur à **200 000 nœuds**.
- **31 679 positions ajoutées : 11 755 jouées et 19 924 hypothétiques**, dont 9 997 tirages uniformes et 9 927 choix par désaccord. Les états hypothétiques n'héritent jamais du résultat de la partie réelle.
- Après ajout aux corpus précédents : **198 111 lignes brutes**, **126 868 positions distinctes d'entraînement**, 5 164 de validation et 4 617 de test. Les deux dernières partitions sont strictement identiques à celles du parent.
- Collecte : **20,2 minutes**. Calcul d'entraînement cumulé sur Radeon RX 7900 XTX : **102,7 secondes**, hors préparation et export.

Sur l'échelle de valeur `[-1, 1]`, le désaccord absolu moyen avec le label complet vaut **1,061** pour les états ciblés, contre **0,540** pour les tirages uniformes. La sélection collecte donc bien des états où le modèle s'écarte davantage du professeur. Cela ne prouve pas qu'elle améliore à elle seule la force : ce cycle ajoute aussi de nouvelles parties et davantage de données, sans expérience témoin permettant de séparer ces effets.

[Rapport de collecte et provenance](../../benchmarks/loop-v2/collection-report.json) · [Corpus et partitions](../../benchmarks/loop-v2/data-report.json) · [Configuration](../../benchmarks/loop-v2/config.json).

## Trois essais

| Essai | Meilleure époque | MSE validation | MSE test | Sélection contre le parent |
| --- | ---: | ---: | ---: | --- |
| [seed-42](seed-42) | 3 | 0,3126 | 0,3913 | 15 victoires / 9 défaites |
| [seed-43](seed-43) | 3 | 0,3101 | 0,4185 | 13 victoires / 1 nul / 10 défaites |
| [seed-44](seed-44) | 5 | 0,3192 | 0,4404 | 15 victoires / 9 défaites |

Le parent avait une MSE de 0,3477 en validation et de 0,4407 sur ce test. Les matchs de développement sélectionnent le candidat ; la MSE de validation départage les ex æquo. La MSE de test n'intervient pas dans ce choix. Les trois essais gardent la même partition et changent uniquement la graine d'apprentissage. Chacun peut conserver les poids initiaux si aucune époque n'améliore la validation.

Les trois exports sont vérifiés sur **77 positions chacun**, entre GPU, PyTorch CPU et moteur C# natif. L'écart maximal constaté avec C# est inférieur à `9e-8`.

## Contrôle final

Le candidat est fixé avant le contrôle sur **64 nouveaux départs de cinq poses**, chacun joué dans les deux couleurs. Le filtre écarte les ancêtres possibles des 174 837 clés du corpus et des évaluations réservées. Les trois confrontations utilisent le même panel et un budget de **100 ms par coup**.

| Confrontation | Victoires | Nuls | Défaites | Points |
| --- | ---: | ---: | ---: | ---: |
| Candidat contre parent | 69 | 0 | 59 | 53,91 % |
| Candidat contre Marmelab | 36 | 0 | 92 | 28,13 % |
| Parent contre Marmelab | 28 | 1 | 99 | 22,27 % |

Les 260 fins exactes sont réussies, avec un coup optimal dans chaque cas. Les 384 parties finales ne recoupent aucune position du corpus ; leurs trajectoires sont réservées pour les collectes suivantes.

La borne basse unilatérale de Hoeffding contre le parent vaut **38,61 %**, sous le seuil de 50 %. La décision automatique conserve donc le parent. Cette borne est conservatrice et suppose des départs appariés indépendants ; elle ne démontre pas l'absence de progrès. Les scores contre Marmelab ne permettent pas non plus de comparer directement les cycles, puisque leurs panels diffèrent. Aucun Elo officiel n'est estimé.

[Décision](../../benchmarks/loop-v2/decision.json) · [Sélection et contrôles](../../benchmarks/loop-v2/selection.json) · [Départs](../../benchmarks/loop-v2/promotion-openings.json) · [Contre parent](../../benchmarks/loop-v2/candidate-parent.jsonl) · [Contre Marmelab](../../benchmarks/loop-v2/candidate-teacher.jsonl) · [Parent contre Marmelab](../../benchmarks/loop-v2/parent-teacher.jsonl).

La boucle, la collecte ciblée et l'export fonctionnent. Le gain de force reste à établir avec davantage de précision ; ce résultat ne justifie pas de déployer les nouveaux poids. La procédure de reprise est décrite dans [training/README.md](../../training/README.md).
