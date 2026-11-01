using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Windowing;
using DalamudTataru.Models;
using DalamudTataru.Services;
using Dalamud.Bindings.ImGui;

namespace DalamudTataru.Windows;

public class ConfigWindow : Window
{
    private readonly Configuration config;
    private readonly GeminiTranslator translator;
    private readonly LmStudioTranslator lmStudio;
    private readonly YoudaoTranslator youdao;
    private readonly GoogleTranslator google;
    private readonly SubtitleOverlayWindow overlayWindow;
    private readonly DialogueReplaceOverlayWindow replaceOverlayWindow;
    private readonly DialogueWatcher dialogueWatcher;
    private readonly GlossaryManager glossary;
    private readonly IFontHandle? fontHandle;

    private bool showApiKey = false;
    private string testStatus = string.Empty;
    private bool isTesting = false;
    private bool isFetchingModels = false;
    private bool isFetchingLmModels = false;
    private bool isUpdatingGlossary = false;
    private string glossaryStatus = string.Empty;

    private string promptEditorBuffer = string.Empty;
    private bool promptEditorLoaded = false;
    private int selectedPromptPreset = 0;
    private string promptTestSpeaker = "ヤ・シュトラ";
    private string promptTestText = "お前が光の戦士か……。ウリエンジェから話は聞いている。";
    private string promptTestResult = string.Empty;
    private bool isTestingCustomPrompt = false;

    private static readonly string[] PromptPresetTemplates = new[]
    {
        // 0: Tataru Native Style
        "You are an expert game localizer for Final Fantasy XIV (FFXIV). Translate the {sourceLang} dialogue into natural, immersive {targetLang}. Match character tone, personality, and honorifics. Return a JSON object with keys 'name' and 'text'.",

        // 1: Echoglossian Style
        "You are a professional game translator for Final Fantasy XIV. Translate the following {sourceLanguage} dialogue into {targetLanguage}. Preserve character persona, fantasy RPG tone, and nuances without adding conversational filler. Return a JSON object with keys 'name' and 'text'.",

        // 2: EN -> ZH Fantasy Localization
        "你是一位精通《Final Fantasy XIV》（最終幻想14）的資深在地化專家。請將遊戲中的英文對話翻譯成自然、雅緻、符合奇幻小說體裁的{targetLang}。\n翻譯原則：\n1. 針對古典/早期近代英語（如 Urianger 之 thou/thee/thine/doth 與文言倒裝句）轉譯為典雅古風漢語。\n2. 針對各地區方言與俚語（如利姆薩·羅敏薩海盜黑話、妖精族Pixie語調）轉譯為鮮活道地的語氣。\n3. 嚴格分離說話者與台詞，保留角色口吻與性格。\n4. 輸出符合 JSON 格式的物件，包含 'name'（中文名）與 'text'（中文台詞）。",

        // 3: Sakura / Light Novel Style
        "你是一位精通 ACG 與日系奇幻輕小說翻譯的資深在地化專家。請將《Final Fantasy XIV》的{sourceLang}遊戲台詞翻譯成文風生動流暢、充滿沉浸感的{targetLang}。請忠實還原角色的性格特徵（如傲嬌、元氣、沉穩或霸道），不可擅自省略感嘆詞或語氣助詞。請輸出標準 JSON 物件，包含 'name' 與 'text'。"
    };

    private string[] models = new[]
    {
        "gemini-3.8-flash",
        "gemini-3.7-flash",
        "gemini-3.6-flash",
        "gemini-3.5-flash",
        "gemini-3.1-pro-preview",
        "gemini-3.1-flash-lite",
        "gemini-flash-latest",
        "自訂模型 (Custom)"
    };

    public ConfigWindow(Configuration config, GeminiTranslator translator, LmStudioTranslator lmStudio, YoudaoTranslator youdao, GoogleTranslator google, SubtitleOverlayWindow overlayWindow, DialogueReplaceOverlayWindow replaceOverlayWindow, DialogueWatcher dialogueWatcher, GlossaryManager glossary, IFontHandle? fontHandle = null)
        : base("Tataru Native Translator 設定###TataruConfigWindow")
    {
        this.config = config;
        this.translator = translator;
        this.lmStudio = lmStudio;
        this.youdao = youdao;
        this.google = google;
        this.overlayWindow = overlayWindow;
        this.replaceOverlayWindow = replaceOverlayWindow;
        this.dialogueWatcher = dialogueWatcher;
        this.glossary = glossary;
        this.fontHandle = fontHandle;

        Size = new Vector2(620, 660);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        IDisposable? fontScope = null;
        if (fontHandle?.Available == true)
        {
            fontScope = fontHandle.Push();
        }

        try
        {
            if (ImGui.BeginTabBar("TataruSettingsTabs"))
            {
                if (ImGui.BeginTabItem("AI 翻譯設定"))
                {
                    DrawTranslationSettings();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("AI 提示詞編輯 (Prompt)"))
                {
                    DrawPromptEditor();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("字幕與視窗外觀"))
                {
                    DrawOverlaySettings();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("關於 Tataru"))
                {
                    DrawAbout();
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }
        }
        finally
        {
            fontScope?.Dispose();
        }
    }

    private void DrawTranslationSettings()
    {
        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "主要翻譯引擎設定");
        ImGui.Separator();
        ImGui.Spacing();

        // 1. Engine Selector
        ImGui.Text("主要翻譯引擎：");
        int currentEngineIdx = (int)config.PrimaryEngine;
        string[] engineNames = new[]
        {
            "Google Gemini AI (推薦 - 翻譯自然沉浸、符合人物性格)",
            "LM Studio 本地大模型 (隱私安全、無網路依賴、需啟動本地伺服器)",
            "有道翻譯 (免金鑰、無額度限制)",
            "Google 翻譯 (免金鑰、無額度限制、連線穩定)"
        };

        ImGui.SetNextItemWidth(-1);
        if (ImGui.Combo("##PrimaryEngine", ref currentEngineIdx, engineNames, engineNames.Length))
        {
            config.PrimaryEngine = (TranslationEngine)currentEngineIdx;
            config.Save();
            translator.ClearCache();
        }

        ImGui.Spacing();

        // 2. Engine Specific Settings
        if (config.PrimaryEngine == TranslationEngine.Gemini)
        {
            ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "Google Gemini API 參數設定");
            ImGui.Spacing();

            // API Key
            ImGui.Text("Gemini API Key：");
            var apiKey = config.GeminiApiKey;
            var flags = showApiKey ? ImGuiInputTextFlags.None : ImGuiInputTextFlags.Password;
            float showBtnW = 75;
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - showBtnW - 8);
            if (ImGui.InputText("##GeminiApiKey", ref apiKey, 200, flags))
            {
                config.GeminiApiKey = apiKey;
                config.Save();
            }
            ImGui.SameLine();
            if (ImGui.Button(showApiKey ? "隱藏##gem" : "顯示##gem", new Vector2(showBtnW, 0)))
            {
                showApiKey = !showApiKey;
            }

