<!-- Pipes along rail curves, with the three nested layer forms. -->

# Element: Boolean

## Curve 1

- LayerName: Pipe
  - simple
- orientation: top
- shape: circle
- radius: 1
- position: 3, 0
- actions: curve

## pipe-simple

- LayerName: "Pipe", "simple"
- orientation: top
- curve: Curve 1
- radius: .125
- actions: pipe

## Curve 2

- LayerName: Pipe::multiple
- orientation: top
- shape: circle
- radius: 0.5
- position: 6, 0
- actions: curve

## pipe-multi-radius

- LayerName: "Pipe", "multiple"
- orientation: top
- curve: Curve 2
- radius:
  - 0: .125
  - 2: .06
  - 4: .1
- actions: pipe
