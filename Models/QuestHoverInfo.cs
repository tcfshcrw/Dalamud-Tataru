using System;
using System.Numerics;

namespace DalamudTataru.Models;

public class QuestHoverInfo
{
    public string CleanText { get; set; } = string.Empty;
    public string? TranslatedText { get; set; }
    public bool IsPending { get; set; }
    public Vector2 MousePos { get; set; }
    public Vector2 NodePos { get; set; }
    public Vector2 NodeSize { get; set; }
    public nint NodePointer { get; set; }
    public string AddonName { get; set; } = string.Empty;
}
