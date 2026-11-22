# Element: Boolean

## Knob

- LayerName: "Test_Steps", "knob"
- orientation: top
- shape: circle
- radius: 2
- depth:  3
- position: 1100, 0
- filletEdge:
  - select: 1
  - radius: 0.25
- actions: extrude

## Stripe

- LayerName: "Test_Steps", "blank"
- orientation: top
- height: 1.2
- width:  2.4
- depth:  0.1
- position: 1100, -1.2
- offset: 2.9
- actions: extrude

## Trim Inner

- LayerName: "Test_Steps", "guides"
- orientation: top
- shape: circle
- radius: 1.5
- position: 1100, 0
- offset: 2
- actions: curve

## Trim Outer

- LayerName: "Test_Steps", "guides"
- select: Trim Inner
- actions: offset
- value: 1

## Scrap

- LayerName: "Test_Steps", "guides"
- orientation: top
- shape: circle
- radius: 0.5
- position: 1100, 6
- actions: curve

## Make Ring

- actions: deselectAll
- select: Trim
- actions: extrude
- value: 2

## Inlay

- LayerName: "Test_Steps", "knob"
- actions: deselectAll
- select: Stripe
- actions: booleanDifference
- value: Trim
- actions: copy
- actions: paste
- actions: deselectAll
- select: Test_Steps::knob
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
