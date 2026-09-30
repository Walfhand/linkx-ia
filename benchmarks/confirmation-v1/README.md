# Confirmation du premier candidat

Le candidat [loop-v1 / seed-43](../../models/loop-v1/seed-43) a été fixé avant cette expérience. Il conserve un avantage observé, mais **n'est pas promu** selon le critère fixé avant les matchs. Le parent reste [NNUE v1 H512](../../models/nnue-v1/h512).

## Protocole

- 512 nouveaux départs de cinq poses, chacun joué avec les deux couleurs : **1 024 parties par confrontation**, 3 072 au total.
- Même recherche C# compilée et figée, mêmes poids, budget de **100 ms par coup**, confrontations exécutées successivement sur le même ordinateur.
- Graine 84711 ; aucun choix de candidat sur ces nouveaux matchs. Les départs excluent les ancêtres possibles des positions du corpus et des évaluations précédentes, y compris après miroir horizontal ou échange des couleurs.
- Critère inchangé : borne basse unilatérale de Hoeffding à 95 % supérieure à 50 % contre le parent, aucune baisse observée contre Marmelab, aucun recoupement et réussite des fins exactes.

[Protocole et empreintes](protocol.json) · [Départs](promotion-openings.json).

## Résultats

| Confrontation | Victoires | Nuls | Défaites | Points |
| --- | ---: | ---: | ---: | ---: |
| Candidat contre parent | 547 | 5 | 472 | 53,66 % |
| Candidat contre Marmelab | 246 | 3 | 775 | 24,17 % |
| Parent contre Marmelab | 214 | 3 | 807 | 21,04 % |

Les nuls comptent pour un demi-point. Le candidat avait obtenu 58,6 % contre le parent sur le premier panel de 128 parties ; le gain observé est plus faible sur ce panel indépendant. Son avantage de 3,13 points contre Marmelab est descriptif, sans test spécifique de significativité de cet écart.

La borne basse contre le parent vaut **48,25 %**, sous le seuil de promotion. Cette borne conservatrice est calculée sur les 512 paires d'ouvertures et suppose des départs appariés indépendants. Elle ne démontre pas que les deux modèles ont la même force ; elle empêche de promouvoir ce candidat avec le niveau de preuve demandé. Aucun Elo de tournoi n'est déduit de ces résultats.

Le candidat résout **260 / 260** fins exactes et joue un coup optimal dans chaque cas. Les trajectoires de ces 3 072 parties ont **zéro recoupement** avec le corpus. Elles rejoignent les exclusions de la collecte suivante ; le fichier global contient alors 38 178 positions réservées.

[Décision](decision.json) · [Contrôles](selection.json) · [Candidat contre parent](candidate-parent.jsonl) · [Candidat contre Marmelab](candidate-teacher.jsonl) · [Parent contre Marmelab](parent-teacher.jsonl).

## Suite exécutée

Le cycle `loop-v2` conserve le parent H512 et les données du premier cycle. Il collecte 1 000 nouvelles parties avec deux états retenus parmi huit par recherche : un tirage uniforme et un désaccord important avec une analyse courte du professeur. Chaque état retenu reçoit ensuite un label indépendant à plein budget. Les résultats de ce cycle sont publiés séparément dans [models/loop-v2](../../models/loop-v2).
