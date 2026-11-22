<!-- Steps: a stripe inlaid in the flat top of a knob. The stripe blank is cut
     round by a ring made from a circle and its offset, copied, cut into the
     knob with one copy, and the other copy is moved to its own layer. A scrap
     curve is deleted. -->

# Element: Boolean

## Knob

- LayerName: Test_Steps::knob
- shape: circle
- radius: 2
- depth: 3
- position: 1100, 0
- filletEdge:
  - select: 1
  - radius: .25
- actions: extrude

## Stripe

- LayerName: Test_Steps::blank
- width: 2.4
- height: 1.2
- depth: .1
- position: 1100, -1.2
- z-position: 2.9
- actions: extrude

## Trim

- LayerName: Test_Steps::guides

### Trim Inner

- shape: circle
- radius: 1.5
- position: 1100, 0
- z-position: 2
- actions: curve

### Trim Outer

- select: Trim Inner
- actions: offset
- value: 1

## Scrap

- LayerName: Test_Steps::guides
- shape: circle
- radius: .5
- position: 1100, 6
- actions: curve

## Make Ring

- actions: deselectAll
- select: Trim
- actions: extrude
- depth: 2

## Inlay

- actions: deselectAll
- select: Stripe
- actions: booleanDifference
- value: Trim
- actions: copy
- actions: paste
- actions: deselectAll
- LayerName: Test_Steps::knob
- actions: select
- actions: booleanDifference
- value: Stripe[0]
- actions: deselectAll
- select: Stripe
- actions: changeLayer
- value: Test_Steps::inlay

## Tidy

- actions: deselectAll
- select: Scrap
- actions: delete
