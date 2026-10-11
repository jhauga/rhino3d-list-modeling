<!-- Expressions evaluate before units convert. The inch document makes
     every part here a one inch cube at a different position. -->

# Element: Boolean

## Divide

- orientation: top
- height: 2/2
- width: 4/4
- depth: .5*2
- position: 400, 0
- actions: extrude

## Units Inside

- orientation: top
- height: 2in/2
- width: (12.7 + 12.7)mm
- depth: 25.4mm * 1
- position: 405, 0
- actions: extrude

## Parentheses And Negatives

- orientation: top
- height: (3 - 1) / 2
- width: -(-1)
- depth: 1
- position: 410, -(2 - 2)
- actions: extrude
