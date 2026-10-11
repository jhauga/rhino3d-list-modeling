<!-- The three accepted LayerName forms resolve to the same nested layer. -->

# Element: Boolean

## Path Form

- LayerName: Test_Nested::Child
- orientation: top
- height: 2
- width: 2
- depth: 1
- position: 300, 0
- actions: extrude

## Quoted Form

- LayerName: "Test_Nested", "Child"
- orientation: top
- height: 2
- width: 2
- depth: 1
- position: 305, 0
- actions: extrude

## Bullet Form

- LayerName: Test_Nested
  - Child
- orientation: top
- height: 2
- width: 2
- depth: 1
- position: 310, 0
- actions: extrude
