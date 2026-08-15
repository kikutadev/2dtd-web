using System.Text.Json.Serialization;

namespace DungeonDefense.Contracts;

public sealed record CampaignResourceFile(
    [property: JsonPropertyName("stone")] int Stone,
    [property: JsonPropertyName("iron")] int Iron,
    [property: JsonPropertyName("soul")] int Soul,
    [property: JsonPropertyName("relic")] int Relic);

public sealed record CampaignSpeciesLevelFile(
    [property: JsonPropertyName("species_id")] string SpeciesId,
    [property: JsonPropertyName("level")] int Level);

public sealed record CampaignInvasionProgressFile(
    [property: JsonPropertyName("location_id")] string LocationId,
    [property: JsonPropertyName("unlocked_depth")] int UnlockedDepth,
    [property: JsonPropertyName("cleared_floor_ids")] IReadOnlyList<string> ClearedFloorIds);

public sealed record CampaignProductionAccumulatorFile(
    [property: JsonPropertyName("stone_units")] long StoneUnits,
    [property: JsonPropertyName("iron_units")] long IronUnits,
    [property: JsonPropertyName("soul_units")] long SoulUnits);

public sealed record CampaignInvasionRegenerationFile(
    [property: JsonPropertyName("location_id")] string LocationId,
    [property: JsonPropertyName("floor_id")] string FloorId,
    [property: JsonPropertyName("ready_at_utc")] DateTimeOffset ReadyAtUtc);

public sealed record CampaignRealtimeFile(
    [property: JsonPropertyName("last_observed_utc")] DateTimeOffset LastObservedUtc,
    [property: JsonPropertyName("effective_utc")] DateTimeOffset EffectiveUtc,
    [property: JsonPropertyName("production")] CampaignProductionAccumulatorFile Production,
    [property: JsonPropertyName("invasion_regeneration")] IReadOnlyList<CampaignInvasionRegenerationFile> InvasionRegeneration);

public sealed record CampaignActiveInvasionUnitFile(
    [property: JsonPropertyName("entity_id")] string EntityId,
    [property: JsonPropertyName("unit_id")] string UnitId,
    [property: JsonPropertyName("formation_index")] int FormationIndex,
    [property: JsonPropertyName("hp")] int Hp,
    [property: JsonPropertyName("shield")] int Shield,
    [property: JsonPropertyName("attack_cooldown_remaining")] int AttackCooldownRemaining,
    [property: JsonPropertyName("deployed")] bool Deployed,
    [property: JsonPropertyName("archetype")] string? Archetype = null,
    [property: JsonPropertyName("section_damage_percent")] int? SectionDamagePercent = null,
    [property: JsonPropertyName("incoming_damage_percent")] int? IncomingDamagePercent = null,
    [property: JsonPropertyName("attack_cooldown_percent")] int? AttackCooldownPercent = null);

public sealed record CampaignActiveInvasionSpellCooldownFile(
    [property: JsonPropertyName("spell_id")] string SpellId,
    [property: JsonPropertyName("remaining_ticks")] int RemainingTicks);

public sealed record CampaignActiveInvasionEventFile(
    [property: JsonPropertyName("tick")] int Tick,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("actor_id")] string ActorId,
    [property: JsonPropertyName("target_id")] string? TargetId,
    [property: JsonPropertyName("amount")] int Amount,
    [property: JsonPropertyName("detail")] string? Detail);

public sealed record CampaignActiveInvasionFile(
    [property: JsonPropertyName("content_version")] string ContentVersion,
    [property: JsonPropertyName("location_id")] string LocationId,
    [property: JsonPropertyName("floor_id")] string FloorId,
    [property: JsonPropertyName("seed")] int Seed,
    [property: JsonPropertyName("tick")] int Tick,
    [property: JsonPropertyName("mp")] int Mp,
    [property: JsonPropertyName("used_deployment_capacity")] int UsedDeploymentCapacity,
    [property: JsonPropertyName("section_index")] int SectionIndex,
    [property: JsonPropertyName("section_defense_hp")] int SectionDefenseHp,
    [property: JsonPropertyName("section_attack_cooldown")] int SectionAttackCooldown,
    [property: JsonPropertyName("retreat_remaining_ticks")] int? RetreatRemainingTicks,
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("secured_loot")] CampaignResourceFile SecuredLoot,
    [property: JsonPropertyName("units")] IReadOnlyList<CampaignActiveInvasionUnitFile> Units,
    [property: JsonPropertyName("spell_cooldowns")] IReadOnlyList<CampaignActiveInvasionSpellCooldownFile> SpellCooldowns,
    [property: JsonPropertyName("events")] IReadOnlyList<CampaignActiveInvasionEventFile> Events,
    [property: JsonPropertyName("is_first_clear_scenario")] bool? IsFirstClearScenario = null,
    [property: JsonPropertyName("is_resolved")] bool? IsResolved = null);

public sealed record CampaignClearedDungeonFile(
    [property: JsonPropertyName("archive_id")] string ArchiveId,
    [property: JsonPropertyName("region_id")] string RegionId,
    [property: JsonPropertyName("cleared_day")] int ClearedDay,
    [property: JsonPropertyName("final_assault_profile_id")] string FinalAssaultProfileId,
    [property: JsonPropertyName("dungeon")] PlayerDungeonSaveFile Dungeon);

public sealed record CampaignChallengeBestFile(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("best_score")] int BestScore);

public sealed record CampaignSaveFile(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("content_version")] string ContentVersion,
    [property: JsonPropertyName("day")] int Day,
    [property: JsonPropertyName("region_id")] string RegionId,
    [property: JsonPropertyName("resources")] CampaignResourceFile Resources,
    [property: JsonPropertyName("completed_research_ids")] IReadOnlyList<string> CompletedResearchIds,
    [property: JsonPropertyName("unlock_ids")] IReadOnlyList<string> UnlockIds,
    [property: JsonPropertyName("species_levels")] IReadOnlyList<CampaignSpeciesLevelFile> SpeciesLevels,
    [property: JsonPropertyName("invasion_progress")] IReadOnlyList<CampaignInvasionProgressFile> InvasionProgress,
    [property: JsonPropertyName("dungeon")] PlayerDungeonSaveFile Dungeon,
    [property: JsonPropertyName("realtime")] CampaignRealtimeFile? Realtime = null,
    [property: JsonPropertyName("active_invasion")] CampaignActiveInvasionFile? ActiveInvasion = null,
    [property: JsonPropertyName("cleared_dungeons")] IReadOnlyList<CampaignClearedDungeonFile>? ClearedDungeons = null,
    [property: JsonPropertyName("challenge_best_scores")] IReadOnlyList<CampaignChallengeBestFile>? ChallengeBestScores = null);
