# List Syntax

A list is a Markdown file (`.md` or `.txt`) made of headers and bullet
properties. Headers give the structure, bullets give the values, and the
plugin turns each part into geometry.

## Structure

Three header levels carry meaning:

```text
# Element: <Type>          one operation applied across the parts below it
## <Part name>             one modeled piece, any name you like
### group:<identifier>     a piece nested under a part, sharing a group id
- key: value               a property of the part above it
  - key: value             a nested line, read by the property above it
```

- A level 1 header starts an **element**. The word after `Element:` is the
  element type (see [Element types](#element-types)). `# BooleanDifference`
  without the `Element:` prefix also works.
- A level 2 header starts a **part** and names it. The name becomes the object
  name in Rhino.
- A level 3 header of the form `group:<identifier>` starts a grouped part. All
  grouped parts with the same identifier are treated as one selection: cutters
  are applied together, and surviving objects are put in a Rhino group with
  that name.
- A level 2 header with any level 3 header under it is a **container**: it
  labels the parts nested below and is never built itself. Properties written
  directly under it become the defaults its children start from, so shared
  values are written once. Properties repeated on a child override them.
- Property bullets may use `-`, `*` or `+`; lists written by the plugin use
  `-`. Keys are case-insensitive and ignore spaces and underscores, so
  `LayerName`, `layer name` and `layer_name` are the same key.
- A property can carry **nested lines**, indented under it. `filletEdge`,
  `radius` and `LayerName` read their nested lines (see
  [Fillet edges](#fillet-edges), [Pipes](#pipes) and
  [Nested layers](#nested-layers)). Under any other property, nested lines are
  read as ordinary properties of the part, so uneven indentation does no harm.

Lines starting with `()->`, `()=>` or `<!--` are comments, and an HTML comment
may span several lines. Block quotes (`> note`) and thematic breaks (`---`) are
skipped without comment, so notes can sit between parts. Code fence lines are
ignored, so a list pasted inside a fenced block still runs. A value of `null`
or `none` is the same as leaving the property out.

## Properties

| Key | Value | Default | Notes |
| --- | --- | --- | --- |
| `LayerName` | text, or a nested layer path | current layer | Optional on every part. Created if missing; a missing or `null` value builds on the layer that is current when the list runs. See [Nested layers](#nested-layers). |
| `LayerColor` | color | layer default | Only used when the layer is created. See [Colors](#colors). |
| `orientation` | `top`, `bottom`, `front`, `back`, `right`, `left` (`side` = left) | `top` | Decides the axis mapping. |
| `shape` | `rectangle`, `circle`, `ellipse`, `polygon:N`, `hexagon`, `octagon`, ... | `rectangle` | See [Shapes](#shapes). |
| `radius` | number with optional unit, or nested stations | | One value for both plane axes; sets width and height together and implies `circle` when no shape is named. On a pipe, the pipe's radius. |
| `height` | number with optional unit | required unless `radius` is given | Along the height axis of the orientation. Not used by pipes. |
| `width` | number with optional unit | required unless `radius` is given | Along the width axis. Not used by pipes. |
| `depth` | number with optional unit | required for `extrude` | Extrusion distance along the depth axis. `distance`, `length` and `thickness` mean the same. |
| `position` | two numbers, comma separated | `0, 0` | Center of the profile on the orientation plane. A third value sets `offset`. |
| `offset` | number with optional unit | `0` | Optional start along the depth axis. |
| `filletEdge` | nested `select` and `radius` lines | | Rounds edges of an extruded part. See [Fillet edges](#fillet-edges). |
| `curve` | a part name or an object name | | The curve a pipe follows. See [Pipes](#pipes). |
| `actions` | `extrude`, `curve` or `pipe`, then `subtract` or `union` | `extrude` | See [Actions](#actions). |

### Units

Write a unit directly after the number: `.84in`, `10 mm`, `2cm`, `1.5m`,
`3ft`. A number without a unit is in the model's units. Recognized units are
inches, millimeters, centimeters, meters and feet. Values are converted to the
model units of the open document, so one list works in a millimeter file and
an inch file alike.

### Arithmetic

Any number can be written as arithmetic with `+`, `-`, `*`, `/` and
parentheses. A unit may follow any number or a closing parenthesis, and it
names the unit of the whole value:

```markdown
- radius: 1.25in/2
- width: (40 + 2.5)mm
- position: 10/4, -(3 - 1)
```

- One value uses one unit: `1in + 2mm` is an error.
- Multiplying two lengths gives an area, and dividing one length by another
  gives a ratio. Neither is a length, so both are errors. `2in * 3` and
  `2in / 4` are fine.
- Division by zero is an error.
- Lists written by the plugin carry the result, so `1.25in/2` is written back
  as `0.625in`.
- In a `position`, separate the values with commas when the arithmetic has
  spaces in it.
- Text in parentheses followed by `->` is a shorthand annotation and is
  dropped, so keep `->` out of a value that uses parentheses.

### Orientation and axes

The orientation names the face the profile is drawn on. Height, width and
depth follow the axes of that face, and position is read on that face:

| Orientation | Width axis | Height axis | Depth axis | Position means |
| --- | --- | --- | --- | --- |
| `top` | X | Y | +Z | x, y |
| `bottom` | X | Y | -Z | x, y |
| `front` | X | Z | +Y | x, z |
| `back` | X | Z | -Y | x, z |
| `right` | Y | Z | +X | y, z |
| `left` / `side` | Y | Z | -X | y, z |

The profile is centered on the position, drawn at zero along the depth axis
(or at `offset` when given), and extruded `depth` along the depth axis in the
direction shown. A `bottom` part therefore grows downward from the XY plane.

### Shapes

| Shape | Uses `width` as | Uses `height` as |
| --- | --- | --- |
| `rectangle` (default) | full width | full height |
| `circle` | radius | ignored (write the same value, or use `radius`) |
| `ellipse` | radius along the width axis | radius along the height axis |
| `polygon:N` | radius of the circle the polygon is inscribed in | ignored |

Circles and polygons are easier to write with `radius`, which fills width and
height with one value relative to the orientation plane:

```markdown
- shape: circle
- radius: .05in
```

Writing `radius` without a `shape` line makes the part a circle. A `radius`
followed by `shape: hexagon` gives a hexagon inscribed in that radius. Word
shapes `triangle`, `pentagon`, `hexagon` and `octagon` set the side count for
you.

### Colors

`LayerColor` accepts .NET color names with or without spaces (`Dark Blue`,
`darkblue`, `Brown`), hex triplets (`#1f2a44`, `#fa0`), and channel lists
(`31, 42, 68` or `rgb(31, 42, 68)`). A value that cannot be read produces a
warning and the layer keeps Rhino's default color.

### Nested layers

A layer inside another layer can be written three ways, and all three name
the same layer, `Posts` under `Shop`:

```markdown
- LayerName: Shop::Posts

- LayerName: "Shop", "Posts"

- LayerName: Shop
  - Posts
```

- Every level is created on demand. Only the last level takes the
  `LayerColor`; parents created on the way keep Rhino's default color.
- A plain name with no path finds a layer of that name wherever it sits.
- Quotes split levels only when the value is nothing but quoted names and
  commas, so `Bolts, spare` is still one layer with a comma in its name.
- Nested bullets can go several levels deep. Two nested names at the same
  level produce a warning, and the first one is used.
- Lists written by the plugin use the quoted form.

### Actions

`actions` is one or two words: how the part is made, then what its solid does
in the element.

| Word | Kind | Effect |
| --- | --- | --- |
| `extrude` | made by | The profile extruded along the depth axis. The default. |
| `curve` | made by | Only the profile curve; `depth` is not needed. |
| `pipe` | made by | A round tube along a curve. See [Pipes](#pipes). |
| `subtract` | role | The solid cuts into every kept solid in the element and is consumed. |
| `union` | role | The solid merges into the kept solids. |

A part with no role is kept as it is. A role on its own means an extruded
part: `subtract` is the same as `extrude, subtract`. `pipe, subtract` cuts a
groove along a curve:

```markdown
- curve: Ring
- radius: .1
- actions: pipe, subtract
```

A curve cannot cut or merge, so `curve, subtract` produces a warning and keeps
the curve. Two words of the same kind produce a warning and the first one is
used. `surface` is accepted and treated as `curve` for now, with a warning, so
the surface step can be done by hand.

## Fillet edges

`filletEdge` rounds edges of an extruded part. Each `select` line picks edge
loops, and the `radius` line after it gives their radius:

```markdown
## Post

- shape: circle
- radius: 1
- depth: 4
- filletEdge:
  - select: 1
  - radius: .25
- actions: extrude
```

A **loop** is the set of edges lying in one plane across the solid: one round
edge on a cylinder, four edges on a box. A plain `select` counts loops along
the part's own depth: `0` is the loop on the profile plane, at `offset`, and
`1` is the loop at the end of the depth. The post above has its top rim
rounded; `select: 0, 1` would round both rims.

`radius` takes one entry per selected loop, in the same order. Fewer entries
than loops means the last one repeats, so `radius: .25` rounds every selected
loop alike:

```markdown
- filletEdge:
  - select: 0, 1
  - radius: .5, .25
```

### Edge by edge

A bracketed entry gives a loop's edges different radii. Edges are read
clockwise from the top: top, right, bottom, left, looking at the profile with
width to the right and height up. On a four-edge loop the values expand the
way CSS margins do:

| Entry | Top | Right | Bottom | Left |
| --- | --- | --- | --- | --- |
| `[a]` or `a` | a | a | a | a |
| `[a, b]` | a | b | a | b |
| `[a, b, c]` | a | b | c | b |
| `[a, b, c, d]` | a | b | c | d |

```markdown
## Plate

- height: 4
- width: 6
- depth: 1
- filletEdge:
  - select: 0, 1
  - radius: .1, [.5, .25]
- actions: extrude
```

The plate's bottom loop is rounded `.1` all around. On its top loop the two
edges along the width get `.5` and the two along the height get `.25`. A loop
with other than four edges takes the values in clockwise order, repeating the
list.

### Face groups

`select: top`, `select: front` and `select: right` open a group that counts
loops along a world axis instead of along the part's depth. Inside the group,
`0` is the loop at the lowest coordinate on that axis and `1` the highest, and
edges are ordered as seen in the matching Rhino view:

| Group | Counts along | Edge order as seen in |
| --- | --- | --- |
| plain `select` | the part's depth | the profile: width right, height up |
| `top` | world Z | the Top view: X right, Y up |
| `front` | world Y | the Front view: X right, Z up |
| `right` | world X | the Right view: Y right, Z up |

`bottom`, `back` and `left` (or `side`) count along the same axes as `top`,
`front` and `right`, with `0` still the lowest loop.

When two selects name the same edge, the first one wins. That makes it short
to round every edge of a box:

```markdown
- filletEdge:
  - select: top
    - select: 0, 1
    - radius: .1, .2
  - select: front
    - select: 0, 1
    - radius: .05
```

The `top` group takes all eight horizontal edges, `.1` below and `.2` above.
The `front` group's loops also contain four of those edges, which keep their
radius, so it adds only the four upright edges at `.05`.

### When fillets are applied

Fillets are applied to a part's own solid as soon as it is extruded, before
the element's union and subtract steps, so loop `0` and loop `1` always mean
the ends of the extrusion. A filleted cutter cuts with its rounded edges.

A radius too large for the edges around it produces a warning, and the part is
kept without fillets. A `filletEdge` on a curve or pipe part produces a
warning and is ignored.

## Pipes

A pipe is a round tube along a curve. It needs only a `curve` and a `radius`:

```markdown
## Ring

- shape: circle
- radius: 3
- position: 0, 0
- actions: curve

## Tube

- curve: Ring
- radius: .25
- actions: pipe
```

- `curve` names a curve part anywhere in the list, before or after the pipe and
  in any element. When no part has that name, a curve already in the model with
  that object name is used, so a pipe can follow a curve drawn by hand. A name
  found in neither place is an error.
- Height, width, depth and position do not apply to a pipe; the curve places
  it.
- A pipe along an open curve gets flat ends, so every pipe is a closed solid
  that can be kept, merged or used as a cutter.
- The curve part stays in the model.

### A radius that changes

Nested lines under `radius` set the radius at points along the curve, and the
pipe blends between them:

```markdown
- curve: Rail
- radius:
  - 0%: .25
  - 50%: .1
  - 100%: .25
- actions: pipe
```

A percentage is a distance along the curve's length. A bare number is a
parameter of the curve itself; for a circle part that is the angle in radians
from the point on its width axis, `0` to `6.2832`. A station that falls off
the curve is an error that names the curve's range. On a closed curve the
start and the end are the same point, so `0%` and `100%` need the same
radius.

A pipe whose radius is within five times the model's absolute tolerance is
still built, but booleans with it often fail. The run warns when that happens;
a smaller absolute tolerance in Document Properties solves it.

## Element types

| Type | Family | Behavior |
| --- | --- | --- |
| `Boolean`, `Solid` | solid | Kept parts stay separate; union parts merge in; subtract parts cut. |
| `BooleanDifference`, `Difference` | solid | Same as `Boolean`; the name documents intent. On the Steps route, a BooleanDifference with no cutters asks how to get them. |
| `BooleanUnion`, `Union` | union | All extruded parts are unioned first, then cutters are applied. |
| `Loft`, `Revolve`, `Sweep1`, `Sweep2`, `Patch`, `Cap`, `Surface` | surface | Planned. Every part produces its profile curve and a warning is reported. |

An unknown type produces a warning and is treated like `Boolean`. If the first
part appears before any element header, `# Element: Boolean` is assumed.

## Example

A board with two round mount holes on one layer and a box on another:

```markdown
# Element: BooleanDifference

## Circuit Board

* LayerName: Circuit_Board
* LayerColor: Dark Blue
* orientation: top
* height: .84in
* width:  1.68in
* depth:  .04in
* position: 0,0
* actions: extrude

## Mount Holes

### group:holes

- orientation: top
- shape: circle
- radius: .05in
- depth:  1in
- position: .62, -.35
- actions: subtract

### group:holes

- orientation: top
- shape: circle
- radius: .05in
- depth:  1in
- position: -.62, .35
- actions: subtract

# Element: Boolean

## Box

* LayerName: Shipping_Box
* LayerColor: Brown
* orientation: top
* height: 1in
* width:  2in
* depth:  .25in
* position: 2,2
* actions: extrude
```

The same file lives at [examples/circuit-board.md](../examples/circuit-board.md).
[examples/filleted-post.md](../examples/filleted-post.md) puts fillets, a pipe
groove, nested layers and arithmetic together.

A longer walk-through of writing a parts list, with small standalone scripts
that build one in Rhino and in FreeCAD:
[Model From a Parts List](https://gist.github.com/jhauga/615902a81b4200e557ca62bb8e99f7ea).

## Diagnostics

The parser and the runner report three levels, and the level decides whether the
part is built:

- **Error**: the part cannot be built correctly, so it is skipped. A missing
  dimension, an unreadable number anywhere in the part, arithmetic that is not a
  length, a zero or negative dimension, a pipe without its curve or radius, and
  a curve name that cannot be found are errors. The rest of the list still runs.
- **Warning**: a value could not be read but resolves to a documented default,
  so the part is still built. An unknown color, orientation, shape or action, a
  position with the wrong number of values, extra action words, a fillet that
  could not be made, a pipe close to the model tolerance, a boolean that failed
  and left its parts behind, and a planned element type are warnings.
- **Info**: ignored lines and assumed defaults.

All three print to the command line. When a person triggered the run, the
completion popup repeats the warnings and errors.
