"""Original Relicade editorial project tiles, rendered locally without network input.

Factual project/game names are typeset, not reproduced as logos. No game imagery,
characters, screenshots, stock art or external image data enter this renderer.
Run directly to regenerate the checked-in snapshot's tiles, not its metadata.
"""
from __future__ import annotations

import copy
import hashlib
import io
import json
from pathlib import Path
import re

from PIL import Image, ImageDraw, ImageFont, __version__ as PILLOW_VERSION

ROOT = Path(__file__).resolve().parent
FONT_ROOT = ROOT.parent / 'brand/assets/fonts'
SOURCE_URL = 'https://github.com/MrDiamondBallz/relicade/blob/main/prototype/project_art.py'
VERSION = '1.0.0'
SIZES = {'hero': (1800, 750), 'header': (1200, 650), 'capsule': (800, 1100)}
GRAPHITE = '#111211'
IVORY = '#F4F0E6'
ORANGE = '#FF663D'
# Exact original cut-R polygon from brand/symbol.svg, not a third-party logo.
CUT_R = [(16, 12), (80, 12), (104, 36), (104, 52), (76, 52), (76, 46),
         (70, 40), (44, 40), (44, 60), (80, 60), (112, 116), (80, 116),
         (44, 80), (44, 116), (16, 116)]
SLUG_PATTERN = re.compile(r'^[a-z0-9]+(?:-[a-z0-9]+)*$')
FONTS = ('barlow-700.ttf', 'manrope-600.ttf')
REFERENCE_USAGE = 'External reference metadata only; not downloaded or redistributed.'
RIGHTS = ('Relicade original project-tile art: original typography and geometric composition; '
          'not game imagery or gameplay. Bundled Barlow and Manrope fonts retain their SIL OFL licenses. '
          'Factual game/project names identify their respective owners; no affiliation, endorsement '
          'or trademark clearance is implied. External reference art rights remain with its owners.')


def font(size: int, display: bool = False):
    return ImageFont.truetype(str(FONT_ROOT / FONTS[0 if display else 1]), max(1, size))


def mark(draw, x: float, y: float, size: float, fill):
    draw.polygon([(x + px * size / 128, y + py * size / 128) for px, py in CUT_R], fill=fill)


def wrapped(draw, text: str, face, width: int):
    """Wrap ordinary words; split an overlong token rather than clip it."""
    lines = []
    line = ''
    for word in text.split():
        if draw.textlength(word, font=face) > width:
            if line:
                lines.append(line)
                line = ''
            part = ''
            for character in word:
                if part and draw.textlength(part + character, font=face) > width:
                    lines.append(part)
                    part = ''
                part += character
            line = part
        elif line and draw.textlength(line + ' ' + word, font=face) > width:
            lines.append(line)
            line = word
        else:
            line = (line + ' ' + word).strip()
    if line:
        lines.append(line)
    return lines or ['UNTITLED PROJECT']


