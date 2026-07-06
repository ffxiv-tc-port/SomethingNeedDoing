--[[
自動收穫指定作物（虛無界風茄）並用指定土壤重新種植

全流程已實機驗證通過：
  - 物件名稱規則、狀態文字來自互動時的聊天訊息、SelectString 選項要濾掉標題行、
    Talk 對話框要用 /click Talk Click 推進、waitForXxxReady 輪詢逾時要抓寬鬆。
  - 空花盆訊息含「沒有種」，選項為 播種/取消。
  - 選「播種」後跳出的是 HousingGardening addon（土壤與種子兩個拖曳格 + 確定/取消按鈕），
    不是右鍵背包道具、也不是拖曳。土壤格/種子格本身可以程式化模擬右鍵（DragDropClick
    事件，which=1 是土壤格、which=2 是種子格，透過 hook ReceiveEvent 實測得到），跳出
    ContextIconMenu（只有一個候選道具時直接選 index 0），確定按鈕是 node id=8，最後會跳
    標準 SelectYesno 二次確認，選「是」(index 0) 完成種植。
]]

local TARGET_PLANT_NAME = "虛無界風茄" -- 只收穫這個作物，其他成熟作物一律跳過不動
local SEED_ITEM_NAME = "虛無界風茄"    -- 種子道具名稱跟作物同名（已確認）
local SOIL_ITEM_NAME = "園藝土壤"
local CONFIRM_BUTTON_NODE_ID = 8 -- HousingGardening 的「確定」按鈕（已實測確認）
local SOIL_SLOT_WHICH = 1
local SEED_SLOT_WHICH = 2

local function isPlantableName(name)
    return name == "園圃" or name:match("花盆$") ~= nil or name:match("花圃$") ~= nil
end

local INTERACT_RANGE = 3.0
local MAX_OBJECT_INDEX = 599

local NEEDS_CARE_TEXT = "狀態不太好"
local HEALTHY_TEXT = "茁壯成長"
local MATURE_TEXT = "已經成熟了"
local WITHERED_TEXT = "已經枯萎了"
local EMPTY_POT_TEXT = "沒有種"
local HARVEST_LABEL = "收穫"
local SOW_LABEL = "播種"
local CANCEL_LABEL = "取消"

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

local function cancelIfPossible(options)
    local idx = optionIndex(options, CANCEL_LABEL)
    if idx then selectOption(idx) end
end

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

local function logVisibleAddons(prefix)
    local names = Addons.GetVisibleAddonNames()
    local parts = {}
    for i = 0, names.Count - 1 do table.insert(parts, names[i]) end
    Dalamud.Log(string.format("%s 可見視窗=[%s]", prefix, table.concat(parts, ",")))
end

-- 對 HousingGardening 的土壤/種子拖曳格模擬右鍵，跳出 ContextIconMenu 後選第一個候選道具
-- （目前只處理背包裡該類型只有一種道具的情況；如果同時有多種土壤/種子，這裡只會選第一個）
local function fillDragDropSlot(which, itemLabel)
    local gardening = Addons.GetAddon("HousingGardening")
    gardening:RightClickDragDropSlot(which)

    if not waitForAddonReady("ContextIconMenu", 1500) then
        Dalamud.Log(string.format("右鍵 %s 格沒有跳出選擇道具選單，中止種植，請人工處理", itemLabel))
        return false
    end

    if not Addons.SelectContextIconMenuEntry(0) then
        Dalamud.Log(string.format("%s 選擇道具失敗，中止種植，請人工處理", itemLabel))
        return false
    end
    yield("/wait 0.2")
    return true
end

-- 選「播種」之後：HousingGardening 開啟 -> 填土壤 -> 填種子 -> 點確定 -> SelectYesno 選是
local function trySowSeed()
    if not waitForAddonReady("HousingGardening", 2000) then
        logVisibleAddons("播種後沒看到 HousingGardening")
        return
    end

    if not fillDragDropSlot(SOIL_SLOT_WHICH, SOIL_ITEM_NAME) then return end
    if not fillDragDropSlot(SEED_SLOT_WHICH, SEED_ITEM_NAME) then return end

    local gardening = Addons.GetAddon("HousingGardening")
    if not gardening:ClickButton(CONFIRM_BUTTON_NODE_ID) then
        Dalamud.Log("點擊確定按鈕失敗（可能還沒填滿或按鈕被擋），中止種植，請人工處理")
        return
    end

    if waitForAddonReady("SelectYesno", 1500) then
        yield('/callback "SelectYesno" true 0') -- 是
        yield("/wait 0.3")
        Dalamud.Log(string.format("已確認種植 %s，結果：%s", SEED_ITEM_NAME, tostring(Chat.GetLastMessage())))
    else
        Dalamud.Log("點確定後沒看到 SelectYesno 二次確認，請自行確認種植狀態")
    end
