# Element: Boolean

## Knob

- LayerName: "Shop", "Knob"
- LayerColor: Silver
- orientation: top
- shape: circle
- radius: 2
- depth:  3
- position: 0, 0
- filletEdge:
  - select: 1
  - radius: 0.25
- actions: extrude

## Stripe

- LayerName: "Shop", "Stripe"
- LayerColor: Dark Red
- orientation: top
- height: 1.2
- width:  2.4
- depth:  0.1
- position: 0, -1.2
- offset: 2.9
- actions: extrude

## Trim Inner

- LayerName: "Shop", "Guides"
- orientation: top
- shape: circle
- radius: 1.5
- position: 0, 0
- offset: 2
- actions: curve

## Trim Outer

- LayerName: "Shop", "Guides"
- select: Trim Inner
- actions: offset
- value: 1

## Make Ring

- actions: deselectAll
- select: Trim
- actions: extrude
- value: 2

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
