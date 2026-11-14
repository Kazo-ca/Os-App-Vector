#!/usr/bin/env bash
# Puts a VectorCraft web build in src/Kazo.Vector/wwwroot/vectorcraft/ (decision 5 of
# doc/en/plan.md; the same method as Kazo Photo's vendor-photocraft.sh). That folder is never edited
# by hand: this script replaces it whole and writes vectorcraft.json, its provenance (version,
# source, name, size and SHA-256 of each file). tests/Kazo.Vector.Tests checks the files still match.
#
# Usage:
#   tools/vendor-vectorcraft.sh --dir <folder> --craft-fonts-dir <checkout> [--craft-fonts <sha>]
#                               [--rustc <text>] [--trunk <version>] [--profile <text>]
#
# <folder> is Trunk's output for apps/vectorcraft-web of the external/vectorcraft submodule (one
# vectorcraft-web-<hash>.js, one vectorcraft-web-<hash>_bg.wasm, snippets/), as
# tools/build-vectorcraft.sh makes it. <checkout> is the craft-fonts checkout the build embedded,
# for the licences of its two web faces. There is no "official zip" mode: Kazo Vector is always
# built from our fork (decision 5).
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
target="$repo_root/src/Kazo.Vector/wwwroot/vectorcraft"
submodule="$repo_root/external/vectorcraft"

fail() { echo "$*" >&2; exit 1; }

build_dir=""
fonts_dir=""
fonts_rev=""
rustc_version=""
trunk_version=""
profile=""
while [[ $# -gt 0 ]]; do
  [[ $# -ge 2 || "$1" == -h || "$1" == --help ]] || fail "$1 needs a value"
  case "$1" in
    --dir) build_dir="$2"; shift 2 ;;
    --craft-fonts-dir) fonts_dir="$2"; shift 2 ;;
    --craft-fonts) fonts_rev="$2"; shift 2 ;;
    --rustc) rustc_version="$2"; shift 2 ;;
    --trunk) trunk_version="$2"; shift 2 ;;
    --profile) profile="$2"; shift 2 ;;
    -h|--help) sed -n '2,15p' "$0"; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done
[[ -n "$build_dir" ]] || fail "--dir <folder> is required (Trunk's output; tools/build-vectorcraft.sh makes it)"
[[ -d "$build_dir" ]] || fail "$build_dir is not a folder"
[[ -n "$fonts_dir" && -f "$fonts_dir/fonts/manifest.txt" ]] || fail "--craft-fonts-dir <checkout> is required (the craft-fonts the build embedded)"
source_dir="$(cd "$build_dir" && pwd)"

