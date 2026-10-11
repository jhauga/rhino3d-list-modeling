using System;
using System.Collections.Generic;
using ListModeling.Core.Syntax;
using Rhino.Geometry;

namespace ListModeling.Core.Runtime
{
  /// <summary>
  /// Maps a part's orientation, dimensions and position onto world geometry.
  ///
  /// Axis mapping per orientation (width axis, height axis, depth axis):
  ///   top      +X  +Y  +Z      position is x, y
  ///   bottom   +X  +Y  -Z      position is x, y
  ///   front    +X  +Z  +Y      position is x, z
  ///   back     +X  +Z  -Y      position is x, z
  ///   right    +Y  +Z  +X      position is y, z
  ///   left     +Y  +Z  -X      position is y, z
  ///
  /// The profile is centered on the position, drawn on the plane through the origin of
  /// the depth axis (lifted by the optional offset), and extruded along the depth axis.
  /// </summary>
  public static class PartGeometry
  {
    public static void Frame(Orientation orientation, out Vector3d width, out Vector3d height, out Vector3d depth)
    {
      switch (orientation)
      {
        case Orientation.Bottom:
          width = Vector3d.XAxis; height = Vector3d.YAxis; depth = -Vector3d.ZAxis; break;
        case Orientation.Front:
          width = Vector3d.XAxis; height = Vector3d.ZAxis; depth = Vector3d.YAxis; break;
        case Orientation.Back:
          width = Vector3d.XAxis; height = Vector3d.ZAxis; depth = -Vector3d.YAxis; break;
        case Orientation.Right:
          width = Vector3d.YAxis; height = Vector3d.ZAxis; depth = Vector3d.XAxis; break;
        case Orientation.Left:
          width = Vector3d.YAxis; height = Vector3d.ZAxis; depth = -Vector3d.XAxis; break;
        default:
          width = Vector3d.XAxis; height = Vector3d.YAxis; depth = Vector3d.ZAxis; break;
      }
    }

    /// <summary>The profile plane: origin at the position, lifted along the depth axis by offset.</summary>
    public static Plane ProfilePlane(Orientation orientation, double positionA, double positionB, double offset)
    {
      Frame(orientation, out var width, out var height, out var depth);
      var origin = Point3d.Origin + width * positionA + height * positionB + depth * offset;
      return new Plane(origin, width, height);
    }

    /// <summary>
    /// The closed profile curve. For circles the width is the radius; for ellipses width
    /// and height are the two radii; polygons are inscribed in a circle of radius width.
    /// </summary>
    public static Curve Profile(PartShape shape, int polygonSides, Plane plane, double width, double height)
    {
      switch (shape)
      {
        case PartShape.Circle:
          return new Circle(plane, width).ToNurbsCurve();

        case PartShape.Ellipse:
          return new Ellipse(plane, width, height).ToNurbsCurve();

        case PartShape.Polygon:
          {
            int sides = Math.Max(3, polygonSides);
            var points = new List<Point3d>(sides + 1);
            for (int i = 0; i < sides; i++)
            {
              double angle = Math.PI / 2 + 2 * Math.PI * i / sides;
              points.Add(plane.PointAt(width * Math.Cos(angle), width * Math.Sin(angle)));
            }
            points.Add(points[0]);
            return new Polyline(points).ToNurbsCurve();
          }

        default:
          return new Rectangle3d(plane, new Interval(-width / 2, width / 2), new Interval(-height / 2, height / 2)).ToNurbsCurve();
      }
    }

    /// <summary>
    /// Extrudes a closed planar profile along an explicit vector and caps it. Returns null
    /// when the extrusion or the capping fails, so callers can report instead of crash.
    /// </summary>
    public static Brep Solid(Curve profile, Vector3d extrusionVector, double tolerance)
    {
      if (profile == null || !profile.IsClosed || extrusionVector.IsTiny())
        return null;

      var surface = Surface.CreateExtrusion(profile, extrusionVector);
      if (surface == null)
        return null;

      var open = Brep.CreateFromSurface(surface);
      if (open == null)
        return null;

      var capped = open.CapPlanarHoles(tolerance);
      if (capped == null || !capped.IsSolid)
        return null;

      // A polygon profile extrudes into one side face with kinks at the corners. The
      // document splits it into a face per side when the solid is added, but filleting
      // happens before that and cannot work on a kinked face, so split it here.
      capped.Faces.SplitKinkyFaces(Rhino.RhinoMath.DefaultAngleTolerance, true);

      // Booleans want outward-facing solids.
      if (capped.SolidOrientation == BrepSolidOrientation.Inward)
        capped.Flip();

      return capped;
    }
  }
}
