# Tataru Native Translator (Dalamud-Tataru)

一款為 **Final Fantasy XIV (FFXIV)** 玩家打造的**原生遊戲內即時劇情翻譯插件**。  
整合 **[Tataru Assistant](https://github.com/winw1010/tataru-assistant)** 的專有名詞與人名辭典，並透過 **Google Gemini AI** 進行非同步即時翻譯。

---

## 核心特色

- **純原生遊戲內運行**：直接作為 Dalamud Plugin 運行於遊戲內部，**不需要開啟任何外部 exe 程式或 Electron 視窗**。
- **極致精準的對話攔截**：透過 Dalamud 記憶體直接抓取遊戲 UI 文字（`AddonTalk` 主線支線對話、`TalkSubtitle` 過場 CG 字幕、`AddonBattleTalk` 副本首領喊話），零延遲且絕不漏字，再也不用依賴脆弱的 OCR 或外部指針掃描。
- **Tataru 繁簡專有名詞辭典整合**：內建 Tataru 原版 `jp-ch-name.json` 與 `after-translation-*.json` 譯後名詞校正，NPC 姓名與艾歐澤亞專有名詞精準對齊官方中文在地化。
- **Gemini AI 智能翻譯**：預設採用最新 `gemini-3.8-flash`（並支援一鍵從 Google API 同步最新模型清單），翻譯語氣自然流暢、反應迅速，且具備本地快取機制（重複對話秒出，省 API 額度）。
- **輕量化 ImGui 字幕懸浮窗**：
  - 支援說話者名稱顯示、譯文與原文（日文）即時對照。
  - 支援滑鼠穿透（Click-Through）、自訂字體與透明度、對話結束自動淡出隱藏。

---

## 專案目錄結構

```
Dalumud-Tataru/
├── Data/
│   ├── jp-ch-name.json             # 日中 NPC/人名對照表 (來自 Tataru 原版)
│   ├── after-translation-cht.json  # 繁體中文譯後名詞校正表
│   └── after-translation-chs.json  # 簡體中文譯後名詞校正表
├── Models/
│   └── DialogueEntry.cs            # 對話資料模型與語言設定定義
├── Services/
│   ├── DialogueWatcher.cs          # 遊戲原生對話/字幕視窗監聽器
│   ├── GeminiTranslator.cs         # Google Gemini REST API 非同步翻譯與快取服務
│   └── GlossaryManager.cs          # 專有名詞讀取與前後置文本校正服務
├── Windows/
│   ├── ConfigWindow.cs             # 插件設定介面 (API Key、模型選擇、連線測試)
│   └── SubtitleOverlayWindow.cs    # ImGui 原生字幕懸浮視窗
├── Configuration.cs                # 插件設定資料結構與持久化
├── Plugin.cs                       # Dalamud 插件主入口
├── DalamudTataru.csproj            # 專案建置檔 (基於 Dalamud.NET.Sdk)
└── DalamudTataru.json              # Dalamud 插件 Manifest
```

---

## 安裝方式 (Dalamud 自訂插件庫)

1. 在遊戲中輸入 `/xlsettings` 開啟 Dalamud 設定視窗。
2. 切換至 **「實驗性功能 (Experimental)」** 分頁。
3. 找到 **「自訂插件庫 (Custom Plugin Repositories)」** 區塊，新增以下網址並儲存：
   ```
   https://raw.githubusercontent.com/tcfshcrw/Dalumud-Tataru/main/pluginmaster.json
   ```
4. 開啟插件安裝器 (`/xlplugins`)，搜尋 **Tataru Native Translator** 即可一鍵安裝與自動更新！

---

## 快速上手

### 1. 取得免費的 Gemini API Key
1. 前往 [Google AI Studio](https://aistudio.google.com/)。
2. 登入 Google 帳號後點擊 **Get API key** -> **Create API key**。
3. 複製產生的 API Key。

### 2. 在遊戲內設定
1. 載入本插件後，於遊戲聊天框輸入指令：
   ```
   /tataru
   ```
2. 在彈出的設定視窗中貼上你的 **Gemini API Key**。
3. 點擊「**測試 API 連線與翻譯**」，確認顯示「測試成功」即可開始體驗！

---

## 遊戲內指令

| 指令 | 說明 |
| :--- | :--- |
| `/tataru` 或 `/tataru config` | 開啟 / 關閉設定視窗 |
| `/tataru toggle` | 開啟 / 關閉字幕視窗（可用於手動調整視窗位置） |
| `/tataru replace` | 開關原生對話框覆蓋模式（隱藏原版泡泡並雙語顯示，支援 `on`/`off`） |
| `/tataru gemini on` | 切換主翻譯引擎為 Gemini |
| `/tataru gemini off` | 切換主翻譯引擎為有道 (Youdao) |
| `/tataru LMS` | 開關或切換主翻譯引擎為 LM Studio 本地模型（支援 `/tataru LMS on` / `/tataru LMS off`） |
| `/tataru gemini` | 在 Gemini 與有道翻譯之間切換 |
| `/tataru clearcache` | 清除目前記憶體中所有的對話翻譯暫存快取 |
| `/tataru update` | 直接從 Tataru-Assistant 雲端更新最新專有名詞與譯後修飾文本庫 |

---

## 致敬與感謝

- 感謝 [winw1010/tataru-assistant](https://github.com/winw1010/tataru-assistant) 團隊為 FF14 中文社群長期整理的專有名詞庫與譯名對照表。
- 感謝 **Dalamud** 開發團隊提供優秀的 FFXIV 插件生態。
