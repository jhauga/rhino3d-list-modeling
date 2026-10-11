using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ListModeling.Core.Syntax;
using Rhino.Geometry;

namespace ListModeling.Core.Runtime
{
  /// <summary>
  /// Builds a pipe solid around a rail curve. A constant radius is one station with no
  /// parameter; otherwise each station sets the radius at a point on the rail, given as
  /// a parameter of the curve or as a percentage of its length, and Rhino blends between
  /// them. Open rails get flat caps so the pipe is always a closed solid that can cut or
  /// merge like any extruded part.
  ///
  /// RhinoCommon takes station positions as normalized parameters (0 to 1 across the
  /// curve's domain), so both forms are converted before the call.
  /// </summary>
  public static class PipeBuilder
  {
    /// <summary>
    /// Returns the pipe, or null with the reason in problem. A pipe that comes back open
    /// is returned with a note in notice; it can be kept but not used as a cutter.
    ///
    /// seamTurn moves the seam of a closed rail that fraction of the way around before the
    /// pipe is built, with every station kept at its place on the curve. A pipe whose seam
    /// lies on the seam of the solid it cuts makes the boolean fail; circles and cylinders
    /// both start on their width axis, so a groove around a round part meets exactly that.
    /// </summary>
    public static Brep Build(Curve rail, IReadOnlyList<RadiusStation> stations, Func<Measure, double> toModel,
      double tolerance, double angleTolerance, out string problem, out string notice, double seamTurn = 0)
    {
      problem = null;
      notice = null;
      if (rail == null || !rail.IsValid)
      {
        problem = "the pipe's curve is not valid";
        return null;
      }
      if (stations == null || stations.Count == 0)
      {
        problem = "the pipe has no radius";
        return null;
      }

      var shape = rail;
      if (seamTurn > 0 && rail.IsClosed)
      {
        var turned = rail.DuplicateCurve();
        if (turned.ChangeClosedCurveSeam(rail.Domain.ParameterAt(seamTurn)))
          shape = turned;
      }

      Brep[] pieces;
      if (stations.Count == 1 && !stations[0].Parameter.HasValue)
      {
        pieces = Brep.CreatePipe(shape, toModel(stations[0].Radius), false, PipeCapMode.Flat, false, tolerance, angleTolerance);
      }
      else
      {
        var points = new List<KeyValuePair<double, double>>();
        foreach (var station in stations)
        {
          if (!TryRailParameter(rail, station, out var t, out problem))
            return null;
          // Stations are placed on the curve as written; when the seam moved, find the
          // same point on the turned curve.
          if (!ReferenceEquals(shape, rail) && shape.ClosestPoint(rail.PointAt(t), out var onShape))
            t = onShape;
          points.Add(new KeyValuePair<double, double>(shape.Domain.NormalizedParameterAt(t), toModel(station.Radius)));
        }

        // On a closed curve the end is the start, so a station written at the end
        // (100%, or the last parameter) belongs at 0.
        if (shape.IsClosed)
        {
          for (int i = 0; i < points.Count; i++)
          {
            if (points[i].Key >= 1 - 1e-9)
              points[i] = new KeyValuePair<double, double>(0, points[i].Value);
          }
        }

        // Stations at one point collapse to one when their radii agree; when they
        // disagree there is no single radius to give the pipe there.
        points.Sort((a, b) => a.Key.CompareTo(b.Key));
        for (int i = points.Count - 1; i > 0; i--)
        {
          if (Math.Abs(points[i].Key - points[i - 1].Key) > 1e-9)
            continue;
          if (Math.Abs(points[i].Value - points[i - 1].Value) > 1e-9)
          {
            problem = rail.IsClosed
              ? "two radius stations with different radii sit at the same point; on a closed curve the start and the end (0% and 100%) are the same point"
              : "two radius stations with different radii sit at the same point on the curve";
            return null;
          }
          points.RemoveAt(i);
        }

        pieces = Brep.CreatePipe(shape, points.Select(p => p.Key), points.Select(p => p.Value),
          false, PipeCapMode.Flat, false, tolerance, angleTolerance);
      }

      if (pieces == null || pieces.Length == 0)
      {
        problem = "Rhino could not build the pipe; a radius larger than the tightest bend of the curve is the usual cause";
        return null;
      }

      var brep = pieces.Length == 1 ? pieces[0] : JoinIntoOne(pieces, tolerance);
      if (brep == null)
      {
        problem = "the pipe came back in pieces that do not join";
        return null;
      }

      if (!brep.IsSolid)
        notice = "the pipe is not a closed solid";
      else if (brep.SolidOrientation == BrepSolidOrientation.Inward)
        brep.Flip();

      return brep;
    }

    static bool TryRailParameter(Curve rail, RadiusStation station, out double t, out string problem)
    {
      problem = null;
      t = 0;
      double value = station.Parameter ?? 0;

      if (station.IsPercent)
      {
        if (rail.NormalizedLengthParameter(value / 100.0, out t))
          return true;
        problem = $"the radius station at {Format(value)}% could not be placed on the curve";
        return false;
      }

      var domain = rail.Domain;
      double slack = Math.Max(1e-9, domain.Length * 1e-9);
      if (value < domain.Min - slack || value > domain.Max + slack)
      {
        problem = $"the radius station at {Format(value)} is outside the curve, which runs from {Format(domain.Min)} to {Format(domain.Max)}; " +
                  "use a percentage such as 50% to place a station by length";
        return false;
      }
      t = Math.Min(domain.Max, Math.Max(domain.Min, value));
      return true;
    }

    static Brep JoinIntoOne(Brep[] pieces, double tolerance)
    {
      var joined = Brep.JoinBreps(pieces, tolerance);
      return joined != null && joined.Length == 1 ? joined[0] : null;
    }

    static string Format(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
  }
}
