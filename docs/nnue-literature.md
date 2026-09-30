# Littérature : quelle suite pour l'IA Linkx ?

Revue ciblée du 30 septembre 2026. Sources primaires : articles, publications des auteurs et documentation officielle. Les résultats publiés sur d'autres jeux servent à formuler des expériences pour Linkx ; ils ne prédisent pas son Elo.

## Conclusion pour notre projet

La priorité proposée est une **collecte de positions issues de la recherche, alimentée par les parties du moteur actuel et annotée plus fortement**, puis une boucle d'amélioration mesurée. Le NNUE de 183 873 paramètres reste le contrôle CPU. Son optimalité architecturale pour Linkx n'est pas établie.

Le [diagnostic local](nnue-diagnostic.md) montre à la fois un problème d'exploration et des évaluations erronées. La meilleure exploitation du temps a déjà été améliorée. La génération v1, elle, conserve surtout les positions effectivement jouées par Marmelab, avec exploration. Elle ne conserve pas les nombreux états intermédiaires que le NNUE rencontre lorsqu'il explore des coups hypothétiques. Ce décalage est une hypothèse prioritaire à tester ; nous n'en avons pas encore isolé l'effet par un entraînement comparatif.

## Résultats les plus utiles

### 1. Apprendre à partir de l'arbre exploré

