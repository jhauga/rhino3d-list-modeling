<!-- A base plate with a rounded post standing in it and a groove cut around
     the post. The post merges into the plate, so the result is one solid named
     after the plate, plus the guide curve the groove follows. -->

# Element: Boolean

## Groove Path

- LayerName: Assembly
  - Guides
- shape: circle
- radius: .5in
- position: 0, 0
- offset: 1.5in
- actions: curve

## Base Plate

- LayerName: "Assembly", "Parts"
- LayerColor: Steel Blue
- orientation: top
- height: 3in
- width:  3in
- depth:  .25in
- position: 0, 0
- filletEdge:
  - select: 1
  - radius: .125in
- actions: extrude

## Post

- LayerName: Assembly::Parts
- shape: circle
- radius: 1in/2
- depth:  2.5in
- position: 0, 0
- offset: .125in
- filletEdge:
  - select: 1
  - radius: .1in
- actions: union

## Groove

- curve: Groove Path
- radius: .0625in
- actions: pipe, subtract
