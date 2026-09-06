using System;
using System.Collections.Generic;
using System.Linq;

namespace ListModeling.Core
{
  /// <summary>
  /// The embedded list text per open document, keyed by document runtime serial number.
  /// The plugin serializes this into the .3dm so the list travels with the model.
  /// </summary>
  public static class ListStore
  {
    static readonly Dictionary<uint, string> Texts = new Dictionary<uint, string>();

    /// <summary>Raised when text is replaced from outside the panel (file read, command output).</summary>
    public static event Action<uint> TextReplaced;

    public static string GetText(uint documentSerialNumber)
      => Texts.TryGetValue(documentSerialNumber, out var text) ? text : string.Empty;

    /// <summary>Updates the store from the panel editor. Does not raise TextReplaced.</summary>
    public static void SetText(uint documentSerialNumber, string text)
      => Texts[documentSerialNumber] = text ?? string.Empty;

    /// <summary>Replaces the text and tells any open panel to refresh.</summary>
    public static void ReplaceText(uint documentSerialNumber, string text)
    {
      SetText(documentSerialNumber, text);
      TextReplaced?.Invoke(documentSerialNumber);
    }

    /// <summary>Adds a block after the existing text, separated by a blank line, and refreshes the panel.</summary>
    public static void AppendText(uint documentSerialNumber, string block)
    {
      var existing = GetText(documentSerialNumber).TrimEnd();
      var combined = existing.Length == 0
        ? (block ?? string.Empty)
        : existing + Environment.NewLine + Environment.NewLine + (block ?? string.Empty);
      ReplaceText(documentSerialNumber, combined);
    }

    public static bool HasText(uint documentSerialNumber)
      => !string.IsNullOrWhiteSpace(GetText(documentSerialNumber));

    public static bool HasAnyText()
      => Texts.Values.Any(t => !string.IsNullOrWhiteSpace(t));

    public static void Clear(uint documentSerialNumber)
      => Texts.Remove(documentSerialNumber);
  }
}
