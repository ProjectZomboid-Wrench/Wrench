-- Wrench's loader sets WrenchHost from its OnGameBoot hook, which runs before any Lua event callback, so by the time
-- the main menu opens it is present exactly when the loader is installed.

WrenchCheck = {}
WrenchCheck.url = "https://github.com/LambdaSix/PZNet"
-- PZ's openUrl and Steam overlay only open steamcommunity.com and Indie Stone sites, so the GitHub page can only be
-- copied. Set this to the metamod's Workshop page once published, and the button opens that instead.
WrenchCheck.workshopUrl = nil

local function requiresWrench(info)
    local require = info:getRequire()
    if not require then
        return false
    end
    for i = 0, require:size() - 1 do
        -- B42 writes requirements as \ModId; older mods omit the backslash.
        local id = string.gsub(require:get(i), "^\\", "")
        if id == "Wrench" then
            return true
        end
    end
    return false
end

local function dependentModNames()
    local names = {}
    local active = getActivatedMods()
    for i = 0, active:size() - 1 do
        local info = getModInfoByID(active:get(i))
        if info and info:getId() ~= "Wrench" and requiresWrench(info) then
            table.insert(names, info:getName())
        end
    end
    return names
end

local function openWorkshopPage()
    if isSteamOverlayEnabled() then
        activateSteamOverlayToWebPage(WrenchCheck.workshopUrl)
    else
        openUrl(WrenchCheck.workshopUrl)
    end
end

function WrenchCheck.onModalClick(target, button)
    if button.internal ~= "YES" then
        return
    end
    if WrenchCheck.workshopUrl then
        openWorkshopPage()
    else
        Clipboard.setClipboard(WrenchCheck.url)
    end
end

-- Permission prompts for .NET mods are not Lua's job: Wrench shows them as its own game state, out of reach of
-- Lua, so no Lua code (including this mod) can answer them.
function WrenchCheck.onMainMenuEnter()
    if WrenchHost or WrenchCheck.shown then
        return
    end
    WrenchCheck.shown = true

    local text = "<CENTRE> <RGB:1,0.8,0.2> Wrench is not installed <LINE> <LINE> <RGB:1,1,1> "
    local names = dependentModNames()
    if #names > 0 then
        text = text .. "These mods need it and won't work without it: <LINE> " .. table.concat(names, ", ") .. " <LINE> <LINE> "
    end
    text = text .. "Wrench is a loader that runs alongside the game and isn't installed through the Workshop. "
        .. "Installation instructions: <LINE> " .. WrenchCheck.url

    local width, height = 520, 260
    local modal = ISModalRichText:new(getCore():getScreenWidth() / 2 - width / 2,
        getCore():getScreenHeight() / 2 - height / 2, width, height, text, true, nil, WrenchCheck.onModalClick)
    modal:initialise()
    modal.yes:setTitle(WrenchCheck.workshopUrl and "Open Workshop page" or "Copy link")
    modal.no:setTitle("Close")
    modal:addToUIManager()
    modal:setAlwaysOnTop(true)
end

Events.OnMainMenuEnter.Add(WrenchCheck.onMainMenuEnter)
