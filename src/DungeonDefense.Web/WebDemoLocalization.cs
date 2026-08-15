namespace DungeonDefense.Web;

/// <summary>
/// Localization owned by the thin Web demo host. Gameplay/entity terminology intentionally matches
/// the production Godot UiLocalization strings without introducing a Godot dependency into Web.
/// </summary>
internal static class WebDemoLocalization
{
    public const string Japanese = "ja";
    public const string English = "en";

    private static readonly IReadOnlyDictionary<string, string> En = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["loading"] = "Loading dungeon…",
        ["title"] = "Your Dungeon",
        ["guide"] = "1. Choose a defense · 2. Click a highlighted cell · 3. Start the defense and watch the real simulation",
        ["invasion_demo"] = "Invasion Demo",
        ["phase"] = "Phase",
        ["build_phase"] = "Dungeon Build",
        ["capacity"] = "Capacity",
        ["route"] = "Route",
        ["defense"] = "Defense",
        ["ready"] = "Ready",
        ["needs_fix"] = "Needs Fix",
        ["placements"] = "Placements",
        ["wave"] = "Wave",
        ["core"] = "Core",
        ["tick"] = "Tick",
        ["floor_1"] = "Floor B1",
        ["build_heading"] = "Place defenses, then start the assault.",
        ["battle_heading"] = "The edited board is passed directly into the real battle.",
        ["remove_hint"] = "Choose a placed object on the board to remove it.",
        ["place_hint"] = "Place {0}. Highlighted cells are valid under production placement rules.",
        ["place_section"] = "Build",
        ["edit_section"] = "Edit",
        ["rotate_room"] = "Rotate room 90°",
        ["remove"] = "Remove placement",
        ["reset_board"] = "Reset board",
        ["summary"] = "Build Summary",
        ["first_contact"] = "First contact",
        ["none"] = "None",
        ["trap_contacts"] = "Trap contacts",
        ["guard_coverage"] = "Guard coverage",
        ["facility_coverage"] = "Facility coverage",
        ["start_defense"] = "Start Defense",
        ["return_build"] = "Back to Build",
        ["magic"] = "Magic",
        ["freeze"] = "Freeze 20 MP",
        ["push"] = "Push 15 MP",
        ["recent_events"] = "Recent Battle Events",
        ["events_empty"] = "Start the defense to receive combat events from Core.",
        ["footer"] = "Build rules, placement validation, and combat run on the production Core/Application; combat motion is rendered from the shared Presentation Visual State.",
        ["placed"] = "Placed {0}.",
        ["removed"] = "Removed {0}.",
        ["nothing_remove"] = "There is no removable placement on this cell.",
        ["board_reset"] = "The board was reset to its initial state.",
        ["returned_build"] = "The same build is ready for editing again; battle results do not rewrite it.",
        ["run_start"] = "Start Defense",
        ["run_pause"] = "Pause",
        ["run_resume"] = "Resume",
        ["run_again"] = "Run Same Build Again",
        ["outcome_success"] = "Defense Success",
        ["outcome_failure"] = "Defense Failure",
        ["outcome_running"] = "Defending",
        ["outcome_paused"] = "Paused",
        ["kind_room"] = "Room",
        ["kind_trap"] = "Trap",
        ["kind_guard"] = "Guard",
        ["kind_facility"] = "Facility",
        ["event_wave"] = "New wave",
        ["event_spawn"] = "Invader spawned",
        ["event_trap"] = "Trap triggered",
        ["event_attack"] = "Attack",
        ["event_heal"] = "Heal",
        ["event_death"] = "Unit defeated",
        ["event_core"] = "Core took {0} damage",
        ["event_success"] = "Defense success",
        ["event_failure"] = "Defense failure",
        ["event_spell"] = "Spell cast",
    };

    private static readonly IReadOnlyDictionary<string, string> Ja = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["loading"] = "ダンジョンを読み込み中…",
        ["title"] = "あなたのダンジョン",
        ["guide"] = "1. 配置物を選ぶ · 2. 明るいセルを選ぶ · 3. 防衛開始で実Simulationをそのまま遊ぶ",
        ["invasion_demo"] = "侵攻デモ",
        ["phase"] = "Phase",
        ["build_phase"] = "ダンジョン構築",
        ["capacity"] = "Capacity",
        ["route"] = "Route",
        ["defense"] = "防衛",
        ["ready"] = "開始可能",
        ["needs_fix"] = "要修正",
        ["placements"] = "配置",
        ["wave"] = "Wave",
        ["core"] = "Core",
        ["tick"] = "Tick",
        ["floor_1"] = "地下1階",
        ["build_heading"] = "配置してから防衛を開始",
        ["battle_heading"] = "構築した盤面をそのまま実戦闘へ",
        ["remove_hint"] = "削除したい配置物を盤面から選択してください。",
        ["place_hint"] = "{0} を配置します。明るいセルがproduction rule上の配置可能位置です。",
        ["place_section"] = "配置する",
        ["edit_section"] = "編集",
        ["rotate_room"] = "部屋を90°回転",
        ["remove"] = "配置を削除",
        ["reset_board"] = "盤面をリセット",
        ["summary"] = "構成サマリー",
        ["first_contact"] = "最初の接敵",
        ["none"] = "なし",
        ["trap_contacts"] = "罠接触",
        ["guard_coverage"] = "Guard coverage",
        ["facility_coverage"] = "Facility coverage",
        ["start_defense"] = "この構成で防衛開始",
        ["return_build"] = "構築に戻る",
        ["magic"] = "魔法",
        ["freeze"] = "凍結 20 MP",
        ["push"] = "押し戻し 15 MP",
        ["recent_events"] = "直近の戦況",
        ["events_empty"] = "防衛を開始するとCoreから戦闘イベントが流れます。",
        ["footer"] = "構築・配置判定・戦闘はproduction Core/Applicationをそのまま実行し、戦闘モーションはshared Presentation Visual Stateを描画しています。",
        ["placed"] = "{0}を配置しました。",
        ["removed"] = "{0}を削除しました。",
        ["nothing_remove"] = "このセルには削除できる配置物がありません。",
        ["board_reset"] = "盤面を初期状態へ戻しました。",
        ["returned_build"] = "戦闘結果を反映せず、同じ構成を再編集できます。",
        ["run_start"] = "防衛開始",
        ["run_pause"] = "一時停止",
        ["run_resume"] = "再開",
        ["run_again"] = "同じ構成でもう一度",
        ["outcome_success"] = "迎撃成功",
        ["outcome_failure"] = "迎撃失敗",
        ["outcome_running"] = "防衛中",
        ["outcome_paused"] = "一時停止",
        ["kind_room"] = "部屋",
        ["kind_trap"] = "罠",
        ["kind_guard"] = "守備兵",
        ["kind_facility"] = "設備",
        ["event_wave"] = "新しいWave",
        ["event_spawn"] = "侵入者が出現",
        ["event_trap"] = "罠が発動",
        ["event_attack"] = "攻撃",
        ["event_heal"] = "回復",
        ["event_death"] = "ユニット撃破",
        ["event_core"] = "コアに {0} ダメージ",
        ["event_success"] = "防衛成功",
        ["event_failure"] = "防衛失敗",
        ["event_spell"] = "魔法発動",
    };

    private static readonly Dictionary<string, (string En, string Ja)> BuildNames =
        new Dictionary<string, (string En, string Ja)>(StringComparer.Ordinal)
        {
            ["room.guard_2x2"] = ("Guard Room", "守備室"),
            ["room.poison_2x2"] = ("Poison Chamber", "毒室"),
            ["room.execution_2x2"] = ("Execution Chamber", "処刑室"),
            ["room.mana_2x2"] = ("Mana Chamber", "魔力室"),
            ["trap.spike"] = ("Spike Trap", "棘罠"),
            ["trap.poison"] = ("Poison Trap", "毒罠"),
            ["monster.skeleton_warrior"] = ("Skeleton Warrior", "スケルトン戦士"),
            ["monster.skeleton_archer"] = ("Skeleton Archer", "スケルトン弓兵"),
            ["facility.arrow_slit"] = ("Arrow Slit", "矢狭間"),
            ["facility.magic_eye"] = ("Magic Eye", "魔眼"),
        };

    public static string Text(string locale, string key)
    {
        var source = IsJapanese(locale) ? Ja : En;
        return source.TryGetValue(key, out var value) ? value : key;
    }

    public static string Format(string locale, string key, params object[] args)
        => string.Format(System.Globalization.CultureInfo.InvariantCulture, Text(locale, key), args);

    public static string BuildName(string locale, string definitionId)
        => BuildNames.TryGetValue(definitionId, out var names) ? (IsJapanese(locale) ? names.Ja : names.En) : definitionId;

    private static bool IsJapanese(string locale)
        => locale.StartsWith(Japanese, StringComparison.OrdinalIgnoreCase);
}
