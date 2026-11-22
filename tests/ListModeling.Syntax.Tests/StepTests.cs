using System.Linq;
using ListModeling.Core.Syntax;
using Xunit;

namespace ListModeling.Syntax.Tests
{
  /// <summary>Steps, targets and select: how a header becomes a sequence, a select part or a part.</summary>
  [Trait("suite", "unit")]
  public class StepTests
  {
    const string Dimensions = "- height: 1\n- width: 1\n- depth: 1\n";

    static ListDocument Parse(string body) => ListParser.Parse("# Element: Boolean\n\n## Seq\n\n" + body);

    static Part Only(ListDocument doc) => Assert.Single(doc.AllParts);

    static string Normalize(string text) => text.Replace("\r\n", "\n");

    // ---- Step words ---------------------------------------------------------------------

    [Theory]
    [InlineData("select", StepKind.Select)]
    [InlineData("SELECT", StepKind.Select)]
    [InlineData("deselect", StepKind.Deselect)]
    [InlineData("De-Select", StepKind.Deselect)]
    [InlineData("de_select", StepKind.Deselect)]
    [InlineData("deselectAll", StepKind.DeselectAll)]
    [InlineData("deselect all", StepKind.DeselectAll)]
    [InlineData("DESELECT_ALL", StepKind.DeselectAll)]
    [InlineData("selNone", StepKind.DeselectAll)]
    [InlineData("sel none", StepKind.DeselectAll)]
    [InlineData("offset", StepKind.Offset)]
    [InlineData("Off-Set", StepKind.Offset)]
    [InlineData("extrude", StepKind.Extrude)]
    [InlineData("Extrude", StepKind.Extrude)]
    [InlineData("booleanDifference", StepKind.BooleanDifference)]
    [InlineData("boolean difference", StepKind.BooleanDifference)]
    [InlineData("Boolean_Difference", StepKind.BooleanDifference)]
    [InlineData("boolean-difference", StepKind.BooleanDifference)]
    [InlineData("copy", StepKind.Copy)]
    [InlineData("PASTE", StepKind.Paste)]
    [InlineData("changeLayer", StepKind.ChangeLayer)]
    [InlineData("ChangeLayer", StepKind.ChangeLayer)]
    [InlineData("change layer", StepKind.ChangeLayer)]
    [InlineData("change-layer", StepKind.ChangeLayer)]
    [InlineData("setLayer", StepKind.ChangeLayer)]
    [InlineData("set_layer", StepKind.ChangeLayer)]
    [InlineData("delete", StepKind.Delete)]
    [InlineData("Delete", StepKind.Delete)]
    public void EveryStepWordReadsInAnySpelling(string word, StepKind kind)
    {
      // Two actions lines make the header a sequence whatever the word is.
      var doc = Parse("- actions: deselectAll\n- actions: " + word + "\n");
      var part = Only(doc);
      Assert.True(part.IsSequence);
      Assert.Equal(2, part.Steps.Count);
      Assert.Equal(kind, part.Steps[1].Kind);
      Assert.DoesNotContain(doc.Diagnostics, d => d.Message.Contains("Unknown action"));
    }

    [Fact]
    public void SeveralWordsOnOneActionsLineAreStepsInOrder()
    {
      var part = Only(Parse("- select: Knob\n- actions: copy, paste, change layer\n- value: Shop\n"));
      Assert.Equal(new[] { StepKind.Select, StepKind.Copy, StepKind.Paste, StepKind.ChangeLayer },
        part.Steps.Select(s => s.Kind).ToArray());
      Assert.Equal("Shop", part.Steps[3].LayerPath);
    }

    // ---- Parts, select parts and sequences ------------------------------------------------

    [Fact]
    public void OneActionsLineOfOldWordsIsAPart()
    {
      var doc = Parse(Dimensions + "- actions: extrude\n");
      var part = Only(doc);
      Assert.False(part.IsSequence);
      Assert.Empty(part.Steps);
      Assert.Null(part.SelectTarget);
      Assert.Empty(doc.Diagnostics);
    }

    [Fact]
    public void OneActionsLineWithAnUnknownWordIsStillAPart()
    {
      var doc = Parse(Dimensions + "- actions: sculpt\n");
      var part = Only(doc);
      Assert.False(part.IsSequence);
      Assert.Equal(PartAction.Extrude, part.PrimaryAction);
      var warning = Assert.Single(doc.Diagnostics);
      Assert.Equal("Unknown action 'sculpt' ignored.", warning.Message);
    }

