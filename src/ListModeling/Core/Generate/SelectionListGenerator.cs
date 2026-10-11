using System;
using System.Collections.Generic;
using System.Linq;
using ListModeling.Core.Syntax;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace ListModeling.Core.Generate
{
  /// <summary>
  /// Describes selected objects as a list. The model decides the algorithm:
  ///
  ///   1. The world bounding box gives three extents. The thinnest one is the depth axis,
  ///      which picks the orientation (Z thin: top, Y thin: front, X thin: right).
  ///   2. The other two extents are width and height; the box center on the profile plane
  ///      is the position; the box start along the depth axis is the offset.
  ///   3. A planar cap perpendicular to the depth axis (or the curve itself) is tested for
  ///      a circle, an ellipse, or a regular polygon; anything else is a rectangle.
  ///   4. Objects on the same layer share one "# Element: Boolean" block, and the layer
  ///      name and color are written on every part.
  ///
  /// Curves become "curve" parts; everything else becomes an "extrude" part. Values are
  /// in model units.
  /// </summary>
  public static class SelectionListGenerator
  {
    public static string GenerateText(RhinoDoc doc, IEnumerable<RhinoObject> objects)
      => ListWriter.Write(Generate(doc, objects));

    public static ListDocument Generate(RhinoDoc doc, IEnumerable<RhinoObject> objects)
    {
      var list = new ListDocument();
      double tolerance = doc.ModelAbsoluteTolerance;
      var byLayer = new Dictionary<int, Element>();
      int counter = 0;

      foreach (var obj in objects)
      {
        if (obj == null || obj.Geometry == null)
          continue;

        var bbox = obj.Geometry.GetBoundingBox(true);
        if (!bbox.IsValid)
        {
          list.Diagnostics.Add(new Diagnostic(DiagnosticLevel.Warning, 0, $"Skipped object {obj.Id}: no bounding box."));
          continue;
        }

        counter++;
        bool isCurve = obj.Geometry is Curve;
        var part = new Part(PartName(obj, counter, isCurve), 0) { HasProperties = true };

        var layer = doc.Layers.FindIndex(obj.Attributes.LayerIndex);
        if (layer != null)
        {
          part.LayerName = layer.FullPath;
          part.LayerColor = ColorParser.ToDisplayString(layer.Color);
        }

        DescribeBox(part, bbox, isCurve, tolerance);
        DetectShape(part, obj.Geometry, tolerance);
        part.Actions.Add(isCurve ? PartAction.Curve : PartAction.Extrude);

        if (!byLayer.TryGetValue(obj.Attributes.LayerIndex, out var element))
        {
          element = new Element("Element", "Boolean", 0);
          byLayer[obj.Attributes.LayerIndex] = element;
          list.Elements.Add(element);
        }
        element.Parts.Add(part);
      }

      if (list.Elements.Count == 0)
        list.Diagnostics.Add(new Diagnostic(DiagnosticLevel.Warning, 0, "Nothing selected could be described."));

      return list;
    }

    static string PartName(RhinoObject obj, int index, bool isCurve)
    {
      if (!string.IsNullOrWhiteSpace(obj.Name))
        return obj.Name.Trim();
      return (isCurve ? "Curve " : "Solid ") + index;
    }

    static void DescribeBox(Part part, BoundingBox bbox, bool isCurve, double tolerance)
    {
      double dx = bbox.Max.X - bbox.Min.X;
      double dy = bbox.Max.Y - bbox.Min.Y;
      double dz = bbox.Max.Z - bbox.Min.Z;
      var center = bbox.Center;

      // Thinnest extent is the depth axis; ties fall through to top.
      if (dy < dx - tolerance && dy < dz - tolerance)
      {
        part.Orientation = Orientation.Front;
        Set(part, dx, dz, dy, center.X, center.Z, bbox.Min.Y, isCurve, tolerance);
      }
      else if (dx < dy - tolerance && dx < dz - tolerance)
      {
        part.Orientation = Orientation.Right;
        Set(part, dy, dz, dx, center.Y, center.Z, bbox.Min.X, isCurve, tolerance);
      }
      else
      {
        part.Orientation = Orientation.Top;
        Set(part, dx, dy, dz, center.X, center.Y, bbox.Min.Z, isCurve, tolerance);
      }
    }

    static void Set(Part part, double width, double height, double depth, double a, double b, double offset, bool isCurve, double tolerance)
    {
      part.Width = new Measure(Round(width), LengthUnit.Model);
      part.Height = new Measure(Round(height), LengthUnit.Model);
      if (!isCurve)
        part.Depth = new Measure(Round(Math.Max(depth, tolerance)), LengthUnit.Model);
      part.PositionA = new Measure(Round(a), LengthUnit.Model);
      part.PositionB = new Measure(Round(b), LengthUnit.Model);
      part.Offset = new Measure(Math.Abs(offset) < tolerance ? 0 : Round(offset), LengthUnit.Model);
    }

    static double Round(double value)
    {
      var rounded = Math.Round(value, 4);
      return rounded == 0 ? 0 : rounded;   // never write -0
    }

    /// <summary>Looks for a circle, ellipse, or regular polygon in the object's profile; rectangle otherwise.</summary>
    static void DetectShape(Part part, GeometryBase geometry, double tolerance)
    {
      var profile = FindProfile(part.Orientation, geometry, tolerance);
      if (profile == null)
        return;

      if (profile.TryGetCircle(out var circle, tolerance))
      {
        part.Shape = PartShape.Circle;
        part.Width = new Measure(Round(circle.Radius), LengthUnit.Model);
        part.Height = part.Width;
        return;
      }

      if (profile.TryGetEllipse(out var ellipse, tolerance))
      {
        part.Shape = PartShape.Ellipse;
        part.Width = new Measure(Round(ellipse.Radius1), LengthUnit.Model);
        part.Height = new Measure(Round(ellipse.Radius2), LengthUnit.Model);
        return;
      }

      if (profile.TryGetPolyline(out var polyline) && polyline.IsClosed)
      {
        int sides = polyline.Count - 1;
        if (sides == 4)
          return; // rectangle; bounding box already describes it

        if (sides >= 3 && IsRegular(polyline, sides, tolerance))
        {
          part.Shape = PartShape.Polygon;
          part.PolygonSides = sides;
          var centroid = polyline.CenterPoint();
          part.Width = new Measure(Round(centroid.DistanceTo(polyline[0])), LengthUnit.Model);
          part.Height = part.Width;
        }
      }
    }

    static bool IsRegular(Polyline polyline, int sides, double tolerance)
    {
      double first = polyline[0].DistanceTo(polyline[1]);
      for (int i = 1; i < sides; i++)
      {
        if (Math.Abs(polyline[i].DistanceTo(polyline[i + 1]) - first) > tolerance * 10)
          return false;
      }
      return true;
    }

    /// <summary>A closed planar curve perpendicular to the depth axis: the curve itself, or a cap of the solid.</summary>
    static Curve FindProfile(Orientation orientation, GeometryBase geometry, double tolerance)
    {
      if (geometry is Curve curve)
        return curve.IsClosed ? curve : null;

      Brep brep = geometry as Brep;
      if (brep == null && geometry is Extrusion extrusion)
        brep = extrusion.ToBrep(true);
      if (brep == null && geometry is Surface surface)
        brep = surface.ToBrep();
      if (brep == null)
        return null;

      Runtime.PartGeometry.Frame(orientation, out _, out _, out var depthAxis);

      foreach (var face in brep.Faces)
      {
        if (!face.IsPlanar(tolerance))
          continue;
        if (!face.TryGetPlane(out var plane, tolerance))
          continue;
        // IsParallelTo: 1 parallel, -1 anti-parallel, 0 neither. Either cap will do.
        if (plane.Normal.IsParallelTo(depthAxis, RhinoMath.ToRadians(1.0)) == 0)
          continue;

        var loop = face.OuterLoop;
        if (loop == null)
          continue;
        var edge = loop.To3dCurve();
        if (edge != null && edge.IsClosed)
          return edge;
      }
      return null;
    }
  }
}
