# Element: BooleanDifference

## Plate

- LayerName: Shop::Plate
- LayerColor: Steel Blue
- width: 10
- height: 6
- depth: 1

## Pockets

- actions: subtract
- shape: square
- width: 1

### Foot Pocket

- z-position: 0
- depth: .25

#### group:feet

- position: -4, -2

#### group:feet

- position: 4, -2

#### group:feet

- position: -4, 2

#### group:feet

- position: 4, 2

### Label Recess

- shape: rectangle
- width: 4
- height: 2
- z-position: 1
- depth: -.1

### Screw Holes

- shape: circle
- radius: .2
- depth: through

#### group:screws

- position: -4, -2

#### group:screws

- position: 4, -2

#### group:screws

- position: -4, 2

#### group:screws

- position: 4, 2

# Element: BooleanDifference

## Pockets

- actions:
- LayerName: Shop::Feet
- LayerColor: Black

## Feet

- actions: extrude

### Foot Pocket

#### group:feet[*]

## Screws Through Feet

- actions: subtract

### Screw Holes

#### group:screws[*]
