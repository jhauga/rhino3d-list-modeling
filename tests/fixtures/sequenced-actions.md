<!-- A filleted cylinder with a groove cut by a pipe used as a cutter.
     The groove pipe sits 0.14 to 0.16 from the axis, so the cylinder
     radius of .31/2 (0.155) lets it cut 0.015 into the wall. Runs at an
     absolute tolerance of 0.001; the pipe is only 0.01 thick. -->

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
- filletEdge:
  - select: 1
  - radius: .02
- radius: .31/2
- depth:  0.39
- position: 3.0033, 2.3069
- offset: 0.04
- actions: extrude

## pipe-subtract

- orientation: top
- curve: Curve_3
- radius: .01
- actions: pipe, subtract
