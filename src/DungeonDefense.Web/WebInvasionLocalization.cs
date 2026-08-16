using DungeonDefense.Core;

namespace DungeonDefense.Web;

/// <summary>Web-host text for the standalone invasion demo, aligned with production UiLocalization terminology.</summary>
internal static class WebInvasionLocalization
{
    private static readonly Dictionary<string, (string En, string Ja)> Locations = new(StringComparer.Ordinal)
    {
        ["location.black_iron_mine"] = ("Black Iron Mine", "黒鉄坑道"),
        ["location.sunken_chapel"] = ("Sunken Chapel", "沈んだ礼拝堂"),
        ["location.ossuary_foundry"] = ("Ossuary Foundry", "納骨鋳造所"),
        ["location.veiled_reliquary"] = ("Veiled Reliquary", "帳の聖遺物庫"),
    };

    private static readonly Dictionary<string, (string En, string Ja)> Threats = new(StringComparer.Ordinal)
    {
        ["armored"] = ("Armored", "重装"), ["core"] = ("Core", "コア"), ["crossfire"] = ("Crossfire", "十字射撃"),
        ["elite"] = ("Elite", "精鋭"), ["holy"] = ("Holy", "聖属性"), ["narrow"] = ("Narrow Route", "狭路"),
        ["ranged"] = ("Ranged", "遠隔"), ["scout"] = ("Scout", "斥候"), ["storehouse"] = ("Storehouse", "資源庫"),
        ["support"] = ("Support", "支援"), ["foundry"] = ("Foundry", "鋳造所"), ["soul-smoke"] = ("Soul Smoke", "魂煙"),
        ["forge-guard"] = ("Forge Guard", "炉衛"), ["soul-core"] = ("Soul Core", "魂核"), ["ancient"] = ("Ancient", "古代"),
        ["curse"] = ("Cursed", "呪い"), ["relic-vault"] = ("Relic Vault", "聖遺物庫"), ["warded"] = ("Warded", "結界"),
        ["relic-core"] = ("Relic Core", "遺物核"),
    };

