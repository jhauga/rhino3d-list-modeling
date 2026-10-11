# Element: Boolean

## Groove Path

- LayerName: "Assembly", "Guides"
- orientation: top
- shape: circle
- radius: 0.5in
- position: 0, 0
- offset: 1.5in
- actions: curve

## Base Plate

- LayerName: "Assembly", "Parts"
- LayerColor: Steel Blue
- orientation: top
- height: 3in
- width:  3in
- depth:  0.25in
- position: 0, 0
- filletEdge:
  - select: 1
  - radius: 0.125in
- actions: extrude

## Post

- LayerName: "Assembly", "Parts"
- orientation: top
- shape: circle
- radius: 0.5in
- depth:  2.5in
- position: 0, 0
- offset: 0.125in
- filletEdge:
  - select: 1
  - radius: 0.1in
- actions: union

## Groove

- orientation: top
- curve: Groove Path
- radius: 0.0625in
- actions: pipe, subtract
