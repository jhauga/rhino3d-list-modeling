<!-- Every way to select and deselect. Each sequence ends by moving what is
     selected to a layer named after the way it was selected, so the layers
     show which cubes each one reached. -->

# Element: Boolean

- LayerName: Test_Selections::start
- width: 1
- height: 1
- depth: 1

## Solo

- position: 1200, 0

## Pair

### Pair Left

- position: 1205, 0

### Pair Right

- position: 1210, 0

## Row

### group:row

- position: 1215, 0

### group:row

- position: 1220, 0

### group:row

- position: 1225, 0

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