end

-- 收穫後理論上會立刻變空，重新互動一次進入播種流程
local function harvestThenReplant(entity)
    Dalamud.Log("收穫中")
    local options = readSelectStringOptions()
    local harvestIdx = optionIndex(options, HARVEST_LABEL)
    if not harvestIdx then
        Dalamud.Log("找不到收穫選項，取消跳過")
        cancelIfPossible(options)
        return
    end
    Chat.ClearLastMessage()
    selectOption(harvestIdx)
    Dalamud.Log(string.format("收穫結果：%s", tostring(Chat.GetLastMessage())))

    yield("/wait 0.8") -- 收穫動畫/狀態更新需要一點緩衝時間，太快重新互動會抓不到
    Chat.ClearLastMessage()
    entity:Interact()
    if not waitForSelectStringAfterInteract(4000) then
        logVisibleAddons("收穫後重新互動逾時")
        Dalamud.Log(string.format("收穫後重新互動失敗，跳過種植：%s", tostring(Chat.GetLastMessage())))
        return
    end

    local msg = Chat.GetLastMessage()
    if msg == nil or not msg:find(EMPTY_POT_TEXT, 1, true) then
        Dalamud.Log(string.format("收穫後預期是空盆，但訊息是 [%s]，中止自動種植，請人工確認", tostring(msg)))
        cancelIfPossible(readSelectStringOptions())
        return
    end

    local options2 = readSelectStringOptions()
    local sowIdx = optionIndex(options2, SOW_LABEL)
    if not sowIdx then
        Dalamud.Log("找不到播種選項，取消跳過")
        cancelIfPossible(options2)
        return
    end
    selectOption(sowIdx)
    trySowSeed()
end

local function tendOnePlot(entity)
    Chat.ClearLastMessage()
    entity:SetAsTarget()
    yield("/wait 0.15")
    entity:Interact()

    if not waitForSelectStringAfterInteract(3000) then
        logVisibleAddons("互動逾時")
        Dalamud.Log(string.format("互動失敗，跳過：%s", tostring(Chat.GetLastMessage())))
        return
    end

    local statusMsg = Chat.GetLastMessage()
    local options = readSelectStringOptions()
    Dalamud.LogDebug(string.format("訊息=[%s] 選項=[%s]", tostring(statusMsg), table.concat(options, ",")))

    if statusMsg == nil then statusMsg = "" end

    -- 空盆：直接嘗試播種目標作物
    if statusMsg:find(EMPTY_POT_TEXT, 1, true) then
        local sowIdx = optionIndex(options, SOW_LABEL)
        if sowIdx then
            Dalamud.Log("空盆，嘗試播種 " .. SEED_ITEM_NAME)
            selectOption(sowIdx)
            trySowSeed()
        else
            cancelIfPossible(options)
        end
        return
    end

    -- 已成熟：只收穫我們指定的作物，其他成熟作物一律跳過不動
    if statusMsg:find(MATURE_TEXT, 1, true) then
        if statusMsg:find(TARGET_PLANT_NAME, 1, true) then
            harvestThenReplant(entity)
        else
            Dalamud.LogDebug("已成熟但不是目標作物，跳過：" .. statusMsg)
            cancelIfPossible(options)
        end
        return
    end

    -- 枯萎/健康/需要護理/其他：這個腳本只負責收穫+種植，其餘一律不動
    Dalamud.LogDebug("非成熟/非空盆狀態，本腳本不處理，跳過：" .. statusMsg)
    cancelIfPossible(options)
end

local plots = findNearbyPlots()
Dalamud.Log(string.format("找到 %d 個附近的種植物件", #plots))

for i, plot in ipairs(plots) do
    Dalamud.Log(string.format("正在處理第 %d/%d 個種植物件", i, #plots))
    tendOnePlot(plot)
    yield("/wait 0.15")
end

Dalamud.Log("所有附近種植物件處理完畢。")