**Veness, Silver, Uther et Blair — Bootstrapping from Game Tree Search, NeurIPS 2009.** TreeStrap entraîne l'évaluation sur les valeurs calculées dans l'arbre. Le papier motive cette collecte par la différence entre les positions d'une partie et celles, parfois peu naturelles, rencontrées pendant la recherche. Il distingue les valeurs calculées à profondeur limitée et les bornes produites par les coupures alpha-bêta, avec une perte unilatérale pour ces dernières. L'expérience utilise une évaluation linéaire avec des caractéristiques métiers, pas un NNUE. [Article, sections 3–5](https://proceedings.neurips.cc/paper_files/paper/2009/file/389bc7bb1e1c2a5e7e147703232a88f6-Paper.pdf).

**Application proposée :** échantillonner des états internes et des alternatives de coups. Pour un premier essai simple, réanalyser ces états comme de nouvelles racines. Une version ultérieure pourra exploiter directement les bornes correctement typées. Une borne de coupure ne doit jamais devenir artificiellement une victoire prouvée.

### 2. Faire progresser conjointement réseau et recherche

**Anthony, Tian et Barber — Thinking Fast and Slow with Deep Learning and Tree Search, NeurIPS 2017.** Expert Iteration alterne recherche, apprentissage et amélioration de la recherche par le réseau. Les expériences portent sur Hex ; une politique, puis une valeur, guident MCTS. Les auteurs obtiennent de meilleurs apprentis lorsque leur expert continue de progresser. Cela fournit une méthode pour aller au-delà d'une imitation permanente du même professeur. Ce n'est pas une expérience NNUE avec alpha-bêta. [Article](https://proceedings.neurips.cc/paper/2017/file/d8e1344e27a5b08cdfd5d027d9b8d6de-Paper.pdf).

**Application proposée :** conserver des versions figées, générer un nouveau lot avec le moteur courant, le réannoter, entraîner un candidat puis le comparer aux références. Le professeur Marmelab reste un point d'ancrage tant que notre recherche ne le remplace pas avantageusement.

### 3. Corriger les états rencontrés par l'élève

**Ross, Gordon et Bagnell — DAgger, AISTATS 2011.** Les observations rencontrées dépendent des actions de l'agent. DAgger collecte les états produits par l'apprenti, demande les actions de l'expert et agrège les données au fil des itérations. Ses garanties concernent le cadre d'imitation étudié ; elles ne garantissent pas un Elo pour un réseau de valeur. [Article et algorithme 3.1](https://proceedings.mlr.press/v15/ross11a/ross11a.pdf).

**Application proposée :** faire jouer aussi le NNUE et le classique, puis demander au professeur d'analyser les situations qu'ils produisent. Garder un mélange d'anciennes et de nouvelles positions. Ce serait une adaptation de l'idée de collecte, pas une implémentation littérale de DAgger.

### 4. Un réseau de valeur peut rester une voie pertinente

**Cohen-Solal — Learning to Play Two-Player Perfect-Information Games without Knowledge, version arXiv révisée en 2025.** Le travail étend l'apprentissage sur les arbres aux fonctions non linéaires et étudie Descent, qui prolonge des variantes vers des états terminaux. Il rapporte des résultats sur plusieurs jeux, dont Hex. La distinction entre estimation, résolution et résultat terminal y est explicite. Cela soutient l'intérêt d'une voie minimax avec apprentissage de valeur ; notre alpha-bêta actuel n'est cependant pas Descent. [Article, sections 3–5 et 7](https://arxiv.org/html/2008.01188v4).

**Application proposée :** profiter des parties Linkx limitées à 28 poses pour rechercher des résultats exacts sur les sous-problèmes abordables. Cette borne ne garantit pas qu'on puisse explorer exhaustivement l'arbre avec nos ressources. Descent constitue un comparateur éventuel si notre boucle avec alpha-bêta plafonne, sans preuve préalable qu'il gagnerait sur Linkx.

### 5. Répartir le calcul et utiliser des signaux utiles

**Wu — Accelerating Self-Play Learning in Go, 2019.** KataGo étudie des budgets de recherche variables, des cibles auxiliaires et des informations propres au jeu. Ses ablations montrent que ces choix comptent pour l'efficacité d'apprentissage. L'expérience utilise MCTS, des réseaux convolutifs et des ressources importantes ; ses facteurs d'accélération ne sont pas transposables directement à notre machine. [Article](https://arxiv.org/html/1902.10565).

**Application proposée :** comparer génération rapide et réanalyse coûteuse d'un sous-ensemble. Des cibles auxiliaires liées aux connexions ou au départage peuvent être testées ensuite, avec une ablation dédiée et un contrôle du coût CPU.

### 6. L'autojeu seul n'assure pas la diversité

**Baxter, Tridgell et Weaver — KnightCap / TDLeaf, 1999.** TDLeaf lie l'apprentissage aux feuilles des variantes principales. Dans les expériences rapportées, des adversaires variés apportaient des situations utiles que l'autojeu déterministe explorait mal. Il s'agit d'une configuration historique particulière, pas d'un échec général de l'autojeu. [Article, section 4](https://arxiv.org/pdf/cs/9901002).

**Application proposée :** mélanger professeur, classique, NNUE courant et anciennes versions ; surveiller la diversité effective des états et des ouvertures plutôt que le seul nombre de parties.

### 7. Ce que la pratique NNUE permet d'affirmer

La [documentation officielle NNUE](https://official-stockfish.github.io/docs/nnue-pytorch-wiki/docs/nnue.html) décrit le compromis entre qualité d'évaluation, calcul incrémental, quantification et vitesse de recherche. Le [guide officiel d'entraînement](https://github.com/official-stockfish/nnue-pytorch/wiki/Basic-training-procedure-(train.py)) documente le mélange scores/résultats, les filtres de données, la variance entre entraînements et la nécessité de mesurer la force en parties. Ce sont des pratiques empiriques, pas une recette universelle.

**Application proposée :** conserver H=512 pendant le test de collecte et répéter les entraînements avec plusieurs initialisations. Les poids 90 % évaluation / 10 % résultat et la calibration `tanh(score/4000)` de notre pilote sont des choix à tester, pas des constantes validées pour Linkx.

### 8. Une source à interpréter avec prudence

**Tan et Watkinson Medina — Study of the Proper NNUE Dataset, prépublication 2024.** Cette étude sur Xiangqi privilégie les positions tactiquement stables. Son compte rendu ne fournit pas assez de détails statistiques pour transférer son gain annoncé à Linkx. Les filtres sont liés aux captures, aux échecs au roi et à la quiescence de ce jeu. [Texte](https://arxiv.org/html/2412.17948v1).

**Application proposée :** vérifier la stabilité des cibles et investiguer des prolongements tactiques adaptés aux connexions. Écarter toutes les positions menaçantes de nos données sans que la recherche sache les résoudre laisserait un trou dans la couverture du réseau. Nous devons notamment conserver les résultats tactiques réellement prouvés.

## Expérience V2 recommandée

Ce protocole est notre proposition, à valider expérimentalement :

1. **Figer les contrôles.** Même moteur de recherche, même NNUE H=512, mêmes partitions et budgets. Séparer la graine d'initialisation de la graine de partition : `train.py --seed` contrôle actuellement les deux.
2. **Comparer deux collectes.** A : positions de parties comme en v1. B : mélange de positions jouées et de positions hypothétiques échantillonnées dans les recherches du moteur courant. Dédupliquer et équilibrer les phases du jeu. Comparer à volume d'exemples égal puis au coût total de génération égal.
3. **Fiabiliser les cibles.** Réanalyser les échantillons avec une recherche plus forte, garder les résultats prouvés prioritaires, enregistrer profondeur, budget, origine et statut. Distinguer un score exact pour un horizon limité d'un résultat de partie prouvé. Conserver correctement le point de vue du joueur après les passes forcées.
4. **Entraîner plusieurs candidats.** Au moins trois initialisations avec le même découpage. Sélectionner sur validation et matchs de développement ; les positions réservées restent exclues. Les diagnostics consommés pour concevoir V2 appartiennent désormais au développement, pas au test final.
5. **Mesurer puis itérer.** Comparaisons appariées contre professeur et anciennes versions, avec budgets CPU et conditions identiques. Publier incertitude et coût. Le nombre de positions utiles et les victoires mesurées décident d'une nouvelle itération.

L'extension vers un réseau politique-valeur avec MCTS est un comparateur crédible inspiré des travaux sur Hex. Elle requiert une nouvelle mesure de latence CPU. Aucun article consulté n'établit qu'elle serait supérieure au NNUE sur Linkx. Gravité, réserves de polyominos, passes forcées, deux axes de connexion et départage par zones empêchent de copier directement un moteur Hex ou Stockfish.
