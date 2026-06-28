# design/

The Figma <-> code bridge for SaaS BTP.

This folder is the boundary between design source-of-truth (Figma) and the
codebase. It is populated at **AI OS Palier 3** (design-to-code integration);
it is intentionally empty scaffolding for now.

## Layout

| Path             | Purpose                                                        |
|------------------|---------------------------------------------------------------|
| `tokens/`        | Design tokens exported from Figma (colors, type, spacing, ...) consumed by `apps/web` (Tailwind/Shadcn) and `apps/mobile`. |
| `exports/`       | Static assets exported from Figma (icons, illustrations, logos). |

## Source of truth

- Figma file: TODO — add the SaaS BTP Figma file URL here.

## Status

- Palier: not started (activated at AI OS Palier 3).
- Token pipeline (Figma export -> `tokens/` -> app theme) to be defined then.
