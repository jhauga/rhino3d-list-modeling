# Element: Boolean

## filletCircle-TopEdge

- orientation: top
- shape: circle
- radius: 0.155
- depth:  0.39
- position: -0.65, -0.04
- offset: 0.04
- filletEdge:
  - select: 1
  - radius: 0.02
- actions: extrude

## filletCircle-BothEdge

- orientation: top
- shape: circle
- radius: 0.13
- depth:  0.39
- position: -0.65, -2.0826
- offset: 0.04
- filletEdge:
  - select: 0, 1
  - radius: 0.1, 0.02
- actions: extrude

## filletCircle-BothEqual

- orientation: top
- shape: circle
- radius: 0.13
- depth:  0.39
- position: 0.6527, -1.9938
- offset: 0.04
- filletEdge:
  - select: 0, 1
  - radius: 0.02
- actions: extrude

## filletBox-topEdge

- LayerName: Default
- LayerColor: Black
- orientation: top
- height: 1.446
- width:  2.4518
- depth:  1.2635
- position: -0.2099, 0.723
- filletEdge:
  - select: 1
  - radius: [0.05, 0.05, 0.1, 0.02]
- actions: extrude

## Fillet Box - Both Edge

- orientation: front
- height: 1.446
- width:  2.4518
- depth:  1.2635
- position: 0, 0.723
- filletEdge:
  - select: 0, 1
  - radius: [0.1, 0.2], [0.3, 0.15, 0.07, 0.03]
- actions: extrude

## filletBox-allEdges

- orientation: top
- height: 1.3675
- width:  1.9752
- depth:  3.298
- position: -0.3878, -1.649
- filletEdge:
  - select: top
    - select: 0, 1
    - radius: 0.1, 0.2
  - select: front
    - select: 0, 1
    - radius: [0.01, 0.05], 0.1
- actions: extrude
