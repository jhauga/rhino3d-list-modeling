# Roadmap

## Goal

Publish the plugin to the Rhino package marketplace (yak) under the package
name `list-modeling`, once the alpha has been verified on real models.

## Current state: 0.0.0-alpha.1.1.0

The simple use case works end to end: a list of rectangular, round, elliptic
or polygonal parts with an orientation, dimensions, a position and an action
becomes solids, cutters and curves on named layers, run from a file, from the
panel, from step-by-step prompts, or from a script. A selection can be turned
back into a list.

The second alpha adds the first tooling and the first part made along a curve:
`filletEdge` rounds the edges of an extruded part, and the `pipe` action builds
a tube along a curve part or a named curve in the model, which can also cut a
groove. Layers can be nested, every number can be written as arithmetic, and
`actions` reads as how a part is made followed by what its solid does. Treat
every build as experimental.

## Expanding the syntax

The syntax was designed so new element types and actions slot in without
changing existing lists. Two directions were planned: surface elements, and
tooling. Tooling started first, with fillets, and surface elements are next.

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
part can already describe, and the `curve` property that a pipe uses to name
its rail is the way a sweep will name one too. Each of these will be an
element handler in the runner, next to the solid and union handlers.

### Tooling

Fillets shipped first, as a `filletEdge` block on the part rather than an
extra action, because an edge operation needs to say which edges:

```markdown
- filletEdge:
  - select: 0, 1
  - radius: .5, [.5, .25]
```

Loops are counted along the part's own depth, or along a world axis inside a
`top`, `front` or `right` group, and a bracketed radius goes edge by edge.
Fillets are applied to the part's own solid before the boolean step, so the
loop numbers always describe the extrusion the list wrote. The full rules are
in [syntax.md](syntax.md#fillet-edges).

`chamfer` and `blend` will follow the same shape: a `chamferEdge` or
`blendEdge` block with the same `select` lines and a distance in place of the
radius.

The `pipe` action shipped alongside it as the first way to make a part other
than extruding a profile. `actions` now reads as how a part is made (`extrude`,
`curve`, `pipe`) followed by what its solid does (`subtract`, `union`), which
leaves room for the next generators, such as `sweep`, without new syntax.

## Sibling plugin

`rhino3d-markdown-notes` will offer **Export to 'List Modeling'** when this
plugin is installed, running a note as a list in the background while the notes
panel stays open. The contract is written down in
[compatibility.md](compatibility.md) and only needs work on the notes side.

## Planned, in order

1. Verify the alpha in Rhino on Windows: all three command routes, the panel
   round trip, persistence in the `.3dm`, undo, and the boolean retry path.
2. First pre-release package on the yak test server; confirm the `rh8` tag.
3. Verify fillets and pipes by eye beside the reference models, then add
   Steps prompts for them.
4. Surface elements, starting with `Loft` via `offset`.
5. Tooling: `chamferEdge` and `blendEdge`, on the `filletEdge` pattern.
6. Sibling integration with `rhino3d-markdown-notes`.
7. Marketplace release.

[TODO.md](../TODO.md) tracks the items above at task granularity.
