# Changelog

## 2.2.0 — Sons, messages vocaux et WebP animés

### Nouveautés

- **Sons** : les fichiers audio (MP3, WAV, M4A, AAC, FLAC, WMA) sont lus avec le volume de LiveChat, sur une carte qui affiche leur nom et une barre de progression. Avant, ils n'apparaissaient que sous forme de carte « fichier », sans son.
- **Messages vocaux Discord** : ils sont au format Ogg Opus, que le lecteur de Windows refuse. LiveChat convertit le conteneur Ogg en Matroska, sans réencoder le son, puis le lit sur une carte « Message vocal ». Les fichiers Ogg Vorbis restent non pris en charge.
- **WebP animés** : ils s'animent au lieu de rester figés, grâce au codec WebP de Windows.
- **Nouvelle option** « Lire les sons et les messages vocaux » (activée par défaut). Une fois désactivée, un son s'affiche comme un simple fichier, sans être lu.

### Technique

- La durée maximale d'une vidéo s'applique aussi aux sons.
- 18 nouveaux tests (149 au total).

## 2.1.0 — Stickers animés, modération, « Rejouer » et diagnostics

### Nouveautés

- **Messages supprimés** : quand un message est supprimé sur Discord (par un modérateur, par exemple), ses médias sont retirés de la file d'attente, passés s'ils sont à l'écran et ne peuvent plus être rejoués. Avant, un média supprimé dans le salon restait affiché sur le stream.
- **Stickers animés** : les PNG animés (APNG), format de la plupart des stickers Discord, s'animent au lieu de rester figés sur leur première image. Ils sont affichés jusqu'au bout de leur animation, comme les GIF.
- **Rejouer le dernier média** : depuis le menu de l'icône de notification, ou avec un raccourci global à choisir dans les paramètres. Pratique en jeu quand on a manqué un média.
- **Journal plus utile** : chaque message reçu avec un média ou un aperçu y est résumé (pièces jointes, types d'aperçus, nombre de médias affichés), pour comprendre pourquoi un lien ne s'est pas affiché.
- **Commandes slash** : elles ne sont réenregistrées auprès de Discord que si elles ont changé. Avant, chaque démarrage les réécrivait, et des relances rapprochées déclenchaient la limite de débit de Discord.

### Corrections de bugs

- **Journaux** : le fichier était choisi au lancement. Comme LiveChat reste souvent ouvert plusieurs jours, tout s'écrivait dans un seul fichier qui grossissait sans fin, et les journaux de plus de 7 jours n'étaient purgés qu'au redémarrage. Le fichier change désormais à minuit.
- **Paramètres** : les curseurs s'arrêtaient à 30 s (images), 300 s (vidéos) et 50 médias, alors que les limites sont de 60 s, 600 s et 100 médias. Une valeur plus haute était silencieusement réduite au premier enregistrement.
- **Légendes** : les liens masqués `[texte](lien)` s'affichaient à moitié (« [texte]( »), et l'*italique*, les citations `>` ainsi que les mentions brutes des messages transférés n'étaient pas nettoyés.
- **Icône de notification** : chaque recherche de mise à jour recréait une police sans libérer la précédente (petite fuite d'objets GDI).
- `settings.json` ne contient plus la propriété calculée `IsValidGlobalHotkey`.

### Technique

- GitHub Actions : `actions/checkout@v7` et `actions/setup-dotnet@v6`, car Node.js 20 est déprécié.
- 19 nouveaux tests (131 au total), dont un test de robustesse du décodeur APNG sur 300 fichiers corrompus.

## 2.0.3 — Mise à jour depuis la 2.0.1

### Corrections de bugs

- **Mise à jour bloquée depuis la 2.0.1** : après « Installer et redémarrer », l'ancienne version restait bloquée pendant sa fermeture, car elle chargeait une dépendance dans son exécutable déjà remplacé. La nouvelle version abandonnait au bout de 15 s et il fallait arrêter LiveChat dans le Gestionnaire des tâches. Désormais, la nouvelle version arrête l'ancienne instance du même dossier, retire son icône de notification, puis démarre. Depuis la 2.0.1, la mise à jour prend donc une quinzaine de secondes.

### Améliorations

- **Fenêtre de mise à jour** : les notes de version sont mises en forme (titres, puces) au lieu d'afficher le Markdown brut.
- 1 nouveau test (112 au total).

## 2.0.2 — Corrections et nouveautés issues des tests de bout en bout

### Nouveautés

- **Stickers** : les stickers PNG et GIF sont affichés. Les stickers APNG (animés) s'affichent sur leur première image, et les stickers Lottie ne sont pas pris en charge.
- **Messages transférés** : un média transféré dans le salon (bouton « Transférer » de Discord) est affiché, avec le texte du message d'origine en légende.
- **GIF longs** : un GIF plus long que la durée d'affichage des images est montré jusqu'au bout, comme les GIF Tenor, dans la limite de la durée maximale des vidéos.
- **Mises à jour** : la recherche se fait au démarrage puis toutes les 6 heures, car LiveChat reste souvent ouvert plusieurs jours.
- **Messages d'erreur plus précis** : un lien expiré, une image abîmée et une vidéo dont le codec manque sont maintenant distingués.
- **Accessibilité** : les boutons de la fenêtre et les champs des paramètres ont un nom lisible par les lecteurs d'écran, et l'épingle fonctionne aussi au clavier.
- `--preview SPOILER_fichier.png` affiche le média flouté, comme sur Discord.

### Corrections de bugs

- **WebM illisible** : le lecteur de WPF (moteur de Windows Media Player) refuse les fichiers `.webm`, même avec les extensions vidéo du Microsoft Store installées. Le WebM étant du Matroska, la vidéo téléchargée est maintenant enregistrée en `.mkv` et lue par le lecteur Matroska de Windows (VP8, VP9, son Opus).
- **Volume à 100 %** : à partir de la deuxième vidéo, le volume réglé au maximum était ignoré et les vidéos étaient jouées à 50 %. Le volume transmis au lecteur est plafonné à 99 %, une différence inaudible.
- **Statut après une coupure réseau** : quand Discord reprenait la session, l'indicateur restait sur « Connexion perdue, reconnexion en cours… » alors que tout refonctionnait.
- **Nettoyage après une mise à jour** : au démarrage, tous les fichiers `*.old` du dossier de LiveChat *et de ses sous-dossiers* étaient supprimés, y compris ceux de l'utilisateur quand `LiveChat.exe` est lancé depuis Téléchargements ou le Bureau. Seules les sauvegardes créées par la mise à jour sont désormais supprimées.
- **Fermeture pendant une mise à jour** : une fois l'exécutable remplacé, l'ancienne version ne pouvait plus charger ses dépendances (erreur sur `WpfAnimatedGif` dans le journal). Elles sont chargées avant le remplacement.

### Technique

- **Moitié moins de mémoire** : l'exécutable n'est plus compressé, donc les assemblies ne sont plus décompressées en mémoire au lancement. Au repos, LiveChat utilise 167 Mo au lieu de 322 Mo. En contrepartie, l'exe fait environ 155 Mo sur le disque (71 Mo auparavant) et l'archive de mise à jour passe de 63 à 65 Mo.
- Les notes de release sont tirées de ce fichier (en français) : c'est ce texte qu'affiche la fenêtre de mise à jour, au lieu de la liste des pull requests générée par GitHub.
- 22 nouveaux tests (111 au total).

## 2.0.1 — Lecture des vidéos

### Corrections de bugs

- **Vidéos Discord** : aucune ne se lisait. Le lecteur de Windows refuse les liens qui contiennent des paramètres (`?ex=…&is=…&hm=…`), or Discord signe tous ses liens de pièces jointes. Les vidéos sont maintenant téléchargées dans un fichier temporaire (500 Mo maximum, progression affichée), lues en local puis supprimées. Les fichiers restants sont nettoyés au démarrage.

### Améliorations

- **Média illisible** : au lieu de passer silencieusement au suivant, la fenêtre affiche la raison pendant 4 s (format non pris en charge, comme le WebM ; lien expiré ; fichier trop volumineux).
- **Paramètres** : un token au mauvais format (Client Secret collé à la place du Bot Token) est signalé.
- 10 nouveaux tests (89 au total).

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
- **Exécutable autonome** : un seul `LiveChat.exe` qui embarque le runtime .NET et WPF, sans rien à installer. La v1 était livrée avec une vingtaine de DLL et exigeait le .NET 8 Desktop Runtime.
- Seul `Discord.Net.WebSocket` 3.20 est référencé, au lieu du méta-paquet complet (Commands, Interactions, Webhook… inutilisés).
- Code organisé en couches (configuration, services, vues). La logique (analyse des messages, file, paramètres, mises à jour) est couverte par 79 tests unitaires.
- Le dépôt contient désormais le code source. Les binaires sont publiés dans les Releases par GitHub Actions (CI + workflow de release).
- `.gitignore` .NET (l'ancien était un modèle pour le C), `.editorconfig`, `global.json`.
