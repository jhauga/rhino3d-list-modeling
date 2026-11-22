using System;
using System.Collections.Generic;
using Eto.Drawing;
using Eto.Forms;

namespace ListModeling.Views
{
  /// <summary>
  /// The column of line numbers left of the panel editor.
  ///
  /// It sits in the same row as the editor and has the same height, so a row's top in
  /// the editor is the y to draw its number at. One number per line of the list, blank
  /// lines included, right aligned and smaller and lighter than the text; a wrapped line
  /// is numbered at its first row. The column shares the editor's frame, so the editor's
  /// own left border is the line between the numbers and the text.
  ///
  /// The editor raises no scroll event Eto can see, so a timer compares a cheap summary
  /// of what is on screen and repaints only when it changed. The timer runs only while
  /// the panel is shown. When the line lookups are not available the column hides itself.
  /// </summary>
  sealed class LineNumberGutter : Drawable
  {
    /// <summary>Point size of the numbers; the editor uses 9.</summary>
    const float NumberSize = 7.5f;
    const float PadLeft = 4;
    const float PadRight = 4;
    const int MinimumDigits = 2;
    const double TickSeconds = 0.05;

    readonly TextArea _textArea;
    readonly EditorLines _lines;
    readonly UITimer _timer;
    readonly Font _font;
    readonly Color _numberColor = SystemColors.DisabledText;
    readonly Color _frameColor;

    (int, double, int, double, double, int)? _painted;
    int _textVersion;
    int _digits;

    public LineNumberGutter(TextArea textArea)
    {
      ID = ListModelingPanel.Ids.LineNumbers;
      _textArea = textArea;
      _lines = new EditorLines(textArea);
      _lines.Failed += OnLinesFailed;
      _font = new Font(textArea.Font.Family, NumberSize);
      _frameColor = _lines.BorderColor(SystemColors.DisabledText);
      BackgroundColor = SystemColors.ControlBackground;
      CanFocus = false;

      _timer = new UITimer { Interval = TickSeconds };
      _timer.Elapsed += OnTick;

      Paint += OnPaint;
      textArea.TextChanged += (s, e) => { _textVersion++; FitWidth(); Repaint(); };
      textArea.SizeChanged += (s, e) => Repaint();
      SizeChanged += (s, e) => Repaint();

      FitWidth();
      Visible = _lines.Available;
    }

    /// <summary>True while the numbers can be drawn on this platform.</summary>
    public bool Available => _lines.Available;

    /// <summary>Starts following the editor; call when the panel is shown.</summary>
    public void Start()
    {
      if (_lines.Available)
      {
        Repaint();
        _timer.Start();
      }
    }

    /// <summary>Stops following the editor; call when the panel is hidden or closed.</summary>
    public void Stop() => _timer.Stop();

    /// <summary>Forgets what was painted, so the next tick or paint draws afresh.</summary>
    public void Repaint()
    {
      _painted = null;
      if (Visible && _lines.Available)
        Invalidate();
    }

    /// <summary>The rows on screen that start a line, read from the editor right now.</summary>
    public List<NumberedRow> VisibleRows(bool layoutFirst)
    {
      if (layoutFirst)
        _lines.UpdateLayout();
      return _lines.VisibleRows();
    }

    public void ScrollToEnd()
    {
      _lines.ScrollToEnd();
      Repaint();
    }

    public void ScrollToStart()
    {
      _lines.ScrollToStart();
      Repaint();
    }

    void OnTick(object sender, EventArgs e)
    {
      if (!_lines.Available)
      {
        _timer.Stop();
        return;
      }
      if (!Visible)
        return;

      var signature = _lines.Signature();
      if (signature == null)
        return;   // no layout yet; try again on the next tick

      var s = signature.Value;
      var now = (s.FirstRow, s.FirstTop, s.RowCount, s.Height, s.Width, _textVersion);
      if (_painted.HasValue && _painted.Value.Equals(now))
        return;

      _painted = now;
      Invalidate();
    }

    void OnPaint(object sender, PaintEventArgs e)
    {
      var g = e.Graphics;
      float width = Width;
      float height = Height;
      if (width <= 0 || height <= 0)
        return;

      // Top, left and bottom of the shared frame; the editor draws the right side.
      g.FillRectangle(_frameColor, 0, 0, width, 1);
      g.FillRectangle(_frameColor, 0, 0, 1, height);
      g.FillRectangle(_frameColor, 0, height - 1, width, 1);

      foreach (var row in _lines.VisibleRows())
      {
        if (row.Top + row.Height < 1 || row.Top > height - 1)
          continue;
        string number = row.Line.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var size = g.MeasureString(_font, number);
        float x = width - PadRight - size.Width;
        float y = row.Top + (row.Height - size.Height) / 2;
        g.DrawText(_font, _numberColor, x, y, number);
      }
    }

    /// <summary>Wide enough for the digits of the last line number, at least two.</summary>
    void FitWidth()
    {
      int digits = Math.Max(MinimumDigits, CountLines(_textArea.Text).ToString(System.Globalization.CultureInfo.InvariantCulture).Length);
      if (digits == _digits)
        return;
      _digits = digits;
      var size = _font.MeasureString(new string('0', digits));
      Width = (int)Math.Ceiling(PadLeft + size.Width + PadRight);
    }

    static int CountLines(string text)
    {
      int lines = 1;
      if (text != null)
      {
        foreach (char c in text)
        {
          if (c == '\n')
            lines++;
        }
      }
      return lines;
    }

    void OnLinesFailed()
    {
      _timer.Stop();
      // A failure can come from inside a paint; change the layout afterward, not during it.
      Application.Instance.AsyncInvoke(() => Visible = false);
    }
  }
}
