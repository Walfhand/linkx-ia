# Entraînement local

Le premier MLP est disponible dans [models/pilot-v1](../models/pilot-v1). La campagne [NNUE v1](../models/nnue-v1) compare trois réseaux à calcul incrémental. Ces modèles sont expérimentaux ; aucune force Elo officielle n'est mesurée.

## Boucle itérative

Le [premier cycle exécuté](../models/loop-v1) collecte les parties du NNUE et un réservoir borné de positions produites pendant ses recherches. Les positions prélevées sont réanalysées comme des racines par Marmelab : les bornes internes du moteur ne deviennent pas des labels exacts. Un état hypothétique n'a pas de résultat de partie observé ; son `outcome` reste `null` et sa cible vient uniquement de la réanalyse. Les résultats prouvés restent prioritaires.

```bash
dotnet build LinkxAi.slnx -c Release
.venv/bin/python training/loop.py --reference /chemin/linkx-reference --parent models/nnue-v1/h512 --output training/runs/loop-v1 --iterations 1
# Même configuration : reprendre les étapes terminées et ajouter une deuxième itération.
.venv/bin/python training/loop.py --reference /chemin/linkx-reference --parent models/nnue-v1/h512 --output training/runs/loop-v1 --iterations 2
```

Le premier mode accepte les NNUE H=512. Il utilise l'image Radeon vérifiée ci-dessous. Les fichiers du parent, du nouveau modèle et de sortie doivent se trouver dans le workspace monté dans le conteneur ; le clone de règles peut être ailleurs sur l'hôte. Les corpus du parent doivent être disponibles dans `training/data` avec leurs empreintes d'origine.

Par défaut, chaque itération génère 1 000 parties en quatre lots, utilise 5 000 nœuds pour les acteurs, 200 000 pour les labels et deux prélèvements par recherche. Les options `--games`, `--shards`, `--actor-nodes`, `--teacher-nodes`, `--samples` et `--match-budget` permettent de changer ces budgets dans un nouveau répertoire d'expérience. Une configuration existante est verrouillée, hors nombre d'itérations. Les lots terminés sont réutilisés ; les lots incomplets sont conservés et refusés pour inspection.

La collecte se limite aux familles d'entraînement du parent et exclut les clés de ses validations/tests, en plus du fichier global de réservation. Le contrôleur vérifie ensuite les empreintes exactes des partitions. `--split-seed` est indépendant de `--seed`. `--resume-weights` reprend le parent et permet de le conserver si l'apprentissage n'améliore pas la validation. `--prefer-stronger-labels` préfère les preuves et analyses plus profondes pour les doublons d'entraînement, sans modifier les labels de validation/test.

Trois réentraînements sont comparés sur les ouvertures de développement. Un candidat unique passe ensuite 128 parties contre le parent et 128 contre Marmelab ; le parent joue les mêmes 128 contre Marmelab. Le panel final est nouveau à chaque itération et filtre les ancêtres possibles de tout état connu. Le test de promotion est conservateur et raisonne par paires d'ouvertures : borne basse unilatérale de Hoeffding à 95 % supérieure à 50 % contre le parent, aucune baisse observée contre le professeur, aucune fuite de positions et 260 fins exactes réussies. Il ne garantit pas un Elo général ; il suppose des départs appariés échantillonnés indépendamment. Une promotion change seulement le parent local de la boucle, sans déployer l'API.

L'état atomique du contrôleur est dans `state.json`. Les modèles, matchs complets et décisions de chaque passage sont dans `iteration-N`. Les corpus et essais locaux restent ignorés par Git ; les modèles et rapports publiés d'une expérience sont copiés dans `models` et `benchmarks`.

## NNUE v1

La campagne utilise 10 000 parties, réparties en quatre lots de 2 500, aux graines 53201 à 53204. Le professeur conserve son budget de 50 000 nœuds par position et 20 % d'exploration. L'ensemble de données et les partitions sont identiques pour les trois tailles.

```bash
node training/generate-data.mjs --reference /tmp/linkx-reference --games 2500 --nodes 50000 --seed 53201 --output training/data/nnue-v1-53201.jsonl
# Répéter pour 53202, 53203 et 53204 (un fichier distinct par processus).
```

Chaque perspective comporte 294 entrées binaires : 162 cases/couleurs, 42 réserves catégorielles (deux joueurs, sept formes, trois quantités) et 90 hauteurs de colonnes (neuf colonnes, dix hauteurs). La hauteur est celle de la case occupée la plus haute, mesurée depuis le bas. Aucun retournement vertical n'est autorisé.

