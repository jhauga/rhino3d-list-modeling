using System;
using System.Collections.Generic;
using System.Text;
using ListModeling.Core.Syntax;

namespace ListModeling.Core.Runtime
{
  /// <summary>What a run produced and what went wrong along the way.</summary>
  public sealed class RunReport
  {
    public RunReport(string source)
    {
      Source = string.IsNullOrWhiteSpace(source) ? "list" : source;
    }

    /// <summary>File name or label of the list that was run.</summary>
    public string Source { get; }

    /// <summary>Every object the run added to the document.</summary>
    public List<Guid> CreatedIds { get; } = new List<Guid>();

    /// <summary>
    /// The solids that came out of extrude and union actions (after any subtraction).
    /// The Steps route selects these before handing off to Rhino's own BooleanDifference.
    /// </summary>
    public List<Guid> BaseIds { get; } = new List<Guid>();

    public List<string> Messages { get; } = new List<string>();
    public List<string> Warnings { get; } = new List<string>();
    public List<string> Errors { get; } = new List<string>();

    public int ObjectsAdded => CreatedIds.Count;
    public bool Succeeded => Errors.Count == 0;

    public void AddDiagnostics(IEnumerable<Diagnostic> diagnostics)
    {
      foreach (var d in diagnostics)
      {
        switch (d.Level)
        {
          case DiagnosticLevel.Error: Errors.Add(d.ToString()); break;
          case DiagnosticLevel.Warning: Warnings.Add(d.ToString()); break;
          default: Messages.Add(d.ToString()); break;
        }
      }
    }

    /// <summary>Short status line for the command history.</summary>
    public string StatusLine()
      => $"List Modeling: {Source}: {ObjectsAdded} object(s) added, {Warnings.Count} warning(s), {Errors.Count} error(s).";

    /// <summary>Multi-line text for the completion popup.</summary>
    public string Summary()
    {
      var sb = new StringBuilder();
      sb.AppendLine($"Source: {Source}");
      sb.AppendLine($"Objects added: {ObjectsAdded}");
      if (Warnings.Count > 0)
      {
        sb.AppendLine();
        sb.AppendLine("Warnings:");
        foreach (var w in Warnings)
          sb.AppendLine("  " + w);
      }
      if (Errors.Count > 0)
      {
        sb.AppendLine();
        sb.AppendLine("Errors:");
        foreach (var e in Errors)
          sb.AppendLine("  " + e);
      }
      return sb.ToString().TrimEnd();
    }
  }
}
