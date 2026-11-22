# Element: Sculpt

## Missing Depth

* LayerName: Test_Malformed
* orientation: top
* height: 5
* width:  5
* actions: extrude

## Unreadable Number

* orientation: top
* height: tall
* width:  5
* depth:  1
* actions: extrude

## Bad Everything Else

* LayerColor: chartreusey
* orientation: sideways
* shape: blob
* height: 5
* width:  5
* depth:  1
* position: 3
* actions: sculpt
* material: aluminium

## Zero Depth

* orientation: top
* height: 5
* width:  5
* depth:  0
* actions: extrude

## Good Part

* LayerName: Test_Malformed
* orientation: top
* height: 5
* width:  5
* depth:  1
* position: 0, 0
* actions: extrude

## Through Without Cut

* orientation: top
* height: 5
* width:  5
* depth:  through
* actions: extrude

## Select Without Layer

* actions: select

## Missing Group

### group:nowhere[0]

## Misspelled Step

* select: Good Part
* actions: deselctAll

## Offset Without Value

* select: Good Part
* actions: offset

## Value Without Action

* value: 3
* actions: copy
* actions: paste
