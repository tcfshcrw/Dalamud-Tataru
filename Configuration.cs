using System;
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
    public TargetLanguage TargetLang { get; set; } = TargetLanguage.TraditionalChinese;
    public string CustomSystemPrompt { get; set; } = string.Empty;

    // LM Studio (Local LLM)
    public string LmStudioEndpoint { get; set; } = "http://localhost:1234";
    public string LmStudioModel { get; set; } = string.Empty;
    public string LmStudioApiKey { get; set; } = string.Empty;

    // Glossary
    public bool AutoUpdateGlossary { get; set; } = false;

    // Hook Toggles
    public bool TranslateTalk { get; set; } = true;
    public bool TranslateBattleTalk { get; set; } = true;
    public bool TranslateSubtitle { get; set; } = true;

    // UI Overlay Settings
    public bool ShowOriginalText { get; set; } = false;
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