    [Fact]
    public void SelectWithARoleIsStillThePlannedError()
    {
      var doc = Parse("- LayerName: Plate\n- actions: select, subtract\n");
      var part = Only(doc);
      Assert.False(part.IsSequence);
      Assert.True(part.HasError);
      Assert.Contains(doc.Diagnostics, d => d.Message.Contains("'select, subtract' is planned"));
    }

    [Fact]
    public void ALoneActionsSelectWithALayerReadsTheLayer()
    {
      var doc = Parse("- LayerName: Shop::Plate\n- actions: select\n");
      var part = Only(doc);
      Assert.Empty(doc.Diagnostics);
      Assert.False(part.IsSequence);
      Assert.Equal(PartGenerator.Select, part.Generator);
      Assert.Null(part.SelectTarget);
    }

    [Fact]
    public void ALoneActionsSelectWithoutALayerSelectsByItsOwnName()
    {
      var part = Only(Parse("- actions: select\n"));
      Assert.Equal(PartGenerator.Select, part.Generator);
      Assert.True(part.SelectTarget.OwnName);
      Assert.Equal("Seq", part.SelectTarget.Name);
    }

    [Theory]
    [InlineData("- select: Knob\n")]
    [InlineData("- actions: select\n- value: Knob\n")]
    public void ALoneSelectWithATargetIsASelectPart(string body)
    {
      var doc = Parse("- LayerName: Shop::Plate\n" + body);
      var part = Only(doc);
      Assert.Empty(doc.Diagnostics);
      Assert.False(part.IsSequence);
      Assert.Equal(PartGenerator.Select, part.Generator);
      Assert.Equal(new[] { PartAction.Select }, part.Actions.ToArray());
      // A written target wins over the LayerName.
      Assert.Equal("Knob", part.SelectTarget.Name);
      Assert.False(part.SelectTarget.OwnName);
    }

    [Theory]
    [InlineData("- actions: select\n- actions: extrude\n- depth: 1\n")]
    [InlineData("- actions: delete\n")]
    [InlineData("- deselect: Knob\n")]
    [InlineData("- select: Knob\n- actions: extrude\n- depth: 1\n")]
    [InlineData("- select: Knob\n- select: Plate\n")]
    [InlineData("- actions: extrude\n- value: 2\n")]
    public void TheseHeadersAreSequences(string body)
    {
      var doc = Parse(body);
      var part = Only(doc);
      Assert.True(part.IsSequence);
      Assert.Empty(part.Actions);
      Assert.NotEmpty(part.Steps);
      Assert.False(part.HasError);
    }

    [Fact]
    public void ASequenceOnlyNotesTheShapeItWasGiven()
    {
      var doc = Parse("- shape: circle\n- radius: 2\n- position: 1, 2\n- select: Knob\n- actions: delete\n");
      var info = Assert.Single(doc.Diagnostics);
      Assert.Equal(DiagnosticLevel.Info, info.Level);
      Assert.Equal("Part 'Seq': steps act on what is selected; its shape, dimensions and position are not used.", info.Message);
      Assert.False(Only(doc).HasError);
    }

    [Fact]
    public void DepthAndOrientationOnASequenceAreUsedSoTheyGiveNoNote()
    {
      var doc = Parse("- orientation: front\n- depth: 2\n- select: Knob\n- actions: extrude\n");
      Assert.Empty(doc.Diagnostics);
      var part = Only(doc);
      Assert.Equal(Orientation.Front, part.Orientation);
      Assert.Equal(2.0, part.Steps[1].Distance.Value.Value, 9);
    }

    // ---- value --------------------------------------------------------------------------

    [Fact]
    public void AValueGoesToTheStepAboveIt()
    {
      var part = Only(Parse("- select: Curve\n- actions: offset\n- value: 2mm + 1\n- actions: booleanDifference\n- value: Cutter[1]\n"));
      Assert.Equal(3.0, part.Steps[1].Distance.Value.Value, 9);
      Assert.Equal(LengthUnit.Millimeters, part.Steps[1].Distance.Value.Unit);
      Assert.Equal("Cutter", part.Steps[2].Target.Name);
      Assert.Equal(1, part.Steps[2].Target.Index);
    }

