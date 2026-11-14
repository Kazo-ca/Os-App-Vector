> [English version](../en/plan.md)

# Plan : l'app Kazo Vector pour Kazo OS

Étape 0 du [guide de portage des apps Craft](https://github.com/Kazo-ca/Os/blob/main/doc/fr/wasm/dev/craft-app-port-guide.md)
de Kazo OS, écrite le 2026-11-14 (issue `TODO` [#1](https://github.com/Kazo-ca/Os-App-Vector/issues/1)).
Ce document fixe le but, ce que la lecture de VectorCraft a montré, les décisions, les questions
posées à Benoit et les chantiers. Chaque chantier est écrit pour être confié tel quel à un fil
d'implémentation. La référence la plus proche est Kazo Photo
([plan](https://github.com/Kazo-ca/Os-App-Photo/blob/photo/base/doc/fr/plan.md)) : même manque en
amont (pas de mode hébergé), même chemin (fork, puis fenêtre). Viennent ensuite Kazo Fig (plusieurs
documents en onglets) et Kazo Pdf (exports reçus comme copies).

## But

Kazo Vector est **l'app de dessin vectoriel de Kazo OS** : tracés, formes, texte, dégradés, plans
de travail, fichiers `.svg`, `.ai`, `.eps` et `.pdf`. Il emballe le build web de
[VectorCraft](https://github.com/storytold/vectorcraft) (équipe ArtCraft, `MIT OR Apache-2.0`, Rust
et egui, en alpha) et le branche sur les fichiers, les dialogues et les associations de Kazo OS.

**Ce qui distingue ce portage** : VectorCraft a déjà l'API de commandes (`window.vectorcraft`,
`craft-web-api`), mais **pas de mode hébergé (`craft-web-host`)**. Il faut l'écrire, dans notre fork
[`Kazo-ca/vectorcraft`](https://github.com/Kazo-ca/vectorcraft), avant que la fenêtre Kazo puisse
enregistrer quoi que ce soit dans un fichier de l'OS. Kazo Vector est donc, dès sa première
version, un build **modifié** compilé depuis le fork, comme Kazo Photo. Le fork a un seul chantier
(Kazo Photo en avait deux, l'API de commandes manquait aussi).

Le code Kazo commun (iframe, proxy, sélecteur de fichiers, raccourcis, réglages,
`DocumentSession`, exports) vient du module `Kazo.Os.AppSdk.Craft` de Kazo OS
([ADR 0259](https://github.com/Kazo-ca/Os/blob/main/doc/fr/wasm/dev/adr/0259-shared-craft-app-host-module.md),
[ADR 0264](https://github.com/Kazo-ca/Os/blob/main/doc/fr/wasm/dev/adr/0264-craft-app-exports-reach-the-window.md),
guide [`craft-apps.md`](https://github.com/Kazo-ca/Os/blob/main/doc/fr/wasm/framework/craft-apps.md)).
Version minimale : **Kazo OS 3.3.4**, déjà publiée.

## Lecture de VectorCraft (étape 0, 2026-11-14)

Lue et compilée sur l'étiquette amont `v0.24.1` (commit `f4922aa7`, 2026-11-11). Le fork
`Kazo-ca/vectorcraft` existe (`main` = copie de l'amont, commit `8998d94c` du 2026-11-13) ; il n'a
pas encore les étiquettes de l'amont.

| Point | Constat |
| --- | --- |
| Build web | `apps/vectorcraft-web` (Trunk, `public_url = "./"`, profil Cargo `release` : LTO « thin », `wasm-opt -Oz`), eframe sur **wgpu** : WebGPU, repli WebGL 2 tout seul, `?webgl` pour le forcer. Rust 1.95 au minimum. Les 27 langues sont dans le dépôt (`crates/ui-egui/src/i18n/*.tsv`) : pas de `craft-i18n` à fournir. Rust seulement, aucun JavaScript écrit à la main. |
| Mode hébergé | **Absent.** Aucune dépendance à `craft-web-host`. `apps/vectorcraft-web/src/web.rs` : Ouvrir = sélecteur du navigateur (`rfd`), tout enregistrement = téléchargement (`Services::download`). |
| API de commandes | **Présente** : `window.vectorcraft` (`craft-web-api`, révision `fd5e2fef` de `craft-libs`), installée **toujours**, sans paramètre `?control`. `request`, `execute`, `commands`, `docInspect`, `uiInspect`, `screenshot`, plus cinq fonctions propres : `inspect()`, `openFile(name, bytes)` (ouvre comme un fichier déposé), `captureDownloads(on)`, `files()`, `readFile(name)` (les enregistrements gardés dans la page au lieu d'être téléchargés, 16 au plus). Un appel abandonné par la page n'est pas exécuté (`ControlTicket::give_up`). |
| Pourquoi `openFile` et `captureDownloads` ne suffisent pas | Ils ne disent pas à la page **quand** un enregistrement a lieu, ni si c'est le document ou une copie, ni si le document est modifié ; et le document se dit enregistré dès le téléchargement. Une fenêtre Kazo bâtie dessus devrait sonder la page et deviner. Le contrat `craft-web-host` règle ces points et le module Kazo le parle déjà. |
| Canevas | `vectorcraft_canvas`, écrit en dur : une instance par document HTML, donc une iframe par fenêtre. La page porte aussi `#vectorcraft_loading` (texte de chargement, puis messages d'échec). |
| Taille | `.wasm` de **102,4 Mo (97,6 Mio)**, colle `.js` de 188 Ko ; 37,4 Mo en gzip (mesuré ici, et de même taille dans le zip officiel `vectorcraft-web-0.24.1.zip`, 38,3 Mo). Le `README` de `packaging/web` annonce 13 Mo : il n'est plus à jour. Sous les limites de Kazo OS (128 Mio par fichier ; paquet de 64 Mio compressé, 128 Mio décompressé), mais **à 2,4 % de la limite de GitHub** (100 Mio par fichier dans un dépôt). Voir « Taille du build » et la question Q1. |
| Ouvrir | `io::open_bytes(app, name, bytes, path)` accepte déjà un chemin. Les fichiers arrivés par la page (`inbox`) sont ouverts avec `path = None`. Avec un chemin, le moteur ne le garde que si le format est un format d'enregistrement et que la lecture n'a rien laissé de côté (`fileio/load.rs`) ; sinon le document est « sans fichier » et porte le nom comme titre. |
| Enregistrer | `io::save::save` puis `write_plan`. Formats du document (`SAVE_FORMATS`) : `vectorcraft` (natif), `template` (`.vctemplate`), `pdf`, `svg`, `svgz`, `ai`, `eps`. `SavePlan::retargets()` distingue l'enregistrement **du document** (Enregistrer, Enregistrer sous) d'une copie (Enregistrer une copie, Enregistrer comme modèle). Un enregistrement peut écrire **plusieurs fichiers** (option « plans de travail séparés », images liées). |
| Enregistrer sur le web | Avec un chemin : téléchargement direct sous le nom du fichier. Sans chemin : VectorCraft ouvre son propre dialogue `saveOptions` (nom et format), puis les options du format (SVG, PDF), puis télécharge. Vérifié à l'essai : `file.save` sur un SVG ouvert par `openFile` répond `{ pending: "saveOptions" }`. |
| « Modifié » | `SaveJob::complete` marque le document enregistré **dès que le téléchargement est lancé** : le navigateur ne confirme rien. C'est le piège « l'app se dit propre avant l'écriture » du guide (craft-libs #611). |
| Exports et autres fichiers | Tous par le même `Services::download` : Exporter sous, Exporter pour les écrans (plusieurs fichiers en un `.zip`), Enregistrer pour le web, Assemblage (`.zip`), Enregistrer pour Office, CSS, rapports, et les fichiers de préréglages (nuanciers, styles, bibliothèques, actions, raccourcis, préréglages PDF et d'impression…). |
| Documents | Plusieurs documents en **onglets** dans une page. Chaque document a un identifiant stable (`uid`) dans le moteur, mais `ui.inspect` ne donne que `documents[{ title, dirty }]` et `activeDocument` (la place de l'onglet) : ni `uid` ni chemin. |
| Deux onglets, un fichier | Ouvrir un chemin déjà ouvert **ramène l'onglet existant** (`same_open_path`). Dans le navigateur les chemins sont de simples noms : deux fichiers de même nom venant de deux dossiers seraient pris pour le même. |
| Commandes | `engine.execute { command, params }`, `ui.menu.invoke`, `engine.commands`, `ui.inspect`, `ui.set`, `ui.dialog.set` / `.confirm` / `.cancel`, `ui.screenshot`. Fichier : `file.new`, `file.open`, `file.save`, `file.saveAs`, `file.saveCopy`, `file.saveAsTemplate`, `file.close`, `file.closeAll`, `file.revert`, `file.exportAs`, `file.print`. Documents : `document.activate { index }`. `file.close` ouvre un dialogue `saveChanges` par document modifié. `app.open` et `app.save` demandent des accès fichier que le web n'a pas. |
| Thème | `ui.set { brightness }` : `dark`, `mediumDark` (défaut), `mediumLight`, `light`. Vérifié à l'essai. |
| Langue | `prefs.set { key: "interfaceLanguage", value }`, `auto` par défaut (langues du navigateur, ou `?lang=`). 27 catalogues, plus l'anglais : `bg bs ca cs da de el es fi fr hi hu id it ja ka ko nl pl pt-br ru sv tr uk vi zh-hans zh-hant`. Vérifié à l'essai (`fr`). |
| Sélecteur du navigateur (`rfd`) | Trois usages : Ouvrir (un fichier), Importer (plusieurs fichiers), Scripts › Autre script. Tous passent par un `input type=file` : le sélecteur du module les remplace (`PickRequested`). Les demandes de **dossier** n'existent pas sur le web. |
| Formats ouverts | Documents vectoriels : `vectorcraft drawcraft vctemplate svg svgz ai ait pdf eps epsf ps dxf emf wmf af afdesign afpub`. Images (ouvertes comme un document qui contient l'image) : `png pns jpg jpeg gif webp tif tiff bmp dib rle tga vda icb vst pcx pxr jpf jpx jp2 j2k j2c jpc heic heif psd pdd psb jxl`. S'ouvrent aussi, sans créer de document : nuanciers (`.vcswatches .gpl .ase .acb`), styles (`.vcstyles`), bibliothèques (`.vclibrary`), préréglages (`.vcflattener .vcpdfpresets .vcprintpresets .vcperspective .vcexportpresets`), jeux de raccourcis (`.kys`), scripts (`.jsx`, vérifiés sans être lancés) et modules d'extension (`.wasm`, installés). |
| Réglages | **Le build web ne garde aucun réglage** : à l'essai, `localStorage` reste vide après un changement de thème et de langue (le `README` de `packaging/web` dit le contraire). Chaque page démarre avec les réglages par défaut. |
| Récupération de données | Le seul usage de `localStorage` : des copies de secours des documents modifiés (`vectorcraft-recovery/<zone>/<nom>`, en base64), offertes au démarrage suivant après un arrêt brutal, avec des verrous Web Locks entre onglets. Écrites aussi quand le rendu graphique est perdu. Voir la question Q3. |
| Polices | Dans le `.wasm` : Source Sans 3, Source Serif 4, Inter, JetBrains Mono, et BIZ UDPGothic quand `CRAFT_FONTS_DIR` est fourni. À côté du `.wasm` (`fonts/<sha16>/`, vérifiées par SHA-256) : les polices de `crates/text/web-fonts.txt` que le clone de `craft-fonts` contient. À la révision du build officiel (`b076417b`), ce sont trois polices (Noto Sans Arabic, Thai et Georgian, 1,3 Mo) chargées avant le démarrage ; les autres familles arabes de la liste n'y sont pas encore. Pas de `fonts/manifest.txt` à la demande comme PhotoCraft. |
| Rendu perdu | Si le navigateur retire le rendu graphique, l'app se relance sur un nouveau canevas (trois fois au plus), puis abandonne avec un message dans `#vectorcraft_loading`. |
| Impression | Fichier › Imprimer met le PDF dans une iframe cachée et appelle l'impression du navigateur : le dialogue du navigateur sort de l'OS. |
| Presse-papiers, pipette d'écran, stylet | API du navigateur (`EyeDropper`, événements de collage, Pointer Events) : marchent dans l'iframe, que le module ouvre avec `clipboard-read` et `clipboard-write`. |
| Taille de fenêtre | Bureau : 1440 × 900, minimum 800 × 500. |
| Marque | Le nom et les logos ArtCraft sont des marques (`docs/brand/LICENSE-brand.txt`) : un build modifié doit les retirer et ne pas se présenter comme un produit ArtCraft ; « basé sur VectorCraft de l'équipe ArtCraft » en texte est permis. Vu à l'essai : bouton « Discord » dans la barre du haut, sur l'écran d'accueil et dans À propos ; liens « ArtCraft website », « VectorCraft on getartcraft.com », « Source code on GitHub » sur l'accueil et dans À propos ; « Fait partie d'ArtCraft » dans À propos ; l'icône du dragon en haut à gauche et dans À propos (`assets/app-icon`, « the owner's original ArtCraft drawing », licence à lire dans son `LICENSE.txt`). |
| Règles du dépôt amont | `AGENTS.md` : Rust seulement, pas de `unwrap`/`panic` hors tests, pas d'`unsafe`, tout est une commande, ne jamais casser wasm, pas de changement de CI dans une PR de fonctionnalité, **aucune mention d'agent** dans une PR, une issue avant une PR. |

### Essai (2026-11-14)

Fait sur l'étiquette `v0.24.1` (`f4922aa7`), dans le conteneur de cette session (4 cœurs, Linux), sans aucun changement au code.

| Point | Résultat |
| --- | --- |
| Outils | Rust stable 1.97.0, cible `wasm32-unknown-unknown`, Trunk 0.21.14 (binaire), `craft-fonts` à la révision du build officiel (`b076417b`, `CRAFT_FONTS_DIR`). |
| Commande | `trunk build --release` dans `apps/vectorcraft-web`. |
| Durée | 18 min 40 s à froid : 14 min de `cargo`, puis 4 min de `wasm-opt`. |
| Sortie | `dist/web` : `index.html`, un `.js` de 188 159 octets, un `.wasm` de 102 359 248 octets (129,8 Mo avant `wasm-opt` ; 37,4 Mo en gzip), `fonts/` (3 polices, 1,3 Mo), `icons/`, `snippets/`. |
| Démarrage | Chromium sans carte graphique (WebGL 2 par SwiftShader, `?webgl`) : l'app affiche son écran d'accueil 0,6 s après le chargement de la page depuis un serveur local. |
| API de page | `window.vectorcraft` présent sans paramètre, avec les onze fonctions. |
| Ouvrir | `vectorcraft.openFile("logo.svg", octets)` ouvre le SVG dans un onglet ; `ui.inspect` donne `documents: [{ title: "logo.svg", dirty: false }]`. |
| Modifier | `shape.rectangle` : `dirty` passe à `true`, le titre de la page devient `*logo.svg — VectorCraft`. |
| Enregistrer | `file.save` répond `{ pending: "saveOptions" }` : sans chemin, VectorCraft demande le nom et le format. C'est ce que H2 corrige. |
| Thème, langue | `ui.set { brightness: "light" }` et `prefs.set { key: "interfaceLanguage", value: "fr" }` changent l'interface tout de suite. |
| Stockage | `localStorage` vide au démarrage et après ces changements. |
| Console | Aucune erreur ; un avertissement « Software rasterizer detected », attendu ici. |

Captures : `vector/trials/` dans les fichiers du projet (hors dépôt).

### Taille du build

Le `.wasm` officiel est trop près de la limite de GitHub pour être rangé dans un dépôt : la
prochaine version de VectorCraft peut la dépasser. Deux essais de réglages de taille, faits par des
variables d'environnement de Cargo (`CARGO_PROFILE_RELEASE_*`), donc **sans changer le code ni le
fork** :

| Build | `.wasm` | gzip | Durée à froid | Résultat |
| --- | --- | --- | --- | --- |
| Profil officiel (`opt-level = 3`, LTO « thin », `wasm-opt -Oz`) | 102 359 248 octets (97,6 Mio) | 37,4 Mo | 18 min 40 s | démarre en 0,6 s |
| Taille (`opt-level = "s"`, LTO « thin », `strip`, `wasm-opt -Oz`) | **71 694 182 octets (68,4 Mio)** | **29,2 Mo** | 10 min 30 s (8 min 19 s de `cargo`, 2 min 11 s de `wasm-opt`) | démarre en 0,5 s ; même parcours d'essai réussi (ouvrir, modifier, `file.save`, thème, langue) |
| Taille avec LTO « fat » | — | — | — | l'édition des liens est arrêtée faute de mémoire (plus de 14 Gio) : à ne pas retenir, les machines de GitHub Actions n'en ont pas plus |

Le build « taille » est 30 % plus petit et laisse 32 % de marge sous la limite de GitHub. Deux
réserves :

- **La vitesse de dessin n'a pas été mesurée.** `opt-level = "s"` rend en général le code un peu
  plus lent ; PhotoCraft compile pourtant son build web ainsi (profil `wasm-release`). À mesurer au
  chantier 2 sur un grand dessin, dans les deux builds.
- Avec `strip = true`, la section qui nomme les fonctions wasm utilisées (SIMD, mémoire en bloc)
  disparaît et le `wasm-opt` lancé par Trunk refuse le fichier. À l'essai, `wasm-opt` a été lancé à
  la main avec les options `--enable-…`. Le script du chantier 2 prend `strip = "debuginfo"` ou
  passe ces options à Trunk, et remesure.

Le catalogue du Store (`Os-AppMarketPlace`) compile l'app au commit de l'étiquette et ne prend
aucun binaire de l'auteur : le `.wasm` doit donc pouvoir être obtenu à partir de ce commit, soit
parce qu'il y est, soit parce que la compilation .NET le télécharge et en vérifie le SHA-256.

## Ce qu'il faut ajouter à VectorCraft (fork)

Le contrat est celui de `craft-web-host` (`storytold/craft-libs`, version « 1 »), le même que les
autres apps Craft, avec le préfixe `vectorcraft:`. Le code vit dans `apps/vectorcraft-web`, sur le
modèle de `apps/photocraft-web/src/{host.rs,hostmode.rs}` du fork
[`Kazo-ca/photocraft`](https://github.com/Kazo-ca/photocraft/tree/kazo/base) (commit `e136e791`) et
de `apps/gridcraft-web/src/{host.rs,hostmode.rs}`. Trois points demandent en plus une petite
ouverture dans `crates/ui-egui` (marqués ★), parce que la coquille web ne voit pas aujourd'hui ce
qu'il lui faut.

| # | Comportement en mode hébergé (`data-host="page"` ou `?host=parent`) | Pourquoi |
| --- | --- | --- |
| H1 | `vectorcraft:ready { version: "1", appVersion, file: false }` quand l'app écoute ; `vectorcraft:failed { error }` si elle ne démarre pas, **et quand le rendu est perdu pour de bon** (`give_up`). | La fenêtre Kazo sait quand envoyer le fichier et quand montrer une erreur. |
| H2 | `vectorcraft:open { name, bytes }` ouvre le fichier comme Fichier › Ouvrir, refusé au-delà d'une limite avant d'être copié. Le document ouvert par l'hôte **reçoit son nom comme chemin** (`open_bytes(…, Some(name))`) ; le moteur ne le garde que pour un format d'enregistrement lu en entier. | Avec un chemin, Enregistrer réécrit le même fichier au lieu d'ouvrir le dialogue `saveOptions`. |
| H3 | ★ L'enregistrement **du document** (`SavePlan::retargets()`) part en `vectorcraft:save { name, bytes, saveAs, id }`. `saveAs` est faux quand le document est écrit sous son propre chemin, vrai dans les autres cas (Enregistrer sous, premier enregistrement, autre format). Le dialogue `saveOptions` de VectorCraft reste : il choisit le format, puis l'enregistrement part avec `saveAs` vrai. | L'hôte sait s'il réécrit le fichier lié ou s'il doit demander où enregistrer. |
| H4 | ★ Le document n'est marqué enregistré (`SaveJob::complete`) qu'à la réception de `vectorcraft:saved { id }` ; sans réponse, il reste modifié. Le `SaveJob` garde l'état du document au moment de l'enregistrement : une confirmation tardive ne marque pas propre un document modifié depuis. | Corrige pour VectorCraft le piège « l'app se dit propre avant l'écriture » (craft-libs #611). |
| H5 | Les **copies, modèles, exports, assemblages et fichiers de préréglages** gardent le téléchargement (`<a download>`). Un enregistrement du document qui écrit **plusieurs fichiers** (plans de travail séparés, images liées) envoie le fichier du document à l'hôte et laisse les autres en téléchargement. | Le module Kazo intercepte les téléchargements et les remet à la fenêtre comme copies (ADR 0264) ; rien à inventer. |
| H6 | `vectorcraft:dirty { dirty }` quand un des documents ouverts a des modifications, ou n'en a plus. | Titre de la fenêtre. |
| H7 | ★ `ui.inspect` donne pour chaque document `id` (son `uid`), `path` et `active`, en plus de `title` et `dirty` : la forme de WordCraft, que `OpenDocument.ListFrom` du module lit déjà. | Lier chaque onglet à son fichier sans se fier à la place de l'onglet ni au titre. |
| H8 | Hors mode hébergé, rien ne change (téléchargements, sélecteur du navigateur, `openFile`, `captureDownloads`). | Le patch reste proposable en amont. |
| H9 | En mode hébergé : pas de bouton « Discord » (barre du haut, accueil, À propos), pas de liens vers ArtCraft sur l'accueil ; À propos nomme le projet en texte simple, sans « Fait partie d'ArtCraft » ; l'icône du dragon est vérifiée contre sa licence (`assets/app-icon/LICENSE.txt`, `docs/brand/`) et retirée de l'interface si c'est une marque. | Build modifié. |
| H10 | En mode hébergé (Q3 = A), la récupération de données n'écrit pas dans `localStorage`. | Le stockage du navigateur est partagé entre les utilisateurs Kazo et avec l'OS. |

Tests demandés dans le fork : tests unitaires des règles pures (`saveAs`, confirmation, limite de
taille, forme de `ui.inspect`), `cargo fmt --check`, `cargo clippy -D warnings`,
`cargo xtask layers`, `cargo xtask wasm`, un build Trunk, et un parcours Playwright sur une page
d'essai qui joue l'hôte (`open`, `save`, `saved`, `dirty`, `failed`, copie téléchargée).

## Décisions

| # | Sujet | Décision |
| --- | --- | --- |
| 1 | Nom et identité | « **Kazo Vector** ». Id `ca.kazo.vector`, éditeur `pub.kazo`, assembly `Kazo.Vector`, catégorie `graphics` (celle de Kazo Fig et de Kazo Photo), plusieurs fenêtres, fenêtre de 1280 × 820, contenu minimum 800 × 500. |
| 2 | Code commun | Module `Kazo.Os.AppSdk.Craft`, **Kazo OS 3.3.4** au minimum (exports de l'ADR 0264 compris). Sous-module `external/KazoOs` sur l'étiquette `v3.3.4`, paquets de nuget.org : pas de branche de l'OS à suivre. |
| 3 | Changement d'écran | `canChangeScreen: false` dans le manifeste dès le chantier 2 (issue [#2](https://github.com/Kazo-ca/Os-App-Vector/issues/2), ADR 0263). |
| 4 | Mode hébergé | Dans notre fork maintenant, et proposé en amont en parallèle : la réponse de Benoit à la même question pour Kazo Photo (Q1 = A, 2026-11-13), reprise ici. |
| 5 | Provenance du build | Toujours compilé depuis le fork : branche `kazo/v0.24.1` (= étiquette amont `v0.24.1`, commit `f4922aa7`), branche d'intégration `kazo/base` créée d'elle. Sous-module `external/vectorcraft` sur `kazo/base`. Sortie de Trunk (`dist/web`, pas le zip de `packaging/web/package.sh`) avec `vectorcraft.json` (version, commit, noms et SHA-256). Où vit le `.wasm` : question Q1. |
| 6 | Page de l'iframe | `wwwroot/host/index.html` : canevas `vectorcraft_canvas` avec `data-host="page"`, `data-craft-app="vectorcraft"`, `data-craft-build="../vectorcraft/vectorcraft.json"`, plus l'élément `#vectorcraft_loading` de la page amont. Pas de paramètre `control` : `window.vectorcraft` est toujours là. |
| 7 | Onglets | On garde les onglets de VectorCraft : une fenêtre tient plusieurs documents, chaque onglet lié à son fichier ; un double-clic dans l'explorateur ouvre une nouvelle fenêtre. La réponse de Benoit pour Kazo Photo (Q2 = A), reprise ici. |
| 8 | Enregistrement | `Saving` : règle du module (`DocumentSession.Decide(name, saveAs)`), la fenêtre renvoie `true` seulement quand le fichier est écrit, ce qui déclenche `saved` (H4). `Exporting` : toujours une copie (`DecideCopy`). |
| 9 | Deux fichiers de même nom | Un nom n'est lié qu'une fois par fenêtre. Ouvrir un fichier dont le nom est déjà lié dans cette fenêtre l'ouvre dans une **nouvelle fenêtre** : VectorCraft ramènerait sinon l'onglet de l'autre fichier. |
| 10 | Garde de fermeture | Lue dans `ui.inspect` (H7) ; la fenêtre active chaque document modifié (`document.activate`), lance `file.save` et attend la fin de **ses** écritures. |
| 11 | Thème et langue | `ui.set { brightness }` : `mediumDark` (le défaut de VectorCraft) pour le thème sombre de l'OS, `light` pour le clair, au démarrage et à chaque changement. Langue : `prefs.set` de `interfaceLanguage` au démarrage (`CraftLanguage.Pick` sur les 27 codes). |
| 12 | Réglages | Le build web n'en garde aucun (voir la lecture) : rien à ranger par utilisateur dans la v1, et chaque fenêtre démarre avec les réglages par défaut, le thème et la langue de l'OS. Limite écrite dans la fiche. Les faire garder par VectorCraft est une demande à l'amont. |
| 13 | Raccourcis | `ReservedKeys.Os` **sans** `Ctrl+N` : dans un éditeur à onglets, `Ctrl+N` est « Nouveau document » de VectorCraft. Les conflits entre les accords de l'OS et ceux de VectorCraft sont relevés au chantier 3 et notés dans la fiche. |
| 14 | Polices | Le build suit le build officiel : `CRAFT_FONTS_DIR` sur `craft-fonts` à la révision `b076417b`, soit les polices intégrées et trois polices servies (1,3 Mo, licences OFL). Pas d'autres polices dans la v1. |
| 15 | Impression | Fichier › Imprimer garde le dialogue d'impression du navigateur dans la v1. Limite écrite dans la fiche. |
| 16 | Grands fichiers | La limite de Kazo OS (128 Mio par fichier, ADR 0245) s'applique ; un fichier plus grand est refusé avec un message. |
| 17 | Piste amont | Les textes des issues (en anglais, sans mention d'agent) sont préparés dans `/mnt/project-files/vector/upstream/` par le fil du fork ; Benoit les publie. Les branches du fork sont écrites pour pouvoir devenir des PR amont. |
| 18 | ADR de Kazo OS | Aucune prévue : le module a déjà ce qu'il faut. Si un chantier en demande une, prochain numéro libre **0271** au 2026-11-14 (à revérifier sur `main`, les PR ouvertes et toutes les branches `*/base` au moment de l'écrire ; Kazo Deck et Kazo Design avancent en parallèle). |

## Questions posées à Benoit

Posées dans le fil le 2026-11-14. **Benoit a répondu A aux trois le même jour** :

- **Q1 = A** : build compilé avec les réglages de taille et gardé dans le dépôt ; le script échoue
  au-delà de 90 Mio.
- **Q2 = A** : app par défaut pour les formats vectoriels que rien d'autre n'ouvre, « Ouvrir avec »
  pour `.pdf` et `.afdesign`, rien pour les images.
- **Q3 = A** : récupération de données coupée dans la v1 (H10), avec une issue `TODO` pour la
  refaire plus tard dans le dossier privé de l'utilisateur :
  [#4](https://github.com/Kazo-ca/Os-App-Vector/issues/4). Benoit a demandé la même issue pour
  Kazo Photo ([Os-App-Photo#11](https://github.com/Kazo-ca/Os-App-Photo/issues/11)) et Kazo Fig
  ([Os-App-Fig#9](https://github.com/Kazo-ca/Os-App-Fig/issues/9)).

Les questions telles qu'elles ont été posées :

**Q1. Où vit le build de VectorCraft ?** Kazo Photo garde son build dans le dépôt (39 Mo) parce
que le Store ne compile pas de Rust. Ici le `.wasm` officiel pèse 102 Mo, à 2,4 % de la limite de
GitHub (100 Mio par fichier).

- **A (recommandée)** : compiler avec les réglages de taille et **garder le build dans le dépôt**,
  comme Kazo Photo. `.wasm` de 72 Mo, 32 % sous la limite ; rien à changer dans le catalogue ni
  dans le fork. Le script de compilation échoue si le `.wasm` dépasse 90 Mio, et on passe alors à
  B. À payer : chaque mise à jour de VectorCraft ajoute environ 70 Mo à l'historique du dépôt, et
  le dessin peut être un peu plus lent (mesuré au chantier 2 ; si l'écart gêne, on passe à B avec
  le profil officiel).
- B : garder le profil officiel (102 Mo) et **ne pas mettre le `.wasm` dans le dépôt**. Un
  workflow le compile et le publie comme fichier d'une release GitHub de ce dépôt ; le dépôt ne
  garde que `vectorcraft.json` (nom, taille, SHA-256), et la compilation .NET télécharge le
  fichier et le vérifie. Le dépôt reste léger et la vitesse est celle de l'amont, mais c'est un
  mécanisme de plus à écrire et à tester (chantier 2 plus long), le catalogue compile alors avec
  un fichier téléchargé (épinglé par son SHA-256) et une compilation sans réseau ne marche plus.
- C : Git LFS. Le dépôt reste léger sans mécanisme maison, mais il faut activer LFS dans la
  compilation du catalogue, et LFS a un quota de stockage et de transfert que chaque
  compilation de CI entame de 100 Mo.

**Q2. Formats associés à Kazo Vector.** Aujourd'hui aucune app n'ouvre `.svg` par défaut (Kazo
Photo et Kazo Fig l'offrent dans « Ouvrir avec »). Kazo Pdf ouvre `.pdf`. Kazo Photo est l'app par
défaut des fichiers Affinity (`.af .afdesign .afpub`), de `.psd` et des images.

- **A (recommandée)** : app par défaut pour les formats vectoriels que rien d'autre n'ouvre :
  `.vectorcraft .vctemplate .svg .svgz .ai .ait .eps .epsf .ps .dxf .emf .wmf`. « Ouvrir avec »
  seulement pour `.pdf` (Kazo Pdf reste l'app par défaut) et `.afdesign` (Kazo Photo l'a déjà).
  Rien pour les images (`.png`, `.jpg`, `.psd`…) : Kazo Vector les place dans un document par
  Fichier › Importer, il n'a pas à les ouvrir par double-clic.
- B : comme A, et « Ouvrir avec » pour les images aussi. Le menu « Ouvrir avec » de chaque photo
  s'allonge d'une app qui n'est pas faite pour ça.
- C : app par défaut seulement pour `.vectorcraft` et `.vctemplate`, tout le reste en « Ouvrir
  avec ». Un double-clic sur un `.svg` ou un `.ai` n'ouvre alors rien tant que l'utilisateur n'a
  pas choisi.

**Q3. Récupération de données après un arrêt brutal.** VectorCraft garde des copies de secours des
documents modifiés dans le stockage du navigateur (`localStorage`). Dans Kazo OS ce stockage est
celui du site de l'OS : partagé par tous les utilisateurs Kazo du même navigateur, limité à
environ 5 Mo, et utilisé par l'OS lui-même.

- **A (recommandée)** : coupée dans la v1 (H10). Après un arrêt brutal du navigateur, le travail
  non enregistré est perdu, comme dans Kazo Photo et Kazo Fig. Aucun document d'un utilisateur
  n'est offert à un autre, et l'OS garde son stockage.
- B : laissée telle quelle. La récupération marche, mais un utilisateur Kazo peut se voir offrir
  le document d'un autre, et des copies volumineuses peuvent remplir le stockage dont l'OS a
  besoin.
- C : copies de secours rangées dans le dossier privé de l'utilisateur Kazo. C'est la bonne
  solution à terme, mais elle demande un échange de plus entre VectorCraft et la fenêtre (hors
  du contrat `craft-web-host`) : un chantier de plus dans le fork et dans la fenêtre, à faire
  après la v1.

## Chantiers

Deux dépôts : le fork `Kazo-ca/vectorcraft` (Rust) et `Kazo-ca/Os-App-Vector` (C#). Dans chacun,
une branche d'intégration, une branche et une PR par chantier ; un fil d'implémentation par
chantier. Benoit fusionne `vector/base` dans `main` ; `main` du fork reste la copie de l'amont.

| N° | Dépôt, branche (vers) | Contenu | Dépend de |
| --- | --- | --- | --- |
| 1 | Os-App-Vector, `vector/1-plan` (`vector/base`) | ce plan (fr et en) | — |
| F1 | vectorcraft, `kazo/1-web-host` (`kazo/base`) | H1 à H10 : mode hébergé | plan fusionné |
| 2 | Os-App-Vector, `vector/2-solution` (`vector/base`) | solution, manifeste, compilation depuis le fork, page d'iframe, fenêtre qui démarre | plan fusionné ; « terminé » attend F1 |
| 3 | Os-App-Vector, `vector/3-window` (`vector/base`) | ouvrir, enregistrer, onglets, exports, garde de fermeture, sélecteur, raccourcis, thème, langue | 2 et F1 |
| 4 | Os-App-Vector, `vector/4-store` (`vector/base`) | fiche, README, licences, mesures, tag, catalogue, `features.md` | 3 |

**Ordre** : F1 dans un fil, le chantier 2 dans un deuxième fil, en même temps ; puis 3 ; puis 4.
Kazo OS 3.3.4 est publié : le chantier 4 n'attend aucune version de l'OS.

### F1. Mode hébergé (fork)

- Créer `kazo/v0.24.1` sur le commit amont `f4922aa7` (`git fetch https://github.com/storytold/vectorcraft refs/tags/v0.24.1`), puis `kazo/base` de cette branche.
- `apps/vectorcraft-web/src/host.rs` et `hostmode.rs` sur le modèle du fork de PhotoCraft : `Host::connect("vectorcraft", canvas)`, écoute de `open` et `saved`, `post_ready`, `post_save`, `post_dirty`, `post_failed`. `craft-web-host` épinglé à une révision de `storytold/craft-libs`, comme `craft-web-api`.
- Les ouvertures ★ dans `crates/ui-egui`, les plus petites possibles et utiles hors de Kazo : un service « écriture du document » à côté de `download` (H3), la confirmation qui appelle `SaveJob::complete` (H4), `id`, `path` et `active` dans `ui.inspect` (H7), un drapeau « hébergé » pour H9. Lire `io/save.rs` (`write_plan`, `save`), `io/open.rs` (`open_bytes`), `background.rs` et `control.rs` avant de choisir la forme ; le fork de PhotoCraft montre une forme qui marche (`Services::write_document`, `DocumentWrite`, `confirm_document_write`, `Services::hosted`).
- H9, H10, et une ligne dans `docs/development.md` (« Web build ») qui décrit le mode hébergé.
- Brouillons des issues amont dans `/mnt/project-files/vector/upstream/` (mode hébergé ; `id` et `path` dans `ui.inspect` ; réglages non gardés sur le web ; `README` de `packaging/web` qui annonce 13 Mo), en anglais, sans mention d'agent.
- **Terminé quand** : la page d'essai qui joue l'hôte ouvre un SVG et un `.vectorcraft`, reçoit `save` avec le bon `saveAs` dans les quatre cas (réécriture d'un SVG, premier enregistrement d'un nouveau document, Enregistrer sous, changement de format), le document ne redevient propre qu'après `saved`, Enregistrer une copie et Exporter sous téléchargent toujours, et la même page sans `data-host` se comporte comme avant. Le fil donne le commit de `kazo/base` à épingler.

### 2. Solution et compilation (`vector/2-solution`)

- Solution renommée (`Kazo.Vector`, `Kazo.Vector.Os`, `Kazo.Vector.Tests`), `[AppManifest]` de la décision 1, `canChangeScreen: false`, capacités de Kazo Photo, associations de Q2 = A.
- Sous-module `external/KazoOs` sur `v3.3.4`, sous-module `external/vectorcraft` sur `kazo/base`.
- `tools/build-vectorcraft.sh` (Rust stable du jour, cible `wasm32-unknown-unknown`, Trunk 0.21.14, `CRAFT_FONTS_DIR` sur `craft-fonts` à `b076417b`, `trunk build --release` dans `apps/vectorcraft-web`, réglages de taille de Q1 = A : `opt-level = "s"`, LTO « thin », échec au-delà de 90 Mio ; le build est gardé dans `wwwroot/vectorcraft`), workflow `build-vectorcraft.yml`, `vectorcraft.json`, test xUnit « le commit de `vectorcraft.json` est celui du sous-module ». Modèles : `Os-App-Photo` (`photo/base`) et `Os-App-PDF`.
- Page d'iframe (décision 6) et fenêtre sur `CraftFrame` et `FrameStartup` : chargement, app, erreur, « Réessayer » ; thème et langue (décision 11).
- Tant que F1 n'est pas fusionné, compiler `kazo/v0.24.1` pour roder l'outillage ; le parcours qui attend `ready` vient avec F1 (repasser alors l'épingle sur `kazo/base`).
- **Terminé quand** : l'app démarre en WebGPU et en WebGL 2, deux fenêtres tournent ensemble, le paquet `.kazoapp` s'installe dans Kazo OS 3.3.4 ; taille du paquet, temps de démarrage et mémoire par fenêtre notés ici.

### 3. Fenêtre (`vector/3-window`)

- Ouverture : fichier demandé au lancement, `OpenAsync`, décision 9. Fichiers qui ne créent pas de document (nuanciers, bibliothèques, préréglages…) : envoyés à VectorCraft sans liaison. Fichier › Ouvrir de VectorCraft montre le dialogue de l'OS (`PickRequested`).
- Enregistrement : décision 8 ; liste de formats (`CraftDocumentFormats` : `vectorcraft vctemplate pdf svg svgz ai eps`) ; les quatre cas de F1 de bout en bout dans Kazo OS ; un dialogue annulé ou une écriture refusée laisse le document modifié.
- Onglets (décision 7) : liaison de chaque document (`id` de H7) à son chemin, titre de la fenêtre sur l'onglet actif, garde de fermeture (décision 10).
- Exports comme copies (`Exporting`, `.zip` compris), sélecteur de fichiers (un ou plusieurs fichiers), raccourcis (décision 13), thème en direct.
- Mesure d'un grand fichier (un SVG ou un `.ai` de plusieurs dizaines de Mo) : temps d'ouverture et d'enregistrement, mémoire.
- **Terminé quand** : aucun cas ne perd de modifications sans que l'utilisateur l'ait choisi, aucun dialogue de fichier ni téléchargement du navigateur ne sort de l'app (l'impression exceptée, décision 15), et les raccourcis de l'OS marchent le curseur dans le dessin.

### 4. Store (`vector/4-store`)

- Icône propre à ce dépôt, sans marque ArtCraft ; fiche fr et en (« basé sur VectorCraft de l'équipe ArtCraft », limites connues : alpha, 128 Mio par fichier, réglages non gardés, pas de récupération après un arrêt brutal (issue #4), impression par le navigateur, fenêtre qui reste sur son écran, taille du téléchargement) ; captures ; README refait sur la fiche (`store:readme`, « Essayer » avec `?autolaunch=ca.kazo.vector`) ; `THIRD-PARTY-NOTICES` (VectorCraft, ses avis, polices OFL).
- Parcours dans un Kazo OS 3.3.4 publié (`tests/installed`), workflow « Tag a release », PR au catalogue `Os-AppMarketPlace` (la signature se fait là, aucune clé ici), ligne de `features.md` dans `Kazo-ca/Os`.
- **Terminé quand** : Kazo Vector s'installe depuis le Store, ouvre un `.svg` par double-clic et l'enregistre.

## Piste en amont

Brouillons en anglais, publiés par Benoit, dans `/mnt/project-files/vector/upstream/` (écrits par
le fil F1) :

- VectorCraft : mode hébergé du build web (`craft-web-host`), avec H1 à H6 ;
- VectorCraft : `id`, `path` et `active` dans les documents de `ui.inspect` ;
- VectorCraft : le build web ne garde pas les réglages ; le `README` de `packaging/web` annonce
  13 Mo pour un `.wasm` de 102 Mo ;
- `craft-libs` : se joindre à #611 (confirmation d'enregistrement) et #610 (`open-request`).

Non vérifié : les issues déjà ouvertes en amont sur ces sujets (`storytold` est hors de portée de
la session pour l'API de GitHub) ; Benoit regarde avant de publier.

## Avancement

- [x] Étape 0 : lecture de VectorCraft 0.24.1, compilation et essai du build web (2026-11-14).
- [x] Plan fr et en.
- [x] Réponses de Benoit à Q1 à Q3 : A, A, A (2026-11-14) ; issue #4 pour la récupération de données.
- [x] F1 : mode hébergé dans le fork (2026-11-14, Kazo-ca/vectorcraft#1, `kazo/base` = `326de0f4`).
  Page d'essai `data-host="page"` : SVG et `.vectorcraft` ouverts, `save` avec le bon `saveAs` dans
  les quatre cas, document propre seulement après `saved`, copie et export téléchargés, rien de
  changé sans `data-host` (19 vérifications Playwright). `.wasm` 71 859 008 octets (`opt-level = "s"`),
  compilation 12 min 26 s, `ready` en 1,3 s environ. Brouillons d'issues amont (en anglais) dans
  `/mnt/project-files/vector/upstream/vectorcraft-web-host-mode.md`, à publier par Benoit.
- [x] Chantier 2 : solution, manifeste, compilation depuis le fork, page d'iframe, fenêtre
  (2026-11-14 ; le test qui attend `ready` attend F1, puis le sous-module passe sur `kazo/base`).

### Chantier 2 : mesures (2026-11-14)

Conteneur de la session (4 cœurs, Linux, sans carte graphique : WebGL 2 et WebGPU par
SwiftShader). VectorCraft compilé depuis `kazo/v0.24.1` (`f4922aa7`), la version amont sans
changement.

| Point | Résultat |
| --- | --- |
| Compilation | Réglages de `tools/build-vectorcraft.sh` (`opt-level = "s"`, LTO « thin », une unité de code, `strip = "debuginfo"`) : 11 min 55 s à froid. Avec `strip = "debuginfo"`, le `wasm-opt` de Trunk prend le fichier tel quel (le piège de `strip = true` vu à l'essai de l'étape 0 ne se produit pas). |
| `.wasm` | **71 830 257 octets (68,5 Mio)**, 30 % sous le build officiel, 25 % sous la limite de 100 Mio de GitHub ; le script refuse tout ce qui dépasse 90 Mio. Colle de 183 703 octets. `wwwroot/vectorcraft` pèse 71 Mo avec les trois polices servies et les licences. |
| Paquet `.kazoapp` | **30 448 921 octets (29,0 Mio)**, sous la limite de 64 Mio de Kazo OS. |
| Installation | `kazopkg install` dans Kazo OS 3.3.4 publié (`npm run test:installed`) : OK. |
| Démarrage, WebGL 2 | première fenêtre 10,7 s (téléchargement et compilation du `.wasm` compris), deuxième 1,9 s. |
| Démarrage, WebGPU | première fenêtre 10,2 s, deuxième 0,8 s (`RENDERER=webgpu`). |
| Mémoire (processus de rendu de Chromium) | WebGL 2 : 1 365 Mio l'OS seul, 1 980 avec une fenêtre (+615), 2 070 avec deux (+90). WebGPU : 1 372, 2 053 (+681), 2 198 (+145). |
| Deux fenêtres | Deux VectorCraft tournent ensemble, chacun dans son iframe (parcours de bout en bout). |
| Vitesse de dessin | SVG de 20 000 courbes remplies et tracées, WebGL 2, deux tours, build « taille » contre le `vectorcraft-web-0.24.1.zip` officiel : démarrage 0,6–0,7 s contre 0,7–0,8 s ; ouverture 1,8–2,3 s contre 1,8–1,9 s ; zoom avant ou arrière avec l'image peinte 730–820 ms contre 620–770 ms ; tout sélectionner 1,7 s contre 2,2–2,3 s ; déplacer les 20 000 objets 11,8–12,3 s contre 10,9–14,3 s. L'écart reste dans le bruit du rendu logiciel, sauf le zoom, environ 10 % plus lent : pas une raison de changer de réglage. À refaire sur une vraie carte graphique. |
| Polices servies | VectorCraft va chercher `fonts/<hash>/…` à côté de sa page : la page de l'iframe porte `<base href="../vectorcraft/">` pour qu'elles viennent du dossier du build (les autres adresses de la page ne changent pas). |

Sans F1, la fenêtre reste sur son écran de chargement (VectorCraft ne dit jamais `ready`) et
montre l'écran d'erreur après le délai du module ; VectorCraft tourne quand même dans l'iframe
cachée.
- [ ] Chantier 3 : fenêtre, ouverture et enregistrement liés, onglets, exports, garde de fermeture.
- [ ] Chantier 4 : fiche du Store, README, avis de tiers, parcours dans l'OS publié.
- [ ] Sortie : `vector/base` dans `main` (Benoit), étiquette `v0.1.0`, catalogue, `features.md`.
