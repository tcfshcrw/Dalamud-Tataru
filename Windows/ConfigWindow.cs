using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
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
    private readonly GlossaryManager glossary;

    private bool showApiKey = false;
    private string testStatus = string.Empty;
    private bool isTesting = false;
    private bool isFetchingModels = false;
    private bool isFetchingLmModels = false;
    private bool isUpdatingGlossary = false;
    private string glossaryStatus = string.Empty;

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

    public ConfigWindow(Configuration config, GeminiTranslator translator, LmStudioTranslator lmStudio, YoudaoTranslator youdao, GoogleTranslator google, SubtitleOverlayWindow overlayWindow, GlossaryManager glossary)
        : base("Tataru Native Translator 設定###TataruConfigWindow")
    {
        this.config = config;
        this.translator = translator;
        this.lmStudio = lmStudio;
        this.youdao = youdao;
        this.google = google;
        this.overlayWindow = overlayWindow;
        this.glossary = glossary;

        Size = new Vector2(560, 620);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        if (ImGui.BeginTabBar("TataruSettingsTabs"))
        {
            if (ImGui.BeginTabItem("AI 翻譯設定"))
            {
                DrawTranslationSettings();
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
        ImGui.TextDisabled($"目前已載入：{glossary.NameMapCount} 筆 NPC/專有名詞對照，繁中校正 {glossary.PostReplaceChtCount} 筆，簡中校正 {glossary.PostReplaceChsCount} 筆。");
        ImGui.Spacing();

        var autoUpdate = config.AutoUpdateGlossary;
        if (ImGui.Checkbox("啟動遊戲/插件時自動從 Tataru-Assistant 檢查並更新文本庫", ref autoUpdate))
        {
            config.AutoUpdateGlossary = autoUpdate;
            config.Save();
        }

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

    private void DrawOverlaySettings()
    {
        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "字幕顯示項目");
        ImGui.Separator();
        ImGui.Spacing();

        var fontScale = config.FontScale;
        if (ImGui.SliderFloat("字幕文字縮放倍率", ref fontScale, 0.5f, 3.0f, "%.2f 倍"))
        {
            config.FontScale = Math.Clamp(fontScale, 0.5f, 3.0f);
            config.Save();
        }
        ImGui.TextDisabled("直接放大或縮小字幕文字尺寸（預設 1.00 倍，可調至 1.50 ~ 2.00 倍）。");
        ImGui.Spacing();

        var opacity = config.WindowOpacity;
        if (ImGui.SliderFloat("視窗背景不透明度 (Opacity)", ref opacity, 0.0f, 1.0f, "%.2f"))
        {
            config.WindowOpacity = Math.Clamp(opacity, 0.0f, 1.0f);
            config.Save();
        }
        ImGui.TextDisabled("調整字幕底色深淺：0.00 為全透明（純文字懸浮無黑底），1.00 為純黑完全不透明。");
        ImGui.Spacing();

        var showSpeaker = config.ShowSpeaker;
        if (ImGui.Checkbox("顯示說話者名稱", ref showSpeaker))
        {
            config.ShowSpeaker = showSpeaker;
            config.Save();
        }

        var showOriginal = config.ShowOriginalText;
        if (ImGui.Checkbox("顯示日文原文對照", ref showOriginal))
        {
            config.ShowOriginalText = showOriginal;
            config.Save();
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
        if (ImGui.SliderFloat("對話結束後自動隱藏延遲 (秒)", ref autoHide, 0.0f, 30.0f, "%.1f 秒"))
        {
            config.AutoHideSeconds = autoHide;
            config.Save();
        }
        ImGui.TextDisabled("設為 0 秒則不自動隱藏，保留最後一條對話。");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.Button("重開字幕視窗 (強制顯示以供調整位置)"))
        {
            overlayWindow.IsOpen = true;
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
        ImGui.TextDisabled("Version 1.0.0");
        ImGui.Spacing();
        ImGui.TextWrapped("專為 Final Fantasy XIV 玩家打造的原生遊戲內即時劇情翻譯插件。");
        ImGui.TextWrapped("整合 Tataru Assistant 專有名詞辭典與 Google Gemini AI，無須開啟外部 exe 程式，享受原汁原味的沉浸式劇情體驗。");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.Text("指令：");
        ImGui.BulletText("/tataru 或 /tataru config - 開啟設定視窗");
        ImGui.BulletText("/tataru toggle - 開關字幕懸浮視窗");
        ImGui.BulletText("/tataru lms [on|off] - 開關或切換至 LM Studio 本地模型 (/tataru LMS)");
        ImGui.BulletText("/tataru gemini [on|off] - 開關或切換至 Gemini AI");
        ImGui.BulletText("/tataru update - 從 Tataru-Assistant 雲端立即更新最新文本庫");
    }

    private void UpdateGlossaryFromCloud()
    {
        isUpdatingGlossary = true;
        glossaryStatus = "正在從 Tataru-Assistant (winw1010/tataru-assistant-text) 下載最新文本...";

        Task.Run(async () =>
        {
            try
            {
                var (success, msg) = await glossary.UpdateGlossariesFromTataruAsync();
                glossaryStatus = success ? $"✔ {msg}" : $"❌ 更新失敗：{msg}";
            }
            catch (Exception ex)
            {
                glossaryStatus = $"❌ 更新失敗：{ex.Message}";
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