    private static readonly Dictionary<string, (string En, string Ja)> Texts = new(StringComparer.Ordinal)
    {
        ["title"] = ("Invasion", "侵攻"), ["back"] = ("Defense", "防衛へ"),
        ["loading"] = ("Loading invasion content…", "侵攻データを読み込み中…"),
        ["briefing"] = ("Briefing / Formation", "偵察 / 編成"), ["battle"] = ("Invasion Battle", "侵攻戦"),
        ["briefing_desc"] = ("Major routes, threats and mission goals are known before deployment.", "主要経路・脅威・攻略目標は出撃前に確認できます。"),
        ["location"] = ("Target", "侵攻先"), ["floor"] = ("Floor", "階層"), ["objective"] = ("Objective", "攻略目標"),
        ["sections"] = ("Sections", "区画"), ["capacity"] = ("Deployment", "出撃枠"), ["status"] = ("Status", "状況"),
        ["threats"] = ("Threats", "脅威"), ["route"] = ("Known Defense Sections", "確認済み防衛区画"),
        ["visible_loot"] = ("Visible section loot", "確認済み戦利品"), ["clear_reward"] = ("First-clear reward", "初回攻略報酬"),
        ["formation"] = ("Formation", "編成"), ["formation_hint"] = ("The canonical demo formation mirrors the production campaign fixture.", "初期編成はproductionのCampaign成功fixtureと同じです。"),
        ["start"] = ("Begin Invasion", "侵攻開始"), ["deploy_all"] = ("Deploy All", "全軍投入"),
        ["support"] = ("Support", "支援"), ["mend"] = ("Mend 25 MP", "治癒 25 MP"), ["ward"] = ("Ward 35 MP", "防護 35 MP"),
        ["retreat"] = ("Retreat", "撤退"), ["resume"] = ("Resume", "再生"), ["pause"] = ("Pause", "一時停止"),
        ["reserve"] = ("Reserve", "予備"), ["active"] = ("Active", "戦闘中"), ["defeated"] = ("Defeated", "戦闘不能"),
        ["section_hp"] = ("Defense HP", "防衛HP"), ["secured"] = ("Secured Loot", "確保済み戦利品"),
        ["recent"] = ("Recent Events", "直近の戦況"), ["events_empty"] = ("Deploy units to begin attacking the current section.", "部隊を投入すると現在区画への攻撃が始まります。"),
        ["success"] = ("Invasion Success", "侵攻成功"), ["wiped"] = ("Party Wiped", "部隊壊滅"), ["retreated"] = ("Retreated", "撤退完了"),
        ["result"] = ("Result", "結果"), ["again"] = ("Back to Briefing", "偵察へ戻る"),
        ["running"] = ("In progress", "侵攻中"), ["ready"] = ("Ready", "出撃準備完了"),
        ["section_cleared"] = ("Section cleared", "区画突破"), ["unit_deployed"] = ("Unit deployed", "部隊投入"),
        ["unit_attack"] = ("Unit attack", "攻撃"), ["unit_damaged"] = ("Unit damaged", "被ダメージ"), ["unit_defeated"] = ("Unit defeated", "戦闘不能"),
        ["spell_cast"] = ("Support spell", "支援魔法"), ["loot_secured"] = ("Loot secured", "戦利品確保"),
        ["objective_complete"] = ("Objective completed", "目標達成"), ["retreat_requested"] = ("Retreat requested", "撤退開始"),
        ["locations"] = ("Targets", "侵攻先"),
        ["locations_desc"] = ("Choose a target before scouting its floors.", "まず侵攻先を選び、攻略する階層を偵察します。"),
        ["available_floors"] = ("Available", "出撃可能"),
        ["unlocked"] = ("Unlocked", "解放"),
        ["inspect"] = ("Inspect", "確認"),
        ["scouting"] = ("Scouting", "偵察"),
        ["scout_desc"] = ("Compare objective, threats and rewards before forming the party.", "攻略目標・脅威・報酬を比較してから部隊を編成します。"),
        ["first_clear"] = ("First clear", "初回攻略"),
        ["repeat"] = ("Repeat", "再攻略"),
        ["form_party"] = ("Form party", "部隊編成"),
        ["locations_side_hint"] = ("Each location has its own floors and threat profile. Inspect one target at a time.", "侵攻先ごとに階層と脅威構成が異なります。まず一つの侵攻先を選んで確認します。"),
        ["scout_side_hint"] = ("Choose the floor before formation so the roster answers the threats you actually saw.", "先に階層の脅威と報酬を確認し、その情報をもとに部隊を組みます。"),
        ["force"] = ("Survivors", "生存戦力"),
        ["progress"] = ("Reached", "到達区画"),
        ["result_next"] = ("Next", "次の行動"),
        ["result_hint"] = ("Return to scouting and change the floor or formation before the next assault.", "偵察へ戻り、次の侵攻前に階層や編成を見直せます。"),
        ["section_reached"] = ("Reached", "到達"),
        ["section_unreached"] = ("Unreached", "未到達"),
        ["invasion.result.lesson.success"] = ("The assault broke through. Compare survivors and secured loot before choosing the next target.", "防衛線を突破しました。生存戦力と確保した戦利品を確認して次の侵攻先を選びます。"),
        ["invasion.result.lesson.retreated"] = ("The force disengaged with secured loot. Re-form the party around the section that stopped the advance.", "確保済み戦利品を持って離脱しました。進軍が止まった区画に合わせて編成を見直せます。"),
        ["invasion.result.lesson.wiped"] = ("The force was wiped before the objective. Review the reached section and party composition before retrying.", "目標達成前に部隊が壊滅しました。到達区画と編成を見直して再挑戦します。"),
        ["footer"] = ("This section-based battle runs the production InvasionSimulation directly; it does not reuse or imitate DefenseSimulation.", "この区画制戦闘はproduction InvasionSimulationを直接実行し、DefenseSimulationへの擬似変換は行っていません。"),
    };

    public static string Text(string locale, string key)
        => Texts.TryGetValue(key, out var value) ? Pick(locale, value) : key;

    public static string LocationName(string locale, string id)
        => Locations.TryGetValue(id, out var value) ? Pick(locale, value) : id;

    public static string ThreatName(string locale, string tag)
        => Threats.TryGetValue(tag, out var value) ? Pick(locale, value) : tag;

    public static string ObjectiveName(string locale, InvasionObjectiveKind objective) => objective switch
    {
        InvasionObjectiveKind.Raid => IsJa(locale) ? "資源奪取" : "Raid",
        InvasionObjectiveKind.Eliminate => IsJa(locale) ? "守備隊殲滅" : "Eliminate",
        InvasionObjectiveKind.CoreBreak => IsJa(locale) ? "コア破壊" : "Core Break",
        _ => objective.ToString(),
    };

    private static string Pick(string locale, (string En, string Ja) value) => IsJa(locale) ? value.Ja : value.En;
    private static bool IsJa(string locale) => locale.StartsWith(WebDemoLocalization.Japanese, StringComparison.OrdinalIgnoreCase);
}
