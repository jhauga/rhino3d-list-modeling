using System;
using System.Collections.Generic;
using System.Linq;
using ListModeling.Core.Syntax;
using Rhino.Geometry;

namespace ListModeling.Core.Runtime
{
  /// <summary>
  /// Applies a part's filletEdge block to its freshly extruded solid, before any boolean,
  /// while the edge loops are still the ones the list describes.
  ///
  /// A loop is the set of edges lying in one plane across the solid. Along the part's own
  /// extrusion, loop 0 is on the profile plane and loop 1 at the end of the depth. In a
  /// face group (top, front, right) loop 0 is the plane at the lowest coordinate on that
  /// world axis (Z, Y, X) and loop 1 the highest.
  ///
  /// Edges in a loop are ordered like CSS margins: top, right, bottom, left, clockwise
  /// from the top, as seen in the view the frame is named after. For the part's own loops
  /// that is the profile with width to the right and height up; for face groups it is the
  /// Top view (X right, Y up), the Front view (X right, Z up) or the Right view (Y right,
  /// Z up). A list of radii expands like CSS on a four-edge loop and cycles on others.
  ///
  /// When two selections name the same edge, the first one wins.
  /// </summary>
  public static class FilletEdges
  {
    /// <summary>Where a part's extrusion starts and which way it runs.</summary>
    public readonly struct ExtrusionFrame
    {
      public ExtrusionFrame(Plane profilePlane, Vector3d depthDirection, double depth)
      {
        ProfilePlane = profilePlane;
        DepthDirection = depthDirection;
        Depth = depth;
      }

      /// <summary>Origin at the start of the extrusion, X along the width, Y along the height.</summary>
      public Plane ProfilePlane { get; }
      public Vector3d DepthDirection { get; }
      public double Depth { get; }
    }

    readonly struct LoopFrame
    {
      public LoopFrame(Point3d origin, Vector3d normal, Vector3d right, Vector3d up)
      {
        Origin = origin;
        Normal = normal;
        Right = right;
        Up = up;
      }

      public Point3d Origin { get; }
      public Vector3d Normal { get; }
      public Vector3d Right { get; }
      public Vector3d Up { get; }
    }

    /// <summary>
    /// Returns the filleted solid, or the solid unchanged when nothing could be filleted.
    /// Problems go into the report as warnings; skipped duplicates as messages.
    /// </summary>
    public static Brep Apply(Brep solid, FilletSpec spec, ExtrusionFrame frame, Func<Measure, double> toModel,
      double tolerance, RunReport report, string partLabel)
    {
      if (solid == null || spec == null || spec.Selections.Count == 0)
        return solid;

      var edges = new List<int>();
      var radii = new List<double>();
      var claimed = new HashSet<int>();
      double onPlane = Math.Max(tolerance, 1e-9);

      foreach (var selection in spec.Selections)
      {
        int alreadyFilleted = 0;
        for (int position = 0; position < selection.Loops.Count; position++)
        {
          int loop = selection.Loops[position];
          var loopFrame = FrameFor(selection.Axis, loop, solid, frame);
          var loopEdges = OrderedLoopEdges(solid, loopFrame, onPlane);
          if (loopEdges.Count == 0)
          {
            report.Warnings.Add($"Part '{partLabel}' (line {selection.Line}): no edges found for loop {loop} {AxisPhrase(selection.Axis)}; nothing is filleted there.");
            continue;
          }

          // Fewer radius entries than loops: the last entry carries on.
          var entry = selection.Radii[Math.Min(position, selection.Radii.Count - 1)];
          var values = Expand(entry.Select(toModel).ToList(), loopEdges.Count);

          for (int i = 0; i < loopEdges.Count; i++)
          {
            if (!claimed.Add(loopEdges[i]))
            {
              alreadyFilleted++;
              continue;
            }
            edges.Add(loopEdges[i]);
            radii.Add(values[i]);
          }
        }

        if (alreadyFilleted > 0)
          report.Messages.Add($"Part '{partLabel}' (line {selection.Line}): {alreadyFilleted} edge(s) were already filleted by an earlier select and keep that radius.");
      }

      if (edges.Count == 0)
        return solid;

      var filleted = Fillet(solid, edges, radii, RailType.RollingBall, tolerance)
                  ?? Fillet(solid, edges, radii, RailType.DistanceFromEdge, tolerance);
      if (filleted == null)
      {
        report.Warnings.Add($"Part '{partLabel}': Rhino could not fillet the selected edges, usually because a radius is too large for the edges around it; the solid is kept without fillets.");
        return solid;
      }
      return filleted;
    }

