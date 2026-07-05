--[[
自動照料庭院種植物件（園圃/花盆，依實際狀態判斷 護理/施肥/跳過）

前提（已透過實機截圖 + 玩家回報確認）：
  - 可互動物件的顯示名稱：庭院「園圃」，或各種「XX花盆」（海濱花盆/林間花盆/綠洲花盆...）
  - 狀態文字不是來自 SelectString 的選項內容，是互動當下跳出的聊天/系統訊息
    （例如「風茄正茁壯成長。」「黃麻正...狀態不太好...」），格式是"植物名+狀態片語"，
    所以用「包含」比對而不是完全相等。
    -> 這需要新增的 Chat 模組（Chat.ClearLastMessage/Chat.GetLastMessage）才能讀到，
       屬於 C# 端變更，需要重新編譯 SomethingNeedDoing.dll 才會生效。
  - 依訊息內容判斷狀態：
      * 含「狀態不太好」：需要護理，選項 施肥/護理/處理/取消 -> 先護理再施肥
      * 含「茁壯成長」：健康，選項 施肥/護理/處理/取消 -> 直接施肥,不用護理
      * 含「已經成熟了」：可收穫，選項 收穫/取消 -> 選取消,留給玩家自己手動收穫
      * 含「已經枯萎了」：需要人工處理 -> 選取消,不自動處理
      * 含「沒有種」：空花盆/空園圃 -> 選取消,跳過
  - SelectString 選項在畫面上的顯示順序，就是 /callback 用的 0-based index

如果之後遇到訊息內容跟上面幾種狀況都不一樣（fallback 會直接選取消跳過並
記錄捕捉到的訊息文字），把訊息內容或截圖給我，我再補上對應規則。
]]

-- 可種植物件的名稱規則：庭院的「園圃」，以及各種「XX花盆」（海濱花盆/林間花盆/綠洲花盆...）
local function isPlantableName(name)
    return name == "園圃" or name:match("花盆$") ~= nil or name:match("花圃$") ~= nil
end

local INTERACT_RANGE = 3.0   -- 互動距離（碼）。太大會選到超出實際互動範圍的物件，導致「距離太遠」
local MAX_OBJECT_INDEX = 599 -- 物件表掃描上限

local NEEDS_CARE_TEXT = "狀態不太好"
local HEALTHY_TEXT = "茁壯成長"
local MATURE_TEXT = "已經成熟了"
local WITHERED_TEXT = "已經枯萎了"
local HARVEST_LABEL = "收穫"
local CARE_LABEL = "護理"
local FERTILIZE_LABEL = "施肥"
local CANCEL_LABEL = "取消"
local FERTILIZER_ITEM_LABEL = "魚粉" -- 選施肥後，還會跳一層選擇肥料道具的選單
local ALREADY_FERTILIZED_TEXT = "已經施加了足夠的肥料了"
local EMPTY_POT_TEXT = "沒有種" -- 涵蓋「花盆裡沒有種任何東西」「園圃...沒有種...」等各種容器的空盆訊息

-- 掃描附近所有名稱符合、距離內的種植物件
local function findNearbyPlots()
    local plots = {}
    for i = 0, MAX_OBJECT_INDEX do
        local ok, entity = pcall(function() return Entity[i] end)
        if ok and entity ~= nil and isPlantableName(entity.Name) and entity.DistanceTo <= INTERACT_RANGE then
            table.insert(plots, entity)
        end
    end
    return plots
end

-- 讀取目前 SelectString 視窗的選項文字，過濾掉純數字的內部值，以及帶冒號的標題行
-- （例如「園圃2：地壟8」是標題不是選項，不然 index 會整個偏移一位）
local function readSelectStringOptions()
    local addon = Addons.GetAddon("SelectString")
    local raw = addon:GetValueTexts()
    local options = {}
    for i = 0, raw.Count - 1 do
        local s = raw[i]
        if s ~= nil and s ~= "" and not tostring(s):match("^%d+$")
            and not s:find("：", 1, true) and not s:find(":", 1, true) then
            table.insert(options, s)
        end
    end
    return options
end

