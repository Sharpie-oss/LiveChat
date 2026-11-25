# LiveChat

LiveChat affiche **en incrustation, par-dessus vos jeux et applications**, les images, GIF et vidéos postés dans un salon Discord. C'est pratique pour un stream, une soirée entre amis ou un écran partagé : chacun poste un média dans le salon et il apparaît dans une petite fenêtre toujours visible.

![Icône](src/LiveChat/Assets/LiveChat.png)

## Fonctionnalités

- **Images, GIF animés, vidéos, stickers** (pièces jointes, liens directs, GIF du sélecteur de Discord — Klipy, Tenor, Giphy —, images des aperçus de liens, messages transférés).
- **File d'attente** : les médias s'affichent l'un après l'autre, avec une barre de progression et un badge `+N` pour les médias en attente. La file est limitée, pour se protéger du spam.
- **Légendes** : le texte du message s'affiche par-dessus le média. La commande `/caption` permet d'envoyer un média avec une légende.
- **Raccourcis clavier globaux**, actifs même en jeu : **STOP** (arrête et vide la file, touche `Pause` par défaut) et **Passer**.
- **Commandes slash** `/caption`, `/skip` et `/stop`. Les deux dernières sont réservées aux membres qui peuvent gérer les messages.
- **Spoilers** floutés, avec un clic pour les dévoiler.
- **Discrétion** : la fenêtre ne vole pas le focus de votre jeu, n'apparaît pas dans Alt+Tab et se masque automatiquement quand il n'y a rien à afficher. Vous pouvez aussi l'épingler.
- **Volume** réglable avec la molette de la souris sur la fenêtre ou avec le bouton dédié. Il est mémorisé, comme la position et la taille de la fenêtre.
- **Mise en pause** de la réception depuis l'icône de notification.
- **Mise à jour automatique** depuis les Releases GitHub (recherche au démarrage puis toutes les 6 heures), avec vérification SHA-256.
- **Token chiffré** avec DPAPI (Windows), lisible uniquement par votre compte Windows.

## Installation

LiveChat est **autonome** : un seul `LiveChat.exe` (Windows 10/11 64 bits), sans .NET à installer.

