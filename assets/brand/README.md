# Shiftwall brand assets

Original artwork, hand-written SVG. Concept "shifting brick": one brick slides out of the wall —
a configuration moving from one firewall to another. No third-party logos, shapes or colours.

| File | Use |
|---|---|
| `logo.svg` | Master logo (64×64 grid), used for 32 px and larger |
| `logo-16.svg`, `logo-24.svg` | Pixel-aligned versions for 16 and 24 px |
| `icon.ico` | Windows icon: 16, 24, 32, 48, 64, 128, 256 |
| `logo-512.png`, `logo-1024.png` | README / social |
| `banner.svg`, `banner-1280x640.png` | GitHub social preview |
| `variants/` | Discarded design candidates (kept for history) |

## Palette

| Token | Hex | Role |
|---|---|---|
| Tile | `#0A2530` | Background tile |
| Wall | `#1F6474` | Bricks (master) |
| Accent | `#5EEAD4` | Shifting brick, outline (35 % opacity) |
| Trail | `#2DD4BF` | Motion trail (60 % opacity) |

The tile carries its own background and outline, so the logo works on light and dark surfaces.

## Regenerating rasters

```bash
node tools/brand/render-brand.js
```

Requires Node.js and the `playwright` package with Chromium. The banner text is rendered with the
Inter typeface (SIL Open Font License 1.1); the SVG falls back to Segoe UI / Arial if Inter is absent.