    static Brep Fillet(Brep solid, List<int> edges, List<double> radii, RailType rail, double tolerance)
    {
      Brep[] result;
      try
      {
        result = Brep.CreateFilletEdges(solid, edges, radii, radii, BlendType.Fillet, rail, tolerance);
      }
      catch (Exception)
      {
        return null;
      }
      if (result == null || result.Length == 0)
        return null;

      var brep = result.Length == 1 ? result[0] : JoinIntoOne(result, tolerance);
      if (brep == null || !brep.IsValid || !brep.IsSolid)
        return null;
      if (brep.SolidOrientation == BrepSolidOrientation.Inward)
        brep.Flip();
      return brep;
    }

    static Brep JoinIntoOne(Brep[] pieces, double tolerance)
    {
      var joined = Brep.JoinBreps(pieces, tolerance);
      return joined != null && joined.Length == 1 ? joined[0] : null;
    }

    static LoopFrame FrameFor(FilletAxis axis, int loop, Brep solid, ExtrusionFrame frame)
    {
      if (axis == FilletAxis.Extrusion)
      {
        var origin = frame.ProfilePlane.Origin + frame.DepthDirection * (loop == 0 ? 0 : frame.Depth);
        return new LoopFrame(origin, frame.DepthDirection, frame.ProfilePlane.XAxis, frame.ProfilePlane.YAxis);
      }

      var box = solid.GetBoundingBox(true);
      var corner = loop == 0 ? box.Min : box.Max;
      switch (axis)
      {
        case FilletAxis.X:
          return new LoopFrame(new Point3d(corner.X, 0, 0), Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis);
        case FilletAxis.Y:
          return new LoopFrame(new Point3d(0, corner.Y, 0), Vector3d.YAxis, Vector3d.XAxis, Vector3d.ZAxis);
        default:
          return new LoopFrame(new Point3d(0, 0, corner.Z), Vector3d.ZAxis, Vector3d.XAxis, Vector3d.YAxis);
      }
    }

    /// <summary>Edges whose ends and middle lie on the loop plane, clockwise from the top.</summary>
    static List<int> OrderedLoopEdges(Brep solid, LoopFrame frame, double onPlane)
    {
      var found = new List<(int index, Point3d middle)>();
      foreach (var edge in solid.Edges)
      {
        var middle = edge.PointAt(edge.Domain.Mid);
        if (OnPlane(edge.PointAtStart, frame, onPlane) && OnPlane(edge.PointAtEnd, frame, onPlane) && OnPlane(middle, frame, onPlane))
          found.Add((edge.EdgeIndex, middle));
      }
      if (found.Count < 2)
        return found.Select(f => f.index).ToList();

      var center = new Point3d(found.Average(f => f.middle.X), found.Average(f => f.middle.Y), found.Average(f => f.middle.Z));
      return found
        .OrderBy(f => ClockwiseFromTop(f.middle - center, frame))
        .Select(f => f.index)
        .ToList();
    }

    static bool OnPlane(Point3d point, LoopFrame frame, double tolerance)
      => Math.Abs((point - frame.Origin) * frame.Normal) <= tolerance;

    static double ClockwiseFromTop(Vector3d offset, LoopFrame frame)
    {
      double angle = Math.Atan2(offset * frame.Right, offset * frame.Up);
      return angle < -1e-9 ? angle + 2 * Math.PI : Math.Max(0, angle);
    }

    /// <summary>
    /// One value covers every edge. On a four-edge loop two values mean top and bottom,
    /// then right and left; three mean top, right and left, then bottom. Other loops cycle.
    /// </summary>
    static double[] Expand(List<double> values, int edgeCount)
    {
      var result = new double[edgeCount];
      if (values.Count == 1)
      {
        for (int i = 0; i < edgeCount; i++)
          result[i] = values[0];
        return result;
      }

      if (edgeCount == 4)
      {
        switch (values.Count)
        {
          case 2: return new[] { values[0], values[1], values[0], values[1] };
          case 3: return new[] { values[0], values[1], values[2], values[1] };
          default: return new[] { values[0], values[1], values[2], values[3] };
        }
      }

      for (int i = 0; i < edgeCount; i++)
        result[i] = values[i % values.Count];
      return result;
    }

    static string AxisPhrase(FilletAxis axis)
    {
      switch (axis)
      {
        case FilletAxis.X: return "in the right group";
        case FilletAxis.Y: return "in the front group";
        case FilletAxis.Z: return "in the top group";
        default: return "along the part's depth";
      }
    }
  }
}
