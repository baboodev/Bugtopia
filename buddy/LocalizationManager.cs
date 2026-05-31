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
            { "pt-BR", "Português (Brasil)" }
        };

        // Built-in English fallback strings. These are also used as stable translation keys.
        private static readonly Dictionary<string, string> EnglishDefaults = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Main navigation / tabs
            { "Self", "Self" },
            { "Resource Gathering", "Resource Gathering" },
            { "Features", "Features" },
            { "Radar", "Radar" },
            { "Teleport", "Teleport" },
            { "Items Selector", "Items Selector" },
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
            { "Collect Types:", "Collect Types:" },
            { "  Mushrooms", "  Mushrooms" },
            { "  Berries / Bushes / Plants", "  Berries / Bushes / Plants" },
            { "  Event Resources", "  Event Resources" },
            { "Hide UI + Player (Client Side)", "Hide UI + Player (Client Side)" },
            { "Bird Vacuum (Client Side)", "Bird Vacuum (Client Side)" },
            { "Custom Camera FOV", "Custom Camera FOV" },
            { "DISABLE ALL", "DISABLE ALL" },
            { "REFRESH & SCAN", "REFRESH & SCAN" },
            { "Auto Repair", "Auto Repair" },
            { "Eat Selected Food", "Eat Selected Food" },
            { "Repair Teleport Backward", "Repair Teleport Backward" },
            { "❄️ Auto Snow Sculpture", "❄️ Auto Snow Sculpture" },
            { "Auto Click Icon", "Auto Click Icon" },
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
            { "PRESS ANY KEY FOR:", "PRESS ANY KEY FOR:" },
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
            { "Farm Rocks", "Farmear rocas" },
            { "Farm Ores", "Farmear menas" },
            { "Farm Trees", "Farmear árboles" },
            { "Farm Rare Trees", "Farmear árboles raros" },
            { "Farm Apple Trees", "Farmear manzanos" },
            { "Farm Mandarin Trees", "Farmear árboles mandarinos" },
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
            { "Noclip", "Noclip" },
            { "Noclip Speed: {0:F1}", "Noclip Speed: {0:F1}" },
            { "Noclip Boost: {0:F1}x", "Noclip Boost: {0:F1}x" },
            { "Anti AFK (Auto Click)", "Anti AFK (Auto Click)" },
            { "AFK Click Interval: {0:F0}s", "AFK Click Interval: {0:F0}s" },
            { "Noclip: WASD + Space/Ctrl\nShift = Speed Boost", "Noclip: WASD + Space/Ctrl\nShift = Speed Boost" },
            { "Building - Bypass Overlap", "Building - Bypass Overlap" },
            { "Bypass Overlap", "Bypass Overlap" },
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
        };

        // Built-in Spanish fallback strings.
        private static readonly Dictionary<string, string> SpanishDefaults = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Main navigation / tabs
            { "Self", "Personal" },
            { "Resource Gathering", "Recolección" },
            { "Features", "Funciones" },
            { "Radar", "Radar" },
            { "Teleport", "Teletransporte" },
            { "Items Selector", "Selector de objetos" },
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
            { "Bird Vacuum (Client Side)", "Vacío de aves (lado del cliente)" },
            { "Custom Camera FOV", "FOV de cámara personalizado" },
            { "DISABLE ALL", "DESACTIVAR TODO" },
            { "REFRESH & SCAN", "ACTUALIZAR Y ESCANEAR" },
            { "Auto Repair", "Auto reparar" },
            { "Eat Selected Food", "Comer comida seleccionada" },
            { "Repair Teleport Backward", "Teletransporte de reparación hacia atrás" },
            { "❄️ Auto Snow Sculpture", "❄️ Escultura de nieve automática" },
            { "Auto Click Icon", "Auto clic en icono" },
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
            { "PRESS ANY KEY FOR:", "PULSA UNA TECLA PARA:" },
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
            { "Bird Vacuum (Client Side)", "鸟类吸附（客户端）" },
            { "Custom Camera FOV", "自定义相机视野" },
            { "DISABLE ALL", "全部关闭" },
            { "REFRESH & SCAN", "刷新并扫描" },
            { "Auto Repair", "自动修理" },
            { "Eat Selected Food", "食用所选食物" },
            { "Repair Teleport Backward", "修理后传送返回" },
            { "❄️ Auto Snow Sculpture", "❄️ 自动雪雕" },
            { "Auto Click Icon", "自动点击图标" },
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
            { "Credits: • evermoreee12 for Bypass Overlap", "鸣谢：• evermoreee12 提供绕过重叠" },
            // Main navigation / tabs
            { "Self", "个人" },
            { "Resource Gathering", "资源采集" },
            { "Features", "功能" },
            { "Radar", "雷达" },
            { "Teleport", "传送" },
            { "Items Selector", "物品选择器" },
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
            { "Auto Buy: Teleport -> Buy -> Return", "自动购买：传送 -> 购买 -> 返回" },
            { "State: {0}", "状态：{0}" },
            { "Current Ingredient: {0}", "当前食材：{0}" },
            { "Max per ingredient: {0}", "每种食材最大数量：{0}" },
            { "Home", "主页" },
            { "Animal Care", "动物照料" },
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
            { "PRESS ANY KEY FOR:", "请按下任意按键：" },
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
        };
        private static readonly Dictionary<string, string> PortugueseDefaults = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "Self", "Eu" },
            { "Resource Gathering", "Coleta de Recursos" },
            { "Features", "Recursos" },
            { "Radar", "Radar" },
            { "Teleport", "Teleporte" },
            { "Items Selector", "Selecionador de Itens" },
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
            { "Bird Vacuum (Client Side)", "Aspirador de Pássaros (Cliente)" },
            { "Custom Camera FOV", "FOV da Câmera Customizado" },
            { "DISABLE ALL", "DESATIVAR TUDO" },
            { "REFRESH & SCAN", "ATUALIZAR & ESCANEAR" },
            { "Auto Repair", "Reparo Automático" },
            { "Eat Selected Food", "Comer Comida Selecionada" },
            { "Repair Teleport Backward", "Teleporte de Retorno após Reparo" },
            { "❄️ Auto Snow Sculpture", "❄️ Escultura de Neve Automática" },
            { "Auto Click Icon", "Ícone de Clique Automático" },
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
            { "PRESS ANY KEY FOR:", "PRESSIONE QUALQUER TECLA PARA:" },
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
            if (string.IsNullOrEmpty(text) || string.Equals(CurrentLanguage, "en", StringComparison.OrdinalIgnoreCase))
            {
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
                Dictionary<string, string> ordered = CreateOrderedDictionary(defaults, existing);
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

            return GetDefaultValue(EnglishDefaults, key);
        }

        private static Dictionary<string, string> CreateOrderedDictionary(Dictionary<string, string> defaults, Dictionary<string, string> existing)
        {
            Dictionary<string, string> ordered = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (KeyValuePair<string, string> pair in defaults)
            {
                if (existing != null && existing.TryGetValue(pair.Key, out string existingValue))
                {
                    ordered[pair.Key] = ShouldPreferDefaultTranslation(pair.Value, existingValue) ? pair.Value : (existingValue ?? string.Empty);
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
