# 0013 — The menu says which environment it is: a name, a logo and the version

- **Status**: accepted
- **Date**: 2026-11-13
- **Spec**: D-21, F-SYS-04; follows Mnemarr's ADR 0044 and Portarr's D-91, so that the three products say the same thing in the same place

## Context

The sandbox, a test host and the production Geminarr look the same, and a person who has several tabs open does not know which one they are in, nor which version runs. Portarr and Mnemarr solve it with a small block at the
bottom of the menu: the name of the **laboratory** (the environment, not the application), a picture and the version. The maintainer asked for exactly the same in Geminarr, with the same style, so that the design stays
homogeneous. The picture is personal: it must not be in this public repository.

## Decision

1. **A badge at the bottom of the menu, the same as Mnemarr's** (its CSS and its behavior are copied, not reinvented): the name (display font, 600), the image (at most 96 px, rounded corners, no card or border) and the version
   (`v0.1.0`, mono, small), straight on the sidebar. Under 768 px only the image remains, the texts stay available to assistive technology. The texts take the accent color unless a color is set. The version follows the system
   status (asked every 30 seconds), so that a new version shows up without a reload.
2. **Two layers, the nearest wins**: what the **deployment** says (`GEMINARR__IDENTITY__NAME`, `…COLORS__NAME`, `…COLORS__VERSION`, `…LOGOFILE`: a file mounted into the container, checked like an upload), then the **product's own**
   (its name, its drawing of two rings, the accent). There is no interface to change it and no database: Geminarr has one operator and is configured by environment variables and files (the *arr pattern), and each environment says its own in the
   repository that describes it (GitOps), which keeps the personal picture out of this repository. Mnemarr's third layer (an administrator's, stored) is not built here; it would be a decision of its own.
3. **Everything that is given is checked, by the same rules as Mnemarr and Portarr**: the name is one line of at most 40 characters, a color is `#rgb`, `#rrggbb` or a theme token (a value never carries CSS, and the interface checks it
   again), the image is a png, a webp or an svg of at most 1 MiB **read by its content** (an svg is well formed, has the SVG root, no script, event handler, `foreignObject`, link to another document, DTD or style that loads from
   outside). A bad name or color stops the start with a sentence that names the setting; a logo that fails is not used, and the reason is shown (never its path).
4. **The image is served like an icon**: `GET /api/v1/identity/logo?v=<hash>` (sign-in required, like the rest) with `private, max-age=31536000, immutable` for the hash and five minutes without it, an ETag, `nosniff` and
   `default-src 'none'; sandbox`. The general headers of the host leave alone the answer of an endpoint that says so (`[KeepsItsOwnHeaders]`), only when the image is served (200, or 304); an error on the same address has the
   application's headers like any other.
5. **`GET /api/v1/identity`** (name, colors, logo address, version, why a logo is not used) answers the operator who is signed in, and nobody else (the fallback policy).
6. **The header carries the product's mark** (the two rings) next to its name: the product's drawing is in the header, the laboratory's picture is in the menu.

## Alternatives considered

- **A setting in the interface, stored**: rejected for now, one operator and a container configured by files; the cost is a redeploy to change a name.
- **The picture in the repository**: rejected, it is personal and the repository is public.
- **A different design**: rejected, the maintainer wants the same one in the three products.

## Consequences

- A deployment that sets nothing shows "Geminarr", its two rings and its version. The real deployment mounts the laboratory's picture (a file of its GitOps folder) and sets its name.
- The settings page shows the four settings, the path of the logo never.
