# Relicade — brand foundation

**Old legends. New life.**

Status: **design prototype**, not a released launcher. Original identity direction; no trademark clearance or uniqueness search has been performed. This guide does not claim ownership of games or community projects.

## The direction

A graphite stage, an ivory voice, one molten-orange signal. The silhouette and the games carry the identity—not effects, nostalgia props or technical decoration.

**Visual spine:** an oversized custom wordmark establishes the name; the controller R carries recognition into compact product surfaces; orange identifies the next deliberate action.

Relicade's vision is a coherent home for community-revived classics, retro games and emulation, including native ports, recompilations and recreations. Emulation is future scope, not a current capability. Speak to players without hiding the people doing the work. Do not imply every project is official, complete, legally cleared, installed or playable.

## Source assets

| File | Use |
| --- | --- |
| `symbol.svg` | Reusable standalone controller R. `viewBox="0 0 128 128"`. Orange by default. |
| `wordmark.svg` | Original uppercase RELICADE, eight named editable path letters. `viewBox="0 0 718 104"`. Orange controller R with ivory ELICADE suffix. No font dependency. |
| `wordmark-tail.svg` | Native ELICADE suffix: seven unchanged paths from `wordmark.svg`, `viewBox="112 0 606 104"`. Only use after the orange controller-R initial; never as the standalone product name. |
| `app-icon.svg` | Orange mark on graphite field, rounded-square master. `viewBox="0 0 256 256"`. |
| `lockup-dark.svg` | Orange symbol / ivory path wordmark, transparent, for graphite surfaces. |
| `lockup-light.svg` | Graphite symbol and path wordmark, transparent, for ivory surfaces. |
| `lockup-monochrome.svg` | Ivory symbol and path wordmark, transparent, one-color reproduction. |
| `symbol-ivory.svg`, `symbol-graphite.svg`, `wordmark-graphite.svg` | Ready-to-use monochrome source variants. |
| `assets/fonts/` | Locally bundled Barlow Condensed and Manrope font masters and OFL notices. |
| `tokens.css` | Shared CSS primitives, focus and reduced-motion rules. Safe to import into the browser prototype. |
| `index.html` | Responsive brand board; links to `../prototype/`. No remote dependencies. |

The standalone symbol is directly consumable by the parent prototype as `../brand/symbol.svg`. SVGs have embedded accessible titles and descriptions. When an adjacent visible label already names Relicade, use an empty `alt=""` on the image to avoid duplicate announcements.

### Geometry

Active native identity rule: the established **orange #FF663D controller R is the first
letter**, followed by the complete ivory ELICADE suffix. Preserve the larger R
emphasis, outer silhouette and optical spacing; never place a second ivory
R after it, retype the lettering or reduce the name to an icon-only header.
Export `wordmark-tail.svg` as `native/Assets/wordmark-tail.png` for this binding;
pair it with transparent `symbol.svg`, exported as `native/Assets/sidebar-symbol.png`.
Align the initial and suffix optically, with the tagline beneath the suffix;
the boxed app icon is not part of the interface wordmark.
The accessible product name remains **Relicade**. Pure app/window/tray/package
icon surfaces use the established orange R on its graphite field, without a
suffix. This active-native rule supersedes the general monochrome/light-surface
permissions below for those surfaces. The controller details apply to standalone symbols, full-name
masters, repository lockups, the brand board and native/Android exports. Historical
application captures remain dated evidence; never repaint them as a new build.

The controller R retains the original outer silhouette and **open right counter**,
with one directional cross and two diagonal face-button circles cut through the
letter using even-odd fill. This master supersedes the former
uncut single-polygon rule. Keep the supplied controller cutouts, clipped shoulder
and forward diagonal leg; monochrome variants change the fill, not the geometry.

Symbol live bounds: x=16–112, y=12–116 in the 128-unit canvas. Keep the supplied viewBox padding. Add external clear space of at least **16 units per 128-unit canvas**. Use extra clear space in hero compositions. Do not stretch, rotate, outline, bevel, glow, add shadows to the mark, close the counter or detach the diagonal leg.

Wordmark cap height: 104 units. External clear space: at least **26 units** on every side. Forward-cut E/L/C terminals repeat the mark's direction without making every glyph a stencil. Preserve optical spacing and letter transforms; do not retype the name as a substitute for the asset. The A and D have editable even-odd counters.

