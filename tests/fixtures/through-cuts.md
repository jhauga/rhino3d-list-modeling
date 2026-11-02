<!-- depth: through cuts the full extent of the element's kept solids along
     the cutter's depth axis, whatever the cutter's own position on it. The
     slot is clear of the round hole and of the plate edge, so no face is
     coplanar or tangent. -->

# Element: BooleanDifference

## Stack Lower

- orientation: top
- height: 4
- width: 4
- depth: 1
- position: 700, 0

## Stack Upper

- orientation: top
- height: 4
- width: 4
- depth: 1
- position: 700, 0
- z-position: 3

## Through Both

- shape: circle
- radius: .5
- depth: through
- position: 700, 0
- z-position: 10
- actions: subtract

## Side Slot

- orientation: front
- width: 1
- height: .5
- depth: through
- x-position: 701.25
- z-position: .5
- actions: subtract
