# Something Need Doing (SND)

[SomethingNeedDoing](https://github.com/Jaksuhn/SomethingNeedDoing) 的台服移植版。擴充遊戲原生巨集系統的插件：不限行數、不限視窗數，並內建 Lua 腳本引擎與大量自動化模組。

原作者：[Jaksuhn/SomethingNeedDoing](https://github.com/Jaksuhn/SomethingNeedDoing)

## 功能

- 巨集管理與排程：可同時執行多個巨集，支援暫停／恢復／停止（含「執行到下一個迴圈點再停」）、指定迴圈次數執行
- 相容原生巨集語法，並擴充更多指令與修飾詞
- Lua 腳本引擎，API 涵蓋：技能施放、遊戲介面（Addon）操作、聊天、插件設定讀寫、Dalamud 服務、戰鬥單位、Excel 表查詢、FATE、副本狀態、任務、物品欄、跨插件 IPC、玩家狀態、系統資訊等
- 社群腳本庫：可搭配 [SNDScripts](https://github.com/WigglyMuffin/SNDScripts/)、[The Dumpster Fire](https://github.com/McVaxius/dhogsbreakfeast) 等社群維護的腳本集使用
- repo 的 `Macros/` 資料夾附幾支台服用的 Lua 巨集：照料庭院園圃／花盆（`tend_garden.lua`、`care_all.lua`）、從僱員快速取回全部道具（`retrieve_all_from_retainer.lua`，需要 AutoRetainer）

## 指令

- `/somethingneeddoing`、`/snd`、`/pcraft`：開啟主視窗
- 以下為子指令，接在上面任一指令後面（例：`/snd run "<巨集名>"`）
- `run "<巨集名>"`／`run loop <次數> "<巨集名>"`：執行巨集／迴圈執行
- `pause "<巨集名>"`／`pause loop "<巨集名>"`／`pause all`：暫停
- `resume "<巨集名>"`／`resume all`：恢復
- `stop "<巨集名>"`／`stop loop "<巨集名>"`／`stop all`：停止
- `status`：切換執行中巨集清單視窗
- `changelog`：切換更新記錄視窗
- `help`：列出指令說明

## 台服版的修改

- 介面繁體中文化。
- 巨集用到的相依插件，安裝來源改指台服的第三方插件倉庫。
- 新增 Lua API：讀取聊天訊息、操作物品欄與圖示右鍵選單、AutoRetainer 逐格取回僱員道具；`/callback` 送出的事件會透過 IPC 廣播給其他插件。
- Lua 可以讀深層迷宮狀態（`InstancedContent.DeepDungeon`）：樓層、房間格、隊友所在房間、魔陶器與魔石數量，也能使用魔陶器／魔石。
- Lua 可以控制 ICE 的宇宙探索（`IPC.ICE`）：啟動／停止、設定「做完目前任務就停」、查詢是否還在執行、目前任務與上次停下的原因。
- Lua 可以使用 AutoRetainer 的「跑一輪就回來」（`IPC.AutoRetainer` 的 `RoundTrip*` 函式）；AutoRetainer 版本太舊沒有這項時回傳 0／nil／false，巨集照常執行。
- 巨集跑完或出錯停下時可請塔塔露說一句（需要 TataruPraise）。
- 巨集壓制 YesAlready 改用具名租約，跑完不會掀掉別的插件的壓制；關遊戲或重載時確實放開。
- 修正 `/click` 在 `/loop` 第二圈起必定失敗、`/interact` 沒有目標時不報錯、製作巨集 SmartWait 等太久就安靜結束等問題。
- 修正 `/equip` 換裝明明已換上卻報「找不到裝備選項」；`/keyitem` 用台服的重要物品名稱也找得到。
- 巨集停止後馬上再執行時，新的一輪會等上一輪收尾完才開始，不會被上一輪的收尾中斷。
- 巨集讀到的視窗文字、公會名、F.A.T.E. 名稱會先剝掉遊戲的圖示與連結標記。
- 遊戲改版後部分遊戲資料讀不到時，Lua 模組改回報讀不到，不會讓遊戲出錯。
- 多處降低記憶體用量。

## 安裝

1. 在遊戲內輸入 `/xlsettings` 開啟「Dalamud 設定」，到「試驗性功能」分頁的「第三方插件倉庫」加入
   `https://raw.githubusercontent.com/ffxiv-tc-port/DalamudPluginsTC/main/repo.json`
   勾選啟用後儲存。
2. 輸入 `/xlplugins` 開啟「插件管理員」，搜尋「Something Need Doing」安裝。

同一個插件只裝這裡的版本，不要同時安裝上游（國際服）版本。

## 需要搭配的插件

- 選用：[AutoRetainer](https://github.com/ffxiv-tc-port/AutoRetainer)（僱員相關的 Lua 函式與附帶的取回道具巨集）、[ICE](https://github.com/ffxiv-tc-port/ICE)（宇宙探索的 Lua 函式）、[TCToolbox](https://github.com/ffxiv-tc-port/TCToolbox)（附帶的園圃巨集會用到）、[TataruPraise](https://github.com/ffxiv-tc-port/TataruPraise)（塔塔露語音）、[YesAlready](https://github.com/ffxiv-tc-port/YesAlready)（巨集執行期間可暫停它）

授權：上游未附授權。
