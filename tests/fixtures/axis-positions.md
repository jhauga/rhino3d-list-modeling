<!-- x-, y- and z-position set one world coordinate whatever the orientation;
     the line nearest the part wins; a negative depth extrudes the other way;
     a square takes one side. Every part is a 1 by 1 by 1 cube. -->

# Element: Boolean

## Top Lifted

- orientation: top
- shape: square
- width: 1
- depth: 1
- position: 600, 0
- z-position: 5

## Bottom Lifted

- orientation: bottom
- shape: square
- height: 1
- depth: 1
- position: 605, 0
- z-position: 5

## Front Pushed

- orientation: front
- shape: square
- width: 1
- depth: 1
- x-position: 610
- y-position: 3
- z-position: 2

## Back Pushed

- orientation: back
- shape: square
- width: 1
- depth: 1
- x-position: 615
- y-position: 3
- z-position: 2

## Right Pushed

- orientation: right
- shape: square
- width: 1
- depth: 1
- x-position: 620
- y-position: 0
- z-position: 2

## Left Pushed

- orientation: left
- shape: square
- width: 1
- depth: 1
- x-position: 625
- y-position: 0
- z-position: 2

## Negative Depth

- orientation: top
- shape: square
- width: 1
- depth: -1
- position: 630, 0
- z-position: 1

## Nearest Wins

- orientation: top
- shape: square
- width: 1
- depth: 1
- z-position: 9

### Override By Position

- position: 635, 0, 2

### Later Line Wins

- position: 640, 0, 2
- z-position: 4

### Cleared

- position: 645, 0
- z-position:
