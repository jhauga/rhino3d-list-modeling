# Element: Boolean

## Curve_3

- LayerName: "Capacitor", "filletEdge-pipe_subtract"
- orientation: top
- shape: circle
- radius: 0.15
- position: 3.0033, 2.3069
- offset: 0.1328
- actions: curve

# Element: Boolean

## combo_fillet,pipe-subtract

- LayerName: "Capacitor", "filletEdge-pipe_subtract"
- LayerColor: Black
- orientation: top
- shape: circle
- radius: 0.155
- depth:  0.39
- position: 3.0033, 2.3069
- offset: 0.04
- filletEdge:
  - select: 1
  - radius: 0.02
- actions: extrude

## pipe-subtract

- orientation: top
- curve: Curve_3
- radius: 0.01
- actions: pipe, subtract
