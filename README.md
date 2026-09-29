# linkx-ia

Template .NET 10 pour une IA du tournoi [Linkx](https://github.com/marmelab/linkx/blob/main/docs/protocole-ia.md).

L'API est une Minimal API avec [QuickApi](https://github.com/Walfhand/QuickApi). Le code est organisé par modules (`Operations`, `Turns`) et par cas d'usage dans `Features`. Le modèle du domaine vit dans `Modules/Turns/Domain` et ne dépend pas d'ASP.NET. Les tests unitaires ciblent ce modèle ; les tests d'intégration démarrent l'API avec `WebApplicationFactory`.

## Développement

```bash
dotnet test LinkxAi.slnx
dotnet run --project src/LinkxAi.Api
```

- `GET /api/v1/health` répond `200 ok`.
- `POST /api/v1/move` reçoit le corps du protocole Linkx. Il valide les champs de base et répond `501` tant que les règles du jeu et le choix du coup ne sont pas implémentés. Le template n'est donc pas encore prêt pour une qualification.

## Docker et Dokploy

```bash
docker compose up --build
```

Dans Dokploy, créer un service **Docker Compose** depuis ce dépôt, branche `main`, fichier `docker-compose.yml`. Ajouter un domaine HTTPS au service `api` sur le port interne `8080`. Utiliser `/api/v1/health` pour la sonde, puis déclarer `https://<domaine>/api/v1/move` comme adresse de l'IA quand la réponse de jeu sera implémentée. Le port n'est pas publié sur l'hôte ; Dokploy route le domaine vers le service.

Les changements de comportement suivent le cycle TDD : test rouge, implémentation minimale, test vert, puis refactorisation. La CI exécute les suites unitaires et d'intégration, puis construit l'image Docker.
