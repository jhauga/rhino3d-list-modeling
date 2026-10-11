using System;
using System.Collections.Generic;
using System.Globalization;

namespace ListModeling.Core.Syntax
{
  /// <summary>
  /// Evaluates arithmetic in a numeric value: numbers, + - * /, parentheses and unary
  /// minus, with an optional length unit after any number or closing parenthesis.
  /// ".31/2", "2in/2" and "(12.7 + 12.7)mm" are all lengths.
  ///
  /// A unit names the unit of the whole expression, so every unit written in one
  /// expression has to be the same one. Multiplying two lengths (an area) or dividing
  /// one length by another (a ratio) is rejected, because neither result is a length.
  /// </summary>
  public static class Arithmetic
  {
    /// <summary>
    /// Evaluates text into a measure. Returns false when the text cannot be read. The
    /// error is null when the text is simply not arithmetic, and holds a reason when it
    /// is arithmetic whose result cannot be a length (mixed units, an area, a ratio,
    /// division by zero).
    /// </summary>
    public static bool TryEvaluate(string text, out Measure measure, out string unknownUnit, out string error)
    {
      measure = default;
      unknownUnit = null;
      error = null;
      if (string.IsNullOrWhiteSpace(text))
        return false;

      var tokens = new List<Token>();
      if (!Tokenize(text, tokens))
        return false;

      var evaluator = new Evaluator(tokens);
      bool ok = evaluator.Expression(out var value, out _) && evaluator.AtEnd;
      error = evaluator.Error;
      if (!ok)
        return false;

      if (double.IsNaN(value) || double.IsInfinity(value))
      {
        error = "the result is not a finite number";
        return false;
      }

      unknownUnit = evaluator.UnknownUnit;
      // Never hand back -0, which would be written out as "-0".
      measure = new Measure(value == 0 ? 0 : value, evaluator.Unit ?? LengthUnit.Model);
      return true;
    }

    // ---- Tokens -------------------------------------------------------------------

    enum TokenKind { Number, Unit, Plus, Minus, Star, Slash, Open, Close }

    readonly struct Token
    {
      public Token(TokenKind kind, double number = 0, string text = null)
      {
        Kind = kind;
        Number = number;
        Text = text;
      }

      public TokenKind Kind { get; }
      public double Number { get; }
      public string Text { get; }
    }

