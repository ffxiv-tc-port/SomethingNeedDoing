--[[
快速取回目前開啟中雇員身上的道具，全部進玩家自己的背包，不會放進兵裝庫。
預設「不取水晶」（見 INCLUDE_CRYSTALS）。

前提：
  - 使用 AutoRetainer 的 IPC 方法 RetrieveNextRetainerItemSlot()：每呼叫一次只觸發一格取回，
    不等遊戲確認道具真的離開雇員庫存就馬上回傳，讓這支腳本自己控制節奏。
  - 執行前雇員的「物品儲存」視窗必須已經開啟（跟手動取回一樣，先跟雇員互動）。IPC 方法本身
    會檢查視窗是否開啟，沒開啟就直接回傳 false，不會報錯，但這支腳本一次道具都取不到。

設計：整輪掃完再看結果，不逐格精算
  RetrieveNextRetainerItemSlot() 回傳 true 只代表「送出了一個取回指令」，不代表「那一格
  真的離開了雇員」。與其逐格輪詢確認（那會被單格的往返延遲綁住，實測中位數 0.382 秒／格），
  不如照玩家手動連點的方式做：**一輪把該取的格數送完，再回頭看雇員少了幾格**。

  好處有三：
    1. 快 —— 每格只花一個 STEP（預設 0.05 秒），不用等單格確認往返。
    2. 多輪才有意義 —— 每輪之間才做一次「有沒有真的變少」的判定，沒變少就收工。
       逐格判定的話，多輪只是把同一件事重做，沒有額外資訊。
    3. 不會卡死 —— 取不走的格子（例如水晶已達上限）最多讓「這一輪沒有進度」，
       下一次判定就會停止。舊版逐格重試曾實測對水晶第 3 格連送 50 個指令、卡住 15.94 秒。

  指令上限用「這一輪開始時實際佔用的格數」推得，不是無上限迴圈——物品欄本來就有界，
  不需要精算。
]]

local INCLUDE_CRYSTALS = false -- true = 連水晶一起取回。⚠️ 水晶不佔背包格，若水晶已達上限會
                               -- 取不走；此時本腳本會在「該輪沒有進度」時停止，不會卡死。
local STEP = 0.05              -- 同一輪內兩次取回指令之間的間隔秒數（玩家快速連點的節奏）
local SETTLE = 0.5             -- 一輪送完之後，等庫存資料穩定再判定的秒數
local MAX_PASSES = 5           -- 最多掃幾輪；每輪之間都會判定「是否真的變少」，沒變少就提早結束
local SLACK = 2                -- 每輪允許比實際佔用格數多送幾個指令（吸收邊界情況）

local ITEM_PAGES = {
    "RetainerPage1", "RetainerPage2", "RetainerPage3", "RetainerPage4",
    "RetainerPage5", "RetainerPage6", "RetainerPage7",
}

-- 掃描範圍：預設只看道具頁；開啟 INCLUDE_CRYSTALS 才把水晶納入進度判斷。
local SCAN_PAGES = {}
for _, name in ipairs(ITEM_PAGES) do table.insert(SCAN_PAGES, name) end
if INCLUDE_CRYSTALS then table.insert(SCAN_PAGES, "RetainerCrystals") end

-- 回傳指定容器目前「已佔用」的格數。每次都重新取得 wrapper，不跨幀保存原生指標。
--
-- 📌 SND v7.20.0.13 起，InventoryContainerWrapper 對容器指標做了 null 檢查（拿不到就回 0），
--    所以這裡不再需要靠「呼叫時機」來避免解參考未載入的容器。pcall 仍然保留，擋受管理層的
--    錯誤（例如容器名稱打錯導致的型別問題）。
local function usedSlots(pages)
    local used = 0
    for _, name in ipairs(pages) do
        local ok, n = pcall(function()
            local c = Inventory[name]
            return c.Count - c.FreeSlots
        end)
        if ok and type(n) == "number" then used = used + n end
    end
    return used
end

if not IPC.IsInstalled("AutoRetainer") then
    yield("/echo AutoRetainer 未安裝，無法執行。")
    return
end

local totalRetrieved = 0
local totalCommands = 0
local stopReason = nil

for pass = 1, MAX_PASSES do
    local before = usedSlots(SCAN_PAGES)

    if before == 0 then
        stopReason = stopReason or (INCLUDE_CRYSTALS and "雇員身上已經沒有東西" or "道具頁已清空（未取水晶）")
        break
    end

    -- 這一輪最多送這麼多指令：實際佔用格數 + 一點餘裕。物品欄有界，不需要精算。
    local budget = before + SLACK
    local sentThisPass = 0

    for _ = 1, budget do
        -- IPC 自己會擋掉「視窗沒開」與「背包快滿」，回 false 就是該收工了。
        if not IPC.AutoRetainer.RetrieveNextRetainerItemSlot() then
            stopReason = stopReason or "AutoRetainer 回報沒有可取回的道具（或背包已快滿）"
            break
        end
        sentThisPass = sentThisPass + 1
        totalCommands = totalCommands + 1
        yield("/wait " .. STEP)
    end

    -- 一輪送完，等資料穩定再看實際少了幾格。這是唯一的進度判定點。
    yield("/wait " .. SETTLE)
    local after = usedSlots(SCAN_PAGES)
    local movedThisPass = before - after
    if movedThisPass < 0 then movedThisPass = 0 end
    totalRetrieved = totalRetrieved + movedThisPass

    Dalamud.Log(string.format(
        "第 %d 輪完成：送出 %d 個指令，雇員格數 %d → %d（本輪取回 %d 格，累計指令 %d 次）。",
        pass, sentThisPass, before, after, movedThisPass, totalCommands))

    -- 這一輪完全沒有進度：再掃下去也不會變（例如水晶已達上限、背包滿了、視窗關了）。
    if movedThisPass == 0 then
        stopReason = stopReason or "本輪沒有任何格子被取走，停止以免空轉"
        break
    end

    -- 送出的指令數已經被 IPC 中斷（回 false），代表沒有更多可取的了。
    if sentThisPass < budget and stopReason then
        break
    end
end

if totalRetrieved == 0 then
    yield("/echo 沒有取回任何道具，請確認雇員的物品儲存視窗是否已開啟，或背包是否已經滿了。")
    if stopReason then Dalamud.Log("停止原因：" .. stopReason) end
else
    -- 指令放大倍率：整輪送完再判定的作法下，健康值應該接近 1.0～1.2
    -- （SLACK 會讓最後一輪多送幾個無效指令，那是刻意的）。舊版逐格重試實測是 3.1～3.9。
    local amplification = totalCommands / totalRetrieved
    local report = string.format("取回完成，共取回 %d 格道具。", totalRetrieved)
    Dalamud.Log(string.format("%s（送出指令 %d 次，放大倍率 %.2fx；停止原因：%s）",
        report, totalCommands, amplification, stopReason or "正常結束"))
    yield("/echo " .. report)
end
