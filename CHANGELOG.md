# Changelog

## 2.0.0 — Refonte complète

Le dépôt ne contenait que des binaires compilés : le code a été décompilé, relu en entier, puis réécrit.

### Corrections de bugs

- **Mise à jour automatique** : elle ne pouvait pas fonctionner. L'URL de l'API était mal construite (`repos/Sharpie-oss/https://github.com/...`) et l'erreur était masquée. Même corrigée, elle aurait installé l'archive du *code source* (`zipball`) à la place de l'application.
- **Raccourci STOP** : il ne marchait que si la fenêtre avait le focus (donc jamais en jeu) et n'était pas sauvegardé. C'est désormais un raccourci global, enregistré dans les paramètres.
- **STOP « fantôme »** : après un STOP, le minuteur de l'image en cours continuait. Il pouvait effacer le média suivant ou en sauter un, et la file d'attente restait bloquée jusqu'au message suivant.
- **Plantages** :
  - un token invalide faisait planter l'application (`async void` sans gestion d'erreur) ;
  - un `config.json` corrompu l'empêchait de démarrer.
- **Premier lancement** : fermer la fenêtre de configuration pouvait arrêter l'application (mode d'arrêt WPF par défaut).
- **Thread Discord bloqué** : les `Dispatcher.Invoke` synchrones bloquaient le gateway Discord.
- **Concurrence** : la file d'attente et l'indicateur « en cours d'affichage » étaient lus sans verrou, depuis plusieurs threads.
- **Fenêtre intempestive** : n'importe quel message texte du salon faisait apparaître puis disparaître la fenêtre.
- **GIF figés** : WPF n'anime pas les GIF nativement.
- **Liens directs ignorés** : les liens vers des images (`https://…/image.png`) n'étaient pas affichés, car l'URL se trouve dans la miniature de l'embed. Les aperçus ajoutés après coup par Discord étaient aussi ignorés.
- **Commande `/caption`** : elle était réenregistrée en commande *globale* à chaque reconnexion (appels inutiles, délai de propagation).
- **Vidéos bloquantes** : une vidéo qui ne s'ouvrait jamais bloquait la file indéfiniment.
- **Barre de progression** : elle durait toujours 5 s, quelle que soit la durée réelle.

### Sécurité

- Le token est chiffré avec DPAPI dans `%APPDATA%\LiveChat` : il était stocké en clair à côté de l'exécutable. L'ancien fichier est migré puis supprimé.
- Les mises à jour proviennent uniquement d'une archive de release GitHub en HTTPS, avec vérification SHA-256.
- L'installation de la mise à jour n'utilise plus de script `.bat` écrit dans `%TEMP%` sous un nom fixe. En cas d'erreur, les fichiers d'origine sont restaurés.
- La file d'attente et la taille des images téléchargées sont limitées.

### Nouveautés

- Fenêtre de paramètres complète : durées, volume, taille de la file, légendes, spoilers, raccourcis, démarrage avec Windows, mises à jour. Les changements s'appliquent sans redémarrer.
- Commandes `/skip` et `/stop`, et `/caption` qui accepte maintenant les images.
- Le texte du message s'affiche en légende, et les messages texte seuls peuvent être affichés en option.
- Les spoilers sont floutés et se dévoilent d'un clic.
- Le volume se règle à la molette. Le volume, la position, la taille et l'épinglage de la fenêtre sont mémorisés.
- Indicateur d'état de la connexion, avec des messages clairs : token refusé, intent « Message Content » manquant, salon introuvable, permissions.
- Reconnexion automatique avec délai progressif quand le réseau est indisponible au démarrage.
- Mise en pause de la réception, menu « Tester l'affichage », options `--test` et `--preview`.
- Une seule instance à la fois : relancer l'application affiche la fenêtre existante.
- La fenêtre ne vole plus le focus et n'apparaît plus dans Alt+Tab.
- Journaux dans `%APPDATA%\LiveChat\logs`.
- Nouvelle icône.

### Technique

- Passage de .NET 8 (support terminé en novembre 2026) à **.NET 10 LTS**.
- Seul `Discord.Net.WebSocket` 3.20 est référencé, au lieu du méta-paquet complet (Commands, Interactions, Webhook… inutilisés).
- Code organisé en couches (configuration, services, vues). La logique (analyse des messages, file, paramètres, mises à jour) est couverte par 79 tests unitaires.
- Le dépôt contient désormais le code source. Les binaires sont publiés dans les Releases par GitHub Actions (CI + workflow de release).
- `.gitignore` .NET (l'ancien était un modèle pour le C), `.editorconfig`, `global.json`.