    [Fact]
    public void AValueWithNoStepAboveItWarnsAndIsIgnored()
    {
      var doc = Parse("- value: 3\n- actions: copy\n- actions: paste\n");
      var warning = Assert.Single(doc.Diagnostics);
      Assert.Equal(DiagnosticLevel.Warning, warning.Level);
      Assert.Equal("A value line needs an action above it; 'value: 3' is ignored.", warning.Message);
      Assert.Equal(5, warning.Line);
      Assert.False(Only(doc).HasError);
    }

    [Fact]
    public void AValueOnAStepThatTakesNoneWarnsAndIsIgnored()
    {
      var doc = Parse("- select: Knob\n- actions: copy\n- value: 3\n");
      var warning = Assert.Single(doc.Diagnostics);
      Assert.Equal("'copy' takes no value; 'value: 3' is ignored.", warning.Message);
      Assert.False(Only(doc).HasError);
    }

    [Fact]
    public void ValueWarningsAreGivenOnceWhenTheSequenceIsBuiltAgain()
    {
      var doc = ListParser.Parse(
        "# Element: Boolean\n\n## Seq\n\n- value: 3\n- actions: copy\n- actions: paste\n\n" +
        "# Element: Boolean\n\n## Seq\n");
      Assert.Single(doc.Diagnostics);
      Assert.Equal(2, doc.AllParts.Count(p => p.IsSequence));
    }

    // ---- Targets ------------------------------------------------------------------------

    [Fact]
    public void APlainTargetIsANameAndALayer()
    {
      var target = StepTarget.Parse("Stripe");
      Assert.Equal("Stripe", target.Text);
      Assert.Equal("Stripe", target.Name);
      Assert.Equal("Stripe", target.LayerPath);
      Assert.Null(target.Index);
      Assert.Null(target.Group);
      Assert.Null(target.Problem);
      Assert.False(target.ByLayer);
      Assert.False(target.OwnName);
    }

    [Theory]
    [InlineData("Stripe[1]", 1)]
    [InlineData("Stripe [12]", 12)]
    [InlineData("Stripe[*]", null)]
    public void AnIndexPicksOneObject(string text, int? index)
    {
      var target = StepTarget.Parse(text);
      Assert.Equal("Stripe", target.Name);
      Assert.Equal(index, target.Index);
      Assert.Null(target.Problem);
      Assert.Equal(text, target.Text);
    }

    [Theory]
    [InlineData("group:feet", null)]
    [InlineData("group: feet[2]", 2)]
    public void AGroupTargetNamesTheGroupId(string text, int? index)
    {
      var target = StepTarget.Parse(text);
      Assert.Equal("feet", target.Group);
      Assert.Null(target.Name);
      Assert.Null(target.LayerPath);
      Assert.Equal(index, target.Index);
    }

    [Theory]
    [InlineData("Shop::Plate")]
    [InlineData("\"Shop\", \"Plate\"")]
    public void ALayerPathIsReadLikeLayerName(string text)
    {
      var target = StepTarget.Parse(text);
      Assert.Equal("Shop::Plate", target.LayerPath);
      Assert.Equal("Shop::Plate", target.Name);
    }

    [Theory]
    [InlineData("Stripe[1-3]", "Cannot read '[1-3]' after 'Stripe'; write [*] or one number, counting from 0.")]
    [InlineData("Stripe[]", "Cannot read '[]' after 'Stripe'; write [*] or one number, counting from 0.")]
    public void AnythingElseBetweenTheBracketsIsAnError(string text, string problem)
    {
      Assert.Equal(problem, StepTarget.Parse(text).Problem);

      var doc = Parse("- select: " + text + "\n- actions: delete\n");
      Assert.Contains(doc.Diagnostics, d => d.Level == DiagnosticLevel.Error && d.Line == 5 && d.Message == "Part 'Seq': " + problem);
      Assert.True(Only(doc).HasError);
    }

    [Fact]
    public void ASelectPartWithABadIndexIsAnError()
    {
      var doc = Parse("- select: Stripe[x]\n");
      Assert.Contains(doc.Diagnostics, d => d.Level == DiagnosticLevel.Error && d.Message.Contains("Cannot read '[x]' after 'Stripe'"));
      Assert.True(Only(doc).HasError);
    }