-- 判斷互動時捕捉到的狀態訊息屬於哪一種狀態（用包含比對，因為訊息前面會帶植物名稱）
local function classifyStatus(message)
    if message == nil or message == "" then return nil end
    if message:find(WITHERED_TEXT, 1, true) then return WITHERED_TEXT end
    if message:find(NEEDS_CARE_TEXT, 1, true) then return NEEDS_CARE_TEXT end
    if message:find(HEALTHY_TEXT, 1, true) then return HEALTHY_TEXT end
    if message:find(MATURE_TEXT, 1, true) then return MATURE_TEXT end
    if message:find(EMPTY_POT_TEXT, 1, true) then return EMPTY_POT_TEXT end
    return nil
end

-- 找選項在清單中的 0-based index（對應 /callback 用的 index）
local function optionIndex(options, label)
    for i, t in ipairs(options) do
        if t == label then return i - 1 end
    end
    return nil
end

local function selectOption(index)
    yield('/callback "SelectString" true ' .. index)
    yield("/wait 0.15")
end

-- 輪詢等待某個 addon 準備好，比固定 wait 更快也更穩
-- 注意：每次 yield 都是一次完整的 native 指令派送，實際耗時比 interval 數字大，
-- 所以 timeoutMs 需要抓寬鬆一點，避免明明互動成功卻被判定逾時而誤跳過
local function waitForAddonReady(name, timeoutMs)
    local waited = 0
    local interval = 150
    while waited < timeoutMs do
        if Addons.GetAddon(name).Ready then return true end
        yield("/wait " .. (interval / 1000))
        waited = waited + interval
    end
    return Addons.GetAddon(name).Ready
end

-- 等待 SelectString，但有些物件互動後會先跳出「對話」（Talk addon，NPC/物件的過場文字），
-- 要先把它點掉才會進到 SelectString，不然會一直卡住直到逾時
local function waitForSelectStringAfterInteract(timeoutMs)
    local waited = 0
    local interval = 150
    while waited < timeoutMs do
        if Addons.GetAddon("SelectString").Ready then return true end
        if Addons.GetAddon("Talk").Exists then
            yield("/click Talk Click")
        end
        yield("/wait " .. (interval / 1000))
        waited = waited + interval
    end
    return Addons.GetAddon("SelectString").Ready
end

local function cancelIfPossible(options)
    local idx = optionIndex(options, CANCEL_LABEL)
    if idx then selectOption(idx) end
end

local function findItemByName(name)
    local containers = { "Inventory1", "Inventory2", "Inventory3", "Inventory4" }
    for _, cname in ipairs(containers) do
        local container = Inventory[cname]
        for i = 0, container.Count - 1 do
            local item = container[i]
            if item ~= nil and item.ItemId ~= 0 then
                local row = Excel.GetRow("Item", item.ItemId)
                if row ~= nil and row["Name"] == name then
                    return item
                end
            end
        end
    end
    return nil
end

-- 選完「施肥」後：
--   1. 如果肥料已經足夠，遊戲只會跳訊息「已經施加了足夠的肥料了」，不會開背包 -> 直接跳過
--   2. 否則會開背包等你「右鍵道具 -> 施肥」，這裡改用 AgentInventoryContext 直接開啟該
--      道具的右鍵選單（OpenContextMenu），再從 ContextMenu 選「施肥」（SelectContextMenuEntry）
local function tryChooseFertilizerItem()
    yield("/wait 0.15")
    local msg = Chat.GetLastMessage()
    if msg ~= nil and msg:find(ALREADY_FERTILIZED_TEXT, 1, true) then
        Dalamud.LogDebug("肥料已經足夠，不用開背包")
        return
    end

    local item = findItemByName(FERTILIZER_ITEM_LABEL)
    if not item then
        Dalamud.Log("背包裡找不到魚粉，跳過，請人工處理施肥道具選擇")
        return
    end

    Dalamud.LogDebug("找到魚粉，開啟右鍵選單")
    item:OpenContextMenu()
    waitForAddonReady("ContextMenu", 1000)

    if Addons.SelectContextMenuEntry(FERTILIZE_LABEL) then
        Dalamud.LogDebug("已從右鍵選單選擇施肥")
    else
        Dalamud.Log("右鍵選單裡找不到「施肥」選項，跳過，請截圖回報")
    end
    yield("/wait 0.1")
