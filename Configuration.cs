using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Configuration;
using Dalamud.Plugin;
using DalamudTataru.Models;

namespace DalamudTataru;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // Translation Engine
    public TranslationEngine PrimaryEngine { get; set; } = TranslationEngine.Gemini;
    public bool EnableFallback { get; set; } = true;
    public bool EnableYoudaoFallback
    {
        get => EnableFallback;
        set => EnableFallback = value;
    }

    // Gemini API
    public string GeminiApiKey { get; set; } = string.Empty;
    public string GeminiModel { get; set; } = "gemini-3.8-flash";
    public SourceLanguage SourceLang { get; set; } = SourceLanguage.Auto;
    public TargetLanguage TargetLang { get; set; } = TargetLanguage.TraditionalChinese;
    public string CustomSystemPrompt { get; set; } = string.Empty;

    // LM Studio (Local LLM)
    public string LmStudioEndpoint { get; set; } = "http://localhost:1234";
    public string LmStudioModel { get; set; } = string.Empty;
    public string LmStudioApiKey { get; set; } = string.Empty;
    public float LmStudioTemperature { get; set; } = 0.2f;

    // Glossary & Custom Terminology
    public bool AutoUpdateGlossary { get; set; } = true;
    public bool ShowStartupNotification { get; set; } = true;
    public bool EnableCustomGlossary { get; set; } = true;
    private List<CustomGlossaryEntry>? customGlossaryEntries;
    public List<CustomGlossaryEntry> CustomGlossaryEntries
    {
        get => customGlossaryEntries ??= new();
        set => customGlossaryEntries = value ?? new();
    }

    // Hook Toggles
    public bool TranslateTalk { get; set; } = true;
    public bool TranslateBattleTalk { get; set; } = true;
    public bool TranslateSubtitle { get; set; } = true;

    // UI Overlay Settings
    public bool ReplaceGameTalkAddon { get; set; } = false;
    public float ReplaceFontScale { get; set; } = 1.0f;
    public bool ReplaceUseNativeBubble { get; set; } = false;
    public bool ReplaceUseParchmentTheme { get; set; } = true;
    public bool ReplaceAutoSyncSize { get; set; } = false;
    public Vector2 ReplaceOverlayPosition { get; set; } = new Vector2(400, 750);
    public Vector2 ReplaceOverlaySize { get; set; } = new Vector2(650, 180);
    public bool ReplaceFollowTalkAddonPosition { get; set; } = true;
    public Vector2 ReplacePositionOffset { get; set; } = Vector2.Zero;
    public Vector2 ReplaceSizeOffset { get; set; } = Vector2.Zero;
    public bool ShowOriginalText { get; set; } = false;
    public bool ReplaceShowOriginalText { get; set; } = true;
    public float OriginalFontScaleRatio { get; set; } = 0.75f;
    public bool ShowSpeaker { get; set; } = true;
    public float FontScale { get; set; } = 1.0f;
    public float WindowOpacity { get; set; } = 0.85f;
    public float AutoHideSeconds { get; set; } = 8.0f;
    public bool ClickThrough { get; set; } = false;
    public bool LockOverlay { get; set; } = false;
    public Vector4 BackgroundColor { get; set; } = new Vector4(0.08f, 0.08f, 0.1f, 0.85f);
    public Vector4 SpeakerColor { get; set; } = new Vector4(1.0f, 0.85f, 0.35f, 1.0f);
    public Vector4 TextColor { get; set; } = new Vector4(0.95f, 0.95f, 0.95f, 1.0f);
    public Vector4 OriginalTextColor { get; set; } = new Vector4(0.65f, 0.65f, 0.65f, 0.85f);
    public Vector2 CustomOverlayPosition { get; set; } = new Vector2(400, 750);
    public Vector2 CustomOverlaySize { get; set; } = new Vector2(600, 140);

    [NonSerialized]
    private IDalamudPluginInterface? pluginInterface;

    public void Initialize(IDalamudPluginInterface pluginInterface)
    {
        this.pluginInterface = pluginInterface;
    }

    public void Save()
    {
        this.pluginInterface?.SavePluginConfig(this);
    }
}