# The provenance names the fork and the submodule's commit, which must be clean so the commit alone
# says what was compiled.
[[ -f "$submodule/Cargo.toml" ]] || fail "external/vectorcraft is empty. Run: git submodule update --init external/vectorcraft"
[[ -z "$(git -C "$submodule" status --porcelain)" ]] || fail "external/vectorcraft has local changes; the provenance could not name what was built"
commit="$(git -C "$submodule" rev-parse HEAD)"
repository="$(git -C "$repo_root" config -f .gitmodules submodule.external/vectorcraft.url)"
version="$(awk '/^\[/ { in_pkg = ($0 == "[workspace.package]"); next }
                in_pkg && $1 == "version" { gsub(/[" ]/, "", $3); print $3; exit }' "$submodule/Cargo.toml")"
[[ -n "$version" ]] || fail "no [workspace.package] version in external/vectorcraft/Cargo.toml"

# Escapes a value for a JSON string (the rustc version is free text).
json_string() {
  local value="$1"
  value="${value//\\/\\\\}"
  value="${value//\"/\\\"}"
  printf '"%s"' "$value"
}

# Exactly one glue module and one .wasm, named by Trunk with the same hash.
shopt -s nullglob
glues=("$source_dir"/vectorcraft-web-*.js)
wasms=("$source_dir"/vectorcraft-web-*_bg.wasm)
shopt -u nullglob
if [[ ${#glues[@]} -ne 1 || ${#wasms[@]} -ne 1 ]]; then
  fail "Expected one vectorcraft-web-*.js and one vectorcraft-web-*_bg.wasm in $source_dir"
fi
glue="$(basename "${glues[0]}")"
wasm="$(basename "${wasms[0]}")"
[[ "${glue%.js}" == "${wasm%_bg.wasm}" ]] || fail "$glue and $wasm do not come from the same build"

# What the app needs: the glue, the .wasm, the snippets the glue imports, the served fonts, then
# every licence and notice of the release: VectorCraft's own (MIT OR Apache-2.0, NOTICE), its asset
# lists (ASSETS.md, ATTRIBUTION.md), the OFL licences of the fonts its .wasm embeds (assets/fonts),
# the licences of its PDF backend (licenses/) and the OFL licence of each craft-fonts family of the
# build. Left out: Trunk's index.html (host/index.html replaces it) and icons/ (tab icons the iframe
# never shows).
rm -rf "$target"
mkdir -p "$target"
cp "$source_dir/$glue" "$source_dir/$wasm" "$target/"
[[ ! -d "$source_dir/snippets" ]] || cp -R "$source_dir/snippets" "$target/"
# The craft-fonts faces the build serves beside the .wasm (fonts/<hash>/, from
# crates/text/web-fonts.txt), which VectorCraft fetches and checks by SHA-256.
[[ ! -d "$source_dir/fonts" ]] || cp -R "$source_dir/fonts" "$target/"
for notice in LICENSE-MIT LICENSE-APACHE NOTICE ASSETS.md ATTRIBUTION.md; do
  [[ -f "$submodule/$notice" ]] || fail "external/vectorcraft/$notice is missing"
  cp "$submodule/$notice" "$target/"
done
shopt -s nullglob
font_licences=("$submodule"/assets/fonts/OFL-*.txt)
shopt -u nullglob
[[ ${#font_licences[@]} -gt 0 ]] || fail "no assets/fonts/OFL-*.txt in external/vectorcraft"
cp "${font_licences[@]}" "$target/"
[[ -d "$submodule/licenses" ]] || fail "external/vectorcraft/licenses is missing"
cp -R "$submodule/licenses" "$target/licenses"
# craft-fonts keeps one folder per family (fonts/<family>/OFL.txt): the family of the face the .wasm
# embeds (BIZ UDPGothic), then the family of each served file, found by its file name.
families=(biz-ud-pgothic)
if [[ -d "$target/fonts" ]]; then
  while IFS= read -r served; do
    name="$(basename "$served")"
    found="$(cd "$fonts_dir/fonts" && find . -mindepth 2 -maxdepth 2 -type f -name "$name" | head -n 1)"
    [[ -n "$found" ]] || fail "the served font $name is not in craft-fonts"
    family="$(dirname "${found#./}")"
    [[ " ${families[*]} " == *" $family "* ]] || families+=("$family")
  done < <(find "$target/fonts" -type f \( -name "*.ttf" -o -name "*.otf" \) | LC_ALL=C sort)
fi
for family in "${families[@]}"; do
  [[ -f "$fonts_dir/fonts/$family/OFL.txt" ]] || fail "craft-fonts has no fonts/$family/OFL.txt"
  cp "$fonts_dir/fonts/$family/OFL.txt" "$target/OFL-craft-fonts-$family.txt"
done

# The provenance file, files sorted by path so a new version gives a readable diff.
{
  printf '{\n'
  printf '  "version": "%s",\n' "$version"
  printf '  "source": {\n'
  printf '    "kind": "source-build",\n'
  printf '    "repository": %s,\n' "$(json_string "$repository")"
  printf '    "commit": "%s"' "$commit"
  [[ -z "$fonts_rev" ]] || printf ',\n    "craftFonts": "%s"' "$fonts_rev"
  [[ -z "$rustc_version" ]] || printf ',\n    "rustc": %s' "$(json_string "$rustc_version")"
  [[ -z "$trunk_version" ]] || printf ',\n    "trunk": %s' "$(json_string "$trunk_version")"
  [[ -z "$profile" ]] || printf ',\n    "profile": %s' "$(json_string "$profile")"
  printf '\n  },\n'
  printf '  "glue": "%s",\n' "$glue"
  printf '  "wasm": "%s",\n' "$wasm"
  printf '  "files": [\n'
  first=1
  while IFS= read -r path; do
    size="$(stat -c %s "$target/$path")"
    sha="$(sha256sum "$target/$path" | cut -d' ' -f1)"
    [[ $first -eq 1 ]] || printf ',\n'
    first=0
    printf '    { "path": "%s", "size": %s, "sha256": "%s" }' "$path" "$size" "$sha"
  done < <(cd "$target" && find . -type f ! -path ./vectorcraft.json | sed 's#^\./##' | LC_ALL=C sort)
  printf '\n  ]\n'
  printf '}\n'
} > "$target/vectorcraft.json"

echo "VectorCraft $version vendored in ${target#"$repo_root"/}: $glue, $wasm"