    // ---- select without a value -----------------------------------------------------------

    [Fact]
    public void ASelectWithoutAValueReadsTheNearestLayerNameAboveIt()
    {
      var part = Only(Parse(
        "- actions: deselectAll\n- LayerName: Shop::Plate\n- actions: select\n" +
        "- LayerName: \"Shop\", \"Rail\"\n- actions: select\n- actions: delete\n"));
      Assert.True(part.IsSequence);
      var selects = part.Steps.Where(s => s.Kind == StepKind.Select).ToList();
      Assert.Equal(2, selects.Count);
      Assert.True(selects[0].Target.ByLayer);
      Assert.Equal("Shop::Plate", selects[0].Target.LayerPath);
      Assert.Equal("Shop::Rail", selects[1].Target.LayerPath);
    }

    [Fact]
    public void ADeselectWithoutAValueReadsTheLayerNameAboveIt()
    {
      var part = Only(Parse("- select: Knob\n- LayerName: Shop\n- deselect:\n- actions: delete\n"));
      var deselect = part.Steps.Single(s => s.Kind == StepKind.Deselect);
      Assert.True(deselect.Target.ByLayer);
      Assert.Equal("Shop", deselect.Target.LayerPath);
      Assert.Null(deselect.Problem);
    }

    [Fact]
    public void ASelectWithNoLayerNameAnywhereSelectsByTheHeadersOwnName()
    {
      var part = Only(Parse("- actions: select\n- actions: delete\n"));
      var select = part.Steps[0];
      Assert.True(select.Target.OwnName);
      Assert.Equal("Seq", select.Target.Name);
    }

    [Fact]
    public void ASelectReadsTheLayerNameItInheritsFromAContainer()
    {
      var doc = ListParser.Parse(
        "# Element: Boolean\n\n## Holder\n\n- LayerName: Shop::Plate\n\n### Seq\n\n- actions: select\n- actions: delete\n");
      var part = Only(doc);
      Assert.True(part.Steps[0].Target.ByLayer);
      Assert.Equal("Shop::Plate", part.Steps[0].Target.LayerPath);
    }

    [Fact]
    public void ASelectReadsTheLayerNameOfTheElementDefaults()
    {
      var doc = ListParser.Parse("# Element: Boolean\n\n- LayerName: Shop\n\n## Seq\n\n- actions: select\n- actions: delete\n");
      Assert.Equal("Shop", Only(doc).Steps[0].Target.LayerPath);
    }

    // ---- Parse errors ---------------------------------------------------------------------

    [Theory]
    [InlineData("- actions: offset\n", "'offset' needs a value, the distance to offset by.")]
    [InlineData("- actions: deselectAll\n- deselect:\n", "'deselect' needs a value, the objects to deselect.")]
    [InlineData("- actions: booleanDifference\n", "'booleanDifference' needs a value, the solids to cut with.")]
    [InlineData("- actions: changeLayer\n", "'changeLayer' needs a value, the layer to move to.")]
    [InlineData("- actions: deselectAll\n- actions: extrude\n", "'extrude' needs a value or a depth, the distance to extrude.")]
    [InlineData("- actions: offset\n- value: wide\n", "cannot read the distance 'wide' for 'offset'")]
    [InlineData("- actions: offset\n- value: 2 - 2\n", "'offset' needs a distance other than zero.")]
    [InlineData("- depth: through\n- actions: deselectAll\n- actions: extrude\n- value: 1\n", "'extrude' cannot use depth 'through'; give it a distance.")]
    [InlineData("- select: Knob\n- actions: curve\n", "'curve' builds a part and cannot be a step.")]
    [InlineData("- select: Knob\n- actions: pipe\n", "'pipe' builds a part and cannot be a step.")]
    [InlineData("- select: Knob\n- actions: subtract\n", "'subtract' builds a part and cannot be a step.")]
    [InlineData("- select: Knob\n- actions: union\n", "'union' builds a part and cannot be a step.")]
    [InlineData("- select: Knob\n- actions: surface\n", "'surface' builds a part and cannot be a step.")]
    public void AStepThatCannotBeReadIsAnErrorThatMarksThePart(string body, string message)
    {
      var doc = Parse(body);
      var part = Only(doc);
      Assert.True(part.IsSequence);
      Assert.True(part.HasError);
      Assert.Contains(doc.Diagnostics, d => d.Level == DiagnosticLevel.Error && d.Message.StartsWith("Part 'Seq': ") && d.Message.Contains(message));
    }

