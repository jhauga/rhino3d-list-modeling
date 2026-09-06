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
* key: value               a property of the part above it
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
- Property bullets may use `*`, `-` or `+`. Keys are case-insensitive and
  ignore spaces and underscores, so `LayerName`, `layer name` and `layer_name`
  are the same key.

Lines starting with `()->`, `()=>` or `<!--` are comments. Code fence lines
are ignored, so a list pasted inside a fenced block still runs. A value of
`null` or `none` is the same as leaving the property out.

## Properties

| Key | Value | Default | Notes |
| --- | --- | --- | --- |
| `LayerName` | text | current layer | Optional on every part. Created if missing; a missing or `null` value builds on the layer that is current when the list runs. |
| `LayerColor` | color | layer default | Only used when the layer is created. See [Colors](#colors). |
| `orientation` | `top`, `bottom`, `front`, `back`, `right`, `left` (`side` = left) | `top` | Decides the axis mapping. |
| `shape` | `rectangle`, `circle`, `ellipse`, `polygon:N`, `hexagon`, `octagon`, ... | `rectangle` | See [Shapes](#shapes). |
| `radius` | number with optional unit | | One value for both plane axes; sets width and height together and implies `circle` when no shape is named. |
| `height` | number with optional unit | required unless `radius` is given | Along the height axis of the orientation. |
| `width` | number with optional unit | required unless `radius` is given | Along the width axis. |
| `depth` | number with optional unit | required unless action is `curve` | Extrusion distance along the depth axis. |
| `position` | two numbers, comma separated | `0, 0` | Center of the profile on the orientation plane. A third value sets `offset`. |
| `offset` | number with optional unit | `0` | Optional start along the depth axis. |
| `actions` | `extrude`, `subtract`, `union`, `curve` | `extrude` | What happens to the profile. |

### Units

Write a unit directly after the number: `.84in`, `10 mm`, `2cm`, `1.5m`,
`3ft`. A number without a unit is in the model's units. Recognized units are
inches, millimeters, centimeters, meters and feet. Values are converted to the
model units of the open document, so one list works in a millimeter file and
an inch file alike.

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
* shape: circle
* radius: .05in
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

### Actions

| Action | Effect |
| --- | --- |
| `extrude` | Builds a solid from the profile and keeps it. |
| `subtract` | Builds a solid and cuts it out of every kept solid in the element. The cutter itself is consumed. |
| `union` | Builds a solid and merges it into the kept solids. |
| `curve` | Adds only the profile curve; `depth` is not needed. |

`surface` is accepted and treated as `curve` for now, with a warning, so the
surface step can be done by hand.

## Element types

| Type | Family | Behavior |
| --- | --- | --- |
| `Boolean`, `Solid` | solid | Extruded parts stay separate; union parts merge in; subtract parts cut. |
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

## Diagnostics

The parser and the runner report three levels, and the level decides whether the
part is built:

- **Error**: the part cannot be built correctly, so it is skipped. A missing
  dimension, an unreadable number, and a zero or negative dimension are errors.
  The rest of the list still runs.
- **Warning**: a value could not be read but resolves to a documented default,
  so the part is still built. An unknown color, orientation, shape or action, a
  position with the wrong number of values, a boolean that failed and left its
  parts behind, and a planned element type are warnings.
- **Info**: ignored lines and assumed defaults.

All three print to the command line. When a person triggered the run, the
completion popup repeats the warnings and errors.
