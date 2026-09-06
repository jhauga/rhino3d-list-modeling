using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ListModeling.Core.Syntax;
using Xunit;

namespace ListModeling.Syntax.Tests
{
  /// <summary>
  /// Regression baselines for the syntax layer. Every list under examples/ and
  /// tests/fixtures/ is parsed, then written back and compared against a checked-in
  /// golden file, and its diagnostics are compared the same way. A change in wording,
  /// ordering or defaults shows up as a readable text diff instead of a silent shift
  /// in behavior.
  ///
  /// To accept intended changes, rerun with the environment variable
  /// LISTMODELING_UPDATE_GOLDEN=1 and review the resulting diff before committing.
  /// </summary>
  [Trait("suite", "regression")]
  public class GoldenFileTests
  {
    static bool UpdateGolden =>
      Environment.GetEnvironmentVariable("LISTMODELING_UPDATE_GOLDEN") == "1";

    public static IEnumerable<object[]> ListFiles()
    {
      foreach (var path in SourceFiles())
        yield return new object[] { GoldenKey(path) };
    }

    static IEnumerable<string> SourceFiles()
      => Directory.GetFiles(RepoPaths.Examples, "*.md")
          .Concat(Directory.GetFiles(RepoPaths.Fixtures, "*.md"))
          .OrderBy(p => p, StringComparer.Ordinal);

    /// <summary>Stable, flat identifier for a source list, for example "fixtures--shapes".</summary>
    static string GoldenKey(string path)
      => Path.GetFileName(Path.GetDirectoryName(path)) + "--" + Path.GetFileNameWithoutExtension(path);

    static string SourcePath(string key)
      => SourceFiles().Single(p => GoldenKey(p) == key);

    [Theory]
    [MemberData(nameof(ListFiles))]
    public void WrittenFormMatchesItsGoldenFile(string key)
    {
      var document = ListParser.Parse(File.ReadAllText(SourcePath(key)));
      Compare(key + ".written.md", ListWriter.Write(document));
    }

    [Theory]
    [MemberData(nameof(ListFiles))]
    public void DiagnosticsMatchTheirGoldenFile(string key)
    {
      var document = ListParser.Parse(File.ReadAllText(SourcePath(key)));
      var text = document.Diagnostics.Count == 0
        ? "(none)" + Environment.NewLine
        : string.Join(Environment.NewLine, document.Diagnostics.Select(d => d.ToString())) + Environment.NewLine;
      Compare(key + ".diagnostics.txt", text);
    }

    [Theory]
    [MemberData(nameof(ListFiles))]
    public void EveryListSurvivesAWriteAndReparseUnchanged(string key)
    {
      var once = ListWriter.Write(ListParser.Parse(File.ReadAllText(SourcePath(key))));
      var twice = ListWriter.Write(ListParser.Parse(once));
      Assert.Equal(once, twice);
    }

    [Fact]
    public void TheShippedExampleParsesCleanly()
    {
      var document = ListParser.Parse(File.ReadAllText(Path.Combine(RepoPaths.Examples, "circuit-board.md")));
      Assert.False(document.HasErrors);
      Assert.Equal(2, document.Elements.Count);
      Assert.Equal(4, document.AllParts.Count());
    }

    [Fact]
    public void TheMalformedFixtureReportsEveryProblemAndKeepsTheGoodPart()
    {
      var document = ListParser.Parse(File.ReadAllText(Path.Combine(RepoPaths.Fixtures, "malformed.md")));

      Assert.True(document.HasErrors);
      var errors = document.Diagnostics.Where(d => d.Level == DiagnosticLevel.Error).ToList();
      var warnings = document.Diagnostics.Where(d => d.Level == DiagnosticLevel.Warning).ToList();

      // Errors mean the part cannot be built and the runner will skip it.
      Assert.Contains(errors, d => d.Message.Contains("depth"));
      Assert.Contains(errors, d => d.Message.Contains("Cannot read height"));
      Assert.Contains(errors, d => d.Message.Contains("zero or negative"));

      // Warnings mean a value was resolved to a default and the part is still built.
      Assert.Contains(warnings, d => d.Message.Contains("two values"));
      Assert.Contains(warnings, d => d.Message.Contains("Unknown element type"));
      Assert.Contains(warnings, d => d.Message.Contains("Unknown orientation"));
      Assert.Contains(warnings, d => d.Message.Contains("Unknown shape"));
      Assert.Contains(warnings, d => d.Message.Contains("Unknown action"));

      // A broken part never takes its neighbors down with it.
      var good = document.AllParts.Single(p => p.Name == "Good Part");
      Assert.True(good.HasAllDimensions);
      Assert.False(good.HasError);

      // Exactly the two parts whose values could all be resolved are buildable.
      var buildable = document.AllParts.Where(p => !p.HasError).Select(p => p.Name).ToList();
      Assert.Equal(new[] { "Bad Everything Else", "Good Part" }, buildable);
    }

    static void Compare(string goldenName, string actual)
    {
      Directory.CreateDirectory(RepoPaths.Golden);
      var goldenPath = Path.Combine(RepoPaths.Golden, goldenName);

      if (UpdateGolden || !File.Exists(goldenPath))
      {
        File.WriteAllText(goldenPath, actual);
        if (!UpdateGolden)
          Assert.Fail($"Golden file {goldenName} did not exist and has been created. Review it and rerun.");
        return;
      }

      var expected = File.ReadAllText(goldenPath);
      if (Normalize(expected) == Normalize(actual))
        return;

      Assert.Fail(
        $"{goldenName} differs from the golden file.{Environment.NewLine}" +
        $"Rerun with LISTMODELING_UPDATE_GOLDEN=1 to accept the change.{Environment.NewLine}" +
        $"--- expected ---{Environment.NewLine}{expected}{Environment.NewLine}" +
        $"--- actual ---{Environment.NewLine}{actual}");
    }

    /// <summary>Line endings are a checkout detail, not a behavior change.</summary>
    static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');
  }
}