1. Téléchargez `LiveChat-win-x64.zip` depuis la [dernière release](https://github.com/Sharpie-oss/LiveChat/releases/latest).
2. Décompressez-le dans un dossier où vous avez les droits d'écriture (par exemple `%LOCALAPPDATA%\LiveChat`), afin que la mise à jour automatique puisse remplacer l'exécutable.
3. Lancez `LiveChat.exe`. La fenêtre de configuration s'ouvre au premier démarrage.

## Créer le bot Discord

1. Sur le [portail développeur Discord](https://discord.com/developers/applications), créez une application (**New Application**).
2. Dans l'onglet **Bot** :
   - cliquez sur **Reset Token** et copiez le token ;
   - activez **Message Content Intent** (obligatoire pour lire les pièces jointes et les liens).
3. Invitez le bot sur votre serveur avec cette URL, en remplaçant `VOTRE_CLIENT_ID` par l'*Application ID* :

   ```
   https://discord.com/oauth2/authorize?client_id=VOTRE_CLIENT_ID&scope=bot+applications.commands&permissions=66560
   ```

   Les permissions demandées sont « Voir les salons » et « Lire l'historique des messages ».
4. Dans Discord, activez le **mode développeur** (Paramètres › Avancés), puis faites un clic droit sur le salon › **Copier l'identifiant du salon**. Vous pouvez aussi coller directement le lien du salon dans LiveChat.

## Utilisation

| Action | Comment |
|---|---|
| Afficher / masquer la fenêtre | Clic sur l'icône de notification, ou menu › *Afficher / masquer* |
| Passer au média suivant | Bouton ⏭ de la fenêtre, raccourci « Passer », menu, ou `/skip` |
| Tout arrêter | Raccourci STOP (`Pause` par défaut), menu, ou `/stop` |
| Régler le volume | Molette sur la fenêtre, ou bouton 🔊 |
| Garder la fenêtre affichée | Bouton 📌 |
| Vérifier l'affichage | Menu › *Tester l'affichage* |
| Changer les réglages | Menu › *Paramètres…* (appliqués sans redémarrer) |

### Options en ligne de commande

| Option | Effet |
|---|---|
| `--test` | Affiche la carte de test au démarrage. |
| `--preview <fichier\|url>` | Affiche un média local ou distant, par exemple pour vérifier qu'un format vidéo est lu par Windows. Peut être répété. Un fichier nommé `SPOILER_…` est flouté, comme sur Discord. |
| `LIVECHAT_DATA_DIR` (variable d'environnement) | Change le dossier des données (usage portable). |

## Données et confidentialité

- Les paramètres sont dans `%APPDATA%\LiveChat\settings.json`. Le token y est **chiffré** (DPAPI, portée utilisateur).
- L'ancien `config.json` de la v1, qui contenait le token en clair à côté de l'exécutable, est importé automatiquement puis supprimé.
- Les journaux sont dans `%APPDATA%\LiveChat\logs`, conservés 7 jours (menu › *Ouvrir le dossier des journaux*). Le token n'y est jamais écrit.
- LiveChat ne contacte que Discord, les hébergeurs des médias postés, et l'API GitHub pour vérifier les mises à jour (désactivable).

## Formats pris en charge

- **Images** : PNG, JPEG, BMP, TIFF, WebP (codec WebP de Windows), GIF animé. Un GIF plus long que la durée d'affichage est montré jusqu'au bout.
- **Stickers** : PNG et GIF ; les stickers APNG s'affichent sur leur première image, les stickers Lottie ne sont pas pris en charge.
- **Vidéos** : MP4 (H.264), MOV, WebM (VP8, VP9), MKV, WMV, AVI. Le WebM utilise les décodeurs VP8/VP9 de Windows (« Extensions vidéo VP9 », généralement préinstallées sur Windows 10 et 11) ; l'AV1 et le HEVC demandent leur extension du Microsoft Store. Une vidéo illisible affiche la raison pendant 4 s, puis LiveChat passe au média suivant.
- Les liens YouTube, Twitch, etc. ne sont pas lus : ce ne sont pas des fichiers vidéo.
- Un lien n'est affiché que si Discord en génère un aperçu. Un lien mort, ou placé entre `||barres de spoiler||`, n'a pas d'aperçu : il n'est donc pas affiché. Pour un média en spoiler, envoyez le fichier en cochant « Marquer comme spoiler ».
- La fenêtre reste au premier plan au-dessus des jeux en *plein écran fenêtré* (borderless). Les jeux en plein écran exclusif la masquent.

## Développement

Prérequis : [SDK .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0) sous Windows.

```powershell
dotnet build LiveChat.sln
dotnet test LiveChat.sln
dotnet run --project src/LiveChat -- --test
```

Pour produire l'exécutable autonome (dossier `publish/`) :

```powershell
dotnet publish src/LiveChat/LiveChat.csproj -p:PublishProfile=win-x64
```

Arborescence :

```
src/LiveChat/
  App.xaml(.cs)          Point d'entrée et orchestration
  Configuration/         Paramètres, stockage chiffré, validation, raccourcis
  Models/                Messages entrants et médias à afficher
  Services/              Discord, analyse des messages, file d'attente, mises à jour, raccourcis globaux
  Views/                 Fenêtres WPF (incrustation, paramètres, mise à jour)
  Controls/              Champ de capture de raccourci
  Tray/                  Icône de la zone de notification
  Themes/                Styles WPF (thème sombre)
tests/LiveChat.Tests/    Tests unitaires (xUnit)
```

### Publier une version

La CI (`.github/workflows/ci.yml`) compile et teste chaque push. Pour publier une version :

```powershell
git tag v2.0.0
git push origin v2.0.0
```

Le workflow `release.yml` publie alors `LiveChat-win-x64.zip` (un seul exécutable autonome non compressé d'environ 155 Mo, soit une archive d'environ 63 Mo) et sa somme SHA-256 dans une release GitHub. C'est cette archive qu'utilise la mise à jour automatique.

Voir [CHANGELOG.md](CHANGELOG.md) pour l'historique des versions.
