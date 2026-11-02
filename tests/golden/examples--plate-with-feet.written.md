# Element: BooleanDifference

## Plate

- LayerName: "Shop", "Plate"
- LayerColor: Steel Blue
- orientation: top
- height: 6
- width:  10
- depth:  1
- position: 0, 0
- actions: extrude

## Foot Pocket

### group:feet

- orientation: top
- shape: square
- width:  1
- depth:  0.25
- position: -4, -2
- actions: subtract

### group:feet

- orientation: top
- shape: square
- width:  1
- depth:  0.25
- position: 4, -2
- actions: subtract

### group:feet

- orientation: top
- shape: square
- width:  1
- depth:  0.25
- position: -4, 2
- actions: subtract

### group:feet

- orientation: top
- shape: square
- width:  1
- depth:  0.25
- position: 4, 2
- actions: subtract

## Label Recess

- orientation: top
- height: 2
- width:  4
- depth:  -0.1
- position: 0, 0
- offset: 1
- actions: subtract

## Screw Holes

### group:screws

- orientation: top
- shape: circle
- radius: 0.2
- depth:  through
- position: -4, -2
- actions: subtract

### group:screws

- orientation: top
- shape: circle
- radius: 0.2
- depth:  through
- position: 4, -2
- actions: subtract

### group:screws

- orientation: top
- shape: circle
- radius: 0.2
- depth:  through
- position: -4, 2
- actions: subtract

### group:screws

- orientation: top
- shape: circle
- radius: 0.2
- depth:  through
- position: 4, 2
- actions: subtract

# Element: BooleanDifference

## Foot Pocket

### group:feet

- LayerName: "Shop", "Feet"
- LayerColor: Black
- orientation: top
- shape: square
- width:  1
- depth:  0.25
- position: -4, -2
- actions: extrude

### group:feet

- LayerName: "Shop", "Feet"
- LayerColor: Black
- orientation: top
- shape: square
- width:  1
- depth:  0.25
- position: 4, -2
- actions: extrude

### group:feet

- LayerName: "Shop", "Feet"
- LayerColor: Black
- orientation: top
- shape: square
- width:  1
- depth:  0.25
- position: -4, 2
- actions: extrude

### group:feet

- LayerName: "Shop", "Feet"
- LayerColor: Black
- orientation: top
- shape: square
- width:  1
- depth:  0.25
- position: 4, 2
- actions: extrude

## Screw Holes

### group:screws

- LayerName: "Shop", "Feet"
- LayerColor: Black
- orientation: top
- shape: circle
- radius: 0.2
- depth:  through
- position: -4, -2
- actions: subtract

### group:screws

- LayerName: "Shop", "Feet"
- LayerColor: Black
- orientation: top
- shape: circle
- radius: 0.2
- depth:  through
- position: 4, -2
- actions: subtract

### group:screws

- LayerName: "Shop", "Feet"
- LayerColor: Black
- orientation: top
- shape: circle
- radius: 0.2
- depth:  through
- position: -4, 2
- actions: subtract

### group:screws

- LayerName: "Shop", "Feet"
- LayerColor: Black
- orientation: top
- shape: circle
- radius: 0.2
- depth:  through
- position: 4, 2
- actions: subtract
