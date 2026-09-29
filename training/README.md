# Entraînement local

Le premier modèle est disponible dans [models/pilot-v1](../models/pilot-v1). Il s'agit d'un petit réseau de valeur expérimental ; aucune force Elo n'est encore mesurée.

## Données

Le moteur maître Marmelab sert de professeur au commit `f8f07bdc6c042d11105ca0da24f00d32e1238e01`. Le générateur vérifie ce commit et refuse un domaine modifié. Les recherches sont bornées aux nœuds pour être reproductibles. Chaque partie comporte 20 % d'exploration parmi les coups légaux ; chaque position reçoit néanmoins une analyse du professeur. Les résultats prouvés gardent leur valeur exacte même si une erreur jouée ensuite change l'issue de la partie.

Les 260 positions de `holdout-records.json`, leurs miroirs horizontaux et leurs permutations de couleurs sont exclues. Les ensembles entraînement/validation/test sont séparés par familles d'ouvertures, avec toutes les parties d'une famille dans le même ensemble. Les positions équivalentes sont ensuite dédupliquées entre ensembles. Seul l'entraînement reçoit une augmentation par miroir ; aucune rotation ni inversion verticale n'est utilisée.

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