def render_tile(app: dict, kind: str):
    """Return RGB image and measured text bounds for actual visual-QA tests."""
    w, h = SIZES[kind]
    slug = app['slug']
    seed = int(hashlib.sha256(slug.encode()).hexdigest()[:8], 16)
    variant = seed % 3
    # Neutral ivory, near-black and warm charcoal editions share one identity.
    bg, ink, muted, motif = [
        (GRAPHITE, IVORY, '#A6A59C', '#272A26'),
        (IVORY, GRAPHITE, '#64645D', '#DDD9CE'),
        ('#242722', IVORY, '#ABAFA4', '#3B4037'),
    ][variant]
    image = Image.new('RGB', (w, h), bg)
    draw = ImageDraw.Draw(image)
    bounds = []
    title_bounds = []

    def text(x, y, value, size, color=ink, display=False):
        face = font(size, display)
        draw.text((x, y), value, font=face, fill=color, anchor='lt')
        bounds.append(draw.textbbox((x, y), value, font=face, anchor='lt'))

    def title(x, y, width, height, max_size):
        name = str(app.get('projectName') or app.get('name') or 'Community project').upper()
        lines, size, leading = [], 8, 8
        for size in range(max_size, 7, -1):
            face = font(size, True)
            lines = wrapped(draw, name, face, width)
            leading = round(size * 1.03)
            if len(lines) * leading <= height:
                break
        for line in lines:
            text(x, y, line, size, display=True)
            title_bounds.append(bounds[-1])
            y += leading
        return y

    def descriptor(x, y, width, max_height=64, size=22):
        value = str(app.get('name') or app.get('projectName') or 'Community project')
        lines, current, step = [], 8, 8
        for current in range(size, 7, -1):
            face = font(current)
            lines = wrapped(draw, value, face, width)
            step = round(current * 1.4)
            if len(lines) * step <= max_height:
                break
        for line in lines:
            text(x, y, line, current, muted)
            y += step

    if kind == 'hero':
        # Cover-cropped at many UI ratios, this is geometry rather than a second
        # embedded headline: accessible factual game/project type is in the UI.
        # Header and capsule are the standalone typographic project editions.
        draw.polygon([(640, 0), (w, 0), (w, h), (870, h)], fill=motif)
        mark(draw, 1010, 110, 590, bg)
        draw.line([(1540, 110), (1740, 110)], fill=ORANGE, width=3)
        draw.rectangle((0, h - 6, w, h), fill=ORANGE)
    elif kind == 'header':
        # Keep all type inside the mobile card's narrower center crop.
        margin = 145
        # A cropped oversized cut-R gives a recognizable editorial silhouette.
        if variant == 0:
            mark(draw, 692, -18, 620, motif)
            draw.polygon([(0, 0), (20, 0), (20, h), (0, h)], fill=ORANGE)
        elif variant == 1:
            draw.polygon([(885, 0), (w, 0), (w, h), (640, h)], fill=motif)
            mark(draw, 855, 168, 330, ORANGE)
        else:
            draw.line((margin, 151, w - margin, 151), fill=muted, width=1)
            mark(draw, 802, 288, 420, motif)
            draw.rectangle((margin, 0, margin + 128, 10), fill=ORANGE)
        mark(draw, margin - 5, 49, 45, ORANGE)
        text(margin + 53, 62, 'RELICADE / PROJECT EDITIONS', 18)
        text(margin, 256, 'COMMUNITY PROJECT', 18, muted)
        # Mobile save control occupies the top-right 50px of the displayed card.
        # Project title stays below it, not obscured by an interactive overlay.
        end = title(margin - 3, 318, 760, 183, 91)
        descriptor(margin, max(528, end + 12), 760, 47, 18)
        draw.line((margin, h - 78, w - margin, h - 78), fill=motif if variant != 2 else muted, width=1)
        text(margin, h - 49, 'ORIGINAL RELICADE ART / NOT GAMEPLAY', 17, muted)
    else:
        margin = 57
        draw.rectangle((0, 0, w, 12), fill=ORANGE)
        mark(draw, 432, -38, 440, motif)
        mark(draw, margin - 6, 56, 56, ORANGE)
        text(margin + 67, 75, 'RELICADE', 24)
        text(margin, 219, 'PROJECT EDITIONS', 19, muted)
        draw.line((margin, 267, w - margin, 267), fill=ORANGE, width=3)
        end = title(margin - 2, 318, w - margin * 2, 415, 107)
        descriptor(margin, max(757, end + 18), w - margin * 2, 120, 25)
        text(margin, 943, 'COMMUNITY PROJECT', 18, muted)
        text(margin, 991, 'ORIGINAL RELICADE ART', 18, muted)
        text(margin, 1021, 'NOT GAMEPLAY', 18, muted)
    return image, {'variant': variant, 'textBounds': bounds, 'titleBounds': title_bounds}


