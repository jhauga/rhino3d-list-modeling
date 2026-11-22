<!-----------------------------------------------------------------------------
--- Progress ------------------------------------------------------------------

Markdown file that will build the entirety of progress.3dm, the goal of
which is an electronic component module:
  - Buck Boost Converter Module - 1.25V-30V 3A.
-------------------------------------------------------------------------------
------------------------------------------------------------------------------>
# Element: BooleanDifference

## Circuit Board

- LayerName: Model - Buck Boost Converter Module - 1.25V-30V 3A
- LayerColor: Dark Blue
- orientation: top
- height: 0.84in
- width:  1.68in
- depth:  0.04in
- position: 0, 0
- actions: extrude

## Mount Holes

### group:holes

- orientation: top
- shape: circle
- radius: 0.05in
- depth:  1in
- position: 0.62, -0.35
- actions: subtract

### group:holes

- orientation: top
- shape: circle
- radius: 0.05in
- depth:  1in
- position: -0.62, 0.35
- actions: subtract

# Element: Boolean

## Box

- LayerName: Shipping_Box
- LayerColor: Brown
- orientation: top
- height: 1in
- width:  2in
- depth:  0.25in
- position: 2, 2
- actions: extrude

# Element: BooleanDifference

## Circuit Board

- LayerName: Model - Buck Boost Converter Module - 1.25V-30V 3A::Circuit Board::board
- actions: select

## Pin Connections

- actions: subtract

### Pin Top Connection

- orientation: top
- shape: square
- width: .1
- depth: -.004
- z-position: .04

#### group:pin-top-connections

- position: -.79, .37

#### group:pin-top-connections

- position: .79, .37

#### group:pin-top-connections

- position: -.79, -.37

#### group:pin-top-connections

- position: .79, -.37

### Pin Bottom Connection

- orientation: top
- shape: square
- width: .1
- depth: .004
- z-position: 0

#### group:pin-bottom-connections

- position: -.79, .37

#### group:pin-bottom-connections

- position: .79, .37

#### group:pin-bottom-connections

- position: -.79, -.37

#### group:pin-bottom-connections

- position: .79, -.37

### Pin Holes

- shape: circle
- radius: .015
- depth: through
- z-position: .04

#### group:pin-holes

- position: -.79, .37

#### group:pin-holes

- position: .79, .37

#### group:pin-holes

- position: -.79, -.37

#### group:pin-holes

- position: .79, -.37

# Element: BooleanDifference

## Pin Connections

- actions:
- LayerName: Model - Buck Boost Converter Module - 1.25V-30V 3A::Circuit Board::circuit_connections

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