- Standalone symbol: **24 CSS px minimum** for routine interface use. 16 px is a constrained favicon case, not the standard UI size; inspect the raster before shipping.
- Wordmark: **144 CSS px recommended width**. Compact navigation may use 94–128 px after rendered inspection; all other placements retain the standard clear space. Do not squeeze beyond that; use the symbol plus an accessible product name instead.
- App icon: 32 CSS px minimum; use 48–64 px for prominent application placements.
- Allowed one-color treatments: orange on graphite, ivory on graphite, graphite on ivory, graphite on orange. Do not use ivory on orange.
- On light surfaces recolor the vector fill to graphite; do not add an arbitrary enclosing badge. The app icon retains its graphite field.
- The app icon's 48/256 corner radius belongs to the icon master, not all product controls. A native Windows export must be checked against platform icon/mask requirements; this SVG is not a signed native package or multi-resolution ICO.

## Color and semantic tokens

Import `tokens.css` or map these values into the parent prototype. Do not substitute almost-matching oranges.

| CSS variable | Value | Role |
| --- | --- | --- |
| `--graphite`, `--bg` | `#111211` | Main background |
| `--ivory`, `--text` | `#F4F0E6` | Primary text and light brand field |
| `--orange`, `--accent` | `#FF663D` | Identity and primary action |
| `--on-accent` | `#111211` | Text on orange |
| `--surface` | `#1B1D1B` | Raised product surface |
| `--surface-raised` | `#242724` | Nested/active surface |
| `--muted` | `#ABA99F` | Secondary readable text |
| `--border` | `#393C37` | Decorative separators only |
| `--focus` | `#F4F0E6` | High-contrast focus outline on dark surfaces |

Measured WCAG contrast, rounded to two decimals:

- Ivory / graphite: **16.50:1**.
- Orange / graphite (either direction): **6.46:1**.
- Muted / graphite: **7.96:1**.
- Ivory / orange: **2.56:1 — not suitable for text**.

Orange is punctuation, not a full-page default. Brand specimens may deliberately invert this. No purple, RGB gradients, glass, chromatic glow or color-coded states without labels. Success and failure must have explicit words; do not introduce extra brand colors just to decorate status.

Decorative dividers are intentionally quiet. Do **not** rely on `--border` alone to identify a required input or selected state; pair a stronger boundary or fill with a text/icon cue. Verify required control boundaries against adjacent colors at 3:1. Disabled elements can be quiet, but explain why unavailable actions are disabled.

## Typography

```css
--font-body: Manrope, Arial, Helvetica, sans-serif;
--font-display: Barlow, 'Arial Narrow', Arial, sans-serif;
--font-mono: 'Courier New', monospace;
```

The wordmark is path lettering, not a font recommendation. Do not use its shapes to render arbitrary interface text.

| Role | Desktop size / leading | Weight | Tracking |
| --- | --- | --- | --- |
| Brand hero | `clamp(52px, 6.6vw, 96px)` / `.98` | 700 | `-.055em` |
| Product section heading | 28–40 px / 1.1 | 700 | `-.025em` |
| Body | 16 px / 1.5 | 400 | normal |
| Compact UI | 14 px / 1.45 | 400 or 700 | normal |
| Small supporting copy | 12–13 px / 1.5 | 400 | normal |
| Eyebrow, brand board only | 11 px / 1.5 | 700 | `.13em` |

Use sentence case in the product. Reserve uppercase for short editorial headings and small labels; do not set long descriptions in tracked capitals. The board's tiny specimen labels are annotations, not a default for essential product controls.

Barlow Condensed 700 supplies the editorial display voice. Manrope 400/600/800 supplies body and interface type. Both are bundled locally in `brand/assets/fonts/`, with their SIL Open Font License texts. `tokens.css` loads them without network requests. Sources: Google Fonts distribution (`fonts.gstatic.com`); licenses from the corresponding `google/fonts` upstream directories. Arial remains a resilient fallback. Preserve natural reflow; never fake condensed text with CSS transforms. Game artwork may supply its own lettering, but does not change the Relicade type system.

## Layout, shape and interaction

- Spacing unit: 4 CSS px. Preferred scale: **4 / 8 / 12 / 16 / 24 / 32 / 48 / 64**.
- Controls: minimum 44 CSS px interactive height; 48 px for primary actions. Do not confuse the visible 24 px symbol with its minimum 44 px hit target.
- Radius: 4 px for compact controls, 8 px for larger product surfaces. Crisp structure, not capsules everywhere.
- Borders: 1 CSS px. Avoid hairlines thinner than one CSS px.
- Maintain negative space around the identity. Avoid repeated outlined cards when a heading, alignment or divider will do.
- Reading order must match DOM order. Real links navigate; buttons change state. All keyboard targets must have visible focus.

