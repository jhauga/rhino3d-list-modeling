<!-- Fillet edge loops on cylinders and boxes. The models under
     .support/3d-examples/ show the expected topology, not exact sizes. -->

# Element: Boolean

## filletCircle-TopEdge

- orientation: top
- shape: circle
- radius: .31/2
- distance: .39
- position: -.65, -.04, .04
- filletEdge:
  - select: 1
  - radius: .02
- actions: extrude

## filletCircle-BothEdge

- orientation: top
- shape: circle
- radius: 0.13
- depth:  0.39
- position: -0.65, -2.0826
- offset: 0.04
- filletEdge:
  - select: 0,1
  - radius: .1, .02
- actions: extrude

## filletCircle-BothEqual

- orientation: top
- shape: circle
- filletEdge:
  - select: 0,1
  - radius: .02
- radius: 0.13
- depth:  0.39
- position: 0.6527, -1.9938
- offset: 0.04
- actions: extrude

## filletBox-topEdge

- LayerName: Default
- LayerColor: Black
- orientation: top
- height: 1.446
- width:  2.4518
- distance:  1.2635
- position: -0.2099, 0.723
- filletEdge:
  - select: 1
  - radius: [.05,.05,.1,.02]
- actions: extrude

## Fillet Box - Both Edge

- orientation: front
- height: 1.446
- width:  2.4518
- depth:  1.2635
- position: 0, 0.723
- filletEdge:
  - select: 0,1
  - radius: [.1, .2], [.3, .15, .07, .03]
- actions: extrude

## filletBox-allEdges

- orientation: top
- distance: 3.298
- width:  1.9752
- height:  1.3675
- position: -0.3878, -1.649
- filletEdge:
  - select: top
    - select: 0,1
    - radius: .1, .2
  - select: front
    - select: 0,1
    - radius: [.01, .05], [.1]
- actions: extrude