Les deux perspectives utilisent la même transformation `294 → H`. Leurs résultats sont concaténés avec le joueur au trait en premier, puis traversent `2H → 32 → 1`. Les tailles comparées sont H=256 (91 969 paramètres), H=512 (183 873) et H=1024 (367 681).

L'entraînement simule la quantification avec gradients droits à travers les arrondis : poids de transformation à l'échelle 256, activations limitées à 0..256, poids des couches finales à l'échelle 64, biais finaux à l'échelle 16 384. Les arrondis utilisent la règle du pair le plus proche. Une tangente hyperbolique produit la valeur finale dans -1..1.

Pour chaque largeur, utiliser l'image Radeon décrite ci-dessous avec ces arguments :

```bash
python3 training/train.py --data training/data/nnue-v1-53201.jsonl training/data/nnue-v1-53202.jsonl training/data/nnue-v1-53203.jsonl training/data/nnue-v1-53204.jsonl --output training/runs/nnue-v1-h256 --device cuda --nnue-width 256 --epochs 200 --seed 42
```

Les poids `weights.pt` et le fichier natif `model.nnue` sont exportés ensemble. Le format `LXNNU001` contient un en-tête little-endian (signature de huit octets, largeur, 294, 32), puis chaque couche : poids int16 et biais int32. La première couche est stockée par entrée pour appliquer les différences rapidement. Les bornes de poids et de dimensions sont vérifiées au chargement pour exclure les débordements d'entiers.

Le moteur C# conserve deux accumulateurs, pour les couleurs fixes bleu et blanc. Il applique uniquement les entrées qui changent après un coup et restaure les accumulateurs du parent lors d'une annulation. Une passe forcée ne permute pas ces accumulateurs ; seul l'ordre d'entrée des couches finales dépend du joueur au trait. Le parcours de recherche garantit l'annulation même lors d'une interruption ou d'une exception.

```bash
dotnet build LinkxAi.slnx -c Release
.venv/bin/python training/check-nnue.py training/runs/nnue-v1-h256
node training/check-dotnet.mjs training/runs/nnue-v1-h256
node scripts/validate-teacher.mjs /tmp/linkx-reference /tmp/linkx-exact-endgames.json 100 training/runs/nnue-v1-h256/model.nnue > training/runs/nnue-v1-h256/duels.json
```

La vérification compare les prédictions GPU, PyTorch CPU et C# sur 32 positions de validation et 45 instantanés indépendants des règles. Les tests unitaires comparent aussi les accumulateurs entiers après coups, passes et annulations, puis la recherche avec et sans calcul incrémental. Les duels contrôlent les 260 fins de partie exactes et les coups légaux contre le moteur Marmelab, le moteur C# classique et le premier coup légal.

Ces matchs sont un diagnostic de premier essai : les 17 ouvertures recoupent les familles du corpus, 34 parties par adversaire ne suffisent pas pour estimer un Elo fiable, et les mesures varient avec le matériel. La sélection d'une taille lors de ce diagnostic ne vaut pas promotion en production. Le futur service Docker `engine` reste à intégrer après validation de la force et du budget complet de réponse.

## Données

Le moteur maître Marmelab sert de professeur au commit `f8f07bdc6c042d11105ca0da24f00d32e1238e01`. Le générateur vérifie ce commit et refuse un domaine modifié. Les recherches sont bornées aux nœuds pour être reproductibles. Chaque partie comporte 20 % d'exploration parmi les coups légaux ; chaque position reçoit néanmoins une analyse du professeur. Les résultats prouvés gardent leur valeur exacte même si une erreur jouée ensuite change l'issue de la partie.

Les positions de `holdout-records.json`, leurs miroirs horizontaux et leurs permutations de couleurs sont exclues des nouvelles générations. Le fichier contient les 260 fins exactes initiales, les états de validation du diagnostic et ceux des évaluations suivantes. Les manifestes historiques conservent la liste réservée lors de leur génération. Les ensembles entraînement/validation/test sont séparés par familles d'ouvertures, avec toutes les parties d'une famille dans le même ensemble. Les positions équivalentes sont ensuite dédupliquées entre ensembles. Seul l'entraînement reçoit une augmentation par miroir ; aucune rotation ni inversion verticale n'est utilisée.

Node.js 22.18 ou supérieur suffit à générer les données. Avec le clone de référence au bon commit :