end

-- 對單一種植物件執行：互動 -> 讀狀態訊息 -> 依狀態決定動作
local function tendOnePlot(entity)
    Chat.ClearLastMessage()
    entity:SetAsTarget()
    yield("/wait 0.15")
    entity:Interact()

    if not waitForSelectStringAfterInteract(3000) then
        local addon = Addons.GetAddon("SelectString")
        local names = Addons.GetVisibleAddonNames()
        local parts = {}
        for i = 0, names.Count - 1 do table.insert(parts, names[i]) end
        Dalamud.Log(string.format("互動失敗（可能距離太遠或已被其他效果擋住），跳過：%s [Exists=%s Ready=%s] 可見視窗=[%s]",
            tostring(Chat.GetLastMessage()), tostring(addon.Exists), tostring(addon.Ready), table.concat(parts, ",")))
        return
    end

    local statusMsg = Chat.GetLastMessage()
    local status = classifyStatus(statusMsg)
    local options = readSelectStringOptions()
    Dalamud.LogDebug(string.format("訊息=[%s] 狀態=[%s] 選項=[%s]", tostring(statusMsg), tostring(status), table.concat(options, ",")))

    -- 沒抓到已知狀態文字，但選單只有 收穫/取消 兩項，也視為已成熟
    if status == nil and #options == 2 and optionIndex(options, HARVEST_LABEL) ~= nil then
        status = MATURE_TEXT
    end

    if status == MATURE_TEXT then
        Dalamud.Log("已經成熟了，可收穫，跳過，請手動收穫")
        cancelIfPossible(options)
        return
    end

    if status == WITHERED_TEXT then
        Dalamud.Log("已經枯萎了，跳過，需要人工處理")
        cancelIfPossible(options)
        return
    end

    if status == EMPTY_POT_TEXT then
        Dalamud.LogDebug("空花盆，跳過")
        cancelIfPossible(options)
        return
    end

    if status == NEEDS_CARE_TEXT then
        local careIdx = optionIndex(options, CARE_LABEL)
        if careIdx then
            Dalamud.LogDebug("狀態不太好，先護理")
            selectOption(careIdx)

            -- 護理後選單關閉，重新互動才能選施肥
            entity:Interact()
            if not waitForSelectStringAfterInteract(3000) then
                Dalamud.Log("護理後重新互動失敗，跳過施肥")
                return
            end
            local options2 = readSelectStringOptions()
            local fertIdx = optionIndex(options2, FERTILIZE_LABEL)
            if fertIdx then
                Dalamud.LogDebug("護理完成，施肥")
                Chat.ClearLastMessage()
                selectOption(fertIdx)
                tryChooseFertilizerItem()
            end
        else
            Dalamud.Log("狀態不太好，但找不到護理選項，取消跳過")
            cancelIfPossible(options)
        end
        return
    end

    if status == HEALTHY_TEXT then
        local fertIdx = optionIndex(options, FERTILIZE_LABEL)
        if fertIdx then
            Dalamud.LogDebug("茁壯成長，直接施肥")
            Chat.ClearLastMessage()
            selectOption(fertIdx)
            tryChooseFertilizerItem()
        else
            cancelIfPossible(options)
        end
        return
    end

    -- 不認得的狀態，安全起見取消跳過
    Dalamud.Log(string.format("未知狀態訊息 [%s]，取消跳過，請回報這則訊息內容", tostring(statusMsg)))
    cancelIfPossible(options)
end

local plots = findNearbyPlots()
Dalamud.Log(string.format("找到 %d 個附近的種植物件", #plots))

for i, plot in ipairs(plots) do
    Dalamud.Log(string.format("正在照料第 %d/%d 個種植物件", i, #plots))
    tendOnePlot(plot)
    yield("/wait 0.15")
end

Dalamud.Log("所有附近種植物件處理完畢。")