### Focus

Shared rule: **2 px ivory outline, 4 px offset, 4 px graphite separation ring**. The dark separation ring keeps the focus visible on an orange action. On a light product surface, invert the pair: graphite outline with ivory separation. Never remove focus to tidy a screenshot. Ensure scroll containers do not clip rings.

Hover alone never reveals required content. Disabled state, selection, failure and pending work must be stated with words, not color alone. Respect browser zoom; no viewport lock, no `user-scalable=no`.

### Motion

`--motion-fast: 160ms; --ease-out: cubic-bezier(.2, .8, .2, 1);`

Use 160 ms for short color, opacity and nonessential surface transitions. No logo loops, marquee, parallax, pulsing status decorations or delayed entrances. Motion must never imply an unacknowledged install, save or launch.

**Reduced motion means no transition or animation**, not a slower 160 ms animation. `prefers-reduced-motion: reduce` disables both and restores instant scrolling in `tokens.css`. The board contains no animated entrance or looping behavior.

## Windows scaling / DPI

Author layout in **CSS pixels**, not assumed physical pixels. Keep SVG for logo rendering. Do not stretch bitmap icons or CSS-transform an entire app to compensate for OS scaling.

| Windows scale | Nominal DPR | 24 CSS px symbol | 44 CSS px control | 16 CSS px text |
| --- | --- | --- | --- | --- |
| 100% | 1 | 24 device px | 44 device px | 16 device px |
| 125% | 1.25 | 30 device px | 55 device px | 20 device px |
| 150% | 1.5 | 36 device px | 66 device px | 24 device px |
| 200% | 2 | 48 device px | 88 device px | 32 device px |

At a fixed 1920-pixel physical width, nominal CSS viewport widths are 1920, 1536, 1280 and 960 respectively before browser chrome. Browser zoom can multiply the effective scale again; these are test fixtures, not a reliable way to detect the OS setting.

At every scale check: navigation wrapping, focus rings, complete text, counter openness, sharp icon edges, dialog fit, horizontal overflow and keyboard reachability. Never maintain density by shrinking type. Narrower layouts should stack or progressively disclose secondary information. Raster/native ICO exports should include properly rendered sizes instead of upscaling a single source; actual Windows shell/icon and mixed-monitor validation remain future native work.

## Voice

**Confident, specific, respectful.** Speak like a good launcher, not an emulator forum argument or a luxury advertisement. Product facts before adjectives.

- Approved tagline: **Old legends. New life.** Preserve words and punctuation.
- Relicade stands on its own. Do not use reference-launcher names as product labels, marketing claims or comparative taglines. Public product screenshots must reproduce the actual current interface: an accurate upstream address visible in the running product is not an affiliation claim and must not be renamed or painted out to make a cleaner marketing image. Preserve technical provenance and all required third-party notices separately.
- Credit community projects and creators where players make decisions.
- Use “Add to library”, “View project”, “Open project site”, “Remove from library”.
- Say “Downloads are not available in this prototype.” Do not say “Ready to play” without a verified playable install.
- Say “This project requires your own game files.” only where the project's actual requirements establish it.
- Say “Could not save your library in this browser. Try again.” when storage fails. Do not show a success toast first.
- No fake player counts, install totals, playtimes, progress bars, reviews or performance claims.
- No “revolutionary”, “ultimate”, “seamless”, “next-gen”, “unlock the future”, fake urgency, emojis or claims of universal compatibility.

## Brand-board behavior and boundary

The board has a single reading path: name → promise → mark → palette → type → product application. The only primary action is opening `../prototype/`. Source downloads and the guide are secondary footer links. A persistent label identifies the page as a design prototype. There is no account, install, executable launch, payment, backend state or simulated progress.

Visible states are ordinary link idle/hover/focus and responsive reflow. Asset-load failure should leave meaningful alternative text. The linked product preview is verified separately; this presentation does not certify native application behaviour.

## Verification boundary

Static XML, local references, path-only wordmark and browser rendering are checked separately from native product claims. Browser/device-scale emulation is useful evidence for layout and rasterization, **not** a claim of having tested actual Windows hardware, accessibility APIs, a packaged launcher or trademark availability.
