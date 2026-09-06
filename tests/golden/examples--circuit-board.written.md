# Element: BooleanDifference

## Circuit Board

* LayerName: Circuit_Board
* LayerColor: Dark Blue
* orientation: top
* height: 0.84in
* width:  1.68in
* depth:  0.04in
* position: 0, 0
* actions: extrude

## Mount Holes

### group:holes

* orientation: top
* shape: circle
* radius: 0.05in
* depth:  1in
* position: 0.62, -0.35
* actions: subtract

### group:holes

* orientation: top
* shape: circle
* radius: 0.05in
* depth:  1in
* position: -0.62, 0.35
* actions: subtract

# Element: Boolean

## Box

* LayerName: Shipping_Box
* LayerColor: Brown
* orientation: top
* height: 1in
* width:  2in
* depth:  0.25in
* position: 2, 2
* actions: extrude
