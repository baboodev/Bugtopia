using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace HeartopiaMod
{
    internal static class LocalizationManager
    {
        // Display names shown in the Settings > Localization UI.
        private static readonly Dictionary<string, string> LanguageNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "en", "English" },
            { "es", "Español" },
            { "zh-CN", "简体中文" },
            { "pt-BR", "Português (Brasil)" },
            { "th", "ไทย" }
        };

        // Built-in English fallback strings. These are also used as stable translation keys.
        private static readonly Dictionary<string, string> EnglishDefaults = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Main navigation / tabs
            { "Self", "Self" },
            { "Resource Gathering", "Resource Gathering" },
            { "Features", "Features" },
            { "New Features", "New Features" },
            { "Daily Quests", "Daily Quests" },
            { "Collect Daily Quest Log", "Collect Daily Quest Log" },
            { "Auto Submit Daily Items", "Auto Submit Daily Items" },
            { "Submit Bird Photo", "Submit Bird Photo" },
            { "Skip 5 Star Items", "Skip 5 Star Items" },
            { "Radar", "Radar" },
            { "Teleport", "Teleport" },
            { "Bag / Warehouse", "Bag / Warehouse" },
            { "Settings", "Settings" },
            { "Main", "Main" },
            { "Building", "Building" },
            { "Foraging", "Foraging" },
            { "Chop & Mine", "Chop & Mine" },
            { "Fishing", "Fishing" },
            { "Insects", "Insects" },
            { "Food & Repair", "Food & Repair" },
            { "Snow Sculpting", "Snow Sculpting" },
            { "Auto Buy", "Auto Buy" },
            { "AUTO BUY (Cooking Store)", "AUTO BUY (Cooking Store)" },
            { "BUY ALL (COIN)", "BUY ALL (COIN)" },
            { "Shop buy-all already running", "Shop buy-all already running" },
            { "Auto Buy: Teleport -> Buy -> Return", "Auto Buy: Teleport -> Buy -> Return" },
            { "ENABLE AUTO FORAGING", "ENABLE AUTO FORAGING" },
            { "DISABLE AUTO FORAGING", "DISABLE AUTO FORAGING" },
            { "Aura Farm", "Aura Farm" },
            { "Status:", "Status:" },
            { "✗ No radar toggles selected", "✗ No radar toggles selected" },
            { "✗ No radar toggles selected\n   Auto Farm disabled...", "✗ No radar toggles selected\n   Auto Farm disabled..." },
            { "✗ Radar is OFF!\n   Enable Radar first.", "✗ Radar is OFF!\n   Enable Radar first." },
            { "✓ Ready", "✓ Ready" },
            { "Area Load Delay: {0}s", "Area Load Delay: {0}s" },
            { "Resolver: STANDBY", "Resolver: STANDBY" },
            { "Resolver: READY", "Resolver: READY" },
            { "Resolver: RESOLVING / NOT READY", "Resolver: RESOLVING / NOT READY" },
            { "LOOT PRIORITIES", "LOOT PRIORITIES" },
            { "Mushrooms:", "Mushrooms:" },
            { "Events:", "Events:" },
            { "Other:", "Other:" },
            { "Priority Location: None", "Priority Location: None" },
            { "Available: {0}", "Available: {0}" },
            { "Markers: {0}", "Markers: {0}" },
            { "RADAR SETTINGS", "RADAR SETTINGS" },
            { "Marker Style:", "Marker Style:" },
            { "Default", "Default" },
            { "Default ✓", "Default ✓" },
            { "Simple Text", "Simple Text" },
            { "Simple Text ✓", "Simple Text ✓" },
            { "Radar markers: Default", "Radar markers: Default" },
            { "Radar markers: Simple Text", "Radar markers: Simple Text" },
            { "Failed to save radar settings", "Failed to save radar settings" },
            { "Radar Max Distance: {0}m", "Radar Max Distance: {0}m" },
            { "ENABLE RADAR", "ENABLE RADAR" },
            { "DISABLE RADAR", "DISABLE RADAR" },
            { "Select All Loots", "Select All Loots" },
            { "Clear All Loots", "Clear All Loots" },
            { "Force Refresh Scan", "Force Refresh Scan" },
            { "None", "None" },
            { "Mushrooms", "Mushrooms" },
            { "Berries", "Berries" },
            { "Resources", "Resources" },
            { "Trees", "Trees" },
            { "Misc", "Misc" },
            { "All Mushrooms", "All Mushrooms" },
            { "Black Truffle", "Black Truffle" },
            { "Stones", "Stones" },
            { "Ores", "Ores" },
            { "Rare Trees", "Rare Trees" },
            { "Apple Trees", "Apple Trees" },
            { "Mandarin Trees", "Mandarin Trees" },
            { "Fish Shadows", "Fish Shadows" },
            { "Meteors", "Meteors" },
            { "Click Duration: {0:F1}s", "Click Duration: {0:F1}s" },
            { "Auto-Repair Tool (Paused TP FARM): {0:F0}s", "Auto-Repair Tool (Paused TP FARM): {0:F0}s" },
            { "Fish Detect Range", "Fish Detect Range" },
            { "Max Reel", "Max Reel" },
            { "Hold Time", "Hold Time" },
            { "Pause", "Pause" },
            { "Equip Rod", "Equip Rod" },
            { "Teleport Fishing", "Teleport Fishing" },
            { "Enable Auto Fishing", "Enable Auto Fishing" },
            { "Disable Auto Fishing", "Disable Auto Fishing" },
            { "Equip Axe", "Equip Axe" },
            { "ENABLE CHOP & MINE", "ENABLE CHOP & MINE" },
            { "DISABLE CHOP & MINE", "DISABLE CHOP & MINE" },
            { "Auto Collect", "Auto Collect" },
            { "Auto Pickup Drops", "Auto Pickup Drops" },
            { "Pickup Distance: {0}m", "Pickup Distance: {0}m" },
            { "Collect Types:", "Collect Types:" },
            { "  Mushrooms", "  Mushrooms" },
            { "  Berries / Bushes / Plants", "  Berries / Bushes / Plants" },
            { "  Event Resources", "  Event Resources" },
            { "Hide UI + Player (Client Side)", "Hide UI + Player (Client Side)" },
            { "Hide Jump Button (Space still works)", "Hide Jump Button (Space still works)" },
            { "Bunny Hop (hold Space)", "Bunny Hop (hold Space)" },
            { "Bird Vacuum (Client Side)", "Bird Vacuum (Client Side)" },
            { "Custom Camera FOV", "Custom Camera FOV" },
            { "DISABLE ALL", "DISABLE ALL" },
            { "REFRESH & SCAN", "REFRESH & SCAN" },
            { "Auto Repair", "Auto Repair" },
            { "Eat Selected Food", "Eat Selected Food" },
            { "Repair Teleport Backward", "Repair Teleport Backward" },
            { "❄️ Auto Snow Sculpture", "❄️ Auto Snow Sculpture" },
            { "Auto Click Icon", "Auto Click Icon" },
            { "Move snowballs to backpack", "Move snowballs to backpack" },
            { "Idle", "Idle" },
            { "DISABLED", "DISABLED" },
            { "GATHERING...", "GATHERING..." },
            { "TELEPORTING...", "TELEPORTING..." },
            { "IDLE", "IDLE" },
            { "Running (step {0})", "Running (step {0})" },
            { "Running (step {0}) - {1}", "Running (step {0}) - {1}" },
            { "Game Speed: {0:F1}x", "Game Speed: {0:F1}x" },
            { "Camera FOV: {0:F0}°", "Camera FOV: {0:F0}°" },
            { "Bag Automation", "Bag Automation" },
            { "Repair Status: {0}", "Repair Status: {0}" },
            { "Eat Status: {0}", "Eat Status: {0}" },
            { "Current Energy: {0}", "Current Energy: {0}" },
            { "Bag automation already running", "Bag automation already running" },
            { "Auto Eat will continue until energy is full.", "Auto Eat will continue until energy is full." },
            { "Auto Eat Energy Panel", "Auto Eat Energy Panel" },
            { "Auto Eat Trigger: {0}% or lower", "Auto Eat Trigger: {0}% or lower" },
            { "Food Type", "Food Type" },
            { "Auto Repair: open bag → find {0} → Use → close bag\nAuto Eat: open bag → find {1} → Use → close bag", "Auto Repair: open bag → find {0} → Use → close bag\nAuto Eat: open bag → find {1} → Use → close bag" },
            { "AUTO SNOW SCULPTURE", "AUTO SNOW SCULPTURE" },
            { "Click Interval: {0:F0}ms", "Click Interval: {0:F0}ms" },
            { "Interval: {0:F0}ms", "Interval: {0:F0}ms" },
            { "State: {0}", "State: {0}" },
            { "Current Ingredient: {0}", "Current Ingredient: {0}" },
            { "Max per ingredient: {0}", "Max per ingredient: {0}" },
            { "Home", "Home" },
            { "Animal Care", "Animal Care" },
            { "NPCs", "NPCs" },
            { "Locations", "Locations" },
            { "Events", "Events" },
            { "House", "House" },
            { "Custom", "Custom" },
            { "Keybinds", "Keybinds" },
            { "UI Theme", "UI Theme" },
            // Settings / localization
            { "Localization", "Localization" },
            { "Current Language: {0}", "Current Language: {0}" },
            { "Language switched to {0}", "Language switched to {0}" },
            { "SETTINGS", "SETTINGS" },
            { "KEYBIND SETTINGS", "KEYBIND SETTINGS" },
            { "PRESS ANY KEY FOR:", "PRESS ANY KEY OR MOUSE BUTTON FOR:" },
            { "CANCEL", "CANCEL" },
            { "RESET TO DEFAULTS", "RESET TO DEFAULTS" },
            { "Defaults restored (Toggle Menu: Insert)", "Defaults restored (Toggle Menu: Insert)" },
            { "Enable Notifications", "Enable Notifications" },
            { "Notifications enabled", "Notifications enabled" },
            { "Auto Start on Lobby", "Auto Start on Lobby" },
            { "Auto Start enabled", "Auto Start enabled" },
            { "Auto Start disabled", "Auto Start disabled" },
            { "Auto Close Announcements", "Auto Close Announcements" },
            { "Auto Close Announcement enabled", "Auto Close Announcement enabled" },
            { "Auto Close Announcement disabled", "Auto Close Announcement disabled" },
            { "Hide ID", "Hide ID" },
            { "ID display hidden", "ID display hidden" },
            { "ID display shown", "ID display shown" },
            { "Custom ID", "Custom ID" },
            { "Custom ID enabled", "Custom ID enabled" },
            { "Custom ID disabled", "Custom ID disabled" },
            { "Value", "Value" },
            { "Leave blank for your real ID. If filled, it replaces the visible ID.", "Leave blank for your real ID. If filled, it replaces the visible ID." },
            { "Custom ID cleared", "Custom ID cleared" },
            { "Custom ID updated", "Custom ID updated" },
            { "Show Status Overlay", "Show Status Overlay" },
            { "Status overlay enabled", "Status overlay enabled" },
            { "Status overlay disabled", "Status overlay disabled" },
            { "Block Input", "Block Input" },
            { "Block Input Enabled", "Block Input Enabled" },
            { "Block Input Disabled", "Block Input Disabled" },
            { "FPS Bypass", "FPS Bypass" },
            { "FPS Bypass Enabled", "FPS Bypass Enabled" },
            { "FPS Bypass Disabled", "FPS Bypass Disabled" },
            { "Target Max FPS: {0}", "Target Max FPS: {0}" },
            { "Effective cap: {0}  |  Live: {1:F0} FPS", "Effective cap: {0}  |  Live: {1:F0} FPS" },
            { "Join Friend", "Join Friend" },
            { "Join My Town", "Join My Town" },
            { "Join Public", "Join Public" },
            // Status / overlay labels
            { "Active", "Active" },
            { "Running", "Running" },
            { "Fish Farm", "Fish Farm" },
            { "Target", "Target" },
            { "Caught", "Caught" },
            { "No active features", "No active features" },
            { "Keybind", "Keybind" },
            { "Accent Color", "Accent Color" },
            { "Status Overlay", "Status Overlay" },
            { "Tab", "Tab" },
            { "Speed", "Speed" },
            { "Enabled", "Enabled" },
            { "Disabled", "Disabled" },
            { "Farm Status", "Farm Status" },
            { "Fish Status", "Fish Status" },
            // Radar / marker labels
            { "Mushroom", "Mushroom" },
            { "Blueberry", "Blueberry" },
            { "Raspberry", "Raspberry" },
            { "Bubble", "Bubble" },
            { "Bird", "Bird" },
            { "Insect", "Insect" },
            { "Rare Tree", "Rare Tree" },
            { "Apple Tree", "Apple Tree" },
            { "Mandarin Tree", "Mandarin Tree" },
            { "Tree", "Tree" },
            { "Farm Rocks", "Farm Rocks" },
            { "Farm Ores", "Farm Ores" },
            { "Farm Trees", "Farm Trees" },
            { "Farm Rare Trees", "Farm Rare Trees" },
            { "Farm Apple Trees", "Farm Apple Trees" },
            { "Farm Mandarin Trees", "Farm Mandarin Trees" },
            { "Reset Cooldowns", "Reset Cooldowns" },
            { "Chop & Mine flow:", "Chop & Mine flow:" },
            { "• Build list of available markers", "• Build list of available markers" },
            { "• Shuffle and teleport to markers", "• Shuffle and teleport to markers" },
            { "• Simulate F key for configured duration", "• Simulate F key for configured duration" },
            { "• Mark resource collected and set cooldowns", "• Mark resource collected and set cooldowns" },
            { "Fish Shadow", "Fish Shadow" },
            { "Meteor", "Meteor" },
            { "Stone", "Stone" },
            { "Ore", "Ore" },
            { "Oyster", "Oyster" },
            { "Button", "Button" },
            { "Oyster Mushroom", "Oyster Mushroom" },
            { "Button Mushroom", "Button Mushroom" },
            { "Penny Bun", "Penny Bun" },
            { "Shiitake", "Shiitake" },
            { "Truffle", "Truffle" },
            { "Fiddlehead", "Fiddlehead" },
            { "Burdock", "Burdock" },
            { "Mustard Greens", "Mustard Greens" },
            { "Tall Mustard", "Tall Mustard" },
            { "Blueberries", "Blueberries" },
            { "Raspberries", "Raspberries" },
            { "Bubbles", "Bubbles" },
            { "Birds", "Birds" },
            // Food & repair options
            { "Repair Kit", "Repair Kit" },
            { "Crafty Repair Kit", "Crafty Repair Kit" },
            // Auto Buy ingredient matching
            { "Springday Brown Sugar", "Springday Brown Sugar" },
            { "Salsa Sauce", "Salsa Sauce" },
            { "Pasteurized Egg", "Pasteurized Egg" },
            { "Meat", "Meat" },
            { "Red Bean", "Red Bean" },
            { "Egg", "Egg" },
            { "Milk", "Milk" },
            { "Rice Flour", "Rice Flour" },
            { "Tea Leaves", "Tea Leaves" },
            { "Cooking Oil", "Cooking Oil" },
            { "Matcha Powder", "Matcha Powder" },
            { "Cheese", "Cheese" },
            { "Butter", "Butter" },
            { "Coffee Beans", "Coffee Beans" },
            // Auto Eat food options
            { "Bad Food", "Bad Food" },
            { "Blue Jam", "Blue Jam" },
            { "Rasp Jam", "Rasp Jam" },
            { "Mix Jam", "Mix Jam" },
            { "Bake Mushroom", "Bake Mushroom" },
            { "Salad", "Salad" },
            { "Any Food", "Any Food" },
            // Interaction / automation text
            { "Tool durability depleted", "Tool durability depleted" },
            { "Scanner Durability low", "Scanner Durability low" },
            { "Use", "Use" },
            { "Eat", "Eat" },
            { "Equip Net", "Equip Net" },
            { "Equip Bird Scanner", "Equip Bird Scanner" },
            { "DISABLE INSECT CATCHING", "DISABLE INSECT CATCHING" },
            { "ENABLE INSECT CATCHING", "ENABLE INSECT CATCHING" },
            { "DISABLE BIRD CATCHING", "DISABLE BIRD CATCHING" },
            { "ENABLE BIRD CATCHING", "ENABLE BIRD CATCHING" },
            { "Status: {0}", "Status: {0}" },
            { "Auto Stop Timer", "Auto Stop Timer" },
            { "Timer (HH:MM:SS)", "Timer (HH:MM:SS)" },
            { "Set at least 1 second to enable auto-stop.", "Set at least 1 second to enable auto-stop." },
            { "Auto-stop after: {0}", "Auto-stop after: {0}" },
            { "Time remaining: {0}", "Time remaining: {0}" },
            { "Teleport Cooldown: {0:F1}s", "Teleport Cooldown: {0:F1}s" },
            { "Scan Timeout: {0:F1}s", "Scan Timeout: {0:F1}s" },
            { "Auto-Repair (Paused TP Farm): {0:F0}s", "Auto-Repair (Paused TP Farm): {0:F0}s" },
            { "Teleport Offset: {0:F2}m", "Teleport Offset: {0:F2}m" },
            { "Multi-Catch Limit: {0}", "Multi-Catch Limit: {0}" },
            { "Auto Insect Farm", "Auto Insect Farm" },
            { "Auto Bird Farm", "Auto Bird Farm" },
            { "Caught This Session: {0}", "Caught This Session: {0}" },
            { "Catch Cooldown: {0:F1}s", "Catch Cooldown: {0:F1}s" },
            { "Scan Range: {0:F0}m", "Scan Range: {0:F0}m" },
            { "Tool: {0}", "Tool: {0}" },
            { "Unknown", "Unknown" },
            { "Insect Farm Enabled", "Insect Farm Enabled" },
            { "Insect Farm Disabled", "Insect Farm Disabled" },
            { "Insect Farm auto-stop set: {0}", "Insect Farm auto-stop set: {0}" },
            { "Insect Farm auto-stopped (timer)", "Insect Farm auto-stopped (timer)" },
            { "Bird Farm Enabled", "Bird Farm Enabled" },
            { "Bird Farm Disabled", "Bird Farm Disabled" },
            { "Bird Farm auto-stop set: {0}", "Bird Farm auto-stop set: {0}" },
            { "Bird Farm auto-stopped (timer)", "Bird Farm auto-stopped (timer)" },
            { "Auto Repair started", "Auto Repair started" },
            { "Auto Repair already running", "Auto Repair already running" },
            { "Auto Eat started ({0})", "Auto Eat started ({0})" },
            { "Auto Eat already running", "Auto Eat already running" },
            { "Use Bait", "Use Bait" },
            { "Use Attractor", "Use Attractor" },
            { "Bait used", "Bait used" },
            { "Attractor used", "Attractor used" },
            { "No bait found in bag", "No bait found in bag" },
            { "No attractor found in bag", "No attractor found in bag" },
            { "Use Bait spreads bait near water (not while fishing). Assign a hotkey in Settings.", "Use Bait spreads bait near water (not while fishing). Assign a hotkey in Settings." },
            { "Use Attractor places a fish attractor near water (not while fishing). Assign a hotkey in Settings.", "Use Attractor places a fish attractor near water (not while fishing). Assign a hotkey in Settings." },
            { "Noclip", "Noclip" },
            { "Vehicle Bypass", "Vehicle Bypass" },
            { "Vehicle Bypass Server Events", "Vehicle Bypass Server Events" },
            { "Noclip Speed: {0:F1}", "Noclip Speed: {0:F1}" },
            { "Noclip Boost: {0:F1}x", "Noclip Boost: {0:F1}x" },
            { "Anti AFK (Auto Click)", "Anti AFK (Auto Click)" },
            { "AFK Click Interval: {0:F0}s", "AFK Click Interval: {0:F0}s" },
            { "Noclip: WASD + Space/Ctrl\nShift = Speed Boost", "Noclip: WASD + Space/Ctrl\nShift = Speed Boost" },
            { "Building - Bypass Overlap", "Building - Bypass Overlap" },
            { "Bypass Overlap", "Bypass Overlap" },
            { "Free Placement", "Free Placement" },
            { "Angle", "Angle" },
            { "Grid", "Grid" },
            { "No surface limit", "No surface limit" },
            { "No range/height limit", "No range/height limit" },
            { "no object", "no object" },
            { "Height", "Height" },
            { "Plane", "Plane" },
            { "Credits: • evermoreee12 for Bypass Overlap", "Credits: • evermoreee12 for Bypass Overlap" },
            { "Auto Repair triggered by durability notification - pausing farm for {0:F0}s", "Auto Repair triggered by durability notification - pausing farm for {0:F0}s" },
            { "Auto Eat triggered by energy panel ({0})", "Auto Eat triggered by energy panel ({0})" },
            { "Keybinds saved", "Keybinds saved" },
            { "Keybinds loaded", "Keybinds loaded" },
            { "Failed to save keybinds", "Failed to save keybinds" },
            { "Failed to load keybinds", "Failed to load keybinds" },
            { "UI theme saved", "UI theme saved" },
            { "UI theme loaded", "UI theme loaded" },
            { "Failed to save UI theme", "Failed to save UI theme" },
            { "Failed to load UI theme", "Failed to load UI theme" },
            // Custom Food feature
            { "Custom Food", "Custom Food" },
            { "Custom Food: Click any food item in your bag to select it", "Custom Food: Click any food item in your bag to select it" },
            { "Detected food click: {0}", "Detected food click: {0}" },
            { "Opening bag to scan for food items...", "Opening bag to scan for food items..." },
            { "No food items found in bag.", "No food items found in bag." },
            { "Found {0} food item(s) in bag.", "Found {0} food item(s) in bag." },
            { "Click 'Scan Bag' to find food items.", "Click 'Scan Bag' to find food items." },
            { "Open your bag and click 'Scan Bag'.", "Open your bag and click 'Scan Bag'." },
            { "Custom food set to: {0}", "Custom food set to: {0}" },
            // Pictures decrypt
            { "pictures.title", "Pictures" },
            { "pictures.decrypt_all", "Decrypt all" },
            { "pictures.busy", "Decrypt already running" },
            { "pictures.decrypting", "Decrypting ScreenCapture files..." },
            { "pictures.source_missing", "ScreenCapture folder not found" },
            { "pictures.paths", "From:\n{0}\n\nTo:\n{1}" },
            { "pictures.progress", "Processing {0}/{1} — decrypted {2}, plain {3}, failed {4}, skipped {5}" },
            { "pictures.done", "Done: {0} new ({1} decrypted, {2} plain, {3} failed, {4} skipped)\n{5}" },
            { "pictures.done_short", "Exported {0} new file(s), skipped {1} — {2}" },
            { "pictures.encrypt_changed", "Encrypt changed" },
            { "pictures.scan_changed", "Scan changed" },
            { "pictures.encrypting", "Encrypting changed files..." },
            { "pictures.manifest_missing", "Manifest missing — run Decrypt all first" },
            { "pictures.changed_count", "Changed files: {0}" },
            { "pictures.no_changes", "No changed files (hashes match manifest)" },
            { "pictures.encrypt_progress", "Encrypt {0}/{1} — imported {2}, failed {3}" },
            { "pictures.encrypt_done", "Re-imported {0} file(s), {1} failed\n{2}" },
            { "pictures.encrypt_done_short", "Re-imported {0} changed file(s)" },
            { "pictures.draw_hint", "Draw: open your drawing in the in-game editor, press Extract to save drawing.png, edit that PNG, then press Upload to re-import it into the game." },
            { "pictures.done_draw", "Done: {0} new ({1} decrypted, {2} plain, {3} failed, {4} skipped, {5} draw previews)\n{6}" },
            // Ice skating — network score sender
            { "ice_skating.send_score_title", "Send Ice Skating score" },
            { "ice_skating.challenge_button", "Challenge (5 perfect)" },
            { "ice_skating.challenge_score", "Score" },
            { "ice_skating.runs", "Runs" },
            { "ice_skating.perfect_drill", "Perfect Drill" },
            // Homeland Farm
            { "homeland_farm.title", "Homeland Farm" },
            // Extra / Craft
            { "extra.title", "Extra" },
            { "craft.open", "Open Craft" },
            { "craft.open_ok", "Craft panel opened." },
            { "craft.aura_unavailable", "Craft unavailable (AuraMono not ready)." },
            { "craft.player_unavailable", "Local player unavailable." },
            { "craft.level_object_unavailable", "Craft table object unavailable." },
            { "craft.open_interact_unavailable", "OpenInteractPanel not available." },
            { "craft.open_interact_threw", "Failed to open craft panel." },
            { "craft.level_manager_unavailable", "Level object manager unavailable." },
            { "craft.dictionary_unavailable", "Level object list unavailable." },
            { "craft.no_level_objects", "No level objects found. Enter your homeland first." },
            { "craft.all_tables_stored", "All craft tables are stored. Place a workbench in your homeland." },
            { "craft.no_table", "No craft table found. Stand near a workbench in your homeland." },
            { "homeland_farm.water_section", "WATER" },
            { "homeland_farm.water_radius", "Radius: {0}m" },
            { "homeland_farm.water_in_radius", "Water in radius" },
            { "homeland_farm.water_own", "Water own" },
            { "homeland_farm.water_friends", "Water friends" },
            { "homeland_farm.water_unwatered", "Water unwatered" },
            { "homeland_farm.harvest_section", "HARVEST" },
            { "homeland_farm.harvest_crops_all", "Harvest all own crops" },
            { "homeland_farm.plant_seeds_section", "PLANT SEEDS" },
            { "homeland_farm.collect_plant_seeds_all", "Collect all own plant seeds" },
            { "homeland_farm.collect_dormant", "Collect dormant" },
            { "homeland_farm.collect_flowers", "Collect flowers" },
            { "homeland_farm.weeds_section", "WEEDS" },
            { "homeland_farm.weed_all", "Weed all own" },
            { "homeland_farm.sow_section", "SOW CROPS" },
            { "homeland_farm.sow", "Sow" },
            { "homeland_farm.sow_all", "Sow" },
            { "homeland_farm.sow_in_radius", "Sow" },
            { "homeland_farm.radius_section", "FARM RADIUS" },
            { "homeland_farm.auto_section", "AUTO FARMING" },
            { "homeland_farm.auto_capture", "Capture planters" },
            { "homeland_farm.auto_captured", "Captured {0} planter(s)" },
            { "homeland_farm.auto_not_captured", "Not captured — press Capture planters" },
            { "homeland_farm.auto_hint", "Sows the selected seed, weeds and harvests until seeds run out." },
            { "homeland_farm.auto_start", "Start auto farm" },
            { "homeland_farm.auto_stop", "Stop auto farm" },
            { "homeland_farm.auto_fertilize", "Auto fertilize after sow" },
            { "homeland_farm.fertilize_section", "FERTILIZE CROPS" },
            { "homeland_farm.fertilize", "Fertilize" },
            { "homeland_farm.fertilize_all", "Fertilize" },
            { "homeland_farm.fertilize_in_radius", "Fertilize" },
            { "homeland_farm.stop", "Stop" },
            { "homeland_farm.status_idle", "Idle." },
            { "homeland_farm.status_stopped", "Stopped." },
            { "homeland_farm.need_homeland", "Enter homeland first." },
            { "homeland_farm.seed_storage", "Seed source" },
            { "homeland_farm.fert_storage", "Fertilizer source" },
            { "homeland_farm.storage_backpack", "Backpack" },
            { "homeland_farm.storage_warehouse", "Warehouse" },
            { "homeland_farm.storage_both", "Both" },
            { "homeland_farm.refresh", "Refresh" },
            { "homeland_farm.refresh_seeds", "Refresh seeds" },
            { "homeland_farm.refresh_fertilizers", "Refresh fertilizers" },
            { "homeland_farm.cached_seeds", "Cached {0} seed(s)" },
            { "homeland_farm.press_refresh_seeds", "Press Refresh seeds" },
            { "homeland_farm.cached_fertilizers", "Cached {0} fertilizer(s)" },
            { "homeland_farm.press_refresh_fertilizers", "Press Refresh fertilizers" },
            { "homeland_farm.prev", "<" },
            { "homeland_farm.next", ">" },
            { "homeland_farm.no_seeds", "No crop seeds found." },
            { "homeland_farm.no_fertilizers", "No crop fertilizers found." },
            { "homeland_farm.operations_section", "OPERATIONS" },
            { "homeland_farm.radius_slider_label", "Radius" },
            { "homeland_farm.log_water_radius", "Log water diagnostics" },
            { "homeland_farm.log_water_failed", "Water log failed." },
            { "homeland_farm.log_water_done", "Water log done." },
            { "homeland_farm.status_warming", "Warming up..." },
        };

        // Built-in Spanish fallback strings.
        private static readonly Dictionary<string, string> SpanishDefaults = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Main navigation / tabs
            { "Self", "Personal" },
            { "Resource Gathering", "Recolección" },
            { "Features", "Funciones" },
            { "New Features", "Nuevas funciones" },
            { "Radar", "Radar" },
            { "Teleport", "Teletransporte" },
            { "Bag / Warehouse", "Bolsa / Almacén" },
            { "Settings", "Ajustes" },
            { "Main", "Principal" },
            { "Building", "Construcción" },
            { "Foraging", "Forrajeo" },
            { "Chop & Mine", "Talar y minar" },
            { "Fishing", "Pesca" },
            { "Insects", "Insectos" },
            { "Food & Repair", "Comida y reparación" },
            { "Snow Sculpting", "Escultura de nieve" },
            { "Auto Buy", "Compra automática" },
            { "AUTO BUY (Cooking Store)", "COMPRA AUTOMATICA (Tienda de cocina)" },
            { "BUY ALL (COIN)", "COMPRAR TODO (MONEDAS)" },
            { "Shop buy-all already running", "Compra total ya en curso" },
            { "Auto Buy: Teleport -> Buy -> Return", "Compra automatica: Teletransporte -> Comprar -> Regresar" },
            { "ENABLE AUTO FORAGING", "ACTIVAR RECOLECCION AUTOMATICA" },
            { "DISABLE AUTO FORAGING", "DESACTIVAR RECOLECCION AUTOMATICA" },
            { "Aura Farm", "Granja de aura" },
            { "Status:", "Estado:" },
            { "✗ No radar toggles selected", "✗ No hay opciones de radar seleccionadas" },
            { "✗ No radar toggles selected\n   Auto Farm disabled...", "✗ No hay opciones de radar seleccionadas\n   Recoleccion automatica desactivada..." },
            { "✗ Radar is OFF!\n   Enable Radar first.", "✗ El radar esta apagado\n   Activa el radar primero." },
            { "✓ Ready", "✓ Listo" },
            { "Area Load Delay: {0}s", "Espera de carga de zona: {0}s" },
            { "Resolver: STANDBY", "Resolver: EN ESPERA" },
            { "Resolver: READY", "Resolver: LISTO" },
            { "Resolver: RESOLVING / NOT READY", "Resolver: RESOLVIENDO / NO LISTO" },
            { "LOOT PRIORITIES", "PRIORIDADES DE BOTIN" },
            { "Mushrooms:", "Hongos:" },
            { "Events:", "Eventos:" },
            { "Other:", "Otros:" },
            { "Priority Location: None", "Ubicacion prioritaria: Ninguna" },
            { "Available: {0}", "Disponibles: {0}" },
            { "Markers: {0}", "Marcadores: {0}" },
            { "RADAR SETTINGS", "AJUSTES DE RADAR" },
            { "Marker Style:", "Estilo de marcador:" },
            { "Default", "Predeterminado" },
            { "Default ✓", "Predeterminado ✓" },
            { "Simple Text", "Texto simple" },
            { "Simple Text ✓", "Texto simple ✓" },
            { "Radar markers: Default", "Marcadores de radar: Predeterminado" },
            { "Radar markers: Simple Text", "Marcadores de radar: Texto simple" },
            { "Failed to save radar settings", "No se pudieron guardar los ajustes del radar" },
            { "Radar Max Distance: {0}m", "Distancia maxima del radar: {0}m" },
            { "ENABLE RADAR", "ACTIVAR RADAR" },
            { "DISABLE RADAR", "DESACTIVAR RADAR" },
            { "Select All Loots", "Seleccionar todo" },
            { "Clear All Loots", "Limpiar todo" },
            { "Force Refresh Scan", "Forzar escaneo" },
            { "None", "Ninguno" },
            { "Mushrooms", "Hongos" },
            { "Berries", "Bayas" },
            { "Resources", "Recursos" },
            { "Trees", "Arboles" },
            { "Misc", "Varios" },
            { "All Mushrooms", "Todos los hongos" },
            { "Black Truffle", "Trufa negra" },
            { "Stones", "Piedras" },
            { "Ores", "Minerales" },
            { "Rare Trees", "Arboles raros" },
            { "Apple Trees", "Manzanos" },
            { "Mandarin Trees", "Arboles mandarinos" },
            { "Fish Shadows", "Sombras de peces" },
            { "Meteors", "Meteoritos" },
            { "Click Duration: {0:F1}s", "Duracion del clic: {0:F1}s" },
            { "Auto-Repair Tool (Paused TP FARM): {0:F0}s", "Herramienta de autorreparacion (TP pausado): {0:F0}s" },
            { "Fish Detect Range", "Rango de deteccion de peces" },
            { "Max Reel", "Tirado maximo" },
            { "Hold Time", "Tiempo de mantener" },
            { "Pause", "Pausa" },
            { "Equip Rod", "Equipar caña" },
            { "Teleport Fishing", "Teletransporte de pesca" },
            { "Enable Auto Fishing", "Activar pesca automática" },
            { "Disable Auto Fishing", "Desactivar pesca automática" },
            { "Equip Axe", "Equipar hacha" },
            { "ENABLE CHOP & MINE", "ACTIVAR TALAR Y MINAR" },
            { "DISABLE CHOP & MINE", "DESACTIVAR TALAR Y MINAR" },
            { "Auto Collect", "Recolección automática" },
            { "Collect Types:", "Tipos de recolección:" },
            { "  Mushrooms", "  Hongos" },
            { "  Berries / Bushes / Plants", "  Bayas / arbustos / plantas" },
            { "  Event Resources", "  Recursos de evento" },
            { "Hide UI + Player (Client Side)", "Ocultar UI + jugador (lado del cliente)" },
            { "Hide Jump Button (Space still works)", "Ocultar botón de salto (Espacio sigue funcionando)" },
            { "Bunny Hop (hold Space)", "Bunny hop (mantén Espacio)" },
            { "Bird Vacuum (Client Side)", "Vacío de aves (lado del cliente)" },
            { "Custom Camera FOV", "FOV de cámara personalizado" },
            { "DISABLE ALL", "DESACTIVAR TODO" },
            { "REFRESH & SCAN", "ACTUALIZAR Y ESCANEAR" },
            { "Auto Repair", "Auto reparar" },
            { "Eat Selected Food", "Comer comida seleccionada" },
            { "Repair Teleport Backward", "Teletransporte de reparación hacia atrás" },
            { "❄️ Auto Snow Sculpture", "❄️ Escultura de nieve automática" },
            { "Auto Click Icon", "Auto clic en icono" },
            { "Move snowballs to backpack", "Mover bolas de nieve a la mochila" },
            { "Idle", "Inactivo" },
            { "DISABLED", "DESACTIVADO" },
            { "GATHERING...", "RECOLECTANDO..." },
            { "TELEPORTING...", "TELETRANSPORTANDO..." },
            { "IDLE", "INACTIVO" },
            { "Running (step {0})", "En marcha (paso {0})" },
            { "Running (step {0}) - {1}", "En marcha (paso {0}) - {1}" },
            { "Game Speed: {0:F1}x", "Velocidad del juego: {0:F1}x" },
            { "Camera FOV: {0:F0}°", "FOV de camara: {0:F0}°" },
            { "Bag Automation", "Automatizacion de bolsa" },
            { "Repair Status: {0}", "Estado de reparacion: {0}" },
            { "Eat Status: {0}", "Estado de comida: {0}" },
            { "Current Energy: {0}", "Energia actual: {0}" },
            { "Bag automation already running", "La automatizacion de bolsa ya esta en marcha" },
            { "Auto Eat will continue until energy is full.", "La comida automatica continuara hasta llenar la energia." },
            { "Auto Eat Energy Panel", "Auto comida por panel de energia" },
            { "Auto Eat Trigger: {0}% or lower", "Activar auto comida: {0}% o menos" },
            { "Food Type", "Tipo de comida" },
            { "Auto Repair: open bag → find {0} → Use → close bag\nAuto Eat: open bag → find {1} → Use → close bag", "Auto reparacion: abrir bolsa → buscar {0} → Usar → cerrar bolsa\nAuto comida: abrir bolsa → buscar {1} → Usar → cerrar bolsa" },
            { "AUTO SNOW SCULPTURE", "ESCULTURA DE NIEVE AUTOMATICA" },
            { "Click Interval: {0:F0}ms", "Intervalo de clic: {0:F0}ms" },
            { "Interval: {0:F0}ms", "Intervalo: {0:F0}ms" },
            { "State: {0}", "Estado: {0}" },
            { "Current Ingredient: {0}", "Ingrediente actual: {0}" },
            { "Max per ingredient: {0}", "Maximo por ingrediente: {0}" },
            { "Home", "Inicio" },
            { "Animal Care", "Cuidado animal" },
            { "Daily Quests", "Misiones diarias" },
            { "Collect Daily Quest Log", "Recopilar registro de misiones diarias" },
            { "Auto Submit Daily Items", "Entregar objetos de misiones diarias" },
            { "Submit Bird Photo", "Entregar tarjetas de aves" },
            { "Skip 5 Star Items", "Omitir objetos de 5 estrellas" },
            { "NPCs", "NPC" },
            { "Locations", "Ubicaciones" },
            { "Events", "Eventos" },
            { "House", "Casa" },
            { "Custom", "Personalizado" },
            { "Keybinds", "Teclas" },
            { "UI Theme", "Tema UI" },
            // Settings / localization
            { "Localization", "Idioma" },
            { "Current Language: {0}", "Idioma actual: {0}" },
            { "Language switched to {0}", "Idioma cambiado a {0}" },
            { "SETTINGS", "AJUSTES" },
            { "KEYBIND SETTINGS", "AJUSTES DE TECLAS" },
            { "PRESS ANY KEY FOR:", "PULSA UNA TECLA O BOTÓN DEL RATÓN PARA:" },
            { "CANCEL", "CANCELAR" },
            { "RESET TO DEFAULTS", "RESTAURAR" },
            { "Defaults restored (Toggle Menu: Insert)", "Valores restaurados (Menú: Insert)" },
            { "Enable Notifications", "Activar notificaciones" },
            { "Notifications enabled", "Notificaciones activadas" },
            { "Auto Start on Lobby", "Inicio automático en lobby" },
            { "Auto Start enabled", "Inicio automático activado" },
            { "Auto Start disabled", "Inicio automático desactivado" },
            { "Auto Close Announcements", "Cerrar anuncios automáticamente" },
            { "Auto Close Announcement enabled", "Cierre automático de anuncios activado" },
            { "Auto Close Announcement disabled", "Cierre automático de anuncios desactivado" },
            { "Hide ID", "Ocultar ID" },
            { "ID display hidden", "ID oculta" },
            { "ID display shown", "ID visible" },
            { "Custom ID", "ID personalizada" },
            { "Custom ID enabled", "ID personalizada activada" },
            { "Custom ID disabled", "ID personalizada desactivada" },
            { "Value", "Valor" },
            { "Leave blank for your real ID. If filled, it replaces the visible ID.", "Déjalo vacío para tu ID real. Si lo rellenas, reemplaza la ID visible." },
            { "Custom ID cleared", "ID personalizada borrada" },
            { "Custom ID updated", "ID personalizada actualizada" },
            { "Show Status Overlay", "Mostrar panel de estado" },
            { "Status overlay enabled", "Panel de estado activado" },
            { "Status overlay disabled", "Panel de estado desactivado" },
            { "Block Input", "Bloquear entrada" },
            { "Block Input Enabled", "Bloqueo de entrada activado" },
            { "Block Input Disabled", "Bloqueo de entrada desactivado" },
            { "FPS Bypass", "Límite FPS" },
            { "FPS Bypass Enabled", "Límite FPS activado" },
            { "FPS Bypass Disabled", "Límite FPS desactivado" },
            { "Target Max FPS: {0}", "FPS máximos objetivo: {0}" },
            { "Effective cap: {0}  |  Live: {1:F0} FPS", "Límite efectivo: {0}  |  Actual: {1:F0} FPS" },
            { "Join Friend", "Unirse a amigo" },
            { "Join My Town", "Ir a mi pueblo" },
            { "Join Public", "Entrar pública" },
            // Status / overlay labels
            { "Active", "Activo" },
            { "Running", "En marcha" },
            { "Fish Farm", "Granja de pesca" },
            { "Target", "Objetivo" },
            { "Caught", "Capturados" },
            { "No active features", "No hay funciones activas" },
            { "Keybind", "Tecla" },
            { "Accent Color", "Color de acento" },
            { "Status Overlay", "Panel de estado" },
            { "Tab", "Pestaña" },
            { "Speed", "Velocidad" },
            { "Enabled", "Activado" },
            { "Disabled", "Desactivado" },
            { "Farm Status", "Estado de granja" },
            { "Fish Status", "Estado de pesca" },
            // Radar / marker labels
            { "Mushroom", "Seta" },
            { "Blueberry", "Arándano" },
            { "Raspberry", "Frambuesa" },
            { "Bubble", "Burbuja" },
            { "Bird", "Ave" },
            { "Insect", "Insecto" },
            { "Rare Tree", "Árbol raro" },
            { "Apple Tree", "Manzano" },
            { "Mandarin Tree", "Árbol mandarino" },
            { "Tree", "Árbol" },
            { "Farm Rocks", "Farmear rocas" },
            { "Farm Ores", "Farmear menas" },
            { "Farm Trees", "Farmear árboles" },
            { "Farm Rare Trees", "Farmear árboles raros" },
            { "Farm Apple Trees", "Farmear manzanos" },
            { "Farm Mandarin Trees", "Farmear árboles mandarinos" },
            { "Reset Cooldowns", "Resetear tiempos de espera" },
            { "Chop & Mine flow:", "Flujo de talar y minar:" },
            { "• Build list of available markers", "• Construir lista de marcadores disponibles" },
            { "• Shuffle and teleport to markers", "• Mezclar y teletransportarse a los marcadores" },
            { "• Simulate F key for configured duration", "• Simular la tecla F durante el tiempo configurado" },
            { "• Mark resource collected and set cooldowns", "• Marcar el recurso recolectado y aplicar tiempos de espera" },
            { "Fish Shadow", "Sombra de pez" },
            { "Meteor", "Meteoro" },
            { "Stone", "Piedra" },
            { "Ore", "Mena" },
            { "Oyster", "Seta ostra" },
            { "Button", "Champiñón" },
            { "Oyster Mushroom", "Seta ostra" },
            { "Button Mushroom", "Champiñón" },
            { "Penny Bun", "Boletales" },
            { "Shiitake", "Shiitake" },
            { "Truffle", "Trufa negra" },
            { "Fiddlehead", "Helecho águila silvestre" },
            { "Burdock", "Bardana silvestre" },
            { "Mustard Greens", "Mostaza silvestre" },
            { "Tall Mustard", "Ajo mostaza silvestre" },
            { "Blueberries", "Arándanos" },
            { "Raspberries", "Frambuesas" },
            { "Bubbles", "Burbujas" },
            { "Birds", "Aves" },
            // Food & repair options
            { "Repair Kit", "Caja de reparación" },
            { "Crafty Repair Kit", "Caja vivaz de reparación" },
            // Auto Buy ingredient matching
            { "Springday Brown Sugar", "Paquete de azúcar moreno primaveral" },
            { "Salsa Sauce", "Salsa" },
            { "Pasteurized Egg", "Huevo pasteurizado" },
            { "Meat", "Carne" },
            { "Red Bean", "Frijol rojo" },
            { "Egg", "Huevo" },
            { "Milk", "Leche" },
            { "Rice Flour", "Harina de arroz" },
            { "Tea Leaves", "Té negro" },
            { "Cooking Oil", "Aceite comestible" },
            { "Matcha Powder", "Polvo de matcha" },
            { "Cheese", "Queso" },
            { "Butter", "Mantequilla" },
            { "Coffee Beans", "Grano de café" },
            // Auto Eat food options
            { "Bad Food", "Comida extraña" },
            { "Blue Jam", "Mermelada de arándano" },
            { "Rasp Jam", "Mermelada de frambuesa" },
            { "Mix Jam", "Mermelada mixta" },
            { "Bake Mushroom", "Hongos asados" },
            { "Salad", "Ensalada del campo" },
            { "Any Food", "Cualquier comida" },
            // Interaction / automation text
            { "Tool durability depleted", "La durabilidad de las herramientas se ha agotado, usa la caja de reparación" },
            { "Scanner Durability low", "Durabilidad del escáner baja" },
            { "Use", "Usar" },
            { "Eat", "Comer" },
            { "Equip Net", "Equipar red" },
            { "Equip Bird Scanner", "Equipar escáner de aves" },
            { "DISABLE INSECT CATCHING", "DESACTIVAR CAPTURA DE INSECTOS" },
            { "ENABLE INSECT CATCHING", "ACTIVAR CAPTURA DE INSECTOS" },
            { "DISABLE BIRD CATCHING", "DESACTIVAR CAPTURA DE AVES" },
            { "ENABLE BIRD CATCHING", "ACTIVAR CAPTURA DE AVES" },
            { "Status: {0}", "Estado: {0}" },
            { "Auto Stop Timer", "Temporizador de apagado" },
            { "Timer (HH:MM:SS)", "Temporizador (HH:MM:SS)" },
            { "Set at least 1 second to enable auto-stop.", "Pon al menos 1 segundo para activar el apagado automático." },
            { "Auto-stop after: {0}", "Detener automáticamente tras: {0}" },
            { "Time remaining: {0}", "Tiempo restante: {0}" },
            { "Teleport Cooldown: {0:F1}s", "Espera de teletransporte: {0:F1}s" },
            { "Scan Timeout: {0:F1}s", "Tiempo de escaneo: {0:F1}s" },
            { "Auto-Repair (Paused TP Farm): {0:F0}s", "Autorreparación (TP pausado): {0:F0}s" },
            { "Teleport Offset: {0:F2}m", "Desplazamiento TP: {0:F2}m" },
            { "Multi-Catch Limit: {0}", "Limite de captura multiple: {0}" },
            { "Auto Insect Farm", "Granja de insectos automática" },
            { "Auto Bird Farm", "Granja de aves automática" },
            { "Caught This Session: {0}", "Capturados esta sesión: {0}" },
            { "Catch Cooldown: {0:F1}s", "Enfriamiento de captura: {0:F1}s" },
            { "Scan Range: {0:F0}m", "Rango de escaneo: {0:F0}m" },
            { "Tool: {0}", "Herramienta: {0}" },
            { "Unknown", "Desconocido" },
            { "Insect Farm Enabled", "Granja de insectos activada" },
            { "Insect Farm Disabled", "Granja de insectos desactivada" },
            { "Insect Farm auto-stop set: {0}", "Apagado automático de insectos fijado: {0}" },
            { "Insect Farm auto-stopped (timer)", "Granja de insectos detenida (temporizador)" },
            { "Bird Farm Enabled", "Granja de aves activada" },
            { "Bird Farm Disabled", "Granja de aves desactivada" },
            { "Bird Farm auto-stop set: {0}", "Apagado automático de aves fijado: {0}" },
            { "Bird Farm auto-stopped (timer)", "Granja de aves detenida (temporizador)" },
            { "Auto Repair started", "Auto reparación iniciada" },
            { "Auto Repair already running", "La auto reparación ya está en marcha" },
            { "Auto Eat started ({0})", "Auto comida iniciada ({0})" },
            { "Auto Eat already running", "La auto comida ya está en marcha" },
            { "Auto Repair triggered by durability notification - pausing farm for {0:F0}s", "Auto reparación activada por notificación de durabilidad; pausando la granja durante {0:F0}s" },
            { "Auto Eat triggered by energy panel ({0})", "Auto comida activada por panel de energia ({0})" },
            { "Noclip", "Noclip" },
            { "Noclip Speed: {0:F1}", "Velocidad de noclip: {0:F1}" },
            { "Noclip Boost: {0:F1}x", "Impulso de noclip: {0:F1}x" },
            { "Anti AFK (Auto Click)", "Anti AFK (clic automático)" },
            { "AFK Click Interval: {0:F0}s", "Intervalo de clic AFK: {0:F0}s" },
            { "Noclip: WASD + Space/Ctrl\nShift = Speed Boost", "Noclip: WASD + Espacio/Ctrl\nShift = impulso de velocidad" },
            { "Building - Bypass Overlap", "Construcción - omitir superposición" },
            { "Bypass Overlap", "Omitir superposición" },
            { "Free Placement", "Colocación libre" },
            { "Angle", "Ángulo" },
            { "Grid", "Cuadrícula" },
            { "No surface limit", "Sin límite de superficie" },
            { "No range/height limit", "Sin límite de rango/altura" },
            { "no object", "sin objeto" },
            { "Height", "Altura" },
            { "Plane", "Plano" },
            { "Credits: • evermoreee12 for Bypass Overlap", "Créditos: • evermoreee12 por Omitir superposición" },
            { "Keybinds saved", "Teclas guardadas" },
            { "Keybinds loaded", "Teclas cargadas" },
            { "Failed to save keybinds", "No se pudieron guardar las teclas" },
            { "Failed to load keybinds", "No se pudieron cargar las teclas" },
            { "UI theme saved", "Tema UI guardado" },
            { "UI theme loaded", "Tema UI cargado" },
            { "Failed to save UI theme", "No se pudo guardar el tema UI" },
            { "Failed to load UI theme", "No se pudo cargar el tema UI" },
            // Custom Food feature
            { "Custom Food", "Comida Personalizada" },
            { "Custom Food: Click any food item in your bag to select it", "Comida Personalizada: Haz clic en cualquier alimento de tu bolsa para seleccionarlo" },
            { "Detected food click: {0}", "Alimento detectado: {0}" },
            { "Opening bag to scan for food items...", "Abriendo bolsa para escanear alimentos..." },
            { "No food items found in bag.", "No se encontraron alimentos en la bolsa." },
            { "Found {0} food item(s) in bag.", "Se encontró {0} alimento(s) en la bolsa." },
            { "Click 'Scan Bag' to find food items.", "Haz clic en 'Escanear Bolsa' para buscar alimentos." },
            { "Open your bag and click 'Scan Bag'.", "Abre tu bolsa y haz clic en 'Escanear Bolsa'." },
            { "Custom food set to: {0}", "Comida personalizada establecida: {0}" },
            // Homeland Farm
            { "pictures.title", "Fotos" },
            { "pictures.decrypt_all", "Descifrar todo" },
            { "pictures.busy", "Descifrado en curso" },
            { "pictures.decrypting", "Descifrando archivos de ScreenCapture..." },
            { "pictures.source_missing", "No se encontró la carpeta ScreenCapture" },
            { "pictures.paths", "Desde:\n{0}\n\nHacia:\n{1}" },
            { "pictures.progress", "Procesando {0}/{1} — descifrados {2}, planos {3}, fallidos {4}, omitidos {5}" },
            { "pictures.done", "Listo: {0} nuevos ({1} descifrados, {2} planos, {3} fallidos, {4} omitidos)\n{5}" },
            { "pictures.done_short", "Exportados {0} nuevos, omitidos {1} — {2}" },
            { "pictures.encrypt_changed", "Cifrar cambios" },
            { "pictures.scan_changed", "Buscar cambios" },
            { "pictures.encrypting", "Cifrando archivos modificados..." },
            { "pictures.manifest_missing", "Falta el manifiesto — ejecuta Descifrar todo primero" },
            { "pictures.changed_count", "Archivos modificados: {0}" },
            { "pictures.no_changes", "Sin cambios (los hashes coinciden)" },
            { "pictures.encrypt_progress", "Cifrado {0}/{1} — importados {2}, fallidos {3}" },
            { "pictures.encrypt_done", "Reimportados {0} archivo(s), {1} fallidos\n{2}" },
            { "pictures.encrypt_done_short", "Reimportados {0} archivo(s)" },
            { "pictures.draw_hint", "Draw: abre tu dibujo en el editor del juego, pulsa Extract para guardar drawing.png, edítalo y luego Upload para volver a importarlo." },
            { "pictures.done_draw", "Listo: {0} nuevos ({1} descifrados, {2} planos, {3} fallidos, {4} omitidos, {5} vistas Draw)\n{6}" },
            { "ice_skating.send_score_title", "Enviar puntuación de patinaje" },
            { "ice_skating.challenge_button", "Desafío (5 perfectos)" },
            { "ice_skating.challenge_score", "Puntos" },
            { "ice_skating.runs", "Vueltas" },
            { "ice_skating.perfect_drill", "Práctica perfecta" },
            { "homeland_farm.title", "Granja del hogar" },
            { "extra.title", "Extra" },
            { "craft.open", "Abrir crafteo" },
            { "craft.open_ok", "Panel de crafteo abierto." },
            { "craft.aura_unavailable", "Crafteo no disponible (AuraMono no listo)." },
            { "craft.player_unavailable", "Jugador local no disponible." },
            { "craft.level_object_unavailable", "Objeto de mesa de crafteo no disponible." },
            { "craft.open_interact_unavailable", "OpenInteractPanel no disponible." },
            { "craft.open_interact_threw", "No se pudo abrir el panel de crafteo." },
            { "craft.level_manager_unavailable", "Administrador de objetos de nivel no disponible." },
            { "craft.dictionary_unavailable", "Lista de objetos de nivel no disponible." },
            { "craft.no_level_objects", "No se encontraron objetos de nivel. Entra primero a tu hogar." },
            { "craft.all_tables_stored", "Todas las mesas de crafteo están guardadas. Coloca una en tu hogar." },
            { "craft.no_table", "No se encontró mesa de crafteo. Acércate a una en tu hogar." },
            { "homeland_farm.water_section", "REGAR" },
            { "homeland_farm.water_radius", "Radio: {0} m" },
            { "homeland_farm.water_in_radius", "Regar en radio" },
            { "homeland_farm.water_own", "Regar propios" },
            { "homeland_farm.water_friends", "Regar de amigos" },
            { "homeland_farm.water_unwatered", "Regar sin regar" },
            { "homeland_farm.harvest_section", "COSECHAR" },
            { "homeland_farm.harvest_crops_all", "Cosechar cultivos propios" },
            { "homeland_farm.plant_seeds_section", "SEMILLAS DE PLANTA" },
            { "homeland_farm.collect_plant_seeds_all", "Recoger semillas propias" },
            { "homeland_farm.collect_dormant", "Recoger dormidas" },
            { "homeland_farm.collect_flowers", "Recoger flores" },
            { "homeland_farm.weeds_section", "MALAS HIERBAS" },
            { "homeland_farm.weed_all", "Desmalezar propios" },
            { "homeland_farm.sow_section", "SEMBRAR CULTIVOS" },
            { "homeland_farm.sow", "Sembrar" },
            { "homeland_farm.sow_all", "Sembrar" },
            { "homeland_farm.sow_in_radius", "Sembrar" },
            { "homeland_farm.radius_section", "RADIO DE GRANJA" },
            { "homeland_farm.auto_section", "GRANJA AUTOMÁTICA" },
            { "homeland_farm.auto_capture", "Capturar maceteros" },
            { "homeland_farm.auto_captured", "Capturados {0} macetero(s)" },
            { "homeland_farm.auto_not_captured", "Sin capturar — pulsa Capturar maceteros" },
            { "homeland_farm.auto_hint", "Siembra la semilla elegida, desmaleza y cosecha hasta agotar semillas." },
            { "homeland_farm.auto_start", "Iniciar granja automática" },
            { "homeland_farm.auto_stop", "Detener granja automática" },
            { "homeland_farm.auto_fertilize", "Fertilizar automáticamente tras sembrar" },
            { "homeland_farm.fertilize_section", "FERTILIZAR CULTIVOS" },
            { "homeland_farm.fertilize", "Fertilizar" },
            { "homeland_farm.fertilize_all", "Fertilizar" },
            { "homeland_farm.fertilize_in_radius", "Fertilizar" },
            { "homeland_farm.stop", "Detener" },
            { "homeland_farm.status_idle", "Inactivo." },
            { "homeland_farm.status_stopped", "Detenido." },
            { "homeland_farm.need_homeland", "Entra primero al hogar." },
            { "homeland_farm.seed_storage", "Origen de semillas" },
            { "homeland_farm.fert_storage", "Origen de fertilizante" },
            { "homeland_farm.storage_backpack", "Mochila" },
            { "homeland_farm.storage_warehouse", "Almacén" },
            { "homeland_farm.storage_both", "Ambos" },
            { "homeland_farm.refresh", "Actualizar" },
            { "homeland_farm.refresh_seeds", "Actualizar semillas" },
            { "homeland_farm.refresh_fertilizers", "Actualizar fertilizantes" },
            { "homeland_farm.cached_seeds", "En caché {0} semilla(s)" },
            { "homeland_farm.press_refresh_seeds", "Pulsa Actualizar semillas" },
            { "homeland_farm.cached_fertilizers", "En caché {0} fertilizante(s)" },
            { "homeland_farm.press_refresh_fertilizers", "Pulsa Actualizar fertilizantes" },
            { "homeland_farm.prev", "<" },
            { "homeland_farm.next", ">" },
            { "homeland_farm.no_seeds", "No se encontraron semillas de cultivo." },
            { "homeland_farm.no_fertilizers", "No se encontraron fertilizantes de cultivo." },
            { "homeland_farm.operations_section", "OPERACIONES" },
            { "homeland_farm.radius_slider_label", "Radio" },
            { "homeland_farm.log_water_radius", "Registrar diagnóstico de riego" },
            { "homeland_farm.log_water_failed", "Registro de riego fallido." },
            { "homeland_farm.log_water_done", "Registro de riego completado." },
            { "homeland_farm.status_warming", "Iniciando..." },
        };

        // Built-in Simplified Chinese fallback strings.
        private static readonly Dictionary<string, string> ChineseSimplifiedDefaults = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "ENABLE AUTO FORAGING", "启用自动采集" },
            { "DISABLE AUTO FORAGING", "关闭自动采集" },
            { "Aura Farm", "光环农场" },
            { "Status:", "状态：" },
            { "✗ No radar toggles selected", "未选择任何雷达选项" },
            { "✗ No radar toggles selected\n   Auto Farm disabled...", "未选择任何雷达选项\n   自动采集已关闭..." },
            { "✗ Radar is OFF!\n   Enable Radar first.", "雷达已关闭！\n   请先开启雷达。" },
            { "✓ Ready", "已就绪" },
            { "Area Load Delay: {0}s", "区域加载延迟：{0}s" },
            { "Resolver: STANDBY", "解析器：待命" },
            { "Resolver: READY", "解析器：就绪" },
            { "Resolver: RESOLVING / NOT READY", "解析器：正在解析 / 未就绪" },
            { "LOOT PRIORITIES", "拾取优先级" },
            { "Mushrooms:", "蘑菇：" },
            { "Events:", "事件：" },
            { "Other:", "其他：" },
            { "Priority Location: None", "优先位置：无" },
            { "Available: {0}", "可用：{0}" },
            { "Markers: {0}", "标记：{0}" },
            { "RADAR SETTINGS", "雷达设置" },
            { "Marker Style:", "标记样式：" },
            { "Default", "默认" },
            { "Default ✓", "默认 ✓" },
            { "Simple Text", "简单文本" },
            { "Simple Text ✓", "简单文本 ✓" },
            { "Radar markers: Default", "雷达标记：默认" },
            { "Radar markers: Simple Text", "雷达标记：简单文本" },
            { "Failed to save radar settings", "保存雷达设置失败" },
            { "Radar Max Distance: {0}m", "雷达最大距离：{0}m" },
            { "ENABLE RADAR", "启用雷达" },
            { "DISABLE RADAR", "关闭雷达" },
            { "Select All Loots", "全选掉落" },
            { "Clear All Loots", "清除全部" },
            { "Force Refresh Scan", "强制刷新扫描" },
            { "None", "无" },
            { "Mushrooms", "蘑菇" },
            { "Berries", "浆果" },
            { "Resources", "资源" },
            { "Trees", "树木" },
            { "Misc", "杂项" },
            { "All Mushrooms", "全部蘑菇" },
            { "Black Truffle", "黑松露" },
            { "Stones", "石头" },
            { "Ores", "矿石" },
            { "Rare Trees", "稀有树木" },
            { "Apple Trees", "苹果树" },
            { "Mandarin Trees", "柑橘树" },
            { "Fish Shadows", "鱼影" },
            { "Meteors", "陨石" },
            { "Click Duration: {0:F1}s", "点击持续时间：{0:F1}s" },
            { "Auto-Repair Tool (Paused TP FARM): {0:F0}s", "自动修理工具（传送农场暂停）：{0:F0}s" },
            { "Fish Detect Range", "鱼类检测范围" },
            { "Max Reel", "最大收线时间" },
            { "Hold Time", "按住时间" },
            { "Pause", "暂停" },
            { "Equip Rod", "装备鱼竿" },
            { "Teleport Fishing", "传送钓鱼" },
            { "Enable Auto Fishing", "启用自动钓鱼" },
            { "Disable Auto Fishing", "关闭自动钓鱼" },
            { "Equip Axe", "装备斧头" },
            { "ENABLE CHOP & MINE", "启用伐木与采矿" },
            { "DISABLE CHOP & MINE", "关闭伐木与采矿" },
            { "Auto Collect", "自动收集" },
            { "Collect Types:", "收集类型：" },
            { "  Mushrooms", "  蘑菇" },
            { "  Berries / Bushes / Plants", "  浆果 / 灌木 / 植物" },
            { "  Event Resources", "  活动资源" },
            { "Hide UI + Player (Client Side)", "隐藏界面 + 玩家（客户端）" },
            { "Hide Jump Button (Space still works)", "隐藏跳跃按钮（空格仍可用）" },
            { "Bunny Hop (hold Space)", "连跳（按住空格）" },
            { "Bird Vacuum (Client Side)", "鸟类吸附（客户端）" },
            { "Custom Camera FOV", "自定义相机视野" },
            { "DISABLE ALL", "全部关闭" },
            { "REFRESH & SCAN", "刷新并扫描" },
            { "Auto Repair", "自动修理" },
            { "Eat Selected Food", "食用所选食物" },
            { "Repair Teleport Backward", "修理后传送返回" },
            { "❄️ Auto Snow Sculpture", "❄️ 自动雪雕" },
            { "Auto Click Icon", "自动点击图标" },
            { "Move snowballs to backpack", "将雪球移至背包" },
            { "Idle", "空闲" },
            { "DISABLED", "已禁用" },
            { "GATHERING...", "采集中..." },
            { "TELEPORTING...", "传送中..." },
            { "IDLE", "空闲" },
            { "Running (step {0})", "运行中（步骤 {0}）" },
            { "Running (step {0}) - {1}", "运行中（步骤 {0}） - {1}" },
            { "Farm Rocks", "农场石头" },
            { "Farm Ores", "农场矿石" },
            { "Farm Trees", "农场树木" },
            { "Farm Rare Trees", "农场稀有树木" },
            { "Farm Apple Trees", "农场苹果树" },
            { "Farm Mandarin Trees", "农场柑橘树" },
            { "Reset Cooldowns", "重置冷却" },
            { "Chop & Mine flow:", "伐木与采矿流程：" },
            { "• Build list of available markers", "• 构建可用标记列表" },
            { "• Shuffle and teleport to markers", "• 随机排序并传送到标记点" },
            { "• Simulate F key for configured duration", "• 在设定时长内模拟按下 F 键" },
            { "• Mark resource collected and set cooldowns", "• 标记资源已采集并设置冷却" },
            { "Oyster Mushroom", "平菇" },
            { "Button Mushroom", "口蘑" },
            { "Blueberries", "蓝莓" },
            { "Raspberries", "覆盆子" },
            { "Bubbles", "气泡" },
            { "Noclip", "穿墙" },
            { "Noclip Speed: {0:F1}", "穿墙速度：{0:F1}" },
            { "Noclip Boost: {0:F1}x", "穿墙加速：{0:F1}x" },
            { "Anti AFK (Auto Click)", "防挂机（自动点击）" },
            { "AFK Click Interval: {0:F0}s", "挂机点击间隔：{0:F0}s" },
            { "Noclip: WASD + Space/Ctrl\nShift = Speed Boost", "穿墙：WASD + 空格/Ctrl\nShift = 加速" },
            { "Building - Bypass Overlap", "建造 - 绕过重叠" },
            { "Bypass Overlap", "绕过重叠" },
            { "Free Placement", "自由放置" },
            { "Angle", "角度" },
            { "Grid", "网格" },
            { "No surface limit", "无表面限制" },
            { "No range/height limit", "无范围/高度限制" },
            { "no object", "无对象" },
            { "Height", "高度" },
            { "Plane", "平面" },
            { "Credits: • evermoreee12 for Bypass Overlap", "鸣谢：• evermoreee12 提供绕过重叠" },
            // Main navigation / tabs
            { "Self", "个人" },
            { "Resource Gathering", "资源采集" },
            { "Features", "功能" },
            { "New Features", "新功能" },
            { "Radar", "雷达" },
            { "Teleport", "传送" },
            { "Bag / Warehouse", "背包 / 仓库" },
            { "Settings", "设置" },
            { "Main", "主页" },
            { "Building", "建造" },
            { "Foraging", "采集" },
            { "Chop & Mine", "伐木与采矿" },
            { "Fishing", "钓鱼" },
            { "Insects", "昆虫" },
            { "Food & Repair", "食物与修理" },
            { "Snow Sculpting", "雪雕" },
            { "Auto Buy", "自动购买" },
            { "AUTO BUY (Cooking Store)", "自动购买（烹饪商店）" },
            { "BUY ALL (COIN)", "全部购买（金币）" },
            { "Shop buy-all already running", "商店批量购买已在运行" },
            { "Auto Buy: Teleport -> Buy -> Return", "自动购买：传送 -> 购买 -> 返回" },
            { "State: {0}", "状态：{0}" },
            { "Current Ingredient: {0}", "当前食材：{0}" },
            { "Max per ingredient: {0}", "每种食材最大数量：{0}" },
            { "Home", "主页" },
            { "Animal Care", "动物照料" },
            { "Daily Quests", "每日任务" },
            { "Collect Daily Quest Log", "收集每日任务日志" },
            { "Auto Submit Daily Items", "自动提交每日物品任务" },
            { "Submit Bird Photo", "提交观鸟信息卡" },
            { "Skip 5 Star Items", "跳过5星物品" },
            { "NPCs", "NPC" },
            { "Locations", "地点" },
            { "Events", "活动" },
            { "House", "房屋" },
            { "Custom", "自定义" },
            { "Keybinds", "按键" },
            { "UI Theme", "UI 主题" },
            // Settings / localization
            { "Localization", "语言" },
            { "Current Language: {0}", "当前语言: {0}" },
            { "Language switched to {0}", "语言已切换为 {0}" },
            { "SETTINGS", "设置" },
            { "KEYBIND SETTINGS", "按键设置" },
            { "PRESS ANY KEY FOR:", "请按下任意按键或鼠标按钮：" },
            { "CANCEL", "取消" },
            { "RESET TO DEFAULTS", "恢复默认" },
            { "Defaults restored (Toggle Menu: Insert)", "已恢复默认值（菜单切换键：Insert）" },
            { "Enable Notifications", "启用通知" },
            { "Notifications enabled", "通知已启用" },
            { "Auto Start on Lobby", "大厅自动启动" },
            { "Auto Start enabled", "自动启动已启用" },
            { "Auto Start disabled", "自动启动已关闭" },
            { "Auto Close Announcements", "自动关闭公告" },
            { "Auto Close Announcement enabled", "自动关闭公告已启用" },
            { "Auto Close Announcement disabled", "自动关闭公告已关闭" },
            { "Hide ID", "隐藏 ID" },
            { "ID display hidden", "ID 已隐藏" },
            { "ID display shown", "ID 已显示" },
            { "Custom ID", "自定义 ID" },
            { "Custom ID enabled", "自定义 ID 已启用" },
            { "Custom ID disabled", "自定义 ID 已关闭" },
            { "Value", "值" },
            { "Leave blank for your real ID. If filled, it replaces the visible ID.", "留空则使用你的真实 ID。如果填写，将替换界面上显示的 ID。" },
            { "Custom ID cleared", "自定义 ID 已清除" },
            { "Custom ID updated", "自定义 ID 已更新" },
            { "Show Status Overlay", "显示状态覆盖层" },
            { "Status overlay enabled", "状态覆盖层已启用" },
            { "Status overlay disabled", "状态覆盖层已关闭" },
            { "Block Input", "阻止输入" },
            { "Block Input Enabled", "阻止输入已启用" },
            { "Block Input Disabled", "阻止输入已关闭" },
            { "FPS Bypass", "FPS 限制" },
            { "FPS Bypass Enabled", "FPS 限制已启用" },
            { "FPS Bypass Disabled", "FPS 限制已关闭" },
            { "Target Max FPS: {0}", "目标最大 FPS：{0}" },
            { "Effective cap: {0}  |  Live: {1:F0} FPS", "生效上限：{0}  |  当前：{1:F0} FPS" },
            { "Join Friend", "加入好友" },
            { "Join My Town", "加入我的小镇" },
            { "Join Public", "加入公共世界" },
            // Status / overlay labels
            { "Active", "已启用" },
            { "Running", "运行中" },
            { "Fish Farm", "钓鱼农场" },
            { "Target", "目标" },
            { "Caught", "已捕获" },
            { "No active features", "当前没有启用的功能" },
            { "Keybind", "按键" },
            { "Accent Color", "主题颜色" },
            { "Status Overlay", "状态覆盖层" },
            { "Tab", "选项卡" },
            { "Speed", "速度" },
            { "Enabled", "开" },
            { "Disabled", "关" },
            { "Farm Status", "农场状态" },
            { "Fish Status", "钓鱼状态" },
            // Radar / marker labels
            { "Mushroom", "蘑菇" },
            { "Blueberry", "蓝莓" },
            { "Raspberry", "覆盆子" },
            { "Bubble", "气泡" },
            { "Bird", "鸟" },
            { "Insect", "昆虫" },
            { "Rare Tree", "稀有树" },
            { "Apple Tree", "苹果树" },
            { "Mandarin Tree", "柑橘树" },
            { "Tree", "树" },
            { "Fish Shadow", "鱼影" },
            { "Meteor", "流星" },
            { "Stone", "石头" },
            { "Ore", "矿石" },
            { "Oyster", "平菇" },
            { "Button", "口蘑" },
            { "Penny Bun", "牛肝菌" },
            { "Shiitake", "香菇" },
            { "Truffle", "松露" },
            { "Fiddlehead", "蕨菜" },
            { "Burdock", "牛蒡" },
            { "Mustard Greens", "芋菜" },
            { "Tall Mustard", "高芝芥" },
            { "Birds", "鸟类" },
            // Food & repair options
            { "Repair Kit", "修理包" },
            { "Crafty Repair Kit", "工匠修理包" },
            // Auto Buy ingredient matching
            { "Springday Brown Sugar", "春日红糖" },
            { "Salsa Sauce", "莎莎酱" },
            { "Pasteurized Egg", "巴氏杀菌蛋" },
            { "Meat", "肉" },
            { "Red Bean", "红豆" },
            { "Egg", "鸡蛋" },
            { "Milk", "牛奶" },
            { "Rice Flour", "米粉" },
            { "Tea Leaves", "茶叶" },
            { "Cooking Oil", "食用油" },
            { "Matcha Powder", "抹茶粉" },
            { "Cheese", "奶酪" },
            { "Butter", "黄油" },
            { "Coffee Beans", "咖啡豆" },
            // Auto Eat food options
            { "Bad Food", "劣质食物" },
            { "Blue Jam", "蓝莓果酱" },
            { "Rasp Jam", "覆盆子果酱" },
            { "Mix Jam", "混合果酱" },
            { "Bake Mushroom", "烤蘑菇" },
            { "Salad", "沙拉" },
            { "Any Food", "任意食物" },
            // Interaction / automation text
            { "Tool durability depleted", "工具耐久耗尽，请使用维修盒" },
            { "Scanner Durability low", "扫描器耐久度过低" },
            { "Use", "使用" },
            { "Eat", "食用" },
            { "Equip Net", "装备捕虫网" },
            { "Equip Bird Scanner", "装备鸟类扫描器" },
            { "DISABLE INSECT CATCHING", "关闭捕虫" },
            { "ENABLE INSECT CATCHING", "启用捕虫" },
            { "DISABLE BIRD CATCHING", "关闭抓鸟" },
            { "ENABLE BIRD CATCHING", "启用抓鸟" },
            { "Status: {0}", "状态：{0}" },
            { "Auto Stop Timer", "自动停止计时器" },
            { "Timer (HH:MM:SS)", "计时器（HH:MM:SS）" },
            { "Set at least 1 second to enable auto-stop.", "至少设置 1 秒才能启用自动停止。" },
            { "Auto-stop after: {0}", "自动停止时间：{0}" },
            { "Time remaining: {0}", "剩余时间：{0}" },
            { "Teleport Cooldown: {0:F1}s", "传送冷却：{0:F1}s" },
            { "Scan Timeout: {0:F1}s", "扫描超时：{0:F1}s" },
            { "Auto-Repair (Paused TP Farm): {0:F0}s", "自动修理（传送农场暂停）：{0:F0}s" },
            { "Teleport Offset: {0:F2}m", "传送偏移：{0:F2}m" },
            { "Multi-Catch Limit: {0}", "多重捕捉上限：{0}" },
            { "Auto Insect Farm", "自动捕虫农场" },
            { "Auto Bird Farm", "自动抓鸟农场" },
            { "Caught This Session: {0}", "本次捕获：{0}" },
            { "Catch Cooldown: {0:F1}s", "捕获冷却：{0:F1}s" },
            { "Scan Range: {0:F0}m", "扫描范围：{0:F0}m" },
            { "Tool: {0}", "工具：{0}" },
            { "Unknown", "未知" },
            { "Insect Farm Enabled", "捕虫农场已启用" },
            { "Insect Farm Disabled", "捕虫农场已关闭" },
            { "Insect Farm auto-stop set: {0}", "捕虫农场自动停止已设置：{0}" },
            { "Insect Farm auto-stopped (timer)", "捕虫农场已自动停止（计时器）" },
            { "Bird Farm Enabled", "抓鸟农场已启用" },
            { "Bird Farm Disabled", "抓鸟农场已关闭" },
            { "Bird Farm auto-stop set: {0}", "抓鸟农场自动停止已设置：{0}" },
            { "Bird Farm auto-stopped (timer)", "抓鸟农场已自动停止（计时器）" },
            { "Auto Repair started", "自动修理已启动" },
            { "Auto Repair already running", "自动修理已在运行中" },
            { "Auto Eat started ({0})", "自动进食已启动（{0}）" },
            { "Auto Eat already running", "自动进食已在运行中" },
            { "Auto Repair triggered by durability notification - pausing farm for {0:F0}s", "检测到耐久提示，已触发自动修理，并暂停农场 {0:F0} 秒" },
            { "Auto Eat triggered by energy panel ({0})", "检测到能量面板阈值，已触发自动进食（{0}）" },
            { "Keybinds saved", "按键已保存" },
            { "Keybinds loaded", "按键已加载" },
            { "Failed to save keybinds", "保存按键失败" },
            { "Failed to load keybinds", "加载按键失败" },
            { "UI theme saved", "UI 主题已保存" },
            { "UI theme loaded", "UI 主题已加载" },
            { "Failed to save UI theme", "保存 UI 主题失败" },
            { "Failed to load UI theme", "加载 UI 主题失败" },
            { "Game Speed: {0:F1}x", "游戏速度：{0:F1}x" },
            { "Camera FOV: {0:F0}°", "相机视野：{0:F0}°" },
            { "Bag Automation", "背包自动化" },
            { "Repair Status: {0}", "修理状态：{0}" },
            { "Eat Status: {0}", "进食状态：{0}" },
            { "Current Energy: {0}", "当前体力：{0}" },
            { "Auto Eat will continue until energy is full.", "自动进食会持续到体力回满。" },
            { "Auto Eat Energy Panel", "能量面板自动进食" },
            { "Auto Eat Trigger: {0}% or lower", "自动进食触发：{0}% 或更低" },
            { "Food Type", "食物类型" },
            { "AUTO SNOW SCULPTURE", "自动雪雕" },
            { "Click Interval: {0:F0}ms", "点击间隔：{0:F0}ms" },
            { "Interval: {0:F0}ms", "间隔：{0:F0}ms" },
            // Custom Food feature
            { "Custom Food", "自定义食物" },
            { "Custom Food: Click any food item in your bag to select it", "自定义食物：点击背包中的任意食物来选择" },
            { "Detected food click: {0}", "检测到食物点击：{0}" },
            { "Opening bag to scan for food items...", "正在打开背包扫描食物..." },
            { "No food items found in bag.", "背包中未找到食物。" },
            { "Found {0} food item(s) in bag.", "在背包中找到 {0} 个食物。" },
            { "Click 'Scan Bag' to find food items.", "点击 '扫描背包' 查找食物。" },
            { "Open your bag and click 'Scan Bag'.", "打开背包并点击 '扫描背包'。" },
            { "Custom food set to: {0}", "自定义食物已设置为：{0}" },
            // Homeland Farm
            { "pictures.title", "图片" },
            { "pictures.decrypt_all", "全部解密" },
            { "pictures.busy", "正在解密" },
            { "pictures.decrypting", "正在解密 ScreenCapture 文件..." },
            { "pictures.source_missing", "未找到 ScreenCapture 文件夹" },
            { "pictures.paths", "来源:\n{0}\n\n目标:\n{1}" },
            { "pictures.progress", "处理 {0}/{1} — 已解密 {2}，明文 {3}，失败 {4}，跳过 {5}" },
            { "pictures.done", "完成: {0} 个新文件（解密 {1}，明文 {2}，失败 {3}，跳过 {4}）\n{5}" },
            { "pictures.done_short", "新导出 {0} 个，跳过 {1} — {2}" },
            { "pictures.encrypt_changed", "加密已修改" },
            { "pictures.scan_changed", "扫描修改" },
            { "pictures.encrypting", "正在加密已修改文件..." },
            { "pictures.manifest_missing", "缺少清单 — 请先全部解密" },
            { "pictures.changed_count", "已修改文件: {0}" },
            { "pictures.no_changes", "无修改（哈希与清单一致）" },
            { "pictures.encrypt_progress", "加密 {0}/{1} — 已导入 {2}，失败 {3}" },
            { "pictures.encrypt_done", "已重新导入 {0} 个文件，失败 {1}\n{2}" },
            { "pictures.encrypt_done_short", "已重新导入 {0} 个已修改文件" },
            { "pictures.draw_hint", "Draw：在游戏编辑器中打开画作，点 Extract 导出 drawing.png，编辑该 PNG 后点 Upload 重新导入游戏。" },
            { "pictures.done_draw", "完成: {0} 个新文件（解密 {1}，明文 {2}，失败 {3}，跳过 {4}，Draw 预览 {5}）\n{6}" },
            { "ice_skating.send_score_title", "发送滑冰分数" },
            { "ice_skating.challenge_button", "挑战（5 次完美）" },
            { "ice_skating.challenge_score", "分数" },
            { "ice_skating.runs", "次数" },
            { "ice_skating.perfect_drill", "完美练习" },
            { "homeland_farm.title", "家园农场" },
            { "extra.title", "额外" },
            { "craft.open", "打开制作" },
            { "craft.open_ok", "制作面板已打开。" },
            { "craft.aura_unavailable", "制作不可用（AuraMono 未就绪）。" },
            { "craft.player_unavailable", "本地玩家不可用。" },
            { "craft.level_object_unavailable", "制作台对象不可用。" },
            { "craft.open_interact_unavailable", "OpenInteractPanel 不可用。" },
            { "craft.open_interact_threw", "无法打开制作面板。" },
            { "craft.level_manager_unavailable", "关卡对象管理器不可用。" },
            { "craft.dictionary_unavailable", "关卡对象列表不可用。" },
            { "craft.no_level_objects", "未找到关卡对象。请先进入家园。" },
            { "craft.all_tables_stored", "所有制作台已收起。请在家园放置工作台。" },
            { "craft.no_table", "未找到制作台。请在家园工作台附近操作。" },
            { "homeland_farm.water_section", "浇水" },
            { "homeland_farm.water_radius", "半径：{0}米" },
            { "homeland_farm.water_in_radius", "范围内浇水" },
            { "homeland_farm.water_own", "浇灌自己的" },
            { "homeland_farm.water_friends", "浇灌好友的" },
            { "homeland_farm.water_unwatered", "浇灌未浇水的" },
            { "homeland_farm.harvest_section", "收获" },
            { "homeland_farm.harvest_crops_all", "收获所有自己的作物" },
            { "homeland_farm.plant_seeds_section", "植物种子" },
            { "homeland_farm.collect_plant_seeds_all", "收集所有自己的植物种子" },
            { "homeland_farm.collect_dormant", "收集休眠" },
            { "homeland_farm.collect_flowers", "收集花卉" },
            { "homeland_farm.weeds_section", "除草" },
            { "homeland_farm.weed_all", "除尽自己的杂草" },
            { "homeland_farm.sow_section", "播种作物" },
            { "homeland_farm.sow", "播种" },
            { "homeland_farm.sow_all", "播种" },
            { "homeland_farm.sow_in_radius", "播种" },
            { "homeland_farm.radius_section", "农场范围" },
            { "homeland_farm.auto_section", "自动农场" },
            { "homeland_farm.auto_capture", "捕获种植箱" },
            { "homeland_farm.auto_captured", "已捕获 {0} 个种植箱" },
            { "homeland_farm.auto_not_captured", "未捕获 — 请按捕获种植箱" },
            { "homeland_farm.auto_hint", "播种所选种子，除草并收获，直到种子用完。" },
            { "homeland_farm.auto_start", "开始自动农场" },
            { "homeland_farm.auto_stop", "停止自动农场" },
            { "homeland_farm.auto_fertilize", "播种后自动施肥" },
            { "homeland_farm.fertilize_section", "施肥作物" },
            { "homeland_farm.fertilize", "施肥" },
            { "homeland_farm.fertilize_all", "施肥" },
            { "homeland_farm.fertilize_in_radius", "施肥" },
            { "homeland_farm.stop", "停止" },
            { "homeland_farm.status_idle", "空闲。" },
            { "homeland_farm.status_stopped", "已停止。" },
            { "homeland_farm.need_homeland", "请先进入家园。" },
            { "homeland_farm.seed_storage", "种子来源" },
            { "homeland_farm.fert_storage", "肥料来源" },
            { "homeland_farm.storage_backpack", "背包" },
            { "homeland_farm.storage_warehouse", "仓库" },
            { "homeland_farm.storage_both", "两者" },
            { "homeland_farm.refresh", "刷新" },
            { "homeland_farm.refresh_seeds", "刷新种子" },
            { "homeland_farm.refresh_fertilizers", "刷新肥料" },
            { "homeland_farm.cached_seeds", "已缓存 {0} 个种子" },
            { "homeland_farm.press_refresh_seeds", "点击刷新种子" },
            { "homeland_farm.cached_fertilizers", "已缓存 {0} 个肥料" },
            { "homeland_farm.press_refresh_fertilizers", "点击刷新肥料" },
            { "homeland_farm.prev", "<" },
            { "homeland_farm.next", ">" },
            { "homeland_farm.no_seeds", "未找到作物种子。" },
            { "homeland_farm.no_fertilizers", "未找到作物肥料。" },
            { "homeland_farm.operations_section", "操作" },
            { "homeland_farm.radius_slider_label", "范围" },
            { "homeland_farm.log_water_radius", "浇水诊断日志" },
            { "homeland_farm.log_water_failed", "浇水日志失败。" },
            { "homeland_farm.log_water_done", "浇水日志完成。" },
            { "homeland_farm.status_warming", "正在预热..." },
        };
        private static readonly Dictionary<string, string> PortugueseDefaults = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "Self", "Eu" },
            { "Resource Gathering", "Coleta de Recursos" },
            { "Features", "Recursos" },
            { "New Features", "Novos recursos" },
            { "Radar", "Radar" },
            { "Teleport", "Teleporte" },
            { "Bag / Warehouse", "Bolsa / Armazém" },
            { "Settings", "Configurações" },
            { "Main", "Principal" },
            { "Building", "Construção" },
            { "Foraging", "Coleta" },
            { "Chop & Mine", "Cortar e Minerar" },
            { "Fishing", "Pesca" },
            { "Insects", "Insetos" },
            { "Food & Repair", "Comida e Reparo" },
            { "Snow Sculpting", "Escultura de Neve" },
            { "Auto Buy", "Compra Automática" },
            { "AUTO BUY (Cooking Store)", "COMPRA AUTOMÁTICA (Loja de Culinária)" },
            { "BUY ALL (COIN)", "COMPRAR TUDO (MOEDAS)" },
            { "Shop buy-all already running", "Compra total já em execução" },
            { "Auto Buy: Teleport -> Buy -> Return", "Compra automática: Teleporte -> Comprar -> Voltar" },
            { "ENABLE AUTO FORAGING", "ATIVAR COLETA AUTOMÁTICA" },
            { "DISABLE AUTO FORAGING", "DESATIVAR COLETA AUTOMÁTICA" },
            { "Aura Farm", "Aura Farm" },
            { "Status:", "Status:" },
            { "✗ No radar toggles selected", "✗ Nenhuma opção do radar selecionada" },
            { "✗ No radar toggles selected\n   Auto Farm disabled...", "✗ Nenhuma opção do radar selecionada\n   Coleta automática desativada..." },
            { "✗ Radar is OFF!\n   Enable Radar first.", "✗ Radar está DESLIGADO!\n   Ative o Radar primeiro." },
            { "✓ Ready", "✓ Pronto" },
            { "Area Load Delay: {0}s", "Atraso de carregamento da área: {0}s" },
            { "Resolver: STANDBY", "Resolvedor: EM ESPERA" },
            { "Resolver: READY", "Resolvedor: PRONTO" },
            { "Resolver: RESOLVING / NOT READY", "Resolvedor: RESOLVENDO / NÃO PRONTO" },
            { "LOOT PRIORITIES", "PRIORIDADES DE COLETA" },
            { "Mushrooms:", "Cogumelos:" },
            { "Events:", "Eventos:" },
            { "Other:", "Outros:" },
            { "Priority Location: None", "Local Prioritário: Nenhum" },
            { "Available: {0}", "Disponível: {0}" },
            { "Markers: {0}", "Marcadores: {0}" },
            { "RADAR SETTINGS", "CONFIGURAÇÕES DO RADAR" },
            { "Marker Style:", "Estilo do Marcador:" },
            { "Default", "Padrão" },
            { "Default ✓", "Padrão ✓" },
            { "Simple Text", "Texto Simples" },
            { "Simple Text ✓", "Texto Simples ✓" },
            { "Radar markers: Default", "Marcadores do radar: Padrão" },
            { "Radar markers: Simple Text", "Marcadores do radar: Texto simples" },
            { "Failed to save radar settings", "Falha ao salvar configurações do radar" },
            { "Radar Max Distance: {0}m", "Distância máxima do radar: {0} m" },
            { "ENABLE RADAR", "ATIVAR RADAR" },
            { "DISABLE RADAR", "DESATIVAR RADAR" },
            { "Select All Loots", "Selecionar Todos" },
            { "Clear All Loots", "Limpar Seleção" },
            { "Force Refresh Scan", "Forçar Atualização" },
            { "None", "Nenhum" },
            { "Mushrooms", "Cogumelos" },
            { "Berries", "Frutas" },
            { "Resources", "Recursos" },
            { "Trees", "Árvores" },
            { "Misc", "Diversos" },
            { "All Mushrooms", "Todos os Cogumelos" },
            { "Black Truffle", "Trufa Negra" },
            { "Stones", "Pedras" },
            { "Ores", "Minérios" },
            { "Rare Trees", "Árvores Raras" },
            { "Apple Trees", "Macieiras" },
            { "Mandarin Trees", "Laranjeiras" },
            { "Fish Shadows", "Sombras de Peixe" },
            { "Meteors", "Meteoritos" },
            { "Click Duration: {0:F1}s", "Duração do clique: {0:F1}s" },
            { "Auto-Repair Tool (Paused TP FARM): {0:F0}s", "Reparo automático (TP pausado): {0:F0}s" },
            { "Fish Detect Range", "Alcance de detecção de peixes" },
            { "Max Reel", "Recolhimento Máximo" },
            { "Hold Time", "Tempo Puxando" },
            { "Pause", "Pausar" },
            { "Equip Rod", "Equipar Vara" },
            { "Teleport Fishing", "Pesca por Teleporte" },
            { "Enable Auto Fishing", "Ativar Pesca Automática" },
            { "Disable Auto Fishing", "Desativar Pesca Automática" },
            { "Equip Axe", "Equipar Machado" },
            { "ENABLE CHOP & MINE", "ATIVAR CORTAR & MINERAR" },
            { "DISABLE CHOP & MINE", "DESATIVAR CORTAR & MINERAR" },
            { "Auto Collect", "Coleta Automática" },
            { "Collect Types:", "Tipos de Coleta:" },
            { "  Mushrooms", "  Cogumelos" },
            { "  Berries / Bushes / Plants", "  Frutas / Arbustos / Plantas" },
            { "  Event Resources", "  Recursos de Evento" },
            { "Hide UI + Player (Client Side)", "Ocultar UI + Jogador (Cliente)" },
            { "Hide Jump Button (Space still works)", "Ocultar botão de pulo (Espaço continua funcionando)" },
            { "Bunny Hop (hold Space)", "Bunny hop (segure Espaço)" },
            { "Bird Vacuum (Client Side)", "Aspirador de Pássaros (Cliente)" },
            { "Custom Camera FOV", "FOV da Câmera Customizado" },
            { "DISABLE ALL", "DESATIVAR TUDO" },
            { "REFRESH & SCAN", "ATUALIZAR & ESCANEAR" },
            { "Auto Repair", "Reparo Automático" },
            { "Eat Selected Food", "Comer Comida Selecionada" },
            { "Repair Teleport Backward", "Teleporte de Retorno após Reparo" },
            { "❄️ Auto Snow Sculpture", "❄️ Escultura de Neve Automática" },
            { "Auto Click Icon", "Ícone de Clique Automático" },
            { "Move snowballs to backpack", "Mover bolas de neve para a mochila" },
            { "Idle", "Inativo" },
            { "DISABLED", "DESATIVADO" },
            { "GATHERING...", "COLETANDO..." },
            { "TELEPORTING...", "TELETRANSPORTANDO..." },
            { "IDLE", "INATIVO" },
            { "Running (step {0})", "Executando (passo {0})" },
            { "Running (step {0}) - {1}", "Executando (passo {0}) - {1}" },
            { "Game Speed: {0:F1}x", "Velocidade do Jogo: {0:F1}x" },
            { "Camera FOV: {0:F0}°", "FOV da Câmera: {0:F0}°" },
            { "Bag Automation", "Automação da Mochila" },
            { "Repair Status: {0}", "Estado do Reparo: {0}" },
            { "Eat Status: {0}", "Estado de Comida: {0}" },
            { "Current Energy: {0}", "Energia Atual: {0}" },
            { "Bag automation already running", "Automação da bolsa já em execução" },
            { "Auto Eat will continue until energy is full.", "A alimentação automática continuará até a energia estar cheia." },
            { "Auto Eat Energy Panel", "Alimentação automática pelo painel de energia" },
            { "Auto Eat Trigger: {0}% or lower", "Acionar alimentação automática: {0}% ou menos" },
            { "Food Type", "Tipo de Comida" },
            { "Auto Repair: open bag → find {0} → Use → close bag\nAuto Eat: open bag → find {1} → Use → close bag", "Reparo automático: abrir mochila → encontrar {0} → Usar → fechar mochila\nComida automática: abrir mochila → encontrar {1} → Usar → fechar mochila" },
            { "AUTO SNOW SCULPTURE", "ESCULTURA DE NEVE AUTOMÁTICA" },
            { "Click Interval: {0:F0}ms", "Intervalo de Clique: {0:F0} ms" },
            { "Interval: {0:F0}ms", "Intervalo: {0:F0} ms" },
            { "State: {0}", "Estado: {0}" },
            { "Current Ingredient: {0}", "Ingrediente Atual: {0}" },
            { "Max per ingredient: {0}", "Máximo por ingrediente: {0}" },
            { "Home", "Início" },
            { "Animal Care", "Cuidados com Animais" },
            { "Daily Quests", "Missões diárias" },
            { "Collect Daily Quest Log", "Coletar log de missões diárias" },
            { "Auto Submit Daily Items", "Enviar itens de missões diárias" },
            { "Submit Bird Photo", "Enviar cartões de aves" },
            { "Skip 5 Star Items", "Ignorar itens de 5 estrelas" },
            { "NPCs", "NPCs" },
            { "Locations", "Locais" },
            { "Events", "Eventos" },
            { "House", "Casa" },
            { "Custom", "Personalizado" },
            { "Keybinds", "Atalhos" },
            { "UI Theme", "Tema da UI" },
            { "Localization", "Localização" },
            { "Current Language: {0}", "Idioma Atual: {0}" },
            { "Language switched to {0}", "Idioma alterado para {0}" },
            { "SETTINGS", "CONFIGURAÇÕES" },
            { "KEYBIND SETTINGS", "CONFIGURAÇÕES DE ATALHOS" },
            { "PRESS ANY KEY FOR:", "PRESSIONE QUALQUER TECLA OU BOTÃO DO MOUSE PARA:" },
            { "CANCEL", "CANCELAR" },
            { "RESET TO DEFAULTS", "RESTAURAR PADRÕES" },
            { "Defaults restored (Toggle Menu: Insert)", "Padrões restaurados (Abrir Menu: Insert)" },
            { "Enable Notifications", "Ativar Notificações" },
            { "Notifications enabled", "Notificações ativadas" },
            { "Auto Start on Lobby", "Iniciar Automaticamente no Lobby" },
            { "Auto Start enabled", "Início automático ativado" },
            { "Auto Start disabled", "Início automático desativado" },
            { "Auto Close Announcements", "Fechar Anúncios Automaticamente" },
            { "Auto Close Announcement enabled", "Fechar anúncio automaticamente ativado" },
            { "Auto Close Announcement disabled", "Fechar anúncio automaticamente desativado" },
            { "Hide ID", "Ocultar ID" },
            { "ID display hidden", "ID oculta" },
            { "ID display shown", "ID visível" },
            { "Custom ID", "ID Personalizada" },
            { "Custom ID enabled", "ID personalizada ativada" },
            { "Custom ID disabled", "ID personalizada desativada" },
            { "Value", "Valor" },
            { "Leave blank for your real ID. If filled, it replaces the visible ID.", "Deixe em branco para manter sua ID real. Se preenchido, substituirá a ID visível." },
            { "Custom ID cleared", "ID personalizada limpa" },
            { "Custom ID updated", "ID personalizada atualizada" },
            { "Show Status Overlay", "Mostrar Sobreposição de Status" },
            { "Status overlay enabled", "Sobreposição de status ativada" },
            { "Status overlay disabled", "Sobreposição de status desativada" },
            { "Block Input", "Bloquear Input" },
            { "Block Input Enabled", "Bloquear input ativado" },
            { "Block Input Disabled", "Bloquear input desativado" },
            { "FPS Bypass", "Bypass de FPS" },
            { "FPS Bypass Enabled", "Bypass de FPS ativado" },
            { "FPS Bypass Disabled", "Bypass de FPS desativado" },
            { "Target Max FPS: {0}", "FPS Máximo Alvo: {0}" },
            { "Effective cap: {0}  |  Live: {1:F0} FPS", "Limite ativo: {0}  |  Ao vivo: {1:F0} FPS" },
            { "Join Friend", "Entrar com Amigo" },
            { "Join My Town", "Entrar na Minha Cidade" },
            { "Join Public", "Entrar em Pública" },
            { "Active", "Ativo" },
            { "Running", "Executando" },
            { "Fish Farm", "Fazenda de Peixes" },
            { "Target", "Alvo" },
            { "Caught", "Capturado" },
            { "No active features", "Nenhuma função ativa" },
            { "Keybind", "Tecla" },
            { "Accent Color", "Cor de Destaque" },
            { "Status Overlay", "Sobreposição de Status" },
            { "Tab", "Aba" },
            { "Speed", "Velocidade" },
            { "Enabled", "Ativado" },
            { "Disabled", "Desativado" },
            { "Farm Status", "Status da Fazenda" },
            { "Fish Status", "Status da Pesca" },
            { "Mushroom", "Cogumelo" },
            { "Blueberry", "Mirtilo" },
            { "Raspberry", "Framboesa" },
            { "Bubble", "Bolha" },
            { "Insect", "Inseto" },
            { "Rare Tree", "Árvore Rara" },
            { "Apple Tree", "Macieira" },
            { "Mandarin Tree", "Laranjeira" },
            { "Tree", "Árvore" },
            { "Farm Rocks", "Coletar Pedras" },
            { "Farm Ores", "Coletar Minérios" },
            { "Farm Trees", "Coletar Árvores" },
            { "Farm Rare Trees", "Coletar Árvores Raras" },
            { "Farm Apple Trees", "Coletar Macieiras" },
            { "Farm Mandarin Trees", "Coletar Laranjeiras" },
            { "Reset Cooldowns", "Redefinir Tempos" },
            { "Chop & Mine flow:", "Fluxo de Cortar & Minerar:" },
            { "• Build list of available markers", "• Construir lista de marcadores disponíveis" },
            { "• Shuffle and teleport to markers", "• Embaralhar e teleportar para marcadores" },
            { "• Simulate F key for configured duration", "• Simular tecla F pelo tempo configurado" },
            { "• Mark resource collected and set cooldowns", "• Marcar recurso coletado e definir tempos" },
            { "Fish Shadow", "Sombra de Peixe" },
            { "Meteor", "Meteorito" },
            { "Stone", "Pedra" },
            { "Ore", "Minério" },
            { "Oyster", "Ostra" },
            { "Button", "Cogumelo" },
            { "Oyster Mushroom", "Ostra" },
            { "Button Mushroom", "Cogumelo" },
            { "Penny Bun", "Porcini" },
            { "Shiitake", "Shiitake" },
            { "Truffle", "Trufa" },
            { "Fiddlehead", "Samambaia" },
            { "Burdock", "Bardana" },
            { "Mustard Greens", "Mostarda Selvagem" },
            { "Tall Mustard", "Alho-Mostarda" },
            { "Blueberries", "Mirtilos" },
            { "Raspberries", "Framboesas" },
            { "Bubbles", "Bolhas" },
            { "Repair Kit", "Kit de Reparo" },
            { "Crafty Repair Kit", "Kid de Reparo Ágil" },
            { "Springday Brown Sugar", "Saco Primaveril de Açúcar Mascavo" },
            { "Salsa Sauce", "Molho Salsa" },
            { "Meat", "Carne" },
            { "Egg", "Ovo" },
            { "Milk", "Leite" },
            { "Cheese", "Queijo" },
            { "Butter", "Manteiga" },
            { "Coffee Beans", "Grãos de Café" },
            { "Tea Leaves", "Chá Preto" },
            { "Matcha Powder", "Pó de Matcha" },
            { "Rice Flour", "Macarrão de Arroz" },
            { "Red Bean", "Feijão-azuki" },
            { "Cooking Oil", "Óleo" },
            { "Pasteurized Egg", "Ovo Pasteurizado" },
            { "Bad Food", "Comida Ruim" },
            { "Blue Jam", "Geleia de Mirtilo" },
            { "Rasp Jam", "Geleia de Framboesa" },
            { "Mix Jam", "Geleia Mista" },
            { "Bake Mushroom", "Cogumelo Assado" },
            { "Salad", "Salada" },
            { "Any Food", "Qualquer Comida" },
            { "Tool durability depleted", "A durabilidade de ferramenta está esgotada, por favor use Kit de Reparo." },
            { "Scanner Durability low", "Durabilidade insuficiente do scanner." },
            { "Use", "Usar" },
            { "Eat", "Comer" },
            { "Equip Net", "Equipar Rede" },
            { "DISABLE INSECT CATCHING", "DESATIVAR CAPTURA DE INSETOS" },
            { "ENABLE INSECT CATCHING", "ATIVAR CAPTURA DE INSETOS" },
            { "Status: {0}", "Status: {0}" },
            { "Auto Stop Timer", "Temporizador de Parada Automática" },
            { "Timer (HH:MM:SS)", "Temporizador (HH:MM:SS)" },
            { "Set at least 1 second to enable auto-stop.", "Defina pelo menos 1 segundo para ativar a parada automática." },
            { "Auto-stop after: {0}", "Parada automática após: {0}" },
            { "Time remaining: {0}", "Tempo restante: {0}" },
            { "Teleport Cooldown: {0:F1}s", "Tempo entre teleportes: {0:F1}s" },
            { "Scan Timeout: {0:F1}s", "Tempo limite de escaneamento: {0:F1}s" },
            { "Auto-Repair (Paused TP Farm): {0:F0}s", "Reparo automático (fazenda TP pausada): {0:F0}s" },
            { "Teleport Offset: {0:F2}m", "Deslocamento de teleporte: {0:F2} m" },
            { "Multi-Catch Limit: {0}", "Limite de Capturas Múltiplas: {0}" },
            { "Auto Insect Farm", "Fazenda automática de insetos" },
            { "Caught This Session: {0}", "Capturados nesta sessão: {0}" },
            { "Catch Cooldown: {0:F1}s", "Tempo entre capturas: {0:F1}s" },
            { "Scan Range: {0:F0}m", "Alcance de escaneamento: {0:F0} m" },
            { "Tool: {0}", "Ferramenta: {0}" },
            { "Unknown", "Desconhecido" },
            { "Insect Farm Enabled", "Fazenda de insetos ativada" },
            { "Insect Farm Disabled", "Fazenda de insetos desativada" },
            { "Insect Farm auto-stop set: {0}", "Auto-parada da fazenda de insetos configurada: {0}" },
            { "Insect Farm auto-stopped (timer)", "Fazenda de insetos parada (temporizador)" },
            { "Bird Farm Enabled", "Fazenda de aves ativada" },
            { "Bird Farm Disabled", "Fazenda de aves desativada" },
            { "Bird Farm auto-stop set: {0}", "Auto-parada da fazenda de aves configurada: {0}" },
            { "Bird Farm auto-stopped (timer)", "Fazenda de aves parada (temporizador)" },
            { "Auto Repair started", "Reparo automático iniciado" },
            { "Auto Repair already running", "Reparo automático já em execução" },
            { "Auto Eat started ({0})", "Alimentação automática iniciada ({0})" },
            { "Auto Eat already running", "Alimentação automática já em execução" },
            { "Auto Repair triggered by durability notification - pausing farm for {0:F0}s", "Reparo automático acionado por notificação de durabilidade - pausando a fazenda por {0:F0}s" },
            { "Auto Eat triggered by energy panel ({0})", "Alimentação automática acionada pelo painel de energia ({0})" },
            { "Noclip", "Noclip" },
            { "Noclip Speed: {0:F1}", "Velocidade Noclip: {0:F1}" },
            { "Noclip Boost: {0:F1}x", "Impulso Noclip: {0:F1}x" },
            { "Anti AFK (Auto Click)", "Anti AFK (Clique Automático)" },
            { "AFK Click Interval: {0:F0}s", "Intervalo de clique AFK: {0:F0}s" },
            { "Noclip: WASD + Space/Ctrl\nShift = Speed Boost", "Noclip: WASD + Espaço/Ctrl\nShift = Aumento de Velocidade" },
            { "Building - Bypass Overlap", "Construção - Ignorar Sobreposição" },
            { "Bypass Overlap", "Ignorar Sobreposição" },
            { "Free Placement", "Posicionamento livre" },
            { "Angle", "Ângulo" },
            { "Grid", "Grade" },
            { "No surface limit", "Sem limite de superfície" },
            { "No range/height limit", "Sem limite de alcance/altura" },
            { "no object", "sem objeto" },
            { "Height", "Altura" },
            { "Plane", "Plano" },
            { "Credits: • evermoreee12 for Bypass Overlap", "Créditos: • evermoreee12 por Ignorar Sobreposição" },
            { "Keybinds saved", "Atalhos salvos" },
            { "Keybinds loaded", "Atalhos carregados" },
            { "Failed to save keybinds", "Falha ao salvar atalhos" },
            { "Failed to load keybinds", "Falha ao carregar atalhos" },
            { "UI theme saved", "Tema de UI salvo" },
            { "UI theme loaded", "Tema de UI carregado" },
            { "Failed to save UI theme", "Falha ao salvar tema de UI" },
            { "Failed to load UI theme", "Falha ao carregar tema de UI" },
            // Custom Food feature
            { "Custom Food", "Comida Personalizada" },
            { "Custom Food: Click any food item in your bag to select it", "Comida Personalizada: Clique em qualquer item de comida na mochila para selecionar" },
            { "Detected food click: {0}", "Comida detectada: {0}" },
            { "Opening bag to scan for food items...", "Abrindo mochila para escanear itens de comida..." },
            { "No food items found in bag.", "Nenhum item de comida encontrado na mochila." },
            { "Found {0} food item(s) in bag.", "Encontrado {0} item(ns) de comida na mochila." },
            { "Click 'Scan Bag' to find food items.", "Clique em 'Escanear Mochila' para encontrar itens de comida." },
            { "Open your bag and click 'Scan Bag'.", "Abra sua mochila e clique em 'Escanear Mochila'." },
            { "Custom food set to: {0}", "Comida personalizada definida para: {0}" },
            // Homeland Farm
            { "pictures.title", "Fotos" },
            { "pictures.decrypt_all", "Descriptografar tudo" },
            { "pictures.busy", "Descriptografia em andamento" },
            { "pictures.decrypting", "Descriptografando arquivos ScreenCapture..." },
            { "pictures.source_missing", "Pasta ScreenCapture não encontrada" },
            { "pictures.paths", "De:\n{0}\n\nPara:\n{1}" },
            { "pictures.progress", "Processando {0}/{1} — descriptografados {2}, texto {3}, falhas {4}, ignorados {5}" },
            { "pictures.done", "Concluído: {0} novos ({1} descriptografados, {2} texto, {3} falhas, {4} ignorados)\n{5}" },
            { "pictures.done_short", "Exportados {0} novos, ignorados {1} — {2}" },
            { "pictures.encrypt_changed", "Criptografar alterados" },
            { "pictures.scan_changed", "Verificar alterados" },
            { "pictures.encrypting", "Criptografando arquivos alterados..." },
            { "pictures.manifest_missing", "Manifesto ausente — execute Descriptografar tudo primeiro" },
            { "pictures.changed_count", "Arquivos alterados: {0}" },
            { "pictures.no_changes", "Nenhuma alteração (hashes coincidem)" },
            { "pictures.encrypt_progress", "Criptografar {0}/{1} — importados {2}, falhas {3}" },
            { "pictures.encrypt_done", "Reimportados {0} arquivo(s), {1} falhas\n{2}" },
            { "pictures.encrypt_done_short", "Reimportados {0} arquivo(s) alterado(s)" },
            { "pictures.draw_hint", "Draw: abra o desenho no editor do jogo, use Extract para exportar drawing.png, edite o PNG e depois Upload para reimportar no jogo." },
            { "pictures.done_draw", "Concluído: {0} novos ({1} descriptografados, {2} texto, {3} falhas, {4} ignorados, {5} previews Draw)\n{6}" },
            { "ice_skating.send_score_title", "Enviar pontuação de patinação" },
            { "ice_skating.challenge_button", "Desafio (5 perfeitos)" },
            { "ice_skating.challenge_score", "Pontos" },
            { "ice_skating.runs", "Voltas" },
            { "ice_skating.perfect_drill", "Treino perfeito" },
            { "homeland_farm.title", "Fazenda da Casa" },
            { "extra.title", "Extra" },
            { "craft.open", "Abrir craft" },
            { "craft.open_ok", "Painel de craft aberto." },
            { "craft.aura_unavailable", "Craft indisponível (AuraMono não pronto)." },
            { "craft.player_unavailable", "Jogador local indisponível." },
            { "craft.level_object_unavailable", "Objeto da mesa de craft indisponível." },
            { "craft.open_interact_unavailable", "OpenInteractPanel indisponível." },
            { "craft.open_interact_threw", "Falha ao abrir o painel de craft." },
            { "craft.level_manager_unavailable", "Gerenciador de objetos de nível indisponível." },
            { "craft.dictionary_unavailable", "Lista de objetos de nível indisponível." },
            { "craft.no_level_objects", "Nenhum objeto de nível encontrado. Entre na sua casa primeiro." },
            { "craft.all_tables_stored", "Todas as mesas de craft estão guardadas. Coloque uma na sua casa." },
            { "craft.no_table", "Nenhuma mesa de craft encontrada. Fique perto de uma na sua casa." },
            { "homeland_farm.water_section", "REGAR" },
            { "homeland_farm.water_radius", "Raio: {0} m" },
            { "homeland_farm.water_in_radius", "Regar no raio" },
            { "homeland_farm.water_own", "Regar próprios" },
            { "homeland_farm.water_friends", "Regar de amigos" },
            { "homeland_farm.water_unwatered", "Regar não regados" },
            { "homeland_farm.harvest_section", "COLHER" },
            { "homeland_farm.harvest_crops_all", "Colher cultivos próprios" },
            { "homeland_farm.plant_seeds_section", "SEMENTES DE PLANTA" },
            { "homeland_farm.collect_plant_seeds_all", "Coletar sementes próprias" },
            { "homeland_farm.collect_dormant", "Coletar dormentes" },
            { "homeland_farm.collect_flowers", "Coletar flores" },
            { "homeland_farm.weeds_section", "ERVAS DANINHAS" },
            { "homeland_farm.weed_all", "Capinar próprios" },
            { "homeland_farm.sow_section", "PLANTAR CULTIVOS" },
            { "homeland_farm.sow", "Semear" },
            { "homeland_farm.sow_all", "Semear" },
            { "homeland_farm.sow_in_radius", "Semear" },
            { "homeland_farm.radius_section", "RAIO DA FAZENDA" },
            { "homeland_farm.auto_section", "FAZENDA AUTOMÁTICA" },
            { "homeland_farm.auto_capture", "Capturar canteiros" },
            { "homeland_farm.auto_captured", "Capturados {0} canteiro(s)" },
            { "homeland_farm.auto_not_captured", "Não capturado — pressione Capturar canteiros" },
            { "homeland_farm.auto_hint", "Planta a semente selecionada, capina e colhe até acabar as sementes." },
            { "homeland_farm.auto_start", "Iniciar fazenda automática" },
            { "homeland_farm.auto_stop", "Parar fazenda automática" },
            { "homeland_farm.auto_fertilize", "Fertilizar automaticamente após plantar" },
            { "homeland_farm.fertilize_section", "FERTILIZAR CULTIVOS" },
            { "homeland_farm.fertilize", "Fertilizar" },
            { "homeland_farm.fertilize_all", "Fertilizar" },
            { "homeland_farm.fertilize_in_radius", "Fertilizar" },
            { "homeland_farm.stop", "Parar" },
            { "homeland_farm.status_idle", "Ocioso." },
            { "homeland_farm.status_stopped", "Parado." },
            { "homeland_farm.need_homeland", "Entre na casa primeiro." },
            { "homeland_farm.seed_storage", "Origem das sementes" },
            { "homeland_farm.fert_storage", "Origem do fertilizante" },
            { "homeland_farm.storage_backpack", "Mochila" },
            { "homeland_farm.storage_warehouse", "Armazém" },
            { "homeland_farm.storage_both", "Ambos" },
            { "homeland_farm.refresh", "Atualizar" },
            { "homeland_farm.refresh_seeds", "Atualizar sementes" },
            { "homeland_farm.refresh_fertilizers", "Atualizar fertilizantes" },
            { "homeland_farm.cached_seeds", "Em cache {0} semente(s)" },
            { "homeland_farm.press_refresh_seeds", "Pressione Atualizar sementes" },
            { "homeland_farm.cached_fertilizers", "Em cache {0} fertilizante(s)" },
            { "homeland_farm.press_refresh_fertilizers", "Pressione Atualizar fertilizantes" },
            { "homeland_farm.prev", "<" },
            { "homeland_farm.next", ">" },
            { "homeland_farm.no_seeds", "Nenhuma semente de cultivo encontrada." },
            { "homeland_farm.no_fertilizers", "Nenhum fertilizante de cultivo encontrado." },
            { "homeland_farm.operations_section", "OPERAÇÕES" },
            { "homeland_farm.radius_slider_label", "Raio" },
            { "homeland_farm.log_water_radius", "Registrar diagnóstico de rega" },
            { "homeland_farm.log_water_failed", "Registro de rega falhou." },
            { "homeland_farm.log_water_done", "Registro de rega concluído." },
            { "homeland_farm.status_warming", "Iniciando..." },
        };

        // Built-in Thai fallback strings.
        private static readonly Dictionary<string, string> ThaiDefaults = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "Self", "ตัวเอง" },
            { "Resource Gathering", "เก็บทรัพยากร" },
            { "Features", "ฟีเจอร์" },
            { "New Features", "ฟีเจอร์ใหม่" },
            { "Daily Quests", "เควสรายวัน" },
            { "Collect Daily Quest Log", "เก็บบันทึกเควสรายวัน" },
            { "Auto Submit Daily Items", "ส่งไอเทมเควสรายวันอัตโนมัติ" },
            { "Submit Bird Photo", "ส่งการ์ดนก" },
            { "Skip 5 Star Items", "ข้ามไอเทม 5 ดาว" },
            { "Radar", "เรดาร์" },
            { "Teleport", "เทเลพอร์ต" },
            { "Bag / Warehouse", "กระเป๋า / คลัง" },
            { "Settings", "การตั้งค่า" },
            { "Main", "หลัก" },
            { "Building", "สร้าง" },
            { "Foraging", "เก็บของ" },
            { "Chop & Mine", "ตัดไม้และขุดเหมือง" },
            { "Fishing", "ตกปลา" },
            { "Insects", "แมลง" },
            { "Food & Repair", "อาหารและซ่อม" },
            { "Snow Sculpting", "ปั้นหิมะ" },
            { "Auto Buy", "ซื้ออัตโนมัติ" },
            { "AUTO BUY (Cooking Store)", "ซื้ออัตโนมัติ (ร้านทำอาหาร)" },
            { "BUY ALL (COIN)", "ซื้อทั้งหมด (เหรียญ)" },
            { "Shop buy-all already running", "กำลังซื้อทั้งหมดอยู่แล้ว" },
            { "Auto Buy: Teleport -> Buy -> Return", "ซื้ออัตโนมัติ: เทเลพอร์ต -> ซื้อ -> กลับ" },
            { "ENABLE AUTO FORAGING", "เปิดเก็บของอัตโนมัติ" },
            { "DISABLE AUTO FORAGING", "ปิดเก็บของอัตโนมัติ" },
            { "Aura Farm", "ฟาร์มออร่า" },
            { "Status:", "สถานะ:" },
            { "✗ No radar toggles selected", "✗ ยังไม่ได้เลือกตัวเลือกเรดาร์" },
            { "✗ No radar toggles selected\n   Auto Farm disabled...", "✗ ยังไม่ได้เลือกตัวเลือกเรดาร์\n   ปิดฟาร์มอัตโนมัติแล้ว..." },
            { "✗ Radar is OFF!\n   Enable Radar first.", "✗ เรดาร์ปิดอยู่!\n   เปิดเรดาร์ก่อน" },
            { "✓ Ready", "✓ พร้อม" },
            { "Area Load Delay: {0}s", "หน่วงโหลดพื้นที่: {0} วิ" },
            { "Resolver: STANDBY", "ตัวแก้ปัญหา: สแตนด์บาย" },
            { "Resolver: READY", "ตัวแก้ปัญหา: พร้อม" },
            { "Resolver: RESOLVING / NOT READY", "ตัวแก้ปัญหา: กำลังแก้ / ยังไม่พร้อม" },
            { "LOOT PRIORITIES", "ลำดับความสำคัญของของ" },
            { "Mushrooms:", "เห็ด:" },
            { "Events:", "อีเวนต์:" },
            { "Other:", "อื่นๆ:" },
            { "Priority Location: None", "ตำแหน่งลำดับความสำคัญ: ไม่มี" },
            { "Available: {0}", "พร้อมใช้: {0}" },
            { "Markers: {0}", "มาร์กเกอร์: {0}" },
            { "RADAR SETTINGS", "การตั้งค่าเรดาร์" },
            { "Marker Style:", "สไตล์มาร์กเกอร์:" },
            { "Default", "ค่าเริ่มต้น" },
            { "Default ✓", "ค่าเริ่มต้น ✓" },
            { "Simple Text", "ข้อความธรรมดา" },
            { "Simple Text ✓", "ข้อความธรรมดา ✓" },
            { "Radar markers: Default", "มาร์กเกอร์เรดาร์: ค่าเริ่มต้น" },
            { "Radar markers: Simple Text", "มาร์กเกอร์เรดาร์: ข้อความธรรมดา" },
            { "Failed to save radar settings", "บันทึกการตั้งค่าเรดาร์ไม่สำเร็จ" },
            { "Radar Max Distance: {0}m", "ระยะเรดาร์สูงสุด: {0} ม." },
            { "ENABLE RADAR", "เปิดเรดาร์" },
            { "DISABLE RADAR", "ปิดเรดาร์" },
            { "Select All Loots", "เลือกทั้งหมด" },
            { "Clear All Loots", "ล้างทั้งหมด" },
            { "Force Refresh Scan", "บังคับสแกนใหม่" },
            { "None", "ไม่มี" },
            { "Mushrooms", "เห็ด" },
            { "Berries", "เบอร์รี่" },
            { "Resources", "ทรัพยากร" },
            { "Trees", "ต้นไม้" },
            { "Misc", "อื่นๆ" },
            { "All Mushrooms", "เห็ดทั้งหมด" },
            { "Black Truffle", "ทรัฟเฟิลดำ" },
            { "Stones", "หิน" },
            { "Ores", "แร่" },
            { "Rare Trees", "ต้นไม้หายาก" },
            { "Apple Trees", "ต้นแอปเปิ้ล" },
            { "Mandarin Trees", "ต้นส้ม" },
            { "Fish Shadows", "เงาปลา" },
            { "Meteors", "อุกกาบาต" },
            { "Click Duration: {0:F1}s", "ระยะเวลาคลิก: {0:F1} วิ" },
            { "Auto-Repair Tool (Paused TP FARM): {0:F0}s", "ซ่อมอัตโนมัติ (หยุด TP ฟาร์ม): {0:F0} วิ" },
            { "Fish Detect Range", "ระยะตรวจจับปลา" },
            { "Max Reel", "ดึงสูงสุด" },
            { "Hold Time", "เวลากดค้าง" },
            { "Pause", "หยุดชั่วคราว" },
            { "Equip Rod", "สวมเบ็ด" },
            { "Teleport Fishing", "เทเลพอร์ตตกปลา" },
            { "Enable Auto Fishing", "เปิดตกปลาอัตโนมัติ" },
            { "Disable Auto Fishing", "ปิดตกปลาอัตโนมัติ" },
            { "Equip Axe", "สวมขวาน" },
            { "ENABLE CHOP & MINE", "เปิดตัดไม้และขุดเหมือง" },
            { "DISABLE CHOP & MINE", "ปิดตัดไม้และขุดเหมือง" },
            { "Auto Collect", "เก็บอัตโนมัติ" },
            { "Collect Types:", "ประเภทที่เก็บ:" },
            { "  Mushrooms", "  เห็ด" },
            { "  Berries / Bushes / Plants", "  เบอร์รี่ / พุ่มไม้ / พืช" },
            { "  Event Resources", "  ทรัพยากรอีเวนต์" },
            { "Hide UI + Player (Client Side)", "ซ่อน UI + ผู้เล่น (ฝั่งไคลเอนต์)" },
            { "Hide Jump Button (Space still works)", "ซ่อนปุ่มกระโดด (Space ยังใช้ได้)" },
            { "Bunny Hop (hold Space)", "กระโดดต่อเนื่อง (กด Space ค้าง)" },
            { "Bird Vacuum (Client Side)", "ดูดนก (ฝั่งไคลเอนต์)" },
            { "Custom Camera FOV", "FOV กล้องกำหนดเอง" },
            { "DISABLE ALL", "ปิดทั้งหมด" },
            { "REFRESH & SCAN", "รีเฟรชและสแกน" },
            { "Auto Repair", "ซ่อมอัตโนมัติ" },
            { "Eat Selected Food", "กินอาหารที่เลือก" },
            { "Repair Teleport Backward", "เทเลพอร์ตกลับหลังซ่อม" },
            { "❄️ Auto Snow Sculpture", "❄️ ปั้นหิมะอัตโนมัติ" },
            { "Auto Click Icon", "ไอคอนคลิกอัตโนมัติ" },
            { "Move snowballs to backpack", "ย้ายลูกบอลหิมะไปกระเป๋า" },
            { "Idle", "ว่าง" },
            { "DISABLED", "ปิด" },
            { "GATHERING...", "กำลังเก็บ..." },
            { "TELEPORTING...", "กำลังเทเลพอร์ต..." },
            { "IDLE", "ว่าง" },
            { "Running (step {0})", "กำลังทำงาน (ขั้นตอน {0})" },
            { "Running (step {0}) - {1}", "กำลังทำงาน (ขั้นตอน {0}) - {1}" },
            { "Game Speed: {0:F1}x", "ความเร็วเกม: {0:F1}x" },
            { "Camera FOV: {0:F0}°", "FOV กล้อง: {0:F0}°" },
            { "Bag Automation", "ระบบอัตโนมัติกระเป๋า" },
            { "Repair Status: {0}", "สถานะซ่อม: {0}" },
            { "Eat Status: {0}", "สถานะกิน: {0}" },
            { "Current Energy: {0}", "พลังงานปัจจุบัน: {0}" },
            { "Bag automation already running", "ระบบกระเป๋าทำงานอยู่แล้ว" },
            { "Auto Eat will continue until energy is full.", "กินอัตโนมัติจะทำงานจนกว่าพลังงานจะเต็ม" },
            { "Auto Eat Energy Panel", "กินอัตโนมัติจากแผงพลังงาน" },
            { "Auto Eat Trigger: {0}% or lower", "เริ่มกินอัตโนมัติ: {0}% หรือต่ำกว่า" },
            { "Food Type", "ประเภทอาหาร" },
            { "Auto Repair: open bag → find {0} → Use → close bag\nAuto Eat: open bag → find {1} → Use → close bag", "ซ่อมอัตโนมัติ: เปิดกระเป๋า → หา {0} → ใช้ → ปิดกระเป๋า\nกินอัตโนมัติ: เปิดกระเป๋า → หา {1} → ใช้ → ปิดกระเป๋า" },
            { "AUTO SNOW SCULPTURE", "ปั้นหิมะอัตโนมัติ" },
            { "Click Interval: {0:F0}ms", "ช่วงคลิก: {0} มิลลิวิ" },
            { "Interval: {0:F0}ms", "ช่วงเวลา: {0} มิลลิวิ" },
            { "State: {0}", "สถานะ: {0}" },
            { "Current Ingredient: {0}", "วัตถุดิบปัจจุบัน: {0}" },
            { "Max per ingredient: {0}", "สูงสุดต่อวัตถุดิบ: {0}" },
            { "Home", "หน้าแรก" },
            { "Animal Care", "ดูแลสัตว์" },
            { "NPCs", "NPC" },
            { "Locations", "ตำแหน่ง" },
            { "Events", "อีเวนต์" },
            { "House", "บ้าน" },
            { "Custom", "กำหนดเอง" },
            { "Keybinds", "ปุ่มลัด" },
            { "UI Theme", "ธีม UI" },
            { "Localization", "ภาษา" },
            { "Current Language: {0}", "ภาษาปัจจุบัน: {0}" },
            { "Language switched to {0}", "เปลี่ยนภาษาเป็น {0}" },
            { "SETTINGS", "การตั้งค่า" },
            { "KEYBIND SETTINGS", "การตั้งค่าปุ่มลัด" },
            { "PRESS ANY KEY FOR:", "กดปุ่มคีย์บอร์ดหรือปุ่มเมาส์ใดก็ได้สำหรับ:" },
            { "CANCEL", "ยกเลิก" },
            { "RESET TO DEFAULTS", "รีเซ็ตเป็นค่าเริ่มต้น" },
            { "Defaults restored (Toggle Menu: Insert)", "คืนค่าเริ่มต้นแล้ว (เปิดเมนู: Insert)" },
            { "Enable Notifications", "เปิดการแจ้งเตือน" },
            { "Notifications enabled", "เปิดการแจ้งเตือนแล้ว" },
            { "Auto Start on Lobby", "เริ่มอัตโนมัติในล็อบบี้" },
            { "Auto Start enabled", "เปิดเริ่มอัตโนมัติแล้ว" },
            { "Auto Start disabled", "ปิดเริ่มอัตโนมัติแล้ว" },
            { "Auto Close Announcements", "ปิดประกาศอัตโนมัติ" },
            { "Auto Close Announcement enabled", "เปิดปิดประกาศอัตโนมัติแล้ว" },
            { "Auto Close Announcement disabled", "ปิดปิดประกาศอัตโนมัติแล้ว" },
            { "Hide ID", "ซ่อน ID" },
            { "ID display hidden", "ซ่อน ID แล้ว" },
            { "ID display shown", "แสดง ID แล้ว" },
            { "Custom ID", "ID กำหนดเอง" },
            { "Custom ID enabled", "เปิด ID กำหนดเองแล้ว" },
            { "Custom ID disabled", "ปิด ID กำหนดเองแล้ว" },
            { "Value", "ค่า" },
            { "Leave blank for your real ID. If filled, it replaces the visible ID.", "เว้นว่างเพื่อใช้ ID จริง หากกรอกจะแทนที่ ID ที่แสดง" },
            { "Custom ID cleared", "ล้าง ID กำหนดเองแล้ว" },
            { "Custom ID updated", "อัปเดต ID กำหนดเองแล้ว" },
            { "Show Status Overlay", "แสดงแผงสถานะ" },
            { "Status overlay enabled", "เปิดแผงสถานะแล้ว" },
            { "Status overlay disabled", "ปิดแผงสถานะแล้ว" },
            { "Block Input", "บล็อกอินพุต" },
            { "Block Input Enabled", "เปิดบล็อกอินพุตแล้ว" },
            { "Block Input Disabled", "ปิดบล็อกอินพุตแล้ว" },
            { "FPS Bypass", "บายพาส FPS" },
            { "FPS Bypass Enabled", "เปิดบายพาส FPS แล้ว" },
            { "FPS Bypass Disabled", "ปิดบายพาส FPS แล้ว" },
            { "Target Max FPS: {0}", "FPS สูงสุดเป้าหมาย: {0}" },
            { "Effective cap: {0}  |  Live: {1:F0} FPS", "เพดานที่ใช้: {0}  |  สด: {1:F0} FPS" },
            { "Join Friend", "เข้าร่วมเพื่อน" },
            { "Join My Town", "เข้าเมืองของฉัน" },
            { "Join Public", "เข้าแบบสาธารณะ" },
            { "Active", "ใช้งาน" },
            { "Running", "กำลังทำงาน" },
            { "Fish Farm", "ฟาร์มปลา" },
            { "Target", "เป้าหมาย" },
            { "Caught", "จับได้" },
            { "No active features", "ไม่มีฟีเจอร์ที่เปิดอยู่" },
            { "Keybind", "ปุ่มลัด" },
            { "Accent Color", "สีเน้น" },
            { "Status Overlay", "แผงสถานะ" },
            { "Tab", "แท็บ" },
            { "Speed", "ความเร็ว" },
            { "Enabled", "เปิด" },
            { "Disabled", "ปิด" },
            { "Farm Status", "สถานะฟาร์ม" },
            { "Fish Status", "สถานะตกปลา" },
            { "Mushroom", "เห็ด" },
            { "Blueberry", "บลูเบอร์รี่" },
            { "Raspberry", "ราสเบอร์รี่" },
            { "Bubble", "ฟอง" },
            { "Bird", "นก" },
            { "Insect", "แมลง" },
            { "Rare Tree", "ต้นไม้หายาก" },
            { "Apple Tree", "ต้นแอปเปิ้ล" },
            { "Mandarin Tree", "ต้นส้ม" },
            { "Tree", "ต้นไม้" },
            { "Farm Rocks", "ฟาร์มหิน" },
            { "Farm Ores", "ฟาร์มแร่" },
            { "Farm Trees", "ฟาร์มต้นไม้" },
            { "Farm Rare Trees", "ฟาร์มต้นไม้หายาก" },
            { "Farm Apple Trees", "ฟาร์มต้นแอปเปิ้ล" },
            { "Farm Mandarin Trees", "ฟาร์มต้นส้ม" },
            { "Reset Cooldowns", "รีเซ็ตคูลดาวน์" },
            { "Chop & Mine flow:", "ขั้นตอนตัดไม้และขุดเหมือง:" },
            { "• Build list of available markers", "• สร้างรายการมาร์กเกอร์ที่มี" },
            { "• Shuffle and teleport to markers", "• สุ่มและเทเลพอร์ตไปยังมาร์กเกอร์" },
            { "• Simulate F key for configured duration", "• จำลองปุ่ม F ตามเวลาที่ตั้ง" },
            { "• Mark resource collected and set cooldowns", "• ทำเครื่องหมายว่าเก็บแล้วและตั้งคูลดาวน์" },
            { "Fish Shadow", "เงาปลา" },
            { "Meteor", "อุกกาบาต" },
            { "Stone", "หิน" },
            { "Ore", "แร่" },
            { "Oyster", "หอยนางรม" },
            { "Button", "เห็ด" },
            { "Oyster Mushroom", "เห็ดหูหนู" },
            { "Button Mushroom", "เห็ด" },
            { "Penny Bun", "เห็ดโปรตินี" },
            { "Shiitake", "เห็ดหอม" },
            { "Truffle", "ทรัฟเฟิล" },
            { "Fiddlehead", "ยอดเฟิร์น" },
            { "Burdock", "ขมิ้น" },
            { "Mustard Greens", "ผักกาดขม" },
            { "Tall Mustard", "ผักกาดป่า" },
            { "Blueberries", "บลูเบอร์รี่" },
            { "Raspberries", "ราสเบอร์รี่" },
            { "Bubbles", "ฟอง" },
            { "Birds", "นก" },
            { "Repair Kit", "ชุดซ่อม" },
            { "Crafty Repair Kit", "ชุดซ่อมพิเศษ" },
            { "Springday Brown Sugar", "น้ำตาลทรายแดงสปริงเดย์" },
            { "Salsa Sauce", "ซอสซัลซ่า" },
            { "Pasteurized Egg", "ไข่พาสเจอร์ไรซ์" },
            { "Meat", "เนื้อ" },
            { "Red Bean", "ถั่วแดง" },
            { "Egg", "ไข่" },
            { "Milk", "นม" },
            { "Rice Flour", "แป้งข้าว" },
            { "Tea Leaves", "ใบชา" },
            { "Cooking Oil", "น้ำมันปรุงอาหาร" },
            { "Matcha Powder", "ผงมัทฉะ" },
            { "Cheese", "ชีส" },
            { "Butter", "เนย" },
            { "Coffee Beans", "เมล็ดกาแฟ" },
            { "Bad Food", "อาหารเสีย" },
            { "Blue Jam", "แยมบลูเบอร์รี่" },
            { "Rasp Jam", "แยมราสเบอร์รี่" },
            { "Mix Jam", "แยมรวม" },
            { "Bake Mushroom", "เห็ดอบ" },
            { "Salad", "สลัด" },
            { "Any Food", "อาหารใดก็ได้" },
            { "Tool durability depleted", "ความทนทานเครื่องมือหมดแล้ว" },
            { "Scanner Durability low", "ความทนทานสแกนเนอร์ต่ำ" },
            { "Use", "ใช้" },
            { "Eat", "กิน" },
            { "Equip Net", "สวมตาข่าย" },
            { "Equip Bird Scanner", "สวมสแกนเนอร์นก" },
            { "DISABLE INSECT CATCHING", "ปิดจับแมลง" },
            { "ENABLE INSECT CATCHING", "เปิดจับแมลง" },
            { "DISABLE BIRD CATCHING", "ปิดจับนก" },
            { "ENABLE BIRD CATCHING", "เปิดจับนก" },
            { "Status: {0}", "สถานะ: {0}" },
            { "Auto Stop Timer", "ตัวจับเวลาหยุดอัตโนมัติ" },
            { "Timer (HH:MM:SS)", "ตัวจับเวลา (HH:MM:SS)" },
            { "Set at least 1 second to enable auto-stop.", "ตั้งอย่างน้อย 1 วินาทีเพื่อเปิดหยุดอัตโนมัติ" },
            { "Auto-stop after: {0}", "หยุดอัตโนมัติหลัง: {0}" },
            { "Time remaining: {0}", "เวลาที่เหลือ: {0}" },
            { "Teleport Cooldown: {0:F1}s", "คูลดาวน์เทเลพอร์ต: {0:F1} วิ" },
            { "Scan Timeout: {0:F1}s", "หมดเวลาสแกน: {0:F1} วิ" },
            { "Auto-Repair (Paused TP Farm): {0:F0}s", "ซ่อมอัตโนมัติ (หยุด TP ฟาร์ม): {0:F0} วิ" },
            { "Teleport Offset: {0:F2}m", "ออฟเซ็ตเทเลพอร์ต: {0:F2} ม." },
            { "Multi-Catch Limit: {0}", "จำกัดจับหลายตัว: {0}" },
            { "Auto Insect Farm", "ฟาร์มแมลงอัตโนมัติ" },
            { "Auto Bird Farm", "ฟาร์มนกอัตโนมัติ" },
            { "Caught This Session: {0}", "จับในเซสชันนี้: {0}" },
            { "Catch Cooldown: {0:F1}s", "คูลดาวน์จับ: {0:F1} วิ" },
            { "Scan Range: {0:F0}m", "ระยะสแกน: {0:F0} ม." },
            { "Tool: {0}", "เครื่องมือ: {0}" },
            { "Unknown", "ไม่ทราบ" },
            { "Insect Farm Enabled", "เปิดฟาร์มแมลงแล้ว" },
            { "Insect Farm Disabled", "ปิดฟาร์มแมลงแล้ว" },
            { "Insect Farm auto-stop set: {0}", "ตั้งหยุดฟาร์มแมลงอัตโนมัติ: {0}" },
            { "Insect Farm auto-stopped (timer)", "ฟาร์มแมลงหยุดอัตโนมัติ (ตัวจับเวลา)" },
            { "Bird Farm Enabled", "เปิดฟาร์มนกแล้ว" },
            { "Bird Farm Disabled", "ปิดฟาร์มนกแล้ว" },
            { "Bird Farm auto-stop set: {0}", "ตั้งหยุดฟาร์มนกอัตโนมัติ: {0}" },
            { "Bird Farm auto-stopped (timer)", "ฟาร์มนกหยุดอัตโนมัติ (ตัวจับเวลา)" },
            { "Auto Repair started", "เริ่มซ่อมอัตโนมัติแล้ว" },
            { "Auto Repair already running", "ซ่อมอัตโนมัติทำงานอยู่แล้ว" },
            { "Auto Eat started ({0})", "เริ่มกินอัตโนมัติ ({0})" },
            { "Auto Eat already running", "กินอัตโนมัติทำงานอยู่แล้ว" },
            { "Noclip", "Noclip" },
            { "Noclip Speed: {0:F1}", "ความเร็ว Noclip: {0:F1}" },
            { "Noclip Boost: {0:F1}x", "บูสต์ Noclip: {0:F1}x" },
            { "Anti AFK (Auto Click)", "กัน AFK (คลิกอัตโนมัติ)" },
            { "AFK Click Interval: {0:F0}s", "ช่วงคลิก AFK: {0:F0} วิ" },
            { "Noclip: WASD + Space/Ctrl\nShift = Speed Boost", "Noclip: WASD + Space/Ctrl\nShift = บูสต์ความเร็ว" },
            { "Building - Bypass Overlap", "สร้าง - ข้ามการทับซ้อน" },
            { "Bypass Overlap", "ข้ามการทับซ้อน" },
            { "Free Placement", "วางอิสระ" },
            { "Angle", "มุม" },
            { "Grid", "กริด" },
            { "No surface limit", "ไม่จำกัดพื้นผิว" },
            { "No range/height limit", "ไม่จำกัดระยะ/ความสูง" },
            { "no object", "ไม่มีวัตถุ" },
            { "Height", "ความสูง" },
            { "Plane", "ระนาบ" },
            { "Credits: • evermoreee12 for Bypass Overlap", "เครดิต: • evermoreee12 สำหรับข้ามการทับซ้อน" },
            { "Auto Repair triggered by durability notification - pausing farm for {0:F0}s", "ซ่อมอัตโนมัติจากแจ้งเตือนความทนทาน - หยุดฟาร์ม {0:F0} วิ" },
            { "Auto Eat triggered by energy panel ({0})", "กินอัตโนมัติจากแผงพลังงาน ({0})" },
            { "Keybinds saved", "บันทึกปุ่มลัดแล้ว" },
            { "Keybinds loaded", "โหลดปุ่มลัดแล้ว" },
            { "Failed to save keybinds", "บันทึกปุ่มลัดไม่สำเร็จ" },
            { "Failed to load keybinds", "โหลดปุ่มลัดไม่สำเร็จ" },
            { "UI theme saved", "บันทึกธีม UI แล้ว" },
            { "UI theme loaded", "โหลดธีม UI แล้ว" },
            { "Failed to save UI theme", "บันทึกธีม UI ไม่สำเร็จ" },
            { "Failed to load UI theme", "โหลดธีม UI ไม่สำเร็จ" },
            { "Custom Food", "อาหารกำหนดเอง" },
            { "Custom Food: Click any food item in your bag to select it", "อาหารกำหนดเอง: คลิกไอเทมอาหารในกระเป๋าเพื่อเลือก" },
            { "Detected food click: {0}", "ตรวจพบการคลิกอาหาร: {0}" },
            { "Opening bag to scan for food items...", "กำลังเปิดกระเป๋าเพื่อสแกนอาหาร..." },
            { "No food items found in bag.", "ไม่พบอาหารในกระเป๋า" },
            { "Found {0} food item(s) in bag.", "พบอาหาร {0} รายการในกระเป๋า" },
            { "Click 'Scan Bag' to find food items.", "คลิก 'สแกนกระเป๋า' เพื่อหาอาหาร" },
            { "Open your bag and click 'Scan Bag'.", "เปิดกระเป๋าแล้วคลิก 'สแกนกระเป๋า'" },
            { "Custom food set to: {0}", "ตั้งอาหารกำหนดเองเป็น: {0}" },
            { "pictures.title", "รูปภาพ" },
            { "pictures.decrypt_all", "ถอดรหัสทั้งหมด" },
            { "pictures.busy", "กำลังถอดรหัสอยู่" },
            { "pictures.decrypting", "กำลังถอดรหัสไฟล์ ScreenCapture..." },
            { "pictures.source_missing", "ไม่พบโฟลเดอร์ ScreenCapture" },
            { "pictures.paths", "จาก:\n{0}\n\nไปยัง:\n{1}" },
            { "pictures.progress", "ประมวลผล {0}/{1} — ถอดรหัส {2}, ข้อความ {3}, ล้มเหลว {4}, ข้าม {5}" },
            { "pictures.done", "เสร็จสิ้น: {0} ไฟล์ใหม่ (ถอดรหัส {1}, ข้อความ {2}, ล้มเหลว {3}, ข้าม {4})\n{5}" },
            { "pictures.done_short", "ส่งออกใหม่ {0}, ข้าม {1} — {2}" },
            { "pictures.encrypt_changed", "เข้ารหัสที่แก้ไข" },
            { "pictures.scan_changed", "สแกนไฟล์ที่แก้ไข" },
            { "pictures.encrypting", "กำลังเข้ารหัสไฟล์ที่แก้ไข..." },
            { "pictures.manifest_missing", "ไม่มี manifest — รัน Decrypt all ก่อน" },
            { "pictures.changed_count", "ไฟล์ที่แก้ไข: {0}" },
            { "pictures.no_changes", "ไม่มีการเปลี่ยนแปลง (แฮชตรงกับ manifest)" },
            { "pictures.encrypt_progress", "เข้ารหัส {0}/{1} — นำเข้า {2}, ล้มเหลว {3}" },
            { "pictures.encrypt_done", "นำเข้าใหม่ {0} ไฟล์, ล้มเหลว {1}\n{2}" },
            { "pictures.encrypt_done_short", "นำเข้าใหม่ {0} ไฟล์ที่แก้ไข" },
            { "pictures.draw_hint", "Draw: เปิดภาพวาดในเอดิเตอร์ของเกม กด Extract เพื่อบันทึก drawing.png แก้ไขไฟล์ PNG แล้วกด Upload เพื่อนำกลับเข้าเกม" },
            { "pictures.done_draw", "เสร็จสิ้น: {0} ใหม่ (ถอดรหัส {1}, ข้อความ {2}, ล้มเหลว {3}, ข้าม {4}, Draw preview {5})\n{6}" },
            { "ice_skating.send_score_title", "ส่งคะแนนเล่นสเก็ต" },
            { "ice_skating.challenge_button", "ท้าทาย (สมบูรณ์แบบ 5 ครั้ง)" },
            { "ice_skating.challenge_score", "คะแนน" },
            { "ice_skating.runs", "รอบ" },
            { "ice_skating.perfect_drill", "ฝึกสมบูรณ์แบบ" },
            { "homeland_farm.title", "ฟาร์มในบ้าน" },
            { "extra.title", "เพิ่มเติม" },
            { "craft.open", "เปิดคราฟต์" },
            { "craft.open_ok", "เปิดแผงคราฟต์แล้ว" },
            { "craft.aura_unavailable", "คราฟต์ไม่พร้อมใช้งาน (AuraMono ยังไม่พร้อม)" },
            { "craft.player_unavailable", "ผู้เล่นท้องถิ่นไม่พร้อมใช้งาน" },
            { "craft.level_object_unavailable", "วัตถุโต๊ะคราฟต์ไม่พร้อมใช้งาน" },
            { "craft.open_interact_unavailable", "OpenInteractPanel ไม่พร้อมใช้งาน" },
            { "craft.open_interact_threw", "เปิดแผงคราฟต์ไม่สำเร็จ" },
            { "craft.level_manager_unavailable", "ตัวจัดการวัตถุระดับไม่พร้อมใช้งาน" },
            { "craft.dictionary_unavailable", "รายการวัตถุระดับไม่พร้อมใช้งาน" },
            { "craft.no_level_objects", "ไม่พบวัตถุระดับ กรุณาเข้าบ้านก่อน" },
            { "craft.all_tables_stored", "โต๊ะคราฟต์ทั้งหมดถูกเก็บแล้ว วางโต๊ะในบ้านของคุณ" },
            { "craft.no_table", "ไม่พบโต๊ะคราฟต์ ยืนใกล้โต๊ะในบ้านของคุณ" },
            { "homeland_farm.water_section", "รดน้ำ" },
            { "homeland_farm.water_radius", "รัศมี: {0} ม." },
            { "homeland_farm.water_in_radius", "รดน้ำในรัศมี" },
            { "homeland_farm.water_own", "รดน้ำของตัวเอง" },
            { "homeland_farm.water_friends", "รดน้ำของเพื่อน" },
            { "homeland_farm.water_unwatered", "รดน้ำที่ยังไม่ได้รด" },
            { "homeland_farm.harvest_section", "เก็บเกี่ยว" },
            { "homeland_farm.harvest_crops_all", "เก็บเกี่ยวพืชของตัวเองทั้งหมด" },
            { "homeland_farm.plant_seeds_section", "เมล็ดพืช" },
            { "homeland_farm.collect_plant_seeds_all", "เก็บเมล็ดพืชของตัวเองทั้งหมด" },
            { "homeland_farm.collect_dormant", "เก็บที่พักตัว" },
            { "homeland_farm.collect_flowers", "เก็บดอกไม้" },
            { "homeland_farm.weeds_section", "วัชพืช" },
            { "homeland_farm.weed_all", "ถอนวัชพืชของตัวเองทั้งหมด" },
            { "homeland_farm.sow_section", "หว่านพืช" },
            { "homeland_farm.sow", "หว่าน" },
            { "homeland_farm.sow_all", "หว่าน" },
            { "homeland_farm.sow_in_radius", "หว่าน" },
            { "homeland_farm.radius_section", "รัศมีฟาร์ม" },
            { "homeland_farm.auto_section", "ฟาร์มอัตโนมัติ" },
            { "homeland_farm.auto_capture", "จับแปลงปลูก" },
            { "homeland_farm.auto_captured", "จับแปลงปลูก {0} แปลง" },
            { "homeland_farm.auto_not_captured", "ยังไม่ได้จับ — กดจับแปลงปลูก" },
            { "homeland_farm.auto_hint", "หว่านเมล็ดที่เลือก ถอนวัชพืช และเก็บเกี่ยวจนกว่าเมล็ดจะหมด" },
            { "homeland_farm.auto_start", "เริ่มฟาร์มอัตโนมัติ" },
            { "homeland_farm.auto_stop", "หยุดฟาร์มอัตโนมัติ" },
            { "homeland_farm.auto_fertilize", "ใส่ปุ๋ยอัตโนมัติหลังหว่าน" },
            { "homeland_farm.fertilize_section", "ใส่ปุ๋ยพืช" },
            { "homeland_farm.fertilize", "ใส่ปุ๋ย" },
            { "homeland_farm.fertilize_all", "ใส่ปุ๋ย" },
            { "homeland_farm.fertilize_in_radius", "ใส่ปุ๋ย" },
            { "homeland_farm.stop", "หยุด" },
            { "homeland_farm.status_idle", "ว่าง" },
            { "homeland_farm.status_stopped", "หยุดแล้ว" },
            { "homeland_farm.need_homeland", "เข้าบ้านก่อน" },
            { "homeland_farm.seed_storage", "แหล่งเมล็ด" },
            { "homeland_farm.fert_storage", "แหล่งปุ๋ย" },
            { "homeland_farm.storage_backpack", "กระเป๋า" },
            { "homeland_farm.storage_warehouse", "คลัง" },
            { "homeland_farm.storage_both", "ทั้งสอง" },
            { "homeland_farm.refresh", "รีเฟรช" },
            { "homeland_farm.refresh_seeds", "รีเฟรชเมล็ด" },
            { "homeland_farm.refresh_fertilizers", "รีเฟรชปุ๋ย" },
            { "homeland_farm.cached_seeds", "แคชเมล็ด {0} รายการ" },
            { "homeland_farm.press_refresh_seeds", "กดรีเฟรชเมล็ด" },
            { "homeland_farm.cached_fertilizers", "แคชปุ๋ย {0} รายการ" },
            { "homeland_farm.press_refresh_fertilizers", "กดรีเฟรชปุ๋ย" },
            { "homeland_farm.prev", "<" },
            { "homeland_farm.next", ">" },
            { "homeland_farm.no_seeds", "ไม่พบเมล็ดพืช" },
            { "homeland_farm.no_fertilizers", "ไม่พบปุ๋ยพืช" },
            { "homeland_farm.operations_section", "การทำงาน" },
            { "homeland_farm.radius_slider_label", "รัศมี" },
            { "homeland_farm.log_water_radius", "บันทึกการวินิจฉัยการรดน้ำ" },
            { "homeland_farm.log_water_failed", "บันทึกการรดน้ำล้มเหลว" },
            { "homeland_farm.log_water_done", "บันทึกการรดน้ำเสร็จแล้ว" },
            { "homeland_farm.status_warming", "กำลังเริ่มต้น..." },
        };

        // Runtime localization state.
        private static Dictionary<string, string> currentTranslations = new Dictionary<string, string>(StringComparer.Ordinal);
        private static string localizationDirectory;

        public static string CurrentLanguage { get; private set; } = "en";

        // Public API
        public static void Initialize(string baseDirectory, string preferredLanguage)
        {
            HelperPaths.TryMigrateLegacyUserData(baseDirectory);
            localizationDirectory = HelperPaths.GetDirectory("Localization");
            Directory.CreateDirectory(localizationDirectory);

            EnsureDefaultFile("en", EnglishDefaults);
            EnsureDefaultFile("es", SpanishDefaults);
            EnsureDefaultFile("zh-CN", ChineseSimplifiedDefaults);
            EnsureDefaultFile("pt-BR", PortugueseDefaults);
            EnsureDefaultFile("th", ThaiDefaults);
            SetLanguage(preferredLanguage);
        }

        public static string[] GetAvailableLanguageCodes()
        {
            if (string.IsNullOrEmpty(localizationDirectory) || !Directory.Exists(localizationDirectory))
            {
                return new[] { "en" };
            }

            string[] codes = Directory.GetFiles(localizationDirectory, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(code => string.Equals(code, "en", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(code => code, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return codes.Length > 0 ? codes : new[] { "en" };
        }

        public static string GetLanguageDisplayName(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return LanguageNames["en"];
            }

            return LanguageNames.TryGetValue(code, out string displayName) ? displayName : code.ToUpperInvariant();
        }

        public static void SetLanguage(string preferredLanguage)
        {
            string[] availableLanguages = GetAvailableLanguageCodes();
            string resolvedLanguage = availableLanguages.FirstOrDefault(code => string.Equals(code, preferredLanguage, StringComparison.OrdinalIgnoreCase)) ?? "en";
            currentTranslations = LoadLanguageFile(resolvedLanguage);
            CurrentLanguage = resolvedLanguage;
        }

        public static string Translate(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            if (string.Equals(CurrentLanguage, "en", StringComparison.OrdinalIgnoreCase))
            {
                if (currentTranslations.TryGetValue(text, out string englishOverride) && !string.IsNullOrEmpty(englishOverride))
                {
                    return englishOverride;
                }

                if (EnglishDefaults.TryGetValue(text, out string englishDefault) && !string.IsNullOrEmpty(englishDefault))
                {
                    return englishDefault;
                }

                return text;
            }

            if (currentTranslations.TryGetValue(text, out string localizedText) && !string.IsNullOrEmpty(localizedText))
            {
                if (LooksBrokenLocalization(localizedText))
                {
                    string fallback = GetDefaultTranslationForCurrentLanguage(text);
                    if (!string.IsNullOrEmpty(fallback))
                    {
                        return fallback;
                    }
                }

                return localizedText;
            }

            return text;
        }

        public static string Format(string format, params object[] args)
        {
            string translatedFormat = Translate(format);
            return args == null || args.Length == 0 ? translatedFormat : string.Format(translatedFormat, args);
        }

        public static string[] GetTranslationCandidates(string key)
        {
            HashSet<string> candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddTranslationCandidates(candidates, key);
            AddTranslationCandidates(candidates, GetDefaultValue(EnglishDefaults, key));
            AddTranslationCandidates(candidates, GetDefaultValue(SpanishDefaults, key));
            AddTranslationCandidates(candidates, GetDefaultValue(ChineseSimplifiedDefaults, key));
            AddTranslationCandidates(candidates, GetDefaultValue(ThaiDefaults, key));

            if (currentTranslations.TryGetValue(key, out string localizedValue))
            {
                AddTranslationCandidates(candidates, localizedValue);
            }

            return candidates.ToArray();
        }

        // File loading / persistence
        private static void EnsureDefaultFile(string languageCode, Dictionary<string, string> defaults)
        {
            string path = Path.Combine(localizationDirectory, languageCode + ".json");
            if (!File.Exists(path))
            {
                File.WriteAllText(path, SerializeDictionary(CreateOrderedDictionary(defaults, null)));
                return;
            }

            try
            {
                string json = File.ReadAllText(path);
                Dictionary<string, string> existing = ParseDictionary(json);
                Dictionary<string, string> leakSource = string.Equals(languageCode, "en", StringComparison.OrdinalIgnoreCase)
                    ? SpanishDefaults
                    : null;
                Dictionary<string, string> ordered = CreateOrderedDictionary(defaults, existing, leakSource);
                string orderedJson = SerializeDictionary(ordered);
                if (!string.Equals(json.Trim(), orderedJson.Trim(), StringComparison.Ordinal))
                {
                    File.WriteAllText(path, orderedJson);
                }
            }
            catch
            {
                File.WriteAllText(path, SerializeDictionary(CreateOrderedDictionary(defaults, null)));
            }
        }

        private static Dictionary<string, string> LoadLanguageFile(string languageCode)
        {
            try
            {
                string path = Path.Combine(localizationDirectory, languageCode + ".json");
                if (!File.Exists(path))
                {
                    return new Dictionary<string, string>(StringComparer.Ordinal);
                }

                string json = File.ReadAllText(path);
                return ParseDictionary(json);
            }
            catch
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }
        }

        private static string GetDefaultValue(Dictionary<string, string> source, string key)
        {
            return source.TryGetValue(key, out string value) ? value : null;
        }

        private static string GetDefaultTranslationForCurrentLanguage(string key)
        {
            if (string.Equals(CurrentLanguage, "zh-CN", StringComparison.OrdinalIgnoreCase))
            {
                return GetDefaultValue(ChineseSimplifiedDefaults, key);
            }

            if (string.Equals(CurrentLanguage, "es", StringComparison.OrdinalIgnoreCase))
            {
                return GetDefaultValue(SpanishDefaults, key);
            }

            if (string.Equals(CurrentLanguage, "pt-BR", StringComparison.OrdinalIgnoreCase))
            {
                return GetDefaultValue(PortugueseDefaults, key);
            }

            if (string.Equals(CurrentLanguage, "th", StringComparison.OrdinalIgnoreCase))
            {
                return GetDefaultValue(ThaiDefaults, key);
            }

            return GetDefaultValue(EnglishDefaults, key);
        }

        private static readonly Dictionary<string, HashSet<string>> ObsoleteLocalizationValues =
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
            {
                ["pictures.draw_hint"] = new HashSet<string>(StringComparer.Ordinal)
                {
                    "Draw: edit colored Draw/*.png files. Index maps are stored in Draw/.index/ for roundtrip.",
                },
            };

        private static Dictionary<string, string> CreateOrderedDictionary(
            Dictionary<string, string> defaults,
            Dictionary<string, string> existing,
            Dictionary<string, string> leakedDefaults = null)
        {
            Dictionary<string, string> ordered = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (KeyValuePair<string, string> pair in defaults)
            {
                if (existing != null && existing.TryGetValue(pair.Key, out string existingValue))
                {
                    ordered[pair.Key] = ShouldReplaceObsoleteLocalization(pair.Key, existingValue)
                        || ShouldReplaceCrossLanguageLeak(pair.Key, pair.Value, existingValue, leakedDefaults)
                        || ShouldPreferDefaultTranslation(pair.Value, existingValue)
                        ? pair.Value
                        : (existingValue ?? string.Empty);
                }
                else
                {
                    ordered[pair.Key] = pair.Value ?? string.Empty;
                }
            }

            if (existing != null)
            {
                foreach (KeyValuePair<string, string> pair in existing)
                {
                    if (!ordered.ContainsKey(pair.Key))
                    {
                        ordered[pair.Key] = pair.Value ?? string.Empty;
                    }
                }
            }

            return ordered;
        }

        private static bool ShouldReplaceObsoleteLocalization(string key, string existingValue)
        {
            return !string.IsNullOrEmpty(existingValue)
                && ObsoleteLocalizationValues.TryGetValue(key, out HashSet<string> obsoleteValues)
                && obsoleteValues.Contains(existingValue);
        }

        private static bool ShouldReplaceCrossLanguageLeak(
            string key,
            string defaultValue,
            string existingValue,
            Dictionary<string, string> leakedDefaults)
        {
            if (leakedDefaults == null || string.IsNullOrEmpty(existingValue) || string.IsNullOrEmpty(defaultValue))
            {
                return false;
            }

            string leakedValue = GetDefaultValue(leakedDefaults, key);
            return !string.IsNullOrEmpty(leakedValue)
                && string.Equals(existingValue, leakedValue, StringComparison.Ordinal)
                && !string.Equals(defaultValue, leakedValue, StringComparison.Ordinal);
        }

        private static bool ShouldPreferDefaultTranslation(string defaultValue, string existingValue)
        {
            if (string.IsNullOrEmpty(existingValue))
            {
                return true;
            }

            if (string.IsNullOrEmpty(defaultValue))
            {
                return false;
            }

            bool defaultHasNonAscii = defaultValue.Any(ch => ch > 127);
            if (!defaultHasNonAscii)
            {
                return false;
            }

            return LooksBrokenLocalization(existingValue);
        }

        private static bool LooksBrokenLocalization(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            return value.IndexOf("??", StringComparison.Ordinal) >= 0 || value.IndexOf("�", StringComparison.Ordinal) >= 0;
        }

        // Translation helpers
        private static void AddTranslationCandidates(HashSet<string> candidates, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            foreach (string candidate in value.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = candidate.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    candidates.Add(trimmed);
                }
            }
        }

        // JSON serialization helpers
        private static string SerializeDictionary(Dictionary<string, string> values)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("{");

            bool isFirst = true;
            foreach (KeyValuePair<string, string> pair in values)
            {
                if (!isFirst)
                {
                    builder.AppendLine(",");
                }

                isFirst = false;
                builder.Append("  \"");
                builder.Append(EscapeJsonString(pair.Key));
                builder.Append("\": ");
                builder.Append('"');
                builder.Append(EscapeJsonString(pair.Value ?? string.Empty));
                builder.Append('"');
            }

            builder.AppendLine();
            builder.Append('}');
            return builder.ToString();
        }

        private static Dictionary<string, string> ParseDictionary(string json)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(json))
            {
                return result;
            }

            int index = 0;
            SkipWhitespace(json, ref index);
            if (!TryRead(json, ref index, '{'))
            {
                return result;
            }

            while (true)
            {
                SkipWhitespace(json, ref index);
                if (TryRead(json, ref index, '}'))
                {
                    break;
                }

                string key = ParseQuotedString(json, ref index);
                if (key == null)
                {
                    return new Dictionary<string, string>(StringComparer.Ordinal);
                }

                SkipWhitespace(json, ref index);
                if (!TryRead(json, ref index, ':'))
                {
                    return new Dictionary<string, string>(StringComparer.Ordinal);
                }

                SkipWhitespace(json, ref index);
                string value = ParseQuotedString(json, ref index);
                if (value == null)
                {
                    return new Dictionary<string, string>(StringComparer.Ordinal);
                }

                result[key] = value;

                SkipWhitespace(json, ref index);
                if (TryRead(json, ref index, ','))
                {
                    continue;
                }

                if (TryRead(json, ref index, '}'))
                {
                    break;
                }

                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            return result;
        }

        // Lightweight JSON parser helpers
        private static void SkipWhitespace(string text, ref int index)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index]))
            {
                index++;
            }
        }

        private static bool TryRead(string text, ref int index, char expected)
        {
            if (index >= text.Length || text[index] != expected)
            {
                return false;
            }

            index++;
            return true;
        }

        private static string ParseQuotedString(string text, ref int index)
        {
            if (!TryRead(text, ref index, '"'))
            {
                return null;
            }

            StringBuilder builder = new StringBuilder();
            while (index < text.Length)
            {
                char current = text[index++];
                if (current == '"')
                {
                    return builder.ToString();
                }

                if (current != '\\')
                {
                    builder.Append(current);
                    continue;
                }

                if (index >= text.Length)
                {
                    return null;
                }

                char escaped = text[index++];
                switch (escaped)
                {
                    case '"':
                    case '\\':
                    case '/':
                        builder.Append(escaped);
                        break;
                    case 'b':
                        builder.Append('\b');
                        break;
                    case 'f':
                        builder.Append('\f');
                        break;
                    case 'n':
                        builder.Append('\n');
                        break;
                    case 'r':
                        builder.Append('\r');
                        break;
                    case 't':
                        builder.Append('\t');
                        break;
                    case 'u':
                        if (index + 4 > text.Length)
                        {
                            return null;
                        }

                        string hex = text.Substring(index, 4);
                        if (!ushort.TryParse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out ushort codeUnit))
                        {
                            return null;
                        }

                        builder.Append((char)codeUnit);
                        index += 4;
                        break;
                    default:
                        return null;
                }
            }

            return null;
        }

        private static string EscapeJsonString(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(value.Length + 8);
            foreach (char current in value)
            {
                switch (current)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\b':
                        builder.Append("\\b");
                        break;
                    case '\f':
                        builder.Append("\\f");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (current < 32)
                        {
                            builder.Append("\\u");
                            builder.Append(((int)current).ToString("x4"));
                        }
                        else
                        {
                            builder.Append(current);
                        }
                        break;
                }
            }

            return builder.ToString();
        }
    }
}
