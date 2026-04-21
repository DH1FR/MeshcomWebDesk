namespace MeshcomWebDesk.Models;

/// <summary>
/// A user-defined quick-text button shown in the send bar.
/// Clicking fills the message input with <see cref="Text"/> for review before sending.
/// </summary>
public class QuickTextEntry
{
    /// <summary>Short button label (e.g. "73", "QRV?").</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Text loaded into the input field on click. Max 149 characters.</summary>
    public string Text { get; set; } = string.Empty;
}