    [Fact]
    public void AStepErrorIsOnTheStepsOwnLine()
    {
      var doc = Parse("- select: Knob\n- actions: deselectAll\n- actions: offset\n");
      var error = Assert.Single(doc.Diagnostics);
      Assert.Equal(7, error.Line);
    }

    [Fact]
    public void AnUnknownWordInASequenceStopsTheWholeHeader()
    {
      var doc = Parse("- select: Knob\n- actions: deselctAll\n- actions: delete\n");
      var part = Only(doc);
      Assert.True(part.HasError);
      Assert.Equal(new[] { StepKind.Select, StepKind.Delete }, part.Steps.Select(s => s.Kind).ToArray());
      Assert.Contains(doc.Diagnostics, d => d.Level == DiagnosticLevel.Warning && d.Line == 6
        && d.Message == "Unknown action 'deselctAll' ignored. Did you mean 'deselectAll'?");
      Assert.Contains(doc.Diagnostics, d => d.Level == DiagnosticLevel.Error && d.Line == 3
        && d.Message == "Part 'Seq': an action on line 6 could not be read, so none of its steps run.");
    }

    // ---- The suggestion -------------------------------------------------------------------

    [Theory]
    [InlineData("deselctAll", "deselectAll")]
    [InlineData("booleandiference", "booleanDifference")]
    [InlineData("extrud", "extrude")]
    [InlineData("chnageLayer", "changeLayer")]
    public void AMisspelledActionGetsTheClosestWord(string word, string suggestion)
    {
      Assert.Equal(suggestion, StepWords.Closest(word));
      var doc = Parse(Dimensions + "- actions: " + word + "\n");
      Assert.Contains(doc.Diagnostics, d => d.Message == $"Unknown action '{word}' ignored. Did you mean '{suggestion}'?");
    }

    [Theory]
    [InlineData("sculpt")]
    [InlineData("polish")]
    public void AWordFarFromEveryActionGetsNoSuggestion(string word)
    {
      Assert.Null(StepWords.Closest(word));
      var doc = Parse(Dimensions + "- actions: " + word + "\n");
      Assert.Contains(doc.Diagnostics, d => d.Message == $"Unknown action '{word}' ignored.");
    }

    // ---- Steps are not passed down -----------------------------------------------------

    [Fact]
    public void StepsOnAContainerWarnOnceAndAreNotPassedDown()
    {
      var doc = ListParser.Parse(
        "# Element: Boolean\n\n## Holder\n\n- select: Knob\n- actions: delete\n- value: 3\n\n" +
        "### First\n\n" + Dimensions + "\n### Second\n\n" + Dimensions);
      var warning = Assert.Single(doc.Diagnostics);
      Assert.Equal(DiagnosticLevel.Warning, warning.Level);
      Assert.Equal(5, warning.Line);
      Assert.Equal("Steps written on a container are not passed down; write them under the header that runs them.", warning.Message);
      Assert.All(doc.AllParts, p =>
      {
        Assert.False(p.IsSequence);
        Assert.Empty(p.Steps);
        Assert.Equal(PartAction.Extrude, p.PrimaryAction);
        Assert.False(p.HasError);
      });
    }

    [Fact]
    public void ASubtractOnAContainerIsStillPassedDown()
    {
      var doc = ListParser.Parse(
        "# Element: Boolean\n\n## Holes\n\n- actions: subtract\n- actions: copy\n\n### First\n\n" + Dimensions);
      Assert.Single(doc.Diagnostics, d => d.Message.StartsWith("Steps written on a container"));
      var part = Only(doc);
      Assert.Equal(PartRole.Subtract, part.Role);
      Assert.False(part.IsSequence);
    }

    [Fact]
    public void StepsOnTheElementDefaultsAreNotPassedDown()
    {
      var doc = ListParser.Parse("# Element: Boolean\n\n- deselect: Knob\n" + Dimensions + "\n## Block\n\n- position: 1, 0\n");
      Assert.Single(doc.Diagnostics, d => d.Message.StartsWith("Steps written on a container"));
      Assert.False(Only(doc).IsSequence);
    }

