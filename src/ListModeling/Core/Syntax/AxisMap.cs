namespace ListModeling.Core.Syntax
{
  /// <summary>A world axis.</summary>
  public enum WorldAxis { X, Y, Z }

  /// <summary>
  /// Which world axis each of an orientation's axes lies on. The width and height axes
  /// always point along a positive world axis; the depth axis points the negative way for
  /// bottom, back and left. Runtime.PartGeometry draws profiles on the same frame.
  ///
  ///   orientation  width  height  depth
  ///   top          X      Y       +Z
  ///   bottom       X      Y       -Z
  ///   front        X      Z       +Y
  ///   back         X      Z       -Y
  ///   right        Y      Z       +X
  ///   left         Y      Z       -X
  /// </summary>
  public static class AxisMap
  {
    public static void Of(Orientation orientation, out WorldAxis width, out WorldAxis height, out WorldAxis depth, out int depthSign)
    {
      switch (orientation)
      {
        case Orientation.Bottom:
          width = WorldAxis.X; height = WorldAxis.Y; depth = WorldAxis.Z; depthSign = -1; return;
        case Orientation.Front:
          width = WorldAxis.X; height = WorldAxis.Z; depth = WorldAxis.Y; depthSign = 1; return;
        case Orientation.Back:
          width = WorldAxis.X; height = WorldAxis.Z; depth = WorldAxis.Y; depthSign = -1; return;
        case Orientation.Right:
          width = WorldAxis.Y; height = WorldAxis.Z; depth = WorldAxis.X; depthSign = 1; return;
        case Orientation.Left:
          width = WorldAxis.Y; height = WorldAxis.Z; depth = WorldAxis.X; depthSign = -1; return;
        default:
          width = WorldAxis.X; height = WorldAxis.Y; depth = WorldAxis.Z; depthSign = 1; return;
      }
    }

    /// <summary>
    /// Which coordinate a world coordinate sets for an orientation, and the factor to apply:
    /// the first or second position value as written, or the offset, negated when the depth
    /// axis points down its world axis.
    /// </summary>
    internal static CoordinateSlot SlotFor(Orientation orientation, WorldAxis axis, out int factor)
    {
      Of(orientation, out var width, out var height, out _, out var depthSign);
      factor = 1;
      if (axis == width)
        return CoordinateSlot.PlaneA;
      if (axis == height)
        return CoordinateSlot.PlaneB;
      factor = depthSign;
      return CoordinateSlot.Depth;
    }
  }
}
