--[[
快速取回目前開啟中雇員身上的道具，全部進玩家自己的背包，不會放進兵裝庫。
預設「不取水晶」（見 INCLUDE_CRYSTALS）。

前提：
  - 使用 AutoRetainer 的 IPC 方法 RetrieveNextRetainerItemSlot()：每呼叫一次只觸發一格取回，
    不等遊戲確認道具真的離開雇員庫存就馬上回傳，讓這支腳本自己控制節奏。
  - 執行前雇員的「物品儲存」視窗必須已經開啟（跟手動取回一樣，先跟雇員互動）。IPC 方法本身
    會檢查視窗是否開啟，沒開啟就直接回傳 false，不會報錯，但這支腳本一次道具都取不到。

設計：每格只送一個指令，然後等伺服器把佇列消化完
  舊版的放大來源已經查清楚：取回是真的伺服器往返，實測每格約 0.127 秒才會從雇員庫存消失，
  而這支腳本每 0.05 秒就送一個指令。舊版的 IPC 每次都從第 0 格重新掃、回傳「第一個有東西的
  格子」，所以同一格在真正清空之前會被重複送 2～3 次指令（實機量到 2.48 倍）。

  現在 AutoRetainer 那端會記住「這一輪已經對哪些格子送過指令」，在那一格的內容真的變動之前
  不再回傳它。所以：
    1. 每輪開始先呼叫 ResetRetainerRetrieveTracking() 把記憶清乾淨。
    2. 一路呼叫 RetrieveNextRetainerItemSlot() 直到它回傳 false ——
       這代表「當下每一個有東西的格子都已經各收到一個指令了」。
    3. 然後**等到雇員格數不再變動**才判定進度。這一步不能用固定秒數：送指令的速度
       (0.05 秒/個) 比伺服器消化的速度 (~0.13 秒/格) 快，送完之後佇列裡還有一大截沒落地，
       固定等 0.5 秒會嚴重低估進度，還會讓下一輪對「其實正在飛行中」的格子重送指令。
    4. 還有剩就再跑一輪（伺服器拒絕掉的、或塞不下的，下一輪會重新納入）。

  預期指令放大倍率接近 1.0。

⚠️ 撞到 MAX_PASSES 上限時會**明講**「撞到上限、還有幾格沒取」，不會印成「正常結束」。
]]

local INCLUDE_CRYSTALS = false -- true = 連水晶一起取回。⚠️ 水晶不佔背包格，若水晶已達上限會
                               -- 取不走；此時本腳本會在「該輪沒有進度」時停止，不會卡死。
local STEP = 0.05              -- 同一輪內兩次取回指令之間的間隔秒數
                               -- ⚠️ 這個值不再影響放大倍率（已由 IPC 端去重），只影響送指令的
                               --    爆發速度。若發現常常需要跑很多輪才取完（代表伺服器沒吃下
                               --    整串佇列），把它調到 0.15 會更穩，代價是慢一點。
local MAX_PASSES = 12          -- 最多掃幾輪。撞到上限會誠實回報，不會假裝跑完。
local SLACK = 2                -- 每輪允許比實際佔用格數多送幾個指令（吸收邊界情況）
local DRAIN_POLL = 0.25        -- 等待伺服器消化佇列時，多久量一次雇員格數
local DRAIN_QUIET = 1.0        -- 連續這麼多秒格數都沒變，才視為這一輪已經落地
local DRAIN_CAP = 30           -- 單輪最多等這麼多秒，避免萬一格數一直抖動就永遠等下去

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

-- 舊版 AutoRetainer 沒有這個 IPC 方法。用 pcall 擋住，退回舊行為（會有指令放大，但不會壞）。
local canReset = pcall(function() IPC.AutoRetainer.ResetRetainerRetrieveTracking() end)
if not canReset then
    Dalamud.Log("[取回] AutoRetainer 沒有 ResetRetainerRetrieveTracking，退回舊行為，指令放大倍率會偏高。請更新 AutoRetainer。")
end

local totalRetrieved = 0
local totalCommands = 0
local stopReason = nil
local hitPassLimit = false