```bash
node training/generate-data.mjs --reference /chemin/vers/linkx --games 128 --nodes 50000 --seed 43101 --output training/data/pilot-43101.jsonl
```

Le premier essai utilise quatre lots de 128 parties, aux graines 43101, 43102, 43103 et 43104. Ils peuvent être générés par quatre processus indépendants. Les fichiers existants ne sont pas écrasés. Le fichier `.meta.json`, écrit à la fin, certifie le nombre de lignes et la version du professeur ; un lot incomplet est refusé à l'entraînement.

## Entraînement sur Radeon

L'image suivante a été vérifiée avec la Radeon RX 7900 XTX de cette machine. Elle contient PyTorch 2.9.1 et ROCm 7.2.1. L'option PyTorch `cuda` désigne aussi le GPU AMD via ROCm ; elle échoue explicitement si aucun GPU n'est accessible.

```bash
training_image=rocm/pytorch@sha256:96a2fb24dec9896e2f8238178f0c49d0dcc4c7dcc597be09e4564316bd86d191
docker run --rm --device=/dev/kfd --device=/dev/dri \
  --user "$(id -u):$(id -g)" \
  --mount "type=bind,source=$PWD,target=/workspace" --workdir /workspace \
  --env LINKX_TRAINING_IMAGE="$training_image" \
  "$training_image" python3 training/train.py \
  --data training/data/pilot-43101.jsonl training/data/pilot-43102.jsonl training/data/pilot-43103.jsonl training/data/pilot-43104.jsonl \
  --output training/runs/pilot-v2 --device cuda --epochs 200 --seed 42
```

Le réseau comprend 176 entrées, deux couches cachées de 128 et 32 neurones, puis une valeur entre -1 et 1. Les entrées décrivent les deux couleurs du point de vue du joueur au trait et les deux réserves. Le score cible vaut le résultat exact quand il est prouvé ; sinon, il mélange 90 % d'évaluation du professeur (`tanh(score/4000)`) et 10 % du résultat observé en partie. Cette calibration est celle du premier essai.

L'apprentissage s'arrête après 20 époques sans amélioration de validation et restaure les meilleurs poids. Le test final est mesuré seulement après cette sélection. Les fichiers du corpus et des essais restent locaux, ignorés par Git.

## Export et vérification sur CPU

```bash
uv venv --python 3.12 .venv
uv pip install --python .venv/bin/python -r training/requirements-cpu.txt
.venv/bin/python training/export.py training/runs/pilot-v2
```

L'export compare d'abord les prédictions du modèle entraîné avec son exécution PyTorch sur CPU, puis vérifie ONNX et ses résultats avec ONNX Runtime sur CPU. Il mesure aussi le temps d'une évaluation après échauffement. Le modèle exporté inclut les poids ; `manifest.json` décrit les entrées, la provenance, la partition des données et l'architecture. Le checkpoint `weights.pt` permet de reprendre les poids dans PyTorch.

## Tests

```bash
.venv/bin/python -m unittest discover -s training -p 'test_*.py'
LINKX_REFERENCE=/chemin/vers/linkx node --test training/generate.test.mjs
```

La CI exécute ces tests sur CPU. Ils vérifient le point de vue des couleurs, les miroirs autorisés, les réserves, l'absence de recouvrement entre ensembles, la conservation des meilleurs poids, la génération déterministe et l'équivalence de l'export. L'entraînement du réseau reste distinct des règles et de la recherche ; avant de l'utiliser dans le futur service Docker `engine`, il faudra mesurer sa force dans des duels à budget égal.

## Mesurer la force en jeu

L'outil d'analyse peut charger un modèle ONNX une fois au démarrage, sur CPU. Il encode les positions comme le générateur Python et injecte l'évaluation dans la même recherche que le moteur classique. Un score appris ne peut jamais se faire passer pour un résultat prouvé : seuls les résultats terminaux ou une recherche complète permettent ce verdict.

```bash
dotnet build tools/LinkxAi.Analysis -c Release
node training/check-dotnet.mjs
node scripts/validate-teacher.mjs /chemin/vers/linkx /tmp/endgames.json 100 models/pilot-v1/model.onnx > /tmp/pilot-duels.json
```

Le dernier argument active le réseau et ajoute un duel contre l'évaluation classique. Sans lui, l'outil conserve le fonctionnement décrit dans le README principal. Les [premiers résultats](../models/pilot-v1/README.md) ne justifient pas de remplacer l'évaluation de l'API par ce réseau.
