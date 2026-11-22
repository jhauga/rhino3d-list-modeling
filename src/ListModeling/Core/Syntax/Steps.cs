using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace ListModeling.Core.Syntax
{
  /// <summary>
  /// One action a sequence runs on what is selected, in the order written. Steps are
  /// read from a header's own lines; see ListParser for when a header is a sequence.
  /// </summary>
  public enum StepKind { Select, Deselect, DeselectAll, Offset, Extrude, BooleanDifference, Copy, Paste, ChangeLayer, Delete }

  /// <summary>
  /// Names objects for select, deselect and the value of booleanDifference.
  ///
  ///   Stripe              every object the header Stripe owns
  ///   Stripe[1]           one of them, counting from 0 in the order the list made them
  ///   Stripe[*]           the same as Stripe
  ///   group:feet[2]       the objects of a group id, or one of them
  ///   Shop::Plate         also read as a layer path, the way LayerName reads it
  ///
  /// When the list runs, a target is looked up among the headers of the run first, then
  /// among the object names in the model, then as a layer. A target read from a
  /// LayerName line looks at layers only.
  /// </summary>
  public sealed class StepTarget
  {
    static readonly Regex IndexPattern = new Regex(@"^(?<base>.*?)\s*\[(?<sel>[^\[\]]*)\]$", RegexOptions.Compiled);
    static readonly Regex GroupPattern = new Regex(@"^group\s*:\s*(?<id>.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    StepTarget(string text, string name, string group, int? index, string layerPath, bool byLayer, bool ownName, string problem)
    {
      Text = text;
      Name = name;
      Group = group;
      Index = index;
      LayerPath = layerPath;
      ByLayer = byLayer;
      OwnName = ownName;
      Problem = problem;
    }

    /// <summary>The target as written, cleaned; the layer path for one read from a LayerName line.</summary>
    public string Text { get; }

    /// <summary>The header or object name, without its index; null for a group or a LayerName target.</summary>
    public string Name { get; }

    /// <summary>The id of a "group:id" target; null otherwise.</summary>
    public string Group { get; }

    /// <summary>One object, counting from 0; null means all of them.</summary>
    public int? Index { get; }

    /// <summary>The text read as a layer path ("Parent::Child"); null for a group target.</summary>
    public string LayerPath { get; }

    /// <summary>True when it came from a LayerName line: it looks at layers only.</summary>
    public bool ByLayer { get; }

    /// <summary>True for a select with neither a value nor a LayerName: it selects by the header's own name.</summary>
    public bool OwnName { get; }

    /// <summary>The parse error for this target, or null.</summary>
    public string Problem { get; }

    /// <summary>Reads a written target: a name, a group id or a layer path, with an optional index.</summary>
    public static StepTarget Parse(string text)
    {
      var cleaned = (text ?? string.Empty).Trim();
      var baseText = cleaned;
      int? index = null;
      string problem = null;

      var indexed = IndexPattern.Match(cleaned);
      if (indexed.Success)
      {
        baseText = indexed.Groups["base"].Value.Trim();
        var selector = indexed.Groups["sel"].Value.Trim();
        if (selector != "*")
        {
          if (int.TryParse(selector, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            index = number;
          else
            problem = $"Cannot read '[{selector}]' after '{baseText}'; write [*] or one number, counting from 0.";
        }
      }

      var group = GroupPattern.Match(baseText);
      if (group.Success)
        return new StepTarget(cleaned, null, group.Groups["id"].Value.Trim(), index, null, false, false, problem);

      var path = ListParser.LayerPathOf(baseText);
      // A quoted layer list has no header name of its own; its path stands in for one.
      var name = baseText.IndexOf('"') >= 0 ? path : baseText;
      return new StepTarget(cleaned, name, null, index, path, false, false, problem);
    }

    /// <summary>The layer a LayerName line names, for a select or deselect without a value.</summary>
    public static StepTarget FromLayer(string layerPath)
      => new StepTarget(layerPath, null, null, null, layerPath, true, false, null);

    /// <summary>The header's own name, for a select with neither a value nor a LayerName.</summary>
    public static StepTarget FromOwnName(string name)
      => new StepTarget(name, name, null, null, null, false, true, null);

    public override string ToString() => Text;
  }

  /// <summary>One step of a sequence.</summary>
  public sealed class Step
  {
    internal Step(StepKind kind, int line, string word)
    {
      Kind = kind;
      Line = line;
      Word = word;
    }

    public StepKind Kind { get; }

    /// <summary>The line the step is written on.</summary>
    public int Line { get; }

    /// <summary>The word as written.</summary>
    public string Word { get; }

    /// <summary>select, deselect and booleanDifference: the objects it names.</summary>
    public StepTarget Target { get; internal set; }

    /// <summary>offset and extrude: the distance.</summary>
    public Measure? Distance { get; internal set; }

    /// <summary>changeLayer: the layer, as a "Parent::Child" path.</summary>
    public string LayerPath { get; internal set; }

    /// <summary>The parse error for this step, or null. Any problem means none of the header's steps run.</summary>
    public string Problem { get; internal set; }

    public override string ToString()
    {
      var spelling = StepWords.Spelling(Kind);
      if (Target != null) return spelling + ": " + Target.Text;
      if (Distance.HasValue) return spelling + ": " + Distance.Value;
      if (LayerPath != null) return spelling + ": " + LayerPath;
      return spelling;
    }
  }

  /// <summary>
  /// The words a step can be written with, and the action words that build a part.
  /// Words match without regard to case, spaces, hyphens and underscores, so
  /// "ChangeLayer", "changeLayer" and "change layer" are one word.
  /// </summary>
  public static class StepWords
  {
    static readonly Dictionary<string, StepKind> Kinds = new Dictionary<string, StepKind>(StringComparer.Ordinal)
    {
      ["select"] = StepKind.Select,
      ["deselect"] = StepKind.Deselect,
      ["deselectall"] = StepKind.DeselectAll,
      ["selnone"] = StepKind.DeselectAll,
      ["offset"] = StepKind.Offset,
      ["extrude"] = StepKind.Extrude,
      ["booleandifference"] = StepKind.BooleanDifference,
      ["copy"] = StepKind.Copy,
      ["paste"] = StepKind.Paste,
      ["changelayer"] = StepKind.ChangeLayer,
      ["setlayer"] = StepKind.ChangeLayer,
      ["delete"] = StepKind.Delete,
    };

    /// <summary>Words that build a part where they are written, so they cannot be steps.</summary>
    static readonly HashSet<string> PartWords = new HashSet<string>(StringComparer.Ordinal)
    {
      "curve", "profile", "outline", "pipe", "tube", "subtract", "cut", "difference",
      "union", "add", "join", "surface", "solid",
    };

    /// <summary>Every action word, in the spelling a suggestion uses.</summary>
    static readonly string[] Spellings =
    {
      "extrude", "solid", "subtract", "cut", "difference", "union", "add", "join",
      "curve", "profile", "outline", "pipe", "tube", "select", "surface",
      "deselect", "deselectAll", "selNone", "offset", "booleanDifference",
      "copy", "paste", "changeLayer", "setLayer", "delete",
    };

    /// <summary>The form words are compared in: lower case, without spaces, hyphens and underscores.</summary>
    public static string Key(string word)
      => (word ?? string.Empty).Replace(" ", "").Replace("-", "").Replace("_", "").ToLowerInvariant();

    public static bool TryGetKind(string word, out StepKind kind) => Kinds.TryGetValue(Key(word), out kind);

    /// <summary>True for a word that exists only as a step: every step except select and extrude.</summary>
    public static bool IsStepOnly(string word)
      => TryGetKind(word, out var kind) && IsStepOnly(kind);

    public static bool IsStepOnly(StepKind kind) => kind != StepKind.Select && kind != StepKind.Extrude;

    /// <summary>True for a word that builds a part (curve, pipe, subtract, union, surface and their other names).</summary>
    public static bool BuildsAPart(string word) => PartWords.Contains(Key(word));

    /// <summary>True for any word an actions line can hold.</summary>
    public static bool IsKnown(string word) => Kinds.ContainsKey(Key(word)) || PartWords.Contains(Key(word));

    /// <summary>The spelling the writer and the messages use.</summary>
    public static string Spelling(StepKind kind)
    {
      switch (kind)
      {
        case StepKind.Select: return "select";
        case StepKind.Deselect: return "deselect";
        case StepKind.DeselectAll: return "deselectAll";
        case StepKind.Offset: return "offset";
        case StepKind.Extrude: return "extrude";
        case StepKind.BooleanDifference: return "booleanDifference";
        case StepKind.Copy: return "copy";
        case StepKind.Paste: return "paste";
        case StepKind.ChangeLayer: return "changeLayer";
        default: return "delete";
      }
    }

    /// <summary>
    /// Splits an actions value into words. Commas, semicolons and spaces separate words,
    /// but neighbors that only make a word together are joined, so "change layer" and
    /// "deselect all" are one word each.
    /// </summary>
    public static List<string> Split(string value)
    {
      var tokens = (value ?? string.Empty).Trim().Trim('[', ']')
        .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(t => t.Trim())
        .Where(t => t.Length > 0);

      var words = new List<string>();
      foreach (var group in tokens)
      {
        var pieces = group.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        int i = 0;
        while (i < pieces.Length)
        {
          int take = 1;
          for (int span = Math.Min(3, pieces.Length - i); span > 1; span--)
          {
            var joined = string.Concat(pieces.Skip(i).Take(span));
            if (Kinds.ContainsKey(Key(joined)))
            {
              take = span;
              break;
            }
          }
          words.Add(string.Join(" ", pieces.Skip(i).Take(take)));
          i += take;
        }
      }
      return words;
    }

    /// <summary>The known action word within two edits of the given one, or null.</summary>
    public static string Closest(string word)
    {
      var key = Key(word);
      if (key.Length == 0)
        return null;

      string best = null;
      int bestDistance = 3;
      foreach (var spelling in Spellings)
      {
        int distance = EditDistance(key, Key(spelling));
        if (distance < bestDistance)
        {
          best = spelling;
          bestDistance = distance;
        }
      }
      return bestDistance > 0 && bestDistance <= 2 ? best : null;
    }

    static int EditDistance(string a, string b)
    {
      var previous = new int[b.Length + 1];
      var current = new int[b.Length + 1];
      for (int j = 0; j <= b.Length; j++)
        previous[j] = j;
      for (int i = 1; i <= a.Length; i++)
      {
        current[0] = i;
        for (int j = 1; j <= b.Length; j++)
        {
          int cost = a[i - 1] == b[j - 1] ? 0 : 1;
          current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
        }
        var swap = previous;
        previous = current;
        current = swap;
      }
      return previous[b.Length];
    }
  }
}
