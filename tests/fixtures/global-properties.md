<!-- The global properties case. Changed from the original: generic layer
     names, the second element type spelled correctly, and four pin holes,
     one under each pair of connections. The board is not in the list: the
     in-Rhino case adds a 2 by 1 by .1 box centered on the origin to the board
     layer before the run. -->

# Element: BooleanDifference

## Circuit Board

- LayerName: Sample Module - 5V-12V 2A::Circuit Board::board
- actions: select

## Pin Connections

- actions: subtract

### Pin Top Connection

- orientation: top
- shape: square
- width: .1
- depth: -.004
- z-position: .05

#### group:pin-top-connections

- position: -.8, .35

#### group:pin-top-connections

- position: .8, .35

#### group:pin-top-connections

- position: -.8, -.35

#### group:pin-top-connections

- position: .8, -.35

### Pin Bottom Connection

- orientation: top
- shape: square
- width: .1
- depth: .004
- z-position: -.05

#### group:pin-bottom-connections

- position: -.8, .35

#### group:pin-bottom-connections

- position: .8, .35

#### group:pin-bottom-connections

- position: -.8, -.35

#### group:pin-bottom-connections

- position: .8, -.35

### Pin Holes

- shape: circle
- radius: .015
- depth: through
- z-position: .05

#### group:pin-holes

- position: -.8, .35

#### group:pin-holes

- position: .8, .35

#### group:pin-holes

- position: -.8, -.35

#### group:pin-holes

- position: .8, -.35

# Element: BooleanDifference

## Pin Connections

- actions:
- LayerName: Sample Module - 5V-12V 2A::Circuit Board::circuit_connections

## Pin Circuits

- actions: extrude

### Pin Top Connection

### Pin Bottom Connection

#### group:pin-top-connections[*]

#### group:pin-bottom-connections[*]

## Pin Through Holes

- actions: subtract

### Pin Holes

#### group:pin-holes[*]
