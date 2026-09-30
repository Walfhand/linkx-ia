# linkx-ia

API .NET 10 et moteur de règles pour le tournoi [Linkx](https://github.com/marmelab/linkx/blob/main/docs/protocole-ia.md).

L'API est une Minimal API avec [QuickApi](https://github.com/Walfhand/QuickApi). Le code est organisé par modules (`Operations`, `Turns`) et par cas d'usage dans `Features`. Le modèle du domaine vit dans `Modules/Turns/Domain` et ne dépend pas d'ASP.NET. Les tests unitaires ciblent ce modèle ; les tests d'intégration démarrent l'API avec `WebApplicationFactory`.

## Développement

```bash
dotnet test LinkxAi.slnx
dotnet run --project src/LinkxAi.Api
```

- `GET /api/v1/health` répond `200 ok`.
- `POST /api/v1/move` accepte le JSON du protocole, y compris un `record` vide ou déjà entamé et une version `protocol` inconnue. Il rejoue la partie et répond `200` avec un unique coup légal canonique. Une recherche alpha-bêta approfondit par paliers et garde le dernier palier terminé. Son budget est borné à `max(0, min(4500, deadline_ms - 500))` millisecondes, avec un maximum de 100 000 nœuds ; si aucun palier ne termine, le premier coup légal sert de secours.
- Le moteur reconstitue plateau, réserves et joueur au trait ; il applique rotations, miroirs, chute, support intégral, connexions par côtés et diagonales, passes forcées et départage par la plus grande zone. Les passes peuvent être omises dans `record`. Les objets de position restent inchangés après une pose, réussie ou refusée : une pose réussie produit une nouvelle position.
- Un `record` illégal, une partie terminée ou une couleur incohérente avec le trait reçoit `400` avec les erreurs de validation. Les erreurs de notation indiquent le jeton, son rang à partir de 1 (hors indication du premier joueur) et la raison. Le service ne renvoie jamais `--`. La notation vide commence avec les bleus ; pour commencer avec les blancs, envoyer `record: "w"`.
- Si `LINKX_SECRET` est défini, l'API exige `X-Linkx-Timestamp` et `X-Linkx-Signature` sur `/move`. Le HMAC-SHA256 porte sur `<timestamp>.<corps exact>` ; la fenêtre d'horodatage admise est de cinq minutes. Sans secret configuré, la vérification est désactivée, comme le permet le protocole.

## Docker et Dokploy

```bash
docker compose up --build
```

Dans Dokploy, créer un service **Docker Compose** depuis ce dépôt, branche `main`, fichier `docker-compose.yml`. Ajouter un domaine HTTPS au service `api` sur le port interne `8080`. Utiliser `/api/v1/health` pour la sonde. Définir `LINKX_SECRET` avec le secret remis à l'inscription, puis déclarer `https://<domaine>/api/v1/move` comme adresse de l'IA. Le port n'est pas publié sur l'hôte ; Dokploy route le domaine vers le service.

Les changements de comportement suivent le cycle TDD : test rouge, implémentation minimale, test vert, puis refactorisation. Les tests couvrent notamment les 95 coups du plateau vide, les refus sans mutation, les fins par connexion/blocage/nul, les passes omises et les appels HTTP signés et simultanés. La CI exécute les suites unitaires et d'intégration, puis construit l'image Docker.

Les règles suivent [la spécification Marmelab](https://github.com/marmelab/linkx/blob/f8f07bdc6c042d11105ca0da24f00d32e1238e01/plan.md), avec des exemples de rejeu et des résultats de référence issus de ce dépôt. Voir [les crédits et la licence MIT](THIRD_PARTY_NOTICES.md).

La suite unitaire compare aussi le moteur à 54 positions et refus enregistrés dans `tests/LinkxAi.Tests.Unit/ReferenceFixtures/rules.json`, générés indépendamment par le moteur TypeScript Marmelab au commit indiqué. Elle lit ces résultats enregistrés ; elle n'exécute pas TypeScript pendant `dotnet test`. Pour produire un corpus de comparaison plus large, utiliser Node.js 22.18 ou supérieur avec un clone du dépôt de référence :

```bash
node scripts/generate-reference-fixtures.mjs /chemin/vers/linkx 100 > /tmp/linkx-reference-cases.json
```

## Recherche et professeur d'entraînement

La recherche actuelle évalue les chemins de connexion, les réserves et les plus grandes zones. Elle partage les règles testées de `GamePosition`, possède une table de transposition propre à chaque appel et distingue une estimation d'un résultat de fin de partie prouvé. Elle est déterministe avec un budget de nœuds ; avec un budget de temps, la profondeur atteinte dépend de la machine.

Le [diagnostic du NNUE](docs/nnue-diagnostic.md) compare les budgets de 100 ms, 1 seconde et 4 secondes, puis mesure une amélioration de l'ordre des coups par réponses prioritaires et historique. La [revue de littérature](docs/nnue-literature.md) propose la prochaine expérience d'apprentissage à partir des positions explorées par la recherche.

La [première boucle d'apprentissage exécutée](models/loop-v1) ajoute les parties du moteur courant et des positions hypothétiques réanalysées. Elle réentraîne sur GPU, compare les candidats au parent et au professeur, et conserve automatiquement la référence quand les preuves de progression sont insuffisantes.

Le professeur utilisé pour le premier entraînement est le moteur Marmelab figé au commit de référence. Notre recherche doit d'abord démontrer qu'elle le remplace avantageusement. Les mesures et leurs limites sont dans [la sélection du professeur](docs/teacher-selection.md).

Le [pilote MLP](models/pilot-v1) et les [premiers NNUE incrémentaux](models/nnue-v1) sont disponibles avec leurs poids et résultats. L'[entraînement local](training/README.md) utilise la Radeon. Les NNUE sont exportés en entiers et exécutés nativement en C# sur CPU, avec mises à jour après les coups et restauration après annulation. L'API actuelle conserve sa recherche classique jusqu'à une validation suffisante du réseau en parties indépendantes.

L'outil d'analyse lit une requête JSON par ligne et écrit un résultat JSON par ligne, avec `move`, `score`, `depth`, `nodes`, `exact` et `elapsedMs`. Le score est donné du point de vue du joueur au trait dans la position de départ. `elapsedMs` mesure la recherche après rejeu, pour comparer des budgets de recherche égaux :

```bash
dotnet build tools/LinkxAi.Analysis -c Release
echo '{"record":"15","budgetMs":1000,"maxNodes":10000}' | dotnet tools/LinkxAi.Analysis/bin/Release/net10.0/LinkxAi.Analysis.dll
```

Pour reconstruire le banc de fins de partie puis mesurer le candidat, utiliser le même clone de référence que pour le corpus des règles :

```bash
node scripts/generate-search-benchmark.mjs /chemin/vers/linkx /tmp/linkx-reference-cases.json > /tmp/endgames.json
node scripts/validate-teacher.mjs /chemin/vers/linkx /tmp/endgames.json 100 > /tmp/teacher-validation.json
```

Le solveur du banc explore exhaustivement les fins de partie sans heuristique et écarte toute position qu'il ne termine pas. Ces positions sont réservées à la validation et ne doivent pas servir à entraîner le futur réseau.