def apply_original_art(app: dict, *, root: Path = ROOT):
    """Replace render fields, retaining upstream URLs only as external references.

    Byte-identical renders are reused; old third-party bytes at a matching path
    are always replaced. There is deliberately no image-download option.
    """
    slug = app.get('slug')
    if not isinstance(slug, str) or not SLUG_PATTERN.fullmatch(slug):
        raise ValueError('Project tile slug must be lowercase hyphenated alphanumeric')
    root = Path(root)
    references = copy.deepcopy(app.get('referenceArtSources') or {})
    references['usage'] = REFERENCE_USAGE
    for key in ('artwork', 'libraryArt'):
        if key in app:
            references[key] = app.pop(key)
    previous = app.pop('artProvenance', [])
    # Never carry old local-file/hash claims into reference-only metadata.
    # Existing external-source attribution survives; original provenance is replaced.
    old_refs = references.get('provenance', [])
    external = [p for p in previous if p.get('sourceUrl') != SOURCE_URL]
    references['provenance'] = [
        {key: value for key, value in record.items() if key in ('kind', 'sourceUrl', 'rights', 'transformation')}
        for record in (old_refs + external)
    ]
    app['referenceArtSources'] = references
    app['localArt'] = {}
    app['artProvenance'] = []
    renderer = {'path': 'prototype/project_art.py', 'sourceUrl': SOURCE_URL,
                'sha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
                'version': VERSION, 'pillowVersion': PILLOW_VERSION}
    font_sources = [{'path': f'brand/assets/fonts/{name}',
                     'sha256': hashlib.sha256((FONT_ROOT / name).read_bytes()).hexdigest(),
                     'license': f'brand/assets/fonts/{"Barlow" if name.startswith("barlow") else "Manrope"}-OFL.txt'}
                    for name in FONTS]
    for kind in SIZES:
        image, layout = render_tile(app, kind)
        # Lossless WebP keeps original type edges exact and output reproducible.
        output = io.BytesIO()
        image.save(output, 'WEBP', lossless=True, method=6, exact=True)
        raw = output.getvalue()
        relative = f'assets/art/{slug}-{kind}.webp'
        path = root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        if not path.exists() or path.read_bytes() != raw:
            path.write_bytes(raw)
        app['localArt'][kind] = relative
        app['artProvenance'].append({
            'kind': kind, 'sourceUrl': SOURCE_URL, 'localPath': relative,
            'sha256': hashlib.sha256(raw).hexdigest(), 'rights': RIGHTS,
            'transformation': 'Locally rendered original editorial typography and cut-R geometry; lossless WebP. No upstream images used.',
            'renderer': renderer, 'fonts': font_sources,
            'dimensions': list(SIZES[kind]), 'layoutVariant': layout['variant'],
            'presentation': ('Crop-safe original brand geometry; accessible factual game/project titles are supplied by the product UI.'
                             if kind == 'hero' else 'Original editorial typography using factual project/game names, not their logos.'),
        })
    return app


def main():
    catalog = ROOT / 'data/catalog.json'
    snapshot = json.loads(catalog.read_text())
    # Keep the original metadata acquisition time and sourceAttribution verbatim.
    for app in snapshot['apps']:
        apply_original_art(app)
    snapshot['attribution'] = ('Public catalog metadata and project details: respective providers and project maintainers. '
                               'Local project tiles: original Relicade editorial art, not game imagery or gameplay. '
                               'External artwork URLs are reference-only metadata, not downloaded or redistributed. '
                               'Original sourceAttribution is retained verbatim; game/project names and external art rights remain '
                               'with their respective owners. Unaffiliated prototype; no ROMs or executables included.')
    catalog.write_text(json.dumps(snapshot, ensure_ascii=False, indent=2) + '\n')
    print(f'Generated/reused {len(snapshot["apps"]) * len(SIZES)} original project tiles; metadata timestamp unchanged.')


if __name__ == '__main__':
    main()
