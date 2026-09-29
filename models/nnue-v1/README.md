# Premier NNUE Linkx

Entraînement supervisé local sur **Radeon RX 7900 XTX**, puis inférence native C# sur **Ryzen 7 5700X**, sans GPU. Les trois réseaux restent expérimentaux ; l'API publique conserve son évaluation classique.

**H=512, soit 183 873 paramètres, est le candidat retenu pour les prochains essais.** Il obtient le meilleur résultat de ce petit banc contre le moteur classique. Il reste nettement derrière le professeur Marmelab. Ces matchs ne démontrent pas encore un progrès statistiquement établi sur des ouvertures indépendantes.

## Corpus et entraînement

- 10 000 parties du professeur Marmelab contre lui-même, au commit `f8f07bdc6c042d11105ca0da24f00d32e1238e01`, avec 20 % de coups d'exploration.
- Quatre lots de 2 500 parties, graines 53201 à 53204, budget de 50 000 nœuds par analyse.
- 134 804 positions brutes, 78 288 distinctes après déduplication par miroir horizontal et permutation des couleurs.
- 68 507 positions d'entraînement, 5 164 de validation et 4 617 de test ; respectivement 15, 1 et 1 familles d'ouvertures. Le miroir horizontal double les exemples d'entraînement.
- Les 260 positions du banc exact et leurs symétries sont exclues du corpus. Le test final ne sélectionne pas les époques : les poids restaurés minimisent l'erreur de validation.
- Environ 16 minutes pour générer les données, puis 57,3 secondes de calcul GPU cumulé pour les trois réseaux, hors préparation et vérification des exports.

L'architecture et les commandes reproductibles sont décrites dans [le guide d'entraînement](../../training/README.md). Chaque dossier contient le checkpoint PyTorch `weights.pt`, le modèle entier natif `model.nnue`, les métriques, l'historique, la provenance et les vérifications C#. Les données volumineuses restent locales ; leurs empreintes et les graines sont conservées dans les manifestes.

| Modèle | Paramètres | Fichier natif | Meilleure époque / exécutées | Erreur MSE sur test |
| --- | ---: | ---: | ---: | ---: |
| [H=256](h256) | 91 969 | 184 536 octets | 4 / 24 | 0,4513 |
| [H=512](h512) | 183 873 | 368 856 octets | 2 / 22 | 0,4407 |
| [H=1024](h1024) | 367 681 | 737 496 octets | 2 / 22 | 0,4529 |

Le prédicteur constant a une MSE de 0,6337 sur ce test. L'erreur d'entraînement continue de baisser après les meilleures époques alors que celle de validation remonte : prolonger ces entraînements n'améliorait pas leur généralisation. Une baisse de MSE ne mesure pas un gain Elo.

## Force mesurée sur CPU

Chaque adversaire est joué sur 17 ouvertures, avec les deux couleurs, à **100 ms de recherche par coup**. Les cellules indiquent **victoires / nuls / défaites** du NNUE. Le maître Marmelab est appelé sans livre d'ouverture. Les trois campagnes sont exécutées successivement après l'entraînement.

| Modèle | Premier coup légal | Marmelab | C# classique | Fins de partie exactes |
| --- | --- | --- | --- | --- |
| [H=256](../../benchmarks/nnue-v1-h256.json) | 34 / 0 / 0 | 5 / 1 / 28 | 16 / 0 / 18 | 260 / 260 |
| [H=512](../../benchmarks/nnue-v1-h512.json) | 34 / 0 / 0 | 8 / 0 / 26 | 20 / 0 / 14 | 260 / 260 |
| [H=1024](../../benchmarks/nnue-v1-h1024.json) | 34 / 0 / 0 | 8 / 0 / 26 | 12 / 0 / 22 | 260 / 260 |

Tous les coups des 306 parties sont rejoués par les règles indépendantes du moteur TypeScript. Les 17 ouvertures de ces matchs recoupent les familles du corpus : ce diagnostic ne constitue ni une estimation d'Elo ni une validation de promotion. La marge de 20–14 contre le classique est trop petite pour conclure seule.

Contre Marmelab, les débits observés sont environ 148 000, 102 000 et 59 000 nœuds/s pour H=256, H=512 et H=1024. La profondeur moyenne atteinte passe de 2,93 à 2,79 puis 2,52 ; les trajectoires de parties diffèrent, ces débits ne sont donc pas un microbenchmark sur positions identiques. Le plus gros réseau ne gagne pas davantage contre le professeur et perd davantage contre le classique.

## Vérifications et limites

Les tests NNUE ont été ajoutés avant l'implémentation (échec d'import Python et compilation C# sans les nouvelles fonctions), puis passés au vert. Ils vérifient encodage, perspectives, réserves, hauteurs, apprentissage quantifié, format binaire, bornes d'entiers, passes forcées, annulation, recherche interrompue et égalité entre accumulateurs incrémentaux et recalcul complet. La recherche complète et incrémentale produit aussi les mêmes décisions à budget de nœuds égal. Vérifications locales : 153 tests unitaires et 22 tests d'intégration .NET, 16 tests Python, génération Node déterministe et construction Docker réussies.

Chaque export est comparé sur 77 positions entre entraînement GPU, PyTorch CPU et C#. L'écart absolu maximal observé reste inférieur à `8.1e-8`. La CI conserve ces prédictions de référence et les vérifie pour les trois modèles.

Le [contrôle de deux recherches simultanées](../../benchmarks/nnue-v1-concurrency.json) utilise le candidat H=512 et le plafond de 100 000 nœuds de l'API. Les deux réponses arrivent en **1,58 et 1,72 seconde**, avec des coups légaux. Il mesure les processus C# locaux, avec rejeu et échanges JSON, sans HTTP ni réseau. Le délai complet devra être mesuré dans le futur service Docker `engine` et sur Dokploy.

La suite proposée est de travailler avec H=512, élargir les situations d'entraînement et améliorer les analyses des positions difficiles, puis refaire des matchs sur des ouvertures réservées. Cette première campagne n'inclut pas encore une boucle d'autojeu du NNUE avec réentraînement.
