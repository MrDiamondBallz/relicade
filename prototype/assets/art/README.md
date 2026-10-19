# Original Relicade project editions

These 36 local WebP files are **original Relicade editorial presentation**, not
artwork from the games and not gameplay. They replace the earlier private-study
third-party art cache. No characters, third-party logos, screenshots, stock art,
or upstream image pixels are used.

## Composition and regeneration

- Renderer: [`prototype/project_art.py`](https://github.com/MrDiamondBallz/relicade/blob/main/prototype/project_art.py).
- Header: 1200 × 650; factual project/game names in Barlow/Manrope and original
  geometric cut-R compositions. Three coordinated graphite/ivory/charcoal tones
  with molten-orange accents; typography is safe for the mobile card crop and
  its save-button overlay.
- Capsule: 800 × 1100; standalone typographic project poster in the same family.
- Hero: 1800 × 750; crop-safe original cut-R geometry. The product UI supplies the
  accessible, factual game and project titles. Embedded duplicate headlines were
  rejected in visual QA because responsive cover-cropping would clip them.
- Every header/capsule identifies itself as `COMMUNITY PROJECT`, `ORIGINAL
  RELICADE ART`, and `NOT GAMEPLAY`. The original cut-R is from
  `brand/symbol.svg`, not a game logo.

From the repository root, run `python prototype/project_art.py` to regenerate
from the existing snapshot **without fetching anything** or changing its
metadata acquisition timestamp. `python prototype/fetch_catalog.py` intentionally
refreshes metadata from its approved provider, but still generates only original
local art. Neither command downloads upstream artwork. There is no image-download
flag or game-art publishing path.

Lossless WebP bytes are deterministic for the bundled font files and the same
Pillow/libwebp renderer environment. Byte-identical local files are reused.
Cross-version encoder byte identity is not promised. `data/catalog.json` records
actual output hashes/dimensions, renderer source hash/version/Pillow version, and
font paths/hashes/license paths for each output. Keep provenance in sync by
regenerating whenever the renderer changes.

## Rights and attribution boundary

Barlow and Manrope remain subject to their bundled SIL Open Font Licenses:
`brand/assets/fonts/Barlow-OFL.txt` and `brand/assets/fonts/Manrope-OFL.txt`.
No font files or licenses were removed or replaced.

Factual names identify independent games and community projects; this art does
not reproduce their logo lettering or imply affiliation/endorsement. Original
typography/geometry does not establish trademark clearance or license the named
games, software, or external art. No ROMs, game assets, or executables are present
in these tiles.

Upstream game-art URLs, including SteamGridDB references, are retained only in
`referenceArtSources` as external reference metadata. Their rights remain with
the respective creators/owners; no reuse or redistribution permission is claimed.
Historical source attribution remains verbatim in `sourceAttribution`. Reference
records do not contain the retired cached files' local paths or hashes. Only
`localArt` and `artProvenance` describe the replacement local files.
