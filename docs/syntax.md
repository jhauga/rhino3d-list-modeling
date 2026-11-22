# List Syntax

A list is a Markdown file (`.md` or `.txt`) made of headers and bullet
properties. Headers give the structure, bullets give the values, and the
plugin turns each part into geometry.

## Structure

Headers give the structure and bullets give the values:

```text
# Element: <Type>          one operation applied across the parts below it
- key: value               a default for every part in the element
## <Name>                  a header: a part, or a container for deeper headers
### <Name>                 a header nested under the one above it
#### group:<identifier>    a part that joins the group with that identifier
- key: value               a property of the header above it
  - key: value             a nested line, read by the property above it
- select: <target>         a step: select the objects the target names
- deselect: <target>       a step: take them out of the selection
- actions: <step>          a step that acts on what is selected
- value: <input>           the input of the step above it
```

- A level 1 header starts an **element**. The word after `Element:` is the
  element type (see [Element types](#element-types)). `# BooleanDifference`
  without the `Element:` prefix also works.
- Every other header names a **part**, and the name becomes the object name in
  Rhino. A header belongs under the nearest header above it with fewer `#`, at
  any depth, and a level may be skipped.
- A header with any deeper header under it is a **container**: it labels the
  headers nested below it and is never built itself. Its properties are
  defaults for every part nested under it, at every level (see
  [Shared values](#shared-values)).
- A header of the form `group:<identifier>` starts a grouped part, named after
  the nearest header above it that is not a group. All grouped parts with the
  same identifier are treated as one selection: cutters are applied together,
  and surviving objects are put in a Rhino group with that name.
- Properties written between `# Element:` and the first header below it are
  defaults for every part in the element.
- A header written again later in the list reuses it (see
  [Reusing headers](#reusing-headers)).
- A header whose own lines hold **steps** is a **sequence**: instead of building
  a shape, it runs its steps in order on what is selected, the way a person
  selects objects in Rhino and runs a command on them (see [Steps](#steps)).
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
ignored, so a list pasted inside a fenced block still runs. An empty value,
`null` or `none` clears a value set by a header above (see
[Empty values](#empty-values)).

## Shared values

A value written on a container reaches every part nested under it, however
deep. The nearest header wins: a part's own line beats its container, and a
container beats the container above it. Under one header, a later line beats
an earlier one.

```markdown
# Element: BooleanDifference

- LayerName: Shop::Plate

## Plate

- width: 10
- height: 6
- depth: 1

## Pockets

- actions: subtract
- shape: square
- width: 1

### Foot Pocket

- z-position: 0
- depth: .25

#### group:feet

- position: -4, -2

#### group:feet

- position: 4, 2
```

Every part of this element is on `Shop::Plate`. Both foot pockets are 1 by 1
squares cut .25 deep into the underside of the plate: the action and the square
come from `Pockets`, the `z-position` and the depth from `Foot Pocket`, and only
the position is each part's own.

### Empty values

A property with nothing after it, or with `null` or `none`, clears what the
headers above it said about that key:

```markdown
## Pockets

- actions: subtract
- width: 1

### Spacer

- actions:
- height: 1
- depth: .25
```

`Spacer` is a plain extruded block, not a cutter. Where a part is first
written, a cleared value goes back to the element's default, or to the built-in
default when the element sets none. In a part built again somewhere else, the
value of its new place shows through instead (see
[Where a rebuilt part's values come from](#where-a-rebuilt-parts-values-come-from)).

## Reusing headers

A header can be written again later in the list, in the same element or in a
later one, to build its parts again or to change its values:

| Written again as | Name seen before as | Meaning |
| --- | --- | --- |
| a header with deeper headers under it | a container | Continues that container. Lines under it are added to the container's, and only the headers listed under it here are built. |
| a header with lines but no deeper headers | a container | Changes the container's values from that line on. Nothing is built here, and an info line says so. |
| a header with nothing under it | a part | Builds that part again. |
| a header with nothing under it | a container | Builds every part of that container again. |
| a header with lines under it | a part | A new part that happens to share the name, as always. |

- Names match regardless of case, extra spaces and header level. When several
  headers share a name, the latest one is reused.
- A change applies from that line on. Parts built earlier keep the values they
  were built with.
- `group:` headers are never matched by name; group references rebuild them.

### Group references

`group:<identifier>[*]` builds every part of that group written before this
line again, and `group:<identifier>[n]` builds one of them, counting from 0 in
the order they were written:

```markdown
#### group:feet[*]

#### group:feet[2]

- depth: .5
```

Lines under a reference apply to that rebuilt part only and win over every
other value. A rebuilt grouped part keeps its group, so it joins the Rhino group
of that name. An unknown group, a number past the end of the group, and
anything between the brackets other than `*` or one number are errors that
skip only that reference.

### Where a rebuilt part's values come from

A rebuilt part keeps its own values and those of the headers it was first
written under. The headers around the place it is rebuilt, and that element's
defaults, fill in only what those leave out. To let the new place decide a
value, clear it on the original container first:

```markdown
# Element: BooleanDifference

## Pockets

- actions:
- LayerName: Shop::Feet

## Feet

- actions: extrude

### Foot Pocket

#### group:feet[*]
```

Following the list under [Shared values](#shared-values), the first header
clears `subtract` on `Pockets` and moves its parts to `Shop::Feet`. `Feet` then
builds every foot pocket again as a solid: the same 1 by 1 square, .25 deep, at
the same positions. Without the cleared line, the rebuilt pockets would still
be cutters.

### What an element builds

Each part is built once per element; a second mention of the same part is
skipped with an info line. When an element rebuilds a container with a bare
header and also references one of its groups, the reference decides which parts
of that group are built:

```markdown
### Foot Pocket

### Holder

#### group:feet[0]

#### group:feet[2]
```

Only the first and the third foot are built, although `### Foot Pocket` on its
own would build every one.

## Properties

| Key | Value | Default | Notes |
| --- | --- | --- | --- |
| `LayerName` | text, or a nested layer path | current layer | Optional on every part. Created if missing; a missing or `null` value builds on the layer that is current when the list runs. On a `select` part with no target, the layer to read; a `select` step without a value reads the nearest one above it. See [Nested layers](#nested-layers). |
| `LayerColor` | color | layer default | Only used when the layer is created. See [Colors](#colors). |
| `orientation` | `top`, `bottom`, `front`, `back`, `right`, `left` (`side` = left) | `top` | Decides the axis mapping. |
| `shape` | `rectangle`, `square`, `circle`, `ellipse`, `polygon:N`, `hexagon`, `octagon`, ... | `rectangle` | See [Shapes](#shapes). |
| `radius` | number with optional unit, or nested stations | | One value for both plane axes; sets width and height together and implies `circle` when no shape is named. On a pipe, the pipe's radius. |
| `height` | number with optional unit | required unless `radius` is given | Along the height axis of the orientation. A square needs only one of `height` and `width`. Not used by pipes. |
| `width` | number with optional unit | required unless `radius` is given | Along the width axis. Not used by pipes. |
| `depth` | number with optional unit, or `through` | required for `extrude` | Extrusion distance along the depth axis; a negative depth extrudes the other way. `through` cuts through the element's kept solids (see [Through cuts](#through-cuts)). `distance`, `length` and `thickness` mean the same. |
| `position` | two numbers, comma separated | `0, 0` | Center of the profile on the orientation plane. A third value sets `offset`. |
| `offset` | number with optional unit | `0` | Optional start along the depth axis. |
| `x-position`, `y-position`, `z-position` | number with optional unit | | One world coordinate of the profile, whatever the orientation. See [World coordinates](#world-coordinates). |
| `filletEdge` | nested `select` and `radius` lines | | Rounds edges of an extruded part. See [Fillet edges](#fillet-edges). |
| `curve` | a part name or an object name | | The curve a pipe follows. See [Pipes](#pipes). |
| `actions` | `extrude`, `curve`, `pipe` or `select`, then `subtract` or `union`; or step words | `extrude` | See [Actions](#actions). The step words (`select`, `deselect`, `deselectAll`, `offset`, `extrude`, `booleanDifference`, `copy`, `paste`, `changeLayer`, `delete`) run as steps; see [Steps](#steps). |
| `select` | a target, or nothing | | A step that adds the objects a target names to the selection. Alone under a header, it makes a select part. See [Targets](#targets). |
| `deselect` | a target | | A step that takes the objects a target names out of the selection. |
| `value` | a number, a layer or a target | | The input of the step above it, the one Rhino would ask for. See [Steps](#steps). |

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
direction shown. A `bottom` part therefore grows downward from the XY plane. A
negative `depth` extrudes the other way from the same profile, so a pocket can
be cut down from a top face:

```markdown
- orientation: top
- z-position: 1
- depth: -.1
```

### World coordinates

`x-position`, `y-position` and `z-position` set one world coordinate of the
profile, whatever the orientation. Each one lands on the profile's center or on
the plane the profile is drawn on:

| Orientation | `x-position` sets | `y-position` sets | `z-position` sets |
| --- | --- | --- | --- |
| `top`, `bottom` | the center | the center | the profile plane |
| `front`, `back` | the center | the profile plane | the center |
| `right`, `left` / `side` | the profile plane | the center | the center |

`z-position: 1` on a `bottom` part draws the profile at z = 1 and extrudes it
down from there; on a `front` part it puts the center of the profile at z = 1.

These lines and `position` or `offset` write the same coordinates, so the usual
rule decides between them: the line nearest the part wins, and under one header
the later line wins. A `z-position` on a container is therefore overridden by a
third `position` value on a part below it. Lists written by the plugin carry the
result as `position` and `offset`.

### Shapes

| Shape | Uses `width` as | Uses `height` as |
| --- | --- | --- |
| `rectangle` (default) | full width | full height |
| `square` | side length | side length when `width` is not written |
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

A `square` needs one side, written as `width` or as `height`. When both are
written and differ, the run warns and uses the width. `box` and `rect` are
other names for `rectangle`.

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
| `select` | made by | The objects already in the model that it names, or those on its layer. See [Editing solids already in the model](#editing-solids-already-in-the-model). |
| `subtract` | role | The solid cuts into every kept solid it reaches in the element and is consumed. |
| `union` | role | The solid merges into the kept solids. |

The same `actions` key also runs **steps** on what is selected, such as
`offset`, `copy` or `delete`. A header whose own lines hold steps is a sequence
rather than a part; see [Steps](#steps).

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

A cutter that reaches none of the element's kept solids cuts nothing. It
produces a warning and is left in the model, so a cutter in the wrong place is
easy to find.

### Through cuts

`depth: through` on a subtract part makes the cutter run all the way through
the kept solids of its element:

```markdown
## Screw Hole

- shape: circle
- radius: .2
- depth: through
- position: 4, 2
- actions: subtract
```

- The cutter spans the full extent of the element's kept solids along its depth
  axis, measured after the union step, with a small margin at each end. Its own
  `offset` or depth-axis coordinate is ignored; its position on the profile
  plane is used as usual.
- `through` applies to subtract parts only. On any other part it is an error.
- An element with no kept solid to go through reports an error for the cutter.
- A `filletEdge` on a through cutter would round edges outside the solid, so it
  produces a warning and is ignored.

### Editing solids already in the model

A `select` part takes objects already in the model as kept solids of its
element, so a list can cut into or add onto a model drawn by hand. It finds
them by layer, by the header that built them, or by object name:

```markdown
# Element: BooleanDifference

## Plate

- LayerName: Shop::Plate
- actions: select

## Screw Hole

- shape: circle
- radius: .2
- depth: through
- position: 4, 2
- actions: subtract
```

A header is a select part when its only step is one `select`, written in any
of three forms:

| Written as | Selects |
| --- | --- |
| `- LayerName: Shop::Plate` and `- actions: select` | every object on that layer |
| `- select: Plate` | what the target names (see [Targets](#targets)) |
| `- actions: select` alone | the objects named after the header itself |

- With a `LayerName` and no target, it reads every object on exactly that
  layer, not on its sublayers, that a person could select: visible, unlocked,
  and on a visible, unlocked layer. That layer is never created; a missing
  layer, or one with nothing on it, is an error.
- A written target always wins over a `LayerName`, so a select part in an
  element whose defaults name a layer can still select by header.
- With neither, it selects by its own name: every object an earlier header of
  the run built under that name, or else every object in the model with that
  object name. List Modeling names each object after its header, so this finds
  what an earlier run built. Nothing found is an error.
- Any kind of object can be selected. Its closed solids, box and extrusion
  objects included, become kept solids of the element; everything found also
  joins the run's selection, so a sequence after it can act on it (see
  [Steps](#steps)).
- Dimensions, shape and position are not used; writing them gives an info
  line.
- Solids are edited in place. The result replaces each object and keeps its
  id, name, layer and color. An object the element does not change is left as
  it is, and so is one whose boolean fails. When a cut splits a solid in two,
  the second piece is added beside it with the same attributes.
- The run is still one undo step. Running the list again edits the solids
  again.
- For now a selected solid is always kept: `select, subtract` and
  `select, union` are errors, and so is `select` in a `BooleanUnion` or surface
  element. A `union` part in the same element merges into the selected solid.

## Steps

A header can hold **steps**: actions that run in order on what is selected,
the way a person works in Rhino by selecting objects, running a command and
typing the value it asks for. Such a header is a **sequence**. It builds
nothing from its own shape; its steps act on the selection.

```markdown
# Element: Boolean

## Guide

- shape: circle
- radius: 2
- actions: curve

## Guide Ring

- select: Guide
- actions: offset
- value: .5
```

`Guide` is a part that builds a circle. `Guide Ring` is a sequence: it selects
that circle and offsets it by .5, which adds a second circle of radius 2.5
named `Guide Ring`.

### Step words

Step words are written on `actions` lines, one word per line or several on one
line separated by commas. They match without regard to case, spaces, hyphens
and underscores, so `changeLayer`, `ChangeLayer` and `change layer` are one
word.

| Step | Other names | `value` |
| --- | --- | --- |
| `select` | | a target, optional (see [Targets](#targets)) |
| `deselect` | | a target |
| `deselectAll` | `selNone` | none |
| `offset` | | a distance |
| `extrude` | | a distance, optional when the header has a `depth` |
| `booleanDifference` | | a target: the solids to cut with |
| `copy` | | none |
| `paste` | | none |
| `changeLayer` | `setLayer` | a layer, written as for `LayerName` |
| `delete` | | none |

`difference` and `cut` already mean `subtract`, so `booleanDifference` has no
shorter name.

### `value`

A `value` line is the input of the nearest step above it under the same
header, the one Rhino would ask for when the command runs: a number, a layer
or the objects to use.

- `- select: Plate` is short for `- actions: select` followed by
  `- value: Plate`, and the same goes for `deselect`.
- Numbers take units and arithmetic like every other number.
- A `value` with no step above it, or under a step that takes none, produces a
  warning and is ignored.
- A step that needs a value and has none, a distance that cannot be read or is
  zero, and `depth: through` with an `extrude` step are errors.

### Sequences

A header with no deeper header under it is read from its own lines. It is a
sequence when they hold any of these:

- two or more `actions` lines with a value;
- a word that exists only as a step: every step word except `select` and
  `extrude`;
- a `deselect` line;
- a `select` line beside an `actions` line, or a second `select` line;
- a `value` under a step other than `select`.

Otherwise a lone `select` makes the header a select part (see
[Editing solids already in the model](#editing-solids-already-in-the-model)),
and anything else is a part, as always. A single `actions: extrude` line is
still an extruded part.

- Steps run where they are written and are never passed down. A `select`,
  `deselect` or `value` line or a step word on a container, in an element's
  defaults or under a group reference produces one warning and is ignored. A
  role such as `actions: subtract` on a container still reaches its parts.
- A sequence does not use a shape, dimensions or a position; writing them
  gives an info line. `depth` is read by `extrude`, and `orientation` by
  `extrude` and `offset`.
- A word that builds a part (`curve`, `pipe`, `subtract`, `union`,
  `surface`) cannot be a step in a sequence; it is an error.
- An action word that cannot be read produces a warning, with the closest
  known word when one is near (`deselctAll` suggests `deselectAll`). In a
  sequence it also stops the whole header, because skipping only that step
  would leave the selection in a state the list did not ask for.
- A sequence header written again with nothing under it runs the sequence
  again.

### The selection

- Each run keeps its own selection. It starts empty, whatever is selected in
  the viewport, so the same list always does the same thing.
- `select` adds to it, `deselect` takes out of it, and `deselectAll` empties
  it. A select part adds what it finds.
- It is emptied at every `# Element:` header, and so is what `copy`
  remembered. Inside an element it carries from header to header, so one
  header can select and the next one act.
- When a list used `select`, `deselect` or `deselectAll` anywhere, the
  viewport shows the run's selection when the run ends. A list without them
  leaves the viewport's selection alone.

An element first builds its parts and select parts, booleans included. Then
its sequences run, top to bottom. A sequence therefore sees the element's parts
in their final state, wherever it is written in the element, and cannot reach
an object that a later element builds.

### What each step does

| Step | Acts on | Effect | Selection after |
| --- | --- | --- | --- |
| `select`, `deselect`, `deselectAll` | | change the selection | as named |
| `offset` | selected curves | A new curve beside each, at the distance. The originals stay. | the new curves |
| `extrude` | selected closed planar curves | The curves become solids and are removed. A closed curve inside another makes a hole, so two circles give a ring. | the solids |
| `booleanDifference` | selected closed solids | Each is cut by the target's closed solids. A cut solid keeps its object id; the cutters are removed. | the cut solids |
| `copy` | the selection | Remembers it. Nothing in the model changes, and the computer's clipboard is not used. | unchanged |
| `paste` | what `copy` remembered | A copy of each object in the same place, with the same name, layer and attributes. | the copies |
| `changeLayer` | the selection | Moves it to the layer, creating the layer when needed; the header's `LayerColor` colors a layer created this way. | unchanged |
| `delete` | the selection | Removes it from the model. | empty |

- **Offset direction.** A closed curve grows for a positive distance and
  shrinks for a negative one. An open curve moves to its left for a positive
  distance, as seen from the side its orientation faces (the Top view for
  `top`), and to its right for a negative one. Corners stay sharp. A curve
  that is not planar is an error.
- **Where new objects go.** An offset curve takes the attributes of the curve
  it came from and the name of the step's header. An extruded solid takes the
  attributes of the first curve of its region. When the sequence has a
  `LayerName`, either one goes to that layer instead. New objects start
  outside the Rhino groups of the objects they came from.
- **Extrude.** The direction is the depth axis of the header's orientation when
  the curves' plane is perpendicular to it, otherwise the plane's normal; a
  negative distance goes the other way. The distance is the step's value, or
  the header's `depth`. An open curve, a curve that is not planar, and curves
  that touch or cross so that they enclose no region are errors. Rhino's own
  ExtrudeCrv keeps the curves; here they become the solid, as every other
  `extrude` in a list does.
- **Boolean difference.** Each selected solid is cut by the cutters whose
  bounding boxes reach it. An object that is both selected and a cutter is a
  cutter. A result in several pieces replaces the solid with the first piece
  and adds the others beside it. A cutter that reaches no selected solid stays
  in the model with a warning. A cut that fails is tried once more at a tenth
  of the model's absolute tolerance; when it fails again, nothing changes and
  the header stops with an error.
- **Objects a step cannot use.** A step uses the selected objects it can work
  with and warns once about the others (`offset needs curves; 2 selected
  objects that are not curves are left alone.`). With none it can work with,
  or with nothing selected, it is an error. The same goes for `copy`,
  `changeLayer` and `delete` with nothing selected, and for `paste` with
  nothing copied.
- **Stopping.** A step that reports an error stops its header. Steps already
  done stay done, and the rest of the list still runs.
- Steps do not give the thin-part warning: their solids have no dimensions in
  the list.

### What a header owns

The run keeps which objects belong to which header, so a later step can name
them:

- An object a part builds belongs to the part's name and to the name of every
  header above it, so a container's name reaches everything built under it. A
  grouped part also belongs to `group:<identifier>`.
- A select part owns what it found, without taking it from the header that
  built it.
- An offset curve belongs to the step's header. A pasted copy belongs to the
  headers of its original and comes after it, so `Stripe[0]` is the older one.
  An extruded solid belongs to the headers of the first curve of its region,
  and an extra piece of a cut to those of the solid it came from.
- An object replaced in place keeps its id, so nothing changes hands. A removed
  object leaves every header, the selection and what `copy` remembered.
- Within a header, objects are in the order they were made, which is what an
  index counts.

### A sequence in a list

[examples/inlaid-stripe.md](../examples/inlaid-stripe.md) inlays a stripe in
the flat top of a knob. Its parts build a filleted knob, a plain block for the
stripe, and one circle:

```markdown
## Trim

- LayerName: Shop::Guides

### Trim Inner

- shape: circle
- radius: 1.5
- z-position: 2
- actions: curve

### Trim Outer

- select: Trim Inner
- actions: offset
- value: 1
```

`Trim Outer` offsets the circle outward, so the container `Trim` now owns two
circles. The next sequence turns them into one ring:

```markdown
## Make Ring

- actions: deselectAll
- select: Trim
- actions: extrude
- depth: 2
```

The last one cuts the block round with the ring, keeps a copy of the result,
and cuts the knob with the original, which leaves a recess the copy fits into:

```markdown
## Inlay

- actions: deselectAll
- select: Stripe
- actions: booleanDifference
- value: Trim
- actions: copy
- actions: paste
- actions: deselectAll
- select: Knob
- actions: booleanDifference
- value: Stripe[0]
```

The curves, the ring and one copy of the stripe are used up, so the run adds
two objects: the knob with its recess and the stripe sitting in it.

## Targets

A target names objects. `select`, `deselect` and the value of
`booleanDifference` take one.

| Written as | Means |
| --- | --- |
| `Stripe` | every object the header `Stripe` owns; for a container, every object owned by the headers under it |
| `Stripe[1]` | one of them, counting from 0 in the order the list made them |
| `Stripe[*]` | the same as `Stripe` |
| `group:feet`, `group:feet[2]` | the objects of that group identifier, or one of them |
| `Shop::Plate` or `"Shop", "Plate"` | every object on that layer |

A target is looked up when the list runs, in this order, and the first place
that has anything wins:

1. the headers of this run, matched the way header names always are;
2. objects already in the model whose object name is that text;
3. a layer with that name or path, read like `LayerName`; a plain name finds a
   layer of that name wherever it sits.

- In the model only objects a person could select count: visible, unlocked,
  and on a visible, unlocked layer. Any kind of object counts, not only
  solids.
- A group target that the run did not build reads a Rhino group of that name
  in the model.
- Nothing found is an error that stops the header. So is an index past the
  end, and anything between the brackets other than `*` or one number.
- Objects found in the model by name or by layer come in Rhino's own order, so
  an index is dependable only for objects the same run built.
- A `select` without a value reads the layer of the nearest `LayerName` line
  above it under the same header, or failing that the `LayerName` the header
  inherits. With no `LayerName` at all, it selects by the header's own name.
  A `deselect` without a value reads the `LayerName` the same way, and with
  none it is an error.
- A target that is written always wins over a `LayerName`.
- A container is selected with `select: <its name>` from another header,
  because a container's name written again with lines under it changes the
  container's values (see [Reusing headers](#reusing-headers)).
- Running a list with steps a second time can reach what the first run built:
  a layer target finds everything on the layer, and a name that no header of
  the new run owns finds the objects of that name in the model.

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
- The curve part stays in the model. A sequence in the same element can delete
  it; sequences run after the element's booleans, so the groove is already
  cut:

```markdown
## Rail

- shape: circle
- radius: 3
- z-position: .5
- actions: curve

## Groove

- curve: Rail
- radius: .1
- actions: pipe, subtract

## Rail Cleanup

- select: Rail
- actions: delete
```

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
[examples/plate-with-feet.md](../examples/plate-with-feet.md) cuts pockets, a
recess and through holes into a plate with shared values, then builds the feet
again from the same headers as solids on a layer of their own.
[examples/inlaid-stripe.md](../examples/inlaid-stripe.md) uses steps to cut a
stripe round with a ring, copy it, and inlay it in the top of a knob.

A longer walk-through of writing a parts list, with small standalone scripts
that build one in Rhino and in FreeCAD:
[Model From a Parts List](https://gist.github.com/jhauga/615902a81b4200e557ca62bb8e99f7ea).

## Diagnostics

The parser and the runner report three levels, and the level decides whether the
part is built:

- **Error**: the part cannot be built correctly, so it is skipped. A missing
  dimension, an unreadable number anywhere in the part, arithmetic that is not a
  length, a zero dimension or a negative height or width, a pipe without its
  curve or radius, a curve name that cannot be found, `depth: through` on a part
  that does not cut or with nothing to cut through, a `select` part that finds
  nothing or has a role, and a group reference that cannot be followed are
  errors. So are a step that cannot be read (a missing or unreadable value, a
  zero distance, `depth: through` with `extrude`, a word that builds a part, or
  any action word that cannot be read in a sequence), a target that names
  nothing or an index past its end, a step with nothing selected that it can
  work with, and a cut that fails twice; any of them stops its header. The rest
  of the list still runs.
- **Warning**: a value could not be read but resolves to a documented default,
  so the part is still built. An unknown color, orientation, shape or action, a
  position with the wrong number of values, extra action words, a square whose
  height differs from its width, a fillet that could not be made, a pipe close
  to the model tolerance, a solid in a boolean thinner than about twice the
  model tolerance, a cutter that reaches none of the kept solids, a boolean that
  failed and left its parts behind, and a planned element type are warnings.
  So are a `value` with no step above it or under a step that takes none,
  steps written on a container, in an element's defaults or under a group
  reference, selected objects of a kind a step cannot use, and a step's cutter
  that reaches no selected solid. An unknown action word names the closest
  known word when one is near.
- **Info**: ignored lines, assumed defaults, a repeated container whose lines
  only change its values, a part mentioned twice in one element, and a shape,
  dimensions or position written on a sequence, which steps do not use.

Booleans with a solid about as thin as the model's absolute tolerance can fail,
or worse, succeed with the wrong shape. When the run warns about one, a smaller
absolute tolerance in Document Properties solves it.

All three print to the command line. When a person triggered the run, the
completion popup repeats the warnings and errors.
