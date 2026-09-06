# Element: BooleanDifference

## Drilled Board

* LayerName: Test_Board
* LayerColor: Dark Blue
* orientation: top
* height: 10
* width:  20
* depth:  2
* position: 0, 0
* actions: extrude

## Bolt Holes

### group:bolts

* orientation: top
* shape: circle
* radius: 1.5
* depth:  10
* position: 6, 0
* offset: -4
* actions: subtract

### group:bolts

* orientation: top
* shape: circle
* radius: 1.5
* depth:  10
* position: -6, 0
* offset: -4
* actions: subtract

# Element: BooleanUnion

## Union Left

* LayerName: Test_Union
* LayerColor: Brown
* orientation: top
* height: 10
* width:  10
* depth:  5
* position: 40, 0
* actions: extrude

## Union Right

* LayerName: Test_Union
* orientation: top
* height: 10
* width:  10
* depth:  5
* position: 46, 0
* actions: extrude
