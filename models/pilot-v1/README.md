# Premier modèle expérimental

Ce réseau de valeur a été entraîné sur la **Radeon RX 7900 XTX**, avec le maître Marmelab comme professeur. Il n'est pas sélectionné pour le tournoi : les premiers duels ne démontrent aucun avantage face à notre évaluation classique. Il n'est pas chargé par l'API actuelle.

Fichiers :

- `model.onnx` : modèle et poids portables, 117 119 octets.
- `weights.pt` : checkpoint PyTorch, à charger avec `weights_only=True` et la classe `ValueNet`.
- `manifest.json` : architecture, format des entrées, version du professeur, empreintes et partitions.
- `metrics.json` et `history.json` : résultats mesurés et historique d'apprentissage.
- `export.json` et `export-check.npz` : vérification numérique et exemples réservés à l'export.

## Données et résultats

512 parties ont fourni 6 917 positions brutes, puis 4 787 positions distinctes après déduplication des symétries. Les 260 positions du banc exact sont exclues.

| Ensemble | Positions | Familles d'ouvertures | Erreur quadratique | Référence constante |
| --- | ---: | ---: | ---: | ---: |
| Entraînement | 4 121 | 15 | 0,3249 | 0,5757 |
| Validation | 360 | 1 | 0,4321 | 0,5351 |
| Test réservé | 306 | 1 | 0,5087 | 0,5947 |

Le modèle retenu provient de l'époque 3. L'arrêt automatique est intervenu à l'époque 23 : les époques supplémentaires amélioraient l'apprentissage mais dégradaient la validation. L'entraînement de ce petit réseau a duré 3,88 secondes sur GPU ; ce temps n'inclut ni la préparation de l'environnement ni la génération des données.

L'erreur du test est environ 14,5 % plus faible que celle d'une prédiction constante égale à la moyenne d'entraînement. C'est un premier signal d'apprentissage, **pas une mesure d'Elo**. Une seule famille d'ouvertures est réservée au test, et le corpus est encore petit.

L'export ONNX concorde avec PyTorch sur les exemples de vérification (écart maximal CPU de `1,19e-7`). L'évaluation sur le CPU local prend environ 6,4 microsecondes en médiane et 7,0 au 95e percentile, modèle déjà chargé. Ces chiffres concernent uniquement le réseau.

## Duels dans la recherche C#

Le [rapport complet](../../benchmarks/pilot-v1-duels.json) utilise 100 ms par recherche et 17 ouvertures (plateau vide plus celles du tournoi), jouées avec les couleurs échangées. Le réseau remplace uniquement l'évaluation des feuilles ; les règles, la recherche, la détection de victoire et les budgets restent les mêmes.

| Adversaire | Victoires du réseau | Nuls | Défaites |
| --- | ---: | ---: | ---: |
| Premier coup légal | 34 | 0 | 0 |
| Maître Marmelab | 5 | 0 | 29 |
| Notre recherche avec évaluation classique | 17 | 0 | 17 |

Les 260 fins de partie exactes restent résolues correctement. Les duels sont un diagnostic local ; certaines ouvertures figuraient dans le corpus et 34 parties par adversaire ne suffisent pas à chiffrer un Elo fiable. Le réseau reste expérimental. La suite doit enrichir les positions et leur diversité, puis valider à nouveau sur des parties réservées ; augmenter seulement les époques sur ce corpus a déjà dégradé la validation.
