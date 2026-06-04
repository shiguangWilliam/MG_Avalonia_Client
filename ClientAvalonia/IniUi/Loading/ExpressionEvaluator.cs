using ClientAvalonia.IniUi.Models;

namespace ClientAvalonia.IniUi.Loading;

/// <summary>Port of ClientGUI.Parser expression evaluation against a UiNode tree.</summary>
public sealed class ExpressionEvaluator
{
    private const int CharZero = 48;

    private readonly Dictionary<string, int> _constants;
    private UiNodeTree? _tree;
    private UiNode? _parsingNode;
    private string _input = string.Empty;
    private int _tokenPlace;

    public ExpressionEvaluator(int resolutionWidth = 1280, int resolutionHeight = 720, IReadOnlyDictionary<string, int>? extraConstants = null)
    {
        _constants = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["RESOLUTION_WIDTH"] = resolutionWidth,
            ["RESOLUTION_HEIGHT"] = resolutionHeight,
        };

        if (extraConstants != null)
        {
            foreach (KeyValuePair<string, int> kvp in extraConstants)
                _constants[kvp.Key] = kvp.Value;
        }
    }

    public int Evaluate(string input, UiNodeTree tree, UiNode parsingNode)
    {
        _tree = tree;
        _parsingNode = parsingNode;
        _input = input;
        _tokenPlace = 0;
        return GetExprValue();
    }

    public void UpdateResolution(int width, int height, IReadOnlyDictionary<string, int>? parserConstants = null)
    {
        _constants["RESOLUTION_WIDTH"] = width;
        _constants["RESOLUTION_HEIGHT"] = height;

        if (parserConstants == null)
            return;

        foreach (KeyValuePair<string, int> kvp in parserConstants)
            _constants[kvp.Key] = kvp.Value;
    }

    private int GetExprValue()
    {
        int value = 0;

        while (true)
        {
            SkipWhitespace();
            if (IsEndOfInput())
                return value;

            char c = _input[_tokenPlace];

            if (char.IsDigit(c))
                value = GetInt();
            else if (c == '+')
            {
                _tokenPlace++;
                value += GetNumericalValue();
            }
            else if (c == '-')
            {
                _tokenPlace++;
                value -= GetNumericalValue();
            }
            else if (c == '/')
            {
                _tokenPlace++;
                int divisor = GetExprValue();
                value = divisor == 0 ? value : value / divisor;
            }
            else if (c == '*')
            {
                _tokenPlace++;
                value *= GetExprValue();
            }
            else if (c == '(')
            {
                _tokenPlace++;
                value = GetExprValue();
            }
            else if (c == ')')
            {
                _tokenPlace++;
                return value;
            }
            else if (char.IsUpper(c))
                value = GetConstantValue();
            else if (char.IsLower(c))
                value = GetFunctionValue();
        }
    }

    private int GetNumericalValue()
    {
        SkipWhitespace();
        if (IsEndOfInput())
            return 0;

        char c = _input[_tokenPlace];
        if (char.IsDigit(c))
            return GetInt();
        if (char.IsUpper(c))
            return GetConstantValue();
        if (char.IsLower(c))
            return GetFunctionValue();
        if (c == '(')
        {
            _tokenPlace++;
            return GetExprValue();
        }

        throw new InvalidOperationException($"Unexpected character '{c}' in expression: {_input}");
    }

    private int GetFunctionValue()
    {
        string functionName = GetIdentifier();
        SkipWhitespace();
        ConsumeChar('(');
        string paramName = GetIdentifier();
        SkipWhitespace();
        ConsumeChar(')');

        if (paramName == "$ParentControl")
        {
            if (_parsingNode?.Parent == null)
                throw new InvalidOperationException("$ParentControl used for root node");
            paramName = _parsingNode.Parent.Id;
        }
        else if (paramName == "$Self")
        {
            paramName = _parsingNode?.Id ?? throw new InvalidOperationException("$Self without parsing node");
        }

        UiNode target = _tree!.FindNode(paramName)
            ?? throw new KeyNotFoundException($"Control '{paramName}' not found in expression: {_input}");

        return functionName switch
        {
            "getX" => target.GetIntProp("CanvasLeft"),
            "getY" => target.GetIntProp("CanvasTop"),
            "getWidth" => target.GetIntProp("Width"),
            "getHeight" => target.GetIntProp("Height"),
            "getRight" => target.GetIntProp("CanvasLeft") + target.GetIntProp("Width"),
            "getBottom" => target.GetIntProp("CanvasTop") + target.GetIntProp("Height"),
            "horizontalCenterOnParent" => HorizontalCenterOnParent(_parsingNode!),
            _ => throw new InvalidOperationException($"Unknown function {functionName} in {_input}"),
        };
    }

    private static int HorizontalCenterOnParent(UiNode node)
    {
        if (node.Parent == null)
            return node.GetIntProp("CanvasLeft");

        int parentW = node.Parent.GetIntProp("Width");
        int selfW = node.GetIntProp("Width");
        int x = (parentW - selfW) / 2;
        node.Props["CanvasLeft"] = (double)x;
        return x;
    }

    private int GetConstantValue()
    {
        string name = GetIdentifier();
        if (!_constants.TryGetValue(name, out int value))
            throw new KeyNotFoundException($"Unknown constant {name} in {_input}");
        return value;
    }

    private int GetInt()
    {
        int value = 0;
        while (!IsEndOfInput() && char.IsDigit(_input[_tokenPlace]))
        {
            value = (value * 10) + _input[_tokenPlace] - CharZero;
            _tokenPlace++;
        }

        return value;
    }

    private string GetIdentifier()
    {
        var sb = new System.Text.StringBuilder();
        while (!IsEndOfInput())
        {
            char c = _input[_tokenPlace];
            if (char.IsWhiteSpace(c))
                break;
            if (!char.IsLetterOrDigit(c) && c != '_' && c != '$' && c != '.')
                break;
            sb.Append(c);
            _tokenPlace++;
        }

        return sb.ToString();
    }

    private void SkipWhitespace()
    {
        while (!IsEndOfInput() && char.IsWhiteSpace(_input[_tokenPlace]))
            _tokenPlace++;
    }

    private void ConsumeChar(char expected)
    {
        if (_input[_tokenPlace] != expected)
            throw new InvalidOperationException($"Expected '{expected}' in {_input}");
        _tokenPlace++;
    }

    private bool IsEndOfInput() => _tokenPlace >= _input.Length;
}
