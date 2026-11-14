# Avis de tiers

> [English version](THIRD-PARTY-NOTICES.md)

Kazo Vector est sous licence MIT ([LICENSE](LICENSE)). Son code C#, la page de son iframe
(`src/Kazo.Vector/wwwroot/host/index.html`) et son icône (`src/Kazo.Vector/wwwroot/icons/app.svg`,
`store/icon.png`) sont le travail de ce dépôt. Le proxy de l'iframe et le style de la page qu'il
utilise (`craft-proxy.js`, `craft-page.css`) appartiennent à Kazo OS (`Kazo.Os.AppSdk.Craft`, MIT).

Il contient une œuvre de tiers, **VectorCraft**, qui apporte son propre matériel de tiers et les
polices qu'il intègre ou sert.

## VectorCraft

| | |
| --- | --- |
| Œuvre | [VectorCraft](https://github.com/storytold/vectorcraft), une app d'illustration vectorielle écrite en Rust, de l'équipe ArtCraft et des contributeurs de VectorCraft |
| Version | 0.24.1, compilée depuis notre fork [`Kazo-ca/vectorcraft`](https://github.com/Kazo-ca/vectorcraft), branche `kazo/base`, commit `326de0f44b3bf6cda2198b2fd6321cc0b2c7550b` |
| Changements | **Modifiée.** Le fork ajoute un mode hébergé au build web (ouvrir et enregistrer par la page hôte, `id`, `path` et `active` dans `ui.inspect`, liens ArtCraft et récupération de données coupés en mode hébergé), sur l'étiquette amont `v0.24.1` (commit `f4922aa7ac99a4f6d56fc68a83a44945207ec610`). Chaque changement est un commit du fork, sous la licence de VectorCraft |
| Polices | intégrées ou servies à la compilation depuis [`storytold/craft-fonts`](https://github.com/storytold/craft-fonts) au commit `b076417b5880bd4154bd22f00695b1d28c6bea83`, comme les versions publiées de VectorCraft |
| Licence | MIT ou Apache-2.0, au choix de l'utilisateur |
| Fichiers | `src/Kazo.Vector/wwwroot/vectorcraft/` (le build web : `.js`, `.wasm`, `snippets/`, `fonts/`) |
| Provenance | `src/Kazo.Vector/wwwroot/vectorcraft/vectorcraft.json` : le commit du fork, celui de craft-fonts, les compilateurs, le profil de compilation et le SHA-256 de chaque fichier |

Les avis de VectorCraft sont livrés **sans modification** à côté de son build, et dans le paquet
installé (`_content/Kazo.Vector/vectorcraft/`) :

| Fichier | Contenu |
| --- | --- |
| `LICENSE-MIT`, `LICENSE-APACHE` | Les licences de VectorCraft |
| `NOTICE` | Le copyright de VectorCraft, son avis de marque, et le matériel de tiers qu'il contient, avec les avis que ces licences demandent (icônes Lucide, polices, polices et icônes d'egui, ressources CMap d'Adobe, moteur PDF, sctk-adwaita, motifs de césure indiens…) |
| `ASSETS.md`, `ATTRIBUTION.md` | La liste, fichier par fichier, du matériel de VectorCraft (auteur, source, licence, empreinte) |
| `licenses/pdf-backend/` | Les licences du moteur PDF (hayro) et du matériel qu'il contient (substituts Foxit, profil ICC CGATS, CMaps d'Adobe) |
| `OFL-*.txt`, `fonts/*/OFL.txt` | La SIL Open Font License 1.1 de chaque police que le build intègre (Inter, JetBrains Mono, Source Sans 3, Source Serif 4, BIZ UDPGothic) ou sert (Noto Sans Arabic, Georgian et Thai) |

### Fichiers cités par le NOTICE

Le `NOTICE` de VectorCraft nomme des fichiers de licence que son build web ne contient pas. Kazo
Vector les livre, sans modification, dans `src/Kazo.Vector/wwwroot/legal/vectorcraft/` :

| Fichier | Source | Couvre |
| --- | --- | --- |
| `LICENSE-lucide.txt` | VectorCraft `assets/icons/LICENSE-lucide.txt`, au commit livré | Les icônes Lucide de l'interface (ISC ; celles qui viennent de Feather aussi MIT) |
| `LICENSE-ubuntu-font.txt` | crate `epaint_default_fonts` 0.37.0, `fonts/UFL.txt` | Ubuntu Light, la police par défaut d'egui (Ubuntu Font Licence 1.0) |
| `LICENSE-hack.txt` | crate `epaint_default_fonts` 0.37.0, `fonts/Hack-Regular.txt` | Hack Regular (MIT, Bitstream Vera) |
| `OFL-noto-emoji.txt` | crate `epaint_default_fonts` 0.37.0, `fonts/OFL.txt` | Noto Emoji (SIL Open Font License 1.1) |
| `LICENSE-emoji-icon-font.txt` | crate `epaint_default_fonts` 0.37.0, `fonts/emoji-icon-font-mit-license.txt` | emoji-icon-font et son extrait egui-icons (MIT) |
| `LICENSE-indic-hyphenation-MIT.txt` | l'en-tête des fichiers `crates/text/patterns/hyph-*.tex` de VectorCraft, au commit livré | Les motifs de césure indiens compilés dans l'app (option MIT) |

Les crates Rust compilées dans le build web sont sous licences permissives (surtout MIT et/ou
Apache-2.0 ; aussi BSD, ISC, Zlib, BSL-1.0, Unicode-3.0 et IJG), comme le dit le `NOTICE` de
VectorCraft ; `self_cell` est pris sous son option Apache-2.0.

### La marque ArtCraft

Le nom, le logotype et les logos ArtCraft sont des marques de l'équipe ArtCraft ; la licence libre
de VectorCraft ne les couvre pas. Kazo Vector n'en utilise aucun : son nom, son icône et sa fiche du
Store sont ceux de ce dépôt, et dans Kazo OS VectorCraft tourne en mode hébergé, qui masque les
liens ArtCraft. « Basé sur VectorCraft de l'équipe ArtCraft » dit seulement d'où vient le logiciel.

## Dans le paquet

Le paquet Kazo OS (`.kazoapp`) contient, sous `wwwroot/` :

- `legal/LICENSE.txt` : la licence MIT de Kazo Vector (le `LICENSE` de ce dépôt) ;
- `legal/THIRD-PARTY-NOTICES.md` et `legal/THIRD-PARTY-NOTICES-fr.md` : ce fichier et sa version
  anglaise ;
- `legal/vectorcraft/` : les fichiers cités par le `NOTICE` de VectorCraft, comme plus haut ;
- `vectorcraft/LICENSE-MIT`, `LICENSE-APACHE`, `NOTICE`, `ASSETS.md`, `ATTRIBUTION.md`,
  `licenses/` et les fichiers `OFL` : les avis de VectorCraft, comme plus haut.

Un test C# (`LegalFilesTests`) vérifie que le projet met ces fichiers dans le paquet.

## Données de test

Les tests de bout en bout dessinent avec les commandes de VectorCraft ; ils ne livrent aucun fichier
de tiers. Le parcours installé (`tests/installed/install-and-open.mjs`) ouvre l'exemple de
VectorCraft `examples/dusk-poster.svg` (contributeurs de VectorCraft, MIT OR Apache-2.0, listé dans
son `ASSETS.md`) depuis le sous-module `external/vectorcraft`, sans le copier dans ce dépôt ; les
captures du Store le montrent.
