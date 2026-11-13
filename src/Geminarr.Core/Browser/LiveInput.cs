using System.Text.Json;

namespace Geminarr.Core.Browser;

/// <summary>The kinds of input a person can send to the browser of the live view.</summary>
public enum LiveInputKind
{
    MouseMove,
    MouseDown,
    MouseUp,
    Wheel,
    KeyDown,
    KeyUp,
    Text,
}

/// <summary>One input event, already checked: coordinates inside the screen, a key that is a key, a text that is not endless.</summary>
public sealed record LiveInput(LiveInputKind Kind, double X = 0, double Y = 0, string Button = "left", double DeltaX = 0, double DeltaY = 0, string? Key = null, string? Text = null);

/// <summary>Reads what the page of the operator sends over the WebSocket: JSON, bounded, and refused when it is not exactly what was expected. Nothing it says reaches the browser unchecked.</summary>
public static class LiveInputParser
{
    /// <summary>A message is at most this long: an event is a few dozen bytes, a paste is the longest thing.</summary>
    public const int MaxMessageChars = 8_192;

    /// <summary>A pasted text is at most this long.</summary>
    public const int MaxTextChars = 4_096;

    /// <summary>The key names of the browser's keyboard API (<c>KeyA</c>, <c>Enter</c>, <c>ArrowLeft</c>, a single character…).</summary>
    private const int MaxKeyChars = 32;

    private static readonly HashSet<string> Buttons = ["left", "middle", "right"];

    /// <summary>The input, or null when the message is not a valid one for a screen of this size.</summary>
    public static LiveInput? Parse(string message, int width, int height)
    {
        if (message.Length is 0 or > MaxMessageChars)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(message);
            return document.RootElement.ValueKind == JsonValueKind.Object ? FromObject(document.RootElement, width, height) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static LiveInput? FromObject(JsonElement root, int width, int height)
    {
        if (Text(root, "t") is not { } type)
        {
            return null;
        }

        return type switch
        {
            "move" => Mouse(root, LiveInputKind.MouseMove, width, height),
            "down" => Mouse(root, LiveInputKind.MouseDown, width, height),
            "up" => Mouse(root, LiveInputKind.MouseUp, width, height),
            "wheel" => Wheel(root, width, height),
            "keydown" => KeyEvent(root, LiveInputKind.KeyDown),
            "keyup" => KeyEvent(root, LiveInputKind.KeyUp),
            "text" => Paste(root),
            _ => null,
        };
    }

    private static LiveInput? Mouse(JsonElement root, LiveInputKind kind, int width, int height)
    {
        if (Point(root, width, height) is not { } point || (Text(root, "button") is { } button && !Buttons.Contains(button)))
        {
            return null;
        }

        return new LiveInput(kind, point.X, point.Y, Text(root, "button") ?? "left");
    }

    private static LiveInput? Wheel(JsonElement root, int width, int height)
    {
        if (Point(root, width, height) is not { } point || Number(root, "dx") is not { } dx || Number(root, "dy") is not { } dy)
        {
            return null;
        }

        return new LiveInput(LiveInputKind.Wheel, point.X, point.Y, DeltaX: Math.Clamp(dx, -2_000, 2_000), DeltaY: Math.Clamp(dy, -2_000, 2_000));
    }

    private static LiveInput? KeyEvent(JsonElement root, LiveInputKind kind) =>
        Text(root, "key") is { Length: > 0 and <= MaxKeyChars } key && !key.Any(char.IsControl) ? new LiveInput(kind, Key: key) : null;

    private static LiveInput? Paste(JsonElement root) =>
        Text(root, "text") is { Length: > 0 and <= MaxTextChars } text ? new LiveInput(LiveInputKind.Text, Text: text) : null;

    private static (double X, double Y)? Point(JsonElement root, int width, int height) =>
        Number(root, "x") is { } x && Number(root, "y") is { } y && x >= 0 && y >= 0 && x <= width && y <= height ? (x, y) : null;

    private static string? Text(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static double? Number(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number) ? number : null;
}
