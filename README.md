# linkx-ia

Template .NET 10 pour une IA du tournoi [Linkx](https://github.com/marmelab/linkx/blob/main/docs/protocole-ia.md).

L'API est une Minimal API avec [QuickApi](https://github.com/Walfhand/QuickApi). Le code est organisé par modules (`Operations`, `Turns`) et par cas d'usage dans `Features`. Le modèle du domaine vit dans `Modules/Turns/Domain` et ne dépend pas d'ASP.NET. Les tests unitaires ciblent ce modèle ; les tests d'intégration démarrent l'API avec `WebApplicationFactory`.

## Développement

```bash
dotnet test LinkxAi.slnx
dotnet run --project src/LinkxAi.Api
```

- `GET /api/v1/health` répond `200 ok`.
- `POST /api/v1/move` accepte le JSON du protocole, y compris un `record` vide ou déjà entamé et une version `protocol` inconnue. Il répond `200` avec `{"move":"15"}`. Ce coup fixe vérifie seulement le format de réponse : il peut être illégal selon la position. Ne pas inscrire cette IA au tournoi avant l'implémentation du jeu.
- Si `LINKX_SECRET` est défini, l'API exige `X-Linkx-Timestamp` et `X-Linkx-Signature` sur `/move`. Le HMAC-SHA256 porte sur `<timestamp>.<corps exact>` ; la fenêtre d'horodatage admise est de cinq minutes. Sans secret configuré, la vérification est désactivée, comme le permet le protocole.

## Docker et Dokploy

```bash
docker compose up --build
```

Dans Dokploy, créer un service **Docker Compose** depuis ce dépôt, branche `main`, fichier `docker-compose.yml`. Ajouter un domaine HTTPS au service `api` sur le port interne `8080`. Utiliser `/api/v1/health` pour la sonde. Définir `LINKX_SECRET` avec le secret remis à l'inscription, puis déclarer `https://<domaine>/api/v1/move` comme adresse de l'IA une fois les règles implémentées. Le port n'est pas publié sur l'hôte ; Dokploy route le domaine vers le service.

Les changements de comportement suivent le cycle TDD : test rouge, implémentation minimale, test vert, puis refactorisation. La CI exécute les suites unitaires et d'intégration, puis construit l'image Docker.
