<!-- Repeated headers and group references. The second element rebuilds
     parts from the first: its offset and layer come from its own defaults,
     the depth from the update of Blocks, and one width from a site line. -->

# Element: Boolean

- LayerName: Test_References::first

## Blocks

- orientation: top
- height: 1
- width: 1
- depth: 1

### Block A

- position: 800, 0

### Corner

#### group:corners

- position: 805, 0

#### group:corners

- position: 805, 5

#### group:corners

- position: 805, 10

## Peg

- shape: circle
- radius: .25
- depth: 2
- position: 810, 0

# Element: Boolean

- LayerName: Test_References::second
- offset: 10

## Blocks

- depth: 2

## Rebuilt

### Block A

### Corner

#### group:corners[1]

## Peg

## Picked

### group:corners[2]

- width: 2
