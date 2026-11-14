#!/usr/bin/env bash
# Compiles VectorCraft from the external/vectorcraft submodule (our fork Kazo-ca/vectorcraft;
# decision 5 and work item 2 of doc/en/plan.md) with Trunk, then vendors the result in
# src/Kazo.Vector/wwwroot/vectorcraft/ with tools/vendor-vectorcraft.sh --dir. The
# .github/workflows/build-vectorcraft.yml workflow runs it; it also runs on a developer machine.
#
# Usage:
#   tools/build-vectorcraft.sh
#
# Needs: Rust stable (1.95 or later) with the wasm32-unknown-unknown target, Trunk at the version
# below, git and network access (craft-fonts). Like VectorCraft's own releases, the build embeds the
# web face of storytold/craft-fonts (BIZ UDPGothic, for the Japanese interface) and serves the
# craft-fonts faces of crates/text/web-fonts.txt beside the .wasm, at the revision VectorCraft's
# release workflow pins. CRAFT_FONTS_DIR, when set, names a craft-fonts checkout to use instead of
# cloning one; it must be on that commit.
#
# Size profile (question Q1 = A of doc/en/plan.md): VectorCraft's release profile (opt-level 3,
# thin LTO, one codegen unit) gives a 102 MB .wasm, 2.4 % under GitHub's 100 MiB limit per file.
# CARGO_PROFILE_RELEASE_OPT_LEVEL=s builds for size instead (about 72 MB) without changing the fork.
# Debug info is stripped, not the whole name section: with strip = true the target_features section
# goes too and Trunk's wasm-opt refuses the file. Fat LTO runs out of memory: not used. The script
# fails when the .wasm is above 90 MiB, well before GitHub would refuse the push.
set -euo pipefail

# The Trunk version of VectorCraft's release workflow (craft-common-actions, install-trunk).
trunk_version="0.21.14"
# Largest .wasm the repository takes (GitHub refuses files above 100 MiB).
max_wasm_bytes=$((90 * 1024 * 1024))

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
vectorcraft="$repo_root/external/vectorcraft"

case "${1:-}" in
  "") ;;
  -h|--help) sed -n '2,22p' "$0"; exit 0 ;;
  *) echo "Unknown argument: $1" >&2; exit 2 ;;
esac

fail() { echo "error: $*" >&2; exit 1; }

[[ -f "$vectorcraft/Cargo.toml" ]] || fail "external/vectorcraft is empty. Run: git submodule update --init external/vectorcraft"
# A local change of VectorCraft must go through a commit of the fork, so the provenance names it.
[[ -z "$(git -C "$vectorcraft" status --porcelain)" ]] || fail "external/vectorcraft has local changes; commit them in the fork first"

command -v trunk >/dev/null || fail "trunk not found; install Trunk $trunk_version"
actual_trunk="$(trunk --version | awk '{print $2}')"
[[ "$actual_trunk" == "$trunk_version" ]] || fail "trunk $actual_trunk found; VectorCraft's release uses $trunk_version"
rustup target list --installed 2>/dev/null | grep -qx wasm32-unknown-unknown \
  || fail "the wasm32-unknown-unknown target is missing (rustup target add wasm32-unknown-unknown)"

commit="$(git -C "$vectorcraft" rev-parse HEAD)"
rustc_version="$(rustc --version)"
# The craft-fonts revision of VectorCraft's release workflow (`craft-fonts-ref:`).
fonts_rev="$(awk '$1 == "craft-fonts-ref:" { print $2; exit }' "$vectorcraft/.github/workflows/release.yml")"
[[ "$fonts_rev" =~ ^[0-9a-f]{40}$ ]] || fail "no craft-fonts-ref in external/vectorcraft/.github/workflows/release.yml"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

fonts="${CRAFT_FONTS_DIR:-}"
if [[ -z "$fonts" ]]; then
  fonts="$work/craft-fonts"
  git init -q "$fonts"
  git -C "$fonts" fetch -q --depth 1 https://github.com/storytold/craft-fonts.git "$fonts_rev"
  git -C "$fonts" checkout -q FETCH_HEAD
fi
fonts="$(cd "$fonts" && pwd)"
[[ "$(git -C "$fonts" rev-parse HEAD)" == "$fonts_rev" ]] || fail "$fonts is not on craft-fonts $fonts_rev"

# trunk build --release in apps/vectorcraft-web (wasm-opt -Oz, set by its index.html; the post_build
# hook copies the served craft-fonts faces into fonts/), into a temporary folder so the submodule
# stays clean.
profile="opt-level=s, lto=thin, codegen-units=1, strip=debuginfo"
echo "Building VectorCraft $commit (rustc: $rustc_version, trunk $actual_trunk, craft-fonts $fonts_rev, $profile)"
(
  export CRAFT_FONTS_DIR="$fonts" CRAFT_FONTS_REQUIRED=1
  export CARGO_PROFILE_RELEASE_OPT_LEVEL=s CARGO_PROFILE_RELEASE_LTO=thin CARGO_PROFILE_RELEASE_CODEGEN_UNITS=1
  export CARGO_PROFILE_RELEASE_STRIP=debuginfo
  export CARGO_TARGET_DIR="${CARGO_TARGET_DIR:-$vectorcraft/target}"
  cd "$vectorcraft/apps/vectorcraft-web"
  trunk build --release --dist "$work/site"
)

shopt -s nullglob
wasms=("$work/site"/vectorcraft-web-*_bg.wasm)
shopt -u nullglob
[[ ${#wasms[@]} -eq 1 ]] || fail "Trunk produced ${#wasms[@]} .wasm files"
wasm_bytes="$(stat -c %s "${wasms[0]}")"
[[ "$wasm_bytes" -le "$max_wasm_bytes" ]] \
  || fail "the .wasm is $wasm_bytes bytes, above the $max_wasm_bytes the repository takes (90 MiB)"

"$repo_root/tools/vendor-vectorcraft.sh" --dir "$work/site" --craft-fonts-dir "$fonts" \
  --craft-fonts "$fonts_rev" --rustc "$rustc_version" --trunk "$actual_trunk" --profile "$profile"