for pass = 1, MAX_PASSES do
    local before = usedSlots(SCAN_PAGES)

    if before == 0 then
        stopReason = INCLUDE_CRYSTALS and "雇員身上已經沒有東西" or "道具頁已清空（未取水晶）"
        break
    end

    -- 每輪都把 IPC 端的「已送過指令」記憶清乾淨：上一輪被伺服器拒絕、或當時背包塞不下的格子，
    -- 這一輪要重新納入，否則會從「重複送」變成「永遠跳過、少取一格」。
    if canReset then pcall(function() IPC.AutoRetainer.ResetRetainerRetrieveTracking() end) end

    -- 這一輪最多送這麼多指令：實際佔用格數 + 一點餘裕。有去重之後正常會在送滿之前就回 false。
    local budget = before + SLACK
    local sentThisPass = 0
    local exhausted = false

    for _ = 1, budget do
        -- 回 false = 沒東西可取、背包快滿、視窗關了，或「剩下的格子都已經各有一個指令在飛」。
        if not IPC.AutoRetainer.RetrieveNextRetainerItemSlot() then
            exhausted = true
            break
        end
        sentThisPass = sentThisPass + 1
        totalCommands = totalCommands + 1
        yield("/wait " .. STEP)
    end

    -- 一格都送不出去：不是沒東西（before > 0），就是真的有障礙。再跑下去只會空轉。
    if sentThisPass == 0 then
        stopReason = "AutoRetainer 一格都不接受（背包已滿、雇員視窗已關閉，或剩下的都是無法取回的獨占道具）"
        break
    end

    -- 等到雇員格數不再變動＝伺服器把這一輪的指令消化完了。這是唯一的進度判定點。
    local last = usedSlots(SCAN_PAGES)
    local quiet = 0
    local waited = 0
    while waited < DRAIN_CAP do
        yield("/wait " .. DRAIN_POLL)
        waited = waited + DRAIN_POLL
        local now = usedSlots(SCAN_PAGES)
        if now == last then
            quiet = quiet + DRAIN_POLL
            if quiet >= DRAIN_QUIET then break end
        else
            quiet = 0
            last = now
        end
    end

    local after = usedSlots(SCAN_PAGES)
    local movedThisPass = before - after
    if movedThisPass < 0 then movedThisPass = 0 end
    totalRetrieved = totalRetrieved + movedThisPass

    Dalamud.Log(string.format(
        "第 %d 輪完成：送出 %d 個指令，雇員格數 %d → %d（本輪取回 %d 格，等待落地 %.2f 秒，累計指令 %d 次）。",
        pass, sentThisPass, before, after, movedThisPass, waited, totalCommands))

    if after == 0 then
        stopReason = INCLUDE_CRYSTALS and "全部取完" or "道具頁全部取完（未取水晶）"
        break
    end

    -- 這一輪完全沒有進度：再掃下去也不會變（例如水晶已達上限、背包滿了、視窗關了）。
    if movedThisPass == 0 then
        stopReason = "本輪沒有任何格子被取走，停止以免空轉"
        break
    end

    -- 這是最後一輪，而且上面沒有任何「已完成」的判定成立 —— 記下來，等等要誠實回報。
    if pass == MAX_PASSES then
        hitPassLimit = true
    end
end

if hitPassLimit then
    local left = usedSlots(SCAN_PAGES)
    stopReason = string.format("[未取完] 撞到輪數上限 MAX_PASSES=%d，雇員身上還有 %d 格沒取完，請再執行一次", MAX_PASSES, left)
end

if stopReason == nil then
    -- 理論上到不了這裡；真的到了就照實說不知道，不要冒充「正常結束」。
    stopReason = "未知（迴圈結束但沒有任何停止條件成立）"
end

if totalRetrieved == 0 then
    yield("/echo 沒有取回任何道具，請確認雇員的物品儲存視窗是否已開啟，或背包是否已經滿了。")
    yield("/echo 停止原因：" .. stopReason)
    Dalamud.Log("停止原因：" .. stopReason)
else
    -- 指令放大倍率：IPC 端去重之後，健康值應該接近 1.0～1.1
    -- （SLACK 與跨輪重試會讓它略高於 1）。去重之前實機量到 2.48，更早的逐格重試版是 3.1～3.9。
    local amplification = totalCommands / totalRetrieved
    local report = string.format("取回完成，共取回 %d 格道具。", totalRetrieved)
    Dalamud.Log(string.format("%s（送出指令 %d 次，放大倍率 %.2fx；停止原因：%s）",
        report, totalCommands, amplification, stopReason))
    yield("/echo " .. report)
    if hitPassLimit then
        yield("/echo " .. stopReason)
    end
end
