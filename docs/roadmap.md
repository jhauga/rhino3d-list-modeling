# Roadmap

## Goal

Publish the plugin to the Rhino package marketplace (yak) under the package
name `list-modeling`, once the alpha has been verified on real models.

## Current state: 0.0.0-alpha.1

The simple use case works end to end: a list of rectangular, round, elliptic
or polygonal parts with an orientation, dimensions, a position and an action
becomes solids, cutters and curves on named layers, run from a file, from the
panel, from step-by-step prompts, or from a script. A selection can be turned
back into a list. Treat every build as experimental.

## Expanding the syntax

The syntax was designed so new element types and actions slot in without
changing existing lists. Two directions are planned, in this order.

### Surface elements

`Loft`, `Revolve`, `Sweep1`, `Sweep2`, `Patch` and `Cap` are already accepted
as element types; today they emit each part's profile curve and a warning.
Making them real needs one thing the syntax does not have yet: a way to place
several profiles at different positions along the depth axis. The `offset`
property already lifts a profile along that axis, so a loft can be written as
a sequence of parts with increasing offsets:

```markdown
# Element: Loft

## Base

* orientation: top
* shape: circle
* width: 10mm
* height: 10mm
* offset: 0

## Neck

* orientation: top
* shape: circle
* width: 4mm
* height: 4mm
* offset: 30mm
```

Revolve needs an axis; the natural form is an `axis` property on the element
or a `curve` part flagged as the axis. Sweeps need a rail, which a `curve`
part can already describe. Each of these will be an element handler in the
runner, next to the solid and union handlers.

### Tooling

`fillet`, `chamfer` and `blend` are edge operations, so they belong on a part
as additional actions with a radius or distance:

```markdown
* actions: extrude, fillet
* fillet: 2mm
```

Applied after the boolean step, to the edges of the resulting solid. Edge
selection rules (all edges, only the edges on one face, only vertical edges)
will need a small vocabulary of their own.

## Sibling plugin

`rhino3d-markdown-notes` will offer **Export to 'List Modeling'** when this
plugin is installed, running a note as a list in the background while the notes
panel stays open. The contract is written down in
[compatibility.md](compatibility.md) and only needs work on the notes side.

## Planned, in order

1. Verify the alpha in Rhino on Windows: all three command routes, the panel
   round trip, persistence in the `.3dm`, undo, and the boolean retry path.
2. First pre-release package on the yak test server; confirm the `rh8` tag.
3. Surface elements, starting with `Loft` via `offset`.
4. Tooling actions, starting with `fillet`.
5. Sibling integration with `rhino3d-markdown-notes`.
6. Marketplace release.

[TODO.md](../TODO.md) tracks the items above at task granularity.
