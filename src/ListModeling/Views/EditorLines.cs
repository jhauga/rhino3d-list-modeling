using System;
using System.Collections.Generic;
using Eto.Drawing;
using Eto.Forms;

namespace ListModeling.Views
{
  /// <summary>
  /// Reads which lines of the panel editor are on screen and where they are drawn.
  ///
  /// The Eto build Rhino 8 ships gives a TextArea no scroll position, no scroll event
  /// and no line geometry, so this asks the platform control behind it. On Rhino for
  /// Windows that is a WPF System.Windows.Controls.TextBox, reached through dynamic so
  /// the plugin needs no WPF reference. Its "lines" are rows as displayed: with word wrap
  /// one line of the list can take several rows, and a row starts a line of the list
  /// when it begins the text or follows a line feed.
  ///
  /// Anywhere else, or once any native call throws, <see cref="Available"/> turns false
  /// for the rest of the session and the panel carries on without line numbers.
  /// </summary>
  sealed class EditorLines
  {
    const string WpfTextBoxType = "System.Windows.Controls.TextBox";

    // A failure on one panel means the same calls fail on every panel, so stay off.
    static bool s_failed;

    readonly TextArea _textArea;
    dynamic _native;
    bool _checked;

    public EditorLines(TextArea textArea)
    {
      _textArea = textArea;
    }

    /// <summary>Raised once, the first time a native call fails.</summary>
    public event Action Failed;

    /// <summary>True while the platform control is the WPF text box and no call has failed.</summary>
    public bool Available => !s_failed && Native != null;

    dynamic Native
    {
      get
      {
        if (!_checked)
        {
          _checked = true;
          var control = _textArea.ControlObject;
          if (control != null && IsWpfTextBox(control.GetType()))
            _native = control;
        }
        return _native;
      }
    }

    static bool IsWpfTextBox(Type type)
    {
      for (var t = type; t != null; t = t.BaseType)
      {
        if (t.FullName == WpfTextBoxType)
          return true;
      }
      return false;
    }

    /// <summary>
    /// A cheap summary of what is on screen: the first visible row, its top, the row
    /// count and the control's size. Null when the control has no layout yet (it returns
    /// -1 before its first layout and while the panel's tab is not the selected one).
    /// </summary>
    public (int FirstRow, double FirstTop, int RowCount, double Height, double Width)? Signature()
    {
      if (!Available)
        return null;
      try
      {
        var native = Native;
        int first = native.GetFirstVisibleLineIndex();
        if (first < 0)
          return null;
        int index = native.GetCharacterIndexFromLineIndex(first);
        double top = index < 0 ? double.NaN : (double)native.GetRectFromCharacterIndex(index).Top;
        int rows = native.LineCount;
        double height = native.ActualHeight;
        double width = native.ActualWidth;
        return (first, top, rows, height, width);
      }
      catch (Exception)
      {
        Fail();
        return null;
      }
    }

    /// <summary>
    /// The rows on screen that start a line of the list, top to bottom, each with its
    /// line number and its top and height in the control's own units. Empty when the
    /// control has no layout yet.
    /// </summary>
    public List<NumberedRow> VisibleRows()
    {
      var rows = new List<NumberedRow>();
      if (!Available)
        return rows;
      try
      {
        var native = Native;
        int first = native.GetFirstVisibleLineIndex();
        int last = native.GetLastVisibleLineIndex();
        if (first < 0 || last < first)
          return rows;

        string text = native.Text ?? string.Empty;
        int counted = 0;    // characters already scanned for line feeds
        int line = 1;       // the line of the list that holds character 'counted'

        for (int row = first; row <= last; row++)
        {
          int index = native.GetCharacterIndexFromLineIndex(row);
          if (index < 0 || index > text.Length)
            break;

          line += CountLineFeeds(text, counted, index);
          counted = index;

          bool startsLine = index == 0 || text[index - 1] == '\n';
          if (!startsLine)
            continue;

          var rect = native.GetRectFromCharacterIndex(index);
          double top = rect.Top;
          double height = rect.Height;
          if (double.IsNaN(top) || double.IsInfinity(top) || double.IsNaN(height) || double.IsInfinity(height))
            continue;

          rows.Add(new NumberedRow(line, (float)top, (float)height));
        }
      }
      catch (Exception)
      {
        Fail();
        rows.Clear();
      }
      return rows;
    }

    /// <summary>
    /// The color of the editor's own border, so the number column can share its frame.
    /// Falls back to the given color when the border is not a plain color.
    /// </summary>
    public Color BorderColor(Color fallback)
    {
      if (!Available)
        return fallback;
      try
      {
        dynamic color = Native.BorderBrush.Color;
        return Color.FromArgb((byte)color.R, (byte)color.G, (byte)color.B, (byte)color.A);
      }
      catch (Exception)
      {
        // A gradient or missing brush is not a failure of the line lookups.
        return fallback;
      }
    }

    /// <summary>Lays the control out now, so the next lookups see a change made a moment ago.</summary>
    public void UpdateLayout() => Call(native => native.UpdateLayout());

    public void ScrollToEnd() => Call(native => native.ScrollToEnd());

    public void ScrollToStart() => Call(native => native.ScrollToHome());

    void Call(Action<dynamic> action)
    {
      if (!Available)
        return;
      try { action(Native); }
      catch (Exception) { Fail(); }
    }

    void Fail()
    {
      if (s_failed)
        return;
      s_failed = true;
      Failed?.Invoke();
    }

    static int CountLineFeeds(string text, int from, int to)
    {
      int count = 0;
      for (int i = from; i < to; i++)
      {
        if (text[i] == '\n')
          count++;
      }
      return count;
    }
  }

  /// <summary>A row on screen that starts a line of the list.</summary>
  readonly struct NumberedRow
  {
    public NumberedRow(int line, float top, float height)
    {
      Line = line;
      Top = top;
      Height = height;
    }

    /// <summary>The line number, counting from 1.</summary>
    public int Line { get; }

    /// <summary>Top of the row, measured from the top of the editor control.</summary>
    public float Top { get; }

    public float Height { get; }
  }
}
