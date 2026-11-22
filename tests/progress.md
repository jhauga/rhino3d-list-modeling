<!-----------------------------------------------------------------------------
--- Progress ------------------------------------------------------------------

Markdown file that will build the entirety of progress.3dm, the goal of
which is an electronic component module:
  - Buck Boost Converter Module - 1.25V-30V 3A.
Run on an empty inch document with an absolute tolerance of 0.001.
-------------------------------------------------------------------------------
------------------------------------------------------------------------------>
# Element: BooleanDifference

## Circuit Board

- LayerName: Model - Buck Boost Converter Module - 1.25V-30V 3A::Circuit Board::board
- LayerColor: #0000bf
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
- LayerColor: #e6e6e6

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

# Element: BooleanDifference

## Input Capacitor Shell

- LayerName: Model - Buck Boost Converter Module - 1.25V-30V 3A::capacitors::Input Capacitor (C inp.)::shell
- LayerColor: #bebebe
- orientation: top
- shape: circle
- radius: 0.15
- depth: 0.39
- position: -0.65, 0.04
- offset: 0.05
- filletEdge:
  - select: 1
  - radius: 0.02
- actions: extrude

## Input Capacitor Groove Path

- LayerName: Default
- shape: circle
- radius: 0.15
- position: -0.65, 0.04
- offset: 0.08
- actions: curve

## Input Capacitor Groove

- curve: Input Capacitor Groove Path
- radius: 0.005
- actions: pipe, subtract

## Input Capacitor Groove Cleanup

- select: Input Capacitor Groove Path
- actions: delete

# Element: Boolean

## Input Capacitor Subtract Paint

- LayerName: Model - Buck Boost Converter Module - 1.25V-30V 3A::capacitors::Input Capacitor (C inp.)::paint
- LayerColor: #393434
- orientation: top
- height: 0.1
- width:  0.3
- depth:  0.001
- position: -0.65, -0.075
- offset: 0.439
- actions: extrude

## Input Capacitor Round Subtract Paint

### Input Extrude Cut Curve 1

- LayerName: Default
- orientation: top
- shape: circle
- radius: 0.13
- position: -0.65, 0.04
- offset: 0.34
- actions: curve

### Input Extrude Cut Curve 2

- select: Input Extrude Cut Curve 1
- actions: offset
- value: 0.2

## Input Capacitor Paint Layers

- actions: deselectAll
- select: Input Capacitor Round Subtract Paint
- actions: extrude
- depth: .2

## Input Capacitor Paint

- actions: deselectAll
- select: Input Capacitor Subtract Paint
- actions: booleanDifference
- value: Input Capacitor Round Subtract Paint
- actions: copy
- actions: paste
- actions: deselectAll
- LayerName: Model - Buck Boost Converter Module - 1.25V-30V 3A::capacitors::Input Capacitor (C inp.)::shell
- actions: select
- actions: booleanDifference
- value: Input Capacitor Subtract Paint[0]
- actions: deselectAll
- select: Input Capacitor Subtract Paint
- actions: changeLayer
- value: Model - Buck Boost Converter Module - 1.25V-30V 3A::capacitors::Input Capacitor (C inp.)::paint

# Element: BooleanDifference

## Output Capacitor Shell

- LayerName: Model - Buck Boost Converter Module - 1.25V-30V 3A::capacitors::Output Capacitor (C out.)::shell
- LayerColor: #bebebe
- orientation: top
- shape: circle
- radius: 0.155
- depth: 0.39
- position: 0.65, 0.04
- offset: 0.05
- filletEdge:
  - select: 1
  - radius: 0.02
- actions: extrude

## Output Capacitor Groove Path

- LayerName: Default
- shape: circle
- radius: 0.155
- position: 0.65, 0.04
- offset: 0.075
- actions: curve

## Output Capacitor Groove

- curve: Output Capacitor Groove Path
- radius: 0.005
- actions: pipe, subtract

## Output Capacitor Groove Cleanup

- select: Output Capacitor Groove Path
- actions: delete

# Element: Boolean

## Output Capacitor Subtract Paint

- LayerName: Model - Buck Boost Converter Module - 1.25V-30V 3A::capacitors::Output Capacitor (C out.)::paint
- LayerColor: #221919
- orientation: top
- height: 0.08
- width:  0.2252
- depth:  0.001
- position: 0.65, -0.07
- offset: 0.439
- actions: extrude

## Output Capacitor Round Subtract Paint

### Output Extrude Cut Curve 1

- LayerName: Default
- orientation: top
- shape: circle
- radius: 0.135
- position: 0.65, 0.04
- offset: 0.34
- actions: curve

### Output Extrude Cut Curve 2

- select: Output Extrude Cut Curve 1
- actions: offset
- value: 0.2

## Output Capacitor Paint Layers

- actions: deselectAll
- select: Output Capacitor Round Subtract Paint
- actions: extrude
- depth: .2

## Output Capacitor Paint

- actions: deselectAll
- select: Output Capacitor Subtract Paint
- actions: booleanDifference
- value: Output Capacitor Round Subtract Paint
- actions: copy
- actions: paste
- actions: deselectAll
- LayerName: Model - Buck Boost Converter Module - 1.25V-30V 3A::capacitors::Output Capacitor (C out.)::shell
- actions: select
- actions: booleanDifference
- value: Output Capacitor Subtract Paint[0]
- actions: deselectAll
- select: Output Capacitor Subtract Paint
- actions: changeLayer
- value: Model - Buck Boost Converter Module - 1.25V-30V 3A::capacitors::Output Capacitor (C out.)::paint
