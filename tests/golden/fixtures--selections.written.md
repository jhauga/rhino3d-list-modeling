# Element: Boolean

## Solo

- LayerName: "Test_Selections", "start"
- orientation: top
- height: 1
- width:  1
- depth:  1
- position: 1200, 0
- actions: extrude

## Pair Left

- LayerName: "Test_Selections", "start"
- orientation: top
- height: 1
- width:  1
- depth:  1
- position: 1205, 0
- actions: extrude

## Pair Right

- LayerName: "Test_Selections", "start"
- orientation: top
- height: 1
- width:  1
- depth:  1
- position: 1210, 0
- actions: extrude

## Row

### group:row

- LayerName: "Test_Selections", "start"
- orientation: top
- height: 1
- width:  1
- depth:  1
- position: 1215, 0
- actions: extrude

### group:row

- LayerName: "Test_Selections", "start"
- orientation: top
- height: 1
- width:  1
- depth:  1
- position: 1220, 0
- actions: extrude

### group:row

- LayerName: "Test_Selections", "start"
- orientation: top
- height: 1
- width:  1
- depth:  1
- position: 1225, 0
- actions: extrude

# Element: Boolean

## By Header

- select: Solo
- actions: changeLayer
- value: Test_Selections::by-header

## By Container

- actions: deselectAll
- select: Pair
- actions: changeLayer
- value: Test_Selections::by-container

## By Index

- actions: deselectAll
- select: group:row[1]
- actions: changeLayer
- value: Test_Selections::by-index

## By Layer

- actions: deselectAll
- select: Test_Selections::start
- actions: changeLayer
- value: Test_Selections::by-layer

## Kept

- actions: deselectAll
- select: Row
- select: Pair
- deselect: Pair Left
- deselect: Test_Selections::by-index
- actions: changeLayer
- value: Test_Selections::kept

# Element: Boolean

## Solo

- actions: select

## Own Name

- actions: changeLayer
- value: Test_Selections::own-name