    [Fact]
    public void StepsUnderAGroupReferenceWarnOnceAndAreIgnored()
    {
      var doc = ListParser.Parse(
        "# Element: Boolean\n\n## Feet\n\n" + Dimensions + "\n### group:feet\n\n- position: 1, 0\n\n" +
        "# Element: Boolean\n\n## group:feet[*]\n\n- select: Knob\n- actions: delete\n");
      var warning = Assert.Single(doc.Diagnostics);
      Assert.Equal("Steps cannot be written under a group reference; they are ignored.", warning.Message);
      var again = Assert.Single(doc.Elements[1].Parts);
      Assert.False(again.IsSequence);
      Assert.Equal(PartAction.Extrude, again.PrimaryAction);
    }

    // ---- Path ---------------------------------------------------------------------------

    [Fact]
    public void APartUnderTwoContainersBelongsToBoth()
    {
      var doc = ListParser.Parse(
        "# Element: Boolean\n\n## Outer\n\n### Middle\n\n#### Block\n\n" + Dimensions);
      Assert.Equal(new[] { "Outer", "Middle", "Block" }, Only(doc).Path.ToArray());
    }

    [Fact]
    public void AGroupItemBelongsToTheHeaderAboveIt()
    {
      var doc = ListParser.Parse(
        "# Element: Boolean\n\n## Row\n\n" + Dimensions + "\n### group:row\n\n- position: 1, 0\n\n### group:row\n\n- position: 2, 0\n");
      Assert.All(doc.AllParts, p => Assert.Equal(new[] { "Row" }, p.Path.ToArray()));
      Assert.All(doc.AllParts, p => Assert.Equal("row", p.Group));
    }

    [Fact]
    public void APartRebuiltUnderAnotherHeaderAlsoBelongsToThatHeader()
    {
      var doc = ListParser.Parse(
        "# Element: Boolean\n\n## Shelf\n\n### Block\n\n" + Dimensions + "\n" +
        "# Element: Boolean\n\n## Crate\n\n### Block\n");
      var again = Assert.Single(doc.Elements[1].Parts);
      Assert.Equal(new[] { "Crate", "Shelf", "Block" }, again.Path.ToArray());
    }

    // ---- Repeats ------------------------------------------------------------------------

    [Fact]
    public void ABareRepeatOfASequenceRunsItAgain()
    {
      var doc = ListParser.Parse(
        "# Element: Boolean\n\n## Tidy\n\n- select: Scrap\n- actions: delete\n\n" +
        "# Element: Boolean\n\n## Tidy\n");
      Assert.Empty(doc.Diagnostics);
      var again = Assert.Single(doc.Elements[1].Parts);
      Assert.True(again.IsSequence);
      Assert.Equal(new[] { StepKind.Select, StepKind.Delete }, again.Steps.Select(s => s.Kind).ToArray());
      Assert.Equal("Scrap", again.Steps[0].Target.Name);
    }

    // ---- Writer -------------------------------------------------------------------------

    [Fact]
    public void EveryStepIsWrittenInItsOwnForm()
    {
      var doc = Parse(
        "- LayerName: Shop::Plate\n- LayerColor: Red\n- orientation: front\n- depth: 2\n" +
        "- actions: deselectAll\n- select: Knob[1]\n- deselect: group:feet\n- actions: select\n" +
        "- actions: offset\n- value: .5\n- actions: extrude\n- actions: booleanDifference\n- value: Cutter\n" +
        "- actions: copy\n- actions: paste\n- actions: setLayer\n- value: \"Shop\", \"Done\"\n- actions: delete\n");
      Assert.Empty(doc.Diagnostics);
      var written = Normalize(ListWriter.Write(doc));
      Assert.Contains(
        "## Seq\n\n" +
        "- LayerName: \"Shop\", \"Plate\"\n" +
        "- LayerColor: Red\n" +
        "- orientation: front\n" +
        "- actions: deselectAll\n" +
        "- select: Knob[1]\n" +
        "- deselect: group:feet\n" +
        "- select: Shop::Plate\n" +
        "- actions: offset\n" +
        "- value: 0.5\n" +
        "- actions: extrude\n" +
        "- value: 2\n" +
        "- actions: booleanDifference\n" +
        "- value: Cutter\n" +
        "- actions: copy\n" +
        "- actions: paste\n" +
        "- actions: changeLayer\n" +
        "- value: Shop::Done\n" +
        "- actions: delete\n", written);
    }

