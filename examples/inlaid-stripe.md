<!-- A knob with a stripe inlaid in its flat top. The stripe starts as a
     plain block, is cut round by a ring made from a circle and its offset,
     and is then copied: one copy cuts its own recess into the knob, the
     other stays as the stripe. -->

# Element: Boolean

## Knob

- LayerName: Shop::Knob
- LayerColor: Silver
- shape: circle
- radius: 2
- depth: 3
- filletEdge:
  - select: 1
  - radius: .25
- actions: extrude

## Stripe

- LayerName: Shop::Stripe
- LayerColor: Dark Red
- width: 2.4
- height: 1.2
- depth: .1
- position: 0, -1.2
- z-position: 2.9
- actions: extrude

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
- select: Knob
- actions: booleanDifference
- value: Stripe[0]
