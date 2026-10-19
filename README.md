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
- 巨集用到的相依插件，安裝來源改指台服插件庫。
- 新增 Lua API：讀取聊天訊息、操作物品欄與圖示右鍵選單、AutoRetainer 逐格取回僱員道具；`/callback` 送出的事件會透過 IPC 廣播給其他插件。
- 巨集跑完或出錯停下時可請塔塔露說一句（需要 TataruPraise）。
- 巨集壓制 YesAlready 改用具名租約，跑完不會掀掉別的插件的壓制；關遊戲或重載時確實放開。
- 修正 `/click` 在 `/loop` 第二圈起必定失敗、`/interact` 沒有目標時不報錯、製作巨集 SmartWait 等太久就安靜結束等問題。
- 巨集讀到的視窗文字、公會名、F.A.T.E. 名稱會先剝掉遊戲的圖示與連結標記。
- 遊戲改版後部分遊戲資料讀不到時，Lua 模組改回報讀不到，不會讓遊戲出錯。
- 多處降低記憶體用量。