    [Fact]
    public void AWrittenSequenceParsesBackToTheSameSteps()
    {
      var first = Parse(
        "- actions: deselectAll\n- select: Stripe\n- actions: booleanDifference\n- value: Trim\n" +
        "- actions: copy, paste\n- LayerName: Shop::Knob\n- actions: select\n- actions: booleanDifference\n- value: Stripe[0]\n" +
        "- select: Stripe\n- actions: change layer\n- value: Shop::Stripe\n");
      var second = ListParser.Parse(ListWriter.Write(first));
      Assert.Empty(second.Diagnostics);

      var a = Only(first).Steps.Select(s => s.ToString()).ToArray();
      var b = Only(second).Steps.Select(s => s.ToString()).ToArray();
      Assert.Equal(a, b);
      Assert.Equal("select: Shop::Knob", b[5]);
    }

    [Fact]
    public void AnOwnNameSelectBesideALayerNameIsWrittenWithTheName()
    {
      var first = Parse("- actions: select\n- LayerName: Shop\n- actions: delete\n");
      Assert.True(Only(first).Steps[0].Target.OwnName);
      var written = Normalize(ListWriter.Write(first));
      Assert.Contains("- LayerName: Shop\n- select: Seq\n- actions: delete\n", written);
    }

    [Theory]
    [InlineData("- LayerName: Plate\n- actions: select\n", "- LayerName: Plate\n- actions: select\n")]
    [InlineData("- select: Knob[2]\n", "- select: Knob[2]\n")]
    [InlineData("- actions: select\n", "- actions: select\n")]
    public void ASelectPartIsWrittenInItsForm(string body, string expected)
    {
      var doc = Parse(body);
      var written = Normalize(ListWriter.Write(doc));
      Assert.Contains("## Seq\n\n" + expected, written);
      Assert.Equal(written, Normalize(ListWriter.Write(ListParser.Parse(written))));

      var again = Only(ListParser.Parse(written));
      Assert.Equal(Only(doc).SelectTarget?.Text, again.SelectTarget?.Text);
      Assert.Equal(PartGenerator.Select, again.Generator);
    }

    [Fact]
    public void TheStepsFixtureParsesCleanly()
    {
      var doc = ListParser.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(RepoPaths.Fixtures, "steps.md")));
      Assert.Empty(doc.Diagnostics);
      var parts = doc.AllParts.ToList();
      Assert.Equal(new[] { "Knob", "Stripe", "Trim Inner", "Trim Outer", "Scrap", "Make Ring", "Inlay", "Tidy" },
        parts.Select(p => p.Name).ToArray());
      Assert.Equal(new[] { false, false, false, true, false, true, true, true }, parts.Select(p => p.IsSequence).ToArray());
      Assert.Equal(new[] { "Trim", "Trim Outer" }, parts[3].Path.ToArray());

      var inlay = parts[6];
      Assert.Equal(11, inlay.Steps.Count);
      Assert.True(inlay.Steps[6].Target.ByLayer);
      Assert.Equal("Test_Steps::knob", inlay.Steps[6].Target.LayerPath);
      Assert.Equal(0, inlay.Steps[7].Target.Index);
      Assert.Equal("Test_Steps::inlay", inlay.Steps[10].LayerPath);
      Assert.Equal(2.0, parts[5].Steps[2].Distance.Value.Value, 9);
    }

    [Fact]
    public void TheSelectionsFixtureParsesCleanly()
    {
      var doc = ListParser.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(RepoPaths.Fixtures, "selections.md")));
      Assert.Empty(doc.Diagnostics);
      Assert.Equal(6, doc.Elements[0].Parts.Count);
      Assert.All(doc.Elements[1].Parts, p => Assert.True(p.IsSequence));
      var solo = doc.Elements[2].Parts[0];
      Assert.False(solo.IsSequence);
      Assert.True(solo.SelectTarget.OwnName);
      Assert.Equal("Solo", solo.SelectTarget.Name);
      Assert.True(doc.Elements[2].Parts[1].IsSequence);
    }
  }
}