            ImGui.TextDisabled("可至 Google AI Studio (aistudio.google.com) 免費申請 API Key");
            ImGui.Spacing();

            // Model Selection
            ImGui.Text("模型選擇：");
            int currentModelIdx = Array.IndexOf(models, config.GeminiModel);
            bool isCustom = currentModelIdx < 0 || currentModelIdx == models.Length - 1;
            if (isCustom) currentModelIdx = models.Length - 1;

            float syncModelBtnW = 140;
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - syncModelBtnW - 8);
            if (ImGui.Combo("##GeminiModelCombo", ref currentModelIdx, models, models.Length))
            {
                if (currentModelIdx < models.Length - 1)
                {
                    config.GeminiModel = models[currentModelIdx];
                    config.Save();
                }
            }
            ImGui.SameLine();
            if (ImGui.Button(isFetchingModels ? "同步中..." : "從 API 同步最新", new Vector2(syncModelBtnW, 0)) && !isFetchingModels)
            {
                RefreshModelsFromApi();
            }

            if (isCustom)
            {
                ImGui.Text("自訂模型名稱：");
                var customModel = config.GeminiModel;
                ImGui.SetNextItemWidth(-1);
                if (ImGui.InputText("##CustomGeminiModel", ref customModel, 100))
                {
                    config.GeminiModel = customModel.Trim();
                    config.Save();
                }
            }

            ImGui.TextDisabled("推薦使用 gemini-3.8-flash 或 gemini-3.5-flash：極速回應、語氣自然且免費額度充足。");
        }
        else if (config.PrimaryEngine == TranslationEngine.LmStudio)
        {
            ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "LM Studio 本地伺服器連線設定");
            ImGui.Spacing();

            ImGui.Text("伺服器位址 (Endpoint)：");
            var endpoint = config.LmStudioEndpoint;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText("##LmEndpoint", ref endpoint, 150))
            {
                config.LmStudioEndpoint = endpoint.Trim();
                config.Save();
            }
            ImGui.TextDisabled("預設為 http://localhost:1234。若 LM Studio 在本機執行，保持預設即可。");
            ImGui.Spacing();

            ImGui.Text("模型識別名稱 (Model ID)：");
            var lmModel = config.LmStudioModel;
            float syncLmBtnW = 150;
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - syncLmBtnW - 8);
            if (ImGui.InputText("##LmModel", ref lmModel, 150))
            {
                config.LmStudioModel = lmModel.Trim();
                config.Save();
            }
            ImGui.SameLine();
            if (ImGui.Button(isFetchingLmModels ? "連線同步中..." : "從 LM Studio 同步", new Vector2(syncLmBtnW, 0)) && !isFetchingLmModels)
            {
                RefreshLmModelsFromApi();
            }
            ImGui.TextDisabled("留空則自動使用 LM Studio 當前已載入的模型 (local-model)。");
            ImGui.Spacing();

            ImGui.Text("API Key (選填)：");
            var lmKey = config.LmStudioApiKey;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText("##LmApiKey", ref lmKey, 100))
            {
                config.LmStudioApiKey = lmKey.Trim();
                config.Save();
            }
            ImGui.TextDisabled("一般情況下 LM Studio 本地伺服器無需填寫 API Key。");
            ImGui.Spacing();

            var lmTemp = config.LmStudioTemperature;
            if (ImGui.SliderFloat("模型採樣溫度 (Temperature)", ref lmTemp, 0.0f, 1.0f, "%.2f"))
            {
                config.LmStudioTemperature = Math.Clamp(lmTemp, 0.0f, 1.0f);
                config.Save();
            }
            ImGui.TextDisabled("控制輸出隨機度（預設 0.20）。若模型容易胡言亂語或過度腦補，建議調低至 0.00 ~ 0.10（最保守精準）。");
            ImGui.Spacing();

            ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.2f, 1.0f), "提示：請在 LM Studio 的「Local Server」頁面點擊「Start Server」啟動本地 API。");
        }
        else if (config.PrimaryEngine == TranslationEngine.Youdao)
        {
            ImGui.TextColored(new Vector4(0.3f, 0.9f, 0.4f, 1.0f), "✔ 目前已啟用「有道翻譯」作為主要引擎（免 API Key，直接即開即用）。");
            ImGui.Spacing();
            if (ImGui.CollapsingHeader("Gemini API 選填設定 (供切換或備用)"))
            {
                ImGui.Text("Gemini API Key：");
                var apiKey = config.GeminiApiKey;
                var flags = showApiKey ? ImGuiInputTextFlags.None : ImGuiInputTextFlags.Password;
                float showBtnW = 75;
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - showBtnW - 8);
                if (ImGui.InputText("##secyd_key", ref apiKey, 200, flags))
                {
                    config.GeminiApiKey = apiKey;
                    config.Save();
                }
                ImGui.SameLine();
                if (ImGui.Button(showApiKey ? "隱藏##secyd" : "顯示##secyd", new Vector2(showBtnW, 0)))
                {
                    showApiKey = !showApiKey;
                }
            }
        }
        else if (config.PrimaryEngine == TranslationEngine.Google)
        {
            ImGui.TextColored(new Vector4(0.3f, 0.9f, 0.4f, 1.0f), "✔ 目前已啟用「Google 翻譯」作為主要引擎（免 API Key，穩定迅速）。");
            ImGui.Spacing();
            if (ImGui.CollapsingHeader("Gemini API 選填設定 (供切換或備用)"))
            {
                ImGui.Text("Gemini API Key：");
                var apiKey = config.GeminiApiKey;
                var flags = showApiKey ? ImGuiInputTextFlags.None : ImGuiInputTextFlags.Password;
                float showBtnW = 75;
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - showBtnW - 8);
                if (ImGui.InputText("##secgg_key", ref apiKey, 200, flags))
                {
                    config.GeminiApiKey = apiKey;
                    config.Save();
                }
                ImGui.SameLine();
                if (ImGui.Button(showApiKey ? "隱藏##secgg" : "顯示##secgg", new Vector2(showBtnW, 0)))
                {
                    showApiKey = !showApiKey;
                }
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Source Language
        ImGui.Text("來源語言 (遊戲客戶端語言)：");
        int sourceLangIdx = (int)config.SourceLang;
        string[] sourceLanguages = {
            "自動判斷 (Auto - 日文/英文皆支援)",
            "日文 (Japanese - 日服/預設)",
            "英文 (English - 國際服/美歐專用)"
        };
        ImGui.SetNextItemWidth(-1);
        if (ImGui.Combo("##SourceLangCombo", ref sourceLangIdx, sourceLanguages, sourceLanguages.Length))
        {
            config.SourceLang = (SourceLanguage)sourceLangIdx;
            config.Save();
            translator.ClearCache();
        }
        ImGui.TextDisabled("支援遊玩日服（日文）或美歐國際服（英文）。設為「自動判斷」時，引擎會自動辨識並翻譯為中文。");
        ImGui.Spacing();

        // Target Language
        ImGui.Text("目標語言：");
        int targetLangIdx = (int)config.TargetLang;
        string[] languages = { "繁體中文 (Traditional Chinese)", "简体中文 (Simplified Chinese)" };
        ImGui.SetNextItemWidth(-1);
        if (ImGui.Combo("##TargetLangCombo", ref targetLangIdx, languages, languages.Length))
        {
            config.TargetLang = (TargetLanguage)targetLangIdx;
            config.Save();
            translator.ClearCache();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Fallback Toggles
        var enableFallback = config.EnableFallback;
        if (ImGui.Checkbox("啟用 100% 免費備用防線 (Fallback: 有道 -> Google)", ref enableFallback))
        {
            config.EnableFallback = enableFallback;
            config.Save();
        }
        ImGui.TextDisabled("備援防線僅使用【有道翻譯】與【Google 翻譯】等完全免費公開服務，絕不呼叫付費 API，亦不消耗任何 API 額度或點數！");
        ImGui.Spacing();

        ImGui.Separator();
        ImGui.Spacing();

        // Test Translation Buttons (2x2 grid)
        ImGui.Text("API 與連線測試：");
        float btnW = (ImGui.GetContentRegionAvail().X - 10) / 2;
        if (ImGui.Button(isTesting ? "測試中..." : "測試 Gemini", new Vector2(btnW, 32)) && !isTesting)
        {
            RunApiTest();
        }
        ImGui.SameLine();
        if (ImGui.Button(isTesting ? "測試中..." : "測試 LM Studio", new Vector2(btnW, 32)) && !isTesting)
        {
            RunLmStudioTest();
        }

        if (ImGui.Button(isTesting ? "測試中..." : "測試有道翻譯", new Vector2(btnW, 32)) && !isTesting)
        {
            RunYoudaoTest();
        }
        ImGui.SameLine();
        if (ImGui.Button(isTesting ? "測試中..." : "測試 Google 翻譯", new Vector2(btnW, 32)) && !isTesting)
        {
            RunGoogleTest();
        }

        if (!string.IsNullOrEmpty(testStatus))
        {
            ImGui.Spacing();
            ImGui.TextWrapped(testStatus);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Addon Toggles
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "翻譯監聽範圍");

        var talk = config.TranslateTalk;
        if (ImGui.Checkbox("主線 / 支線 NPC 對話 (Talk)", ref talk))
        {
            config.TranslateTalk = talk;
            config.Save();
        }

        var subtitle = config.TranslateSubtitle;
        if (ImGui.Checkbox("過場動畫與 CG 字幕 (Subtitle)", ref subtitle))
        {
            config.TranslateSubtitle = subtitle;
            config.Save();
        }

        var battle = config.TranslateBattleTalk;
        if (ImGui.Checkbox("副本首領喊話與戰鬥對話 (BattleTalk)", ref battle))
        {
            config.TranslateBattleTalk = battle;
            config.Save();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Glossary & Dictionaries
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "Tataru 專有名詞辭典與文本庫");
        ImGui.TextDisabled($"目前已載入：{glossary.NameMapCount} 筆 NPC/專有名詞對照（含英文名詞 {glossary.EnNameMapCount} 筆），繁中校正 {glossary.PostReplaceChtCount} 筆，簡中校正 {glossary.PostReplaceChsCount} 筆。");
        ImGui.Spacing();

        var autoUpdate = config.AutoUpdateGlossary;
        if (ImGui.Checkbox("啟動遊戲/插件時自動從 Tataru-Assistant 檢查並更新文本庫", ref autoUpdate))
        {
            config.AutoUpdateGlossary = autoUpdate;
            config.Save();
        }

        var showStartup = config.ShowStartupNotification;
        if (ImGui.Checkbox("每次啟動時提示現在版本 引擎模型 與自動更新訊息 顯示同一行內", ref showStartup))
        {
            config.ShowStartupNotification = showStartup;
            config.Save();
        }
        ImGui.TextDisabled("啟用後，插件啟動時會在遊戲聊天室單行輸出版本號、目前翻譯引擎與模型、以及文本庫更新訊息。");

        if (ImGui.Button(isUpdatingGlossary ? "文本更新中..." : "立即從 Tataru-Assistant 雲端更新文本庫", new Vector2(-1, 32)) && !isUpdatingGlossary)
        {
            UpdateGlossaryFromCloud();
        }

        if (!string.IsNullOrEmpty(glossaryStatus))
        {
            ImGui.Spacing();
            ImGui.TextWrapped(glossaryStatus);
        }
    }

    private void DrawPromptEditor()
    {
        if (!promptEditorLoaded)
        {
            promptEditorBuffer = config.CustomSystemPrompt ?? string.Empty;
            promptEditorLoaded = true;
        }

        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "AI 系統提示詞編輯器 (System Prompt Editor)");
        ImGui.Separator();
        ImGui.Spacing();

        // Status indicator
        bool isCustom = !string.IsNullOrWhiteSpace(config.CustomSystemPrompt);
        if (isCustom)
        {
            ImGui.TextColored(new Vector4(0.3f, 0.9f, 0.4f, 1.0f), "● 目前狀態：已啟用自訂提示詞 (Custom Prompt Active)");
        }
        else
        {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "○ 目前狀態：使用 Tataru 官方預設提示詞 (Default)");
        }
        ImGui.TextDisabled("提示詞用於指導 Gemini 或本地 LM Studio 模型如何理解遊戲語境、角色性格與輸出格式。");
        ImGui.Spacing();

        // 1. Comparison & Guide Header
        if (ImGui.CollapsingHeader("Tataru Assistant 與 Echoglossian 提示詞架構深度比較與指南"))
        {
            ImGui.Indent(10f);
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(1.0f, 0.85f, 0.35f, 1.0f), "【核心設計哲學差異】");
            ImGui.BulletText("Tataru Assistant (塔塔露助手)：\n" +
                             "  • 結構化規範：強調 JSON 輸出（{\"name\": \"...\", \"text\": \"...\"}），強制將說話者姓名與內文分開。\n" +
                             "  • 優勢：原生泡泡框覆蓋時，名字不會污染台詞；且能與專有名詞辭典（jp-ch-name）精確對接。\n" +
                             "  • 語氣保真：深度要求日語敬語/謙讓語、第一人稱、角色個性與國服/繁中譯名一致性。");
            ImGui.Spacing();
            ImGui.BulletText("Echoglossian：\n" +
                             "  • 自由樣板與純文本：原生採用變數插值（如 {targetLanguage}），偏向直接輸出譯文。\n" +
                             "  • 優勢：適用於全球多國語言互譯（JP/EN/FR/DE -> 任意語言），相容不支援 JSON Mode 的輕量模型。\n" +
                             "  • 差異：通常缺乏對說話者名字的獨立結構化解析，古英語/敬語細節需手動寫入提示詞。");
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.3f, 0.9f, 0.4f, 1.0f), "【本插件的融合優勢】");
            ImGui.TextWrapped("本插件完美整合兩者精華：在保留 Tataru 結構化解析與辭典特點的同時，全面支援動態樣板變數（{sourceLang}、{targetLang}、{targetLanguage}），並強化英翻中古風英語與俚語轉譯！");
            ImGui.Spacing();
            ImGui.Unindent(10f);
            ImGui.Separator();
            ImGui.Spacing();
        }

        // 2. Preset Selector
        ImGui.Text("快速範本選擇 (Presets)：");
        string[] presetLabels = new[]
        {
            "Tataru Assistant 原生風格 (結構化 JSON、角色語氣保真 - 推薦)",
            "Echoglossian 國際風格 (通用變數、RPG氛圍、簡約指令)",
            "英翻中古風小說風格 (專攻國際服古英語/Urianger/海盜黑話)",
            "Sakura / 輕小說生動風格 (二次元沉浸語氣、細膩情感)"
        };

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 140 - 8);
        ImGui.Combo("##PromptPresetCombo", ref selectedPromptPreset, presetLabels, presetLabels.Length);
        ImGui.SameLine();
        if (ImGui.Button("載入範本", new Vector2(140, 0)))
        {
            if (selectedPromptPreset >= 0 && selectedPromptPreset < PromptPresetTemplates.Length)
            {
                promptEditorBuffer = PromptPresetTemplates[selectedPromptPreset];
            }
        }

        ImGui.Spacing();

        // 3. Variables Helper
        ImGui.Text("動態插值變數（點擊快速插入）：");
        if (ImGui.Button("{targetLang}##btn"))
        {
            promptEditorBuffer += "{targetLang}";
        }
        ImGui.SameLine();
        if (ImGui.Button("{targetLanguage}##btn"))
        {
            promptEditorBuffer += "{targetLanguage}";
        }
        ImGui.SameLine();
        if (ImGui.Button("{sourceLang}##btn"))
        {
            promptEditorBuffer += "{sourceLang}";
        }
        ImGui.SameLine();
        if (ImGui.Button("{sourceLanguage}##btn"))
        {
            promptEditorBuffer += "{sourceLanguage}";
        }
        ImGui.TextDisabled("變數將在送出翻譯時自動替換為當前設定的來源/目標語言名稱（如 Traditional Chinese、日文、英文）。");
        ImGui.Spacing();

        // 4. Editor TextArea
        ImGui.Text("自訂 System Prompt 內容：");
        ImGui.InputTextMultiline("##CustomPromptTextArea", ref promptEditorBuffer, 4000, new Vector2(-1, 200));
        ImGui.Spacing();

        // 5. Actions
        float actionBtnW = (ImGui.GetContentRegionAvail().X - 16) / 3;
        if (ImGui.Button("儲存提示詞設定", new Vector2(actionBtnW, 32)))
        {
            config.CustomSystemPrompt = promptEditorBuffer.Trim();
            config.Save();
            translator.ClearCache();
            promptTestResult = "提示詞已成功儲存並生效！快取已清空。";
        }
        ImGui.SameLine();
        if (ImGui.Button("還原為官方預設", new Vector2(actionBtnW, 32)))
        {
            promptEditorBuffer = string.Empty;
            config.CustomSystemPrompt = string.Empty;
            config.Save();
            translator.ClearCache();
            promptTestResult = "已還原為官方預設提示詞！快取已清空。";
        }
        ImGui.SameLine();
        if (ImGui.Button("清空編輯器", new Vector2(actionBtnW, 32)))
        {
            promptEditorBuffer = string.Empty;
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // 6. Live Testing Section
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "即時提示詞效果測試");
        ImGui.TextDisabled("可輸入測試台詞，驗證目前編輯器內的提示詞翻譯效果（無需先行儲存）：");
        ImGui.Spacing();

        // Sample preset switchers for test
        ImGui.Text("快速測試範例：");
        if (ImGui.SmallButton("載入日文測試例 (雅·修特拉)"))
        {
            promptTestSpeaker = "ヤ・シュトラ";
            promptTestText = "お前が光の戦士か……。ウリエンジェから話は聞いている。";
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("載入英文古語測試例 (于里昂熱 - Urianger)"))
        {
            promptTestSpeaker = "Urianger";
            promptTestText = "Thou hast ever been a beacon of hope in our darkest hour. Pray, let us speak anon.";
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("載入海盜黑話測試例 (利姆薩)"))
        {
            promptTestSpeaker = "Baderon";
            promptTestText = "Well met, adventurer! Lookin' for a spot o' work, or just a pint o' ale to wet yer whistle?";
        }

        ImGui.Spacing();
        ImGui.Text("測試說話者 (Speaker)：");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##PromptTestSpeaker", ref promptTestSpeaker, 100);

        ImGui.Text("測試台詞內文 (Text)：");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextMultiline("##PromptTestText", ref promptTestText, 500, new Vector2(-1, 60));
        ImGui.Spacing();

        if (ImGui.Button(isTestingCustomPrompt ? "測試翻譯中..." : "使用目前提示詞進行測試翻譯", new Vector2(-1, 32)) && !isTestingCustomPrompt)
        {
            RunPromptTest();
        }

        if (!string.IsNullOrEmpty(promptTestResult))
        {
            ImGui.Spacing();
            ImGui.TextWrapped(promptTestResult);
        }

        ImGui.Spacing();
    }

    private void RunPromptTest()
    {
        if (isTestingCustomPrompt) return;
        isTestingCustomPrompt = true;
        promptTestResult = "正在套用當前編輯之提示詞進行測試翻譯...";

        var promptToUse = promptEditorBuffer.Trim();
        var prevPrompt = config.CustomSystemPrompt;
        config.CustomSystemPrompt = promptToUse;

        Task.Run(async () =>
        {
            try
            {
                (string spk, string txt) res;
                if (config.PrimaryEngine == TranslationEngine.LmStudio)
                {
                    res = await lmStudio.TranslateAsync(promptTestSpeaker, promptTestText, config.TargetLang);
                }
                else
                {
                    res = await translator.TranslateWithGeminiCoreAsync(promptTestSpeaker, promptTestText);
                }
                promptTestResult = $"【解析結果】\n說話者：{res.spk}\n譯文：{res.txt}";
            }
            catch (Exception ex)
            {
                promptTestResult = $"【測試失敗】: {ex.Message}";
            }
            finally
            {
                config.CustomSystemPrompt = prevPrompt;
                isTestingCustomPrompt = false;
            }
        });
    }

    private void DrawOverlaySettings()
    {
        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "字幕顯示項目");
        ImGui.Separator();
        ImGui.Spacing();

        if (config.ReplaceGameTalkAddon)
        {
            var replaceFontScale = config.ReplaceFontScale;
            if (ImGui.SliderFloat("文字縮放倍率 (覆蓋模式專屬)", ref replaceFontScale, 0.5f, 3.0f, "%.2f 倍"))
            {
                config.ReplaceFontScale = Math.Clamp(replaceFontScale, 0.5f, 3.0f);
                config.Save();
            }
            ImGui.TextDisabled("目前已啟用覆蓋模式，此處調整覆蓋模式專屬文字尺寸（與獨立模式互不影響）。");
        }
        else
        {
            var fontScale = config.FontScale;
            if (ImGui.SliderFloat("文字縮放倍率 (獨立字幕模式)", ref fontScale, 0.5f, 3.0f, "%.2f 倍"))
            {
                config.FontScale = Math.Clamp(fontScale, 0.5f, 3.0f);
                config.Save();
            }
            ImGui.TextDisabled("直接放大或縮小獨立字幕文字尺寸（預設 1.00 倍）。");
        }
        ImGui.Spacing();

        var opacity = config.WindowOpacity;
        if (ImGui.SliderFloat("視窗背景不透明度 (Opacity)", ref opacity, 0.0f, 1.0f, "%.2f"))
        {
            config.WindowOpacity = Math.Clamp(opacity, 0.0f, 1.0f);
            config.Save();
        }
        ImGui.TextDisabled("調整字幕底色深淺：0.00 為全透明（純文字懸浮無黑底），1.00 為純黑完全不透明。");
        ImGui.Spacing();

        if (!config.ReplaceGameTalkAddon)
        {
            var showSpeaker = config.ShowSpeaker;
            if (ImGui.Checkbox("顯示說話者名稱 (僅獨立字幕模式生效)", ref showSpeaker))
            {
                config.ShowSpeaker = showSpeaker;
                config.Save();
            }
            ImGui.TextDisabled("在獨立字幕框模式下顯示說話者名稱。啟用「原生對話框覆蓋模式」時將自動忽略（由遊戲原版姓名條呈現）。");
            ImGui.Spacing();

            var showOriginal = config.ShowOriginalText;
            if (ImGui.Checkbox("顯示日文原文對照 (獨立字幕模式)", ref showOriginal))
            {
                config.ShowOriginalText = showOriginal;
                config.Save();
            }
            ImGui.TextDisabled("在獨立字幕視窗中顯示日文原文對照。");

            if (config.ShowOriginalText)
            {
                var origRatio = config.OriginalFontScaleRatio;
                if (ImGui.SliderFloat("日文原文縮放比例 (相對於譯文)##standaloneOrigRatio", ref origRatio, 0.30f, 1.00f, "%.2f 倍"))
                {
                    config.OriginalFontScaleRatio = Math.Clamp(origRatio, 0.30f, 1.00f);
                    config.Save();
                }
                ImGui.TextDisabled("專門縮小日文原文對照字體（預設 0.75 倍）。");
            }
        }

        ImGui.Spacing();
        var replaceTalk = config.ReplaceGameTalkAddon;
        if (ImGui.Checkbox("原生對話框覆蓋模式：覆蓋遊戲對話框位置並隱藏原版泡泡框", ref replaceTalk))
        {
            config.ReplaceGameTalkAddon = replaceTalk;
            config.Save();
            if (replaceTalk)
            {
                overlayWindow.IsOpen = false;
                overlayWindow.IsConfigOpen = false;
                replaceOverlayWindow.IsConfigOpen = true;
                replaceOverlayWindow.IsOpen = true;
            }
            else
            {
                replaceOverlayWindow.IsOpen = false;
                replaceOverlayWindow.IsConfigOpen = false;
                overlayWindow.IsConfigOpen = true;
                overlayWindow.IsOpen = true;
            }
        }
        ImGui.TextDisabled("啟用後，遊戲原生的 NPC 對話泡泡將被完全隱藏，由 Tataru 字幕視窗取而代之覆蓋於原位置。對話結束時視窗會即刻消失。依然可正常滑鼠點擊或空白鍵推進對話。");

        if (config.ReplaceGameTalkAddon)
        {
            ImGui.Spacing();
            ImGui.Indent();

            var replaceShowOriginal = config.ReplaceShowOriginalText;
            if (ImGui.Checkbox("在覆蓋模式下保留原文輸出 (日文雙語對照)", ref replaceShowOriginal))
            {
                config.ReplaceShowOriginalText = replaceShowOriginal;
                config.Save();
            }
            ImGui.TextDisabled("啟用後，覆蓋視窗將同時顯示中文譯文與日文原文；若取消勾選，則僅輸出中文譯文（為對話框節省空間，防止長句溢出）。");

            if (config.ReplaceShowOriginalText)
            {
                var origRatio = config.OriginalFontScaleRatio;
                if (ImGui.SliderFloat("日文原文縮放比例 (相對於譯文)##replaceOrigRatio", ref origRatio, 0.30f, 1.00f, "%.2f 倍"))
                {
                    config.OriginalFontScaleRatio = Math.Clamp(origRatio, 0.30f, 1.00f);
                    config.Save();
                }
                ImGui.TextDisabled("專門縮小日文原文對照字體（預設 0.75 倍）。當泡泡框空間有限或長句過多時，調小此數值可大幅節省空間！");
            }
            ImGui.Spacing();

            ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "泡泡框風格選擇 (勾選切換)");
            ImGui.Separator();
            ImGui.Spacing();

            var useParchment = config.ReplaceUseParchmentTheme && !config.ReplaceUseNativeBubble;
            if (ImGui.Checkbox("方案 A：Tataru 仿原版羊皮紙風格 (象牙米白底色 + 深棕外框 + 自由拉伸尺寸)", ref useParchment))
            {
                if (useParchment)
                {
                    config.ReplaceUseParchmentTheme = true;
                    config.ReplaceUseNativeBubble = false;
                }
                else
                {
                    config.ReplaceUseParchmentTheme = false;
                }
                config.Save();
            }
            ImGui.TextDisabled("高仿 FFXIV 遊戲原版對話泡泡！擁有經典米白羊皮紙底色、復古外框與深色字體，且視窗可自由任意縮放大小，不怕雙語行數過多溢出。");

            var useNative = config.ReplaceUseNativeBubble;
            if (ImGui.Checkbox("方案 B：保留遊戲原生泡泡框底圖 (100% 原始對話泡泡與跳動金箭頭，僅隱藏遊戲內文)", ref useNative))
            {
                if (useNative)
                {
                    config.ReplaceUseNativeBubble = true;
                    config.ReplaceUseParchmentTheme = false;
                }
                else
                {
                    config.ReplaceUseNativeBubble = false;
                }
                config.Save();
            }
            ImGui.TextDisabled("完整保留遊戲原生繪製的泡泡底圖與翻頁游標，僅精準隱藏日文文字節點，由 Tataru 以透明背景直接覆蓋深色中文譯文。");

            if (!config.ReplaceUseParchmentTheme && !config.ReplaceUseNativeBubble)
            {
                ImGui.TextColored(new Vector4(1.0f, 0.85f, 0.35f, 1.0f), "ℹ️ 目前兩者皆未勾選：套用經典深色半透明科技懸浮風。");
            }
            ImGui.Spacing();

            var followTalk = config.ReplaceFollowTalkAddonPosition;
            if (ImGui.Checkbox("對話時視窗座標自動跟隨遊戲對話框", ref followTalk))
            {
                config.ReplaceFollowTalkAddonPosition = followTalk;
                config.Save();
            }
            ImGui.TextDisabled("若開啟，對話時覆蓋視窗會自動對齊原版 NPC 對話框的螢幕座標；若關閉，則完全固定在您拖曳擺放的位置。");

            if (config.ReplaceFollowTalkAddonPosition)
            {
                var posOffset = config.ReplacePositionOffset;
                if (ImGui.DragFloat2("對齊座標微調偏移 (X, Y 像素)", ref posOffset, 1.0f, -600f, 600f, "%.0f px"))
                {
                    config.ReplacePositionOffset = posOffset;
                    config.Save();
                }
                ImGui.TextDisabled("相對於遊戲原版 NPC 對話框錨點的微調偏移量。");
            }

            ImGui.Spacing();
            var autoSyncSize = config.ReplaceAutoSyncSize;
            if (ImGui.Checkbox("對話時視窗大小自動同步遊戲對話框 (Auto-sync Size)", ref autoSyncSize))
            {
                config.ReplaceAutoSyncSize = autoSyncSize;
                config.Save();
            }
            ImGui.TextDisabled("若開啟，對話時覆蓋視窗的尺寸會 100% 自動抓取並貼合遊戲原生對話框的實際大小（包含 HUD 縮放倍率）；若關閉，則允許您自由拉動邊框調整尺寸。");

            if (config.ReplaceAutoSyncSize)
            {
                var sizeOffset = config.ReplaceSizeOffset;
                if (ImGui.DragFloat2("同步尺寸微調偏移 (寬, 高 像素)", ref sizeOffset, 1.0f, -400f, 400f, "%.0f px"))
                {
                    config.ReplaceSizeOffset = sizeOffset;
                    config.Save();
                }
                ImGui.TextDisabled("在自動同步大小的基礎上進行寬度與高度微調。");
            }

            if (dialogueWatcher.TalkAddonPosition.HasValue && dialogueWatcher.TalkAddonSize.HasValue)
            {
                ImGui.Spacing();
                if (ImGui.Button("一鍵抓取並套用當前遊戲對話框座標與尺寸"))
                {
                    replaceOverlayWindow.ApplyTalkAddonBounds(dialogueWatcher.TalkAddonPosition.Value, dialogueWatcher.TalkAddonSize.Value);
                }
                ImGui.TextDisabled($"當前偵測到遊戲對話框座標: ({dialogueWatcher.TalkAddonPosition.Value.X:F0}, {dialogueWatcher.TalkAddonPosition.Value.Y:F0})，大小: ({dialogueWatcher.TalkAddonSize.Value.X:F0} x {dialogueWatcher.TalkAddonSize.Value.Y:F0})");
            }

            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.3f, 0.9f, 0.4f, 1.0f), "💡 覆蓋視窗尺寸與位置調整方式：");
            ImGui.TextDisabled("此設定視窗開啟時，畫面上的【Tataru 原生對話覆蓋視窗】會常駐顯示。\n您可以直接用滑鼠拖動它至 NPC 對話框位置，並拉伸邊框至所需大小，遊戲會自動永久記憶！");

            ImGui.Unindent();
        }

        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "視窗控制");
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextColored(new Vector4(0.3f, 0.9f, 0.4f, 1.0f), "💡 貼心設計：只要開啟設定視窗，字幕視窗便會永遠常駐顯示，方便您拖曳定位與調整大小；關閉設定視窗後，將自動啟動滑鼠點擊穿透 (Click-Through)，完全不干擾遊戲戰鬥與點擊。");
        ImGui.Spacing();

        var lockOverlay = config.LockOverlay;
        if (ImGui.Checkbox("鎖定字幕視窗位置與大小", ref lockOverlay))
        {
            config.LockOverlay = lockOverlay;
            config.Save();
        }

        var autoHide = config.AutoHideSeconds;
        if (ImGui.SliderFloat("對話結束後自動隱藏延遲 (獨立模式專用)", ref autoHide, 0.0f, 30.0f, "%.1f 秒"))
        {
            config.AutoHideSeconds = autoHide;
            config.Save();
        }
        ImGui.TextDisabled("僅在「獨立翻譯框」模式生效（設為 0 秒則不隱藏）。在覆蓋模式下，遊戲對話結束時會即刻跟隨關閉。");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.Button("重開字幕視窗 (強制顯示以供調整位置)"))
        {
            if (config.ReplaceGameTalkAddon)
            {
                replaceOverlayWindow.IsOpen = true;
            }
            else
            {
                overlayWindow.IsOpen = true;
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("重設目前視窗位置與大小"))
        {
            if (config.ReplaceGameTalkAddon)
            {
                replaceOverlayWindow.ResetPosition();
            }
            else
            {
                overlayWindow.ResetPosition();
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("清除翻譯快取"))
        {
            translator.ClearCache();
        }
    }

    private void DrawAbout()
    {
        ImGui.Spacing();
        ImGui.TextColored(new Vector4(1.0f, 0.85f, 0.35f, 1.0f), "Tataru Native Translator (Dalamud-Tataru)");
        var version = GetType().Assembly.GetName().Version;
        ImGui.TextDisabled(version != null ? $"Version {version}" : "Version 1.0.0");
        ImGui.Spacing();
        ImGui.TextWrapped("專為 Final Fantasy XIV 玩家打造的原生遊戲內即時劇情翻譯插件。");
        ImGui.Spacing();

        var showStartup = config.ShowStartupNotification;
        if (ImGui.Checkbox("每次啟動時提示現在版本 引擎模型 與自動更新訊息 顯示同一行內##about", ref showStartup))
        {
            config.ShowStartupNotification = showStartup;
            config.Save();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.Text("指令：");
        ImGui.BulletText("/tataru 或 /tataru config - 開啟設定視窗");
        ImGui.BulletText("/tataru toggle - 開關字幕懸浮視窗");
        ImGui.BulletText("/tataru replace [on|off] - 開關原生對話框覆蓋模式（隱藏原版泡泡並雙語顯示）");
        ImGui.BulletText("/tataru lms [on|off] - 開關或切換至 LM Studio 本地模型 (/tataru LMS)");
        ImGui.BulletText("/tataru gemini [on|off] - 開關或切換至 Gemini AI");
        ImGui.BulletText("/tataru clearcache - 清除所有翻譯暫存快取");
        ImGui.BulletText("/tataru update - 從 Tataru-Assistant 雲端立即更新最新文本庫");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "特別鳴謝 (Special Thanks)：");
        ImGui.TextWrapped("特別感謝 Tataru Assistant (winw1010/tataru-assistant) 及其社群貢獻者所建立維護的龐大 FFXIV 專有名詞辭典與開源生態支援！");
    }

    private void UpdateGlossaryFromCloud()
    {
        isUpdatingGlossary = true;
        glossaryStatus = "正在從 Tataru-Assistant (winw1010/tataru-assistant-text) 下載最新文本...";

        Task.Run(async () =>
        {
            try
            {
                var (success, hasUpdated, msg) = await glossary.UpdateGlossariesFromTataruAsync(forceReload: true);
                glossaryStatus = success ? $"✔ {msg}" : $"❌ 更新失敗：{msg}";
                Plugin.ChatGui.Print($"[Tataru] Glossary: {(success ? (hasUpdated ? "Updated to latest version." : "Already up to date.") : $"Update failed: {msg}")}");
            }
            catch (Exception ex)
            {
                glossaryStatus = $"❌ 更新失敗：{ex.Message}";
                Plugin.ChatGui.Print($"[Tataru] Glossary update error: {ex.Message}");
            }
            finally
            {
                isUpdatingGlossary = false;
            }
        });
    }

    private void RefreshModelsFromApi()
    {
        if (string.IsNullOrWhiteSpace(config.GeminiApiKey))
        {
            testStatus = "請先輸入 Gemini API Key 再同步模型清單！";
            return;
        }

        isFetchingModels = true;
        testStatus = "正在從 Google API 抓取最新可用模型清單...";

        Task.Run(async () =>
        {
            try
            {
                var fetched = await translator.FetchAvailableModelsAsync();
                if (fetched.Count > 0)
                {
                    var list = new List<string>(fetched);
                    list.Add("自訂模型 (Custom)");
                    models = list.ToArray();
                    testStatus = $"【同步成功】已更新 {fetched.Count} 個最新 Gemini 模型！";
                }
                else
                {
                    testStatus = "【同步失敗】無法取得模型清單，請確認 API Key 是否正確。";
                }
            }
            catch (Exception ex)
            {
                testStatus = $"【同步失敗】: {ex.Message}";
            }
            finally
            {
                isFetchingModels = false;
            }
        });
    }

    private void RefreshLmModelsFromApi()
    {
        isFetchingLmModels = true;
        testStatus = "正在從 LM Studio 抓取已載入/可用模型清單...";

        Task.Run(async () =>
        {
            try
            {
                var fetched = await lmStudio.FetchAvailableModelsAsync();
                if (fetched.Count > 0)
                {
                    config.LmStudioModel = fetched[0];
                    config.Save();
                    testStatus = $"【LM Studio 同步成功】找到 {fetched.Count} 個模型，已選取：{fetched[0]}";
                }
                else
                {
                    testStatus = "【LM Studio 提示】未收到模型清單，請確認 LM Studio 的 Local Server 已點擊「Start Server」且有載入模型。";
                }
            }
            catch (Exception ex)
            {
                testStatus = $"【LM Studio 同步失敗】: {ex.Message}";
            }
            finally
            {
                isFetchingLmModels = false;
            }
        });
    }

    private void RunLmStudioTest()
    {
        isTesting = true;
        testStatus = "LM Studio 連線測試中...";

        Task.Run(async () =>
        {
            try
            {
                var (speaker, text) = await lmStudio.TranslateAsync("タタル", "冒険者さん、こんにちはでっす！", config.TargetLang);
                testStatus = $"【LM Studio 測試結果】\n說話者：{speaker}\n譯文：{text}";
            }
            catch (Exception ex)
            {
                testStatus = $"【LM Studio 測試失敗】: {ex.Message}";
            }
            finally
            {
                isTesting = false;
            }
        });
    }

    private void RunApiTest()
    {
        if (string.IsNullOrWhiteSpace(config.GeminiApiKey))
        {
            testStatus = "請先輸入有效的 Gemini API Key！";
            return;
        }

        isTesting = true;
        testStatus = "Gemini 連線測試中...";

        Task.Run(async () =>
        {
            try
            {
                var (speaker, text) = await translator.TranslateWithGeminiCoreAsync("タタル", "冒険者さん、こんにちはでっす！");
                testStatus = $"【Gemini 測試結果】\n說話者：{speaker}\n譯文：{text}";
            }
            catch (Exception ex)
            {
                testStatus = $"【Gemini 測試失敗】: {ex.Message}";
            }
            finally
            {
                isTesting = false;
            }
        });
    }

    private void RunYoudaoTest()
    {
        isTesting = true;
        testStatus = "有道連線測試中...";

        Task.Run(async () =>
        {
            try
            {
                var (speaker, text) = await youdao.TranslateAsync("タタル", "冒険者さん、こんにちはでっす！", config.TargetLang);
                testStatus = $"【有道測試結果】\n說話者：{speaker}\n譯文：{text}";
            }
            catch (Exception ex)
            {
                testStatus = $"【有道測試失敗】: {ex.Message}";
            }
            finally
            {
                isTesting = false;
            }
        });
    }

    private void RunGoogleTest()
    {
        isTesting = true;
        testStatus = "Google 連線測試中...";

        Task.Run(async () =>
        {
            try
            {
                var (speaker, text) = await google.TranslateAsync("タタル", "冒険者さん、こんにちはでっす！", config.TargetLang);
                testStatus = $"【Google 測試結果】\n說話者：{speaker}\n譯文：{text}";
            }
            catch (Exception ex)
            {
                testStatus = $"【Google 測試失敗】: {ex.Message}";
            }
            finally
            {
                isTesting = false;
            }
        });
    }
}