    static bool Tokenize(string text, List<Token> tokens)
    {
      int i = 0;
      while (i < text.Length)
      {
        char c = text[i];
        if (char.IsWhiteSpace(c))
        {
          i++;
          continue;
        }

        if (char.IsDigit(c) || (c == '.' && i + 1 < text.Length && char.IsDigit(text[i + 1])))
        {
          int start = i;
          while (i < text.Length && char.IsDigit(text[i]))
            i++;
          if (i < text.Length && text[i] == '.')
          {
            i++;
            while (i < text.Length && char.IsDigit(text[i]))
              i++;
          }
          // An exponent needs at least one digit; "2e" is the number 2 followed by a unit "e".
          if (i < text.Length && (text[i] == 'e' || text[i] == 'E'))
          {
            int j = i + 1;
            if (j < text.Length && (text[j] == '+' || text[j] == '-'))
              j++;
            if (j < text.Length && char.IsDigit(text[j]))
            {
              i = j;
              while (i < text.Length && char.IsDigit(text[i]))
                i++;
            }
          }
          if (!double.TryParse(text.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return false;
          tokens.Add(new Token(TokenKind.Number, number));
          continue;
        }

        if (char.IsLetter(c))
        {
          int start = i;
          while (i < text.Length && char.IsLetter(text[i]))
            i++;
          tokens.Add(new Token(TokenKind.Unit, text: text.Substring(start, i - start)));
          continue;
        }

        switch (c)
        {
          case '"':
          case '\'':
            tokens.Add(new Token(TokenKind.Unit, text: c.ToString()));
            break;
          case '+': tokens.Add(new Token(TokenKind.Plus)); break;
          case '-': tokens.Add(new Token(TokenKind.Minus)); break;
          case '*': tokens.Add(new Token(TokenKind.Star)); break;
          case '/': tokens.Add(new Token(TokenKind.Slash)); break;
          case '(': tokens.Add(new Token(TokenKind.Open)); break;
          case ')': tokens.Add(new Token(TokenKind.Close)); break;
          default: return false;
        }
        i++;
      }
      return tokens.Count > 0;
    }

    // ---- Recursive descent ----------------------------------------------------------
    //
    //   expression := term (("+" | "-") term)*
    //   term       := factor (("*" | "/") factor)*
    //   factor     := ("-" | "+") factor | primary
    //   primary    := (number | "(" expression ")") unit?
    //
    // Every rule reports whether its result carries a unit, which is what decides
    // whether a product or a quotient is still a length.

    sealed class Evaluator
    {
      readonly List<Token> _tokens;
      int _position;
      string _unitText;

      public Evaluator(List<Token> tokens) { _tokens = tokens; }

      public LengthUnit? Unit { get; private set; }
      public string UnknownUnit { get; private set; }
      public string Error { get; private set; }
      public bool AtEnd => _position == _tokens.Count;

      bool Next(TokenKind kind)
      {
        if (_position < _tokens.Count && _tokens[_position].Kind == kind)
        {
          _position++;
          return true;
        }
        return false;
      }

      public bool Expression(out double value, out bool hasUnit)
      {
        if (!Term(out value, out hasUnit))
          return false;

        for (;;)
        {
          bool plus = Next(TokenKind.Plus);
          if (!plus && !Next(TokenKind.Minus))
            return true;
          if (!Term(out var right, out var rightUnit))
            return false;
          value = plus ? value + right : value - right;
          hasUnit |= rightUnit;
        }
      }

      bool Term(out double value, out bool hasUnit)
      {
        if (!Factor(out value, out hasUnit))
          return false;

        for (;;)
        {
          bool multiply = Next(TokenKind.Star);
          if (!multiply && !Next(TokenKind.Slash))
            return true;
          if (!Factor(out var right, out var rightUnit))
            return false;

          if (multiply)
          {
            if (hasUnit && rightUnit)
              return Fail("multiplying two lengths gives an area, not a length");
            value *= right;
          }
          else
          {
            if (right == 0)
              return Fail("division by zero");
            if (hasUnit && rightUnit)
              return Fail("dividing one length by another gives a ratio, not a length");
            value /= right;
          }
          hasUnit |= rightUnit;
        }
      }

      bool Factor(out double value, out bool hasUnit)
      {
        if (Next(TokenKind.Minus))
        {
          if (!Factor(out value, out hasUnit))
            return false;
          value = -value;
          return true;
        }
        if (Next(TokenKind.Plus))
          return Factor(out value, out hasUnit);
        return Primary(out value, out hasUnit);
      }

      bool Primary(out double value, out bool hasUnit)
      {
        value = 0;
        hasUnit = false;

        if (_position < _tokens.Count && _tokens[_position].Kind == TokenKind.Number)
        {
          value = _tokens[_position].Number;
          _position++;
        }
        else if (Next(TokenKind.Open))
        {
          if (!Expression(out value, out hasUnit) || !Next(TokenKind.Close))
            return false;
        }
        else
        {
          return false;
        }

        if (_position < _tokens.Count && _tokens[_position].Kind == TokenKind.Unit)
        {
          if (!ApplyUnit(_tokens[_position].Text))
            return false;
          _position++;
          hasUnit = true;
        }
        return true;
      }

      bool ApplyUnit(string text)
      {
        if (!Units.TryParseUnit(text, out var unit))
        {
          // Same rule as a single number: an unknown unit is reported and model units are used.
          UnknownUnit = UnknownUnit ?? text;
          unit = LengthUnit.Model;
        }

        if (Unit.HasValue && Unit.Value != unit)
          return Fail($"mixed units '{_unitText}' and '{text}'");

        Unit = unit;
        _unitText = _unitText ?? text;
        return true;
      }

      bool Fail(string reason)
      {
        Error = Error ?? reason;
        return false;
      }
    }
  }
}
