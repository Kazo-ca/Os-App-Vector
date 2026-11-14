# Third-party notices

> [Version française](THIRD-PARTY-NOTICES-fr.md)

Kazo Vector is under the MIT license ([LICENSE](LICENSE)). Its C# code, its iframe page
(`src/Kazo.Vector/wwwroot/host/index.html`) and its icon (`src/Kazo.Vector/wwwroot/icons/app.svg`,
`store/icon.png`) are this repository's own work. The iframe proxy and page style it uses
(`craft-proxy.js`, `craft-page.css`) belong to Kazo OS (`Kazo.Os.AppSdk.Craft`, MIT).

It bundles one third-party work, **VectorCraft**, which brings its own third-party material and the
fonts it embeds or serves.

## VectorCraft

| | |
| --- | --- |
| Work | [VectorCraft](https://github.com/storytold/vectorcraft), a vector illustration app written in Rust, by the ArtCraft team and the VectorCraft contributors |
| Version | 0.24.1, compiled from our fork [`Kazo-ca/vectorcraft`](https://github.com/Kazo-ca/vectorcraft), branch `kazo/base`, commit `326de0f44b3bf6cda2198b2fd6321cc0b2c7550b` |
| Changes | **Modified.** The fork adds a host mode to the web build (open and save through the host page, `id`, `path` and `active` in `ui.inspect`, ArtCraft links and data recovery off in host mode), on top of the upstream tag `v0.24.1` (commit `f4922aa7ac99a4f6d56fc68a83a44945207ec610`). Each change is a commit of the fork, under VectorCraft's own license |
| Fonts | embedded or served at build time from [`storytold/craft-fonts`](https://github.com/storytold/craft-fonts) at commit `b076417b5880bd4154bd22f00695b1d28c6bea83`, as VectorCraft's own releases do |
| License | MIT or Apache-2.0, at the user's option |
| Files | `src/Kazo.Vector/wwwroot/vectorcraft/` (the web build: `.js`, `.wasm`, `snippets/`, `fonts/`) |
| Provenance | `src/Kazo.Vector/wwwroot/vectorcraft/vectorcraft.json`: the fork's commit, the craft-fonts commit, the compilers, the build profile and the SHA-256 of every file |

VectorCraft's own notices ship **unmodified** next to its build, and in the installed package
(`_content/Kazo.Vector/vectorcraft/`):

| File | Content |
| --- | --- |
| `LICENSE-MIT`, `LICENSE-APACHE` | VectorCraft's licenses |
| `NOTICE` | VectorCraft's copyright, its trademark notice, and the third-party material it bundles, with the notices those licenses require (Lucide icons, the fonts, egui's fonts and icons, Adobe's CMap resources, the PDF backend, sctk-adwaita, the Indic hyphenation patterns…) |
| `ASSETS.md`, `ATTRIBUTION.md` | VectorCraft's per-file list of assets (author, source, license, hash) |
| `licenses/pdf-backend/` | The licenses of the PDF backend (hayro) and of the assets it carries (Foxit substitutes, CGATS ICC profile, Adobe CMaps) |
| `OFL-*.txt`, `fonts/*/OFL.txt` | The SIL Open Font License 1.1 of every face the build embeds (Inter, JetBrains Mono, Source Sans 3, Source Serif 4, BIZ UDPGothic) or serves (Noto Sans Arabic, Georgian and Thai) |

### Files the NOTICE refers to

VectorCraft's `NOTICE` names license files its web build does not carry. Kazo Vector ships them,
unmodified, in `src/Kazo.Vector/wwwroot/legal/vectorcraft/`:

| File | Source | Covers |
| --- | --- | --- |
| `LICENSE-lucide.txt` | VectorCraft `assets/icons/LICENSE-lucide.txt`, at the bundled commit | The Lucide icons of the interface (ISC; those derived from Feather also MIT) |
| `LICENSE-ubuntu-font.txt` | crate `epaint_default_fonts` 0.37.0, `fonts/UFL.txt` | Ubuntu Light, egui's default face (Ubuntu Font Licence 1.0) |
| `LICENSE-hack.txt` | crate `epaint_default_fonts` 0.37.0, `fonts/Hack-Regular.txt` | Hack Regular (MIT, Bitstream Vera) |
| `OFL-noto-emoji.txt` | crate `epaint_default_fonts` 0.37.0, `fonts/OFL.txt` | Noto Emoji (SIL Open Font License 1.1) |
| `LICENSE-emoji-icon-font.txt` | crate `epaint_default_fonts` 0.37.0, `fonts/emoji-icon-font-mit-license.txt` | emoji-icon-font and its egui-icons subset (MIT) |
| `LICENSE-indic-hyphenation-MIT.txt` | the header of VectorCraft's `crates/text/patterns/hyph-*.tex`, at the bundled commit | The Indic hyphenation patterns compiled into the app (MIT option) |

The Rust crates compiled into the web build are under permissive licenses (mostly MIT and/or
Apache-2.0; also BSD, ISC, Zlib, BSL-1.0, Unicode-3.0 and IJG), as VectorCraft's `NOTICE` says;
`self_cell` is used under its Apache-2.0 option.

### The ArtCraft brand

The ArtCraft name, wordmark and logos are trademarks of the ArtCraft team; VectorCraft's open-source
license does not cover them. Kazo Vector uses none of them: its name, its icon and its Store listing
are this repository's own, and in Kazo OS VectorCraft runs in host mode, which hides the ArtCraft
links. "Based on VectorCraft by the ArtCraft team" only says where the software comes from.

## In the package

The Kazo OS package (`.kazoapp`) carries, under `wwwroot/`:

- `legal/LICENSE.txt`: Kazo Vector's MIT license (this repository's `LICENSE`);
- `legal/THIRD-PARTY-NOTICES.md` and `legal/THIRD-PARTY-NOTICES-fr.md`: this file and its French
  version;
- `legal/vectorcraft/`: the files VectorCraft's `NOTICE` refers to, as above;
- `vectorcraft/LICENSE-MIT`, `LICENSE-APACHE`, `NOTICE`, `ASSETS.md`, `ATTRIBUTION.md`,
  `licenses/` and the `OFL` files: VectorCraft's notices, as above.

A C# test (`LegalFilesTests`) checks that the project puts these files in the package.

## Test data

The end-to-end tests draw with VectorCraft's own commands; they ship no third-party file. The
installed run (`tests/installed/install-and-open.mjs`) opens VectorCraft's own example
`examples/dusk-poster.svg` (VectorCraft contributors, MIT OR Apache-2.0, listed in its `ASSETS.md`)
from the `external/vectorcraft` submodule, without copying it into this repository; the Store
screenshots show it.
